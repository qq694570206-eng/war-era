---
name: unity-sound-effect-generation
description: "Generate sound effects (SFX) in Unity using AI via text descriptions, then trim/peak-finish before import. Use when the user wants one-shot sound effects such as gunshots, footsteps, explosions, UI clicks, item pickups, environmental sounds, etc. — e.g. \"生成枪声音效\", \"给按钮加个点击音效\", \"make a footstep sound\", \"generate explosion SFX\". DO NOT use for background music or looping ambient audio — use the generate_audio_clip skill instead. Flow: MCP generate_sound_effect + check_task → audio_finish oneshot (preserve complete events and tails; crop only verified event windows) → file_upload → import_audio_from_url."
---

> **执行前读取**：[共享执行约定](../../experience/templates/generator-async-pattern.md)。
> 主 agent 按需委托 `general-purpose`；已在子代理中则直接执行本 skill，不再委托。已提交的阶段从原任务续接。
> **委托时不要**把 `analyze_multimedia`（或其它视觉/多模态工具）写成必做听感关卡；`audio_finish` 的包络统计也不是语义事件次数。

> 整理脚本与参考：[`../game_audio_production/`](../game_audio_production/)。依赖 Python 3.10+、NumPy、ffmpeg/ffprobe。

# Generate Sound Effects (SFX) in Unity 🔊

生成 one-shot 音效，**默认经整理后再进 Unity**，保存为 AudioClip（通常在 `Assets/TJGenerators/History/ImportAudio/` 或用户指定路径）。

**流程**：
1. MCP `generate_sound_effect` → 轮询 → `check_task` → `output.data.audioUrl`
2. 下载原件 → `audio_finish.py` **oneshot** → 检查报告；仅在确认完整事件边界后用 `--start/--end` 重切 → `final.wav`
3. `file_upload(final.wav)` → `import_audio_from_url`（勿 `is_bgm`）→ `query_local_task`

仅当用户明确要求「原样 / 快速试听 / 不要后处理」时，跳过步骤 2，且 CDN URL 必须是 **wav/mp3** 才能直 import。

## 执行步骤

1. **生成**：MCP `generate_sound_effect`
   - `prompt`：中英皆可；写清声源、动作、次数、起音/尾音；建议含 "SFX" / "sound effect"。可参考 [prompts.md](../game_audio_production/references/prompts.md)。
   - `duration_seconds`：短 UI/攻击音显式 **1–3**；勿对短确认音默认 8 秒（接口默认 8）。范围 1–180。
   - `output_format`：**`wav`**（便于 finish）；跳过整理时也只用 wav/mp3。
   - 拿 `task_id`，按共享约定执行返回的轮询命令。
2. **取 URL**：轮询结束 → `check_task` 一次 → `output.data.audioUrl`。费用确认规则同前。
3. **整理（默认）**：见 [processing.md](../game_audio_production/references/processing.md)、[generator-unity.md](../game_audio_production/references/generator-unity.md)。
   ```text
   SKILL_DIR = …/extension/skills/game_audio_production
   # 下载 audioUrl → WORK/source.wav
   python "SKILL_DIR/scripts/audio_finish.py" inspect "WORK/source.wav" --out "WORK/inspect-v1"
   python "SKILL_DIR/scripts/audio_finish.py" oneshot "WORK/source.wav" --out "WORK/sfx-v1" --mono --sample-rate 48000 --peak-db -6 --leading-ms 8 --trailing-ms 60
   ```
   默认 oneshot 只裁静音，**不**提取语义事件。读 stdout / `report.json`：
   - `oneshot_review_recommended=true` 或 `transient_peak_count` **≥ 2** 只提示多个包络活动区，不证明有多个语义事件。一次爆炸、挥剑或提示音也可能有多个起音。**禁止仅凭计数或峰时间截短声音**，尤其不能固定取第一峰前后几十毫秒。
   - 只有可靠试听或用户给出的区间能确认多余事件及完整起止点时，才对原 `source.wav` 用 `--start/--end` 重切（新 `--out`），保留完整攻击、衰减和混响尾音。用户明确要连击/多步时保留所需事件。
   - 无可靠音频能力时保留仅去静音和电平整理的版本，标注「未试听，事件边界未确认」并继续导入。工具声称未收到音频时不要反复盲试；旧报告的 `oneshot_needs_event_window` 也不能作为强制裁剪依据。
   - 条件允许时简要听核起音/次数/尾音；脚本 `technical_result=passed` 不等于听感合格。保留原件与各版 report。
4. **上传 + 导入**：`file_upload` **最终选用**的 `final.wav` 绝对路径 → `file_url` →
   ```python
   execute_custom_tool("import_audio_from_url", {
     "audio_url": "<file_url>",
     "prompt": "...",
     "output_path": "...",                # 可选
     "play_on_awake": False
   })
   ```
5. **等待查询**：当前子代理内前台等待 ≤30s → `query_local_task` 一次 → 读 `audio_path`；仍 importing/recovering 则再短等。验证可加载为 AudioClip。

**档位**：生成约 10–60 秒；整理与导入多为秒级。

## 约束

1. **禁止**把未整理的 flac/aac CDN 丢给 import；跳过整理时也只许 wav/mp3。
2. **勿** `is_bgm: true`（会误建 BGMPlayer）。
3. 按秒计费；达阈值须用户明确同意后才 `confirm_cost=true`。
4. AudioSource：一次性 `loop=false`，默认 `playOnAwake=false`；仅用户要求接线时再改场景。
5. 缺 NumPy/ffmpeg 时报告缺项并停止（或征得同意后才走「原样 import」降级）。
6. **禁止**把视觉/不可靠多模态结论或包络计数当作事件次数验收；未经确认不得截短事件及尾音。

## 故障处理

| 症状 | 处理 |
|---|---|
| `check_task` 仍 pending | 同 task 再等，勿重复提交 |
| 缺 ffmpeg / NumPy | 报告缺项；不伪造成品 |
| `oneshot_review_recommended` / `transient_peak_count`≥2 | 复核事件边界；未确认时保留完整声音，不强制重切 |
| oneshot 无有效信号 / gain_limited | 读 report；改区间或有限重生 |
| import 拒扩展名 | 确认上传的是 final.wav |
| query `recovering` | 再短等同一 import 任务 |
