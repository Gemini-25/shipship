using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v23 행성 환경음: 내려앉은 행성마다 바깥 소리가 다르다 — 바람 · 비 · 산성비 · 모래 폭풍 · 눈보라 휘파람 · 용암 · 분기공 · 간헐천 ·
///   수정 들판의 울림 · 가까운 번개(화면 번쩍임과 같이) · 먼 천둥 · 지진 · 땅 꺼짐 · 달궈진 선체가 딱딱 · 얼어붙은 선체가 끼익 · 내려앉을 때 쿵 · 엔진.
///   공기가 없는 행성에선 바람 · 비가 없고, 땅을 타고 오는 낮은 울림만 다리로 전해진다.
/// 확대율: 배 전체(멀리)에선 바깥 소리가 그대로 · 방 몇 개(중간)에선 조금 · 사람 하나(가까이)에선 선체 너머로 먹먹하게 작아진다.
/// </summary>
public partial class SoundSystem
{
    private AudioStreamPlayer? _pWind, _pRain, _pSand, _pHowl, _pLava, _pEngine;
    private AudioStreamWav _thunderNear = null!, _thunderFar = null!, _quake = null!, _geyser = null!, _hullTick = null!, _crystalChime = null!, _thump = null!;
    private readonly List<AudioStreamPlayer> _outPool = new();
    private int _pStrikes = -1, _pQuakes = -1, _pSinks = -1;
    private PlanetPhase _pPhase;
    private bool _pSeen;
    private double _pClock, _nextFar, _nextGeyser, _nextChime, _nextTick;
    private readonly Random _pr = new(907);

    /// <summary>--soundcheck: 1초마다 소리 상태를 글로 찍는다 (확인용).</summary>
    public static bool Probe;
    private double _probeAt;

    private void InitPlanetSound()
    {
        _pWind = OutLoop("wind", WindWave());
        _pRain = OutLoop("rain", RainWave());
        _pSand = OutLoop("sand", SandWave());
        _pHowl = OutLoop("howl", HowlWave());
        _pLava = OutLoop("lava", LavaWave());
        _pEngine = OutLoop("engine", EngineWave());
        _pEngine.Bus = Buses.Amb; // 배 엔진은 바깥이 아니다
        _thunderNear = Dsp.Wav("thunder_near", ThunderNear());
        _thunderFar = Dsp.Wav("thunder_far", ThunderFar());
        _quake = Dsp.Wav("quake", QuakeWave());
        _geyser = Dsp.Wav("geyser", GeyserWave());
        _hullTick = Dsp.Wav("hull_tick", HullTickWave());
        _crystalChime = Dsp.Wav("chime", ChimeWave());
        _thump = Dsp.Wav("touchdown", ThumpWave());
        for (int i = 0; i < 4; i++)
        {
            var p = new AudioStreamPlayer { Bus = Buses.Outside };
            AddChild(p);
            _outPool.Add(p);
        }
    }

    private AudioStreamPlayer OutLoop(string name, float[] samples)
    {
        var p = new AudioStreamPlayer { Stream = Dsp.Wav(name, samples, loop: true), VolumeDb = -80f, Bus = Buses.Outside };
        AddChild(p);
        return p;
    }

    private void PlayOut(AudioStreamWav s, string name, float db, float pitch = 1f, bool outside = true)
    {
        var pool = outside ? _outPool : _pool;
        var p = pool.FirstOrDefault(x => !x.Playing) ?? pool[0];
        p.Stream = s;
        p.VolumeDb = db;
        p.PitchScale = pitch;
        p.Play();
        Dsp.Heard(name);
    }

    private float R(float a, float b) => a + (b - a) * (float)_pr.NextDouble();

