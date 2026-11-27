---
name: unity-sprite-sequence-generation
description: "Generate one continuous 2D animation per MCP generate_sprite_animation call: one transparent 4×4 sheet, 16 chronological frames, one Unity AnimationClip. Use for idle/walk/run/attack cycles and sprite effects. Independent actions or directional variants require separate calls, each with a full 16-frame sequence. Static images use generate_image; eight-direction sheets use generate_eight_dir_sprite. Import the completed sheet with import_sprite_sequence_from_url."
---

> **执行前读取**：[共享执行约定](../../experience/templates/generator-async-pattern.md)。
> 主 agent 按需委托 `general-purpose`；已在子代理中则直接执行本 skill，不再委托。已提交的阶段从原任务续接。

> ⛔ **场景放置规则**（本 skill **无 placeholder**）
> - **调用方式**：`activate_skill("unity-place-assets-in-scene")` → 按 §4g AnimationClip 模板用 `exec_editor_script` 建 `GameObject + SpriteRenderer + Animator + AnimatorController`（**不是** `execute_custom_tool`）。
> - **子代理**：等导入 `completed` 拿到 `animation_clip_path` 后**调一次**配 Animator + AnimationClip，**不要再调第二次**。
> - **主 agent**：报告里的 `animation_clip_path` 是"已放置"的证据，不是"请你放置"的指示，**不要再调**。
> - **例外**：用户明确要"再加一个实例"时才再次调用。详见 [共享执行约定](../../experience/templates/generator-async-pattern.md)。

# Generate 2D Sprite Sequence Animation in Unity 🎬 (MCP two-phase)

Generate 2D sprite sequence animations in Unity using AI.
Output：一张 **4×4 透明 PNG 精灵表** → 切成多帧 **Sprite PNG** + 一个 **AnimationClip (`.anim`)** 驱动 `SpriteRenderer.m_Sprite`，落在 `Assets/TJGenerators/History/`。

## 生成契约：一次调用只生成一段连续动画

- **16 格是同一动作的时间序列**：从左到右、从上到下连续播放，换行继续动作；行列不代表方向、动作类别或角色变体。用户要求、项目旧约定和参考图布局均不能改变这个契约。
- `prompt` 描述角色、风格、视角及一个连续动作，让工具安排帧位。禁止“每行一个朝向”“每个方向四帧”“第几行/第几格/第几帧画什么”等布局与逐格编排。可以描述“抬剑→挥砍→收势”等动作过程；连续转身、旋转也可以是一段动画。
- **四方向行走 = 四次生成**：分别描述朝下、朝右、朝上、朝左的完整行走循环，每次各产出一张 4×4 / 16 帧图，分别导入为四个 AnimationClip。多个独立动作也分别调用。复用角色外观与风格参考；不要用一张图的四行代替四个任务，也不要让服务端隐式扩成多个任务。按实际任务数量遵守现有费用与并发规则。
- 静态单图用 MCP `generate_image`；八向图用 `generate_eight_dir_sprite`，遵守其独立参数和流程，不把八向布局塞入本工具。参考图只提供角色与风格，不要求保留其中的多方向/多动作布局。
- 提交前由 Agent 理解需求并完成动作/方向拆分，再为每次调用编写符合契约的提示词；不删掉冲突要求后擅自减少用户所需方向。后端固定输出契约，对提示词仅做必填、类型与长度校验，不进行语义拦截，也不会自动拆分任务；不能把任务建单成功当作需求已符合契约的证明。

**两段式流程**：
- **第一段·生成（MCP 后端）**：`generate_sprite_animation` 提交 → 后台轮询 → `check_task` 拿 **4×4 透明 PNG sheet CDN URL**（永久链接；全能耀斑（`frontier_flare`）原生透明）
- **第二段·导入 + 切片 + AnimationClip（Unity CustomTool）**：`import_sprite_sequence_from_url` 下载 → `CutoutAndSlice(preserve_existing_alpha=true)` → 帧 PNG + `.anim` → `query_local_task` 拉结果

