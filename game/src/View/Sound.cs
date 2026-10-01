using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v10 소리. 파일 없이 시작할 때 파형을 만들어 쓴다 (22kHz 모노 16비트).
/// 효과음: 치명 경보(사이렌), 경고(삑삑), 운석 충돌(쿵), 원자로 긴급 정지(내려가는 소리).
/// 배경음: 원자로·환기 기계음(출력만큼), 공기 새는 소리(새는 방이 있으면), 불 소리(불 칸 수만큼). 일시정지·불러오는 중엔 조용히.
/// v11.3: 충돌음·새는 소리·불 소리에 자리가 생기고(화면에서 먼 것은 작게, 좌우로), 문·발소리·로봇 구동음 (SoundSpatial.cs).
/// 시뮬레이션은 소리를 모른다 — 화면처럼 월드를 읽기만 한다.
/// </summary>
public partial class SoundSystem : Node
{
    private const int Rate = 22050;

    private Main _main = null!;
    private readonly List<AudioStreamPlayer> _pool = new();
    private AudioStreamPlayer _hum = null!;
    private AudioStreamPlayer2D _hiss = null!, _fire = null!; // v11.3 가장 가까운 새는 방·불 자리에서
    private AudioStreamWav _siren = null!, _beep = null!, _impact = null!, _scram = null!;
    private long _lastAlert;
    private Impact? _lastImpact;
    private int _scrams;
    private ulong _lastBeepMsec;

    public void Init(Main main)
    {
        _main = main;
        _siren = Make(Siren());
        _beep = Make(Beep());
        _impact = Make(ImpactSound());
        _scram = Make(Scram());
        _hum = Loop(Hum());
        _hiss = Loop2D(Noise(2f, 0.22f, high: true, seed: 3));
        _fire = Loop2D(FireSound());
        InitSpatial();
        for (int i = 0; i < 6; i++)
        {
            var p = new AudioStreamPlayer();
            AddChild(p);
            _pool.Add(p);
        }
        _lastAlert = main.Sim.AlertSerial;
        _lastImpact = main.Sim.Impacts.LastOrDefault();
        _scrams = main.Sim.History.Scrams;
        ApplyVolume();
    }

