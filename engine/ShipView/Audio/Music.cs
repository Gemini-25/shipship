namespace ShipView.Audio;

/// <summary>
/// 바탕 음악 (docs/ENGINE.md §소리 엔진 · 바퀴 1) — 배 안 소리와 따로 도는 조용한 소리 바탕. 연출의 분위기를 따라 저절로 바뀐다.
/// <para><b>조용함</b>: 느린 패드 화음 (D 도리안 · 8초마다 바뀌고 2초에 걸쳐 넘어간다) · 가끔 맑은 음 하나 (D 단조 5음계).</para>
/// <para><b>조짐</b>: 화음이 어두워지고 (♭9 · 감화음) · 낮은 심장 박동 (55Hz 두 번 — 쿵쿵) 이 생긴다.</para>
/// <para><b>위험</b>: 박동이 빨라지고 (분당 50 → 120) · 거름이 열려 밝고 날카로워진다 · 화음이 4초마다.</para>
/// <para><b>결정적 순간</b>: 패드를 거의 비우고 낮은 D · A 지속음만 (두 음이 천천히 맥놀이) — 효과음이 주인공이 되게.</para>
/// 긴장(0~1)은 4초에 걸쳐 따라가 갑자기 바뀌지 않는다. 씨앗이 같으면 같은 음악 (확인 · 시험).
/// </summary>
public sealed class Score
{
    // 화음 (MIDI 음 번호): 잔잔한 넷 · 어두운 넷
    private static readonly int[][] Calm =
    {
        new[] { 50, 57, 60, 64, 65 },   // Dm9
        new[] { 46, 53, 57, 62, 64 },   // B♭maj7(#11)
        new[] { 41, 48, 55, 57, 64 },   // Fmaj9
        new[] { 48, 55, 57, 62, 64 },   // C6/9
    };
    private static readonly int[][] Dark =
    {
        new[] { 50, 53, 57, 63 },       // Dm(♭9 위)
        new[] { 46, 50, 53, 57 },       // B♭maj7
        new[] { 45, 52, 58, 61 },       // A7(♭9)
        new[] { 50, 53, 56, 60 },       // D 반감7
    };
    private static readonly int[] Sparkle = { 62, 65, 67, 69, 72, 74 };

    private const int Pads = 5;
    private readonly double[] _phA = new double[Pads * 2], _phB = new double[Pads * 2];
    private readonly float[] _hzA = new float[Pads], _hzB = new float[Pads];
    private float _mix = 1;                     // 0: 앞 화음 · 1: 지금 화음 (넘어가는 중)
    private int _chord, _set;
    private long _n, _nextChord, _nextBeat, _nextSpark;
    private float _t, _target;                  // 긴장 (0 조용함 ~ 1 결정적 순간)
    private OnePole _lpL1, _lpL2, _lpR1, _lpR2, _sub;
    private Mode _thump, _thump2, _spark;
    private long _thump2At = -1;
    private double _droneA, _droneB;
    private Noise _rng;

    public Score(uint seed = 11)
    {
        _rng = new Noise(seed);
        SetChord(0, 0, instant: true);
    }

    /// <summary>지금 긴장 (0~1 · 따라가는 중인 값).</summary>
    public float Tension => _t;
    /// <summary>패드를 자르는 높이 (Hz — 긴장할수록 열린다) · 패드 크기 (결정적 순간엔 0.15) · 지금 화음 갈래 (0 잔잔 · 1 어두움).</summary>
    public float Cut { get; private set; }
    public float Pad { get; private set; } = 1;
    public int ChordSet => _set;

    /// <summary>연출의 분위기 → 바라는 긴장.</summary>
    public void SetMood(string mood) => _target = mood switch
    {
        "조짐" => 0.35f,
        "위험" => 0.7f,
        "결정적 순간" => 1f,
        _ => 0f,
    };

    private static float Hz(int midi) => 440f * MathF.Pow(2f, (midi - 69) / 12f);

    private void SetChord(int set, int k, bool instant = false)
    {
        var src = set == 0 ? Calm : Dark;
        var notes = src[k % src.Length];
        for (int i = 0; i < Pads; i++)
        {
            _hzA[i] = _hzB[i];
            _hzB[i] = i < notes.Length ? Hz(notes[i]) : 0;
            _phA[i * 2] = _phB[i * 2]; _phA[i * 2 + 1] = _phB[i * 2 + 1];
        }
        if (instant) { Array.Copy(_hzB, _hzA, Pads); _mix = 1; } else _mix = 0;
        _set = set; _chord = k;
    }

