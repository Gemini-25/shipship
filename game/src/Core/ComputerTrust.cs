using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.6 사람마다 다른 컴퓨터 신뢰 · 배우기 · 말투.
//  신뢰: 컴퓨터 경보로 빠져나와 산 사람은 믿고, 컴퓨터 조치(진공·질식 소화 · 닫힌 격벽)로 다친 사람과 "빈 방"이라 믿긴 사람은 의심한다.
//    → 컴퓨터의 대피 지시를 따를지가 갈린다 (아주 못 믿으면 위험이 눈앞에 오기 전까지 버틴다) · 일기 · 대화에 남는다.
//    배 전체의 컴퓨터 신뢰(지휘 · 회의)는 그대로 두고, 사람마다의 신뢰가 그 위에서 따로 움직인다.
//  배우기: 비슷한 사고에 더 빨리(교훈 반영 모듈 — 같은 종류 방의 불은 소화 수순을 일찍 올린다) · 오경보가 잦은 감지기는 덜 믿는다(BeliefModel) ·
//    틀린 판단을 기억한다 (같은 방 같은 조치는 한 번 더 확인).
//  말투: 이름이 붙고, 오래 쓰면 배 문화를 닮는다. 컴퓨터를 갈아 끼우면(재설치) 성격이 처음으로 돌아가 아쉬워한다.

public sealed class CrewTrustBook
{
    private readonly World _w;
    private readonly Dictionary<int, float> _t = new();
    private long _talkNext;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 3571 + 13));
    public int Ups, Downs, Hesitations, Talks;
    /// <summary>사람마다 신뢰가 바뀐 까닭 (최근 것).</summary>
    public Dictionary<int, string> LastWhy { get; } = new();

    public CrewTrustBook(World w) => _w = w;

    /// <summary>처음 값: 배 전체 신뢰 + 가치관 (규칙·효율은 조금 더, 자유는 덜).</summary>
    public float Base(CrewMember c) => Math.Clamp(_w.Command.ComputerTrust + (c.Value switch { CrewValue.Rules => 0.05f, CrewValue.Efficiency => 0.05f, CrewValue.Freedom => -0.1f, CrewValue.People => -0.03f, _ => 0f }), 0.05f, 0.98f);

    public float Of(CrewMember c) => _t.TryGetValue(c.Id, out var v) ? v : Base(c);

    public bool Changed(CrewMember c) => _t.ContainsKey(c.Id);

    public void Change(CrewMember c, float delta, string why, bool quiet = false)
    {
        var w = _w;
        float before = Of(c);
        float after = Math.Clamp(before + delta, 0.05f, 0.98f);
        _t[c.Id] = after;
        c.ComputerFaith = after;
        LastWhy[c.Id] = why;
        if (delta > 0f) Ups++; else Downs++;
        if (quiet && MathF.Abs(delta) < 0.1f) return;
        w.Log.Add(w.Tick, LogKind.Life, $"컴퓨터 신뢰 {before * 100:0}% → {after * 100:0}% — {why}", c.Id);
        if (MathF.Abs(delta) >= 0.1f)
        {
            Life.Diary(w, c, Persona.Say(c, delta < 0 ? $"{why}. 이제 컴퓨터 말은 반만 믿는다" : $"{why}. 컴퓨터 덕에 살았다"));
            MarkLog.Add(c.Memory.Marks, w.Tick, delta < 0 ? $"컴퓨터를 의심하게 됐다 — {why}" : $"컴퓨터를 믿게 됐다 — {why}");
        }
    }

    /// <summary>컴퓨터의 대피 지시를 따르나 (아주 못 믿으면 규칙을 중시하는 사람만 따른다).</summary>
    public bool Obeys(CrewMember c) => Of(c) >= 0.3f || c.Value == CrewValue.Rules;

    /// <summary>못 믿어서 버텼다 (대피 지시를 바로 따르지 않았다).</summary>
    public void Hesitated(CrewMember c)
    {
        Hesitations++;
        if (R.Chance(0.2f)) _w.Log.Add(_w.Tick, LogKind.Life, Persona.Say(c, "컴퓨터가 나가라는데… 지난번에도 틀렸잖아"), c.Id);
    }

    /// <summary>컴퓨터가 빈 방이라 믿은 곳에서 사람이 업어 나왔다.</summary>
    public void Saved(CrewMember patient, CrewMember rescuer, Room room)
    {
        Life.Diary(_w, patient, Persona.Say(patient, $"컴퓨터는 {room.Name}에 내가 없다고 믿었다. {Ko.IGa(rescuer.Name)} 직접 와서 살았다"));
        Life.Diary(_w, rescuer, Persona.Say(rescuer, $"컴퓨터가 빈 방이라던 {room.Name}에 {Ko.IGa(patient.Name)} 쓰러져 있었다"));
    }

    /// <summary>세 시간마다: 같은 방에 믿는 사람과 못 믿는 사람이 있으면 컴퓨터 이야기를 한다.</summary>
    public void Update()
    {
        var w = _w;
        if (w.Tick < _talkNext) return;
        _talkNext = w.Tick + SimTime.Hours(3);
        foreach (var c in w.Crew)
        {
            if (c.Dead || !c.IsAwake || c.Room == null || !Changed(c)) continue;
            var other = w.Crew.FirstOrDefault(o => o != c && !o.Dead && o.IsAwake && o.Room == c.Room && MathF.Abs(Of(o) - Of(c)) >= 0.2f);
            if (other == null || !R.Chance(0.5f)) continue;
            var (low, high) = Of(c) < Of(other) ? (c, other) : (other, c);
            string why = LastWhy.TryGetValue(low.Id, out var x) ? x : "그냥";
            w.Log.Add(w.Tick, LogKind.Life, $"{Persona.Say(low, $"컴퓨터 말 믿지 마 — {why}")} / {Persona.Say(high, "그래도 경보 덕에 여럿 살았잖아")}", low.Id);
            low.ChangeAffinity(high, -0.01f);
            Talks++;
            break;
        }
    }

    public float Average() { var live = _w.Crew.Where(c => !c.Dead).ToList(); return live.Count == 0 ? 0f : live.Average(Of); }
    public float Spread() { var live = _w.Crew.Where(c => !c.Dead).Select(Of).ToList(); return live.Count == 0 ? 0f : live.Max() - live.Min(); }
}

