// ================================================================
// UnitView.cs — 单位表现层（挂载在 ArtRoot 上，SpriteRenderer 同节点）
//
// 三层结构（docs/animation-facing-spec-v1.md §5.2）：
//   UnitEntity  根：位置由逻辑驱动，不旋转、不缩放
//    └ RecoilPivot ：只承担后坐位移（世界方向 → local 转换）
//       └ ArtRoot   ：只承担朝向 rotation + 呼吸/弹跳的 posY/scale，挂 SpriteRenderer
//
// 为什么必须拆：
//   旧代码在同一个 transform 上既设 rotation 又设 localPosition，而 recoilDir 是世界
//   方向。rotation≠0 时它会被再旋转一次 → 载具朝右上开炮，车却往左上退。
//
// 缩放只走 PPU（TextureImporter.spritePixelsPerUnit），本脚本不改 localScale 的基准。
// 规格 §7.1 的 artRoot.localScale = k 公式按团队裁定作废：PPU 是导入期常量，
// Sprite.bounds 天然正确，无需每帧算 k，也不会被这里的呼吸缩放覆写。
//
// 朝向策略（§1 混合方案，不是一套方案打天下）：
//   步兵   C：仅 flipX 镜像，不旋转（素材是站立人形，绕 Z 转会躺倒）
//   载具   A：全向旋转，zDeg = facing*Rad2Deg - def.spriteForwardDeg
//   建筑   D：不旋转（有固定占地与投影，一转就穿帮）
// ================================================================
using UnityEngine;

public class UnitView : MonoBehaviour
{
    [Header("Hierarchy (留空则自动向上/向下查找)")]
    [Tooltip("后坐位移节点。留空则用本节点的父节点（ArtRoot 的父 = RecoilPivot）")]
    public Transform recoilPivot;

    [Header("Tuning")]
    [Tooltip("履带载具转向速率（度/秒）。180° 用时 0.33s（§2.4）")]
    public float trackedTurnRate = 540f;
    [Tooltip("轮式载具转向速率（度/秒）。180° 用时 0.25s（§2.4）")]
    public float wheeledTurnRate = 720f;
    [Tooltip("步兵镜像死区。|cos(facing)|<=此值时保持上一帧，避免正上/正下疯狂闪烁")]
    public float flipDeadZone = 0.15f;

    private UnitEntity entity;
    private SpriteRenderer sr;
    private Transform artRoot;

    // 呼吸/抖动用的固定相位（按实例散开，避免同屏单位同相抖动成"波浪"）
    private float phaseA, phaseB;

    private bool flipXState = false;   // 步兵镜像当前状态（带死区保持）

    // 排序层 ID 缓存：排序层名解析只在启动做一次。
    // （每帧写 sr.sortingLayerName 会让 Unity 每次做一遍名字→ID 查找，白白浪费。）
    private static int worldSpriteLayerId = int.MinValue;
    private int lastSortingOrder = int.MinValue;

    void Awake()
    {
        entity = GetComponentInParent<UnitEntity>();
        sr = GetComponent<SpriteRenderer>();
        artRoot = transform;

        if (recoilPivot == null && transform.parent != null)
            recoilPivot = transform.parent;

        // 固定相位：不能用 Random.Range 每帧取（60fps 下会过热成噪点，§4.4）
        phaseA = (GetInstanceID() % 100) * 0.0628f;
        phaseB = (GetInstanceID() % 73) * 0.0873f;

        if (worldSpriteLayerId == int.MinValue)
            worldSpriteLayerId = SortingLayer.NameToID("WorldSprite");

        // 注意：不在这里改 sr.color。占位 prefab 的染色是"没接真图"时的可见性保障，
        // 强制白会把占位圆染成白圆反而看不见。真图接管时由
        // UnitEntity.ApplyDefinitionSprite() 负责把 color 复位为白。
    }

    void Update()
    {
        if (entity == null || entity.def == null || entity.isDead) return;

        float dt = Time.deltaTime;
        float t = entity.animTime;

        bool isMoving = (entity.hasMoveTarget || entity.target != null) && !entity.holdPosition;
        float h = SpriteWorldHeight();

        // 深度排序（§6）：单位与建筑必须同层同规则，否则会出现
        // "坦克钻进 HQ 后面还压着它"的穿帮——等距游戏最常见的排序 bug。
        UpdateSorting();

        // 后坐位移对所有类型统一在这里算（ArtRoot 只管朝向/呼吸，二者互不污染）
        ApplyRecoil();

        if (entity.def.isBuilding)
        {
            UpdateBuildingVisual(isMoving, h, t);
        }
        else if (entity.def.category == UnitCategory.Infantry)
        {
            UpdateInfantryVisual(isMoving, h, t, dt);
        }
        else
        {
            // Vehicle + Harvester 都走全向旋转，只是转向速率不同
            bool wheeled = entity.def.category == UnitCategory.Harvester;
            UpdateVehicleVisual(isMoving, h, t, dt, wheeled);
        }
    }

