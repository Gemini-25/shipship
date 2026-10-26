namespace ShipEngine;

/// <summary>
/// ③ 국소 현상 (나) 구멍으로 끌려감 · (다) 몸이 구멍을 막는다 (깊이 바퀴 3 · docs/ENGINE.md ③ '국소 현상') — 판단 몫만 (PH14: 발이 떨어질지 · 그 뒤 몸의 움직임은 ④ 몸 물체).
/// 사람은 제가 느끼는 끌림 (감압 돌풍 · 구멍 입의 ½ρv²·Cd·A — 서 있으면 0.7m² · 낮으면 0.3m²)이 발 마찰을 넘을 것 같으면 곁의 설비 · 문틀을 붙잡는다 (본능 — 손힘 500N을 ④에 요청).
/// 걷기 전에 선 몸들을 ④에 알린다 (발 디딤) · 발이 떨어진 이는 넘어지고 걷던 길을 버린다 · 그 몸 물체를 따라 칸을 맞춘다 (날면 떠감 · 구멍을 막으면 빨려 붙음 — 기압 차 × 구멍 넓이가 300N 아래로 내려가야 뗀다 ·
/// 멈추면 다시 제 힘으로). 몸이 막은 넓이는 장에 요청한다 — 아래 층은 위를 모른다.
/// </summary>
public sealed partial class Mind
{
    public const double PersonKg = 70, PersonCd = 1.0, StandArea = 0.7, LowArea = 0.3, FootMu = 0.6, HandGrip = 500;
    public const double WetMu = 0.1, Gravity = 9.8;   // 젖은 바닥 발 마찰 · 중력 (물건의 미끄러짐 · 날림은 모두 ④ — PH4d가 다리를 걷었다)
    public const double JamKg = 10;                   // 이보다 무거운 짐을 든 이는 잘 넘어진다
    public const double BodyPass = 0.25, BodyFace = 0.5, BodySeal = 0.97, PinFreeN = 300;
    public long PeoplePulled, PeopleSpaced, PeoplePinned, HoldsHeld, HolesPlugged;
    private readonly SortedDictionary<int, float> _plugs = new();   // 지난 틱에 장에 요청한 몸 막음 (칸 → 넓이) — 찍지 않는다: 되살리면 장의 PlugBody에서 다시 세운다
    private bool _plugsStale = true;

    private void Drag(Sim sim, double dt)
    {
        if (_life == null) return;
        foreach (var a in Agents)
        {
            if (!a.IsPerson) { RobotDrag(sim, a); continue; }
            ref var c = ref a.C;
            if (c.Body < 0) continue;
            ref var b = ref _life[c.Body];
            if (_st != null && _st.RagdollOf(c.Body) is int ri and >= 0) { FollowRagdoll(sim, a, ref b, ri, dt); continue; }   // 몸 물체 (PH14 — ④가 셈한다)
            if (c.Adrift != 0) { Drift(ref c, ref b); continue; }
            int cell = b.Cell;
            if (cell < 0 || cell >= L.N) continue;
            if (c.PinAt != 0) { Pinned(sim, a, ref b, dt); continue; }
            if (c.CarriedBy >= 0) continue;   // 업힌 이는 업은 이가 붙든다
            double wx = F.WindX[cell], wy = F.WindY[cell], v = Math.Sqrt(wx * wx + wy * wy), pa = v >= 0.5 ? F.WindPa[cell] : 0;
            // 구멍 곁: 구멍 입으로 모이는 흐름 (방 바람보다 세면 그쪽으로 — 구멍이 나는 그 순간에도 · 장이 흐르기 전에)
            double qMouth = F.HoleMouthPa(cell, out int mouth);
            if (qMouth > pa) { pa = qMouth; wx = L.X(mouth) - L.X(cell); wy = L.Y(mouth) - L.Y(cell); v = 1; }
            if (pa < 1) continue;
            double drag = pa * PersonCd * (b.Low != 0 ? LowArea : StandArea);
            double grip = (F.Stored(T.sWater, cell) > 50 ? WetMu : FootMu) * Math.Max(0.05, F.Grav[cell]) * PersonKg * Gravity;
            if (drag <= grip) continue;
            int cause = WindCause(cell);
            // 붙잡기: 바람이 발을 떼려 하면 (제가 느끼는 끌림이 발 마찰을 넘는다) 깨어 있는 이는 곁의 설비 · 문틀을 붙든다 (본능 — 손힘을 ④에 요청 · 버틸지는 ④가 셈한다)
            if (c.Up == 0 || !HandholdNear(cell)) continue;
            _st?.Hold(c.Body, HandGrip);
            if (drag <= grip + HandGrip && (c.HeldAt == 0 || sim.Tick - c.HeldAt > Time.Minutes(1)))
            {
                int hc = sim.Causes.Add(CauseKind.Person, $"{Ko.I(NameOf(a.Id))} 빨아들이는 바람에 곁의 것을 붙잡고 버텼다", sim.Tick, cell, cause, a.Id);
                sim.Facts.Emit(sim.Tick, FactKind.Fell, cell, 0, hc, a.Id);
                HoldsHeld++;
            }
            c.HeldAt = sim.Tick;
        }
        Footing(sim);
        ApplyPlugs();
    }

