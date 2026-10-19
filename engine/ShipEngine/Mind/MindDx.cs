namespace ShipEngine;

/// <summary>
/// ⑦ (다) 진단 — 돌보는 이가 증상으로 병을 짐작한다 (몸 지식 · 도구만큼 · 틀리면 닮은 병으로) · 진료 기록에 남긴다 · 본 대로 치료한다 ·
/// 낫지 않거나 더 나빠지면 주컴퓨터가 다시 보게 한다 (docs/ENGINE.md '장기 · 약 · 수술' (다)).
/// </summary>
public sealed partial class Mind
{
    public long Diagnoses, Misdiagnoses, Corrected;
    private bool[]? _roomBed, _roomScan;
    private HashSet<int>? _medBedCells;

    private bool[] RoomsWith(string type)
    {
        var r = new bool[L.Rooms.Count];
        foreach (var fu in L.Furniture) if (fu.Type == type && fu.Room >= 0 && fu.Room < r.Length) r[fu.Room] = true;
        return r;
    }

    /// <summary>그 칸이 치료 침대인가 (산소 요법).</summary>
    private bool IsMedBedCell(int cell) => (_medBedCells ??= _medBeds.SelectMany(fu => L.Furniture[fu].Cells).ToHashSet()).Contains(cell);

    /// <summary>
    /// 돌보는 이가 그 몸을 본다 — 진료 기록(그 사람의)에 남기고 본 병을 돌려준다.
    /// 맞힐 몫: <see cref="Life.DxChance"/> (그 방의 의료 침대 · 진단 장치 · 로봇의 기체 센서) · 하루 안에 다시 보는 것이면 앞서 본 병을 빼고 보아 +0.3.
    /// 바로잡으면 앞서 잘못 본 이(사람)의 몸 지식이 는다 (+0.05).
    /// </summary>
    private Ail Diagnose(Sim sim, Agent a, int vb, string who, int parent)
    {
        var b = _life!.Bodies[vb];
        Ail truth = Life.Ailment(b);
        int pa = AgentOfBody(vb);
        if (pa < 0) return truth;
        ref var pc = ref Agents[pa].C;
        Sym sym = Life.Symptoms(b);
        bool again = pc.DxAt > 0 && sim.Tick - pc.DxAt < Time.Hours(24);
        Ail before = again ? pc.DxAil : Ail.None;
        int room = RoomOf(b.Cell);
        bool bed = room >= 0 && (_roomBed ??= RoomsWith("MedBed"))[room], scan = room >= 0 && (_roomScan ??= RoomsWith("DiagnosticScanner"))[room];
        bool sniff = !a.IsPerson && (F.Ppm(T.gCO, b.Cell) > 30 || F.Ppm(T.gHCN, b.Cell) > 5);
        double p = Life.DxChance(truth, b, a.C.KnBody, bed, scan, sniff) + (again && before != truth ? 0.3 : 0);
        bool right = Rng.Unit(sim.Seed, Rng.Key("진단"), sim.Tick, vb * 64 + a.Id) < p;
        Ail seen = right ? truth : Life.LookAlike(truth);
        if (truth != Ail.None) { Diagnoses++; if (!right) Misdiagnoses++; }
        string tools = scan ? " (진단 장치)" : bed ? " (의료 침대 검사)" : sniff ? " (기체 센서)" : "";
        var kind = a.IsPerson ? CauseKind.Person : CauseKind.Robot;
        string me = Ko.I(NameOf(a.Id));
        bool fixedIt = again && right && before != truth && before != Ail.None;
        int cause;
        if (fixedIt)
        {
            Corrected++;
            cause = sim.Causes.Add(kind, $"{me} 다시 보니 {Ko.Was(Life.AilName(truth))} — {Ko.Euro(Life.AilName(before))} 보았었다{tools}", sim.Tick, b.Cell, parent, pc.DxCause, a.Id);
            if (pc.DxBy >= 0 && pc.DxBy < Agents.Count && Agents[pc.DxBy].IsPerson && pc.DxBy != a.Id)
            {
                ref var wc = ref Agents[pc.DxBy].C;
                wc.KnBody = Math.Min(1, wc.KnBody + 0.05f);   // 틀린 것에서 배운다
            }
        }
        else cause = sim.Causes.Add(kind, $"{me} {Ko.Eul(who)} {Ko.Euro(Life.AilName(seen))} 보았다 — {Life.SymText(sym)}{tools}", sim.Tick, b.Cell, parent, 0, a.Id);
        pc.DxAil = seen; pc.DxAt = sim.Tick; pc.DxBy = a.Id; pc.DxCause = cause; pc.DxRoom = room; pc.DxSym = sym; pc.DxAwake = b.Conscious;
        sim.Facts.Emit(sim.Tick, FactKind.Diagnosed, b.Cell, fixedIt ? 2 : right ? 1 : 0, cause, a.Id, pa);
        return seen;
    }

