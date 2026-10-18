using ShipEngine;

namespace Lab;

/// <summary>
/// 기준 평가 (docs/DEPTH.md 끝 · '최상' 기준): 재미 측정과 같은 세계(이야기꾼 켬 · 항해)를 여러 날 돌려 기준 항목을 그대로 잰다 —
/// 버팀(살아남은 이 · 첫 죽음 · 반이 죽은 날 · 물자 남은 날) · 죽음 원인 갈래 · 날마다 하이라이트 3~12 (사건 · 사람 섞임) · 엔진 여섯 개를 건넌 사슬 ·
/// 사고의 잦기와 크기 (하루마다) · 원인 없는 큰 일 · 배 갈라짐과 그 뒤 산 사람 · 한 틱 걸린 시간 → 60프레임을 지키며 낼 수 있는 배속.
/// 한 판마다 'JUDGE {…}' 한 줄(JSON)을 남긴다 — 여러 프로세스로 나눠 돌리고 tools/judge.py로 묶는다.
/// 쓰기: Lab --judge [배,배…] [날] [시드 수] [가혹함] [첫 시드 번호(0부터)]
/// </summary>
public static partial class Program
{
    private static readonly FactKind[] Accidents =
    {
        FactKind.Ignition, FactKind.WallBreach, FactKind.WallCrack, FactKind.MachineFail, FactKind.Short, FactKind.PipeLeak, FactKind.Impact,
        FactKind.Toxic, FactKind.DoorJammed, FactKind.Flood, FactKind.Overheat, FactKind.BreakerTrip, FactKind.Sick,
    };

    internal static void Judge(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--judge").Skip(1).ToArray();
        var ships = (rest.Length > 0 ? rest[0] : "Didim").Split(',');
        double days = rest.Length > 1 ? double.Parse(rest[1]) : 3;
        int seeds = rest.Length > 2 ? int.Parse(rest[2]) : 3;
        byte level = rest.Length > 3 ? byte.Parse(rest[3]) : (byte)4;
        int first = rest.Length > 4 ? int.Parse(rest[4]) : 0;
        foreach (var ship in ships)
            for (int k = first; k < first + seeds; k++) JudgeRun(ship, (ulong)(11 + k * 7), days, level);
    }

