using ShipEngine;

namespace ShipView;

/// <summary>지나온 날 띠의 한 칸: 날 (0부터) · 분위기 (0 조용함 · 1 조짐 · 2 위험 · 3 결정적 순간) · 잃은 이가 있었나 · 오늘 · 누르면 그 장 (판 줄 열쇠).</summary>
public readonly record struct DayCell(int Day, int Mood, bool Lost, bool Today, long Key);

/// <summary>항해의 끝 (docs/OBSERVER.md §5.6): 모두 잃음 (산 사람 0) · 임무 마침 · 함장이 그만둠 (메뉴 [항해 끝내기]).</summary>
public enum VoyageEnd : byte { None, AllLost, Mission, Quit }

/// <summary>
/// 기록 (docs/OBSERVER.md §5 — V3 · 화면만 · 세계는 읽기만): 날이 바뀌면 장 카드 (4초 · 한 주 장 6초 · 멈추지 않는다 · 누르면 기록 그 장) ·
/// [기록] 판 (여덟째 탭 · Y — 목록 없이 곧 자세히 · 작은 탭 넷: 날마다 · 잃은 이 · 사진첩 · 항해) · 지나온 날 띠 · 묘비 · 결산 카드 (모두 잃음 · 임무 마침 · 그만둠 — 멈춘다).
/// 판 줄은 바뀔 때만 짓는다 (작은 탭 · 장 수 · 고른 줄 · 하이라이트 · 다시보기 고리의 첫 찍기 · 잃은 이 · 오늘 장은 2초에 한 번).
/// </summary>
public sealed partial class LiveShow
{
    public static readonly string[] RecordTabs = { "날마다", "잃은 이", "사진첩", "항해" };
    /// <summary>기록 판 줄 열쇠 (docs/OBSERVER.md §15.5 — 사람 · 자세히 열쇠 1~7 뒤): 작은 탭 · 줄.</summary>
    public static long KeyRecord(int sub, long n) => (8L << 40) | ((long)sub << 36) | n;
    /// <summary>날마다의 장 i (연출의 장 차례)의 줄 — 0 머리 · 1~5 장면 · 16~ 주인공. 오늘 줄은 <see cref="KeyToday"/>.</summary>
    public static long KeyChapter(int i, int part = 0) => KeyRecord(0, ((long)(i + 1) << 8) | (uint)part);
    public static readonly long KeyToday = KeyRecord(0, 1);
    private static long KeyTomb(int agent) => KeyRecord(1, agent + 1);
    private static long KeyVoyageScene(int hi) => KeyRecord(3, (1L << 20) | (uint)hi);
    private static long KeyVoyagePerson(int agent) => KeyRecord(3, (2L << 20) | (uint)agent);
    public const double ChapterSec = 4, WeekSec = 6, NowEverySec = 2;
    public const int EndPeople = 12, EndScenes = 5;

    // ── 장 카드 (4초 · 한 주 6초) ──
    private Chapter? _cardCh;
    private int[] _cardFaces = Array.Empty<int>();
    private double _cardLeft, _cardFull;
    private int _chaptersSeen;

    /// <summary>날이 바뀌었다 (연출의 장이 하나 늘었다 — 시험은 곧바로 부른다): 가운데 칸에 장 카드.</summary>
    public void DayTurned(Chapter ch)
    {
        _cardCh = ch; _cardFull = _cardLeft = ch.Week ? WeekSec : ChapterSec;
        _cardFaces = ch.Cast.Select(FaceSlotOf).Where(s => s >= 0).Take(3).ToArray();
    }

    /// <summary>나아갈 때마다 (Advance): 새 장이 생겼으면 카드 (같은 틱에 날 장과 한 주 장이 함께 나면 한 주 장) · 카드 시간을 줄인다.</summary>
    private void WatchChapters(double realSec)
    {
        var chs = Dir.Chapters;
        if (chs.Count > _chaptersSeen)
        {
            Chapter? week = null, last = null;
            for (int i = _chaptersSeen; i < chs.Count; i++) { last = chs[i]; if (chs[i].Week) week = chs[i]; }
            DayTurned(week ?? last!);
        }
        _chaptersSeen = chs.Count;
        _cardLeft = Math.Max(0, _cardLeft - realSec);
    }

