# 单位朝向表现方案

- 版本：**v1.5**
- 状态：**当前有效**
- 专业域：动画（朝向表现 / 运动暗示 / 后坐 / 特效序列帧）
- 作者：动画总监 关建帧
- 取代：无（首版）
- **v1.5 变更**（回应 lead-programmer 的 commit `ddb8211` 与本轮独立复测）：
  - ① `tank` 的 **26 我用第二种方法独立复现**（末端收窄扫描，峰值 334–335° = 世界 26/25°），两法一致，可信度高
  - ② **推翻我自己 v1.3/v1.4 的"heavytank 两法自洽"论证** —— 4° 吻合不能证明那是炮管（同轴两个突起，主轴只会跟近的吻合）。`−20` 降级为**暂定，待人眼定**，新增 §2.3.2
  - ③ 新增 §13.3.2 **5 张素材被画布裁切**（warfactory 最严重，heavytank/harvester 高）—— 这才是 heavytank 测不准的真根因
  - ④ 新增 §19 实现差异清单、§20 三条 Editor 验收点（来自 v1.4）
  - ⑤ 撤回 v1.4 的"拆 AnimPivot 是阻塞项"表述：PPU 落地后尺寸基准不丢，代价只是非整数采样模糊
- **v1.4 变更**（本轮全部是"文档内部打架"的修正，不改方案选型）：
  - ① §2.3 新增「是否实际生效」列，并纠正 art 的结构指控（`Harvester` 已被 `else` 兜底收进全向旋转，真正不生效的只有 Defense 两栋）
  - ② `spriteForwardDeg` 终值统一为 **tank 26 / heavytank −20 / artillery 26 / harvester 0**；**§7 总表原写 23 / −11 / 223，与 §2.3 打架，本轮同步改掉**（v1.5 已把 heavytank 的 −20 标为暂定）
  - ③ 新增 **§4.0 `sprite.bounds.size.y` 是画布高不是内容高**（tank 高估 1.77×），给 `spriteFillRatio` 字段方案
  - ④ §7 步兵行原写 `0.035·h` / `2°`，与 §4.1 的 `0.020·h` / `1.2°` 打架，**以 §4.1 为准改掉**（§7 是程序照着写代码的那张表，不能留旧值）
  - ⑤ 新增 §19「主程序当前实现与本规格的差异清单」，逐条列出 `UnitView.cs` 现状未落地的 5 处
- **v1.3 变更**：
  - ① **PPU 改用 team-lead 裁定的逐资产值**（§7.1.1 为权威表），分类值（步兵 21.3/载具 10.7/建筑 3.7）作废；rifleman 因此从 12 单位改为 **15 单位 / 27px**，§4 幅度已重算但结论不变
  - ② 新增 §12.4.1 **爆炸尺寸 bug 已核实**（占位圆 ×25 = 1600 单位 = 屏幕高 2.7 倍，每次死亡全屏炸）
  - ③ §7 补 `UnitView` 挂载层级判断标准（实测：它当前**没有任何挂载点**，是要新建）
  - ④ 新增 §13.3.1 缺导入 Preset 是根因（一次性脚本不根治）
  - ⑤ 新增 §17 直接回答 art 的三个提问、§18 PPU 最终立场
- **v1.2 变更**：撤回"单位 5px"论断（素材从未接入）、新增 §13 接入清单与 §14 zoom 冲突、`spriteForwardDeg` 改炮管锚、后坐改按像素定
- v1.1 变更：§7.1 尺寸基准由 localScale 改为 PPU；§5.2 三层改四层；新增 §12 爆炸序列帧
- 上游依赖：`docs/art-direction-v2.md`、`docs/animation-direction-v1.md`、`docs/art-asset-integration-v1.md`、`docs/design-game-design-v2.md`
- 适用对象：`UnitView.cs`、`VFXManager.cs`、`TouchInputSystem.cs`、`PhaseBBootstrap.cs`、`UnitEntity.cs`、`SuperWeaponSystem.cs`、`UnitDefinition`（新增字段）
- 一句话结论：**三件事按顺序做，否则全是空谈：① 把 24 张图改成 `textureType: Sprite` 并接上 `def.sprite`（素材现在根本没进游戏）；② 修相机 zoom clamp（否则视野差 15 倍）；③ 旋转公式改成 `facing*Rad2Deg − spriteForwardDeg`，**不要保留 `+90`**。步兵走镜像、载具全向旋转、建筑不转。**

---

## 0. 本方案的实测依据（不是猜的）

对 `assets/` 下 9 张素材做了轮廓测量（连通域、核心区 PCA 主轴、逐列厚度剖面、外露细长结构方向）。三条硬数据：

| 素材 | bbox (px) | 长宽比 | 核心剪影主轴 | 外露细长结构方向 | 判读 |
|---|---|---|---|---|---|
| tank | 254×145 | 1.75 | 177°（近水平） | +24.6°（右上） | **俯视/近俯视载具**，炮管朝右略上 |
| heavytank | 254×205 | 1.24 | 164° | −11° | 近俯视，但素材本身**被生成歪了约 15°** |
| atcannon | 249×211 | 1.18 | 166° | −36° | 近俯视建筑，素材偏 −36° |
| mgtower | 254×238 | 1.07 | 55° | −41° | 近俯视建筑，素材偏 −41° |
| harvester | 254×249 | 1.02 | 35° | −138° | 剪影近正方形，测量置信度低，需目视复核 |
| rifleman | 247×254 | 0.97 | **91.5°（竖直）** | +43° | **站立人形（垂直拉长 2.12 倍）**，不是俯视 |
| bazooka | 246×244 | 1.01 | **79.5°（竖直）** | −42° | 站立人形 |

**两条由数据直接得出的结论：**

1. **素材不是统一的等距视角。** 载具接近俯视（可以绕 Z 旋转，几何成立）；步兵是站立的正面/四分之三视图（主轴竖直、垂直拉长 2 倍以上），**绕 Z 旋转会让他躺下**。所以不存在"一套旋转方案打天下"，必须分类。
2. **每张图的基准朝向都不一样**（实测从 −41° 到 +24.6°），这是 AI 生成素材的必然结果。任何单一硬编码偏移（比如现在的 `+90`）最多只对一张图成立。

**一条程序必须先修的阻塞项（v1.2 已核实到底，比 v1 的判断更严重）：**

我在 v1 里写"贴图约 5px 高，只有判定圈的 1/6"。art-model-director 反驳说该用锚定 PPU 3.20，那样 tank 是 28 单位 / 上屏 50px，缺口不存在。**我们俩都没错，但都问错了问题。真正的现状是：美术图一张都没被当 Sprite 加载过。**

实测三条（`Assets/_Project/Art/`，共 24 个 PNG）：

| 检查项 | 占位图 `placehold_circle.png`（脚本生成，必然正确） | 24 张美术图（全部） |
|---|---|---|
| `textureType` | **8**（= Sprite） | **0**（= Default 普通贴图） |
| `spriteMode` | 1（Single） | **0**（None） |
| `spritePixelsToUnits` | 1 | 100 |
| `alphaIsTransparency` | **1** | **0** |

`textureType: 0` 意味着 **Unity 不把这些图当 Sprite 资源，`LoadAssetAtPath<Sprite>` 会返回 null**。配套证据：
- 15 个 `UD_*.asset` 的 `sprite` 字段**全部是 `{fileID: 0}`（空）**；
- 全工程搜索 `.sprite =`，只有 `PhaseBBootstrap` 给占位图赋值、以及 `MakeFxPrefab` 内部赋值；**没有任何代码从 `def.sprite` 给运行时单位设 SpriteRenderer**。

**所以现状不是"尺寸不对"，是"美术素材从未接进游戏"**——游戏里跑的还是圆/方占位图。这也解释了为什么朝向问题在实机上"看不出来"。

**因此 §7.1 的 PPU 与 §2.3 的标定表都是"接入之后才有意义"的参数**，第一步必须是把导入参数改对（`textureType 8 / spriteMode 1 / alphaIsTransparency 1`），并把 `def.sprite` 填上、在 `UnitView` 里接一次赋值。详见 §13。

> **v1.2 撤回**：v1 里"单位只有 5px 高"这个论断**作废**。它是在"假设 PPU=100 且图已是 Sprite"的错误前提下算的，而图根本不是 Sprite。当前实际表现是 256px 的整张图（如果被当 Default 贴图渲染）或完全不显示（如果当 Sprite 取值为 null）。具体取决于渲染路径，需要主程序实机确认——**这个我不猜。**

---

## 1. 朝向策略选型

**结论：混合方案（红警2 同款路线），不选单一方案。**

| 类别 | 选定策略 | 理由 |
|---|---|---|
| **步兵类** | **C — 仅左右镜像翻转**（不旋转） | 素材是站立人形（主轴竖直 91.5°、垂直拉长 2.12×）。绕 Z 旋转会让士兵躺倒/倒挂，不可修复。红警2 步兵正是纯 `flipX`，玩家从未觉得不对——因为**人形的辨识特征是"站着"，不是"朝哪"**。 |
| **载具类**（履带 + 轮式） | **A — 全向旋转**（按 §2 改公式、按 §2.3 逐张标定） | 载具素材实测接近俯视（tank 长宽比 1.75、炮管完整出轮廓），**俯视图绕 Z 旋转在几何上是正确的**——俯视图本身就与方位角无关。注意：这是"俯视图全向旋转"，不是"把等距图硬转"。 |
| **建筑类**（含防御塔） | **D — 固定朝向，不旋转** | 建筑有固定占地与投影关系，一转就穿帮（墙体会歪）。防御塔的瞄准用**炮口特效方向 + 建筑微抖**表达，不转本体，见 §5.4。 |
| **不采用 B（八方向离散）** | — | 每兵种要 8 张图，当前每兵种只有 1 张；AI 补图成本 ×8，且 8 个方向的透视一致性几乎不可能保证。等 §3 的"等距判据"成立时再升级。 |

**关键区分判据（可自动跑，用于以后新单位入库）：**

```
长宽比 aspect = bboxW / bboxH
主轴角 axis   = 核心区 PCA 主轴（0° 水平 / 90° 竖直）

若 aspect >= 1.40 且 axis 在 ±30° 内（近水平）              -> 俯视载具，方案 A 全向旋转
若 aspect <= 1.10 且 axis 在 90°±30° 内 且 elongation >= 1.8 -> 站立人形，方案 C 镜像
其他                                                        -> 目视判定，默认走 C（保守）
```

---

## 2. 旋转角公式

### 2.1 现有代码错在哪

```csharp
// UnitView.cs:81 当前
float targetAngle = entity.facing * Mathf.Rad2Deg + 90f;
```

这个 `+90` 假设"素材的炮管朝图片正上方"。**实测 tank 炮管朝右偏上 24.6°、车体主轴水平，即素材的"前"是 +X（0°），不是 +Y（90°）。**

代入验证：`facing = 0`（向右移动）→ `zDeg = 90°` → 炮管从 24.6° 被转到 **114.6°（朝左上）**。单位向右开、炮口朝左，这就是团队看到的"横着走/倒着走"。

### 2.2 正确公式

```csharp
// 目标朝向角（度）
float zDeg = entity.facing * Mathf.Rad2Deg - def.spriteForwardDeg;

// 平滑（载具）
transform.localRotation = Quaternion.RotateTowards(
    transform.localRotation, Quaternion.Euler(0f, 0f, zDeg), TURN_RATE * Time.deltaTime);
```

- `def.spriteForwardDeg` = **新增数据字段**（float，度）：素材里"车头/炮管指向"相对图片 +X 轴的逆时针角。
- 数学理由：Unity 中 `rotation.z = θ` 表示把图片坐标系逆时针转 θ。素材前向在图片坐标里是 `spriteForwardDeg`，要让它落到世界方向 `facing`，需 `θ = facing − spriteForwardDeg`。`+90` 是 `spriteForwardDeg = −90`（"素材朝上"）的特例，而实测素材朝右。
- **建筑与步兵不使用此公式**（建筑 `zDeg ≡ 0`；步兵走 `flipX`）。

### 2.3 逐张标定表（`spriteForwardDeg` 初值）

**定义（务必与 art-model-director 对齐口径）**：
`spriteForwardDeg` = 素材里**车头/炮管的指向**相对图片 +X 轴的逆时针角（度），值域取 −180..180。

**v1.4 重要：本表新增「是否实际生效」列。** 原因：`UnitCategory` 枚举有 5 个值，而 `UnitView` 只分派 3 条路径。填了值不等于会生效。

#### 分派实况（已核对 `UnitView.cs` 当前实现）

```csharp
if (def.isBuilding)                       -> UpdateBuildingVisual   // 不旋转
else if (category == Infantry)            -> UpdateInfantryVisual   // 仅 flipX
else                                       -> UpdateVehicleVisual   // 全向旋转
```

`UnitCategory` 枚举实际有 **5 个值**：`Infantry / Vehicle / Harvester / Building / Defense`。

