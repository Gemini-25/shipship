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

    /// <summary>물 밸브를 틀고 잠근다 (판단의 요청).</summary>
    public void SetValve(int d, bool open) { if (ShowerValve.Length != Devices.Count) Array.Resize(ref ShowerValve, Devices.Count); ShowerValve[d] = (byte)(open ? 1 : 0); }
    public bool ValveOpen(int d) => d >= 0 && d < ShowerValve.Length && ShowerValve[d] != 0;

    private void ShowerStep(Sim sim, int d, double kw, double dt)
    {
        if (ShowerMeter.Length != Devices.Count) Array.Resize(ref ShowerMeter, Devices.Count);
        if (!ValveOpen(d) || !WaterAtDevice(d)) { if (On[d] != 0) SetOn(d, false); return; }   // 잠겼다 · 물관이 끊겼다 — 물이 흐르지 않으면 흐름 스위치가 온수기를 끈다 (부하 덜기 뒤 다시 켜도 빈 온수기가 전기를 먹지 않게)
        double flow = Math.Min(WaterTank, ShowerFlow * dt);
        if (flow <= 0) return;
        var dev = Devices[d];
        bool hot = Working(d);
        WaterTank -= flow; sim.Book.Add(Res.Water, Flow.Washed, -flow);
        ShowerMeter[d] += flow;
        double steam = hot ? flow * 0.01 : 0;   // 따뜻한 물의 1%가 김으로 (8L면 80mL)
        if (steam > 0) F.AddGas(dev.Cell, T.gH2O, steam, 313);
        WasteWater += flow - steam;
        if (hot) HeatInto(dev, kw * 1000 * 0.15 * dt);   // 데운 물의 열은 거의 다 배수구로 — 칸에는 김 · 젖은 벽으로 15%
    }

    private void SaveShower(StateWriter w) { w.Tag("샤워"); w.Arr(ShowerMeter); w.Arr(ShowerValve); }
    private void LoadShower(StateReader r) { r.Tag("샤워"); ShowerMeter = r.Arr<double>(); ShowerValve = r.Arr<byte>(); }
}
