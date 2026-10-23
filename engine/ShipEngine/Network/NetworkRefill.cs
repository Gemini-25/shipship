namespace ShipEngine;

/// <summary>
/// 쓴 소화기 · 우주복 · 마스크 채우기 (판단이 요청하는 월드 몫 — docs/MIND.md 3단 · 함장 결정 16 (가)): 채우는 것은 배에 있는 것에서 온다 —
/// 물 소화기는 물탱크 물 (장부 '소화기에 담았다') · 이산화탄소 소화기는 제거기가 모아 둔 이산화탄소 · 우주복 · 마스크는 그 방 공기의 산소 (보관함의 산소 농축기 — 압력 변동 흡착).
/// 없는 데서 생기지 않는다 (예전엔 보관함에서 꺼내는 순간 가득 찼다).
/// </summary>
public sealed partial class Network
{
    /// <summary>물 소화기를 물탱크 물로 채운다 — 채운 몰.</summary>
    public double FillExtinguisher(Sim sim, double mol)
    {
        double take = Math.Min(Math.Max(0, mol), Math.Max(0, WaterTank));
        if (take <= 0) return 0;
        WaterTank -= take; sim.Book.Add(Res.Water, Flow.Refill, -take);
        return take;
    }

    /// <summary>이산화탄소 소화기를 모아 둔 이산화탄소로 채운다 — 채운 몰.</summary>
    public double FillCO2(double mol)
    {
        double take = Math.Min(Math.Max(0, mol), Math.Max(0, CO2Store));
        CO2Store -= take;
        return take;
    }

    /// <summary>방 공기의 산소를 걸러 우주복 · 마스크에 채운다 — 그 방 산소의 5분의 1까지 (방이 숨쉴 만하게 남는다) · 채운 몰.</summary>
    public double FillO2(Sim sim, int cell, double mol)
    {
        int room = cell >= 0 && cell < L.N ? L.Room[cell] : -1;
        var cells = room >= 0 && room < L.Rooms.Count && L.Rooms[room].Cells.Length > 0 ? L.Rooms[room].Cells : new[] { cell };
        double total = 0;
        foreach (int c in cells) total += F.Gas(T.gO2, c);
        double want = Math.Min(Math.Max(0, mol), 0.2 * total);
        if (want <= 1e-9 || total <= 1e-12) return 0;
        double frac = want / total, got = 0;
        foreach (int c in cells) { double g = F.Gas(T.gO2, c); if (g > 0) got += F.RemoveGas(c, T.gO2, g * frac); }
        return got;
    }
}
