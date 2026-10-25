using ShipEngine;
using ShipSim.Art;

namespace ShipView;

/// <summary>
/// 마우스만으로 되는 화면 (docs/UI.md 1.3 — 함장 요청 10-23): 손잡이마다 부르는 명령. 작업 스레드에서 부르고 (Godot은 명령 줄로 넘긴다) 규칙 시험이 확인한다.
/// 바뀌는 것은 화면 값뿐 — 세계 · 결정론 · 지문은 그대로 (출항 준비 멈춤도 틱을 돌리지 않을 뿐이다).
/// </summary>
public sealed partial class LiveShow
{
    // ── 빠르기 (단추 · 키가 같은 표를 쓴다) ──
    public static readonly string[] SpeedLabels = { "하루 24분", "10분", "3분", "1분", "최대", "×1" };
    public static readonly double[] DayMinutesAt = { 24, 10, 3, 1, 0.01 };   // '최대' — 조용함은 기계가 되는 만큼 (한 장의 시간 예산)
    public static readonly double[] FixedGameSecPerSec = { 1, 10, 60, 600, 3600 };
    /// <summary>고른 빠르기 단추 (0~5 · -2 고정) · 고정 빠르기 몇 번째 (-1 없음).</summary>
    public int SpeedPick = 2, FixedPick = -1;

    /// <summary>빠르기를 바꾼다: 0~4 하루 길이 목표 (연출이 늦출 때는 늦춘다) · 5 실시간 ×1. 멈춘 채 고르면 다시 간다 · 출항 준비면 떠난다.</summary>
    public void SetSpeed(int at)
    {
        if (at < 0 || at >= SpeedLabels.Length) return;
        if (Holding) Depart();
        SpeedPick = at; FixedPick = -1;
        if (at < DayMinutesAt.Length) { DayMinutes = DayMinutesAt[at]; SpeedOverride = 0; }
        else SpeedOverride = 1 / Time.SecondsPerTick;
        Paused = false;
    }

    /// <summary>브레이크 없는 고정 빠르기 (×1 · ×10 · ×60 · ×600 · ×3600) — 불이 나도 그 빠르기로 지나간다.</summary>
    public void SetFixed(int i)
    {
        if (i < 0 || i >= FixedGameSecPerSec.Length) return;
        if (Holding) Depart();
        SpeedPick = -2; FixedPick = i;
        SpeedOverride = FixedGameSecPerSec[i] / Time.SecondsPerTick; Paused = false;
        Notify($"고정 빠르기 ×{FixedGameSecPerSec[i]:0} — 일이 나도 늦추지 않는다 (빠르기 단추로 연출에 맡긴다)");
    }

    /// <summary>멈춤 · 계속 (출항 준비면 떠난다).</summary>
    public void TogglePause() { if (Holding) { Depart(); return; } Paused = !Paused; }

    // ── 출항 준비 (docs/UI.md 4 — 추천 A): 띄우면 세계는 멈춘 채 항로 판이 열린다 · 항로를 누르면 고르기만 · [이대로 출항] · 멈춤 · 빠르기로 떠난다 ──
    public bool Holding;

    public void HoldForDeparture()
    {
        if (double.IsNaN(DepartInMin())) return;
        Holding = Paused = true;
        Panel = PanelKind.Route;
        var o = Sim.Find<Outside>();
        PanelPick = o != null ? Math.Max(0, o.Route - 1) : 0;
        PanelPickKey = o?.Route ?? 0;
    }

    public void Depart()
    {
        bool was = Holding;
        Holding = Paused = false;
        if (was && Panel == PanelKind.Route) Panel = PanelKind.None;
    }

    /// <summary>출발 점화까지 게임 분 (떠났으면 NaN).</summary>
    public double DepartInMin()
    {
        var o = Sim.Find<Outside>();
        if (o == null || double.IsNaN(o.PendingTo) || o.VoyageTicks > 0) return double.NaN;
        foreach (var p in o.Plan) if (p.Kind == BurnKind.Depart) return Math.Max(0, (p.Due - Sim.Tick) * Time.SecondsPerTick / 60);
        return double.NaN;
    }

    // ── 판 줄 열쇠 (세계에서 흔들리지 않는 번호 — docs/UI.md 1.2) ──
    public long PanelPickKey;
    public static long KeyHighlight(int index) => (3L << 40) + index;
    public static long KeyJournal(long tick) => (1L << 40) + tick;
    public static long KeyPerson(int id) => (2L << 40) + id;
    /// <summary>자세한 판 줄 (docs/UI2.md §4.6): 작은 탭 · 줄 (때가 있는 줄은 틱 · 믿음은 차례).</summary>
    public static long KeyDetail(int sub, long n) => (4L << 40) | ((long)sub << 36) | n;


