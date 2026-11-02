// ================================================================
// CombatSystem.cs — 战斗结算系统
// 伤害计算 + 伤害矩阵应用
// ================================================================
using UnityEngine;

public class CombatSystem : MonoBehaviour
{
    public static CombatSystem Instance { get; private set; }

    [Header("Data")]
    public DamageMatrix damageMatrix;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>
    /// 攻击者对目标造成伤害
    /// </summary>
    public void DealDamage(UnitEntity attacker, UnitEntity target)
    {
        if (target == null || target.isDead) return;
        if (attacker == null || attacker.def.damage <= 0) return;

        ArmorType targetArmor = target.GetArmorType();
        float baseDamage = attacker.def.damage;
        float finalDamage = baseDamage;

        if (damageMatrix != null)
        {
            finalDamage = damageMatrix.CalculateDamage(baseDamage, attacker.def.damageType, targetArmor);
        }

        target.TakeDamage(finalDamage);
    }
}
