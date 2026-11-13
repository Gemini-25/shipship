# 화풍 시안 그리개 (게임이 아니다 — 견본용)

배 도면(`engine/data/ships/didim.json`)을 읽어 같은 구역을 세 화풍으로 브라우저 캔버스에 그린다 → `docs/screens/graphics/style_*.jpg`.
함장이 화풍을 고르면 그 화풍을 게임(그래픽 카드로 그리는 마지막 단계)에 옮긴다. 이 폴더는 그때까지의 견본 도구다.

1. `Lab --shot Didim 11 12.5 11 21 43 26` → `build/shots/didim.png`를 `now.png`로 복사 (지금 그림 · 시안 1의 바탕)
2. `ship.js` 만들기: `const SHIP = {w,h,rows,rooms,doors,furniture}` (didim.json에서)
3. `mock.html`(캔버스 s1 · s2 · s3 + ship.js + mock.js) · 글꼴은 `game/assets/fonts/Pretendard-*.otf`
4. `node shoot.js` (Playwright) → s1~s3.jpg · `node sheet.js` → 비교표
