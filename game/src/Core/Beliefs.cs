using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.15 승무원 두뇌 2.0 — ① 믿음: 사람마다 자기 세계 모형.
// 본 것(눈) · 들은 것(경보 · 방송 · 무전 · 엿들음) · 전해 들은 것(말 · 소문 · 컴퓨터)이 시각 · 확신도 · 출처와 함께 남는다.
// 틀릴 수 있고(소문 · 거짓말 · 오래된 정보), 오래되면 흐려지고(반감기), 가서 보면 고쳐진다.
// 흩어져 있던 "아는 것" 조각 — Mind.Knows(사고) · 방송을 들은 사람 · Cosmic.Knows · 당직 쪽지 소문 · 칸막이 엿들음 — 이 이곳으로 모인다.
// 주요 행동(대피할 방 · 불 확인 · 물건 찾기 · 사람 찾기 · 정전 대처)은 세계 대신 이 믿음으로 판단한다.

/// <summary>믿음의 주제. Id는 방 · 사람 · 물건 종류 · 설비 · 대재난 번호.</summary>
public enum Topic : byte { Fire, Breach, Down, Dark, Air, Water, Person, Item, Omen, Cosmic, Shelter, Warn, Outage, Slip, Mix, Forecast, Thing } // v16 통합: 미끄러운 바닥(본 것 · 방송) · 위험 조합 경고(Matter) · 컴퓨터 예측(Outlook)

/// <summary>어떻게 알았나 — 출처마다 확신과 흐려지는 속도가 다르다.</summary>
public enum BeliefSource : byte { Seen, Alarm, Broadcast, Radio, Told, Rumor, Overheard, Computer, Guess }

/// <summary>정전의 원인 짐작 (Topic.Outage의 값).</summary>
public enum OutageCause { Unknown = 0, Breaker = 1, Power = 2, Lights = 3, Wiring = 4 }

/// <summary>믿음 한 줄: 주제 · 대상 · 값 · 확신도(그때) · 언제 · 어디서 들었나 · 누구에게서.</summary>
public sealed class Belief
{
    public Topic Topic;
    public int Id;
    /// <summary>값: 불 · 정전 등은 1/0, 사람 · 물건은 방/보관함 Id (-1 = 거기 없다).</summary>
    public int Value;
    /// <summary>덧붙은 값: 사람은 잠듦(1) · 물건은 개수.</summary>
    public int Aux;
    public float Conf;
    public long Tick;
    public long First;
    /// <summary>지금 값을 믿기 시작한 때 (값이 바뀌면 새로).</summary>
    public long Since;
    public BeliefSource Src;
    public int From = -1;
    public int Changes;
    /// <summary>위험 믿음에 경보가 함께 울렸다 (다들 들었을 것이다).</summary>
    public bool Alarmed;
}

/// <summary>한 사람의 믿음 장부.</summary>
public sealed class BeliefBook
{
    internal readonly Dictionary<long, Belief> Map = new();
    /// <summary>(사실 열쇠, 사람) — 내가 알려 준 사람 · 같이 본 사람 (남이 아는지 짐작하는 재료).</summary>
    internal readonly HashSet<(long fact, int who)> Shared = new();
    public int Learned, Corrected, Doubted, Told, Heard;
    public long LastCorrection = -1;
    public string? LastCorrectionText;
    /// <summary>내 방이 캄캄해진 걸 알아챈 때 (정전 대처의 시작).</summary>
    public long DarkSince = -1;
    /// <summary>여러 방이 한꺼번에 꺼졌다 (큰 정전 — 다른 방 사람도 어둠 속일 것이다).</summary>
    public bool DarkWide;
    public IEnumerable<Belief> All => Map.Values;
    public int Count => Map.Count;
}