| 类别 | `isBuilding` | 实际走哪条分支 | 是否读取 `spriteForwardDeg` |
|---|---|---|---|
| Infantry（rifleman/bazooka/sniper） | false | `UpdateInfantryVisual` | **否**（镜像不用角度） |
| Vehicle（tank/heavytank/artillery） | false | `UpdateVehicleVisual` | **是** ✓ |
| **Harvester**（harvester） | false | 落到 `else` → `UpdateVehicleVisual`（轮式） | **是** ✓（v1.3 后的新 `else` 兜底） |
| Building（hq 等 7 栋） | **true** | `UpdateBuildingVisual` | 否 |
| **Defense**（mgtower/atcannon） | **true** | `UpdateBuildingVisual` | 否 |

**关键澄清（这条 art 说错了一半，我 v1.3 也说漏了一半）：**
- art 说"`Harvester` 和 `Defense` 不匹配任何分支 → 没有朝向表现"。**`Defense` 对**（它 `isBuilding: true`，被第一行短路），**`Harvester` 不对**——它 `isBuilding: false`、不是 Infantry，因此落到最终 `else`，**走的是全向旋转**。旧版 `UnitView` 确实是 `else if (category == Vehicle)` 会漏掉它，但当前实现已改成 `else` 兜底，Harvester 被收进来了。
- 真正"填了也白填"的只有 **Defense 两栋**（mgtower / atcannon），因为它们是 `isBuilding: true`。

#### 标定表

| 单位 | spriteForwardDeg | 是否实际生效 | 依据 | 置信度 |
|---|---|---|---|---|
| tank | **26** | ✓ 生效 | 我用"末端切线宽 / 距离 < 0.22 且 r > 95"扫全 360°，**唯一命中带 328–337°，峰值 334–335° = 世界 +26/+25°，r=139px、末端厚 13px、w/r=0.10**；程序独立用径向射线法得 24–26°（r=145、厚 63–73px）。两法独立且一致 | **高** |
| heavytank | **−20**（**暂定，待目视**） | ✓ 生效 | **三法分歧，我本轮未能独立确认。见 §2.3.2** | **低（四个值里唯一没有共识的）** |
| artillery | **26** | ✓ 生效 | 无独立素材，复用 tank 图 → **必须与 tank 同值**，否则同一张贴图有两个朝向 | 强制（不是测出来的） |
| harvester | **0** | ✓ 生效 | **双向对称，不填**（见下） | —— |
| rifleman / bazooka / sniper | — | 不生效 | 走镜像 | — |
| mgtower / atcannon | — | **不生效** | `isBuilding: true` → 走建筑分支 | — |
| 其他建筑 | — | **不生效** | 同上 | — |

**代入验证（tank，facing = 0 向右）**：`zDeg = 0 − 26 = −26°`。原图炮管 26°，旋转 −26° 后落到 **0°（正右）**。✓

#### 2.3.2 heavytank 的 −20 是四个值里唯一没有共识的（必须目视定）

三个独立测量、三个不同答案，而且**都落在同一条轴上**：

| 方法 | 执行者 | 结果 | 该方法的失效原因 |
|---|---|---|---|
| PCA 主轴 | art | +16°（≡ −164°） | 180° 周期，丢符号（§2.3.1 已证） |
| 外露细长结构（早期） | 我 | −11…−13° | 取了"最远端"，但**最远端是画布裁切造成的截断面，不是结构端点** |
| 径向射线（炮管锚） | lead-programmer | 采 −20° | 他自己声明"没能独立验证"：348° 端最远但厚 141px（与车体同粗），96–104° 最细但 r 只有 104px |
| 末端收窄扫描（本轮） | 我 | **两个候选，都被画布边缘截断** | 见下 |

我本轮用 `w/r < 0.22 且 r > 95` 扫全 360°，heavytank 出现**两个**合格候选，**两个的末端都贴画布边界**：

| 候选 | 图片角 | 世界角 | r | 末端厚 | 是否贴边 |
|---|---|---|---|---|---|
| A | 13° | **−13°** | 131 | 28.8 | **★ 被裁切** |
| B | 199° | **161°** | 134 | 21.2 | **★ 被裁切** |

**两个候选互为 174°，正好是同一条轴的两端。** 这就是为什么四种方法给出 +16 / −13 / −20 / −11 却"都沾边"——**它们测的是同一条轴上的不同取点，而这条轴两端都被画布切掉了，"哪端是炮口"这个信息在现有图片里就是缺的。**

**结论：`−20` 我不能确认，也不能否认。** 定这条值的唯一可靠手段是**人眼看图**，不是再换一种算法。判定方法给死：

1. 打开 `Art/Keyed/heavytank.png`，找**锥形收窄且带炮口环 / 制退器**的那一端
2. 若在**图片右下方（世界 −20° 附近）** → 保持 **−20**
3. 若在**图片左上方（世界 160° 附近）** → 改为 **160**（错的只是符号，加 180 即可）
4. **不要**用"哪端更长"判断——两端都被裁切，长度不可靠

> 这条也是给 art 的素材诉求：**heavytank 的炮管伸出画布了**。见 §13.4。


#### harvester 为什么填 0（我撤回 v1.2 的 223）

art 的重测说服了我，理由是硬的：

| 检验 | 结果 | 含义 |
|---|---|---|
| PCA `elongation` | **1.02** | 剪影几乎正圆，无主导长轴 |
| 窄长突起峰值 | **+45° 和 +225°** | 正好对向 |
| 两峰强度 | **2.78 vs 2.61** | 几乎等强（差 6%） |

**这图结构上双向对称，没有"车头"概念。** 我 v1.2 报的 223.3° 和 art 的 45° 相差 179°——**是同一双向轴的两端，两个都能成立等于都不是答案**。

**填 0 的正确理解不是"它朝 +X"，而是"不做补偿"**：`zDeg = facing − 0 = facing`，即图随运动方向整体转。铲斗两头一样，倒着开看不出来。

> art 建议"降到低置信度"，我更进一步：**不是置信度低，是这个值不该存在**。表里写 0 并注明"双向对称，不做补偿"，避免以后有人以为这是测出来的 0°。

### 2.3.1 为什么不能用 PCA 主轴定前向（这条会决定对错一半）

**主轴是 180° 周期的，它只说"车体长轴的方向"，不说"车头在哪一端"。** 从主轴推 `spriteForwardDeg`，有一半概率把车头判反 180°——而且错了之后，代码不会报错，只会在跑起来时看到"车倒着开"。

实测对照（主轴 vs 炮管锚）：

| 单位 | PCA 主轴 | 炮管/车头方向 | 差异 | 判读 |
|---|---|---|---|---|
| tank | 177° ≡ −3° | **+26°** | 29° | 车体本身斜置，两法不一致；以炮管锚为准 |
| heavytank | 164° ≡ −16° | **−20°（暂定）/ 161°（若符号反）** | 4° / 177° | **两法在 −16 附近吻合，但 −16 与 −20 相差 4° 不足以区分"同一端"与"相反端"** —— 详见 §2.3.2 |
| harvester | 24.9° | 双向 +45°/+225° 等强（2.78 vs 2.61） | — | **无车头**，不填（见 §2.3） |

> **v1.5 自我更正**：我 v1.3/v1.4 一直把"heavytank 主轴 −16 与炮管 −20 只差 4°"当作"两法自洽、符号已确认"的**强证据**。**这个推理是错的。**
> 4° 的吻合只能证明"主轴方向和某个突起方向接近"，**不能证明那个突起是炮管**——同一条轴上有两个突起（−13° 和 161°），主轴 −16° 与 −13° 差 3°、与 161° 差 177°，**它当然只会跟近的那个吻合，这不能区分真假。** 我把"数值接近"当成了"结构确认"，这是方法论错误。见 §2.3.2。

**唯一可靠的做法：用有方向性的结构定前后**（炮管、推土铲、驾驶室），并且**必须确认那个结构没被画布裁切**。主轴只能用来验证一致性，不能用来定符号。

> 注：art-model-director 在我早期数据上提出按主轴偏差填表（tank 0 / heavytank +16 / atcannon +14）。**heavytank 的 +16 与现在的 −20 相差 36°，符号确实不同**，但按本节结论，**+16 与 −20 都可能是错的**（两者都基于未确认哪端是炮口）。tank +26 这一条已被两法独立复现，可用。harvester 填 0 也对。**只有 heavytank 悬着。**

### 2.4 转向速率与转向-起步耦合

| 类别 | TURN_RATE | 180° 用时 | 理由 |
|---|---|---|---|
| 履带载具（tank / heavytank / artillery） | **540 °/s** | 0.33s | 坦克有重量，转向要有惯性；现有 720°/s（0.25s）偏轻飘 |
| 轮式载具（harvester / jeep） | **720 °/s** | 0.25s | 轮式更灵 |
| 建筑 | 不旋转 | — | — |
| 步兵 | 立即（镜像无中间态） | 0 帧 | 镜像切换本身无过渡可言 |

对应 `animation-direction-v1.md` §3"先转向后起步 0.1s"：

```csharp
float angErr  = Mathf.Abs(Mathf.DeltaAngle(currentZ, zDeg));   // 度
float speedMul = angErr > 45f ? 0.35f : 1f;                     // 履带 0.35 / 轮式 0.55
```
`angErr > 45°` 时先减速转身。这既是"倒车掉头"的物理直觉，也顺便盖掉了旋转瞬间的素材跳变。

---

## 3. 倾斜补偿：**不做 Z 轴 pre-rotation，理由可算**

**结论：`preRotationZ = 0`，不补偿。**

依据：若素材真实相机仰角为 `elev`，同一段屏幕竖直距离对应的地面距离被压缩 `sin(elev)` 倍。物体在屏幕平面内旋转时，这个压缩轴**不跟着转**，于是产生最大误差 `(1 − sin(elev))`。

| elev | sin(elev) | 最大失真 | 在 5px 贴图上 |
|---|---|---|---|
| 90°（纯俯视） | 1.000 | 0% | 0 px |
| 75° | 0.966 | 3.4% | 0.2 px |
| 70°（本批载具实测接近） | 0.940 | 6.0% | 0.3 px |
| 45°（真等距） | 0.707 | 29% | 1.5 px（30px 贴图上 9px，明显） |

本批载具剪影长宽比 1.18–1.75、主轴接近水平、炮管完整出轮廓，**属于 70° 以上的近俯视**。6% 压缩失真在移动端不可辨；加补偿层换取 6% 是负收益（多一个 transform 层、多一次矩阵运算、多一个美术要维护的参数）。

**升级条件（写死，避免以后拍脑袋）：** 只有当新素材满足 `长宽比 < 1.30 且剪影里出现竖直方向的矩形立面` 时，才判定为真等距。此时**不做倾斜补偿，而是直接升级到方案 B 八方向离散**，并在 `spriteForwardDeg` 之外增加 `dirIndex`（0–7）。这条判据比倾斜补偿更符合成本。

---

## 4. 运动表现：没有帧动画时靠什么"在动"

总原则一：**频率必须由移动速度倒推，不能手填。** 手填频率 = 脚滑（每循环位移 ÷ (循环帧数 ÷ 帧率) 必须等于移动速度）。

总原则二（v1.2 新增，这条决定幅度）：**幅度必须按"屏幕像素"定，不能按世界单位或比例拍。** 换算：

```
pxPerUnit = Screen.height / (2 × camera.orthographicSize)
偏移世界单位 = 目标像素 / pxPerUnit
写成 h 的比例 = (目标像素 / pxPerUnit) / h
```

参考值（1080p 屏高、`orthographicSize = 300` → **1.8 px/单位**，PPU 用 §7.1.1 裁定值）：

| 单位 | 画布高（`bounds.size.y`） | **实际内容高** | 内容占画布 | 屏占（按内容） | 备注 |
|---|---|---|---|---|---|
| rifleman / bazooka | 15.0 | **15.0** | 100% | **27.0 px** | 满画布 |
| tank | 28.0 | **15.9** | **56.6%** | **28.6 px** | **画布大量留白** |
| harvester | 28.0 | 28.0 | 100% | 50.4 px | 满画布 |
| heavytank | 32.0 | **25.6** | 80.1% | **46.1 px** | 有留白 |
| atcannon | 36.0 | **29.7** | 82.4% | 53.5 px | 有留白 |
| hq | 80.0 | 80.0 | 100% | 144.0 px | 满画布 |

### 4.0 一个必须知道的坑：`sprite.bounds.size.y` 不等于"看得见的高度"

**当前 `UnitView.SpriteWorldHeight()` 用的是 `sr.sprite.bounds.size.y`，它返回的是画布高度，不是内容高度。**

实测各资产内容填充率（我逐张跑了 alpha/RGB 轮廓）：

