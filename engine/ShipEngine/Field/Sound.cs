namespace ShipEngine;

/// <summary>소리의 종류 — 들은 사람이 무엇으로 알아듣나 (⑧ 마음의 감각).</summary>
public enum SoundKind : byte
{
    Bang,       // 쾅 (충돌 · 폭발 · 문이 부서짐)
    Hiss,       // 쉬익 (새는 공기 · 증기)
    Roar,       // 우르릉 (불길)
    Crack,      // 쩍 (벽에 금이 감)
    Alarm,      // 경보
    Voice,      // 말 · 외침
    Scream,     // 비명
    Machine,    // 기계 소리 (덜컹 · 끼익)
    Step,       // 발소리
    Thud,       // 쿵 (넘어짐 · 떨어짐)
    Creak,      // 끼익 (압력을 버티는 문 · 벽)
}

/// <summary>낸 소리 하나 (보기용 기록).</summary>
public struct Emission
{
    public long Tick;
    public SoundKind Kind;
    public int Cell, Source, Actor, Cause;
    public float Db;           // 1m에서의 크기
}

/// <summary>들린 소리 하나.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)] // 찍기는 바이트를 그대로 쓴다 — 정렬용 빈 바이트(쓰레기 값)가 없게
public struct Heard
{
    public int Event;
    public SoundKind Kind;
    public int Source;     // 난 칸
    public float Db;       // 들은 크기 (dB)
    public int Cause;
    public long Tick;
    public int Actor;      // 낸 사람 (-1)
}

/// <summary>
/// ③ 장 — 소리. 일이 일어나는 순간 그 칸에서 퍼진다 (사건마다 한 번 · 다익스트라로 줄어드는 길):
/// 공기 칸마다 거리에 따라 줄고(구면 확산 + 흡수), 닫힌 문 −25dB, 벽 −45dB(구조로 전해짐), 엷은 공기에서는 작게, 진공에서는 전해지지 않는다.
/// 마음은 자기 칸에서 들린 것만 안다 (<see cref="At"/>). 효과음도 여기서 나온 크기를 쓴다.
/// </summary>
public sealed class Sound : IEngine
{
    public string Name => "소리";
    public Phase Phase => Phase.Field;
    public int Every => 1;
    public int Offset => 0;

    public const float Floor = 25;     // 이보다 작으면 안 들린다 (dB)
    public const int KeepTicks = 2;    // 들린 소리가 남아 있는 틱 (감각이 읽을 시간)

    private readonly Field F;
    private readonly Layout L;
    /// <summary>칸 → 이번에 들린 소리들 (최근 KeepTicks 틱).</summary>
    private readonly Dictionary<int, List<Heard>> _heard = new();
    private readonly List<(long tick, int cell)> _order = new();
    private int _nextEvent;
    public long Emitted;
    /// <summary>지금까지 낸 소리 사건의 마지막 번호 (마음이 어디까지 들었는지 적는다).</summary>
    public int LastEvent => _nextEvent;

    public Sound(Field f) { F = f; L = f.L; }

    // ── 보기용 기록: 낸 소리 사건 (관전 화면의 소리 엔진이 읽는다 — 세계는 읽지 않고 찍지도 않는다 · 되살린 세계는 빈 기록에서) ──
    public const int LogSize = 1024;
    private readonly Emission[] _log = new Emission[LogSize];
    public long Logged { get; private set; }
    /// <summary>번호 from(누계) 이후 낸 소리들 (기록에 남은 것만).</summary>
    public IEnumerable<Emission> LogSince(long from)
    {
        for (long i = Math.Max(from, Logged - LogSize); i < Logged; i++) yield return _log[i % LogSize];
    }

    /// <summary>칸 cell에서 크기 db(1m에서의 dB)의 소리가 난다 (source: 들은 이가 아는 소리의 자리 — 없으면 cell).</summary>
    public int Emit(Sim sim, int cell, float db, SoundKind kind, int cause = 0, int actor = -1, int source = -1)
    {
        int src = source >= 0 ? source : cell;
        int ev = ++_nextEvent;
        Emitted++;
        _log[Logged % LogSize] = new Emission { Tick = sim.Tick, Kind = kind, Cell = cell, Source = src, Db = db, Actor = actor, Cause = cause };
        Logged++;
        if (Thin(cell) <= 0) return ev; // 진공: 아무도 못 듣는다
        var best = new Dictionary<int, float> { [cell] = db };
        var pq = new PriorityQueue<int, float>();
        pq.Enqueue(cell, -db);
        Span<int> nb = stackalloc int[4];
        while (pq.TryDequeue(out int c, out float negLevel))
        {
            float lv = -negLevel;
            if (best.TryGetValue(c, out float b) && b > lv + 1e-4f) continue;
            Add(sim, c, new Heard { Event = ev, Kind = kind, Source = src, Db = lv, Cause = cause, Tick = sim.Tick, Actor = actor });
            L.Neighbors(c, nb);
            foreach (int j in nb)
            {
                if (j < 0 || L.Kind[j] == CellKind.Outside) continue;
                float loss = StepLoss(c, j, cell);
                float next = lv - loss;
                if (next < Floor) continue;
                if (best.TryGetValue(j, out float old) && old >= next) continue;
                best[j] = next;
                pq.Enqueue(j, -next);
            }
        }
        if (db >= 100) sim.Facts.Emit(sim.Tick, FactKind.LoudSound, cell, db, cause, actor, (int)kind);
        return ev;
    }

