using ShipEngine;

namespace ShipView;

/// <summary>예측 하나의 맞댐: 열림 · 맞음 · 틀림 · 끼어든 일 (맞음 몫에서 뺀다) · 맞댈 수 없다.</summary>
public enum ForecastState : byte { Open, Right, Wrong, Interfered, Unmatched }

/// <summary>장부의 예측 한 줄 (docs/UI2.md §4.5 (나)): 주컴퓨터의 예측 · 그때의 수 · 맞댈 때 · 실제 · 까닭.</summary>
public sealed class Forecast
{
    public Guess G;
    public long Seq, Due, Closed;
    public ForecastState State;
    /// <summary>끼어든 일 · 틀린 까닭 · 맞댈 수 없는 까닭 (게임 글).</summary>
    public string Why = "";
    /// <summary>실제 — 갈래마다 A · B · C와 같은 뜻 (봉쇄: 죽음 · 쓰러짐 · 다른 방이 잃은 기압 / 미리 돌려 보기: 쓰러짐 (더) · 잃은 공기 / 걸음: 걸린 분 / 물자: 하루 쓴 식량 · 물 / 대피: 닿기까지 초).</summary>
    public float ActA, ActB, ActC;
    public int DeadThen, DownThen;
    public float[]? BeforeKPa;
    public double AirThen, FoodThen, WaterThen;
    public byte RationThen;
    public bool Opened, BlindSeen, ByPerson, RationMoved;
    public long Arrived = -1;
    /// <summary>예측한 때와 맞댈 때 사이에 끼어든 일 (첫 하나 — 빈 글이면 없다).</summary>
    public string Interrupt = "";
    public ImagineShot? Shot;
    public ImagineEnd? Actual;
}

/// <summary>
/// 예측 장부 (docs/UI2.md §4.5 (나) — 관객의 장부 · 세계 밖): 틱마다 읽기만 한다 (산 세계만 — 다시보기 중에는 멈춘다) ·
/// 주컴퓨터의 화면용 예측 고리 (<see cref="ShipMind.Guesses"/>)에서 새 예측을 받아 맞댈 때가 되면 실제와 같은 셈으로 맞댄다 ·
/// 끼어든 일 (함장의 손 · 다른 곳의 큰 일)은 따로 두고 맞음 몫에서 뺀다 · 센서는 주컴퓨터가 읽은 틱마다 방 하나씩 그 틱 앞 참값과 견준다.
/// MIND.md 12단의 주컴퓨터 제 어긋남 (판단 엔진)과는 다르다 — 이것은 관객의 장부다.
/// </summary>
public sealed class ForecastBook
{
    public const int Keep = 200, Kinds = 6;
    public readonly List<Forecast> Open = new(), Closed = new();
    public readonly int[] Right = new int[Kinds], Wrong = new int[Kinds], Interfered = new int[Kinds], Unmatched = new int[Kinds];
    /// <summary>어긋남 합 (봉쇄: 쓰러짐 차이 · 걸음: 실제 ÷ 예측) · 그 수.</summary>
    public readonly double[] OffSum = new double[Kinds];
    public readonly int[] OffN = new int[Kinds];
    /// <summary>틀린 까닭마다 수 (갈래마다).</summary>
    public readonly Dictionary<string, int>[] WhyWrong = Enumerable.Range(0, Kinds).Select(_ => new Dictionary<string, int>()).ToArray();
    public long SensorWire, SensorWireRight, SensorRadio, SensorRadioRight, SensorBlind;
    /// <summary>날마다 맞음 · 맞댐 (끼어든 일 · 맞댈 수 없는 것은 뺀다).</summary>
    public readonly SortedDictionary<int, (int right, int total)> Days = new();
    public long Seq;

    private readonly float[][] _kpa = new float[8][];
    private readonly long[] _kpaTick = new long[8];
    private long _seen;
    private int _reads = -1, _truthRoom = -1, _sensorIdx;
    private double _truthKPa, _truthO2;
    private int _handSeen;
    private long _factsSeen;

    /// <summary>그 칸의 방 (벽 · 문 칸은 곁의 첫 방 — 없으면 -1).</summary>
    private static int RoomNear(Layout L, int c)
    {
        if (c < 0 || c >= L.N) return -1;
        if (L.Room[c] >= 0) return L.Room[c];
        foreach (int j in new[] { c - 1, c + 1, c - L.W, c + L.W }) if (j >= 0 && j < L.N && L.Room[j] >= 0) return L.Room[j];
        return -1;
    }
    private readonly List<(long tick, int room, string text, bool hand)> _events = new();

    public static string KindName(GuessKind k) => k switch
    {
        GuessKind.Seal => "봉쇄 상상", GuessKind.Rehearse => "미리 돌려 보기", GuessKind.Step => "걸음 시간", GuessKind.Supply => "물자 날", GuessKind.Shelter => "대피 도착", _ => "",
    };

