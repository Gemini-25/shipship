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
    // ── 생활 소리 (소리 단계 S2 — 판단 엔진이 정한 행동 · 몸이 낸다) ──
    Clink,      // 딸그락 (식기 · 컵 · 냄비)
    Tool,       // 공구 (렌치 딸깍딸깍 · 드라이버)
    Weld,       // 용접 · 봉합 (지지직)
    Hammer,     // 망치 (탕탕)
    Snore,      // 코골이 (몸 — ⑦)
    SleepTalk,  // 잠꼬대
    Cough,      // 기침 (몸 — ⑦)
    Laugh,      // 웃음
    Hum,        // 흥얼거림
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

/// <summary>이어지는 소리 하나 (돌아가는 장치 — 위 층이 요청한다): 자리 · 1m에서의 크기 (0이면 멈춤).</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct Drone
{
    public int Cell;
    public float Db;
}

/// <summary>
/// ③ 장 — 소리. 일이 일어나는 순간 그 칸에서 퍼진다 (사건마다 한 번 · 다익스트라로 줄어드는 길):
/// 공기 칸마다 거리에 따라 줄고(구면 확산 + 흡수), 닫힌 문 −25dB, 벽 −45dB(구조로 전해짐), 엷은 공기에서는 작게, 진공에서는 전해지지 않는다.
/// 마음은 자기 칸에서 들린 것만 안다 (<see cref="At"/>). 효과음도 여기서 나온 크기를 쓴다.
/// <para><b>바탕 소음</b> (바퀴 2): 위 층(⑥ 망)이 돌아가는 장치마다 '이어지는 소리'를 요청하면(<see cref="SetDrone"/>) 1분마다 방마다 퍼진 소리(사빈 확산음장) +
/// 옆 방에서 문 · 벽을 넘어온 몫(투과 손실 · 넓이 비) + 공조 바람 바닥을 셈한다 — 칸의 바탕 소음 = 그 방의 퍼진 소리 + 4m 안 장치에서 바로 온 소리 (<see cref="Ambient"/>).
/// 바탕 소음은 다른 소리를 가린다 (마음이 읽는다 — <see cref="Mask"/>).</para>
/// </summary>
public sealed class Sound : IEngine
{
    /// <summary>몸이 낸 소리(코골이 · 기침 — ⑦ 생명은 사람 번호를 모른다)의 '낸 이' 자리: 몸 번호를 −1000 아래로 적는다 (마음이 제 몸 소리를 가려낸다 ·
    /// −2는 주컴퓨터 — 처음엔 −2부터 적어 0번 몸이 주컴퓨터 경보를 제 몸 소리로 여겼다 · 규칙 시험 '일산화탄소 경보').</summary>
    public const int BodyBase = -1000;
    public static int BodyActor(int body) => BodyBase - body;
    public static int BodyOf(int actor) => actor <= BodyBase ? BodyBase - actor : -1;

    public string Name => "소리";
    public Phase Phase => Phase.Field;
    public int Every => 1;
    public int Offset => 0;

    public const float Floor = 25;     // 이보다 작으면 안 들린다 (dB)
    public const int KeepTicks = 2;    // 들린 소리가 남아 있는 틱 (감각이 읽을 시간)

    // ── 바탕 소음 (바퀴 2) ──
    public const int AmbientEvery = 25;          // 1분마다 셈한다
    public const double Absorb = 0.25;           // 방 겉면의 평균 흡음률 (금속 벽 + 선반 · 천 · 사람 — 어림)
    public const double VentDb = 30;             // 공조 바람 · 먼 기계의 바닥 소음 (공기가 있는 방 — WHO 잠자리 30dBA)
    public const float MaskFrom = 40;            // 바탕 소음이 이보다 크면 그 넘는 만큼 다른 소리를 가린다
    public const double DirectM = 4;             // 장치에서 바로 온 소리를 더하는 거리 (m)
    public const double DoorM2 = 2.5, WallM2 = 2.5; // 문 · 벽 칸 하나의 넓이 (1m × 천장 2.5m)

    private readonly Field F;
    private readonly Layout L;
    /// <summary>칸 → 이번에 들린 소리들 (최근 KeepTicks 틱).</summary>
    private readonly Dictionary<int, List<Heard>> _heard = new();
    private readonly List<(long tick, int cell)> _order = new();
    private int _nextEvent;
    public long Emitted;
    /// <summary>지금까지 낸 소리 사건의 마지막 번호 (마음이 어디까지 들었는지 적는다).</summary>
    public int LastEvent => _nextEvent;

