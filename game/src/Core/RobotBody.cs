using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v22 기계의 상처 (1) — 로봇 몸의 부위: 사람 몸처럼 부위마다 따로 닳고 · 찌그러지고 · 떨어져 나간다.
//   몸통 · 구동부(궤도 · 바퀴) · 왼팔 · 오른팔 · 센서 · 제어기 · 동력부 · 도구 (팔은 팔 달린 종류만).
//   단계: 멀쩡 → 긁힘 → 찌그러짐 → 망가짐 → 떨어져 나감.
//   망가진 부위가 그 일을 막는다: 구동부 → 절뚝 · 팔 → 집는 일이 느리고 둘 다 잃으면 못 한다 · 센서 → 헤맨다 · 제어기 → 손이 굼뜨다 ·
//   동력부 → 전지가 샌다 · 도구 → 특기가 느리고 잃으면 특기를 못 한다.
//   무엇에 다치나: 불(센서 · 도구 · 몸통) · 폭발과 파편(아무 데나 크게) · 운석 충돌 · 물(구동부 · 동력부) · 진공(센서) ·
//   뜨겁고 찬 방(동력부 · 제어기 · 구동부) · 방사선(제어기) · 닳음(고장마다 그 부위). 몸통이 떨어져 나가면 부서진다.
//   고친 자리엔 덧댄 판이 남는다 — 몸에 남는 흉터 (연출 · 이력).

public enum BotPart : byte { Frame, Drive, ArmL, ArmR, Sensor, Brain, Power, Tool }
public enum PartStage : byte { Ok, Scratched, Dented, Broken, Lost }

public sealed partial class Robot
{
    /// <summary>v22 부위 상태 0~1 (BotPart 순서). 0이면 떨어져 나갔다 — 몸통이 0이면 부서진다.</summary>
    public float[] Parts { get; internal set; } = RobotBody.Fresh();
    /// <summary>v22 부위마다 덧댄 수리판 (고친 자리 — 몸에 남는 흉터).</summary>
    public int[] Patches { get; internal set; } = new int[RobotBody.Count];
    /// <summary>v22 지금 고장이 난 부위 (-1 없음).</summary>
    public int FaultPart { get; internal set; } = -1;
    /// <summary>v22 가장 최근에 크게 다친 일 (카드 · 연대기).</summary>
    public string LastHurt { get; internal set; } = "";
}

public sealed class RobotBodyStats
{
    public int Hits, Stages, Lost, Patched, Blasts, Impacts, Env, Wrecks, Limps, Gripless;
    public string Line() => $"부위 피해 {Hits} (단계 넘음 {Stages} · 떨어져 나감 {Lost}) · 덧댄 판 {Patched} · 폭발 {Blasts} · 충돌 {Impacts} · 환경 {Env} · 부서짐 {Wrecks} · 절뚝 {Limps} · 집지 못함 {Gripless}";
}

public static class RobotBody
{
    public const int Count = 8;
    public static readonly BotPart[] All = { BotPart.Frame, BotPart.Drive, BotPart.ArmL, BotPart.ArmR, BotPart.Sensor, BotPart.Brain, BotPart.Power, BotPart.Tool };

    public static float[] Fresh() { var a = new float[Count]; Array.Fill(a, 1f); return a; }

    /// <summary>팔 달린 종류 (정비 · 재배 바탕 · 간호 · 의사 · 조리 · 구조 · 지게 · 배관 · 봉합).</summary>
    public static bool HasArms(RobotKind k) => RobotsV15.Base(k) is RobotKind.Maintainer or RobotKind.Gardener
        || k is RobotKind.Nurse or RobotKind.Medic or RobotKind.Chef or RobotKind.Rescuer or RobotKind.Lifter or RobotKind.Plumber or RobotKind.Sealer;

    public static bool Has(RobotKind k, BotPart p) => p is not (BotPart.ArmL or BotPart.ArmR) || HasArms(k);

    public static PartStage Stage(float hp) => hp <= 0f ? PartStage.Lost : hp < 0.25f ? PartStage.Broken : hp < 0.5f ? PartStage.Dented : hp < 0.75f ? PartStage.Scratched : PartStage.Ok;
    public static PartStage StageOf(Robot r, BotPart p) => Has(r.Kind, p) ? Stage(r.Parts[(int)p]) : PartStage.Ok;

