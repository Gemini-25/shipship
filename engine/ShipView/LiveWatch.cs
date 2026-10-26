using ShipEngine;

namespace ShipView;

/// <summary>
/// 내 사람 (docs/OBSERVER.md §6.1 · 6.2 — V6 · 화면만 · 세계는 읽기만):
/// 출항 소개 (출항 준비의 첫 쪽 — 사람마다 얼굴 · 이름 · 맡은 일 · 사람됨 · 꿈 · 지난 일 · 남몰래는 '관객만 안다' · ☆) ·
/// ☆ 눈여겨보기 (별 단 이에게 일이 나면 알림 4초 · 누르면 그 이를 따라간다 · 조용할 때 연출이 그 방을 먼저 · 더 오래 비춘다 — 카메라만) ·
/// 지나온 길 (사람 자세히 일곱째 작은 탭 — 사람 일 장부의 그 이 길을 때 차례로).
/// </summary>
public sealed partial class LiveShow
{
    /// <summary>☆ 눈여겨보는 이 (마음 번호 — 화면 값 · 화면 마디에 남는다).</summary>
    public readonly SortedSet<int> Stars = new();
    public const string IntroTitle = "함께 떠날 사람들", IntroFoot = "☆를 누르면 눈여겨본다 — 일이 나면 알리고 조용할 때 먼저 비춘다 · 줄을 누르면 자세히 (↑↓ · P)";
    public const string StarOn = "★ 눈여겨봄", StarOff = "☆ 눈여겨보기";
    public const double StarSec = 4;
    /// <summary>같은 이의 같은 갈래 일은 이만큼 안에 거듭 알리지 않는다 (게임 시간 — 다침은 틱마다 날 수 있다).</summary>
    public static readonly long StarAgain = Time.Minutes(10);
    private long _starFacts;
    private readonly Dictionary<(int agent, FactKind kind), long> _starSaid = new();
    private int _noticeAgent = -1;

    /// <summary>출항 소개 쪽인가 (출항 준비 중 사람 판의 목록).</summary>
    public bool Intro => Holding && Panel == PanelKind.People && Detail == DetailKind.None;

    /// <summary>☆를 켜고 끈다 (사람만) — 연출에도 넘긴다 (일상 비춤만 읽는다).</summary>
    public void ToggleStar(int agent)
    {
        var mind = Sim.Find<Mind>();
        if (mind == null || agent < 0 || agent >= mind.Agents.Count || !mind.Agents[agent].IsPerson) return;
        bool on = Stars.Add(agent);
        if (!on) Stars.Remove(agent);
        Dir.Starred = Stars.ToArray();
        string name = mind.NameOf(agent);
        Notify(on ? $"☆ {Ko.Eul(name)} 눈여겨본다 — 일이 나면 알리고 조용할 때 먼저 비춘다" : $"{name} — 눈여겨보기를 껐다");
    }

    /// <summary>산 세계가 나아간 뒤 (Advance): 별 단 이의 새 사실 → 알림 4초 '☆ 김하늘이 쓰러졌다' (누르면 그 이를 따라간다) · 같은 이 · 같은 갈래는 10분에 한 번.</summary>
    private void WatchStars()
    {
        long total = Sim.Facts.Total;
        if (total == _starFacts) return;   // 새 사실이 없다 (틱마다 부른다 — ≤ 1µs)
        if (Stars.Count == 0 || Sim.Find<Mind>() is not { } mind) { _starFacts = total; return; }
        foreach (var f in Sim.Facts.Since(_starFacts))
        {
            var (who, text) = StarLine(mind, f);
            if (who < 0) continue;
            if (f.Kind != FactKind.Died && _starSaid.TryGetValue((who, f.Kind), out long last) && f.Tick - last < StarAgain) continue;
            _starSaid[(who, f.Kind)] = f.Tick;
            Notify("☆ " + text, StarSec, PanelKind.People);
            _noticeAgent = who;
        }
        _starFacts = total;
    }

    /// <summary>사실 하나가 별 단 이의 일이면 그 이 · 알림 글 (아니면 −1): 쓰러짐 · 다침 · 숨짐 · 다툼 (따짐 · 몸싸움 · 탓) · 편지 · 짝 · 꿈.</summary>
    private (int who, string text) StarLine(Mind mind, in Fact f)
    {
        int n = mind.Agents.Count;
        bool S(int a) => a >= 0 && a < n && Stars.Contains(a);
        string N(int a) => a >= 0 && a < n ? mind.NameOf(a) : "누군가";
        int body = f.Actor >= 0 && f.Kind is FactKind.Collapsed or FactKind.Hurt or FactKind.Died ? mind.AgentOfBody(f.Actor) : -1;
        int pair = S(f.Actor) ? f.Actor : S(f.Other) ? f.Other : -1;
        switch (f.Kind)
        {
            case FactKind.Collapsed when S(body): return (body, $"{Ko.I(N(body))} 쓰러졌다");
            case FactKind.Hurt when S(body): return (body, $"{Ko.I(N(body))} 다쳤다");
            case FactKind.Died when S(body): return (body, $"{Ko.I(N(body))} 숨졌다");
            case FactKind.Talk when (TalkEnd)(int)f.Size == TalkEnd.Argue && pair >= 0: return (pair, $"{Ko.Gwa(N(f.Actor))} {Ko.I(N(f.Other))} 다퉜다");
            case FactKind.Fight when pair >= 0: return (pair, $"{Ko.Gwa(N(f.Actor))} {Ko.I(N(f.Other))} 몸싸움을 벌였다");
            case FactKind.Blamed when pair >= 0: return (pair, $"{Ko.I(N(f.Actor))} {Ko.Eul(N(f.Other))} 탓했다");
            case FactKind.Letter when S(f.Actor): return (f.Actor, f.Size <= -2 ? $"{Ko.I(N(f.Actor))} 고향에서 부고 편지를 받았다" : $"{Ko.I(N(f.Actor))} 고향에서 편지를 받았다");
            case FactKind.Relationship when (int)f.Size == 1 && pair >= 0: return (pair, $"{Ko.Gwa(N(f.Actor))} {Ko.I(N(f.Other))} 짝이 되었다");
            case FactKind.Aim when f.Size >= 1 && S(f.Actor): return (f.Actor, $"{Ko.I(N(f.Actor))} 꿈을 이루었다");
        }
        return (-1, "");
    }

