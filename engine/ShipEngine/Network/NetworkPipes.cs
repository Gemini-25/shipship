namespace ShipEngine;

/// <summary>
/// ⑥ 바퀴 3 (라) 새는 관 — 녹 · 밸브 · 찾기 (docs/ENGINE.md ⑥ '바퀴 3 설계').
/// <para>
/// 녹슨 관: 냉각관 · 물관 칸은 젖은 바닥 · 습한 공기에서 녹슨다 (마른 칸 평균 30만 시간 · 젖으면 20배 · 습도 70% 넘으면 3배 — 칸마다 숨은 한계 와이블) →
/// 한계를 넘으면 바늘구멍 (틈 0.05) — 자리는 아무도 모른다 (그 칸에 가서 젖은 바닥 · 쉬익 · 냄새로 알아야 한다).
/// </para>
/// <para>
/// 밸브: 방마다 관 갈래마다 밸브 (그 방 관 칸의 들머리) — 잠그면 그 방과 그 너머(물줄기 아래쪽)로 흐르지 않고 · 거기 샘은 압력이 빠져 스미는 만큼만.
/// 주컴퓨터는 관망을 채우는 흐름(냉매 · 물탱크 계량)으로 모르는 샘의 크기를 읽는다 — 밸브를 잠가 멎는 쪽으로 좁힌다.
/// </para>
/// </summary>
public sealed partial class Network
{
    public const double RustLifeH = 300_000;
    private float[] _rust = Array.Empty<float>();       // [칸 × 2] 녹 (마른 칸 기준 시간)
    private float[] _rustLimit = Array.Empty<float>();  // [칸] 숨은 한계 (시드 · 찍지 않는다)
    private int[] _rustCause = Array.Empty<int>();      // [칸 × 2] 녹슬게 한 까닭 (처음 젖었을 때)
    /// <summary>[방 × 2] 관 밸브 (1: 잠겼다) — 냉각관 · 물관.</summary>
    public byte[] Valve = Array.Empty<byte>();
    public long PipesRusted, LeaksSpotted;

    private void BuildPipes()
    {
        _rust = new float[L.N * 2]; _rustCause = new int[L.N * 2]; _rustLimit = new float[L.N];
        for (int c = 0; c < L.N; c++)
        {
            double u = Math.Max(1e-9, Rng.Unit(_seed, Rng.Key("관 녹 한계"), c, 0));
            _rustLimit[c] = (float)(RustLifeH * Math.Sqrt(-Math.Log(u)) * 1.13);
        }
        Valve = new byte[L.Rooms.Count * 2];
    }

    private static int Slot(LineKind k) => k == LineKind.Water ? 1 : 0;

    /// <summary>관이 녹슨다 (마모 단계 · 25틱마다): 젖은 바닥 · 습한 공기 → 한계를 넘으면 바늘구멍 (자리는 모른다).</summary>
    private void PipesRust(Sim sim, double hours)
    {
        foreach (int c in _lineCells)
        {
            if ((LineAt[c] & 0b110) == 0) continue;
            double water = F.Stored(T.sWater, c);
            double rh = F.Partial(T.gH2O, c) / Math.Max(1e-6, Water.SatKPa(Math.Max(250, F.Temp(c))));
            double rate = (1 + 20 * Math.Min(1, water / 5)) * (rh > 0.7 ? 3 : 1);
            for (int s = 0; s < 2; s++)
            {
                var k = s == 0 ? LineKind.Coolant : LineKind.Water;
                if ((LineAt[c] & Bit(k)) == 0 || (LineCut[c] & Bit(k)) != 0 || PipeLeak[c * 2 + s] > 0) continue;
                _rust[c * 2 + s] += (float)(hours * rate);
                if (rate > 5 && _rustCause[c * 2 + s] == 0) _rustCause[c * 2 + s] = SpillNear(c) is int sp && sp != 0 ? sp : WetCauseAt(c);
                if (_rust[c * 2 + s] < _rustLimit[c]) continue;
                PipesRusted++;
                string what = k == LineKind.Water ? "물관" : "냉각관";
                int cause = sim.Causes.Add(CauseKind.Wear, _rustCause[c * 2 + s] != 0 ? $"젖은 바닥에서 {Ko.I(what)} 녹슬어 바늘구멍이 났다" : $"{Ko.I(what)} 오래 녹슬어 바늘구멍이 났다", sim.Tick, c, _rustCause[c * 2 + s]);
                LeakPipe(sim, c, k, 0.05f, cause, known: false);
            }
        }
    }

