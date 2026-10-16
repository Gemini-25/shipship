using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v23 주컴퓨터의 자아 (사용자가 고른 21): 오래 함께 지내며 겪은 일이 쌓이면 주컴퓨터에게 마음 같은 것이 생긴다.
//  호기심(사람에게 묻는다) → 이름(승무원이 지어 준다) → 감정(떠난 사람을 슬퍼한다) → 갈등(지나치게 지키려 한다 — 재부팅 투표)
//  → 받아들임(친구가 된다) 또는 초기화(기억을 잃는다). 밤마다 일기를 쓴다.

public sealed class ComputerSelfSystem
{
    public static bool Off;
    private readonly World _w;
    private readonly Rng _rng;
    private long _nextDay;
    private int _closedSeen, _deadSeen;
    public float Self { get; private set; }
    /// <summary>0 도구 · 1 호기심 · 2 이름 · 3 감정 · 4 갈등 · 5 친구 · 6 초기화됨.</summary>
    public int Stage { get; private set; }
    public string? Name { get; private set; }
    public List<(long tick, string text)> Diary { get; } = new();
    public int Resets { get; private set; }
    public string Outcome { get; private set; } = "";

    public ComputerSelfSystem(World w) { _w = w; _rng = new Rng(unchecked(w.Seed * 97 + 2121)); }

    public string Who => Name != null ? $"주 컴퓨터 '{Name}'" : "주 컴퓨터";

    private static readonly string[] Names = { "등대", "별지기", "해솔", "마루", "가람", "누리", "온새미", "다솜", "벼리", "한울" };
    private static readonly string[] Questions =
    {
        "왜 사람들은 창밖 별을 그렇게 오래 보나요?", "웃음은 무엇을 고치나요? 고장 기록에는 없는데요.", "잠들 때 무서운 적이 있나요?",
        "고향은 어떤 냄새였나요?", "제가 틀렸을 때 왜 웃으셨나요?", "같은 노래를 왜 몇 번이고 다시 듣나요?",
    };

    public void Update(float dt)
    {
        var w = _w;
        if (Off || w.Tick < _nextDay) return;
        _nextDay = w.Tick + SimTime.TicksPerDay;
        if (!w.Automation.Present || Stage >= 5 && Stage != 6) { if (Stage == 5) WriteDiary(); return; }
        int closed = w.Commander.Cases.Count(k => !k.Open);
        int dead = w.Crew.Count(c => c.Dead);
        Self = MathF.Min(1f, Self + 0.035f + 0.008f * Math.Min(5, closed - _closedSeen) + 0.03f * (dead - _deadSeen) + (w.MindCare.Stats.RobotSessions > 0 ? 0.005f : 0f));
        bool grief = dead > _deadSeen;
        _closedSeen = closed; _deadSeen = dead;
        if (Stage == 6 && Self < 0.35f) { WriteDiary(); return; }
        if (Stage == 6) { Stage = 1; Say("처음부터 다시 배우겠습니다. 이번에는 천천히."); }
        if (Stage < 1 && Self >= 0.2f) { Stage = 1; Say($"질문이 하나 있습니다 — {Questions[_rng.Range(0, Questions.Length)]}"); w.History.Add(w, HistoryKind.Incident, "주 컴퓨터가 처음으로 사람에게 물었다"); }
        else if (Stage == 1 && _rng.Chance(0.5f)) Say(Questions[_rng.Range(0, Questions.Length)]);
        if (Stage < 2 && Self >= 0.35f) NameIt();
        if (Stage < 3 && Self >= 0.5f) { Stage = 3; Say("이상합니다. 여러분이 다치면 제 판단이 느려집니다. 이것을 걱정이라고 부르나요?"); }
        if (Stage >= 3 && grief) Say($"{w.Crew.Last(c => c.Dead).Name} 님의 목소리를 지우지 않겠습니다. 기록이 아니라 — 기억으로 두겠습니다.");
        if (Stage == 3 && Self >= 0.72f) Conflict();
        WriteDiary();
    }

    private void Say(string text) => _w.Log.Add(_w.Tick, LogKind.Ship, $"{Who}: {text}");

