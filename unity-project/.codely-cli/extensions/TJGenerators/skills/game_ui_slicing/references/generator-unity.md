# Unity / Codely 生图衔接（本 extension）

切图脚本不调用生成后端。生图复用已有 MCP + Unity 导入，与 `unity-image-generation` 相同两段式。

**Agent 入口**：只使用父 skill `unity-image-generation`（单一流水线中的生产分件阶段）。本文件是资料，不是独立入口。

旧的 [generator-mcp.md](generator-mcp.md) 保留作纯 MCP 宿主参考；**在本包内以本文为准**。

## 生成单部件或小图册

1. 用 [prompts.md](prompts.md) 填好 prompt（血条等可变件禁止烤死进度；**禁止**口头「透明背景」）。
2. MCP `generate_image`：高细节美术可选 `provider=frontier_sunburst` + `output_format=png` + **`is_segmentation=true`**（全能日辉 + 智能抠图，对齐编辑器自动抠图）；用户点名其它模型则用指定值并报告实际模型。MCP 路径必须**显式**传 `is_segmentation`，不会因 png 自动打开。
3. `check_task` → 图片 CDN URL。
4. `execute_custom_tool("import_image_from_url", { image_url, prompt, output_path? })`。
5. 按共享约定等待后 `query_local_task` → `image_path`。
6. **inspect 验 alpha**：有透明 → regions `mode=alpha`；烘焙棋盘/全不透明 → 均匀键色底 + `is_segmentation=false` 重试一次，再切。
7. `slice_ui_sprites`：**inspect → slice → 填 layout（`border_ltrb` 必测，无九宫填 `[0,0,0,0]`）→ `qa`（必做）**。

参考图：先 `file_upload` 再把 `file_url` 传入 `image_urls`。可用 `slice_ui_sprites` 的 `command=stage_upload` 生成带哈希的副本路径再上传。

## 整页效果图（全能日辉，可选）

要布局参考时：MCP `generate_image` + **`frontier_sunburst`** + `png`，整页一般 `is_segmentation=false`。

## 截图分层

已有截图通过 MCP `generate_image_layers` 分层；具体参数及计费确认见[分层流程](../../generate_image_layers/SKILL.md)。用 Unity `import_image_from_url(import_type="sprite")` 导入可复用元素，保留实际顺序和布局信息，然后切片并运行 QA。
