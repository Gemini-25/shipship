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
        if (Environment.GetEnvironmentVariable("JUDGE_SPOTS") == "1")
            for (int h = 0; h < d.HourBest.Count; h++) Console.WriteLine($"    {h / 24}일 {h % 24:00}시 가장 높은 후보 {d.HourBest[h].spot:0} {FactNames.Of(d.HourBest[h].kind)}{(d.HourBest[h].spot >= Director.HighlightAt ? " ★" : "")}");
        // 60프레임: 한 프레임 16.7ms 중 그리기 몫(약 4.7ms)을 빼고 12ms를 엔진에 — 한 틱 2.4초
        double maxSpeed = 60 * (12 / Math.Max(1e-6, worstHourMs)) * Time.SecondsPerTick;
        double food = s.Book.DaysLeft((int)Res.Food, s.Tick), water = s.Book.DaysLeft((int)Res.Water, s.Tick);
        string J(IEnumerable<int> xs) => "[" + string.Join(",", xs) + "]";
        string Esc(string x) => x.Replace("\\", "\\\\").Replace("\"", "\\\"");
        Console.WriteLine($"\n  {ship} 시드 {seed} · 사람 {crew} · {ran:0.0}일 · {clock.Elapsed.TotalSeconds:0}초 — 산 사람 {alive}/{crew} · 죽음 {deadCount} ({string.Join(" · ", causes.Select(kv => $"{kv.Key} {kv.Value}"))})");
        Console.WriteLine($"    하이라이트 날마다 {string.Join(" ", hlDay.Take(fullDays))} (3~12인 날 {inRange}/{fullDays} · 사건과 사람이 섞인 날 {mixed}) · 가장 넓은 사슬 {wide?.Count ?? 0}개 엔진 ({wide?.Path}) · 여섯 개 넘게 {d.ChainsWith(6)} · 원인 없는 큰 일 {d.Orphans.Values.Sum()} · 배 갈라짐 {splits}{(splits > 0 ? $" (그때 산 사람 {aliveAtSplit} → 끝에 {alive})" : "")}");
        Console.WriteLine($"    사고 날마다 {string.Join(" ", acc.Take(fullDays))} ({string.Join(" ", accKinds.OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{FactNames.Of(kv.Key)} {kv.Value}"))}) · 다침/쓰러짐 {string.Join(" ", hurt.Take(fullDays))} · 식량 {food:0}일 · 물 {water:0}일 · 예비 공기 {net.AirReserve:0}몰 · 마스크 {mind.MasksWorn} · 긴급 공기 {s.Get<ShipMind>().EmergencyAir} · 거짓말 {s.Get<Society>().LiesTold}(들통 {s.Get<Society>().LiesExposed}) · 한 틱 {msTick:0.00}ms (가장 느린 시간 {worstHourMs:0.00}ms → 60프레임 배속 {maxSpeed:0}배)");
        Console.WriteLine($"JUDGE {{\"ship\":\"{ship}\",\"seed\":{seed},\"crew\":{crew},\"days\":{days},\"ran\":{ran:0.00},\"alive\":{alive},\"deaths\":{deadCount},"
            + $"\"firstDeath\":{(firstDeath < 0 ? -1 : firstDeath / (double)dayTicks):0.00},\"halfDead\":{(halfDead < 0 ? -1 : halfDead / (double)dayTicks):0.00},"
            + $"\"causes\":{{{string.Join(",", causes.Select(kv => $"\"{Esc(kv.Key)}\":{kv.Value}"))}}},"
            + $"\"hl\":{J(hlDay.Take(fullDays))},\"hlPeople\":{J(hlPeople.Take(fullDays))},\"acc\":{J(acc.Take(fullDays))},\"hurt\":{J(hurt.Take(fullDays))},\"died\":{J(died.Take(fullDays))},"
            + $"\"maxEngines\":{wide?.Count ?? 0},\"six\":{d.ChainsWith(6)},\"five\":{d.ChainsWith(5)},\"orphans\":{d.Orphans.Values.Sum()},\"splits\":{splits},\"aliveAtSplit\":{aliveAtSplit},"
            + $"\"food\":{Math.Min(9999, food):0},\"water\":{Math.Min(9999, water):0},\"air\":{net.AirReserve:0},\"msTick\":{msTick:0.000},\"worstMs\":{worstHourMs:0.000},\"speed\":{maxSpeed:0}}}");
    }
}
