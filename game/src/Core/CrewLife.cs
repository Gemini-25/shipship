using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v12.7 승무원: 사람마다 살아온 길(경력)·가치관·습관·자격이 있고, 그것이 일과 다툼과 실수로 드러난다.
//   실수: 피곤하거나, 예민하거나, 자격 없이 하거나, 머리를 다쳤으면 — 사람답게 틀린다. 왜 틀렸는지가 기록과 인과 사슬에 남는다.
//   갈등: 예민한 둘이 가치관·습관이 부딪히면 말다툼 → 냉전 → 사교적인 사람이 중재한다.
//   돌봄: 아프거나 다친 사람을 가까운 사람이 찾아가 곁에 있어 준다.
//   죽음 뒤: 시신을 안치실(없으면 냉동 창고·창고)로 모시고, 다음 날 추모한다. 가까웠던 사람은 오래 슬퍼한다.

public enum Background { Miner, MilitaryTech, FarmResearcher, CargoPilot, MedStudent, Reporter, Teacher, Chef, Lineworker, Programmer, Artist, Athlete }
public enum CrewValue { Safety, Efficiency, People, Rules, Freedom }
public enum Habit { NightOwl, EarlyBird, NeatFreak, Messy, Talker, Loner, GymRat, Snacker, Worrier, Tinkerer }
public enum Qual { Reactor, Eva, Medic, Helm, Electrical }

public static class Life
{
    public static string Name(Background b) => b switch
    {
        Background.Miner => "소행성 광부", Background.MilitaryTech => "군 정비병", Background.FarmResearcher => "농업 연구원",
        Background.CargoPilot => "화물선 항해사", Background.MedStudent => "의대 중퇴", Background.Reporter => "기자",
        Background.Teacher => "교사", Background.Chef => "요리사", Background.Lineworker => "송전 기사", Background.Programmer => "프로그래머",
        Background.Artist => "화가", _ => "운동선수",
    };

    public static string Name(CrewValue v) => v switch
    {
        CrewValue.Safety => "안전 먼저", CrewValue.Efficiency => "효율 먼저", CrewValue.People => "사람 먼저", CrewValue.Rules => "규칙대로", _ => "제 방식대로",
    };

    public static string Name(Habit h) => h switch
    {
        Habit.NightOwl => "올빼미", Habit.EarlyBird => "아침형", Habit.NeatFreak => "정리광", Habit.Messy => "어지르기", Habit.Talker => "수다쟁이",
        Habit.Loner => "혼자가 편함", Habit.GymRat => "운동광", Habit.Snacker => "군것질", Habit.Worrier => "걱정이 많음", _ => "만지작거림",
    };

    public static string Name(Qual q) => q switch
    {
        Qual.Reactor => "원자로 운전", Qual.Eva => "선외 작업", Qual.Medic => "응급 의료", Qual.Helm => "조타", _ => "고압 전기",
    };

    /// <summary>경력이 주는 솜씨와 자격.</summary>
    public static (Skill skill, float bonus, Qual[] quals) Gift(Background b) => b switch
    {
        Background.Miner => (Skill.Mechanics, 0.08f, new[] { Qual.Eva }),
        Background.MilitaryTech => (Skill.Electrical, 0.08f, new[] { Qual.Eva, Qual.Electrical }),
        Background.FarmResearcher => (Skill.Botany, 0.1f, Array.Empty<Qual>()),
        Background.CargoPilot => (Skill.Piloting, 0.1f, new[] { Qual.Helm }),
        Background.MedStudent => (Skill.Medicine, 0.08f, new[] { Qual.Medic }),
        Background.Chef => (Skill.Cooking, 0.12f, Array.Empty<Qual>()),
        Background.Lineworker => (Skill.Electrical, 0.1f, new[] { Qual.Electrical }),
        Background.Programmer => (Skill.Engineering, 0.06f, Array.Empty<Qual>()),
        _ => (Skill.Mechanics, 0.03f, Array.Empty<Qual>()),
    };

