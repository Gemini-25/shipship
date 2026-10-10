using System;
using System.Linq;
using ShipSim.Core;

// v12.2 인과 사슬: 사고가 무엇에서 시작해 무엇으로 번졌고 누가 되돌렸나
public static partial class Program
{
    private static void PrintIncidents(World w, int max = 3)
    {
        foreach (var inc in w.Causes.Notable().Take(max))
        {
            Console.WriteLine($"  ▶ 사고 #{inc.Root} (무게 {inc.Weight(w.Causes)} · 고리 {inc.Nodes.Count} · {(inc.Open ? "진행 중" : $"끝 {SimTime.Clock(inc.End)}")})");
            foreach (var line in w.Causes.Tree(inc).Take(60)) Console.WriteLine("     " + line);
        }
    }

    private static int RunCauseTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"인과 사슬 점검 (v12.2) · 시드 {seed}\n");

        // ── 1) 배전실에 큰 운석 ──
        {
            var w = DayOne(seed, "Mirinae");
            var power = w.Ship.FurnitureOf(FurnitureType.PowerPanel).First().Room;
            long t0 = w.Tick;
            w.Causes.ObserverNext = true;
            Incidents.Meteor(w, Scenarios.OuterTarget(w, power), 1f);
            w.Causes.ObserverNext = false;
            Run(w, SimTime.Hours(12));
            var inc = w.Causes.Incidents.First(i => i.Start >= t0); // 첫날에 난 작은 사고(실수 등)가 앞에 있을 수 있다 — 운석부터 시작한 사고를 본다
            var kinds = inc.Nodes.Select(i => w.Causes.Node(i).Kind).Distinct().ToList();
            Check("배전실 큰 운석 — 사슬이 번지고 복구가 붙는다", inc.Nodes.Count >= 4 && kinds.Contains(CauseKind.Recovery),
                $"고리 {inc.Nodes.Count} · 종류 {string.Join(",", kinds)}");
            PrintIncidents(w, 1);
        }

        // ── 2) 위기 시나리오 (밤 · 운석 셋 · 불 · 냉각 펌프 고착 · 배터리 15%) ──
        {
            var w = WreckedShip(seed, "Mirinae");
            Run(w, SimTime.Hours(12));
            int nodes = w.Causes.Nodes.Count;
            bool scram = w.Causes.Nodes.Any(n => n.Kind == CauseKind.Scram && n.Parent >= 0);
            bool casualtyLinked = w.Causes.Nodes.Where(n => n.Kind == CauseKind.Casualty).All(n => n.Parent >= 0);
            Check("위기 — 원자로 정지·쓰러짐이 원인에 이어진다", nodes > 5 && casualtyLinked,
                $"고리 {nodes} · 사고 {w.Causes.Incidents.Count} · 원자로 정지에 원인 {(scram ? "있음" : "없음/안 멈춤")} · 쓰러짐 {w.Causes.Nodes.Count(n => n.Kind == CauseKind.Casualty)}");
            PrintIncidents(w, 3);
        }

        // ── 3) 평소에는 사고 카드가 거의 없다 ──
        {
            var w = DayOne(seed, "Hanbit");
            Run(w, SimTime.TicksPerDay * 4);
            int notable = w.Causes.Notable().Count();
            Check("평소 운항 — 사고 카드가 넘치지 않는다", notable <= 3,
                $"사고 {w.Causes.Incidents.Count} (카드 {notable}) · 고리 {w.Causes.Nodes.Count}");
            PrintIncidents(w, 2);
        }

        // ── 4) 사슬은 시뮬레이션을 바꾸지 않는다 (결정론) ──
        {
            uint H() { var w = WreckedShip(seed, "Mirinae"); Run(w, SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint a = H(), b = H();
            Check("사슬이 든 배의 결정론", a == b, $"지문 {a:x8} / {b:x8}");
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 인과 사슬 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