    /// <summary>
    /// 자세히 열기 (docs/UI2.md §6.2): 사람 · 로봇은 그 이를 고르고 따라간다 · 판은 그 이의 목록 판이 넓어진 것 (갈래는 그 이를 보고 고친다 — 카드의 [자세히 보기]가 사람인지 로봇인지 몰라도) ·
    /// 같은 갈래끼리 옮기면 작은 탭은 그대로 · 주컴퓨터는 주컴퓨터 판.
    /// </summary>
    public void OpenDetail(DetailKind k, int id)
    {
        if (k == DetailKind.None) { DetailBack(); return; }
        if (k == DetailKind.Computer) { if (Panel != PanelKind.Journal) TogglePanel(PanelKind.Journal); return; }
        var mind = World.Find<Mind>();
        if (mind == null || id < 0 || id >= mind.Agents.Count) return;
        var kind = mind.Agents[id].IsPerson ? DetailKind.Person : DetailKind.Robot;
        if (kind != Detail) DetailSub = 0;
        Panel = kind == DetailKind.Person ? PanelKind.People : PanelKind.Robots;
        Detail = kind; DetailId = id;
        DetailSub = Math.Clamp(DetailSub, 0, DetailTabsOf(kind).Length - 1);
        SelectAgent(id);
        PanelPick = 0; PanelPickKey = 0;
    }

    /// <summary>작은 탭을 고른다 (첫 줄로).</summary>
    public void SetDetailSub(int i)
    {
        if (Detail == DetailKind.None) return;
        DetailSub = Math.Clamp(i, 0, DetailTabsOf(Detail).Length - 1);
        PanelPick = 0; PanelPickKey = 0;
    }

    /// <summary>자세히 → 목록 (그 이의 줄을 고른 채) · 주컴퓨터는 목록이 없어 판을 닫는다.</summary>
    public void DetailBack()
    {
        if (Detail == DetailKind.Computer) { ClosePanel(); return; }
        if (Detail == DetailKind.None) return;
        int id = DetailId;
        Detail = DetailKind.None; DetailId = -1; DetailSub = 0;
        PanelPickKey = KeyPerson(id);
        SyncPick(PanelRows());
    }

    /// <summary>자세히 연 이를 다시 따라간다.</summary>
    public void DetailFollow() { if (Detail is DetailKind.Person or DetailKind.Robot && DetailId >= 0) SelectAgent(DetailId); }

    /// <summary>고른 줄을 열쇠로 다시 찾는다 (줄 순서가 바뀌어도 같은 줄 · 열쇠가 사라지면 가까운 줄).</summary>
    private void SyncPick(PanelRow[] rows)
    {
        if (rows.Length == 0) return;
        int at = Array.FindIndex(rows, r => r.Key == PanelPickKey && r.Key != 0);
        if (at >= 0) { PanelPick = at; return; }
        PanelPick = Math.Clamp(PanelPick, 0, rows.Length - 1);
        PanelPickKey = rows[PanelPick].Key;
    }

    /// <summary>판의 줄을 눌렀다 (열쇠로): 장면 — 고르기만 · 항로 — 출항 전이면 그 항로로 (판은 열어 둔다) · 주컴퓨터 — 펼침 · 사슬 — 그 자리로 ·
    /// 사람 · 로봇 — 자세히 + 따라가기 (판은 열린 채 넓어진다 — 예전엔 판을 닫아 자세히 볼 길이 없었다 · docs/UI2.md §7 결함 3) · 자세히의 줄 — 펼침. 못 하면 false (까닭은 알림).</summary>
    public bool PanelClick(long key)
    {
        var rows = PanelRows();
        int at = Array.FindIndex(rows, r => r.Key == key);
        if (at < 0) return false;
        PanelPick = at; PanelPickKey = key;
        var row = rows[at];
        if (Detail != DetailKind.None) return true;   // 자세히의 줄: 고르기 · 펼치기
        switch (Panel)
        {
            case PanelKind.Route:
                if (key < 1) return false;
                if (!row.ActOn) { Notify("이미 떠났다 — " + row.Why); return false; }
                return ChooseRoute((int)key);
            case PanelKind.Chain:
            {
                if (key <= 0) return false;
                int cell = World.Causes[(int)key].Cell;
                if (cell < 0) { Notify(row.Why.Length > 0 ? row.Why : "그 일은 배 한 곳의 일이 아니다"); return false; }
                LookAt(cell);
                return true;
            }
            case PanelKind.People or PanelKind.Robots:
            {
                if (key < KeyPerson(0) || key >= KeyHighlight(0)) return false;
                OpenDetail(Panel == PanelKind.People ? DetailKind.Person : DetailKind.Robot, (int)(key - KeyPerson(0)));
                return true;
            }
            case PanelKind.Hand: return HandClick(key);   // 사고 판: 갈래 줄 = 겨눔 · 방금 일으킨 것 = 그 자리로
            default: return true;   // 장면 · 주컴퓨터: 고르기 · 펼치기
        }
    }

