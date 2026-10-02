using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v17.1 몸의 변화: 머리카락 · 수염이 자라고, 먹은 양 · 운동으로 체중이 변한다.
//   머리: 사람마다 모양 · 색 · 좋아하는 길이가 있고 하루 몇 mm씩 자란다. 덥수룩해지면 그걸 본 친한 사람이 잘라 준다
//     (솜씨 · 다친 손 · 피로 · 감정 · 성미에 따라 말끔 / 그럭저럭 / 삐뚤빼뚤 — 관계 · 기분 · 기억 · 다음 부탁이 갈린다).
//     자른 머리카락은 바닥에 남고(깔끔한 사람이 치우며 투덜댄다) 옷에 붙는다(빨래). 불 곁의 긴 머리는 그을린다.
//   체중: 실제로 먹은 것(허기가 채워진 만큼)과 쓴 것(몸무게 · 식성 · 움직임 · 운동)의 차이. 배급이면 빠지고, 군것질 · 냄새로 당긴 끼니면 붙는다.
//     우주복은 맞춘 치수가 있다 — 5kg 넘게 벗어나면 꽉 끼거나 헐렁해 선외에서 관절이 긁히고 목 고리가 덜 물려 새고 산소를 더 쓴다.
//     입을 때 투덜거리고, 보관함에서 치수를 조정한다.
//   본 사람만 안다: 사람마다 남의 지난 모습(머리 길이 · 자른 때 · 체중)을 기억하고, 다시 보면 견주어 말한다 (칭찬 · 놀림 · 걱정).
//     누가 머리를 잘 자르는지도 본 것 · 들은 것으로 안다 (덥수룩한 사람은 그 사람에게 부탁한다).
//   주 컴퓨터는 잰 체중만 안다 (자기 침대 압력 감지기 · 의무실 체중계 · 우주복 원격 측정): 우주복 치수 불일치 · 체중 추세를 경고 · 조정 작업을 올린다.

public enum HairStyle : byte { Buzz, Crop, Side, Curly, Bob, Long, Ponytail, Bun, Braid, Bald }
public enum OutfitPattern : byte { Plain, Stripe, Sleeves, Vest, Scarf, Patch }
public enum CutResult : byte { None, Neat, Fair, Botched, Singed }

/// <summary>한 사람의 생김새와 몸 (머리 · 수염 · 체중 · 우주복 치수 · 이발 솜씨).</summary>
public sealed class BodyLook
{
    public int Id { get; init; }
    public byte Skin { get; init; }
    public byte HairColor { get; init; }
    public HairStyle Style { get; set; }
    public OutfitPattern Pattern { get; init; }
    public float HeightCm { get; init; }
    public float GrowCmDay { get; init; }
    /// <summary>지금 머리 길이 (cm) · 그 사람이 좋아하는 길이 (자른 직후).</summary>
    public float HairCm { get; set; }
    public float StyleCm { get; set; }
    /// <summary>수염이 나는 사람 · 기르는 사람 (안 기르면 아침마다 깎는다).</summary>
    public bool Stubbly { get; init; }
    public bool KeepsBeard { get; init; }
    public float BeardMm { get; set; }
    public float BeardKeepMm { get; init; }
    public float Kg { get; set; }
    public float StartKg { get; init; }
    /// <summary>우주복을 맞춘 체중.</summary>
    public float SuitKg { get; set; }
    /// <summary>이발 솜씨 0~1 (자를수록 는다).</summary>
    public float Barber { get; set; }
    /// <summary>삐뚤빼뚤함 0~1 (자라면서 옅어진다).</summary>
    public float Uneven { get; set; }
    public CutResult LastCut { get; set; }
    public long CutAt { get; set; } = -1;
    public int CutBy { get; set; } = -1;
    public int Cuts, CutsGiven;
    public long ShaggySince = -1;
    /// <summary>오늘 먹은 몫 · 쓴 몫 (허기 단위 · 1 = 배부름).</summary>
    public float Intake, Burn, LastIntake, LastBurn;
    public long ExerciseTicks;
    internal float FoodSeen = -1f, FitSeen = -1f, LastIntakeStep;
    internal long LastGrumble = -1_000_000;
    internal bool SuitOn, LeakBoosted, EvaGrumbled;
    /// <summary>주 컴퓨터가 마지막으로 잰 체중 (−1 = 아직 모른다).</summary>
    public float KnownKg { get; set; } = -1f;
    public long KnownAt { get; set; } = -1;
    public string KnownBy { get; set; } = "";
    public List<(int day, float kg)> Measured { get; } = new();
    /// <summary>컴퓨터가 우주복 치수 조정 작업을 올렸다.</summary>
    public bool FitOrder { get; set; }
    /// <summary>이 사람이 자기 우주복이 안 맞는 걸 안다 (입어 봤다 · 들었다).</summary>
    public bool FitKnown { get; set; }
    public long LastShave = -1_000_000, LastTrend = -1_000_000;

    public float Bmi => Kg / (HeightCm * HeightCm / 10000f);
    public float Misfit => Kg - SuitKg;
    public bool Tight => Misfit >= Body2System.FitWarnKg;
    public bool Loose => Misfit <= -Body2System.FitWarnKg;
    public static float ShagAt(float styleCm) => styleCm * 1.45f + 1.5f;
    /// <summary>0 = 막 자름 · 1 = 덥수룩 (그 이상 계속 자란다).</summary>
    public float Shag => Style == HairStyle.Bald ? 0f : Math.Clamp((HairCm - StyleCm) / (ShagAt(StyleCm) - StyleCm), 0f, 2.5f);
    public bool Shaggy => Shag >= 1f;
}

/// <summary>한 사람이 다른 사람의 모습을 마지막으로 본 것 (견주어 알아챈다).</summary>
public sealed class LookImpression
{
    public float HairCm;
    public long CutAt;
    public float Uneven;
    public float Kg;
    public long Tick;
    public long Said = -1_000_000;
}

/// <summary>바닥에 떨어진 머리카락 (치울 때까지).</summary>
public sealed class HairClip
{
    public int Id { get; init; }
    public Cell Cell { get; init; }
    public int RoomId { get; init; }
    public long Tick { get; init; }
    public float Cm { get; init; }
    public byte Color { get; init; }
    public int Barber { get; init; }
    public int Client { get; init; }
    public int Sweeper { get; set; } = -1;
}

/// <summary>이발 한 번 (자르는 사람 · 앉는 사람 · 의자).</summary>
public sealed class HairSession
{
    public int Id { get; init; }
    public int Barber { get; init; }
    public int Client { get; init; }
    public Cell Seat { get; init; }
    public int SeatFurniture { get; init; } = -1;
    public long Start { get; init; }
    public bool Self { get; init; }
    public float Progress { get; set; }
    public bool Seated { get; set; }
    public bool Done { get; set; }
    public bool Canceled { get; set; }
}