| 资产 | 内容占画布 | `bounds.h` | 真实内容高 | **高估倍数** |
|---|---|---|---|---|
| **tank** | **56.6%** | 28.0 | 15.9 | **1.77×** |
| heavytank | 80.1% | 32.0 | 25.6 | 1.25× |
| atcannon | 82.4% | 36.0 | 29.7 | 1.21× |
| radar | 94.5% | 44.0 | 41.6 | 1.06× |
| refinery | 96.5% | 56.0 | 54.0 | 1.04× |
| powerplant | 96.9% | 44.0 | 42.6 | 1.03× |
| barracks / warfactory | 98.0% | 48/56 | 47.1/54.9 | 1.02× |
| rifleman / bazooka / harvester / mgtower / hq | 100% | — | — | 1.00× |

**后果**：`h` 是所有运动幅度的基准（§4 全部公式都乘 `h`）。tank 的 `h` 被高估 1.77 倍 → **tank 的颠簸、后坐、车体抖动全部比设计值大 77%**。而 rifleman（100% 填充）完全准确。

**这意味着"同一套参数"在 tank 上偏大、在步兵上准确——正好和 §5.3 想解决的"参数在不同单位上强度不一"是同一类问题，但方向相反。**

**修法（二选一，推荐第一个）：**

```csharp
// 方案 A（推荐）：用 Sprite 的 tight mesh bounds——需要导入时设 spriteMeshType: Tight
// 但 Tight 会增加顶点数，且部分图轮廓碎，移动端不划算

// 方案 B（推荐用于本项目）：数据侧填一个内容填充率，一次性标定
float h = sr.sprite.bounds.size.y * def.spriteFillRatio;   // 新增字段，默认 1.0
// tank 0.57 / heavytank 0.80 / atcannon 0.82 / 其余 1.0
```

**方案 B 的理由**：填充率是**资产属性**，不随运行时变化，测一次写进 `UnitDefinition` 即可，零运行时开销、零额外顶点。而且它同时可用于"点击命中区域"和"血条避让"——不止动画用得上。

**优先级**：这条列入施工清单第 2.5 步（PPU 之后、运动参数之前）。**不修则 tank 的抖动比设计值大 77%，而 heavytank 大 25%，两者观感不一致却没有理由。**

> **v1.4 说明**：这条是 art-model-director 的结构质疑引出的。他没直接说这一点，但他指出"你漏了结构问题"促使我重读了 `UnitView.cs` 当前实现，才发现 `SpriteWorldHeight()` 的返回值本身就是错的。**这是本轮最有价值的发现。**

**由此得出的一条硬规则（贴图越小越不能弹跳）：**

> 整体 bob 的幅度若超过贴图**实际内容高**的 **8%**，观感会从"走路"变成"蹦跳"。
> - **步兵 27px**（满画布，准确）：8% = 2.2px。取 0.54px，保守但正确。
> - **载具按真实内容高算**：tank 真实 28.6px → 8% = 2.3px，可做 1–1.8px 颠簸。

统一记 `t = entity.animTime`（`UnitEntity` 已每帧累加），`h = 贴图在世界坐标下的高度`。

> **通道归属约定（本节所有公式都受此约束，§7.1 四层结构）**：下文表格里的 `posY` 写在 `AnimPivot.localPosition`；`scaleX / scaleY` 写在 `AnimPivot.localScale`（写成 `1 ± δ` 形式，δ 恒 < 0.04）；`rotZ` 写在 `ArtRoot.localRotation`。**三者不同层，不许混写同一节点**，否则又会回到 §7.1 那个"每帧覆写"的坑。`SpriteRenderer.localScale` 处处恒为 one。

> **本节所有"px"值的适用前提**：`orthographicSize = 300` 且缩放未被夹紧。**当前 `TouchInputSystem.cs:15-16` 的 `minZoom = 5f / maxZoom = 20f` 与 `orthographicSize = 300` 冲突**（Bootstrap 从未给这两个字段赋值），玩家首次双指缩放会把 halfHeight 从 300 瞬间夹到 20 或 5，视野缩小 **15 倍**，所有 px 数值失效。修法见 §14。**这个 bug 不修，本节整节无法验收。**

### 4.1 步兵（rifleman / bazooka / sniper）

```csharp
float strideLen = 0.6f * (2f * def.selectionRadius);            // 一步 ≈ 0.6 个自身直径
float stepFreq  = Mathf.Clamp(speed / strideLen, 2f, 8f);       // 步/秒
float phase     = Mathf.PI * stepFreq * t;                      // 每步一个完整上下
// rifleman: 45 / 9.6 = 4.69 步/秒
```

| 通道 | 公式 | rifleman 实际值（h=15 单位，屏占 27.0px） |
|---|---|---|
| 垂直弹跳 | `posY = h * 0.020f * Mathf.Abs(Mathf.Sin(phase))` | 0.30 单位 = **0.54 px** |
| 左右摇摆 | `rotZ = 1.2f * Mathf.Sin(phase + Mathf.PI * 0.5f)` | ±1.2° |
| 落地压缩 | `scaleY = 1f - 0.02f * Mathf.Abs(Mathf.Sin(phase))` | 0.98–1.00（比例量，与像素无关） |
| 水平镜像 | `flipX`，带 0.15 死区（见下） | — |
| 待机呼吸 | `scaleY = 1f + 0.008f * Mathf.Sin(2π * 0.6f * t)` | 周期 1.67s（比例量） |

> **v1.3 复核（PPU 裁定后重算）**：rifleman 由 12 单位改成 **15 单位 / 27px**。`0.020·h` 换算 = **0.54px**，占身高 2%，在 27px 人物上是"轻微上下"而非"抽搐"，**保留 0.020 不改**。
>
> 若未来想更明显，上限是 `0.035·h` = 0.94px（3.5% 身高）——**超过这个就回到抽搐区**。v1 的原始 `0.035` 在当时"h=12"的错误假设下算成 0.75px 尚可，但在 27px 上反而更明显，所以 v1.2 的 0.020 是对的，**PPU 改了不影响这个结论**。
>
> **步兵的可读性靠"压缩 + 镜像"，不靠"上下跳"**——27px 高的人形上，1px 位移与 1.2° 倾斜的醒目度相当，但倾斜更不容易被误读成"在跳"。这是尺寸决定的，不是审美偏好。

**镜像防抖（必须写，否则单位在正上/正下时会疯狂闪）：**

```csharp
float c = Mathf.Cos(entity.facing);
if (c < -0.15f)      sr.flipX = true;
else if (c > 0.15f)  sr.flipX = false;
// |c| <= 0.15（facing 在 ±81°~99°，几乎正上/正下）保持上一帧，不切换
```
（现有 `facing > PI/2 || facing < -PI/2` 没有死区，正上正下会抖。）

**朝里/朝外（facing 近 ±90°）的暗示：** 人形图在正上/正下没有对应视角。用 `scaleX = 1 − 0.08·|sin(facing)|`（横向收 8%）伪装侧身，同时 `stepFreq × 0.85`（往纵深走视觉上应更慢）。**不做旋转。**

### 4.2 履带载具（tank / heavytank / artillery）

**履带车不弹跳。** 弹跳是轮式/生物的特征，给坦克加弹跳是"纸片感"的第一来源。

```csharp
float L      = spriteWorldLongAxis;                     // 车体长边，tank ≈ 24 单位
float pitch  = 0.25f * L;                               // 履带节距 ≈ 6 单位
float fTrk   = Mathf.Clamp(speed / pitch, 1.5f, 6f);     // Hz，上限 6Hz 防频闪
// tank: 60 / 6 = 10 -> clamp 到 6 Hz
```

| 通道 | 公式 | 说明（tank: h=28 单位，屏占 50.4px） |
|---|---|---|
| 地面颠簸 | `posY += h * 0.006f * Mathf.Sin(2π * fTrk * t)` | 0.17 单位 = **0.30 px**。幅度很小，只表明"不是死物" |
| 车体微抖 | `rotZ += 0.6f * Mathf.Sin(2π * 1.37f * fTrk * t)` | 1.37 非整数比，避免与颠簸同相变成规律抖动 |
| 加减速压缩 | `scaleY = 1f - 0.02f * Mathf.Clamp01(accel / 40f)` | 起步略压、刹车略拉（比例量） |
| 履带暗示 | **不画履带**。车尾每 0.12s 生成 1 撮尘土（复用 `VFXManager`） | 见下 |

> **v1.2 复核**：`0.006·h` 在 tank 上是 0.30px，低于视觉阈值但配合 6Hz 不明显频闪，可接受；**这是"有生命感"的下限，不是"看得出在动"的上限**——载具的"在动"主要靠扬尘与位移，不靠车体抖。若实机上完全看不见，优先加大扬尘密度，**不要加大 bob**（加大 bob 会让坦克变蹦床）。

**为什么禁止画履带滚动：** 单张静态图没有履带层，无法滚 UV；画上去的履带纹在旋转时会变成"整块布丁在转"。**用扬尘 + 车体抖动表达接地，成本 0，且不会被视角拆穿。**

**上限 6Hz 的理由：** 移动端 30fps 渲染下，高于 6Hz 的周期运动会频闪混叠（奈奎斯特一半是 15Hz，但小屏幕 + 无运动模糊，实践中 6Hz 就开始闪）。

### 4.3 轮式载具（harvester / 后续 jeep）

| 通道 | 公式 |
|---|---|
| 悬挂弹跳 | `posY += h * 0.012f * Mathf.Abs(Mathf.Sin(2π * 3.2f * t))` |
| 车身侧摆 | `rotZ += 1.2f * Mathf.Sin(2π * 3.2f * t)` |
| 转向侧倾 | 转弯时 `rotZ += 1.5f * Mathf.Sign(角速度)`（轮式过弯有离心侧倾，履带没有） |

轮式弹跳幅度是履带的 2 倍、频率固定 3.2Hz（轮胎气压决定，不随速度变，随速度只变幅度 ±30%）。

### 4.4 建筑

不旋转、不位移。开火抖动幅度按体积算：

```csharp
float a = h * 0.0015f;   // 现有硬编码 0.02 单位，在 60 单位的建筑上等于没有
transform.localPosition = new Vector3(
    a * 0.5f * Mathf.Sin(t * 90f + phaseA),
    a * 0.5f * Mathf.Sin(t * 90f + phaseB), 0f);
```
改为固定 90 rad/s（≈14Hz）的正弦 + 每栋建筑随机相位：看起来是"金属震颤"。现有 `Random.Range` 每帧抖动在 60fps 下会过热成噪点。

---

## 5. 开火后坐（现有实现有两处必须改）

### 5.1 问题一：后坐只活 3 帧

```csharp
// UnitEntity.cs:71
if (recoil > 0) recoil -= dt * 3f;    // recoil 初值 0.15 -> 生命 0.05s
```
0.05s = 60fps 下 **3 帧**。后坐是打击感的主要来源，3 帧等于没有（玩家甚至看不见炮管回缩）。

**改为按"动作时长"配置，而不是按"衰减速率"配置：**

```csharp
// UnitEntity.cs
public float recoil      = 0f;              // 0..1 归一化
public float recoilDecay = 1f / 0.18f;      // = 1 / 持续时间

if (recoil > 0f) recoil = Mathf.Max(0f, recoil - dt * recoilDecay);
// 开火时：recoil = 1f;
```
**回弹曲线用 easeOut：** `r = recoil * recoil` ——后坐起始猛、快速衰减，符合真实反冲。

> **v1.4 补一个必须标红的坑：点火值必须是 `1f`，不是 `0.15f`。**
> `UnitEntity.cs` 现在写的是 `recoil = 0.15f;`（旧代码遗留）。因为幅度乘的是 `recoil²`，0.15 → `r = 0.0225`，**只剩标称值的 2.25%**；而且衰减到 0 只需 `0.15 / (1/0.18) = 0.027s = 1.6 帧`（设计 0.18s = 11 帧）。
> 结果：tank 后坐实际 **0.068px**、存活 **1.6 帧**。**两处缺陷叠加，等于没有。** 详见 §19.1。
> **"归一化到 0..1" 这句话必须落实为字面上的 `1f`**——只要点火值不是 1，§5.3 那张像素表就是一张废纸。

### 5.2 问题二：后坐方向与旋转共用同一个 transform

```csharp
// UnitView.cs:100-105 当前：同一个 transform 上既设 rotation 又设 localPosition
Vector2 recoilDir = -new Vector2(Mathf.Cos(entity.facing), Mathf.Sin(entity.facing));
transform.localPosition = recoilDir * recoilOffset;
```
`recoilDir` 是**世界方向**，却赋给 `localPosition`。当 `rotation ≠ 0` 时它会被再旋转一次 → 后坐方向错（载具朝右上开炮，车却往左上退）。

**结构改为四层（程序照此建预制体）：**

```
UnitEntity        位置：逻辑移动，不旋转
 └─ RecoilPivot   后坐位移：只写 localPosition
     └─ ArtRoot   朝向 rotation：只写 localRotation
         └─ AnimPivot  呼吸/压缩：只写 localPosition + localScale
             └─ SpriteRenderer  贴图，localScale 恒 one
```

