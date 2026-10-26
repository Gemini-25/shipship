namespace ShipEngine;

/// <summary>
/// ⑪ → ⑧ 배급 (깊이 바퀴 2). 주컴퓨터(⑩)가 하루 몫을 정하면(요청) 마음이 지킨다 — 배식기 · 냉장고 · 물 나오는 곳은 사람마다 0시부터 받은 몫을 잰다 (몫을 넘게 주지 않는다).
/// 몫을 다 받고도 배고프면 참는다 — 짜증(덜 다정할수록) · 긴장(신경성일수록)이 오르고 기쁨이 준다 (미네소타 굶주림 실험 1944–45: 먹을 것 생각 · 짜증 · 감추기).
/// 덜 성실하고 덜 다정한 이는 몰래 더 먹는다: 몫을 다 먹었고 몹시 배고프면 재지 않는 식량 보관함(포장 식량)에서 한 끼를 꺼내 먹는다 —
/// 곁에 깨어 있는 이가 있으면 덜 한다 (들킬까 봐). 사실 Pilfer (본 이의 원망은 ⑨) · 나르기로 꺼낸 것만 기록에 남는다 (주컴퓨터의 무게 센서가 견준다).
/// </summary>
public sealed partial class Mind
{
    public double RationFood, RationWater;   // 한 사람 하루 몫 (양분 몰 · 물 몰) — 0이면 배급이 없다
    public int RationCause;                   // 배급을 연 원인 마디 (주컴퓨터)
    public long Refused, Pilfered;
    public double PilferedMol;
    public double[] LockerLogged = Array.Empty<double>();   // 보관함마다 기록된 꺼냄 (나르기 일 — 몰래 꺼낸 것은 없다)
    private double[] _ateToday = Array.Empty<double>(), _drankToday = Array.Empty<double>();
    private long _rationDay = -1;
    private int[]? _foodLockersA;
    private int[] FoodLockers => _foodLockersA ??= L.Furniture.Where(fu => fu.Cells.Length > 0 && T.Equip(fu.Type).Sealed).Select(fu => fu.Id).ToArray();

    /// <summary>주컴퓨터의 요청: 한 사람 하루 몫을 정한다 (0 · 0이면 푼다).</summary>
    public void SetRation(double food, double water, int cause) { RationFood = food; RationWater = water; RationCause = cause; }

    /// <summary>오늘(0시부터) 더 받을 수 있는 몫 — 배급이 없으면 끝없다.</summary>
    public double FoodLeft(int agent) => RationFood <= 0 || Defying(agent, _nowTick) ? double.MaxValue : Math.Max(0, RationFood - (agent < _ateToday.Length ? _ateToday[agent] : 0));   // ⑨ 배급 반란에 찬성한 이는 배식기 몫을 넘는다
    public double WaterLeft(int agent) => RationWater <= 0 ? double.MaxValue : Math.Max(0, RationWater - (agent < _drankToday.Length ? _drankToday[agent] : 0));
    /// <summary>의료 몫: 몸의 물이 3% 넘게 모자란 이(화상 · 열 · 피 흘림)는 배급과 상관없이 모자란 만큼 마신다 (배급의 원칙 — 아픈 이 · 다친 이에게 먼저 · 찾아 고친 결함: 덴 이가 하루 몫에 막혀 목이 말라 죽었다).</summary>
    public double WaterAllowed(int agent)
    {
        double left = WaterLeft(agent);
        if (left == double.MaxValue || _life == null || agent >= Agents.Count || !Agents[agent].IsPerson) return left;
        double deficit = BodyWater0 - _life.Bodies[Agents[agent].C.Body].Water;
        return deficit > 0.03 * BodyWater0 ? Math.Max(left, deficit) : left;
    }
    public double AteToday(int agent) => agent < _ateToday.Length ? _ateToday[agent] : 0;
    public double DrankToday(int agent) => agent < _drankToday.Length ? _drankToday[agent] : 0;

    private void RationDay(long tick)
    {
        if (_ateToday.Length < Agents.Count) { Array.Resize(ref _ateToday, Agents.Count); Array.Resize(ref _drankToday, Agents.Count); }
        if (LockerLogged.Length < L.Furniture.Count) Array.Resize(ref LockerLogged, L.Furniture.Count);
        long day = tick / Time.TicksPerDay;
        if (day == _rationDay) return;
        _rationDay = day; Array.Clear(_ateToday); Array.Clear(_drankToday);
    }

