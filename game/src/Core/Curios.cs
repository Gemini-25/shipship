using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v17.9 숨은 것 · 드문 것 · 도감 — 긴 항해에서 매번 처음 보는 것이 나오게 (표: CurioTable).
//  · 숨은 물건 · 낙서 · 전 승무원 흔적: 처음에 몇 개를 배 곳곳에 숨기고, 며칠마다 하나씩 더 "나온다"(환기구에서 굴러 나옴 · 패널을 뜯다).
//    청소 · 점검 · 수리하던 사람이 곁에서 찾는다 → 기쁨 · 일지 · 곁의 사람에게 보여 준다(가까워짐) · 도감.
//  · 창밖에 지나가는 것: 하루 이틀에 한 번. 센서가 살아 있으면 주 컴퓨터가 먼저 보고 "창으로 오라"고 방송 → 쉬던 사람이 창가로 모인다
//    → 함께 본 사람끼리 가까워지고 첫 사람이 사진을 남긴다. 컴퓨터가 없으면 창가에 우연히 있던 사람만 본다.
//  · 아주 드문 이상 현상: 방의 상태가 맞을 때만(눅눅함 → 떠오르는 물방울 · 추위 → 서리 · 정전 → 남은 빛 · 밤 복도 → 발소리 …).
//    본 사람은 겁먹고(용감한 사람은 덜) · 컴퓨터는 센서를 뒤져 "까닭을 못 찾음"을 남기고, 겁먹은 사람이 많으면 안심시키는 방송을 한다.
//  · 숨은 재능: 사람마다 하나. 맞는 때(좋아하는 노래 · 식사 자리 · 밤 창가 · 수리 · 위기 · 심심함 · 창밖 구경)에 드러난다 → 본 사람들이 가까워진다.
//  · 비밀: 몇 사람에게. 같은 방에서 밤을 보내는 사람 · 관련 흔적 · 컴퓨터의 재고 점검으로 드러난다 → 지켜 주면 고마워하고, 퍼뜨리면 틀어진다.
// 그림은 화면(ShipViewCurios · HudCollection)이 표의 벡터 그림으로 그린다 — 항목마다 다르다.

/// <summary>배 안에 놓인 숨은 물건 · 낙서 · 흔적 하나.</summary>
public sealed class CurioPlaced
{
    public string Key { get; init; } = "";
    public int Room { get; init; }
    public Cell At { get; init; }
    public long Since { get; init; }
    public bool Found { get; set; }
    public int FoundBy { get; set; } = -1;
    public long FoundAt { get; set; } = -1;
}

/// <summary>지금 벌어지는 드문 일 (창밖 · 이상 현상).</summary>
public sealed class CurioEvent
{
    public string Key { get; init; } = "";
    public int Room { get; init; } = -1;
    public long Start { get; init; }
    public long End { get; init; }
    public bool Announced { get; set; }
    public List<int> Seen { get; } = new();
    public int Scared { get; set; }
    public bool Explained { get; set; }
    public float Progress(long now) => Math.Clamp((now - Start) / (float)Math.Max(1, End - Start), 0f, 1f);
}

/// <summary>도감에 남은 한 줄.</summary>
public sealed record CurioFind(string Key, long Tick, int By, int Room, string Note);

public sealed class CurioStats
{
    public int Placed, Arrived, Found, Shown, Passings, Watched, Gathered, Announced, Anomalies, Frightened, ComputerNotes, Calmed, Talents, Secrets, Kept, Told, Audits;
    public string Summary() =>
        $"숨김 {Placed}(새로 나옴 {Arrived}) · 찾음 {Found} · 보여 줌 {Shown} · 창밖 {Passings}(본 사람 {Watched} · 창가로 모임 {Gathered} · 방송 {Announced}) · 이상 현상 {Anomalies}(겁먹음 {Frightened} · 컴퓨터 기록 {ComputerNotes} · 안심 방송 {Calmed}) · " +
        $"드러난 재능 {Talents} · 드러난 비밀 {Secrets}(지켜 줌 {Kept} · 퍼뜨림 {Told}) · 재고 점검 {Audits}";
}

