using System.Diagnostics;
using ShipEngine;

namespace Lab;

/// <summary>
/// 실험실: --law 법칙 시험 · --rule 규칙 시험 · --budget 시간 예산 · --scene 이름 [폴더] 관찰 장면 · --bench 성능 · --ships 배 도면 목록.
/// 실패가 하나라도 있으면 1로 끝난다 (CI가 빨강).
/// </summary>
public static partial class Program
{
    private static int _fail, _pass;
    private static string? _only;

    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        _only = Environment.GetEnvironmentVariable("ONLY");
        if (args.Contains("--threads")) Par.Threads = int.Parse(args[Array.IndexOf(args, "--threads") + 1]);
        if (args.Contains("--shard"))
        {
            var p = args[Array.IndexOf(args, "--shard") + 1].Split('/'); _shard = int.Parse(p[0]); _shards = int.Parse(p[1]);
            // 갈래끼리 이미 코어를 나눠 쓴다 — 갈래 안 장 계산까지 코어를 다 쓰면 서로 빼앗는다 (결정론이라 결과는 같다)
            if (!args.Contains("--threads")) Par.Threads = Math.Max(1, Environment.ProcessorCount / _shards);
        }
        var sw = Stopwatch.StartNew();
        try
        {
            if (args.Contains("--law")) Laws();
            else if (args.Contains("--rule")) Rules();
            else if (args.Contains("--bench")) Bench();
            else if (args.Contains("--scene")) Scene(args[Array.IndexOf(args, "--scene") + 1], args.Length > Array.IndexOf(args, "--scene") + 2 ? args[Array.IndexOf(args, "--scene") + 2] : "build/scenes");
            else if (args.Contains("--ships")) Ships();
            else if (args.Contains("--probe-det")) ProbeDeterminism(args.Contains("load"));
            else if (args.Contains("--probe-crew")) ProbeCrew(args);
            else if (args.Contains("--fun")) Fun(args);
            else if (args.Contains("--probe-story")) ProbeStory(args);
            else if (args.Contains("--probe-robot")) ProbeRobot(args);
            else if (args.Contains("--probe-heat")) ProbeHeat(args);
            else if (args.Contains("--probe-danger")) ProbeDanger(args);
            else if (args.Contains("--probe-person")) ProbePerson(args);
            else if (args.Contains("--probe-log")) ProbeLog(args);
            else if (args.Contains("--probe-holes")) ProbeHoles(args);
            else if (args.Contains("--probe-cell")) ProbeCell(args);
            else if (args.Contains("--probe-flap")) ProbeFlap(args);
            else if (args.Contains("--probe-door")) ProbeDoor(args);
            else if (args.Contains("--probe-fire")) ProbeFire(args);
            else if (args.Contains("--shot")) Shot(args);
            else if (args.Contains("--film")) Film(args);
            else if (args.Contains("--live")) Live(args);
            else if (args.Contains("--listen")) Listen(args);
            else if (args.Contains("--ledger")) LedgerRun(args);
            else if (args.Contains("--shipgen")) ShipGen(args);
            else if (args.Contains("--profile")) ProfileShip(args);
            else if (args.Contains("--fingerprint")) Fingerprints();
            else if (args.Contains("--budget")) Budget();
            else if (args.Contains("--music")) MusicDemo(args);
            else { Console.WriteLine("Lab --law | --rule | --bench | --scene 이름 [폴더] | --ships | --fun [배,…] [날] [시드 수] [성격] [가혹함] | --live 배 시드 초 | --listen 배 시드 초 [건너뛸 시간]"); return 2; }
        }
        catch (Exception e)
        {
            Console.WriteLine($"✘ 멈춤: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            return 1;
        }
        EndGroup();
        if (Environment.GetEnvironmentVariable("RULE_TIMES") == "1") foreach (var (k, t) in _times) Console.WriteLine($"⏱\t{k}\t{t:0.0}");
        if (_failedGroups.Count > 0) Console.WriteLine($"\n✘ 실패한 묶음: {string.Join(",", _failedGroups)}");
        if (_pass + _fail > 0)
            Console.WriteLine($"\n{(_fail == 0 ? "✔" : "✘")} 통과 {_pass} · 실패 {_fail} · {sw.Elapsed.TotalSeconds:0.0}초");
        return _fail == 0 ? 0 : 1;
    }

