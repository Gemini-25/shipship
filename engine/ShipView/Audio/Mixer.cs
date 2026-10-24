namespace ShipView.Audio;

/// <summary>
/// 섞개: 귀가 보낸 소리 목록을 받아 44,100Hz 스테레오로 그린다 (Godot 메인 스레드 · 실험실은 파일로).
/// 소리마다: 합성 → 가림 거름(한 극 둘 — 벽 너머는 가파르게 먹먹) → 크기(튀지 않게 따라감) → 좌우 → 마른 소리 + 방 울림으로 보내기.
/// 마지막에 누름(꼭대기를 따라가 넘치는 만큼만 · 바로 누르고 0.3초에 풀린다)과 부드러운 끝 — 넓은 폭(함장 결정 17): 평소 소리는 건드리지 않고 폭발만 누른다. 이어 도는 소리는 열쇠로 하나씩 — 이번 목록에 없으면 천천히 줄여 끈다.
/// 바탕 음악(<see cref="Score"/>)은 방 울림 · 좌우 · 가림 없이 위에 얹는다 (배 안의 소리가 아니다).
/// </summary>
public sealed class Mixer
{
    private sealed class Slot
    {
        public Voice V = null!;
        public OnePole Lp1, Lp2;
        public float Gain, Cutoff = 16000, Pan;
        public float TGain, TCutoff = 16000, TPan;
        public bool Seen;
    }

    private readonly List<Slot> _shots = new();
    private readonly Dictionary<long, Slot> _loops = new();
    private readonly Reverb _rev = new();
    private float _wet = 0.25f, _tWet = 0.25f;
    private uint _seed = 7;
    public float Master = 0.9f;
    /// <summary>바탕 음악 (연출의 분위기를 따른다) · 크기 (0이면 끈다). 효과음이 크면 음악은 잠깐 작아진다.</summary>
    public readonly Score Music = new();
    public float MusicGain = MusicLevel;
    /// <summary>음악의 보통 크기 (넓은 폭에 맞춰 — 효과음이 작아진 만큼 함께 낮췄다).</summary>
    public const float MusicLevel = 0.45f;
    /// <summary>마지막 누름: 넘지 않을 꼭대기 · 풀리는 빠르기 (0.3초) · 지금 따라가는 꼭대기.</summary>
    public const float Ceiling = 0.89f;
    private static readonly float Release = 1f - MathF.Exp(-1f / (0.3f * Dsp.Rate));
    private float _peak;
    /// <summary>확인용: 지금까지 누른 가장 큰 몫 (dB).</summary>
    public float MaxReduction { get; private set; }
    private float _sfxEnv;
    public int Voices => _shots.Count + _loops.Count;

    /// <summary>귀가 보낸 것을 모두 받는다 (그리기 전에).</summary>
    public void Take(Acoustics ears)
    {
        while (ears.Out.TryDequeue(out var f)) Apply(f);
    }

    public void Apply(SoundFrame f)
    {
        if (f.Reset) { foreach (var s in _loops.Values) s.TGain = 0; }
        _rev.SetRt60(f.Rt60); _tWet = f.Wet;
        if (f.Mood.Length > 0) Music.SetMood(f.Mood);
        if (f.Cue.Set) Music.SetCue(f.Cue);
        foreach (var v in f.Shots)
        {
            if (_shots.Count > 24) break;
            _shots.Add(new Slot { V = v, Gain = v.Gain, TGain = v.Gain, Cutoff = v.Cutoff, TCutoff = v.Cutoff, Pan = v.Pan, TPan = v.Pan });
        }
        foreach (var s in _loops.Values) s.Seen = false;
        foreach (var u in f.Loops)
        {
            if (!_loops.TryGetValue(u.Key, out var s))
            {
                s = new Slot { V = Make(u), Cutoff = u.Cutoff, Pan = u.Pan };
                _loops[u.Key] = s;
            }
            s.Seen = true; s.TGain = u.Gain; s.TCutoff = u.Cutoff; s.TPan = u.Pan; s.V.Send = u.Send;
            switch (s.V)
            {
                case Jet j: j.Set(u.A, u.B); break;
                case Fire fi: fi.Set(u.A); break;
                case Machine m: m.Set(u.A, u.B); break;
                case RoomTone r: r.Set(u.A); break;
                case Breath b: b.Set(u.A, u.B); break;
                case Ring g: g.Set(u.A); break;
                case Tune tu: tu.Set(u.B); break;
                case Wind wi: wi.Set(u.A, u.B); break;
                case Spray sp: sp.Set(u.A, u.B); break;
            }
        }
        foreach (var s in _loops.Values)
            if (!s.Seen)
            {
                s.TGain = 0;
                switch (s.V) { case Jet j: j.Stop(); break; case Fire fi: fi.Set(0); break; case Machine m: m.Set(0); break; case Spray sp: sp.Stop(); break; }
            }
    }

