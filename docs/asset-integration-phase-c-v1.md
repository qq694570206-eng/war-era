# 阶段 C — AI 素材接入施工记录 v1

- 专业域：程序 / 资产接入
- 作者：主程序 寇豆码
- 上游规格：`docs/art-asset-integration-v1.md`、`docs/animation-facing-spec-v1.md`
- 适用范围：`unity-project/Assets/_Project` 工程；`assets/` 原图只读，未改动

---

## 1. 怎么跑

### 1.1 抠图（必须先跑，否则 Unity 侧拿不到 sprite）

```bash
cd /Users/lijinlei/geren_www/战争纪元
python3 tools/key_assets.py
```

依赖：`Pillow` + `numpy`（已装，未新增依赖）。输出 20 张 RGBA PNG 到
`unity-project/Assets/_Project/Art/Keyed/`，并写一份 `tools/keyed_report.json`。

可选参数：
- `--dry-run`：只核算不落盘
- `--only hq tank`：只处理指定资产（单张复查用）

脚本是纯确定性流程（无随机、无时间戳），重复执行输出逐字节一致。

### 1.2 Unity 侧

1. 用团结引擎打开 `unity-project/`
2. 菜单 **WarEra → 阶段B 一键搭建**
3. 打开 `Assets/_Project/Scenes/Battle.unity` 按 Play

一键搭建内部顺序（已改）：
`EnsureFolders → CreatePlaceholderSprites → ConfigureSpriteImporters → CreateFlashMaterial
→ CreatePrefabs → CreateFactionAssets → CreateUnitDefinitions → CreateMapAssets → BuildScene`

---

## 2. 怎么验

### 2.1 抠图产物（可脱离 Unity 复跑）

```bash
python3 tools/key_assets.py
```

预期尾部输出：

```
处理成功 20 张，失败 0 张
报告已写入：tools/keyed_report.json
```

逐张自检（mode / 四角 alpha）：

```bash
python3 -c "
import numpy as np
from PIL import Image
K='unity-project/Assets/_Project/Art/Keyed/'
for n in ['hq','tank','rifleman','harvester','mgtower']:
    im=Image.open(K+n+'.png')
    a=np.asarray(im.convert('RGBA'))[:,:,3]
    print(n, im.mode, [int(a[0,0]),int(a[0,-1]),int(a[-1,0]),int(a[-1,-1])])
"
```

预期：`mode == 'RGBA'`，四角 alpha 全 `0`。

### 2.2 白边（规格 §10 验收点 ③）

```bash
python3 -c "
import numpy as np
from PIL import Image
for n in ['bazooka','mgtower','tank','rifleman']:
    a=np.asarray(Image.open('unity-project/Assets/_Project/Art/Keyed/%s.png'%n).convert('RGBA')).astype(float)
    al=a[:,:,3]/255.0
    m=(al>0.05)&(al<=0.3)
    print(n,'羽化带 meanRGB=',a[:,:,:3][m].mean(axis=0).round(0))
"
```

预期：数值远低于 250（说明 bleed 生效）。若接近 `(250,250,250)` = 白边没去掉。

### 2.3 硬编码朝向偏移已清除

```bash
grep -rn "+ 90f\|+90f" --include="*.cs" unity-project/Assets/_Project/Scripts/
```

预期：无输出。

### 2.4 Unity 内目视（规格 §6 三条）

| 验收点 | 操作 | 判定 |
|---|---|---|
| ① 载具朝向 | 选 3 辆坦克，命令向右/上/左/下各移动一次 | 炮管始终朝**移动方向前方**；任何方向都不出现"炮管朝后" |
| ② 步兵不躺倒 | 命令步兵向四方向移动 | 人物始终直立；左右移动仅镜像；正上/正下观察 3 秒无翻转闪烁 |
| ③ 静动可分 | 静止坦克与移动坦克并排截图 | 移动有轻微颠簸，静止完全静止；**坦克无大幅弹跳** |
| 剪影测试 | 单位填纯黑后截图 | rifleman/bazooka/tank/heavytank 四者可区分 |
| 白边测试 | 截图放大 400% | 边缘无 1px 白/浅灰描边 |

