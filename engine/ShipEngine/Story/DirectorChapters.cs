namespace ShipEngine;

/// <summary>원인 사슬의 박자 하나 — 그 소동에서 무게 있는 일이 난 때 · 자리 · 누가 · 무엇.</summary>
public struct Beat { public long Tick; public int Cell, Actor, Cause; public FactKind Kind; public float W; }

/// <summary>장 — 하루 · 한 주를 묶은 이야기 (제목 · 요약 줄 · 다시볼 장면 · 주인공).</summary>
public sealed class Chapter
{
    public int Day;                  // 몇째 날 (0부터 — 한 주 장이면 그 주의 마지막 날)
    public bool Week;
    public long From, To;
    public string Title = "";
    public readonly List<string> Lines = new();
    public readonly List<Highlight> Scenes = new();
    public readonly List<int> Cast = new();      // 사람 · 로봇 (마음의 번호)
    public float Peak;
    public string Arc = "";
}

/// <summary>다시보기의 컷 하나 — 언제부터 언제까지 · 그 컷의 한가운데 순간(At — 사진 한 장이면 이때) · 어디(칸) · 누구를 따라 · 빠르기 · 자막 · 갈래(조짐 · 순간 · 정점 · 시점 · 뒤).</summary>
public sealed class Shot { public long From, To, At; public int Cell = -1, Actor = -1; public double Speed; public string Caption = "", Kind = ""; }

/// <summary>
/// ⑬ 이야기 편집 (깊이 바퀴 3 · docs/ENGINE.md ⑬ '이야기 편집') — 읽기만 한다:
/// (가) 날이 바뀔 때 하루를 한 장으로 (제목 · 큰 사슬 · 사람 이야기 · 판단 · 항해 · 하루의 결 · 장면 · 주인공) · 이레마다 한 주 장.
/// (나) 하이라이트마다 컷 목록 — 첫 조짐 → 일이 난 순간들(가장 무거운 것이 정점) → 나온 사람마다의 시점 → 뒤.
/// </summary>
public sealed partial class Director
{
    public const int MaxBeats = 32, MaxShots = 8, SceneCount = 5;
    public static readonly long ShotCap = Time.Minutes(1);   // 컷 하나의 길이 (정점은 두 배)
    private static long Sec(double s) => Math.Max(1, (long)Math.Round(s / Time.SecondsPerTick));
    public readonly List<Chapter> Chapters = new();
    private int _day = -1;
    private long _dayFrom;
    private readonly List<(int actor, int cause)> _dayDeaths = new();
    private readonly List<(int depth, int cause)> _dayDecisions = new();
    private readonly List<string> _dayVoyage = new();
    private readonly List<string> _dayMoods = new();   // 하루의 결: 조용함 · 조짐 n번 · 위험(때) · 결정적 순간(때)
    private int _episodePeak, _omenRun;                // 지금 소동의 가장 높은 분위기 (0 조용함 · 1 조짐 · 2 위험 · 3 결정적 순간) · 이어진 조짐뿐인 소동 수
    private long _episodePeakAt;
    private static readonly string[] MoodNames = { "조용함", "조짐", "위험", "결정적 순간" };
    private static int MoodLevel(string m) => m switch { "결정적 순간" => 3, "위험" => 2, "조짐" => 1, _ => 0 };
    private readonly Dictionary<int, float> _dayCast = new();

    /// <summary>사실 하나를 오늘의 장에 적는다 (Observe의 사실 고리에서).</summary>
    private void DayNote(Sim sim, in Fact f, float w, int cell, int agent, CauseChain? rec)
    {
        if (rec != null && w >= 8 && rec.Beats.Count < MaxBeats) rec.Beats.Add(new Beat { Tick = f.Tick, Cell = cell, Actor = agent, Cause = f.Cause, Kind = f.Kind, W = w });
        if (f.Kind == FactKind.Died && agent >= 0) _dayDeaths.Add((agent, f.Cause));
        if (f.Kind == FactKind.Decision && f.Cause != 0 && f.Size != 0) _dayDecisions.Add((sim.Causes.Depth(f.Cause), f.Cause));
        if (f.Kind == FactKind.Ahead && f.Cause != 0) _dayVoyage.Add(sim.Causes.Text(f.Cause).Replace("항해 달력: ", ""));
        if (agent >= 0 && w >= 8) _dayCast[agent] = _dayCast.GetValueOrDefault(agent) + w;
    }