    private Voice Make(LoopUpdate u)
    {
        uint seed = _seed = _seed * 1664525u + 1013904223u;
        return u.Kind switch
        {
            LoopKind.Jet => new Jet(seed),
            LoopKind.Fire => new Fire(seed),
            LoopKind.Machine => new Machine(u.Role ?? "", seed),
            LoopKind.Breath => new Breath(seed),
            LoopKind.Ring => new Ring(seed),
            LoopKind.Tune => new Tune((uint)u.Key, (int)u.A),   // 사람마다 같은 곡 (열쇠 = 사람)
            LoopKind.Wind => new Wind(seed),
            LoopKind.Water => new Spray(seed),
            _ => new RoomTone(seed),
        };
    }

    /// <summary>frames개 (왼 · 오 번갈아) 그린다.</summary>
    public void Render(Span<float> stereo, int frames)
    {
        stereo[..(frames * 2)].Clear();
        const float follow = 0.0015f * Dsp.K;
        for (int i = 0; i < frames; i++)
        {
            _wet += (_tWet - _wet) * 0.0002f * Dsp.K;
            float l = 0, r = 0, send = 0;
            for (int k = _shots.Count - 1; k >= 0; k--)
            {
                var s = _shots[k];
                float x = Run(s, follow);
                if (s.V.Done) { _shots.RemoveAt(k); continue; }
                var (gl, gr) = Pan.Gains(s.Pan);
                l += x * gl; r += x * gr; send += x * s.V.Send;
            }
            foreach (var s in _loops.Values)
            {
                float x = Run(s, follow);
                var (gl, gr) = Pan.Gains(s.Pan);
                l += x * gl; r += x * gr; send += x * s.V.Send;
            }
            float wet = _rev.Run(send) * _wet * 2.2f;
            // 음악: 효과음 크기를 따라가며 (빨리 올라 · 천천히 내려) 그만큼 줄인다
            float sfx = MathF.Max(MathF.Abs(l), MathF.Abs(r));
            _sfxEnv += (sfx - _sfxEnv) * (sfx > _sfxEnv ? 0.01f : 0.00005f) * Dsp.K;
            float ml = 0, mr = 0;
            if (MusicGain > 0) { (ml, mr) = Music.Next(); float g = MusicGain / (1 + 6 * _sfxEnv); ml *= g; mr *= g; }
            float outL = (l + wet + ml) * Master, outR = (r + wet * 0.92f + mr) * Master;
            float pk = MathF.Max(MathF.Abs(outL), MathF.Abs(outR));
            _peak = pk > _peak ? pk : _peak + (pk - _peak) * Release;
            if (_peak > Ceiling) { float g = Ceiling / _peak; outL *= g; outR *= g; MaxReduction = MathF.Max(MaxReduction, -20 * MathF.Log10(g)); }
            stereo[2 * i] = Soft(outL);
            stereo[2 * i + 1] = Soft(outR);
        }
        // 다 그친 이어 도는 소리를 치운다
        if (_loops.Count > 0)
            foreach (var key in _loops.Where(kv => kv.Value.V.Done || (!kv.Value.Seen && kv.Value.Gain < 1e-4f)).Select(kv => kv.Key).ToList()) _loops.Remove(key);
    }

    private static float Run(Slot s, float follow)
    {
        if (s.V.Delay > 0) { s.V.Delay--; return 0; }      // 늦게 닿는 소리: 아직 길 위에 있다
        s.Gain += (s.TGain - s.Gain) * follow;
        s.Cutoff += (s.TCutoff - s.Cutoff) * follow;
        s.Pan += (s.TPan - s.Pan) * follow;
        float x = s.V.Next();
        if (s.Cutoff < 15000) x = s.Lp2.Low(s.Lp1.Low(x, s.Cutoff), s.Cutoff);
        return x * s.Gain;
    }

    /// <summary>부드러운 끝: 0.85 아래는 그대로 · 위는 1을 넘지 않게 둥글게.</summary>
    private static float Soft(float x)
    {
        float a = MathF.Abs(x);
        return a <= 0.85f ? x : MathF.Sign(x) * (0.85f + 0.15f * MathF.Tanh((a - 0.85f) / 0.15f));
    }

    /// <summary>16비트 작은 끝 WAV (스테레오 · 44,100Hz) — 실험실이 장면 소리를 파일로 남긴다.</summary>
    public static void WriteWav(string path, ReadOnlySpan<float> stereo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        int n = stereo.Length;
        bw.Write("RIFF"u8); bw.Write(36 + n * 2); bw.Write("WAVE"u8);
        bw.Write("fmt "u8); bw.Write(16); bw.Write((short)1); bw.Write((short)2); bw.Write(Dsp.Rate); bw.Write(Dsp.Rate * 4); bw.Write((short)4); bw.Write((short)16);
        bw.Write("data"u8); bw.Write(n * 2);
        foreach (float v in stereo) bw.Write((short)Math.Clamp((int)(v * 32000), short.MinValue, short.MaxValue));
    }
}