    /// <summary>장 카드를 눌렀다: 기록 판 '날마다'에서 그 장을 펼친다.</summary>
    public void ChapterCardGo()
    {
        var ch = _cardCh;
        if (ch == null) return;
        _cardLeft = 0;
        int i = Dir.Chapters.IndexOf(ch);
        OpenRecord(0);
        if (i >= 0) { PanelPickKey = KeyChapter(i); _recordShut = 0; SyncPick(PanelRows()); }
    }

    /// <summary>날 띠의 칸을 눌렀다: 그 장을 펼친다 (오늘 칸이면 오늘 줄).</summary>
    public void DayCellGo(long key)
    {
        OpenRecord(0);
        PanelPickKey = key; _recordShut = 0;
        SyncPick(PanelRows());
    }

    /// <summary>기록 판을 연다 (그 작은 탭으로).</summary>
    public void OpenRecord(int sub)
    {
        if (Panel != PanelKind.Record || Detail != DetailKind.Record) { ClosePanel(); TogglePanel(PanelKind.Record); }
        if (DetailSub != sub) SetDetailSub(sub);
    }

    /// <summary>사람 줄 얼굴 그림의 칸 (사람 차례 — 로봇 · 드론은 -1).</summary>
    private int FaceSlotOf(int agent)
    {
        var mind = Sim.Find<Mind>();
        if (mind == null || agent < 0 || agent >= mind.Agents.Count || !mind.Agents[agent].IsPerson) return -1;
        int slot = 0;
        foreach (var a in mind.Agents) { if (a.Id == agent) return slot; if (a.IsPerson) slot++; }
        return -1;
    }

    // ── 오늘 장 (2초에 한 번 새로 — ChapterNow ≤ 0.2ms) ──
    private Chapter? _now;
    private double _nowAt = double.NegativeInfinity;
    private int _nowVer, _nowChapters = -1;
    private Chapter NowChapter()
    {
        if (_now == null || _anim - _nowAt >= NowEverySec || _nowChapters != Dir.Chapters.Count) { _now = Dir.ChapterNow(Sim); _nowAt = _anim; _nowChapters = Dir.Chapters.Count; _nowVer++; }
        return _now;
    }

    /// <summary>지나온 날 띠: 지어진 날 장마다 한 칸 + 오늘 (지금 · 시험은 때를 넘긴다).</summary>
    public DayCell[] DayStrip(long? now = null)
    {
        int today = (int)((now ?? Sim.Tick) / Time.TicksPerDay);
        var chs = Dir.Chapters;
        var cells = new List<DayCell>(chs.Count + 1);
        for (int i = 0; i < chs.Count; i++) if (!chs[i].Week) cells.Add(new DayCell(chs[i].Day, chs[i].Mood, chs[i].Lost.Count > 0, false, KeyChapter(i)));
        if (cells.Count == 0 || cells[^1].Day < today) { var n = NowChapter(); cells.Add(new DayCell(today, n.Mood, n.Lost.Count > 0, true, KeyToday)); }
        else cells[^1] = cells[^1] with { Today = true };
        return cells.ToArray();
    }

    // ── 기록 판 줄 (바뀔 때만 짓는다) ──
    private PanelRow[] _recRows = Array.Empty<PanelRow>();
    private int _recSig;
    private long _recordShut;   // 다시 눌러 접은 장 (머리 줄 열쇠)

    private void RecordRows(List<PanelRow> rows)
    {
        if (DetailSub == 0) NowChapter();
        int sig = HashCode.Combine(DetailSub, Dir.Chapters.Count, PanelPickKey, _recordShut, Dir.Highlights.Count, Dir.Ring.Count > 0 ? Dir.Ring[0].tick : -1, _nowVer,
            HashCode.Combine((int)Ended, Sim.Facts.CountOf(FactKind.Died), Dir.People.Logs.Count, Dir.People.MournSeen, DetailSub == 3 ? Sim.Tick / 25 : 0, Vault.Scenes.Count, Vault.DayWorlds.Count));
        if (sig != _recSig || _recRows.Length == 0)
        {
            var l = new List<PanelRow>();
            switch (DetailSub)
            {
                case 0: DayRows(l); break;
                case 1: TombRows(l); break;
                case 2: l.Add(new PanelRow("사진첩은 아직 비어 있다", "", false, Key: -1)); break;
                default: VoyageRows(l); break;
            }
            _recRows = l.ToArray(); _recSig = sig;
        }
        rows.AddRange(_recRows);
    }