public sealed class CurioSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6007 + 2903));
    private bool _init;
    private long _next, _nextArrival, _nextPassing, _nextAnomaly, _nextAudit;
    private readonly Dictionary<int, string> _talent = new(), _secret = new();
    private readonly HashSet<int> _talentShown = new(), _secretShown = new();
    private readonly HashSet<int> _rationFlagged = new();

    public List<CurioPlaced> Placed { get; } = new();
    public List<CurioEvent> Active { get; } = new();
    public List<CurioFind> Finds { get; } = new();
    public HashSet<string> Seen { get; } = new();
    public CurioStats Stats { get; } = new();

    public CurioSystem(World w) => _w = w;

    public string? TalentOf(CrewMember c) => _talent.TryGetValue(c.Id, out var k) ? k : null;
    public string? SecretOf(CrewMember c) => _secret.TryGetValue(c.Id, out var k) ? k : null;
    public bool TalentKnown(CrewMember c) => _talentShown.Contains(c.Id);
    public bool SecretKnown(CrewMember c) => _secretShown.Contains(c.Id);
    public CurioEvent? Passing => Active.FirstOrDefault(e => CurioTable.Of(e.Key).Kind == CurioKind.Passing);

    /// <summary>창이 있는 방 (밖이 보인다).</summary>
    public static bool HasWindow(Room r) => r.Type is RoomType.Observatory or RoomType.Bridge or RoomType.Lounge or RoomType.Mess or RoomType.Comms or RoomType.Hydroponics;

    // ── 처음: 사람마다 재능 · 몇 사람에게 비밀 · 숨은 물건 몇 개 ──

    private void Init()
    {
        _init = true;
        var w = _w;
        var talents = CurioTable.Of(CurioKind.Talent).ToList();
        var secrets = CurioTable.Of(CurioKind.Secret).ToList();
        foreach (var c in w.Crew)
        {
            _talent[c.Id] = talents[(int)((uint)(c.Id * 2654435761u + (uint)w.Seed * 40503u) >> 7) % talents.Count].Key;
            if (R.Chance(0.4f)) _secret[c.Id] = R.Pick(secrets).Key;
        }
        for (int i = 0; i < 8; i++) PlaceNew(false);
        long now = w.Tick;
        _nextArrival = now + SimTime.Hours(R.Range(36f, 72f));
        _nextPassing = now + SimTime.Hours(R.Range(10f, 40f));
        _nextAnomaly = now + SimTime.Hours(R.Range(30f, 80f));
        _nextAudit = (now / SimTime.TicksPerDay + 1) * SimTime.TicksPerDay + SimTime.Hours(6);
    }

    /// <summary>아직 못 본 것을 먼저 (모두 봤으면 드물게 다시) — 드문 것일수록 덜 뽑힌다.</summary>
    private CurioSpec? PickUnseen(IEnumerable<CurioSpec> pool, Func<CurioSpec, bool>? fits = null)
    {
        var list = pool.Where(s => fits == null || fits(s)).ToList();
        if (list.Count == 0) return null;
        var fresh = list.Where(s => !Seen.Contains(s.Key) && !Placed.Any(p => p.Key == s.Key)).ToList();
        var from = fresh.Count > 0 ? fresh : R.Chance(0.35f) ? list : null;
        if (from == null) return null;
        float total = from.Sum(s => s.Rarity), roll = R.Float() * total;
        foreach (var s in from) { roll -= s.Rarity; if (roll <= 0f) return s; }
        return from[^1];
    }

    private CurioPlaced? PlaceNew(bool arrival)
    {
        var w = _w;
        var spec = PickUnseen(CurioTable.All.Where(s => s.Kind is CurioKind.Hidden or CurioKind.Graffiti or CurioKind.Trace));
        if (spec == null) return null;
        var rooms = w.Ship.LiveRooms.Where(r => !r.OffLimits && (spec.Where.Length == 0 || spec.Where.Contains(r.Type))).ToList();
        if (rooms.Count == 0) rooms = w.Ship.LiveRooms.Where(r => !r.OffLimits && r.Type != RoomType.Corridor).ToList();
        if (rooms.Count == 0) return null;
        var room = R.Pick(rooms);
        var cells = room.Cells.Where(w.Ship.IsWalkable).ToList();
        if (cells.Count == 0) return null;
        var p = new CurioPlaced { Key = spec.Key, Room = room.Id, At = R.Pick(cells), Since = w.Tick };
        Placed.Add(p);
        Stats.Placed++;
        if (arrival) Stats.Arrived++;
        return p;
    }

    // ── 틱 ──

    public void Update(float dt)
    {
        var w = _w;
        if (w.Tick < _next) return;
        _next = w.Tick + SimTime.Minutes(1);
        if (!_init) Init();
        long now = w.Tick;
        if (now >= _nextArrival) { _nextArrival = now + SimTime.Hours(R.Range(36f, 80f)); PlaceNew(true); }
        if (now >= _nextPassing) { _nextPassing = now + SimTime.Hours(R.Range(18f, 54f)); StartPassing(); }
        if (now >= _nextAnomaly) { _nextAnomaly = now + SimTime.Hours(R.Range(40f, 110f)); if (!StartAnomaly()) _nextAnomaly = now + SimTime.Hours(6); }
        if (now >= _nextAudit) { _nextAudit += SimTime.TicksPerDay; Audit(); }
        // 방마다 깨어 있는 사람
        var byRoom = new Dictionary<int, List<CrewMember>>();
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away || c.Room is not Room r) continue;
            if (!byRoom.TryGetValue(r.Id, out var l)) byRoom[r.Id] = l = new List<CrewMember>();
            l.Add(c);
        }
        Discover(byRoom);
        Events(byRoom);
        Cues(byRoom);
        Secrets(byRoom);
    }

    // ── 찾기 ──

    private void Discover(Dictionary<int, List<CrewMember>> byRoom)
    {
        var w = _w;
        foreach (var p in Placed)
        {
            if (p.Found || !byRoom.TryGetValue(p.Room, out var here)) continue;
            foreach (var c in here)
            {
                if (!c.IsAwake || (c.Position - p.At.Center).Length() > 2.5f) continue;
                var act = c.Job?.Activity;
                float chance = act is ChoresActivity or InspectActivity or MendActivity or TidyActivity ? 0.05f : 0.006f;
                if (w.Ship.Rooms[p.Room].Dark) chance *= 0.3f;
                if (!R.Chance(chance)) continue;
                Found(p, c, here);
                break;
            }
        }
    }

    private void Found(CurioPlaced p, CrewMember c, List<CrewMember> here)
    {
        var w = _w;
        var spec = CurioTable.Of(p.Key);
        p.Found = true; p.FoundBy = c.Id; p.FoundAt = w.Tick;
        Stats.Found++;
        Record(spec, c.Id, p.Room, spec.Line);
        w.Log.Add(w.Tick, LogKind.Life, spec.Line, c.Id);
        w.Brain2.Emotions.Feel(c, Feeling.Joy, 0.25f, $"{Ko.EulReul(spec.Name)} 찾았다");
        // 곁의 사람에게 보여 준다 → 가까워진다
        foreach (var o in here)
        {
            if (o == c || !o.IsAwake || (o.Position - c.Position).Length() > 4f) continue;
            c.ChangeAffinity(o, 0.03f); o.ChangeAffinity(c, 0.03f);
            Stats.Shown++;
        }
        // 전 승무원의 흔적 → 그 사람의 가족이 이 배에 있으면 비밀이 드러난다
        if (spec.Kind == CurioKind.Trace)
            foreach (var o in w.Crew)
                if (!o.Dead && SecretOf(o) == "s_relative" && !SecretKnown(o)) { Reveal(o, c, "흔적을 보고 털어놓았다"); break; }
    }

    private void Record(CurioSpec spec, int by, int room, string note)
    {
        bool fresh = Seen.Add(spec.Key);
        Finds.Add(new CurioFind(spec.Key, _w.Tick, by, room, note));
        if (Finds.Count > 400) Finds.RemoveAt(0);
        if (fresh && spec.Shelf != CodexShelf.People) _w.Log.Add(_w.Tick, LogKind.Ship, $"{spec.Name} — {CurioTable.ShelfName(spec.Shelf)} 모음에 넣었다");
    }

    // ── 창밖 · 이상 현상 ──

    private void StartPassing()
    {
        var w = _w;
        var spec = PickUnseen(CurioTable.Of(CurioKind.Passing));
        if (spec == null) return;
        var e = new CurioEvent { Key = spec.Key, Start = w.Tick, End = w.Tick + SimTime.Minutes(R.Range(40f, 90f)) };
        Active.Add(e);
        Stats.Passings++;
        // 주 컴퓨터: 센서가 먼저 본다 → 창으로 오라고
        var a = w.Automation;
        if (a.Present && a.CoreOnline && w.Sensors.Online && w.Music.Mood < MusicMood.Tension)
        {
            var win = w.Ship.LiveRooms.Where(HasWindow).OrderBy(r => r.Type == RoomType.Observatory ? 0 : r.Type == RoomType.Lounge ? 1 : 2).FirstOrDefault();
            if (win != null && a.Speak.Announce($"{win.Name} 창밖에 {spec.Name} — 한 시간쯤 보입니다. 쉬는 분은 와서 보세요", win, 0) != null)
            {
                e.Announced = true;
                Stats.Announced++;
            }
        }
    }

    private bool StartAnomaly()
    {
        var w = _w;
        bool night = SimTime.HourOfDay(w.Tick) is >= 23f or < 5f;
        Room? where = null;
        bool Fits(CurioSpec s)
        {
            where = null;
            var rooms = w.Ship.LiveRooms.Where(r => !r.OffLimits && (s.Where.Length == 0 || s.Where.Contains(r.Type))).ToList();
            Func<Room, bool> ok = s.Key switch
            {
                "floating_drops" => r => r.Humidity > 0.55f,
                "frost_fern" => r => r.Air.Temperature < 16f,
                "afterglow" => r => r.Dark,
                "empty_steps" => r => night,
                "lone_door" => r => r.Doors.Count > 0,
                _ => r => true,
            };
            var fit = rooms.Where(ok).ToList();
            if (fit.Count == 0) return false;
            where = R.Pick(fit);
            return true;
        }
        var spec = PickUnseen(CurioTable.Of(CurioKind.Anomaly), Fits);
        if (spec == null || !Fits(spec) || where == null) return false;
        Active.Add(new CurioEvent { Key = spec.Key, Room = where.Id, Start = w.Tick, End = w.Tick + SimTime.Minutes(R.Range(10f, 40f)) });
        Stats.Anomalies++;
        return true;
    }

    private void Events(Dictionary<int, List<CrewMember>> byRoom)
    {
        var w = _w;
        var a = w.Automation;
        foreach (var e in Active.ToList())
        {
            var spec = CurioTable.Of(e.Key);
            if (w.Tick >= e.End)
            {
                Active.Remove(e);
                if (spec.Kind == CurioKind.Anomaly && !Seen.Contains(spec.Key) && a.Present && a.CoreOnline && e.Room >= 0)
                {
                    // 아무도 못 봤어도 컴퓨터 센서에 남는다
                    Record(spec, -1, e.Room, $"{w.Ship.Rooms[e.Room].Name} 센서 기록 — {spec.Line}");
                    Stats.ComputerNotes++;
                }
                continue;
            }
            if (spec.Kind == CurioKind.Passing)
            {
                foreach (var (rid, here) in byRoom)
                {
                    if (!HasWindow(w.Ship.Rooms[rid])) continue;
                    foreach (var c in here)
                    {
                        if (!c.IsAwake || e.Seen.Contains(c.Id) || !R.Chance(e.Announced ? 0.3f : 0.08f)) continue;
                        e.Seen.Add(c.Id);
                        Stats.Watched++;
                        w.Brain2.Emotions.Feel(c, Feeling.Joy, 0.2f, $"창밖에 {spec.Name}");
                        c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.05f);
                        if (e.Seen.Count == 1) { Record(spec, c.Id, rid, $"{c.Name}의 사진 — {spec.Line}"); w.Log.Add(w.Tick, LogKind.Life, $"창밖 {Ko.EulReul(spec.Name)} 사진으로 남겼다", c.Id); }
                        foreach (var oid in e.Seen)
                        {
                            if (oid == c.Id || w.Crew.FirstOrDefault(x => x.Id == oid) is not CrewMember o || o.Room != c.Room) continue;
                            c.ChangeAffinity(o, 0.02f); o.ChangeAffinity(c, 0.02f);
                        }
                        if (TalentOf(c) == "t_stars" && !TalentKnown(c) && here.Count > 1) ShowTalent(c, here);
                    }
                }
                continue;
            }
            // 이상 현상: 그 방 사람들
            if (!byRoom.TryGetValue(e.Room, out var inRoom)) continue;
            foreach (var c in inRoom)
            {
                if (!c.IsAwake || e.Seen.Contains(c.Id)) continue;
                e.Seen.Add(c.Id);
                if (e.Seen.Count == 1) { Record(spec, c.Id, e.Room, spec.Line); w.Log.Add(w.Tick, LogKind.Life, spec.Line, c.Id); }
                float fear = 0.35f * (1.2f - c.Traits.Bravery);
                if (fear > 0.15f)
                {
                    e.Scared++;
                    Stats.Frightened++;
                    w.Brain2.Emotions.Feel(c, Feeling.Fear, fear, spec.Name);
                    c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + fear * 0.2f);
                }
            }
            // 컴퓨터: 센서를 뒤진다 → 까닭을 못 찾으면 기록 · 겁먹은 사람이 둘 넘으면 안심 방송
            if (!e.Explained && e.Seen.Count > 0 && a.Present && a.CoreOnline)
            {
                e.Explained = true;
                var room = w.Ship.Rooms[e.Room];
                if (a.Book.Add(ActKind.Advice, room, $"{room.Name} — {spec.Name}", "센서에는 까닭이 잡히지 않는다", "기록해 둔다", "다시 보이면 알려 주세요", $"curio:{e.Key}", SimTime.Hours(12)) != null) Stats.ComputerNotes++;
                if (e.Scared >= 2 && a.Speak.Announce($"{room.Name}에서 본 것은 센서상 위험 신호가 없습니다 — 기록해 두었습니다", room, 0) != null)
                {
                    Stats.Calmed++;
                    foreach (var c in inRoom) w.Brain2.Emotions.Feel(c, Feeling.Fear, -0.2f, "컴퓨터가 위험하지 않다고 했다");
                }
            }
        }
    }

    // ── 재능 ──

    /// <summary>음악 시스템이 부른다: 좋아하는 노래가 흐르는 방에 다른 사람이 있으면 노래 · 건반 솜씨가 드러날 수 있다.</summary>
    public bool OnMusic(CrewMember c, CabinTune t)
    {
        if (TalentKnown(c) || TalentOf(c) is not ("t_song" or "t_piano") || c.Room is not Room r) return false;
        var here = _w.Crew.Where(o => o != c && !o.Dead && o.IsAwake && o.Room == r).ToList();
        if (here.Count == 0 || !R.Chance(0.02f)) return false;
        here.Add(c);
        ShowTalent(c, here);
        return true;
    }

    private void Cues(Dictionary<int, List<CrewMember>> byRoom)
    {
        var w = _w;
        bool night = SimTime.HourOfDay(w.Tick) is >= 22f or < 2f;
        bool crisis = w.Music.Mood == MusicMood.Crisis;
        foreach (var (rid, here) in byRoom)
        {
            if (here.Count < 2) continue;
            var room = w.Ship.Rooms[rid];
            foreach (var c in here)
            {
                if (TalentKnown(c) || !c.IsAwake || TalentOf(c) is not string tk) continue;
                var cue = CurioTable.Of(tk).Cue;
                var act = c.Job?.Activity;
                bool on = cue switch
                {
                    TalentCue.Meal => act is EatActivity or SharedMealActivity && here.Count >= 3,
                    TalentCue.Night => night && room.Type is RoomType.Observatory or RoomType.Lounge,
                    TalentCue.Repair => act is MendActivity,
                    TalentCue.Crisis => crisis && here.Any(o => o != c && (o.Down || o.Vitals.Injury > 0.3f)),
                    TalentCue.Bored => act is RelaxActivity && room.Type == RoomType.Lounge,
                    _ => false,
                };
                if (on && R.Chance(0.004f)) ShowTalent(c, here);
            }
        }
    }

    private void ShowTalent(CrewMember c, List<CrewMember> here)
    {
        var w = _w;
        if (!_talentShown.Add(c.Id) || TalentOf(c) is not string tk) return;
        var spec = CurioTable.Of(tk);
        Stats.Talents++;
        Record(spec, c.Id, c.Room?.Id ?? -1, $"{c.Name} — {spec.Line}");
        w.Log.Add(w.Tick, LogKind.Life, spec.Line, c.Id);
        w.Brain2.Emotions.Feel(c, Feeling.Pride, 0.3f, spec.Name);
        foreach (var o in here)
        {
            if (o == c || !o.IsAwake) continue;
            o.ChangeAffinity(c, 0.05f);
            c.ChangeAffinity(o, 0.02f);
            o.Needs.Stress = MathF.Max(0f, o.Needs.Stress - 0.04f);
            w.Brain2.Emotions.Feel(o, Feeling.Joy, 0.15f, $"{Ko.IGa(c.Name)} 그런 걸 할 줄 알았다", c);
        }
    }

    // ── 비밀 ──

    private void Audit()
    {
        var w = _w;
        var a = w.Automation;
        if (!a.Present || !a.CoreOnline) return;
        Stats.Audits++;
        foreach (var c in w.Crew)
        {
            if (c.Dead || SecretOf(c) != "s_ration" || SecretKnown(c) || _rationFlagged.Contains(c.Id) || !R.Chance(0.5f)) continue;
            _rationFlagged.Add(c.Id);
            a.Book.Add(ActKind.Advice, null, "식량 재고가 장부와 두 끼니 다르다", "누가 덜어 간 듯하다 — 탓하지 않고 알린다", "재고를 다시 센다", "모르게 덜어 간 것이 있으면 제자리에", $"audit:{c.Id}", SimTime.Hours(48));
            a.Speak.Announce("식량 재고가 장부와 조금 맞지 않습니다 — 다시 세 보겠습니다", null, 0);
            break;
        }
    }

    private void Secrets(Dictionary<int, List<CrewMember>> byRoom)
    {
        var w = _w;
        bool night = SimTime.HourOfDay(w.Tick) is >= 21f or < 7f;
        foreach (var (rid, here) in byRoom)
        {
            if (here.Count < 2) continue;
            var room = w.Ship.Rooms[rid];
            foreach (var owner in here)
            {
                if (SecretOf(owner) is not string sk || SecretKnown(owner)) continue;
                float p = 0f;
                if (night && room.Type == RoomType.Quarters) p = 0.0008f;
                if (sk == "s_ration" && _rationFlagged.Contains(owner.Id)) p *= 12f;
                if (sk == "s_fear" && room.Dark) p = 0.05f;
                if (sk == "s_garden" && room.Type == RoomType.Hydroponics) p = Math.Max(p, 0.002f);
                if (p <= 0f || !R.Chance(p)) continue;
                var who = here.Where(o => o != owner && o.IsAwake).OrderByDescending(o => o.AffinityTo(owner)).FirstOrDefault();
                if (who != null) Reveal(owner, who, sk == "s_fear" ? "어둠 속에서 알아챘다" : "같은 방에서 알게 됐다");
            }
        }
    }

    /// <summary>비밀이 드러난다: 좋은 비밀은 가까워지고, 나쁜 비밀은 지켜 주면 고마워하고 퍼뜨리면 틀어진다.</summary>
    public void Reveal(CrewMember owner, CrewMember finder, string how)
    {
        var w = _w;
        if (SecretOf(owner) is not string sk || !_secretShown.Add(owner.Id)) return;
        var spec = CurioTable.Of(sk);
        Stats.Secrets++;
        Record(spec, owner.Id, owner.Room?.Id ?? -1, $"{owner.Name} — {spec.Line}");
        if (spec.Polarity > 0)
        {
            finder.ChangeAffinity(owner, 0.08f);
            owner.ChangeAffinity(finder, 0.05f);
            w.Log.Add(w.Tick, LogKind.Life, $"{owner.Name}의 비밀을 알았다 ({how}) — {spec.Name}. 조금 더 가까워졌다", finder.Id);
            return;
        }
        if (finder != owner && (finder.AffinityTo(owner) > 0.25f || spec.Polarity == 0))
        {
            Stats.Kept++;
            w.Relations.Remember(owner, finder, RelationReason.KeptMySecret, $"{spec.Name} — 알고도 말하지 않았다");
            w.Log.Add(w.Tick, LogKind.Life, $"{owner.Name}의 비밀을 알았지만 혼자만 알기로 했다", finder.Id);
        }
        else
        {
            Stats.Told++;
            w.Relations.Remember(owner, finder, RelationReason.ToldOnMe, $"{Ko.EulReul(spec.Name)} 퍼뜨렸다");
            foreach (var o in w.Crew.Where(o => o != owner && o != finder && !o.Dead).OrderByDescending(o => o.AffinityTo(finder)).Take(2))
            {
                w.Relations.Remember(o, owner, RelationReason.LiedToUs, spec.Name);
                o.ChangeAffinity(owner, -0.04f);
            }
            w.Log.Add(w.Tick, LogKind.Life, $"{owner.Name}의 비밀 — {spec.Name} — 을 다른 사람들에게 말했다", finder.Id);
        }
    }

    // ── 시험 · 화면 ──

    /// <summary>시험: 그 자리에 숨긴 것을 둔다.</summary>
    public CurioPlaced PlaceForTest(string key, Room r, Cell at)
    {
        if (!_init) Init();
        var p = new CurioPlaced { Key = key, Room = r.Id, At = at, Since = _w.Tick };
        Placed.Add(p);
        return p;
    }

    public void ForceTalent(CrewMember c, string key) { if (!_init) Init(); _talent[c.Id] = key; _talentShown.Remove(c.Id); }
    public void ForceSecret(CrewMember c, string key) { if (!_init) Init(); _secret[c.Id] = key; _secretShown.Remove(c.Id); }
    public void ForcePassing() { if (!_init) Init(); StartPassing(); }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Placed.Count); I(Finds.Count); I(Seen.Count); I(Active.Count); I(Stats.Talents); I(Stats.Secrets); I(Stats.Watched); I(Stats.Found);
        foreach (var p in Placed) { I(p.Room); I(p.At.X); I(p.At.Y); I(p.Found ? 1 : 0); }
    }
}