    private static float Tri(double ph) { double x = ph - Math.Floor(ph); return (float)(x < 0.5 ? 4 * x - 1 : 3 - 4 * x); }

    /// <summary>한 표본 (왼 · 오).</summary>
    public (float l, float r) Next()
    {
        _n++;
        _t += (_target - _t) * (1f / (4f * Dsp.Rate));            // 4초에 걸쳐 따라간다
        // 화음 바꾸기: 조용하면 8초 · 위험이면 4초 · 결정적 순간엔 머문다
        if (_n >= _nextChord)
        {
            int set = _t > 0.25f ? 1 : 0;
            if (_t < 0.9f) SetChord(set, set == _set ? _chord + 1 : 0);
            double sec = 8 - 4 * Math.Min(1, _t / 0.7);
            _nextChord = _n + (long)(sec * Dsp.Rate);
        }
        _mix = Math.Min(1, _mix + 1f / (2f * Dsp.Rate));          // 2초에 걸쳐 넘어간다
        float pad = 1 - Math.Clamp((_t - 0.75f) / 0.25f, 0, 1) * 0.85f; // 결정적 순간엔 거의 비운다
        float l = 0, r = 0;
        for (int i = 0; i < Pads; i++)
        {
            // 두 오실레이터를 ±4센트 어긋나게 (좌우로 나눠 넓게)
            float a = 0, b = 0;
            if (_hzA[i] > 0 && _mix < 1)
            {
                _phA[i * 2] += _hzA[i] * 1.0023 / Dsp.Rate; _phA[i * 2 + 1] += _hzA[i] * 0.9977 / Dsp.Rate;
                a = (1 - _mix);
            }
            if (_hzB[i] > 0)
            {
                _phB[i * 2] += _hzB[i] * 1.0023 / Dsp.Rate; _phB[i * 2 + 1] += _hzB[i] * 0.9977 / Dsp.Rate;
                b = _mix;
            }
            float w = i == 0 ? 0.9f : 0.6f;                       // 바닥 음은 조금 크게
            l += w * (a * Tri(_phA[i * 2]) + b * Tri(_phB[i * 2]));
            r += w * (a * Tri(_phA[i * 2 + 1]) + b * Tri(_phB[i * 2 + 1]));
        }
        float cut = 450 + 1600 * _t;
        Cut = cut; Pad = pad;
        l = _lpL2.Low(_lpL1.Low(l, cut), cut) * 0.06f * pad;
        r = _lpR2.Low(_lpR1.Low(r, cut), cut) * 0.06f * pad;
        // 심장 박동: 조짐부터 (쿵 · 0.25초 뒤 작게 쿵)
        if (_t > 0.2f && _n >= _nextBeat)
        {
            float bpm = 50 + 70 * Math.Clamp((_t - 0.2f) / 0.6f, 0, 1);
            _thump = new Mode(55, 0.45f, 0.3f * Math.Min(1, (_t - 0.2f) * 3));
            _thump2At = _n + (long)(0.25 * Dsp.Rate);
            _nextBeat = _n + (long)(60.0 / bpm * Dsp.Rate);
        }
        if (_n == _thump2At) _thump2 = new Mode(52, 0.35f, 0.2f * Math.Min(1, (_t - 0.2f) * 3));
        float beat = _sub.Low(_thump.Next() + _thump2.Next(), 160);
        // 맑은 음: 조용할 때만 · 2초마다 30%
        if (_n >= _nextSpark)
        {
            _nextSpark = _n + 2 * Dsp.Rate;
            if (_t < 0.2f && _rng.Chance(0.3f)) _spark = new Mode(Hz(Sparkle[(int)((_rng.White() + 1) * 0.5f * Sparkle.Length) % Sparkle.Length]), 2.2f, 0.035f);
        }
        float sp = _spark.Next();
        // 결정적 순간: 낮은 D · A 지속음 (0.3Hz 맥놀이)
        float drone = 0;
        float dr = Math.Clamp((_t - 0.8f) / 0.2f, 0, 1);
        if (dr > 0)
        {
            _droneA += 73.42 / Dsp.Rate; _droneB += 110.3 / Dsp.Rate;
            drone = dr * 0.05f * (MathF.Sin((float)(_droneA * Dsp.Tau)) + 0.6f * MathF.Sin((float)(_droneB * Dsp.Tau)));
        }
        return (l + beat + sp * 1.1f + drone, r + beat + sp * 0.9f + drone);
    }
}
