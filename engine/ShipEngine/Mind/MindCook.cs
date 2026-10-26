namespace ShipEngine;

/// <summary>
/// ⑧ 요리 · 실수에서 배우기 (깊이 바퀴 2). 끼니때 요리를 좋아하는 이(조리 솜씨 · 열림)는 조리대에서 밥을 짓는다 —
/// 켜고(냄비에 물 1L · 음식 냄새가 퍼진다) · 15분 뒤 끄고 · 먹는다. 끄는 것을 잊는 것은 사람의 상태에서 나온다:
/// 다 지어 놓고 깜빡 (덜 성실할수록 · 졸릴수록 · 긴장할수록 — 끼니 백에 두엇) · 하다 말고 급한 일이 생기면 (급한 소리 · 쓰러짐 — 반쯤).
/// 켜 둔 조리대가 일을 내면 (주컴퓨터가 눌는 연기에 전원을 내렸다 · 불이 붙었다) 켜 둔 이는 배운다: 조심이 오르고 (이레에 반쯤 흐려진다) 그만큼 덜 잊는다.
/// </summary>
public sealed partial class Mind
{
    public long Cooked, Forgot;
    private int[] _stoves = Array.Empty<int>();
    private int[] _stoveFood = Array.Empty<int>();   // 조리대마다 같은 방의 가장 가까운 식량 설비 (−1 없음)
    private long _learnCursor;

    private void BindStoves()
    {
        if (_net == null || _stoves.Length > 0 && _stoveFood.Length == _stoves.Length) return;
        var list = new List<int>();
        foreach (var d in _net.Devices) if (d.Def.Role == "stove") list.Add(d.Id);
        _stoves = list.ToArray();
        _stoveFood = new int[_stoves.Length];
        for (int k = 0; k < _stoves.Length; k++)
        {
            int cell = _net.Devices[_stoves[k]].Cell, room = RoomOf(cell), best = -1, bd = int.MaxValue;
            foreach (int fu in _food)
            {
                if (L.Furniture[fu].Room != room) continue;
                int d = Manhattan(cell, L.Furniture[fu].Cells[0]);
                if (d < bd) { bd = d; best = fu; }
            }
            _stoveFood[k] = best;
        }
    }

    /// <summary>이 끼니에 밥을 지을 마음이 있나 — 조리 솜씨 · 열림 (세 시간마다 다시 정한다 · 같은 시드면 같다). 너무 배고프면 바로 먹는다.</summary>
    private bool WantsToCook(Sim sim, Agent a)
    {
        ref var c = ref a.C;
        if (c.Hunger > 0.9f) return false;
        float like = 0.15f + 0.6f * c.SkCook + 0.2f * c.Open;
        return Rng.Unit(sim.Seed, Rng.Key("요리"), a.Id, sim.Tick / Time.Hours(3)) < like;
    }

    /// <summary>밥을 지을 조리대 (꺼져 있고 · 멀쩡하고 · 전기가 오고 · 남이 쓰지 않고 · 같은 방에 먹을 것이 있는 · 가장 가까운) — 없으면 −1.</summary>
    private int CookStove(Agent a)
    {
        if (_net == null) return -1;
        BindStoves();
        int best = -1, bd = int.MaxValue;
        for (int k = 0; k < _stoves.Length; k++)
        {
            int d = _stoves[k], fu = _stoveFood[k];
            bool mine = a.C.CookOn == d + 1 || a.C.Goal == GoalKind.Cook && a.C.GoalTarget == d; // 지금 내가 쓰는 조리대 (켜져 있어도)
            if (fu < 0 || _net.On[d] != 0 && !mine || _net.IsBroken(d, Part.Electric) || FoodAt(fu) < 1) continue;
            var dev = _net.Devices[d];
            if (dev.Room < 0 || !_net.RoomPowered(dev.Room)) continue;
            bool used = false;
            foreach (var o in Agents) if (o != a && (o.C.CookOn == d + 1 || o.C.Goal == GoalKind.Cook && o.C.GoalTarget == d)) { used = true; break; }
            if (used) continue;
            int dist = Manhattan(a.C.Cell, dev.Cell);
            if (dist < bd) { bd = dist; best = d; }
        }
        return best;
    }

    private int StoveFood(int dev)
    {
        BindStoves();
        int k = Array.IndexOf(_stoves, dev);
        return k >= 0 ? _stoveFood[k] : -1;
    }

    private bool BuildCook(Agent a, Option o, List<PlanStep> plan)
    {
        if (_net == null || o.Target < 0) return false;
        int src = StoveFood(o.Target);
        if (src < 0) return false;
        int sc = _net.Devices[o.Target].Cell, fc = L.Furniture[src].Cells[0];
        plan.Add(Go(sc, 1));
        plan.Add(Use(UseKind.Cook, Time.Minutes(15), sc, o.Target));
        plan.Add(Go(fc, 1));
        plan.Add(Use(UseKind.Eat, Time.Minutes(10), fc, src));
        return true;
    }

