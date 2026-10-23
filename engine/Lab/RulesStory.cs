using ShipEngine;

namespace Lab;

/// <summary>원인 사슬 (낡음 · 금 · 먼 운석) · ⑬ 연출 규칙 시험.</summary>
public static partial class Program
{
    private static void StoryRules()
    {
        if (Do("원인 사슬"))
        {
            Console.WriteLine("\n[원인 사슬]");

            // ── 낡은 부품 → 몇 시간 안에 고장 · 원인 사슬의 뿌리가 그 낡음 ──
            {
                var s = ShipWorld.Create("Didim", 4);
                var net = s.Get<Network>();
                var dev = net.Devices.First(d => net.On[d.Id] != 0 && d.Life(Part.Mechanical) > 0 && !net.IsBroken(d.Id, Part.Mechanical));
                int c0 = s.Causes.Add(CauseKind.Wear, $"{dev.Name}의 기계 부품이 오래돼 닳았다", s.Tick, dev.Cell);
                net.AgeLeft(dev.Id, Part.Mechanical, 1.0, c0);
                long failAt = -1; int failCause = 0;
                for (int k = 0; k < Time.Hours(8) && failAt < 0; k += 25)
                {
                    s.Run(25);
                    foreach (var f in s.Facts.Since(0))
                        if (f.Kind == FactKind.MachineFail && f.Cell == dev.Cell) { failAt = f.Tick; failCause = f.Cause; break; }
                }
                var chain = failCause != 0 ? s.Causes.Chain(failCause) : new List<int>();
                Check("낡은 부품은 몇 시간 안에 그 장치가 스스로 고장 난다 · 고장의 뿌리가 그 낡음", failAt > 0 && chain.Count >= 2 && chain[0] == c0,
                    $"{(failAt > 0 ? $"{failAt / 1500.0:0.0}시간 뒤 고장" : "고장 없음")} · {(failCause != 0 ? s.Causes.Story(failCause) : "")}");
            }

            // ── 같은 자리에서 같은 일이 또 일어나면 원인 이야기는 한 번만 (금이 갔다 → 금이 갔다 → … 가 아니게) ──
            {
                var s = ShipWorld.Create("Didim", 4);
                var stc = s.Get<Structure>(); var fld = s.Get<Field>(); var L = s.Layout;
                bool NextToRoom(int i) => new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Any(d => L.In(L.X(i) + d.Item1, L.Y(i) + d.Item2) && L.Room[L.Index(L.X(i) + d.Item1, L.Y(i) + d.Item2)] >= 0);
                int cell = Enumerable.Range(0, L.N).First(i => L.Hull[i] && L.Kind[i] == CellKind.Wall && fld.Hole[i] <= 0 && NextToRoom(i));
                // 숨은 피로: 1기압 부하가 정격의 0.93이 되게 + 판 두께를 넘는 금 (8cm)
                double rating = stc.Rating(cell) / Math.Max(1e-3, (1 - stc.Fatigue[cell]) * (1 - stc.Corrode[cell]));
                stc.Fatigue[cell] = (float)Math.Clamp(1 - 101.3 / (rating * 0.93), stc.Fatigue[cell], 0.95);
                int c0 = s.Causes.Add(CauseKind.Wear, "외판 한 곳에 보이지 않는 금이 자라고 있었다", s.Tick, cell);
                stc.DamageCause[cell] = c0;
                stc.SeedCrack(s, cell, 0.08, c0);
                int cracks = 0; int lastCause = 0;
                for (int k = 0; k < Time.Hours(12) && cracks < 3; k += 25)
                {
                    s.Run(25);
                    var cr = s.Facts.Since(0).Where(f => f.Kind is FactKind.WallCrack or FactKind.WallBreach && f.Cell == cell).ToList();
                    cracks = cr.Count; if (cr.Count > 0) lastCause = cr[^1].Cause;
                    if (fld.Hole[cell] > 0) stc.Patch(cell, 0); // 누가 막았다 — 피로는 그대로라 또 금이 간다
                }
                var texts = lastCause != 0 ? s.Causes.Chain(lastCause).Select(s.Causes.Text).ToList() : new List<string>();
                bool repeats = texts.Zip(texts.Skip(1)).Any(p => p.First == p.Second);
                Check("외판 피로: 금이 몇 번 더 자라도 원인 이야기에 같은 말이 되풀이되지 않는다 · 뿌리는 그 숨은 금", cracks >= 2 && !repeats && texts.Count >= 2 && texts[0] == s.Causes.Text(c0),
                    $"금 {cracks}번 · {(lastCause != 0 ? s.Causes.Story(lastCause) : "금 없음")}");
            }

            // ── 먼 운석도 끝까지 온다 (예전: 1,000km 밖에서 놓은 운석이 바로 지워졌다) ──
            {
                var s = ShipWorld.Create("Didim", 3);
                int c0 = s.Causes.Add(CauseKind.Setup, "먼 운석", s.Tick);
                s.Get<Outside>().AddRock(1, 8000, 1, 0, 0, 8000 * 60 * 20, c0); // 20분 거리 · 9,600km
                s.Run(Time.Minutes(22));
                var o = s.Get<Outside>();
                Check("20분 거리(9,600km)에서 놓은 운석도 사라지지 않고 와서 부딪힌다", o.RockHits == 1, $"충돌 {o.RockHits} · 조짐 {s.Facts.CountOf(FactKind.DebrisNear)}");
            }
        }

        if (Do("연출"))
        {
            Console.WriteLine("\n[연출]");
            var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
            int At(int x, int y) => Worlds.At(lay, x, y);

            // ── 조용하면 빨리 · 불이 나면 카메라가 불로 가고 느려진다 ──
            {
                var s = Worlds.Full(lay, 2);
                var d = new Director(s.Layout) { KeepRing = false };
                for (int i = 0; i < 40; i++) { s.Step(); d.Observe(s); }
                double calmSpeed = d.Speed; string calmMood = d.Mood;
                int fire = At(15, 1); // 오른쪽 방 의자
                s.Get<Chemistry>().Ignite(fire, 3e6, s.Causes.Add(CauseKind.Setup, "의자 밑 불씨", s.Tick));
                int near = -1; double slow = 1e9;
                for (int i = 0; i < 60; i++)
                {
                    s.Step(); d.Observe(s);
                    slow = Math.Min(slow, d.Speed);
                    if (near < 0 && d.FocusCell >= 0 && Math.Max(Math.Abs(lay.X(d.FocusCell) - lay.X(fire)), Math.Abs(lay.Y(d.FocusCell) - lay.Y(fire))) <= 3) near = i;
                }
                Check("조용할 땐 빨리 감는다 · 불이 붙으면 카메라가 불로 가고 시간이 느려진다", calmSpeed == Director.CalmSpeed && near >= 0 && near < 10 && slow <= Director.DangerSpeed,
                    $"처음 {calmMood} {calmSpeed}틱/초 · 불 쪽으로 {near}틱 · 가장 느리게 {slow}틱/초 · 지금 '{d.Mood}'");
            }

            // ── 하루 몇 분 (docs/REVIEW.md ① — 함장 결정 '추천대로'): 조용함은 하루 목표에 맞춰 빨리 · 가벼운 알아챔 · 외침은 카메라만 ·
            //    같은 뿌리의 조짐은 장면 하나 · 느린 시간 예산을 다 쓰면 소문은 늦추지 않고 다툼 · 탓 · 감지만 ──
            {
                var s = Worlds.Full(lay, 2);
                var d = new Director(s.Layout) { KeepRing = false, DayMinutes = 3 };
                for (int i = 0; i < 40; i++) { s.Step(); d.Observe(s); }
                double calm3 = d.Speed;
                int cell = At(15, 2);
                string Step(int n) { for (int i = 0; i < n; i++) { s.Step(); d.Observe(s); } return d.Mood; }
                s.Facts.Emit(s.Tick, FactKind.Noticed, cell);
                string afterNotice = Step(2);
                int root = s.Causes.Add(CauseKind.Setup, "센서에 잡힌 것", s.Tick);
                s.Facts.Emit(s.Tick, FactKind.Sensed, cell, 1, root);
                Step(2);
                long opened = s.Tick;
                Step((int)Time.Minutes(2));
                s.Facts.Emit(s.Tick, FactKind.Sensed, cell, 1, root);   // 같은 뿌리 — 창을 늘리지 않는다
                Step((int)Time.Minutes(1.2));
                string sameRoot = d.Mood;                               // 처음 연 때부터 3분 넘음 → 조용함
                // 느린 시간 예산(하루 3분의 4할 = 72초)을 다 쓴다: 감지 하나마다 조짐 3분 = 화면 약 9초
                for (int k = 0; k < 8; k++) { s.Facts.Emit(s.Tick, FactKind.Sensed, cell, 1, s.Causes.Add(CauseKind.Setup, $"감지 {k}", s.Tick)); Step((int)Time.Minutes(3.1)); }
                double slowUsed = d.SlowTodaySec;
                s.Facts.Emit(s.Tick, FactKind.Talk, cell, (float)TalkEnd.Spread, s.Causes.Add(CauseKind.Setup, "소문", s.Tick));
                string rumor = Step(2);
                s.Facts.Emit(s.Tick, FactKind.Talk, cell, (float)TalkEnd.Argue, s.Causes.Add(CauseKind.Setup, "다툼", s.Tick));
                string argue = Step(2);
                Check("하루 3분: 조용하면 화면 1초에 약 200틱 (조용한 날 = 꼭 3분) · 가벼운 알아챔은 카메라만 · 같은 뿌리의 감지는 3분 창을 다시 늘리지 않는다 · 느린 시간(72초)을 다 쓰면 소문은 늦추지 않고 다툼은 늦춘다",
                    calm3 is >= 190 and <= 210 && afterNotice == "조용함" && sameRoot == "조용함" && slowUsed >= 72 && rumor == "조용함" && argue == "조짐",
                    $"조용함 {calm3:0}틱/초 · 알아챔 뒤 {afterNotice} · 같은 뿌리 {sameRoot} · 쓴 느린 시간 {slowUsed:0}초 · 소문 {rumor} · 다툼 {argue} (열린 때 {Time.Clock(opened)})");
            }

            // ── 비슷한 두 불 사이를 오가지 않는다 (머물기) ──
            {
                var s = Worlds.Full(lay, 2);
                var d = new Director(s.Layout) { KeepRing = false };
                s.Run(5);
                var ch = s.Get<Chemistry>();
                ch.Ignite(At(3, 5), 3e6, s.Causes.Add(CauseKind.Setup, "왼쪽 방 침대 불씨", s.Tick));
                ch.Ignite(At(15, 1), 3e6, s.Causes.Add(CauseKind.Setup, "오른쪽 방 의자 불씨", s.Tick));
                for (int i = 0; i < Time.Minutes(3); i++) { s.Step(); d.Observe(s); }
                Check("비슷한 두 사건 사이를 이리저리 오가지 않는다 (한 장면에 머문다)", d.Switches <= 2, $"장면 바꿈 {d.Switches}번 · 화면 {d.ScreenSeconds:0}초");
            }

            // ── 한 소동은 한 장면: 잔해 폭풍이 외판 여러 곳을 뚫고 주컴퓨터가 방마다 봉쇄해도 하이라이트는 한둘 ──
            {
                var s = ShipWorld.Crewed("Hwasal", 18, 2, 4, 0);
                var d = new Director(s.Layout) { KeepRing = false };
                s.Run(Time.Minutes(10));
                int c0 = s.Causes.Add(CauseKind.Setup, "잔해 지대가 항로로 들어온다", s.Tick);
                var dd = s.Tables.Space.Debris["rubble"];
                s.Get<Outside>().AddCloud("rubble", dd.Speed * 120 + 300_000, 300_000, 1.2e-6, 0, 1, c0);
                for (int i = 0; i < Time.Minutes(40); i++) { s.Step(); d.Observe(s); }
                long seals = s.Facts.CountOf(FactKind.ComputerSeal), breaches = s.Facts.CountOf(FactKind.WallBreach);
                Check("한 소동은 한 장면: 잔해 폭풍에 구멍 · 봉쇄가 여럿이어도 하이라이트는 한둘", breaches + seals >= 4 && d.Highlights.Count is >= 1 and <= 2,
                    $"벽이 뚫림 {breaches} · 봉쇄 {seals} → 하이라이트 {d.Highlights.Count}: {string.Join(" / ", d.Highlights.Select(h => h.Title))}");
            }

            // ── 하이라이트는 첫 조짐부터 · 다시보기는 같은 일을 그대로 되풀이한다 ──
            {
                var s = ShipWorld.Create("Didim", 3);
                var d = new Director(s.Layout);
                int c0 = s.Causes.Add(CauseKind.Setup, "운석 하나가 배를 향해 온다", s.Tick);
                s.Get<Outside>().AddRock(1.5, 6000, -1, 0.15, 2, 6000 * 60 * 4, c0); // 4분 거리
                d.Observe(s);
                long hitAt = -1; string moodAtHit = "";
                for (int i = 0; i < Time.Minutes(9); i++)
                {
                    s.Step(); d.Observe(s);
                    if (hitAt < 0 && s.Get<Outside>().RockHits > 0) { hitAt = s.Tick; moodAtHit = d.Mood; }
                }
                var h = d.Highlights.OrderByDescending(x => x.Score).FirstOrDefault();
                long warnAt = s.Facts.Since(0).Where(f => f.Kind == FactKind.DebrisNear).Select(f => f.Tick).DefaultIfEmpty(-1).First();
                Check("큰 사건은 하이라이트가 되고, 그 시작은 첫 조짐(운석이 다가온다)보다 앞이다", h != null && hitAt > 0 && h.StartTick <= warnAt && h.StartTick <= hitAt && moodAtHit == "결정적 순간",
                    h == null ? "하이라이트 없음" : $"'{h.Title}' · 점수 {h.Score:0} · 시작 {h.StartTick} ≤ 조짐 {warnAt} ≤ 충돌 {hitAt} · 충돌 때 '{moodAtHit}'");
                if (h != null)
                {
                    var r = d.Replay(s, h);
                    bool sameEnd = false; long replayHit = -1, start = -1;
                    if (r != null)
                    {
                        start = r.Tick;
                        while (r.Tick < s.Tick) { r.Step(); if (replayHit < 0 && r.Get<Outside>().RockHits > 0) replayHit = r.Tick; }
                        sameEnd = r.Save().AsSpan().SequenceEqual(s.Save());
                    }
                    Check("다시보기: 고리에서 되살린 세계를 이어 돌리면 같은 틱에 같은 충돌 · 끝 상태가 바이트까지 같다", r != null && replayHit == hitAt && sameEnd,
                        $"되살린 틱 {start} · 충돌 {replayHit} (원래 {hitAt}) · 끝 같음 {sameEnd} · 고리 {d.Ring.Count}장");
                }
            }
        }
    }
}
