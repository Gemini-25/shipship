using ShipEngine;

namespace ShipView;

/// <summary>겨눔 미리 보기 (한 점 — 화면 값): 되나 · 칸 · 그린 자리 (칸 가운데) · 오는 쪽 (운석 · 구름 화살표) · 이름표 · 안 되는 까닭 · 물체 (밀어 던지기).</summary>
public readonly record struct HandAim(bool Ok, int Cell, double X, double Y, double Dx, double Dy, string Label, string Why, int Obj = 0);

/// <summary>세계 위 손 표: 누른 칸에 퍼지는 호박색 고리 (나이 0~0.5초) · 기다리는 손은 깜빡이는 점.</summary>
public readonly record struct HandMark(double X, double Y, float Age, bool Waiting, bool Done = false);

/// <summary>
/// 함장의 손 — 화면 쪽 (docs/UI2.md §2.3 · §2.8 · §6.2 — U3): 사고 판 줄 · 크기 칩 · 겨눔 · 겨눔 미리 보기 (<see cref="Hand.Why"/>와 같은 판정) · 세계를 누르면 받아 두기 ·
/// 밀어 던지기 · 선 갈래는 겹쳐 보기 '선과 관'을 잠깐 · 알림 · 손 카메라 8초 · 기다리는 손 표시. 세계에 들어가는 길은 <see cref="Hand.Enqueue"/> 하나 (작업 스레드 · 틱 사이).
/// </summary>
public sealed partial class LiveShow
{
    /// <summary>겨누는 갈래 (없음 = 겨누지 않는다) · 크기.</summary>
    public HandKind Armed;
    public byte ArmedSize = 1;
    /// <summary>갈래마다 마지막으로 고른 크기 (기본 보통).</summary>
    private readonly byte[] _handSize = Enumerable.Repeat((byte)1, 17).ToArray();
    private Overlay _showBeforeArm; private bool _armedLines;
    private double _hoverX = double.NaN, _hoverY;
    private readonly List<(int cell, double at)> _handRings = new();
    /// <summary>일으킨 칸 고리 (U9): 받아 둔 손이 실제로 일어난 칸 · 화면 때 — 3초.</summary>
    private readonly List<(int cell, double at)> _doneRings = new();
    public const double DoneRingSec = 3;
    private int _handSeenSeq;   // 산 세계 한 손 장부에서 본 마지막 차례 (헛손 알림 — 개수로 세면 장부가 가득 찬 뒤로는 늘지 않는다)

    public static readonly string[] HandSizeNames = { "작게", "보통", "크게" };

    /// <summary>갈래 이름 (게임 글).</summary>
    public static string HandName(HandKind k) => k switch
    {
        HandKind.Fire => "불씨", HandKind.Hole => "외판 구멍", HandKind.PowerCut => "전선 끊기", HandKind.DataCut => "센서 선 끊기", HandKind.Fault => "설비 고장",
        HandKind.Gravity => "인공 중력 발생기 멈추기", HandKind.WaterLeak => "물관 샘", HandKind.CoolantLeak => "냉각관 샘", HandKind.DoorJam => "문 끼임",
        HandKind.O2Drift => "산소 조절 센서 어긋남", HandKind.Blast => "폭발", HandKind.Shove => "밀어 던지기", HandKind.Rock => "운석", HandKind.Cloud => "파편 구름",
        HandKind.Flare => "별 폭발", HandKind.Route => "항로", _ => "",
    };

    /// <summary>크기 칩이 있는 갈래 (작게 · 보통 · 크게).</summary>
    public static bool HandSized(HandKind k) => k is HandKind.Fire or HandKind.Blast or HandKind.Hole or HandKind.WaterLeak or HandKind.CoolantLeak or HandKind.O2Drift or HandKind.Rock or HandKind.Cloud or HandKind.Flare;
    /// <summary>겨누지 않는 갈래 (누르면 곧 — 겨눔 줄의 [일으키기]).</summary>
    public static bool HandAimless(HandKind k) => k is HandKind.Gravity or HandKind.O2Drift or HandKind.Flare;
    /// <summary>선 · 관 갈래 (겨누는 동안 겹쳐 보기 '선과 관').</summary>
    public static bool HandLines(HandKind k) => k is HandKind.PowerCut or HandKind.DataCut or HandKind.WaterLeak or HandKind.CoolantLeak;
    /// <summary>바깥 갈래 (손 카메라를 옮기지 않는다 — 잡히거나 닿을 때 연출이 간다).</summary>
    private static bool HandOutside(HandKind k) => k is HandKind.Rock or HandKind.Cloud or HandKind.Flare or HandKind.O2Drift or HandKind.Route;

