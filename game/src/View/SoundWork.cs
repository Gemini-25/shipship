using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v23 일하는 소리: 자리에서 손을 놀리는 사람 · 로봇마다 하는 일에 맞는 소리 —
///   렌치 딸깍 (수리 · 정비) · 용접 지지직 (외벽 · 파공 · 격벽) · 망치 (만들기 · 치우기 · 문) · 전기 찌직 (차단기 · 배선 · 조명) ·
///   자판 (함교 · 연구 · 근무) · 지글지글 · 도마 (요리) · 가위 (재배) · 삑삑 (치료 · 수술) · 걸레 (청소) · 소화기 (불 끄기) · 드릴 (제작 · 재활용).
/// 확대율: 배 전체(멀리)에선 들리지 않고, 방 몇 개(중간)에선 가장 가까운 셋이 작게, 사람 하나(가까이)에선 넷이 또렷하게.
/// 로봇은 조금 높고 빠르게. 진공인 방에선 소리가 없다. 빠른 배속(10배 넘게)에선 쉰다.
/// </summary>
public partial class SoundSystem
{
    public enum WorkSfx : byte { None, Ratchet, Weld, Hammer, Zap, Typing, Sizzle, Chop, Snip, Beep, Mop, Spray, Drill }

    private AudioStreamWav?[] _workWav = Array.Empty<AudioStreamWav?>();
    private readonly List<(int key, System.Numerics.Vector2 pos, WorkSfx kind, bool robot)> _workers = new();
    private readonly Dictionary<int, double> _workNext = new();
    private double _workClock, _workScan;
    private readonly Random _wr = new(313);

    private static readonly string[] WorkName = { "", "렌치", "용접", "망치", "전기", "자판", "지글지글", "도마", "가위", "삑삑", "걸레", "소화기", "드릴" };

    private void InitWork()
    {
        _workWav = new AudioStreamWav?[]
        {
            null, Dsp.Wav("w_ratchet", Ratchet()), Dsp.Wav("w_weld", Weld()), Dsp.Wav("w_hammer", Hammer()), Dsp.Wav("w_zap", Zap()),
            Dsp.Wav("w_typing", Typing()), Dsp.Wav("w_sizzle", Sizzle()), Dsp.Wav("w_chop", Chop()), Dsp.Wav("w_snip", Snip()),
            Dsp.Wav("w_beep", MedBeep()), Dsp.Wav("w_mop", Mop()), Dsp.Wav("w_spray", Spray()), Dsp.Wav("w_drill", Drill()),
        };
    }

    /// <summary>맡은 일 → 소리.</summary>
    public static WorkSfx SfxFor(WorkKind k) => k switch
    {
        WorkKind.RepairHull or WorkKind.SealBreach or WorkKind.WeldBulkhead or WorkKind.RebuildFrame or WorkKind.RepairJoint or WorkKind.InstallTruss
            or WorkKind.ReplacePanel or WorkKind.Reline or WorkKind.SealSeep or WorkKind.SealO2Line or WorkKind.PatchPipe or WorkKind.ReplacePipe => WorkSfx.Weld,
        WorkKind.ResetBreaker or WorkKind.RestoreCircuit or WorkKind.InstallJumper or WorkKind.RemoveJumper or WorkKind.FixLights or WorkKind.IsolatePower
            or WorkKind.Rewire or WorkKind.BreakerOn or WorkKind.RepairRelay or WorkKind.RepairNet or WorkKind.Reconnect => WorkSfx.Zap,
        WorkKind.BuildWorkshop or WorkKind.BuildComputer or WorkKind.BuildOxygen or WorkKind.Clamp or WorkKind.ClearRubble or WorkKind.Salvage
            or WorkKind.Cannibalize or WorkKind.RepairDoor or WorkKind.CrankDoor => WorkSfx.Hammer,
        WorkKind.Fabricate or WorkKind.Recycle => WorkSfx.Drill,
        WorkKind.Cook => WorkSfx.Sizzle,
        WorkKind.Harvest or WorkKind.Tend => WorkSfx.Snip,
        WorkKind.Treat or WorkKind.FitProsthetic or WorkKind.Rehab => WorkSfx.Beep,
        WorkKind.Extinguish => WorkSfx.Spray,
        WorkKind.CleanUp or WorkKind.DiscardFood => WorkSfx.Mop,
        WorkKind.RadarWatch or WorkKind.ChangeCourse or WorkKind.PilotDrones or WorkKind.PlanRepipe or WorkKind.Calibrate or WorkKind.ManualControl
            or WorkKind.Distress or WorkKind.AnswerSignal => WorkSfx.Typing,
        _ => WorkSfx.Ratchet,
    };

