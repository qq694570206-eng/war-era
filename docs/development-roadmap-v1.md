# 钢铁防线 — Unity 正式开发路线图

- 版本：v1
- 状态：**当前有效**
- 日期：2026-11-03
- 基于：设计案 v2（多背景架构）+ Unity 工程源码（22 个 C# 脚本）

---

## 当前已完成

| 模块 | 状态 | 交付物 |
|---|---|---|
| 设计文档 | ✅ 完成 | 8 份方向文档（设计/技术/美术/动画/特效/音频） |
| 多背景架构设计 | ✅ 完成 | 5 背景可选（冷兵器/一战/二战/二战后/现代） |
| 网页 Demo | ✅ 完成 | v0.4 矢量绘制版，验证核心循环 |
| Unity 工程代码 | ✅ 完成 | 22 个 C# 脚本（核心系统全部就位） |
| AI 生成素材 | ✅ 完成 | 12 张等距视角 PNG（二战背景） |

## 当前未完成

| 模块 | 缺什么 | 阻塞程度 |
|---|---|---|
| Unity 场景 | 没有场景文件、没有 GameObject 挂载 | 🔴 必须 |
| ScriptableObject 数据 | 15 个 UnitDefinition 资产未创建 | 🔴 必须 |
| Prefab | 没有单位/建筑预制体 | 🔴 必须 |
| 美术资产 | 没有正式的 Unity 美术资产（模型/材质/动画） | 🟡 阶段2 可占位 |
| 音频资产 | 没有 AudioClip | 🟡 阶段2 可占位 |
| UI Canvas | 没有 UI 布局文件 | 🔴 必须 |
| 编译验证 | 脚本未经 Unity 编译器验证 | 🔴 必须 |

---

## 开发路线图

### 阶段 A：工程能跑（Unity 打开零报错）

**目标**：Unity 打开工程，所有脚本编译通过，能进入编辑模式。

| 步 | 做什么 | 验收 |
|---|---|---|
| A1 | Unity 打开 unity-project，修复编译错误 | Console 零报错 |
| A2 | 创建 ScriptableObject 数据资产（15 个 UnitDefinition + DamageMatrix + 2 个 FactionDefinition + 4 个 TechUpgrade） | 数据资产在 Project 窗口可见 |
| A3 | 创建空场景 Battle.unity，添加摄像机+GameManager | 场景能打开不报错 |
| A4 | 挂载所有 Manager 脚本到 GameManager GameObject | Inspector 中各 Manager 字段可见 |

**预计工作量**：1-2 小时（取决于编译错误数量）

### 阶段 B：可玩骨架（方块占位能跑通核心循环）

**目标**：用 Cube/Capsule 占位，能跑通「采矿→建厂→出兵→交战→摧毁 HQ」。

| 步 | 做什么 | 验收 |
|---|---|---|
| B1 | 制作 15 个占位 Prefab（Cube=建筑、Capsule=步兵、Cylinder=载具） | Prefab 列表完整 |
| B2 | UnitDefinition 关联 Prefab | 每个数据资产的 prefab 字段非空 |
| B3 | 正交摄像机配置 + 世界边界 | 镜头不超出地图 |
| B4 | 建造面板 UI（按钮网格 + 资源条 + 小地图） | UI 在屏幕上可见 |
| B5 | 触屏输入跑通（点选/框选/移动/攻击/建造） | 点单位能选中，点地面能移动 |
| B6 | 采矿车自动往返矿点→矿场 | 矿石数字增长 |
| B7 | 兵营出兵/车厂出坦克 | 单位从建筑旁生成 |
| B8 | 战斗：单位自动开火+伤害结算+死亡 | 单位能打死对方 |
| B9 | 敌方 AI 三波递进出兵 | 敌人在 15/35/60 秒出兵 |
| B10 | 胜负判定：摧毁 HQ = 胜利/失败 | 游戏能结束 |

**预计工作量**：2-3 天

### 阶段 C：系统完善（补齐网页 Demo 有但 Unity 还没有的功能）

| 步 | 做什么 | 验收 |
|---|---|---|
| C1 | 编队系统（5 组 + 长按编入 + 点击选中 + 镜头跳转） | 编队能用 |
| C2 | 科技树 UI 面板（T2 解锁 + 装甲/火力/机动升级） | 科技能升级 |
| C3 | 超武系统完整（雷达站→充能→瞄准→核弹→爆炸特效） | 核弹能炸 |
| C4 | 战争迷雾渲染（未探索=黑/已探索=半暗/可见=透明） | 迷雾可见 |
| C5 | 集结点系统（建筑选中→设集结点→单位自动走过去） | 集结点能用 |
| C6 | 小地图渲染（实体点 + 摄像机框 + 迷雾覆盖） | 小地图有内容 |
| C7 | 音效接入（AudioClip + 分类播放 + 距离衰减） | 有声音 |
| C8 | 多地图支持（3 张地图：平原/城市/十字路口 + 障碍物） | 能选地图 |
| C9 | 阵营选择 + AI 难度（主菜单→选阵营/难度/地图→开始） | 主菜单能用 |

