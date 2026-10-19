namespace ShipEngine;

/// <summary>
/// ⑪ 공급 사슬 — 사람 · 로봇 쪽 (docs/ENGINE.md ⑪ 공급 사슬): 갈아 낸 부품은 버리지 않고 장치 곁에 헌 부품으로 남긴다 ·
/// 주컴퓨터가 맡긴 나르기 일로 헌 부품을 정제기까지 옮긴다 (녹이는 것은 ⑪ 월드).
/// </summary>
public sealed partial class Mind
{
    public long ScrapLeft, Hauled;

    /// <summary>헌 부품 갈래 (전기 갈래면 헌 전기 부품 — 녹이면 전자 소자를 건질 수 있다).</summary>
    public byte ScrapOf(int part) => part == (int)Part.Electric ? iScrapElec : iScrap;

    /// <summary>갈아 낸 부품을 장치 곁(그 설비)에 남긴다 — 장부 '갈아 냄'.</summary>
    private void LeaveScrap(Sim sim, int dev, int part)
    {
        if (_net == null || dev < 0 || dev >= _net.Devices.Count || part < 0 || part > 3) return;
        int fu = _net.Devices[dev].Furniture;
        if (fu < 0 || fu >= L.Furniture.Count) return;
        byte item = ScrapOf(part);
        Stock[fu * ItemCount + item]++;
        sim.Book.AddItem(item, Flow.Removed, 1);
        ScrapLeft++;
    }

    /// <summary>헌 부품을 녹이는 가장 가까운 정제기 (설비 · -1 없음).</summary>
    private int NearestRefinery(int from)
    {
        int best = -1, bd = int.MaxValue;
        foreach (var f in L.Furniture)
        {
            if (f.Type != "Refinery" || f.Cells.Length == 0) continue;
            int d = Manhattan(from, f.Cells[0]);
            if (d < bd) { bd = d; best = f.Id; }
        }
        return best;
    }

    /// <summary>든 물건을 설비에 넣는다 (나르기 — Target: 설비).</summary>
    private void PutStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        int fu = st.Target;
        if (c.Holding == 255 || fu < 0 || fu >= L.Furniture.Count) { JobDone(a, t); return; }
        if (Dist(c.Cell, L.Furniture[fu].Cells[0]) > 2) { Fail(sim, a, "멀다", SnagKind.Far); return; }
        Stock[fu * ItemCount + c.Holding]++;
        c.Holding = 255; c.HoldCharge = 0;
        Hauled++;
        JobDone(a, t);
    }
}
