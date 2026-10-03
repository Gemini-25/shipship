using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.0 의복 · 보호구 · 개인 장비
//   옷: 근무 땐 작업복 · 쉴 땐 평상복(사람마다 취향) · 잘 땐 잠옷 — 갈아입는 건 제 침실에서. 경보에 뛰어나온 사람은 잠옷 바람이다.
//   보호구: 방열복(불 끄러 들어가기 전에 걸친다 · 벌 수가 모자란다) · 방사선 조끼(방사선이 높은 방) · 장갑(전기 · 배관 · 외판) · 보안경(용접 · 절단).
//     → 베이고 데고 감전되는 확률과 정도가 달라진다 (잠옷은 불에 더 잘 붙는다).
//   더러움 · 빨래: 손 · 옷의 때는 Soil이 맡고, 여기선 갈아입을 깨끗한 벌 수를 센다 (물을 아껴 빨래가 밀리면 더러운 옷 그대로).
//   찢어짐: 외판에 베이면 소매가 찢어지고, 저녁에 실로 기운다 (실이 없으면 그대로).
//   안경: 눈이 나쁜 사람은 안경이 있어야 계기를 읽는다 — 다치거나 깔고 앉아 금이 가면 숫자가 겹쳐 보여 잘못 적는다.
//     주 컴퓨터가 감지기 값과 맞춰 보고 다시 읽어 달라 하면, 그제야 안경 탓인 걸 알고 의무실 예비 안경으로 바꾸거나 테이프로 감는다.
//   손에 익은 물건: 제 공구는 쓸수록 손에 붙어 빨라지고(별명이 붙는다) · 남의 공구는 어색하다 · 제 컵으로 마시는 아침.

public enum Garment : byte { Work, Casual, Sleep, Heat, RadVest }
public enum Specs : byte { None, Good, Cracked, Taped, Lost }

public sealed class Outfit
{
    public int Id { get; init; }
    public Garment Wearing { get; set; } = Garment.Work;
    /// <summary>방열복 · 조끼 밑에 입은 옷.</summary>
    public Garment Under { get; set; } = Garment.Work;
    public bool Gloves { get; set; }
    public bool Goggles { get; set; }
    public float Torn { get; set; }
    public int CleanSets { get; set; } = 3;
    /// <summary>평상복 취향 (색 · 모양 0~5).</summary>
    public byte Taste { get; init; }
    public bool NeedsGlasses { get; set; }
    public Specs Eyes { get; set; }
    /// <summary>안경 탓에 숫자가 겹쳐 보인다는 걸 안다.</summary>
    public bool KnowsBlurry { get; set; }
    public int Misreads { get; set; }
    public float ToolHours { get; set; }
    public string ToolNick { get; set; } = "";
    public long AwkwardAt = -1_000_000, MugDay = -1, DirtyDay = -1, LowRadSince = -1, WarnedAt = -1_000_000, TornAt = -1;
    public bool PajamaRun { get; set; }

    public bool Blurry => NeedsGlasses && Eyes is Specs.Cracked or Specs.Lost or Specs.None;
    public bool Guarded => Wearing is Garment.Heat or Garment.RadVest;
}

public sealed class WearStats
{
    public int HeatDons, Suiting, VestDons, NoSuitFights, Rushed, PajamaRuns, Readings, Misreads, ComputerCaught, MateCaught, Missed,
               GlassesCracked, Spares, Taped, Mended, NoThread, NoClean, LaundryAsks, Nicknames, Awkward, Mugs, MugMissed, Changes, ComputerHeatWarn;
    public string Summary() =>
        $"방열복 {HeatDons} (걸치기 시작 {Suiting} · 없이 들어감 {NoSuitFights} · 서둘러 그냥 {Rushed}) · 조끼 {VestDons} · 잠옷 바람 {PajamaRuns} · 갈아입기 {Changes} · " +
        $"계기 읽기 {Readings} (잘못 {Misreads} · 컴퓨터가 잡음 {ComputerCaught} · 동료가 {MateCaught} · 놓침 {Missed}) · 안경 금 {GlassesCracked} · 예비 {Spares} · 테이프 {Taped} · " +
        $"기움 {Mended} (실 없음 {NoThread}) · 깨끗한 옷 없음 {NoClean} · 빨래 권고 {LaundryAsks} · 공구 별명 {Nicknames} · 어색 {Awkward} · 제 컵 {Mugs}/{MugMissed}";
}

