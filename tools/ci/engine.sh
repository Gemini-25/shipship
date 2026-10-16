#!/usr/bin/env bash
# 새 엔진 CI(engine.yml) 결과 몇 줄: 상태 · 걸린 시간 · 단계별 결과 (긴 로그는 받지 않는다)
#   tools/ci/engine.sh          가장 최근 실행
#   tools/ci/engine.sh --wait   지금 커밋(HEAD)의 실행이 끝날 때까지 1분마다 (많아야 30분)
set -u
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
REPO=$(git -C "$ROOT" remote get-url origin | sed -E 's#^.*github\.com[:/]##; s#\.git$##')
BRANCH=$(git -C "$ROOT" rev-parse --abbrev-ref HEAD)
RUN=""
if [ "${1:-}" = "--wait" ]; then
  SHA=$(git -C "$ROOT" rev-parse HEAD)
  for i in $(seq 1 30); do
    st=$(gh api "repos/$REPO/actions/workflows/engine.yml/runs?per_page=5&branch=$BRANCH" \
         --jq "[.workflow_runs[] | select(.head_sha == \"$SHA\")][0] | \"\(.id) \(.status)\"" 2>/dev/null)
    case "$st" in *" completed") RUN=${st%% *}; break ;; esac
    sleep 60
  done
  [ -n "$RUN" ] || { echo "(30분 안에 끝나지 않았다: ${SHA:0:7} · $st)"; exit 1; }
fi
[ -n "$RUN" ] || RUN=$(gh api "repos/$REPO/actions/workflows/engine.yml/runs?per_page=1&branch=$BRANCH" --jq '.workflow_runs[0].id')
gh api "repos/$REPO/actions/runs/$RUN" --jq '"실행 #\(.run_number) \(.head_sha[0:7]) · \(.status) \(.conclusion // "") · \(.run_started_at) → \(.updated_at)"'
gh api "repos/$REPO/actions/runs/$RUN/jobs" --jq '.jobs[] | .steps[] | select(.name != null) | "  \(if .conclusion == "success" then "✔" elif .conclusion == "failure" then "✘" else "·" end) \(.name)"' | grep -v "Set up job\|Post \|Complete job"
