// ================================================================
// UnitEntity.cs — 单位实体（挂载在单位 GameObject 上）
// 管理 HP、移动、战斗、状态机
// ================================================================
using UnityEngine;

public class UnitEntity : MonoBehaviour
{
    [Header("Data")]
    public UnitDefinition def;
    public FactionDefinition faction;
    public bool isEnemy = false;

    [Header("Runtime State")]
    public float currentHp;
    public float maxHp;
    public bool isDead = false;

    [Header("Movement")]
    public Vector3 targetPosition;
    public bool hasMoveTarget = false;
    public bool holdPosition = false;
    public float facing = 0f;

    [Header("Combat")]
    public UnitEntity target;
    public float fireTimer = 0f;
    [Tooltip("后坐强度 0..1（1 = 刚开火）。由 UnitView 读取换算位移")]
    public float recoil = 0f;
    [Tooltip("后坐衰减速率 = 1 / 持续时间。按『动作时长』配置，不按『衰减速率』配")]
    public float recoilDecay = 1f / 0.18f;   // 默认 0.18s（坦克炮，§5.3）

    [Header("Building")]
    public bool constructing = false;
    public float buildProgress = 0f;

    [Header("Harvester")]
    public bool isHarvester = false;
    public int oreCarried = 0;
    public float harvestTimer = 0f;

    [Header("Visual")]
    public SpriteRenderer spriteRenderer;
    public float animTime = 0f;
    public float flashTime = 0f;

    // 受击闪白：走 MaterialPropertyBlock，不走 spriteRenderer.material。
    // material 会实例化材质副本（每单位一份），既破合批又漏内存；
    // propBlock 是 per-renderer 数据，不产生材质变体。
    private MaterialPropertyBlock flashBlock;
    private static readonly int FlashShaderParam = Shader.PropertyToID("_FlashAmount");
    private bool flashApplied = false;

    void Awake()
    {
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        flashBlock = new MaterialPropertyBlock();
    }

