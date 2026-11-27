---
name: unity-voice-clone
description: "Clone a voice from an audio sample in Unity, producing a custom_voice_id that can be used with generate_tts to synthesize speech in the cloned voice. Use this skill whenever the user wants to clone a voice, create a custom voice profile, or replicate someone's voice for TTS — e.g. \"克隆声音\", \"复制语音\", \"clone this voice\", \"make a custom voice\", \"用这个音频做语音合成\". Trigger proactively for any voice cloning or custom voice creation request in Unity. Do NOT use for generating TTS directly (use generate_tts) or generating BGM/SFX (use generate_audio_clip / generate_sound_effect). MCP flow: voice_clone runs entirely on the MCP backend (file_upload + voice_clone + check_task) — its result is a custom_voice_id string (no Unity landing); the subsequent TTS output lands via import_audio_from_url."
---

> **执行前读取**：[共享执行约定](../../experience/templates/generator-async-pattern.md)。
> 主 agent 按需委托 `general-purpose`；已在子代理中则直接执行本 skill，不再委托。已提交的阶段从原任务续接。

> **后续 TTS**：克隆得到 `custom_voice_id` 后，合成与落盘走 [`unity-tts-generation`](../generate_tts/SKILL.md)（含默认响度整理）。本 skill **不做** `audio_finish`、不 import。

# Voice Clone in Unity 🎙️ (MCP flow)

克隆产物是 **`custom_voice_id` 字符串，不是资产文件**——本 skill 自身无 Unity 落盘段。完整链路：

```
本地音频 → file_upload → CDN URL
  → MCP voice_clone(audio_url) → check_task → output.data.customVoiceId
  →（需要语音时）activate / 执行 unity-tts-generation：
       generate_tts → finish normalize → file_upload → import_audio_from_url
```

## 🚦 执行步骤

1. **上传样本**：`file_upload(file_path)`（本地音频 mp3/wav/m4a，10 秒–5 分钟，<20MB）→ 若 `already_uploaded=false`，把字节 PUT 到 `upload_url`（验证 HTTP 200）→ 拿永久 `file_url`
2. **克隆**：MCP `voice_clone(audio_url=file_url)` → 拿 `task_id`，按共享约定执行返回的轮询命令（约 30-120 秒）
3. **取结果**：轮询结束 → `check_task` 一次 → **`output.data.customVoiceId`**（一字不差写入最终报告）
4. **（用户要求试听/直接合成时）**：按 `unity-tts-generation` 执行（`voice_id=customVoiceId`），不要在本 skill 内自行直 import 未整理音频

## ⚠️ Skill 独有约束

1. **`voice_id` 必须一字不差**——`customVoiceId` 是服务端返回的字符串，禁止自己编造或截断；预设音色（如 `Chinese (Mandarin)_Gentleman`）或克隆 ID 之外的一切值都会失败。
2. **克隆本身不落盘**——不要为 voice_clone 调任何 import / audio_finish；TTS 落盘见 `unity-tts-generation`。
3. **本地样本必须先 `file_upload`** 拿 CDN URL（禁止 base64 / 本地路径直传）。
4. **采样要求**——10 秒–5 分钟、mp3/wav/m4a、<20MB；超限先剪辑。

## 故障处理

| 症状 | 原因 | 处理 |
|---|---|---|
| `check_task` 仍 pending 超预估 | 克隆 30-120s 正常 | 对同一 task_id 再 check_task 一次，勿重复提交 |
| 返回的 customVoiceId 用不了 | 复制时被截断/改写 | 重新 check_task 取原始值，一字不差使用 |
| 后续 TTS 失败 | 非法 voice_id 或格式 | 核对 ID；落盘与整理按 `unity-tts-generation` |
| 通知未到达 | 通知可能未送达 | 按共享执行约定处理——有间隔等待 + 状态查询 是标准完成模式 |
