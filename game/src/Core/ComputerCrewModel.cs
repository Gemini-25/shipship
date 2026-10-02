using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.16 주컴퓨터 두뇌 2.0 — 승무원 개인 모형 (관찰로 배운다 · 틀릴 수 있다).
//  한 시간마다 컴퓨터가 볼 수 있는 사람(데이터선이 닿는 방 · 생체 감시)만 본다: 기력(조금 흐리게) · 일할 때 기력이 주는 빠르기 ·
//    하루 중 지치는 시각 · 하는 일의 솜씨(일하는 모습으로 — 지친 사람은 서툴러 보인다 → 모형이 틀린다).
//  싫어하는 당번: 당번표를 받은 사람이 싫은 일이면 투덜댄다(승무원 행동) — 컴퓨터는 들은 만큼 배운다 (참는 사람의 싫음은 모른다).
//  쓰임: 당번표(ComputerV16.MakeRoster — 지친 사람 · 싫어하는 사람을 피하고 솜씨 있는 사람에게) · 작업 요청(Chores.Appeal 훅 — 믿는 만큼 따른다) ·
//    쉬라는 부탁(지친 사람은 급하지 않은 일을 덜 맡는다) · 부탁을 따랐나(습관) · 회의에서 컴퓨터 안건에 어떻게 표를 던졌나(습관).
//  사생활 ↔ 안전: 방침 "사생활 — 개인 공간 존중"이면 침실 안은 보지 않는다. 오래 못 봤거나 쓰러졌으면 생체 신호만 본다(윤리 갈등으로 기록 · 말한다).

public sealed class CrewProfile
{
    public int Id { get; init; }
    public float[] Skill { get; } = Enumerable.Repeat(0.5f, 7).ToArray();
    public int[] SkillSeen { get; } = new int[7];
    public float Rest { get; set; } = 0.7f;
    public long RestSeen { get; set; } = -1;
    public float Drain { get; set; } = 0.05f;
    public float TiredHour { get; set; } = -1f;
    public Dictionary<string, float> Aversion { get; } = new();
    public Dictionary<string, int> Grumbles { get; } = new();
    public int Asked, Followed, Ignored, Rejects, Accepts, Explained, Seen;
    public long LastExplained { get; set; } = -1;
    public float Aversion0(string duty) => Aversion.TryGetValue(duty, out var v) ? v : 0.3f;
}

/// <summary>컴퓨터가 한 사람에게 한 부탁 (작업 하나 · 기한).</summary>
public sealed record WorkAsk(int CrewId, int OrderId, string Title, string Why, long Until, long Tick);

