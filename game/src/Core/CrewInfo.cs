using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v17.3 개인 자리 · 못 끝낸 일 · 정보 차이 · 선내 메신저 · 사진 · 공동 장부.
// "누가 왜 행동했고, 무엇이 바뀌었으며, 누가 그것을 기억하는가."
//
// 자리: 식당에서 늘 앉던 자리 · 친한 사람 곁 · 배식기 소음에서 먼 자리(조용한 사람) · 밝은 자리(책 읽는 사람) · 방금 다툰 사람 곁은 피한다 — 작은 가중치.
// 못 끝낸 일: 거의 다 만든 모형 · 고치려던 독서등 · 늦어진 생일 선물 · 구해 준 사람 대신 서는 근무 · 깨진 컵 붙이기 —
//   두뇌의 중기 목표로 올라가 시간 날 때 이어서 하고, 떠난 사람의 것은 가까웠던 사람이 이어받는다.
// 정보 차이(CrewInfoGap.cs): 깨진 컵(본 사람 · 소리만 들은 사람 · 처음 발견한 사람 · 주인의 의심 · 해명 · 사과) ·
//   마지막에 둔 곳부터 찾기 · 남이 치운 물건 · 말한 사람의 신용.
// 공동 장부: 설거지 · 대신 선 근무 · 내놓은 물자 → 고마움 · 혼자만 하는 사람의 불만 → 회의 안건 → 당번.
// 사진: 중요한 날 찍어 개인 물건으로 · 벽에 걸고 · 지나가다 보며 그날과 그 사람을 떠올린다.

public enum TodoKind : byte { Model, Lamp, Gift, Cover, MendCup }

public sealed class Todo
{
    public int Id { get; init; }
    public int Owner { get; set; }
    public TodoKind Kind { get; init; }
    /// <summary>무엇 (모형 이름 · 선물 · 컵 이름).</summary>
    public string What { get; set; } = "";
    public float Progress { get; set; }
    /// <summary>누구를 위해 (선물 받을 사람 · 대신 서 줄 사람).</summary>
    public int For { get; init; } = -1;
    /// <summary>다 만든 것 · 붙일 컵 (Belonging.Id).</summary>
    public int Item { get; set; } = -1;
    public long Since { get; init; }
    public long LastWork { get; set; } = -1;
    public int Sessions { get; set; }
    public bool Done { get; set; }
    public long DoneAt { get; set; } = -1;
    /// <summary>물려받았으면 처음 시작한 사람.</summary>
    public int From { get; set; } = -1;
    /// <summary>하다가 끊겼다 (눈에 밟힌다 — 다음에 먼저 손이 간다).</summary>
    public bool Cut { get; set; }
}

public enum LedgerKind : byte { Dishes, Cover, Donate }

public sealed record LedgerEntry(long Tick, int Who, LedgerKind Kind, int For, string Text);

public enum PhotoScene : byte { Group, Birthday, Window, Work, Meal }

public sealed class PhotoInfo
{
    public int Id { get; init; }
    public int Belonging { get; init; } = -1;
    public int Taker { get; init; }
    public long Tick { get; init; }
    public PhotoScene Scene { get; init; }
    public string Caption { get; init; } = "";
    public RoomType Place { get; init; }
    public int[] People { get; init; } = Array.Empty<int>();
    public bool Hung { get; set; }
    public Cell Wall { get; set; }
    /// <summary>벽 쪽 (액자가 붙는 방향).</summary>
    public Cell WallDir { get; set; }
    public int Views { get; set; }
    /// <summary>벽에 걸린 뒤 한 번이라도 본 사람.</summary>
    public List<int> SeenBy { get; } = new();
}

public enum InfoDo : byte { CheckSound, Confront, Explain, Apologize, Search, Todo, Dishes, HangPhoto, LookPhoto, GoMeeting }

public sealed class InfoIntent
{
    public InfoDo Do { get; init; }
    public int Crew { get; init; }
    public int Other { get; init; } = -1;
    public int Thing { get; init; } = -1;
    public Cell At { get; set; }
    public float Score { get; set; }
    public string Why { get; set; } = "";
    public long Since { get; init; }
    public long Until { get; set; }
}

public sealed class Spat
{
    public int A { get; init; }
    public int B { get; init; }
    public long Tick { get; init; }
    public string Why { get; init; } = "";
    public bool Made { get; set; }
}

public sealed class InfoStats
{
    public int SeatPicks, Regulars, SeatAway, Todos, TodoWork, TodoDone, Inherited, Delivered, CoveredShift;
    public int Jolts, Cups, Saw, Heard, Checked, Found, Accused, Denied, Vented, Explained, ComputerExplained, Apologized, Asked, Mended;
    public int Wants, Searches, LastSeenFirst, FoundThere, FoundElsewhere, AskedWhere, Answered, Tidied, CredDrops;
    public int Dishes, Covers, Donations, Gripes, Guilty, Agenda, Rostered, RosterDone, Thanks;
    public int Photos, Hung, Looks, Remembered, Late, Paged, MissedMeeting, Jokes;
    public string Line() =>
        $"자리 {SeatPicks}(늘 앉는 자리 {Regulars} · 피해 앉음 {SeatAway}) · 못 끝낸 일 {Todos}(이어 함 {TodoWork} · 끝냄 {TodoDone} · 물려받음 {Inherited} · 선물 {Delivered} · 대신 근무 {CoveredShift}) · " +
        $"컵 {Cups}(본 사람 {Saw} · 소리만 {Heard} → 확인 {Checked} · 발견 {Found} · 의심 {Accused} · 해명 {Explained}(컴퓨터 {ComputerExplained}) · 사과 {Apologized} · 붙임 {Mended}) · " +
        $"찾기 {Searches}(마지막 둔 곳부터 {LastSeenFirst} · 그 자리 {FoundThere} · 딴 데 {FoundElsewhere} · 물어봄 {AskedWhere} · 답 {Answered}) · " +
        $"장부: 설거지 {Dishes} · 대신 근무 {Covers} · 내놓음 {Donations} · 불만 {Gripes}(찔림 {Guilty}) · 안건 {Agenda} · 당번 {Rostered} · " +
        $"사진 {Photos}(벽에 {Hung} · 보며 떠올림 {Looks}) · 회의에 늦음 {Late}(컴퓨터가 부름 {Paged} · 놓침 {MissedMeeting}) · 농담 {Jokes}";
}

