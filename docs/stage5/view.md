# 5단 갈래 설계 — 그림·소리

(설계안 → 회의적 검토 · 구현 차례는 docs/STAGE5.md)

## 설계

{
 "branch": "claude/engine-rewrite (지금 작업 트리는 ccr-b5fdcd8e-yv2cp8 — origin/ccr-b5fdcd8e-yv2cp8을 따른다 · engine/Lab/Rules.cs · engine/ShipView/Live.cs에 커밋하지 않은 남의 수정이 있다)",
 "exists": [
  "engine/ShipView/PainterWash.cs WashKind · WashRise · WashBody(kind): 변기 · 세면대(거울) · 샤워 칸 그림. 한 장으로 고정이다 — 커튼은 늘 반쯤 쳐 있고, 상태(사용 중 · 물 · 때 · 불)를 읽지 않는다",
  "engine/ShipView/Painter.cs 629~634: 화장실 설비를 Cached(Key(5, 9100+wk, …))로 상태 없이 찍는다 (설비 고리 안)",
  "engine/ShipView/Painter.cs 칸 덮개 고리 676~690: 바닥 물(sWater 2몰 넘게 → DotFx.WaterTile · 짙기 = 물/60) · 그을음 SootStain · FloorLiquids (암모니아 · 전해액). 지린 오줌 · 넘친 변기의 오줌은 이미 여기로 그려진다 — 다만 설비 칸에서는 변기 그림 위를 통째로 덮는다",
  "engine/ShipView/PainterTraces.cs WallTraces · FloorLiquids: 녹 · 실금 · 쏟은 액체를 세계 값으로 그린다 — 곰팡이는 없다",
  "engine/ShipView/PainterPeople.cs HurtOf · FaceOf · MotionOf · PersonImg(캐시 열쇠 0~49비트) · Portrait(열쇠): 피 · 화상 · 땀 · 아픔은 그린다 — Body.Dirt · Body.Wet(지림)는 읽지 않는다. 사람 화가 game/src/Art/DotPeople.cs는 옛 게임과 함께 쓰는 파일이다 (Dot은 RampAt · PartAt · Mark · Tone이 열려 있다)",
  "engine/ShipView/Painter.cs 693~711 사람 · 로봇 고리 (y 차례 · _front 앞 가림 PutClip) · 떠 있는 물방울 층 (사람 위)",
  "engine/ShipEngine/Life/Life.cs Mold[칸] · MoldStep (상대 습도 = F.Partial(gH2O)/Water.SatKPa(T) 0.8↑ · 278~310K) · LifeCare 상처 감염 (곰팡이 칸은 세 배) · 폐 손상 · ShipMindChores 청소 일감 · MindAct 547 닦으면 Mold=0 — 그림은 없다",
  "engine/ShipEngine/Life/LifeWash.cs: Body.Dirt · Body.Wet · Grime[] · Clogged[] · Bowl[](걸린 대변) · Tank[] · Flushes[] · Flush(흡입 팬 Working이면 내려감) · Unclog · Scrub · Rinse · Wash(sim, body, water, K, cell) · Wipe",
  "engine/ShipEngine/Network/NetworkShower.cs: ShowerValve · ShowerMeter · SetValve · ValveOpen · ShowerStep (6분에 8L · 온수기가 돌면 1%가 김 gH2O 313K로 칸에 · 열 15%) · NetworkLines.WaterAt(설비) · WaterAtDevice",
  "engine/ShipEngine/Mind/MindWash.cs: WashSign[](주컴퓨터가 켜는 '사용 금지' 불 — 그림 없음) · ShowerStart(밸브 + 온수기 켬) · ShowerOff · WithdrawShower(틀어 둔 채 나감) · WashDone (Toilet → Life.Flush · Hands → Life.Wash(…, 300, -1) — 칸 없이)",
  "engine/ShipEngine/Field/Sound.cs: SoundKind 23종 (Chime까지) · Emit(사건 — 다익스트라로 퍼짐 · 보기용 기록 LogSince) · SetDrone/DroneDb(이어지는 소리 — 바탕 소음 · 가림 Mask)",
  "engine/ShipEngine/Network/Network.cs 341 NoiseDb(표 db + 낡은 몫 · Working일 때) · 1100~1118 Noise() → SetDrone. data/devices.json: Toilet db 40 · on true (가만히 있어도 늘 1m 40dB로 웅웅) · Shower db 50 (온수기가 켜졌을 때만 — 찬물 · 틀어 둔 찬물은 소리가 없다)",
  "engine/ShipEngine/Life/LifeNoise.cs BodyNoise (몸이 내는 소리 — 코골이 · 기침은 월드가 Emit) · Mind/MindNoise.cs WorkNoise (판단이 정한 행동 → ③에 요청 — 식기 · 공구) · MindAct.cs 239~245 Use 걸음의 생활 소리 칸 · MindSense.Hear (Hiss는 '샌다'로 배운다 — 새 종류는 배우지 않고 돌아보기만)",
  "engine/ShipView/Audio/Acoustics.cs: MakeShot(사건 → Voice) · CaptionOf(자막) · Machines(장치 → LoopKind.Machine 'appliance' 웅웅 — 샤워 · 변기도 여기서) · Hear · MaxShots 8 · MaxMachines 6. Mixer.cs Make(루프 종류 → 목소리) · 갱신 switch. Voices*.cs 목소리들 (크기 기준 RMS 0.25)",
  "engine/Lab: RulesWash.cs WashShip(침실 — 문 — 변기 둘 · 샤워 칸 · 세면대 · 배전반 · 전지) · RulesTraces.cs(Painter.Frame 한 장 점 세기 틀) · RulesPeopleArt.cs · RulesMusic.cs(LiveShow · Ears.ForceEar · Remap · Advance로 Loops를 읽는 틀) · SceneWash.cs(--scene wash) · Listen.cs(--listen life/world · AllVoices · LoopVoices · --voices)"
 ],
 "items": [
  {
   "name": "샤워 칸 — 쓰는 동안 커튼이 닫히고 · 물이 흐르면 물줄기 · 젖은 받침",
   "layer": "화면",
   "what": "샤워 칸 그림을 세계 상태 셋으로 나눈다. ① 사람이 그 샤워 칸 칸에 서 있다 (몸의 자리 — 세계 값): 커튼이 끝까지 닫힌다. 커튼 막대(그림 17줄) 위로 머리만 보인다 ② 물이 흐른다 (밸브 열림 · 물관 · 물탱크 > 0 = 새 읽기 함수 ShowerRunning): 샤워기에서 받침까지 옅은 하늘색 물줄기 (4장으로 움직인다) · 받침이 젖어 빛난다 · 사람이 없는데 흐르면 (틀어 둔 채 나간 사람) 커튼은 열린 채 물줄기가 다 보인다 ③ 아무도 없고 잠겼다: 커튼을 왼쪽 4칸에 접는다. 커튼은 세계에 따로 두지 않는다 — 몸의 자리를 그대로 그릴 뿐 새 값을 만들지 않는다. 줄 선 이는 곁 칸이라 커튼이 열려 있다 · 로봇은 닫지 않는다",
   "where": "PainterWash.cs WashBody(3, 상태) — 열린 커튼 / 물줄기 장 / 새 ShowerCurtain() (닫힌 커튼 앞 그림만) · Painter.cs 629~634 열쇠를 Key(5, 9100 + wk*16 + 상태, …)로 · 사람 고리 693~711: 샤워 칸 칸에 선 사람을 그린 바로 뒤에 커튼 앞 그림을 얹는다 (뒤 줄 사람은 나중에 그려 앞에 선다) · NetworkShower.cs에 public bool ShowerRunning(int d) => ValveOpen(d) && WaterAtDevice(d) && WaterTank > 0 (읽기만 · 새 상태 없음)",
   "test": "규칙 '화장실 그림' (새 RulesWashView.cs · WashShip · Painter.Frame · 빛 끔 · 약 1초): 빈 칸 · 잠김 → 샤워 칸 상자의 청록(커튼) 점 N0 · 물빛 점 0 / 사람을 샤워 칸 칸에 두고 묶음(Pin) → 청록 점 ≥ N0+250 · 막대 아래 몸통 자리에 옷 색 점 0 · 막대 위 머리 점 > 0 / 사람을 빼고 밸브만 열어 2틱 → 물줄기 점 > 40 · 청록 점 N0 / 잠그면 물줄기 0. 고치기 전 코드는 그림이 한 장(반쯤 친 커튼 고정)이라 청록 점이 그대로이고 물줄기는 0이다 → 실패",
   "fingerprint": "같음",
   "cost": "보이는 샤워 칸(≤12)마다 열쇠 셈과 캐시된 그림 (상태 셋 + 물줄기 4장) — 장당 수 µs · 저장 판 그대로"
  },
  {
   "name": "김 · 습기 — 뜨거운 물이 흐르면 김이 오르고 · 습한 방은 안개 · 거울이 뿌옇게",
   "layer": "화면",
   "what": "칸의 상대 습도 = F.Partial(gH2O, c) / Water.SatKPa(F.Temp(c)) (Life.MoldStep과 같은 식 — 세계 값). ① 뜨거운 물이 흐른다 (ShowerRunning && net.Working(d) — 온수기): 샤워기 위로 흰 김 뭉치 3~6개가 장마다 오른다. 짙기는 0.25 + 0.5 × clamp((습도 − 0.5)/0.45). 찬물이면 김 없음 (물줄기는 있다) ② 습도가 0.9를 넘는 바닥 칸: 옅은 흰 안개 (짙기 0.25 × (습도 − 0.9)/0.1 · 사람 위에 덮는다) ③ 세면대 칸 습도가 0.85를 넘으면 거울이 뿌옇다 (거울 덮개 짙기 0.6 × (습도 − 0.85)/0.15). 샤워가 끝나도 김은 세계 습도가 빠지는 만큼 오래 남는다 (제습기가 꺼진 화장실은 늘 뿌옇다 — 곰팡이 씨앗이 보인다)",
   "where": "PainterWash.cs에 새 Steam(img, f, net, x0, y0, w, h, frame) — Painter.cs '떠 있는 물방울' 층 바로 뒤 (사람 위) · 세면대 WashBody(2, 상태)에 '김 서림' 열쇠 하나",
   "test": "규칙 '화장실 그림': WashShip · 사람 없이 밸브와 온수기를 켜고 (net.SetValve · SetOn) 5틱 → 샤워기 위 3칸 줄의 흰 점(밝기 > 230) > 20 / 온수기를 끈 찬물 → 흰 점 0 · 물줄기는 > 0 / 화장실 칸마다 gH2O를 습도 0.97로 채움 → 바닥 칸 밝기 평균 +8 넘게 · 거울 칸 밝은 점이 늘어난다 / 습도 0.4 → 그대로. 고치기 전 코드는 김 · 안개를 그리지 않는다 → 실패",
   "fingerprint": "같음",
   "cost": "보이는 칸마다 습도 셈 (exp 하나 — 넓게 볼 때 약 3,000칸 · 0.05ms 남짓) · 김 뭉치는 샤워 칸마다 몇 개. `Lab --live Saetteul 18 20` '확대마다 한 장'으로 잰다 — 6단 16ms를 넘으면 방마다 한 번 셈으로 바꾼다"
  },
  {
   "name": "곰팡이 얼룩",
   "layer": "화면",
   "what": "Life.Mold[c]가 0.05 이상인 칸에 검푸른 점이 무리 지어 핀다. 점 수는 4 + 60 × 곰팡이, 빛깔은 #1f2a1a · #2e3b22 · #3f4a2a 셋을 칸마다 해시로 고른다 (DotHash — 늘 같은 자리), 짙기는 0.35 + 0.5 × 곰팡이. 가구가 없는 바닥 칸에는 칸 전체에 피고, 화장실 설비 칸(샤워 받침 · 변기 · 세면대)에는 줄눈과 가장자리 4도트에만 핀다. 다른 가구 칸은 건너뛴다. 로봇 · 사람이 닦으면 (MindAct 547의 Mold = 0) 다음 장에 사라지고, 마르면 세계 값이 줄어드는 만큼 옅어진다",
   "where": "PainterTraces.cs 새 MoldStain(img, px, py, mold, c, edgeOnly) · Painter.cs 칸 덮개 고리 679 (그을음 곁) · life는 이미 그 자리에 있다 (life.Mold 길이를 확인한다)",
   "test": "규칙 '재질 흔적' 묶음에 하나 더 (RulesTraces.cs · 디딤호 식당 · 약 1초): 가구 없는 바닥 칸에 Mold 0 → 검푸른 점(G > R+8 · 밝기 < 90) 0 / Mold 0.5 → 30 넘게 / 다시 0 → 0. 고치기 전 코드는 Mold를 읽는 그림이 없어 0이다 → 실패",
   "fingerprint": "같음",
   "cost": "보이는 바닥 칸마다 배열 한 번 읽기 · 곰팡이 핀 칸만 점을 찍는다"
  },
  {
   "name": "변기 상태 — 때 · 막힘 · '사용 금지' 불",
   "layer": "화면",
   "what": "변기 그림이 세계 값 셋을 읽는다. ① 때 (Life.Grime[fu]): 0.3 이상이면 대야 테 안쪽에 갈색 고리 (#8a6a3a · 짙기 0.3 + 0.6 × 때 · 3단) ② 막힘 (Clogged[fu] 또는 Bowl[fu] > 0): 대야 물이 탁한 갈색으로 차오른다 ③ '사용 금지' 불 (Mind.WashSign[fu] — 주컴퓨터가 조절기 보고로 켠 불): 뒤쪽 흡입 장치에 빨간 불 2×2 (깜빡이지 않는다 · 빛을 낸다 — Lighting이면 _emissive). 닦기(Scrub) · 뚫기(Unclog) · 불 끄기를 하면 다음 장에 돌아간다. 화면은 신의 눈이라 주컴퓨터가 모르는 때도 보인다 (주컴퓨터는 쓴 수로만 안다)",
   "where": "PainterWash.cs WashBody(1, 상태) 변기 갈래 · Painter.cs 629 열쇠 (때 3단 × 막힘 × 불 = 12장 캐시) · life.Grime · Clogged · Bowl · mind.WashSign 길이를 확인한다 (화장실 없는 배는 빈 배열)",
   "test": "규칙 '화장실 그림': WashShip 변기 하나 · 깨끗 → 갈색 0 · 빨강 0 / Grime 0.9 → 대야 둘레 갈색 점 > 15 / life.Clog + Bowl 0.3 → 대야 안 갈색 > 25 / m.SignToilet(fu, true) → 빨강 점(R > 200 · G < 90) ≥ 4 / Scrub · Unclog · SignToilet(false) → 다시 0 · 0. 고치기 전 코드는 변기 그림이 한 장이라 값이 바뀌어도 점이 같다 → 실패",
   "fingerprint": "같음",
   "cost": "변기마다 열쇠 셈 하나 · 캐시 12장 · 저장 판 그대로"
  },
  {
   "name": "설비 칸 바닥 물 — 넘친 물은 변기 발치에",
   "layer": "화면",
   "what": "바닥 물(sWater 2몰 넘게)은 이미 그려진다 (지린 오줌 · 넘친 오줌). 그런데 화장실 설비 칸에서는 DotFx.WaterTile이 변기 · 세면대 · 샤워 칸 그림 위를 통째로 덮어 설비가 물에 잠긴 듯 보였다. 설비 칸이면 칸 아래쪽 반(발치 · 그림의 앞)에 타원 웅덩이로 그린다: 짙기 min(0.85, 물/60), 크기는 물의 양만큼 (가장자리를 밝게 — FloorLiquids와 같은 꼴). 샤워 칸에 흐르는 물은 받침이 젖는 것으로 따로 그린다 (1번)",
   "where": "Painter.cs 681~682 칸 덮개 — _washKind[L.FurnitureAt[c]] != 0인 칸이면 WaterTile 대신 발치 웅덩이",
   "test": "규칙 '화장실 그림': WashShip 변기 칸에 물 30몰 → 변기 그림 가운데 대야 6×6 점이 물 없을 때와 같다 · 칸 아래 8줄의 물빛 점 > 30. 고치기 전 코드는 대야 점이 물빛 덮개로 바뀐다 → 실패",
   "fingerprint": "같음",
   "cost": "0 (그리는 몫이 칸 하나에서 반 칸으로 준다)"
  },
  {
   "name": "얼룩진 옷 · 지린 옷",
   "layer": "화면",
   "what": "Body.Dirt가 0.35 · 0.6 · 0.85를 넘으면 (씻으러 가는 문턱이 0.35다) 옷 색표 칸(l.Cloth · l.Accent · ClothNavy)에 갈색 회색 점(새 색표 셋 #2a1d12 · #4a3420 · #6b4e30)을 6 · 14 · 24개 해시 자리에 찍는다. Body.Wet가 1이면 (참지 못하고 지렸다) 다리 부위(Dot 부위 2 · 3)의 윗 5줄 옷 칸을 한 단 어둡게 하고 얼룩 한 점을 더한다. 우주복이면 그리지 않는다 (옷을 덮는다). 씻으면 (Rinse 200몰 넘게 · Wipe) 다음 장에 사라진다. 옛 게임과 함께 쓰는 DotPeople.cs는 건드리지 않고, ShipView 안에서 DotPeople.Draw가 돌려준 Dot에 Mark · Tone으로 덧칠한다 (누운 · 기는 그림은 돌리기 전에 칠하므로 맞는다)",
   "where": "PainterPeople.cs PersonImg — Draw 뒤 · Rotate 앞에 Stain(dot, l, grime, wet) · 캐시 열쇠 50~51비트(더러움 단계) · 52비트(지림) · Painter.cs 997 DrawPerson이 b.Dirt · b.Wet로 단계를 셈해 넘긴다 · Portrait 열쇠 43~45비트 (얼굴 줄이 다시 찍히게)",
   "test": "규칙 '사람 그림' 묶음에 하나 더 (RulesPeopleArt.cs · 작은 배 · 사람 하나 묶음 · 같은 장 번호 · 약 1초): Dirt 0 → 갈색 점 n0 / Dirt 0.8 → n0 + 10 넘게 / Wet 1 → 다리 윗부분 어두운 점 > 6 / 우주복 → n0 / life.Rinse(body, Life.ShowerMol) → n0 · 다리가 원래대로. 고치기 전 코드는 Dirt · Wet를 읽는 그림이 없다 → 실패",
   "fingerprint": "같음",
   "cost": "그릴 때 한 번 (캐시) · 단계가 넷이라 캐시(상한 6,000장)가 조금 더 찬다"
  },
  {
   "name": "세면대 물줄기 — 손 씻는 동안",
   "layer": "화면",
   "what": "세면대 칸에서 손 씻는 걸음(UseKind.Hands) 중인 사람이 있고, 그 세면대에 물이 오면 (net.WaterAt(세면대) && WaterTank > 0) 수도꼭지(그림 16, 15)에서 대야로 1도트 물줄기가 흐르고 대야에 물결이 인다 (4장). 사람이 하는 일을 그리는 것은 MotionOf(먹기 · 공구)와 같은 결이다. 물관이 끊긴 세면대는 손을 대도 물이 없다",
   "where": "Painter.cs 629 세면대 갈래 (열쇠 + 장) · 그 장의 '세면대를 쓰는 이' 표를 사람 고리 앞에서 한 번 만든다 (사람 수만큼 · 설비 칸 표)",
   "test": "규칙 '화장실 그림': WashShip · 성실 1인 사람 방광 0.7 (손 씻기 확률 0.95) → 변기 → 세면대 Hands 걸음에 든 틱에 찍는다 (10분 안 · 약 2초) → 수도꼭지 아래 물빛 점 > 6 / 세면대 칸의 물관을 끊고 (net.CutLines 물) 같은 차림 → 0. 고치기 전 코드는 세면대 그림이 한 장이다 → 0 → 실패",
   "fingerprint": "같음",
   "cost": "장마다 사람 고리 한 번 (이미 도는 고리에 얹는다) · 캐시 4장"
  },
  {
   "name": "진공 변기 '쉬익' — 물 내릴 때만 · 가만히 있는 변기는 조용하다",
   "layer": "소리(화면)",
   "what": "(월드) Life.Flush가 흡입 팬이 돌 때 (장치 Working — 전기 · 기계 부품) 그 칸에서 1m 72dB SoundKind.Flush (새 종류 · enum 끝에 붙인다)를 낸다. 막혀 넘쳐도 팬이 돌면 쉬익 한 번은 난다. 팬이 서 있으면 (고장 · 전기 없음) 소리가 없다 — 내려가지 않은 것을 귀로도 안다. 가만히 있는 변기는 소리가 없다: data/devices.json Toilet db 40 → 0 (지금은 가만히 있어도 늘 1m 40dB로 웅웅대 화장실 바탕 소음을 올리고, 화면은 변기마다 기계 웅웅 루프를 MaxMachines 6 자리에 틀었다). 마음은 Flush를 '샌다'로 배우지 않는다 (MindSense.Hear에 갈래가 없다) — 돌아보기만 한다. (화면) Acoustics.MakeShot → 새 Whoosh 목소리 1.8초: 밸브 '탁' 0.05초 → 흡입 1.4초 (흰 잡음이 400 → 1,800Hz 띠로 오르며 빨리 올라 천천히 준다) → 끝에 120Hz '꿀렁' 0.15초 · 자막 '쉬익 — 변기 물 내림'. 크기 72dB는 기능 확인용 기본값이다 (조절은 맨 끝)",
   "where": "LifeWash.cs Flush — ToiletDev · Working을 맨 앞에서 보고 (막힘 갈래보다 먼저) sim.Find<Sound>()?.Emit(sim, cell, 72, SoundKind.Flush, 0) · Field/Sound.cs SoundKind · data/devices.json Toilet db · ShipView/Audio/Acoustics.cs MakeShot · CaptionOf · Audio/VoicesLife.cs 새 Whoosh · Lab/Listen.cs AllVoices에 더한다 (RMS 0.25 기준)",
   "test": "규칙 '화장실 소리' (새 · RulesWashView.cs · WashShip · 약 2초): (월드) 사람 방광 0.7 → 20분 안에 비우면 소리 기록(LogSince)에 Flush 하나 · 변기 칸 · 72dB / 흡입 팬 기계 부품을 망가뜨리고 (net.Broken[d*4+기계] = 1) life.Flush를 바로 부르면 Flush 0 / 아무도 쓰지 않는 동안 snd.DroneDb(변기 장치) == 0 · 화장실 칸 Ambient ≤ 31dB. (화면) LiveShow · Ears.ForceEar = 화장실 칸 · Remap → 비운 장의 Shots에 Whoosh 하나 · 자막 '쉬익'. 고치기 전 코드에는 Flush 종류가 없고 · 변기 drone이 40dB라서 실패한다",
   "fingerprint": "바뀜",
   "cost": "비울 때마다 Emit 한 번 (다익스트라 한 번 — 새뜰호 하루 약 240번이고 하루는 36,000틱이라 작다) · 바탕 소음 셈은 그대로 · 저장 판 그대로 (enum 끝에 붙임 · 새 상태 없음)"
  },
  {
   "name": "샤워 물소리 — 물이 흐르면 쏴아 (온수기와 상관없이)",
   "layer": "소리(화면)",
   "what": "(월드) 샤워 칸의 이어지는 소리 = 물이 흐를 때 (ShowerRunning) 1m 60dB. 온수기는 소리가 없다. 지금은 NoiseDb가 표 db 50을 온수기가 켜졌을 때만 내서, 찬물 샤워나 틀어 둔 찬물은 소리가 없었고 · 물이 없는데 온수기만 켜져도 웅웅했다. 틀어 둔 채 나간 샤워 칸은 빈 방에서 쏴아 소리를 낸다 (귀로 알 수 있다). (화면) Acoustics.Machines에서 샤워 장치는 Machine 대신 새 LoopKind.Water (열쇠 5,000,000 + 장치)로 보낸다 → 새 Spray 목소리: 흰 잡음 1~9kHz 띠 + 받침을 때리는 투둑 (초당 80번 · 2~6kHz 짧은 울림) + 배수구 꾸르륵 (250Hz 좁은 띠 · 0.3Hz 너울). B = 그 칸에 사람이 서 있나 (몸이 물을 받아 높은 음과 투둑이 줄어든다 — 거름 5kHz) · 자막 '쏴아 — 샤워 물' (이어 도는)",
   "where": "Network.cs 341 NoiseDb — dev.Type == \"Shower\"이면 ShowerRunning(d) ? 60 : 0 · NetworkShower.cs ShowerRunning (1번과 같은 함수) · Acoustics.cs Machines · LoopKind에 Water · Mixer.cs Make · 갱신 switch (Spray.Set(A, B)) · 안 보이면 Stop · VoicesLife.cs 새 Spray · Listen.cs LoopVoices",
   "test": "규칙 '화장실 소리': WashShip 사람 없음 · 밸브만 열고 온수기 끔 (찬물) 3틱 → snd.DroneDb(샤워 장치) ≈ 60 / 밸브 닫고 온수기만 켬 → 0 / 밸브는 열고 샤워 칸 물관을 끊음 → 0. (화면) LiveShow 귀를 화장실에 → 열렸으면 Loops에 Water 하나 (Machine 'appliance'는 없다) · 닫혔으면 없다. 고치기 전 코드는 찬물 흐름이 0dB이고, 온수기만 켠 빈 칸은 50dB Machine 루프다 → 실패",
   "fingerprint": "바뀜",
   "cost": "장치 고리 안에서 셈 하나 (이미 1분마다 SetDrone) · 화면 루프는 보이는 샤워 칸만 · 저장 판 그대로"
  },
  {
   "name": "손 씻는 소리",
   "layer": "소리(화면)",
   "what": "(월드) 세면대에서 물이 실제로 나왔을 때 (손 씻기 물 > 0) 세면대 칸에서 1m 55dB SoundKind.Faucet (새 · 끝에 붙인다). 새 Life.HandWash(sim, body, water, cell) = Wash(…, 300, cell) + Emit. MindWash.WashDone의 Hands 갈래는 지금 Wash(…, -1)를 칸 없이 부른다 → HandWash(…, st.Cell)를 부르게 바꾼다 (판단은 요청만 · 소리는 물이 흐른 월드가 낸다). 300K 물이라 김 · 열은 지금처럼 0이다. 물관이 끊긴 세면대는 지금처럼 WashDone에서 빠져나가므로 소리가 없다. (화면) MakeShot → 새 Faucet 목소리 1.5초: 물줄기 흰 잡음 2.5kHz 띠 + 물방울 튐 (초당 30번 · 1.5~4kHz) + 손이 물줄기를 가르는 4Hz 흔들림 · 앞뒤 0.1초 오르내림 · 자막 '쏴 — 손 씻는 물'",
   "where": "LifeWash.cs 새 HandWash · MindWash.cs 315~318 Hands 갈래 (부르는 함수와 칸만) · Field/Sound.cs SoundKind · Acoustics.cs MakeShot · CaptionOf · VoicesLife.cs 새 Faucet · Listen.cs AllVoices",
   "test": "규칙 '화장실 소리': WashShip · 성실 1인 사람 방광 0.7 → 손 씻기 한 번 (HandWashes 1)이면 소리 기록에 Faucet 하나 · 세면대 칸 · 55dB / 세면대 물관을 끊은 같은 차림 → HandWashes 0 · Faucet 0. (화면) LiveShow 귀를 화장실에 → 그 장 Shots에 Faucet. 고치기 전 코드에는 Faucet 종류가 없다 → 실패",
   "fingerprint": "바뀜",
   "cost": "손 씻기마다 Emit 한 번 (새뜰호 하루 약 200번) · 저장 판 그대로"
  },
  {
   "name": "관찰 장면 '화장실 그림 · 소리' + 들어 보기",
   "layer": "화면",
   "what": "`Lab --scene washview`: 새뜰호(40명 · 로봇 10) 아침 06:00~09:00. 샤워 칸 커튼이 닫힌 장 · 김이 오르는 장 · 손 씻는 물줄기 · 더러워진 이 · 지린 이의 옷 · 넘친 변기 발치의 물 · '사용 금지' 불을 찍는다. 차림 (원인 마디 Setup): 한 화장실 제습기를 끄고 '지난주 제습기가 멈췄었다'로 바닥 두 칸 곰팡이 0.4 · 변기 하나 막힘. `Lab --listen wash`: 귀를 화장실 문 곁에 두고 build/sound/wash.wav로 쉬익 · 쏴아 · 손 씻는 소리를 담는다. 새 기능이 실제로 나오고 작동하는지만 본다 (재미 · 빈도는 재지 않는다). 그림 전후 비교는 docs/screens/wash/에 둔다",
   "where": "Lab/SceneWash.cs 새 WashViewScene · Scenes.cs 이름표 · Lab/Listen.cs 새 ListenWash ('wash' 갈래) · docs/WASH.md '(마) 된 것' 절 · README 진행률",
   "test": "관찰 장면이라 규칙 시험이 아니다. 장면 끝 줄 '커튼 닫힌 장 n · 김 장 n · 곰팡이 칸 n · 얼룩 옷 n · 쉬익 n · 쏴아 n · 손 씻는 소리 n'이 모두 0이 아닌지 · 그림 · 소리가 세계 값과 맞는지 눈과 귀로 본다",
   "fingerprint": "같음",
   "cost": "실행 몇십 초 (CI 밖)"
  }
 ],
 "risks": [
  "하루 3분 연출이면 게임 1초가 화면 약 1/480초다. 6분 샤워는 화면 0.75초, 30초 손 씻기는 0.06초라서 닫힌 커튼 · 물줄기 · 샤워 물소리 루프가 깜빡이듯 짧다. 세계 그대로이므로 붙들지 않는다 (입말 풍선처럼 붙들면 사람이 나간 뒤에도 커튼이 닫혀 있는 가짜가 된다). 김(습도)만 오래 남는다. 쉬익과 손 씻는 소리는 한 번 울리는 소리라 제 길이(1.5~1.8초)로 들린다",
  "아침에 쉬익이 한 장에 여럿 몰리면 MaxShots 8 안에서 큰 것만 남는다. 함장이 빠르게 감을 때 쉬익이 겹쳐 시끄러울 수 있다 — 크기 · 잦기는 맨 끝 조절에서 본다",
  "변기의 늘 40dB를 걷으면 화장실 바탕 소음이 10dB쯤 낮아진다. 가림(Mask)이 줄어 화장실에서 다른 소리가 더 잘 들리고, 바탕 소음 · 깨기 · 돌아보기 시험의 값이 바뀔 수 있다 (지문 바뀜 — 빨강이 나면 시험 기대값이 아니라 까닭을 먼저 본다)",
  "침실 문이 열려 있으면 화장실 곁 침실에 밤 쉬익(1m 72dB → 4m에 약 60dB)이 닿아 얕은 잠(문턱 50dB)이 깰 수 있다. 실제와 같은 일이지만 잠 · 기분 셈이 바뀐다. 72 · 60 · 55dB는 기능 확인용 기본값이다",
  "샤워 drone이 온수기가 아니라 물 흐름을 따르므로, 전력을 줄여 온수기를 끈 찬물 샤워에도 이제 소리가 난다 (지금까지는 조용했다) — 의도한 바뀜이다",
  "SoundKind에 Flush · Faucet 둘을 끝에 붙인다. Heard는 바이트 그대로 찍히므로 앞 값은 그대로이고 저장 판도 그대로이지만, 옛 저장을 읽는 왕복 시험을 한 번 돌려 확인한다. 새 종류는 MindSense.Hear에 갈래가 없어 배우지 않고 Glance로 돌아보기만 한다 — 화장실 줄에 선 이들이 쉬익마다 2틱 고개를 돌린다",
  "얼룩 옷은 옷 색표로 칸을 고른다. 같은 색표를 쓰는 목도리 · 조끼 무늬(Pattern)도 얼룩질 수 있고, 지린 자국은 Dot 부위 2 · 3(다리)의 윗줄로 어림한다. DotPeople.cs(옛 게임과 함께 쓰는 파일)는 건드리지 않는다",
  "사람 그림 캐시 열쇠에 3비트(50~52)를 더해 같은 사람의 그림 장이 최대 8배까지 늘 수 있다 (실제로는 더러움 단계가 천천히 바뀐다). 6,000장 상한에서 지우기가 잦아지는지 `--live`로 본다",
  "보이는 칸마다 습도 셈(exp 하나)을 더하면 넓게 볼 때 6단의 16ms 예산(지금 24ms를 줄이는 중)에 얹힌다. 넘으면 방마다 한 번 셈 · 몇 장에 한 번 셈으로 바꾼다 — 지문과는 상관없다",
  "곰팡이는 저절로 느리게 자란다 (0.01 → 0.05가 습한 날 2.3일). 그래서 짧은 관찰 장면은 차림(Setup 원인)으로 보인다. 빈도와 크기 조절은 엔진 개편 끝에 한다 (자연 사건 결정)",
  "세면대 물은 세계가 손 씻기가 끝날 때 한 번에 꺼낸다 (`_net.WaterTank -=` — 8단 요청 길에서 고칠 곳). 그래서 물줄기 그림은 세계의 흐름이 아니라 사람의 걸음(손 씻는 중)과 세면대 물관을 따른다. 8단에서 세면대 밸브(샤워처럼)를 두면 그림도 밸브를 읽게 바꾼다",
  "커튼은 세계에 따로 두지 않는다 — '사람이 샤워 칸 칸에 있다'를 그린다. 나중에 사생활 · 엿보기 같은 판단(라)이 커튼을 쓰려면 그때 월드 값으로 올려야 한다",
  "그림은 Mind.WashSign · Life 배열을 읽기만 해야 한다 (Frame이 세계를 쓰면 지문이 갈린다). 화장실이 없는 배에서는 Grime · WashSign이 빈 배열이다 — 길이 확인을 빠뜨리면 그림이 터진다",
  "작업 트리에 커밋하지 않은 남의 수정(engine/Lab/Rules.cs · engine/ShipView/Live.cs)이 있다. 새 시험 묶음을 Rules.cs 목록에 더할 때 겹치지 않게 한다",
  "커밋을 둘로 나눈다: 그림 (1~7 · 지문 같음 · `Lab --fingerprint`로 확인) → 소리 월드 + 화면 소리 (8~10 · 지문 바뀜). 그래야 속도 · 그림 바뀜과 세계 바뀜이 섞이지 않는다"
 ]
}