public sealed class BeliefSystem
{
    private readonly World _w;
    private readonly Dictionary<int, BeliefBook> _books = new();
    private int _phase;
    private int[] _roomFire = Array.Empty<int>();
    private bool[] _wasDark = Array.Empty<bool>();
    private readonly List<Room> _newDark = new();
    private bool _darkInit;
    private readonly List<List<CrewMember>> _roomCrew = new();
    private int _lastBroadcast = -1;
    private long _heardTick = -1;
    private readonly List<(string key, Room? room, bool alarm)> _prevInc = new();
    private readonly Dictionary<(int crew, long fact), long> _compSaw = new(); // 주 컴퓨터가 카메라로 그 사람이 그 자리에서 본 걸 본 때
    private long _nextPrune, _nextNudge;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7919 + 1213));

    public int Learns, Corrections, Doubts, Imports, AllClears, Nudges, NudgesHeeded;

    public BeliefSystem(World w) => _w = w;

    public static long Key(Topic t, int id) => ((long)t << 32) | (uint)id;
    public static Topic TopicOf(long key) => (Topic)(key >> 32);
    public static int IdOf(long key) => (int)(key & 0xffffffffL);

    public BeliefBook Of(CrewMember c)
    {
        if (!_books.TryGetValue(c.Id, out var b)) _books[c.Id] = b = new BeliefBook();
        return b;
    }

    public IEnumerable<(int crew, BeliefBook book)> Books => _books.Select(kv => (kv.Key, kv.Value));

    // ─────────────────────────── 흐려짐 ───────────────────────────

    /// <summary>반감기 (시간): 사람은 금방 옮겨 다니고, 물건 자리는 오래 간다. 본 것이 들은 것보다 오래 간다.</summary>
    public static float HalfLife(Topic t, BeliefSource s) => t switch
    {
        Topic.Person => 1.5f,
        Topic.Item => 48f,
        Topic.Dark => 6f,
        Topic.Omen => 72f,
        Topic.Cosmic => 48f,
        Topic.Shelter => 1.5f,
        Topic.Warn => 2f,
        Topic.Outage => 4f,
        Topic.Slip => 3f, // 바닥은 마른다
        Topic.Mix => 24f, // 위험한 조합 경고는 오래 기억한다
        Topic.Forecast => 36f, // 며칠 뒤 예측 — 다음 예측까지
        _ => s switch
        {
            BeliefSource.Seen => 8f,
            BeliefSource.Alarm or BeliefSource.Broadcast or BeliefSource.Computer => 5f,
            BeliefSource.Radio or BeliefSource.Told => 4f,
            _ => 2.5f,
        },
    };

    /// <summary>지금의 확신도 (오래될수록 흐려진다).</summary>
    public float Eff(Belief b) => Eff(b, _w.Tick);

    public static float Eff(Belief b, long now)
    {
        long age = now - b.Tick;
        if (age <= 0) return b.Conf;
        float hl = HalfLife(b.Topic, b.Src) * SimTime.TicksPerHour;
        return b.Conf * MathF.Pow(0.5f, age / hl);
    }

    public static string SourceName(BeliefSource s) => s switch
    {
        BeliefSource.Seen => "직접 봄",
        BeliefSource.Alarm => "경보",
        BeliefSource.Broadcast => "방송",
        BeliefSource.Radio => "무전",
        BeliefSource.Told => "들음",
        BeliefSource.Rumor => "소문",
        BeliefSource.Overheard => "엿들음",
        BeliefSource.Computer => "컴퓨터",
        _ => "짐작",
    };

    // ─────────────────────────── 공통 API ───────────────────────────

    public Belief? Get(CrewMember c, Topic t, int id) => _books.TryGetValue(c.Id, out var b) && b.Map.TryGetValue(Key(t, id), out var x) ? x : null;

    /// <summary>이 사람이 그렇게 믿나 (값이 같고 확신이 문턱 이상).</summary>
    public bool Believes(CrewMember c, Topic t, int id, int value = 1, float min = 0.35f) =>
        Get(c, t, id) is Belief b && b.Value == value && Eff(b) >= min;

    /// <summary>그 값일 확신 (값이 다르면 0).</summary>
    public float Conf(CrewMember c, Topic t, int id, int value = 1) => Get(c, t, id) is Belief b && b.Value == value ? Eff(b) : 0f;

    /// <summary>
    /// 새로 알게 된 것 — 같은 믿음이면 굳어지고, 다른 정보면 확신을 견준다: 직접 본 것 · 더 센 정보는 믿음을 바꾸고, 약한 정보는 의심만 남긴다.
    /// 직접 봐서 믿음이 바뀌면 "고쳐진" 것이다 (헛걸음 · 출처 신뢰 · 컴퓨터 신뢰 · 감정 · 말).
    /// </summary>
    public Belief? Learn(CrewMember c, Topic t, int id, int value, BeliefSource src, float conf, int from = -1, int aux = 0)
    {
        if (!BrainSystem.Enabled || c.Dead) return null;
        var w = _w;
        long now = w.Tick;
        var book = Of(c);
        if (src != BeliefSource.Seen) conf *= w.Brain2.Learning.SourceTrust(c, src);
        conf = Math.Clamp(conf, 0.02f, 1f);
        long k = Key(t, id);
        if (!book.Map.TryGetValue(k, out var b))
        {
            b = new Belief { Topic = t, Id = id, Value = value, Aux = aux, Conf = conf, Tick = now, First = now, Since = now, Src = src, From = from };
            book.Map[k] = b;
            book.Learned++;
            Learns++;
            OnNew(c, b, null);
            return b;
        }
        float cur = Eff(b, now);
        if (b.Value == value)
        {
            b.Aux = aux;
            if (conf >= cur || src == BeliefSource.Seen)
            {
                bool confirm = src == BeliefSource.Seen && b.Src != BeliefSource.Seen && now - b.Tick > SimTime.Minutes(3);
                var oldSrc = b.Src;
                int oldFrom = b.From;
                b.Conf = MathF.Max(conf, cur);
                b.Tick = now;
                b.Src = src;
                b.From = from;
                if (confirm) Confirmed(c, b, oldSrc, oldFrom);
            }
            return b;
        }
        bool strong = src == BeliefSource.Seen || conf >= cur * 0.9f || cur < 0.2f;
        if (!strong)
        {
            // 약한 반대 정보: 값은 그대로, 의심이 생긴다
            b.Conf = cur * (1f - 0.35f * conf);
            b.Tick = now;
            book.Doubted++;
            Doubts++;
            return b;
        }
        int old = b.Value;
        var prevSrc = b.Src;
        int prevFrom = b.From;
        b.Value = value;
        b.Aux = aux;
        b.Conf = conf;
        b.Tick = now;
        b.Src = src;
        b.From = from;
        b.Changes++;
        b.Since = now;
        b.Alarmed = false;
        // 사람은 원래 옮겨 다닌다 — 찾으러 간 자리에 없을 때만 "틀렸다"
        bool meaningful = t is Topic.Fire or Topic.Breach or Topic.Down or Topic.Item or Topic.Outage or Topic.Warn
                          || t == Topic.Person && value < 0 && old >= 0 && w.Brain2.Plans.Current(c)?.Step?.Who?.Id == id;
        if (src == BeliefSource.Seen && cur >= 0.3f && meaningful) Corrected(c, b, old, prevSrc, prevFrom, cur);
        OnNew(c, b, old);
        return b;
    }

    public Belief? Learn(CrewMember c, Fact f, BeliefSource src, float conf, int from = -1) => Learn(c, f.Topic, f.Id, f.Value, src, conf, from);

    /// <summary>Mind.Knows의 사고 열쇠(fire:방 · breach:방 · down:사람)를 믿음으로 (v13.3 아는 것의 차이와 같은 출처).</summary>
    public void FromMind(CrewMember c, string key, KnowSource src, string what)
    {
        if (!BrainSystem.Enabled) return;
        if (!TryParse(key, out var t, out int id)) return;
        var bs = src switch { KnowSource.Seen => BeliefSource.Seen, KnowSource.Alarm => BeliefSource.Alarm, KnowSource.Radio => BeliefSource.Radio, _ => BeliefSource.Rumor };
        float conf = src switch { KnowSource.Seen => 1f, KnowSource.Alarm => 0.9f, KnowSource.Radio => 0.85f, _ => 0.65f };
        var b = Learn(c, t, id, 1, bs, conf);
        if (b != null && src == KnowSource.Alarm) b.Alarmed = true;
    }

    public static bool TryParse(string key, out Topic t, out int id)
    {
        t = Topic.Fire;
        id = -1;
        int i = key.IndexOf(':');
        if (i < 0 || !int.TryParse(key.AsSpan(i + 1), out id)) return false;
        var head = key.AsSpan(0, i);
        if (head.SequenceEqual("fire")) t = Topic.Fire;
        else if (head.SequenceEqual("breach")) t = Topic.Breach;
        else if (head.SequenceEqual("down")) t = Topic.Down;
        else return false;
        return true;
    }

    public static string MindKey(Topic t, int id) => t switch { Topic.Fire => $"fire:{id}", Topic.Breach => $"breach:{id}", Topic.Down => $"down:{id}", _ => "" };

    private void OnNew(CrewMember c, Belief b, int? old)
    {
        var w = _w;
        bool danger = b.Value == 1 && b.Topic is Topic.Fire or Topic.Breach or Topic.Down or Topic.Warn or Topic.Shelter or Topic.Cosmic;
        if (danger)
        {
            c.NextThinkTick = Math.Min(c.NextThinkTick, w.Tick + 1); // 알게 된 그 자리에서 다시 판단한다
            float fear = b.Src == BeliefSource.Seen ? 0.35f : 0.18f;
            if (b.Topic == Topic.Fire && c.Fears.Contains(Fear.Fire)) fear *= 1.8f;
            if (b.Topic == Topic.Down) fear *= 1.2f;
            w.Brain2.Emotions.Feel(c, Feeling.Fear, fear * Eff(b), $"{Describe(b)} ({SourceName(b.Src)})");
        }
        else if (old == 1 && b.Value == 0 && b.Topic is Topic.Fire or Topic.Breach)
            w.Brain2.Emotions.Feel(c, Feeling.Fear, -0.2f, "");
    }

    /// <summary>직접 봐서 들은 것이 맞았다: 그 출처를 조금 더 믿는다 (컴퓨터면 컴퓨터 신뢰).</summary>
    private void Confirmed(CrewMember c, Belief b, BeliefSource oldSrc, int oldFrom)
    {
        var w = _w;
        if (b.Topic is not (Topic.Fire or Topic.Breach or Topic.Down or Topic.Person or Topic.Item or Topic.Outage)) return;
        w.Brain2.Learning.Credit(c, oldSrc, true);
        if (oldSrc is BeliefSource.Broadcast or BeliefSource.Computer && w.Automation.Present && b.Topic != Topic.Person)
            w.Automation.Trusts.Change(c, 0.015f, $"{Describe(b)} — 컴퓨터 말이 맞았다", quiet: true);
        if (oldFrom >= 0 && CrewById(oldFrom) is CrewMember teller && teller != c) c.ChangeAffinity(teller, 0.01f);
    }

    /// <summary>가서 보니 달랐다: 믿음을 고치고, 헛걸음이면 짜증 · 틀린 출처를 덜 믿게 된다 · 말로 나온다.</summary>
    private void Corrected(CrewMember c, Belief b, int old, BeliefSource oldSrc, int oldFrom, float oldConf)
    {
        var w = _w;
        var book = Of(c);
        book.Corrected++;
        Corrections++;
        book.LastCorrection = w.Tick;
        string was = Describe(new Belief { Topic = b.Topic, Id = b.Id, Value = old, Aux = b.Aux }), now = Describe(b);
        book.LastCorrectionText = $"{was} 줄 알았는데 — {now} ({SourceName(oldSrc)}{(oldFrom >= 0 && CrewById(oldFrom) is CrewMember tf ? $" · {tf.Name}" : "")})";
        w.Brain2.Learning.Credit(c, oldSrc, false);
        if (oldFrom >= 0 && CrewById(oldFrom) is CrewMember teller && teller != c && oldSrc is BeliefSource.Told or BeliefSource.Rumor)
            c.ChangeAffinity(teller, -0.015f);
        bool danger = b.Topic is Topic.Fire or Topic.Breach or Topic.Down;
        if (oldSrc is BeliefSource.Broadcast or BeliefSource.Computer && w.Automation.Present && (danger || b.Topic is Topic.Outage or Topic.Item))
            w.Automation.Trusts.Change(c, -0.03f, $"{was} — 컴퓨터 말과 달랐다", quiet: true);
        if (danger && old == 1) w.Brain2.Emotions.Feel(c, Feeling.Fear, -0.25f, "");
        if (c.Job?.TargetRoom is Room tr && c.Room == tr && danger) w.Brain2.Emotions.Feel(c, Feeling.Anger, 0.04f, "헛걸음");
        c.NextThinkTick = Math.Min(c.NextThinkTick, w.Tick + 1);
        if (b.Topic is Topic.Fire or Topic.Breach or Topic.Person or Topic.Item or Topic.Outage)
        {
            string line = b.Topic switch
            {
                Topic.Fire or Topic.Breach => old == 1 ? $"…{(b.Topic == Topic.Fire ? "불" : "구멍")}이 없잖아. {(oldSrc is BeliefSource.Rumor or BeliefSource.Told or BeliefSource.Overheard ? "헛소문이었나" : "벌써 끝났구나")}" : $"어? {now}!",
                Topic.Person => $"{Ko.IGa(CrewById(b.Id)?.Name ?? "?")} 여기 없네",
                Topic.Item => $"{Ko.IGa(ItemKinds.Name((ItemKind)b.Id))} 여기 없네",
                _ => $"{now}였구나",
            };
            if (c.IsAwake && c.SaidUntil < w.Tick) c.Say(w, Persona.Say(c, line));
            w.Log.Add(w.Tick, LogKind.Life, $"믿음을 고쳤다 — {book.LastCorrectionText}", c.Id);
        }
    }

    /// <summary>믿고 찾아간 자리에 없었다 (가는 길에 믿음이 이미 바뀌었어도 헛걸음은 헛걸음) — 고친 믿음으로 적는다.</summary>
    public void Missed(CrewMember c, Topic t, int id, int wrongValue)
    {
        var w = _w;
        if (Get(c, t, id) is Belief b && b.Value == wrongValue)
        {
            Learn(c, t, id, -1, BeliefSource.Seen, 0.9f);
            if (b.Value == -1 && Of(c).LastCorrection == w.Tick) return;
        }
        var book = Of(c);
        book.Corrected++;
        Corrections++;
        book.LastCorrection = w.Tick;
        book.LastCorrectionText = $"{Describe(new Belief { Topic = t, Id = id, Value = wrongValue })} 줄 알았는데 — 없었다";
        w.Log.Add(w.Tick, LogKind.Life, $"믿음을 고쳤다 — {book.LastCorrectionText}", c.Id);
    }

    /// <summary>믿음 한 줄을 사람 말로.</summary>
    public string Describe(Belief b)
    {
        string R(int id) => RoomById(id)?.Name ?? "어느 방";
        string P(int id) => CrewById(id)?.Name ?? "누군가";
        return b.Topic switch
        {
            Topic.Fire => b.Value == 1 ? $"{R(b.Id)}에 불" : $"{R(b.Id)} 불 없음",
            Topic.Breach => b.Value == 1 ? $"{R(b.Id)}에 구멍" : $"{R(b.Id)} 구멍 없음",
            Topic.Down => b.Value == 1 ? $"{P(b.Id)} 쓰러짐" : $"{P(b.Id)} 괜찮음",
            Topic.Dark => b.Value == 1 ? $"{R(b.Id)} 캄캄함" : $"{R(b.Id)} 불 켜짐",
            Topic.Air => b.Value == 1 ? $"{R(b.Id)} 공기 나쁨" : $"{R(b.Id)} 공기 괜찮음",
            Topic.Water => b.Value == 1 ? $"{R(b.Id)} 물 참" : $"{R(b.Id)} 바닥 마름",
            Topic.Person => b.Value >= 0 ? $"{P(b.Id)}{(b.Aux == 1 ? " 자는 중" : "")} — {R(b.Value)}" : $"{P(b.Id)} 어디 있는지 모름",
            Topic.Item => b.Value >= 0 ? $"{ItemKinds.Name((ItemKind)b.Id)} — {FurnitureById(b.Value)?.Room.Name ?? "?"} {FurnitureById(b.Value)?.Name ?? ""}{(b.Aux > 0 ? $" {b.Aux}개" : "")}" : $"{ItemKinds.Name((ItemKind)b.Id)} 없음",
            Topic.Omen => $"{FurnitureById(b.Id)?.Name ?? "설비"} 이상 기미",
            Topic.Cosmic => $"대재난 예보 #{b.Id}",
            Topic.Shelter => "대피소로 가라는 방송",
            Topic.Warn => $"{R(b.Id)} 위험 (컴퓨터 경고)",
            Topic.Outage => $"{R(b.Id)} 정전 — {OutageName((OutageCause)b.Value)}",
            Topic.Slip => b.Value == 1 ? $"{R(b.Id)} 바닥 미끄러움" : $"{R(b.Id)} 바닥 마름",
            Topic.Mix => b.Value == 1 ? $"{R(b.Id)} 위험한 조합 (컴퓨터 경고)" : $"{R(b.Id)} 조합 괜찮음",
            Topic.Forecast => ForecastText(b),
            Topic.Thing => _w.Info.DescribeThing(b), // v17.3 개인 물건 (어디 있나 · 누가 깼나)
            _ => "?",
        };
    }

    /// <summary>컴퓨터 예측 믿음 (Id = 자원 순번 · Value 1 = 부족 예보 · Aux = 며칠 뒤 × 10).</summary>
    private static string ForecastText(Belief b)
    {
        string name = b.Id >= 0 && b.Id < ShipForecast.Models.Count ? ShipForecast.Models[b.Id].Name : "자원";
        return b.Value == 1 ? $"{ShipForecast.When(b.Aux / 10f)} {name} 부족 (컴퓨터 예측)" : $"{name} 넉넉 (컴퓨터 예측)";
    }

    /// <summary>컴퓨터 예측을 들은 사람: 그 자원이 모자랄 거라고 믿나 (확신).</summary>
    public float ForecastBelief(CrewMember c, string key)
    {
        int i = ShipForecast.Models.FindIndex(m => m.Key == key);
        return i < 0 ? 0f : Conf(c, Topic.Forecast, i, 1);
    }

    public static string OutageName(OutageCause k) => k switch
    {
        OutageCause.Breaker => "차단기가 떨어졌다",
        OutageCause.Power => "발전 · 배터리 쪽",
        OutageCause.Lights => "조명 고장",
        OutageCause.Wiring => "전선이 끊겼다",
        _ => "까닭을 모름",
    };

    /// <summary>이 믿음이 지금 세계와 어긋나나 (화면 · 시험용 — 판단에는 쓰지 않는다).</summary>
    public bool Wrong(Belief b)
    {
        var w = _w;
        switch (b.Topic)
        {
            case Topic.Fire: return RoomById(b.Id) is Room r && (FireIn(r) > 0 ? 1 : 0) != b.Value;
            case Topic.Breach: return RoomById(b.Id) is Room r2 && (r2.Leaking ? 1 : 0) != b.Value;
            case Topic.Dark: return RoomById(b.Id) is Room r3 && (r3.Dark ? 1 : 0) != b.Value;
            case Topic.Down: return CrewById(b.Id) is CrewMember p && (p.Down && !p.Dead ? 1 : 0) != b.Value;
            case Topic.Person: return CrewById(b.Id) is CrewMember q && b.Value >= 0 && q.Room?.Id != b.Value;
            case Topic.Item: return b.Value >= 0 && FurnitureById(b.Value)?.Storage is Inventory inv && inv.Count((ItemKind)b.Id) <= 0;
            default: return false;
        }
    }

    // ─────────────────────────── 방이 안전하다고 믿나 (대피) ───────────────────────────

    /// <summary>대피할 방 고르기: 위험하다고 믿는 방(불 · 구멍 · 나쁜 공기 · 컴퓨터 경고)은 실제로 괜찮아도 피한다.</summary>
    public bool SafeEnough(CrewMember c, Room room)
    {
        if (!BrainSystem.Enabled || !_books.TryGetValue(c.Id, out var book)) return true;
        foreach (var t in Hazards)
            if (book.Map.TryGetValue(Key(t, room.Id), out var b) && b.Value == 1 && Eff(b) >= 0.4f) return false;
        return true;
    }

    private static readonly Topic[] Hazards = { Topic.Fire, Topic.Breach, Topic.Air, Topic.Warn };

    /// <summary>이 사람이 믿는 위험 (불 · 구멍 · 쓰러짐) — 확신이 높은 것부터.</summary>
    public List<Belief> Dangers(CrewMember c, float min = 0.35f)
    {
        var list = new List<Belief>();
        if (!_books.TryGetValue(c.Id, out var book)) return list;
        foreach (var b in book.Map.Values)
            if (b.Value == 1 && b.Topic is Topic.Fire or Topic.Breach or Topic.Down && Eff(b) >= min) list.Add(b);
        list.Sort((x, y) => Eff(y).CompareTo(Eff(x)) is int s && s != 0 ? s : x.Id.CompareTo(y.Id));
        return list;
    }

    /// <summary>그 사람이 어디 있다고 믿나 (모르면 null).</summary>
    public Room? WhereIs(CrewMember c, CrewMember who, out float conf)
    {
        conf = 0f;
        if (Get(c, Topic.Person, who.Id) is not Belief b || b.Value < 0) return null;
        conf = Eff(b);
        return RoomById(b.Value);
    }

    /// <summary>그 물건이 어느 보관함에 있다고 믿나 (모르면 null, 없다고 믿으면 null + none).</summary>
    public Furniture? WhereItem(CrewMember c, ItemKind k, out float conf, out bool none)
    {
        conf = 0f;
        none = false;
        if (Get(c, Topic.Item, (int)k) is not Belief b) return null;
        conf = Eff(b);
        if (b.Value < 0) { none = conf >= 0.3f; return null; }
        return FurnitureById(b.Value);
    }

    // ─────────────────────────── 보기 (관측) ───────────────────────────

    public int FireIn(Room r) => r.Id < _roomFire.Length ? _roomFire[r.Id] : _w.Fire.CountIn(r);

    /// <summary>그 자리에서 보이는 것을 믿음에 적는다: 내 방(불 · 어둠 · 공기 · 물 · 구멍 · 사람 · 관심 있는 물건) · 열린 문 너머 옆방의 불.</summary>
    public void Look(CrewMember c)
    {
        var w = _w;
        if (!BrainSystem.Enabled || c.Dead || !c.IsAwake || c.Outside || c.Room is not Room r) return;
        var book = Of(c);
        int fire = FireIn(r);
        Learn(c, Topic.Fire, r.Id, fire > 0 ? 1 : 0, BeliefSource.Seen, 1f);
        bool dark = r.Dark;
        var pd = Get(c, Topic.Dark, r.Id);
        int prevVal = pd?.Value ?? -1;
        long prevTick = pd?.Tick ?? -1;
        Learn(c, Topic.Dark, r.Id, dark ? 1 : 0, BeliefSource.Seen, 1f);
        // 불이 켜져 있던 방이 캄캄해졌다 (정전을 알아챔) — 원래 어두운 방을 지나가는 것과는 다르다
        if (dark && prevVal == 0 && w.Tick - prevTick < SimTime.Hours(3) && book.DarkSince < 0)
        {
            book.DarkSince = w.Tick;
            w.Brain2.Emotions.Feel(c, Feeling.Fear, 0.12f + (c.Fears.Contains(Fear.Dark) ? 0.45f : 0f), $"{r.Name} 정전");
            c.NextThinkTick = Math.Min(c.NextThinkTick, w.Tick + 1);
        }
        if (!dark && book.DarkSince >= 0 && !AnyDarkBelief(c)) { book.DarkSince = -1; book.DarkWide = false; }
        Learn(c, Topic.Air, r.Id, Atmosphere.Danger(r) > 0.3f ? 1 : 0, BeliefSource.Seen, 1f);
        Learn(c, Topic.Breach, r.Id, r.Leaking ? 1 : 0, BeliefSource.Seen, 1f);
        Learn(c, Topic.Water, r.Id, r.Flood > 40f ? 1 : 0, BeliefSource.Seen, 1f);
        // v16 통합 (배 본체): 미끄럽다고 믿던 바닥이 말랐으면 고친다 · 젖은 바닥을 보면 안다
        if (book.Map.TryGetValue(Key(Topic.Slip, r.Id), out var sb) && sb.Value == 1 && (sb.Src != BeliefSource.Seen || w.Tick - sb.Tick > SimTime.Minutes(30)))
            Learn(c, Topic.Slip, r.Id, w.Body.WetCells(r) >= 2 ? 1 : 0, BeliefSource.Seen, 0.9f);
        // 열린 문 너머 (불빛 · 연기)
        foreach (var d in r.Doors)
        {
            if (d.Openness < 0.3f) continue;
            var o = d.RoomA == r ? d.RoomB : d.RoomA;
            if (o == null || o.Detached) continue;
            Learn(c, Topic.Fire, o.Id, FireIn(o) > 0 ? 1 : 0, BeliefSource.Seen, 0.85f);
        }
        // 사람: 여기 있는 사람 · 여기 있을 줄 알았는데 없는 사람
        var here = r.Id < _roomCrew.Count ? _roomCrew[r.Id] : null;
        if (here != null)
            foreach (var p in here)
            {
                if (p == c || p.Dead) continue;
                Learn(c, Topic.Person, p.Id, r.Id, BeliefSource.Seen, 1f, -1, p.Pose == Pose.Sleeping ? 1 : 0);
                if (fire > 0 && p.IsAwake) book.Shared.Add((Key(Topic.Fire, r.Id), p.Id)); // 같이 봤다 — 저 사람도 안다
            }
        List<Belief>? gone = null;
        foreach (var b in book.Map.Values)
        {
            if (b.Topic == Topic.Person && b.Value == r.Id && (here == null || !here.Any(p => p.Id == b.Id)) && Eff(b) >= 0.3f) (gone ??= new()).Add(b);
            else if (b.Topic == Topic.Item && b.Value >= 0 && FurnitureById(b.Value) is Furniture f && f.Room == r) (gone ??= new()).Add(b);
            else if (b.Topic == Topic.Down && b.Value == 1 && CrewById(b.Id) is CrewMember dp && (here?.Contains(dp) == true || dp.Room == r) && !(dp.Down && !dp.Dead)) (gone ??= new()).Add(b);
        }
        if (gone != null)
            foreach (var b in gone)
            {
                if (b.Topic == Topic.Person) Learn(c, Topic.Person, b.Id, -1, BeliefSource.Seen, 0.9f);
                else if (b.Topic == Topic.Down) Learn(c, Topic.Down, b.Id, 0, BeliefSource.Seen, 1f);
                else if (FurnitureById(b.Value) is Furniture box && box.Storage != null)
                {
                    int n = box.Storage.Count((ItemKind)b.Id);
                    Learn(c, Topic.Item, b.Id, n > 0 ? box.Id : -1, BeliefSource.Seen, 1f, -1, n);
                }
            }
        // 관심 있는 물건 (계획에 필요한 부품 · 재료): 이 방 보관함을 훑는다
        var want = w.Brain2.Plans.Interest(c);
        if (want.Count > 0)
            foreach (var f in r.Furniture)
            {
                if (f.Storage == null || f.Stowed) continue;
                foreach (var k in want)
                {
                    int n = f.Storage.Count(k);
                    if (n > 0) Learn(c, Topic.Item, (int)k, f.Id, BeliefSource.Seen, 1f, -1, n);
                }
            }
    }

    /// <summary>불이 꺼졌다 — 정전을 알아챈다 (두려움 · 다시 생각).</summary>
    private void Noticed(CrewMember c, Room r, bool wide)
    {
        var w = _w;
        if (c.Dead || !c.IsAwake || c.Outside) return;
        Learn(c, Topic.Dark, r.Id, 1, BeliefSource.Seen, 1f);
        var book = Of(c);
        if (book.DarkSince >= 0) { book.DarkWide |= wide; return; }
        book.DarkSince = w.Tick;
        book.DarkWide = wide;
        w.Brain2.Emotions.Feel(c, Feeling.Fear, 0.12f + (c.Fears.Contains(Fear.Dark) ? 0.45f : 0f), $"{r.Name} 정전");
        c.NextThinkTick = Math.Min(c.NextThinkTick, w.Tick + 1);
    }

    private bool AnyDarkBelief(CrewMember c)
    {
        if (!_books.TryGetValue(c.Id, out var book)) return false;
        foreach (var b in book.Map.Values) if (b.Topic == Topic.Dark && b.Value == 1 && Eff(b) > 0.5f && b.Src == BeliefSource.Seen && _w.Tick - b.Tick < SimTime.Minutes(20)) return true;
        return false;
    }

    // ─────────────────────────── 시스템 틱 ───────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        if (!BrainSystem.Enabled) return;
        // 방마다 불 칸 수 · 사람 (한 번만 센다)
        int rooms = w.Ship.Rooms.Count;
        if (_roomFire.Length < rooms) _roomFire = new int[rooms + 8];
        Array.Clear(_roomFire);
        if (w.Fire.Count > 0)
            foreach (var cell in w.Fire.Fires.Keys)
                if (w.Ship.RoomAt(cell) is Room fr && fr.Id < _roomFire.Length) _roomFire[fr.Id]++;
        while (_roomCrew.Count < rooms) _roomCrew.Add(new List<CrewMember>());
        foreach (var l in _roomCrew) l.Clear();
        foreach (var c in w.Crew)
            if (!c.Dead && !c.Away && c.Room is Room cr && cr.Id < _roomCrew.Count) _roomCrew[cr.Id].Add(c);

        // 방에 있던 사람은 불이 꺼지는 순간을 안다 (정전을 알아챔)
        // 여러 방이 한꺼번에 꺼지면 (환기팬이 멎고 배가 조용해진다) 큰 정전인 줄 안다
        if (_wasDark.Length < rooms) Array.Resize(ref _wasDark, rooms + 8);
        _newDark.Clear();
        foreach (var r in w.Ship.Rooms)
        {
            if (r.Id >= _wasDark.Length) continue;
            bool d = r.Dark && !r.Detached;
            if (d && !_wasDark[r.Id] && _darkInit) _newDark.Add(r);
            _wasDark[r.Id] = d;
        }
        foreach (var r in _newDark)
        {
            if (r.Id < _roomCrew.Count) foreach (var c in _roomCrew[r.Id]) Noticed(c, r, _newDark.Count >= 2);
            // 문 너머 옆방 사람도 그 방이 꺼진 걸 본다
            foreach (var d in r.Doors)
            {
                var o = d.RoomA == r ? d.RoomB : d.RoomA;
                if (o == null || o.Id >= _roomCrew.Count || d.Openness < 0.3f && !_newDark.Contains(o)) continue;
                foreach (var c in _roomCrew[o.Id]) if (c.IsAwake && !c.Dead) Learn(c, Topic.Dark, r.Id, 1, BeliefSource.Seen, 0.85f);
            }
        }
        _darkInit = true;

        ImportIncidents();
        ImportBroadcasts();
        ImportOverheard();
        if (_phase % 4 == 0) { ImportCosmic(); ImportOmens(); }
        ComputerWatch();

        // 나눠 보기: 한 번에 4분의 1씩 (사람마다 약 2.4분마다 둘러본다)
        foreach (var c in w.Crew)
            if (((c.Id + _phase) & 3) == 0) Look(c);
        _phase++;

        if (w.Tick >= _nextPrune)
        {
            _nextPrune = w.Tick + SimTime.Hours(6);
            foreach (var book in _books.Values)
            {
                List<long>? drop = null;
                foreach (var (k, b) in book.Map) if (Eff(b) < 0.04f) (drop ??= new()).Add(k);
                if (drop != null) foreach (var k in drop) book.Map.Remove(k);
                if (book.Shared.Count > 400) book.Shared.Clear();
            }
        }
    }

    /// <summary>사고가 끝났다: 경보가 닿는 방에서 깨어 있던 사람은 "해제"를 듣는다 — 못 들은 사람은 옛 믿음을 그대로 지닌다.</summary>
    private void ImportIncidents()
    {
        var w = _w;
        var now = w.Minds.Incidents();
        // 경보가 함께 울린 위험은 표시해 둔다 (다들 들었을 것 — 알리러 가지 않는다)
        foreach (var inc in now)
        {
            if (!inc.alarm || !TryParse(inc.key, out var t, out int id)) continue;
            foreach (var c in w.Crew)
                if (!c.Dead && c.IsAwake && w.Minds.AlarmReaches(c.Room) && Get(c, t, id) is Belief b && b.Value == 1) b.Alarmed = true;
        }
        foreach (var old in _prevInc)
        {
            if (now.Any(i => i.key == old.key)) continue;
            if (!TryParse(old.key, out var t, out int id) || !old.alarm) continue;
            AllClears++;
            foreach (var c in w.Crew)
                if (!c.Dead && c.IsAwake && !c.Outside && w.Minds.AlarmReaches(c.Room) && Get(c, t, id) is Belief b && b.Value == 1)
                    Learn(c, t, id, 0, BeliefSource.Alarm, 0.8f);
        }
        _prevInc.Clear();
        foreach (var inc in now) _prevInc.Add((inc.key, inc.room, inc.alarm));
    }

    /// <summary>방송을 들은 사람만: 그 방의 위험 · 대피 지시를 믿는다 — 컴퓨터를 믿는 만큼.</summary>
    private void ImportBroadcasts()
    {
        var w = _w;
        if (!w.Automation.Present) return;
        var pa = w.Automation.Speak;
        foreach (var bc in pa.Recent)
        {
            if (bc.Id <= _lastBroadcast) continue;
            _lastBroadcast = bc.Id;
            Imports++;
            var room = bc.RoomId >= 0 ? RoomById(bc.RoomId) : null;
            foreach (int id in bc.HeardBy)
            {
                if (CrewById(id) is not CrewMember c || c.Dead) continue;
                float conf = 0.3f + 0.7f * w.Automation.Trusts.Of(c);
                Of(c).Heard++;
                if (bc.Order == "shelter") Learn(c, Topic.Shelter, bc.RoomId, 1, BeliefSource.Broadcast, conf, -2);
                if (room == null) continue;
                if (FireIn(room) > 0) Learn(c, Topic.Fire, room.Id, 1, BeliefSource.Broadcast, conf, -2);
                if (room.Leaking) Learn(c, Topic.Breach, room.Id, 1, BeliefSource.Broadcast, conf, -2);
                if (bc.Priority >= 2 && bc.Order != "shelter") Learn(c, Topic.Warn, room.Id, 1, BeliefSource.Broadcast, conf, -2);
                if (room.Dark && bc.Text.Contains("정전")) Learn(c, Topic.Dark, room.Id, 1, BeliefSource.Broadcast, conf, -2);
            }
        }
    }

    /// <summary>칸막이 너머로 엿들은 말: 말한 두 사람이 그 방에 있다 (엿들음).</summary>
    private void ImportOverheard()
    {
        var w = _w;
        long top = _heardTick;
        foreach (var h in w.Body.Heard)
        {
            if (h.Tick <= _heardTick) continue;
            top = Math.Max(top, h.Tick);
            if (CrewById(h.Listener) is not CrewMember l) continue;
            Learn(l, Topic.Person, h.A, h.FromRoom, BeliefSource.Overheard, 0.7f);
            Learn(l, Topic.Person, h.B, h.FromRoom, BeliefSource.Overheard, 0.7f);
            Imports++;
        }
        _heardTick = top;
    }

    /// <summary>우주 대재난 예보를 아는 사람 (Cosmic.Knows) — 헛예보(Ghost)면 틀린 믿음이 된다.</summary>
    private void ImportCosmic()
    {
        var w = _w;
        foreach (var e in w.Cosmic.Events)
        {
            if (e.Phase == CosmicPhase.Done) continue;
            foreach (var c in w.Crew)
            {
                if (c.Dead || Get(c, Topic.Cosmic, e.Id) != null) continue;
                if (w.Cosmic.Knowing(c, e) is not { } k) continue;
                var src = k.src switch { KnowSource.Seen => BeliefSource.Seen, KnowSource.Alarm => BeliefSource.Broadcast, KnowSource.Radio => BeliefSource.Radio, _ => BeliefSource.Rumor };
                Learn(c, Topic.Cosmic, e.Id, 1, src, src == BeliefSource.Rumor ? 0.6f : 0.85f);
                Imports++;
            }
        }
    }

    /// <summary>당직 쪽지 · 고장 기미 소문 (Watch): 본 사람은 본 것으로, 들은 사람은 소문으로.</summary>
    private void ImportOmens()
    {
        var w = _w;
        foreach (var n in w.Watch.OpenNotes)
        {
            int fid = n.Machine.Body.Id;
            foreach (var (id, saw) in n.Holders)
            {
                if (CrewById(id) is not CrewMember c || c.Dead || Get(c, Topic.Omen, fid) != null) continue;
                Learn(c, Topic.Omen, fid, 1, saw ? BeliefSource.Seen : BeliefSource.Rumor, saw ? 0.9f : 0.6f);
            }
        }
    }

    // ─────────────────────────── 주 컴퓨터와 잇기 ───────────────────────────

    /// <summary>
    /// 주 컴퓨터가 짐작하는 이 사람의 믿음 — 마음을 읽지는 못한다: 방송을 들었나(들은 사람 목록 · 신뢰) · 카메라에 그 자리에서 본 게 찍혔나만으로.
    /// </summary>
    public (float conf, string why) ComputerGuess(CrewMember c, Topic t, int id)
    {
        var w = _w;
        var a = w.Automation;
        if (!a.Present) return (0f, "컴퓨터 없음");
        long k = Key(t, id);
        if (_compSaw.TryGetValue((c.Id, k), out long saw) && w.Tick - saw < SimTime.Hours(4)) return (0.9f, "카메라에 그 자리가 찍혔다");
        var room = RoomById(id);
        for (int i = a.Speak.Recent.Count - 1; i >= 0; i--)
        {
            var bc = a.Speak.Recent[i];
            if (w.Tick - bc.Tick > SimTime.Hours(4)) break;
            if (room != null && bc.RoomId != room.Id) continue;
            if (bc.HeardBy.Contains(c.Id)) return (0.3f + 0.7f * a.Trusts.Of(c), $"방송을 들었다 (신뢰 {a.Trusts.Of(c) * 100f:0}%)");
            if (bc.Missed.Contains(c.Id)) return (0.1f, "방송을 못 들었다");
        }
        return (0.15f, "알릴 길이 없었다");
    }

    /// <summary>주 컴퓨터가 보기에 이 위험을 모를 것 같은 사람 (짐작 0.4 미만).</summary>
    public List<CrewMember> ComputerThinksUnaware(Topic t, int id) =>
        _w.Crew.Where(c => !c.Dead && !c.Away && !c.IsChild && ComputerGuess(c, t, id).conf < 0.4f).ToList();

    /// <summary>
    /// 주 컴퓨터의 눈: 카메라(데이터선)가 닿는 방에서 누가 불을 봤는지 적어 두고(짐작의 재료),
    /// 이미 꺼진 방으로 소화기를 들고 가는 사람에게는 손목 단말로 "꺼졌다"고 알린다 — 믿는 만큼 듣는다.
    /// </summary>
    private void ComputerWatch()
    {
        var w = _w;
        var a = w.Automation;
        if (!a.Present || !a.MainOnline) return;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Room is not Room r || !r.DataLinked) continue;
            if (FireIn(r) > 0) _compSaw[(c.Id, Key(Topic.Fire, r.Id))] = w.Tick;
        }
        if (w.Tick < _nextNudge) return;
        _nextNudge = w.Tick + SimTime.Minutes(2);
        foreach (var c in w.Crew)
        {
            if (c.Dead || !c.CanAct || c.Job?.Activity is not PlanActivity || w.Brain2.Plans.Current(c) is not CrewPlan p || p.Kind != PlanKind.CheckFire) continue;
            if (p.Room is not Room tr || !tr.DataLinked || FireIn(tr) > 0 || w.Fire.IsKnown(tr)) continue;
            if (Get(c, Topic.Fire, tr.Id) is not Belief b || b.Value != 1 || b.Src == BeliefSource.Computer || p.Nudged) continue;
            p.Nudged = true; // 통합7 한 번 말하고 만다 — 2분마다 거듭 알려 결국 못 믿는 사람(신뢰 8%)까지 돌려세우던 것
            Nudges++;
            float trust = a.Trusts.Of(c);
            w.Log.Add(w.Tick, LogKind.Ship, $"[손목 단말] {a.Voice.Call} → {c.Name}: {tr.Name}에는 불이 없다 (감지기 · 카메라)", c.Id);
            var after = Learn(c, Topic.Fire, tr.Id, 0, BeliefSource.Computer, 0.25f + 0.75f * trust, -2);
            if (after != null && after.Value == 0)
            {
                NudgesHeeded++;
                if (c.SaidUntil < w.Tick) c.Say(w, Persona.Say(c, "컴퓨터가 불은 없다네 — 돌아가자"));
                w.Brain2.Plans.Finish(c, p, true, "컴퓨터가 불은 없다고 했다");
                c.Interrupt(w);
            }
            else if (c.SaidUntil < w.Tick) c.Say(w, Persona.Say(c, "컴퓨터 말을 어떻게 믿어. 내 눈으로 볼래"));
        }
    }

    // ─────────────────────────── 도우미 ───────────────────────────

    public Room? RoomById(int id)
    {
        var rooms = _w.Ship.Rooms;
        if (id >= 0 && id < rooms.Count && rooms[id].Id == id) return rooms[id];
        foreach (var r in rooms) if (r.Id == id) return r;
        return null;
    }

    public CrewMember? CrewById(int id)
    {
        var crew = _w.Crew;
        if (id >= 0 && id < crew.Count && crew[id].Id == id) return crew[id];
        foreach (var c in crew) if (c.Id == id) return c;
        return null;
    }

    private Furniture? _lastF;
    public Furniture? FurnitureById(int id)
    {
        if (_lastF != null && _lastF.Id == id) return _lastF;
        foreach (var f in _w.Ship.Furniture) if (f.Id == id) return _lastF = f;
        return null;
    }

    /// <summary>지문 (결정론 점검).</summary>
    public long Hash()
    {
        long h = 17;
        foreach (var (id, book) in _books)
        {
            h = h * 31 + id;
            h = h * 31 + book.Map.Count;
            h = h * 31 + book.Corrected;
            foreach (var b in book.Map.Values) h = h * 31 + b.Value * 7 + (int)b.Topic + b.Id * 13 + (int)(b.Conf * 1000);
        }
        return h;
    }
}