public sealed partial class InfoSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7349 + 1187));
    public static long UpdateTicks;

    public ShipChat Chat { get; }
    public InfoStats Stats { get; } = new();
    public List<Todo> Todos { get; } = new();
    public List<LedgerEntry> Ledger { get; } = new();
    public List<PhotoInfo> Photos { get; } = new();
    public List<InfoIntent> Intents { get; } = new();
    public List<Spat> Spats { get; } = new();
    /// <summary>개수대에 쌓인 그릇.</summary>
    public int Dirty { get; internal set; }
    /// <summary>회의가 정한 설거지 당번 (돌아가며) — 비어 있으면 아직 없다.</summary>
    public List<int> Roster { get; } = new();
    public long RosterSince { get; private set; } = -1;
    public bool DishAgendaPending { get; private set; }
    public string DishAgendaWhy { get; private set; } = "";

    private readonly SortedDictionary<long, int> _seatUse = new();
    private readonly SortedDictionary<int, int> _meals = new();
    private readonly List<(long tick, int who)> _ate = new();
    private readonly SortedDictionary<int, long> _guilt = new();
    private readonly SortedDictionary<int, long> _looked = new();
    private readonly SortedDictionary<int, bool> _lampFixed = new();
    private readonly HashSet<int> _seeded = new();
    private readonly HashSet<int> _unawareAtStart = new();
    private readonly SortedDictionary<int, int> _stash = new();
    private int _nextTodo = 1, _nextPhoto = 1;
    private long _next5, _next30, _nextHour;
    private long _relScan = -1;
    private int _histSeen;
    private long _gripeDay = -1, _moveDay = -1, _birthdayPhotoDay = -1;

    public InfoSystem(World w)
    {
        _w = w;
        Chat = new ShipChat(w, this);
    }

    private CrewMember? CrewOf(int id) => id >= 0 && id < _w.Crew.Count && _w.Crew[id].Id == id ? _w.Crew[id] : _w.Crew.FirstOrDefault(c => c.Id == id);
    private static bool Adult(CrewMember c) => !c.Dead && !c.IsChild && !c.Away;

    // ───────────────────────────── 시스템 틱 ─────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        long now = w.Tick;
        Chat.Update();
        WatchCups();
        MeetingWatch();
        if (now >= _next5)
        {
            _next5 = now + SimTime.Minutes(5);
            ObserveSeats();
            ObserveCupsInUse();
            ObserveThings();
            ObserveCases();
            CountMeals();
        }
        if (now >= _next30)
        {
            _next30 = now + SimTime.Minutes(30);
            SeedTodos();
            TodoGoals();
            ScanRelations();
            Intents.RemoveAll(i => i.Until <= now || CrewOf(i.Crew) is not { Dead: false });
            Spats.RemoveAll(s => now - s.Tick > SimTime.TicksPerDay * 2);
            LampNights(dt * 10f);
        }
        if (now >= _nextHour)
        {
            _nextHour = now + SimTime.TicksPerHour;
            Hourly();
        }
        UpdateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    private void Hourly()
    {
        var w = _w;
        float hour = SimTime.HourOfDay(w.Tick);
        bool crisis = Crisis.Acting(w);
        if (!crisis)
        {
            RollJolt();
            Wants();
            Gripes(hour);
            MaybeMoveMeeting(hour);
            Donations(hour);
        }
        ImportantDays(hour, crisis);
    }

    // ───────────────────────────── 자리 ─────────────────────────────

    private static long SeatKey(int crew, int seat) => (long)crew * 100000 + seat;

    /// <summary>늘 앉던 자리 (두 번 이상 앉은 곳 중 가장 많이).</summary>
    public int Regular(CrewMember c)
    {
        int best = -1, n = 1;
        long lo = SeatKey(c.Id, 0), hi = SeatKey(c.Id + 1, 0);
        foreach (var (k, v) in _seatUse)
        {
            if (k < lo) continue;
            if (k >= hi) break;
            if (v > n) { n = v; best = (int)(k - lo); }
        }
        return best;
    }

    public int SeatCount(CrewMember c, Furniture seat) => _seatUse.TryGetValue(SeatKey(c.Id, seat.Id), out var v) ? v : 0;

    /// <summary>EatActivity 자리 고르기 (거리² 단위 · 작은 값): 늘 앉던 자리 · 친한 사람 곁 · 소음 · 조명 · 방금 다툰 사람.</summary>
    public float SeatBias(CrewMember c, Furniture seat)
    {
        var w = _w;
        float b = 0f;
        // 늘 앉던 자리 (앉을수록 조금씩)
        int n = SeatCount(c, seat);
        if (n > 0) b -= MathF.Min(7f, 1.6f * n);
        var room = seat.Room;
        foreach (var o in w.Crew)
        {
            if (o == c || o.Dead || o.Room != room) continue;
            Vector2? op = SeatPos(o);
            if (op is not Vector2 p) continue;
            float d = (seat.Center - p).Length();
            if (d > 3.5f) continue;
            // 친한 사람 곁 (가까울수록)
            float aff = c.AffinityTo(o);
            if (aff > 0.2f && d < 2.6f) b -= 5f * aff * (2.6f - d) / 2.6f;
            // 최근에 다툰 사람 (컵 · 말다툼)
            if (SpatWith(c, o) is Spat s) b += 20f * (3.5f - d) * (s.Made ? 0.25f : 1f);
        }
        // 소음: 배식기 · 커피 머신 · 화구 곁은 조용한 사람이 꺼린다
        float quiet = 1f - c.Traits.Sociability + (Life.Has(c, Habit.Loner) ? 0.5f : 0f) - (Life.Has(c, Habit.Talker) ? 0.4f : 0f);
        if (quiet > 0.3f)
            foreach (var f in room.Furniture)
                if (f.Type is FurnitureType.MealDispenser or FurnitureType.CoffeeMachine or FurnitureType.Stove or FurnitureType.DishWasher)
                {
                    float d = (seat.Center - f.Center).Length();
                    if (d < 3f) b += quiet * 2.2f * (3f - d);
                }
        // 조명: 책 읽는 사람은 밝은 자리 · 올빼미는 어둑한 자리
        int lit = LightNear(seat);
        if (Life.Has(c, Habit.Bookworm) || c.Hobbies.Contains(Hobby.Reading)) b -= 1.5f * lit;
        else if (Life.Has(c, Habit.NightOwl)) b += 0.8f * lit;
        return b;
    }

    private int LightNear(Furniture seat)
    {
        var body = _w.Body;
        var grid = _w.Ship.Grid;
        int n = 0;
        if (body.Ceiling.Length != grid.CellCount) return 0;
        foreach (var d in Cell.Dirs8)
        {
            var x = seat.Cells.Count > 0 ? seat.Cells[0] + d : seat.UseSpots[0];
            if (grid.InBounds(x) && (body.Ceiling[grid.Index(x)] & CeilingFlags.Light) != 0) n++;
        }
        return Math.Min(3, n);
    }

    private static Vector2? SeatPos(CrewMember o)
    {
        if (o.Job is Job j) foreach (var r in j.Reservations) if (r.Type == FurnitureType.Seat) return r.Center;
        if (o.Pose == Pose.Sitting) return o.Position;
        return null;
    }

    public Spat? SpatWith(CrewMember a, CrewMember b)
    {
        for (int i = Spats.Count - 1; i >= 0; i--)
        {
            var s = Spats[i];
            if (s.A == a.Id && s.B == b.Id || s.A == b.Id && s.B == a.Id) return _w.Tick - s.Tick < SimTime.TicksPerDay ? s : null;
        }
        return null;
    }

    /// <summary>CrewLife 말다툼 · 컵 의심: 둘 사이에 다툼이 남는다 (자리를 떨어져 앉는다).</summary>
    public void OnQuarrel(CrewMember a, CrewMember b, string why = "말다툼")
    {
        Spats.Add(new Spat { A = a.Id, B = b.Id, Tick = _w.Tick, Why = why });
        if (Spats.Count > 60) Spats.RemoveAt(0);
    }

    /// <summary>식당에 앉은 사람: 그 자리를 센다 (늘 앉는 자리가 생긴다) · 다툰 사람과 떨어져 앉았는지.</summary>
    private void ObserveSeats()
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Pose != Pose.Sitting || c.Room is not { Type: RoomType.Mess } room || c.Job?.Activity is not EatActivity) continue;
            var seat = room.Furniture.FirstOrDefault(f => f.Type == FurnitureType.Seat && f.UseSpots.Count > 0 && f.UseSpots[0] == c.Cell);
            if (seat == null) continue;
            long k = SeatKey(c.Id, seat.Id);
            if (_counted.TryGetValue(c.Id, out var cj) && cj == c.Job) continue; // 한 끼에 한 번만 센다
            _counted[c.Id] = c.Job!;
            int before = Regular(c);
            _seatUse[k] = (_seatUse.TryGetValue(k, out var v) ? v : 0) + 1;
            Stats.SeatPicks++;
            if (before == seat.Id) Stats.Regulars++;
            foreach (var o in w.Crew)
                if (o != c && !o.Dead && o.Room == room && SpatWith(c, o) is Spat s && !s.Made && SeatPos(o) is Vector2 op && (op - seat.Center).Length() >= 3.5f)
                {
                    Stats.SeatAway++;
                    w.Log.Add(w.Tick, LogKind.Life, $"{o.Name} 곁을 피해 멀찍이 앉는다 ({s.Why})", c.Id);
                    break;
                }
        }
    }
    private readonly Dictionary<int, Job> _counted = new();

    // ───────────────────────────── 못 끝낸 일 ─────────────────────────────

    public IEnumerable<Todo> TodosOf(CrewMember c) => Todos.Where(t => t.Owner == c.Id && !t.Done);

    public static string TodoText(Todo t, World w)
    {
        string? who = t.For >= 0 ? w.Crew.FirstOrDefault(c => c.Id == t.For)?.Name : null;
        string head = t.From >= 0 && w.Crew.FirstOrDefault(c => c.Id == t.From) is CrewMember f ? $"{Ko.IGa(f.Name)} 남긴 " : "";
        return t.Kind switch
        {
            TodoKind.Model => $"{head}{t.What} 마무리",
            TodoKind.Lamp => head == "" ? "머리맡 독서등 고치기" : $"{head}독서등 고치기",
            TodoKind.Gift => $"{head}늦어진 {who ?? "친구"} 생일 선물 ({t.What})",
            TodoKind.Cover => $"{who ?? "그 사람"} 대신 근무 한 번 서기",
            _ => $"깨진 {t.What} 붙이기",
        };
    }

    private Todo AddTodo(CrewMember c, TodoKind k, string what, float progress, int forId = -1, int item = -1)
    {
        var t = new Todo { Id = _nextTodo++, Owner = c.Id, Kind = k, What = what, Progress = progress, For = forId, Item = item, Since = _w.Tick };
        Todos.Add(t);
        Stats.Todos++;
        return t;
    }

    private static readonly string[] ModelNames = { "탐사선 모형", "화물선 모형", "고향 등대 모형", "기관실 축소 모형", "첫 우주 정거장 모형", "범선 모형" };
    private static readonly string[] GiftNames = { "손뜨개 장갑", "깎다 만 나무 새", "손으로 쓴 노래 악보", "말린 꽃 책갈피", "작은 오르골" };

    /// <summary>처음 본 사람에게 못 끝낸 일 두세 개 (성격 · 취미 · 지난 생일).</summary>
    private void SeedTodos()
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (!Adult(c) || !c.Profiled || _seeded.Contains(c.Id)) continue;
            _seeded.Add(c.Id);
            var rng = new Rng(unchecked(w.Seed * 4271 + c.Id * 6007 + 17));
            int made = 0;
            bool crafty = c.Hobbies.Any(h => h is Hobby.ModelBuilding or Hobby.Woodwork or Hobby.Electronics);
            if (crafty || rng.Chance(0.3f)) { AddTodo(c, TodoKind.Model, ModelNames[rng.Range(0, ModelNames.Length)], rng.Range(0.55f, 0.85f)); made++; }
            if (Life.Has(c, Habit.Bookworm) || Life.Has(c, Habit.Tinkerer) || c.Hobbies.Contains(Hobby.Reading) || rng.Chance(0.3f)) { AddTodo(c, TodoKind.Lamp, "독서등", rng.Range(0f, 0.3f)); made++; }
            int day = SimTime.Day(w.Tick);
            var friend = w.Crew.Where(o => o != c && Adult(o) && c.AffinityTo(o) > 0.15f)
                .Where(o => { int ago = (day - Bday(o) + 30) % 30; return ago >= 2 && ago <= 12; })
                .OrderByDescending(o => c.AffinityTo(o)).ThenBy(o => o.Id).FirstOrDefault();
            if (made < 3 && friend != null) { AddTodo(c, TodoKind.Gift, GiftNames[rng.Range(0, GiftNames.Length)], rng.Range(0.1f, 0.5f), friend.Id); made++; }
            if (made == 0) AddTodo(c, TodoKind.Model, ModelNames[rng.Range(0, ModelNames.Length)], rng.Range(0.5f, 0.8f));
            _stash[c.Id] = rng.Chance(0.5f) ? 2 : 1;
        }
    }

    /// <summary>생일 (일상 장면과 같은 셈).</summary>
    public int Bday(CrewMember c) => (c.Id * 37 + _w.Seed) % 30;

    /// <summary>못 끝낸 일을 두뇌의 중기 목표로 올린다 · 떠난 사람의 일은 가까웠던 사람이 이어받는다.</summary>
    private void TodoGoals()
    {
        var w = _w;
        if (!BrainSystem.Enabled) return;
        foreach (var t in Todos.ToList())
        {
            if (t.Done) continue;
            var c = CrewOf(t.Owner);
            if (c == null) continue;
            if (c.Dead) { Inherit(t, c); continue; }
            if (t.For >= 0 && CrewOf(t.For) is { Dead: true } && t.Kind is TodoKind.Gift or TodoKind.Cover) { t.Done = true; continue; }
            var g = w.Brain2.Goals.Of(c).FirstOrDefault(x => x.Key == "todo" + t.Id);
            if (g == null || g.Until - w.Tick < SimTime.Hours(4))
                w.Brain2.Goals.Push(c, "todo" + t.Id, TodoText(t, w), t.From >= 0 ? "떠난 사람이 끝내지 못한 일" : t.Kind switch
                {
                    TodoKind.Model => "거의 다 됐는데 손을 놓았다",
                    TodoKind.Lamp => "밤에 책을 읽고 싶다",
                    TodoKind.Gift => "생일을 그냥 넘겼다",
                    TodoKind.Cover => "나를 구해 줬다",
                    _ => "아끼던 컵이다",
                }, t.Kind is TodoKind.Gift or TodoKind.Cover ? ActCat.Social : t.Kind == TodoKind.MendCup ? ActCat.Care : ActCat.Hobby, 24f, 0.6f);
        }
    }

    private void Inherit(Todo t, CrewMember dead)
    {
        var w = _w;
        t.Done = true;
        if (t.Kind is TodoKind.Cover || t.Progress < 0.2f) return;
        var heir = w.Crew.Where(o => o != dead && Adult(o) && o.CanAct && (t.Kind != TodoKind.Gift || o.Id != t.For))
            .OrderByDescending(o => (dead.Partner == o.Id ? 1f : 0f) + o.AffinityTo(dead) + (t.Kind == TodoKind.Lamp ? 0.3f * o.SkillLevel(Skill.Electrical) : 0f)).ThenBy(o => o.Id).FirstOrDefault();
        if (heir == null || heir.AffinityTo(dead) < 0.05f && dead.Partner != heir.Id) return;
        var nt = AddTodo(heir, t.Kind, t.What, t.Progress, t.For, t.Item);
        nt.From = dead.Id;
        Stats.Inherited++;
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(dead.Name)} 끝내지 못한 {Ko.EulReul(t.What)} 마저 하기로 한다", heir.Id);
        Life.Diary(w, heir, Persona.Say(heir, $"{dead.Name}의 {t.What}. 내가 끝내 주기로 했다"));
    }

    /// <summary>못 끝낸 일 하나를 한 번 이어 한다 (InfoActivity) — 다 되면 결과가 생긴다.</summary>
    internal void WorkOn(CrewMember c, Todo t, float hours)
    {
        var w = _w;
        float rate = t.Kind switch
        {
            TodoKind.Model => 0.22f,
            TodoKind.Lamp => 0.12f + 0.3f * c.SkillLevel(Skill.Electrical),
            TodoKind.Gift => 0.3f,
            TodoKind.MendCup => 0.25f,
            _ => 1f,
        };
        if (Life.Has(c, Habit.Perfectionist)) rate *= 0.8f;
        t.Progress = MathF.Min(1f, t.Progress + rate * hours);
        t.LastWork = w.Tick;
        t.Sessions++;
        Stats.TodoWork++;
        if (t.Progress < 1f || t.Kind == TodoKind.Gift) return;
        Finish(c, t);
    }

    internal void Finish(CrewMember c, Todo t)
    {
        var w = _w;
        t.Done = true;
        t.DoneAt = w.Tick;
        Stats.TodoDone++;
        var from = t.From >= 0 ? CrewOf(t.From) : null;
        switch (t.Kind)
        {
            case TodoKind.Model:
            {
                var b = w.Belongings.Seed2(c, BelongingKind.Artwork, from != null ? $"{Ko.IGa(from.Name)} 만들다 둔 것을 {Ko.IGa(c.Name)} 마저 만든 것" : "몇 달을 붙들고 있던 것");
                b.Name = t.What;
                b.Maker = from?.Id ?? c.Id;
                if (c.Bed is Furniture bed && bed.UseSpots.Count > 0) b.At = bed.UseSpots[0];
                t.Item = b.Id;
                w.Log.Add(w.Tick, LogKind.Life, $"마침내 {Ko.EulReul(t.What)} 완성했다", c.Id);
                Life.Diary(w, c, Persona.Say(c, from != null ? $"{from.Name}의 {Ko.EulReul(t.What)} 끝냈다. 보여 주고 싶었는데" : $"{t.What}, 드디어 끝났다"));
                w.Brain2.Emotions.Feel(c, Feeling.Pride, 0.25f, $"{t.What} 완성");
                if (from != null) { c.GriefUntil = Math.Max(w.Tick, c.GriefUntil - SimTime.Hours(12)); w.History.Add(w, HistoryKind.Bond, $"{Ko.IGa(c.Name)} {Ko.IGa(from.Name)} 남긴 {Ko.EulReul(t.What)} 마저 완성했다", c.Room, new[] { c }, log: false); }
                break;
            }
            case TodoKind.Lamp:
                _lampFixed[c.Id] = true;
                w.Log.Add(w.Tick, LogKind.Life, "머리맡 독서등을 고쳤다 — 이제 밤에 책을 읽을 수 있다", c.Id);
                Life.Diary(w, c, Persona.Say(c, "독서등이 다시 켜진다. 별것 아닌데 기분이 좋다"));
                w.Brain2.Emotions.Feel(c, Feeling.Joy, 0.15f, "독서등을 고쳤다");
                break;
            case TodoKind.MendCup:
            {
                if (w.Belongings.Get(t.Item) is Belonging cup)
                {
                    cup.Condition = 0.7f;
                    if (!cup.Name.StartsWith("이어 붙인")) cup.Name = "이어 붙인 " + cup.Name;
                    cup.At = null;
                    MarkLog.Add(cup.Marks, w.Tick, $"{Ko.IGa(c.Name)} 조각을 모아 금빛으로 이어 붙였다");
                    var owner = CrewOf(cup.Owner);
                    if (owner != null && owner != c)
                    {
                        w.Relations.Remember(owner, c, RelationReason.FixedMyThing, $"깨진 {Ko.EulReul(cup.Name)} 이어 붙여 줬다");
                        owner.ChangeAffinity(c, 0.06f);
                        Life.Diary(w, owner, Persona.Say(owner, $"{Ko.IGa(c.Name)} 내 컵을 붙여 줬다. 금 간 자리가 더 예쁘다"));
                    }
                }
                Stats.Mended++;
                w.Log.Add(w.Tick, LogKind.Life, $"깨진 {Ko.EulReul(t.What)} 조각조각 이어 붙였다", c.Id);
                break;
            }
        }
        if (from != null) Life.Diary(w, c, Persona.Say(c, $"{from.Name}, 네가 하던 거 끝냈어"));
    }

    /// <summary>늦어진 생일 선물을 건넨다.</summary>
    internal void Deliver(CrewMember c, Todo t, CrewMember to)
    {
        var w = _w;
        t.Done = true;
        t.DoneAt = w.Tick;
        Stats.TodoDone++;
        Stats.Delivered++;
        var b = w.Belongings.Gift(to, c, BelongingKind.Artwork, t.What, $"{Ko.IGa(c.Name)} 늦게 건넨 생일 선물");
        b.Maker = c.Id;
        t.Item = b.Id;
        c.Say(w, Persona.Say(c, $"늦었지만 — 생일 축하해. {t.What}"));
        to.Say(w, Persona.Say(to, "이걸 아직 기억하고 있었어?"));
        w.Relations.Remember(to, c, RelationReason.GaveMeGift, $"늦었지만 생일 선물로 {Ko.EulReul(t.What)} 줬다");
        to.ChangeAffinity(c, 0.08f); c.ChangeAffinity(to, 0.04f);
        w.Brain2.Emotions.Feel(to, Feeling.Joy, 0.25f, "늦은 생일 선물", c);
        Life.Diary(w, to, Persona.Say(to, $"{Ko.IGa(c.Name)} 늦은 생일 선물을 줬다. {t.What}"));
        Life.Diary(w, c, Persona.Say(c, $"{to.Name}에게 드디어 선물을 건넸다"));
        w.Log.Add(w.Tick, LogKind.Life, $"{to.Name}에게 늦어진 생일 선물({t.What})을 건넸다", c.Id);
    }

    /// <summary>구해 준 사람 대신 근무를 선다 (공동 장부에도 남는다).</summary>
    internal bool CoverFor(CrewMember c, Todo t, CrewMember r)
    {
        var w = _w;
        if (r.ExcusedUntil > w.Tick || c.CoveringUntil > w.Tick) return false;
        t.Done = true;
        t.DoneAt = w.Tick;
        Stats.TodoDone++;
        Stats.CoveredShift++;
        r.ExcusedUntil = w.Tick + SimTime.Hours(6);
        c.CoveringUntil = w.Tick + SimTime.Hours(6);
        c.Say(w, Persona.Say(c, "그때 날 꺼내 줬잖아. 오늘 근무는 내가 설게"));
        w.Relations.Remember(r, c, RelationReason.DidMyShift, "구해 준 보답이라며 근무를 대신 서 줬다");
        r.ChangeAffinity(c, 0.06f);
        Note(c, LedgerKind.Cover, r, $"{r.Name} 대신 근무");
        Life.Diary(w, c, Persona.Say(c, $"{r.Name} 대신 근무를 섰다. 조금은 갚은 것 같다"));
        return true;
    }

    /// <summary>밤에 고친 독서등으로 책을 읽는 사람은 잠들기 전 마음이 조금 가라앉는다.</summary>
    private void LampNights(float dtHours)
    {
        var w = _w;
        float hour = SimTime.HourOfDay(w.Tick);
        foreach (var (id, ok) in _lampFixed)
        {
            if (!ok || CrewOf(id) is not CrewMember c || c.Dead || c.Room != c.Bed?.Room) continue;
            if (SimTime.HoursFromTo(hour, c.Schedule.SleepStart) is float h && h > 0f && h < 1.5f && c.IsAwake)
                c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.02f * dtHours);
        }
    }

    public bool LampFixed(CrewMember c) => _lampFixed.TryGetValue(c.Id, out var v) && v;

    /// <summary>관계 장부를 훑는다: 대신 서 준 근무 → 장부 · 구해 준 사람 → "대신 근무 한 번" 할 일.</summary>
    private void ScanRelations()
    {
        var w = _w;
        long since = _relScan;
        _relScan = w.Tick;
        if (since < 0) return;
        foreach (var m in w.Relations.All)
        {
            if (m.Tick <= since) continue;
            var who = CrewOf(m.Who);
            var about = CrewOf(m.About);
            if (who == null || about == null || who.Dead) continue;
            if (m.Reason == RelationReason.DidMyShift && !m.Text.Contains("보답"))
            {
                Note(about, LedgerKind.Cover, who, $"{who.Name} 대신 근무");
                if (!about.Dead && Chat.CannotRead(who) == null)
                    Chat.Post(who, ChatKind.Thanks, ShipChat.Voice(who, $"{about.Name} 대신 서 줘서 고마워. 하나 빚졌다", $"{about.Name} 님, 근무 대신 서 주셔서 고맙습니다"), about: about.Id);
            }
            else if (m.Reason == RelationReason.SavedMe && !about.Dead && !Todos.Any(t => t.Owner == who.Id && t.For == about.Id && t.Kind == TodoKind.Cover && !t.Done))
                AddTodo(who, TodoKind.Cover, "근무", 0f, about.Id);
        }
    }

    // ───────────────────────────── 공동 장부 ─────────────────────────────

    public void Note(CrewMember who, LedgerKind k, CrewMember? forWhom, string text)
    {
        var w = _w;
        Ledger.Add(new LedgerEntry(w.Tick, who.Id, k, forWhom?.Id ?? -1, text));
        if (Ledger.Count > 300) Ledger.RemoveAt(0);
        switch (k)
        {
            case LedgerKind.Dishes: Stats.Dishes++; break;
            case LedgerKind.Cover: Stats.Covers++; break;
            default: Stats.Donations++; break;
        }
    }

    /// <summary>최근 사흘 동안 그 사람이 한 몫 · 먹은 끼니.</summary>
    public int Share(CrewMember c, LedgerKind k, float days = 3f)
    {
        long from = _w.Tick - (long)(SimTime.TicksPerDay * days);
        int n = 0;
        for (int i = Ledger.Count - 1; i >= 0 && Ledger[i].Tick >= from; i--) if (Ledger[i].Who == c.Id && Ledger[i].Kind == k) n++;
        return n;
    }

    public int Ate(CrewMember c, float days = 3f)
    {
        long from = _w.Tick - (long)(SimTime.TicksPerDay * days);
        int n = 0;
        for (int i = _ate.Count - 1; i >= 0 && _ate[i].tick >= from; i--) if (_ate[i].who == c.Id) n++;
        return n;
    }

    /// <summary>먹은 만큼 그릇이 쌓인다.</summary>
    private void CountMeals()
    {
        var w = _w;
        long now = w.Tick;
        foreach (var c in w.Crew)
        {
            int m = c.Stats.Meals;
            if (!_meals.TryGetValue(c.Id, out var last)) { _meals[c.Id] = m; continue; }
            if (m <= last) continue;
            _meals[c.Id] = m;
            for (int k = last; k < m; k++) _ate.Add((now, c.Id));
            Dirty += m - last;
        }
        while (_ate.Count > 0 && now - _ate[0].tick > SimTime.TicksPerDay * 4) _ate.RemoveAt(0);
        Dirty = Math.Min(Dirty, 60);
    }

    /// <summary>오늘 설거지 당번.</summary>
    public CrewMember? OnDuty()
    {
        if (Roster.Count == 0) return null;
        long day = (_w.Tick - RosterSince) / SimTime.TicksPerDay;
        for (int k = 0; k < Roster.Count; k++)
        {
            var c = CrewOf(Roster[(int)((day + k) % Roster.Count)]);
            if (c != null && Adult(c)) return c;
        }
        return null;
    }

    /// <summary>설거지할 마음 (InfoActivity).</summary>
    public (float s, string why) DishWant(CrewMember c)
    {
        var duty = OnDuty();
        if (Dirty < (duty == c ? 3 : 5) || c.IsChild || c.Dead) return (0f, "—");
        float s = 0.1f + 0.18f * c.Traits.Diligence + MathF.Min(0.15f, (Dirty - 5) * 0.015f);
        string why = $"개수대에 그릇 {Dirty}개";
        if (Life.Has(c, Habit.NeatFreak)) s += 0.12f;
        if (Life.Has(c, Habit.Messy) || Life.Has(c, Habit.Procrastinator)) s -= 0.08f;
        if (duty == c) { s += 0.32f; why += " · 오늘 내가 당번"; }
        else if (duty != null && !duty.Dead) s -= 0.08f; // 오늘은 당번이 있다
        if (_guilt.TryGetValue(c.Id, out var g) && g > _w.Tick) { s += 0.25f; why += " · 장부에 내 이름이 없다"; }
        return (MathF.Max(0f, s), why);
    }

    internal void DidDishes(CrewMember c, int n)
    {
        var w = _w;
        Dirty = 0;
        Note(c, LedgerKind.Dishes, null, $"설거지 {n}개");
        _guilt.Remove(c.Id);
        bool duty = OnDuty() == c;
        if (duty) Stats.RosterDone++;
        w.Log.Add(w.Tick, LogKind.Life, $"설거지를 했다 (그릇 {n}개){(duty ? " — 오늘 당번" : "")}", c.Id);
        foreach (var o in w.Crew)
            if (o != c && !o.Dead && o.Room == c.Room && o.IsAwake && Share(o, LedgerKind.Dishes) >= 2) { o.ChangeAffinity(c, 0.02f); Stats.Thanks++; }
    }

    /// <summary>저녁에 장부를 본다: 혼자만 설거지하는 사람이 메신저에 불만을 올리고, 회의 안건이 된다.</summary>
    private void Gripes(float hour)
    {
        var w = _w;
        long day = w.Tick / SimTime.TicksPerDay;
        if (hour < 17f || hour > 22f || _gripeDay == day) return;
        var adults = w.Crew.Where(c => Adult(c) && c.CanAct).ToList();
        if (adults.Count < 3) return;
        var top = adults.OrderByDescending(c => Share(c, LedgerKind.Dishes)).ThenBy(c => c.Id).First();
        int mine = Share(top, LedgerKind.Dishes);
        var free = adults.Where(c => c != top && Ate(c) >= 5 && Share(c, LedgerKind.Dishes) == 0).OrderByDescending(c => Ate(c)).ThenBy(c => c.Id).ToList();
        bool touchy = Life.Has(top, Habit.Grumbler) || Life.Has(top, Habit.NeatFreak) || Life.Has(top, Habit.ShortTempered);
        if (mine < 3 || free.Count == 0 || !touchy && mine < 4 || Chat.CannotRead(top) != null) return;
        _gripeDay = day;
        Gripe(top, free, mine);
    }

    /// <summary>무임승차 불만: 메신저 · 관계 · 회의 안건.</summary>
    public void Gripe(CrewMember top, List<CrewMember> free, int mine)
    {
        var w = _w;
        Stats.Gripes++;
        Chat.Post(top, ChatKind.Gripe, ShipChat.Voice(top, $"설거지 사흘 동안 나만 {mine}번 한 듯. 장부 좀 봐 줘", $"설거지 장부 좀 봐 주세요. 사흘 동안 제가 {mine}번 했습니다"), thing: -2);
        foreach (var f in free.Take(3))
        {
            w.Relations.Remember(top, f, RelationReason.FreeRide, "먹기만 하고 설거지는 안 한다");
            top.ChangeAffinity(f, -0.03f);
        }
        w.Brain2.Emotions.Feel(top, Feeling.Anger, 0.1f, "설거지는 늘 나만");
        if (Roster.Count == 0)
        {
            DishAgendaPending = true;
            DishAgendaWhy = $"{Ko.IGa(top.Name)} 사흘 동안 설거지 {mine}번 — {string.Join("·", free.Take(3).Select(f => f.Name))}은(는) 0번";
        }
        w.Log.Add(w.Tick, LogKind.Life, $"설거지 장부를 보고 메신저에 불만을 올렸다 ({mine}번 · 안 한 사람 {free.Count}명)", top.Id);
    }

    /// <summary>회의 안건: 설거지 당번 (주 컴퓨터가 장부를 읽어 숫자를 댄다).</summary>
    public void Agenda(MeetingRecord rec, List<CrewMember> attendees, CrewMember chair)
    {
        var w = _w;
        if (!DishAgendaPending || Roster.Count > 0) return;
        var voters = attendees.Where(c => !w.Society.OnProbation(c)).ToList();
        if (voters.Count < 2) voters = attendees;
        if (voters.Count < 2) return;
        DishAgendaPending = false;
        Stats.Agenda++;
        var counts = w.Crew.Where(c => Adult(c)).OrderByDescending(c => Share(c, LedgerKind.Dishes)).ThenBy(c => c.Id).Take(5).Select(c => $"{c.Name} {Share(c, LedgerKind.Dishes)}").ToList();
        bool comp = w.Automation.Present && w.Automation.CoreOnline;
        var item = new AgendaItem
        {
            Title = "설거지 당번 — 돌아가며 하기", Topic = "ledger:dishes", Evidence = DishAgendaWhy,
            Computer = comp ? $"주 컴퓨터: 장부 사흘 치 — 설거지 {string.Join(" · ", counts)}" : null, ComputerSign = comp ? 1 : 0,
        };
        (float, string) Opinion(CrewMember c)
        {
            int mine = Share(c, LedgerKind.Dishes), ate = Ate(c);
            if (mine >= 2) return (0.7f, "늘 하던 사람만 한다");
            if (c.Value == CrewValue.Rules) return (0.5f, "정해 두는 게 낫다");
            if (ate >= 5 && mine == 0) return (Life.Has(c, Habit.Procrastinator) || Life.Has(c, Habit.Messy) ? -0.3f : 0.15f, mine == 0 ? "할 때 하면 되지" : "");
            return (0.2f, "공평하게");
        }
        var (yes, no) = w.Meetings.Debate(voters, Opinion, c => 0.3f + c.Traits.Diligence * 0.3f, item, chair);
        bool pass = yes.Count > no.Count || yes.Count == no.Count && Opinion(chair).Item1 > 0f;
        item.Passed = pass;
        item.Outcome = pass ? "당번표를 붙였다" : "그대로";
        rec.Items.Add(item);
        if (pass)
        {
            Roster.Clear();
            Roster.AddRange(w.Crew.Where(c => Adult(c)).Select(c => c.Id));
            RosterSince = w.Tick - w.Tick % SimTime.TicksPerDay;
            Stats.Rostered++;
        }
        w.Meetings.Record(item.Title, "ledger:dishes", -1, chair, yes, no, "");
        w.History.Add(w, HistoryKind.Decision, $"회의: 설거지 당번을 돌아가며? 찬성 {yes.Count} · 반대 {no.Count} → {(pass ? "당번표" : "그대로")}", null, voters, log: true);
    }

    /// <summary>내놓은 물자: 넉넉한 사람이 고향 재료를 공동 찬장에 내놓는다 (요리 · 고마움).</summary>
    private void Donations(float hour)
    {
        var w = _w;
        if (hour < 10f || hour > 20f || !R.Chance(0.08f)) return;
        bool birthday = w.Crew.Any(c => Adult(c) && Bday(c) == SimTime.Day(w.Tick) % 30);
        var giver = w.Crew.Where(c => Adult(c) && c.CanAct && c.IsAwake && _stash.TryGetValue(c.Id, out var s) && s > 0
                && (Life.Has(c, Habit.Generous) || c.Value == CrewValue.People || birthday))
            .OrderByDescending(c => c.Traits.Sociability).ThenBy(c => c.Id).FirstOrDefault();
        if (giver != null) Donate(giver, birthday ? "생일상에 보태려고" : "같이 먹으려고");
    }

    public void Donate(CrewMember c, string why)
    {
        var w = _w;
        _stash[c.Id] = Math.Max(0, (_stash.TryGetValue(c.Id, out var s) ? s : 1) - 1);
        var dish = Dishes.HomeDish(w, c);
        int i = Dishes.IndexOf(dish.Id);
        if (i >= 0) w.Cooking.HomeGoods[i]++;
        Note(c, LedgerKind.Donate, null, $"고향 재료 ({dish.Name})");
        w.Log.Add(w.Tick, LogKind.Life, $"아껴 둔 고향 재료를 공동 찬장에 내놓았다 ({why} — {dish.Name})", c.Id);
        if (Chat.CannotRead(c) == null)
            Chat.Post(c, ChatKind.Talk, ShipChat.Voice(c, $"고향 재료 좀 내놨어. 누가 {dish.Name} 해 먹자", $"고향 재료를 찬장에 내놓았습니다. {dish.Name} 해 드세요"), about: c.Id);
    }

    // ───────────────────────────── 사진 ─────────────────────────────

    public PhotoInfo? PhotoOf(Belonging b) => b.Kind != BelongingKind.Photo ? null : Photos.FirstOrDefault(p => p.Belonging == b.Id);
    public PhotoInfo? Photo(int id) => Photos.FirstOrDefault(p => p.Id == id);
    /// <summary>벽에 건 사진은 치우지 않는다 (TidyActivity · 화면).</summary>
    public bool Hung(Belonging b) => b.Kind == BelongingKind.Photo && b.At != null && Photos.Any(p => p.Belonging == b.Id && p.Hung);

    /// <summary>사진을 찍는다: 개인 물건이 되고 메신저에 올라가고, 찍은 사람은 벽에 걸고 싶어 한다.</summary>
    public PhotoInfo Snap(CrewMember taker, Room room, PhotoScene scene, string caption, IEnumerable<CrewMember> people)
    {
        var w = _w;
        var b = w.Belongings.Seed2(taker, BelongingKind.Photo, $"{SimTime.Day(w.Tick)}일 {room.Name}에서 찍은 것");
        b.Name = caption + " 사진";
        b.Maker = taker.Id;
        var p = new PhotoInfo
        {
            Id = _nextPhoto++, Belonging = b.Id, Taker = taker.Id, Tick = w.Tick, Scene = scene, Caption = caption, Place = room.Type,
            People = people.Where(x => !x.Dead).Select(x => x.Id).Distinct().OrderBy(x => x).Take(7).ToArray(),
        };
        Photos.Add(p);
        Stats.Photos++;
        taker.Say(w, Persona.Say(taker, scene == PhotoScene.Window ? "잠깐만, 이건 찍어 둬야 해" : "자, 다들 이쪽 봐요 — 하나, 둘"));
        w.Log.Add(w.Tick, LogKind.Life, $"{caption} — 사진을 찍었다 ({p.People.Length}명)", taker.Id);
        if (Chat.CannotRead(taker) == null)
            Chat.Post(taker, ChatKind.Photo, ShipChat.Voice(taker, $"{caption} 사진 올림", $"{caption} 사진 올립니다"), photo: p.Id);
        AddIntent(InfoDo.HangPhoto, taker, -1, p.Id, default, 0.45f, $"{caption} 사진을 걸어 두고 싶다", 48f);
        return p;
    }

    /// <summary>중요한 날: 생일 · 사고를 함께 넘긴 날 · 이정표 · 창밖 풍경 (창밖 사진을 벼르던 사람).</summary>
    private void ImportantDays(float hour, bool crisis)
    {
        var w = _w;
        // 연대기의 새 줄: 함께 넘긴 고비 · 수습 · 이정표 — 곁에 있던 사람이 찍는다
        var ev = w.History.Events;
        if (_histSeen > ev.Count) _histSeen = 0;
        for (int i = _histSeen; i < ev.Count; i++)
        {
            var e = ev[i];
            if (crisis || e.Kind is not (HistoryKind.Bond or HistoryKind.Recovery or HistoryKind.Milestone) || e.CrewIds.Length < 2 || w.Tick - e.Tick > SimTime.Hours(3)) continue;
            var ppl = e.CrewIds.Select(CrewOf).Where(c => c is { Dead: false, IsAwake: true }).Cast<CrewMember>().ToList();
            var room = ppl.FirstOrDefault()?.Room;
            if (room == null || ppl.Count < 2 || ppl.Any(c => c.Room != room)) continue;
            var taker = Taker(room, ppl);
            if (taker == null || !R.Chance(0.5f)) continue;
            string cap = e.Kind switch
            {
                HistoryKind.Recovery => $"{SimTime.Day(w.Tick)}일, 고비를 넘긴 날",
                HistoryKind.Milestone => $"{SimTime.Day(w.Tick)}일 기념",
                _ => ppl.Count == 2 ? $"{Ko.WaGwa(ppl[0].Name)} {ppl[1].Name}" : $"{SimTime.Day(w.Tick)}일 {room.Name}에서",
            };
            Snap(taker, room, e.Kind == HistoryKind.Recovery ? PhotoScene.Work : PhotoScene.Group, cap, ppl);
        }
        _histSeen = ev.Count;
        if (crisis) return;
        // 생일: 생일인 사람과 둘 이상이 한 방에 모였을 때
        long day = w.Tick / SimTime.TicksPerDay;
        if (hour >= 11f && hour <= 22f && _birthdayPhotoDay != day)
            foreach (var c in w.Crew)
            {
                if (!Adult(c) || !c.IsAwake || Bday(c) != SimTime.Day(w.Tick) % 30 || c.Room is not Room room) continue;
                var mates = w.Crew.Where(o => o != c && !o.Dead && o.IsAwake && o.Room == room).ToList();
                if (mates.Count < 2) continue;
                var taker = Taker(room, mates);
                if (taker == null) continue;
                _birthdayPhotoDay = day;
                Snap(taker, room, PhotoScene.Birthday, $"{c.Name} 생일", mates.Append(c));
                break;
            }
        // 창밖 사진을 벼르던 사람
        if (R.Chance(0.12f))
            foreach (var c in w.Crew)
            {
                if (!Adult(c) || !c.IsAwake || c.Job?.Activity is not (RelaxActivity or WanderActivity or HobbyActivity) || !w.Brain2.Goals.Has(c, "gaze") || c.Room is not Room r) continue;
                if (Photos.Any(p => p.Taker == c.Id && p.Scene == PhotoScene.Window && w.Tick - p.Tick < SimTime.TicksPerDay * 3)) continue;
                Snap(c, r, PhotoScene.Window, R.Chance(0.5f) ? "창밖 성운" : "멀어지는 별빛", new[] { c }.Where(_ => false));
                break;
            }
    }

    /// <summary>찍을 사람: 사진기가 있거나 사진이 취미인 사람 → 없으면 가장 붙임성 있는 사람 (손목 단말로).</summary>
    private CrewMember? Taker(Room room, List<CrewMember> ppl)
    {
        var w = _w;
        var here = w.Crew.Where(c => !c.Dead && c.IsAwake && !c.IsChild && c.Room == room && c.Job?.Urgent != true).ToList();
        return here.Where(c => c.Hobbies.Contains(Hobby.Photography) || w.Belongings.All.Any(b => b.Owner == c.Id && b.Kind == BelongingKind.Camera)).OrderBy(c => c.Id).FirstOrDefault()
               ?? here.OrderByDescending(c => c.Traits.Sociability).ThenBy(c => c.Id).FirstOrDefault();
    }

    /// <summary>벽에 걸 자리: 바닥 칸 중 한쪽이 벽인 곳 (다른 사진과 겹치지 않게).</summary>
    public (Cell at, Cell dir)? WallSpot(Room room, Vector2 near)
    {
        var ship = _w.Ship;
        (Cell, Cell)? best = null;
        float bk = float.MaxValue;
        foreach (var c in room.Cells)
        {
            if (!ship.IsOpenFloor(c) || ship.DoorAt(c) != null) continue;
            foreach (var d in Cell.Dirs4)
            {
                var n = c + d;
                if (ship.Grid.InBounds(n) && (ship.Grid.Kind(n) == TileKind.Floor || ship.DoorAt(n) != null)) continue;
                if (Photos.Any(p => p.Hung && p.Wall == c)) continue;
                float k = (c.Center - near).LengthSquared() + (d.Y == -1 ? 0f : 4f);
                if (k < bk) { bk = k; best = (c, d); }
            }
        }
        return best;
    }

    internal void HangNow(CrewMember c, PhotoInfo p, Cell at, Cell dir)
    {
        var w = _w;
        if (w.Belongings.Get(p.Belonging) is not Belonging b) return;
        b.At = at;
        b.Holder = -1;
        p.Hung = true;
        p.Wall = at;
        p.WallDir = dir;
        Stats.Hung++;
        var room = w.Ship.RoomAt(at);
        MarkLog.Add(b.Marks, w.Tick, $"{Ko.IGa(c.Name)} {room?.Name} 벽에 걸었다");
        w.Log.Add(w.Tick, LogKind.Life, $"{p.Caption} 사진을 {room?.Name ?? "방"} 벽에 걸었다", c.Id);
    }

    /// <summary>벽의 사진을 보고 떠올린다: 떠난 사람 · 다툰 사람 · 친한 사람 — 기억과 관계가 움직인다.</summary>
    internal string LookAt(CrewMember c, PhotoInfo p)
    {
        var w = _w;
        p.Views++;
        if (!p.SeenBy.Contains(c.Id)) p.SeenBy.Add(c.Id);
        Stats.Looks++;
        _looked[c.Id] = w.Tick;
        var ppl = p.People.Select(CrewOf).Where(x => x != null && x != c).Cast<CrewMember>().ToList();
        string what;
        var dead = ppl.FirstOrDefault(x => x.Dead);
        var foe = ppl.Where(x => !x.Dead && (SpatWith(c, x) != null || c.AffinityTo(x) < -0.05f)).OrderBy(x => SpatWith(c, x) != null ? 0 : 1).ThenBy(x => c.AffinityTo(x)).ThenBy(x => x.Id).FirstOrDefault();
        var friend = ppl.Where(x => !x.Dead).OrderByDescending(x => c.AffinityTo(x)).FirstOrDefault();
        if (dead != null)
        {
            what = $"사진 속 {Ko.IGa(dead.Name)} 웃고 있다";
            w.Brain2.Emotions.Feel(c, Feeling.Sadness, 0.12f, $"{dead.Name}의 사진");
            c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.03f);
            c.GriefUntil = Math.Max(c.GriefUntil, w.Tick + SimTime.Hours(2));
        }
        else if (foe != null)
        {
            what = $"사진 속에선 {Ko.WaGwa(foe.Name)} 나란히 웃고 있었다";
            c.ChangeAffinity(foe, 0.04f);
            if (BrainSystem.Enabled) w.Brain2.Goals.Push(c, "reconcile", $"{Ko.WaGwa(foe.Name)} 화해", "사진을 보니 마음이 풀렸다", ActCat.Social, 24f, 0.9f);
        }
        else if (friend != null)
        {
            what = $"{p.Caption} — {Ko.IGa(friend.Name)} 그날 한 말이 떠오른다";
            c.ChangeAffinity(friend, 0.015f);
            w.Brain2.Emotions.Feel(c, Feeling.Joy, 0.06f, p.Caption);
        }
        else
        {
            what = $"{p.Caption} — 그날이 떠오른다";
            w.Brain2.Emotions.Feel(c, Feeling.Joy, 0.04f, p.Caption);
        }
        Stats.Remembered++;
        Life.Diary(w, c, Persona.Say(c, $"벽에 걸린 {p.Caption} 사진을 한참 봤다. {what}"));
        w.Log.Add(w.Tick, LogKind.Life, $"벽의 {p.Caption} 사진 앞에 멈춰 섰다 — {what}", c.Id);
        return what;
    }

    /// <summary>지금 볼 만한 벽 사진 (그 방에 있거나 지나는 사람).</summary>
    public (PhotoInfo p, float s, string why)? PhotoToLook(CrewMember c)
    {
        var w = _w;
        if (Photos.Count == 0 || _looked.TryGetValue(c.Id, out var t) && w.Tick - t < SimTime.Hours(10)) return null;
        (PhotoInfo, float, string)? best = null;
        foreach (var p in Photos)
        {
            if (!p.Hung) continue;
            var room = w.Ship.RoomAt(p.Wall);
            if (room == null || room != c.Room && (c.Room == null || (room.Center - c.Room.Center).LengthSquared() > 400f)) continue;
            float s = 0.1f;
            string why = $"{p.Caption} 사진";
            bool inIt = p.People.Contains(c.Id);
            if (inIt) s += 0.06f;
            if (!p.SeenBy.Contains(c.Id)) { s += 0.12f; why = $"새로 걸린 {p.Caption} 사진"; }
            foreach (var id in p.People)
            {
                if (id == c.Id || CrewOf(id) is not CrewMember o) continue;
                if (o.Dead && (c.GriefUntil > w.Tick || c.AffinityTo(o) > 0.25f)) { s += 0.25f; why = $"사진 속 {o.Name}"; }
                else if (!o.Dead && SpatWith(c, o) != null) { s += 0.15f; why = $"사진 속 {o.Name} — 다툰 뒤"; }
            }
            if (best == null || s > best.Value.Item2) best = (p, s, why);
        }
        return best;
    }

    // ───────────────────────────── 회의 시간 변경 ─────────────────────────────

    /// <summary>오후에 가끔 선장이 오늘 회의를 옮긴다 (메신저로만).</summary>
    private void MaybeMoveMeeting(float hour)
    {
        var w = _w;
        long day = w.Tick / SimTime.TicksPerDay;
        if (_moveDay == day || hour < 13f || hour > 16f) return;
        float at = Chat.MeetingHour(w.Meetings.Hour);
        if (hour > at - 1.5f || !R.Chance(0.12f)) return;
        _moveDay = day;
        var cap = w.Command.Captain;
        if (cap == null || cap.Dead || Chat.CannotRead(cap) != null) return;
        bool earlier = R.Chance(0.7f);
        string why = earlier ? R.Pick(new[] { "저녁 교대가 겹쳐서", "다들 일찍 쉬자", "내일 아침이 이르니까" }) : R.Pick(new[] { "정비가 늦게 끝날 것 같아서", "저녁 먹고 하자" });
        Chat.MoveMeeting(cap, at + (earlier ? -1f : 1f), why);
    }

    /// <summary>회의가 열리는 동안: 변경을 못 본 사람 — 컴퓨터가 스피커로 부르고, 늦게 들어오면 남는다.</summary>
    private void MeetingWatch()
    {
        var w = _w;
        var mv = Chat.Move;
        var m = w.Meetings;
        long today = w.Tick / SimTime.TicksPerDay;
        if (mv == null || mv.Day != today) { _unawareAtStart.Clear(); return; }
        if (!(m.Gathering || m.Session != null))
        {
            // 회의는 끝났는데 원래 시각이 되어서야 오는 사람
            if (mv.GatherStart >= 0 && mv.To < mv.From && SimTime.HourOfDay(w.Tick) >= mv.From)
                foreach (var id in _unawareAtStart)
                    if (CrewOf(id) is CrewMember c && Chat.Unaware(c) && !mv.Late.Contains(id) && !mv.Missed.Contains(id) && c.IsAwake && !Intents.Any(i => i.Crew == id && i.Do == InfoDo.GoMeeting))
                        AddIntent(InfoDo.GoMeeting, c, -1, -1, default, 0.7f, "원래 회의 시각", 2f);
            return;
        }
        if (mv.GatherStart < 0)
        {
            mv.GatherStart = w.Tick;
            foreach (var c in w.Crew) if (!c.Dead && Chat.Unaware(c)) _unawareAtStart.Add(c.Id);
        }
        var venue = m.Venue;
        foreach (var id in _unawareAtStart)
        {
            if (CrewOf(id) is not CrewMember c || c.Dead || mv.Late.Contains(id)) continue;
            if (venue != null && c.Room == venue && c.Job?.Activity is not (MeetingActivity or InfoActivity) && Chat.Unaware(c))
            {
                // 다른 볼일로 들어왔다가 모여 있는 걸 보고서야 안다 — 그 길로 앉는다
                Chat.Tell(c, $"{venue.Name}에 들렀다가 다들 모여 있는 걸 보고서야 회의가 당겨진 걸 알았다");
                continue;
            }
            // 모이기 시작한 뒤에야 회의하러 나선 사람 (변경을 못 봤다) — 늦었다
            if (c.Job?.Activity is MeetingActivity && !Chat.Unaware(c))
            {
                mv.Late.Add(id);
                Stats.Late++;
                Chat.LateArrivals++;
                int mins = (int)((w.Tick - mv.GatherStart) / (float)SimTime.Minutes(1));
                c.Say(w, Persona.Say(c, "회의가 당겨졌어? 메신저를 못 봤어"));
                w.Log.Add(w.Tick, LogKind.Life, $"회의가 당겨진 걸 모르고 {mins}분 늦게 회의하러 나섰다", c.Id);
                w.Brain2.Emotions.Feel(c, Feeling.Shame, 0.12f, "회의에 늦었다");
                if (w.Crew.FirstOrDefault(x => x.Id == mv.Author) is CrewMember a && !a.Dead && a != c)
                {
                    if (a.Value == CrewValue.Rules) a.ChangeAffinity(c, -0.02f);
                    else a.Say(w, Persona.Say(a, "메신저 좀 봐 —"));
                }
                Chat.Tell(c, "늦게 와서야 회의가 당겨진 걸 알았다");
                continue;
            }
            // 컴퓨터는 읽음 표시를 본다: 10분이 지나도 안 오면 그 방 스피커로 부른다
            if (!mv.Paged.Contains(id) && w.Tick - mv.GatherStart >= SimTime.Minutes(10) && m.Invited.Contains(id) && Chat.Unaware(c)
                && w.Automation.Present && w.Automation.CoreOnline && c.Room is Room r && c.IsAwake && !c.Outside && w.Automation.Speak.SpeakerWorks(r))
            {
                mv.Paged.Add(id);
                Stats.Paged++;
                Chat.Pages++;
                w.Log.Add(w.Tick, LogKind.Ship, $"[방송 · {r.Name}] {c.Name} 님, 오늘 회의는 {(int)mv.To}시로 당겨져 지금 {venue?.Name ?? "회의실"}에서 하고 있습니다");
                Chat.Tell(c, "스피커에서 제 이름을 듣고서야 회의가 당겨진 걸 알았다");
            }
        }
    }

    /// <summary>아무도 없는 회의실: 그제야 메신저를 연다.</summary>
    internal void EmptyMeeting(CrewMember c)
    {
        var w = _w;
        if (Chat.Move is not MeetingMove mv) return;
        if (!mv.Missed.Contains(c.Id)) mv.Missed.Add(c.Id);
        Stats.MissedMeeting++;
        Chat.Check(c, "아무도 없는 회의실에서 그제야 메신저를 열었다 — 회의는 벌써 끝났다");
        w.Brain2.Emotions.Feel(c, Feeling.Shame, 0.15f, "회의를 놓쳤다");
        Life.Diary(w, c, Persona.Say(c, "회의 시간이 바뀐 걸 몰랐다. 아무도 없는 방에 혼자 앉아 있었다"));
    }

    // ───────────────────────────── 할 일 (InfoActivity) ─────────────────────────────

    public InfoIntent AddIntent(InfoDo d, CrewMember c, int other, int thing, Cell at, float score, string why, float hours)
    {
        var w = _w;
        var x = Intents.FirstOrDefault(i => i.Do == d && i.Crew == c.Id && i.Other == other && i.Thing == thing);
        if (x == null) { x = new InfoIntent { Do = d, Crew = c.Id, Other = other, Thing = thing, Since = w.Tick }; Intents.Add(x); }
        x.At = at;
        x.Score = MathF.Max(x.Score, score);
        x.Why = why;
        x.Until = w.Tick + SimTime.Hours(hours);
        c.NextThinkTick = Math.Min(c.NextThinkTick, w.Tick + 1 + c.Id % 7);
        return x;
    }

    public void Drop(InfoIntent i) => Intents.Remove(i);

    // ───────────────────────────── 지문 ─────────────────────────────

    public void Hash(Action<long> I, Action<float> F)
    {
        Chat.Hash(I);
        I(Todos.Count); foreach (var t in Todos) { I(t.Owner); F(t.Progress); I(t.Done ? 1 : 0); }
        I(Ledger.Count); I(Dirty); I(Photos.Count); foreach (var p in Photos) { I(p.Hung ? 1 : 0); I(p.Views); }
        I(Cases.Count); foreach (var k in Cases) { I(k.Suspect); I(k.Finder); I(k.Explained >= 0 ? 1 : 0); I(k.Apologized >= 0 ? 1 : 0); }
        I(Intents.Count); I(Spats.Count); I(Roster.Count);
        foreach (var (k, v) in _seatUse) { I(k); I(v); }
    }
}
