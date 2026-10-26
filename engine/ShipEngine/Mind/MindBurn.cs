namespace ShipEngine;

/// <summary>
/// ⑧ 몸이 받는 가속 (⑤ 바퀴 2 · docs/ENGINE.md ⑤ 기동): 배가 갑자기 가속하면 서 있는 사람이 넘어진다.
/// 선 승객의 균형 한계 0.1~0.15g (대중교통 연구 · Hoberock 1977 — 저크 0.3g/s 아래): 넘어질 몫 = ((한 틱에 오른 가속 − 0.08g) ÷ 0.25g)를 0~1로 —
/// 저크가 0.3g/s 아래면 (정해진 점화는 20초에 걸쳐 올린다) 몸이 따라가 (저크/0.3)²만큼만 · 갑자기 다 켜면(운석 비키기) 그대로.
/// 방송을 듣고 붙잡았으면 ×0.05 (10초 넘게 앞서 들었으면 · 막 들었으면 ×0.5) · 앉아 일하거나 먹는 중 ×0.3 · 젖은 바닥 ×2 · 사람이나 무거운 것을 들었으면 ×1.5 · 누운 이(자는 이 · 쓰러진 이)는 0.
/// 넘어지면 타박 (3% + (가속 − 0.2g)×20%로 뼈가 다친다 — 몸은 ⑦ Life.Fall이 셈한다) · 업던 사람 · 든 것을 놓친다.
/// </summary>
public sealed partial class Mind
{
    public long Falls, BracedOnBurn;
    public const double JerkG = 0.3;   // 선 승객이 버티는 저크 (g/s — Hoberock 1977)
    private double _lastG;
    private Outside? _out;

    /// <summary>갑자기 오른 가속(g)에 선 사람이 넘어질 몫.</summary>
    public static double FallChance(double jumpG) => Math.Clamp((jumpG - 0.08) / 0.25, 0, 1);

    private void Inertia(Sim sim)
    {
        if (_out == null || _life == null) return;
        double g = Math.Sqrt(_out.Ax * _out.Ax + _out.Ay * _out.Ay) / Outside.G0;
        double jump = g - _lastG;
        _lastG = g;
        if (jump < 0.03) return;   // 천천히 오르면 버틴다
        // 저크(g/s): 정해진 점화는 20초에 걸쳐 올린다 — 0.3g/s 아래면 몸이 따라간다 (Hoberock) · 갑자기 다 켜면 저크가 끝없이 크다
        double jerk = _out.BurnRampSec > 0 ? _out.BurnAccel / Outside.G0 / _out.BurnRampSec : double.PositiveInfinity;
        double soft = jerk >= JerkG ? 1 : (jerk / JerkG) * (jerk / JerkG);
        long t = sim.Tick;
        foreach (var a in Agents)
        {
            if (!a.IsPerson || a.C.Up == 0 || a.C.Body < 0 || a.C.CarriedBy >= 0) continue;
            var b = _life[a.C.Body];
            if (b.Alive == 0 || b.Conscious == 0 || b.Asleep != 0) continue;
            double p = FallChance(jump) * soft;
            if (p <= 0) continue;
            // 방송을 듣고 붙잡았나 (10초 넘게 앞서 들었으면 · 막 들었으면 반쯤)
            float braced = 1;
            foreach (var bl in a.Beliefs)
                if (bl.Kind == BeliefKind.Burn && bl.Conf >= 0.3f && t - bl.Seen < Time.Minutes(10))
                    braced = Math.Min(braced, t - bl.Seen >= 4 ? 0.05f : 0.5f);
            if (braced < 1) BracedOnBurn++;
            p *= braced;
            if (Seated(a)) p *= 0.3;                                                                     // 의자 · 침대 · 조종대에 앉아 있다
            if (F.Stored(T.sWater, a.C.Cell) > 50) p *= 2;                                                 // 젖은 바닥
            if (a.C.Carrying >= 0 || a.C.Holding != 255 && ItemKg(a.C.Holding) >= JamKg) p *= 1.5;
            if (Rng.Unit(sim.Seed, Rng.Key("넘어짐"), t, a.Id) >= Math.Min(0.95, p)) continue;
            FallDown(sim, a, g, "점화에", _out!.BurnCause);
        }
    }

