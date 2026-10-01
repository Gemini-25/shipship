using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v13.0 방침: 승무원 회의가 정하고, 중앙 컴퓨터와 사람은 그 안에서 움직인다.
// 방침 하나 = 주제 · 선택지 · 지금 값 · 정한 날과 찬반 · 근거. 사고 결과·정기 회의·사람이 바뀔 때 다시 본다 (v13.2).
// 가치관 기울기: 안전 → 신중한 쪽, 효율 → 배·결과 쪽, 사람 → 인명 쪽, 규칙 → 정해진 절차 쪽, 자유 → 재량 쪽.

public sealed record PolicySpec(string Id, string Area, string Name, string[] Options, int Default, string Note);

public sealed class PolicyChange
{
    public long Tick { get; init; }
    public string Id { get; init; } = "";
    public int From { get; init; }
    public int To { get; init; }
    public string Why { get; init; } = "";
    public int Yes { get; init; }
    public int No { get; init; }
}

public sealed class PolicySystem
{
    public static readonly PolicySpec[] All =
    {
        new("decompress", "재난", "감압 격벽", new[] { "사람 우선", "배 우선" }, 0,
            "감압된 방에 사람이 있으면 격벽을 2분 기다린다 ↔ 바로 닫는다"),
        new("vacuumfire", "재난", "진공 소화", new[] { "금지", "빈 방만", "대피 카운트다운 뒤", "컴퓨터 판단" }, 1,
            "불난 방의 공기를 바깥으로 빼서 끈다 — 확실하고 빠르지만 공기·작물을 잃는다"),
        new("inertfire", "재난", "질식 소화", new[] { "금지", "빈 방만", "경보 30초 뒤" }, 1,
            "불활성 가스로 산소를 몰아내 끈다 — 공기는 지키지만 가스가 한정돼 있고 안에 있으면 숨이 막힌다"),
        new("zoneabandon", "재난", "구역 포기 시점", new[] { "일찍", "보통", "끝까지" }, 0,
            "아무도 못 막는 새는 방을 언제 포기하나 (1시간 반 / 4시간 / 실링폼이 떨어질 때까지)"),
    };

    public static PolicySpec Spec(string id) => All.First(p => p.Id == id);

    private readonly World _w;
    private readonly Dictionary<string, int> _value = new();
    private readonly Dictionary<string, long> _setAt = new();
    public List<PolicyChange> Changes { get; } = new();

    public PolicySystem(World w)
    {
        _w = w;
        foreach (var p in All) _value[p.Id] = p.Default;
    }

    public int this[string id]
    {
        get => _value.TryGetValue(id, out var v) ? v : Spec(id).Default;
    }

    public string Option(string id) => Spec(id).Options[Math.Clamp(this[id], 0, Spec(id).Options.Length - 1)];

    /// <summary>방침을 바꾼다 (회의·시험). 바뀐 이력이 남는다.</summary>
    public void Set(string id, int value, string why = "", int yes = 0, int no = 0)
    {
        var spec = Spec(id);
        value = Math.Clamp(value, 0, spec.Options.Length - 1);
        int from = this[id];
        _value[id] = value;
        if (from == value) return;
        _setAt[id] = _w.Tick;
        Changes.Add(new PolicyChange { Tick = _w.Tick, Id = id, From = from, To = value, Why = why, Yes = yes, No = no });
    }

    /// <summary>마지막으로 바꾼 틱 (-1: 처음 그대로).</summary>
    public long SetAt(string id) => _setAt.TryGetValue(id, out var t) ? t : -1;

    /// <summary>구역 포기까지 기다리는 시간 (시간). 끝까지면 무한.</summary>
    public float AbandonHours => this["zoneabandon"] switch { 0 => 1.5f, 1 => 4f, _ => float.PositiveInfinity };
}
