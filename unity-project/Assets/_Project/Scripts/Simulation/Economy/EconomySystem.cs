// ================================================================
// EconomySystem.cs — 经济系统（资源管理）
// 矿石/电力/科技点
// ================================================================
using UnityEngine;

public class EconomySystem : MonoBehaviour
{
    public static EconomySystem Instance { get; private set; }

    [Header("Player Resources")]
    public int ore = 1200;
    public int powerSupply = 25;
    public int powerUsed = 0;
    public int techPoints = 0;

    [Header("Enemy Resources")]
    public int enemyOre = 800;

    [Header("Income")]
    public int orePerTrip = 100;
    public int techPerTrip = 1;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public bool CanAfford(int cost) => cost >= 0 && ore >= cost;
    public bool HasPower(int need) => (powerSupply - powerUsed) >= need;

    public bool SpendOre(int amount)
    {
        if (amount < 0 || ore < amount) return false;
        ore -= amount;
        return true;
    }

    public void AddOre(int amount)
    {
        if (amount <= 0) return;
        ore += amount;
        techPoints += Mathf.CeilToInt(amount / 100f); // 每100矿石给1科技点
    }

    // A failed spawn returns prepaid funds without awarding mining technology points.
    public void RefundOre(int amount)
    {
        if (amount > 0) ore += amount;
    }

    public void AddPower(int amount) => powerSupply += amount;
    public void UsePower(int amount) => powerUsed += amount;
    public void RemovePower(int amount) => powerSupply -= amount;
    public void FreePower(int amount) => powerUsed -= amount;

    public int GetPowerAvailable() => powerSupply - powerUsed;

    /// <summary>
    /// 采矿车卸矿
    /// </summary>
    public void DepositOre(int amount, bool isEnemy = false)
    {
        if (isEnemy)
            enemyOre += amount;
        else
            AddOre(amount);
    }
}
