namespace ShipView.Audio;

/// <summary>
/// 바탕 음악 (docs/ENGINE.md §소리 엔진 · 바퀴 1) — 배 안 소리와 따로 도는 조용한 소리 바탕. 연출의 분위기를 따라 저절로 바뀐다.
/// <para><b>조용함</b>: 느린 패드 화음 (D 도리안 · 8초마다 바뀌고 2초에 걸쳐 넘어간다) · 가끔 맑은 음 하나 (D 단조 5음계).</para>
/// <para><b>조짐</b>: 화음이 어두워지고 (♭9 · 감화음) · 낮은 심장 박동 (55Hz 두 번 — 쿵쿵) 이 생긴다.</para>
/// <para><b>위험</b>: 박동이 빨라지고 (분당 50 → 120) · 거름이 열려 밝고 날카로워진다 · 화음이 4초마다.</para>
/// <para><b>결정적 순간</b>: 패드를 거의 비우고 낮은 D · A 지속음만 (두 음이 천천히 맥놀이) — 효과음이 주인공이 되게.</para>
/// 긴장(0~1)은 4초에 걸쳐 따라가 갑자기 바뀌지 않는다. 씨앗이 같으면 같은 음악 (확인 · 시험).
/// <para><b>따라가는 것 (소리 단계 S5 · 결정 11)</b>: 하루 시간 — 밤(22~5시)은 화음이 1.5배 느리고 · 어둡고(거름 −150Hz) · 맑은 음이 드물다 · 아침(6~10시)은 맑은 음이 잦다.
/// 항해 달력 — 파편 구간 사흘 앞부터 불안(어두운 화음 · 먼 박동) · 지나온 뒤 하루는 안도(밝은 화음). 배의 행사 — 추모 모임은 애가(낮은 지속음 · 느린 화음 · 맑은 음 없음) ·
/// 축하 자리는 밝은 화음 · 화음 음을 8분으로 퉁긴다. 고른 사람의 기분 — 가라앉으면 애가 쪽 · 기쁘면 밝은 쪽 · 들뜨면(긴장 · 겁) 거름이 열린다.
/// 위험(연출의 긴장)이 늘 먼저다 — 축하 중에도 위험이면 어두운 화음.</para>
/// </summary>
public struct MusicCue
{
    public bool Set;
    public float Hour;                 // 배의 시계 (0~24)
    public float Unease, Relief;       // 항해 달력: 파편 구간 앞 · 지나온 뒤 (0~1)
    public float Mourn, Festive;       // 배의 행사: 추모 · 축하 (0~1)
    public float Valence, Arousal;     // 고른 사람의 기분: −1 가라앉음 ~ 1 기쁨 · 0 차분 ~ 1 들뜸
}

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
    private static readonly int[][] Bright =
    {
        new[] { 50, 57, 61, 64, 66 },   // Dmaj9
        new[] { 43, 50, 54, 57, 59 },   // Gmaj9
        new[] { 47, 54, 57, 61, 62 },   // Bm9
        new[] { 45, 52, 54, 59, 61 },   // A6/9
    };
    private static readonly int[][] Lament =
    {
        new[] { 38, 50, 53, 57, 64 },   // Dm(add9) — 낮은 D
        new[] { 38, 50, 55, 58, 62 },   // Gm/D
        new[] { 46, 50, 53, 57, 62 },   // B♭maj7
        new[] { 45, 52, 55, 61, 64 },   // A7
    };
    private static readonly int[] Sparkle = { 62, 65, 67, 69, 72, 74 }, SparkleMaj = { 62, 64, 66, 69, 71, 74 };
    /// <summary>화음 갈래: 0 잔잔 · 1 어두움 (위험 · 불안) · 2 밝음 (축하 · 안도 · 기쁨) · 3 애가 (추모 · 슬픔).</summary>
    public const int SetCalm = 0, SetDark = 1, SetBright = 2, SetLament = 3;

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
    private MusicCue _cue;
    private float _moodTarget;
    private long _nextArp;
    private Mode _arp;
    private int _arpK;
    private double _celloPh;
    private float _cello;

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
    /// <summary>지금 화음의 길이 (초) · 맑은 음 확률 (2초마다) · 바탕 음악이 따라가는 것 (확인 · 시험).</summary>
    public float ChordSec { get; private set; } = 8;
    public float SparkChance { get; private set; } = 0.3f;
    public MusicCue Cue => _cue;

    /// <summary>바탕 음악이 따라갈 것 (하루 시간 · 항해 달력 · 행사 · 고른 사람의 기분).</summary>
    public void SetCue(in MusicCue cue) { _cue = cue; _target = Math.Max(_moodTarget, 0.22f * cue.Unease); }

    /// <summary>밤 (0 낮 ~ 1 한밤): 20시부터 23시까지 짙어지고 · 5시부터 7시까지 걷힌다.</summary>
    public static float Night(float hour) => hour >= 23 || hour < 5 ? 1 : hour >= 20 ? (hour - 20) / 3f : hour < 7 ? 1 - (hour - 5) / 2f : 0;
    private float NightNow => _cue.Set ? Night(_cue.Hour) : 0;
    private bool Morning => _cue.Set && _cue.Hour >= 6 && _cue.Hour < 10;

    /// <summary>연출의 분위기 → 바라는 긴장.</summary>
    public void SetMood(string mood)
    {
        _moodTarget = mood switch
        {
            "조짐" => 0.35f,
            "위험" => 0.7f,
            "결정적 순간" => 1f,
            _ => 0f,
        };
        _target = Math.Max(_moodTarget, 0.22f * _cue.Unease);
    }

    /// <summary>이번 화음 갈래: 위험이 먼저 · 그다음 추모 · 슬픔 → 애가 · 축하 · 안도 · 기쁨 → 밝음 · 불안 → 어두움 · 나머지 잔잔.</summary>
    private int PickSet()
    {
        if (_t > 0.25f) return SetDark;
        if (_cue.Mourn > 0.5f || _cue.Valence < -0.45f) return SetLament;
        if (_cue.Festive > 0.5f || _cue.Relief > 0.4f || _cue.Valence > 0.45f) return SetBright;
        if (_cue.Unease > 0.5f) return SetDark;
        return SetCalm;
    }

    private static float Hz(int midi) => 440f * MathF.Pow(2f, (midi - 69) / 12f);

    private void SetChord(int set, int k, bool instant = false)
    {
        var src = set switch { SetDark => Dark, SetBright => Bright, SetLament => Lament, _ => Calm };
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
        float night = NightNow;
        if (_n >= _nextChord)
        {
            int set = PickSet();
            if (_t < 0.9f) SetChord(set, set == _set ? _chord + 1 : 0);
            double sec = (8 - 4 * Math.Min(1, _t / 0.7)) * (1 + 0.5 * night) * (_set == SetLament ? 1.5 : 1) * (_set == SetBright && _cue.Festive > 0.5f ? 0.6 : 1);
            ChordSec = (float)sec;
            _nextChord = _n + (long)(sec * Dsp.Rate);
        }
        _mix = Math.Min(1, _mix + 1f / (2f * Dsp.Rate));          // 2초에 걸쳐 넘어간다
        float pad = (1 - Math.Clamp((_t - 0.75f) / 0.25f, 0, 1) * 0.85f) * (1 - 0.25f * night); // 결정적 순간엔 거의 비운다 · 밤엔 조금 작게
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
        float cut = 450 + 1600 * _t - 150 * night + (_set == SetBright ? 250 : 0) + 150 * _cue.Arousal;
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
        // 맑은 음: 조용할 때만 · 2초마다 30% (밤엔 드물고 · 아침 · 밝은 화음엔 잦고 · 애가엔 없다)
        SparkChance = _set == SetLament ? 0 : 0.3f * (1 - 0.6f * night) * (Morning ? 1.5f : 1) * (_set == SetBright ? 1.6f : 1);
        if (_n >= _nextSpark)
        {
            _nextSpark = _n + 2 * Dsp.Rate;
            var sc = _set == SetBright ? SparkleMaj : Sparkle;
            if (_t < 0.2f && _rng.Chance(SparkChance)) _spark = new Mode(Hz(sc[(int)((_rng.White() + 1) * 0.5f * sc.Length) % sc.Length]), 2.2f, 0.035f);
        }
        float sp = _spark.Next();
        // 축하: 화음 음을 8분으로 퉁긴다 (분당 100)
        if (_cue.Festive > 0.5f && _t < 0.25f && _n >= _nextArp)
        {
            _nextArp = _n + (long)(0.3 * Dsp.Rate);
            float hz = _hzB[(_arpK++ % (Pads - 1)) + 1] * 2;
            if (hz > 0) _arp = new Mode(hz, 0.7f, 0.022f);
        }
        float arp = _arp.Next();
        // 애가: 낮은 지속음 (화음의 바닥 한 옥타브 아래 · 5Hz 떨림) — 추모 모임 · 슬픔
        _cello += ((_set == SetLament ? 1f : 0f) - _cello) * (1f / (3f * Dsp.Rate));
        float cello = 0;
        if (_cello > 0.001f)
        {
            _celloPh += _hzB[0] * 0.5 * (1 + 0.004 * Math.Sin(_n * Dsp.Tau * 5.0 / Dsp.Rate)) / Dsp.Rate;
            cello = _cello * 0.045f * (MathF.Sin((float)(_celloPh * Dsp.Tau)) + 0.3f * MathF.Sin((float)(_celloPh * 2 * Dsp.Tau)));
        }
        // 결정적 순간: 낮은 D · A 지속음 (0.3Hz 맥놀이)
        float drone = 0;
        float dr = Math.Clamp((_t - 0.8f) / 0.2f, 0, 1);
        if (dr > 0)
        {
            _droneA += 73.42 / Dsp.Rate; _droneB += 110.3 / Dsp.Rate;
            drone = dr * 0.05f * (MathF.Sin((float)(_droneA * Dsp.Tau)) + 0.6f * MathF.Sin((float)(_droneB * Dsp.Tau)));
        }
        return (l + beat + sp * 1.1f + drone + arp * 0.8f + cello, r + beat + sp * 0.9f + drone + arp * 1.2f + cello);
    }
}
