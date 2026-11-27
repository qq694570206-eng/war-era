# 美术资产接入规格 v1.3

取代：无（首版）

- 专业域：美术 / 模型与 2D 资产 / UI 视觉
- 作者：美术总监 甄塑之
- 适用：`assets/` 下 24 张 PNG（13 单位建筑 + 4 特效 + 4 地形 + 3 UI）接入团结引擎（Unity 2022.3 内核）`_Project` 工程
- 所有数字为 `python3 + PIL` 实测或由实测值推导，未取估值
- **v1.3 变更**（1 处新增章节 + 1 处判据推翻，由 animation-director v1.5 §13.3.2 裁切清单触发）：
  1. `§十四` 新增**素材边界裁切实测**。推翻其"边界 alpha 171–202 = 实体被切"的判据——实测 `a==202` 是高斯羽化零填充的算术产物（预测 202 / 实测 202，误差 0），与实体无关。改用 `T1 触边数 / T2 切面段 / T3 屏显像素` 三判据，全在原图上跑（免疫羽化）。
  2. `§14.3` 定稿：**只有 warfactory 真需要返修**（右边界 122 源 px 直线断面 = 48 屏 px），harvester 4.7 屏 px 可接受，其余 11 张 `T2 = 0` 完整。其清单里 5 张"被切"有 4 张实为触边。
  3. `§14.5` **heavytank 炮管测不出的真因不是裁切，是图上没有炮管特征**：最细外向突起 63 px（tank 为 17 px），放宽到 64 px 仍无任何方向；「台阶后 ≥18 px 长 + ≤25 px 宽的平行突起」定义下不存在。两端确实各自出画布，但缺口仅 0.4 / 0.9 屏 px，不构成识别障碍。
  4. `§14.6` 补 heavytank 的**替代锚点**：车尾高射机枪（121°/世界 −121°）的反方向 **+59°** 是车头，与人眼"找炮管"互为交叉验证；并与其 §20.1 验收 D 一致（`-20f` 降级为目视暂定）。
- **v1.2 变更**（1 处根本性前提 + 4 处自纠错，由 animation-director 二次复核触发）：
  1. `§0⓪` 新增**最重要前提**：24 张图 `textureType: 0` / `spriteMode: 0`，**一张都不是 Sprite**；15 个 `UD_*.asset` 的 `sprite` 全为 `{fileID: 0}`；无代码读 `def.sprite`。**素材从未接入，跑的是占位图。**
  2. `§十一` **重写**：作废 v1.1 的 `spriteForwardDeg` 表（tank 0 / heavytank +16 / atcannon +14）与公式（`+90f + 值`）。改用窄长突起检测重测：tank **+26**、heavytank **−20**（符号反了）、公式改为 `facing*Rad2Deg - def.spriteForwardDeg`（**删 `+90f`**）。
  3. `§11.5` 新增：`Harvester` / `Defense` 类别**没有表现分支**（`UnitView.cs:33-45` 只分派 Building/Vehicle/Infantry），所以 `spriteForwardDeg` 实际只需填 2 个值。
  4. `§十二` 第 0 步改为**相对推导**（`orthographicSize × 0.5/1.5`），不写死 150/450；第 3 步加 `textureType → 8` 硬卡点。
  5. `§十二` 新增第 6b 步：清掉占位染色（`PhaseBBootstrap.cs:98/106/115/132` 给 Prefab 设了蓝/灰/黄 color），不清则真图接入后整张染蓝。
- **v1.1 变更**（3 处更正 + 2 处新增，均由 animation-director 复核触发）：
  1. `§1.2` 更正：逐资产 PPU 3.20–25.60 是**目标值**，不是现状。现状实测 `*.png.meta` **全部 `spritePixelsToUnits: 100`**，13 单位 + 4 特效无一例外。
  2. `§3.4` 新增：特效（explosion / muzzle_flash / nuke_cloud）必须用**亮度+饱和度联合判据**，不能用单位类的 RGB-tol，否则打穿火球中心。
  3. `§3.5` 新增：`smoke_column` 亮度键控完整参数（`bgLum=202`），并标明代价是烟体层次被键掉。
  4. `§4.2.1` 新增：`scale` 语义必须重定义。现 `UnitEntity.cs:214` 传 25 → 占位圆放大到 **1600 世界单位 = 屏幕高的 2.7 倍**。
  5. `§4.2` 补正：四张特效图 **mode=RGB 无 alpha 通道**，alpha 恒 255。"AlphaBlend vs Additive" 的讨论成立于抠图之后，抠图前挂进场景是白方块。

---

## 0. 六条前置结论

**⓪ 最重要的一条：素材从未接入游戏，现在跑的还是圆/方占位图。**
实测 `*.meta`：24 张美术图 **`textureType: 0`（Default）、`spriteMode: 0`（None）**，而占位图 `placehold_circle.png` 是 `textureType: 8`（Sprite）、`spriteMode: 1`（Single）。
叠加两条：
- 15 个 `UD_*.asset` 的 `sprite` 字段**全部 `{fileID: 0}`（空）**；
- 全工程 `.sprite =` 只有 `PhaseBBootstrap` 给占位图赋值的几行，**没有任何代码从 `def.sprite` 给运行时单位设 sprite**。

`textureType: 0` 下 `LoadAssetAtPath<Sprite>()` 返回 **null**，`spriteMode: 0` 连 sprite 子资源都不生成。**所以即使程序写了 `sr.sprite = def.sprite`，拿到的也是 null。**

**这意味着本篇所有讨论的前提是"先把图变成 Sprite 并接上"，而不是"接入后朝向对不对"。** 施工顺序见 §十二，第 3 步（`textureType → 8`）是硬卡点。

**② 24 张图全部 RGB 无 Alpha，一张都不能直接接线。**
实测四角：`hq (227,227,221)`、`tank (253,253,251)`、`rifleman (211,209,203)`。不抠图直接接 `UnitDefinition.sprite`，屏幕上会出现 24 个白方块。

**③ 背景不是统一的白，是三档，必须逐图给阈值。**

| 档 | 背景 seed（实测·边缘环众数） | 资产 |
|---|---|---|
| 纯白 253–255 | `(253,253,253)` / `(255,255,255)` / `(255,255,254)` | powerplant, refinery, barracks, warfactory, radar, atcannon, tank, heavytank, nuke_cloud, muzzle_flash, build_panel_bg, minimap_frame, resource_icons |
| 浅灰 247–251 | `(249,249,245)` / `(247,247,249)` | harvester, explosion |
| 灰渐变 203–227 | `(227,225,221)` / `(227,225,219)` / `(253,253,251)` / `(253,253,253)` / `(205,203,199)` | hq, rifleman, bazooka, mgtower, smoke_column |

用单一 tol=12 去抠，后 5 张会残留大片灰底（实测 halo 像素：hq 4296、bazooka 4201、mgtower 3478、smoke_column 9503、rifleman 2794）。

**④ `localScale` 不能用，缩放只能走 PPU。**
`UnitView.cs:92` 每帧写 `transform.localScale = new Vector3(1, 1+bob, 1)`；`VFXManager.cs:90` 写 `localScale = Vector3.one * scale`。任何靠 Prefab localScale 定尺寸的方案都会被运行时覆写。本规格所有缩放一律由 `TextureImporter.spritePixelsPerUnit` 实现，Prefab `localScale` 恒为 1。

**⑤ 源素材没有统一比例，所以不存在"统一 PPU"。**
`hq` 与 `rifleman` 同为 256px 满框，实际比例差 5.5 倍——256px 的框不携带尺度信息。因此本规格给的是「锚定 PPU + 逐资产 PPU」，见 §1。

**⑥ 相机缩放参数自相矛盾，本规格的 PPU 推导依赖它先被修复。**
`PhaseBBootstrap.cs:249` 写 `cam.orthographicSize = 300f`，但 `TouchInputSystem.cs:15-16` 声明 `minZoom = 5f` / `maxZoom = 20f`，第 166 行 `Mathf.Clamp(orthographicSize + zoomDelta, minZoom, maxZoom)`。
**后果：玩家第一次双指缩放，正交半高会从 300 被瞬间夹到 20（缩小到 1/15）或 5（1/60）。** Bootstrap 从未给 `input.minZoom / maxZoom` 赋值（`grep` 只赋值了 `worldBounds`），所以是全默认值。
本规格所有"屏幕像素密度 1.8 px/单位"的推导，只在 `orthographicSize = 300` 时成立。**主程序必须先把 `minZoom/maxZoom` 改成 `150/450`**（围绕 300 的 0.5×–1.5× 缩放区间），否则 §1、§7 的尺寸结论全部失效。这是施工清单第 0 步，见 §十二。

---

## 一、PPU：锚定值 3.20 + 逐资产推导

### 1.1 锚定 PPU = 3.20

| 项 | 值 | 来源 |
|---|---|---|
| 摄像机正交半高 | 300 单位 | `PhaseBBootstrap.cs:249` |
| 默认缩放屏幕像素密度 | 1080 / 600 = **1.8 px/单位** | 由上一行 + 1920×1080 推算 |
| hq 逻辑尺寸 | 80×80 单位 | `PhaseBBootstrap.cs:186`（`buildingSize`） |
| hq 主体像素 | 256×203 px（去噪后，接地阴影带计入） | 实测 |
| **锚定 PPU** | **256 / 80 = 3.20** | — |

校验：hq 在屏幕上占 80×1.8 = 144 px，由 256 px 贴图缩到 144 px（缩小率 0.56），无放大模糊。
反证：若 PPU 取工程占位值 1.0，hq.png 会变成 **256 世界单位宽 = 逻辑尺寸的 3.2 倍**，糊满半个屏幕。
旁证：占位 `placehold_square.png` 是 64 px @ PPU 1.0 = 64 单位，为了凑到 hq 的 80 单位逻辑宽度，工程用 `localScale=1.6`（→102.4 单位，偏大 28%）做视觉补偿。接真图后这个 1.6 **必须删掉**，尺寸全部交给 PPU。

> **v1.1 更正**：上一版 `§1.2` 里「逐资产 PPU 3.20–25.60」被误读成"现状"。那是**目标值**。现状实测全部是 **100**，见 §1.2 开头。这个区别有实际后果：若主程序以为"现状已接近目标、只差微调"，就会跳过 §十二 第 3 步，结果所有单位停在 5px。

### 1.2 逐资产 PPU = 主体 bbox 宽 ÷ 目标世界宽

**先纠正一个容易误读的地方（v1.0 表述有歧义，v1.1 明确）**：下表 PPU 是**应当改成**的值，不是现状。
现状实测：`unity-project/Assets/_Project/Art/Units/*.png.meta` 13 份全部 `spritePixelsToUnits: 100`（`Effects/*.png.meta` 4 份也是 100），无例外。
**100 是 Unity 导入默认值，没有任何一张被配过。** 按 100 算，256px 贴图 = 2.56 世界单位，1080p 下约 5 px 高，只有 `selectionRadius`（8–12，直径 16–24 单位）的 1/6。**所以现状不是"尺寸偏小需微调"，是"全部错了 1~25 倍"。**

