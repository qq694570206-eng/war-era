// ================================================================
// GameManager.cs — 游戏总控
// 初始化所有系统、管理游戏流程、胜负判定
// ================================================================
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Game Config")]
    public FactionDefinition playerFaction;
    public FactionDefinition enemyFaction;
    public string difficulty = "normal";
    public string mapName = "plain";

    [Header("World")]
    public Vector3 playerBasePosition = new Vector3(200, 1200, 0);
    public Vector3 enemyBasePosition = new Vector3(2100, 400, 0);

    [Header("References")]
    public UnitDefinition hqDef;
    public UnitDefinition harvesterDef;
    public UnitDefinition powerplantDef;
    public UnitDefinition barracksDef;
    public UnitDefinition refineryDef;
    public UnitDefinition warfactoryDef;
    public UnitDefinition mgtowerDef;

    [Header("Starting Resources")]
    public int startingOre = 1200;
    public int startingPower = 25;

    private bool gameOver = false;
    public bool IsGameOver => gameOver;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        InitializeGame();
    }

    void InitializeGame()
    {
        // 设置初始资源
        if (EconomySystem.Instance != null)
        {
            EconomySystem.Instance.ore = startingOre;
            EconomySystem.Instance.powerSupply = startingPower;
        }

        // 设置难度
        if (EnemyAI.Instance != null)
        {
            EnemyAI.Instance.SetDifficulty(difficulty);
            EnemyAI.Instance.enemyFaction = enemyFaction;
        }

        // 设置玩家阵营
        if (BuildSystem.Instance != null)
            BuildSystem.Instance.playerFaction = playerFaction;

        // 创建玩家基地
        SpawnPlayerBase();
        SpawnEnemyBase();

        // 摄像机对准玩家基地
        if (Camera.main != null)
        {
            Camera.main.transform.position = new Vector3(playerBasePosition.x, playerBasePosition.y, -10);
        }

        Debug.Log("[GameManager] Game initialized successfully.");
    }

    void SpawnPlayerBase()
    {
        if (UnitRegistry.Instance == null) return;

        UnitRegistry.Instance.SpawnUnit(hqDef, playerFaction, playerBasePosition, false);
        UnitRegistry.Instance.SpawnUnit(harvesterDef, playerFaction, playerBasePosition + new Vector3(40, 0, 0), false);
        UnitRegistry.Instance.SpawnUnit(powerplantDef, playerFaction, playerBasePosition + new Vector3(-50, -60, 0), false);
    }

    void SpawnEnemyBase()
    {
        if (UnitRegistry.Instance == null) return;

        var ex = enemyBasePosition;
        var e = UnitRegistry.Instance.SpawnUnit(hqDef, enemyFaction, ex, true);
        if (e != null) e.def = hqDef;

        UnitRegistry.Instance.SpawnUnit(powerplantDef, enemyFaction, ex + new Vector3(-60, -40, 0), true);
        UnitRegistry.Instance.SpawnUnit(powerplantDef, enemyFaction, ex + new Vector3(-60, 40, 0), true);
        UnitRegistry.Instance.SpawnUnit(barracksDef, enemyFaction, ex + new Vector3(70, -40, 0), true);
        UnitRegistry.Instance.SpawnUnit(refineryDef, enemyFaction, ex + new Vector3(70, 50, 0), true);
        UnitRegistry.Instance.SpawnUnit(mgtowerDef, enemyFaction, ex + new Vector3(-50, 60, 0), true);
        UnitRegistry.Instance.SpawnUnit(mgtowerDef, enemyFaction, ex + new Vector3(50, 60, 0), true);
        UnitRegistry.Instance.SpawnUnit(warfactoryDef, enemyFaction, ex + new Vector3(-70, -60, 0), true);
        UnitRegistry.Instance.SpawnUnit(harvesterDef, enemyFaction, ex + new Vector3(-20, 20, 0), true);

        // 标记敌方建筑为已完成
        foreach (var unit in UnitRegistry.Instance.GetEnemyUnits())
        {
            if (unit != null && unit.def.isBuilding)
            {
                unit.currentHp = unit.maxHp;
            }
        }
    }

    void Update()
    {
        if (gameOver) return;

        // 胜负判定
        bool playerHQ = UnitRegistry.Instance?.HasHQ(false) ?? false;
        bool enemyHQ = UnitRegistry.Instance?.HasHQ(true) ?? false;

        if (!playerHQ)
        {
            gameOver = true;
            UIManager.Instance?.ShowGameOver(false);
        }
        else if (!enemyHQ)
        {
            gameOver = true;
            UIManager.Instance?.ShowGameOver(true);
        }
    }
}
