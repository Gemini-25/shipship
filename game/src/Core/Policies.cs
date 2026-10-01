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
        new("risktaking", "재난", "위험 감수", new[] { "신중", "보통", "과감" }, 0,
            "위험한 방의 일: 2인 1조(한 명은 문 밖에서 지킨다) / 숨 쉴 수 없는 방만 2인 1조 / 혼자"),
        // v13.1 지휘·조직
        new("command", "지휘", "현장 지휘", new[] { "사람", "컴퓨터", "상황 따라" }, 2,
            "위기 때 누가 조를 짜나 — 선장(못 하면 다음 사람) / V 지휘 컴퓨터 / 컴퓨터가 멀쩡하고 더 믿을 만하면 컴퓨터"),
        new("rotation", "지휘", "위기 교대", new[] { "2시간", "4시간", "끝날 때까지" }, 1,
            "비상 일을 오래 한 사람을 대기조와 바꿔 재운다"),
        new("election", "지휘", "선장 선출", new[] { "직책 순서", "다수결", "경력" }, 1,
            "불신임으로 선장이 물러나면 누가 맡나"),
        new("noconfidence", "지휘", "선장 불신임", new[] { "과반", "3분의 2" }, 0,
            "신뢰가 무너진 선장을 저녁 회의에서 물러나게 하는 문턱"),
        new("suits", "자원", "우주복", new[] { "비상조 먼저", "먼저 쓰는 사람", "한 사람 한 벌" }, 0,
            "모자란 우주복을 누가 입나 — 위험한 방에 가는 조부터 / 먼저 집는 사람 / 사람마다 정해 둔 한 벌"),
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
