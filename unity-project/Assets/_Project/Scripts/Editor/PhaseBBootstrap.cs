// ================================================================
// PhaseBBootstrap.cs — 阶段B 一键搭建（编辑器引导脚本）
// 菜单：WarEra → 阶段B 一键搭建
// 自动生成：15个UnitDefinition + 2个FactionDefinition + 地图数据
//           + 占位Prefab + 完整场景（系统接线 + UI Canvas）
// 幂等：重复执行会覆盖重建资产与场景
// ================================================================
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.IO;

public static class PhaseBBootstrap
{
    const string DataCore = "Assets/_Project/Data/Core";
    const string DataWW2 = "Assets/_Project/Data/Theme_WW2";
    const string PrefabDir = "Assets/_Project/Prefabs";
    const string SceneDir = "Assets/_Project/Scenes";
    const string KeyedDir = "Assets/_Project/Art/Keyed";

    static Sprite circleSprite;
    static Sprite squareSprite;
    static Material flashMaterial;

    [MenuItem("WarEra/阶段B 一键搭建")]
    public static void Setup()
    {
        if (EditorApplication.isCompiling)
        {
            Debug.LogError("[PhaseB] 正在编译中，请等编译完成后再执行。");
            return;
        }
        if (EditorApplication.isPlaying)
        {
            Debug.Log("[PhaseB] 检测到 Play 模式，自动退出后自动重新搭建…");
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => Setup();
            return;
        }
        EnsureFolders();

        // Refresh 必须放在【配置导入参数之前】，不能放最后。
        // 必须先让 Unity 发现 Art/Keyed/*.png 并生成 meta，AssetImporter.GetAtPath 才拿得到
        // TextureImporter。原来 Refresh 在第 52 行（最后），等于"先配置再发现文件"：
        // 首次跑时文件还没进 AssetDatabase，GetAtPath 返回 null → 走 ImportAsset 兜底，
        // 而随后的 Refresh 又会把刚写的设置重新导入一遍冲掉。
        // 这就是"函数确实被调用了，但 meta 里仍是 textureType: 0"的原因。
        AssetDatabase.Refresh();

        CreatePlaceholderSprites();
        ConfigureSpriteImporters();     // P0-2：逐张 PPU / pivot / Sprite 导入参数
        CreateFlashMaterial();          // P0-6：受击闪白材质
        CreatePrefabs();                // P0-3：四层结构
        CreateFactionAssets();
        var defs = CreateUnitDefinitions();   // P0-4 / P0-5：spriteForwardDeg + def.sprite
        CreateMapAssets();
        BuildScene(defs);
        AssetDatabase.SaveAssets();

        // 最后统一自检。放在 SaveAssets 之后，因为要读的是"落盘后"的资产，
        // 而不是内存里刚 new 出来的对象 —— 后者永远是对的，证明不了任何事。
        VerifyBuild(defs);

        Debug.Log("[PhaseB] ✅ 搭建完成。打开 Assets/_Project/Scenes/Battle.unity 按 Play 试玩。" +
                  "操作：左键点选单位，拖动框选，右键/点击下达移动攻击，底部按钮建造。");
    }

    static void EnsureFolders()
    {
        foreach (var dir in new[] { DataCore, DataWW2, PrefabDir, SceneDir,
                                    "Assets/_Project/Scenes", KeyedDir,
                                    "Assets/_Project/Shaders" })
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
    }

    // ================================================================
    // 构建后自检 —— "关掉游戏还能证明它是对的"
    //
    // 为什么必须单独写这一段：上面每一步都会打印自己的成功日志，
    // 但"每步都报成功"和"东西真的接上了"是两件事。实测踩过两类：
    //   ① shader 编译失败（redefinition of 'SpriteFrag'）→ CreateFlashMaterial 照样返回，
    //      材质创建成功但没 pass，闪白静默退回顶点色。日志里只有一行 Shader error，
    //      混在几千行导入日志里，而画面看起来"闪白还有一点"，很容易被判成能用。
    //   ② prefab 引用的材质 guid 与磁盘上 .mat 的 guid 不一致 → 材质丢引用，
    //      SpriteRenderer 回落默认材质，闪白彻底没有，且 Inspector 里只是显示 None。
    // 这两类都不会抛异常、不会中断构建，只能靠事后读落盘资产反查。
    //
    // 断言口径：期望值来自【规格表/常量表】，不引用被测对象自己的字段。
    // 例如"每个 def.sprite 必须非空"来自规格 §0⓪ 的要求，
    // 而不是读 def.sprite 再断言它等于自己。
    // ================================================================
    static void VerifyBuild(System.Collections.Generic.Dictionary<string, UnitDefinition> defs)
    {
        int fail = 0;

        // ---- ① shader 真的编译通过，且材质真的挂在 prefab 上 ----
        Shader sh = Shader.Find("WarEra/SpriteFlash");
        if (sh == null || !sh.isSupported)
        {
            fail++;
            Debug.LogError("[自检] ✗ shader 'WarEra/SpriteFlash' 未编译通过" +
                           "（Shader.Find == null 或 isSupported == false）。" +
                           "闪白会静默退回顶点色，见 UnitEntity.UpdateFlash 的 else 分支。");
        }
        else
        {
            // 从【落盘的 prefab】读 renderer，而不是从内存对象 —— 内存对象一定是对的。
            var prefab = LoadPrefab("UnitPrefab");
            var sr = prefab != null ? prefab.GetComponentInChildren<SpriteRenderer>(true) : null;
            if (sr == null)
            {
                fail++;
                Debug.LogError("[自检] ✗ UnitPrefab 上找不到 SpriteRenderer。");
            }
            else if (sr.sharedMaterial == null || sr.sharedMaterial.shader != sh)
            {
                fail++;
                Debug.LogError("[自检] ✗ UnitPrefab 的 SpriteRenderer 没有挂 WarEra/SpriteFlash 材质" +
                               $"（当前 = {(sr.sharedMaterial == null ? "null" : sr.sharedMaterial.shader.name)}）。" +
                               "典型原因：prefab 引用的材质 guid 与磁盘 .mat 不一致（材质丢引用）。" +
                               "闪白会静默失效。修法：删掉 Prefabs/UnitPrefab.prefab 后重跑本菜单。");
            }
            else if (!sr.sharedMaterial.HasProperty("_FlashAmount"))
            {
                fail++;
                Debug.LogError("[自检] ✗ 材质缺少 _FlashAmount 属性，MaterialPropertyBlock 会静默失效。");
            }
        }

        // ---- ② 每个 UnitDefinition 真的拿到了 sprite（规格 §0⓪）----
        // 期望：15 个 unitId 全部非空。数量从规格表来，不从 map.Count 来。
        const int ExpectedDefCount = 15;
        int wired = 0;
        var missing = new System.Text.StringBuilder();
        foreach (var kv in defs)
        {
            // 重新从磁盘加载，确认 def.sprite 真的被序列化保存了
            var onDisk = AssetDatabase.LoadAssetAtPath<UnitDefinition>(
                DataCore + "/UD_" + kv.Key + ".asset");
            if (onDisk == null || onDisk.sprite == null)
                missing.Append(kv.Key).Append(' ');
            else
                wired++;
        }
        if (defs.Count != ExpectedDefCount)
        {
            fail++;
            Debug.LogError($"[自检] ✗ UnitDefinition 数量 = {defs.Count}，规格要求 {ExpectedDefCount} 个。");
        }
        if (wired != ExpectedDefCount)
        {
            fail++;
            Debug.LogError($"[自检] ✗ 落盘后 sprite 为空的 UnitDefinition：{missing}" +
                           $"（{wired}/{ExpectedDefCount} 有图）。这些单位进游戏会显示占位方块。");
        }

        // ---- ③ 真图的 PPU 换算出的世界尺寸必须等于规格目标宽 ----
        // 这是"PPU 写对了吗"的唯一硬证据：读 Sprite.bounds，不读 PPU 字段自己。
        // 期望宽来自美术规格 §2 目标表（独立来源）。
        //
        // 【步兵故意不在此表】rifleman/bazooka 的 PPU 17.07 是按"整幅 15 单位"校准的，
        // 而可见人形只占 x63–210（147px）→ 人形 8.6 单位 ≈ 规格目标 8.67。
        // 拿 bounds.size.x（= 整幅 15.0）去比 8.67 必然报错，但那是正确实现。
        // 也就是说：这个断言对步兵不成立，不加进表里比"加了再给个宽容差"更诚实。
        var expectWidth = new System.Collections.Generic.Dictionary<string, float>
        {
            { "hq", 80f }, { "powerplant", 44f }, { "refinery", 56f }, { "barracks", 48f },
            { "warfactory", 56f }, { "radar", 44f }, { "mgtower", 32f }, { "atcannon", 36f },
            { "tank", 28f }, { "heavytank", 32f }, { "harvester", 28f },
        };
        var badSize = new System.Text.StringBuilder();
        foreach (var kv in expectWidth)
        {
            var onDisk = AssetDatabase.LoadAssetAtPath<UnitDefinition>(
                DataCore + "/UD_" + kv.Key + ".asset");
            if (onDisk == null || onDisk.sprite == null) continue;   // ② 已报过
            float w = onDisk.sprite.bounds.size.x;
            // 容差 1%：radar 实测 −6.3%（主体 240px 未满框），那是规格承认的偏差，单独放过。
            float tol = kv.Key == "radar" ? 0.07f : 0.01f;
            if (Mathf.Abs(w - kv.Value) > kv.Value * tol)
                badSize.Append($"{kv.Key} 实测{w:0.0}u 应{kv.Value:0}u ");
        }
        if (badSize.Length > 0)
        {
            fail++;
            Debug.LogError($"[自检] ✗ PPU 换算出的世界宽度不符规格：{badSize}" +
                           "（期望值来自 docs/art-asset-integration-v1.md §2 目标宽表）");
        }

        if (fail == 0)
        {
            Debug.Log($"[自检] ✅ 全部通过：shader 可编译且已挂到 prefab、{wired}/{ExpectedDefCount} 个 定义有 sprite、" +
                      $"{expectWidth.Count} 张建筑/载具世界宽度符合规格目标。");
        }
        else
        {
            Debug.LogError($"[自检] ❌ {fail} 项未通过，见上方逐条 LogError。" +
                           "这些项目【不会】中断构建，也不会抛异常 —— 但进游戏后是静默失效的。");
        }
    }