---

## 3. 本轮实际改了什么

### P0-1 抠图脚本 `tools/key_assets.py`（新增）

按美术规格 §3 实现，逐条对应：

| 规格要求 | 实现 | 位置 |
|---|---|---|
| seed 用四边 2px 边缘环众数，不用四角点 | `ring_mode()` 取四边各 2 行/列拼接后 `Counter.most_common(1)` | `ring_mode` |
| tol 逐图指定（不手填、不统一 12） | `ASSETS` 表逐条写死：hq 23 / tank 12 / harvester 18 / rifleman 24 / mgtower 18 / bazooka 20 | `ASSETS` |
| mask 从四边 4-邻域 floodfill，禁止全图阈值 | `floodfill_bg()` 以四边为种子、栈式 4-邻域扩展 | `floodfill_bg` |
| hq/rifleman/bazooka/mgtower 做 3×3 开+闭运算 | `morph=True` 四张，`morph_open_close()` = 开1遍+闭1遍 | `morph_open_close` |
| 禁止填洞：explosion、harvester | **全流程无填洞步骤**；四边 floodfill 天然保留内部高光洞 | 无填洞代码 |
| 羽化半径 1px（高斯 σ=0.7） | 3×3 高斯核 σ=0.7，`feather()` | `feather` |
| 必须去白边 bleed：3 轮最近不透明像素扩散 | `bleed(rounds=3)`，**在羽化之前**调用 | `bleed` |
| smoke_column 亮度键控 α=clamp((203−lum)/203)，不羽化不 bleed | `key_luma()`，`LUMA_BG=203` | `key_luma` |
| explosion 逐帧独立边缘环 | `cells=(4,4)`，16 格各自 `key_floodfill` | `process_one` |
| 输出同名到 `Art/Keyed/` | `OUT_DIR = .../Art/Keyed/<name>.png` | 常量 |

**两个必须知道的实现细节（都是踩过的坑）：**

1. **bleed 必须在羽化之前**。先羽化的话，边缘像素 alpha 已变成 0.05~0.99，
   会被 bleed 判为"不透明"而跳过 → 白底 RGB 原样留在边缘，双线性采样后仍是 1px 白晕。
   实测反例：先羽化后 bleed 时羽化带 `meanRGB=(241,240,236)`；调序后降到 `(219,218,211)`。

2. **形态学的边界填充不能用 edge**。腐蚀时图像外必须视为背景（`constant=False`），
   否则边缘像素被自我复制、永远侵蚀不掉。实测反例：edge padding 时 mgtower 左上角
   残留 240px 前景（原图该处 rgb=(231,229,225)，距 seed 28 > tol 18，是噪点）。

另加一步规格未写但必须做的 **`drop_specks()` 连通域过滤**：3×3 开运算只能去掉 1-2px
孤立点，去不掉 20~160px 的噪点团。实测 mgtower 有 44 个触边碎块（最大 162px）、
rifleman 6 个（36px）、bazooka 16 个（78-87px）。不过滤则 bbox 被噪点撑满
（bazooka 正是因此 "Trim 结果不可信"）。阈值 `max(主块×0.004, 200px)`：
最小主体是 tank 24115px，远大于阈值；实测最大噪点团 162px < 200，全部清除。

### P0-2 导入参数（`PhaseBBootstrap.ConfigureSpriteImporters`）

