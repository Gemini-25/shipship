namespace ShipEngine;

/// <summary>
/// ⑥ 바퀴 3 (나) 고장 원인 — 닳음의 까닭을 원인 마디로 (docs/ENGINE.md ⑥ '바퀴 3 설계').
/// <para>
/// 부품 마모를 까닭별로 나눠 쌓는다: 그냥 씀 · 열 · 습기 · 그을음(먼지) · 기름 마름. 숨은 한계를 넘으면 가장 큰 몫의 까닭이 고장 원인이 된다 —
/// 열은 그 칸을 달군 것(불 · 냉각 고장) · 습기는 샌 물 · 망가진 제습기 · 그을음은 그 방(없으면 배)의 마지막 불에 잇는다.
/// 마른 베어링: 기계 부품이 정비 없이 수명의 80%를 넘게 돌면 빨리 닳는다 (정비 = 갈아 끼우기가 늦어진 배).
/// </para>
/// <para>
/// 전선 피복도 칸마다 늙는다 (그 칸 온도로 10K마다 두 배 · 40℃에서 숨은 한계 평균 20만 시간 — 칸마다 와이블).
/// 한계를 넘으면 피복이 갈라진다 — 전기를 넣으면 드러난 구리끼리 닿아 합선 (탄 전선과 같다 · 이어야 한다).
/// </para>
/// </summary>
public sealed partial class Network
{
    private const int Why = 6;                       // 까닭: 0 그냥 씀 · 1 열 · 2 습기 · 3 그을음 · 4 기름 마름 · 5 볼트 풀림 (④ 열 · 진동)
    private float[] _wearBy = Array.Empty<float>();  // [장치 × 4 + 부품] × 5 — 지난 수리 뒤 까닭별로 쌓인 마모 (시간)
    private int[] _condCause = Array.Empty<int>();   // [장치 × 4 + 부품] × 5 — 그 까닭을 만든 원인 마디 (처음 컸을 때)
    private float[] _run = Array.Empty<float>();     // [장치 × 4 + 부품] 지난 수리(정비) 뒤 돈 시간
    private readonly Dictionary<int, int> _brokeCause = new(); // 부품 → 망가진 원인 마디 (고치면 지운다)
    private int[] _sootCause = Array.Empty<int>();   // [방] 그 방의 마지막 불 (그을음의 뿌리)
    private int _lastFireCause;                      // 배의 마지막 불
    // 전선 피복 (칸마다)
    public const double CableLifeH = 200_000, CableRefK = 313;
    private float[] _insul = Array.Empty<float>();       // [칸] 늙은 시간 (40℃ 기준)
    private float[] _insulLimit = Array.Empty<float>();  // [칸] 숨은 한계 (시드에서 · 찍지 않는다)
    private int[] _insulCause = Array.Empty<int>();      // [칸] 피복을 달군 원인 (처음 뜨거웠을 때)
    private readonly Dictionary<int, int> _overloadCause = new(); // 급전선 → '짐이 많아 늘 뜨겁다' 마디
    public long CablesAged, NeglectedHours;

    private void BuildWear()
    {
        int nk = Devices.Count * 4;
        _wearBy = new float[nk * Why]; _condCause = new int[nk * Why]; _run = new float[nk];
        _sootCause = new int[L.Rooms.Count];
        _insul = new float[L.N]; _insulCause = new int[L.N]; _insulLimit = new float[L.N];
        for (int c = 0; c < L.N; c++)
        {
            double u = Math.Max(1e-9, Rng.Unit(_seed, Rng.Key("피복 한계"), c, 0));
            _insulLimit[c] = (float)(CableLifeH * Math.Sqrt(-Math.Log(u)) * 1.13);
        }
    }

