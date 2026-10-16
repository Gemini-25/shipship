using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 시험 안의 서로 독립인 긴 항해 여러 개를 자식 프로세스로 나눠 동시에 돌린다.
/// 스레드로 나누면 전역 칸(캐시 · 스위치)을 나눠 써 결정론이 깨질 수 있다 — 프로세스는 저마다 깨끗하다.
/// · 부모: RunChildren(태그, n) → 같은 인자로 자기 자신을 n번 띄우고(SHIPSIM_CHILD=태그:번호) 돌려받은 한 줄을 번호 순서대로
/// · 자식: ChildIndex(태그)가 번호를 주면 그 하나만 돌리고 ChildReply(값)로 답한 뒤 끝낸다
/// · 동시에 띄우는 수: SHIPSIM_CHILD_JOBS (기본 CPU 수) · SHIPSIM_CHILD_JOBS=1이면 하나씩 (예전과 같은 차례)
/// </summary>
public static partial class Program
{
    internal static string[] MainArgs = Array.Empty<string>();

    private static int? ChildIndex(string tag)
    {
        var v = Environment.GetEnvironmentVariable("SHIPSIM_CHILD");
        return v != null && v.StartsWith(tag + ":", StringComparison.Ordinal) && int.TryParse(v[(tag.Length + 1)..], out int i) ? i : null;
    }

    private static void ChildReply(string payload) => Console.WriteLine("##CHILD\t" + payload);

    private static string[] RunChildren(string tag, int n)
    {
        int jobs = int.TryParse(Environment.GetEnvironmentVariable("SHIPSIM_CHILD_JOBS"), out var j) && j > 0 ? j : Math.Max(1, Environment.ProcessorCount);
        var results = new string[n];
        using var gate = new SemaphoreSlim(jobs);
        var tasks = Enumerable.Range(0, n).Select(async i =>
        {
            await gate.WaitAsync();
            try { results[i] = await RunChild(tag, i); }
            finally { gate.Release(); }
        }).ToArray();
        Task.WaitAll(tasks);
        return results;
    }

    private static async Task<string> RunChild(string tag, int i)
    {
        string exe = Environment.ProcessPath!;
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(exe) == "dotnet") psi.ArgumentList.Add(typeof(Program).Assembly.Location); // dotnet Headless.dll 로 돌 때
        foreach (var a in MainArgs) psi.ArgumentList.Add(a);
        psi.Environment["SHIPSIM_CHILD"] = $"{tag}:{i}";
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        string output = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        var line = output.Split('\n').LastOrDefault(l => l.StartsWith("##CHILD\t", StringComparison.Ordinal));
        if (line == null)
            throw new InvalidOperationException($"자식 {tag}:{i} 답 없음 (exit {p.ExitCode}): {await err} {output[Math.Max(0, output.Length - 500)..]}");
        return line[8..].TrimEnd('\r');
    }
}