    private bool Opened(long head) => PanelPickKey >> 8 == head >> 8 && PanelPickKey != _recordShut;

    private string AgentName(int a) { var m = Sim.Find<Mind>(); return m != null && a >= 0 && a < m.Agents.Count ? m.NameOf(a) : "누군가"; }
    private string RoleOf(int a) { var m = Sim.Find<Mind>(); return m != null && a >= 0 && a < m.Agents.Count && m.Agents[a].IsPerson && m.Agents[a].C.Role < m.RoleNames.Length ? m.RoleNames[m.Agents[a].C.Role] : ""; }
    /// <summary>장면 줄: 표 (● 고리 · 금고 · ◐ 감아 · ○ 사진만 — 도형으로 그린다) · 때 · 제목 · ▶.</summary>
    private PanelRow SceneRow(Highlight h, long key) { var (mark, sub, can) = WayRow(h); return new PanelRow($"{HourMin(h.Tick)}  {h.Title}", sub, false, Key: key, Act: "▶ 다시보기", ActOn: can, Why: PhotoWhy, Mark: mark); }

    /// <summary>날마다: '오늘 지금까지' → 장 (새것 위) · 고른 장은 펼친다 (요약 줄 · 다시볼 장면 · 주인공).</summary>
    private void DayRows(List<PanelRow> rows)
    {
        var now = NowChapter();
        bool openNow = PanelPickKey == KeyToday && _recordShut != KeyToday;
        rows.Add(new PanelRow($"오늘 지금까지 — {Tail(now.Title)}", openNow ? string.Join("\n", now.Lines) : now.Arc.Length > 0 ? "하루의 결 — " + now.Arc : "", false, Key: KeyToday));
        var chs = Dir.Chapters;
        for (int i = chs.Count - 1; i >= 0; i--)
        {
            var c = chs[i];
            long head = KeyChapter(i);
            bool open = Opened(head);
            rows.Add(new PanelRow(c.Title, open ? string.Join("\n", c.Lines) : "", false, Key: head));
            if (!open) continue;
            for (int k = 0; k < c.Scenes.Count && k < Director.SceneCount; k++) rows.Add(SceneRow(c.Scenes[k], KeyChapter(i, 1 + k)));
            for (int k = 0; k < c.Cast.Count; k++)
            {
                int a = c.Cast[k];
                string role = RoleOf(a);
                int slot = FaceSlotOf(a);
                rows.Add(new PanelRow($"주인공 — {AgentName(a)}{(role.Length > 0 ? " · " + role : "")}", "누르면 자세히", false, Face: slot, Key: KeyChapter(i, 16 + k)));
            }
        }
        static string Tail(string t) { int i = t.IndexOf(" — ", StringComparison.Ordinal); return i > 0 ? t[(i + 3)..] : t; }
    }

    /// <summary>숨진 이 (그 사람 · 숨진 때) — 장부가 본 죽음이 먼저 · 연출이 보기 전에 숨진 이는 몸의 때.</summary>
    private List<(int agent, long at, int cause)> Lost()
    {
        var l = new List<(int, long, int)>();
        var mind = Sim.Find<Mind>(); var life = Sim.Find<Life>();
        if (mind == null) return l;
        foreach (var a in mind.Agents)
        {
            if (!a.IsPerson) continue;
            var log = Dir.People.Find(a.Id);
            if (log is { DiedAt: >= 0 }) { l.Add((a.Id, log.DiedAt, log.DiedCause)); continue; }
            if (life != null && a.C.Body >= 0 && a.C.Body < life.Bodies.Count && life.Bodies[a.C.Body].Alive == 0) l.Add((a.Id, life.Bodies[a.C.Body].DiedAt, 0));
        }
        return l.OrderBy(x => x.Item2).ThenBy(x => x.Item1).ToList();
    }

