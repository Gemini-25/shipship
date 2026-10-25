using ShipEngine;

namespace Lab;

/// <summary>법칙 시험 — ⑧ · ⑩ 판단 엔진 풀이기 (그림만으로 — 세계 없이). docs/ENGINE.md 판단 엔진 (다)</summary>
public static partial class Program
{
    /// <summary>에어락 없는 작은 배: 왼쪽 방(구멍 · 진공) — 문 — 오른쪽 방(로봇 · 통풍구) [— 옆 방].</summary>
    internal static Picture NoAirlockPicture(byte peopleRight = 0, bool sideRoom = false, bool jam = false)
    {
        var p = new Picture();
        int l = p.AddPlace(0, "방1", 30, 0, vent: true, hole: true);
        int r = p.AddPlace(1, "방2", 24, 101.3f, vent: true, people: peopleRight);
        p.AddDoor(l, r, 0, jam: jam);
        if (sideRoom) { int s = p.AddPlace(2, "방3", 20, 101.3f, vent: true); p.AddDoor(r, s, 1); }
        p.Body = r; p.Goal = l;
        return p;
    }

    /// <summary>에어락이 있는 배: 구멍 난 방 — 복도(사람 셋) — 에어락 — 바깥 (구멍 난 방 외판은 바깥에서 닿는다).</summary>
    internal static Picture AirlockPicture()
    {
        var p = new Picture();
        int l = p.AddPlace(0, "창고", 30, 0, vent: true, hull: true, hole: true);
        int c = p.AddPlace(1, "복도", 40, 101.3f, vent: true, people: 3);
        int a = p.AddPlace(2, "에어락", 4, 101.3f, vent: true);
        int o = p.AddOut();
        p.AddDoor(l, c, 0); p.AddDoor(c, a, 1); p.AddDoor(a, o, 2);
        p.Body = c; p.Goal = l;
        return p;
    }

    /// <summary>살핌호 7일: 복도(사람 둘 · 로봇) 곁에 구멍 난 창고 · 곁방 여섯(틈 있는 보통 문) · 에어락 바깥문이 끼었다 · 다른 진공 방 하나.</summary>
    internal static Picture JammedLockPicture()
    {
        var p = new Picture();
        int store = p.AddPlace(0, "식량 창고", 20, 0, vent: true, hull: true, hole: true);
        int hall = p.AddPlace(1, "중앙 통로", 40, 66, vent: true, people: 2);
        var side = new List<int>();
        foreach (var n in new[] { "원자로실", "배전실", "생명유지실", "침실", "정비실", "의무실" }) side.Add(p.AddPlace(side.Count + 2, n, 16, 66, vent: true, hull: true));
        int lockR = p.AddPlace(8, "에어락", 4, 0, vent: true, hull: true);
        int galley = p.AddPlace(9, "주방", 20, 0, vent: true, hull: true, hole: true);
        int o = p.AddOut();
        p.AddDoor(hall, store, 0, leaky: false);
        foreach (int sr in side) p.AddDoor(sr, hall, p.D, leaky: true);
        p.AddDoor(hall, lockR, p.D);
        p.AddDoor(hall, galley, p.D);
        p.AddDoor(lockR, o, p.D, jam: true);
        p.Body = hall; p.Goal = store;
        return p;
    }

