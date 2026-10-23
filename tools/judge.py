#!/usr/bin/env python3
"""Lab --judge 결과(JUDGE 줄)를 묶어 다시 세운 '최상' 기준에 견준다 (docs/DEPTH.md '다시 세운 최상 기준').
버팀 넷 (① 사람 ② 물자 · 설비 순환 ③ 값이 터지거나 굳지 않음 ④ 지루하지 않음) · 저절로 드러남 · 얽힘(엔진 사이의 반응) · 60프레임.
엔진 층마다의 깊이 · 사실성(더 무거운 쪽)은 법칙 시험 · 깊이 표로 따로 본다 — 여기는 돌렸을 때의 결과.
쓰기: python3 tools/judge.py 출력파일...  (여러 프로세스로 나눠 돌린 것을 함께 읽는다)"""
import json, sys
from collections import defaultdict

ENG = ["", "기반", "물질", "장", "구조", "바깥", "망", "생명", "마음", "사회", "주컴퓨터", "일·물자", "이야기꾼", "연출"]
WORLD = range(2, 12)   # 세계 · 마음 엔진 (기반 · 이야기꾼 · 연출은 반응을 주고받는 쪽이 아니다)

runs = []
for path in sys.argv[1:]:
    for line in open(path, encoding='utf8', errors='replace'):
        if line.startswith('JUDGE '):
            runs.append(json.loads(line[6:]))
if not runs:
    print('JUDGE 줄이 없다'); sys.exit(1)

def mark(ok): return '✔' if ok else '✘'
def mn(xs, d=0): return min(xs) if xs else d

print(f'판 {len(runs)}개 · 배 {len({r["ship"] for r in runs})}척')
print('\n배 · 시드 · 날      산 사람   죽음  하루 하이라이트   가장 긴 조용함   구멍 최장   예비 공기 최저   방 산소 최저   멈춘 사람   틱 ms → 60프레임 배속')
for r in sorted(runs, key=lambda r: (r['ship'], r['seed'], r['days'])):
    days = max(1, len(r['hl']))
    print(f'{r["ship"]:9} {r["seed"]:3} {r["ran"]:5.1f}/{r["days"]:<4g} {r["alive"]:3}/{r["crew"]:<3} {r["deaths"]:4}  {sum(r["hl"]) / days:5.1f}/날          '
          f'{r.get("quietH", 0):5.0f}시간      {max(r.get("dHoleAge", [0]) or [0]):5.0f}시간   {mn(r.get("dAir", [])):9.0f}몰    {mn(r.get("dO2", []), 99):6.1f}kPa   {max(r.get("dStuck", [0]) or [0]):3}명    {r["worstMs"]:.2f} → {r["speed"]}배')

problems = defaultdict(list)
for r in runs:
    tag = f'{r["ship"]} {r["seed"]}'
    died = r.get('died', [])
    # ① 사람: 한꺼번에 다 죽거나 · 하루에 반 넘게 (몇 명이 죽는지는 난이도에 달렸다)
    if r['alive'] == 0: problems['사람'].append(f'{tag}: {r["ran"]:.1f}일에 모두 숨짐')
    elif died and max(died) * 2 >= r['crew']: problems['사람'].append(f'{tag}: 하루에 {max(died)}명')
    # ② 물자 · 설비 순환
    if mn(r.get('dFood', []), 99) < 3: problems['순환'].append(f'{tag}: 식량 {mn(r["dFood"]):.0f}일치')
    if mn(r.get('dWater', []), 99) < 3: problems['순환'].append(f'{tag}: 물 {mn(r["dWater"]):.0f}일치')
    air, o2 = r.get('dAir', []), r.get('dO2', [])
    if any(a <= 0 and o < 16 for a, o in zip(air, o2)): problems['순환'].append(f'{tag}: 예비 공기 바닥 · 사람 있는 곳 산소 {mn(o2, 99):.1f}kPa')
    if max(r.get('dHoleAge', [0]) or [0]) > 12: problems['순환'].append(f'{tag}: 구멍이 {max(r["dHoleAge"]):.0f}시간 막히지 않음')
    patch, hole = r.get('dPatch', []), r.get('dHoleAge', [])
    if sum(1 for p, h in zip(patch, hole) if p == 0 and h > 6) >= 2: problems['순환'].append(f'{tag}: 봉합 꾸러미 0인 채 구멍 (이틀 넘게)')
    br = r.get('dBroken', [])
    if len(br) >= 4 and br[-1] >= max(3, 2 * max(1, br[0])) and br[-1] >= br[-2] >= br[-3]: problems['순환'].append(f'{tag}: 고장 장치가 쌓임 {br[0]} → {br[-1]}')
    # ③ 값이 터지거나 굳지 않는다
    if r.get('nan', 0) > 0: problems['값'].append(f'{tag}: 숫자가 아닌 값 {r["nan"]}')
    if max(r.get('dStuck', [0]) or [0]) >= 2: problems['값'].append(f'{tag}: 여덟 시간 넘게 같은 일에 멈춘 사람 {max(r["dStuck"])}명')
    # ④ 지루하지 않다 (고정 문턱 — 볼 만한 일은 세계가 만든다)
    hl = r['hl']; days = max(1, len(hl))
    ok = sum(1 for h in hl if 3 <= h <= 12)
    if ok / days < 0.7: problems['지루함'].append(f'{tag}: 하이라이트 3~12인 날 {ok}/{days}')
    gap = r.get('gapH', r.get('quietH', 0))   # 함장 기준 (나): 작은 장면(20~80)도 볼 거리 — 하이라이트 문턱 80은 그대로
    if gap > 12: problems['지루함'].append(f'{tag}: {gap:.0f}시간 동안 볼 거리 없음 (작은 장면도 없음)')
    if r.get('orphans', 0) > 0: problems['저절로'].append(f'{tag}: 원인 없는 큰 일 {r["orphans"]}')
    # ⑤ 판단 (docs/REVIEW.md ③ — 사람 · 하루마다 · 아는 것으로 막히면 다른 길)
    if 'flips' in r:
        pd = max(1.0, r['crew'] * max(0.1, r['ran']))
        if r['flipMin'] / pd > 10: problems['판단'].append(f'{tag}: 흔들림 {r["flips"]}번 · {r["flipMin"]}분 (사람 하루 {r["flipMin"] / pd:.0f}분)')
        if r['failRepeats'] / pd > 2: problems['판단'].append(f'{tag}: 실패 뒤 30분 안에 같은 일을 다시 {r["failRepeats"]}번 (사람 하루 {r["failRepeats"] / pd:.1f})')
        if r['robotOffJobMin'] / max(0.1, r['ran']) > 60: problems['판단'].append(f'{tag}: 맡은 일을 두고 딴 일 한 로봇 {r["robotOffJobMin"]}분')
        if r['jobsOverdue'] > max(2, 0.2 * r['jobsDone']): problems['판단'].append(f'{tag}: 기한 넘긴 일감 {r["jobsOverdue"]} (끝낸 것 {r["jobsDone"]})')
        if r['personReraised'] > 0: problems['판단'].append(f'{tag}: 같은 사람의 사건이 30분 안에 다시 섬 {r["personReraised"]}')
        if r.get('robotHeldIdleMin', 0) / max(0.1, r['ran']) > 30: problems['판단'].append(f'{tag}: 로봇이 든 채 논 시간 {r["robotHeldIdleMin"]}분')
        if r.get('robotStuckMin', 0) / max(0.1, r['ran']) > 10: problems['판단'].append(f'{tag}: 막힌 채 맡김을 쥔 로봇 {r["robotStuckMin"]}분')
        if r.get('pathLoss', 0) > 5: problems['판단'].append(f'{tag}: 사건에 보낸 로봇의 길 손해 평균 {r["pathLoss"]}걸음 ({r.get("sent", 0)}번)')

