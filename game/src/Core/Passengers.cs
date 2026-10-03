using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.6 승객 (가볍게) — 배 일을 하지 않는 사람. 구조한 생존자 · 기항지 손님으로 탄다.
//  · 일: 당직 · 정비 · 비상 배치에서 빠진다. 자원봉사하는 승객은 가벼운 일(수확 · 요리 · 물 나르기 · 간이침대 · 정리)을 거든다.
//  · 불만: 제 침대가 없다 · 춥다/덥다 · 어둡다 · 시끄러워 못 잔다 · 배고프다 · 경보가 무섭다 · 심심하다 → 마음이 깎이다가 승무원(함장이 먼저)을 찾아가 따진다.
//    들어 준 사람(붙임성 · 정)은 담요 · 귀마개 · 간이침대로 달래고 가까워진다. 흘려들은 사람은 서먹해지고 마음이 무겁다.
//    같은 불만이 쌓이면 주컴퓨터가 장부에 적고 고칠 길을 낸다.
//  · 불: 훈련받지 않은 승객은 불길 가까이에서 쉽게 공황에 빠진다 (주컴퓨터가 승객에게 갈 곳을 방송하면 조금 덜하다).
//    자원봉사 승객이 공황에 빠진 사람에게 가서 손을 잡고 안전한 방으로 이끈다 — 본 사람들은 그 사람을 다시 본다.
//  · 승객끼리 · 승무원과: 자원봉사 승객이 불만 많은 승객을 달랜다 · 데려온 사람에게 정이 간다 (도킹).
// 난수는 전용(시드 × 소수). 1분마다 불 · 10분마다 마음.

public sealed class PassengerInfo
{
    public int Id { get; init; }
    public string From { get; init; } = "";
    public bool Rescued { get; init; }
    public bool Volunteer { get; init; }
    /// <summary>마음 (0 = 화가 났다 · 1 = 편하다).</summary>
    public float Content { get; set; } = 0.7f;
    public int Complaints { get; set; }
    public long NextGripe { get; set; }
    /// <summary>지금 하려는 불평 ("" = 없음).</summary>
    public string Gripe { get; set; } = "";
    public string LastGripe { get; set; } = "";
    public int Heard { get; set; }
    public int Brushed { get; set; }
    /// <summary>생김 (짐 · 옷 · 모자 · 무늬 — 그림이 승객마다 다르다).</summary>
    public int Look { get; init; }
    public int Led { get; set; }
    public int Comforted { get; set; }
    public long BoardedAt { get; init; }
    public long PanicAt { get; set; } = -1;
    public int LedBy { get; set; } = -1;
    public long LedAt { get; set; } = -1;
}

public sealed class PassengerStats
{
    public int Boarded, Rescued, Guests, Complaints, Heard, Brushed, Panics, Led, Comforts, Advices, Broadcasts, VolunteerChores;
}

