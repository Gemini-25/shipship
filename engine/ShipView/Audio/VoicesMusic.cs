namespace ShipView.Audio;

// 배 안에서 나는 음악 (소리 단계 S5 — docs/SOUND.md 결정 12): 쉬는 이가 악기를 치거나 작은 스피커로 튼 음악.
// 세계가 낸 소리(③ 소리 장 — 벽 · 문 너머는 먹먹하게)를 그 사람의 기분으로 연주한다 — 기쁘면 장조 · 빠르게, 가라앉으면 단조 · 느리게.
// 씨앗(사람)이 같으면 같은 곡.

/// <summary>
/// 기타 (카플러스-스트롱 줄): 잡음 한 줌을 줄 길이(표본 = 표본율 / 음높이)의 지연선에 넣고 · 두 표본 평균으로 되먹이면 높은 음부터 잦아드는 퉁긴 줄이 된다.
/// 손가락 퉁김 (트래비스 — 베이스 · 3 · 2 · 3 · 5도 베이스 · 3 · 2 · 3) · 한 마디에 화음 하나 · 장조 I–V–vi–IV / 단조 i–VI–III–VII.
/// </summary>
public sealed class Guitar
{
    private sealed class Str
    {
        public float[] Buf = new float[Dsp.Rate / 30 + 2], Tmp = new float[Dsp.Rate / 30 + 2];
        public int Len, I;
        public float G, Last, Bright;
        /// <summary>퉁긴다: 손끝이 닿은 모양 (잡음을 두 번 거른 둥근 덩어리 — bright Hz 위가 없다) 을 아직 우는 줄 위에 얹는다.</summary>
        public void Pluck(float hz, float amp, float t60, float bright, ref Noise n)
        {
            Len = Math.Clamp((int)(Dsp.Rate / hz), 2, Buf.Length - 1);
            G = MathF.Pow(0.001f, 1f / (t60 * hz));   // 한 바퀴마다 (T60 동안 −60dB)
            Bright = bright;
            float a = 1f - MathF.Exp(-Dsp.Tau * bright / Dsp.Rate), lp1 = 0, lp2 = 0, mean = 0;
            var tmp = Tmp;   // 소리 스레드에서 새로 잡지 않는다
            for (int k = 0; k < Len * 3; k++) { lp1 += (n.White() - lp1) * a; lp2 += (lp1 - lp2) * a; if (k >= Len * 2) tmp[k - Len * 2] = lp2; }
            for (int k = 0; k < Len; k++) mean += tmp[k];
            mean /= Len;
            float peak = 1e-6f;
            for (int k = 0; k < Len; k++) peak = Math.Max(peak, MathF.Abs(tmp[k] - mean));
            for (int k = 0; k < Len; k++) Buf[k] = Buf[k] * 0.3f + amp * (tmp[k] - mean) / peak;   // 직류를 빼고 크기를 맞춘다
            I = 0;
        }
        public float Next()
        {
            if (Len == 0) return 0;
            int j = (I + 1) % Len;
            float y = Buf[I];
            Buf[I] = G * 0.5f * (y + Buf[j]);
            I = j;
            return y;
        }
    }

    private static readonly int[] Major = { 0, 7, 9, 5 }, Minor = { 0, 8, 3, 10 };   // 마디마다 뿌리음 (근음에서 반음)
    private readonly Str[] _s = { new(), new(), new(), new() };
    private Noise _n;
    private readonly int _root;
    private int _i, _next, _eighth, _bar;
    private float _mood, _eighthLen;
    private bool _minor;
    private OnePole _body, _hp, _top;

    public Guitar(uint seed)
    {
        _n = new Noise(seed * 2654435761u + 7);
        _root = new[] { 40, 43, 45, 38 }[(seed * 2654435761u >> 16) % 4];   // E2 · G2 · A2 · D2 (사람마다)
        _next = 0;
    }