    private PropActor[] _feetBuf = new PropActor[16];
    /// <summary>
    /// 선 몸들 (걷기 전 — 틱 처음 자리)의 발 디딤을 ④에 요청한다 (붙잡음 요청을 더해 — 발이 떨어질지는 ④가 셈한다) · 이번에 발이 떨어진 이는 넘어지고 걷던 길을 버린다.
    /// 업힌 이 · 빨려 붙은 이 · 떠내려가는 이 · 이미 몸 물체인 이는 빼고.
    /// </summary>
    private void Footing(Sim sim)
    {
        if (_st == null || _life == null) return;
        int n = 0;
        foreach (var a in Agents)
        {
            ref var c = ref a.C;
            PropActor x;
            if (a.IsPerson)
            {
                if (c.Body < 0 || c.Adrift != 0 || c.PinAt != 0 || c.CarriedBy >= 0) continue;
                ref var b = ref _life[c.Body];
                if (b.Alive == 0 || b.Cell < 0 || b.Cell >= L.N || _st.RagdollOf(c.Body) >= 0) continue;
                x = new PropActor { Tag = c.Body, Cell = b.Cell, Kg = (float)PersonKg, Low = b.Low };
            }
            else
            {
                if (c.Kind != AgentKind.Robot || c.Mech < 0 || c.Damage >= 1 || c.Cell < 0 || c.Cell >= L.N || _st.RagdollOf(-1 - c.Mech) >= 0) continue;
                x = new PropActor { Tag = -1 - c.Mech, Cell = c.Cell, Kg = RobotKg };
            }
            if (n == _feetBuf.Length) Array.Resize(ref _feetBuf, n * 2);
            _feetBuf[n++] = x;
        }
        if (n == 0) return;
        long knocked = _st.PeopleKnocked + _st.RobotsKnocked;
        _st.Footing(sim, _feetBuf.AsSpan(0, n));
        if (_st.PeopleKnocked + _st.RobotsKnocked == knocked) return;
        foreach (var a in Agents)
        {
            int tag = a.IsPerson ? a.C.Body : a.C.Kind == AgentKind.Robot && a.C.Mech >= 0 ? -1 - a.C.Mech : int.MinValue;
            if (tag == int.MinValue || (!a.IsPerson && tag >= 0) || a.IsPerson && tag < 0 || _st.RagdollOf(tag) < 0) continue;
            if (a.IsPerson && a.C.Up != 0) { Fall(sim, a); PeoplePulled++; }   // 발이 떨어져 넘어진다
            a.Path.Clear(); a.PathIdx = 0;
        }
    }

