using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.6 주컴퓨터 소리 (읽기만 한다 — 시뮬레이션은 소리를 모른다):
///  · 서버 랙 팬: 서버실 자리에서 계속 돈다 — 부하만큼 빠르고 크게, 방이 달아오르면 높게 울며 떨고(과열), 재부팅이면 멎었다가 차츰 다시 돈다, 멎으면 조용하다.
///  · 선내 방송 차임: 새 방송이 나가면 화면 가운데에 가까운 "울린 스피커" 자리에서 딩동 (경보 방송은 낮고 세 번) — 스피커가 안 울린 방뿐이면 소리도 없다.
/// </summary>
public partial class SoundSystem
{
    private AudioStreamPlayer2D? _fan;
    private AudioStreamWav _chime = null!, _chimeAlarm = null!;
    private int _lastBroadcast = -1;

    private void InitComputerSound()
    {
        _fan = Loop2D(FanSound());
        _chime = Make(ChimeSound(false));
        _chimeAlarm = Make(ChimeSound(true));
        _lastBroadcast = _main.Sim.Automation.Speak.Recent.LastOrDefault()?.Id ?? -1;
    }

    /// <summary>프레임마다: 팬 자리 · 크기 · 높이 · 새 방송 차임.</summary>
    private void ProcessComputerSound(World w, double delta, bool quiet)
    {
        if (_fan == null) InitComputerSound();
        var a = w.Automation;
        bool ambient = Settings.Ambience && !quiet && !_main.Paused;
        float target = 0f, pitch = 1f;
        if (a.ComputerBody is Furniture body && !body.Room.Detached && body.Machine is Machine m)
        {
            _fan!.GlobalPosition = GlobalOf(body.Room.Center);
            float near = MathF.Max(0.12f, Gain(body.Room.Center));
            float temp = body.Room.Air.Temperature;
            bool running = m.Powered && !m.Stopped;
            if (running && a.Rebooting)
            {
                // 재부팅: 처음엔 멎어 가다가 끝 무렵 다시 돈다
                float span = MathF.Max(1f, a.RebootUntil - a.RebootStarted);
                float k = Math.Clamp((w.Tick - a.RebootStarted) / span, 0f, 1f);
                float spin = k < 0.5f ? 1f - k * 1.6f : 0.2f + (k - 0.5f) * 1.6f;
                target = 0.18f * spin * near;
                pitch = 0.55f + 0.45f * spin;
            }
            else if (running)
            {
                float load = Math.Clamp(a.Load, 0f, 1.4f);
                float hot = Math.Clamp((temp - (AutomationSystem.OverheatC - 6f)) / 6f, 0f, 1f);
                target = (0.1f + 0.22f * load + 0.2f * hot) * near;
                pitch = 0.85f + 0.3f * load + 0.35f * hot;
                if (hot > 0.3f) pitch += 0.04f * MathF.Sin((float)Time.GetTicksMsec() * 0.013f); // 과열: 팬이 떨며 운다
            }
        }
        if (!ambient) target = 0f;
        Fade(_fan!, target, delta);
        if (_fan!.Playing) _fan.PitchScale = Mathf.MoveToward(_fan.PitchScale, Mathf.Clamp(pitch, 0.4f, 1.8f), (float)delta * 0.6f);

        // 새 방송: 울린 스피커 가운데 화면 가운데에 가장 가까운 방에서
        var last = a.Speak.Recent.LastOrDefault();
        if (last == null || last.Id == _lastBroadcast) return;
        _lastBroadcast = last.Id;
        if (quiet || !Settings.Effects || last.Rooms.Count == 0 || last.Priority < 1) return;
        var center = GetViewport().GetVisibleRect().Size * 0.5f;
        Room? at = null;
        float best = float.MaxValue;
        foreach (int id in last.Rooms)
        {
            if (id < 0 || id >= w.Ship.Rooms.Count) continue;
            var r = w.Ship.Rooms[id];
            float d = (ScreenOf(r.Center) - center).LengthSquared();
            if (d < best) { best = d; at = r; }
        }
        if (at != null) Play2D(last.Priority >= 2 ? _chimeAlarm : _chime, at.Center, last.Priority >= 2 ? -8f : -13f, MathF.Max(0.3f, Gain(at.Center)));
    }

    // ── 파형 ──

    /// <summary>서버 팬: 띠를 거른 바람 + 날개 지나가는 낮은 웅웅(120Hz 배음) + 가는 베어링 소리. 2초 고리 (이음매 없이 정수 주기).</summary>
    private static float[] FanSound()
    {
        int n = Rate * 2;
        var s = new float[n];
        var rng = new Random(77);
        float lp = 0f, lp2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float x = (float)(rng.NextDouble() * 2 - 1);
            lp += 0.12f * (x - lp);
            lp2 += 0.5f * (lp - lp2);
            float air = (lp - 0.6f * lp2) * 1.4f;
            float blade = 0.22f * MathF.Sin(t * MathF.Tau * 120f) + 0.1f * MathF.Sin(t * MathF.Tau * 240f) + 0.04f * MathF.Sin(t * MathF.Tau * 1380f);
            float beat = 0.85f + 0.15f * MathF.Sin(t * MathF.Tau * 3f); // 날개 둘이 엇갈려 생기는 맥놀이
            s[i] = 0.5f * beat * (air + blade);
        }
        // 고리 이음매를 부드럽게 (끝 20ms를 처음과 섞는다)
        int fade = Rate / 50;
        for (int i = 0; i < fade; i++)
        {
            float k = i / (float)fade;
            s[n - fade + i] = s[n - fade + i] * (1f - k) + s[i] * k;
        }
        return s;
    }

    /// <summary>방송 차임: 안내는 높은 "딩—동", 경보는 낮게 세 번 "띵띵띵".</summary>
    private static float[] ChimeSound(bool alarm)
    {
        float len = alarm ? 0.75f : 0.9f;
        int n = (int)(Rate * len);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float v = 0f;
            if (alarm)
            {
                for (int k = 0; k < 3; k++)
                {
                    float tk = t - k * 0.22f;
                    if (tk < 0f) continue;
                    v += MathF.Sin(tk * MathF.Tau * 523f) * MathF.Exp(-tk * 9f) + 0.4f * MathF.Sin(tk * MathF.Tau * 1046f) * MathF.Exp(-tk * 14f);
                }
            }
            else
            {
                float t2 = t - 0.36f;
                v = MathF.Sin(t * MathF.Tau * 880f) * MathF.Exp(-t * 5f) * (t < 0.36f ? 1f : MathF.Exp(-(t - 0.36f) * 20f))
                    + (t2 > 0f ? MathF.Sin(t2 * MathF.Tau * 659f) * MathF.Exp(-t2 * 4f) : 0f);
                v += 0.25f * MathF.Sin(t * MathF.Tau * 1760f) * MathF.Exp(-t * 12f);
            }
            float attack = MathF.Min(1f, t / 0.004f);
            s[i] = 0.4f * attack * v;
        }
        return s;
    }
}
