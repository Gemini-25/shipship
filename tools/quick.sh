#!/usr/bin/env bash
# 빠른 묶음 (기능 하나 넣을 때마다): 빌드 → 규칙 시험 + 핵심 장면 15개를 동시에 4개씩 → 한 장 요약.
#   tools/quick.sh [출력 폴더=build/quick] [더 돌릴 시험 이름 ...]
# 전체 시험은 GitHub Actions가 푸시마다 돌린다 (.github/workflows/tests.yml · docs/DEV_PROCESS.md).
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-$ROOT/build/quick}"; shift || true
export HEADLESS="$ROOT/build/headless/Headless.dll"
dotnet build "$ROOT/tools/Headless" -c Release -o "$ROOT/build/headless" --nologo -v q 2>&1 | grep -E " error |rror\(s\)" || true
[ -f "$HEADLESS" ] || { echo "빌드 실패"; exit 2; }
mkdir -p "$OUT"
rm -f "${OUT:?}"/*.txt
LIST="$OUT/list"
# 오래 걸리는 것부터 (가장 긴 시험이 맨 끝에 혼자 돌지 않게)
awk '!/^#/ && ($2=="rule" || $2=="core") {print $3, $1}' "$ROOT/tools/tests/tests.txt" | sort -rn | awk '{print $2}' > "$LIST"
for t in "$@"; do echo "$t" >> "$LIST"; done
"$ROOT/tools/ci/run-tests.sh" "$LIST" "$OUT" 4 7
ONLY="rule,core"; [ $# -gt 0 ] && ONLY=""
python3 "$ROOT/tools/ci/summarize.py" --results "$OUT" --md "$OUT/summary.md" ${ONLY:+--only $ONLY}
