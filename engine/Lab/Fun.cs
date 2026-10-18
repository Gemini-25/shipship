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
        double Calm, int Firsts, int Seeds, int Fruit, double ScreenMinPerDay, double Score, List<string> Titles, string Daily,
        string Camera = "", string ChainLine = "", int MaxEngines = 0, int MaxLine = 0, double Before = 0, double Missed = 0, double ChainSeen = 0, List<string>? Wide = null);

    internal static FunResult FunRun(string ship, ulong seed, double days, StoryStyle style = StoryStyle.Balanced, byte level = 4)
    {
        var lay = Layout.Ship(ship, Tables.Default);
        int beds = lay.Furniture.Count(f => f.Type == "Bed"), docks = lay.Furniture.Count(f => f.Type == "RobotDock");
        var s = ShipWorld.Crewed(ship, seed, Math.Max(2, beds), docks, 0); // 침대마다 한 사람 · 충전독마다 로봇 하나
        if (Environment.GetEnvironmentVariable("FUN_STAY") != "1") ShipWorld.Embark(s);   // 항해에 나선다 (출발 점화 · 지나가는 배)
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
        int seedN = st.Seeds.Count(x => x.Kind != SeedKind.GoodNews);   // 반가운 소식은 일을 만들려는 씨앗이 아니다
        double sFruit = seedN > 0 ? 15 * fruit.Count / (double)seedN : 0;
        double penalty = life.Deaths / days > Math.Max(1, crew * 0.15) ? 20 : 0;
        double score = Math.Max(0, sHl + sKinds + sDepth + sNear + sCalm + sFruit - penalty);
        var titles = d.Highlights.OrderByDescending(h => h.Score).Take(6)
            .Select(h => $"{Time.Clock(h.Tick)} [{Director.FactText(h.Kind)}] {h.Title}").ToList();
        // 일상: 쉬는 시간에 한 일 · 밥 짓기 · 몸의 시계 (아침형 ~ 저녁형)
        var chrono = mind.Agents.Where(a => a.IsPerson).Select(a => a.C.Chrono).DefaultIfEmpty(0).ToList();
        string daily = "쉼 " + string.Join(" · ", Enumerable.Range(0, mind.HobbyCount.Length).Where(h => mind.HobbyCount[h] > 0).Select(h => $"{Mind.HobbyName((Hobby)h)} {mind.HobbyCount[h]}"))
            + $" / 밥 짓기 {mind.Cooked} · 깜빡 {mind.Forgot} / 몸의 시계 {chrono.Min():+0.0;-0.0}~{chrono.Max():+0.0;-0.0}시간"
            + $" / 같이 먹음 {mind.AteTogether} · 따라 나감 {mind.Followed} · 방을 꺼리게 됨 {mind.Dreads}"
            + $" / 돌봄 {mind.Treats}(맨손 {mind.TreatsBare}) · 곪음 {life.Infections} · 패혈증 {life.Sepses} · 감기 {life.Colds}"
            + $" / 막 {st.Arc} · 큰 일로 여파 {st.Aftermaths} · 형편 보고 쉼 {st.Mercies} · 열매 {st.Fruited} · 헛돎 {st.Fizzled} · 털어놓음 {s.Get<Society>().Confessions} · 숨김 {s.Get<Society>().SecretsKept}"
            + $" / 씨앗 {string.Join(" ", st.Seeds.GroupBy(x => x.Kind).Select(g => $"{Storyteller.SeedName(g.Key)} {g.Count()}"))}"
            + $" / 소리: 가려 못 들음 {mind.Masked} · 이상한 기계 소리 알아챔 {mind.GrindHeard} · 듣고 미리 갈기 {mind.EarMaintained} · 낸 소리 {s.Get<Sound>().Emitted} · 큰 소리(100dB 넘게) {s.Facts.CountOf(FactKind.LoudSound)}"
            + Helm(s, mind) + Mend(s, mind);
        // 사슬: 건넌 엔진 수 · 가장 넓은 사슬 · 원인 없는 큰 일
        var wide = d.AllChains().OrderByDescending(c => c.Count).ThenByDescending(c => c.Facts).Take(2).ToList();
        var deep = d.AllChains().OrderByDescending(c => c.Line).ThenByDescending(c => c.Depth).FirstOrDefault();
        int maxE = wide.Count > 0 ? wide[0].Count : 0, maxLine = deep?.Line ?? 0;
        string chainLine = $"엔진 3개 넘게 {d.ChainsWith(3)} · 4개 {d.ChainsWith(4)} · 5개 {d.ChainsWith(5)} · 6개 {d.ChainsWith(6)} · 가장 넓은 것 {maxE}개 · 새 엔진으로 번짐 {d.Spreads}"
            + $" · 원인 없는 큰 일 {d.Orphans.Values.Sum()}" + (d.Orphans.Count > 0 ? " (" + string.Join(" ", d.Orphans.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{Director.FactText(kv.Key)} {kv.Value}")) + ")" : "");
        var wideLines = wide.Select(c => $"넓게 [{c.Count}] {c.Path} · 일 {c.Facts} · 깊이 {c.Depth} · 가지 {c.Leaves.Count} — {Trim(s.Causes.Story(c.Cause), 160)}").ToList();
        if (deep != null) wideLines.Add($"한 줄로 [{deep.Line}] {deep.LinePath} · 깊이 {s.Causes.Depth(deep.LineCause)} — {Trim(s.Causes.Story(deep.LineCause), 200)}");
        chainLine += $" · 한 줄로 가장 많이 건넌 것 {maxLine}개";
        double pct(int a, int b) => b > 0 ? (double)a / b : 0;
        return new FunResult(ship, seed, crew, days, hl, kinds, depth, d.NearMisses, life.Deaths, calm, d.Firsts, seedN, fruit.Count,
            d.ScreenSeconds / 60 / days, score, titles, daily, d.CameraLine(), chainLine, maxE, maxLine, pct(d.SeenBefore, d.Watched), pct(d.MissedStory, d.Watched), pct(d.ChainSeen, d.ChainWatched), wideLines);
    }

    private static string Trim(string t, int n) => t.Length <= n ? t : "…" + t[^n..];

    /// <summary>⑤ 바깥 한 줄: 점화 · 넘어짐 · 레이더 · 비키기 · 다른 배 · 추진제.</summary>
    private static string Helm(Sim s, Mind mind)
    {
        var o = s.Get<Outside>(); var sm = s.Get<ShipMind>();
        return $" / 바깥: 점화 {o.BurnCount} (미룸 {sm.BurnsDelayed} · 놓침 {o.MissedBurns}) · 넘어짐 {mind.Falls} (붙잡음 {mind.BracedOnBurn}) · 레이더 운석 {sm.RocksSeen} · 비킴 {sm.DodgesAvoided}/{sm.DodgesTried} · 충격 대비 {sm.BraceCalls}"
            + $" · 다른 배 {o.ContactSeq} · 교신 {sm.Hails} · 구함 {sm.Rescued} · 거절 {sm.Declined} · 건짐 {sm.Salvaged} · 추진제 {o.PropellantKg / Math.Max(1, o.PropellantStart):P0} · {o.Distance:0.000}AU";
    }

    /// <summary>⑥ 고쳐 쓰기 한 줄: 합선 · 연장선 · 떼어 쓰기 · 헌 부품.</summary>
    private static string Mend(Sim s, Mind mind)
    {
        var net = s.Get<Network>(); var sm = s.Get<ShipMind>();
        return $" / 고쳐 쓰기: 합선 {net.Shorts} · 연장선 {net.JumpersLaid} (합선 {net.JumperShorts} · 봉쇄로 뽑음 {sm.JumpersCut} · 걷음 {net.JumpersPulled}) · 떼어 쓰기 {mind.Salvages} · 고침 {mind.Repaired}";
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
                Console.WriteLine($"    카메라: {r.Camera}");
                Console.WriteLine($"    사슬: {r.ChainLine}");
                foreach (var w in r.Wide ?? new()) Console.WriteLine($"      {w}");
                foreach (var t in r.Titles) Console.WriteLine($"      {t}");
            }
        Console.WriteLine($"\n  카메라: 먼저 와 있음 {all.Average(r => r.Before):P0} · 못 본 이야기 {all.Average(r => r.Missed):P0} · 연쇄의 일 본 것 {all.Average(r => r.ChainSeen):P0} · 가장 넓은 사슬 {all.Max(r => r.MaxEngines)}개 엔진 · 한 줄로 {all.Max(r => r.MaxLine)}개");
        Console.WriteLine($"  평균 점수 {all.Average(r => r.Score):0} · 하이라이트 하루 {all.Average(r => r.HlPerDay):0.0} · 죽음 {all.Sum(r => r.Deaths)} · 씨앗 {all.Sum(r => r.Seeds)} → 일 {all.Sum(r => r.Fruit)}");
    }
}