    /// <summary>한 틱 (세계가 한 걸음 나아간 뒤) — 읽기만.</summary>
    public void Observe(Sim w)
    {
        var sm = w.Find<ShipMind>(); var f = w.Find<Field>();
        if (sm == null || f == null) return;
        var L = w.Layout;
        long now = w.Tick;
        // 센서: 방금 걸음에서 주컴퓨터가 읽었다 — 그 틱 앞 (지난 걸음 끝)에 셈해 둔 참값과 견준다
        if (_reads >= 0 && sm.Reads != _reads && _truthRoom >= 0 && _truthRoom < sm.RoomSeen.Length)
        {
            int r = _truthRoom;
            if (sm.RoomSeen[r] == 0) SensorBlind++;
            else
            {
                bool ok = Math.Abs(sm.RoomKPa[r] - _truthKPa) <= 1 && Math.Abs(sm.RoomO2[r] - _truthO2) <= 0.5;
                if (sm.RoomSeen[r] == 1) { SensorWire++; if (ok) SensorWireRight++; }
                else { SensorRadio++; if (ok) SensorRadioRight++; }
            }
        }
        _truthRoom = -1;
        _reads = sm.Reads;
        // 방마다 첫 칸 기압 (8틱 고리 — 틱 t 끝 = 틱 t + 1 가운데: 상상의 '그때')
        int slot = (int)(now & 7);
        var arr = _kpa[slot] ??= new float[L.Rooms.Count];
        for (int r = 0; r < L.Rooms.Count && r < arr.Length; r++) arr[r] = L.Rooms[r].Cells.Length > 0 ? (float)f.Pressure(L.Rooms[r].Cells[0]) : 0;
        _kpaTick[slot] = now;
        Interruptions(w, sm, now);
        // 새 예측
        var ring = sm.Guesses;
        if (ring != null && ring.Added > _seen)
        {
            long fresh = Math.Min(ring.Added - _seen, ring.Count);
            for (long k = fresh - 1; k >= 0; k--) Receive(w, sm, f, ring.Newest((int)k), now);
            _seen = ring.Added;
        }
        for (int i = Open.Count - 1; i >= 0; i--) Track(w, sm, f, Open[i], now);
        // 다음 걸음에서 주컴퓨터가 읽는다 — 그 틱 앞 참값 (같은 셈: 방 평균 기압 · 가장 옅은 산소)을 방 하나씩 돌아가며
        int eff = ShipMind.ReadEvery * (sm.Throttled != 0 ? 3 : 1);
        if (sm.Online && now % eff == 0 && L.Rooms.Count > 0)
        {
            for (int tries = 0; tries < L.Rooms.Count; tries++)
            {
                int r = _sensorIdx++ % L.Rooms.Count;
                var cells = L.Rooms[r].Cells;
                if (cells.Length == 0) continue;
                double p = 0, o2 = double.MaxValue;
                foreach (int c in cells) { double m = f.Read(c, out _, out double pc); p += pc; o2 = Math.Min(o2, m > 1e-12 ? pc * f.Gas(w.Tables.gO2, c) / m : 0); }
                _truthRoom = r; _truthKPa = p / cells.Length; _truthO2 = o2;
                break;
            }
        }
    }

    /// <summary>끼어든 일: 함장의 손 (받은 손이 적용됐다) · 다른 곳의 새 사고 (불 · 벽 뚫림 · 관 샘).</summary>
    private void Interruptions(Sim w, ShipMind sm, long now)
    {
        if (w.Find<Hand>() is { } hand && hand.Done.Count > 0 && hand.Done[^1].Seq != _handSeen)   // 새로 적용된 손이 있을 때만 훑는다
        {
            if (hand.Done[^1].Seq < _handSeen) _handSeen = 0;
            foreach (var o in hand.Done)
                if (o.Seq > _handSeen && o.Kind != HandKind.Route) _events.Add((o.Tick, -1, "함장이 일으킨 " + LiveShow.HandName(o.Kind), true));
            _handSeen = hand.Done[^1].Seq;
        }
        // 새 사고의 시작 (월드 사실 — 불이 붙었다 · 벽이 터졌다 · 관이 샌다): 같은 사고가 번진 것 (옆 방으로 새는 공기 · 쓰러짐)은 상상이 이미 셈한 미래라 끼어든 일이 아니다
        if (_factsSeen > w.Facts.Total) _factsSeen = 0;
        foreach (var fa in w.Facts.Since(_factsSeen))
        {
            if (fa.Kind is not (FactKind.Ignition or FactKind.WallBreach or FactKind.PipeLeak)) continue;
            int room = RoomNear(w.Layout, fa.Cell);
            _events.Add((fa.Tick, room, $"다른 곳의 새 사고 — {sm.RoomName(room)} {(fa.Kind == FactKind.Ignition ? "불이 붙었다" : fa.Kind == FactKind.WallBreach ? "벽이 뚫렸다" : "관이 샌다")}", false));
        }
        _factsSeen = w.Facts.Total;
        if (_events.Count > 256) _events.RemoveRange(0, _events.Count - 256);
    }

