using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v17.5 꿈 · 잠버릇 · 아침 식탁 · 장소의 기억 · 혼자 먹다 돌아오기 · 떠난 사람의 자리.
//  꿈거리(인상): 겪은 사고(규모 · 그 방에 있었나 · 직접 봤나 — 두뇌 2.0의 '본 것' 믿음) · 잃은 사람(가까웠던 만큼) — 며칠에 걸쳐 옅어진다.
//  잠들 때 굴린다: 인상 × 성격(용기 · 걱정 · 낙천) × 긴장(Memory.Trauma) × 지금 감정(두려움 · 슬픔) × 잠자리(젖은 침구 · 맨 매트리스 · 남의 자리 · 소음 · 소품).
//   악몽: 뒤척이고 한두 번 깬다 (옆에서 자던 사람도 깨기도) · 덜 쉰다 · 그 자리의 기억이 다시 짙어진다.
//   그리움: 떠난 사람이 늘 앉던 자리에 앉아 있는 꿈 — 아침에 슬프다. 고향 꿈 — 조금 기쁘다.
//  아침: 감정 · 일기 · 아침 식탁에서 꿈 이야기 (같은 꿈을 꾼 사람끼리 가까워지고 · 위로받으면 두려움이 풀리고 · 짓궂은 사람은 놀린다 · 숫기 없는 사람은 혼자 삭인다).
//  주컴퓨터: 침대 감지기의 수면 기록을 읽어 사흘 사이 자주 깨는 사람의 아침 근무를 늦추자고 한다 (믿는 만큼 받아들인다).
//  장소의 기억: 쓰러진 자리 · 큰 사고 자리를 사람마다 다른 무게로 피한다 (길 고르기 비용) — 용기 · 가까움 · 본 것 · 긴장이 정하고 날이 갈수록 옅어진다.

public enum DreamKind : byte { Fire, Water, Dark, Breach, Loss, Fall, Alarm, Home }

/// <summary>꿈거리 하나 (겪은 일의 인상).</summary>
public sealed class Impression
{
    public string Key { get; init; } = "";
    public long Tick { get; set; }
    public DreamKind Kind { get; init; }
    public float Weight { get; set; }
    public string Text { get; init; } = "";
    public int About { get; init; } = -1;
    public int Room { get; init; } = -1;
}

/// <summary>하룻밤의 꿈.</summary>
public sealed class Dream
{
    public int Crew { get; init; }
    public long Tick { get; init; }
    public DreamKind Kind { get; init; }
    public bool Nightmare { get; init; }
    public bool Grief { get; init; }
    public float Strength { get; init; }
    public string Text { get; init; } = "";
    public string Source { get; init; } = "";
    public int About { get; init; } = -1;
    public int Room { get; init; } = -1;
    public int Wakes { get; set; }
    public long Woke { get; set; } = -1;
    public bool Told { get; set; }
    public List<int> WokeOthers { get; } = new();
    /// <summary>깬 직후의 기분 (두려움 + 슬픔 − 기쁨).</summary>
    public float Mood { get; set; }
}

/// <summary>한 사람 안에 남은 사고 뒤 며칠.</summary>
public sealed class AfterMind
{
    public int Id { get; init; }
    public float Withdraw { get; set; }
    public string? WithdrawWhy { get; set; }
    public long WithdrawFrom { get; set; } = -1;
    public int AteStage { get; set; }
    public int AwayRoom { get; set; } = -1;
    public int AwayFrom { get; set; } = -1;
    public string? AwayWhy { get; set; }
    public int AwayMeals { get; set; }
    public long AwayLast { get; set; } = -1;
    public long AwayFirst { get; set; } = -1;
    public SortedDictionary<int, int> SeatUse { get; } = new();
    public int LastSeat { get; set; } = -1;
    internal Job? LastEatJob;
    public List<Impression> Seen { get; } = new();
    public bool Sleeping { get; set; }
    public long SleepStart { get; set; } = -1;
    public Dream? Night { get; set; }
    public Dream? Last { get; set; }
    public List<long> WakeTicks { get; } = new();
    public long NextWake { get; set; } = -1;
    public int WakesLeft { get; set; }
    public long SatUp { get; set; } = -1;
    public int Dreams { get; set; }
    public int Nightmares { get; set; }
    public long PausedDay { get; set; } = -1;
    public long AdvisedSleep { get; set; } = -1;
    public bool WetNoted { get; set; }
    /// <summary>화면: 안고 가는 침구 (침대 번호).</summary>
    public int CarryBundle { get; set; } = -1;
    /// <summary>혼자 먹기: 2 선실 · 1 식당 구석 · 0 늘 앉던 자리.</summary>
    public int Stage => Withdraw >= 0.6f ? 2 : Withdraw >= 0.3f ? 1 : 0;
}

/// <summary>장소의 기억: 그 자리를 사람마다 다른 무게로 피한다.</summary>
public sealed class PlaceMemory
{
    public int Id { get; init; }
    public Cell At { get; init; }
    public int Room { get; init; }
    public long Tick { get; init; }
    public byte Kind { get; init; } // 0 사람이 쓰러진 자리 · 1 큰 사고 자리
    public string Text { get; init; } = "";
    public int Crew { get; init; } = -1;
    public SortedDictionary<int, float> Weight { get; } = new();
    public SortedDictionary<int, long> Since { get; } = new();
    public int FlowerBy { get; set; } = -1;
    public long FlowerAt { get; set; } = -1;
}

public sealed partial class AftermathSystem
{
    public List<PlaceMemory> Places { get; } = new();
    private int _majorTraces;
    private readonly SortedSet<int> _caseSeen = new();

    // ───────────────────────────── 인상 (꿈거리) ─────────────────────────────

    internal void Impress(CrewMember c, DreamKind kind, float weight, string text, int about, int room, string key)
    {
        if (c.Dead || weight <= 0.02f) return;
        var m = Mind(c);
        foreach (var im in m.Seen)
            if (im.Key == key) { if (weight > im.Weight) { im.Weight = weight; im.Tick = _w.Tick; } return; }
        m.Seen.Add(new Impression { Key = key, Tick = _w.Tick, Kind = kind, Weight = weight, Text = text, About = about, Room = room });
        m.Seen.RemoveAll(x => _w.Tick - x.Tick > SimTime.TicksPerDay * 7);
        if (m.Seen.Count > 8) m.Seen.Remove(m.Seen.OrderBy(x => x.Weight).ThenBy(x => x.Tick).First());
    }

