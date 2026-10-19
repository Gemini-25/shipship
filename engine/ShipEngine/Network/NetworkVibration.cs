namespace ShipEngine;

/// <summary>
/// ⑥ 진동 (④ 열 · 진동 · docs/ENGINE.md ④ '열 · 진동'): 돌아가는 기계(기계 부품이 있는 장치)는 초당 25번 흔들린다 —
/// 세기 = 4MPa × 갈래(펌프 1.25 · 엔진 2 · 보조 발전기 1.5 · 공조 0.75 · 공장 1 · 그 밖 0.4) × (1 + 4 × 기계 부품 닳음) (풀렸으면 × 1.5).
/// <para>볼트는 진동에 풀린다 (정커 효과 — 세기의 제곱 · 기본 세기에서 200시간) · 다 풀리면 덜컹거린다 (사실 · 소리 — 모터가 세 배 빨리 닳는다).
/// 곁 벽의 금은 ④ 구조에 요청한다 (1분에 1,500번). 조이는 것은 사람 · 로봇 (주컴퓨터가 맡긴다).</para>
/// </summary>
public sealed partial class Network
{
    public const double VibBaseMPa = 4, VibHz = 25, LooseHours = 200;
    public float[] Loose = Array.Empty<float>();
    public int[] LooseCause = Array.Empty<int>();
    public long Rattles, Tightened;

    public static double VibRole(string role) => role switch { "pump" => 1.25, "engine" => 2, "aux" => 1.5, "hvac" => 0.75, "industry" => 1, _ => 0.4 };

    /// <summary>그 장치의 진동 세기 (MPa — 돌 때만 · 기계 부품이 있어야).</summary>
    public double VibMPa(int d)
    {
        var dev = Devices[d];
        if (!dev.Has(Part.Mechanical) || !Working(d)) return 0;
        double worn = Math.Min(1, WearFraction(d, Part.Mechanical));
        return VibBaseMPa * VibRole(dev.Def.Role) * (1 + 4 * worn) * (d < Loose.Length && Loose[d] >= 1 ? 1.5 : 1);
    }

    private void VibrationStep(Sim sim, double hours)
    {
        if (Loose.Length != Devices.Count) { Loose = new float[Devices.Count]; LooseCause = new int[Devices.Count]; }
        for (int d = 0; d < Devices.Count; d++)
        {
            double a = VibMPa(d);
            if (a <= 0) continue;
            var dev = Devices[d];
            float before = Loose[d];
            Loose[d] = (float)Math.Min(1.2, Loose[d] + hours / LooseHours * (a / VibBaseMPa) * (a / VibBaseMPa));
            if (before < 1 && Loose[d] >= 1)
            {
                Rattles++;
                LooseCause[d] = sim.Causes.Add(CauseKind.Wear, $"{dev.Name}의 볼트가 진동에 풀려 덜컹거린다", sim.Tick, dev.Cell, _wearCause.GetValueOrDefault(d * 4 + (int)Part.Mechanical));
                sim.Facts.Emit(sim.Tick, FactKind.Rattle, dev.Cell, (float)a, LooseCause[d], other: d);
                sim.Find<Sound>()?.Emit(sim, dev.Cell, (float)(dev.Def.Db + 10), SoundKind.Machine, LooseCause[d]);
            }
            _stc?.Vibrate(sim, dev.Cell, a, VibHz * hours * 3600, LooseCause[d] != 0 ? LooseCause[d] : _wearCause.GetValueOrDefault(d * 4 + (int)Part.Mechanical));
        }
    }

    /// <summary>⑧의 요청: 볼트를 조였다 — 풀림을 걷는다.</summary>
    public void Tighten(int d)
    {
        if (d < 0 || d >= Loose.Length) return;
        Loose[d] = 0; LooseCause[d] = 0; Tightened++;
    }

    private void SaveVibration(StateWriter w) { w.Arr(Loose); w.Arr(LooseCause); w.I64(Rattles); w.I64(Tightened); }
    private void LoadVibration(StateReader r) { Loose = r.Arr<float>(); LooseCause = r.Arr<int>(); Rattles = r.I64(); Tightened = r.I64(); }
}