## 🚦 执行五步（不要跳读外链）

> ⚠️ **本 skill 无 placeholder**——导入完成前没有可放置的 AnimationClip。

1. 按生成契约确定本次只有一段连续动画，再调 MCP `generate_sprite_animation`（`prompt` 必填；参考图先 `file_upload` 拿 CDN URL 传 `image_urls`）→ 拿 `task_id`，在当前执行者内执行返回的轮询命令
2. 轮询结束 → `check_task` 一次 → sheet URL 在 `output.data.result.image_urls`（**数组**，取第一张；扩展名应为 `.png`）
3. `execute_custom_tool("import_sprite_sequence_from_url", {sheet_url, fps, loop, ...})` → 拿导入 `task_id`（**没有** `placeholder_path`）
4. **在当前子代理内前台等待（每次最多 30 秒）** → **不要结束任务**
5. 本次等待结束 → `query_local_task` **一次** → 读 `animation_clip_path` / `folder_path`；仍是 `importing`/`postprocessing` 则再前台短等待。确认 `completed` 后 → `activate_skill("unity-place-assets-in-scene")` 用 `exec_editor_script` 放置一次（资产类型 `AnimationClip`）

**档位**：第一段生成约 30–90 秒（按服务端间隔）；第二段导入+切片实测秒级～几十秒。

## 立绘到动画的串行流程

没有参考图时，在同一个执行者内加载 `unity-sprite-generation`，等待静态图完成后：对真实 `image_path` 做 `file_upload` → CDN URL 传给 `generate_sprite_animation` 的 `image_urls`，不能把占位图当输入。序列完成后把 AnimationClip 接到目标 Animator，并沿用静态精灵的 `localScale`。只要动画时复用同一角色；用户同时要独立立绘和动画展示时用不同名称分别放置，保持 scale 一致。

## ⚠️ Skill 独有约束

1. **`prompt` 永远 required**（MCP）——即使带参考图也必须写动作描述（如 "character idle breathing loop"）。
2. **本地参考图必须先 `file_upload`** 拿 CDN URL，禁止本地路径直传 MCP。
3. **网格固定 4×4 / 单段连续 16 帧**——MCP `generate_sprite_animation` 产出原生透明 sheet；导入使用 `cols=4, rows=4`，`preserve_existing_alpha=true`，全部帧组成一个 AnimationClip。不能改切片参数或自写按行播放器来掩盖生成契约冲突。
4. **旧 Unity `animation_type`（idle/frontRun/backRun）已退役**——动作意图写进 `prompt`（及可选参考图），不再传 `animation_type` / `generator_id`。
5. **🚫 不要再调** `generate_sprite_sequence` / `generate_frontier_sequence` / `generate_2d_sprite_sequence_*`（这些旧生成入口已移除）。
6. **资产类型 `AnimationClip`**——放置 skill 的模板会端到端建立 GameObject + SpriteRenderer + Animator + AnimatorController。

## 与 generate_sprite 的差异速查

| 维度 | 本 skill（序列帧） | `generate_sprite` |
|---|---|---|
| 生成入口 | MCP `generate_sprite_animation` | MCP `generate_image`（见静态 Sprite skill） |
| 导入入口 | `import_sprite_sequence_from_url` | `import_image_from_url(import_type="sprite")` |
| 输出 | 16 帧 PNG + 1 个 `.anim` | 1 张 PNG（Sprite） |
| Placeholder | **❌ 无** | ✅ 1×1 灰色 Sprite |
| 资产类型 | `AnimationClip` | `Sprite` |
| 场景结构 | GameObject + SpriteRenderer + **Animator + Controller** | GameObject + SpriteRenderer 或 UI Image |

## When to Use / NOT to Use

