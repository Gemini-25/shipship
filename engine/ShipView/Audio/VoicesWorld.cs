namespace ShipView.Audio;

// 세상의 소리 몇 가지 (소리 단계 S6): 열 수축 · 팽창의 딱 · 주컴퓨터 방송 알림음 · 외판 너머 바람과 모래 폭풍.

/// <summary>딱 (열 수축 · 팽창 — 이음매가 미끄러진다): 아주 짧은 마찰 딸깍 + 판의 높은 갈래 둘이 잠깐 운다 (식는 차 · 밤의 집에서 나는 소리).</summary>
public sealed class Ping : Voice
{
    private Mode _a, _b;
    private Noise _n;
    private OnePole _hp;
    private int _i;
    private readonly int _len;
    public Ping(uint seed, float hard = 1)
    {
        _n = new Noise(seed);
        float f = (1500 + 1400 * (_n.White() + 1) * 0.5f) * hard;
        _a = new Mode(f, 0.09f, 0.5f);
        _b = new Mode(f * 2.76f, 0.04f, 0.3f);
        _len = (int)(0.3f * Dsp.Rate);
        Send = 0.35f;
    }
    public override float Next()
    {
        if (_i >= _len) { Done = true; return 0; }
        float t = _i / (float)Dsp.Rate;
        float click = _hp.High(_n.White(), 2500) * MathF.Exp(-t * 1800);
        _i++;
        return (_a.Next() + _b.Next() + 0.35f * click) * 2.6f;   // 크기 기준 (가장 센 0.1초 RMS 0.25)
    }
}

/// <summary>띵동 (주컴퓨터 방송 알림음): 종 같은 두 음 — 미(659Hz) 다음 도(523Hz) · 음마다 1 · 2.76 · 5.4배 갈래 (작은 종) · 0.45초 사이.</summary>
public sealed class Chime : Voice
{
    private readonly Mode[] _m = new Mode[6];
    private int _i;
    private readonly int _second, _len;
    public Chime()
    {
        _second = (int)(0.45f * Dsp.Rate);
        _len = (int)(2.2f * Dsp.Rate);
        Set(0, 659.3f);
        Send = 0.3f;
    }
    private void Set(int k, float hz)
    {
        _m[k] = new Mode(hz, 1.4f, 0.5f);
        _m[k + 1] = new Mode(hz * 2.76f, 0.5f, 0.12f);
        _m[k + 2] = new Mode(hz * 5.4f, 0.2f, 0.05f);
    }
    public override float Next()
    {
        if (_i >= _len) { Done = true; return 0; }
        if (_i == _second) Set(3, 523.3f);
        _i++;
        float x = 0;
        for (int k = 0; k < 6; k++) x += _m[k].Next();
        return x * 0.87f;   // 크기 기준
    }
}

/// <summary>
/// 외판 너머의 바람 (이어 도는 소리): 갈색 잡음이 바람 세기만큼 너울지고 (몇 초마다 돌풍 — 0.05 ~ 0.3Hz) · 모서리를 스치는 낮은 휘파람(250 ~ 380Hz 좁은 띠) ·
/// 모래가 날리면 알갱이가 외판을 때리는 투둑 소리 (초당 수십 번 · 600Hz 언저리 짧은 울림). 외판이 높은 음을 먹어 먹먹하다 (섞개의 거름).
/// </summary>
public sealed class Wind : Voice
{
    private Noise _n;
    private float _gust = 0.5f, _gustT = 0.5f, _grit, _speed = 1, _hit;
    private int _i, _nextGust;
    private OnePole _lp, _dc, _hitLp;
    private Biquad _whistle = Biquad.Band(300, 6);
    private Biquad _sand = Biquad.Band(650, 3);
    public Wind(uint seed) { _n = new Noise(seed); Send = 0.2f; }
    /// <summary>모래 (0~1) · 바람 빠르기 몫 (0~1 — 돌풍의 깊이 · 휘파람 높이).</summary>
    public void Set(float grit, float speed) { _grit = Math.Clamp(grit, 0, 1); _speed = Math.Clamp(speed, 0, 1); }
    public override float Next()
    {
        if (_i++ >= _nextGust)
        {
            _gustT = 0.35f + 0.65f * (_n.White() + 1) * 0.5f;
            _nextGust = _i + (int)((3 + 6 * (_n.White() + 1) * 0.5f) * Dsp.Rate);
        }
        _gust += (_gustT - _gust) * (0.6f / Dsp.Rate);
        float body = _dc.High(_lp.Low(_n.Brown(), 400), 25) * (0.55f + 0.45f * _gust);
        float whistle = _whistle.Run(_n.White()) * 0.12f * _speed * _gust;
        float sand = 0;
        if (_grit > 0)
        {
            if (_n.Chance(_grit * 40f * _gust / Dsp.Rate * 1f)) _hit = 0.6f + 0.4f * (_n.White() + 1) * 0.5f;
            _hit *= MathF.Pow(0.001f, 1f / (0.03f * Dsp.Rate));
            sand = _sand.Run(_n.White() * _hit) * 0.9f + _hitLp.Low(_n.White(), 2000) * 0.08f * _grit * _gust;
        }
        return (body * 3.2f + whistle + sand) * 0.9f * (1 - 0.3f * _grit);   // 크기 기준 (모래가 날려도 같은 크기 — 세기는 섞개의 크기가 정한다)
    }
}
