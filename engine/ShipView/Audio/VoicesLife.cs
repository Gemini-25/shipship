namespace ShipView.Audio;

// 생활 소리 (소리 단계 S2 — docs/SOUND.md 결정 8): 세계가 낸 소리(식기 · 공구 · 용접 · 망치 · 코골이 · 잠꼬대 · 기침 · 웃음 · 흥얼)를 물리 모양으로 만든다.
// 모두 씨앗이 같으면 같은 소리 · 길이가 정해져 있다 (섞개가 다 그치면 치운다).

/// <summary>딸그락 (식기 · 컵 · 냄비): 한두세 번 부딪힘 — 도자기 · 금속의 높은 갈래 셋이 짧게 운다 (컵은 높고 짧게 · 냄비는 낮고 길게).</summary>
public sealed class Clink : Voice
{
    private readonly Mode[] _m;
    private readonly int[] _at;
    private Noise _n;
    private int _i;
    private readonly int _len;
    public Clink(uint seed, float size = 1)
    {
        _n = new Noise(seed);
        int strikes = 1 + (int)((_n.White() + 1) * 1.49f);
        _m = new Mode[strikes * 3]; _at = new int[strikes];
        float t = 0;
        for (int s = 0; s < strikes; s++)
        {
            _at[s] = (int)(t * Dsp.Rate);
            float f = (2400 + 1600 * (_n.White() + 1) * 0.5f) / size, amp = 0.35f * (s == 0 ? 1 : 0.6f);
            _m[s * 3] = new Mode(f, 0.25f * size, amp);
            _m[s * 3 + 1] = new Mode(f * 2.32f, 0.12f * size, amp * 0.6f);
            _m[s * 3 + 2] = new Mode(f * 4.25f, 0.06f * size, amp * 0.35f);
            t += 0.08f + 0.17f * (_n.White() + 1) * 0.5f;
        }
        _len = (int)((t + 0.4f * size) * Dsp.Rate);
        Send = 0.2f;
    }
    public override float Next()
    {
        if (_i >= _len) { Done = true; return 0; }
        float x = 0;
        for (int s = 0; s < _at.Length; s++)
        {
            if (_i < _at[s]) continue;
            float tk = (_i - _at[s]) / (float)Dsp.Rate;
            x += _m[s * 3].Next() + _m[s * 3 + 1].Next() + _m[s * 3 + 2].Next() + _n.White() * 0.4f * MathF.Exp(-tk * 900);
        }
        _i++;
        return x * 2.1f;
    }
}

/// <summary>공구 (렌치 딸깍딸깍): 래칫 톱니가 걸릴 때마다 짧은 딸깍 (높은 잡음 + 3.2kHz 금속 울림) — 초당 14번 · 두 번 당긴다 · 사이에 쉼.</summary>
public sealed class Ratchet : Voice
{
    private Noise _n;
    private Mode _ring;
    private OnePole _hp;
    private int _i, _next;
    private readonly int _len;
    private float _burst;
    public Ratchet(uint seed, float pitch = 1)
    {
        _n = new Noise(seed);
        _len = (int)(Dsp.Rate * 1.2f);
        _ring = new Mode(3200 * pitch, 0.03f, 0);
        Send = 0.2f;
    }
    public override float Next()
    {
        if (_i >= _len) { Done = true; return 0; }
        float t = _i / (float)Dsp.Rate;
        bool pulling = t < 0.45f || t > 0.65f && t < 1.1f;
        if (pulling && _i >= _next)
        {
            _burst = 1;
            _ring = new Mode(3200 * (1 + 0.03f * _n.White()), 0.035f, 0.35f);
            _next = _i + (int)(Dsp.Rate / 14f * (1 + 0.1f * _n.White()));
        }
        _i++;
        float click = _hp.High(_n.White(), 1800) * _burst;
        _burst *= MathF.Exp(-1f / (0.002f * Dsp.Rate));
        return (click * 0.9f + _ring.Next()) * 2.9f;
    }
}

