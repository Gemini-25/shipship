namespace ShipView.Audio;

/// <summary>
/// 재질의 소리 성질 (고유 진동): 첫 진동수 f1(판 하나 — 탄성 · 밀도 · 두께에서 · 강철 판 ~180Hz) · 손실 η(클수록 빨리 그친다) ·
/// 단단함(부딪힐 때 높은 진동을 얼마나 깨우나) · 흡음(방 울림에서 벽이 소리를 먹는 몫).
/// f1 ∝ √(E/ρ) — 강철 · 알루미늄은 비슷하지만 알루미늄은 가벼워 더 크게 · 고분자는 낮고 금방 그친다.
/// </summary>
public readonly record struct Acoustic(float F1, float Eta, float Hard, float Absorb)
{
    public static Acoustic Of(string materialId) => materialId switch
    {
        "steel" => new(180, 0.004f, 0.9f, 0.04f),
        "aluminum" => new(185, 0.006f, 0.8f, 0.05f),
        "titanium" => new(210, 0.005f, 0.9f, 0.04f),
        "copper" => new(160, 0.006f, 0.7f, 0.05f),
        "glass" => new(320, 0.008f, 1.0f, 0.04f),
        "ceramic" => new(300, 0.01f, 1.0f, 0.03f),
        "composite" => new(220, 0.03f, 0.6f, 0.1f),
        "polymer" => new(90, 0.08f, 0.3f, 0.15f),
        "foam" => new(60, 0.3f, 0.05f, 0.7f),
        "fabric" => new(70, 0.35f, 0.05f, 0.6f),
        "food" => new(80, 0.3f, 0.1f, 0.4f),
        _ => new(180, 0.01f, 0.7f, 0.08f),
    };
}

/// <summary>소리 하나 (한 번 울리는 것 · 이어 도는 것). 섞개가 거름 · 크기 · 좌우 · 울림을 덧씌운다.
/// 기준 (소리 단계 S2): 크기 1(Gain)로 만들면 가장 센 0.1초의 RMS가 0.25 언저리 — 소리끼리 dB대로 들리게 (`Lab --voices`로 잰다).</summary>
public abstract class Voice
{
    public float Gain = 1, Cutoff = 16000, Pan, Send = 0.25f;
    /// <summary>늦게 닿는 소리: 섞개가 이만큼(표본) 기다렸다 울린다 (공기 343m/s · 뼈대 5,000m/s — 화면 시간으로).</summary>
    public int Delay;
    public bool Done;
    public abstract float Next();
}

/// <summary>
/// 충돌 (쾅): 맞은 판의 고유 진동 8갈래를 감쇠 사인으로 (단순 지지 정사각 판의 진동수 비 (m²+n²)/2) — 갈래마다 T60 = 2.2/(f·η).
/// 단단할수록 높은 갈래를 세게 깨운다 · 닿는 순간의 잡음 · 큰 에너지면 배 뼈대가 낮게 운다.
/// </summary>
public sealed class Hit : Voice
{
    private static readonly float[] Ratio = { 1f, 2.5f, 4f, 5f, 6.5f, 8.5f, 10f, 13f };
    private readonly Mode[] _modes;
    private Noise _n;
    private OnePole _contact;
    private readonly float _contactHz, _boomAmp;
    private int _i;
    private readonly int _len;
    private double _boomPhase;

    public Hit(Acoustic m, float kJ, uint seed, float sizeScale = 1)
    {
        _n = new Noise(seed);
        float f1 = m.F1 / (1 + 0.18f * MathF.Log10(1 + kJ)) / sizeScale; // 큰 충돌은 넓은 판을 울린다 — 낮아진다
        _modes = new Mode[Ratio.Length];
        for (int k = 0; k < Ratio.Length; k++)
        {
            float f = f1 * Ratio[k] * (1 + 0.04f * _n.White());
            if (f > Dsp.Top * 0.95f) { _modes[k] = default; continue; }
            float t60 = Math.Clamp(2.2f / (f * m.Eta), 0.04f, 4f);
            float amp = MathF.Pow(1f / (k + 1), 1.4f - m.Hard) * 0.5f;
            _modes[k] = new Mode(f, t60, amp, _n.White() * MathF.PI);
        }
        _contactHz = 800 + 5000 * m.Hard;
        _boomAmp = kJ > 50 ? Math.Min(1f, MathF.Log10(kJ / 50f) * 0.6f) : 0;
        _len = (int)(Dsp.Rate * Math.Clamp(2.2f / (f1 * m.Eta), 0.3f, 4f));
    }

