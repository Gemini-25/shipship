using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v15.6 캠페인 임무 6 → 30: 4장(교역의 길 · 탐사의 길) 뒤로 갈래가 퍼지고 (5~11장), 모든 갈래는 12장 세대선으로 모인다.
// 목표는 배가 이미 가진 상태로 판정한다 — 기항지 · 구조 · 난파선 · 비축 · 사고 없는 날 · 쓰러지지 않고 버티기 · 새 식구 · 출생 · 관행 · 일상 · 사기.
// 새 임무에는 기한이 있다: 넘기면 대가를 치르고 다음 장으로 간다 (캠페인이 멈추지 않는다). 해내면 보상을 받는다.
// 어떤 임무는 항로를 튼다 (난파선 · 성운 · 방사선대 · 자유항 구간을 넣는다) — 구간의 효과는 항로 그대로다 (건지기 · 센서 · 태양 폭풍 · 교역).
// 갈림길은 회의 표결 말고도 배 사정(물자 · 다친 사람 · 사기 · 일손 · 용기)이나 지난 임무의 결과로 갈린다.

/// <summary>갈림길을 무엇으로 정하나 (Council = 3장의 회의 표결).</summary>
public enum MissionFork { Council, Supplies, Bravery, Wounds, Outcome, Morale, Crowd }

/// <summary>임무의 보상·대가 종류.</summary>
public enum MissionPay { None, Credits, Plate, Electronics, MedKit, Meal, Rare, Research, Calm, Stress, Air, Fuel, Repair, Wear }

/// <summary>새 갈림길이 고른 길.</summary>
public sealed record ForkPick(string Next, string Label, string Why);

