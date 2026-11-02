// ================================================================
// TechSystem.cs — 科技升级系统
// ================================================================
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "TechUpgrade_", menuName = "IronFrontline/Tech Upgrade")]
public class TechUpgrade : ScriptableObject
{
    public string upgradeId;
    public string displayName;
    public string description;
    public int techCost;
    public int requiredTechLevel = 1;

    [Header("Effects")]
    public float hpMultiplier = 1f;
    public float damageMultiplier = 1f;
    public float speedMultiplier = 1f;
    public bool unlocksTechLevel2 = false;
}

public class TechSystem : MonoBehaviour
{
    public static TechSystem Instance { get; private set; }

    [Header("Data")]
    public TechUpgrade[] availableUpgrades;
    public List<string> purchasedUpgrades = new List<string>();

    [Header("State")]
    public int techLevel = 1;
    public float hpMul = 1f;
    public float dmgMul = 1f;
    public float speedMul = 1f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public bool CanPurchase(TechUpgrade upgrade)
    {
        if (purchasedUpgrades.Contains(upgrade.upgradeId)) return false;
        if (EconomySystem.Instance.techPoints < upgrade.techCost) return false;
        if (techLevel < upgrade.requiredTechLevel - 1) return false;
        return true;
    }

    public bool Purchase(TechUpgrade upgrade)
    {
        if (!CanPurchase(upgrade)) return false;

        EconomySystem.Instance.techPoints -= upgrade.techCost;
        purchasedUpgrades.Add(upgrade.upgradeId);

        if (upgrade.unlocksTechLevel2) techLevel = 2;

        hpMul *= upgrade.hpMultiplier;
        dmgMul *= upgrade.damageMultiplier;
        speedMul *= upgrade.speedMultiplier;

        // 应用到已有单位
        foreach (var unit in UnitRegistry.Instance.GetPlayerUnits())
        {
            if (unit == null || unit.isDead) continue;
            if (upgrade.hpMultiplier != 1f)
            {
                unit.maxHp = Mathf.RoundToInt(unit.maxHp * upgrade.hpMultiplier);
                unit.currentHp = Mathf.RoundToInt(unit.currentHp * upgrade.hpMultiplier);
            }
        }

        AudioManager.Instance?.PlayBuildSound();
        UIManager.Instance?.ShowToast($"{upgrade.displayName} 已升级！");

        return true;
    }

    public bool IsPurchased(string id) => purchasedUpgrades.Contains(id);
}
