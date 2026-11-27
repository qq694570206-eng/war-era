#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
key_assets.py — AI 白/灰底素材批量抠图（阶段 C / P0-1）

依据：
  docs/art-asset-integration-v1.md   §2 逐资产 tol/pivot 总表、§3 抠图判定、§4.1 explosion 序列图
  docs/animation-facing-spec-v1.md   （无抠图条款，仅引用 PPU）
  团队裁定（优先于两份规格原文）：PPU 逐张取裁定表；输出扁平到 Art/Keyed/ 并保持同名

流程（每个资产）：
  1. seed = 四边 2px 宽边缘环的众数色（不是四角单点 —— 四角会被阴影污染，
     实测 hq 四角从 (214,213,209) 到 (245,245,239)）
  2. tol 逐图取自 §2 总表（实测标定值，不做自动推导：24 张图跨三档底，自动必错）
  3. 背景候选 = 与 seed 逐通道最大差 <= tol
  4. mask = 从四边所有候选像素做 4-邻域 floodfill
     禁止全图阈值：全图阈值会把物体内部白色高光一起打穿
     （muzzle_flash 中心白核、explosion 白芯、hq 403px 高光洞）
  5. hq / rifleman / bazooka / mgtower：3x3 开运算 1 遍 + 闭运算 1 遍（§3.1 第 4 条）
  6. 连通域过滤去碎块（规格 §3.3 "先去噪再取 bbox" 的落地；见 drop_specks 注释）
  7. 去白边 bleed：3 轮最近不透明像素 RGB 扩散（在【硬 mask】上做，见 key_floodfill 注释）
  8. 羽化 1px（3x3 高斯 sigma=0.7），只作用在 mask 边界内外各 1px
  9. smoke_column 例外：亮度键控 alpha = clamp((203 - lum)/203)，不羽化不 bleed

不做填洞：全流程无填洞步骤。四边 floodfill 天然不会把被前景包围的内部区域判成背景，
因此 hq 403px / powerplant 359px / mgtower 1313px 的内部高光洞自动保留。
explosion / harvester 是规格里的强制例外（填洞 = 直接毁图），此处同样不填。

输出：unity-project/Assets/_Project/Art/Keyed/<同名>.png（RGBA，保持原帧尺寸）
      共 20 张（24 张原图 − 4 张地形 tile，tile 不抠图、本轮不接线）
幂等：只读 assets/，只写 Art/Keyed/，无随机、无时间戳，重复执行逐字节一致

用法：
  python3 tools/key_assets.py                  # 全量抠图
  python3 tools/key_assets.py --dry-run        # 只核算不落盘
  python3 tools/key_assets.py --only hq tank   # 单张复查