    /// <summary>
    /// ④ 칸 강체 — 덜컹임: 몸(배 · 떨어져 나간 조각)이 갑자기 밀리거나 돌기 시작하면 (뿜는 공기 · 갈라짐 · 부딪힘) 그 자리에서 느낀 가속만큼 선 사람이 넘어진다
    /// (점화와 같은 잣대 — 갑자기 오른 가속이라 저크는 끝없이 크다 · 앉았으면 ×0.3 · 젖은 바닥 ×2 · 든 것이 있으면 ×1.5 · 누운 이는 0).
    /// </summary>
    private void BodyJolt(Sim sim)
    {
        if (_st == null || _st.Bodies.Count < 2 || _life == null) return;
        long t = sim.Tick;
        foreach (var a in Agents)
        {
            if (!a.IsPerson || a.C.Up == 0 || a.C.Body < 0 || a.C.CarriedBy >= 0) continue;
            double g = _st.JoltG(a.C.Cell);   // 그 자리 바닥의 덜컹임 (④가 셈한다)
            double p = FallChance(g);
            if (p <= 0) continue;
            var b = _life[a.C.Body];
            if (b.Alive == 0 || b.Conscious == 0 || b.Asleep != 0) continue;
            if (Seated(a)) p *= 0.3;
            if (F.Stored(T.sWater, a.C.Cell) > 50) p *= 2;
            if (a.C.Carrying >= 0 || a.C.Holding != 255 && ItemKg(a.C.Holding) >= JamKg) p *= 1.5;
            if (Rng.Unit(sim.Seed, Rng.Key("덜컹 넘어짐"), t, a.Id) >= Math.Min(0.95, p)) continue;
            FallDown(sim, a, g, "덜컹임에", _st.BodyCause(a.C.Cell));
        }
    }

    /// <summary>앉아 있나: 그 자리에서 하는 일(쉬기 · 먹기 · 일 · 어울리기) 중이고 곁에 의자 · 침대 · 조종대가 있다 (멍하니 서 있는 것은 아니다).</summary>
    private bool Seated(Agent a)
    {
        if (a.C.StepIndex >= a.Plan.Count || a.Plan[a.C.StepIndex].Kind != StepKind.Use) return false;
        if (a.C.Goal is not (GoalKind.Relax or GoalKind.Eat or GoalKind.Work or GoalKind.Socialize)) return false;
        int x0 = L.X(a.C.Cell), y0 = L.Y(a.C.Cell);
        for (int y = y0 - 1; y <= y0 + 1; y++)
            for (int x = x0 - 1; x <= x0 + 1; x++)
            {
                if (!L.In(x, y)) continue;
                int fu = L.FurnitureAt[L.Index(x, y)];
                if (fu >= 0 && L.Furniture[fu].Type is "Seat" or "Bed" or "Console") return true;
            }
        return false;
    }

    /// <summary>넘어졌다: 몸은 월드가 셈하고 (⑦ Life.Fall — 타박 · 뼈 · 업혀 있던 이 · 사실) · 마음은 겁이 나고 든 것을 떨군다.</summary>
    private void FallDown(Sim sim, Agent a, double g, string how, int parent, bool showG = true)
    {
        long t = sim.Tick;
        int cell = a.C.Cell, vb = a.C.Carrying;
        if (vb >= 0) DropCarried(sim, a, false);   // 업던 이를 놓친다 (바닥에 부딪힌 몸은 월드가)
        _life!.Fall(sim, a.C.Body, g, how, parent, a.Id, NameOf(a.Id), a.Id, showG, vb);
        Fallen(a, t);
        Falls++;
    }