    /// <summary>
    /// 몸 물체를 따른다 (PH14): 날면 우주로 빨려 나간 것 (떠내려감 · 둘레 칸은 배 틀의 가장 가까운 끝 칸) · 구멍을 막으면 빨려 붙음 (멍 · 떼기는 그대로) ·
    /// 미끄러지는 동안 몸의 칸은 물체의 칸 · 멈추면 (놓임) 거둔다 — 다시 제 힘으로 (깨어 있으면 일어선다 · 아니면 그 자리에 누운 채).
    /// </summary>
    private void FollowRagdoll(Sim sim, Agent a, ref Body b, int ri, double dt)
    {
        ref var c = ref a.C;
        var p = _st!.Props[ri];
        if (c.Up != 0) { Fall(sim, a); PeoplePulled++; }   // ④가 발을 뗐다 (바람이 손힘 · 발 마찰을 이겼다) — 넘어져 끌려간다
        a.Path.Clear(); a.PathIdx = 0;
        if (Structure.Flying(p))
        {
            int at = _st.SpaceCellOf(p);
            if (c.Adrift == 0)
            {
                int sc = sim.Causes.Add(CauseKind.Pressure, $"{Ko.I(NameOf(a.Id))} 빨아들이는 바람에 끌려 구멍 밖 우주로 빨려 나갔다", sim.Tick, at, p.Cause, a.Id);
                sim.Facts.Emit(sim.Tick, FactKind.WentOutside, at, 1, sc, a.Id);
                c.Adrift = 1; b.HurtCause = sc;
                PeopleSpaced++;
            }
            b.Cell = at; c.Cell = at;
            return;
        }
        int cell = _st.CellOf(p);
        if (cell >= 0) { b.Cell = cell; c.Cell = cell; }
        if (p.State == 6)
        {
            if (c.PinAt == 0)
            {
                int j = p.PlugAt - 1;
                int pc = sim.Causes.Add(CauseKind.Pressure, $"{Ko.I(NameOf(a.Id))} 구멍에 빨려 붙었다 — 몸이 구멍을 막았다 ({F.Hole[j] * 1e4:0}cm²)", sim.Tick, j, p.Cause, a.Id);
                sim.Facts.Emit(sim.Tick, FactKind.Hurt, cell, 0.2f, pc, c.Body);
                c.PinAt = p.PlugAt;
                b.HurtCause = pc;
                PeoplePinned++;
            }
            Pinned(sim, a, ref b, dt);
            if (c.PinAt == 0) _st.EndRagdoll(c.Body);   // 몸을 뗐다
            return;
        }
        if (c.PinAt != 0) c.PinAt = 0;   // 물체가 구멍에서 떨어졌다 (누르는 힘이 무게 아래)
        if (c.Adrift != 0) c.Adrift = 0;   // 날던 몸이 배의 트인 칸에 다시 들었다 (④가 실었다)
        if (Settled(p)) _st.EndRagdoll(c.Body);   // 멈췄다 — 다시 제 힘으로
    }

    /// <summary>바람에 밀린 로봇 (PH14 — 발 디딤은 ④가 셈한다: 끌림이 바퀴 · 발 마찰을 넘으면 몸 물체 Kind 6)을 따른다 — 몸의 칸은 물체의 칸 · 멈추면 다시 제 힘으로 · 날면 우주에서 떠내려간다.</summary>
    private void RobotDrag(Sim sim, Agent a)
    {
        if (_st == null || a.C.Kind != AgentKind.Robot || a.C.Mech < 0) return;
        ref var c = ref a.C;
        int tag = -1 - c.Mech;
        int ri = _st.RagdollOf(tag);
        if (ri >= 0)
        {
            var p = _st.Props[ri];
            int at = Structure.Flying(p) ? _st.SpaceCellOf(p) : _st.CellOf(p);
            if (at >= 0) c.Cell = at;
            a.Path.Clear(); a.PathIdx = 0;
            if (Settled(p)) _st.EndRagdoll(tag);
        }
    }

    /// <summary>몸 물체가 멈췄나: 놓였다 · 무중력에서 떠 있는 채 느려졌다 (0.05m/s 아래 — 그 뒤는 떠다님으로).</summary>
    private static bool Settled(in Prop p) => p.State == 0 || p.State == 4 && p.Vx * p.Vx + p.Vy * p.Vy < 0.05 * 0.05 && Math.Abs(p.W) < 0.5;

    /// <summary>곁(제 칸 · 여덟 이웃)에 붙잡을 것이 있나: 설비 · 문틀.</summary>
    private bool HandholdNear(int cell)
    {
        int x = L.X(cell), y = L.Y(cell);
        for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
            {
                if (!L.In(x + ox, y + oy)) continue;
                int j = L.Index(x + ox, y + oy);
                if (L.FurnitureAt[j] >= 0 || L.Kind[j] == CellKind.Door) return true;
            }
        return false;
    }

    /// <summary>떠내려감: 빨려 나간 빠르기로 배에서 멀어진다 (바깥 칸을 따라 · 도면 끝에서 멈춘다 — 돌아올 줄이 없다).</summary>
    private void Drift(ref AgentCore c, ref Body b)
    {
        int x = L.X(b.Cell) + c.AdriftDx, y = L.Y(b.Cell) + c.AdriftDy;
        if (!L.In(x, y)) return;
        int j = L.Index(x, y);
        if (L.Kind[j] != CellKind.Outside && F.Severed[j] == 0) return;
        b.Cell = j; c.Cell = j;
    }