    public override float Next()
    {
        if (_i >= _len) { Done = true; return 0; }
        float t = _i++ / (float)Dsp.Rate;
        float s = 0;
        for (int k = 0; k < _modes.Length; k++) s += _modes[k].Next();
        s += _contact.Low(_n.White(), _contactHz) * MathF.Exp(-t * 120f) * 1.2f;
        if (_boomAmp > 0)
        {
            _boomPhase += (55 - 18 * t) / Dsp.Rate;
            s += _boomAmp * MathF.Sin((float)(_boomPhase * Dsp.Tau)) * MathF.Exp(-t * 2.5f);
        }
        return s * 0.54f;
    }
}

/// <summary>쩍 (금): 높은 잡음이 짧게 갈라지고 · 재질의 높은 갈래가 운다.</summary>
public sealed class Crack : Voice
{
    private readonly Mode _a, _b;
    private Noise _n;
    private float _prev;
    private int _i;
    public Crack(Acoustic m, uint seed)
    {
        _n = new Noise(seed);
        _a = new Mode(m.F1 * 8.5f, Math.Clamp(2.2f / (m.F1 * 8.5f * m.Eta), 0.05f, 1f), 0.3f);
        _b = new Mode(m.F1 * 13f, Math.Clamp(2.2f / (m.F1 * 13f * m.Eta), 0.05f, 0.8f), 0.2f);
    }
    public override float Next()
    {
        if (_i > Dsp.Rate * 0.8f) { Done = true; return 0; }
        float t = _i++ / (float)Dsp.Rate;
        float x = _n.White(), hi = x - 0.9f * _prev; _prev = x;
        return hi * MathF.Exp(-t * 35f) * 0.7f + _a.Next() + _b.Next();
    }
}

/// <summary>끼익: 압력을 버티는 문 · 판 — 붙었다 미끄러지는 마찰(맥박의 간격이 흔들린다)이 판의 낮은 갈래를 두드린다.</summary>
public sealed class Creak : Voice
{
    private readonly Biquad[] _res;
    private Noise _n;
    private int _i;
    private float _next;
    private readonly int _len;
    private readonly float _period;
    public Creak(Acoustic m, float load, uint seed)
    {
        _n = new Noise(seed);
        _res = new[] { Biquad.Band(m.F1 * 1.0f, 25), Biquad.Band(m.F1 * 2.5f, 20), Biquad.Band(m.F1 * 4f, 15) };
        _len = (int)(Dsp.Rate * (0.6f + 0.5f * load));
        _period = Dsp.Rate / (60f + 80f * load); // 버티는 힘이 클수록 빠르게 걸렸다 미끄러진다
    }
    public override float Next()
    {
        if (_i >= _len) { Done = true; return 0; }
        float env = MathF.Sin(MathF.PI * _i / _len);
        float pulse = 0;
        if (_i >= _next) { pulse = 1f; _next = _i + _period * (1 + 0.35f * _n.White()); }
        _i++;
        float s = _res[0].Run(pulse) * 1.0f + _res[1].Run(pulse) * 0.6f + _res[2].Run(pulse) * 0.35f;
        return 150f * env * s;   // 좁은 띠(Q 25)는 맥박 하나로 조금만 운다 — 예전 3배는 거의 들리지 않았다 (−46dB)
    }
}

/// <summary>
/// 쉬익 (새는 공기): 틈으로 뿜는 바람 — 분출 속도 v(막힘 흐름이면 소리 빠르기 · 아니면 √(2Δp/ρ))와 틈 크기 d에서
/// 띠의 가운데 f = 0.2·v/d (스트로할) · 크기는 흐름 세기에서. 이어 돈다 (흐름이 바뀌면 부드럽게 따라간다).
/// </summary>
public sealed class Jet : Voice
{
    private Noise _n;
    private Biquad _band, _band2;
    private float _hz, _level, _curHz = 1000, _curLevel;
    private int _k;
    public Jet(uint seed) { _n = new Noise(seed); _band = Biquad.Band(1000, 0.7f); _band2 = Biquad.Band(2000, 1.2f); }
    public void Set(float hz, float level) { _hz = Math.Clamp(hz, 150, 14000); _level = level; }
    public void Stop() => _level = 0;
    public override float Next()
    {
        _curLevel += (_level - _curLevel) * 0.0008f * Dsp.K;
        if ((_k++ & 63) == 0)
        {
            _curHz += (_hz - _curHz) * 0.05f * Dsp.K;
            _band.SetBand(_curHz, 0.7f); _band2.SetBand(_curHz * 1.9f, 1.4f);
        }
        if (_level <= 0 && _curLevel < 1e-4f) { Done = true; return 0; }
        float w = _n.White();
        return _curLevel * (_band.Run(w) * 1.6f + _band2.Run(w) * 0.6f) * 0.68f * MathF.Pow(1000f / _curHz, 0.31f);   // 띠가 높을수록 넓다 — 같은 크기로
    }
}