    public static string StageName(PartStage s) => s switch
    {
        PartStage.Ok => "멀쩡",
        PartStage.Scratched => "긁힘",
        PartStage.Dented => "찌그러짐",
        PartStage.Broken => "망가짐",
        _ => "떨어져 나감",
    };

    public static string Name(RobotKind k, BotPart p) => p switch
    {
        BotPart.Frame => "몸통",
        BotPart.Drive => RobotsV15.Base(k) is RobotKind.Maintainer or RobotKind.Safety ? "궤도" : "바퀴",
        BotPart.ArmL => "왼팔",
        BotPart.ArmR => "오른팔",
        BotPart.Sensor => "센서",
        BotPart.Brain => "제어기",
        BotPart.Power => RobotsV21.Reactor ? "소형 원자로" : "전지",
        _ => ToolName(k),
    };

    public static string ToolName(RobotKind k) => k switch
    {
        RobotKind.Stretcher => "침상",
        RobotKind.Nurse or RobotKind.Medic => "의료 도구",
        RobotKind.Cleaner => "솔",
        RobotKind.Chef => "조리 도구",
        RobotKind.Plumber => "배관 공구",
        RobotKind.Sealer => "봉합 노즐",
        RobotKind.Airman or RobotKind.Ventman => "송풍기",
        _ => RobotsV15.Base(k) switch
        {
            RobotKind.Safety => "소화 노즐",
            RobotKind.Gardener => "물뿌리개",
            RobotKind.Maintainer => "공구함",
            _ => "짐칸",
        },
    };

    /// <summary>고장이 난 부위 (그 고장을 고치면 그 부위가 돌아온다).</summary>
    public static BotPart PartFor(RobotFault f, Robot r, Rng rng) => f switch
    {
        RobotFault.Sensor => BotPart.Sensor,
        RobotFault.Jam => HasArms(r.Kind) ? (r.Parts[(int)BotPart.ArmL] <= r.Parts[(int)BotPart.ArmR] ? BotPart.ArmL : BotPart.ArmR) : BotPart.Tool,
        RobotFault.Overheat or RobotFault.Cell => BotPart.Power,
        RobotFault.Drive => BotPart.Drive,
        RobotFault.Controller => BotPart.Brain,
        _ => rng.Chance(0.5f) ? BotPart.Frame : rng.Chance(0.5f) ? BotPart.Sensor : BotPart.Tool, // 그을림
    };

    /// <summary>망가진 부위가 일으키는 고장.</summary>
    public static RobotFault FaultFor(BotPart p) => p switch
    {
        BotPart.Drive => RobotFault.Drive,
        BotPart.Sensor => RobotFault.Sensor,
        BotPart.Brain => RobotFault.Controller,
        BotPart.Power => RobotFault.Cell,
        BotPart.Frame => RobotFault.Scorched,
        _ => RobotFault.Jam,
    };

    // ─────────────────────────────── 효과 ───────────────────────────────

    /// <summary>걸음: 구동부가 상하면 절뚝이고 · 센서가 망가지면 더듬고 · 몸통이 찢기면 조심한다.</summary>
    public static float SpeedMul(Robot r)
    {
        var a = r.Parts;
        float d = a[(int)BotPart.Drive];
        float m = d <= 0f ? 0.25f : d < 0.25f ? 0.45f : d < 0.5f ? 0.7f : d < 0.75f ? 0.9f : 1f;
        if (a[(int)BotPart.Sensor] < 0.25f) m *= 0.8f;
        if (a[(int)BotPart.Frame] < 0.25f) m *= 0.85f;
        return m;
    }

    /// <summary>일 시간 배율 (클수록 느리다): 팔 · 도구 · 센서 · 제어기.</summary>
    public static float WorkMul(Robot r)
    {
        var a = r.Parts;
        float m = 1f;
        if (HasArms(r.Kind))
        {
            float l = a[(int)BotPart.ArmL], rr = a[(int)BotPart.ArmR];
            if (l <= 0f ^ rr <= 0f) m *= 1.6f; // 한 팔로
            if (l > 0f && l < 0.25f) m *= 1.3f;
            if (rr > 0f && rr < 0.25f) m *= 1.3f;
        }
        float t = a[(int)BotPart.Tool];
        m *= t <= 0f ? 2.2f : t < 0.25f ? 1.5f : t < 0.5f ? 1.15f : 1f;
        if (a[(int)BotPart.Sensor] < 0.25f) m *= 1.25f;
        if (a[(int)BotPart.Brain] < 0.25f) m *= 1.2f;
        return m;
    }