| 项 | 值 |
|---|---|
| `spritePixelsPerUnit` | 逐张按裁定表（hq 3.20 … muzzle_flash 25.60） |
| `spriteAlignment` | `Custom`，`spritePivot` 按规格 §2 逐张 |
| `textureType` | `Sprite` |
| `alphaIsTransparency` | `true` |
| `filterMode` | `Bilinear` |
| `maxTextureSize` | `256` |
| `mipmapEnabled` | `false`（2D 正交，mipmap 只让远景变糊，规格 §7.1-3） |
| `spriteMeshType` | `FullRect`（不做 Tight，避免逐图网格差异） |
| explosion / resource_icons | `Multiple` + 按单元格切片（64×64 / 128×128） |

**Trim 的处理：本轮不做 Trim，也不需要做。**
实测裁定表的 PPU 等价于 `256 / 目标世界宽`（即 Sprite rect 保持整幅 256 时目标宽精确成立）：

| 资产 | 目标宽 | 256/PPU | 偏差 |
|---|---|---|---|
| hq | 80 | 80.00 | 0.0% |
| refinery | 56 | 56.02 | 0.0% |
| mgtower | 32 | 32.00 | 0.0% |
| tank | 28 | 28.01 | 0.0% |
| radar | 44 | 43.99 | −6.3%（主体 240px 未满框，视觉略小 6%，可接受） |
| rifleman | 8.67（人形） | 整幅 15.00 / 人形 8.6 | 人形 0.9%（人形只占 x63–210） |

rifleman 的 17.07 是"整幅 15 单位"校准值，而可见人形宽 147px → 8.6 单位，
与规格目标 8.67 差 0.9%，符合规格"按 bbox 定尺寸会让人缩到一半"的原意。
**因此无需手动 Trim，也就没有"需手动 Trim 的图"这一项遗留。**

### P0-3 三层预制体（`PhaseBBootstrap.MakeUnitPrefab`）

```
UnitPrefab / BuildingPrefab     (UnitEntity)                 根：位置由逻辑驱动，不旋转
 └ RecoilPivot                                              只承担后坐位移
    └ ArtRoot  (SpriteRenderer + UnitView)                   朝向 + 呼吸/抖动
```

- 占位 `localScale = 1.6` 已删除（美术规格 §0③）
- `ArtRoot.localScale` 恒为 1，尺寸全交给 PPU

`UnitView` 相应改造：
- `sr` 从 ArtRoot 取（自己节点）
- 朝向旋转写 `ArtRoot.localRotation`
- 后坐位移写 `RecoilPivot.localPosition`，且**世界方向先转父空间**
  （`parent.InverseTransformDirection(worldBack)`）

### P0-4 `spriteForwardDeg` + 公式替换

- `Data/UnitDefinition.cs` 新增 `public float spriteForwardDeg = 0f;`
- `UnitView`：`entity.facing * Mathf.Rad2Deg - def.spriteForwardDeg`（旧的 `+ 90f` 已删）
- 转向速率：履带 `540°/s`、轮式 `720°/s`（规格 §2.4）
- 逐张标定：tank `0`、heavytank `−15`、artillery `0`、harvester `−45`
- 步兵镜像带 `±0.15` 死区（`flipDeadZone`），正上/正下保持上一帧不切换
- 建筑 `localRotation = identity`，不旋转

### P0-5 `def.sprite` 接线（本轮最关键修复）

问题：`UnitRegistry.SpawnUnit` 只 `Instantiate(def.prefab)`，**从不读 `def.sprite`**，
所以素材放进工程后屏幕仍是占位方块 —— 典型的"目录满了但没接进游戏"。

修复三处：
1. `UnitEntity.ApplyDefinitionSprite()`：从 ArtRoot 的 SpriteRenderer 取引用，
   `sr.sprite = def.sprite` + `sr.color = white`；`def.sprite == null` 时保留占位并
   `Debug.LogWarning` 指明是哪个 unitId、该查哪个路径
2. `UnitEntity.Initialize()` 末尾调用 `ApplyDefinitionSprite()`
3. `PhaseBBootstrap.WireSprites()`：15 个 UnitDefinition 逐张
   `AssetDatabase.LoadAssetAtPath<Sprite>("Art/Keyed/<file>.png")`

