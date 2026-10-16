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
                foreach (int c in ff.L.Rooms.First(r => r.Cells.Contains(Worlds.At(ff.L, 3, 2))).Cells) ff.AddGas(c, ff.T.gH2, 14, 293);
            });
            s5.Run(3);
            s5.Get<Chemistry>().Ignite(Worlds.At(l5, 3, 2), 5e4, 0);
            s5.Run(30);
            var st5 = s5.Get<Structure>();
            Check("수소 폭연이 보통 문을 날린다 (압력이 정격을 넘는다)", st5.DoorsBlown > 0 && s5.Get<Field>().DoorOpen[0] > 0,
                $"날아간 문 {st5.DoorsBlown} · 문 열림 {s5.Get<Field>().DoorOpen[0]:0.0} · 큰 소리 {s5.Facts.CountOf(FactKind.LoudSound)}번");
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
    }
}