    private static DreamKind KindOf(ScaleCase k)
    {
        if (k.KindsSeen.Contains(CauseKind.Fire) || k.KindsSeen.Contains(CauseKind.Explosion)) return DreamKind.Fire;
        if (k.KindsSeen.Contains(CauseKind.Flood)) return DreamKind.Water;
        if (k.KindsSeen.Contains(CauseKind.Breach) || k.KindsSeen.Contains(CauseKind.Detach) || k.KindsSeen.Contains(CauseKind.Impact)) return DreamKind.Breach;
        if (k.KindsSeen.Contains(CauseKind.Casualty) || k.KindsSeen.Contains(CauseKind.Death)) return DreamKind.Fall;
        if (k.KindsSeen.Contains(CauseKind.Outage)) return DreamKind.Dark;
        return DreamKind.Alarm;
    }

    private static Topic? TopicOf(DreamKind k) => k switch
    {
        DreamKind.Fire => Topic.Fire, DreamKind.Water => Topic.Water, DreamKind.Breach => Topic.Breach, DreamKind.Dark => Topic.Dark, DreamKind.Fall => Topic.Down, _ => null,
    };

    /// <summary>두뇌 2.0: 직접 본 일이면 꿈이 짙다.</summary>
    private float SeenBoost(CrewMember c, DreamKind kind, long since)
    {
        if (TopicOf(kind) is not Topic t) return 1f;
        foreach (var b in _w.Brain2.Beliefs.Of(c).All)
            if (b.Topic == t && b.Src == BeliefSource.Seen && b.First >= since - SimTime.Minutes(30)) return 1.4f;
        return 1f;
    }

    private void Incidents()
    {
        var w = _w;
        foreach (var k in w.Scale.Cases)
        {
            if (k.Now < IncidentScale.Room) continue;
            bool live = k.Open || w.Tick - k.End < Every * 2;
            if (!live) continue;
            var kind = KindOf(k);
            float wt = k.Peak switch { IncidentScale.Room => 0.25f, IncidentScale.System => 0.45f, IncidentScale.Ship => 0.7f, _ => 0.9f };
            foreach (var c in w.Crew)
            {
                if (c.Dead) continue;
                bool here = c.Room != null && k.Rooms.Contains(c.Room.Id);
                bool part = here || k.Workers.Contains(c.Id) || k.Mustered.Contains(c.Id) || k.Feared.Contains(c.Id);
                if (!part) continue;
                Impress(c, kind, wt * (here ? 1f : 0.6f) * SeenBoost(c, kind, k.Start), k.Name, -1, k.RoomId, $"case:{k.Id}");
            }
            // 큰 사고 자리: 장소의 기억 (계통 이상 · 한 번)
            if (k.Peak >= IncidentScale.System && !_caseSeen.Contains(k.Id) && RoomById(k.RoomId) is Room kr && kr.Cells.Count > 0)
            {
                _caseSeen.Add(k.Id);
                var at = kr.Cells[kr.Cells.Count / 2];
                if (w.Fire.Scorch.Count > 0) foreach (var (cell, v) in w.Fire.Scorch.OrderByDescending(x => x.Value).ThenBy(x => x.Key.Y).ThenBy(x => x.Key.X)) if (w.Ship.RoomAt(cell) == kr) { at = cell; break; }
                AddPlace(at, kr, 1, $"{k.Name} 자리", -1, witnessBoost: true, involved: k);
            }
        }
        // v16.19 큰 사고의 흔적 자리도 장소의 기억으로
        var mt = w.Major.Traces;
        for (; _majorTraces < mt.Count; _majorTraces++)
        {
            var t = mt[_majorTraces];
            if (RoomById(t.Room) is Room tr && !Places.Any(p => p.Room == t.Room && p.Kind == 1 && w.Tick - p.Tick < SimTime.Hours(6)))
                AddPlace(t.At, tr, 1, $"{MajorIncidentSystem.Spec(t.Kind).Name} 자리", -1, witnessBoost: true);
        }
    }

    // ───────────────────────────── 잠 · 꿈 ─────────────────────────────

