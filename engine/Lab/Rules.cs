using ShipEngine;

namespace Lab;

/// <summary>규칙 시험 — 게임 규칙 하나씩 (작은 배 · 몇 초). 새 기능 · 고친 버그마다 하나씩 더한다.</summary>
public static partial class Program
{
    private static int s0(Chemistry c) => 0;

    private static void Rules()
    {
        Console.WriteLine("규칙 시험\n");

        if (Do("배도면"))
        {
            int n = 0;
            foreach (var key in Layout.ShipKeys())
            {
                var l = Layout.Ship(key, Tables.Default);
                var s = Worlds.FieldOnly(l, 1);
                var f = s.Get<Field>();
                s.Run(Time.Minutes(30));
                double lo = double.MaxValue, hi = 0;
                foreach (var r in f.Regions) { double p = f.Pressure(r[0]); lo = Math.Min(lo, p); hi = Math.Max(hi, p); }
                Check($"배 도면 {l.Name}: 공기를 채우고 30분 — 새는 곳 없이 1기압 근처", lo > 98 && hi < 104 && f.VentedTotal < 1,
                    $"{l.W}×{l.H} · 방 {l.Rooms.Count} · 구역 {f.Regions.Count} · 압력 {lo:0.0}~{hi:0.0}kPa · 샌 공기 {f.VentedTotal:0.0}몰");
                n++;
            }
            if (n == 0) Check("배 도면이 하나 이상 있다", false);
        }

        if (Do("배전기"))
        {
            // 모든 배: 평시 10분 — 과부하로 떨어지는 차단기 없음 · 원자로 멀쩡 (한누리호 냉각실 급전선이 펌프 여럿을 못 버티던 버그)
            var bad = new List<string>();
            int n = 0;
            foreach (var key in Layout.ShipKeys())
            {
                var s = ShipWorld.Create(key, 1);
                var net = s.Get<Network>();
                s.Run(Time.Minutes(10));
                if (net.Trips > 0 || net.ReactorScram) bad.Add($"{s.Layout.Name}(차단기 {net.Trips} · 원자로 {(net.ReactorScram ? "멈춤" : "멀쩡")})");
                n++;
            }
            Check($"배 {n}척 평시 10분: 과부하 차단기 없음 · 원자로 멀쩡 (급전선 굵기는 방의 장치에 맞춘다)", bad.Count == 0, bad.Count == 0 ? "모두 멀쩡" : string.Join(" · ", bad));
        }

        if (Do("불"))
        {
            var lay = Worlds.Ascii(Worlds.Lounge, Worlds.Seats);
            int a = Worlds.At(lay, 3, 3), d = Worlds.At(lay, 6, 3), far = Worlds.At(lay, 14, 1);

            // 불씨 → 붙는다 → 옆 의자로 번진다
            var s = Worlds.WithChem(lay, 1, (f, c) => c.Ignite(a, 3e6, s0(c)));
            var ch = s.Get<Chemistry>(); var f = s.Get<Field>();
            double o2Start = f.Total(f.T.gO2);
            int SeatsBurned() => new[] { 3, 4, 5, 6 }.Count(x => f.Stored(f.T.sPoly, Worlds.At(lay, x, 3)) < 560);
            s.Run(Time.Minutes(1));
            Check("불씨를 대면 의자에 불이 붙는다", ch.IsBurning(a), $"{f.Temp(a):0}K · 산소 {f.Partial(f.T.gO2, a):0.0}kPa");
            double smokeNear = f.Ppm(f.T.gSoot, Worlds.At(lay, 3, 2)), smokeFar = f.Ppm(f.T.gSoot, far);
            Check("연기는 불 곁에서 짙다", smokeNear > smokeFar * 3 && smokeNear > 10, $"곁 {smokeNear:0}ppm · 먼 구석 {smokeFar:0}ppm");
            s.Run(Time.Minutes(25));
            int b = Worlds.At(lay, 4, 3);
            string story = s.Causes.Story(ch.FireCause[b]);
            Check("불은 옆 의자로 번진다 (불꽃 복사 — 대본 없이) · 원인 사슬이 남는다", SeatsBurned() >= 2 && ch.Ignitions >= 2 && story.Contains("번졌다"),
                $"탄 의자 {SeatsBurned()}/4 · 발화 {ch.Ignitions}번 · 둘째 의자 원인: {story}");
            double co = Enumerable.Range(0, f.Cells).Max(i => f.Ppm(f.T.gCO, i));
            double o2End = f.Total(f.T.gO2);
            Check("닫힌 방의 불은 산소를 먹고 일산화탄소를 낸다", co > 300 && o2End < o2Start * 0.9, $"일산화탄소 최고 {co:0}ppm · 산소 {o2Start:0} → {o2End:0}몰 · 먼 구석 산소 {f.Ppm(f.T.gO2, far) / 1e4:0.0}%");

            // 진공으로 빼면 꺼진다
            var s2 = Worlds.WithChem(lay, 1, (ff, c) => c.Ignite(a, 3e6, 0));
            s2.Run(Time.Minutes(2));
            var ch2 = s2.Get<Chemistry>(); var f2 = s2.Get<Field>();
            bool was = ch2.BurningCount > 0;
            f2.SetHole(Worlds.At(lay, 16, 3), 2.5f);
            s2.Run(Time.Minutes(1));
            Check("방을 우주로 빼면 불이 꺼진다", was && ch2.BurningCount == 0, $"불 붙은 칸 {(was ? "있었다" : "없었다")} → {ch2.BurningCount}");

            // 물을 부으면 꺼진다
            var s3 = Worlds.WithChem(lay, 1, (ff, c) => c.Ignite(a, 3e6, 0));
            s3.Run(Time.Minutes(1));
            var ch3 = s3.Get<Chemistry>(); var f3 = s3.Get<Field>();
            foreach (int x in new[] { 3, 4 }) f3.AddStore(Worlds.At(lay, x, 3), f3.T.sWater, 3000);
            s3.Run(Time.Minutes(2));
            Check("물을 부으면 식어서 꺼진다 (증발열)", !ch3.IsBurning(a), $"{f3.Temp(a):0}K");

            // 수소가 섞인 방에 불꽃 → 한꺼번에 탄다
            var s4 = Worlds.WithChem(Worlds.Ascii(Worlds.Symmetric), 1, (ff, c) =>
            {
                for (int i = 0; i < ff.Cells; i++) if (ff.RegionOf.Length == 0 || true) if (ff.L.Kind[i] == CellKind.Floor) ff.AddGas(i, ff.T.gH2, 12, 293);
            });
            var f4 = s4.Get<Field>(); var ch4 = s4.Get<Chemistry>();
            s4.Run(3);
            int mid = Worlds.At(f4.L, 5, 3);
            double p0 = f4.Pressure(mid);
            ch4.Ignite(mid, 5e4, 0);
            double pk = 0, tk = 0;
            for (int i = 0; i < 20; i++) { s4.Step(); pk = Math.Max(pk, f4.Pressure(mid)); tk = Math.Max(tk, f4.Temp(mid)); }
            Check("수소가 섞인 방에 불꽃 → 한꺼번에 타며 압력이 튄다", ch4.GasBursts > 10 && pk > p0 * 1.5 && tk > 1200,
                $"폭연 {ch4.GasBursts}칸 · 압력 {p0:0} → {pk:0}kPa · {tk:0}K · 남은 수소 {f4.Total(f4.T.gH2):0.0}몰");
        }

        if (Do("구조"))
        {
            // 작은 파편(50kJ)이 외판에 → 구멍 → 그 방이 빠진다 · 원인 사슬
            var l = Worlds.Ascii(Worlds.Sealed);
            var s = Worlds.Full(l, 1);
            var st = s.Get<Structure>(); var f = s.Get<Field>();
            int hull = Worlds.At(l, 0, 3);
            int cause = s.Causes.Add(CauseKind.Impact, "파편이 외판을 쳤다", s.Tick, hull);
            st.Impact(s, hull, 50_000, 1, 0, cause);
            s.Run(Time.Minutes(5));
            int left = Worlds.At(l, 4, 3), right = Worlds.At(l, 15, 3);
            Check("파편(50kJ)이 외판을 뚫으면 그 방이 빠진다 · 차압 문 너머는 그대로", f.Hole[hull] > 0 && f.Pressure(left) < 60 && f.Pressure(right) > 99,
                $"구멍 {f.Hole[hull] * 1e4:0}cm² · 왼쪽 {f.Pressure(left):0.0} · 오른쪽 {f.Pressure(right):0.0} kPa · 원인 {s.Causes.Story(st.DamageCause[hull])}");
            // 큰 충돌(50MJ)은 여러 벽을 뚫고 불덩이를 남긴다
            var s2 = Worlds.Full(l, 1);
            var st2 = s2.Get<Structure>(); var f2 = s2.Get<Field>();
            st2.Impact(s2, hull, 5e7, 1, 0, 0);
            s2.Run(Time.Minutes(3));
            int holes = Enumerable.Range(0, f2.Cells).Count(i => f2.Hole[i] > 0);
            Check("큰 충돌(50MJ)은 여러 벽을 뚫고 지나간다 — 두 방이 다 빠진다", holes >= 2 && f2.Pressure(left) < 5 && f2.Pressure(right) < 60,
                $"구멍 {holes}칸 · 왼쪽 {f2.Pressure(left):0.0} · 오른쪽 {f2.Pressure(right):0.0} kPa");
            // 피로가 쌓인 칸막이는 한쪽이 진공이 되면 찢어진다
            var l3 = Worlds.Ascii(Worlds.TwoRooms);
            var s3 = Worlds.Full(l3, 1);
            var st3 = s3.Get<Structure>(); var f3 = s3.Get<Field>();
            int wall = Worlds.At(l3, 10, 1); // 두 방 사이 칸막이 (문 아님)
            st3.Fatigue[wall] = 0.45f;
            f3.SetHole(Worlds.At(l3, 0, 2), 2.5f); // 왼쪽 방 외판이 뚫렸다
            s3.Run(Time.Minutes(10));
            Check("피로가 쌓인 칸막이는 한쪽이 진공이 되면 찢어진다 (오른쪽 방도 빠진다)", f3.Hole[wall] > 0 && f3.Pressure(Worlds.At(l3, 15, 2)) < 90,
                $"칸막이 구멍 {f3.Hole[wall]:0.000}m² · 부하 {st3.Stress[wall]:0.00} · 오른쪽 방 {f3.Pressure(Worlds.At(l3, 15, 2)):0.0}kPa");
            Check("새 칸막이는 버틴다 (피로 0)", Enumerable.Range(0, f3.Cells).Where(i => i != wall && f3.L.Kind[i] == CellKind.Wall && !f3.L.Hull[i]).All(i => f3.Hole[i] == 0 || i == wall));
            // 기압 차가 있으면 문이 안 열린다 (손으로)
            var s4 = Worlds.Full(l, 1);
            var st4 = s4.Get<Structure>(); var f4 = s4.Get<Field>();
            f4.SetHole(hull, 2.5f);
            s4.Run(Time.Minutes(1));
            st4.DoorPowered[0] = 0;
            st4.RequestDoor(0, 1f);
            s4.Run(20);
            Check("기압 차가 크면 손으로 문을 못 연다 (진공 쪽 문)", f4.DoorOpen[0] == 0, $"차이 {f4.Pressure(right) - f4.Pressure(left):0}kPa · 문 {f4.DoorOpen[0]:0.0}");
            // 폭발이 보통 문을 날린다
            var l5 = Worlds.Ascii(Worlds.TwoRooms);
            var s5 = Worlds.Full(l5, 1, sim =>
            {
                var ff = sim.Get<Field>();
                foreach (int c in ff.L.Rooms.First(r => r.Cells.Contains(Worlds.At(ff.L, 3, 2))).Cells) ff.AddGas(c, ff.T.gH2, 30, 293);   // 수소 22% 남짓 (보통 문 정격 210kPa — DF9)
            });
            s5.Run(3);
            s5.Get<Chemistry>().Ignite(Worlds.At(l5, 3, 2), 5e4, 0);
            s5.Run(30);
            var st5 = s5.Get<Structure>();
            Check("수소 폭연이 보통 문을 날린다 (압력이 정격을 넘는다)", st5.DoorsBlown > 0 && s5.Get<Field>().DoorOpen[0] > 0,
                $"날아간 문 {st5.DoorsBlown} · 문 열림 {s5.Get<Field>().DoorOpen[0]:0.0} · 큰 소리 {s5.Facts.CountOf(FactKind.LoudSound)}번");
            // 닫힌 문 칸은 기체가 없다 — 그 문과 맞닿은 벽이 한쪽이 진공이라고 착각하면 안 된다 (디딤호 9시간에 금이 갔던 버그)
            var l8 = Worlds.Ascii(Worlds.WallOverDoor);
            var s8 = Worlds.Full(l8, 1);
            s8.Run(5);
            int over = Worlds.At(l8, 4, 2);
            Check("닫힌 문 위의 벽은 압력을 받지 않는다 (문 칸의 빈 기체를 진공으로 보지 않는다)", s8.Get<Structure>().Stress[over] < 0.05f,
                $"부하 {s8.Get<Structure>().Stress[over]:0.00} · 위 {s8.Get<Field>().Pressure(Worlds.At(l8, 4, 1)):0}kPa");
            // 보통 문은 한 기압 차를 버틴다 — 끼익 소리 · 문틈으로 쉬익 새어 옆 방도 천천히 빠진다 · 그 원인은 외판을 친 파편까지 이어진다
            var l6 = Worlds.Ascii(Worlds.TwoRooms);
            var s6 = Worlds.Full(l6, 1);
            var st6 = s6.Get<Structure>(); var f6 = s6.Get<Field>(); var snd6 = s6.Get<Sound>();
            int hull6 = Worlds.At(l6, 0, 2), doorCell6 = Worlds.At(l6, 10, 3), door6 = f6.L.DoorAt[doorCell6], right6 = Worlds.At(l6, 15, 3);
            int rock6 = s6.Causes.Add(CauseKind.Impact, "파편이 외판을 쳤다", s6.Tick, hull6);
            st6.Impact(s6, hull6, 3e5, 1, 0, rock6);
            int creaks = 0;
            for (int k = 0; k < Time.Minutes(15); k++)
            {
                s6.Step();
                if (snd6.At(right6).Any(h => h.Kind == SoundKind.Creak)) creaks++;
                if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1" && k % 250 == 0)
                    Console.WriteLine($"    {k / 25}분 · 왼쪽 {f6.Pressure(Worlds.At(l6, 3, 2)):0.0} · 오른쪽 {f6.Pressure(right6):0.0} · 부하 {st6.Stress[doorCell6]:0.00} · 피로 {st6.Fatigue[doorCell6]:0.000}");
            }
            Check("보통 문은 한 기압 차를 버티며 끼익거린다 · 문틈으로 새어 옆 방이 천천히 빠진다 · 문을 민 압력의 원인은 외판을 친 파편",
                f6.DoorOpen[door6] == 0 && creaks > 0 && f6.Pressure(right6) is > 60 and < 98 && s6.Causes.Story(st6.DamageCause[doorCell6]).Contains("파편"),
                $"문 {f6.DoorOpen[door6]:0.0} · 끼익 {creaks}번 · 오른쪽 {f6.Pressure(right6):0}kPa · 원인 {s6.Causes.Story(st6.DamageCause[doorCell6])}");
            // 피로가 쌓인 문은 날아간다 — 원인 사슬이 이어진다
            var s7 = Worlds.Full(l6, 1);
            var st7 = s7.Get<Structure>();
            st7.Fatigue[doorCell6] = 0.6f;   // 정격이 한 기압 아래로 (보통 문 210kPa × 0.4 — DF9: 정적 하중은 피로를 쌓지 않는다 · 정격을 넘어야 날아간다)
            int rock7 = s7.Causes.Add(CauseKind.Impact, "파편이 외판을 쳤다", s7.Tick, hull6);
            st7.Impact(s7, hull6, 3e5, 1, 0, rock7);
            s7.Run(Time.Minutes(10));
            var blown = s7.Facts.Since(0).Where(x => x.Kind == FactKind.DoorBlown).ToList();
            Check("피로가 쌓인 문(60% — 정격이 한 기압 아래)은 한쪽이 빠지면 날아간다 · '문이 날아갔다'의 원인은 파편까지 이어진다",
                blown.Count > 0 && s7.Causes.Story(blown[0].Cause).Contains("파편"),
                blown.Count > 0 ? $"{blown[0].Tick / 25.0:0.0}분 · {s7.Causes.Story(blown[0].Cause)}" : "안 날아갔다");
        }

        if (Do("바깥"))
        {
            // 잔해 지대를 지난다 → 파편이 외판을 친다 (대부분 작아 움푹 · 가끔 구멍)
            var s = ShipWorld.Create("Didim", 3);
            var o = s.Get<Outside>(); var st = s.Get<Structure>();
            int seed = s.Causes.Add(CauseKind.Route, "잔해 지대가 항로에 있다", s.Tick);
            o.AddCloud("rubble", 400_000, 300_000, 3e-7, 1, 0, seed);
            s.Run(Time.Minutes(8));
            var hit = s.Facts.Since(0).Where(f => f.Kind == FactKind.Impact).ToList();
            string story = hit.Count > 0 ? s.Causes.Story(hit[0].Cause) : "";
            Check("잔해 지대를 지나면 파편이 외판을 친다 · 원인 사슬이 항로의 원인까지 이어진다", o.DebrisHits > 0 && story.StartsWith("잔해 지대가 항로에 있다"),
                $"맞음 {o.DebrisHits}번 · 구멍 {st.Breaches} · 예: {story}");
            // 운석 정면: 큰 것은 여러 벽을 뚫는다 · 오기 전에 '다가온다'가 먼저
            var s2 = ShipWorld.Create("Didim", 3);
            var o2 = s2.Get<Outside>(); var f2 = s2.Get<Field>();
            o2.AddRock(2, 5000, 1, 0.1, 0, 30_000, 0);
            long warnAt = -1, hitAt = -1;
            for (int i = 0; i < 60 && hitAt < 0; i++)
            {
                s2.Step();
                if (warnAt < 0 && s2.Facts.CountOf(FactKind.DebrisNear) > 0) warnAt = s2.Tick;
                if (o2.RockHits > 0) hitAt = s2.Tick;
            }
            s2.Run(Time.Minutes(2));
            int holes = Enumerable.Range(0, f2.Cells).Count(i => f2.Hole[i] > 0);
            Check("2kg 운석(25MJ)이 정면으로 → 먼저 '다가온다' · 부딪혀 여러 벽을 뚫는다", warnAt >= 0 && warnAt < hitAt && holes >= 2 && f2.VentedTotal > 100,
                $"조짐 틱 {warnAt} · 충돌 틱 {hitAt} · 구멍 {holes}칸 · 샌 공기 {f2.VentedTotal:0}몰");
            // 비껴간다
            var s3 = ShipWorld.Create("Didim", 3);
            var o3 = s3.Get<Outside>();
            o3.AddRock(2, 5000, 1, 0, 300, 30_000, 0);
            s3.Run(60);
            Check("300m 비껴가는 운석은 맞지 않는다 (아슬아슬함은 사실로 남는다)", o3.RockHits == 0 && o3.NearMisses == 1, $"맞음 {o3.RockHits} · 비껴감 {o3.NearMisses}");
            // 별 폭발: 조짐 → 폭발 → 안쪽 방이 덜 맞는다
            var s4 = ShipWorld.Create("Salpim", 3);
            var o4 = s4.Get<Outside>(); var f4 = s4.Get<Field>();
            o4.Star = "redDwarf";
            o4.ScheduleFlare(s4.Tick, 5, 0);
            s4.Run(Time.Minutes(25));
            var floor = Enumerable.Range(0, f4.Cells).Where(i => f4.L.Kind[i] == CellKind.Floor).ToList();
            double hi = floor.Max(i => o4.Dose[i]), lo = floor.Min(i => o4.Dose[i]);
            bool warned = s4.Facts.Since(0).Any(x => x.Kind == FactKind.Flare && x.Other == 0);
            bool burst = s4.Facts.Since(0).Any(x => x.Kind == FactKind.Flare && x.Other == 1);
            Check("별 폭발: X선 조짐이 먼저 · 폭발 뒤 바깥 방사선이 뛴다 · 안쪽 칸이 덜 맞는다 (벽 · 설비가 가린다)", warned && burst && o4.DoseOut > 20 && lo < hi * 0.6,
                $"바깥 {o4.DoseOut:0}mGy/시 · 배 안 {lo:0.0}~{hi:0.0}mGy/시");
            // 행성: 붉은 사막에 내리면 구멍으로 0.6kPa까지만 빠지고 바깥은 춥다 (방 하나 — 옆 방이 문틈으로 공기를 대 주지 않게)
            var l5 = Worlds.Ascii(Worlds.Sealed);
            var s5 = Worlds.Full(l5, 3);
            var o5 = s5.Get<Outside>(); var f5 = s5.Get<Field>();
            o5.Land("marslike", s5.Tick);
            s5.Run(5);
            f5.SetHole(Worlds.At(l5, 0, 3), 0.3f);
            s5.Run(Time.Minutes(20));
            int room = Worlds.At(l5, 4, 3);
            if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1")
                foreach (var k in f5.Links.Where(k => k.RB < 0)) Console.WriteLine($"    바깥 틈 {k.Kind} 칸 {k.A} 넓이 {k.Area} · 이산화탄소 {f5.Ppm(f5.T.gCO2, k.A):0}ppm · {f5.Pressure(k.A):0.00}kPa · 흐름 {k.Flow:0.000}");
            Check("붉은 사막에 내린 배: 구멍 난 방은 바깥 기압(0.6kPa) 근처까지만 빠지고 이산화탄소가 들어온다", f5.Pressure(room) < 3 && f5.Ppm(f5.T.gCO2, room) > 10000,
                $"{f5.Pressure(room):0.00}kPa · 이산화탄소 {f5.Ppm(f5.T.gCO2, room) / 1e4:0}% · 바깥 {f5.Outside.Temp:0}K");
            // 끓는 행성: 외판이 바깥 압력에 짓눌린다
            var s6 = ShipWorld.Create("Didim", 3);
            var o6 = s6.Get<Outside>(); var st6 = s6.Get<Structure>(); var f6 = s6.Get<Field>();
            o6.Land("venuslike", s6.Tick);
            s6.Run(4);   // 외판이 터지고 450K를 넘는 때(2틱)의 두 배 (예전 2분 — 무거운 것은 터지는 두 틱의 물체 셈)
            double hot = Enumerable.Range(0, f6.Cells).Where(i => f6.RegionOf[i] >= 0).Max(i => f6.Temp(i));
            Check("끓는 행성(92기압 · 735K)에 내리면 외판이 짓눌려 터지고 뜨거운 이산화탄소가 밀려든다", st6.Breaches > 0 && hot > 450,
                $"터진 벽 {st6.Breaches} · 배 안 가장 뜨거운 곳 {hot:0}K");
        }

        if (Do("망"))
        {
            // 평시 정전 없음 · 방 온도 · 원자로는 '평시 하루' 묶음에서 (사람이 탄 하루 끝에 — RulesDay.cs)

            // 냉각 펌프를 끄면 원자로가 뜨거워져 스스로 멈춘다 → 배터리 → 결국 어둠
            var s2 = ShipWorld.Create("Didim", 5);
            var n2 = s2.Get<Network>();
            for (int d = 0; d < n2.Devices.Count; d++) if (n2.Devices[d].Def.Role == "pump") n2.SetOn(d, false);
            long scramAt = -1;
            for (int i = 0; i < Time.Hours(12) && scramAt < 0; i++) { s2.Step(); if (n2.ReactorScram) scramAt = s2.Tick; }
            double battAfter = n2.Devices.Where(d => d.Def.Role == "battery").Sum(d => n2.Charge[d.Id]);
            Check("냉각 펌프가 모두 서면 원자로가 뜨거워져 스스로 멈춘다 (스크램) — 그 뒤는 배터리", scramAt > 0 && n2.Supplied > 0,
                $"스크램 {(scramAt > 0 ? Time.Clock(scramAt) : "없음")} · 배터리 {battAfter:0.0}kWh · 원인 {s2.Causes.Story(n2.ScramCause)}");

            // 합선: 전선이 지나는 칸이 젖으면 → 차단기 → 그 방이 어둡다
            var s3 = ShipWorld.Create("Didim", 5);
            var n3 = s3.Get<Network>(); var f3 = s3.Get<Field>(); var li3 = s3.Get<Light>();
            var fd = n3.Feeders.First(x => x.Path.Length > 4);
            f3.AddStore(fd.Path[fd.Path.Length / 2], f3.T.sWater, 400);
            s3.Run(10);
            var trip = s3.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.BreakerTrip);
            Check("전선이 지나는 바닥이 젖으면 합선 → 차단기가 떨어지고 그 방이 어두워진다", n3.Breaker[fd.Id] == 0 && li3.Lamp[fd.Room] < 50,
                $"원인 {s3.Causes.Story(trip.Cause)} · 조명 {li3.Lamp[fd.Room]:0}lx");

            // 낡아 붙은 차단기 + 합선 → 아크가 계속 → 불
            var s4 = ShipWorld.Create("Didim", 5);
            var n4 = s4.Get<Network>(); var f4 = s4.Get<Field>(); var ch4 = s4.Get<Chemistry>();
            var fd4 = n4.Feeders.First(x => x.Path.Length > 4);
            // 간선이 함께 지나지 않는 칸 (간선이 지나는 칸이면 아크가 간선까지 태워 원자로가 떨어져 나간다 — 그것은 '선과 관' 묶음에서)
            int wet = fd4.Path.Skip(fd4.Path.Length / 2).Concat(fd4.Path).First(c => !n4.Buses.Any(b => Array.IndexOf(b.Path, c) >= 0));
            n4.BreakerStuck[fd4.Id] = 1;
            f4.AddStore(wet, f4.T.sPoly, 300); // 전선 피복 · 짐
            f4.AddStore(wet, f4.T.sWater, 30);
            s4.Run(Time.Minutes(3));
            Check("차단기가 낡아 붙어 있으면 합선 아크가 계속되어 불이 난다", ch4.Ignitions > 0, $"발화 {ch4.Ignitions} · 원인 {s4.Causes.Story(ch4.FireCause[wet])}");

            // 덕트로 연기가 번진다 — 댐퍼를 닫은 방은 빠진다
            var s5 = ShipWorld.Create("Didim", 5);
            var n5 = s5.Get<Network>(); var f5 = s5.Get<Field>();
            s5.Step();
            var rooms = Enumerable.Range(0, f5.L.Rooms.Count).Where(r => n5.Vent[r] >= 0 && n5.RoomPowered(r)).ToList();
            int src = rooms[0], open = rooms[^1], shut = rooms[^2];
            n5.SetDamper(shut, false);
            f5.AddGas(n5.Vent[src], f5.T.gSoot, 40, 320);
            s5.Run(10);   // 덕트는 틱마다 통풍구 공기를 고루 돌린다 — 먼 방에 번지고 닫은 방이 빠진 때(5틱 안)의 두 배 (예전 10분)
            double ppmOpen = f5.Ppm(f5.T.gSoot, n5.Vent[open]), ppmShut = f5.Ppm(f5.T.gSoot, n5.Vent[shut]);
            Check("연기가 덕트를 타고 먼 방까지 번진다 · 댐퍼를 닫은 방은 빠진다", ppmOpen > 5 && ppmShut < ppmOpen * 0.1,
                $"열린 방 {ppmOpen:0}ppm · 닫은 방 {ppmShut:0.0}ppm");

            // 낡은 냉각 펌프 봉함 → 고장 → 암모니아가 샌다 (원인: 낡음)
            var s6 = ShipWorld.Create("Didim", 5);
            var n6 = s6.Get<Network>(); var f6 = s6.Get<Field>();
            int pump = n6.Devices.First(d => d.Def.Role == "pump").Id;
            n6.Age(pump, Part.Seal, 0.99995);
            s6.Run(Time.Minutes(70));   // 봉함이 망가져 암모니아가 25ppm을 넘는 때(35분)의 두 배 (예전 3시간)
            int pc = n6.Devices[pump].Cell;
            var fail = s6.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.MachineFail && x.Other == pump);
            Check("낡은 냉각 펌프 봉함이 망가지면 암모니아가 그 방으로 샌다 (확률이 아니라 마모에서)", n6.IsBroken(pump, Part.Seal) && f6.Ppm(f6.T.gNH3, pc) > 25,
                $"암모니아 {f6.Ppm(f6.T.gNH3, pc):0}ppm · 원인 {s6.Causes.Story(fail.Cause)}");

