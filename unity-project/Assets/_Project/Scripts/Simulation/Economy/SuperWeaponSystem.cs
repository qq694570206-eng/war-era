// ================================================================
// SuperWeaponSystem.cs — 超级武器系统（核弹打击）
// 需雷达站 + 充能时间 → 玩家点击目标 → 范围高爆伤害
// ================================================================
using UnityEngine;

public class SuperWeaponSystem : MonoBehaviour
{
    public static SuperWeaponSystem Instance { get; private set; }

    [Header("Config")]
    public float chargeTime = 60f;        // 充能时间（秒）
    public float blastRadius = 140f;       // 爆炸半径
    public float blastDamage = 600f;       // 基础伤害
    public float radiationDuration = 5f;   // 辐射残留时间

    [Header("State")]
    public float currentCharge = 0f;
    public bool isReady = false;
    public bool isTargeting = false;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update()
    {
        if (isReady) return;

        // 检查是否有雷达站
        bool hasRadar = false;
        foreach (var u in UnitRegistry.Instance.GetPlayerUnits())
        {
            if (u == null || u.isDead) continue;
            if (u.def.unitId == "radar") { hasRadar = true; break; }
        }

        if (!hasRadar) return;

        currentCharge += GameClock.Instance?.DeltaTime ?? Time.deltaTime;

        if (currentCharge >= chargeTime)
        {
            isReady = true;
            UIManager.Instance?.ShowAlert("终极武器就绪！");
            AudioManager.Instance?.PlayAlertSound();
        }
    }

    /// <summary>
    /// 激活超武瞄准模式
    /// </summary>
    public void Activate()
    {
        if (!isReady) return;
        isTargeting = true;
        UIManager.Instance?.ShowToast("选择打击目标");
    }

    /// <summary>
    /// 发射核弹到目标位置
    /// </summary>
    public void Fire(Vector3 targetPosition)
    {
        if (!isReady || !isTargeting) return;

        isReady = false;
        isTargeting = false;
        currentCharge = 0f;

        // 特效
        VFXManager.Instance?.SpawnExplosion(targetPosition, blastRadius / 15f);
        AudioManager.Instance?.PlayNuke();

        // 范围伤害（延迟 0.5 秒模拟弹道飞行）
        StartCoroutine(DelayedBlast(targetPosition, 0.5f));

        UIManager.Instance?.ShowToast("核弹已发射！");
    }

    System.Collections.IEnumerator DelayedBlast(Vector3 target, float delay)
    {
        yield return new WaitForSeconds(delay);

        // 全屏闪光
        // TODO: 接入全屏闪光特效

        // 范围伤害
        foreach (var unit in UnitRegistry.Instance.GetAllUnits())
        {
            if (unit == null || unit.isDead) continue;

            float dist = Vector3.Distance(unit.transform.position, target);
            if (dist < blastRadius)
            {
                float falloff = 1f - dist / blastRadius;
                float damage = blastDamage * falloff;
                unit.TakeDamage(damage);
            }
        }

        // 辐射残留特效
        for (int i = 0; i < 20; i++)
        {
            float angle = Random.Range(0, Mathf.PI * 2);
            float r = Random.Range(0, blastRadius);
            Vector3 pos = target + new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, 0);
            VFXManager.Instance?.SpawnExplosion(pos, 3f);
        }
    }
}
