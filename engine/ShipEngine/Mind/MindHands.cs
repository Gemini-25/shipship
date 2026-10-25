namespace ShipEngine;

/// <summary>
/// 손 (판단 결함 — docs/REVIEW.md ③-3 · 함장 결정 16 (가) · docs/MIND.md 3단): 손에 든 물건이 다음 일을 막으면 손을 비운다 —
/// 급한 일(불 · 구멍 · 구하기)이면 그 자리에 내려놓고, 아니면 제자리(그 물건을 두는 보관함 — 빈 자리가 있는 가장 가까운 곳)에 걸어가 넣는다.
/// 덤벙대는 이(성실 0.35 아래)는 일상의 일 앞에서 든 것을 그냥 내려놓는다 — 바닥 물건은 본 로봇이 줍는다 (큰 물건은 주컴퓨터 카메라도 본다 — 줍기 일감).
/// <para>쓰다 남은 물건(소화기 물 · 이산화탄소 · 우주복 · 마스크 산소)은 남은 양 그대로 보관함에 들어간다 — 꺼낼 때 그 양이다 (저절로 차지 않는다).
/// 주컴퓨터가 '채우기'를 맡기면 로봇이 채울 곳에서 채운다: 물 소화기는 물꼭지(물탱크 물 — 장부 '소화기에 담았다') ·
/// 이산화탄소 소화기는 이산화탄소 제거기(모아 둔 이산화탄소) · 우주복 · 마스크는 그 보관함의 산소 농축기(방 공기의 산소).</para>
/// </summary>
public sealed partial class Mind
{
    public List<SpentItem> Spent = new();
    public long Stashed, Refilled, Gathered, Laid;

    private Dictionary<byte, int[]>? _homes;   // 물건 → 그 물건을 두는 설비 (people.json 보관함 표 · 도면에서 다시 나온다 — 찍지 않는다)

    /// <summary>그 물건을 두는 설비들 (처음 보관함 표에 그 물건이 있는 설비).</summary>
    private int[] HomesOf(byte item)
    {
        if (_homes == null)
        {
            var map = new Dictionary<byte, List<int>>();
            foreach (var fu in L.Furniture)
            {
                if (fu.Cells.Length == 0 || !P.Stock.TryGetValue(fu.Type, out var st)) continue;
                foreach (var (name, n) in st)
                {
                    int it = Array.IndexOf(ItemNames, name);
                    if (it < 0 || n <= 0) continue;
                    if (!map.TryGetValue((byte)it, out var l)) map[(byte)it] = l = new List<int>();
                    l.Add(fu.Id);
                }
            }
            _homes = map.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
        }
        return _homes.TryGetValue(item, out var h) ? h : Array.Empty<int>();
    }

    /// <summary>처음 그 설비에 둔 그 물건 수 (제자리의 자리 수).</summary>
    private int HomeSlots(int fu, byte item) => P.Stock.TryGetValue(L.Furniture[fu].Type, out var st) && st.TryGetValue(ItemNames[item], out int n) ? n : 0;

    /// <summary>든 물건의 제자리: 빈 자리가 있는 가장 가까운(길 거리) 그 물건의 보관함 — 다 찼으면 가장 가까운 보관함 (−1: 둘 곳이 없는 물건).</summary>
    public int HomeFor(byte item, int from)
    {
        int best = -1, bd = int.MaxValue, full = -1, fd = int.MaxValue;
        foreach (int fu in HomesOf(item))
        {
            int d = PathDist(from, L.Furniture[fu].Cells[0]);
            if (Stock[fu * ItemCount + item] < HomeSlots(fu, item)) { if (d < bd) { bd = d; best = fu; } }
            else if (d < fd) { fd = d; full = fu; }
        }
        return best >= 0 ? best : full;
    }

    /// <summary>남은 양을 세는 물건 (소화기 물 · 이산화탄소 · 우주복 · 마스크 산소).</summary>
    private bool Charged(byte item) => item == iExt || item == iCO2 || item == iSuit || item == iMask;