/// <summary>v17.9 창밖에 무언가 지나가면(컴퓨터 방송 · 들은 사람) 쉬던 사람이 창가로 가서 본다.</summary>
public sealed class WindowWatchActivity : Activity
{
    public override string Id => "window";
    public override string Label => "창밖 구경";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var e = w.Curios.Passing;
        if (e == null || !e.Announced || e.Seen.Contains(c.Id) || w.Tick > e.End - SimTime.Minutes(10)) return (0f, "—");
        if (OnShift(c, w) && c.Job?.Urgent == true) return (0f, "—");
        return (0.25f + 0.2f * (1f - c.Needs.Stress) + (OnShift(c, w) ? -0.15f : 0f), $"창밖에 {CurioTable.Of(e.Key).Name}");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var e = w.Curios.Passing;
        if (e == null) return null;
        Cell? best = null;
        int bd = int.MaxValue;
        foreach (var r in w.Ship.LiveRooms)
        {
            if (!CurioSystem.HasWindow(r) || r.OffLimits) continue;
            foreach (var cell in r.Cells)
            {
                if (!w.Ship.IsWalkable(cell) || w.IsSpotTaken(cell, c)) continue;
                int d = dist.Get(cell);
                if (d >= 0 && d < bd) { bd = d; best = cell; }
            }
        }
        if (best is not Cell spot) return null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(spot));
        toils.Add(new WaitToil(SimTime.Minutes(25), Pose.Standing, null, minTicks: SimTime.Minutes(8))
        {
            DoneWhen = (cm, world) => world.Curios.Passing == null,
        });
        w.Curios.Stats.Gathered++;
        return new Job(this, "창밖 구경", toils) { LogText = $"창밖 {Ko.EulReul(CurioTable.Of(e.Key).Name)} 보러 간다" };
    }
}