public static class CampaignV15
{
    public static readonly Mission[] Rows =
    {
        // 5장 — 교역의 길은 상선단 호위로, 탐사의 길은 성운 한복판으로
        new(5, "convoy", "상선단 호위", "상선단과 함께 기항지 한 곳에 닿는다 (기항지마다 짐삯) — 그때 물자가 넉넉하면 난파선 수색을 맡는다.", MissionGoal.Ports, null, 1,
            "bazaar", "wreck", "보급항으로", "난파선 수색으로", Days: 16, Fork: MissionFork.Supplies, Reward: MissionPay.Credits, RewardN: 25, Cost: MissionPay.Credits, CostN: 15),
        new(5, "deepfield", "깊은 성운", "항로를 성운 한복판으로 튼다 (센서가 흐리고 전기가 튄다) — 그 안에서 새 기술 하나를 익힌다.", MissionGoal.Research, null, 1,
            "beacon", "survey", "구조 신호를 따라", "성운 더 깊이", Days: 12, Fork: MissionFork.Bravery, Reward: MissionPay.Research, RewardN: 30, Cost: MissionPay.Stress, CostN: 8, Detour: LegKind.Nebula),
        // 6장
        new(6, "bazaar", "보급항", "다음 장을 위해 금속판을 여섯 개 더 쌓는다 (정제기가 광석으로 더 만들고, 모으는 동안 개조에 쓰지 않는다).", MissionGoal.Stock, ItemKind.Plate, 6,
            "ledger", null, null, null, Days: 10, Reward: MissionPay.Calm, RewardN: 10, Cost: MissionPay.Stress, CostN: 6),
        new(6, "wreck", "난파선 수색", "상선단이 알려 준 난파선으로 항로를 튼다 — 한 척을 건진다. 다친 사람이 있으면 돌아가 쉰다.", MissionGoal.Salvage, null, 1,
            "clinic", "relic", "다친 사람부터", "난파선 더 안쪽으로", Days: 8, Fork: MissionFork.Wounds, Reward: MissionPay.Electronics, RewardN: 3, Cost: MissionPay.Stress, CostN: 6, Detour: LegKind.Derelict),
        new(6, "beacon", "구조 신호", "탈출 캡슐의 신호를 잡았다 — 회의가 건지기로 하면 한 명이라도 건진다 (추진제가 든다).", MissionGoal.Rescue, null, 1,
            "newcomers", "vigil", "건진 사람들과", "건지지 못한 사람들을 기억하며", Days: 3, Fork: MissionFork.Outcome, Reward: MissionPay.MedKit, RewardN: 2, Cost: MissionPay.Stress, CostN: 8),
        new(6, "survey", "미지 탐사", "아무도 가 보지 않은 곳 — 나흘 동안 아무도 쓰러지지 않고 버틴다.", MissionGoal.Endure, null, 4,
            "relic", null, null, null, Reward: MissionPay.Research, RewardN: 40, Cost: MissionPay.Stress, CostN: 10),
        // 7장
        new(7, "ledger", "장부", "항로에 거래소 하나를 넣고 상선단의 짐을 다시 맡는다 (기항지마다 짐삯) — 캐 둔 얼음 여덟 개를 판다 (기항지는 여섯만 남기고 판다).", MissionGoal.Deliver, ItemKind.Ice, 8,
            "harbor", null, null, null, Days: 16, Reward: MissionPay.Plate, RewardN: 6, Cost: MissionPay.Stress, CostN: 6, Detour: LegKind.Port),
        new(7, "clinic", "의무실", "다친 사람이 나을 때까지 — 이틀 동안 사고 없이 지낸다 (작은 실수는 셈하지 않는다).", MissionGoal.Safe, null, 2,
            "drill", null, null, null, Days: 8, Reward: MissionPay.MedKit, RewardN: 3, Cost: MissionPay.Wear, CostN: 8),
        new(7, "relic", "옛 배의 유물", "난파선에서 본 희귀 소재 — 둘을 기항지에 판다 (기항지는 둘만 남기고 판다). 사기가 오르면 기록 보관소로.", MissionGoal.Deliver, ItemKind.Rare, 2,
            "drill", "archive", "숨을 고르며", "기세를 몰아", Days: 16, Fork: MissionFork.Morale, Reward: MissionPay.Research, RewardN: 40, Cost: MissionPay.Credits, CostN: 10),
        new(7, "newcomers", "새 식구", "건진 사람들이 배에 녹아든다 — 함께 먹고 자고 일하는 일상 열두 번.", MissionGoal.Daily, null, 12,
            "lifeline", null, null, null, Days: 5, Reward: MissionPay.Calm, RewardN: 12, Cost: MissionPay.Stress, CostN: 6),
        new(7, "vigil", "기억하는 배", "건지지 못한 사람들을 함께 기억하며 마음을 추스른다 — 사기를 70까지 되찾는다.", MissionGoal.Morale, null, 70,
            "archive", null, null, null, Days: 10, Reward: MissionPay.Calm, RewardN: 15, Cost: MissionPay.Stress, CostN: 10),
        // 8장
        new(8, "harbor", "두 항구", "항로에 자유항 하나를 넣는다 — 기항지 두 곳을 들른다. 일손이 모자라면 사람을 찾아 나선다.", MissionGoal.Ports, null, 2,
            "census", "granary", "사람을 찾아", "창고를 채우러", Days: 22, Fork: MissionFork.Crowd, Reward: MissionPay.Credits, RewardN: 30, Cost: MissionPay.Stress, CostN: 8, Detour: LegKind.Port),
        new(8, "drill", "비상 훈련", "훈련을 거듭한다 — 사흘 동안 사고 없이 지낸다.", MissionGoal.Safe, null, 3,
            "storm", null, null, null, Days: 10, Reward: MissionPay.Repair, RewardN: 10, Cost: MissionPay.Wear, CostN: 10),
        new(8, "archive", "기록 보관소", "모은 기록을 풀어 새 기술 둘을 익힌다.", MissionGoal.Research, null, 2,
            "school", "deepwreck", "배운 것을 가르치며", "오래된 난파선으로", Days: 16, Fork: MissionFork.Bravery, Reward: MissionPay.Research, RewardN: 60, Cost: MissionPay.Stress, CostN: 8),
        new(8, "lifeline", "또 하나의 신호", "다른 캡슐의 신호가 들어왔다 — 이번에도 건진다.", MissionGoal.Rescue, null, 1,
            "school", null, null, null, Days: 3, Reward: MissionPay.Meal, RewardN: 12, Cost: MissionPay.Stress, CostN: 10),
        // 9장
        new(9, "census", "사람을 찾아", "일손이 모자라다 — 떠도는 캡슐의 신호에 답하거나 기항지에서 사람을 구해 새 식구 한 명을 맞는다.", MissionGoal.Crew, null, 1,
            "homestead", null, null, null, Days: 16, Reward: MissionPay.Calm, RewardN: 10, Cost: MissionPay.Stress, CostN: 10),
        new(9, "granary", "자재 창고", "긴 항해를 앞두고 선체를 고칠 구조재를 네 개 더 쌓는다 (정제기가 더 만들고, 개조에 쓰지 않는다).", MissionGoal.Stock, ItemKind.Structure, 4,
            "homestead", null, null, null, Days: 10, Reward: MissionPay.Calm, RewardN: 10, Cost: MissionPay.Stress, CostN: 8),
        new(9, "storm", "폭풍 속으로", "지름길인 방사선대를 지난다 — 닷새 동안 아무도 쓰러지지 않는다.", MissionGoal.Endure, null, 5,
            "lastport", null, null, null, Reward: MissionPay.Fuel, RewardN: 15, Cost: MissionPay.Stress, CostN: 12, Detour: LegKind.RadiationBelt),
        new(9, "school", "배움터", "가르치고 배우는 날들 — 일상 스무 번.", MissionGoal.Daily, null, 20,
            "homestead", null, null, null, Days: 6, Reward: MissionPay.Research, RewardN: 30, Cost: MissionPay.Stress, CostN: 6),
        new(9, "deepwreck", "오래된 난파선", "기록에 남은 오래된 탐사선으로 항로를 튼다 — 건져 온다.", MissionGoal.Salvage, null, 1,
            "lastport", null, null, null, Days: 8, Reward: MissionPay.Rare, RewardN: 2, Cost: MissionPay.Stress, CostN: 8, Detour: LegKind.Derelict),
        // 10장
        new(10, "homestead", "정착 준비", "배를 집으로 — 떠도는 사람에게 문을 연다 (구조 신호 · 기항지 · 출생) · 새 식구 한 명을 더 맞는다.", MissionGoal.Crew, null, 1,
            "cradle", null, null, null, Days: 18, Reward: MissionPay.Plate, RewardN: 8, Cost: MissionPay.Stress, CostN: 8),
        new(10, "lastport", "마지막 기항지", "{dest}까지 아무도 잃지 않고 닿는다 — 여기서부터는 돌아갈 곳이 없다.", MissionGoal.Arrive, null, 0,
            "cradle", "keepers", "아이를 맞을 준비", "잃은 사람을 기리며", Fork: MissionFork.Outcome, Reward: MissionPay.Credits, RewardN: 40, Cost: MissionPay.Stress, CostN: 12),
        // 11장
        new(11, "cradle", "요람", "배가 세대선이 된다 (사흘이 한 해) — 첫 아이가 태어난다.", MissionGoal.Births, null, 1,
            "colony", null, null, null, Days: 24, Reward: MissionPay.Calm, RewardN: 15, Cost: MissionPay.Stress, CostN: 6),
        new(11, "keepers", "기억의 파수꾼", "잃은 사람의 이야기를 남긴다 — 관행이 새로 생기거나, 새 사람이 그 관행을 받아들인다.", MissionGoal.Customs, null, 1,
            "colony", null, null, null, Days: 18, Reward: MissionPay.Calm, RewardN: 20, Cost: MissionPay.Stress, CostN: 6),
    };