    /// <summary>
    /// 손 비우기 걸음을 계획에 더한다 (빈손이면 아무것도 안 함): 급하면 그 자리에 내려놓기 · 아니면 제자리로 가서 넣기 (식량은 식량 창고 · 헌 부품은 정제기) —
    /// 둘 곳이 없으면 내려놓는다. 제자리로 갔으면 here가 그 자리가 된다 (다음 걸음의 '가까운 곳'을 거기서 고른다).
    /// </summary>
    private void FreeHands(Agent a, ref int here, List<PlanStep> plan, bool urgent)
    {
        byte item = a.C.Holding;
        if (item == 255) return;
        if (!urgent)
        {
            if (IsFood(item))
            {
                int store = Nearest(here, _food, _ => true);
                if (store >= 0) { plan.Add(Go(L.Furniture[store].Cells[0], 1)); plan.Add(new PlanStep { Kind = StepKind.Stow, Target = store }); here = L.Furniture[store].Cells[0]; return; }
            }
            else if (item == iScrap || item == iScrapElec)
            {
                int refinery = NearestRefinery(here);
                if (refinery >= 0) { plan.Add(Go(L.Furniture[refinery].Cells[0], 1)); plan.Add(new PlanStep { Kind = StepKind.Put, Target = refinery }); here = L.Furniture[refinery].Cells[0]; return; }
            }
            else if (item != iWaste)
            {
                int home = HomeFor(item, here);
                if (home >= 0) { plan.Add(Go(L.Furniture[home].Cells[0], 1)); plan.Add(new PlanStep { Kind = StepKind.Put, Target = home, Arg = 1 }); here = L.Furniture[home].Cells[0]; return; }
            }
        }
        plan.Add(new PlanStep { Kind = StepKind.Lay, Cell = here });
    }

    /// <summary>든 것이 이 일을 막나: 일상의 일 · 손일 앞에서는 든 물건을 먼저 비운다 (그 일에 쓰는 물건이면 아니다).</summary>
    private bool HoldBlocks(Agent a, GoalKind g)
    {
        byte item = a.C.Holding;
        if (item == 255) return false;
        return g switch
        {
            GoalKind.Sleep or GoalKind.Eat or GoalKind.Drink or GoalKind.Work or GoalKind.Relax or GoalKind.Socialize or GoalKind.Toilet or GoalKind.Wash
                or GoalKind.Exercise or GoalKind.Cook or GoalKind.Craft or GoalKind.Clean or GoalKind.Tighten or GoalKind.Counsel => true,
            GoalKind.Charge => a.C.Battery >= 0.3f,   // 전지가 바닥이면 든 채로 독에
            GoalKind.ToiletCare => item != iWaste,
            _ => false,
        };
    }

    /// <summary>돌려놓기 선택지: 둘 곳이 있는 물건을 들고 있고 지금 쓸 데가 없다 (불을 믿으면 소화기는 들고 있는다).</summary>
    private void StashOption(Agent a, int here)
    {
        ref var c = ref a.C;
        byte item = c.Holding;
        if (item == 255 || IsFood(item) || item == iWaste || item == iScrap || item == iScrapElec) return;
        // 방금(10분 안) 가져온 것은 들고 있는다 (쓰려고 가져왔다 — 잠깐 쉬어도) · 불을 끈 뒤에도 잠시 지켜본다
        if (_nowTick - c.HeldSince < Time.Minutes(10)) return;
        // 하던 일에 쓰려고 든 것 (정비하러 가는 부품 · 불 끄러 가는 소화기 …)은 돌려놓지 않는다 — 쉬는 · 노는 · 충전하는 중이면 돌려놓는다
        if (a.Plan.Count > 0 && c.StepIndex < a.Plan.Count && c.Goal is not (GoalKind.Idle or GoalKind.Relax or GoalKind.Socialize or GoalKind.Charge or GoalKind.Stash)) return;
        if ((item == iExt || item == iCO2 || item == iBlanket) && a.Beliefs.Any(b => b.Kind == BeliefKind.Fire && b.Conf >= 0.25f)) return;
        int home = HomeFor(item, here);
        if (home < 0) return;
        // 사람: 꼼꼼할수록 바로 돌려놓는다 (덤벙대는 이는 쉴 때나) · 로봇: 충전보다 앞서 (전지가 넉넉하면)
        float s = a.IsPerson ? 0.25f + 1.1f * c.Consc : 0.9f;
        Add(GoalKind.Stash, home, L.Furniture[home].Cells[0], s);
    }

