using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>물체 법칙 (docs/PHYSICS.md §10): 보는 쪽 한 벌 · (PH1b부터) 도트 꼴.</summary>
public static partial class Program
{
    private static void PropLaws()
    {
        if (!Do("물체")) return;
        Console.WriteLine("\n[물체 — 보는 쪽 · 도트 꼴 · 평면 움직임]");
        FacingLaw();
        FormLaw();
        PlaneLaws();
        WindLaws();
        FrameLaws();
        TrackLaws();
        PileLaws();
    }

    // 긴 방 (16m) 곁에 작은 방 — 가운데 겹을 끊으면 긴 방이 배 (몸 0 · 무거운 쪽) · 작은 방이 조각
    private const string BurnShip = @"
#######################
#................###..#
#................###..#
#................###..#
#######################";

    /// <summary>글자 그림의 x 줄 벽을 위아래 끝까지 끊어 몸을 가른다 (부스러기 없이 — 몸 · 물체만의 역학).</summary>
    private static (Sim s, Structure st) SplitWorld(string art, int cutX, ulong seed = 5)
    {
        var lay = Worlds.Ascii(art);
        var s = FieldStructure(lay, seed);
        var st = s.Get<Structure>();
        st.Debris = false;
        s.Run(3);
        for (int y = 0; y < 5; y++) st.Destroy(s, Worlds.At(lay, cutX, y), 0);
        s.Run(3);
        return (s, st);
    }

    /// <summary>몸 + 물체 + 부스러기 + 우주로 간 기체 − 점화 (운동량 · 원점에 대한 각운동량 — 닫힌 계면 늘 같다).</summary>
    private static (double px, double py, double l) WorldBook(Structure st)
    {
        var (bx, by, bl, _) = st.BodyMomentum(); var (px, py, pl) = st.PropMomentum(); var (sx, sy, sl) = st.ShardMomentum();
        return (bx + px + sx + st.GasPx - st.BurnPx, by + py + sy + st.GasPy - st.BurnPy, bl + pl + sl + st.GasL - st.BurnL);
    }

    /// <summary>
    /// 틱 안 기록 (PH5 — docs/PHYSICS.md §6-1 · §10 법칙 13 · 법칙 3의 때): 기록을 켜도 세계는 같다 (바이트 · 사실 흐름 — 켬 = 끔) ·
    /// 마찰로 서는 때를 사건 열쇠틀이 1e-6초로 안다 · 열쇠틀마다 운동 에너지가 줄기만 한다.
    /// </summary>
    private static void TrackLaws()
    {
        // 법칙 13: 기록이 세계를 바꾸지 않는다 — 부엌 외판 0.5m² 감압 (물건이 돌며 미끄러지고 · 막고 · 우주로) + 갈라진 두 방 (덜컹) 20틱 · 켬 = 끔 (찍은 바이트 · 사실 흐름 · 틱 안 때까지)
        {
            (byte[] bytes, string facts, int keys) Run(bool on)
            {
                var lay = Worlds.Ascii(BreachRooms, Worlds.Seats);
                var s = Worlds.Full(lay, 4);
                var st = s.Get<Structure>(); var f = s.Get<Field>();
                if (on) st.Track = new MotionTrack();
                foreach (var (name, x, y) in new[] { ("식량 상자", 1, 2), ("컵", 2, 1), ("공구 상자", 2, 3), ("수리 꾸러미", 1, 1) }) Worlds.PutItem(s, name, Worlds.At(lay, x, y));
                s.Run(2);
                st.DamageCause[Worlds.At(lay, 0, 2)] = s.Causes.Add(CauseKind.Setup, "외판에 파편이 박혔다", s.Tick); f.SetHole(Worlds.At(lay, 0, 2), 0.5f);
                int keys = 0;
                for (int k = 0; k < 20; k++) { s.Step(); keys += st.Track?.Keys.Count ?? 0; }
                return (s.Save(), string.Join(";", s.Facts.Since(0).Select(x => $"{x.Tick}:{x.Kind}:{x.Cell}:{x.Cause}:{x.Sub}")), keys);
            }
            var off = Run(false); var on = Run(true);
            Check("기록이 세계를 바꾸지 않는다 (법칙 13): 부엌 외판 0.5m² 감압 20틱 — 틱 안 기록을 켠 세계 = 끈 세계 (찍은 바이트 · 사실 흐름 · 틱 안 때까지) · 켠 세계는 열쇠틀을 남겼다",
                off.bytes.AsSpan().SequenceEqual(on.bytes) && off.facts == on.facts && on.keys > 0,
                $"바이트 같음 {off.bytes.AsSpan().SequenceEqual(on.bytes)} · 사실 같음 {off.facts == on.facts} · 열쇠틀 {on.keys}");
        }
        // 법칙 3의 때: 1g 마른 바닥 식량 상자 3m/s → 선 때 v/(μk g) = 1.0204082초 (기록의 사건 열쇠틀 1e-6) · 열쇠틀마다 운동 에너지가 줄기만 한다
        {
            var s = PropWorld(1);
            var st = s.Get<Structure>();
            st.Track = new MotionTrack();
            int id = Slide(s, "식량 상자", 4.5, 4.5, 3, 0);
            s.Step();
            var keys = st.Track.Of(MotionTrack.WhoProp, id).ToArray();
            var p = st.Props[st.IndexOf(id)];
            double stop = keys.Where(k => k.Ev == MotionTrack.EvStop).Select(k => (double)k.T).DefaultIfEmpty(-1).First();
            bool down = keys.Length > 2;
            for (int k = 1; k < keys.Length; k++) down &= keys[k].Vx * keys[k].Vx + keys[k].Vy * keys[k].Vy <= keys[k - 1].Vx * keys[k - 1].Vx + keys[k - 1].Vy * keys[k - 1].Vy + 1e-12;
            double want = 3 / (0.3 * 9.8);
            Check("마찰로 선 때 (법칙 3): 1g 마른 바닥 식량 상자 3m/s → 선 때 v/(μk g) = 1.0204초 — 기록의 사건 열쇠틀 (1e-6초) · 열쇠틀마다 운동 에너지가 줄기만 한다 · 16장 넘게",
                Math.Abs(stop - want) < 1e-6 && down && keys.Length >= 10 && keys[0].T == 0,
                $"선 때 {stop:0.0000000}초 (식 {want:0.0000000}) · 열쇠틀 {keys.Length} · 줄기만 {down}");
        }
    }