    private void Sleepers(float h)
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead) { if (Peek(c) is AfterMind dm) dm.Sleeping = false; continue; }
            bool sl = c.Pose == Pose.Sleeping;
            var m = sl || Minds.ContainsKey(c.Id) ? Mind(c) : null;
            if (m == null) continue;
            if (sl && !m.Sleeping) OnSleep(c, m);
            else if (!sl && m.Sleeping) OnWake(c, m);
            m.Sleeping = sl;
            if (sl) During(c, m, h);
        }
    }

    private (bool own, bool wet, bool bare) Bedding(CrewMember c)
    {
        var bed = c.Bed;
        bool own = bed != null && (bed.UseSpots.Contains(c.Cell) || bed.Cells.Contains(c.Cell));
        return (own, own && BedWet(bed!) > 0.3f, own && Bare(bed!));
    }

    private void OnSleep(CrewMember c, AfterMind m)
    {
        var w = _w;
        m.SleepStart = w.Tick;
        m.Night = null;
        var (own, wet, bare) = Bedding(c);
        if (wet) { Stats.WetNights++; if (!m.WetNoted) { m.WetNoted = true; Life.Diary(w, c, Persona.Say(c, "침구가 아직 축축하다. 그래도 누웠다")); } }
        else if (bare) { Stats.BareNights++; if (!m.WetNoted) { m.WetNoted = true; Life.Diary(w, c, Persona.Say(c, "침구를 널어 두고 맨 매트리스에 누웠다. 등이 배긴다")); } }
        // 꿈거리
        Impression? top = null;
        float total = 0f, topV = 0f;
        foreach (var im in m.Seen)
        {
            float age = (w.Tick - im.Tick) / (float)SimTime.TicksPerHour;
            if (age > 144f) continue;
            float v = im.Weight * MathF.Exp(-age / 40f) * FearMatch(c, im.Kind);
            total += v;
            if (v > topV || v == topV && top != null && im.Tick > top.Tick) { topV = v; top = im; }
        }
        float sens = (1.25f - 0.5f * c.Traits.Bravery)
            * (c.Habits.Contains(Habit.Worrier) || c.Habits.Contains(Habit.Pessimist) ? 1.25f : 1f)
            * (c.Habits.Contains(Habit.Optimist) || c.Habits.Contains(Habit.Cheerful) ? 0.8f : 1f)
            * (1f + 1.5f * c.Memory.Trauma)
            * (1f + 0.6f * w.Brain2.Emotions.Get(c, Feeling.Fear) + 0.4f * w.Brain2.Emotions.Get(c, Feeling.Sadness))
            * (wet ? 1.3f : bare ? 1.15f : own ? 1f : 1.1f)
            * (c.Room != null && c.Room.Noise > 0.3f ? 1.15f : 1f)
            * (1f - 4f * Props.SleepAdd(c.Room));
        float I = total * sens;
        float p = Math.Clamp(I - 0.08f, 0f, 0.85f);
        Dream? d = null;
        if (top != null && p > 0f && R.Chance(p))
        {
            var about = top.About >= 0 ? Crew(top.About) : null;
            bool grief = top.Kind == DreamKind.Loss && about != null && c.AffinityTo(about) > 0.35f && R.Chance(0.5f);
            d = new Dream
            {
                Crew = c.Id, Tick = w.Tick, Kind = top.Kind, Nightmare = !grief, Grief = grief, Strength = Math.Clamp(I, 0f, 1.5f), About = top.About, Room = top.Room,
                Source = top.Text, Text = DreamText(top, grief, about, R),
            };
        }
        else if (c.Habits.Contains(Habit.Homesick) && R.Chance(0.12f))
            d = new Dream { Crew = c.Id, Tick = w.Tick, Kind = DreamKind.Home, Strength = 0.3f, Text = R.Pick(HomeDreams) };
        if (d == null) return;
        m.Night = d;
        m.Dreams++;
        Stats.Dreams++;
        if (d.Nightmare)
        {
            m.Nightmares++;
            Stats.Nightmares++;
            m.WakesLeft = 1 + (I > 0.8f ? 1 : 0) + (R.Chance(0.3f) ? 1 : 0);
            m.NextWake = w.Tick + SimTime.Hours(1.2f + 1.5f * R.Float());
        }
        else if (d.Grief) Stats.GriefDreams++;
        else Stats.HomeDreams++;
    }

    private static float FearMatch(CrewMember c, DreamKind k)
    {
        Fear? f = k switch { DreamKind.Fire => Fear.Fire, DreamKind.Water => Fear.Water, DreamKind.Dark => Fear.Dark, DreamKind.Breach => Fear.Vacuum, DreamKind.Loss => Fear.Death, DreamKind.Fall => Fear.Blood, _ => null };
        return f is Fear ff && c.Fears.Contains(ff) ? 1.5f : 1f;
    }

    private static readonly string[] HomeDreams = { "고향 집 부엌에서 밥 냄새가 나는 꿈", "어릴 적 살던 골목을 걷는 꿈", "비 오는 날 창가에 앉아 있는 꿈" };

    private static string DreamText(Impression im, bool grief, CrewMember? about, Rng r)
    {
        string n = about?.Name ?? "누군가";
        if (grief) return r.Pick(new[] { $"{Ko.IGa(n)} 늘 앉던 자리에 앉아 웃고 있는 꿈", $"{Ko.WaGwa(n)} 식당에서 아무 일 없다는 듯 밥을 먹는 꿈", $"{Ko.IGa(n)} 교대하러 와서 어깨를 두드리는 꿈" });
        return im.Kind switch
        {
            DreamKind.Fire => r.Pick(new[] { "불길이 문을 막아 나갈 수 없는 꿈", "연기 속에서 소화기를 찾아 더듬는 꿈", "벽 너머가 벌겋게 타오르는 꿈" }),
            DreamKind.Water => r.Pick(new[] { "물이 침대까지 차오르는 꿈", "물에 잠긴 통로를 헤엄쳐 가는 꿈", "천장에서 물이 끝없이 새는 꿈" }),
            DreamKind.Dark => r.Pick(new[] { "불이 꺼진 통로를 끝없이 걷는 꿈", "깜깜한 방에서 스위치를 찾는 꿈" }),
            DreamKind.Breach => r.Pick(new[] { "벽에 금이 가며 숨이 빨려 나가는 꿈", "문이 닫히는데 손이 닿지 않는 꿈" }),
            DreamKind.Loss => r.Pick(new[] { $"{Ko.EulReul(n)} 부르는데 대답이 없는 꿈", $"{Ko.IGa(n)} 쓰러진 자리로 달려가는데 다리가 움직이지 않는 꿈" }),
            DreamKind.Fall => r.Pick(new[] { "쓰러진 채 아무도 오지 않는 꿈", "동료를 업고 끝없는 계단을 오르는 꿈" }),
            _ => r.Pick(new[] { "경보가 그치지 않는 꿈", "방송이 들리는데 무슨 말인지 모르는 꿈" }),
        };
    }

    private void During(CrewMember c, AfterMind m, float h)
    {
        var w = _w;
        var (_, wet, bare) = Bedding(c);
        if (wet) c.Needs.Rest = MathF.Max(0f, c.Needs.Rest - 0.03f * h);
        else if (bare) c.Needs.Rest = MathF.Max(0f, c.Needs.Rest - 0.012f * h);
        if (m.Night is not { Nightmare: true } d || m.WakesLeft <= 0 || w.Tick < m.NextWake) return;
        // 깬다: 숨을 몰아쉬며 일어나 앉는다 — 옆에서 자던 사람도 깨기도
        m.WakesLeft--;
        m.NextWake = w.Tick + SimTime.Hours(1f + 1.5f * R.Float());
        m.SatUp = w.Tick;
        d.Wakes++;
        m.WakeTicks.Add(w.Tick);
        if (m.WakeTicks.Count > 8) m.WakeTicks.RemoveAt(0);
        Stats.Wakes++;
        c.Needs.Rest = MathF.Max(0f, c.Needs.Rest - 0.04f);
        CrewMember? woke = null;
        foreach (var o in w.Crew)
        {
            if (o == c || o.Dead || o.Pose != Pose.Sleeping || o.Room != c.Room || o.Habits.Contains(Habit.HeavySleeper)) continue;
            if ((o.Position - c.Position).LengthSquared() > 25f || !R.Chance(0.5f)) continue;
            o.Needs.Rest = MathF.Max(0f, o.Needs.Rest - 0.02f);
            if (!d.WokeOthers.Contains(o.Id)) d.WokeOthers.Add(o.Id);
            Stats.WokeOthers++;
            woke ??= o;
        }
        if (d.Wakes == 1)
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} 자다가 숨을 몰아쉬며 깼다" + (woke != null ? $" — 옆에서 자던 {Ko.IGa(woke.Name)} 덩달아 깼다" : ""), c.Id);
    }

    private void OnWake(CrewMember c, AfterMind m)
    {
        var w = _w;
        m.WetNoted = false;
        if (m.Night is not Dream d) return;
        m.Night = null;
        d.Woke = w.Tick;
        m.Last = d;
        var emo = w.Brain2.Emotions;
        var about = d.About >= 0 ? Crew(d.About) : null;
        if (d.Nightmare)
        {
            emo.Feel(c, Feeling.Fear, 0.12f + 0.18f * MathF.Min(1f, d.Strength), $"악몽 — {d.Source}");
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.05f);
            if (d.Kind == DreamKind.Loss && about != null) emo.Feel(c, Feeling.Sadness, 0.12f, $"꿈에 {about.Name}", about);
            RefreshPlaces(c, d);
            string tail = d.Wakes >= 2 ? $" 밤새 {d.Wakes}번 깼다." : d.Wakes == 1 ? " 한 번 깨서 한참 천장을 봤다." : "";
            Life.Diary(w, c, Persona.Say(c, $"{(m.Nightmares > 1 ? "또 " : "")}{d.Text}을 꿨다.{tail}"));
        }
        else if (d.Grief)
        {
            emo.Feel(c, Feeling.Sadness, 0.2f, $"꿈에 {about?.Name}", about);
            emo.Feel(c, Feeling.Joy, 0.05f, "꿈에서라도 봤다");
            Life.Diary(w, c, Persona.Say(c, $"{d.Text}을 꿨다. 깨고 나서 한참 누워 있었다."));
        }
        else
        {
            emo.Feel(c, Feeling.Joy, 0.1f, "고향 꿈");
            Life.Diary(w, c, Persona.Say(c, $"{d.Text}을 꿨다. 오늘은 조금 가볍다."));
        }
        Stats.Diaries++;
        d.Mood = emo.Get(c, Feeling.Fear) + emo.Get(c, Feeling.Sadness) - emo.Get(c, Feeling.Joy);
    }

    /// <summary>주컴퓨터: 침대 감지기의 수면 기록 — 사흘 사이 자주 깬 사람은 아침 근무를 늦추자고 한다.</summary>
    private void SleepAdvice()
    {
        var w = _w;
        if (!w.Automation.Present || !w.Automation.MainOnline) return;
        foreach (var (id, m) in Minds)
        {
            int n = m.WakeTicks.Count(t => w.Tick - t < SimTime.TicksPerDay * 3);
            if (n < 3 || m.AdvisedSleep >= 0 && w.Tick - m.AdvisedSleep < SimTime.TicksPerDay * 2) continue;
            if (Crew(id) is not CrewMember c || c.Dead) continue;
            m.AdvisedSleep = w.Tick;
            var act = w.Automation.Book.Add(ActKind.Advice, c.Bed?.Room ?? c.Room, $"수면 기록: {c.Name} 사흘 사이 밤에 {n}번 깸 · 깬 뒤 맥박이 빠르다",
                "판단: 사고 뒤 잠을 설친다 — 피로가 쌓이면 일하다 실수가 는다", "조치: 다음 근무 시작을 두 시간 늦춤", "요청: 의무관 면담 · 잠자리를 살펴 주기", $"after:sleep:{c.Id}", SimTime.TicksPerDay);
            if (act == null) continue;
            Stats.SleepAdvice++;
            if (w.Automation.Trusts.Obeys(c))
            {
                c.ExcusedUntil = Math.Max(c.ExcusedUntil, w.Tick + SimTime.Hours(2));
                Stats.Excused++;
                w.Log.Add(w.Tick, LogKind.Life, $"주 컴퓨터가 {Ko.EulReul(c.Name)} 두 시간 더 쉬게 했다 — 사흘째 밤마다 깬다", c.Id);
            }
        }
    }

    /// <summary>화면: 악몽에 뒤척이는 중 · 방금 깨서 일어나 앉음.</summary>
    public bool Tossing(CrewMember c) => Peek(c) is { Sleeping: true, Night.Nightmare: true };
    public bool SatUp(CrewMember c) => Peek(c) is AfterMind m && m.SatUp >= 0 && _w.Tick - m.SatUp < SimTime.Minutes(4);

    // ───────────────────────────── 식사: 늘 앉는 자리 · 돌아오기 · 빈자리 · 아침 식탁 ─────────────────────────────

    private void Meals()
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Job is not { Activity: EatActivity } j || c.Pose != Pose.Sitting || c.Room is not Room r) continue;
            var m = Mind(c);
            if (m.LastEatJob != j)
            {
                m.LastEatJob = j;
                if (r.Kind == RoomType.Mess) AteAtMess(c, m, r);
            }
            else if (r.Kind == RoomType.Mess && Seats.Count > 0)
            {
                // 신입이 아직 그 의자에 앉아 있다: 아는 사람이 들어오면 그때 이야기한다
                foreach (var es in Seats)
                {
                    if (!es.Active || es.Room != r.Id || es.Knew.Contains(c.Id)) continue;
                    if (r.Furniture.FirstOrDefault(f => f.Id == es.Seat) is Furniture sf && (sf.UseSpots.Contains(c.Cell) || sf.Cells.Contains(c.Cell))) SeatTaken(c, sf, r);
                }
            }
            Breakfast(c, m, r);
        }
        if (Seats.Count > 0) LaterTell();
        // 혼자 먹기는 날이 갈수록 옅어진다
        float day = Every / (float)SimTime.TicksPerDay;
        foreach (var (_, m) in Minds) if (m.Withdraw > 0f) m.Withdraw = MathF.Max(0f, m.Withdraw - 0.16f * day);
    }

    private void AteAtMess(CrewMember c, AfterMind m, Room r)
    {
        var w = _w;
        Furniture? seat = null;
        foreach (var f in r.Furniture) if (f.Type == FurnitureType.Seat && (f.UseSpots.Contains(c.Cell) || f.Cells.Contains(c.Cell))) { seat = f; break; }
        if (seat != null)
        {
            m.SeatUse[seat.Id] = m.SeatUse.GetValueOrDefault(seat.Id) + 1;
            m.LastSeat = seat.Id;
            SeatTaken(c, seat, r);
        }
        // 그을음 냄새로 다른 데서 먹다가 돌아왔다
        if (m.AwayWhy == "soot")
        {
            Stats.SootReturns++;
            int days = Math.Max(1, (int)MathF.Round((w.Tick - (m.AwayFirst >= 0 ? m.AwayFirst : w.Tick)) / (float)SimTime.TicksPerDay));
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} {(days >= 2 ? $"{days}일 만에 " : "")}다시 {r.Name}에서 먹는다 — 그을음 냄새가 거의 빠졌다", c.Id);
            Life.Diary(w, c, Persona.Say(c, $"{r.Name} 냄새가 이제 견딜 만하다. 다시 거기서 먹었다"));
            m.AwayWhy = null;
            m.AwayRoom = -1;
            m.AwayFirst = -1;
        }
        // 혼자 먹다가 → 구석 → 늘 앉던 자리
        int st = m.Stage;
        if (m.AteStage == 2 && st <= 1)
        {
            Stats.Corner++;
            int days = Math.Max(1, (int)MathF.Round((w.Tick - m.WithdrawFrom) / (float)SimTime.TicksPerDay));
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} {days}일 만에 {r.Name}에 나와 먹었다 — {(st == 1 ? "구석 자리에서 말없이" : "늘 앉던 자리에서")}", c.Id);
            Life.Diary(w, c, Persona.Say(c, st == 1 ? "오랜만에 식당에서 먹었다. 구석에 앉았다" : "오랜만에 식당에서 먹었다"));
            if (st == 0) Stats.BackToSeat++;
        }
        else if (m.AteStage == 1 && st == 0)
        {
            Stats.BackToSeat++;
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} 다시 늘 앉던 자리에서 먹는다 — 말수도 조금 돌아왔다", c.Id);
        }
        m.AteStage = st;
    }

    /// <summary>떠난 사람의 의자에 누가 앉았다: 모르는 신입이면 가까웠던 사람이 그 사람 이야기를 한다 · 날이 지나면 자리를 내준다.</summary>
    /// <summary>신입에게 그 의자의 주인 이야기를 한다 (그 자리에서 · 나중에 마주쳤을 때).</summary>
    private void TellNewcomer(EmptySeat es, CrewMember c, CrewMember teller, Room r, bool later)
    {
        var w = _w;
        var dead = Crew(es.Crew);
        float days = (w.Tick - es.Since) / (float)SimTime.TicksPerDay;
        Stats.NewcomerTold++;
        es.SatBy = -1;
        bool keep = (teller.Habits.Contains(Habit.Superstitious) || teller.Habits.Contains(Habit.Serious)) && dead != null && teller.AffinityTo(dead) > 0.3f && days < 7f;
        es.Knew.Add(c.Id);
        string story = dead != null ? StoryOf(dead) : "";
        string where = later ? "네가 앉았던 그 의자 말이야, " : "거기 ";
        w.Relations.Remember(c, teller, RelationReason.TaughtMe, $"{es.Name} 이야기를 해 줬다");
        Impress(c, DreamKind.Loss, 0.12f, $"{es.Name} 이야기", es.Crew, es.Room, $"told:{es.Crew}");
        var seatRoom = RoomById(es.Room);
        if (keep)
        {
            Stats.SeatsKept++;
            teller.Say(w, Persona.Say(teller, $"{where}{es.Name} 자리였어. {story} — 한동안은 비워 두자"));
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(teller.Name)} 신입 {c.Name}에게 그 의자가 {es.Name}의 자리였다고 일러 주었다 — 자리는 한동안 더 비워 두기로", teller.Id);
        }
        else
        {
            es.Released = w.Tick;
            es.ReleasedTo = c.Id;
            Stats.SeatsReleased++;
            teller.Say(w, Persona.Say(teller, $"{where}{es.Name} 자리였어. {story} — 이제 네 자리 해"));
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(teller.Name)} 신입 {c.Name}에게 {es.Name} 이야기를 해 주고 그 자리를 내주었다", teller.Id);
            w.History.Add(w, HistoryKind.Memory, $"{es.Name}의 빈자리에 신입 {Ko.IGa(c.Name)} 앉게 되었다 — {Ko.IGa(teller.Name)} {es.Name} 이야기를 해 주었다", seatRoom ?? r, new[] { teller, c }, log: false);
            foreach (var t in Traces) if (t.Kind == AfterTraceKind.EmptySeat && t.Item == es.Seat && !t.Gone) t.Text = $"{seatRoom?.Name}의 의자 — {Ko.IGa(es.Name)} 앉던 자리, 지금은 {c.Name}의 자리";
        }
        Life.Diary(w, c, Persona.Say(c, $"내가 앉은 의자가 {es.Name}의 자리였단다. {story}"));
    }

    /// <summary>아무도 못 본 사이 신입이 그 의자에 앉았었다: 아는 사람이 신입과 한방에 있게 되면 일러 준다.</summary>
    private void LaterTell()
    {
        var w = _w;
        foreach (var es in Seats)
        {
            if (!es.Active || es.SatBy < 0) continue;
            if (w.Tick - es.SatAt > SimTime.Hours(30) || Crew(es.SatBy) is not CrewMember n || n.Dead || es.Knew.Contains(n.Id)) { es.SatBy = -1; continue; }
            if (!n.IsAwake || n.Room is not Room r) continue;
            var dead = Crew(es.Crew);
            CrewMember? teller = null;
            foreach (var o in w.Crew)
            {
                if (o == n || o.Dead || !es.Knew.Contains(o.Id) || o.Room != r || !o.IsAwake || o.IsChild || o.Job?.Urgent == true) continue;
                if (teller == null || dead != null && o.AffinityTo(dead) > teller.AffinityTo(dead)) teller = o;
            }
            if (teller != null) TellNewcomer(es, n, teller, r, true);
        }
    }

    private void SeatTaken(CrewMember c, Furniture seat, Room r)
    {
        var w = _w;
        foreach (var es in Seats)
        {
            if (!es.Active || es.Seat != seat.Id) continue;
            var dead = Crew(es.Crew);
            float days = (w.Tick - es.Since) / (float)SimTime.TicksPerDay;
            if (!es.Knew.Contains(c.Id))
            {
                // 신입: 그 자리를 모른다
                CrewMember? teller = null;
                foreach (var o in w.Crew)
                {
                    if (o == c || o.Dead || !es.Knew.Contains(o.Id) || o.Room != r || !o.IsAwake || o.IsChild) continue;
                    if (teller == null || (dead != null && o.AffinityTo(dead) > teller.AffinityTo(dead))) teller = o;
                }
                if (teller == null) { es.SatBy = c.Id; es.SatAt = w.Tick; return; } // 아무도 못 봤다 — 나중에 누가 듣고 일러 준다
                TellNewcomer(es, c, teller, r, false);
                return;
            }
            if (c.Id == es.Crew) return;
            if (days >= 4f)
            {
                es.Released = w.Tick;
                es.ReleasedTo = c.Id;
                Stats.SeatsReleased++;
                w.Log.Add(w.Tick, LogKind.Life, $"{(int)days}일 만에 {es.Name}의 자리에 다시 사람이 앉았다 — {c.Name}", c.Id);
                Life.Diary(w, c, Persona.Say(c, $"{es.Name} 자리에 앉았다. 이상하게 따뜻했다"));
            }
            return;
        }
    }

    private string StoryOf(CrewMember dead)
    {
        var fav = _w.Relations.Favorite(dead);
        return dead.Habits.Contains(Habit.Joker) ? "밥 먹을 때마다 농담하던 사람" : dead.Habits.Contains(Habit.Snacker) ? "주머니에 늘 간식이 있던 사람"
            : dead.Habits.Contains(Habit.EarlyBird) ? "아침마다 제일 먼저 와 있던 사람" : $"{Ko.EulReul(fav)} 좋아하던 사람";
    }

    /// <summary>EatActivity: 의자 고르기 — 떠난 사람의 의자는 비워 두고(아는 사람만) · 혼자이고 싶은 사람은 구석으로 (거리² 단위).</summary>
    public float SeatBias(CrewMember c, Furniture seat)
    {
        if (Off) return 0f;
        float b = 0f;
        foreach (var es in Seats)
        {
            if (!es.Active || es.Seat != seat.Id || !es.Knew.Contains(c.Id) || c.Id == es.Crew) continue;
            float days = (_w.Tick - es.Since) / (float)SimTime.TicksPerDay;
            float k = days < 5f ? 1f : MathF.Max(0f, 1f - (days - 5f) / 3f);
            b += 400f * k;
        }
        if (Peek(c) is { Stage: 1 })
            foreach (var o in _w.Crew)
            {
                if (o == c || o.Dead || o.Room != seat.Room || o.Pose != Pose.Sitting) continue;
                float d = (o.Position - seat.Center).Length();
                if (d < 2.5f) b += (2.5f - d) * 30f;
            }
        return b;
    }

    /// <summary>EatActivity: 떠난 사람의 의자 앞에서 잠깐 멈춘다 (가까웠던 사람 · 엿새 동안 · 하루 한 번).</summary>
    public List<Toil> PauseToils(CrewMember c, Furniture? seat, DistanceField dist)
    {
        var list = new List<Toil>();
        if (Off || seat == null || Seats.Count == 0) return list;
        var w = _w;
        long today = SimTime.Day(w.Tick);
        if (Peek(c) is AfterMind pm && pm.PausedDay == today) return list;
        foreach (var es in Seats)
        {
            if (!es.Active || es.Room != seat.Room.Id || es.Seat == seat.Id || !es.Knew.Contains(c.Id)) continue;
            if (w.Tick - es.Since > SimTime.TicksPerDay * 6) continue;
            var dead = Crew(es.Crew);
            if (dead == null || c.AffinityTo(dead) < 0.15f && !c.Memory.Comrades.Contains(dead.Id)) continue;
            var ef = seat.Room.Furniture.FirstOrDefault(f => f.Id == es.Seat);
            if (ef == null || ef.UseSpots.Count == 0) continue;
            var spot = ef.UseSpots[0];
            if (!dist.Reachable(spot)) continue;
            var face = ef.Center;
            var e = es;
            list.Add(new GotoToil(spot));
            list.Add(new WaitToil(SimTime.Minutes(1), Pose.Standing, face));
            list.Add(new DoToil((cm, world) => { world.After.Paused(cm, e); return true; }));
            Mind(c).PausedDay = today;
            break;
        }
        return list;
    }

    internal void Paused(CrewMember c, EmptySeat es)
    {
        var w = _w;
        es.Pauses++;
        Stats.Pauses++;
        var dead = Crew(es.Crew);
        w.Brain2.Emotions.Feel(c, Feeling.Sadness, 0.06f, $"{es.Name}의 빈자리", dead);
        if (!es.Cup)
        {
            es.Cup = true;
            es.CupBy = c.Id;
            Stats.Cups++;
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} {Ko.IGa(es.Name)} 앉던 의자 앞에 잠깐 멈춰 섰다가 그 자리에 컵 하나를 놓았다", c.Id);
            Life.Diary(w, c, Persona.Say(c, $"{es.Name} 자리에 컵을 하나 놓아 두었다"));
        }
        else w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} {es.Name}의 빈자리 앞에서 잠깐 멈췄다", c.Id);
    }

    /// <summary>아침 식탁: 간밤의 꿈을 이야기한다 (같은 꿈 · 위로 · 놀림 · 혼자 삭임).</summary>
    private void Breakfast(CrewMember c, AfterMind m, Room r)
    {
        var w = _w;
        if (m.Last is not Dream d || d.Told || d.Woke < 0 || w.Tick - d.Woke > SimTime.Hours(4) || d.Kind == DreamKind.Home && !c.Habits.Contains(Habit.Talker)) return;
        CrewMember? o = null;
        int score = -1;
        foreach (var x in w.Crew)
        {
            if (x == c || x.Dead || x.Room != r || !x.IsAwake || x.IsChild || x.IsMoving) continue;
            int s = d.WokeOthers.Contains(x.Id) ? 3 : Peek(x)?.Last is Dream xd && xd.Woke >= 0 && w.Tick - xd.Woke < SimTime.Hours(6) && xd.Kind == d.Kind ? 2 : c.AffinityTo(x) > 0.2f ? 1 : 0;
            if (s > score) { score = s; o = x; }
        }
        if (o == null) return;
        d.Told = true;
        bool shy = c.Habits.Contains(Habit.Loner) || c.Traits.Sociability < 0.3f && c.AffinityTo(o) < 0.3f;
        if (shy && !d.WokeOthers.Contains(o.Id))
        {
            Stats.Kept2++;
            Life.Diary(w, c, Persona.Say(c, "꿈 이야기는 아무에게도 하지 않았다"));
            return;
        }
        Stats.TableTalks++;
        var od = Peek(o)?.Last;
        bool same = od != null && od != d && od.Woke >= 0 && w.Tick - od.Woke < SimTime.Hours(6) && od.Kind == d.Kind;
        bool woke = d.WokeOthers.Contains(o.Id);
        bool tease = !same && !woke && d.Nightmare && (o.Habits.Contains(Habit.Joker) || o.Habits.Contains(Habit.Prankster)) && o.AffinityTo(c) < 0.2f;
        var emo = w.Brain2.Emotions;
        if (woke) o.Say(w, Persona.Say(o, $"어젯밤 소리 지르던데 — 괜찮아?"));
        c.Say(w, Persona.Say(c, d.Grief ? $"어젯밤 꿈에 {Ko.EulReul(d.About >= 0 ? Crew(d.About)?.Name ?? "그 사람" : "그 사람")} 봤어" : $"어젯밤 {d.Text}을 꿨어"));
        if (same)
        {
            Stats.SharedDreams++;
            od!.Told = true;
            o.Say(w, Persona.Say(o, "나도... 비슷한 꿈을 꿨어"));
            c.ChangeAffinity(o, 0.08f); o.ChangeAffinity(c, 0.08f);
            w.Relations.Remember(c, o, RelationReason.SharedHardship, "같은 꿈을 꿨다");
            w.Relations.Remember(o, c, RelationReason.SharedHardship, "같은 꿈을 꿨다");
            emo.Feel(c, Feeling.Fear, -0.1f, ""); emo.Feel(o, Feeling.Fear, -0.1f, "");
            w.Log.Add(w.Tick, LogKind.Life, $"아침 식탁 — {Ko.WaGwa(c.Name)} {Ko.IGa(o.Name)} 간밤에 같은 꿈을 꿨다는 걸 알았다 ({d.Source})", c.Id);
            Life.Diary(w, c, Persona.Say(c, $"{o.Name}도 같은 꿈을 꿨단다. 나만 그런 게 아니었다"));
            ComfortPlaces(c, 0.9f); ComfortPlaces(o, 0.9f);
        }
        else if (tease)
        {
            Stats.Teased++;
            o.Say(w, Persona.Say(o, "꿈은 꿈이지 — 애도 아니고"));
            emo.Feel(c, Feeling.Shame, 0.12f, "꿈 이야기를 놀림받았다", o);
            emo.Feel(c, Feeling.Anger, 0.08f, "꿈 이야기를 놀림받았다", o);
            c.ChangeAffinity(o, -0.05f);
            w.Log.Add(w.Tick, LogKind.Life, $"아침 식탁 — {Ko.IGa(c.Name)} 악몽 이야기를 꺼냈다가 {o.Name}에게 놀림을 받았다", c.Id);
            Life.Diary(w, c, Persona.Say(c, $"꿈 이야기를 괜히 했다. {Ko.EunNeun(o.Name)} 웃기만 했다"));
        }
        else
        {
            string line = o.Habits.Contains(Habit.Optimist) || o.Habits.Contains(Habit.Cheerful) ? "다 지나갔어. 오늘은 괜찮을 거야"
                : o.Habits.Contains(Habit.Serious) ? "계속 그러면 의무관한테 얘기해 봐"
                : o.Habits.Contains(Habit.Generous) ? "커피 한 잔 더 가져다줄게"
                : d.Grief ? "나도 가끔 그 사람 생각해" : "나도 그날 생각이 나";
            o.Say(w, Persona.Say(o, line));
            emo.Feel(c, Feeling.Fear, -0.15f, "");
            if (d.Grief) emo.Feel(c, Feeling.Sadness, -0.08f, "");
            c.ChangeAffinity(o, 0.05f);
            w.Relations.Remember(c, o, RelationReason.Comforted, "꿈 이야기를 들어 줬다");
            var cm = Mind(c);
            cm.Withdraw = MathF.Max(0f, cm.Withdraw - 0.12f);
            ComfortPlaces(c, 0.85f);
            w.Log.Add(w.Tick, LogKind.Life, $"아침 식탁 — {Ko.IGa(c.Name)} 간밤 꿈 이야기를 하자 {Ko.IGa(o.Name)} 들어 주었다", c.Id);
            Life.Diary(w, c, Persona.Say(c, $"아침에 {o.Name}에게 꿈 이야기를 했다. 조금 나아졌다"));
        }
    }

    // ───────────────────────────── 장소의 기억 ─────────────────────────────

    internal void AddPlace(Cell at, Room room, byte kind, string text, int crew, bool witnessBoost, ScaleCase? involved = null)
    {
        var w = _w;
        var dead = crew >= 0 ? Crew(crew) : null;
        var p = new PlaceMemory { Id = _ids++, At = at, Room = room.Id, Tick = w.Tick, Kind = kind, Text = text, Crew = crew };
        foreach (var o in w.Crew)
        {
            if (o.Dead || o.Id == crew) continue;
            float close = dead != null ? MathF.Max(0f, o.AffinityTo(dead)) : 0f;
            float wgt = kind == 0 ? 0.45f + 0.6f * close : 0.25f;
            bool here = o.Room == room;
            if (witnessBoost && here) wgt += 0.3f;
            if (involved != null && (involved.Workers.Contains(o.Id) || involved.Feared.Contains(o.Id))) wgt += 0.2f;
            wgt *= Personal(o);
            if (wgt < 0.15f) continue;
            p.Weight[o.Id] = MathF.Min(1.2f, wgt);
            p.Since[o.Id] = w.Tick;
        }
        if (p.Weight.Count == 0) return;
        Places.Add(p);
        Stats.Places++;
        if (Places.Count > 24) Places.RemoveAt(0);
    }

    private static float Personal(CrewMember o) => (1.35f - o.Traits.Bravery)
        * (o.Habits.Contains(Habit.Worrier) || o.Habits.Contains(Habit.Superstitious) ? 1.2f : 1f)
        * (o.Habits.Contains(Habit.Optimist) || o.Habits.Contains(Habit.Daredevil) ? 0.7f : 1f);

    /// <summary>그 자리를 얼마나 피하나 (0~1.2): 무게 × 옅어짐 (용기 · 긴장으로 사람마다 다른 빠르기).</summary>
    public float PlaceWeight(CrewMember c, PlaceMemory p)
    {
        if (!p.Weight.TryGetValue(c.Id, out var wgt)) return 0f;
        float days = (_w.Tick - p.Since[c.Id]) / (float)SimTime.TicksPerDay;
        float tau = 2f + 4f * (1f - c.Traits.Bravery) + 6f * c.Memory.Trauma;
        return wgt * MathF.Exp(-days / tau);
    }

    /// <summary>그 칸을 지날 때 이 사람에게 더 드는 길 비용 (시험 · 화면용).</summary>
    public int PlaceCost(CrewMember c, Cell cell)
    {
        int cost = 0;
        foreach (var p in Places)
        {
            int dx = Math.Abs(p.At.X - cell.X), dy = Math.Abs(p.At.Y - cell.Y);
            if (dx > 1 || dy > 1) continue;
            int k = Quant(PlaceWeight(c, p));
            cost += dx == 0 && dy == 0 ? k : k / 2;
        }
        return cost;
    }

    private static int Quant(float v) => v < 0.1f ? 0 : (int)MathF.Round(v * 60f / 6f) * 6;

    /// <summary>악몽은 그 자리의 기억을 붙잡는다 (옅어지던 시간이 반으로).</summary>
    private void RefreshPlaces(CrewMember c, Dream d)
    {
        foreach (var p in Places)
        {
            if (!p.Since.ContainsKey(c.Id)) continue;
            if (!(d.Room >= 0 && p.Room == d.Room || d.About >= 0 && p.Crew == d.About)) continue;
            p.Since[c.Id] = _w.Tick - (_w.Tick - p.Since[c.Id]) / 2;
        }
    }

    /// <summary>위로 · 같은 꿈: 그 자리의 무게가 준다.</summary>
    private void ComfortPlaces(CrewMember c, float k)
    {
        foreach (var p in Places) if (p.Weight.TryGetValue(c.Id, out var v)) p.Weight[c.Id] = v * k;
    }

    /// <summary>30분마다: 사람마다 길 비용 칸을 다시 편다 (가장 무거운 셋) · 가까웠던 사람은 그 자리에 종이꽃을 둔다.</summary>
    private void PlaceUpdate()
    {
        var w = _w;
        Places.RemoveAll(p => w.Tick - p.Tick > SimTime.TicksPerDay * 30);
        var tmp = new List<(float v, PlaceMemory p)>(4);
        var grid = w.Ship.Grid;
        foreach (var c in w.Crew)
        {
            if (c.Dead) continue;
            tmp.Clear();
            foreach (var p in Places) { float v = PlaceWeight(c, p); if (v >= 0.1f) tmp.Add((v, p)); }
            if (tmp.Count == 0) { c.Memory.Spots = null; continue; }
            tmp.Sort((a, b) => b.v.CompareTo(a.v) != 0 ? b.v.CompareTo(a.v) : a.p.Id.CompareTo(b.p.Id));
            var arr = new List<int>(54);
            for (int i = 0; i < tmp.Count && i < 3; i++)
            {
                int k = Quant(tmp[i].v);
                if (k <= 0) continue;
                var at = tmp[i].p.At;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var cell = new Cell(at.X + dx, at.Y + dy);
                    if (!grid.InBounds(cell)) continue;
                    arr.Add(grid.Index(cell));
                    arr.Add(dx == 0 && dy == 0 ? k : k / 2);
                }
            }
            var old = c.Memory.Spots;
            if (old != null && old.AsSpan().SequenceEqual(arr.ToArray())) continue;
            c.Memory.Spots = arr.Count > 0 ? arr.ToArray() : null;
        }
        // 종이꽃: 가까웠던 사람이 하루 뒤 그 자리를 지나다 내려놓는다
        foreach (var p in Places)
        {
            if (p.Kind != 0 || p.FlowerBy >= 0 || w.Tick - p.Tick < SimTime.Hours(12) || Crew(p.Crew) is not CrewMember dead) continue;
            foreach (var c in w.Crew)
            {
                if (c.Dead || !c.IsAwake || c.Job?.Urgent == true || c.AffinityTo(dead) < 0.35f) continue;
                if (Math.Abs(c.Cell.X - p.At.X) > 2 || Math.Abs(c.Cell.Y - p.At.Y) > 2) continue;
                p.FlowerBy = c.Id;
                p.FlowerAt = w.Tick;
                Stats.Flowers++;
                var room = RoomById(p.Room);
                AddTrace(new AfterTrace { Kind = AfterTraceKind.SpotFlower, Room = p.Room, At = p.At, Tick = w.Tick, Crew = dead.Id, Event = $"{dead.Name}의 죽음", Text = $"{room?.Name} 바닥의 종이꽃 — {Ko.IGa(dead.Name)} 쓰러진 자리 ({c.Name})" });
                w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} {Ko.IGa(dead.Name)} 쓰러졌던 자리에 종이꽃 하나를 내려놓았다", c.Id);
                Life.Diary(w, c, Persona.Say(c, $"{Ko.IGa(dead.Name)} 쓰러진 자리에 꽃을 접어 두었다. 아직 그쪽으로는 잘 안 가게 된다"));
                break;
            }
        }
    }
}
