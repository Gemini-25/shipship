#!/usr/bin/env python3
"""
모양 있는 배 생성기 (v19 새 배 — 화살촉 · 귀상어 · 보름달 · 삼지창 · 가오리 · 고래).

기존 배(기본 다섯 척 · 손으로 그린 여섯 척)는 그대로 두고, 선체 바깥 모양이 저마다 다른 배를 더한다.
만드는 순서:
  ① 통로(바닥 띠)와 방(벽 테두리 + 바닥)을 칸 좌표로 놓는다 — 방은 이웃과 벽을 나눠 쓴다.
  ② 실루엣 다각형(여러 개 합집합)으로 깎는다: 칸 가운데가 다각형 밖이면 우주. 남은 바닥에 닿은 우주 칸은 벽(외판)이 된다.
     → 뱃머리 · 날개 · 원반 테두리가 비스듬하거나 둥글게 깎이고, 그 자리의 방은 사다리꼴 · 세모꼴이 된다.
  ③ 문: 방마다 정한 쪽(N · S · E · W)의 벽 중 바깥이 통로인 칸, 짝 방 사이 문, 에어락 바깥 해치, 차압 문.
  ④ 설비 채우기: 문에서 먼 칸부터, 설비끼리 한 칸 띄우고 — 놓을 때마다 모든 설비에 문에서 걸어서 손이 닿는지 확인한다.
  ⑤ 검사: 주인 없는 바닥 · 문이 두 공간을 잇는지 · 중요한 방(원자로 · 배전 · 주컴퓨터실)이 선체에 닿지 않는지 · 함교에서 모든 방에 길.
배관 규칙(Piping.Build): 냉각실은 위 선체에 닿고(펌프 위 바깥이 방열판), 원자로는 냉각실 왼쪽 아래.
엔진실은 맨 뒤(왼쪽) — 엔진 코어를 뒷벽에 붙인다 (노즐은 그 뒤 선체 바깥에 그린다).
출력: game/src/Core/ShipShapesAscii.cs (손으로 고치지 말고 이 생성기를 고친다). 내력은 ShipShapes.cs (손으로 쓴다).
  python3 tools/shipgen/gen_shapes.py            → 생성 + 검사 + 파일 쓰기
  python3 tools/shipgen/gen_shapes.py --dry      → 검사만
  python3 tools/shipgen/gen_shapes.py --png DIR  → 칸 그림 미리보기 (PIL)
  python3 tools/shipgen/gen_shapes.py --show Key → 설계도 출력
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gen_ships import (R3, R4, R5, R6, P, X, Z, Y, O, U, G, F, V, D, B, M, W, N, H, L, Q, K, K3, C, I, A, S, T, E,
                       TSET, SOFA, ROWS2, many, LEGEND_POOL)

BASE_LABELS = set('cberkplwsjmfaqhgo')
DIRS4 = ((1, 0), (-1, 0), (0, 1), (0, -1))
SIDE = {'N': (0, -1), 'S': (0, 1), 'E': (1, 0), 'W': (-1, 0)}
G2 = ['GG']
K2 = ['K', 'K']
BB = ['BB']           # 가로 침대 (좁은 방)
KH = ['KK']           # 가로 선반 (낮은 방)
BED_ROW = ['B.B.B']   # 침대 셋 한 줄


class Fail(Exception):
    pass


def fail(msg):
    raise Fail(msg)


# ───────────────────────── 실루엣 ─────────────────────────
def inside_poly(px, py, poly):
    """짝-홀 규칙 (다각형 꼭짓점 목록)."""
    n, c = len(poly), False
    j = n - 1
    for i in range(n):
        xi, yi = poly[i]; xj, yj = poly[j]
        if (yi > py) != (yj > py) and px < (xj - xi) * (py - yi) / (yj - yi) + xi:
            c = not c
        j = i
    return c


def mirror(half, cy):
    """위쪽 반 윤곽 (x, 높이 위) 목록을 위아래 대칭 다각형으로: half 는 뒤에서 앞으로, 높이는 축(cy)에서 위로 잰 값."""
    top = [(x, cy - h) for x, h in half]
    bot = [(x, cy + h) for x, h in reversed(half)]
    return top + bot


def ellipse(cx, cy, rx, ry, n=48):
    import math
    return [(cx + rx * math.cos(2 * math.pi * k / n), cy + ry * math.sin(2 * math.pi * k / n)) for k in range(n)]


# ───────────────────────── 격자 ─────────────────────────
class Canvas:
    def __init__(self, key, name, W, H):
        self.key, self.name = key, name
        self.W, self.H = W, H
        self.g = [[' '] * W for _ in range(H)]
        self.rooms = []
        self.corr = set()
        self.legend = {}
        self.links = []
        self.bulks = []
        self.polys = []
        self.cuts = []
        self.crit_hull_ok = False   # 모양 때문에 중요한 방이 선체에 닿아도 된다 (경고만)
        self.warn = []

    def at(self, x, y):
        return self.g[y][x] if 0 <= x < self.W and 0 <= y < self.H else ' '

    def put(self, x, y, ch):
        if not (0 <= x < self.W and 0 <= y < self.H): fail(f'{self.key} 격자 밖 {x},{y}')
        self.g[y][x] = ch

    # 통로: 바닥 사각형 (벽은 나중에 우주와 닿는 곳만 · 방 벽은 방이 그린다)
    def corridor(self, x0, y0, x1, y1):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                if self.at(x, y) == '#': fail(f'{self.key} 통로가 벽을 덮는다 {x},{y}')
                self.put(x, y, '.')
                self.corr.add((x, y))

    # 방: 안쪽 좌표 (x0..x1, y0..y1) · 벽은 테두리 (이웃 방과 나눠 쓴다)
    def room(self, lab, x0, y0, x1, y1, items, doors=('S',), kind=None, hatch=None, prefer=None, link=None, tag=None):
        for y in range(y0 - 1, y1 + 2):
            for x in range(x0 - 1, x1 + 2):
                edge = x in (x0 - 1, x1 + 1) or y in (y0 - 1, y1 + 1)
                cur = self.at(x, y)
                if edge:
                    if (x, y) in self.corr: fail(f'{self.key} 방 {lab}/{kind} 벽이 통로를 덮는다 {x},{y}')
                    if cur == '.': fail(f'{self.key} 방 {lab}/{kind} 벽이 다른 방 바닥을 덮는다 {x},{y}')
                    self.put(x, y, '#')
                else:
                    if cur != ' ': fail(f'{self.key} 방 {lab}/{kind} 이 다른 것과 겹친다 {x},{y} {cur!r}')
                    self.put(x, y, '.')
        r = dict(lab=lab, kind=kind, x0=x0, y0=y0, x1=x1, y1=y1, items=list(items), doors=list(doors), hatch=hatch,
                 prefer=prefer, link=link, tag=tag, door_in=[], cells=None)
        self.rooms.append(r)
        return r

    @staticmethod
    def _fit(specs, start, end, key):
        """end(마지막 벽)까지 꽉 채우도록 남는 칸을 나눠 준다 ('grow' 표시한 방부터)."""
        if end is None: return [sp[1] for sp in specs]
        ws = [sp[1] for sp in specs]
        extra = end - (start + sum(w + 1 for w in ws))
        if extra < 0: fail(f'{key} 줄이 넘친다 ({-extra}칸 · 첫 방 {specs[0][0]})')
        idx = [i for i, sp in enumerate(specs) if len(sp) > 3 and sp[3].get('grow')] or list(range(len(ws)))
        k = 0
        while extra > 0: ws[idx[k % len(idx)]] += 1; extra -= 1; k += 1
        return ws

    def row(self, y0, y1, x0, specs, doors=('S',), to=None):
        """가로로 붙은 방 줄: specs = [(라벨|종류, 안쪽 폭, 설비, {옵션})] — 이웃끼리 벽 하나를 나눈다. 다음 x(벽) 를 돌려준다.
        to: 마지막 벽 x (남는 칸은 방에 나눠 준다)."""
        x = x0
        out = []
        ws = self._fit(specs, x0, to, self.key)
        for sp, w in zip(specs, ws):
            lab, items = sp[0], sp[2]
            opt = sp[3] if len(sp) > 3 else {}
            kind = None
            if lab not in BASE_LABELS:
                kind, lab = lab, '?'
            r = self.room(lab, x + 1, y0, x + w, y1, items, doors=opt.get('doors', doors), kind=kind, hatch=opt.get('hatch'),
                          prefer=opt.get('prefer'), link=opt.get('link'), tag=opt.get('tag'))
            out.append(r)
            x += w + 1
        return x, out

    def col(self, x0, x1, y0, specs, doors=('W',), to=None):
        """세로로 붙은 방 줄 (위에서 아래로). to: 마지막 벽 y."""
        y = y0
        out = []
        hs = self._fit(specs, y0, to, self.key)
        for sp, h in zip(specs, hs):
            lab, items = sp[0], sp[2]
            opt = sp[3] if len(sp) > 3 else {}
            kind = None
            if lab not in BASE_LABELS:
                kind, lab = lab, '?'
            r = self.room(lab, x0, y + 1, x1, y + h, items, doors=opt.get('doors', doors), kind=kind, hatch=opt.get('hatch'),
                          prefer=opt.get('prefer'), link=opt.get('link'), tag=opt.get('tag'))
            out.append(r)
            y += h + 1
        return y, out

    def shape(self, *polys):
        self.polys.extend(polys)

    def cut(self, *polys):
        """실루엣에서 도려낼 다각형 (홈 · 노치)."""
        self.cuts.extend(polys)

    def bulkhead(self, x, y0, y1, door_y=None):
        """세로 통로를 가로지르는 차압 문 (x 열, y0..y1)."""
        self.bulks.append(('v', x, y0, y1, door_y))

    def bulkhead_h(self, y, x0, x1, door_x=None):
        self.bulks.append(('h', y, x0, x1, door_x))


# ───────────────────────── 깎기 ─────────────────────────
def carve(s):
    if not s.polys: return
    for y in range(s.H):
        for x in range(s.W):
            if s.g[y][x] == ' ': continue
            px, py = x + 0.5, y + 0.5
            ins = any(inside_poly(px, py, p) for p in s.polys) and not any(inside_poly(px, py, p) for p in s.cuts)
            if not ins:
                s.g[y][x] = ' '
                s.corr.discard((x, y))
    seal(s)


def seal(s):
    """바닥에 (8방향) 닿은 우주 칸을 벽으로 — 선체가 닫힌다."""
    add = []
    for y in range(s.H):
        for x in range(s.W):
            if s.g[y][x] != ' ': continue
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    if s.at(x + dx, y + dy) == '.':
                        add.append((x, y)); break
                else: continue
                break
    for x, y in add:
        if not (0 < x < s.W - 1 and 0 < y < s.H - 1): fail(f'{s.key} 선체가 격자 끝에 닿는다 {x},{y} — 여백을 늘린다')
        s.g[y][x] = '#'
    # 배 안에 갇힌 우주(구멍)가 없어야 한다 — 바깥 테두리에서 우주를 따라 퍼져 닿지 않는 우주 칸
    out = set()
    st = [(x, y) for x in range(s.W) for y in (0, s.H - 1)] + [(x, y) for y in range(s.H) for x in (0, s.W - 1)]
    st = [c for c in st if s.g[c[1]][c[0]] == ' ']
    out.update(st)
    while st:
        c = st.pop()
        for dx, dy in DIRS4:
            n = (c[0] + dx, c[1] + dy)
            if 0 <= n[0] < s.W and 0 <= n[1] < s.H and n not in out and s.g[n[1]][n[0]] == ' ':
                out.add(n); st.append(n)
    holes = [(x, y) for y in range(s.H) for x in range(s.W) if s.g[y][x] == ' ' and (x, y) not in out]
    if holes: fail(f'{s.key} 배 안에 빈 구멍 {len(holes)}칸 (예: {holes[:4]})')
    # 벽에 막혀 떨어져 나간 부스러기 벽(우주와 바닥 어디에도 쓸모없는)은 그대로 둔다


def room_cells(s, r):
    cells = [(x, y) for y in range(r['y0'], r['y1'] + 1) for x in range(r['x0'], r['x1'] + 1) if s.g[y][x] == '.']
    if not cells: fail(f"{s.key} 방 {r['lab']}/{r['kind']} 이 다 깎였다")
    # 한 덩어리인지
    seen, st = {cells[0]}, [cells[0]]
    cs = set(cells)
    while st:
        c = st.pop()
        for dx, dy in DIRS4:
            n = (c[0] + dx, c[1] + dy)
            if n in cs and n not in seen: seen.add(n); st.append(n)
    if len(seen) != len(cells): fail(f"{s.key} 방 {r['lab']}/{r['kind']} 이 깎여 두 쪽이 됐다")
    return cells


# ───────────────────────── 문 ─────────────────────────
def owner_map(s):
    own = {}
    for i, r in enumerate(s.rooms):
        for c in r['cells']: own[c] = i
    return own


def place_doors(s):
    own = owner_map(s)
    # 차압 문 (통로를 가로지르는 벽 + 문 하나)
    for b in s.bulks:
        if b[0] == 'v':
            _, x, y0, y1, dy = b
            ys = [y for y in range(y0, y1 + 1) if (x, y) in s.corr]
            if not ys: fail(f'{s.key} 차압 문 자리에 통로가 없다 x={x}')
            for y in ys: s.g[y][x] = '#'; s.corr.discard((x, y))
            dyy = dy if dy is not None else ys[len(ys) // 2]
            s.g[dyy][x] = '+'
        else:
            _, y, x0, x1, dx = b
            xs = [x for x in range(x0, x1 + 1) if (x, y) in s.corr]
            if not xs: fail(f'{s.key} 차압 문 자리에 통로가 없다 y={y}')
            for x in xs: s.g[y][x] = '#'; s.corr.discard((x, y))
            dxx = dx if dx is not None else xs[len(xs) // 2]
            s.g[y][dxx] = '+'
    for i, r in enumerate(s.rooms):
        cx = (r['x0'] + r['x1']) / 2; cy = (r['y0'] + r['y1']) / 2
        for d in r['doors']:
            if isinstance(d, tuple):       # 정한 자리 (벽 칸)
                x, y = d
                if s.g[y][x] != '#': fail(f"{s.key} {r['lab']}/{r['kind']} 문 자리가 벽이 아니다 {x},{y}")
                s.g[y][x] = '+'
                for dx, dy in DIRS4:
                    if own.get((x + dx, y + dy)) == i: r['door_in'].append((x + dx, y + dy))
                continue
            vx, vy = SIDE[d]
            cand = []
            for (x, y) in r['cells']:
                wx, wy = x + vx, y + vy
                if s.g[wy][wx] != '#': continue
                ox, oy = wx + vx, wy + vy
                if (ox, oy) in s.corr and s.g[oy][ox] == '.':
                    # 옆 칸도 벽이어야 문틀이 선다 (모서리 피하기)
                    px, py = (wx + vy, wy + vx), (wx - vy, wy - vx)
                    if s.at(*px) in '#+' and s.at(*py) in '#+':
                        cand.append(((x, y), (wx, wy)))
            if not cand: fail(f"{s.key} 방 {r['lab']}/{r['kind']} 의 {d} 쪽에 통로가 없다")
            if d in 'NS': cand.sort(key=lambda c: (abs(c[0][0] - cx), c[0][0]))
            else: cand.sort(key=lambda c: (abs(c[0][1] - cy), c[0][1]))
            # 이미 문이 옆에 있으면 피한다
            for inner, wall in cand:
                if any(s.at(wall[0] + dx, wall[1] + dy) == '+' for dx, dy in DIRS4): continue
                s.g[wall[1]][wall[0]] = '+'
                r['door_in'].append(inner)
                break
            else: fail(f"{s.key} 방 {r['lab']}/{r['kind']} 문 자리 없음 ({d})")
        if r['hatch']:                     # 에어락 바깥 해치: 그쪽 벽 너머가 우주
            vx, vy = SIDE[r['hatch']]
            cand = []
            for (x, y) in r['cells']:
                wx, wy = x + vx, y + vy
                if s.g[wy][wx] == '#' and s.at(wx + vx, wy + vy) == ' ' and s.at(wx + 2 * vx, wy + 2 * vy) == ' ':
                    px, py = (wx + vy, wy + vx), (wx - vy, wy - vx)
                    if s.at(*px) == '#' and s.at(*py) == '#': cand.append(((x, y), (wx, wy)))
            if not cand: fail(f"{s.key} 에어락 해치 자리 없음 ({r['hatch']})")
            cand.sort(key=lambda c: (abs(c[0][0] - cx) + abs(c[0][1] - cy)))
            inner, wall = cand[0]
            s.g[wall[1]][wall[0]] = '+'
            r['door_in'].append(inner)
            r['hatch_in'] = [inner, (inner[0] - vx, inner[1] - vy)]
    # 짝 방 사이 문
    by_tag = {r['tag']: i for i, r in enumerate(s.rooms) if r['tag']}
    for i, r in enumerate(s.rooms):
        if not r['link']: continue
        j = by_tag.get(r['link'])
        if j is None: fail(f"{s.key} 사이 문 짝 없음 {r['link']}")
        q = s.rooms[j]
        cand = []
        for (x, y) in r['cells']:
            for dx, dy in DIRS4:
                wx, wy = x + dx, y + dy
                if s.g[wy][wx] == '#' and own.get((wx + dx, wy + dy)) == j:
                    px, py = (wx + dy, wy + dx), (wx - dy, wy - dx)
                    if s.at(*px) in '#+' and s.at(*py) in '#+': cand.append(((x, y), (wx, wy), (wx + dx, wy + dy)))
        if not cand: fail(f"{s.key} 사이 문 자리 없음 {r['lab']}/{r['kind']} ↔ {q['lab']}/{q['kind']}")
        # 이미 있는 문 안쪽 칸과 가까운 줄 (통로 쪽)
        ref = r['door_in'][0] if r['door_in'] else (r['x0'], r['y0'])
        cand.sort(key=lambda c: abs(c[0][0] - ref[0]) + abs(c[0][1] - ref[1]))
        for a, wall, b in cand:
            if any(s.at(wall[0] + dx, wall[1] + dy) == '+' for dx, dy in DIRS4): continue
            s.g[wall[1]][wall[0]] = '+'
            r['door_in'].append(a); q['door_in'].append(b)
            break


# ───────────────────────── 설비 ─────────────────────────
def reach(cells, blocked, starts):
    seen = set(s for s in starts if s in cells and s not in blocked)
    st = list(seen)
    while st:
        c = st.pop()
        for dx, dy in DIRS4:
            n = (c[0] + dx, c[1] + dy)
            if n in cells and n not in blocked and n not in seen: seen.add(n); st.append(n)
    return seen


def pack_room(s, r):
    cells = set(r['cells'])
    starts = list(dict.fromkeys(r['door_in']))
    if not starts: fail(f"{s.key} 방 {r['lab']}/{r['kind']} 에 문이 없다")
    keep = set(starts) | set(r.get('hatch_in', []))
    # 문 안쪽 칸에서 거리 (먼 곳부터 채운다)
    dist = {c: 10 ** 6 for c in cells}
    q = list(starts)
    for c in q: dist[c] = 0
    k = 0
    while k < len(q):
        c = q[k]; k += 1
        for dx, dy in DIRS4:
            n = (c[0] + dx, c[1] + dy)
            if n in cells and dist[n] > dist[c] + 1: dist[n] = dist[c] + 1; q.append(n)
    occ = {}          # 칸 → 설비 번호
    letters = {}      # 칸 → 글자
    placed = []
    pref = r['prefer']
    def key_pos(tx, ty, iw, ih):
        d = max(dist.get((tx + dx, ty + dy), 0) for dx in range(iw) for dy in range(ih))
        if pref == 'W': return (tx, -d, ty)
        if pref == 'E': return (-(tx + iw), -d, ty)
        if pref == 'N': return (ty, -d, tx)
        if pref == 'S': return (-(ty + ih), -d, tx)
        return (-d, ty, tx)
    xs = [c[0] for c in cells]; ys = [c[1] for c in cells]
    for idx, item in enumerate(r['items']):
        ih, iw = len(item), max(len(l) for l in item)
        spots = []
        for ty in range(min(ys), max(ys) - ih + 2):
            for tx in range(min(xs), max(xs) - iw + 2):
                spots.append((key_pos(tx, ty, iw, ih), tx, ty))
        spots.sort()
        done = False
        for gap in (1, 0):
            for _, tx, ty in spots:
                box = [(tx + dx, ty + dy) for dy in range(ih) for dx in range(iw)]
                if any(b not in cells or b in occ or b in keep for b in box): continue
                if gap and any((x + ax, y + ay) in occ for x, y in box for ax in (-1, 0, 1) for ay in (-1, 0, 1)): continue
                chars = [(tx + dx, ty + dy, ch) for dy, line in enumerate(item) for dx, ch in enumerate(line) if ch != '.']
                # 같은 글자끼리 붙으면 한 덩어리가 된다 — 피한다
                if any(letters.get((x + ax, y + ay)) == ch for x, y, ch in chars for ax, ay in DIRS4 if (x + ax, y + ay) not in box): continue
                for x, y, ch in chars: occ[(x, y)] = idx; letters[(x, y)] = ch
                for b in box:
                    if b not in occ: occ[b] = -1 - idx   # 무늬 안 빈칸 (소파 사이) — 걷는 칸이지만 다른 설비는 못 놓는다
                # 손 닿음: 문에서 걸어서 모든 설비 옆에 선다 · 문끼리 이어진다
                blocked = {c for c, v in occ.items() if v >= 0}
                seen = reach(cells, blocked, starts)
                ok = all(st in seen for st in starts)
                if ok:
                    for j in set(v for v in occ.values() if v >= 0):
                        fc = [c for c, v in occ.items() if v == j]
                        if not any((c[0] + dx, c[1] + dy) in seen for c in fc for dx, dy in DIRS4): ok = False; break
                if ok:
                    placed.append(idx); done = True
                    break
                for b in box:
                    occ.pop(b, None); letters.pop(b, None)
            if done: break
        if not done:
            if '--debug' in sys.argv:
                for y in range(min(ys) - 1, max(ys) + 2):
                    print(''.join(letters.get((x, y)) or ('d' if (x, y) in starts else '.' if (x, y) in cells else s.at(x, y)) for x in range(min(xs) - 1, max(xs) + 2)))
            fail(f"{s.key} 방 {r['lab']}/{r['kind']} 설비 자리 없음: {item} (방 칸 {len(cells)} · 놓은 것 {len(placed)}/{len(r['items'])})")
    for c, ch in letters.items(): s.g[c[1]][c[0]] = ch
    # 이름표: 문 가까운 빈 바닥 (문 안쪽 칸 바로 옆)
    blocked = set(letters)
    seen = reach(cells, blocked, starts)
    free = [c for c in seen if c not in keep and c not in letters and occ.get(c, 0) == 0 or c in seen and c not in keep and c not in occ]
    free = [c for c in free if c not in letters]
    if not free: free = [c for c in seen if c not in letters]
    free.sort(key=lambda c: (dist[c], c[1], c[0]))
    lab = r['lab']
    if r['kind']:
        if r['kind'] not in s.legend: s.legend[r['kind']] = LEGEND_POOL[len(s.legend)]
        lab = s.legend[r['kind']]
    lx, ly = free[0]
    s.g[ly][lx] = lab


def label_corridors(s):
    seen = set()
    for y in range(s.H):
        for x in range(s.W):
            if s.g[y][x] == '.' and (x, y) not in seen:
                comp, st = [], [(x, y)]
                seen.add((x, y))
                while st:
                    c = st.pop(); comp.append(c)
                    for dx, dy in DIRS4:
                        n = (c[0] + dx, c[1] + dy)
                        if n not in seen and s.at(*n) not in '#+ ':
                            seen.add(n); st.append(n)
                labs = [c for c in comp if s.g[c[1]][c[0]] in BASE_LABELS or s.g[c[1]][c[0]] in LEGEND_POOL]
                if not labs:
                    fl = sorted((c for c in comp if s.g[c[1]][c[0]] == '.'), key=lambda c: (c[1], c[0]))
                    s.g[fl[0][1]][fl[0][0]] = 'c'


# ───────────────────────── 검사 ─────────────────────────
def check(s):
    g, Wt, Ht = s.g, s.W, s.H
    def kind(x, y): return s.at(x, y)
    labset = BASE_LABELS | set(s.legend.values())
    labels = [(x, y) for y in range(Ht) for x in range(Wt) if g[y][x] in labset]
    owner = {}
    for (lx, ly) in labels:
        if (lx, ly) in owner: fail(f'{s.key} 이름표 둘이 한 방에 {lx},{ly}')
        owner[(lx, ly)] = (lx, ly)
        st = [(lx, ly)]
        while st:
            c = st.pop()
            for dx, dy in DIRS4:
                n = (c[0] + dx, c[1] + dy)
                if kind(*n) in '# +': continue
                if n in owner:
                    if owner[n] != (lx, ly): fail(f'{s.key} 방 둘이 붙었다 {n}')
                    continue
                owner[n] = (lx, ly); st.append(n)
    for y in range(Ht):
        for x in range(Wt):
            if g[y][x] not in '# +' and (x, y) not in owner: fail(f'{s.key} 주인 없는 바닥 {x},{y} {g[y][x]}')
    for y in range(Ht):
        for x in range(Wt):
            if g[y][x] != '+': continue
            v = kind(x, y - 1) not in '#+' and kind(x, y + 1) not in '#+'
            h = kind(x - 1, y) not in '#+' and kind(x + 1, y) not in '#+'
            if not (v or h): fail(f'{s.key} 문이 두 공간을 잇지 않는다 {x},{y}')
    hull = {(x, y) for y in range(Ht) for x in range(Wt) if g[y][x] in '#+' and any(kind(x + dx, y + dy) == ' ' for dx in (-1, 0, 1) for dy in (-1, 0, 1))}
    inv = {}
    for c, o in owner.items(): inv.setdefault(o, []).append(c)
    crit = {'r', 'p', s.legend.get('ComputerRoom')}
    for o, cells in inv.items():
        lab = g[o[1]][o[0]]
        if lab in crit and any((c[0] + dx, c[1] + dy) in hull for c in cells for dx in (-1, 0, 1) for dy in (-1, 0, 1)):
            if s.crit_hull_ok: s.warn.append(f'중요한 방 {lab} 이 선체에 닿는다')
            else: fail(f'{s.key} 중요한 방 {lab} 이 선체에 닿는다')
    # 함교에서 모든 방에 길 (설비 칸은 못 지나간다 · 바깥 해치는 빼고)
    bridge = [o for o in inv if g[o[1]][o[0]] == 'b']
    if not bridge: fail(f'{s.key} 함교 없음')
    def walk(c): return kind(*c) not in '# ' and not kind(*c).isupper()
    seen, st = {bridge[0]}, [bridge[0]]
    while st:
        c = st.pop()
        for dx, dy in DIRS4:
            n = (c[0] + dx, c[1] + dy)
            if n in seen or not walk(n): continue
            seen.add(n); st.append(n)
    miss = [g[o[1]][o[0]] for o in inv if o not in seen]
    if miss: fail(f'{s.key} 함교에서 길이 닿지 않는 방: {miss}')
    need_rooms = set('bekrplwsjmfaqhgo')
    have = {g[o[1]][o[0]] for o in inv}
    if not need_rooms <= have: fail(f'{s.key} 필수 방 없음: {sorted(need_rooms - have)}')
    letters = {ch for row in g for ch in row}
    need_f = set('RECPXYOUGVFDBMLQIAWNHZ')
    if not need_f <= letters: fail(f'{s.key} 필수 설비 없음: {sorted(need_f - letters)}')
    # 주컴퓨터실 치우침
    xs = [x for y in range(Ht) for x in range(Wt) if g[y][x] != ' ']
    ys = [y for y in range(Ht) for x in range(Wt) if g[y][x] != ' ']
    cxm, cym = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
    cr = [o for o in inv if g[o[1]][o[0]] == s.legend.get('ComputerRoom')]
    off = (0, 0)
    if cr:
        cc = inv[cr[0]]
        off = (round((sum(c[0] for c in cc) / len(cc) - cxm) / (max(xs) - min(xs) + 1), 3), round((sum(c[1] for c in cc) / len(cc) - cym) / (max(ys) - min(ys) + 1), 3))
    beds = sum(1 for row in g for i, ch in enumerate(row) if ch == 'B' and (i == 0 or row[i - 1] != 'B'))
    return dict(rooms=len(inv), kinds=len({g[o[1]][o[0]] for o in inv}), comp=off, w=max(xs) - min(xs) + 1, h=max(ys) - min(ys) + 1)


def build(fn):
    s = fn()
    carve(s)
    for r in s.rooms: r['cells'] = room_cells(s, r)
    place_doors(s)
    for r in s.rooms: pack_room(s, r)
    label_corridors(s)
    trim(s)
    return s


def trim(s):
    """위아래 · 왼쪽 빈 줄을 걷어 낸다 (오른쪽 공백은 줄마다 rstrip)."""
    rows = [i for i, row in enumerate(s.g) if any(ch != ' ' for ch in row)]
    cols = [x for x in range(s.W) if any(s.g[y][x] != ' ' for y in range(s.H))]
    y0, y1, x0 = rows[0], rows[-1], cols[0]
    s.g = [row[x0:] for row in s.g[y0:y1 + 1]]
    s.H, s.W = len(s.g), len(s.g[0])


def to_text(s):
    lines = [''.join(row).rstrip() for row in s.g]
    leg = [f'@{ch}={k}' for k, ch in s.legend.items()]
    return '\n'.join(leg + lines)


def beds_of(s):
    n = 0
    seen = set()
    for y in range(s.H):
        for x in range(s.W):
            if s.g[y][x] == 'B' and (x, y) not in seen:
                n += 1
                st = [(x, y)]; seen.add((x, y))
                while st:
                    c = st.pop()
                    for dx, dy in DIRS4:
                        m = (c[0] + dx, c[1] + dy)
                        if s.at(*m) == 'B' and m not in seen: seen.add(m); st.append(m)
    return n


# ───────────────────────── 미리보기 ─────────────────────────
COL = {' ': (6, 8, 14), '#': (96, 108, 128), '+': (240, 200, 80), '.': (34, 40, 52)}
FCOL = {'R': (255, 120, 60), 'E': (255, 170, 60), 'P': (90, 170, 255), 'X': (240, 220, 90), 'Y': (180, 220, 90), 'Z': (220, 160, 90), 'O': (120, 220, 255),
        'U': (80, 140, 255), 'G': (90, 200, 110), 'V': (230, 120, 90), 'F': (200, 230, 255), 'D': (230, 180, 120), 'B': (150, 120, 220), 'M': (240, 100, 120),
        'L': (200, 200, 200), 'Q': (160, 160, 255), 'I': (90, 255, 220), 'A': (255, 255, 255), 'W': (200, 150, 100), 'N': (170, 140, 120), 'H': (190, 190, 120),
        'K': (130, 110, 90), 'C': (100, 200, 200), 'S': (120, 130, 150), 'T': (140, 120, 100)}


def png(s, path, scale=8):
    from PIL import Image, ImageDraw
    im = Image.new('RGB', (s.W * scale, s.H * scale), COL[' '])
    d = ImageDraw.Draw(im)
    for y in range(s.H):
        for x in range(s.W):
            ch = s.g[y][x]
            col = COL.get(ch) or FCOL.get(ch) or ((80, 230, 160) if ch.islower() or ch in LEGEND_POOL else (255, 0, 255))
            if ch not in COL and ch not in FCOL: col = (40, 60, 52)
            d.rectangle([x * scale, y * scale, x * scale + scale - 1, y * scale + scale - 1], fill=col)
    im.save(path)


# ═════════════════════════ 배 ═════════════════════════
# 좌표: x 는 뒤(왼쪽, 엔진) → 앞(오른쪽, 뱃머리), y 는 위(좌현) → 아래(우현). 방은 안쪽 칸 좌표.

def falcon():
    """송골매호 — 화살촉형 정찰 연구선 (10인 · 군용 · 새 배).
    뒤로 젖힌 날개 끝이 엔진보다 뒤에 있고(꼬리 홈), 날개 끝에서 뱃머리 끝까지 앞전이 한 줄로 비스듬히 모인다.
    날개 안은 날개 통로를 따라 격납고 · 재배실 · 정비실 · 에어락, 몸통은 통로 고리 둘 사이 심장부, 뱃머리 끝에 통유리 함교."""
    s = Canvas('Songgolmae', '송골매호', 112, 64)
    cy = 30
    m = lambda y: 2 * cy - 1 - y
    s.corridor(24, 23, 77, 24); s.corridor(24, m(24), 77, m(23))       # 가로 통로 (위 · 아래)
    s.corridor(24, 14, 25, m(14)); s.corridor(76, 25, 77, m(25))       # 뒤 · 앞 세로 통로
    s.corridor(11, 14, 23, 14); s.corridor(26, 14, 42, 14)              # 날개 통로 (위)
    s.corridor(11, m(14), 23, m(14)); s.corridor(26, m(14), 42, m(14))  # 날개 통로 (아래)
    s.room('e', 17, 21, 22, m(21), [E, E, E, E, C], doors=[(23, 23), (23, m(23))], prefer='W')
    s.room('?', 13, 16, 22, 19, [K, K], doors=['E'], kind='PropellantTank')
    s.room('?', 13, m(19), 22, m(16), [K, K, C], doors=['E'], kind='HeatStorage')
    s.row(26, m(26), 26, [
        ('r', 8, [R4, C], {'doors': ['N']}),
        ('l', 8, [U, U, O, O, O], {'doors': ['N', 'S']}),
        ('ComputerRoom', 7, [I, C, C, K3], {'doors': ['S']}),
        ('p', 8, [X, Z, Y, Y, Y], {'doors': ['N']}),
        ('g', 7, [SOFA, S, S], {'doors': ['S']}),
        ('Freezer', 5, [F, K, K], {'doors': ['N'], 'grow': True}),
    ], to=75)
    s.row(16, 21, 26, [
        ('q', 12, many(B, 8)),
        ('k', 10, [P, P, P, C]),
        ('h', 7, [M, M, KH]),
        ('o', 6, [A, C], {'grow': True}),
    ], to=75)
    s.row(m(21), m(16), 26, [
        ('PrivateCabins', 6, many(B, 2) + [K3]),
        ('f', 12, many(G, 6)),
        ('j', 6, [V, F, K], {'tag': 'j'}),
        ('m', 9, [D, D, TSET, TSET], {'link': 'j'}),
        ('Observatory', 6, [S, S, C], {'grow': True}),
    ], doors=['N'], to=75)
    s.row(8, 12, 3, [
        ('ShuttleBay', 10, [K, K, L]),
        ('s', 14, [K, KH, KH, KH]),
    ], to=40)
    s.row(m(12), m(8), 3, [
        ('w', 14, [H, W, N, K3]),
        ('a', 12, [L, L, Q, Q], {'hatch': 'S', 'grow': True}),
    ], doors=['N'], to=40)
    s.room('b', 79, 23, 103, m(23), [C, C, C, S, S, S], doors=['W'], prefer='W')
    half = [(16.5, 0), (16.5, 9.6), (2.5, 23.4), (12, 23.4), (104, 0.3)]
    s.shape(mirror(half, cy))
    return s


def hammerhead():
    """귀상어호 — 망치머리형 심우주 탐사선 (8인 · 민간 · 중고).
    엔진 블록(통로 고리 · 심장부) → 가는 척추(통로 둘 사이 선실 줄) → 앞머리를 가로지르는 망치(세로 통로 · 양 끝 관측실 · 가운데 함교)."""
    s = Canvas('Gwisangeo', '귀상어호', 100, 64)
    cy = 30
    m = lambda y: 2 * cy - 1 - y
    s.corridor(9, 23, 67, 24); s.corridor(9, m(24), 67, m(23))     # 가로 통로 둘 (척추를 지나 망치까지)
    s.corridor(9, 25, 10, m(25))                                     # 뒤 세로 통로
    s.corridor(66, 5, 67, m(5))                                      # 망치 세로 통로
    s.room('e', 2, 17, 7, m(17), [E, E, E, C], doors=[(8, 23), (8, m(23))], prefer='W')
    s.row(17, 21, 8, [
        ('k', 8, [P, P, C]),
        ('s', 6, [K, K, K]),
        ('w', 10, [H, W, N, K3], {'grow': True}),
    ], to=40)
    s.row(m(21), m(17), 8, [
        ('a', 7, [L, L, Q, Q], {'hatch': 'S'}),
        ('f', 12, many(G, 5), {'grow': True}),
        ('Freezer', 5, [F, K]),
    ], doors=['N'], to=40)
    s.row(26, m(26), 11, [
        ('r', 7, [R4, C], {'doors': ['N']}),
        ('ComputerRoom', 6, [I, C, C], {'doors': ['S']}),
        ('p', 6, [X, Z, Y, Y], {'doors': ['N']}),
        ('l', 6, [U, O, O], {'doors': ['S'], 'grow': True}),
    ], to=40)
    s.row(26, m(26), 40, [
        ('q', 9, many(B, 8), {'doors': ['N']}),
        ('j', 5, [V, F, K], {'doors': ['S'], 'tag': 'j'}),
        ('m', 7, [D, TSET], {'doors': ['S'], 'link': 'j', 'grow': True}),
    ], to=65)
    s.col(69, 83, 4, [
        ('Observatory', 6, [S, S, S, C, SOFA]),
        ('Lab', 6, [W, C, K3]),
        ('o', 5, [A, C, K3]),
    ], doors=['W'], to=25)
    s.room('b', 69, 26, 90, m(26), [C, C, C, S, S], doors=['W'], prefer='E')
    s.col(69, 83, m(25), [
        ('h', 5, [M, M, K3]),
        ('g', 6, [SOFA, S, S]),
        ('Calibration', 6, [W, C, K3]),
    ], doors=['W'], to=m(4))
    half = [(1.5, 0), (1.5, 11), (4.5, 14), (36, 14), (43, 7), (64.6, 7), (65.5, 25.6), (78, 25.6), (84.6, 19), (84.6, 6), (91.6, 3), (91.6, 0)]
    s.shape(mirror(half, cy))
    return s


def saucer():
    """보름달호 — 원반형 장거리 여객 탐사선 (20인 · 민간 · 새 배).
    앞은 둥근 원반 (네모 고리 통로 · 가로지르는 통로 · 안쪽 방 넷 줄 · 테두리 방), 가는 목 통로, 뒤는 기관 선체,
    기관 선체 위아래로 기둥 통로에 매단 나셀 두 개 (추진제 · 배터리)."""
    s = Canvas('Boreumdal', '보름달호', 120, 70)
    cy = 32
    m = lambda y: 2 * cy - 1 - y
    # 원반: 네모 고리 통로 + 가로지르는 통로
    s.corridor(62, 14, 97, 15); s.corridor(62, m(15), 97, m(14))
    s.corridor(62, 16, 63, m(16)); s.corridor(96, 16, 97, m(16))
    s.corridor(64, 31, 95, 32)
    # 목 · 기관 선체 통로 · 기둥
    s.corridor(43, 31, 61, 32)
    s.corridor(9, 26, 42, 27); s.corridor(9, m(27), 42, m(26))
    s.corridor(9, 28, 10, m(28)); s.corridor(41, 28, 42, m(28))
    s.corridor(21, 8, 22, 25); s.corridor(21, m(25), 22, m(8))
    # 원반 안쪽 네 줄
    s.row(17, 22, 64, [('q', 13, many(B, 9)), ('q', 13, many(B, 9), {'grow': True})], doors=['N'], to=95)
    s.row(24, 29, 64, [('ComputerRoom', 8, [I, C, C, K3]), ('p', 8, [X, Z, Y, Y, Y]), ('l', 10, [U, U, O, O, O, O], {'grow': True})], doors=['S'], to=95)
    s.row(m(29), m(24), 64, [('Shelter', 8, [K, K, S, S, S, S]), ('MeetingRoom', 9, [T, T, S, S, S, S], {'grow': True}), ('PrivateCabins', 8, many(B, 2) + [K3])], doors=['N'], to=95)
    s.row(m(22), m(17), 64, [('Freezer', 6, [F, F, K]), ('j', 8, [V, V, F, K], {'tag': 'j'}), ('m', 12, [D, D, D, TSET, TSET, TSET], {'link': 'j', 'grow': True})], doors=['S'], to=95)
    # 원반 테두리 (위 · 아래 · 앞 · 뒤)
    s.row(6, 12, 61, [('Garden', 10, [G2, S, S]), ('f', 11, many(G, 6), {'grow': True}), ('Observatory', 10, [S, S, C])], to=98)
    s.row(m(12), m(6), 61, [('g', 10, [S, S, T]), ('h', 11, [M, M, M, K3], {'grow': True}), ('Gym', 10, [ROWS2])], doors=['N'], to=98)
    s.col(99, 106, 15, [('o', 9, [A, C, K3]), ('b', 12, [C, C, C, C, S, S, S], {'prefer': 'E', 'grow': True}), ('Navigation', 8, [C, C, K3])], doors=['W'], to=m(15))
    s.col(54, 60, 18, [('AlgaeLab', 11, [G2, G2, U])], doors=['E'], to=30)
    s.col(54, 60, 33, [('EscapeBay', 11, [K, K])], doors=['E'], to=m(18))
    # 기관 선체
    s.room('e', 2, 20, 7, m(20), [E, E, E, E, C], doors=[(8, 26), (8, m(26))], prefer='W')
    s.row(20, 24, 8, [('k', 11, [P, P, P, C])], to=20)
    s.row(20, 24, 23, [('BatteryRoom', 7, [Y, Y]), ('s', 9, [K, K, K, K], {'grow': True})], to=43)
    s.row(m(24), m(20), 8, [('w', 11, [H, W, W, N, K3])], doors=['N'], to=20)
    s.row(m(24), m(20), 23, [('a', 9, [L, L, L, Q, Q], {'hatch': 'S'}), ('Cargo', 8, [K, K, K], {'grow': True})], doors=['N'], to=43)
    s.row(29, m(29), 11, [('r', 9, [R5, C], {'doors': ['N']}), ('FuelCell', 7, [Z, Z, C], {'doors': ['S']}), ('Substation', 6, [X, K3], {'doors': ['N'], 'grow': True})], to=40)
    # 나셀 (기둥 꼭대기 양쪽)
    s.room('?', 2, 9, 19, 12, [K, K, K], doors=['E'], kind='PropellantTank')
    s.room('?', 24, 9, 38, 12, [Y, Y, C], doors=['W'], kind='BatteryRoom')
    s.room('?', 2, m(12), 19, m(9), [K, K, K], doors=['E'], kind='GasStorage')
    s.room('?', 24, m(12), 38, m(9), [K, K, C], doors=['W'], kind='HeatStorage')
    s.shape(ellipse(80, cy, 26.2, 26.2, 72))
    s.shape(mirror([(1.5, 0), (1.5, 9), (5, 13.6), (40, 13.6), (44, 8), (44, 0)], cy))
    s.shape([(42, cy - 2.6), (62, cy - 2.6), (62, cy + 2.6), (42, cy + 2.6)])
    for c in (cy - 21.5, cy + 21.5):
        s.shape(mirror([(1.5, 0), (1.5, 1.4), (3, 2.6), (36, 2.6), (41.5, 0)], c))
    for y0, y1 in ((12, 20), (m(20), m(12))):
        s.shape([(20.5, y0), (23.5, y0), (23.5, y1), (20.5, y1)])
    return s


def trident():
    """삼지창호 — 삼지창형 소행성 채굴선 (12인 · 개척민 · 중고).
    뒤 몸통(엔진 · 통로 셋 · 심장부)에서 앞으로 뻗은 갈래 셋. 가운데 갈래는 척추 통로와 작은 방들, 끝에 함교.
    양쪽 갈래는 바깥 띠와 옆 통로가 그대로 뻗고, 끝은 미늘처럼 바깥으로 젖힌 파쇄실 · 용접실."""
    s = Canvas('Samjichang', '삼지창호', 116, 64)
    cy = 30
    m = lambda y: 2 * cy - 1 - y
    s.corridor(9, 18, 78, 19); s.corridor(9, m(19), 78, m(18))     # 옆 통로 (갈래까지)
    s.corridor(9, 29, 95, 30)                                        # 척추 통로 (가운데 갈래 끝까지)
    s.corridor(9, 20, 10, m(20))                                     # 뒤 세로 통로
    s.room('e', 2, 12, 7, m(12), [E, E, E, E, C], doors=[(8, 18), (8, 29), (8, m(18))], prefer='W')
    s.row(12, 16, 8, [
        ('k', 11, [P, P, P, P, C]),
        ('s', 7, [K, K, K]),
        ('Cargo', 9, [K, K, K, K]),
        ('DroneBay', 7, [Q, Q, K]),
        ('w', 11, [H, W, W, N, K3]),
        ('Recycling', 6, [N, K3], {'grow': True}),
    ], to=79)
    s.room('?', 80, 5, 87, 19, [N, K3, K], doors=['W'], kind='Crusher', prefer='N')
    s.row(m(16), m(12), 8, [
        ('q', 12, many(B, 8)),
        ('q', 6, many(B, 4)),
        ('j', 7, [V, V, F, F, K], {'tag': 'j'}),
        ('m', 9, [D, D, D, TSET, TSET], {'link': 'j'}),
        ('f', 13, many(G, 8), {'grow': True}),
        ('a', 8, [L, L, L, Q, Q], {'hatch': 'S'}),
    ], doors=['N'], to=79)
    s.room('?', 80, m(19), 87, m(5), [W, K3], doors=['W'], kind='WeldingShop', prefer='S')
    s.row(21, 27, 11, [
        ('r', 8, [R4, C], {'doors': ['N']}),
        ('p', 8, [X, Z, Y, Y, Y], {'doors': ['S']}),
        ('Shelter', 7, [K, K, S, S, S, S], {'doors': ['N'], 'grow': True}),
    ], to=40)
    s.row(m(27), m(21), 11, [
        ('l', 9, [U, U, O, O, O], {'doors': ['N']}),
        ('ComputerRoom', 7, [I, C, C, K3], {'doors': ['S']}),
        ('Freezer', 6, [F, K, K], {'doors': ['N'], 'grow': True}),
    ], to=40)
    s.row(25, 27, 40, [
        ('o', 8, [A, C]),
        ('Navigation', 8, [C, C]),
        ('Lab', 9, [W, C], {'grow': True}),
        ('Observatory', 8, [S, S, C]),
        ('MeetingRoom', 9, [T, S, S]),
    ], to=95)
    s.row(m(27), m(25), 40, [
        ('h', 9, [M, M, K]),
        ('g', 9, [SOFA, S], {'grow': True}),
        ('MushroomFarm', 7, [G2, G2]),
        ('AlgaeLab', 7, [G2, G2]),
        ('Morgue', 5, [K]),
        ('FuelCell', 8, [Z, Z, C]),
    ], doors=['N'], to=95)
    s.room('b', 97, 24, 108, m(24), [C, C, C, S, S], doors=['W'], prefer='E')
    half = [(1.5, 0), (1.5, 14), (5.5, 18.6), (79, 18.6), (89.5, 25.6), (84.6, 9.6), (40.9, 9.6), (40.9, 5.6), (96, 5.6), (109.5, 0)]
    s.shape(mirror(half, cy))
    return s


def manta():
    """가오리호 — 가오리형 농업선 (16인 · 민간 · 중고).
    넓게 펼친 세모 날개 안이 온통 재배실 (날개 통로 둘 · 날개 척추 통로). 앞머리 양쪽 뿔에 통신실 · 항법실, 가운데 함교. 꼬리는 선체 바깥 그림."""
    s = Canvas('Gaori', '가오리호', 104, 84)
    cy = 38
    m = lambda y: 2 * cy - 1 - y
    s.corridor(24, 31, 85, 32); s.corridor(24, m(32), 85, m(31))     # 가로 통로
    s.corridor(24, 33, 25, m(33)); s.corridor(79, 33, 80, m(33))     # 뒤 · 앞 세로 통로
    s.corridor(29, 22, 70, 23); s.corridor(29, m(23), 70, m(22))     # 날개 통로 1
    s.corridor(34, 13, 58, 13); s.corridor(34, m(13), 58, m(13))     # 날개 통로 2
    s.corridor(46, 14, 47, 21); s.corridor(46, m(21), 47, m(14))     # 날개 척추 (통로 2 ↔ 1)
    s.corridor(46, 24, 47, 30); s.corridor(46, m(30), 47, m(24))     # 날개 척추 (통로 1 ↔ 가로)
    s.room('e', 17, 34, 22, m(34), [E, E, C], doors=['E'], prefer='W')
    s.row(34, m(34), 26, [
        ('r', 8, [R5, C], {'doors': ['N']}),
        ('l', 9, [U, U, O, O, O], {'doors': ['S']}),
        ('ComputerRoom', 7, [I, C, C, K3], {'doors': ['N']}),
        ('p', 8, [X, Z, Y, Y, Y], {'doors': ['S']}),
        ('WaterPlant', 6, [U, U], {'doors': ['N']}),
        ('Freezer', 6, [F, F, K], {'doors': ['S'], 'grow': True}),
    ], to=78)
    # 안쪽 띠 (날개 통로 1 과 가로 통로 사이)
    s.row(25, 29, 26, [('g', 9, [SOFA, S, S]), ('s', 8, [K, K, K, K], {'grow': True})], to=45)
    s.row(25, 29, 48, [('q', 10, many(B, 8)), ('h', 6, [M, M, K3]), ('k', 11, [P, P, P, P, C], {'grow': True})], to=78)
    s.row(m(29), m(25), 26, [('q', 12, many(B, 8)), ('Shelter', 5, [K, S, S, S], {'grow': True})], doors=['N'], to=45)
    s.row(m(29), m(25), 48, [('j', 10, [V, V, V, F, F, K], {'tag': 'j'}), ('m', 15, [D, D, D, D] + many(TSET, 4), {'link': 'j', 'grow': True})], doors=['N'], to=78)
    # 뿔 · 함교
    s.room('o', 82, 25, 90, 29, [A, C], doors=['S'])
    s.room('?', 82, m(29), 90, m(25), [C, C], doors=['N'], kind='Navigation')
    s.room('b', 82, 34, 92, m(34), [C, C, C, S, S, S], doors=['W'], prefer='E')
    # 날개 가운데 띠 (재배)
    s.row(15, 20, 30, [('f', 14, many(G, 5), {'grow': True})], to=45)
    s.row(15, 20, 48, [('MushroomFarm', 8, [G, G, K3]), ('AlgaeLab', 10, [G, G, U], {'grow': True})], to=70)
    s.row(m(20), m(15), 30, [('f', 14, many(G, 5), {'grow': True})], doors=['N'], to=45)
    s.row(m(20), m(15), 48, [('ProteinFarm', 8, [G, G, U]), ('Garden', 10, [G, G, S, S], {'grow': True})], doors=['N'], to=70)
    # 날개 끝 띠
    s.row(6, 11, 34, [('SeedVault', 8, [K, K]), ('w', 12, [H, W, N, K3], {'grow': True})], to=58)
    s.row(m(11), m(6), 34, [('a', 10, [L, L, L, Q, Q], {'hatch': 'S', 'grow': True}), ('DroneBay', 10, [Q, Q, K])], doors=['N'], to=58)
    half = [(16.5, 0), (16.5, 4.6), (22, 6), (30, 18), (40, 36), (48, 37), (62, 22), (76, 12.6), (84, 13.6), (91, 8), (86.5, 5.4), (93.5, 0)]
    s.shape(mirror(half, cy))
    return s


def whale():
    """고래호 — 고래형 세대선 (24인 · 개척민 · 오래된 배).
    둥글고 커다란 몸통: 네모 고리 통로 + 가운데를 꿰뚫는 척추 통로, 고리 안쪽 큰 방 두 줄, 바깥 둥근 테두리 방들.
    뒤로 가는 꼬리 통로, 꼬리 지느러미 안에 엔진실. 둥근 머리에 함교."""
    s = Canvas('Gorae', '고래호', 150, 64)
    cy = 30
    m = lambda y: 2 * cy - 1 - y
    s.corridor(44, 16, 128, 17); s.corridor(44, m(17), 128, m(16))   # 고리 위 · 아래
    s.corridor(44, 18, 45, m(18)); s.corridor(127, 18, 128, m(18))   # 고리 뒤 · 앞
    s.corridor(17, 29, 126, 30)                                       # 척추 (꼬리 → 머리)
    s.room('e', 4, 17, 15, m(17), [E, E, E, E, C], doors=[(16, 29)], prefer='W')
    # 고리 안 (위 · 아래 큰 방 줄)
    s.row(19, 27, 46, [
        ('r', 10, [R6, C], {'doors': ['N']}),
        ('FuelCell', 7, [Z, Z, C], {'doors': ['S']}),
        ('l', 10, [U, U, O, O, O, O], {'doors': ['S']}),
        ('ComputerRoom', 9, [I, C, C, K3], {'doors': ['S']}),
        ('p', 9, [X, Z, Y, Y, Y, Y], {'doors': ['N']}),
        ('BatteryRoom', 7, [Y, Y, Y], {'doors': ['S']}),
        ('Theater', 12, many(ROWS2, 4) + [C], {'doors': ['S'], 'grow': True}),
    ], to=126)
    s.row(m(27), m(19), 46, [
        ('School', 10, [T, T, S, S, S, S], {'doors': ['N']}),
        ('MeetingRoom', 9, [T, T, S, S, S, S], {'doors': ['S']}),
        ('Chapel', 7, [S, S, S, S], {'doors': ['N']}),
        ('Freezer', 7, [F, F, K, K], {'doors': ['N']}),
        ('j', 9, [V, V, V, F, F, K], {'doors': ['N'], 'tag': 'j'}),
        ('m', 14, [D, D, D, D] + many(TSET, 5), {'doors': ['N', 'S'], 'link': 'j', 'grow': True}),
        ('Shelter', 9, [K, K, S, S, S, S], {'doors': ['S']}),
    ], to=126)
    # 둥근 테두리 위 · 아래
    s.row(5, 14, 42, [
        ('SeedVault', 8, [K, KH]),
        ('k', 11, [P, P, P, P, C]),
        ('f', 14, many(G, 8), {'grow': True}),
        ('f', 14, many(G, 8), {'grow': True}),
        ('Garden', 11, [G, G, S, S, S]),
        ('s', 10, [K, K, K]),
    ], to=131)
    s.row(m(14), m(5), 42, [
        ('q', 13, many(B, 8)),
        ('q', 13, many(B, 8)),
        ('q', 13, many(B, 8), {'grow': True}),
        ('h', 11, [M, M, M, M, K3]),
        ('g', 10, [SOFA, SOFA, S]),
        ('Gym', 9, [ROWS2, ROWS2]),
    ], doors=['N'], to=131)
    # 뒤쪽 둥근 부분 (고리 뒤 세로 통로 뒤)
    s.col(33, 42, 15, [('w', 12, [H, W, W, N, K3])], doors=['E'], to=28)
    s.col(33, 42, 31, [('a', 12, [L, L, L, Q, Q, Q], {'hatch': 'W'})], doors=['E'], to=m(15))
    # 둥근 머리
    s.col(130, 140, 15, [('o', 7, [A, C, K3]), ('b', 12, [C, C, C, C, S, S, S], {'prefer': 'E', 'grow': True}), ('Navigation', 7, [C, C, K3])], doors=['W'], to=m(15))
    body = ellipse(87, cy, 56.5, 25.6, 96)
    body = [(x if x > 50 else 50 - (50 - x) * 0.82, y) for x, y in body]
    s.shape(body)
    s.shape([(15, cy - 2.6), (36, cy - 2.6), (36, cy + 2.6), (15, cy + 2.6)])
    s.shape([(2.5, cy - 15.6), (9, cy - 13.6), (16.6, cy - 4), (16.6, cy + 4), (9, cy + 13.6), (2.5, cy + 15.6), (7.5, cy)])
    return s


def dragonfly():
    """잠자리호 — 잠자리형 소형 연구선 (6인 · 민간 · 새 배).
    가늘고 긴 몸통 (위아래 방 줄 · 가운데 통로), 둥근 머리 함교, 앞뒤 두 쌍의 날개 — 날개는 뿌리 통로에서 방 둘이 이어진다.
    몸통이 가늘어 원자로 · 배전실이 선체 바로 안쪽에 있다 (운석 한 방이 아프다)."""
    s = Canvas('Jamjari', '잠자리호', 116, 70)
    cy = 30
    m = lambda y: 2 * cy - 1 - y
    s.crit_hull_ok = True
    s.corridor(9, 29, 90, 30)
    for x in (34, 57):
        s.corridor(x, 21, x + 1, 28); s.corridor(x, m(28), x + 1, m(21))
    s.room('e', 2, 24, 7, m(24), [E, E, C], doors=['E'], prefer='W')
    s.row(24, 27, 8, [('k', 7, [P, P, C]), ('r', 6, [R3, C]), ('l', 9, [U, O, O], {'grow': True})], to=33)
    s.row(24, 27, 36, [('ComputerRoom', 6, [I, C]), ('p', 6, [X, Z, Y, Y]), ('s', 5, [K, KH], {'grow': True})], to=56)
    s.row(24, 27, 59, [('q', 12, many(B, 6)), ('h', 6, [M, K]), ('Observatory', 10, [S, S, C], {'grow': True})], to=91)
    s.row(m(27), m(24), 8, [('w', 10, [H, W, N]), ('a', 6, [L, L, Q], {'hatch': 'S'}), ('Freezer', 6, [F, K], {'grow': True})], doors=['N'], to=33)
    s.row(m(27), m(24), 36, [('j', 5, [V, F, K], {'tag': 'j'}), ('m', 7, [D, TSET], {'link': 'j'}), ('Shelter', 5, [K, S, S], {'grow': True})], doors=['N'], to=56)
    s.row(m(27), m(24), 59, [('g', 9, [SOFA, S]), ('Lab', 8, [W, C, K]), ('o', 10, [A, C], {'grow': True})], doors=['N'], to=91)
    s.room('b', 92, 23, 104, m(23), [C, C, C, S, S], doors=['W'], prefer='E')
    # 날개: 뿌리 통로 → 방 → 방 (사이 문)
    s.room('f', 21, 13, 41, 19, many(G, 3) + [G2], doors=[(34, 20)], tag='fw1')
    s.room('?', 19, 3, 41, 11, [K, K, Y], doors=[], kind='BatteryRoom', link='fw1')
    s.room('?', 50, 13, 70, 19, [G, G, U], doors=[(57, 20)], tag='fw2', kind='AlgaeLab')
    s.room('?', 50, 3, 74, 11, [G, G, S, S], doors=[], kind='Garden', link='fw2')
    s.room('?', 21, m(19), 41, m(13), [K, K, K], doors=[(34, m(20))], tag='fw3', kind='Cargo')
    s.room('?', 19, m(11), 41, m(3), [Q, Q, K], doors=[], kind='DroneBay', link='fw3')
    s.room('?', 50, m(19), 70, m(13), [W, C, K], doors=[(57, m(20))], tag='fw4', kind='Calibration')
    s.room('?', 50, m(11), 74, m(3), [S, S, C], doors=[], kind='Observatory', link='fw4')
    s.shape(mirror([(1.5, 0), (1.5, 5), (3, 6.6), (91, 6.6), (93, 5), (93, 0)], cy))
    s.shape(ellipse(98.5, cy, 8.2, 8.0, 40))
    w_front = [(52, cy - 6), (61.6, cy - 6), (73.6, cy - 25.6), (70, cy - 28.6), (62.6, cy - 28)]
    w_rear = [(28.6, cy - 6), (38.6, cy - 6), (31, cy - 26), (24, cy - 28.6), (19.6, cy - 25.6)]
    for w in (w_front, w_rear):
        s.shape(w, [(x, 2 * cy - y) for x, y in w])
    return s


def wedge():
    """한울호 — 쐐기형 함대 기함 (30인 · 군용 · 새 배).
    거대한 쐐기 하나: 가운데 축 통로에서 바깥으로 갈수록 짧아지는 방 띠와 통로가 층층이. 뒷면 가득 엔진실,
    꼬리 쪽 가운데에 지휘탑(함교 — 축 통로가 시작하는 자리), 앞으로 갈수록 좁아져 뱃머리 끝은 바늘처럼."""
    s = Canvas('Hanul', '한울호', 140, 86)
    cy = 40
    m = lambda y: 2 * cy - 1 - y
    s.corridor(31, 39, 121, 40)                                      # 축 통로
    s.corridor(9, 29, 101, 30); s.corridor(9, m(30), 101, m(29))     # 통로 1
    s.corridor(9, 19, 68, 20); s.corridor(9, m(20), 68, m(19))       # 통로 2
    s.corridor(9, 9, 36, 10); s.corridor(9, m(10), 36, m(9))         # 통로 3
    s.corridor(9, 11, 10, 28); s.corridor(9, 31, 10, m(31)); s.corridor(9, m(28), 10, m(11))   # 뒤 세로 통로
    s.room('e', 2, 4, 7, m(4), [E, E, E, E, E, C], doors=[(8, 9), (8, 19), (8, 29), (8, m(29)), (8, m(19)), (8, m(9))], prefer='W')
    s.room('b', 12, 32, 29, m(32), [C, C, C, C, C, S, S, S, S, S], doors=['W', (30, 39)], prefer='E')
    # 띠 A (축 통로 옆 · 심장부)
    s.row(32, 37, 30, [
        ('r', 10, [R5, C], {'doors': ['N']}),
        ('FuelCell', 7, [Z, Z, C], {'doors': ['S']}),
        ('ComputerRoom', 9, [I, C, C, C, K3], {'doors': ['S']}),
        ('p', 10, [X, Z, Y, Y, Y, Y, Y], {'doors': ['N']}),
        ('Substation', 6, [X, K3], {'doors': ['S']}),
        ('ServerRoom', 9, [K3, K3, K3, C], {'doors': ['S']}),
        ('Security', 7, [C, C, K3], {'doors': ['N']}),
        ('Navigation', 9, [C, C, K3], {'doors': ['S'], 'grow': True}),
        ('o', 8, [A, C], {'doors': ['S']}),
    ], to=124)
    s.row(m(37), m(32), 30, [
        ('l', 10, [U, U, O, O, O, O, O], {'doors': ['S']}),
        ('BatteryRoom', 7, [Y, Y, Y, Y], {'doors': ['N']}),
        ('HvacRoom', 6, [O, K3], {'doors': ['N']}),
        ('Armory' if False else 'Shelter', 9, [K, K, S, S, S, S, S, S], {'doors': ['S']}),
        ('MeetingRoom', 9, [T, T, S, S, S, S, S, S], {'doors': ['N']}),
        ('Lab', 9, [W, W, C, K3], {'doors': ['N']}),
        ('Calibration', 7, [W, C], {'doors': ['N'], 'grow': True}),
        ('Observatory', 10, [S, S, C], {'doors': ['N']}),
    ], to=124)
    # 띠 B
    s.row(22, 27, 11, [
        ('q', 13, many(B, 8)),
        ('q', 13, many(B, 8)),
        ('PrivateCabins', 6, many(B, 2) + [K3]),
        ('g', 10, [SOFA, SOFA, S]),
        ('Gym', 8, [ROWS2, ROWS2]),
        ('k', 11, [P, P, P, P, C], {'grow': True}),
    ], to=100)
    s.row(m(27), m(22), 11, [
        ('q', 13, many(B, 8)),
        ('PrivateCabins', 6, many(B, 2) + [K3]),
        ('PrivateCabins', 6, many(B, 2) + [K3]),
        ('Freezer', 7, [F, F, K, K]),
        ('j', 11, [V, V, V, V, F, F, F, K], {'tag': 'j'}),
        ('m', 16, [D, D, D, D, D] + many(TSET, 5), {'link': 'j'}),
        ('h', 10, [M, M, M, K3], {'grow': True}),
    ], doors=['N'], to=100)
    # 띠 C
    s.row(12, 17, 11, [
        ('f', 14, many(G, 8)),
        ('f', 12, many(G, 6)),
        ('MushroomFarm', 8, [G, G, K3]),
        ('AlgaeLab', 8, [G, U], {'grow': True}),
    ], to=60)
    s.row(m(17), m(12), 11, [
        ('w', 13, [H, W, W, N, N, K3]),
        ('RobotBay', 8, [W, K3, K]),
        ('DroneBay', 8, [Q, Q, K], {'grow': True}),
        ('Triage', 6, [M, M]),
    ], doors=['N'], to=60)
    # 띠 D (날개 끝)
    s.row(3, 7, 9, [('s', 12, [K, K, K, KH]), ('Cargo', 9, [K, KH], {'grow': True})], to=33)
    s.row(m(7), m(3), 9, [('a', 12, [L, L, L, Q, Q, Q], {'hatch': 'S'}), ('ShuttleBay', 9, [KH, KH], {'grow': True})], doors=['N'], to=33)
    half = [(1.5, 0), (1.5, 33), (5, 37.6), (14, 37.6), (131, 0.4)]
    s.shape(mirror(half, cy))
    return s


SHIPS = [falcon, hammerhead, saucer, trident, manta, whale, dragonfly, wedge]


def main():
    out = ['// 자동 생성: tools/shipgen/gen_shapes.py (v19 모양 있는 새 배). 손으로 고치지 말고 생성기를 고친다.',
           '// 방 · 통로를 놓고 실루엣 다각형으로 깎았다 — 뱃머리 · 날개 · 원반 테두리가 비스듬하거나 둥글다.',
           'namespace ShipSim.Core;', '', 'public static partial class ShipBlueprints', '{']
    bad = 0
    pngdir = sys.argv[sys.argv.index('--png') + 1] if '--png' in sys.argv else None
    only = sys.argv[sys.argv.index('--only') + 1].split(',') if '--only' in sys.argv else None
    for fn in SHIPS:
        if only and fn.__name__ not in only: continue
        try:
            s = build(fn)
            info = check(s)
        except Fail as e:
            print('✘', e); bad += 1
            continue
        print(f"{s.name:6} {s.key:11} {info['w']}×{info['h']} 방 {info['rooms']} 종류 {info['kinds']} 침대 {beds_of(s)} 주컴퓨터실 치우침 {info['comp'][0]:+.3f},{info['comp'][1]:+.3f}" + (f"  ⚠ {' · '.join(s.warn)}" if s.warn else ''))
        if '--show' in sys.argv and s.key in sys.argv: print(to_text(s))
        if pngdir: png(s, os.path.join(pngdir, s.key + '.png'))
        out.append(f'    public const string {s.key}Name = "{s.name}";')
        out.append(f'    public const string {s.key} = """')
        out.append(to_text(s))
        out.append('""";')
        out.append('')
    out.append('}')
    if bad: sys.exit(1)
    if '--dry' not in sys.argv and not pngdir:
        path = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'game', 'src', 'Core', 'ShipShapesAscii.cs')
        open(path, 'w', encoding='utf-8').write('\n'.join(out) + '\n')


if __name__ == '__main__':
    main()