    /// <summary>그 일을 제대로 하려면 필요한 자격 (없어도 하지만 서툴고 실수가 잦다).</summary>
    public static Qual? Needs(WorkOrder o) => o.Kind switch
    {
        WorkKind.RestartReactor => Qual.Reactor,
        WorkKind.ManualControl => Qual.Helm,
        WorkKind.Treat or WorkKind.Rescue => Qual.Medic,
        WorkKind.BreakerOn or WorkKind.IsolateRoom or WorkKind.ReplacePanel => Qual.Electrical,
        _ => o.External ? Qual.Eva : null,
    };

    /// <summary>역할이 처음부터 가진 자격.</summary>
    public static Qual[] RoleQuals(CrewRole r) => r switch
    {
        CrewRole.Engineer => new[] { Qual.Reactor, Qual.Electrical },
        CrewRole.Electrician => new[] { Qual.Electrical },
        CrewRole.Medic => new[] { Qual.Medic },
        CrewRole.Pilot => new[] { Qual.Helm },
        CrewRole.Technician => new[] { Qual.Eva },
        _ => Array.Empty<Qual>(),
    };

    /// <summary>항해를 시작할 때: 사람마다 경력·가치관·습관 (본 난수를 건드리지 않게 따로 뽑는다 — 기존 배의 역사가 그대로).</summary>
    public static void Assign(World w)
    {
        foreach (var c in w.Crew.Where(c => !c.Profiled))
        {
            c.Profiled = true;
            var rng = new Rng(unchecked(w.Seed * 7349 + c.Id * 104729 + 11));
            var bgs = Enum.GetValues<Background>();
            // 역할과 어울리는 경력이 조금 더 잦다
            var fit = c.Role switch
            {
                CrewRole.Medic => Background.MedStudent, CrewRole.Pilot => Background.CargoPilot, CrewRole.Electrician => Background.Lineworker,
                CrewRole.Botanist => Background.FarmResearcher, CrewRole.Cook => Background.Chef, CrewRole.Technician => Background.Miner,
                CrewRole.Engineer => Background.MilitaryTech, _ => bgs[rng.Range(0, bgs.Length)],
            };
            c.Background = rng.Chance(0.55f) ? fit : bgs[rng.Range(0, bgs.Length)];
            var vals = Enum.GetValues<CrewValue>();
            c.Value = vals[rng.Range(0, vals.Length)];
            var habits = Enum.GetValues<Habit>().ToList();
            c.Habits.Clear();
            int n = 1 + (rng.Chance(0.5f) ? 1 : 0);
            for (int i = 0; i < n && habits.Count > 0; i++)
            {
                var h = habits[rng.Range(0, habits.Count)];
                habits.Remove(h);
                // 서로 어긋나는 습관은 같이 갖지 않는다
                habits.RemoveAll(x => (h, x) is (Habit.NightOwl, Habit.EarlyBird) or (Habit.EarlyBird, Habit.NightOwl) or (Habit.NeatFreak, Habit.Messy)
                    or (Habit.Messy, Habit.NeatFreak) or (Habit.Talker, Habit.Loner) or (Habit.Loner, Habit.Talker));
                c.Habits.Add(h);
            }
            var (skill, bonus, quals) = Gift(c.Background);
            c.SkillLevels[(int)skill] = MathF.Min(1f, c.SkillLevels[(int)skill] + bonus);
            c.Quals.Clear();
            foreach (var q in RoleQuals(c.Role).Concat(quals)) c.Quals.Add(q);
            if (c.RawSkill(Skill.Medicine) >= 0.55f) c.Quals.Add(Qual.Medic);
            if (c.RawSkill(Skill.Piloting) >= 0.55f) c.Quals.Add(Qual.Helm);
        }
    }

    public static bool Has(CrewMember c, Habit h) => c.Habits.Contains(h);

    /// <summary>창고에서 꺼내 쓴다 (모자라면 아무것도 안 꺼낸다).</summary>
    public static bool Take(World w, ItemKind k, int n)
    {
        if (w.Ship.CountStored(k) < n) return false;
        foreach (var box in w.Ship.Containers)
        {
            n -= box.Storage!.Take(k, n);
            if (n <= 0) return true;
        }
        return n <= 0;
    }

    public static string Profile(CrewMember c) =>
        $"{Name(c.Background)} · {Name(c.Value)}" + (c.Habits.Count > 0 ? " · " + string.Join("·", c.Habits.Select(Name)) : "");

