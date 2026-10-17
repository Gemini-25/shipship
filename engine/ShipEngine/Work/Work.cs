namespace ShipEngine;

/// <summary>일감 하나 (일감판).</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct Job
{
    public GoalKind Kind;   // Craft · Maintain · Clean
    public int Target;      // Craft: 작업대 설비 번호 · Maintain: 장치 × 4 + 부품 · Clean: 칸
    public int Arg;         // Craft: 만들 물건 번호
    public int Agent;       // 맡은 이 (-1)
    public long Until;      // 맡긴 기한
}

/// <summary>
/// ⑪ 일과 물자 (docs/ENGINE.md §4 ⑪) — 배가 스스로 살림을 꾸린다: 일감판에 할 일을 올리고 (모자란 물건 만들기 · 예방 정비 · 곰팡이 청소)
/// 로봇 먼저, 그다음 솜씨가 맞는 사람에게 맡긴다. 정제기 같은 설비는 전기를 먹고 원료를 조금씩 만든다.
/// 정비는 숨은 한계가 아니라 '쓴 시간'으로 정한다 — 어떤 고장은 미리 막고, 어떤 것은 그래도 터진다.
/// </summary>
public sealed class Work : IEngine
{
    public string Name => "일과 물자";
    public Phase Phase => Phase.Requests;
    public int Every => 25;
    public int Offset => 7;

    public const int PlanEvery = 125;          // 5분마다 일감판을 본다
    public const double MaintainAt = 0.8;      // 부품을 표준 수명의 80% 썼으면 미리 간다
    public const float HarvestAt = 0.3f;       // 작물 1m²(단마다)에 양분이 이만큼(몰 — 사흘 치) 쌓이면 거둔다
    public const double RestockBelow = 30;     // 냉장고 · 배식기의 식량이 이 몰 아래면 식량 보관함에서 채운다 (한 상자 40몰)

    private readonly Layout L;
    private readonly Tables T;
    private Mind? _mind;
    private Network? _net;
    private int[]? _pointsA, _lockersA;
    /// <summary>먹을 것을 꺼내는 곳 (냉장고 · 배식기) · 식량 보관함 (포장 식량).</summary>
    private int[] _points => _pointsA ??= L.Furniture.Where(f => f.Cells.Length > 0 && f.Type is "Fridge" or "MealDispenser").Select(f => f.Id).ToArray();
    private int[] _lockers => _lockersA ??= L.Furniture.Where(f => f.Cells.Length > 0 && T.Equip(f.Type).Sealed).Select(f => f.Id).ToArray();

    private double FoodAt(int fu)
    {
        double s = 0;
        if (_f != null && fu >= 0 && fu < L.Furniture.Count) foreach (int c in L.Furniture[fu].Cells) s += _f.Stored(T.sFood, c);
        return s;
    }
    private Life? _life;
    private Field? _f;
    public List<Job> Jobs = new();
    public float[] MakerProgress = Array.Empty<float>();   // 설비마다 만드는 중 (시간)
    public long Crafted, Maintained, Cleaned, Made, Harvested;

    public Work(Layout layout, Tables tables) { L = layout; T = tables; }

    public void Step(Sim sim, double dt)
    {
        _mind ??= sim.Find<Mind>(); _net ??= sim.Find<Network>(); _life ??= sim.Find<Life>(); _f ??= sim.Find<Field>();
        if (_mind == null) return;
        if (MakerProgress.Length != L.Furniture.Count) MakerProgress = new float[L.Furniture.Count];
        Makers(sim, dt);
        if (sim.Tick % PlanEvery == Offset) Plan(sim);
        Ledger(sim);
    }

    // ───────────────────────── ⑪ 물자 장부 ─────────────────────────

    /// <summary>처음: 지금 재고에서 이미 적힌 흐름을 빼 '흐름 0일 때의 재고'를 처음 재고로 · 그 뒤 한 시간마다 고리에 남긴다.</summary>
    private void Ledger(Sim sim)
    {
        var book = sim.Book;
        book.EnsureItems(_mind!.ItemCount);
        if (!book.Started)
        {
            for (int k = 0; k < book.Kinds; k++)
            {
                double sum = 0;
                for (int f = 0; f < Book.Flows; f++) sum += k < Book.Bulk ? book.BulkFlow[k * Book.Flows + f] : book.ItemFlow[(k - Book.Bulk) * Book.Flows + f];
                if (k < Book.Bulk) book.Start[k] = Stock(k) - sum; else book.ItemStart[k - Book.Bulk] = Stock(k) - sum;
            }
            book.Started = true; book.StartTick = sim.Tick;
        }
        if ((sim.Tick - book.StartTick) % Time.TicksPerHour < Every) book.Snapshot(sim.Tick, Stock);
    }

