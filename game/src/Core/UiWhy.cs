using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v17.6 숫자마다 "왜 이 값" — 효율 82% = 전압 −10% · 마모 −5% … 처럼 값을 이루는 몫을 그대로 풀어 보인다.
// 식은 Machine.Efficiency 와 같은 곱 (시험이 곱해서 같은지 확인한다) — 읽기만.

/// <summary>값을 이루는 한 몫: 이름 · 곱하는 수(1 = 영향 없음) · 덧붙임.</summary>
public readonly record struct WhyTerm(string Name, float Factor, string Note = "")
{
    /// <summary>"−10%" · "+15%".</summary>
    public string Pct => Factor >= 1f ? $"+{(Factor - 1f) * 100f:0}%" : $"−{(1f - Factor) * 100f:0}%";
}

public static class UiWhy
{
    /// <summary>설비 효율의 몫들 (영향이 있는 것만 · 큰 것부터). 전기가 없으면 "전기 없음" 하나.</summary>
    public static List<WhyTerm> Efficiency(Machine m)
    {
        var list = new List<WhyTerm>();
        if (!m.Powered && m.Spec.PowerDraw > 0f) { list.Add(new WhyTerm("전기 없음", 0f, m.Body.Room.Name + " 회로")); return list; }
        var room = m.Body.Room;
        void Add(string name, float f, string note = "") { if (MathF.Abs(f - 1f) > 0.004f) list.Add(new WhyTerm(name, f, note)); }
        Add("고장", m.FaultFactor, string.Join("·", m.Faults.Take(2).Select(x => x.Name)));
        Add("등급", Grades.Output(m.Grade), Grades.Name(m.Grade));
        Add("마모", 1f - 0.25f * m.Wear * m.Wear, $"마모 {m.Wear * 100:0}%");
        Add("상태", 0.6f + 0.4f * m.Condition, $"상태 {m.Condition * 100:0}%");
        Add("과열", m.Heat > 0.7f ? MathF.Max(0.5f, 1f - (m.Heat - 0.7f)) : 1f, $"열 {m.Heat * 100:0}%");
        Add("분말 · 오염", 1f - 0.35f * m.Fouled);
        Add("관 이음", m.Line < 0.3f && Procedures.Plumbed(m.Body.Type) ? 0.3f : 1f, "물 · 냉각수가 덜 든다");
        Add("전압", m.Spec.PowerDraw > 0f ? MathF.Min(1f, 0.15f + 0.85f * room.PowerFlow / 0.8f) : 1f, $"{room.Name} 전압 {room.PowerFlow * 100:0}%");
        Add("수압", Procedures.Plumbed(m.Body.Type) && room.WaterFlow > 0f ? MathF.Min(1f, 0.3f + 0.7f * room.WaterFlow / 0.6f) : 1f, $"수압 {room.WaterFlow * 100:0}%");
        Add("단수", m.Body.Type == FurnitureType.OxygenGenerator && (!room.WaterLinked || room.ValveShut) ? 0.15f : 1f, "전기분해할 물이 없다");
        return list.OrderBy(t => MathF.Abs(MathF.Log(MathF.Max(1e-4f, t.Factor)))).Reverse().ToList();
    }

    /// <summary>몫을 곱한 값 (= 실제 효율).</summary>
    public static float Product(IEnumerable<WhyTerm> terms) => terms.Aggregate(1f, (a, t) => a * t.Factor);

    /// <summary>한 줄 요약: "효율 82% = 전압 −10% · 마모 −5%".</summary>
    public static string Line(string what, float value, IReadOnlyList<WhyTerm> terms) =>
        terms.Count == 0 ? $"{what} {value * 100:0}% — 깎이는 것 없음" : $"{what} {value * 100:0}% = " + string.Join(" · ", terms.Take(4).Select(t => $"{t.Name} {t.Pct}"));

    /// <summary>상태 줄 숫자의 까닭 (전력 · 배터리 · 산소 · 물 · 식량 · 작업).</summary>
    public static List<string> Chip(World w, string label)
    {
        var p = w.Power;
        var ship = w.Ship;
        var lines = new List<string>();
        switch (label)
        {
            case "전력":
                lines.Add($"원자로 한도 {p.ReactorLimit:0} kW 중 {p.Delivered:0} kW를 쓴다");
                if (p.ReactorRamp < 1f) lines.Add($"재기동 중 — {p.ReactorRamp * 100:0}%까지 올라왔다");
                if (p.Brownout) lines.Add("모자라서 급한 곳부터 돌린다");
                foreach (var (name, kw) in ship.Machines.Where(m => m.Powered && m.Active).GroupBy(m => m.Body.Room.Name).Select(g => (g.Key, g.Sum(m => m.Spec.PowerDraw))).OrderByDescending(x => x.Item2).Take(4))
                    lines.Add($"{name} {kw:0.0} kW");
                break;
            case "배터리":
                lines.Add($"충전 {p.BatteryPercent * 100:0}% · 흐름 {(p.BatteryFlow >= 0 ? "+" : "")}{p.BatteryFlow:0.0} kW");
                lines.Add(p.BatteryFlow < -0.1f ? "쓰는 전기가 원자로보다 많아 배터리에서 꺼내 쓴다" : "남는 전기로 채운다");
                break;
            case "산소":
                foreach (var r in ship.LiveRooms.OrderBy(r => r.Air.O2).Take(3)) lines.Add($"{r.Name} {r.Air.O2:0.0} kPa");
                foreach (var m in ship.Machines.Where(m => m.Body.Type == FurnitureType.OxygenGenerator).Take(2))
                    lines.Add(Line($"산소 발생기 ({m.Body.Room.Name}) 효율", m.Efficiency, Efficiency(m)));
                break;
            case "물":
                lines.Add($"탱크 {w.Water.Level:0} / {w.Water.Capacity:0} L");
                foreach (var m in ship.Machines.Where(m => m.Body.Type == FurnitureType.WaterRecycler).Take(2))
                    lines.Add(Line("재생기 효율", m.Efficiency, Efficiency(m)));
                break;
            case "식량":
                lines.Add($"먹을 것 {FoodPolicy.FoodDays(w):0.0}일치 · 사람 {w.Crew.Count(c => !c.Dead)}명");
                break;
            case "작업":
                foreach (var o in w.Board.Open.OrderByDescending(o => o.Urgency).Take(4)) lines.Add($"{o.Title} — 급함 {o.Urgency:0.00}");
                break;
        }
        return lines;
    }
}
