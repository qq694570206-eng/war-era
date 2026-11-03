# 战争纪元 (War Era) — Unity 工程

## 导入指南

### 1. 安装 Unity
- 下载 **Unity Hub**: https://unity.com/download
- 通过 Hub 安装 **Unity 6 LTS** (或 2022 LTS)
- 安装时勾选模块：**Build Support → Android** + **iOS**（按需）

### 2. 打开工程
- Unity Hub → Add Project → 选择 `unity-project/` 文件夹
- 首次打开会生成 `Library/`、`Temp/` 等缓存目录（不进 git）

### 3. 等待编译
- Unity 会自动编译所有 C# 脚本
- 如果有报错，检查是否遗漏了 ScriptableObject 数据资产（需要手动创建）

### 4. 创建必要资产
工程代码已就绪，但 ScriptableObject 数据资产需要手动创建：

**在 `Assets/_Project/Data/Theme_WW2/` 下创建：**
1. 右键 → Create → WarEra → Unit Definition（每个单位/建筑一个）
2. 右键 → Create → WarEra → Damage Matrix（一个）
3. 右键 → Create → WarEra → Faction Definition（两个：盟军+轴心）
4. 右键 → Create → WarEra → Tech Upgrade（4个升级项）

**单位定义清单（按设计案）：**
| unitId | displayName | category | HP | damage | range | cost |
|---|---|---|---|---|---|---|
| hq | 指挥中心 | Building | 2500 | — | — | 0 |
| powerplant | 电厂 | Building | 400 | — | — | 300 |
| refinery | 矿场 | Building | 500 | — | — | 500 |
| barracks | 兵营 | Building | 600 | — | — | 400 |
| warfactory | 车厂 | Building | 800 | — | — | 800 |
| radar | 雷达站 | Building | 450 | — | 350 | 600 |
| mgtower | 机枪塔 | Defense | 350 | 15 | 130 | 350 |
| atcannon | 反坦炮 | Defense | 300 | 45 | 140 | 500 |
| rifleman | 步枪兵 | Infantry | 100 | 12 | 75 | 150 |
| bazooka | 反坦兵 | Infantry | 90 | 35 | 80 | 300 |
| sniper | 狙击手 | Infantry | 80 | 80 | 120 | 650 |
| tank | 中型坦克 | Vehicle | 550 | 55 | 100 | 900 |
| heavytank | 重型坦克 | Vehicle | 900 | 75 | 105 | 1300 |
| artillery | 自行火炮 | Vehicle | 350 | 90 | 180 | 1200 |
| harvester | 采矿车 | Harvester | 250 | — | — | 0 |

### 5. 创建场景
1. 新建场景 `Assets/_Project/Scenes/Battle.unity`
2. 创建空 GameObject `GameManager`，挂载以下脚本：
   - GameManager
   - GameClock
   - UnitRegistry
   - CombatSystem
   - EconomySystem
   - BuildSystem
   - TechSystem
   - EnemyAI
   - TouchInputSystem
   - UIManager
   - VFXManager
   - AudioManager
3. 创建 UI Canvas，添加资源条/建造面板/单位信息卡等 UI 元素
4. 创建正交摄像机，Tag 设为 MainCamera

### 6. 制作 Prefab
每个 UnitDefinition 需要关联一个 Prefab（3D 模型或 2D 精灵）。
Demo 阶段可以用简单 Cube/Capsule 占位。

## 工程结构
```
Assets/_Project/
├── Scripts/
│   ├── Core/           # GameManager, GameClock
│   ├── Simulation/
│   │   ├── Units/      # UnitEntity, UnitRegistry, EnemyAI
│   │   ├── Combat/     # CombatSystem
│   │   └── Economy/    # EconomySystem, BuildSystem, TechSystem
│   ├── Presentation/   # VFXManager, AudioManager
│   ├── Input/          # TouchInputSystem
│   ├── UI/             # UIManager
│   └── Data/           # UnitDefinition, DamageMatrix, FactionDefinition
├── Data/               # ScriptableObject 数据资产（手动创建）
├── Art/                # 美术资产（模型/贴图/材质）
├── Audio/              # 音频资产
├── Prefabs/            # 预制体
└── Scenes/             # 场景
```

## 技术选型
- **Unity 6 LTS + URP**
- 正交摄像机（RTS 俯视）
- 数据驱动（ScriptableObject）
- 对象池（子弹/特效）
- 触屏输入（Input System 或旧 Input Manager）