映射表（含无素材的复用）：

| unitId | 贴图 |
|---|---|
| hq / powerplant / refinery / barracks / warfactory / radar / mgtower / atcannon | 同名 |
| rifleman / bazooka / tank / heavytank / harvester | 同名 |
| **sniper** | `rifleman`　`// TODO: 缺独立素材` |
| **artillery** | `tank`　`// TODO: 缺独立素材` |

### P0-6 受击闪白（选了方案①：自建 shader）

原因：`Sprites/Default` 没有 `_FlashAmount`，原代码
`spriteRenderer.material.HasProperty(FlashShaderParam)` 直接为 false，
**整段闪白静默失效**（不报错、没效果）。

为什么不用方案②（`sr.color`）：`SpriteRenderer.color` 是顶点色，与贴图做**乘算**。
白色 tint × 彩色贴图 = 原色，看不出闪白；想提亮需 >1，但顶点色按 `Color32` 存储会被
clamp 到 1，同样无效。"加色提亮"只能在 fragment 里做。

落地：
- 新增 `Assets/_Project/Shaders/SpriteFlash.shader`（`WarEra/SpriteFlash`），
  在 `Sprites/Default` 基础上只加两行：声明 `_FlashAmount`、`c.rgb = lerp(c.rgb, white, _FlashAmount)`
- 新增材质 `Assets/_Project/Shaders/SpriteFlash.mat`，单位/建筑 prefab 共用（保证合批）
- `_FlashAmount` 标了 `[PerRendererData]` —— **少这个标记，`MaterialPropertyBlock` 不生效，闪白静默失效**
- `UnitEntity.UpdateFlash()` 用 `MaterialPropertyBlock`（不产生材质实例、不破合批），
  且只在状态切换时写；若 `sharedMaterial` 没有该属性则退回 `sr.color` 提亮

### 附带修的两处（都由本次改动直接暴露）

1. **后坐只活 3 帧**：`recoil -= dt * 3f`（初值 0.15 → 0.05s = 3 帧）改为
   `recoilDecay = 1 / 持续时间`，逐武器配 0.08~0.26s，回弹曲线 `recoil²`。
2. **深度排序**：`ProjectSettings/TagManager.asset` 新增 7 个排序层
   （`Ground/GroundDeco/Ore/Shadow/WorldSprite/FX/FXAir`）；
   `UnitView.UpdateSorting()` 按 `(1600 − y) × 2` 设 order。
   `sortingLayerID` 与 `sortingOrder` 都只在变化时写（避免每帧 native 调用）。
   单位与建筑**同层同规则** —— 否则会出现"坦克钻进 HQ 后面还压着它"。

---

## 4. 已知未做（本轮明确排除）

| 项 | 状态 | 原因 / 下一步 |
|---|---|---|
| explosion 16 帧序列动画 | **未接线** | P1。`Art/Keyed/explosion.png` 已切成 16 个 sprite，但 `VFXManager.SpawnExplosion` 仍用占位圆。需换 `ParticleSystem + Texture Sheet Animation` 或 Animator，并把 `DeactivateAfter` 0.6 → 0.70（16帧@24fps = 0.667s，0.6 会截掉最后一帧） |
| muzzle_flash 真实贴图 | **未接线** | P1。sprite 已导入（PPU 25.60、pivot center），`MuzzleFlashPrefab` 仍是占位圆 |
| 地形铺设（grass/road/desert/snow） | **未接线** | P1。需先加 `Material terrainMaterial` 字段（美术规格 §7.4 明确这是要主程序加的）；`ThemeManager.terrainTile` 是 Sprite，不能平铺 |
| 3 张 UI 图 | **未接线** | P1。产物已在 `Art/Keyed/`（`build_panel_bg` 已 Crop 到 y40–215），导入参数已配好，未接 UI 容器 |
| nuke_cloud / smoke_column | **未接线** | P2。工程无核弹/烟雾调用点 |
| harvester 的 `spriteForwardDeg = −45` | **待目视复核** | 规格 §2.3 标注置信度"低"（剪影长宽比 1.02，PCA 不可靠）。代码已标 TODO |
| 步兵弹跳/呼吸参数 | 已按 §4.1 实现 | 幅度按贴图高比例，re-scale 后无需重调；实机手感仍需人验 |
| 扬尘（履带车尾 0.12s/撮） | **未做** | 规格 §4.2 需要 `VFXManager` 新接口；本轮未加 |
| `scaleY` 加减速压缩 | **未做** | 需要加速度信号（当前 `UnitEntity` 不记录 accel）；本轮未加 |
| 建筑 `rotZ = 4°·sin(facing)` 微转 | **未做** | 规格 §5.4 标注"可选"；本轮建筑保持完全不转 |
| 数值平衡 | **未动** | 按要求排除 |

