using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v12.7 승무원: 사람마다 살아온 길(경력)·가치관·습관·자격이 있고, 그것이 일과 다툼과 실수로 드러난다.
//   실수: 피곤하거나, 예민하거나, 자격 없이 하거나, 머리를 다쳤으면 — 사람답게 틀린다. 왜 틀렸는지가 기록과 인과 사슬에 남는다.
//   갈등: 예민한 둘이 가치관·습관이 부딪히면 말다툼 → 냉전 → 사교적인 사람이 중재한다.
//   돌봄: 아프거나 다친 사람을 가까운 사람이 찾아가 곁에 있어 준다.
//   죽음 뒤: 시신을 안치실(없으면 냉동 창고·창고)로 모시고, 다음 날 추모한다. 가까웠던 사람은 오래 슬퍼한다.

public enum Background
{
    Miner, MilitaryTech, FarmResearcher, CargoPilot, MedStudent, Reporter, Teacher, Chef, Lineworker, Programmer, Artist, Athlete,
    // v14.0
    Firefighter, Nurse, Welder, Plumber, Chemist, Physicist, Astronomer, Diver, Paramedic, Soldier, Police, Lawyer, Accountant, Monk,
    Psychologist, AutoMechanic, Carpenter, Gardener, Baker, Veterinarian, DroneRacer, Roboticist, SysAdmin, Musician, Writer, TruckDriver,
    Climber, SafetyInspector,
}
public enum CrewValue { Safety, Efficiency, People, Rules, Freedom }
public enum Habit
{
    NightOwl, EarlyBird, NeatFreak, Messy, Talker, Loner, GymRat, Snacker, Worrier, Tinkerer,
    // v14.0
    Perfectionist, Hasty, Procrastinator, Optimist, Pessimist, Joker, Serious, Superstitious, Insomniac, HeavySleeper,
    CoffeeAddict, TeaLover, Hoarder, Generous, Grumbler, Hummer, Bookworm, Gazer, Fidgety, Methodical,
    Daredevil, Homesick, Cheerful, ShortTempered, Patient, Forgetful, Leader, Follower, Collector, Prankster,
}
public enum Qual
{
    Reactor, Eva, Medic, Helm, Electrical,
    // v14.0
    Welding, Plumbing, LifeSupport, Computer, Chemistry, Firefighting, RescueTeam, FoodSafety, Agronomy, RobotTech,
    DronePilot, Radiation, Structure, Counseling, Instructor,
}

public static class Life
{
    public static string Name(Background b) => Persona.Bg(b).Name; // v14.0 경력 40

    public static string Name(CrewValue v) => v switch
    {
        CrewValue.Safety => "안전 먼저", CrewValue.Efficiency => "효율 먼저", CrewValue.People => "사람 먼저", CrewValue.Rules => "규칙대로", _ => "제 방식대로",
    };

    public static string Name(Habit h) => Persona.Of(h).Name; // v14.0 습관 40

    public static string Name(Qual q) => Persona.Of(q).Name; // v14.0 자격 20

    /// <summary>경력이 주는 솜씨와 자격.</summary>
    public static (Skill skill, float bonus, Qual[] quals) Gift(Background b) { var s = Persona.Bg(b); return (s.Skill, s.Bonus, s.Quals); }

    /// <summary>v14.0 자격이 있나 (응급 의료는 구조대를 겸한다).</summary>
    public static bool HasQual(CrewMember c, Qual q) => c.Quals.Contains(q) || q == Qual.RescueTeam && c.Quals.Contains(Qual.Medic);

