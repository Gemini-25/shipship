using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v14.9 배의 문화: 겪은 일이 관행이 되어 물건과 행동으로 전해진다.
// 큰불을 겪은 배는 식사 전에 소화기 위치를 본다 · 정전 속에서 배를 살린 정비사의 점검 방식이 제자에게 간다 ·
// 추모일에 그 사람의 자리에 물건을 놓는다 · 신입은 처음엔 모르다가 설명을 듣고 받아들인다 ·
// 이유를 아는 사람이 떠나고 기록도 없으면 이유는 잊히고 행동만 남는다 — 같은 설계의 배가 서로 다른 공동체가 된다.

public enum CustomKind { FireCheck, Memorial, WaterThrift, HandWash, MaintainerWay, HatchBuddy, SurvivalMeal, StowAway, TetherTools, SortWaste }

/// <summary>한 배의 관행 하나: 언제 · 무엇 때문에 생겼고, 누가 이유를 알고, 누가 따라 하나.</summary>
public sealed class Custom
{
    public CustomKind Kind { get; init; }
    public long Born { get; init; }
    /// <summary>생긴 까닭 (그때 일).</summary>
    public string Origin { get; init; } = "";
    /// <summary>처음 그렇게 한 사람 (있으면).</summary>
    public string? Founder { get; init; }
    public HashSet<int> Knowers { get; } = new();
    public HashSet<int> Followers { get; } = new();
    /// <summary>배 기록에 남겼나 (컴퓨터가 살아 있으면 — 기록이 사라지면 이유를 찾을 길이 없다).</summary>
    public bool Written { get; set; }
    public bool ReasonLost { get; set; }
    /// <summary>추모일 · 고비를 넘긴 날: 다음 기념일.</summary>
    public long NextDay { get; set; }
    /// <summary>누구를 기리는지 (추모).</summary>
    public string? Honoree { get; init; }
    public int HonoreeId { get; init; } = -1;
}

public sealed class CultureStats
{
    public int Born, Explained, Refused, Imitated, Forgotten, Inherited, ExtChecks, MemorialItems, SharedMeals, ThriftWashes, StrictWashes, BuddyChecks, Recovered;
    public string Summary() =>
        $"관행 {Born} · 설명해 줌 {Explained}(아직 안 받아들임 {Refused}) · 이유 모르고 따라 함 {Imitated} · 이유 잊힘 {Forgotten} · 기록에서 이유를 찾음 {Recovered} · 제자에게 {Inherited} · " +
        $"소화기 확인 {ExtChecks} · 추모 물건 {MemorialItems} · 함께 먹은 날 {SharedMeals} · 아껴 씻기 {ThriftWashes} · 꼭 씻기 {StrictWashes} · 짝 점검 {BuddyChecks}";
}