    /// <summary>임무가 항로에 넣는 구간의 이름.</summary>
    public static string DetourName(Mission m) => m.Id switch
    {
        "deepwreck" => "오래된 탐사선 '늘봄'",
        "ledger" => "거래소 '저울'",
        _ => m.Detour switch
        {
            LegKind.Derelict => "표류하는 화물선 '물푸레'",
            LegKind.Nebula => "성운의 심장",
            LegKind.RadiationBelt => "방사선대 지름길",
            LegKind.Port => "자유항 '등불'",
            _ => "순항",
        },
    };

    public static float DetourDays(LegKind k) => k switch { LegKind.Derelict => 0.5f, LegKind.Port => 1f, _ => 2f };

    public static string PayName(MissionPay p) => p switch
    {
        MissionPay.Credits => "돈", MissionPay.Plate => ItemKinds.Name(ItemKind.Plate), MissionPay.Electronics => ItemKinds.Name(ItemKind.Electronics),
        MissionPay.MedKit => ItemKinds.Name(ItemKind.MedKit), MissionPay.Meal => ItemKinds.Name(ItemKind.Meal), MissionPay.Rare => ItemKinds.Name(ItemKind.Rare),
        MissionPay.Research => "연구", MissionPay.Calm or MissionPay.Stress => "스트레스", MissionPay.Air => "공기 탱크", MissionPay.Fuel => "추진제",
        MissionPay.Repair or MissionPay.Wear => "설비 마모", _ => "",
    };

