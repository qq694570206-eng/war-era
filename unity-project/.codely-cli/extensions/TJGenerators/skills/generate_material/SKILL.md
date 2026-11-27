---
name: unity-material-generation
description: Generate PBR surface material assets (seamless texture PNG + Unity .mat file) in Unity using AI via text prompts, reference images, or built-in texture pattern templates. Use this skill whenever the user wants to create a surface material for a 3D object in a Unity game — even if they just say "帮我生成一个金属材质", "给地面加个石头纹理", "make a wood texture for my floor", or "generate a brick wall material". Trigger proactively for any surface material or PBR texture creation request in a Unity context, including environment textures, object surfaces, architectural materials, or any tileable texture content for 3D objects. Do NOT use for 2D sprites, icons, or UI images — those belong to the unity-sprite-generation skill.
---

# 游戏表面材质

按[共享执行约定](../../experience/templates/generator-async-pattern.md)等待异步任务。

1. 调 Generator MCP `generate_material` 生成无缝表面纹理。用 `material_preset`、`texture_pattern`、`material_style` 和 `prompt` 表达材质与尺度，按实时 schema 选择尺寸。需要直接使用参考图时，可用 MCP `generate_image` 的参考图模式，说明平铺纹理和边缘连续的要求。
2. 记录生成任务 ID，按其轮询指令等待，用 MCP `check_task` 读取实际返回的贴图 URL。已有贴图直接从下一步继续。
3. 用 Unity `import_image_from_url` 导入普通 Texture2D，用 `query_local_task` 核对完成。将纹理设为 Repeat，在项目当前渲染管线下创建或复用 `.mat` 并绑定到材质底色。使用编辑器脚本操作资产；若没有独立的法线、金属度或粗糙度数据，仅设置可验证的材质参数。
4. 用户需要将材质放进场景时，按[放置技能](../place_assets_in_scene/SKILL.md)分配给目标 Renderer。检查实际纹理、材质路径、平铺接缝和目标物体。仅导入失败时，复用同一 URL。

## 材质类型预设 (`material_preset`)

> Preset 同时设置生成的 `.mat` 上的 `Metallic` / `Smoothness` 数值，无需手动调。

| ID | 名称 | 类别 | Metallic | Smoothness |
|----|------|------|----------|------------|
| `metal` | 金属 | 基础 | 0.9 | 0.8 |
| `wood` | 木头 | 基础 | 0 | 0.3 |
| `stone` | 石头 | 基础 | 0 | 0.1 |
| `fabric` | 布料 | 基础 | 0 | 0.3 |
| `leather` | 皮革 | 基础 | 0 | 0.3 |
| `concrete` | 混凝土 | 建筑 | 0 | 0.1 |
| `brick` | 砖块 | 建筑 | 0 | 0.1 |
| `tile` | 瓷砖 | 建筑 | 0 | 0.1 |
| `glass` | 玻璃 | 透明 | 0 | 0.95 |
| `ceramic` | 陶瓷 | 特殊 | 0 | 0.5 |
| `grass` | 草地 | 自然 | 0 | 0.5 |
| `sand` | 沙地 | 自然 | 0 | 0.5 |
| `snow` | 雪地 | 自然 | 0 | 0.5 |

## 纹理图案模板 (`texture_pattern`)


| ID | 名称 | 描述 | 类别 |
|----|------|------|------|
| `uniform` | 均匀平滑 | 无方向性平滑表面 | 基础 |
| `horizontal_lines` | 水平条纹 | 水平方向条纹 | 条纹 |
| `vertical_lines` | 垂直条纹 | 垂直方向条纹 | 条纹 |
| `cross_hatch` | 交叉网格 | 交叉网格纹理 | 网格 |
| `diagonal` | 对角线 | 对角方向纹理 | 条纹 |
| `wave` | 波浪 | 波浪形纹理 | 曲线 |
| `noise` | 噪点粗糙 | 随机噪点纹理 | 随机 |
| `honeycomb` | 蜂窝 | 六边形蜂窝纹理 | 几何 |
| `brick_layout` | 砖块排列 | 砖块排列纹理 | 几何 |
| `scales` | 鳞片 | 鳞片状纹理 | 有机 |
| `cracks` | 裂纹 | 裂纹纹理 | 破损 |
| `woven` | 编织 | 编织纹理 | 织物 |

## 表面状态风格 (`material_style`)

| ID | 名称 | 描述 |
|----|------|------|
| `new` | 崭新 | 干净、无磨损 |
| `aged` | 做旧 | 有划痕、磨损 |
| `dirty` | 脏污 | 有污渍、灰尘 |
| `wet` | 潮湿 | 有水渍、湿润 |
| `weathered` | 风化 | 自然风化效果 |

## Prompt 写作指南

`material_preset` + `material_style` 通常已经覆盖大部分语义。`prompt` 字段**只补充细节**。

| 目标 | Prompt |
|---|---|
| 指定颜色 | `"dark grey slate with blue-grey tones"` |
| 表面细节 | `"fine grain wood with visible knots"` |
| 图案提示 | `"large irregular cobblestone blocks"` |
| 强制 seamless | `"seamless tileable, no visible seams"` |
| 综合 | `"rusty iron, orange and brown tones, heavy corrosion"` |

技巧：
- 优先让 `material_preset` + `material_style` 工作；`prompt` 只补 preset/style 表达不出的细节
- 平铺表面要明确加 `"seamless tileable"`
- `texture_pattern` + `material_preset` 组合可同时控制结构与材质类型
- **不要描述物体**（如 "a sword"），只描述**表面本身**