    /// <summary>줍기 선택지: 맡긴 줍기 · 제 방 바닥에 떨어진 물건 (로봇 · 꼼꼼한 사람 — 보이면 줍는다) — 둘 곳이 있는 것만 (오물 통은 변기 손질이 줍는다).</summary>
    private void GatherOption(Agent a, int here)
    {
        ref var c = ref a.C;
        bool assigned = c.AssignedGoal == GoalKind.Gather && c.AssignedUntil > _nowTick;
        if (c.Holding != 255 && !assigned) return;
        if (a.IsPerson && !assigned && c.Consc < 0.6f) return;
        int room = RoomOf(here), best = -1, bd = int.MaxValue;
        var book = _st!; var props = book.Props;   // 바닥에 놓인 물건 (④ 물체 장부 — 놓은 차례)
        for (int i = 0; i < props.Count; i++)
        {
            var x = props[i];
            if (x.Kind != 0) continue;
            int xc = book.CellOf(x); byte xi = (byte)x.What;
            if (xi == iWaste || x.PlugAt != 0 || HomesOf(xi).Length == 0 && !IsFood(xi)) continue;
            bool mine = assigned && x.Id == c.AssignedTarget;   // 맡긴 그 물체 (일감 대상은 물체 Id)
            if (!mine && (room < 0 || RoomOf(xc) != room)) continue;   // 제 눈에 보이는 것 (같은 방)
            int d = mine ? -1 : PathDist(here, xc);
            if (d < bd) { bd = d; best = i; }
        }
        if (best < 0) return;
        var pick = props[best];   // 고른 그 물체를 Id로 넘긴다 (계획이 다른 물건을 집지 않게)
        Add(GoalKind.Gather, pick.Id, book.CellOf(pick), assigned && pick.Id == c.AssignedTarget ? 1.2f : a.IsPerson ? 0.6f * c.Consc : 0.8f);
    }

    /// <summary>바닥에 놓인 그 물체의 칸 (④ 물체 장부에서 쓸 때마다 읽는다 — 없거나 · 물건이 아니거나 · 구멍을 막았으면 −1).</summary>
    private int LooseCell(int id)
    {
        var book = _st!;
        int i = book.IndexOf(id);
        return i < 0 || book.Props[i].Kind != 0 || book.Props[i].PlugAt != 0 ? -1 : book.CellOf(book.Props[i]);
    }

    /// <summary>
    /// 줍는 걸음 앞: 그 물체가 손 닿는 곳(곁 칸)에 있나. 움직이는 중이면 멈출 때까지 기다리고 (1분 넘으면 막힘) ·
    /// 다른 칸으로 옮겨 갔으면 앞 걸음(가기)을 그 칸으로 고쳐 다시 간다. 참이면 이 틱에 줍는다.
    /// </summary>
    private bool Reach(Sim sim, Agent a, int id, int cell, int item)
    {
        ref var c = ref a.C;
        var p = _st!.Props[_st.IndexOf(id)];
        if (p.State is not (0 or 6))
        {
            if (sim.Tick - c.StepStart > Time.Minutes(1)) Fail(sim, a, "물건이 아직 움직인다", SnagKind.NoItem, cell, item);
            return false;
        }
        if (Dist(c.Cell, cell) <= 1) return true;
        if (c.StepIndex > 0 && a.Plan[c.StepIndex - 1].Kind == StepKind.GoTo)
        {
            var go = a.Plan[c.StepIndex - 1];
            go.Cell = cell; a.Plan[c.StepIndex - 1] = go;
            c.StepIndex--; a.Path.Clear(); a.PathIdx = 0;
            return false;
        }
        Fail(sim, a, "멀다", SnagKind.Far);
        return false;
    }

    /// <summary>돌려놓기 계획: 제자리로 가서 넣는다.</summary>
    private bool PlanStash(Agent a, Option o, List<PlanStep> plan)
    {
        if (a.C.Holding == 255 || o.Target < 0 || o.Target >= L.Furniture.Count) return false;
        plan.Add(Go(L.Furniture[o.Target].Cells[0], 1));
        plan.Add(new PlanStep { Kind = StepKind.Put, Target = o.Target, Arg = 1 });
        return true;
    }

    /// <summary>줍기 계획 (대상: 고른 물체 Id): 곁 칸으로 가서 줍고 → 제자리 (손이 차 있으면 먼저 비운다).</summary>
    private bool PlanGather(Agent a, Option o, int here, List<PlanStep> plan)
    {
        int at = LooseCell(o.Target);
        if (at < 0) return false;
        byte item = (byte)_st!.Props[_st.IndexOf(o.Target)].What;
        if (item == iWaste) return false;
        FreeHands(a, ref here, plan, false);
        plan.Add(Go(at, 1));   // 곁 칸까지 (물체 칸에 들어서지 않는다 — 손을 뻗어 줍는다)
        plan.Add(new PlanStep { Kind = StepKind.Gather, Cell = at, Target = o.Target, Arg = item });
        if (IsFood(item))
        {
            int store = Nearest(at, _food, _ => true);
            if (store < 0) return false;
            plan.Add(Go(L.Furniture[store].Cells[0], 1));
            plan.Add(new PlanStep { Kind = StepKind.Stow, Target = store });
            return true;
        }
        int home = HomeFor(item, at);
        if (home < 0) return false;
        plan.Add(Go(L.Furniture[home].Cells[0], 1));
        plan.Add(new PlanStep { Kind = StepKind.Put, Target = home, Arg = 1 });
        return true;
    }

