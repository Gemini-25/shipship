using ShipEngine;

namespace Lab;

/// <summary>⑬ 연출 — 볼 곳을 잘 고르는 카메라 (깊이 바퀴 2) 규칙 시험: 사슬의 앞머리 · 몸으로 읽는 조짐 · 주인공 따라가기 · 두 곳 오가기.</summary>
public static partial class Program
{
    /// <summary>긴 배: 양 끝 방 둘 사이 긴 통로 (70칸 — 한 화면에 다 안 들어온다).</summary>
    private const string LongShip = @"
######################################################################
#........#..................................................#........#
#........#..................................................#........#
#........D..................................................D........#
#........#..................................................#........#
#........#..................................................#........#
######################################################################";

    private static void CameraRules()
    {
        if (!Do("카메라")) return;
        Console.WriteLine("\n[카메라]");
        var lay = Worlds.Ascii(LongShip);
        int At(int x, int y) => Worlds.At(lay, x, y);
        int A = At(4, 3), B = At(64, 3);

        // ── 사슬의 앞머리: 지금 보는 사슬이 새 엔진으로 번지면 (화면 밖이어도) 그리로 간다 · 딴 뿌리의 작은 일에는 끌려가지 않는다 ──
        {
            var s = Worlds.Full(lay, 1);
            var d = new Director(s.Layout) { KeepRing = false };
            int r = s.Causes.Add(CauseKind.Story, "외판 한 곳이 오래 삭았다", s.Tick, A);
            int c1 = s.Causes.Add(CauseKind.Breach, "외판이 터졌다", s.Tick, A, r);
            s.Facts.Emit(s.Tick, FactKind.WallBreach, A, 1, c1);
            for (int i = 0; i < 10; i++) d.Observe(s);
            bool atA = d.InView(A) && d.SceneRoot == r;
            int other = s.Causes.Add(CauseKind.Machine, "다른 방 장치가 덜컹거린다", s.Tick, B);
            s.Facts.Emit(s.Tick, FactKind.BreakerTrip, B, 1, other);
            for (int i = 0; i < 6; i++) d.Observe(s);
            bool stayed = d.InView(A) && !d.InView(B);
            int c2 = s.Causes.Add(CauseKind.Electric, "터진 외판 너머 전선이 끊겼다", s.Tick, B, c1);
            s.Facts.Emit(s.Tick, FactKind.BreakerTrip, B, 1, c2);
            double t0 = d.ScreenSeconds, reached = -1;
            for (int i = 0; i < 40 && reached < 0; i++) { d.Observe(s); if (d.InView(B)) reached = d.ScreenSeconds - t0; }
            var rec = d.Chains.GetValueOrDefault(r);
            Check("사슬의 앞머리: 지금 보는 사슬(외판이 터짐)이 전기로 번지면 화면 밖 그곳으로 3초 안에 간다 · 딴 뿌리의 작은 일에는 끌려가지 않는다",
                atA && stayed && reached is >= 0 and <= Director.CaughtSec && d.Pulls >= 1 && rec?.Path == "이야기꾼 → 구조 → 망",
                $"처음 터진 곳 {atA} · 딴 일에 머묾 {stayed} · 번진 곳까지 {reached:0.0}초 · 끌림 {d.Pulls} · 사슬 '{rec?.Path}' ({rec?.Count}개 엔진)");
        }

        // ── 미리 보기: 오른쪽 끝으로 운석이 온다 (2분 거리) · 카메라는 왼쪽 끝 작은 일을 보고 있다 — 복제 세계로 앞을 돌려 보고, 부딪히는 그 순간에는 이미 그 자리를 보고 있다 ──
        {
            var s = Worlds.Full(lay, 7);
            var d = new Director(s.Layout) { KeepRing = false };
            int keep = s.Causes.Add(CauseKind.Fire, "왼쪽 방 상자가 그을린다", s.Tick, A);
            int c0 = s.Causes.Add(CauseKind.Story, "운석 하나가 배를 향해 온다", s.Tick);
            s.Get<Outside>().AddRock(0.3, 6000, -1, 0, 0, 6000 * 60 * 2, c0);   // 오른쪽에서 왼쪽으로 · 2분 거리
            long cursor = s.Facts.Total; bool hit = false, seenAtHit = false; int hitCell = -1;
            for (int i = 0; i < Time.Minutes(4) && !hit; i++)
            {
                if (i % 6 == 0) s.Facts.Emit(s.Tick, FactKind.FireSpread, A, 1, keep);
                s.Step();
                foreach (var f in s.Facts.Since(cursor))
                    if (!hit && f.Kind is FactKind.Impact or FactKind.WallBreach && f.Cell >= 0) { hit = true; hitCell = f.Cell; seenAtHit = d.InView(f.Cell); }   // 연출이 그 일을 읽기 전 화면
                cursor = s.Facts.Total;
                d.Observe(s);
            }
            Check("미리 보기: 오른쪽 끝으로 오는 운석 — 복제 세계로 앞을 돌려 보고 · 부딪히는 그 순간에는 이미 그 자리를 보고 있다 (왼쪽 끝 작은 일을 두고)",
                hit && seenAtHit && d.Foresights > 0 && d.ForeCuts > 0,
                $"부딪힘 {hit} (칸 x {(hitCell >= 0 ? lay.X(hitCell) : -1)}) · 그때 화면 안 {seenAtHit} · 미리 보기 {d.Foresights}번 · 본 일 {d.Predicted} · 미리 자름 {d.ForeCuts} · {d.ForeseeMs / Math.Max(1, d.Foresights):0.0}ms");
        }

        // ── 몸으로 읽는 조짐: 닫힌 방에 작은 구멍 — 기압이 천천히 떨어진다 (사실은 없다) · 배 반대편에 작은 볼거리가 줄곧 있어도 몸이 알기(산소 포화 0.9 아래) 전에 카메라가 그 사람에게 간다 ──
        {
            var sealedLay = Worlds.Ascii(LongShip.Replace("#........D......", "#........#......"));
            var s = Worlds.Full(sealedLay, 3);
            var m = s.Get<Mind>(); var life = s.Get<Life>();
            int p = m.AddPerson(s, Worlds.At(sealedLay, 4, 3));
            var d = new Director(s.Layout) { KeepRing = false };
            int far = Worlds.At(sealedLay, 64, 3), farCause = s.Causes.Add(CauseKind.Fire, "오른쪽 방 상자가 그을린다", s.Tick, far);
            for (int i = 0; i < 5; i++) { s.Step(); d.Observe(s); }
            s.Get<Field>().SetHole(Worlds.At(sealedLay, 0, 3), 0.0002f);   // 2cm²
            long tView = -1, tBody = -1;
            for (int i = 0; i < Time.Minutes(25) && (tView < 0 || tBody < 0); i++)
            {
                if (i % 12 == 0) s.Facts.Emit(s.Tick, FactKind.FireSpread, far, 1, farCause);   // 다른 곳에 볼 것이 있다 (작게 · 줄곧)
                s.Step(); d.Observe(s);
                var b = life.Bodies[m.Agents[p].C.Body];
                if (tView < 0 && d.InView(b.Cell)) tView = s.Tick;
                if (tBody < 0 && b.SpO2 < 0.9f) tBody = s.Tick;
            }
            Check("몸으로 읽는 조짐: 닫힌 방의 기압이 천천히 떨어지면 몸이 알기(산소 포화 0.9 아래) 전에 카메라가 그 사람에게 간다",
                tView >= 0 && (tBody < 0 || tView < tBody) && d.AirWarnings > 0,
                $"카메라 {(tView < 0 ? "못 감" : Time.Clock(tView))} · 몸 {(tBody < 0 ? "아직" : Time.Clock(tBody))} · 공기 조짐 {d.AirWarnings} · 기압 {s.Get<Field>().Pressure(Worlds.At(sealedLay, 4, 3)):0}kPa");
        }

        // ── 카메라만 쓰는 관심은 하이라이트가 아니다: 넷이 있는 닫힌 방에 4cm² 구멍 — 기압이 빨리 떨어져 카메라는 그 사람에게 가지만, 몸이 알기 전에는 (곁에 작은 일이 있어도) 하이라이트가 없다 ──
        {
            var sealedLay = Worlds.Ascii(LongShip.Replace("#........D......", "#........#......"));
            var s = Worlds.Full(sealedLay, 6);
            var m = s.Get<Mind>(); var life = s.Get<Life>();
            int p = m.AddPerson(s, Worlds.At(sealedLay, 4, 3));
            for (int k = 0; k < 3; k++) m.AddPerson(s, Worlds.At(sealedLay, 2 + 2 * k, 2));   // 방에 넷
            var d = new Director(s.Layout) { KeepRing = false };
            for (int i = 0; i < 5; i++) { s.Step(); d.Observe(s); }
            s.Get<Field>().SetHole(Worlds.At(sealedLay, 0, 3), 0.0004f);
            bool seen = false; long tBody = -1;
            for (int i = 0; i < Time.Minutes(15) && tBody < 0; i++)
            {
                var b = life.Bodies[m.Agents[p].C.Body];
                if (i % 25 == 0) s.Facts.Emit(s.Tick, FactKind.Noticed, b.Cell, 1, 0, p);   // 곁의 작은 일 (무언가를 알아챘다 — 이것만으로는 볼 만한 장면이 아니다)
                s.Step(); d.Observe(s);
                b = life.Bodies[m.Agents[p].C.Body];
                seen |= d.InView(b.Cell);
                if (b.SpO2 < 0.9f) tBody = s.Tick;
            }
            Check("카메라만 쓰는 관심은 하이라이트가 아니다: 기압이 빨리 떨어지는 방의 사람에게 카메라는 가지만 · 몸이 알기 전에는 하이라이트가 없다",
                seen && d.AirWarnings > 0 && d.Highlights.Count == 0 && tBody > 0,
                $"카메라 감 {seen} · 공기 조짐 {d.AirWarnings} · 하이라이트 {d.Highlights.Count} ({string.Join(" / ", d.Highlights.Select(h => h.Title))}) · 몸 {(tBody < 0 ? "아직" : Time.Clock(tBody))}");
        }

        // ── 익숙해짐: 닫힌 방의 산소가 처음부터 옅다 (14kPa · 새지 않는다) — 처음엔 조짐이지만 한 시간이 지나면 더는 아니다 (며칠을 그렇게 사는 배가 늘 '조짐'이면 화면이 늘 느리다) ──
        {
            var sealedLay = Worlds.Ascii(LongShip.Replace("#........D......", "#........#......"));
            var s = Worlds.Full(sealedLay, 5);
            var m = s.Get<Mind>(); var f = s.Get<Field>();
            m.AddPerson(s, Worlds.At(sealedLay, 4, 3));
            foreach (int c in s.Layout.Rooms[s.Layout.Room[Worlds.At(sealedLay, 4, 3)]].Cells) f.RemoveGas(c, f.T.gO2, f.N[c * f.G + f.T.gO2] * 0.32);
            var d = new Director(s.Layout) { KeepRing = false };
            int early = 0, late = 0;
            for (int i = 0; i < Time.Hours(2); i++)
            {
                int before = d.AirWarnings;
                s.Step(); d.Observe(s);
                if (d.AirWarnings > before) { if (i < Time.Minutes(10)) early++; else if (i >= Time.Hours(1)) late++; }
            }
            double o2 = f.Partial(f.T.gO2, Worlds.At(sealedLay, 4, 3));
            Check("익숙해짐: 처음부터 옅은 산소(새지 않는다) 속의 사람 — 처음 10분은 조짐 · 한 시간이 지나면 더는 조짐이 아니다",
                early > 0 && late == 0, $"처음 10분 조짐 {early} · 한 시간 뒤 {late} · 산소 {o2:0.0}kPa");
        }

        // ── 주인공 따라가기: 쓰러진 이를 업고 긴 통로를 달리는 사람을 따라간다 (구한 일이 난 자리에 남지 않는다) ──
        {
            var s = Worlds.Full(lay, 2);
            var m = s.Get<Mind>();
            int hero = m.AddPerson(s, A), hurt = m.AddPerson(s, At(5, 3));
            var d = new Director(s.Layout) { KeepRing = false };
            int cause = s.Causes.Add(CauseKind.Person, "쓰러진 이를 들쳐 업었다", s.Tick, A, 0, 0, hero);
            s.Facts.Emit(s.Tick, FactKind.Rescue, A, 1, cause, hero, hurt);
            for (int i = 0; i < 5; i++) d.Observe(s);
            bool subject = d.Subject == hero;
            m.Agents[hero].C.Carrying = hurt; m.Agents[hurt].C.CarriedBy = hero;
            int seen = 0, steps = 0;
            for (int x = 5; x <= 50; x++)
            {
                m.Agents[hero].C.Cell = At(x, 3); m.Agents[hurt].C.Cell = At(x, 3);
                for (int k = 0; k < 2; k++) { d.Observe(s); steps++; if (d.InView(At(x, 3))) seen++; }
            }
            Check("주인공 따라가기: 쓰러진 이를 업고 통로 끝까지 달리는 사람을 화면에 둔다",
                subject && seen >= steps * 0.9 && d.InView(At(50, 3)) && d.FollowTicks > 0,
                $"주인공 {subject} · 화면 안 {seen}/{steps} · 끝에서 보임 {d.InView(At(50, 3))} · 따라간 틱 {d.FollowTicks}");
        }

        // ── 두 곳 오가기: 양 끝 두 곳에서 비슷한 일이 이어지면 한 곳만 보지 않고 오간다 (한 장면에 적어도 20초) ──
        {
            var s = Worlds.Full(lay, 4);
            var d = new Director(s.Layout) { KeepRing = false };
            int ra = s.Causes.Add(CauseKind.Fire, "왼쪽 방 침대가 탄다", s.Tick, A), rb = s.Causes.Add(CauseKind.Fire, "오른쪽 방 상자가 탄다", s.Tick, B);
            double seenA = 0, seenB = 0, last = 0, minHold = double.MaxValue, since = 0; bool? onA = null;
            for (int i = 0; i < 9000; i++)
            {
                if (i % 6 == 0) s.Facts.Emit(s.Tick, FactKind.FireSpread, A, 1, ra);
                if (i % 6 == 3) s.Facts.Emit(s.Tick, FactKind.FireSpread, B, 1, rb);
                d.Observe(s);
                double dt = d.ScreenSeconds - last; last = d.ScreenSeconds;
                bool a = d.InView(A), b = d.InView(B);
                if (a) seenA += dt; if (b) seenB += dt;
                if (a != b)
                {
                    if (onA != null && onA != a) { minHold = Math.Min(minHold, since); since = 0; }
                    onA = a; since += dt;
                }
            }
            Check("두 곳 오가기: 양 끝에서 비슷한 일이 이어지면 두 곳을 다 보여 준다 (한 곳에 적어도 20초)",
                seenA >= 20 && seenB >= 20 && minHold >= Director.CrossHoldSec - 1 && d.CrossCuts >= 2,
                $"왼쪽 {seenA:0}초 · 오른쪽 {seenB:0}초 · 엇갈려 자름 {d.CrossCuts} · 가장 짧게 머문 것 {(minHold == double.MaxValue ? 0 : minHold):0}초 · 화면 {d.ScreenSeconds:0}초");
        }

        // ── 하이라이트 문턱은 고정: 첫 말다툼은 하이라이트 · 한 시간 뒤 · 조용한 일곱 시간 뒤의 같은 크기 말다툼은 아니다 (볼 만한 일은 세계가 만든다 — 연출이 조용한 날 문턱을 낮추지 않는다 · 함장 기준) ──
        {
            var s = Worlds.Full(lay, 2);
            var d = new Director(s.Layout) { KeepRing = false };
            void Argue(string what)
            {
                int r = s.Causes.Add(CauseKind.Person, what, s.Tick, A);
                int c = s.Causes.Add(CauseKind.Person, "언성이 높아졌다", s.Tick, A, r);
                s.Facts.Emit(s.Tick, FactKind.Talk, A, (int)TalkEnd.Argue, c);
                for (int i = 0; i < 5; i++) d.Observe(s);
            }
            void Pass(double hours) { for (long k = 0; k < Time.Hours(hours); k++) { s.Step(); d.Observe(s); } }
            Argue("배식 순서로 다퉜다");
            int first = d.Highlights.Count;
            Pass(1); Argue("빨래 당번으로 다퉜다");
            int soon = d.Highlights.Count;
            Pass(7); Argue("음악 소리로 다퉜다");
            int late = d.Highlights.Count;
            Check("하이라이트 문턱은 고정: 첫 말다툼(처음 보는 일)은 하이라이트 · 한 시간 뒤도 · 조용한 일곱 시간 뒤도 같은 크기의 말다툼은 아니다 (연출이 문턱을 낮추지 않는다)",
                first == 1 && soon == 1 && late == 1,
                $"첫 말다툼 뒤 {first}개 · 한 시간 뒤 {soon}개 · 조용한 일곱 시간 뒤 {late}개 (문턱 {Director.HighlightAt:0})");
        }
    }
}