    /// <summary>이어지는 소리 (열쇠 = 요청한 쪽의 번호 — 장치 번호) · 찍는다.</summary>
    private readonly List<Drone> _drones = new();
    /// <summary>방마다 퍼진 소리의 세기 (선형 — 10^(dB/10)) · 1분마다 셈한다 · 찍는다 (그 사이 문이 열려도 다음 셈까지 그대로).</summary>
    private double[] _roomE;
    /// <summary>방마다 그 방 이어지는 소리의 열쇠들 (_drones에서 다시 지을 수 있다 — 찍지 않는다).</summary>
    private List<int>[] _roomDrones;
    private readonly double[] _roomA, _room4R;               // 방의 흡음 넓이 A = Sα · 4/R (R = A/(1−α))
    private readonly (int cell, int a, int b)[] _doorPairs;  // 문 칸 · 양쪽 방
    private readonly (int cell, int a, int b)[] _wallPairs;  // 두 방 사이의 벽 칸

    public Sound(Field f)
    {
        F = f; L = f.L;
        int nr = L.Rooms.Count;
        _roomE = new double[nr]; _roomA = new double[nr]; _room4R = new double[nr];
        _peakDb = new float[L.N]; _peakTick = new long[L.N]; _peakCause = new int[L.N];
        _roomDrones = Enumerable.Range(0, nr).Select(_ => new List<int>()).ToArray();
        for (int r = 0; r < nr; r++)
        {
            int n = Math.Max(1, L.Rooms[r].Cells.Length);
            double S = 2.0 * n + 4 * Math.Sqrt(n) * 2.5;            // 바닥 · 천장 + 둘레 벽 (정사각으로 어림)
            _roomA[r] = S * Absorb;
            _room4R[r] = 4 / (_roomA[r] / (1 - Absorb));
        }
        var doors = new List<(int, int, int)>(); var walls = new List<(int, int, int)>();
        Span<int> nb = stackalloc int[4];
        for (int c = 0; c < L.N; c++)
        {
            if (L.Kind[c] is not (CellKind.Wall or CellKind.Door)) continue;
            L.Neighbors(c, nb);
            int a = -1, b = -1;
            foreach (int j in nb)
            {
                if (j < 0 || L.Room[j] < 0) continue;
                if (a < 0) a = L.Room[j]; else if (L.Room[j] != a && b < 0) b = L.Room[j];
            }
            if (a < 0 || b < 0) continue;
            if (L.Kind[c] == CellKind.Door) doors.Add((c, Math.Min(a, b), Math.Max(a, b))); else walls.Add((c, Math.Min(a, b), Math.Max(a, b)));
        }
        _doorPairs = doors.ToArray(); _wallPairs = walls.ToArray();
    }

    // ── 보기용 기록: 낸 소리 사건 (관전 화면의 소리 엔진이 읽는다 — 세계는 읽지 않고 찍지도 않는다 · 되살린 세계는 빈 기록에서) ──
    public const int LogSize = 1024;
    private readonly Emission[] _log = new Emission[LogSize];
    public long Logged { get; private set; }
    /// <summary>번호 from(누계) 이후 낸 소리들 (기록에 남은 것만).</summary>
    public IEnumerable<Emission> LogSince(long from)
    {
        for (long i = Math.Max(from, Logged - LogSize); i < Logged; i++) yield return _log[i % LogSize];
    }

    // ── 귀에 닿은 가장 큰 소리 (칸마다 · 소리 단계 S3 — ⑦ 몸이 청력 피해를 셈한다) ──
    /// <summary>창: 이보다 오래된 가장 큰 소리는 새 소리에 자리를 내준다 (틱 — 생명이 5틱마다 읽는다).</summary>
    public const int PeakWindow = 5;
    private float[] _peakDb;
    private long[] _peakTick;
    private int[] _peakCause;
    /// <summary>틱 since부터 이 칸에 닿은 가장 큰 소리 (dB · 원인 마디 — 없으면 0).</summary>
    public (float db, int cause) PeakSince(int cell, long since) =>
        (uint)cell < (uint)_peakDb.Length && _peakTick[cell] >= since && _peakTick[cell] > 0 ? (_peakDb[cell], _peakCause[cell]) : (0, 0);

    private float[] _best = Array.Empty<float>();
    private int[] _bestStamp = Array.Empty<int>();
    private int _bestNow;
    private readonly PriorityQueue<int, float> _pq = new();

