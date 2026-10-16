# 작업 규칙 (에이전트용)

- 사용자와는 한국어로. 게임 안 글에는 개발 용어를 쓰지 않는다 (지휘자는 "함장").
- GitHub에 올린 것은 늘 빌드되고 돌아가야 한다. 실행 파일은 올리지 않는다.
- 새 배 설계는 초안으로 먼저 보이고 승인을 받는다. 로봇 · 주 컴퓨터가 먼저, 사람은 그게 안 될 때 나선다. 60프레임 유지.
- README 맨 위는 늘 최신 진행 상황(진행률 %) · 화면 사진.

## 시험 · 개발 과정 — docs/DEV_PROCESS.md
1. 기능 하나 넣을 때마다: 빌드(게임 `dotnet build game/ShipSim.csproj` · 도구 `dotnet build tools/Headless -c Release`) + 그 기능 시험 + `tools/quick.sh`.
   quick이 빨강이면(규칙 · 핵심 실패) 진짜 버그로 보고 고친다.
2. 새 기능 · 고친 버그마다 `tools/Headless/RuleTests.cs`에 규칙 시험을 하나 더한다 (작은 배 · 몇 초 · 고치기 전 코드에서 실패하는지 확인).
3. 전체 시험은 직접 돌리지 않는다 — 푸시하면 GitHub Actions `tests`가 돈다. 결과는 `tools/ci/last.sh`(몇 줄 — 상태 · 걸린 시간 · 한 줄 요약 · 조사할 것)만 읽는다 (푸시 뒤 `tools/ci/last.sh --wait`를 백그라운드로 띄워 두면 끝날 때 알 수 있다). 긴 로그 · 작업 목록은 받지 않는다.
4. 장면 시험(scene) 실패는 막지 않는다. 요약의 "🟡 조사 필요"(기준에선 통과 · 다른 시드에서도 실패)만 조사한다. 시험을 끄지는 않는다.
5. 묶음 끝: Actions 요약 정리 → 조사 필요만 조사 → `tools/tests/baseline.txt` 갱신 · `tools/ci/last.sh --times | python3 tools/ci/retime.py`(시험별 초) → README · docs 갱신 → 보고 (중간엔 한 줄, 끝에 자세히).
6. 확인한 단계마다 커밋 · 푸시. 새 시험을 만들면 `tools/tests/tests.txt`에 한 줄 더한다.