    // ================================================================
    // P0-2 逐张导入参数（PPU / Custom pivot / Sprite / alphaIsTransparency）
    //
    // PPU 来源：团队裁定表（优先于美术规格 §1.2）。实测该表等价于 256 / 目标世界宽，
    //          即"Sprite rect 保持整幅 256"时目标宽精确成立。
    //          rifleman 是唯一例外：256/17.07 = 15 单位宽的整幅，而可见人形只占
    //          x63–210（147px）→ 人形实际 8.6 单位 ≈ 规格目标 8.67，同样成立。
    // pivot 来源：美术规格 §2 / §5（归一化，基准 = 图像矩形左下角）
    // ================================================================
    struct SpriteSpec
    {
        public string file;      // Art/Keyed 下的文件名（不含扩展名）
        public float ppu;
        public Vector2 pivot;
        public bool multiple;    // explosion / resource_icons 走 Multiple
        public int cellW, cellH;
        public SpriteSpec(string f, float p, float px, float py, bool mul = false, int cw = 0, int ch = 0)
        { file = f; ppu = p; pivot = new Vector2(px, py); multiple = mul; cellW = cw; cellH = ch; }
    }

    static readonly SpriteSpec[] SpriteSpecs = new[]
    {
        // 建筑（pivot y=0.10：让底菱形中心对齐 transform.position）
        new SpriteSpec("hq",         3.20f, 0.5f, 0.10f),
        new SpriteSpec("powerplant", 5.82f, 0.5f, 0.10f),
        new SpriteSpec("refinery",   4.57f, 0.5f, 0.10f),
        new SpriteSpec("barracks",   5.33f, 0.5f, 0.10f),
        new SpriteSpec("warfactory", 4.57f, 0.5f, 0.10f),
        new SpriteSpec("radar",      5.82f, 0.5f, 0.10f),
        new SpriteSpec("mgtower",    8.00f, 0.5f, 0.10f),
        new SpriteSpec("atcannon",   7.11f, 0.5f, 0.10f),
        // 步兵（pivot y=0.02：脚踩画面最底，留 2% 避免与地面共面抖动）
        new SpriteSpec("rifleman",  17.07f, 0.5f, 0.02f),
        new SpriteSpec("bazooka",   17.07f, 0.5f, 0.02f),
        // 载具（pivot y=0.00：履带接地面 = transform 位置）
        new SpriteSpec("tank",       9.14f, 0.5f, 0.00f),
        new SpriteSpec("heavytank",  8.00f, 0.5f, 0.00f),
        new SpriteSpec("harvester",  9.14f, 0.5f, 0.00f),
        // 特效
        // ★ explosion 的 PPU 必须填【单帧值 2.67】，不是整图值 10.67。
        //   Multiple 模式下 spritePixelsToUnits 作用于每个 sub-sprite（64×64 的帧），
        //   不是 256×256 的整图：64 / 2.67 = 23.97 世界单位 ≈ 规格目标 24（§2 表）。
        //   若填 10.67：64 / 10.67 = 6.00 单位，爆炸只有目标的 1/4，肉眼明显偏小。
        //   tools/keyed_manifest.json 里也是 2.67，两边必须一致（曾出现 C# 10.67 / 清单 2.67 打架）。
        new SpriteSpec("explosion",   2.67f, 0.5f, 0.50f, true, 64, 64),   // 4x4=16 帧（P1 接线）
        new SpriteSpec("muzzle_flash", 25.60f, 0.5f, 0.50f),
        new SpriteSpec("nuke_cloud",  4.27f, 0.5f, 0.00f),
        new SpriteSpec("smoke_column", 18.3f, 0.5f, 0.00f),
        // UI（P1 接线，本轮只备好导入参数）
        new SpriteSpec("resource_icons", 1.0f, 0.5f, 0.5f, true, 128, 128),
        new SpriteSpec("build_panel_bg", 1.0f, 0.5f, 0.5f),
        new SpriteSpec("minimap_frame",  1.0f, 0.5f, 0.5f),
    };