    /// <summary>물자 k의 지금 재고 — 덩어리: 식량(창고 · 손 · 바닥의 양분 몰) · 마실 물(물탱크) · 산소(배 공기 + 예비 공기) / 그 뒤로 물건 (개수).</summary>
    public double Stock(int k)
    {
        switch (k)
        {
            case (int)Res.Food:
            {
                double food = 0;
                if (_f != null) for (int c = 0; c < L.N; c++) food += _f.Stored(T.sFood, c);
                if (_mind != null)
                {
                    foreach (var a in _mind.Agents) if (_mind.IsFood(a.C.Holding)) food += a.C.HoldCharge;
                    foreach (var x in _mind.Loose) if (_mind.IsFood(x.Item)) food += x.Charge;
                }
                return food;
            }
            case (int)Res.Water: return _net?.WaterTank ?? 0;
            case (int)Res.O2: return (_f?.Total(T.gO2) ?? 0) + (_net?.AirReserve ?? 0) * 0.21;
            default: return _mind == null || _mind.IsFood(k - Book.Bulk) ? 0 : _mind.CountAll(k - Book.Bulk); // 손에 든 식량은 식량으로 (위)
        }
    }

    /// <summary>장부에 없는 몫 (지금 재고 − 처음 재고 − 적은 흐름): 불 · 새어 나감 · 그 밖의 길.</summary>
    public double Other(Sim sim, int k)
    {
        var book = sim.Book;
        if (!book.Started) return 0;
        var (i, o) = book.Totals(k);
        double start = k < Book.Bulk ? book.Start[k] : book.ItemStart[k - Book.Bulk];
        return Stock(k) - start - (i - o);
    }

    /// <summary>며칠 버티나: (만드는 것까지 친 날 수 · 만들지 못하면 날 수) — 줄지 않으면 무한.</summary>
    public (double days, double noMake) DaysLeft(Sim sim, int k)
    {
        var (use, make) = sim.Book.PerDay(k, sim.Tick);
        double stock = Stock(k);
        double net = use - make;
        return (net > 1e-9 ? stock / net : double.PositiveInfinity, use > 1e-9 ? stock / use : double.PositiveInfinity);
    }

    /// <summary>장부 한 장 (화면 · 일지 · 실험실) — 재고 · 하루에 쓰는 양 · 만드는 양 · 며칠 버티나.</summary>
    public List<string> Report(Sim sim)
    {
        var lines = new List<string>();
        string D(double d) => double.IsInfinity(d) ? "줄지 않음" : d > 999 ? "999일 넘게" : $"{d:0.0}일";
        for (int k = 0; k < Book.Bulk; k++)
        {
            var (use, make) = sim.Book.PerDay(k, sim.Tick);
            var (days, noMake) = DaysLeft(sim, k);
            double st = Stock(k);
            string line = k switch
            {
                (int)Res.Food => $"식량 {st / Mind.MealMol:0}끼니 · 하루에 먹는 양 {use / Mind.MealMol:0.0}끼니 · 거두는 양 {make / Mind.MealMol:0.0}끼니",
                (int)Res.Water => $"마실 물 {st * 0.018:0}L · 하루에 쓰는 양 {use * 0.018:0.0}L · 되찾는 양 {make * 0.018:0.0}L",
                _ => $"산소 {st * 0.032:0}kg · 하루에 숨 쉬는 양 {use * 0.032:0.0}kg · 만드는 양 {make * 0.032:0.0}kg",
            };
            lines.Add($"{line} → {D(days)} (만들지 못하면 {D(noMake)})");
        }
        foreach (string name in new[] { "수리 꾸러미", "봉합 꾸러미", "소화기", "원료" })
        {
            int item = _mind!.ItemIndex(name);
            if (item < 0) continue;
            int k = Book.Bulk + item;
            var (use, make) = sim.Book.PerDay(k, sim.Tick);
            lines.Add($"{name} {Stock(k):0}개 · 하루에 쓰는 양 {use:0.0} · 만드는 양 {make:0.0}");
        }
        return lines;
    }

    // ───────────────────────── 저절로 만드는 설비 ─────────────────────────

