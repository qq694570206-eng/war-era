// ================================================================
// UnitRegistry.cs — 全局单位注册表
// 管理所有单位的创建、查询、销毁
// ================================================================
using System.Collections.Generic;
using UnityEngine;

public class UnitRegistry : MonoBehaviour
{
    public static UnitRegistry Instance { get; private set; }

    private List<UnitEntity> allUnits = new List<UnitEntity>();
    private List<UnitEntity> playerUnits = new List<UnitEntity>();
    private List<UnitEntity> enemyUnits = new List<UnitEntity>();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public UnitEntity SpawnUnit(UnitDefinition def, FactionDefinition faction, Vector3 position, bool isEnemy)
    {
        if (def == null)
        {
            Debug.LogError("Cannot spawn a unit without a UnitDefinition.");
            return null;
        }
        if (def.prefab == null)
        {
            Debug.LogError($"UnitDefinition '{def.unitId}' has no prefab assigned!");
            return null;
        }

        GameObject go = Instantiate(def.prefab, position, Quaternion.identity);
        UnitEntity entity = go.GetComponent<UnitEntity>();
        if (entity == null)
        {
            entity = go.AddComponent<UnitEntity>();
        }

        // 【修复点】原来这里只 Instantiate，从不读 def.sprite —— 素材接进工程后
        // 屏幕上的单位仍是占位方块。Initialize 内部会调 ApplyDefinitionSprite()
        // 把 def.sprite 真正挂到 ArtRoot 上的 SpriteRenderer。
        entity.Initialize(def, faction, isEnemy);

        Register(entity);
        return entity;
    }

    public void Register(UnitEntity entity)
    {
        if (entity == null || entity.isDead || allUnits.Contains(entity)) return;
        allUnits.Add(entity);
        if (entity.isEnemy)
            enemyUnits.Add(entity);
        else
            playerUnits.Add(entity);
    }

    public void Unregister(UnitEntity entity)
    {
        allUnits.Remove(entity);
        playerUnits.Remove(entity);
        enemyUnits.Remove(entity);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// 查找最近的敌方单位
    /// </summary>
    public UnitEntity FindNearestEnemy(Vector3 position, float maxRange, bool fromEnemy)
    {
        var searchList = fromEnemy ? playerUnits : enemyUnits;
        UnitEntity nearest = null;
        float minDistSq = maxRange * maxRange;

        foreach (var unit in searchList)
        {
            if (unit == null || unit.isDead) continue;
            float distSq = (unit.transform.position - position).sqrMagnitude;
            if (distSq < minDistSq)
            {
                minDistSq = distSq;
                nearest = unit;
            }
        }
        return nearest;
    }

    /// <summary>
    /// 获取单位间避让偏移（简单版）
    /// </summary>
    public Vector3 GetAvoidanceOffset(UnitEntity self)
    {
        Vector3 offset = Vector3.zero;
        float radius = self.def.selectionRadius + 2f;

        foreach (var unit in allUnits)
        {
            if (unit == null || unit == self || unit.isDead) continue;
            if (unit.def.isBuilding) continue;

            Vector3 diff = self.transform.position - unit.transform.position;
            float distSq = diff.sqrMagnitude;
            float minDist = radius + unit.def.selectionRadius;

            if (distSq < minDist * minDist && distSq > 0.01f)
            {
                float dist = Mathf.Sqrt(distSq);
                float force = (minDist - dist) / minDist;
                offset += diff.normalized * force * 0.5f;
            }
        }
        return offset;
    }

    public List<UnitEntity> GetPlayerUnits() => playerUnits;
    public List<UnitEntity> GetEnemyUnits() => enemyUnits;
    public List<UnitEntity> GetAllUnits() => allUnits;

    public int GetPlayerUnitCount() => playerUnits.Count;
    public int GetEnemyUnitCount() => enemyUnits.Count;

    /// <summary>
    /// 检查某阵营的 HQ 是否存在
    /// </summary>
    public bool HasHQ(bool isEnemy)
    {
        var list = isEnemy ? enemyUnits : playerUnits;
        foreach (var u in list)
        {
            if (u != null && !u.isDead && u.def.unitId == "hq") return true;
        }
        return false;
    }
}