/// <summary>믿음 하나 (주제 · 대상 · 값) — `Beliefs.Learn(w, c, 사실, 출처, 확신)`에 넘기는 꾸러미.</summary>
public readonly record struct Fact(Topic Topic, int Id, int Value)
{
    public static Fact FireIn(Room r, bool on = true) => new(Topic.Fire, r.Id, on ? 1 : 0);
    public static Fact PersonIn(CrewMember p, Room? r) => new(Topic.Person, p.Id, r?.Id ?? -1);
    public static Fact ItemAt(ItemKind k, Furniture? f) => new(Topic.Item, (int)k, f?.Id ?? -1);
    public static Fact DarkIn(Room r, bool dark = true) => new(Topic.Dark, r.Id, dark ? 1 : 0);
}

/// <summary>믿음 공통 API (어느 시스템에서든 한 줄로): 배우기 · 믿나 · 확신.</summary>
public static class Beliefs
{
    public static Belief? Learn(World w, CrewMember c, Fact f, BeliefSource src, float conf, int from = -1) => w.Brain2.Beliefs.Learn(c, f, src, conf, from);
    public static bool Believes(World w, CrewMember c, Fact f, float min = 0.35f) => w.Brain2.Beliefs.Believes(c, f.Topic, f.Id, f.Value, min);
    public static float Conf(World w, CrewMember c, Fact f) => w.Brain2.Beliefs.Conf(c, f.Topic, f.Id, f.Value);
}
