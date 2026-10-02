using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.20 ⑤ 성격이 자란다 — 첫날은 중립. 겪은 일이 두 축을 민다 (배마다 다른 컴퓨터):
//  신중 ↔ 과감: 자기 실수(틀린 판단 · 사과) · 사람이 쓰러진 사고 → 신중 / 맞힌 판단 · 높은 신뢰 · 늦게 닫아 옆방까지 잃은 일 → 과감 (빨리 움직인다).
//  사람 우선 ↔ 배 우선: 닫힌 격벽 안에서 쓰러진 사람 · 죽음 · 회의 방침(사람 우선) · 배 문화(사람) → 사람 / 공기를 잃은 일 · 방침(배 우선) · 문화(효율) → 배.
//  성격은 미리 돌려 보기의 무게(사람/배)와 불확실한 안의 벌점(신중)에 실리고, 말투에도 묻어난다.

public sealed class ComputerCharacter
{
    private readonly World _w;
    /// <summary>−1 과감 … +1 신중.</summary>
    public float Caution { get; private set; }
    /// <summary>−1 배 우선 … +1 사람 우선.</summary>
    public float PeopleTilt { get; private set; }
    public List<(long tick, string text, float dc, float dp)> Shifts { get; } = new();
    private int _trapped, _lateSeals, _deaths, _wrong, _right, _mistakes, _losses;
    private bool _seeded;
    private long _next, _dayNext;
    public int Grown;

    public ComputerCharacter(World w) => _w = w;

    public string Temper => Caution > 0.25f ? "신중" : Caution < -0.25f ? "과감" : "중립";
    public string Tilt => PeopleTilt > 0.25f ? "사람 우선" : PeopleTilt < -0.25f ? "배 우선" : "균형";
    public string Line => $"{Temper} ({Caution:+0.00;-0.00;0}) · {Tilt} ({PeopleTilt:+0.00;-0.00;0})";

    /// <summary>성격을 민다 (겪은 일 하나).</summary>
    public void Nudge(float caution, float people, string why)
    {
        float c0 = Caution, p0 = PeopleTilt;
        Caution = Math.Clamp(Caution + caution, -1f, 1f);
        PeopleTilt = Math.Clamp(PeopleTilt + people, -1f, 1f);
        if (MathF.Abs(Caution - c0) + MathF.Abs(PeopleTilt - p0) < 0.005f) return;
        Grown++;
        Shifts.Add((_w.Tick, why, Caution - c0, PeopleTilt - p0));
        if (Shifts.Count > 40) Shifts.RemoveAt(0);
        string was = $"{(c0 > 0.25f ? "신중" : c0 < -0.25f ? "과감" : "중립")}/{(p0 > 0.25f ? "사람 우선" : p0 < -0.25f ? "배 우선" : "균형")}";
        string now = $"{Temper}/{Tilt}";
        if (was != now)
        {
            string how = (Caution > 0.25f ? "전보다 조심스러워졌다" : Caution < -0.25f ? "전보다 빨리 움직인다" : "조심과 서두름 사이로 돌아왔다")
                         + " · " + (PeopleTilt > 0.25f ? "사람을 먼저 챙긴다" : PeopleTilt < -0.25f ? "배를 먼저 지킨다" : "사람과 배를 고루 본다");
            _w.History.Add(_w, HistoryKind.Decision, $"주 컴퓨터가 달라졌다 — {how} ({why})", null, log: true);
        }
    }

    /// <summary>타임라인 결정이 채점됐다 (ComputerForesee).</summary>
    internal void Graded(ForeseeDecision d)
    {
        if (d.Score == 1) Nudge(-0.02f, 0f, $"{d.Title} — {Ko.IGa(d.Pick.Name)} 맞았다");
        else if (d.Score == -1)
        {
            // 틀렸다: 신중해지고, 사람이 쓰러졌으면 사람 쪽으로 · 공기 · 불을 잃었으면 배 쪽으로
            bool hurt = d.Result.Contains("쓰러");
            Nudge(0.1f, hurt ? 0.08f : -0.04f, $"{d.Title} — {Ko.IGa(d.Pick.Name)} 틀렸다 ({d.Result})");
        }
    }