/// <summary>용접 · 봉합 (지지직): 아크가 튀는 잦은 딱딱(초당 300번 남짓 — 3kHz 띠) + 100Hz 웅 (전원) · 1.6초.</summary>
public sealed class Weld : Voice
{
    private Noise _n;
    private Biquad _band, _band2;
    private double _ph;
    private int _i;
    private readonly int _len;
    public Weld(uint seed)
    {
        _n = new Noise(seed);
        _band = Biquad.Band(3000, 0.8f); _band2 = Biquad.Band(6500, 1.2f);
        _len = (int)(Dsp.Rate * 1.6f);
        Send = 0.2f;
    }
    public override float Next()
    {
        if (_i >= _len) { Done = true; return 0; }
        float t = _i++ / (float)Dsp.Rate;
        float env = MathF.Min(1, t / 0.08f) * MathF.Min(1, (1.6f - t) / 0.12f);
        float pop = _n.Chance(300f / Dsp.Rate) ? _n.White() * 3f : 0;
        float crackle = _band.Run(pop + 0.15f * _n.White()) * 1.4f + _band2.Run(pop) * 0.6f;
        _ph += 100.0 / Dsp.Rate;
        float buzz = 0.04f * (MathF.Sin((float)(_ph * Dsp.Tau)) + 0.5f * MathF.Sin((float)(_ph * Dsp.Tau * 3)));
        return env * (crackle + buzz) * 1.5f;
    }
}

/// <summary>망치 (탕탕): 두세 번 내리친다 (0.55초마다) — 맞은 강철 판의 갈래 넷 (420Hz · 비 1 · 2.6 · 4.1 · 6.3) + 닿는 순간의 잡음.</summary>
public sealed class Hammer : Voice
{
    private static readonly float[] Ratio = { 1f, 2.6f, 4.1f, 6.3f };
    private readonly Mode[] _m;
    private readonly int[] _at;
    private Noise _n;
    private OnePole _lp;
    private int _i;
    private readonly int _len;
    public Hammer(uint seed)
    {
        _n = new Noise(seed);
        int strikes = 2 + (_n.White() > 0 ? 1 : 0);
        _m = new Mode[strikes * Ratio.Length]; _at = new int[strikes];
        float f1 = 420 * (1 + 0.08f * _n.White());
        for (int s = 0; s < strikes; s++)
        {
            _at[s] = (int)(s * 0.55f * Dsp.Rate);
            for (int k = 0; k < Ratio.Length; k++)
                _m[s * Ratio.Length + k] = new Mode(f1 * Ratio[k] * (1 + 0.02f * _n.White()), Math.Clamp(0.6f / (k + 1), 0.08f, 0.6f), 0.45f / (k + 1));
        }
        _len = (int)(((strikes - 1) * 0.55f + 0.7f) * Dsp.Rate);
        Send = 0.35f;
    }
    public override float Next()
    {
        if (_i >= _len) { Done = true; return 0; }
        float x = 0;
        for (int s = 0; s < _at.Length; s++)
        {
            if (_i < _at[s]) continue;
            float tk = (_i - _at[s]) / (float)Dsp.Rate;
            for (int k = 0; k < Ratio.Length; k++) x += _m[s * Ratio.Length + k].Next();
            x += _lp.Low(_n.White(), 4000) * 1.6f * MathF.Exp(-tk * 160);
        }
        _i++;
        return x * 1.05f;
    }
}

