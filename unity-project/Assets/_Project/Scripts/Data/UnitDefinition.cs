// ================================================================
// UnitDefinition.cs — 单位/建筑数据定义（ScriptableObject）
// 数据驱动：所有单位属性通过数据表配置，代码不硬编码
// ================================================================
using UnityEngine;

[CreateAssetMenu(fileName = "UnitDef_", menuName = "IronFrontline/Unit Definition")]
public class UnitDefinition : ScriptableObject
{
    [Header("Identity")]
    public string displayName = "New Unit";
    public string unitId = "unit_id";
    public UnitCategory category = UnitCategory.Infantry;

    [Header("Stats")]
    public float maxHp = 100f;
    public float damage = 10f;
    public float range = 50f;
    public float fireRate = 0.5f; // seconds between shots
    public DamageType damageType = DamageType.Light;
    public float moveSpeed = 40f; // units per second
    public float sightRange = 120f;

    [Header("Cost")]
    public int oreCost = 100;
    public int powerNeed = 0;
    public int powerSupply = 0;
    public int techLevel = 1;

    [Header("Visual")]
    public Sprite sprite;
    public GameObject prefab;
    public float selectionRadius = 8f;

    [Header("Building Specific")]
    public bool isBuilding = false;
    public Vector2 buildingSize = new Vector2(44, 44);
    public float buildTime = 3f;

    [Header("Production")]
    public bool canProduceUnits = false;
    public UnitDefinition[] producibleUnits;

    [Header("Harvester")]
    public bool isHarvester = false;
    public int oreCapacity = 100;
    public float harvestTime = 2f;
}

public enum UnitCategory
{
    Infantry,
    Vehicle,
    Harvester,
    Building,
    Defense
}

public enum DamageType
{
    Light,       // 步枪/机枪 — 克轻甲
    Piercing,    // 穿甲/炮弹 — 克中重甲
    Explosive,   // 高爆/手雷 — 克步兵/建筑
    Energy       // 能量/特殊
}

public enum ArmorType
{
    Infantry,  // 轻甲
    Vehicle,   // 中甲
    Heavy,     // 重甲
    Building   // 建筑
}
