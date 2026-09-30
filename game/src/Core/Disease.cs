using System;
using System.Linq;

namespace ShipSim.Core;

// v12.4 전염병: 같은 방에 있으면 옮는다 (환기가 돌면 덜, 치료 침대에 누워 있으면 덜, 우주복은 막는다).
// 잠복 → 열이 오르고 지친다 → 가장 아플 때 쓰러질 수도 → 낫고 면역. 누가 누구에게 옮겼는지가 인과 사슬에 남는다.

public sealed class DiseaseStats
{
    public int Infections, Recovered, Deaths, PatientZero;
    public override string ToString() => $"감염 {Infections}(처음 {PatientZero}) · 회복 {Recovered} · 사망 {Deaths}";
}

public sealed class DiseaseSystem
{
    private readonly World _w;
    public DiseaseStats Stats { get; } = new();
    public DiseaseSystem(World w) => _w = w;

    public static bool Sick(CrewMember c) => c.InfectedAt >= 0;

    /// <summary>앓는 정도 0~1: 반나절 잠복 → 이틀째 가장 아프고 → 나흘쯤 낫는다 (치료 침대에 누우면 빨리).</summary>
    public float Severity(CrewMember c)
    {
        if (c.InfectedAt < 0) return 0f;
        float days = (_w.Tick - c.InfectedAt) / (float)SimTime.TicksPerDay - c.CureDays;
        if (days < 0.5f) return 0.1f * days / 0.5f;
        float t = (days - 0.5f) / 3.5f;
        return MathF.Max(0f, c.DiseasePeak * MathF.Sin(MathF.PI * MathF.Min(1f, t)));
    }

    /// <summary>옮긴다 (from이 없으면 처음 걸린 사람 — 보급품·물·먼지에서).</summary>
    public void Infect(CrewMember c, CrewMember? from)
    {
        var w = _w;
        if (c.Dead || c.InfectedAt >= 0 || c.Immune) return;
        c.InfectedAt = w.Tick;
        c.CureDays = 0f;
        c.DiseasePeak = 0.45f + 0.5f * w.Rng.Float() * (1.1f - 0.4f * c.Vitals.Health);
        Stats.Infections++;
        if (from == null) Stats.PatientZero++;
        int parent = from != null ? w.Causes.OpenNode($"ill:{from.Id}") : -1;
        w.Causes.Effect(CauseKind.Illness, $"ill:{c.Id}", from != null ? $"{c.Name} 감염 ({from.Name}에게서 · {c.Room?.Name ?? "?"})" : $"{c.Name} 감염 — 처음 걸린 사람", c.Room, c.Position, parent);
        MarkLog.Add(c.Memory.Marks, w.Tick, from != null ? $"{from.Name}에게서 병이 옮았다" : "열이 나기 시작했다");
    }

    public void Update(float dt)
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead && c.InfectedAt >= 0) { Stats.Deaths++; c.InfectedAt = -1; continue; }
            if (c.Dead || c.InfectedAt < 0) continue;
            float s = Severity(c);
            // 치료 침대: 낫는 속도가 두 배
            if (c.CareBed != null) c.CureDays += dt / 24f;
            // 증상: 지치고, 예민해지고, 아주 아프면 몸이 버티지 못한다
            if (s > 0.3f)
            {
                c.Needs.Rest = MathF.Max(0f, c.Needs.Rest - 0.06f * s * dt);
                c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.02f * s * dt);
                if (w.Rng.Chance(0.4f * dt)) NeedsSystem.AddInjury(c.Vitals, 0.03f * s, "열병"); // 의무실로 가게 한다
            }
            if (s > 0.75f && c.CareBed == null) c.Vitals.Health = MathF.Max(0f, c.Vitals.Health - 0.015f * (s - 0.7f) * 4f * dt);
            // 낫는다
            float days = (w.Tick - c.InfectedAt) / (float)SimTime.TicksPerDay - c.CureDays;
            if (days > 4f)
            {
                c.InfectedAt = -1;
                c.Immune = true;
                Stats.Recovered++;
                int n = w.Causes.OpenNode($"ill:{c.Id}");
                if (n >= 0) w.Causes.Resolve(n, $"{c.Name} 나았다 (면역)", $"crew:{c.Id}");
                w.Log.Add(w.Tick, LogKind.Life, "열이 내렸다 — 다 나았다", c.Id);
                continue;
            }
            // 옮는다: 같은 방, 잠복이 끝난 뒤부터
            if (days < 0.3f || c.Outside || c.Room == null) continue;
            foreach (var d in w.Crew)
            {
                if (d == c || d.Dead || d.Outside || d.Room != c.Room || d.InfectedAt >= 0 || d.Immune || d.Suit != null) continue;
                float p = 0.5f * MathF.Max(0.15f, s) * dt
                          * (Atmosphere.Vented(c.Room) ? 0.6f : 1.3f)
                          * (c.CareBed != null ? 0.3f : 1f)
                          * (c.Room.Type == RoomType.Medbay ? 0.6f : 1f)
                          * (w.History.Doctrine.Quarantine ? 0.5f : 1f); // 교훈: 격리 수칙
                if (w.Rng.Chance(p)) Infect(d, c);
            }
        }
    }
}