/// <summary>
/// 불: 열 방출량(kW)에서 — 낮은 우르릉(갈색 잡음을 불길 크기만큼 낮게 거르고 일렁이게) + 탁탁(타는 것이 튀는 짧은 딱 소리 · 불이 클수록 잦다).
/// </summary>
public sealed class Fire : Voice
{
    private Noise _n;
    private OnePole _roar, _flick, _dc;
    private Biquad _pop;
    private float _kw, _curKw;
    public Fire(uint seed) { _n = new Noise(seed); _pop = Biquad.Band(2600, 2.5f); }
    public void Set(float kw) => _kw = kw;
    public override float Next()
    {
        _curKw += (_kw - _curKw) * 0.0005f * Dsp.K;
        if (_kw <= 0 && _curKw < 0.5f) { Done = true; return 0; }
        float size = MathF.Log10(1 + _curKw);                 // 1kW 0.3 · 100kW 2 · 1MW 3
        float flicker = 0.6f + 0.4f * _flick.Low(_n.White() * 3f, 6f);
        float roar = _dc.High(_roar.Low(_n.Brown(), 90 + 60 * size), 30) * flicker * 2.2f; // 갈색 잡음의 아주 느린 떠돎(30Hz 아래 — 들리지 않고 꼭대기만 먹는다)은 덜어 낸다
        float pop = _n.Chance(0.0004f * Dsp.K * size * size) ? _n.White() * 4f : 0;
        return Math.Min(1f, size / 3f) * (roar + _pop.Run(pop) * 0.8f) * 2.2f;
    }
}

/// <summary>
/// 기계: 돌아가는 장치의 웅웅 (전원 주파수 배음) + 팬 바람 · 날개 박자. 이어 돈다.
/// 낡은 베어링 (wear dB — 엔진의 낡은 몫): 굴림 요소가 결함을 지날 때마다(바깥 궤도 결함 박자 BPFO ≈ 3.6 × 축 회전 — 29.5Hz 축이면 약 105Hz)
/// 하우징의 고유 진동(2.5kHz 언저리)을 두드리는 딱딱 소리 + 거친 우르릉 — 낡을수록 세고 박자가 흔들린다 (베어링 고장 3 · 4단계).
/// </summary>
public sealed class Machine : Voice
{
    private Noise _n;
    private OnePole _air, _rumble;
    private Biquad _housing = Biquad.Band(2500, 6);
    private double _ph;
    private readonly float _f0, _fan, _whine;
    private float _level, _cur, _wear;
    private int _i;
    private float _nextKnock;
    private const float Bpfo = 105;
    public Machine(string role, uint seed)
    {
        _n = new Noise(seed);
        (_f0, _fan, _whine) = role switch
        {
            "reactor" => (40f, 0.3f, 0.0f),
            "engine" => (32f, 0.6f, 0.0f),
            "pump" => (50f, 0.4f, 0.5f),
            "hvac" or "scrubber" or "dehumid" => (50f, 0.9f, 0.2f),
            "fridge" => (60f, 0.2f, 0.1f),
            "computer" => (120f, 0.6f, 0.3f),
            "industry" or "recycler" => (45f, 0.5f, 0.4f),
            _ => (50f, 0.3f, 0.1f),
        };
    }
    public void Set(float level) => _level = level;
    /// <summary>크기 · 낡은 몫 (dB — 0 새것 · 12 곧 망가진다).</summary>
    public void Set(float level, float wear) { _level = level; _wear = Math.Clamp(wear, 0, 12); }
    public override float Next()
    {
        _cur += (_level - _cur) * 0.0005f * Dsp.K;
        if (_level <= 0 && _cur < 1e-4f) { Done = true; return 0; }
        float t = _i / (float)Dsp.Rate;
        _ph += _f0 / Dsp.Rate;
        float p = (float)(_ph * Dsp.Tau);
        float hum = MathF.Sin(p) + 0.5f * MathF.Sin(2 * p) + 0.25f * MathF.Sin(3 * p);
        float blade = 0.6f + 0.4f * MathF.Sin(t * Dsp.Tau * _f0 * 0.4f);
        float air = _air.Low(_n.White(), 700) * _fan * blade * 2f;
        float whine = _whine * 0.3f * MathF.Sin(p * 24f);
        float worn = 0;
        if (_wear > 0.5f)
        {
            float k = _wear / 12f;                                   // 0 ~ 1
            float knock = 0;
            if (_i >= _nextKnock) { knock = 1; _nextKnock = _i + Dsp.Rate / Bpfo * (1 + 0.25f * k * _n.White()); }
            worn = _housing.Run(knock * (0.5f + 0.5f * _n.White())) * 20f * k       // 딱딱 (하우징이 운다)
                 + _rumble.Low(_n.White(), 180) * 1.2f * k * k;                     // 거친 우르릉 (4단계 — 띠가 넓어진다)
        }
        _i++;
        return _cur * (0.5f * hum + air + whine + worn) * 0.57f;
    }
}