    /// <summary>날이 바뀌면 지난 하루를 장으로 · 이레마다 한 주 장 · 시간마다 분위기를 적는다.</summary>
    private void DayTick(Sim sim, long t)
    {
        int day = (int)(t / Time.TicksPerDay);
        if (_day < 0) { _day = day; _dayFrom = t; }
        int lv = MoodLevel(Mood);
        if (lv == 0 && _dayMoods.Count == 0 && _omenRun == 0 && _episodePeak == 0) _dayMoods.Add(MoodNames[0]);
        if (lv > _episodePeak) { _episodePeak = lv; _episodePeakAt = t; }
        else if (lv == 0 && _episodePeak > 0)
        {
            if (_episodePeak == 1) _omenRun++;
            else { FlushOmens(_dayMoods, ref _omenRun); _dayMoods.Add(Peak(_episodePeak, _episodePeakAt)); }
            _episodePeak = 0;
        }
        if (day == _day) return;
        var ch = BuildDay(sim, t);
        Chapters.Add(ch);
        if ((ch.Day + 1) % 7 == 0) Chapters.Add(WeekOf(Chapters.Where(c => !c.Week).TakeLast(7).ToList()));
        _day = day; _dayFrom = t;
        _dayDeaths.Clear(); _dayDecisions.Clear(); _dayVoyage.Clear(); _dayMoods.Clear(); _dayCast.Clear(); _omenRun = 0;
    }

    private static string Peak(int lv, long at) => $"{MoodNames[lv]}({at % Time.TicksPerDay / Time.TicksPerHour:00}:{at % Time.TicksPerHour * 60 / Time.TicksPerHour:00})";
    private static void FlushOmens(List<string> to, ref int n) { if (n > 0) to.Add(n == 1 ? "조짐" : $"조짐 {n}번"); n = 0; }

    /// <summary>하루의 결 — 적어 둔 것 + 아직 닫히지 않은 소동 · 지금 조용하면 끝에 '조용함'.</summary>
    private List<string> ArcParts()
    {
        var parts = new List<string>(_dayMoods);
        int omens = _omenRun + (_episodePeak == 1 ? 1 : 0);
        FlushOmens(parts, ref omens);
        if (_episodePeak >= 2) parts.Add(Peak(_episodePeak, _episodePeakAt));
        if (MoodLevel(Mood) == 0 && (parts.Count == 0 || parts[^1] != MoodNames[0])) parts.Add(MoodNames[0]);
        return parts;
    }

    /// <summary>함장이 묻는 '오늘 장 보기' — 날이 바뀌기 전 지금까지를 한 장으로 (적어 두지는 않는다).</summary>
    public Chapter ChapterNow(Sim sim) => BuildDay(sim, sim.Tick);

    private static string Tail(string story, int max = 34)
    {
        int i = story.LastIndexOf(" → ", StringComparison.Ordinal);
        string s = i >= 0 ? story[(i + 3)..] : story;
        return s.Length > max ? s[..max] + "…" : s;
    }

    private static string Ends(string story)
    {
        var parts = story.Split(" → ");
        return parts.Length <= 2 ? story : $"{parts[0]} → … → {parts[^1]}";
    }

