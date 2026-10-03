using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.16 개인 이야기 아크 + v18.17 대화 카드 · 캠프의 밤 · 잡담 · 로맨스.
// 개인 이야기: 승무원마다 3~5단계 (가족 · 빚 · 죄책감 · 숨긴 병 · 복수 · 꿈 · 비밀 · 믿음 · 사랑 — 틀은 StoryTable.cs).
//   단계를 여는 것: 시간 · 장소(그 방에 직접 간다 — StoryActivity) · 털어놓기(가까운 사람을 찾아가 대화 카드) · 배가 다친 날 ·
//   마음이 무거운 날 · 다툼 · 누군가의 죽음. 끝: 해결 · 실패(새 갈래 틀로 이어진다) · 비극.
//   결말은 가까운 사람들의 감정 · 그 사람의 이야기(다음 단계를 당긴다 · 새 이야기를 연다) · 관계 · 회의 안건으로 번진다.
// 승무원: 이야기 단계마다 마음이 쏠리는 일(근무를 늘린다 · 취미 · 사람을 찾는다)이 목표로 걸리고, 장소를 찾아가고, 털어놓으러 간다.
// 주 컴퓨터: 기록(약장 · 근무표 · 통신 대기열 · 사고 기록)으로 먼저 알아채고 당사자에게 조용히 권하거나 함장에게 귀띔한다.
// 출신(고향 행성 · 집안 · 세대 · 믿음)은 사람마다 정해져 있고, 잡담 · 캠프의 밤 · 죽음 앞의 반응을 바꾼다.

/// <summary>출신: 고향 · 집안 · 세대 · 믿음 (번호는 StorySystem.Homes 등 표의 자리).</summary>
public readonly record struct Roots(int Home, int Class, int Gen, int Faith);

public sealed class Arc
{
    public int Id { get; init; }
    public ArcSpec Spec { get; init; } = null!;
    public int Who { get; init; }
    /// <summary>마지막으로 펼쳐진 단계 (0부터).</summary>
    public int Step { get; set; }
    public long Born { get; init; }
    public long StepAt { get; set; }
    public int Friend { get; set; } = -1;
    public int Rival { get; set; } = -1;
    public string Kin { get; set; } = "";
    public int From { get; set; } = -1;
    public string Prev { get; set; } = "";
    public ArcEnd End { get; set; }
    public long EndedAt { get; set; } = -1;
    public string Ending { get; set; } = "";
    public float Support { get; set; }
    public float Push { get; set; }
    public int PlaceMinutes { get; set; }
    public bool Confided { get; set; }
    public bool ComputerSaw { get; set; }
    public string Title { get; set; } = "";
    /// <summary>다른 사람의 결말이 이 이야기를 민 기록 (누구 · 무엇).</summary>
    public List<(int who, string what)> Touched { get; } = new();
    public List<(long t, int step, string text)> Beats { get; } = new();
    public bool Active => End == ArcEnd.None;
    public int Stages => Spec.Steps.Length + 1;
}

public sealed class StoryStats
{
    public int Arcs, Steps, Resolved, Failed, Tragic, Branches, Ripples, ComputerNotes, Visits;
    public int Cards, CardWins, CardLosses, CardBranches;
    public int Camps, CampScenes, Confessions, Fights, Makeups;
    public int SmallTalks, Gossip;
    public int Crushes, Confessed, Couples, Crises, Jealousy, Breakups, Weddings, Cheers, Whispers, Agendas;
    public string Line() => $"이야기 {Arcs}(단계 {Steps} · 풀림 {Resolved} · 어긋남 {Failed} · 무너짐 {Tragic} · 갈래 {Branches} · 번짐 {Ripples}) · " +
        $"카드 {Cards}(성공 {CardWins} · 실패 {CardLosses}) · 밤 모임 {Camps}(장면 {CampScenes}) · 잡담 {SmallTalks} · " +
        $"호감 {Crushes} · 고백 {Confessed} · 연인 {Couples} · 위기 {Crises} · 질투 {Jealousy} · 이별 {Breakups} · 결혼 {Weddings}";
}

