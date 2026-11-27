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

    static Sprite circleSprite;
    static Sprite squareSprite;

    [MenuItem("WarEra/阶段B 一键搭建")]
    public static void Setup()
    {
        EnsureFolders();
        CreatePlaceholderSprites();
        CreatePrefabs();
        CreateFactionAssets();
        var defs = CreateUnitDefinitions();
        CreateMapAssets();
        BuildScene(defs);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[PhaseB] ✅ 搭建完成。打开 Assets/_Project/Scenes/Battle.unity 按 Play 试玩。" +
                  "操作：左键点选单位，拖动框选，右键/点击下达移动攻击，底部按钮建造。");
    }

    static void EnsureFolders()
    {
        foreach (var dir in new[] { DataCore, DataWW2, PrefabDir, SceneDir, "Assets/_Project/Scenes" })
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
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
    static void CreatePrefabs()
    {
        // 单位占位（圆）
        var unit = new GameObject("UnitPrefab");
        var us = unit.AddComponent<SpriteRenderer>();
        us.sprite = circleSprite; us.color = new Color(0.25f, 0.65f, 0.84f);
        unit.AddComponent<UnitEntity>();
        PrefabUtility.SaveAsPrefabAsset(unit, PrefabDir + "/UnitPrefab.prefab");
        Object.DestroyImmediate(unit);

        // 建筑占位（方）
        var bld = new GameObject("BuildingPrefab");
        var bs = bld.AddComponent<SpriteRenderer>();
        bs.sprite = squareSprite; bs.color = new Color(0.35f, 0.42f, 0.5f);
        bs.transform.localScale = Vector3.one * 1.6f;
        bld.AddComponent<UnitEntity>();
        PrefabUtility.SaveAsPrefabAsset(bld, PrefabDir + "/BuildingPrefab.prefab");
        Object.DestroyImmediate(bld);

        // 矿点
        var ore = new GameObject("OreNodePrefab");
        var os = ore.AddComponent<SpriteRenderer>();
        os.sprite = circleSprite; os.color = new Color(0.77f, 0.66f, 0.42f);
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
        return map;
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
