using ShipEngine;

namespace ShipView;

/// <summary>경과 카드 가로 시계의 점 (0~1 — 일어남부터 끝 · 열린 경과는 지금까지): 갈래 0 일어남 · 1 처음 안 이 · 2 주컴퓨터가 앎 · 3 맡김 · 4 닿음 · 5 끔 · 막음 · 고침 · 6 끝 (열렸으면 지금).</summary>
public readonly record struct EpisodeDot(float At, byte Kind);

/// <summary>
/// 사건 경과 카드 · 결산 띠 (docs/OBSERVER.md §7.1 · 7.2 — V8 · 화면만 · 연출 장부 <see cref="Director.Episodes"/>를 읽기만):
/// 왼쪽 카드 자리에 경과 하나 (머리 · 가로 시계 · 마디마다 'm:ss' · 늦은 까닭 · 아는 이 · 다친 이 · 견줌 · [▶ 다시보기]) — 사건 띠 줄 · 결산 띠를 누르거나 함장의 손이 일으킨 일이면 연다 ·
/// 경과가 끝나면 가운데 칸에 결산 띠 6초 ('침실 둘 불 — 11분 · 다친 이 1 · 끈 이 로봇 1' — 누르면 그 카드). 감은 동안 끝난 경과는 띠를 띄우지 않는다.
/// </summary>
public sealed partial class LiveShow
{
    public const double TallySec = 6;
    private int _episodeRoot;                         // 연 경과 카드 (뿌리 — 0 없음 · 카드 자리는 하나)
    private readonly HashSet<int> _tallied = new();   // 결산 띠를 띄운 (또는 감은 동안 끝난) 경과
    private readonly List<int> _handRoots = new();    // 함장의 손이 일으킨 뿌리 — 그 경과가 열리면 카드를 띄운다 (손 카메라 8초 뒤에도 결과까지)
    private string _tally = "";
    private int _tallyRoot;
    private double _tallyLeft;

    /// <summary>연 경과 카드의 뿌리 (0 없음).</summary>
    public int EpisodeRoot => _episodeRoot;
    /// <summary>결산 띠 글 (없으면 빈 글).</summary>
    public string Tally => _tallyLeft > 0 ? _tally : "";
    private float TallyAlpha => _tallyLeft > 0 ? (float)Math.Clamp(Math.Min(_tallyLeft, TallySec - _tallyLeft + 0.1) / 0.4, 0, 1) : 0;

    /// <summary>경과 카드를 연다 (그 뿌리의 경과가 있으면 — 고른 이 · 물건 카드는 닫힌다).</summary>
    public void OpenEpisode(int root)
    {
        if (root == 0 || Dir.EpisodeOf(root) == null) return;
        _episodeRoot = root; Selected = -1; FollowSelected = false; Look = default;
    }

    /// <summary>사건 띠 줄을 눌렀다: 그 자리로 · 그 뿌리의 경과 카드.</summary>
    public void FeedGo(int cell, int root) { if (cell >= 0) LookAt(cell); if (root != 0) OpenEpisode(root); }

    /// <summary>결산 띠를 눌렀다 (뿌리 0이면 띄운 그 경과): 경과 카드 · 그 자리로.</summary>
    public void TallyGo(int root = 0)
    {
        root = root != 0 ? root : _tallyRoot;
        if (Dir.EpisodeOf(root) is { } e && e.Cell >= 0) LookAt(e.Cell);
        OpenEpisode(root);
        _tallyLeft = 0;
    }

    /// <summary>확인용 (화면 사진 --ui=tally): 끝난 경과의 결산 띠를 다시 띄운다 (감은 동안 끝나 띠 없이 적힌 것).</summary>
    public void TallyAgain(int root) { if (Dir.EpisodeOf(root) is { End: >= 0 } e) { _tally = TallyText(e); _tallyRoot = root; _tallyLeft = TallySec; } }

    /// <summary>경과 카드 [▶ 다시보기]: 그 뿌리의 장면을 다시 본다.</summary>
    public void EpisodeReplay() { if (Dir.EpisodeOf(_episodeRoot) is { } e && EpisodeScene(e) is { } h && CanReplay(h)) StartReplay(h); }

    /// <summary>그 경과의 장면 (같은 뿌리의 하이라이트 — 늦은 것).</summary>
    private Highlight? EpisodeScene(Episode e) { for (int i = Dir.Highlights.Count - 1; i >= 0; i--) if (Dir.Highlights[i].Root == e.Root) return Dir.Highlights[i]; return null; }