    /// <summary>맡은 일이 없는 활동 → 소리 (이름으로 가른다).</summary>
    private static WorkSfx SfxFor(Activity? a, string? label) => a?.GetType().Name switch
    {
        "ResearchActivity" or "DutyActivity" or "PlanActivity" or "InfoActivity" or "StationActivity" => WorkSfx.Typing,
        "PrintActivity" => WorkSfx.Drill,
        "SurgeryActivity" or "AssistSurgeryActivity" or "TransplantOpActivity" or "HarvestActivity" or "CareRoundActivity" or "OrganCareActivity" or "RadCareActivity" => WorkSfx.Beep,
        "AltCropActivity" or "EcoActivity" => WorkSfx.Snip,
        "TidyActivity" or "SweepClipsActivity" or "WashUpActivity" or "SpaceTidyActivity" => WorkSfx.Mop,
        "MendActivity" or "ChoresActivity" or "RoomWorkActivity" or "PartTestActivity" or "CalibrateArmActivity" or "SuitMendActivity" or "AnnexWorkActivity" or "CrankActivity" => WorkSfx.Ratchet,
        _ => label != null && (label.Contains("요리") || label.Contains("조리")) ? WorkSfx.Sizzle : WorkSfx.None,
    };

    /// <summary>다음 소리까지 (초).</summary>
    private float Interval(WorkSfx k) => k switch
    {
        WorkSfx.Ratchet => 0.9f + 1.1f * (float)_wr.NextDouble(),
        WorkSfx.Weld => 1.7f + 1.6f * (float)_wr.NextDouble(),
        WorkSfx.Hammer => 1.4f + 1.4f * (float)_wr.NextDouble(),
        WorkSfx.Zap => 2.2f + 2.5f * (float)_wr.NextDouble(),
        WorkSfx.Typing => 1.0f + 1.4f * (float)_wr.NextDouble(),
        WorkSfx.Sizzle or WorkSfx.Chop => 1.6f + 1.8f * (float)_wr.NextDouble(),
        WorkSfx.Snip => 1.4f + 2.2f * (float)_wr.NextDouble(),
        WorkSfx.Beep => 1.6f + 0.6f * (float)_wr.NextDouble(),
        WorkSfx.Mop => 1.1f + 0.8f * (float)_wr.NextDouble(),
        WorkSfx.Spray => 1.3f + 0.4f * (float)_wr.NextDouble(),
        _ => 1.7f + 1.5f * (float)_wr.NextDouble(),
    };

    private static float WorkDb(WorkSfx k) => k switch
    {
        WorkSfx.Weld => -15f, WorkSfx.Hammer => -14f, WorkSfx.Spray => -12f, WorkSfx.Drill => -16f, WorkSfx.Typing => -19f,
        WorkSfx.Beep => -20f, WorkSfx.Snip => -18f, WorkSfx.Mop => -18f, WorkSfx.Sizzle => -17f, WorkSfx.Chop => -16f, _ => -16f,
    };

