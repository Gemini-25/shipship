using ShipEngine;

namespace Lab;

/// <summary>
/// ⑥ 바퀴 3 (가) 칸마다 선 · 관 (docs/ENGINE.md ⑥ '바퀴 3 설계'): 가지선 · 간선 섬 · 냉각관 조각 · 물관 · 탄 전선은 이어야 한다 · 찢긴 냉각관 · 아크가 간선까지 태운다.
/// </summary>
public static partial class Program
{
    // 방1 원자로실(원자로 R · 냉각 펌프 u · 배터리 b) | 방2 주컴퓨터실(배전반 p · 주컴퓨터 c · 충전독 r) | 방3 생명유지실(산소 발생기 O · 냉각 펌프 v · 물 재생기 W · 재배 선반 g) | 방4 휴게실(커피 기계 k)
    private const string LineShip = @"
#################################
#R....u..#.....#....O...W.#.....#
#........D..p..D..........D..k..#
#b.......#..c.r#..v....g..#.....#
#################################";

    private static int _lineRoomOf(Layout lay, int c)
    {
        if (lay.Room[c] >= 0) return lay.Room[c];
        Span<int> nb = stackalloc int[4];
        lay.Neighbors(c, nb);
        foreach (int j in nb) if (j >= 0 && lay.Room[j] >= 0) return lay.Room[j];
        return -1;
    }