    /// <summary>부품 하나를 hours만큼 닳게 한다 (마모 단계 · 정비 없이 돈 시간 — 같은 셈).</summary>
    private void WearPart(Sim sim, int d, int p, double hours, bool running, double temp, double rh, double soot)
    {
        int k = d * 4 + p, b = k * Why;
        var dev = Devices[d];
        double life = Math.Max(1, dev.Life((Part)p));
        double heat = 1, wet = 1, dust = 1, dry = 1, rattle = 1;
        switch ((Part)p)
        {
            case Part.Electric: heat = Math.Pow(2, Math.Clamp((temp - 298) / 10, -2, 8)); break;
            case Part.Mechanical:
                heat = 1 + Math.Max(0, temp - 298) / 50;
                if (running) dry = 1 + 3 * Math.Max(0, _run[k] / life - 0.8);   // 정비 때(수명의 80%)를 넘겨 돈 베어링
                if (running && d < Loose.Length && Loose[d] >= 1) rattle = 3;      // 볼트가 풀려 덜컹거리는 모터 (④ 열 · 진동)
                break;
            case Part.Seal: wet = 1 + Math.Max(0, rh); heat = 1 + Math.Max(0, temp - 298) / 80; break;
            default: dust = 1 + soot / 100; break;   // 거르개: 그을음 100ppm마다 한 배 더
        }
        double amount = hours * (running ? 1 : 0.15) * heat * wet * dust * dry * rattle;
        Wear[k] += (float)amount;
        if (running) _run[k] += (float)hours;
        // 까닭별 몫: 늘린 배수의 로그 비로 나눈다 (1보다 작은 배수 — 찬 방의 전기 — 는 그냥 씀으로)
        double up = Math.Max(1, heat) * wet * dust * dry * rattle;
        if (up > 1.0001)
        {
            double extra = amount * (1 - 1 / up), lsum = Math.Log(up);
            if (heat > 1) _wearBy[b + 1] += (float)(extra * Math.Log(heat) / lsum);
            if (wet > 1) _wearBy[b + 2] += (float)(extra * Math.Log(wet) / lsum);
            if (dust > 1) _wearBy[b + 3] += (float)(extra * Math.Log(dust) / lsum);
            if (dry > 1) _wearBy[b + 4] += (float)(extra * Math.Log(dry) / lsum);
            if (rattle > 1) _wearBy[b + 5] += (float)(extra * Math.Log(rattle) / lsum);
            _wearBy[b] += (float)(amount - extra);
        }
        else _wearBy[b] += (float)amount;
        // 그 까닭을 만든 것 (처음 크게 닿았을 때)
        if (heat > 1.5 && _condCause[b + 1] == 0) _condCause[b + 1] = HeatCondCause(sim, dev.Cell);
        if (wet > 1.5 && _condCause[b + 2] == 0) _condCause[b + 2] = WetCondCause(dev);
        if (rattle > 1.5 && _condCause[b + 5] == 0) _condCause[b + 5] = LooseCause[d];
        if (dust > 1.5 && _condCause[b + 3] == 0) _condCause[b + 3] = dev.Room >= 0 && dev.Room < _sootCause.Length && _sootCause[dev.Room] != 0 ? _sootCause[dev.Room] : _lastFireCause;
        if (Wear[k] / _limit[k] >= 1) Fail(sim, d, (Part)p);
    }

    /// <summary>그 칸을 덥힌 까닭: 그 칸 · 곁의 불 · 달군 것 → 냉각이 끊긴 까닭 (망가진 펌프 · 공조) → 원자로 멈춤 (없으면 0).</summary>
    private int HeatCondCause(Sim sim, int cell)
    {
        int h = HeatSource(sim.Find<Chemistry>(), cell);
        if (h != 0) return h;
        foreach (var dv in Devices)
            if (dv.Def.Role is "pump" or "hvac")
                for (int p = 0; p < 4; p++) if (Broken[dv.Id * 4 + p] != 0 && _brokeCause.TryGetValue(dv.Id * 4 + p, out int bc)) return bc;
        return ReactorScram ? ScramCause : 0;
    }

    /// <summary>습기의 까닭: 같은 방에서 샌 물 (물 재생기 · 물관) → 망가진 제습기 (없으면 0).</summary>
    private int WetCondCause(Device dev)
    {
        int s = SpillNear(dev.Cell);
        if (s != 0) return s;
        foreach (int c in _lineCells) if (_lineRoom[c] == dev.Room && PipeLeak[c * 2 + 1] > 0 && LineCause[c * 3 + 2] != 0) return LineCause[c * 3 + 2];
        foreach (var dv in Devices)
            if (dv.Def.Role == "dehumid")
                for (int p = 0; p < 4; p++) if (Broken[dv.Id * 4 + p] != 0 && _brokeCause.TryGetValue(dv.Id * 4 + p, out int bc)) return bc;
        return 0;
    }

