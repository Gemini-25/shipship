using System.Runtime.InteropServices;

namespace ShipEngine;

/// <summary>로봇 · 드론의 몸 (⑥ — 움직이는 장치): 자리 · 전지 · 부위 넷의 상함(0~1) · 부위마다 원인 · 마음이 요청한 힘씀 · 충전독에 붙음.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RobotBody
{
    public int Cell;
    public float Battery;
    public float Legs, Arms, Eyes, Core;            // 다리 · 팔 · 눈(센서) · 속(전자 부품 — 1이면 멈춘다)
    public int LegsCause, ArmsCause, EyesCause, CoreCause;
    public byte Load;                                // 마음의 요청: 0 쉼 · 1 움직임 · 일
    public byte Docked;                              // 마음의 요청: 충전독에 붙었다
    public float ImpactSeen;                         // 그 칸에서 이미 받은 충격 에너지 (구조의 장부는 몇 틱 쌓인다)
    public long ShockAt;                             // 마지막 감전 (1분에 한 번)
    public int PropId;                               // ④ 제 힘을 잃은 동안의 물체 Id (0 없음 — docs/PHYSICS.md PH14)
}

public enum RobotPart : byte { Legs, Arms, Eyes, Core }

/// <summary>
/// ⑥ 로봇 몸 (판단 이관 (사) — 몸 · 기계의 손상은 월드가 · docs/ENGINE.md): 로봇의 마음(⑧)은 어디 있는지 · 얼마나 힘쓰는지 · 충전독에 붙었는지만 요청하고,
/// 전지 · 상함은 세상이 계산한다. 마음은 그 몸을 느끼고 대응만 한다 (느린 다리 · 쓸 수 없는 팔 · 흐린 눈 · 멈춤).
/// <para>전지: 쉼 시간당 2% · 움직이거나 일하면 8% · 돌아가는 충전독에 붙어 있으면 +40% (독에 장치가 없으면 늘 돈다).
/// 열: 겉면 600K · 공기 450K 넘는 칸 — 속 10분에 다 · 눈 5분에 다. 충격: 구조가 그 칸에 받은 충격 에너지 — 다리 e/16kJ · 팔 e/24kJ · 속 e/48kJ.
/// 물: 바닥 물 3cm 넘게 서 있으면 다리 봉함이 새 한 시간에 다 · 5mm 넘는 물 위에서 그 칸에 전기가 흐르면 감전 (속 +0.3 · 다리 +0.2 — 1분에 한 번).
/// 진공: 5kPa 아래에서 힘을 쓰면 열을 못 버려 속이 여섯 시간에 다 (쉬면 괜찮다). 부위가 0.5를 넘거나 속이 1이 되면 사실 '고장'.</para>
/// </summary>
public sealed partial class Network
{
    public const double RobotIdleDrain = 0.02, RobotBusyDrain = 0.08, RobotCharge = 0.4;   // 시간당
    public const double RobotHotSurfaceK = 600, RobotHotAirK = 450;
    public const double RobotWadeMol = 30 * 55.5, RobotWetMol = 5 * 55.5;                   // 3cm · 5mm (칸 1m²)
    public const double RobotVacuumKPa = 5;
    public const float RobotHandsGone = 0.5f;

    // ── 상태 (찍는다) ──
    public List<RobotBody> Robots = new();
    public long RobotShocks, RobotPartsLost;

    /// <summary>로봇 몸 하나를 놓는다 (마음이 로봇을 들일 때) — 몸 번호.</summary>
    public int AddRobot(int cell)
    {
        Robots.Add(new RobotBody { Cell = cell, Battery = 1, ShockAt = long.MinValue / 2, ImpactSeen = cell >= 0 && _stc != null ? _stc.ImpactJ[cell] : 0 });
        return Robots.Count - 1;
    }

    public ref RobotBody Robot(int id) => ref CollectionsMarshal.AsSpan(Robots)[id];

    /// <summary>다리 — 걷는 빠르기의 몫 (1이면 못 걷는다).</summary>
    public static double RobotPace(in RobotBody b) => b.Legs >= 1 ? 0 : 1 - 0.7 * b.Legs;
    /// <summary>팔 — 손일을 할 수 있나.</summary>
    public static bool RobotHands(in RobotBody b) => b.Arms < RobotHandsGone;
    /// <summary>눈 — 보는 거리의 몫.</summary>
    public static double RobotSight(in RobotBody b) => 1 - 0.8 * Math.Min(1, b.Eyes);

    public static string PartName(RobotPart p) => p switch { RobotPart.Legs => "다리", RobotPart.Arms => "팔", RobotPart.Eyes => "눈", _ => "속" };

