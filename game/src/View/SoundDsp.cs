using System;
using System.Collections.Generic;
using Godot;

namespace ShipSim.View;

/// <summary>
/// v23 효과음 묶음: 소리 길 셋 — 효과음(Sfx) · 환경음(Amb) · 바깥(Outside → 환경음).
///   효과음 크기 · 환경음 크기는 설정에서 따로 고른다. 바깥 길에는 낮은 소리만 남기는 거름이 있어
///   멀리서 배 전체를 보면 바람 · 비가 그대로, 가까이 들어가면 선체 너머로 먹먹하게 들린다.
/// </summary>
internal static class Buses
{
    public const string Sfx = "Sfx", Amb = "Amb", Outside = "Outside";

    public static void Ensure()
    {
        Make(Sfx, "Master");
        Make(Amb, "Master");
        int o = Make(Outside, Amb);
        if (AudioServer.GetBusEffectCount(o) == 0) AudioServer.AddBusEffect(o, new AudioEffectLowPassFilter { CutoffHz = 16000f, Resonance = 0.5f });
    }

    private static int Make(string name, string send)
    {
        int i = AudioServer.GetBusIndex(name);
        if (i < 0)
        {
            AudioServer.AddBus();
            i = AudioServer.BusCount - 1;
            AudioServer.SetBusName(i, name);
        }
        AudioServer.SetBusSend(i, send);
        return i;
    }

    public static void Volume(string bus, float linear)
    {
        int i = AudioServer.GetBusIndex(bus);
        if (i >= 0) AudioServer.SetBusVolumeDb(i, Mathf.LinearToDb(Mathf.Max(0.0001f, linear)));
    }

    public static void VolumeDb(string bus, float db)
    {
        int i = AudioServer.GetBusIndex(bus);
        if (i >= 0) AudioServer.SetBusVolumeDb(i, db);
    }

    public static void Lowpass(float hz)
    {
        int o = AudioServer.GetBusIndex(Outside);
        if (o >= 0 && AudioServer.GetBusEffectCount(o) > 0 && AudioServer.GetBusEffect(o, 0) is AudioEffectLowPassFilter f) f.CutoffHz = hz;
    }

    public static float Cutoff()
    {
        int o = AudioServer.GetBusIndex(Outside);
        return o >= 0 && AudioServer.GetBusEffectCount(o) > 0 && AudioServer.GetBusEffect(o, 0) is AudioEffectLowPassFilter f ? f.CutoffHz : 0f;
    }
}

/// <summary>파형을 만드는 작은 도구 (22kHz 모노) — 잡음 · 거름 · 이음새 없는 반복 · 파일로 꺼내 보기.</summary>
internal static class Dsp
{
    public const int Rate = 22050;
    public const float Tau = MathF.PI * 2f;

    /// <summary>--sounddump=폴더: 만든 소리를 wav로 꺼내 둔다 (확인용).</summary>
    public static string? DumpDir;

    public static float[] Buf(float seconds) => new float[(int)(Rate * seconds)];

    public sealed class Noise
    {
        private readonly Random _r;
        private float _b, _p0, _p1, _p2;
        public Noise(int seed) => _r = new Random(seed);
        public float White() => (float)(_r.NextDouble() * 2.0 - 1.0);
        public float U() => (float)_r.NextDouble();
        public float Range(float a, float b) => a + (b - a) * U();
        public float Brown() { _b = Math.Clamp(_b + 0.02f * White(), -1f, 1f); return _b; }
        public float Pink()
        {
            float w = White();
            _p0 = 0.99765f * _p0 + w * 0.0990460f;
            _p1 = 0.96300f * _p1 + w * 0.2965164f;
            _p2 = 0.57000f * _p2 + w * 1.0526913f;
            return (_p0 + _p1 + _p2 + w * 0.1848f) * 0.25f;
        }
    }

    /// <summary>v23 부드럽게 떠도는 값 (-1~1): 바람 세기 · 천둥 구름 · 땅울림처럼 사인보다 덜 규칙적인 흔들림.</summary>
    public sealed class Wander
    {
        private readonly Random _r;
        private readonly float _step;
        private float _a, _b, _k = 1f;
        public Wander(int seed, float hz) { _r = new Random(seed); _step = hz / Rate; _a = Pick(); _b = Pick(); }
        private float Pick() => (float)(_r.NextDouble() * 2.0 - 1.0);
        public float Next()
        {
            _k += _step;
            if (_k >= 1f) { _k -= 1f; _a = _b; _b = Pick(); }
            float e = 0.5f - 0.5f * MathF.Cos(MathF.PI * _k);
            return _a + (_b - _a) * e;
        }
    }