    /// <summary>줄 단추 (장면 판의 ▶ 다시보기). 못 하면 false (까닭은 알림).</summary>
    public bool PanelAct(long key)
    {
        if (Panel != PanelKind.Highlights || key < KeyHighlight(0)) return false;
        int i = (int)(key - KeyHighlight(0));
        if (i < 0 || i >= Dir.Highlights.Count) return false;
        PanelPickKey = key;
        if (!StartReplay(Dir.Highlights[i])) { Notify("그 장면은 너무 오래돼 다시 볼 수 없다 (한 시간 안의 것만)"); return false; }
        ClosePanel();
        return true;
    }

    // ── 카메라 · 보기 ──
    public void SetChainLines(bool on) => Show = on ? Overlay.Chain : Overlay.None;
    public void SetOverlay(Overlay o) => Show = o;
    public void DirectorCam() { Follow = true; Overview = false; Selected = -1; FollowSelected = false; }
    public void ToggleOverview() => Overview = !Overview;
    public void Release() { Selected = -1; FollowSelected = false; Look = default; }   // 고른 이 · 고른 물건을 놓는다
    public void FollowAgain() { if (Selected >= 0) { FollowSelected = true; Overview = false; } }

    /// <summary>그 사람 · 로봇이 그려진 자리로 카메라를 (따라가기는 그대로 — 손으로 옮긴 것과 같다).</summary>
    public void CenterOn(int agent)
    {
        var (x, y) = DrawnAt(agent);
        if (double.IsNaN(x)) return;
        _handAt = _anim; Follow = false; Overview = false; FollowSelected = false;
        CamX = x; CamY = y;
    }

    /// <summary>커서 축 확대: 확대 전후에 칸 (x, y)가 같은 화면 자리에 남게 (배 둘레 맞춤까지) · 따라가는 중이면 그 사람 축 · 배 전체면 끄고 커서 축.</summary>
    public void ZoomAt(double f, double x, double y)
    {
        if (Selected >= 0 && FollowSelected) { ZoomBy(f); return; }
        var (l, t, w, h) = Camera();
        double fx = (x - l) / w, fy = (y - t) / h;   // 커서가 화면의 어느 몫에 있나
        _handAt = _anim; Follow = false; Overview = false;
        ViewW = Math.Clamp(w * f, MinView, MaxView);
        double nw = ViewW, nh = ViewW * Aspect;
        CamX = x - fx * nw + nw / 2; CamY = y - fy * nh + nh / 2;
        var (l2, t2, _, _) = Camera();   // 배 둘레 안으로 맞춘 뒤 실제 가운데를 그대로 둔다 (다음 장에 미끄러지지 않게)
        CamX = l2 + nw / 2; CamY = t2 + nh / 2;
    }