    /// <summary>채울 곳 (설비): 물 소화기 — 물꼭지(싱크 · 물 재활용기) · 이산화탄소 소화기 — 이산화탄소 제거기 · 우주복 · 마스크 — 그 보관함.</summary>
    public int RefillPoint(byte item, int from, int box)
    {
        if (item == iSuit || item == iMask) return box;
        string[] types = item == iCO2 ? new[] { "Scrubber", "LifeSupport" } : new[] { "Sink", "WaterRecycler" };
        int best = -1, bd = int.MaxValue;
        foreach (var f in L.Furniture)
        {
            if (f.Cells.Length == 0 || Array.IndexOf(types, f.Type) < 0) continue;
            int d = PathDist(from, f.Cells[0]);
            if (d < bd) { bd = d; best = f.Id; }
        }
        return best;
    }

    /// <summary>채우기 계획 (맡긴 일 — 대상 보관함 · Arg 물건): 보관함에서 쓴 것을 꺼내 → 채울 곳에서 채우고 → 그 보관함에 돌려놓는다.</summary>
    private bool PlanRefill(Agent a, Option o, int here, List<PlanStep> plan)
    {
        int box = o.Target;
        byte item = (byte)a.C.AssignedArg;
        if (box < 0 || box >= L.Furniture.Count || !Charged(item)) return false;
        if (a.C.Holding == item && a.C.HoldCharge < ItemCharge(item) - 0.5)
        {
            // 이미 쓴 것을 들고 있다 (꺼낸 뒤 끊겼다) — 채울 곳으로
        }
        else
        {
            if (SpentIn(box, item) <= 0) return false;
            FreeHands(a, ref here, plan, false);
            plan.Add(Go(L.Furniture[box].Cells[0], 1));
            plan.Add(new PlanStep { Kind = StepKind.Take, Target = box, Arg = item, Value = 1 });   // Value 1: 쓴 것부터
        }
        int point = RefillPoint(item, L.Furniture[box].Cells[0], box);
        if (point < 0) return false;
        plan.Add(Go(L.Furniture[point].Cells[0], 1));
        plan.Add(new PlanStep { Kind = StepKind.Refill, Target = point, Arg = item });
        plan.Add(Go(L.Furniture[box].Cells[0], 1));
        plan.Add(new PlanStep { Kind = StepKind.Put, Target = box, Arg = 2 });
        return true;
    }

    /// <summary>그 보관함에 든 쓴 물건 수 (보관함 수보다 많지 않게).</summary>
    public int SpentIn(int fu, byte item)
    {
        int n = 0;
        foreach (var s in Spent) if (s.Furniture == fu && s.Item == item) n++;
        return Math.Min(n, Stock[fu * ItemCount + item]);
    }

    /// <summary>
    /// 보관함에서 하나 꺼낸다 — 남은 양과 함께 (가득 찬 것이 먼저 · spentFirst면 쓴 것부터 — 채우기).
    /// 보관함 수가 쓴 것 기록보다 적으면(타서 없어졌다 · 다른 길로 썼다) 남는 기록을 지운다.
    /// </summary>
    private bool TakeFrom(int fu, byte item, bool spentFirst, out float charge)
    {
        int k = fu * ItemCount + item;
        charge = 0;
        if (Stock[k] <= 0) return false;
        int spent = 0, low = -1, high = -1;
        for (int i = 0; i < Spent.Count; i++)
        {
            if (Spent[i].Furniture != fu || Spent[i].Item != item) continue;
            spent++;
            if (low < 0 || Spent[i].Charge < Spent[low].Charge) low = i;
            if (high < 0 || Spent[i].Charge > Spent[high].Charge) high = i;
        }
        while (spent > Stock[k]) { Spent.RemoveAt(low); spent--; low = high = -1; for (int i = 0; i < Spent.Count; i++) if (Spent[i].Furniture == fu && Spent[i].Item == item) { if (low < 0 || Spent[i].Charge < Spent[low].Charge) low = i; if (high < 0 || Spent[i].Charge > Spent[high].Charge) high = i; } }
        int pick = spentFirst && spent > 0 ? low : Stock[k] > spent ? -1 : high;
        Stock[k]--;
        if (pick >= 0) { charge = Spent[pick].Charge; Spent.RemoveAt(pick); }
        else charge = (float)ItemCharge(item);
        return true;
    }