    // 판 줄 열쇠 (docs/UI2.md §4.6)
    public static long KeyHandKind(HandKind k) => (5L << 40) + (int)k;
    public static long KeyHandSize(HandKind k, int size) => (6L << 40) + (int)k * 4 + size;
    public static long KeyHandDone(int seq) => (7L << 40) + seq;

    private Hand? LiveHandEngine => Sim.Find<Hand>();

    /// <summary>손을 쓸 수 있나 · 못 쓰면 까닭 (다시보기 중 · 기다리는 손 가득 · 손이 없는 세계).</summary>
    public string HandBlocked()
    {
        var h = LiveHandEngine;
        if (h == null) return "이 세계에는 함장의 손이 닿지 않는다";
        if (_replay != null) return "다시보기 중에는 일으킬 수 없다 — 돌아온 뒤에";
        if (h.Waiting.Count >= Hand.MaxWaiting) return "손이 밀려 있다 — 시간이 가야 한다";
        return "";
    }

    /// <summary>겨눈다 (사고 판 줄 · 크기 칩): 선 · 관 갈래는 겹쳐 보기 '선과 관'을 잠깐 켠다 (그만두면 예전 것으로).</summary>
    public void ArmHand(HandKind k, byte size)
    {
        string why = HandBlocked();
        if (why.Length > 0) { Notify(why, 3, PanelKind.Hand); return; }
        size = (byte)Math.Min((int)size, 2);
        bool wasLines = _armedLines;
        Armed = k; ArmedSize = size; _handSize[(int)k] = size;
        if (HandLines(k)) { if (!wasLines) { _showBeforeArm = Show; _armedLines = true; } Show = Overlay.Lines; }
        else if (wasLines) { if (Show == Overlay.Lines) Show = _showBeforeArm; _armedLines = false; }
    }

    /// <summary>그만두기 (오른쪽 누르기 · [그만두기] · 같은 줄 다시 · Esc).</summary>
    public void Disarm()
    {
        Armed = HandKind.None;
        if (_armedLines) { if (Show == Overlay.Lines) Show = _showBeforeArm; _armedLines = false; }
    }

    /// <summary>그 자리 가장 가까운 물체 (0.7칸 안 · 그린 자리 — 놓인 것 · 미끄러지는 것) — 없으면 0.</summary>
    private int PropNear(double x, double y)
    {
        var st = Sim.Find<Structure>();
        if (st == null) return 0;
        int best = 0; double bd = 0.7 * 0.7;
        foreach (var p in st.Props)
        {
            if (p.State is 5 or 6 or 7 || p.Kind is 5 or 6) continue;
            var (px, py, _) = DrawnProp(p.Id);
            if (double.IsNaN(px)) continue;
            double d = (px - x) * (px - x) + (py - y) * (py - y);
            if (d < bd) { bd = d; best = p.Id; }
        }
        return best;
    }

    /// <summary>겨눈 손 하나를 만든다 (그 자리 · 겨눈 갈래 · 크기) — 운석은 외판 칸 · 구름은 누른 쪽에서 · 고장은 그 설비의 첫 부품 (봉함 · 전기 · 기계 · 거르개 차례).</summary>
    private HandOrder AimOrder(double x, double y) => OrderFor(Armed, ArmedSize, x, y);

    /// <summary>그 자리 · 갈래 · 크기의 손 하나 (겨눔 · 카드의 [사고 ›]가 같은 셈).</summary>
    private HandOrder OrderFor(HandKind kind, byte size, double x, double y)
    {
        int cell = CellUnder(x, y);
        var o = new HandOrder { Kind = kind, Size = size, Cell = cell };
        var L = Sim.Layout;
        switch (kind)
        {
            case HandKind.Cloud:
            {
                double dx = L.W / 2.0 - x, dy = L.H / 2.0 - y, n = Math.Sqrt(dx * dx + dy * dy);
                if (n > 1e-6) { o.X = (float)(dx / n); o.Y = (float)(dy / n); }
                break;
            }
            case HandKind.Shove: o.Obj = PropNear(x, y); break;
            case HandKind.Fault:
            {
                var net = Sim.Find<Network>();
                int d = net != null ? Hand.DeviceAt(net, L, cell) : -1;
                if (d >= 0) foreach (var p in new[] { Part.Seal, Part.Electric, Part.Mechanical, Part.Filter }) if (net!.Devices[d].Has(p) && !net.IsBroken(d, p)) { o.Part = (byte)p; break; }
                break;
            }
        }
        return o;
    }