    internal static bool Do(string key)
    {
        if (_only != null && !_only.Split(',').Any(k => key.Contains(k.Trim()))) { EndGroup(); return false; }
        if (_shards > 1 && ShardOf(key) != _shard) { EndGroup(); return false; }
        EndGroup();
        _group = key; _groupStart = Stopwatch.GetTimestamp();
        return true;
    }

    // ── 묶음 시간 · 나눠 돌리기 (--shard i/n: 묶음을 지난 시간표대로 n 갈래에 고르게 — tools/rules.sh가 함께 띄운다) ──
    private static int _shard, _shards = 1;
    private static string? _group;
    private static long _groupStart;
    private static readonly List<(string key, double sec)> _times = new();
    private static Dictionary<string, int>? _assign;

    private static void EndGroup()
    {
        if (_group == null) return;
        _times.Add((_group, (Stopwatch.GetTimestamp() - _groupStart) / (double)Stopwatch.Frequency));
        _group = null;
    }

    /// <summary>묶음 → 갈래: 시간표(tools/rule-times.txt — "묶음 초")의 긴 것부터 가장 덜 찬 갈래에 · 표에 없으면 이름의 해시.</summary>
    private static int ShardOf(string key)
    {
        if (_assign == null)
        {
            _assign = new();
            var load = new double[_shards];
            string path = Environment.GetEnvironmentVariable("RULE_TIMES_FILE") ?? "tools/rule-times.txt";
            var rows = File.Exists(path)
                ? File.ReadAllLines(path).Select(l => l.Split('\t')).Where(p => p.Length == 2 && double.TryParse(p[1], out _)).Select(p => (k: p[0], t: double.Parse(p[1]))).OrderByDescending(r => r.t).ToList()
                : new();
            foreach (var (k, t) in rows) { int best = Array.IndexOf(load, load.Min()); _assign[k] = best; load[best] += t; }
        }
        return _assign.TryGetValue(key, out int sh) ? sh : (int)(Rng.Key(key) % (ulong)_shards);
    }

    private static long _lap;
    private static readonly List<string> _failedGroups = new();
    /// <summary>시험 하나가 이보다 오래 걸리면 알린다 — 하루를 기다리지 말고 상황을 차려 놓고 짧게 (평시 하루 묶음은 빼고).</summary>
    public const double SlowTestSec = 10;

    internal static void Check(string name, bool ok, string detail = "")
    {
        long now = Stopwatch.GetTimestamp();
        double lap = (now - Math.Max(_lap, _groupStart)) / (double)Stopwatch.Frequency;
        if (Environment.GetEnvironmentVariable("RULE_TIMES") == "1") Console.WriteLine($"⏲\t{_group}\t{lap:0.0}\t{name}");
        _lap = now;
        if (ok) _pass++; else { _fail++; if (_group != null && !_failedGroups.Contains(_group)) _failedGroups.Add(_group); }
        Console.WriteLine($"  {(ok ? "✔" : "✘")} {name}{(detail.Length > 0 ? " — " + detail : "")}");
        if (_group != null && lap > SlowTestSec && _group != "평시 하루") Console.WriteLine($"  ⚠ 느린 시험 {lap:0}초 ({_group}) — 상황을 차려 놓고 짧게 · 작은 배로 (docs/ENGINE.md 시험 시간)");
    }

    internal static double Rel(double a, double b) => Math.Abs(a - b) / Math.Max(1e-30, Math.Max(Math.Abs(a), Math.Abs(b)));
}
