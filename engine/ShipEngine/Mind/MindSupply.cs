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

    /// <summary>④가 부서졌다고 한 설비인가 (틀부터 다시 지어야 한다 — 부품만 끼우면 다시 부서진 것으로 돈다).</summary>
    public bool Smashed(int dev) => _net != null && _st != null && dev >= 0 && dev < _net.Devices.Count && _net.Devices[dev].Furniture is int fu && fu >= 0 && fu < _st.FurnBroken.Length && _st.FurnBroken[fu] != 0;

    /// <summary>다시 짓기 계획: 외장 틀을 들고 (작업대) 장치로 가서 틀을 짠다 (없으면 false — 주컴퓨터가 만들기를 맡긴다).</summary>
    private bool PlanRebuild(Agent a, int dev, int here, List<PlanStep> plan)
    {
        ref var c = ref a.C;
        if (c.Holding != iFrame)
        {
            FreeHands(a, ref here, plan, false);
            int src = NearestStock(here, iFrame);
            if (src < 0) return false;
            plan.Add(Go(L.Furniture[src].Cells[0], 1));
            plan.Add(new PlanStep { Kind = StepKind.Take, Target = src, Arg = iFrame });
        }
        plan.Add(Go(_net!.Devices[dev].Cell, 1));
        plan.Add(new PlanStep { Kind = StepKind.Rebuild, Target = dev });
        return true;
    }

    public long Rebuilt;

    /// <summary>틀을 새로 짠다 (사람 90분 × (1.6 − 솜씨) · 로봇 60분) — 구조(④)에 요청 · 원인 마디 · 사실 '고쳤다'.</summary>
    private void RebuildStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        int dev = st.Target;
        if (_net == null || _st == null || !Smashed(dev)) { Next(a, t); return; }
        if (Dist(c.Cell, _net.Devices[dev].Cell) > 2) { Fail(sim, a, "멀다", SnagKind.Far); return; }
        if (c.Holding != iFrame) { Fail(sim, a, "외장 틀이 없다", SnagKind.NoItem, c.Cell, iFrame); return; }
        Activity(a, "hard");
        WorkNoise(sim, a, (t - c.StepStart) % 6 < 3 ? SoundKind.Hammer : SoundKind.Weld, 84);   // 다시 짓기: 망치 · 용접
        double minutes = a.IsPerson ? P.Items["외장 틀"].Minutes * (1.6 - c.SkRepair) : 60;
        if (t - c.StepStart < Time.Minutes(minutes)) return;
        int fu = _net.Devices[dev].Furniture;
        int broke = _st.FurnCause[fu];
        _st.Rebuild(fu);
        UseHeld(sim, a);
        Rebuilt++;
        int cause = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, $"{Ko.I(NameOf(a.Id))} 부서진 {_net.Devices[dev].Name}의 틀을 새로 짰다 — 이제 부품을 끼운다", t, _net.Devices[dev].Cell, broke, 0, a.Id);
        sim.Facts.Emit(t, FactKind.Repaired, _net.Devices[dev].Cell, 9, cause, a.Id, dev);
        if (a.IsPerson) c.SkRepair = Math.Min(1, c.SkRepair + 0.01f * Practice(c, "고치기") * UseSkill(a, 0, t));
        Next(a, t);
    }

    /// <summary>든 물건을 설비에 넣는다 (나르기 — Target: 설비).</summary>
    private void PutStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        int fu = st.Target;
        bool stash = st.Arg >= 1;   // 1: 돌려놓기 · 손 비우기 (맡긴 일의 끝이 아니다) · 2: 채운 것 · 주운 것을 돌려놓기 (채우기 · 줍기 일감의 끝)
        if (c.Holding == 255 || fu < 0 || fu >= L.Furniture.Count) { if (stash) Next(a, t); else JobDone(a, t); return; }
        if (Dist(c.Cell, L.Furniture[fu].Cells[0]) > 2) { Fail(sim, a, "멀다", SnagKind.Far); return; }
        StoreHeld(fu, a);
        if (stash) { Stashed++; if (st.Arg == 2) JobDone(a, t); else Next(a, t); return; }
        Hauled++;
        JobDone(a, t);
    }
}