    private static (int dead, int down) Counts(Sim w, bool hypoxia)
    {
        var mind = w.Find<Mind>(); var life = w.Find<Life>();
        int dead = 0, down = 0;
        if (mind == null || life == null) return (0, 0);
        foreach (var a in mind.Agents)
        {
            if (!a.IsPerson || a.C.Body < 0) continue;
            var b = life.Bodies[a.C.Body];
            if (b.Alive == 0) dead++; else if (b.Conscious == 0 || hypoxia && b.Hypoxia > 0.3f) down++;
        }
        return (dead, down);
    }

    /// <summary>배가 가진 공기 (예비 탱크 + 배 안 칸의 몰 — 미리 돌려 보기와 같은 셈).</summary>
    private static double Air(Sim w)
    {
        var f = w.Find<Field>(); var net = w.Find<Network>(); var L = w.Layout;
        double n = net?.AirReserve ?? 0;
        if (f != null) for (int i = 0; i < L.N; i++) if (L.IsSpace(i)) n += f.Moles(i);
        return n;
    }

    /// <summary>마실 물 (물탱크 + 되찾을 오수 — 주컴퓨터 물 셈과 같은 셈).</summary>
    private static double WaterNow(Sim w)
    {
        var net = w.Find<Network>();
        if (net == null) return 0;
        bool rec = false;
        foreach (var d in net.Devices) if (d.Def.Role == "recycler" && net.Working(d.Id) && !net.IsBroken(d.Id, Part.Filter)) { rec = true; break; }
        return net.WaterTank + (rec ? Network.WaterRecovery * net.WasteWater : 0);
    }

    private static bool AnyBlind(Sim w, ShipMind sm)
    {
        for (int r = 0; r < sm.RoomSeen.Length && r < w.Layout.Rooms.Count; r++) if (w.Layout.Rooms[r].Cells.Length > 0 && sm.RoomSeen[r] == 0) return true;
        return false;
    }

    /// <summary>지금 배의 모습 (상상 그림과 같은 꼴 — 방마다 첫 칸 기압 · 사람마다 칸과 상태).</summary>
    public static ImagineEnd Picture(Sim w)
    {
        var L = w.Layout; var f = w.Find<Field>(); var mind = w.Find<Mind>(); var life = w.Find<Life>();
        var end = new ImagineEnd { RoomKPa = new float[L.Rooms.Count] };
        for (int r = 0; r < L.Rooms.Count; r++) end.RoomKPa[r] = f != null && L.Rooms[r].Cells.Length > 0 ? (float)f.Pressure(L.Rooms[r].Cells[0]) : 0;
        if (mind != null && life != null)
        {
            var people = mind.Agents.Where(q => q.IsPerson && q.C.Body >= 0).ToList();
            end.Cell = new int[people.Count]; end.State = new byte[people.Count];
            for (int k = 0; k < people.Count; k++) { var b = life.Bodies[people[k].C.Body]; end.Cell[k] = b.Cell; end.State[k] = (byte)(b.Alive == 0 ? 2 : b.Conscious == 0 || b.Hypoxia > 0.3f ? 1 : 0); }
        }
        return end;
    }

    private void Receive(Sim w, ShipMind sm, Field f, Guess g, long now)
    {
        if (g.Kind == GuessKind.StepEnd)
        {
            var st = Open.FirstOrDefault(x => x.G.Kind == GuessKind.Step && x.G.Obj == g.Obj && x.G.Incident == g.Incident);
            if (st == null) return;
            st.ActA = (float)((g.Tick - st.G.Tick) / (double)Time.Minutes(1));
            bool ok = g.Verdict == 1 && st.ActA <= st.G.A * 1.5f;
            if (g.Verdict == 1) { OffSum[(int)GuessKind.Step] += st.ActA / Math.Max(0.1, st.G.A); OffN[(int)GuessKind.Step]++; }
            Close(st, ok ? ForecastState.Right : ForecastState.Wrong, ok ? "" : g.Verdict == 0 ? (st.ByPerson ? "사람이 계획대로 하지 못했다 — 막혔다 · 시간을 넘겼다" : "막혔다 · 시간을 넘겼다") : st.BlindSeen ? "못 본 방이 있었다 — 더 오래 걸렸다" : "더 오래 걸렸다", g.Tick);
            return;
        }
        var fc = new Forecast { G = g, Seq = ++Seq, BlindSeen = AnyBlind(w, sm) };
        switch (g.Kind)
        {
            case GuessKind.Seal:
            {
                fc.Due = g.Tick + ShipMind.ImagineTicks;
                (fc.DeadThen, fc.DownThen) = Counts(w, true);
                int slot = (int)(g.Tick & 7);
                fc.BeforeKPa = _kpaTick[slot] == g.Tick && _kpa[slot] != null ? (float[])_kpa[slot].Clone() : Picture(w).RoomKPa;
                if (sm.LastImagine is { } shot && shot.Tick == g.Tick && shot.Room == g.Room) fc.Shot = shot;
                break;
            }
            case GuessKind.Rehearse:
                if (g.Verdict == 0) { Close(fc, ForecastState.Unmatched, "하지 않기로 했다 — 일어나지 않아 맞대지 않는다", now); return; }
                fc.Due = g.Tick + ShipMind.ImagineTicks;
                fc.DownThen = Counts(w, false) is var (d0, n0) ? d0 + n0 : 0;
                fc.AirThen = Air(w);   // 한 틱 늦게 잰다 (예측을 받은 틱)
                break;
            case GuessKind.Step:
            {
                fc.Due = g.Tick + Time.Minutes(g.A * 3 + 10);   // 주컴퓨터가 '시간이 넘었다'로 접는 때 (+ 2분)
                var mind = w.Find<Mind>();
                var step = sm.PlanSteps.FirstOrDefault(s => s.Uid == g.Obj && s.Incident == g.Incident);
                fc.ByPerson = mind != null && step.Robot >= 0 && step.Robot < mind.Agents.Count && mind.Agents[step.Robot].IsPerson;
                break;
            }
            case GuessKind.Supply:
                if (!(g.A < 999) && !(g.B < 999)) { Close(fc, ForecastState.Unmatched, "넉넉하다 — 줄지 않아 맞대지 않는다", now); return; }
                fc.Due = g.Tick + Time.TicksPerDay;
                fc.FoodThen = w.Book.LastStock((int)Res.Food); fc.WaterThen = WaterNow(w); fc.RationThen = g.Verdict;
                break;
            case GuessKind.Shelter:
                fc.Due = g.Tick + (long)Math.Ceiling(g.A / Time.SecondsPerTick) + Time.Minutes(30);
                break;
        }
        Open.Add(fc);
    }

