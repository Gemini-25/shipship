#!/usr/bin/env python3
"""시험 결과를 한 장으로 줄인다 (사람 · 에이전트가 긴 출력 대신 이것만 읽는다).

   python3 tools/ci/summarize.py --results 결과폴더 [--recheck 재확인폴더] [--plan 새실패.json] [--md 요약.md] [--only rule,core] [--times]
   --plan: 다시 볼 장면을 오래 걸리는 것부터 조각(--recheck-shards, 기본 5)에 고르게 — 조각마다 '이름 이름 …' 한 줄인 JSON 목록
   --times: 시험별 걸린 초를 한 줄로 ('시간 이름=초 …' — tools/ci/retime.py가 tests.txt에 옮긴다)

판정 (docs/DEV_PROCESS.md):
  · 줄 맨 앞의 ✘ 가 실패한 확인 — '성능' 줄은 따로 센다 (동시에 돌리면 흔들린다)
  · rule(규칙) · core(핵심) 실패나 멈춤/예외 → 빨강 (막음 · 바로 조사)
  · scene(장면) 실패는 기준(baseline.txt)과 견준다 — 기준에서도 실패면 '원래 실패', 아니면 '새 실패'
    새 실패는 다른 시드 둘(11 · 23)로 지금 코드와 기준 커밋을 함께 다시 돌린다:
      그 시드에서 기준은 통과 · 지금은 실패 → '조사 필요' · 지금 둘 다 통과 → '운' · 기준도 같은 시드에서 실패 → '시드 탓'
종료 코드: 빨강이 하나라도 있으면 1."""
import argparse, json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
TOTAL = re.compile(r'^\s*✘\s*(\d+\s*개\s*실패|실패\s*\d+)\s*$')
CRASH = {'124': '시간 초과', '134': '중단', '137': '죽음', '139': '세그폴트'}


def read_tests(path):
    rows = []
    for line in open(path, encoding='utf-8'):
        line = line.strip()
        if line and not line.startswith('#'):
            name, tier, sec = (line.split() + ['scene', '60'])[:3]
            rows.append((name, tier, int(sec)))
    return rows


def find_files(folder):
    found = {}
    for d, _, files in os.walk(folder):
        for f in files:
            if f.endswith('.txt'):
                found[f[:-4]] = os.path.join(d, f)
    return found


def judge(path):
    """(상태, 첫 실패 줄, 초) — 상태: 통과 · 실패 · 성능만 · 멈춤 · 없음"""
    if not path:
        return '없음', '', 0
    text = open(path, encoding='utf-8', errors='replace').read()
    m = re.search(r'exit=(\d+)(?: sec=(\d+))?', text)
    sec = int(m.group(2)) if m and m.group(2) else 0
    if not m:
        return '멈춤', '끝 줄 없음', sec
    if m.group(1) in CRASH:
        return '멈춤', CRASH[m.group(1)], sec
    # 줄 맨 앞 ✘ = 실패한 확인 · 합계 줄('✘ 1개 실패' · '✘ 실패 2')은 세지 않는다 (성능 줄만 실패해도 합계가 찍힌다)
    fails = [l.strip() for l in text.splitlines() if re.match(r'^\s*✘', l) and not TOTAL.match(l)]
    real = [l for l in fails if '성능' not in l]
    if real:
        return '실패', real[0], sec
    if fails:
        return '성능만', fails[0], sec
    if m.group(1) != '0' and 'Unhandled exception' in text:
        return '멈춤', '예외', sec
    return '통과', '', sec


