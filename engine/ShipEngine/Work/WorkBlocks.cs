namespace ShipEngine;

/// <summary>
/// ⑪ 막힌 마디 찾기 (깊이 바퀴 2). 모자란 것마다 생산 사슬을 거슬러 첫 막힌 곳을 찾는다 (5분마다 · 일감판과 함께) —
/// 만들 물건(둘 수만큼 없고 만드는 이도 없다) ← 작업대 · 재료 ← 재료를 만드는 설비(없다 · 망가졌다 · 꺼 두었다) · 만들 솜씨 /
/// 마실 물 · 식량 · 산소(30일을 못 버틴다) ← 재활용 장치 · 거르개 · 거두는 양 · 산소 발생기 · 물.
/// 찾은 것은 주컴퓨터에 알린다 (요청 — 방송 · 일지). 막힌 곳이 '전력 여유'면 그 설비는 80% 대신 95%까지 켠다 (모자람이 급하면 덜 급한 여유를 쓴다).
/// </summary>
public sealed partial class Work
{
    public readonly List<string> Blocks = new();
    private readonly HashSet<int> _powerFirst = new();   // 막힌 곳이 전력 여유인 설비 (95%까지 켠다)

    private void FindBlocks(Sim sim)
    {
        var mind = _mind!;
        Blocks.Clear(); _powerFirst.Clear();
        // 만들 물건: 둘 수만큼 없는데 맡은 이도 없다
        foreach (var (name, r) in T.People.Recipes)
        {
            int item = mind.ItemIndex(name);
            if (item < 0 || mind.TotalOf(item) >= r.Keep || Jobs.Any(j => j.Kind == GoalKind.Craft && j.Arg == item)) continue;
            string? why = null;
            int bench = L.Furniture.FindIndex(f => f.Type == r.At && f.Cells.Length > 0);
            int bdev = bench >= 0 && _net != null ? _net.Devices.FindIndex(d => d.Furniture == bench) : -1;
            if (bench < 0) why = $"{T.Equip(r.At).Name}가 없다";
            else if (bdev >= 0 && (_net!.IsBroken(bdev, Part.Electric) || _net.IsBroken(bdev, Part.Mechanical))) why = $"{_net.Devices[bdev].Name}가 망가졌다";
            else
                foreach (var (need, amount) in r.Needs)
                {
                    int ni = mind.ItemIndex(need);
                    if (ni >= 0 && mind.TotalOf(ni) >= amount) continue;
                    why = $"{need}가 없다 ← {MakerWhy(need)}";
                    break;
                }
            if (why == null && !mind.Agents.Any(a => a.C.Up != 0 && (!a.IsPerson && a.C.Kind != AgentKind.Drone || a.IsPerson && a.C.SkRepair >= 0.4f))) why = "만들 솜씨가 있는 이가 없다";
            if (why != null) Blocks.Add($"{name} ← {why}");
        }
        // 마실 물 · 식량 · 산소: 만드는 것까지 쳐서 30일을 못 버틴다
        var (wd, _) = DaysLeft(sim, (int)Res.Water);
        if (wd < 30) Blocks.Add($"마실 물 {wd:0}일 치 ← {WaterWhy()}");
        var (fd, _) = DaysLeft(sim, (int)Res.Food);
        if (fd < 30) Blocks.Add($"식량 {fd:0}일 치 ← {FoodWhy(sim)}");
        var (od, _) = DaysLeft(sim, (int)Res.O2);
        if (od < 30) Blocks.Add($"산소 {od:0}일 치 ← {O2Why()}");
        sim.Find<ShipMind>()?.ReportBlocks(sim, Blocks);
    }

    /// <summary>재료를 만드는 설비 쪽의 까닭: 없다 · 망가졌다 · 꺼 두었다(전력 여유) · 만드는 중.</summary>
    private string MakerWhy(string item)
    {
        if (_net == null) return "만드는 설비가 없다";
        string? off = null, making = null, broken = null;
        foreach (var dev in _net.Devices)
        {
            if (!T.People.Makers.TryGetValue(dev.Type, out var mk) || mk.Item != item) continue;
            if (_net.IsBroken(dev.Id, Part.Electric) || _net.IsBroken(dev.Id, Part.Mechanical)) { broken ??= $"{dev.Name}가 망가졌다"; continue; }
            if (_net.On[dev.Id] == 0) { off ??= $"{dev.Name}를 꺼 두었다 (전력 여유가 없다)"; _powerFirst.Add(dev.Id); continue; }
            int fu = dev.Furniture;
            making ??= $"{dev.Name}가 만드는 중 ({(fu >= 0 && fu < MakerProgress.Length ? MakerProgress[fu] : 0):0.0}/{mk.Hours:0}시간)";
        }
        return making ?? off ?? broken ?? $"{item}를 만드는 설비가 없다";
    }

    private string WaterWhy()
    {
        if (_net == null) return "물을 되찾는 장치가 없다";
        string? worn = null, off = null, broken = null;
        bool any = false;
        foreach (var dev in _net.Devices)
        {
            if (dev.Def.Role != "recycler") continue;
            any = true;
            if (_net.IsBroken(dev.Id, Part.Mechanical) || _net.IsBroken(dev.Id, Part.Electric)) { broken ??= $"{dev.Name}가 망가졌다"; continue; }
            if (_net.IsBroken(dev.Id, Part.Filter)) { broken ??= $"{dev.Name}의 거르개가 망가졌다"; continue; }
            if (_net.On[dev.Id] == 0) { off ??= $"{dev.Name}를 꺼 두었다"; continue; }
            double w = _net.WearFraction(dev.Id, Part.Filter);
            if (w > 0.6) worn ??= $"{dev.Name}의 거르개가 낡았다 (회수율 {(Network.WaterRecovery - 0.09 * Math.Min(1, w)) * 100:0}%)";
        }
        return !any ? "물을 되찾는 장치가 없다" : broken ?? off ?? worn ?? "마시는 양이 되찾는 양보다 많다";
    }

    private string FoodWhy(Sim sim)
    {
        var (use, make) = sim.Book.PerDay((int)Res.Food, sim.Tick);
        if (_life == null || _life.Crops.Count == 0) return "기르는 작물이 없다 — 실어 온 식량뿐";
        return $"거두는 양(하루 {make / Mind.MealMol:0.0}끼)이 먹는 양(하루 {use / Mind.MealMol:0.0}끼)에 못 미친다";
    }

    private string O2Why()
    {
        if (_net == null) return "산소 발생기가 없다";
        string? off = null, broken = null; bool any = false;
        foreach (var dev in _net.Devices)
        {
            if (dev.Def.Role != "o2gen") continue;
            any = true;
            if (_net.IsBroken(dev.Id, Part.Electric) || _net.IsBroken(dev.Id, Part.Mechanical)) { broken ??= $"{dev.Name}가 망가졌다"; continue; }
            if (_net.On[dev.Id] == 0) off ??= $"{dev.Name}를 꺼 두었다";
        }
        if (!any) return "산소 발생기가 없다";
        if (broken != null || off != null) return broken ?? off!;
        return _net.WaterTank < 50 ? "산소 발생기에 넣을 물이 없다" : "쓰는 산소가 만드는 산소보다 많다";
    }
}
