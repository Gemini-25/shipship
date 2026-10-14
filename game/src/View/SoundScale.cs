using System;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.18 규모별 경보음 (읽기만 한다 — 시뮬레이션은 소리를 모른다). 규모가 오를 때마다(ScaleSystem.Serial) 그 규모의 소리를 한 번:
///  ① 개인: 손목 단말의 짧은 두 번 "삑삑" (작게) · ② 방: 올라가는 세 음 차임 · ③ 계통: 두 음을 번갈아 우는 경적
///  ④ 배 전체: 낮게 깔리는 혼 위로 올라가는 사이렌 두 번 · ⑤ 우주급: 깊은 웅웅 위로 반짝이는 배음이 내려앉는다.
/// </summary>
public partial class SoundSystem
{
    private AudioStreamWav[]? _scaleSounds;
    private int _lastScaleSerial = -1;

    private void ProcessScaleSound(World w, bool quiet)
    {
        if (_scaleSounds == null)
        {
            _scaleSounds = new[] { Make(PersonalPip()), Make(RoomChime()), Make(SystemKlaxon()), Make(ShipSiren()), Make(CosmicDrone()) };
            _lastScaleSerial = w.Scale.Serial;
            return;
        }
        if (w.Scale.Serial == _lastScaleSerial) return;
        _lastScaleSerial = w.Scale.Serial;
        var (_, scale, tick) = w.Scale.LastRise;
        if (quiet || !Settings.Effects || tick < 0 || w.Tick - tick > SimTime.Minutes(2)) return;
        float db = scale switch { IncidentScale.Personal => -20f, IncidentScale.Room => -12f, IncidentScale.System => -8f, IncidentScale.Ship => -4f, _ => -3f };
        Play(_scaleSounds[(int)scale], db);
    }

    private static float Env(float t, float len, float attack = 0.01f) => MathF.Min(1f, t / attack) * MathF.Max(0f, 1f - t / len);

    /// <summary>개인: 손목 단말 "삑삑" (2kHz 짧게 두 번).</summary>
    private static float[] PersonalPip()
    {
        int n = (int)(Rate * 0.32f);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float v = 0f;
            for (int k = 0; k < 2; k++)
            {
                float tk = t - k * 0.14f;
                if (tk >= 0f && tk < 0.07f) v += MathF.Sin(tk * MathF.Tau * 2093f) * Env(tk, 0.07f, 0.003f);
            }
            s[i] = 0.3f * v;
        }
        return s;
    }

    /// <summary>방: 올라가는 세 음 (도 · 미 · 솔) 차임.</summary>
    private static float[] RoomChime()
    {
        int n = (int)(Rate * 1.1f);
        var s = new float[n];
        float[] f = { 523f, 659f, 784f };
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float v = 0f;
            for (int k = 0; k < 3; k++)
            {
                float tk = t - k * 0.18f;
                if (tk < 0f) continue;
                v += (MathF.Sin(tk * MathF.Tau * f[k]) + 0.3f * MathF.Sin(tk * MathF.Tau * f[k] * 2f)) * MathF.Exp(-tk * 4.5f);
            }
            s[i] = 0.32f * MathF.Min(1f, t / 0.004f) * v;
        }
        return s;
    }

    /// <summary>계통: 두 음(440 · 554Hz)을 번갈아 우는 각진 경적 1.3초.</summary>
    private static float[] SystemKlaxon()
    {
        int n = (int)(Rate * 1.3f);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float f = (int)(t / 0.16f) % 2 == 0 ? 440f : 554f;
            float ph = t * f;
            float sq = MathF.Sign(MathF.Sin(ph * MathF.Tau)) * 0.55f + 0.45f * MathF.Sin(ph * MathF.Tau * 3f) / 3f;
            s[i] = 0.22f * sq * Env(t, 1.3f, 0.02f);
        }
        return s;
    }

    /// <summary>배 전체: 90Hz 혼이 깔리고 그 위로 300 → 900Hz로 올라가는 사이렌 두 번.</summary>
    private static float[] ShipSiren()
    {
        float len = 2.4f;
        int n = (int)(Rate * len);
        var s = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float k = (t % 1.2f) / 1.2f;
            float f = 300f + 600f * MathF.Sin(k * MathF.PI * 0.5f);
            phase += f / Rate;
            float siren = MathF.Sin(phase * MathF.Tau) + 0.35f * MathF.Sin(phase * MathF.Tau * 2f);
            float horn = MathF.Sin(t * MathF.Tau * 90f) + 0.5f * MathF.Sin(t * MathF.Tau * 180f) + 0.25f * MathF.Sin(t * MathF.Tau * 270f);
            s[i] = (0.22f * siren + 0.2f * horn) * Env(t, len, 0.05f);
        }
        return s;
    }

    /// <summary>우주급 (v23 다시): 살짝 어긋난 낮은 톱니 셋이 맥놀이하며 웅웅대고, 잡음에 울리는 선체 공명(90·135·210·330Hz)이 깔리며, 높은 쉬익이 천천히 내려앉는다 · 넓은 잔향.</summary>
    private static float[] CosmicDrone()
    {
        float len = 3.4f;
        var s = Dsp.Buf(len);
        var n = new Dsp.Noise(131);
        var sawLp1 = new Dsp.Low();
        var sawLp2 = new Dsp.Low();
        var modes = new Dsp.Band[4];
        float[] modeHz = { 90f, 135f, 210f, 330f }, modeAmp = { 1f, 0.8f, 0.55f, 0.35f };
        var whoosh = new Dsp.Band();
        var breathe = new Dsp.Wander(132, 0.8f);
        double p1 = 0, p2 = 0, p3 = 0;
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Dsp.Rate;
            p1 += 45f / Dsp.Rate; p2 += 45.6f / Dsp.Rate; p3 += 67.5f / Dsp.Rate;
            float saw = (float)(2 * (p1 - Math.Floor(p1)) - 1) + (float)(2 * (p2 - Math.Floor(p2)) - 1) + 0.6f * (float)(2 * (p3 - Math.Floor(p3)) - 1);
            float drone = sawLp2.Run(sawLp1.Run(saw, 300f), 260f);
            float x = n.Pink();
            float hull = 0f;
            for (int k = 0; k < 4; k++) hull += modeAmp[k] * modes[k].Run(x, modeHz[k], 0.08f);
            float wf = 2000f * MathF.Pow(0.2f, t / len);
            float hiss = whoosh.Run(n.White(), wf, 0.35f) * MathF.Min(1f, t / 0.25f);
            float env = MathF.Min(1f, t / 0.45f) * MathF.Min(1f, MathF.Max(0f, (len - t) / 1.1f));
            s[i] = env * ((0.8f + 0.2f * breathe.Next()) * (0.55f * drone + 0.12f * hull) + 0.09f * hiss);
        }
        return Dsp.Normalize(Dsp.Reverb(s, 1.3f, 0.3f, 0.5f), 0.32f);
    }
}