print()
for key, title in [('사람', '버팀 ① 사람이 살아남는다 (한꺼번에 다 죽는 판 없음)'), ('순환', '버팀 ② 물자 · 설비가 순환한다'),
                   ('값', '버팀 ③ 값이 터지거나 굳지 않는다'), ('지루함', '버팀 ④ 지루하지 않다 (하이라이트 3~12인 날 70% · 작은 장면까지 쳐서 볼 거리 없는 시간 12시간 안)'),
                   ('저절로', '저절로 드러남 (원인 없는 큰 일 0)'),
                   ('판단', '판단 (흔들림 사람 하루 10분 안 · 같은 실패 되풀이 하루 2번 안 · 로봇이 맡은 일을 둔 시간 하루 60분 안 · 기한 넘긴 일감 20% 안 · 같은 사람 사건 다시 섬 0 · 로봇이 든 채 논 시간 하루 30분 안 · 막힌 채 맡김 하루 10분 안 · 길 손해 평균 5걸음 안)')]:
    p = problems[key]
    print(f'{mark(not p)} {title}' + ('' if not p else ' — ' + ' / '.join(p[:8]) + (f' 외 {len(p) - 8}' if len(p) > 8 else '')))

# 얽힘: 엔진 쌍마다 반응 (모든 판 합)
pairs = defaultdict(int)
for r in runs:
    for k, v in r.get('pairs', {}).items():
        a, b = map(int, k.split('>')); pairs[(a, b)] += v
outs = {e: {b for (a, b), v in pairs.items() if a == e and v >= 3 and b != e} for e in range(1, 14)}
ins = {e: {a for (a, b), v in pairs.items() if b == e and v >= 3 and a != e} for e in range(1, 14)}
weak = [ENG[e] for e in WORLD if len(outs[e]) < 2 or len(ins[e]) < 2]
live = sum(1 for (a, b), v in pairs.items() if v >= 3 and a in WORLD and b in WORLD)
print(f'{mark(not weak)} 얽힘 (엔진마다 다른 엔진 둘 넘게에 반응하고 · 둘 넘게를 움직인다): 세계 엔진 사이 반응하는 쌍 {live}/{len(WORLD) * (len(WORLD) - 1)}'
      + (f' · 덜 얽힌 엔진: {", ".join(weak)}' if weak else ''))
for e in WORLD:
    print(f'    {ENG[e]:5} ← {", ".join(ENG[a] for a in sorted(ins[e])) or "-"}  |  → {", ".join(ENG[b] for b in sorted(outs[e])) or "-"}')

worst = max(runs, key=lambda r: r['worstMs'])
print(f'{mark(worst["speed"] >= 60)} 60프레임: 가장 느린 배 {worst["ship"]} 틱 {worst["worstMs"]:.2f}ms → {worst["speed"]}배속')
deaths = sum(r['deaths'] for r in runs)
print(f'  (참고 · 난이도에 따라 다름) 죽음 {deaths}명 / {sum(r["crew"] for r in runs)}명 · 배 갈라짐 {sum(r.get("splits", 0) for r in runs)}번')