    private void RobotStep(Sim sim, double dt)
    {
        if (Robots.Count == 0) return;
        var span = CollectionsMarshal.AsSpan(Robots);
        var chem = sim.Find<Chemistry>();
        double h = dt / 3600;
        for (int i = 0; i < span.Length; i++)
        {
            ref var b = ref span[i];
            int c = b.Cell;
            if (b.Core >= 1 || c < 0 || c >= L.N) continue;
            // 전지
            if (b.Docked != 0 && DockAt(c)) b.Battery = (float)Math.Min(1, b.Battery + RobotCharge * h);
            else if (b.Battery > 0) b.Battery = (float)Math.Max(0, b.Battery - (b.Load != 0 ? RobotBusyDrain : RobotIdleDrain) * h);
            if (b.Battery <= 0) continue;   // 꺼진 몸은 힘을 쓰지 않는다 (열 · 충격은 그래도 — 아래는 켜진 몸만 본다: 꺼진 로봇은 그 자리에 굳는다)
            // 열
            if (F.HasSurface(c) && F.Tf[c] > RobotHotSurfaceK || F.Temp(c) > RobotHotAirK)
            {
                int why = HeatSource(chem, c);
                Harm(sim, i, ref b, RobotPart.Eyes, (float)(dt / 300), why, "뜨거운 열기에 로봇의 센서가 흐려졌다");
                Harm(sim, i, ref b, RobotPart.Core, (float)(dt / 600), why, "뜨거운 열기에 로봇의 속이 달았다");
            }
            // 충격 (구조의 장부는 생명이 다섯 틱마다 비운다 — 늘어난 만큼만)
            if (_stc != null)
            {
                float e = _stc.ImpactJ[c];
                if (e < b.ImpactSeen) b.ImpactSeen = 0;
                float de = e - b.ImpactSeen;
                b.ImpactSeen = e;
                if (de > 0)
                {
                    int why = _stc.DamageCause[c];
                    Harm(sim, i, ref b, RobotPart.Legs, de / 16000f, why, "충격에 로봇의 다리가 상했다");
                    Harm(sim, i, ref b, RobotPart.Arms, de / 24000f, why, "충격에 로봇의 팔이 상했다");
                    Harm(sim, i, ref b, RobotPart.Core, de / 48000f, why, "충격에 로봇의 속이 상했다");
                }
            }
            // 물 · 전기
            double water = F.Stored(T.sWater, c);
            if (water > RobotWadeMol) Harm(sim, i, ref b, RobotPart.Legs, (float)(dt / 3600), SpillNear(c), "물에 잠긴 로봇 다리의 봉함이 샜다");
            if (water > RobotWetMol && sim.Tick - b.ShockAt >= Time.Minutes(1) && LiveAt(c))
            {
                b.ShockAt = sim.Tick; RobotShocks++;
                int why = sim.Causes.Add(CauseKind.Electric, "물이 고인 바닥에 전기가 흘러 로봇이 감전됐다", sim.Tick, c, SpillNear(c));
                Harm(sim, i, ref b, RobotPart.Core, 0.3f, why, "");
                Harm(sim, i, ref b, RobotPart.Legs, 0.2f, why, "");
            }
            // 진공: 힘을 쓰면 열을 못 버린다
            if (b.Load != 0 && F.Pressure(c) < RobotVacuumKPa)
                Harm(sim, i, ref b, RobotPart.Core, (float)(dt / (6 * 3600)), 0, "진공에서 열을 못 버려 로봇의 속이 달았다");
        }
    }

    /// <summary>부위 하나가 상한다 — 처음 상한 원인을 남긴다 · 0.5를 넘거나(속은 1) 사실 '고장'.</summary>
    private void Harm(Sim sim, int id, ref RobotBody b, RobotPart part, float amount, int parent, string text)
    {
        if (amount <= 0) return;
        float before = PartOf(ref b, part);
        float v = Math.Min(1, before + amount);
        int why = PartCause(ref b, part);
        if (why == 0 || before == 0) why = text.Length > 0 ? sim.Causes.Add(CauseKind.Machine, text, sim.Tick, b.Cell, parent) : parent;
        SetPart(ref b, part, v, why);
        float edge = part == RobotPart.Core ? 1 : 0.5f;
        if (before < edge && v >= edge)
        {
            RobotPartsLost++;
            sim.Facts.Emit(sim.Tick, FactKind.MachineFail, b.Cell, (float)part, why, other: id);
        }
    }

    private static float PartOf(ref RobotBody b, RobotPart p) => p switch { RobotPart.Legs => b.Legs, RobotPart.Arms => b.Arms, RobotPart.Eyes => b.Eyes, _ => b.Core };
    private static int PartCause(ref RobotBody b, RobotPart p) => p switch { RobotPart.Legs => b.LegsCause, RobotPart.Arms => b.ArmsCause, RobotPart.Eyes => b.EyesCause, _ => b.CoreCause };
    private static void SetPart(ref RobotBody b, RobotPart p, float v, int why)
    {
        switch (p)
        {
            case RobotPart.Legs: b.Legs = v; b.LegsCause = why; break;
            case RobotPart.Arms: b.Arms = v; b.ArmsCause = why; break;
            case RobotPart.Eyes: b.Eyes = v; b.EyesCause = why; break;
            default: b.Core = v; b.CoreCause = why; break;
        }
    }

    /// <summary>그 칸이 돌아가는 충전독인가 (독에 장치가 없으면 늘 돈다).</summary>
    private bool DockAt(int cell)
    {
        int fu = L.FurnitureAt[cell];
        foreach (var d in Devices) if (d.Furniture == fu) return Working(d.Id);
        return true;
    }

    private void SaveRobots(StateWriter w) { w.List(Robots); w.I64(RobotShocks); w.I64(RobotPartsLost); }
    private void LoadRobots(StateReader r) { Robots = r.List<RobotBody>(); RobotShocks = r.I64(); RobotPartsLost = r.I64(); }
}