public sealed class WearSystem
{
    private readonly World _w;
    private readonly SortedDictionary<int, Outfit> _of = new();
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7253 + 911));
    private long _nextMin, _nextHour;
    private bool _stocked;

    public WearStats Stats { get; } = new();
    public int HeatSuits { get; set; }
    public int RadVests { get; set; }
    public int SpareGlasses { get; set; }

    public WearSystem(World w) => _w = w;

    public Outfit? Peek(CrewMember c) => _of.TryGetValue(c.Id, out var o) ? o : null;

    public Outfit Of(CrewMember c)
    {
        if (_of.TryGetValue(c.Id, out var o)) return o;
        uint h = unchecked((uint)c.Id * 2654435761u ^ (uint)_w.Seed * 40503u);
        h ^= h >> 13;
        bool needs = !c.IsChild && h % 100 < 24;
        o = new Outfit { Id = c.Id, Taste = (byte)(h / 100 % 6), NeedsGlasses = needs, Eyes = needs ? Specs.Good : Specs.None };
        _of[c.Id] = o;
        return o;
    }

    public int Wearing(Garment g) { int n = 0; foreach (var o in _of.Values) if (o.Wearing == g) n++; return n; }
    public int HeatFree => HeatSuits - Wearing(Garment.Heat);
    public int VestFree => RadVests - Wearing(Garment.RadVest);

    private void Stock()
    {
        if (_stocked) return;
        _stocked = true;
        int n = _w.Crew.Count;
        HeatSuits = Math.Max(2, n / 8);
        RadVests = Math.Max(2, n / 6);
        SpareGlasses = 1 + n / 12;
    }

    private bool OnShift(CrewMember c) =>
        SimTime.InWindow(SimTime.HourOfDay(_w.Tick), c.Schedule.WorkStart, c.Schedule.WorkLength) && c.ExcusedUntil <= _w.Tick || c.CoveringUntil > _w.Tick;

    private static bool Bedroom(Room? r) => r != null && r.Type is RoomType.Quarters or RoomType.PrivateCabins or RoomType.QuietQuarters or RoomType.Laundry;

    private static bool GloveWork(WorkKind k) => k is WorkKind.ResetBreaker or WorkKind.RestoreCircuit or WorkKind.InstallJumper or WorkKind.ReplacePanel or WorkKind.IsolatePower
        or WorkKind.BreakerOn or WorkKind.PatchPipe or WorkKind.ReplacePipe or WorkKind.LayBypass or WorkKind.SealBreach or WorkKind.RepairHull or WorkKind.WeldBulkhead
        or WorkKind.Repair or WorkKind.Cannibalize or WorkKind.Extinguish or WorkKind.RepairRadiator;

    private static bool GoggleWork(WorkKind k) => k is WorkKind.WeldBulkhead or WorkKind.RepairHull or WorkKind.Cannibalize or WorkKind.Fabricate or WorkKind.SealBreach;

    private static bool ToolSkill(Skill s) => s is Skill.Mechanics or Skill.Electrical or Skill.Engineering;

    // ───────────────────────────── 틱 ─────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        if (w.Tick >= _nextMin) { _nextMin = w.Tick + SimTime.Minutes(1); Stock(); foreach (var c in w.Crew) if (!c.Dead) Dress(c, Of(c)); }
        if (w.Tick >= _nextHour) { _nextHour = w.Tick + SimTime.Hours(1); Hour(); }
    }

    private void Dress(CrewMember c, Outfit o)
    {
        var w = _w;
        var order = c.Job?.Order;
        o.Gloves = order != null && GloveWork(order.Kind);
        o.Goggles = order != null && GoggleWork(order.Kind);
        if (c.Suit != null) return;

        // 방열복: 불이 꺼지고 일이 끝나면 벗어 건다
        if (o.Wearing == Garment.Heat && order?.Kind != WorkKind.Extinguish && (c.Room == null || w.Fire.CountIn(c.Room) == 0))
        {
            o.Wearing = o.Under;
            w.Log.Add(w.Tick, LogKind.Life, "그을린 방열복을 벗어 걸었다", c.Id);
        }
        // 방사선 조끼: 방사선이 높은 방에 들어가면 걸치고, 한동안 낮으면 벗는다
        float rad = c.Room?.Radiation ?? 0f;
        if (o.Wearing == Garment.RadVest)
        {
            if (rad < 0.05f) { if (o.LowRadSince < 0) o.LowRadSince = w.Tick; else if (w.Tick - o.LowRadSince > SimTime.Minutes(30)) { o.Wearing = o.Under; o.LowRadSince = -1; } }
            else o.LowRadSince = -1;
        }
        else if (!o.Guarded && rad >= 0.12f && c.IsAwake && VestFree > 0)
        {
            o.Under = o.Wearing;
            o.Wearing = Garment.RadVest;
            o.LowRadSince = -1;
            Stats.VestDons++;
            w.Log.Add(w.Tick, LogKind.Life, $"{c.Room?.Name}에 들어가며 납 조끼를 걸쳤다", c.Id);
        }

        // 근무 · 쉼 · 잠: 제 침실에서 갈아입는다 (경보에 뛰어나오면 잠옷 그대로)
        var want = c.Pose == Pose.Sleeping ? Garment.Sleep : OnShift(c) ? Garment.Work : Garment.Casual;
        var cur = o.Guarded ? o.Under : o.Wearing;
        if (want != cur && Bedroom(c.Room) && c.Pose != Pose.Working)
        {
            if (o.Guarded) o.Under = want; else o.Wearing = want;
            if (want != Garment.Sleep) o.PajamaRun = false;
            Stats.Changes++;
        }
        if (cur == Garment.Sleep && c.IsAwake && c.Job?.Urgent == true && !o.PajamaRun)
        {
            o.PajamaRun = true;
            Stats.PajamaRuns++;
            w.Log.Add(w.Tick, LogKind.Life, $"경보에 잠옷 바람으로 뛰어나왔다", c.Id);
        }

        // 손에 익은 공구 · 남의 공구
        if (order != null && ToolSkill(order.Skill) && c.Pose == Pose.Working)
        {
            if (c.ToolFactor > 1f)
            {
                o.ToolHours += 1f / 60f;
                if (o.ToolHours >= 20f && o.ToolNick.Length == 0) Nick(c, o);
            }
            else if (c.ToolFactor < 1f && w.Tick - o.AwkwardAt > SimTime.Hours(12))
            {
                o.AwkwardAt = w.Tick;
                Stats.Awkward++;
                c.Say(w, Persona.Say(c, "남의 공구라 손에 안 붙는다 — 영 어색하네"));
            }
        }

        // 주 컴퓨터: 방열복이 남았는데 그냥 불길로 들어간다
        if (order?.Kind == WorkKind.Extinguish && o.Wearing != Garment.Heat && HeatFree > 0 && c.Pose == Pose.Working
            && w.Automation.Present && w.Automation.MainOnline && w.Tick - o.WarnedAt > SimTime.Minutes(20))
        {
            o.WarnedAt = w.Tick;
            Stats.ComputerHeatWarn++;
            w.Log.Add(w.Tick, LogKind.Warning, $"주 컴퓨터: {Ko.EunNeun(c.Name)} 방열복 없이 불길 앞에 있다 — 소화기 함 옆 방열복 {HeatFree}벌이 남았다", c.Id);
        }
    }

    private static readonly string[] Nicks = { "늙은 렌치", "붉은 손잡이", "복덩이", "말썽쟁이", "할배 스패너", "까마귀", "단짝", "고집쟁이" };

    private void Nick(CrewMember c, Outfit o)
    {
        var w = _w;
        o.ToolNick = Nicks[(int)((uint)(c.Id * 7 + w.Seed) % (uint)Nicks.Length)];
        Stats.Nicknames++;
        w.Log.Add(w.Tick, LogKind.Life, $"손때 묻은 공구에 '{o.ToolNick}'라는 별명이 붙었다", c.Id);
        Life.Diary(w, c, Persona.Say(c, $"내 공구 '{o.ToolNick}' — 이젠 눈 감고도 손이 간다"));
    }

    private void Hour()
    {
        var w = _w;
        float hour = SimTime.HourOfDay(w.Tick);
        int day = SimTime.Day(w.Tick);
        int noClean = 0;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away) continue;
            var o = Of(c);
            if (!c.IsChild && c.Soil.ClothesMax > 0.45f && o.CleanSets == 0)
            {
                noClean++;
                if (o.DirtyDay != day && c.IsAwake)
                {
                    o.DirtyDay = day;
                    Stats.NoClean++;
                    if (Life.Has(c, Habit.NeatFreak)) { c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.05f); c.Say(w, Persona.Say(c, "갈아입을 옷이 하나도 없다니")); }
                }
            }
            if (!c.IsAwake) continue;
            // 제 컵 (아침)
            if (hour >= 7f && hour < 10f && o.MugDay != day && (Life.Has(c, Habit.CoffeeAddict) || Life.Has(c, Habit.TeaLover)))
            {
                o.MugDay = day;
                var mug = w.Belongings.All.FirstOrDefault(b => b.Owner == c.Id && b.Kind == BelongingKind.Mug);
                if (mug is { Usable: true }) { Stats.Mugs++; w.Brain2.Emotions.Feel(c, Feeling.Joy, 0.05f, $"제 {mug.Name}에 마신 아침"); }
                else
                {
                    Stats.MugMissed++;
                    c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.03f);
                    c.Say(w, Persona.Say(c, "남의 컵으로 마시니 맛이 안 난다"));
                }
            }
            // 찢어진 옷 기우기 (저녁 · 쉬는 때)
            if (o.Torn > 0.3f && hour >= 19f && hour < 23f && !OnShift(c) && c.Job?.Urgent != true)
            {
                if (Life.Take(w, ItemKind.Thread, 1)) { o.Torn = 0f; Stats.Mended++; w.Log.Add(w.Tick, LogKind.Life, "찢어진 소매를 실로 기웠다", c.Id); }
                else if (o.TornAt >= 0 && w.Tick - o.TornAt > SimTime.Hours(20)) { o.TornAt = w.Tick; Stats.NoThread++; c.Say(w, Persona.Say(c, "기울 실이 없다 — 찢어진 채로 다녀야겠다")); }
            }
            // 안경: 깔고 앉기 · 떨어뜨리기 (드물게)
            if (o.NeedsGlasses && o.Eyes == Specs.Good && R.Chance(0.0012f)) Crack(c, o, "안경을 깔고 앉아 알에 금이 갔다");
            // 흐릿한 걸 알면 고친다: 의무실 예비 안경 → 없으면 손재주 있는 사람이 테이프로
            if (o.Blurry && (o.KnowsBlurry || R.Chance(0.15f)) && c.Job?.Urgent != true) FixGlasses(c, o);
        }
        // 주 컴퓨터: 빨래가 밀려 갈아입을 옷이 없는 사람이 여럿
        if ((int)hour == 8 && noClean >= 2 && w.Automation.Present && w.Automation.MainOnline)
        {
            Stats.LaundryAsks++;
            w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: 깨끗한 옷이 떨어진 사람이 {noClean}명 — 물이 되면 빨래부터 돌리자고 함장에게 올렸다", -1);
        }
    }

    private void Crack(CrewMember c, Outfit o, string how)
    {
        var w = _w;
        o.Eyes = Specs.Cracked;
        o.KnowsBlurry = false;
        Stats.GlassesCracked++;
        w.Log.Add(w.Tick, LogKind.Life, how, c.Id);
    }

    private void FixGlasses(CrewMember c, Outfit o)
    {
        var w = _w;
        if (SpareGlasses > 0)
        {
            SpareGlasses--;
            o.Eyes = Specs.Good;
            Stats.Spares++;
            w.Log.Add(w.Tick, LogKind.Life, "의무실 서랍의 예비 안경으로 바꿔 꼈다 — 도수가 조금 안 맞는다", c.Id);
            Life.Diary(w, c, Persona.Say(c, "예비 안경을 꼈다. 세상이 다시 또렷하다"));
            return;
        }
        var fixer = w.Crew.Where(x => !x.Dead && x != c && x.IsAwake && x.Room == c.Room && (Life.Has(x, Habit.Tinkerer) || x.Role is CrewRole.Technician or CrewRole.Medic))
            .OrderBy(x => x.Id).FirstOrDefault();
        if (fixer == null && !(Life.Has(c, Habit.Tinkerer) || c.Role == CrewRole.Technician)) return;
        o.Eyes = Specs.Taped;
        Stats.Taped++;
        if (fixer != null) { c.ChangeAffinity(fixer, 0.04f); w.Relations.Remember(c, fixer, RelationReason.FixedMyThing, "금 간 안경을 테이프로 감아 줬다"); }
        w.Log.Add(w.Tick, LogKind.Life, fixer != null ? $"{Ko.IGa(fixer.Name)} 금 간 안경을 테이프로 감아 줬다" : "금 간 안경을 테이프로 칭칭 감았다", c.Id);
    }

    // ───────────────────────────── 불 · 방사선 · 다침 (다른 시스템이 부른다) ─────────────────────────────

    /// <summary>불 끄러 가기 전에 방열복을 걸친다 (남은 벌이 있으면 · 서두르는 사람은 작은 불이면 그냥 뛰어든다).</summary>
    public void DonForFire(CrewMember c, List<Toil> toils, WorkOrder order)
    {
        var w = _w;
        var o = Of(c);
        if (o.Wearing == Garment.Heat || c.Suit != null) return;
        if (HeatFree <= 0) { Stats.NoSuitFights++; return; }
        int fires = order.Target.Room is Room r ? w.Fire.CountIn(r) : 0;
        if ((Life.Has(c, Habit.Hasty) || Life.Has(c, Habit.Daredevil)) && fires < 3) { Stats.Rushed++; Stats.NoSuitFights++; return; }
        Stats.Suiting++;
        toils.Add(new WaitToil(SimTime.Minutes(1.2f), Pose.Working));
        toils.Add(new DoToil((cm, world) =>
        {
            var x = Of(cm);
            if (x.Wearing == Garment.Heat) return true;
            if (HeatFree <= 0) { Stats.NoSuitFights++; return true; }
            x.Under = x.Guarded ? x.Under : x.Wearing;
            x.Wearing = Garment.Heat;
            Stats.HeatDons++;
            world.Log.Add(world.Tick, LogKind.Work, "소화기 함 옆 방열복을 걸치고 두건을 내렸다", cm.Id);
            return true;
        }));
    }

    /// <summary>작업 중 다칠 확률 배율 (옷 · 보호구 · 흐린 눈).</summary>
    public float RiskMul(CrewMember c, string cause)
    {
        var o = Of(c);
        bool burn = cause.Contains("화상") || cause.Contains("불"), shock = cause.Contains("감전"), cut = cause.Contains("베");
        float m = 1f;
        if (burn) m *= HeatMul(c);
        if (o.Gloves) m *= shock ? 0.5f : burn ? 0.8f : cut ? 0.7f : 1f;
        if (o.Goggles && cause.Contains("용접")) m *= 0.7f;
        if (o.Blurry) m *= 1.15f;
        return m;
    }

    /// <summary>불길에 데는 정도 (방열복은 막고 · 잠옷은 잘 붙는다).</summary>
    public float HeatMul(CrewMember c) => Of(c).Wearing switch { Garment.Heat => 0.35f, Garment.Sleep => 1.3f, Garment.Casual => 1.1f, _ => 1f };

    public float RadMul(CrewMember c) => Peek(c)?.Wearing == Garment.RadVest ? 0.55f : 1f;

    /// <summary>다쳤다: 옷이 찢어지고 · 크게 다치면 안경에 금이 간다.</summary>
    public void Hurt(CrewMember c, string cause, bool bad)
    {
        var o = Of(c);
        if (cause.Contains("베") || cause.Contains("화상")) { o.Torn = MathF.Min(1f, o.Torn + (bad ? 0.6f : 0.35f)); o.TornAt = _w.Tick; }
        if (bad && o.NeedsGlasses && o.Eyes is Specs.Good or Specs.Taped && R.Chance(0.5f)) Crack(c, o, "넘어지며 안경이 날아가 알에 금이 갔다");
    }

    // ───────────────────────────── 깨끗한 옷 · 빨래 (Soil이 부른다) ─────────────────────────────

    public bool TakeClean(CrewMember c)
    {
        var o = Of(c);
        if (o.CleanSets <= 0) return false;
        o.CleanSets--;
        return true;
    }

    public void Washed() { foreach (var o in _of.Values) o.CleanSets = 3; }

    // ───────────────────────────── 계기 읽기 · 손에 익은 공구 ─────────────────────────────

    /// <summary>흐린 눈이 숫자를 잘못 읽을 확률.</summary>
    public float Blur(CrewMember c)
    {
        var o = Of(c);
        if (!o.NeedsGlasses) return 0f;
        float p = o.Eyes switch { Specs.Good => 0f, Specs.Taped => 0.12f, Specs.Cracked => 0.6f, _ => 0.75f };
        if (p > 0f && c.Room?.Dark == true) p += 0.1f;
        return p;
    }

    /// <summary>계기를 읽는다: 흐리면 겹쳐 보이는 숫자(3↔8 · 6↔0 · 1↔7 · 5↔6)를 적는다.</summary>
    public (int shown, int truth) ReadGauge(CrewMember c, Machine m)
    {
        int truth = (int)MathF.Round(Math.Clamp(m.Condition, 0f, 1f) * 100f);
        Stats.Readings++;
        if (!R.Chance(Blur(c))) return (truth, truth);
        var s = truth.ToString().ToCharArray();
        for (int i = 0; i < s.Length; i++)
        {
            char n = s[i] switch { '3' => '8', '8' => '3', '6' => '0', '0' => '6', '1' => '7', '7' => '1', '5' => '6', '9' => '4', _ => s[i] };
            if (n != s[i]) { s[i] = n; break; }
        }
        int shown = int.Parse(new string(s));
        if (shown == truth) shown = truth >= 50 ? truth - 30 : truth + 30;
        return (shown, truth);
    }

    /// <summary>일을 마친 순간: 고친 설비의 계기를 읽어 적는다 — 틀린 값은 주 컴퓨터 · 곁의 동료가 잡거나 그대로 남는다.</summary>
    public void OnJobEnded(CrewMember c, Job job, ToilStatus status)
    {
        var w = _w;
        if (status != ToilStatus.Succeeded || job.Order is not { Kind: WorkKind.Repair or WorkKind.Maintain or WorkKind.Upgrade or WorkKind.RestoreGrade } o) return;
        if (o.Target.Furniture?.Machine is not Machine m || c.Dead) return;
        Gauge(c, m);
    }

    public bool Gauge(CrewMember c, Machine m)
    {
        var w = _w;
        var (shown, truth) = ReadGauge(c, m);
        if (shown == truth) return false;
        var o = Of(c);
        o.Misreads++;
        Stats.Misreads++;
        w.Log.Add(w.Tick, LogKind.Work, $"{m.Name} 계기를 {shown}%로 적었다", c.Id);
        if (w.Automation.Present && w.Automation.MainOnline && m.SensorCal >= 0.6f)
        {
            Stats.ComputerCaught++;
            o.KnowsBlurry = true;
            w.Log.Add(w.Tick, LogKind.Warning, $"주 컴퓨터: {Ko.IGa(c.Name)} 적은 {m.Name} 값 {shown}%가 감지기 {truth}%와 다르다 — 다시 읽어 달라", c.Id);
            c.Say(w, Persona.Say(c, "숫자가 겹쳐 보여서… 안경 때문이다"));
            return true;
        }
        var mate = w.Crew.Where(x => !x.Dead && x != c && x.IsAwake && x.Room == c.Room && !Of(x).Blurry).OrderBy(x => x.Id).FirstOrDefault();
        if (mate != null)
        {
            Stats.MateCaught++;
            o.KnowsBlurry = true;
            mate.Say(w, Persona.Say(mate, $"{shown}? {truth}인데. 안경 좀 봐"));
            return true;
        }
        Stats.Missed++;
        w.Log.Add(w.Tick, LogKind.Work, $"잘못 적은 {m.Name} 값이 일지에 그대로 남았다", c.Id);
        return true;
    }

    /// <summary>제 공구: 쓸수록 손에 붙는다 (1.04 → 1.08).</summary>
    public float OwnToolFeel(CrewMember c) => 1.04f + 0.04f * MathF.Min(1f, (Peek(c)?.ToolHours ?? 0f) / 30f);

    /// <summary>남의 공구: 어색하다.</summary>
    public float BorrowFeel(CrewMember c) => 0.96f;

    public void Hash(Action<long> I, Action<float> F)
    {
        I(_of.Count); I(HeatSuits); I(RadVests); I(SpareGlasses);
        foreach (var o in _of.Values) { I(o.Id); I((long)o.Wearing); I(o.CleanSets); I((long)o.Eyes); I(o.Misreads); F(o.Torn); F(o.ToolHours); }
    }
}