适用：2D 角色动画、帧动画、idle/run/walk/attack 动作、敌人 NPC 动效、像素角色循环动画、任意可用文字描述的 2D 序列动作。

不适用：
- 静态 2D 图标 / 道具 / 立绘 → `unity-sprite-generation` skill（MCP `generate_image`）
- 无参考图、用户也无法描述时 → 先按 `unity-sprite-generation` 出图再本 skill
- 3D 模型动画 → `unity-3d-model-generation`
- 天空盒 / 音乐 / 通用图 → 各自专属 skill

## 工具

### 第一段 · MCP `generate_sprite_animation`（直接调用）

```json
{
  "prompt": "cute cartoon fox, front-facing idle breathing loop, full body, game sprite",
  "image_urls": "https://.../hero_ref.png"
}
```

- `prompt` required；`image_urls` 可选（逗号分隔 CDN URL）
- 输出固定：全能耀斑（`frontier_flare`）、原生透明 PNG、4×4 / 16 帧
- 异步：提交拿 `task_id` + 轮询元数据 → 后台轮询 → `check_task` 一次取 sheet URL

### 第二段 · `import_sprite_sequence_from_url`（execute_custom_tool）

```python
execute_custom_tool(
  tool_name="import_sprite_sequence_from_url",
  parameters={
    "sheet_url": "https://.../sheet.png",  # check_task 拿到的永久 CDN URL（也可用 image_url）
    "fps": 12,                             # AnimationClip 帧率 1–60，默认 12
    "loop": True,                          # 默认 true
    # "cols": 4, "rows": 4,                # 本 MCP 产物固定 4×4，全部 16 帧组成一条动画
    # "preserve_existing_alpha": True,     # 默认 true（保留 Flare 原生透明）
    "prompt": "cute cartoon fox idle",     # 可选，History 显示
    "session_id": "..."
  })
```

返回：`task_id`、`status=importing`、`estimated_wait_seconds≈30`、`notification_mode=query_status`。**没有 `placeholder_path`**。

提交失败时 `result["success"] == false`，读 `message`，**不要** poll。

### `query_local_task`

每次等待结束调用**一次**；返回 `status`（importing/postprocessing/recovering/completed/failed）。完成时：

| 字段 | 说明 |
|---|---|
| `folder_path` | 帧 PNG 输出目录 |
| `frame_count` | 帧数（通常 16） |
| `animation_clip_path` | `.anim` 路径（**主要消费目标**） |
| `sheet_path` | 下载的精灵表路径 |
| `preview_url` | 源 CDN URL |

**不要无间隔连续查询。**

## 输出资产

| 资产 | 描述 |
|---|---|
| `ImportSpriteSequence/<url-hash>.png` | 下载的 4×4 sheet |
| `*_slices_*/frame_rXX_cYY.png` | 切出的帧 Sprite |
| `*_slices_*/sprite_sequence.anim` | AnimationClip，关键帧驱动 `SpriteRenderer.m_Sprite` |

## 参数速查

| 参数 | 阶段 | 默认 | 说明 |
|---|---|---|---|
| `prompt` | MCP | **required** | 动作描述（含角色外观若无参考图） |
| `image_urls` | MCP | 可选 | 参考图 CDN URL（逗号分隔） |
| `sheet_url` / `image_url` | 导入 | **required** | check_task 返回的 sheet URL |
| `fps` | 导入 | `12` | 帧率 1–60 |
| `loop` | 导入 | `true` | 循环或单次 |
| `cols` / `rows` | 导入 | `4` / `4` | 网格；MCP flare 固定 4×4 |
| `preserve_existing_alpha` | 导入 | `true` | 保留 PNG alpha；仅绿幕 sheet 才改 `false` |

### `fps` / `loop` 决策

| 用途 | FPS | Loop |
|---|---|---|
| 经典像素 / 复古 | 8–12 | idle/walk/run → `true` |
| 移动端流畅角色 | 12–18 | attack/jump/death → `false` |
| 高质量 / 特效 | 24–30+ | 按是否循环判断 |