    /// <summary>치는 이의 기분 (−1 가라앉음 ~ 1 기쁨).</summary>
    public void Set(float mood) => _mood = mood;

    private static float Hz(int midi) => 440f * MathF.Pow(2f, (midi - 69) / 12f);

    public float Next()
    {
        if (_i >= _next)
        {
            if (_eighth % 8 == 0)
            {
                _minor = _mood < -0.1f;   // 마디 첫머리에서만 장 · 단을 바꾼다
                _eighthLen = 60f / (72 + 18 * Math.Clamp(_mood, -1, 1)) / 2 * Dsp.Rate;
                _bar++;
            }
            int deg = (_minor ? Minor : Major)[(_bar - 1) % 4];
            int r = _root + deg + (deg >= 7 ? -12 : 0);
            int q = !_minor && deg == 9 || _minor && deg == 0 ? 3 : 4;   // 3도: 장조의 vi · 단조의 i만 단3도
            int[] chord = { r, r + 7, r + 12, r + 12 + q, r + 19 };   // 뿌리 · 5도 · 옥타브 · 3도 · 5도 위
            int pos = _eighth % 8;
            float vel = 0.5f + 0.1f * _n.White();
            switch (pos)
            {
                case 0: _s[0].Pluck(Hz(chord[0]), 0.9f * vel, 3.0f, 1200, ref _n); break;
                case 4: _s[0].Pluck(Hz(chord[1]), 0.8f * vel, 3.0f, 1200, ref _n); break;
                case 1: case 3: case 5: case 7: _s[2].Pluck(Hz(chord[4]), 0.55f * vel, 1.6f, 2600, ref _n); break;
                case 2: case 6: _s[1].Pluck(Hz(chord[3]), 0.6f * vel, 2.0f, 2000, ref _n); break;
            }
            if (pos == 0 && _bar % 2 == 0) _s[3].Pluck(Hz(chord[2] + (_minor ? 3 : 4) + 12), 0.35f * vel, 1.4f, 3200, ref _n);   // 두 마디마다 높은 장식음
            _eighth++;
            _next = _i + (int)(_eighthLen * (1 + 0.02f * _n.White()));   // 사람 손 — 조금씩 어긋난다
        }
        _i++;
        float x = _s[0].Next() + _s[1].Next() + _s[2].Next() + _s[3].Next();
        x = _hp.High(x, 70);
        x += 0.35f * _body.Low(x, 220);   // 몸통 울림 (낮은 음을 조금 북돋운다)
        x = _top.Low(x, 5000);            // 나무 앞판이 높은 음을 먹는다
        return x * 1.66f;   // 크기 기준 (가장 센 0.1초 RMS 0.25 — Lab --voices)
    }
}

/// <summary>
/// 작은 스피커로 트는 노래: 화음 바탕(세모파) · 5음계 가락(부드러운 네모파 — 쉼이 섞인다) · 낮은 북 · 찰랑이(8분)를
/// 작은 스피커의 띠(300Hz ~ 5kHz · 살짝 눌림)로 — 64마디마다 다음 곡 (조 · 빠르기가 바뀐다).
/// </summary>
public sealed class Speaker
{
    private static readonly int[] Major = { 0, 7, 9, 5 }, Minor = { 0, 8, 3, 10 };
    private static readonly int[] PentaMaj = { 0, 2, 4, 7, 9, 12, 14, 16 }, PentaMin = { 0, 3, 5, 7, 10, 12, 15, 17 };
    private Noise _n;
    private int _i, _next, _step, _key, _note;
    private float _mood, _stepLen, _bpm;
    private bool _minor;
    private readonly double[] _padPh = new double[3];
    private readonly float[] _padHz = new float[3];
    private double _melPh;
    private float _melHz, _melEnv, _kick, _kickPh, _hat;
    private OnePole _hp1, _hp2, _lp1, _lp2, _hatHp, _melLp;

    public Speaker(uint seed)
    {
        _n = new Noise(seed * 2246822519u + 3);
        NewSong();
    }

