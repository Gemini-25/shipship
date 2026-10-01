using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v12.9 캠페인: 이어지는 임무와 갈림길. 임무마다 목표(살아서 닿기·소재 모으기·구조·연구)가 있고,
// 해내면 다음 장으로, 3장에서 길이 갈린다 (교역의 길 ↔ 탐사의 길). 마지막은 세대선 — 배에서 아이가 태어나고 자란다.
// 관찰자 게임이라 목표는 "배가 스스로 해내는지"를 보는 이야기 틀이다 (사람이 직접 하지 않는다).

public enum MissionGoal { Arrive, Deliver, Rescue, Research, Survive }

public sealed record Mission(int Chapter, string Id, string Title, string Brief, MissionGoal Goal, ItemKind? Item, int Amount, string? NextA, string? NextB, string? BranchA, string? BranchB, bool Generation = false);

public sealed class CampaignSystem
{
    private readonly World _w;
    public bool Active { get; private set; }
    public Mission? Current { get; private set; }
    public List<(Mission m, bool ok, long tick, string note)> Done { get; } = new();
    public string? Choice { get; private set; }
    private int _deaths0;
    private long _start;
    private int _voyage0;

    public static readonly Mission[] All =
    {
        new(1, "first", "첫 항해", "케레스 정거장까지 아무도 잃지 않고 닿는다.", MissionGoal.Arrive, null, 0, "mine", null, null, null),
        new(2, "mine", "소행성대의 돌", "희귀 소재 6개를 모아 다음 기항지에 판다.", MissionGoal.Deliver, ItemKind.Rare, 6, "fork", null, null, null),
        new(3, "fork", "갈림길", "구조 신호와 교역로가 동시에 들어왔다 — 회의가 정한다.", MissionGoal.Survive, null, 0, "trade", "explore", "교역의 길 (상선과 함께)", "탐사의 길 (미지의 성운으로)"),
        new(4, "trade", "상선단", "두 번 기항해 교역하고, 남은 돈 60을 모은다.", MissionGoal.Deliver, null, 60, "colony", null, null, null),
        new(4, "explore", "미지의 성운", "성운을 지나며 새 기술 둘을 익힌다.", MissionGoal.Research, null, 2, "colony", null, null, null),
        new(5, "colony", "세대선", "목적지는 너무 멀다 — 배에서 아이가 태어나 자라고, 다음 세대가 배를 맡는다.", MissionGoal.Survive, null, 0, null, null, null, null, Generation: true),
    };

    /// <summary>항해를 시작할 때의 방식 (Tuning "mode.campaign": 0 자유 · 1 캠페인 · 2 세대선) — 저장 파일 머리에 남는다.</summary>
    public static float ModeValue { get; set; }

    public CampaignSystem(World w) => _w = w;

    public void Start()
    {
        Active = true;
        Begin(All[0]);
    }

    private void Begin(Mission m)
    {
        var w = _w;
        Current = m;
        _deaths0 = w.History.Deaths;
        _start = w.Tick;
        _voyage0 = w.Voyage.Number;
        if (m.Generation) w.Generation.Enable();
        w.RaiseAlert($"{m.Chapter}장 — {m.Title}: {m.Brief}", null, AlertLevel.Notice, shipWide: true);
        w.History.Add(w, HistoryKind.Decision, $"{m.Chapter}장 「{m.Title}」 — {m.Brief}", null, log: true);
    }

    /// <summary>진행 정도 (0~1)와 한 줄.</summary>
    public (float progress, string text) Status()
    {
        var w = _w;
        var m = Current;
        if (m == null) return (1f, "캠페인 끝");
        return m.Goal switch
        {
            MissionGoal.Arrive => (w.Voyage.DoneDays / MathF.Max(1f, w.Voyage.TotalDays), $"{w.Voyage.Destination}까지 {w.Voyage.TotalDays - w.Voyage.DoneDays:0.0}일 · 잃은 사람 {w.History.Deaths - _deaths0}"),
            MissionGoal.Deliver when m.Item is ItemKind k => (MathF.Min(1f, w.Ship.CountStored(k) / (float)m.Amount), $"{ItemKinds.Name(k)} {w.Ship.CountStored(k)}/{m.Amount}"),
            MissionGoal.Deliver => (MathF.Min(1f, w.Voyage.Credits / m.Amount), $"돈 {w.Voyage.Credits:0}/{m.Amount}"),
            MissionGoal.Research => (MathF.Min(1f, (w.Eras.Known.Count - KnownAtStart) / (float)m.Amount), $"새 기술 {w.Eras.Known.Count - KnownAtStart}/{m.Amount}"),
            _ => (MathF.Min(1f, (w.Tick - _start) / (float)(SimTime.TicksPerDay * (m.Generation ? 60L : 4L))), m.Generation ? $"세대선 {w.Generation.Births}명 태어남 · 어린이 {w.Crew.Count(c => !c.Dead && c.IsChild)}" : "버틴다"),
        };
    }

    public int KnownAtStart { get; private set; }

    /// <summary>시스템 틱 (한 시간에 한 번이면 충분).</summary>
    public void Update()
    {
        if (!Active || Current is not Mission m) return;
        var w = _w;
        bool arrived = w.Voyage.Number > _voyage0;
        bool ok = m.Goal switch
        {
            MissionGoal.Arrive => arrived,
            MissionGoal.Deliver when m.Item is ItemKind k => w.Ship.CountStored(k) >= m.Amount && w.Voyage.Current.Kind == LegKind.Port,
            MissionGoal.Deliver => w.Voyage.Credits >= m.Amount,
            MissionGoal.Research => w.Eras.Known.Count - KnownAtStart >= m.Amount,
            _ => !m.Generation && w.Tick - _start > SimTime.TicksPerDay * 4L,
        };
        bool failed = m.Goal == MissionGoal.Arrive && arrived && w.History.Deaths > _deaths0;
        if (!ok && !failed) return;
        Done.Add((m, ok && !failed, w.Tick, failed ? $"{w.History.Deaths - _deaths0}명을 잃었다" : "해냈다"));
        w.History.Add(w, HistoryKind.Decision, $"{m.Chapter}장 「{m.Title}」 — {(failed ? $"닿았지만 {w.History.Deaths - _deaths0}명을 잃었다" : "해냈다")}", null, log: true);
        string? next = m.NextA;
        if (m.NextB != null)
        {
            // 갈림길: 배의 사정으로 회의가 정한다 — 물자가 넉넉하고 다친 사람이 적으면 탐사, 아니면 교역
            bool rich = w.Ship.CountStored(ItemKind.Plate) >= 10 && w.Crew.Count(c => !c.Dead && c.Vitals.Injury > 0.2f) == 0;
            bool curious = w.Crew.Count(c => !c.Dead && c.Value == CrewValue.Freedom) >= 2;
            next = rich || curious ? m.NextB : m.NextA;
            Choice = next == m.NextB ? m.BranchB : m.BranchA;
            w.History.Add(w, HistoryKind.Decision, $"회의: {Choice} — " + (rich ? "물자가 넉넉하다" : curious ? "가 보고 싶다는 사람이 많다" : "안전한 길을 고른다"), null, log: true);
        }
        if (next == null) { Current = null; return; }
        KnownAtStart = w.Eras.Known.Count;
        Begin(All.First(x => x.Id == next));
    }
}