    private static void LineRules()
    {
        if (!Do("선과 관")) return;
        Console.WriteLine("\n[⑥ 칸마다 선 · 관]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var legend = new Dictionary<char, string>
        {
            ['R'] = "ReactorCore", ['u'] = "CoolantPump", ['v'] = "CoolantPump", ['b'] = "Battery", ['p'] = "PowerPanel", ['c'] = "MainComputer", ['r'] = "RobotDock",
            ['O'] = "OxygenGenerator", ['W'] = "WaterRecycler", ['g'] = "GrowBed", ['k'] = "CoffeeMachine",
        };
        var lay = Worlds.Ascii(LineShip, legend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        int room3 = lay.Room[At(20, 2)], room4 = lay.Room[At(29, 2)];
        byte pw = Network.Bit(LineKind.Power), cl = Network.Bit(LineKind.Coolant), wt = Network.Bit(LineKind.Water);

        (Sim s, Network net) World(bool robot = false)
        {
            var s = Worlds.Full(lay, 3);
            if (robot) s.Get<Mind>().AddRobot(s, At(14, 3), AgentKind.Robot, At(14, 3));
            s.Run(3);
            var net = s.Get<Network>();
            net.SetOn(net.Devices.First(d => d.Type == "CoffeeMachine").Id, true);
            s.Run(2);
            return (s, net);
        }
        int Dev(Network n, string type, int nth = 0) => n.Devices.Where(d => d.Type == type).Skip(nth).First().Id;
        int Story(Sim s, string what) => s.Causes.Add(CauseKind.Story, what, s.Tick);
        // 그 가닥에만 깔린 칸 (다른 급전선 · 간선 · 가지선이 지나지 않는다)
        IEnumerable<int> Only(Network n, int[] path) => path.Where(c => n.Feeders.All(fd => fd.Path == path || Array.IndexOf(fd.Path, c) < 0) && n.Buses.All(b => b.Path == path || Array.IndexOf(b.Path, c) < 0)
            && n.DropPath.All(dp => dp == path || Array.IndexOf(dp, c) < 0));

        // ── 가지선: 산소 발생기 칸의 전선이 끊기면 그 칸을 지나는 가지선의 장치(산소 발생기 · 그 너머 물 재생기)만 서고 방은 밝다 ──
        {
            var (s, net) = World();
            int o2 = Dev(net, "OxygenGenerator"), coffee = Dev(net, "CoffeeMachine"), grow = Dev(net, "GrowBed"), rec = Dev(net, "WaterRecycler");
            if (Debug) Console.WriteLine(LinesMap(net, lay) + LinesMap(net, lay, LineKind.Coolant) + LinesMap(net, lay, LineKind.Water));
            int cut = net.Devices[o2].Cell;
            bool before = net.Supply[o2] > 0.5f && net.Supply[grow] > 0.5f;
            net.CutLines(s, cut, pw, Story(s, "산소 발생기 밑 가지선을 쥐가 갉았다"));
            s.Run(2);
            bool through = Array.IndexOf(net.DropPath[rec], cut) >= 0;
            Check("가지선: 산소 발생기 칸의 전선이 끊기면 그 칸을 지나는 가지선의 장치만 전기를 못 받고 (산소 발생기 · 그 너머 물 재생기) 방은 밝다 · 다른 가지선의 재배 선반 · 옆 방 커피 기계는 그대로",
                before && net.Supply[o2] == 0 && net.DropCut(o2) && (!through || net.DropCut(rec)) && net.Supply[grow] > 0.5f && net.RoomPowered(room3) && net.Working(coffee),
                $"전 {before} · 산소 발생기 {net.Supply[o2]:0.00} · 물 재생기 끊김 {net.DropCut(rec)} (지나감 {through}) · 재배 선반 {net.Supply[grow]:0.00} · 방 밝음 {net.RoomPowered(room3)} · 커피 기계 {net.Working(coffee)}");
        }

        // ── 간선 섬: 원자로 간선이 끊기면 원자로 없이 배터리가 받고 · 배터리 간선까지 끊기면 배전반 섬은 어둡다 ──
        {
            var (s, net) = World();
            int reactor = Dev(net, "ReactorCore"), batt = Dev(net, "Battery");
            var rb = net.Buses.First(b => b.A == reactor || b.B == reactor);
            var bb = net.Buses.First(b => b.A == batt || b.B == batt);
            int rc = Only(net, rb.Path).First();
            net.CutLines(s, rc, pw, Story(s, "원자로 간선이 낡아 끊겼다"));
            s.Run(2);
            int isl1 = net.Islands; double rkw = net.ReactorKw, sup1 = net.Supplied; bool lit1 = net.RoomPowered(room4);
            int bc = Only(net, bb.Path).First();
            net.CutLines(s, bc, pw, Story(s, "배터리 간선도 끊겼다"));
            s.Run(2);
            Check("간선 섬: 원자로 간선이 끊기면 원자로는 따로 떨어진 섬 — 배터리가 받아 방이 밝고 · 배터리 간선까지 끊기면 원천 없는 섬이 되어 어둡다",
                isl1 == 2 && rkw == 0 && sup1 > 0 && lit1 && net.Islands == 3 && !net.RoomPowered(room4) && net.Supplied == 0,
                $"섬 {isl1} · 원자로 {rkw:0.0}kW · 공급 {sup1:0.0}kW · 밝음 {lit1} → 섬 {net.Islands} · 밝음 {net.RoomPowered(room4)} · 공급 {net.Supplied:0.0}kW");
        }

        // ── 냉각관: 원자로에 닿는 관이 끊기면 달아오른 원자로가 식지 못한다 (이어진 배는 330K로 식힌다) · 700K를 넘으면 스크램 — 원인 사슬에 냉각관 ──
        {
            (double t10, string story, int rc) Heat(bool cut, double k0)
            {
                var (s, net) = World();
                var f = s.Get<Field>();
                int reactor = Dev(net, "ReactorCore");
                int cell = net.Devices[reactor].Cell;
                if (cut) net.CutLines(s, cell, cl, Story(s, "원자로 냉각관 이음매가 갈라졌다"));
                s.Run(1);
                int rc = net.CoolComp(reactor);
                f.Ts[cell] = k0;
                s.Run((int)Time.Minutes(10));
                return (f.Ts[cell], net.ReactorScram ? s.Causes.Story(net.ScramCause) : "", rc);
            }
            var cutWarm = Heat(true, 600); var okWarm = Heat(false, 600); var cutHot = Heat(true, 705);
            if (Debug) Console.WriteLine($"    {cutHot.story}");
            Check("냉각관: 원자로에 닿는 냉각관이 끊기면 달아오른 원자로가 식지 못한다 (이어진 배는 식힌다) · 700K를 넘으면 스스로 멈춘다 — 원인 사슬에 냉각관이 끊긴 까닭까지",
                cutWarm.rc < 0 && okWarm.rc >= 0 && cutWarm.t10 > 590 && okWarm.t10 < cutWarm.t10 - 50 && cutHot.story.Contains("냉각관이 끊겨") && cutHot.story.Contains("이음매가 갈라졌다"),
                $"10분 뒤 원자로 {cutWarm.t10:0}K (끊김) / {okWarm.t10:0}K (이어짐) · 조각 {cutWarm.rc}/{okWarm.rc} · \"{cutHot.story}\"");
        }

        // ── 냉각관 갈래: 원자로실과 주컴퓨터실 사이 문의 관이 끊기면 원자로는 제 방 펌프만 · 주컴퓨터는 생명유지실 펌프로 ──
        {
            var (s, net) = World();
            int reactor = Dev(net, "ReactorCore"), comp = Dev(net, "MainComputer"), u = Dev(net, "CoolantPump", 0), v = Dev(net, "CoolantPump", 1);
            int door = At(9, 2);
            bool laid = net.LineCutAt(door, LineKind.Coolant) == false && (net.LineAt[door] & cl) != 0;
            bool one = net.CoolComp(reactor) == net.CoolComp(comp);
            net.CutLines(s, door, cl, Story(s, "문턱 밑 냉각관이 찌그러졌다"));
            s.Run(1);
            Check("냉각관 갈래: 두 방 사이 문의 냉각관이 끊기면 관망이 둘로 — 원자로는 제 방 펌프와 · 주컴퓨터는 생명유지실 펌프와만 이어진다",
                laid && one && net.CoolComp(reactor) == net.CoolComp(u) && net.CoolComp(comp) == net.CoolComp(v) && net.CoolComp(reactor) != net.CoolComp(comp) && net.CoolComp(reactor) >= 0,
                $"깔림 {laid} · 처음 {one} · 원자로 {net.CoolComp(reactor)} 펌프u {net.CoolComp(u)} · 주컴퓨터 {net.CoolComp(comp)} 펌프v {net.CoolComp(v)}");
        }

        // ── 물관: 산소 발생기 앞 물관이 끊기면 물을 못 가른다 (산소가 안 는다) · 커피 기계는 물이 온다 ──
        {
            double Made(bool cut)
            {
                var (s, net) = World();
                var f = s.Get<Field>();
                int o2 = Dev(net, "OxygenGenerator");
                if (cut) net.CutLines(s, net.Devices[o2].Cell, wt, Story(s, "산소 발생기 물관이 빠졌다"));
                foreach (var room in lay.Rooms) foreach (int c in room.Cells) f.RemoveGas(c, f.T.gO2, f.Gas(f.T.gO2, c) * 0.15);   // 산소를 옅게 — 산소 발생기가 다 돈다
                double w0 = s.Book.Of(Res.Water, Flow.Split);
                s.Run((int)Time.Minutes(10));
                return w0 - s.Book.Of(Res.Water, Flow.Split);
            }
            var (s1, n1) = World();
            n1.CutLines(s1, n1.Devices[Dev(n1, "OxygenGenerator")].Cell, wt, Story(s1, "산소 발생기 물관이 빠졌다"));
            s1.Run(1);
            bool dry = !n1.WaterAtDevice(Dev(n1, "OxygenGenerator")), coffeeWet = n1.WaterAt(n1.Devices[Dev(n1, "CoffeeMachine")].Furniture);
            double ok = Made(false), cutMade = Made(true);
            Check("물관: 산소 발생기 앞 물관이 끊기면 물을 못 받아 산소를 못 낸다 · 다른 갈래의 커피 기계는 물이 온다",
                dry && coffeeWet && ok > 0.5 && cutMade == 0,
                $"물 끊김 {dry} · 커피 기계 물 {coffeeWet} · 10분 동안 가른 물 {ok:0.0}몰 (이어짐) / {cutMade:0.0}몰 (끊김)");
        }

        // ── 탄 전선: 휴게실 급전선이 불에 타면 식어도 차단기를 올릴 수 없다 → 로봇이 가서 잇는다 → 다시 밝다 ──
        {
            var (s, net) = World(robot: true);
            var f = s.Get<Field>(); var sm = s.Get<ShipMind>();
            var fd = net.Feeders[net.RoomFeeder[room4]];
            int burn = Only(net, fd.Path).Last(c => lay.Room[c] == room4 || lay.Kind[c] == CellKind.Door);
            int seed = Story(s, "휴게실 바닥에 떨어진 납땜 인두");
            s.Get<Chemistry>().HeatCause[burn] = seed;
            for (int k = 0; k < 6; k++) { f.Tf[burn] = 600; f.Ts[burn] = 600; s.Step(); }
            f.Tf[burn] = 293; f.Ts[burn] = 293;
            bool cutNow = net.FeederLineCut(fd.Id);
            s.Run((int)Time.Minutes(1));
            bool darkCool = !net.RoomPowered(room4) && net.PathFaulty(fd.Id);
            long fixAt = -1;
            for (int k = 0; k < (int)Time.Minutes(40) && fixAt < 0; k++) { s.Step(); if (!net.FeederLineCut(fd.Id)) fixAt = s.Tick; }
            s.Run((int)Time.Minutes(12));
            var fix = s.Facts.Since(0).LastOrDefault(x => x.Kind == FactKind.Repaired && x.Cell == burn);
            string fixStory = fix.Kind == FactKind.Repaired ? s.Causes.Text(fix.Cause) : "";
            var cutFact = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.CableCut && x.Cell == burn);
            string cutStory = cutFact.Kind == FactKind.CableCut ? s.Causes.Story(cutFact.Cause) : "";
            if (Debug) foreach (var x in sm.Log.TakeLast(12)) Console.WriteLine($"      {Time.Clock(x.tick)} {x.text}");
            Check("탄 전선: 급전선이 불에 타면 식어도 차단기를 다시 올릴 수 없다 (드러난 구리 — 올리면 또 합선) · 주컴퓨터가 '선이 끊겼다'로 로봇을 보내 잇고 · 차단기를 올려 다시 밝다",
                cutNow && darkCool && fixAt > 0 && net.RoomPowered(room4) && fixStory.Contains("끊긴 전선") && cutStory.Contains("납땜 인두") && cutStory.Contains("타 끊겼다"),
                $"끊김 {cutNow} · 식은 뒤 어둠 {darkCool} · 이음 {(fixAt > 0 ? Time.Clock(fixAt) : "없음")} · 밝음 {net.RoomPowered(room4)} · \"{fixStory}\" · \"{cutStory}\"");
        }

        // ── 찢긴 냉각관: 곁 외판이 뚫리면 관이 찢겨 냉매가 뿜어 나온다 ──
        {
            var (s, net) = World();
            var f = s.Get<Field>();
            int pipe = Enumerable.Range(0, lay.N).First(c => (net.LineAt[c] & cl) != 0 && lay.Room[c] >= 0 && lay.Kind[c - lay.W] == CellKind.Wall && lay.Kind[c - 2 * lay.W] != CellKind.Floor);
            double c0 = net.Coolant;
            f.SetHole(pipe - lay.W, 0.06f);
            s.Run((int)Time.Minutes(2));
            var leak = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.PipeLeak && x.Cell == pipe);
            Check("찢긴 냉각관: 곁 외판이 뚫리면 지나던 냉각관이 찢겨 냉매가 뿜어 나간다 (끊김 · 틈 1 · 원인은 그 구멍)",
                net.LineCutAt(pipe, LineKind.Coolant) && net.LeakAt(pipe, LineKind.Coolant) >= 1 && c0 - net.Coolant > 3 && leak.Kind == FactKind.PipeLeak,
                $"끊김 {net.LineCutAt(pipe, LineKind.Coolant)} · 틈 {net.LeakAt(pipe, LineKind.Coolant):0.0} · 냉매 {c0:0} → {net.Coolant:0}몰 · {(leak.Kind == FactKind.PipeLeak ? s.Causes.Story(leak.Cause) : "-")}");
        }

        // ── (다) 피복을 타고 번지는 불: 산소가 짙은 낮은 기압 대기(탐사 대기 34%)에서는 전선 칸 하나의 불이 전선 길을 따라 옆 방까지 · 보통 공기(21%)에서는 그 칸에서 꺼진다 ──
        {
            (int cells, int rooms, string story, int cut) Burn(double frac, bool open = true, double ignJ = 3e6)
            {
                var (s, net) = World();
                var f = s.Get<Field>(); var ch = s.Get<Chemistry>();
                net.AirReserve = 0;   // 낮은 기압으로 사는 배 — 예비 공기로 다시 채우지 않는다
                // 질소를 빼서 산소 비율을 맞춘다 (NASA 탐사 대기 56.5kPa · 산소 34% 처럼)
                foreach (var room in lay.Rooms) foreach (int c in room.Cells) { double o = f.Gas(f.T.gO2, c), n2 = f.Gas(f.T.gN2, c); double want = o / frac - (f.Moles(c) - n2); if (want < n2) f.RemoveGas(c, f.T.gN2, n2 - Math.Max(0, want)); }
                // 생명유지실 복도 쪽 전선 묶음 (곁에 설비가 없는 칸 — 큰 불이 산소를 먹지 않게)
                bool Bare(int c) { Span<int> nb = stackalloc int[4]; lay.Neighbors(c, nb); foreach (int j in nb) if (j >= 0 && lay.Furniture.Any(fu => Array.IndexOf(fu.Cells, j) >= 0)) return false; return lay.Furniture.All(fu => Array.IndexOf(fu.Cells, c) < 0); }
                int start = net.Feeders[net.RoomFeeder[room4]].Path.Last(c => lay.Room[c] == room3 && Bare(c));   // 휴게실 문 앞 전선
                int seed = Story(s, "생명유지실 문 앞 바닥 밑 전선에 불씨");
                ch.Ignite(start, ignJ, seed);
                var ever = new HashSet<int>();
                var st = s.Get<Structure>();
                int door = lay.DoorAt[At(26, 2)];
                // 열어 둔 문: 함장이 자동 봉쇄를 꺼 둔 배 — 켜 두면 피복이 타며 내는 염화수소를 주컴퓨터가 곧바로 잡아 그 방을 봉쇄하고 문을 닫는다 (② 사고 화학 (나) · 그것은 '사고 화학' 묶음이 본다)
                if (open && s.Find<ShipMind>() is ShipMind cmo) cmo.Policy.AutoSeal = 0;
                for (int k = 0; k < (int)Time.Minutes(12); k++)
                {
                    if (open) st.RequestDoor(door, 1);   // 생명유지실 ↔ 휴게실 문을 열어 둔다
                    s.Step();
                    for (int c = 0; c < lay.N; c++) if (ch.Burning[c] != 0 && ever.Add(c) && Debug) Console.WriteLine($"      {Time.Clock(s.Tick)} 불 ({lay.X(c)},{lay.Y(c)}) 방 {lay.Room[c]} 가닥 {net.Strands[c]} 피복 {f.Stored(f.T.sPoly, c):0.0} 산소 {f.Partial(f.T.gO2, c):0.0}/{f.Pressure(c):0.0}kPa · 문 {f.DoorOpen[door]:0.0}");
                }
                if (Debug) Console.WriteLine(LinesMap(net, lay));
                if (Debug && s.Find<ShipMind>() is ShipMind cm) foreach (var (tk, tx) in cm.Log.TakeLast(12)) Console.WriteLine($"      주컴 {Time.Clock(tk)} {tx}");
                int far = ever.Where(c => c != start).OrderByDescending(c => Math.Abs(lay.X(c) - lay.X(start)) + Math.Abs(lay.Y(c) - lay.Y(start))).FirstOrDefault(-1);
                return (ever.Count, ever.Select(c => _lineRoomOf(lay, c)).Where(r => r >= 0).Distinct().Count(), far >= 0 ? s.Causes.Story(ch.FireCause[far] != 0 ? ch.FireCause[far] : s.Facts.Since(0).Where(x => x.Kind is FactKind.FireSpread && x.Cell == far).Select(x => x.Cause).FirstOrDefault()) : "",
                    Enumerable.Range(0, lay.N).Count(c => net.LineCutAt(c, LineKind.Power)));
            }
            var thick = Burn(0.34); var shut = Burn(0.34, open: false); var air = Burn(0.21); var hot = Burn(0.34, ignJ: 9e6);
            if (Debug) Console.WriteLine($"    {thick.story}");
            Check("(다) 피복을 타고 번지는 불: 산소 34%의 낮은 기압 대기에서는 전선 칸 하나의 불꽃이 전선 묶음을 따라 기어 열린 문 너머 옆 방까지 번지고 그 길의 전선이 끊긴다 · 닫힌 문(관통부)에서는 멈춘다 · 보통 공기(21%)에서는 그 칸에서 꺼진다 (잘 안 타는 피복)",
                thick.cells >= 8 && thick.rooms >= 2 && thick.story.Contains("불씨") && thick.cut >= 5 && shut.rooms == 1 && air.cells <= 2,
                $"34% 열린 문: 탄 칸 {thick.cells} · 방 {thick.rooms} · 끊긴 전선 칸 {thick.cut} / 닫힌 문: 방 {shut.rooms} / 21%: 탄 칸 {air.cells} · \"{thick.story}\"");
            Check("(다) 센 불씨(9MJ): 불씨 칸의 피복이 앞머리보다 빨리 다 타도 앞머리는 이어 간다 (다 탄 칸이면 앞머리는 이미 그 끝에 있다) — 열린 문 너머 옆 방까지",
                hot.rooms >= 2, $"센 불씨: 탄 칸 {hot.cells} · 방 {hot.rooms}");
        }

        // ── (라) 녹슨 물관: 젖은 바닥의 관은 스무 배 빨리 녹슬고 · 바늘구멍이 나면 주컴퓨터가 밸브를 잠가 가며 방을 좁히고 로봇이 관을 따라 찾아 죈다 ──
        {
            var (s, net) = World(robot: true);
            var f = s.Get<Field>(); var sm = s.Get<ShipMind>();
            var pipe = Enumerable.Range(0, lay.N).Where(c => (net.LineAt[c] & wt) != 0 && lay.Room[c] == room4).OrderBy(c => c).ToList();
            int wetCell = pipe[0], hole = pipe[pipe.Count / 2];
            int dryCell = Enumerable.Range(0, lay.N).Where(c => (net.LineAt[c] & wt) != 0 && lay.Room[c] >= 0).OrderByDescending(c => Math.Abs(lay.X(c) - lay.X(wetCell)) + Math.Abs(lay.Y(c) - lay.Y(wetCell))).First();
            double w0 = net.RustFraction(wetCell, LineKind.Water), d0 = net.RustFraction(dryCell, LineKind.Water);
            for (int k = 0; k < (int)Time.Hours(1); k++) { if (f.Stored(f.T.sWater, wetCell) < 20) f.AddStore(wetCell, f.T.sWater, 30); s.Step(); }   // 늘 물이 고인 바닥
            double wetRate = net.RustFraction(wetCell, LineKind.Water) - w0, dryRate = net.RustFraction(dryCell, LineKind.Water) - d0;
            int seed = Story(s, "휴게실 커피 기계 밑에 물이 늘 고여 있었다");
            net.AgeRust(hole, LineKind.Water, 1.0, seed);
            long t0 = s.Tick, fixedAt = -1;
            for (int k = 0; k < (int)Time.Hours(1) && fixedAt < 0; k++) { s.Step(); if (net.LeakAt(hole, LineKind.Water) <= 0 && s.Tick - t0 > 100) fixedAt = s.Tick; }
            s.Run(30);
            var log = sm.Log.Where(x => x.tick >= t0).Select(x => x.text).ToList();
            var leakFact = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.PipeLeak && x.Cell == hole);
            string story = leakFact.Kind == FactKind.PipeLeak ? s.Causes.Story(leakFact.Cause) : "";
            if (Debug) foreach (var l in log) Console.WriteLine($"      {l}");
            bool valvesOpen = lay.Rooms.All(r => !net.ValveClosed(r.Id, LineKind.Water));
            Check("(라) 녹슨 물관: 젖은 바닥의 관은 마른 관보다 스무 배 빨리 녹슬고 · 바늘구멍이 나면 주컴퓨터가 '어딘가 샌다'를 알아 밸브를 잠가 가며 방을 좁히고 · 로봇이 그 방 관을 따라 찾아 죈다 · 밸브는 다시 연다",
                wetRate > dryRate * 15 && fixedAt > 0 && log.Any(l => l.Contains("밸브를 잠가 본다")) && log.Any(l => l.Contains($"샘은 {lay.Rooms[room4].Name}에 있다")) && log.Any(l => l.Contains("새는 자리를 찾았다")) && valvesOpen && story.Contains("물이 늘 고여"),
                $"녹 빠르기 {wetRate / Math.Max(1e-12, dryRate):0}배 · 죈 때 {(fixedAt > 0 ? $"{(fixedAt - t0) / (double)Time.Minutes(1):0}분" : "못 죔")} · 밸브 열림 {valvesOpen} · 일지 {log.Count}줄 · \"{story}\"");
        }