    /// <summary>
    /// 由 UnitRegistry 在生成后调用：把 def.sprite 真正挂到 SpriteRenderer 上。
    /// 修复"素材目录满了、进游戏还是一片方块"这个假完成点。
    /// </summary>
    public void ApplyDefinitionSprite()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer == null) return;
        if (def == null) return;

        if (def.sprite != null)
        {
            spriteRenderer.sprite = def.sprite;
            spriteRenderer.color = Color.white;
        }
        else
        {
            // 回退占位 sprite（不换 sprite，只保留 prefab 自带的占位），并明确告警
            Debug.LogWarning($"[UnitEntity] UnitDefinition '{def.unitId}' 没有配置 sprite，" +
                             $"回退到占位图。检查 PhaseBBootstrap.CreateUnitDefinitions() 的 " +
                             $"Art/Keyed/{def.unitId}.png 是否导入成功。");
        }
    }

    public void Initialize(UnitDefinition definition, FactionDefinition factionDef, bool enemy)
    {
        def = definition;
        faction = factionDef;
        isEnemy = enemy;
        isHarvester = definition.isHarvester;

        // 应用阵营修正
        maxHp = faction != null ? faction.GetModifiedHp(definition.maxHp) : definition.maxHp;
        currentHp = maxHp;

        // 后坐时长按武器类型配置（§5.3），不是全局 3f 的固定衰减
        recoilDecay = 1f / RecoilDurationOf(definition.unitId);

        // 把 def.sprite 真正挂上去
        ApplyDefinitionSprite();
    }

    /// <summary>后坐持续时间（秒）。0.05s = 3 帧，玩家根本看不见（§5.1）。</summary>
    static float RecoilDurationOf(string unitId)
    {
        switch (unitId)
        {
            case "rifleman":  return 0.10f;
            case "bazooka":   return 0.15f;
            case "sniper":    return 0.10f;
            case "mgtower":   return 0.08f;
            case "tank":      return 0.18f;
            case "heavytank": return 0.22f;
            case "artillery": return 0.26f;
            case "atcannon":  return 0.12f;
            default:          return 0.15f;
        }
    }

    void Update()
    {
        if (isDead || def == null) return;

        float dt = GameClock.Instance?.DeltaTime ?? Time.deltaTime;
        animTime += dt;

        if (flashTime > 0) flashTime -= dt;
        if (recoil > 0f) recoil = Mathf.Max(0f, recoil - dt * recoilDecay);

        UpdateFlash();

        if (def.isBuilding)
        {
            UpdateBuilding(dt);
            return;
        }

        if (isHarvester)
        {
            UpdateHarvester(dt);
            return;
        }

        UpdateCombat(dt);
    }

    void UpdateBuilding(float dt)
    {
        // 防御建筑自动攻击
        if (def.damage > 0 && def.range > 0)
        {
            fireTimer -= dt;
            if (fireTimer <= 0)
            {
                UnitEntity nearest = UnitRegistry.Instance.FindNearestEnemy(transform.position, def.range, isEnemy);
                if (nearest != null && !nearest.isDead)
                {
                    fireTimer = def.fireRate;
                    facing = Mathf.Atan2(nearest.transform.position.y - transform.position.y,
                                         nearest.transform.position.x - transform.position.x);
                    CombatSystem.Instance.DealDamage(this, nearest);
                    VFXManager.Instance?.SpawnBullet(transform.position, nearest.transform.position, isEnemy);
                    VFXManager.Instance?.SpawnMuzzleFlash(transform.position + new Vector3(Mathf.Cos(facing) * def.buildingSize.x * 0.4f, Mathf.Sin(facing) * def.buildingSize.y * 0.4f, 0));
                    AudioManager.Instance?.PlayWeaponSound(def.damageType, def.category == UnitCategory.Vehicle);
                }
            }
        }
    }

    void UpdateCombat(float dt)
    {
        // 自动寻敌
        if (target == null && !holdPosition && !hasMoveTarget)
        {
            float detectRange = def.range * 1.8f;
            target = UnitRegistry.Instance.FindNearestEnemy(transform.position, detectRange, isEnemy);
        }

        if (target != null)
        {
            if (target.isDead) { target = null; return; }

            Vector3 toTarget = target.transform.position - transform.position;
            float dist = toTarget.magnitude;
            facing = Mathf.Atan2(toTarget.y, toTarget.x);

            float modifiedRange = faction != null ? faction.GetModifiedRange(def.range) : def.range;

            if (dist <= modifiedRange)
            {
                // 在射程内，攻击
                fireTimer -= dt;
                if (fireTimer <= 0)
                {
                    fireTimer = def.fireRate;
                    recoil = 0.15f;
                    CombatSystem.Instance.DealDamage(this, target);
                    VFXManager.Instance?.SpawnBullet(transform.position, target.transform.position, isEnemy);
                    VFXManager.Instance?.SpawnMuzzleFlash(transform.position + new Vector3(Mathf.Cos(facing) * def.selectionRadius, Mathf.Sin(facing) * def.selectionRadius, 0));
                    AudioManager.Instance?.PlayWeaponSound(def.damageType, def.category == UnitCategory.Vehicle);
                }
            }
            else if (!holdPosition)
            {
                // 不在射程，靠近
                MoveTowards(target.transform.position, dt);
            }
        }
        else if (hasMoveTarget)
        {
            Vector3 toTarget = targetPosition - transform.position;
            float dist = toTarget.magnitude;
            if (dist > 1f)
            {
                facing = Mathf.Atan2(toTarget.y, toTarget.x);
                MoveTowards(targetPosition, dt);
            }
            else
            {
                hasMoveTarget = false;
            }
        }
    }

    void MoveTowards(Vector3 target, float dt)
    {
        Vector3 toTarget = target - transform.position;
        float dist = toTarget.magnitude;
        if (dist > 0.1f)
        {
            float speed = (faction != null ? faction.GetModifiedSpeed(def.moveSpeed) : def.moveSpeed) * dt;
            Vector3 move = toTarget.normalized * speed;

            // 简单避让
            Vector3 avoid = UnitRegistry.Instance.GetAvoidanceOffset(this);
            move += avoid * 0.5f;

            transform.position += move;
        }
    }

    void UpdateHarvester(float dt)
    {
        // 采矿逻辑（简化版，完整版在 HarvesterSystem）
        // TODO: 对接 OreNode 系统
    }

    public void TakeDamage(float damage)
    {
        if (isDead || damage <= 0f || float.IsNaN(damage) || float.IsInfinity(damage)) return;
        currentHp -= damage;
        flashTime = 0.08f;

        if (currentHp <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// 受击闪白。原实现在 Sprites/Default 上找 _FlashAmount —— 该属性不存在，
    /// 于是整段静默失效（不报错、没效果）。现改为：
    ///   1) 材质换成 WarEra/SpriteFlash（自带 _FlashAmount 并标了 [PerRendererData]）
    ///   2) 值走 MaterialPropertyBlock，不产生材质实例
    /// 若材质不是该 shader（例如仍用占位图），退回 sr.color 直接把颜色提亮，
    /// 保证任何情况下"受击有反馈"这条不会静默丢失。
    /// </summary>
    void UpdateFlash()
    {
        if (spriteRenderer == null) return;

        bool wantFlash = flashTime > 0f;
        if (wantFlash == flashApplied) return;   // 只在状态切换时写，避免每帧 SetPropertyBlock
        flashApplied = wantFlash;

        Material mat = spriteRenderer.sharedMaterial;
        bool hasProp = mat != null && mat.HasProperty(FlashShaderParam);
        if (hasProp)
        {
            spriteRenderer.GetPropertyBlock(flashBlock);
            flashBlock.SetFloat(FlashShaderParam, wantFlash ? 1f : 0f);
            spriteRenderer.SetPropertyBlock(flashBlock);
        }
        else
        {
            spriteRenderer.color = wantFlash ? new Color(1f, 0.72f, 0.72f) : Color.white;
        }
    }

    public void Die()
    {
        if (isDead) return;
        isDead = true;
        target = null;
        hasMoveTarget = false;
        UnitRegistry.Instance?.Unregister(this);
        // scale 语义 = 直径倍数（见 VFXManager.SpawnExplosion 注释）。
        // 旧值 25/15 是按"乘到 64 单位占位图上"算的 → 实际直径 1600/960 单位，
        // 屏幕全高只有 600，等于每次死亡都糊满屏。目标直径取单位尺寸的 1.5~2 倍：
        // 载具 1.2（≈77 单位）、步兵 0.7（≈45 单位）。
        VFXManager.Instance?.SpawnExplosion(transform.position, def.category == UnitCategory.Vehicle ? 1.2f : 0.7f);
        AudioManager.Instance?.PlayExplosion(def.category == UnitCategory.Vehicle);

        // 延迟销毁（等死亡动画播完）
        Destroy(gameObject, 0.8f);
    }

    void OnDestroy()
    {
        UnitRegistry.Instance?.Unregister(this);
    }

    public void CommandMove(Vector3 position)
    {
        targetPosition = position;
        hasMoveTarget = true;
        target = null;
        holdPosition = false;
    }

    public void CommandAttack(UnitEntity enemy)
    {
        target = enemy;
        hasMoveTarget = false;
        holdPosition = false;
    }

    public void CommandHold()
    {
        holdPosition = true;
        target = null;
        hasMoveTarget = false;
    }

    public ArmorType GetArmorType()
    {
        if (def.isBuilding) return ArmorType.Building;
        if (def.category == UnitCategory.Infantry) return ArmorType.Infantry;
        if (def.category == UnitCategory.Vehicle) return def.selectionRadius > 15 ? ArmorType.Heavy : ArmorType.Vehicle;
        return ArmorType.Vehicle;
    }
}