public sealed class ComputerLearn
{
    private readonly World _w;
    public ComputerLearn(World w) => _w = w;

    /// <summary>틀린 판단 (방 · 조치 종류 · 까닭).</summary>
    public List<(long tick, int roomId, string kind, string why)> WrongCalls { get; } = new();
    /// <summary>방 종류별 불: 몇 번 겪었나 · 마지막에 몇 분 걸렸나.</summary>
    public Dictionary<RoomType, (int count, float minutes)> FireLessons { get; } = new();
    public int Faster;

    public void Remember(int roomId, string kind, string why)
    {
        WrongCalls.Add((_w.Tick, roomId, kind, why));
        if (WrongCalls.Count > 30) WrongCalls.RemoveAt(0);
    }

    public void Wrong(ComputerAct a)
    {
        if (a.RoomId < 0 || a.Kind == ActKind.Proposal) return; // 제안은 제 채점에서 기억한다
        Remember(a.RoomId, a.Kind.ToString(), a.Result);
    }

    /// <summary>같은 방의 같은 조치에서 사흘 안에 틀린 적이 있나.</summary>
    public string? WrongBefore(Room r, string kind) =>
        WrongCalls.LastOrDefault(x => x.roomId == r.Id && (x.kind == kind || x.kind == "Suppress" && kind is "vacuum" or "inert") && _w.Tick - x.tick < SimTime.TicksPerDay * 3).why;

    /// <summary>불을 끝냈다 — 그 종류의 방 교훈.</summary>
    public void FireDone(Room r, float minutes)
    {
        var t = r.Kind;
        var (n, _) = FireLessons.GetValueOrDefault(t);
        FireLessons[t] = (n + 1, minutes);
    }

    /// <summary>교훈 반영: 같은 종류 방에서 불을 겪었으면 소화 수순을 일찍 올린다 (한 번에 20% · 최대 40%).</summary>
    public float GraceMul(Room r)
    {
        if (!_w.Automation.Active(ComputerModule.Lessons) || !FireLessons.TryGetValue(r.Kind, out var l)) return 1f;
        return 1f - 0.2f * Math.Min(2, l.count);
    }
}