    /// <summary>칸 cell에서 크기 db(1m에서의 dB)의 소리가 난다 (source: 들은 이가 아는 소리의 자리 — 없으면 cell).</summary>
    public int Emit(Sim sim, int cell, float db, SoundKind kind, int cause = 0, int actor = -1, int source = -1)
    {
        int src = source >= 0 ? source : cell;
        int ev = ++_nextEvent;
        Emitted++;
        _log[Logged % LogSize] = new Emission { Tick = sim.Tick, Kind = kind, Cell = cell, Source = src, Db = db, Actor = actor, Cause = cause };
        Logged++;
        if (Thin(cell) <= 0) return ev; // 진공: 아무도 못 듣는다
        // 다익스트라 (칸마다 가장 큰 크기 — 다시 쓰는 배열: 생활 소리로 낸 소리가 많아져 사전을 새로 만들지 않는다 · 결과는 같다)
        if (_best.Length != L.N) { _best = new float[L.N]; _bestStamp = new int[L.N]; }
        int stamp = ++_bestNow;
        var pq = _pq; pq.Clear();
        _best[cell] = db; _bestStamp[cell] = stamp;
        pq.Enqueue(cell, -db);
        Span<int> nb = stackalloc int[4];
        while (pq.TryDequeue(out int c, out float negLevel))
        {
            float lv = -negLevel;
            if (_bestStamp[c] == stamp && _best[c] > lv + 1e-4f) continue;
            Add(sim, c, new Heard { Event = ev, Kind = kind, Source = src, Db = lv, Cause = cause, Tick = sim.Tick, Actor = actor });
            L.Neighbors(c, nb);
            foreach (int j in nb)
            {
                if (j < 0 || F.IsOut(j)) continue;   // 떨어져 나간 자리는 우주다 — 공기도 판도 없다
                float loss = StepLoss(c, j, cell);
                float next = lv - loss;
                if (next < Floor) continue;
                if (_bestStamp[j] == stamp && _best[j] >= next) continue;
                _best[j] = next; _bestStamp[j] = stamp;
                pq.Enqueue(j, -next);
            }
        }
        if (db >= 100 && kind is not (SoundKind.Hiss or SoundKind.Roar)) sim.Facts.Emit(sim.Tick, FactKind.LoudSound, cell, db, cause, actor, (int)kind); // 이어 나는 쉬익 · 우르릉은 5틱마다 다시 낸다 — 큰 소리 사실은 한 번 터지는 것만
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
        if (h.Db > _peakDb[c] || _peakTick[c] < sim.Tick - PeakWindow) { _peakDb[c] = h.Db; _peakTick[c] = sim.Tick; _peakCause[c] = h.Cause; }
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
        if (sim.Tick % AmbientEvery == 0) ComputeAmbient();
    }

    public const int HissEvery = 5;
    /// <summary>이보다 작은 쉬익은 내지 않는다 (1m에서 dB — 조용한 방의 바닥 소음 위로 겨우 들리는 크기).</summary>
    public const float HissMinDb = 40;

    /// <summary>
    /// 틈으로 뿜는 바람의 크기 (1m에서 dB) — 분출 속도 v(막힘 흐름이면 소리 빠르기 310m/s · 아니면 √(2Δp/ρ))와 넓이에서:
    /// 라이트힐 제트 소음(소리 힘 ∝ ρ·v⁸·d²) — 65 + 10·log(넓이/5cm²) + 80·log(v/129m/s) + 10·log(ρ/1.2)
    /// (5cm² 문틈 10kPa 65dB · 진공으로 96dB · 200cm² 구멍 진공으로 112dB · 4cm² 2kPa 36dB). 화면의 분출 소리도 같은 식.
    /// </summary>
    public static float JetDb(double area, double hiKPa, double loKPa)
    {
        if (area <= 0 || hiKPa <= loKPa || hiKPa < 1) return 0;
        double rho = hiKPa * 1000 * 0.029 / (8.314 * 293);
        double v = loKPa / hiKPa < 0.528 ? 310 : Math.Sqrt(2 * (hiKPa - loKPa) * 1000 / rho);
        return (float)Math.Clamp(65 + 10 * Math.Log10(area / 5e-4) + 80 * Math.Log10(v / 129) + 10 * Math.Log10(rho / 1.2), 0, 120);
    }