    private static void JudgeRun(string ship, ulong seed, double days, byte level)
    {
        var lay = Layout.Ship(ship, Tables.Default);
        int beds = lay.Furniture.Count(f => f.Type == "Bed"), docks = lay.Furniture.Count(f => f.Type == "RobotDock");
        var s = ShipWorld.Crewed(ship, seed, Math.Max(2, beds), docks, 0);
        ShipWorld.Embark(s);
        var st = s.Get<Storyteller>(); st.Enabled = true; st.Level = level;
        var d = new Director(s.Layout) { KeepRing = false };
        var mind = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>();
        int crew = mind.Agents.Count(a => a.IsPerson);
        long end = (long)(days * Time.Hours(24)), dayTicks = Time.Hours(24);
        int nDays = (int)Math.Ceiling(days);
        var acc = new int[nDays]; var hurt = new int[nDays]; var died = new int[nDays];
        var causes = new SortedDictionary<string, int>();
        var accKinds = new Dictionary<FactKind, int>();
        long cursor = 0, firstDeath = -1, halfDead = -1, lastTick = 0;
        double worstHourMs = 0, hourMs = 0; long ticks = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var tickClock = new System.Diagnostics.Stopwatch();
        int deadCount = 0, splits = 0, aliveAtSplit = -1;
        int Living() => mind.Agents.Count(a => a.IsPerson && life.Bodies[a.C.Body].Alive != 0);
        // 다시 세운 기준 (docs/DEPTH.md): 버팀 ② 물자 · 설비 순환 · ③ 값이 터지거나 굳지 않는다 · 얽힘 = 엔진 사이의 반응
        var f0 = s.Get<Field>();
        var holeSince = new long[s.Layout.N];
        var dFood = new double[nDays]; var dWater = new double[nDays]; var dAir = new double[nDays]; var dPatch = new int[nDays]; var dRepair = new int[nDays];
        var dBroken = new int[nDays]; var dHoleAge = new double[nDays]; var dHoleCm2 = new double[nDays]; var dO2 = new double[nDays]; var dStuck = new int[nDays];
        for (int i = 0; i < nDays; i++) dO2[i] = 99;
        int nan = 0;
        var pairs = new int[14, 14];
        var linkSeen = new HashSet<int>();
        // 관찰 (JUDGE_SEEDS=1): 씨앗마다 그 뿌리에서 난 일의 가장 높은 하이라이트 점수 — 어떤 씨앗이 볼 만한 일을 낳나
        bool seedWatch = Environment.GetEnvironmentVariable("JUDGE_SEEDS") == "1";
        var seedBest = new Dictionary<int, (float spot, FactKind kind, long tick, int n)>();
        for (long t = 0; t < end; t++)
        {
            tickClock.Restart();
            s.Step();
            tickClock.Stop();
            double ms = tickClock.Elapsed.TotalMilliseconds;
            hourMs += ms; ticks++;
            if (t % Time.TicksPerHour == Time.TicksPerHour - 1) { worstHourMs = Math.Max(worstHourMs, hourMs / Time.TicksPerHour); hourMs = 0; }
            d.Observe(s);
            int day = (int)Math.Min(nDays - 1, t / dayTicks);
            foreach (var f in s.Facts.Since(cursor))
            {
                if (seedWatch && f.Cause != 0 && f.Kind != FactKind.StorySeed)
                {
                    var rc = s.Causes.Chain(f.Cause);
                    if (rc.Count > 0 && st.Seeds.Exists(x => x.Cause == rc[0]))
                    {
                        float sp = d.SpotScore(f.Cell);
                        var o = seedBest.GetValueOrDefault(rc[0]);
                        seedBest[rc[0]] = sp > o.spot ? (sp, f.Kind, f.Tick, o.n + 1) : (o.spot, o.kind, o.tick, o.n + 1);
                    }
                }
                if (f.Cause != 0 && linkSeen.Add(f.Cause))
                {
                    // 엔진 사이의 반응: 원인 사슬에서 이웃한 두 마디의 엔진이 다르면 (마디마다 한 번)
                    var ch = s.Causes.Chain(f.Cause);
                    for (int i = 1; i < ch.Count; i++)
                    {
                        int e1 = Director.EngineOf(s.Causes[ch[i - 1]].Kind), e2 = Director.EngineOf(s.Causes[ch[i]].Kind);
                        if (e1 > 0 && e2 > 0 && e1 != e2) pairs[e1, e2]++;
                    }
                    int ef = Director.EngineOf(f.Kind), el = ch.Count > 0 ? Director.EngineOf(s.Causes[ch[^1]].Kind) : 0;
                    if (ef > 0 && el > 0 && ef != el) pairs[el, ef]++;
                }
                if (Array.IndexOf(Accidents, f.Kind) >= 0) { acc[day]++; accKinds[f.Kind] = accKinds.GetValueOrDefault(f.Kind) + 1; }
                else if (f.Kind is FactKind.Hurt or FactKind.Collapsed) hurt[day]++;
                else if (f.Kind == FactKind.ShipSplit) { splits++; if (aliveAtSplit < 0) aliveAtSplit = Living(); }
                else if (f.Kind == FactKind.Died)
                {
                    died[day]++; deadCount++;
                    if (firstDeath < 0) firstDeath = f.Tick;
                    if (halfDead < 0 && Living() * 2 <= crew) halfDead = f.Tick;
                    string why = s.Causes.Text(f.Cause);
                    int dash = why.IndexOf("— ", StringComparison.Ordinal);
                    why = dash >= 0 ? why[(dash + 2)..] : why;
                    causes[why] = causes.GetValueOrDefault(why) + 1;
                }
            }
            cursor = s.Facts.Total;
            lastTick = t;
            if (t % Time.TicksPerHour == 0)
            {
                double cm2 = 0, age = 0;
                var hnb = new int[4];
                bool Leaky(int c) { s.Layout.Neighbors(c, hnb); foreach (int j in hnb) if (j >= 0 && s.Layout.IsSpace(j) && f0.Severed[j] == 0) return true; return false; }   // 배 안 공기에 닿은 구멍만 (벽 속 · 바깥에만 닿은 구멍은 새지 않는다)
                for (int c = 0; c < s.Layout.N; c++)
                {
                    if (f0.Hole[c] > 0 && Leaky(c)) { if (holeSince[c] == 0) holeSince[c] = t + 1; cm2 += f0.Hole[c] * 1e4; age = Math.Max(age, (t + 1 - holeSince[c]) / (double)Time.TicksPerHour); }
                    else holeSince[c] = 0;
                }
                int broken = 0;
                foreach (var dv in net.Devices) for (int pp = 0; pp < 4; pp++) if (net.Broken[dv.Id * 4 + pp] != 0) { broken++; break; }
                int stuck = 0; double o2 = 99;
                foreach (var a in mind.Agents)
                {
                    if (!a.IsPerson) continue;
                    var b = life.Bodies[a.C.Body];
                    if (b.Alive == 0) continue;
                    double po2 = f0.Partial(s.Tables.gO2, b.Cell), pk = f0.Pressure(b.Cell);
                    if (double.IsNaN(po2) || double.IsNaN(pk) || double.IsNaN(b.SpO2) || double.IsNaN(b.CoreK)) nan++;
                    if (b.Mask == 0 && b.Suit == 0) o2 = Math.Min(o2, po2);
                    if (b.Asleep == 0 && b.Conscious != 0 && a.C.Goal is not (GoalKind.Sleep or GoalKind.Work or GoalKind.Down) && t - a.C.GoalSince > Time.Hours(8)) stuck++;
                }
                dFood[day] = s.Book.DaysLeft((int)Res.Food, s.Tick); dWater[day] = s.Book.DaysLeft((int)Res.Water, s.Tick); dAir[day] = net.AirReserve;
                dPatch[day] = mind.TotalOf(mind.iPatch); dRepair[day] = mind.TotalOf(mind.iRepair); dBroken[day] = Math.Max(dBroken[day], broken);
                dHoleAge[day] = Math.Max(dHoleAge[day], age); dHoleCm2[day] = Math.Max(dHoleCm2[day], cm2); dO2[day] = Math.Min(dO2[day], o2); dStuck[day] = Math.Max(dStuck[day], stuck);
            }
            if (deadCount > 0 && died[day] > 0 && Living() == 0) break;   // 아무도 없다 — 버티지 못했다 (태운 생존자의 죽음도 세지만 끝은 산 사람이 없을 때)
        }
        double ran = (lastTick + 1) / (double)dayTicks;
        int alive = mind.Agents.Count(a => a.IsPerson && life.Bodies[a.C.Body].Alive != 0);
        var hlDay = new int[nDays]; var hlPeople = new int[nDays];
        foreach (var h in d.Highlights)
        {
            int day = (int)Math.Min(nDays - 1, h.Tick / dayTicks);
            hlDay[day]++;
            if (Director.PeopleFact(h.Kind) || h.Kind is FactKind.Aim or FactKind.Pilfer) hlPeople[day]++;
        }
        int fullDays = Math.Max(1, (int)Math.Floor(ran));
        int inRange = Enumerable.Range(0, fullDays).Count(i => hlDay[i] >= 3 && hlDay[i] <= 12);
        int mixed = Enumerable.Range(0, fullDays).Count(i => hlPeople[i] > 0 && hlPeople[i] < hlDay[i]);
        var wide = d.AllChains().OrderByDescending(c => c.Count).ThenByDescending(c => c.Facts).FirstOrDefault();
        double msTick = clock.Elapsed.TotalMilliseconds / Math.Max(1, ticks);
        if (seedWatch)
            foreach (var sd in st.Seeds)
            {
                var b = seedBest.GetValueOrDefault(sd.Cause);
                Console.WriteLine($"    씨앗 {Time.Clock(sd.Tick)} {sd.Kind} ({Storyteller.ActName(sd.Act)}) → 사실 {b.n} · 가장 높은 {b.spot:0} {(b.n > 0 ? FactNames.Of(b.kind) + " " + Time.Clock(b.tick) : "")}{(b.spot >= Director.HighlightAt ? " ★" : "")} · {s.Causes.Text(sd.Cause)}");
            }
        if (Environment.GetEnvironmentVariable("JUDGE_SPOTS") == "1")
            for (int h = 0; h < d.HourBest.Count; h++) Console.WriteLine($"    {h / 24}일 {h % 24:00}시 가장 높은 후보 {d.HourBest[h].spot:0} {FactNames.Of(d.HourBest[h].kind)}{(d.HourBest[h].spot >= Director.HighlightAt ? " ★" : "")}");
        // 60프레임: 한 프레임 16.7ms 중 그리기 몫(약 4.7ms)을 빼고 12ms를 엔진에 — 한 틱 2.4초
        double maxSpeed = 60 * (12 / Math.Max(1e-6, worstHourMs)) * Time.SecondsPerTick;
        double food = s.Book.DaysLeft((int)Res.Food, s.Tick), water = s.Book.DaysLeft((int)Res.Water, s.Tick);
        string J(IEnumerable<int> xs) => "[" + string.Join(",", xs) + "]";
        string JD(IEnumerable<double> xs, double cap = 9999) => "[" + string.Join(",", xs.Select(x => double.IsInfinity(x) || x > cap ? cap.ToString(System.Globalization.CultureInfo.InvariantCulture) : x.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))) + "]";
        // 하이라이트: 가장 긴 조용한 시간 · 주마다 갈래 수
        var hts = d.Highlights.Select(h => h.Tick).OrderBy(x => x).ToList();
        double quiet = 0; long prevH = 0;
        foreach (long ht in hts) { quiet = Math.Max(quiet, (ht - prevH) / (double)Time.TicksPerHour); prevH = ht; }
        quiet = Math.Max(quiet, (lastTick - prevH) / (double)Time.TicksPerHour);
        var weekKinds = Enumerable.Range(0, Math.Max(1, fullDays / 7)).Select(w => d.Highlights.Where(h => h.Tick / (7 * dayTicks) == w).Select(h => h.Kind).Distinct().Count()).ToList();
        var pairList = new List<string>();
        for (int a = 1; a < 14; a++) for (int b = 1; b < 14; b++) if (pairs[a, b] > 0) pairList.Add($"\"{a}>{b}\":{pairs[a, b]}");
        string Esc(string x) => x.Replace("\\", "\\\\").Replace("\"", "\\\"");
        Console.WriteLine($"\n  {ship} 시드 {seed} · 사람 {crew} · {ran:0.0}일 · {clock.Elapsed.TotalSeconds:0}초 — 산 사람 {alive}/{crew} · 죽음 {deadCount} ({string.Join(" · ", causes.Select(kv => $"{kv.Key} {kv.Value}"))})");
        Console.WriteLine($"    하이라이트 날마다 {string.Join(" ", hlDay.Take(fullDays))} (3~12인 날 {inRange}/{fullDays} · 사건과 사람이 섞인 날 {mixed}) · 가장 넓은 사슬 {wide?.Count ?? 0}개 엔진 ({wide?.Path}) · 여섯 개 넘게 {d.ChainsWith(6)} · 원인 없는 큰 일 {d.Orphans.Values.Sum()}{(d.Orphans.Count > 0 ? " (" + string.Join(" ", d.Orphans.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{Director.FactText(kv.Key)} {kv.Value}")) + ")" : "")} · 배 갈라짐 {splits}{(splits > 0 ? $" (그때 산 사람 {aliveAtSplit} → 끝에 {alive})" : "")}");
        Console.WriteLine($"    사고 날마다 {string.Join(" ", acc.Take(fullDays))} ({string.Join(" ", accKinds.OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{FactNames.Of(kv.Key)} {kv.Value}"))}) · 다침/쓰러짐 {string.Join(" ", hurt.Take(fullDays))} · 식량 {food:0}일 · 물 {water:0}일 · 예비 공기 {net.AirReserve:0}몰 · 마스크 {mind.MasksWorn} · 긴급 공기 {s.Get<ShipMind>().EmergencyAir} · 거짓말 {s.Get<Society>().LiesTold}(들통 {s.Get<Society>().LiesExposed}) · 회의 {s.Get<Society>().Councils}(재판 {s.Get<Society>().Trials} 유죄 {s.Get<Society>().Convictions} · 선거 {s.Get<Society>().Elections} · 반란 {s.Get<Society>().Mutinies} · 못 열림 {s.Get<Society>().CouncilsFailed} · 흩어짐 {s.Get<Society>().CouncilsBroken}) · 대화 {s.Get<Society>().Talks}(다툼 {s.Get<Society>().Arguments} · 사과 {s.Get<Society>().Apologies} · 소문 {s.Get<Society>().Rumors} · 편듦 {s.Get<Society>().Sides} · 말림 {s.Get<Society>().Mediations} · 몸싸움 {s.Get<Society>().Fights} · 밤 깸 {s.Get<Society>().NightWakes}) · 대피 {s.Get<ShipMind>().Shelters} · 한 틱 {msTick:0.00}ms (가장 느린 시간 {worstHourMs:0.00}ms → 60프레임 배속 {maxSpeed:0}배)");
        Console.WriteLine($"JUDGE {{\"ship\":\"{ship}\",\"seed\":{seed},\"crew\":{crew},\"days\":{days},\"ran\":{ran:0.00},\"alive\":{alive},\"deaths\":{deadCount},"
            + $"\"firstDeath\":{(firstDeath < 0 ? -1 : firstDeath / (double)dayTicks):0.00},\"halfDead\":{(halfDead < 0 ? -1 : halfDead / (double)dayTicks):0.00},"
            + $"\"causes\":{{{string.Join(",", causes.Select(kv => $"\"{Esc(kv.Key)}\":{kv.Value}"))}}},"
            + $"\"hl\":{J(hlDay.Take(fullDays))},\"hlPeople\":{J(hlPeople.Take(fullDays))},\"acc\":{J(acc.Take(fullDays))},\"hurt\":{J(hurt.Take(fullDays))},\"died\":{J(died.Take(fullDays))},"
            + $"\"maxEngines\":{wide?.Count ?? 0},\"six\":{d.ChainsWith(6)},\"five\":{d.ChainsWith(5)},\"orphans\":{d.Orphans.Values.Sum()},\"splits\":{splits},\"aliveAtSplit\":{aliveAtSplit},"
            + $"\"food\":{Math.Min(9999, food):0},\"water\":{Math.Min(9999, water):0},\"air\":{net.AirReserve:0},\"msTick\":{msTick:0.000},\"worstMs\":{worstHourMs:0.000},\"speed\":{maxSpeed:0},"
            + $"\"dFood\":{JD(dFood.Take(fullDays))},\"dWater\":{JD(dWater.Take(fullDays))},\"dAir\":{JD(dAir.Take(fullDays), 1e9)},\"dPatch\":{J(dPatch.Take(fullDays))},\"dRepair\":{J(dRepair.Take(fullDays))},"
            + $"\"dBroken\":{J(dBroken.Take(fullDays))},\"dHoleAge\":{JD(dHoleAge.Take(fullDays))},\"dHoleCm2\":{JD(dHoleCm2.Take(fullDays))},\"dO2\":{JD(dO2.Take(fullDays))},\"dStuck\":{J(dStuck.Take(fullDays))},"
            + $"\"nan\":{nan},\"quietH\":{quiet:0.0},\"weekKinds\":{J(weekKinds)},\"pairs\":{{{string.Join(",", pairList)}}}}}");
    }
}