## 검토

**'그림·소리' 설계안 검토 결과 — 코드를 직접 열어 맞춰 본 고친 설계**

가장 큰 문제는 셋이다.
- 설계는 32도트 그림(`Painter.Draw`)만 고친다. 넓은 보기(16·8도트, `PainterFar.cs`)는 설비 몸을 덩어리에 구워 두고, 다시 굽는 기준(`StampOf`)은 `FillOf`와 `FixOff`만 본다. 상태 그림을 캐시 열쇠에만 넣으면 넓은 보기에서는 옛 그림에 멈추고, 4장 움직임도 frame 0으로 굳는다.
- 변기 소리 40→0dB은 낡은 팬의 끽끽, 볼트 풀린 덜컹, 기계 루프를 함께 끈다. 이번 단에서 뺀다.
- 작업 트리의 남의 미커밋 수정은 Rules.cs·Live.cs 둘이 아니라 24파일이고, (마)가 고칠 파일과 겹친다.

## 찾은 것

**(1) 이미 있는 것을 다시 만드는 것**
- 상대 습도 식 `F.Partial(gH2O)/Max(1e-6, SatKPa(Max(250,T)))`는 이미 7곳에 같은 꼴로 있다 (`Life.cs` 686·702, `Network.cs` 799·1081, `NetworkWear.cs` 166, `NetworkPipes.cs` 44, Structure 둘).
  - 그림이 여덟째 사본을 만들지 않는다. `Field.RelHumidity(c)` 읽기 함수를 하나 두고 그림만 그것을 쓴다.
  - 월드 쪽 사본은 이번에 바꾸지 않는다 (지문 때문).