    /// <summary>살핌호 6일(파편 폭풍 뒤): 우주로 트인 관측실 — 관측 돛대(로봇) — 함교(주컴퓨터) — 중앙 통로(사람 둘) · 곁방 스물(틈 있는 보통 문 · 몇은 사람) · 에어락 · 배 전체 82kPa.</summary>
    internal static Picture HubPicture()
    {
        var p = new Picture();
        int obs = p.AddPlace(0, "관측실", 20, 0, vent: true, hull: true, hole: true, holeArea: 0.3f);
        int mast = p.AddPlace(1, "관측 돛대", 30, 83, vent: true, hull: true);
        int bridge = p.AddPlace(2, "함교", 40, 82, vent: true, hull: true); p.Brain[bridge] = true;
        int hall = p.AddPlace(3, "중앙 통로", 60, 81, vent: true, people: 2);
        int lockR = p.AddPlace(4, "에어락", 8, 83, vent: true, hull: true);
        int o = p.AddOut();
        p.AddDoor(mast, obs, 0); p.AddDoor(bridge, mast, 1, leaky: true); p.AddDoor(hall, bridge, 2, leaky: true); p.AddDoor(hall, lockR, 3); p.AddDoor(lockR, o, 4);
        for (int k = 0; k < 20; k++)
        {
            int sr = p.AddPlace(5 + k, $"곁방 {k + 1}", 16, 82, vent: true, hull: true, people: (byte)(k % 4 == 0 ? 1 : 0));
            p.AddDoor(sr, hall, 5 + k, open: k == 12, leaky: k != 12);
        }
        p.Body = mast; p.Goal = obs;
        return p;
    }

