---
name: unity-terrain-generation
description: Generate a large-scale Unity Terrain (heightmap-based) and place it in the scene. Use this skill — NOT unity-3d-model-generation — whenever the user describes a landscape, terrain, or landform environment, including canyon, valley, mountain range, hills, plains, plateau, island, volcano, desert dunes, coastal cliffs, or any terrain feature. Trigger examples — "帮我生成一个地形", "生成峡谷地形", "生成山地地貌", "侵蚀地貌", "冲积平原", "沉积岩", "create a canyon", "generate a valley", "make mountain terrain", "rocky landscape", "eroded cliffs", "floodplain terrain", "generate landscape". KEY RULE — if the request describes a large-scale ground/environment shape (not a standalone prop or object), always use this skill. By default, the AI agent automatically handles the full workflow (generate heightmap → apply to scene) without user intervention.
---

# 地形高度图与 Unity Terrain

按[共享执行约定](../../experience/templates/generator-async-pattern.md)等待各阶段。若已有可用高度图，直接从导入和应用步骤开始。

1. 通过 Generator MCP `generate_image` 请求正上方的灰度**高度**，白色高、黑色低，线性连续，无斜视、阴影、文字、色彩和透明背景。使用方形构图，`is_segmentation=false`，优先 PNG。图片尺寸决定地貌细节；地形占地由米数决定。
2. 等待同一生成任务并用 MCP `check_task` 获取高度图 URL。用 `import_image_from_url` 将其保存到项目的 Assets 内。通过 `query_local_task` 确认图片导入成功并取得 `image_path`。
3. 只需要高度图时交付上述文件。需要场景地形时调用 Unity `apply_terrain_heightmap`，传 `heightmap_path=image_path`、原始 `prompt`、会话 ID 和需要的 `terrain_go_name`。给出了较长边长度就用 `world_size_meters`；未指定时工具会从描述估算尺寸，交互编辑器可在估算失败后询问用户。
4. 保存返回的 `apply_task_id`。尺寸联网估算和后处理在后台执行；等待后调用 `query_local_task(task_type="terrain_apply", apply_task_id=...)`，检查生成的 TerrainData、场景 Terrain、长宽米数、山脊和谷底。相同高度图内容、场景、会话和参数的重试复用进行中或已完成的应用任务；仅明确需要另一份相同地形时提供新的 `application_id`，之后重试保留该值。MCP 的任务 ID 只用于 `check_task`。

显式 `world_size_meters=0` 表示 1 像素 = 1 米，不估算、不弹窗；只有省略该参数时才自动估算。估算失败后的输入取消或无交互环境会使已返回的 apply 任务失败（查询结果包含 `world_size_required`），可补充米数重试。域重载保留完成记录，未完成任务标记失败以允许重试。

## 后处理参数（仅 `use_default_options=False`）

> 默认：**原版亮度当高度**；多尺度压山体亮刺与暗坑（去蜂窝侵蚀洞），河谷走廊保留深度。默认开启 Median、热侵蚀、高斯（限幅后）、压尖刺；双边默认关。

| 参数 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `sketch_reconstruction` | bool | `true` | 开启多尺度浮雕限幅（压尖刺+填山体坑洞，保河道）；关闭则纯亮度当高度 |
| `sketch_detail_blend` | float | `0.25` | 亮刺保留幅度；越高越接近原版细纹，越低山体更圆；河道不受影响 |
| `sketch_structure_scale` | float | `0` | 中尺度尖刺波长（相对边长；0≈0.01，把细亮脊从基底拆出限幅） |
| `sketch_illumination_scale` | float | `0` | 保留字段，当前未使用 |
| `median3x3` | bool | `true` | 中位数滤波（去噪点） |
| `gaussian_blur` | bool | `true` | 高斯平滑（压尖刺开启时在限幅之后执行） |
| `gaussian_sigma` | float | `1.2` | 高斯 σ |
| `bilateral_filter` | bool | `false` | 保边平滑（仅 `sketch_reconstruction=false` 时生效） |
| `bilateral_sigma_space` | float | `2.5` | 双边空间 sigma |
| `bilateral_sigma_color` | float | `0.12` | 双边色 sigma（小 = 边更锐） |
| `thermal_erosion` | bool | `true` | 热侵蚀 |
| `thermal_erosion_iterations` | int | `8` | 越大越平滑 |
| `thermal_erosion_talus` | float | `0.022` | 滑坡角阈值 |
| `percentile_normalize` | bool | `true` | 百分位拉对比度（尖刺压制在其后） |
| `percentile_low` | float | `0.01` | 下限百分位 |
| `percentile_high` | float | `0.99` | 上限百分位 |
| `height_gamma` | float | `1.0` | < 1 = 山多；> 1 = 平 |
| `remap_output_min` | float | `0` | 输出下限 |
| `remap_output_max` | float | `1` | 输出上限 |

### 调参速查

| 场景 | 推荐参数 |
|---|---|
| 用户未说尺寸（岛/公园/农场等） | **省略** `world_size_meters`，工具 LLM 估算；失败则 Unity 弹窗让用户填 |
| 用户明确「200 米 / 2km」 | 传 `world_size_meters`，或把数字写进 prompt |
| apply 返回 `world_size_required` | 用户取消了尺寸弹窗 → 带 `world_size_meters` 重试 Stage 2 |
| 对比纯亮度（无尖刺压制） | `sketch_reconstruction=false` |
| 侧视更圆润 | `sketch_detail_blend=0.12` |
| 侧视更接近原版细纹（默认） | `sketch_detail_blend=0.25` |
| 山更夸张 | `height_gamma=0.8` |
| 更平、更像平原 | `height_gamma=1.2` |
| 抬高海平面 / 基准 | `remap_output_min=0.05` |
| 限制最高峰 | `remap_output_max=0.85` |
| 额外关闭热侵蚀 | `thermal_erosion=false` |
| 额外关闭高斯 | `gaussian_blur=false` |

> **面积 vs 分辨率**：`resolution` 只影响细节；世界占地靠 `world_size_meters` 或自动估算。不要靠降分辨率「变小」。

## Prompt 写作指南

| 地形类型 | Prompt |
|---|---|
| 山脉 | `"rugged mountain range with steep peaks and deep valleys, gradual foothills"` |
| 缓丘 | `"gently rolling grassy plains with subtle elevation changes and smooth curves"` |
| 岛屿 | `"tropical island with central mountain peak, flat coastal area, gradual slopes"` |
| 峡谷 | `"deep canyon with layered rock formations, wide rim, narrow river valley at bottom"` |
| 火山 | `"dormant volcano with central caldera, smooth sloped sides, flat ash plains"` |
| 沙丘 | `"sand dunes with smooth flowing curves, gradual ridges, no sharp edges"` |
| 海岸 | `"coastal terrain with high cliffs on one side, gradual inland slope"` |

技巧：
- **不要**写植被、建筑、树——它们会变成意外的高度凸起
- 加 `"smooth"` / `"gradual"` / `"gentle"` 让过渡自然
- 提供 `image_path` 参考图能**显著**改善形状准确度
- prompt 加 `"top-down orthographic view"` 暗示模型走 heightmap 风格
- 平坦区域：加 `"large flat areas"` / `"minimal relief"` / `"subtle elevation"`
