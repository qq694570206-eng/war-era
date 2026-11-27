// ================================================================
// UnitView.cs — 单位表现层（挂在 ArtRoot 上）
//
// 生成者：lead-programmer
// 依据：docs/animation-facing-spec-v1.md §5.2（四层结构）
//       + 制作人裁定（四层、通道归属、后坐按像素）
//
// 【四层结构：每层只有一个写入者】
//   UnitPrefab            UnitEntity          transform.position     只由逻辑写
//    └ RecoilPivot                             localPosition          只写后坐
//       └ ArtRoot         UnitView             localRotation          只写朝向   ← 本脚本
//          └ AnimPivot                          localPosition+scale    只写呼吸/弹跳
//             └ SpriteRenderer                  localScale ≡ one       任何代码不许碰
//
// 为什么必须四层而不是三层：
//   三层时 ArtRoot 同时写 localRotation（朝向）和 localScale（呼吸），
//   呼吸一旦写 localScale 就会把 PPU 换算出的显示尺寸一起覆写掉 —— 尺寸基准消失。
//   拆出 AnimPivot 后，尺寸由 PPU 在导入期决定，运行时无人碰 SpriteRenderer 的 scale。
//
// 【三个通道，三层，不许混写】
//   前后/上下位移（弹跳、行走 bob） → AnimPivot.localPosition
//   伸缩（呼吸、压缩）             → AnimPivot.localScale
//   朝向角度                       → ArtRoot.localRotation
//   后坐位移                       → RecoilPivot.localPosition
//   位置/逻辑                      → UnitEntity.transform.position
//
// 缩放只走 PPU（TextureImporter.spritePixelsPerUnit）。动画规格 §7.1 的
// artRoot.localScale = k 公式已按裁定作废：PPU 是导入期常量，Sprite.bounds 天然正确。
//
// 朝向策略（§1 混合方案，不是一套方案打天下）：
//   步兵   C：仅 flipX 镜像，不旋转（素材是站立人形，绕 Z 转会躺倒）
//   载具   A：全向旋转，zDeg = facing*Rad2Deg - def.spriteForwardDeg（没有 +90）
//   建筑   D：不旋转（有固定占地与投影，一转就穿帮）
// ================================================================
using UnityEngine;

public class UnitView : MonoBehaviour
{
    [Header("Hierarchy")]
    [Tooltip("后坐位移节点（ArtRoot 的父）。留空则自动取 transform.parent")]
    public Transform recoilPivot;
    [Tooltip("呼吸/弹跳节点（ArtRoot 的子）。留空则自动取 transform.GetChild(0)")]
    public Transform animPivot;

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
    private static int worldSpriteLayerId = int.MinValue;
    private int lastSortingOrder = int.MinValue;
    private int lastSortingLayer = int.MinValue;

    void Awake()
    {
        entity = GetComponentInParent<UnitEntity>();
        artRoot = transform;

        if (recoilPivot == null && transform.parent != null)
            recoilPivot = transform.parent;
        if (animPivot == null && transform.childCount > 0)
            animPivot = transform.GetChild(0);

        // sr 从 AnimPivot 的子对象取（四层结构下 renderer 在最底层）
        sr = animPivot != null ? animPivot.GetComponentInChildren<SpriteRenderer>()
                               : GetComponentInChildren<SpriteRenderer>();

        // 固定相位：不能用 Random.Range 每帧取（60fps 下会过热成噪点，§4.4）
        phaseA = (GetInstanceID() % 100) * 0.0628f;
        phaseB = (GetInstanceID() % 73) * 0.0873f;

        if (worldSpriteLayerId == int.MinValue)
            worldSpriteLayerId = SortingLayer.NameToID("WorldSprite");

        // 注意：不在这里改 sr.color。占位 prefab 的染色是"没接真图"时的可见性保障，
        // 强制白会把占位圆染成白圆反而看不见。真图接管时由
        // UnitEntity.ApplyDefinitionSprite() 把 color 复位为白（补丁 ③）。
    }