/// <summary>목소리 (외침 · 말 · 비명): 성대 맥박(사람마다 높이) → 모음 띠 셋 · 두 음절. 비명은 높이 치솟고 갈라진다. 말뜻은 없다.</summary>
public sealed class Shout : Voice
{
    private static readonly (float f1, float f2, float f3)[] Vowels = { (730, 1090, 2440), (570, 840, 2410), (300, 2290, 3010), (530, 1840, 2480), (440, 1020, 2240) };
    private Noise _n;
    private Biquad _a, _b, _c;
    private double _ph;
    private readonly float _f0;
    private readonly bool _scream;
    private readonly int _len, _syl;
    private readonly int _v1, _v2;
    private int _i;
    public Shout(float f0, bool scream, uint seed)
    {
        _n = new Noise(seed);
        _f0 = f0; _scream = scream;
        _len = (int)(Dsp.Rate * (scream ? 0.9f : 0.6f));
        _syl = scream ? _len : _len / 2;
        _v1 = (int)((_n.White() + 1) * 2.49f); _v2 = (int)((_n.White() + 1) * 2.49f);
        Set(_v1);
    }
    private void Set(int v) { var f = Vowels[v]; _a = Biquad.Band(f.f1, 6); _b = Biquad.Band(f.f2, 8); _c = Biquad.Band(f.f3, 10); }
    public override float Next()
    {
        if (_i >= _len) { Done = true; return 0; }
        if (_i == _syl && !_scream) Set(_v2);
        float t = _i / (float)Dsp.Rate, ts = (_i % _syl) / (float)_syl;
        _i++;
        float f = _scream ? _f0 * (2.4f + 0.8f * MathF.Min(1, t / 0.15f) - 0.6f * MathF.Max(0, t - 0.5f)) + 30 * MathF.Sin(t * Dsp.Tau * 7) : _f0 * (1.15f - 0.25f * ts);
        _ph += f / Dsp.Rate;
        float ph = (float)(_ph - Math.Floor(_ph));
        float glottal = ph < 0.4f ? MathF.Sin(ph / 0.4f * MathF.PI) : 0;
        float src = glottal + (_scream ? 0.35f : 0.06f) * _n.White();
        float env = MathF.Pow(MathF.Sin(MathF.PI * ts), 0.6f);
        return env * (_a.Run(src) * 1.0f + _b.Run(src) * 0.7f + _c.Run(src) * 0.35f) * (_scream ? 1.45f : 1.5f); // 다른 소리와 같은 크기로 (좁은 모음 띠가 많이 먹는다)
    }
}

/// <summary>발소리: 바닥 재질에 따라 — 단단한 금속 판은 짧게 울리고(갈래 둘) · 무른 바닥은 툭 하고 만다. 로봇은 구동음.</summary>
public sealed class Step : Voice
{
    private Noise _n;
    private Mode _ring1, _ring2;
    private int _i;
    private readonly float _hard;
    public Step(Acoustic floor, uint seed)
    {
        _n = new Noise(seed);
        _hard = floor.Hard;
        _ring1 = new Mode(floor.F1 * 4f, Math.Clamp(1.2f / (floor.F1 * 4f * floor.Eta), 0.02f, 0.25f), 0.25f * floor.Hard);
        _ring2 = new Mode(floor.F1 * 6.5f, Math.Clamp(1.2f / (floor.F1 * 6.5f * floor.Eta), 0.02f, 0.18f), 0.15f * floor.Hard);
    }
    public override float Next()
    {
        if (_i > Dsp.Rate * 0.25f) { Done = true; return 0; }
        float t = _i++ / (float)Dsp.Rate;
        float thud = MathF.Sin(t * Dsp.Tau * (110f - 300f * t)) * MathF.Exp(-t * 45f);
        float scuff = _n.White() * MathF.Exp(-t * 90f) * (0.2f + 0.3f * _hard);
        return (0.8f * (thud + scuff) + _ring1.Next() + _ring2.Next()) * 1.2f;
    }
}