    /// <summary>쉬익: 분출 소리가 40dB 넘는 틈마다 높은 쪽 칸에서 (분출 물리 — <see cref="JetDb"/>).</summary>
    private void Hiss(Sim sim)
    {
        foreach (var k in F.Links)
        {
            double pa = F.ZoneKPa(k.RA), pb = k.RB >= 0 ? F.ZoneKPa(k.RB) : F.Outside.KPa; // 구역 기압 (틈 곁 칸 하나는 흐름에 잠깐 튄다)
            double hi = Math.Max(pa, pb), lo = Math.Min(pa, pb);
            if (hi - lo < 0.5) continue;
            float db = JetDb(k.Area, hi, lo);
            if (db < HissMinDb) continue;
            int at = pa >= pb ? k.A : k.B;
            if (at < 0) continue;
            Emit(sim, at, db, SoundKind.Hiss, 0, -1, k.Via >= 0 ? k.Via : at); // 들린 자리 = 틈 (문 · 벽 칸)
        }
    }

    // ───────────────────────── 바탕 소음 (바퀴 2) ─────────────────────────

    /// <summary>이어지는 소리를 요청한다 (위 층 — 돌아가는 장치): 열쇠 key(장치 번호)의 자리 · 1m에서의 크기 (0이면 멈춤). 다음 셈(1분마다)에 바탕 소음이 된다.</summary>
    public void SetDrone(int key, int cell, float db)
    {
        if (key < 0) return;
        while (_drones.Count <= key) _drones.Add(new Drone { Cell = -1 });
        _drones[key] = new Drone { Cell = cell, Db = Math.Max(0, db) };
    }

    /// <summary>요청된 이어지는 소리의 크기 (1m · dB · 없으면 0).</summary>
    public float DroneDb(int key) => (uint)key < (uint)_drones.Count ? _drones[key].Db : 0;

    private static double E(double db) => Math.Pow(10, db / 10);
    private static float Db(double e) => e > 1 ? (float)(10 * Math.Log10(e)) : 0;

    /// <summary>방 공기의 짙기 (1: 1기압 · 0: 진공) — 첫 칸의 기압 (구역은 기압이 고르다).</summary>
    private double RoomThin(int r)
    {
        var cells = L.Rooms[r].Cells;
        if (cells.Length == 0) return 0;
        double p = F.Pressure(cells[0]);
        return p < 1 ? 0 : Math.Min(1.5, p / 101.3);
    }

    /// <summary>
    /// 방마다 퍼진 소리 (1분마다): 그 방 장치의 소리 힘 Lw = Lp₁ₘ + 11dB × 4/R (사빈 확산음장) + 공조 바닥 30dB → 엷은 공기는 20·log 짙기 ·
    /// 옆 방에서 넘어온 몫 = 그 방 세기 × 투과율(닫힌 문 −25 · 조금 열린 문 −8 · 열린 문 0 · 벽 −45 · 금 −15 · 뚫림 0dB) × 넓이 / 받는 방의 흡음 넓이.
    /// </summary>
    private void ComputeAmbient()
    {
        int nr = L.Rooms.Count;
        foreach (var l in _roomDrones) l.Clear();
        var own = new double[nr];
        for (int key = 0; key < _drones.Count; key++)
        {
            var d = _drones[key];
            if (d.Cell < 0) continue;
            int r = RoomNear(d.Cell);
            if (r < 0) continue;
            _roomDrones[r].Add(key);                         // 방 목록은 자리로만 (크기와 상관없이 — 되살린 세계에서도 같은 목록)
            if (d.Db > 0) own[r] += E(d.Db + 11) * _room4R[r];
        }
        var thin = new double[nr];
        for (int r = 0; r < nr; r++)
        {
            thin[r] = RoomThin(r);
            own[r] = thin[r] > 0 ? (own[r] + E(VentDb)) * thin[r] * thin[r] : 0;
        }
        for (int r = 0; r < nr; r++) _roomE[r] = own[r];
        foreach (var (c, a, b) in _doorPairs)
        {
            int d = L.DoorAt[c];
            double tau = d < 0 ? 0 : F.DoorPassable(d) ? 1 : F.DoorOpen[d] > 0 ? 0.158 : 0.00316;
            Pass(a, b, tau * DoorM2);
        }
        foreach (var (c, a, b) in _wallPairs)
        {
            double tau = F.Hole[c] >= 2 ? 1 : F.Hole[c] > 0 ? 0.0316 : 3.16e-5;
            Pass(a, b, tau * WallM2);
        }
        void Pass(int a, int b, double tauS)
        {
            if (thin[a] > 0 && thin[b] > 0) { _roomE[a] += own[b] * tauS / _roomA[a] * thin[a] * thin[a]; _roomE[b] += own[a] * tauS / _roomA[b] * thin[b] * thin[b]; } // 엷은 공기 쪽은 벽이 덜 울린다
        }
    }

