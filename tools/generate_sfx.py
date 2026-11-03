#!/usr/bin/env python3
"""
战争纪元 — 音效合成器
用代码生成基础 WAV 音效文件，无需录制
输出到 assets/audio/
"""
import wave
import struct
import math
import os
import random

OUTPUT_DIR = "/Users/lijinlei/geren_www/战争纪元/assets/audio"
SAMPLE_RATE = 22050  # 22kHz，移动端够用且文件小

def ensure_dir(path):
    os.makedirs(path, exist_ok=True)

def save_wav(filename, samples):
    """保存 16-bit PCM WAV"""
    path = os.path.join(OUTPUT_DIR, filename)
    with wave.open(path, 'w') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SAMPLE_RATE)
        frames = b''.join(struct.pack('<h', max(-32768, min(32767, int(s * 32767)))) for s in samples)
        w.writeframes(frames)
    size = os.path.getsize(path)
    print(f"  {filename}: {len(samples)/SAMPLE_RATE:.2f}s, {size//1024}KB")

def generate_sine(freq, duration, volume=0.5):
    """生成正弦波"""
    n = int(SAMPLE_RATE * duration)
    return [volume * math.sin(2 * math.pi * freq * i / SAMPLE_RATE) for i in range(n)]

def generate_square(freq, duration, volume=0.5):
    """生成方波"""
    n = int(SAMPLE_RATE * duration)
    return [volume * (1 if math.sin(2 * math.pi * freq * i / SAMPLE_RATE) > 0 else -1) for i in range(n)]

def generate_sawtooth(freq, duration, volume=0.5):
    """生成锯齿波"""
    n = int(SAMPLE_RATE * duration)
    return [volume * (2 * (freq * i / SAMPLE_RATE % 1) - 1) for i in range(n)]

def generate_noise(duration, volume=0.5, lowpass=0.5):
    """生成白噪声（带简单低通）"""
    n = int(SAMPLE_RATE * duration)
    samples = [random.uniform(-1, 1) * volume for _ in range(n)]
    # 简单低通滤波
    if lowpass > 0:
        filtered = []
        prev = 0
        for s in samples:
            prev = prev * lowpass + s * (1 - lowpass)
            filtered.append(prev)
        samples = filtered
    return samples

def envelope(samples, attack=0.01, decay=0.1):
    """包络：快速起音 + 指数衰减"""
    n = len(samples)
    atk = int(n * attack)
    for i in range(min(atk, n)):
        samples[i] *= i / atk
    for i in range(atk, n):
        samples[i] *= math.exp(-(i - atk) / (n * decay))
    return samples

def fade_out(samples, duration=0.1):
    """淡出"""
    n = len(samples)
    fade = int(SAMPLE_RATE * duration)
    for i in range(max(0, n - fade), n):
        samples[i] *= (n - i) / fade
    return samples

# === 音效生成 ===

def sfx_rifle():
    """步枪：方波 + 快速衰减"""
    s = generate_square(180, 0.1, 0.4)
    # 频率下降
    for i in range(len(s)):
        s[i] = 0.4 * (1 if math.sin(2 * math.pi * (180 - 120 * i / len(s)) * i / SAMPLE_RATE) > 0 else -1)
    s = envelope(s, 0.005, 0.05)
    save_wav("sfx_rifle.wav", s)

def sfx_cannon():
    """火炮：锯齿波 + 低频"""
    n = int(SAMPLE_RATE * 0.25)
    s = []
    for i in range(n):
        freq = 80 - 50 * i / n
        s.append(0.5 * (2 * (freq * i / SAMPLE_RATE % 1) - 1))
    s = envelope(s, 0.01, 0.08)
    save_wav("sfx_cannon.wav", s)

def sfx_explosion():
    """爆炸：白噪声 + 低通 + 衰减"""
    s = generate_noise(0.4, 0.7, 0.3)
    s = envelope(s, 0.005, 0.15)
    save_wav("sfx_explosion.wav", s)

