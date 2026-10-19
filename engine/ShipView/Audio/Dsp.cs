namespace ShipView.Audio;

/// <summary>소리 계산의 바탕 — 44,100Hz (함장 결정 16) · 실수. 거름(한 극 · 두 극) · 공명 · 잡음 · 울림(슈뢰더) · 좌우(같은 세기).</summary>
public static class Dsp
{
    public const int Rate = 44100;
    public const float Tau = MathF.PI * 2;
    /// <summary>표본마다 따라가는 몫 · 확률은 22,050Hz에서 정했다 — 지금 빠르기에서 같은 시간에 같은 만큼 (표본이 두 배면 몫은 반).</summary>
    public const float K = 22050f / Rate;
    public static readonly float SqK = MathF.Sqrt(K);
    /// <summary>22,050Hz에서 표본마다 c만큼 따라가던 것을 지금 빠르기로 (1 − (1 − c)^K).</summary>
    public static float Per(float c) => 1f - MathF.Pow(1f - c, K);
    /// <summary>거름이 다루는 가장 높은 음 (나이퀴스트 아래).</summary>
    public const float Top = Rate * 0.45f;
    public static float Db(float db) => MathF.Pow(10f, db / 20f);
}

/// <summary>잡음 (xorshift — 소리마다 씨앗이 있어 같은 장면은 같은 소리).</summary>
public struct Noise
{
    private uint _s;
    private float _brown, _pink0, _pink1, _pink2;
    public Noise(uint seed) { _s = seed * 2654435761u + 1; _brown = _pink0 = _pink1 = _pink2 = 0; }
    public float White()
    {
        _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
        return (_s & 0xFFFFFF) / (float)0x800000 - 1f;
    }
    public float Brown() { _brown = Math.Clamp(_brown + 0.02f * Dsp.SqK * White(), -1f, 1f); return _brown; }
    public float Pink()
    {
        float w = White();
        _pink0 = 0.99765f * _pink0 + w * 0.0990460f; _pink1 = 0.96300f * _pink1 + w * 0.2965164f; _pink2 = 0.57000f * _pink2 + w * 1.0526913f;
        return (_pink0 + _pink1 + _pink2 + w * 0.1848f) * 0.25f;
    }
    public bool Chance(float p) => (White() + 1f) * 0.5f < p;
}

/// <summary>한 극 낮은 음 거름 (자르는 높이를 매 표본 바꿀 수 있다).</summary>
public struct OnePole
{
    private float _y;
    public float Low(float x, float hz) { float a = 1f - MathF.Exp(-Dsp.Tau * Math.Clamp(hz, 10f, Dsp.Top) / Dsp.Rate); _y += a * (x - _y); return _y; }
    public float High(float x, float hz) => x - Low(x, hz);
}

/// <summary>두 극 거름 (RBJ 쌍이차) — 띠 · 낮은 음 · 높은 음.</summary>
public struct Biquad
{
    private float b0, b1, b2, a1, a2, z1, z2;
    public static Biquad Band(float hz, float q) { var f = new Biquad(); f.SetBand(hz, q); return f; }
    public static Biquad Lowpass(float hz, float q = 0.707f) { var f = new Biquad(); f.SetLow(hz, q); return f; }
    public void SetBand(float hz, float q)
    {
        float w = Dsp.Tau * Math.Clamp(hz, 20f, Dsp.Top) / Dsp.Rate, al = MathF.Sin(w) / (2 * q), a0 = 1 + al;
        b0 = al / a0; b1 = 0; b2 = -al / a0; a1 = -2 * MathF.Cos(w) / a0; a2 = (1 - al) / a0;
    }
    public void SetLow(float hz, float q = 0.707f)
    {
        float w = Dsp.Tau * Math.Clamp(hz, 20f, Dsp.Top) / Dsp.Rate, al = MathF.Sin(w) / (2 * q), c = MathF.Cos(w), a0 = 1 + al;
        b0 = (1 - c) / 2 / a0; b1 = (1 - c) / a0; b2 = b0; a1 = -2 * c / a0; a2 = (1 - al) / a0;
    }
    public float Run(float x)
    {
        float y = b0 * x + z1;
        z1 = b1 * x - a1 * y + z2;
        z2 = b2 * x - a2 * y;
        return y;
    }
}