    private void Track(Sim w, ShipMind sm, Field f, Forecast fc, long now)
    {
        if (fc.Interrupt.Length == 0)
            foreach (var e in _events)
                if (e.tick > fc.G.Tick && e.tick <= now && (e.hand || e.room != fc.G.Room)) { fc.Interrupt = "끼어든 일: " + e.text; break; }
        if (fc.G.Kind != GuessKind.Supply && !fc.BlindSeen) fc.BlindSeen = AnyBlind(w, sm);
        switch (fc.G.Kind)
        {
            case GuessKind.Rehearse:
                if (now <= fc.G.Tick + Time.Minutes(1) && fc.G.Obj >= 0 && fc.G.Obj < f.DoorOpen.Length && f.DoorOpen[fc.G.Obj] > 0.5f) fc.Opened = true;
                break;
            case GuessKind.Supply:
                if ((sm.RationFoodOn | sm.RationWaterOn << 1) != fc.RationThen) fc.RationMoved = true;
                break;
            case GuessKind.Shelter when fc.Arrived < 0 && w.Find<Outside>() is { } o:
                if (fc.G.Verdict == 1)
                {
                    double half = Math.Max(w.Layout.W, w.Layout.H) / 2.0;
                    int ci = o.Clouds.FindIndex(c => c.Id == fc.G.Obj);
                    if (ci >= 0) { var c = o.Clouds[ci]; if (Math.Sqrt(c.X * c.X + c.Y * c.Y) - c.Radius - half <= 0) fc.Arrived = now; }
                }
                else if (o.DoseOut > 2 * o.CalmDose + 0.05) fc.Arrived = now;
                break;
        }
        if (now >= fc.Due) Resolve(w, sm, f, fc, now);
    }