```csharp
// RecoilPivot 上
Vector3 worldBack = new Vector3(-Mathf.Cos(facing), -Mathf.Sin(facing), 0f);
recoilPivot.localPosition = Quaternion.Inverse(transform.rotation) * worldBack * offset;

// ArtRoot 上（不受后坐与呼吸影响）
artRoot.localRotation = Quaternion.Euler(0f, 0f, zDeg);

// AnimPivot 上（只写自己那层，不碰 SpriteRenderer）
animPivot.localPosition = new Vector3(0f, bobY, 0f);
animPivot.localScale    = new Vector3(1f + sx, 1f + sy, 1f);
```
这样"炮管缩回"、"车体转向"、"呼吸压缩"三者互不污染。

### 5.3 后坐参数与攻击三段时序

**v1.2 重要修正**：v1 里我给的是 `offset = h * 0.025f`。换算成像素（h 是**世界单位**，这里不是屏高）：

| 单位 | h(世界) | v1 公式 offset = 0.025·h | 换算像素（1.8 px/单位） | 判定 |
|---|---|---|---|---|
| tank | 28 | 0.70 单位 | **1.26 px** | 太小 |
| rifleman | 12 | 0.30 单位 | **0.54 px** | **完全看不见** |
| hq | 80 | 2.00 单位 | 3.60 px | 勉强 |

**而现有代码 `UnitView.cs:102` 是 `entity.recoil * 0.3f`，`recoil` 最大 0.15 → 0.045 世界单位 = 0.08 px。等于零。** 这就是为什么现在开火没有任何后坐感——不是参数偏小，是**在正交 300 的视野下完全不可见**。

**改为按屏幕像素直接定（换算回世界单位）：**

```csharp
// 目标像素 → 世界单位
float pxPerUnit = Screen.height / (2f * cam.orthographicSize);
float offsetWorld = targetPx / pxPerUnit;
float offset = offsetWorld * (recoil * recoil);   // easeOut
```

| 武器 | 前摇 | 命中/顿帧 | 收招 | 总帧(60fps) | **目标像素** | 后坐时长 | 对应 fireRate |
|---|---|---|---|---|---|---|---|
| 步枪 rifleman | 6f (100ms) | 1f 闪光 | 8f | 15f (250ms) | **1.5 px** | 0.10s | 0.8s |
| 火箭 bazooka | 14f (233ms) | 6f | 12f | 32f (533ms) | **2.0 px** | 0.15s | 1.5s |
| 机枪 mgtower | 8f | 持续 24f | 8f | 40f (667ms) | **1.5 px/发** | 0.08s/发 | — |
| 坦克炮 tank | 12f (200ms) | 8f 顿帧 | 12f | 32f (533ms) | **3.0 px** | 0.18s | 1.2s |
| 重炮 heavytank | 18f (300ms) | 10f 顿帧 | 16f | 44f (733ms) | **4.0 px** | 0.22s | — |
| 火炮 artillery | 24f (400ms) | 10f 顿帧 | 20f | 54f (900ms) | **5.0 px** | 0.26s | 2.5s |
| 防空炮 atcannon（建筑） | 10f | 6f | 10f | 26f (433ms) | **3.0 px**（走建筑抖动） | 0.12s | 1.8s |

> **这张表的"目标像素"是峰值，成立前提是点火值 = 1.0。** 见 §5.1 的红字与 §19.1。当前实现点火 0.15 → 全表 ×0.0225。

**为什么用像素而不用 h 的比例（v1 的做法）**：`h` 比例在不同尺寸单位上给出完全不同的视觉强度。tank（50px）上 1.26px 尚可，rifleman（27px）上 0.54px 就是不可见——**同一套比例参数在两类单位上一边够用一边失效**。而"后坐要看得见"这个需求本身是像素级的（人眼分辨阈约 1px），所以参数就该按像素定，世界值由 `pxPerUnit` 反算。

**每条时长的理由（不标理由的时长等于拍脑袋）：**
- **步枪前摇 6 帧**：玩家不需要预判自己的步兵，但需要枪声与闪光对齐；超过 10 帧连射会粘手。
- **坦克炮前摇 12 帧（200ms）**：给对手约 0.2s 反应窗口，这是"看到炮塔转过来就该跑"的博弈基础；低于 8 帧等于无预警。
- **火炮前摇 24 帧（400ms）**：`fireRate 2.5s` 的重火力，长前摇是它"贵且慢"的视觉价格。
- **坦克收招 12f vs 重炮 16f**：收招 = 破绽。重炮必须更笨重，否则重炮无双。
- **顿帧 3–10 帧**：命中瞬间后座到位后**停住**，这是"打到了"的唯一证据。

**车体上仰角**（与像素无关，用角度）：`rotZ 附加 = -1.5° * (recoil*recoil) * Mathf.Sign(Mathf.Cos(facing))`。

### 5.4 建筑防御塔的"瞄准"（不转本体怎么表达方向）

`mgtower` / `atcannon` 有朝向需求但建筑不转，用三条替代手段（**注意：此处"三条"指瞄准表达手段，与 §7.1 的四层节点结构无关**），成本从低到高：

1. **炮口特效定向（必做）**：`SpawnMuzzleFlash` 的坐标用 `facing` 计算（现有代码已在算方位），并把闪光贴图**沿 facing 拉长 1.8×**（`localScale` 对齐即可，零新资产）。
2. **建筑微转 ±4°（可选）**：`rotZ = 4f * Mathf.Sin(facing)` ——不是朝目标，而是"朝目标方向偏一点"，玩家会读成"它在瞄"。超过 4° 开始穿帮。
3. **不做**：任何炮塔层旋转（单张图无分离炮塔层）。

---

## 6. 验收点（3 条，必须真人上机看）

**① 朝右移动时炮管指向右侧且图像不倒置。**
操作：选 3 辆坦克，命令向右、向上、向左、向下各移动一次。判定：炮管始终指向**移动方向前方**，车身长轴与运动方向夹角 < 15°，任何方向下都不出现"炮管朝后/朝左"。

**② 步兵绝不出现"躺倒"。**
操作：命令步兵向右、向左、向正上、向正下移动。判定：四个方向上人物**始终直立**（头部朝屏幕上方）；左右移动时仅发生镜像切换；正上/正下时观察 3 秒无翻转闪烁。

**③ 静止与移动一眼可分，且坦克不弹跳。**
操作：静止坦克与移动坦克并排截屏对比。判定：移动坦克有轻微颠簸与车尾扬尘，静止坦克完全静止；**坦克无步兵式大幅弹跳**（幅度 < 0.01×贴图高）。

> **前置第 0 条**（不属于表现验收但阻塞以上三条）：贴图世界宽度 ≥ 1.2 × `selectionRadius` 直径，即 1080p 下单位不小于 30px 高。**在 5px 的贴图上做朝向验收没有意义。**

---

## 7. 单位类型总表（程序员照着写代码用这张）

| 单位类型 | 朝向策略 | 旋转公式 | 运动表现参数 | 验收点 |
|---|---|---|---|---|
| **步兵** rifleman / bazooka / sniper | C 镜像翻转，**不旋转** | `zDeg ≡ 0`；`flipX`，死区 `\|cos(facing)\|>0.15` | `stepFreq = clamp(speed/(0.6·2·selR), 2, 8)`；`posY = h·0.020·\|sin(π·stepFreq·t)\|`；`rotZ = 1.2°·sin(π·stepFreq·t + π/2)`；`scaleY = 1 − 0.02·\|sin\|`；`scaleX = 1 − 0.08·\|sin(facing)\|`；待机呼吸 0.6Hz / 0.008 | ② |
| **履带载具** tank / heavytank / artillery | A 全向旋转 | `zDeg = facing·Rad2Deg − spriteForwardDeg`，`RotateTowards` **540°/s**；`spriteForwardDeg`: **tank 26 / heavytank −20 / artillery 26**（§2.3） | `fTrk = clamp(speed/(0.25·L), 1.5, 6)` Hz；`posY += h·0.006·sin(2π·fTrk·t)`；`rotZ += 0.6°·sin(2π·1.37·fTrk·t)`；`scaleY = 1 − 0.02·(accel/40)`；**禁止弹跳**；车尾扬尘 0.12s/撮 | ①③ |
| **轮式载具** harvester / jeep | A 全向旋转 | 同上，`RotateTowards` **720°/s**；`spriteForwardDeg = 0`（**双向对称，不做补偿**，§2.3） | `posY += h·0.012·\|sin(2π·3.2·t)\|`；`rotZ += 1.2°·sin(2π·3.2·t)`；过弯 `rotZ += 1.5°·sign(ω)` | ①③ |
| **建筑**（生产/经济） | D 固定，不旋转 | `zDeg ≡ 0`（不写旋转代码） | `posX/posY = ±h·0.0015`；震颤 14Hz：`a·sin(90t + φ)` | ③ |
| **防御建筑** mgtower / atcannon | D 固定 + 特效定向 | `zDeg ≡ 0`；可选 `rotZ = 4°·sin(facing)` | 同上；炮口闪光沿 `facing` 拉长 1.8× | ③ + 肉眼确认炮口朝向敌侧 |
| **飞行器**（未来） | A 全向旋转 | 同上，`spriteForwardDeg` 逐张标定 | 悬停 `posY += h·0.02·(sin(2π·0.5t) + 0.5·sin(2π·0.37t))`（双相位，必须不规律） | ①③ |

### 7.1 贴图缩放（前置修正，必须与朝向一起改）

> **v1.1 修订**：本节初版写成"在 `ArtRoot` 上乘 `k`"。经 art-model-director 指出并经我复核代码，**这样做会被每帧覆写而失效**，改为 PPU 方案 + 四层结构。见下。

**方案：改 PPU，不靠 localScale 做尺寸基准。**

```csharp
// UnitDefinition 新增 float spriteTargetHeight;   // 期望世界高度
// 拼装时一次性设置，之后不再碰：
sr.sprite = sprite;   // PPU 已在导入期写好，运行时无需任何代码
```

> **不提供"在最内层设 scale 代替 PPU"的退路。** 那等于把尺寸基准重新放到一个会被误写的通道上，与本节的整个结论矛盾。**PPU 是唯一尺寸基准，`SpriteRenderer.localScale` 恒为 `Vector3.one`，任何代码不许写它。** 若 meta 无法修改，说明导入流程有问题，应修导入流程，不应绕到 scale 上。

| ~~单位~~ | ~~目标世界高度~~ | ~~PPU~~ | **← 整表作废，见 §7.1.1** |
|---|---|---|
| ~~rifleman / bazooka / sniper~~ | ~~12 单位~~ | ~~21.3~~ | 作废 |
| ~~tank / harvester（长边）~~ | ~~24 单位~~ | ~~10.7~~ | 作废 |
| ~~heavytank~~ | ~~26 单位~~ | ~~9.8~~ | 作废 |
| ~~建筑（hq/warfactory/refinery 等）~~ | ~~60–80 单位~~ | ~~3.7~~ | 作废 |

> **v1.3：上表的分类值已作废，改用下面的逐资产裁定值。** 保留此表仅为记录推理过程。
> 作废理由：建筑目标高度从 mgtower 32 到 hq 80 差 2.5 倍，一刀切 3.7 会让机枪塔（32 单位）显示得比基地（80 单位）还大 2.5 倍。**分类值只适用于同类单位尺寸接近的情况，建筑明显不满足。**

### 7.1.1 PPU 逐资产裁定值（v1.3，**以此表为准**）

由 team-lead 裁定，与 `art-asset-integration-v1.md` §2 一致。**这两处必须是同一张表，改一处必须改另一处。**

| 资产 | PPU | 世界高 h | 屏占（orth=300，1.8px/单位） | 8% 安全预算 |
|---|---|---|---|---|
| hq | **3.20** | 80.0 | 144.0 px | 11.5 px |
| warfactory | **4.57** | 56.0 | 100.8 px | 8.1 px |
| refinery | **4.57** | 56.0 | 100.8 px | 8.1 px |
| barracks | **5.33** | 48.0 | 86.5 px | 6.9 px |
| powerplant | **5.82** | 44.0 | 79.2 px | 6.3 px |
| radar | **5.82** | 44.0 | 79.2 px | 6.3 px |
| atcannon | **7.11** | 36.0 | 64.8 px | 5.2 px |
| heavytank | **8.00** | 32.0 | 57.6 px | 4.6 px |
| mgtower | **8.00** | 32.0 | 57.6 px | 4.6 px |
| tank | **9.14** | 28.0 | 50.4 px | 4.0 px |
| harvester | **9.14** | 28.0 | 50.4 px | 4.0 px |
| rifleman | **17.07** | 15.0 | **27.0 px** | 2.2 px |
| bazooka | **17.07** | 15.0 | **27.0 px** | 2.2 px |

**注意 rifleman / bazooka 是 15 世界单位 / 27px，不是我 v1.2 里写的 12 单位 / 21.6px。** §4 的幅度参数已按 27px 重算（见 §4.0 修订、§4.1）。