    /// <summary>v23 짧은 잔향 (빗방울 · 천둥 · 도마가 방이나 하늘에 퍼진다) — 되먹임 빗 넷 + 올패스 둘 (슈뢰더). size 0.3(작은 방) ~ 1.5(하늘).</summary>
    public static float[] Reverb(float[] s, float size, float mix, float damp = 0.35f)
    {
        int[] combs = { 1116, 1188, 1277, 1356 };
        int[] alls = { 556, 225 };
        var wet = new float[s.Length];
        foreach (int c0 in combs)
        {
            int d = Math.Max(8, (int)(c0 * size * Rate / 44100f));
            var buf = new float[d];
            float lp = 0f, fb = 0.78f;
            int idx = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float y = buf[idx];
                lp = y * (1f - damp) + lp * damp;
                buf[idx] = s[i] + lp * fb;
                wet[i] += y * 0.25f;
                if (++idx >= d) idx = 0;
            }
        }
        foreach (int a0 in alls)
        {
            int d = Math.Max(4, (int)(a0 * size * Rate / 44100f));
            var buf = new float[d];
            int idx = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float b = buf[idx];
                float y = -wet[i] + b;
                buf[idx] = wet[i] + b * 0.5f;
                wet[i] = y;
                if (++idx >= d) idx = 0;
            }
        }
        var o = new float[s.Length];
        for (int i = 0; i < s.Length; i++) o[i] = s[i] * (1f - mix) + wet[i] * mix;
        return o;
    }

    /// <summary>한 극 거름 (낮은 쪽만).</summary>
    public struct Low
    {
        private float _y;
        public float Run(float x, float hz) { float a = 1f - MathF.Exp(-Tau * hz / Rate); _y += a * (x - _y); return _y; }
    }

    /// <summary>한 극 거름 (높은 쪽만) — 귀에 안 들리는 낮은 떨림을 걷어 낸다.</summary>
    public struct High
    {
        private Low _l;
        public float Run(float x, float hz) => x - _l.Run(x, hz);
    }

    /// <summary>상태 변수 거름: 띠 (q = 감쇠, 작을수록 날카롭다).</summary>
    public struct Band
    {
        private float _lp, _bp;
        public float Run(float x, float hz, float q)
        {
            float f = 2f * MathF.Sin(MathF.PI * MathF.Min(hz, Rate / 6.5f) / Rate);
            _lp += f * _bp;
            float hp = x - _lp - q * _bp;
            _bp += f * hp;
            return _bp;
        }
    }

    /// <summary>끝을 처음에 겹쳐 이음새 없이 돈다 (길이는 겹친 만큼 짧아진다).</summary>
    public static float[] Loop(float[] s, float fadeSeconds = 0.12f)
    {
        int f = Math.Min(s.Length / 3, (int)(Rate * fadeSeconds));
        int n = s.Length - f;
        var o = new float[n];
        Array.Copy(s, o, n);
        for (int i = 0; i < f; i++)
        {
            float k = i / (float)f;
            o[i] = s[i] * k + s[n + i] * (1f - k);
        }
        return o;
    }

    public static float[] Normalize(float[] s, float peak)
    {
        float m = 0f;
        foreach (var x in s) m = MathF.Max(m, MathF.Abs(x));
        if (m > 1e-6f) for (int i = 0; i < s.Length; i++) s[i] *= peak / m;
        return s;
    }

    /// <summary>짧은 소리 하나를 at(초)에 더한다 — env(지난 초) × 파형.</summary>
    public static void Add(float[] s, float at, float length, Func<float, float> f)
    {
        int a = (int)(at * Rate), n = (int)(length * Rate);
        for (int i = 0; i < n && a + i < s.Length; i++) if (a + i >= 0) s[a + i] += f(i / (float)Rate);
    }

    public static AudioStreamWav Wav(string name, float[] s, bool loop = false)
    {
        if (!loop) // 한 번 울리는 소리: 끝을 30ms 줄여 닫는다 (뚝 끊기는 '틱' 없이)
        {
            int f = Math.Min(s.Length / 4, Rate * 3 / 100);
            for (int i = 0; i < f; i++) s[s.Length - 1 - i] *= i / (float)f;
        }
        var bytes = new byte[s.Length * 2];
        for (int i = 0; i < s.Length; i++)
        {
            short v = (short)Math.Clamp((int)(s[i] * 32000f), short.MinValue, short.MaxValue);
            bytes[2 * i] = (byte)(v & 0xff);
            bytes[2 * i + 1] = (byte)((v >> 8) & 0xff);
        }
        if (DumpDir != null) Dump(name, bytes);
        var w = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = bytes };
        if (loop) { w.LoopMode = AudioStreamWav.LoopModeEnum.Forward; w.LoopBegin = 0; w.LoopEnd = s.Length; }
        return w;
    }

    internal static void Dump(string name, byte[] pcm)
    {
        try
        {
            System.IO.Directory.CreateDirectory(DumpDir!);
            using var f = System.IO.File.Create(System.IO.Path.Combine(DumpDir!, name + ".wav"));
            using var b = new System.IO.BinaryWriter(f);
            b.Write("RIFF"u8.ToArray()); b.Write(36 + pcm.Length); b.Write("WAVE"u8.ToArray());
            b.Write("fmt "u8.ToArray()); b.Write(16); b.Write((short)1); b.Write((short)1); b.Write(Rate); b.Write(Rate * 2); b.Write((short)2); b.Write((short)16);
            b.Write("data"u8.ToArray()); b.Write(pcm.Length); b.Write(pcm);
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { GD.PrintErr($"sounddump: {e.Message}"); }
    }

    /// <summary>확대율 → 가까움 0(배 전체) ~ 1(사람 하나).</summary>
    public static float Closeness(float zoom)
    {
        float t = Math.Clamp((zoom - 0.35f) / (1.6f - 0.35f), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    public static readonly float[] Pentatonic = { 1f, 9f / 8f, 5f / 4f, 3f / 2f, 5f / 3f, 2f };

    /// <summary>확인용 기록 (--soundcheck): 최근에 울린 소리 이름.</summary>
    public static readonly List<string> Recent = new();
    public static void Heard(string name) { Recent.Add(name); if (Recent.Count > 12) Recent.RemoveAt(0); }
}
