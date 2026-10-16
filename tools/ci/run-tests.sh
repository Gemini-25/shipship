#!/usr/bin/env bash
# 시험 이름 목록(한 줄에 하나)을 동시에 N개씩 돌려 결과를 OUT/이름.txt 로 남긴다 (끝 줄: exit=코드 sec=초).
#   tools/ci/run-tests.sh <목록 파일> <출력 폴더> [동시에=3] [시드=7] [파일 뒤붙임]
#   HEADLESS=Headless.dll 경로 (기본 build/headless/Headless.dll) · TEST_TIMEOUT=초 (기본 3000)
set -u
LIST="$1"; OUT="$2"; JOBS="${3:-3}"; SEED="${4:-7}"; SUFFIX="${5:-}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
DLL="${HEADLESS:-$ROOT/build/headless/Headless.dll}"
mkdir -p "$OUT"
one() {
  local t="$1" t0
  t0=$(date +%s)
  timeout "${TEST_TIMEOUT:-3000}" dotnet "$DLL" 1 "$SEED" --"$t" > "$OUT/$t$SUFFIX.txt" 2>&1
  echo "exit=$? sec=$(( $(date +%s) - t0 ))" >> "$OUT/$t$SUFFIX.txt"
}
export -f one; export DLL OUT SEED SUFFIX
grep -v -e '^[[:space:]]*$' -e '^#' "$LIST" | xargs -P "$JOBS" -I{} bash -c 'one "$@"' _ {}