进一步实测：`Art/Units/hq.png` 与 `assets/hq.png` 的 md5 相同（`288914c5…`），**工程里的 24 张图就是未抠图的原图**，直接拖进去了。`alphaIsTransparency: 0`、`textureCompression: 1`（压缩开启）。这意味着当前工程跑起来，单位是带白底的 5px 小白方块。

12 张单位图里 10 张 bbox 触边（见 §2 例外列），Trim 后 sprite rect 基本等于整帧，PPU 由目标尺寸直接反算，不需要 localScale 补救。

| 资产 | 主体 bbox（去噪后，px） | 目标世界宽 | PPU = bboxW / 目标宽 |
|---|---|---|---|
| hq | 256×203 | 80 | **3.20** |
| powerplant | 253×248 | 44 | **5.75** |
| refinery | 256×247 | 56 | **4.57** |
| barracks | 256×251 | 48 | **5.33** |
| warfactory | 256×251 | 56 | **4.57** |
| radar | 240×241 | 44 | **5.45** |
| mgtower | 256×238 | 32 | **8.00** |
| atcannon | 249×211 | 36 | **6.92** |
| rifleman | 148×256 | 8.67 | **17.07** |
| bazooka | 254×256（噪点污染，不可用） | 8.67 | **17.07**（套 rifleman） |
| tank | 256×146 | 28 | **9.14** |
| heavytank | 256×203 | 32 | **8.00** |
| harvester | 256×256 | 28 | **9.14** |
| explosion（单帧 64×64） | 64×64 | 24 | **2.67** |
| muzzle_flash | 256×251 | 10 | **25.60** |
| nuke_cloud | 249×256 | 60 | **4.15** |
| smoke_column（alpha>0.25 区） | 192×256 | 14 | **13.71** |
| tiles/* | 256×256 | 142 | **1.80** |

---

## 二、逐资产规格总表

> Pivot 为归一化坐标，基准是**去噪后主体矩形**（左下 0,0）。导入前必须在 Sprite Editor 执行 Trim（Automatic，threshold 0），使 sprite rect = 主体 bbox。目标世界尺寸按「bbox 高宽比 × 目标宽」计算，不另设高度。

| 资产名 | 原尺寸(px) | 目标世界尺寸(W×H) | 抠图参数 | Pivot | 例外说明 |
|---|---|---|---|---|---|
| hq | 256×256 | 80×63.4 | seed`(227,225,221)` tol **23**・开运算 3×3・**禁填洞** | (0.5, 0.10) | 灰渐变底（ring_sd 9.6–10.3），tol<20 残边 4296 px。底 4 行前景跨 237 px = 接地阴影带，不可裁 |
| powerplant | 256×256 | 44×43.1 | seed`(253,253,253)` tol **12** | (0.5, 0.10) | 底部 30 行前景宽 116→0 收敛成尖角（等距底菱形下顶点），标准接地形态 |
| refinery | 256×256 | 56×54.0 | seed`(253,253,253)` tol **12** | (0.5, 0.10) | 边缘环最小值 `(44,27,19)`，本体深色贴边，floodfill 不误伤 |
| barracks | 256×256 | 48×47.1 | seed`(255,255,255)` tol **12** | (0.5, 0.10) | 唯一纯 255 底，阈值最稳；底部收窄 87→0 |
| warfactory | 256×256 | 56×54.9 | seed`(253,253,253)` tol **12** | (0.5, 0.10) | 前景占比 76.8%，抠图残留最少 |
| radar | 256×256 | 44×44.2 | seed`(255,255,254)` tol **12** | (0.5, 0.10) | **唯一不满框且不触边**（bbox 240×241，四周留 7 px），必须 Trim，否则 44 单位里含一圈空白 |
| mgtower | 256×256 | 32×29.7 | seed`(253,253,253)` tol **18**・开运算 3×3・闭运算 3×3 | (0.5, 0.10) | 灰噪点造成 29 个触边连通块；tol 12 时 halo 3478 px。开+闭运算必做 |
| atcannon | 256×256 | 36×30.5 | seed`(255,255,255)` tol **12** | (0.5, 0.10) | 主体 249×211 且非居中（下方留 31 px），Trim 后 pivot 按新 rect 重算 |
| rifleman | 256×256 | 8.67×15.0 | seed`(227,225,219)` tol **24**・开运算 3×3・**先去噪再取 bbox** | (0.5, 0.02) | 主体仅横向 148 px（x 63–210），两侧草地噪点 810 连通块；按 256 定尺寸会让人缩到一半大小 |
| bazooka | 256×256 | 8.67×15.0 | seed`(253,253,251)` tol **20**・同上 | (0.5, 0.02) | 噪点把 bbox 撑满（solid 0.71，213 连通块），Trim 结果不可信 → 套 rifleman 的 bbox 参数，接入前人工目检 1 次 |
| tank | 256×256 | 28×16.0 | seed`(253,253,253)` tol **12** | (0.5, 0.00) | bbox 256×146，上下各空 55 px；**左右双双触边（画面被切）**，无法还原全长，按可视部分定标 |
| heavytank | 256×256 | 32×25.4 | seed`(255,255,255)` tol **12** | (0.5, 0.00) | 左右触边；炮塔占高，与 tank 同场时高差 1.6 倍是设计意图 |
| harvester | 256×256 | 28×28.0 | seed`(249,249,245)` tol **18**・**禁填洞**・禁强去噪 | (0.5, 0.00) | 前景占比 **95.0%**，几乎无背景可抠；tol 12 打洞 13042 px，填洞吃掉整个车身 |
| effects/explosion | 256×256 | 单帧 24×24（4×4=16 帧） | seed`(247,247,249)` tol **12**・**逐帧独立边缘环**・**禁填洞** | (0.5, 0.5) | **不是单张爆炸，是 16 帧序列图**，见 §4.1。含黑烟 5251 px（亮度<20） |
| effects/muzzle_flash | 256×256 | 10×10 | seed`(253,253,253)` tol **12**・**先 floodfill 后 Trim** | (0.5, 0.5) | 中心实测 `(128,128)=(253,255,246)`，与背景同色但被橙红包围；实测中心 64×64 内被判为背景 **0/4096 px**，未被击穿。顺序不能颠倒 |
| effects/nuke_cloud | 256×256 | 60×61.7 | seed`(255,255,255)` tol **12** | (0.5, 0.0) | 蘑菇云自地面升起，pivot 在底；灰白烟体需 AlphaBlend |
| effects/smoke_column | 256×256 | 14×18.7 | **不用 floodfill**：亮度键控 `α = clamp((203 − lum)/203, 0, 1)` | (0.5, 0.0) | 背景 `(205,203,199)` 与烟体同为浅灰；floodfill 后 154 连通块 / 26 触边，必然残边。灰度键控是唯一路径 |
| tiles/desert | 256×256 | 142×142 | 不抠图（整版不透明） | (0.5, 0.5) | 不无缝：`|L−R|`=39.0 / `|T−B|`=44.8，vs 内部邻差 12.7 |
| tiles/grass | 256×256 | 142×142 | 不抠图 | (0.5, 0.5) | 最暗（均亮 84）；`|L−R|`=38.1 / `|T−B|`=32.3 |
| tiles/road | 256×256 | 142×142 | 不抠图 | (0.5, 0.5) | **接缝最小**：`|L−R|`=20.7 / `|T−B|`=22.6，内部邻差 12.2（比值 1.70，四张最低） |
| tiles/snow | 256×256 | 142×142 | 不抠图 | (0.5, 0.5) | 最亮（均亮 181，sd 55.1），会盖过所有单位，仅容许小面积点缀 |
| ui/build_panel_bg | 256×256 | 有效区 256×176（y 40–215） | seed`(254,255,255)` tol **12** | — | 上下各 40 px 是白底，**必须先 Crop 到 y 40–215** 再导入 |
| ui/minimap_frame | 256×256 | 256×256 1:1 | seed`(253,253,253)` tol **12** | — | 中央蓝区 x 53–201 / y 42–170 **不透明**，不能当遮罩用，见 §6.2 |
| ui/resource_icons | 256×256 | 4 格各 128×128 | seed`(254,253,251)` tol **12** | — | 天然 2×2，四块实测 bbox：TL(16,15)–(114,118)、TR(151,9)–(227,121)、BL(20,130)–(113,242)、BR(154,136)–(230,242) |

---

## 三、抠图判定标准

### 3.1 背景识别判据

1. **seed 取样**：取图像 2 px 宽边缘环（四边各 2 行/列）的**众数色**，不用四角单点。四角会被阴影和物体溢入污染——hq 四角实测从 `(214,213,209)` 到 `(245,245,239)`，单点取样会取错。
2. **tol 推导公式**：`tol = clamp(2.2 × ring_sd, 10, 46)`，`ring_sd` = 边缘环内点对 seed 的逐通道标准差。实测：纯白底 sd 0.7–3.6 → tol 落在下限 10–12；灰渐变底 sd 9.6–10.6 → tol 21–24。**不允许手填 tol**，因为 24 张图跨三档底，手填必错。
3. **mask 生成**：从四边所有通过 tol 判据的像素做 **4-邻域 floodfill**。**不用全图阈值**——全图阈值会把物体内部白色高光一起打穿（muzzle_flash 中心白核、explosion 白芯）。floodfill 保留的"内部同色洞"实测：hq 403 px、powerplant 359 px、mgtower 1313 px，这些是本体高光，必须保留。
4. **去噪**：3×3 开运算 1 遍（去边缘孤立噪点）+ 3×3 闭运算 1 遍（补羽化断点）。`hq / rifleman / bazooka / mgtower` 四张必做，其余可跳过。
5. **禁止填洞（2 张）**：`explosion`（黑烟与白芯交替，填洞吃掉整个火球）、`harvester`（前景 95%，填洞吃掉车身）。填洞 = 直接毁图。

### 3.2 透明边缘

- **羽化半径 = 1 px**（高斯 σ=0.7），只作用于 mask 边界内外各 1 px。
  依据：图缩到约 0.56 倍显示（256→144 px），2 px 以上羽化在缩小后糊成灰边。
- **必须做去白边 bleed**：对透明像素做 **3 轮**最近不透明像素颜色扩散填充。
  不做的话双线性采样会在边缘采到白色，每张资产带 1 px 白晕——这是 AI 白底素材最常见的翻车点，也是"看起来像贴上去的"根本原因。
- **例外**：`smoke_column` 走亮度键控，alpha 天然渐变，**不做羽化、不做 bleed**（会把浅灰烟涂成深灰）。

### 3.3 例外清单（这 5 张不能套统一参数）

| 资产 | 为什么例外 | 处理 |
|---|---|---|
| harvester | 前景 95%，背景几乎不存在 | tol 18 + 禁填洞 + 禁强去噪 |
| rifleman | 主体只占中间 148 px，两侧草地噪点 810 块 | 先去噪再取 bbox，按 bbox 定尺寸 |
| bazooka | 同 rifleman，噪点更密（bbox 被撑满，213 块） | 套 rifleman 的 bbox 参数 |
| mgtower | 灰噪点造成 29 个触边连通块 | tol 18 + 开闭运算各 1 遍 |
| smoke_column | 前后景同为浅灰，floodfill 失效 | 亮度键控，不用 floodfill |

### 3.4 特效专用判据：亮度 + 饱和度联合（v1.1 新增）

§3.1 的 `tol` 判据对**单位/建筑**够用（背景是中性灰白），但对**特效**会失效：爆炸火焰核心是近白高光，与白底同色，纯 RGB-tol 会把火球中心打穿。

特效改用**亮度 + 饱和度联合判据**：

```
背景判据 = (亮度 > 235) AND (饱和度 < 0.08)
饱和度 sat = (max(R,G,B) − min(R,G,B)) / max(R,G,B)，max=0 时 sat=0
```

仍然**仅从四边 floodfill**，不做全局扫描。实测四张：

| 特效 | 近白低饱和（判背景） | 高饱和 s>0.35（判主体） | 最大饱和度 | floodfill 抠掉 | 主体内残留近白 | 结论 |
|---|---|---|---|---|---|---|
| explosion | **41.8%** | 37.5% | 1.00 | 41.0% | **1.2%** | 可抠，火心被保护 |
| muzzle_flash | 29.9% | 42.5% | 1.00 | 29.6% | **0.4%** | 可抠，最容易 |
| nuke_cloud | 46.0% | 48.4% | 1.00 | 45.9% | **0.2%** | 可抠 |
| smoke_column | 16.4% | **0.0%** | **0.12** | — | — | **不可抠** |

**第 3 列「高饱和」是关键指标**：三张可抠图的饱和区占 37–48%，说明火球/焰心/蘑菇云有明确彩色主体可分离。`smoke_column` **全图最大饱和度 0.12、饱和区 0.0%**——纯灰阶图，没有任何颜色可作分割依据。

`饱和 <0.05 / <0.08 / <0.12` 三档实测差异极小（抠掉 40.6% / 41.0% / 41.1%），**取 0.08 即可**，不必精调。

### 3.5 smoke_column 亮度键控参数

```
bgLum = 202          （边缘环众数色 (205,203,199) 的均亮 = 202.33）
lum   = (R+G+B)/3
α     = clamp((bgLum − lum) / bgLum, 0, 1)
```

实测：alpha>0.25 区域 bbox = **192×256**（x 31–222）；逐行宽度 `y=5:72px / y=64:198px / y=128:93px / y=192:72px / y=250:62px`，上宽下窄，符合烟柱形态。

**代价（必须知道）**：烟体内部层次会被一起键掉。烟与底同为灰阶，亮度高的部分（烟的亮部）会被判成背景。所以 `smoke_column` 抠完不是"完整烟柱"，而是"烟柱的暗部"，视觉上偏瘦偏硬。**这是素材本身的限制，不是参数没调好。** 要更好的烟只能重出图（黑底 + Additive），不在本轮范围。

---

## 四、特效处理

### 4.1 explosion 是 16 帧序列图，不是单张爆炸

三重实测证据：

| 证据 | 实测值 |
|---|---|
| 自相关周期 | X 方向 lag 63/64 相关系数 **476**（最高峰），Y 方向 lag 62/63 为 350 |
| 4×4 切格（cell 64）每格前景数 | 行1 `1121→1456→2596→2957`、行2 `2367→2749→2735→2759`、行3 `2782→2739→2965→2249`、行4 `2176→1979→2298→1821` |
| 每格质心 | 全部落在 (28.6–34.6, 29.4–38.1)，即贴近格心 (32,32)。若是一张大图切块，边缘格质心会偏到格界 |

处理方式：
- `SpriteImportMode = Multiple`，Grid By Cell Size **64×64**，Offset (0,0)，Padding (0,0)，共 **4 行 × 4 列**，行优先命名 `explosion_00`…`explosion_15`（`explosion_00` = 左上角 = 起爆帧，`explosion_15` = 右下角 = 余烬帧）。
- 逐帧前景数呈"起→盛→衰"单峰：行1 `1121→1456→2596→2957`、行2 `2367→2749→2735→2759`、行3 `2782→2739→2965→2249`、行4 `2176→1979→2298→1821`。**证明读帧顺序必须是行优先**，若按列优先读会得到"忽大忽小"的抖动。
- 每帧**独立**取边缘环算 seed/tol，逐帧 floodfill（不共用第 0 帧参数）。
- 材质 **AlphaBlend，不用 Additive**：含黑烟 5251 px（亮度<20），Additive 会把黑烟加成全透明，爆炸变成一圈橙边。
- 播放：16 帧 @ **24 fps = 0.667 s**，`Wrap Mode = Clamp`（不循环），播完销毁或回池。
- 若嫌 0.667 s 太慢（RTS 爆炸通常 0.3–0.5 s），**只允许改 fps 到 32（0.5 s），不允许丢帧**。丢帧会让"起爆→膨胀"过渡断裂。

### 4.2 四张特效的材质与参数

| 特效 | 材质 | 混合 | Tint | 播放方式 | 生命周期 / 运动 | 走对象池 |
|---|---|---|---|---|---|---|
| explosion | `M_FXAlpha` | AlphaBlend | `(1, 0.92, 0.82)` | **序列帧**（16 帧，不循环） | 0.667 s | 是（现有 `explosionPool`，`VFXManager.cs:85`） |
| muzzle_flash | `M_FXAlpha` | AlphaBlend | `(1, 0.85, 0.40) × 1.4` | **单张 + 淡出**（alpha 1→0，线性） | 60 ms | 是（现有 `muzzlePool`） |
| nuke_cloud | `M_FXAlpha` | AlphaBlend | `(0.90, 0.86, 0.80)` | **缩放 + 淡出**（scale 0.7→1.15，末段 alpha 渐隐） | 1.2 s，y 升 24 单位 | 否（工程无调用点，**未接线**） |
| smoke_column | `M_FXAlpha` | AlphaBlend | `(0.62, 0.62, 0.61)` | **单张 + 持续淡出**（alpha 0.85→0，全程） | 2.0 s，y 升 40 单位 | 否（同上） |

**四张全部 AlphaBlend、不做 Additive 的原因**：这四张都是"带实体的烟/火"，不是纯光。Additive 会把白烟烧成白块、把黑烟完全吃掉。阶段 C 若要纯光效（枪口、命中火花），**另做一套纯黑底素材**走 Additive，不要拿这四张改。

> **v1.1 补正**：上一版说"smoke_column alpha 天然渐变"是错的。实测四张特效图 **mode 全部 = RGB，没有 alpha 通道**，转换后 alpha 恒 255。
> 所以"AlphaBlend vs Additive"这个讨论**建立在抠图之后**。抠图前四张挂进场景，玩家看到的是 256×256 白方块。顺序是：**先抠出 alpha（§3），再谈混合模式**。
> `smoke_column` 的 alpha 是**键控函数生成的连续值**（§2 表：`α = clamp((203−lum)/203)`），不是原图自带；做淡出时基础 alpha 已 <1，衰减曲线要按 `键控alpha × fade` 算。另外三张抠完是硬边 alpha（0/255 为主），淡出只能整图乘 `fade`，做不出逐像素层次——**若要层次，抠图时把火焰边缘 alpha 做 2–3 px 衰减**（这属于 §3.2 羽化的用途之一）。

**颜色乘算一律写在 `SpriteRenderer.color` 或 `Image.color`，不写进材质**。理由：写进材质会让同一材质的四个特效共享 Tint，且改一次影响全局；写进 renderer color 则每个实例独立，还额外修改 `_Color` 不会破坏 SRP Batcher 兼容性。

**材质总数上限 2 个**（`M_FXAlpha`、`M_SpriteDefault`）。材质越多 DrawCall 越碎。

**是否走对象池**：`explosion` 和 `muzzle_flash` 复用 `VFXManager` 现有 `explosionPool` / `muzzlePool`（`VFXManager.cs:74` 的 `GetFromPool`），**不要新建池**。`nuke_cloud` / `smoke_column` 频率极低（每次核弹 1 次），直接 `Instantiate` + `Destroy` 即可，进池反而常驻内存。

### 4.2.1 `scale` 语义必须重定义，否则爆炸会盖满全屏（v1.1 新增）

`VFXManager.cs:90` 无条件写 `go.transform.localScale = Vector3.one * scale`，而三个调用点传的值是：

| 调用点 | 传入 scale | 语义 |
|---|---|---|
| `UnitEntity.cs:214` 单位死亡 | **25f（载具）/ 15f（步兵）** | 历史上是"占位圆放大倍数" |
| `SuperWeaponSystem.cs:74` 核弹 | `blastRadius / 15f` = **9.33** | 想让特效覆盖爆炸半径 |
| `SuperWeaponSystem.cs:112` 子爆 | **3f** | — |

占位 `placehold_circle.png` 是 64×64 px @ PPU 1.0 = **64 世界单位**。乘 25 = **1600 世界单位**，而屏幕全高只有 600 单位（`orthographicSize=300`）——**现有占位爆炸直径是屏幕高的 2.7 倍**。这不是"素材问题"，是占位期的糙数值。

接真图后必须重定义：
- 特效 Prefab 的 `localScale` **保持 1**（PPU 决定尺寸，§1.2 里 explosion 单帧 24 世界单位）。
- `scale` 参数改为**倍数语义**：目标直径 = 24 × scale。
- 三个调用点改值：

| 调用点 | 原值 | 改为 | 结果直径 | 与逻辑对齐 |
|---|---|---|---|---|
| `UnitEntity.cs:214` 载具死亡 | 25 | **1.2** | 28.8 单位 | ≈ tank 宽度 28，刚好包住残骸 |
| `UnitEntity.cs:214` 步兵死亡 | 15 | **0.7** | 16.8 单位 | ≈ 步兵 15 高，略大 |
| `SuperWeaponSystem.cs:74` 核弹 | 9.33 | **11.7** | 280 单位 | ≈ blastRadius 140 的直径 280 ✓ |
| `SuperWeaponSystem.cs:112` 子爆 | 3 | **1.5** | 36 单位 | 次级爆点 |

**若改走 ParticleSystem**，`scale` 不能直接喂 `localScale`，要转成 `main.startSize`（乘到粒子初始尺寸）。语义仍是"倍数"，不要保留 15/25 这种历史值。

### 4.3 特效接线冲突（必须先解决，否则必翻车）

`VFXManager.SpawnExplosion()` 做的是：
```
var ps = go.GetComponent<ParticleSystem>();
if (ps != null) { ps.Clear(); ps.Play(); }
StartCoroutine(DeactivateAfter(go, 0.6f));
```
占位 `ExplosionPrefab` 挂的是 `SpriteRenderer`，没有 `ParticleSystem`。两个问题：

1. **走序列帧必须动这里**：SpriteRenderer + Animator（16 帧 24fps），或**不改脚本**、把 Prefab 换成 `ParticleSystem` + `Texture Sheet Animation`（推荐后者，脚本零改动，且能用 `Start Lifetime` 控制时长）。
2. `DeactivateAfter(go, 0.6f)` 与 16 帧 24fps 的 **0.667 s** 不相等 → **最后 1 帧被截**。改为 **0.70 s**。
3. `go.transform.localScale = Vector3.one * scale` 会覆盖 Prefab 上的缩放 → 特效 Prefab 的 `localScale` 必须保持 1，尺寸全靠 §1.2 的 PPU。

---

## 五、Pivot 规则

| 类别 | Pivot Y | 依据（实测） | 影响 |
|---|---|---|---|
| 建筑（8 张） | **0.10** | 接地菱形收敛行数：powerplant 22/248、radar 20/241、barracks 24/251 → 0.088 / 0.083 / 0.096，取中位 0.10 | 让底菱形的**中心**对齐 `transform.position`。用 0 会让整栋建筑前移半个底菱形 |
| 步兵（2 张） | **0.02** | rifleman bbox 底边 y=255（脚踩画面最底） | 留 2% 避免与地面共面抖动 |
| 载具（4 张） | **0.00** | tank bbox 底边 y=200，下方 55 px 全空；底部 4 行横向跨度 28 px 为平底 | 履带接地面 = transform 位置，压在地上而不是浮着 |
| 特效·径向（explosion / muzzle_flash） | **0.50** | 爆炸/焰心以中心为发射点 | 旋转缩放都以中心为原点 |
| 特效·升起（nuke_cloud / smoke_column） | **0.00** | 从地面升起 | 升起动画只改 `position.y`，不重算 pivot |
| 地形 tile | **0.50** | 平铺单元 | 对齐网格原点 |
| UI | — | 不用 pivot，用九宫格 border（§6） | — |

**校正要求**：pivot y 是起始值。接入后在场景放 1 个 hq + 开 40×40 网格，目视确认底菱形对齐，容许 ±3% 微调。**这是全篇唯一允许目视微调的项。**

---

## 六、排序与渲染分层

`sortingLayer` 名称与 `sortingOrder` 规则：

| 层名 | sortingOrder 规则 | 对象 |
|---|---|---|
| `Ground` | 固定 **−1000** | 地形基底（单 Quad，见 §7） |
| `GroundDeco` | **−950** | 路带、沙/雪斑块 |
| `Ore` | **−900** | 矿点（`circleSprite` 占位可保留） |
| `Shadow` | **−800** | 单位/建筑接触阴影 |
| `WorldSprite` | `Mathf.RoundToInt((1600 − y) × 2)` → 0…3200 | **单位与建筑共用同一层同一规则**。y 越小 order 越大 → 屏幕下方压住上方 |
| `FX` | `5000 + Mathf.RoundToInt((1600 − y) × 2)` | 地面级特效（explosion / smoke_column / muzzle_flash） |
| `FXAir` | 固定 **6000** | 空中特效（nuke_cloud） |
| `UI` | Canvas 排序 | 全部 UI |

规则要点：
- **单位与建筑必须同层同规则**。否则会出现"坦克钻进 HQ 后面还压着它"的穿帮——这是等距游戏最常见的排序 bug。
- `WorldSprite` 上限 3200 < `FX` 下限 5000，两层不重叠，避免特效被建筑切一半。
- **不要给 SpriteRenderer 加 `Sorting Group`**：本工程没有复杂嵌套，加了只增加逐帧开销。
- §7 的地形 Quad 必须落在 `Ground`，且 z 值要足够远（见 §7.1 第 1 条），否则会盖住所有单位。

---

## 七、地形铺设方案

### 7.1 结论：单 Quad + 材质 `_MainTex_ST` 平铺。不用 Tilemap，不用逐个 Sprite

| 方案 | DrawCall | 结论 |
|---|---|---|
| 逐个 Sprite（2400/142 × 1600/142 ≈ 18×12 = 216 个） | 1（可批）但 216 个 GameObject | 不选，节点数浪费 |
| Tilemap + 216 tile | 1–2 | 不选，本工程只有单层底图，Tilemap 的 chunk 机制用不上 |
| **单 Quad + 材质 `_MainTex_ST`** | **1** | **选这个** |

具体做法：
1. 建 1 个 `Quad`，`scale = (2400, 1600, 1)`，position `(1200, 800, 10)`（相机 z=−10，Ground 放 z=10 即最远层）。
2. 材质用 `Sprites/Default` 或 `Unlit/Transparent`，`_MainTex_ST = (16.90, 11.27, 0, 0)`。
   换算：`2400 / 142 = 16.90`，`1600 / 142 = 11.27`。
3. Filter Mode = **Bilinear**；Wrap Mode = **Repeat**；**关闭 Mipmap**（2D 正交，Mipmap 只让远景变糊）；`Max Size = 256`（原图只有 256，放大到 512 只会浪费显存）。
4. 顶点色乘算压暗：`_Color = (0.62, 0.62, 0.62, 1)`，理由见 §7.3。

**为什么 tile 世界尺寸定 142 而不是 400**：142 单位 × 1.8 px/单位 = **256 px = 1:1 像素映射**，贴图不放大不模糊。若定 400 单位，屏幕上 720 px 显示 256 px 贴图（2.8 倍放大），地面糊成一片色块。

### 7.2 用哪张：grass 打底，road 铺路带；desert/snow 只做点缀

实测四张 tile 的无缝性（接缝差 vs 内部相邻像素平均差，比值越接近 1 越无缝）：

| tile | 均亮 | `|L−R|` | `|T−B|` | 内部邻差 | 接缝比值 | 用途 |
|---|---|---|---|---|---|---|
| road | 91 | 20.7 | 22.6 | 12.2 | **1.70** | 最无缝 → 路带、大面积主底 |
| grass | 84 | 38.1 | 32.3 | 21.8 | 1.75 | 主底（最暗，衬托单位） |
| desert | 112 | 39.0 | 44.8 | 12.7 | 3.07 | 小面积斑块（≤15%） |
| snow | 181 | 65.3 | 65.0 | 21.4 | 3.03 | 仅点缀（≤5%） |

**没有一张是真无缝的**（比值 >1.5 都算有可见接缝）。因此禁止单张平铺铺满全图，否则会出现明显网格线。方案：

- **主底**：`grass`，`_MainTex_ST = (16.90, 11.27)`，全局压暗到 0.62。
  选 grass 而非 road 的理由：grass 均亮 84 最暗，而 13 张单位图的均亮在 67–147 之间，暗底才能让单位跳出来（灰度测试：亮底上场单位会糊掉）。
- **路带**：`road`，用 3 条独立 Quad，每条 `scale = (2400, 142, 1)`（横向贯通）或 `(142, 1600, 1)`（纵向贯通），`_Color = (0.55, 0.55, 0.55, 1)`。
  3 条贯通带的接缝只有横纵各 2 条，肉眼不可见；铺 216 个 tile 则会有 30 条接缝。
- **沙/雪斑块**：`desert` / `snow`，各 ≤3 个 Quad，尺寸 400×400 左右，`_Color` 压到 0.45，放在矿点与出生点周边做地形提示。**不作为主底**——snow 均亮 181 会直接盖住所有单位。

总 DrawCall：1（grass）+ 3（road）+ 6（斑块）= **10**。全部同一材质仅 `_MainTex` 不同的话，实际 4 个材质变体，仍可批。

### 7.2.1 关于「24 格网格」与 Sprite Atlas 的结论

按 400×400 世界单位铺（`2400/400 × 1600/400 = 6×4 = 24` 格）：

| 做法 | GameObject | DrawCall | 结论 |
|---|---|---|---|
| 24 个 Sprite 各铺一格 | 24 | 1–4（纹理不同则按图集数分裂） | 可行但没必要 |
| 24 格共用 1 张图 + 同图集 | 24 | **1** | 可批，但 24 个节点白占场景 |
| **1 个 Quad + `_MainTex_ST`** | **1** | **1** | **采用** |

- **不要开 Sprite Atlas 包地形。** 理由：地形是单张大图平铺，`Sprite Atlas` 的作用是把多张小图合并成一张以减少材质切换；本方案地形只有 1 个材质，图集化收益为 0，反而多一层图集构建与内存驻留。
- **要开 Sprite Atlas 的是单位与建筑**：13 张 256×256 = 13 个独立纹理，若不图集化，同屏 30 个单位会产生 30 次 SetPass。合成 1 张 2048×2048 图集（13 张 256 拼进去只占约 20%），同屏所有单位 **1 个 DrawCall**。这是本工程性价比最高的一项优化，`Create > 2D > Sprite Atlas`，勾 `Include in Build`，`Allow Rotation = false`（旋转会让 UV 错位）。
- 特效 4 张单独一个图集（都走 `M_FXAlpha`），UI 3 张单独一个图集（`2048×2048` 单图集，见 §8.0）。
- **图集总数控制在 3 个**（单位建筑 / 特效 / UI），对应 3 个材质，同屏最多 3 组 SetPass。

### 7.2.2 层次关系（从下到上）

| 序 | 内容 | 材质/色 | 说明 |
|---|---|---|---|
| 1 | grass 主底 Quad | `_Color 0.62` | 全屏底，暗度基准 |
| 2 | road 路带 3 条 | `_Color 0.55` | 比 grass 再暗 11%，压出"路面"感 |
| 3 | desert / snow 斑块 | `_Color 0.45` | 最暗，只做地形提示，不做主底 |
| 4 | 单位/建筑 | 原色（均亮 67–147） | 最亮，浮在最上 |

snow 原图均亮 181，压 0.45 后有效亮度 81，与 grass 压暗后（52）差 29，可辨但不抢；不压暗的话 snow(181) 会直接盖过所有单位。

### 7.3 为什么把地形压暗到 0.62

按§0 的色彩预算（主色 60% / 辅色 30% / 点缀 10%）：
- 地形是全屏最大的色块，必须承担"主色"角色，但不能抢单位。
- grass 原图均亮 84（0.33 亮度），再乘 0.62 → 有效亮度 52（0.20）。单位均亮 67–147，对比度 ≥1.3，满足灰度测试（去掉颜色主次关系仍成立）。
- 不压暗的后果：单位均亮 67（heavytank）在均亮 84 的 grass 上几乎同明度，近战时完全分不清谁是谁。

### 7.4 地形接线点

现有 `ThemeManager.terrainTile`（`Core/ThemeManager.cs:97`）是 `Sprite` 类型，**只能放 1 张、无法平铺** → 与本方案冲突。两条路：
- **推荐**：`MapDefinition.terrainPreview`（`Data/MapDefinition.cs:18`）继续当小地图缩略图用；新增 `Material terrainMaterial` 字段给 Quad 用。**这是要主程序改的**（本规格不改工程文件）。
- 过渡方案：先用 `ThemeManager.terrainTile` 放 grass + `Image.Type.Tiled` 的 UI Image 顶一层，等主程序加字段后再切 Quad。

---

## 八、UI 面板规格

Canvas 参考分辨率 **1920×1080**，`CanvasScaler.ScaleWithScreenSize`（`PhaseBBootstrap.cs:363-364` 已配置）。

### 8.0 先说一个坏消息：BuildUI 里没有建造栏和小地图

我逐行读了 `PhaseBBootstrap.cs:354-431` 的 `BuildUI`，它实际创建的 UI 对象只有 8 个：

| 现有对象名 | 类型 | 位置 | 用途 |
|---|---|---|---|
| `OreText` | Text 36px | 左上 | 矿石数 |
| `PowerText` | Text 36px | 左上 | 电力 |
| `TechText` | Text 36px | 左上 | 科技 |
| `UnitInfoPanel` | Image(纯色 0,0,0,0.75) | 左下 480×190 | 选中单位信息 |
| `ToastPanel` | Image(纯色 0,0,0,0.7) | 底部中央 | 提示条 |
| `AlertPanel` | Image(纯色 0.5,0.05,0.05,0.85) | 顶部中央 | 警报 |
| `GameOverPanel` | Image(纯色 0,0,0,0.85) | 全屏 | 结算 |
| `UI Canvas` | Canvas | — | 根节点 |

**没有任何建造按钮容器、没有小地图容器、没有资源图标。** Bootstrap 结尾的提示语写着"底部按钮建造"，但代码里没有生成这些按钮——阶段 B 的建造入口目前只存在于 `EconomySystem` 的逻辑层，没有 UI。

所以三张 UI 图的接线目标不是"替换某个现有 Image"，而是**新建三个容器**。对应关系：

| 素材 | 挂到哪个对象 | 现状 |
|---|---|---|
| `build_panel_bg.png` | **新建** `BuildPanel`（挂在 `UI Canvas` 下，anchor 底部拉伸） | 不存在，需新建 |
| `minimap_frame.png` | **新建** `MinimapPanel`（挂在 `UI Canvas` 下，anchor 右上） | 不存在，需新建 |
| `resource_icons.png` | 改造现有 `OreText` / `PowerText` 为 `Image + Text` 子节点组合 | 现为纯 Text |

**这一条比任何 border 值都重要：素材接不进去不是参数问题，是目标对象不存在。** 下列 §8.1–8.3 的 border 值成立的前提是这三个容器先被建出来。

### 8.1 build_panel_bg（底部建造栏）

| 项 | 值 | 依据 |
|---|---|---|
| 源有效区 | y **40–215**（高 176 px），x 0–255 | 实测纵向内容区间；上下各 40 px 纯白 |
| 导入前处理 | Crop 到 `(0,40,256,216)` → 256×176 | 不裁则面板上下各有一圈白边 |
| 目标 RectTransform | 宽 = 屏宽，高 **132 px**（@1080 基准） | 176 px × 0.75 缩放，保留原比例 |
| 九宫格 border | Left **31**, Right **36**, Top **77**→按裁后换算 **37**, Bottom **76**→**36** | 实测装饰边界：左 31 / 右 36 / 上 77 / 下 76（裁掉 40 后上下各减 40，下限 0） |
| Image.Type | **Sliced** | 横向拉伸时左右铆钉不缩放 |

注：裁后高度 176，Top 37 + Bottom 36 = 73 < 176，中段可拉伸 103 px，够用。

**挂载目标**：新建 `UI Canvas/BuildPanel`，`RectTransform` 设 anchorMin `(0,0)` / anchorMax `(1,0)`，offsetMin `(0, 54)` / offsetMax `(0, 54+132)` —— 底边距 54 px 满足 5% 安全区（1080×5%=54）。
面板内放 `HorizontalLayoutGroup`（`spacing 12`，`padding 40/40/24/24`，`childAlignment MiddleCenter`），子项为建造按钮，每个 **96×96**。

### 8.2 minimap_frame（小地图边框）

| 项 | 值 | 依据 |
|---|---|---|
| 九宫格 border | Left **73**, Right **74**, Top **86**, Bottom **110** | 实测装饰边界 |
| Image.Type | **Sliced** | 四边金属框不随尺寸变形 |
| 内部可视区 | x 53–201 / y 42–170（**149×129 px，不透明蓝灰**） | 实测蓝区 bbox |
| **关键约束** | 这张图**没有透明镂空**，中心是 `(218,226,229)` 不透明蓝 | 不能当遮罩用 |

**做法**：`minimap_frame` 只做**外框装饰**，另外放一个 `RawImage`（RenderTexture 小地图）压在它下面，尺寸 `149×129 × 缩放`，位置对齐内区。层级：RawImage(小地图) → minimap_frame(框)。不要试图从 frame 里"挖洞"。

**挂载目标**：新建 `UI Canvas/MinimapPanel`，`RectTransform` anchorMin/anchorMax 均 `(1,1)`，`pivot (1,1)`，`offsetMin (-298, -298)` / `offsetMax (-54, -54)` → 尺寸 **244×244**，右边距 54 px、上边距 54 px，双双满足 5% 安全区。
容器内分两层：
- `MinimapPanel/MapView`：`RawImage`，尺寸 **146×126**（对应内区 149×129 按 244/256=0.953 缩放），居中偏上对齐内框；
- `MinimapPanel/Frame`：`Image`（minimap_frame，Sliced，border 73/74/86/110），铺满 244×244。

### 8.3 resource_icons（资源图标）

| 项 | 值 |
|---|---|
| 切分 | `SpriteImportMode = Multiple`，Grid By Cell Size **128×128**，4 格：`icon_00`(TL) `icon_01`(TR) `icon_02`(BL) `icon_03`(BR) |
| 每格再 Trim | 各格 bbox 见 §2 表，Trim 后用 `preserveAspect` 统一到 **48×48 px** 显示 |
| UI 尺寸 | 48×48（@1080 基准），与正文 24 px 字号等高两倍 |
| Pivot | (0.5, 0.5) |
| 九宫格 border | **无**（4 个独立图标，不做 Sliced，`Image.Type = Simple` + `preserveAspect = true`） |

四块实测 bbox 不一致（99×104 / 77×113 / 94×113 / 77×107），说明是 4 张独立图标贴进 2×2 格，**必须逐格 Trim 后等比归一**，否则四个图标视觉大小不同。

**挂载目标**：改造现有 `OreText` / `PowerText` / `TechText`（`PhaseBBootstrap.cs:369-376`）。
现状是三个裸 `Text`，字号 36。改法：给每个包一层 `Image + HorizontalLayoutGroup`：
- `OreText` → 新建父节点 `OreGroup`（`Image` 用 `icon_00`，48×48，`preserveAspect`）+ 子节点 `OreText`（Text 36px，位置右移 56 px）；
- `PowerText` → `icon_01`（闪电/电池）；`TechText` → `icon_02`（科技）。
- `icon_03` 留作第 4 资源位（阶段 C 若有第 4 种资源再用），暂不挂。

**注意 `PhaseBBootstrap.BuildUI` 里 Text 的字号是 36，不是 §8.4 说的 24。** 36 ≥ 24 满足下限，但 `UnitInfoPanel` 里的 `HP` / `DMG` / `Range` 是 28，也满足。**唯一不达标的是 `ToastText` 的 34 和 `infoCount` 的 30，都在 24 以上，实际全部达标。** 本规格不改字号，只记录现状合规。

### 8.4 三条 UI 硬规则（本规格强制）

1. **信息层级靠大小和位置，不靠颜色**：建造栏按钮 96×96，资源数字 32 px 字号，单位名 24 px。战斗中没有玩家会去分辨颜色差异。
2. **安全区**：建造栏底边距屏幕底 ≥ 5%（1080×5% = **54 px**）；`minimap_frame` 右边距 ≥ 54 px；移动端刘海/圆角会吃掉边缘。
3. **字号下限**：移动端正文 ≥ **24 px**（1080p 基准）。低于 24 在 5 寸屏看不清。48×48 图标下方标签不超过 6 个汉字。

**UI 验收方法**：把截图缩到 **50%** 再看一遍。读不出来的信息就是放错位置了。

---

## 九、接线总表（规格项 / 资产文件 / 接线点）

| 规格项 | 资产文件 | 接线点 |
|---|---|---|
| 建筑 sprite | `hq/powerplant/refinery/barracks/warfactory/radar/mgtower/atcannon.png` | `UnitDefinition.sprite`（`Data/UnitDefinition.cs:31`），按 unitId 一一对应 |
| 单位 sprite | `rifleman/bazooka/tank/heavytank/harvester.png` | 同上。**`sniper`、`artillery` 无素材 → 缺失，需在 `PhaseBBootstrap.Def()` 里先用同类占位或标 TODO** |
| PPU | 全部 18 张 | `TextureImporter.spritePixelsPerUnit`，逐资产取 §1.2 值 |
| Pivot | 全部 18 张 | Sprite Editor → Pivot → Custom，按 §2 表填写 |
| 排序 | 全部 | `SpriteRenderer.sortingLayerName` = `WorldSprite` / `FX` / `FXAir`，`sortingOrder` 按 §6 公式 |
| 特效 explosion | `effects/explosion.png`（16 帧） | `VFXManager.SpawnExplosion()`（`Presentation/VFXManager.cs:85`）+ `ExplosionPrefab`；**需把 `DeactivateAfter` 0.6→0.70** |
| 特效 muzzle_flash | `effects/muzzle_flash.png` | `VFXManager.SpawnMuzzleFlash()` + `MuzzleFlashPrefab`，位置已由 `UnitEntity.cs:140` 提供 |
| 特效 nuke_cloud / smoke_column | `effects/nuke_cloud.png`、`smoke_column.png` | **未接线**：当前工程无核弹/烟雾调用点。阶段 C 需要时挂 `FXAir` |
| 地形主底 | `tiles/grass.png` | **未接线**：需新增 `Material terrainMaterial` 字段（§7.4），当前只有 `ThemeManager.terrainTile`（Sprite，不能平铺） |
| 地形路带/斑块 | `tiles/road.png`、`desert.png`、`snow.png` | **未接线**：同上 |
| UI 建造栏 | `ui/build_panel_bg.png` | **未接线**：`PhaseBBootstrap.BuildScene` 里的底部按钮容器改用 Sliced Image；需先 Crop |
| UI 小地图框 | `ui/minimap_frame.png` | **未接线**：小地图容器外框 Image(Sliced) + 内层 RawImage |
| UI 资源图标 | `ui/resource_icons.png` | **未接线**：资源条 Image，切 4 格 |
| 受击闪白 | — | `UnitEntity.cs:196` 依赖 shader `_FlashAmount` 属性。**`Sprites/Default` 没有这个属性** → 接真图后闪白会静默失效。需主程序换 shader 或在材质量里补该属性 |

**标「未接线」的 11 项 = 文件存在但没接进游戏。** 按经验这是最容易出现"假完成"的地方：素材目录看着满了，进游戏还是一片方块。

---

## 十、验收点（五条，全部肉眼可判定）

| # | 验收点 | 判定方法 | 不通过说明什么 |
|---|---|---|---|
| 1 | **无白边** | 截图放大 400%，看 hq / tank / rifleman 三者轮廓边缘，不允许出现 1 px 白色或浅灰描边 | §3.2 的 bleed 3 轮没做或做少了 |
| 2 | **hq 上屏尺寸正确** | hq 在默认缩放下宽约 **144 px**（=80 单位×1.8 px/单位），肉眼约等于屏宽的 7.5% | PPU 填错（应是 3.20）或 `localScale` 没归 1 |
| 3 | **坦克宽约 28 单位** | tank 宽 ≈ 屏宽的 1/38；与 hq 并排时 hq 宽度是 tank 的 **2.9 倍** | PPU 9.14 填错，或 tank 忘了 Trim（未 Trim 时上下 55 px 空白会让视觉高度虚高） |
| 4 | **剪影可辨** | 截图去色后填纯黑，`rifleman / bazooka / tank / heavytank` 四者仍能区分（人形 vs 履带、炮管长短） | §1.2 尺寸定错，不是配色问题 |
| 5 | **地形不抢主体** | 地面明显暗于所有单位；snow 斑块出现处，其上单位仍清晰可辨 | §7.2.2 的 0.62 / 0.45 压暗漏配 |

补充（等 §十一 朝向归零完成后生效）：**6. tank / heavytank 朝向一致**——两者都在 `facing = 0` 时不旋转，车体长轴水平、炮管朝屏幕右。若一个朝右上、一个朝右下，说明 §十一 的 `spriteForwardDeg` 没统一。

---

## 十一、朝向归零（v1.2 重写：原方案有 4 处错）

> **v1.2 说明**：本节 v1.1 版给的表（tank 0 / heavytank +16 / atcannon +14）和公式（`+90f + spriteForwardDeg`）**全部作废**。animation-director 复核后我用两套独立方法重测，确认自己错了，下面是修正版。

### 11.1 我原来错在哪

| # | 我 v1.1 的说法 | 错在哪 |
|---|---|---|
| 1 | 用 `PCA 主轴` 判断"前向" | **主轴是 180° 周期的无向线**，只说车体长轴方向，**不说车头在哪一端**。用它推前向，有一半概率把车头判反 180°，而且代码不报错，只会看到车倒着开 |
| 2 | tank 偏离仅 −3°，可当基准填 0 | tank **炮管实测 +26°、车体主轴 +1°**，两者差 25°，**素材本身车体与炮管就不一致**。填 0 的结果是炮管永远偏右上 26° |
| 3 | heavytank 填 **+16** | **符号反了**。实测炮管在 **−20°**（342°~328°区），主轴 −16°，两者同侧、只差 4°，自洽。填 +16 会让重坦斜 36° 且方向相反 |
| 4 | 公式 `+ 90f + spriteForwardDeg` | **`+90f` 本身就是错的**。它等价于隐含"素材朝正上"，而实测 tank 炮管朝 +26°（偏右上而非正上）。保留 `+90` 再叠加补偿，等于在原 bug 上再叠 64° 误差 |

### 11.2 正确的测量方法与结果

**判据一：PCA 主轴（只给"车体长轴"，不给朝向）**
tank +1.0° / heavytank −16.1° / atcannon −19.1° / harvester −23.4°

**判据二（决定性）：径向窄长突起检测（炮管专用）**
逐 1° 射线测"径向可达距离 ÷ 该处横向宽度"。炮管是**径向最长且横向最窄**的结构，比值会显著高于车体。实测比值最高方向：

| 资产 | 炮管方向（比值最高） | 比值 | 主轴 | 两者关系 |
|---|---|---|---|---|
| tank | **+26°** | 11.08（reach 144 / width 13） | +1.0° | 差 25°，**不一致** |
| heavytank | **−20°**（340°~328°簇） | 1.97~2.02 | −16.1° | 差 4°，**自洽** |
| atcannon | **−35°**（325°~322°簇） | 3.54 | −19.1° | 差 16° |
| harvester | **+45° / +225°**（推土铲，双向对称） | 2.78 / 2.61 | −23.4°（elong 1.02 ≈ 圆形） | 无明显车头，见 11.4 |

tank 的比值 11.08 是四张里最高的——它的炮管又细又长，特征极强，**这个测量几乎不可能错**。

**判据三（交叉验证）：径向距离对向比**
炮管侧距离 ÷ 对侧距离。tank 26° 比 206° = 141.1 / 100.4 = **1.41**；heavytank 121° 比 301° = 1.37（121° 是车尾高射机枪方向，非炮管）；atcannon 96° 比 276° = 1.48。

### 11.3 修正后的填表与公式

**选"炮管锚"作前向定义**（玩家判断朝向看炮口，不看车体）。

```csharp
// 字段语义：素材炮管的方位角（屏幕坐标系，+X 为 0°，逆时针为正）
float zDeg = entity.facing * Mathf.Rad2Deg - def.spriteForwardDeg;
```

> 注意：**不再有 `+ 90f`**。`+90f` 是在"素材朝正上"的前提下才成立的偏移，这些素材都不满足。

| 资产 | `spriteForwardDeg`（炮管方位角） | 置信度 | 依据 |
|---|---|---|---|
| **tank** | **+26** | **高** | 窄长突起比值 11.08，特征极强；对向比 1.41 佐证 |
| **heavytank** | **−20** | **低（v1.3 降级）** | ~~342°~328° 簇比值 1.97~2.02，与主轴 −16° 差 4° 自洽~~ **此推理已作废**。v1.3 §14.5 实测：heavytank **整张图不存在"细且平行"的炮管特征**（最细外向突起 63 px，tank 为 17 px；放宽到 64 px 仍无方向）。原"与主轴差 4° 自洽"是同轴双突起导致的假吻合——主轴只会跟近的那个突起吻合，证明不了那根突起是炮管。**−20 保留为目视暂定值**，须在编辑器里人眼确认（§14.6） |
| **atcannon** | **−35** | **中** | 325°~322° 簇比值 3.54；但与主轴 −19° 差 16°，取簇中心 ±3° |
| **harvester** | **+45**（推土铲） | **低** | 见 11.4，**建议填 0 不做补偿** |
| mgtower | 不适用 | — | 塔类不旋转；且 `UnitView.cs` 只对 `Vehicle`/`Infantry`/`Building` 分派，塔走 Building 分支不旋转 |
| rifleman / bazooka | 不适用 | — | 站立人形，走 `flipX` 镜像 |

**容差**：`spriteForwardDeg` 允许 ±5° 误差。因为单位宽 28 单位时，5° 的顶点位移是 `14 × sin5° = 1.2` 单位 ≈ 屏幕 2 px，不可见。填值不必追求 1° 精度。

### 11.4 harvester 特殊：没有"车头"这个概念

harvester 的 PCA `elong = 1.02`（几乎正圆），径向比值的两个峰在 **+45° 和 +225°**（正好对向，推土铲两端对称）。

**结论：这张图在结构上是双向对称的，不存在明确的"前"与"后"。** 强行填 45 或 225 都是在猜。**建议 `spriteForwardDeg = 0` 不做补偿**——反正铲子两头一样，倒着开也看不出来。

这也解释了为什么 animation-director 报 223.3° 与 44° 两个值能同时成立：它们相差 179°，是同一个双向轴的两端。

### 11.5 一个比他报告更前置的问题：Harvester 根本没有表现分支

`UnitView.cs:33-45` 的分派只有三条：
```csharp
if (entity.def.isBuilding)            UpdateBuildingVisual(...);
else if (category == Vehicle)         UpdateVehicleVisual(...);
else if (category == Infantry)        UpdateInfantryVisual(...);
```
而 `UnitCategory` 枚举有 5 个值（`UnitDefinition.cs:50-57`）：`Infantry / Vehicle / Harvester / Building / Defense`。

**`Harvester` 和 `Defense` 两个类别既不进 Vehicle 分支也不进 Infantry 分支 → 它们没有任何朝向表现。**

harvester 的 category 实测是 `UnitCategory.Harvester`（`PhaseBBootstrap.cs:195`）。所以：
- 采矿车在游戏里**朝向永远不动**（不旋转、不 flip），跟它填什么 `spriteForwardDeg` 无关；
- `mgtower` / `atcannon` 是 `UnitCategory.Defense`（`PhaseBBootstrap.cs:206-207`），同理不旋转——**这一点恰好让 atcannon 的 −35° 变得不重要**。

**所以 `spriteForwardDeg` 实际只需要填 2 个值：tank +26、heavytank −20。** atcannon / mgtower 走 Defense 分支不旋转，harvester 走 Harvester 分支不旋转。

若未来要让 harvester 也转向，需先给 `UnitView` 加 `Harvester` 分支（复用 `UpdateVehicleVisual`），届时有 2 个选择：填 45 或填 0（见 11.4，我倾向 0）。

### 11.6 光照不自洽 —— 不归本规格

`spriteForwardDeg` 补偿的是"素材朝向偏了"，**修不了光照方向**：图里高光是烘死的，旋转时高光跟着转，而战场光照固定。这个缺陷代码改不了。

解法（轮廓描边 + 底座阵营色环）已由 animation-director 提给 team-lead 排入 `art-direction-v2` 修订。**本规格只记录结论，不实现** —— 描边与色环是可视化设计，不是接入规格。

---

## 十二、给主程序的施工清单

按顺序执行。前一步不完成，后一步必失败。

| # | 步骤 | 输入 | 产出 | 关键参数 |
|---|---|---|---|---|
| **0** | **修相机缩放** | `TouchInputSystem.cs:15-16`、`PhaseBBootstrap.cs:311-313` | 改后的脚本 | **用相对推导，不写死数字**：`minZoom = mainCamera.orthographicSize * 0.5f`、`maxZoom = mainCamera.orthographicSize * 1.5f`（当前 300 → 150/450）。**不做这步，下面所有尺寸在玩家缩放过一次后全部失效**（见 §0⑥）。写死 150/450 等于把基准值抄两遍，以后调基准必有一处忘改——这个 bug 的成因正是同一件事写在两个地方 |
| **1** | 写抠图脚本 | `assets/` 24 张原图 | `tools/cutout_assets.py` | 按 §3.1 的 seed/tol 公式 + §3.2 的 1px 羽化 + 3 轮 bleed；输出 RGBA |
| **2** | 跑抠图 | `cutout_assets.py` | `Assets/_Project/Art/Sprites/*.png`（18 张）+ `Art/Tiles/*.png`（4 张）+ `Art/UI/*.png`（3 张） | 5 张例外图按 §3.3 单独跑；explosion 切 16 帧；**原图不动** |
| **3** | 配导入参数 | `Art/Sprites/*.png` | `.meta` 文件 | **硬卡点：`textureType: 0 → 8`（Sprite）、`spriteMode: 0 → 1`（Single）**。不改这两项，`LoadAssetAtPath<Sprite>()` 返回 null，后面全废（§0⓪）。现状实测：13 单位 + 4 特效 + 4 地形 + 3 UI 的 `.meta` **全部 `textureType: 0` / `spriteMode: 0` / `alphaIsTransparency: 0` / `spritePixelsToUnits: 100`**。再逐资产改 PPU 为 §1.2 值；Pivot 按 §5；`spriteImportMode`：单位/建筑 Single、explosion Multiple(64×64)、resource_icons Multiple(128×128)；**`alphaIsTransparency` 全部改 1** |
| **4** | 建 3 个 Sprite Atlas | `Art/Sprites`、`Art/Effects`、`Art/UI` | `Atlas_Units.spriteatlas` 等 | 单位 2048²（`Allow Rotation = false`）、特效 512²、UI 2048²（§7.2.1） |
| **5** | Prefab 改造 | `Prefabs/UnitPrefab.prefab`、`BuildingPrefab.prefab` | 改后 Prefab | **删 `BuildingPrefab` 的 `localScale=1.6`**（`PhaseBBootstrap.cs:107`）；两者 `localScale` 归 1；`sortingLayerName = WorldSprite` |
| **6** | 补 sprite 赋值代码 | `UnitDefinition.sprite`（字段已有但**无消费者**） | `UnitEntity.cs` 补 1 行 | `Initialize()` 里加 `if (def.sprite != null && spriteRenderer != null) spriteRenderer.sprite = def.sprite;`。**这是 24 张图能否上屏的唯一开关**。注意：`Awake()` 的 `GetComponentInChildren<SpriteRenderer>()` 早于 `Initialize()` 执行，若 Prefab 结构按第 5 步改成多层，需确认拿到的仍是真正渲染的那个 SpriteRenderer |
| **6b** | **清掉占位染色** | `PhaseBBootstrap.cs:98,106,115,132` | Prefab 的 SpriteRenderer.color | **`us.color = new Color(0.25f, 0.65f, 0.84f)`（单位蓝）、`bs.color = (0.35f,0.42f,0.5f)`（建筑灰）、`os.color = (0.77f,0.66f,0.42f)`（矿点）全是占位期染色，存在 Prefab 上。**真图接入后单位会整张染成蓝色**——这不是新 bug，是占位染色没清。接真图时必须 `sr.color = Color.white`。特效同理（`MakeFxPrefab` 第 132 行给每个特效设了颜色） |
| **7** | 填 13 个 UnitDefinition 的 sprite | `Data/Core/UD_*.asset` | 13 个 asset 被赋值 | 按 unitId 对应；`sniper` / `artillery` 无素材 → 留空 + 标 TODO |
| **8** | 加朝向补偿字段 | `UnitDefinition.cs`、`UnitView.cs` | 改后脚本 | 加 `float spriteForwardDeg`（语义=**炮管方位角**）。`UnitView.cs:81` 的 `targetAngle = entity.facing * Rad2Deg + 90f` 改为 **`float zDeg = entity.facing * Mathf.Rad2Deg - def.spriteForwardDeg;`**——**`+90f` 必须删掉**，它隐含"素材朝正上"，实测 tank 炮管朝 +26°，保留 `+90` 会引入 64° 误差（§11.1 第 4 条）。填值只需 2 个：tank **+26**、heavytank **−20**（§11.3）；atcannon/mgtower 走 `Defense` 分支、harvester 走 `Harvester` 分支，**都不旋转，填了也不生效**（§11.5） |
| **9** | 地形铺设 | `Art/Tiles/*.png` | 场景内 1 Quad + 3 路带 Quad + ≤6 斑块 Quad | `_MainTex_ST = (16.90, 11.27, 0, 0)`；grass `_Color 0.62`、road 0.55、斑块 0.45；`sortingLayer = Ground`，z=10（§7.1） |
| **10** | 地形字段增补 | `ThemeManager.cs:97` | 新增字段 | 现有 `Sprite terrainTile` 只能放 1 张不能平铺 → 新增 `public Material terrainMaterial`（§7.4） |
| **11** | 特效替换 | `Art/Effects/*.png` | `Prefabs/ExplosionPrefab`、`MuzzleFlashPrefab` | `M_FXAlpha` AlphaBlend；explosion 走 ParticleSystem + Texture Sheet Animation（16 帧，24fps）或 SpriteRenderer+Animator |
| **12** | 修特效回收时长 + scale 语义 | `VFXManager.cs:90,95`、`UnitEntity.cs:214`、`SuperWeaponSystem.cs:74,112` | 改后脚本 | `DeactivateAfter(go, 0.6f)` → **`0.70f`**（§4.3）；**`scale` 从"localScale 倍数"改为"直径倍数"**，调用点改值：载具 25→1.2、步兵 15→0.7、核弹 9.33→11.7、子爆 3→1.5（§4.2.1）。不改则爆炸直径 1600 单位 = 屏幕高的 2.7 倍 |
| **13** | 修闪白 shader | `UnitEntity.cs:196` | 材质或 shader 替换 | `_FlashAmount` 在 `Sprites/Default` 上**不存在**，受击闪白会静默失效（§九 末行） |
| **14** | 建 3 个 UI 容器 | `PhaseBBootstrap.BuildUI` | `BuildPanel` / `MinimapPanel` / 3 个 icon Group | **这三个容器当前不存在**（§8.0）；border 值按 §8.1–8.3 |
| **15** | UI 图预处理 | `Art/UI/*.png` | 处理后的 png | `build_panel_bg` 先 Crop 到 y 40–215；`resource_icons` 切 4 格后逐格 Trim |

**前置依赖**：步骤 0 → 1 → 2 → 3 → 4 是链式的，任何一步跳过后面全废。步骤 6 是"素材能不能上屏"的总开关，**比 7 更优先**——先有赋值代码，再填表。

**最小可验证路径**（想先看效果就跑这条）：0 → 1 → 2（只跑 hq/tank/rifleman 三张）→ 3 → 5 → 6 → 7（只填这三个）→ 按 Play。若三个单位正确上屏且尺寸对，再补全其余。

---

## 十三、不做

- 不为 `sniper` / `artillery` 造素材（缺失，标 TODO）。
- 不重画任何原图，不修改 `assets/` 下任何文件。抠图产物另存到 `unity-project/Assets/_Project/Art/`。
- 不做动画（行走/开火帧序列）——归 animation-director。本规格只保证"静态图能正确显示"。
- 不改 Unity 工程文件（含 `PhaseBBootstrap.cs` 里的 `localScale=1.6`、`TouchInputSystem` 的 `minZoom/maxZoom`、`VFXManager` 的 `DeactivateAfter`）。这些均以「规格 + 行号 + 目标值」形式列在 §十二 施工清单，由主程序执行。
- 不返修载具 16° 朝向偏差，改用 `spriteForwardDeg` 补偿（§十一）。

---

## 十四、素材边界裁切实测（v1.3 新增，回应 animation-director v1.5 §13.3.2）

animation-director 报"5 张图主体伸出画布"，用的判据是**边界像素 alpha 均值 198、范围 171–202**。我独立复测后确认**结论方向对，但那张清单要改两处**：一处**少判了一张**（`mgtower` 实为干净），一处**多判了 5 张**（`hq`/`refinery`/`tank`/`bazooka`/`rifleman` 实为干净），且核心判据**不能用 alpha**。下面是我实测的替代判据与定稿表。

### 14.1 为什么 alpha 判据不成立（先破后立）

`a == 202` 是 `tools/key_assets.py:210 feather()` 用零填充做 3×3 高斯卷积的**算术产物**，与"像素是不是实体"完全无关：

```
sigma = 0.7 → 归一化核
   0.0439  0.1217  0.0439
   0.1217  0.3377  0.1217
   0.0439  0.1217  0.0439

画布边框像素：有 3 个核位置落在图外（zero padding）
  丢弃权重 = k[0,0]+k[1,0]+k[2,0] = 0.2095
  保留权重 = 0.7905
  若掩膜=1（该像素本身是前景）→ alpha = 255 × 0.7905 = 201.59 → 取整 202
```

实测三项验证：

| 验证 | 实测 | 结论 |
|---|---|---|
| 边框像素 alpha 众数 | **202** | 与算术预测 202 误差 **0** |
| `a==202` 在 `hq`（判为"完整"）里的分布 | **382 px，100% 落在前景交界带，前景内部 0 px** | 是羽化边缘，不是实心度 |
| `a==202` 在 8 张图里的分布 | atcannon/radar/bazooka/tank/heavytank/warfactory/rifleman **全部 100% 在交界带** | 系统性产物 |

**所以 `alpha 171–202` 的含义是"这个像素紧贴画布边界、且被判成前景"**，它证明的是**触边**（touching），不是**被切**（cut）。任何一张主体画满到边界的图都会有这个值——包括 `hq` 和 `bazooka`，它们的剪影是完整的。

### 14.2 替代判据（三个，全部在原图上跑）

原图 `alpha` 恒 255（§3 已证），所以原图上的亮度剖面**完全免疫羽化伪影**。

| 代号 | 判据 | 含义 | 阈值 |
|---|---|---|---|
| **T1 触边数** | 边界像素 `luma < 200` 的个数 | 有多少结构压在边界上（背景实测 245–255） | 仅作参考 |
| **T2 切面段** | 边界上「连通段长 ≥ 20 px **且** 向内 20 px 仍是 `luma < 170` 结构」 | **被画布切平的硬证据**：横着撞出去，不是自然收口 | ≥ 20 px 才算 |
| **T3 屏显像素** | `T2 段长 × (1.8 / PPU)` | 屏幕上能看见的**直线断面**有多长 | ≥ 20 屏 px = 重 |

`T3` 是把"源图占多少比例"换成"玩家能看见多少缺陷"——`1 世界单位 = 1.8 屏幕 px`（§1.1 推导），`1 源 px = 1.8/PPU 屏 px`。同样 20 源 px 在 `rifleman`（PPU 17.07）只有 2.1 屏 px，在 `warfactory`（PPU 4.57）有 7.9 屏 px。**必须先做这个换算再排优先级**，否则会把小人物的 1 px 毛边和建筑的半面墙并列。

### 14.3 定稿表：13 张单位的边界裁切实测

| 资产 | PPU | 左 T1/T2/T3 | 右 T1/T2/T3 | 上/下 T2 | 判定 |
|---|---|---|---|---|---|
| hq | 3.20 | 0 / 0 / 0 | 10 / 0 / 0 | 0 / 0 | **完整** |
| powerplant | 5.75 | 0 / 0 / 0 | 0 / 0 / 0 | 0 / 0 | **完整** |
| refinery | 4.57 | 3 / 0 / 0 | 4 / 0 / 0 | 0 / 0 | **完整** |
| barracks | 5.33 | 17 / 0 / 0 | 10 / 0 / 0 | 0 / 0 | **完整** |
| **warfactory** | 4.57 | 100 / **31** / 12.2 | 123 / **122** / **48.1** | 25 | **★★ 重 48 屏 px** |
| radar | 5.45 | 0 / 0 / 0 | 0 / 0 / 0 | 0 / 0 | **完整** |
| mgtower | 8.00 | 13 / 0 / 0 | 17 / 0 / 0 | 0 / 0 | **完整** |
| atcannon | 6.92 | 0 / 0 / 0 | 0 / 0 / 0 | 0 / 0 | **完整** |
| rifleman | 17.07 | 29 / 0 / 0 | 30 / 0 / 0 | 29 | **轻 3 屏 px** |
| bazooka | 17.07 | 0 / 0 / 0 | 0 / 0 / 0 | 0 | **完整** |
| tank | 9.14 | 11 / 0 / 0 | 0 / 0 / 0 | 0 | **完整** |
| heavytank | 8.00 | 25 / 0 / 0 | 16 / 0 / 0 | 0 | **完整** |
| harvester | 9.14 | 32 / **24** / 4.7 | 2 / 0 / 0 | 0 | **轻 5 屏 px** |

**只有 warfactory 是真问题**（右边界 122 源 px 直线断面 = 48 屏 px），`harvester` 4.7 屏 px 属于"缩到 144 显示档后看不出来"，其余 11 张完整。

### 14.4 与 animation-director v1.5 §13.3.2 逐张对照

他们判"有主体伸出画布"的是 **6 张**（其余 7 张判 `0` 完整）。逐张对照如下：

| 资产 | 他们判 | 他们给的数（左/右/上） | 我实测 T2 | 我的判定 | 差异说明 |
|---|---|---|---|---|---|
| **warfactory** | **★ 最严重** | 105 / 124 / 37 | **122**（右） | **★★ 重 48 屏 px** | **一致，且比他们说的还严重**：右边界 122 px 连通段向内 20 px 仍是 `luma<170` 结构，是硬切面 |
| **harvester** | 高 | 36 / 8 / — | **24**（左） | **轻 4.7 屏 px** | 方向一致，**严重度下调**：PPU 9.14 折算后仅 4.7 屏 px，且它不旋转（`spriteForwardDeg=0`），断面永不暴露 |
| **heavytank** | 高（"直接导致炮管测不出"） | 30 / 16 / — | **0** | **完整** | **不一致**：两端确实各自出画布，但缺口仅 0.4 / 0.9 屏 px；炮管测不出的真因见 §14.5 |
| **barracks** | 中 | 25 / 13 / — | **0** | **完整** | **不一致**：左右 17/10 个 `luma<200` 像素是软阴影，向内 3–5 px 即回 200+ |
| **tank** | 低（"炮管端只贴边 2–4px"） | 12 / 3 / — | **0** | **完整** | **不一致**：左边 11 px 是**炮管末端自然收尖**（内 20 px 处已 <20 px），不是切面 |
| **refinery** | 低 | 9 / 14 / — | **0** | **完整** | **不一致**：边界 9 px 在原图上 `luma = 189–255`，**是背景**；Keyed 里的 `alpha 159–202` 纯属羽化带 |
| hq / powerplant / radar / mgtower / atcannon / rifleman / bazooka | 0 完整 | 0 | **0** | **完整** | **一致**（rifleman 上边界有 29 px 段，但 PPU 17.07 → 3 屏 px，判轻） |

**汇总：他们 6 张"被切"里，实际只有 2 张（warfactory、harvester）有切面，4 张（heavytank、barracks、tank、refinery）是触边。** 判据差异全部源于 alpha 伪影——`alpha 171–202` 只说明"该像素紧贴画布边界且被判成前景"，`hq` 这种剪影完整的图在同位置也有 382 个 `a==202`。


### 14.5 heavytank 炮管测不出的真因：不是被切，是没有

这一条我们俩的**结论相反**，我把证据全列出来供裁。

**他们的判断**：13° 与 199° 两端都被裁切，所以"哪端是炮口"的信息在图片里缺失。

**我的实测**：两端都被裁切**这一点成立**（13° 端出画布前最后像素 `luma = 13`，199° 端 `luma = 5`，都未回到背景白），但**它不构成"炮管测不出"的原因**——因为 heavytank **根本没有可几何识别的炮管**：

| 判据 | tank | heavytank |
|---|---|---|
| 最细外向突起（`r∈[100,128]`，`宽≤32px`） | **角 331°，宽 17.0 px** | **无任何方向** |
| 放宽到 `宽≤64px` | 角 331° | **仍无任何方向** |
| 「炮管」定义 = 台阶后长 ≥18 px 且宽 ≤25 px 的平行突起 | **角 331°，长 38 px，宽 18 px** | **不存在** |
| 远端细结构（开运算 r=12 残差）峰值角 | 330–345°（477 px）+ 315–330°（439 px） | **225–240°（155 px）/ 255–270°（81 px）**，即**车尾方向** |

**heavytank 的轮廓里没有任何一段"细且平行"的杆状结构**，最细方向垂宽 63 px（对比 tank 的 17 px）。它两端伸出去的是**粗壮的履带/车体端**（垂宽 89→55 单调收窄），不是炮管。

**所以四种答案的真因不是裁切，是"这张图没有炮管特征"**：任何径向/PCA 算法在 heavytank 上只能找到"车体长轴两端"，而长轴两端在拓扑上等价，符号（哪端朝前）**在几何上不可解**——这跟我 §11 说的"PCA 主轴不辨车头"是同一件事，只是这次连"炮管"这个更弱的锚点也不存在。

### 14.6 heavytank 朝向值：我同意降级为"目视暂定"，但补一条可交叉验证的替代锚点

我接受他们 §20.1 的验收 D 流程（人眼找锥形收窄 + 炮口环，右下保 `-20f`，左上改 `160f`）。补两条实测供人眼参考：

1. **两端"被切掉多少"实测**：13° 端末段宽度斜率 −4.0 px/px，还需 1.6 px 收敛到 0（= **0.2 世界单位 / 0.4 屏 px**）；199° 端斜率 −1.5 px/px，还需 4.0 px（= **0.5 世界单位 / 0.9 屏 px**）。**两端缺口都不到 1 屏 px**——所以"两端被切"对人眼判别的干扰极小，真正的问题是"没有炮管"。
2. **替代锚点：车尾高射机枪**。§11.2 原表记过 `121° 比 301° = 1.37`，我当时注明"121° 是车尾高射机枪方向，非炮管"。这条仍在：**高射机枪在车尾**，所以它的反方向就是车头。121° 折算世界角 = **−121°（左下）**，其反方向 **+59°（右上）** 是车头。**若炮管在车头侧，则炮管应朝右上（+59°），与 −20°（右下）矛盾。** 请人眼确认时把这条作为第二判据——如果图上能看到高射机枪/天线/格栅等车尾特征，用"反方向"定车头比"找炮管"可靠。

**若两条都确认不了**：`-20f` 保持现状，但**在 `PhaseBBootstrap.cs` 加一行注释标 `TODO 目视未确认`**，不要让它看起来像测出来的值。这个字符串本身不影响运行，但能让后来人知道该怀疑它。

### 14.7 顺带：`smoke_column` 与 `nuke_cloud` 的边界（非单位，一并测）

| 资产 | 边界 T1 | T2 | 说明 |
|---|---|---|---|
| smoke_column | 上 31 / 下 46 | 0 / 0 | 烟柱上下都自然收口，只是烟稀薄处被键控打到 0 |
| nuke_cloud | 上 34 / 下 86 | 0 / 0 | 蘑菇云底部贴边，属构图，不是裁切 |
| muzzle_flash | 上 16 | 0 | 闪光边缘，正常 |

这三张**不需要返修**。粒子类素材本来就允许贴边（它们是"无限延伸"的视觉元素，不靠完整轮廓辨识）。

### 14.8 返修建议（按屏幕可见度排序，不是我拍脑袋）

| 优先级 | 资产 | 理由 | 建议 |
|---|---|---|---|
| **P0** | **warfactory** | 右边界 122 px 直线断面 = **48 屏 px**，建筑是玩家最常盯着看的一类 | **重出**，主体宽度需补 116 px（按 `luma<170` 结构斜率外推：右边界宽 122 px、斜率 −1.07 px/px → 需 116 px 才收到 0）。相当于画布横向扩到 **372 px** 或主体缩到 69% |
| **P1** | heavytank | 不是裁切问题，是**缺炮管特征** → 朝向值永久不可测 | 若阶段 C 要自动化朝向，**重出时给炮管加长 ≥30% 并留 8% 边距**；若不重出，接受 `-20f` 目视值 + `TODO` 注释 |
| **P2** | harvester | 4.7 屏 px 断面，且 `spriteForwardDeg = 0`（不旋转），**转向时也不会露出断面** | **不返修** |
| — | 其余 10 张 | T2 = 0 | **不返修**，导出时留 8% 安全边距即可 |

**8% 安全边距够不够，逐张算过**：`needed = W(边界) / |dW/dx| + 2px`

| 资产 | 需要补边距 | 8% = 20 px 够吗 |
|---|---|---|
| warfactory | **116 px**（右）+ 20 px（左） | **★ 不够** |
| harvester | 22 px（左） | 差 2 px（可忽略） |
| heavytank | 8 px | 够 |
| barracks / mgtower | 12 / 10 px | 够 |
| tank / refinery / hq / atcannon / radar / powerplant | ≤ 7 px | 够 |
