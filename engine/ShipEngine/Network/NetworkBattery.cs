namespace ShipEngine;

/// <summary>
/// ② 사고 화학 (라) 리튬 전지 열폭주 — 셀 묶음 (docs/ENGINE.md ② '(라) 구현 설계').
/// <para>
/// 전해액이 든 배터리 하나 = 셀 묶음 여덟. 묶음마다 온도와 단계를 따로 둔다 — 겉면(그 칸)과 · 이웃 묶음과 열을 나눈다.
/// 90℃ 넘으면 SEI가 스스로 데우고 (아레니우스) · 130℃ 분리막이 녹아 속 합선 · 150℃ 안전 밸브가 열려 그 묶음 몫의 전해액을 뿜고 (불붙는 기체 · 플루오린 화합물) ·
/// 200℃ 폭주 — 그 묶음 몫의 저장 에너지: 절반은 묶음으로 (이웃을 달궈 번진다) · 절반은 칸의 불로.
/// 스스로 데우는 열 · 합선 · 폭주 열은 모두 저장 에너지에서 (다 쓴 배터리는 뿜기만 하고 불이 약하다).
/// </para>
/// </summary>
public sealed partial class Network
{
    public const int CellGroups = 8;
    public const double SeiK = 363, SeparatorK = 403, VentK = 423, RunawayK = 473;
    /// <summary>SEI 스스로 데움: 100℃에서 초당 3.3e-4K (분당 0.02K — 가속 열량계의 시작 문턱) · 활성화 에너지 130kJ/mol.</summary>
    public const double SeiRate0 = 3.3e-4, SeiT0 = 373, SeiEa = 130_000;
    /// <summary>묶음이 겉면과 · 이웃 묶음과 나누는 열 (W/K) · 분리막이 녹은 뒤 속 합선 (초당 K).</summary>
    public const double CellToSurface = 20, CellToCell = 30, ShortKps = 0.5;
    public float[] CellK = Array.Empty<float>();      // [장치 × 8] 묶음 온도 (K)
    public byte[] CellStage = Array.Empty<byte>();    // [장치 × 8] 0 멀쩡 · 1 뿜었다 · 2 폭주했다
    public int[] CellCause = Array.Empty<int>();      // [장치 × 8] 그 묶음을 달군 원인 (처음 90℃를 넘었을 때)
    public long CellVents, CellRunaways;
    private bool[] _hasCells = Array.Empty<bool>();

    private void BuildCells()
    {
        CellK = new float[Devices.Count * CellGroups]; CellStage = new byte[Devices.Count * CellGroups]; CellCause = new int[Devices.Count * CellGroups];
        _hasCells = new bool[Devices.Count];
        for (int d = 0; d < Devices.Count; d++)
        {
            for (int k = 0; k < CellGroups; k++) CellK[d * CellGroups + k] = (float)F.Tf[Devices[d].Cell];
            _hasCells[d] = T.Equip(Devices[d].Type).Parts.ContainsKey("electrolyte");
        }
    }

    /// <summary>전해액이 든 배터리인가 (설비 표의 부품에 '전해액' — 축전기는 아니다). 다 뿜어 전해액이 바닥나도 셀 묶음은 그대로다.</summary>
    public bool HasCells(Device dev) => dev.Id < _hasCells.Length && _hasCells[dev.Id];

    /// <summary>묶음이 겉면과 나누는 몫: 양 끝 묶음은 케이스에 닿아 많이 · 안쪽 묶음은 다른 셀에 둘러싸여 적게 (합은 묶음 수 — 평균 1). 불 곁에선 끝부터 달아오른다.</summary>
    private static double SurfaceShare(int k) => k == 0 || k == CellGroups - 1 ? 2.5 : 0.5;

    /// <summary>묶음 하나의 열용량 (J/K): kWh × 6.75kg (150Wh/kg) × 1000J/kgK ÷ 8.</summary>
    private static double GroupC(Device dev) => Math.Max(1, dev.Def.Kwh) * 6750 / CellGroups;

    /// <summary>④ 찌그러짐 · 파편: 묶음 하나가 곧바로 속 합선 (480K) — 그 열이 이웃으로 번진다.</summary>
    public void CrushCell(Device dev, int cause)
    {
        int i = dev.Id * CellGroups;
        for (int k = 0; k < CellGroups; k++)
            if (CellStage[i + k] < 2) { CellK[i + k] = Math.Max(CellK[i + k], 480); if (CellCause[i + k] == 0) CellCause[i + k] = cause; return; }
    }

