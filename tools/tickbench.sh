#!/usr/bin/env bash
# 틱 속도 (잡음 줄이기): 같은 배를 n번 돌려 가장 빠른 값 · 가운데 값 — 속도만 고친 뒤 견줄 때.
# 쓰기: tools/tickbench.sh [배 — 기본 Didim] [번 — 기본 5] [dll — 기본 build/lab/Lab.dll]
SHIP="${1:-Didim}"; N="${2:-5}"; DLL="${3:-build/lab/Lab.dll}"
for i in $(seq 1 "$N"); do dotnet "$DLL" --profile "$SHIP" 6 2 3000 --one --nolap | grep -o "틱마다 [0-9.]*ms" | grep -o "[0-9.]*ms" | tr -d ms; done | sort -n | awk '{a[NR]=$1} END {printf "%s 틱 %s번: 가장 빠름 %.3fms · 가운데 %.3fms\n", "'"$SHIP"'", NR, a[1], a[int((NR+1)/2)]}'
