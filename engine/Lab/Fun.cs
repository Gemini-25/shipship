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
            if (_funFacts.to > 0 && Environment.GetEnvironmentVariable("FUN_STATE") == "1" && t % (Environment.GetEnvironmentVariable("FUN_EVERY") is string fe ? (long)(double.Parse(fe) * Time.Hours(1)) : Time.Hours(1)) == 0 && t >= Time.Hours(_funFacts.from) && t <= Time.Hours(_funFacts.to))
            {
                var fs = s.Get<Field>(); var ns = s.Get<Network>(); var ss = s.Get<Structure>();
                double kpa = 0; int n = 0; foreach (var r in s.Layout.Rooms) foreach (int c in r.Cells) { kpa += fs.Pressure(c); n++; }
                double holes = 0; int leaks = 0; for (int c = 0; c < s.Layout.N; c++) if (fs.Hole[c] > 0 && fs.Hole[c] < 2) { holes += fs.Hole[c]; leaks++; }
                var o2 = ns.Devices.Where(x => x.Def.Role == "o2gen").Select(x => $"{x.Name}{(ns.Working(x.Id) ? "돎" : "섬")}{ns.Duty[x.Id]:0.0}");
                if (Environment.GetEnvironmentVariable("FUN_HOLES") == "1")
                    for (int c = 0; c < s.Layout.N; c++)
                        if (fs.Hole[c] > 0 && fs.Hole[c] < 2)
                        {
                            Span<int> nbh = stackalloc int[4]; s.Layout.Neighbors(c, nbh);
                            string sides = string.Join("/", nbh.ToArray().Where(j => j >= 0).Select(j => s.Layout.Kind[j] == CellKind.Outside ? "밖" : s.Layout.Room[j] >= 0 ? $"{s.Layout.Rooms[s.Layout.Room[j]].Name}:{fs.Pressure(j):0}" : s.Layout.Kind[j].ToString()));
                            Console.WriteLine($"        구멍 {s.Layout.Kind[c]} ({s.Layout.X(c)},{s.Layout.Y(c)}) {fs.Hole[c] * 1e4:0}cm² {(s.Layout.Room[c] >= 0 ? s.Layout.Rooms[s.Layout.Room[c]].Name : "")} · {sides} · {s.Causes.Story(ss.DamageCause[c])}");
                        }
                if (Environment.GetEnvironmentVariable("FUN_INC") == "1" && s.Find<ShipMind>() is { } smi)
                    foreach (var x in smi.Incidents.Where(x => x.Active != 0))
                        Console.WriteLine($"        사건 {x.Kind} {(x.Room >= 0 ? s.Layout.Rooms[x.Room].Name : "")} ({s.Layout.X(Math.Max(0, x.Cell))},{s.Layout.Y(Math.Max(0, x.Cell))}) 봉쇄 {x.Sealed} 로봇 {x.RobotA}/{x.RobotB} 번 {x.Tries} 사람 {x.Person}");
                if (Environment.GetEnvironmentVariable("FUN_AGENTS") == "1")
                    foreach (var ag in mind.Agents)
                        Console.WriteLine($"        {mind.NameOf(ag.Id)} ({s.Layout.X(ag.C.Cell)},{s.Layout.Y(ag.C.Cell)}) {(s.Layout.Room[ag.C.Cell] >= 0 ? s.Layout.Rooms[s.Layout.Room[ag.C.Cell]].Name : "")} · {mind.Thought(ag.Id)} · 믿음 {string.Join(",", ag.Beliefs.Where(b => b.Conf >= 0.3f && b.Kind is BeliefKind.Breach or BeliefKind.Leak or BeliefKind.Vacuum).Select(b => $"{Mind.BeliefName(b.Kind)}@({s.Layout.X(b.Cell)},{s.Layout.Y(b.Cell)}):{b.Conf:0.0}"))}");
                Console.WriteLine($"      ◦ {Time.Clock(t)} 기압 {kpa / Math.Max(1, n):0.0}kPa · 산소 {ns.PpO2:0.0}kPa · 예비 {ns.AirReserve:0} · 구멍 {leaks}곳 {holes * 1e4:0.0}cm² · 금 {Enumerable.Range(0, s.Layout.N).Count(c => ss.Crack[c] > 0)} · 물 {ns.WaterTank / 55.5:0}L · {string.Join(" ", o2)} · 공급 {ns.Supplied:0}/{ns.Demanded:0}kW · 빠짐 {fs.VentedTotal:0}");
            }
            if (_funFacts.to > 0)
                foreach (var f in s.Facts.Since(cursor))
                    if (f.Tick >= Time.Hours(_funFacts.from) && f.Tick <= Time.Hours(_funFacts.to) && Director.WeightOf(f) >= 12)
                        Console.WriteLine($"      · {Time.Clock(f.Tick)} {Director.FactText(f.Kind)} {(f.Actor >= 0 && f.Actor < mind.Agents.Count ? mind.NameOf(f.Actor) : "")} {(f.Cell >= 0 && s.Layout.Room[f.Cell] >= 0 ? s.Layout.Rooms[s.Layout.Room[f.Cell]].Name : "")} — {Trim(s.Causes.Story(f.Cause), 220)}");
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
            + Helm(s, mind);
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

    /// <summary>FUN_FACTS=시작시-끝시: 그 사이의 큰 일(무게 12 넘게)을 원인 사슬과 함께 적는다 (재미 측정과 같은 세계 — 연출이 미리 보기로 복제를 만들어도 같다).</summary>
    private static readonly (double from, double to) _funFacts = Environment.GetEnvironmentVariable("FUN_FACTS") is string ff && ff.Split('-') is { Length: 2 } fp ? (double.Parse(fp[0]), double.Parse(fp[1])) : (0, 0);

    private static string Trim(string t, int n) => t.Length <= n ? t : "…" + t[^n..];

    /// <summary>⑤ 바깥 한 줄: 점화 · 넘어짐 · 레이더 · 비키기 · 다른 배 · 추진제.</summary>
    private static string Helm(Sim s, Mind mind)
    {
        var o = s.Get<Outside>(); var sm = s.Get<ShipMind>();
        return $" / 바깥: 점화 {o.BurnCount} (미룸 {sm.BurnsDelayed} · 놓침 {o.MissedBurns}) · 넘어짐 {mind.Falls} (붙잡음 {mind.BracedOnBurn}) · 레이더 운석 {sm.RocksSeen} · 비킴 {sm.DodgesAvoided}/{sm.DodgesTried} · 충격 대비 {sm.BraceCalls}"
            + $" · 다른 배 {o.ContactSeq} · 교신 {sm.Hails} · 구함 {sm.Rescued} · 거절 {sm.Declined} · 건짐 {sm.Salvaged} · 추진제 {o.PropellantKg / Math.Max(1, o.PropellantStart):P0} · {o.Distance:0.000}AU";
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
                if (Environment.GetEnvironmentVariable("FUN_SEED") is string only && only != (11 + k * 7).ToString()) continue;
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
