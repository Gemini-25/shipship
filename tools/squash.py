#!/usr/bin/env python3
"""긴 출력에서 되풀이되는 줄 · 줄 묶음을 접는다 (숫자 · 시각만 다른 줄은 같은 줄로 본다).
쓰기: dotnet build/lab/Lab.dll --rule 2>&1 | python3 tools/squash.py [--width 260]
"""
import re, sys, signal
signal.signal(signal.SIGPIPE, signal.SIG_DFL)   # head로 잘라 읽어도 조용히 끝난다

width = 260
if '--width' in sys.argv: width = int(sys.argv[sys.argv.index('--width') + 1])
lines = [l.rstrip('\n') for l in sys.stdin]
key = [re.sub(r'\d+(\.\d+)?', '#', l) for l in lines]

out, i, n = [], 0, len(lines)
while i < n:
    best_len, best_rep = 0, 0
    for L in range(1, 9):                       # 1~8줄 묶음이 바로 뒤에 되풀이되나
        if i + 2 * L > n: break
        rep = 1
        while i + (rep + 1) * L <= n and key[i + rep * L:i + (rep + 1) * L] == key[i:i + L]: rep += 1
        if rep > 1 and rep * L > best_rep * best_len: best_len, best_rep = L, rep
    if best_rep > 1:
        out += lines[i:i + best_len]
        last = lines[i + best_rep * best_len - 1]
        out.append(f'      … 위 {best_len}줄이 {best_rep - 1}번 더 되풀이 (마지막: {last.strip()[:80]})')
        i += best_rep * best_len
    else:
        out.append(lines[i]); i += 1
for l in out: print(l[:width])