    /// <summary>전지가 새는 배율 (동력부가 상하면).</summary>
    public static float DrainMul(Robot r)
    {
        float p = r.Parts[(int)BotPart.Power];
        return p < 0.25f ? 1.8f : p < 0.5f ? 1.3f : 1f;
    }

    /// <summary>두 팔을 다 잃었다 — 집는 일을 못 한다.</summary>
    public static bool Gripless(Robot r) => HasArms(r.Kind) && r.Parts[(int)BotPart.ArmL] <= 0f && r.Parts[(int)BotPart.ArmR] <= 0f;

    /// <summary>특기 도구를 잃었다 — 특기 일을 못 한다.</summary>
    public static bool ToolLost(Robot r) => r.Parts[(int)BotPart.Tool] <= 0f;

    /// <summary>손이 드는 일 (팔이 없으면 못 한다 — 나르기 · 순찰 · 밸브 · 차단기는 된다).</summary>
    public static bool HandJob(WorkKind k) => k is not (WorkKind.Restock or WorkKind.StockDock or WorkKind.CarryWater or WorkKind.StockCache or WorkKind.StowCot
        or WorkKind.ResetBreaker or WorkKind.CloseValve or WorkKind.OpenValve or WorkKind.PreventiveCheck or WorkKind.Refuel or WorkKind.RefillCoolant);

    /// <summary>가장 상한 부위의 상태.</summary>
    public static float Worst(Robot r)
    {
        float w = 1f;
        foreach (var p in All) if (Has(r.Kind, p)) w = MathF.Min(w, r.Parts[(int)p]);
        return w;
    }

    /// <summary>상한(긁힘 아래) 부위 수.</summary>
    public static int Damaged(Robot r) => All.Count(p => Has(r.Kind, p) && r.Parts[(int)p] < 0.75f);

    /// <summary>덧댄 판 모두.</summary>
    public static int PatchTotal(Robot r) { int n = 0; foreach (var x in r.Patches) n += x; return n; }

    /// <summary>카드 한 줄: 상한 부위만 (멀쩡하면 "멀쩡").</summary>
    public static string Line(Robot r)
    {
        var bad = All.Where(p => Has(r.Kind, p) && r.Parts[(int)p] < 0.75f).Select(p => $"{Name(r.Kind, p)} {StageName(Stage(r.Parts[(int)p]))}").ToList();
        int patches = PatchTotal(r);
        return (bad.Count == 0 ? "멀쩡" : string.Join(" · ", bad)) + (patches > 0 ? $" · 덧댄 판 {patches}" : "");
    }
}

public sealed partial class RobotSystem
{
    public RobotBodyStats Body { get; } = new();
    private Rng? _bodyRng;
    private Rng RB => _bodyRng ??= new Rng(unchecked(_world.Seed * 7919 + 77));
    private long _bodyLast = -1;
    private bool _bodyBreaking;

    /// <summary>
    /// 부위를 다친다: 단계를 넘으면 몸에 남고 · 망가지면 그 부위 고장 · 떨어져 나가면 크게 남고 · 몸통이 떨어져 나가면 부서진다.
    /// </summary>
    public void Hurt(Robot r, BotPart p, float dmg, string cause)
    {
        var w = _world;
        if (dmg <= 0f || r.Wrecked || r.State == RobotState.Lost) return;
        if (!RobotBody.Has(r.Kind, p)) p = BotPart.Frame;
        int i = (int)p;
        float before = r.Parts[i];
        if (before <= 0f) { if (p != BotPart.Frame) Hurt(r, BotPart.Frame, dmg * 0.5f, cause); return; } // 이미 없는 부위 — 몸통이 대신 맞는다
        float after = MathF.Max(0f, before - dmg);
        r.Parts[i] = after;
        Body.Hits++;
        var s0 = RobotBody.Stage(before);
        var s1 = RobotBody.Stage(after);
        if (s1 == s0) return;
        Body.Stages++;
        string part = RobotBody.Name(r.Kind, p);
        r.LastHurt = $"{part} {RobotBody.StageName(s1)} — {cause}";
        MarkLog.Add(r.Marks, w.Tick, $"{part} {RobotBody.StageName(s1)} ({cause})");
        if (p == BotPart.Drive && s1 >= PartStage.Dented && s0 < PartStage.Dented) Body.Limps++;
        if (s1 == PartStage.Lost)
        {
            Body.Lost++;
            if (p == BotPart.Frame)
            {
                Body.Wrecks++;
                Wreck(r, $"{cause}에 몸통이 부서졌다");
                return;
            }
            if (RobotBody.Gripless(r)) Body.Gripless++;
            w.Log.Add(w.Tick, LogKind.Warning, $"{r.Name}의 {Ko.IGa(part)} 떨어져 나갔다 — {cause} ({r.Room?.Name ?? "?"})");
        }
        // 망가지거나 떨어져 나갔다 — 그 부위 고장 (이미 고장이면 고친 뒤 다음 분에 이어서)
        if (s1 >= PartStage.Broken && r.Fault == null && !_bodyBreaking && r.State is RobotState.Active or RobotState.Docked)
        {
            r.FaultPart = i;
            _bodyBreaking = true;
            try { Break(r, RobotBody.FaultFor(p)); } finally { _bodyBreaking = false; }
        }
    }