    public static ItemKind? PayItem(MissionPay p) => p switch
    {
        MissionPay.Plate => ItemKind.Plate, MissionPay.Electronics => ItemKind.Electronics, MissionPay.MedKit => ItemKind.MedKit,
        MissionPay.Meal => ItemKind.Meal, MissionPay.Rare => ItemKind.Rare, _ => null,
    };
}

public sealed partial class CampaignSystem
{
    // 임무를 시작할 때의 기준값 (v15.6)
    private int _ports0, _rescued0, _missed0, _salvaged0, _births0, _customs0, _adopt0, _daily0, _crew0, _stock0;
    private readonly HashSet<int> _down0 = new();

    /// <summary>관행을 받아들인 사람 (이유를 듣고 · 모르고 따라 하고).</summary>
    private int Adopted() => _w.Culture.Stats.Explained + _w.Culture.Stats.Imitated;

    private int SalvagedAll() => _w.Voyage.Past.Sum(s => s.Salvage) + _w.Voyage.Salvaged;

    /// <summary>시험용: 임무 시계를 앞당긴다 (기한 점검).</summary>
    internal void Rewind(float days) => _start -= (long)(days * SimTime.TicksPerDay);

    /// <summary>새 목표의 기준값을 잡고, 항로를 틀거나 신호를 받는다.</summary>
    private void BeginV15(Mission m)
    {
        var w = _w;
        _ports0 = w.Voyage.PortsVisited;
        _rescued0 = w.Comms.Rescued;
        _missed0 = w.Comms.SignalsMissed;
        _salvaged0 = SalvagedAll();
        _births0 = w.Generation.Births;
        _customs0 = w.Culture.Customs.Count;
        _adopt0 = Adopted();
        _daily0 = w.Daily.Stats.Fired;
        _crew0 = w.Crew.Count;
        _stock0 = m.Item is ItemKind k ? w.Ship.CountStored(k) : 0;
        _down0.Clear();
        foreach (var c in w.Crew) if (c.Down && !c.Dead) _down0.Add(c.Id);
        if (m.Detour is LegKind kind)
        {
            // 지금 구간 다음에 넣는다 (구간의 효과는 항로가 그대로 맡는다)
            var v = w.Voyage;
            var leg = new Leg { Kind = kind, Name = CampaignV15.DetourName(m), Days = CampaignV15.DetourDays(kind) };
            v.Legs.Insert(Math.Min(v.Index + 1, v.Legs.Count), leg);
            w.History.Add(w, HistoryKind.Decision, $"항로를 틀었다 — {v.Current.Name} 다음에 {leg.Name} ({VoyageSystem.KindName(kind)} · {leg.Days:0.#}일)", null, log: true);
        }
        // 구조 · 새 식구 임무: 떠도는 캡슐의 신호가 들어온다 (건질지는 회의 · 조타 · 추진제가 정한다)
        if (m.Goal is MissionGoal.Rescue or MissionGoal.Crew && !w.Comms.SignalOpen && w.Comms.PodEta < 0 && Hazards.Apply(w, HazardKind.RescueSignal, default, -1) == null)
            w.History.Add(w, HistoryKind.Decision, "구조 신호가 왔다지만 통신실이 잡지 못한다", null, log: true);
        if (m.Goal == MissionGoal.Births) w.Generation.Enable();
    }

    /// <summary>비축 임무: 모으는 물건은 목표만큼 개조에 쓰지 않는다.</summary>
    private int StockHold(ItemKind k) => Active && Current is { Goal: MissionGoal.Stock, Item: ItemKind ik } m && ik == k ? _stock0 + m.Amount : 0;

