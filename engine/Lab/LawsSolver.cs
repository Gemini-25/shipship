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
        int iClear = rc.Steps.FindIndex(s => s.Act == ActKind.Clear), iPump = rc.Steps.FindIndex(s => s.Act == ActKind.Pump);
        var c0 = NoAirlockPicture(peopleRight: 2);
        var rc0 = Solver.Solve(c0, all, true);
        Check("사람이 있는 방의 공기는 빼지 않는다: 옆 방이 있으면 먼저 내보내고 · 갈 곳이 없으면 그 길은 없다",
            rc.Found && rc.Risk == 0 && iClear >= 0 && iClear < iPump && (!rc0.Found || rc0.Risk == 0 && !rc0.Steps.Any(s => s.Act == ActKind.Pump)),
            $"{(rc.Found ? Solver.Describe(c, rc.Steps) : "못 찾음")} / 갈 곳 없음: {(rc0.Found ? Solver.Describe(c0, rc0.Steps) : "못 찾음")}");

        // 끼인 문 → 먼저 바로잡는다 (하위 목표)
        var j = NoAirlockPicture(jam: true);
        var rj = Solver.Solve(j, all, true);
        Check("끼인 문은 먼저 바로잡는다", rj.Found && rj.Steps.Count > 0 && rj.Steps[0].Act == ActKind.Unjam && Solver.Replay(j, rj.Steps, all, true),
            rj.Found ? Solver.Describe(j, rj.Steps) : "못 찾음");

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

        // 펼침 예산
        var tiny = Solver.Solve(AirlockPicture(), all, true, budget: 20);
        Check("펼침은 예산을 넘지 않는다", tiny.Expanded <= 21 && (tiny.BudgetOut || tiny.Found), $"펼침 {tiny.Expanded} · 예산 20");
    }
}