    /// <summary>그 일을 제대로 하려면 필요한 자격 (없어도 하지만 서툴고 실수가 잦다).</summary>
    public static Qual? Needs(WorkOrder o) => o.Kind switch
    {
        WorkKind.RestartReactor => Qual.Reactor,
        WorkKind.ManualControl => Qual.Helm,
        WorkKind.Treat => Qual.Medic,
        WorkKind.Rescue => Qual.RescueTeam, // v14.0 (응급 의료도 된다)
        WorkKind.BreakerOn or WorkKind.IsolateRoom or WorkKind.ReplacePanel => Qual.Electrical,
        _ when o.External => Qual.Eva,
        // v14.0 자격 20
        WorkKind.WeldBulkhead or WorkKind.RepairHull => Qual.Welding,
        WorkKind.RepairJoint or WorkKind.Clamp or WorkKind.InstallTruss or WorkKind.RebuildFrame => Qual.Structure,
        WorkKind.PatchPipe or WorkKind.ReplacePipe or WorkKind.LayBypass or WorkKind.CloseValve or WorkKind.OpenValve or WorkKind.IsolatePipes or WorkKind.RefillCoolant => Qual.Plumbing,
        WorkKind.BuildOxygen => Qual.LifeSupport,
        WorkKind.Calibrate => Qual.Computer,
        WorkKind.BleedRoom or WorkKind.SealO2Line => Qual.Chemistry,
        WorkKind.Extinguish => Qual.Firefighting,
        WorkKind.Cook => Qual.FoodSafety,
        WorkKind.Tend or WorkKind.Harvest => Qual.Agronomy,
        WorkKind.ServiceRobot or WorkKind.RepairRobot => Qual.RobotTech,
        WorkKind.PilotDrones or WorkKind.ServiceDrone => Qual.DronePilot,
        WorkKind.Train => Qual.Instructor,
        WorkKind.Repair when o.Target.Furniture?.Type is FurnitureType.OxygenGenerator or FurnitureType.Scrubber or FurnitureType.WaterRecycler => Qual.LifeSupport,
        WorkKind.Repair when o.Target.Furniture?.Type == FurnitureType.MainComputer => Qual.Computer,
        WorkKind.Repair when o.Target.Furniture?.Type == FurnitureType.ReactorCore => Qual.Radiation,
        _ => null,
    };

    /// <summary>역할이 처음부터 가진 자격.</summary>
    public static Qual[] RoleQuals(CrewRole r) => r switch
    {
        CrewRole.Engineer => new[] { Qual.Reactor, Qual.Electrical, Qual.Radiation },
        CrewRole.Electrician => new[] { Qual.Electrical, Qual.Computer },
        CrewRole.Medic => new[] { Qual.Medic },
        CrewRole.Pilot => new[] { Qual.Helm, Qual.DronePilot },
        CrewRole.Technician => new[] { Qual.Eva, Qual.Welding, Qual.Plumbing },
        CrewRole.Botanist => new[] { Qual.Agronomy, Qual.LifeSupport },
        CrewRole.Cook => new[] { Qual.FoodSafety },
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
            // v14.0 역할에 어울리는 경력이 여럿 — 그중 하나는 따로 굴린다 (본 흐름의 뽑기 횟수는 그대로)
            var fits = Persona.Fits(c.Role);
            var side = new Rng(unchecked(w.Seed * 3571 + c.Id * 31337 + 5));
            var fit = fits.Length > 0 ? fits[side.Range(0, fits.Length)] : bgs[rng.Range(0, bgs.Length)];
            c.Background = rng.Chance(0.55f) ? fit : bgs[rng.Range(0, bgs.Length)];
            if (!c.BornAboard) c.Age = 22f + rng.Range(0f, 34f); // v12.9
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
            // v14.0 서로 어긋나는 습관을 더 (완벽주의↔서두름, 낙천가↔비관적, 불면증↔잠꾸러기, 따르기↔앞장서기)
            if (c.Habits.Count == 2 && (c.Habits[0], c.Habits[1]) is (Habit.Perfectionist, Habit.Hasty) or (Habit.Hasty, Habit.Perfectionist)
                or (Habit.Optimist, Habit.Pessimist) or (Habit.Pessimist, Habit.Optimist) or (Habit.Insomniac, Habit.HeavySleeper) or (Habit.HeavySleeper, Habit.Insomniac)
                or (Habit.Leader, Habit.Follower) or (Habit.Follower, Habit.Leader)) c.Habits.RemoveAt(1);
            Persona.Extras(w, c); // v14.0 취미 · 두려움 · 말버릇
            var (skill, bonus, quals) = Gift(c.Background);
            c.SkillLevels[(int)skill] = MathF.Min(1f, c.SkillLevels[(int)skill] + bonus);
            c.Quals.Clear();
            foreach (var q in RoleQuals(c.Role).Concat(quals)) c.Quals.Add(q);
            if (c.RawSkill(Skill.Medicine) >= 0.55f) c.Quals.Add(Qual.Medic);
            if (c.RawSkill(Skill.Piloting) >= 0.55f) c.Quals.Add(Qual.Helm);
            foreach (var (q, _) in Eligible(c).ToList()) c.Quals.Add(q); // v14.0 처음부터 갖춘 자격은 조용히
        }
    }

    public static bool Has(CrewMember c, Habit h) => c.Habits.Contains(h);

    /// <summary>v14.0 자격 20 — 지금 딸 수 있는 자격 (솜씨가 찼다 · 구조대는 구조 두 번 · 소방은 사고 대응 네 번 · 상담은 사교적인 사람 · 교관은 무엇이든 0.8).</summary>
    public static IEnumerable<(Qual q, Skill s)> Eligible(CrewMember c)
    {
        foreach (var qs in Persona.Quals)
        {
            if (c.Quals.Contains(qs.Id)) continue;
            if (qs.Id == Qual.RescueTeam && c.Stats.Rescues < 2) continue;
            if (qs.Id == Qual.Firefighting && c.Stats.Emergencies < 4) continue;
            if (qs.Id == Qual.Counseling && c.Traits.Sociability < 0.6f) continue;
            var skill = qs.Id == Qual.Instructor ? Enum.GetValues<Skill>().OrderByDescending(c.RawSkill).First() : qs.Skill;
            if (c.RawSkill(skill) >= qs.Need) yield return (qs.Id, skill);
        }
    }

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
        $"{Name(c.Background)} · {Name(c.Value)}" + (c.Habits.Count > 0 ? " · " + string.Join("·", c.Habits.Select(Name)) : "")
        + (Persona.Line(c) is string pl && pl.Length > 0 ? " · " + pl.TrimStart(' ', '·') : "");

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
    public int HobbyRests; // v14.0 취미의 방에서 쉰 번
    public override string ToString() =>
        $"실수 {Mistakes}(잡아냄 {Caught}) · 말다툼 {Arguments} · 냉전 {Feuds} · 중재 {Mediations} · 문병 {Visits} · 추모 {Funerals} · 시신 수습 {BodiesMoved} · 자격 {QualsEarned} · 팔다리 잃음 {LimbsLost} · 의수·의족 {Prosthetics}";
}