public sealed class CultureSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6247 + 83));
    public CultureStats Stats { get; } = new();
    public List<Custom> Customs { get; } = new();
    private long _lowWaterSince = -1;
    private bool _crisis;
    private readonly HashSet<int> _gone = new();
    private readonly Dictionary<int, long> _lastCheck = new();
    private readonly Dictionary<(int, CustomKind), long> _heard = new();

    public CultureSystem(World w) => _w = w;

    public static string Name(CustomKind k) => k switch
    {
        CustomKind.FireCheck => "식사 전에 소화기 자리를 본다",
        CustomKind.Memorial => "추모일에 그 사람의 자리에 물건을 놓는다",
        CustomKind.WaterThrift => "물을 아껴 씻는다",
        CustomKind.HandWash => "조리·치료 전엔 손을 꼭 씻는다",
        CustomKind.MaintainerWay => "점검은 귀부터 — 소리를 먼저 듣는다",
        CustomKind.HatchBuddy => "에어락에선 급해도 짝 점검을 한다",
        CustomKind.StowAway => "쓰고 난 선반은 걸쇠를 걸고 카트는 끈으로 묶어 둔다 · 자기 전 침대 끈",
        CustomKind.TetherTools => "공구는 끈에 매어 두고 쓰면 바로 넣는다",
        CustomKind.SortWaste => "쓰레기는 금속 · 플라스틱 · 음식물로 나눠 버린다",
        _ => "고비를 넘긴 날엔 함께 먹는다",
    };

    public Custom? Of(CustomKind k) => Customs.FirstOrDefault(x => x.Kind == k);
    public Custom Adopt(CustomKind k, string origin, string? founder) => Of(k) ?? Born(k, origin, founder); // v16.9 배의 내력에서 생긴 관행 (고물 배의 정비 습관)
    public bool Follows(CrewMember c, CustomKind k) => Of(k) is Custom cu && cu.Followers.Contains(c.Id);
    public bool KnowsWhy(CrewMember c, CustomKind k) => Of(k) is Custom cu && cu.Knowers.Contains(c.Id);

    /// <summary>승무원 정보에 쓰는 한 줄: 따르는 관행과 그 이유를 아는지.</summary>
    public string? Line(CrewMember c)
    {
        var mine = Customs.Where(x => x.Followers.Contains(c.Id)).ToList();
        if (mine.Count == 0) return null;
        return "관행: " + string.Join(" · ", mine.Select(x => Name(x.Kind) + (x.Knowers.Contains(c.Id) ? "" : " (이유는 모른다)")));
    }

    /// <summary>관행 하나의 설명 (화면 · 기록).</summary>
    public string Describe(Custom x)
    {
        var w = _w;
        int know = x.Knowers.Count(id => id < w.Crew.Count && !w.Crew[id].Dead), follow = x.Followers.Count(id => id < w.Crew.Count && !w.Crew[id].Dead);
        string why = x.ReasonLost ? "이유는 잊혔다 — 그렇게 해 왔다" : x.Origin;
        return $"{Name(x.Kind)} — {why} · 따르는 사람 {follow} · 이유를 아는 사람 {know}" + (x.Written ? " · 기록에 있다" : "");
    }

    public void Update(float dt)
    {
        var w = _w;
        Births();
        Spread(dt);
        Fade();
        Anniversaries();
    }

    // ───────────────────────────── 생긴다 ─────────────────────────────

    private void Births()
    {
        var w = _w;
        if (Of(CustomKind.FireCheck) == null && w.History.Fires >= 1)
        {
            var fire = w.History.Events.LastOrDefault(e => e.Kind is HistoryKind.Incident or HistoryKind.Damage && e.Text.Contains("불"));
            Born(CustomKind.FireCheck, $"{SimTime.Day(fire?.Tick ?? w.Tick)}일 불 — 소화기를 찾느라 늦었다", null);
        }
        if (Of(CustomKind.Memorial) == null && w.Life.Memorial.Count > 0)
        {
            var (name, tick, cause) = w.Life.Memorial[^1];
            var dead = w.Crew.FirstOrDefault(c => c.Dead && c.Name == name);
            var cu = Born(CustomKind.Memorial, $"{Ko.IGa(name)} 떠났다 ({cause})", null, name, dead?.Id ?? -1);
            cu.NextDay = tick + SimTime.TicksPerDay * 7;
        }
        // 물이 바닥 근처에서 여섯 시간 넘게
        if (w.Water.Level < w.Water.Capacity * 0.12f) { if (_lowWaterSince < 0) _lowWaterSince = w.Tick; }
        else _lowWaterSince = -1;
        if (Of(CustomKind.WaterThrift) == null && _lowWaterSince >= 0 && w.Tick - _lowWaterSince > SimTime.Hours(6))
            Born(CustomKind.WaterThrift, $"{SimTime.Day(w.Tick)}일 물이 바닥났다 — 한 방울도 아쉬웠다", null);
        if (Of(CustomKind.HandWash) == null && w.Soil.Stats.TaintedMeals + w.Soil.Stats.WoundInfections >= 3
            && w.Crew.Count(c => c.Ailments.Any(a => a.Id is "foodpoison" or "gastro" or "woundinf")) >= 2)
            Born(CustomKind.HandWash, $"{SimTime.Day(w.Tick)}일 더러운 손으로 만든 밥에 여럿이 탈이 났다", null);
        if (Of(CustomKind.MaintainerWay) == null && w.Net.Stats.Blackouts >= 1 && w.Net.Stats.Repairs + w.Net.Stats.TempRepairs >= 2)
        {
            var hero = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderByDescending(c => c.RawSkill(Skill.Electrical)).ThenBy(c => c.Id).First();
            var cu = Born(CustomKind.MaintainerWay, $"정전 속에서 {Ko.IGa(hero.Name)} 소리로 끊긴 곳을 찾아 배를 살렸다", hero.Name);
            // 처음엔 그 사람과 곁에서 본 사람만 — 나머지는 배워야 한다
            cu.Followers.Clear(); cu.Knowers.Clear();
            cu.Followers.Add(hero.Id); cu.Knowers.Add(hero.Id);
        }
        if (Of(CustomKind.HatchBuddy) == null && w.Crew.Any(c => c.Outside && (c.Down || c.Dead)))
        {
            var who = w.Crew.First(c => c.Outside && (c.Down || c.Dead));
            Born(CustomKind.HatchBuddy, $"{Ko.IGa(who.Name)} 선체 밖에서 쓰러졌다 — 아무도 우주복을 다시 보지 않았다", null);
        }
        bool crisis = Crisis.Level(w) >= CrisisLevel.Emergency;
        if (_crisis && !crisis && Of(CustomKind.SurvivalMeal) == null && w.Crew.Count(c => !c.Dead) >= 2)
        {
            var cu = Born(CustomKind.SurvivalMeal, $"{SimTime.Day(w.Tick)}일 고비를 다 같이 넘겼다", null);
            cu.NextDay = w.Tick + SimTime.TicksPerDay * 7;
        }
        _crisis = crisis;
    }

    private Custom Born(CustomKind k, string origin, string? founder, string? honoree = null, int honoreeId = -1)
    {
        var w = _w;
        var cu = new Custom { Kind = k, Born = w.Tick, Origin = origin, Founder = founder, Honoree = honoree, HonoreeId = honoreeId, Written = Records(w) };
        foreach (var c in w.Crew.Where(c => !c.Dead && !c.IsChild)) { cu.Knowers.Add(c.Id); cu.Followers.Add(c.Id); }
        Customs.Add(cu);
        Stats.Born++;
        w.History.Add(w, HistoryKind.Lesson, $"관행이 생겼다: {Name(k)} ({origin})", log: true);
        return cu;
    }

    /// <summary>배 기록이 살아 있나 (주 컴퓨터가 돌면 적어 둔다).</summary>
    private static bool Records(World w) => w.Ship.FurnitureOf(FurnitureType.MainComputer).Any(f => f.Machine is { Stopped: false } && !f.Room.Detached);

    // ───────────────────────────── 퍼진다 ─────────────────────────────

    /// <summary>
    /// 같은 방에 있는 신입에게 이유를 아는 사람이 설명한다 (받아들이기는 성격에 따라 · 몇 번 들어야 하기도).
    /// 이유를 모르는 사람은 "원래 그렇게 해"라고만 말한다 — 기록이 있으면 찾아보고 이유를 안다. 보고 따라 하기도 한다.
    /// </summary>
    private void Spread(float dt)
    {
        var w = _w;
        if (Customs.Count == 0) return;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.IsChild || !c.CanAct || !c.IsAwake || c.Room is not Room room || c.Job?.Urgent == true || _gone.Contains(c.Id)) continue;
            foreach (var cu in Customs)
            {
                if (cu.Kind == CustomKind.MaintainerWay) continue; // 점검 방식은 배우기(곁에서 따라 하기)로만 건너간다
                bool follows = cu.Followers.Contains(c.Id);
                if (follows && cu.Knowers.Contains(c.Id)) continue;
                var teacher = w.Crew.FirstOrDefault(o => o != c && !o.Dead && o.IsAwake && o.Room == room && !_gone.Contains(o.Id) && cu.Followers.Contains(o.Id) && o.Job?.Urgent != true
                                                         && (!follows || cu.Knowers.Contains(o.Id)));
                if (teacher == null || !R.Chance(0.6f * dt)) continue;
                // 한가한 선배는 말로 알려 주고, 바쁜 선배는 말없이 한다 — 신입은 보고 따라 한다 (이유는 모른 채)
                bool talk = teacher.Job?.Order == null && teacher.Pose is Pose.Sitting or Pose.Standing && R.Chance(0.35f + 0.5f * teacher.Traits.Sociability);
                if (follows)
                {
                    // 이유를 모르고 따라 하던 사람이 나중에 이유를 듣는다
                    if (!talk) continue;
                    cu.Knowers.Add(c.Id);
                    Stats.Explained++;
                    Life.Diary(w, c, Persona.Say(c, $"그래서 {Name(cu.Kind)}구나 — {cu.Origin}"));
                    break;
                }
                bool why = talk && cu.Knowers.Contains(teacher.Id);
                var key = (c.Id, cu.Kind);
                _heard[key] = _heard.TryGetValue(key, out var n) ? n + 1 : 1;
                // 받아들이기: 성실한 사람 · 그 일을 겪어 본 사람 · 이유를 들은 사람일수록 빨리
                float accept = 0.35f + 0.35f * c.Traits.Diligence + (why ? 0.25f : -0.1f) + 0.15f * (_heard[key] - 1) + 0.2f * MathF.Max(0f, c.AffinityTo(teacher));
                if (!R.Chance(MathF.Min(0.95f, accept)))
                {
                    Stats.Refused++;
                    if (c.SaidUntil < w.Tick) c.Say(w, Persona.Say(c, why ? "굳이 그래야 하나" : "왜 그래야 하는데?"));
                    continue;
                }
                cu.Followers.Add(c.Id);
                if (why)
                {
                    cu.Knowers.Add(c.Id);
                    Stats.Explained++;
                    teacher.Say(w, Persona.Say(teacher, $"우린 {Name(cu.Kind)} — {cu.Origin}"));
                    Life.Diary(w, c, Persona.Say(c, $"{Ko.IGa(teacher.Name)} 이 배에선 {Name(cu.Kind)}고 했다. {cu.Origin}"));
                    w.Relations.Remember(c, teacher, RelationReason.TaughtMe, $"이 배의 관행을 알려 줬다 ({Name(cu.Kind)})");
                }
                else if ((talk || ComputerV15.Archived(w)) && cu.Written && (Records(w) || ComputerV15.Archived(w))) // v15.9 기록 보관: 신입이 스스로 찾아 읽는다
                {
                    cu.Knowers.Add(c.Id);
                    Stats.Recovered++;
                    if (talk) teacher.Say(w, Persona.Say(teacher, "원래 그렇게 해 — 왜인지는 기록에 있을걸"));
                    Life.Diary(w, c, Persona.Say(c, $"{Name(cu.Kind)} — 기록을 찾아보니 {cu.Origin}"));
                }
                else
                {
                    Stats.Imitated++;
                    if (talk) teacher.Say(w, Persona.Say(teacher, "원래 그렇게 해"));
                    Life.Diary(w, c, Persona.Say(c, talk ? $"다들 {Name(cu.Kind)}. 왜인지는 아무도 모른다" : $"{Ko.IGa(teacher.Name)} {Name(cu.Kind)}. 나도 따라 했다"));
                }
                break;
            }
        }
    }

    /// <summary>배우기(곁에서 따라 하기)에서 선배의 관행이 제자에게 (이유와 함께).</summary>
    public void OnLesson(CrewMember mentor, CrewMember student)
    {
        foreach (var cu in Customs.Where(x => x.Followers.Contains(mentor.Id) && !x.Followers.Contains(student.Id)))
        {
            cu.Followers.Add(student.Id);
            if (cu.Knowers.Contains(mentor.Id)) cu.Knowers.Add(student.Id);
            Stats.Inherited++;
            Life.Diary(_w, student, Persona.Say(student, $"{Ko.IGa(mentor.Name)} 일하는 걸 보니 {Name(cu.Kind)}. 나도 그렇게 하기로 했다"));
        }
    }

    // ───────────────────────────── 잊힌다 ─────────────────────────────

    /// <summary>떠나거나 죽은 사람: 그 사람이 알던 이유가 함께 간다.</summary>
    public void OnGone(CrewMember c)
    {
        _gone.Add(c.Id);
        foreach (var cu in Customs) { cu.Knowers.Remove(c.Id); cu.Followers.Remove(c.Id); }
    }

    private void Fade()
    {
        var w = _w;
        foreach (var c in w.Crew) if (c.Dead && !_gone.Contains(c.Id)) OnGone(c);
        foreach (var cu in Customs)
        {
            if (!cu.Written && cu.Knowers.Count > 0 && Records(w) && ComputerV15.Archived(w)) { cu.Written = true; w.Automation.V15Acts[ComputerModule.Archive]++; } // v15.9 기록 보관: 아는 사람이 있을 때 적어 둔다
            if (cu.Written && !Records(w) && !ComputerV15.Archived(w)) cu.Written = false; // 기록이 사라졌다 (v15.9 예비 기억 장치에 있으면 남는다)
            bool lost = cu.Knowers.Count == 0 && !cu.Written;
            if (lost && !cu.ReasonLost && cu.Followers.Count > 0)
            {
                cu.ReasonLost = true;
                Stats.Forgotten++;
                w.History.Add(w, HistoryKind.Memory, $"'{Name(cu.Kind)}' — 왜 그러는지 아는 사람이 이제 없다. 행동만 남았다", log: true);
            }
            else if (!lost && cu.ReasonLost) cu.ReasonLost = false;
        }
    }

    // ───────────────────────────── 기념일 ─────────────────────────────

    private void Anniversaries()
    {
        var w = _w;
        foreach (var cu in Customs)
        {
            if (cu.NextDay <= 0 || w.Tick < cu.NextDay) continue;
            cu.NextDay += SimTime.TicksPerDay * 7;
            if (cu.Kind == CustomKind.Memorial) w.Log.Add(w.Tick, LogKind.Life, $"오늘은 {cu.Honoree}의 추모일이다");
            else if (cu.Kind == CustomKind.SurvivalMeal) w.Log.Add(w.Tick, LogKind.Life, "오늘은 고비를 넘긴 날 — 저녁은 다 같이");
            _dayOf[cu.Kind] = w.Tick;
        }
    }

    private readonly Dictionary<CustomKind, long> _dayOf = new();
    /// <summary>오늘이 그 기념일인가 (기념일이 시작되고 열여덟 시간).</summary>
    public bool IsDay(CustomKind k) => _dayOf.TryGetValue(k, out var t) && _w.Tick - t < SimTime.Hours(18);
    private readonly HashSet<(int, CustomKind)> _keptDay = new();
    public bool Kept(CrewMember c, CustomKind k) => _keptDay.Contains((c.Id, k)) && IsDay(k);
    internal void Keep(CrewMember c, CustomKind k) { if (_keptDay.Count > 4000) _keptDay.Clear(); _keptDay.Add((c.Id, k)); }

    // ───────────────────────────── 행동 ─────────────────────────────

    public bool CheckedRecently(CrewMember c) => _lastCheck.TryGetValue(c.Id, out var t) && _w.Tick - t < SimTime.Hours(5);
    internal void MarkChecked(CrewMember c) { _lastCheck[c.Id] = _w.Tick; Stats.ExtChecks++; }
}

