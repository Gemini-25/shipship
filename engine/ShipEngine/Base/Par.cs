namespace ShipEngine;

/// <summary>
/// 결정론 병렬. 일을 '덩어리'로 나눠 여러 코어에서 돌린다.
/// 덩어리 크기는 코어 수와 상관없이 고정이고, 덩어리끼리 쓰는 곳이 겹치지 않으며, 합은 덩어리 순서대로 더한다
/// → 1코어 · 8코어의 결과가 바이트까지 같다 (법칙 시험 '결정론 병렬').
/// </summary>
public static class Par
{
    /// <summary>쓸 코어 수 (시험이 1로 바꿔 견준다).</summary>
    public static int Threads { get; set; } = Math.Max(1, Environment.ProcessorCount);

    /// <summary>덩어리 하나에 든 줄 수 — 고정.</summary>
    public const int RowsPerChunk = 8;

    /// <summary>줄 [0, rows)를 덩어리로 나눠 body(시작 줄, 끝 줄)을 부른다. 덩어리끼리 서로 다른 곳에만 써야 한다.</summary>
    public static void Rows(int rows, Action<int, int> body)
    {
        int chunks = (rows + RowsPerChunk - 1) / RowsPerChunk;
        if (Threads <= 1 || chunks <= 1)
        {
            for (int c = 0; c < chunks; c++) body(c * RowsPerChunk, Math.Min(rows, (c + 1) * RowsPerChunk));
            return;
        }
        Parallel.For(0, chunks, new ParallelOptions { MaxDegreeOfParallelism = Threads },
            c => body(c * RowsPerChunk, Math.Min(rows, (c + 1) * RowsPerChunk)));
    }

    /// <summary>항목 [0, n)을 고정 크기 덩어리로 (사람 · 구역 등 줄이 아닌 것).</summary>
    public static void Items(int n, int perChunk, Action<int, int> body)
    {
        int chunks = (n + perChunk - 1) / perChunk;
        if (Threads <= 1 || chunks <= 1)
        {
            for (int c = 0; c < chunks; c++) body(c * perChunk, Math.Min(n, (c + 1) * perChunk));
            return;
        }
        Parallel.For(0, chunks, new ParallelOptions { MaxDegreeOfParallelism = Threads },
            c => body(c * perChunk, Math.Min(n, (c + 1) * perChunk)));
    }

    /// <summary>줄 덩어리마다 부분합을 내고 덩어리 순서대로 더한다.</summary>
    public static double Sum(int rows, Func<int, int, double> part)
    {
        int chunks = (rows + RowsPerChunk - 1) / RowsPerChunk;
        var parts = new double[chunks];
        Rows(rows, (a, b) => parts[a / RowsPerChunk] = part(a, b));
        double s = 0;
        for (int i = 0; i < chunks; i++) s += parts[i];
        return s;
    }
}