**预计工作量**：3-5 天

### 阶段 D：美术资产接入（从占位到正式视觉）

| 步 | 做什么 | 验收 |
|---|---|---|
| D1 | 2D 精灵图制作/获取（每单位 8 方向 × 多帧行走/开火/死亡） | 精灵图就位 |
| D2 | 建筑精灵图（等距视角 + 建造进度帧） | 建筑图就位 |
| D3 | 地形 Tileset（草地/城市/沙漠 + 道路/沙袋/废墟装饰） | 地形有纹理 |
| D4 | UI 图标（建筑/单位头像 + 按钮样式） | UI 不再是文字 |
| D5 | 特效精灵图/粒子（爆炸/枪口闪光/核弹序列） | 特效有图 |
| D6 | 动画控制器（Animator 状态机：Idle/Move/Attack/Death） | 单位有帧动画 |
| D7 | 单位朝向系统（8 方向精灵切换 或 单图旋转） | 单位面向正确 |

**美术资产来源选项**：
- ① 网上免费/付费素材包（Unity Asset Store / itch.io）
- ② AI 生成多帧精灵图（质量不可控，需要大量修整）
- ③ 像素风格（64×64 精灵，更容易做出精致感）
- ④ 3D 低多边形模型（更现代，但风格偏离红警）

**预计工作量**：取决于美术来源，1-2 周

### 阶段 E：移动端适配与优化

| 步 | 做什么 | 验收 |
|---|---|---|
| E1 | 竖屏/横屏自适应布局 | 两种方向 UI 不遮挡 |
| E2 | 安卓打包测试（APK 能装能跑） | 手机上能玩 |
| E3 | iOS 打包测试（Xcode 签名+真机调试） | iPhone 上能玩 |
| E4 | 性能优化（对象池验证 + Draw Call 合并 + LOD） | 低端机 30fps |
| E5 | 触屏手感调优（判定半径/框选灵敏度/镜头速度） | 操作不卡手 |
| E6 | 包体控制（IL2CPP + Stripping + ASTC 纹理） | 首包 ≤ 200MB |

**预计工作量**：1-2 周

### 阶段 F：内容扩展（多背景包）

| 步 | 做什么 | 验收 |
|---|---|---|
| F1 | 数据驱动验证（换一个背景包数据，代码不改） | 冷兵器背景能跑 |
| F2 | 冷兵器背景包（阵营/兵种/建筑/美术/音效） | 可玩 |
| F3 | 一战背景包 | 可玩 |
| F4 | 二战后局部战争背景包 | 可玩 |
| F5 | 现代战争背景包 | 可玩 |
| F6 | Addressables 分包（按需下载背景包） | 切换背景不卡 |

**预计工作量**：每背景包 3-5 天

### 阶段 G：发布准备

| 步 | 做什么 | 验收 |
|---|---|---|
| G1 | QA 全量测试（用例覆盖 + 缺陷修复） | 无阻断缺陷 |
| G2 | 应用商店资质（开发者账号 + 隐私政策 + 分级） | 资质就位 |
| G3 | README / 操作说明 / 已知问题 | 文档完整 |
| G4 | 发布前检查清单逐项过 | 全部 ✅ |
| G5 | 用户自行提交商店审核 | — |

---

## 优先级排序

**做完阶段 A-B 就有一个能在手机上跑的 Demo**——用方块占位但核心玩法完整。
**做完阶段 C-D 就有一个视觉接近红警的可玩游戏**。
**做完阶段 E 就能发到手机上给朋友试玩**。

| 优先级 | 阶段 | 做完后的状态 |
|---|---|---|
| P0 | A + B | 工程能跑 + 核心循环可玩（方块占位） |
| P1 | C | 系统完善（编队/科技/超武/迷雾/音效/多地图） |
| P2 | D | 美术接入（从方块到红警级视觉） |
| P3 | E | 移动端适配（手机能玩 + 性能达标） |
| P4 | F | 多背景包扩展 |
| P5 | G | 发布准备 |

---

## 当前阻塞项

| 阻塞 | 影响 | 解决方式 |
|---|---|---|
| Unity 编辑器下载中 | 无法编译验证脚本 | 等下载完成 |
| 没有 Unity 场景文件 | 工程打开后是空的 | 阶段 A3 创建 |
| 没有 ScriptableObject 资产 | 代码引用的数据为空 | 阶段 A2 手动创建 |
| 没有正式美术资产 | 视觉是方块占位 | 阶段 D 解决（来源待定） |

---

## 阶段 A 的详细执行清单（Unity 装好后立即执行）