    /// <summary>밥 짓기 한 틱: 처음에 켠다 (냄비에 물 1L — 탱크에서) · 다 되면 끈다 (깜빡할 수 있다). 음식 냄새는 뜨거운 냄비에서 월드가 낸다.</summary>
    private void CookStep(Sim sim, Agent a, PlanStep st, bool done)
    {
        if (_net == null || st.Target < 0) return;
        ref var c = ref a.C;
        int d = st.Target;
        if (c.CookOn != d + 1)
        {
            c.CookOn = d + 1;
            if (_net.On[d] == 0) _net.SetOn(d, true);
            _net.FillPan(sim, d);
            if (a.IsPerson) _life?.Touch(c.Body, _net.Devices[d].Furniture);   // 조리대 · 냄비를 만진다 (5단 C21 — 월드가 맞바꾼다)
        }
        if (done && a.IsPerson && _life != null)
        {
            var (pan, _, burnt) = _life.HandlePan(sim, c.Body, d, c.Sleepy, Math.Clamp(c.Stress, 0, 1), c.Careful, a.Id, NameOf(a.Id));   // 냄비를 내린다 (월드가 쏟음을 셈한다 — 5단 C10)
            if (pan > 0) AfterSpill(sim, a, _net.Devices[d].Cell, burnt);   // 쏟은 이가 닦거나 그냥 간다 (5단 C11)
        }
        if (done) { Cooked++; if (a.IsPerson) a.C.SkCook = Math.Min(1, a.C.SkCook + 0.003f * Practice(a.C, "조리") * UseSkill(a, 4, sim.Tick)); FinishCook(sim, a, false); }
    }

    /// <summary>하던 밥 짓기를 놓았나 (계획이 바뀌었다 · 쓰러졌다) — 놓았으면 조리대를 끄거나 · 잊는다.</summary>
    private void WithdrawCook(Sim sim, Agent a)
    {
        ref var c = ref a.C;
        if (c.CookOn == 0) return;
        bool still = c.Up != 0 && c.Goal == GoalKind.Cook && c.StepIndex < a.Plan.Count && a.Plan[c.StepIndex].Kind == StepKind.Use && (UseKind)a.Plan[c.StepIndex].Arg == UseKind.Cook;
        if (!still) FinishCook(sim, a, true);
    }

    /// <summary>조리대를 끈다 — 아니면 잊는다: 다 지어 놓고 깜빡 (끼니 백에 두엇 · 덜 성실 · 졸림 · 긴장) / 하다 말고 (반쯤 · 급하면 더). 배운 조심만큼 덜 잊는다.</summary>
    private void FinishCook(Sim sim, Agent a, bool interrupted)
    {
        ref var c = ref a.C;
        int d = c.CookOn - 1;
        c.CookOn = 0;
        if (_net == null || d < 0 || _net.On[d] == 0) return;
        bool urgent = interrupted && (c.Up == 0 || c.Mode != 0 || c.Goal is GoalKind.Evacuate or GoalKind.Flee or GoalKind.Panic or GoalKind.FightFire or GoalKind.Rescue or GoalKind.Patch or GoalKind.Investigate or GoalKind.GetSuit or GoalKind.Down);
        float p = interrupted ? (0.35f + 0.4f * (1 - c.Consc)) * (urgent ? 1.3f : 0.6f) : 0.02f * (1.4f - c.Consc) * (0.6f + c.Sleepy + 0.5f * c.Stress);
        p = Math.Min(0.9f, p * (1 - c.Careful) * (1 - 0.5f * c.LessonFire) * (StoveRule ? 0.5f : 1));   // 실수에서 배운 조심 · 불을 겪어 본 경계 · ⑨ 배의 규칙
        if (Rng.Unit(sim.Seed, Rng.Key("깜빡"), a.Id, sim.Tick) >= p) { _net.SetOn(d, false); return; }
        string why = c.Up == 0 ? "쓰러졌다" : urgent ? "급한 소리에 달려 나갔다" : interrupted ? "다른 일에 정신이 팔렸다" : c.Sleepy > 0.6f ? "졸려서 깜빡했다" : "깜빡했다";
        var dev = _net.Devices[d];
        int cause = sim.Causes.Add(CauseKind.Person, $"{Ko.I(NameOf(a.Id))} {Ko.Eul(dev.Name)} 켜 둔 채 자리를 떴다 — {why}", sim.Tick, dev.Cell, 0, 0, a.Id);
        _net.OnCause[d] = cause;
        Forgot++;
        sim.Facts.Emit(sim.Tick, FactKind.Mistake, dev.Cell, p, cause, a.Id, d);
    }

    /// <summary>1분마다: 제 실수가 일을 냈나 — 켜 둔 조리대가 꺼졌다(주컴퓨터 · 남이) · 불이 붙었다 → 켜 둔 이는 조심이 오른다 (불 0.6 · 남이 끈 것 0.3).</summary>
    private void Learn(Sim sim)
    {
        foreach (var f in sim.Facts.Since(_learnCursor))
        {
            if (f.Kind is not (FactKind.Ignition or FactKind.Switched) || f.Cause == 0) continue;
            int root = sim.Causes.Root(f.Cause);
            var rc = sim.Causes[root];
            if (rc.Kind != CauseKind.Person || rc.Actor < 0 || rc.Actor >= Agents.Count || !Agents[rc.Actor].IsPerson || f.Actor == rc.Actor) continue;
            ref var c = ref Agents[rc.Actor].C;
            c.Careful = Math.Min(1, c.Careful + (f.Kind == FactKind.Ignition ? 0.6f : 0.3f));
        }
        _learnCursor = sim.Facts.Total;
    }
}
