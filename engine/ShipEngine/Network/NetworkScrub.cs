namespace ShipEngine;

/// <summary>
/// ⑥ 미량 기체 정화 세 단 (docs/ENGINE.md ⑥ · 검토 ④-1) — 정화기 · 공기 재생기의 거르개를 ISS 미량 오염 제거 장치처럼:
/// ① 활성탄(산 처리 숯)이 암모니아 · 시안화수소 · 음식 향을 붙잡고 · 오존은 숯 겉에서 산소로 풀린다 ·
/// ② 촉매 산화가 일산화탄소 · 수소 · 메탄을 태운다 (산소를 쓰고 · 반응열은 그 칸 공기로 — 반응식 표 tccs_*) ·
/// ③ 수산화리튬 흡착제가 산성 기체(염화수소 · 플루오린화수소 · PF₅ · POF₃)를 붙잡는다.
/// 숯 · 흡착제는 찰수록 덜 거르고 · 찬 몫만큼 거르개 부품이 닳은 것으로 친다 — 갈면(수리) 붙잡은 기체가 헌 거르개에 실려 나간다 (원소 장부 BedSpent).
/// </summary>
public sealed partial class Network
{
    public const double CarbonCap = 30, SorbentCap = 20;   // 숯 · 흡착제가 담는 몫 (몰)
    public double[] Bed = Array.Empty<double>();          // [장치 × 기체] 거르개 안에 붙잡은 기체 (몰)
    public double[] BedSpent = Array.Empty<double>();     // [기체] 갈아 낸 거르개에 실려 나간 몫 (몰)
    private int[]? _carbon, _acid;
    private Reaction[]? _burnRx;                          // 촉매 산화 (일산화탄소 · 수소 · 메탄)
    private Reaction? _o3Rx;

    private void ScrubInit()
    {
        _carbon ??= new[] { T.gNH3, T.gHCN, T.gAroma, T.gStench };   // 악취 (낙산)도 숯이 붙잡는다 (5단 C19)
        _acid ??= new[] { T.gHCl, T.gHF, T.gPF5, T.gPOF3 };
        _burnRx ??= new[] { T.R("tccs_co"), T.R("tccs_h2"), T.R("tccs_ch4") };
        _o3Rx ??= T.R("tccs_o3");
        if (Bed.Length != Devices.Count * F.G) Bed = new double[Devices.Count * F.G];
        if (BedSpent.Length != F.G) BedSpent = new double[F.G];
    }

    private double Held(int d, int[] gases) { ScrubInit(); double s = 0; foreach (int g in gases) s += Bed[d * F.G + g]; return s; }
    /// <summary>숯이 찬 몫 (0~1).</summary>
    public double CarbonFill(int d) => Math.Min(1, Held(d, _carbon!) / CarbonCap);
    /// <summary>흡착제가 찬 몫 (0~1).</summary>
    public double SorbentFill(int d) => Math.Min(1, Held(d, _acid!) / SorbentCap);

    /// <summary>거르개 안 + 갈아 낸 몫을 물질별로 (원소 장부 — 법칙 '정화').</summary>
    public double[] ScrubHeld()
    {
        ScrubInit();
        var m = new double[T.Substances.Count];
        for (int d = 0; d < Devices.Count; d++) for (int g = 0; g < F.G; g++) m[T.Gases[g].Index] += Bed[d * F.G + g];
        for (int g = 0; g < F.G; g++) m[T.Gases[g].Index] += BedSpent[g];
        return m;
    }

    /// <summary>시험: 숯 · 흡착제를 frac만큼 채운다 (암모니아 · 염화수소로 — 원소 장부 밖).</summary>
    public void FillBed(int d, double frac)
    {
        ScrubInit();
        Bed[d * F.G + T.gNH3] += Math.Max(0, frac * CarbonCap - Held(d, _carbon!));
        Bed[d * F.G + T.gHCl] += Math.Max(0, frac * SorbentCap - Held(d, _acid!));
    }

    /// <summary>거르개를 갈았다 — 붙잡은 기체는 헌 거르개에 실려 나간다.</summary>
    private void EmptyBed(int d)
    {
        ScrubInit();
        for (int g = 0; g < F.G; g++) { BedSpent[g] += Bed[d * F.G + g]; Bed[d * F.G + g] = 0; }
    }

    /// <summary>칸들의 독한 기체를 frac만큼 세 단으로 거른다 (장치 d의 거르개). 걸러 낸 몰.</summary>
    private double ScrubTrace(Sim sim, int d, int[] cells, double frac)
    {
        ScrubInit();
        int b = d * F.G;
        double took = 0, fc = frac * (1 - CarbonFill(d)), fs = frac * (1 - SorbentFill(d));
        foreach (int c in cells)
        {
            if (fc > 0) foreach (int g in _carbon!) { double n = F.Gas(g, c); if (n > 1e-9) { double x = F.RemoveGas(c, g, n * fc); Bed[b + g] += x; took += x; } }
            if (fs > 0) foreach (int g in _acid!) { double n = F.Gas(g, c); if (n > 1e-9) { double x = F.RemoveGas(c, g, n * fs); Bed[b + g] += x; took += x; } }
            foreach (var r in _burnRx!) took += Catalyze(sim, c, r, frac);
            took += Catalyze(sim, c, _o3Rx!, frac);
        }
        // 찬 몫만큼 거르개가 닳았다 (예방 정비가 갈 때를 안다)
        double fill = Math.Max(CarbonFill(d), SorbentFill(d)), life = Devices[d].Life(Part.Filter);
        int k = d * 4 + (int)Part.Filter;
        if (life > 0 && fill * life > Wear[k]) Wear[k] = (float)(fill * life);
        return took;
    }

    /// <summary>촉매 반응 하나: 첫 반응물(태울 기체 · 오존)의 frac만큼 — 산소가 모자라면 있는 만큼. 반응열(생성 엔탈피)은 그 칸 공기로.</summary>
    private double Catalyze(Sim sim, int c, Reaction r, double frac)
    {
        double n = F.Gas(T.Substances[r.In[0].S].Gas, c);
        if (n <= 1e-9) return 0;
        double ext = n * frac / r.In[0].Nu;
        foreach (var x in r.In) ext = Math.Min(ext, F.Gas(T.Substances[x.S].Gas, c) / x.Nu);
        if (ext <= 1e-12) return 0;
        double tg = F.Temp(c), q = r.Heat(T, tg) * ext;
        foreach (var x in r.In)
        {
            int g = T.Substances[x.S].Gas;
            F.RemoveGas(c, g, x.Nu * ext);
            if (g == T.gO2) sim.Book.Add(Res.O2, Flow.Burned, -x.Nu * ext);
        }
        foreach (var x in r.Out) F.AddGas(c, T.Substances[x.S].Gas, x.Nu * ext, tg);
        F.AddHeat(c, q);
        return r.In[0].Nu * ext;
    }

    private void SaveScrub(StateWriter w) { w.Tag("정화"); w.Arr(Bed); w.Arr(BedSpent); }
    private void LoadScrub(StateReader r) { r.Tag("정화"); Bed = r.Arr<double>(); BedSpent = r.Arr<double>(); }
}
