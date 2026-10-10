"""
텍스처 생성기 (v10 업그레이드). 칸 하나 = 128px, 변형 넷(2x2) = 256x256.

바닥·벽은 투명 배경 위에 홈(어두운 선)·가장자리 하이라이트·나사·잔흠집·얼룩을 그린 RGBA 무늬다.
게임에서는 방 색 바닥 위에 이 무늬를 덮는다 (색은 코드의 팔레트가 정한다). 에어락의 경고 줄무늬만 색이 있다.
그 밖에: 천장 조명이 바닥에 떨어뜨리는 빛(light), 벽 아래 그늘(shade), 배경 성운(nebula).

    python3 tools/textures/make_textures.py
    (그다음 Godot 에디터를 한 번 열거나: godot --headless --path game --import)

출력: game/assets/textures/*.png
"""
import os, random, math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

TILE = 128
SS = 2  # 슈퍼샘플링
OUT = os.path.join(os.path.dirname(__file__), '..', '..', 'game', 'assets', 'textures')
os.makedirs(OUT, exist_ok=True)
U = TILE / 64.0  # 예전 64px 좌표계를 그대로 쓰기 위한 배율

def canvas():
    return Image.new('RGBA', (TILE * 2 * SS, TILE * 2 * SS), (0, 0, 0, 0))

def px(v):
    return int(v * U * SS)

DARK = lambda a: (0, 0, 0, int(255 * a))
LIGHT = lambda a: (255, 255, 255, int(255 * a))