    /// <summary>구멍에 빨려 붙은 이: 기압 차 × 구멍 넓이에 눌려 있다 — 힘이 300N 아래가 되거나 구멍을 덧대면 몸을 뗀다 (판단 — 멍은 ⑦).</summary>
    private void Pinned(Sim sim, Agent a, ref Body b, double dt)
    {
        ref var c = ref a.C;
        int j = c.PinAt - 1;
        double force = HoleSuction(b.Cell, j, BodyFace);
        if (F.Hole[j] <= 0.01f || F.Severed[j] != 0 || force < PinFreeN)
        {
            c.PinAt = 0;
            int fc = sim.Causes.Add(CauseKind.Pressure, F.Hole[j] <= 0.01f ? $"구멍을 덧대 {Ko.I(NameOf(a.Id))} 몸을 뗐다" : $"기압이 빠져 {Ko.I(NameOf(a.Id))} 구멍에서 몸을 뗐다", sim.Tick, j, b.HurtCause, a.Id);
            sim.Facts.Emit(sim.Tick, FactKind.Rescue, b.Cell, 0, fc, a.Id);
            return;
        }
        // 빨린 자리의 멍 · 아픔은 ⑦이 ④의 누르는 힘으로 셈한다 (PH16) — 마음은 붙어 있는 동안 움직이지 못한다
        a.Path.Clear(); a.PathIdx = 0; c.MoveBudget = 0;
    }

    /// <summary>구멍에 빨려 붙은 것을 누르는 힘 (N): 그 칸과 구멍 너머의 기압 차 × 덮은 넓이.</summary>
    private double HoleSuction(int cell, int hole, double face)
    {
        if (cell < 0 || hole < 0) return 0;
        int bx = 2 * L.X(hole) - L.X(cell), by = 2 * L.Y(hole) - L.Y(cell);
        double pOut = L.In(bx, by) && !F.IsOut(L.Index(bx, by)) && F.Passable(L.Index(bx, by)) ? F.Pressure(L.Index(bx, by)) : F.Outside.KPa;
        return Math.Max(0, F.Pressure(cell) - pOut) * 1000 * Math.Min(F.Hole[hole], face);
    }

    /// <summary>
    /// 몸 막음을 장에 요청한다 (빨려 붙은 몸 — 덮은 넓이의 97% · 칸마다 가장 큰 것): 장의 PlugBody를 통째로 다시 쓴다 — 지난번에 쓴 칸은 되살린 뒤 PlugBody > 0인 칸에서 다시 세운다
    /// (찾아 고친 결함: 지운 칸 목록을 찍지 않아 되살린 세계에서는 풀린 막음이 장에 남았다 · PHYSICS.md §14-2 C). 바닥 물건의 막음은 ④가 PlugProp에 쓴다 (PH4c).
    /// </summary>
    private void ApplyPlugs()
    {
        if (_plugsStale)
        {
            _plugs.Clear();
            for (int c = 0; c < F.PlugBody.Length; c++) if (F.PlugBody[c] > 0) _plugs[c] = F.PlugBody[c];
            _plugsStale = false;
        }
        Dictionary<int, float>? now = null;
        foreach (var a in Agents)
            if (a.C.PinAt != 0)
            {
                int j = a.C.PinAt - 1;
                (now ??= new())[j] = Math.Max(now.GetValueOrDefault(j), (float)(Math.Min(F.Hole[j], BodyFace) * BodySeal));
            }
        if (now == null && _plugs.Count == 0) return;
        foreach (var kv in _plugs) if (now == null || !now.ContainsKey(kv.Key)) F.SetPlug(kv.Key, 0);
        _plugs.Clear();
        if (now == null) return;
        foreach (var kv in now.OrderBy(kv => kv.Key)) { F.SetPlug(kv.Key, kv.Value); _plugs[kv.Key] = kv.Value; }
    }

    /// <summary>물건 무게 (kg — 데이터 표).</summary>
    public double ItemKg(int item) => P.Items.TryGetValue(ItemNames[item], out var d) ? d.Kg : 2;

    /// <summary>그 칸 바람의 원인 (구역이 새는 틈을 낸 원인).</summary>
    private int WindCause(int c)
    {
        int r = F.RegionOf.Length > c ? F.RegionOf[c] : -1;
        if (_st == null || r < 0) return 0;
        foreach (var k in F.Links) if (k.RA == r && k.RB < 0 && _st.DamageCause[k.Via] != 0) return _st.DamageCause[k.Via];
        return 0;
    }
}