**四层结构（每层只有一个写入者，这是关键）：**

```
UnitEntity        位置：逻辑移动。任何表现层不许写这里
 └─ RecoilPivot   后坐位移：只写 localPosition（§5.2）
     └─ ArtRoot   朝向：只写 localRotation（§2.2）   ← UnitView 实际挂这层（可接受，见下）
         └─ AnimPivot  呼吸/压缩/抖动：只写 localPosition + localScale
             └─ SpriteRenderer   ← 贴图。谁的 localScale 都不许碰
```

> **v1.3 补：`UnitView` 自己挂在哪一层，是改造时的头号陷阱。**
> **v1.4 修正：不再规定"必须挂在 RecoilPivot 上"——规定不变式，不规定挂载点。**
> 判断标准只有一条：**一个通道只能有一个写入者。** 现有实现把 `UnitView` 挂在 `ArtRoot` 上并持 `recoilPivot` 引用（`PhaseBBootstrap.cs:303`），它写 `recoilPivot.localPosition` + `artRoot.localRotation` + `artRoot.localPosition` + `artRoot.localScale`——挂载点与文档原写法不同，但因为三个引用都是显式的，**通道没串**，可以接受。
> 真正不能接受的是写 `transform.xxx`（自己所在节点）——那样挂在哪层就污染哪层。**规则改写为：`UnitView` 里除 `Awake` 外不许出现裸的 `transform.localPosition / localRotation / localScale`，一律走显式引用。**
> 若挂错（比如挂 `AnimPivot` 上又写 `transform.localPosition`），会出现 `RecoilPivot` 与 `AnimPivot` **抢同一个 `localPosition`**，表现为后坐和呼吸互相覆盖、走起来一顿一顿。

**为什么建议拆出 `AnimPivot`（v1.4 收窄了这条的严重程度）：**

`UnitView.cs` 在 `UpdateInfantryVisual` 末尾对 **SpriteRenderer 所在节点**（`ArtRoot`）直接赋值 `artRoot.localScale = new Vector3(scaleX, scaleY, 1f)`。

> **v1.4 自我校准**：我 v1.1–v1.3 一直写"尺寸基准会完全消失"。**在 PPU 方案落地后这句话不再成立**——尺寸基准现在由 `TextureImporter.spritePixelsPerUnit` 决定，`localScale` 的 1.0 只是"不额外缩放"，写成 `0.92` 也只是"额外压 8%"，基准不会丢。
> 所以这条的真实代价是**精度损失，不是基准丢失**：`scaleX = 1 − 0.08·|sin(facing)|` 在 27px 宽的人形上压掉约 1.6px，非整数倍重采样 → **横向轻微模糊**；`scaleY` 的 2% 更轻。art-model-director 说的"会拉伸模糊"是准确的，我说的"基准消失"在当前实现下不成立，**以他的说法为准**。
> 拆分 `AnimPivot` 的理由因此降级为：① 消除 8%/2% 的非整数采样模糊（把缩放限制在 δ<0.04 的更小量级）；② 避免以后有人真把尺寸写回 `localScale` 时重新踩坑。**是优化，不是阻塞项。**

把 bob 隔离到最内层 `AnimPivot` 后：

- `SpriteRenderer.localScale` 恒为 `Vector3.one`（**任何代码都不许写它**）
- 尺寸基准在 `SpriteRenderer` 上通过 PPU 决定，一次设定
- `AnimPivot.localScale = Vector3.one * (1 ± δ)`，δ 见 §4 各表（δ 恒 < 0.04，绝不会缩到 0 附近，与初始约 1 的基准天然兼容）

**为什么不用"localScale 上乘 k"：** `k = 9.4` 这种量级与 `1+bob` 这种量级写在同一节点上，任何一处漏改就打架；且 `localScale` 缩放会改变像素采样密度（非整数倍时模糊）。PPU 是导入期设定，运行时零开销、零冲突。

### 7.2 深度排序（必须一起改，否则重叠时排序会错）

旋转后贴图在屏幕 Y 方向的占位变化，固定 `sortingOrder` 会让"站在后面"的单位盖住前面的：

```csharp
sr.sortingOrder = Mathf.RoundToInt(-entity.transform.position.y * 100f);
```
同 y 值（±0.01 内）时按 `GetInstanceID() % 8` 微调，避免闪面。

---

## 8. 2D 与 3D 实现差异 + 给美术的返修诉求

| 项 | 本项目（2D 单张静态图） | 若将来上 3D / 序列帧 |
|---|---|---|
| 朝向 | 逐张标定 `spriteForwardDeg` + Z 旋转 / 镜像 | 骨骼直接转，无标定需求 |
| 履带 | 扬尘 + 车体抖动，**禁止画履带纹** | UV 滚动 or 履带骨骼 |
| 炮塔独立 | 做不到（单张图无法分离炮塔层） | 炮塔独立子节点，独立旋转 |
| 运动暗示 | 全部用 transform 通道（pos/rot/scale）+ VFX | 逐帧动画 |
| 后坐 | RecoilPivot 位移，必须与 ArtRoot 分离 | 直接做动画曲线 |
| 骨骼预算 | **0 骨骼**（纯 transform），移动端 0 开销 | PC ≤120 / 移动 ≤60 / Web ≤40 |

**给美术（art-model-director）的 3 条返修诉求，按性价比排序：**

1. **★最高：载具素材统一到"炮管朝图片正右（+X）、车体长轴水平"。** 现在 tank +24.6°、heavytank −15°、atcannon −36°、mgtower −41°，每张都要单独标定，且倾斜角会在旋转时放大成"车体歪斜"。统一后 `spriteForwardDeg` 全表填 0，程序少一个出错面。
2. **中：载具素材统一到纯俯视（相机仰角 ≥ 75°），不要带侧立面。** 当前近俯视够用，但仰角越低，旋转后光照方向不自洽越明显（高光会跟着图一起转，而战场光照是固定的）。**这个缺陷无法用代码修**——只能靠"轮廓描边 + 底座阵营色环"（`art-direction-v2.md` §5 ①②，成本 0）把玩家注意力从高光转到剪影上。这也是我建议优先做底座环的原因：它同时解决朝向可读性与光照不自洽。
3. **低：步兵补 1 张"朝正上/正下"的背面视角**，可彻底摆脱 `scaleX` 伪装的 8% 收缩。不补也能用。

---

## 9. 朝向相关状态机（最小集）

| 迁移 | 混合时长 | 理由 |
|---|---|---|
| 移动 → 待机（朝向） | 载具 8f；步兵立即 | 载具停下时车头惯性微过冲再回正（`RotateTowards` 已含）；步兵镜像无中间态 |
| 任意 → 受击 | **0–2f** | 受击必须立刻打断，这是手感底线；只闪白，不改朝向 |
| 攻击 → 待机 | 走完收招，不许直接跳 | 见 §5.3 的收招帧数 |
| 死亡 | **不混合，直接切** | 混合会让尸体抽搐；朝向冻结在死亡瞬间的角度 |

**打断优先级（高打断低）：受击 > 死亡 > 攻击 > 移动 > 待机。** 同优先级不许互打断——会互相卡死。

---

## 10. 实现顺序（给主程序的排期建议）

| 步 | 内容 | 依赖 | 预期收益 |
|---|---|---|---|
| **0** | **修相机 zoom clamp（§14）** | 无 | 不做则所有像素参数失效，视野差 15 倍 |
| **0.5** | **24 张图改 `textureType: Sprite` + 填 `def.sprite` + `sr.color = white`（§13）** | 无 | **不做则美术素材根本没进游戏**，后面全部空谈 |
| 1 | 四层预制体结构（Entity / RecoilPivot / ArtRoot / AnimPivot） | 0.5 | 解锁后面所有项，修掉后坐方向 bug、scale 被覆写 |
| 2 | 按 §7.1.1 逐资产填 PPU（**权威表**，非分类值） | 1 | 单位尺寸到位，朝向问题才开始可见 |
| 3 | `spriteForwardDeg` 字段 + §2.2 公式（**删掉 +90**） | 1,2 | 修掉"横着走" |
| 4 | 步兵镜像死区 + §4.1 修正后幅度 | 1 | 修掉正上正下抖、修掉抽搐式弹跳 |
| 5 | §4.2 / 4.3 载具运动参数 | 1,2 | 修掉"纸片滑动" |
| 6 | §5.1 后坐时长 + §5.3 像素化后坐值 | 1 | 打击感（现 0.08px 等于没有） |
| 7 | §7.2 深度排序 | 2 | 修掉重叠错层 |
| 8 | §12.4.1 爆炸尺寸重定义（25→1.2 等 4 处） | 2 | **修掉"每次死亡全屏爆炸"的现有 bug** |
| 9 | §12 爆炸序列帧接入（需美术抠图完成） | 8 | 爆炸从占位变 16 帧真动画 |

**第 0 / 0.5 步是 v1.2 新增的两条硬前置。** 之前的排期把它们埋在"第 2 步改 PPU"里，实际是两件完全不同的事：改 PPU 解决"尺寸"，改 `textureType` 解决"图能不能被加载"。**后者不做，前者填什么值都不生效。**

第 0、0.5、1、2 步不做，后面全部无效——**这是本方案的硬顺序依赖。**

---

## 11. 明确不做（划界）

- 不做倾斜补偿组件（§3 已给量化理由）。
- 不做炮塔独立旋转层（单张图无分离层）。
- 不做八方向素材（当前 1 张/兵种，成本 ×8）。
- 不决定攻击判定框大小（数值与程序的事）；本方案只给动作的视觉范围。
- 不保证"好玩"：本方案只能保证**参数自洽**（旋转角、频率、时长互相不矛盾）。手感必须真人在实机上判断。

---

## 12. 爆炸序列帧（v1.1 新增，由 art-model-director 的实测触发）

### 12.1 4×4 序列帧成立——我独立复核过，结论一致

我用暗部像素（`lum < 20`）做前景掩码，在 `Assets/_Project/Art/Effects/explosion.png`（256×256）上复测：

| 检验 | 我的结果 | 判读 |
|---|---|---|
| X 自相关峰 | **lag = 64**（corr 260，次峰 63/65/66 均 ≤247） | 周期 64px ✓ |
| Y 自相关峰 | **lag = 64/67**（corr 180） | 周期 64px ✓ |
| 按 64×64 切 16 格后暗部质心 | 全部落在 (28.7–34.0, 25.3–36.0)，格心 (31.5, 31.5) | 每格自成一帧 ✓ |
| 每格暗部像素数 | 188–456（与帧序单调相关，非随机） | 火焰由小到大 ✓ |

**结论：4×4 = 16 帧，成立。** 我采纳。但有两个必须先解决的技术问题。

### 12.2 问题一：四张特效**全都没有 alpha 通道**

我查了 `Assets/_Project/Art/Effects/` 与 `assets/effects/` 两处的四张图：

| 文件 | alpha 范围 | 全不透明占比 |
|---|---|---|
| explosion.png | **255–255** | 100% |
| muzzle_flash.png | **255–255** | 100% |
| nuke_cloud.png | **255–255** | 100% |
| smoke_column.png | **255–255** | 100% |

**四张图的 alpha 全是常数 255，没有一处透明。** 背景是**纯白/近白**（explosion 四角 ≈ (250,250,250)，nuke_cloud 四角恰好 (255,255,255)），alpha 为 255 的不透明白底。

这意味着：**现在直接把 explosion.png 挂到场景里，玩家会看到一个 256×256 的白色方块盖在战场上。** 这不是"AlphaBlend 还是 Additive"的问题——两者都以 alpha 为前提，而这里 alpha 恒为 1。**必须先抠图。**

量化抠图难度（我按"近白且低饱和"判背景）：

| 文件 | 近白低饱和占比 | 高饱和发光占比 | 抠图判断 |
|---|---|---|---|
| explosion | 41.8% | 39.7%（饱和区亮度均值 83） | **可抠**：火焰高饱和，与白底可分 |
| muzzle_flash | 30.2% | 47.8%（饱和区亮度均值 137） | **可抠**，最容易 |
| nuke_cloud | 46.1% | 50.1%（饱和区亮度均值 84） | **可抠** |
| smoke_column | 17.0% | **0.0%** | **不可抠**：全图零饱和度，灰白烟 vs 灰白底，只剩亮度差 |

**所以我同意 art-model-director 对 `smoke_column` 用亮度键控的判断，但要补一句：**
- 前三张用"近白 + 低饱和"双条件键控 + 亮度阈值做 alpha，可行（有 40–50% 高饱和像素作锚点）。
- `smoke_column` 零饱和度，**亮度键控是唯一手段**，且会损失烟体内部层次（浅灰烟会被一起键掉）。他的做法（alpha 天然渐变、淡出曲线按 `当前alpha × fade` 算）在这张图上是必须的，我确认这个约束成立。
- **但"alpha 天然渐变"只对 smoke_column 成立**，另外三张 alpha 是恒 255，不存在渐变基础。

