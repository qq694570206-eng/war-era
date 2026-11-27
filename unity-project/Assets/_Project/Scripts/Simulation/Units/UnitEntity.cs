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
    public float recoil = 0f; // 后坐偏移

    [Header("Harvester")]
    public bool isHarvester = false;
    public int oreCarried = 0;
    public float harvestTimer = 0f;

    [Header("Visual")]
    public SpriteRenderer spriteRenderer;
    public float animTime = 0f;
    public float flashTime = 0f;

    private static readonly int FlashShaderParam = Shader.PropertyToID("_FlashAmount");

    void Awake()
    {
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
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
    }

    void Update()
    {
        if (isDead || def == null) return;

        float dt = GameClock.Instance?.DeltaTime ?? Time.deltaTime;
        animTime += dt;

        if (flashTime > 0) flashTime -= dt;
        if (recoil > 0) recoil -= dt * 3f;

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

        // 闪烁效果
        if (spriteRenderer != null && spriteRenderer.material.HasProperty(FlashShaderParam))
        {
            spriteRenderer.material.SetFloat(FlashShaderParam, 1f);
        }

        if (currentHp <= 0)
        {
            Die();
        }
    }

    public void Die()
    {
        if (isDead) return;
        isDead = true;
        target = null;
        hasMoveTarget = false;
        UnitRegistry.Instance?.Unregister(this);
        VFXManager.Instance?.SpawnExplosion(transform.position, def.category == UnitCategory.Vehicle ? 25f : 15f);
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
