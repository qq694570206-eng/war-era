#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
check_art_import.py — 素材接入的【离线静态校验】（不需要 Unity）

作者：主程序 寇豆码
依据：docs/asset-integration-phase-c-v1.md §2.2

【这套校验在防什么】
"跑起来没报错"不等于对。素材接入里有三类失败，全都是不抛异常、不红字、
Console 干净、画面也"看起来有点像能用"的：
  A. 参数写了两遍只有一处对 —— PPU / pivot / spriteMode 同时存在四处：
       ① tools/key_assets.py 的 ASSETS 表        —— 人写的原始值
       ② tools/keyed_manifest.json               —— 脚本生成的清单
       ③ PhaseBBootstrap.cs 的 SpriteSpecs 数组   —— Unity 侧真正写 meta 的值
       ④ Art/Keyed/*.png.meta                    —— 落盘后的实际结果
     实测事故：explosion 在 ③ 是 10.67、在 ② 是 2.67，差 4 倍 → 爆炸小成 1/4，
     且 Console 一句报错都没有。
  B. shader 编译失败但函数照常返回 —— Shader.Find 只要文件在就成功，
     即使编译失败。实测：函数重定义 SpriteFrag → Shader error 一行，
     材质创建成功但没 pass，闪白静默退回顶点色（"还能看见一点"的假象）。
  C. 材质引用断链 —— prefab 里写的是 guid，磁盘 .mat 的 guid 不一致时
     SpriteRenderer 回落默认材质，Inspector 只显示 None，运行时毫无提示。

【用法】
    python3 tools/check_art_import.py            # 全量校验
    python3 tools/check_art_import.py --quiet    # 只打印结论行

【退出码】
    0 = 全部通过
    1 = 参数不一致 / shader 有编译隐患 / 材质引用断链（是 bug，必须改）
    2 = 参数一致但 meta 未导入（不是 bug，只是还没跑编辑器菜单）
"""
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PY_ASSETS = os.path.join(ROOT, "tools", "key_assets.py")
MANIFEST = os.path.join(ROOT, "tools", "keyed_manifest.json")
BOOTSTRAP = os.path.join(ROOT, "unity-project", "Assets", "_Project",
                         "Scripts", "Editor", "PhaseBBootstrap.cs")
SHADER = os.path.join(ROOT, "unity-project", "Assets", "_Project", "Shaders",
                      "SpriteFlash.shader")
PREFAB_DIR = os.path.join(ROOT, "unity-project", "Assets", "_Project", "Prefabs")

KEYED_DIR = os.path.join(ROOT, "unity-project", "Assets", "_Project", "Art", "Keyed")

# Unity 枚举值（写进 .meta 的整数值，别改）
TEXTURE_TYPE_SPRITE = 8
SPRITE_MODE_SINGLE = 1
SPRITE_MODE_MULTIPLE = 2
ALIGNMENT_CUSTOM = 9
MESH_FULLRECT = 0


# ----------------------------------------------------------------- ① key_assets.py
def read_py_assets():
    """解析 ASSETS 表。返回 {name: dict(ppu, pivot=(x,y), grid=(cols,rows)|None)}。

    用正则而不是 import：import 会执行整个脚本（含写盘），校验器不能有副作用。

    两个字段都可表示"要切开"，语义不同但校验只看网格：
      cells=        —— 逐格独立抠图（explosion，每帧自己的边缘环）
      import_cells= —— 只切导入，不逐格抠图（resource_icons，背景一致）
    两者都会有 stats['grid']，因此这里统一读成 grid。
    """
    src = open(PY_ASSETS, encoding="utf-8").read()
    start = src.index("ASSETS = [")
    end = src.index("\n]", start)
    block = src[start:end]

    # 【必须先剥注释再分块】否则上一条目后面挂的说明性注释里出现
    # "import_cells=(2,2)" 这样的字样，会被算到下一条目头上。
    # 实测踩过：resource_icons 的注释把 minimap_frame 误判成 Multiple。
    # 剥离规则：整行以 # 开头的删掉；行内 # 之后的内容删掉（本表里没有含 # 的字符串）。
    lines = []
    for ln in block.splitlines():
        code = ln.split("#", 1)[0]
        if code.strip():
            lines.append(code)
    block = "\n".join(lines)

    out = {}
    # 每条以 dict( 开头，中间可能跨行（explosion / resource_icons 都是两行）
    for chunk in block.split("dict(")[1:]:
        m_name = re.search(r'name="([^"]+)"', chunk)
        m_ppu = re.search(r'ppu=([\d.]+)', chunk)
        m_piv = re.search(r'pivot=\(\s*([\d.]+)\s*,\s*([\d.]+)\s*\)', chunk)
        # 逐格抠图的 cells= 与仅切导入的 import_cells= 都可能是网格
        m_grid = (re.search(r'import_cells=\(\s*(\d+)\s*,\s*(\d+)\s*\)', chunk)
                  or re.search(r'\bcells=\(\s*(\d+)\s*,\s*(\d+)\s*\)', chunk))
        if not (m_name and m_ppu and m_piv):
            continue
        out[m_name.group(1)] = dict(
            ppu=float(m_ppu.group(1)),
            pivot=(float(m_piv.group(1)), float(m_piv.group(2))),
            grid=(int(m_grid.group(1)), int(m_grid.group(2))) if m_grid else None,
        )
    return out


# ----------------------------------------------------------------- ② manifest
def read_manifest():
    data = json.load(open(MANIFEST, encoding="utf-8"))
    out = {}
    for it in data["items"]:
        out[it["name"]] = dict(
            ppu=float(it["spritePixelsPerUnit"]),
            pivot=(float(it["pivot"][0]), float(it["pivot"][1])),
            multiple=(it["spriteMode"] == "Multiple"),
            grid=tuple(it["grid"]) if it.get("grid") else None,
            cell=tuple(it["cellSize"]) if it.get("cellSize") else None,
        )
    return out


# ----------------------------------------------------------------- ③ C#
def read_csharp():
    """解析 SpriteSpecs 数组里的 new SpriteSpec(...) 调用。"""
    src = open(BOOTSTRAP, encoding="utf-8").read()
    pat = re.compile(
        r'new\s+SpriteSpec\(\s*"([^"]+)"\s*,\s*([\d.]+)f\s*,\s*([\d.]+)f\s*,\s*([\d.]+)f'
        r'(?:\s*,\s*(true|false)\s*(?:,\s*(\d+)\s*,\s*(\d+)\s*)?)?\s*\)'
    )
    out = {}
    for m in pat.finditer(src):
        name, ppu, px, py, mul, cw, ch = m.groups()
        out[name] = dict(
            ppu=float(ppu),
            pivot=(float(px), float(py)),
            multiple=(mul == "true"),
            cell=(int(cw), int(ch)) if cw else None,
        )
    return out


# ----------------------------------------------------------------- ④ .meta
def read_meta(name):
    """读单张 .meta 的关键字段。文件不存在返回 None。

    【maxTextureSize 必须读 platformSettings 里 DefaultTexturePlatform 那一项】
    顶层也有一个 maxTextureSize: 2048，那是旧版遗留字段，Unity 2022 不再按它生效。
    真正决定导入尺寸的是 platformSettings[buildTarget=DefaultTexturePlatform]。
    实测：tank.meta 顶层 2048、DefaultTexturePlatform 256、Standalone/Server 各 2048，
    而 overridden=0 表示后两者不生效 —— 只看顶层会误报"全部 20 张超标"。
    """
    path = os.path.join(KEYED_DIR, name + ".png.meta")
    if not os.path.isfile(path):
        return None
    txt = open(path, encoding="utf-8").read()

    def top1(key, cast=int):
        m = re.search(r"^  %s:\s*(\S+)\s*$" % key, txt, re.M)
        return cast(m.group(1)) if m else None

    m_piv = re.search(r"^  spritePivot:\s*\{x:\s*([-\d.]+),\s*y:\s*([-\d.]+)\}", txt, re.M)
    pivot = (float(m_piv.group(1)), float(m_piv.group(2))) if m_piv else None

    # 默认平台块的 maxTextureSize：从 buildTarget: DefaultTexturePlatform 起，到下一个 buildTarget 止
    m_def = re.search(
        r"buildTarget: DefaultTexturePlatform(.*?)(?=\n\s*-\s*serializedVersion|\Z)",
        txt, re.S)
    max_size = None
    if m_def:
        m_ms = re.search(r"maxTextureSize:\s*(\d+)", m_def.group(1))
        if m_ms:
            max_size = int(m_ms.group(1))

    # 切片数量：spriteSheet.sprites 下的条目名，形如 explosion_00
    sub = len(re.findall(r"^      name: %s_\d+" % re.escape(name), txt, re.M))

    return dict(
        texture_type=top1("textureType"),
        sprite_mode=top1("spriteMode"),
        alignment=top1("alignment"),
        ppu=top1("spritePixelsToUnits", float),
        pivot=pivot,
        alpha_is_transparency=top1("alphaIsTransparency"),
        mesh_type=top1("spriteMeshType"),
        max_size=max_size,
        sub_sprites=sub,
        path=path,
    )


# ----------------------------------------------------------------- 工具
def png_size(path):
    """读 PNG 的 IHDR 取 (w,h)，不依赖 PIL（校验器要能在干净环境跑）。"""
    with open(path, "rb") as f:
        head = f.read(24)
    if head[:8] != b"\x89PNG\r\n\x1a\n":
        return None
    return int.from_bytes(head[16:20], "big"), int.from_bytes(head[20:24], "big")


def approx(a, b, tol=1e-4):
    if a is None or b is None:
        return False
    return abs(a - b) <= tol


# ----------------------------------------------------------------- ⑤ shader 编译隐患
# UnitySprites.cginc 导出的符号。自定义 shader 里【重复定义】其中任何一个，
# 都会得到 "redefinition of 'XXX'" 的编译错误，而 Shader.Find 依然返回非 null。
CGINC_EXPORTS = ("SpriteVert", "SpriteFrag", "SampleSpriteTexture",
                 "UnityFlipSprite", "v2f", "appdata_t")


def check_shader():
    """静态检查 SpriteFlash.shader 是否会踩 CGINC 的重定义坑。

    这条检查存在的唯一原因：实测发生过
        Shader error in 'WarEra/SpriteFlash': redefinition of 'SpriteFrag' at line 66 (on metal)
    表现是闪白静默退回顶点色 —— 画面"看起来还有一点白"，很难联想到 shader 编译失败。
    离线静态检查能在不开 Unity 的情况下提前抓到。
    """
    errs = []
    if not os.path.isfile(SHADER):
        return ["SpriteFlash.shader 不存在：" + SHADER]

    src = open(SHADER, encoding="utf-8").read()

    # 去掉块注释再查，避免把注释里的说明文字当成代码
    code = re.sub(r"/\*.*?\*/", "", src, flags=re.S)
    code = re.sub(r"//[^\n]*", "", code)

    # ① 是否 include 了 UnitySprites.cginc（include 了才有重定义风险）
    uses_cginc = "UnitySprites.cginc" in code
    if uses_cginc:
        for sym in CGINC_EXPORTS:
            # 匹配"定义"而不是"调用"：返回类型 + 符号名 + 左括号
            if re.search(r"\b\w+\s+%s\s*\(" % re.escape(sym), code):
                # 排除 struct 声明之外的情况：v2f/appdata_t 在 cginc 里是 struct
                if sym in ("v2f", "appdata_t"):
                    continue
                errs.append(f"shader 里重新定义了 '{sym}'，而 UnitySprites.cginc 已导出它 "
                            f"→ 会报 redefinition 编译错误（闪白静默失效）。改名即可。")

    # ② #pragma fragment 指向的函数必须真的在当前文件里定义
    m_frag = re.search(r"#pragma\s+fragment\s+(\w+)", code)
    if m_frag:
        frag = m_frag.group(1)
        if frag not in ("SpriteFrag",) and not re.search(r"\b\w+\s+%s\s*\(" % re.escape(frag), code):
            errs.append(f"#pragma fragment {frag} 指向的函数在文件里找不到定义。")
    else:
        errs.append("shader 里没有 #pragma fragment。")

    # ③ _FlashAmount 必须带 [PerRendererData]，否则 MaterialPropertyBlock 静默失效
    if "_FlashAmount" in code:
        if not re.search(r"\[PerRendererData\]\s*_FlashAmount", code):
            errs.append("_FlashAmount 缺少 [PerRendererData] 标记 → "
                        "MaterialPropertyBlock 不生效，闪白静默失效。")

    return errs