    private void Resolve(Sim w, ShipMind sm, Field f, Forecast fc, long now)
    {
        var g = fc.G;
        ForecastState st; string why = "";
        switch (g.Kind)
        {
            case GuessKind.Seal:
            {
                var (dead, down) = Counts(w, true);
                double lost = 0;
                var L = w.Layout;
                for (int r = 0; r < L.Rooms.Count; r++)
                    if (r != g.Room && L.Rooms[r].Cells.Length > 0 && fc.BeforeKPa != null && r < fc.BeforeKPa.Length) lost += Math.Max(0, fc.BeforeKPa[r] - f.Pressure(L.Rooms[r].Cells[0]));
                fc.ActA = dead; fc.ActB = down; fc.ActC = (float)lost;
                fc.Actual = Picture(w);
                OffSum[(int)g.Kind] += Math.Abs(down - g.B); OffN[(int)g.Kind]++;
                bool ok = dead == (int)g.A && Math.Abs(down - g.B) <= 1 && Math.Abs(lost - g.C) <= Math.Max(5, 0.3 * g.C);
                st = ok ? ForecastState.Right : ForecastState.Wrong;
                break;
            }
            case GuessKind.Rehearse:
            {
                if (!fc.Opened) { st = ForecastState.Unmatched; why = "문이 1분 안에 실제로 열리지 않았다 — 맞댈 수 없다"; break; }
                var (dead, down) = Counts(w, false);
                fc.ActA = Math.Max(0, dead + down - fc.DownThen); fc.ActB = (float)Math.Max(0, fc.AirThen - Air(w));
                bool ok = (int)fc.ActA == (int)g.A && Math.Abs(fc.ActB - g.B) <= Math.Max(100, 0.5 * g.B);
                st = ok ? ForecastState.Right : ForecastState.Wrong;
                break;
            }
            case GuessKind.Step:
                st = ForecastState.Unmatched; why = "끝을 알 수 없다 — 계획이 접혔다";
                break;
            case GuessKind.Supply:
            {
                if (fc.RationMoved) { st = ForecastState.Unmatched; why = "그사이 배급이 바뀌었다 — 맞대지 않는다"; break; }
                double food = fc.FoodThen - w.Book.LastStock((int)Res.Food), water = fc.WaterThen - WaterNow(w);
                fc.ActA = (float)food; fc.ActB = (float)water;
                bool any = false, ok = true;
                if (g.A < 999 && g.A > 0 && fc.FoodThen > 0) { any = true; double want = fc.FoodThen / g.A; ok &= Math.Abs(food - want) <= 0.2 * want; }
                if (g.B < 999 && g.B > 0 && fc.WaterThen > 0) { any = true; double want = fc.WaterThen / g.B; ok &= Math.Abs(water - want) <= 0.2 * want; }
                st = !any ? ForecastState.Unmatched : ok ? ForecastState.Right : ForecastState.Wrong;
                if (!any) why = "넉넉하다 — 맞대지 않는다";
                break;
            }
            default:   // 대피 도착
            {
                if (fc.Arrived < 0) { st = ForecastState.Wrong; why = "오지 않았다 (30분 넘게)"; break; }
                fc.ActA = (float)((fc.Arrived - g.Tick) * Time.SecondsPerTick);
                st = Math.Abs(fc.ActA - g.A) <= Math.Max(60, 0.2 * g.A) ? ForecastState.Right : ForecastState.Wrong;
                break;
            }
        }
        if (st == ForecastState.Wrong && why.Length == 0) why = fc.BlindSeen ? "못 본 방이 있었다" : "셈이 어긋났다";
        if (fc.Interrupt.Length > 0 && st is ForecastState.Right or ForecastState.Wrong) { st = ForecastState.Interfered; why = fc.Interrupt; }
        Close(fc, st, why, now);
    }

    private void Close(Forecast fc, ForecastState st, string why, long now)
    {
        fc.State = st; fc.Why = why; fc.Closed = now;
        Open.Remove(fc);
        Closed.Add(fc);
        if (Closed.Count > Keep) Closed.RemoveAt(0);
        int k = (int)fc.G.Kind;
        switch (st)
        {
            case ForecastState.Right: Right[k]++; break;
            case ForecastState.Wrong: Wrong[k]++; WhyWrong[k][why] = WhyWrong[k].GetValueOrDefault(why) + 1; break;
            case ForecastState.Interfered: Interfered[k]++; WhyWrong[k][why.StartsWith("끼어든 일: 함장") ? "함장의 손" : "다른 곳의 새 사고"] = WhyWrong[k].GetValueOrDefault(why.StartsWith("끼어든 일: 함장") ? "함장의 손" : "다른 곳의 새 사고") + 1; break;
            default: Unmatched[k]++; break;
        }
        if (st is ForecastState.Right or ForecastState.Wrong)
        {
            int day = (int)(now / Time.TicksPerDay);
            var (r, n) = Days.GetValueOrDefault(day);
            Days[day] = (r + (st == ForecastState.Right ? 1 : 0), n + 1);
        }
    }

    /// <summary>맞댄 예측 가운데 맞은 것 (끼어든 일 · 맞댈 수 없는 것은 뺀다 · 센서는 따로).</summary>
    public (int right, int total) Summary() { int r = 0, n = 0; for (int k = 0; k < Kinds; k++) { r += Right[k]; n += Right[k] + Wrong[k]; } return (r, n); }
}

/// <summary>주컴퓨터 자세히의 '예측' 작은 탭 (docs/UI2.md §4.5 · docs/OBSERVER.md §8-10 — U8): 지금 하는 예측 · 정확도 · 날마다 · 지난 예측과 실제 (상상 그림 두 판 → 실제와 나란히).</summary>
public sealed partial class LiveShow
{
    private float[]? _roomBoxes;

