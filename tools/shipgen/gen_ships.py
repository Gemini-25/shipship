#!/usr/bin/env python3
"""
기본 배 다섯 척 생성기 (v16.22 배 재설계 · 크기별 등급).
  제비호(4 · 소형) · 미리내호(6 · 기본) · 한빛호(12 · 중형) · 은하호(20 · 대형) · 천마호(30 · 초대형)

뼈대 (다섯 척 공통):
  · 뒤(왼쪽)는 엔진실이 선체 높이 전체를 차지한다.
  · 바깥 줄(맨 위 · 맨 아래 띠)은 화물 · 창고 · 냉각(방열판은 선체 바깥) · 재배 · 침실 · 정비 · 에어락 — 운석을 먼저 맞는 완충.
  · 그 안쪽은 통로 고리(위 · 아래 가로 통로를 양 끝 세로 통로가 잇는다 — 어디든 두 갈래 길).
  · 고리 안쪽이 심장부: 원자로 · 배전 · 주컴퓨터실(배 한가운데) · 생명유지 — 어느 벽도 선체에 닿지 않는다.
  · 큰 배는 고리 안에 띠가 더 있고(안쪽 띠), 통로가 차압 문으로 구획을 나눈다.
  · 앞(오른쪽)은 뱃머리: 통신실(안테나) · 함교 · (큰 배) 항법실 · 예비 함교.
  · 작을수록 꼭 필요한 방만, 클수록 방 종류가 많고 넓고 호화롭다.
  · 원자로 · 배전 · 주컴퓨터실은 문 하나 (지나다니는 길이 아니다 — 원자로실 출입 통제 문을 질러가다 막히지 않게).
  · 짝이 되는 방은 사이 문 (냉동 창고 ↔ 주방 ↔ 식당 · 정비실 ↔ 창고 · 에어락 ↔ 선외 준비실 · 휴게실 ↔ 체력단련실).
배관 배치 규칙(Piping.Build)을 지킨다: 냉각 펌프는 위 선체 바로 아래 줄, 첫 펌프는 원자로 가운데보다 오른쪽,
냉각실 아래 벽 바로 밑은 통로, 급수 본관은 생명유지실 정수기에서 시작한다. 로봇 충전대(J)는 그리지 않는다 (배를 띄울 때 단다).
출력: game/src/Core/ShipTemplates.cs  (손으로 고치지 말고 이 생성기를 고친다)
"""
import os, sys

LEGEND_POOL = "dintuvxyz0123456789!$%&*?^~;:<>|/-_`,()[]{}αβγδεζηθικλμνξπρστυφχψω"
BASE = set('cberkplwsjmfaqhgo')

# ───────────────────────── 설비 묶음 ─────────────────────────
# 항목: (글자 무늬 줄 목록) — 같은 글자끼리 붙으면 하나의 설비가 된다.
def blk(ch, w, h): return [ch * w] * h
R3, R4, R5, R6 = (blk('R', s, s) for s in (3, 4, 5, 6))
P = blk('P', 2, 2); X = ['XXX']; Z = ['ZZ']; Y = blk('Y', 2, 2); O = blk('O', 2, 2); U = blk('U', 2, 2)
G = ['GGGG']; F = ['FF']; V = ['VV']; D = ['D']; B = ['B', 'B']; M = ['M', 'M']; W = ['WWW']; N = blk('N', 2, 2)
H = ['HH']; L = ['L', 'L']; Q = ['Q', 'Q']; K = blk('K', 2, 2); K3 = ['K', 'K', 'K']; C = ['C']; I = blk('I', 2, 2)
A = ['AA']; S = ['S']; T = ['TT']; J = ['J', 'J']; E = blk('E', 3, 3)
TSET = ['SS', 'TT', 'SS']          # 식탁 하나 + 의자 넷
SOFA = ['S.S', 'TTT', 'S.S']        # 휴게 탁자
ROWS2 = ['SS', 'SS']                # 객석 · 운동 기구 줄

def many(item, n): return [item] * n

# ───────────────────────── 격자 ─────────────────────────
class Ship:
    def __init__(self, key, w, h):
        self.key = key
        self.W, self.H = w, h
        self.g = [[' '] * w for _ in range(h)]
        self.rooms = []        # dict(label, kind, x0, y0, x1, y1 (내부), doors, items, reserve)
        self.legend = {}

    def rect(self, x0, y0, x1, y1):
        """벽 테두리 (x0..x1, y0..y1 포함) + 안은 바닥."""
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                edge = x in (x0, x1) or y in (y0, y1)
                if edge:
                    if self.g[y][x] == ' ': self.g[y][x] = '#'
                else:
                    self.g[y][x] = '.'

    def floor(self, x0, y0, x1, y1):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1): self.g[y][x] = '.'

    def room(self, label, kind, x0, y0, x1, y1, doors, items, reserve=()):
        """내부 좌표 (x0..x1, y0..y1)."""
        r = dict(label=label, kind=kind, x0=x0, y0=y0, x1=x1, y1=y1, doors=doors, items=items, reserve=set(reserve), placed=[], hatch=None)
        self.rooms.append(r)
        return r