    /// <summary>매 프레임: 바깥 길 거름 · 크기 (확대율) · 행성 소리.</summary>
    private void ProcessPlanetSound(World w, double delta, bool quiet)
    {
        var pl = w.Planet;
        var p = pl.Here;
        float zoom = _main.Camera.Zoom.X;
        float z = Dsp.Closeness(zoom);
        bool airless = p != null && p.Pressure < 1f;
        // 확대율: 멀리선 열린 소리 · 가까이선 선체 너머 (공기가 없으면 땅을 타고 오는 낮은 울림뿐)
        Buses.Lowpass(airless ? 260f : 16000f * MathF.Pow(900f / 16000f, z));
        Buses.VolumeDb(Buses.Outside, airless ? -6f : -9f * z);
        float alpha = _main.Ground?.Alpha ?? 0f;
        bool down = p != null && pl.Phase is PlanetPhase.Approach or PlanetPhase.Descent or PlanetPhase.Landed or PlanetPhase.Liftoff;
        bool live = Settings.Ambience && !quiet && !_main.Paused && down;
        if (!_main.Paused) _pClock += delta;

        if (!down || alpha <= 0.01f)
        {
            _pSeen = false;
            if (_pWind != null) { Fade(_pWind, 0f, delta); Fade(_pRain!, 0f, delta); Fade(_pSand!, 0f, delta); Fade(_pHowl!, 0f, delta); Fade(_pLava!, 0f, delta); }
            if (_pEngine != null) Fade(_pEngine, live && pl.Phase == PlanetPhase.Approach ? 0.3f : 0f, delta);
            _pPhase = pl.Phase;
            return;
        }
        if (_pWind == null) InitPlanetSound();
        int quakes = pl.Harm.GetValueOrDefault("지진"), sinks = pl.Harm.GetValueOrDefault("땅 꺼짐");
        if (!_pSeen)
        {
            // 막 들리기 시작했을 때는 지난 번개 · 지진을 다시 울리지 않는다
            _pSeen = true;
            _pStrikes = pl.Strikes; _pQuakes = quakes; _pSinks = sinks;
            _nextFar = _pClock + R(3f, 9f); _nextGeyser = _pClock + R(8f, 20f); _nextChime = _pClock + R(1f, 3f); _nextTick = _pClock + R(1f, 4f);
        }
        var wx = pl.Weather;
        float pw = pl.WxPower, wind = pl.Wind;
        float air = airless ? 0f : 1f;
        bool wet = wx is PlanetWeather.Rain or PlanetWeather.AcidRain or PlanetWeather.Thunder or PlanetWeather.Typhoon;
        float a = live ? alpha : 0f;
        float windVol = air * MathF.Min(0.9f, (p!.Pressure > 30f ? 0.1f : 0.04f) + 0.75f * wind) * a;
        float rainVol = air * (wet ? 0.3f + 0.55f * pw : 0f) * a;
        float sandVol = wx == PlanetWeather.DustStorm ? (0.3f + 0.55f * pw) * a * air : 0f;
        float howlVol = air * (wx is PlanetWeather.Blizzard or PlanetWeather.Typhoon ? 0.2f + 0.45f * pw : p.Climate == PlanetClimate.Frozen ? 0.25f * wind : 0f) * a;
        float lavaVol = (p.Surface == PlanetSurface.Lava ? 0.6f : p.Has(PlanetFeature.Vents) ? 0.3f : p.Surface == PlanetSurface.Ash ? 0.12f : 0f) * a;
        Fade(_pWind!, windVol, delta);
        Fade(_pRain!, rainVol, delta);
        Fade(_pSand!, sandVol, delta);
        Fade(_pHowl!, howlVol, delta);
        Fade(_pLava!, lavaVol, delta);
        _pWind!.PitchScale = 0.85f + 0.4f * wind;
        _pRain!.PitchScale = wx == PlanetWeather.AcidRain ? 1.12f : wx == PlanetWeather.Typhoon ? 0.92f : 1f;
        _pHowl!.PitchScale = 0.9f + 0.3f * pw;
        float engine = !live ? 0f : pl.Phase is PlanetPhase.Descent or PlanetPhase.Liftoff ? 0.85f : pl.Phase == PlanetPhase.Approach ? 0.3f : 0f;
        Fade(_pEngine!, engine, delta);

        // 일어난 일 — 수는 늘 따라가고, 들릴 때만 울린다
        bool fx = live;
        if (pl.Strikes != _pStrikes)
        {
            if (fx && pl.Strikes > _pStrikes) PlayOut(_thunderNear, "가까운 번개", -1f, R(0.92f, 1.08f));
            _pStrikes = pl.Strikes;
        }
        if (quakes != _pQuakes)
        {
            if (fx && quakes > _pQuakes) PlayOut(_quake, "지진", -1f, R(0.9f, 1.05f), outside: false);
            _pQuakes = quakes;
        }
        if (sinks != _pSinks)
        {
            if (fx && sinks > _pSinks) PlayOut(_quake, "땅 꺼짐", -4f, 1.25f, outside: false);
            _pSinks = sinks;
        }
        if (_pPhase == PlanetPhase.Descent && pl.Phase == PlanetPhase.Landed && fx) PlayOut(_thump, "내려앉음", -2f, 1f, outside: false);
        _pPhase = pl.Phase;
        if (!fx || !pl.Landed) return;
        // 먼 천둥 (번쩍임 없이 멀리서)
        if (air > 0f && wx is PlanetWeather.Thunder or PlanetWeather.Typhoon && _pClock >= _nextFar)
        {
            PlayOut(_thunderFar, "먼 천둥", -5f - R(0f, 6f), R(0.8f, 1.05f));
            _nextFar = _pClock + R(7f, 22f) / (0.5f + pw);
        }
        // 간헐천
        if (p.Has(PlanetFeature.Geysers) && _pClock >= _nextGeyser)
        {
            PlayOut(_geyser, "간헐천", -7f, R(0.85f, 1.1f));
            _nextGeyser = _pClock + R(20f, 50f);
        }
        // 수정 들판 · 노래하는 행성: 바람에 울린다
        bool crystal = p.Surface == PlanetSurface.Crystal || p.Has(PlanetFeature.CrystalField) || p.Key == "singing";
        if (crystal && air > 0f && _pClock >= _nextChime)
        {
            PlayOut(_crystalChime, "수정 울림", p.Key == "singing" ? -8f : -14f, Dsp.Pentatonic[_pr.Next(Dsp.Pentatonic.Length)] * (_pr.Next(2) == 0 ? 0.5f : 1f));
            _nextChime = _pClock + R(0.8f, 3.5f) / (0.3f + wind + (p.Key == "singing" ? 0.5f : 0f));
        }
        // 달궈진 선체 딱딱 · 얼어붙은 선체 끼익
        float shell = pl.ShellC;
        if ((shell > 45f || wx == PlanetWeather.HeatWave || shell < -25f) && _pClock >= _nextTick)
        {
            bool cold = shell < -25f;
            PlayOut(_hullTick, cold ? "얼어붙은 선체" : "달궈진 선체", cold ? -13f : -15f, cold ? R(0.5f, 0.68f) : R(0.9f, 1.3f), outside: false);
            _nextTick = _pClock + R(1.5f, 6f);
        }
    }

