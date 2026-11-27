---
name: unity-audio-clip-generation
description: "Generate background music (BGM) and ambient audio clips in Unity using AI, with optional loop-seam finishing before import. Use when the user wants background music, BGM, soundtrack, ambient sound, or looping audio — e.g. \"给场景加个背景音乐\", \"生成背景音乐\", \"make some music for my game\". DO NOT use for sound effects (SFX) — use generate_sound_effect instead. Flow: MCP generate_music + check_task → audio_finish loop/normalize → file_upload → import_audio_from_url (is_bgm=true)."
---

> **执行前读取**：[共享执行约定](../../experience/templates/generator-async-pattern.md)。
> 主 agent 按需委托 `general-purpose`；已在子代理中则直接执行本 skill，不再委托。已提交的阶段从原任务续接。

> 整理脚本与参考：[`../game_audio_production/`](../game_audio_production/)。依赖 Python 3.10+、NumPy、ffmpeg/ffprobe。

# Generate BGM / Ambient Audio in Unity 🎵

生成 BGM / 环境床，**默认经整理后再进 Unity**。循环曲做接缝；非循环可做响度。导入使用 `is_bgm=true`（可自动 BGMPlayer）。

**流程**：
1. MCP `generate_music` → 轮询 → `check_task` → `output.data.audioUrl`
2. 下载原件 → `audio_finish.py` **loop**（循环）或 **normalize**（非循环）→ `final.wav`
3. `file_upload(final.wav)` → `import_audio_from_url(is_bgm=true)` → `query_local_task`

仅当用户明确要求「原样 / 快速试听 / 不要后处理」时跳过步骤 2（CDN 须为 wav/mp3）。

## 执行步骤

1. **生成**：MCP `generate_music`
   - `prompt`：含 "BGM" / "background music"；风格、编制、是否循环。见 [prompts.md](../game_audio_production/references/prompts.md)。
   - `duration_seconds`：**必须显式**；游戏 BGM 常 15–30s，循环可 30–60s 并留选段余量。省略不等于保证 90s。范围 1–180。
   - `output_format`：**`wav`**。
   - 按共享约定轮询。
2. **取 URL**：`check_task` → `audioUrl`。费用确认规则同前（时长影响积分）。
3. **整理（默认）**：见 [processing.md](../game_audio_production/references/processing.md)、[generator-unity.md](../game_audio_production/references/generator-unity.md)。
   ```text
   SKILL_DIR = …/extension/skills/game_audio_production
   python "SKILL_DIR/scripts/audio_finish.py" inspect "WORK/source.wav" --out "WORK/inspect-v1"
   # 循环：按本次原件选 start/end（勿盲套示例秒数）
   python "SKILL_DIR/scripts/audio_finish.py" loop "WORK/source.wav" --out "WORK/loop-v1" --start START --end END --crossfade-ms 80 --sample-rate 48000 --lufs -18 --true-peak -2
   # 非循环床：normalize（目标按项目；示例 -18 LUFS / -2 dBTP）
   # python ".../audio_finish.py" normalize "WORK/source.wav" --out "WORK/bgm-v1" --sample-rate 48000 --lufs -18 --true-peak -2
   ```
   循环时按原件选乐句后再 `loop`。条件允许时听完整段，再听 `repeat-3x.wav` **三遍**并重点听两个接缝（可对照 `join-preview.wav`）；接缝有问题则改 start/end 或交叠后继续导入。不要对整段头尾淡成静音冒充无缝。
   听/分析仅用明确支持音频的能力；工具仅支持图像/视频就不要拿它反复试声音。无此能力时写「未试听」并继续导入。脚本成功不等于接缝听感合格。
4. **上传 + 导入**：
   ```python
   execute_custom_tool("import_audio_from_url", {
     "audio_url": "<file_url of final.wav>",
     "is_bgm": True,
     "prompt": "...",
     "output_path": "..."   # 可选
   })
   ```
5. **等待查询**：≤30s 前台等待 → `query_local_task` 一次 → `audio_path` / `created_bgm_player`。验证 AudioClip。

**档位**：生成约 30–120 秒；整理与导入另计。

## 约束

1. 成品经 upload 再 import；禁止未整理 flac/aac 直丢 import。
2. `is_bgm=true`：无引用时自动 BGMPlayer（loop + 2D + playOnAwake）；已有引用只重绑。
3. 按秒计费与 `confirm_cost` 规则同前。
4. 缺依赖时报告；征得同意前不擅自降级为原样 import。

## 故障处理

| 症状 | 处理 |
|---|---|
| 费用确认提示 | 展示预估，用户同意后 confirm_cost=true |
| 循环仍顿一下 | 重选乐句/交叠后继续导入，不是加长淡入 |
| import 拒扩展名 | 确认上传 final.wav |
| query `recovering` | 再短等同一 import |
