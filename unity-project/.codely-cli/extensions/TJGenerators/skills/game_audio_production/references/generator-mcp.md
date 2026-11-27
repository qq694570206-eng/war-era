# Generator MCP 音频调用（本包）

按当前 MCP 工具 schema 操作。不要把生图的 `provider`、`transparent_background` 等字段搬到声音工具。

**在 Codely / TJGenerators 宿主内**：异步等待遵循 [共享执行约定](../../../experience/templates/generator-async-pattern.md)（执行返回的 `poll_command_powershell` / `poll_command`，再 `check_task`）。

## SFX 与 BGM

`generate_sound_effect({prompt, duration_seconds, output_format})`：prompt ≤500 字符，duration 1–180。短 UI/攻击音显式给 1–3 秒，别默认 8 秒。生产流水线优先 `output_format=wav`（便于本地 finish）；若将**未经 finish 的 URL**直接 `import_audio_from_url`，则只能 wav/mp3。

`generate_music` 字段相同；prompt 含 BGM / background music；必须显式时长。循环曲可略留选段与交叠余量。接口无音频 provider 字段，记录完成响应实际 type。

有 `video_url` 时长度跟随视频。普通文生音频不要额外上传。

## TTS

```json
{
  "prompt": "欢迎来到工坊。准备好就开始吧。",
  "voice_id": "Chinese (Mandarin)_Gentleman",
  "emotion": "neutral",
  "speed": 1.0,
  "audio_format": "flac",
  "sample_rate": 44100,
  "language_boost": "Chinese"
}
```

`prompt` 只放要念的原文。预设以当前工具描述为准，不编造 `voice_id`。未要求克隆时不用 `voice_clone`。

生产流水线优先 `audio_format=flac`（可解码无损），再经 finish 输出 WAV 后上传导入。若跳过 finish、直接 Unity 导入，必须用 `mp3`（import 拒 flac/pcm）。

`sample_rate` 实测支持 8000/16000/22050/24000/32000/44100，**不要直接请求 48000**；需要 48kHz 在下载后转码。服务明确拒绝参数才改参重试；等待超时 ≠ 生成失败，勿因此重复扣费提交。

## 等待与下载

1. 保存完整响应与 `task_id`；执行返回的轮询命令（见共享约定）。一个任务只启一个真实等待者。
2. 等待结束后对同一 ID 调一次 `check_task`。仅 queued/running 则继续等，不重提交。只有 completed + 真实 URL 才能下载。
3. 音效/音乐用 `output.data.audioUrl`；TTS 用完成响应实际 URL 字段。不猜地址，不默认用未处理的 `originalAudioUrl` 替代主产物。
4. 高费用报价须用户明确同意后才 `confirm_cost=true`。

## 可选参考上传与音频描述

本地文件：`file_upload({file_path})` → 必要时 PUT `upload_url` → 使用 `file_url`。成品经 finish 后进 Unity 也走同一上传，见 [generator-unity.md](generator-unity.md)。

`generate_prompt_from_media({audio_urls, style_hint})` 可在**明确支持音频描述**时辅助核对台词/接缝/粗内容；验收优先每次一个文件。不能当精密响度仪、人工试听或**事件次数**结论。时间戳超过实际时长则舍弃该项。

宿主侧的 `analyze_multimedia` 等同理：多为按 prompt 的多模态语义描述，**不是** onset/包络计数；部分后端只收视觉、会报「未收到音频」。**不要**把它写成 SFX 必做听感关卡。`audio_finish` 的 `transient_peak_hint` / `oneshot_review_recommended` 也仅作诊断，不能单独触发裁剪。工具不支持声音或一次明确拒绝后勿反复盲试；无法确认完整事件边界时保留原事件和尾音，标「未试听」，详见 [processing.md](processing.md)。
