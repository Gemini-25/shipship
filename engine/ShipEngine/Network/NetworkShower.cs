namespace ShipEngine;

/// <summary>
/// 샤워 칸 (월드 — docs/WASH.md (다)): 물 밸브를 틀어 두면 사람이 있든 없든 물이 흐른다 — 6분에 8L · 물탱크 → 오수 (물 재생기가 되돌린다) ·
/// 온수기(장치의 켬 · 전기)가 돌면 데운 물의 1%가 김으로 칸의 공기에 · 열은 거의 다 물과 함께 배수구로 (칸에는 15%).
/// 밸브와 온수기는 따로다 — 전기가 끊기거나 주컴퓨터가 전력을 줄이려 온수기를 꺼도 찬물은 흐른다.
/// 언제 틀고 잠그는지는 판단 (⑧ 씻는 이 · ⑩ 주컴퓨터) — 씻는 이는 흐른 물(계량기의 차)만큼 씻긴다.
/// </summary>
public sealed partial class Network
{
    /// <summary>샤워 물 (몰/초 — 6분에 8L).</summary>
    public const double ShowerFlow = Life.ShowerMol / 360;
    /// <summary>샤워 칸마다 지금까지 흐른 물 (몰 — 계량기 · 쓰는 이는 틀 때와 잠글 때의 차로 안다).</summary>
    public double[] ShowerMeter = Array.Empty<double>();
    /// <summary>샤워 칸마다 물 밸브 (1 열림 — 씻는 이 · 주컴퓨터가 틀고 잠근다).</summary>
    public byte[] ShowerValve = Array.Empty<byte>();
    /// <summary>5단: 샤워 칸마다 온수기 조절기 (0 멀쩡 · 1 접점 붙음 — 뜨거움 · 2 열린 채 굳음 — 찬물) · 그 고장의 원인 마디 · 무중력에 떠다닌 샤워 물의 원인 마디.</summary>
    public byte[] ThermoFault = Array.Empty<byte>();
    public int[] ThermoCause = Array.Empty<int>(), ShowerFloatCause = Array.Empty<int>();

    /// <summary>온수기 조절기의 고장 사이 평균 시간 (데우며 흐르는 시간 · 기능 확인용 기본값 — 찍지 않는다).</summary>
    public double ThermoMtbfH = 1500;
    /// <summary>들어오는 물 온도 · 조절기가 맞추는 온도 (K).</summary>
    public const double InletK = 293.15, SetK = 313;

    private void EnsureThermo()
    {
        if (ThermoFault.Length != Devices.Count) Array.Resize(ref ThermoFault, Devices.Count);
        if (ThermoCause.Length != Devices.Count) Array.Resize(ref ThermoCause, Devices.Count);
    }

    /// <summary>온수기가 지금 물을 데우나: 켜졌고 · 전기 · 기계 부품이 돌고 (Working) · 조절기가 열린 채 굳지 않았다.</summary>
    private bool Heating(int d) => Working(d) && !(d < ThermoFault.Length && ThermoFault[d] == 2);

    /// <summary>
    /// 샤워 물 온도 (K — 5단 C16): 데우면 들어오는 물 293K + 온수기 열 / (흐름 × 75.3 J/몰K) — 조절기가 313K로 자른다 · 접점이 붙으면 자르지 않는다 (이 배 3kW로 ≈ 325K) ·
    /// 데우지 않으면 (꺼짐 · 전기 없음 · 전기 부품 고장 · 열린 채 굳음) 293K.
    /// </summary>
    public double ShowerWaterK(int d)
    {
        if (!Heating(d)) return InletK;
        double k = InletK + Devices[d].Def.Kw * 1000 / (ShowerFlow * 75.3);
        return d < ThermoFault.Length && ThermoFault[d] == 1 ? k : Math.Min(SetK, k);
    }

    /// <summary>요청 (시험 · 사고): 조절기가 고장 난다 — 접점 붙음 (뜨거움) 또는 열린 채 굳음 (찬물) · 원인 마디 · 사실 '조절기 고장'.</summary>
    public void BreakThermostat(Sim sim, int d, bool hot, int cause)
    {
        EnsureThermo();
        ThermoFault[d] = (byte)(hot ? 1 : 2);
        ThermoCause[d] = cause;
        sim.Facts.Emit(sim.Tick, FactKind.ThermoStuck, Devices[d].Cell, ThermoFault[d], cause, other: d);
    }

