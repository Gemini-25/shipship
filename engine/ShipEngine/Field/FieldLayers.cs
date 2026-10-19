namespace ShipEngine;

/// <summary>
/// ③ 국소 현상 (가) 칸 안 위아래 두 층 (깊이 바퀴 3 · docs/ENGINE.md ③ '국소 현상'): 칸(높이 2.5m)을 위 · 아래 반으로 본다.
/// 칸마다 층의 세기 s (−1 ~ 1) 하나 — 부력 B = g·H·(뜨거움 − 무거움)이 섞임 u²를 이기면 층이 선다 (s* = tanh(B/u²) · 서는 데 6초 · 흩어지는 데 30초).
/// 뜨거운 층(s &gt; 0)에는 불의 산물이 위로 · 무거운 층(s &lt; 0)에는 공기보다 1.2배 넘게 무거운 기체가 바닥으로 — 나머지(공기)는 층마다 남은 자리를 채운다.
/// 세상의 몰 수는 칸 그대로다 (층은 그 칸 안에서 어디에 있나만 — 머리 높이의 숨 · 온도를 읽는 데 쓴다).
/// </summary>
public sealed partial class Field
{
    public const double LayerH = 2.5, LayerU0 = 0.4, LayerHotDeadK = 5, LayerDenseDead = 0.02, LayerHeavy = 1.2, AirMolar = 0.02897;
    /// <summary>층이 서는 데 (부력으로 2.5m를 오르내리는 몇 초) · 흩어지는 데 (섞임이 이기는 반 분) 걸리는 시간상수.</summary>
    public const double LayerTauForm = 6, LayerTauMix = 30;
    /// <summary>칸마다 층의 세기 (−1: 무거운 기체가 바닥에 · 0: 섞임 · 1: 뜨거운 산물이 천장에) — 찍는다.</summary>
    public float[] Strat = Array.Empty<float>();
    private bool[] _hotMember = Array.Empty<bool>(), _heavyMember = Array.Empty<bool>();

    private void InitLayers()
    {
        Strat = new float[Cells]; Plug = new float[Cells];
        _hotMember = new bool[G]; _heavyMember = new bool[G];
        foreach (int g in new[] { T.gSoot, T.gCO, T.gCO2, T.gHCN, T.gHCl, T.gHF, T.gPF5, T.gPOF3 }) if (g >= 0 && g < G) _hotMember[g] = true;
        for (int g = 0; g < G; g++) _heavyMember[g] = _molar[g] > LayerHeavy * AirMolar;
    }

    /// <summary>뜨거운 층에 실리는 기체인가 (불의 산물) · 무거운 층에 실리는 기체인가 (공기보다 1.2배 넘게 무겁다).</summary>
    /// <summary>
    /// ③ 국소 현상 (나 · 다): 구멍을 막은 넓이 (m² — 칸마다 · 찍는다). 구멍에 빨려 붙은 몸 · 물건 대신 마음이 요청한다.
    /// 구멍 틈의 넓이 = 구멍 − 막은 넓이 (빨려 붙은 몸 · 상자 둘레의 틈은 요청하는 쪽이 셈에 넣는다).
    /// </summary>
    public float[] Plug = Array.Empty<float>();

    public void SetPlug(int cell, float sealedArea)
    {
        if (Plug.Length != Cells) Plug = new float[Cells];
        Plug[cell] = Math.Max(0f, sealedArea);
    }

    /// <summary>틱마다 흐르기 전에: 구멍 틈의 넓이를 막은 만큼 줄인다 (막음이 풀리면 처음 넓이로).</summary>
    private void PlugLinks()
    {
        if (Plug.Length != Cells) return;
        foreach (var k in Links)
            if (k.Kind is LinkKind.Breach or LinkKind.Crack && k.Via >= 0)
                k.Area = Plug[k.Via] > 0 ? Math.Max(0.0, k.Area0 - Plug[k.Via]) : k.Area0;
    }

    public bool HotLayered(int g) => _hotMember[g];
    public bool HeavyLayered(int g) => _heavyMember[g];