    /// <summary>출항 소개: 사람마다 '이름 · 맡은 일' · 사람됨 (꿈 · 즐겨 하는 것 · 짝) · 지난 일 · 얼굴 · ☆.</summary>
    private void IntroRows(List<PanelRow> rows)
    {
        var mind = Sim.Find<Mind>(); var so = Sim.Find<Society>();
        if (mind == null) return;
        foreach (var a in mind.Agents)
        {
            if (!a.IsPerson) continue;
            string role = RoleOf(a.Id), past = PastLines(mind, so, a.Id);
            string sub = Persona(mind, so, a) + (past.Length > 0 ? "\n" + past : "");
            rows.Add(new PanelRow($"{mind.NameOf(a.Id)}{(role.Length > 0 ? " · " + role : "")}", sub, false, Face: FaceSlotOf(a.Id), Key: KeyPerson(a.Id), Act: Stars.Contains(a.Id) ? StarOn : StarOff, ActOn: true));
        }
    }

    /// <summary>지난 일 (출항 과거사 — 두 사람을 잇는 꼴): '최민준 ↔ 김하늘 — 묵은 앙금' · '김하늘 → 정지우 — 남몰래 좋아함 (관객만 안다)' (받는 쪽 줄에는 없다 — 그 이는 모른다).</summary>
    private static string PastLines(Mind mind, Society? so, int id)
    {
        if (so == null) return "";
        var parts = new List<string>();
        foreach (var t in so.Past)
        {
            if (t.A != id && t.B != id || t.Kind == 4 && t.A != id) continue;
            string a = mind.NameOf(t.A), b = mind.NameOf(t.B);
            parts.Add(t.Kind switch { 2 => $"{a} ↔ {b} — 오랜 벗", 3 => $"{a} ↔ {b} — 묵은 앙금", 4 => $"{a} → {b} — 남몰래 좋아함 (관객만 안다)", _ => $"{a} ↔ {b} — 아는 사이" });
        }
        return parts.Count > 0 ? "지난 일: " + string.Join(" · ", parts) : "";
    }

    /// <summary>
    /// 지나온 길 (사람 자세히 일곱째 작은 탭): 맨 위 달라진 것 ('겁이 많았다 → 맞서며 단단해졌다' — 마음 4 → 5 · 8) 또는 그 이의 한 일 ·
    /// 그 아래 항해 내내 구함 · 다침 · 쓰러짐 · 겪은 충격과 회복 · 짝 · 꿈 · 숨짐 (때 차례 — 사람 일 장부) · 출항 전 지난 일.
    /// </summary>
    private void PathRows(Sim w, Mind mind, Society? so, Agent a, List<PanelRow> rows)
    {
        var log = Dir.People.Find(a.Id);
        var path = log?.Path ?? new List<PathStep>();
        string name = mind.NameOf(a.Id);
        // 마음이 달라진 것: 겁 (4) → 단단해짐 (5) · 외상 뒤 성장 (8)
        int fear = path.FindIndex(p => p.Kind == PathKind.Heart && p.Heart == HeartEvent.Fearful);
        int grew = path.FindLastIndex(p => p.Kind == PathKind.Heart && p.Heart is HeartEvent.Hardened or HeartEvent.Grew);
        string change = fear >= 0 && grew > fear ? $"겁이 많았다 → {(path[grew].Heart == HeartEvent.Grew ? "아픔 뒤에 자랐다" : "맞서며 단단해졌다")}"
            : grew >= 0 ? (path[grew].Heart == HeartEvent.Grew ? "아픔 뒤에 자랐다" : "맞서며 단단해졌다")
            : fear >= 0 ? "겁이 많아졌다" : "";
        string deeds = Dir.People.Deeds(a.Id);
        rows.Add(new PanelRow(change.Length > 0 ? change : deeds.Length > 0 ? $"{name}의 항해 — {deeds}" : path.Count > 0 ? $"{name}의 항해" : $"{name}의 항해 — 아직 큰 일 없이",
            change.Length > 0 && deeds.Length > 0 ? deeds : $"{w.Tick / Time.TicksPerDay + 1}일째 · 지나온 일 {path.Count}", false, Key: DK(1)));
        for (int i = 0; i < path.Count; i++)
        {
            var p = path[i];
            rows.Add(new PanelRow($"{DayClock(p.Tick)}  {p.Text}", "", false, Key: DK(16 + i)));
        }
        if (so?.PastLine(a.Id) is { Length: > 0 } past) rows.Add(new PanelRow("출항 전 지난 일", past, false, Key: DK(0)));
    }
}
