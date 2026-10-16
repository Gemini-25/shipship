namespace ShipEngine;

/// <summary>
/// 혼합물: 물질별 몰 수 + 온도. 탱크 · 몸 · 저장고 · 장치 안이 모두 혼합물이다 (칸의 기체는 장 물리가 배열로 따로 든다).
/// 반응은 혼합물 위에서 일어나고, 원소 · 에너지는 늘 보존된다 (법칙 시험).
/// </summary>
public sealed class Mix : IState
{
    public readonly double[] N;
    /// <summary>온도 (K).</summary>
    public double T = 293.15;

    public Mix(Tables t) => N = new double[t.Substances.Count];
    private Mix(double[] n, double temp) { N = n; T = temp; }

    public Mix Copy() => new((double[])N.Clone(), T);

    public double this[Substance s] { get => N[s.Index]; set => N[s.Index] = value; }

    public double Total => N.Sum();

    /// <summary>열용량 (J/K).</summary>
    public double HeatCapacity(Tables t)
    {
        double c = 0;
        for (int i = 0; i < N.Length; i++) c += N[i] * t.Substances[i].C;
        return c;
    }

    /// <summary>에너지 장부 값 (J): 물질마다 생성 엔탈피 + 모든 상의 현열 c(T-298.15). 혼합물 안에서는 응축상의 현열도 센다.</summary>
    public double Energy(Tables t)
    {
        double e = 0;
        for (int i = 0; i < N.Length; i++)
            if (N[i] != 0) e += N[i] * (t.Substances[i].Hf * 1000.0 + t.Substances[i].C * (T - 298.15));
        return e;
    }

    /// <summary>열을 더한다 (J) — 온도가 오른다.</summary>
    public void AddHeat(Tables t, double joules)
    {
        double c = HeatCapacity(t);
        if (c > 1e-9) T += joules / c;
    }

    /// <summary>다른 혼합물을 섞는다 (에너지 보존 — 섞인 온도는 열용량 가중 평균).</summary>
    public void Add(Tables t, Mix other)
    {
        double c1 = HeatCapacity(t), c2 = other.HeatCapacity(t);
        for (int i = 0; i < N.Length; i++) N[i] += other.N[i];
        if (c1 + c2 > 1e-12) T = (c1 * T + c2 * other.T) / (c1 + c2);
    }

    /// <summary>비율 f (0~1)만큼 떼어 낸다.</summary>
    public Mix Take(double f)
    {
        f = Math.Clamp(f, 0, 1);
        var m = new Mix(new double[N.Length], T);
        for (int i = 0; i < N.Length; i++) { m.N[i] = N[i] * f; N[i] -= m.N[i]; }
        return m;
    }

    /// <summary>
    /// 반응을 최대 extent 몰만큼 일으킨다 (반응물이 모자라면 그만큼만). 실제로 일어난 몰 수를 돌려준다.
    /// 에너지 장부: 반응 전후 Σ n(hf + c(T−298.15))가 같도록 온도를 맞춘다.
    /// </summary>
    public double React(Tables t, Reaction r, double extent)
    {
        if (extent <= 0) return 0;
        foreach (var x in r.In) extent = Math.Min(extent, N[x.S] / x.Nu);
        if (extent <= 1e-15) return 0;
        double e0 = Energy(t);
        foreach (var x in r.In) N[x.S] = Math.Max(0, N[x.S] - x.Nu * extent);
        foreach (var x in r.Out) N[x.S] += x.Nu * extent;
        // 반응 뒤 같은 에너지가 되는 온도: e0 = Σ n hf + C (T' − 298.15)
        double hf = 0, c = 0;
        for (int i = 0; i < N.Length; i++) { hf += N[i] * t.Substances[i].Hf * 1000.0; c += N[i] * t.Substances[i].C; }
        if (c > 1e-12) T = 298.15 + (e0 - hf) / c;
        return extent;
    }

    public void Save(StateWriter w) { w.Arr(N); w.F64(T); }
    public void Load(StateReader r) { r.ArrInto(N); T = r.F64(); }
}