# ----------------------------------------------------------------- ⑥ 材质引用
# 【先说清楚这个检查能做什么、不能做什么】
# Tuanjie 的 .meta 里 guid 是【加密串】（base64 形态、41 字节），
# 而 prefab / .mat 内部引用用的是普通 32 位十六进制 guid，两者无法离线换算。
# 实测：SpriteFlash.shader.meta 的 guid 解 base64 后是 0d2c4ce6…，
# 而编辑器日志里同一个 shader 的 guid 是 2fccf933…，md5/sha1 也都不对应
# → 存在真正的加密（有密钥），离线不可逆。
#
# 所以"prefab 引用的材质 guid 是否等于磁盘 .mat 的 guid"这件事【离线证不了】，
# 只能在编辑器里用 AssetDatabase 反查（见 PhaseBBootstrap.VerifyBuild ①）。
# 硬要离线比，只会产出 100% 的假阳性 —— 第一版就是这种错，两处全红但都是误报。
#
# 那这个检查还剩什么可证伪的？一条，而且是真会发生的：
#   prefab 的 SpriteRenderer 材质指向【Unity 内置默认材质】
#   （guid 形如 0000000000000000f000000000000000）。
# 实测 HEAD 版本的 UnitPrefab.prefab 正是这样 —— 说明"建了材质却没挂上"，
# 此时闪白、染色全部无效，Console 一个字都不报。这条能离线抓。
BUILTIN_DEFAULT_MATERIAL_GUIDS = {
    "0000000000000000f000000000000000",   # Default-Material / Sprites-Default
    "0000000000000000e000000000000000",
    "0000000000000000d000000000000000",
}