/// <summary>
/// 코골이 (드르렁): 들숨에 목젖이 떨린다 — 30 ~ 50Hz 맥박(사람마다 · 조금씩 흔들림)이 목 · 입의 울림(450 · 1,100Hz)을 지나고 · 거친 숨 잡음이 섞인다 (1.4초) ·
/// 날숨은 낮게 쉬 (0.7초).
/// </summary>
public sealed class Snore : Voice
{
    private Noise _n;
    private Biquad _f1, _f2;
    private OnePole _breath;
    private readonly float _rate;
    private float _next;
    private int _i;
    private readonly int _in, _len;
    public Snore(uint seed, float heavy)
    {
        _n = new Noise(seed);
        _rate = 32 + 18 * (_n.White() + 1) * 0.5f;
        _f1 = Biquad.Band(450, 3); _f2 = Biquad.Band(1100, 4);
        _in = (int)(Dsp.Rate * (1.2f + 0.4f * heavy));
        _len = _in + (int)(Dsp.Rate * 0.7f);
        Send = 0.15f;
    }
    public override float Next()
    {
        if (_i >= _len) { Done = true; return 0; }
        float x;
        if (_i < _in)
        {
            float t = _i / (float)_in;
            float env = MathF.Pow(MathF.Sin(MathF.PI * t), 0.7f);
            float pulse = 0;
            if (_i >= _next) { pulse = 1; _next = _i + Dsp.Rate / _rate * (1 + 0.15f * _n.White()); }
            float src = pulse * 6 + 0.25f * _n.White();
            x = env * (_f1.Run(src) * 1.2f + _f2.Run(src) * 0.5f + _breath.Low(_n.White(), 900) * 0.3f);
        }
        else
        {
            float t = (_i - _in) / (float)(_len - _in);
            x = MathF.Sin(MathF.PI * t) * _breath.Low(_n.White(), 700) * 0.25f;
        }
        _i++;
        return x * 4.4f;
    }
}

/// <summary>목소리 한 갈래 (잠꼬대 · 웃음 · 흥얼거림의 바탕): 성대 맥박 → 모음 띠 셋.</summary>
public abstract class VoicedBase : Voice
{
    protected static readonly (float f1, float f2, float f3)[] Vowels = { (730, 1090, 2440), (570, 840, 2410), (300, 2290, 3010), (530, 1840, 2480), (440, 1020, 2240) };
    protected Noise N;
    protected Biquad A, B, C;
    protected double Ph;
    protected VoicedBase(uint seed) { N = new Noise(seed); SetVowel(0); }
    protected void SetVowel(int v) { var f = Vowels[v % Vowels.Length]; A = Biquad.Band(f.f1, 6); B = Biquad.Band(f.f2, 8); C = Biquad.Band(f.f3, 10); }
    /// <summary>높이 f0 · 숨 섞음 breath 에서 한 표본 (모음 띠를 지난 것).</summary>
    protected float Voiced(float f0, float breath)
    {
        Ph += f0 / Dsp.Rate;
        float ph = (float)(Ph - Math.Floor(Ph));
        float glottal = ph < 0.4f ? MathF.Sin(ph / 0.4f * MathF.PI) : 0;
        float src = glottal + breath * N.White();
        return A.Run(src) * 1.0f + B.Run(src) * 0.7f + C.Run(src) * 0.35f;
    }
}

/// <summary>잠꼬대 (웅얼웅얼): 낮고 작은 목소리 · 음절 셋 ~ 다섯 (초당 4개) · 모음이 바뀌고 · 입을 덜 열어 먹먹하다.</summary>
public sealed class Mumble : VoicedBase
{
    private readonly float _f0;
    private readonly int _syl, _len;
    private int _i;
    private OnePole _lp;
    public Mumble(float f0, uint seed) : base(seed)
    {
        _f0 = f0 * 0.9f;
        _syl = (int)(Dsp.Rate * 0.24f);
        _len = _syl * (3 + (int)((N.White() + 1) * 1.1f));
        Send = 0.15f;
    }
    public override float Next()
    {
        if (_i >= _len) { Done = true; return 0; }
        if (_i % _syl == 0) SetVowel((int)((N.White() + 1) * 2.49f));
        float ts = (_i % _syl) / (float)_syl;
        _i++;
        float env = MathF.Pow(MathF.Sin(MathF.PI * ts), 0.8f) * (0.6f + 0.4f * N.White() * 0.5f + 0.2f);
        return _lp.Low(Voiced(_f0 * (1.05f - 0.1f * ts), 0.12f), 1800) * env * 3.6f;
    }
}

