using ShipEngine;

namespace Lab;

/// <summary>⑤ 별계 지도 (깊이 바퀴 3) — 정거장 · 광산에 들르기 · 주컴퓨터가 다음 들를 곳을 고른다. docs/ENGINE.md ⑤ '별계 지도'</summary>
public static partial class Program
{
    private static void MapRules()
    {
        if (!Do("들를 곳")) return;
        Console.WriteLine("\n[⑤ 별계 지도 — 들를 곳 · 다음 갈 곳]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var hl = Worlds.Ascii(HelmShip, HelmLegend);

        // 그곳으로 가는 전이 궤도를 다 지나 다음 틱에 닿는 배 (창에 맞춰 떠났다)
        (Sim s, Outside o, ShipMind sm, Mind m) Arriving(string where, ulong seed, Action<Sim>? setup = null)
        {
            var s = Worlds.Full(hl, seed);
            var o = s.Get<Outside>(); var sm = s.Get<ShipMind>(); var m = s.Get<Mind>();
            s.Run(5);
            setup?.Invoke(s);
            int place = o.PlaceOf(where);
            long T = (long)(Outside.VoyageDays(o.Distance, o.Places[place].Au) * Time.TicksPerDay);
            long dep = s.Tick - T;
            o.AlignWindow(place, dep);
            o.StartVoyage(s, o.Places[place].Au); o.VoyageStart = dep; o.VoyagePlace = place;
            s.Run(2);
            return (s, o, sm, m);
        }

        // ── 정거장에 닿으면 곁에 떠 있다 — 주컴퓨터가 교신해 추진제를 부탁하고 · 붙어 오면 탱크를 채운다 ──
        {
            var (s, o, sm, m) = Arriving("띠 보급소", 41, s => { var o = s.Get<Outside>(); o.PropellantKg = 0.5 * o.PropellantStart; });
            int st = o.PlaceOf("띠 보급소");
            bool at = o.AtPlace == st, near = o.Contacts.Any(c => c.Kind == ContactKind.Station);
            double before = o.PropellantKg;
            for (int k = 0; k < 24 && o.FuelTaken <= 0; k++) s.Run(Time.Minutes(15));
            string said = sm.Log.Select(l => l.text).LastOrDefault(t => t.Contains("추진제") && t.Contains("채웠다")) ?? "";
            if (Debug) Console.WriteLine("    " + string.Join("\n    ", sm.Log.TakeLast(6).Select(x => $"{Time.Clock(x.tick)} {x.text}")));
            Check("정거장: 띠 보급소에 닿으면 곁에 떠 있다 — 주컴퓨터가 교신해 추진제를 부탁하고 · 붙어 오면 탱크를 가득 채운다",
                at && near && Math.Abs(o.PropellantKg - o.PropellantStart) < 1 && Rel(o.FuelTaken, o.PropellantStart - before) < 1e-6 && said.Length > 0,
                $"든 곳 {at} · 곁의 정거장 {near} · 추진제 {before / o.PropellantStart * 100:0}% → {o.PropellantKg / o.PropellantStart * 100:0}% · \"{said}\"");
        }

        // ── 그 정거장으로 가는 길이면 그 거리를 지날 때 '곁을 지난다'로 먼저 만나지 않는다 (닿아서 만난다 — 예전: 도착 점화 전에 곁을 지나는 정거장이 또 생겼다) ──
        {
            (int stations, bool met) Cross(bool headingThere)
            {
                var s = Worlds.Full(hl, 46);
                var o = s.Get<Outside>();
                s.Run(5);
                o.Route = 4; o.TrafficOn = true;
                int st = o.PlaceOf("띠 보급소");
                o.StartVoyage(s, 1.21); o.VoyagePlanned = 1; o.VoyageStart = s.Tick - o.VoyageTicks;   // 도착 점화를 기다리며 그 거리에 있다
                if (headingThere) o.VoyagePlace = st;
                s.Run(3);
                return (o.Contacts.Count(c => c.Kind == ContactKind.Station), (o.StationsMet & 1) != 0);
            }
            var there = Cross(true); var past = Cross(false);
            Check("그 정거장으로 가는 길이면 그 거리에서 '곁을 지난다'로 먼저 만나지 않는다 (닿아서 만난다) · 지나가는 길이면 곁을 지난다",
                there.stations == 0 && past.stations == 1, $"가는 길: 정거장 {there.stations} / 지나는 길: {past.stations}");
        }

        // ── 광산: 바위 띠 광산에 닿으면 원료를 넉넉히 (10~20) — 추진제는 없다 ──
        {
            var (s, o, sm, m) = Arriving("바위 띠 광산", 42, s =>
            {
                var m = s.Get<Mind>(); var o = s.Get<Outside>();
                for (int fu = 0; fu < s.Layout.Furniture.Count; fu++) m.Stock[fu * m.ItemCount + m.ItemIndex("원료")] = 0;
                o.PropellantKg = 0.5 * o.PropellantStart;
            });
            for (int k = 0; k < 24 && sm.SuppliesIn == 0; k++) s.Run(Time.Minutes(15));
            int raw = m.TotalOf(m.ItemIndex("원료"));
            Check("광산: 바위 띠 광산에 닿으면 원료를 넉넉히 받는다 (10~20) — 추진제는 주지 않는다",
                o.AtPlace == o.PlaceOf("바위 띠 광산") && raw >= 10 && raw <= 20 && o.FuelTaken == 0,
                $"원료 0 → {raw} · 받은 추진제 {o.FuelTaken:0}kg");
        }

        // ── 다음 들를 곳: 붉은 사막으로 떠나기 전 — 추진제가 모자라면 주컴퓨터가 정거장(창이 열리는 곳)으로 바꾼다 · 넉넉하면 그대로 ──
        {
            (int pending, long detours, string said, double left, double need, string waitLine) Leave(double fuel, bool stationWindow)
            {
                var s = Worlds.Full(hl, 43);
                var o = s.Get<Outside>(); var sm = s.Get<ShipMind>();
                s.Run(5);
                ShipWorld.Embark(s, 4, 60);
                o.PropellantKg = fuel * o.PropellantStart;
                if (stationWindow) o.AlignWindow(o.PlaceOf("띠 보급소"), s.Tick + Time.Days(10));
                s.Run(Time.Minutes(30));
                int mars = o.PlaceOf("붉은 사막");
                return (o.PendingPlace, sm.FuelDetours, sm.Log.Select(l => l.text).LastOrDefault(t => t.StartsWith("다음 들를 곳")) ?? "", o.DvLeft, o.LegDv(1, mars),
                    sm.Log.Select(l => l.text).LastOrDefault(t => t.Contains("머물며 기다린다")) ?? "");
            }
            var full = Leave(1, true); var low = Leave(0.45, true); var none = Leave(0.45, false);
            var o0 = Worlds.Full(hl, 43).Get<Outside>();
            int st0 = o0.PlaceOf("띠 보급소"), mars0 = o0.PlaceOf("붉은 사막");
            Check("다음 들를 곳: 추진제가 넉넉하면 붉은 사막으로 그대로 · 모자라면 (남은 Δv < 드는 몫 × 1.05) 창이 열리는 정거장으로 바꾼다 — 채우러 · 정거장 창이 멀고 닿을 수도 없으면 머문다",
                full.pending == mars0 && full.detours == 0 && low.pending == st0 && low.detours == 1 && low.said.Contains("추진제가 모자라다") && none.pending == -1 && none.detours == 0 && none.waitLine.Contains("닿을 정거장도 없다"),
                $"가득: 남은 Δv {full.left / 1000:0.0} · 드는 {full.need / 1000:0.0}km/s → {full.pending} / 45%: {low.left / 1000:0.0} → {low.pending} \"{low.said}\" / 정거장 창이 멀다: → {none.pending} \"{none.waitLine}\"");
        }

        // ── 부품이 막혔으면: 원료 · 전자 소자가 없고 정거장 창이 임무 창보다 먼저 열리면 정거장에 먼저 들른다 · 임무 창이 먼저면 그대로 ──
        {
            (int pending, long parts, string said) Plan(double marsDays)
            {
                var s = Worlds.Full(hl, 45);
                var o = s.Get<Outside>(); var sm = s.Get<ShipMind>(); var m = s.Get<Mind>();
                s.Run(5);
                foreach (string it in new[] { "원료", "전자 소자" }) for (int fu = 0; fu < hl.Furniture.Count; fu++) m.Stock[fu * m.ItemCount + m.ItemIndex(it)] = 0;
                int mars = o.PlaceOf("붉은 사막"), st = o.PlaceOf("띠 보급소");
                o.AlignWindow(mars, s.Tick + Time.Days(marsDays)); o.AlignWindow(st, s.Tick + Time.Days(20));
                sm.SetMission(new[] { mars });
                s.Run(Time.Minutes(10));
                return (o.PendingPlace, sm.PartsDetours, sm.NextLine);
            }
            var late = Plan(200); var soon = Plan(5);
            var o1 = Worlds.Full(hl, 45).Get<Outside>();
            Check("부품이 막혔으면: 원료 · 전자 소자가 없고 정거장 창(20일)이 임무 창(200일)보다 먼저 열리면 정거장에 들른다 · 임무 창(5일)이 먼저면 그대로 간다",
                late.pending == o1.PlaceOf("띠 보급소") && late.parts == 1 && late.said.Contains("수리 부품이 막혔다") && soon.pending == o1.PlaceOf("붉은 사막") && soon.parts == 0,
                $"임무 창 200일 → {late.pending} \"{late.said}\" / 5일 → {soon.pending} \"{soon.said}\"");
        }

        // ── 임무를 마친다: 마지막 갈 곳에 닿으면 주컴퓨터가 알린다 ──
        {
            var (s, o, sm, m) = Arriving("붉은 사막", 44, s => s.Get<ShipMind>().SetMission(new[] { s.Get<Outside>().PlaceOf("붉은 사막") }));
            s.Run(Time.Minutes(10));
            string said = sm.Log.Select(l => l.text).LastOrDefault(t => t.Contains("임무")) ?? "";
            Check("임무: 마지막 갈 곳(붉은 사막)에 닿으면 주컴퓨터가 '임무를 마쳤습니다'", sm.MissionsDone == 1 && sm.Mission.Count == 0 && said.Contains("임무를 마쳤습니다"),
                $"마친 임무 {sm.MissionsDone} · \"{said}\"");
        }
    }
}