def origin(i):
    return ((i % 2) * TILE * SS, (i // 2) * TILE * SS)

def seam(d, ox, oy, a_dark=0.42, a_light=0.08, w=1.4):
    s = TILE * SS
    d.rectangle([ox, oy, ox + s - 1, oy + px(w)], fill=DARK(a_dark))
    d.rectangle([ox, oy, ox + px(w), oy + s - 1], fill=DARK(a_dark))
    d.rectangle([ox + px(w), oy + px(w), ox + s - 1, oy + px(w) + px(0.7)], fill=LIGHT(a_light))
    d.rectangle([ox + px(w), oy + px(w), ox + px(w) + px(0.7), oy + s - 1], fill=LIGHT(a_light))

def bevel_panel(d, x0, y0, x1, y1, a_dark=0.32, a_light=0.08, r=2.0):
    """눌러 찍은 판: 테두리 홈 + 왼쪽 위 하이라이트 + 오른쪽 아래 그늘."""
    d.rounded_rectangle([px(x0), px(y0), px(x1), px(y1)], radius=px(r), outline=DARK(a_dark), width=px(0.9))
    d.line([px(x0 + 1), px(y0 + 1), px(x1 - 1), px(y0 + 1)], fill=LIGHT(a_light), width=px(0.6))
    d.line([px(x0 + 1), px(y0 + 1), px(x0 + 1), px(y1 - 1)], fill=LIGHT(a_light), width=px(0.6))
    d.line([px(x0 + 1), px(y1 - 1), px(x1 - 1), px(y1 - 1)], fill=DARK(a_dark * 0.5), width=px(0.6))
    d.line([px(x1 - 1), px(y0 + 1), px(x1 - 1), px(y1 - 1)], fill=DARK(a_dark * 0.5), width=px(0.6))

def rivet(d, cx, cy, r=1.5, a=0.5):
    d.ellipse([px(cx - r), px(cy - r), px(cx + r), px(cy + r)], fill=DARK(a))
    d.ellipse([px(cx - r * 0.7), px(cy - r * 0.7), px(cx + r * 0.1), px(cy + r * 0.1)], fill=LIGHT(0.2))

def off(ox, oy):
    return ox / (U * SS), oy / (U * SS)

def noise_field(rng, size, octaves=4, persistence=0.55):
    """부드러운 값 잡음 (얼룩·때)."""
    total = np.zeros((size, size))
    amp, freq, norm = 1.0, 4, 0.0
    for _ in range(octaves):
        g = rng.normal(0, 1, (freq + 1, freq + 1))
        img = Image.fromarray(((g - g.min()) / (np.ptp(g) + 1e-9) * 255).astype(np.uint8)).resize((size, size), Image.BICUBIC)
        total += amp * (np.asarray(img) / 255.0 - 0.5)
        norm += amp
        amp *= persistence
        freq *= 2
    return total / norm

def grime_layer(rng, amount=0.10, grain=0.035):
    """때(큰 얼룩) + 결(자잘한 잡음)."""
    size = TILE * 2
    stain = noise_field(rng, size)
    fine = rng.normal(0, 1, (size, size))
    a = np.clip(np.abs(stain) * amount * 2.2, 0, 1) + np.clip(np.abs(fine) * grain, 0, 1)
    rgb = np.where((stain + fine * 0.2)[..., None] > 0, 255, 0).repeat(3, axis=2)
    return Image.fromarray(np.dstack([rgb, np.clip(a, 0, 1) * 255]).astype(np.uint8), 'RGBA')

def scratches(d, r, ox, oy, n=10, a=0.06):
    for _ in range(n):
        x = ox + r.randint(6, TILE - 6) * SS; y = oy + r.randint(6, TILE - 6) * SS
        dx = r.randint(-18, 18) * SS; dy = r.randint(-5, 5) * SS
        d.line([x, y, x + dx, y + dy], fill=LIGHT(a) if r.random() < 0.6 else DARK(a * 1.8), width=SS)

def finish(img, name, rng, grime=0.1, grain=0.035):
    small = img.resize((TILE * 2, TILE * 2), Image.LANCZOS)
    small = Image.alpha_composite(small, grime_layer(rng, grime, grain))
    small.save(os.path.join(OUT, name + '.png'))
    print('wrote', name)

# ─────────────────────────── 바닥 ───────────────────────────

def plate():
    """통로·기본: 솔질한 강판 두 장, 걸음길 표시, 점검 해치, 나사."""
    rng = np.random.default_rng(1); r = random.Random(1)
    img = canvas(); d = ImageDraw.Draw(img)
    for i in range(4):
        ox, oy = origin(i); bx, by = off(ox, oy)
        seam(d, ox, oy)
        bevel_panel(d, bx + 3, by + 3, bx + 61, by + 30)
        bevel_panel(d, bx + 3, by + 34, bx + 61, by + 61)
        for cx, cy in [(6, 6), (58, 6), (6, 58), (58, 58), (6, 32), (58, 32)]:
            rivet(d, bx + cx, by + cy)
        for _ in range(26):  # 솔질 결
            y = oy + r.randint(6, TILE - 6) * SS; x0 = ox + r.randint(4, 60) * SS
            d.line([x0, y, x0 + r.randint(20, 60) * SS, y], fill=LIGHT(0.03), width=SS)
        if i == 1:
            d.rounded_rectangle([px(bx + 22), px(by + 38), px(bx + 42), px(by + 56)], radius=px(2), outline=DARK(0.4), width=px(0.9))
            d.rectangle([px(bx + 29), px(by + 45), px(bx + 35), px(by + 48)], fill=DARK(0.35))
        scratches(d, r, ox, oy, 8)
    finish(img, 'floor_plate', rng, 0.09)

def diamond():
    """창고·정비실: 미끄럼 방지 줄무늬 강판."""
    rng = np.random.default_rng(7); r = random.Random(7)
    img = canvas(); d = ImageDraw.Draw(img)
    for i in range(4):
        ox, oy = origin(i); bx, by = off(ox, oy)
        seam(d, ox, oy, 0.45)
        for gy in range(0, 64, 6):
            for gx in range(0, 64, 6):
                cx, cy = bx + gx + (3 if (gy // 6) % 2 else 0), by + gy + 3
                ang = 0.8 if ((gx + gy) // 6) % 2 else -0.8
                l = 2.2
                d.line([px(cx - l * math.cos(ang)), px(cy - l * math.sin(ang)), px(cx + l * math.cos(ang)), px(cy + l * math.sin(ang))], fill=LIGHT(0.09), width=px(1.1))
                d.line([px(cx - l * math.cos(ang)) + SS, px(cy - l * math.sin(ang)) + SS, px(cx + l * math.cos(ang)) + SS, px(cy + l * math.sin(ang)) + SS], fill=DARK(0.18), width=px(0.7))
        for cx, cy in [(5, 5), (59, 5), (5, 59), (59, 59)]:
            rivet(d, bx + cx, by + cy, 1.4)
        scratches(d, r, ox, oy, 6, 0.05)
    finish(img, 'floor_diamond', rng, 0.12)

def grate():
    """기관·원자로·냉각·배전·생명유지: 깊은 격자, 아래로 관이 비친다."""
    rng = np.random.default_rng(2); r = random.Random(2)
    img = canvas(); d = ImageDraw.Draw(img)
    for i in range(4):
        ox, oy = origin(i); bx, by = off(ox, oy)
        # 아래가 들여다보이는 어둠과 관
        d.rectangle([px(bx + 4), px(by + 4), px(bx + 60), px(by + 60)], fill=DARK(0.32))
        py = by + (18 if i % 2 else 40)
        d.rounded_rectangle([px(bx + 4), px(py), px(bx + 60), px(py + 7)], radius=px(3), fill=DARK(0.2))
        d.line([px(bx + 4), px(py + 1.5), px(bx + 60), px(py + 1.5)], fill=LIGHT(0.06), width=px(0.8))
        seam(d, ox, oy, 0.5, 0.07)
        horizontal = i in (0, 3)
        for k in range(8):
            t = 6 + k * 7
            if horizontal:
                d.rectangle([px(bx + 4), px(by + t), px(bx + 60), px(by + t + 2.4)], fill=LIGHT(0.12))
                d.rectangle([px(bx + 4), px(by + t + 2.4), px(bx + 60), px(by + t + 3.3)], fill=DARK(0.4))
            else:
                d.rectangle([px(bx + t), px(by + 4), px(bx + t + 2.4), px(by + 60)], fill=LIGHT(0.12))
                d.rectangle([px(bx + t + 2.4), px(by + 4), px(bx + t + 3.3), px(by + 60)], fill=DARK(0.4))
        d.rectangle([px(bx + 3), px(by + 3), px(bx + 61), px(by + 61)], outline=DARK(0.45), width=px(1.2))
        for cx, cy in [(4, 4), (60, 4), (4, 60), (60, 60)]:
            rivet(d, bx + cx, by + cy, 1.6, 0.55)
    finish(img, 'floor_grate', rng, 0.12, 0.04)

def tile():
    """주방·식당·의무실: 윤기 나는 타일과 줄눈."""
    rng = np.random.default_rng(3); r = random.Random(3)
    img = canvas(); d = ImageDraw.Draw(img)
    for i in range(4):
        ox, oy = origin(i); bx, by = off(ox, oy)
        for sx in range(4):
            for sy in range(4):
                x, y = bx + sx * 16, by + sy * 16
                d.rectangle([px(x), px(y), px(x + 16), px(y + 0.9)], fill=DARK(0.3))
                d.rectangle([px(x), px(y), px(x + 0.9), px(y + 16)], fill=DARK(0.3))
                d.polygon([(px(x + 2), px(y + 2)), (px(x + 9), px(y + 2)), (px(x + 2), px(y + 9))], fill=LIGHT(0.05))
                if r.random() < 0.08:
                    d.rectangle([px(x + 1), px(y + 1), px(x + 15), px(y + 15)], fill=DARK(0.06))
    finish(img, 'floor_tile', rng, 0.05, 0.02)

def soft():
    """침실·휴게실: 카펫 결."""
    rng = np.random.default_rng(4)
    img = canvas(); d = ImageDraw.Draw(img)
    for i in range(4):
        ox, oy = origin(i); bx, by = off(ox, oy)
        d.rectangle([px(bx), px(by), px(bx + 64), px(by + 0.8)], fill=DARK(0.2))
        d.rectangle([px(bx), px(by), px(bx + 0.8), px(by + 64)], fill=DARK(0.2))
        for k in range(0, 64, 2):
            d.line([px(bx), px(by + k), px(bx + 64), px(by + k)], fill=LIGHT(0.02 if k % 4 else 0.035), width=SS)
        for k in range(0, 64, 8):
            d.line([px(bx + k), px(by), px(bx + k), px(by + 64)], fill=DARK(0.03), width=SS)
    finish(img, 'floor_soft', rng, 0.05, 0.035)

def hydro():
    """수경재배실: 물 빠지는 격자판과 젖은 윤기."""
    rng = np.random.default_rng(5)
    img = canvas(); d = ImageDraw.Draw(img)
    for i in range(4):
        ox, oy = origin(i); bx, by = off(ox, oy)
        seam(d, ox, oy, 0.4, 0.06)
        for gx in range(6):
            for gy in range(6):
                cx, cy = bx + 9 + gx * 9.2, by + 9 + gy * 9.2
                d.ellipse([px(cx - 1.9), px(cy - 1.9), px(cx + 1.9), px(cy + 1.9)], fill=DARK(0.55))
                d.arc([px(cx - 1.9), px(cy - 1.9), px(cx + 1.9), px(cy + 1.9)], 200, 320, fill=LIGHT(0.12), width=SS)
        d.polygon([(px(bx + 4), px(by + 44)), (px(bx + 44), px(by + 4)), (px(bx + 52), px(by + 4)), (px(bx + 4), px(by + 52))], fill=LIGHT(0.04))
        if i == 2:
            d.ellipse([px(bx + 30), px(by + 36), px(bx + 50), px(by + 46)], fill=LIGHT(0.05))
    finish(img, 'floor_hydro', rng, 0.1)

def bridge():
    """함교: 어두운 복합 패널과 가는 빛줄."""
    rng = np.random.default_rng(8)
    img = canvas(); d = ImageDraw.Draw(img)
    for i in range(4):
        ox, oy = origin(i); bx, by = off(ox, oy)
        seam(d, ox, oy, 0.35, 0.05, 1.0)
        bevel_panel(d, bx + 4, by + 4, bx + 60, by + 60, 0.22, 0.05, 4)
        if i in (0, 3):
            d.line([px(bx + 10), px(by + 32), px(bx + 54), px(by + 32)], fill=LIGHT(0.1), width=px(0.7))
        else:
            d.line([px(bx + 32), px(by + 10), px(bx + 32), px(by + 54)], fill=LIGHT(0.1), width=px(0.7))
    finish(img, 'floor_bridge', rng, 0.05, 0.02)

def hazard():
    """에어락: 강판 가장자리에 노랑·검정 경고 줄무늬 (색이 있는 유일한 무늬)."""
    rng = np.random.default_rng(9); r = random.Random(9)
    img = canvas(); d = ImageDraw.Draw(img)
    for i in range(4):
        ox, oy = origin(i); bx, by = off(ox, oy)
        seam(d, ox, oy)
        bevel_panel(d, bx + 3, by + 3, bx + 61, by + 61)
        # 가장자리 한쪽에 줄무늬 띠
        band = [(bx + 3, by + 3, bx + 61, by + 12), (bx + 52, by + 3, bx + 61, by + 61), (bx + 3, by + 52, bx + 61, by + 61), (bx + 3, by + 3, bx + 12, by + 61)][i]
        x0, y0, x1, y1 = band
        stripe = Image.new('RGBA', img.size, (0, 0, 0, 0)); sd = ImageDraw.Draw(stripe)
        for k in range(-80, 160, 8):
            sd.polygon([(px(bx + k), px(by)), (px(bx + k + 4), px(by)), (px(bx + k + 4 - 64), px(by + 64)), (px(bx + k - 64), px(by + 64))], fill=(245, 200, 60, 150))
        mask = Image.new('L', img.size, 0); ImageDraw.Draw(mask).rectangle([px(x0), px(y0), px(x1), px(y1)], fill=255)
        dark = Image.new('RGBA', img.size, (0, 0, 0, 0)); ImageDraw.Draw(dark).rectangle([px(x0), px(y0), px(x1), px(y1)], fill=(20, 20, 20, 140))
        img = Image.alpha_composite(img, Image.composite(dark, Image.new('RGBA', img.size, (0, 0, 0, 0)), mask))
        img = Image.alpha_composite(img, Image.composite(stripe, Image.new('RGBA', img.size, (0, 0, 0, 0)), mask))
        d = ImageDraw.Draw(img)
        scratches(d, r, ox, oy, 8, 0.07)
    finish(img, 'floor_hazard', rng, 0.12)

# ─────────────────────────── 벽 ───────────────────────────

def wall():
    """선체·격벽: 판금 이음새, 골조 갈빗대, 볼트, 흘러내린 얼룩."""
    rng = np.random.default_rng(6); r = random.Random(6)
    img = canvas(); d = ImageDraw.Draw(img)
    for i in range(4):
        ox, oy = origin(i); bx, by = off(ox, oy)
        for y in (0, 32):
            d.rectangle([px(bx), px(by + y), px(bx + 64), px(by + y + 1.1)], fill=DARK(0.45))
            d.rectangle([px(bx), px(by + y + 1.1), px(bx + 64), px(by + y + 1.8)], fill=LIGHT(0.07))
        rib = bx + (i % 2) * 20 + 22
        d.rectangle([px(rib), px(by), px(rib + 3.5), px(by + 64)], fill=DARK(0.22))
        d.line([px(rib), px(by), px(rib), px(by + 64)], fill=LIGHT(0.08), width=px(0.6))
        for x in range(8, 64, 14):
            rivet(d, bx + x, by + 5, 1.2, 0.45)
            rivet(d, bx + x, by + 37, 1.2, 0.45)
        for _ in range(3):  # 흘러내린 얼룩
            x = px(bx + r.randint(4, 58)); y = px(by + r.randint(4, 30)); h = px(r.randint(8, 24))
            d.line([x, y, x, y + h], fill=DARK(0.08), width=px(1.2))
        scratches(d, r, ox, oy, 6, 0.05)
    finish(img, 'wall_plate', rng, 0.14, 0.05)

# ─────────────────────────── 빛·그늘·배경 ───────────────────────────

def light():
    """천장 조명이 바닥에 떨어뜨리는 빛 (흰색, 가운데가 밝은 원)."""
    n = 256
    y, x = np.mgrid[0:n, 0:n]
    rr = np.sqrt((x - n / 2 + 0.5) ** 2 + (y - n / 2 + 0.5) ** 2) / (n / 2)
    a = np.clip(1 - rr, 0, 1) ** 2.2
    img = np.dstack([np.full((n, n), 255), np.full((n, n), 250), np.full((n, n), 235), a * 255]).astype(np.uint8)
    Image.fromarray(img, 'RGBA').save(os.path.join(OUT, 'light.png')); print('wrote light')

def shade():
    """벽 아래 그늘: 위(벽 쪽)가 진하고 아래로 옅어지는 띠."""
    w, h = 16, 64
    a = (1 - np.linspace(0, 1, h)) ** 1.8
    img = np.zeros((h, w, 4), np.uint8); img[..., 3] = (a[:, None] * 255).astype(np.uint8)
    Image.fromarray(img, 'RGBA').save(os.path.join(OUT, 'shade.png')); print('wrote shade')

def nebula():
    """배경 성운: 푸른·보랏빛 구름 (어둡게, 별 뒤에)."""
    rng = np.random.default_rng(12)
    n = 512
    a = noise_field(rng, n, 6, 0.6); b = noise_field(rng, n, 6, 0.55); c = noise_field(rng, n, 5, 0.5)
    y, x = np.mgrid[0:n, 0:n] / n
    fall = np.clip(1.3 - np.sqrt((x - 0.3) ** 2 + (y - 0.7) ** 2) * 1.6, 0, 1)
    fall2 = np.clip(1.2 - np.sqrt((x - 0.8) ** 2 + (y - 0.2) ** 2) * 1.8, 0, 1)
    cloud = np.clip(a * 1.6 + 0.35, 0, 1) * fall + np.clip(b * 1.6 + 0.3, 0, 1) * fall2
    rgb = np.dstack([
        40 * cloud + 60 * np.clip(c + 0.2, 0, 1) * fall2,
        55 * cloud * fall + 20 * fall2 * cloud,
        110 * cloud + 40 * fall2 * cloud,
    ])
    alpha = np.clip(cloud * 0.85, 0, 1) * 255
    img = np.dstack([np.clip(rgb, 0, 255), alpha]).astype(np.uint8)
    Image.fromarray(img, 'RGBA').filter(ImageFilter.GaussianBlur(2)).save(os.path.join(OUT, 'nebula.png')); print('wrote nebula')

for f in (plate, diamond, grate, tile, soft, hydro, bridge, hazard, wall, light, shade, nebula):
    f()