/// <summary>식사 전에 소화기 자리를 본다 (큰불을 겪은 배).</summary>
public sealed class ExtinguisherCheckActivity : Activity
{
    public override string Id => "extcheck";
    public override string Label => "소화기 자리 확인";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var cu = w.Culture;
        if (!cu.Follows(c, CustomKind.FireCheck) || cu.CheckedRecently(c) || c.Down || Crisis.Acting(w)) return (0f, "—");
        if (c.Needs.Hunger < 0.5f) return (0f, "—");
        // 먹으러 가기 바로 전: 식사보다 조금 높게 (금방 끝난다)
        float eat = Curve.Smooth(c.Needs.Hunger, 0.35f, 0.9f) * 1.1f;
        return (MathF.Min(1.2f, eat + 0.05f), cu.KnowsWhy(c, CustomKind.FireCheck) ? $"밥 먹기 전에 — {cu.Of(CustomKind.FireCheck)!.Origin}" : "밥 먹기 전에 (다들 그렇게 한다)");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var spot = w.Ship.Furniture.Where(f => f.Storage != null && f.Storage.Count(ItemKind.Extinguisher) > 0 && !f.Room.Detached && !f.Stowed)
            .SelectMany(f => f.UseSpots).Where(s => dist.Reachable(s)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
        if (spot is not Cell at || dist.Get(at) > 40) { w.Culture.MarkChecked(c); return null; } // 너무 멀면 머릿속으로만
        var toils = new List<Toil>
        {
            new GotoToil(at),
            new WaitToil(SimTime.Minutes(1), Pose.Standing, at.Center),
            new DoToil((cm, world) => { world.Culture.MarkChecked(cm); return true; }),
        };
        return new Job(this, "소화기 자리 확인", toils) { LogText = "밥 먹기 전에 소화기 자리를 본다", LogKind = LogKind.Life };
    }
}