def fail(msg):
    print('생성 실패:', msg)
    sys.exit(1)

# ───────────────────────── 띠 배치 ─────────────────────────
def spread(widths, total, grow):
    """방 내부 폭 목록을 (벽 포함) total 칸에 맞춘다 — grow 표시한 방부터 넓힌다."""
    need = sum(widths) + len(widths) + 1
    extra = total - need
    if extra < 0: fail(f'띠 폭 모자람 need {need} > {total}')
    ws = list(widths)
    # 남는 칸은 고루 나눈다 (grow 표시한 방은 두 몫)
    idx = [i for i, g in enumerate(grow) if g] + list(range(len(ws)))
    k = 0
    while extra > 0:
        ws[idx[k % len(idx)]] += 1; extra -= 1; k += 1
    return ws

def spine(x0, y0, x1, y1, ds, res):
    res = set(res)
    if 'N' in ds: res.update((x, y0) for x in range(x0, x1 + 1))
    if 'S' in ds: res.update((x, y1) for x in range(x0, x1 + 1))
    if 'E' in ds: res.update((x1, y) for y in range(y0, y1 + 1))
    if 'W' in ds or 'N' in ds or 'S' in ds: res.update((x0, y) for y in range(y0, y1 + 1))
    return res

def pack(at, x0, y0, x1, y1, ds, res, items):
    """먼 쪽 줄부터 첫 자리에 — 설비끼리는 한 칸씩 띄운다. 안 되면 키 큰 것부터 다시."""
    out = pack1(at, x0, y0, x1, y1, ds, res, items)
    if out is None: out = pack1(at, x0, y0, x1, y1, ds, res, sorted(items, key=lambda it: -len(it)))
    return out

def pack1(at, x0, y0, x1, y1, ds, res, items):
    """먼 쪽 줄부터 첫 자리에 — 설비끼리는 한 칸씩 띄운다 (둘레 바닥이 통로 쪽 줄과 이어진다)."""
    far_top = not ('N' in ds and 'S' not in ds)
    rows = list(range(y0, y1 + 1)) if far_top else list(range(y1, y0 - 1, -1))
    occ, out = {}, []
    for item in items:
        ih, iw = len(item), max(len(l) for l in item)
        ok = False
        for ry in rows:
            ty = ry if far_top else ry - ih + 1
            for tx in range(x0, x1 - iw + 2):
                box = [(tx + dx, ty + dy) for dy in range(ih) for dx in range(iw)]
                if any(not (x0 <= x <= x1 and y0 <= y <= y1) for x, y in box): continue
                if any(at(x, y) != '.' or (x, y) in res for x, y in box): continue
                if any((x + ax, y + ay) in occ for x, y in box for ax in (-1, 0, 1) for ay in (-1, 0, 1)): continue
                for dy, line in enumerate(item):
                    for dx, ch in enumerate(line):
                        if ch != '.': out.append((tx + dx, ty + dy, ch))
                for x, y in box: occ[(x, y)] = 1
                ok = True
                break
            if ok: break
        if not ok: return None
    return out

def fit_w(items, h, ds, w, hres=()):
    """빈 방에 설비가 다 들어가는 가장 좁은 폭."""
    for ww in range(max(w, 3), 60):
        res = spine(0, 0, ww - 1, h - 1, ds, set(hres))
        if pack(lambda x, y: '.', 0, 0, ww - 1, h - 1, ds, res, items) is not None: return ww
    fail(f'방 폭 못 맞춤 {items}')

def fit_h(items, w, ds, h):
    for hh in range(max(h, 2), 40):
        if pack(lambda x, y: '.', 0, 0, w - 1, hh - 1, ds, spine(0, 0, w - 1, hh - 1, ds, set()), items) is not None: return hh
    fail(f'방 높이 못 맞춤 {items}')

def room_doors(r, band_doors):
    """원자로 · 배전 · 주컴퓨터실은 문 하나 (지나다니는 길이 되지 않게 — 출입 통제 문을 질러가다 막히지 않게)."""
    ds = r.get('doors', band_doors)
    if (r['label'] in ('r', 'p') or r.get('kind') == 'ComputerRoom') and len(ds) > 1: ds = ['S']
    return ds

def band_need(rooms): return sum(r['w'] for r in rooms) + len(rooms) + 1