    /// <summary>--soundcheck: 지금 들리는 것 (1초마다).</summary>
    private void ProbeLine(World w, double delta)
    {
        _probeAt -= delta;
        if (_probeAt > 0) return;
        _probeAt = 1.0;
        static string V(AudioStreamPlayer? p) => p == null || !p.Playing ? "-" : $"{Mathf.DbToLinear(p.VolumeDb) * 100:0}";
        float zoom = _main.Camera.Zoom.X;
        int outBus = AudioServer.GetBusIndex(Buses.Outside);
        GD.Print($"[소리] {w.Planet.Here?.Name ?? "우주"}({w.Planet.Here?.Surface}) · 확대 {zoom:0.00}({ZoomDetail.Name(ZoomDetail.Of(zoom))}) · 바깥 거름 {Buses.Cutoff():0}Hz {AudioServer.GetBusVolumeDb(outBus):0.0}dB"
            + $" · 효과음 {AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(Buses.Sfx)):0.0}dB · 환경음 {AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(Buses.Amb)):0.0}dB"
            + $" · 바람 {V(_pWind)} 비 {V(_pRain)} 모래 {V(_pSand)} 휘파람 {V(_pHowl)} 용암 {V(_pLava)} 엔진 {V(_pEngine)} · 기계음 {V(_hum)}"
            + $" · 일 소리 {_workers.Count}곳 · 최근: {string.Join(", ", Dsp.Recent.TakeLast(6))}");
    }