public sealed partial class StorySystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7027 + 3301));
    public static bool Off;
    public static long UpdateTicks;
    /// <summary>이야기가 흐르는 빠르기 (시험에서 긴 항해를 줄일 때).</summary>
    public float Pace { get; set; } = 1f;
    /// <summary>시험: 저절로 새 이야기를 열지 않는다.</summary>
    public bool NoSeeds { get; set; }

    public List<Arc> Arcs { get; } = new();
    public StoryStats Stats { get; } = new();
    private int _nextArc = 1;
    private long _next, _nextTalk, _nextPanic;
    private readonly SortedDictionary<int, Roots> _roots = new();
    private readonly SortedDictionary<int, long> _seedAt = new();

    public StorySystem(World w) => _w = w;

    private CrewMember? P(int id) { if (id < 0) return null; foreach (var c in _w.Crew) if (c.Id == id) return c; return null; }
    private static bool Adult(CrewMember c) => !c.Dead && !c.IsChild;
    private string Name(int id) => P(id)?.Name ?? "누군가";

    // ───────────────────────────── 출신 ─────────────────────────────

    public static readonly string[] Homes = { "지구 바닷가 도시", "화성 굴 도시", "달 뒷면 광산촌", "세레스 정거장", "타이탄 기지", "금성 구름 도시", "가니메데 얼음 마을", "궤도 고리 정거장" };
    public static readonly string[] HomeShort = { "지구", "화성", "달", "세레스", "타이탄", "금성", "가니메데", "궤도 고리" };
    public static readonly string[] Classes = { "광산촌 집안", "공장 일꾼 집안", "장사하는 집안", "학자 집안", "군인 집안", "부잣집" };
    public static readonly string[] Gens = { "젊은 축", "한창때", "고참", "배에서 난 아이" };
    public static readonly string[] Faiths = { "별길 신자", "조상을 모시는 집", "믿는 것 없음", "기계에 이름 붙이는 사람", "명상하는 사람" };

    public Roots RootsOf(CrewMember c)
    {
        if (_roots.TryGetValue(c.Id, out var r)) return r;
        int h = (int)((uint)(c.Id * 2654435761u + (uint)_w.Seed * 40503u) >> 7);
        int home = c.Background switch
        {
            Background.Miner => h % 2 == 0 ? 2 : 3,
            Background.Diver => 6, Background.Astronomer => 7, Background.FarmResearcher or Background.Gardener => 1,
            _ => h % Homes.Length,
        };
        int cls = c.Background switch
        {
            Background.Miner or Background.Welder => 0,
            Background.Lineworker or Background.Plumber or Background.TruckDriver or Background.AutoMechanic or Background.Carpenter => 1,
            Background.Chef or Background.Baker or Background.Accountant => 2,
            Background.Physicist or Background.Astronomer or Background.Psychologist or Background.Teacher or Background.Chemist or Background.Programmer => 3,
            Background.Soldier or Background.Police or Background.MilitaryTech => 4,
            Background.Lawyer => 5,
            _ => (h >> 4) % Classes.Length,
        };
        int gen = c.BornAboard ? 3 : c.Age < 29f ? 0 : c.Age < 46f ? 1 : 2;
        int faith = c.Background == Background.Monk ? 4 : c.Habits.Contains(Habit.Superstitious) ? 3 : (h >> 9) % 3;
        r = new Roots(home, cls, gen, faith);
        _roots[c.Id] = r;
        return r;
    }

    public string RootsLine(CrewMember c) { var r = RootsOf(c); return $"{Homes[r.Home]} · {Classes[r.Class]} · {Gens[r.Gen]} · {Faiths[r.Faith]}"; }

    // ───────────────────────────── 틱 ─────────────────────────────

    public void Update(float dt)
    {
        if (Off) return;
        var w = _w;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (w.Tick >= _nextPanic) { _nextPanic = w.Tick + SimTime.Minutes(1); PanicWatch(); }
        if (w.Tick >= _nextTalk) { _nextTalk = w.Tick + SimTime.Minutes(3); SmallTalkTick(); }
        CampTick();
        if (w.Tick >= _next)
        {
            _next = w.Tick + SimTime.Minutes(10);
            ArcTick();
            LoveTick();
            CardWatch();
            ExpireTalks();
        }
        UpdateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    // ───────────────────────────── 이야기 시작 ─────────────────────────────

    public Arc? ArcOf(CrewMember c) { foreach (var a in Arcs) if (a.Who == c.Id && a.Active) return a; return null; }
    public IEnumerable<Arc> ArcsOf(CrewMember c) => Arcs.Where(a => a.Who == c.Id);

    private void ArcTick()
    {
        var w = _w;
        bool crisis = Crisis.Acting(w);
        if (!NoSeeds && !crisis)
        {
            int adults = 0, active = 0;
            foreach (var c in w.Crew) if (Adult(c)) adults++;
            foreach (var a in Arcs) if (a.Active) active++;
            int cap = Math.Max(3, (int)(adults * 0.7f));
            foreach (var c in w.Crew)
            {
                if (active >= cap) break;
                if (!Adult(c) || ArcOf(c) != null) continue;
                if (!_seedAt.TryGetValue(c.Id, out long at)) { at = SimTime.Hours(5 + (c.Id * 37 + w.Seed) % 64); _seedAt[c.Id] = at; }
                if (w.Tick < at / Math.Max(0.1f, Pace)) continue;
                if (PickSpec(c) is ArcSpec spec) { Start(spec, c); active++; }
                else _seedAt[c.Id] = w.Tick + SimTime.Hours(48);
            }
        }
        for (int i = 0; i < Arcs.Count; i++)
        {
            var a = Arcs[i];
            if (!a.Active) continue;
            var c = P(a.Who);
            if (c == null || c.Dead) { a.End = ArcEnd.Failed; a.EndedAt = w.Tick; a.Ending = "끝을 보지 못했다"; continue; }
            if (crisis) continue;
            Gate(a, c);
        }
    }

    private ArcSpec? PickSpec(CrewMember c)
    {
        var used = new HashSet<string>(Arcs.Where(a => a.Who == c.Id).Select(a => a.Spec.Key));
        float total = 0f;
        var pool = new List<(ArcSpec s, float wt)>();
        foreach (var s in StoryTable.All)
        {
            if (!s.Seed || used.Contains(s.Key)) continue;
            if (s.Key == "old_betrayer" && RivalOf(c) == null) continue;
            float wt = s.Fit(c);
            if (wt <= 0f) continue;
            // 같은 틀이 배 안에 이미 돌고 있으면 덜 고른다 (이야기가 겹치지 않게)
            if (Arcs.Any(a => a.Active && a.Spec.Key == s.Key)) wt *= 0.25f;
            pool.Add((s, wt)); total += wt;
        }
        if (pool.Count == 0) return null;
        float x = R.Float() * total;
        foreach (var (s, wt) in pool) { x -= wt; if (x <= 0f) return s; }
        return pool[^1].s;
    }

    /// <summary>이야기를 연다 (표의 틀 · 그 사람 · 앞 이야기).</summary>
    public Arc Start(ArcSpec spec, CrewMember c, Arc? from = null, string prev = "")
    {
        var w = _w;
        var a = new Arc { Id = _nextArc++, Spec = spec, Who = c.Id, Born = w.Tick, StepAt = w.Tick, From = from?.Id ?? -1, Prev = prev };
        a.Kin = KinFor(c, spec);
        a.Rival = RivalOf(c)?.Id ?? -1;
        a.Friend = FriendOf(c)?.Id ?? -1;
        a.Title = StoryTable.Fill(spec.Name, k => Slot(a, c, k));
        Arcs.Add(a);
        if (Arcs.Count > 400) Arcs.RemoveAll(x => !x.Active && w.Tick - x.EndedAt > SimTime.TicksPerDay * 20);
        Stats.Arcs++;
        Beat(a, c, 0, true);
        return a;
    }

    private string KinFor(CrewMember c, ArcSpec spec)
    {
        int h = c.Id * 7 + spec.Key.Length * 3 + _w.Seed;
        string[] pool = spec.Key switch
        {
            "child_growing" => new[] { "딸", "아들", "막내", "큰딸" },
            "corp_revenge" => new[] { "형", "누나", "아버지", "단짝" },
            "name_a_star" => new[] { "어머니", "첫 선생님", "할머니", "동생" },
            "sibling_wedding" => new[] { "동생", "언니", "형", "막내" },
            _ => c.Age < 32f ? new[] { "어머니", "아버지", "동생", "할머니" } : new[] { "어머니", "아버지", "누나", "형", "할머니" },
        };
        return pool[((h % pool.Length) + pool.Length) % pool.Length];
    }

    private CrewMember? FriendOf(CrewMember c, int not = -1)
    {
        CrewMember? best = null; float bv = 0.12f;
        foreach (var o in _w.Crew)
        {
            if (o == c || o.Id == not || !Adult(o)) continue;
            float v = o.AffinityTo(c) * 0.6f + c.AffinityTo(o) * 0.4f;
            if (v > bv) { bv = v; best = o; }
        }
        return best;
    }

    private CrewMember? RivalOf(CrewMember c)
    {
        CrewMember? best = null; float bv = -0.08f;
        foreach (var o in _w.Crew)
        {
            if (o == c || !Adult(o)) continue;
            float v = c.AffinityTo(o);
            if (v < bv) { bv = v; best = o; }
        }
        return best;
    }

    /// <summary>조각 하나: 그 사람 · 관계 · 장소 · 배 상태.</summary>
    private string Slot(Arc a, CrewMember c, string key)
    {
        var w = _w;
        switch (key)
        {
            case "kin": return a.Kin;
            case "home": return Homes[RootsOf(c).Home];
            case "friend": return a.Friend >= 0 ? Name(a.Friend) : "아무도";
            case "rival": return a.Rival >= 0 ? Name(a.Rival) : "누군가";
            case "room": return (PlaceRoom(a, c) ?? c.Room)?.Name ?? "배";
            case "sum": return $"{3 + (c.Id * 11 + w.Seed) % 17}백";
            case "prev": return a.Prev != "" ? a.Prev : "그 일";
            case "ship":
            {
                if (w.Society.Morale < 0.35f) return "배 분위기마저 가라앉은 요즘";
                if (w.History.Events.Count > 0 && w.Tick - w.History.Events[^1].Tick < SimTime.Hours(12) && w.History.Events[^1].Kind is HistoryKind.Incident or HistoryKind.Damage) return "배가 또 다친 날";
                return $"항해 {SimTime.Day(w.Tick)}일째";
            }
        }
        return key;
    }

    public string Text(Arc a, string template) { var c = P(a.Who); return c == null ? template : StoryTable.Fill(template, k => Slot(a, c, k)); }

    // ───────────────────────────── 단계 ─────────────────────────────

    private void Gate(Arc a, CrewMember c)
    {
        var w = _w;
        int next = a.Step + 1;
        float pace = Math.Max(0.1f, Pace) * (1f + a.Push);
        long since = w.Tick - a.StepAt;
        if (next >= a.Spec.Steps.Length)
        {
            if (since >= SimTime.Hours(6f / pace)) Finish(a, c);
            return;
        }
        var st = a.Spec.Steps[next];
        long min = SimTime.Hours(st.Hours / pace);
        if (since < min) return;
        bool late = since >= min * 3 + SimTime.Hours(12f / pace); // 문이 끝내 안 열리면 그냥 흘러간다
        bool open = st.Gate switch
        {
            ArcGate.Time => true,
            ArcGate.Place => PlaceOk(a, c),
            ArcGate.Confide => a.Confided || late,
            ArcGate.Hurt => HurtSince(c, a.StepAt) || late,
            ArcGate.Strain => c.Needs.Stress > 0.45f || NegFeel(c) > 0.3f || late,
            ArcGate.Quarrel => c.Quarrel > a.StepAt || late,
            ArcGate.Loss => DeathSince(a.StepAt) || late,
            _ => true,
        };
        if (st.Gate == ArcGate.Confide && !open) { ConfideIntent(a, c); return; }
        if (!open) return;
        a.Step = next;
        a.StepAt = w.Tick;
        a.PlaceMinutes = 0;
        if (st.Gate == ArcGate.Confide && !a.Confided) a.Friend = -1; // 끝내 아무에게도 말하지 못했다
        Beat(a, c, next, false);
    }

    private float NegFeel(CrewMember c)
    {
        var e = _w.Brain2.Emotions.Of(c);
        return MathF.Max(MathF.Max(e[Feeling.Sadness], e[Feeling.Fear]), MathF.Max(e[Feeling.Shame], e[Feeling.Anger]));
    }

    private bool HurtSince(CrewMember c, long since)
    {
        var ev = _w.History.Events;
        for (int i = ev.Count - 1; i >= 0 && ev[i].Tick > since; i--)
            if (ev[i].Kind is HistoryKind.Incident or HistoryKind.Damage or HistoryKind.Casualty) return true;
        return c.Vitals.Injury > 0.15f && c.Vitals.TreatedTick > since;
    }

    private bool DeathSince(long since)
    {
        foreach (var c in _w.Crew) if (c.Dead && c.DiedAt > since) return true;
        return false;
    }

    /// <summary>장소 문: 그 방(없으면 비슷한 방)에 모두 합쳐 20분 넘게 머물렀다.</summary>
    private bool PlaceOk(Arc a, CrewMember c)
    {
        var room = PlaceRoom(a, c);
        if (room == null) return true;
        if (c.Room == room) a.PlaceMinutes += 10;
        return a.PlaceMinutes >= 20;
    }

    /// <summary>다음 단계가 장소라면 찾아갈 방.</summary>
    public Room? PlaceRoom(Arc a, CrewMember c)
    {
        int next = Math.Min(a.Step + 1, a.Spec.Steps.Length - 1);
        var st = a.Spec.Steps[next].Rooms ?? a.Spec.Steps.FirstOrDefault(s => s.Rooms != null)?.Rooms;
        if (st == null) return null;
        Room? best = null; float bd = float.MaxValue;
        foreach (var kind in st)
        {
            foreach (var r in _w.Ship.Rooms)
            {
                if (r.Kind != kind || r.Cells.Count == 0) continue;
                float d = (r.Center - c.Position).LengthSquared();
                if (d < bd) { bd = d; best = r; }
            }
            if (best != null) return best;
        }
        foreach (var r in _w.Ship.Rooms) if (r.Kind is RoomType.Lounge or RoomType.Quarters or RoomType.Mess) return r;
        return null;
    }

    private static Feeling ThemeFeel(ArcTheme t) => t switch
    {
        ArcTheme.Family => Feeling.Sadness, ArcTheme.Debt => Feeling.Fear, ArcTheme.Guilt => Feeling.Shame, ArcTheme.Illness => Feeling.Fear,
        ArcTheme.Revenge => Feeling.Anger, ArcTheme.Dream => Feeling.Joy, ArcTheme.Secret => Feeling.Fear, ArcTheme.Faith => Feeling.Sadness, _ => Feeling.Sadness,
    };

    /// <summary>한 단계가 펼쳐진다: 기록 · 일기 · 감정 · 마음이 쏠리는 일 · 컴퓨터가 알아챔 · 큰 고비는 연대기에.</summary>
    private void Beat(Arc a, CrewMember c, int step, bool first)
    {
        var w = _w;
        var st = a.Spec.Steps[step];
        string text = Text(a, st.Text);
        a.Beats.Add((w.Tick, step, text));
        Stats.Steps++;
        w.Log.Add(w.Tick, LogKind.Life, $"{c.Name} — {text}", c.Id);
        Life.Diary(w, c, Persona.Say(c, text + "."));
        var f = ThemeFeel(a.Spec.Theme);
        w.Brain2.Emotions.Feel(c, f, f == Feeling.Joy ? 0.18f : 0.2f, a.Title, a.Rival >= 0 && a.Spec.Theme == ArcTheme.Revenge ? P(a.Rival) : null);
        if (f != Feeling.Joy) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.04f);
        if (st.Act != ActCat.None) w.Brain2.Goals.Push(c, "story:" + a.Spec.Key, a.Title, text, st.Act, 24f, 0.9f);
        // 복수 · 앙숙: 마주치면 사이가 더 벌어진다
        if (a.Spec.Theme == ArcTheme.Revenge && P(a.Rival) is CrewMember rv) { c.ChangeAffinity(rv, -0.05f); }
        // 꿈(고향): 꿈에 나온다
        if (a.Spec.Theme is ArcTheme.Family or ArcTheme.Love) w.After.Impress(c, DreamKind.Home, 0.5f, text, a.Friend, c.Room?.Id ?? -1, "story" + a.Id);
        if (first || step == a.Spec.Steps.Length / 2)
            w.History.Add(w, HistoryKind.Memory, $"{c.Name}의 이야기 — {a.Title}: {text}", c.Room, new[] { c });
        if (step == 1 || st.Gate == ArcGate.Place && !a.ComputerSaw) ComputerTake(a, c, st);
    }

    /// <summary>주 컴퓨터가 기록으로 먼저 알아챈다 — 당사자에게 조용히 권하거나 함장에게 귀띔한다.</summary>
    private void ComputerTake(Arc a, CrewMember c, ArcStep st)
    {
        var w = _w;
        if (a.ComputerSaw) return;
        var ai = w.Automation;
        string? said = a.Spec.Theme switch
        {
            ArcTheme.Illness => $"{c.Name} 님, 약장 기록과 작업 기록이 맞지 않습니다 — 아무에게도 알리지 않을 테니 검진을 받아 보시겠어요?",
            ArcTheme.Debt => $"{c.Name} 님, 이번 주 근무가 정해진 시간을 넘었습니다 — 다음 기항지 정산을 미리 당겨 드릴 수 있습니다",
            ArcTheme.Family => $"{c.Name} 님, 장거리 통신 창이 열리면 가장 먼저 알려 드릴게요",
            ArcTheme.Dream => $"{c.Name} 님, 쓰시는 일에 맞춰 조용한 시간을 비워 두었습니다",
            ArcTheme.Guilt => $"{c.Name} 님, 예전 기록을 다시 살펴봤습니다 — 혼자 짊어질 일이 아니었습니다",
            ArcTheme.Revenge when w.Command.Captain is CrewMember cap && cap != c && P(a.Rival) is CrewMember rv && rv != cap
                => $"함장님, {Ko.WaGwa(c.Name)} {rv.Name} 사이가 심상치 않습니다 — 당분간 같은 근무를 피하게 하시길 권합니다",
            ArcTheme.Secret => null, // 비밀은 컴퓨터도 모른다
            _ => null,
        };
        if (said == null) return;
        a.ComputerSaw = true;
        a.Support += 0.1f;
        Stats.ComputerNotes++;
        w.Log.Add(w.Tick, LogKind.Ship, $"{ai.Voice.Call}: {ai.Manner.Speak(said)}", c.Id);
    }

    // ───────────────────────────── 결말 ─────────────────────────────

    /// <summary>결말을 정한다: 털어놓은 사람의 도움 · 솜씨 · 기분 · 배 분위기 · 다른 사람이 밀어 준 힘.</summary>
    public (float win, float ruin, string why) Odds(Arc a, CrewMember c)
    {
        var w = _w;
        float friend = a.Friend >= 0 && P(a.Friend) is CrewMember f ? MathF.Max(0f, f.AffinityTo(c)) : 0f;
        float support = MathF.Min(1f, a.Support + friend * 0.5f + (a.Confided ? 0.15f : 0f));
        float skill = a.Spec.Skill is Skill s ? c.SkillLevel(s) : 0.4f;
        var e = w.Brain2.Emotions.Of(c);
        float mood = e[Feeling.Joy] * 0.5f - c.Needs.Stress * 0.5f;
        float ship = w.Society.Morale - 0.5f;
        float win = Math.Clamp(0.28f + 0.4f * support + 0.15f * (skill - 0.3f) + 0.2f * mood + 0.2f * ship + 0.15f * a.Push, 0.08f, 0.85f);
        float ruin = Math.Clamp(0.12f + 0.15f * c.Needs.Stress - 0.15f * support + (Crisis.Acting(w) ? 0.1f : 0f), 0.03f, 0.4f);
        return (win, ruin, $"도움 {support:0.00} · 솜씨 {skill:0.00} · 기분 {mood:+0.00;-0.00} · 배 {ship:+0.00;-0.00}");
    }

    public void Finish(Arc a, CrewMember c, ArcEnd? force = null)
    {
        var w = _w;
        var (pw, pr, _) = Odds(a, c);
        float x = R.Float();
        var end = force ?? (x < pw ? ArcEnd.Resolved : x < pw + pr ? ArcEnd.Tragic : ArcEnd.Failed);
        a.End = end; a.EndedAt = w.Tick;
        a.Ending = Text(a, end switch { ArcEnd.Resolved => a.Spec.Win, ArcEnd.Tragic => a.Spec.Ruin, _ => a.Spec.Lose });
        _seedAt[c.Id] = w.Tick + SimTime.Hours(36f / Math.Max(0.1f, Pace));
        switch (end) { case ArcEnd.Resolved: Stats.Resolved++; break; case ArcEnd.Tragic: Stats.Tragic++; break; default: Stats.Failed++; break; }
        w.Log.Add(w.Tick, LogKind.Life, $"{c.Name} — {a.Ending}", c.Id);
        Life.Diary(w, c, Persona.Say(c, a.Ending + "."));
        var fr = P(a.Friend);
        w.History.Add(w, end == ArcEnd.Resolved ? HistoryKind.Bond : HistoryKind.Memory, $"{c.Name}의 이야기 — {a.Title}: {a.Ending}", c.Room, fr != null ? new[] { c, fr } : new[] { c });
        var em = w.Brain2.Emotions;
        switch (end)
        {
            case ArcEnd.Resolved:
                em.Feel(c, Feeling.Joy, 0.4f, a.Title);
                if (a.Spec.Theme is ArcTheme.Dream or ArcTheme.Secret or ArcTheme.Guilt) em.Feel(c, Feeling.Pride, 0.3f, a.Title);
                c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.2f);
                if (fr != null) { w.Relations.Remember(c, fr, RelationReason.Comforted, $"{a.Title} — 끝까지 곁에 있어 줬다"); c.ChangeAffinity(fr, 0.1f); fr.ChangeAffinity(c, 0.05f); }
                break;
            case ArcEnd.Tragic:
                em.Feel(c, Feeling.Sadness, 0.55f, a.Title);
                c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.2f);
                c.GriefUntil = Math.Max(c.GriefUntil, w.Tick + SimTime.Hours(30));
                break;
            default:
                em.Feel(c, Feeling.Sadness, 0.2f, a.Title);
                break;
        }
        ThemeEnd(a, c, end);
        Ripple(a, c, end);
        // 실패도 새 갈래
        if (end == ArcEnd.Failed && a.Spec.Branch != "" && StoryTable.Get(a.Spec.Branch) is ArcSpec br && !ArcsOf(c).Any(x => x.Spec.Key == br.Key))
        {
            Stats.Branches++;
            Start(br, c, a, a.Title);
        }
    }

    private void ThemeEnd(Arc a, CrewMember c, ArcEnd end)
    {
        var w = _w;
        var rv = P(a.Rival);
        switch (a.Spec.Key)
        {
            case "old_betrayer" when rv != null && !rv.Dead:
                if (end == ArcEnd.Resolved) { w.Relations.Remember(c, rv, RelationReason.Apologized, "그날 일을 사과했다"); c.ChangeAffinity(rv, 0.2f); rv.ChangeAffinity(c, 0.1f); }
                else if (end == ArcEnd.Tragic)
                {
                    rv.Vitals.Injury = MathF.Min(1f, rv.Vitals.Injury + 0.08f); rv.Vitals.InjuryCause ??= $"{c.Name}의 주먹";
                    w.Relations.Remember(rv, c, RelationReason.BlamedMe, "나에게 주먹을 휘둘렀다");
                    rv.ChangeAffinity(c, -0.3f);
                    if (!MotionSystem.Off) w.Motions.Propose(rv, MotionKind.Accusation, SittingKind.Trial, $"{c.Name}의 주먹질", "식당 앞에서 나를 때렸다", target: c.Id);
                    Stats.Agendas++;
                }
                break;
            case "fake_license" when end == ArcEnd.Tragic && !MotionSystem.Off && rv != null && !rv.Dead:
                w.Motions.Propose(rv, MotionKind.Accusation, SittingKind.Trial, $"{c.Name}의 자격증", "산 자격으로 일해 왔다", target: c.Id);
                Stats.Agendas++;
                break;
            case "hidden_tremor" when end == ArcEnd.Tragic:
                c.Vitals.Injury = MathF.Min(1f, c.Vitals.Injury + 0.12f); c.Vitals.InjuryCause ??= "떨리는 손으로 하던 작업";
                break;
            case "gambling_debt" or "shop_debt" when P(a.Friend) is CrewMember f:
                w.Relations.Remember(f, c, end == ArcEnd.Resolved ? RelationReason.KeptPromise : RelationReason.OwesMe, end == ArcEnd.Resolved ? "빌린 돈을 갚았다" : "빌린 돈을 아직 못 받았다");
                break;
        }
    }

    /// <summary>결말이 다른 사람의 이야기에 번진다.</summary>
    private void Ripple(Arc a, CrewMember c, ArcEnd end)
    {
        var w = _w;
        var em = w.Brain2.Emotions;
        foreach (var o in w.Crew)
        {
            if (o == c || !Adult(o)) continue;
            bool close = o.Id == a.Friend || o.AffinityTo(c) >= 0.3f;
            if (!close) continue;
            if (end == ArcEnd.Resolved) em.Feel(o, Feeling.Joy, 0.15f, $"{c.Name}의 일", c);
            else if (end == ArcEnd.Tragic) em.Feel(o, Feeling.Sadness, 0.25f, $"{c.Name}의 일", c);
            if (ArcOf(o) is Arc oa && oa.Active)
            {
                // 같은 결의 이야기일수록 크게 민다: 꿈은 꿈을, 비극은 망설임을
                float push = end == ArcEnd.Resolved ? (oa.Spec.Theme == a.Spec.Theme ? 0.8f : 0.4f) : end == ArcEnd.Tragic ? 0.3f : 0.1f;
                oa.Push += push;
                if (end == ArcEnd.Resolved) oa.Support += 0.1f;
                string what = end == ArcEnd.Resolved ? $"{c.Name}의 일을 보고 용기를 냈다" : end == ArcEnd.Tragic ? $"{c.Name}의 일 이후 서두르게 됐다" : $"{c.Name}의 일을 들었다";
                oa.Touched.Add((c.Id, what));
                Stats.Ripples++;
                if (push >= 0.3f) Life.Diary(w, o, Persona.Say(o, $"{what}."));
            }
            else if (end == ArcEnd.Tragic && o.Id == a.Friend && a.Spec.Theme is ArcTheme.Illness or ArcTheme.Guilt or ArcTheme.Family or ArcTheme.Faith
                && StoryTable.Get("survivor_guilt") is ArcSpec sg && !ArcsOf(o).Any(x => x.Spec.Key == sg.Key))
            {
                var na = Start(sg, o, a, $"{c.Name}의 일");
                na.Touched.Add((c.Id, "그 사람의 결말이 이 이야기를 열었다"));
                Stats.Ripples++;
            }
        }
    }

    // ───────────────────────────── 지문 ─────────────────────────────

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Arcs.Count); I(_nextArc);
        foreach (var a in Arcs) { I(a.Id * 31L + a.Who); I(a.Step); I((long)a.End); I(a.EndedAt); F(a.Support); F(a.Push); }
        I(Cards.Count); foreach (var k in Cards) { I(k.Id); I(k.Chosen); I(k.Success ? 1 : 0); }
        I(Loves.Count); foreach (var l in Loves) { I(l.A * 4096L + l.B); I((long)l.Stage); I(l.Since); }
        I(Stats.SmallTalks); I(Stats.Camps); I(Stats.CampScenes); I(_talks.Count);
    }
}
