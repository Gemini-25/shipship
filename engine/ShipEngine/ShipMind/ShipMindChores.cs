namespace ShipEngine;

/// <summary>
/// ⑩ 주컴퓨터의 살림 (판단 이관 (아) — 일감판 · 막힌 곳 찾기 · 만드는 설비 켜고 끄기는 판단이다 · docs/ENGINE.md): 주컴퓨터가 센서 · 재고 기록으로 배의 살림을 꾸린다 —
/// 일감판에 할 일을 올리고 (모자란 물건 만들기 · 예방 정비 · 알린 기계 소리 · 거두기 · 식량 나르기 · 청소) 로봇 먼저(몸이 그 일을 할 수 있는), 그다음 솜씨가 맞는 사람에게 맡긴다 ·
/// 모자란 것마다 생산 사슬을 거슬러 첫 막힌 곳을 찾아 방송 · 일지 · 만드는 설비(정제기 · 수집기)는 모자라고 전력에 여유가 있으면 켜고 다 차면 끈다.
/// ⑪ 일과 물자(월드)는 켜진 설비가 만드는 것 · 장부만 계산한다. 주컴퓨터가 멈추면 새 일을 올리지 못하고 설비도 그대로다 (맡긴 일은 하던 대로).
/// </summary>
public sealed partial class ShipMind
{
    public const int ChoreEvery = 125, ChoreOffset = 7, MakerEvery = 25;   // 5분마다 일감판 · 1분마다 설비
    public List<Job> Jobs = new();
    private Work? _work;

    /// <summary>살림 — 주컴퓨터가 돌 때 (Step에서 · Online 뒤).</summary>
    private void Chores(Sim sim)
    {
        if (_mind == null || _work == null) return;
        if (sim.Tick % MakerEvery == ChoreOffset % MakerEvery) SwitchMakers();
        if (sim.Tick % ChoreEvery == ChoreOffset) { PlanChores(sim); FindBlocks(sim); }
    }

    /// <summary>만드는 설비 켜고 끄기: 다 차면 끄고 · 모자라고 전력에 여유(80% 안 — 막힌 곳이면 95%)가 있으면 켠다.</summary>
    private void SwitchMakers()
    {
        if (_net == null) return;
        foreach (var dev in _net.Devices)
        {
            if (!T.People.Makers.TryGetValue(dev.Type, out var mk)) continue;
            int item = _mind!.ItemIndex(mk.Item);
            if (item < 0) continue;
            int have = _mind.Stock[dev.Furniture * _mind.ItemCount + item];
            if (have >= mk.Max) { if (_net.On[dev.Id] != 0 && dev.Def.Kw > 3) _net.SetOn(dev.Id, false); continue; }
            if (_net.On[dev.Id] == 0 && _net.Demanded + dev.Def.Kw < (_powerFirst.Contains(dev.Id) ? 0.95 : 0.8) * _net.Capacity()) _net.SetOn(dev.Id, true);
        }
    }

    // ───────────────────────── 일감판 ─────────────────────────