public sealed class ComputerVoice
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 2903 + 7));
    private static readonly string[] Names = { "아라", "누리", "한별", "모아", "다온", "미르", "가람", "솔빛", "새벽", "온새" };
    public string Name { get; private set; } = "";
    public string NamedBy { get; private set; } = "";
    public long NamedAt { get; private set; } = -1;
    /// <summary>배 문화를 닮은 말투 ("" = 아직 기계 말투).</summary>
    public string Tone { get; private set; } = "";
    public long Since { get; private set; }
    public int BodyId { get; private set; } = -1;
    public int Resets;
    public string FormerName { get; private set; } = "";
    private long _next;

    public ComputerVoice(World w) => _w = w;

    public string Call => Name != "" ? Name : "주 컴퓨터";

    /// <summary>말투를 입힌다 (배 문화를 닮은 끝맺음).</summary>
    public string Style(string text) => Tone switch
    {
        "규칙" => text + " — 절차대로 하십시오.",
        "자유" => text + " — 알아서들 잘 하겠지만.",
        "사람" => text + " — 모두 무사하길.",
        "안전" => text + " — 두 번 확인하십시오.",
        "효율" => text,
        _ => text + ".",
    };

    /// <summary>여섯 시간마다: 컴퓨터가 바뀌었나 (재설치) · 이름 붙이기 · 말투.</summary>
    public void Update()
    {
        var w = _w;
        if (w.Tick < _next) return;
        _next = w.Tick + SimTime.Hours(6);
        var body = w.Automation.ComputerBody;
        if (body == null) return;
        if (BodyId < 0) { BodyId = body.Id; Since = w.Tick; }
        else if (body.Id != BodyId)
        {
            // 재설치: 성격이 처음으로 돌아간다
            BodyId = body.Id;
            Since = w.Tick;
            Resets++;
            if (Name != "" || Tone != "")
            {
                FormerName = Name != "" ? Name : "옛 컴퓨터";
                foreach (var c in w.Crew.Where(c => !c.Dead && w.Automation.Trusts.Of(c) >= 0.55f).Take(3))
                    Life.Diary(w, c, Persona.Say(c, $"새 컴퓨터는 '{FormerName}'처럼 말하지 않는다. 그 말투가 그립다"));
                w.History.Add(w, HistoryKind.Decision, $"주 컴퓨터를 새로 설치했다 — '{FormerName}'의 이름과 말투가 지워졌다", body.Room, log: true);
            }
            Name = ""; NamedBy = ""; NamedAt = -1; Tone = "";
            return;
        }
        // 이름: 사흘 넘게 함께했고, 컴퓨터를 가장 믿는 사람이 0.7을 넘으면 그 사람이 이름을 붙인다
        if (Name == "" && w.Tick - Since >= SimTime.TicksPerDay * 3)
        {
            var fan = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderByDescending(c => w.Automation.Trusts.Of(c)).ThenBy(c => c.Id).FirstOrDefault();
            if (fan != null && w.Automation.Trusts.Of(fan) >= 0.7f)
            {
                Name = R.Pick(Names);
                NamedBy = fan.Name;
                NamedAt = w.Tick;
                w.History.Add(w, HistoryKind.Decision, $"{Ko.IGa(fan.Name)} 주 컴퓨터를 '{Name}'(이)라 부르기 시작했다", body.Room, new[] { fan }, log: true);
            }
        }
        // 말투: 닷새 넘게 함께하면 배 문화(첫 출항 회의의 가치관)를 닮는다
        if (Tone == "" && w.Tick - Since >= SimTime.TicksPerDay * 5 && w.Meetings.Culture is string cu && cu != "")
        {
            Tone = cu.StartsWith("규칙") ? "규칙" : cu.StartsWith("자유") ? "자유" : cu.StartsWith("사람") ? "사람" : cu.StartsWith("안전") ? "안전" : "효율";
            w.Log.Add(w.Tick, LogKind.Ship, $"{Call}의 말투가 배를 닮아 간다 ({cu})");
        }
    }
}

public sealed partial class AutomationSystem
{
    private CrewTrustBook? _trusts;
    private ComputerLearn? _learn;
    private ComputerVoice? _voice;
    /// <summary>v16.6 사람마다 다른 컴퓨터 신뢰.</summary>
    public CrewTrustBook Trusts => _trusts ??= new CrewTrustBook(_world);
    /// <summary>v16.6 배우기 (교훈 · 틀린 판단).</summary>
    public ComputerLearn Learn => _learn ??= new ComputerLearn(_world);
    /// <summary>v16.6 이름 · 말투.</summary>
    public ComputerVoice Voice => _voice ??= new ComputerVoice(_world);
}