    private void ProcessWork(World w, double delta, bool quiet)
    {
        bool fx = Settings.Effects && !quiet && !_main.Paused && _main.SpeedIndex <= 1;
        float zoom = _main.Camera.Zoom.X;
        float near = Math.Clamp((zoom - 0.42f) / (1.2f - 0.42f), 0f, 1f);
        if (!fx || near <= 0.02f) { _workers.Clear(); return; }
        _workClock += delta;
        _workScan -= delta;
        if (_workScan <= 0)
        {
            _workScan = 0.25;
            Scan(w, zoom >= ZoomDetail.NearFrom ? 4 : 3);
        }
        foreach (var (key, pos, kind, robot) in _workers)
        {
            if (!_workNext.TryGetValue(key, out var next) || next < _workClock - 8.0)
            {
                _workNext[key] = _workClock + 0.6 * _wr.NextDouble(); // 막 보인 사람은 조금 뒤에 (한꺼번에 울리지 않게)
                continue;
            }
            if (_workClock < next) continue;
            var k = kind == WorkSfx.Sizzle && _wr.Next(3) == 0 ? WorkSfx.Chop : kind; // 요리: 가끔 도마
            if (_workWav[(int)k] is AudioStreamWav s)
            {
                Play2D(s, pos, WorkDb(k) + (robot ? -2f : 0f), Gain(pos) * (0.35f + 0.65f * near), (robot ? 1.12f : 1f) * (0.94f + 0.12f * (float)_wr.NextDouble()));
                Dsp.Heard(WorkName[(int)k] + (robot ? "(로봇)" : ""));
            }
            _workNext[key] = _workClock + Interval(k) * (robot ? 0.8f : 1f);
        }
    }