    private void PlanChores(Sim sim)
    {
        var mind = _mind!;
        long now = sim.Tick;
        // 끝난 일은 내린다 · 끝나지 않은 채 기한을 넘겼거나 맡은 이가 막혀 손을 놓았으면 누가 · 왜를 적고 내린다 (일감 결과 기억 — 다음엔 다른 이에게)
        for (int i = Jobs.Count - 1; i >= 0; i--)
        {
            var j = Jobs[i];
            if (Done(j)) { Jobs.RemoveAt(i); continue; }
            var why = GaveUp(j, now, out bool gave);
            if (j.Until >= now && !gave) continue;
            NoteJobFail(sim, j, why);
            Jobs.RemoveAt(i);
        }
        // 모자란 물건 만들기 — 원료가 모자라면 무엇을 먼저 만들지 고른다 (⑪ 공급 사슬 (라))
        PlanCrafts(sim);
        // 예방 정비: 표준 수명의 80%를 쓴 부품 (숨은 한계는 모른다)
        if (_net != null)
            foreach (var dev in _net.Devices)
                for (int p = 0; p < 4; p++)
                {
                    double life = dev.Life((Part)p);
                    int k = dev.Id * 4 + p;
                    if (life <= 0 || _net.Broken[k] != 0 || _net.Wear[k] < Work.MaintainAt * life) continue;
                    if (Jobs.Any(j => j.Kind == GoalKind.Maintain && j.Target == k)) continue;
                    if (!SpareKnown(p)) continue; // 갈아 끼울 그 갈래 부품이 없다 (⑪ 공급 사슬)
                    Post(sim, new Job { Kind = GoalKind.Maintain, Target = k }, dev.Cell, person => person.C.SkRepair >= 0.5f);
                }
        // ④ 볼트가 풀려 덜컹거리는 장치 (소리) · 그 방에 진동 센서가 있으면 반쯤 풀렸을 때부터 — 조이기 (로봇 먼저 · 사람은 고치는 솜씨가 조금 있으면)
        if (_net != null)
            for (int d = 0; d < _net.Loose.Length; d++)
            {
                if (_net.Loose[d] < Network.LooseSensed || _net.Loose[d] < LooseKnownAt(d) || Jobs.Any(j => j.Kind == GoalKind.Tighten && j.Target == d)) continue;
                Post(sim, new Job { Kind = GoalKind.Tighten, Target = d }, _net.Devices[d].Cell, person => person.C.SkRepair >= 0.3f);
            }
        // 사람이 알린 이상한 기계 소리 (⑧이 남긴 알림을 읽는다 — 배 안 통신): 아직 끽끽거리면 그 장치의 기계 부품을 미리 간다 (로봇 먼저)
        if (_net != null)
            foreach (var rp in mind.GrindReports)
            {
                if (now - rp.Tick > Time.Hours(2) || rp.Device < 0 || rp.Device >= _net.Devices.Count || _net.WornDb(rp.Device) < Network.GrindFromDb) continue;
                int k = rp.Device * 4 + (int)Part.Mechanical;
                if (_net.Broken[k] != 0 || Jobs.Any(j => j.Kind == GoalKind.Maintain && j.Target == k) || !SpareKnown((int)Part.Mechanical)) continue;
                Post(sim, new Job { Kind = GoalKind.Maintain, Target = k }, _net.Devices[rp.Device].Cell, person => person.C.SkRepair >= 0.5f);
            }
        // 거두기: 다 자란 작물 (로봇 먼저 · 사람은 기르는 솜씨가 조금이라도 있으면)
        if (_life != null)
            for (int k = 0; k < _life.Crops.Count; k++)
            {
                var cr = _life.Crops[k];
                if (cr.Biomass < Work.HarvestAt * _life.CropArea(_net, k) || Jobs.Any(j => j.Kind == GoalKind.Harvest && j.Target == k)) continue;
                Post(sim, new Job { Kind = GoalKind.Harvest, Target = k }, cr.Cell, person => person.C.SkFarm >= 0.3f);
            }
        // 식량 나르기: 냉장고 · 배식기가 비어 가면 가까운 식량 보관함에서 (로봇 먼저 · 사람은 누구나)
        foreach (int point in _work!.FoodPoints)
        {
            if (_work!.FoodAt(point) >= Work.RestockBelow || Jobs.Any(j => j.Kind == GoalKind.Restock && j.Target == point)) continue;
            int cell = L.Furniture[point].Cells[0], best = -1, bd = int.MaxValue;
            foreach (int lk in _work.FoodLockers)
            {
                if (_work!.FoodAt(lk) < 1) continue;
                int d = mind.Dist(cell, L.Furniture[lk].Cells[0]);
                if (d < bd) { bd = d; best = lk; }
            }
            if (best >= 0) Post(sim, new Job { Kind = GoalKind.Restock, Target = point, Arg = best }, L.Furniture[best].Cells[0], person => true);
        }
        // 헌 부품 나르기 (⑪ 공급 사슬 (다)): 장치 곁에 남은 헌 부품을 정제기로 (로봇 먼저 · 사람은 누구나)
        PlanHauls(sim);
        // 채우기 · 줍기 (docs/MIND.md 3단 — 손): 보관함 무게 센서로 덜 찬 소화기 · 우주복 · 마스크를 안다 · 카메라로 바닥의 큰 물건을 본다
        PlanRefills(sim);
        PlanGathers(sim);
        // 청소: 방마다 가장 지저분한 곳 하나 — 곰팡이 · 고인 물 (물 고인 바닥 밑 전선은 합선된다)
        foreach (var room in L.Rooms)
        {
            int worst = -1; double wm = 0;
            foreach (int c in room.Cells)
            {
                double dirt = (_life != null && _life.Mold[c] > 0.3f ? _life.Mold[c] : 0) + (F.Stored(T.sWater, c) > 5 ? F.Stored(T.sWater, c) / 100 + 0.3 : 0);
                if (dirt > wm) { wm = dirt; worst = c; }
            }
            if (worst < 0 || Jobs.Any(j => j.Kind == GoalKind.Clean && L.Room[j.Target] == room.Id)) continue;
            Post(sim, new Job { Kind = GoalKind.Clean, Target = worst }, worst, person => true);
        }
    }

