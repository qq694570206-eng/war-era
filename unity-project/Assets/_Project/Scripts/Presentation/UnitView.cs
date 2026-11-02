// ================================================================
// UnitView.cs — 单位表现层
// 负责朝向旋转、行走弹跳、开火后坐、受击闪烁、死亡动画
// 挂载在单位预制体的子对象上（SpriteRenderer 所在节点）
// ================================================================
using UnityEngine;

public class UnitView : MonoBehaviour
{
    private UnitEntity entity;
    private SpriteRenderer sr;
    private MaterialPropertyBlock propBlock;

    private static readonly int FlashAmount = Shader.PropertyToID("_FlashAmount");

    void Awake()
    {
        entity = GetComponentInParent<UnitEntity>();
        sr = GetComponent<SpriteRenderer>();
        propBlock = new MaterialPropertyBlock();
    }

    void Update()
    {
        if (entity == null || entity.isDead) return;

        float dt = Time.deltaTime;

        // === 朝向 ===
        bool isMoving = (entity.hasMoveTarget || entity.target != null) && !entity.holdPosition;
        bool isFiring = entity.recoil > 0;

        if (entity.def.isBuilding)
        {
            UpdateBuildingVisual(isFiring);
        }
        else if (entity.def.category == UnitCategory.Vehicle)
        {
            UpdateVehicleVisual(isMoving, isFiring);
        }
        else if (entity.def.category == UnitCategory.Infantry)
        {
            UpdateInfantryVisual(isMoving, isFiring);
        }

        // === 受击闪烁 ===
        if (sr != null && entity.flashTime > 0)
        {
            sr.GetPropertyBlock(propBlock);
            propBlock.SetFloat(FlashAmount, 1f);
            sr.SetPropertyBlock(propBlock);
        }
        else if (sr != null)
        {
            sr.GetPropertyBlock(propBlock);
            propBlock.SetFloat(FlashAmount, 0f);
            sr.SetPropertyBlock(propBlock);
        }
    }

    void UpdateBuildingVisual(bool isFiring)
    {
        // 建筑不旋转，开火时微摇
        if (isFiring)
        {
            transform.localPosition = new Vector3(
                Random.Range(-0.02f, 0.02f),
                Random.Range(-0.02f, 0.02f),
                0
            );
        }
        else
        {
            transform.localPosition = Vector3.zero;
        }
    }

    void UpdateVehicleVisual(bool isMoving, bool isFiring)
    {
        // 载具整体旋转到朝向（+90 度偏移，因为精灵图默认朝上）
        float targetAngle = entity.facing * Mathf.Rad2Deg + 90f;
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            Quaternion.Euler(0, 0, targetAngle),
            720f * Time.deltaTime
        );

        // 行走弹跳
        if (isMoving)
        {
            float bob = Mathf.Sin(entity.animTime * 8f) * 0.05f;
            transform.localScale = new Vector3(1, 1 + bob, 1);
        }
        else
        {
            transform.localScale = Vector3.MoveTowards(transform.localScale, Vector3.one, 0.1f * Time.deltaTime);
        }

        // 开火后坐（沿朝向反方向偏移）
        if (isFiring)
        {
            float recoilOffset = entity.recoil * 0.3f;
            Vector2 recoilDir = -new Vector2(Mathf.Cos(entity.facing), Mathf.Sin(entity.facing));
            transform.localPosition = recoilDir * recoilOffset;
        }
        else
        {
            transform.localPosition = Vector3.MoveTowards(transform.localPosition, Vector3.zero, 0.5f * Time.deltaTime);
        }
    }

    void UpdateInfantryVisual(bool isMoving, bool isFiring)
    {
        // 步兵：面朝左时水平翻转
        if (sr != null)
        {
            bool facingLeft = entity.facing > Mathf.PI / 2f || entity.facing < -Mathf.PI / 2f;
            sr.flipX = facingLeft;
        }

        // 行走弹跳
        if (isMoving)
        {
            float bob = Mathf.Abs(Mathf.Sin(entity.animTime * 10f)) * 0.08f;
            transform.localPosition = new Vector3(0, bob, 0);
        }
        else
        {
            transform.localPosition = Vector3.MoveTowards(transform.localPosition, Vector3.zero, 0.1f * Time.deltaTime);
        }

        // 开火后坐
        if (isFiring)
        {
            float recoilOffset = entity.recoil * 0.15f;
            Vector2 recoilDir = -new Vector2(Mathf.Cos(entity.facing), Mathf.Sin(entity.facing));
            transform.localPosition += (Vector3)(recoilDir * recoilOffset);
        }
    }
}