- 김 뭉치는 `DotFx.Puff`와 `Painter.Hiss`(압력 맞춤 밸브의 흰 뭉치)가 이미 있다. 새 스프라이트를 만들지 않는다.
- 웅덩이는 `FloorLiquids`의 타원과 빛 받은 자리를 함수로 빼서 같이 쓴다.
- 곰팡이 점은 `WallTraces`의 녹 얼룩(2×2, `DotHash.U`) 꼴을 그대로 쓴다.

**(2) 판단이 참 세계를 읽는 곳**
설계 자체는 화면(신의 눈)이라 괜찮다. 다만 건드리는 코드 곁에 이미 셋이 있다.
- `MindWash.cs` 98 `ToiletOk`: 사람이 배 안 모든 변기의 `net.Working`(흡입 팬·전기)을 어디서든 안다.
  - 주석은 "고장 등이 켜져 있으면 안다"라고 적었지만, 전기가 끊긴 변기는 등이 켜질 수 없다. 먼 화장실 것도 안다.
- `ShipMindChores.cs` 116·128: 주컴퓨터가 카메라(`RoomSeen`) 없이 모든 방의 `Life.Mold`를 읽어 청소 일감을 낸다.
- `MindAct.cs` 547: 판단이 `Mold = 0`과 바닥 물을 직접 쓴다 (8단 '요청 길'에 이미 있는 꼴).
- 셋 모두 판단을 바꾸고 지문을 바꾸므로 (마)에서는 고치지 않는다. NEXT.md 8단 목록에 적는다.