    private void CountAte(Agent a, double mol) { if (a.Id < _ateToday.Length) _ateToday[a.Id] += mol; }
    private void CountDrank(Agent a, double mol) { if (a.Id < _drankToday.Length) _drankToday[a.Id] += mol; }

    /// <summary>몫을 다 받고도 배고프다 — 참는다: 짜증 · 긴장 · 기쁨이 준다 (욕구 갱신에서).</summary>
    private void RationHunger(Agent a, double h, bool asleep)
    {
        ref var c = ref a.C;
        if (RationFood <= 0 || asleep || c.Hunger < 0.6f || FoodLeft(a.Id) >= 0.2) return;
        float over = c.Hunger - 0.6f;
        c.Anger = (float)Math.Min(1, c.Anger + h * over * 2 * (1.3 - c.Agree));   // 짜증은 4시간에 반쯤 가라앉는데(욕구 갱신) 굶주림이 그보다 빨리 쌓는다
        c.Stress = (float)Math.Min(1, c.Stress + h * over * 0.6 * (0.4 + c.Neuro));
        c.Joy = (float)Math.Max(0, c.Joy - h * over);
    }

    /// <summary>몰래 한 끼: 몫을 다 먹었고 몹시 배고픈 덜 성실 · 덜 다정한 이 — 먹을 것이 든 가장 가까운 보관함 (곁에 깨어 있는 이가 있으면 덜).</summary>
    private void PilferOption(Agent a, int here)
    {
        ref var c = ref a.C;
        bool defy = Defying(a.Id, _nowTick);   // ⑨ 배급 반란 — 드러내 놓고 연다
        if (RationFood <= 0 || c.Hunger < 0.65f || !defy && FoodLeft(a.Id) >= 0.2) return;
        float bent = defy ? 1 : (1 - c.Consc) * (1 - 0.7f * c.Agree);
        if (bent < 0.35f) return;
        int lk = Nearest(here, FoodLockers, fu => FoodAt(fu) >= MealMol);
        if (lk < 0) return;
        int room = RoomOf(L.Furniture[lk].Cells[0]);
        bool watched = false;
        foreach (var o in Agents)
            if (o != a && o.IsPerson && o.C.Up != 0 && RoomOf(o.C.Cell) == room && _life![o.C.Body].Asleep == 0 && _life[o.C.Body].Conscious != 0) { watched = true; break; }
        Add(GoalKind.Pilfer, lk, L.Furniture[lk].Cells[0], (c.Hunger - 0.6f) * 8 * bent * (watched && !defy ? 0.25f : 1));
    }

    /// <summary>보관함에서 한 끼를 몰래 꺼내 먹었다 (3분 — 다 마쳤을 때).</summary>
    private void PilferDone(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        double got = 0;
        foreach (int x in L.Furniture[st.Target].Cells) { got += F.TakeStore(x, T.sFood, Math.Max(0, MealMol - got)); if (got >= MealMol) break; }
        if (got <= 0) return;
        _life!.EatBy(sim, c.Body, got, st.Target); FoodEaten += got; sim.Book.Add(Res.Food, Flow.Eaten, -got);
        c.Hunger = (float)Math.Max(0, c.Hunger - got); c.AteAt = sim.Tick; Meals++;
        c.Stress = Math.Min(1, c.Stress + 0.05f * c.Consc);   // 마음이 편하지는 않다
        Pilfered++; PilferedMol += got;
        int cause = sim.Causes.Add(CauseKind.Person, $"{Ko.I(NameOf(a.Id))} 식량 보관함에서 몫 밖의 한 끼를 몰래 꺼내 먹었다", sim.Tick, c.Cell, RationCause, 0, a.Id);
        sim.Facts.Emit(sim.Tick, FactKind.Pilfer, c.Cell, (float)got, cause, a.Id, st.Target);
    }

    private void SaveRation(StateWriter w)
    {
        w.F64(RationFood); w.F64(RationWater); w.I32(RationCause); w.I64(Refused); w.I64(Pilfered); w.F64(PilferedMol);
        w.Arr(LockerLogged); w.Arr(_ateToday); w.Arr(_drankToday); w.I64(_rationDay);
    }
    private void LoadRation(StateReader r)
    {
        RationFood = r.F64(); RationWater = r.F64(); RationCause = r.I32(); Refused = r.I64(); Pilfered = r.I64(); PilferedMol = r.F64();
        LockerLogged = r.Arr<double>(); _ateToday = r.Arr<double>(); _drankToday = r.Arr<double>(); _rationDay = r.I64();
    }
}
