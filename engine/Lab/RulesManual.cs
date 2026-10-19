using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>
/// 판단 엔진 (바) 회의가 매뉴얼을 고친다 (docs/ENGINE.md '(바) 설계'): 지어낸 계획이 통하면 회의 안건이 되고 · 표결로 매뉴얼에 넣고 ·
/// 같은 꼴의 사건은 기본 절차서를 해 보지 않고 매뉴얼로 바로 · 주컴퓨터를 믿지 않는 배는 넣지 않는다.
/// </summary>
public static partial class Program
{
    // 진공이 될 방 A1 · A2 (바깥문이 없다) — 앞방 B1 · B2 — 가운데 식당(사람 넷 · 로봇 · 주컴퓨터 · 작업대)
    private const string ManualShip = @"
#########################################
#..x....#.....#...........#.....#....x..#
#.......D.....D....h.c....D.....D.......#
#..e....#.....#..pb.w.k.k.#.....#....e..#
#########################################";

    private static void ManualRules()
    {
        if (!Do("매뉴얼")) return;
        Console.WriteLine("\n[⑨ · ⑩ 판단 엔진 (바) 매뉴얼]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var legend = new Dictionary<char, string> { ['x'] = "OxygenMaskBox", ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer", ['e'] = "Bed",
            ['h'] = "HeatExchanger", ['k'] = "Shelf", ['w'] = "Workbench" };
        var lay = Worlds.Ascii(ManualShip, legend);
        int At(int x, int y) => Worlds.At(lay, x, y);

        // 사람 넷 (식당) · 로봇 하나 — 사람을 먼저 넣는다 (⑨의 사람 번호 = 마음의 번호)
        (Sim s, Mind m, Society soc, ShipMind sm, Field f, List<int> p) World(float trust)
        {
            var s = Worlds.Full(lay, 7);
            var m = s.Get<Mind>(); var soc = s.Get<Society>(); var sm = s.Get<ShipMind>(); var f = s.Get<Field>();
            var p = new List<int>();
            foreach (int x in new[] { 16, 17, 18, 23 }) { int a = m.AddPerson(s, At(x, 1)); m.SetPersonality(a, 0.5f, 0.7f, 0.6f, 0.5f, 0.3f); m[a].C.KnPressure = 0.5f; p.Add(a); }
            m.AddRobot(s, At(22, 1), AgentKind.Robot, At(22, 1));
            s.Run(2);
            foreach (int a in p) soc.TrustComputer[a] = trust;
            s.Get<Network>().AirReserve = 2e6;
            return (s, m, soc, sm, f, p);
        }
        void Keep(Sim s, Mind m, List<int> p) { foreach (int a in p) { ref var c = ref m[a].C; c.Sleepy = 0; c.Hunger = 0; c.Thirst = 0; s.Get<Life>()[c.Body].Asleep = 0; } }
        // 구멍을 내고 막힐 때까지 (많아야 세 시간) — 계획을 지은 데 걸린 틱 · 막은 데 걸린 틱
        (long devise, long patch) Breach(Sim s, Mind m, ShipMind sm, Field f, List<int> p, int hole, Action? midPlan = null)
        {
            long t0 = s.Tick, dv = -1; long d0 = sm.DevisesFound;
            f.SetHole(hole, 0.02f);
            for (int k = 0; k < (int)Time.Hours(3) && f.Hole[hole] > 0; k++)
            {
                s.Step(); Keep(s, m, p); if (dv < 0 && sm.DevisesFound > d0) dv = s.Tick - t0;
                if (midPlan != null && sm.PlanSteps.Any(q => q.Act == (byte)ActKind.Pump && q.State == 1)) { midPlan(); midPlan = null; }   // 공기를 되돌리는 동안 한 번 (화면 시험)
            }
            long pt = f.Hole[hole] <= 0 ? s.Tick - t0 : -1;
            for (int k = 0; k < (int)Time.Hours(2) && sm.PlanSteps.Count > 0; k++) { s.Step(); Keep(s, m, p); }   // 다시 채우기까지 (계획이 끝나야 '통했다')
            return (dv, pt);
        }
        void Meet(Sim s, Mind m, Society soc, List<int> p) { s.Run((int)Time.Minutes(35)); m.CallCouncilNow(s); for (int k = 0; k < (int)Time.Minutes(45) && soc.Meeting.Kind != CouncilKind.None; k++) { s.Step(); Keep(s, m, p); } }

        int hole1 = At(3, 0), hole2 = At(37, 0);

        // ── (바-가) · (바-나): 주컴퓨터를 믿는 배 ──
        {
            var (s, m, soc, sm, f, p) = World(0.65f);
            PanelRow[] mid = Array.Empty<PanelRow>();
            var (dv1, pt1) = Breach(s, m, sm, f, p, hole1, () => { var show = new ShipView.LiveShow(s, "시험호"); show.TogglePanel(PanelKind.Journal); mid = show.PanelRows(); });
            long worked = s.Facts.CountOf(FactKind.MethodWorked);
            Meet(s, m, soc, p);
            var changed = s.Facts.Since(0).LastOrDefault(x => x.Kind == FactKind.ManualChanged);
            string story = changed.Kind == FactKind.ManualChanged ? s.Causes.Story(changed.Cause) : "-";
            var lines = soc.Sayings.Where(y => y.Tick >= s.Tick - Time.Minutes(40)).Select(y => s.Causes.Text(y.Line)).ToList();
            if (Debug) { Console.WriteLine($"    (가) {story}"); foreach (var l in lines) Console.WriteLine($"      {l}"); }
            Check("매뉴얼 (가): 지어낸 계획으로 구멍을 막으면 '통했다'가 회의 안건이 되고 · 모여 한 마디씩 · 과반이면 매뉴얼에 넣는다",
                pt1 > 0 && worked >= 1 && soc.Manual.Count(e => e.Removed == 0) == 1 && changed.Size > 0 && lines.Any(l => l.Contains("매뉴얼에 넣자")),
                $"막음 {pt1 / (double)Time.Minutes(1):0}분 · 통했다 {worked} · 매뉴얼 {soc.Manual.Count(e => e.Removed == 0)} · 말 {lines.Count}마디 · {story}");

            // 화면 (사): 주컴퓨터 판(J) — 계획 중인 사건 줄에 생각 사슬 · 걸음(✔ · ▶) / 회의 뒤 '매뉴얼' 줄
            var after = new ShipView.LiveShow(s, "시험호"); after.TogglePanel(PanelKind.Journal); var rows = after.PanelRows();
            var planRow = mid.FirstOrDefault(r => r.Main.StartsWith("지금 · ") && r.Sub.Contains("계획: "));
            var manRow = rows.FirstOrDefault(r => r.Main.StartsWith("매뉴얼"));
            if (Debug) { Console.WriteLine($"    (사) {planRow.Main}\n      {planRow.Sub?.Replace("\n", "\n      ")}"); Console.WriteLine($"    (사) {manRow.Main}: {manRow.Sub}"); }
            Check("화면 (사): 주컴퓨터 판에 지어낸 계획의 생각 사슬 · 걸음(✔ 마침 · ▶ 하는 중)이 그 사건 줄에 · 회의 뒤에는 매뉴얼 줄 (찬성 · 통한 번)",
                planRow.Main != null && planRow.Sub.Contains("궁리: ") && planRow.Sub.Contains("지어낸 안") && planRow.Sub.Contains("▶ ") && planRow.Sub.Contains("✔ ")
                && manRow.Main == "매뉴얼 (1)" && manRow.Sub.Contains("임시 에어락") && manRow.Sub.Contains("찬성 4/4"),
                $"사건 줄 '{planRow.Sub?.Split('\n').LastOrDefault()}' · 매뉴얼 줄 '{manRow.Main}: {manRow.Sub}'");

            int xs0 = sm.Attempts.Count(a => a.Act == 255);
            var (dv2, pt2) = Breach(s, m, sm, f, p, hole2);
            var tried2 = sm.Attempts.Where(a => a.Act == 255).Skip(xs0).Count();
            bool byManual = sm.Log.Any(x => x.text.Contains("매뉴얼"));
            if (Debug) foreach (var x in sm.Log.Where(x => x.text.StartsWith("생각"))) Console.WriteLine($"      {Time.Clock(x.tick)} {x.text}");
            Check("매뉴얼 (나): 같은 꼴의 둘째 구멍은 기본 절차서(안에서 · 바깥에서)를 해 보지 않고 매뉴얼로 바로 궁리한다 — 계획을 짓기까지 첫 번째보다 빠르다",
                byManual && tried2 == 0 && dv2 > 0 && dv2 < dv1 && pt2 > 0,
                $"매뉴얼로 {byManual} · 해 본 기본 방법 {tried2} · 계획까지 {dv1 / (double)Time.Minutes(1):0.0}분 → {dv2 / (double)Time.Minutes(1):0.0}분 · 막음 {pt2 / (double)Time.Minutes(1):0}분");
        }

        // ── (바-다): 주컴퓨터를 믿지 않는 배 — 부결 ──
        {
            var (s, m, soc, sm, f, p) = World(0.15f);
            var (dv1, pt1) = Breach(s, m, sm, f, p, hole1);
            Meet(s, m, soc, p);
            var changed = s.Facts.Since(0).LastOrDefault(x => x.Kind == FactKind.ManualChanged);
            var lines = soc.Sayings.Where(y => y.Tick >= s.Tick - Time.Minutes(40)).Select(y => s.Causes.Text(y.Line)).ToList();
            if (Debug) foreach (var l in lines) Console.WriteLine($"      {l}");
            Check("매뉴얼 (다): 주컴퓨터를 믿지 않는 배는 '주컴퓨터가 지어낸 것을 어떻게 믿나'로 부결 — 매뉴얼에 넣지 않는다",
                pt1 > 0 && changed.Kind == FactKind.ManualChanged && changed.Size == 0 && soc.Manual.Count(e => e.Removed == 0) == 0 && lines.Any(l => l.Contains("어떻게 믿나")),
                $"막음 {pt1 / (double)Time.Minutes(1):0}분 · 표결 {(changed.Kind == FactKind.ManualChanged ? changed.Size.ToString() : "없음")} · 매뉴얼 {soc.Manual.Count(e => e.Removed == 0)} · 말 {lines.Count}마디");
        }
    }
}
