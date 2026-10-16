#!/usr/bin/env python3
"""시험 목록을 N 조각으로 나눈다 (오래 걸리는 것부터 가장 가벼운 조각에 — 조각마다 걸리는 시간이 비슷하게).
   python3 tools/ci/shard.py tools/tests/tests.txt <조각 번호> <조각 수> [층,층]  → 그 조각의 시험 이름을 한 줄씩"""
import sys

def load(path, tiers=None):
    rows = []
    for line in open(path, encoding='utf-8'):
        line = line.strip()
        if not line or line.startswith('#'):
            continue
        name, tier, sec = (line.split() + ['scene', '60'])[:3]
        if tiers and tier not in tiers:
            continue
        rows.append((name, tier, int(sec)))
    return rows

if __name__ == '__main__':
    path, k, n = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
    tiers = set(sys.argv[4].split(',')) if len(sys.argv) > 4 else None
    load_of = [0] * n
    out = [[] for _ in range(n)]
    for name, _, sec in sorted(load(path, tiers), key=lambda r: -r[2]):
        i = min(range(n), key=lambda j: load_of[j])
        load_of[i] += sec
        out[i].append(name)
    print('\n'.join(out[k]))