    private void Makers(Sim sim, double dt)
    {
        if (_net == null) return;
        foreach (var dev in _net.Devices)
        {
            if (!T.People.Makers.TryGetValue(dev.Type, out var mk)) continue;
            int item = _mind!.ItemIndex(mk.Item);
            if (item < 0) continue;
            int fu = dev.Furniture;
            int have = _mind.Stock[fu * _mind.ItemCount + item];
            // 켜고 끄기: 다 차면 끄고 · 모자라고 전력에 여유(80% 안)가 있으면 켠다
            if (have >= mk.Max) { if (_net.On[dev.Id] != 0 && dev.Def.Kw > 3) _net.SetOn(dev.Id, false); continue; }
            if (_net.On[dev.Id] == 0 && _net.Demanded + dev.Def.Kw < 0.8 * _net.Capacity()) _net.SetOn(dev.Id, true);
            if (!_net.Working(dev.Id)) continue;
            MakerProgress[fu] += (float)(dt / 3600);
            if (MakerProgress[fu] >= mk.Hours) { MakerProgress[fu] = 0; _mind.Stock[fu * _mind.ItemCount + item]++; Made++; sim.Book.AddItem(item, Flow.Made, 1); }
        }
    }

    // ───────────────────────── 일감판 ─────────────────────────

    private void Plan(Sim sim)
    {
        var mind = _mind!;
        long now = sim.Tick;
        // 끝났거나 기한이 지난 일은 내린다
        Jobs.RemoveAll(j => j.Until < now || Done(j));
        // 일감판은 주컴퓨터가 센서 · 재고 기록으로 돌린다 — 주컴퓨터가 멈추면 새 일을 올리지 못한다 (맡긴 일은 하던 대로 · 바닥의 물이나 곰팡이를 아무도 모른다)
        var computer = sim.Find<ShipMind>();
        if (computer != null && !computer.Online) return;
        // 모자란 물건 만들기
        foreach (var (name, r) in T.People.Recipes)
        {
            int item = mind.ItemIndex(name);
            if (item < 0 || mind.TotalOf(item) >= r.Keep || Jobs.Any(j => j.Kind == GoalKind.Craft && j.Arg == item)) continue;
            if (!r.Needs.All(kv => mind.TotalOf(mind.ItemIndex(kv.Key)) >= kv.Value)) continue; // 재료가 없다
            int bench = L.Furniture.FindIndex(f => f.Type == r.At && f.Cells.Length > 0);
            if (bench < 0) continue;
            Post(sim, new Job { Kind = GoalKind.Craft, Target = bench, Arg = item }, L.Furniture[bench].Cells[0], person => person.C.SkRepair >= 0.4f);
        }
        // 예방 정비: 표준 수명의 80%를 쓴 부품 (숨은 한계는 모른다)
        if (_net != null)
            foreach (var dev in _net.Devices)
                for (int p = 0; p < 4; p++)
                {
                    double life = dev.Life((Part)p);
                    int k = dev.Id * 4 + p;
                    if (life <= 0 || _net.Broken[k] != 0 || _net.Wear[k] < MaintainAt * life) continue;
                    if (Jobs.Any(j => j.Kind == GoalKind.Maintain && j.Target == k)) continue;
                    if (mind.TotalOf(mind.iRepair) <= 0) continue; // 갈아 끼울 부품(수리 꾸러미)이 없다
                    Post(sim, new Job { Kind = GoalKind.Maintain, Target = k }, dev.Cell, person => person.C.SkRepair >= 0.5f);
                }
        // 거두기: 다 자란 작물 (로봇 먼저 · 사람은 기르는 솜씨가 조금이라도 있으면)
        if (_life != null)
            for (int k = 0; k < _life.Crops.Count; k++)
            {
                var cr = _life.Crops[k];
                if (cr.Biomass < HarvestAt * _life.CropArea(_net, k) || Jobs.Any(j => j.Kind == GoalKind.Harvest && j.Target == k)) continue;
                Post(sim, new Job { Kind = GoalKind.Harvest, Target = k }, cr.Cell, person => person.C.SkFarm >= 0.3f);
            }
        // 식량 나르기: 냉장고 · 배식기가 비어 가면 가까운 식량 보관함에서 (로봇 먼저 · 사람은 누구나)
        foreach (int point in _points)
        {
            if (FoodAt(point) >= RestockBelow || Jobs.Any(j => j.Kind == GoalKind.Restock && j.Target == point)) continue;
            int cell = L.Furniture[point].Cells[0], best = -1, bd = int.MaxValue;
            foreach (int lk in _lockers)
            {
                if (FoodAt(lk) < 1) continue;
                int d = mind.Dist(cell, L.Furniture[lk].Cells[0]);
                if (d < bd) { bd = d; best = lk; }
            }
            if (best >= 0) Post(sim, new Job { Kind = GoalKind.Restock, Target = point, Arg = best }, L.Furniture[best].Cells[0], person => true);
        }
        // 청소: 방마다 가장 지저분한 곳 하나 — 곰팡이 · 고인 물 (물 고인 바닥 밑 전선은 합선된다)
        foreach (var room in L.Rooms)
        {
            int worst = -1; double wm = 0;
            foreach (int c in room.Cells)
            {
                double dirt = (_life != null && _life.Mold[c] > 0.3f ? _life.Mold[c] : 0) + (_f != null && _f.Stored(T.sWater, c) > 5 ? _f.Stored(T.sWater, c) / 100 + 0.3 : 0);
                if (dirt > wm) { wm = dirt; worst = c; }
            }
            if (worst < 0 || Jobs.Any(j => j.Kind == GoalKind.Clean && L.Room[j.Target] == room.Id)) continue;
            Post(sim, new Job { Kind = GoalKind.Clean, Target = worst }, worst, person => true);
        }
    }