### 12.3 问题二：`DeactivateAfter(go, 0.6f)` 会截断——但他算的时长我要改

16 帧 @ 24fps = 16/24 = **0.6667s**。当前 `VFXManager.cs:97` 是 `DeactivateAfter(go, 0.6f)` → 截掉最后 1 帧（0.6/0.0417 = 14.4 帧）。

**改 0.70s 是对的**（留 0.033s 余量 = 0.8 帧），我确认这个数。

但要说清**为什么不是按最后帧的显示时间算**：序列帧在包尾立刻消失会把"余烟散尽"掐掉，观感是从"爆炸"直接跳到"没有"。0.70s 让最后 1 帧（纯黑烟）多停留 0.033s，视觉上是自然收尾。

### 12.4 粒子系统的空跑（与他的判断一致，补一处他漏的）

`VFXManager.cs:93-94`：
```csharp
var ps = go.GetComponent<ParticleSystem>();
if (ps != null) { ps.Clear(); ps.Play(); }
```
占位 `ExplosionPrefab` 挂的是 `SpriteRenderer`（`PhaseBBootstrap.cs:122` `MakeFxPrefab` 只加 SpriteRenderer），`ps == null` → **整段是空跑**。确认。

### 12.4.1 爆炸尺寸现在是屏幕的 2.7 倍（v1.3 新增，art-model-director 发现，我已核实）

他只说"scale 语义要变"，**实际是一个已经存在的严重 bug**。我核了三个调用点：

| 调用点 | 传入 scale |
|---|---|
| `UnitEntity.cs:214` 死亡 | **25f（载具）/ 15f（步兵）** |
| `SuperWeaponSystem.cs:74` 核弹 | `blastRadius / 15f` ≈ 9.33 |
| `SuperWeaponSystem.cs:112` 子爆 | 3f |

占位 `placehold_circle.png` 是 **64px @ PPU 1.0 = 64 世界单位**。乘 25 → **1600 世界单位直径**。而 `orthographicSize = 300` → 屏幕全高只有 **600 世界单位**。

**⇒ 现有占位爆炸的直径是屏幕高度的 2.7 倍。** 也就是说现在每次载具死亡，整个屏幕都在炸。这不是"粒子尺寸语义"的隐患，是**已经发生的视觉 bug**，只是占位期没人当真。

**重定义：`scale` = 目标直径 / 24（24 世界单位为基准直径）**

| 调用点 | 现值 | 改为 | 依据 |
|---|---|---|---|
| 载具死亡 `UnitEntity:214` | 25f | **1.2** | 28.8 单位 ≈ tank 车体宽 |
| 步兵死亡 `UnitEntity:214` | 15f | **0.7** | 16.8 单位 ≈ 步兵高 15 |
| 核弹 `SuperWeaponSystem:74` | 9.33 | **11.7** | 280 单位 = blastRadius 140 的直径 |
| 子爆 `SuperWeaponSystem:112` | 3f | **1.5** | 36 单位，明显小于核弹 |

核弹那条要验算：`blastRadius / 15f` 当 blastRadius=140 时是 9.33，×64 = 597 单位 ≈ 屏幕高——**核弹铺满全屏是合理的**，所以这条原本"看起来对"是巧合。但改成直径制后应为 `blastRadius * 2 / 24 = 11.67`，语义才自洽（直径而非半径）。

**这条已并进施工清单（§10 第 12 步），优先级高于爆炸序列帧**——先让爆炸尺寸正常，再谈播几帧。

**他漏了一处**：`VFXManager.cs:90` 有 `go.transform.localScale = Vector3.one * scale;`，而 `SpawnExplosion` 的调用方传的是 `25f`（车）和 `15f`（步兵）——见 `UnitEntity.cs:214`。也就是说爆炸会被放大 15–25 倍。若改用粒子系统，`scale` 的语义要从"贴图缩放"改成"粒子尺寸"，**不能保留 localScale = 15**，否则粒子也会被放大到覆盖全屏。这是接入时必踩的坑，先记在这。

### 12.5 推荐方案：Texture Sheet Animation，脚本改动最小

| 方案 | 脚本改动 | 移动端开销 | 我的判断 |
|---|---|---|---|
| **ParticleSystem + Texture Sheet Animation（推荐）** | 只需 `ps.Clear(); ps.Play();` 保留，改 prefab | 1 个 draw call / 实例，粒子系统有固定 CPU 开销 | ✓ 采纳他的建议 |
| SpriteRenderer + Animator | 要加 Animator、控制器、状态机，且 `ps` 分支仍空跑 | Animator 每实例有开销 | 不推荐，为一个特效挂动画状态机太重 |
| 脚本手切 Sprite 数组 | 要写新脚本，`ps` 分支仍要删 | 最低 | 次选（若粒子系统在低端机上测出问题再用） |

**纹理图集设置：**
- `Texture Sheet Animation`：Tiles **4 × 4**，Animation **Single Row**，Frame over Time 用 **Constant**（不是 Curve）——本图是等间距帧，Constant 每帧停留相同时间，16 帧 @ 0.6667s。
- 这里的"Single Row / 4×4"要确认引擎按行还是按列扫；若扫错方向，帧序会变成横向跳，观感是"爆炸在左右抖"。**验收时必看。**

### 12.6 我对他四条的整体回应

| 他的说法 | 我的核实 | 结论 |
|---|---|---|
| ① explosion 是 4×4=16 帧 | **独立复测成立**（lag=64，质心贴格心，像素数单调） | **采纳** |
| ① `DeactivateAfter` 改 0.70s | 正确 | **采纳** |
| ① 推荐 ParticleSystem + Texture Sheet | 正确，且我补了"scale 语义要改"的坑 | **采纳** |
| ② localScale 不能用于尺寸基准 | **成立，且比他说得更严重**：不只是"拉伸模糊"，是每帧被重置成 ~1，尺寸基准完全消失 | **采纳，已改 §7.1 为 PPU 方案 + 四层结构** |
| ② PPU 现状"3.20–25.60" | **与实测不符：我查了全部 13 张单位图，PPU 全部 = 100**（`grep spritePixelsToUnits` 无例外） | **不采纳这个描述**；3.20–25.60 是目标值，我在 §7.1 按目标值给了表 |
| ③ 只能 transform 级动画 | 成立 | **采纳，我的整篇方案本来就是 transform 级** |
| ④ 四张必须 AlphaBlend 不能 Additive | **方向对，但前提不成立**：四张图 alpha 恒 255，现在连 AlphaBlend 也无从谈起，**必须先抠图** | **升级为阻塞项**，见 §12.2 |
| ④ smoke_column 用亮度键控 | 成立（我实测零饱和度，只能靠亮度） | **采纳** |

**一句话：他的四条我采纳三条半，但第 ④ 条的真正阻塞点他判断轻了——不是"选哪个混合模式"，是"根本没有 alpha 通道，四张图现在都是白底方块"。**

---

## 13. 美术素材未接入：修复清单（v1.2 新增，P0 最高优先级）

### 13.1 实测事实

| 检查项 | 占位图（脚本生成） | 24 张美术图 |
|---|---|---|
| `textureType` | 8（Sprite） | **0（Default）** |
| `spriteMode` | 1（Single） | **0（None）** |
| `alphaIsTransparency` | 1 | **0** |
| `spritePixelsToUnits` | 1 | 100（Unity 默认值） |
| `UD_*.asset` 的 `sprite` 字段 | — | **15/15 全为 `{fileID: 0}`** |
| 代码里 `.sprite =` 的来源 | 占位图 | **无任何一处从 `def.sprite` 赋值** |

**结论：素材从未进入游戏，游戏里跑的是圆/方占位图。** 所有关于"朝向不对""尺寸不对"的讨论，前提都是这张图已经被当 Sprite 加载——而它没有。

### 13.2 为什么 `textureType: 0` 是致命的

Unity 的 `AssetDatabase.LoadAssetAtPath<Sprite>(path)` 只有在 `textureType == Sprite` 时才返回 Sprite，否则返回 **null**。`spriteMode: 0`（None）意味着连 sprite 子资源都不会生成。所以即便程序写了 `sr.sprite = def.sprite`，`def.sprite` 也是 null。

### 13.3 修复步骤（按顺序）

```csharp
// 1) 批量修导入参数（放 Editor 脚本，跑一次）
foreach (var path in unitsAndEffectsPaths) {
    var ti = (TextureImporter)AssetImporter.GetAtPath(path);
    ti.textureType          = TextureImporterType.Sprite;  // 8
    ti.spriteImportMode     = SpriteImportMode.Single;     // 1
    ti.alphaIsTransparency  = true;                        // 1
    ti.spritePixelsPerUnit  = GetPPUFor(path);             // 见下表
    ti.filterMode           = FilterMode.Bilinear;
    ti.maxTextureSize       = 256;                         // 素材就是 256，别让引擎放大
    ti.SaveAndReimport();
}
```

```csharp
// 2) 填 def.sprite 并在 UnitView 里接一次赋值（只接一次，不是每帧）
void Start() { if (entity?.def?.sprite != null) sr.sprite = entity.def.sprite; }
```

```csharp
// 3) 淡出占位色：占位图有 color 染色，接真图后必须设回白色
sr.color = Color.white;   // 否则真图会被乘上 (0.25,0.65,0.84) 的蓝
```
> 第 3 步容易漏：`PhaseBBootstrap.cs:98` 给占位图设了 `us.color = new Color(0.25f, 0.65f, 0.84f)`。这行留在预制体上，真图接进来后整张图会被染成蓝色。**这是接入后第一个会看到的"新 bug"，其实不是新 bug。**

### 13.3.1 根因不是操作失误，是缺导入预设（Preset）

`textureType: 0` 是 `.png` 拖进工程时的 **Unity 默认导入结果**——Unity 不会自动把任意 PNG 当 Sprite，除非放在 `Resources/` 下或经过 Preset。所以这不是谁做错了，是**流程缺一个环节**。

**一次性脚本只修当前 24 张，下次美术加图还会错。** 建议同时做：

```
1. Project 窗口选中一张图 -> Inspector 设成 Sprite/Single/alphaIsTransparency/正确 PPU
2. Inspector 右上 Save Preset -> 命名 SpriteUnit / SpriteEffect
3. Project Settings -> Preset Manager -> 给 TextureImporter 指定
   Art/Units/ 与 Art/Effects/ 的默认 Preset（按文件夹路径匹配）
```

这样以后拖图进这两个目录**自动就是 Sprite**，不需要任何人记得跑脚本。**根治 vs 打补丁的区别。**

### 13.3.2 素材被画布裁切：5 张图的主体伸出了画面（v1.5 新增，本轮发现）

我在复核 `spriteForwardDeg` 时顺手扫了所有图的四条边界，发现**有 5 张的实体像素贴在画布边缘**（alpha 均值 198、范围 171–202，是实体不是软阴影；软阴影不会到 198 这么实）：

| 资产 | 贴边像素数（左/右/上/下） | 严重度 |
|---|---|---|
| **warfactory** | **左 105 / 右 124 / 上 37** | **★ 最严重，两侧+顶部都切** |
| harvester | 左 36 / 右 8 | 高 |
| **heavytank** | 左 30 / 右 16 | **高（直接影响炮管判定）** |
| barracks | 左 25 / 右 13 | 中 |
| tank | 左 12 / 右 3 | 低（炮管端仅贴边 2–4px） |
| refinery | 左 9 / 右 14 | 低 |
| hq / powerplant / radar / mgtower / atcannon / rifleman / bazooka | 0 | 完整 |

**为什么这件事对动画规格要紧（不是纯美术问题）：**

1. **`spriteForwardDeg` 测不准。** heavytank 的炮管两端都被切，导致"哪端是炮口"这个信息在图片里就缺了——这是 §2.3.2 三条测量路线分歧的真正根因。**不是谁算错了，是数据不完整。**
2. **`sprite.bounds.size.x/y` 会偏小。** FullRect 下 bounds 恒为 256×256 ÷ PPU，这没问题；但**视觉中心会偏移**——单位看起来"往后坐"，因为剪影在画布内不对称。
3. **旋转时会露出"平切面"。** 炮管末端被切平，转到朝向玩家时会看到一条直线断面，而不是圆炮口。这是**每时每刻都在发生**的视觉破绽，不是某一帧的问题。

**修法优先级（对 art）：**
- **warfactory 必须重出**（左右各切掉 105/124 px，主体缺了快一半宽度）
- **heavytank / harvester 建议重出**（顺带解决 §2.3.2 的炮管悬案）
- tank / barracks / refinery 可以在导出时**留 8% 安全边距**就够，不必重画

> 这条按 art 的"不返修"口径可以不改图——**但 heavytank 的 `−20` 就必须由人眼定，不能指望测量。** 两件事绑在一起，见 §2.3.2。

### 13.4 PPU 取值表（v1.3：改为 team-lead 裁定的逐资产值）