    /// <summary>넘어진 뒤 마음: 든 것을 떨구고 (걷던 빠르기로 원판 바깥 앞쪽에) · 멈추고 · 겁이 난다 (업던 이는 넘어지기 전에 놓았다).</summary>
    private void Fallen(Agent a, long t)
    {
        if (a.C.Holding != 255) { int id = PutDown(a, a.C.Holding, a.C.HoldCharge, true); if (a.C.Holding == iWaste) WasteDown.Add(id); a.C.Holding = 255; a.C.HoldCharge = 0; }   // 걷던 빠르기로 날아간다 (원판 바깥 앞쪽에서)
        a.C.MoveBudget = 0; a.C.StepStart = t;
        a.C.Fear = Math.Min(1, a.C.Fear + 0.2f);
    }

    /// <summary>⑩ 반가운 교신 (지나가는 배와 소식을 나눴다): 배 사람 모두 기쁨이 조금 오르고 외로움이 준다.</summary>
    public void Cheer(Sim sim, float amount, int cause)
    {
        foreach (var a in Agents)
        {
            if (!a.IsPerson) continue;
            ref var c = ref a.C;
            c.Joy = Math.Min(1, c.Joy + amount); c.Lonely = Math.Max(0, c.Lonely - amount); c.Bored = Math.Max(0, c.Bored - amount * 0.5f);
        }
    }

    /// <summary>⑩ 조난선에 가지 않기로 했다: 다정한 이일수록 마음이 무겁다 (슬픔 · 긴장).</summary>
    public void Distressed(Sim sim, int crew, int cause)
    {
        foreach (var a in Agents)
        {
            if (!a.IsPerson) continue;
            ref var c = ref a.C;
            float k = Math.Clamp(c.Kindness, 0, 1) * Math.Min(1, 0.3f + 0.15f * crew);
            c.Sad = Math.Min(1, c.Sad + 0.3f * k); c.Stress = Math.Min(1, c.Stress + 0.2f * k);
        }
    }

    /// <summary>⑩ 난파선에서 건진 수리 꾸러미 · 갈래 부품 · 전자 소자를 가장 가까운 작업대에 둔다 (⑪ 공급 사슬).</summary>
    public void Salvage(Sim sim, int nearCell, int kits, int cause, int[]? parts = null, int chips = 0)
    {
        int best = -1, bd = int.MaxValue;
        foreach (var fu in L.Furniture)
        {
            if (fu.Type != "Workbench" || fu.Cells.Length == 0) continue;
            int d = Dist(fu.Cells[0], nearCell);
            if (d < bd) { bd = d; best = fu.Id; }
        }
        if (best < 0) return;
        Stock[best * ItemCount + iRepair] += kits;
        sim.Book.AddItem(iRepair, Flow.Salvaged, kits);
        if (parts != null) for (int p = 0; p < 4 && p < parts.Length; p++) { Stock[best * ItemCount + iSpare[p]] += parts[p]; sim.Book.AddItem(iSpare[p], Flow.Salvaged, parts[p]); }
        Stock[best * ItemCount + iChip] += chips;
        sim.Book.AddItem(iChip, Flow.Salvaged, chips);
    }

    /// <summary>⑪ 보급: 다른 배 · 정거장에서 받은 것을 에어락 곁 작업대에 둔다 (보급 물건 차례 — <see cref="Outside.SupplyItems"/> · 장부 '보급').</summary>
    public void Supplied(Sim sim, int nearCell, int[] counts)
    {
        int best = -1, bd = int.MaxValue;
        foreach (var fu in L.Furniture)
        {
            if (fu.Type != "Workbench" || fu.Cells.Length == 0) continue;
            int d = Dist(fu.Cells[0], nearCell);
            if (d < bd) { bd = d; best = fu.Id; }
        }
        if (best < 0) return;
        for (int i = 0; i < counts.Length && i < Outside.SupplyItems.Length; i++)
        {
            int item = ItemIndex(Outside.SupplyItems[i]);
            if (item < 0 || counts[i] <= 0) continue;
            Stock[best * ItemCount + item] += counts[i];
            sim.Book.AddItem(item, Flow.Supplied, counts[i]);
        }
    }
}