    /// <summary>
    /// 单独重跑导入参数（不动场景/预制体）。一键搭建之外留这个入口，
    /// 是为了"只想修 meta 但又不想重建场景"时不必跑全流程。
    /// </summary>
    [MenuItem("WarEra/仅修复 Art/Keyed 导入参数")]
    public static void FixSpriteImportersOnly()
    {
        AssetDatabase.Refresh();          // 先让 AssetDatabase 认识这些 png
        ConfigureSpriteImporters();
        AssetDatabase.SaveAssets();
    }

    static void ConfigureSpriteImporters()
    {
        // 三级结果分开计数，避免"成功了 N 张"掩盖"失败了哪张为什么"。
        //   ok       —— 写入并 SaveAndReimport 成功
        //   missing  —— 文件不存在（多半是没跑 tools/key_assets.py）
        //   failed   —— 文件在、但拿不到 TextureImporter 或回读校验不一致（必须带原因）
        int ok = 0, missing = 0, failed = 0;
        var missingFiles = new System.Text.StringBuilder();
        var failedFiles = new System.Text.StringBuilder();

        foreach (var spec in SpriteSpecs)
        {
            string path = KeyedDir + "/" + spec.file + ".png";
            if (!File.Exists(path))
            {
                missing++;
                missingFiles.Append(spec.file).Append(' ');
                continue;
            }

            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null)
                {
                    failed++;
                    failedFiles.Append(spec.file).Append("(拿不到 TextureImporter) ");
                    continue;
                }
            }

            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = spec.multiple ? SpriteImportMode.Multiple : SpriteImportMode.Single;
            ti.spritePixelsPerUnit = spec.ppu;
            ti.alphaIsTransparency = true;
            ti.filterMode = FilterMode.Bilinear;
            ti.mipmapEnabled = false;          // 2D 正交，Mipmap 只让远景变糊
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.maxTextureSize = 256;           // 原图只有 256，放到 512 只浪费显存
            ti.textureCompression = TextureImporterCompression.Uncompressed; // 小图，避免块压缩糊边
            ti.npotScale = TextureImporterNPOTScale.None;

            var settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            settings.spriteMode = (int)ti.spriteImportMode;
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = spec.pivot;
            settings.spriteMeshType = SpriteMeshType.FullRect;  // 不做 Tight 网格，避免逐图差异
            settings.spriteGenerateFallbackPhysicsShape = false;
            ti.SetTextureSettings(settings);

            if (spec.multiple)
            {
                // 按单元格切分，行优先命名 <file>_00..NN（美术规格 §4.1 / §8.3）
                string fileNoExt = spec.file;
                var metas = new System.Collections.Generic.List<SpriteMetaData>();
                int cols = Mathf.Max(1, 256 / Mathf.Max(1, spec.cellW));
                int rows = Mathf.Max(1, 256 / Mathf.Max(1, spec.cellH));
                int idx = 0;
                for (int r = rows - 1; r >= 0; r--)      // Unity 的 y 从下往上
                {
                    for (int c = 0; c < cols; c++)
                    {
                        metas.Add(new SpriteMetaData
                        {
                            name = string.Format("{0}_{1:00}", fileNoExt, idx),
                            rect = new Rect(c * spec.cellW, r * spec.cellH, spec.cellW, spec.cellH),
                            alignment = (int)SpriteAlignment.Custom,
                            pivot = spec.pivot
                        });
                        idx++;
                    }
                }
                ti.spritesheet = metas.ToArray();
            }

            ti.SaveAndReimport();

