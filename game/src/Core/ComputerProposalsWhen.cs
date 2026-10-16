using System.Linq;

namespace ShipSim.Core;

// v16.24 컴퓨터 제안을 언제 정하나 — 완전 관전: 기한까지 기다리지 않고 함장(지휘하는 사람) · 회의가 자연스러운 때 정한다.
//  · 급하다(진공 · 불활성 가스처럼 위험하거나, 기한이 몇 분뿐이거나, 배 전체 사고 중): 바로 정한다.
//  · 함장이 함교 · 컴퓨터실 · 통신실 단말 앞에 있다: 제안을 보고(그 자리에서 몇 분 생각) 정한다.
//  · 회의 중이고 함장이 회의에 있다: 안건으로 올려 그 자리에서 정한다.
//  · 아니면 기한까지 기다린다 (기한이 지나면 예전처럼 정한다).
// 지나온 과정(올라옴 → 봤다 → 정했다)은 제안의 Trail에 남아 화면이 그대로 보여 준다. 난수 없음.

public static class ProposalTiming
{
    /// <summary>단말을 보고 정하기까지 (분).</summary>
    public const float ThinkMinutes = 2f;

    /// <summary>지휘하는 사람 (지휘 중인 사람 → 함장).</summary>
    public static CrewMember? Boss(World w)
    {
        var cmd = w.Command;
        return cmd.Active && cmd.Commander is CrewMember c0 && c0.CanAct ? c0 : cmd.Captain is CrewMember cap && cap.CanAct ? cap : null;
    }

    /// <summary>단말이 있는 방인가.</summary>
    public static bool AtTerminal(CrewMember c) =>
        c.Room is Room r && !c.Outside && r.Kind is RoomType.Bridge or RoomType.ServerRoom or RoomType.Comms && c.Pose != Pose.Sleeping;

    /// <summary>제안을 올렸을 때 첫 줄.</summary>
    public static void Raised(World w, Proposal p)
    {
        p.Trail.Add((w.Tick, $"주 컴퓨터가 제안을 올렸다 — {p.Basis}"));
        if (Urgent(w, p)) p.Trail.Add((w.Tick, "급한 일이라 지휘하는 사람에게 바로 알렸다"));
    }

    public static bool Urgent(World w, Proposal p) =>
        p.Kind is "vacuum" or "inert" || p.Deadline - p.Tick <= SimTime.Minutes(3) || w.Scale.OpenCases.Any(k => k.Big);

    /// <summary>
    /// 지금 정할 때인가 (아직 기한 전): 정할 때면 까닭을 돌려준다. 단말 앞에서는 처음 본 때를 적고 몇 분 생각한 뒤에 정한다.
    /// </summary>
    public static string? Moment(World w, Proposal p)
    {
        var boss = Boss(w);
        if (boss == null) return null;
        if (Urgent(w, p)) return $"급하다 — {Ko.IGa(boss.Name)} 바로 정했다";
        var m = w.Meetings;
        if (m.Session != null && m.Present.Contains(boss.Id))
        {
            if (p.SeenAt < 0) { p.SeenAt = w.Tick; p.Trail.Add((w.Tick, $"회의 안건으로 올렸다 ({m.Venue?.Name ?? "회의실"})")); }
            return "회의에서 이야기하고 정했다";
        }
        bool thought = p.SeenAt >= 0 && w.Tick - p.SeenAt >= SimTime.Minutes(ThinkMinutes);
        if (AtTerminal(boss))
        {
            if (p.SeenAt < 0)
            {
                p.SeenAt = w.Tick;
                p.Trail.Add((w.Tick, $"{Ko.IGa(boss.Name)} {boss.Room!.Name} 단말에서 제안을 봤다"));
                return null;
            }
            return thought ? $"{boss.Room!.Name} 단말 앞에서 정했다" : null;
        }
        // 보고 자리를 떴어도 이미 읽었으니 생각한 만큼 지나면 정한다 (손목 단말로 답한다)
        return thought ? "단말에서 본 것을 생각해 보고 손목 단말로 답했다" : null;
    }

    /// <summary>정한 뒤 마지막 줄.</summary>
    public static void Decided(World w, Proposal p, string moment) => p.Trail.Add((w.Tick, moment));
}