    /// <summary>상태 띠 칸을 눌렀다: 공기 — 숨이 가장 모자란 사람 · 사람 — 쓰러진 · 다친 이 (없으면 사람 판) · 선체 — 첫 구멍 · 불 — 가장 센 불 · 전력 · 물자 — 주컴퓨터 판.</summary>
    public void GaugeGo(string icon)
    {
        var w = World; var mind = w.Find<Mind>(); var life = w.Find<Life>();
        switch (icon)
        {
            case "air" when mind != null && life != null:
            {
                int worst = -1; float lo = 0.95f;
                foreach (var a in mind.Agents) if (a.IsPerson && life.Bodies[a.C.Body].Alive != 0 && life.Bodies[a.C.Body].SpO2 < lo) { lo = life.Bodies[a.C.Body].SpO2; worst = a.Id; }
                if (worst >= 0) SelectAgent(worst); else Notify("모두 숨을 잘 쉰다");
                return;
            }
            case "crew" when mind != null && life != null:
            {
                int who = -1; float bad = 0.05f;
                foreach (var a in mind.Agents)
                {
                    if (!a.IsPerson) continue;
                    var b = life.Bodies[a.C.Body];
                    if (b.Alive == 0) continue;
                    float v = (b.Conscious == 0 ? 10 : 0) + Math.Max(b.Burn, b.Blunt) + b.Hypoxia;
                    if (v > bad) { bad = v; who = a.Id; }
                }
                if (who >= 0) SelectAgent(who); else { if (Panel != PanelKind.People) TogglePanel(PanelKind.People); }
                return;
            }
            case "hull":
            {
                var f = w.Find<Field>();
                if (f != null) for (int c = 0; c < w.Layout.N; c++) if (f.Hole[c] > 0) { LookAt(c); return; }
                Notify("구멍 난 곳이 없다");
                return;
            }
            case "fire":
            {
                var ch = w.Find<Chemistry>();
                int best = -1; double hot = 0;
                if (ch != null) for (int c = 0; c < w.Layout.N; c++) if (ch.Burning[c] != 0 && ch.Hrr[c] > hot) { hot = ch.Hrr[c]; best = c; }
                if (best >= 0) LookAt(best); else Notify("타는 곳이 없다");
                return;
            }
            default:
                if (Panel != PanelKind.Journal) TogglePanel(PanelKind.Journal);
                return;
        }
    }

    // ── 알림 · 방송 ──
    private PanelKind _noticeLink;
    /// <summary>알림을 눌렀다: 붙은 판을 연다.</summary>
    public void NoticeGo() { var k = _noticeLink; if (k != PanelKind.None && Panel != k) TogglePanel(k); _noticeLeft = 0; }
    /// <summary>방송 자막을 눌렀다: 주컴퓨터 판의 아침 방송 줄.</summary>
    public void BroadcastGo() { if (Panel != PanelKind.Journal) TogglePanel(PanelKind.Journal); PanelPickKey = -3; }
    /// <summary>안 읽은 일지가 있나 (주컴퓨터 판을 열면 지운다).</summary>
    public bool JournalNew;

    // ── 세계 위: 누른 · 올린 사람 ──
    public int HoverId = -1;
    /// <summary>메인 스레드가 마우스를 올린 사람을 알린다 (다음 장에 그 한 명의 이름 · 하는 일을 싣는다 · -1이면 없음).</summary>
    public void SetHover(int agent) => HoverId = agent;
    private bool HoverOk() => HoverId >= 0 && World.Find<Mind>() is { } m && HoverId < m.Agents.Count;

    /// <summary>사람 · 로봇이 그려진 자리 (칸 · 가운데).</summary>
    public AgentSpot[] SpotsNow()
    {
        var mind = World.Find<Mind>();
        if (mind == null) return Array.Empty<AgentSpot>();
        var spots = new AgentSpot[mind.Agents.Count];
        for (int i = 0; i < spots.Length; i++)
        {
            var a = mind.Agents[i];
            var (x, y) = DrawnAt(a.Id);
            spots[i] = new AgentSpot(a.Id, x, y, !a.IsPerson);
        }
        return spots;
    }

    /// <summary>0.9칸 안의 가장 가까운 이 (그림은 칸 위쪽으로 0.15 솟는다) — 없으면 -1. 올림과 누름이 같은 셈을 쓴다.</summary>
    public static int Nearest(IReadOnlyList<AgentSpot> spots, double x, double y) => Nearest(spots, x, y, out _);

    /// <summary>
    /// 0.9칸 안의 가장 가까운 이 · 그 거리 (칸): 사람 · 로봇이 겹치면 사람 (로봇보다 0.25칸 더 멀어도 — docs/UI2.md §3.2 · 사람을 따라가려다 곁의 로봇을 고르지 않게).
    /// </summary>
    public static int Nearest(IReadOnlyList<AgentSpot> spots, double x, double y, out double dist)
    {
        int best = -1; double score = double.MaxValue; dist = double.NaN;
        foreach (var s in spots)
        {
            if (double.IsNaN(s.X)) continue;
            double d = Math.Sqrt((s.X - x) * (s.X - x) + (s.Y - y - 0.15) * (s.Y - y - 0.15));
            if (d >= 0.9) continue;
            double v = d + (s.Robot ? 0.25 : 0);
            if (v < score) { score = v; best = s.Id; dist = d; }
        }
        return best;
    }
}
