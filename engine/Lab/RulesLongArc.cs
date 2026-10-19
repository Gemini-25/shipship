using ShipEngine;

namespace Lab;

/// <summary>⑫ 긴 줄기 (깊이 바퀴 3) — 내다보기 · 항해 달력 · 구간 준비 · 사람이 받아들이기. docs/ENGINE.md ⑤ '긴 줄기'</summary>
public static partial class Program
{
    private static void LongArcLaws()
    {
        if (!Do("내다보기")) return;
        Console.WriteLine("\n[⑫ 긴 줄기 — 내다보기]");
        // ── 구간에 드는 때 = 전이 궤도의 거리가 그 경계에 닿는 때 (케플러) · 항로만의 구간은 그 항로만 ──
        {
            (List<Outside.Span> spans, Outside o, Sim s) Plan(int route)
            {
                var s = ShipWorld.Create("Hwasal", 5);
                var o = s.Get<Outside>();
                o.Route = route;
                s.Run(3);
                o.StartVoyage(s, 1.52);
                return (o.ZoneSpans(s.Tick), o, s);
            }
            var (sp4, o4, s4) = Plan(4); var (sp8, _, _) = Plan(8);
            var belt = sp4.FirstOrDefault(x => Tables.Default.Space.Zones[x.Zone].Name == "작은 바위 띠");
            double rIn = o4.DistanceAt(belt.From), rBefore = o4.DistanceAt(belt.From - 1), rOut = o4.DistanceAt(belt.To), rLast = o4.DistanceAt(belt.To - 1);
            bool kessler8 = sp8.Any(x => Tables.Default.Space.Zones[x.Zone].Name == "옛 정거장 파편 지대"), kessler4 = sp4.Any(x => Tables.Default.Space.Zones[x.Zone].Name == "옛 정거장 파편 지대");
            Check("내다보기: 1 → 1.52AU 길의 '작은 바위 띠'(1.12~1.3AU)는 전이 궤도 거리가 그 경계에 닿는 틱에 들고 나온다 (한 틱 안) · '옛 정거장 파편 지대'는 그 항로(8)에만",
                sp4.Count >= 1 && rBefore < 1.12 && rIn >= 1.12 && rLast <= 1.3 && rOut > 1.3 && kessler8 && !kessler4,
                $"드는 날 {(belt.From - s4.Tick) / (double)Time.TicksPerDay:0.0} · 나오는 날 {(belt.To - s4.Tick) / (double)Time.TicksPerDay:0.0} ({(belt.To - belt.From) / (double)Time.TicksPerDay:0.0}일) · 경계 {rBefore:0.00000}/{rIn:0.00000} · {rLast:0.00000}/{rOut:0.00000} · 항로 8 옛 정거장 {kessler8} · 항로 4 {kessler4}");
        }
        // ── 태양 합의 때 = SEP가 2° 안인 때 (닿은 뒤 그 궤도를 돈다고 보고 — 셈한 때에 실제로 합) ──
        {
            var s = ShipWorld.Create("Hwasal", 6);
            var o = s.Get<Outside>();
            o.Route = 4;
            s.Run(3);
            o.StartVoyage(s, 1.52); o.VoyagePlanned = 0;
            var conj = o.ConjSpans(s.Tick, s.Tick + Time.Days(600));
            long mid = conj.Count > 0 ? (conj[0].From + conj[0].To) / 2 : -1;
            double sepIn = mid > 0 ? o.SepDegAt(mid) : 99, sepOut = conj.Count > 0 ? o.SepDegAt(conj[0].From - Time.Hours(1)) : 0;
            // 실제로 그때까지 가 본다 (전이 궤도를 지나 닿고 · 그 궤도를 돈다) — 셈한 한가운데에 통신이 끊겨 있나
            long start0 = o.VoyageStart;
            double from = conj.Count > 0 ? (conj[0].From - start0) / (double)Time.TicksPerDay : 0, to = conj.Count > 0 ? (conj[0].To - start0) / (double)Time.TicksPerDay : 0;
            bool real = false;
            if (mid > 0) { o.VoyageStart -= mid - s.Tick; s.Run(2); real = o.Conjunction(s.Tick); }
            Check("내다보기: 태양 합 — 붉은 사막에 닿아 도는 배는 떠난 지 488일 무렵 합 (SEP 2° 안) · 셈한 한가운데에 실제로 가 보면 통신이 끊겨 있다",
                conj.Count >= 1 && sepIn < 2 && sepOut >= 2 && real && Math.Abs((from + to) / 2 - 488) < 5,
                $"합 {conj.Count}번 · 처음 {from:0}~{to:0}일째 · 한가운데 SEP {sepIn:0.00}° · 가 보니 합 {real}");
        }
    }