/// <summary>공압 문: 띠를 거른 바람이 부풀었다 가라앉고 · 끝에 낮게 "턱" (옛 게임 파형).</summary>
public sealed class Door : Voice
{
    private Noise _n;
    private float _lp, _prev;
    private int _i;
    private static readonly float DoorA = Dsp.Per(0.25f);   // 1kHz 언저리 낮은 음 거름
    public Door(uint seed) { _n = new Noise(seed); }
    public override float Next()
    {
        int n = (int)(Dsp.Rate * 0.42f);
        if (_i >= n) { Done = true; return 0; }
        float t = _i++ / (float)Dsp.Rate;
        float x = _n.White();
        _lp += DoorA * (x - _lp);
        float hp = _lp - _prev; _prev = _lp;
        float env = t < 0.32f ? MathF.Sin(MathF.PI * t / 0.32f) : 0f;
        float thunk = t > 0.3f ? MathF.Sin((t - 0.3f) * Dsp.Tau * 70f) * MathF.Exp(-(t - 0.3f) * 30f) : 0f;
        return (0.9f * env * (_lp * 0.6f + hp * 1.2f) + 0.5f * thunk) * 1.7f;
    }
}

/// <summary>경보: 주컴퓨터 방송은 낮게 세 번 "띵띵띵" · 원자로 긴급 정지 · 큰 경보는 사이렌 (옛 게임 파형).</summary>
public sealed class Alarm : Voice
{
    private readonly bool _siren;
    private double _ph;
    private int _i;
    public Alarm(bool siren) { _siren = siren; }
    public override float Next()
    {
        float len = _siren ? 1.3f : 0.75f;
        if (_i >= len * Dsp.Rate) { Done = true; return 0; }
        float t = _i++ / (float)Dsp.Rate;
        if (_siren)
        {
            float f = 620f + 280f * (0.5f + 0.5f * MathF.Sin(t * Dsp.Tau * 1.5f));
            _ph += f / Dsp.Rate;
            float x = MathF.Sin((float)(_ph * Dsp.Tau)) + 0.3f * MathF.Sin((float)(_ph * Dsp.Tau * 3));
            return 0.34f * x * MathF.Min(1f, t / 0.05f) * MathF.Min(1f, (len - t) / 0.15f);
        }
        float v = 0;
        for (int k = 0; k < 3; k++)
        {
            float tk = t - k * 0.22f;
            if (tk < 0f) continue;
            v += MathF.Sin(tk * Dsp.Tau * 523f) * MathF.Exp(-tk * 9f) + 0.4f * MathF.Sin(tk * Dsp.Tau * 1046f) * MathF.Exp(-tk * 14f);
        }
        return 0.46f * MathF.Min(1f, t / 0.004f) * v;
    }
}

/// <summary>기계가 상하는 소리 (끽끽 · 덜컹): 거친 톱니 · 불규칙한 쿵.</summary>
public sealed class Grind : Voice
{
    private Noise _n;
    private double _ph;
    private OnePole _lp;
    private int _i;
    public Grind(uint seed) { _n = new Noise(seed); }
    public override float Next()
    {
        if (_i > Dsp.Rate * 0.7f) { Done = true; return 0; }
        float t = _i++ / (float)Dsp.Rate;
        _ph += (300 + 180 * MathF.Sin(t * Dsp.Tau * 9) + 60 * _n.White()) / Dsp.Rate;
        float ph = (float)(_ph - Math.Floor(_ph));
        float saw = _lp.Low(2 * ph - 1, 2500);
        float clunk = _n.Chance(0.0006f * Dsp.K) ? 3f : 0;
        return (MathF.Sin(MathF.PI * t / 0.7f) * (0.5f * saw + 0.3f * _n.White() * MathF.Abs(saw)) + clunk * MathF.Exp(-t)) * 0.87f;
    }
}