public sealed class Body2Stats
{
    public int Cuts, Neat, Fair, Botched, SelfCuts, Refusals, Singed, Shaves, Trims, Requests;
    public int HairComments, WeightComments, Teases, Compliments, Worries, Reputation;
    public int Measures, FitWarnings, TrendWarnings, FitAdjusts, Heeded, Shrugged, MisfitEva, MisfitScuffs, Grumbles, Jogs, Sweeps, Clips, ClothesHair;
    public string Summary() =>
        $"이발 {Cuts}(말끔 {Neat} · 그럭저럭 {Fair} · 삐뚤 {Botched} · 혼자 {SelfCuts} · 거절 {Refusals} · 부탁 {Requests}) · 그을림 {Singed} · 면도 {Shaves} · 다듬기 {Trims}"
        + $" · 알아채고 말함 머리 {HairComments} · 체중 {WeightComments}(놀림 {Teases} · 칭찬 {Compliments} · 걱정 {Worries})"
        + $" · 컴퓨터 측정 {Measures} · 치수 경고 {FitWarnings} · 추세 경고 {TrendWarnings}(따름 {Heeded} · 흘려들음 {Shrugged}) · 치수 조정 {FitAdjusts}"
        + $" · 안 맞는 우주복 선외 {MisfitEva}(긁힘 {MisfitScuffs}) · 투덜 {Grumbles} · 달리기 {Jogs} · 머리카락 치움 {Sweeps}/{Clips}";
}