def check_material_refs():
    """检查 prefab 的 SpriteRenderer 材质不是 Unity 内置默认材质。

    能证伪的：prefab 挂着内置默认材质 → 闪白/染色无效（真会发生，HEAD 版就是这样）。
    不能证伪的：材质 guid 的身份 → 需要编辑器（见 docstring 里的说明）。
    """
    errs = []
    if not os.path.isdir(PREFAB_DIR):
        return errs

    mat_path = os.path.join(ROOT, "unity-project", "Assets", "_Project", "Shaders",
                            "SpriteFlash.mat")
    has_mat = os.path.isfile(mat_path)

    for fn in sorted(os.listdir(PREFAB_DIR)):
        if not fn.endswith(".prefab"):
            continue
        path = os.path.join(PREFAB_DIR, fn)
        txt = open(path, encoding="utf-8", errors="ignore").read()

        for m in re.finditer(r"m_Materials:\s*\n\s*-\s*\{fileID:\s*(-?\d+),\s*guid:\s*(\w+),", txt):
            file_id, guid = m.group(1), m.group(2)

            # 只有"单位/建筑"prefab 需要自定义材质；特效/占位 prefab 用默认材质是正常的
            needs_flash = fn in ("UnitPrefab.prefab", "BuildingPrefab.prefab")
            if not needs_flash:
                continue

            if guid in BUILTIN_DEFAULT_MATERIAL_GUIDS or set(guid) == {"0"}:
                errs.append(
                    f"{fn} 的 SpriteRenderer 指向 Unity 内置默认材质（guid={guid}）→ "
                    f"闪白/受击反馈全部无效，且 Console 不报错。"
                    f"应指向 Assets/_Project/Shaders/SpriteFlash.mat。")
            elif not has_mat:
                errs.append(
                    f"{fn} 引用了工程内材质 guid={guid}，但磁盘上没有 SpriteFlash.mat。"
                    f"先跑 WarEra → 阶段B 一键搭建 生成材质。")

    return errs


