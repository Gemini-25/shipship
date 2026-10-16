#!/usr/bin/env python3
"""CI가 잰 시험별 초를 tools/tests/tests.txt에 옮긴다 (조각을 고르게 나누는 데 쓴다).
   python3 tools/ci/retime.py < 시간줄.txt      (report 작업 로그의 '시간 이름=초 …' 줄 — 앞에 붙은 로그 시각은 무시)
   잰 값이 없거나 0인 시험은 그대로 둔다. 순서 · 층 · 주석도 그대로."""
import os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
path = os.path.join(ROOT, 'tools/tests/tests.txt')
text = sys.stdin.read()
m = re.search(r'시간 ((?:\S+=\d+\s*)+)', text)
if not m:
    sys.exit('시간 줄이 없다')
secs = {k: int(v) for k, v in (p.split('=') for p in m.group(1).split())}
out, changed = [], 0
for line in open(path, encoding='utf-8'):
    parts = line.split()
    if line.lstrip().startswith('#') or len(parts) < 3 or secs.get(parts[0], 0) <= 0:
        out.append(line)
        continue
    new = max(1, secs[parts[0]])
    changed += int(parts[2]) != new
    out.append(f'{parts[0]} {parts[1]} {new}\n')
open(path, 'w', encoding='utf-8').writelines(out)
print(f'{changed}개 바뀜 · 잰 시험 {len(secs)}개')