/// <summary>감쇠하는 사인 하나 (고유 진동 한 갈래) — 두 극 공명기를 회전으로 (흔들림 없이 길게 운다).</summary>
public struct Mode
{
    private float _c, _s, _re, _im, _decay;
    public Mode(float hz, float t60, float amp, float phase = 0)
    {
        float w = Dsp.Tau * hz / Dsp.Rate;
        _c = MathF.Cos(w); _s = MathF.Sin(w);
        _decay = MathF.Pow(0.001f, 1f / Math.Max(1f, t60 * Dsp.Rate));
        _re = amp * MathF.Cos(phase); _im = amp * MathF.Sin(phase);
    }
    public float Next()
    {
        float re = (_re * _c - _im * _s) * _decay, im = (_re * _s + _im * _c) * _decay;
        _re = re; _im = im;
        return im;
    }
    public bool Quiet => _re * _re + _im * _im < 1e-9f;
}

/// <summary>
/// 울림 (슈뢰더: 빗살 넷 + 전대역 둘) — 방이 클수록 · 벽이 단단할수록 길게. RT60(초)에서 빗살의 되먹임을 정한다.
/// </summary>
public sealed class Reverb
{
    private static readonly int[] CombLen = new[] { 1116, 1188, 1277, 1356 }.Select(n => n * Dsp.Rate / 44100).ToArray(); // 프리버브 길이 (44.1kHz 기준)
    private static readonly int[] ApLen = new[] { 556, 441 }.Select(n => n * Dsp.Rate / 44100).ToArray();
    private static readonly float Damp = Dsp.Per(0.35f);
    private readonly float[][] _comb, _ap;
    private readonly int[] _ci = new int[4], _ai = new int[2];
    private readonly float[] _fb = new float[4];
    private readonly float[] _damp = new float[4];
    public float Rt60 { get; private set; } = -1;

    public Reverb()
    {
        _comb = CombLen.Select(n => new float[n]).ToArray();
        _ap = ApLen.Select(n => new float[n]).ToArray();
        SetRt60(0.5f);
    }

    public void SetRt60(float rt60)
    {
        rt60 = Math.Clamp(rt60, 0.05f, 6f);
        if (MathF.Abs(rt60 - Rt60) < 0.01f) return;
        Rt60 = rt60;
        for (int i = 0; i < 4; i++) _fb[i] = MathF.Pow(0.001f, CombLen[i] / (rt60 * Dsp.Rate));
    }

    public float Run(float x)
    {
        float y = 0;
        for (int i = 0; i < 4; i++)
        {
            var b = _comb[i]; int k = _ci[i];
            float o = b[k];
            _damp[i] += Damp * (o - _damp[i]); // 벽이 높은 음을 먹는다
            b[k] = x + _damp[i] * _fb[i];
            _ci[i] = (k + 1) % b.Length;
            y += o;
        }
        y *= 0.25f;
        for (int i = 0; i < 2; i++)
        {
            var b = _ap[i]; int k = _ai[i];
            float o = b[k];
            b[k] = y + o * 0.5f;
            y = o - y * 0.5f;
            _ai[i] = (k + 1) % b.Length;
        }
        return y;
    }
}

/// <summary>좌우: 같은 세기 (−1 왼쪽 · 0 가운데 · 1 오른쪽).</summary>
public static class Pan
{
    public static (float l, float r) Gains(float pan)
    {
        float a = (Math.Clamp(pan, -1f, 1f) + 1f) * MathF.PI / 4f;
        return (MathF.Cos(a), MathF.Sin(a));
    }
}