    /// <summary>
    /// 몸 위의 물체 (PH4d — docs/PHYSICS.md §3-2 '바닥 몸의 관성' · §10 법칙 6 · 7): 몸은 틱 안에서 고르게 가속하고 · 물체는 그 몸의 틀에서 −a · 원심 · 코리올리 · 오일러를 느낀다 —
    /// 마찰 없는 물체는 세상에 선다 (갈릴레이) · 덜컹은 바닥이 바뀐 것이라 세상 빠르기를 바꾸지 않는다 · 바닥 · 벽이 물체에 준 몫은 몸이 되받는다 (장부).
    /// </summary>
    private static void FrameLaws()
    {
        // 법칙 7 갈릴레이: 긴 방이 0.3g로 점화 (몸 0 · 무게중심을 민다) · 무중력 바닥 (마찰 없음)의 식량 상자 — 틱 처음 · 가운데 · 끝 모두 세상 자리 그대로 (≤ 1e-6m)
        {
            var (s, st) = SplitWorld(BurnShip, 18);
            var L = s.Layout;
            for (int c = 0; c < L.N; c++) if (st.BodyOf(c) == 0) s.Get<Field>().Grav[c] = 0;
            { var o = st.Bodies[0]; o.W = 0; st.Bodies[0] = o; }   // 차림: 갈라질 때 얻은 아주 작은 돎을 지운다 (틱 가운데 잣대를 곧은 식으로)
            int id = Slide(s, "식량 상자", 2.6, 3.5, 0, 0);
            double a = -0.3 * 9.8;   // 작은 방 (몸 1)에서 멀어지는 쪽으로
            st.ShipAx = a;
            var p0 = st.Props[st.IndexOf(id)];
            var (wx0, wy0, wvx, wvy, _) = st.PropWorld(p0);   // 세상에서 곧게 (몸이 갈라질 때 얻은 빠르기 그대로)
            var b0 = st.Bodies[0];
            st.Track = new MotionTrack();
            s.Step();
            var tr = st.Track; st.Track = null;
            // 틱 가운데: 물체 열쇠틀 가운데 1.2초에 가장 가까운 것 · 그 때 몸의 자세 (몸 열쇠틀 에르미트) → 세상 자리 / 몸도 그 때 x₀ + v₀t + ½at²
            double midT = 0, worstMid = double.MaxValue, worstBody = double.MaxValue;
            foreach (var k in tr.Of(MotionTrack.WhoProp, id)) if (Math.Abs(k.T - 1.2) < Math.Abs(midT - 1.2)) midT = k.T;
            var keys = tr.Of(MotionTrack.WhoProp, id).ToArray();
            if (keys.Length > 0 && tr.Pose(MotionTrack.WhoBody, 0, midT, out double bx, out double by, out double bth))
            {
                var km = keys.First(k => k.T == (float)midT);
                double c = Math.Cos(bth), sn = Math.Sin(bth), dx = km.X - b0.Lx, dy = km.Y - b0.Ly;
                worstMid = Math.Max(Math.Abs(bx + c * dx - sn * dy - (wx0 + wvx * midT)), Math.Abs(by + sn * dx + c * dy - (wy0 + wvy * midT)));
                double ax = a * Math.Cos(b0.Th), ay = a * Math.Sin(b0.Th);
                worstBody = Math.Max(Math.Abs(bx - (b0.X + b0.Vx * midT + 0.5 * ax * midT * midT)), Math.Abs(by - (b0.Y + b0.Vy * midT + 0.5 * ay * midT * midT)));
            }
            var p1 = st.Props[st.IndexOf(id)];
            var (wx1, wy1, _, _, _) = st.PropWorld(p1);
            double dtk = Time.SecondsPerTick;
            double worstEnd = Math.Max(Math.Abs(wx1 - wx0 - wvx * dtk), Math.Abs(wy1 - wy0 - wvy * dtk));
            double slid = p1.X - p0.X;   // 배 안에서
            Check("갈릴레이: 0.3g로 고르게 가속하는 배 (갈라진 몸 0) 안 무중력 바닥의 식량 상자는 세상에서 곧게 간다 — 틱 끝 · 틱 가운데 (기록의 열쇠틀 · 몸 자세 에르미트) 세상 자리 = 처음 + 처음 세상 빠르기 × t (≤ 1e-6m) · 몸은 틱 가운데에도 x₀ + v₀t + ½at² (반-차기면 ⅛a·dt² = 2.1m 어긋난다) · 상자는 배 안에서 ½a·dt² 뒤로",
                worstEnd <= 1e-6 && worstMid <= 1e-6 && worstBody <= 1e-6 && Math.Abs(midT - 1.2) < 0.1 && Math.Abs(slid - 0.5 * Math.Abs(a) * dtk * dtk) < 1e-6,
                $"틱 끝 어긋남 {worstEnd:0.0e0}m · 틱 가운데 ({midT:0.00}초) 상자 {(worstMid == double.MaxValue ? "기록 없음" : $"{worstMid:0.0e0}m")} · 몸 {(worstBody == double.MaxValue ? "기록 없음" : $"{worstBody:0.0e0}m")} · 배 안에서 {slid:0.000}m 뒤로");
        }
        // 법칙 12 점화 중 왕복: 몸이 없는 배가 0.4g로 점화하는 중 미끄러지는 식량 상자 — 찍고 되살려 10틱 바이트 같음 (⑤가 틱마다 먼저 가속을 알린다 — 찍지 않는 요청)
        {
            var lay = Worlds.Ascii(HelmShip, HelmLegend);
            var s = Worlds.Full(lay, 15);
            var o = s.Get<Outside>(); var st = s.Get<Structure>();
            o.DryKg = o.PropellantKg = o.Engines * o.EngineN / (0.4 * Outside.G0) / 2;
            int id = Worlds.PutItem(s, "식량 상자", Worlds.At(lay, 12, 7));
            s.Run(2);
            o.Burn(s, BurnKind.Correct, 1, 0, 100, 0, 30, 0, false, o.Engines, s.Causes.Add(CauseKind.Route, "항로 고침", s.Tick));
            s.Run(1);   // 상자가 미끄러지기 시작했다 (찍는 때 — 아직 벽에 닿지 않았다)
            var r = new Sim(s.Setup); r.Load(s.Save());
            double at = st.Props[st.IndexOf(id)].X;
            s.Run(10); r.Run(10);
            bool same = s.Save().AsSpan().SequenceEqual(r.Save());
            int i = st.IndexOf(id);
            double moved = i >= 0 ? Worlds.At(lay, 12, 7) % lay.W + 0.5 - st.Props[i].X : 0;
            Check("점화 중 왕복: 몸이 없는 배가 0.4g로 점화하는 중 미끄러지는 식량 상자 — 찍고 되살려 10틱 바이트 같음 (배의 가속은 ⑤가 틱마다 먼저 알리는 요청)",
                same && moved > 0.3 && at > st.Props[i].X + 0.3, $"바이트 같음 {same} · 상자 {moved:0.00}m 뒤로 (찍은 때 뒤로 {at - st.Props[i].X:0.00}m)");
        }
        // 법칙 6 운동량 장부: 닫힌 두 방 — 몸 0은 0.3g 점화 (마른 바닥 식량 상자는 잠든 채 실려 가고 · 젖은 바닥 컵은 미끄러져 벽에 눌린다) · 몸 1은 3m/s로 밀려 (덜컹) 식량 상자가 미끄러져 벽에 부딪힌다 —
        // 몸 + 물체 + 기체 = 점화 (운동량 · 원점에 대한 각운동량 1e-9)
        {
            var (s, st) = SplitWorld(Sealed, 6);
            var f = s.Get<Field>();
            int left = st.BodyOf(s.Layout.Index(3, 3)), right = st.BodyOf(s.Layout.Index(10, 3));
            int cup = Slide(s, "컵", 3.4, 2.5, 0, 0);
            f.AddStore(s.Layout.Index(3, 2), f.T.sWater, 300);
            int boxL = Slide(s, "식량 상자", 3.5, 3.6, 0, 0), boxR = Slide(s, "식량 상자", 9.9, 3.4, 0, 0);
            st.ShipAx = -0.3 * 9.8;   // 몸 1에서 멀어지는 쪽 (부딪지 않게)
            var e0 = WorldBook(st);
            var o = st.Bodies[right];
            double jx = o.Mass * 3, jy = o.Mass * 0.4;
            st.Push(right, o.X + 0.3, o.Y - 0.2, jx, jy);
            var e1 = WorldBook(st);
            double pushL = (o.X + 0.3) * jy - (o.Y - 0.2) * jx;
            double worstP = Math.Sqrt((e1.px - e0.px - jx) * (e1.px - e0.px - jx) + (e1.py - e0.py - jy) * (e1.py - e0.py - jy)), worstL = Math.Abs(e1.l - e0.l - pushL);
            double scale = 0;
            int bounced = 0;
            for (int k = 0; k < 10; k++)
            {
                long b0 = st.Bounces;
                s.Step();
                if (st.Bounces > b0) bounced++;
                var e = WorldBook(st);
                double ex = e.px - e0.px - jx, ey = e.py - e0.py - jy;
                worstP = Math.Max(worstP, Math.Sqrt(ex * ex + ey * ey)); worstL = Math.Max(worstL, Math.Abs(e.l - e0.l - pushL));
                scale = Math.Max(scale, Math.Sqrt(st.BurnPx * st.BurnPx + st.BurnPy * st.BurnPy) + Math.Sqrt(jx * jx + jy * jy));
            }
            var pc = st.Props[st.IndexOf(cup)]; var pl = st.Props[st.IndexOf(boxL)]; var pr = st.Props[st.IndexOf(boxR)];
            Check("운동량 장부 (물체): 닫힌 두 방 — 0.3g 점화하는 몸 0 (잠든 식량 상자는 실려 가고 · 젖은 바닥 컵은 미끄러져 벽에 눌린다) · 3m/s로 밀린 몸 1의 식량 상자가 미끄러져 벽에 부딪힌다 — 몸 + 물체 + 기체 = 점화 + 민 것 (운동량 · 각운동량 1e-9)",
                left != right && scale > 0 && worstP <= 1e-9 * scale && worstL <= 1e-9 * scale * 20 && pl.State == 0 && pc.X > 3.7 && bounced > 0,
                $"몸 {left}/{right} · 점화 {Math.Sqrt(st.BurnPx * st.BurnPx + st.BurnPy * st.BurnPy) / 1000:0.0}kN·s · 남는 운동량 {worstP:0.0e0} · 각운동량 {worstL:0.0e0} · 컵 x {pc.X:0.000} · 몸 0 상자 {(pl.State == 0 ? "잠" : "깸")} · 몸 1 상자 x {pr.X:0.000} (튐 {bounced}틱)");
        }
        // 법칙 6 운동량 장부 — 틱 사이 덜컹에 잠든 채 실려 가는 것 (검토 고침): 갈라진 두 방의 몸 1을 0.02m/s로 비스듬히 민다 (식량 상자는 깨지 않는다) ·
        // 같은 배를 잠든 식량 상자 둘과 함께 가른다 (조각 빠르기 하한 0.02m/s) — 몸 + 물체 = 민 것 (밀자마자 · 3~4틱 뒤까지 1e-9 · 실려 간 몫까지 몸이 함께 받는다)
        {
            var (s, st) = SplitWorld(Sealed, 6);
            int right = st.BodyOf(s.Layout.Index(10, 3));
            int box = Slide(s, "식량 상자", 9.9, 3.4, 0, 0);
            var e0 = WorldBook(st);
            var o = st.Bodies[right];
            double jx = o.Mass * 0.02, jy = o.Mass * 0.003, pushL = (o.X + 0.3) * jy - (o.Y - 0.2) * jx, scaleA = Math.Sqrt(jx * jx + jy * jy);
            st.Push(right, o.X + 0.3, o.Y - 0.2, jx, jy);
            double worstA = 0, worstAL = 0;
            for (int k = 0; k <= 3; k++)
            {
                if (k > 0) s.Step();
                var e = WorldBook(st);
                double ex = e.px - e0.px - jx, ey = e.py - e0.py - jy;
                worstA = Math.Max(worstA, Math.Sqrt(ex * ex + ey * ey)); worstAL = Math.Max(worstAL, Math.Abs(e.l - e0.l - pushL));
            }
            bool slept = st.Props[st.IndexOf(box)].State == 0;
            // 가르기: 물건을 먼저 두고 가운데 겹을 끊는다 — 닫힌 계라 장부는 처음 그대로
            var lay = Worlds.Ascii(Sealed);
            var s2 = FieldStructure(lay, 5);
            var st2 = s2.Get<Structure>();
            st2.Debris = false;
            s2.Run(3);
            int boxL = Slide(s2, "식량 상자", 3.5, 3.6, 0, 0), boxR = Slide(s2, "식량 상자", 9.9, 3.4, 0, 0);
            var g0 = WorldBook(st2);
            for (int y = 0; y < 5; y++) st2.Destroy(s2, Worlds.At(lay, 6, y), 0);
            double scaleB = 0, worstB = 0, worstBL = 0;
            for (int k = 0; k <= 4; k++)
            {
                if (k > 0) s2.Step();
                double sb = 0;
                foreach (var b in st2.Bodies) if (b.Mass > 0) sb += b.Mass * Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy);
                scaleB = Math.Max(scaleB, sb);
                var e = WorldBook(st2);
                worstB = Math.Max(worstB, Math.Sqrt((e.px - g0.px) * (e.px - g0.px) + (e.py - g0.py) * (e.py - g0.py))); worstBL = Math.Max(worstBL, Math.Abs(e.l - g0.l));
            }
            bool slept2 = st2.Props[st2.IndexOf(boxL)].State == 0 && st2.Props[st2.IndexOf(boxR)].State == 0;
            Check("운동량 장부 (틱 사이 덜컹): 갈라진 몸 1을 0.02m/s로 비스듬히 밀어도 · 잠든 식량 상자 둘을 둔 배를 갈라도 상자는 잠든 채 실려 가고 몸 + 물체 = 민 것 (밀자마자 · 몇 틱 뒤까지 운동량 1e-9 · 각운동량 2e-8 — 실려 간 몫까지 몸이 함께 받는다)",
                slept && slept2 && st2.Bodies.Count >= 2 && scaleB > 0 && worstA <= 1e-9 * scaleA && worstAL <= 1e-9 * scaleA * 20 && worstB <= 1e-9 * scaleB && worstBL <= 1e-9 * scaleB * 20,
                $"민 몸: 남는 운동량 {worstA:0.0e0}N·s · 각운동량 {worstAL:0.0e0} (민 것 {scaleA:0}N·s · 상자 {(slept ? "잠" : "깸")}) · 가른 배: {worstB:0.0e0}N·s · {worstBL:0.0e0} (조각 {scaleB:0}N·s · 상자 {(slept2 ? "잠" : "깸")})");
        }
    }

    /// <summary>
    /// 보는 쪽 한 벌 (PH1a): 가구가 보는 쪽은 도면에서 한 번 (`Layout.Facing`) — 그림이 셈하던 때 (Painter `FacingOfBox`)와 일곱 척 가구마다 같다.
    /// 값은 옮기기 전 코드로 잰 S/N/E/W 문자열의 FNV-1a 해시.
    /// </summary>
    private static void FacingLaw()
    {
        var was = new Dictionary<string, ulong>
        {
            ["didim"] = 0xeedd48741bc2bb93UL, ["hannuri"] = 0x7eaa18f4db702dffUL, ["hwasal"] = 0x7957fe64ce798e38UL, ["nareum"] = 0x29289543d328937bUL,
            ["saetteul"] = 0x13d64cffb51b55c6UL, ["salpim"] = 0x7f4072b4d2ba2241UL, ["soedol"] = 0x7647af24efa023eeUL,
        };
        var bad = new List<string>(); var got = new List<string>();
        foreach (var key in Layout.ShipKeys().OrderBy(k => k, StringComparer.Ordinal))
        {
            var L = Layout.Ship(key, Tables.Default);
            ulong h = 1469598103934665603UL;
            foreach (var f in L.Facing) { h ^= "SNEW"[(int)f]; h *= 1099511628211UL; }
            got.Add(key);
            if (L.Facing.Length != L.Furniture.Count || !was.TryGetValue(key, out var w) || w != h) bad.Add($"{key} {h:x16}");
        }
        Check("보는 쪽 한 벌 — 도면에서 한 번 셈한 가구의 보는 쪽이 그림이 셈하던 것과 일곱 척 모두 같다", bad.Count == 0 && got.Count == 7,
            bad.Count == 0 ? $"{got.Count}척 같음" : "다른 배: " + string.Join(" · ", bad));
    }

    /// <summary>
    /// 도트 꼴 (PH1b — docs/PHYSICS.md §10 법칙 1): 질량 = 표 kg (설비 105 · 물건 전부 · 누운 꼴 넷) · 대칭 꼴의 무게중심이 가운데 · 상자 회전관성 m(w² + d²)/12 ·
    /// 누우면 높이가 옆으로 간다 · 일곱 척 가구의 발자국이 칸 안 · 앞면 눈금 (표 h × 16 = 그림 앞면) · 위에서 본 발자국 = 그림의 불투명 윗면 ±2도트.
    /// </summary>
    private static void FormLaw()
    {
        var t = Tables.Default;
        const double d = DotForm.Dot;
        static bool Near(double a, double b) => Math.Abs(a - b) <= 1e-9 * Math.Max(1, Math.Abs(b));
        var ships = Layout.ShipKeys().OrderBy(k => k, StringComparer.Ordinal).Select(k => Layout.Ship(k, t)).ToList();
        // 배에 놓인 가구마다 (종류 · 칸 상자 · 보는 쪽 — 같은 것은 한 번)
        var placed = new SortedSet<(string type, int bw, int bh, Facing f)>();
        foreach (var L in ships)
            for (int i = 0; i < L.Furniture.Count; i++)
            {
                var c = L.Furniture[i].Cells; if (c.Length == 0) continue;
                placed.Add((L.Furniture[i].Type, c.Max(L.X) - c.Min(L.X) + 1, c.Max(L.Y) - c.Min(L.Y) + 1, L.Facing[i]));
            }
        // ① 질량
        var bad = new List<string>(); int nEquip = 0, nItem = 0;
        foreach (var (type, e) in t.Equipment)
        {
            if (type == "_default") continue;
            nEquip++;
            var p = placed.FirstOrDefault(x => x.type == type);
            var fm = p.type == null ? DotForge.Make(t, type) : DotForge.Make(t, type, p.f, p.bw, p.bh);
            double kg = e.Parts.Values.Sum();
            if (!Near(fm.Mass, kg) || !Near(fm.Kg.Sum(), kg)) bad.Add($"{type} {fm.Mass:0.###}/{kg:0.###}");
        }
        var items = t.People.Items.Keys.Concat(t.Forms.Where(kv => kv.Value.Kg > 0 && !t.Equipment.ContainsKey(kv.Key) && !t.People.Items.ContainsKey(kv.Key)).Select(kv => kv.Key)).ToList();
        foreach (var name in items)
        {
            nItem++;
            double kg = t.People.Items.TryGetValue(name, out var it) ? it.Kg : t.Forms[name].Kg;
            for (int lie = 0; lie <= 4; lie++)
            {
                var fm = DotForge.Make(t, name, lie: lie);
                if (!Near(fm.Mass, kg) || !Near(fm.Kg.Sum(), kg) || fm.Columns == 0) { bad.Add($"{name}/{lie} {fm.Mass:0.###}/{kg:0.###}"); break; }
            }
        }
        Check("도트 꼴 — 질량 = 표 kg (설비 · 물건 · 누운 꼴 넷 — 기둥 합도 · 1e-9)", bad.Count == 0 && nEquip == 105 && nItem > 30,
            bad.Count == 0 ? $"설비 {nEquip} · 물건 {nItem} × 다섯 자세" : string.Join(" · ", bad.Take(6)) + (bad.Count > 6 ? $" … ({bad.Count})" : ""));
        // ② 대칭 꼴 무게중심 · 상자 회전관성
        var sym = new[] { "소화기", "식량 상자", "컵", "접시" }.Select(n => DotForge.Make(t, n)).ToArray();
        var off = sym.Where(f => Math.Abs(f.Cx - f.W * d / 2) > 1e-12 || Math.Abs(f.Cy - f.D * d / 2) > 1e-12).Select(f => $"{f.Name} ({f.Cx / d:0.##}, {f.Cy / d:0.##})/({f.W / 2.0}, {f.D / 2.0})").ToList();
        Check("도트 꼴 — 대칭 꼴의 무게중심이 발자국 가운데 (소화기 · 식량 상자 · 컵 · 접시)", off.Count == 0 && sym.All(f => f.Columns > 0), off.Count == 0 ? "넷 모두 가운데" : string.Join(" · ", off));
        var box = DotForge.Make(t, "식량 상자");
        double w = box.W * d, dd = box.D * d, ib = box.Mass * (w * w + dd * dd) / 12;
        Check("도트 꼴 — 상자 회전관성 = m(w² + d²)/12 ± 2% (식량 상자)", box.Mass > 0 && Math.Abs(box.I - ib) <= 0.02 * ib,
            $"{box.W}×{box.D}도트 · {box.Mass:0.#}kg · I {box.I:0.0000} / {ib:0.0000}kg·m²");
        // 넘어짐 여유 · 마찰 반지름 · 바람 넓이 (상자 옆 = 깊이 × 높이 · 앞 = 가로 × 높이 · 대각은 그 사이 · 대칭이면 압력 중심이 가운데) · 원판 ρ = 2R/3
        double hgt = box.H * DotForm.Layer, ext = DotForge.Make(t, "소화기").Rho, r3 = 2 * (5 * d / 2) / 3;
        bool tip = Math.Abs(box.TipB[0] - w / 2) < 1e-12 && Math.Abs(box.TipB[1] - dd / 2) < 1e-12 && Math.Abs(box.TipB[2] - w / 2) < 1e-12 && Math.Abs(box.TipB[3] - dd / 2) < 1e-12;
        bool wind = Math.Abs(box.Area16[0] - dd * hgt) < 1e-12 && Math.Abs(box.Area16[4] - w * hgt) < 1e-12 && Math.Abs(box.Area16[8] - dd * hgt) < 1e-12
            && box.Area16[2] > box.Area16[4] && box.Area16[2] <= (w + dd) / Math.Sqrt(2) * hgt + 1e-12 && box.Cop16.All(c => Math.Abs(c) < 1e-9);
        Check("도트 꼴 — 넘어짐 여유 · 바람 넓이 · 마찰 반지름 (상자 b = 반 너비 · 옆 · 앞 · 대각 넓이 · 압력 중심 0 · 소화기 ρ = 2R/3 ± 5%)", tip && wind && Math.Abs(ext - r3) <= 0.05 * r3,
            $"b {box.TipB[0] / d:0.#}·{box.TipB[1] / d:0.#}도트 · 넓이 {box.Area16[0]:0.0000}·{box.Area16[2]:0.0000}·{box.Area16[4]:0.0000}m² · ρ {ext / d:0.000}/{r3 / d:0.000}도트");
        // ③ 누운 꼴: 높이가 누운 쪽으로 간다 (+x로 누우면 무게중심 x = 선 꼴의 높이 · 높이 = 반대쪽 끝까지 — 한 층 안)
        var lies = new List<string>();
        foreach (var n in new[] { "소화기", "식량 상자", "우주복", "유리병", "공구 상자" })
        {
            var up = DotForge.Make(t, n);
            for (int lie = 1; lie <= 4; lie++)
            {
                var lf = DotForge.Make(t, n, lie: lie);
                // 누운 쪽 축 · 그 쪽 무게중심 (꼴 끝에서)
                double along = lie switch { 1 => lf.Cx, 2 => lf.Cy, 3 => lf.W * d - lf.Cx, _ => lf.D * d - lf.Cy };
                double rest = lie switch { 1 => up.W * d - up.Cx, 2 => up.D * d - up.Cy, 3 => up.Cx, _ => up.Cy };
                if (lf.Columns == 0 || up.Hc <= 0 || Math.Abs(along - up.Hc) > d || Math.Abs(lf.Hc - rest) > DotForm.Layer || lf.Lie != lie) lies.Add($"{n}/{lie} {along:0.000}~{up.Hc:0.000} · {lf.Hc:0.000}~{rest:0.000}");
            }
        }
        Check("도트 꼴 — 누운 꼴: 선 꼴의 높이가 누운 쪽으로 · 높이는 닿은 면에서 (한 도트 · 한 층 안 · 다섯 물건 × 네 쪽)", lies.Count == 0, lies.Count == 0 ? "20꼴 맞음" : string.Join(" · ", lies.Take(4)));
        // ④ 일곱 척 가구: 발자국이 칸 안 · 앞면 눈금 · 위에서 본 발자국 = 그림 윗면 ±2도트
        var outBox = new List<string>(); var scale = new List<string>(); var foot = new List<string>(); int nArt = 0;
        foreach (var (type, bw, bh, f) in placed)
        {
            var fm = DotForge.Make(t, type, f, bw, bh);
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int y = 0; y < fm.D; y++) for (int x = 0; x < fm.W; x++) if (fm.Solid(x, y)) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
            if (x1 < 0 || fm.Ox + x0 < 0 || fm.Oy + y0 < 0 || fm.Ox + x1 >= bw * 32 || fm.Oy + y1 >= bh * 32) { outBox.Add($"{type} {bw}×{bh} {f}"); continue; }
            if (!ArtFurniture.Has(type)) continue;
            nArt++;
            var (front, _) = ArtFurniture.Size(type, bw, bh, f);
            if (Math.Abs(t.Equip(type).H * 16 - front) > 1e-9 || fm.H != front) scale.Add($"{type} {t.Equip(type).H * 16:0.##}·{fm.H}/{front}");
            // 그림의 윗면 띠: E · W 그림은 옆모습이라 칸 상자를 돌린 남쪽 그림과 견준다
            bool side = f is Facing.E or Facing.W;
            int aw = side ? bh : bw, ah = side ? bw : bh;
            var img = ArtFurniture.Body(type, aw, ah, side ? Facing.S : f, default, 0);
            var (fr, tp) = ArtFurniture.Size(type, aw, ah, side ? Facing.S : f);
            int ty = img.H - fr - tp, ax0 = int.MaxValue, ax1 = -1, rows = 0;
            for (int y = ty; y < img.H - fr; y++)
            {
                bool any = false;
                for (int x = 0; x < img.W; x++) if (img.Get(x, y).A > 0) { any = true; ax0 = Math.Min(ax0, x); ax1 = Math.Max(ax1, x); }
                if (any) rows++;
            }
            int fw = side ? y1 - y0 + 1 : x1 - x0 + 1, fd = side ? x1 - x0 + 1 : y1 - y0 + 1;
            if (Math.Abs(fw - (ax1 - ax0 + 1)) > 2 || Math.Abs(fd - rows) > 2) foot.Add($"{type} {bw}×{bh} {f} {fw}×{fd}/{ax1 - ax0 + 1}×{rows}");
        }
        Check("도트 꼴 — 일곱 척 가구의 발자국이 칸 상자 안", outBox.Count == 0 && placed.Count > 100, outBox.Count == 0 ? $"{placed.Count}가지 (종류 · 상자 · 보는 쪽)" : string.Join(" · ", outBox.Take(6)) + $" ({outBox.Count})");
        Check("도트 꼴 — 앞면 눈금: 표 높이 h × 16 = 그림 앞면 도트 = 꼴 층 수 (새 그림 가구)", scale.Count == 0 && nArt > 20, scale.Count == 0 ? $"{nArt}가지" : string.Join(" · ", scale.Distinct().Take(6)));
        Check("도트 꼴 — 위에서 본 발자국 = 그림의 불투명 윗면 ±2도트 (일곱 척 새 그림 가구)", foot.Count == 0 && nArt > 20, foot.Count == 0 ? $"{nArt}가지" : string.Join(" · ", foot.Take(12)) + $" ({foot.Count})");
    }

    // 공기 없는 10×6 방 (글자 그림 둘레 한 줄을 덧대어 안쪽은 도면 x 2~12 · y 2~8 m)
    private const string PropRoom = "\n############\n#..........#\n#..........#\n#..........#\n#..........#\n#..........#\n#..........#\n############";

    /// <summary>힘이 없는 작은 세계 (장 + 구조 · 공기 없음 · 마음 없음): 바닥 중력 g를 고르게.</summary>
    private static Sim PropWorld(float g, ulong seed = 1, string art = PropRoom, Dictionary<char, string>? legend = null)
    {
        var lay = Worlds.Ascii(art, legend);
        var s = new Sim(new SimSetup(lay, seed, sim => { var f = new Field(sim.Layout, sim.Tables); sim.Add(f); sim.Add(new Structure(f)); }));
        Array.Fill(s.Get<Field>().Grav, g);
        return s;
    }

    /// <summary>시험 차림: 물건을 놓고 (x, y)에 둔다 · 밀어 빠르기 (vx, vy) · 각속도 w를 준다 (충격량 = 질량 × 빠르기).</summary>
    private static int Slide(Sim s, string name, double x, double y, double vx, double vy, double w = 0)
    {
        var st = s.Get<Structure>();
        int item = s.Tables.People.Items.Keys.ToList().IndexOf(name);
        int id = st.PutItem(item, 0, s.Layout.Index((int)x, (int)y));
        int i = st.IndexOf(id); var p = st.Props[i]; p.X = x; p.Y = y; st.Props[i] = p;
        var fm = st.PropForm(p);
        if (vx != 0 || vy != 0 || w != 0) st.Shove(id, fm.Mass * vx, fm.Mass * vy, fm.I * w);
        return id;
    }

    /// <summary>방 안쪽 (도면 x 2~12 · y 2~8 m) 밖으로 나간 껍질 꼭짓점의 가장 깊은 겹침 (m).</summary>
    private static double WallOverlap(Structure st, in Prop p)
    {
        var fm = st.PropForm(p);
        double c = Math.Cos(p.Th), sn = Math.Sin(p.Th), worst = 0;
        foreach (var (hx, hy) in fm.Hull)
        {
            double rx = hx - fm.Cx, ry = hy - fm.Cy, x = p.X + c * rx - sn * ry, y = p.Y + sn * rx + c * ry;
            worst = Math.Max(worst, Math.Max(Math.Max(2 - x, x - 12), Math.Max(2 - y, y - 8)));
        }
        return worst;
    }

    /// <summary>
    /// 평면 움직임 (PH4a — docs/PHYSICS.md §10 법칙 2 · 3 · 14 · 12 · 벽): 힘이 없으면 그대로 · 마찰로 선다 (정확한 걸음) · 잠 · 미끄러지는 중 왕복 · 1코어 = 4코어 ·
    /// 빠른 상자도 벽을 건너뛰지 않는다 (매 틱 끝 겹침 ≤ 0.5도트 · 반발 e).
    /// </summary>
    private static void PlaneLaws()
    {
        var t = Tables.Default;
        const double g = 9.8, dot = DotForm.Dot;
        var box = DotForge.Make(t, "식량 상자");
        // 법칙 2: 힘이 없으면 그대로 — 무중력 · 공기 없음 · 50틱 뒤 x₀ + vt · θ = ωt · 운동 에너지 비트까지
        {
            var s = PropWorld(0);
            var st = s.Get<Structure>();
            int id = Slide(s, "식량 상자", 3.0, 2.8, 0.05, 0.02, 0.5);
            var p0 = st.Props[st.IndexOf(id)];
            double ke0 = 0.5 * p0.Kg * (p0.Vx * p0.Vx + p0.Vy * p0.Vy) + 0.5 * p0.Inertia * p0.W * p0.W;
            s.Run(50);
            var p = st.Props[st.IndexOf(id)];
            double time = 50 * Time.SecondsPerTick, ke = 0.5 * p.Kg * (p.Vx * p.Vx + p.Vy * p.Vy) + 0.5 * p.Inertia * p.W * p.W;
            double ex = Math.Abs(p.X - (p0.X + p0.Vx * time)), ey = Math.Abs(p.Y - (p0.Y + p0.Vy * time)), eth = Math.Abs(p.Th - (p0.Th + p0.W * time));
            Check("평면 움직임 — 힘이 없으면 그대로: 무중력 · 공기 없는 방의 식량 상자 (0.05, 0.02)m/s · 0.5rad/s → 50틱 뒤 x₀ + vt · θ = ωt (1e-9) · 운동 에너지 비트까지",
                ex <= 1e-9 && ey <= 1e-9 && eth <= 1e-9 && BitConverter.DoubleToInt64Bits(ke) == BitConverter.DoubleToInt64Bits(ke0) && p.State == 1,
                $"x 어긋남 {ex:0.0e0} · y {ey:0.0e0} · θ {eth:0.0e0} ({p.Th:0.000}rad) · 운동 에너지 {ke0:R} → {ke:R} · 상태 {p.State}");
        }
        // 법칙 3: 마찰로 선다 — 1g 마른 강철 바닥 · 식량 상자 (고분자 μk 0.3): 3m/s → v²/2μg = 1.5306m · 돌기만 3rad/s → Δθ = Iω²/(2 μk m g ρ)
        {
            var s = PropWorld(1);
            var st = s.Get<Structure>();
            int a = Slide(s, "식량 상자", 3.0, 3.0, 3, 0), b = Slide(s, "식량 상자", 6.0, 6.0, 0, 0, 3);
            var a0 = st.Props[st.IndexOf(a)]; var b0 = st.Props[st.IndexOf(b)];
            s.Run(1);
            var pa = st.Props[st.IndexOf(a)]; var pb = st.Props[st.IndexOf(b)];
            double want = 9 / (2 * 0.3 * g), turn = b0.Inertia * 9 / (2 * 0.3 * b0.Kg * g * box.Rho);
            double da = pa.X - a0.X, db = pb.Th - b0.Th;
            Check("평면 움직임 — 마찰로 선다: 1g 마른 바닥 · 식량 상자 3m/s → 1.5306m (1e-6) 뒤 멈춤 · 돌기만 3rad/s → Δθ = Iω²/(2μk m g ρ) (1e-6) · 자리는 그대로",
                Math.Abs(da - want) <= 1e-6 && pa.Vx == 0 && pa.Vy == 0 && Math.Abs(pa.Y - a0.Y) <= 1e-12 && Math.Abs(db - turn) <= 1e-6 && pb.W == 0 && Math.Abs(pb.X - b0.X) + Math.Abs(pb.Y - b0.Y) <= 1e-12,
                $"미끄러짐 {da:0.0000000}/{want:0.0000000}m · v ({pa.Vx:0.###}, {pa.Vy:0.###}) · 돎 {db:0.0000000}/{turn:0.0000000}rad · ω {pb.W:0.###} (ρ {box.Rho:0.0000}m)");
        }
        // 법칙 14: 잠 — 깨어 있는 것 하나면 첫 틱 잔걸음 > 0 · 선 뒤 잠들면 100틱 동안 잔걸음이 늘지 않는다 · 섬 놓침 0
        {
            var s = PropWorld(1);
            var st = s.Get<Structure>();
            int id = Slide(s, "식량 상자", 4.0, 4.0, 1, 0.5, 1);
            s.Run(1);
            long steps1 = st.PropSteps;
            var p = st.Props[st.IndexOf(id)];
            s.Run(100);
            Check("평면 움직임 — 잠: 깨어 있는 상자 하나면 첫 틱 잔걸음 > 0 · 선 뒤 0.5초면 잠든다 (빠르기 0 · 찍힌 잠) · 100틱 동안 잔걸음이 늘지 않는다 · 섬 놓침 0",
                steps1 > 0 && p.Asleep == 1 && p.State == 0 && p.Vx == 0 && p.Vy == 0 && p.W == 0 && st.PropSteps == steps1 && st.IslandMiss == 0,
                $"첫 틱 잔걸음 {steps1} · 잠 {p.Asleep} · 상태 {p.State} · 100틱 뒤 잔걸음 {st.PropSteps} · 섬 놓침 {st.IslandMiss}");
        }
        // 법칙 12: 결정론 — 미끄러지는 상자 20개 (0.1g · 벽에 튀며)를 찍고 되살려 60틱 바이트 같음 · 1코어 = 4코어
        {
            (ulong hash, bool same, int awake) Run(int threads)
            {
                int keep = Par.Threads; Par.Threads = threads;
                var s = PropWorld(0.1f, 9);
                var st = s.Get<Structure>();
                var rng = new Random(5);
                string[] names = { "식량 상자", "소화기", "공구 상자", "수리 꾸러미", "책" };
                for (int k = 0; k < 20; k++)
                {
                    double a = rng.NextDouble() * 2 * Math.PI, v = 1 + 5 * rng.NextDouble();
                    Slide(s, names[k % names.Length], 2.5 + k % 5 * 2, 2.5 + k / 5 * 1.5, v * Math.Cos(a), v * Math.Sin(a), 6 * rng.NextDouble() - 3);
                }
                s.Run(1);
                var r = new Sim(s.Setup); r.Load(s.Save());
                s.Run(60); r.Run(60);
                bool same = s.Save().AsSpan().SequenceEqual(r.Save());
                int awake = 0; foreach (var p in st.Props) if (Structure.Awake(p)) awake++;
                Par.Threads = keep;
                return (s.Hash(), same, awake);
            }
            var one = Run(1); var four = Run(4);
            Check("평면 움직임 — 결정론: 미끄러지며 벽에 튀는 상자 20개를 1틱 뒤 찍고 되살려 60틱 — 바이트 같음 · 1코어 = 4코어",
                one.same && four.same && one.hash == four.hash && one.hash != 0, $"되살림 같음 {one.same}/{four.same} · {one.hash:x16} / {four.hash:x16} · 61틱 뒤 깨어 있음 {one.awake}");
        }
        // 닫힌 문은 벽 · 제자리 가구는 기둥: 1g 상자 3 · 4m/s가 닫힌 문 · 탁자 앞에서 튀어 선다 (문 칸 · 탁자 발자국 안으로 들어가지 않는다)
        {
            const string art = "\n############\n#.......tt.#\n#.......tt.#\n#..........#\n#####D######\n#..........#\n############";
            var s = PropWorld(1, 1, art, new Dictionary<char, string> { ['t'] = "Table" });
            var st = s.Get<Structure>(); var L = s.Layout;
            var table = L.Furniture.Single(fu => fu.Type == "Table");
            var tf = DotForge.Make(t, "Table", L.Facing[table.Id], 2, 2);
            int lx = int.MaxValue; for (int y = 0; y < tf.D; y++) for (int x = 0; x < tf.W; x++) if (tf.Solid(x, y)) lx = Math.Min(lx, x);
            double tableLeft = (table.Cells.Min(L.X) * 32 + tf.Ox + lx) * dot;   // 탁자 발자국 왼쪽 끝 (m)
            int a = Slide(s, "식량 상자", 6.5, 3.5, 0, 3), b = Slide(s, "식량 상자", 6.5, 2.9, 4, 0);
            s.Run(2);
            var pa = st.Props[st.IndexOf(a)]; var pb = st.Props[st.IndexOf(b)];
            double south = pa.Y + box.Hull.Max(v => v.y) - box.Cy, east = pb.X + box.Hull.Max(v => v.x) - box.Cx;
            Check("평면 움직임 — 닫힌 문은 벽 · 제자리 가구는 기둥: 3 · 4m/s 상자가 닫힌 문 앞 · 탁자 발자국 앞에서 튀어 선다 (안으로 0.5도트 넘게 들어가지 않는다 · 닿을 만큼 갔다)",
                south <= 5 + 0.5 * dot && south > 4.85 && east <= tableLeft + 0.5 * dot && east > tableLeft - 0.15 && pa.Asleep == 1 && pb.Asleep == 1 && st.Bounces >= 2,
                $"문 앞 상자 남쪽 끝 {south:0.000}/5 · 탁자 앞 상자 동쪽 끝 {east:0.000}/{tableLeft:0.000}m · 튐 {st.Bounces} · 잠 {pa.Asleep}{pb.Asleep}");
        }
        // 벽을 건너뛰지 않는다: 20m/s 상자 — 무중력 하나 (벽에 튀어 e = min(고분자 0.5, 강철 0.4)씩 줄어든다) · 1g 하나 (튀고 미끄러져 벽에 선다) — 매 틱 끝 겹침 ≤ 0.5도트
        {
            var s = PropWorld(0);
            var st = s.Get<Structure>();
            var f = s.Get<Field>();
            for (int c = 0; c < s.Layout.N; c++) if (s.Layout.Y(c) >= 5) f.Grav[c] = 1;   // 아래 반은 1g
            int a = Slide(s, "식량 상자", 10.5, 3.5, 20, 0), b = Slide(s, "식량 상자", 9.5, 6.5, 20, 0), cc = Slide(s, "식량 상자", 11.69, 7.2, 0.9, 0);   // 셋째: 0.46m/s로 닿아 (튀지 않고) 벽에 붙어 선다
            double worst = 0; long bounces1 = 0; Prop pa1 = default;
            for (int k = 0; k < 4; k++)
            {
                s.Step();
                foreach (int id in new[] { a, b, cc }) worst = Math.Max(worst, WallOverlap(st, st.Props[st.IndexOf(id)]));
                if (k == 0) { pa1 = st.Props[st.IndexOf(a)]; bounces1 = st.Bounces; }
            }
            var pb = st.Props[st.IndexOf(b)]; var pc = st.Props[st.IndexOf(cc)];
            double gap = 12 - (pc.X + box.Hull.Max(v => v.x) - box.Cx);
            double e = 0.4, want = 20 * Math.Pow(e, 2);
            Check("평면 움직임 — 벽을 건너뛰지 않는다: 20m/s 상자 둘 (무중력 · 1g) — 매 틱 끝 벽 겹침 ≤ 0.5도트 · 무중력 상자는 두 번 튀어 20 × 0.4² = 3.2m/s (±2%) · 1g 상자는 방 안에 선다 · 느리게 닿은 상자는 벽에 붙어 잔다 (틈 ≤ 1도트)",
                worst <= 0.5 * dot && Math.Abs(pa1.Vx - want) <= 0.02 * want && Math.Abs(pa1.Vy) < 1e-3 && Math.Abs(pa1.W) < 1e-3 && bounces1 >= 2 && pb.Vx == 0 && pb.X > 2 && pb.X < 12
                && pc.Asleep == 1 && Math.Abs(gap) <= dot,
                $"겹침 {worst / dot:0.00}도트 · 무중력 상자 1틱 뒤 vx {pa1.Vx:0.000}/{want:0.000} · vy {pa1.Vy:0.0e0} · ω {pa1.W:0.000} · 튐 {bounces1} · 1g 상자 x {pb.X:0.000} vx {pb.Vx:0.###} · 붙어 선 상자 틈 {gap / dot:0.00}도트 · 잠 {pc.Asleep}");
        }
    }

    // 공기 없는 20×10 방 (안쪽은 도면 x 2~22 · y 2~12 m) — 물체 60개
    private const string WideRoom = "\n######################\n#....................#\n#....................#\n#....................#\n#....................#\n#....................#\n#....................#\n#....................#\n#....................#\n#....................#\n#....................#\n######################";

    private static double Kinetic(Structure st)
    {
        double e = 0;
        foreach (var p in st.Props) if (p.Kg > 0) e += 0.5 * p.Kg * (p.Vx * p.Vx + p.Vy * p.Vy) + 0.5 * p.Inertia * p.W * p.W;
        return e;
    }

    /// <summary>물체 쌍 가운데 가장 깊은 겹침 (m — 껍질 SAT · 겹치지 않으면 0).</summary>
    private static double WorstPairOverlap(Structure st, IEnumerable<int> ids)
    {
        var list = ids.Select(st.IndexOf).Where(i => i >= 0).Select(i => st.Props[i]).ToList();
        double worst = 0;
        for (int a = 0; a < list.Count; a++)
            for (int b = a + 1; b < list.Count; b++) worst = Math.Max(worst, -st.PairSeparation(list[a], list[b]));
        return worst;
    }

    /// <summary>
    /// 물체끼리 (PH6a — docs/PHYSICS.md §3-5 · §10 법칙 6 · 8 · 9 · 12 · 14): 닫힌 무중력 방에서 서로 · 벽에 튀는 열 개 — 운동 에너지가 틱마다 늘지 않고 · 물체 + 벽이 받은 몫 = 처음 ·
    /// 갈라진 배의 조각 바닥에서 상자가 상자에 부딪혀도 몸 + 물체 = 민 것 · 비스듬한 충격량 ω = (r × J)/I · 1코어 = 4코어 (60개) · 구석에 쌓이는 10개 왕복 · 섬 놓침 0.
    /// </summary>
    private static void PileLaws()
    {
        const double dot = DotForm.Dot;
        string[] names = { "식량 상자", "소화기", "공구 상자", "수리 꾸러미", "책" };
        // 법칙 8 · 6 (몸이 없는 배): 닫힌 무중력 · 공기 없는 방 — 열 개가 서로 · 벽에 튄다
        {
            var s = PropWorld(0, 4);
            var st = s.Get<Structure>();
            var rng = new Random(11);
            for (int k = 0; k < 10; k++)
            {
                double a = rng.NextDouble() * 2 * Math.PI, v = 1 + 2 * rng.NextDouble();
                Slide(s, names[k % names.Length], 3 + k % 5 * 2, 3.5 + k / 5 * 3, v * Math.Cos(a), v * Math.Sin(a), 4 * rng.NextDouble() - 2);
            }
            (double px, double py, double l) Book() { var (px, py, l) = st.PropMomentum(); return (px + st.ToHostPx, py + st.ToHostPy, l + st.ToHostL - st.FixL); }
            var b0 = Book();
            double e0 = Kinetic(st), prev = e0, rise = 0, worstP = 0, worstL = 0, scale = 0;
            foreach (var p in st.Props) scale += p.Kg * Math.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy);
            for (int k = 0; k < 20; k++)
            {
                s.Step();
                double e = Kinetic(st);
                rise = Math.Max(rise, e - prev); prev = e;
                var b = Book();
                worstP = Math.Max(worstP, Math.Sqrt((b.px - b0.px) * (b.px - b0.px) + (b.py - b0.py) * (b.py - b0.py))); worstL = Math.Max(worstL, Math.Abs(b.l - b0.l));
            }
            Check("물체끼리 — 에너지는 늘지 않는다 (법칙 8): 닫힌 무중력 · 공기 없는 방에서 물체 열 개가 서로 · 벽에 튄다 — 운동 에너지가 틱마다 줄기만 한다 · 물체끼리 충격량을 주고받았다",
                rise <= 1e-12 * e0 && prev < e0 && st.PairHits > 0,
                $"운동 에너지 {e0:0.00} → {prev:0.00}J · 틱 사이 가장 큰 늚 {rise:0.0e0}J · 물체끼리 {st.PairHits} · 튐 {st.Bounces}");
            Check("물체끼리 — 운동량 장부 (법칙 6 · 몸이 없는 배): 물체 + 벽이 받은 몫 = 처음 (운동량 1e-9 · 각운동량은 + 바로잡음 FixL) · 물체끼리 충격량을 주고받았다",
                worstP <= 1e-9 * scale && worstL <= 1e-9 * scale * 20 && st.PairHits > 0, $"남는 운동량 {worstP:0.0e0} · 각운동량 {worstL:0.0e0} (운동량 크기 {scale:0.0}) · FixL {st.FixL:0.0e0} · 물체끼리 {st.PairHits}");
        }
        // 법칙 6 (몸 위): 갈라진 두 방 — 3m/s로 밀린 몸 1의 바닥에서 상자 둘이 미끄러져 (바닥이 빠져나간다) 앞 상자는 벽에 · 뒤 상자는 앞 상자에 부딪힌다 — 몸 + 물체 = 민 것 (1e-9)
        {
            var (s, st) = SplitWorld(Sealed, 6);
            int right = st.BodyOf(s.Layout.Index(10, 3));
            int front = Slide(s, "식량 상자", 9.7, 3.4, 0, 0), back = Slide(s, "식량 상자", 10.9, 3.5, 0, 0);
            var e0 = WorldBook(st);
            var o = st.Bodies[right];
            double jx = o.Mass * 3, jy = o.Mass * 0.2, pushL = (o.X + 0.3) * jy - (o.Y - 0.2) * jx, scale = Math.Sqrt(jx * jx + jy * jy);
            st.Push(right, o.X + 0.3, o.Y - 0.2, jx, jy);
            double worstP = 0, worstL = 0;
            for (int k = 0; k < 6; k++)
            {
                s.Step();
                var e = WorldBook(st);
                double ex = e.px - e0.px - jx, ey = e.py - e0.py - jy;
                worstP = Math.Max(worstP, Math.Sqrt(ex * ex + ey * ey)); worstL = Math.Max(worstL, Math.Abs(e.l - e0.l - pushL));
            }
            var pf = st.Props[st.IndexOf(front)]; var pb = st.Props[st.IndexOf(back)];
            Check("물체끼리 — 운동량 장부 (법칙 6 · 몸 위): 3m/s로 밀린 조각 바닥에서 미끄러진 상자 둘 — 앞 상자는 벽에 · 뒤 상자는 앞 상자에 부딪힌다 — 몸 + 물체 + 기체 = 민 것 (운동량 · 각운동량 1e-9)",
                worstP <= 1e-9 * scale && worstL <= 1e-9 * scale * 20 && st.PairHits > 0 && st.PairSeparation(pf, pb) > -dot,
                $"남는 운동량 {worstP:0.0e0} · 각운동량 {worstL:0.0e0} · 물체끼리 {st.PairHits} · 두 상자 사이 {st.PairSeparation(pf, pb) / dot:0.00}도트 · 앞 x {pf.X:0.000} · 뒤 x {pb.X:0.000}");
        }
        // 법칙 9 (비스듬): 무중력 · 공기 없는 방 — 45° 돈 가벼운 책의 모서리가 선 식량 상자의 서쪽 면을 가운데에서 0.15m 비켜 맞힌다 → 맞은 상자 Δω = (r × J)/I
        // (J = 상자가 받은 m Δv · r = 맞은 점 (서쪽 면 · 책 모서리 줄) − 무게중심 · 한 점으로 맞고 책이 가벼워 맞는 동안 상자가 거의 돌지 않는다 · ±2%)
        {
            var s = PropWorld(0, 2);
            var st = s.Get<Structure>();
            int b = Slide(s, "식량 상자", 7.5, 5.0, 0, 0);
            int a = Slide(s, "책", 6.6, 5.0, 3, 0);
            { int i = st.IndexOf(a); var p = st.Props[i]; p.Th = Math.PI / 4; st.Props[i] = p; }
            var pa0 = st.Props[st.IndexOf(a)]; var pb0 = st.Props[st.IndexOf(b)];
            var fa = st.PropForm(pa0); var fb = st.PropForm(pb0);
            double c = Math.Cos(pa0.Th), sn = Math.Sin(pa0.Th), tipX = double.MinValue, tipY = 0;
            foreach (var (hx, hy) in fa.Hull) { double x = pa0.X + c * (hx - fa.Cx) - sn * (hy - fa.Cy), y = pa0.Y + sn * (hx - fa.Cx) + c * (hy - fa.Cy); if (x > tipX) { tipX = x; tipY = y; } }
            { int i = st.IndexOf(a); var p = st.Props[i]; p.Y += 5.0 - 0.15 - tipY; st.Props[i] = p; }   // 모서리가 가운데에서 0.15m 위로 비켜 맞게
            pa0 = st.Props[st.IndexOf(a)];
            double face = pb0.X + fb.Hull.Min(v => v.x) - fb.Cx;
            s.Run(1);   // 한 틱 (맞은 상자가 동쪽 벽에 닿기 전)
            var pb = st.Props[st.IndexOf(b)];
            double jx = pb.Kg * pb.Vx, jy = pb.Kg * pb.Vy, rx = face - pb0.X, ry = -0.15;
            double want = (rx * jy - ry * jx) / pb.Inertia;
            Check("물체끼리 — 비스듬한 충격량 (법칙 9): 45° 돈 책의 모서리가 3m/s로 선 식량 상자의 옆면을 가운데에서 0.15m 비켜 맞힌다 — 맞은 상자 ω = (r × J)/I (±2% — 맞는 동안 모서리가 면을 따라 조금 미끄러진다) · 앞으로 밀린다",
                Math.Abs(pb.W - want) <= 0.02 * Math.Abs(want) && Math.Abs(want) > 0.01 && pb.Vx > 0.01 && st.PairHits > 0,
                $"맞은 상자 ω {pb.W:0.00000}/{want:0.00000}rad/s · v ({pb.Vx:0.0000}, {pb.Vy:0.0000}) · r ({rx:0.000}, {ry:0.000})m · 책 {pa0.Kg:0.00}kg · 물체끼리 {st.PairHits}");
        }
        // 법칙 12 · 14: 1코어 = 4코어 — 60개가 0.1g 바닥에서 서로 · 벽에 튄다 (30틱) · 섬 놓침 0
        {
            (ulong hash, long pairs, long miss) Run(int threads)
            {
                int keep = Par.Threads; Par.Threads = threads;
                var s = PropWorld(0.1f, 9, WideRoom);
                var st = s.Get<Structure>();
                var rng = new Random(5);
                for (int k = 0; k < 60; k++)
                {
                    double a = rng.NextDouble() * 2 * Math.PI, v = 1 + 4 * rng.NextDouble();
                    Slide(s, names[k % names.Length], 2.6 + k % 12 * 1.6, 2.7 + k / 12 * 1.9, v * Math.Cos(a), v * Math.Sin(a), 6 * rng.NextDouble() - 3);
                }
                s.Run(30);
                Par.Threads = keep;
                return (s.Hash(), st.PairHits, st.IslandMiss);
            }
            var one = Run(1); var four = Run(4);
            Check("물체끼리 — 결정론 (법칙 12 · 14): 0.1g 바닥에서 서로 · 벽에 튀는 60개 (30틱) — 1코어 = 4코어 · 섬 놓침 0",
                one.hash == four.hash && one.pairs > 50 && one.miss == 0 && four.miss == 0, $"{one.hash:x16} / {four.hash:x16} · 물체끼리 {one.pairs} · 섬 놓침 {one.miss}/{four.miss}");
        }
        // 법칙 12 · 14: 구석에 쌓이는 10개 — 무중력 바닥 · 몸이 없는 배가 비스듬히 0.6m/s² 점화 (틀의 힘이 +x +y 구석으로 민다) · 10틱 뒤 찍고 되살려 60틱 바이트 같음 · 섬 놓침 0 · 겹침 ≤ 1도트
        {
            var s = PropWorld(0, 3);
            var st = s.Get<Structure>();
            var ids = new List<int>();
            for (int k = 0; k < 10; k++) ids.Add(Slide(s, names[k % names.Length], 6.5 + k % 4 * 1.3, 4.2 + k / 4 * 1.2, 0, 0));
            st.ShipAx = -0.6; st.ShipAy = -0.6;
            s.Run(10);
            var r = new Sim(s.Setup); r.Load(s.Save());
            r.Get<Structure>().ShipAx = st.ShipAx; r.Get<Structure>().ShipAy = st.ShipAy;
            double worst = 0;
            for (int k = 0; k < 60; k++) { s.Step(); r.Step(); if (k >= 50) worst = Math.Max(worst, WorstPairOverlap(st, ids)); }
            bool same = s.Save().AsSpan().SequenceEqual(r.Save());
            int awake = st.Props.Count(p => Structure.Awake(p));
            Check("물체끼리 — 쌓인 중 왕복 (법칙 12 · 14): 몸이 없는 배가 비스듬히 점화해 무중력 바닥의 10개가 구석에 쌓인다 — 10틱 뒤 찍고 되살려 60틱 바이트 같음 · 섬 놓침 0 · 쌓인 것끼리 겹침 ≤ 1도트",
                same && st.IslandMiss == 0 && worst <= dot && st.PairHits > 0 && awake == 10,
                $"되살림 같음 {same} · 섬 놓침 {st.IslandMiss} · 마지막 10틱 가장 깊은 겹침 {worst / dot:0.00}도트 · 물체끼리 {st.PairHits} · 깨어 있음 {awake}");
        }
    }

    /// <summary>바람을 시험이 쓰는 세계 (장은 걸음을 돌지 않는다 — 구조만 돈다 · 공기를 채운 장 · 바닥 중력 g): 바람 칸은 <see cref="Blow"/>로 쓴다.</summary>
    private static (Sim s, Field f) WindWorld(float g, string art = PropRoom)
    {
        var lay = Worlds.Ascii(art);
        Field? fld = null;
        var s = new Sim(new SimSetup(lay, 1, sim => { fld = new Field(sim.Layout, sim.Tables); sim.Add(new Structure(fld)); }));
        fld!.FillAir(); Array.Fill(fld.Grav, g);
        return (s, fld);
    }

    /// <summary>칸 c에 바람: 동압 q (Pa) · 쪽 (dx, dy) · 처음 밀도 1.2kg/m³ · 줄지 않는다 (장이 쓰는 꼴 그대로 — 바람 빠르기 √(2q/ρ)).</summary>
    private static void Blow(Field f, int c, double q, double dx, double dy)
    {
        double len = Math.Sqrt(dx * dx + dy * dy), u = Math.Sqrt(2 * q / 1.2);
        f.WindPa[c] = (float)q; f.WindX[c] = (float)(u * dx / len); f.WindY[c] = (float)(u * dy / len); f.WindK[c] = 0; f.WindRho[c] = 1.2f;
        if (!f.WindCells.Contains(c)) f.WindCells.Add(c);
    }

    /// <summary>
    /// 바람 (PH4c — docs/PHYSICS.md §3-2 · §10 법칙 9): 물체가 덮은 칸마다 나눠 ½ρ|u − v|(u − v) Cd A(φ) × 그 칸 몫 · 힘 점 = 그 칸 도트의 가운데 + 압력 중심 —
    /// 고른 바람에 대칭 상자는 돌지 않고 (ω 비트 0) 바람 쪽으로만 미끄러진다 · 압력 중심이 어긋난 꼴은 그 쪽으로 돈다 · 칸마다 다른 바람(기울기)에는 대칭 상자도 돈다 ·
    /// 거울 바람 · 거울 상자 둘은 거울 자리에 있다 (1e-12) · 막음이 풀리는 틱 바로 전에 찍고 되살려도 같다.
    /// </summary>
    private static void WindLaws()
    {
        var t = Tables.Default;
        const double g = 9.8;
        // 고른 바람: 1g · 방 안 모든 칸에 같은 바람 — 칸 가운데 식량 상자 (한 칸 안 · 600Pa) · 압력 중심이 가장 많이 어긋난 물건 (물건 표에서 고른다 · 그 어긋남이 큰 쪽 바람 · 넉넉히 미끄러질 만큼)
        {
            string odd = ""; int dir = 0; double cop = 0, qOdd = 0;
            foreach (var name in t.People.Items.Keys)
            {
                var fm = DotForge.Make(t, name);
                if (fm.Columns == 0 || fm.W > 24 || fm.D > 24) continue;   // 한 칸 안에 드는 것
                for (int a16 = 0; a16 <= 4; a16 += 4)
                    if (fm.Area16[a16] > 0 && Math.Abs(fm.Cop16[a16]) > Math.Abs(cop) + 1e-12) { cop = fm.Cop16[a16]; odd = name; dir = a16; qOdd = 3 * 1.25 * 0.65 * fm.Mass * g / (fm.Cd * fm.Area16[a16]); }   // 가장 미끄럽지 않은 재질의 정지 마찰 세 배
            }
            double wx = dir == 0 ? 1 : 0, wy = dir == 0 ? 0 : 1;
            Prop Run(string name, double q)
            {
                var (s, f) = WindWorld(1);
                var st = s.Get<Structure>(); var L = s.Layout;
                for (int c = 0; c < L.N; c++) if (L.Kind[c] == CellKind.Floor) Blow(f, c, q, wx, wy);
                int id = Slide(s, name, 4.5, 3.5, 0, 0);
                s.Step();
                return st.Props[st.IndexOf(id)];
            }
            var pa = Run("식량 상자", 600); var pb = Run(odd, qOdd);
            bool along = dir == 0 ? pa.X > 4.6 && pa.Y == 3.5 && pa.Vy == 0 : pa.Y > 3.6 && pa.X == 4.5 && pa.Vx == 0;
            // 압력 중심이 무게중심에서 바람에 수직인 몸 축 u 쪽으로 cop 어긋났다 → 돌림힘 = −cop × F (u = (−sin a, cos a))
            Check($"바람 — 고른 바람 (1g · {(dir == 0 ? "+x" : "+y")}): 칸 가운데 식량 상자 (600Pa)는 바람 쪽으로만 미끄러진다 (돎 · 옆 빠르기 비트 0) · 압력 중심이 어긋난 {Ko.Eun(odd)} ({qOdd:0}Pa) 그 쪽으로 돈다",
                along && pa.W == 0 && pa.Th == 0 && Math.Abs(cop) > 1e-3 && Math.Abs(pb.Th) > 1e-3 && Math.Sign(pb.Th) == -Math.Sign(cop),
                $"상자 ({pa.X:0.000}, {pa.Y:0.000}) · ω {pa.W:R} · θ {pa.Th:R} / {odd} 압력 중심 {cop * 1000:0.0}mm · θ {pb.Th:0.0000}rad");
        }
        // 기울기 바람: 1g · 4행 칸만 +x 1500Pa (5행은 고요) — 두 행에 반씩 걸친 대칭 상자가 돈다 (위 반만 밀린다 → 돌림힘 +)
        {
            var (s, f) = WindWorld(1);
            var st = s.Get<Structure>(); var L = s.Layout;
            for (int c = 0; c < L.N; c++) if (L.Kind[c] == CellKind.Floor && L.Y(c) == 4) Blow(f, c, 1500, 1, 0);
            int a = Slide(s, "식량 상자", 4.5, 5.0, 0, 0);
            s.Step();
            var p = st.Props[st.IndexOf(a)];
            Check("바람 — 기울기 바람: 4행만 +x 1500Pa · 두 행에 반씩 걸친 식량 상자 (대칭)가 돈다 (밀린 위 반 쪽 — θ > 0) · 바람 쪽으로 간다",
                p.Th > 1e-3 && p.X > 4.5, $"θ {p.Th:0.0000}rad · x {p.X:0.000}");
        }
        // 거울: 가운데 (x = 7)로 모이는 거울 바람 (멀수록 세다) · 거울 상자 둘 (자리 · 자세 · 빠르기 · 돎이 거울) → 20틱 내내 거울 자리 (1e-12)
        {
            var (s, f) = WindWorld(1);
            var st = s.Get<Structure>(); var L = s.Layout;
            for (int c = 0; c < L.N; c++)
            {
                if (L.Kind[c] != CellKind.Floor) continue;
                double cx = L.X(c) + 0.5 - 7;
                Blow(f, c, 450 + 100 * Math.Abs(cx) + 100 * (L.Y(c) % 2), -Math.Sign(cx), 0);
            }
            int a = Slide(s, "식량 상자", 3.3, 4.6, 0.4, 0.2, 0.7), b = Slide(s, "식량 상자", 10.7, 4.6, -0.4, 0.2, -0.7);
            { int i = st.IndexOf(a); var p = st.Props[i]; p.Th = 0.3; st.Props[i] = p; i = st.IndexOf(b); p = st.Props[i]; p.Th = -0.3; st.Props[i] = p; }
            double worst = 0; bool moved = false;
            for (int k = 0; k < 20; k++)
            {
                s.Step();
                var pa = st.Props[st.IndexOf(a)]; var pb = st.Props[st.IndexOf(b)];
                worst = Math.Max(worst, Math.Max(Math.Abs(pa.X + pb.X - 14), Math.Max(Math.Abs(pa.Y - pb.Y), Math.Abs(pa.Th + pb.Th))));
                moved |= Math.Abs(pa.X - 3.3) > 0.2;
            }
            Check("바람 — 거울: 가운데로 모이는 거울 바람 · 거울 상자 둘 → 20틱 내내 거울 자리 · 자세 (1e-12) · 바람에 움직였다",
                worst <= 1e-12 && moved, $"가장 큰 어긋남 {worst:0.0e0} · 움직임 {moved}");
        }
        // 법칙 12: 막음이 풀리는 틱 바로 전 왕복 — 식량 상자 · 사람이 0.06m² 구멍을 막은 채 구멍을 덧대고 찍는다 → 되살린 세계와 10틱 바이트 같음 (풀린 막음이 장에 남지 않는다)
        {
            var lay = Worlds.Ascii(BreachRooms, Worlds.Seats);
            int hole = Worlds.At(lay, 0, 2);
            var s = Worlds.Full(lay, 1);
            var st = s.Get<Structure>(); var f = s.Get<Field>(); var m = s.Get<Mind>();
            st.RequestDoor(lay.DoorAt[Worlds.At(lay, 6, 2)], 0);
            int who = m.AddPerson(s, Worlds.At(lay, 1, 2));
            int id = Worlds.PutItem(s, "식량 상자", Worlds.At(lay, 1, 2));
            s.Run(3);
            f.SetHole(hole, 0.06f);
            m[who].C.PinAt = hole + 1; st.SetItemPlug(id, hole + 1);   // 둘 다 빨려 붙었다 (몸이 먼저 막아 구멍 입 바람이 약하다 — 차림으로)
            s.Run(20);
            int ip = st.IndexOf(id);
            bool plugged = ip >= 0 && st.Props[ip].PlugAt == hole + 1 && f.PlugOf(hole) > 0;
            st.Patch(hole, 0);
            var r = new Sim(s.Setup); r.Load(s.Save());
            s.Run(10); r.Run(10);
            bool same = s.Save().AsSpan().SequenceEqual(r.Save());
            Check("막음 왕복: 상자 · 사람이 구멍을 막은 채 덧대고 찍는다 (막음이 풀리는 틱 바로 전) → 되살린 세계와 10틱 바이트 같음 · 둘 다 풀렸다",
                plugged && same && f.PlugOf(hole) == 0 && r.Get<Field>().PlugOf(hole) == 0,
                $"막음 {plugged} · 바이트 같음 {same} · 막은 넓이 {f.PlugOf(hole):0.0000} / 되살린 세계 {r.Get<Field>().PlugOf(hole):0.0000}m²");
        }
    }
}