    /// <summary>방마다 마지막 불을 적어 둔다 (그을음의 뿌리 — 마모 단계마다).</summary>
    private void NoteFires(Sim sim)
    {
        var chem = sim.Find<Chemistry>();
        if (chem == null || chem.BurningCount == 0) return;
        for (int c = 0; c < L.N; c++)
        {
            if (chem.Burning[c] == 0 || chem.FireCause[c] == 0) continue;
            _lastFireCause = chem.FireCause[c];
            int r = c < _lineRoom.Length ? _lineRoom[c] : L.Room[c];
            if (r >= 0 && r < _sootCause.Length) _sootCause[r] = chem.FireCause[c];
        }
    }

    /// <summary>고장 원인 말: 기록된 까닭(만들 때 흠 · 손댄 이)이 있으면 그것 · 아니면 가장 큰 몫의 까닭이 30%를 넘으면 그 까닭 · 아니면 '낡아 망가졌다'.</summary>
    private (string text, int parent) WhyFailed(Device dev, Part p)
    {
        int k = dev.Id * 4 + (int)p, b = k * Why;
        string what = p switch { Part.Electric => "회로", Part.Mechanical => "모터", Part.Seal => "봉함", _ => "거르개" };
        string plain = $"{dev.Name}의 {Ko.I(what)} 낡아 망가졌다";
        if (_wearCause.TryGetValue(k, out int wc)) return (plain, wc);
        double total = 0; int top = 0;
        for (int j = 0; j < Why; j++) total += _wearBy[b + j];
        for (int j = 1; j < Why; j++) if (_wearBy[b + j] > _wearBy[b + top] || top == 0 && _wearBy[b + j] > 0) top = j;
        // 정비 때(수명의 80%)를 넘겨 돈 모터는 그것이 까닭이다 — 다른 까닭이 크지 않으면
        if ((top == 0 || total <= 0 || _wearBy[b + top] < 0.3 * total) && p == Part.Mechanical && _run[k] > 0.8 * dev.Life(p)) top = 4;
        else if (top == 0 || total <= 0 || _wearBy[b + top] < 0.3 * total) return (plain, 0);
        string text = (top, p) switch
        {
            (1, Part.Electric) => $"{dev.Name}의 회로가 뜨거운 데서 일찍 낡아 망가졌다",
            (1, Part.Seal) => $"{dev.Name}의 봉함이 열에 굳어 갈라졌다",
            (1, _) => $"{dev.Name}의 {Ko.I(what)} 뜨거운 데서 일찍 닳아 망가졌다",
            (2, _) => $"{dev.Name}의 {Ko.I(what)} 습기에 부풀어 망가졌다",
            (3, _) => $"{dev.Name}의 거르개에 그을음이 끼어 막혔다",
            (5, _) => $"{dev.Name}의 {Ko.I(what)} 볼트가 풀린 채 덜컹거리다 닳아 망가졌다",
            _ => $"{dev.Name}의 모터 베어링 기름이 말라 망가졌다 — 정비 없이 {_run[k]:0}시간을 돌았다",
        };
        return (text, _condCause[b + top]);
    }

    private void ClearWear(int k)
    {
        if (k * Why + Why > _wearBy.Length) return;
        for (int j = 0; j < Why; j++) { _wearBy[k * Why + j] = 0; _condCause[k * Why + j] = 0; }
        _run[k] = 0;
        _brokeCause.Remove(k);
    }

    /// <summary>정비 없이 hours시간 더 돌았다 (시험 — 지금 그 칸의 형편으로 10시간씩 닳게 한다 · 망가지면 멈춘다).</summary>
    public void Neglect(Sim sim, int device, Part p, double hours)
    {
        int k = device * 4 + (int)p;
        if (_limit[k] == float.MaxValue) return;
        var dev = Devices[device];
        int c = dev.Cell;
        double temp = Math.Max(F.Temp(c), F.Ts[c]);
        double rh = F.Partial(T.gH2O, c) / Math.Max(1e-6, Water.SatKPa(Math.Max(250, F.Temp(c))));
        double soot = F.Ppm(T.gSoot, c);
        for (double left = hours; left > 1e-9 && Broken[k] == 0; left -= 10)
        {
            WearPart(sim, device, (int)p, Math.Min(10, left), true, temp, rh, soot);
            NeglectedHours += (long)Math.Min(10, left);
        }
    }