    /// <summary>일기 한 줄 (사람마다 최근 것만 남긴다).</summary>
    public static void Diary(World w, CrewMember c, string text)
    {
        c.Diary.Add((w.Tick, text));
        if (c.Diary.Count > 40) c.Diary.RemoveAt(0);
    }
}

public sealed class LifeStats
{
    public int Mistakes, Caught, Arguments, Feuds, Mediations, Visits, Funerals, BodiesMoved, QualsEarned, Prosthetics, LimbsLost;
    public override string ToString() =>
        $"실수 {Mistakes}(잡아냄 {Caught}) · 말다툼 {Arguments} · 냉전 {Feuds} · 중재 {Mediations} · 문병 {Visits} · 추모 {Funerals} · 시신 수습 {BodiesMoved} · 자격 {QualsEarned} · 팔다리 잃음 {LimbsLost} · 의수·의족 {Prosthetics}";
}

/// <summary>실수·다툼·문병·추모를 돌린다 (시스템 틱).</summary>
public sealed class LifeSystem
{
    private readonly World _w;
    public LifeStats Stats { get; } = new();
    private readonly List<(long due, int furnitureId, int node, string why)> _sloppy = new();
    private readonly List<(long at, int deadId)> _funerals = new();
    private readonly HashSet<int> _mourned = new();
    private readonly HashSet<(int, BodyPart)> _lost = new();
    public List<(string name, long tick, string cause)> Memorial { get; } = new();

    public LifeSystem(World w) => _w = w;

    // ── 실수 ──

    /// <summary>실수할 확률과 그 까닭 (가장 큰 것 하나).</summary>
    public (float p, string why) MistakeOdds(CrewMember c, WorkOrder o)
    {
        float p = 0.015f;
        var reasons = new List<(float w, string text)>();
        if (c.Needs.Rest < 0.25f) { p *= 4f; reasons.Add((4f, $"졸려서 (기력 {c.Needs.Rest * 100:0}%)")); }
        if (c.Needs.Stress > 0.65f) { p *= 2.5f; reasons.Add((2.5f, $"예민해서 (스트레스 {c.Needs.Stress * 100:0}%)")); }
        if (c.Needs.Food < 0.15f) { p *= 1.5f; reasons.Add((1.5f, "배가 고파 손이 떨려서")); }
        if (Life.Needs(o) is Qual q && !c.Quals.Contains(q)) { p *= 2.5f; reasons.Add((2.5f, $"{Life.Name(q)} 자격이 없어서")); }
        float head = Wounds.HeadLoad(c.Vitals);
        if (head > 1.05f) { p *= head; reasons.Add((head, "머리를 다쳐 판단이 흐려서")); }
        float hand = Wounds.HandFactor(c.Vitals);
        if (hand < 0.8f) { p *= 1.8f; reasons.Add((1.8f, "다친 팔이 말을 안 들어서")); }
        float skill = c.SkillLevel(o.Skill);
        if (skill < 0.35f) { p *= 1.8f; reasons.Add((1.8f, $"서툴러서 ({Skills.Name(o.Skill)} {skill * 100:0}%)")); }
        p *= 1.2f - 0.5f * c.Traits.Calm;
        if (_w.Eras.Has("checklist")) p *= 0.7f; // v12.8 점검표 문화
        if (Life.Has(c, Habit.Messy)) { p *= 1.4f; reasons.Add((1.4f, "덜렁대서")); }
        if (Life.Has(c, Habit.Worrier) || Life.Has(c, Habit.NeatFreak)) p *= 0.75f;
        if (c.Job?.Urgent == true) { p *= 1.5f; reasons.Add((1.5f, "급하게 하다가")); }
        string why = reasons.Count > 0 ? reasons.OrderByDescending(r => r.w).First().text : "깜빡해서";
        return (MathF.Min(0.5f, p), why);
    }