    private bool Done(in Job j) => j.Kind switch
    {
        GoalKind.Craft => _mind!.TotalOf(j.Arg) >= (T.People.Recipes.TryGetValue(_mind.ItemNames[j.Arg], out var r) ? r.Keep : 0),
        GoalKind.Maintain => _net == null || _net.Wear[j.Target] < Work.MaintainAt * _net.Devices[j.Target / 4].Life((Part)(j.Target % 4)) * 0.5,
        GoalKind.Clean => (_life == null || _life.Mold[j.Target] < 0.05f) && F.Stored(T.sWater, j.Target) < 2,
        GoalKind.Harvest => _life == null || j.Target >= _life.Crops.Count || _life.Crops[j.Target].Biomass < Work.HarvestAt * 0.5f * _life.CropArea(_net, j.Target),
        GoalKind.Restock => _work!.FoodAt(j.Target) >= Work.RestockBelow * 2 || _work.FoodAt(j.Arg) < 1,
        GoalKind.Haul => HaulDone(j.Arg, j.Agent),
        GoalKind.Tighten => _net == null || j.Target >= _net.Loose.Length || _net.Loose[j.Target] < Network.LooseFelt,
        GoalKind.ToiletCare => CareDone(j),
        GoalKind.Refill => j.Target >= L.Furniture.Count || _mind!.SpentIn(j.Target, (byte)j.Arg) <= 0 && (j.Agent < 0 || j.Agent >= _mind.Agents.Count || _mind.Agents[j.Agent].C.Holding != j.Arg),
        GoalKind.Gather => GatherDone(j.Target, j.Arg, j.Agent),
        _ => true,
    };

    /// <summary>채우기 일감: 쓰다 남은 것이 든 보관함마다 하나 (물건마다) — 채울 곳이 있는 것만 (로봇 먼저 · 사람은 누구나).</summary>
    private void PlanRefills(Sim sim)
    {
        var mind = _mind!;
        foreach (var sp in mind.Spent)
        {
            int fu = sp.Furniture;
            if (fu < 0 || fu >= L.Furniture.Count || mind.SpentIn(fu, sp.Item) <= 0) continue;
            if (Jobs.Any(j => j.Kind == GoalKind.Refill && j.Target == fu && j.Arg == sp.Item)) continue;
            if (mind.RefillPoint(sp.Item, L.Furniture[fu].Cells[0], fu) < 0) continue;
            Post(sim, new Job { Kind = GoalKind.Refill, Target = fu, Arg = sp.Item }, L.Furniture[fu].Cells[0], person => true);
        }
    }

