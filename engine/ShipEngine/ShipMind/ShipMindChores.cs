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
        if (sim.Tick % MakerEvery == ChoreOffset % MakerEvery) { SwitchMakers(); WatchLeftOn(sim); }
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
            if (_net.On[dev.Id] == 0 && dev.Id < LineShed.Length && LineShed[dev.Id] != 0) continue;   // 연장선 짐을 줄이려고 껐다 (DF3) — 제 급전선이 살아날 때까지
            if (_net.On[dev.Id] == 0 && _net.Demanded + dev.Def.Kw < (_powerFirst.Contains(dev.Id) ? 0.95 : 0.8) * _net.Capacity()) _net.SetOn(dev.Id, true);
        }
    }

    /// <summary>쓰는 이 없이 이만큼(분) 켜진 조리대 · 오븐은 주컴퓨터가 끈다 (DF5 — 경보 문턱 아래도).</summary>
    public const int LeftOnCutMin = 15;
    public long LeftOnCuts;
    private byte[] _leftOnMin = Array.Empty<byte>();   // 장치마다 곁에 사람 없이 켜져 있던 분 (카메라로 본 방만 — 찍는다)

    /// <summary>1분마다: 보이는 방의 켜진 조리대 · 오븐 곁에 사람이 없으면 분을 세고 · 15분이면 전원을 내린다 (켜 둔 이의 원인에 잇는다 — 켜 둔 이가 배운다).</summary>
    private void WatchLeftOn(Sim sim)
    {
        if (_net == null || _mind == null) return;
        if (_leftOnMin.Length != _net.Devices.Count) Array.Resize(ref _leftOnMin, _net.Devices.Count);
        foreach (var dv in _net.Devices)
        {
            if (dv.Def.Role != "stove") continue;
            int r = L.Room[dv.Cell];
            if (_net.On[dv.Id] == 0 || r < 0 || r >= RoomSeen.Length || RoomSeen[r] == 0 || _mind.Attended(dv.Cell)) { _leftOnMin[dv.Id] = 0; continue; }   // 꺼졌다 · 못 본다 · 곁에 사람이 있다 (쓰는 중)
            if (++_leftOnMin[dv.Id] < LeftOnCutMin) continue;
            _leftOnMin[dv.Id] = 0;
            int cut = sim.Causes.Add(CauseKind.Computer, $"주컴퓨터가 쓰는 이 없이 {LeftOnCutMin}분 켜져 있던 {RoomName(r)} {dv.Name} 전원을 내렸다", sim.Tick, dv.Cell, _net.OnCause[dv.Id]);
            sim.Facts.Emit(sim.Tick, FactKind.Switched, dv.Cell, 0, cut, -1, dv.Id);
            _net.SetOn(dv.Id, false); StovesCut++; LeftOnCuts++;
            Say(sim, $"{RoomName(r)} {dv.Name} 전원을 내렸다 — 쓰는 이 없이 {LeftOnCutMin}분 켜져 있었다", NoteKind.Power);
        }
    }

    // ───────────────────────── 일감판 ─────────────────────────

    private void PlanChores(Sim sim)
    {
        var mind = _mind!;
        long now = sim.Tick;
        ScanLitter();
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
        // 거두기: 다 자란 작물 (로봇 먼저 · 사람은 기르는 솜씨가 조금이라도 있으면) — 농부가 일하는 재배실의 작물은 농부의 일로 둔다 (DF6)
        if (_life != null)
            for (int k = 0; k < _life.Crops.Count; k++)
            {
                var cr = _life.Crops[k];
                if (cr.Biomass < Work.HarvestAt * _life.CropArea(_net, k) || Jobs.Any(j => j.Kind == GoalKind.Harvest && j.Target == k) || FarmerAt(L.Room[cr.Cell])) continue;
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
        // 시신 수습 (DF5): 숨진 지 10분 넘은 이 — 덮이지 않았고 · 옮기는 중이 아니고 · 그 방이 보이고 위험을 아는 방이 아니면 (로봇 먼저 · 없으면 사람)
        if (_life != null)
            foreach (var a in mind.Agents)
            {
                if (!a.IsPerson || a.C.Body < 0) continue;
                var b = _life.Bodies[a.C.Body];
                int r = b.Cell >= 0 ? L.Room[b.Cell] : -1;
                if (b.Alive != 0 || b.Shroud != 0 || a.C.CarriedBy >= 0 || now - b.DiedAt < Time.Minutes(10) || r < 0 || RoomSeen[r] == 0 || RoomKnownBad(r)) continue;
                if (Jobs.Any(j => j.Kind == GoalKind.LayOut && j.Target == a.C.Body)) continue;
                Post(sim, new Job { Kind = GoalKind.LayOut, Target = a.C.Body }, b.Cell, person => !RobotFor(GoalKind.LayOut));   // 사람에게는 그 일을 할 로봇이 배에 없을 때만 (바쁜 로봇은 기다린다)
            }
        // 청소: 보이는 방마다 가장 지저분한 곳 하나 — 곰팡이 · 고인 물 (물 고인 바닥 밑 전선은 합선된다) · 깨진 조각 · 가루 · 넘어진 것 (PH13)
        foreach (var room in L.Rooms)
        {
            int worst = -1; double wm = 0;
            if (room.Id >= RoomSeen.Length || RoomSeen[room.Id] == 0)
            {
                // 보이지 않는 방 (5단 C15 — 카메라 없이 곰팡이 · 고인 물을 읽지 않는다): 합선으로 차단기가 내려간 회로면 차단기가 알린 합선 자리를 닦게 한다 (젖은 바닥 밑 전선 — 닦아야 다시 올린다)
                for (int fd = 0; fd < ShortAt.Length && _net != null; fd++)
                    if (ShortAt[fd] >= 0 && L.Room[ShortAt[fd]] == room.Id && _net.Breaker[fd] == 0) { worst = ShortAt[fd]; break; }
                if (worst < 0 || Jobs.Any(j => j.Kind == GoalKind.Clean && L.Room[j.Target] == room.Id)) continue;
                Post(sim, new Job { Kind = GoalKind.Clean, Target = worst }, worst, person => true);
                continue;
            }
            foreach (int c in room.Cells)
            {
                double dirt = (_life != null && _life.Mold[c] > 0.3f ? _life.Mold[c] : 0) + (F.Stored(T.sWater, c) > 5 ? F.Stored(T.sWater, c) / 100 + 0.3 : 0) + _litter.GetValueOrDefault(c) + (_heavy.Contains(c) ? 1 : 0);   // 둘이서 들 것 먼저 (둘을 함께 보낸다)
                if (dirt > wm) { wm = dirt; worst = c; }
            }
            if (worst < 0 || Jobs.Any(j => j.Kind == GoalKind.Clean && L.Room[j.Target] == room.Id)) continue;
            if (Post(sim, new Job { Kind = GoalKind.Clean, Target = worst }, worst, person => true) && _heavy.Contains(worst))
                Post(sim, new Job { Kind = GoalKind.Clean, Target = worst }, worst, person => true);   // 둘이서 든다 — 같은 칸에 하나 더
        }
    }

    /// <summary>정돈 (PH13): 카메라에 보인 깨진 조각 · 가루 · 누운 것 · 넘어진 것 (불 켜진 방) — 칸마다 치울 몫 (일감을 거두기 전에 센다: 찍지 않으니 되살린 세계도 같게).</summary>
    private void ScanLitter()
    {
        _litter.Clear(); _heavy.Clear();
        if (_st != null)
        {
            // 일할 수 있는 로봇이 둘이면 둘이서 들 무게까지 (PH16 — 한 로봇 40kg · 둘이 80kg)
            int able = 0;
            foreach (var a in _mind!.Agents) if (!a.IsPerson && a.C.Kind == AgentKind.Robot && a.C.Up != 0 && _mind.RobotCan(a, GoalKind.Clean)) able++;
            double limit = Mind.RobotLiftKg * Math.Clamp(able, 1, 2);
            foreach (var p in _st.Props)
            {
                if (p.State != 0 || (p.Flags & Structure.DecorFlag) != 0) continue;
                bool piece = p.Kind == 2, fallen = p.Kind is 0 or 1 && p.Pose != 0;
                if (!piece && !fallen || fallen && p.Kg > limit) continue;   // 로봇 (둘)도 못 세울 무게는 일감으로 내지 않는다 (같은 실패를 되풀이하지 않게)
                int c = _st.CellOf(p);
                if (c < 0 || L.Room[c] < 0 || _net != null && !_net.RoomPowered(L.Room[c])) continue;
                _litter[c] = _litter.GetValueOrDefault(c) + (piece ? 0.4 : 0.3);
                if (fallen && p.Kg > Mind.RobotLiftKg) _heavy.Add(c);
            }
            for (int c = 0; c < _st.Grit.Length; c++)
                if (_st.Grit[c] > 0 && L.Room[c] >= 0 && (_net == null || _net.RoomPowered(L.Room[c]))) _litter[c] = _litter.GetValueOrDefault(c) + 0.5;
        }
    }

    // 이번 정돈 셈의 칸마다 치울 것 (찍지 않는다 — 틱마다 장부에서 다시 센다)
    private readonly Dictionary<int, double> _litter = new();
    private readonly HashSet<int> _heavy = new();   // 둘이서 들 것이 있는 칸 (찍지 않는다 — 틱마다 다시 센다)

    /// <summary>그 장치의 점검 뚜껑을 그 때 뒤에 열었나 (출입 표).</summary>
    private bool AccessedSince(int dev, long tick) { foreach (var x in Accesses) if (x.Device == dev && x.Tick > tick) return true; return false; }

    private bool Done(in Job j) => j.Kind switch
    {
        GoalKind.Craft => _mind!.TotalOf(j.Arg) >= (T.People.Recipes.TryGetValue(_mind.ItemNames[j.Arg], out var r) ? r.Keep : 0),
        GoalKind.Maintain => _net == null || (j.Arg == 1 ? AccessedSince(j.Target / 4, j.Until - Time.Hours(2)) : _net.Wear[j.Target] < Work.MaintainAt * _net.Devices[j.Target / 4].Life((Part)(j.Target % 4)) * 0.5),   // 알림으로 올린 일감은 점검 뚜껑 기록으로 끝낸다 (새 부품이라 닳음 잣대로는 올리자마자 지워진다 — 5단 C25)
        GoalKind.Clean => (_life == null || _life.Mold[j.Target] < 0.05f) && F.Stored(T.sWater, j.Target) < 2 && !_litter.ContainsKey(j.Target),
        GoalKind.Harvest => _life == null || j.Target >= _life.Crops.Count || _life.Crops[j.Target].Biomass < Work.HarvestAt * 0.5f * _life.CropArea(_net, j.Target),
        GoalKind.Restock => _work!.FoodAt(j.Target) >= Work.RestockBelow * 2 || _work.FoodAt(j.Arg) < 1,
        GoalKind.Haul => HaulDone(j.Arg, j.Agent),
        GoalKind.Tighten => _net == null || j.Target >= _net.Loose.Length || _net.Loose[j.Target] < Network.LooseFelt,
        GoalKind.ToiletCare => CareDone(j),
        GoalKind.Refill => j.Target >= L.Furniture.Count || _mind!.SpentIn(j.Target, (byte)j.Arg) <= 0 && (j.Agent < 0 || j.Agent >= _mind.Agents.Count || _mind.Agents[j.Agent].C.Holding != j.Arg),
        GoalKind.Gather => GatherDone(j.Target, j.Arg, j.Agent),
        GoalKind.LayOut => _life == null || j.Target >= _life.Bodies.Count || _life.Bodies[j.Target].Shroud != 0,
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
        if (_st == null) return;
        foreach (var p in _st.Props)   // 바닥에 놓인 물건 (④ 물체 장부)
        {
            if (p.Kind != 0 || (p.Flags & Structure.DecorFlag) != 0 || p.State == 5) continue;   // 꾸밈 (제자리가 있다 — PH9)은 맡기지 않는다 · 우주로 나간 것은 배에 없다 (PH11)
            int cell = _st.CellOf(p); byte item = (byte)p.What;
            if (p.PlugAt != 0 || item == mind.iWaste || mind.ItemKg(item) < 5 || mind.HomeFor(item, cell) < 0) continue;
            int room = L.Room[cell];
            if (room < 0 || _net != null && !_net.RoomPowered(room)) continue;   // 카메라가 꺼진 방은 모른다
            if (Jobs.Any(j => j.Kind == GoalKind.Gather && j.Target == p.Id)) continue;
            Post(sim, new Job { Kind = GoalKind.Gather, Target = p.Id, Arg = item }, cell, person => true);   // 대상: 그 물체 Id (칸은 쓸 때마다 장부에서)
        }
    }

    /// <summary>줍기 끝: 그 물체가 바닥에 없고 맡은 이도 그 물건을 들고 있지 않다 (제자리에 넣었다).</summary>
    private bool GatherDone(int id, int item, int agent)
    {
        var mind = _mind!;
        if (PropCell(id) >= 0) return false;
        return agent < 0 || agent >= mind.Agents.Count || mind.Agents[agent].C.Holding != item;
    }

    /// <summary>바닥에 놓인 그 물체의 칸 (카메라 — ④ 물체 장부 · 없거나 구멍을 막았으면 −1).</summary>
    private int PropCell(int id)
    {
        int i = _st?.IndexOf(id) ?? -1;
        return i < 0 || _st!.Props[i].Kind != 0 || _st.Props[i].PlugAt != 0 || _st.Props[i].State == 7 ? -1 : _st.CellOf(_st.Props[i]);   // 문틀에 낀 것은 문을 바로잡아야 나온다
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
            GoalKind.Refill => "쓴 것 채우기", GoalKind.Gather => "떨어진 것 줍기", GoalKind.LayOut => "시신 수습", _ => Mind.GoalName(j.Kind),
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
            GoalKind.ToiletCare => j.Arg == 3 ? PropCell(j.Target) : F(j.Target),
            GoalKind.Gather => PropCell(j.Target),
            GoalKind.LayOut => _life != null && j.Target < _life.Bodies.Count ? _life.Bodies[j.Target].Cell : -1,
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

    /// <summary>그 방에서 농부가 일하고 있나 (카메라 — 일터에서 일하는 이 · 맡은 일 표).</summary>
    private bool FarmerAt(int room)
    {
        if (room < 0 || room >= RoomSeen.Length || RoomSeen[room] == 0) return false;
        var mind = _mind!;
        int farmer = Array.IndexOf(mind.RoleNames, "농부");
        foreach (var a in mind.Agents) if (a.IsPerson && a.C.Up != 0 && a.C.Role == farmer && a.C.Goal is GoalKind.Work or GoalKind.Harvest && L.Room[a.C.Cell] == room) return true;
        return false;
    }

    /// <summary>그 일을 할 몸의 로봇이 배에 있나 (지금 바쁜지는 묻지 않는다).</summary>
    private bool RobotFor(GoalKind g)
    {
        foreach (var a in _mind!.Agents) if (a.C.Kind == AgentKind.Robot && a.C.Up != 0 && a.C.Battery >= 0.2f && _mind.RobotCan(a, g)) return true;
        return false;
    }

    private static bool ChoreBusy(GoalKind g) => g is GoalKind.FightFire or GoalKind.Rescue or GoalKind.Patch or GoalKind.Brace or GoalKind.Repair
        or GoalKind.Evacuate or GoalKind.Flee or GoalKind.Panic or GoalKind.Down or GoalKind.Craft or GoalKind.Maintain or GoalKind.Clean or GoalKind.Harvest or GoalKind.Restock or GoalKind.Haul or GoalKind.Tighten or GoalKind.ToiletCare
        or GoalKind.Refill or GoalKind.Gather or GoalKind.LayOut;

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
        // 마실 물 · 식량 · 산소: 보통으로 쓰면 30일을 못 버틴다 (물 · 식량은 배급 · 방송 · 일지와 같은 셈 — DF6)
        double wd = WaterDaysLeft(sim);
        if (wd < 30) Add($"마실 물 {wd:0}일 치 ← {WaterWhy()}");
        // 오수가 밀린다: 물 재생기가 거르는 양이 드는 양에 못 미친다 (오수 통 수위 · 재생기 계량기 — DF6: 예전엔 '마시는 양' 탓으로 오진했다)
        if (WasteBacklog(out double wasteL, out double inL, out double outL)) { WasteWhy(); Add($"오수 {wasteL:0}L가 밀린다 ← 물 재생기가 거르는 양(하루 {outL:0}L)이 드는 양(하루 {inL:0}L)에 못 미친다"); }
        double fd = FoodDaysLeft(sim);
        if (fd < 30) Add($"식량 {fd:0}일 치 ← {FoodWhy(sim)}");
        var (od, _) = _work!.DaysLeft(sim, (int)Res.O2);
        if (od < 30) Add($"산소 {od:0}일 치 ← {O2Why()}");
        ReportBlocks(sim, Blocks, BlockCauses);
        WasteCare(sim);
    }

    /// <summary>밀린 오수의 까닭에 닿는 원인 (낡거나 망가진 재생기 거르개 · 기계 — 없으면 0).</summary>
    private void WasteWhy()
    {
        if (_net == null) return;
        foreach (var dev in _net.Devices)
        {
            if (dev.Def.Role != "recycler") continue;
            int pc = _net.PartCause(dev.Id, Part.Filter);
            if (pc != 0) { _why = pc; return; }
        }
    }

    private byte _wasteRule;   // 밀린 오수로 샤워 배급을 열었다 (풀릴 때 거둔다)

    /// <summary>
    /// 오수가 밀리면 (DF6): 샤워 배급을 열고 (물 배급과 같은 몫 — 씻은 물이 오수의 큰 몫) · 낡은 재생기 거르개(30% 넘게 쓴 것)를 미리 갈게 맡긴다 ·
    /// 오수가 10L 아래로 빠지면 (물 배급이 없으면) 샤워 배급을 거둔다.
    /// </summary>
    private void WasteCare(Sim sim)
    {
        if (_mind == null || _net == null) return;
        bool backlog = WasteBacklog(out double wasteL, out double inL, out double outL);
        if (backlog)
        {
            if (_mind.ShowerRuleMin == 0)
            {
                int why = BlockCause("오수");
                _mind.SetShowerRule(sim, ShowerRuleMin, ShowerRuleGap, why);
                _wasteRule = 1;
                Say(sim, $"알립니다. 오수가 {wasteL:0}L 밀렸습니다 — 물 재생기가 하루 {outL:0}L를 거르는데 {inL:0}L가 듭니다 · 오늘부터 샤워는 {ShowerRuleMin}분 · 이틀에 한 번");
            }
            foreach (var dev in _net.Devices)
            {
                if (dev.Def.Role != "recycler" || !dev.Has(Part.Filter)) continue;
                int k = dev.Id * 4 + (int)Part.Filter;
                if (_net.Broken[k] != 0 || _net.WearFraction(dev.Id, Part.Filter) < 0.3 || Jobs.Any(j => j.Kind == GoalKind.Maintain && j.Target == k) || !SpareKnown((int)Part.Filter)) continue;
                Post(sim, new Job { Kind = GoalKind.Maintain, Target = k }, dev.Cell, person => person.C.SkRepair >= 0.5f);   // 낡은 거르개는 덜 흐른다 — 미리 간다
            }
        }
        else if (_wasteRule != 0 && _net.WasteWater * 0.018 < 10 && RationWaterOn == 0)
        {
            _wasteRule = 0;
            _mind.SetShowerRule(sim, 0, ShowerRuleGap, 0);
            Say(sim, "오수가 다 걸러졌습니다 — 샤워 배급을 풉니다");
        }
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
        if (!any) return "물을 되찾는 장치가 없다";
        string? known = broken ?? off ?? worn;
        if (known != null) return known;
        return WasteBacklog(out _, out _, out _) ? "오수가 밀린다 — 물 재생기가 거르는 양이 드는 양에 못 미친다" : "쓰는 물이 되찾는 물보다 많다";   // 예전 '마시는 양이 되찾는 양보다 많다' — 밀린 오수를 마시는 양 탓으로 오진했다 (DF6)
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
        w.Arr(_leftOnMin); w.I64(LeftOnCuts);
    }
    private void LoadChores(StateReader r)
    {
        Jobs = r.List<Job>(); JobFails = r.List<JobFail>(); JobsFailed = r.I64(); JobsStuck = r.I64();
        Blocks.Clear(); int nb = r.I32(); for (int i = 0; i < nb; i++) Blocks.Add(r.Str() ?? ""); BlockCauses.Clear(); BlockCauses.AddRange(r.Arr<int>()); _powerFirst.Clear(); foreach (int x in r.Arr<int>()) _powerFirst.Add(x);
        _leftOnMin = r.Arr<byte>(); LeftOnCuts = r.I64();
    }
}