    /// <summary>한 칸 건널 때 주는 크기: 거리(구면 확산을 칸마다 근사) · 흡수 · 문 · 벽 · 엷은 공기.</summary>
    private float StepLoss(int from, int to, int source)
    {
        float loss = 0.6f; // 흡수 · 산란
        int dx = L.X(to) - L.X(source), dy = L.Y(to) - L.Y(source);
        double d1 = Math.Sqrt(dx * dx + dy * dy), d0 = Math.Max(1, d1 - 1);
        loss += (float)(20 * Math.Log10(Math.Max(1, d1) / d0)) * 0.7f;
        var k = L.Kind[to];
        if (k == CellKind.Wall) loss += F.Hole[to] >= 2 ? 0 : F.Hole[to] > 0 ? 15 : 45;
        else if (k == CellKind.Door && !F.DoorPassable(L.DoorAt[to])) loss += F.DoorOpen[L.DoorAt[to]] > 0 ? 8 : 25;
        if (L.IsSpace(to))
        {
            double th = Thin(to);
            if (th <= 0) return 999;
            loss += (float)(-20 * Math.Log10(th) * 0.5);
        }
        return Math.Max(0.3f, loss); // 짙은 대기에서도 소리는 칸을 건널수록 준다
    }

    /// <summary>공기가 엷은 정도 (1: 1기압 · 0: 진공).</summary>
    private double Thin(int c)
    {
        if (!L.IsSpace(c) && L.Kind[c] != CellKind.Wall) return 0;
        if (L.Kind[c] == CellKind.Wall) return 1;
        if (L.Kind[c] == CellKind.Door && !F.DoorPassable(L.DoorAt[c])) return 1; // 닫힌 문은 단단한 판 (문 칸에 기체가 없어도 진공이 아니다 — 25dB만 줄인다)
        double p = F.Pressure(c);
        return p < 1 ? 0 : Math.Min(1.5, p / 101.3);
    }

    private void Add(Sim sim, int c, Heard h)
    {
        if (!_heard.TryGetValue(c, out var list)) { _heard[c] = list = new List<Heard>(2); _order.Add((sim.Tick, c)); }
        list.Add(h);
    }

    /// <summary>이 칸에서 들린 소리들 (최근 틱).</summary>
    public IReadOnlyList<Heard> At(int cell) => _heard.TryGetValue(cell, out var l) ? l : Array.Empty<Heard>();

    /// <summary>이 칸에서 들린 가장 큰 소리 (dB, 없으면 0).</summary>
    public float Loudest(int cell)
    {
        float m = 0;
        foreach (var h in At(cell)) m = Math.Max(m, h.Db);
        return m;
    }

    public void Step(Sim sim, double dt)
    {
        // 오래된 것을 지운다
        int k = 0;
        while (k < _order.Count && _order[k].tick <= sim.Tick - KeepTicks)
        {
            int c = _order[k].cell;
            if (_heard.TryGetValue(c, out var list))
            {
                list.RemoveAll(h => h.Tick <= sim.Tick - KeepTicks);
                if (list.Count == 0) _heard.Remove(c);
            }
            k++;
        }
        if (k > 0) _order.RemoveRange(0, k);
        if (sim.Tick % HissEvery == 0) Hiss(sim);
    }

    public const int HissEvery = 5;

    /// <summary>쉬익: 기압 차가 5kPa 넘는 틈마다 높은 쪽 칸에서 (흐름이 클수록 크게 — 문틈 약 65dB · 큰 구멍 약 95dB).</summary>
    private void Hiss(Sim sim)
    {
        foreach (var k in F.Links)
        {
            double pa = F.Pressure(k.A), pb = k.B >= 0 ? F.Pressure(k.B) : F.Outside.KPa;
            if (Math.Abs(pa - pb) < 5) continue;
            int at = pa >= pb ? k.A : k.B;
            if (at < 0) continue;
            float db = (float)Math.Min(99, 60 + 10 * Math.Log10(Math.Abs(k.Flow) + 0.01));
            Emit(sim, at, db, SoundKind.Hiss, 0, -1, k.Via >= 0 ? k.Via : at); // 들린 자리 = 틈 (문 · 벽 칸)
        }
    }

    public void Save(StateWriter w)
    {
        w.I32(_nextEvent); w.I64(Emitted);
        var cells = _heard.Keys.OrderBy(c => c).ToList();
        w.I32(cells.Count);
        foreach (int c in cells) { w.I32(c); w.List(_heard[c]); }
        w.I32(_order.Count);
        foreach (var (t, c) in _order) { w.I64(t); w.I32(c); }
    }

    public void Load(StateReader r)
    {
        _nextEvent = r.I32(); Emitted = r.I64();
        _heard.Clear(); _order.Clear();
        int n = r.I32();
        for (int i = 0; i < n; i++) { int c = r.I32(); _heard[c] = r.List<Heard>(); }
        int m = r.I32();
        for (int i = 0; i < m; i++) { long t = r.I64(); int c = r.I32(); _order.Add((t, c)); }
    }
}