public sealed partial class Body2System
{
    /// <summary>성능 비교용 (끄면 머리 · 체중이 그대로).</summary>
    public static bool Enabled = Environment.GetEnvironmentVariable("SHIPSIM_BODY2") != "0";
    public const float FitWarnKg = 5f;
    /// <summary>허기 1(배부름)만큼 남거나 모자라면 붙거나 빠지는 체중 (게임용으로 빠르게).</summary>
    public const float KgPerFood = 0.6f;
    /// <summary>시작 체중 · 식성 1 · 보통 하루 움직임일 때 한 시간에 쓰는 몫.</summary>
    public const float BaseBurn = 0.088f;

    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7121 + 389));
    private readonly Dictionary<int, BodyLook> _looks = new();
    private readonly Dictionary<long, LookImpression> _seen = new();
    /// <summary>누가 누구의 이발 솜씨를 어떻게 믿나 (본 것 · 들은 것) 0~1.</summary>
    private readonly Dictionary<long, float> _rep = new();
    /// <summary>덥수룩한 사람의 부탁 (손님 → 자르는 사람).</summary>
    private readonly Dictionary<int, (int barber, long tick)> _asks = new();
    /// <summary>거절당한 때 (자르는 사람, 손님).</summary>
    private readonly Dictionary<long, long> _refused = new();
    private long _nextStep = -1, _nextLook = -1;
    private int _lastDay = -1;
    private int _nextClip = 1, _nextSession = 1;
    public List<HairClip> Clips { get; } = new();
    public List<HairSession> Sessions { get; } = new();
    public Body2Stats Stats { get; } = new();

    public Body2System(World w) => _w = w;

    private static long Pair(int a, int b) => ((long)a << 24) | (uint)b;

    /// <summary>그 사람의 생김새 (처음 부르면 만든다 — 사람마다 따로 뽑아 순서와 상관없다).</summary>
    public BodyLook Of(CrewMember c)
    {
        if (!_looks.TryGetValue(c.Id, out var l)) _looks[c.Id] = l = Make(c);
        return l;
    }

    /// <summary>화면용: 있으면 (만들지 않는다).</summary>
    public BodyLook? Peek(CrewMember c) => _looks.TryGetValue(c.Id, out var l) ? l : null;

    public LookImpression? Seen(CrewMember who, CrewMember about) => _seen.TryGetValue(Pair(who.Id, about.Id), out var x) ? x : null;
    public float Rep(CrewMember who, CrewMember barber) => _rep.TryGetValue(Pair(who.Id, barber.Id), out var v) ? v : -1f;
    public CrewMember? Crew(int id) => id < 0 ? null : _w.Crew.FirstOrDefault(c => c.Id == id);

    private BodyLook Make(CrewMember c)
    {
        var r = new Rng(unchecked(_w.Seed * 7919 + c.Id * 104729 + 17));
        bool kid = c.IsChild;
        // 머리 모양: 경력 · 성미가 조금 기운다 (군인 · 운동선수는 짧게, 예술가 · 음악가는 길거나 묶는다)
        var styles = new List<HairStyle> { HairStyle.Crop, HairStyle.Crop, HairStyle.Side, HairStyle.Side, HairStyle.Curly, HairStyle.Bob, HairStyle.Long, HairStyle.Ponytail, HairStyle.Bun, HairStyle.Braid, HairStyle.Buzz, HairStyle.Bald };
        if (c.Background is Background.Soldier or Background.MilitaryTech or Background.Athlete or Background.Police or Background.Firefighter) { styles.Add(HairStyle.Buzz); styles.Add(HairStyle.Crop); }
        if (c.Background is Background.Artist or Background.Musician or Background.Writer) { styles.Add(HairStyle.Long); styles.Add(HairStyle.Ponytail); styles.Add(HairStyle.Braid); }
        if (c.Background is Background.Chef or Background.Baker or Background.Nurse or Background.MedStudent) { styles.Add(HairStyle.Bun); styles.Add(HairStyle.Ponytail); }
        if (c.Age < 40f || kid) styles.Remove(HairStyle.Bald);
        var style = r.Pick(styles);
        float styleCm = style switch
        {
            HairStyle.Buzz => 0.8f, HairStyle.Crop => 3f, HairStyle.Side => 5f, HairStyle.Curly => 6f, HairStyle.Bob => 14f,
            HairStyle.Long => 30f, HairStyle.Ponytail => 28f, HairStyle.Bun => 32f, HairStyle.Braid => 34f, _ => 0f,
        } * r.Range(0.85f, 1.2f);
        byte hairColor = (byte)r.Range(0, 6);
        if (c.Background is Background.Artist or Background.Musician or Background.DroneRacer && r.Chance(0.6f)) hairColor = (byte)(6 + r.Range(0, 2)); // 물들인 머리
        if (c.Age > 52f && r.Chance(0.7f)) hairColor = 8; // 희끗희끗
        bool stubbly = !kid && r.Chance(0.5f);
        float h = kid ? 100f + 4.5f * c.Age : r.Range(156f, 192f);
        float bmi = r.Range(19.5f, 27.5f) + (Life.Has(c, Habit.Snacker) ? 1.5f : 0f) - (Life.Has(c, Habit.GymRat) ? 1f : 0f);
        float kg = MathF.Round(bmi * h * h / 10000f, 1);
        float barber = 0.12f + r.Range(0f, 0.3f);
        if (c.Background is Background.Artist or Background.Nurse or Background.Carpenter or Background.Chef or Background.Baker or Background.Veterinarian) barber += 0.2f;
        if (c.Hobbies.Any(x => x is Hobby.Knitting or Hobby.ModelBuilding or Hobby.Painting or Hobby.Woodwork)) barber += 0.15f;
        if (Life.Has(c, Habit.Perfectionist) || Life.Has(c, Habit.Methodical)) barber += 0.1f;
        if (Life.Has(c, Habit.Hasty) || Life.Has(c, Habit.Fidgety)) barber -= 0.1f;
        var l = new BodyLook
        {
            Id = c.Id, Skin = (byte)r.Range(0, 6), HairColor = hairColor, Style = style, StyleCm = styleCm,
            Pattern = (OutfitPattern)r.Range(0, 6), HeightCm = MathF.Round(h), GrowCmDay = r.Range(0.3f, 0.45f),
            Stubbly = stubbly, KeepsBeard = stubbly && r.Chance(0.35f), BeardKeepMm = r.Range(5f, 14f),
            Kg = kg, StartKg = kg, SuitKg = MathF.Round(kg + r.Range(-1.5f, 1.5f), 1), Barber = Math.Clamp(barber, 0.05f, 0.9f),
        };
        l.HairCm = style == HairStyle.Bald ? 0f : styleCm + r.Range(0f, 0.95f) * (BodyLook.ShagAt(styleCm) - styleCm);
        l.BeardMm = !stubbly ? 0f : l.KeepsBeard ? l.BeardKeepMm * r.Range(0.6f, 1.3f) : r.Range(0.2f, 2f);
        return l;
    }

    // ─────────────────────────────── 매 틱 ───────────────────────────────

    public void Update(float dt)
    {
        if (!Enabled) return;
        var w = _w;
        // 먹은 것: 허기가 채워진 만큼 (식사 · 간식 · 남이 건넨 것 · 냄새에 당긴 끼니 — 어디서든)
        foreach (var c in w.Crew)
        {
            if (c.Dead) continue;
            var l = Of(c);
            float food = c.Needs.Food;
            if (l.FoodSeen >= 0f && food > l.FoodSeen) l.Intake += food - l.FoodSeen;
            l.FoodSeen = food;
            bool on = c.Suit != null;
            if (on != l.SuitOn)
            {
                l.SuitOn = on;
                if (on) Donned(c, l);
                else { l.LeakBoosted = false; l.EvaGrumbled = false; }
            }
        }
        if (w.Tick < _nextStep) return;
        long step = SimTime.Minutes(10);
        _nextStep = w.Tick + step;
        float hours = step / (float)SimTime.TicksPerHour;
        long pf = Prof.Now;
        Step(hours);
        if (w.Tick >= _nextLook)
        {
            _nextLook = w.Tick + SimTime.Minutes(20);
            if (!Crisis.Acting(w)) LookAround();
        }
        int day = w.Day;
        if (day != _lastDay)
        {
            if (_lastDay >= 0) NewDay();
            _lastDay = day;
        }
        if (SimTime.HourOfDay(w.Tick) is >= 8f and < 8.17f) ComputerDaily();
        Prof.Lap("sys.Body2", pf);
    }

    private static float AppetiteOf(CrewMember c)
    {
        float a = c.Traits.Appetite;
        foreach (var h in c.Habits) a *= Persona.Of(h).Appetite;
        return a;
    }

    private void Step(float hours)
    {
        var w = _w;
        float days = hours / 24f;
        foreach (var c in w.Crew)
        {
            if (c.Dead) continue;
            var l = Of(c);
            // ── 머리 · 수염이 자란다 (삐뚤한 머리는 자라며 고르게) ──
            if (l.Style != HairStyle.Bald) l.HairCm += l.GrowCmDay * days;
            if (l.Uneven > 0f) l.Uneven = MathF.Max(0f, l.Uneven - 0.12f * days);
            if (l.Stubbly) l.BeardMm += 2.6f * days;
            if (l.Shaggy) { if (l.ShaggySince < 0) l.ShaggySince = w.Tick; }
            else l.ShaggySince = -1;
            // ── 쓴 것: 몸무게 · 식성 · 움직임 · 운동 ──
            bool jogging = c.Job?.Activity is JogActivity && c.IsMoving;
            bool exercising = l.FitSeen >= 0f && c.Fitness > l.FitSeen + 1e-5f || jogging;
            if (jogging) { c.Fitness = MathF.Min(1f, c.Fitness + 0.07f * hours); c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.04f * hours); }
            l.FitSeen = c.Fitness;
            float act = exercising ? 3.2f : c.Pose switch
            {
                Pose.Sleeping => 0.78f, Pose.Sitting => 0.95f, Pose.Walking => c.Job?.Urgent == true ? 1.7f : 1.2f, Pose.Working => 1.15f, Pose.Down => 0.7f, _ => 1.05f,
            };
            if (c.Outside) act = MathF.Max(act, 1.6f); // 선외 작업은 힘이 든다
            if (exercising) l.ExerciseTicks += (long)(hours * SimTime.TicksPerHour);
            float burn = BaseBurn * MathF.Pow(MathF.Max(0.5f, l.Kg / l.StartKg), 0.75f) * (0.5f + 0.5f * AppetiteOf(c)) * act * hours;
            l.Burn += burn;
            float eaten = l.Intake - l.LastIntakeStep;
            l.LastIntakeStep = l.Intake;
            if (!c.IsChild) l.Kg = Math.Clamp(l.Kg + KgPerFood * (eaten - burn), 38f, 140f);
            // ── 선외: 안 맞는 우주복 ──
            if (c.Outside && c.Suit is SuitState s && !c.Dead) Misfit(c, l, s, hours);
            // ── 불 곁의 긴 머리는 그을린다 ──
            if (c.Room != null && l.HairCm > 9f && w.Fire.CountIn(c.Room) > 0) Singe(c, l);
            // ── 아침 면도 · 수염 다듬기 (자기 선실 · 씻는 곳에서) ──
            if (l.Stubbly && c.IsAwake && c.CanAct && c.Job?.Urgent != true && c.Room != null && w.Tick - l.LastShave > SimTime.Hours(16)
                && c.Room.Type is RoomType.Quarters or RoomType.PrivateCabins or RoomType.QuietQuarters or RoomType.Laundry or RoomType.Decon or RoomType.Gym)
            {
                if (!l.KeepsBeard && l.BeardMm > 1.5f && (Hour(w) is >= 5f and < 11f || l.BeardMm > 8f)) { l.BeardMm = 0.3f; l.LastShave = w.Tick; Stats.Shaves++; }
                else if (l.KeepsBeard && l.BeardMm > l.BeardKeepMm * 1.6f) { l.BeardMm = l.BeardKeepMm; l.LastShave = w.Tick; Stats.Trims++; }
            }
            // ── 주 컴퓨터의 체중 측정 (자기 침대 · 의무실) ──
            if (w.Automation.Present && w.Automation.MainOnline && c.Room != null)
            {
                if (c.Room.Type is RoomType.Medbay or RoomType.Triage && w.Tick - l.KnownAt > SimTime.Hours(8)) Measure(c, l, "의무실 체중계");
                else if (c.Pose == Pose.Sleeping && c.Bed != null && c.Bed == c.HomeBed && w.Tick - l.KnownAt > SimTime.Hours(20)) Measure(c, l, "침대 압력 감지기");
            }
        }
        // 오래된 머리카락은 공기 정화기 · 로봇 청소가 걷어 간다 (이틀)
        if (Clips.Count > 0) Clips.RemoveAll(x => w.Tick - x.Tick > SimTime.Hours(48));
        if (Sessions.Count > 0) Sessions.RemoveAll(x => x.Done || x.Canceled || w.Tick - x.Start > SimTime.Hours(3));
        if (_asks.Count > 0)
            foreach (var k in _asks.Where(kv => w.Tick - kv.Value.tick > SimTime.Hours(20)).Select(kv => kv.Key).OrderBy(k => k).ToList()) _asks.Remove(k);
    }

    private static float Hour(World w) => SimTime.HourOfDay(w.Tick);

    private void NewDay()
    {
        foreach (var c in _w.Crew)
        {
            if (c.Dead) continue;
            var l = Of(c);
            l.LastIntake = l.Intake; l.LastBurn = l.Burn;
            l.Intake = 0f; l.Burn = 0f; l.LastIntakeStep = 0f;
        }
    }

    // ─────────────────────────────── 불 · 선외 ───────────────────────────────

    private void Singe(CrewMember c, BodyLook l)
    {
        var w = _w;
        bool tied = l.Style is HairStyle.Bun or HairStyle.Braid || l.Style == HairStyle.Ponytail && l.HairCm < 40f;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (!w.Fire.Fires.TryGetValue(new Cell(c.Cell.X + dx, c.Cell.Y + dy), out var v) || v < 0.2f) continue;
            if (!R.Chance(tied ? 0.08f : 0.3f)) return;
            float lost = MathF.Min(l.HairCm * 0.35f, 4f + 6f * R.Float());
            l.HairCm -= lost;
            l.Uneven = MathF.Min(1f, l.Uneven + 0.6f);
            l.LastCut = CutResult.Singed; l.CutAt = w.Tick; l.CutBy = -1;
            Stats.Singed++;
            w.Brain2.Emotions.Feel(c, Feeling.Fear, 0.12f, "불길에 머리카락이 그을렸다");
            w.Log.Add(w.Tick, LogKind.Warning, "불길에 머리카락 끝이 그을렸다 — 탄내가 난다", c.Id);
            MarkLog.Add(c.Memory.Marks, w.Tick, "불 곁에서 머리카락이 그을렸다");
            return;
        }
    }

    private void Misfit(CrewMember c, BodyLook l, SuitState s, float hours)
    {
        var w = _w;
        float m = l.Misfit;
        bool beard = l.BeardMm > 12f; // 수염이 목 고리 밀착을 떨어뜨린다
        if (MathF.Abs(m) < FitWarnKg && !beard) return;
        var wv = s.Wear;
        float k = MathF.Max(0f, MathF.Abs(m) - FitWarnKg + 1f) / 4f;
        if (!l.LeakBoosted && wv.BaseLeak > 0f)
        {
            // 꽉 끼면 목 고리가 덜 물리고, 헐렁하면 몸이 놀아 이음매가 벌어진다
            wv.BaseLeak *= 1f + (m >= FitWarnKg ? 0.25f * k : m <= -FitWarnKg ? 0.15f * k : 0f) + (beard ? 0.12f : 0f);
            l.LeakBoosted = true;
            Stats.MisfitEva++;
            wv.Notes.Add(m >= FitWarnKg ? "몸에 꽉 끼는 채로 밖에 나갔다" : m <= -FitWarnKg ? "헐렁한 채로 밖에 나갔다" : "수염이 목 고리에 끼었다");
        }
        if (m >= FitWarnKg)
        {
            // 꽉 끼는 관절이 긁힌다 (모르는 긁힘 — 점검에서 찾는다)
            wv.Scuff = MathF.Min(1f, wv.Scuff + 0.05f * k * hours);
            if (R.Chance(0.25f * hours)) { wv.Scratches++; Stats.MisfitScuffs++; }
        }
        else if (m <= -FitWarnKg) wv.Fuel = MathF.Max(0f, wv.Fuel - 0.03f * k * hours); // 헐렁해 몸이 놀아 자세를 자꾸 바로잡는다
        s.Oxygen = MathF.Max(0f, s.Oxygen - 0.08f * k * hours); // 숨이 가쁘다
        if (!l.EvaGrumbled && c.CanAct && MathF.Abs(m) >= FitWarnKg)
        {
            l.EvaGrumbled = true;
            w.EvaRisk.Say(c, Persona.Say(c, m > 0 ? "우주복이 꽉 껴서 팔이 잘 안 올라가…" : "우주복이 헐렁해서 몸이 자꾸 돈다"), RadioTone.Chat, c.Id, c.Position, c.Name);
            Stats.Grumbles++;
        }
    }

    // ─────────────────────────────── 우주복을 입을 때 ───────────────────────────────

    private void Donned(CrewMember c, BodyLook l)
    {
        var w = _w;
        // 우주복 원격 측정: 몸통 압력 · 목 고리 밀착으로 체중을 짐작한다
        if (w.Automation.Present && w.Automation.MainOnline) Measure(c, l, "우주복 원격 측정");
        float m = l.Misfit;
        if (MathF.Abs(m) < FitWarnKg) return;
        l.FitKnown = true;
        if (w.Tick - l.LastGrumble < SimTime.Hours(6)) return;
        l.LastGrumble = w.Tick;
        Stats.Grumbles++;
        bool grumbler = Life.Has(c, Habit.Grumbler) || Life.Has(c, Habit.ShortTempered);
        c.Say(w, Persona.Say(c, m > 0 ? (grumbler ? "누가 우주복을 줄여 놨어? …아, 내가 찐 거구나" : "우주복이 꽉 끼네…") : "우주복이 헐렁하다 — 살이 많이 빠졌나"));
        c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.03f);
        if (m > 0) w.Brain2.Emotions.Feel(c, grumbler ? Feeling.Anger : Feeling.Shame, 0.08f, "우주복이 꽉 낀다");
        w.Log.Add(w.Tick, LogKind.Life, m > 0 ? "우주복이 꽉 낀다 — 허리 고리가 겨우 잠긴다" : "우주복이 헐렁하다 — 어깨끈을 끝까지 조여도 남는다", c.Id);
    }

    // ─────────────────────────────── 주 컴퓨터 ───────────────────────────────

    /// <summary>컴퓨터가 체중을 잰다 (잰 것만 안다). 치수가 크게 어긋나면 바로 경고한다.</summary>
    public void Measure(CrewMember c, BodyLook l, string by)
    {
        var w = _w;
        l.KnownKg = MathF.Round(l.Kg, 1);
        l.KnownAt = w.Tick;
        l.KnownBy = by;
        int day = w.Day;
        if (l.Measured.Count > 0 && l.Measured[^1].day == day) l.Measured[^1] = (day, l.KnownKg);
        else { l.Measured.Add((day, l.KnownKg)); if (l.Measured.Count > 14) l.Measured.RemoveAt(0); }
        Stats.Measures++;
        float m = l.KnownKg - l.SuitKg;
        if (MathF.Abs(m) >= FitWarnKg && (Life.HasQual(c, Qual.Eva) || c.EvaHours > 0f || c.Suit != null)) WarnFit(c, l, by);
    }

    private void WarnFit(CrewMember c, BodyLook l, string by)
    {
        var w = _w;
        var au = w.Automation;
        float m = l.KnownKg - l.SuitKg;
        var room = w.Ship.FurnitureOf(FurnitureType.SuitLocker).Select(f => f.Room).FirstOrDefault();
        int before = Stats.MisfitScuffs;
        var act = au.Book.Add(ActKind.Advice, room,
            $"{c.Name} 우주복 치수 불일치 — 잰 체중 {l.KnownKg:0.0}kg · 맞춘 치수 {l.SuitKg:0.0}kg ({m:+0.0;-0.0}kg · {by})",
            m > 0 ? "예측: 선외에서 꽉 낀 관절이 긁히고 목 고리가 덜 물려 샌다 · 숨이 가빠 산소를 더 쓴다" : "예측: 헐렁해 몸이 놀고 이음매가 벌어진다 · 자세를 바로잡느라 추진제를 더 쓴다",
            "조치: 우주복 치수 조정 작업을 올렸다", $"요청: {c.Name}은(는) 다음 선외 작업 전에 보관함에서 치수를 맞출 것",
            $"fit:{c.Id}", SimTime.Hours(20), 60f * 30f,
            (world, a) =>
            {
                var cl = world.Body2.Peek(c);
                if (cl == null) return null;
                if (MathF.Abs(cl.Misfit) < 3f) return (1, "치수를 맞췄다");
                if (world.Body2.Stats.MisfitScuffs > before) return (1, "경고대로 선외에서 우주복이 상했다");
                return (2, "아직 맞추지 않았다");
            });
        if (act == null) return;
        Stats.FitWarnings++;
        l.FitOrder = true;
        l.FitKnown = true;
        if (c.CanAct && c.Room != null) c.NextThinkTick = Math.Min(c.NextThinkTick, w.Tick + 1);
    }

    /// <summary>하루 한 번 (08시): 잰 체중의 추세 · 치수 불일치.</summary>
    private void ComputerDaily()
    {
        var w = _w;
        var au = w.Automation;
        if (!au.Present || !au.MainOnline) return;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.IsChild) continue;
            var l = Of(c);
            if (w.Tick - l.LastTrend < SimTime.Hours(20) || l.Measured.Count < 2) continue;
            var (d0, k0) = l.Measured.FirstOrDefault(x => x.day >= l.Measured[^1].day - 7);
            var (d1, k1) = l.Measured[^1];
            int span = d1 - d0;
            float delta = k1 - k0;
            bool thin = l.KnownKg > 0f && l.KnownKg / (l.HeightCm * l.HeightCm / 10000f) < 18.5f;
            if (span < 3 || MathF.Abs(delta) < 3f && !thin) continue;
            l.LastTrend = w.Tick;
            bool up = delta > 0f;
            float startTrend = k1;
            var act = au.Book.Add(ActKind.Advice, null,
                $"{c.Name} 체중 {delta:+0.0;-0.0}kg / {span}일 ({k0:0.0} → {k1:0.0}kg · {l.KnownBy})",
                up ? "판단: 먹는 양이 쓰는 양보다 많다 · 운동 부족 추세 — 이대로면 우주복 치수가 어긋난다" : thin ? "판단: 영양 부족 — 체력 · 회복이 떨어진다" : "판단: 먹는 양이 모자라다 (배급 · 과로) 추세",
                up ? "조치: 운동 권고를 보냈다" : "조치: 식사를 챙기라고 알렸다",
                up ? $"요청: {c.Name}은(는) 땀방 · 달리기로 몸을 움직일 것" : $"요청: {c.Name}은(는) 끼니를 거르지 말 것",
                $"trend:{c.Id}", SimTime.Hours(70), 60f * 72f,
                (world, a) =>
                {
                    var cl = world.Body2.Peek(c);
                    if (cl == null) return null;
                    return up ? (cl.Kg < startTrend - 0.5f ? 1 : 2, cl.Kg < startTrend - 0.5f ? "체중이 돌아섰다" : "그대로다")
                              : (cl.Kg > startTrend + 0.5f ? 1 : 2, cl.Kg > startTrend + 0.5f ? "체중이 회복되고 있다" : "그대로다");
                });
            if (act == null) continue;
            Stats.TrendWarnings++;
            // 듣는 사람: 컴퓨터를 믿으면 목표로 삼고, 아니면 흘려듣고 투덜댄다
            float trust = au.Trusts.Of(c);
            if (trust >= 0.45f)
            {
                Stats.Heeded++;
                if (up) w.Brain2.Goals.Push(c, "body:trim", "몸을 좀 움직이자", "주 컴퓨터가 몸무게가 늘고 있다고 알렸다", ActCat.Hobby, 72f, 1.2f);
                Life.Diary(w, c, Persona.Say(c, up ? "컴퓨터가 체중이 늘었다고 한다. 좀 뛰어야겠다" : "컴퓨터가 끼니를 챙기라고 한다"));
            }
            else
            {
                Stats.Shrugged++;
                Stats.Grumbles++;
                c.Say(w, Persona.Say(c, "컴퓨터가 내 몸무게까지 참견이네"));
                w.Brain2.Emotions.Feel(c, Feeling.Anger, 0.05f, "주 컴퓨터가 몸무게를 참견했다");
            }
        }
        // 치수 불일치는 매일 다시 본다 (선외 자격이 있는 사람)
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.IsChild) continue;
            var l = Of(c);
            if (l.KnownKg > 0f && MathF.Abs(l.KnownKg - l.SuitKg) >= FitWarnKg && Life.HasQual(c, Qual.Eva)) WarnFit(c, l, l.KnownBy);
        }
    }

    // ─────────────────────────────── 알아채기 ───────────────────────────────

    /// <summary>같은 방에 있는 사람끼리 서로 본다 — 지난번에 본 모습과 견주어 알아채고 말한다.</summary>
    private void LookAround()
    {
        var w = _w;
        var byRoom = new Dictionary<int, List<CrewMember>>();
        foreach (var c in w.Crew)
        {
            if (!c.IsAwake || !c.CanAct || c.Outside || c.Room == null || c.Room.Dark) continue;
            if (!byRoom.TryGetValue(c.Room.Id, out var list)) byRoom[c.Room.Id] = list = new List<CrewMember>(4);
            list.Add(c);
        }
        foreach (var rid in byRoom.Keys.OrderBy(k => k).ToList())
        {
            var list = byRoom[rid];
            if (list.Count < 2) continue;
            foreach (var o in list)
                foreach (var s in list)
                    if (o != s) Notice(o, s);
        }
    }

    /// <summary>{o}가 {s}를 본다 (시험에서도 부른다).</summary>
    public void Notice(CrewMember o, CrewMember s)
    {
        var w = _w;
        var l = Of(s);
        long key = Pair(o.Id, s.Id);
        if (!_seen.TryGetValue(key, out var imp))
        {
            _seen[key] = new LookImpression { HairCm = l.HairCm, CutAt = l.CutAt, Uneven = l.Uneven, Kg = l.Kg, Tick = w.Tick };
            return;
        }
        bool newCut = l.CutAt > imp.CutAt && l.LastCut != CutResult.None;
        float dKg = l.Kg - imp.Kg;
        bool free = o.Job?.Urgent != true && w.Tick - imp.Said > SimTime.Hours(6) && !o.IsChild || o.IsChild && R.Chance(0.3f);
        if (free && newCut) HairRemark(o, s, l, imp);
        else if (free && MathF.Abs(dKg) >= 3f && w.Tick - imp.Tick > SimTime.Hours(20)) WeightRemark(o, s, l, dKg, imp);
        else if (free && l.Shaggy) Asks(s, o); // 덥수룩한 사람이 솜씨 좋다고 믿는 사람에게 부탁한다
        imp.HairCm = l.HairCm; imp.CutAt = l.CutAt; imp.Uneven = l.Uneven; imp.Kg = l.Kg; imp.Tick = w.Tick;
    }

    private void HairRemark(CrewMember o, CrewMember s, BodyLook l, LookImpression imp)
    {
        var w = _w;
        float aff = o.AffinityTo(s);
        float p = 0.35f + 0.45f * o.Traits.Sociability + (Life.Has(o, Habit.Talker) || Life.Has(o, Habit.Joker) ? 0.2f : 0f);
        if (!R.Chance(p)) return;
        imp.Said = w.Tick;
        Stats.HairComments++;
        var barber = Crew(l.CutBy);
        bool teaser = Life.Has(o, Habit.Joker) || Life.Has(o, Habit.Prankster) || aff < -0.15f;
        switch (l.LastCut)
        {
            case CutResult.Neat:
                o.Say(w, Persona.Say(o, barber != null && barber != o ? $"머리 잘랐네? 잘 어울려 — {Ko.IGa(barber.Name)} 잘랐어?" : "머리 잘랐네? 잘 어울려"));
                w.Brain2.Emotions.Feel(s, Feeling.Joy, 0.1f, $"{Ko.IGa(o.Name)} 머리가 잘 어울린다고 했다", o);
                s.ChangeAffinity(o, 0.02f);
                Stats.Compliments++;
                break;
            case CutResult.Botched:
            case CutResult.Singed:
                if (teaser)
                {
                    o.Say(w, Persona.Say(o, l.LastCut == CutResult.Singed ? "머리에서 탄내 나 — 불 끄다 그랬어?" : barber != null ? $"누가 그랬어? …{Ko.EuRo(barber.Name)}구나" : "머리가 왜 그래?"));
                    w.Brain2.Emotions.Feel(s, Feeling.Shame, 0.14f, $"{Ko.IGa(o.Name)} 삐뚤빼뚤한 머리를 놀렸다", o);
                    s.ChangeAffinity(o, -0.04f);
                    Stats.Teases++;
                }
                else
                {
                    o.Say(w, Persona.Say(o, "괜찮아, 금방 자랄 거야"));
                    w.Brain2.Emotions.Feel(s, Feeling.Joy, 0.05f, $"{Ko.IGa(o.Name)} 머리 괜찮다고 다독였다", o);
                    s.ChangeAffinity(o, 0.03f);
                }
                break;
            default:
                o.Say(w, Persona.Say(o, "머리 다듬었구나"));
                break;
        }
        // 누가 잘랐는지 들었다 → 그 사람 솜씨를 이렇게 믿는다
        if (barber != null && barber != o)
        {
            float q = l.LastCut switch { CutResult.Neat => 0.85f, CutResult.Fair => 0.55f, CutResult.Botched => 0.15f, _ => -1f };
            if (q >= 0f) { long rk = Pair(o.Id, barber.Id); _rep[rk] = _rep.TryGetValue(rk, out var old) ? 0.5f * (old + q) : q; Stats.Reputation++; }
        }
    }

    private void WeightRemark(CrewMember o, CrewMember s, BodyLook l, float dKg, LookImpression imp)
    {
        var w = _w;
        float aff = o.AffinityTo(s);
        bool blunt = Life.Has(o, Habit.Joker) || Life.Has(o, Habit.Talker) || Life.Has(o, Habit.Prankster) || aff < -0.1f;
        bool kind = o.Value == CrewValue.People || Life.Has(o, Habit.Cheerful) || Life.Has(o, Habit.Optimist) || aff > 0.35f;
        float p = 0.2f + 0.3f * o.Traits.Sociability + (blunt ? 0.25f : 0f);
        if (!R.Chance(p)) return;
        imp.Said = w.Tick;
        Stats.WeightComments++;
        bool trained = l.ExerciseTicks > SimTime.Hours(2);
        if (dKg > 0f)
        {
            if (blunt && !kind)
            {
                o.Say(w, Persona.Say(o, "살 좀 붙었네? 우주복 들어가겠어?"));
                w.Brain2.Emotions.Feel(s, Feeling.Shame, 0.15f, $"{Ko.IGa(o.Name)} 살쪘다고 했다", o);
                s.ChangeAffinity(o, -0.04f);
                Stats.Teases++;
                // 부끄러우면 몸을 움직이기로 한다 (성실할수록)
                if (s.Traits.Diligence > 0.35f || Life.Has(s, Habit.GymRat))
                    w.Brain2.Goals.Push(s, "body:trim", "몸을 좀 움직이자", $"{Ko.IGa(o.Name)} 살쪘다고 했다", ActCat.Hobby, 48f, 1f);
            }
            else
            {
                o.Say(w, Persona.Say(o, "요즘 잘 먹나 봐 — 얼굴 좋아 보여"));
                w.Brain2.Emotions.Feel(s, Feeling.Joy, 0.05f, $"{Ko.IGa(o.Name)} 얼굴 좋아 보인다고 했다", o);
                s.ChangeAffinity(o, 0.02f);
                Stats.Compliments++;
            }
        }
        else if (trained)
        {
            o.Say(w, Persona.Say(o, "운동한 티가 나네"));
            w.Brain2.Emotions.Feel(s, Feeling.Pride, 0.12f, $"{Ko.IGa(o.Name)} 운동한 티가 난다고 했다", o);
            s.ChangeAffinity(o, 0.03f);
            Stats.Compliments++;
        }
        else
        {
            // 굶어서 빠졌다: 걱정한다 (가까운 사람은 끼니를 챙기게 한다)
            o.Say(w, Persona.Say(o, "얼굴이 반쪽이 됐어 — 밥은 먹고 다녀?"));
            w.Brain2.Emotions.Feel(o, Feeling.Fear, 0.05f, $"{s.Name}이(가) 많이 말랐다", s);
            s.ChangeAffinity(o, 0.03f);
            s.Needs.Food = MathF.Max(0f, s.Needs.Food - 0.05f); // 들으니 배가 고프다 (끼니를 당긴다)
            Stats.Worries++;
        }
    }

    /// <summary>{s}(덥수룩)가 {o}에게 잘라 달라고 부탁한다 — 솜씨를 믿고 사이가 나쁘지 않을 때.</summary>
    private bool Asks(CrewMember s, CrewMember o)
    {
        var w = _w;
        var l = Of(s);
        if (o.IsChild || s.IsChild || _asks.ContainsKey(s.Id) || Sessions.Any(x => x.Client == s.Id)) return false;
        if (l.ShaggySince < 0 || w.Tick - l.ShaggySince < SimTime.Hours(Life.Has(s, Habit.NeatFreak) ? 4 : 30)) return false;
        float rep = Rep(s, o);
        if (rep < 0f) rep = o.Id == s.Id ? 0f : 0.35f; // 모르면 반쯤 믿는다
        if (rep < 0.4f || s.AffinityTo(o) < 0f || Botched(s, o)) return false;
        // 지금 여기 있는 사람 중 가장 믿는 사람에게만
        if (!R.Chance(0.5f)) return false;
        _asks[s.Id] = (o.Id, w.Tick);
        Stats.Requests++;
        s.Say(w, Persona.Say(s, $"{o.Name}, 머리 좀 잘라 줄래? 너무 덥수룩해"));
        o.NextThinkTick = Math.Min(o.NextThinkTick, w.Tick + 1);
        return true;
    }

    public bool AskedBy(CrewMember barber, out CrewMember? client)
    {
        client = null;
        foreach (var (cid, v) in _asks.OrderBy(kv => kv.Key))
            if (v.barber == barber.Id) { client = Crew(cid); if (client != null) return true; }
        return false;
    }

    public bool Botched(CrewMember client, CrewMember barber) =>
        _w.Relations.Of(client, barber).Any(m => m.Reason == RelationReason.BotchedMyHair && _w.Tick - m.Tick < SimTime.TicksPerDay * 20);

    public bool RefusedRecently(CrewMember barber, CrewMember client) => _refused.TryGetValue(Pair(barber.Id, client.Id), out var t) && _w.Tick - t < SimTime.Hours(30);

    // ─────────────────────────────── 이발 ───────────────────────────────

    public HairSession? SessionOf(CrewMember c) => Sessions.FirstOrDefault(x => !x.Done && !x.Canceled && (x.Barber == c.Id || x.Client == c.Id));

    public HairSession Begin(CrewMember barber, CrewMember client, Cell seat, Furniture? f, bool self)
    {
        var s = new HairSession { Id = _nextSession++, Barber = barber.Id, Client = client.Id, Seat = seat, SeatFurniture = f?.Id ?? -1, Start = _w.Tick, Self = self };
        Sessions.Add(s);
        _asks.Remove(client.Id);
        return s;
    }

    /// <summary>손님이 거절한다: 지난번에 망쳐 놨다 (기억) · 솜씨를 못 믿는다 (본 것 · 들은 것).</summary>
    public bool Refuses(CrewMember barber, CrewMember client, out string why)
    {
        why = "";
        if (_asks.TryGetValue(client.Id, out var a) && a.barber == barber.Id) return false; // 먼저 부탁했다
        if (Botched(client, barber)) why = "됐어 — 지난번 그 머리 아직 기억해";
        else if (Rep(client, barber) is >= 0f and < 0.3f) why = "고마운데… 다른 사람한테 부탁할게";
        else if (client.AffinityTo(barber) < -0.2f) why = "괜찮아";
        if (why.Length == 0) return false;
        var w = _w;
        _refused[Pair(barber.Id, client.Id)] = w.Tick;
        Stats.Refusals++;
        client.Say(w, Persona.Say(client, why));
        w.Brain2.Emotions.Feel(barber, Feeling.Shame, 0.08f, $"{Ko.IGa(client.Name)} 머리를 맡기지 않았다", client);
        MarkLog.Add(barber.Memory.Marks, w.Tick, $"{Ko.IGa(client.Name)} 머리를 맡기지 않았다");
        return true;
    }

    /// <summary>자르는 손의 상태: 솜씨 · 성미 · 다친 손 · 피로 · 감정 · 어두움.</summary>
    public float Quality(CrewMember barber, CrewMember client, bool self)
    {
        var bl = Of(barber);
        var w = _w;
        float q = 0.22f + 0.62f * bl.Barber;
        if (Life.Has(barber, Habit.Perfectionist) || Life.Has(barber, Habit.Methodical)) q += 0.08f;
        if (Life.Has(barber, Habit.Hasty)) q -= 0.12f;
        q -= 0.55f * (1f - Wounds.HandFactor(barber.Vitals));
        if (barber.Needs.Rest < 0.25f) q -= 0.12f;
        var emo = w.Brain2.Emotions;
        q -= 0.15f * MathF.Max(emo.Get(barber, Feeling.Anger), emo.Get(barber, Feeling.Fear));
        if (barber.Room?.Dark == true) q -= 0.12f;
        if (Life.Has(client, Habit.Fidgety)) q -= 0.08f;
        if (self) q -= 0.22f; // 거울 보며 뒷머리는 못 자른다
        return q + R.Range(-0.12f, 0.12f);
    }

    public CutResult Finish(HairSession ses, CrewMember barber, CrewMember client)
    {
        var w = _w;
        ses.Done = true;
        var l = Of(client);
        var bl = Of(barber);
        float q = Quality(barber, client, ses.Self);
        var res = q >= 0.6f ? CutResult.Neat : q >= 0.36f ? CutResult.Fair : CutResult.Botched;
        float before = l.HairCm;
        l.HairCm = res switch { CutResult.Neat => l.StyleCm, CutResult.Fair => l.StyleCm * 0.85f, _ => l.StyleCm * 0.6f };
        l.HairCm = MathF.Min(l.HairCm, before);
        l.Uneven = res switch { CutResult.Neat => 0f, CutResult.Fair => 0.25f, _ => 0.85f };
        l.LastCut = res; l.CutAt = w.Tick; l.CutBy = barber.Id; l.ShaggySince = -1; l.Cuts++;
        if (l.KeepsBeard && l.BeardMm > l.BeardKeepMm) l.BeardMm = l.BeardKeepMm; // 수염도 다듬어 준다
        bl.CutsGiven++;
        bl.Barber = MathF.Min(0.95f, bl.Barber + 0.06f * (1f - bl.Barber)); // 자를수록 는다
        Stats.Cuts++;
        if (ses.Self) Stats.SelfCuts++;
        if (res == CutResult.Neat) Stats.Neat++; else if (res == CutResult.Fair) Stats.Fair++; else Stats.Botched++;
        // 바닥에 떨어진 머리카락 · 옷에 붙은 머리카락 (빨래)
        float cm = before - l.HairCm;
        if (cm > 0.5f)
        {
            var room = w.Ship.RoomAt(ses.Seat);
            Clips.Add(new HairClip { Id = _nextClip++, Cell = ses.Seat, RoomId = room?.Id ?? -1, Tick = w.Tick, Cm = cm, Color = l.HairColor, Barber = barber.Id, Client = client.Id });
            Stats.Clips++;
            client.Soil.Clothes[(int)SoilKind.Dust] = MathF.Min(1f, client.Soil.Clothes[(int)SoilKind.Dust] + 0.12f + 0.02f * cm);
            Stats.ClothesHair++;
        }
        if (ses.Self)
        {
            client.Say(w, Persona.Say(client, res == CutResult.Botched ? "뒷머리가… 거울로는 안 보인다" : "혼자 잘랐는데 그럭저럭"));
            if (res == CutResult.Botched) w.Brain2.Emotions.Feel(client, Feeling.Shame, 0.1f, "혼자 자른 머리가 삐뚤빼뚤하다");
            Life.Diary(w, client, Persona.Say(client, "아무도 안 잘라 줘서 혼자 머리를 잘랐다."));
            w.Log.Add(w.Tick, LogKind.Life, res == CutResult.Botched ? "거울 보며 혼자 머리를 잘랐다 — 뒷머리가 삐뚤빼뚤하다" : "거울 보며 혼자 머리를 잘랐다", client.Id);
            return res;
        }
        var rel = w.Relations;
        switch (res)
        {
            case CutResult.Neat:
                w.Brain2.Emotions.Feel(client, Feeling.Joy, 0.22f, $"{Ko.IGa(barber.Name)} 머리를 말끔하게 잘라 줬다", barber);
                w.Brain2.Emotions.Feel(barber, Feeling.Pride, 0.15f, $"{client.Name}의 머리를 잘 잘랐다", client);
                client.ChangeAffinity(barber, 0.07f); barber.ChangeAffinity(client, 0.04f);
                rel.Remember(client, barber, RelationReason.CutMyHair, $"{Ko.IGa(barber.Name)} 머리를 말끔하게 잘라 줬다");
                client.Say(w, Persona.Say(client, "와, 깔끔하다. 고마워"));
                Life.Diary(w, client, Persona.Say(client, $"{Ko.IGa(barber.Name)} 머리를 잘라 줬다. 거울 보니 기분이 좋다."));
                break;
            case CutResult.Fair:
                w.Brain2.Emotions.Feel(client, Feeling.Joy, 0.06f, $"{Ko.IGa(barber.Name)} 머리를 잘라 줬다", barber);
                client.ChangeAffinity(barber, 0.03f); barber.ChangeAffinity(client, 0.02f);
                client.Say(w, Persona.Say(client, "음, 이 정도면 됐어"));
                break;
            default:
                bool hot = Life.Has(client, Habit.ShortTempered) || client.Traits.Calm < 0.3f;
                w.Brain2.Emotions.Feel(client, hot ? Feeling.Anger : Feeling.Shame, 0.25f, $"{Ko.IGa(barber.Name)} 머리를 삐뚤빼뚤하게 잘라 놨다", barber);
                w.Brain2.Emotions.Feel(barber, Feeling.Shame, 0.18f, $"{client.Name}의 머리를 망쳤다", client);
                client.ChangeAffinity(barber, -0.08f);
                rel.Remember(client, barber, RelationReason.BotchedMyHair, $"{Ko.IGa(barber.Name)} 머리를 삐뚤빼뚤하게 잘라 놨다");
                client.Say(w, Persona.Say(client, hot ? "이게 뭐야! 한쪽이 짧잖아" : "…아, 괜찮아. 자라겠지"));
                barber.Say(w, Persona.Say(barber, "미안… 가위가 미끄러졌어"));
                MarkLog.Add(client.Memory.Marks, w.Tick, $"{Ko.IGa(barber.Name)} 머리를 삐뚤빼뚤하게 잘라 놨다");
                Life.Diary(w, client, Persona.Say(client, $"{barber.Name}에게 머리를 맡겼다가 망했다. 한동안 모자라도 쓰고 싶다."));
                break;
        }
        // 그 자리에서 본 사람은 솜씨를 안다 (손님 · 곁의 사람)
        float qq = res switch { CutResult.Neat => 0.85f, CutResult.Fair => 0.55f, _ => 0.15f };
        foreach (var o in w.Crew)
        {
            if (o == barber || o.Dead || !o.IsAwake || o.Room == null || o.Room != client.Room && o != client) continue;
            long rk = Pair(o.Id, barber.Id);
            _rep[rk] = _rep.TryGetValue(rk, out var old) ? 0.5f * (old + qq) : qq;
            if (o != client && _seen.TryGetValue(Pair(o.Id, client.Id), out var imp)) { imp.HairCm = l.HairCm; imp.CutAt = l.CutAt; imp.Uneven = l.Uneven; }
        }
        if (_seen.TryGetValue(Pair(barber.Id, client.Id), out var bi)) { bi.HairCm = l.HairCm; bi.CutAt = l.CutAt; bi.Uneven = l.Uneven; }
        string how = res switch { CutResult.Neat => "말끔하게", CutResult.Fair => "그럭저럭", _ => "한쪽이 짧게 삐뚤빼뚤" };
        w.Log.Add(w.Tick, LogKind.Life, $"덥수룩하던 {client.Name}의 머리를 {how} 잘라 줬다", barber.Id);
        if (l.Cuts == 1 && bl.CutsGiven <= 2)
            w.History.Add(w, HistoryKind.Bond, $"{Ko.IGa(barber.Name)} {client.Name}의 머리를 처음 {how} 잘라 줬다", client.Room, new[] { barber, client });
        return res;
    }

    public static string Name(CutResult r) => r switch
    {
        CutResult.Neat => "말끔", CutResult.Fair => "그럭저럭", CutResult.Botched => "삐뚤빼뚤", CutResult.Singed => "그을림", _ => "—",
    };

    public static string StyleName(HairStyle s) => s switch
    {
        HairStyle.Buzz => "삭발에 가까운 짧은 머리", HairStyle.Crop => "짧은 머리", HairStyle.Side => "옆 가르마", HairStyle.Curly => "곱슬머리", HairStyle.Bob => "단발",
        HairStyle.Long => "긴 생머리", HairStyle.Ponytail => "말총머리", HairStyle.Bun => "올린 머리", HairStyle.Braid => "땋은 머리", _ => "민머리",
    };

    public void Cancel(HairSession? s) { if (s != null && !s.Done) s.Canceled = true; }

    /// <summary>카드 한 줄: 몸무게 · 우주복 · 머리 · 수염 (그 배 사람이 하는 말로).</summary>
    public string? Line(CrewMember c)
    {
        if (Peek(c) is not BodyLook l) return null;
        float d = l.Kg - l.StartKg;
        var sb = new System.Text.StringBuilder($"몸무게 {l.Kg:0}kg");
        if (MathF.Abs(d) >= 1f) sb.Append($" (출항 때보다 {d:+0;-0}kg)");
        if (l.Tight) sb.Append(" · 우주복이 꽉 낀다");
        else if (l.Loose) sb.Append(" · 우주복이 헐렁하다");
        if (l.Shaggy) sb.Append(" · 머리가 덥수룩하다");
        else if (l.Uneven > 0.5f) sb.Append(" · 머리가 삐뚤빼뚤하다");
        else if (l.CutAt >= 0 && _w.Tick - l.CutAt < SimTime.TicksPerDay * 2 && Crew(l.CutBy) is CrewMember b && b != c) sb.Append($" · {Ko.IGa(b.Name)} 머리를 잘라 줬다");
        if (l.KeepsBeard && l.BeardMm > 6f) sb.Append(" · 수염을 기른다");
        else if (!l.KeepsBeard && l.BeardMm > 4f) sb.Append(" · 수염이 거뭇하다");
        return sb.ToString();
    }

    public void Swept(HairClip clip, CrewMember who)
    {
        var w = _w;
        clip.Sweeper = who.Id;
        Clips.Remove(clip);
        Stats.Sweeps++;
        // 남이 자르고 안 치운 걸 치웠다: 깔끔한 사람은 투덜댄다
        if (who.Id != clip.Barber && Crew(clip.Barber) is CrewMember b && Life.Has(who, Habit.NeatFreak))
        {
            who.ChangeAffinity(b, -0.03f);
            who.Say(w, Persona.Say(who, $"자른 머리카락은 좀 치우지 — {b.Name}"));
            Stats.Grumbles++;
        }
    }

    public void Adjusted(CrewMember c)
    {
        var w = _w;
        var l = Of(c);
        float m = l.Misfit;
        l.SuitKg = MathF.Round(l.Kg, 1);
        l.FitOrder = false;
        l.FitKnown = false;
        Stats.FitAdjusts++;
        w.Log.Add(w.Tick, LogKind.Work, m > 0 ? "우주복이 끼어서 허리 고리와 어깨끈을 늘렸다" : "우주복이 헐렁해서 어깨끈과 몸통 고리를 줄였다", c.Id);
        if (w.Automation.Present && w.Automation.MainOnline) l.KnownKg = l.SuitKg;
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        foreach (var id in _looks.Keys.OrderBy(k => k))
        {
            var l = _looks[id];
            F(l.HairCm); F(l.BeardMm); F(l.Kg); F(l.SuitKg); F(l.Barber); F(l.Uneven); I(l.Cuts); I(l.CutBy); I(l.FitOrder ? 1 : 0); F(l.KnownKg);
        }
        I(Clips.Count); I(Sessions.Count); I(_seen.Count); I(_rep.Count);
        var s = Stats;
        I(s.Cuts); I(s.Refusals); I(s.HairComments); I(s.WeightComments); I(s.FitWarnings); I(s.TrendWarnings); I(s.FitAdjusts); I(s.Sweeps); I(s.Singed); I(s.Shaves);
    }
}