    /// <summary>나아갈 때마다 (show: 화면 길 — 감기는 띠 없이 끝난 것으로만 적는다): 끝난 경과 → 결산 띠 6초 · 손이 일으킨 일의 경과가 열리면 카드 (자리가 비었으면).</summary>
    private void WatchEpisodes(double realSec, bool show)
    {
        _tallyLeft = Math.Max(0, _tallyLeft - realSec);
        var eps = Dir.Episodes;
        for (int i = 0; i < eps.Count; i++)
        {
            var e = eps[i];
            if (e.End < 0 || !_tallied.Add(e.Root)) continue;
            if (show) { _tally = TallyText(e); _tallyRoot = e.Root; _tallyLeft = TallySec; }
        }
        if (_tallied.Count > 4 * Director.MaxEpisodes) _tallied.IntersectWith(eps.Select(e => e.Root));
        for (int i = _handRoots.Count - 1; i >= 0; i--)
        {
            if (Dir.EpisodeOf(_handRoots[i]) is not { } he) continue;
            if (Selected < 0 && !Look.Any) OpenEpisode(he.Root);
            _handRoots.RemoveAt(i);
        }
    }

    /// <summary>손이 일으킨 뿌리를 적는다 (항해 손 기록에 옮길 때 — 일어난 손만).</summary>
    private void NoteHandRoot(int cause) { if (cause == 0) return; _handRoots.Add(cause); if (_handRoots.Count > 8) _handRoots.RemoveAt(0); }

    private static string Ms(long ticks) { long s = (long)Math.Round(Math.Max(0, ticks) * Time.SecondsPerTick); return $"{s / 60}:{s % 60:00}"; }
    private static string FixWord(FactKind k) => k switch { FactKind.Patched => "막음", FactKind.Repaired => "고침", _ => "끔" };
    private static string FixWho(FactKind k) => k switch { FactKind.Patched => "막은 이", FactKind.Repaired => "고친 이", _ => "끈 이" };
    private static string CountWord(int n) => n switch { 3 => "셋", 4 => "넷", 5 => "다섯", 6 => "여섯", 7 => "일곱", 8 => "여덟", 9 => "아홉", 10 => "열", _ => $"{n}번" };

    private string AgentNameOr(int a) => Sim.Find<Mind>() is { } m && a >= 0 && a < m.Agents.Count ? m.NameOf(a) : "누군가";

    /// <summary>결산 띠: '침실 둘 불 — 11분 · 다친 이 1 · 숨진 이 0 · 끈 이 로봇 1' (없는 것은 뺀다).</summary>
    private string TallyText(Episode e)
    {
        var parts = new List<string> { Span(Math.Max(1, e.End - e.Start)) };
        int hurt = e.Harm.Where(h => h.kind != FactKind.Died).Select(h => h.agent).Distinct().Count(), died = e.Harm.Count(h => h.kind == FactKind.Died);
        if (hurt > 0) parts.Add($"다친 이 {hurt}");
        if (died > 0) parts.Add($"숨진 이 {died}");
        if (e.Saved.Count > 0) parts.Add($"구한 이 {e.Saved.Count}");
        if (e.FixAt >= 0 && e.Fixer >= 0) parts.Add($"{FixWho(e.FixKind)} {AgentNameOr(e.Fixer)}");
        return $"{e.Name} — {string.Join(" · ", parts)}";
    }

