namespace ShipView.Audio;

/// <summary>
/// 섞개: 귀가 보낸 소리 목록을 받아 22,050Hz 스테레오로 그린다 (Godot 메인 스레드 · 실험실은 파일로).
/// 소리마다: 합성 → 가림 거름(한 극 둘 — 벽 너머는 가파르게 먹먹) → 크기(튀지 않게 따라감) → 좌우 → 마른 소리 + 방 울림으로 보내기.
/// 마지막에 부드러운 누름(tanh)으로 터지지 않게. 이어 도는 소리는 열쇠로 하나씩 — 이번 목록에 없으면 천천히 줄여 끈다.
/// </summary>
public sealed class Mixer
{
    private sealed class Slot
    {
        public Voice V = null!;
        public OnePole Lp1, Lp2;
        public float Gain, Cutoff = 10000, Pan;
        public float TGain, TCutoff = 10000, TPan;
        public bool Seen;
    }

    private readonly List<Slot> _shots = new();
    private readonly Dictionary<long, Slot> _loops = new();
    private readonly Reverb _rev = new();
    private float _wet = 0.25f, _tWet = 0.25f;
    private uint _seed = 7;
    public float Master = 0.9f;
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
                case Machine m: m.Set(u.A); break;
                case RoomTone r: r.Set(u.A); break;
                case Breath b: b.Set(u.A, u.B); break;
            }
        }
        foreach (var s in _loops.Values)
            if (!s.Seen)
            {
                s.TGain = 0;
                switch (s.V) { case Jet j: j.Stop(); break; case Fire fi: fi.Set(0); break; case Machine m: m.Set(0); break; }
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
            _ => new RoomTone(seed),
        };
    }

    /// <summary>frames개 (왼 · 오 번갈아) 그린다.</summary>
    public void Render(Span<float> stereo, int frames)
    {
        stereo[..(frames * 2)].Clear();
        const float follow = 0.0015f;
        for (int i = 0; i < frames; i++)
        {
            _wet += (_tWet - _wet) * 0.0002f;
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
            stereo[2 * i] = Soft((l + wet) * Master);
            stereo[2 * i + 1] = Soft((r + wet * 0.92f) * Master);
        }
        // 다 그친 이어 도는 소리를 치운다
        if (_loops.Count > 0)
            foreach (var key in _loops.Where(kv => kv.Value.V.Done || (!kv.Value.Seen && kv.Value.Gain < 1e-4f)).Select(kv => kv.Key).ToList()) _loops.Remove(key);
    }

    private static float Run(Slot s, float follow)
    {
        s.Gain += (s.TGain - s.Gain) * follow;
        s.Cutoff += (s.TCutoff - s.Cutoff) * follow;
        s.Pan += (s.TPan - s.Pan) * follow;
        float x = s.V.Next();
        if (s.Cutoff < 9500) x = s.Lp2.Low(s.Lp1.Low(x, s.Cutoff), s.Cutoff);
        return x * s.Gain;
    }

    private static float Soft(float x) => MathF.Tanh(x);

    /// <summary>16비트 작은 끝 WAV (스테레오 · 22,050Hz) — 실험실이 장면 소리를 파일로 남긴다.</summary>
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