    private bool Done(in Job j) => j.Kind switch
    {
        GoalKind.Craft => _mind!.TotalOf(j.Arg) >= (T.People.Recipes.TryGetValue(_mind.ItemNames[j.Arg], out var r) ? r.Keep : 0),
        GoalKind.Maintain => _net == null || _net.Wear[j.Target] < MaintainAt * _net.Devices[j.Target / 4].Life((Part)(j.Target % 4)) * 0.5,
        GoalKind.Clean => (_life == null || _life.Mold[j.Target] < 0.05f) && (_f == null || _f.Stored(T.sWater, j.Target) < 2),
        GoalKind.Harvest => _life == null || j.Target >= _life.Crops.Count || _life.Crops[j.Target].Biomass < HarvestAt * 0.5f * _life.CropArea(_net, j.Target),
        GoalKind.Restock => FoodAt(j.Target) >= RestockBelow * 2 || FoodAt(j.Arg) < 1,
        _ => true,
    };

    /// <summary>맡길 이를 찾는다: 놀고 있는 로봇이 먼저 (가까운 순) — 없으면 깨어 있고 바쁘지 않은 솜씨 맞는 사람.</summary>
    private void Post(Sim sim, Job j, int cell, Func<Agent, bool> canPerson)
    {
        var mind = _mind!;
        int best = -1, bd = int.MaxValue;
        foreach (var a in mind.Agents)
        {
            if (a.C.Up == 0 || a.C.AssignedUntil > sim.Tick || a.C.Kind == AgentKind.Drone) continue;
            if (Busy(a.C.Goal)) continue;
            if (!a.IsPerson && a.C.Battery < 0.4f) continue;
            if (a.IsPerson && (!canPerson(a) || a.C.Goal != GoalKind.Work)) continue; // 사람은 일하는 시간에만
            int d = mind.Dist(a.C.Cell, cell) + (a.IsPerson ? 1000 : 0); // 로봇 먼저
            if (d < bd) { bd = d; best = a.Id; }
        }
        if (best < 0) return;
        j.Agent = best; j.Until = sim.Tick + Time.Hours(2);
        mind.AssignJob(best, j.Kind, j.Target, j.Arg, j.Until);
        Jobs.Add(j);
    }

    private static bool Busy(GoalKind g) => g is GoalKind.FightFire or GoalKind.Rescue or GoalKind.Patch or GoalKind.Brace or GoalKind.Repair
        or GoalKind.Evacuate or GoalKind.Flee or GoalKind.Panic or GoalKind.Down or GoalKind.Craft or GoalKind.Maintain or GoalKind.Clean or GoalKind.Harvest or GoalKind.Restock;

    // ───────────────────────── 찍기 ─────────────────────────

    public void Save(StateWriter w)
    {
        w.List(Jobs); w.Arr(MakerProgress);
        w.I64(Crafted); w.I64(Maintained); w.I64(Cleaned); w.I64(Made); w.I64(Harvested);
    }

    public void Load(StateReader r)
    {
        Jobs = r.List<Job>(); MakerProgress = r.Arr<float>();
        Crafted = r.I64(); Maintained = r.I64(); Cleaned = r.I64(); Made = r.I64(); Harvested = r.I64();
    }
}