public sealed class CrewModelBook
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 5807 + 151));
    private readonly Dictionary<int, CrewProfile> _p = new();
    private readonly Dictionary<int, WorkAsk> _asks = new();
    private readonly Dictionary<int, long> _rest = new();
    private readonly Dictionary<int, long> _privacyAt = new();
    private long _next;
    public int Observed, PrivacyOverrides, RestAsks, WorkAsks, Grumbles, Followed;

    public static readonly string[] Duties = { "야간 당직", "조리", "청소", "정비 순찰", "선외 작업" };

    public CrewModelBook(World w) => _w = w;

    public CrewProfile Of(CrewMember c) => _p.TryGetValue(c.Id, out var p) ? p : _p[c.Id] = new CrewProfile { Id = c.Id };
    public bool Knows(CrewMember c) => _p.TryGetValue(c.Id, out var p) && p.RestSeen >= 0;
    public IReadOnlyDictionary<int, WorkAsk> Asks => _asks;
    public bool RestAsked(CrewMember c) => _rest.TryGetValue(c.Id, out var t) && t > _w.Tick;
    /// <summary>쉬라는 부탁을 받은 사람 (화면).</summary>
    public IEnumerable<int> Resting => _rest.Where(kv => kv.Value > _w.Tick).Select(kv => kv.Key);

    /// <summary>실제로 싫어하는 정도 (세계 — 컴퓨터는 모른다): 성격 · 습관 · 취미.</summary>
    public static float TrueAversion(CrewMember c, string duty)
    {
        bool H(Habit h) => c.Habits.Contains(h);
        return duty switch
        {
            "조리" => c.Hobbies.Contains(Hobby.Cooking) || c.Hobbies.Contains(Hobby.Baking) || c.Role == CrewRole.Cook ? 0.05f : 0.25f + 0.35f * (1f - c.Traits.Diligence),
            "청소" => H(Habit.NeatFreak) ? 0f : H(Habit.Messy) ? 0.85f : 0.2f + 0.4f * (1f - c.Traits.Diligence),
            "야간 당직" => H(Habit.NightOwl) ? 0f : H(Habit.EarlyBird) || H(Habit.HeavySleeper) ? 0.8f : 0.35f,
            "정비 순찰" => H(Habit.Tinkerer) ? 0f : Math.Clamp(0.55f - 0.5f * c.SkillLevel(Skill.Mechanics), 0f, 1f),
            "선외 작업" => Math.Clamp(1f - c.Traits.Bravery + c.Memory.Trauma * 0.5f, 0f, 1f),
            _ => 0.3f,
        };
    }

    /// <summary>컴퓨터가 지금 이 사람을 볼 수 있나 (사생활 방침 · 안전이면 생체 신호만).</summary>
    private bool Visible(CrewMember c, out bool vitalsOnly)
    {
        var w = _w;
        var a = w.Automation;
        vitalsOnly = false;
        if (c.Dead || c.Away || c.Outside || c.Room == null || c.Room.Detached) return false;
        if (!c.Room.DataLinked && !a.Has(ComputerModule.BioMonitor)) return false;
        if (w.Policies["privacy"] == 1 && c.Room.Type is RoomType.Quarters or RoomType.QuietQuarters)
        {
            var p = Of(c);
            bool worry = c.Down || c.Vitals.Health < 0.4f || p.RestSeen >= 0 && w.Tick - p.RestSeen > SimTime.Hours(14) && p.Rest < 0.35f;
            if (!worry) return false;
            vitalsOnly = true;
            if (_privacyAt.GetValueOrDefault(c.Id, -SimTime.TicksPerDay) < w.Tick - SimTime.TicksPerDay)
            {
                _privacyAt[c.Id] = w.Tick;
                PrivacyOverrides++;
                string why = c.Down ? "쓰러졌다" : c.Vitals.Health < 0.4f ? $"건강 {c.Vitals.Health * 100:0}%" : $"{(w.Tick - p.RestSeen) / (float)SimTime.TicksPerHour:0}시간 못 봤고 마지막 기력 {p.Rest * 100:0}%";
                a.Authority.Dilemma("사생활 ↔ 안전", $"{c.Name} 침실 — {why}", "생체 신호만 봤다 (말소리 · 화면은 보지 않는다)",
                    "방침 '개인 공간 존중' — 안전이 걸리면 생체 신호만", new[] { c });
                if (c.Value == CrewValue.Freedom) a.Trusts.Change(c, -0.02f, "침실까지 들여다봤다", quiet: true);
                else if (c.Value == CrewValue.People || c.Value == CrewValue.Safety) a.Trusts.Change(c, 0.01f, "몸이 안 좋을 때 살펴 줬다", quiet: true);
                if (w.Automation.Apps is ComputerApps apps) apps.Messages.Add(new PersonalMessage(w.Tick, c.Id, "사생활", $"{why} — 침실 생체 신호만 확인했다 (방침: 개인 공간 존중)"));
            }
        }
        return true;
    }

    /// <summary>한 시간마다 볼 수 있는 사람을 본다.</summary>
    public void Update(bool force = false)
    {
        var w = _w;
        var a = w.Automation;
        if (!a.Present || !a.MainOnline) return;
        if (!force && w.Tick < _next) return;
        _next = w.Tick + SimTime.Hours(1);
        float hour = SimTime.HourOfDay(w.Tick);
        foreach (var c in w.Crew)
        {
            if (c.IsChild || !Visible(c, out bool vitalsOnly)) continue;
            var p = Of(c);
            Observed++;
            p.Seen++;
            float rest = Math.Clamp(c.Needs.Rest + R.Range(-0.04f, 0.04f), 0f, 1f);
            bool working = !vitalsOnly && (c.Job?.Order != null || ChoresActivity.OnShiftStatic(c, w)) && c.Pose != Pose.Sleeping;
            if (p.RestSeen >= 0 && working)
            {
                float h = (w.Tick - p.RestSeen) / (float)SimTime.TicksPerHour;
                if (h > 0.5f && h < 4f && p.Rest > rest) p.Drain = 0.7f * p.Drain + 0.3f * MathF.Min(0.3f, (p.Rest - rest) / h);
            }
            if (rest < 0.3f && p.Rest >= 0.3f && c.Pose != Pose.Sleeping) p.TiredHour = p.TiredHour < 0f ? hour : 0.7f * p.TiredHour + 0.3f * hour;
            p.Rest = rest;
            p.RestSeen = w.Tick;
            if (!vitalsOnly && c.Job?.Order is WorkOrder o && c.Pose == Pose.Working)
            {
                int s = (int)o.Skill;
                float obs = c.SkillLevel(o.Skill) + R.Range(-0.12f, 0.12f) - 0.2f * (1f - c.Needs.Rest); // 지친 사람은 서툴러 보인다 (모형이 틀리는 까닭)
                p.Skill[s] = Math.Clamp(p.Skill[s] + 0.3f * (obs - p.Skill[s]), 0f, 1f);
                p.SkillSeen[s]++;
                if (p.SkillSeen[s] == 4) a.Authority.Learned("사람", $"{c.Name} {Skills.Name(o.Skill)} 솜씨 {p.Skill[s] * 100:0}%쯤 (일하는 모습 {p.SkillSeen[s]}번)");
            }
            // 부탁을 따랐나 (습관)
            if (_asks.TryGetValue(c.Id, out var ask))
            {
                if (c.Job?.Order?.Id == ask.OrderId) { p.Followed++; Followed++; _asks.Remove(c.Id); }
                else if (w.Tick > ask.Until) { p.Ignored++; _asks.Remove(c.Id); if (p.Ignored == 3) a.Authority.Learned("사람", $"{Ko.EunNeun(c.Name)} 컴퓨터 부탁을 잘 안 듣는다 ({p.Followed}/{p.Followed + p.Ignored})"); }
            }
        }
    }

    /// <summary>지금 기력 짐작 (본 뒤로 지난 시간만큼 줄인다).</summary>
    public float RestNow(CrewMember c)
    {
        var p = Of(c);
        if (p.RestSeen < 0) return c.Needs.Rest > 0.5f ? 0.65f : 0.6f; // 본 적 없다 — 대충
        float h = (_w.Tick - p.RestSeen) / (float)SimTime.TicksPerHour;
        return Math.Clamp(p.Rest - p.Drain * MathF.Min(h, 8f) * 0.6f, 0f, 1f);
    }

    /// <summary>당번 하나를 맡기는 값 (낮을수록 맡긴다): 지침 · 싫어함 · 솜씨.</summary>
    public float DutyCost(CrewMember c, string duty)
    {
        var p = Of(c);
        float rest = RestNow(c);
        float hours = duty == "야간 당직" ? 8f : 2f;
        float after = rest - p.Drain * hours;
        float cost = rest < 0.3f ? 3f : after < 0.2f ? 1.5f : rest < 0.45f ? 0.8f : 0f;
        cost += 1.2f * p.Aversion0(duty);
        cost -= duty switch { "조리" => 0.8f * p.Skill[(int)Skill.Cooking], "정비 순찰" => 0.8f * p.Skill[(int)Skill.Mechanics], _ => 0f };
        return cost;
    }

    /// <summary>당번을 받은 사람의 반응 (싫으면 투덜댄다) — 컴퓨터는 들은 만큼 배운다.</summary>
    public void OnDuty(CrewMember c, string duty)
    {
        var w = _w;
        var a = w.Automation;
        var p = Of(c);
        float truth = TrueAversion(c, duty);
        bool patient = c.Habits.Contains(Habit.Patient) || c.Habits.Contains(Habit.Follower);
        bool grumble = truth > 0.55f && R.Chance(patient ? 0.15f : 0.8f);
        bool heard = c.Room != null && (c.Room.DataLinked || a.Has(ComputerModule.Assistant)) && !(w.Policies["privacy"] == 1 && c.Room.Type is RoomType.Quarters or RoomType.QuietQuarters);
        float before = p.Aversion0(duty);
        if (grumble)
        {
            Grumbles++;
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.02f);
            w.Log.Add(w.Tick, LogKind.Life, Persona.Say(c, $"또 {duty}야? 컴퓨터는 내가 이거 싫어하는 걸 모르나"), c.Id);
            a.Trusts.Change(c, -0.01f, $"싫은 {duty} 당번을 맡겼다", quiet: true);
            if (heard) { p.Grumbles[duty] = p.Grumbles.GetValueOrDefault(duty) + 1; p.Aversion[duty] = before + 0.4f * (0.95f - before); }
        }
        else if (heard) p.Aversion[duty] = before + 0.12f * (0.15f - before);
        if (before < 0.6f && p.Aversion0(duty) >= 0.6f) a.Authority.Learned("사람", $"{Ko.EunNeun(c.Name)} {Ko.EulReul(duty)} 싫어한다 — 투덜댐 {p.Grumbles.GetValueOrDefault(duty)}번");
    }

    /// <summary>이 일을 가장 잘 할 사람 (솜씨 짐작 · 기력 · 싫어함 — 지친 사람은 빼고).</summary>
    public CrewMember? Best(Skill s, IEnumerable<CrewMember>? pool = null, string duty = "정비 순찰")
    {
        var w = _w;
        CrewMember? best = null;
        float bs = float.MinValue;
        foreach (var c in pool ?? w.Crew)
        {
            if (c.Dead || c.IsChild || !c.CanAct || c.Outside || c.Away) continue;
            float rest = RestNow(c);
            if (rest < 0.3f) continue;
            var p = Of(c);
            float sc = p.Skill[(int)s] + 0.3f * rest - 0.3f * p.Aversion0(duty) + (RestAsked(c) ? -1f : 0f);
            if (sc > bs || sc == bs && best != null && c.Id < best.Id) { bs = sc; best = c; }
        }
        return best;
    }

    /// <summary>작업 부탁 (믿는 만큼 따른다 — Chores.Appeal 훅).</summary>
    public void Ask(CrewMember c, WorkOrder o, string why, float hours = 4f)
    {
        var w = _w;
        if (_asks.TryGetValue(c.Id, out var old) && old.OrderId == o.Id) return;
        _asks[c.Id] = new WorkAsk(c.Id, o.Id, o.Title, why, w.Tick + SimTime.Hours(hours), w.Tick);
        Of(c).Asked++;
        WorkAsks++;
        w.Automation.Apps.Messages.Add(new PersonalMessage(w.Tick, c.Id, "부탁", $"{o.Title} 부탁 — {why}"));
        c.NextThinkTick = Math.Min(c.NextThinkTick, w.Tick + 1 + c.Id % 7);
    }

    /// <summary>쉬라는 부탁 (급하지 않은 일은 남에게).</summary>
    public void AskRest(CrewMember c, string why, float hours = 6f)
    {
        var w = _w;
        if (RestAsked(c)) return;
        _rest[c.Id] = w.Tick + SimTime.Hours(hours);
        RestAsks++;
        w.Automation.Apps.Messages.Add(new PersonalMessage(w.Tick, c.Id, "쉼", $"{why} — 급하지 않은 일은 다른 사람에게 부탁했다"));
        if (_asks.TryGetValue(c.Id, out _)) _asks.Remove(c.Id);
    }

    public void Forget(int crewId) { _asks.Remove(crewId); _rest.Remove(crewId); }

    /// <summary>Chores.Appeal 훅: 부탁받은 일은 더 하고 싶고, 쉬라는 부탁을 받으면 급하지 않은 일을 덜 한다 — 컴퓨터를 믿는 만큼.</summary>
    public float RequestBias(CrewMember c, WorkOrder o)
    {
        if (_asks.Count == 0 && _rest.Count == 0) return 0f;
        float b = 0f;
        if (_asks.TryGetValue(c.Id, out var ask) && ask.OrderId == o.Id && ask.Until > _w.Tick) b += 0.35f * _w.Automation.Trusts.Of(c);
        if (_rest.TryGetValue(c.Id, out var until) && until > _w.Tick && o.Urgency < 0.6f && Routine(o.Kind)) b -= 0.4f * _w.Automation.Trusts.Of(c); // 늘 하는 일만 (급한 일 · 사고 수습 · 관제석은 그대로)
        return b;
    }

    /// <summary>늘 하는 일 (쉬라는 부탁이 미루게 하는 일).</summary>
    public static bool Routine(WorkKind k) => k is WorkKind.Maintain or WorkKind.Tend or WorkKind.Harvest or WorkKind.Cook or WorkKind.Restock or WorkKind.Fabricate
        or WorkKind.PreventiveCheck or WorkKind.Calibrate or WorkKind.Upgrade or WorkKind.Train or WorkKind.Drill or WorkKind.CleanUp;

    /// <summary>한 줄 요약 (화면).</summary>
    public string Summary(CrewMember c)
    {
        var p = Of(c);
        if (p.RestSeen < 0) return "아직 못 봤다";
        int best = 0;
        for (int i = 1; i < 7; i++) if (p.SkillSeen[i] > 0 && (p.SkillSeen[best] == 0 || p.Skill[i] > p.Skill[best])) best = i;
        var hate = p.Aversion.Where(kv => kv.Value >= 0.6f).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();
        return $"기력 {RestNow(c) * 100:0}%" + (p.TiredHour >= 0f ? $" · {p.TiredHour:0}시쯤 지침" : "") + (p.SkillSeen[best] > 0 ? $" · {Skills.Name((Skill)best)} {p.Skill[best] * 100:0}%" : "")
               + (hate != null ? $" · {hate} 싫어함" : "") + (p.Asked > 0 ? $" · 부탁 {p.Followed}/{p.Asked}" : "");
    }

    internal void Hash(Action<long> I, Action<float> F)
    {
        I(Observed); I(PrivacyOverrides); I(RestAsks); I(WorkAsks); I(Grumbles); I(Followed); I(_asks.Count); I(_rest.Count);
        foreach (var c in _w.Crew) if (_p.TryGetValue(c.Id, out var p)) { F(p.Rest); F(p.Drain); I(p.Asked); }
    }
}