## 使用示例

```python
# 1) MCP 生成 → task_id → 后台轮询 → check_task → sheet_url
# 2) 导入
result = execute_custom_tool("import_sprite_sequence_from_url", {
  "sheet_url": sheet_url,
  "fps": 12,
  "loop": True,
  "prompt": "cute cartoon fox idle",
  "session_id": "..."
})
import_task_id = result["task_id"]
# 前台等待最多 30 秒；在当前子代理内等待，不结束任务
# 3) 本次等待结束
status = execute_custom_tool("query_local_task", {"task_id": import_task_id})
# → animation_clip_path / folder_path
# 4) activate_skill("unity-place-assets-in-scene")，按 AnimationClip 模板放置一次
```

### 参考图驱动（立绘 → 动画）

```python
# 先 generate_sprite 得到 image_path，再：
# file_upload(image_path) → PUT → file_url
# MCP generate_sprite_animation({ prompt: "... idle loop ...", image_urls: file_url })
# → check_task → sheet_url → import_sprite_sequence_from_url → place AnimationClip
```

## 放入场景（导入 completed 后再做）

资产类型 **`AnimationClip`**，路径用 `animation_clip_path`。Scene side-effect：

1. 创建 GameObject + SpriteRenderer（若场景没有匹配名）
2. 创建 AnimatorController（保存到 `folder_path` 同目录）
3. 把 AnimationClip 设为 default state
4. 给 GameObject 加 Animator 并绑定 controller

## 故障排查

先区分请求契约、素材动作和播放接线。复用现有导入器。项目确需自定义切片时，逐帧核对矩形覆盖原格子的非透明像素，并在运行画面确认边缘与锚点。逐帧检查完整的 16 帧和跨行、循环接点。走路要在脚部可见的画面核对左右脚各自的着地与过渡，确认两侧轮流在前。若用户要求在游戏中播放，按共享约定查看实际 Game View 的一轮循环。帧号变化、bounds 和整图像素差用于定位，动作验收看脚和落地姿势。发现行与行分别对应独立朝向或动作时按契约不符处理。其它动作质量问题保留任务和产物，记录失败的方向、关键帧或视频时间点，按已有费用流程决定是否重做。

| 问题 | 原因 | 解决 |
|---|---|---|
| MCP 缺 `prompt` | 必填 | 即使有参考图也要写动作描述 |
| check_task 无 PNG URL | 未完成或格式异常 | 确认 status=completed；Flare 固定 PNG |
| `import_sprite_sequence_from_url` 报扩展名 | 非 png/jpg/jpeg | 只用 MCP sprite animation 返回的 PNG |
| query 返回 `recovering` | 域重载中断 | 再前台短等待；URL 永久、幂等重跑 |
| 导入 failed / Post-process failed | 下载或切片失败 | 读 `error`；可用同 `sheet_url` 重试导入 |
| AnimationClip 在但角色不动 | 缺 SpriteRenderer / Animator / Controller | 按放置 skill AnimationClip 模板建立完整结构 |

### Domain reload 后恢复

通用恢复见共享约定。查询原**导入** `task_id`（不是 MCP task_id）；核对 `folder_path` 帧数、Sprite 类型、`.anim` 曲线。目录存在不能单独证明成功。

---

**Task ID Format（导入）**：`import_sprite_seq_{counter}_{timestamp}`

**Notes**：
- MCP 生成 ID 用 `check_task`；Unity 导入 ID 用 `query_local_task`
- 帧 PNG 自动导入为 `TextureImporterType.Sprite`
- AnimationClip 关键帧驱动 `SpriteRenderer.m_Sprite`
- 自动应用 `TuanjieAI` 标签
- Editor 菜单/窗口的序列帧生成仍走原 GenerationService，不受本 MCP 迁移影响