    private Chapter BuildDay(Sim sim, long t)
    {
        var mind = sim.Find<Mind>();
        string Name(int a) => mind != null && a >= 0 && a < mind.Agents.Count ? mind.NameOf(a) : "누군가";
        int day = _day < 0 ? (int)(t / Time.TicksPerDay) : _day;
        var ch = new Chapter { Day = day, From = _dayFrom, To = t };
        var today = Highlights.Where(h => h.Tick >= _dayFrom && h.Tick < t).ToList();
        foreach (var h in today.OrderByDescending(h => h.Score).Take(SceneCount).OrderBy(h => h.Tick)) ch.Scenes.Add(h);
        ch.Peak = today.Count > 0 ? today.Max(h => h.Score) : 0;
        foreach (var kv in _dayCast.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Take(3)) ch.Cast.Add(kv.Key);
        // 제목
        var top = today.OrderByDescending(h => h.Score).FirstOrDefault();
        if (_dayDeaths.Count > 0) ch.Title = $"{day + 1}일째 — {Ko.Eul(Name(_dayDeaths[0].actor) + (_dayDeaths.Count > 1 ? $" 외 {_dayDeaths.Count - 1}명" : ""))} 잃은 날";
        else if (top != null) ch.Title = $"{day + 1}일째 — {Tail(top.Title)}";
        else if (_dayVoyage.Count > 0) ch.Title = $"{day + 1}일째 — {Tail(_dayVoyage[0])}";
        else ch.Title = $"{day + 1}일째 — 조용한 하루";
        // 요약 줄
        var big = Chains.Values.Concat(BestChains).Where(c => c.Last >= _dayFrom && c.First < t && c.Weight >= WatchAt).OrderByDescending(c => c.Weight * (1 + c.Count)).ThenBy(c => c.Root).FirstOrDefault();   // 같으면 뿌리 차례 (사전 차례에 기대지 않는다 — 이어 보기)
        if (big != null && big.Cause != 0) ch.Lines.Add($"큰 사슬 [{SpreadPath(big)}] {Ends(sim.Causes.Story(big.LineCause != 0 ? big.LineCause : big.Cause))}");
        foreach (var h in today.Where(h => h.People).OrderByDescending(h => h.Score).Take(2)) ch.Lines.Add($"사람 — {(h.Line.Length > 0 ? h.Line : Ends(h.Title))}");
        foreach (var d in _dayDeaths) ch.Lines.Add($"잃음 — {Name(d.actor)}: {(d.cause != 0 ? Ends(sim.Causes.Story(d.cause)) : "")}");
        var dec = _dayDecisions.OrderByDescending(x => x.depth).FirstOrDefault();
        if (dec.cause != 0) ch.Lines.Add($"판단 — {Ends(sim.Causes.Story(dec.cause))}");
        foreach (string v in _dayVoyage.Distinct().Take(2)) ch.Lines.Add($"항해 — {v}");
        var moods = ArcParts();
        ch.Arc = string.Join(" → ", moods.Count > 6 ? moods.Take(3).Append("…").Concat(moods.TakeLast(2)) : moods);
        if (ch.Arc.Length > 0) ch.Lines.Add($"하루의 결 — {ch.Arc}");
        if (ch.Cast.Count > 0) ch.Lines.Add($"주인공 — {string.Join(" · ", ch.Cast.Select(Name))}");
        if (top == null && _dayDeaths.Count == 0) ch.Lines.Insert(0, "큰일 없이 지나갔다");
        return ch;
    }

    private string SpreadPath(CauseChain c) => string.Join(" → ", c.Order.Select(e => PlainName[e]).Where(n => n.Length > 0).Distinct());