    /// <summary>한 대에 고루 퍼지는 큰 충격 (폭발 · 파편 · 충돌): 무게대로 부위를 골라 맞고, 몸통도 조금.</summary>
    public void Shake(Robot r, float dmg, string cause)
    {
        if (dmg <= 0f) return;
        Span<float> wt = stackalloc float[RobotBody.Count];
        wt[(int)BotPart.Frame] = 3f; wt[(int)BotPart.Drive] = 2f; wt[(int)BotPart.ArmL] = 1.5f; wt[(int)BotPart.ArmR] = 1.5f;
        wt[(int)BotPart.Sensor] = 2f; wt[(int)BotPart.Brain] = 1f; wt[(int)BotPart.Power] = 1f; wt[(int)BotPart.Tool] = 1.5f;
        float sum = 0f;
        for (int k = 0; k < RobotBody.Count; k++) { if (!RobotBody.Has(r.Kind, (BotPart)k)) wt[k] = 0f; sum += wt[k]; }
        float x = RB.Float() * sum;
        int pick = 0;
        for (int k = 0; k < RobotBody.Count; k++) { x -= wt[k]; if (x <= 0f) { pick = k; break; } }
        Hurt(r, (BotPart)pick, dmg, cause);
        if (pick != (int)BotPart.Frame && !r.Wrecked) Hurt(r, BotPart.Frame, dmg * 0.3f, cause);
    }

    /// <summary>고장이 났다 (닳음 · 불 · 원자로 …): 그 부위가 망가진 단계까지 내려온다 (가벼운 고장은 찌그러짐).</summary>
    private void BodyOnFault(Robot r, RobotFault f)
    {
        if (_bodyBreaking && r.FaultPart >= 0) return; // 부위가 먼저 망가져 난 고장
        var p = RobotBody.PartFor(f, r, RB);
        if (!RobotBody.Has(r.Kind, p)) p = BotPart.Frame;
        int i = (int)p;
        r.FaultPart = i;
        float cap = Minor(f) ? 0.45f : 0.2f;
        if (r.Parts[i] > cap) r.Parts[i] = cap;
    }

    /// <summary>고쳤다: 고장 난 부위가 돌아온다 (사람 · 동료가 제대로 고치면 덧댄 판이 남고, 스스로는 다시 맞추기만).</summary>
    private void BodyOnRepair(Robot r, bool self, bool makeshift)
    {
        int i = r.FaultPart;
        r.FaultPart = -1;
        if (i < 0) return;
        float to = self ? 0.55f : makeshift ? 0.5f : 0.85f;
        bool replaced = r.Parts[i] <= 0f;
        if (r.Parts[i] < to) r.Parts[i] = to;
        if (!self || replaced)
        {
            r.Patches[i]++;
            Body.Patched++;
            MarkLog.Add(r.Marks, _world.Tick, $"{RobotBody.Name(r.Kind, (BotPart)i)}에 {(replaced ? "새 부품을 달았다" : "판을 덧댔다")}");
        }
    }