def short(s, n=110):
    s = s.lstrip('✘ ').replace('|', '/')
    return s if len(s) <= n else s[:n] + '…'


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--results', required=True)
    ap.add_argument('--tests', default=os.path.join(ROOT, 'tools/tests/tests.txt'))
    ap.add_argument('--baseline', default=os.path.join(ROOT, 'tools/tests/baseline.txt'))
    ap.add_argument('--recheck')
    ap.add_argument('--plan')
    ap.add_argument('--md')
    ap.add_argument('--only')
    ap.add_argument('--max-recheck', type=int, default=20)
    ap.add_argument('--recheck-shards', type=int, default=5)
    ap.add_argument('--times', action='store_true')
    a = ap.parse_args()

    tiers = set(a.only.split(',')) if a.only else None
    tests = [r for r in read_tests(a.tests) if not tiers or r[1] in tiers]
    base = {l.strip() for l in open(a.baseline, encoding='utf-8') if l.strip() and not l.startswith('#')}
    files = find_files(a.results)
    rfiles = find_files(a.recheck) if a.recheck else {}

    red, new, known, fixed, perf, lucky, investigate, seedish = [], [], [], [], [], [], [], []
    passed = total_sec = 0
    slow = []
    secs = {}
    for name, tier, est in tests:
        st, line, sec = judge(files.get(name))
        secs[name] = sec if sec else est
        total_sec += sec
        slow.append((sec, name))
        if st == '통과':
            passed += 1
            if tier == 'scene' and name in base:
                fixed.append(name)
        elif st == '성능만':
            passed += 1
            perf.append((name, line))
        elif st in ('멈춤', '없음') or tier in ('rule', 'core'):
            red.append((name, tier, st, line))
        elif name in base:
            known.append((name, line))
        else:
            new.append((name, line))
            cur = [judge(rfiles.get(f'{name}.s{s}'))[0] for s in (11, 23)]
            base = [judge(rfiles.get(f'{name}.b{s}'))[0] for s in (11, 23)]
            if any(s == '없음' for s in cur):
                continue
            bad = lambda s: s in ('실패', '멈춤')
            if not any(bad(c) for c in cur):
                lucky.append((name, cur, base))
            elif all(b == '없음' for b in base) or any(bad(c) and not bad(b) for c, b in zip(cur, base)):
                investigate.append((name, cur, base))  # 기준은 그 시드에서 통과 · 지금은 실패 (또는 기준을 못 돌렸다)
            else:
                seedish.append((name, cur, base))  # 지금 실패한 시드에서 기준도 실패 — 시드 7에 맞춘 장면

    if a.plan:
        # 조각마다 지금 코드 · 기준 커밋 × 시드 둘 = 4개를 동시에 돌린다 — 오래 걸리는 것부터 가장 가벼운 조각에
        pick = sorted([n for n, _ in new][:a.max_recheck], key=lambda n: -secs.get(n, 60))
        k = min(a.recheck_shards, len(pick))
        groups, load = [[] for _ in range(k)], [0] * k
        for n in pick:
            i = min(range(k), key=lambda j: load[j])
            groups[i].append(n)
            load[i] += secs.get(n, 60)
        json.dump([' '.join(g) for g in groups], open(a.plan, 'w'))
    if a.times:
        print('시간 ' + ' '.join(f'{n}={secs[n]}' for n, _, _ in tests))
        return 0

    out = []
    def seeds(c, b):
        return ' · '.join(f'시드 {sd}: 지금 {x}' + (f' / 기준 {y}' if y != '없음' else '') for sd, x, y in zip((11, 23), c, b))

    head = '🔴' if red else ('🟡' if investigate else '🟢')
    out.append(f'## {head} 시험 {passed}/{len(tests)} 통과 · 빨강 {len(red)} · 새 실패 {len(new)} (조사 필요 {len(investigate)} · 운 {len(lucky)} · 시드 탓 {len(seedish)}) · 원래 실패 {len(known)} · 기준보다 나아짐 {len(fixed)}')
    out.append(f'시험 시간 합 {total_sec // 60}분 · 오래 걸린 것: ' + ', '.join(f'{n} {s}초' for s, n in sorted(slow, reverse=True)[:5]))
    if red:
        out.append('\n### 🔴 막음 — 규칙 · 핵심 실패 또는 멈춤 (진짜 버그)')
        out += [f'- **{n}** ({t} · {st}) — {short(l)}' for n, t, st, l in red]
    if investigate:
        out.append('\n### 🟡 조사 필요 — 기준에선 통과 · 지금 실패 · 다른 시드에서도 (기준은 통과하는데) 실패')
        out += [f'- **{n}** — {seeds(c, b)}' for n, c, b in investigate]
    if new:
        out.append('\n### 새 실패 (기준 대비)')
        out += [f'- {n} — {short(l)}' for n, l in new]
    if lucky:
        out.append('\n운으로 본다 (다른 시드 둘 다 통과): ' + ', '.join(n for n, _, _ in lucky))
    if seedish:
        out.append('\n시드 탓으로 본다 (지금 실패한 시드에서 기준 커밋도 실패): ' + ', '.join(f'{n} ({seeds(c, b)})' for n, c, b in seedish))
    if fixed:
        out.append('\n기준에선 실패 · 지금 통과: ' + ', '.join(fixed))
    if perf:
        out.append('\n성능 줄만 실패 (동시에 돌려 흔들림 — 따로 재 봄): ' + ', '.join(n for n, _ in perf))
    if known:
        out.append(f'\n<details><summary>원래 실패 {len(known)}개 (기준에서도 실패)</summary>\n')
        out += [f'- {n} — {short(l, 90)}' for n, l in known]
        out.append('</details>')
    md = '\n'.join(out) + '\n'
    if a.md:
        open(a.md, 'w', encoding='utf-8').write(md)
    print(md)
    return 1 if red else 0


if __name__ == '__main__':
    sys.exit(main())
