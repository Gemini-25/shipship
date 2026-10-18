#!/usr/bin/env python3
"""표본 수집(dotnet-trace speedscope)에서 시간을 먹는 함수를 뽑는다 — 제 시간(self) · 안에 든 시간(inclusive).
쓰기: tools/hot.sh 가 부른다 — python3 tools/hot.py trace.speedscope.json [보일 줄 수 · 기본 25] [이름 거르개 · 기본 ShipEngine]
"""
import json, sys, collections, re

path = sys.argv[1]
top = int(sys.argv[2]) if len(sys.argv) > 2 else 25
filt = sys.argv[3] if len(sys.argv) > 3 else 'ShipEngine'
d = json.load(open(path))
frames = [f['name'] for f in d['shared']['frames']]
selft, incl, callers = collections.Counter(), collections.Counter(), collections.defaultdict(collections.Counter)
total = 0.0
idle = re.compile(r'WaitHandle|Monitor\.Wait|Thread\.Sleep|SemaphoreSlim|ManualResetEvent|LowLevelLifoSemaphore|WaitOne|SpinWait', re.I)
pseudo = {'CPU_TIME', 'UNMANAGED_CODE_TIME'}
for p in d['profiles']:
    if p['type'] != 'evented': continue
    stack, last = [], None
    for e in p['events']:
        t = e['at']
        if stack and last is not None:
            dt = t - last
            real = [f for f in stack if frames[f] not in pseudo]
            top_f = frames[real[-1]] if real else frames[stack[-1]]
            if not any(idle.search(frames[f]) for f in stack[-3:]):
                total += dt
                selft[top_f] += dt
                own = next((frames[f] for f in reversed(real) if filt in frames[f]), None)   # 바깥 함수(라이브러리)를 부른 엔진 함수
                if own and own != top_f: callers[top_f][own] += dt
                for f in set(stack): incl[frames[f]] += dt
        if e['type'] == 'O': stack.append(e['frame'])
        else:
            if stack: stack.pop()
        last = t
def short(n):
    n = re.sub(r'\(.*', '', n)
    n = n.replace('ShipEngine.', '').replace('ShipEngine!', '')
    return n[-90:]
print(f'바쁜 시간 합 {total/1000:.1f}초 (기다림 빼고)')
print(f'\n— 제 시간 (함수 몸 안) 위 {top}')
for n, v in selft.most_common(top):
    print(f'{v/total*100:5.1f}%  {short(n)}')
    if filt not in n and callers[n]:   # 라이브러리 함수면 누가 불렀나
        print('         ← ' + ' · '.join(f'{short(c)} {w/total*100:.1f}%' for c, w in callers[n].most_common(3)))
print(f'\n— 안에 든 시간 ({filt}) 위 {top}')
k = 0
for n, v in incl.most_common():
    if filt not in n: continue
    print(f'{v/total*100:5.1f}%  {short(n)}'); k += 1
    if k >= top: break