def report_material_guid_limit():
    """打印一条说明，避免下一个人误以为"没报错=材质引用已验证"。"""
    return ("prefab → 材质 的 guid 身份【离线不可验证】（Tuanjie 的 .meta guid 是加密串，"
            "与 prefab 里的十六进制 guid 不可换算）；"
            "该断言在 PhaseBBootstrap.VerifyBuild ① 里用 AssetDatabase 反查，需在编辑器内跑。")




def main(argv):
    quiet = "--quiet" in argv

    py = read_py_assets()
    mf = read_manifest()
    cs = read_csharp()

    names = sorted(py)
    hard_errors = []
    not_imported = []

    if not quiet:
        print("=" * 104)
        print("素材导入参数四方比对  ①key_assets.py  ②keyed_manifest.json  ③PhaseBBootstrap.cs  ④.png.meta")
        print("=" * 104)
        print("%-16s %-22s %-14s %-14s %-10s %s"
              % ("资产", "①→②→③ PPU", "pivot", "SpriteMode", "④meta", "判定"))

    # 集合一致性
    for label, group in (("key_assets.py", py), ("keyed_manifest.json", mf),
                         ("PhaseBBootstrap.cs", cs)):
        missing = set(names) - set(group)
        extra = set(group) - set(names)
        if missing:
            hard_errors.append(f"{label} 缺 {sorted(missing)}")
        if extra:
            hard_errors.append(f"{label} 多 {sorted(extra)}")

    for name in names:
        if name not in py or name not in mf or name not in cs:
            continue
        p, m, c = py[name], mf[name], cs[name]
        row_err = []

        # ①②③ 三处参数必须逐字相同（这是纯代码层面，必须一致）
        if not approx(p["ppu"], m["ppu"]) or not approx(p["ppu"], c["ppu"]):
            row_err.append("PPU ①%.4f ②%.4f ③%.4f" % (p["ppu"], m["ppu"], c["ppu"]))
        for tag, q in (("②", m), ("③", c)):
            if not (approx(p["pivot"][0], q["pivot"][0]) and approx(p["pivot"][1], q["pivot"][1])):
                row_err.append("pivot ①%s %s%s" % (p["pivot"], tag, q["pivot"]))

        # ---- 网格：① 是 (列,行)，③ 是单格像素 (w,h)。必须换算后才能比 ----
        # 换算基准是 PNG 实际像素尺寸（不是硬编码 256），否则 build_panel_bg(256×176) 会算错。
        want_mul = p["grid"] is not None
        if m["multiple"] != want_mul:
            row_err.append("spriteMode ①%s ②%s" % ("Multiple" if want_mul else "Single",
                                                    "Multiple" if m["multiple"] else "Single"))
        if c["multiple"] != want_mul:
            row_err.append("spriteMode ①%s ③%s" % ("Multiple" if want_mul else "Single",
                                                    "Multiple" if c["multiple"] else "Single"))

        if want_mul:
            png = os.path.join(KEYED_DIR, name + ".png")
            size = png_size(png) if os.path.isfile(png) else None
            cols, rows = p["grid"]
            if size is None:
                row_err.append("PNG 缺失，无法核对切分尺寸：%s" % png)
            else:
                want_cell = (size[0] // cols, size[1] // rows)
                # ② 的 cellSize 与 ③ 的 cell 都必须等于这个推导值
                if m["cell"] and m["cell"] != want_cell:
                    row_err.append("cellSize ②%s ≠ 推导%s" % (m["cell"], want_cell))
                if c["cell"] and c["cell"] != want_cell:
                    row_err.append("cellSize ③%s ≠ 推导%s" % (c["cell"], want_cell))
                if m["grid"] and m["grid"] != (cols, rows):
                    row_err.append("grid ②%s ≠ ①%s" % (m["grid"], (cols, rows)))
                if size[0] % cols or size[1] % rows:
                    row_err.append("图尺寸 %s 不能被 %dx%d 整除" % (size, cols, rows))

        # ④ meta：判"是否已导入"，不是判 bug
        meta = read_meta(name)
        if meta is None:
            row_err.append("meta 不存在")
            meta_state = "缺失"
        elif meta["texture_type"] != TEXTURE_TYPE_SPRITE:
            not_imported.append(name)
            meta_state = "未导入"
        else:
            meta_state = "已导入"
            if not approx(meta["ppu"], c["ppu"]):
                row_err.append("meta PPU %.4f ≠ ③%.4f" % (meta["ppu"], c["ppu"]))
            if meta["pivot"] and not (approx(meta["pivot"][0], c["pivot"][0], 1e-3)
                                      and approx(meta["pivot"][1], c["pivot"][1], 1e-3)):
                row_err.append("meta pivot %s ≠ ③%s" % (meta["pivot"], c["pivot"]))
            if meta["alignment"] != ALIGNMENT_CUSTOM:
                row_err.append("meta alignment=%s（应 Custom=9）" % meta["alignment"])
            if meta["alpha_is_transparency"] != 1:
                row_err.append("meta alphaIsTransparency=%s" % meta["alpha_is_transparency"])
            if meta["mesh_type"] != MESH_FULLRECT:
                row_err.append("meta spriteMeshType=%s（应 FullRect=0）" % meta["mesh_type"])
            if meta["max_size"] != 256:
                row_err.append("meta maxTextureSize=%s（DefaultTexturePlatform，应 256）"
                               % meta["max_size"])
            if meta["sprite_mode"] != (SPRITE_MODE_MULTIPLE if want_mul else SPRITE_MODE_SINGLE):
                row_err.append("meta spriteMode=%s（应 %s）"
                               % (meta["sprite_mode"],
                                  SPRITE_MODE_MULTIPLE if want_mul else SPRITE_MODE_SINGLE))
            if want_mul:
                # 期望帧数 = 列数 × 行数。用 PNG 实际尺寸推，不用硬编码 256。
                png = os.path.join(KEYED_DIR, name + ".png")
                size = png_size(png) if os.path.isfile(png) else (256, 256)
                cols, rows = p["grid"]
                want_n = cols * rows
                if meta["sub_sprites"] != want_n:
                    row_err.append("meta 切片 %d 帧 ≠ 应 %d 帧" % (meta["sub_sprites"], want_n))

        if row_err:
            hard_errors.append(f"{name}: " + "；".join(row_err))

        if not quiet:
            print("%-16s %-22s %-14s %-14s %-10s %s"
                  % (name,
                     "%.4f → %.4f → %.4f" % (p["ppu"], m["ppu"], c["ppu"]),
                     "%.2f,%.2f" % p["pivot"],
                     "Multiple" if want_mul else "Single",
                     meta_state,
                     "OK" if not row_err else "✗ " + "；".join(row_err)))

    # ---------------- 结论 ----------------
    if not quiet:
        print("-" * 104)

    print(f"资产数 {len(names)}；参数不一致 {len(hard_errors)} 项；meta 未导入 {len(not_imported)} 张")

    # ---------------- ⑤ shader 编译隐患 ----------------
    shader_errs = check_shader()
    if shader_errs:
        print(f"\n[shader 编译隐患 —— {len(shader_errs)} 项]")
        for e in shader_errs:
            print("  ✗ " + e)
        hard_errors.extend(shader_errs)
    elif not quiet:
        print("SpriteFlash.shader：无重定义 / _FlashAmount 有 [PerRendererData] ✅")

    # ---------------- ⑥ 材质引用 ----------------
    mat_errs = check_material_refs()
    if mat_errs:
        print(f"\n[材质引用 —— {len(mat_errs)} 项]")
        for e in mat_errs:
            print("  ✗ " + e)
        hard_errors.extend(mat_errs)
    elif not quiet:
        print("prefab 材质引用：UnitPrefab/BuildingPrefab 均未指向内置默认材质 ✅")
    if not quiet:
        print("  （提示）" + report_material_guid_limit())

    if hard_errors:
        print("\n[必须修 —— 这些都不抛异常、不中断构建，但会让功能静默失效]")
        for e in hard_errors:
            print("  ✗ " + e)
        return 1

    if not_imported:
        print(f"\n[meta 未导入 —— 不是 bug，只是还没跑编辑器菜单]")
        print("  C# 里的 SpriteSpecs 只是声明，必须执行一次下面任一菜单才算落盘：")
        print("    WarEra → 阶段B 一键搭建                  （全流程重建）")
        print("    WarEra → 仅修复 Art/Keyed 导入参数        （只写 meta，不动场景）")
        print("  执行后再跑本脚本，④meta 列应全部变「已导入」。")
        print("  未导入时的游戏表现：单位仍是占位方块（def.sprite 为 null 会刷 LogWarning）。")
        return 2

    print("\n✅ 四方一致且已导入：PPU / pivot / spriteMode / 切片数 / 材质开关全部对上。")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
