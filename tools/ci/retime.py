#!/usr/bin/env python3
"""CI가 잰 시험별 초를 tools/tests/tests.txt에 옮긴다 (조각을 고르게 나누는 데 쓴다).
   python3 tools/ci/retime.py [--replace] < 시간줄.txt   (tools/ci/last.sh --times 의 '시간 이름=초 …' 줄)
   한 번 잰 값은 ±30% 흔들려 (같은 시험이 130초 · 192초) 기본은 지금 값과 반씩 섞는다 (--replace: 잰 값 그대로).
   잰 값이 없거나 0인 시험은 그대로 둔다. 순서 · 층 · 넷째 칸(solo) · 주석도 그대로."""
import os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
path = os.path.join(ROOT, 'tools/tests/tests.txt')
text = sys.stdin.read()
m = re.search(r'시간 ((?:\S+=\d+\s*)+)', text)
if not m:
    sys.exit('시간 줄이 없다')
secs = {k: int(v) for k, v in (p.split('=') for p in m.group(1).split())}
replace = '--replace' in sys.argv
out, changed = [], 0
for line in open(path, encoding='utf-8'):
    parts = line.split()
    if line.lstrip().startswith('#') or len(parts) < 3 or secs.get(parts[0], 0) <= 0:
        out.append(line)
        continue
    new = max(1, secs[parts[0]] if replace else round((int(parts[2]) + secs[parts[0]]) / 2))
    changed += int(parts[2]) != new
    out.append(' '.join([parts[0], parts[1], str(new)] + parts[3:]) + '\n')
open(path, 'w', encoding='utf-8').writelines(out)
print(f'{changed}개 바뀜 · 잰 시험 {len(secs)}개')