**(3) 이야기꾼처럼 마음을 직접 건드리는 것:** 없다.
- 쉬익·쏴아는 `MindSense.Hear`에 갈래가 없어 배우지 않고 돌아보기(Glance)만 한다. 확인했다.
- 잠을 깨우는 것은 귀 문턱 법칙이다.

**(4) 결정론·저장·복제**
- `SoundKind`를 끝에 붙여도 저장 판은 그대로다.
  - `Sim.ShapeOf`는 흐름·물자·사실·원인의 종류 수만 센다 (SoundKind는 세지 않는다). Heard는 바이트로 찍힌다.
  - `SaveVersion`을 올리지 않는다.
- `Life.Grime`·`Clogged`·`Bowl`은 `WashArrays`가 처음 불릴 때까지 빈 배열이다.
  - 그림은 길이를 확인한다.
  - 시험은 `life.Scrub`나 `life.Clog`를 먼저 불러 배열을 잡는다.
- 복제 세계(계산 일꾼·데우기): `Emit`은 그 세계의 Sound에만 가고, 정적 표를 쓰지 않는다. 문제없다.

**(5) 시험**
- 샤워 이어지는 소리를 '3틱'에 보는 시험은 성립하지 않는다. `Noise()`는 `WearStep` 안에 있어 25틱(1분)마다 돈다.
- 기준 그림에 이미 걸리는 점이 있어 '0'이나 고정 문턱 시험이 성립하지 않는다.
  - 커튼 윗단 #d6f2f1은 평균 밝기 232, 도기 #eef3f6도 230을 넘는다.
  - 곰팡이 #2e3b22를 밝은 타일 바닥에 0.6으로 섞으면 밝기가 약 100이라 '밝기 < 90'을 넘지 못한다.
  - 그래서 모두 '기준 그림과의 차'로 센다.
