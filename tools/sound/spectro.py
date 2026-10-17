#!/usr/bin/env python3
"""소리 확인: WAV → 스펙트로그램 그림 (가로 시간 · 세로 높이(로그) · 밝기 크기) + 위에 크기 곡선.
   python3 tools/sound/spectro.py build/sound/kestrel-18.wav [out.png]"""
import sys, wave, numpy as np
from PIL import Image, ImageDraw

src = sys.argv[1]
out = sys.argv[2] if len(sys.argv) > 2 else src.rsplit('.', 1)[0] + '.png'
with wave.open(src) as w:
    rate, ch, n = w.getframerate(), w.getnchannels(), w.getnframes()
    x = np.frombuffer(w.readframes(n), dtype=np.int16).astype(np.float32) / 32768
x = x.reshape(-1, ch).mean(axis=1)
win, hop = 1024, 256
frames = max(1, (len(x) - win) // hop)
spec = np.zeros((frames, win // 2), np.float32)
hann = np.hanning(win)
for i in range(frames):
    seg = x[i * hop:i * hop + win] * hann
    spec[i] = np.abs(np.fft.rfft(seg))[:win // 2]
db = 20 * np.log10(spec / (win / 4) + 1e-7)
W, H, top = 1200, 360, 80
img = Image.new('RGB', (W, H + top), (8, 10, 16))
freqs = np.arange(win // 2) * rate / win
logf = np.log10(np.clip(freqs, 40, None))
ys = ((logf - np.log10(40)) / (np.log10(rate / 2) - np.log10(40)) * (H - 1)).astype(int)
cols = np.linspace(0, frames - 1, W).astype(int)
px = img.load()
for X, fi in enumerate(cols):
    col = np.full(H, -120.0)
    for k in range(1, win // 2):
        y = H - 1 - ys[k]
        col[y] = max(col[y], db[fi, k])
    last = -120.0
    for y in range(H - 1, -1, -1):
        v = col[y] if col[y] > -119 else last
        last = v
        t = np.clip((v + 100) / 80, 0, 1)
        px[X, top + y] = (int(255 * t ** 0.8), int(200 * t ** 1.6), int(90 + 120 * (1 - t) * t))
d = ImageDraw.Draw(img)
rms = np.sqrt(np.convolve(x * x, np.ones(hop) / hop, 'same'))[::max(1, len(x) // W)][:W]
for X in range(len(rms) - 1):
    y0 = top - 4 - int(np.clip((20 * np.log10(rms[X] + 1e-6) + 60) / 60, 0, 1) * (top - 10))
    y1 = top - 4 - int(np.clip((20 * np.log10(rms[X + 1] + 1e-6) + 60) / 60, 0, 1) * (top - 10))
    d.line([(X, y0), (X + 1, y1)], fill=(120, 200, 255))
for f in (100, 300, 1000, 3000, 10000):
    y = top + H - 1 - int((np.log10(f) - np.log10(40)) / (np.log10(rate / 2) - np.log10(40)) * (H - 1))
    d.line([(0, y), (12, y)], fill=(200, 200, 200)); d.text((14, y - 6), f'{f}Hz', fill=(200, 200, 200))
secs = len(x) / rate
for s in range(0, int(secs) + 1, 5):
    X = int(s / secs * (W - 1)); d.line([(X, top + H - 8), (X, top + H)], fill=(200, 200, 200)); d.text((X + 2, top + H - 18), f'{s}s', fill=(200, 200, 200))
img.save(out)
print(out, f'{secs:.1f}s')