def build(spec):
    cw = spec['corr']
    We = spec['engine']['w']
    bands = spec['bands']            # 위에서 아래로: ('room', dict) | ('corr',)
    bow = spec['bow']
    # 세로 위치
    ys = []
    y = 0
    for b in bands:
        h = cw if b[0] == 'corr' else b[1]['h']
        ys.append((y, y + h + 1))   # (위 벽, 아래 벽)
        y += h + 1
    Htot = y + 1
    corr_idx = [i for i, b in enumerate(bands) if b[0] == 'corr']
    first_c, last_c = corr_idx[0], corr_idx[-1]
    yA0 = ys[first_c][0] + 1          # 첫 통로 첫 줄
    yC1 = ys[last_c][1] - 1           # 마지막 통로 마지막 줄
    # 가로 위치: 엔진실 [0..xE] · 바깥 띠 [xE..xB] · 안쪽 띠 [xE+cw+1 .. xB-cw-1] · 뱃머리 [xB..xB+bowW+1]
    xE = We + 1
    cut0 = spec.get('chamfer', 2)
    for bi, b in enumerate(bands):
        if b[0] == 'room':
            for r in b[1]['rooms']:
                hh = b[1]['h']
                hres = {(r['hatch'], hh - 1), (r['hatch'], hh - 2)} if r.get('hatch') is not None else set()
                r['w'] = fit_w(r['items'], hh, room_doors(r, b[1]['doors']), r['w'], hres)
            ci = [i for i, x in enumerate(bands) if x[0] == 'corr']
            if bi < ci[0] or bi > ci[-1]: b[1]['rooms'][-1]['w'] += cut0   # 앞 끝 모서리를 깎을 자리
    for r in bow['rooms']: r['h'] = fit_h(r['items'], bow['w'], ['W'], r['h'])
    D = 0
    for i, b in enumerate(bands):
        if b[0] != 'room': continue
        outer = i < first_c or i > last_c
        need = band_need(b[1]['rooms'])
        D = max(D, need - 1 if outer else need - 1 + 2 * cw + 2)
        if "--bands" in sys.argv: print(spec["key"], i, "outer" if outer else "inner", need - 1 if outer else need - 1 + 2 * cw + 2)
    D = max(D, spec.get('min_len', 0))
    xB = xE + D
    bowW = bow['w']
    Wtot = xB + bowW + 2
    s = Ship(spec['key'], Wtot, Htot)

    # 엔진실
    s.rect(0, 0, xE, Htot - 1)
    eng = s.room('e', None, 1, 1, We, Htot - 2, ['E'], spec['engine']['items'])
    # 띠
    for i, b in enumerate(bands):
        top, bot = ys[i]
        if b[0] == 'corr': continue
        bd = b[1]
        outer = i < first_c or i > last_c
        a, z = (xE, xB) if outer else (xE + cw + 1, xB - cw - 1)
        widths = spread([r['w'] for r in bd['rooms']], z - a + 1, [r.get('grow', False) for r in bd['rooms']])
        x = a
        for r, w in zip(bd['rooms'], widths):
            s.rect(x, top, x + w + 1, bot)
            s.room(r['label'], r.get('kind'), x + 1, top + 1, x + w, bot - 1, room_doors(r, bd['doors']), r['items'], r.get('reserve', ()))
            if r.get('hatch'): s.rooms[-1]['hatch'] = r['hatch']
            s.rooms[-1]['link'] = r.get('link', False)   # 앞 방과 사이 문 (주방 ↔ 식당 같은 짝)
            x += w + 1
    # 통로: 가로 띠 + 세로 두 줄 (고리)
    for i in corr_idx:
        top, bot = ys[i]
        s.floor(xE + 1, top + 1, xB - 1, bot - 1)
    for y in range(yA0, yC1 + 1):
        for x in list(range(xE + 1, xE + cw + 1)) + list(range(xB - cw, xB)):
            s.g[y][x] = '.'
    # 가로 통로의 위 · 아래 벽 (세로 통로 칸 제외)
    for i in corr_idx:
        top, bot = ys[i]
        for yy in (top, bot):
            for x in range(xE, xB + 1):
                if s.g[yy][x] == ' ': s.g[yy][x] = '#'
    # 뱃머리
    by0, by1 = yA0 - 1, yC1 + 1
    hs = [r['h'] for r in bow['rooms']]
    hs = spread(hs, by1 - by0 + 1, [r.get('grow', False) for r in bow['rooms']])
    y = by0
    for r, h in zip(bow['rooms'], hs):
        s.rect(xB, y, xB + bowW + 1, y + h + 1)
        s.room(r['label'], r.get('kind'), xB + 1, y + 1, xB + bowW, y + h, ['W'], r['items'])
        y += h + 1
    # 모서리 깎기 (엔진실 뒤 · 뱃머리 앞 · 바깥 띠 앞 끝)
    cut = spec.get('chamfer', 2)
    def carve(cells):
        for (x, y) in cells:
            if 0 <= x < Wtot and 0 <= y < Htot: s.g[y][x] = ' '
    for k in range(cut):
        for j in range(cut - k):
            carve([(k, j), (k, Htot - 1 - j)])
            carve([(Wtot - 1 - k, by0 + j), (Wtot - 1 - k, by1 - j)])
            carve([(xB - k, j), (xB - k, Htot - 1 - j)])
    # 깎인 자리 다시 벽으로
    for y in range(Htot):
        for x in range(Wtot):
            if s.g[y][x] == '.' and any(0 <= y + dy < Htot and 0 <= x + dx < Wtot and s.g[y + dy][x + dx] == ' '
                                        for dy in (-1, 0, 1) for dx in (-1, 0, 1)) or s.g[y][x] == '.' and (x in (0, Wtot - 1) or y in (0, Htot - 1)):
                s.g[y][x] = '#'

    # 차압 문 자리 (통로를 구획으로 나눈다)
    bulk = []
    nb = spec.get('bulkheads', 0)
    for i in corr_idx:
        top, bot = ys[i]
        for k in range(nb):
            bx = xE + cw + 1 + (k + 1) * (xB - xE - 2 * cw - 2) // (nb + 1)
            bulk.append((bx, top + 1, bot - 1))
    bulk_x = {b[0] for b in bulk}

    # 문
    def door(x, y):
        if s.g[y][x] != '#': fail(f'문 자리가 벽이 아니다 {x},{y} {s.g[y][x]!r}')
        s.g[y][x] = '+'
    def pick_x(r, avoid=()):
        xs = [x for x in range(r['x0'], r['x1'] + 1) if x not in bulk_x and x not in avoid and x - 1 not in bulk_x and x + 1 not in bulk_x]
        mid = (r['x0'] + r['x1']) // 2
        return min(xs, key=lambda x: (abs(x - mid), x))
    corr_rows = [ys[i][0] + 1 for i in corr_idx]
    for r in s.rooms:
        for d in r['doors']:
            if d == 'N':
                x = pick_x(r); door(x, r['y0'] - 1); r['reserve'].add((x, r['y0']))
            elif d == 'S':
                x = pick_x(r); door(x, r['y1'] + 1); r['reserve'].add((x, r['y1']))
            elif d == 'E':   # 엔진실 → 통로마다
                for cy in corr_rows:
                    door(r['x1'] + 1, cy); r['reserve'].add((r['x1'], cy))
            elif d == 'W':   # 뱃머리 → 세로 통로
                ry = [y for y in range(r['y0'], r['y1'] + 1) if s.g[y][r['x0'] - 1] == '#' and s.g[y][r['x0'] - 2] == '.']
                y = min(ry, key=lambda y: abs(y - (r['y0'] + r['y1']) // 2))
                door(r['x0'] - 1, y); r['reserve'].add((r['x0'], y))
        if r.get('link'):   # 앞 방과 사이 문: 둘 다 통로 쪽 빈 줄에서
            prev = [q for q in s.rooms if q['x1'] == r['x0'] - 2 and q['y0'] == r['y0'] and q['y1'] == r['y1']]
            if prev:
                yy = r['y1'] if 'S' in r['doors'] else r['y0']
                door(r['x0'] - 1, yy); r['reserve'].add((r['x0'], yy)); prev[0]['reserve'].add((r['x0'] - 2, yy))
        if r['hatch']:     # 에어락 바깥 해치 (아래 선체)
            x = r['x0'] + r['hatch']
            door(x, r['y1'] + 1); r['reserve'].update({(x, r['y1']), (x, r['y1'] - 1)})
    for (bx, y0, y1) in bulk:
        for y in range(y0, y1 + 1): s.g[y][bx] = '#'
        s.g[y0][bx] = '+'

    # 방마다: 통로 쪽 줄과 왼쪽 세로 줄은 비운다 (설비마다 손 닿는 바닥이 이어지게) → 설비 채우기
    for r in s.rooms:
        x0, y0, x1, y1 = r['x0'], r['y0'], r['x1'], r['y1']
        ds = r['doors']
        res = r['reserve'] = spine(x0, y0, x1, y1, ds, r['reserve'])
        placed = pack(lambda x, y: s.g[y][x], x0, y0, x1, y1, ds, res, r['items'])
        if placed is None: print("\n".join("".join(s.g[y][x0 - 1:x1 + 2]) for y in range(y0 - 1, y1 + 2))); fail(f"{spec['key']} {r['label']}/{r['kind']} 설비 자리 없음 (방 {x1 - x0 + 1}×{y1 - y0 + 1})")
        for x, y, ch in placed: s.g[y][x] = ch
        # 이름표: 비워 둔 줄의 왼쪽 위
        lab = r['label']
        if r['kind']:
            if r['kind'] not in s.legend:
                s.legend[r['kind']] = LEGEND_POOL[len(s.legend)]
            lab = s.legend[r['kind']]
        spots = sorted(res, key=lambda c: (c[1], c[0])) if 'S' not in ds else sorted(res, key=lambda c: (-c[1], c[0]))
        spots = [c for c in spots if x0 <= c[0] <= x1 and y0 <= c[1] <= y1 and s.g[c[1]][c[0]] == '.']
        lx, ly = spots[0]
        s.g[ly][lx] = lab
    # 통로 구획마다 이름표
    seen = set()
    for y in range(Htot):
        for x in range(Wtot):
            if s.g[y][x] == '.' and (x, y) not in seen:
                comp, st = [], [(x, y)]
                seen.add((x, y))
                while st:
                    c = st.pop(); comp.append(c)
                    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        n = (c[0] + dx, c[1] + dy)
                        if n not in seen and 0 <= n[0] < Wtot and 0 <= n[1] < Htot and s.g[n[1]][n[0]] not in '#+ ':
                            seen.add(n); st.append(n)
                labs = [c for c in comp if s.g[c[1]][c[0]].islower() or s.g[c[1]][c[0]] in LEGEND_POOL]
                if not labs:
                    fl = sorted(c for c in comp if s.g[c[1]][c[0]] == '.')
                    s.g[fl[0][1]][fl[0][0]] = 'c'
    return s, dict(xE=xE, xB=xB, yA0=yA0, yC1=yC1, W=Wtot, H=Htot)

# ───────────────────────── 검사 ─────────────────────────
def check(s, meta):
    g = s.g
    Ht, Wt = s.H, s.W
    def kind(x, y): return g[y][x] if 0 <= x < Wt and 0 <= y < Ht else ' '
    # 방 영역: 이름표에서 바닥 따라
    labels = [(x, y) for y in range(Ht) for x in range(Wt) if (g[y][x].islower() or g[y][x] in LEGEND_POOL) and g[y][x] not in ' #+.']
    owner = {}
    for (lx, ly) in labels:
        st = [(lx, ly)]
        if (lx, ly) in owner: fail(f'이름표 둘 {lx},{ly}')
        owner[(lx, ly)] = (lx, ly)
        while st:
            c = st.pop()
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                n = (c[0] + dx, c[1] + dy)
                ch = kind(*n)
                if ch in '# +': continue
                if n in owner:
                    if owner[n] != (lx, ly): fail(f'방 둘이 붙었다 {n}')
                    continue
                owner[n] = (lx, ly); st.append(n)
    for y in range(Ht):
        for x in range(Wt):
            if g[y][x] not in '# +' and (x, y) not in owner: fail(f'주인 없는 바닥 {x},{y} {g[y][x]}')
    for y in range(Ht):
        for x in range(Wt):
            if g[y][x] != '+': continue
            v = kind(x, y - 1) not in '#+' and kind(x, y + 1) not in '#+'
            h = kind(x - 1, y) not in '#+' and kind(x + 1, y) not in '#+'
            if not (v or h): fail(f'{s.key} 문이 두 공간을 잇지 않는다 {x},{y}')
    # 선체 벽: 우주에 (8방향) 닿은 벽
    hull = {(x, y) for y in range(Ht) for x in range(Wt) if g[y][x] in '#+' and any(kind(x + dx, y + dy) == ' ' for dx in (-1, 0, 1) for dy in (-1, 0, 1))}
    inv = {}
    for c, o in owner.items(): inv.setdefault(o, []).append(c)
    crit = {'r', 'p'} | {s.legend.get('ComputerRoom')}
    for o, cells in inv.items():
        lab = g[o[1]][o[0]]
        if lab in crit:
            if any((c[0] + dx, c[1] + dy) in hull for c in cells for dx in (-1, 0, 1) for dy in (-1, 0, 1)):
                fail(f'{s.key} 중요한 방 {lab} 이 선체에 닿는다')
    # 주컴퓨터실이 가운데 근처
    xs = [x for y in range(Ht) for x in range(Wt) if g[y][x] != ' ']
    cx, cy = (min(xs) + max(xs)) / 2, (Ht - 1) / 2
    cr = [o for o in inv if g[o[1]][o[0]] == s.legend.get('ComputerRoom')][0]
    ccx = sum(c[0] for c in inv[cr]) / len(inv[cr]); ccy = sum(c[1] for c in inv[cr]) / len(inv[cr])
    return dict(rooms=len(inv), kinds=len({g[o[1]][o[0]] for o in inv}), comp_dx=round((ccx - cx) / Wt, 3), comp_dy=round((ccy - cy) / Ht, 3))

# ───────────────────────── 방 묶음 ─────────────────────────
def Rm(label, items, w, kind=None, grow=False, doors=None, h=None, hatch=None, link=False):
    r = dict(label=label, items=items, w=w, kind=kind, grow=grow, link=link)
    if doors: r['doors'] = doors
    if h: r['h'] = h
    if hatch is not None: r['hatch'] = hatch
    return r

def Sp(kind, items, w, grow=False, doors=None, h=None, link=False):
    return Rm('?', items, w, kind=kind, grow=grow, doors=doors, h=h, link=link)

def band(h, doors, rooms): return ('room', dict(h=h, doors=doors, rooms=rooms))
CORR = ('corr',)

# ── 제비호 (4인 · 소형): 꼭 필요한 방만. 통로는 한 칸 폭, 방은 좁다. 냉동 창고의 저장 식량이 수경 재배를 받쳐 준다.
KESTREL = dict(key='Kestrel', crew=4, corr=1, chamfer=2,
    engine=dict(w=5, items=[E, E, C]),
    bands=[
        band(4, ['S'], [Rm('s', [K, K3], 4), Rm('k', [P, P, C], 6), Rm('f', [G, G, G], 10, grow=True), Sp('Freezer', [F, K], 5), Rm('q', many(B, 4), 8)]),
        CORR,
        band(5, ['N', 'S'], [Rm('r', [R3, C], 5), Rm('l', [U, O, O], 8), Sp('ComputerRoom', [I, C, C], 5), Rm('p', [X, Z, Y, Y], 7), Rm('h', [M, K3], 4)]),
        CORR,
        band(4, ['N'], [Rm('w', [H, W, N, K3], 9), Rm('s', [K, K], 5, link=True), Rm('a', [L, L, Q], 6, hatch=4), Rm('j', [V, F, K], 7), Rm('m', [D, D, TSET], 6, grow=True, link=True), Rm('g', [S, S, T], 4, link=True)]),
    ],
    bow=dict(w=5, rooms=[Rm('o', [A, C], 0, h=3), Rm('b', [C, C, S, S], 0, h=4, grow=True)]))

# ── 미리내호 (6인 · 기본): 소형에 휴게실 · 방사선 대피소 · 조류 배양실 · 선외 준비실. 운동은 휴게실에서 (몸이 굳으면 창고 절반을 땀방으로 고친다).
MIRINAE = dict(key='Mirinae', crew=6, corr=2, chamfer=2,
    engine=dict(w=5, items=[E, E, C]),
    bands=[
        band(5, ['S'], [Rm('s', [K, K, K3], 10), Rm('k', [P, P, C], 6), Rm('f', [G, G, G, G], 10, grow=True), Sp('Freezer', [F, K, K], 6), Rm('j', [V, F, K, T], 7, link=True), Rm('m', [D, D, TSET, TSET], 9, grow=True, link=True)]),
        CORR,
        band(5, ['N', 'S'], [Rm('r', [R3, C, C], 6), Rm('l', [U, O, O], 8), Sp('ComputerRoom', [I, C, C, K3], 7), Rm('p', [X, Z, Y, Y], 7), Sp('Shelter', [K, K, S, S], 6)]),
        CORR,
        band(5, ['N'], [Rm('w', [H, W, N, K3], 9), Rm('a', [L, L, Q, Q], 6, hatch=4), Sp('EvaPrep', [L, K], 5, link=True), Sp('AlgaeLab', [G, G, U], 6), Rm('h', [M, M, K3], 6), Rm('q', many(B, 6), 12), Rm('g', [SOFA, S, S], 7)]),
    ],
    bow=dict(w=6, rooms=[Rm('o', [A, C], 0, h=3), Rm('b', [C, C, C, S, S], 0, h=6, grow=True)]))

# ── 한빛호 (12인 · 중형): 통로 고리 하나를 차압 문 하나로 두 구획. 수경 · 버섯 · 조류로 식량원이 셋, 연료전지 · 펌프실 · 항법실 · 재활용실 · 소화 설비실이 붙는다 (운동은 휴게실 — 몸이 굳으면 창고를 고친다).
HANBIT = dict(key='Hanbit', crew=12, corr=2, chamfer=3, bulkheads=1,
    engine=dict(w=5, items=[E, E, E, C]),
    bands=[
        band(6, ['S'], [Rm('s', [K, K, K], 12), Rm('k', [P, P, C], 6), Sp('PumpRoom', [P, P], 6, link=True), Rm('f', many(G, 8), 10), Sp('Freezer', [F, K, K], 6),
                        Rm('j', [V, V, F, F, K], 9, link=True), Rm('m', [D, D, D, TSET, TSET, TSET], 11, link=True), Sp('Observatory', [S, S, C, SOFA], 7)]),
        CORR,
        band(6, ['N'], [Rm('q', many(B, 8), 9), Sp('QuietQuarters', many(B, 4), 5), Sp('WaterPlant', [U, U], 6), Sp('MushroomFarm', [G, G, K3], 6),
                        Sp('AlgaeLab', [G, G, U], 6), Sp('Quarantine', [M, C], 5), Sp('PartsPrep', [W, K3], 6), Sp('Laundry', [K, K], 5)]),
        band(6, ['S'], [Rm('r', [R4, C, C], 7), Sp('FuelCell', [Z, Z, C], 6), Rm('l', [U, U, O, O, O, O], 8), Sp('ComputerRoom', [I, C, C, K3], 7),
                        Rm('p', [X, Z, Y, Y, Y, Y], 8), Sp('Substation', [X, K3], 6), Sp('BatteryRoom', [Y, Y], 5), Sp('Shelter', [K, K, S, S, S], 7)]),
        CORR,
        band(6, ['N'], [Rm('w', [H, W, W, N, K3], 10), Sp('Recycling', [N, K3], 6, link=True), Sp('DroneBay', [Q, Q, K], 6), Rm('a', [L, L, L, Q, Q], 8, hatch=6), Rm('h', [M, M, M, K3], 7),
                        Rm('g', [SOFA, SOFA, S], 9), Sp('SuppressionRoom', [K, K], 5), Sp('Cargo', many(K, 4), 7)]),
    ],
    bow=dict(w=7, rooms=[Rm('o', [A, C, C], 0, h=4), Rm('b', [C, C, C, S, S, S], 0, h=6, grow=True), Sp('Navigation', [C, C, K3], 0, h=5)]))

# ── 은하호 (20인 · 대형): 다양 · 고성능. 통로 고리 둘(위 · 아래)을 가운데 통로가 잇고, 차압 문 둘로 세 구획.
#    서버실 · 보안실 · 교정실 · 연구실 · 축열실 · 단백질 농장 · 정원 · 관측실 · 개인 선실 · 예비 함교.
EUNHA = dict(key='Eunha', crew=20, corr=2, chamfer=3, bulkheads=2,
    engine=dict(w=5, items=[E, E, E, E, C]),
    bands=[
        band(6, ['S'], [Sp('Cargo', many(K, 6), 8), Rm('k', [P, P, P, C], 9), Sp('PumpRoom', [P, P, P], 9), Rm('f', many(G, 7), 10), Rm('f', many(G, 7), 10),
                        Sp('Observatory', [S, S, S, C, SOFA], 8), Sp('Garden', [G, G, S, S, S], 8), Sp('EscapeBay', [K, K], 5)]),
        CORR,
        band(6, ['N'], [Rm('q', many(B, 8), 9), Rm('q', many(B, 4), 5), Sp('PrivateCabins', many(B, 2) + [K3], 5), Sp('PrivateCabins', many(B, 2) + [K3], 5),
                        Sp('MeetingRoom', [T, T, S, S, S, S], 7), Sp('Chapel', [S, S, S, S], 5)]),
        band(5, ['S'], [Sp('HeatStorage', [K, K, C], 7), Sp('ServerRoom', [K3, K3, K3, C, C], 8), Sp('Security', [C, C, C, K3], 6), Sp('Calibration', [W, C], 6),
                        Sp('Lab', [W, C, K3], 7), Sp('ElectronicsLab', [W, K3], 6), Sp('SuppressionRoom', [K, K], 5), Sp('SeedVault', [K, K], 5)]),
        CORR,
        band(7, ['N', 'S'], [Rm('r', [R5, C, C], 8), Sp('FuelCell', [Z, Z, C], 6), Rm('l', [U, U, O, O, O, O], 8), Sp('ComputerRoom', [I, C, C, C, K3], 8),
                             Rm('p', [X, Z, Y, Y, Y, Y], 8), Sp('Substation', [X, K3], 6), Sp('BatteryRoom', [Y, Y, Y, Y], 7), Sp('HvacRoom', [O, K3], 5)]),
        CORR,
        band(5, ['N'], [Sp('Shelter', [K, K, S, S, S, S], 7), Sp('WaterPlant', [U, U], 6), Sp('MushroomFarm', [G, G, K3], 6), Sp('AlgaeLab', [G, G, G], 6),
                        Sp('ProteinFarm', [G, G, U], 6), Sp('Recycling', [N, K3], 6), Sp('DroneBay', [Q, Q, K], 6)]),
        band(6, ['S'], [Sp('Quarantine', [M, M, C], 6), Sp('Triage', [M, M], 5, link=True), Sp('QuietQuarters', many(B, 4), 5), Sp('Laundry', [K, K], 5),
                        Sp('Freezer', [F, K, K, K], 7), Rm('j', [V, V, V, V, F, F, F, F, K], 11, link=True), Rm('m', [D, D, D, D, D, TSET, TSET, TSET, TSET], 12, link=True)]),
        CORR,
        band(6, ['N'], [Rm('w', [H, W, W, N, N, K3], 11), Rm('a', [L, L, L, Q, Q, Q], 9, hatch=7), Sp('EvaPrep', [L, K], 5, link=True), Rm('h', [M, M, M, M, K3], 8),
                        Rm('g', [SOFA, SOFA, S], 8), Sp('Gym', [ROWS2, ROWS2], 6, link=True), Rm('s', [K, K, K, K], 8)]),
    ],
    bow=dict(w=8, rooms=[Rm('o', [A, C, C], 0, h=5), Rm('b', [C, C, C, C, S, S, S, S], 0, h=8, grow=True), Sp('Navigation', [C, C, K3], 0, h=5), Sp('BackupBridge', [C, C, S], 0, h=5)]))

# ── 천마호 (30인 · 초대형): 호화 · 세대선급. 극장 · 학교 · 명상실 · 원심 거주구 · 물벽 선실 · 고압 치료실 · 셔틀 격납고 · 도킹 포트 · 크레인 조종실,
#    차압 문 셋으로 네 구획.
CHEONMA = dict(key='Cheonma', crew=30, corr=2, chamfer=3, bulkheads=3,
    engine=dict(w=5, items=[E, E, E, E, E, C]),
    bands=[
        band(6, ['S'], [Sp('ShuttleBay', [K, K, K], 8), Sp('Cargo', many(K, 6), 8), Rm('k', [P, P, P, P, C], 12), Sp('PumpRoom', [P, P, P, P], 12),
                        Rm('f', many(G, 10), 15), Rm('f', many(G, 10), 15), Sp('Observatory', [S, S, S, S, C, SOFA], 9), Sp('Theater', many(ROWS2, 5) + [C], 10),
                        Sp('DockingBay', [L, K], 5)]),
        CORR,
        band(6, ['N'], [Rm('q', many(B, 9), 10), Rm('q', many(B, 9), 10), Sp('WaterWallCabin', many(B, 4), 5), Sp('PrivateCabins', many(B, 2) + [K3], 5),
                        Sp('PrivateCabins', many(B, 2) + [K3], 5), Sp('Garden', [G, G, G, S, S], 10), Sp('Gym', [ROWS2, ROWS2, ROWS2], 8), Rm('g', [SOFA, SOFA, S, S], 9),
                        Sp('Meditation', [S, S], 4)]),
        band(5, ['S'], [Sp('GasStorage', [K, K], 5), Sp('HeatStorage', [K, K, C], 7), Sp('ServerRoom', [K3, K3, K3, K3, C, C], 9), Sp('Security', [C, C, C, K3], 6),
                        Sp('Calibration', [W, C], 6), Sp('Lab', [W, W, C, K3], 9), Sp('ElectronicsLab', [W, K3], 6), Sp('MeetingRoom', [T, T, S, S, S, S, S, S], 8),
                        Sp('Archive', [K, K, K], 8), Sp('School', [T, T, S, S, S, S], 8), Sp('Chapel', [S, S, S, S], 5)]),
        CORR,
        band(8, ['N', 'S'], [Sp('PropellantTank', [K, K], 5), Rm('r', [R6, C, C], 9), Sp('FuelCell', [Z, Z, Z, C], 6), Rm('l', [U, U, O, O, O, O, O], 9),
                             Sp('ComputerRoom', [I, C, C, C, C, K3], 8), Rm('p', [X, Z, Y, Y, Y, Y, Y, Y], 9), Sp('Substation', [X, K3], 5),
                             Sp('BatteryRoom', [Y, Y, Y, Y], 5), Sp('HvacRoom', [O, O, K3], 5), Sp('Centrifuge', [ROWS2, ROWS2, S], 7)]),
        CORR,
        band(5, ['N'], [Sp('Shelter', [K, K, S, S, S, S, S, S], 9), Sp('WaterPlant', [U, U, U], 8), Sp('MushroomFarm', [G, G, G, K3], 9), Sp('AlgaeLab', [G, G, G], 9),
                        Sp('ProteinFarm', [G, G, G, U], 9), Sp('SeedVault', [K, K], 5), Sp('SuppressionRoom', [K, K], 5), Sp('Morgue', [K], 4), Sp('CraneControl', [C, C], 4),
                        Sp('WeldingShop', [W, K3], 6), Sp('Crusher', [N, K3], 6)]),
        band(6, ['S'], [Sp('Quarantine', [M, M, C], 6), Sp('QuarantineLock', [L], 4, link=True), Sp('Hyperbaric', [M, C], 4), Sp('Triage', [M, M, M], 5), Sp('Decon', [L], 4),
                        Sp('QuietQuarters', many(B, 4), 5), Sp('Laundry', [K, K], 5), Sp('Freezer', [F, F, K, K, K], 8), Rm('j', [V] * 5 + [F] * 5 + [K], 13, link=True),
                        Rm('m', [D] * 8 + many(TSET, 6), 16, link=True)]),
        CORR,
        band(6, ['N'], [Rm('w', [H, W, W, W, N, N, K3], 13), Sp('Recycling', [N, K3], 6, link=True), Sp('RobotBay', [W, K3, K], 7), Sp('DroneBay', [Q, Q, K], 6),
                        Rm('a', [L, L, L, Q, Q, Q], 9, hatch=7), Sp('EvaPrep', [L, K], 5, link=True), Rm('h', [M] * 6 + [K3], 10), Rm('s', [K] * 6, 10)]),
    ],
    bow=dict(w=9, rooms=[Rm('o', [A, C, C], 0, h=5), Rm('b', [C, C, C, C, C, S, S, S, S, S], 0, h=9, grow=True), Sp('Navigation', [C, C, K3], 0, h=5),
                         Sp('BackupBridge', [C, C, S, S], 0, h=6)]))

SHIPS = [(KESTREL, '제비호'), (MIRINAE, '미리내호'), (HANBIT, '한빛호'), (EUNHA, '은하호'), (CHEONMA, '천마호')]

def to_text(s):
    lines = [''.join(row).rstrip() for row in s.g]
    leg = [f'@{ch}={k}' for k, ch in s.legend.items()]
    return '\n'.join(leg + lines)

if __name__ == '__main__':
    out = ['// 자동 생성: tools/shipgen/gen_ships.py (v16.22 기본 배 다섯 척 · 크기별 등급). 손으로 고치지 말고 생성기를 고친다.',
           '// 뼈대: 뒤 엔진실 · 바깥 띠(화물 · 창고 · 냉각 · 재배 · 침실 · 정비 — 완충) · 통로 고리 · 안쪽 심장부(원자로 · 배전 · 주컴퓨터실 한가운데) · 앞 뱃머리.',
           'namespace ShipSim.Core;', '', 'public static partial class ShipBlueprints', '{']
    for spec, kname in SHIPS:
        s, meta = build(spec)
        if "--show" in sys.argv and spec["key"] in sys.argv: print(to_text(s))
        info = check(s, meta)
        print(f"{kname:6} {spec['crew']:>2}인 {meta['W']}×{meta['H']} 방 {info['rooms']} 종류 {info['kinds']} 주컴퓨터실 치우침 {info['comp_dx']:+.3f},{info['comp_dy']:+.3f}")
        if '--show' in sys.argv and spec['key'] in sys.argv: print(to_text(s))
        out.append(f"    public const string {spec['key']}Name = \"{kname}\";")
        out.append(f"    public const string {spec['key']} = \"\"\"")
        out.append(to_text(s))
        out.append('""";')
        out.append('')
    out.append('}')
    if '--dry' not in sys.argv:
        path = os.path.join(os.path.dirname(__file__), '..', '..', 'game', 'src', 'Core', 'ShipTemplates.cs')
        open(path, 'w', encoding='utf-8').write('\n'.join(out) + '\n')