    private static void LongArcRules()
    {
        if (!Do("항해 달력")) return;
        Console.WriteLine("\n[⑫ 긴 줄기 — 항해 달력 · 준비 · 받아들이기]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        // 붉은 사막으로 가는 전이 궤도 위 — '작은 바위 띠'에 들기 hoursBefore 시간 전 (떠난 때를 당겨서)
        (Sim s, Outside o, ShipMind sm, Mind m, int a, int b) Near(double hoursBefore, ulong seed)
        {
            var s = Worlds.Full(lay, seed);
            var o = s.Get<Outside>(); var sm = s.Get<ShipMind>(); var m = s.Get<Mind>();
            int a = m.AddPerson(s, Worlds.At(lay, 3, 2)), b = m.AddPerson(s, Worlds.At(lay, 5, 2));
            m[a].C.Sleepy = 0; m[b].C.Sleepy = 0;   // 막 일어나 깨어 있다 (방송은 깨어 들은 이만)
            m[a].C.Neuro = 0.9f; m[a].C.Brave = 0.1f; m[b].C.Neuro = 0.1f; m[b].C.Brave = 0.9f;
            s.Run(5);
            o.Route = 4; o.StartVoyage(s, 1.52); o.VoyagePlanned = 0;
            var belt = o.ZoneSpans(s.Tick).First(x => Tables.Default.Space.Zones[x.Zone].Name == "작은 바위 띠");
            o.VoyageStart -= belt.From - s.Tick - Time.Hours(hoursBefore);
            return (s, o, sm, m, a, b);
        }
        // ── 사흘 앞 방송 · 사실 '항해 달력' · 예민한 이가 더 긴장한다 ──
        {
            var (s, o, sm, m, a, b) = Near(71, 51);
            float sa = m[a].C.Stress, sb = m[b].C.Stress;
            s.Run(Time.Minutes(12));   // 방송을 들은 직후 (긴장은 쉬면 몇 시간에 풀린다)
            if (Debug) Console.WriteLine($"    주컴퓨터 {sm.Online} · 달력 {sm.CalBuilt} · 일 {sm.Calendar.Count} · 깨어 {m[a].C.Up}/{m[b].C.Up} · 항로 {o.Route}");
            string said = sm.Log.Select(l => l.text).FirstOrDefault(t => t.Contains("작은 바위 띠") && t.Contains("뒤")) ?? "";
            bool fact = s.Facts.Since(0).Any(f => f.Kind == FactKind.Ahead && f.Size > 0 && f.Other == 0);
            float da = m[a].C.Stress - sa, db = m[b].C.Stress - sb;
            if (Debug) Console.WriteLine("    " + string.Join("\n    ", sm.Log.TakeLast(5).Select(x => $"{Time.Clock(x.tick)} {x.text}")));
            Check("항해 달력: 전이 궤도에 오르면 달력을 짜고 · '작은 바위 띠'에 들기 사흘 앞이면 방송한다 (며칠 · 며칠 동안) · 들은 이는 긴장한다 — 예민하고 겁 많은 이가 더",
                said.StartsWith("3일 뒤") && said.Contains("동안") && fact && da > 0 && da > 2 * db,
                $"\"{said}\" · 긴장 예민한 이 +{da:0.000} · 대담한 이 +{db:0.000}");
        }
        // ── 들면 방송하고 칸막이 문을 닫아 둔다 · 빠져나오면 방송 · 긴장이 풀리고 기쁨 ──
        {
            var (s, o, sm, m, a, b) = Near(0.2, 52);
            int door = lay.Doors.First(d => !d.External).Id;
            s.Get<Structure>().RequestDoor(door, 1);
            s.Run(Time.Minutes(40));
            var st = s.Get<Structure>();
            bool shut = st.DoorTarget[door] == 0 && sm.ZoneDoorsShut > 0;
            string inLine = sm.Log.Select(l => l.text).FirstOrDefault(t => t.Contains("지나기 시작했습니다")) ?? "";
            var ev = sm.Calendar.First(e => e.Zone >= 0);
            o.VoyageStart -= ev.To - s.Tick - Time.Minutes(5);   // 빠져나오기 5분 전으로
            m[a].C.Stress = 0.6f; float j0 = m[a].C.Joy;
            s.Run(Time.Minutes(30));
            string outLine = sm.Log.Select(l => l.text).FirstOrDefault(t => t.Contains("빠져나왔습니다")) ?? "";
            Check("들면 '지나기 시작했습니다 — 칸막이 문을 닫아 둡니다' · 열려 있던 배 안 문을 닫는다 · 빠져나오면 방송 — 들은 이는 긴장이 풀리고 기쁘다",
                shut && inLine.Length > 0 && outLine.Length > 0 && m[a].C.Stress < 0.5f && m[a].C.Joy > j0,
                $"문 닫힘 {shut} (닫은 수 {sm.ZoneDoorsShut}) · \"{inLine}\" · \"{outLine}\" · 긴장 0.6 → {m[a].C.Stress:0.00} · 기쁨 {j0:0.00} → {m[a].C.Joy:0.00}");
        }
    }
}