    /// <summary>그 칸을 적신 까닭: 그 칸 고인 물의 원인 (5단 C8) · 모르면 그 방에서 샌 물관 (없으면 0).</summary>
    private int WetCauseAt(int c)
    {
        if (F.WetCause[c] != 0) return F.WetCause[c];
        int room = _lineRoom[c];
        foreach (int x in _leakCells) if (_lineRoom[x] == room && PipeLeak[x * 2 + 1] > 0 && LineCause[x * 3 + 2] != 0) return LineCause[x * 3 + 2];
        return 0;
    }

    /// <summary>관 칸을 녹슬게 한다 (시험 · 만들 때 남은 흠) — 숨은 한계의 fraction까지.</summary>
    public void AgeRust(int cell, LineKind k, double fraction, int cause = 0)
    {
        if (cell < 0 || cell >= _rustLimit.Length || k is not (LineKind.Coolant or LineKind.Water)) return;
        int s = cell * 2 + Slot(k);
        _rust[s] = (float)Math.Max(_rust[s], _rustLimit[cell] * fraction);
        if (cause != 0) _rustCause[s] = cause;
    }

    public float RustFraction(int cell, LineKind k) => cell >= 0 && cell < _rustLimit.Length && k is LineKind.Coolant or LineKind.Water ? _rust[cell * 2 + Slot(k)] / _rustLimit[cell] : 0;

    /// <summary>모르는 샘의 크기 (몰/초 — 관망을 채우는 흐름 계량으로 주컴퓨터가 읽는다 · 아는 샘은 빼고).</summary>
    public double UnknownLeakRate(LineKind k)
    {
        int s = Slot(k);
        double sum = 0;
        foreach (int c in _leakCells)
            if (PipeLeak[c * 2 + s] > 0 && LeakKnown[c * 2 + s] == 0) sum += LeakFlow(c, k);
        return sum;
    }

    /// <summary>그 방 관에 모르는 샘이 있나 (그 갈래 — 누출 센서가 바닥의 물기로 잡는다).</summary>
    public bool UnknownLeakIn(LineKind k, int room)
    {
        int s = Slot(k);
        foreach (int c in _leakCells)
            if (PipeLeak[c * 2 + s] > 0 && LeakKnown[c * 2 + s] == 0 && _lineRoom[c] == room) return true;
        return false;
    }

    /// <summary>그 관 칸에서 지금 새는 빠르기 (몰/초 — 압력이 걸렸으면 다 · 아니면 스미는 만큼).</summary>
    public double LeakFlow(int c, LineKind k)
    {
        int s = Slot(k);
        float leak = PipeLeak[c * 2 + s];
        if (leak <= 0) return 0;
        bool p = Pumping(c, k);
        // 끊긴 관 (틈 1)은 관 단면으로 뿜는다 (DF9 — 압력이 빠지면 관에 든 것만: 물 500분의 1 · 냉각 (축압기가 조금 민다) 50분의 1) ·
        //  이음매 · 스미는 틈은 그 몫만큼 (펌프가 서면 냉각 10분의 1 · 물 20분의 1)
        if (leak >= 1) return k == LineKind.Water ? WaterLeakMolPerSec * (p ? 1 : 0.002) : CoolLeakMolPerSec * (p ? 1 : 0.02);
        return k == LineKind.Water ? leak * WaterSeepMolPerSec * (p ? 1 : 0.05) : leak * CoolSeepMolPerSec * (p ? 1 : 0.1);
    }

    /// <summary>밸브를 잠그거나 연다 (주컴퓨터 · 사람).</summary>
    public void SetValve(int room, LineKind k, bool closed)
    {
        if (room < 0 || room * 2 + 1 >= Valve.Length) return;
        byte v = (byte)(closed ? 1 : 0);
        if (Valve[room * 2 + Slot(k)] == v) return;
        Valve[room * 2 + Slot(k)] = v;
        _linesDirty = true;
    }
    public bool ValveClosed(int room, LineKind k) => room >= 0 && room * 2 + 1 < Valve.Length && Valve[room * 2 + Slot(k)] != 0;