    /// <summary>줍기 일감: 카메라에 보이는 바닥의 큰 물건 (5kg 넘게 — 소화기 · 우주복 · 방화 담요 …) · 둘 곳이 있는 것 (작은 물건은 본 로봇이 스스로 줍는다).</summary>
    private void PlanGathers(Sim sim)
    {
        var mind = _mind!;
        foreach (var x in mind.Loose)
        {
            if (x.PlugAt != 0 || x.Item == mind.iWaste || mind.ItemKg(x.Item) < 5 || mind.HomeFor(x.Item, x.Cell) < 0) continue;
            int room = L.Room[x.Cell];
            if (room < 0 || _net != null && !_net.RoomPowered(room)) continue;   // 카메라가 꺼진 방은 모른다
            if (Jobs.Any(j => j.Kind == GoalKind.Gather && j.Target == x.Cell && j.Arg == x.Item)) continue;
            Post(sim, new Job { Kind = GoalKind.Gather, Target = x.Cell, Arg = x.Item }, x.Cell, person => true);
        }
    }

    /// <summary>줍기 끝: 그 칸에 그 물건이 없고 맡은 이도 들고 있지 않다 (제자리에 넣었다).</summary>
    private bool GatherDone(int cell, int item, int agent)
    {
        var mind = _mind!;
        foreach (var x in mind.Loose) if (x.Cell == cell && x.Item == item && x.PlugAt == 0) return false;
        return agent < 0 || agent >= mind.Agents.Count || mind.Agents[agent].C.Holding != item;
    }

    public List<JobFail> JobFails = new();
    public long JobsFailed, JobsStuck;
    public const int JobFailHours = 6;

    /// <summary>맡은 이가 손을 놓았나: 그 일을 할 몸이 아니다 (팔 · 다리) · 맡은 뒤 그 일에서 막혀 3분 넘게 다른 것을 한다 — 그때의 까닭 (기한을 넘긴 것이면 그 일에서 남긴 막힘 · 없으면 None).</summary>
    private SnagKind GaveUp(in Job j, long now, out bool gave)
    {
        gave = false;
        var mind = _mind!;
        if (j.Agent < 0 || j.Agent >= mind.Agents.Count) return SnagKind.None;
        var a = mind.Agents[j.Agent];
        ref var c = ref a.C;
        long posted = j.Until - Time.Hours(2);
        if (!a.IsPerson && !mind.RobotCan(a, j.Kind)) { gave = true; return mind.LegsOk(a) ? SnagKind.NoHands : SnagKind.NoLegs; }
        bool snagged = c.SnagTick >= posted && c.SnagGoal == j.Kind && c.Snag != SnagKind.None;
        if (snagged && Mind.Telling(c.Snag) && c.Goal != j.Kind && now - c.SnagTick > Time.Minutes(3)) gave = true;
        return snagged ? c.Snag : SnagKind.None;
    }

    /// <summary>일감이 끝나지 않았다: 적고 (여섯 시간 기억) · 맡김을 거두고 · 일지에 '누가 · 왜 → 다른 이에게'.</summary>
    private void NoteJobFail(Sim sim, in Job j, SnagKind why)
    {
        var mind = _mind!;
        if (JobFails.Count >= 64) JobFails.RemoveAt(0);
        JobFails.Add(new JobFail { Kind = j.Kind, Target = j.Target, Arg = j.Arg, Agent = j.Agent, Snag = why, Tick = sim.Tick });
        JobsFailed++;
        if (j.Agent >= 0 && j.Agent < mind.Agents.Count)
        {
            ref var c = ref mind.Agents[j.Agent].C;
            if (c.AssignedGoal == j.Kind && c.AssignedTarget == j.Target) c.AssignedUntil = sim.Tick;
        }
        string who = j.Agent >= 0 && j.Agent < mind.Agents.Count ? mind.NameOf(j.Agent) : "맡은 이";
        Say(sim, $"일감 {JobTitle(j)} — {who}: {(why == SnagKind.None ? "기한을 넘겼다" : Mind.SnagText(why))} → 다른 이에게", NoteKind.Job);
    }

    /// <summary>그 일감을 그 이가 여섯 시간 안에 못 했나.</summary>
    private bool FailedBy(in Job j, int agent, long now)
    {
        foreach (var f in JobFails) if (f.Kind == j.Kind && f.Target == j.Target && f.Agent == agent && now - f.Tick < Time.Hours(JobFailHours)) return true;
        return false;
    }

