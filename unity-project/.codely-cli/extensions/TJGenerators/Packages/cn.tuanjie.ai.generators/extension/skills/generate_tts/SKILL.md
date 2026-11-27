---
name: unity-tts-generation
description: Synthesize spoken dialogue or narration with the generator MCP, loudness-normalize, then import as a Unity AudioClip. Use for text-to-speech, NPC dialogue, and speech with an existing voice ID. Voice cloning uses unity-voice-clone; music and sound effects use their audio skills. Flow: MCP generate_tts + check_task → audio_finish normalize → file_upload → import_audio_from_url.
---

> **执行前读取**：[共享执行约定](../../experience/templates/generator-async-pattern.md)。
> 主 agent 按需委托 `general-purpose`；已在子代理中则直接执行本 skill，不再委托。已提交的阶段从原任务续接。

> 整理脚本与参考：[`../game_audio_production/`](../game_audio_production/)。依赖 Python 3.10+、NumPy、ffmpeg/ffprobe。

# Speech in Unity

MCP `generate_tts` 生成对白，**默认经响度/采样率整理后再** `import_audio_from_url`。已删除的 Unity `generate_tts` CustomTool 不可再用。

仅当用户明确要求「原样 / 快速试听」时跳过整理，且须用 **`audio_format=mp3`** 才能直 import（import 拒 flac/pcm）。

## 执行步骤

1. **生成**：MCP `generate_tts`
   - `prompt`：**只放要念的原文**（勿写「请用温柔声音说…」这类会被读出的说明）。
   - 保留用户或 `voice_clone` 返回的 `voice_id`；未指定则省略，用后端默认。
   - 生产整理路径优先 `audio_format="flac"` + 合法 `sample_rate`（如 **44100**；**不要直接请求 48000**，后端会 422）。情绪/语速等用参数，见实时 MCP schema 与 [generator-mcp.md](../game_audio_production/references/generator-mcp.md)。
   - 跳过整理时用 `audio_format="mp3"`。
2. **等待取 URL**：按共享约定轮询 → `check_task` → 实际音频 URL（多为 `output.data.audioUrl`）。勿在 pending 时重复提交。
3. **整理（默认）**：见 [processing.md](../game_audio_production/references/processing.md)、[generator-unity.md](../game_audio_production/references/generator-unity.md)。
   ```text
   SKILL_DIR = …/extension/skills/game_audio_production
   # 下载 → WORK/source.flac（或实际扩展名）
   python "SKILL_DIR/scripts/audio_finish.py" inspect "WORK/source.flac" --out "WORK/inspect-v1"
   python "SKILL_DIR/scripts/audio_finish.py" normalize "WORK/source.flac" --out "WORK/voice-v1" --mono --sample-rate 48000 --lufs -18 --true-peak -2
   ```
   不裁句中停顿、不吞首尾字。脚本 exit 2 时读 report 修正后可再 normalize 一次。条件允许时逐字核对台词；听/分析仅用明确支持音频的能力，工具仅支持图像/视频就不要拿它反复试声音。无此能力时写「未试听」并继续导入。错字只能重生，调音量改不了字。
4. **上传 + 导入**：
   ```python
   execute_custom_tool("import_audio_from_url", {
     "audio_url": "<file_url of final.wav>",
     "prompt": "<台词原文>",
     "is_bgm": False,
     "play_on_awake": False,
     "output_path": "..."   # 可选
   })
   ```
5. **等待查询**：≤30s → `query_local_task` 一次 → 验证 `audio_path` 为 AudioClip。仅在用户要求时再接线。对白不 loop。

报告 MCP task id、import task id、`audio_path`、时长，以及是否做过整理。占位文件与计时结束不算成功。遵循费用确认响应。

## 约束

1. 参数名是 `audio_format`，不是 music/SFX 的 `output_format`。
2. 禁止把未上传的 flac CDN 直丢给 import。
3. 缺依赖时报告；不伪造成品。