    /// <summary>한 틱: 묶음 온도 · 단계. 열은 그 칸 겉면과 주고받는다 (보존) · 스스로 데우는 열은 저장 에너지에서.</summary>
    private void Cells(Sim sim, Device dev, double dt)
    {
        int d = dev.Id, i0 = d * CellGroups, c = dev.Cell;
        double cg = GroupC(dev);
        var fc = L.Furniture[dev.Furniture].Cells;
        double tSurf = 0; foreach (int x in fc) tSurf += F.Tf[x]; tSurf /= fc.Length;
        int alive = 0; for (int k = 0; k < CellGroups; k++) if (CellStage[i0 + k] < 2) alive++;
        double toSurface = 0;
        Span<double> dT = stackalloc double[CellGroups];
        for (int k = 0; k < CellGroups; k++)
        {
            double t = CellK[i0 + k];
            double qs = CellToSurface * SurfaceShare(k) * (tSurf - t);                 // W
            double q = qs;
            if (k > 0) q += CellToCell * (CellK[i0 + k - 1] - t);
            if (k < CellGroups - 1) q += CellToCell * (CellK[i0 + k + 1] - t);
            toSurface -= qs;
            if (CellStage[i0 + k] < 2 && Charge[d] > 1e-6)
            {
                double self = 0;
                if (t > SeiK) self += cg * Math.Min(5, SeiRate0 * Math.Exp(SeiEa / 8.314 * (1 / SeiT0 - 1 / t)));
                if (t > SeparatorK) self += cg * ShortKps;
                double take = Math.Min(self * dt, Charge[d] * 3.6e6 / Math.Max(1, alive));   // 저장 에너지에서
                Charge[d] = (float)Math.Max(0, Charge[d] - take / 3.6e6);
                q += take / dt;
                if (t > SeiK && CellCause[i0 + k] == 0) CellCause[i0 + k] = sim.Find<Chemistry>()?.HeatCauseAt(c) ?? 0;
            }
            dT[k] = q * dt / cg;
        }
        for (int k = 0; k < CellGroups; k++) CellK[i0 + k] = (float)(CellK[i0 + k] + dT[k]);
        if (toSurface != 0) foreach (int x in fc) F.AddSurfaceHeat(x, toSurface * dt / fc.Length);
        for (int k = 0; k < CellGroups; k++)
        {
            int g = i0 + k;
            if (CellStage[g] == 0 && CellK[g] >= VentK) VentCells(sim, dev, g);
            if (CellStage[g] == 1 && CellK[g] >= RunawayK) Runaway(sim, dev, g, cg, alive);
        }
    }

    /// <summary>150℃ — 안전 밸브가 열려 그 묶음 몫의 전해액을 뿜는다 (쉬익).</summary>
    private void VentCells(Sim sim, Device dev, int g)
    {
        CellStage[g] = 1; CellVents++;
        int left = 0; for (int k = 0; k < CellGroups; k++) if (CellStage[dev.Id * CellGroups + k] == 0) left++;
        var chem = sim.Find<Chemistry>();
        if (chem == null) return;
        double cg = GroupC(dev);
        foreach (int x in L.Furniture[dev.Furniture].Cells)
        {
            chem.Vent(sim, x, 1.0 / (left + 1), out double q);
            if (q >= 0) { F.AddHeat(x, q); continue; }
            // 갈라지며 드는 열: 셀의 저장 에너지(음극의 리튬)에서 먼저 · 모자라면 그 묶음의 열에서 (뿜는 순간 셀이 잠깐 식는다)
            double fromCharge = Math.Min(-q, Charge[dev.Id] * 3.6e6);
            Charge[dev.Id] = (float)Math.Max(0, Charge[dev.Id] - fromCharge / 3.6e6);
            CellK[g] = (float)Math.Max(250, CellK[g] - (-q - fromCharge) / cg);
        }
        int cause = sim.Causes.Add(CauseKind.Heat, $"{dev.Name}의 셀이 달아올라 안전 밸브로 전해액 증기를 뿜었다", sim.Tick, dev.Cell, CellCause[g]);
        CellCause[g] = cause;
        chem.MarkGas(dev.Cell, cause);   // 수소 · 메탄 · 일산화탄소 — 터지면 이 뿜음이 뿌리 (② (마))
        sim.Facts.Emit(sim.Tick, FactKind.Overheat, dev.Cell, CellK[g], cause, other: dev.Id);
        sim.Find<Sound>()?.Emit(sim, dev.Cell, 85, SoundKind.Hiss, cause);
    }

    /// <summary>200℃ — 폭주: 그 묶음 몫의 저장 에너지가 30초 안에 — 절반은 묶음으로 (이웃을 달궈 번진다) · 절반은 칸의 불로 (펑).</summary>
    private void Runaway(Sim sim, Device dev, int g, double cg, int alive)
    {
        int d = dev.Id;
        double e = Charge[d] * 3.6e6 / Math.Max(1, alive);
        Charge[d] = (float)Math.Max(0, Charge[d] - e / 3.6e6);
        CellStage[g] = 2; CellRunaways++;
        CellK[g] = (float)(CellK[g] + 0.5 * e / cg);
        Broken[d * 4] = 1;
        int cause = sim.Causes.Add(CauseKind.Heat, $"{dev.Name}의 셀이 열폭주했다", sim.Tick, dev.Cell, CellCause[g]);
        CellCause[g] = cause;
        if (e > 1e5) sim.Find<Chemistry>()?.Ignite(dev.Cell, Math.Min(0.5 * e, 8e6), cause);
        sim.Facts.Emit(sim.Tick, FactKind.MachineFail, dev.Cell, (float)(e / 1e6), cause, other: d);
        var snd = sim.Find<Sound>();
        snd?.Emit(sim, dev.Cell, e > 1e6 ? 110 : 95, SoundKind.Bang, cause);
    }

    private void SaveCells(StateWriter w) { w.Arr(CellK); w.Arr(CellStage); w.Arr(CellCause); w.I64(CellVents); w.I64(CellRunaways); }
    private void LoadCells(StateReader r) { r.ArrInto(CellK); r.ArrInto(CellStage); r.ArrInto(CellCause); CellVents = r.I64(); CellRunaways = r.I64(); }
}