    /// <summary>마지막 사고(작은 실수 말고) 뒤로 흐른 날 — 임무를 시작한 뒤로만 센다.</summary>
    private float SafeDays()
    {
        var inc = _w.Causes.Incidents;
        long from = _start;
        for (int i = inc.Count - 1; i >= 0 && inc[i].Start > _start; i--)
            if (_w.Causes.Node(inc[i].Root).Kind != CauseKind.Mistake) { from = inc[i].Start; break; }
        return (_w.Tick - from) / (float)SimTime.TicksPerDay;
    }

    private static float Frac(int have, int need) => MathF.Min(1f, have / (float)Math.Max(1, need));

    /// <summary>새 목표를 해냈나 (옛 목표면 null).</summary>
    private bool? GoalV15(Mission m)
    {
        var w = _w;
        return m.Goal switch
        {
            MissionGoal.Endure => w.Tick - _start >= SimTime.TicksPerDay * (long)m.Amount,
            MissionGoal.Ports => w.Voyage.PortsVisited - _ports0 >= m.Amount,
            MissionGoal.Crew => w.Crew.Count - _crew0 >= m.Amount,
            MissionGoal.Stock when m.Item is ItemKind k => w.Ship.CountStored(k) >= _stock0 + m.Amount,
            MissionGoal.Safe => SafeDays() >= m.Amount,
            MissionGoal.Rescue => w.Comms.Rescued - _rescued0 >= m.Amount,
            MissionGoal.Salvage => SalvagedAll() - _salvaged0 >= m.Amount,
            MissionGoal.Births => w.Generation.Births - _births0 >= m.Amount,
            MissionGoal.Customs => w.Culture.Customs.Count - _customs0 + Adopted() - _adopt0 >= m.Amount,
            MissionGoal.Morale => w.Society.Morale * 100f >= m.Amount,
            MissionGoal.Daily => w.Daily.Stats.Fired - _daily0 >= m.Amount,
            _ => null,
        };
    }

    /// <summary>놓쳤나: 버티는 중에 누가 쓰러지거나 죽었다 · 구조 신호가 끊겼다 · 기한을 넘겼다.</summary>
    private string? MissV15(Mission m)
    {
        var w = _w;
        if (m.Goal == MissionGoal.Endure)
        {
            if (w.History.Deaths > _deaths0) return $"{w.History.Deaths - _deaths0}명을 잃었다";
            if (w.Crew.FirstOrDefault(c => c.Down && !c.Dead && !_down0.Contains(c.Id)) is CrewMember d) return $"{Ko.IGa(d.Name)} 쓰러졌다";
        }
        if (m.Goal == MissionGoal.Rescue && w.Comms.SignalsMissed > _missed0 && w.Comms.PodEta < 0) return "신호가 끊겼다 — 건지지 못했다";
        if (m.Days > 0 && w.Tick - _start > SimTime.TicksPerDay * (long)m.Days) return $"기한 {m.Days}일을 넘겼다";
        return null;
    }