    // ---------------------------------------------------------------- 共用
    /// <summary>
    /// 深度排序（美术规格 §6）：单位与建筑共用同一层同一公式。
    /// WorldSprite 的 order = (1600 - y) * 2，取值 0…3200；
    /// FX 层从 5000 起，两层不重叠，避免特效被建筑切一半。
    /// y 越小 order 越大 → 屏幕下方压住上方。
    /// </summary>
    void UpdateSorting()
    {
        if (sr == null) return;
        sr.sortingLayerID = worldSpriteLayerId;

        int order = Mathf.RoundToInt((1600f - entity.transform.position.y) * 2f);
        // 只在变化时写：sortingOrder 是 native 属性，每帧无条件赋值在 300 单位同屏时
        // 是 1.8 万次/秒的无谓 P/Invoke。y 不动时 order 也不动。
        if (order != lastSortingOrder)
        {
            sr.sortingOrder = order;
            lastSortingOrder = order;
        }
    }

    /// <summary>
    /// 贴图在世界坐标下的高度。所有运动幅度都写成它的比例，
    /// 这样 PPU 改了也不需要重调参数（§4 总原则）。
    /// </summary>
    float SpriteWorldHeight()
    {
        if (sr != null && sr.sprite != null) return sr.sprite.bounds.size.y;
        return Mathf.Max(1f, entity.def.selectionRadius);
    }

    /// <summary>
    /// 后坐位移：世界方向 → RecoilPivot 的 local。
    /// 旧代码把世界方向直接赋给 localPosition，rotation≠0 时会被再旋转一次，
    /// 导致"朝右上开炮、车往左上退"。这里用父节点旋转的逆变换修正。
    /// </summary>
    void ApplyRecoil()
    {
        if (recoilPivot == null) return;

        if (entity.recoil <= 0f)
        {
            recoilPivot.localPosition = Vector3.zero;
            return;
        }

        float h = SpriteWorldHeight();
        // 回弹曲线 easeOut：起始猛、快速衰减，符合真实反冲（§5.1）
        float r = entity.recoil * entity.recoil;
        float offset = h * RecoilAmountOf(entity.def.unitId) * r;

        Vector3 worldBack = new Vector3(-Mathf.Cos(entity.facing), -Mathf.Sin(entity.facing), 0f);
        // localPosition 是相对父节点（根，位置由逻辑驱动、不旋转）的空间。
        // 这里显式做一次世界→父空间的旋转变换，即使根当前 rotation 为 identity 也不省略，
        // 避免以后根节点引入旋转时又退回同一个 bug。
        Transform parent = recoilPivot.parent;
        Vector3 localBack = parent != null
            ? parent.InverseTransformDirection(worldBack)
            : worldBack;
        recoilPivot.localPosition = localBack * offset;
    }

    /// <summary>后坐幅度 A（×贴图高），按武器类型取值，见规格 §5.3 表。</summary>
    static float RecoilAmountOf(string unitId)
    {
        switch (unitId)
        {
            case "rifleman":  return 0.010f;
            case "bazooka":   return 0.020f;
            case "sniper":    return 0.010f;
            case "mgtower":   return 0.015f;
            case "tank":      return 0.025f;
            case "heavytank": return 0.032f;
            case "artillery": return 0.040f;
            case "atcannon":  return 0.030f;   // 建筑抖动 ×2
            default:          return 0.020f;
        }
    }

    // ---------------------------------------------------------------- 建筑
    void UpdateBuildingVisual(bool isMoving, float h, float t)
    {
        // 建筑不旋转：zDeg ≡ 0（§7 总表 D 类）
        artRoot.localRotation = Quaternion.identity;

        // 开火震颤：固定 14Hz 正弦 + 每栋随机相位。
        // 旧代码用 Random.Range 每帧取，60fps 下会过热成噪点，且幅度 0.02 单位
        // 在 60 单位的建筑上几乎不可见。
        if (entity.recoil > 0f)
        {
            float a = h * 0.0015f * 2f;   // 建筑 ×2（§5.3 表末行）
            float k = entity.recoil * entity.recoil;
            artRoot.localPosition = new Vector3(
                a * k * Mathf.Sin(t * 90f + phaseA),
                a * k * Mathf.Sin(t * 90f + phaseB),
                0f);
        }
        else
        {
            artRoot.localPosition = Vector3.zero;
        }
        artRoot.localScale = Vector3.one;
    }

