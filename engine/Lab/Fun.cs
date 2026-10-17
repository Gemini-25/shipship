using ShipEngine;

namespace Lab;

/// <summary>
/// 재미 측정기 (docs/ENGINE.md §4 ⑬ · §6): 이야기꾼을 켜고 연출이 보는 대로 하루 이상 돌려
/// 하이라이트 수 · 갈래 · 원인 사슬 깊이 · 간발의 차 · 죽음 · 조용한 화면 비율 · 처음 보는 일 · 열매 맺은 씨앗을 재고 점수로 묶는다.
/// --fun [배,배…] [날] [시드 수] [성격] [가혹함]
/// </summary>
public static partial class Program
{
    internal sealed record FunResult(string Ship, ulong Seed, int Crew, double Days, double HlPerDay, int Kinds, double Depth, int Near, long Deaths,
        double Calm, int Firsts, int Seeds, int Fruit, double ScreenMinPerDay, double Score, List<string> Titles, string Daily);

    internal static FunResult FunRun(string ship, ulong seed, double days, StoryStyle style = StoryStyle.Balanced, byte level = 4)
    {
        var lay = Layout.Ship(ship, Tables.Default);
        int beds = lay.Furniture.Count(f => f.Type == "Bed"), docks = lay.Furniture.Count(f => f.Type == "RobotDock");
        var s = ShipWorld.Crewed(ship, seed, Math.Max(2, beds), docks, 0); // 침대마다 한 사람 · 충전독마다 로봇 하나
        var st = s.Get<Storyteller>(); st.Enabled = true; st.Style = style; st.Level = level;
        var d = new Director(s.Layout) { KeepRing = false };
        var mind = s.Get<Mind>(); var life = s.Get<Life>();
        int crew = mind.Agents.Count(a => a.IsPerson);
        var seedRoots = new HashSet<int>(); var fruit = new HashSet<int>();
        long cursor = 0;
        long end = (long)(days * Time.Hours(24));
        bool verbose = Environment.GetEnvironmentVariable("FUN_LOG") == "1";
        var clock = System.Diagnostics.Stopwatch.StartNew();
        s.Measure = verbose;
        for (long t = 0; t < end; t++)
        {
            s.Step();
            if (verbose && t % Time.Hours(2) == 0 && t > 0)
            {
                Console.WriteLine($"    {Time.Clock(t)} · {clock.Elapsed.TotalSeconds:0}초 · 사실 {s.Facts.Total} · 씨앗 {st.Seeds.Count} · 하이라이트 {d.Highlights.Count} · 사람 {mind.Agents.Count(a => a.IsPerson && life.Bodies[a.C.Body].Alive != 0)} · "
                    + string.Join(" ", s.LastMs.OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"{kv.Key} {kv.Value / Time.Hours(2):0.00}ms")));
                s.LastMs.Clear();
            }
            d.Observe(s);
            foreach (var sd in st.Seeds) seedRoots.Add(sd.Cause);
            foreach (var f in s.Facts.Since(cursor))
            {
                if (f.Cause == 0 || f.Kind == FactKind.StorySeed || Director.Weight(f.Kind) < 5) continue;
                var chain = s.Causes.Chain(f.Cause);
                if (chain.Count > 0 && seedRoots.Contains(chain[0])) fruit.Add(chain[0]);
            }
            cursor = s.Facts.Total;
        }
        double hl = d.Highlights.Count / days;
        int kinds = d.Highlights.Select(h => h.Kind).Distinct().Count();
        double depth = d.Highlights.Count > 0 ? d.Highlights.Average(h => h.Cause != 0 ? s.Causes.Depth(h.Cause) : 1) : 0;
        double calm = d.CalmShare;
        // 점수 (100): 하이라이트 하루 3~12개 · 갈래 · 사슬 깊이 · 간발의 차 · 조용함과 긴장의 섞임 · 씨앗이 일이 됨 — 사람이 너무 많이 죽으면 깎는다
        double sHl = hl <= 0 ? 0 : hl < 3 ? 25 * hl / 3 : hl <= 12 ? 25 : 25 * 12 / hl;
        double sKinds = 20 * Math.Min(1, kinds / 4.0);
        double sDepth = 15 * Math.Min(1, depth / 3.0);
        double sNear = 10 * Math.Min(1, (d.NearMisses + life.Deaths) / 2.0);
        double sCalm = calm >= 0.4 && calm <= 0.85 ? 15 : 15 * Math.Max(0, 1 - Math.Abs(calm - 0.625) / 0.5);
        double sFruit = st.Seeds.Count > 0 ? 15 * fruit.Count / (double)st.Seeds.Count : 0;
        double penalty = life.Deaths / days > Math.Max(1, crew * 0.15) ? 20 : 0;
        double score = Math.Max(0, sHl + sKinds + sDepth + sNear + sCalm + sFruit - penalty);
        var titles = d.Highlights.OrderByDescending(h => h.Score).Take(6)
            .Select(h => $"{Time.Clock(h.Tick)} [{Director.FactText(h.Kind)}] {h.Title}").ToList();
        // 일상: 쉬는 시간에 한 일 · 밥 짓기 · 몸의 시계 (아침형 ~ 저녁형)
        var chrono = mind.Agents.Where(a => a.IsPerson).Select(a => a.C.Chrono).DefaultIfEmpty(0).ToList();
        string daily = "쉼 " + string.Join(" · ", Enumerable.Range(0, mind.HobbyCount.Length).Where(h => mind.HobbyCount[h] > 0).Select(h => $"{Mind.HobbyName((Hobby)h)} {mind.HobbyCount[h]}"))
            + $" / 밥 짓기 {mind.Cooked} · 깜빡 {mind.Forgot} / 몸의 시계 {chrono.Min():+0.0;-0.0}~{chrono.Max():+0.0;-0.0}시간"
            + $" / 같이 먹음 {mind.AteTogether} · 따라 나감 {mind.Followed} · 방을 꺼리게 됨 {mind.Dreads}"
            + $" / 돌봄 {mind.Treats}(맨손 {mind.TreatsBare}) · 곪음 {life.Infections} · 패혈증 {life.Sepses} · 감기 {life.Colds}";
        return new FunResult(ship, seed, crew, days, hl, kinds, depth, d.NearMisses, life.Deaths, calm, d.Firsts, st.Seeds.Count, fruit.Count,
            d.ScreenSeconds / 60 / days, score, titles, daily);
    }

    internal static void Fun(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--fun").Skip(1).ToArray();
        var ships = (rest.Length > 0 ? rest[0] : "Didim").Split(',');
        double days = rest.Length > 1 ? double.Parse(rest[1]) : 1;
        int seeds = rest.Length > 2 ? int.Parse(rest[2]) : 2;
        var style = rest.Length > 3 ? Enum.Parse<StoryStyle>(rest[3]) : StoryStyle.Balanced;
        byte level = rest.Length > 4 ? byte.Parse(rest[4]) : (byte)4;
        Console.WriteLine($"재미 측정 — {Storyteller.StyleName(style)} · 가혹함 {level} · {days}일 × 시드 {seeds}");
        var all = new List<FunResult>();
        foreach (var ship in ships)
            for (int k = 0; k < seeds; k++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var r = FunRun(ship, (ulong)(11 + k * 7), days, style, level);
                all.Add(r);
                Console.WriteLine($"\n  {ship} 시드 {r.Seed} · 사람 {r.Crew} · {sw.Elapsed.TotalSeconds:0}초 → 점수 {r.Score:0}");
                Console.WriteLine($"    하이라이트 하루 {r.HlPerDay:0.0} · 갈래 {r.Kinds} · 사슬 깊이 {r.Depth:0.0} · 간발의 차 {r.Near} · 죽음 {r.Deaths} · 처음 보는 일 {r.Firsts}");
                Console.WriteLine($"    조용한 화면 {r.Calm:P0} · 화면 하루 {r.ScreenMinPerDay:0}분 · 씨앗 {r.Seeds} → 일이 된 것 {r.Fruit}");
                Console.WriteLine($"    일상: {r.Daily}");
                foreach (var t in r.Titles) Console.WriteLine($"      {t}");
            }
        Console.WriteLine($"\n  평균 점수 {all.Average(r => r.Score):0} · 하이라이트 하루 {all.Average(r => r.HlPerDay):0.0} · 죽음 {all.Sum(r => r.Deaths)} · 씨앗 {all.Sum(r => r.Seeds)} → 일 {all.Sum(r => r.Fruit)}");
    }
}