    /// <summary>새 목표의 진행 (옛 목표면 null).</summary>
    private (float, string)? StatusV15(Mission m)
    {
        var w = _w;
        float days = (w.Tick - _start) / (float)SimTime.TicksPerDay;
        switch (m.Goal)
        {
            case MissionGoal.Endure:
                return (MathF.Min(1f, days / Math.Max(1, m.Amount)), $"버틴 날 {days:0.0}/{m.Amount}일 · 사망 {w.History.Deaths - _deaths0}");
            case MissionGoal.Ports:
            {
                int n = w.Voyage.PortsVisited - _ports0;
                var next = w.Voyage.Legs.Skip(w.Voyage.Index + 1).FirstOrDefault(l => l.Kind == LegKind.Port);
                return (Frac(n, m.Amount), $"들른 기항지 {n}/{m.Amount}" + (next != null ? $" · 다음 기항지 {next.Name}" : ""));
            }
            case MissionGoal.Crew:
            {
                int n = w.Crew.Count - _crew0;
                return (Frac(n, m.Amount), $"새 식구 {n}/{m.Amount} · 승무원 {w.Crew.Count(c => !c.Dead)}명 (처음 {w.StartCrew})");
            }
            case MissionGoal.Stock when m.Item is ItemKind k:
            {
                int have = w.Ship.CountStored(k);
                return (Frac(have - _stock0, m.Amount), $"{ItemKinds.Name(k)} {have}/{_stock0 + m.Amount}");
            }
            case MissionGoal.Safe:
            {
                float safe = SafeDays();
                return (MathF.Min(1f, safe / Math.Max(1, m.Amount)), $"사고 없이 {safe:0.0}/{m.Amount}일");
            }
            case MissionGoal.Rescue:
            {
                var cm = w.Comms;
                string sig = cm.PodEta >= 0 ? "캡슐이 오는 중" : cm.SignalOpen ? $"신호 {(cm.SignalUntil - w.Tick) / (float)SimTime.TicksPerHour:0}시간 남음" : "신호 없음";
                return (Frac(cm.Rescued - _rescued0, m.Amount), $"건진 사람 {cm.Rescued - _rescued0}/{m.Amount} · {sig}");
            }
            case MissionGoal.Salvage:
                return (Frac(SalvagedAll() - _salvaged0, m.Amount), $"건진 난파선 {SalvagedAll() - _salvaged0}/{m.Amount} · 지금 {w.Voyage.Current.Name}");
            case MissionGoal.Births:
                return (Frac(w.Generation.Births - _births0, m.Amount), $"태어남 {w.Generation.Births - _births0}/{m.Amount} · 짝 {w.Crew.Count(c => !c.Dead && c.Partner != null) / 2}쌍");
            case MissionGoal.Customs:
            {
                int born = w.Culture.Customs.Count - _customs0, took = Adopted() - _adopt0;
                return (Frac(born + took, m.Amount), $"새 관행 {born} · 받아들인 사람 {took} (목표 {m.Amount}) · 배의 관행 {w.Culture.Customs.Count}");
            }
            case MissionGoal.Morale:
            {
                float mo = w.Society.Morale;
                return (MathF.Min(1f, mo * 100f / Math.Max(1, m.Amount)), $"사기 {mo * 100f:0}/{m.Amount} ({SocietySystem.MoraleName(mo)})" + (w.Society.MoraleWhy.Length > 0 ? $" · {w.Society.MoraleWhy}" : ""));
            }
            case MissionGoal.Daily:
                return (Frac(w.Daily.Stats.Fired - _daily0, m.Amount), $"일상 {w.Daily.Stats.Fired - _daily0}/{m.Amount}");
            default:
                return null;
        }
    }

    /// <summary>기한이 있는 임무는 남은 날을 붙인다.</summary>
    private (float progress, string text) WithDays(Mission m, (float p, string t) s)
    {
        if (m.Days <= 0) return s;
        float left = m.Days - (_w.Tick - _start) / (float)SimTime.TicksPerDay;
        return (s.p, s.t + $" · 기한 {MathF.Max(0f, left):0.0}일");
    }

    /// <summary>새 갈림길: 배 사정이나 결과로 다음 임무를 고른다 (회의 갈림길·갈림길 없는 임무는 null).</summary>
    private ForkPick? ForkV15(Mission m, bool success)
    {
        if (m.NextB == null || m.NextA == null || m.Fork == MissionFork.Council) return null;
        var w = _w;
        var ship = w.Ship;
        var adults = w.Crew.Where(c => !c.Dead && !c.IsChild).ToList();
        bool b;
        string why;
        switch (m.Fork)
        {
            case MissionFork.Supplies:
            {
                int plates = ship.CountStored(ItemKind.Plate), food = ship.CountStored(ItemKind.Meal) + ship.CountStored(ItemKind.Produce), alive = w.Crew.Count(c => !c.Dead);
                b = plates >= 10 && food >= alive * 4;
                why = $"금속판 {plates} · 먹을 것 {food} ({alive}명) — " + (b ? "넉넉하다" : "모자라다");
                break;
            }
            case MissionFork.Bravery:
            {
                int bold = adults.Count(c => c.Traits.Bravery >= 0.5f), wary = adults.Count - bold;
                b = bold > wary;
                why = $"나서자 {bold} 대 조심하자 {wary}";
                break;
            }
            case MissionFork.Wounds:
            {
                var hurt = w.Crew.Where(c => !c.Dead && (c.Vitals.Injury > 0.2f || c.Down)).ToList();
                b = hurt.Count == 0;
                why = b ? "모두 멀쩡하다" : $"다친 사람 {hurt.Count}명 ({string.Join("·", hurt.Take(3).Select(c => c.Name))})";
                break;
            }
            case MissionFork.Morale:
            {
                float mo = w.Society.Morale;
                b = mo >= 0.6f;
                why = $"사기가 {SocietySystem.MoraleName(mo)} ({mo * 100:0})";
                break;
            }
            case MissionFork.Crowd:
                b = adults.Count >= w.StartCrew;
                why = $"일할 사람 {adults.Count}명 (처음 {w.StartCrew}) — " + (b ? "일손은 넉넉하다" : "일손이 모자라다");
                break;
            default: // Outcome
                b = !success;
                why = success ? "해냈다" : "해내지 못했다";
                break;
        }
        return new ForkPick(b ? m.NextB : m.NextA, (b ? m.BranchB : m.BranchA) ?? "", why);
    }