    private static string DayClock(long t) { long m = t % Time.TicksPerDay * 60 / Time.TicksPerHour; return $"{t / Time.TicksPerDay + 1}일째 {m / 60:00}:{m % 60:00}"; }

    /// <summary>숨진 이의 장면 (그 죽음이 든 하이라이트 — 없으면 null).</summary>
    private Highlight? DeathScene(int agent, long at)
    {
        var mind = Sim.Find<Mind>();
        int body = mind != null && agent >= 0 && agent < mind.Agents.Count ? mind.Agents[agent].C.Body : -1;
        Highlight? best = null;
        foreach (var h in Dir.Highlights)
            if (h.Kind == FactKind.Died && h.Actor == body && body >= 0 || h.StartTick <= at && at <= h.Last + Time.Minutes(2)) if (best == null || h.Score > best.Score) best = h;
        return best;
    }

    /// <summary>숨진 까닭 한 줄: '[불·화학 → 공기·열 → 몸] 침대 불 → … → 숨이 멎었다' (원인을 모르면 '숨을 거두었다').</summary>
    private string DeathLine(int cause)
    {
        if (cause == 0) return "숨을 거두었다";
        string path = Dir.PathOf(Sim, cause);
        return (path.Length > 0 ? path + " " : "") + Director.Short(Sim.Causes.Story(cause));
    }

    /// <summary>잃은 이: 묘비 (이름 · 맡은 일 · 숨진 때와 까닭 · 함께한 날 · 한 일 · 추모 말 · 얼굴 · ▶ 그 장면).</summary>
    private void TombRows(List<PanelRow> rows)
    {
        foreach (var (a, at, cause) in Lost())
        {
            string role = RoleOf(a), deeds = Dir.People.Deeds(a);
            var sub = new List<string> { $"{DayClock(at)} — {DeathLine(cause)}", $"{at / Time.TicksPerDay + 1}일을 함께했다{(deeds.Length > 0 ? " · " + deeds : "")}" };
            if (Dir.People.Find(a) is { } log) foreach (var w in log.Words) sub.Add("추모 — " + w);
            var h = DeathScene(a, at);
            bool can = h != null && CanReplay(h);
            rows.Add(new PanelRow($"{AgentName(a)}{(role.Length > 0 ? " · " + role : "")}", string.Join("\n", sub), false, Face: FaceSlotOf(a), Key: KeyTomb(a),
                Act: h != null ? "▶ 다시보기" : "", ActOn: can, Why: PhotoWhy, Mark: h != null ? WayRow(h).mark : (byte)0));
        }
        if (rows.Count == 0) rows.Add(new PanelRow("아무도 잃지 않았다", "숨진 이가 생기면 여기에 묘비가 선다", false, Key: -1));
    }

    /// <summary>한 사람의 항해 한 줄: 산 이 '살았다 — 구한 사람 2 · 끈 불 3' · 숨진 이 '17일째 — 숨이 멎었다'.</summary>
    private string PersonEnd(int a, List<(int agent, long at, int cause)> lost)
    {
        foreach (var (ag, at, cause) in lost)
            if (ag == a) { string what = cause != 0 ? Sim.Causes.Text(cause) : "숨을 거두었다"; return $"{at / Time.TicksPerDay + 1}일째 — {what}"; }
        string deeds = Dir.People.Deeds(a);
        return deeds.Length > 0 ? $"살았다 — {deeds}" : "살았다";
    }

    private (int all, int alive) Counts()
    {
        var mind = Sim.Find<Mind>(); var life = Sim.Find<Life>();
        int all = 0, alive = 0;
        if (mind != null)
            foreach (var a in mind.Agents)
                if (a.IsPerson) { all++; if (life != null && a.C.Body >= 0 && a.C.Body < life.Bodies.Count && life.Bodies[a.C.Body].Alive != 0) alive++; }
        return (all, alive);
    }

