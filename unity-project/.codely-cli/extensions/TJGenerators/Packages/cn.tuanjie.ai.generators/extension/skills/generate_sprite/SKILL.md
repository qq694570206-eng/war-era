---
name: unity-sprite-generation
description: 在 Unity 中生成静态 Sprite、按钮、图标、道具或透明立绘。和普通 2D 图片共用 Generator MCP generate_image，Unity 负责 Sprite 导入和场景放置。
---

# 生成静态 Sprite

按[统一 2D 图片流程](../generate_image/SKILL.md)执行。按钮、图标和立绘各自描述主体、构图、留白、朝向和目标尺寸；参考图按该流程上传。需要透明底时给 MCP `generate_image` 传 `is_segmentation=true`，请求 PNG。按服务端返回的命令等待同一个任务，通过 `check_task` 取实际产物 URL。

将单图 URL 传给 Unity `import_image_from_url`，指定 `import_type="sprite"`。记录导入任务 ID 和占位路径。需要放置时按[场景放置](../place_assets_in_scene/SKILL.md)将同一 Sprite 赋给 `SpriteRenderer.sprite` 或 `UI.Image.sprite`。用 `query_local_task` 核验最终文件、透明边缘和 Sprite 引用。已有生成 URL 时直接导入。

整套 UI 截图及拆层按[游戏 UI Kit 流程](../generate_image/SKILL.md#整套游戏-ui)。帧动画按[序列帧契约与导入流程](../generate_sprite_sequence/SKILL.md)走 MCP `generate_sprite_animation`：一次一段连续动作的完整 4×4 / 16 帧，多方向版本分别调用，不按行分方向。八向图走 `generate_eight_dir_sprite`。
