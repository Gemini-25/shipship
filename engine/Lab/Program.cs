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
            else if (args.Contains("--probe-det")) ProbeDeterminism();
            else { Console.WriteLine("Lab --law | --rule | --bench | --scene 이름 [폴더] | --ships"); return 2; }
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