    private static void SolverLaws()
    {
        if (!Do("판단 엔진")) return;
        Console.WriteLine("\n[⑧ · ⑩ 판단 엔진 — 풀이기]");
        var all = Know.All;
        string Steps(SolveResult r) => string.Join(" ", r.Steps);

        // 에어락이 없고 문 너머가 우주로 트였다 → 오른쪽 방을 임시 에어락으로 (공기를 되돌리고 · 문을 열고 · 들어가 막는다)
        var a = NoAirlockPicture();
        var ra = Solver.Solve(a, all, computer: true);
        Check("임시 에어락을 지어낸다: 에어락 없이 우주로 트인 방 — 앞방 공기를 예비 탱크로 되돌리고 문을 열어 들어가 막는다 (아무도 다치지 않는다)",
            ra.Found && ra.Steps.Any(s => s.Act == ActKind.Pump) && ra.Steps.Last().Act == ActKind.Patch && ra.Risk == 0 && ra.Invented && Solver.Replay(a, ra.Steps, all, true),
            $"{(ra.Found ? Solver.Describe(a, ra.Steps) : "못 찾음")} · {ra.Minutes:0}분 · 잃을 공기 {ra.AirLost:0}몰 · 되돌림 {ra.AirBack:0}몰 · 펼침 {ra.Expanded}");

        // 같은 그림 · 같은 지식이면 같은 계획 (결정론)
        var a2 = Solver.Solve(NoAirlockPicture(), all, true);
        var b1 = Solver.Solve(AirlockPicture(), all, true); var b2 = Solver.Solve(AirlockPicture(), all, true);
        Check("같은 그림 · 같은 지식이면 같은 계획", Steps(ra) == Steps(a2) && ra.Cost == a2.Cost && Steps(b1) == Steps(b2) && b1.Cost == b2.Cost && b1.Found,
            $"{Steps(ra)} / {Steps(b1)}");

        // 에어락이 있으면 바깥에서 막는 쪽이 싸다 (사람이 있는 복도를 비우지 않는다)
        var b = AirlockPicture();
        Check("에어락이 있으면 에어락으로 나가 바깥에서 막는다 (사람 셋이 있는 복도를 비우지 않는다)",
            b1.Found && b1.FromOutside && b1.Risk == 0 && !b1.Steps.Any(s => s.Act == ActKind.Clear) && Solver.Replay(b, b1.Steps, all, true),
            $"{(b1.Found ? Solver.Describe(b, b1.Steps) : "못 찾음")} · {b1.Minutes:0}분 · 잃을 공기 {b1.AirLost:0}몰");

        // 사람이 있는 방의 공기는 빼지 않는다 — 먼저 옆 방으로 내보낸다
        var c = NoAirlockPicture(peopleRight: 2, sideRoom: true);
        var rc = Solver.Solve(c, all, true);
        var xc = rc.Found ? Solver.Expand(c, rc.Steps, all, true).Select(x => x.step).ToList() : new List<SolveStep>();   // 내보내기는 되돌리기 묶음 안에
        int iClear = xc.FindIndex(s => s.Act == ActKind.Clear), iPump = xc.FindIndex(s => s.Act == ActKind.Pump);
        var c0 = NoAirlockPicture(peopleRight: 2);
        var rc0 = Solver.Solve(c0, all, true);
        Check("사람이 있는 방의 공기는 빼지 않는다: 옆 방이 있으면 먼저 내보내고 · 갈 곳이 없으면 그 길은 없다",
            rc.Found && rc.Risk == 0 && iClear >= 0 && iClear < iPump && (!rc0.Found || rc0.Risk == 0 && !rc0.Steps.Any(s => s.Act == ActKind.Pump)),
            $"{(rc.Found ? Solver.Describe(c, xc) : "못 찾음")} / 갈 곳 없음: {(rc0.Found ? Solver.Describe(c0, rc0.Steps) : "못 찾음")}");

        // 끼인 문 → 먼저 바로잡는다 (하위 목표)
        var j = NoAirlockPicture(jam: true);
        var rj = Solver.Solve(j, all, true);
        Check("끼인 문은 먼저 바로잡는다", rj.Found && rj.Steps.Count > 0 && rj.Steps[0].Act == ActKind.Unjam && Solver.Replay(j, rj.Steps, all, true),
            rj.Found ? Solver.Describe(j, rj.Steps) : "못 찾음");

        // 문짝이 날아간 빈 문틀 (열린 채 끼였다 — PH10): 바로잡을 문짝이 없다 — 그림에서 바로잡기가 되지 않고 계획에 넣지 않는다 (예전엔 바로잡아 닫을 수 있는 문으로 셌다)
        Picture GonePicture(bool gone) { var p = new Picture(); int l = p.AddPlace(0, "방1", 30, 0, vent: true, hole: true), r = p.AddPlace(1, "방2", 24, 101.3f, vent: true); p.AddDoor(l, r, 0, open: true, jam: true, gone: gone); p.Body = r; p.Goal = l; return p; }
        var g = GonePicture(true); var rg = Solver.Solve(g, all, true);
        var unjam = new List<SolveStep> { new SolveStep { Act = ActKind.Unjam, Obj = 0 } }; unjam.AddRange(rg.Steps);   // 바로잡고 나서 같은 길
        bool fixGone = Solver.Replay(g, unjam, all, true), fixJam = Solver.Replay(GonePicture(false), unjam, all, true);
        Check("문짝이 날아간 빈 문틀은 바로잡지 않는다 (끼인 문이 아니다 — 문짝이 있는 끼인 문은 바로잡힌다) · 열린 채 지나가 막는다",
            !fixGone && fixJam && rg.Found && !rg.Steps.Any(s => s.Act == ActKind.Unjam) && Solver.Replay(g, rg.Steps, all, true),
            $"빈 문틀 바로잡기 {fixGone} · 문짝 있는 끼인 문 {fixJam} · 계획: {(rg.Found ? Solver.Describe(g, rg.Steps) : "못 찾음")}");

        // 모르는 원리의 행동은 계획에 없다 — 원리마다 빼 보고 세 그림을 다시 푼다
        var kt = KnowledgeTable.Load();
        var bad = new List<string>();
        foreach (var pr in Enum.GetValues<Principle>())
        {
            var k = all.Without(pr);
            foreach (var pic in new[] { NoAirlockPicture(), AirlockPicture(), NoAirlockPicture(jam: true), NoAirlockPicture(2, true) })
            {
                var r = Solver.Solve(pic, k, true);
                foreach (var st in r.Steps) if (!kt.CanThink(st.Act, k)) bad.Add($"{pr}: {st.Act}");
            }
        }
        var noPump = Solver.Solve(NoAirlockPicture(), all.Without(Principle.PumpBack), true);
        Check("모르는 원리의 행동은 계획에 없다 (되돌리기를 모르면 임시 에어락을 떠올리지 못한다)", bad.Count == 0 && !noPump.Steps.Any(s => s.Act == ActKind.Pump),
            bad.Count > 0 ? string.Join(" · ", bad.Take(4)) : $"되돌리기 모름: {(noPump.Found ? Steps(noPump) : "못 찾음")}");

        // 기압 차를 모르는 이는 그냥 연다 (그 마음속 계획 — 해 보다 막힌다)
        var naive = kt.KnowOf(stackalloc float[] { 1f, 0f, 0f, 0f, 0f, 0.5f });
        var rn = Solver.Solve(NoAirlockPicture(), naive, computer: false);
        Check("기압 차를 모르는 이는 우주로 트인 방의 문을 그냥 열려 한다 (그 사람 마음속 계획)",
            rn.Found && rn.Steps.Count > 0 && rn.Steps[0].Act == ActKind.Open && !rn.Steps.Any(s => s.Act == ActKind.Pump),
            rn.Found ? $"{Solver.Describe(NoAirlockPicture(), rn.Steps)} · 잃을 공기 {rn.AirLost:0}몰" : "못 찾음");

        // 제 몸이 있는 방은 비우지 않는다 (공기냉각 — 공기가 빠진 방의 주컴퓨터는 식지 못해 멈춘다)
        var br = NoAirlockPicture(); br.Brain[br.Body] = true;
        var rb = Solver.Solve(br, all, true);
        Check("제 몸(주컴퓨터)이 있는 방을 비우는 방법은 위험하다", !rb.Found || rb.Risk > 0, rb.Found ? $"{Solver.Describe(br, rb.Steps)} · 위험 {rb.Risk}" : "못 찾음");

        // 살핌호: 에어락 바깥문이 끼었다 → 에어락으로 들어가 안에서 바로잡고 나가 바깥에서 막는다 (사람이 있는 복도는 비우지 않는다 · 곁방 문은 건드리지 않는다)
        var jl = JammedLockPicture();
        var rj2 = Solver.Solve(jl, all, true, maxDepth: 12, budget: 4000);
        Check("에어락 바깥문이 끼었으면 들어가 안에서 바로잡고 나가 바깥에서 막는다 (사람이 있는 복도는 비우지 않는다)",
            rj2.Found && rj2.FromOutside && rj2.Steps.Any(s => s.Act == ActKind.Unjam) && rj2.Risk == 0 && rj2.Expanded < 1500,
            rj2.Found ? $"{Solver.Describe(jl, rj2.Steps)} · {rj2.Minutes:0}분 · 펼침 {rj2.Expanded}" : $"못 찾음 · 펼침 {rj2.Expanded}");

        // 사람이 몸이면: 우주복 보관함이 있으면 입고 들어가 막는다 · 없으면 사람을 둔 채 그 방 공기를 빼지 않는다
        var ps = NoAirlockPicture(); ps.BodyBreathes = true; ps.Lockers[ps.Body] = true;
        var rps = Solver.Solve(ps, all, true);
        var pn = NoAirlockPicture(); pn.BodyBreathes = true;
        var rpn = Solver.Solve(pn, all, true);
        int iSuit = rps.Steps.FindIndex(s => s.Act == ActKind.Suit), iP = rps.Steps.FindIndex(s => s.Act == ActKind.Pump);
        Check("사람이 몸이면 우주복을 먼저 입고 (공기를 빼기 전에) 들어가 막는다 · 우주복이 없으면 사람이 있는 채 공기를 빼지 않는다",
            rps.Found && rps.Risk == 0 && iSuit >= 0 && iSuit < iP && (!rpn.Found || rpn.Risk > 0 || !rpn.Steps.Any(s => s.Act == ActKind.Pump)),
            $"{(rps.Found ? Solver.Describe(ps, rps.Steps) : "못 찾음")} / 우주복 없음: {(rpn.Found ? Solver.Describe(pn, rpn.Steps) + $" · 위험 {rpn.Risk}" : "못 찾음")}");

        // 봉합 꾸러미 셈: 틈 막기 · 구멍 막기는 꾸러미를 하나씩 쓴다 — 가진 것보다 많이 쓰는 계획은 없다 (사람 몸 · 틈 있는 보통 문 곁방)
        {
            string kitNote = "";
            bool kitOk = true;
            foreach (int kits in new[] { 1, 2 })
            {
                var kp = NoAirlockPicture(sideRoom: true); kp.Kits = kits;
                for (int d = 0; d < kp.D; d++) if (kp.DA[d] == kp.Body && kp.DB[d] != 0 || kp.DB[d] == kp.Body && kp.DA[d] != 0) { kp.Leaky[d] = true; kp.Normal[d] = true; }
                var kr = Solver.Solve(kp, all, true);
                int used = kr.Found ? Solver.Expand(kp, kr.Steps, all, true).Count(x => x.step.Act is ActKind.Tape or ActKind.Patch) : 0;
                kitOk &= !kr.Found || used <= kits && Solver.Replay(kp, kr.Steps, all, true);
                if (kits == 2) kitOk &= kr.Found && used == 2;   // 둘이면 틈을 막고 공기를 되돌린 뒤 들어가 막는다
                kitNote += $"꾸러미 {kits}: {(kr.Found ? Solver.Describe(kp, kr.Steps) + $" (씀 {used})" : "못 찾음")} / ";
            }
            Check("봉합 꾸러미는 틈 막기 · 구멍 막기에 하나씩 든다 — 가진 것보다 많이 쓰는 계획은 없다", kitOk, kitNote.TrimEnd(' ', '/'));
        }

        // 큰 구멍: 막는 번 = 넓이 ÷ 한 번에 막는 넓이 (로봇 0.1㎡ · 사람 0.05㎡) — 시간도 꾸러미도 그만큼 · 꾸러미가 모자라면 그 몸으로는 못 막는다
        {
            var big = NoAirlockPicture(); big.HoleArea[big.Goal] = 0.35f;
            var rbig = Solver.Solve(big, all, true);
            var small = NoAirlockPicture(); var rsmall = Solver.Solve(small, all, true);
            var pbig = NoAirlockPicture(); pbig.HoleArea[pbig.Goal] = 0.35f; pbig.PatchArea = 0.05f; pbig.Kits = 3;
            var rp = Solver.Solve(pbig, all, true);
            float pm = (float)KnowledgeTable.Load().A[(int)ActKind.Patch].Minutes;
            Check("큰 구멍은 여러 번 덧댄다: 막는 번 = 넓이 ÷ 한 번에 막는 넓이 (시간 · 꾸러미 모두) — 꾸러미가 모자란 몸으로는 못 막는다",
                big.Patches(big.Goal) == 4 && rbig.Found && Math.Abs(rbig.Minutes - rsmall.Minutes - 3 * pm) < 0.5f && pbig.Patches(pbig.Goal) == 7 && !rp.Found,
                $"로봇 0.35㎡ → {big.Patches(big.Goal)}번 · {rbig.Minutes:0}분 (0.05㎡ {rsmall.Minutes:0}분) / 사람 0.35㎡ → {pbig.Patches(pbig.Goal)}번 · 꾸러미 3: {(rp.Found ? "찾음" : "못 찾음")}");
        }

        // 공기 되돌리기 (⑥ 망): 닫힌 방에서 빨아 낸 질소 · 산소 · 아르곤은 예비 탱크로 (보존) · 20kPa 아래로는 못 뺀다
        {
            var lay = Worlds.Ascii(NoAirlockShip, new Dictionary<char, string> { ['x'] = "OxygenMaskBox", ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer", ['e'] = "Bed", ['h'] = "HeatExchanger" });
            var s = Worlds.Full(lay, 2);
            var f = s.Get<Field>(); var net = s.Get<Network>(); var st = s.Get<Structure>();
            int mid = lay.Room[Worlds.At(lay, 14, 2)];
            foreach (var d in lay.Doors) { st.RequestDoor(d.Id, 0); f.TapeDoor(d.Id); }
            s.Run(30);
            double Air(IEnumerable<int> cells) { double n = 0; foreach (int c in cells) n += f.Gas(f.T.gN2, c) + f.Gas(f.T.gO2, c) + f.Gas(f.T.gAr, c); return n; }
            var cellsAll = Enumerable.Range(0, lay.N).ToList();
            double room0 = Air(lay.Rooms[mid].Cells), ship0 = Air(cellsAll) + net.AirReserve, back0 = net.AirRecovered;
            for (int k = 0; k < Time.Minutes(60); k++) { net.RoomAir[mid] = -lay.Rooms[mid].Cells.Length * Network.PumpMolPerCellSec; s.Step(); }
            double took = room0 - Air(lay.Rooms[mid].Cells), got = net.AirRecovered - back0, ship = Air(cellsAll) + net.AirReserve, kpa = f.Pressure(Worlds.At(lay, 14, 2));
            Check("공기 되돌리기: 닫힌 방에서 빨아 낸 공기는 예비 탱크로 (배 + 탱크의 공기는 그대로) · 20kPa 아래로는 못 뺀다",
                took > 500 && got >= took * 0.98 && Math.Abs(ship - ship0) < ship0 * 0.002 && kpa >= Network.PumpFloorKPa - 0.5 && kpa < Network.PumpFloorKPa + 2,
                $"방이 잃은 {took:0}몰 · 탱크로 되돌린 {got:0}몰 · 배 + 탱크 {ship0:0} → {ship:0}몰 · 남은 기압 {kpa:0.0}kPa");
        }

        // 큰 배: 곁방이 스물인 통로 · 사람 있는 방 — 쓸데없는 내보내기 · 채우기를 펼치지 않아 예산 안에서 찾는다 (한 판단은 몇십 ms)
        {
            var hp = HubPicture();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var rh = Solver.Solve(hp, all, true, banned: s => s.Act == ActKind.Open && s.Obj == 4);   // 바깥에서 막기는 막혔다 (밸브를 잠가 두었다 — 살핌호 6일)
            sw.Stop();
            if (Environment.GetEnvironmentVariable("SNAG_DEBUG") == "1") { Solver.CountActs = new int[16]; Solver.Solve(hp, all, true, banned: s => s.Act == ActKind.Open && s.Obj == 4); Console.WriteLine("    " + string.Join(" ", Enum.GetValues<ActKind>().Select(a => $"{a}{Solver.CountActs[(int)a]}"))); Solver.CountActs = null; }
            Check("큰 배 그림(자리 26 · 문 25 · 사람 있는 통로)에서도 펼침 1500 안에 찾는다 — 사람이 있는 방은 그 방 공기를 뺄 때만 내보낸다",
                rh.Found && rh.Risk == 0 && rh.Expanded <= 1500 && rh.Steps.Any(s => s.Act == ActKind.Pump),
                $"{(rh.Found ? Solver.Describe(hp, rh.Steps) : "못 찾음")} · 펼침 {rh.Expanded} · {sw.ElapsedMilliseconds}ms");
        }

        // 펼침 예산
        var tiny = Solver.Solve(AirlockPicture(), all, true, budget: 20);
        Check("펼침은 예산을 넘지 않는다", tiny.Expanded <= 21 && (tiny.BudgetOut || tiny.Found), $"펼침 {tiny.Expanded} · 예산 20");
    }
}