    /// <summary>항해: 결산 (지금까지 — 머리 · 사람마다 한 줄 · 큰 장면 다섯).</summary>
    private void VoyageRows(List<PanelRow> rows)
    {
        var (all, alive) = Counts();
        int day = (int)(Sim.Tick / Time.TicksPerDay) + 1;
        rows.Add(new PanelRow($"{ShipName} · 항해 {Voyage} — {day}일째{(Ended != VoyageEnd.None ? " · " + EndText(Ended) : "")}",
            $"산 이 {alive}명 / 탄 이 {all}명 · 볼 만했던 장면 {Dir.Highlights.Count} · 지나온 날 {Dir.Chapters.Count(c => !c.Week)}", false, Key: -1));
        var lost = Lost();
        var mind = Sim.Find<Mind>();
        if (mind != null)
            foreach (var a in mind.Agents)
            {
                if (!a.IsPerson) continue;
                string role = RoleOf(a.Id);
                rows.Add(new PanelRow($"{mind.NameOf(a.Id)}{(role.Length > 0 ? " · " + role : "")}", PersonEnd(a.Id, lost), false, Face: FaceSlotOf(a.Id), Key: KeyVoyagePerson(a.Id)));
            }
        foreach (int hi in TopScenes()) rows.Add(SceneRow(Dir.Highlights[hi], KeyVoyageScene(hi)));
    }

    /// <summary>항해의 큰 장면 다섯 (무게 순 · 하이라이트 차례).</summary>
    private IEnumerable<int> TopScenes() => Enumerable.Range(0, Dir.Highlights.Count).OrderByDescending(i => Dir.Highlights[i].Score).ThenBy(i => i).Take(EndScenes);

    /// <summary>기록 판의 줄을 눌렀다 (열쇠 — 고르기 · 펼치기는 이미): 같은 장 머리를 다시 누르면 접는다 · 주인공 · 결산의 사람은 자세히.</summary>
    private bool RecordClick(long key, long was)
    {
        if (key >> 40 != 8) return true;
        int sub = (int)(key >> 36 & 0xF);
        long n = key & ((1L << 36) - 1);
        var chs = Dir.Chapters;
        if (sub == 0 && n >= 256)
        {
            int i = (int)(n >> 8) - 1, part = (int)(n & 0xFF);
            if (part == 0) { _recordShut = key == was && _recordShut != key ? key : 0; return true; }
            if (part >= 16 && i >= 0 && i < chs.Count && part - 16 < chs[i].Cast.Count) { int a = chs[i].Cast[part - 16]; OpenDetail(DetailKind.Person, a); return true; }   // 로봇이면 OpenDetail이 로봇 자세히로
            return true;
        }
        if (sub == 0 && key == KeyToday) { _recordShut = key == was && _recordShut != key ? key : 0; return true; }
        if (sub == 3 && n >> 20 == 2) { OpenDetail(DetailKind.Person, (int)(n & 0xFFFFF)); return true; }
        return true;
    }

    /// <summary>기록 판 줄의 장면 (▶ — 없으면 null).</summary>
    private Highlight? RecordScene(long key)
    {
        if (key >> 40 != 8) return null;
        int sub = (int)(key >> 36 & 0xF);
        long n = key & ((1L << 36) - 1);
        var chs = Dir.Chapters;
        switch (sub)
        {
            case 0 when n >= 256:
            {
                int i = (int)(n >> 8) - 1, part = (int)(n & 0xFF);
                return part is >= 1 and <= Director.SceneCount && i >= 0 && i < chs.Count && part - 1 < chs[i].Scenes.Count ? chs[i].Scenes[part - 1] : null;
            }
            case 1:
            {
                int a = (int)n - 1;
                foreach (var (ag, at, _) in Lost()) if (ag == a) return DeathScene(a, at);
                return null;
            }
            case 3 when n >> 20 == 1:
            {
                int hi = (int)(n & 0xFFFFF);
                return hi < Dir.Highlights.Count ? Dir.Highlights[hi] : null;
            }
        }
        return null;
    }