public sealed class PassengerSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 8803 + 401));
    private long _nextFire, _nextMind, _fireCall = -1;
    public static bool Off;
    public static long UpdateTicks;
    public SortedDictionary<int, PassengerInfo> All { get; } = new();
    public PassengerStats Stats { get; } = new();
    public SortedDictionary<string, int> GripeCount { get; } = new();
    /// <summary>이끄는 사람 → 이끌리는 사람 · 갈 곳.</summary>
    private readonly SortedDictionary<int, (int who, Cell to, long until)> _follow = new();
    public int Version { get; private set; }

    public PassengerSystem(World w) { _w = w; }

    public PassengerInfo? Of(CrewMember c) => c.Passenger && All.TryGetValue(c.Id, out var p) ? p : null;
    public IEnumerable<CrewMember> Aboard => All.Keys.Select(id => _w.Crew[id]).Where(c => !c.Dead && !c.Away);
    public (int who, Cell to, long until)? Following(CrewMember c) => _follow.TryGetValue(c.Id, out var f) && _w.Tick < f.until ? f : null;
    private bool ComputerOn => _w.Automation.Present && _w.Automation.MainOnline;

    /// <summary>승객이 탄다 (구조한 생존자는 지쳐서 · 기항지 손님은 짐을 들고).</summary>
    public CrewMember? Board(Cell at, string from, bool rescued, bool? volunteer = null)
    {
        var w = _w;
        if (Off || w.Crew.Count >= World.MaxCrew) return null;
        var c = w.AddSurvivor(at);
        c.Passenger = true;
        bool vol = volunteer ?? (c.Traits.Bravery > 0.5f && c.Traits.Calm > 0.4f || R.Chance(0.15f)); // 겁이 덜하고 차분한 사람이 나선다
        if (!rescued)
        {
            c.Rescued = false; // 기항지 손님
            c.Vitals.Health = 1f;
            c.Vitals.Injury = 0f;
            c.Vitals.Wounds.Clear();
            c.Vitals.InjuryCause = null;
            c.Memory.Trauma = 0f;
            c.Needs.Food = 0.8f; c.Needs.Rest = 0.8f; c.Needs.Stress = 0.25f;
        }
        c.Joined = rescued ? $"{from}에서 구해 온 승객" : $"{from}에서 탄 승객";
        var p = new PassengerInfo { Id = c.Id, From = from, Rescued = rescued, Volunteer = vol, Look = R.Range(0, 1 << 16), BoardedAt = w.Tick, Content = rescued ? 0.75f : 0.65f, NextGripe = w.Tick + SimTime.Hours(6) };
        All[c.Id] = p;
        Stats.Boarded++;
        if (rescued) Stats.Rescued++; else Stats.Guests++;
        w.Log.Add(w.Tick, LogKind.Life, rescued ? $"승객으로 탔다 — {from}에서 구해 왔다" : $"승객으로 탔다 — {from}에서 · 짐 하나", c.Id);
        if (ComputerOn)
            w.Automation.Speak.Announce(w.Automation.Voice.Style($"승객 {c.Name} 탑승 — {(c.Bed != null && c.Bed.Type == FurnitureType.Bed ? c.Bed.Room.Name + "에 침대" : "간이침대")} · 비상시엔 승무원을 따른다"), w.Ship.RoomAt(at), 0);
        Version++;
        return c;
    }

    /// <summary>자원봉사 승객이 거들 수 있는 가벼운 일 (Chores.Appeal).</summary>
    public static bool MayWork(CrewMember c, World w, WorkOrder o)
    {
        if (!c.Passenger) return true;
        var p = w.Passengers.Of(c);
        if (p == null || !p.Volunteer) return false;
        return o.Kind is WorkKind.Harvest or WorkKind.Tend or WorkKind.Cook or WorkKind.Restock or WorkKind.CarryWater or WorkKind.StowCot or WorkKind.Recycle;
    }

    public void Update(float dt)
    {
        var w = _w;
        if (Off || All.Count == 0) return;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (w.Tick >= _nextFire)
        {
            _nextFire = w.Tick + SimTime.Minutes(1);
            if (w.Fire.Count > 0) FirePanic();
            foreach (var id in _follow.Where(kv => w.Tick >= kv.Value.until).Select(kv => kv.Key).ToList()) _follow.Remove(id);
        }
        if (w.Tick >= _nextMind)
        {
            _nextMind = w.Tick + SimTime.Minutes(10);
            foreach (var p in All.Values) Mind(p);
        }
        UpdateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    // ─────────────────────────────── 불 · 공황 ───────────────────────────────

    private void FirePanic()
    {
        var w = _w;
        foreach (var p in All.Values)
        {
            var c = w.Crew[p.Id];
            if (!c.CanAct || !c.IsAwake || c.Outside || c.Mind.Panicking(w.Tick) || _follow.ContainsKey(c.Id)) continue;
            if (p.PanicAt >= 0 && w.Tick - p.PanicAt < SimTime.Minutes(20)) continue;
            if (!w.Fire.AnyWithin(c.Cell, 6f)) continue;
            // 주컴퓨터: 불 가까이 승객이 있으면 갈 곳을 방송한다 (한 번)
            bool told = false;
            if (ComputerOn)
            {
                if (_fireCall < 0 || w.Tick - _fireCall > SimTime.Minutes(30))
                {
                    _fireCall = w.Tick;
                    Stats.Broadcasts++;
                    var safe = SafeRoom(c);
                    w.Automation.Speak.Announce(w.Automation.Voice.Style($"승객은 {Ko.EuRo(safe?.Name ?? "식당")} — 낮게 · 벽을 짚고 · 승무원 말을 따를 것"), c.Room, 2);
                }
                told = true;
            }
            float chance = 0.4f * MathF.Pow(1f - c.Traits.Calm, 1.2f) * (p.Volunteer ? 0.15f : 1f) * (told ? 0.7f : 1f) * (c.Fears.Contains(Fear.Fire) ? 1.6f : 1f);
            if (!R.Chance(chance)) continue;
            var m = c.Mind;
            m.Panics++;
            m.Frozen = c.Traits.Bravery < 0.5f && R.Chance(0.6f);
            m.PanicUntil = w.Tick + SimTime.Minutes(m.Frozen ? 6f + 4f * R.Float() : 4f + 3f * R.Float());
            p.PanicAt = w.Tick;
            Stats.Panics++;
            c.EndJob(w, ToilStatus.Interrupted);
            c.NextThinkTick = w.Tick;
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.1f);
            w.Log.Add(w.Tick, LogKind.Warning, m.Frozen ? "불길 앞에서 공황 — 얼어붙었다 (배를 처음 타 본 승객)" : "불길 앞에서 공황 — 정신없이 달아난다", c.Id);
            MarkLog.Add(c.Memory.Marks, w.Tick, "불길 앞에서 공황에 빠졌다");
            Version++;
        }
    }

    internal Room? SafeRoom(CrewMember c)
    {
        var w = _w;
        return w.Ship.LiveRooms.Where(r => !r.Detached && Atmosphere.Danger(r) <= 0.1f && !r.Leaking && w.Fire.CountIn(r) == 0 && r != c.Room)
            .OrderBy(r => r.Type is RoomType.Mess or RoomType.Lounge ? 0 : 1).ThenBy(r => (r.Center - c.Position).LengthSquared()).ThenBy(r => r.Id).FirstOrDefault();
    }

    /// <summary>이끌 사람: 공황에 빠진 사람 (승객 먼저 · 가까운 사람).</summary>
    public CrewMember? LeadTarget(CrewMember v, DistanceField dist)
    {
        var w = _w;
        if (Of(v) is not PassengerInfo p || !p.Volunteer || !v.CanAct || !v.IsAwake || v.Mind.Panicking(w.Tick) || v.Outside) return null;
        CrewMember? best = null;
        float bs = float.MaxValue;
        foreach (var o in w.Crew)
        {
            if (o == v || o.Dead || o.Down || o.Outside || !o.Mind.Panicking(w.Tick) || _follow.ContainsKey(o.Id) || !dist.Reachable(o.Cell)) continue;
            if (_follow.Values.Any(f => f.who == o.Id)) continue;
            float d = dist.Get(o.Cell) + (o.Passenger ? 0f : 6f);
            if (d > 30f || d >= bs) continue;
            bs = d; best = o;
        }
        return best;
    }

    /// <summary>손을 잡았다: 공황이 가라앉고 · 이끄는 사람을 따라 안전한 방으로.</summary>
    internal Cell? SafeSpot(CrewMember o)
    {
        var w = _w;
        return SafeRoom(o)?.Cells.Where(x => w.Ship.IsOpenFloor(x) && !w.IsSpotTaken(x, o)).OrderBy(x => x.X).ThenBy(x => x.Y).Cast<Cell?>().FirstOrDefault();
    }

    internal bool TakeHand(CrewMember v, CrewMember o, Cell dest, PassengerActivity act)
    {
        var w = _w;
        if (!o.Mind.Panicking(w.Tick) || (v.Position - o.Position).Length() > 2.5f) return false;
        var room = w.Ship.RoomAt(dest);
        o.Mind.PanicUntil = w.Tick;
        o.Mind.Frozen = false;
        o.EndJob(w, ToilStatus.Interrupted);
        _follow[o.Id] = (v.Id, dest, w.Tick + SimTime.Minutes(25));
        o.StartJob(new Job(act, "손을 잡고 따라간다", new List<Toil> { new GotoToil(dest), new WaitToil(SimTime.Minutes(10), Pose.Sitting) }) { LogText = $"{v.Name}의 손을 잡고 따라간다", LogKind = LogKind.Warning }, w, null);
        o.Affinity[v.Id] = MathF.Min(1f, o.AffinityTo(v) + 0.3f);
        o.Needs.Stress = MathF.Max(0f, o.Needs.Stress - 0.1f);
        var p = Of(v)!;
        p.Led++;
        Stats.Led++;
        if (Of(o) is PassengerInfo op) op.LedBy = v.Id;
        // 본 사람들은 그 사람을 다시 본다
        foreach (var x in w.Crew)
            if (x != v && x != o && !x.Dead && x.IsAwake && x.Room != null && x.Room == v.Room) x.Affinity[v.Id] = MathF.Min(1f, x.AffinityTo(v) + 0.08f);
        v.Say(w, Persona.Say(v, $"{o.Name}, 내 손 잡아요 — {Ko.EuRo(room!.Name)} 가요. 천천히"));
        w.Log.Add(w.Tick, LogKind.Warning, $"공황에 빠진 {Ko.EulReul(o.Name)} 손잡아 {Ko.EuRo(room.Name)} 이끈다", v.Id);
        w.History.Add(w, HistoryKind.Bond, $"불이 난 날 — 승객 {Ko.IGa(v.Name)} 공황에 빠진 {Ko.EulReul(o.Name)} 이끌고 {Ko.EuRo(room.Name)} 나왔다", room, new[] { v, o }, log: false);
        MarkLog.Add(v.Memory.Marks, w.Tick, $"불길 속에서 {Ko.EulReul(o.Name)} 이끌었다");
        Version++;
        return true;
    }

    // ─────────────────────────────── 마음 · 불만 ───────────────────────────────

    private string Gripes(CrewMember c, out int n)
    {
        var w = _w;
        int k = 0;
        string first = "";
        void G(bool on, string s) { if (!on) return; k++; if (first == "") first = s; }
        int hour = (int)(w.Tick % SimTime.TicksPerDay / SimTime.TicksPerHour);
        bool night = hour >= 23 || hour < 6;
        G(c.Bed == null || c.Bed.Type != FurnitureType.Bed, "침대");
        G(c.Room != null && c.Room.Air.Temperature < 17f, "추위");
        G(c.Room != null && c.Room.Air.Temperature > 28f, "더위");
        G(c.Room != null && c.Room.Dark, "어둠");
        G(night && c.Room != null && c.Room.Noise > 0.45f, "소음");
        G(c.Needs.Hunger > 0.7f, "배고픔");
        G(Crisis.Acting(w), "경보");
        G(c.Needs.Social < 0.2f, "심심함");
        n = k;
        return first;
    }

    private void Mind(PassengerInfo p)
    {
        var w = _w;
        var c = w.Crew[p.Id];
        if (c.Dead || c.Away) return;
        string g = Gripes(c, out int n);
        float care = 0f;
        foreach (var o in w.Crew) if (!o.Passenger && !o.Dead && o.AffinityTo(c) > 0.3f) care += 0.004f; // 챙겨 주는 승무원
        p.Content = Math.Clamp(p.Content + (n == 0 ? 0.015f : -0.018f * n) + MathF.Min(0.02f, care) + (p.Volunteer ? 0.005f : 0f), 0f, 1f);
        if (p.Gripe == "" && n > 0 && p.Content < 0.4f && w.Tick >= p.NextGripe && !c.Mind.Panicking(w.Tick)) { p.Gripe = g; Version++; }
    }

    /// <summary>따질 상대: 깨어 있는 함장이 먼저 · 아니면 가까운 승무원.</summary>
    public CrewMember? GripeTarget(CrewMember c, DistanceField dist)
    {
        var w = _w;
        if (w.Command.Captain is CrewMember cap && cap != c && cap.CanAct && cap.IsAwake && !cap.Outside && dist.Reachable(cap.Cell) && cap.Job?.Urgent != true) return cap;
        return w.Crew.Where(o => o != c && !o.Passenger && !o.IsChild && o.CanAct && o.IsAwake && !o.Outside && o.Job?.Urgent != true && dist.Reachable(o.Cell))
            .OrderBy(o => dist.Get(o.Cell)).ThenBy(o => o.Id).FirstOrDefault();
    }

    /// <summary>불평을 다 했다: 들어 준 사람은 달래고, 흘려들은 사람은 서먹해진다.</summary>
    internal void Complain(CrewMember c, CrewMember to)
    {
        var w = _w;
        if (Of(c) is not PassengerInfo p || p.Gripe == "") return;
        string g = p.Gripe;
        p.Gripe = "";
        p.LastGripe = g;
        p.Complaints++;
        p.NextGripe = w.Tick + SimTime.Hours(8);
        Stats.Complaints++;
        GripeCount[g] = GripeCount.GetValueOrDefault(g) + 1;
        string said = g switch
        {
            "침대" => "표를 끊고 탔는데 바닥 같은 간이침대라니요", "추위" => "방이 너무 추워서 잠을 못 자요", "더위" => "숨이 막혀요, 방이 너무 더워요",
            "어둠" => "불은 언제 들어와요? 아무것도 안 보여요", "소음" => "밤새 쿵쿵거려서 한숨도 못 잤어요", "배고픔" => "밥은 언제 나와요?",
            "경보" => "이 배, 괜찮은 거 맞아요? 경보가 또 울려요", _ => "하루 종일 할 게 없어요",
        };
        c.Say(w, Persona.Say(c, said));
        bool listen = to.Traits.Sociability + to.AffinityTo(c) + (w.Command.Captain == to ? 0.15f : 0f) - to.Needs.Stress * 0.5f > 0.5f;
        if (listen)
        {
            string fix = g switch { "침대" => "간이침대에 매트를 하나 더 깔아 주겠다", "추위" => "담요를 갖다주겠다", "소음" => "귀마개를 주겠다", "배고픔" => "남은 끼니를 데워 주겠다", "심심함" => "저녁에 카드 치러 오라고 했다", _ => "괜찮다고, 우리가 보고 있다고 했다" };
            p.Content = MathF.Min(1f, p.Content + 0.25f);
            p.Heard++;
            Stats.Heard++;
            c.Affinity[to.Id] = MathF.Min(1f, c.AffinityTo(to) + 0.12f);
            to.Affinity[c.Id] = MathF.Min(1f, to.AffinityTo(c) + 0.06f);
            to.Needs.Stress = MathF.Min(1f, to.Needs.Stress + 0.02f);
            to.Say(w, Persona.Say(to, fix));
            w.Log.Add(w.Tick, LogKind.Life, $"승객 {Ko.IGa(c.Name)} 따지러 왔다 ({g}) — {fix}", to.Id);
        }
        else
        {
            p.Content = MathF.Max(0f, p.Content - 0.05f);
            p.Brushed++;
            Stats.Brushed++;
            c.Affinity[to.Id] = MathF.Max(-1f, c.AffinityTo(to) - 0.12f);
            to.Affinity[c.Id] = MathF.Max(-1f, to.AffinityTo(c) - 0.05f);
            to.Needs.Stress = MathF.Min(1f, to.Needs.Stress + 0.04f);
            to.Say(w, Persona.Say(to, "지금 바빠요 — 나중에"));
            w.Log.Add(w.Tick, LogKind.Life, $"승객 {Ko.IGa(c.Name)} 따지러 왔다 ({g}) — 흘려들었다", to.Id);
            Life.Diary(w, c, Persona.Say(c, $"{Ko.EunNeun(to.Name)} 내 말을 듣는 둥 마는 둥 했다"));
        }
        // 주컴퓨터: 같은 불만이 쌓이면 장부에 적고 길을 낸다
        if (ComputerOn && GripeCount[g] >= 2)
        {
            string remedy = g switch { "침대" => "빈 선실 정리 · 간이침대 매트", "추위" => "승객 방 온도 2도 올리기", "더위" => "승객 방 환기량 올리기", "소음" => "밤 소음 작업 미루기", "어둠" => "승객 방 조명 먼저", "배고픔" => "승객 끼니 따로 챙기기", "경보" => "승객에게 상황 먼저 알리기", _ => "저녁 모임에 승객도" };
            var act = w.Automation.Book.Add(ActKind.Advice, c.Room, $"승객 불만: {g} {GripeCount[g]}번", $"{c.Name} 외 — 쌓이면 승객끼리 들고일어난다", remedy, "", "pax:" + g, SimTime.Hours(6));
            if (act != null) Stats.Advices++;
        }
        Version++;
    }

    /// <summary>자원봉사 승객이 마음 상한 승객을 달랜다.</summary>
    public CrewMember? ComfortTarget(CrewMember v, DistanceField dist)
    {
        var w = _w;
        if (Of(v) is not PassengerInfo p || !p.Volunteer) return null;
        foreach (var q in All.Values)
        {
            if (q.Id == v.Id || q.Content > 0.4f) continue;
            var o = w.Crew[q.Id];
            if (!o.Dead && o.IsAwake && !o.Outside && dist.Reachable(o.Cell) && dist.Get(o.Cell) < 40f) return o;
        }
        return null;
    }

    internal void Comfort(CrewMember v, CrewMember o)
    {
        var w = _w;
        if (Of(o) is not PassengerInfo q || (v.Position - o.Position).Length() > 3f) return;
        q.Content = MathF.Min(1f, q.Content + 0.15f);
        q.Comforted++;
        Stats.Comforts++;
        o.Affinity[v.Id] = MathF.Min(1f, o.AffinityTo(v) + 0.08f);
        v.Affinity[o.Id] = MathF.Min(1f, v.AffinityTo(o) + 0.05f);
        o.Needs.Social = MathF.Min(1f, o.Needs.Social + 0.2f);
        v.Say(w, Persona.Say(v, "나도 처음엔 그랬어요. 여기 사람들, 괜찮아요"));
        Version++;
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(All.Count);
        foreach (var p in All.Values) { I(p.Id); F(p.Content); I(p.Complaints); I(p.Led); I(p.PanicAt); }
        I(_follow.Count);
        I(Stats.Panics); I(Stats.Comforts);
    }
}

