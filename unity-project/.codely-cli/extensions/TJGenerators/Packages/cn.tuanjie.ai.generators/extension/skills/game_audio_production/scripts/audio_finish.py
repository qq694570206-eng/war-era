"""Conservative audio finishing. Requires NumPy and ffmpeg/ffprobe on PATH."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import shutil
import subprocess
import sys
import wave

import numpy as np

VERSION = '0.2.1'


def require(condition, message):
    if not condition:
        raise ValueError(message)


def finite(value, name):
    require(math.isfinite(float(value)), name + ' must be finite')
    return float(value)


def db(value):
    return 20 * math.log10(value) if value > 0 else None


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def command(args):
    result = subprocess.run(list(map(str, args)), capture_output=True, text=True, encoding='utf-8', errors='replace')
    if result.returncode:
        raise ValueError('Media tool failed: ' + result.stderr[-1600:])
    return result


def ffmpeg(args):
    return command(['ffmpeg', '-hide_banner', '-nostdin', *args])


def probe(source):
    require(Path(source).is_file(), 'Source file not found')
    obj = json.loads(command(['ffprobe', '-v', 'error', '-show_streams', '-show_format', '-of', 'json', source]).stdout)
    streams = [s for s in obj['streams'] if s.get('codec_type') == 'audio']
    require(len(streams) == 1, 'Expected one audio stream; select a stream explicitly outside this helper')
    stream = streams[0]
    require(stream.get('channels') in (1, 2), 'Only mono/stereo supported; do not silently downmix surround')
    return {'codec': stream.get('codec_name'), 'sample_rate': int(stream['sample_rate']),
            'channels': stream['channels'], 'sample_format': stream.get('sample_fmt'),
            'duration_seconds': float(obj.get('format', {}).get('duration', stream.get('duration', 0)))}


def decode(source, rate=None, mono=False):
    info = probe(source)
    rate = rate or info['sample_rate']
    require(8000 <= rate <= 192000, 'Sample rate outside 8000..192000')
    channels = 1 if mono else info['channels']
    result = subprocess.run(['ffmpeg', '-v', 'error', '-xerror', '-nostdin', '-i', str(source),
                             '-map', '0:a:0', '-vn', '-ar', str(rate), '-ac', str(channels),
                             '-f', 'f32le', '-'], capture_output=True)
    require(result.returncode == 0, 'Full audio decode failed: ' + result.stderr.decode(errors='replace')[-800:])
    data = np.frombuffer(result.stdout, dtype='<f4').astype(np.float64).reshape(-1, channels)
    require(len(data) > 0 and np.isfinite(data).all(), 'Empty or nonfinite decoded audio')
    return data, rate, info


def transient_peak_hint(data, rate, min_gap_ms=45, rel_floor=0.18, abs_db=-28):
    """Hysteretic envelope activity hints, never a semantic event count or a crop instruction."""
    amp = np.max(np.abs(data), axis=1)
    peak = float(amp.max())
    if peak <= 0:
        return {'count': 0, 'times_seconds': [], 'threshold_dbfs': None, 'min_gap_ms': min_gap_ms,
                'interpretation': 'No signal. Diagnostic only; not listening or semantic event proof.'}
    thr = max(peak * rel_floor, 10 ** (abs_db / 20))
    min_gap = max(1, int(rate * min_gap_ms / 1000))
    win = max(1, int(rate * 0.012))
    # Peak blocks bridge waveform zero crossings. Rearm only after a sustained quiet
    # gap, rather than counting the same ringing/decaying sound every min_gap_ms.
    block = max(1, round(rate * .005))
    starts = np.arange(0, len(amp), block)
    envelope = np.maximum.reduceat(amp, starts)
    times, armed, quiet_samples = [], True, 0
    for i, level in zip(starts, envelope):
        if armed and level >= thr:
            j = min(len(amp), i + win)
            k = i + int(np.argmax(amp[i:j]))
            times.append(round(k / rate, 4))
            armed, quiet_samples = False, 0
        elif not armed and level < thr * .5:
            quiet_samples += min(block, len(amp) - i)
            if quiet_samples >= min_gap:
                armed = True
        else:
            quiet_samples = 0
    return {'count': len(times), 'times_seconds': times[:16], 'threshold_dbfs': db(thr),
            'min_gap_ms': min_gap_ms,
            'interpretation': ('Envelope activity hint only. Multiple regions require review, not automatic '
                               'cropping: one sound may contain several attacks. Preserve tails unless a '
                               'complete event window is verified.')}


def signal_stats(data, rate, threshold_db=-48):
    require(data.ndim == 2 and len(data) > 0, 'Expected nonempty frames x channels')
    amp = np.max(np.abs(data), axis=1)
    active = np.flatnonzero(amp > 10 ** (threshold_db / 20))
    peak = float(amp.max())
    first = int(active[0]) if len(active) else None
    last = int(active[-1]) + 1 if len(active) else None
    n = min(max(1, round(rate * .1)), len(data))
    a = float(np.sqrt(np.mean(data[:n] ** 2)))
    b = float(np.sqrt(np.mean(data[-n:] ** 2)))
    stereo = None
    if data.shape[1] == 2:
        stereo_rms = float(np.sqrt(np.mean(data ** 2)))
        mono_rms = float(np.sqrt(np.mean(data.mean(axis=1) ** 2)))
        stereo = db(mono_rms / stereo_rms) if stereo_rms > 0 else None
    return {'frames': len(data), 'duration_seconds': len(data) / rate, 'sample_rate': rate,
            'channels': data.shape[1], 'sample_peak_dbfs': db(peak),
            'rms_dbfs': db(float(np.sqrt(np.mean(data ** 2)))),
            'samples_at_or_above_full_scale': int(np.count_nonzero(np.abs(data) >= 1)),
            'near_full_scale_samples': int(np.count_nonzero(np.abs(data) >= 32767 / 32768)),
            'activity_threshold_dbfs': threshold_db, 'first_active_frame': first, 'last_active_frame_exclusive': last,
            'leading_ms': first / rate * 1000 if first is not None else None,
            'trailing_ms': (len(data) - last) / rate * 1000 if last is not None else None,
            'seam_sample_jump': float(np.max(np.abs(data[-1] - data[0]))),
            'head_100ms_rms_dbfs': db(a), 'tail_100ms_rms_dbfs': db(b),
            'downmix_rms_change_db': stereo,
            'transient_peak_hint': transient_peak_hint(data, rate),
            'interpretation': 'Threshold activity, seam, and transient_peak_hint are diagnostics, not listening.'}


def measure_loudness(source, target=-18, true_peak=-2):
    result = ffmpeg(['-v', 'info', '-i', source, '-map', '0:a:0', '-vn', '-af',
                     f'loudnorm=I={target}:TP={true_peak}:LRA=11:print_format=json', '-f', 'null', '-'])
    found = re.findall(r'\{[^{}]*"input_i"[^{}]*\}', result.stderr, re.S)
    require(bool(found), 'Missing loudness measurement')
    raw = json.loads(found[-1])
    def number(key):
        value = float(raw[key])
        return value if math.isfinite(value) else None
    return {'integrated_lufs': number('input_i'), 'true_peak_dbtp': number('input_tp'),
            'loudness_range_lu': number('input_lra'), 'raw': raw}


def write_wav(path, data, rate):
    require(not Path(path).exists(), 'Output exists; choose a new output directory')
    require(np.isfinite(data).all(), 'Refusing nonfinite output')
    require(float(np.max(np.abs(data))) < 1, 'Output would clip; lower gain before writing')
    quantized = np.round(data * 32768).clip(-32768, 32767).astype('<i2')
    with wave.open(str(path), 'wb') as out:
        out.setnchannels(data.shape[1]); out.setsampwidth(2); out.setframerate(rate)
        out.writeframes(quantized.tobytes())


def trim_one_shot(data, rate, threshold_db=-48, leading_ms=8, trailing_ms=60,
                  fade_in_ms=1, fade_out_ms=5, peak_db=-6, max_gain_db=12):
    values = [threshold_db, leading_ms, trailing_ms, fade_in_ms, fade_out_ms, peak_db, max_gain_db]
    require(all(math.isfinite(x) for x in values), 'All processing parameters must be finite')
    require(-100 <= threshold_db <= -10 and -60 <= peak_db <= -.1, 'Invalid threshold or peak')
    require(min(leading_ms, trailing_ms, fade_in_ms, fade_out_ms, max_gain_db) >= 0, 'Timing and maximum gain must be nonnegative')
    active = np.flatnonzero(np.max(np.abs(data), axis=1) > 10 ** (threshold_db / 20))
    require(len(active) > 0, 'No signal above threshold; inspect source, do not amplify silence')
    start = max(0, int(active[0]) - round(leading_ms * rate / 1000))
    end = min(len(data), int(active[-1]) + 1 + round(trailing_ms * rate / 1000))
    output = data[start:end].copy()
    original_peak = float(np.max(np.abs(output)))
    gain = min(10 ** (peak_db / 20) / original_peak, 10 ** (max_gain_db / 20))
    output *= gain
    fade_in = min(round(fade_in_ms * rate / 1000), len(output) // 2)
    fade_out = min(round(fade_out_ms * rate / 1000), len(output) // 2)
    if fade_in > 1:
        output[:fade_in] *= np.linspace(0, 1, fade_in)[:, None]
    if fade_out > 1:
        output[-fade_out:] *= np.linspace(1, 0, fade_out)[:, None]
    return output, {'source_range_frames': [start, end], 'gain_db': db(gain),
                    'fade_in_frames': fade_in, 'fade_out_frames': fade_out,
                    'gain_limited': gain < 10 ** (peak_db / 20) / original_peak - 1e-12}


def loop_segment(data, rate, start, end, crossfade_ms):
    require(all(math.isfinite(v) for v in (start, end, crossfade_ms)), 'Nonfinite loop parameter')
    a, b = round(start * rate), round(end * rate)
    require(0 <= a < b <= len(data), 'Loop range outside decoded audio')
    part = data[a:b].copy()
    n = round(crossfade_ms * rate / 1000)
    require(0 <= n < len(part) // 2, 'Crossfade must be nonnegative and less than half the selection')
    if n >= 2:
        t = np.linspace(0, 1, n)[:, None]
        seam = part[-n:] * (1 - t) + part[:n] * t
        output = np.concatenate([part[n:-n], seam])
    else:
        require(n == 0, 'Use zero crossfade or at least two samples')
        output = part
    return output, {'source_range_frames': [a, b], 'crossfade_frames': n,
                    'crossfade_method': 'linear circular overlap; duration shortened by overlap',
                    'duration_reduction_seconds': n / rate,
                    'warning': 'Smooth waveform endpoints do not prove beat, harmony or ambience continuity.'}


def normalize_file(source, dest, rate, target, true_peak):
    require(-35 <= finite(target, 'LUFS') <= -9, 'LUFS outside supported -35..-9')
    require(-12 <= finite(true_peak, 'true peak') <= -1, 'True peak outside supported -12..-1')
    m = measure_loudness(source, target, true_peak)
    raw = m['raw']
    require(all(math.isfinite(float(raw[k])) for k in ('input_i','input_tp','input_lra','input_thresh','target_offset')),
            'Silent, too short or invalid loudness result; do not apply LUFS to this clip')
    filt = (f'loudnorm=I={target}:TP={true_peak}:LRA=11:measured_I={raw["input_i"]}:'
            f'measured_TP={raw["input_tp"]}:measured_LRA={raw["input_lra"]}:'
            f'measured_thresh={raw["input_thresh"]}:offset={raw["target_offset"]}:linear=true:print_format=json')
    result = ffmpeg(['-n','-i',source,'-map','0:a:0','-vn','-af',filt,'-ar',rate,'-c:a','pcm_s16le',dest])
    logs = re.findall(r'\{[^{}]*"input_i"[^{}]*\}', result.stderr, re.S)
    actual = measure_loudness(dest, target, true_peak)
    require(actual['integrated_lufs'] is not None, 'Final loudness is not measurable')
    return {'target_lufs':target, 'target_true_peak_dbtp':true_peak,
            'pass1':m, 'pass2':json.loads(logs[-1]) if logs else None, 'measured_final':actual,
            'loudness_within_1_lu':abs(actual['integrated_lufs'] - target) <= 1,
            'true_peak_within_0_2_db':actual['true_peak_dbtp'] <= true_peak + .2}


def save_report(path, obj):
    with path.open('x', encoding='utf-8') as f:
        json.dump(obj, f, ensure_ascii=False, indent=2, allow_nan=False)


def selected(data, rate, start, end):
    a = 0 if start is None else round(finite(start, 'start') * rate)
    b = len(data) if end is None else round(finite(end, 'end') * rate)
    require(0 <= a < b <= len(data), 'Selected range is empty or outside source')
    return data[a:b].copy(), [a,b]


def main():
    p = argparse.ArgumentParser(description=__doc__)
    sub = p.add_subparsers(dest='mode', required=True)
    for name in ('inspect','oneshot','normalize','loop'):
        q = sub.add_parser(name); q.add_argument('source',type=Path); q.add_argument('--out',type=Path,required=True)
        q.add_argument('--threshold-db',type=float,default=-48)
        if name != 'inspect':
            q.add_argument('--sample-rate',type=int,default=48000); q.add_argument('--mono',action='store_true')
            q.add_argument('--start',type=float); q.add_argument('--end',type=float)
        if name == 'oneshot':
            q.add_argument('--peak-db',type=float,default=-6);q.add_argument('--max-gain-db',type=float,default=12)
            q.add_argument('--leading-ms',type=float,default=8);q.add_argument('--trailing-ms',type=float,default=60)
            q.add_argument('--fade-in-ms',type=float,default=1);q.add_argument('--fade-out-ms',type=float,default=5)
        if name in ('normalize','loop'):
            q.add_argument('--lufs',type=float,required=True); q.add_argument('--true-peak',type=float,default=-2)
        if name == 'loop':
            q.add_argument('--crossfade-ms',type=float,default=80)
    a = p.parse_args()
    for exe in ('ffmpeg','ffprobe'):
        require(shutil.which(exe) is not None, exe + ' is required on PATH')
    require(not a.out.exists(), 'Output directory exists; choose a new version to preserve evidence')
    require(-100 <= finite(a.threshold_db, 'threshold') <= -10, 'Invalid activity threshold')
    data, rate, info = decode(a.source)
    stats = signal_stats(data,rate,a.threshold_db)
    report = {'version':VERSION,'operation':a.mode,'source':{'file':a.source.name,'sha256':digest(a.source),'probe':info,'signal':stats},
              'parameters':{k:str(v) if isinstance(v,Path) else v for k,v in vars(a).items() if k not in ('source','out')},
              'listening':'not_performed','semantic_content':'not_verified','engine_playback':'not_performed'}
    if a.mode == 'inspect':
        report['source']['loudness'] = measure_loudness(a.source)
        a.out.mkdir(parents=True)
        save_report(a.out/'report.json',report)
        hint = stats['transient_peak_hint']
        print(json.dumps({'duration':stats['duration_seconds'],'peak':stats['sample_peak_dbfs'],
                          'leading_ms':stats['leading_ms'],'transient_peak_count':hint['count'],
                          'transient_peak_times_seconds':hint['times_seconds'],
                          'report':str(a.out/'report.json')},ensure_ascii=False));return
    if a.mono and data.shape[1] == 2:
        change = stats['downmix_rms_change_db']
        require(change is not None and change > -18, 'Severe stereo phase cancellation; inspect before mono conversion')
    data,rate,_ = decode(a.source, a.sample_rate, a.mono)
    if a.mode == 'loop':
        require(a.start is not None and a.end is not None, 'Loop requires measured --start and --end')
        data,processing = loop_segment(data,rate,a.start,a.end,a.crossfade_ms)
    else:
        data,region = selected(data,rate,a.start,a.end)
        processing={'selected_source_range_frames':region}
        if a.mode == 'oneshot':
            data,detail=trim_one_shot(data,rate,a.threshold_db,a.leading_ms,a.trailing_ms,a.fade_in_ms,a.fade_out_ms,a.peak_db,a.max_gain_db)
            processing.update(detail)
    require(float(np.max(np.abs(data))) > 1e-8, 'No useful signal after processing')
    processing['range_sample_rate_hz'] = rate
    # Temporary PCM must not silently clip even when the source itself is over full scale.
    working_gain=min(1, .98/float(np.max(np.abs(data))))
    data*=working_gain
    processing['working_pcm_gain_db']=db(working_gain)
    a.out.mkdir(parents=True)
    if a.mode == 'oneshot':
        final=a.out/'final.wav';write_wav(final,data,rate)
    else:
        intermediate=a.out/'working.wav';write_wav(intermediate,data,rate)
        final=a.out/'final.wav'
        processing['loudness']=normalize_file(intermediate,final,rate,a.lufs,a.true_peak)
    output,rate,_=decode(final)
    report['processing']=processing
    report['final']={'file':final.name,'sha256':digest(final),'probe':probe(final),'signal':signal_stats(output,rate,a.threshold_db),
                     'loudness':measure_loudness(final)}
    if a.mode == 'loop':
        repeated=np.tile(output,(3,1));write_wav(a.out/'repeat-3x.wav',repeated,rate)
        n=min(round(rate*1.5),len(output));seam=np.concatenate([output[-n:],output[:n]])
        write_wav(a.out/'join-preview.wav',seam,rate)
        report['preview']={'repeat_file':'repeat-3x.wav','join_file':'join-preview.wav',
                           'first_join_seconds':len(output)/rate,'second_join_seconds':2*len(output)/rate}
    failures=[]
    if a.mode in ('normalize','loop'):
        measured=processing['loudness']
        if not measured['loudness_within_1_lu']:failures.append('Final LUFS misses target by more than 1 LU')
        if not measured['true_peak_within_0_2_db']:failures.append('Final true peak exceeds target by more than 0.2 dB')
    report['technical_result']='failed' if failures else 'passed'
    report['failures']=failures
    final_hint = report['final']['signal']['transient_peak_hint']
    if a.mode == 'oneshot' and a.start is None and a.end is None and final_hint['count'] >= 2:
        report['oneshot_review_recommended'] = True
        report['oneshot_review_reason'] = (
            'Multiple envelope activity regions; verify event boundaries before selecting --start/--end. '
            'This hint alone does not justify cropping.')
    save_report(a.out/'report.json',report)
    payload = {'result':report['technical_result'],'final':str(final),'duration':len(output)/rate,
               'listening':'not_performed','transient_peak_count':final_hint['count'],
               'transient_peak_times_seconds':final_hint['times_seconds'],
               'report':str(a.out/'report.json')}
    if report.get('oneshot_review_recommended'):
        payload['oneshot_review_recommended'] = True
    print(json.dumps(payload, ensure_ascii=False))
    if failures:sys.exit(2)


if __name__ == '__main__':
    try:
        main()
    except (ValueError,OSError,subprocess.SubprocessError) as error:
        print(str(error),file=sys.stderr);sys.exit(1)
