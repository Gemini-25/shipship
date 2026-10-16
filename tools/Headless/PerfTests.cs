using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ShipSim.Core;

// v17.7 성능 시험: 가장 큰 배(새터) 60명 · 30배속 · 복합 재난(운석 둘 + 화재 + 펌프 정지 · 배터리 바닥)에서
// 하루 시뮬 시간 · 30배속 프레임당 시뮬 시간 · 지문이 그대로인지(관찰 카메라 켬/끔).
public static partial class Program
{
    private static World PerfShip(int seed, bool disaster)
    {
        var w = World.CreateDefault(seed, 60, "Saeteo");
        for (int i = 0; i < SimTime.Hours(2); i++) w.Step();
        if (disaster) { Scenarios.Apply(w, "chaos", out _); Scenarios.Apply(w, "blackout", out _); }
        return w;
    }

    /// <summary>한 장면을 ticks 만큼 돌리며 틱별 시간을 잰다 → (전체 ms, 30배속 60fps 한 프레임(15틱) 평균 · 최악 ms, 지문).</summary>
    private static double LastAllocMb; private static int LastGc;
    private static (double total, double frameAvg, double frameWorst, uint hash, double cpu) PerfRun(World w, long ticks, Action<World>? each = null)
    {
        GC.Collect();
        var times = new double[ticks];
        double freq = Stopwatch.Frequency / 1000.0;
        long alloc0 = GC.GetTotalAllocatedBytes(); int gc0 = GC.CollectionCount(0);
        var cpu0 = Process.GetCurrentProcess().TotalProcessorTime; // 다른 일이 CPU를 나눠 쓸 때는 CPU 시간이 덜 흔들린다
        var sw = Stopwatch.StartNew();
        for (long i = 0; i < ticks; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            w.Step();
            each?.Invoke(w);
            times[i] = (Stopwatch.GetTimestamp() - t0) / freq;
        }
        sw.Stop();
        LastAllocMb = (GC.GetTotalAllocatedBytes() - alloc0) / 1048576.0; LastGc = GC.CollectionCount(0) - gc0;
        double cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpu0).TotalMilliseconds;
        const int perFrame = 30 * SimTime.TicksPerSecond / 60; // 30배속 · 60fps → 한 프레임에 15틱
        double worst = 0;
        for (long i = 0; i + perFrame <= ticks; i += perFrame) { double s = 0; for (int k = 0; k < perFrame; k++) s += times[i + k]; worst = Math.Max(worst, s); }
        LastWorstTick = Array.IndexOf(times, times.Max());
        return (sw.Elapsed.TotalMilliseconds, times.Sum() / ticks * perFrame, worst, SaveGame.StateHash(w), cpu);
    }

    private static long LastWorstTick;

    /// <summary>가장 무거운 틱을 다시 만들어(결정론) 그 한 틱만 구간별로 잰다.</summary>
    private static void PerfSpike(int seed, bool disaster, long at)
    {
        var w = PerfShip(seed, disaster);
        for (long i = 0; i < at; i++) w.Step();
        Prof.Reset(); Prof.On = true;
        var sw = Stopwatch.StartNew();
        w.Step();
        Prof.On = false;
        Console.WriteLine($"    가장 무거운 틱 {at} ({SimTime.Day(w.Tick)}일 {SimTime.Clock(w.Tick)}) · {sw.Elapsed.TotalMilliseconds:0}ms");
        foreach (var (key, ms, calls, _) in Prof.Report().Take(12)) Console.WriteLine($"      {key,-30} {ms,8:0.0}ms · {calls,6}번");
    }

    private static int RunPerfTest(int seed)
    {
        _fails = 0;
        bool prof = Environment.GetEnvironmentVariable("PERF_PROF") == "1";
        Console.WriteLine($"성능 시험 (v17.7) · 시드 {seed} · 새터 60명");
        foreach (var (name, disaster) in new[] { ("평시", false), ("복합 재난", true) })
        {
            if (Environment.GetEnvironmentVariable("PERF_ONLY") is string only && only.Length > 0 && name != only) continue;
            var w = PerfShip(seed, disaster);
            if (prof) { Prof.Reset(); Prof.On = true; }
            var r = PerfRun(w, SimTime.TicksPerDay);
            Prof.On = false;
            Console.WriteLine($"  {name} · 하루 {r.total / 1000:0.00}초 · CPU {r.cpu / 1000:0.00}초 (30배속 실시간 하루 = {SimTime.TicksPerDay / (30.0 * SimTime.TicksPerSecond):0}초) · 30배속 프레임당 시뮬 평균 {r.frameAvg:0.00}ms · 최악 {r.frameWorst:0.0}ms · 지문 {r.hash:x8} · 생존 {w.Crew.Count(c => !c.Dead)}/{w.Crew.Count} · 할당 {LastAllocMb:0}MB · GC {LastGc}번 · 격자 {w.Ship.Grid.Width}×{w.Ship.Grid.Height} · 거리장 다시 씀 {w.Paths.FloodHits} / 새로 {w.Paths.FloodMisses}");
            if (prof) foreach (var (key, ms, calls, _) in Prof.Report().Take(40)) Console.WriteLine($"      {key,-30} {ms,8:0}ms · {calls,8}번");
            if (Environment.GetEnvironmentVariable("PERF_SPIKE") == "1") PerfSpike(seed, disaster, LastWorstTick);
            Check($"{name} · 30배속을 따라간다 (하루 시뮬 < 30배속 실시간 하루의 절반)", Math.Min(r.total, r.cpu) < SimTime.TicksPerDay / (30.0 * SimTime.TicksPerSecond) * 1000 * 0.5, $"{r.total:0}ms · CPU {r.cpu:0}ms");
        }
        if (Environment.GetEnvironmentVariable("PERF_ONLY") is { Length: > 0 }) return _fails == 0 ? 0 : 1;
        // 결정론: 같은 시드 · 같은 재난 → 같은 지문
        uint H() { var w = PerfShip(seed, true); for (int i = 0; i < SimTime.Hours(6); i++) w.Step(); return SaveGame.StateHash(w); }
        uint a = H(), b = H();
        Check("결정론 · 60명 복합 재난 6시간 지문 두 번 같다", a == b, $"{a:x8} / {b:x8}");
        Console.WriteLine($"  지문(60명 · 복합 재난 · 6시간) {a:x8}" + (seed == 7 ? (a == 0xd030e585u ? " · 성능 손보기 전(4818fe9)과 같다" : " · 4818fe9 때는 d030e585") : ""));

        // 카메라가 있든 없든 같은 역사: 관찰 카메라 · 물건 따라가기 · 항해 결산이 매 틱 세상을 읽어도 지문이 같다
        var eye = new WatchScenes();
        int seen = 0, reviews = 0;
        var cw = PerfShip(seed, true);
        for (int i = 0; i < SimTime.Hours(6); i++)
        {
            cw.Step();
            seen += eye.Poll(cw).Count;
            if (i % 300 == 0) { foreach (var it in WatchScenes.Notable(cw).Take(3)) { WatchScenes.Trail(cw, it); WatchScenes.PositionOf(cw, it); } }
            if (i % 1500 == 0) { VoyageReview.Build(cw); reviews++; }
        }
        uint cam = SaveGame.StateHash(cw);
        Check("카메라 유무와 무관 · 관찰 카메라 · 물건 따라가기 · 항해 결산이 읽어도 같은 지문", cam == a, $"{cam:x8} / {a:x8} · 장면 {seen} · 결산 {reviews}");
        WatchChecks(seed);
        return _fails == 0 ? 0 : 1;
    }

    /// <summary>관찰 도구 장면: 평화로운 장면을 잡고 · 물건이 거친 손을 잇고 · 항해 결산이 한 장으로 모인다.</summary>
    private static void WatchChecks(int seed)
    {
        var w = DayOne(seed, "Hanbit");
        var eye = new WatchScenes();
        eye.Poll(w); // 지금까지는 건너뛴다
        var a = w.Crew[0]; var b = w.Crew[1]; var c = w.Crew[2];
        // 선물: 연대기에 적힌 장면은 방 · 사람과 함께 잡힌다
        w.History.Add(w, HistoryKind.Bond, $"{Ko.IGa(a.Name)} 직접 깎은 나무 새를 {b.Name}에게 선물했다", a.Room, new[] { a, b });
        // 화해 · 고백 · 추모 (하루 기록 · 연대기)
        w.Log.Add(w.Tick, LogKind.Life, $"{b.Name}에게 컵 일로 의심한 걸 사과했다", c.Id);
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.WaGwa(a.Name)} {c.Name} — 연인이 됐다", a.Id);
        w.History.Add(w, HistoryKind.Death, $"식당에서 {Ko.EulReul(c.Name)} 추모했다 — 5명이 모였다", a.Room);
        var shots = eye.Poll(w);
        Check("관찰 카메라 · 선물을 잡는다 (방 · 사람 · 자리)", shots.Any(x => x.Kind == WatchKind.Gift && x.At != null && x.Who.Contains(a.Id) && x.Who.Contains(b.Id)), string.Join(" / ", shots.Select(x => x.Kind + ":" + x.Text)));
        Check("관찰 카메라 · 화해 · 고백 · 추모도 잡는다", shots.Any(x => x.Kind == WatchKind.Reconcile) && shots.Any(x => x.Kind == WatchKind.Confession) && shots.Any(x => x.Kind == WatchKind.Memorial));
        Check("관찰 카메라 · 같은 장면을 두 번 잡지 않는다", eye.Poll(w).Count == 0);
        // 늦게 온 관객: 함께하는 장면의 기록에서
        Run(w, SimTime.Hours(30));
        eye.Poll(w);
        var scene = w.Scenes.Scenes.LastOrDefault();
        if (scene != null)
        {
            scene.Trail.Add($"{SimTime.Clock(w.Tick)} {b.Name}: 늦게 왔다 (40% 지나서)");
            var late = eye.Poll(w);
            Check("관찰 카메라 · 늦게 온 관객을 그 사람 자리로", late.Any(x => x.Kind == WatchKind.LateGuest && x.Who.Contains(b.Id) && x.At != null), string.Join(" / ", late.Select(x => x.Text)));
        }
        else Check("관찰 카메라 · 하루 반 동안 함께하는 장면이 열렸다", false, "장면 없음");
        // 물건 따라가기: 만든 사람 → 준 사람 → 임자 → 빌려 간 사람, 지금 든 사람 자리
        var item = w.Belongings.All.First(x => x.Owner == b.Id);
        item.From = a.Id; item.BorrowedBy = c.Id; item.Holder = c.Id;
        var hands = WatchScenes.Hands(item);
        var trail = WatchScenes.Trail(w, item);
        Check("물건 따라가기 · 거친 손 (준 사람 → 임자 → 빌려 간 사람)", hands.IndexOf(a.Id) >= 0 && hands.IndexOf(a.Id) < hands.IndexOf(b.Id) && hands.IndexOf(b.Id) < hands.IndexOf(c.Id), string.Join("→", hands));
        Check("물건 따라가기 · 지금 든 사람의 자리 · 내력 끝은 '지금'", WatchScenes.PositionOf(w, item) == c.Position && trail[^1].Text.StartsWith("지금"), trail[^1].Text);
        Check("물건 따라가기 · 선물 받은 물건은 따라가 볼 만한 물건", WatchScenes.Notable(w).Contains(item));
        // 항해 결산: 큰 사고 · 사람 · 좋은 순간 · 물건
        Scenarios.Apply(w, "combo", out _);
        Run(w, SimTime.Hours(4));
        var v = VoyageReview.Build(w);
        Check("항해 결산 · 큰 사고 · 좋은 순간 · 물건 내력이 한 장에", v.Big.Count > 0 && v.Peace.Count > 0 && v.Items.Count > 0 && v.Items.Any(i => i.Hands.Count >= 3),
            $"사고 {v.Big.Count} · 순간 {v.Peace.Count} · 물건 {v.Items.Count} · 사람 {v.People.Count}");
        Check("항해 결산 · 사람 (떠난 사람 · 앞에 선 사람 · 좋은 순간의 사람)", v.People.Count > 0, string.Join(" / ", v.People.Select(p => p.Name + ":" + p.Why)));
    }
}
