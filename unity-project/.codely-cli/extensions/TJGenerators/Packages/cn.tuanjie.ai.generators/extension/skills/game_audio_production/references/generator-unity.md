# Unity / Codely 音频衔接（本 extension）

整理脚本不调用生成后端；由各音频入口 skill（SFX / BGM / TTS）在 MCP 生成之后调用。

## 落盘（经 finish）

1. MCP 生成完成 → `check_task` → 得到 `audioUrl`（或 TTS 实际 URL）。
2. 将原件下载到任务目录 `WORK/source.<ext>`，保留 task 元数据与源哈希；**不覆盖原件**。
3. `inspect` 核验，再按类型跑 [processing.md](processing.md)：
   - 单次 SFX / UI：`oneshot`（常 `--mono --sample-rate 48000 --peak-db -6`）；`oneshot_review_recommended` / 多活动区仅提示复核，不作为强制裁剪依据。确认完整事件边界后才用 `--start/--end` 重切
   - 对白 / 非循环 BGM 响度：`normalize`（示例 `--lufs -18 --true-peak -2`）
   - 循环 BGM / 环境：听选 `--start/--end` 后 `loop`
4. 成品为 `WORK/<out>/final.wav`（PCM16）。脚本通过只表示技术检查通过，不表示听感合格；条件允许时按类型做简要听核（循环听三遍接缝等）。无音频能力时保留完整事件及尾音，标「未试听，事件边界未确认」后进入上传导入，见 [processing.md](processing.md)。
5. `file_upload({file_path: final.wav 绝对路径})` → 拿永久 `file_url`（必要时 PUT）。
6. `execute_custom_tool("import_audio_from_url", { audio_url: file_url, ... })`：
   - BGM / 循环环境：`is_bgm: true`
   - SFX / TTS：`is_bgm: false`，`play_on_awake: false`
   - `prompt` 写入 History；`output_path` 可选
7. 按共享约定前台等待（≤30s）→ `query_local_task` 一次 → 验证 `audio_path` 可加载为 AudioClip。

**禁止**：把 finish 前的 flac/aac CDN URL 直接丢给 `import_audio_from_url`（工具只接受 .wav/.mp3 扩展名）。
**禁止**：跳过 upload，把本地路径当成 `audio_url`。

## 跳过整理

仅当用户明确要求「原样 / 快速试听 / 不要后处理」时，才把 MCP 返回的 **wav/mp3** CDN URL 直接传给 `import_audio_from_url`。按秒计费的生成仍须遵守费用确认规则。

## 已有声音只要整理

用户给出本地路径或已有 CDN URL → 下载/复制到 WORK → finish → upload → import。不因后处理问题自动重新生成。声音克隆仍用 `unity-voice-clone`；克隆 ID 交给 `unity-tts-generation` 再整理落盘。