            // 곧 망가질 모터는 끽끽거린다
            var s7 = ShipWorld.Create("Didim", 5);
            var n7 = s7.Get<Network>(); var snd7 = s7.Get<Sound>();
            int pump7 = n7.Devices.First(d => d.Def.Role == "pump").Id;
            n7.Age(pump7, Part.Mechanical, 0.9);
            bool squeal = false;
            for (int i = 0; i < Time.Minutes(30) && !squeal; i++) { s7.Step(); squeal = snd7.At(n7.Devices[pump7].Cell).Any(h => h.Kind == SoundKind.Machine); }
            Check("곧 망가질 모터는 끽끽 소리를 낸다 (알아챌 수 있는 조짐)", squeal);

            // 뜨거워진 배터리는 열폭주로 불이 난다
            var s8 = ShipWorld.Create("Didim", 5);
            var n8 = s8.Get<Network>(); var f8 = s8.Get<Field>(); var ch8 = s8.Get<Chemistry>();
            var bat = n8.Devices.First(d => d.Def.Role == "battery");
            for (int i = 0; i < Time.Minutes(30) && !n8.IsBroken(bat.Id, Part.Electric); i++) { f8.Tf[bat.Cell] = Math.Max(f8.Tf[bat.Cell], 650); s8.Step(); }   // 불 곁에서 계속 달궈진다 (셀 묶음이 데워지는 데 몇 분 — ② (라))
            s8.Run(Time.Minutes(0.5));
            Check("뜨거워진 배터리는 열폭주로 불이 난다", ch8.Ignitions > 0 && n8.IsBroken(bat.Id, Part.Electric), $"발화 {ch8.Ignitions}");

