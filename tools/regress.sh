#!/usr/bin/env bash
# 긴 회귀를 한 번에 돌린다 (v10.9). 결과는 OUT 폴더에 하나씩 남는다.
#   tools/regress.sh [출력 폴더] [동시에 돌릴 수=2]
# 빌드: dotnet build -c Release tools/Headless
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-$ROOT/build/regress}"
JOBS="${2:-2}"
H="dotnet $ROOT/tools/Headless/bin/Release/net8.0/Headless.dll"
mkdir -p "$OUT"
run() { local name="$1"; shift; local t0=$(date +%s); $H "$@" > "$OUT/$name.txt" 2>&1; echo "exit=$? sec=$(( $(date +%s) - t0 ))" >> "$OUT/$name.txt"; echo "끝: $name"; }
export -f run; export H OUT
cat <<LIST | xargs -P "$JOBS" -L 1 bash -c 'run "$@"' _
selftest 1 20260929 --selftest
scenarios 1 20260929 --scenario=all
exp1 1 20260929 --experiment=20
exp3 1 20260929 --experiment=20 --waves=3
exp3death 1 20260929 --experiment=20 --waves=3 --death
gate_order 30 20260929 --gate=order --quiet
gate_kind 30 20260929 --gate=kind --quiet
gate_structure 1 20260929 --gate=structure --runs=20
gate_pipes 1 20260929 --gate=pipes --runs=20
gate_auto 1 20260929 --gate=auto
gate_comms 1 20260929 --gate=comms --runs=12
gate_partition 1 20260929 --gate=partition --runs=12
peace90 90 20260929 --quiet
peace30_7 30 7 --quiet
peace30_42 30 42 --quiet
rhythm 40 7 --rhythm=chaos --quiet
savetest 12 20260929 --savetest
scar_20260929 60 20260929 --scarcity --quiet
scar_11 60 11 --scarcity --quiet
scar_22 60 22 --scarcity --quiet
scar_33 60 33 --scarcity --quiet
scar_44 60 44 --scarcity --quiet
scar_55 60 55 --scarcity --quiet
ships10 10 20260929 --ships
LIST
