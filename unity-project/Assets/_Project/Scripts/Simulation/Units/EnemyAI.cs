// ================================================================
// EnemyAI.cs — 敌方 AI 控制器
// 三波递进出兵 + 基地扩展 + 持续施压
// ================================================================
using UnityEngine;

public class EnemyAI : MonoBehaviour
{
    public static EnemyAI Instance { get; private set; }

    [Header("Faction")]
    public FactionDefinition enemyFaction;
    public UnitDefinition[] enemyUnitDefs; // 可生产的单位定义

    [Header("Difficulty")]
    public float aiInterval = 18f;
    public float aiScale = 1f;
    public int maxUnits = 18;
    public bool canBuild = true;

    [Header("Base Position")]
    public Vector3 basePosition = new Vector3(2100, 400, 0);

    private float timer = 0f;
    private int phase = 0;
    private float buildTimer = 0f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update()
    {
        float dt = GameClock.Instance?.DeltaTime ?? Time.deltaTime;
        timer += dt;

        if (canBuild)
        {
            buildTimer += dt;
            if (buildTimer > 15f)
            {
                buildTimer = 0f;
                TryExpandBase();
            }
        }

        // 三波递进
        if (timer > aiInterval && phase == 0)
        {
            SpawnWave("rifleman", Mathf.RoundToInt(4 * aiScale));
            phase = 1;
            UIManager.Instance?.ShowAlert("敌方步兵来袭！");
            AudioManager.Instance?.PlayAlertSound();
        }

        if (timer > aiInterval * 2 && phase == 1)
        {
            SpawnWave("bazooka", Mathf.RoundToInt(2 * aiScale));
            phase = 2;
            UIManager.Instance?.ShowAlert("敌方反装甲部队来袭！");
            AudioManager.Instance?.PlayAlertSound();
        }

        if (timer > aiInterval * 3 && phase == 2)
        {
            SpawnWave("tank", Mathf.RoundToInt(2 * aiScale));
            phase = 3;
            UIManager.Instance?.ShowAlert("敌方装甲部队来袭！");
            AudioManager.Instance?.PlayAlertSound();
        }

        // 持续出兵
        if (phase >= 3 && timer > aiInterval * 3 + 10f)
        {
            if (UnitRegistry.Instance.GetEnemyUnitCount() < maxUnits && Random.value < 0.02f)
            {
                string[] types = { "rifleman", "rifleman", "bazooka", "tank", "tank" };
                if (timer > 90f) types = new[] { "rifleman", "bazooka", "tank", "tank", "heavytank" };
                string type = types[Random.Range(0, types.Length)];
                SpawnUnit(type);
            }
        }
    }

    void SpawnWave(string unitId, int count)
    {
        UnitDefinition def = FindDef(unitId);
        if (def == null) return;

        for (int i = 0; i < count; i++)
        {
            Vector3 pos = basePosition + new Vector3(Random.Range(-20, 20), i * 25, 0);
            UnitEntity unit = UnitRegistry.Instance.SpawnUnit(def, enemyFaction, pos, true);
            if (unit != null)
            {
                unit.CommandMove(new Vector3(250, 1200, 0));
            }
        }
    }

    void SpawnUnit(string unitId)
    {
        UnitDefinition def = FindDef(unitId);
        if (def == null) return;

        Vector3 pos = basePosition + new Vector3(0, Random.Range(-30, 30), 0);
        UnitEntity unit = UnitRegistry.Instance.SpawnUnit(def, enemyFaction, pos, true);
        if (unit != null)
        {
            unit.CommandMove(new Vector3(
                200 + Random.Range(0, 200),
                1000 + Random.Range(0, 200),
                0));
        }
    }

    void TryExpandBase()
    {
        if (EconomySystem.Instance.enemyOre < 350) return;

        // 检查已有建筑
        bool hasRefinery = false;
        bool hasWarfactory = false;
        int towerCount = 0;

        foreach (var u in UnitRegistry.Instance.GetEnemyUnits())
        {
            if (u == null || u.isDead || !u.def.isBuilding) continue;
            if (u.def.unitId == "refinery") hasRefinery = true;
            if (u.def.unitId == "warfactory") hasWarfactory = true;
            if (u.def.unitId == "mgtower") towerCount++;
        }

        UnitDefinition toBuild = null;

        if (!hasRefinery && EconomySystem.Instance.enemyOre >= 500)
        {
            toBuild = FindDef("refinery");
            EconomySystem.Instance.enemyOre -= 500;
        }
        else if (!hasWarfactory && EconomySystem.Instance.enemyOre >= 800)
        {
            toBuild = FindDef("warfactory");
            EconomySystem.Instance.enemyOre -= 800;
        }
        else if (towerCount < 6 && EconomySystem.Instance.enemyOre >= 350)
        {
            toBuild = FindDef("mgtower");
            EconomySystem.Instance.enemyOre -= 350;
        }

        if (toBuild != null)
        {
            Vector3 pos = basePosition + new Vector3(Random.Range(-100, 100), Random.Range(-50, 50), 0);
            UnitRegistry.Instance.SpawnUnit(toBuild, enemyFaction, pos, true);
        }
    }

    UnitDefinition FindDef(string unitId)
    {
        if (enemyUnitDefs == null) return null;
        foreach (var d in enemyUnitDefs)
        {
            if (d != null && d.unitId == unitId) return d;
        }
        return null;
    }

    /// <summary>
    /// 设置难度
    /// </summary>
    public void SetDifficulty(string difficulty)
    {
        switch (difficulty)
        {
            case "easy":
                aiInterval = 25f; aiScale = 0.6f; maxUnits = 10; canBuild = false; break;
            case "normal":
                aiInterval = 18f; aiScale = 1.0f; maxUnits = 18; canBuild = true; break;
            case "hard":
                aiInterval = 12f; aiScale = 1.4f; maxUnits = 28; canBuild = true; break;
        }
    }
}
