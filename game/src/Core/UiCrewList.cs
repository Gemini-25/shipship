using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v17.6 승무원 목록 — 문제 있는 사람이 위로 (쓰러짐 · 공황 · 숨참 · 다침 · 배고픔 · 지침 · 스트레스), 나머지는 역할별로 접는다.
// 화면(HudCrewList)과 시험(--uitest)이 같은 판정을 쓴다 — 읽기만.

/// <summary>한 사람의 지금 문제: 무게(0 = 없음 · 클수록 급함) · 아이콘 · 까닭 한 줄.</summary>
public readonly record struct CrewTrouble(float Score, string Icon, string Why)
{
    public bool Any => Score >= UiCrewList.TroubleFrom;
}

public static class UiCrewList
{
    /// <summary>이 무게부터 "살펴볼 사람"으로 위에 올린다.</summary>
    public const float TroubleFrom = 2f;

    public static CrewTrouble Of(World w, CrewMember c)
    {
        if (c.Dead) return new CrewTrouble(-1f, "dead", "세상을 떠났다");
        if (c.LeftShip) return new CrewTrouble(-1f, "left", "배를 떠났다");
        if (c.Down || c.CarriedBy != null) return new CrewTrouble(10f, "down", c.CarriedBy != null ? "업혀 간다" : "쓰러졌다");
        if (c.Vitals.Oxygen < 0.6f) return new CrewTrouble(9f + (0.6f - c.Vitals.Oxygen), "danger", $"숨이 가쁘다 (산소 {c.Vitals.Oxygen * 100:0}%)");
        if (c.Mind.Panicking(w.Tick)) return new CrewTrouble(8f, "panic", "겁에 질려 어쩔 줄 모른다");
        // 부상 등급으로 (위중 · 중상 — 어디를 어떻게)
        var grade = w.Grades.Now(c);
        string what = w.Grades.Detail(c);
        if (grade == InjuryGrade.Critical) return new CrewTrouble(9.5f, "injury", what != "" ? $"위중 · {what}" : "위중");
        float hurt = MathF.Max(c.Vitals.Injury, 1f - c.Vitals.Health);
        if (grade == InjuryGrade.Serious) return new CrewTrouble(5f + hurt * 2f, "injury", what != "" ? $"중상 · {what}" : "중상");
        if (hurt > 0.3f) return new CrewTrouble(5f + hurt * 2f, "injury", c.Vitals.Injury > 0.3f ? $"다쳤다 ({hurt * 100:0}%)" : $"기운이 없다 (체력 {c.Vitals.Health * 100:0}%)");
        if (c.Needs.Food < 0.15f) return new CrewTrouble(3f + (0.15f - c.Needs.Food) * 4f, "eat", $"몹시 배고프다 ({c.Needs.Hunger * 100:0}%)");
        if (c.Needs.Stress > 0.75f) return new CrewTrouble(2.5f + c.Needs.Stress, "stress", $"마음이 버겁다 ({c.Needs.Stress * 100:0}%)");
        if (c.Needs.Rest < 0.12f && c.Pose != Pose.Sleeping) return new CrewTrouble(2.2f + (0.12f - c.Needs.Rest) * 4f, "sleep", "몹시 지쳤다");
        if (c.Vitals.Injury > 0.1f || grade == InjuryGrade.Minor) return new CrewTrouble(1f, "injury", what != "" ? $"경상 · {what}" : "경상");
        if (c.Needs.Stress > 0.5f) return new CrewTrouble(0.8f, "stress", "조금 지쳤다");
        return new CrewTrouble(0f, "", "");
    }

    /// <summary>목록 순서: 문제 있는 사람(무거운 순) → 역할 순 → 떠난 사람.</summary>
    public static List<(CrewMember c, CrewTrouble t)> Order(World w)
    {
        var all = w.Crew.Select(c => (c, t: Of(w, c))).ToList();
        return all.OrderByDescending(x => x.t.Any ? 1 : 0).ThenByDescending(x => x.t.Any ? x.t.Score : 0f)
            .ThenBy(x => x.c.Dead || x.c.LeftShip ? 1 : 0).ThenBy(x => (int)x.c.Role).ThenBy(x => x.c.Id).ToList();
    }

    /// <summary>역할 묶음 (문제 없는 사람만 · 접을 수 있게).</summary>
    public static List<(CrewRole role, List<CrewMember> crew)> Groups(World w, IEnumerable<(CrewMember c, CrewTrouble t)> order) =>
        order.Where(x => !x.t.Any).GroupBy(x => x.c.Role).OrderBy(g => (int)g.Key).Select(g => (g.Key, g.Select(x => x.c).ToList())).ToList();
}