- 손 씻는 이는 세면대 칸에 서지 않는다. 계획이 `Go(세면대, 1)`이라 곁 칸에 선다. 칸으로 찾으면 늘 0이다.
- 사람 그림의 '부위 2·3 = 다리'는 앞·뒤 그림에서만 맞다. 옆 그림에서는 부위 3이 신발, 4가 먼 다리다.
  - 기본 Accent(ClothRed)는 볼 홍조와 머리끈에도 쓰인다.
- 손 씻기·쉬익을 '방광 0.7 → 20분'으로 돌릴 까닭이 없다.
  - 손 씻기는 계획 한 걸음을 차려 약 13틱이면 된다.
  - 쉬익은 `life.Flush`를 직접 부르면 0틱이다.
- "Machine 'appliance' 루프가 없다"는 틀린다. 변기도 role이 appliance다.
- 미커밋 `RulesFar.cs`의 "넓은 보기 = 32도트 그림을 줄인 것과 칸마다 비슷하다" 시험은 32도트만 고치면 빨강이 날 수 있다.
- 새 enum은 고치기 전 코드에서 시험이 아니라 컴파일로 실패한다. enum만 먼저 더하고 갈래는 비운 채 돌려 빨강을 확인한다.

**(6) 비용**
- 쉬익·손 씻기 `Emit`은 하루 약 440번이다 (36,000틱 중). 작다.
- 그리기 비용의 잣대는 '16ms / 24ms'가 아니라 `Lab --bench`의 그리기 예산이다.
  - 41×24칸 32도트 ≤ 6ms, 97×55칸 16도트 ≤ 8ms, 배 전체 8도트 ≤ 8ms. 지금 3.6~4.8ms다.
  - 습도 셈은 한누리호 8도트 전체 약 2만 칸이면 exp 2만 번, 약 0.3ms다.