    /// <summary>그 일감을 여섯 시간 안에 못 한 번 (로봇 · 사람).</summary>
    private int FailsOf(in Job j, long now, bool robotsOnly = false)
    {
        int n = 0;
        foreach (var f in JobFails)
            if (f.Kind == j.Kind && f.Target == j.Target && f.Agent >= 0 && now - f.Tick < Time.Hours(JobFailHours) && (!robotsOnly || f.Agent < _mind!.Agents.Count && !_mind.Agents[f.Agent].IsPerson)) n++;
        return n;
    }

    /// <summary>맡을 이가 없다 — 둘 넘게 못 했으면 '막힌 곳'으로 한 번 방송한다 (여섯 시간에 한 번 · 막힌 곳 목록에 남는다).</summary>
    private void StuckJob(Sim sim, in Job j, long now)
    {
        if (FailsOf(j, now) < 2) return;
        foreach (var f in JobFails) if (f.Kind == j.Kind && f.Target == j.Target && f.Agent == -2 && now - f.Tick < Time.Hours(JobFailHours)) return;
        SnagKind last = SnagKind.None;
        foreach (var f in JobFails) if (f.Kind == j.Kind && f.Target == j.Target && f.Agent >= 0) last = f.Snag;
        if (JobFails.Count >= 64) JobFails.RemoveAt(0);
        JobFails.Add(new JobFail { Kind = j.Kind, Target = j.Target, Arg = j.Arg, Agent = -2, Snag = last, Tick = now });
        JobsStuck++;
        Say(sim, $"막힌 곳: {JobTitle(j)} — {(last == SnagKind.None ? "맡은 이마다 기한을 넘겼다" : Mind.SnagText(last))} (로봇 · 사람 모두 못 했다)", NoteKind.Stuck);
    }

    /// <summary>일감의 이름 (일지 · 막힌 곳 줄): 무엇 (어디).</summary>
    private string JobTitle(in Job j)
    {
        int cell = JobCell(j);
        string what = j.Kind switch
        {
            GoalKind.Craft => $"{Ko.I(_mind!.ItemNames[Math.Clamp(j.Arg, 0, _mind.ItemCount - 1)])} 만들기", GoalKind.Maintain => "미리 갈기", GoalKind.Clean => "청소", GoalKind.Harvest => "거두기",
            GoalKind.Restock => "식량 나르기", GoalKind.Haul => "헌 부품 나르기", GoalKind.Tighten => "볼트 조이기", GoalKind.ToiletCare => "변기 손질",
            GoalKind.Refill => "쓴 것 채우기", GoalKind.Gather => "떨어진 것 줍기", _ => Mind.GoalName(j.Kind),
        };
        return cell >= 0 && cell < L.N && L.Room[cell] >= 0 ? $"{what} ({RoomName(L.Room[cell])})" : what;
    }

    /// <summary>일감의 자리 (칸 · −1).</summary>
    private int JobCell(in Job j)
    {
        int F(int fu) => fu >= 0 && fu < L.Furniture.Count && L.Furniture[fu].Cells.Length > 0 ? L.Furniture[fu].Cells[0] : -1;
        return j.Kind switch
        {
            GoalKind.Craft or GoalKind.Refill => F(j.Target),
            GoalKind.Maintain => _net != null && j.Target / 4 < _net.Devices.Count ? _net.Devices[j.Target / 4].Cell : -1,
            GoalKind.Tighten => _net != null && j.Target < _net.Devices.Count ? _net.Devices[j.Target].Cell : -1,
            GoalKind.Harvest => _life != null && j.Target < _life.Crops.Count ? _life.Crops[j.Target].Cell : -1,
            GoalKind.Restock => F(j.Arg),
            GoalKind.Haul => F(j.Arg / 256),
            GoalKind.ToiletCare => j.Arg == 3 ? j.Target : F(j.Target),
            _ => j.Target,
        };
    }