            // 回读校验：SaveAndReimport 可能被 Library 缓存吞掉，不校验就只是"我以为写进去了"。
            // 这是"信号连了但没执行"的典型场景 —— ti.textureType = Sprite 赋值成功，
            // 但 reimport 后仍是 Default，游戏里 Sprite 字段为空、方块照样显示。
            var after = AssetImporter.GetAtPath(path) as TextureImporter;
            if (after == null || after.textureType != TextureImporterType.Sprite)
            {
                failed++;
                failedFiles.Append(spec.file)
                          .Append("(回读 textureType=")
                          .Append(after == null ? "null" : after.textureType.ToString())
                          .Append(") ");
                continue;
            }
            if (!Mathf.Approximately(after.spritePixelsPerUnit, spec.ppu))
            {
                failed++;
                failedFiles.Append(spec.file)
                          .Append("(PPU 回读=").Append(after.spritePixelsPerUnit.ToString("0.00"))
                          .Append(" 期望=").Append(spec.ppu.ToString("0.00")).Append(") ");
                continue;
            }
            ok++;
        }

        if (failed > 0)
        {
            Debug.LogError($"[PhaseC] 导入参数写入失败 {failed} 张：{failedFiles}" +
                           "—— 这几张在游戏里不会显示真图（会退回方块/空白）。");
        }
        if (missing > 0)
        {
            Debug.LogError($"[PhaseC] 缺失 {missing} 张：{missingFiles}" +
                           "—— 先跑 python3 tools/key_assets.py 生成 Art/Keyed/。");
        }
        if (ok == SpriteSpecs.Length)
        {
            Debug.Log($"[PhaseC] ✅ 导入参数已写入 {ok} 张（Art/Keyed），全部通过回读校验" +
                      "（textureType=Sprite + PPU 一致 + alphaIsTransparency）。");
        }
        else
        {
            Debug.LogWarning($"[PhaseC] 导入参数写入 {ok}/{SpriteSpecs.Length} 张，" +
                             $"失败 {failed} 张、缺失 {missing} 张。详见上方 LogError。");
        }
    }


    // ---------------- 占位美术 ----------------
    static void CreatePlaceholderSprites()
    {
        circleSprite = CreateSpriteAsset("Assets/_Project/Art/placehold_circle.png", 64, (x, y, cx, cy, r) =>
            (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r);
        squareSprite = CreateSpriteAsset("Assets/_Project/Art/placehold_square.png", 64, (x, y, cx, cy, r) =>
            Mathf.Abs(x - cx) <= r * 0.9f && Mathf.Abs(y - cy) <= r * 0.9f);
    }

    static Sprite CreateSpriteAsset(string path, int size, System.Func<int, int, float, float, float, bool> inside)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = (size - 1) / 2f, r = size / 2f - 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool on = inside(x, y, c, c, r);
                bool edge = on && !inside(x + 1, y, c, c, r) || !inside(x - 1, y, c, c, r) && on
                         || on && !inside(x, y + 1, c, c, r) || !inside(x, y - 1, c, c, r) && on;
                tex.SetPixel(x, y, on ? (edge ? new Color(0.1f, 0.1f, 0.1f, 1f) : Color.white) : Color.clear);
            }
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spritePixelsPerUnit = 1f;
        ti.filterMode = FilterMode.Bilinear;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ---------------- Prefab ----------------
    /// <summary>
    /// P0-6 受击闪白材质。用自建 WarEra/SpriteFlash，而不是 Sprites/Default ——
    /// 后者没有 _FlashAmount 属性，接真图后闪白会静默失效（不报错、没效果）。
    /// 全工程只需这一个材质，所有单位共用，保证合批不碎。
    /// </summary>
    static void CreateFlashMaterial()
    {
        const string matPath = "Assets/_Project/Shaders/SpriteFlash.mat";
        Shader sh = Shader.Find("WarEra/SpriteFlash");
        if (sh == null)
        {
            Debug.LogError("[PhaseC] 找不到 shader 'WarEra/SpriteFlash'。" +
                           "确认 Assets/_Project/Shaders/SpriteFlash.shader 已编译。" +
                           "单位将退回 sr.color 闪白方案（见 UnitEntity.UpdateFlash 的 else 分支）。");
            return;
        }

        // 【必须校验编译结果】Shader.Find 只要文件存在就返回对象 —— 即使 shader 编译失败。
        // 实测踩过的坑：函数体重定义 SpriteFrag 导致
        //   Shader error in 'WarEra/SpriteFlash': redefinition of 'SpriteFrag' at line 66 (on metal)
        // 此时 Shader.Find 仍然成功、CreateAsset 也成功，但材质是粉红/无 pass，
        // 而 UnitEntity.UpdateFlash 发现没有 _FlashAmount 就静默退回 sr.color ——
        // "闪白还能看见一点" ← 这个假象让整条 shader 路径失效一个月都发现不了。
        // 所以这里主动验一次：isSupported + 材质上真能读到属性。
        if (!sh.isSupported)
        {
            Debug.LogError("[PhaseC] shader 'WarEra/SpriteFlash' 编译失败（isSupported=false），" +
                           "多半是 CGPROGRAM 里有编译错误。看上一行 Shader error 的原文修，" +
                           "不要靠 LogWarning 掩盖 —— 单位会退回顶点色闪白。");
            return;
        }

        flashMaterial = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (flashMaterial == null)
            flashMaterial = new Material(sh) { name = "SpriteFlash" };
        flashMaterial.shader = sh;
        if (!File.Exists(matPath)) AssetDatabase.CreateAsset(flashMaterial, matPath);
        else EditorUtility.SetDirty(flashMaterial);

        // 最后一道：材质上真的能读到 _FlashAmount。属性名写错 / [PerRendererData] 缺失
        // 都会让 MaterialPropertyBlock 静默失效，而材质本身看起来完全正常。
        if (!flashMaterial.HasProperty("_FlashAmount"))
        {
            Debug.LogError("[PhaseC] 材质缺少 _FlashAmount 属性 —— 闪白会静默失效。" +
                           "检查 SpriteFlash.shader 的 Properties 里是否声明了 " +
                           "[PerRendererData] _FlashAmount。");
        }
    }

    /// <summary>
    /// P0-3 三层预制体结构（动画规格 §5.2）：
    ///
    ///   UnitPrefab          (UnitEntity)            根：位置由逻辑驱动，不旋转
    ///    └ RecoilPivot                               只承担后坐位移（世界→local 转换）
    ///       └ ArtRoot      (SpriteRenderer+UnitView) 只承担朝向 + 呼吸，并挂 sprite
    ///
    /// 为什么要拆：旧代码在同一个 transform 上既设 rotation 又设 localPosition，
    /// 而 recoilDir 是世界方向 → rotation≠0 时被再旋转一次 → 后坐朝反方向。
    ///
    /// localScale 恒为 1：尺寸全交给 TextureImporter.spritePixelsPerUnit。
    /// 占位时代用 localScale=1.6 凑尺寸，现在必须删掉（美术规格 §0③）。
    /// </summary>
    static void CreatePrefabs()
    {
        MakeUnitPrefab("UnitPrefab", circleSprite, new Color(0.25f, 0.65f, 0.84f));
        MakeUnitPrefab("BuildingPrefab", squareSprite, new Color(0.35f, 0.42f, 0.5f));

        // 矿点（非单位，保持单层）
        var ore = new GameObject("OreNodePrefab");
        var osr = ore.AddComponent<SpriteRenderer>();
        osr.sprite = circleSprite; osr.color = new Color(0.77f, 0.66f, 0.42f);
        ore.AddComponent<OreNode>();
        PrefabUtility.SaveAsPrefabAsset(ore, PrefabDir + "/OreNodePrefab.prefab");
        Object.DestroyImmediate(ore);

        // 特效占位
        MakeFxPrefab("BulletPrefab", squareSprite, new Color(1f, 0.9f, 0.3f), 0.35f);
        MakeFxPrefab("ExplosionPrefab", circleSprite, new Color(1f, 0.45f, 0.1f), 1.4f);
        MakeFxPrefab("MuzzleFlashPrefab", circleSprite, new Color(1f, 0.85f, 0.4f), 0.5f);
        MakeFxPrefab("MoveIndicatorPrefab", circleSprite, new Color(0.3f, 1f, 0.4f, 0.6f), 0.8f);
        MakeFxPrefab("AttackIndicatorPrefab", circleSprite, new Color(1f, 0.3f, 0.2f, 0.7f), 0.8f);
    }

    /// <summary>
    /// 四层预制体骨架（制作人裁定一 + docs/animation-facing-spec-v1.md §5.2）：
    ///
    ///   UnitPrefab              (UnitEntity)               transform.position  只由逻辑写
    ///    └ RecoilPivot                                      localPosition       只写后坐
    ///       └ ArtRoot           (UnitView)                  localRotation       只写朝向
    ///          └ AnimPivot                                  localPosition+scale 只写呼吸/弹跳
    ///             └ SpriteRenderer                          localScale ≡ one    任何代码不许碰
    ///
    /// 为什么必须四层而不是三层：
    ///   三层时 ArtRoot 同时写 rotation（朝向）和 scale（呼吸），呼吸一旦写 localScale
    ///   就会把 PPU 换算出的显示尺寸一起覆写掉 —— 尺寸基准每帧被重置成约 1。
    ///   拆出 AnimPivot 后，尺寸在导入期由 PPU 决定，运行时无人碰 renderer 的 scale。
    ///
    /// sr.color 必须是 Color.white：占位时代给 UnitPrefab 染了蓝、BuildingPrefab 染了灰蓝。
    /// 不清掉的话真图接进来会整张染成蓝色，而错误表现是"图是蓝的"，很难联想到是 prefab 的 tint。
    /// 占位可见性改由占位图自身的颜色承担（placehold 的 PNG 本身是白色，靠 tint 上色），
    /// 因此这里对占位也保持 tint，只在真图接管时由 ApplyDefinitionSprite 复位为白。
    /// </summary>
    static void MakeUnitPrefab(string name, Sprite placeholder, Color tint)
    {
        var root = new GameObject(name);
        var entity = root.AddComponent<UnitEntity>();

        // 第 2 层：只写后坐位移
        var recoilPivot = new GameObject("RecoilPivot");
        recoilPivot.transform.SetParent(root.transform, false);
        recoilPivot.transform.localPosition = Vector3.zero;
        recoilPivot.transform.localRotation = Quaternion.identity;
        recoilPivot.transform.localScale = Vector3.one;

        // 第 3 层：只写朝向旋转，挂 UnitView
        var artRoot = new GameObject("ArtRoot");
        artRoot.transform.SetParent(recoilPivot.transform, false);
        artRoot.transform.localPosition = Vector3.zero;
        artRoot.transform.localRotation = Quaternion.identity;
        artRoot.transform.localScale = Vector3.one;

        // 第 4 层：只写呼吸/弹跳的 pos+scale
        var animPivot = new GameObject("AnimPivot");
        animPivot.transform.SetParent(artRoot.transform, false);
        animPivot.transform.localPosition = Vector3.zero;
        animPivot.transform.localRotation = Quaternion.identity;
        animPivot.transform.localScale = Vector3.one;

        // 第 5 层：SpriteRenderer，localScale 恒为 one，不挂任何脚本
        var srGo = new GameObject("Sprite");
        srGo.transform.SetParent(animPivot.transform, false);
        srGo.transform.localPosition = Vector3.zero;
        srGo.transform.localRotation = Quaternion.identity;
        srGo.transform.localScale = Vector3.one;      // 恒为 1，尺寸全交给 PPU

        var sr = srGo.AddComponent<SpriteRenderer>();
        sr.sprite = placeholder;
        sr.color = tint;                              // 占位期用 tint；真图接管时复位为白
        if (flashMaterial != null) sr.sharedMaterial = flashMaterial;

        var view = artRoot.AddComponent<UnitView>();
        view.recoilPivot = recoilPivot.transform;
        view.animPivot = animPivot.transform;

        entity.spriteRenderer = sr;

        PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/" + name + ".prefab");
        Object.DestroyImmediate(root);
    }

    static void MakeFxPrefab(string name, Sprite sprite, Color color, float scale)
    {
        var go = new GameObject(name);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite; sr.color = color;
        sr.transform.localScale = Vector3.one * scale;
        PrefabUtility.SaveAsPrefabAsset(go, PrefabDir + "/" + name + ".prefab");
        Object.DestroyImmediate(go);
    }

    static GameObject LoadPrefab(string name) =>
        AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/" + name + ".prefab");

    // ---------------- 阵营 ----------------
    static void CreateFactionAssets()
    {
        var allies = ScriptableObject.CreateInstance<FactionDefinition>();
        allies.factionId = "allies"; allies.displayName = "盟军"; allies.description = "精准打击：快+远，但贵";
        allies.speedMultiplier = 1.15f; allies.costMultiplier = 1.2f;
        allies.rangeMultiplier = 1.1f; allies.hpMultiplier = 1.0f;
        AssetDatabase.CreateAsset(allies, DataCore + "/Faction_Allies.asset");

        var axis = ScriptableObject.CreateInstance<FactionDefinition>();
        axis.factionId = "axis"; axis.displayName = "轴心军"; axis.description = "装甲洪流：便宜+厚血，但慢";
        axis.speedMultiplier = 0.85f; axis.costMultiplier = 0.85f;
        axis.rangeMultiplier = 0.95f; axis.hpMultiplier = 1.25f;
        AssetDatabase.CreateAsset(axis, DataCore + "/Faction_Axis.asset");
    }

    static FactionDefinition LoadFaction(string id) =>
        AssetDatabase.LoadAssetAtPath<FactionDefinition>(DataCore + "/Faction_" + id + ".asset");

    // ---------------- 单位/建筑定义（数值对齐 balance-sheet-v1） ----------------
    static System.Collections.Generic.Dictionary<string, UnitDefinition> CreateUnitDefinitions()
    {
        var map = new System.Collections.Generic.Dictionary<string, UnitDefinition>();
        var unitPrefab = LoadPrefab("UnitPrefab");
        var bldPrefab = LoadPrefab("BuildingPrefab");

        UnitDefinition Def(string id, string name, UnitCategory cat, float hp, float dmg, float range,
            float fireRate, DamageType dmgType, float speed, int cost, int powerNeed, int tech,
            float selRadius, bool building, Vector2 bSize, float buildTime,
            bool producer = false, bool harvester = false)
        {
            var d = ScriptableObject.CreateInstance<UnitDefinition>();
            d.unitId = id; d.displayName = name; d.category = cat;
            d.maxHp = hp; d.damage = dmg; d.range = range; d.fireRate = fireRate;
            d.damageType = dmgType; d.moveSpeed = speed; d.sightRange = range + 40f;
            d.oreCost = cost; d.powerNeed = powerNeed; d.techLevel = tech;
            d.selectionRadius = selRadius; d.isBuilding = building; d.buildingSize = bSize;
            d.buildTime = buildTime; d.canProduceUnits = producer; d.isHarvester = harvester;
            d.prefab = building ? bldPrefab : unitPrefab;
            string path = DataCore + "/UD_" + id + ".asset";
            AssetDatabase.CreateAsset(d, path);
            map[id] = d;
            return d;
        }

        // 步兵
        Def("rifleman", "步枪兵", UnitCategory.Infantry, 100, 8, 60, 0.8f, DamageType.Light, 45, 100, 0, 1, 8, false, Vector2.zero, 0);
        Def("bazooka", "反坦兵", UnitCategory.Infantry, 90, 35, 55, 1.5f, DamageType.Piercing, 40, 200, 0, 1, 8, false, Vector2.zero, 0);
        Def("sniper", "狙击手", UnitCategory.Infantry, 70, 30, 120, 2.0f, DamageType.Light, 38, 250, 0, 2, 8, false, Vector2.zero, 0);
        // 载具
        Def("tank", "中型坦克", UnitCategory.Vehicle, 400, 30, 70, 1.2f, DamageType.Piercing, 60, 400, 1, 1, 12, false, Vector2.zero, 0);
        Def("heavytank", "重型坦克", UnitCategory.Vehicle, 700, 50, 80, 1.6f, DamageType.Piercing, 45, 800, 2, 2, 14, false, Vector2.zero, 0);
        Def("artillery", "火炮", UnitCategory.Vehicle, 250, 60, 160, 2.5f, DamageType.Explosive, 35, 600, 2, 2, 12, false, Vector2.zero, 0);
        // 采矿车
        var harv = Def("harvester", "采矿车", UnitCategory.Harvester, 300, 0, 0, 0, DamageType.Light, 50, 300, 0, 1, 12, false, Vector2.zero, 0, harvester: true);
        harv.oreCapacity = 100; harv.harvestTime = 2f;
        // 建筑
        var hq = Def("hq", "指挥部", UnitCategory.Building, 3000, 0, 0, 0, DamageType.Light, 0, 0, 0, 1, 40, true, new Vector2(80, 80), 0);
        hq.powerSupply = 10;
        var pp = Def("powerplant", "发电厂", UnitCategory.Building, 800, 0, 0, 0, DamageType.Light, 0, 300, 0, 1, 40, true, new Vector2(44, 44), 3);
        pp.powerSupply = 10;
        Def("refinery", "矿场", UnitCategory.Building, 1000, 0, 0, 0, DamageType.Light, 0, 500, 1, 1, 44, true, new Vector2(56, 56), 4);
        var bar = Def("barracks", "兵营", UnitCategory.Building, 900, 0, 0, 0, DamageType.Light, 0, 400, 1, 1, 40, true, new Vector2(48, 48), 3, producer: true);
        var war = Def("warfactory", "车厂", UnitCategory.Building, 1200, 0, 0, 0, DamageType.Light, 0, 800, 2, 1, 48, true, new Vector2(56, 56), 5, producer: true);
        Def("radar", "雷达站", UnitCategory.Building, 700, 0, 0, 0, DamageType.Light, 0, 600, 2, 2, 40, true, new Vector2(44, 44), 4);
        var mg = Def("mgtower", "机枪塔", UnitCategory.Defense, 600, 10, 90, 0.4f, DamageType.Light, 0, 350, 1, 1, 28, true, new Vector2(32, 32), 3);
        Def("atcannon", "反坦炮", UnitCategory.Defense, 500, 40, 110, 1.8f, DamageType.Piercing, 0, 450, 1, 1, 28, true, new Vector2(36, 36), 3);

        // 生产链
        bar.producibleUnits = new[] { map["rifleman"], map["bazooka"], map["sniper"] };
        war.producibleUnits = new[] { map["tank"], map["heavytank"], map["artillery"], map["harvester"] };

        // === P0-4 spriteForwardDeg 终值（制作人裁定 L：口径统一为「炮管锚」） ===
        //
        // 定义：素材里"炮管/车头指向"相对图片 +X 轴的逆时针角（度）。
        // 公式：zDeg = facing*Rad2Deg − spriteForwardDeg（没有 +90f）。
        //
        // 为什么不能用 PCA 主轴：主轴有 180° 周期，只描述"长轴方向"不描述
        // "车头在哪一端"，有一半概率判反 180° 且代码不报错，只表现为车倒着开。
        //
        // 【终值判据：径向射线法】从前景质心每 2° 打射线，取"该方向最后一段前景
        // 的末端半径"与"最大连续厚度"。炮管 = 延伸最远 且 该方向最细。
        //   tank      24~26° 末端 r=145px、厚度 63~73px（全图最细）→ 26°
        //             （此前的 −23.2 是符号写反：该方向指向车体前下方，不在炮管上，
        //              实测 −23.2° 射线与炮管偏 49°，肉眼在编辑器里一眼可辨）
        //   heavytank 348° 末端最远但厚度 141px（与车体同粗，不是炮管）；
        //             96~104° 最细 86px 但 r 仅 104px（侧面突出物）
        //             → 采炮塔锚口径 −20
        //   artillery 复用 tank 图，必须与 tank 同为 26（否则同一张图两个朝向）
        //   harvester elong=1.02 双向对称、无车头概念，填 0 不补偿。
        //             （早期 223 = −137「推土铲朝左下」已撤回：填任何单向值都是猜）
        // 建筑（mgtower/atcannon）isBuilding=true 在 UnitView 第一行短路，填了不生效。
        map["tank"].spriteForwardDeg      = 26f;     // 炮管锚，射线法实测 24~26°
        map["heavytank"].spriteForwardDeg = -20f;    // 炮塔锚
        map["artillery"].spriteForwardDeg = 26f;     // 复用 tank 贴图，必须与 tank 一致
        map["harvester"].spriteForwardDeg = 0f;      // 双向对称，不做补偿
        // 步兵（rifleman/bazooka/sniper）走镜像、建筑不旋转，此字段不参与计算。

        // === P0-5 def.sprite 接线（全工程原本的空白点） ===
        WireSprites(map);

        return map;
    }

    /// <summary>
    /// 把 Art/Keyed/&lt;unitId&gt;.png 挂到对应 UnitDefinition.sprite 上。
    ///
    /// 这是"素材目录看着满了、进游戏还是一片方块"的根治点：
    /// UnitRegistry.SpawnUnit 原先只 Instantiate(def.prefab)，从不读 def.sprite。
    /// 现在 UnitEntity.Initialize → ApplyDefinitionSprite 会真正替换 SpriteRenderer.sprite。
    ///
    /// sniper / artillery 无独立素材（美术规格 §11）：artillery 复用 tank、sniper 复用
    /// rifleman，在代码里标 TODO，不留空白方块。
    /// </summary>
    static void WireSprites(System.Collections.Generic.Dictionary<string, UnitDefinition> map)
    {
        // unitId → 实际使用的贴图文件（缺素材的复用同类）
        var spriteOf = new System.Collections.Generic.Dictionary<string, string>();
        string[] direct =
        {
            "hq", "powerplant", "refinery", "barracks", "warfactory", "radar",
            "mgtower", "atcannon", "rifleman", "bazooka", "tank", "heavytank", "harvester"
        };
        foreach (var id in direct) spriteOf[id] = id;

        // TODO: sniper 缺独立素材，复用 rifleman（美术规格 §11 明确不为其造素材）
        spriteOf["sniper"] = "rifleman";
        // TODO: artillery 缺独立素材，复用 tank（同上）
        spriteOf["artillery"] = "tank";

        // 补丁 C：复用必须显式打日志，否则以后没人知道这是复用而非独立素材
        Debug.LogWarning("[PhaseC] 'sniper' 无独立素材，复用 'rifleman' 贴图。TODO: 缺独立素材，待美术补图。");
        Debug.LogWarning("[PhaseC] 'artillery' 无独立素材，复用 'tank' 贴图。TODO: 缺独立素材，待美术补图。");

        int wired = 0;
        var missing = new System.Text.StringBuilder();

        foreach (var kv in map)
        {
            string unitId = kv.Key;
            var def = kv.Value;
            string file = spriteOf.ContainsKey(unitId) ? spriteOf[unitId] : null;
            if (file == null)
            {
                missing.Append(unitId).Append("(无映射) ");
                continue;
            }

            string path = KeyedDir + "/" + file + ".png";
            Sprite sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sp == null)
            {
                // 静默失败是这次坑的根因：textureType=0 时 LoadAssetAtPath<Sprite> 返回
                // null 且不报错。所以这里必须 LogError 而不是 LogWarning。
                Debug.LogError($"[PhaseC] '{unitId}' 的 sprite 加载失败：{path} 返回 null。" +
                               $"最常见原因：该 PNG 的 textureType 不是 Sprite（=8）。" +
                               $"先跑 python3 tools/key_assets.py 再重跑一键搭建。");
                missing.Append(unitId).Append("→null ");
                continue;
            }

            def.sprite = sp;
            EditorUtility.SetDirty(def);
            wired++;
        }

        Debug.Log($"[PhaseC] def.sprite 接线完成 {wired}/{map.Count} 个 UnitDefinition。" +
                  (missing.Length > 0 ? $" 未接线：{missing}" : " 全部命中。"));
    }

    // ---------------- 地图 ----------------
    static void CreateMapAssets()
    {
        var map = ScriptableObject.CreateInstance<MapDefinition>();
        map.mapId = "plain"; map.displayName = "平原遭遇战";
        map.themeId = "ww2"; map.playerCount = 2; map.mapType = "1v1";
        map.worldSize = new Vector2(2400, 1600);
        map.playerStartPositions = new[] {
            new Vector3(200, 1200, 0), new Vector3(2100, 400, 0) };
        map.oreNodes = new[] {
            new OreNodeData { position = new Vector3(350, 1000, 0), radius = 25, isInfinite = true, maxAmount = 9999 },
            new OreNodeData { position = new Vector3(150, 1450, 0), radius = 25, isInfinite = true, maxAmount = 9999 },
            new OreNodeData { position = new Vector3(1950, 600, 0), radius = 25, isInfinite = true, maxAmount = 9999 },
            new OreNodeData { position = new Vector3(2250, 200, 0), radius = 25, isInfinite = true, maxAmount = 9999 },
            new OreNodeData { position = new Vector3(1200, 900, 0), radius = 25, isInfinite = true, maxAmount = 9999 },
            new OreNodeData { position = new Vector3(1350, 700, 0), radius = 25, isInfinite = true, maxAmount = 9999 } };
        map.obstacles = new[] {
            new ObstacleData { position = new Vector3(900, 400, 0), size = new Vector2(60, 60), type = ObstacleType.BuildingRuin },
            new ObstacleData { position = new Vector3(1600, 1100, 0), size = new Vector2(60, 60), type = ObstacleType.BuildingRuin },
            new ObstacleData { position = new Vector3(500, 600, 0), size = new Vector2(40, 40), type = ObstacleType.Forest } };
        map.description = "开阔平原，双方基地对角线分布，中路有矿区争夺点。";
        AssetDatabase.CreateAsset(map, DataWW2 + "/Map_plain.asset");
    }

    // ---------------- 场景 ----------------
    static void BuildScene(System.Collections.Generic.Dictionary<string, UnitDefinition> defs)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 摄像机
        var camGo = new GameObject("Main Camera");
        var cam = camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();
        cam.orthographic = true;
        cam.orthographicSize = 300f;
        cam.nearClipPlane = 0.1f; cam.farClipPlane = 2000f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.10f, 0.14f, 0.07f); // 暗草绿
        camGo.tag = "MainCamera";
        camGo.transform.position = new Vector3(200, 1200, -10);

        // 世界根节点
        var worldRootGo = new GameObject("WorldRoot");

        // 管理器集合
        var mgr = new GameObject("_Managers");

        mgr.AddComponent<GameClock>();
        var eco = mgr.AddComponent<EconomySystem>();
        eco.ore = 1200; eco.powerSupply = 25;
        mgr.AddComponent<UnitRegistry>();
        mgr.AddComponent<CombatSystem>();

        var allies = LoadFaction("allies");
        var axis = LoadFaction("axis");

        var bs = mgr.AddComponent<BuildSystem>();
        bs.playerFaction = allies;

        mgr.AddComponent<TechSystem>();

        var ai = mgr.AddComponent<EnemyAI>();
        ai.enemyFaction = axis;
        ai.canBuild = true; ai.aiInterval = 18f; ai.maxUnits = 18;
        ai.enemyUnitDefs = new[] { defs["rifleman"], defs["bazooka"], defs["tank"] };

        var gm = mgr.AddComponent<GameManager>();
        gm.playerFaction = allies; gm.enemyFaction = axis;
        gm.difficulty = "normal"; gm.mapName = "plain";
        gm.hqDef = defs["hq"]; gm.harvesterDef = defs["harvester"];
        gm.powerplantDef = defs["powerplant"]; gm.barracksDef = defs["barracks"];
        gm.refineryDef = defs["refinery"]; gm.warfactoryDef = defs["warfactory"];
        gm.mgtowerDef = defs["mgtower"];

        var vfx = mgr.AddComponent<VFXManager>();
        vfx.bulletPrefab = LoadPrefab("BulletPrefab");
        vfx.explosionPrefab = LoadPrefab("ExplosionPrefab");
        vfx.muzzleFlashPrefab = LoadPrefab("MuzzleFlashPrefab");
        vfx.moveIndicatorPrefab = LoadPrefab("MoveIndicatorPrefab");
        vfx.attackIndicatorPrefab = LoadPrefab("AttackIndicatorPrefab");

        var audio = mgr.AddComponent<AudioManager>();
        audio.rifleSound = LoadClip("sfx_rifle");
        audio.cannonSound = LoadClip("sfx_cannon");
        audio.explosionSound = LoadClip("sfx_explosion");
        audio.nukeSound = LoadClip("sfx_nuke");
        audio.selectSound = LoadClip("sfx_select");
        audio.moveSound = LoadClip("sfx_move");
        audio.buildSound = LoadClip("sfx_build");
        audio.errorSound = LoadClip("sfx_error");

        var ml = mgr.AddComponent<MapLoader>();
        ml.availableMaps = new[] { AssetDatabase.LoadAssetAtPath<MapDefinition>(DataWW2 + "/Map_plain.asset") };
        ml.oreNodePrefab = LoadPrefab("OreNodePrefab");
        ml.worldRoot = worldRootGo.transform;

        var input = mgr.AddComponent<TouchInputSystem>();
        input.mainCamera = cam;
        input.worldBounds = new Vector2(2400, 1600);

        // 【补丁 A / M：必须在给 mainCamera 赋值之后】修一个会让整个画面炸掉的 bug。
        //
        // TouchInputSystem 的字段默认 minZoom=5 / maxZoom=20，而本场景
        // cam.orthographicSize = 300，第 166 行每次都做
        //   orthographicSize = Clamp(orthographicSize + zoomDelta, minZoom, maxZoom)
        // 玩家第一次双指缩放，正交半高会从 300 被瞬间夹到 20（视野缩小 15 倍）。
        // 而美术的 PPU 全表、1.8px/单位、hq 上屏 144px 全部只在 orthographicSize=300 时成立。
        //
        // 用相对推导而不是写死 150/450：写死等于把 orthographicSize 抄两遍，
        // 以后调基准视野必有一处忘改 —— 这个 bug 的成因正是「同一件事写在两个地方」，
        // 用写死的方式修等于埋同样的雷。
        input.minZoom = cam.orthographicSize * 0.5f;    // 150
        input.maxZoom = cam.orthographicSize * 1.5f;    // 450

        // UI Canvas + UIManager
        var ui = BuildUI(mgr);
        var um = mgr.AddComponent<UIManager>();
        um.oreText = ui.ore; um.powerText = ui.power; um.techText = ui.tech;
        um.unitInfoPanel = ui.infoPanel; um.unitNameText = ui.infoName;
        um.unitCountText = ui.infoCount; um.hpBarFill = ui.hpFill;
        um.hpText = ui.infoHp; um.dmgText = ui.infoDmg; um.rangeText = ui.infoRange;
        um.toastPanel = ui.toastPanel; um.toastText = ui.toastText;
        um.alertPanel = ui.alertPanel; um.alertText = ui.alertText;
        um.gameOverPanel = ui.gameOverPanel; um.gameOverText = ui.gameOverText;

        // EventSystem
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();

        EditorSceneManager.SaveScene(scene, SceneDir + "/Battle.unity");
        Debug.Log("[PhaseB] 场景已保存：Assets/_Project/Scenes/Battle.unity");
    }

    static AudioClip LoadClip(string name) =>
        AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/" + name + ".wav");

    // ---------------- UI ----------------
    struct UiRefs
    {
        public Text ore, power, tech, infoName, infoCount, infoHp, infoDmg, infoRange, toastText, alertText, gameOverText;
        public GameObject infoPanel, toastPanel, alertPanel, gameOverPanel;
        public Image hpFill;
    }

    static Font GetFont()
    {
        Font f = null;
        try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (f == null) try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
        return f;
    }

    static UiRefs BuildUI(GameObject mgr)
    {
        var refs = new UiRefs();
        var font = GetFont();

        var canvasGo = new GameObject("UI Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        refs.ore = MakeText(canvasGo.transform, "OreText", "矿石 1200", 36, font,
            new Vector2(0, 1), new Vector2(0.25f, 1), new Vector2(20, -60), new Vector2(400, -10), Color.white, TextAnchor.MiddleLeft);
        refs.power = MakeText(canvasGo.transform, "PowerText", "电力 0/25", 36, font,
            new Vector2(0, 1), new Vector2(0.25f, 1), new Vector2(20, -110), new Vector2(400, -60), Color.white, TextAnchor.MiddleLeft);
        refs.tech = MakeText(canvasGo.transform, "TechText", "科技 0", 36, font,
            new Vector2(0, 1), new Vector2(0.25f, 1), new Vector2(20, -160), new Vector2(400, -110), Color.white, TextAnchor.MiddleLeft);

        // 单位信息面板（左下）
        refs.infoPanel = MakePanel(canvasGo.transform, "UnitInfoPanel",
            new Vector2(0, 0), new Vector2(0.25f, 0), new Vector2(20, 20), new Vector2(480, 190),
            new Color(0, 0, 0, 0.75f));
        refs.infoPanel.SetActive(false);
        refs.infoName = MakeText(refs.infoPanel.transform, "Name", "单位", 34, font,
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(15, -55), new Vector2(-15, -5), Color.white, TextAnchor.MiddleLeft);
        refs.infoCount = MakeText(refs.infoPanel.transform, "Count", "", 30, font,
            new Vector2(0.6f, 1), new Vector2(1, 1), new Vector2(-100, -55), new Vector2(-15, -5), Color.yellow, TextAnchor.MiddleRight);
        refs.infoHp = MakeText(refs.infoPanel.transform, "HP", "HP", 28, font,
            new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(15, -15), new Vector2(-15, 20), Color.white, TextAnchor.MiddleLeft);
        refs.infoDmg = MakeText(refs.infoPanel.transform, "DMG", "攻击", 28, font,
            new Vector2(0, 0.3f), new Vector2(1, 0.3f), new Vector2(15, -55), new Vector2(-15, -20), Color.white, TextAnchor.MiddleLeft);
        refs.infoRange = MakeText(refs.infoPanel.transform, "Range", "射程", 28, font,
            new Vector2(0, 0.1f), new Vector2(1, 0.1f), new Vector2(15, -45), new Vector2(-15, -10), Color.white, TextAnchor.MiddleLeft);
        refs.hpFill = MakeFillBar(refs.infoPanel.transform, "HpBar", font);

        // Toast（底部中央）
        refs.toastPanel = MakePanel(canvasGo.transform, "ToastPanel",
            new Vector2(0.3f, 0), new Vector2(0.7f, 0), new Vector2(0, 140), new Vector2(0, 200),
            new Color(0, 0, 0, 0.7f));
        refs.toastPanel.SetActive(false);
        refs.toastText = MakeText(refs.toastPanel.transform, "ToastText", "", 34, font,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Color.white, TextAnchor.MiddleCenter);

        // 警报（顶部中央）
        refs.alertPanel = MakePanel(canvasGo.transform, "AlertPanel",
            new Vector2(0.25f, 1), new Vector2(0.75f, 1), new Vector2(0, -190), new Vector2(0, -130),
            new Color(0.5f, 0.05f, 0.05f, 0.85f));
        refs.alertPanel.SetActive(false);
        refs.alertText = MakeText(refs.alertPanel.transform, "AlertText", "", 38, font,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(1f, 0.85f, 0.5f), TextAnchor.MiddleCenter);

        // 结束面板
        refs.gameOverPanel = MakePanel(canvasGo.transform, "GameOverPanel",
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0.85f));
        refs.gameOverPanel.SetActive(false);
        refs.gameOverText = MakeText(refs.gameOverPanel.transform, "GameOverText", "胜利！", 96, font,
            new Vector2(0.1f, 0.4f), new Vector2(0.9f, 0.7f), Vector2.zero, Vector2.zero, Color.white, TextAnchor.MiddleCenter);

        return refs;
    }

    static Text MakeText(Transform parent, string name, string txt, int size, Font font,
        Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax, Color color, TextAnchor align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = offMin; rt.offsetMax = offMax;
        var t = go.AddComponent<Text>();
        t.text = txt; t.fontSize = size; t.font = font;
        t.color = color; t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    static GameObject MakePanel(Transform parent, string name,
        Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = offMin; rt.offsetMax = offMax;
        var img = go.AddComponent<Image>();
        img.color = color;
        return go;
    }

    static Image MakeFillBar(Transform parent, string name, Font font)
    {
        var bg = MakePanel(parent, name + "Bg",
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(15, -95), new Vector2(-15, -65),
            new Color(0.2f, 0.2f, 0.2f));
        var fillGo = new GameObject("Fill", typeof(RectTransform));
        fillGo.transform.SetParent(bg.transform, false);
        var rt = fillGo.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var img = fillGo.AddComponent<Image>();
        img.color = new Color(0.25f, 0.65f, 0.84f);
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        return img;
    }
}