    /// <summary>한 주 장: 날마다의 제목 · 그 주의 가장 큰 일 · 가장 많이 나온 사람.</summary>
    public Chapter WeekOf(List<Chapter> days)
    {
        var ch = new Chapter { Week = true, Day = days.Count > 0 ? days[^1].Day : 0, From = days.Count > 0 ? days[0].From : 0, To = days.Count > 0 ? days[^1].To : 0 };
        int week = ch.Day / 7 + 1;
        var best = days.OrderByDescending(d => d.Title.Contains("잃은 날") ? 1e6 : d.Peak).FirstOrDefault();
        ch.Title = best != null && (best.Peak > 0 || best.Title.Contains("잃은 날")) ? $"{week}주째 — {best.Title[(best.Title.IndexOf('—') + 2)..]}" : $"{week}주째 — 조용한 한 주";
        foreach (var d in days) ch.Lines.Add(d.Title);
        var cast = days.SelectMany(d => d.Cast).GroupBy(a => a).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).FirstOrDefault();
        if (cast != null) { ch.Cast.Add(cast.Key); }
        foreach (var d in days) foreach (var h in d.Scenes) if (ch.Scenes.Count < SceneCount && (best == null || d == best || h.Score >= HighlightAt * 2)) ch.Scenes.Add(h);
        ch.Peak = days.Count > 0 ? days.Max(d => d.Peak) : 0;
        return ch;
    }

    // ───────────────────────── (나) 컷 목록 ─────────────────────────

    /// <summary>하이라이트를 영화처럼 다시 볼 컷들: 첫 조짐 → 일이 난 순간들(정점) → 나온 사람마다의 시점 → 뒤 (때 순 · 겹치지 않게 · 많아야 여덟).</summary>
    public List<Shot> Shots(Sim sim, Highlight h)
    {
        var mind = sim.Find<Mind>();
        string Name(int a) => mind != null && a >= 0 && a < mind.Agents.Count ? mind.NameOf(a) : "";
        var beats = (h.Beats ?? new List<Beat>()).Where(b => b.Tick >= h.StartTick && b.Tick <= h.Last + Time.Minutes(2)).OrderBy(b => b.Tick).ToList();
        var shots = new List<Shot>();
        int rootCell = h.Root != 0 && sim.Causes[h.Root].Cell >= 0 ? sim.Causes[h.Root].Cell : h.Cell;
        string rootText = h.Root != 0 ? sim.Causes.Text(h.Root) : Tail(h.Title);
        shots.Add(new Shot { From = h.StartTick, At = h.StartTick, Cell = rootCell, Speed = OmenSpeed, Caption = rootText, Kind = "조짐" });
        // 순간들: 20초 · 6칸 안이면 한 순간
        var moments = new List<List<Beat>>();
        foreach (var b in beats)
        {
            var m = moments.LastOrDefault();
            if (m != null && b.Tick - m[0].Tick <= Sec(20) && b.Cell >= 0 && m[0].Cell >= 0 && Math.Max(Math.Abs(L.X(b.Cell) - L.X(m[0].Cell)), Math.Abs(L.Y(b.Cell) - L.Y(m[0].Cell))) <= 6) m.Add(b);
            else moments.Add(new List<Beat> { b });
        }
        // 정점: 결정적인 일(구멍 · 쓰러짐 · 죽음 …)이 난 순간이 먼저 · 그다음 무게 (주엔진을 켠 것보다 운석이 뚫은 것)
        static int Grave(List<Beat> m) => m.Any(b => Critical(b.Kind)) ? 1 : 0;
        var byWeight = moments.OrderByDescending(Grave).ThenByDescending(m => m.Max(b => b.W)).ThenBy(m => m[0].Tick).ToList();
        var peak = byWeight.FirstOrDefault();
        foreach (var m in byWeight.Where(m => m == peak || m[0].Tick > h.StartTick + Sec(20)).Take(4))   // 조짐 컷이 보여 주는 첫 순간은 따로 컷을 두지 않는다 (조짐 컷이 한 틱으로 잘리지 않게)
        {
            var lead = m.OrderByDescending(b => Critical(b.Kind) ? 1 : 0).ThenByDescending(b => b.W).First();
            shots.Add(new Shot { From = Math.Max(h.StartTick, m[0].Tick - Sec(8)), At = lead.Tick, Cell = lead.Cell, Actor = lead.Actor, Speed = m == peak ? CriticalSpeed : DangerSpeed,
                Caption = lead.Cause != 0 ? sim.Causes.Text(lead.Cause) : FactText(lead.Kind), Kind = m == peak ? "정점" : "순간" });
        }
        // 여러 시점: 나온 사람마다 처음 나온 때 그 사람을 따라
        var seen = new HashSet<int>(shots.Where(s => s.Actor >= 0).Select(s => s.Actor));
        foreach (var b in beats)
        {
            if (b.Actor < 0 || !seen.Add(b.Actor) || shots.Count >= MaxShots - 1) continue;
            shots.Add(new Shot { From = Math.Max(h.StartTick, b.Tick - Sec(6)), At = b.Tick, Cell = b.Cell, Actor = b.Actor, Speed = DangerSpeed,
                Caption = $"{Name(b.Actor)}의 자리에서 — {(b.Cause != 0 ? sim.Causes.Text(b.Cause) : FactText(b.Kind))}", Kind = "시점" });
        }
        shots.Add(new Shot { From = h.Last + Sec(10), At = h.Last + Time.Minutes(1), Cell = h.Cell, Speed = DangerSpeed * 4, Caption = "그 뒤", Kind = "뒤" });
        shots = shots.OrderBy(s => s.From).ThenBy(s => s.Kind == "조짐" ? 0 : 1).Take(MaxShots).ToList();
        // 겹치지 않게: 다음 컷이 시작하면 끝 · 컷 하나는 길어야 ShotCap (그 사이는 건너뛴다 — 열네 시간 전의 뿌리에서 바로 정점으로)
        for (int i = 0; i < shots.Count; i++)
        {
            if (i + 1 < shots.Count && shots[i + 1].From <= shots[i].From) shots[i + 1].From = shots[i].From + 1;
            long cap = shots[i].Kind == "정점" ? 2 * ShotCap : ShotCap;
            shots[i].To = Math.Min(i + 1 < shots.Count ? shots[i + 1].From : long.MaxValue, shots[i].From + cap);
            shots[i].At = Math.Clamp(shots[i].At, shots[i].From, shots[i].To);
        }
        return shots;
    }
}