    // ── 파형 ──

    /// <summary>바람: 분홍 잡음을 오르내리는 띠로 걸러 돌풍처럼.</summary>
    private static float[] WindWave()
    {
        var s = Dsp.Buf(6.2f);
        var n = new Dsp.Noise(41);
        var band = new Dsp.Band();
        var low = new Dsp.Low();
        float len = s.Length / (float)Dsp.Rate;
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Dsp.Rate;
            float gust = 0.55f + 0.25f * MathF.Sin(Dsp.Tau * t / len) + 0.2f * MathF.Sin(Dsp.Tau * 3f * t / len + 1.3f);
            float x = n.Pink();
            float howl = band.Run(x, 260f + 420f * gust, 0.45f);
            float body = low.Run(x, 380f);
            s[i] = gust * (1.4f * howl + 0.9f * body);
        }
        return Dsp.Loop(Dsp.Normalize(s, 0.75f));
    }

    /// <summary>비 (v23 다시): 넓은 빗소리(분홍 잡음 · 높은 쪽은 걷어 냄) + 촘촘한 잔빗방울(잡음 알갱이) + 굵은 빗방울(짧은 잡음 톡 — 사인 '삑'이 아니다) + 낮은 두드림.</summary>
    private static float[] RainWave()
    {
        var s = Dsp.Buf(4.6f);
        var n = new Dsp.Noise(57);
        var hp = new Dsp.High();
        var lp1 = new Dsp.Low();
        var lp2 = new Dsp.Low();
        var fine = new Dsp.Band();
        var drum = new Dsp.Low();
        var drumHp = new Dsp.High();
        var swell = new Dsp.Wander(58, 0.6f);
        for (int i = 0; i < s.Length; i++)
        {
            float x = n.Pink();
            float rush = lp2.Run(lp1.Run(hp.Run(x, 350f), 6500f), 7500f);
            float grain = n.U() < 0.09f ? n.White() * (0.4f + 0.6f * n.U()) : 0f; // 하나하나는 들리지 않는 잔빗방울
            float patter = fine.Run(grain, 3600f, 0.9f);
            float low = drumHp.Run(drum.Run(n.Brown(), 220f), 70f); // 지붕을 두드리는 낮은 쪽 (DC는 걷어 낸다)
            s[i] = (0.9f + 0.12f * swell.Next()) * (0.55f * rush + 0.75f * patter) + 0.12f * low;
        }
        float len = s.Length / (float)Dsp.Rate;
        int drops = (int)(len * 55f);
        for (int d = 0; d < drops; d++)
        {
            float at = n.U() * len, amp = 0.06f + 0.22f * n.U() * n.U(), f = n.Range(900f, 3200f), dec = n.Range(140f, 320f), q = n.Range(0.25f, 0.6f);
            var b = new Dsp.Band();
            var nn = new Dsp.Noise(d * 7 + 3);
            Dsp.Add(s, at, 0.03f, t => amp * MathF.Exp(-t * dec) * b.Run(nn.White(), f, q) * 2.2f);
        }
        return Dsp.Loop(Dsp.Normalize(Dsp.Reverb(s, 0.45f, 0.18f), 0.7f), 0.3f);
    }

    /// <summary>모래 폭풍: 알갱이가 부딪는 거친 쉬 소리.</summary>
    private static float[] SandWave()
    {
        var s = Dsp.Buf(4.2f);
        var n = new Dsp.Noise(63);
        var low = new Dsp.Low();
        var band = new Dsp.Band();
        float grain = 1f;
        for (int i = 0; i < s.Length; i++)
        {
            if (i % 44 == 0) grain = 0.3f + 0.7f * n.U();
            float x = n.White();
            float high = x - low.Run(x, 2500f);
            s[i] = grain * (0.7f * high + 0.6f * band.Run(x, 1900f, 0.8f));
        }
        return Dsp.Loop(Dsp.Normalize(s, 0.65f));
    }

    /// <summary>눈보라 · 태풍 울부짖음 (v23 다시): 세기가 불규칙하게 몰아치고(사인 대신 떠도는 값), 넓은 바람 소리 위로 틈새를 지나는 울음이 오르내린다.</summary>
    private static float[] HowlWave()
    {
        var s = Dsp.Buf(7.2f);
        var n = new Dsp.Noise(71);
        var b1 = new Dsp.Band();
        var b2 = new Dsp.Band();
        var b3 = new Dsp.Band();
        var rushLp = new Dsp.Low();
        var rushHp = new Dsp.High();
        var gustW = new Dsp.Wander(72, 0.35f);
        var gustF = new Dsp.Wander(73, 1.6f);
        var f1 = new Dsp.Wander(74, 0.45f);
        var f2 = new Dsp.Wander(75, 0.3f);
        var whistleW = new Dsp.Wander(76, 0.25f);
        for (int i = 0; i < s.Length; i++)
        {
            float gust = Math.Clamp(0.6f + 0.32f * gustW.Next() + 0.08f * gustF.Next(), 0.15f, 1f);
            float x = n.Pink();
            float rush = rushLp.Run(rushHp.Run(x, 120f), 900f + 900f * gust);
            float moan = b1.Run(x, 300f + 160f * f1.Next() + 120f * gust, 0.22f);
            float moan2 = b2.Run(x, 520f + 200f * f2.Next(), 0.28f);
            float w = Math.Clamp(whistleW.Next() * 0.5f + gust - 0.55f, 0f, 1f);
            float whistle = b3.Run(n.White(), 820f + 260f * f2.Next() + 300f * gust, 0.06f) * w;
            s[i] = gust * (0.9f * rush + 1.4f * moan + 0.8f * moan2) + 0.9f * whistle;
        }
        return Dsp.Loop(Dsp.Normalize(s, 0.6f), 0.5f);
    }

    /// <summary>용암: 깊은 웅웅 + 끓어오르는 거품 + 튀는 소리 · 김.</summary>
    private static float[] LavaWave()
    {
        var s = Dsp.Buf(5.2f);
        var n = new Dsp.Noise(83);
        var l1 = new Dsp.Low();
        var l2 = new Dsp.Low();
        var hp = new Dsp.High();
        float len = s.Length / (float)Dsp.Rate;
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Dsp.Rate;
            float rumble = 3f * hp.Run(l2.Run(l1.Run(n.Brown(), 90f), 70f), 28f);
            s[i] = 2.6f * rumble + 0.18f * MathF.Sin(Dsp.Tau * 31f * t) * (0.7f + 0.3f * MathF.Sin(Dsp.Tau * t / len));
        }
        for (int k = 0; k < (int)(len * 3.2f); k++)
        {
            float at = n.U() * len, d = n.Range(0.07f, 0.16f), f0 = n.Range(60f, 95f), amp = n.Range(0.25f, 0.6f);
            double ph = 0;
            Dsp.Add(s, at, d, t => { ph += f0 * MathF.Pow(3f, t / d) / Dsp.Rate; return amp * MathF.Sin(MathF.PI * t / d) * MathF.Sin((float)(ph * Dsp.Tau)); });
        }
        for (int k = 0; k < (int)(len * 1.2f); k++)
        {
            float at = n.U() * len, amp = n.Range(0.08f, 0.22f);
            var bb = new Dsp.Band();
            var nn = new Dsp.Noise(k * 13 + 5);
            Dsp.Add(s, at, 0.03f, t => amp * MathF.Exp(-t * 120f) * bb.Run(nn.White(), 1300f, 0.5f) * 3f);
        }
        return Dsp.Loop(Dsp.Normalize(s, 0.75f));
    }

    /// <summary>엔진: 낮은 굉음 + 38 · 76Hz 떨림.</summary>
    private static float[] EngineWave()
    {
        var s = Dsp.Buf(3.2f);
        var n = new Dsp.Noise(91);
        var low = new Dsp.Low();
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Dsp.Rate;
            s[i] = 2.2f * low.Run(n.Brown() + 0.3f * n.White(), 220f) + 0.25f * MathF.Sin(Dsp.Tau * 38f * t) + 0.12f * MathF.Sin(Dsp.Tau * 76f * t);
        }
        return Dsp.Loop(Dsp.Normalize(s, 0.7f));
    }

    /// <summary>가까운 번개 (v23 다시): 날카로운 '쩍' + 짧게 이어지는 찢어짐 → 큰 쾅 → 여러 번 굴러가는 천둥 (사이사이 커졌다 잦아든다) · 하늘에 퍼지는 잔향.</summary>
    private static float[] ThunderNear()
    {
        float len = 5.2f;
        var s = Dsp.Buf(len);
        var n = new Dsp.Noise(101);
        var crackHp = new Dsp.High();
        var boomLp1 = new Dsp.Low();
        var boomLp2 = new Dsp.Low();
        var sub = new Dsp.High();
        var midLp = new Dsp.Low();
        var midLp2 = new Dsp.Low();
        var midHp = new Dsp.High();
        var tear = new Dsp.Band();
        var grumble = new Dsp.Wander(102, 7f);
        // 굴러가는 덩어리: (시작 · 오름 · 내림 · 세기)
        var rolls = new List<(float at, float up, float down, float amp)> { (0.03f, 0.02f, 0.9f, 1f) };
        float tr = 0.35f;
        for (int k = 0; k < 7 && tr < len - 1f; k++) { rolls.Add((tr, n.Range(0.12f, 0.3f), n.Range(0.5f, 1.1f), n.Range(0.35f, 0.75f) * MathF.Exp(-tr * 0.35f))); tr += n.Range(0.25f, 0.6f); }
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Dsp.Rate;
            float w = n.White();
            // 쩍: 처음 15ms는 통째로, 그 뒤 0.35초는 듬성듬성 찢어진다
            float crackEnv = t < 0.012f ? 1f : t < 0.6f && n.U() < 0.22f * MathF.Exp(-t * 5f) + 0.03f ? 0.75f * MathF.Exp(-t * 5f) : 0f;
            float crack = crackHp.Run(w, 1400f) * crackEnv + 0.5f * tear.Run(w * crackEnv, 900f, 0.6f);
            float env = 0f;
            foreach (var r in rolls) if (t >= r.at) { float u = t - r.at; env += r.amp * MathF.Min(1f, u / r.up) * MathF.Exp(-MathF.Max(0f, u - r.up) / r.down); }
            float body = sub.Run(boomLp2.Run(boomLp1.Run(n.Brown() + 0.15f * w, 220f), 140f), 32f);
            float mid = midHp.Run(midLp2.Run(midLp.Run(n.Pink(), 480f), 420f), 110f); // 작은 스피커에서도 들리는 우르릉
            s[i] = 0.9f * crack + env * (0.8f + 0.2f * grumble.Next()) * (5f * body + 1.6f * mid);
        }
        return Dsp.Normalize(Dsp.Reverb(s, 1.4f, 0.28f, 0.55f), 0.95f);
    }

    /// <summary>먼 천둥 (v23 다시): 찢어짐 없이 낮게 깔려 여러 번 우르릉 굴러간다 — 높은 쪽은 거리에 먹혀 둥글다.</summary>
    private static float[] ThunderFar()
    {
        float len = 6.2f;
        var s = Dsp.Buf(len);
        var n = new Dsp.Noise(111);
        var l1 = new Dsp.Low();
        var l2 = new Dsp.Low();
        var sub = new Dsp.High();
        var midLp = new Dsp.Low();
        var midLp2 = new Dsp.Low();
        var midHp = new Dsp.High();
        var grumble = new Dsp.Wander(112, 5f);
        var rolls = new List<(float at, float up, float down, float amp)>();
        float tr = 0.15f;
        for (int k = 0; k < 6 && tr < len - 1.2f; k++) { rolls.Add((tr, n.Range(0.35f, 0.7f), n.Range(0.7f, 1.3f), n.Range(0.5f, 1f) * (k == 1 ? 1.3f : 1f))); tr += n.Range(0.45f, 1f); }
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Dsp.Rate;
            float env = 0f;
            foreach (var r in rolls) if (t >= r.at) { float u = t - r.at; env += r.amp * MathF.Min(1f, u / r.up) * MathF.Exp(-MathF.Max(0f, u - r.up) / r.down); }
            float body = sub.Run(l2.Run(l1.Run(n.Brown(), 140f), 95f), 30f);
            float mid = midHp.Run(midLp2.Run(midLp.Run(n.Pink(), 260f), 220f), 90f);
            s[i] = env * (0.7f + 0.3f * grumble.Next()) * (body + 0.4f * mid);
        }
        return Dsp.Normalize(Dsp.Reverb(s, 1.5f, 0.3f, 0.65f), 0.7f);
    }

    /// <summary>지진 (v23 다시): 땅 밑 깊은 울림이 불규칙하게 밀려오고, 갈리는 소리 · 선반과 걸쇠가 덜그럭(짧은 잡음 덩어리 — 사인 '삑'이 아니다) · 무거운 것이 쿵.</summary>
    private static float[] QuakeWave()
    {
        float len = 4.4f;
        var s = Dsp.Buf(len);
        var n = new Dsp.Noise(121);
        var l1 = new Dsp.Low();
        var l2 = new Dsp.Low();
        var sub = new Dsp.High();
        var growl = new Dsp.Band();
        var grind = new Dsp.Band();
        var surge = new Dsp.Wander(122, 1.3f);
        var shake = new Dsp.Wander(123, 9f);
        float Shape(float t) => MathF.Min(1f, t / 0.5f) * MathF.Min(1f, MathF.Max(0f, (len - t) / 1.6f));
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Dsp.Rate;
            float e = Shape(t) * (0.75f + 0.25f * surge.Next());
            float x = n.Pink();
            float rumble = 5f * sub.Run(l2.Run(l1.Run(n.Brown(), 75f), 55f), 18f);
            float g = growl.Run(x, 140f, 0.6f) * (0.6f + 0.4f * shake.Next());
            float gr = grind.Run(n.White(), 380f, 0.7f) * 0.25f * (0.5f + 0.5f * surge.Next());
            s[i] = e * (rumble + 2.2f * g + gr);
        }
        for (int k = 0; k < 140; k++)
        {
            float at = n.U() * len, amp = 0.1f * Shape(at) * n.Range(0.3f, 1f), f = n.Range(500f, 2600f), q = n.Range(0.15f, 0.4f), dur = n.Range(0.006f, 0.03f);
            var b = new Dsp.Band();
            var nn = new Dsp.Noise(k * 17 + 9);
            Dsp.Add(s, at, dur + 0.03f, t => amp * MathF.Min(1f, t / 0.001f) * MathF.Exp(-t / dur) * b.Run(nn.White(), f, q) * 2f);
        }
        for (int k = 0; k < 5; k++)
        {
            float at = n.Range(0.6f, len - 1f), amp = n.Range(0.4f, 0.8f);
            var lp = new Dsp.Low();
            var nn = new Dsp.Noise(k * 31 + 2);
            Dsp.Add(s, at, 0.12f, t => amp * MathF.Exp(-t * 28f) * lp.Run(nn.White(), 260f) * 4f);
        }
        return Dsp.Normalize(Dsp.Reverb(s, 0.7f, 0.15f), 0.9f);
    }

    /// <summary>간헐천: 보글거리다 솟구치는 김 소리.</summary>
    private static float[] GeyserWave()
    {
        var s = Dsp.Buf(3.4f);
        var n = new Dsp.Noise(131);
        var band = new Dsp.Band();
        float len = s.Length / (float)Dsp.Rate;
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Dsp.Rate;
            float rise = t < 0.6f ? 0f : MathF.Min(1f, (t - 0.6f) / 0.5f);
            float env = rise * MathF.Min(1f, (len - t) / 1.2f);
            s[i] = env * band.Run(n.White(), 400f + 1400f * rise * (1f - 0.3f * (t - 1.1f) / len), 0.9f) * 1.6f;
        }
        for (int k = 0; k < 9; k++)
        {
            float at = n.Range(0f, 0.7f), f0 = n.Range(90f, 140f);
            Dsp.Add(s, at, 0.09f, t => 0.3f * MathF.Sin(MathF.PI * t / 0.09f) * MathF.Sin(Dsp.Tau * f0 * (1f + 3f * t) * t));
        }
        return Dsp.Normalize(s, 0.8f);
    }

    /// <summary>선체가 열에 늘거나 추위에 줄 때: 금속이 "딱".</summary>
    private static float[] HullTickWave()
    {
        var s = Dsp.Buf(0.3f);
        var n = new Dsp.Noise(141);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Dsp.Rate;
            s[i] = MathF.Exp(-t * 28f) * (MathF.Sin(Dsp.Tau * 1150f * t) + 0.6f * MathF.Sin(Dsp.Tau * 2730f * t) + 0.35f * MathF.Sin(Dsp.Tau * 4210f * t))
                + (t < 0.004f ? n.White() * 0.8f : 0f);
        }
        return Dsp.Normalize(s, 0.8f);
    }

    /// <summary>수정이 바람에 울리는 소리 (높이는 재생 속도로 바꾼다).</summary>
    private static float[] ChimeWave()
    {
        var s = Dsp.Buf(2.4f);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Dsp.Rate;
            s[i] = MathF.Min(1f, t / 0.004f) * (MathF.Exp(-t * 2.2f) * MathF.Sin(Dsp.Tau * 880f * t) + 0.45f * MathF.Exp(-t * 3.8f) * MathF.Sin(Dsp.Tau * 2428f * t)
                + 0.2f * MathF.Exp(-t * 7f) * MathF.Sin(Dsp.Tau * 4752f * t));
        }
        return Dsp.Normalize(s, 0.7f);
    }

    /// <summary>내려앉을 때: 착륙 다리가 땅에 쿵 · 뼈대가 끼익.</summary>
    private static float[] ThumpWave()
    {
        var s = Dsp.Buf(1.4f);
        var n = new Dsp.Noise(151);
        var low = new Dsp.Low();
        double ph = 0;
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Dsp.Rate;
            float thud = MathF.Sin(Dsp.Tau * (60f - 25f * t) * t) * MathF.Exp(-t * 5f) + 2.5f * low.Run(n.White(), 150f) * MathF.Exp(-t * 9f);
            ph += (140f + 12f * MathF.Sin(Dsp.Tau * 7f * t)) / Dsp.Rate;
            float creak = t is > 0.3f and < 1.1f ? 0.18f * MathF.Sin(MathF.PI * (t - 0.3f) / 0.8f) * (2f * (float)(ph - Math.Floor(ph)) - 1f) : 0f;
            s[i] = thud + creak;
        }
        return Dsp.Normalize(s, 0.9f);
    }
}