/// <summary>방의 공기: 환기 바람 · 먼 기계가 섞인 낮은 쉬 — 공기가 짙을수록 (진공이면 없다).</summary>
public sealed class RoomTone : Voice
{
    private Noise _n;
    private OnePole _lp, _lp2;
    private float _level, _cur;
    public RoomTone(uint seed) { _n = new Noise(seed); Send = 0; }
    public void Set(float level) => _level = level;
    public override float Next()
    {
        _cur += (_level - _cur) * 0.0003f * Dsp.K;
        float p = _n.Pink();
        return _cur * (_lp.Low(p, 380) * 1.3f + 0.25f * _lp2.Low(_n.White(), 2200)) * 0.42f;
    }
}

/// <summary>우주복 안: 내 숨 (들숨 · 날숨) · 조절기가 산소를 내보내는 쉬익 — 숨이 가쁠수록 빠르게.</summary>
public sealed class Breath : Voice
{
    private Noise _n;
    private Biquad _mouth, _valve;
    private double _ph;
    private float _rate = 0.25f, _level;
    public Breath(uint seed) { _n = new Noise(seed); _mouth = Biquad.Band(900, 0.9f); _valve = Biquad.Band(4200, 3f); Send = 0; }
    public void Set(float breathsPerSec, float level) { _rate = Math.Clamp(breathsPerSec, 0.15f, 1.2f); _level = level; }
    public override float Next()
    {
        _ph += _rate / Dsp.Rate;
        float c = (float)(_ph - Math.Floor(_ph));
        float inhale = c < 0.4f ? MathF.Sin(MathF.PI * c / 0.4f) : 0;
        float exhale = c >= 0.45f && c < 0.9f ? MathF.Sin(MathF.PI * (c - 0.45f) / 0.45f) : 0;
        float w = _n.White();
        return _level * (_mouth.Run(w) * (0.8f * exhale + 0.4f * inhale) + _valve.Run(w) * 0.5f * inhale);
    }
}

/// <summary>
/// 연출의 낮은 울림 (함장 결정 2 — 세계에 없는 소리 · 소리 출력에서만 하는 과장): 큰 쾅에 몸으로 느끼는 48 → 28Hz로 내려앉는 사인 ·
/// 진공에서 발밑으로 온 쾅에 둔한 울림 (영화 '그래비티'처럼 몸을 타고 오는 소리). 세계의 소리값 · 감각 · 판단은 건드리지 않는다.
/// </summary>
public sealed class Boom : Voice
{
    private double _ph;
    private int _i;
    private readonly float _amp, _decay;
    private readonly int _len;
    public Boom(float amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        _amp = 0.9f * amount;
        _decay = 2.6f / (0.5f + amount);                       // 클수록 길게
        _len = (int)(Dsp.Rate * (0.8f + 1.4f * amount));
        Send = 0;
    }
    public override float Next()
    {
        if (_i >= _len) { Done = true; return 0; }
        float t = _i++ / (float)Dsp.Rate;
        _ph += (28 + 20 * MathF.Exp(-t * 3)) / Dsp.Rate;
        float env = MathF.Min(1, t / 0.012f) * MathF.Exp(-t * _decay) * MathF.Min(1, (_len - _i) / (0.05f * Dsp.Rate));
        return _amp * env * MathF.Sin((float)(_ph * Dsp.Tau));
    }
}

/// <summary>
/// 먹먹한 귀의 삐 소리 (이명 — 고른 사람의 일시 청력 손실만큼): 4kHz 언저리 두 사인이 천천히 맥놀이 · 손실이 클수록 크다 (40dB면 가장 크게) ·
/// 손실이 줄면 따라 작아진다. 세계의 소리가 아니라 그 사람의 귀가 만든 소리다 (소리 출력에서만).
/// </summary>
public sealed class Ring : Voice
{
    private double _a, _b;
    private float _level, _cur;
    private readonly float _hz;
    public Ring(uint seed) { _hz = 3800 + (seed % 700); Send = 0; }
    public void Set(float deafDb) => _level = Math.Clamp(deafDb / 40f, 0, 1);
    public override float Next()
    {
        _cur += (_level - _cur) * 0.00002f * Dsp.K;
        if (_level <= 0 && _cur < 1e-4f) { Done = true; return 0; }
        _a += _hz / Dsp.Rate; _b += (_hz * 1.004) / Dsp.Rate;
        return _cur * 0.17f * (MathF.Sin((float)(_a * Dsp.Tau)) + 0.7f * MathF.Sin((float)(_b * Dsp.Tau)));
    }
}