    /// <summary>물 밸브를 틀고 잠근다 (판단의 요청).</summary>
    public void SetValve(int d, bool open) { if (ShowerValve.Length != Devices.Count) Array.Resize(ref ShowerValve, Devices.Count); ShowerValve[d] = (byte)(open ? 1 : 0); }
    public bool ValveOpen(int d) => d >= 0 && d < ShowerValve.Length && ShowerValve[d] != 0;
    /// <summary>지금 물이 흐르나 (밸브가 열렸고 · 물관으로 물이 오고 · 물탱크가 비지 않았다 — 읽기만: 화면의 물줄기).</summary>
    public bool ShowerRunning(int d) => ValveOpen(d) && WaterAtDevice(d) && WaterTank > 0;

    private void ShowerStep(Sim sim, int d, double kw, double dt)
    {
        if (ShowerMeter.Length != Devices.Count) Array.Resize(ref ShowerMeter, Devices.Count);
        if (!ValveOpen(d) || !WaterAtDevice(d)) { if (On[d] != 0) SetOn(d, false); return; }   // 잠겼다 · 물관이 끊겼다 — 물이 흐르지 않으면 흐름 스위치가 온수기를 끈다 (부하 덜기 뒤 다시 켜도 빈 온수기가 전기를 먹지 않게)
        double flow = Math.Min(WaterTank, ShowerFlow * dt);
        if (flow <= 0) return;
        var dev = Devices[d];
        EnsureThermo();
        bool hot = Heating(d);
        if (hot && ThermoFault[d] == 0 && Rng.Unit(sim.Seed, Rng.Key("온수기 조절기 고장"), sim.Tick, d) < dt / 3600 / ThermoMtbfH)   // 데우며 흐르는 동안 조절기가 닳는다 (5단 C16)
        {
            bool stuck = Rng.Unit(sim.Seed, Rng.Key("온수기 조절기"), sim.Tick, d) < 0.5;
            int cause = sim.Causes.Add(CauseKind.Machine, stuck ? $"{dev.Name}의 온수기 조절기 접점이 붙어 물이 뜨겁게 나온다" : $"{dev.Name}의 온수기 조절기가 열린 채 굳어 찬물만 나온다", sim.Tick, dev.Cell, WetCondCause(dev));
            BreakThermostat(sim, d, stuck, cause);
            hot = Heating(d);
        }
        double waterK = ShowerWaterK(d);
        WaterTank -= flow; sim.Book.Add(Res.Water, Flow.Washed, -flow);
        ShowerMeter[d] += flow;
        double steam = waterK > 305 ? flow * 0.01 : 0;   // 따뜻한 물의 1%가 김으로 (8L면 80mL)
        if (steam > 0) F.AddGas(dev.Cell, T.gH2O, steam, waterK);
        WasteWater += flow - steam;
        if (hot) HeatInto(dev, kw * 1000 * 0.15 * dt);   // 데운 물의 열은 거의 다 배수구로 — 칸에는 김 · 젖은 벽으로 15%
    }

    /// <summary>배수구가 빼는 빠르기 (몰/초 — 0.5L/s).</summary>
    public const double DrainFlow = 27.75;
    /// <summary>배수구에 빠진 물 (몰 누계 — 셈 · 찍지 않는다).</summary>
    public double Drained;

    /// <summary>샤워 칸 칸 = 바닥 배수구 (5단 C14): 0.3g 이상이고 그 칸이 끊기지 않았으면 그 칸 바닥 물을 초당 0.5L까지 오수로 (물 재생기가 되돌린다 · 공급 관 조건은 없다).</summary>
    private void ShowerDrain(int d, double dt)
    {
        int c = Devices[d].Cell;
        if (c < 0 || F.Grav[c] < Field.LowG || F.Severed[c] != 0) return;
        double have = F.Stored(T.sWater, c);
        if (have <= 0) return;
        double took = F.TakeStore(c, T.sWater, Math.Min(have, DrainFlow * dt));
        WasteWater += took; Drained += took;
    }

    private void SaveShower(StateWriter w) { w.Tag("샤워"); w.Arr(ShowerMeter); w.Arr(ShowerValve); w.Tag("조절기"); w.Arr(ThermoFault); w.Arr(ThermoCause); w.Arr(ShowerFloatCause); }
    private void LoadShower(StateReader r) { r.Tag("샤워"); ShowerMeter = r.Arr<double>(); ShowerValve = r.Arr<byte>(); r.Tag("조절기"); ThermoFault = r.Arr<byte>(); ThermoCause = r.Arr<int>(); ShowerFloatCause = r.Arr<int>(); }
}
