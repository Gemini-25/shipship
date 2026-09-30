#!/usr/bin/env python3
"""
배 크기 템플릿 생성기 (v10.4, v10.9: 여러 층으로 접은 배).
미리내호(6인)와 같은 뼈대 — 왼쪽 엔진실, 위 줄(원자로·냉각·배전·정비·창고·주방·식당·통신), 가운데 통로, 아래 줄(생명유지·수경재배·에어락·침실·의무·휴게·함교) —
를 인원에 맞춰 방 크기와 설비 수를 늘려 뽑는다. 배관 배치 규칙(원자로 오른쪽 → 냉각실, 펌프는 위 외벽 바로 아래, 정수기는 생명유지실 오른쪽 위)을 지킨다.
출력: game/src/Core/ShipTemplates.cs
"""
import math, os

def ceil(a, b): return -(-a // b)

class Room:
    def __init__(self, label, w, h):
        self.label, self.w, self.h = label, w, h
        self.g = [['.'] * w for _ in range(h)]
        self.g[0][0] = label
    def put(self, ch, x, y, w=1, h=1):
        for yy in range(y, y + h):
            for xx in range(x, x + w):
                assert 0 <= xx < self.w and 0 <= yy < self.h, (self.label, ch, xx, yy, self.w, self.h)
                assert self.g[yy][xx] == '.', (self.label, ch, xx, yy, self.g[yy][xx])
                self.g[yy][xx] = ch

def reactor_room(n, H):
    s = 3 if n <= 6 else 4 if n <= 12 else 5 if n <= 20 else 6
    r = Room('r', s + 5, H)
    r.put('R', 2, 1, s, s)
    r.put('C', s + 3, 0, 2, 1)
    return r

def cooling_room(pumps, H):
    w = 3 * pumps - 1 + 2
    r = Room('k', w, H)
    r.g[0][0] = '.'
    for i in range(pumps):
        r.put('P', 1 + 3 * i, 0, 2, 2)
    r.g[H - 1][0] = 'k'
    return r

def power_room(batteries, H):
    rows = 2 if H >= 7 else 1
    per_row = ceil(batteries, rows)
    w = max(6, 3 * per_row + 1)
    r = Room('p', w, H)
    r.put('X', 1, 0, 3, 1)
    r.put('Z', w - 2, 1, 2, 1)
    for i in range(batteries):
        row, col = divmod(i, per_row)
        r.put('Y', 1 + 3 * col, H - 2 - 3 * row, 2, 2)
    return r

def workshop_room(n, H):
    benches = max(1, ceil(n, 10))
    w = max(6, 4 * benches + 3)
    r = Room('w', w, H)
    for i in range(benches):
        r.put('W', 1 + 4 * i, 0, 3, 1)
    r.put('H', w - 2, 0, 2, 1)
    refineries = 1 if n <= 12 else 2
    for i in range(refineries):
        r.put('N', 1 + 3 * i, H - 2, 2, 2)
    r.put('K', w - 1, H - 3, 1, 3)
    return r

def storage_room(n, H):
    shelves = max(4, ceil(n * 2, 3))
    rows = [0, 3, H - 2] if H >= 7 else [0, H - 2]   # v10.9: 높은 방은 선반 세 줄 (배가 덜 길어진다)
    cols = ceil(shelves, len(rows))
    w = 3 * cols - 1
    r = Room('s', max(5, w + 1), H)
    r.g[0][0] = '.'
    for i in range(shelves):
        col, row = divmod(i, len(rows))
        r.put('K', 1 + 3 * col, rows[row], 2, 2)
    r.g[2][0] = 's'
    return r

def galley_room(n, H):
    stoves = max(1, ceil(n, 6)); fridges = max(1, ceil(n, 6))
    if stoves + fridges <= 4:
        w = max(5, 3 * (stoves + fridges) + 1)
        r = Room('j', w, H)
        x = 1
        for i in range(stoves): r.put('V', x, 0, 2, 1); x += 3
        for i in range(fridges): r.put('F', x, 0, 2, 1); x += 3
        r.put('T', 1, H - 3, 2, 1)
        return r
    # v10.9: 큰 주방 — 조리대는 위 벽, 냉장고는 아래 벽, 가운데 조리대(작업 테이블)
    w = max(7, 3 * max(stoves, fridges) + 2)
    r = Room('j', w, H)
    for i in range(stoves): r.put('V', 1 + 3 * i, 0, 2, 1)
    for i in range(fridges): r.put('F', 1 + 3 * i, H - 1, 2, 1)
    r.put('T', 2, H // 2 - 1, 2, 1)
    return r

def mess_room(n, H):
    disp = max(2, ceil(n, 4))
    tables = max(2, ceil(n, 4))
    w = max(9, 3 * tables + 3, disp + 3)
    r = Room('m', w, H)
    for i in range(disp): r.put('D', 1 + i, 0)
    for i in range(tables):
        x = 3 + 3 * i
        if x + 1 >= w: break
        # 배식기 줄(0) 아래 한 줄은 통로로 비운다 (배식기 앞 칸이 의자에 막히면 못 꺼낸다)
        r.put('S', x, 2, 2, 1); r.put('T', x, 3, 2, 1); r.put('S', x, 4, 2, 1)
    return r

def comms_room(H):
    r = Room('o', 6, H)
    r.put('C', 3, 0)
    r.put('A', 3, 1, 2, 1)
    return r

def life_room(n, H):
    gens = max(2, ceil(n, 3))
    recyclers = max(1, ceil(n, 6))
    w = max(8, 3 * gens + 1, 3 * recyclers + 3)
    r = Room('l', w, H)
    # 정수기: 오른쪽 위 (첫 대가 가장 오른쪽 — 급수 본관이 여기서 수경재배실로 간다)
    for i in range(recyclers):
        r.put('U', w - 2 - 3 * i, 0, 2, 2)
    if gens > 4 and H >= 8:
        # v10.9: 산소 발생기가 많으면 두 줄 (2×3)
        per = ceil(gens, 2)
        w2 = max(w, 3 * per + 1, 3 * recyclers + 3)
        if w2 != w:
            for row in r.g: row.extend(['.'] * (w2 - w))
            r.w = w2
            for row in r.g:
                for x in range(w2): row[x] = '.' if row[x] != 'l' else 'l'
            for i in range(recyclers): r.put('U', w2 - 2 - 3 * i, 0, 2, 2)
            w = w2
        for i in range(gens):
            row, col = divmod(i, per)
            r.put('O', 3 * col, H - 6 + 3 * row, 2, 3)
    else:
        for i in range(gens):
            r.put('O', 3 * i, H - 4, 2, 4)
    r.put('C', w - 1, H - 1)
    return r

def hydro_room(beds, H):
    ys = [1, 3 if H == 7 else 4, H - 2] if H >= 7 else [1, H - 2]   # v10.9: 높은 방은 재배대 세 줄
    per_row = ceil(beds, len(ys))
    w = 5 * per_row + 1
    r = Room('f', w, H)
    for i in range(beds):
        row, col = divmod(i, per_row)
        r.put('G', 1 + 5 * col, ys[row], 4, 1)
    return r

def airlock_room(n, H):
    docks = 2 if n <= 12 else 3
    lockers = 2 if n <= 6 else 3 if n <= 12 else 4
    w = 3 if lockers <= 2 and docks <= 2 else 5
    r = Room('a', w, H)
    xs = list(range(0, w, 2))
    for i in range(lockers):
        r.put('L', xs[i % len(xs)], 1 + 2 * (i // len(xs)), 1, 2) if 1 + 2 * (i // len(xs)) + 1 < H - 2 else None
    for i in range(docks):
        r.put('Q', xs[i % len(xs)], H - 2, 1, 2)
    return r

def quarters_rooms(n, H):
    rooms = []
    left = n
    while left > 0:
        k = min(left, 12)
        bottom = ceil(k, 2); topc = k - bottom
        top_y = 0 if H <= 5 else 1
        w = max(8, 2 * max(bottom, topc) + 3)
        r = Room('q', w, H)
        for i in range(bottom): r.put('B', 1 + 2 * i, H - 2, 1, 2)
        for i in range(topc): r.put('B', 1 + 2 * i + (1 if top_y == 0 else 0), top_y, 1, 2) if 1 + 2 * i + (1 if top_y == 0 else 0) < w - 1 else None
        r.put('K', w - 1, top_y + 2)
        rooms.append(r)
        left -= k
    return rooms

def medbay_room(n, H):
    beds = max(2, ceil(n, 5))
    w = max(5, 2 * beds + 1)
    r = Room('h', w, H)
    r.put('C', w - 1, 0)
    for i in range(beds):
        r.put('M', 1 + 2 * i, H - 3, 1, 2)
    r.put('K', 0, H - 1)
    return r

def lounge_room(n, H):
    groups = max(1, ceil(n, 6))
    w = max(5, 4 * groups + 1)
    r = Room('g', w, H)
    for i in range(groups):
        x = 1 + 4 * i
        r.put('S', x, 1, 2, 1); r.put('T', x, 2, 2, 2); r.put('S', x, 4, 2, 1) if H > 5 else None
    return r


def nose_room(Hn):
    """함교 = 뱃머리. 오른쪽 위아래를 비스듬히 깎아 뾰족하게 (깎인 칸은 우주). 조타 콘솔이 코끝, 주 컴퓨터는 뒤쪽."""
    c = min(7, Hn // 2 - 1)
    w = c + 6
    r = Room('b', w, Hn)
    r.carve = set()
    for y in range(Hn):
        yy = min(y, Hn - 1 - y)
        if yy < c:
            for x in range(w - (c - yy), w):
                r.carve.add((x, y))
    mid = Hn // 2
    r.put('I', 1, mid - 1, 2, 2)
    r.put('C', w - 2 - (1 if (w - 2, mid) in r.carve else 0), mid)   # 조타 (코끝)
    r.put('S', w - 3, mid)
    r.put('C', 5, 1); r.put('S', 4, 1)                                 # 항법
    r.put('C', 5, Hn - 2); r.put('S', 4, Hn - 2)                       # 통신 중계
    return r

def engine_room(n, H2):
    engines = 2 if n <= 12 else 3
    w = 7
    r = Room('e', w, H2)
    r.carve = set()
    c = 3
    for y in range(H2):
        yy = min(y, H2 - 1 - y)
        if yy < c:
            for x in range(0, c - yy):
                r.carve.add((x, y))
    r.g[0][0] = '.'; r.g[0][c] = 'e'   # 라벨은 깎이지 않는 곳에
    span = H2 - 2 * c
    for i in range(engines):
        y = c + (i + 1) * span // (engines + 1) - 1
        r.put('E', 0, y, 3, 3)
    r.put('C', w - 1, c + 1)
    return r

WIDEN_OK = set('swjmqhgfpl')

def widen(room, extra):
    if not hasattr(room, 'w0'): room.w0 = room.w
    for row in room.g: row.extend(['.'] * extra)
    room.w += extra

def furnish(room):
    """v10.9: 넓힌 방의 빈 자리를 그 방답게 채운다 (식탁·소파·조리대·선반). 문 줄(맨 위·아래)은 비워 둔다."""
    w0 = getattr(room, 'w0', room.w)
    H = room.h
    def free(x, y, w, h):
        return all(0 <= xx < room.w - 1 and 0 <= yy < H and room.g[yy][xx] == '.' for xx in range(x, x + w) for yy in range(y, y + h))
    L = room.label
    x = 1 if L == 'j' else w0 + 1
    if L == 'j' and H < 7: return
    while x < room.w - 2:
        if L == 'm' and H >= 5 and free(x, 2, 2, 3):
            room.put('S', x, 2, 2, 1); room.put('T', x, 3, 2, 1); room.put('S', x, 4, 2, 1); x += 3
        elif L == 'g' and H >= 6 and free(x, 1, 2, 4):
            room.put('S', x, 1, 2, 1); room.put('T', x, 2, 2, 2); room.put('S', x, 4, 2, 1); x += 4
        elif L == 'j' and free(x, H // 2, 2, 1) and free(x, H // 2 - 1, 2, 1) and free(x, H // 2 + 1, 2, 1):
            room.put('T', x, H // 2, 2, 1); x += 3
        elif L == 's' and H >= 7 and free(x, 3, 2, 2):
            room.put('K', x, 3, 2, 2); x += 3
        else:
            x += 1

def build(n, name):
    H = 5 if n <= 4 else 6 if n <= 6 else 7 if n <= 12 else 8
    K = 3 if n <= 12 else 4
    s = 3 if n <= 6 else 4 if n <= 12 else 5 if n <= 20 else 6
    reactor_kw = 48 * s * s / 9
    pumps = max(2, ceil(int(reactor_kw * 1.15), 30))
    beds = max(2, ceil(4 * n, 6))

    # ── 층 나누기: 위 층은 기관(원자로·냉각·배전·정비·통신), 그 아래는 생명유지·수경재배, 맨 아래는 에어락 ──
    bands = [[] for _ in range(K)]
    bands[0] = [reactor_room(n, H), cooling_room(pumps, H), power_room(max(2, 2 * ceil(n, 6)), H), workshop_room(n, H)]
    bands[1] = [life_room(n, H), hydro_room(beds, H)]
    bands[K - 1] = [airlock_room(n, H)]
    tail0 = [comms_room(H)]
    pool = [storage_room(n, H), medbay_room(n, H), lounge_room(n, H)] + quarters_rooms(n, H)
    galley, mess = galley_room(n, H), mess_room(n, H)
    ratio = [0.85] + [1.0] * (K - 2) + [0.85]
    inner = list(range(1, K - 1))
    def width(b):
        rooms = bands[b] + (tail0 if b == 0 else [])
        return sum(r.w for r in rooms) + len(rooms) - 1 + (3 if b in inner else 0)
    # 주방+식당은 한 덩어리 (조리한 끼니를 나르는 길이 짧게) — 가운데 층에
    gm_band = inner[-1]
    bands[gm_band] += [galley, mess]
    for r in sorted(pool, key=lambda r: -r.w):
        b = min(range(K), key=lambda b: (width(b) + r.w + 1) / ratio[b])
        bands[b].append(r)
    bands[0] += tail0
    # 층 너비 맞추기: 가운데 층들은 가장 긴 층에 맞추고(방마다 조금씩 넓힌다), 바깥 층은 제 길이대로 (계단식 윤곽)
    W_in = max(width(b) for b in range(K))
    def pad(b, target):
        extra = target - width(b)
        ok = [r for r in bands[b] if r.label in WIDEN_OK and r.label not in 'lf'] or [r for r in bands[b] if r.label in WIDEN_OK]
        while extra > 0 and ok:
            widen(min(ok, key=lambda r: r.w / max(1, len(r.label))), 1)
            extra -= 1
    for b in inner: pad(b, W_in)
    for b in (0, K - 1): pad(b, int(W_in * 0.62))
    for b in range(K):
        for r in bands[b]: furnish(r)
    # 세로 통로(층을 잇는 척추): 가운데 층마다 방들 사이 가운데쯤
    spine_at = {}
    for b in inner:
        idx = max(2 if b == 1 else 1, len(bands[b]) // 2)
        spine_at[b] = idx

    # ── 좌표 ──
    band_y = [1 + b * (H + 4) for b in range(K)]
    H2 = K * H + 4 * (K - 1)
    eng = engine_room(n, H2)
    x0 = 1 + eng.w + 1
    # 뱃머리: K=3 → 가운데 층 + 위아래 통로, K=4 → 가운데 두 층 + 그 사이 통로
    if K == 3:
        ny0, ny1 = band_y[1] - 3, band_y[1] + H + 2
    else:
        ny0, ny1 = band_y[1], band_y[2] + H - 1
    nose = nose_room(ny1 - ny0 + 1)
    body_end = {}
    xs = {}
    for b in range(K):
        x = x0
        pos = []
        for i, r in enumerate(bands[b]):
            if b in spine_at and i == spine_at[b]:
                pos.append(('spine', x)); x += 3
            pos.append((r, x)); x += r.w + 1
        xs[b] = pos
        body_end[b] = x - 2
    x_nose = max(body_end[b] for b in range(K) if b in inner or K == 3 and b == 1) + 2
    # 가운데 층은 뱃머리 앞까지 채운다
    for b in inner:
        gap = x_nose - 2 - body_end[b]
        if gap > 0:
            r = [r for r, _ in xs[b] if r != 'spine'][-1]
            widen(r, gap)
            body_end[b] += gap
    W = x_nose + nose.w + 2
    Ht = 1 + H2 + 1
    grid = [[' '] * W for _ in range(Ht)]
    carved = set()

    def blit(room, gx, gy):
        for yy in range(room.h):
            for xx in range(room.w):
                if (xx, yy) in getattr(room, 'carve', ()):
                    carved.add((gx + xx, gy + yy)); continue
                grid[gy + yy][gx + xx] = room.g[yy][xx]

    blit(eng, 1, 1)
    for b in range(K):
        for r, x in xs[b]:
            if r == 'spine':
                for yy in range(band_y[b] - 1, band_y[b] + H + 1):
                    grid[yy][x] = '.'; grid[yy][x + 1] = '.'
            else:
                blit(r, x, band_y[b])
    blit(nose, x_nose, ny0)
    # 통로: 층 사이마다 2줄, 두 층 중 긴 쪽 끝까지 (뱃머리 앞에서 멈춘다)
    corr_rows = []
    for b in range(K - 1):
        cy = band_y[b] + H + 1
        end = min(max(body_end[b], body_end[b + 1]), x_nose - 2)
        for yy in (cy, cy + 1):
            for xx in range(x0, end + 1): grid[yy][xx] = '.'
        corr_rows.append((cy, end))
    grid[corr_rows[0][0]][x0 + 1] = 'c'

    # ── 벽: 바닥에 8방향으로 닿은 빈칸은 벽 ──
    def floorish(ch): return ch not in ' #'
    for y in range(Ht):
        for x in range(W):
            if grid[y][x] != ' ': continue
            if any(0 <= y + dy < Ht and 0 <= x + dx < W and floorish(grid[y + dy][x + dx])
                   for dy in (-1, 0, 1) for dx in (-1, 0, 1)):
                grid[y][x] = '#'

    def door(x, y):
        assert grid[y][x] == '#', (name, 'door on non-wall', x, y, grid[y][x])
        grid[y][x] = '+'

    def pick(room, gx, row_y, inside_row, prefer=None):
        free = [xx for xx in range(room.w) if room.g[inside_row][xx] in ('.', room.label) and (xx, inside_row) not in getattr(room, 'carve', ())]
        if prefer: free = [xx for xx in free if prefer(xx)] or free
        if not free: return None
        return gx + min(free, key=lambda xx: abs(xx - room.w // 2))

    for b in range(K):
        for r, x in xs[b]:
            if r == 'spine': continue
            if b > 0:   # 위 통로로
                px = pick(r, x, band_y[b] - 1, 0, (lambda xx: xx <= 2) if r.label == 'l' else None)
                if px is not None: door(px, band_y[b] - 1)
            if b < K - 1:   # 아래 통로로
                px = pick(r, x, band_y[b] + H, H - 1, (lambda xx, w=r.w: xx >= w - 3) if r.label == 'k' else None)
                if px is not None: door(px, band_y[b] + H)
            if r.label == 'a':   # 에어락 바깥 해치 (아래 선체, 거치대를 피해)
                door(pick(r, x, band_y[b] + H, H - 1), band_y[b] + H)
    # 엔진실 → 통로마다 양쪽 문
    for cy, _ in corr_rows:
        door(x0 - 1, cy); door(x0 - 1, cy + 1)
    # 뱃머리 → 닿는 통로
    for cy, end in corr_rows:
        if ny0 <= cy <= ny1 and end == x_nose - 2:
            door(x_nose - 1, cy); door(x_nose - 1, cy + 1)
    # 문 앞이 막히지 않았는지
    for y in range(Ht):
        for x in range(W):
            if grid[y][x] != '+': continue
            opens = [(dx, dy) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)) if 0 <= y + dy < Ht and 0 <= x + dx < W and floorish(grid[y + dy][x + dx])]
            for dx, dy in opens:
                ch = grid[y + dy][x + dx]
                assert ch in '.+' or ch.islower(), (name, 'door blocked', x, y, ch)
    lines = [''.join(row).rstrip() for row in grid]
    return '\n'.join(lines), dict(n=n, K=K, H=H, reactor=s, pumps=pumps, beds=beds, W=W, H_total=Ht, aspect=round(W / Ht, 2))

templates = [(4, 'Kestrel', '제비호'), (12, 'Hanbit', '한빛호'), (20, 'Eunha', '은하호'), (30, 'Cheonma', '천마호')]
out = ['// 자동 생성: tools/shipgen/gen_ships.py (v10.4 배 크기 템플릿). 손으로 고치지 말고 생성기를 고친다.',
       'namespace ShipSim.Core;', '', 'public static partial class ShipBlueprints', '{']
info = []
for n, key, kname in templates:
    ascii, meta = build(n, key)
    info.append((n, key, kname, meta))
    out.append(f'    public const string {key}Name = "{kname}";')
    out.append(f'    public const string {key} = """')
    out.append(ascii)
    out.append('""";')
    out.append('')
out.append('}')
path = os.path.join(os.path.dirname(__file__), '..', '..', 'game', 'src', 'Core', 'ShipTemplates.cs')
open(path, 'w', encoding='utf-8').write('\n'.join(out) + '\n')
for n, key, kname, meta in info:
    print(kname, meta)
