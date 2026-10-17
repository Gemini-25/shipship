#!/usr/bin/env bash
# 규칙 시험을 n 갈래로 함께 돌린다 (묶음 시간표 tools/rule-times.txt대로 고르게) — 실패 줄과 합계만 보인다.
# 쓰기: tools/rules.sh [실험실 dll — 기본 build/lab/Lab.dll] [갈래 수 — 기본 4]   · 시간표 다시 재기: RULE_TIMES=1 dotnet <dll> --rule | grep -P '^⏱' | cut -f2,3 > tools/rule-times.txt
set -u
DLL="${1:-build/lab/Lab.dll}"; N="${2:-4}"
DIR="$(mktemp -d)"
start=$(date +%s)
for i in $(seq 0 $((N-1))); do
  dotnet "$DLL" --rule --shard "$i/$N" > "$DIR/$i.txt" 2>&1 &
done
wait
fail=0; pass=0; code=0
for i in $(seq 0 $((N-1))); do
  f="$DIR/$i.txt"
  grep -E "^\s*✘ [^통]|✘ 멈춤" "$f" && code=1
  line=$(grep -E "통과 [0-9]+ · 실패 [0-9]+" "$f" | tail -1)
  if [ -z "$line" ]; then echo "✘ 갈래 $i: 끝 줄이 없다 (멈췄다)"; tail -20 "$f"; code=1; continue; fi
  p=$(echo "$line" | sed -E 's/.*통과 ([0-9]+).*/\1/'); q=$(echo "$line" | sed -E 's/.*실패 ([0-9]+).*/\1/')
  pass=$((pass+p)); fail=$((fail+q))
done
[ "$fail" -gt 0 ] && code=1
echo ""
echo "$([ $code -eq 0 ] && echo ✔ || echo ✘) 통과 $pass · 실패 $fail · $(( $(date +%s) - start ))초 ($N 갈래)"
rm -rf "$DIR"
exit $code