/// <summary>실수·다툼·문병·추모를 돌린다 (시스템 틱).</summary>
public sealed class LifeSystem
{
    private readonly World _w;
    public LifeStats Stats { get; } = new();
    private readonly List<(long due, int furnitureId, int node, string why, int crewId)> _sloppy = new();
    private readonly List<(long at, int deadId)> _funerals = new();
    private readonly HashSet<int> _mourned = new();
    private readonly HashSet<(int, BodyPart)> _lost = new();
    public List<(string name, long tick, string cause)> Memorial { get; } = new();

    private readonly Rng _rng; // v12.9.5 말다툼은 따로 굴린다 (주 난수의 흐름 — 사고·고장 — 을 흔들지 않게)

    public LifeSystem(World w)
    {
        _w = w;
        _rng = new Rng(unchecked(w.Seed * 6151 + 29));
    }

    // ── 실수 ──

    /// <summary>실수할 확률과 그 까닭 (가장 큰 것 하나).</summary>
    public (float p, string why) MistakeOdds(CrewMember c, WorkOrder o)
    {
        float p = 0.015f;
        var reasons = new List<(float w, string text)>();
        if (c.Needs.Rest < 0.25f) { p *= 4f; reasons.Add((4f, $"졸려서 (기력 {c.Needs.Rest * 100:0}%)")); }
        if (c.Needs.Stress > 0.65f) { p *= 2.5f; reasons.Add((2.5f, $"예민해서 (스트레스 {c.Needs.Stress * 100:0}%)")); }
        if (c.Needs.Food < 0.15f) { p *= 1.5f; reasons.Add((1.5f, "배가 고파 손이 떨려서")); }
        if (Life.Needs(o) is Qual q && !Life.HasQual(c, q)) { float k = Persona.Core(q) ? 2.5f : 1.4f; p *= k; reasons.Add((k, $"{Life.Name(q)} 자격이 없어서")); }
        float head = Wounds.HeadLoad(c.Vitals);
        if (head > 1.05f) { p *= head; reasons.Add((head, "머리를 다쳐 판단이 흐려서")); }
        float hand = Wounds.HandFactor(c.Vitals);
        if (hand < 0.8f) { p *= 1.8f; reasons.Add((1.8f, "다친 팔이 말을 안 들어서")); }
        float skill = c.SkillLevel(o.Skill);
        if (skill < 0.35f) { p *= 1.8f; reasons.Add((1.8f, $"서툴러서 ({Skills.Name(o.Skill)} {skill * 100:0}%)")); }
        p *= 1.2f - 0.5f * c.Traits.Calm;
        if (_w.Eras.Has("checklist")) p *= 0.7f; // v12.8 점검표 문화
        // v14.0 습관 40 (덜렁댐·서두름·깜빡함은 늘고, 꼼꼼함·완벽주의·걱정은 준다)
        foreach (var h in c.Habits)
        {
            float k = Persona.Of(h).Mistake;
            p *= k;
            if (k >= 1.2f) reasons.Add((k, h switch { Habit.Messy => "덜렁대서", Habit.Hasty => "서두르다가", Habit.Forgetful => "깜빡해서", _ => $"{Persona.Of(h).Name} 버릇 탓에" }));
        }
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
            _sloppy.Add((w.Tick + SimTime.Hours(w.Rng.Range(2f, 8f)), m.Body.Id, node, why, c.Id));
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
        else if (w.Rng.Chance(0.35f))
        {
            what = "공구에 손을 다쳤다";
            w.Causes.Root(CauseKind.Mistake, $"{c.Name}의 실수 — {what} ({why})", c.Room, c.Position, observer: false);
            // 손을 베고 찧는 정도 — 이미 다친 사람에게 쌓여 결정타가 되지는 않는다
            float slip = MathF.Min(0.04f, MathF.Max(0f, 0.35f - c.Vitals.Injury));
            if (slip > 0f) NeedsSystem.AddInjury(c.Vitals, slip, "작업 중 실수");
        }
        else
        {
            // 대개는 공구를 떨어뜨리거나 순서를 헷갈려 다시 한다 (시간과 기분만 잃는다)
            what = "공구를 떨어뜨려 처음부터 다시 했다";
            w.Causes.Root(CauseKind.Mistake, $"{c.Name}의 실수 — {what} ({why})", c.Room, c.Position, observer: false);
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
        // v13.4 혼자 안 실수: 털어놓으면 다시 손보고, 숨기면 몇 시간 뒤 고장으로 드러난다
        if (m != null && _sloppy.Count > 0 && _sloppy[^1].furnitureId == m.Body.Id && _sloppy[^1].crewId == c.Id)
        {
            if (w.Society.WillHide(c)) w.Society.Hide(c, what);
            else
            {
                _sloppy.RemoveAt(_sloppy.Count - 1);
                w.Society.Confess(c, what);
                return;
            }
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
            var (due, fid, node, why, crewId) = _sloppy[i];
            if (w.Tick < due) continue;
            _sloppy.RemoveAt(i);
            if (w.Ship.Furniture.FirstOrDefault(f => f.Id == fid)?.Machine is not Machine m || m.Faults.Count > 0) continue;
            using (w.Causes.Because(node)) w.Machines.Break(m);
            w.Society.Surfaced(w.Crew.FirstOrDefault(x => x.Id == crewId), m, why); // v13.4 숨긴 실수가 드러나나
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
                // v12.9.4 평소에도 조금씩 부딪힌다 (좁은 배에 오래 붙어 있으면) — 지치고 배고프고 예민하면 훨씬 잦다
                float tension = 0.06f + (Irritable(a) + Irritable(b)) / 2f;
                var (clash, about) = Clash(a, b);
                clash *= Persona.Mul(a, h => h.Clash) * Persona.Mul(b, h => h.Clash); // v14.0 다혈질·참을성·퍼주기
                // v13.2 파벌: 회의에서 가치관대로 갈린 표가 쌓인 사이는 더 자주 부딪힌다
                float rift = w.Meetings.Tension(a.Value, b.Value);
                if (rift > 0.15f) { clash = MathF.Max(clash, 0.5f) * (1f + 2f * rift); about = "회의에서 갈린 표를"; }
                float p = tension * clash * 0.12f * dt * (1.2f - MathF.Max(0f, (a.AffinityTo(b) + b.AffinityTo(a)) / 2f)) * (1.5f - w.Society.Morale); // v13.4 사기
                if (!_rng.Chance(p)) continue;
                Stats.Arguments++;
                a.ChangeAffinity(b, -0.15f); b.ChangeAffinity(a, -0.15f);
                a.Needs.Stress = MathF.Min(1f, a.Needs.Stress + 0.06f);
                b.Needs.Stress = MathF.Min(1f, b.Needs.Stress + 0.06f);
                a.Quarrel = b.Quarrel = w.Tick;
                MindSystem.Anger(a, 0.1f); MindSystem.Anger(b, 0.1f); // v13.3
                bool feud = a.AffinityTo(b) < -0.45f;
                if (feud) Stats.Feuds++;
                string text = $"{Ko.WaGwa(a.Name)} {Ko.IGa(b.Name)} {about} 두고 말다툼했다" + (feud ? " — 서로 말을 안 한다" : "");
                w.Log.Add(w.Tick, LogKind.Life, text, a.Id);
                Life.Diary(w, a, $"{Ko.WaGwa(b.Name)} {about} 두고 다퉜다.");
                Life.Diary(w, b, $"{Ko.WaGwa(a.Name)} {about} 두고 다퉜다.");
                // v13.4 방침(갈등 해결): 선장 판단 — 선장이 한쪽 손을 들어 준다 (진 쪽은 선장이 서운하다)
                int conflict = w.Policies["conflict"];
                if (conflict == 1 && w.Command.Captain is CrewMember cap && cap != a && cap != b && cap.CanAct && _rng.Chance(0.8f))
                {
                    var (win, lose) = a.Value == cap.Value || cap.AffinityTo(a) >= cap.AffinityTo(b) ? (a, b) : (b, a);
                    lose.ChangeAffinity(cap, -0.06f);
                    win.ChangeAffinity(lose, 0.05f); lose.ChangeAffinity(win, 0.05f);
                    Stats.Mediations++;
                    w.Log.Add(w.Tick, LogKind.Life, $"선장 {Ko.IGa(cap.Name)} {win.Name}의 손을 들어 줬다 ({lose.Name}은(는) 서운하다)", cap.Id);
                    continue;
                }
                // 중재: 그 자리에 사교적인 사람이 있으면 바로 달랜다 (그냥 둔다면 아무도 나서지 않는다)
                var mediator = conflict == 2 ? null : awake.Where(x => x != a && x != b && x.Room == a.Room && (x.Traits.Sociability > 0.6f || x.Quals.Contains(Qual.Counseling)))
                    .OrderByDescending(x => x.Quals.Contains(Qual.Counseling)).ThenByDescending(x => x.Traits.Sociability).FirstOrDefault();
                if (mediator != null && _rng.Chance(mediator.Quals.Contains(Qual.Counseling) ? 0.85f : 0.6f)) // v14.0 심리 상담 자격
                {
                    Stats.Mediations++;
                    a.ChangeAffinity(b, 0.1f); b.ChangeAffinity(a, 0.1f);
                    a.ChangeAffinity(mediator, 0.04f); b.ChangeAffinity(mediator, 0.04f);
                    w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(mediator.Name)} 둘 사이를 달랬다", mediator.Id);
                }
            }
    }