    private string WhereName(int cell)
    {
        var L = Sim.Layout;
        if (cell < 0) return "배 바깥";
        string room = L.Room[cell] >= 0 ? L.Rooms[L.Room[cell]].Name : "";
        if (room.Length == 0) foreach (int j in new[] { cell - 1, cell + 1, cell - L.W, cell + L.W }) if (j >= 0 && j < L.N && L.Room[j] >= 0) { room = L.Rooms[L.Room[j]].Name; break; }
        string furn = L.FurnitureAt[cell] >= 0 ? " " + Sim.Tables.Equip(L.Furniture[L.FurnitureAt[cell]].Type).Name : L.Kind[cell] == CellKind.Wall ? (L.Hull[cell] ? " 외판" : " 벽") : L.Kind[cell] == CellKind.Door ? " 문" : "";
        return (room.Length > 0 ? room : "통로") + furn;
    }

    /// <summary>겨눔 미리 보기 (올린 한 점 — <see cref="Hand.Why"/>와 같은 판정): 이름표 '불씨 · 보통 — 식당 침대' · 안 되면 그 까닭.</summary>
    public HandAim HandAimNow()
    {
        if (Armed == HandKind.None) return default;
        string label = HandName(Armed) + (HandSized(Armed) ? " · " + HandSizeNames[ArmedSize] : "");
        if (HandAimless(Armed))
        {
            var oo = new HandOrder { Kind = Armed, Size = ArmedSize, Cell = -1 };
            string w = HandBlocked() is { Length: > 0 } b ? b : Hand.Why(Sim, oo);
            return new HandAim(w.Length == 0, -1, double.NaN, 0, 0, 0, label + " — [일으키기]를 누른다", w);
        }
        if (double.IsNaN(_hoverX)) return new HandAim(false, -1, double.NaN, 0, 0, 0, label, "배 위를 누른다");
        var o = AimOrder(_hoverX, _hoverY);
        string why = HandBlocked() is { Length: > 0 } bl ? bl : Hand.Why(Sim, o);
        var L = Sim.Layout;
        double cx = _hoverX, cy = _hoverY, dx = 0, dy = 0;
        if (o.Cell >= 0) (cx, cy) = CellDrawn(o.Cell);
        string where = WhereName(o.Cell);
        switch (Armed)
        {
            case HandKind.Rock when why.Length == 0:
            {
                var (ux, uy, _) = Hand.RockAim(L, o.Cell);
                dx = -ux; dy = -uy;   // 바깥 쪽 (운석이 오는 줄)
                where += " — 91초 뒤";
                break;
            }
            case HandKind.Cloud: dx = -o.X; dy = -o.Y; cx = _hoverX; cy = _hoverY; where = "누른 쪽에서 14분 뒤"; break;
            case HandKind.Shove:
            {
                var st = Sim.Find<Structure>();
                int i = o.Obj > 0 && st != null ? st.IndexOf(o.Obj) : -1;
                if (i >= 0) { (cx, cy, _) = DrawnProp(o.Obj); where = st!.PropName(st.Props[i]) + " — 눌러 끌고 놓는다"; }
                break;
            }
            case HandKind.Fault when why.Length == 0: where += " " + Device.PartName((Part)o.Part) + " 부품"; break;
        }
        if (why == "그 물건이 없다" && Armed == HandKind.Shove) why = "물건 위를 누른다";
        if (why.Length == 0 && o.Cell >= 0 && Armed is not (HandKind.Shove or HandKind.Cloud)) where += AimPeople(o.Cell, Armed == HandKind.Fire);   // 손 결과 이름표 (V8 — docs/OBSERVER.md §7.3)
        return new HandAim(why.Length == 0, o.Cell, cx, cy, dx, dy, label + " — " + where, why, o.Obj);
    }

