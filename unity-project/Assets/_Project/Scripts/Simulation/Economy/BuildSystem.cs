// ================================================================
// BuildSystem.cs — 建造系统
// 建筑放置、生产队列
// ================================================================
using UnityEngine;

public class BuildSystem : MonoBehaviour
{
    public static BuildSystem Instance { get; private set; }

    [Header("References")]
    public FactionDefinition playerFaction;

    [Header("Placement")]
    public UnitDefinition pendingBuildDef;
    public bool isPlacing = false;
    public float minBuildingDistance = 36f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>
    /// 选择建筑准备放置
    /// </summary>
    public bool SelectBuilding(UnitDefinition def)
    {
        if (!EconomySystem.Instance.CanAfford(def.oreCost))
        {
            UIManager.Instance?.ShowToast("矿石不足！");
            return false;
        }
        if (!EconomySystem.Instance.HasPower(def.powerNeed))
        {
            UIManager.Instance?.ShowToast("电力不足！");
            return false;
        }

        pendingBuildDef = def;
        isPlacing = true;
        UIManager.Instance?.ShowToast($"点击地图放置 {def.displayName}");
        return true;
    }

    /// <summary>
    /// 在指定位置放置建筑
    /// </summary>
    public bool PlaceBuilding(Vector3 position)
    {
        if (pendingBuildDef == null) return false;

        // 检查与其他建筑的距离
        foreach (var unit in UnitRegistry.Instance.GetAllUnits())
        {
            if (unit == null || !unit.def.isBuilding) continue;
            if (Vector3.Distance(unit.transform.position, position) < minBuildingDistance)
            {
                UIManager.Instance?.ShowToast("离其他建筑太近！");
                return false;
            }
        }

        if (!EconomySystem.Instance.SpendOre(pendingBuildDef.oreCost))
        {
            UIManager.Instance?.ShowToast("矿石不足！");
            return false;
        }

        // 创建建筑
        UnitEntity building = UnitRegistry.Instance.SpawnUnit(pendingBuildDef, playerFaction, position, false);
        if (building != null && pendingBuildDef.powerSupply > 0)
            EconomySystem.Instance.AddPower(pendingBuildDef.powerSupply);
        if (building != null && pendingBuildDef.powerNeed > 0)
            EconomySystem.Instance.UsePower(pendingBuildDef.powerNeed);

        AudioManager.Instance?.PlayBuildSound();

        pendingBuildDef = null;
        isPlacing = false;
        return true;
    }

    /// <summary>
    /// 生产单位
    /// </summary>
    public bool ProduceUnit(UnitDefinition unitDef)
    {
        if (unitDef.techLevel > TechSystem.Instance?.techLevel)
        {
            UIManager.Instance?.ShowToast($"需要科技 T{unitDef.techLevel}！");
            return false;
        }

        int cost = playerFaction != null ? playerFaction.GetModifiedCost(unitDef.oreCost) : unitDef.oreCost;
        if (!EconomySystem.Instance.CanAfford(cost))
        {
            UIManager.Instance?.ShowToast("矿石不足！");
            return false;
        }

        // 查找对应生产建筑
        string needBuilding = unitDef.category == UnitCategory.Infantry ? "barracks" : "warfactory";
        UnitEntity producer = null;
        foreach (var u in UnitRegistry.Instance.GetPlayerUnits())
        {
            if (u == null || u.isDead) continue;
            if (u.def.unitId == needBuilding) { producer = u; break; }
        }

        if (producer == null)
        {
            UIManager.Instance?.ShowToast($"需要先建{(unitDef.category == UnitCategory.Infantry ? "兵营" : "车厂")}！");
            return false;
        }

        EconomySystem.Instance.SpendOre(cost);

        Vector3 spawnPos = producer.transform.position + new Vector3(unitDef.category == UnitCategory.Infantry ? 28f : 36f, 0, 0);
        UnitEntity unit = UnitRegistry.Instance.SpawnUnit(unitDef, playerFaction, spawnPos, false);

        AudioManager.Instance?.PlayBuildSound();
        UIManager.Instance?.ShowToast($"{unitDef.displayName} 已生产");

        return true;
    }

    public void CancelPlacement()
    {
        pendingBuildDef = null;
        isPlacing = false;
    }
}
