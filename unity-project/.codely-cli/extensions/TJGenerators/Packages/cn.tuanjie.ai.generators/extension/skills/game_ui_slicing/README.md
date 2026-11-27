# Game UI 切图资料库（非独立入口）

本目录是 **[通用生图技能](../generate_image/SKILL.md#整套游戏-ui)单一流水线** 中「生产分件 / 实测切图 / 动态验收」所用的脚本与参考。

- Agent **使用** `unity-image-generation`
- **不要**把本目录当作独立 skill
- CustomTool `slice_ui_sprites` 读取本目录 `scripts/`

| 路径 | 内容 |
|---|---|
| [references/prompts.md](references/prompts.md) | 部件/图册提示词 |
| [references/dynamic-ui.md](references/dynamic-ui.md) | 血条等可变 UI |
| [references/commands-and-schema.md](references/commands-and-schema.md) | inspect/slice/qa |
| [references/generator-unity.md](references/generator-unity.md) | MCP 生图 + Unity 导入 |
| [references/troubleshooting.md](references/troubleshooting.md) | 排错 |
| [assets/](assets/) | brief / dynamic-ui 模板 |
| [scripts/](scripts/) | `ui_sprites.py` / `preview_dynamic_ui.py` |
| [requirements.txt](requirements.txt) | Pillow |

编排见通用生图技能的「整套游戏 UI」章节。**高细节美术可选全能日辉** `frontier_sunburst`（整页与分件；分件再加 `png`+`is_segmentation=true`；禁止 prompt 口头透明）；切完必须填 `border_ltrb`（无九宫填全零）并跑 `qa`。