### A1: 修复编译错误
1. Unity 打开 unity-project
2. 看 Console 窗口的红色报错
3. 逐个修复（常见问题：缺少 using、API 版本差异、字段未初始化）
4. 直到 Console 只有黄色警告或无信息

### A2: 创建数据资产
在 Project 窗口右键 → Create → IronFrontline → 对应类型：

**UnitDefinition（15 个）**，放在 `Assets/_Project/Data/Theme_WW2/`：

| 文件名 | unitId | displayName | category | hp | damage | range | cost |
|---|---|---|---|---|---|---|---|
| UnitDef_HQ | hq | 指挥中心 | Building | 2500 | 0 | 0 | 0 |
| UnitDef_PowerPlant | powerplant | 电厂 | Building | 400 | 0 | 0 | 300 |
| UnitDef_Refinery | refinery | 矿场 | Building | 500 | 0 | 0 | 500 |
| UnitDef_Barracks | barracks | 兵营 | Building | 600 | 0 | 0 | 400 |
| UnitDef_WarFactory | warfactory | 车厂 | Building | 800 | 0 | 0 | 800 |
| UnitDef_Radar | radar | 雷达站 | Building | 450 | 0 | 350 | 600 |
| UnitDef_MGTower | mgtower | 机枪塔 | Defense | 350 | 15 | 130 | 350 |
| UnitDef_ATCannon | atcannon | 反坦炮 | Defense | 300 | 45 | 140 | 500 |
| UnitDef_Rifleman | rifleman | 步枪兵 | Infantry | 100 | 12 | 75 | 150 |
| UnitDef_Bazooka | bazooka | 反坦兵 | Infantry | 90 | 35 | 80 | 300 |
| UnitDef_Sniper | sniper | 狙击手 | Infantry | 80 | 80 | 120 | 650 |
| UnitDef_Tank | tank | 中型坦克 | Vehicle | 550 | 55 | 100 | 900 |
| UnitDef_HeavyTank | heavytank | 重型坦克 | Vehicle | 900 | 75 | 105 | 1300 |
| UnitDef_Artillery | artillery | 自行火炮 | Vehicle | 350 | 90 | 180 | 1200 |
| UnitDef_Harvester | harvester | 采矿车 | Harvester | 250 | 0 | 0 | 0 |

**DamageMatrix（1 个）**：放在 `Assets/_Project/Data/Core/`

**FactionDefinition（2 个）**：
| 文件名 | factionId | speedMul | costMul | rangeMul | hpMul |
|---|---|---|---|---|---|
| FactionDef_Allies | allies | 1.15 | 1.2 | 1.1 | 1.0 |
| FactionDef_Axis | axis | 1.0 | 0.85 | 1.0 | 1.2 |

**TechUpgrade（4 个）**：
| 文件名 | upgradeId | displayName | techCost | 效果 |
|---|---|---|---|---|
| Tech_T2 | t2 | T2 科技解锁 | 3 | unlocksTechLevel2=true |
| Tech_Armor | armor | 装甲强化 | 5 | hpMul=1.15 |
| Tech_Firepower | firepower | 火力升级 | 5 | dmgMul=1.2 |
| Tech_Speed | speed | 机动强化 | 4 | speedMul=1.1 |

### A3: 创建场景
1. File → New Scene → Empty
2. 保存为 `Assets/_Project/Scenes/Battle.unity`
3. 创建空 GameObject `Managers`，添加组件：
   - GameManager
   - GameClock
   - UnitRegistry
   - CombatSystem
   - EconomySystem
   - BuildSystem
   - TechSystem
   - EnemyAI
   - SuperWeaponSystem
   - FogOfWarSystem
4. 创建空 GameObject `Input`，添加 TouchInputSystem
5. 创建空 GameObject `UI`，添加 UIManager + VFXManager + AudioManager
6. 创建 Camera，设为正交模式，Tag=MainCamera
7. 在 GameManager 的 Inspector 中关联所有数据资产引用

### A4: 验证
- Console 零报错 ✅
- Play 模式能进入不崩溃 ✅
- 场景中有摄像机 + Managers 对象 ✅

---

## 技术决策待定项

| # | 问题 | 选项 | 建议 |
|---|---|---|---|
| 1 | 2D 精灵 vs 3D 低多边形 | 2D 更接近红警；3D 更现代 | 先 2D（验证玩法），后续可换 3D |
| 2 | 像素风格 vs 写实风格 | 像素更容易做出精致感；写实更接近红警 | 取决于美术来源 |
| 3 | 美术来源 | Asset Store / AI 生成 / 像素手绘 / 外包 | 先 Asset Store 免费包占位 |
| 4 | 多人网络 | 单机先做 / 同步预留 | 单机先做，架构已预留 |
| 5 | 商业模式 | 买断 / 免费+内购 / 免费+广告 | 取决于市场定位 |