    /// <summary>화면 가운데에 가까운, 자리에서 일하는 사람 · 로봇 (진공인 방은 뺀다).</summary>
    private void Scan(World w, int take)
    {
        _workers.Clear();
        var center = GetViewport().GetVisibleRect().Size * 0.5f;
        var found = new List<(float d, int key, System.Numerics.Vector2 pos, WorkSfx kind, bool robot)>();
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Down || c.Pose != Pose.Working || c.Job is not Job job || job.Current is GotoToil) continue;
            if (c.Room == null || c.Room.Air.Pressure < 20f || !OnScreen(c.Position, 0f)) continue;
            var kind = job.Current is SprayToil ? WorkSfx.Spray : job.Order is WorkOrder o ? SfxFor(o.Kind) : SfxFor(job.Activity, job.Label);
            if (kind == WorkSfx.None) continue;
            found.Add(((ScreenOf(c.Position) - center).LengthSquared(), c.Id, c.Position, kind, false));
        }
        foreach (var r in w.Robots.Robots)
        {
            if (r.State != RobotState.Active || r.Order is not WorkOrder o || r.Disabled || r.Fault != null) continue;
            if ((r.Position - r.PreviousPosition).LengthSquared() > 1e-6f) continue; // 가는 중
            if (r.Room == null || r.Room.Air.Pressure < 20f || !OnScreen(r.Position, 0f)) continue;
            found.Add(((ScreenOf(r.Position) - center).LengthSquared(), 100_000 + r.Id, r.Position, SfxFor(o.Kind), true));
        }
        foreach (var f in found.OrderBy(f => f.d).Take(take)) _workers.Add((f.key, f.pos, f.kind, f.robot));
    }

    // ── 파형 ──

    private static float Click(Dsp.Noise n, float t, float f, float dec) => (t < 0.003f ? n.White() * 0.7f : 0f) + MathF.Exp(-t * dec) * MathF.Sin(Dsp.Tau * f * t);

    /// <summary>렌치: 래칫 딸깍 다섯 번.</summary>
    private static float[] Ratchet()
    {
        var s = Dsp.Buf(0.5f);
        var n = new Dsp.Noise(201);
        for (int k = 0; k < 5; k++)
        {
            float at = k * 0.075f + n.Range(-0.008f, 0.008f) + 0.01f, amp = 0.6f + 0.4f * n.U();
            Dsp.Add(s, at, 0.03f, t => amp * (Click(n, t, 2900f, 380f) + 0.5f * MathF.Exp(-t * 160f) * MathF.Sin(Dsp.Tau * 880f * t)));
        }
        return Dsp.Normalize(s, 0.8f);
    }

    /// <summary>용접: 지지직 튀는 불꽃 + 쉬 소리 + 낮은 웅.</summary>
    private static float[] Weld()
    {
        var s = Dsp.Buf(1.6f);
        var n = new Dsp.Noise(211);
        var low = new Dsp.Low();
        float pop = 0f;
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Dsp.Rate;
            float env = MathF.Min(1f, t / 0.05f) * MathF.Min(1f, MathF.Max(0f, 1.5f - t) / 0.1f);
            if (n.U() < 0.05f) pop += n.White() * n.U();
            pop *= 0.82f;
            float x = n.White();
            float hiss = x - low.Run(x, 2600f);
            s[i] = env * (1.3f * pop + 0.22f * hiss + 0.07f * MathF.Sign(MathF.Sin(Dsp.Tau * 100f * t)));
        }
        return Dsp.Normalize(s, 0.75f);
    }

    /// <summary>망치: 쾅 · 쾅 · 쾅 (울리는 쇠).</summary>
    private static float[] Hammer()
    {
        var s = Dsp.Buf(1.0f);
        var n = new Dsp.Noise(221);
        for (int k = 0; k < 3; k++)
        {
            float amp = k == 2 ? 1f : 0.8f;
            Dsp.Add(s, 0.02f + k * 0.31f, 0.25f, t => amp * (MathF.Exp(-t * 28f) * MathF.Sin(Dsp.Tau * 165f * t) + (t < 0.004f ? n.White() : 0f)
                + 0.28f * MathF.Exp(-t * 9f) * (MathF.Sin(Dsp.Tau * 1650f * t) + 0.6f * MathF.Sin(Dsp.Tau * 2470f * t))));
        }
        return Dsp.Normalize(s, 0.85f);
    }

    /// <summary>전기: 윙 하는 떨림 · 튀는 불꽃 · 마지막에 계전기 딸깍.</summary>
    private static float[] Zap()
    {
        var s = Dsp.Buf(0.6f);
        var n = new Dsp.Noise(231);
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Dsp.Rate;
            float on = t < 0.36f ? MathF.Min(1f, t / 0.02f) : 0f;
            float buzz = MathF.Sign(MathF.Sin(Dsp.Tau * 120f * t)) * 0.22f + 0.1f * MathF.Sin(Dsp.Tau * 240f * t);
            float spark = n.U() < 0.03f ? n.White() : 0f;
            s[i] = on * (buzz * (0.6f + 0.4f * n.U()) + spark);
        }
        Dsp.Add(s, 0.45f, 0.04f, t => Click(n, t, 1500f, 300f) * 0.7f);
        return Dsp.Normalize(s, 0.7f);
    }

    /// <summary>자판: 탁 · 탁탁 · 탁.</summary>
    private static float[] Typing()
    {
        var s = Dsp.Buf(0.95f);
        var n = new Dsp.Noise(241);
        float at = 0.01f;
        for (int k = 0; k < 8 && at < 0.85f; k++)
        {
            float amp = 0.5f + 0.5f * n.U(), f = n.Range(2200f, 3400f);
            var b = new Dsp.Band();
            var nn = new Dsp.Noise(k + 7);
            Dsp.Add(s, at, 0.025f, t => amp * (b.Run(nn.White(), f, 0.6f) * MathF.Exp(-t * 400f) * 3f + 0.5f * MathF.Exp(-t * 220f) * MathF.Sin(Dsp.Tau * 420f * t)));
            at += n.Range(0.06f, 0.13f);
        }
        return Dsp.Normalize(s, 0.6f);
    }

    /// <summary>지글지글: 기름 튀는 소리.</summary>
    private static float[] Sizzle()
    {
        var s = Dsp.Buf(2.2f);
        var n = new Dsp.Noise(251);
        var low = new Dsp.Low();
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Dsp.Rate;
            float env = MathF.Min(1f, t / 0.25f) * MathF.Min(1f, (2.2f - t) / 0.4f);
            float x = n.White();
            float hi = x - low.Run(x, 3000f);
            s[i] = env * (0.3f * hi * (0.75f + 0.25f * MathF.Sin(Dsp.Tau * 3.1f * t)) + (n.U() < 0.004f ? n.White() * 0.8f : 0f));
        }
        return Dsp.Normalize(s, 0.6f);
    }

    /// <summary>도마: 칼이 탁탁탁.</summary>
    private static float[] Chop()
    {
        var s = Dsp.Buf(0.8f);
        var n = new Dsp.Noise(261);
        for (int k = 0; k < 3; k++)
            Dsp.Add(s, 0.02f + k * 0.22f, 0.08f, t => MathF.Exp(-t * 45f) * MathF.Sin(Dsp.Tau * 230f * t) + 0.6f * MathF.Exp(-t * 70f) * MathF.Sin(Dsp.Tau * 610f * t) + (t < 0.005f ? n.White() * 0.8f : 0f));
        return Dsp.Normalize(s, 0.75f);
    }

    /// <summary>가위: 싹둑 · 싹둑.</summary>
    private static float[] Snip()
    {
        var s = Dsp.Buf(0.5f);
        var n = new Dsp.Noise(271);
        foreach (float at in new[] { 0.02f, 0.05f, 0.25f, 0.28f })
            Dsp.Add(s, at, 0.02f, t => Click(n, t, 3300f, 520f) * 0.8f);
        return Dsp.Normalize(s, 0.6f);
    }

    /// <summary>의료 감시기: 삑 · 삑.</summary>
    private static float[] MedBeep()
    {
        var s = Dsp.Buf(0.7f);
        foreach (float at in new[] { 0.01f, 0.36f })
            Dsp.Add(s, at, 0.08f, t => MathF.Min(1f, t / 0.005f) * MathF.Min(1f, (0.08f - t) / 0.01f) * MathF.Sin(Dsp.Tau * 1180f * t));
        return Dsp.Normalize(s, 0.45f);
    }

    /// <summary>걸레: 쓱 · 쓱.</summary>
    private static float[] Mop()
    {
        var s = Dsp.Buf(1.0f);
        var n = new Dsp.Noise(281);
        var b = new Dsp.Band();
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Dsp.Rate;
            float local = t < 0.5f ? t / 0.45f : (t - 0.5f) / 0.45f;
            float env = local is > 0f and < 1f ? MathF.Sin(MathF.PI * local) : 0f;
            s[i] = env * b.Run(n.White(), 600f + 900f * MathF.Sin(MathF.PI * Math.Clamp(local, 0f, 1f)), 0.7f);
        }
        return Dsp.Normalize(s, 0.6f);
    }

    /// <summary>소화기: 쉬이익 뿜는 소리.</summary>
    private static float[] Spray()
    {
        var s = Dsp.Buf(1.4f);
        var n = new Dsp.Noise(291);
        var low = new Dsp.Low();
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Dsp.Rate;
            float env = MathF.Min(1f, t / 0.04f) * MathF.Min(1f, (1.4f - t) / 0.2f);
            s[i] = env * (low.Run(n.White(), 3200f) * 1.6f + 0.15f * n.White());
        }
        return Dsp.Normalize(s, 0.8f);
    }

    /// <summary>드릴 · 가공기: 윙 올라갔다 내려오는 모터 + 갈리는 소리.</summary>
    private static float[] Drill()
    {
        var s = Dsp.Buf(1.6f);
        var n = new Dsp.Noise(301);
        double ph = 0;
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Dsp.Rate;
            float env = MathF.Min(1f, t / 0.15f) * MathF.Min(1f, (1.6f - t) / 0.25f);
            float f = 95f + 40f * MathF.Min(1f, t / 0.3f) - 15f * MathF.Max(0f, t - 1.2f);
            ph += f / Dsp.Rate;
            float saw = 2f * (float)(ph - Math.Floor(ph)) - 1f;
            s[i] = env * (0.35f * saw + 0.15f * MathF.Sin((float)(ph * Dsp.Tau * 3)) + 0.25f * n.White() * (0.5f + 0.5f * MathF.Sin((float)(ph * Dsp.Tau))));
        }
        return Dsp.Normalize(s, 0.6f);
    }
}
