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

    /// <summary>우주급: 45Hz 깊은 웅웅(맥놀이) 위로 반짝이는 배음이 2kHz → 400Hz로 천천히 내려앉는다.</summary>
    private static float[] CosmicDrone()
    {
        float len = 3.2f;
        int n = (int)(Rate * len);
        var s = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float drone = MathF.Sin(t * MathF.Tau * 45f) * (0.75f + 0.25f * MathF.Sin(t * MathF.Tau * 1.5f)) + 0.4f * MathF.Sin(t * MathF.Tau * 47.5f);
            float f = 2000f * MathF.Pow(0.2f, t / len);
            phase += f / Rate;
            float shimmer = MathF.Sin(phase * MathF.Tau) * (0.5f + 0.5f * MathF.Sin(t * MathF.Tau * 7f));
            s[i] = (0.3f * drone + 0.12f * shimmer) * Env(t, len, 0.4f);
        }
        return s;
    }
}