    /// <summary>손 결과 이름표 (docs/OBSERVER.md §7.3 — V8): ' · 이 방 사람 2 (1명 잠)' · 불씨면 ' · 가장 가까운 소화기 4m' (소화기 · 이산화탄소 소화기가 든 선반 · 보관함 — 칸 1m).</summary>
    private string AimPeople(int cell, bool fire)
    {
        var L = Sim.Layout;
        int room = Director.RoomNear(L, cell);
        if (room < 0) return "";
        var mind = Sim.Find<Mind>(); var life = Sim.Find<Life>();
        int n = 0, asleep = 0;
        if (mind != null && life != null)
            foreach (var a in mind.Agents)
            {
                if (!a.IsPerson || a.C.Cell < 0 || a.C.Cell >= L.N || L.Room[a.C.Cell] != room) continue;
                var b = life.Bodies[a.C.Body];
                if (b.Alive == 0) continue;
                n++; if (b.Asleep != 0) asleep++;
            }
        string s = $" · 이 방 사람 {n}" + (asleep > 0 ? $" ({asleep}명 잠)" : "");
        if (fire)
        {
            double best = double.MaxValue;
            if (mind != null)
                foreach (var fu in L.Furniture)
                {
                    if (fu.Cells.Length == 0 || mind.Stock[fu.Id * mind.ItemCount + mind.iExt] + mind.Stock[fu.Id * mind.ItemCount + mind.iCO2] <= 0) continue;
                    double dx = L.X(fu.Cells[0]) - L.X(cell), dy = L.Y(fu.Cells[0]) - L.Y(cell);
                    best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy));
                }
            s += best < double.MaxValue ? $" · 가장 가까운 소화기 {Math.Round(best):0}m" : " · 남은 소화기 없음";
        }
        return s;
    }

    /// <summary>받아 둔다 (산 세계 · 다음 틱 맨 처음에 일어난다): 알림 · 누른 칸 고리 · 손 카메라 8초 (바깥 갈래는 그대로) · 한 번 누르면 한 번 (겨눔이 풀린다).</summary>
    private bool Take(HandOrder o)
    {
        string why = HandBlocked() is { Length: > 0 } b ? b : Hand.Why(Sim, o);
        if (why.Length > 0) { Notify(why, 3, PanelKind.Hand); return false; }
        if (!LiveHandEngine!.Enqueue(o)) { Notify("손이 밀려 있다 — 시간이 가야 한다", 3, PanelKind.Hand); return false; }
        string what = HandName(o.Kind) + (HandSized(o.Kind) ? " · " + HandSizeNames[Math.Min((int)o.Size, 2)] : "");
        string where = o.Kind == HandKind.Shove ? (Sim.Find<Structure>() is { } st && st.IndexOf(o.Obj) is int i and >= 0 ? st.PropName(st.Props[i]) : "") : o.Cell >= 0 ? WhereName(o.Cell) : "";
        Notify(Paused || Holding ? $"받아 두었다 — {what} · 시간이 가면 일어난다" : $"함장이 일으켰다 — {what}{(where.Length > 0 ? $" ({where})" : "")}", 3, PanelKind.Hand);
        int cell = o.Kind == HandKind.Shove ? CellUnder(_hoverX, _hoverY) : o.Cell;
        if (cell >= 0) { _handRings.Add((cell, _anim)); if (_handRings.Count > 8) _handRings.RemoveAt(0); }
        if (cell >= 0 && !HandOutside(o.Kind)) LookAt(cell);   // 손 카메라 — 8초 그 자리에 머문 뒤 연출이 맡는다
        Disarm();
        return true;
    }

    /// <summary>세계를 눌렀다 (겨누는 중 · 끌지 않고 놓음): 그 자리에 겨눈 손.</summary>
    public bool HandAt(double x, double y)
    {
        if (Armed == HandKind.None) return false;
        _hoverX = x; _hoverY = y;
        if (HandAimless(Armed)) return HandNow();
        if (Armed == HandKind.Shove) { Notify("물건 위에서 눌러 끌고 놓는다 — 끈 만큼 빠르게", 3, PanelKind.Hand); return false; }
        return Take(AimOrder(x, y));
    }

    /// <summary>밀어 던지기 (물체 위에서 눌러 끌고 놓았다): 끈 쪽 · 빠르기 (m/s — 8까지).</summary>
    public bool HandShove(int prop, double vx, double vy)
    {
        if (Armed != HandKind.Shove) return false;
        double v = Math.Sqrt(vx * vx + vy * vy);
        if (v < 0.5) { Notify("조금 더 끌어야 한다 — 0.5칸에 1m/s", 3, PanelKind.Hand); return false; }
        double k = v > Hand.MaxShove ? Hand.MaxShove / v : 1;
        return Take(new HandOrder { Kind = HandKind.Shove, Obj = prop, X = (float)(vx * k), Y = (float)(vy * k), Cell = -1 });
    }

    /// <summary>겨눔 줄의 [일으키기] (겨누지 않는 갈래 — 인공 중력 발생기 · 산소 조절 센서 · 별 폭발).</summary>
    public bool HandNow()
    {
        if (Armed == HandKind.None) return false;
        if (!HandAimless(Armed)) { Notify("배 위를 누른다", 3, PanelKind.Hand); return false; }
        return Take(new HandOrder { Kind = Armed, Size = ArmedSize, Cell = -1 });
    }

    /// <summary>산 세계의 한 손 장부를 지켜본다: 헛손 (받은 뒤 대상이 사라짐)은 알림으로.</summary>
    private void WatchHand()
    {
        var h = LiveHandEngine;
        if (h == null) return;
        int last = h.Done.Count > 0 ? h.Done[^1].Seq : 0;
        if (_handSeenSeq > last) _handSeenSeq = last;   // 다른 세계 (차례가 거꾸로 갔다)
        int i = h.Done.Count;
        while (i > 0 && h.Done[i - 1].Seq > _handSeenSeq) i--;   // 장부는 차례가 오르는 줄 — 본 것 다음부터
        for (; i < h.Done.Count; i++)
        {
            if (h.Done[i].Cause == 0) { Notify($"그사이 대상이 바뀌어 일으키지 못했다 — {HandName(h.Done[i].Kind)}", 4, PanelKind.Hand); continue; }
            int cell = h.Done[i].Cell >= 0 ? h.Done[i].Cell : Sim.Causes[h.Done[i].Cause].Cell;   // 일으킨 칸 (밀어 던지기 · 겨누지 않는 갈래는 원인 마디의 칸)
            if (cell >= 0) { _doneRings.Add((cell, _anim)); if (_doneRings.Count > 8) _doneRings.RemoveAt(0); }
        }
        _handSeenSeq = last;
    }

    /// <summary>세계 위 손 표 (누른 칸 고리 0.5초 · 기다리는 손 점).</summary>
    private HandMark[] HandMarksNow()
    {
        var h = LiveHandEngine;
        var o = new List<HandMark>();
        foreach (var (cell, at) in _handRings)
        {
            double age = _anim - at;
            if (age > 0.5 || _replay != null) continue;
            var (x, y) = CellDrawn(cell);
            o.Add(new HandMark(x, y, (float)age, false));
        }
        foreach (var (cell, at) in _doneRings)
        {
            double age = _anim - at;
            if (age > DoneRingSec || _replay != null) continue;
            var (x, y) = CellDrawn(cell);
            o.Add(new HandMark(x, y, (float)age, false, true));
        }
        if (h != null && _replay == null)
            foreach (var w in h.Waiting)
                if (w.Cell >= 0) { var (x, y) = CellDrawn(w.Cell); o.Add(new HandMark(x, y, 0, true)); }
        return o.ToArray();
    }

    /// <summary>사고 판 (X — docs/UI2.md §5.3): 배 안 · 배 전체 · 바깥 갈래 줄 (크기 칩은 자리표가) · 방금 일으킨 것 다섯.</summary>
    private void HandRows(List<PanelRow> rows)
    {
        string why = HandBlocked();
        bool can = why.Length == 0;
        void Kind(HandKind k, string how, string knows) =>
            rows.Add(new PanelRow(HandName(k), how + "\n" + knows, Armed == k, Key: KeyHandKind(k), ActOn: can, Why: why));
        rows.Add(new PanelRow("배 안 — 누른 자리에", "", false, Key: -11));
        Kind(HandKind.Fire, "탈 것 위를 누른다 · 산소 · 탈 것대로 번진다", "주컴퓨터는 연기 · 열 센서로 안다");
        Kind(HandKind.Blast, "배 안을 누른다 · 벽 · 문 · 가구 · 사람에게 충격", "쾅 소리 · 센서로 안다");
        Kind(HandKind.Hole, "외판 칸을 누른다 · 공기가 빠진다", "주컴퓨터는 기압 센서로 안다");
        Kind(HandKind.PowerCut, "전선 칸을 누른다 (선과 관이 보인다) · 그 너머 전기가 끊긴다", "차단기 · 장치로 안다 — 전기가 끊긴 방은 못 본다");
        Kind(HandKind.DataCut, "센서 선 칸을 누른다 · 그 너머 방을 못 본다", "센서가 끊긴 것으로 안다 · 로봇이 잇는다");
        Kind(HandKind.WaterLeak, "물관 칸을 누른다 · 작게 · 보통은 이음매 · 크게는 터짐", "아무도 모르는 샘 — 바닥 물 · 물탱크로 안다");
        Kind(HandKind.CoolantLeak, "냉각관 칸을 누른다 · 암모니아가 샌다", "매운 냄새 · 암모니아 센서로 안다");
        Kind(HandKind.Fault, "설비를 누른다 · 1분 안에 망가진다 (봉함이면 기체가 샌다)", "장치 상태로 안다");
        Kind(HandKind.DoorJam, "문 칸을 누른다 · 그 자리에서 멈춘다", "문 상태로 안다");
        Kind(HandKind.Shove, "물건 위에서 눌러 끌고 놓는다 · 끈 만큼 빠르게 (8m/s까지)", "날아가 맞는 것은 세상이 셈한다");
        rows.Add(new PanelRow("배 전체 — 누르면 곧", "", false, Key: -12));
        Kind(HandKind.Gravity, "발생기가 망가진다 · 몸이 뜬다", "주컴퓨터는 장치 상태로 알고 방송한다");
        Kind(HandKind.O2Drift, "산소 조절 센서가 낮게 읽기 시작한다 (작게 6시간 · 보통 2시간 · 크게 30분에 걸쳐)", "아무도 모른다 — 주컴퓨터가 제 방 센서와 견줘 알아챈다");
        rows.Add(new PanelRow("바깥 — 우주에서", "", false, Key: -13));
        Kind(HandKind.Rock, "외판 칸을 누른다 · 그 바깥에서 91초 뒤 (5g · 60g · 0.5kg)", "레이더가 크기만큼 잡는다 — 작으면 모른 채 맞는다");
        Kind(HandKind.Cloud, "배 바깥을 누른다 · 누른 쪽에서 14분 뒤", "센서 배열이 잡아 대피를 알린다");
        Kind(HandKind.Flare, "1분 뒤 조짐 · 세기 1 · 2 · 4", "X선 감시로 안다 · 대피");
        var h = LiveHandEngine;
        if (h != null && h.Done.Count > 0)
        {
            rows.Add(new PanelRow("방금 일으킨 것", h.Waiting.Count > 0 ? $"기다리는 손 {h.Waiting.Count} — 시간이 가면 일어난다" : "", false, Key: -14));
            for (int i = h.Done.Count - 1, n = 0; i >= 0 && n < 5; i--, n++)
            {
                var d = h.Done[i];
                bool here = d.Cause != 0 && Sim.Causes[d.Cause].Cell >= 0;
                rows.Add(new PanelRow($"{HourMin(d.Tick)}  {HandName(d.Kind)}{(HandSized(d.Kind) ? " · " + HandSizeNames[Math.Min((int)d.Size, 2)] : "")}",
                    d.Cause != 0 ? Sim.Causes.Text(d.Cause) : "그사이 대상이 바뀌어 일으키지 못했다", false, Key: KeyHandDone(d.Seq), ActOn: here, Why: "그 일은 배 한 곳의 일이 아니다"));
            }
        }
    }

    /// <summary>사고 판 줄을 눌렀다: 갈래 줄 = 겨눔 (겨누는 그 줄이면 그만두기) · 방금 일으킨 것 = 그 자리로.</summary>
    private bool HandClick(long key)
    {
        if (key >> 40 == 5)
        {
            var k = (HandKind)(key - KeyHandKind(HandKind.None));
            if (Armed == k) { Disarm(); return true; }
            ArmHand(k, _handSize[(int)k]);
            return Armed == k;
        }
        if (key >> 40 == 7 && LiveHandEngine is { } h)
        {
            int seq = (int)(key - KeyHandDone(0));
            foreach (var d in h.Done)
                if (d.Seq == seq)
                {
                    int cell = d.Cause != 0 ? Sim.Causes[d.Cause].Cell : -1;
                    if (cell < 0) { Notify("그 일은 배 한 곳의 일이 아니다"); return false; }
                    LookAt(cell);
                    return true;
                }
        }
        return false;
    }

    /// <summary>크기 칩: 그 크기로 겨눈다.</summary>
    public void HandSizeClick(long key)
    {
        long n = key - KeyHandSize(HandKind.None, 0);
        ArmHand((HandKind)(n / 4), (byte)(n % 4));
    }

    /// <summary>갈래마다 고른 크기 (칩 켜짐 — 화면 값).</summary>
    public byte[] HandSizes() => (byte[])_handSize.Clone();
}
