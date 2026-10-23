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

/// <summary>일감 결과 기억 (docs/MIND.md 3단 — 주컴퓨터): 끝나지 않은 일감 — 누가 · 왜 · 때 (Agent −2: '막힌 곳'으로 방송했다).</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct JobFail
{
    public GoalKind Kind;
    public int Target, Arg, Agent;
    public SnagKind Snag;   // None: 기한을 넘겼다
    public long Tick;
}

/// <summary>
/// ⑪ 일과 물자 (docs/ENGINE.md §4 ⑪) — 월드 엔진: 켜진 설비(정제기 · 수집기)가 전기를 먹고 원료를 조금씩 만들고 · 물자 장부(재고 · 흐름 · 며칠 버티나)를 적는다.
/// 일감판 · 막힌 곳 찾기 · 설비 켜고 끄기는 주컴퓨터의 살림이다 (⑩ <see cref="ShipMind"/> — 판단 이관 (아)).
/// <para>공급 사슬 (재활용): 정제기는 헌 부품이 놓여 있으면 그것부터 녹인다 — 30분에 하나 → 원료 하나 (광석 · 부스러기로는 2시간) ·
/// 헌 전기 부품이면 전자 소자를 건질 수도 있다 (세상의 확률 ChipBack — 난수 갈래 '헌 기판').</para>
/// </summary>
public sealed partial class Work : IEngine
{
    public string Name => "일과 물자";
    public Phase Phase => Phase.Requests;
    public int Every => 25;
    public int Offset => 7;

    public const double MaintainAt = 0.8;      // 부품을 표준 수명의 80% 썼으면 미리 간다
    public const float HarvestAt = 0.3f;       // 작물 1m²(단마다)에 양분이 이만큼(몰 — 사흘 치) 쌓이면 거둔다
    public const double RestockBelow = 30;     // 냉장고 · 배식기의 식량이 이 몰 아래면 식량 보관함에서 채운다 (한 상자 40몰)

    private readonly Layout L;
    private readonly Tables T;
    private Mind? _mind;
    private Network? _net;
    private int[]? _pointsA, _lockersA;
    /// <summary>먹을 것을 꺼내는 곳 (냉장고 · 배식기) · 식량 보관함 (포장 식량).</summary>
    public int[] FoodPoints => _pointsA ??= L.Furniture.Where(f => f.Cells.Length > 0 && f.Type is "Fridge" or "MealDispenser").Select(f => f.Id).ToArray();
    public int[] FoodLockers => _lockersA ??= L.Furniture.Where(f => f.Cells.Length > 0 && T.Equip(f.Type).Sealed).Select(f => f.Id).ToArray();

    public double FoodAt(int fu)
    {
        double s = 0;
        if (_f != null && fu >= 0 && fu < L.Furniture.Count) foreach (int c in L.Furniture[fu].Cells) s += _f.Stored(T.sFood, c);
        return s;
    }
    private Life? _life;
    private Field? _f;
    public float[] MakerProgress = Array.Empty<float>();   // 설비마다 만드는 중 (시간)
    public long Crafted, Maintained, Cleaned, Made, Harvested;
    public long Recycled, ChipsSaved;
    public const double RecycleHours = 0.5;    // 헌 부품 하나를 녹이는 시간
    public double ChipBack = 0.5;              // 헌 전기 부품에서 쓸 만한 전자 소자를 건질 확률 (시험이 바꾼다)

    public Work(Layout layout, Tables tables) { L = layout; T = tables; }

    public void Bind(Sim sim) { _mind = sim.Find<Mind>(); _net = sim.Find<Network>(); _life = sim.Find<Life>(); _f = sim.Find<Field>(); }

    public void Step(Sim sim, double dt)
    {
        if (_mind == null) return;
        if (MakerProgress.Length != L.Furniture.Count) MakerProgress = new float[L.Furniture.Count];
        Makers(sim, dt);
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
        foreach (string name in new[] { "수리 꾸러미", "봉합 꾸러미", "소화기", "이산화탄소 소화기", "원료", "전기 부품", "기계 부품", "봉함재", "거르개", "전자 소자", "헌 부품", "헌 전기 부품" })
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
            if (_mind.Stock[fu * _mind.ItemCount + item] >= mk.Max) continue;   // 놓을 자리가 찼다 (켜고 끄기는 주컴퓨터의 살림 — ⑩)
            if (!_net.Working(dev.Id)) continue;
            int scrap = dev.Type == "Refinery" ? ScrapAt(fu) : -1;   // 헌 부품부터 녹인다
            MakerProgress[fu] += (float)(dt / 3600);
            if (MakerProgress[fu] < (scrap >= 0 ? RecycleHours : mk.Hours)) continue;
            MakerProgress[fu] = 0;
            _mind.Stock[fu * _mind.ItemCount + item]++; Made++; sim.Book.AddItem(item, Flow.Made, 1);
            if (scrap < 0) continue;
            _mind.Stock[fu * _mind.ItemCount + scrap]--; sim.Book.AddItem(scrap, Flow.Melted, -1); Recycled++;
            if (scrap == _mind.iScrapElec && Rng.Unit(sim.Seed, Rng.Key("헌 기판"), sim.Tick, fu) < ChipBack)
            {
                _mind.Stock[fu * _mind.ItemCount + _mind.iChip]++; sim.Book.AddItem(_mind.iChip, Flow.Made, 1); ChipsSaved++;
            }
        }
    }

    /// <summary>정제기에 놓인 헌 부품 (헌 전기 부품이 먼저 — 전자 소자를 건지려고 · 없으면 -1).</summary>
    private int ScrapAt(int fu)
    {
        if (_mind!.Stock[fu * _mind.ItemCount + _mind.iScrapElec] > 0) return _mind.iScrapElec;
        if (_mind.Stock[fu * _mind.ItemCount + _mind.iScrap] > 0) return _mind.iScrap;
        return -1;
    }

    // ───────────────────────── 찍기 ─────────────────────────

    public void Save(StateWriter w)
    {
        w.Arr(MakerProgress);
        w.I64(Crafted); w.I64(Maintained); w.I64(Cleaned); w.I64(Made); w.I64(Harvested); w.I64(Recycled); w.I64(ChipsSaved);
    }

    public void Load(StateReader r)
    {
        MakerProgress = r.Arr<float>();
        Crafted = r.I64(); Maintained = r.I64(); Cleaned = r.I64(); Made = r.I64(); Harvested = r.I64(); Recycled = r.I64(); ChipsSaved = r.I64();
    }
}