    /// <summary>기록 판 줄의 ▶: 그 장면을 다시 본다 (판을 닫는다).</summary>
    private bool RecordAct(long key)
    {
        var h = RecordScene(key);
        if (h == null) return false;
        PanelPickKey = key;
        if (!StartReplay(h)) { Notify(PhotoWhy); return false; }
        ClosePanel();
        return true;
    }

    /// <summary>기록 판 머리의 둘째 줄: '디딤호 · 항해 4127 · 43일째 · 지나온 장 42'.</summary>
    private string RecordHeadLine() => $"{ShipName} · 항해 {Voyage} · {Sim.Tick / Time.TicksPerDay + 1}일째 · 지나온 장 {Dir.Chapters.Count(c => !c.Week)}{(Ended != VoyageEnd.None ? " · " + EndText(Ended) : "")}";

    // ── 결산 (docs/OBSERVER.md §5.6) ──
    public VoyageEnd Ended;
    /// <summary>결산 카드 (첫 줄 제목 · 사람마다 한 줄 · 큰 장면 — 빈 배열이면 없다).</summary>
    public string[] EndCard = Array.Empty<string>();
    /// <summary>끝난 틱에 한 번 — 띄우는 쪽이 저장한다 (card.json 끝 갈래).</summary>
    public bool EndSaveDue;

    public static string EndText(VoyageEnd e) => e switch { VoyageEnd.AllLost => "모두 잃었다", VoyageEnd.Mission => "임무를 마쳤다", VoyageEnd.Quit => "함장이 그만두었다", _ => "" };

    /// <summary>항해가 끝났나 (산 세계 · 다시보기가 아닐 때 · 나아간 뒤): 산 사람 0 · 임무를 마쳤다 (임무가 없으면 가는 곳에 닿음) — 한 번만.</summary>
    private void CheckEnd()
    {
        if (Ended != VoyageEnd.None || _replay != null || Holding) return;
        var (all, alive) = Counts();
        if (all > 0 && alive == 0) { EndVoyage(VoyageEnd.AllLost); return; }
        var sm = Sim.Find<ShipMind>();
        if (sm != null && sm.Mission.Count == 0 && sm.MissionsDone > 0) { EndVoyage(VoyageEnd.Mission); return; }
        if ((sm == null || sm.Mission.Count == 0 && sm.MissionsDone == 0) && Sim.Find<Outside>() is { PlacesReached: > 0 } o && o.AtPlace >= 0) EndVoyage(VoyageEnd.Mission);
    }

    /// <summary>항해를 끝낸다: 결산 카드 (가운데 · 멈춘다) · 저장할 때 (끝 갈래). 이미 끝났으면 그대로.</summary>
    public void EndVoyage(VoyageEnd e)
    {
        if (e == VoyageEnd.None || Ended != VoyageEnd.None) return;
        Ended = e;
        EndCard = EndLines(e);
        Paused = true; EndSaveDue = true;
    }

    /// <summary>[그대로 지켜보기]: 카드를 닫고 다시 간다 (로봇만 도는 배도 그대로 — 결산은 기록 판 '항해'에 남는다).</summary>
    public void EndWatch() { EndCard = Array.Empty<string>(); Paused = false; }

    private string[] EndLines(VoyageEnd e)
    {
        int day = (int)(Sim.Tick / Time.TicksPerDay) + 1;
        var l = new List<string> { $"{ShipName} · 항해 {Voyage} — {day}일 · {EndText(e)}" };
        var lost = Lost();
        var mind = Sim.Find<Mind>();
        if (mind != null)
        {
            var people = mind.Agents.Where(a => a.IsPerson).ToList();
            foreach (var a in people.Take(EndPeople)) l.Add($"{mind.NameOf(a.Id)} — {PersonEnd(a.Id, lost)}");
            if (people.Count > EndPeople) l.Add($"외 {people.Count - EndPeople}명");
        }
        foreach (int hi in TopScenes()) { var h = Dir.Highlights[hi]; l.Add($"큰 장면 — {HourMin(h.Tick)} {Director.Short(h.Title)}"); }
        return l.ToArray();
    }
}
