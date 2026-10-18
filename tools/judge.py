#!/usr/bin/env python3
"""Lab --judge 결과(JUDGE 줄)를 묶어 '최상' 기준에 견준다 (docs/DEPTH.md 끝).
쓰기: python3 tools/judge.py 출력파일...  (여러 프로세스로 나눠 돌린 것을 함께 읽는다)"""
import json, sys
from collections import Counter, defaultdict

runs = []
for path in sys.argv[1:]:
    for line in open(path, encoding='utf8', errors='replace'):
        if line.startswith('JUDGE '):
            runs.append(json.loads(line[6:]))
if not runs:
    print('JUDGE 줄이 없다'); sys.exit(1)

def mark(ok): return '✔' if ok else '✘'

print(f'판 {len(runs)}개 · 배 {len({r["ship"] for r in runs})}척')
print('\n배 · 시드 · 날      산 사람   첫 죽음   반이 죽음   하이라이트(3~12인 날)   사고/날   가장 넓은 사슬   틱 ms(느린 시간) → 60프레임 배속')
for r in sorted(runs, key=lambda r: (r['ship'], r['seed'], r['days'])):
    days = max(1, len(r['hl']))
    ok = sum(1 for h in r['hl'] if 3 <= h <= 12)
    acc = sum(r['acc']) / days
    print(f'{r["ship"]:9} {r["seed"]:3} {r["ran"]:5.1f}/{r["days"]:<4g} {r["alive"]:3}/{r["crew"]:<3} '
          f'{(str(r["firstDeath"]) + "일") if r["firstDeath"] >= 0 else "-":>8} {(str(r["halfDead"]) + "일") if r["halfDead"] >= 0 else "-":>9}   '
          f'{sum(r["hl"]) / days:4.1f}/날 ({ok}/{days})        {acc:7.1f}   {r["maxEngines"]:2}개 (6개 넘게 {r["six"]})    {r["msTick"]:.2f}({r["worstMs"]:.2f}) → {r["speed"]}배')

# 1. 버팀: 끝까지 간 판 · 산 사람이 남은 판
full = [r for r in runs if r['ran'] >= r['days'] - 0.01]
survived = [r for r in full if r['alive'] > 0]
long_ = [r for r in runs if r['days'] >= 30]
long_ok = [r for r in long_ if r['ran'] >= r['days'] - 0.01 and r['alive'] * 2 >= r['crew']]
print(f'\n{mark(len(long_ok) == len(long_) and long_)} 버팀 (30~90일): 긴 항해 {len(long_)}판 중 끝까지 반 넘게 산 판 {len(long_ok)} · '
      + ' / '.join(f'{r["ship"]} {r["seed"]} {r["ran"]:.1f}일 산 {r["alive"]}/{r["crew"]}' for r in long_))
# 2. 죽음 원인 갈래
causes = Counter()
for r in runs:
    for k, v in r['causes'].items(): causes[k] += v
print(f'{mark(len(causes) >= 5)} 죽음 원인 다섯 갈래 이상: {len(causes)}갈래 — ' + ' · '.join(f'{k} {v}' for k, v in causes.most_common()))
# 3. 하루 하이라이트 3~12 · 사건과 사람 섞임
days_all = sum(len(r['hl']) for r in runs)
in_range = sum(1 for r in runs for h in r['hl'] if 3 <= h <= 12)
mixed = sum(1 for r in runs for h, p in zip(r['hl'], r['hlPeople']) if 0 < p < h)
print(f'{mark(days_all and in_range / days_all >= 0.7)} 하루 하이라이트 3~12: {in_range}/{days_all}일 ({in_range / max(1, days_all):.0%}) · 평균 {sum(sum(r["hl"]) for r in runs) / max(1, days_all):.1f}개 · 사건과 사람이 섞인 날 {mixed / max(1, days_all):.0%}')
# 4. 엔진 여섯 개를 건넌 사슬
six = [r for r in runs if r['six'] > 0]
print(f'{mark(bool(six))} 엔진 여섯 개 넘게 건넌 사슬: {len(six)}/{len(runs)}판 · 가장 넓은 것 {max(r["maxEngines"] for r in runs)}개')
# 5. 배가 갈라져도 조각마다 산 사람
splits = [r for r in runs if r.get('splits', 0) > 0]
saved = [r for r in splits if r['alive'] > 0]
print(f'{mark(bool(saved))} 배가 갈라져도 산 사람: 갈라진 판 {len(splits)} · 그중 산 사람이 남은 판 {len(saved)}'
      + (' — ' + ' / '.join(f'{r["ship"]} {r["seed"]}: 갈라질 때 {r["aliveAtSplit"]} → 끝 {r["alive"]}' for r in splits) if splits else ''))
# 6. 사고의 잦기와 크기 (가혹함 = 잦기 · 크기)
acc_day = sum(sum(r['acc']) for r in runs) / max(1, days_all)
hurt_day = sum(sum(r['hurt']) for r in runs) / max(1, days_all)
deaths = sum(r['deaths'] for r in runs)
print(f'  사고 하루 {acc_day:.1f}건 · 다침/쓰러짐 하루 {hurt_day:.2f} · 죽음 {deaths} (사람 {sum(r["crew"] for r in runs)}명 중)')
# 7. 60프레임
by_ship = defaultdict(list)
for r in runs: by_ship[r['ship']].append(r)
worst = max(runs, key=lambda r: r['worstMs'])
print(f'{mark(worst["speed"] >= 60)} 60프레임: 가장 느린 배 {worst["ship"]} 틱 {worst["worstMs"]:.2f}ms → 60프레임을 지키며 {worst["speed"]}배속 · '
      + ' · '.join(f'{s} {max(x["worstMs"] for x in v):.2f}ms' for s, v in sorted(by_ship.items())))
# 8. 원인 없는 큰 일 (저절로 드러남)
orph = sum(r['orphans'] for r in runs)
print(f'{mark(orph == 0)} 원인 없는 큰 일: {orph}')
