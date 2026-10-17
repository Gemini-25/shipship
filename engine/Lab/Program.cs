using System.Diagnostics;
using ShipEngine;

namespace Lab;

/// <summary>
/// 실험실: --law 법칙 시험 · --rule 규칙 시험 · --scene 이름 [폴더] 관찰 장면 · --bench 성능 · --ships 배 도면 목록.
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
            else { Console.WriteLine("Lab --law | --rule | --bench | --scene 이름 [폴더] | --ships | --fun [배,…] [날] [시드 수] [성격] [가혹함]"); return 2; }
        }
        catch (Exception e)
        {
            Console.WriteLine($"✘ 멈춤: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            return 1;
        }
        if (_pass + _fail > 0)
            Console.WriteLine($"\n{(_fail == 0 ? "✔" : "✘")} 통과 {_pass} · 실패 {_fail} · {sw.Elapsed.TotalSeconds:0.0}초");
        return _fail == 0 ? 0 : 1;
    }

    internal static bool Do(string key) => _only == null || _only.Split(',').Any(k => key.Contains(k.Trim()));

    internal static void Check(string name, bool ok, string detail = "")
    {
        if (ok) _pass++; else _fail++;
        Console.WriteLine($"  {(ok ? "✔" : "✘")} {name}{(detail.Length > 0 ? " — " + detail : "")}");
    }

    internal static double Rel(double a, double b) => Math.Abs(a - b) / Math.Max(1e-30, Math.Max(Math.Abs(a), Math.Abs(b)));
}