    void Update()
    {
        if (entity == null || entity.def == null || entity.isDead) return;

        float dt = Time.deltaTime;
        float t = entity.animTime;

        bool isMoving = (entity.hasMoveTarget || entity.target != null) && !entity.holdPosition;
        float h = SpriteWorldHeight();

        UpdateSorting();

        // 后坐位移：写 RecoilPivot（不写 ArtRoot/AnimPivot）
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
    /// </summary>
    void UpdateSorting()
    {
        if (sr == null) return;

        if (lastSortingLayer != worldSpriteLayerId)
        {
            sr.sortingLayerID = worldSpriteLayerId;
            lastSortingLayer = worldSpriteLayerId;
        }

        int order = Mathf.RoundToInt((1600f - entity.transform.position.y) * 2f);
        // 只在变化时写：sortingOrder 是 native 属性，每帧无条件赋值在 300 单位同屏时
        // 是 1.8 万次/秒的无谓 P/Invoke。
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
    /// 贴图在世界坐标下的水平宽度 = 车体长边（履带节距的分母用这个，不用 SpriteWorldHeight）。
    /// FullRect 网格下 bounds.size.x = 贴图宽px / PPU，PPU 改了自动跟随。
    /// 兜底取高度，保证极窄贴图（理论上不会有）不会导致 pitch=0 → fTrk 无穷大。
    /// </summary>
    float SpriteWorldWidth()
    {
        if (sr != null && sr.sprite != null)
        {
            float w = sr.sprite.bounds.size.x;
            if (w > 0.01f) return w;
        }
        return SpriteWorldHeight();
    }

    /// <summary>世界单位 → 1080p 屏幕像素的换算系数（orthographicSize=300 时为 1.8）。</summary>
    static float PixelsToWorld(float pixels, Camera cam)
    {
        // orthographicSize 是半高；屏幕全高 = 2*orthoSize 世界单位。
        // 1080p 下 1080px / (2*300) = 1.8 px per world unit。
        float pxPerUnit = (cam != null && cam.orthographicSize > 0.01f)
            ? (Screen.height / (2f * cam.orthographicSize))
            : 1.8f;
        if (pxPerUnit < 0.01f) pxPerUnit = 1.8f;
        return pixels / pxPerUnit;
    }

    /// <summary>
    /// 后坐位移：世界方向 → RecoilPivot 的 local。
    ///
    /// 【必须做坐标变换】旧代码把世界方向直接赋给 localPosition，rotation≠0 时会被
    /// 再旋转一次 → 载具朝右上开炮、车却往左上退。
    ///
    /// 【幅度按像素定，不用比例】旧实现 recoil=0.15 × 0.3 = 0.045 世界单位 ≈ 0.08px，
    /// 人眼阈约 1px —— 这就是"开火完全没后坐感"的真正原因。
    /// 现按 1080p 像素值反算世界单位：步枪 1.5px / 坦克 3px / 火炮 5px。
    /// </summary>
    void ApplyRecoil()
    {
        if (recoilPivot == null) return;

        if (entity.recoil <= 0f)
        {
            // 只在非零时写，避免与其它层抢同一属性
            if (recoilPivot.localPosition != Vector3.zero)
                recoilPivot.localPosition = Vector3.zero;
            return;
        }

        // 回弹曲线 easeOut：起始猛、快速衰减，符合真实反冲（§5.1）
        float r = entity.recoil * entity.recoil;
        float offsetWorld = PixelsToWorld(RecoilPixelsOf(entity.def.unitId), Camera.main) * r;

        Vector3 worldBack = new Vector3(-Mathf.Cos(entity.facing), -Mathf.Sin(entity.facing), 0f);
        Transform parent = recoilPivot.parent;
        Vector3 localBack = parent != null ? parent.InverseTransformDirection(worldBack) : worldBack;
        recoilPivot.localPosition = localBack * offsetWorld;
    }

    /// <summary>后坐幅度（1080p 屏幕像素），按武器类型。人眼阈约 1px，低于它等于没有。
    ///
    /// 数值来源：docs/animation-facing-spec-v1.md §5.3 像素表。
    /// 这几张【没有实机验证过】，一律按规格取，不按手感擅自改小 ——
    /// 改小会让人以为是实现 bug 而不是调参，下次对文档会互相矛盾。
    /// 若将来实机觉得偏大，改这里并在注释写「实机试过 X px 偏大」，别只改数。
    /// </summary>
    static float RecoilPixelsOf(string unitId)
    {
        switch (unitId)
        {
            case "rifleman":  return 1.5f;
            case "sniper":    return 1.5f;   // 复用 rifleman 图，同幅度
            case "bazooka":   return 2.0f;   // 规格 §5.3
            case "mgtower":   return 3.0f;   // 规格 §5.3（建筑走抖动，不走位移）
            case "tank":      return 3.0f;
            case "heavytank": return 4.0f;
            case "artillery": return 5.0f;
            case "atcannon":  return 3.0f;   // 规格 §5.3
            default:          return 2.0f;
        }
    }

    // ---------------------------------------------------------------- 建筑
    void UpdateBuildingVisual(bool isMoving, float h, float t)
    {
        // 建筑不旋转：zDeg ≡ 0（§7 总表 D 类）。写 ArtRoot.localRotation。
        if (artRoot.localRotation != Quaternion.identity)
            artRoot.localRotation = Quaternion.identity;

        // 开火震颤：固定 14Hz 正弦 + 每栋随机相位，写 AnimPivot.localPosition。
        // 旧代码用 Random.Range 每帧取，60fps 下会过热成噪点。
        float a = h * 0.0015f * 2f;   // 建筑 ×2（§5.3 表末行）
        float k = entity.recoil * entity.recoil;
        SetAnimPosition(new Vector3(
            a * k * Mathf.Sin(t * 90f + phaseA),
            a * k * Mathf.Sin(t * 90f + phaseB),
            0f));
        SetAnimScale(Vector3.one);
    }

    // ---------------------------------------------------------------- 载具
    void UpdateVehicleVisual(bool isMoving, float h, float t, float dt, bool wheeled)
    {
        // === 朝向：zDeg = facing*Rad2Deg − spriteForwardDeg（§2.2）===
        // 【没有 +90f】素材的"前"实测朝图片 +X，不是 +Y。+90 等价于"假设素材朝上"，
        // 实测 tank 炮管朝 +23.2°，保留 +90 会把误差从 23° 放大到 67°。
        // spriteForwardDeg 从 UnitDefinition 读，不许在此写死 0（补丁 F / I / L）。
        float zDeg = entity.facing * Mathf.Rad2Deg - entity.def.spriteForwardDeg;

        float rate = wheeled ? wheeledTurnRate : trackedTurnRate;
        Quaternion target = Quaternion.Euler(0f, 0f, zDeg);
        artRoot.localRotation = Quaternion.RotateTowards(artRoot.localRotation, target, rate * dt);

        // === 运动表现（§4.2 履带 / §4.3 轮式），写 AnimPivot ===
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
            // 【关键】履带车不弹跳 —— 弹跳是轮式/生物特征，给坦克加弹跳是纸片感第一来源。
            //
            // 车体长边必须取【水平宽】，不能取 h*1.4：
            //   h 是贴图的竖直高，车体长边在 X 轴，两者不等价。旧写法 h*1.4 对 tank
            //   恰好也撞 6Hz 上限看不出差别，但 heavytank 实测 4.02Hz（应 5.62Hz，慢 40%），
            //   表现为"重坦接地感比中坦还迟钝"，而应该反过来。
            //   实测差异：tank +0.0%（都撞上限）/ heavytank +40.0% / harvester +17.6% / artillery +30.7%
            // 用 sr.sprite.bounds.size.x：FullRect 网格下它就是贴图宽/PPU，PPU 改了自己跟着变。
            float L = SpriteWorldWidth();
            float pitch = 0.25f * L;                          // 履带节距
            float fTrk = Mathf.Clamp(entity.def.moveSpeed / pitch, 1.5f, 6f);
            dy = h * 0.006f * Mathf.Sin(2f * Mathf.PI * fTrk * t);
            // 1.37 非整数比，避免与颠簸同相变成规律抖动
            rollDeg = 0.6f * Mathf.Sin(2f * Mathf.PI * 1.37f * fTrk * t);
        }

        // 车体微抖叠加在朝向上（ArtRoot），不写 AnimPivot 的 rotation
        if (rollDeg != 0f)
            artRoot.localRotation = artRoot.localRotation * Quaternion.Euler(0f, 0f, rollDeg);

        SetAnimPosition(new Vector3(0f, dy, 0f));
        SetAnimScale(Vector3.one);
    }

    // ---------------------------------------------------------------- 步兵
    void UpdateInfantryVisual(bool isMoving, float h, float t, float dt)
    {
        // 步兵不旋转：素材是站立人形（主轴 91.5° 竖直、垂直拉长 2.12 倍），
        // 绕 Z 旋转会让他躺倒/倒挂，不可修复（§1）。
        if (artRoot.localRotation != Quaternion.identity)
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
            // bob 从 0.035·h 降到 0.020·h：原值在 21.6px 高的人物上是 0.75px @4.7Hz，
            // 看起来像抽搐（补丁 N）。
            dy     = h * 0.020f * Mathf.Abs(Mathf.Sin(phase));
            // rotZ 从 ±2.0° 降到 ±1.2°，同上（补丁 N）
            rotZ   = 1.2f * Mathf.Sin(phase + Mathf.PI * 0.5f);
            scaleY = 1f - 0.02f * Mathf.Abs(Mathf.Sin(phase));
        }
        else
        {
            dy = 0f; rotZ = 0f;
            // 待机呼吸 0.6Hz（周期 1.67s）
            scaleY = 1f + 0.008f * Mathf.Sin(2f * Mathf.PI * 0.6f * t);
        }
        scaleX = 1f - 0.08f * sinFacing;

        // 步兵的 rotZ 是"摇摆"，属姿态不属朝向；写 AnimPivot 以免与 ArtRoot 的
        // identity 约束冲突（ArtRoot 步兵恒为 0）。
        SetAnimPosition(new Vector3(0f, dy, 0f));
        SetAnimRotation(rotZ);
        // 横向收缩写在 AnimPivot.localScale 的 X 上；Y 是呼吸/压缩。
        // SpriteRenderer 那层的 localScale 恒为 one，此处不碰。
        SetAnimScale(new Vector3(scaleX, scaleY, 1f));
    }

    // ---------------------------------------------------------------- 写入器
    // 这三个 setter 是唯一允许写 AnimPivot 的地方。集中在此便于审查
    // "每层只有一个写入者"这条约定。

    void SetAnimPosition(Vector3 p)
    {
        if (animPivot != null) animPivot.localPosition = p;
    }

    void SetAnimRotation(float zDeg)
    {
        if (animPivot != null) animPivot.localRotation = Quaternion.Euler(0f, 0f, zDeg);
    }

    void SetAnimScale(Vector3 s)
    {
        if (animPivot != null) animPivot.localScale = s;
    }
}
