namespace ShipEngine;

/// <summary>
/// ② 사고 화학 (마) 짙은 산소 — 산소 발생기 조절 센서의 노화 (docs/ENGINE.md ② '(마) 구현 설계').
/// <para>
/// 갈바닉 산소 센서는 늙으면 낮게 읽는다 → 발생기는 목표 분압을 맞추려 더 만든다 → 배가 소리 없이 짙은 산소가 된다.
/// 읽는 몫 <see cref="O2Gain"/> (1: 바름)은 발생기가 도는 동안 달에 1.2%씩 · 낡은 부품 씨앗이 고르면 몇 시간에 0.7까지 줄어든다.
/// 주컴퓨터가 제 방 센서와 견줘 다시 맞춘다 (<see cref="CalibrateO2"/>) — 위는 읽고 요청만.
/// </para>
/// </summary>
public sealed partial class Network
{
    /// <summary>조절 센서가 늙는 빠르기 (초마다 · 발생기가 도는 동안) — 달에 1.2%.</summary>
    public const double O2AgePerSec = 0.012 / (30 * 86400.0);
    /// <summary>산소 발생기 조절 센서가 읽는 몫 (1: 바름 · 늙으면 낮게).</summary>
    public double O2Gain = 1;
    /// <summary>낡은 센서가 빨리 어긋나는 빠르기 (초마다) · 그 바닥 · 원인 · 주컴퓨터가 다시 맞춘 마디.</summary>
    public double O2DriftRate, O2DriftFloor = 1;
    public int O2DriftCause, O2CalCause;
    public long O2Calibrations;

    /// <summary>조절 센서가 읽은 배 산소 분압 (kPa) — 발생기 일 몫과 산소 비율 상한이 이 값으로 정해진다.</summary>
    public double O2Reading => PpO2 * O2Gain;

    /// <summary>낡은 조절 센서 (시험) — 조절 센서가 몇 시간에 걸쳐 0.7까지 낮게 읽게 된다.</summary>
    public void StartO2Drift(double hours, int cause)
    {
        O2DriftFloor = Math.Min(O2Gain, 0.7);
        O2DriftRate = (O2Gain - O2DriftFloor) / Math.Max(60, hours * 3600);
        O2DriftCause = cause;
    }

    /// <summary>시험 · 차림: 조절 센서가 지금 이만큼 낮게 읽는다.</summary>
    public void SetO2Drift(Sim sim, double gain, int cause)
    {
        O2Gain = gain; O2DriftRate = 0; O2DriftFloor = gain;
        O2DriftCause = cause != 0 ? cause : sim.Causes.Add(CauseKind.Wear, "산소 발생기의 조절 센서가 늙어 산소를 낮게 읽는다", sim.Tick);
    }

    /// <summary>주컴퓨터가 요청: 조절 센서를 제 방 센서에 맞춰 다시 맞춘다 (원인: 센서의 어긋남 · 그것을 잡은 사건).</summary>
    public int CalibrateO2(Sim sim, int found)
    {
        int why = O2DriftCause != 0 ? O2DriftCause : sim.Causes.Add(CauseKind.Wear, "산소 발생기의 조절 센서가 늙어 산소를 낮게 읽었다", sim.Tick);
        O2CalCause = sim.Causes.Add(CauseKind.Computer, "주컴퓨터가 산소 발생기 조절 센서를 제 센서에 맞춰 다시 맞췄다", sim.Tick, -1, why, found);
        O2Gain = 1; O2DriftRate = 0; O2DriftFloor = 1; O2DriftCause = 0;
        O2Calibrations++;
        return O2CalCause;
    }

    /// <summary>한 틱: 발생기가 도는 동안 센서가 늙는다 (늘 · 낡은 부품이면 빨리). 처음 2% 넘게 어긋나면 그 원인 마디를 단다.</summary>
    private void AgeO2Sensor(Sim sim, double dt)
    {
        bool running = false;
        for (int d = 0; d < Devices.Count && !running; d++) if (Devices[d].Def.Role == "o2gen" && Supply[d] > 0.05f) running = true;
        if (!running) return;
        double was = O2Gain;
        O2Gain = Math.Max(0.4, O2Gain - O2AgePerSec * dt);
        if (O2DriftRate > 0) { O2Gain = Math.Max(O2DriftFloor, O2Gain - O2DriftRate * dt); if (O2Gain <= O2DriftFloor) O2DriftRate = 0; }
        if (was >= 0.98 && O2Gain < 0.98 && O2DriftCause == 0)
            O2DriftCause = sim.Causes.Add(CauseKind.Wear, "산소 발생기의 조절 센서가 늙어 산소를 낮게 읽기 시작했다", sim.Tick);
    }

    private void SaveO2(StateWriter w) { w.F64(O2Gain); w.F64(O2DriftRate); w.F64(O2DriftFloor); w.I32(O2DriftCause); w.I32(O2CalCause); w.I64(O2Calibrations); }
    private void LoadO2(StateReader r) { O2Gain = r.F64(); O2DriftRate = r.F64(); O2DriftFloor = r.F64(); O2DriftCause = r.I32(); O2CalCause = r.I32(); O2Calibrations = r.I64(); }
}
