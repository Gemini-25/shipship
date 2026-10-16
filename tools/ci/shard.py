#!/usr/bin/env python3
"""시험 목록을 N 조각으로 나눈다 (오래 걸리는 것부터 가장 가벼운 조각에 — 조각마다 걸리는 시간이 비슷하게).
   python3 tools/ci/shard.py tools/tests/tests.txt <조각 번호> <조각 수> [층,층]  → 그 조각의 시험 이름을 한 줄씩
   넷째 칸이 solo인 시험(안에서 자식 프로세스로 CPU를 다 쓰는 growthtest)은 조각 하나를 혼자 쓴다 — 앞 조각부터."""
import sys

def load(path, tiers=None):
    rows = []
    for line in open(path, encoding='utf-8'):
        line = line.strip()
        if not line or line.startswith('#'):
            continue
        p = line.split()
        name, tier, sec, flag = p[0], (p[1:2] or ['scene'])[0], int((p[2:3] or ['60'])[0]), (p[3:4] or [''])[0]
        if tiers and tier not in tiers:
            continue
        rows.append((name, tier, sec, flag))
    return rows

def split(rows, n):
    solo = [r for r in rows if r[3] == 'solo']
    if len(solo) >= n:
        solo = []
    out = [[r[0]] for r in solo] + [[] for _ in range(n - len(solo))]
    load_of = [r[2] for r in solo] + [0] * (n - len(solo))
    free = range(len(solo), n)
    for name, _, sec, _ in sorted((r for r in rows if r not in solo), key=lambda r: -r[2]):
        i = min(free, key=lambda j: load_of[j])
        load_of[i] += sec
        out[i].append(name)
    return out

if __name__ == '__main__':
    path, k, n = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
    tiers = set(sys.argv[4].split(',')) if len(sys.argv) > 4 else None
    print('\n'.join(split(load(path, tiers), n)[k]))
