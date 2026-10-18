using ShipEngine;

namespace Lab;

/// <summary>숨 쉴 공기 (⑦ · ⑧ · ⑩): 산소 마스크 · 주컴퓨터의 긴급 공기 · 옅은 공기에 쓰러진 원인이 구멍에 잇는다 (docs/ENGINE.md ⑦ 숨 쉴 공기).</summary>
public static partial class Program
{
    private const string BreathShip = @"
##############################
#..x.......#.......#.........#
#..........P..c.pb.P.........#
#..e.......#.......#.........#
##############################";

    private const string BreathShipSmall = @"
#####################
#..x.......#....pb..#
#..........D........#
#..e.......#...c....#
#####################";

    private static void BreathRules()
    {
        if (!Do("숨 쉴 공기")) return;
        Console.WriteLine("\n[⑦ · ⑧ · ⑩ 숨 쉴 공기]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var legend = new Dictionary<char, string> { ['x'] = "OxygenMaskBox", ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer", ['e'] = "Bed" };
        var lay = Worlds.Ascii(BreathShip, legend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        // 두 분에 걸쳐 공기를 줄인다 (한꺼번에 빼면 급감압에 폐를 다친다)
        void Thin(Sim s, Field f, List<int> cells, double keep)
        {
            double k1 = Math.Pow(keep, 1.0 / 50);
            for (int t = 0; t < 50; t++) { foreach (int c in cells) for (int g = 0; g < f.G; g++) f.RemoveGas(c, g, f.N[c * f.G + g] * (1 - k1)); s.Step(); }
        }
        IEnumerable<int> Room(Sim s, int x, int y) => s.Layout.Rooms[s.Layout.Room[At(x, y)]].Cells;

        // ── 산소 마스크: 배 전체가 45kPa (좋은 공기가 어디에도 없다) — 피의 산소가 떨어져 어지러운 이는 가까운 마스크함에서 마스크를 쓰고 산다 / 마스크가 없으면 30분 안에 쓰러진다 ──
        {
            (bool masked, float spo2, bool up, string why) Run(bool masks)
            {
                var s = Worlds.Full(lay, 3);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var f = s.Get<Field>(); var net = s.Get<Network>();
                net.AirReserve = 0;   // 예비 공기 없음 — 주컴퓨터가 채우지 못한다
                int p = m.AddPerson(s, At(6, 2));
                m.SetPersonality(p, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f);
                if (!masks) for (int fu = 0; fu < s.Layout.Furniture.Count; fu++) m.Stock[fu * m.ItemCount + m.iMask] = 0;
                s.Run(2);
                Thin(s, f, Enumerable.Range(0, s.Layout.N).Where(c => s.Layout.IsSpace(c)).ToList(), 0.45);
                bool masked = false;
                for (int k = 0; k < Time.Minutes(30); k++) { s.Step(); masked |= life[m[p].C.Body].Mask != 0; }
                ref var b = ref life[m[p].C.Body];
                return (masked, b.SpO2, b.Conscious != 0 && b.Alive != 0, $"{m[p].C.Goal} · 포화도 {b.SpO2:0.00} · 저산소 {b.Hypoxia:0.00} · 마스크 {b.Mask}/{b.MaskO2:0.0}몰 · {f.Pressure(b.Cell):0}kPa · 산소 {f.Partial(f.T.gO2, b.Cell):0.0}");
            }
            var noneT = Task.Run(() => Run(false)); var yes = Run(true); var none = noneT.Result;
            Check("산소 마스크: 배 전체가 45kPa — 어지러운 이는 가까운 마스크함에서 마스크를 쓰고 산다 (피의 산소 0.9 넘게) / 마스크가 없으면 30분 안에 쓰러진다",
                yes.masked && yes.spo2 > 0.9f && yes.up && !none.up,
                $"마스크 있음: 씀 {yes.masked} · {yes.why} / 없음: {none.why} · 깨어 있음 {none.up}");
        }

        // ── 긴급 공기: 오른쪽 방이 뚫려 배 전체 예비 공기는 잠겼다 · 문이 닫힌 왼쪽 방이 45kPa로 옅고 (산소 9kPa) 그 방에 사람이 자고 있으면 주컴퓨터가 예비 공기를 그 방에 바로 넣는다 / 사람이 없으면 넣지 않는다 ──
        {
            (float o2, long pumped, double used) Run(bool person)
            {
                var s = Worlds.Full(lay, 4);
                var m = s.Get<Mind>(); var f = s.Get<Field>(); var net = s.Get<Network>(); var sm = s.Get<ShipMind>();
                int p = person ? m.AddPerson(s, At(3, 3)) : -1;
                for (int fu = 0; fu < s.Layout.Furniture.Count; fu++) m.Stock[fu * m.ItemCount + m.iMask] = 0;   // 마스크로 버티지 않게
                int door = s.Layout.DoorAt[At(11, 2)];
                f.SetDoor(door, 0); s.Get<Structure>().RequestDoor(door, 0);
                if (person) { s.Get<Life>()[m[p].C.Body].Asleep = 1; m[p].C.Sleepy = 1; }   // 침실에서 자고 있다 (감압 사고의 흔한 장면)
                s.Run(30);
                net.AirReserve = 20000;          // 예비 공기는 넉넉하다
                f.SetHole(At(29, 2), 0.01f);     // 오른쪽 방은 뚫렸다 — 주컴퓨터가 배 전체 예비 공기는 잠근다
                double r0 = net.AirReserve;
                Thin(s, f, Room(s, 6, 2).ToList(), 0.45);
                s.Run(Time.Minutes(20));
                if (Debug) { foreach (var (t, x) in sm.Log.TakeLast(6)) Console.WriteLine($"    {Time.Clock(t)} {x}"); Console.WriteLine($"    왼쪽 {Room(s, 6, 2).Average(c => f.Pressure(c)):0.0}kPa · 가운데 {Room(s, 15, 1).Average(c => f.Pressure(c)):0.0}kPa · 오른쪽 {Room(s, 24, 2).Average(c => f.Pressure(c)):0.0}kPa · 문 {f.DoorOpen[s.Layout.DoorAt[At(11, 2)]]:0.00} · 왼쪽 방 번호 {s.Layout.Room[At(6, 2)]} · 사람 {(p >= 0 ? m.RoomOf(m[p].C.Cell) : -1)}"); }
                float o2 = (float)Room(s, 6, 2).Average(c => f.Partial(f.T.gO2, c));
                return (o2, sm.EmergencyAir, r0 - net.AirReserve);
            }
            var awayT = Task.Run(() => Run(false)); var here = Run(true); var away = awayT.Result;
            Check("긴급 공기: 옆 방이 뚫려 배 전체 예비 공기는 잠겼어도 · 문이 닫힌 방이 45kPa로 옅고 그 방에 사람이 자고 있으면 주컴퓨터가 예비 공기를 그 방에 바로 넣는다 (300몰 넘게 — 깬 사람이 좋은 공기로 나가면 그친다) / 사람이 없으면 넣지 않는다",
                here.pumped >= 1 && here.used > 300 && away.pumped == 0 && away.o2 < 10,
                $"사람 있음: 넣음 {here.pumped} · 산소 {here.o2:0.0}kPa · 쓴 예비 {here.used:0}몰 / 없음: 넣음 {away.pumped} · 산소 {away.o2:0.0}kPa");
        }

        // ── 원인: 구멍으로 공기를 잃고 막은 뒤에도 옅은 공기에 쓰러진 이의 원인 사슬이 그 구멍에 잇는다 (고치기 전: '산소가 모자랐다'에 뿌리가 없었다) ──
        {
            var small = Worlds.Ascii(BreathShipSmall, legend);
            int At2(int x, int y) => Worlds.At(small, x, y);
            IEnumerable<int> Room2(Sim s, int x, int y) => s.Layout.Rooms[s.Layout.Room[At2(x, y)]].Cells;
            var s = Worlds.Full(small, 5);
            var m = s.Get<Mind>(); var f = s.Get<Field>(); var net = s.Get<Network>(); var st = s.Get<Structure>(); var life = s.Get<Life>();
            net.AirReserve = 0;
            int p = m.AddPerson(s, At2(6, 2));
            for (int fu = 0; fu < s.Layout.Furniture.Count; fu++) m.Stock[fu * m.ItemCount + m.iMask] = 0;
            int door3 = s.Layout.DoorAt[At2(11, 2)];
            f.SetDoor(door3, 0); st.RequestDoor(door3, 0); st.JamDoor(s, door3, 0);   // 닫힌 채 끼었다 — 나갈 수 없다
            s.Run(5);
            int hull = At2(0, 2);
            int rock = s.Causes.Add(CauseKind.Impact, "작은 운석이 외판을 뚫었다", s.Tick, hull);
            st.DamageCause[hull] = rock;
            f.SetHole(hull, 0.002f);
            long f0 = s.Facts.Total;
            for (int k = 0; k < Time.Minutes(10) && Room2(s, 6, 2).Average(c => f.Pressure(c)) > 36; k++) s.Step();
            f.SetHole(hull, 0);   // 막았다 — 그래도 공기는 옅다
            var down = default(Fact);
            for (int k = 0; k < Time.Hours(2) && down.Kind == FactKind.None; k++)
            {
                s.Step();
                down = s.Facts.Since(f0).FirstOrDefault(x => x.Kind == FactKind.Collapsed && x.Actor == m[p].C.Body);
            }
            bool linked = down.Kind == FactKind.Collapsed && s.Causes.Chain(down.Cause).Contains(rock);
            if (Debug) Console.WriteLine($"    {(down.Kind != FactKind.None ? s.Causes.Story(down.Cause) : "쓰러지지 않음")} · 기압 {Room2(s, 6, 2).Average(c => f.Pressure(c)):0}kPa");
            Check("원인: 구멍으로 공기를 잃고 막은 뒤에도 옅은 공기에 쓰러진 이의 원인 사슬이 그 구멍에 잇는다",
                linked, down.Kind != FactKind.None ? s.Causes.Story(down.Cause) : "쓰러지지 않음");
        }
    }
}