    /// <summary>
    /// 경과 카드 줄: 머리 '함장이 일으킨 불씨 — 침실 둘 침대 (1일째 00:02 — 끝 00:14 · 12분)' · 마디마다 '0:18 처음 안 이 — 김하늘 (냄새)' · '까닭 · …' ·
    /// '아는 이 3/6 · 다친 이 1 (이도윤)' · '견줌 · 지난 불 셋보다 40초 빨리 알았다' (같은 갈래 지난 경과 셋부터 — 알기까지의 평균).
    /// </summary>
    private string[] EpisodeCard()
    {
        var e = Dir.EpisodeOf(_episodeRoot);
        if (e == null) { _episodeRoot = 0; return Array.Empty<string>(); }
        long end = e.End >= 0 ? e.End : Sim.Tick;
        var l = new List<string> { e.Title, $"{DayClock(e.Start)} — {(e.End >= 0 ? "끝 " + Hm(e.End) : "아직")} · {Span(Math.Max(1, end - e.Start))}" };
        l.Add($"{Ms(0)} 일어남 — {e.Name}");
        var nodes = new List<(long at, string text)>();
        if (e.NoticeAt >= 0) nodes.Add((e.NoticeAt, $"처음 안 이 — {AgentNameOr(e.Noticer)} ({Director.Sense(e.NoticeKind)})"));
        if (e.ComputerAt >= 0) nodes.Add((e.ComputerAt, $"주컴퓨터가 앎 — {Director.Sensor(e.ComputerKind)}"));
        if (e.AssignAt >= 0) nodes.Add((e.AssignAt, $"맡김 — {AgentNameOr(e.Assignee)}{(e.Asked ? " (부탁)" : "")}"));
        if (e.ArriveAt >= 0) nodes.Add((e.ArriveAt, $"닿음 — {AgentNameOr(e.Assignee)}"));
        if (e.FixAt >= 0) nodes.Add((e.FixAt, e.Fixer >= 0 ? $"{FixWord(e.FixKind)} — {AgentNameOr(e.Fixer)}" : e.FixKind == FactKind.FireOut ? "꺼짐 — 저절로 (탈 것이 다했다)" : $"{FixWord(e.FixKind)} — 저절로"));
        foreach (var (at, text) in nodes.OrderBy(n => n.at)) l.Add($"{Ms(at - e.Start)} {text}");
        foreach (var w in e.Why) l.Add("까닭 · " + w);
        int all = Sim.Find<Mind>()?.Agents.Count(a => a.IsPerson) ?? 0;
        var hurt = e.Harm.Where(h => h.kind != FactKind.Died).Select(h => h.agent).Distinct().ToList();
        var died = e.Harm.Where(h => h.kind == FactKind.Died).Select(h => h.agent).ToList();
        string who = $"아는 이 {e.Knowers.Count}/{all}";
        if (hurt.Count > 0) who += $" · 다친 이 {hurt.Count} ({string.Join(" · ", hurt.Take(3).Select(AgentNameOr))})";
        if (died.Count > 0) who += $" · 숨진 이 {died.Count} ({string.Join(" · ", died.Take(3).Select(AgentNameOr))})";
        if (e.Saved.Count > 0) who += $" · 구해진 이 {e.Saved.Count}";
        l.Add(who);
        if (Compare(e) is { Length: > 0 } cmp) l.Add(cmp);
        return l.ToArray();
    }

    /// <summary>견줌 한 줄: 같은 갈래로 이 경과가 일어나기 전에 끝난 경과가 셋 넘게 있으면 알기까지의 평균과 견준다 (5초 안이면 '비슷하게').</summary>
    private string Compare(Episode e)
    {
        long knew = e.KnewAt;
        if (knew < 0) return "";
        int n = 0; double sum = 0;
        foreach (var x in Dir.Episodes)
        {
            if (ReferenceEquals(x, e) || x.Kind != e.Kind || x.End < 0 || x.End > e.Start || x.KnewAt < 0) continue;
            n++; sum += x.KnewAt - x.Start;
        }
        if (n < 3) return "";
        double d = (knew - e.Start - sum / n) * Time.SecondsPerTick;
        string kind = Director.KindWord(e.Kind, e.Name.EndsWith("외판 구멍"));
        string cnt = CountWord(n);
        if (Math.Abs(d) < 5) return $"견줌 · 지난 {kind} {Ko.Gwa(cnt)} 비슷하게 알았다";
        double a = Math.Abs(d);
        return $"견줌 · 지난 {kind} {cnt}보다 {(a < 120 ? $"{Math.Round(a)}초" : $"{Math.Round(a / 60)}분")} {(d < 0 ? "빨리" : "늦게")} 알았다";
    }

    /// <summary>경과 카드의 가로 시계 점 (때 차례 — 일어남 0 · 끝 1).</summary>
    private EpisodeDot[] EpisodeDotsNow()
    {
        if (Dir.EpisodeOf(_episodeRoot) is not { } e) return Array.Empty<EpisodeDot>();
        long end = e.End >= 0 ? e.End : Sim.Tick, span = Math.Max(1, end - e.Start);
        var d = new List<EpisodeDot>(7) { new(0, 0) };
        void Add(long at, byte k) { if (at >= 0) d.Add(new EpisodeDot((float)Math.Clamp((at - e.Start) / (double)span, 0, 1), k)); }
        Add(e.NoticeAt, 1); Add(e.ComputerAt, 2); Add(e.AssignAt, 3); Add(e.ArriveAt, 4); Add(e.FixAt, 5); Add(end, 6);
        return d.OrderBy(x => x.At).ThenBy(x => x.Kind).ToArray();
    }

    /// <summary>경과 카드의 [▶ 다시보기]를 누를 수 있나.</summary>
    private bool EpisodeCanReplay() => Dir.EpisodeOf(_episodeRoot) is { } e && EpisodeScene(e) is { } h && CanReplay(h);
}
