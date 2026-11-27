# 按类型处理

脚本依赖 NumPy 与 PATH 中的 ffmpeg/ffprobe。输入可为可解码的 WAV/FLAC/MP3 等；最终输出 PCM16 WAV。拒绝覆盖输出目录；原件不变。每次保留 report.json，内容包括源与最终哈希、实际指标、处理参数和未试听标志。

`SKILL_DIR` = `extension/skills/game_audio_production`。成品进 Unity 见 [generator-unity.md](generator-unity.md)。

## inspect

```text
python "SKILL_DIR/scripts/audio_finish.py" inspect "WORK/source.flac" --out "WORK/inspect-v1"
```

查看sample_rate/channels/duration、sample_peak_dbfs、near_full_scale_samples、leading_ms、loudness、downmix_rms_change_db，以及 stdout/`signal.transient_peak_hint`（相对峰瞬态提示）。活动阈值默认-48dBFS，不是人耳绝对可听边界；`transient_peak_hint` 也不是听感或语义验收，只用于提醒「可能有多下强瞬态、该不该加 `--start/--end`」。短音效LUFS可能无法测量，null不能当作0。接缝sample_jump低也不能证明音乐拍点连续。

## 单次音效

```text
python "SKILL_DIR/scripts/audio_finish.py" oneshot "WORK/source.wav" --out "WORK/sfx-v1" --mono --sample-rate 48000 --peak-db -6 --leading-ms 8 --trailing-ms 60 --fade-in-ms 1 --fade-out-ms 5
```

默认最大增益12dB，防止把很弱噪声直接拉满；报告gain_limited时不要谎称已达到目标。想保留攻击头可设fade-in-ms=0；尾音没落尽先扩大区间/尾余量，不用长淡出掩盖缺尾。按阈值裁前后不是提取语义事件。

未带 `--start/--end` 的 oneshot 若出现 `oneshot_review_recommended=true`，或成品 `transient_peak_count`≥2，只表示检测到多个包络活动区。检测会等待持续安静区再重新计数，仍不能判定语义事件次数；一次动作也可能包含多个起音。只有可靠试听或用户明确给出的区间确认了多余事件及完整边界，才对原件用 `--start/--end` 重切（新 `--out`）。峰时间不是裁剪边界，禁止固定截取第一峰附近而吞掉尾音。旧版 `oneshot_needs_event_window` 同样只能作诊断参考。

听感：仅用明确支持音频的宿主能力，或 MCP 手册中**已确认吃音频**的描述工具。名称含 multimedia 不等于支持声音；一次明确「未收到音频」后勿反复盲试。无法确认事件边界时保留仅去静音和电平整理的版本，标「未试听，事件边界未确认」并继续导入，不凭诊断计数强制截短。不要把模型审查写成人工试听。

默认保留原声道，只有明确要mono才加--mono。脚本会对明显反相抵消拒绝盲目折叠；不要在外面绕过报错强转。

## 对白、非循环BGM响度

```text
python "SKILL_DIR/scripts/audio_finish.py" normalize "WORK/voice.flac" --out "WORK/voice-v1" --mono --sample-rate 48000 --lufs -18 --true-peak -2
```

先把最终声道和采样率确定，再两遍loudnorm，最后重新测最终文件。-18LUFS/-2dBTP只是示例任务目标；依据用户/项目混音规则选择，不能给所有SFX/BGM一刀切。report记录pass2 normalization_type；可能回退动态处理，不说“永远只是无损增益”。静音/过短或无效测量拒绝放大。normalize不默认裁前导、句中停顿或结尾，可显式选--start/--end，但对白须听后再选。

## 循环

```text
python "SKILL_DIR/scripts/audio_finish.py" loop "WORK/music.wav" --out "WORK/loop-v1" --start 0.5 --end 19.78 --crossfade-ms 80 --sample-rate 48000 --lufs -18 --true-peak -2
```

上例时间仅演示格式，必须按本次原件测量选择。输出final.wav、repeat-3x.wav、join-preview.wav、report.json。循环使用线性首尾交叠，最终时长=(end-start)-crossfade秒。脚本重排起点至交叠之后，记录所有区间，不修改原件。

先挑乐句/节拍上能相接的位置。节拍音乐先考虑零交叠的正确乐句边界；需要交叠时留出额外交叠余量，否则周期变短会拖乱拍点。交叠可消除波形突跳，但不能修复和声冲突、错误拍点、持续长混响或重复强瞬态；条件允许时必须听 `repeat-3x.wav` 三遍并重点听两个接缝，也可对照 `join-preview.wav`。氛围可试100–500ms，瞬态较多先试10–80ms，按实际听感选，不固定套一秒。

响度处理在接缝构造后执行；再次听成品接缝，尤其 normalization_type=dynamic 时。不能仅看处理前接缝；不能对整段做fade in/out造成每循环一次“呼吸式”空洞。无音频试听能力时写「未试听」并继续上传导入，不要用仅支持图像的工具盲试声音。

## 输出判定

脚本通过表示技术检查通过，不表示语义/接缝听感通过。响度偏离目标>1LU或true peak超目标>0.2dB时退出2且保留报告/成品，便于修正。0.2dB是分析/量化容差，严格目标应自行留余量。原件削波只告知，降低增益不能恢复已经丢掉的峰形。交付时可分列数值检查 / 听感或音频模型检查 / 引擎状态。