    /// <summary>나르기 끝: 놓인 곳에 헌 부품이 없고 맡은 이도 들고 있지 않다.</summary>
    private bool HaulDone(int arg, int agent)
    {
        var mind = _mind!;
        if (arg / 256 >= L.Furniture.Count) return true;
        if (mind.Stock[arg / 256 * mind.ItemCount + arg % 256] > 0) return false;
        return agent < 0 || agent >= mind.Agents.Count || mind.Agents[agent].C.Holding != arg % 256;
    }

    /// <summary>맡길 이를 찾는다: 놀고 있는 로봇이 먼저 (가까운 순) — 없으면 깨어 있고 바쁘지 않은 솜씨 맞는 사람 (맡겼으면 참).</summary>
    /// <param name="stalled">⑪ 부품을 기다리는 고치기를 맡았지만 손을 놓고 있는 로봇에게도 맡긴다 (그 부품을 먼저 만들게).</param>
    private bool Post(Sim sim, Job j, int cell, Func<Agent, bool> canPerson, bool stalled = false)
    {
        var mind = _mind!;
        int best = -1, bd = int.MaxValue;
        bool robotsTried = FailsOf(j, sim.Tick, robotsOnly: true) > 0;   // 로봇이 못 했으면 깨어 있는 사람에게도 (일하는 시간 밖이어도 쉬고 있으면)
        foreach (var a in mind.Agents)
        {
            bool waitingPart = stalled && !a.IsPerson && a.C.AssignedGoal is GoalKind.Repair or GoalKind.Maintain && a.C.Goal != a.C.AssignedGoal;
            if (a.C.Up == 0 || a.C.AssignedUntil > sim.Tick && !waitingPart || a.C.Kind == AgentKind.Drone) continue;
            if (ChoreBusy(a.C.Goal)) continue;
            if (FailedBy(j, a.Id, sim.Tick)) continue;   // 이 일을 못 한 이에게 다시 맡기지 않는다 (같은 실패를 되풀이하지 않는다)
            if (!a.IsPerson && (a.C.Battery < 0.4f || !mind.RobotCan(a, j.Kind))) continue;   // ⑥ 몸 보고 — 그 일을 할 몸인가
            if (a.IsPerson && (!canPerson(a) || !(a.C.Goal == GoalKind.Work || robotsTried && a.C.Goal is GoalKind.Idle or GoalKind.Relax))) continue; // 사람은 일하는 시간에만 (로봇이 못 했으면 쉬는 이도)
            int d = mind.PathDist(a.C.Cell, cell) + (a.C.Holding != 255 && Mind.HandGoal(j.Kind) ? 10 : 0) + (a.IsPerson ? 1000 : 0); // 길 거리 + 손에 든 것을 먼저 둘 몫 · 로봇 먼저
            if (d < bd) { bd = d; best = a.Id; }
        }
        if (best < 0) { StuckJob(sim, j, sim.Tick); return false; }
        j.Agent = best; j.Until = sim.Tick + Time.Hours(2);
        mind.AssignJob(best, j.Kind, j.Target, j.Arg, j.Until);
        Jobs.Add(j);
        return true;
    }

    private static bool ChoreBusy(GoalKind g) => g is GoalKind.FightFire or GoalKind.Rescue or GoalKind.Patch or GoalKind.Brace or GoalKind.Repair
        or GoalKind.Evacuate or GoalKind.Flee or GoalKind.Panic or GoalKind.Down or GoalKind.Craft or GoalKind.Maintain or GoalKind.Clean or GoalKind.Harvest or GoalKind.Restock or GoalKind.Haul or GoalKind.Tighten or GoalKind.ToiletCare
        or GoalKind.Refill or GoalKind.Gather;

    public readonly List<string> Blocks = new();
    public readonly List<int> BlockCauses = new();        // 막힌 곳마다 원인 마디 (⑪ — 같은 막힘이면 같은 마디 · 망가진 설비 때문이면 그 고장의 원인에 잇는다)
    private int _why;                                      // 지금 적는 막힌 곳의 까닭에 닿는 원인 (망가진 부품의 원인)
    private readonly HashSet<int> _powerFirst = new();   // 막힌 곳이 전력 여유인 설비 (95%까지 켠다)

