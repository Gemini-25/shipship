#!/usr/bin/env bash
# 이 가지의 가장 최근 CI 실행 결과를 몇 줄로 (report 작업이 남긴 주석만 읽는다 — 긴 로그 · 아티팩트를 받지 않는다).
#   tools/ci/last.sh            상태 · 걸린 시간 · 한 줄 요약 · 조사할 것
#   tools/ci/last.sh --times    시험별 걸린 초 한 줄 (→ python3 tools/ci/retime.py 로 tests.txt에)
#   tools/ci/last.sh --run ID   그 실행
#   tools/ci/last.sh --wait     지금 커밋(HEAD)의 실행이 끝날 때까지 1분마다 보고 기다린다 (많아야 1시간 · 백그라운드로 띄워 두면 끝날 때 알 수 있다)
# gh api 를 쓴다 (GitHub CLI 또는 같은 꼴의 gh).
set -u
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
REPO=$(git -C "$ROOT" remote get-url origin | sed -E 's#^.*github\.com[:/]##; s#\.git$##')
BRANCH=$(git -C "$ROOT" rev-parse --abbrev-ref HEAD)
# 작업 흐름: 이 가지에서 돈 적 있는 것 (새 엔진 가지는 engine.yml · 옛 게임 가지는 tests.yml) — CI_WORKFLOW로 정할 수 있다
WF=${CI_WORKFLOW:-}
if [ -z "$WF" ]; then
  WF=tests.yml
  n=$(gh api "repos/$REPO/actions/workflows/tests.yml/runs?per_page=1&branch=$BRANCH" --jq '.total_count' 2>/dev/null || echo 0)
  [ "${n:-0}" = "0" ] && WF=engine.yml
fi
MODE=summary RUN="" WAIT=0
while [ $# -gt 0 ]; do
  case "$1" in --times) MODE=times ;; --run) RUN="$2"; shift ;; --wait) WAIT=1 ;; esac
  shift
done
if [ "$WAIT" = 1 ] && [ -z "$RUN" ]; then
  SHA=$(git -C "$ROOT" rev-parse HEAD)
  for i in $(seq 1 60); do
    st=$(gh api "repos/$REPO/actions/workflows/$WF/runs?per_page=5&branch=$BRANCH" \
         --jq "[.workflow_runs[] | select(.head_sha == \"$SHA\")][0] | \"\(.id) \(.status)\"" 2>/dev/null)
    case "$st" in *" completed") RUN=${st%% *}; break ;; esac
    sleep 60
  done
  [ -n "$RUN" ] || { echo "(한 시간 안에 끝나지 않았다: ${SHA:0:7} · $st)"; exit 1; }
fi
if [ -z "$RUN" ]; then
  RUN=$(gh api "repos/$REPO/actions/workflows/$WF/runs?per_page=1&branch=$BRANCH" --jq '.workflow_runs[0].id')
fi
gh api "repos/$REPO/actions/runs/$RUN" > /tmp/last-run.$$.json
gh api "repos/$REPO/actions/runs/$RUN/jobs?per_page=100" > /tmp/last-jobs.$$.json
REPORT=$(python3 - "$MODE" /tmp/last-run.$$.json /tmp/last-jobs.$$.json <<'EOF'
import json, sys, datetime
mode, run, jobs = sys.argv[1], json.load(open(sys.argv[2])), json.load(open(sys.argv[3]))['jobs']
P = lambda s: datetime.datetime.fromisoformat(s.replace('Z', '+00:00')) if s else None
def mmss(sec): return f'{int(sec) // 60}분 {int(sec) % 60}초'
end = P(run['updated_at']) if run['status'] == 'completed' else datetime.datetime.now(datetime.timezone.utc)
if mode == 'summary':
    state = '끝 (' + str(run['conclusion']) + ')' if run['status'] == 'completed' else run['status']
    shards = [(P(j['completed_at']) - P(j['started_at'])).total_seconds() for j in jobs
              if j['name'].startswith('shard') and j['started_at'] and j['completed_at']]
    line = f"실행 #{run['run_number']} {run['head_sha'][:7]} · {state} · {mmss((end - P(run['run_started_at'])).total_seconds())}"
    if shards: line += f' · 조각 {len(shards)}개 최장 {mmss(max(shards))} · 최단 {mmss(min(shards))}'
    print(line, file=sys.stderr)
rep = [j for j in jobs if j['name'] == 'report' and j['status'] == 'completed']
print(rep[0]['id'] if rep else '')
EOF
)
rm -f /tmp/last-run.$$.json /tmp/last-jobs.$$.json
[ -n "$REPORT" ] || { echo "(report 작업이 아직 끝나지 않았다)"; exit 0; }
gh api "repos/$REPO/check-runs/$REPORT/annotations?per_page=50" > /tmp/last-ann.$$.json
python3 - "$MODE" /tmp/last-ann.$$.json <<'EOF'
import json, sys
mode, ann = sys.argv[1], json.load(open(sys.argv[2]))
for a in ann:
    t, m = a.get('title') or '', a.get('message') or ''
    if mode == 'times':
        if t == '시간' and m.startswith('시간 '): print(m)
    elif t in ('요약', '재확인') or (t == '시간' and not m.startswith('시간 ')):
        print(m)
    elif t == '조사':
        print(f'- {m}')
EOF
rm -f /tmp/last-ann.$$.json
