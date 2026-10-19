namespace ShipEngine;

/// <summary>
/// ⑩ 주컴퓨터의 공급 사슬 판단 (⑪ 공급 사슬 (다)(라) · docs/ENGINE.md): 원료가 모자라면 무엇을 먼저 만들지 고른다 —
/// ① 망가져 그 갈래 부품을 기다리는 장치 (생명 유지가 먼저) ② 곧 갈 때가 된 부품 ③ 둘 수만큼 채우기. 위에서부터 원료를 떼어 두고 맡기며 ·
/// 밀린 것은 원인 마디 · 방송 한 번. 장치 곁에 남은 헌 부품은 정제기로 나르는 일을 올린다 (녹이는 것은 ⑪ 월드).
/// </summary>
public sealed partial class ShipMind
{
    public string SupplyNote = "";      // 마지막으로 알린 '먼저 만든다' (같은 판단은 다시 알리지 않는다)
    public int SupplyCause;             // 그 판단의 원인 마디
    public long SupplyDeferred;         // 원료가 모자라 밀린 수

    /// <summary>그 갈래 부품이 배에 있다고 아나 (설비 안 · 손 · 로봇 부품 주머니 — 주컴퓨터는 재고 기록 · 로봇의 보고로 안다).</summary>
    private bool SpareKnown(int part)
    {
        var mind = _mind!;
        if (mind.TotalOf(mind.SpareOf(part)) > 0) return true;
        foreach (var a in mind.Agents) if (!a.IsPerson && a.C.Up != 0 && Mind.InPouch(a.C, part)) return true;
        return false;
    }

    /// <summary>생명 유지 장치 (산소 · 이산화탄소 · 냉각 · 원자로 · 물) — 부품을 먼저 받는다.</summary>
    private static bool LifeSupport(Device d) => d.Def.Role is "o2gen" or "scrubber" or "pump" or "reactor" or "recycler";

    private readonly struct CraftWant
    {
        public readonly int Item, Rank, Order; public readonly RecipeDef R; public readonly string Why; public readonly int Cause;
        public CraftWant(int item, RecipeDef r, int rank, int order, string why, int cause) { Item = item; R = r; Rank = rank; Order = order; Why = why; Cause = cause; }
    }