/// <summary>기침 (콜록): 두세 번 — 성문이 터지듯 열리며 거친 바람(목 · 입의 띠 400 · 1,400 · 2,500Hz) + 처음 60ms는 목소리가 섞인다.</summary>
public sealed class CoughVoice : VoicedBase
{
    private readonly float _f0;
    private readonly int _burst, _gap, _n;
    private int _i;
    private Biquad _t1, _t2, _t3;
    public CoughVoice(float f0, uint seed) : base(seed)
    {
        _f0 = f0;
        _burst = (int)(Dsp.Rate * 0.26f); _gap = (int)(Dsp.Rate * 0.12f);
        _n = 2 + (N.White() > 0.2f ? 1 : 0);
        _t1 = Biquad.Band(400, 2); _t2 = Biquad.Band(1400, 3); _t3 = Biquad.Band(2500, 4);
        SetVowel(1);
        Send = 0.2f;
    }
    public override float Next()
    {
        int period = _burst + _gap;
        if (_i >= period * _n) { Done = true; return 0; }
        int k = _i % period;
        _i++;
        if (k >= _burst) return 0;
        float t = k / (float)Dsp.Rate;
        float env = MathF.Min(1, t / 0.004f) * MathF.Exp(-t * 14);
        float w = N.White();
        float air = _t1.Run(w) * 1.0f + _t2.Run(w) * 0.8f + _t3.Run(w) * 0.4f;
        float voice = t < 0.06f ? Voiced(_f0 * 1.1f, 0.3f) * 1.5f : 0;
        return env * (air * 1.6f + voice) * 2.1f;
    }
}

/// <summary>웃음 (하하하): 음절 다섯 남짓 (0.18초마다) — 'ㅎ' 숨 25ms + '아' 모음 · 높이가 조금씩 내려가고 · 뒤로 갈수록 작다.</summary>
public sealed class LaughVoice : VoicedBase
{
    private readonly float _f0;
    private readonly int _syl, _count;
    private int _i;
    public LaughVoice(float f0, uint seed) : base(seed)
    {
        _f0 = f0;
        _syl = (int)(Dsp.Rate * 0.18f);
        _count = 4 + (int)((N.White() + 1) * 1.2f);
        SetVowel(0);
        Send = 0.25f;
    }
    public override float Next()
    {
        if (_i >= _syl * _count) { Done = true; return 0; }
        int s = _i / _syl, k = _i % _syl;
        _i++;
        float t = k / (float)Dsp.Rate, len = _syl / (float)Dsp.Rate * 0.7f;
        if (t > len) return 0;
        float fall = 1 - 0.035f * s;
        float env = MathF.Min(1, t / 0.01f) * MathF.Min(1, (len - t) / 0.03f) * (1 - 0.1f * s);
        float x = t < 0.025f ? N.White() * 0.5f : Voiced(_f0 * 1.35f * fall * (1.05f - 0.1f * t / len), 0.25f) * 3.5f;
        return env * x * 1.3f;
    }
}

/// <summary>흥얼거림 (음~): 입을 다문 목소리 — 콧소리 띠(250Hz) · 800Hz 위는 먹먹 · 다섯 음 가락(5음계)에서 서너 음을 미끄러지듯 (2.4초).</summary>
public sealed class HumVoice : VoicedBase
{
    private static readonly float[] Scale = { 0, 2, 4, 7, 9, 12 };
    private readonly float _f0;
    private readonly float[] _notes;
    private readonly int _note, _len;
    private int _i;
    private float _cur;
    private Biquad _nasal;
    private OnePole _lp, _dc;
    public HumVoice(float f0, uint seed) : base(seed)
    {
        _f0 = f0;
        _notes = new float[3 + (N.White() > 0 ? 1 : 0)];
        for (int k = 0; k < _notes.Length; k++) _notes[k] = Scale[(int)((N.White() + 1) * 0.5f * Scale.Length) % Scale.Length];
        _note = (int)(Dsp.Rate * 0.6f);
        _len = _note * _notes.Length;
        _cur = _notes[0];
        _nasal = Biquad.Band(250, 2);
        Send = 0.25f;
    }
    public override float Next()
    {
        if (_i >= _len) { Done = true; return 0; }
        float t = _i / (float)Dsp.Rate, total = _len / (float)Dsp.Rate;
        float target = _notes[Math.Min(_notes.Length - 1, _i / _note)];
        _cur += (target - _cur) * (1 - MathF.Exp(-1f / (0.04f * Dsp.Rate)));
        _i++;
        float f = _f0 * MathF.Pow(2, _cur / 12f) * (1 + 0.004f * MathF.Sin(t * Dsp.Tau * 5));
        Ph += f / Dsp.Rate;
        float ph = (float)(Ph - Math.Floor(Ph));
        float glottal = ph < 0.5f ? MathF.Sin(ph / 0.5f * MathF.PI) : 0;
        float env = MathF.Min(1, t / 0.15f) * MathF.Min(1, (total - t) / 0.25f);
        return _dc.High(env * (_lp.Low(glottal, 800) * 0.8f + _nasal.Run(glottal) * 0.6f), 70) * 0.62f;   // 반쪽 사인 맥박의 직류는 덜어 낸다
    }
}