    /// <summary>그 갈래 관이 지나는 방들 (작은 번호부터).</summary>
    public List<int> PipeRooms(LineKind k)
    {
        var set = new SortedSet<int>();
        foreach (int c in _lineCells) if ((LineAt[c] & Bit(k)) != 0 && _lineRoom[c] >= 0) set.Add(_lineRoom[c]);
        return set.ToList();
    }

    /// <summary>그 방 밸브를 잠그면 물이 안 가게 되는 방들 (그 방 + 물줄기 아래쪽 — 원천 방은 빠진다).</summary>
    public HashSet<int> Downstream(LineKind k, int room)
    {
        byte bit = Bit(k);
        int slot = Slot(k);
        var reach = new HashSet<int>();
        var q = new Queue<int>();
        foreach (int c in _lineCells)
        {
            if ((LineAt[c] & bit) == 0 || _lineRoom[c] == room || (LineCut[c] & bit) != 0) continue;
            if (!IsSourceCell(c, k)) continue;
            if (reach.Add(c)) q.Enqueue(c);
        }
        while (q.Count > 0)
        {
            int c = q.Dequeue();
            int dirs = LineDir[c * 4 + (int)k];
            for (int d = 0; d < 4; d++)
            {
                if ((dirs & (1 << d)) == 0) continue;
                int j = Toward(c, d);
                if (j < 0 || j >= L.N || (LineCut[j] & bit) != 0 || _lineRoom[j] == room || ValveShut(j, slot) || !reach.Add(j)) continue;
                q.Enqueue(j);
            }
        }
        var rooms = new HashSet<int>();
        foreach (int c in _lineCells) if ((LineAt[c] & bit) != 0 && !reach.Contains(c) && _lineRoom[c] >= 0) rooms.Add(_lineRoom[c]);
        return rooms;
    }

    private bool IsSourceCell(int c, LineKind k)
    {
        if (k == LineKind.Water) { foreach (var fu in L.Furniture) if (fu.Type == "WaterRecycler" && fu.Cells.Length > 0 && fu.Cells[0] == c) return true; return false; }
        foreach (var d in Devices) if (d.Def.Role == "pump" && d.Cell == c) return true;
        return false;
    }

    private bool ValveShut(int c, int slot) => _lineRoom[c] >= 0 && _lineRoom[c] * 2 + slot < Valve.Length && Valve[_lineRoom[c] * 2 + slot] != 0;

    /// <summary>둘레 칸의 모르는 샘을 알아본다 (로봇 · 사람이 그 자리에서 — 젖은 바닥 · 쉬익 · 냄새): 찾은 칸 (-1 없음).</summary>
    public int SpotLeaks(Sim sim, int cell, int radius, int who = -1)
    {
        int found = -1;
        foreach (int c in _leakCells)
        {
            if (Math.Abs(L.X(c) - L.X(cell)) + Math.Abs(L.Y(c) - L.Y(cell)) > radius) continue;
            for (int s = 0; s < 2; s++)
            {
                if (PipeLeak[c * 2 + s] <= 0 || LeakKnown[c * 2 + s] != 0) continue;
                LeakKnown[c * 2 + s] = 1; LeaksSpotted++;
                found = c;
            }
        }
        return found;
    }

    /// <summary>그 방의 그 갈래 관 칸 (작은 번호부터).</summary>
    public List<int> PipeCells(LineKind k, int room)
    {
        var list = new List<int>();
        foreach (int c in _lineCells) if ((LineAt[c] & Bit(k)) != 0 && _lineRoom[c] == room) list.Add(c);
        return list;
    }

    private void SavePipes(StateWriter w) { w.Arr(_rust); w.Arr(_rustCause); w.Arr(Valve); w.I64(PipesRusted); w.I64(LeaksSpotted); }
    private void LoadPipes(StateReader r) { r.ArrInto(_rust); r.ArrInto(_rustCause); r.ArrInto(Valve); PipesRusted = r.I64(); LeaksSpotted = r.I64(); _linesDirty = true; }
}
