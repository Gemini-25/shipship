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
        new(1, "first", "첫 항해", "{dest}까지 아무도 잃지 않고 닿는다.", MissionGoal.Arrive, null, 0, "mine", null, null, null),
        new(2, "mine", "소행성대의 돌", "소행성대 광맥에서 희귀 소재를 캐어 기항지에 4개 판다 (기항지는 둘만 남기고 판다).", MissionGoal.Deliver, ItemKind.Rare, 4, "fork", null, null, null),
        new(3, "fork", "갈림길", "구조 신호와 교역로가 동시에 들어왔다 — 회의가 정한다.", MissionGoal.Survive, null, 0, "trade", "explore", "교역의 길 (상선과 함께)", "탐사의 길 (미지의 성운으로)"),
        new(4, "trade", "상선단", "상선단의 짐을 실어 나르며(기항지마다 짐삯) 돈 60을 모은다 — 꼭 필요한 것만 산다.", MissionGoal.Deliver, null, 60, "colony", null, null, null),
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

    /// <summary>시험·화면 점검용: 곧장 그 장으로.</summary>
    public void JumpTo(string id)
    {
        Active = true;
        KnownAtStart = _w.Eras.Known.Count;
        Begin(All.First(x => x.Id == id));
    }

    private void Begin(Mission m)
    {
        var w = _w;
        Current = m;
        _deaths0 = w.History.Deaths;
        _start = w.Tick;
        _voyage0 = w.Voyage.Number;
        _sold0 = new Dictionary<ItemKind, int>(w.Voyage.Sold);
        if (m.Generation) w.Generation.Enable();
        w.Voyage.Reserve = m.Goal == MissionGoal.Deliver && m.Item == null ? m.Amount : 0f;
        w.RaiseAlert($"{m.Chapter}장 — {m.Title}: {BriefOf(m)}", null, AlertLevel.Notice, shipWide: true);
        w.History.Add(w, HistoryKind.Decision, $"{m.Chapter}장 「{m.Title}」 — {BriefOf(m)}", null, log: true);
    }

    /// <summary>상선단 임무 중에는 기항지마다 짐삯을 받는다.</summary>
    public float ContractFee() => Active && Current?.Id == "trade" ? 12f : 0f;

    /// <summary>임무가 모으는 물건은 개조에 쓰지 않는다 (팔 만큼 + 기항지가 남기는 둘).</summary>
    public int Holds(ItemKind k) => Active && Current is { Goal: MissionGoal.Deliver, Item: ItemKind ik } m && ik == k ? m.Amount + 2 : 0;

    /// <summary>배에서 태어난 아이 중 맏이 (살아 있는).</summary>
    private CrewMember? Eldest() => _w.Crew.Where(c => !c.Dead && c.BornAboard).OrderByDescending(c => c.Age).FirstOrDefault();

    /// <summary>임무 설명 ({dest} = 지금 항해의 목적지).</summary>
    public string BriefOf(Mission m) => m.Brief.Replace("{dest}", _w.Voyage.Destination);

    /// <summary>진행 정도 (0~1)와 한 줄.</summary>
    public (float progress, string text) Status()
    {
        var w = _w;
        var m = Current;
        if (m == null) return (1f, "캠페인 끝");
        return m.Goal switch
        {
            MissionGoal.Arrive => (w.Voyage.DoneDays / MathF.Max(1f, w.Voyage.TotalDays), $"{w.Voyage.Destination}까지 {w.Voyage.TotalDays - w.Voyage.DoneDays:0.0}일 · 잃은 사람 {w.History.Deaths - _deaths0}"),
            MissionGoal.Deliver when m.Item is ItemKind k => (MathF.Min(1f, SoldSince(k) / (float)m.Amount), $"판 {ItemKinds.Name(k)} {SoldSince(k)}/{m.Amount} · 창고 {w.Ship.CountStored(k)}"),
            MissionGoal.Deliver => (MathF.Min(1f, w.Voyage.Credits / m.Amount), $"돈 {w.Voyage.Credits:0}/{m.Amount}"),
            MissionGoal.Research => (MathF.Min(1f, (w.Eras.Known.Count - KnownAtStart) / (float)m.Amount), $"새 기술 {w.Eras.Known.Count - KnownAtStart}/{m.Amount}"),
            _ when m.Generation => (Eldest() is CrewMember k ? MathF.Min(1f, k.Age / 14f) : 0f,
                $"태어남 {w.Generation.Births} · 어린이 {w.Crew.Count(c => !c.Dead && c.IsChild)}" + (Eldest() is CrewMember e ? $" · 맏이 {e.Name} {e.Age:0}살 / 14" : " · 아직 아이가 없다")),
            _ => (MathF.Min(1f, (w.Tick - _start) / (float)(SimTime.TicksPerDay * 4L)), "버틴다"),
        };
    }

    public int KnownAtStart { get; private set; }
    private Dictionary<ItemKind, int> _sold0 = new();

    /// <summary>이 임무를 시작한 뒤 기항지에 판 개수.</summary>
    private int SoldSince(ItemKind k) => _w.Voyage.Sold.GetValueOrDefault(k) - _sold0.GetValueOrDefault(k);

    /// <summary>시스템 틱 (한 시간에 한 번이면 충분).</summary>
    public void Update()
    {
        if (!Active || Current is not Mission m) return;
        var w = _w;
        bool arrived = w.Voyage.Number > _voyage0;
        bool ok = m.Goal switch
        {
            MissionGoal.Arrive => arrived,
            MissionGoal.Deliver when m.Item is ItemKind k => SoldSince(k) >= m.Amount,
            MissionGoal.Deliver => w.Voyage.Credits >= m.Amount,
            MissionGoal.Research => w.Eras.Known.Count - KnownAtStart >= m.Amount,
            _ when m.Generation => w.Generation.Comings > 0, // 배에서 태어난 아이가 열네 살이 되어 일을 맡는다 — 다음 세대
            _ => w.Tick - _start > SimTime.TicksPerDay * 4L,
        };
        bool failed = m.Goal == MissionGoal.Arrive && arrived && w.History.Deaths > _deaths0;
        if (!ok && !failed) return;
        Done.Add((m, ok && !failed, w.Tick, failed ? $"{w.History.Deaths - _deaths0}명을 잃었다" : "해냈다"));
        w.History.Add(w, HistoryKind.Decision, $"{m.Chapter}장 「{m.Title}」 — {(failed ? $"닿았지만 {w.History.Deaths - _deaths0}명을 잃었다" : m.Generation ? "다음 세대가 배를 맡았다 · 캠페인 끝 (배는 계속 간다)" : "해냈다")}", null, log: true);
        string? next = m.NextA;
        if (m.NextB != null)
        {
            // 갈림길: 회의에서 표결한다 — 자유를 중히 여기는 사람은 탐사, 안전·효율은 교역, 사람·규칙은 배 사정을 본다
            // (물자가 넉넉하고 다친 사람이 없으면 탐사, 아니면 교역). 돈이 바닥이면 교역에 한 표 더. 같으면 교역 (안전한 길)
            bool rich = w.Ship.CountStored(ItemKind.Plate) >= 10 && w.Crew.Count(c => !c.Dead && c.Vitals.Injury > 0.2f) == 0;
            int explore = 0, trade = 0;
            foreach (var c in w.Crew.Where(c => !c.Dead && !c.IsChild))
            {
                bool wantsOut = c.Value switch { CrewValue.Freedom => true, CrewValue.Safety or CrewValue.Efficiency => false, _ => rich };
                if (wantsOut) explore++; else trade++;
            }
            if (w.Voyage.Credits < 20f) trade++;
            next = explore > trade ? m.NextB : m.NextA;
            Choice = next == m.NextB ? m.BranchB : m.BranchA;
            w.History.Add(w, HistoryKind.Decision, $"회의: {Choice} — 탐사 {explore} 대 교역 {trade}" + (next == m.NextB ? (rich ? " · 물자가 넉넉하다" : " · 가 보고 싶다는 사람이 많다") : w.Voyage.Credits < 20f ? " · 돈이 바닥이다" : " · 안전한 길을 고른다"), null, log: true);
        }
        if (next == null) { Current = null; w.Voyage.Reserve = 0f; return; }
        KnownAtStart = w.Eras.Known.Count;
        Begin(All.First(x => x.Id == next));
    }
}

public sealed partial class WorkBoard
{
    /// <summary>캠페인 임무가 붙잡아 둔 물건 (개조 재료로 쓰지 않는다).</summary>
    public int Held(ItemKind k) => _world.Campaign.Holds(k);
}
