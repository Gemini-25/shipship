namespace ShipEngine;

/// <summary>
/// 층 안 단계별 시간 (실험실 --profile 전용 — 꺼 두면 아무것도 재지 않는다).
/// 쓰기: <c>long t = Prof.Start(); … t = Prof.Lap("구조:문", t);</c> — 이름 앞머리가 층 (구조 · 주컴퓨터 …).
/// </summary>
public static class Prof
{
    public static Dictionary<string, double>? D;
    public static long Start() => D != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
    public static long Lap(string name, long t0)
    {
        if (D == null) return 0;
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        lock (D) D[name] = D.GetValueOrDefault(name) + (t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;   // 시험은 배 여럿을 함께 돌린다
        return t1;
    }

    /// <summary>모은 시간을 층마다 큰 순서로 (실험실 PROF=1 — 끝에 찍는다).</summary>
    public static string Report()
    {
        if (D == null || D.Count == 0) return "";
        var sb = new System.Text.StringBuilder("⏱ 단계별 시간 (PROF=1 · 초)\n");
        foreach (var g in D.GroupBy(kv => kv.Key.Split(':')[0]).OrderByDescending(g => g.Sum(kv => kv.Value)))
            sb.Append($"  {g.Key} {g.Sum(kv => kv.Value) / 1000:0.0}: ").AppendLine(string.Join(" · ", g.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"{kv.Key[(kv.Key.IndexOf(':') + 1)..]} {kv.Value / 1000:0.00}")));
        return sb.ToString();
    }
}