**(7) 빠진 것**
- 넓은 보기 짝이 통째로 없다: 덩어리 도장, 장마다 덮개, `FarGlows`(지금 `_washKind`면 `continue`), 사람 판 위의 커튼. 물 덮개도 `FrameFar`에 따로 있다.
- `_washKind[L.FurnitureAt[c]]`: 가구 없는 칸은 `FurnitureAt`이 -1이라 배열 밖을 읽어 터진다.
- 변기 db 40 → 0의 부작용:
  - `Noise()`의 끽끽은 db > 0일 때만 난다. 낡은 팬을 사람이 듣고 "소리가 이상하다"고 알리는 길이 끊긴다.
  - 볼트가 풀려 내는 덜컹(`NetworkVibration.cs` 44)은 `Def.Db + 10`이라 10dB로 준다.
  - 실제 배의 화장실에는 꾸밈으로 제습기 50dB가 있어, 걷어서 얻는 '10dB 낮아짐'도 작다.
- 화면 소리의 이어 도는 소리 열쇠가 겹친다 (있던 결함).
  - 기계 루프는 3,000,000 + 장치 번호, 음악 루프는 3,000,000 + 사람×4 + 듣는 방식이다.
  - 사람 10이 스피커를 틀면 장치 41과 같은 열쇠가 된다. Mixer는 먼저 만든 목소리를 그대로 쓴다.
- 쉬익·손 씻는 소리의 낸 이가 비어 있다 (-1). 그러면 쓰는 이가 제 소리에 고개를 돌린다. 코골이·기침처럼 `Sound.BodyActor(몸)`으로 적는다.
- 샤워 소리 60dB를 코드에 박지 않는다. 표의 db(50)를 쓰고 크기 조절은 맨 끝에 한다.
- 작업 트리 미커밋 24파일이 (마)가 고칠 파일과 겹친다.
  - Painter.cs, `PainterFar.cs`(새 파일 — HEAD에 없다), PainterLight.cs, Network.cs, Sim.cs(SaveVersion 104), Mind.cs, ShipMind.cs, Field.cs, Rules.cs(559줄 `FarRules` 등록), RulesFar.cs, LawsWorker.cs·ShipMindThink.cs(추적 안 됨) 등.
- 변기·세면대·샤워 칸 몸이 앞 가림(`_front`)에 들지 않는다. 다른 솟은 설비는 6도트 넘게 솟으면 든다. 작다.

## 고친 설계

**0. 시작 조건 (새 항목)**
- 6단 미커밋 작업(넓은 보기 화가·계산 일꾼)을 먼저 커밋·푸시한 뒤 (마)를 시작한다. 주인이 다른 세션이면 ListAgents로 확인한다.
- (마)는 변경 덩어리 단위로만 올린다. `git add -A`는 쓰지 않는다.
- 새 시험 묶음은 Rules.cs를 건드리지 않는다. `RulesWash.cs` `WashRules()` 맨 앞에서 `WashViewRules()`를 부르고, 그 안에서 `Do("화장실 그림")`·`Do("화장실 소리")`로 나눈다 (`Do`는 이름만 본다).
- 파일 줄 번호가 밀렸다. 작업 트리에서 화장실 설비 갈래는 `Painter.cs` 624~629다.

**A. 샤워 칸 — 쓰는 동안 커튼이 닫히고 · 물이 흐르면 물줄기 · 젖은 받침** (층: 화면)
- 무엇:
  - 몸 `WashBody(3)`은 한 장으로 둔다. 커튼은 왼쪽 4칸에 접힌 그림이다. 굽는 덩어리에 들어가도 틀리지 않는다.
  - 장마다 덧그림 ① 물이 흐르면 (`ShowerRunning`) 샤워기에서 받침까지 물줄기 4장 (frame & 3, 캐시)과 젖은 받침 빛.
  - 장마다 덧그림 ② 사람이 그 칸에 서 있고 지금 걸음이 그 샤워 칸의 Use Shower면, 그 사람을 그린 바로 뒤에 닫힌 커튼 앞 그림을 얹는다. 막대 위로 머리만 보인다.
  - 몸의 자리만으로 정하지 않는 까닭: 들어서는 이, 지나가는 이, 쓰러진 이도 그 칸에 선다 (`WashAhead`의 "누가 그 위에 서 있다"). 로봇은 닫지 않는다.
  - 커튼은 세계 값이 아니다 — 걸음을 그릴 뿐이다.
- 어디:
  - `PainterWash.cs`: 접은 커튼, `ShowerStream(f4)`, `ShowerCurtain()`.
  - `Painter.cs`: 설비 고리 뒤 장마다 층 (`!_bake`), 사람 고리.
  - `PainterFar.cs`: 같은 두 자리. 덮개는 `Down(…, k)`로, 커튼은 사람 판 `sc`에 `_farFront`처럼 `PutClip`으로.
  - `NetworkShower.cs`: `public bool ShowerRunning(int d) => ValveOpen(d) && WaterAtDevice(d) && WaterTank > 0` (읽기만).
- 시험: 규칙 '화장실 그림' (`RulesWashView.cs`, WashShip, `Lighting = false`, frame 0, 약 1초)
  - 기준 그림의 샤워 칸 청록 점 N0, 물빛 점 W0을 센다.
  - (a) 사람을 샤워 칸에 두고 계획을 [Use Shower] 한 걸음으로 차린다 (틱 없이 그림만) → 청록 ≥ N0 + 250, 막대 아래 옷 색 점 0, 막대 위 머리 점 > 0.
  - (b) 사람을 빼고 `SetValve(열림)` (틱 없이) → 물줄기 자리 물빛 ≥ W0 + 40, 청록 그대로 N0.
  - (c) 잠그면 W0으로 돌아간다.
  - (d) 넓은 보기 16도트 같은 차림 → (a)의 청록과 (b)의 물빛이 기준보다 늘어난다.
  - 고치기 전 코드: 커튼이 반쯤 친 채 고정되고 물줄기가 없다 → 실패.
- 지문: 같음. 비용: 보이는 샤워 칸마다 밸브 읽기 하나, 사람마다 칸 비교 하나.

**B. 김 · 습한 방의 안개 · 거울 김 서림** (층: 화면)
- 무엇:
  - 습도는 `Field.RelHumidity(c)`로 읽는다 (새 읽기 함수 — Life.MoldStep과 같은 식).
  - ① 뜨거운 물이 흐르면 (`ShowerRunning && net.Working(d)`) 샤워기 위로 `DotFx.Puff` 흰 뭉치 3~6개가 장마다 오른다. 짙기 0.25 + 0.5 × clamp((습도 − 0.5)/0.45). 찬물이면 김은 없고 물줄기만 있다.
  - ② 습도가 0.9를 넘는 바닥 칸에 흰 막을 덮는다. 짙기 0.25 × (습도 − 0.9)/0.1. 사람 위에 덮는다.
  - ③ 세면대 칸 습도가 0.85를 넘으면 거울 사각(7..24, 1..9)에 흰 덮개를 덮는다. 짙기 0.6 × (습도 − 0.85)/0.15.
  - 셋 다 장마다 그리는 덮개다. 습도는 이어지는 값이라 캐시 열쇠나 굽는 덩어리에 넣지 않는다 (설계안의 '김 서림 열쇠'를 뺐다).