            // 산소 발생기: 배 공기의 산소가 모자라면(5% 뺌 → 19.9kPa) 물을 갈라 산소를 낸다 · 물탱크가 준다 (넉넉하면 쉰다 — ⑪ 물자의 산소 조절 시험)
            var s9 = ShipWorld.Create("Didim", 5);
            var n9 = s9.Get<Network>(); var f9 = s9.Get<Field>();
            for (int c = 0; c < f9.Cells; c++) f9.RemoveGas(c, f9.T.gO2, f9.Gas(f9.T.gO2, c) * 0.05);
            double o2a = f9.Total(f9.T.gO2), w0 = n9.WaterTank;
            s9.Run(Time.Minutes(26));   // 산소가 늘고 · 물이 줄고 · 수소를 뺀 때(13분)의 두 배 (예전 2시간)
            Check("산소가 모자라면 산소 발생기가 물을 갈라 산소를 낸다 (물탱크가 준다 · 수소는 배 밖으로)", f9.Total(f9.T.gO2) > o2a && n9.WaterTank < w0 && n9.VentedH2 > 0,
                $"산소 {o2a:0} → {f9.Total(f9.T.gO2):0}몰 · 물 {w0 / 55.5:0}L → {n9.WaterTank / 55.5:0}L · 분압 {n9.PpO2:0.00}kPa");
        }

        if (Do("생명"))
        {
            var lay = Worlds.Ascii(Worlds.Sealed);
            Sim World(Action<Sim>? setup = null) { var w = Worlds.Full(lay, 1); setup?.Invoke(w); return w; }
            int a = Worlds.At(lay, 4, 3), hull = Worlds.At(lay, 0, 3);

            // 진공: 15초면 쓰러지고 1분 반쯤에 죽는다 · 죽음의 원인 사슬이 파편까지
            var s = World();
            var life = s.Get<Life>(); int b = life.AddBody(Worlds.At(lay, 4, 1));
            int seed = s.Causes.Add(CauseKind.Setup, "파편 하나가 날아왔다", s.Tick);
            s.Get<Structure>().Impact(s, hull, 4e5, 1, 0, seed);
            long downAt = -1, deadAt = -1;
            for (int i = 0; i < Time.Minutes(6) && deadAt < 0; i++)
            {
                s.Step();
                if (downAt < 0 && life[b].Conscious == 0) downAt = s.Tick;
                if (life[b].Alive == 0) deadAt = s.Tick;
            }
            Check("감압된 방의 사람: 곧 쓰러지고 몇 분 안에 죽는다 · 죽음의 원인이 파편까지 이어진다",
                downAt > 0 && deadAt > downAt && Time.ToSeconds(deadAt) < 300 && s.Causes.Story(life[b].DeathCause).StartsWith("파편 하나가 날아왔다"),
                $"쓰러짐 {Time.ToSeconds(downAt):0}초 · 죽음 {Time.ToSeconds(deadAt):0}초 · {s.Causes.Story(life[b].DeathCause)}");
            var sb = World(); var lb = sb.Get<Life>(); int bb = lb.AddBody(Worlds.At(lay, 4, 1));
            sb.Run(5);
            sb.Get<Field>().SetHole(hull, 2.5f);
            sb.Run(10);
            Check("외판이 통째로 뚫려 몇 초 만에 빠지면 폐를 다친다 (급감압)", lb[bb].Barotrauma > 0, $"{lb[bb].Barotrauma:0.00}");

            // 저산소: 산소 분압 8kPa 방은 몇 분 안에 쓰러진다 · 15kPa는 버틴다
            Sim LowO2(double kPa)
            {
                return World(w =>
                {
                    var f = w.Get<Field>();
                    foreach (int c in lay.Rooms.First(r => r.Cells.Contains(a)).Cells)
                    {
                        double n = f.Moles(c), o2 = f.Gas(f.T.gO2, c), want = n * kPa / f.Pressure(c);
                        f.RemoveGas(c, f.T.gO2, Math.Max(0, o2 - want));
                        f.AddGas(c, f.T.gN2, Math.Max(0, o2 - want), f.Temp(c));
                    }
                    var net = w.Get<Network>(); for (int d = 0; d < net.Devices.Count; d++) if (net.Devices[d].Def.Role == "o2gen") net.SetOn(d, false);
                });
            }
            var s2 = LowO2(8); var l2 = s2.Get<Life>(); int b2 = l2.AddBody(a);
            s2.Run(Time.Minutes(8));
            var s3 = LowO2(15); var l3 = s3.Get<Life>(); int b3 = l3.AddBody(a);
            s3.Run(Time.Hours(1));
            Check("산소 분압 8kPa(해발 7천m 꼴)에서는 몇 분 안에 쓰러진다 · 15kPa는 버틴다", l2[b2].Conscious == 0 && l3[b3].Conscious == 1,
                $"8kPa: 포화도 {l2[b2].SpO2:0.00} · 15kPa: 포화도 {l3[b3].SpO2:0.00}");

            // 일산화탄소 3000ppm: 일하는 사람은 한두 시간 안에 쓰러진다
            var s4 = World(w => { var f = w.Get<Field>(); foreach (int c in lay.Rooms.First(r => r.Cells.Contains(a)).Cells) f.AddGas(c, f.T.gCO, f.Moles(c) * 0.003, f.Temp(c)); });
            var l4 = s4.Get<Life>(); int b4 = l4.AddBody(a); l4[b4].ActivityW = 250;
            long co = -1;
            for (int i = 0; i < Time.Hours(3) && co < 0; i++) { s4.Step(); if (l4[b4].Conscious == 0) co = s4.Tick; }
            Check("일산화탄소 3000ppm 속에서 일하면 한두 시간 안에 쓰러진다 (헤모글로빈이 막힌다)", co > 0 && co < Time.Hours(2.5), $"{(co > 0 ? Time.Clock(co) : "버팀")} · COHb {l4[b4].COHb:0.00}");

            // 불 곁: 화상 · 원인이 불
            var lounge = Worlds.Ascii(Worlds.Lounge, Worlds.Seats);
            var s5 = Worlds.Full(lounge, 1);
            var l5 = s5.Get<Life>(); int b5 = l5.AddBody(Worlds.At(lounge, 3, 2));
            s5.Get<Chemistry>().Ignite(Worlds.At(lounge, 3, 3), 3e6, s5.Causes.Add(CauseKind.Person, "누가 담배를 떨어뜨렸다", 0));
            s5.Run(Time.Minutes(5));
            Check("불 붙은 의자 곁에 있으면 데고, 그 원인이 불까지 이어진다", l5[b5].Burn > 0.2 && s5.Causes.Story(l5[b5].HurtCause).Contains("담배"),
                $"화상 {l5[b5].Burn:0.00} · {s5.Causes.Story(l5[b5].HurtCause)}");

            // 굶주림 · 물: 하루에 몸에 쌓인 에너지(포도당 + 지방) 약 8MJ · 물 1L 넘게 — 쉴 때는 지방이 연료의 절반 넘게 (⑦)
            var s6 = World(); var l6 = s6.Get<Life>(); int b6 = l6.AddBody(a);
            double Store(int b) => (l6[b].Reserve * Life.GlucoseJPerO2 * 6 + l6[b].Fat * Life.FatJPerO2 * 23) / 1e6;
            double e0 = Store(b6), r0 = l6[b6].Reserve, f0 = l6[b6].Fat, w0 = l6[b6].Water;
            s6.Run(Time.Days(1));
            double used = e0 - Store(b6);
            Check("하루 동안 몸에 쌓인 에너지(포도당 + 지방, 약 8MJ)와 물(1L 넘게)이 준다", used > 5.6 && used < 16.8 && f0 > l6[b6].Fat && (w0 - l6[b6].Water) / 55.5 > 0.9,
                $"에너지 −{used:0.0}MJ (포도당 −{r0 - l6[b6].Reserve:0.0}몰 · 지방 −{(f0 - l6[b6].Fat) * 0.256:0.00}kg) · 물 −{(w0 - l6[b6].Water) / 55.5:0.0}L · 체온 {l6[b6].CoreK - 273.15:0.0}℃");

            // 방사선: 폭발 때 바깥쪽 칸이 안쪽보다 많이 맞는다
            var s7 = ShipWorld.Create("Salpim", 2);
            var o7 = s7.Get<Outside>(); var f7 = s7.Get<Field>(); var l7 = s7.Get<Life>();
            o7.Star = "blue"; o7.ScheduleFlare(s7.Tick, 1, 0);
            var floor = Enumerable.Range(0, f7.Cells).Where(i => f7.L.Kind[i] == CellKind.Floor && f7.L.FurnitureAt[i] < 0).ToList();
            s7.Run(Time.Minutes(50));
            int outer = floor.OrderByDescending(i => o7.Dose[i]).First(), inner = floor.OrderBy(i => o7.Dose[i]).First();
            int bo = l7.AddBody(outer), bi = l7.AddBody(inner);
            s7.Run(Time.Hours(1));
            Check("별 폭발 동안 바깥쪽 칸의 사람이 안쪽(가려진 곳)보다 훨씬 많이 맞는다", l7[bo].RadGy > l7[bi].RadGy * 2 && l7[bo].RadGy > 0.02, // 한 시간 — 폭발이 실제로 났는지만 (20mGy)
                $"바깥쪽 {l7[bo].RadGy * 1000:0}mGy · 안쪽 {l7[bi].RadGy * 1000:0}mGy");

            // 작물: 재배등이 켜진 재배대는 자라고 산소를 낸다 · 꺼진 것은 안 자란다
            var s8 = ShipWorld.Create("Didim", 2);
            var n8 = s8.Get<Network>(); var l8 = s8.Get<Life>();
            int off = l8.Crops[0].Device;
            n8.SetOn(off, false);
            var start8 = l8.Crops.Select(c => c.Biomass).ToArray();   // 출항 때 재배대마다 자람 단계가 다르다 (DF6) — 두 시간에 자란 몫을 본다
            s8.Run(Time.Hours(2)); // 자라는 빠르기는 늘 같은 틀 — 반나절을 기다리지 않는다
            var lit = Enumerable.Range(1, l8.Crops.Count - 1).Average(k => l8.Crops[k].Biomass - start8[k]);
            float offGrew = l8.Crops[0].Biomass - start8[0];
            double ideal = Enumerable.Range(1, l8.Crops.Count - 1).Average(k => l8.CropArea(n8, k)) * Life.CropMolPerM2Day / 12;
            // 실제 수확량: 1m²에 하루 0.1몰 (먹는 부분 18g — NASA BPC) → 두 시간에 재배대 넓이 × 0.0083몰이 다 자란 몫
            Check("재배등이 켜진 재배대는 자라고(1m²에 하루 0.1몰 — 실제 수확량 · 사람이 없어 이산화탄소가 모자라 그 몇 할) · 꺼진 것은 거의 안 자란다 (두 시간)", lit > ideal * 0.25 && lit <= ideal * 1.0001 && offGrew < lit * 0.3,
                $"켜진 것 평균 {lit:0.000}몰 (다 자라면 {ideal:0.000}) · 꺼진 것 {offGrew:0.000}몰 · 재배대 {l8.Crops.Count}");

            // 곰팡이: 습하고 따뜻한 방에서 자란다
            var s9 = World(w => { var f = w.Get<Field>(); f.Adiabatic = true; /* 난방 장치가 없는 글자 도면 — 얼지 않게 */ foreach (int c in lay.Rooms.First(r => r.Cells.Contains(a)).Cells) { f.AddStore(c, f.T.sWater, 80); f.AddGas(c, f.T.gH2O, 1.5, f.Temp(c)); } var net = w.Get<Network>(); for (int d = 0; d < net.Devices.Count; d++) if (net.Devices[d].Def.Role == "dehumid") net.SetOn(d, false); });
            var l9 = s9.Get<Life>(); var f9 = s9.Get<Field>();
            s9.Run(Time.Days(3));
            if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1")
                Console.WriteLine($"    곰팡이 칸: 습도 {f9.Partial(f9.T.gH2O, a) / Water.SatKPa(f9.Temp(a)):0.00} · 물 {f9.Stored(f9.T.sWater, a):0} · {f9.Temp(a):0}K · 구역 {f9.RegionOf[a]}");
            Check("물이 고이고 습한 방에는 곰팡이가 핀다 · 마른 방에는 없다", l9.Mold[a] > 0.02 && l9.Mold[Worlds.At(lay, 15, 3)] == 0,
                $"젖은 방 {l9.Mold[a]:0.000} · 마른 방 {l9.Mold[Worlds.At(lay, 15, 3)]:0.000}");

            // 음식 상함: 산소 없는 곳에서는 메탄이 생긴다
            var s10 = new Sim(new SimSetup(Worlds.Ascii(Worlds.Symmetric), 1, sim => { var ff = new Field(sim.Layout, sim.Tables); sim.Add(ff); sim.Add(new Life(ff)); }));
            var f10 = s10.Get<Field>(); f10.FillAir();
            int mid = Worlds.At(f10.L, 5, 3);
            for (int i = 0; i < f10.Cells; i++) if (f10.L.Kind[i] == CellKind.Floor) { double o = f10.Gas(f10.T.gO2, i); f10.RemoveGas(i, f10.T.gO2, o); f10.AddGas(i, f10.T.gN2, o, 293); f10.AddGas(i, f10.T.gH2O, 1.5, 293); }
            f10.AddStore(mid, f10.T.sFood, 100);
            s10.Run(Time.Hours(8));   // 메탄이 0.5몰을 넘는 때(4시간)의 두 배 (예전 나흘)
            Check("산소 없는 창고의 음식은 썩으며 메탄을 낸다 (불이 붙을 수 있는 기체)", f10.Total(f10.T.gCH4) > 0.5, $"메탄 {f10.Total(f10.T.gCH4):0.0}몰 · 남은 음식 {f10.Stored(f10.T.sFood, mid):0.0}몰");

            // 충격: 파편이 지나간 칸의 사람은 다친다
            var s11 = World(); var l11 = s11.Get<Life>(); int b11 = l11.AddBody(Worlds.At(lay, 1, 3));
            s11.Get<Structure>().Impact(s11, hull, 60_000, 1, 0, 0);
            s11.Run(10);
            Check("파편이 뚫고 들어온 칸의 사람은 다치고 피를 흘린다", l11[b11].Blunt > 0 && l11[b11].BloodL < 5, $"충격 {l11[b11].Blunt:0.00} · 피 {l11[b11].BloodL:0.00}L");

            // 암모니아: 짙기만큼 — 500ppm 10분은 아프지만 버티고 · 1만ppm은 몇 분 안에 쓰러진다
            (bool down, float lung, float sat) Ammonia(double ppm, long ticks)
            {
                var sa = new Sim(new SimSetup(Worlds.Ascii(Worlds.Symmetric), 1, sim => { var ff = new Field(sim.Layout, sim.Tables); sim.Add(ff); sim.Add(new Life(ff)); }));
                var fa = sa.Get<Field>(); var la = sa.Get<Life>(); fa.FillAir();
                int ba = la.AddBody(Worlds.At(fa.L, 5, 3));
                bool down = false;
                for (long k = 0; k < ticks; k++)
                {
                    for (int i = 0; i < fa.Cells; i++)
                    {
                        if (fa.L.Kind[i] != CellKind.Floor) continue;
                        double want = ppm * 1e-6 * fa.Moles(i), have = fa.Gas(fa.T.gNH3, i);
                        if (want > have) fa.AddGas(i, fa.T.gNH3, want - have, fa.Temp(i));
                    }
                    sa.Step();
                    down |= la[ba].Conscious == 0 || la[ba].Alive == 0;
                }
                return (down, la[ba].LungDamage, la[ba].SpO2);
            }
            var a500 = Ammonia(500, Time.Minutes(10));
            var a10k = Ammonia(10000, Time.Minutes(10));
            Check("암모니아: 500ppm 10분은 목이 아파도 버틴다 · 1만ppm 10분이면 쓰러진다 (짙기만큼 폐가 상한다)", !a500.down && a500.sat > 0.85f && a10k.down,
                $"500ppm 폐 {a500.lung:0.00} · 산소 포화 {a500.sat:0.00} · 쓰러짐 {a500.down} / 1만ppm 폐 {a10k.lung:0.00} · 쓰러짐 {a10k.down}");

            // 몸의 열: 쓰러진 몸은 기초 대사로 (달리던 대사량이 남아 스스로 익지 않는다) · 우주복은 냉각 속옷으로 열을 뺀다 (산소가 남는 동안)
            Sim BodyWorld() { var sw = new Sim(new SimSetup(Worlds.Ascii(Worlds.Symmetric), 1, sim => { var ff = new Field(sim.Layout, sim.Tables); sim.Add(ff); sim.Add(new Life(ff)); })); sw.Get<Field>().FillAir(); return sw; }
            var sh = BodyWorld(); var lh = sh.Get<Life>(); int bh = lh.AddBody(Worlds.At(sh.Layout, 5, 3));
            lh[bh].ActivityW = 700; lh[bh].Hypoxia = 0.6f;
            sh.Run(10);
            float downW = lh[bh].ActivityW; bool wasDown = lh[bh].Conscious == 0;
            var ss = BodyWorld(); var ls = ss.Get<Life>(); int bs = ls.AddBody(Worlds.At(ss.Layout, 5, 3));
            ls[bs].Suit = 1; ls[bs].SuitO2 = 500;
            for (long k = 0; k < Time.Hours(2); k++) { ls[bs].ActivityW = 250; ss.Step(); }
            double suitCore = ls[bs].CoreK - 273.15;
            Check("쓰러진 몸은 기초 대사(80W)로 떨어진다 · 우주복을 입고 2시간 일해도 냉각 속옷이 체온을 지킨다 (38.5℃ 아래)", wasDown && downW <= 90 && suitCore < 38.5,
                $"쓰러짐 {wasDown} · 대사 700 → {downW:0}W · 우주복 속 체온 {suitCore:0.0}℃");

            // 산소 요법: 연기에 폐가 상한 사람(손상 0.45)은 맑은 공기에서도 숨이 모자라 쓰러진다 — 치료 침대의 산소 마스크면 버틴다
            (bool alive, float sat) Lungs(bool onBed)
            {
                var so = ShipWorld.Create("Hwasal", 8);
                var lo = so.Get<Life>();
                var bedCell = so.Layout.Furniture.First(fu => fu.Type == "MedBed").Cells[0];
                int floor = CommonRoom(so.Layout).Cells[0];
                int bo = lo.AddBody(onBed ? bedCell : floor);
                lo[bo].LungDamage = 0.45f;
                for (long k = 0; k < Time.Minutes(20); k++) { lo[bo].LungDamage = Math.Max(lo[bo].LungDamage, 0.45f); so.Step(); }
                return (lo[bo].Alive != 0 && lo[bo].Conscious != 0, lo[bo].SpO2);
            }
            var lungFloor = Lungs(false); var lungBed = Lungs(true);
            Check("연기에 상한 폐(손상 0.45): 맑은 공기에서도 쓰러진다 · 치료 침대 산소 마스크면 버틴다", !lungFloor.alive && lungBed.alive,
                $"바닥 — 깨어 있음 {lungFloor.alive} · 포화 {lungFloor.sat:0.00} / 치료 침대 — 깨어 있음 {lungBed.alive} · 포화 {lungBed.sat:0.00}");
        }

        if (Do("빛"))
        {
            var lay = Worlds.Ascii(Worlds.Lounge, Worlds.Seats);
            var s = Worlds.Full(lay, 1, sim => sim.Get<Chemistry>().Ignite(Worlds.At(lay, 3, 3), 3e6, 0));
            var li = s.Get<Light>();
            for (int r = 0; r < li.Lamp.Length; r++) li.SetLamp(r, 0); // 정전
            s.Run(Time.Minutes(3));
            int near = Worlds.At(lay, 3, 2), far = Worlds.At(lay, 14, 1);
            Check("정전된 방에서도 불꽃은 곁을 밝힌다 · 먼 구석은 어둡다", li.Lux[near] > 500 && li.Lux[far] < 50, $"곁 {li.Lux[near]:0}lx · 구석 {li.Lux[far]:0}lx");
            Check("연기 속에서는 보이는 거리가 짧다", li.Visibility(near) < li.Visibility(far) && li.Visibility(near) < 15, $"곁 {li.Visibility(near):0.0}m · 구석 {li.Visibility(far):0.0}m");
        }

        if (Do("차압문"))
        {
            var l = Worlds.Ascii(Worlds.TwoRooms);
            var s = Worlds.FieldOnly(l, 1, f => f.SetHole(Worlds.At(f.L, 10, 9), 2.5f)); // 통로 아래 외판이 뚫렸다
            s.Run(Time.Minutes(10));
            var f = s.Get<Field>();
            double left = f.Pressure(Worlds.At(l, 3, 2)), right = f.Pressure(Worlds.At(l, 15, 2));
            Check("통로가 뚫리면 통로는 진공 · 보통 문틈 너머 방은 천천히 샌다 (10분에 4분의 1 아래) · 차압 문으로만 통로와 닿은 방은 더 천천히",
                f.Pressure(Worlds.At(l, 10, 7)) < 1 && right > 75 && left > right,
                $"왼쪽 방 {f.Pressure(Worlds.At(l, 3, 2)):0.0} · 오른쪽 방 {f.Pressure(Worlds.At(l, 15, 2)):0.0} · 통로 {f.Pressure(Worlds.At(l, 10, 7)):0.00} kPa");
        }

        MindRules();
        ComputerRules();
        StoryRules();
        SocietyRules();
        WorkRules();
        ViewRules();
        SoundRules();
        LifeRules(); LedgerRules(); PersonRules(); BondRules(); SeedRules(); ShipRules(); DayRules(); SootRules(); RadiantRules(); FlingRules(); VoyageRules(); SensorRules(); JournalRules(); LeakJudgeRules(); HeartRules(); BandRules(); DoorRequestRules(); CookRules(); RhythmRules(); TogetherRules(); PlaceRules(); AskRules(); SupplyRules(); CareRules(); BleedRules(); CameraRules(); HearingRules(); MotionRules(); NetRules(); TextRules(); AirlockRules(); AimRules(); CrackRules(); LieRules(); BreathRules(); LessonRules(); WeatherRules(); NatureRules(); MachineRules(); SupplyChainRules(); CommsRules(); HeatVibRules(); EyeRules(); DressUseRules(); WashRules(); WashSoundRules(); WashFlowSoundRules(); WashAccidentRules(); DoorwayRules(); WashCareRules(); LifeSoundRules(); EarRules(); MicRules(); MusicRules(); SoundWorldRules(); MapRules(); LongArcRules(); ChapterRules(); LocalRules(); LightRules(); FxRules(); PeopleArtRules(); TraceRules(); CouncilRules(); JudgmentRules(); WearyRules(); SnagRules(); GaugeRules(); ScrubRules(); MindFixRules(); HandsRules(); AssessRules(); JudgedRules(); HudRules(); ArtRules(); FarRules(); ManualRules(); LineRules(); WhyRules(); BodyRules(); PropRules(); MishapRules(); SpillRules(); GermRules(); ToxRules(); GravityRules(); OrganRules(); DxRules(); MentalRules(); CultureRules(); SpeedRules(); HandRules(); HandViewRules(); LookRules(); DetailRules(); ForecastRules(); FinishRules(); Depth1Rules(); Depth2Rules(); Depth3Rules(); Physics2Rules(); ResumeRules(); VoyageNumberRules(); RecordRules(); VaultRules();
    }
}