    /// <summary>층이 서려는 세기 s* — 부력(뜨거움 − 무거움) ÷ 섞임 (바탕 0.4m/s · 바람 · 공조 바람). 무중력이면 0.</summary>
    public double LayerTarget(int i)
    {
        if (Severed[i] != 0 || !Passable(i) || Grav[i] < LowG) return 0;
        double n = Read(i, out double gc, out double kPa);
        if (n < 1e-6 || kPa < 5 || gc <= 1e-9) return 0;
        double temp = U[i] / gc, mass = 0;
        for (int g = 0; g < G; g++) mass += N[i * G + g] * _molar[g];
        double hot = Math.Max(0, temp - Ts[i] - LayerHotDeadK) / temp;
        double heavy = Math.Max(0, mass / n / AirMolar * Ts[i] / temp - 1 - LayerDenseDead);
        double b = 9.8 * Grav[i] * LayerH * (hot - heavy);
        if (b == 0) return 0;
        double u2 = LayerU0 * LayerU0 + WindX[i] * WindX[i] + WindY[i] * WindY[i] + Airflow[i] * Airflow[i];
        return Math.Tanh(b / u2);
    }

    /// <summary>5틱마다: 층의 세기가 목표를 따라간다 (서는 데 6초 · 흩어지는 데 30초).</summary>
    private void Layers(double dt)
    {
        if (Strat.Length != Cells) InitLayers();
        double kForm = 1 - Math.Exp(-dt / LayerTauForm), kMix = 1 - Math.Exp(-dt / LayerTauMix);
        for (int i = 0; i < Cells; i++)
        {
            double target = LayerTarget(i);
            if (target == 0 && Strat[i] == 0) continue;
            bool forming = Math.Abs(target) > Math.Abs(Strat[i]) && (target >= 0) == (Strat[i] >= 0);
            double s = Strat[i] + (target - Strat[i]) * (forming ? kForm : kMix);
            Strat[i] = Math.Abs(s) < 1e-4 ? 0 : (float)s;
        }
    }

    /// <summary>그 층(아래: 기어가거나 누운 이의 머리 · 위: 선 이의 머리)에 있는 기체 g의 몰 (층은 칸의 반 — 몰도 반씩).</summary>
    private double LayerMoles(int g, int i, bool low, double n)
    {
        double s = Strat.Length == Cells ? Strat[i] : 0;
        if (Math.Abs(s) < 0.01) return N[i * G + g] * 0.5;
        bool hot = s > 0;
        var member = hot ? _hotMember : _heavyMember;
        double mem = 0;
        for (int q = 0; q < G; q++) if (member[q]) mem += N[i * G + q];
        // 실린 기체의 그 층 몫: 뜨거우면 위에 ½ + ½s · 무거우면 아래에 ½ + ½|s| (층의 몰은 반을 넘지 못한다)
        double share = 0.5 + 0.5 * Math.Abs(s);
        if (mem > 0) share = Math.Min(share, 0.5 * n / mem * 0.999);
        bool rich = hot ? !low : low;   // 실린 기체가 짙은 쪽
        double here = rich ? share : 1 - share;
        if (member[g]) return N[i * G + g] * here;
        double rest = n - mem;
        return rest > 1e-12 ? N[i * G + g] * Math.Max(0, 0.5 * n - mem * here) / rest : 0;
    }

    /// <summary>머리 높이의 분압 (kPa) — low: 기어가거나 누운 이 (아래 층) · 아니면 선 이 (위 층).</summary>
    public double HeadPartial(int g, int i, bool low)
    {
        double n = Read(i, out _, out double kPa);
        return n > 1e-12 ? kPa * LayerMoles(g, i, low, n) / (0.5 * n) : 0;
    }

    /// <summary>머리 높이의 몰 분율 → ppm.</summary>
    public double HeadPpm(int g, int i, bool low)
    {
        double n = Moles(i);
        return n > 1e-9 ? LayerMoles(g, i, low, n) / (0.5 * n) * 1e6 : 0;
    }

    /// <summary>머리 높이의 기체 온도 (K): 뜨거운 층이 서면 위 = T + E·s · 아래 = T − E·s (E = 바닥보다 뜨거운 몫 · 평균은 그대로).</summary>
    public double HeadTemp(int i, bool low)
    {
        double t = Temp(i);
        double s = Strat.Length == Cells ? Strat[i] : 0;
        if (s <= 0.01) return t;
        double e = Math.Max(0, t - Ts[i] - LayerHotDeadK);
        return low ? t - e * s : t + e * s;
    }
}