"""

import argparse
import os
import sys
import json
from collections import Counter

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC_DIR = os.path.join(ROOT, "assets")
OUT_DIR = os.path.join(ROOT, "unity-project", "Assets", "_Project", "Art", "Keyed")
REPORT_PATH = os.path.join(ROOT, "tools", "keyed_report.json")
MANIFEST_PATH = os.path.join(ROOT, "tools", "keyed_manifest.json")

# ---------------------------------------------------------------- 资产表
# tol    : 美术规格 §2 总表的逐图标定值
# morph  : 是否做 3x3 开+闭运算（规格 §3.1：hq/rifleman/bazooka/mgtower 必做）
# pivot  : 归一化 pivot（§2 表 + §5 分类规则），基准 = 图像矩形左下角
# ppu    : 【团队裁定表】（优先于规格 §1.2 的个别差异值）
ASSETS = [
    # ---- 建筑 8 张（pivot y=0.10：让底菱形中心对齐 transform.position） ----
    dict(name="hq",         src="hq.png",         tol=23, morph=True,  pivot=(0.5, 0.10), ppu=3.20),
    dict(name="powerplant", src="powerplant.png", tol=12, morph=False, pivot=(0.5, 0.10), ppu=5.82),
    dict(name="refinery",   src="refinery.png",   tol=12, morph=False, pivot=(0.5, 0.10), ppu=4.57),
    dict(name="barracks",   src="barracks.png",   tol=12, morph=False, pivot=(0.5, 0.10), ppu=5.33),
    dict(name="warfactory", src="warfactory.png", tol=12, morph=False, pivot=(0.5, 0.10), ppu=4.57),
    dict(name="radar",      src="radar.png",      tol=12, morph=False, pivot=(0.5, 0.10), ppu=5.82),
    dict(name="mgtower",    src="mgtower.png",    tol=18, morph=True,  pivot=(0.5, 0.10), ppu=8.00),
    dict(name="atcannon",   src="atcannon.png",   tol=12, morph=False, pivot=(0.5, 0.10), ppu=7.11),
    # ---- 步兵 2 张（pivot y=0.02：脚踩画面最底，留 2% 避免与地面共面抖动） ----
    dict(name="rifleman",   src="rifleman.png",   tol=24, morph=True,  pivot=(0.5, 0.02), ppu=17.07),
    dict(name="bazooka",    src="bazooka.png",    tol=20, morph=True,  pivot=(0.5, 0.02), ppu=17.07),
    # ---- 载具 4 张（pivot y=0.00：履带接地面 = transform 位置） ----
    dict(name="tank",       src="tank.png",       tol=12, morph=False, pivot=(0.5, 0.00), ppu=9.14),
    dict(name="heavytank",  src="heavytank.png",  tol=12, morph=False, pivot=(0.5, 0.00), ppu=8.00),
    dict(name="harvester",  src="harvester.png",  tol=18, morph=False, pivot=(0.5, 0.00), ppu=9.14),
    # ---- 特效 4 张 ----
    dict(name="explosion",    src="effects/explosion.png",    tol=12, morph=False, pivot=(0.5, 0.50), ppu=10.67,
         cells=(4, 4)),                                    # 16 帧序列图，逐帧独立边缘环（§4.1）
    dict(name="muzzle_flash", src="effects/muzzle_flash.png", tol=12, morph=False, pivot=(0.5, 0.50), ppu=25.60),
    dict(name="nuke_cloud",   src="effects/nuke_cloud.png",   tol=12, morph=False, pivot=(0.5, 0.00), ppu=4.27),
    dict(name="smoke_column", src="effects/smoke_column.png", tol=0,  morph=False, pivot=(0.5, 0.00), ppu=18.3,
         mode="luma"),                                     # 亮度键控，不用 floodfill（§3.3）
    # ---- UI 3 张（本轮不接线，产物先备好） ----
    # build_panel_bg：上下各 40px 白底，Crop 到 y40–215 后整幅都是面板本体，无背景可抠。
    # 它不是"抠图对象"而是"裁切对象"：填满整幅即正确，不参与四角 alpha 检查。
    dict(name="build_panel_bg", src="ui/build_panel_bg.png", tol=12, morph=False, pivot=(0.5, 0.5), ppu=1.0,
         crop=(0, 40, 256, 216), opaque=True),
    dict(name="minimap_frame",  src="ui/minimap_frame.png",  tol=12, morph=False, pivot=(0.5, 0.5), ppu=1.0),
    dict(name="resource_icons", src="ui/resource_icons.png", tol=12, morph=False, pivot=(0.5, 0.5), ppu=1.0),
]

# 规格明确"禁止填洞"的 2 张（本脚本全流程无填洞步骤，此处仅用于报告与人工复核提示）
NO_FILL = {"explosion", "harvester"}
# 规格要求提级去噪、需人工目检的 4 张（§3.3）
MUST_MORPH = {"hq", "rifleman", "bazooka", "mgtower"}
LUMA_BG = 203.0   # smoke_column 背景亮度（§2：背景 (205,203,199)，均亮约 203）


# ---------------------------------------------------------------- 基础工具
def load_rgb(path):
    im = Image.open(path)
    if im.mode not in ("RGB", "RGBA"):
        im = im.convert("RGB")
    return im.convert("RGB")


def ring_mode(rgb, width=2):
    """四边 width px 宽边缘环的众数色（§3.1 第 1 条：不用四角单点）。"""
    h, w, _ = rgb.shape
    parts = [
        rgb[0:width, :, :].reshape(-1, 3),
        rgb[h - width:h, :, :].reshape(-1, 3),
        rgb[:, 0:width, :].reshape(-1, 3),
        rgb[:, w - width:w, :].reshape(-1, 3),
    ]
    ring = np.concatenate(parts, axis=0)
    counts = Counter(map(tuple, ring.tolist()))
    return counts.most_common(1)[0][0], ring


def floodfill_bg(candidate):
    """从四边所有候选背景像素做 4-邻域 floodfill，返回"已连通到边界"的布尔图。"""
    h, w = candidate.shape
    reached = np.zeros((h, w), dtype=bool)
    stack = []

    def push(y, x):
        if candidate[y, x] and not reached[y, x]:
            reached[y, x] = True
            stack.append((y, x))

    for x in range(w):
        push(0, x)
        push(h - 1, x)
    for y in range(h):
        push(y, 0)
        push(y, w - 1)

    while stack:
        y, x = stack.pop()
        if y > 0:
            push(y - 1, x)
        if y + 1 < h:
            push(y + 1, x)
        if x > 0:
            push(y, x - 1)
        if x + 1 < w:
            push(y, x + 1)
    return reached


def _window_reduce(a, op, border):
    """3x3 窗口归约。border 决定图像外怎么取值：
    - 腐蚀用 False：图像外视为背景，否则边缘像素被自我复制、永远侵蚀不掉
    - 膨胀用 True ：同理，外侧视为前景让边缘闭合正确
    这个 padding 选择不是细节：用 edge padding 时实测 mgtower 左上角残留 240px
    前景（原图 rgb=(231,229,225)，距 seed 28 > tol 18，是噪点）。
    """
    p = np.pad(a, 1, mode="constant", constant_values=border)
    h, w = a.shape
    out = a.copy()
    for dy in range(3):
        for dx in range(3):
            out = op(out, p[dy:dy + h, dx:dx + w])
    return out


def morph_open_close(fg):
    """3x3 开运算 1 遍（去边缘孤立噪点）+ 3x3 闭运算 1 遍（补羽化断点）。
    顺序按 §3.1 第 4 条：开 1 遍 + 闭 1 遍。
    """
    def erode(m):
        return _window_reduce(m, np.minimum, False)

    def dilate(m):
        return _window_reduce(m, np.maximum, True)

    def open_(m):
        return dilate(erode(m))

    def close_(m):
        return erode(dilate(m))

    return close_(open_(fg))


def feather(alpha, sigma=0.7):
    """1px 半径高斯羽化（3x3 核，sigma=0.7），只作用在边界内外各 1px。
    依据 §3.2：图缩到约 0.56 倍显示（256→144px），2px 以上羽化会糊成灰边。
    """
    k = np.zeros((3, 3), dtype=np.float32)
    for i in range(3):
        for j in range(3):
            k[i, j] = np.exp(-((i - 1) ** 2 + (j - 1) ** 2) / (2.0 * sigma * sigma))
    k /= k.sum()

    p = np.pad(alpha.astype(np.float32), 1, mode="constant", constant_values=0.0)
    h, w = alpha.shape
    out = np.zeros((h, w), dtype=np.float32)
    for i in range(3):
        for j in range(3):
            out += k[i, j] * p[i:i + h, j:j + w]
    return np.clip(out, 0.0, 1.0)


def bleed(rgb, alpha, rounds=3):
    """去白边：对透明像素做 rounds 轮最近不透明像素 RGB 扩散（只改 RGB，不改 alpha）。

    【必须在羽化之前调用，入参 alpha 是硬 mask（0/1）】
    如果先羽化再 bleed，边缘像素 alpha 已是 0.05~0.99，会被判为"不透明"而跳过扩散，
    白底 RGB 原样保留 → 双线性采样后仍是 1px 白晕。
    实测反例：先羽化后 bleed 时羽化带 meanRGB=(241,240,236)，肉眼即白边；
    调序后 tank 降到 (177,176,167)。这是规格 §10 验收点③（白边测试）的直接判据。
    """
    out = rgb.astype(np.float32).copy()
    opaque = alpha > 0.004
    h, w, _ = out.shape
    for _ in range(rounds):
        acc = np.zeros_like(out)
        cnt = np.zeros((h, w), dtype=np.float32)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dy == 0 and dx == 0:
                    continue
                s_rgb = np.zeros_like(out)
                s_op = np.zeros((h, w), dtype=bool)
                ys, yd = (slice(0, h - 1), slice(1, h)) if dy < 0 else \
                         ((slice(1, h), slice(0, h - 1)) if dy > 0 else (slice(0, h), slice(0, h)))
                xs, xd = (slice(0, w - 1), slice(1, w)) if dx < 0 else \
                         ((slice(1, w), slice(0, w - 1)) if dx > 0 else (slice(0, w), slice(0, w)))
                s_rgb[yd, xd] = out[ys, xs]
                s_op[yd, xd] = opaque[ys, xs]
                acc += s_rgb * s_op[:, :, None]
                cnt += s_op
        fill = (~opaque) & (cnt > 0)
        if not fill.any():
            break
        out[fill] = acc[fill] / cnt[fill][:, None]
        opaque = opaque | fill
    return out


def bbox_of(alpha, thr=0.5):
    """前景 bbox（x0, y0, x1, y1），含端点；y 以图像顶为 0（PIL 行序）。"""
    ys, xs = np.where(alpha > thr)
    if len(ys) == 0:
        return None
    return (int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max()))


def components(alpha, thr=0.5):
    """列出所有 4-邻域连通块：[(像素数, 是否触边, (x0,y0,x1,y1)), ...]"""
    h, w = alpha.shape
    solid = alpha > thr
    seen = np.zeros((h, w), dtype=bool)
    out = []
    for sy in range(h):
        for sx in range(w):
            if not solid[sy, sx] or seen[sy, sx]:
                continue
            stack = [(sy, sx)]
            seen[sy, sx] = True
            n = 0
            y0 = y1 = sy
            x0 = x1 = sx
            edge = False
            while stack:
                y, x = stack.pop()
                n += 1
                if y < y0: y0 = y
                if y > y1: y1 = y
                if x < x0: x0 = x
                if x > x1: x1 = x
                if y == 0 or y == h - 1 or x == 0 or x == w - 1:
                    edge = True
                for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
                    if 0 <= ny < h and 0 <= nx < w and solid[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True
                        stack.append((ny, nx))
            out.append((n, edge, (x0, y0, x1, y1)))
    return out


def drop_specks(alpha, min_px=200):
    """连通域过滤：移除面积过小的碎块（噪点）。

    为什么必须有这一步（规格没写，但不做就错）：
    3x3 开运算只能去掉 1~2px 的孤立点，去不掉 20~160px 的噪点团。
    实测修复 padding 后仍存在的触边碎块：
      mgtower 44 个（最大 162px）、rifleman 6 个（36px）、bazooka 16 个（78~87px）。
    不过滤则 bbox 被噪点撑满 —— bazooka 正是因此被规格判为"Trim 不可信，套 rifleman 参数"。

    阈值 min_px=200：实测最大噪点团 162px < 200，全部清除；
    最小主体是 tank 24115px，远大于 200，不会被误删。
    用绝对阈值而非比例阈值：比例阈值（如 0.05%）在 65536px 图上约 32px，
    挡不住 36~162px 的噪点团（这是踩过的坑，别改回比例）。
    """
    comps = components(alpha, 0.5)
    if not comps:
        return alpha, 0
    biggest = max(c[0] for c in comps)
    limit = min(min_px, biggest)   # 主体若比阈值还小（极小资产）也不整个删掉
    if all(c[0] >= limit for c in comps):
        return alpha, 0

    h, w = alpha.shape
    solid = alpha > 0.5
    seen = np.zeros((h, w), dtype=bool)
    keep_mask = np.zeros((h, w), dtype=bool)
    dropped = 0
    for sy in range(h):
        for sx in range(w):
            if not solid[sy, sx] or seen[sy, sx]:
                continue
            stack = [(sy, sx)]
            seen[sy, sx] = True
            pix = []
            while stack:
                y, x = stack.pop()
                pix.append((y, x))
                for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
                    if 0 <= ny < h and 0 <= nx < w and solid[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True
                        stack.append((ny, nx))
            if len(pix) >= limit:
                for y, x in pix:
                    keep_mask[y, x] = True
            else:
                dropped += len(pix)

    # 羽化前的软边（0~0.5 之间）跟着最近的保留块走，避免被一起清成 0
    soft = (alpha > 0.02) & (alpha <= 0.5)
    grow = keep_mask.copy()
    for _ in range(2):
        p = np.pad(grow, 1, mode="constant", constant_values=False)
        grow = keep_mask | (p[0:-2, 1:-1] | p[2:, 1:-1] | p[1:-1, 0:-2] | p[1:-1, 2:])
    out = np.where(keep_mask, alpha, np.where(soft & grow, alpha, 0.0))
    return out.astype(np.float32), dropped


# ---------------------------------------------------------------- 两条抠图路径
def key_floodfill(rgb, tol, morph, despeck=True):
    """标准路径：边缘环众数 seed → tol 判据 → 四边 floodfill → 可选开闭 → 去碎块 → bleed → 羽化。"""
    seed, ring = ring_mode(rgb)
    diff = np.abs(rgb.astype(np.int16) - np.array(seed, dtype=np.int16)).max(axis=2)
    candidate = diff <= tol

    reached = floodfill_bg(candidate)   # 连通到边界的背景
    fg = ~reached                        # 前景 = 未被 floodfill 打到的（内部高光洞自动保留）

    if morph:
        fg = morph_open_close(fg)

    alpha_hard = fg.astype(np.float32)
    dropped = 0
    if despeck:
        alpha_hard, dropped = drop_specks(alpha_hard)

    # 顺序：先 bleed（硬 mask 上向透明区扩色）→ 再羽化（alpha 渐变）
    out_rgb = bleed(rgb, alpha_hard, rounds=3)
    alpha = feather(alpha_hard)

    stats = dict(seed=[int(v) for v in seed], tol=int(tol), morph=bool(morph),
                 ring_sd=round(float(ring.std(axis=0).mean()), 2),
                 despeck_dropped_px=int(dropped))
    return out_rgb, alpha, stats


def key_luma(rgb, bg_lum=LUMA_BG):
    """smoke_column 专用：亮度键控 α=clamp((bgLum−lum)/bgLum)。不羽化、不 bleed。
    背景 (205,203,199) 与烟体同为浅灰，floodfill 必然残边（实测 154 连通块 / 26 触边，§3.3）。
    亮度键控是唯一路径；羽化/bleed 会把浅灰烟涂成深灰（§3.2 例外）。
    """
    lum = rgb.astype(np.float32).mean(axis=2)
    alpha = np.clip((bg_lum - lum) / bg_lum, 0.0, 1.0)
    stats = dict(seed="luma-keyed", tol=None, morph=False, bg_lum=bg_lum)
    return rgb.astype(np.float32), alpha, stats


# ---------------------------------------------------------------- 主流程
def process_one(asset, write=True):
    src_path = os.path.join(SRC_DIR, asset["src"])
    if not os.path.exists(src_path):
        return None, f"源文件缺失：{asset['src']}"

    rgb_img = load_rgb(src_path)
    if "crop" in asset:
        rgb_img = rgb_img.crop(asset["crop"])

    rgb = np.asarray(rgb_img, dtype=np.uint8)
    h, w, _ = rgb.shape

    cells = asset.get("cells")
    stats = dict(name=asset["name"], src=asset["src"], size=[w, h],
                 pivot=list(asset["pivot"]), ppu=asset["ppu"],
                 no_fill=asset["name"] in NO_FILL,
                 needs_manual_review=asset["name"] in MUST_MORPH)

    if cells:
        # explosion：4x4 逐帧独立边缘环 + 逐帧 floodfill（§4.1 明确不共用第 0 帧参数）
        cx, cy = cells
        cw, ch = w // cx, h // cy
        out_rgb = np.zeros((h, w, 3), dtype=np.float32)
        out_a = np.zeros((h, w), dtype=np.float32)
        frames = []
        for ry in range(cy):
            for rx in range(cx):
                sl = (slice(ry * ch, (ry + 1) * ch), slice(rx * cw, (rx + 1) * cw))
                f_rgb, f_a, f_st = key_floodfill(rgb[sl], asset["tol"], asset["morph"])
                out_rgb[sl] = f_rgb
                out_a[sl] = f_a
                frames.append(dict(index=ry * cx + rx, seed=f_st["seed"],
                                   tol=f_st["tol"],
                                   fg=round(float((f_a > 0.5).mean()), 4)))
        stats["frames"] = frames
        stats["cell"] = [cw, ch]
        stats["seed"], stats["tol"] = frames[0]["seed"], frames[0]["tol"]
        stats["fg_ratio"] = round(float((out_a > 0.5).mean()), 4)
        stats["trim"] = "off"      # 序列图整块 alpha 的 bbox 恒等于整帧，Trim 无意义
        bbox = None
    elif asset.get("mode") == "luma":
        out_rgb, out_a, st = key_luma(rgb)
        stats.update(st)
        stats["fg_ratio"] = round(float((out_a > 0.5).mean()), 4)
        stats["trim"] = "manual"
        bbox = bbox_of(out_a)
    elif asset.get("opaque"):
        # build_panel_bg：Crop 后全幅是面板本体，不抠图（§8.1 只要求 Crop）
        out_rgb = rgb.astype(np.float32)
        out_a = np.ones(rgb.shape[:2], dtype=np.float32)
        stats.update(seed="opaque(no-key)", tol=None, morph=False, trim="off",
                     fg_ratio=1.0, note="Crop 后全幅不透明，不参与抠图")
        bbox = None
    else:
        out_rgb, out_a, st = key_floodfill(rgb, asset["tol"], asset["morph"])
        stats.update(st)
        stats["fg_ratio"] = round(float((out_a > 0.5).mean()), 4)
        stats["trim"] = "auto"
        bbox = bbox_of(out_a)

    if bbox:
        stats["bbox"] = list(bbox)

    rgba = np.zeros((h, w, 4), dtype=np.uint8)
    rgba[:, :, :3] = np.clip(out_rgb + 0.5, 0, 255).astype(np.uint8)
    rgba[:, :, 3] = np.clip(out_a * 255.0 + 0.5, 0, 255).astype(np.uint8)

    dst_path = os.path.join(OUT_DIR, asset["name"] + ".png")
    if write:
        os.makedirs(OUT_DIR, exist_ok=True)
        Image.fromarray(rgba, mode="RGBA").save(dst_path, optimize=True)
        stats["out"] = os.path.relpath(dst_path, ROOT).replace(os.sep, "/")

        # 自检：四角 alpha 必须为 0
        # 不适用四角判据的三类：explosion（格界处本就有前景）、smoke_column（烟从地面升起）、
        #                       build_panel_bg（整幅是面板本体，无背景）
        no_corner = bool(cells) or asset.get("mode") == "luma" or asset.get("opaque")
        if not no_corner:
            a8 = np.asarray(Image.open(dst_path).convert("RGBA"))[:, :, 3]
            stats["corner_alpha"] = [int(a8[0, 0]), int(a8[0, -1]),
                                     int(a8[-1, 0]), int(a8[-1, -1])]
    return stats, None


def main(argv):
    ap = argparse.ArgumentParser(description="AI 白/灰底素材批量抠图（阶段C）")
    ap.add_argument("--only", nargs="*", help="只处理指定资产（单张复查）")
    ap.add_argument("--dry-run", action="store_true", help="只核算，不写文件")
    args = ap.parse_args(argv)

    if not os.path.isdir(SRC_DIR):
        print(f"[FATAL] 源目录不存在：{SRC_DIR}")
        return 1

    targets = ASSETS
    if args.only:
        keep = set(args.only)
        targets = [a for a in ASSETS if a["name"] in keep]
        unknown = keep - {a["name"] for a in targets}
        if unknown:
            print(f"[WARN] 未收录的资产名：{sorted(unknown)}")
        if not targets:
            print("[FATAL] --only 未匹配任何资产")
            return 1

    print("=" * 80)
    print("阶段C 素材抠图  |  规格：docs/art-asset-integration-v1.md §2 §3")
    print(f"源目录  ：{os.path.relpath(SRC_DIR, ROOT)}  （只读，不修改）")
    print(f"输出目录：{os.path.relpath(OUT_DIR, ROOT)}" + ("   [dry-run，不落盘]" if args.dry_run else ""))
    print("=" * 80)
    print(f"{'资产':<16}{'seed':<18}{'tol':>4}{'morph':>6}{'fg%':>8}  {'bbox / 备注'}")
    print("-" * 80)

    report, errors, manual = [], [], []
    for a in targets:
        stats, err = process_one(a, write=not args.dry_run)
        if err:
            errors.append(f"{a['name']}: {err}")
            print(f"{a['name']:<16}[ERROR] {err}")
            continue
        report.append(stats)

        seed = stats.get("seed")
        seed_s = str(tuple(seed)) if isinstance(seed, list) else str(seed)
        tol_s = "-" if stats.get("tol") in (None, 0) else str(stats["tol"])
        fg = stats.get("fg_ratio", 0.0) * 100

        note = ""
        if stats.get("frames"):
            note = f"{stats['cell'][0]}x{stats['cell'][1]} x {len(stats['frames'])} 帧，逐帧独立 seed/tol"
        elif stats.get("bbox"):
            note = f"bbox={stats['bbox']}"
        if a["name"] in NO_FILL:
            note += "  [禁填洞]"
        if a["name"] in MUST_MORPH:
            note += "  [需目检]"
        print(f"{stats['name']:<16}{seed_s:<18}{tol_s:>4}"
              f"{'Y' if stats.get('morph') else 'N':>6}{fg:>7.1f}%  {note}")

        if a["name"] in MUST_MORPH:
            manual.append(a["name"])
        if stats.get("corner_alpha") and max(stats["corner_alpha"]) != 0:
            errors.append(f"{a['name']}: 四角 alpha 非零 {stats['corner_alpha']}")

    print("-" * 80)
    print(f"处理成功 {len(report)} 张，失败 {len(errors)} 张")

    if manual:
        print(f"\n需人工目检（美术规格 §3.3 例外清单）：{', '.join(manual)}")

    if errors:
        print("\n[ERRORS]")
        for e in errors:
            print("  " + e)
        return 1

    if not args.dry_run:
        with open(REPORT_PATH, "w", encoding="utf-8") as f:
            json.dump(dict(assets=report, no_fill=sorted(NO_FILL),
                           output_dir=os.path.relpath(OUT_DIR, ROOT).replace(os.sep, "/")),
                      f, ensure_ascii=False, indent=2)
        # Unity 侧 ConfigureSpriteImporters 可对照此清单校验 PPU/pivot
        with open(MANIFEST_PATH, "w", encoding="utf-8") as f:
            json.dump(dict(items=[
                dict(name=s["name"],
                     file=s.get("out", ""),
                     spritePixelsPerUnit=s["ppu"],
                     pivot=s["pivot"],
                     spriteMode="Multiple" if s.get("frames") else "Single")
                for s in report]), f, ensure_ascii=False, indent=2)
        print(f"\n报告：{os.path.relpath(REPORT_PATH, ROOT)}")
        print(f"清单：{os.path.relpath(MANIFEST_PATH, ROOT)}（供 Unity 导入参数对照）")

    print("完成。")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
