// ================================================================
// DamageMatrix.cs — 伤害克制矩阵（ScriptableObject）
// 伤害类型 × 护甲类型 = 伤害倍率
// ================================================================
using UnityEngine;

[CreateAssetMenu(fileName = "DamageMatrix", menuName = "IronFrontline/Damage Matrix")]
public class DamageMatrix : ScriptableObject
{
    [System.Serializable]
    public struct ArmorRow
    {
        public float infantry;
        public float vehicle;
        public float heavy;
        public float building;
    }

    [Header("Damage Type → Armor Type Multiplier")]
    public ArmorRow light = new ArmorRow { infantry = 1.25f, vehicle = 0.75f, heavy = 0.4f, building = 0.3f };
    public ArmorRow piercing = new ArmorRow { infantry = 0.6f, vehicle = 1.2f, heavy = 1.15f, building = 1.0f };
    public ArmorRow explosive = new ArmorRow { infantry = 1.4f, vehicle = 1.0f, heavy = 0.7f, building = 1.3f };
    public ArmorRow energy = new ArmorRow { infantry = 1.0f, vehicle = 1.2f, heavy = 1.0f, building = 1.1f };

    /// <summary>
    /// 获取伤害倍率
    /// </summary>
    public float GetMultiplier(DamageType dmgType, ArmorType armorType)
    {
        ArmorRow row;
        switch (dmgType)
        {
            case DamageType.Light: row = light; break;
            case DamageType.Piercing: row = piercing; break;
            case DamageType.Explosive: row = explosive; break;
            case DamageType.Energy: row = energy; break;
            default: row = light; break;
        }

        switch (armorType)
        {
            case ArmorType.Infantry: return row.infantry;
            case ArmorType.Vehicle: return row.vehicle;
            case ArmorType.Heavy: return row.heavy;
            case ArmorType.Building: return row.building;
            default: return 1f;
        }
    }

    /// <summary>
    /// 计算最终伤害
    /// </summary>
    public float CalculateDamage(float baseDamage, DamageType dmgType, ArmorType armorType)
    {
        return baseDamage * GetMultiplier(dmgType, armorType);
    }
}
