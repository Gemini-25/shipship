namespace ShipEngine;

/// <summary>
/// ⑥ 계산 장치의 칩 (판단 이관 (사) — 몸 · 기계의 손상은 월드가 · docs/ENGINE.md): 주컴퓨터는 장치다 — 그 몸의 열은 세상이 계산한다.
/// <para>계산 장치(MainComputer)마다 빨아들이는 공기 = 그 방 기체의 평균 온도 (제 칸은 제 열로 늘 뜨겁다) · 12초마다 잰다.
/// 40℃ 넘으면 칩이 스스로 느려지고 (열 조절 — 37℃ 아래로 풀린다) · 60℃ 넘으면 열 보호 장치가 끈다 (45℃ 아래로 식으면 다시 켠다).
/// 끌 때 원인 마디(그 방을 데운 원인에 잇는다) + 사실 '과열'. 주컴퓨터(⑩)는 이것을 느끼고 대응만 한다 (센서 읽기가 드물어진다 · 꺼지면 아무것도 못 한다).</para>
/// </summary>
public sealed partial class Network
{
    public const int ChipEvery = 5;                       // 12초
    public const double ChipSlowK = 313, ChipFastK = 310, ChipTripK = 333, ChipBackK = 318;

    // ── 상태 (찍는다 · 장치마다) ──
    public byte[] ChipSlow = Array.Empty<byte>(), ChipTrip = Array.Empty<byte>();
    public float[] ChipK = Array.Empty<float>();
    public int[] ChipTripCause = Array.Empty<int>();
    public long ChipSlows, ChipTrips;

    private int[]? _chips;

    private void EnsureChips()
    {
        if (ChipSlow.Length != Devices.Count)
        {
            ChipSlow = new byte[Devices.Count]; ChipTrip = new byte[Devices.Count];
            ChipK = new float[Devices.Count]; ChipTripCause = new int[Devices.Count];
        }
        _chips ??= Devices.Where(d => d.Type == "MainComputer").Select(d => d.Id).ToArray();
    }

    /// <summary>계산 장치가 열 보호로 꺼져 있지 않고 돈다.</summary>
    public bool ChipUp(int d) => Working(d) && (d >= ChipTrip.Length || ChipTrip[d] == 0);

    /// <summary>방 공기 평균 온도 (기체가 있는 칸 · 몰로 무게) — 칸이 다 비었으면 그 칸 온도.</summary>
    private double IntakeAt(int cell)
    {
        double temp = F.Temp(cell);
        int room = L.Room[cell];
        if (room < 0) return temp;
        double n = 0, nt = 0;
        foreach (int c in L.Rooms[room].Cells) if (F.Pressure(c) > 5) { double m = F.Moles(c); n += m; nt += m * F.Temp(c); }
        return n > 0 ? nt / n : temp;
    }

    private void ChipStep(Sim sim)
    {
        EnsureChips();
        if (sim.Tick % ChipEvery != 0) return;
        foreach (int d in _chips!)
        {
            if (!Working(d)) { ChipSlow[d] = 0; continue; }   // 전기가 없거나 망가졌다 — 식는 것만 잰다
            int cell = Devices[d].Cell;
            double k = IntakeAt(cell);
            ChipK[d] = (float)k;
            if (ChipTrip[d] == 0 && k > ChipTripK)
            {
                ChipTrip[d] = 1; ChipTrips++;
                ChipTripCause[d] = sim.Causes.Add(CauseKind.Heat, $"{Devices[d].Name} 칩이 뜨거워 열 보호 장치가 껐다 — 방 공기 {k - 273:0}℃", sim.Tick, cell, HeatSource(sim.Find<Chemistry>(), cell));
                sim.Facts.Emit(sim.Tick, FactKind.Overheat, cell, (float)k, ChipTripCause[d], other: d);
            }
            else if (ChipTrip[d] != 0 && k < ChipBackK) { ChipTrip[d] = 0; ChipTripCause[d] = 0; }
            byte slow = (byte)(ChipTrip[d] == 0 && (k > ChipSlowK || ChipSlow[d] != 0 && k > ChipFastK) ? 1 : 0);
            if (slow != 0 && ChipSlow[d] == 0) ChipSlows++;
            ChipSlow[d] = slow;
        }
    }

    private void SaveChips(StateWriter w) { w.Arr(ChipSlow); w.Arr(ChipTrip); w.Arr(ChipK); w.Arr(ChipTripCause); w.I64(ChipSlows); w.I64(ChipTrips); }
    private void LoadChips(StateReader r) { ChipSlow = r.Arr<byte>(); ChipTrip = r.Arr<byte>(); ChipK = r.Arr<float>(); ChipTripCause = r.Arr<int>(); ChipSlows = r.I64(); ChipTrips = r.I64(); }
}