/// <summary>v18.6 승객: 이끌기(불 · 공황) · 따라가기 · 따지러 가기 · 달래기.</summary>
public sealed class PassengerActivity : Activity
{
    public override string Id => "passenger";
    public override string Label => "승객";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var ps = w.Passengers;
        if (PassengerSystem.Off || ps.All.Count == 0 || !c.CanAct) return (0f, "—");
        if (ps.Following(c) is { } f) return (4f, $"{w.Crew[f.who].Name}의 손을 잡고 따라간다");
        if (c.Job?.Activity == this) return (c.Job.Label == "공황에 빠진 사람 이끌기" ? 3.5f : 0.7f, c.Job.Label);
        if (!c.Passenger || ps.Of(c) is not PassengerInfo p) return (0f, "—");
        if (ps.LeadTarget(c, dist) is CrewMember o) return (3.5f, $"{Ko.IGa(o.Name)} 공황에 빠졌다 — 손을 잡아 이끈다");
        if (p.Gripe != "" && !Crisis.Acting(w) && ps.GripeTarget(c, dist) is CrewMember t) return (0.62f, $"따지러 간다 ({p.Gripe}) — {t.Name}");
        if (p.Volunteer && !Crisis.Acting(w) && ps.ComfortTarget(c, dist) is CrewMember q) return (0.42f, $"{Ko.EulReul(q.Name)} 달래러 간다");
        return (0f, "—");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var ps = w.Passengers;
        if (ps.Of(c) is not PassengerInfo p) return null;
        if (ps.LeadTarget(c, dist) is CrewMember o && ps.SafeSpot(o) is Cell dest)
        {
            var near = w.Ship.IsOpenFloor(dest + new Cell(1, 0)) ? dest + new Cell(1, 0) : dest;
            var toils = new List<Toil>
            {
                new GotoToil(o.Cell, cm => o.Mind.Panicking(w.Tick)),
                new DoToil((cm, world) => world.Passengers.TakeHand(cm, o, dest, this)),
                new GotoToil(near),
                new WaitToil(SimTime.Minutes(8), Pose.Standing, dest.Center),
            };
            return new Job(this, "공황에 빠진 사람 이끌기", toils) { LogText = $"공황에 빠진 {Ko.EulReul(o.Name)} 데리러 간다", LogKind = LogKind.Warning, Urgent = true };
        }
        if (p.Gripe != "" && ps.GripeTarget(c, dist) is CrewMember t)
            return new Job(this, "따지러 간다", new List<Toil>
            {
                new GotoToil(t.Cell, cm => t.CanAct && !t.Outside),
                new WaitToil(SimTime.Minutes(3), Pose.Standing, t.Position),
                new DoToil((cm, world) => { world.Passengers.Complain(cm, t); return true; }),
            }) { LogText = $"{t.Name}에게 따지러 간다 ({p.Gripe})", LogKind = LogKind.Life };
        if (p.Volunteer && ps.ComfortTarget(c, dist) is CrewMember q)
            return new Job(this, "달래기", new List<Toil>
            {
                new GotoToil(q.Cell),
                new WaitToil(SimTime.Minutes(12), Pose.Sitting, q.Position),
                new DoToil((cm, world) => { world.Passengers.Comfort(cm, q); return true; }),
            }) { LogText = $"{Ko.EulReul(q.Name)} 달래러 간다", LogKind = LogKind.Life };
        return null;
    }
}
