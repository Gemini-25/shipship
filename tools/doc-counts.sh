#!/usr/bin/env bash
# 시험 개수를 문서에 맞춘다 (README · docs/ENGINE.md): 법칙 · 규칙 시험을 세어 "법칙 51 · 규칙 N" 꼴의 글을 고친다.
# 쓰기: tools/doc-counts.sh [실험실 dll — 기본 build/lab/Lab.dll]
set -eu
# 이미 센 수가 있으면: tools/doc-counts.sh --set 법칙 규칙 (시험을 다시 돌리지 않는다)
if [ "${1:-}" = "--set" ]; then laws="$2"; rules="$3"; else
DLL="${1:-build/lab/Lab.dll}"
laws=$(dotnet "$DLL" --law 2>/dev/null | grep -oE "통과 [0-9]+ · 실패 [0-9]+" | tail -1 | awk '{s=$2+$5} END {print s}')
rules=$(tools/rules.sh "$DLL" 4 | grep -oE "통과 [0-9]+ · 실패 [0-9]+" | tail -1 | awk '{s=$2+$5} END {print s}')
fi
sed -i -E "s/법칙 [0-9]+ · 규칙 [0-9]+ 모두 통과/법칙 $laws · 규칙 $rules 모두 통과/" README.md
sed -i -E "s/법칙 시험 [0-9]+ · 규칙 시험 [0-9]+/법칙 시험 $laws · 규칙 시험 $rules/" docs/ENGINE.md
echo "법칙 $laws · 규칙 $rules"