def sfx_explosion_large():
    """大爆炸：更长更低的噪声"""
    s = generate_noise(0.6, 0.9, 0.2)
    s = envelope(s, 0.01, 0.2)
    # 叠加低频
    low = generate_sine(40, 0.6, 0.5)
    s = [a + b for a, b in zip(s, low)]
    save_wav("sfx_explosion_large.wav", s)

def sfx_nuke():
    """核弹：长低频轰鸣 + 噪声"""
    n = int(SAMPLE_RATE * 2.0)
    s = []
    for i in range(n):
        freq = 40 - 20 * i / n
        s.append(0.6 * math.sin(2 * math.pi * freq * i / SAMPLE_RATE))
    noise = generate_noise(1.5, 0.4, 0.15)
    for i in range(min(len(noise), len(s))):
        s[i] += noise[i]
    s = fade_out(s, 0.5)
    save_wav("sfx_nuke.wav", s)

def sfx_build():
    """建造：三角波上升"""
    n = int(SAMPLE_RATE * 0.2)
    s = []
    for i in range(n):
        freq = 440 + 220 * i / n
        s.append(0.4 * math.sin(2 * math.pi * freq * i / SAMPLE_RATE))
    s = envelope(s, 0.01, 0.1)
    save_wav("sfx_build.wav", s)

def sfx_select():
    """选中：短促高频"""
    s = generate_sine(800, 0.06, 0.3)
    s = envelope(s, 0.002, 0.03)
    save_wav("sfx_select.wav", s)

def sfx_move():
    """移动：短促低频"""
    s = generate_sine(600, 0.05, 0.25)
    s = envelope(s, 0.002, 0.025)
    save_wav("sfx_move.wav", s)

def sfx_attack():
    """攻击命令"""
    s = generate_sine(700, 0.08, 0.3)
    s = envelope(s, 0.002, 0.04)
    save_wav("sfx_attack.wav", s)

def sfx_error():
    """错误：低频方波"""
    s = generate_square(150, 0.15, 0.4)
    s = envelope(s, 0.005, 0.08)
    save_wav("sfx_error.wav", s)

def sfx_alert():
    """警报：双声"""
    s1 = generate_square(880, 0.15, 0.3)
    s1 = envelope(s1, 0.005, 0.07)
    gap = [0] * int(SAMPLE_RATE * 0.05)
    s2 = generate_square(880, 0.15, 0.3)
    s2 = envelope(s2, 0.005, 0.07)
    save_wav("sfx_alert.wav", s1 + gap + s2)

def sfx_button():
    """按钮点击"""
    s = generate_sine(1000, 0.04, 0.2)
    s = envelope(s, 0.001, 0.02)
    save_wav("sfx_button.wav", s)

def sfx_superweapon_ready():
    """超武就绪：庄严低频"""
    n = int(SAMPLE_RATE * 0.5)
    s = []
    for i in range(n):
        s.append(0.4 * math.sin(2 * math.pi * 220 * i / SAMPLE_RATE))
        s.append(0.3 * math.sin(2 * math.pi * 330 * i / SAMPLE_RATE))
    s = [a + b for a, b in zip(s[::2], s[1::2])]
    s = envelope(s[:n], 0.05, 0.2)
    save_wav("sfx_superweapon_ready.wav", s)

# === 主程序 ===

if __name__ == "__main__":
    ensure_dir(OUTPUT_DIR)
    print(f"生成音效到 {OUTPUT_DIR}/")
    print()
    
    sfx_rifle()
    sfx_cannon()
    sfx_explosion()
    sfx_explosion_large()
    sfx_nuke()
    sfx_build()
    sfx_select()
    sfx_move()
    sfx_attack()
    sfx_error()
    sfx_alert()
    sfx_button()
    sfx_superweapon_ready()
    
    print()
    print(f"完成！共 13 个音效文件")
