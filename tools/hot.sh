#!/usr/bin/env bash
# 어디가 느린가: Lab을 표본 수집으로 돌리고 시간을 먹는 함수를 뽑는다 (dotnet-trace — 없으면 깐다).
# 쓰기: tools/hot.sh [Lab 인자 — 기본 --profile Didim 6 2 3000 --one --nolap]   예) tools/hot.sh --rule  (ONLY=묶음과 함께)
set -eu
export PATH="$PATH:$HOME/.dotnet/tools"
command -v dotnet-trace >/dev/null || dotnet tool install --global dotnet-trace >/dev/null
OUT="$(mktemp -d)"
ARGS=("$@"); [ ${#ARGS[@]} -eq 0 ] && ARGS=(--profile Didim 6 2 3000 --one --nolap)
dotnet-trace collect --format Speedscope -o "$OUT/t.nettrace" -- dotnet build/lab/Lab.dll "${ARGS[@]}" > "$OUT/log.txt" 2>&1 || { tail -5 "$OUT/log.txt"; exit 1; }
python3 "$(dirname "$0")/hot.py" "$OUT/t.speedscope.json" "${HOT_TOP:-25}"
rm -rf "$OUT"