/// <summary>추모일: 그 사람이 쓰던 자리에 물건을 놓고 잠시 머문다.</summary>
public sealed class MemorialVisitActivity : Activity
{
    public override string Id => "memorialday";
    public override string Label => "추모";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var cu = w.Culture;
        if (!cu.Follows(c, CustomKind.Memorial) || !cu.IsDay(CustomKind.Memorial) || cu.Kept(c, CustomKind.Memorial) || c.Down || Crisis.Acting(w)) return (0f, "—");
        var m = cu.Of(CustomKind.Memorial)!;
        return (0.5f + (OnShift(c, w) ? -0.15f : 0.1f), $"{m.Honoree}의 추모일" + (m.ReasonLost ? " (누구였는지는 잘 모른다)" : ""));
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var m = w.Culture.Of(CustomKind.Memorial)!;
        var dead = m.HonoreeId >= 0 && m.HonoreeId < w.Crew.Count ? w.Crew[m.HonoreeId] : null;
        // 그 사람의 침대 · 없으면 식당의 그 사람 자리 (식당 아무 자리)
        var bed = dead?.Bed;
        var at = bed?.UseSpots.Where(s => dist.Reachable(s)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault()
                 ?? w.Ship.LiveRooms.Where(r => r.Type is RoomType.Mess or RoomType.Lounge).SelectMany(r => r.Cells).Where(x => w.Ship.IsWalkable(x) && dist.Reachable(x)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
        if (at is not Cell spot) return null;
        string item = w.Belongings.Of(c).FirstOrDefault()?.Name ?? (m.ReasonLost ? "물건" : "꽃 대신 접은 종이");
        var toils = new List<Toil>
        {
            new GotoToil(spot),
            new WaitToil(SimTime.Minutes(10), Pose.Standing, spot.Center),
            new DoToil((cm, world) =>
            {
                var cu = world.Culture;
                cu.Keep(cm, CustomKind.Memorial);
                cu.Stats.MemorialItems++;
                cm.Needs.Stress = MathF.Max(0f, cm.Needs.Stress - 0.06f);
                if (cm.Room is Room r) MarkLog.Add(r.Marks, world.Tick, $"{cm.Name}: {m.Honoree}의 자리에 {item}");
                world.Log.Add(world.Tick, LogKind.Life, $"{m.Honoree}의 자리에 {Ko.EulReul(item)} 놓았다" + (m.ReasonLost ? " (다들 그렇게 한다)" : ""), cm.Id);
                Life.Diary(world, cm, Persona.Say(cm, m.ReasonLost ? $"오늘도 {m.Honoree}의 자리에 물건을 놓았다. 누구였는지는 잘 모른다" : $"{m.Honoree}의 자리에 {Ko.EulReul(item)} 두고 왔다"));
                return true;
            }),
        };
        return new Job(this, "추모", toils) { LogText = $"{m.Honoree}의 추모일 — 그 자리에 간다", LogKind = LogKind.Life };
    }
}

/// <summary>고비를 넘긴 날: 저녁을 다 같이 식당에서.</summary>
public sealed class SharedMealActivity : Activity
{
    public override string Id => "sharedmeal";
    public override string Label => "함께 먹는 날";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var cu = w.Culture;
        if (!cu.Follows(c, CustomKind.SurvivalMeal) || !cu.IsDay(CustomKind.SurvivalMeal) || cu.Kept(c, CustomKind.SurvivalMeal) || c.Down || Crisis.Acting(w)) return (0f, "—");
        int hour = (int)(w.Tick % SimTime.TicksPerDay / SimTime.TicksPerHour);
        if (hour < 17 || hour > 21) return (0f, "저녁에");
        return (0.75f, cu.Of(CustomKind.SurvivalMeal)!.ReasonLost ? "다 같이 먹는 날 (왜인지는 모른다)" : $"고비를 넘긴 날 — {cu.Of(CustomKind.SurvivalMeal)!.Origin}");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var mess = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Mess or RoomType.Lounge).SelectMany(r => r.Cells).Where(x => w.Ship.IsWalkable(x) && dist.Reachable(x) && !w.IsSpotTaken(x, c)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
        if (mess is not Cell at) return null;
        var toils = new List<Toil>
        {
            new GotoToil(at),
            new WaitToil(SimTime.Minutes(40), Pose.Sitting),
            new DoToil((cm, world) =>
            {
                var cu = world.Culture;
                cu.Keep(cm, CustomKind.SurvivalMeal);
                int with = world.Crew.Count(o => o != cm && !o.Dead && o.Room == cm.Room);
                if (with > 0) cu.Stats.SharedMeals++;
                cm.Needs.Stress = MathF.Max(0f, cm.Needs.Stress - 0.04f * MathF.Min(5, with));
                cm.Needs.Social = MathF.Min(1f, cm.Needs.Social + 0.25f);
                return true;
            }),
        };
        return new Job(this, "함께 먹는 날", toils) { LogText = "고비를 넘긴 날 — 다 같이 저녁", LogKind = LogKind.Life };
    }
}