    /// <summary>만들 것 고르기: 차례를 매겨 원료를 떼어 두고 맡긴다 (맡길 이가 없으면 떼지 않는다).</summary>
    private void PlanCrafts(Sim sim)
    {
        var mind = _mind!;
        // 남은 재료 (이미 맡긴 만들기의 몫은 뺀다 — 아직 꺼내 쓰지 않았다)
        var budget = new Dictionary<int, int>();
        int Left(int item) => budget.TryGetValue(item, out int n) ? n : budget[item] = mind.TotalOf(item);
        foreach (var j in Jobs)
            if (j.Kind == GoalKind.Craft && T.People.Recipes.TryGetValue(mind.ItemNames[j.Arg], out var jr))
                foreach (var (need, n) in jr.Needs) { int ni = mind.ItemIndex(need); budget[ni] = Left(ni) - n; }
        var wants = new List<CraftWant>();
        int order = 0;
        foreach (var (name, r) in T.People.Recipes)
        {
            order++;
            int item = mind.ItemIndex(name);
            if (item < 0 || Jobs.Any(j => j.Kind == GoalKind.Craft && j.Arg == item)) continue;
            int have = mind.TotalOf(item);
            int part = Array.IndexOf(mind.iSpare, (byte)item);
            int rank = have < r.Keep ? 3 : -1;
            string why = "둘 수만큼 채운다";
            int cause = 0;
            if (part >= 0 && have == 0 && !SpareKnown(part) && _net != null)
            {
                // ① 그 갈래 부품을 기다리는 망가진 장치 (생명 유지가 먼저) · ② 갈 때가 된 부품
                Device? waiting = null, due = null;
                foreach (var d in _net.Devices)
                {
                    if (!d.Has((Part)part)) continue;
                    int k = d.Id * 4 + part;
                    if (_net.Broken[k] != 0) { if (waiting == null || LifeSupport(d) && !LifeSupport(waiting)) waiting = d; }
                    else if (_net.Wear[k] >= Work.MaintainAt * d.Life((Part)part)) due ??= d;
                }
                if (waiting != null) { rank = LifeSupport(waiting) ? 0 : 1; why = $"{Ko.I(waiting.Name)} 기다린다"; cause = _net.PartCause(waiting.Id, (Part)part); }
                else if (due != null) { rank = 2; why = $"{due.Name}의 {Device.PartName((Part)part)} 부품을 갈 때가 됐다"; }
            }
            if (rank < 0) continue;
            wants.Add(new CraftWant(item, r, rank, order, why, cause));
        }
        wants.Sort((a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : a.Order.CompareTo(b.Order));
        CraftWant? first = null;
        foreach (var w in wants)
        {
            bool enough = true;
            foreach (var (need, n) in w.R.Needs) if (Left(mind.ItemIndex(need)) < n) { enough = false; break; }
            if (!enough)
            {
                // 원료가 모자라 밀렸다 — 앞선 것이 원료를 먼저 가져갔으면 그 판단을 알린다
                if (first is CraftWant f && f.Rank < w.Rank && w.R.Needs.Keys.Any(k => f.R.Needs.ContainsKey(k))) Defer(sim, f, w);
                continue;
            }
            int bench = L.Furniture.FindIndex(fu => fu.Type == w.R.At && fu.Cells.Length > 0);
            if (bench < 0) continue;
            // 맡길 이가 지금 없어도 그 몫의 원료는 떼어 둔다 (차례는 판단이다 — 다음 일감판에 맡긴다)
            Post(sim, new Job { Kind = GoalKind.Craft, Target = bench, Arg = w.Item }, L.Furniture[bench].Cells[0], person => person.C.SkRepair >= 0.4f, stalled: w.Rank <= 1);
            foreach (var (need, n) in w.R.Needs) { int ni = mind.ItemIndex(need); budget[ni] = Left(ni) - n; }
            first ??= w;
        }
    }

    /// <summary>원료가 모자라 뒤의 것을 밀었다 — 원인 마디 · 방송 (같은 판단은 한 번).</summary>
    private void Defer(Sim sim, CraftWant first, CraftWant later)
    {
        SupplyDeferred++;
        var mind = _mind!;
        string note = $"원료가 모자라 {Ko.Eul(mind.ItemNames[first.Item])} 먼저 만든다 — {first.Why}";
        if (note == SupplyNote) return;
        SupplyNote = note;
        SupplyCause = sim.Causes.Add(CauseKind.Computer, $"{note} ({Ko.Eun(mind.ItemNames[later.Item])} 미룬다)", sim.Tick, -1, first.Cause);
        Say(sim, $"{note} ({Ko.Eun(mind.ItemNames[later.Item])} 미룬다)");
    }

    /// <summary>헌 부품 나르기: 정제기가 아닌 설비에 놓인 헌 부품마다 (설비 하나에 일 하나 · 정제기가 없으면 올리지 않는다).</summary>
    private void PlanHauls(Sim sim)
    {
        var mind = _mind!;
        int refinery = L.Furniture.FindIndex(f => f.Type == "Refinery" && f.Cells.Length > 0);
        if (refinery < 0) return;
        foreach (byte item in new[] { mind.iScrapElec, mind.iScrap })
            for (int fu = 0; fu < L.Furniture.Count; fu++)
            {
                if (L.Furniture[fu].Type == "Refinery" || L.Furniture[fu].Cells.Length == 0 || mind.Stock[fu * mind.ItemCount + item] <= 0) continue;
                int arg = fu * 256 + item;
                if (Jobs.Any(j => j.Kind == GoalKind.Haul && j.Arg == arg)) continue;
                int best = refinery, bd = int.MaxValue;
                for (int r = 0; r < L.Furniture.Count; r++)
                    if (L.Furniture[r].Type == "Refinery" && L.Furniture[r].Cells.Length > 0 && mind.Dist(L.Furniture[fu].Cells[0], L.Furniture[r].Cells[0]) is int d && d < bd) { bd = d; best = r; }
                Post(sim, new Job { Kind = GoalKind.Haul, Target = best, Arg = arg }, L.Furniture[fu].Cells[0], person => true);
            }
    }

    private void SaveParts(StateWriter w) { w.Str(SupplyNote); w.I32(SupplyCause); w.I64(SupplyDeferred); }
    private void LoadParts(StateReader r) { SupplyNote = r.Str() ?? ""; SupplyCause = r.I32(); SupplyDeferred = r.I64(); }
}