    /// <summary>정비 (사람 · 동료가 조이고 닦기): 긁히고 찌그러진 부위가 조금 돌아온다 (망가지거나 떨어져 나간 건 수리로만).</summary>
    private void BodyOnService(Robot r, float to)
    {
        foreach (var p in RobotBody.All)
        {
            int i = (int)p;
            if (RobotBody.Has(r.Kind, p) && r.Parts[i] >= 0.25f && r.Parts[i] < to) r.Parts[i] = MathF.Min(to, r.Parts[i] + 0.2f);
        }
    }

    /// <summary>
    /// 매 분: 그사이 터진 폭발 · 떨어진 운석 곁의 로봇이 다치고 · 물 · 진공 · 뜨겁고 찬 방 · 방사선이 부위를 깎고 ·
    /// 고장을 고친 뒤에도 망가진 부위가 남았으면 그 부위 고장으로 이어진다.
    /// </summary>
    private void BodyMinute()
    {
        var w = _world;
        long since = _bodyLast < 0 ? w.Tick - SimTime.Minutes(1) : _bodyLast;
        _bodyLast = w.Tick;
        const float dt = 1f / 60f;
        // 폭발
        foreach (var b in w.Volatile.Blasts)
        {
            if (b.Tick <= since) continue;
            float reach = 2f + 3f * b.Power;
            foreach (var r in Robots)
            {
                if (r.Disabled || r.Wrecked || r.State == RobotState.Lost) continue;
                float d = (r.Position - b.At.Center).Length();
                if (d > reach) continue;
                float dmg = MathF.Min(1.2f, b.Power * 0.9f * (1f - d / reach));
                if (dmg < 0.03f) continue;
                Body.Blasts++;
                Shake(r, dmg, $"폭발 ({b.Cause})");
            }
        }
        // 운석 · 파편 충돌
        foreach (var im in w.Impacts)
        {
            if (im.Tick <= since) continue;
            foreach (var r in Robots)
            {
                if (r.Disabled || r.Wrecked || r.State == RobotState.Lost) continue;
                float d = (r.Position - im.Target.Center).Length();
                if (d > 2.5f) continue;
                Body.Impacts++;
                Shake(r, im.Size * 0.8f * (1f - d / 2.5f), "파편 충돌");
            }
        }
        // 방의 환경 · 이어지는 고장
        foreach (var r in Robots)
        {
            if (r.Disabled || r.Wrecked || r.State == RobotState.Lost || r.Room is not Room room) continue;
            if (room.Flood > 40f) Env(r, BotPart.Drive, 0.6f * MathF.Min(1f, room.Flood / 100f) * dt, "물에 잠겨"); // 물이 차면 바퀴 · 궤도부터
            if (room.Flood > 80f) Env(r, BotPart.Power, 0.4f * dt, "동력부에 물이 들어");
            if (room.Air.Pressure < 20f) Env(r, BotPart.Sensor, 0.15f * dt, "진공에 센서가 얼어");
            float t = room.Air.Temperature;
            if (t > 70f) { Env(r, BotPart.Power, 0.012f * (t - 70f) * dt, "열에 동력부가 달아"); Env(r, BotPart.Brain, 0.008f * (t - 70f) * dt, "열에 제어기가 달아"); }
            else if (t < -30f) { Env(r, BotPart.Drive, 0.01f * (-30f - t) * dt, "추위에 관절이 굳어"); Env(r, BotPart.Power, 0.006f * (-30f - t) * dt, "추위에 동력이 떨어져"); }
            if (room.Radiation > 0.5f) Env(r, BotPart.Brain, 0.12f * room.Radiation * dt, "방사선에 회로가 상해");
            // 고친 뒤에도 망가진 부위가 남았다 — 그 부위 고장으로 다시 (부위 하나씩)
            if (r.Fault == null && r.Operational && r.State is RobotState.Active or RobotState.Docked && !r.Upgrading)
                foreach (var p in RobotBody.All)
                {
                    int i = (int)p;
                    if (!RobotBody.Has(r.Kind, p) || r.Parts[i] >= 0.25f) continue;
                    r.FaultPart = i;
                    _bodyBreaking = true;
                    try { Break(r, RobotBody.FaultFor(p)); } finally { _bodyBreaking = false; }
                    break;
                }
        }
    }

    private void Env(Robot r, BotPart p, float dmg, string cause)
    {
        if (dmg <= 0f) return;
        Body.Env++;
        Hurt(r, p, dmg, cause);
    }
}
