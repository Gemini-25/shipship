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

    private readonly Layout L;
    private readonly Tables T;
    private Mind? _mind;
    private Network? _net;
    private Life? _life;
    private Field? _f;
    public List<Job> Jobs = new();
    public float[] MakerProgress = Array.Empty<float>();   // 설비마다 만드는 중 (시간)
    public long Crafted, Maintained, Cleaned, Made;

    public Work(Layout layout, Tables tables) { L = layout; T = tables; }

    public void Step(Sim sim, double dt)
    {
        _mind ??= sim.Find<Mind>(); _net ??= sim.Find<Network>(); _life ??= sim.Find<Life>(); _f ??= sim.Find<Field>();
        if (_mind == null) return;
        if (MakerProgress.Length != L.Furniture.Count) MakerProgress = new float[L.Furniture.Count];
        Makers(sim, dt);
        if (sim.Tick % PlanEvery == Offset) Plan(sim);
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
            if (MakerProgress[fu] >= mk.Hours) { MakerProgress[fu] = 0; _mind.Stock[fu * _mind.ItemCount + item]++; Made++; }
        }
    }

    // ───────────────────────── 일감판 ─────────────────────────

    private void Plan(Sim sim)
    {
        var mind = _mind!;
        long now = sim.Tick;
        // 끝났거나 기한이 지난 일은 내린다
        Jobs.RemoveAll(j => j.Until < now || Done(j));
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
        or GoalKind.Evacuate or GoalKind.Flee or GoalKind.Panic or GoalKind.Down or GoalKind.Craft or GoalKind.Maintain or GoalKind.Clean;

    // ───────────────────────── 찍기 ─────────────────────────

    public void Save(StateWriter w)
    {
        w.List(Jobs); w.Arr(MakerProgress);
        w.I64(Crafted); w.I64(Maintained); w.I64(Cleaned); w.I64(Made);
    }

    public void Load(StateReader r)
    {
        Jobs = r.List<Job>(); MakerProgress = r.Arr<float>();
        Crafted = r.I64(); Maintained = r.I64(); Cleaned = r.I64(); Made = r.I64();
    }
}