    /// <summary>해내면 보상, 놓치면 대가 — 기록에 붙일 한 줄 (" · 보상: 돈 +25").</summary>
    private string SettleV15(Mission m, bool ok)
    {
        var (p, n) = ok ? (m.Reward, m.RewardN) : (m.Cost, m.CostN);
        if (p == MissionPay.None || n <= 0) return "";
        return (ok ? " · 보상: " : " · 대가: ") + Pay(p, ok ? n : -n);
    }

    /// <summary>보상·대가를 배에 준다 (n이 음수면 잃는다). 스트레스·마모는 언제나 나쁜 쪽.</summary>
    private string Pay(MissionPay p, int n)
    {
        var w = _w;
        string name = CampaignV15.PayName(p);
        switch (p)
        {
            case MissionPay.Credits:
            {
                float before = w.Voyage.Credits;
                w.Voyage.Credits = MathF.Max(0f, before + n);
                return $"{name} {Signed(w.Voyage.Credits - before)}";
            }
            case MissionPay.Research:
            {
                float before = w.Research;
                w.Research = MathF.Max(0f, before + n);
                return $"{name} {Signed(w.Research - before)}";
            }
            case MissionPay.Calm or MissionPay.Stress:
            {
                float d = (p == MissionPay.Stress ? Math.Abs(n) : -n) / 100f;
                foreach (var c in w.Crew.Where(c => !c.Dead)) c.Needs.Stress = Math.Clamp(c.Needs.Stress + d, 0f, 1f);
                return $"{name} {Signed(d * 100f)}" + (d < 0f ? " (마음이 놓인다)" : " (마음이 무겁다)");
            }
            case MissionPay.Air:
            {
                var air = w.Air;
                air.Reserve = Math.Clamp(air.Reserve + air.ReserveCapacity * n / 100f, 0f, air.ReserveCapacity);
                return $"{name} {Signed(n)}% ({air.Reserve / air.ReserveCapacity * 100f:0}%)";
            }
            case MissionPay.Fuel:
            {
                var pr = w.Propulsion;
                pr.Propellant = Math.Clamp(pr.Propellant + pr.Capacity * n / 100f, 0f, pr.Capacity);
                return $"{name} {Signed(n)}% ({pr.Propellant / MathF.Max(1f, pr.Capacity) * 100f:0}%)";
            }
            case MissionPay.Repair or MissionPay.Wear:
            {
                float d = (p == MissionPay.Wear ? Math.Abs(n) : -n) / 100f;
                foreach (var mc in w.Ship.Machines) mc.Wear = Math.Clamp(mc.Wear + d, 0f, 1f);
                return $"{name} {Signed(d * 100f)}%" + (d < 0f ? " (손질했다)" : " (닳았다)");
            }
            default:
            {
                if (CampaignV15.PayItem(p) is not ItemKind k) return "";
                if (n > 0)
                {
                    int put = 0;
                    foreach (var box in w.Ship.Containers.Where(f => f.Storage!.Accepts(k)).OrderBy(f => f.Type == FurnitureType.Fridge ? 0 : 1))
                    {
                        put += box.Storage!.Add(k, n - put);
                        if (put >= n) break;
                    }
                    return $"{name} {Signed(put)}" + (put < n ? " (창고가 찼다)" : "");
                }
                int take = Math.Min(-n, w.Ship.CountStored(k));
                return $"{name} {Signed(take > 0 && Life.Take(w, k, take) ? -take : 0)}";
            }
        }
    }

    private static string Signed(float v) => v >= 0f ? $"+{v:0}" : $"−{-v:0}";
}
