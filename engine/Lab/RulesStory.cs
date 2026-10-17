using ShipEngine;

namespace Lab;

/// <summary>⑫ 이야기꾼 · ⑬ 연출 규칙 시험.</summary>
public static partial class Program
{
    private static void StoryRules()
    {
        if (Do("이야기꾼"))
        {
            Console.WriteLine("\n[이야기꾼]");
            // ── 조용한 배에는 원인을 심는다 · 같은 시드면 같은 때 같은 원인 · 긴장이 높으면 심지 않는다 (작은 배 · 첫 두 시간이 지나 처음 심는 때까지) ──
            List<Seed> Run(ulong seed, double tension)
            {
                var s = ShipWorld.Create("Hwasal", seed);
                var st = s.Get<Storyteller>(); st.Enabled = true; st.Tension = tension;
                s.Run(Time.Hours(2.5));
                return st.Seeds;
            }
            var aT = Task.Run(() => Run(5, 0)); var bT = Task.Run(() => Run(5, 0)); var hi = Run(5, 1000); var a = aT.Result; var b = bT.Result;
            bool same = a.Count == b.Count && a.Zip(b).All(p => p.First.Tick == p.Second.Tick && p.First.Kind == p.Second.Kind && p.First.Size == p.Second.Size);
            Check("조용한 배에는 이야기꾼이 원인을 심는다 · 같은 시드면 같은 때 같은 원인", a.Count >= 1 && same,
                $"심은 것 {a.Count} ({string.Join(", ", a.Select(x => $"{x.Tick / 1500.0:0.0}시 {Storyteller.SeedName(x.Kind)}"))}) · 같음 {same}");
            Check("긴장이 이미 높으면 심지 않는다 (쉬게 둔다)", hi.Count == 0, $"심은 것 {hi.Count}");

            // ── 잠복 원인을 겹겹이 쌓지 않는다: 아직 안 터진 원인(대기 긴장)도 셈한다 · 눌어붙은 차단기가 있으면 합선 불씨를 또 심지 않는다 ──
            {
                var s = ShipWorld.Create("Hwasal", 5);
                var st = s.Get<Storyteller>(); st.Enabled = true; st.Pending = 50;
                s.Run(Time.Hours(3));
                int pendingSeeds = st.Seeds.Count;
                // 차단기가 눌어붙은 배에서 이백 번 골라 본다 (16시간을 기다리지 않는다) — 멀쩡한 배에서는 합선 불씨도 고른다 (견줌)
                var s2 = ShipWorld.Create("Hwasal", 5);
                var st2 = s2.Get<Storyteller>(); st2.Style = StoryStyle.SlowBurn; st2.Level = 8;
                int Arcs() { int n = 0; for (int k = 0; k < 200; k++) if (st2.Choose(s2) == SeedKind.ArcRisk) n++; return n; }
                int healthy = Arcs();
                s2.Get<Network>().BreakerStuck[0] = 1;
                int arcs = Arcs();
                Check("잠복 원인을 쌓지 않는다: 안 터진 원인의 긴장도 셈하고 · 눌어붙은 차단기가 있으면 합선 불씨를 또 심지 않는다", pendingSeeds == 0 && arcs == 0 && healthy > 0,
                    $"대기 긴장 높을 때 심은 것 {pendingSeeds} · 이백 번 골라 합선 불씨: 멀쩡한 배 {healthy} · 차단기가 눌어붙은 배 {arcs}");
            }

            // ── 가혹함: 높을수록 큰 운석 ──
            double Mass(byte level)
            {
                var s = ShipWorld.Create("Didim", 9);
                var st = s.Get<Storyteller>(); st.Level = level;
                for (int i = 0; i < 30; i++) st.Plant(s, SeedKind.Rock);
                return st.Seeds.Average(x => x.Size);
            }
            double m2 = Mass(2), m8 = Mass(8);
            Check("가혹함이 높을수록 큰 운석을 보낸다", m8 > m2 * 2, $"가혹함 2: 평균 {m2:0.00}kg · 가혹함 8: {m8:0.00}kg");

            // ── 심은 원인이 실제 일로: 낡은 부품 → 몇 시간 안에 고장 · 원인 사슬의 뿌리가 이야기꾼 ──
            {
                var s = ShipWorld.Create("Didim", 4);
                var st = s.Get<Storyteller>(); st.Level = 8;
                st.Plant(s, SeedKind.WornPart);
                var seed = st.Seeds[0];
                long failAt = -1; int failCause = 0;
                for (int k = 0; k < Time.Hours(8) && failAt < 0; k += 25)
                {
                    s.Run(25);
                    foreach (var f in s.Facts.Since(0))
                        if (f.Kind == FactKind.MachineFail && f.Cell == seed.Cell) { failAt = f.Tick; failCause = f.Cause; break; }
                }
                var chain = failCause != 0 ? s.Causes.Chain(failCause) : new List<int>();
                Check("'낡은 부품'을 심으면 몇 시간 안에 그 장치가 스스로 고장 난다 · 고장의 뿌리가 이야기꾼의 원인", failAt > 0 && chain.Count >= 2 && chain[0] == seed.Cause,
                    $"{(failAt > 0 ? $"{(failAt - seed.Tick) / 1500.0:0.0}시간 뒤 고장" : "고장 없음")} · {(failCause != 0 ? s.Causes.Story(failCause) : "")}");
            }

            // ── 같은 자리에서 같은 일이 또 일어나면 원인 이야기는 한 번만 (금이 갔다 → 금이 갔다 → … 가 아니게) ──
            {
                var s = ShipWorld.Create("Didim", 4);
                var st = s.Get<Storyteller>(); st.Level = 8;
                st.Plant(s, SeedKind.HullFatigue);
                var seed = st.Seeds[0];
                int cracks = 0; int lastCause = 0;
                var stc = s.Get<Structure>(); var fld = s.Get<Field>();
                for (int k = 0; k < Time.Hours(12) && cracks < 3; k += 25)
                {
                    s.Run(25);
                    var cr = s.Facts.Since(0).Where(f => f.Kind is FactKind.WallCrack or FactKind.WallBreach && f.Cell == seed.Cell).ToList();
                    cracks = cr.Count; if (cr.Count > 0) lastCause = cr[^1].Cause;
                    if (fld.Hole[seed.Cell] > 0) stc.Patch(seed.Cell, 0); // 누가 막았다 — 피로는 그대로라 또 금이 간다
                }
                var texts = lastCause != 0 ? s.Causes.Chain(lastCause).Select(s.Causes.Text).ToList() : new List<string>();
                bool repeats = texts.Zip(texts.Skip(1)).Any(p => p.First == p.Second);
                Check("외판 피로: 금이 몇 번 더 자라도 원인 이야기에 같은 말이 되풀이되지 않는다 · 뿌리는 이야기꾼", cracks >= 2 && !repeats && texts.Count >= 2 && texts[0] == s.Causes.Text(seed.Cause),
                    $"금 {cracks}번 · {(lastCause != 0 ? s.Causes.Story(lastCause) : "금 없음")}");
            }

            // ── 외판 피로 씨앗은 기압을 받치는 외판(안쪽이 방)에만 — 착륙 다리 · 날개 같은 뼈대에 심으면 금이 자랄 수 없다 ──
            {
                var s = ShipWorld.Create("Didim", 11);
                var st = s.Get<Storyteller>(); st.Level = 8;
                var L = s.Layout;
                int planted = 0, onRoom = 0;
                for (int k = 0; k < 40; k++)
                {
                    if (!st.Plant(s, SeedKind.HullFatigue)) continue;
                    int c = st.Seeds[^1].Cell; planted++;
                    if (new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Any(d => L.In(L.X(c) + d.Item1, L.Y(c) + d.Item2) && L.Room[L.Index(L.X(c) + d.Item1, L.Y(c) + d.Item2)] >= 0)) onRoom++;
                }
                Check("외판 피로 씨앗은 늘 방에 닿은 외판에 (뼈대 칸에는 심지 않는다)", planted >= 30 && onRoom == planted, $"심음 {planted} · 방에 닿음 {onRoom}");
            }

            // ── 먼 운석도 끝까지 온다 (예전: 1,000km 밖에서 심은 운석이 바로 지워졌다) ──
            {
                var s = ShipWorld.Create("Didim", 3);
                int c0 = s.Causes.Add(CauseKind.Story, "먼 운석", s.Tick);
                s.Get<Outside>().AddRock(1, 8000, 1, 0, 0, 8000 * 60 * 20, c0); // 20분 거리 · 9,600km
                s.Run(Time.Minutes(22));
                var o = s.Get<Outside>();
                Check("20분 거리(9,600km)에서 심은 운석도 사라지지 않고 와서 부딪힌다", o.RockHits == 1, $"충돌 {o.RockHits} · 조짐 {s.Facts.CountOf(FactKind.DebrisNear)}");
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
                s.Get<Chemistry>().Ignite(fire, 3e6, s.Causes.Add(CauseKind.Story, "의자 밑 불씨", s.Tick));
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

            // ── 비슷한 두 불 사이를 오가지 않는다 (머물기) ──
            {
                var s = Worlds.Full(lay, 2);
                var d = new Director(s.Layout) { KeepRing = false };
                s.Run(5);
                var ch = s.Get<Chemistry>();
                ch.Ignite(At(3, 5), 3e6, s.Causes.Add(CauseKind.Story, "왼쪽 방 침대 불씨", s.Tick));
                ch.Ignite(At(15, 1), 3e6, s.Causes.Add(CauseKind.Story, "오른쪽 방 의자 불씨", s.Tick));
                for (int i = 0; i < Time.Minutes(3); i++) { s.Step(); d.Observe(s); }
                Check("비슷한 두 사건 사이를 이리저리 오가지 않는다 (한 장면에 머문다)", d.Switches <= 2, $"장면 바꿈 {d.Switches}번 · 화면 {d.ScreenSeconds:0}초");
            }

            // ── 한 소동은 한 장면: 잔해 폭풍이 외판 여러 곳을 뚫고 주컴퓨터가 방마다 봉쇄해도 하이라이트는 한둘 ──
            {
                var s = ShipWorld.Crewed("Hwasal", 18, 2, 4, 0);
                var d = new Director(s.Layout) { KeepRing = false };
                s.Run(Time.Minutes(10));
                int c0 = s.Causes.Add(CauseKind.Story, "잔해 지대가 항로로 들어온다", s.Tick);
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
                int c0 = s.Causes.Add(CauseKind.Story, "운석 하나가 배를 향해 온다", s.Tick);
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