    /// <summary>든 물건을 보관함에 넣는다 — 덜 찬 것이면 남은 양을 적어 둔다.</summary>
    private void StoreHeld(int fu, Agent a)
    {
        ref var c = ref a.C;
        Stock[fu * ItemCount + c.Holding]++;
        if (Charged(c.Holding) && c.HoldCharge < ItemCharge(c.Holding) - 0.5) Spent.Add(new SpentItem { Furniture = fu, Item = c.Holding, Charge = Math.Max(0, c.HoldCharge) });
        c.Holding = 255; c.HoldCharge = 0;
    }

    private void LayStep(Sim sim, Agent a)
    {
        if (a.C.Holding != 255) { DropHeld(a); Laid++; }
        Next(a, sim.Tick);
    }

    /// <summary>바닥 물건을 줍는다 (그 물체 Id — 칸은 장부에서 · 곁 칸에서 손을 뻗는다 · 없으면 끝).</summary>
    private void GatherStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        var book = _st!;
        int at = LooseCell(st.Target);
        if (at < 0) { if (c.AssignedGoal == GoalKind.Gather) JobDone(a, t); else EndPlan(a); return; }   // 누가 벌써 주웠다
        if (!Reach(sim, a, st.Target, at, st.Arg)) return;
        var got = book.Props[book.IndexOf(st.Target)];
        if (c.Holding != 255) DropHeld(a);
        c.Holding = (byte)got.What; c.HoldCharge = got.Charge;
        book.TakeItem(got.Id);
        Gathered++;
        Next(a, t);
    }

    /// <summary>채운다: 채울 곳 곁에서 — 월드에 요청 (물탱크 물 · 모은 이산화탄소 · 방 공기의 산소) · 채우는 데 몇 분.</summary>
    private void RefillStep(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        if (c.Holding != st.Arg || !Charged(c.Holding)) { Fail(sim, a, "채울 것을 들고 있지 않다", SnagKind.NoItem, c.Cell, st.Arg); return; }
        if (st.Target < 0 || st.Target >= L.Furniture.Count || Dist(c.Cell, L.Furniture[st.Target].Cells[0]) > 2) { Fail(sim, a, "멀다", SnagKind.Far); return; }
        Activity(a, "work");
        if (t - c.StepStart < Time.Minutes(3)) return;
        double need = ItemCharge(c.Holding) - c.HoldCharge;
        double got = need <= 0 || _net == null ? 0 : c.Holding == iExt ? _net.FillExtinguisher(sim, need) : c.Holding == iCO2 ? _net.FillCO2(need) : _net.FillO2(sim, L.Furniture[st.Target].Cells[0], need);
        if (got <= 0 && need > 0.5) { Fail(sim, a, c.Holding == iExt ? "물탱크에 물이 없다" : c.Holding == iCO2 ? "모아 둔 이산화탄소가 없다" : "산소를 걸러 낼 공기가 없다", SnagKind.NoItem, c.Cell, c.Holding); return; }
        float before = c.HoldCharge;
        c.HoldCharge = (float)(before + got);
        Refilled++;
        Next(a, t);
    }

    /// <summary>우주복 · 마스크를 벗어 손에 든다 (손이 차 있으면 그 자리에 내려놓는다 — 줍기) · 남은 산소 그대로.</summary>
    private void HoldTakenOff(Agent a, byte item, float charge)
    {
        ref var c = ref a.C;
        if (c.Holding == 255) { c.Holding = item; c.HoldCharge = charge; return; }
        _st!.PutItem(item, charge, c.Cell);
        Laid++;
    }

    private void SaveHands(StateWriter w) { w.List(Spent); w.I64(Stashed); w.I64(Refilled); w.I64(Gathered); w.I64(Laid); }
    private void LoadHands(StateReader r) { Spent = r.List<SpentItem>(); Stashed = r.I64(); Refilled = r.I64(); Gathered = r.I64(); Laid = r.I64(); }
}