    // ---------------------------------------------------------------- 载具
    void UpdateVehicleVisual(bool isMoving, float h, float t, float dt, bool wheeled)
    {
        // === 朝向：zDeg = facing*Rad2Deg − spriteForwardDeg（§2.2）===
        // 素材的"前"实测朝图片 +X（tank 车体主轴 177°≈水平），不是 +Y。
        // 旧的 +90 假设素材朝上，导致向右开时炮管被转到 114.6°（朝左上）。
        float zDeg = entity.facing * Mathf.Rad2Deg - entity.def.spriteForwardDeg;

        float rate = wheeled ? wheeledTurnRate : trackedTurnRate;
        Quaternion target = Quaternion.Euler(0f, 0f, zDeg);
        artRoot.localRotation = Quaternion.RotateTowards(artRoot.localRotation, target, rate * dt);

        // === 运动表现（§4.2 履带 / §4.3 轮式）===
        float dy = 0f;
        float rollDeg = 0f;

        if (wheeled)
        {
            // 轮式：悬挂弹跳幅度是履带的 2 倍，固定 3.2Hz（轮胎气压决定，不随速度变）
            float amp = 0.012f * h;
            dy = amp * Mathf.Abs(Mathf.Sin(2f * Mathf.PI * 3.2f * t));
            rollDeg = 1.2f * Mathf.Sin(2f * Mathf.PI * 3.2f * t);
        }
        else if (isMoving)
        {
            // 履带：地面颠簸，幅度很小，只表明"不是死物"。
            // 【关键】履带车不弹跳——弹跳是轮式/生物特征，给坦克加弹跳是纸片感第一来源。
            float L = Mathf.Max(h, 1f) * 1.4f;               // 车体长边近似（§4.2）
            float pitch = 0.25f * L;                          // 履带节距
            float fTrk = Mathf.Clamp(entity.def.moveSpeed / pitch, 1.5f, 6f);
            dy = h * 0.006f * Mathf.Sin(2f * Mathf.PI * fTrk * t);
            // 1.37 非整数比，避免与颠簸同相变成规律抖动
            rollDeg = 0.6f * Mathf.Sin(2f * Mathf.PI * 1.37f * fTrk * t);
        }

        artRoot.localPosition = new Vector3(0f, dy, 0f);

        // 车体微抖叠加在朝向上（不改 localRotation 的基准，避免与 RotateTowards 打架）
        if (rollDeg != 0f)
        {
            artRoot.localRotation = artRoot.localRotation * Quaternion.Euler(0f, 0f, rollDeg);
        }
    }

    // ---------------------------------------------------------------- 步兵
    void UpdateInfantryVisual(bool isMoving, float h, float t, float dt)
    {
        // 步兵不旋转：素材是站立人形（主轴 91.5° 竖直、垂直拉长 2.12 倍），
        // 绕 Z 旋转会让他躺倒/倒挂，不可修复（§1）。
        artRoot.localRotation = Quaternion.identity;

        // === 镜像：带 ±0.15 死区 ===
        // 旧逻辑 facing > ±PI/2 没有死区，正上/正下时 cos 在 0 附近抖动 → 疯狂翻转。
        float c = Mathf.Cos(entity.facing);
        if (c < -flipDeadZone) flipXState = true;
        else if (c > flipDeadZone) flipXState = false;
        // |c| <= 死区（facing 在 ±81°~99°，几乎正上/正下）保持上一帧，不切换

        if (sr != null) sr.flipX = flipXState;

        // === 步频必须由移动速度倒推，不能手填（否则脚滑）===
        float speed = entity.def.moveSpeed;
        float strideLen = 0.6f * (2f * entity.def.selectionRadius);   // 一步 ≈ 0.6 个自身直径
        float stepFreq = Mathf.Clamp(speed / Mathf.Max(strideLen, 0.01f), 2f, 8f);

        // 正上/正下时视觉上应更慢、横向收 8% 伪装侧身（§4.1）
        float sinFacing = Mathf.Abs(Mathf.Sin(entity.facing));
        if (sinFacing > 0.99f) stepFreq *= 0.85f;

        float phase = Mathf.PI * stepFreq * t;

        float dy, rotZ, scaleY, scaleX;
        if (isMoving)
        {
            dy     = h * 0.035f * Mathf.Abs(Mathf.Sin(phase));
            rotZ   = 2.0f * Mathf.Sin(phase + Mathf.PI * 0.5f);
            scaleY = 1f - 0.02f * Mathf.Abs(Mathf.Sin(phase));
        }
        else
        {
            dy = 0f; rotZ = 0f;
            // 待机呼吸 0.6Hz（周期 1.67s）
            scaleY = 1f + 0.008f * Mathf.Sin(2f * Mathf.PI * 0.6f * t);
        }
        scaleX = 1f - 0.08f * sinFacing;

        artRoot.localPosition = new Vector3(0f, dy, 0f);
        artRoot.localRotation = Quaternion.Euler(0f, 0f, rotZ);
        artRoot.localScale = new Vector3(scaleX, scaleY, 1f);
    }
}