    /// <summary>일을 끝낸 뒤: 실수했는지 본다. 실수는 바로 드러나기도, 몇 시간 뒤 고장으로 드러나기도 한다.</summary>
    public void AfterWork(CrewMember c, WorkOrder o)
    {
        var w = _w;
        if (c.Dead || o.Kind is WorkKind.Train or WorkKind.Rehab or WorkKind.Handover) return;
        var (p, why) = MistakeOdds(c, o);
        if (!w.Rng.Chance(p)) return;
        Stats.Mistakes++;
        c.Stats.Mistakes++;
        string what;
        var m = o.Target.Furniture?.Machine;
        if (m != null && o.Kind is WorkKind.Repair or WorkKind.Maintain or WorkKind.PreventiveCheck or WorkKind.Upgrade)
        {
            // 덜 조였다 → 몇 시간 뒤 다시 고장
            what = $"{Ko.EulReul(m.Name)} 손보다 볼트를 덜 조였다";
            int node = w.Causes.Root(CauseKind.Mistake, $"{c.Name}의 실수 — {what} ({why})", m.Body.Room, m.Body.Center, observer: false);
            _sloppy.Add((w.Tick + SimTime.Hours(w.Rng.Range(2f, 8f)), m.Body.Id, node, why));
        }
        else if (o.Kind is WorkKind.Cook)
        {
            what = "조리하다 손을 덜 씻었다";
            var f = w.Ship.FurnitureOf(FurnitureType.Fridge).FirstOrDefault(x => x.Storage?.Count(ItemKind.Meal) > 0);
            int node = w.Causes.Root(CauseKind.Mistake, $"{c.Name}의 실수 — {what} ({why})", c.Room, c.Position, observer: false);
            if (f != null) using (w.Causes.Because(node)) w.Hazards.Taint(f);
        }
        else if (o.Kind is WorkKind.IsolateRoom or WorkKind.BreakerOn or WorkKind.ShutRoomValve or WorkKind.OpenRoomValve)
        {
            // 옆방 분전함을 잘못 내렸다
            var next = c.Room != null ? w.Ambience.Neighbors(c.Room).Select(x => x.room).FirstOrDefault(r => !r.Detached && !r.BreakerOff && r.Type != RoomType.Corridor) : null;
            what = next != null ? $"{next.Name} 분전함을 잘못 내렸다" : "스위치를 잘못 눌렀다";
            int node = w.Causes.Root(CauseKind.Mistake, $"{c.Name}의 실수 — {what} ({why})", next ?? c.Room, c.Position, observer: false);
            if (next != null) using (w.Causes.Because(node)) w.Moisture.Isolate(next, null);
        }
        else
        {
            what = "공구에 손을 다쳤다";
            w.Causes.Root(CauseKind.Mistake, $"{c.Name}의 실수 — {what} ({why})", c.Room, c.Position, observer: false);
            NeedsSystem.AddInjury(c.Vitals, 0.06f, "작업 중 실수");
        }
        // 곁에서 본 자격 있는 사람이 바로 잡아내기도 한다
        var watcher = w.Crew.FirstOrDefault(x => x != c && !x.Dead && x.IsAwake && x.Room == c.Room && x.SkillLevel(o.Skill) > c.SkillLevel(o.Skill) + 0.15f);
        if (watcher != null && w.Rng.Chance(0.5f) && _sloppy.Count > 0 && _sloppy[^1].furnitureId == (m?.Body.Id ?? -1))
        {
            _sloppy.RemoveAt(_sloppy.Count - 1);
            Stats.Caught++;
            w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(watcher.Name)} {c.Name}의 실수를 바로 잡아냈다 — {what}", watcher.Id);
            c.ChangeAffinity(watcher, 0.03f);
            return;
        }
        c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.05f);
        w.Log.Add(w.Tick, LogKind.Warning, $"실수: {what} — {why}", c.Id);
        Life.Diary(w, c, $"{what}. {why}.");
    }

    // ── 시스템 틱 ──
    public void Update(float dt)
    {
        var w = _w;
        if (w.Crew.Any(c => !c.Profiled)) Life.Assign(w); // 처음 탄 사람·건져 태운 사람
        // 덜 조인 설비가 다시 고장 난다
        for (int i = _sloppy.Count - 1; i >= 0; i--)
        {
            var (due, fid, node, why) = _sloppy[i];
            if (w.Tick < due) continue;
            _sloppy.RemoveAt(i);
            if (w.Ship.Furniture.FirstOrDefault(f => f.Id == fid)?.Machine is not Machine m || m.Faults.Count > 0) continue;
            using (w.Causes.Because(node)) w.Machines.Break(m);
        }
        Quarrels(dt);
        Mourning();
        Earn();
        // 팔다리를 잃은 일은 배의 역사에 남는다
        foreach (var c in w.Crew)
            foreach (var wd in c.Vitals.Wounds.Where(x => x.Lost))
                if (_lost.Add((c.Id, wd.Part)))
                {
                    Stats.LimbsLost++;
                    w.History.Add(w, HistoryKind.Damage, $"{Ko.IGa(c.Name)} {Ko.EulReul(Wounds.PartName(wd.Part))} 잃었다 ({wd.Cause})", c.Room, new[] { c }, log: true);
                    Life.Diary(w, c, $"{Ko.IGa(Wounds.PartName(wd.Part))} 없다. 아직 실감이 안 난다.");
                }
    }

    /// <summary>예민한 둘이 같은 방에서 부딪힌다 — 가치관·습관이 어긋날수록.</summary>
    private void Quarrels(float dt)
    {
        var w = _w;
        var awake = w.Crew.Where(c => !c.Dead && !c.Down && c.IsAwake && c.Room != null && c.Job?.Urgent != true).ToList();
        for (int i = 0; i < awake.Count; i++)
            for (int j = i + 1; j < awake.Count; j++)
            {
                var a = awake[i]; var b = awake[j];
                if (a.Room != b.Room) continue;
                float tension = (a.Needs.Stress + b.Needs.Stress) / 2f - 0.35f;
                if (tension <= 0f) continue;
                var (clash, about) = Clash(a, b);
                float p = tension * clash * 0.5f * dt * (1.2f - MathF.Max(0f, (a.AffinityTo(b) + b.AffinityTo(a)) / 2f));
                if (!w.Rng.Chance(p)) continue;
                Stats.Arguments++;
                a.ChangeAffinity(b, -0.15f); b.ChangeAffinity(a, -0.15f);
                a.Needs.Stress = MathF.Min(1f, a.Needs.Stress + 0.06f);
                b.Needs.Stress = MathF.Min(1f, b.Needs.Stress + 0.06f);
                a.Quarrel = b.Quarrel = w.Tick;
                bool feud = a.AffinityTo(b) < -0.45f;
                if (feud) Stats.Feuds++;
                string text = $"{Ko.WaGwa(a.Name)} {Ko.IGa(b.Name)} {about} 두고 말다툼했다" + (feud ? " — 서로 말을 안 한다" : "");
                w.Log.Add(w.Tick, LogKind.Life, text, a.Id);
                Life.Diary(w, a, $"{Ko.WaGwa(b.Name)} {about} 두고 다퉜다.");
                Life.Diary(w, b, $"{Ko.WaGwa(a.Name)} {about} 두고 다퉜다.");
                // 중재: 그 자리에 사교적인 사람이 있으면 바로 달랜다
                var mediator = awake.Where(x => x != a && x != b && x.Room == a.Room && x.Traits.Sociability > 0.6f).OrderByDescending(x => x.Traits.Sociability).FirstOrDefault();
                if (mediator != null && w.Rng.Chance(0.6f))
                {
                    Stats.Mediations++;
                    a.ChangeAffinity(b, 0.1f); b.ChangeAffinity(a, 0.1f);
                    a.ChangeAffinity(mediator, 0.04f); b.ChangeAffinity(mediator, 0.04f);
                    w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(mediator.Name)} 둘 사이를 달랬다", mediator.Id);
                }
            }
    }

    /// <summary>둘이 어긋나는 정도와 무엇 때문인지.</summary>
    public static (float clash, string about) Clash(CrewMember a, CrewMember b)
    {
        if ((a.Value, b.Value) is (CrewValue.Safety, CrewValue.Efficiency) or (CrewValue.Efficiency, CrewValue.Safety))
            return (1f, "안전이냐 효율이냐를");
        if ((a.Value, b.Value) is (CrewValue.Rules, CrewValue.Freedom) or (CrewValue.Freedom, CrewValue.Rules))
            return (0.9f, "규칙을 지키느냐를");
        if (Life.Has(a, Habit.NeatFreak) && Life.Has(b, Habit.Messy) || Life.Has(b, Habit.NeatFreak) && Life.Has(a, Habit.Messy))
            return (0.9f, "어질러 둔 공구를");
        if (Life.Has(a, Habit.NightOwl) && Life.Has(b, Habit.EarlyBird) || Life.Has(b, Habit.NightOwl) && Life.Has(a, Habit.EarlyBird))
            return (0.6f, "밤늦게 내는 소리를");
        if (Life.Has(a, Habit.Talker) && Life.Has(b, Habit.Loner) || Life.Has(b, Habit.Talker) && Life.Has(a, Habit.Loner))
            return (0.5f, "쉴 새 없는 수다를");
        if (a.Value != b.Value) return (0.3f, "일하는 방식을");
        return (0.12f, "사소한 일을");
    }

    // ── 죽음 뒤 ──

    /// <summary>죽은 다음 날 아침: 남은 사람들이 추모한다 (가까웠던 사람은 오래 슬퍼한다).</summary>
    public void OnDeath(CrewMember dead)
    {
        var w = _w;
        Memorial.Add((dead.Name, w.Tick, dead.Vitals.InjuryCause ?? "사고"));
        _funerals.Add((w.Tick + SimTime.Hours(14), dead.Id));
        foreach (var o in w.Crew.Where(o => !o.Dead && o != dead))
        {
            float close = MathF.Max(0f, o.AffinityTo(dead));
            if (close > 0.25f) { o.GriefUntil = w.Tick + SimTime.Hours(48 + 96 * close); Life.Diary(w, o, $"{Ko.IGa(dead.Name)} 떠났다. 믿기지 않는다."); }
        }
    }

    private void Mourning()
    {
        var w = _w;
        for (int i = _funerals.Count - 1; i >= 0; i--)
        {
            var (at, id) = _funerals[i];
            if (w.Tick < at || w.History.Current != null) continue;
            _funerals.RemoveAt(i);
            var dead = w.Crew.FirstOrDefault(c => c.Id == id);
            if (dead == null || !_mourned.Add(id)) continue;
            Stats.Funerals++;
            var room = Facilities.Best(w.Ship, "grief").room ?? w.Ship.RoomsOf(RoomType.Mess).FirstOrDefault();
            int came = 0;
            foreach (var o in w.Crew.Where(o => !o.Dead && !o.Down && o.IsAwake))
            {
                came++;
                o.Needs.Stress = MathF.Max(0f, o.Needs.Stress - 0.1f);
                o.Memory.Trauma = MathF.Max(0f, o.Memory.Trauma - 0.03f);
                Life.Diary(w, o, $"{Ko.EulReul(dead.Name)} 추모했다.");
            }
            w.History.Add(w, HistoryKind.Death, $"{room?.Name ?? "식당"}에서 {Ko.EulReul(dead.Name)} 추모했다 — {came}명이 모였다", room);
        }
        // 슬픔: 가까웠던 사람은 한동안 기운이 없다
        foreach (var c in w.Crew)
            if (!c.Dead && c.GriefUntil > w.Tick)
                c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.002f);
    }

    /// <summary>솜씨가 오르면 자격을 딴다 (자격 시험).</summary>
    private void Earn()
    {
        var w = _w;
        foreach (var c in w.Crew.Where(c => !c.Dead))
        {
            void Try(Qual q, Skill s, float need)
            {
                if (c.Quals.Contains(q) || c.RawSkill(s) < need) return;
                c.Quals.Add(q);
                Stats.QualsEarned++;
                w.History.Add(w, HistoryKind.Bond, $"{Ko.IGa(c.Name)} {Life.Name(q)} 자격을 땄다 ({Skills.Name(s)} {c.RawSkill(s) * 100:0}%)", c.Room, new[] { c }, log: true);
                Life.Diary(w, c, $"{Life.Name(q)} 자격을 땄다.");
            }
            Try(Qual.Reactor, Skill.Engineering, 0.6f);
            Try(Qual.Electrical, Skill.Electrical, 0.55f);
            Try(Qual.Medic, Skill.Medicine, 0.5f);
            Try(Qual.Helm, Skill.Piloting, 0.55f);
            Try(Qual.Eva, Skill.Mechanics, 0.6f);
        }
    }
}
