using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.27 주컴퓨터 — 배의 한 구성원. 사고 대응을 넘어 평소 운영 · 함께 지냄 · 스스로 약점을 드러냄.
//  ① 승무원과 함께 (ComputerMateDay · Care · Drill · Talk): 아침 방송 · 저녁 항해 일지 · 돌봄과 사생활 · 훈련 설계 · 거절당했을 때 · 약속.
//  ② 미리 막기 (ComputerMateUpkeep · Sky): 고장 예지 정비(추세 → 남은 수명 범위 → 한가한 시간에 몰아) · 장기 물자 · 항로 · 우주 날씨 예보.
//  ③ 스스로 알아내고 고치기 (ComputerMateFix): 사각지대 정찰(로봇 · 드론) · 반복 고장 원인 → 개조안 → 회의 → 공사 · 늘어난 장비가 보인다.
//  ④ 약점과 사건 (ComputerMateFlaw): 기억이 틀어짐 → 승무원이 바로잡음 → 자기 기억 검사 · 과부하로 놓친 경보 · 성격 탓 실수 · 권한 밖 딜레마.
// 세계 ≠ 컴퓨터가 아는 것: 컴퓨터는 감지기 · 데이터선 · 방송으로 보고 말한다. 승무원은 들은 만큼 알고, 겪은 만큼 기억한다.

public sealed partial class ShipMate
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7333 + 1027));
    /// <summary>시험용: v16.27 전체를 끈다 (전/후 비교).</summary>
    public static bool Off { get; set; }

    public ShipMate(World w) => _w = w;

    private AutomationSystem A => _w.Automation;
    private bool Up => A.Present && A.MainOnline;
    private long _tenNext, _hourNext;

    /// <summary>컴퓨터의 말 한 줄 (기록 · 원하면 방송).</summary>
    internal void Say(string text, Room? about = null, int priority = -1, int crew = -1)
    {
        var w = _w;
        var a = A;
        string line = a.Manner.Speak(text);
        w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: {line}", crew);
        if (priority >= 0) a.Speak.Announce(a.Voice.Style(line), about, priority);
    }

    private CrewMember? Crew(int id) { foreach (var c in _w.Crew) if (c.Id == id) return c; return null; }
    private IEnumerable<CrewMember> Adults => _w.Crew.Where(c => !c.Dead && !c.IsChild);
    private static float Hour(long tick) => SimTime.HourOfDay(tick);

    public void Update(float dt)
    {
        var w = _w;
        if (Off || !A.Present) return;
        SkyTick(); // 폭풍 · 운석우가 실제로 왔나 (가볍다)
        FlawTick(); // 과부하 경보 · 딜레마 (가볍다 — 경보 번호만 본다)
        if (w.Tick < _tenNext) return;
        _tenNext = w.Tick + SimTime.Minutes(10);
        DrillTick();
        TalkTick();
        FixTick();
        CareTick();
        if (w.Tick < _hourNext) return;
        _hourNext = w.Tick + SimTime.TicksPerHour;
        if (!Up) return;
        DayTick();
        UpkeepTick();
        SupplyTick();
        SkyHourly();
        CareHour();
        FlawHour();
    }

    /// <summary>Meetings.Hold 훅: 살핌 범위 · 개조안.</summary>
    public void Agenda(MeetingRecord rec, List<CrewMember> attendees, CrewMember chair)
    {
        if (Off || !A.Present) return;
        var voters = attendees.Where(c => !c.IsChild && !c.Dead).ToList();
        if (voters.Count < 2) return;
        if (_w.Tick >= SimTime.TicksPerDay / 2) CareAgenda(rec, voters, chair);
        UpgradeAgenda(rec, voters, chair);
    }

    internal void Hash(Action<long> I, Action<float> F)
    {
        I(Briefings.Count); I(Evenings.Count); I(CareLevel); I(Hints); I(EarlyRests); I(Adjusts); I(Snoops); I(SnoopsFound);
        I(Drills.Count); I(BillChanges); F(MusterFactor); I(Refusals.Count); I(Promises.Count); I(PromisesKept); I(PromisesBroken);
        I(Trends.Count); I(Slots.Count); I(SlotsDone); I(SlotsMissed); I(Supplies.Count); I(Forecasts.Count); F(SkyScore);
        I(Scouts.Count); I(ScoutsSeen); I(Upgrades.Count); I(UpgradesDone); I(Memory.Count); I(Corruptions); I(Corrections); I(MemoryChecks);
        I(MissedAlarms.Count); I(Admitted); I(Dilemmas.Count); I(Reviews.Count);
    }
}

public sealed partial class AutomationSystem
{
    private ShipMate? _mate;
    /// <summary>v16.27 배의 한 구성원 (아침 방송 · 일지 · 돌봄 · 훈련 · 약속 · 예지 정비 · 물자 · 날씨 · 정찰 · 개조 · 기억 · 딜레마).</summary>
    public ShipMate Mate => _mate ??= new ShipMate(_world);
    public ShipMate? MateOrNull => _mate;

    private void Brain27(float dt) => Mate.Update(dt);
    internal void Hash27(Action<long> I, Action<float> F) => _mate?.Hash(I, F);
}