    public static void ApplyVolume() =>
        AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Mathf.Max(0.0001f, Settings.Volume)));

    public override void _Process(double delta)
    {
        var w = _main.Sim;
        bool quiet = _main.Replaying != null;

        // 효과음: 새로 울린 경보·운석·긴급 정지
        if (w.AlertSerial != _lastAlert)
        {
            // v10.3: 이번에 새로 울린 경보 중 가장 무거운 것으로 (치명 뒤에 경고가 붙어도 사이렌이 빠지지 않게)
            var fresh = w.Alerts.Where(a => a.Serial > _lastAlert).ToList();
            _lastAlert = w.AlertSerial;
            bool critical = fresh.Any(a => a.Level == AlertLevel.Critical);
            bool warning = fresh.Any(a => a.Level == AlertLevel.Warning);
            if (!quiet && Settings.Effects)
            {
                if (critical) Play(_siren, -4f);
                else if (warning && Time.GetTicksMsec() - _lastBeepMsec > 1500)
                {
                    _lastBeepMsec = Time.GetTicksMsec();
                    Play(_beep, -10f);
                }
            }
        }
        var impact = w.Impacts.LastOrDefault();
        if (impact != null && !ReferenceEquals(impact, _lastImpact))
        {
            _lastImpact = impact;
            // v11.3 맞은 자리에서 (화면 밖이어도 작게는 들린다)
            var at = impact.Target.Center;
            if (!quiet && Settings.Effects) Play2D(_impact, at, -2f + 6f * Math.Clamp(impact.Size - 0.5f, 0f, 1f), MathF.Max(0.3f, Gain(at)));
        }
        if (w.History.Scrams != _scrams)
        {
            _scrams = w.History.Scrams;
            if (!quiet && Settings.Effects) Play(_scram, -6f);
        }

        ProcessSpatial(w, delta, quiet); // v11.3 문·발소리·로봇
        ProcessBlasts(w, quiet); // v16.13 폭발음 (종류 · 거리 · 벽 너머 먹먹)

        // 배경음
        bool ambient = Settings.Ambience && !quiet && !_main.Paused;
        PlaceLoops(w);
        var p = w.Power;
        float hum = !ambient ? 0f : p.ReactorOnline ? 0.25f + 0.45f * Math.Clamp(p.Delivered / 45f, 0f, 1f) : p.AuxRunning ? 0.15f : 0.03f;
        float hiss = !ambient ? 0f : w.Ship.LiveRooms.Any(r => r.Leaking) ? 0.6f * _hissGain : 0f;
        float fire = !ambient ? 0f : Math.Min(1f, w.Fire.Count / 6f) * 0.8f * _fireGain;
        Fade(_hum, hum, delta);
        Fade(_hiss, hiss, delta);
        Fade(_fire, fire, delta);
    }

    private void Play(AudioStreamWav stream, float db)
    {
        var p = _pool.FirstOrDefault(x => !x.Playing) ?? _pool[0];
        p.Stream = stream;
        p.VolumeDb = db;
        p.Play();
    }

    private static void Fade(AudioStreamPlayer p, float target, double delta)
    {
        float now = p.Playing ? Mathf.DbToLinear(p.VolumeDb) : 0f;
        float next = Mathf.MoveToward(now, target, (float)delta * 0.8f);
        if (next <= 0.001f) { if (p.Playing) p.Stop(); return; }
        if (!p.Playing) p.Play();
        p.VolumeDb = Mathf.LinearToDb(next);
    }

    private static void Fade(AudioStreamPlayer2D p, float target, double delta)
    {
        float now = p.Playing ? Mathf.DbToLinear(p.VolumeDb) : 0f;
        float next = Mathf.MoveToward(now, target, (float)delta * 0.8f);
        if (next <= 0.001f) { if (p.Playing) p.Stop(); return; }
        if (!p.Playing) p.Play();
        p.VolumeDb = Mathf.LinearToDb(next);
    }

    private AudioStreamPlayer2D Loop2D(float[] samples)
    {
        var s = Make(samples);
        s.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        s.LoopBegin = 0;
        s.LoopEnd = samples.Length;
        var p = new AudioStreamPlayer2D { Stream = s, VolumeDb = -80f, MaxDistance = 1e7f, Attenuation = 0f, PanningStrength = 0.6f };
        AddChild(p);
        return p;
    }

    private AudioStreamPlayer Loop(float[] samples)
    {
        var s = Make(samples);
        s.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        s.LoopBegin = 0;
        s.LoopEnd = samples.Length;
        var p = new AudioStreamPlayer { Stream = s, VolumeDb = -80f };
        AddChild(p);
        return p;
    }

    private static AudioStreamWav Make(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short v = (short)Math.Clamp((int)(samples[i] * 32000f), short.MinValue, short.MaxValue);
            bytes[2 * i] = (byte)(v & 0xff);
            bytes[2 * i + 1] = (byte)((v >> 8) & 0xff);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = bytes };
    }

    // ── 파형 ──

    private static float[] Siren()
    {
        int n = (int)(Rate * 1.3f);
        var s = new float[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float f = 620f + 280f * (0.5f + 0.5f * MathF.Sin(t * MathF.Tau * 1.5f));
            phase += f / Rate;
            float x = MathF.Sin((float)(phase * MathF.Tau)) + 0.3f * MathF.Sin((float)(phase * MathF.Tau * 3));
            float env = MathF.Min(1f, t / 0.05f) * MathF.Min(1f, (1.3f - t) / 0.15f);
            s[i] = 0.45f * x * env;
        }
        return s;
    }

    private static float[] Beep()
    {
        int n = (int)(Rate * 0.34f);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            bool on = t < 0.12f || (t > 0.2f && t < 0.32f);
            float local = t < 0.12f ? t : t - 0.2f;
            float env = on ? MathF.Min(1f, local / 0.01f) * MathF.Min(1f, (0.12f - local) / 0.02f) : 0f;
            s[i] = 0.35f * env * MathF.Sin(t * MathF.Tau * 880f);
        }
        return s;
    }

    private static float[] ImpactSound()
    {
        int n = (int)(Rate * 1.1f);
        var s = new float[n];
        var rng = new Random(7);
        float low = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)(rng.NextDouble() * 2 - 1);
            low += 0.08f * (noise - low); // 낮은 쪽만 남긴 잡음
            float boom = MathF.Sin(t * MathF.Tau * (58f - 20f * t)) * MathF.Exp(-t * 3.2f);
            float crack = low * 3f * MathF.Exp(-t * 7f);
            s[i] = 0.8f * (0.75f * boom + 0.5f * crack);
        }
        return s;
    }

    private static float[] Scram()
    {
        int n = (int)(Rate * 1.1f);
        var s = new float[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float f = 420f * MathF.Exp(-t * 1.3f);
            phase += f / Rate;
            float env = MathF.Min(1f, t / 0.03f) * MathF.Exp(-t * 1.5f);
            s[i] = 0.4f * env * MathF.Sin((float)(phase * MathF.Tau));
        }
        return s;
    }

    /// <summary>2초에 딱 맞게 도는 기계음 (50·100·150Hz) — 이음새 없이 반복된다.</summary>
    private static float[] Hum()
    {
        int n = Rate * 2;
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float wob = 1f + 0.15f * MathF.Sin(t * MathF.Tau * 0.5f);
            s[i] = 0.22f * wob * (MathF.Sin(t * MathF.Tau * 50f) + 0.5f * MathF.Sin(t * MathF.Tau * 100f) + 0.2f * MathF.Sin(t * MathF.Tau * 150f));
        }
        return s;
    }

    private static float[] Noise(float seconds, float amp, bool high, int seed)
    {
        int n = (int)(Rate * seconds);
        var s = new float[n];
        var rng = new Random(seed);
        float prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float x = (float)(rng.NextDouble() * 2 - 1);
            s[i] = amp * (high ? x - 0.85f * prev : x);
            prev = x;
        }
        // 끝과 처음을 부드럽게 겹친다
        int fade = Rate / 20;
        for (int i = 0; i < fade; i++)
        {
            float k = i / (float)fade;
            s[i] = s[i] * k + s[n - fade + i] * (1f - k);
        }
        return s;
    }

    private static float[] FireSound()
    {
        int n = Rate * 2;
        var s = new float[n];
        var rng = new Random(11);
        float brown = 0f;
        for (int i = 0; i < n; i++)
        {
            float x = (float)(rng.NextDouble() * 2 - 1);
            brown = Math.Clamp(brown + 0.02f * x, -1f, 1f);
            float crackle = rng.NextDouble() < 0.0015 ? (float)(rng.NextDouble() * 2 - 1) * 0.9f : 0f;
            s[i] = 0.5f * brown + crackle;
        }
        int fade = Rate / 20;
        for (int i = 0; i < fade; i++)
        {
            float k = i / (float)fade;
            s[i] = s[i] * k + s[n - fade + i] * (1f - k);
        }
        return s;
    }
}