    /// <summary>1분마다 겪은 일을 읽는다 (카운터 차이만 · 방 단위 순회 없음). 하루에 한 번 회의 방침 · 배 문화 · 신뢰에 끌린다.</summary>
    public void Update()
    {
        var w = _w;
        var a = w.Automation;
        if (w.Tick < _next) return;
        _next = w.Tick + SimTime.Minutes(1);
        int trapped = a.TrappedCasualties, late = a.LateSeals;
        int deaths = w.Crew.Count(c => c.Dead);
        var au = a.Authority;
        int mistakes = au.Mistakes.Count;
        int wrong = a.Book.Wrong, right = a.Book.Right;
        int losses = w.Ship.Rooms.Count(r => r.Abandoned);
        if (!_seeded) { _seeded = true; _trapped = trapped; _lateSeals = late; _deaths = deaths; _wrong = wrong; _right = right; _mistakes = mistakes; _losses = losses; return; }
        // TrappedCasualties · LateSeals는 하루 검토 때 0으로 돌아간다 — 줄면 기준만 맞춘다
        if (trapped > _trapped) Nudge(0.12f * (trapped - _trapped), 0.2f * (trapped - _trapped), "닫은 격벽 안에서 사람이 쓰러졌다");
        if (late > _lateSeals) Nudge(-0.06f * (late - _lateSeals), -0.12f * (late - _lateSeals), "기다리는 사이 옆방 공기까지 잃었다");
        if (deaths > _deaths) Nudge(0.08f * (deaths - _deaths), 0.08f * (deaths - _deaths), "승무원을 잃었다");
        if (mistakes > _mistakes) Nudge(0.06f * (mistakes - _mistakes), 0f, "자기 실수를 인정했다");
        if (losses > _losses) Nudge(-0.03f, -0.08f * (losses - _losses), "구역을 잃었다");
        if (wrong - _wrong >= 3) { Nudge(0.04f, 0f, $"틀린 조치 {wrong - _wrong}건"); _wrong = wrong; }
        if (right - _right >= 20) { Nudge(-0.02f, 0f, $"맞은 조치 {right - _right}건"); _right = right; }
        _trapped = trapped; _lateSeals = late; _deaths = deaths; _mistakes = mistakes; _losses = losses;
        if (wrong < _wrong) _wrong = wrong;
        if (right < _right) _right = right;

        // 하루에 한 번: 회의 방침 · 배 문화 · 신뢰에 천천히 끌린다
        if (w.Tick < _dayNext) return;
        _dayNext = w.Tick + SimTime.TicksPerDay;
        if (w.Tick < SimTime.TicksPerDay) return;
        float pull = a.ShipFirst ? -0.04f : 0.03f;
        string cu = w.Meetings.Culture ?? "";
        if (cu.StartsWith("사람")) pull += 0.03f; else if (cu.StartsWith("효율")) pull -= 0.03f;
        float trust = a.Trusts.Average();
        float bold = trust > 0.7f ? -0.03f : trust < 0.4f ? 0.04f : 0f;
        if (cu.StartsWith("안전")) bold += 0.03f; else if (cu.StartsWith("자유")) bold -= 0.02f;
        Nudge(bold, pull, $"하루를 돌아봄 — 방침 {(a.ShipFirst ? "배 우선" : "사람 우선")} · 문화 {(cu == "" ? "아직" : cu)} · 신뢰 {trust * 100:0}%");
    }

    /// <summary>말투에 성격을 싣는다 (중립이면 그대로).</summary>
    public string Flavor(string text)
    {
        string tail = Caution > 0.4f ? "한 번 더 확인하고 가겠습니다" : Caution < -0.4f ? "바로 하겠습니다" : "";
        string who = PeopleTilt > 0.4f ? "다치는 사람이 없게" : PeopleTilt < -0.4f ? "배를 먼저 지키겠습니다" : "";
        var parts = new[] { text, tail, who }.Where(x => x != "");
        return string.Join(". ", parts); // 끝맺음(마침표 · 말투)은 ComputerVoice.Style이 붙인다
    }

    internal void Hash(Action<long> I, Action<float> F) { F(Caution); F(PeopleTilt); I(Grown); }
}

public sealed partial class AutomationSystem
{
    private ComputerCharacter? _character;
    /// <summary>v16.20 컴퓨터 성격 (신중/과감 · 사람/배).</summary>
    public ComputerCharacter Character => _character ??= new ComputerCharacter(_world);
}