        // ── 아크가 간선이 함께 지나는 칸을 달구면 간선도 타 원자로가 섬으로 떨어진다 (배터리가 받는다) ──
        {
            var (s, net) = World();
            var f = s.Get<Field>();
            int reactor = Dev(net, "ReactorCore");
            var rb = net.Buses.First(b => b.A == reactor || b.B == reactor);
            int shared = net.Feeders.SelectMany(fd => fd.Path).FirstOrDefault(c => Array.IndexOf(rb.Path, c) >= 0 && lay.Room[c] >= 0 && c != rb.Path[0]);
            foreach (var fd in net.Feeders.Where(fd => Array.IndexOf(fd.Path, shared) >= 0)) net.BreakerStuck[fd.Id] = 1;   // 낡아 눌어붙은 차단기 — 아크가 이어진다
            f.AddStore(shared, f.T.sWater, 60);
            s.Run(8);
            Check("한 칸에 모인 선: 젖은 칸의 합선 아크(눌어붙은 차단기)가 같은 칸을 지나는 원자로 간선까지 태우면 원자로가 섬으로 떨어져 나가 배터리가 받는다",
                shared > 0 && net.LineCutAt(shared, LineKind.Power) && net.IslandOf(reactor) != net.IslandOf(net.Devices.First(d => d.Type == "PowerPanel").Id) && net.ReactorKw == 0,
                $"칸 {shared} · 끊김 {net.LineCutAt(shared, LineKind.Power)} · 섬 {net.Islands} · 원자로 {net.ReactorKw:0.0}kW · 공급 {net.Supplied:0.0}kW");
        }
    }
}