    private void FindBlocks(Sim sim)
    {
        var mind = _mind!;
        var had = new Dictionary<string, int>();
        for (int i = 0; i < Blocks.Count && i < BlockCauses.Count; i++) had[BlockKey(Blocks[i])] = BlockCauses[i];
        Blocks.Clear(); BlockCauses.Clear(); _powerFirst.Clear();
        void Add(string line)
        {
            Blocks.Add(line);
            BlockCauses.Add(had.TryGetValue(BlockKey(line), out int c) ? c : sim.Causes.Add(CauseKind.Supply, $"막혔다: {line}", sim.Tick, -1, _why));
            _why = 0;
        }
        // 만들 물건: 둘 수만큼 없는데 맡은 이도 없다
        foreach (var (name, r) in T.People.Recipes)
        {
            int item = mind.ItemIndex(name);
            if (item < 0 || mind.TotalOf(item) >= r.Keep || Jobs.Any(j => j.Kind == GoalKind.Craft && j.Arg == item)) continue;
            string? why = null;
            int bench = L.Furniture.FindIndex(f => f.Type == r.At && f.Cells.Length > 0);
            int bdev = bench >= 0 && _net != null ? _net.Devices.FindIndex(d => d.Furniture == bench) : -1;
            if (bench < 0) why = $"{T.Equip(r.At).Name}가 없다";
            else if (bdev >= 0 && (_net!.IsBroken(bdev, Part.Electric) || _net.IsBroken(bdev, Part.Mechanical))) { why = $"{_net.Devices[bdev].Name}가 망가졌다"; _why = BrokenCause(bdev); }
            else
                foreach (var (need, amount) in r.Needs.OrderBy(kv => kv.Key == "전자 소자" ? 0 : 1))   // 배에서 만들 수 없는 것을 먼저 댄다 (원료는 기다리면 생긴다)
                {
                    int ni = mind.ItemIndex(need);
                    if (ni >= 0 && mind.TotalOf(ni) >= amount) continue;
                    why = ni == mind.iChip ? $"{Ko.I(need)} 없다 ← 배에서 만들 수 없다 (실어 온 것 · 헌 전기 부품에서 건진 것 · 보급뿐)" : $"{Ko.I(need)} 없다 ← {MakerWhy(need)}";
                    break;
                }
            if (why == null && !mind.Agents.Any(a => a.C.Up != 0 && (!a.IsPerson && a.C.Kind != AgentKind.Drone || a.IsPerson && a.C.SkRepair >= 0.4f))) why = "만들 솜씨가 있는 이가 없다";
            if (why != null) Add($"{name} ← {why}");
        }
        // 일감: 로봇 · 사람 모두 못 해 방송한 것 (여섯 시간 안 — docs/MIND.md 3단 일감 결과 기억)
        foreach (var f in JobFails)
            if (f.Agent == -2 && sim.Tick - f.Tick < Time.Hours(JobFailHours))
                Add($"{JobTitle(new Job { Kind = f.Kind, Target = f.Target, Arg = f.Arg })} ← {(f.Snag == SnagKind.None ? "맡은 이마다 기한을 넘겼다" : Mind.SnagText(f.Snag))}");
        // 마실 물 · 식량 · 산소: 만드는 것까지 쳐서 30일을 못 버틴다
        var (wd, _) = _work!.DaysLeft(sim, (int)Res.Water);
        if (wd < 30) Add($"마실 물 {wd:0}일 치 ← {WaterWhy()}");
        var (fd, _) = _work.DaysLeft(sim, (int)Res.Food);
        if (fd < 30) Add($"식량 {fd:0}일 치 ← {FoodWhy(sim)}");
        var (od, _) = _work.DaysLeft(sim, (int)Res.O2);
        if (od < 30) Add($"산소 {od:0}일 치 ← {O2Why()}");
        ReportBlocks(sim, Blocks, BlockCauses);
    }