/// <summary>
/// 샤워 물 (쏴아 — 이어 도는 소리 · 5단 A4): 물줄기가 받침을 때리는 흰 잡음 (1 ~ 9kHz 띠 · 물줄기가 고르지 않아 조금씩 일렁인다) +
/// 물방울이 받침을 때리는 투둑 (초당 80번 남짓 · 2 ~ 6kHz 짧은 울림) + 배수구 꾸르륵 (250Hz 좁은 띠 · 0.3Hz로 너울진다).
/// 그 칸에 사람이 서 있으면 (person) 몸이 물을 받아 높은 음(5kHz 위)이 먹먹해지고 투둑이 준다.
/// </summary>
public sealed class Spray : Voice
{
    private Noise _n;
    private OnePole _hp1, _hp2, _lp1, _lp2, _body1, _body2, _flick;
    private Biquad _drain = Biquad.Band(250, 5);
    private readonly Mode[] _drops = new Mode[4];
    private int _drop;
    private double _swell;
    private float _level, _cur, _person, _curPerson;
    public Spray(uint seed) { _n = new Noise(seed); _swell = (_n.White() + 1) * 0.5; Send = 0.2f; }
    /// <summary>세기 · 그 칸에 사람이 서 있나 (0 ~ 1).</summary>
    public void Set(float level, float person) { _level = level; _person = Math.Clamp(person, 0, 1); }
    public void Stop() => _level = 0;
    public override float Next()
    {
        _cur += (_level - _cur) * 0.0005f * Dsp.K;
        if (_level <= 0 && _cur < 1e-4f) { Done = true; return 0; }
        _curPerson += (_person - _curPerson) * 0.0003f * Dsp.K;
        // 물줄기: 1 ~ 9kHz 띠 (두 극씩) · 고르지 않은 흐름 (12Hz 아래로 일렁임) · 몸이 받으면 5kHz 위가 준다
        float hiss = _lp2.Low(_lp1.Low(_hp2.High(_hp1.High(_n.White(), 1000), 1000), 9000), 9000);
        hiss *= 0.8f + 0.2f * _flick.Low(_n.White() * 3f, 12f);
        float muffled = _body2.Low(_body1.Low(hiss, 5000), 5000);
        hiss += (muffled - hiss) * _curPerson;
        // 투둑: 물방울이 받침을 때린다 (몸이 받으면 드물다)
        if (_n.Chance(80f * (1 - 0.6f * _curPerson) / Dsp.Rate))
        {
            float u = (_n.White() + 1) * 0.5f;
            _drops[_drop] = new Mode(2000 + 4000 * u, 0.012f + 0.02f * (_n.White() + 1) * 0.5f, 0.3f * (0.5f + 0.5f * u));
            _drop = (_drop + 1) & 3;
        }
        float patter = _drops[0].Next() + _drops[1].Next() + _drops[2].Next() + _drops[3].Next();
        // 배수구 꾸르륵: 좁은 띠 · 0.3Hz 너울 (물이 빠지며 숨을 쉬듯)
        _swell += 0.3 / Dsp.Rate;
        if (_swell >= 1) _swell -= 1;
        float sw = 0.5f + 0.5f * MathF.Sin((float)(_swell * Dsp.Tau));
        float gurgle = _drain.Run(_n.White()) * sw * sw;
        return _cur * (hiss * 0.7f + patter + gurgle * 2f) * 1.48f;   // 크기 기준 (가장 센 0.1초 RMS 0.25)
    }
}
