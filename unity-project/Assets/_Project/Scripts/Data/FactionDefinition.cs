// ================================================================
// FactionDefinition.cs — 阵营定义（ScriptableObject）
// ================================================================
using UnityEngine;

[CreateAssetMenu(fileName = "FactionDef_", menuName = "IronFrontline/Faction Definition")]
public class FactionDefinition : ScriptableObject
{
    [Header("Identity")]
    public string factionId = "allies";
    public string displayName = "盟军";
    public string description = "精准打击";

    [Header("Colors")]
    public Color primaryColor = new Color(0.25f, 0.65f, 0.84f); // #3FA7D6
    public Color enemyColor = new Color(0.91f, 0.30f, 0.24f);   // #E74C3C

    [Header("Unit Modifiers")]
    public float speedMultiplier = 1.15f;
    public float costMultiplier = 1.2f;
    public float rangeMultiplier = 1.1f;
    public float hpMultiplier = 1.0f;

    [Header("Available Units")]
    public UnitDefinition[] availableUnits;

    [Header("Available Buildings")]
    public UnitDefinition[] availableBuildings;

    /// <summary>
    /// 应用阵营修正到单位定义
    /// </summary>
    public float GetModifiedCost(int baseCost) => Mathf.RoundToInt(baseCost * costMultiplier);
    public float GetModifiedSpeed(float baseSpeed) => baseSpeed * speedMultiplier;
    public float GetModifiedRange(float baseRange) => baseRange * rangeMultiplier;
    public float GetModifiedHp(float baseHp) => Mathf.RoundToInt(baseHp * hpMultiplier);
}