- 어디: `PainterWash.cs` Steam·Fog·MirrorFog, `Painter.cs` 떠 있는 물방울 층 뒤, `PainterFar.cs` 물방울·연기 층 곁 (D도트로), `Field.cs` RelHumidity.
- 시험: 같은 묶음
  - (a) 사람 없이 `SetValve` + `SetOn` 하고 3틱 (전기가 들어 Working이 된다. 주컴퓨터가 빈 샤워 칸을 잠그는 2분보다 짧다) → 샤워기 위 3줄 띠의 밝기 230 넘는 점이 밸브를 닫은 기준보다 20 넘게 늘어난다.
  - (b) 온수기를 끄면 → 흰 점은 기준 그대로, 물줄기는 있다.
  - (c) 화장실 칸 gH2O를 습도 0.97로 채운다 (`RulesCracks.cs` 110의 식) → 가구 없는 바닥 칸 밝기 평균 +8 넘게, 거울 사각의 밝은 점이 늘어난다.
  - (d) 습도 0.4 → 기준 그대로.
  - 고치기 전 코드: 김·안개를 그리지 않는다 → 실패.
- 지문: 같음.
- 비용: 보이는 바닥 칸마다 exp 하나 (2만 칸이면 약 0.3ms). `Lab --bench` 그리기 예산을 넘을 때만 SatKPa를 1K 간격 표로 바꾼다.

**C. 곰팡이 얼룩** (층: 화면)
- 무엇:
  - 설계안과 같다: Mold ≥ 0.05인 칸에 녹 얼룩 꼴(2×2, `DotHash.U`)로 검푸른 점이 무리 지어 핀다. 빛깔 셋은 칸마다 해시로 고른다.
  - 화장실 설비 칸 판정은 `FurnitureAt[c] >= 0 && _washKind[…] != 0`로 한다. 그 칸은 줄눈과 가장자리 4도트에만 핀다. 다른 가구 칸은 건너뛴다.
  - 넓은 보기는 덩어리에 굽는다. 곰팡이 단 여섯(0.05 · 0.1 · 0.2 · 0.4 · 0.7 · 1)을 `StampOf`에 해시로 더하고, 굽는 덩어리는 단 값으로 그린다 (그을음 단과 같은 꼴).
  - 그을음 곁, `if (_bake) continue` 앞에 둔다.
- 시험: '재질 흔적' 묶음에 하나 (`RulesTraces.cs`, 디딤호 식당, 약 1초)
  - 가구 없는 바닥 칸 Mold 0으로 기준 그림을 찍는다.
  - Mold 0.5 → 기준보다 어두워지고(R+G+B 60 넘게 낮아짐) G ≥ R인 점이 30 넘게.
  - 다시 0 → 0.
  - 넓은 보기 16도트 (`FarBakeMax = 1`, 두 장) → 같은 칸이 어두워진다.
  - 고치기 전 코드: 0 → 실패.
- 지문: 같음. 비용: 칸마다 배열 읽기 하나, 도장에 칸마다 단 하나.

**D. 변기 상태 — 때 · 막힘 · '사용 금지' 불** (층: 화면)
- 무엇:
  - 몸에 굽는 것은 때 3단 × 막힘(`Clogged` 또는 `Bowl > 0`)의 6장이다. 캐시 열쇠 `Key(5, 9100 + wk*16 + 상태)`에 넣고, `StampOf`에 변기마다 그 단을 더한다.
  - '사용 금지' 빨간 불 2×2(`Mind.WashSign`)는 장마다 그린다. Lighting이면 `_emissive`로, 넓은 보기는 `FarGlows`에 화장실 갈래를 더해 그린다 (지금은 `continue`).
  - 화면은 신의 눈이라 주컴퓨터가 모르는 때도 보인다.
  - (덧붙임 · 선택) 고장 등 주황: 팬 Broken && Supply > 0.05. `ToiletOk`가 "고장 등을 보고 안다"고 적었으니 화면에도 보인다. 전기가 끊긴 경우의 앎은 (2)대로 8단에서 고친다.
- 시험: 설계안과 같다. 더해서:
  - `Grime`·`Clogged`는 `life.Scrub(fu)`를 먼저 불러 배열을 잡은 뒤 값을 넣는다.
  - 넓은 보기: Grime 0.9 → 다시 굽기 한 장 뒤 그 칸의 갈색 점이 는다. `SignToilet(fu, true)` → `FrameFar`에서 빨강 점.
- 지문: 같음.

**E. 설비 칸 바닥 물 — 넘친 물은 변기 발치에** (층: 화면)
- 무엇:
  - 물 2몰 넘게 고인 칸이 화장실 설비 칸이면 (`FurnitureAt[c] >= 0`을 먼저 확인), `WaterTile`로 칸을 통째로 덮지 않는다.
  - 칸 22~31줄(대야와 세면대 앞판 아래)에 타원 웅덩이로 그린다. 짙기 min(0.85, 물/60).
  - `Draw`와 `FrameFar`의 물 덮개 두 곳 모두 고친다.
- 시험: WashShip 변기 칸에 물 30몰
  - 대야 6×6 (칸 14~19줄)의 점이 물 없을 때와 같다.
  - 22~31줄의 물빛 점이 기준보다 30 넘게 는다.
  - 고치기 전 코드: 대야가 물빛 덮개로 바뀐다 → 실패.
- 지문: 같음. 비용: 0.

**F. 얼룩진 옷 · 지린 옷** (층: 화면)
- 무엇:
  - 칸 고르기: 부위 2~9(다리·몸통 7·팔 8·9)에 있는 옷 색표 칸만 목록으로 모으고, 해시로 6 · 14 · 24개를 고른다 (모자라면 있는 만큼). 단계 문턱은 설계안대로 Dirt 0.35 · 0.6 · 0.85.
  - 옷 색표: `l.Cloth`, `l.Accent`, `ClothNavy`, 그 사람의 바지 색.
  - 빼는 것: 머리·얼굴 부위(20 넘게)의 Accent(머리끈·볼 홍조), 신발과 고무.
  - 지림: 다리의 옷 칸 가운데 맨 윗 5줄을 한 단 어둡게. 부위 번호가 아니라 색표와 줄로 고른다 (옆 그림은 부위 3이 신발이다).
  - 새 색표는 `Ramps.Add` 한 줄 (ShipView 안 static). DotPeople.cs는 건드리지 않는다.
  - 서기·누운·기는 그림 셋과 `Portrait`에 모두 같은 `Stain`을 칠한다. 캐시 열쇠는 사람 그림 50~52비트, Portrait 43~45비트.
- 시험: `RulesPeopleArt.cs` 둘째 블록의 세계(디딤호 · 30틱 돌린 것)를 다시 쓴다.
  - 기준 갈색 점 n0을 센다.
  - Dirt 0.8 → n0 + 10 넘게.
  - Wet 1 → 다리 윗부분의 어두운 점 > 6.
  - 우주복 → n0.
  - `Rinse(ShowerMol)` → n0, 다리가 원래대로.
  - 옆 그림에서 신발 칸이 바뀌지 않는다.
  - 고치기 전 코드: Dirt·Wet를 읽지 않는다 → 실패.
- 지문: 같음.

**G. 세면대 물줄기 — 손 씻는 동안** (층: 화면 · 작다 — 30초 손 씻기는 기본 연출에서 0.06초로 보인다)
- 무엇:
  - 쓰는 이는 지금 걸음이 Use Hands이고 Target이 그 세면대인 사람이다. 곁 칸에 서므로 칸으로 찾지 않는다.
  - 그 표는 설비 고리 전에 한 번 만든다.
  - 물은 `net.WaterAt(세면대) && WaterTank > 0`일 때만. 장마다 덧그림이고 굽지 않는다.
- 시험: 계획 한 걸음 [Use Hands · Target 세면대]과 곁 칸의 사람을 차리고 틱 없이 그림을 찍는다.
  - 수도꼭지 아래 물빛 점이 기준보다 6 넘게.
  - 물관을 끊으면 (`CutLines`) 기준 그대로.
- 지문: 같음.