    public float RunHours(int device, Part p) => _run.Length > device * 4 + (int)p ? _run[device * 4 + (int)p] : 0;

    // ───────────────────────── 전선 피복 ─────────────────────────

    /// <summary>전선 칸마다 피복이 늙는다 (마모 단계 · 25틱마다): 한계를 넘으면 피복이 갈라져 그 칸 전선이 상한다 (전기를 넣으면 합선).</summary>
    private void CablesAge(Sim sim, double hours)
    {
        byte pw = Bit(LineKind.Power);
        foreach (int c in _lineCells)
        {
            if ((LineAt[c] & pw) == 0 || (LineCut[c] & pw) != 0) continue;
            double t = _hotBody[c] ? 293 : Math.Max(F.Tf[c], F.Ts[c]);
            double rate = Math.Pow(2, Math.Clamp((t - CableRefK) / 10, -4, 12));
            _insul[c] += (float)(hours * rate);
            if (rate > 4 && _insulCause[c] == 0) _insulCause[c] = InsulHeatCause(sim, c);
            if (_insul[c] < _insulLimit[c]) continue;
            CablesAged++;
            CutLines(sim, c, pw, sim.Causes.Add(CauseKind.Wear, "오래 달궈진 전선 피복이 갈라졌다", sim.Tick, c, _insulCause[c]));
        }
    }

    /// <summary>피복을 달군 까닭: 곁의 불 · 달군 것 → 정격을 넘게 흐르는 급전선 → 그 방을 덥힌 까닭 (없으면 0).</summary>
    private int InsulHeatCause(Sim sim, int c)
    {
        int h = HeatSource(sim.Find<Chemistry>(), c);
        if (h != 0) return h;
        foreach (var fd in Feeders)
        {
            if (Amps[fd.Id] <= fd.RatingA || Array.IndexOf(fd.Path, c) < 0) continue;
            if (!_overloadCause.TryGetValue(fd.Id, out int oc))
                _overloadCause[fd.Id] = oc = sim.Causes.Add(CauseKind.Electric, $"{L.Rooms[fd.Room].Name} 급전선에 정격보다 많은 전류가 늘 흘러 뜨겁다 ({Amps[fd.Id]:0}A / {fd.RatingA:0}A)", sim.Tick, c);
            return oc;
        }
        return HeatCondCause(sim, c);
    }

    /// <summary>전선 칸을 늙게 한다 (시험 · 만들 때 남은 흠) — 숨은 한계의 fraction까지.</summary>
    public void AgeCable(int cell, double fraction, int cause = 0)
    {
        if (cell < 0 || cell >= _insul.Length) return;
        _insul[cell] = (float)Math.Max(_insul[cell], _insulLimit[cell] * fraction);
        if (cause != 0) _insulCause[cell] = cause;
    }
    public float CableAge(int cell) => cell >= 0 && cell < _insul.Length ? _insul[cell] / _insulLimit[cell] : 0;

    private void SaveWear(StateWriter w)
    {
        w.Arr(_wearBy); w.Arr(_condCause); w.Arr(_run); w.Arr(_sootCause); w.I32(_lastFireCause);
        var bk = _brokeCause.Keys.OrderBy(x => x).ToList();
        w.I32(bk.Count); foreach (var k in bk) { w.I32(k); w.I32(_brokeCause[k]); }
        w.Arr(_insul); w.Arr(_insulCause);
        var ok = _overloadCause.Keys.OrderBy(x => x).ToList();
        w.I32(ok.Count); foreach (var k in ok) { w.I32(k); w.I32(_overloadCause[k]); }
        w.I64(CablesAged); w.I64(NeglectedHours);
    }

    private void LoadWear(StateReader r)
    {
        r.ArrInto(_wearBy); r.ArrInto(_condCause); r.ArrInto(_run); r.ArrInto(_sootCause); _lastFireCause = r.I32();
        _brokeCause.Clear(); int n = r.I32(); for (int i = 0; i < n; i++) { int k = r.I32(); _brokeCause[k] = r.I32(); }
        r.ArrInto(_insul); r.ArrInto(_insulCause);
        _overloadCause.Clear(); n = r.I32(); for (int i = 0; i < n; i++) { int k = r.I32(); _overloadCause[k] = r.I32(); }
        CablesAged = r.I64(); NeglectedHours = r.I64();
    }
}