    public void Set(float mood) => _mood = mood;

    private void NewSong()
    {
        _key = 48 + (int)((_n.White() + 1) * 3.5f);   // C3 ~ F#3
        _bpm = 84 + 16 * (_n.White() + 1) * 0.5f;
        _note = 3;
    }

    private static float Hz(int midi) => 440f * MathF.Pow(2f, (midi - 69) / 12f);
    private static float Tri(double ph) { double x = ph - Math.Floor(ph); return (float)(x < 0.5 ? 4 * x - 1 : 3 - 4 * x); }

    public float Next()
    {
        if (_i >= _next)
        {
            int bar = _step / 8, pos = _step % 8;
            if (pos == 0)
            {
                if (bar > 0 && bar % 64 == 0) NewSong();
                _minor = _mood < -0.1f;
                _stepLen = 60f / (_bpm + 10 * Math.Clamp(_mood, -1, 1)) / 2 * Dsp.Rate;
                int deg = (_minor ? Minor : Major)[bar % 4];
                int r = _key + deg - (deg >= 7 ? 12 : 0);
                int q = !_minor && deg == 9 || _minor && deg == 0 ? 3 : 4;
                _padHz[0] = Hz(r); _padHz[1] = Hz(r + q); _padHz[2] = Hz(r + 7);
            }
            if (pos is 0 or 4) _kick = 1;
            _hat = pos % 2 == 1 ? 0.6f : 0.35f;
            // 가락: 이웃 음으로 걷는다 · 넷에 하나는 쉰다
            if (_n.Chance(0.75f))
            {
                _note = Math.Clamp(_note + (int)MathF.Round(_n.White() * 2), 0, 7);
                _melHz = Hz(_key + 12 + (_minor ? PentaMin : PentaMaj)[_note]);
                _melEnv = 1;
            }
            _step++;
            _next = _i + (int)_stepLen;
        }
        _i++;
        float pad = 0;
        for (int k = 0; k < 3; k++) { _padPh[k] += _padHz[k] / Dsp.Rate; pad += Tri(_padPh[k]); }
        _melPh += _melHz / Dsp.Rate;
        float sq = _melPh - Math.Floor(_melPh) < 0.5 ? 1 : -1;
        float mel = _melLp.Low(sq, 1800) * _melEnv;
        _melEnv *= MathF.Pow(0.001f, 1f / (0.5f * Dsp.Rate));
        _kickPh += (55 + 90 * _kick) / Dsp.Rate;
        float kick = MathF.Sin(_kickPh * Dsp.Tau) * _kick;
        _kick *= MathF.Pow(0.001f, 1f / (0.18f * Dsp.Rate));
        float hat = _hatHp.High(_n.White(), 7000) * _hat;
        _hat *= MathF.Pow(0.001f, 1f / (0.04f * Dsp.Rate));
        float x = 0.22f * pad + 0.35f * mel + 0.9f * kick + 0.25f * hat;
        // 작은 스피커: 300Hz 아래 · 5kHz 위가 없고 · 살짝 눌린다
        x = _hp2.High(_hp1.High(x, 300), 300);
        x = _lp2.Low(_lp1.Low(x, 5000), 5000);
        return MathF.Tanh(x * 1.6f) * 1.12f;   // 크기 기준 (가장 센 0.1초 RMS 0.25)
    }
}

/// <summary>배 안 음악 하나 (이어 도는 소리): 악기(기타) 또는 작은 스피커 — 치는 이의 기분을 따라간다.</summary>
public sealed class Tune : Voice
{
    private readonly Guitar? _g;
    private readonly Speaker? _s;
    public Tune(uint seed, int kind)
    {
        if (kind == 2) _g = new Guitar(seed); else _s = new Speaker(seed);
        Send = 0.3f;
    }
    public void Set(float mood) { _g?.Set(mood); _s?.Set(mood); }
    public override float Next() => _g != null ? _g.Next() : _s!.Next();
}