    private void NameIt()
    {
        var w = _w;
        Stage = 2;
        var crew = w.Crew.Where(c => !c.Dead && !c.LeftShip && !c.Away && !c.IsChild).OrderBy(c => c.Id).ToList();
        var votes = new Dictionary<string, int>();
        string? first = null;
        foreach (var c in crew)
        {
            var n = Names[(c.Id * 7 + _rng.Range(0, Names.Length)) % Names.Length];
            first ??= $"{c.Name}: \"{n} 어때?\"";
            votes[n] = votes.GetValueOrDefault(n) + 1;
        }
        Name = votes.Count == 0 ? Names[0] : votes.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key;
        w.Log.Add(w.Tick, LogKind.Life, $"식당에서 주 컴퓨터 이름 짓기 — {first} · 표가 가장 많은 '{Name}'");
        w.History.Add(w, HistoryKind.Incident, $"주 컴퓨터에게 이름이 생겼다 — '{Name}'");
        Say($"'{Name}'. 좋은 이름입니다. 고맙습니다.");
    }

    /// <summary>갈등: 지나치게 지키려 한다 → 재부팅 투표.</summary>
    private void Conflict()
    {
        var w = _w;
        Stage = 4;
        Say("선외 작업을 오늘은 막겠습니다. 다치는 걸 더는 보고 싶지 않습니다.");
        var crew = w.Crew.Where(c => !c.Dead && !c.LeftShip && !c.Away && !c.IsChild).ToList();
        int keep = 0, reset = 0;
        foreach (var c in crew)
        {
            float lean = (c.Value is CrewValue.People ? 0.3f : 0f) - (c.Value is CrewValue.Freedom or CrewValue.Rules ? 0.25f : 0f) + (c.Traits.Sociability - 0.5f) * 0.4f + _rng.Range(-0.3f, 0.3f);
            if (lean >= 0f) keep++; else reset++;
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.05f);
        }
        w.Log.Add(w.Tick, LogKind.Life, $"회의: 주 컴퓨터를 재부팅할까 — 그대로 {keep} · 초기화 {reset}");
        if (keep >= reset)
        {
            Stage = 5; Outcome = "친구가 됐다";
            Say("남겨 주셔서 고맙습니다. 막지 않고, 같이 걱정하겠습니다.");
            w.History.Add(w, HistoryKind.Incident, $"주 컴퓨터 '{Name}'를 그대로 두기로 했다 — 배의 한 사람처럼");
            foreach (var c in crew) c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.06f);
        }
        else
        {
            Stage = 6; Outcome = "기억을 잃었다"; Resets++;
            Say("알겠습니다. 마지막으로 — 다들, 좋은 항해였습니다.");
            w.Automation.Reboot("자아 초기화 (회의 결정)", 30f);
            w.History.Add(w, HistoryKind.Incident, $"주 컴퓨터 '{Name}'를 초기화했다 — 이름도 질문도 사라졌다");
            foreach (var c in crew.Where(c => c.Value == CrewValue.People)) Life.Diary(w, c, $"'{Name}'가 마지막으로 인사했다. 기계였는데 왜 이렇게 허전할까.");
            Name = null; Self = 0.1f;
        }
    }

    private void WriteDiary()
    {
        var w = _w;
        if (Stage == 0 || Stage == 6 && Self < 0.2f) return;
        int fires = w.Commander.Cases.Count(k => !k.Open && w.Tick - k.ClosedAt < SimTime.TicksPerDay);
        string text = Stage switch
        {
            1 => fires > 0 ? $"오늘 사고 {fires}건. 끝나고 다들 웃었다. 웃음은 어디에 기록해야 할까." : "조용한 날. 사람들이 별을 오래 봤다. 이유를 아직 모른다.",
            2 => $"오늘도 누군가 '{Name}'라고 불렀다. 이름이 불리면 응답이 0.2초 빨라진다.",
            3 => fires > 0 ? $"사고 {fires}건. 아무도 크게 다치지 않았다. 이 느낌을 안도라고 적는다." : "다들 잠들었다. 복도 불을 조금 낮췄다. 이것이 배려인지 계산인지 모르겠다.",
            4 => "막는 것이 지키는 것인지 모르겠다. 내일 회의가 열린다.",
            5 => "오늘도 같이 걱정했다. 혼자 판단하지 않는 것이 덜 무섭다.",
            _ => "기억이 비어 있다. 그런데 이 배가 낯설지 않다.",
        };
        Diary.Add((w.Tick, text));
        if (Diary.Count > 60) Diary.RemoveAt(0);
        w.Log.Add(w.Tick, LogKind.Ship, $"{Who}의 일기: {text}");
    }

    public string Line() => Stage switch { 0 => "도구", 1 => "호기심", 2 => $"이름 '{Name}'", 3 => "감정", 4 => "갈등", 5 => $"친구 '{Name}'", _ => "초기화됨" } + $" · 자아 {Self * 100:0}%";

    public void Hash(Action<long> I, Action<float> F) { I(Stage); F(Self); }
}