    /// <summary>재료를 만드는 설비 쪽의 까닭: 없다 · 망가졌다 · 꺼 두었다(전력 여유) · 만드는 중.</summary>
    private string MakerWhy(string item)
    {
        if (_net == null) return "만드는 설비가 없다";
        string? off = null, making = null, broken = null;
        foreach (var dev in _net.Devices)
        {
            if (!T.People.Makers.TryGetValue(dev.Type, out var mk) || mk.Item != item) continue;
            if (_net.IsBroken(dev.Id, Part.Electric) || _net.IsBroken(dev.Id, Part.Mechanical)) { if (broken == null) { broken = $"{dev.Name}가 망가졌다"; _why = BrokenCause(dev.Id); } continue; }
            if (_net.On[dev.Id] == 0) { off ??= $"{dev.Name}를 꺼 두었다 (전력 여유가 없다)"; _powerFirst.Add(dev.Id); continue; }
            int fu = dev.Furniture;
            making ??= $"{dev.Name}가 만드는 중 ({(fu >= 0 && fu < _work!.MakerProgress.Length ? _work.MakerProgress[fu] : 0):0.0}/{mk.Hours:0}시간)";
        }
        if (making != null || off != null) _why = 0;   // 까닭이 고장이 아니면 고장의 원인에 잇지 않는다
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
            if (_net.IsBroken(dev.Id, Part.Mechanical) || _net.IsBroken(dev.Id, Part.Electric)) { if (broken == null) { broken = $"{dev.Name}가 망가졌다"; _why = BrokenCause(dev.Id); } continue; }
            if (_net.IsBroken(dev.Id, Part.Filter)) { if (broken == null) { broken = $"{dev.Name}의 거르개가 망가졌다"; _why = _net.PartCause(dev.Id, Part.Filter); } continue; }
            if (_net.On[dev.Id] == 0) { off ??= $"{dev.Name}를 꺼 두었다"; continue; }
            double w = _net.WearFraction(dev.Id, Part.Filter);
            if (w > 0.6) worn ??= $"{dev.Name}의 거르개가 낡았다 (회수율 {(Network.WaterRecovery - 0.09 * Math.Min(1, w)) * 100:0}%)";
        }
        return !any ? "물을 되찾는 장치가 없다" : broken ?? off ?? worn ?? "마시는 양이 되찾는 양보다 많다";
    }

    /// <summary>망가진 설비의 원인 (전기 쪽 · 기계 쪽 중 망가진 것).</summary>
    private int BrokenCause(int dev) => _net!.IsBroken(dev, Part.Electric) && _net.PartCause(dev, Part.Electric) != 0 ? _net.PartCause(dev, Part.Electric) : _net.PartCause(dev, Part.Mechanical);

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
            if (_net.IsBroken(dev.Id, Part.Electric) || _net.IsBroken(dev.Id, Part.Mechanical)) { if (broken == null) { broken = $"{dev.Name}가 망가졌다"; _why = BrokenCause(dev.Id); } continue; }
            if (_net.On[dev.Id] == 0) off ??= $"{dev.Name}를 꺼 두었다";
        }
        if (!any) return "산소 발생기가 없다";
        if (broken == null) _why = 0;
        if (broken != null || off != null) return broken ?? off!;
        return _net.WaterTank < 50 ? "산소 발생기에 넣을 물이 없다" : "쓰는 산소가 만드는 산소보다 많다";
    }

    private void SaveChores(StateWriter w)
    {
        w.List(Jobs); w.List(JobFails); w.I64(JobsFailed); w.I64(JobsStuck);
        w.I32(Blocks.Count); foreach (var b in Blocks) w.Str(b); w.Arr(BlockCauses.ToArray()); w.Arr(_powerFirst.OrderBy(x => x).ToArray());
    }
    private void LoadChores(StateReader r)
    {
        Jobs = r.List<Job>(); JobFails = r.List<JobFail>(); JobsFailed = r.I64(); JobsStuck = r.I64();
        Blocks.Clear(); int nb = r.I32(); for (int i = 0; i < nb; i++) Blocks.Add(r.Str() ?? ""); BlockCauses.Clear(); BlockCauses.AddRange(r.Arr<int>()); _powerFirst.Clear(); foreach (int x in r.Arr<int>()) _powerFirst.Add(x);
    }
}