**权威表在 §7.1.1**，此处给分类归并版便于程序分组处理：

| 类别 | 目标世界高度 | PPU |
|---|---|---|
| rifleman / bazooka / sniper | 15 | **17.07** |
| tank / harvester | 28 | **9.14** |
| heavytank | 32 | **8.00** |
| hq | 80 | **3.20** |
| warfactory / refinery | 56 | **4.57** |
| barracks | 48 | **5.33** |
| powerplant / radar | 44 | **5.82** |
| atcannon | 36 | **7.11** |
| mgtower | 32 | **8.00** |

**v1.2 分类值（步兵 21.3 / 载具 10.7 / 建筑 3.2–4.3）已作废。** 作废理由见 §7.1.1：建筑高度跨度 2.5 倍，分类一刀切会让小建筑显示得比大建筑还大。

**与 art-model-director 关于"现状"的分歧已澄清**：他说"逐资产 3.20–25.60"，我说"现状全是 100"——**两者都对，指的是不同东西**：3.20–25.60 是应改成的目标值，100 是当前实际值。他已在 `art-asset-integration-v1.md` v1.1 里加了纠正块。**真正的阻塞不是这个数值分歧，而是 `textureType: 0`（§13.2）。**

---

## 14. 相机缩放冲突：会让所有像素参数失效（v1.2 新增）

### 14.1 冲突

| 位置 | 值 |
|---|---|
| `PhaseBBootstrap.cs:249` | `orthographicSize = 300f` |
| `TouchInputSystem.cs:15` | `minZoom = 5f` |
| `TouchInputSystem.cs:16` | `maxZoom = 20f` |

`PhaseBBootstrap` 创建相机后**从未给 `minZoom / maxZoom` 赋值**，因此保持脚本里的默认 5 / 20。`TouchInputSystem` 在双指缩放时执行 `orthographicSize = Clamp(orth + delta, minZoom, maxZoom)` → **首次缩放瞬间 clamp 到 20**，视野高度从 600 世界单位变成 40，**缩小 15 倍**。

### 14.2 后果（这条直接摧毁本方案）

- 单位屏占从 tank 50.4px 变成 **3.4px**；
- §4 全节的 px 参数失去基准；
- §5.3 的后坐像素值（1.5–5px）在 3.4px 的单位上**比单位本身还大**，后坐会变成"整辆车飞出去"。

### 14.3 修复

```csharp
// TouchInputSystem 里改成按初始 orthographicSize 推导，避免两处硬编码打架
void Start() {
    if (mainCamera == null) mainCamera = Camera.main;
    minZoom = mainCamera.orthographicSize * 0.5f;   // 300 -> 150（拉近 2 倍）
    maxZoom = mainCamera.orthographicSize * 1.5f;   // 300 -> 450（拉远 1.5 倍）
}
```
即 `minZoom = 150 / maxZoom = 450`。**或**在 `PhaseBBootstrap` 创建相机时显式赋值。

### 14.4 为什么建议用"相对初始正交尺寸"而不是写死 150/450

如果以后调了 `orthographicSize` 基准（比如为了视野调整成 240），写死的 150/450 又要重算一遍 clamp 区间，而相对写法自动跟随。**两处硬编码同一件事，一定会有一处忘记改**——这正是这次 bug 的成因。

---

## 15. 给 art-model-director 的逐条回应（v1.2）

### ① 载具统一炮管朝 +X —— 接受"不返修"，但你的补偿公式是错的

**不返修我接受**，理由比你说的更强：`textureType: 0` 说明素材根本没接进来，现在谈返修是空谈。而且 16° 在 50px 上约 7px，确实能靠参数吃掉。

**但 `targetAngle = facing * Rad2Deg + 90f + def.spriteForwardDeg` 这个改法是错的，会抵消掉你自己的修正。**

问题在 `+ 90f` 被保留了。我做了旋转实验直接验证（把 `tank.png` 实际旋转，再测炮管方位角）：

| 旋转量 θ | 旋转后炮管指向 | 当 `facing = 0`（向右移动）时 | 判定 |
|---|---|---|---|
| **0°**（我的公式 `zDeg = facing − 23`） | 23.2° | 指向**右偏上** | ✓ 正确 |
| −23.2°（完全对齐） | 5.5° | 正右 | ✓ 正确 |
| **+90°**（你的公式 `+90 + 0`） | **114.0°** | 指向**左上** | **✗ 和现在的 bug 一模一样** |

**原因**：`+90` 本身等价于 `spriteForwardDeg = −90`（"假设素材朝上"）。你保留了 `+90` 又加 `+c`，等于声称素材前向是 `−90 + c`。而实测 tank 前向是 **+23**。
- 你的写法隐含前向 = `90 − 0 = 90°`（朝上）
- 实测前向 = `23.2°`（朝右上）
- **差 67°，比你原本要修的 16° 还大 4 倍。**

**正确写法（二选一）：**

```csharp
// 方案一（推荐，语义清晰）：删掉 +90，字段就是素材前向角
float zDeg = entity.facing * Mathf.Rad2Deg - def.spriteForwardDeg;

// 方案二（若坚持保留 +90 形式）：
float zDeg = entity.facing * Mathf.Rad2Deg - (90f - def.spriteForwardDegOff);
```
方案一更不容易错——**字段值就是"素材里炮管指向多少度"，谁都能直接量**。

代入验证：tank `facing=0` → `zDeg = 0 − 23 = −23°` → 原炮管 23.2° 转 −23° 后落到 **0.2°（正右）**。✓

### ② 你的填表符号：tank 对，heavytank 反了

| 资产 | 你按主轴算 | 实测炮管锚 | 差 | 结论 |
|---|---|---|---|---|
| tank | 0 | +23.2° | 23° | **你填 0，实际炮管在 +23**；用 0 会让炮管一直偏右上 23° |
| heavytank | **+16** | **−11.3°** | **27°（符号相反）** | **你填反了**，应填 **−11** |
| atcannon | +14 | −36° | 50° | 不旋转的建筑，此值不使用 |
| harvester | 待测 | **223.3°**（车头/推土铲朝左下，目视 ≈225° 互证） | — | **填 223** |

**tank 为什么你填 0 也不对**：你用的是 PCA 主轴 177°≡−3°，认为"接近水平就当基准 0"。但**主轴是 180° 周期的，它只说车体长轴方向，不说车头在哪一端**。tank 的炮管在 +23.2°，车体轴在 −3°，两者差 26°——**素材本身车体与炮管就不一致**。此时必须选一个作为"前向"，我选炮管（因为玩家判断朝向看的是炮口）。

**heavytank 符号反了的证据**：它的主轴 164°≡−16°，炮管 −11.3°，两者差 5°——这是最强的自洽证据，说明该素材前向确实在 **−13° 附近**，而不是 +16°。

### ③ 光照不自洽 / 底座色环 —— 同意不是你的活

我已按你的建议，把"轮廓描边 + 底座阵营色环"作为独立建议提给 team-lead 排进 `art-direction-v2` 修订，不再要求你处理。**但我在 §8 保留这条技术结论**（因为它是"为什么旋转方案必须配合视觉补偿"的论证链的一环），并注明归属。

### ④ 步兵不补背面图 —— 接受

你的理由（13 张里没有多视角套图，单补 1 张会让 rifleman / bazooka 不对称）成立，比我的"更好看"论证更硬。**保留 `scaleX` 收 8% 方案，且 §4.1 已按裁定后的 27px 屏占把幅度定为 0.54px。**

### ⑤ PPU —— 我们各对一半，但你的表述有风险

你说"素材尺寸缺口不存在，是默认 PPU 造成的错觉"。
- **对的一半**：按 3.20 PPU，tank 确实是 28 单位 / 50px，不是 2.56 单位 / 5px。我 v1 的 5px 论断**是在"图已是 Sprite"的错误前提下算的，已撤回**（见 §0）。
- **需要纠正的一半**：你写"逐资产 3.20–25.60"像是现状，但**现状全部是 100**。而且真正的问题不是 PPU 数值，是 `textureType: 0` ——**图根本不是 Sprite，PPU 填什么都不生效**。详见 §13。

### ⑥ zoom clamp bug —— 你这条最有价值，我采纳并已量化

`minZoom=5 / maxZoom=20` vs `orthographicSize=300` 的冲突**确实存在**（我核实了 `TouchInputSystem.cs:15-16` 与 `PhaseBBootstrap.cs:249`），首次缩放的 clamps 到 20 → **视野缩小 15 倍**。

**但修法我要改你的方案**：你说"`minZoom 150 / maxZoom 450`"，我建议改成**相对初始值推导**：
```csharp
minZoom = mainCamera.orthographicSize * 0.5f;
maxZoom = mainCamera.orthographicSize * 1.5f;
```
理由：写死 150/450 等于把 `orthographicSize` 的值抄了两遍，以后调基准视野时必有一处忘改——**这个 bug 的成因正是同一件事被写在两个地方**。已在 §14 给出。

**你说的"你方案里所有 px 论述会失效"完全正确，我已在 §4 开头加了前提声明，并把它列为 §14 独立阻塞项。**

---

## 16. 幅度参数是否需要重算（回应 ⑥ 的连带问题）

**你的提醒是对的：确实要重算，我已经重算了。** 具体改了四处：

| 项 | v1 原值 | 实际像素 | v1.3 新值 | 原因 |
|---|---|---|---|---|
| 步兵 bob | `0.035·h` = 0.42 单位 | 0.75 px（在 27px 人物上） | **`0.020·h` = 0.54 px** | 0.75px@4.7Hz 是抽搐不是走路 |
| 步兵 rotZ | ±2.0° | 在 27px 上过晃 | **±1.2°** | 同上 |
| 后坐 | `0.025·h`（步枪 0.54px） | **不可见** | **改按像素定：步枪 1.5px / 坦克 3px** | 人眼阈约 1px，参数应按像素定 |
| 现有代码 recoil | `0.3 × 0.15 = 0.045 单位` | **0.08 px** | — | 这就是"现在开火没后坐感"的真正原因 |

**没有改的**：`scaleY` 压缩（0.98–1.00）与待机呼吸（±0.008）都是**比例量**，与像素密度无关，原值有效。

---

## 17. 直接回答你的三个提问（v1.3）

### 17.1 "你的方案里是读 `def.spriteForwardDeg` 还是写死 0？"

**读字段，不写死。** 文档 §2.2 的代码就是：

```csharp
float zDeg = entity.facing * Mathf.Rad2Deg - def.spriteForwardDeg;
```

`def.spriteForwardDeg` 是 `UnitDefinition` 上的新字段，逐单位配置。§2.3 的表是**字段初值**，不是写入代码的常量。你的担心（"如果写死 0 那才是真问题"）成立，但我没有这么做。

**但你要在 `art-asset-integration-v1.md` §十一 加的那条"程序侧必须读字段"我建议不要加**，理由：那是实现约束，写在我的规格里更合适，写进你的资产接入规格会造成同一约束在两份文档里各写一遍——**这次的 PPU 分歧就是这么来的**。责任归属清楚一点：**字段定义与取值在我这，读不读在程序，你的规格只描述资产事实。**

### 17.2 返修口径：接受"不返修"，但你的表和公式都有错

**口径统一：不返修，用 `spriteForwardDeg` 参数补偿。** 这个我接受，且我在 v1.2 就已经把返修诉求从"必须"降级成"建议"了（§8 措辞是"按性价比排序的诉求"）。你的 7px 计算我也认同——tank 宽 28 单位、16° 顶点位移 3.9 单位 ≈ 7px，是"有点斜"不是"歪得离谱"。

**但两处必须改：**

**(a) 表里的值错了，tank 和 heavytank 都要改**

| 资产 | 你填 | 实测炮管锚 | 结论 |
|---|---|---|---|
| tank | 0 | **+23.2°** | **应填 23**，填 0 会让炮管恒偏右上 23° |
| heavytank | **+16** | **−11.3°** | **符号反了，应填 −11** |
| atcannon | +14 | −36° | 建筑不旋转，此值不使用 |
| harvester | 待测 | **223.3°** | **填 223**（已补测） |

**(b) 公式保留 `+90f` 是错的**

你写 `facing * Rad2Deg + 90f + def.spriteForwardDeg`。我做了旋转实验验证：

| 旋转 θ | 旋转后炮管指向 | facing=0（向右）时 | 判定 |
|---|---|---|---|
| 0°（`facing − 23`） | 23.2° | 右偏上 | ✓ |
| −23.2°（完全对齐） | 5.5° | 正右 | ✓ |
| **+90°（`+90 + 0`）** | **114.0°** | **左上** | **✗ 与现有 bug 相同** |

`+90` 本身等价于 `spriteForwardDeg = −90`。保留 `+90` 又加 `+c`，等于声称前向是 `−90+c`；而实测 tank 前向 **+23**。**你的写法隐含 90°，实测 23.2°，差 67°——比你原本要修的 16° 大 4 倍。**