---

## 5. 占位资产清单（搜 `PLACEHOLDER` 可定位）

代码中仍在使用占位图的点：

| 位置 | 占位内容 | 计划 |
|---|---|---|
| `PhaseBBootstrap.CreatePrefabs` → `MakeUnitPrefab` | `circleSprite` / `squareSprite` | 保留作为 `def.sprite` 为 null 时的回退，**不清除** |
| `PhaseBBootstrap.MakeFxPrefab` | 圆/方色块 | P1 换 explosion / muzzle_flash 真图 |
| `PhaseBBootstrap.CreatePlaceholderSprites` | 程序生成的圆/方 PNG | 长期保留（单位 prefab 的回退依赖它） |

`placehold_circle.png` / `placehold_square.png` 仍是 `spritePixelsPerUnit = 1.0`，
这是有意的：它们只是回退占位，尺寸不参与真图校准。

---

## 6. 改动文件清单

新增：
- `tools/key_assets.py`
- `tools/keyed_report.json`（脚本产物）
- `unity-project/Assets/_Project/Art/Keyed/*.png`（20 张）
- `unity-project/Assets/_Project/Shaders/SpriteFlash.shader`
- `unity-project/Assets/_Project/Shaders/SpriteFlash.mat`（由一键搭建生成）
- `docs/asset-integration-phase-c-v1.md`（本文件）

修改：
- `unity-project/Assets/_Project/Scripts/Data/UnitDefinition.cs`（+`spriteForwardDeg`）
- `unity-project/Assets/_Project/Scripts/Presentation/UnitView.cs`（三层职责 / 朝向公式 / 镜像死区 / 排序 / 后坐）
- `unity-project/Assets/_Project/Scripts/Simulation/Units/UnitEntity.cs`（`ApplyDefinitionSprite` / `UpdateFlash` / `recoilDecay`）
- `unity-project/Assets/_Project/Scripts/Simulation/Units/UnitRegistry.cs`（接线注释）
- `unity-project/Assets/_Project/Scripts/Editor/PhaseBBootstrap.cs`（导入参数 / 三层 prefab / `WireSprites` / 闪白材质 / 删 1.6 缩放）
- `unity-project/ProjectSettings/TagManager.asset`（+7 个排序层）

未改动：`assets/` 下全部原图。

---

## 7. 复跑注意

- **必须先跑 `key_assets.py`，再执行 Unity 一键搭建**。否则
  `ConfigureSpriteImporters` 会打印缺失清单、`WireSprites` 会让 `def.sprite` 为 null，
  游戏内回落成占位方块并刷 `Debug.LogWarning`。
- 一键搭建是**幂等**的：重复执行会覆盖重建 prefab / definition / scene。
  但 `SpriteFlash.mat` 若已存在则复用（不重建），只在 shader 变更时刷新。
- 改了 `key_assets.py` 里的 tol/pivot/ppu 之后，**必须重跑脚本 + 重跑一键搭建**，
  两者都改才生效。