    /// <summary>날이 선 정도: 스트레스에 피로·배고픔이 더해진다.</summary>
    private static float Irritable(CrewMember c) => c.Needs.Stress + (c.Needs.Rest < 0.3f ? 0.15f : 0f) + (c.Needs.Hunger > 0.7f ? 0.1f : 0f);

    /// <summary>둘이 어긋나는 정도와 무엇 때문인지.</summary>
    public static (float clash, string about) Clash(CrewMember a, CrewMember b)
    {
        if ((a.Value, b.Value) is (CrewValue.Safety, CrewValue.Efficiency) or (CrewValue.Efficiency, CrewValue.Safety))
            return (1f, "안전이냐 효율이냐를");
        if ((a.Value, b.Value) is (CrewValue.Rules, CrewValue.Freedom) or (CrewValue.Freedom, CrewValue.Rules))
            return (0.9f, "규칙을 지키느냐를");
        if (Persona.Rival(a, b) is var (k, about)) return (k, about); // v14.0 부딪히는 습관
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
            // v13.4 방침(장례)
            switch (w.Policies["funeral"])
            {
                case 0: // 우주장: 에어락으로 별에 — 보내고 나면 마음이 한결 놓인다
                    foreach (var o in w.Crew.Where(o => !o.Dead && o.GriefUntil > w.Tick)) o.GriefUntil = w.Tick + (o.GriefUntil - w.Tick) / 2;
                    w.History.Add(w, HistoryKind.Death, $"{Ko.EulReul(dead.Name)} 우주장으로 보냈다 — 에어락 너머 별 사이로", room);
                    break;
                case 2: // 재순환: 물과 흙으로 — 배에는 보탬이 되지만 마음이 편치 않은 사람도 있다
                    w.Water.Level = MathF.Min(w.Water.Capacity, w.Water.Level + 20f);
                    foreach (var o in w.Crew.Where(o => !o.Dead && o.Value is CrewValue.People or CrewValue.Freedom)) o.Needs.Stress = MathF.Min(1f, o.Needs.Stress + 0.05f);
                    w.History.Add(w, HistoryKind.Death, $"{Ko.EulReul(dead.Name)} 재순환했다 — 물 20L와 재배대 흙으로", room);
                    break;
            }
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
            foreach (var (q, s) in Life.Eligible(c).ToList())
            {
                c.Quals.Add(q);
                Stats.QualsEarned++;
                w.History.Add(w, HistoryKind.Bond, $"{Ko.IGa(c.Name)} {Life.Name(q)} 자격을 땄다 ({Skills.Name(s)} {c.RawSkill(s) * 100:0}%)", c.Room, new[] { c }, log: true);
                Life.Diary(w, c, $"{Life.Name(q)} 자격을 땄다.");
            }
        }
    }
}