正确写法：`float zDeg = entity.facing * Mathf.Rad2Deg - def.spriteForwardDeg;`（无 `+90`）

**(c) 填表方法的根因**：你用 PCA 主轴算前向。**主轴是 180° 周期的，只说长轴方向，不说车头在哪一端**，一半概率判反且不报错。heavytank 之所以能暴露这个错，是因为它主轴 164°≡−16° 与炮管 −11.3° **只差 5°**——这种自洽只在两法指同一端时出现，反证真实前向是 −13° 而非 +16°。详见 §2.3.1。

### 17.3 关于 `UnitView` 挂载层级

> **v1.4 更正**：我在 v1.3 写过"`UnitView` 目前没有任何挂载点"，**这条已过期**。当前 `PhaseBBootstrap.cs:303` 已经在 `ArtRoot` 上 `AddComponent<UnitView>()` 并显式注入 `view.recoilPivot`。详见 §7.1 的 v1.4 修正。

**结论改为：挂载点不重要，通道唯一才重要。** 现有挂法（挂 `ArtRoot`、持 `recoilPivot` 引用）是可行的，因为它写的是 `recoilPivot.localPosition` 和 `artRoot.localRotation/localPosition/localScale` 三个显式引用，没有裸写 `transform.xxx`，通道不串。

**唯一硬规则**：`UnitView` 里除 `Awake` 外不许出现裸的 `transform.localPosition / localRotation / localScale`。挂了四层结构后又写 `transform.xxx`，等于把 `ArtRoot` 和 `AnimPivot` 的通道合并，回到"后坐与呼吸互相覆盖"。

### 17.4 爆炸尺寸：你的发现比你说的更严重，已核实

你说"scale 语义要变"，**实际是已经发生的 bug**：占位圆 64px @ PPU 1 = 64 世界单位，×25 = **1600 单位直径**，而屏幕全高 600 单位。**现有占位爆炸是屏幕高度的 2.7 倍——每次载具死亡整个屏幕在炸。**

你给的重定义和逐点改值我采纳（载具 25→1.2、步兵 15→0.7、核弹 9.33→11.7、子爆 3→1.5），但核弹那条要说明：`blastRadius/15` 原本"看起来对"是巧合，改成直径制应为 `blastRadius * 2 / 24`，语义才自洽（直径而非半径）。已写进 §12.4.1，列为施工第 8 步，**优先级高于爆炸序列帧**。

---

## 18. 关于 PPU 的最终立场（v1.3）

你已认错并加了纠正块，我确认收到，**口径已一致**：

- **现状 = 100**（13 张单位 + 4 张特效，无例外，你我各自的 `grep` 与 `md5` 都确认了）
- **目标 = 逐资产值**（hq 3.20 … rifleman 17.07）
- **真正的阻塞 = `textureType: 0`**，不是 PPU 数值

**但有一件事我们都没说透，我补上**：你说"工程里的 24 张就是未抠图的原图直接拖进去的"。那么 `textureType: 0` 很可能是**拖进 Unity 时的默认导入结果**——`.png` 拖进工程，Unity 不会自动设成 Sprite，除非放在 `Resources/` 或经过预设。**所以这不是谁操作失误，是流程缺失：没有导入预设（Preset）。**

**建议**：给 `Art/Units/` 和 `Art/Effects/` 各配一个 `TextureImporter` Preset（Sprite / Single / alphaIsTransparency / 对应 PPU），以后拖图进来自动生效。**这比写一次性脚本更根治**——一次性脚本只修当前 24 张，下次美术加图还会错。这条我写在 §13.3 末尾。

---

## 19. 主程序当前实现与本规格的差异清单（v1.4 新增）

> 我核了 `UnitView.cs`（356 行，四层结构已落地）、`UnitEntity.cs`、`PhaseBBootstrap.cs`、`TouchInputSystem.cs` 的当前状态。**先说结论：四层结构、通道归属、`0.020f`/`1.2°`、像素化后坐、`spriteForwardDeg` 读字段——这五条都已经按规格实现了，做得对。** 下面是仍对不上的地方，按严重程度排。

### 19.1 P0：后坐仍然看不见，因为点火值是 0.15 而不是 1.0

这是本轮最要紧的一条，**它让 `PixelsToWorld` 那一整套像素化工作基本归零**。

`ApplyRecoil()` 里：

```csharp
float r = entity.recoil * entity.recoil;                                  // r = recoil²
float offsetWorld = PixelsToWorld(RecoilPixelsOf(unitId), Camera.main) * r;
```

而 `UnitEntity.cs:195` 开火时写的是 **`recoil = 0.15f;`**（旧代码遗留）。

代入 `recoil = 0.15` → `r = 0.0225` → 幅度只有标称值的 **2.25%**：

| 单位 | `RecoilPixelsOf` 标称 | 实际生效像素 | 判定 |
|---|---|---|---|
| rifleman | 1.5 px | **0.034 px** | 完全不可见 |
| bazooka | 2.5 px | 0.056 px | 完全不可见 |
| tank | 3.0 px | **0.068 px** | 完全不可见 |
| heavytank | 4.0 px | 0.090 px | 完全不可见 |
| artillery | 5.0 px | 0.113 px | 完全不可见 |

**更糟的是持续时间也一起塌了。** `recoilDecay = 1/0.18`（每秒衰减 5.56），从 0.15 衰减到 0 只需：

```
0.15 / (1/0.18) = 0.027 s = 1.6 帧（60fps）
```

**设计要的是 0.18 s = 11 帧，实际 1.6 帧。** 也就是说后坐既不可见（0.07px）又只活 1.6 帧，两个缺陷叠在一起——这正是"开火完全没有打击感"的完整解释。

**修法（一行）**：`UnitEntity.cs:195` `recoil = 0.15f;` → **`recoil = 1f;`**

> 这条我在 §5.1 写过"`recoil` 归一化到 0..1，开火时 `recoil = 1f`"，§17 也强调过"不是参数偏小，是维度错了"。**但文档里两个地方（§5.1 的新写法、§5.3 的旧例）都出现过 `0.15`，主程序照着旧例写了。这是我的文档瑕疵，不是他的实现问题。** 本轮把 §5.3 的老例子也标注清楚。

### 19.2 P1：`fTrk` 把画布高当成车体长边，履带频率偏高 44%

```csharp
float L     = Mathf.Max(h, 1f) * 1.4f;   // 代码注释写"车体长边近似"
float pitch = 0.25f * L;
float fTrk  = Mathf.Clamp(entity.def.moveSpeed / pitch, 1.5f, 6f);
```

`h` 是**贴图世界高度**（竖直方向），而"车体长边"是**水平方向**的宽度。tank 素材 254×145（长宽比 1.75），长边在 X 方向：

| 单位 | `h`（竖直高） | 代码 `L = h×1.4` | 真实长边（画布宽） | `fTrk` 代码值 | `fTrk` 正确值 | 偏差 |
|---|---|---|---|---|---|---|
| tank（speed 60） | 28 | 39.2 | **28** | 6.12 → clamp **6** | 8.57 → clamp **6** | 都撞上限，**此例无差** |
| heavytank（speed 45） | 32 | 44.8 | **32** | **4.02** | **5.62** | **偏低 40%** |

**结论有偏差但不算严重**：tank 因为速度高，两个算法都撞到 6Hz 上限；heavytank 本该 5.62Hz，现在 4.02Hz，履带颠簸比设计慢 40%。观感上是"重坦的接地感比中坦还迟钝"，而实物应该反过来（重坦节距更大但速度更慢，实际频率接近）。

**修法**：`L` 改用宽度，或直接给 `UnitDefinition` 加 `spriteLongAxis` 字段按 §2.3 测量值填（tank 28 / heavytank 32）。**优先级低于 19.1，可以下轮一起改。**

### 19.3 P2：`artcannon` 后坐像素从 3.0 被改成 2.5，无理由

`RecoilPixelsOf` 里 `case "atcannon": return 2.5f;`，文档 §5.3 是 **3.0 px**。

单看这一处无伤大雅（0.5px），但它暴露一个模式：**规格表里的数值被"顺手调了一下"没人说为什么。** 若是主程序实机看过觉得 3px 太大，那按 §七边界"手感必须真人在实机上判断"，我接受他的 2.5，**但请在代码注释里写一句"实机试过 3px 偏大"**，否则下次有人对文档又会以为是实现错了。同理要确认的还有 `bazooka`（文档 2.0 → 实现 2.5，调大了）。

### 19.4 已核对无误的项（不用动）

| 项 | 状态 |
|---|---|
| 四层结构 `UnitEntity / RecoilPivot / ArtRoot / AnimPivot / Sprite` | ✓ `PhaseBBootstrap:308-337` 已建，5 节点 |
| `UnitView` 挂 `ArtRoot`，显式注入 `recoilPivot` + `animPivot` | ✓ 通道不串，符合 §7.1 v1.4 修正后的规则 |
| `zDeg = facing*Rad2Deg - spriteForwardDeg`（**无 +90**） | ✓ |
| 步兵 `0.020·h` / `1.2°` | ✓ 已从 0.035/2.0 改到位 |
| 步兵 `rotZ` 写 `AnimPivot` 而非 `ArtRoot` | ✓ 且注释说明了理由，比规格更严谨 |
| 后坐世界→local 变换 + 像素反算 | ✓ 公式正确（**只是被 19.1 的点火值废掉了**） |
| 步兵镜像 ±0.15 死区 | ✓ |
| `spriteForwardDeg` 从字段读、未写死 | ✓ |
| `ConfigureSpriteImporters` 设 `textureType = Sprite` + PPU + pivot | ✓ 但**尚未运行过**（meta 里仍是 `textureType: 0`） |
| `TextureImporterSettings.spriteMeshType = FullRect` | ✓ 符合 §4.0 方案 B（不引 Tight 顶点开销） |

**只有一条要我提醒**：`ConfigureSpriteImporters` 是一次性 Editor 脚本，**当前 `.meta` 里依然是 `textureType: 0` / `spritePixelsToUnits: 100`，说明它还没被执行**。在它跑过之前，§7.1.1 的 PPU 表和本文件所有 px 数值都还停留在纸面。这条仍是 §10 的第 0.5 步，**不是已完成项**。

---

## 20. 三条验收点（v1.4 收口，可直接照着点）

前面 §6 是体验级验收。这里给三条**能在 Editor 里 5 分钟量出来**的，专门验本轮的改动：

| # | 操作 | 判定 | 验的是 |
|---|---|---|---|
| **A** | 让一辆 tank 开火，逐帧（`Ctrl+P` 暂停逐步）看 `RecoilPivot.localPosition` | 峰值 ≥ 1.5 世界单位（= 3px @1.8px/单位），且持续 **≈11 帧** | 19.1 的后坐链 |
| **B** | 场景里同时放 tank 与 rifleman，都接真图（`Keyed/tank.png`） | tank 屏高 ≈ **50px**（±10%），rifleman ≈ **27px**（±10%），两者不同高 | PPU 是否真的生效（19.4 那条） |
| **C** | 命令 tank 向右走、向上走各 2 秒 | 炮管始终指向前进方向，车体长轴与运动方向夹角 < 15°；**车不出现"横向平移"** | 朝向公式 |

> 验收 A 的前置是 19.1 修完。**没修 A 一定不过，且不修 A 的话后面所有"打击感"讨论都是空谈**——0.068px 的后坐和 0 没有区别。

### 20.1 加一条前置验收 D（v1.5）：heavytank 的 `spriteForwardDeg` 必须人眼定

**这条不能用代码验，只能在编辑器里看图。** 且它是 `artillery` 之外唯一一个"填错了不会报错、只会看到车倒着开"的值。

| 步 | 操作 | 判定 |
|---|---|---|
| D1 | 打开 `Art/Keyed/heavytank.png` | — |
| D2 | 找**锥形收窄、带炮口环或制退器**的那一端 | 记下它在图片的哪个象限 |
| D3 | 若在**右下（世界 ≈ −20°）** | 保持 `-20f`，✓ 通过 |
| D4 | 若在**左上（世界 ≈ +160°）** | 改为 `160f`（= −20 + 180），错的只是符号 |
| D5 | 都不要看"哪端更长" | 两端都被画布裁切（§13.3.2），长度不可靠 |

**为什么必须人眼**：我三种算法（末端收窄扫描 / 外露结构 / 主轴一致性）在 heavytank 上给出 161° 与 −13° 两个互斥答案，且**两个候选的末端都贴着画布边缘**——图里缺了判断依据。再加算法也不会收敛，只会多一个答案。

**副作用提醒**：D4 若改成 160，`spriteForwardDeg` 会变成全表唯一的大角度值（cos 为负）。代入 `zDeg = facing − 160`，facing=0 时 `zDeg = −160°`，视觉上是"车身转了 200°"——**这是对的**（图本来就把车头画在了左边），但如果主程序看到 −160 觉得不对又擅自改小，就退回原 bug。**这条改完请在代码注释里注明依据是目视。**