**H. 진공 변기 '쉬익'** (층: 소리 — 월드 + 화면)
- 무엇:
  - (월드) `Life.Flush`: `_net ??=` 바로 뒤, 막힘 갈래 앞에서 팬 장치가 없거나(망 없는 시험 배) Working이면 `Emit(sim, cell, FlushDb, SoundKind.Flush, 0, Sound.BodyActor(body))`.
  - `FlushDb = 72`는 Life 상수로 둔다 (`CoughDb` 꼴). 기능 확인용 기본값이다.
  - 변기 db 40 → 0은 뺀다. 맨 끝 조절 목록에 "변기 팬은 내릴 때만 돈다 · 낡은 팬의 끽끽도 내릴 때"로 적는다.
  - (화면) `MakeShot` → `Whoosh` 1.8초, 자막 "쉬익 — 변기 물 내림", `AllVoices`에 더한다 (설계안대로).
- 시험: '화장실 소리'
  - `life.Flush`를 직접 부른다 → `LogSince`에 Flush 하나, 변기 칸, 72dB, 낸 이 = BodyActor.
  - 흡입 팬 기계 부품을 망가뜨리면 → 0.
  - (화면) LiveShow 귀를 화장실에 → 다음 장 Shots에 Whoosh, 자막 '쉬익'.
- 지문: 바뀜.

**I. 샤워 물소리 — 물이 흐르면 쏴아** (층: 소리)
- 무엇:
  - `Network.NoiseDb`: 샤워면 `ShowerRunning(d) ? Def.Db(50) + WornDb : 0`. 온수기와 상관없이 물 흐름을 따른다. 60을 박지 않는다.
  - (화면) `Machines`에서 샤워 장치는 `LoopKind.Water` (열쇠 5,000,000 + 장치) → `Spray`. `Mixer`의 `Make`·갱신 갈래·끝내기에 더한다.
  - 있던 결함 고침: 기계 루프 열쇠를 6,000,000 + 장치로 옮긴다. 음악 루프 열쇠는 둔다 — 곡의 씨앗이 열쇠라 사람마다 곡이 그대로 남는다.
- 시험:
  - 사람 없이 밸브만 열고 온수기 끔 → `net.NoiseDb(샤워) == 50` (틱 없이).
  - 닫고 온수기만 켬 → 0. 열고 샤워 칸 물관을 끊음 → 0.
  - 열고 다음 1분 경계까지(25틱 안) → `snd.DroneDb == 50`.
  - (화면) 귀를 화장실에 → 열렸으면 Loops에 Water 하나, 샤워 장치 열쇠의 Machine 없음. 닫혔으면 Water 없음.
  - 열쇠 겹침: 사람 i가 스피커를 틀고 장치 4i+1이 들리는 차림 → 두 루프의 열쇠가 다르다 (고치기 전에는 같다 → 실패).
- 지문: 바뀜.

**J. 손 씻는 소리** (층: 소리)
- 무엇:
  - `Life.HandWash(sim, body, water, cell)` = `Wash(…, 300, cell)` + 물이 0보다 많으면 `Emit(cell, FaucetDb 55, Faucet, 0, BodyActor(body))`.
  - cell은 세면대 칸이다 (사람은 곁 칸에 서지만 물소리는 세면대에서 난다).
  - `WashDone`의 Hands 갈래는 부르는 함수와 칸만 바꾼다. 300K 물이라 김·열은 지금처럼 0이다 (세계는 그대로 · 소리만 는다).
  - (화면) `Faucet` 목소리 1.5초, 자막 "쏴 — 손 씻는 물".
- 시험:
  - 계획 [Use Hands 30초] 한 걸음을 차려 13틱 → HandWashes 1, Faucet 하나, 세면대 칸, 55dB.
  - 물관을 끊으면 → 0과 0.
  - (화면) Shots에 Faucet.
- 지문: 바뀜.

**K. 관찰 장면 `--scene washview` + `--listen wash`** (층: 화면)
- 설계안대로 한다. 더해서:
  - 넓은 보기 16도트 장을 한 장 찍는다.
  - 차림(원인 Setup): 제습기를 "끄는" 대신 기계 부품을 망가뜨린다 ("지난주 제습기 모터가 멈췄다").
  - 장면 끝 줄의 일곱 수가 모두 0이 아닌지 본다.

**L. (선택) 앞 가림**
- 6도트 넘게 솟은 화장실 설비 몸(변기 6 · 세면대 10 · 샤워 14)을 `_front`에 넣는다. 굽기는 `_farFront`로 저절로 모인다.

## 고친 위험
- 그대로 둔 것:
  - 짧은 연출: 샤워는 화면 0.75초, 손 씻기는 0.06초다. 세계 그대로라 붙들지 않는다.
  - 쉬익이 한 장에 몰리면 Shots 8 안에서 큰 것만 남는다.
  - 밤 쉬익이 얕은 잠을 깨울 수 있다 (72 · 50 · 55dB는 기본값이다).
  - 찬물 샤워에도 이제 소리가 난다 (의도한 바뀜).
  - 사람 그림 캐시가 더 찬다.
  - 곰팡이는 느리게 자라 관찰 장면은 Setup으로 보인다.
  - 세면대 물은 걸음 끝에 한 번에 꺼낸다 (8단에서 밸브로).
  - 커튼은 월드 값이 아니다.
  - 빈 배열 길이를 확인한다.
- 뺀 것: 변기 40dB 걷기. 이번 단에서 뺐다.
- 더한 것:
  - 넓은 보기의 도장·덮개·`FarGlows`를 빠뜨리면 미커밋 `RulesFar`의 '같은 그림' 시험이 빨강이 난다 (끄지 않는다).
  - 미커밋 24파일과 겹친다 → 0번 조건.
  - 화면 소리 열쇠 옮김은 화면에만 영향이 있다 (지문 같음).
- 커밋은 셋으로 나눈다.
  - ① 그림 A~G·L — 지문 같음 (`Lab --fingerprint`).
  - ② 소리 H~J + 열쇠 겹침 — 지문 바뀜.
  - ③ 장면 · WASH.md '(마) 된 것' · README 진행률 · docs/screens/wash 전후 그림.

## (마) 밖 — NEXT.md에 적을 것
- 8단 (판단이 참 세계를 읽거나 쓰는 곳):
  - `ToiletOk`가 모든 변기의 `net.Working`을 어디서든 안다.
  - 주컴퓨터 청소 일감이 카메라 없이 `Mold`를 읽는다.
  - `MindAct` 547이 `Mold`와 바닥 물을 직접 쓴다.
- (라) 또는 8단: 물이 안 나온 세면대를 피하지 않는다. 샤워 칸에는 `Avoid`와 일지가 있다 — 같은 실패를 되풀이하지 않는다는 원칙에 어긋난다.
- (라) 사고 법칙: 팬이 멈춘 변기도 오줌은 내려간다. `Flush`에서 대변이 없으면 팬을 보기 전에 돌아간다.
- 맨 끝 조절: 변기 대기 소리 40dB, 낡은 팬의 끽끽 시점.

관련 파일:
- /home/user/shipship/engine/ShipView/PainterWash.cs
- /home/user/shipship/engine/ShipView/Painter.cs
- /home/user/shipship/engine/ShipView/PainterFar.cs
- /home/user/shipship/engine/ShipView/PainterPeople.cs
- /home/user/shipship/engine/ShipView/PainterTraces.cs
- /home/user/shipship/engine/ShipView/Audio/Acoustics.cs
- /home/user/shipship/engine/ShipView/Audio/Mixer.cs
- /home/user/shipship/engine/ShipEngine/Life/LifeWash.cs
- /home/user/shipship/engine/ShipEngine/Network/NetworkShower.cs
- /home/user/shipship/engine/ShipEngine/Network/Network.cs
- /home/user/shipship/engine/ShipEngine/Mind/MindWash.cs
- /home/user/shipship/engine/ShipEngine/ShipMind/ShipMindChores.cs
- /home/user/shipship/engine/data/devices.json
- /home/user/shipship/engine/Lab/RulesWash.cs
- /home/user/shipship/engine/Lab/RulesFar.cs
- /home/user/shipship/engine/Lab/RulesTraces.cs
- /home/user/shipship/engine/Lab/RulesPeopleArt.cs