    /// <summary>
    /// 다시 보아야 하나 (주컴퓨터가 읽는다): 돌봄을 부를 만한 증상이 있고 — 본 적이 없거나 · 본 뒤 쓰러졌거나 (10분) · 새 증상이 났거나 (30분) ·
    /// 네 시간이 지나도 남았다 (진료 기록이 감기이고 열 · 기침이 있으면 감기와 맞으니 하루).
    /// </summary>
    public static bool NeedsExam(long now, Agent a, in Body b)
    {
        if (!a.IsPerson || b.Alive == 0) return false;
        Sym s = Life.Symptoms(b) & Sym.Serious;
        if (s == 0) return false;
        ref var c = ref a.C;
        if (c.DxAt <= 0) return true;
        long age = now - c.DxAt;
        if (b.Conscious == 0 && c.DxAwake != 0) return age > Time.Minutes(10);
        if ((s & ~c.DxSym) != 0) return age > Time.Minutes(30);
        bool fits = c.DxAil == Ail.Cold && (Life.Symptoms(b) & (Sym.Fever | Sym.Cough)) != 0;
        return age > (fits ? Time.Hours(24) : Time.Hours(4));
    }

    /// <summary>그 몸을 수술하러 맡은 이가 있나.</summary>
    private bool SurgeonComing(int body)
    {
        foreach (var o in Agents) if (o.C.Up != 0 && o.C.AssignedGoal == GoalKind.Surgery && o.C.AssignedTarget == body && o.C.AssignedUntil > _nowTick) return true;
        return false;
    }

    /// <summary>
    /// 진료 기록대로 (여섯 시간): 일산화탄소 · 시안화수소 · 과용으로 보았으면 치료 침대에 눕는다 (산소 — 깨어 있으면 제 발로 · 다 찼으면 그 방을 나온다) ·
    /// 감기 · 체함 · 잠 · 심장으로 보았으면 제 침대에서 쉰다 (그 방이 독해도 — 틀린 진단의 값).
    /// </summary>
    private void ChartOptions(Agent a, int here)
    {
        ref var c = ref a.C;
        if (SurgeonComing(c.Body))   // ⑦ (라) 수술하러 온다 — 치료 침대에 눕는다 (인공 장기 · 이식처럼 진료 기록 없이 잡은 수술도)
        {
            int sb = IsMedBedCell(here) ? here : NearestFree(a, _medBeds);
            if (sb >= 0) { Add(GoalKind.Sleep, sb, sb, 3.0f, c.DxCause); return; }
        }
        if (c.DxAt <= 0 || _nowTick - c.DxAt > Time.Hours(6)) return;
        if (c.DxAil is Ail.CO or Ail.Cyanide or Ail.Overdose or Ail.InternalBleed or Ail.Appendicitis)   // 수술할 병도 치료 침대에서 기다린다
        {
            int mb = IsMedBedCell(here) ? here : NearestFree(a, _medBeds);
            if (mb >= 0) Add(GoalKind.Sleep, mb, mb, 3.0f, c.DxCause);
            else if (c.DxRoom >= 0 && RoomOf(here) == c.DxRoom) Add(GoalKind.Evacuate, c.DxRoom, here, 2.5f, c.DxCause);   // 치료 침대가 다 찼으면 그 방이라도 나온다
        }
        else if (c.DxAil is Ail.Cold or Ail.Indigestion or Ail.Sleep or Ail.Heart or Ail.Bruise)
        {
            int bed = c.Bed >= 0 && !Taken(a, c.Bed) ? c.Bed : NearestFree(a, _beds);
            Add(GoalKind.Sleep, bed, bed >= 0 ? bed : here, 2.0f, c.DxCause);
        }
    }
}