    /// <summary>칸의 방 (문 · 벽 칸이면 곁의 방 중 하나 — 번호가 작은 쪽).</summary>
    private int RoomNear(int c)
    {
        if (c < 0) return -1;
        if (L.Room[c] >= 0) return L.Room[c];
        Span<int> nb = stackalloc int[4];
        L.Neighbors(c, nb);
        int best = -1;
        foreach (int j in nb) if (j >= 0 && L.Room[j] >= 0 && (best < 0 || L.Room[j] < best)) best = L.Room[j];
        return best;
    }

    /// <summary>방의 바탕 소음 (퍼진 소리 · dB — 공기가 없으면 0).</summary>
    public float RoomAmbient(int room) => (uint)room < (uint)_roomE.Length ? Db(_roomE[room]) : 0;

    /// <summary>
    /// 칸의 바탕 소음 (dB): 그 방에 퍼진 소리 + 4m 안 장치에서 바로 온 소리 (1/r² · 1m에서의 크기). 문 칸은 곁의 방 중 시끄러운 쪽.
    /// </summary>
    public float Ambient(int cell)
    {
        if ((uint)cell >= (uint)L.N) return 0;
        int r = L.Room[cell];
        if (r < 0)
        {
            if (!L.IsSpace(cell)) return 0;
            Span<int> nb = stackalloc int[4];
            L.Neighbors(cell, nb);
            float best = 0;
            foreach (int j in nb) if (j >= 0 && L.Room[j] >= 0) best = Math.Max(best, Db(_roomE[L.Room[j]]));
            return best;
        }
        double e = _roomE[r];
        if (e <= 0) return 0;
        double th = Math.Min(1, F.Pressure(cell) / 101.3);
        int x = L.X(cell), y = L.Y(cell);
        foreach (int key in _roomDrones[r])
        {
            var d = _drones[key];
            if (d.Db <= 0) continue;
            double dx = L.X(d.Cell) - x, dy = L.Y(d.Cell) - y, d2 = dx * dx + dy * dy;
            if (d2 > DirectM * DirectM) continue;
            e += E(d.Db) / Math.Max(1, d2) * th * th;
        }
        return Db(e);
    }

    /// <summary>가림 (dB): 바탕 소음이 40dB를 넘는 몫 — 이 칸에서 들린 다른 소리는 그만큼 작게 들린다.</summary>
    public float Mask(int cell) => Math.Max(0, Ambient(cell) - MaskFrom);

    public void Save(StateWriter w)
    {
        w.I32(_nextEvent); w.I64(Emitted);
        var cells = _heard.Keys.OrderBy(c => c).ToList();
        w.I32(cells.Count);
        foreach (int c in cells) { w.I32(c); w.List(_heard[c]); }
        w.I32(_order.Count);
        foreach (var (t, c) in _order) { w.I64(t); w.I32(c); }
        w.Tag("바탕 소음"); w.List(_drones); w.Arr(_roomE);
        w.Tag("귀에 닿은 가장 큰 소리"); w.Arr(_peakDb); w.Arr(_peakTick); w.Arr(_peakCause);
    }

    public void Load(StateReader r)
    {
        _nextEvent = r.I32(); Emitted = r.I64();
        _heard.Clear(); _order.Clear();
        int n = r.I32();
        for (int i = 0; i < n; i++) { int c = r.I32(); _heard[c] = r.List<Heard>(); }
        int m = r.I32();
        for (int i = 0; i < m; i++) { long t = r.I64(); int c = r.I32(); _order.Add((t, c)); }
        r.Tag("바탕 소음");
        _drones.Clear(); _drones.AddRange(r.List<Drone>());
        r.ArrInto(_roomE);
        r.Tag("귀에 닿은 가장 큰 소리"); r.ArrInto(_peakDb); r.ArrInto(_peakTick); r.ArrInto(_peakCause);
        foreach (var l in _roomDrones) l.Clear();
        for (int key = 0; key < _drones.Count; key++)
            if (_drones[key].Cell >= 0 && RoomNear(_drones[key].Cell) is int rr && rr >= 0) _roomDrones[rr].Add(key);
    }
}