    /// <summary>방마다 도면 상자 (칸 — x0 · y0 · x1 · y1 · 작은 그림용 · 한 번 셈해 둔다).</summary>
    private float[] RoomBoxes()
    {
        if (_roomBoxes != null) return _roomBoxes;
        var L = Sim.Layout;
        var b = new float[L.Rooms.Count * 4];
        for (int r = 0; r < L.Rooms.Count; r++)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (int c in L.Rooms[r].Cells) { x0 = Math.Min(x0, L.X(c)); y0 = Math.Min(y0, L.Y(c)); x1 = Math.Max(x1, L.X(c) + 1); y1 = Math.Max(y1, L.Y(c) + 1); }
            if (L.Rooms[r].Cells.Length == 0) x0 = y0 = x1 = y1 = 0;
            b[r * 4] = x0; b[r * 4 + 1] = y0; b[r * 4 + 2] = x1; b[r * 4 + 3] = y1;
        }
        return _roomBoxes = b;
    }

    private static PanelBoard? Board(string title, ImagineEnd? e, int level) => e == null || e.RoomKPa.Length == 0 ? null : new PanelBoard(title, e.RoomKPa, e.Cell, e.State, level);

    private static string StateWord(ForecastState s) => s switch { ForecastState.Right => "맞음", ForecastState.Wrong => "틀림", ForecastState.Interfered => "끼어든 일", ForecastState.Unmatched => "맞댈 수 없다", _ => "맞대는 중" };

    /// <summary>머리 줄의 '예측 맞음 9/11' (맞댄 것이 없으면 빈 글).</summary>
    private string ForecastHead() { var (r, n) = Book.Summary(); return n > 0 ? $"예측 맞음 {r}/{n}" : ""; }

    private void PredictRows(Sim w, ShipMind sm, List<PanelRow> rows)
    {
        long now = w.Tick;
        var book = Book;
        if (_replay != null) rows.Add(new PanelRow("산 세계 기준 — 다시보기 중에는 장부가 멈춘다", "되돌아가 보는 동안 생긴 예측은 산 세계로 돌아오면 이어 적는다", false, Key: -1));
        double Min(long ticks) => Math.Max(0, ticks) / (double)Time.Minutes(1);
        // ── 지금 하는 예측 ──
        int head = rows.Count;
        foreach (var th in sm.Thoughts)
            rows.Add(new PanelRow($"{sm.RoomName(th.Room)} — 기다림 · 지금 닫음 두 미래를 5분 앞까지 그려 보는 중", th.Due > now ? "곧 받는다" : "받는 중", true, Key: DK(10 + th.Room)));
        foreach (var fc in book.Open)
        {
            var g = fc.G;
            switch (g.Kind)
            {
                case GuessKind.Seal:
                    rows.Add(new PanelRow($"{sm.RoomName(g.Room)} 봉쇄 상상 — {(g.Verdict == 1 ? "지금 닫음" : "기다림")}을 골랐다",
                        $"5분 뒤 죽음 {g.A:0} · 쓰러짐 {g.B:0} (지금보다 {g.B - fc.DownThen:+0;-0;+0}) · 다른 방 −{g.C:0.0}kPa 예측 · 맞댈 때까지 {Min(fc.Due - now):0}분", true, Key: DK(1000 + fc.Seq),
                        Boards: new[] { Board("기다리면 · 5분 뒤", fc.Shot?.Wait, g.Verdict == 0 ? 1 : 2), Board("지금 닫으면 · 5분 뒤", fc.Shot?.Now, g.Verdict == 1 ? 1 : 2) }.Where(b => b != null).Select(b => b!).ToArray() is { Length: > 0 } bs ? bs : null));
                    break;
                case GuessKind.Rehearse:
                    rows.Add(new PanelRow($"미리 돌려 보기 — {Sim.Layout.Doors[Math.Clamp(g.Obj, 0, Math.Max(0, Sim.Layout.Doors.Count - 1))].Kind switch { _ => "문" }} 연다", $"5분 뒤 쓰러짐 +{g.A:0} · 잃는 공기 {g.B:0}몰 (셈 {g.C:0}) 예측 · {(fc.Opened ? "문이 열렸다" : "문이 열리기를 본다")} · 맞댈 때까지 {Min(fc.Due - now):0}분", false, Key: DK(1000 + fc.Seq)));
                    break;
                case GuessKind.Supply:
                {
                    long next = now + ((37 - now % Time.TicksPerHour) % Time.TicksPerHour + Time.TicksPerHour) % Time.TicksPerHour;
                    rows.Add(new PanelRow($"물자 — 보통으로 쓰면 식량 {(g.A < 999 ? $"{g.A:0}일" : "줄지 않음")} · 물 {(g.B < 999 ? $"{g.B:0}일" : "줄지 않음")}", $"하루 뒤 ({HourMin(fc.Due)}) 실제로 쓴 양과 맞댄다 · 다음 셈 {HourMin(next)}", false, Key: DK(1000 + fc.Seq)));
                    break;
                }
                case GuessKind.Shelter:
                {
                    string what = g.Verdict == 2 ? "양성자 폭풍" : w.Find<Outside>() is { } o && o.Clouds.FindIndex(c => c.Id == g.Obj) is int ci && ci >= 0 ? o.DebrisName(o.Clouds[ci].Kind) : "파편 구름";
                    double left = g.A - (now - g.Tick) * Time.SecondsPerTick;
                    rows.Add(new PanelRow($"대피 — {what} {(left > 0 ? $"{left / 60:0}분 뒤 닿는다" : "닿을 때가 지났다")}", $"대피실 {sm.RoomName(g.Room)} · {(fc.Arrived >= 0 ? "닿았다" : "아직 닿지 않았다")}", true, Key: DK(1000 + fc.Seq)));
                    break;
                }
            }
        }
        foreach (var stp in sm.PlanSteps)
            if (stp.State == 1) rows.Add(new PanelRow($"▶ {sm.StepText(stp)}", $"걸릴 분 {stp.Expect:0.#} · 지난 {Min(now - stp.Start):0.0}분", false, Key: DK(400 + (stp.Uid & 0xffff))));
        if (sm.HelmLine.Split(" · ").FirstOrDefault(x => x.StartsWith("다음 점화")) is { } burn) rows.Add(new PanelRow("항로 — " + burn, "주컴퓨터가 셈한 점화 때", false, Key: DK(1)));
        if (w.Find<Outside>() is { } ou && ou.ZoneAhead(now, 24) is { } zone) rows.Add(new PanelRow("하루 안 구간 — " + zone.Name, "지도로 아는 것", false, Key: DK(2)));
        if (w.Find<Sensors>() is { } sens && sens.Tracks.Count > 0) rows.Add(new PanelRow($"센서에 잡힌 것 {sens.Tracks.Count}", string.Join(" · ", sens.Tracks.GroupBy(t => t.Kind).Select(gr => $"{(gr.Key switch { TrackKind.Rock => "운석", TrackKind.Cloud => "파편 구름", TrackKind.Contact => "다른 배", _ => "별 폭발 조짐" })} {gr.Count()}")), false, Key: DK(3)));
        if (rows.Count == head) rows.Add(new PanelRow("지금 하는 예측이 없다", "봉쇄 상상 · 미리 돌려 보기 · 계획 걸음 · 물자 · 대피가 생기면 여기에", false, Key: DK(4)));
        // ── 정확도 (갈래마다 · 막대 = 맞음 몫) ──
        foreach (var k in new[] { GuessKind.Seal, GuessKind.Rehearse, GuessKind.Step, GuessKind.Supply, GuessKind.Shelter })
        {
            int i = (int)k, r = book.Right[i], n = book.Right[i] + book.Wrong[i];
            var bits = new List<string>();
            if (n == 0) bits.Add("아직 맞댄 것이 없다");
            else if (n < 5) bits.Add($"{n}번뿐 — 아직 알 수 없다");
            if (k == GuessKind.Seal && book.OffN[i] > 0) bits.Add($"쓰러짐 어긋남 평균 {book.OffSum[i] / book.OffN[i]:0.0}");
            if (k == GuessKind.Step && book.OffN[i] > 0 && book.OffSum[i] / book.OffN[i] is double ratio)
                bits.Add(ratio > 1.1 ? $"걸음 시간을 {ratio:0.0}배 짧게 본다" : ratio < 0.9 ? $"걸음 시간을 {1 / ratio:0.0}배 길게 본다" : "걸음 시간을 맞게 본다");
            if (book.Interfered[i] > 0) bits.Add($"끼어들어 뺀 것 {book.Interfered[i]}");
            if (book.Unmatched[i] > 0) bits.Add($"맞댈 수 없는 것 {book.Unmatched[i]}");
            if (book.WhyWrong[i].Count > 0) bits.Add("틀린 까닭: " + string.Join(" · ", book.WhyWrong[i].OrderByDescending(x => x.Value).Take(3).Select(x => $"{x.Key} {x.Value}")));
            rows.Add(new PanelRow($"{ForecastBook.KindName(k)} — 맞음 {r}/{n}", string.Join(" · ", bits), n > 0 && r * 2 < n, Bars: n > 0 ? new[] { Bar("맞음", r / (float)n) } : null, Key: DK(20 + i)));
        }
        rows.Add(new PanelRow($"운석 비키기 — 비껴감 {sm.DodgesAvoided}/{sm.DodgesTried}", sm.DodgesTried == 0 ? "아직 비킨 일이 없다" : $"못 비킴 {sm.DodgesFailed}", sm.DodgesFailed > 0, Bars: sm.DodgesTried > 0 ? new[] { Bar("비껴감", sm.DodgesAvoided / (float)sm.DodgesTried) } : null, Key: DK(27)));
        rows.Add(new PanelRow($"센서 — 선으로 읽은 방 맞음 {book.SensorWireRight}/{book.SensorWire}", $"무선으로 읽은 방 맞음 {book.SensorRadioRight}/{book.SensorRadio} (늦게 읽은 만큼 어긋난다) · 못 본 방 {book.SensorBlind} · 기압 1kPa · 산소 0.5kPa 안이면 맞음", false,
            Bars: book.SensorWire + book.SensorRadio > 0 ? new[] { Bar("선", book.SensorWire > 0 ? book.SensorWireRight / (float)book.SensorWire : 0), Bar("무선", book.SensorRadio > 0 ? book.SensorRadioRight / (float)book.SensorRadio : 0) } : null, Key: DK(28)));
        if (book.Days.Count > 0)
            rows.Add(new PanelRow("날마다 맞음 몫", string.Join(" · ", book.Days.Reverse().Take(7).Reverse().Select(d => $"{d.Key + 1}일 {d.Value.right}/{d.Value.total}")), false,
                Bars: book.Days.Reverse().Take(7).Reverse().Select(d => Bar($"{d.Key + 1}일", d.Value.total > 0 ? d.Value.right / (float)d.Value.total : 0)).ToArray(), Key: DK(29)));
        // ── 지난 예측과 실제 (새것 위) ──
        for (int i = book.Closed.Count - 1, n = 0; i >= 0 && n < 40; i--, n++)
        {
            var fc = book.Closed[i]; var g = fc.G;
            string main, sub;
            PanelBoard[]? boards = null;
            switch (g.Kind)
            {
                case GuessKind.Seal:
                {
                    main = $"{HourMin(g.Tick)} {sm.RoomName(g.Room)} 봉쇄 상상 — {(g.Verdict == 1 ? "지금 닫음" : "기다림")}";
                    sub = $"예측 (5분 뒤): 죽음 {g.A:0} · 쓰러짐 {g.B:0} · 다른 방 −{g.C:0.0}kPa / 실제: 죽음 {fc.ActA:0} · 쓰러짐 {fc.ActB:0} · −{fc.ActC:0.0}kPa → {StateWord(fc.State)}{(fc.Why.Length > 0 ? " (" + fc.Why + ")" : "")}";
                    var other = sm.Imaginations.FirstOrDefault(v => v.Tick == g.Tick && v.Room == g.Room && v.Option != g.Verdict);
                    sub += $"\n안 고른 미래 ({(g.Verdict == 1 ? "기다림" : "지금 닫음")}): {(other.Tick == g.Tick && other.Room == g.Room ? $"죽음 {other.Dead} · 쓰러짐 {other.Down} — " : "")}일어나지 않아 맞댈 수 없다";
                    boards = new[] { Board("기다리면 · 5분 뒤", fc.Shot?.Wait, g.Verdict == 0 ? 1 : 2), Board("지금 닫으면 · 5분 뒤", fc.Shot?.Now, g.Verdict == 1 ? 1 : 2), Board("실제 · 5분 뒤", fc.Actual, 3) }.Where(b => b != null).Select(b => b!).ToArray();
                    if (boards.Length == 0) boards = null;
                    break;
                }
                case GuessKind.Rehearse:
                    main = $"{HourMin(g.Tick)} 미리 돌려 보기 — {(g.Verdict == 1 ? "연다" : "하지 않는다")}";
                    sub = $"예측 (5분 뒤): 쓰러짐 +{g.A:0} · 잃는 공기 {g.B:0}몰" + (fc.State is ForecastState.Right or ForecastState.Wrong or ForecastState.Interfered ? $" / 실제: 쓰러짐 +{fc.ActA:0} · 잃은 공기 {fc.ActB:0}몰" : "") + $" → {StateWord(fc.State)}{(fc.Why.Length > 0 ? " (" + fc.Why + ")" : "")}";
                    break;
                case GuessKind.Step:
                    main = $"{HourMin(g.Tick)} 걸음 시간 — 예측 {g.A:0.#}분";
                    sub = (fc.State is ForecastState.Right or ForecastState.Wrong or ForecastState.Interfered ? $"실제 {fc.ActA:0.#}분" : "") + $" → {StateWord(fc.State)}{(fc.Why.Length > 0 ? " (" + fc.Why + ")" : "")}";
                    break;
                case GuessKind.Supply:
                    main = $"{HourMin(g.Tick)} 물자 날 — 식량 {(g.A < 999 ? $"{g.A:0}일" : "줄지 않음")} · 물 {(g.B < 999 ? $"{g.B:0}일" : "줄지 않음")}";
                    sub = (fc.State is ForecastState.Right or ForecastState.Wrong or ForecastState.Interfered ? $"하루 쓴 양: 식량 {fc.ActA:0}몰 (예측 {(g.A < 999 && g.A > 0 ? fc.FoodThen / g.A : 0):0}) · 물 {fc.ActB:0}몰 (예측 {(g.B < 999 && g.B > 0 ? fc.WaterThen / g.B : 0):0})" : "") + $" → {StateWord(fc.State)}{(fc.Why.Length > 0 ? " (" + fc.Why + ")" : "")}";
                    break;
                default:
                    main = $"{HourMin(g.Tick)} 대피 도착 — {g.A / 60:0}분 뒤 예측";
                    sub = (fc.Arrived >= 0 ? $"실제 {fc.ActA / 60:0.#}분 뒤 닿았다" : "") + $" → {StateWord(fc.State)}{(fc.Why.Length > 0 ? " (" + fc.Why + ")" : "")}";
                    break;
            }
            rows.Add(new PanelRow(main, sub.TrimStart(' ', '→').Length == sub.Length ? sub : sub.TrimStart(), fc.State == ForecastState.Wrong, Key: DK(1000 + fc.Seq), Boards: boards));
        }
    }
}
