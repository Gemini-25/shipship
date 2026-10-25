namespace ShipEngine;

/// <summary>
/// ③ 국소 현상 (나) 구멍으로 끌려감 · (다) 물건이 구멍을 막는다 (깊이 바퀴 3 · docs/ENGINE.md ③ '국소 현상').
/// 감압 돌풍의 끌림힘 ½ρv²·Cd·A (서 있으면 0.7m² · 낮으면 0.3m²)가 발바닥 마찰 μmg를 넘으면 사람이 바람 쪽으로 미끄러진다 —
/// 깨어 있는 이는 곁의 설비 · 문틀을 붙잡아 손힘 500N을 더 버틴다 (본능). 몸보다 큰 구멍(0.25m²)이면 우주로 빨려 나가 떠내려가고 ·
/// 작으면 몸이 구멍에 빨려 붙어 막는다 (기압 차 × 구멍 넓이가 300N 아래로 내려가야 뗀다). 사람의 자리는 마음 층에 있으니
/// 여기서 장(바람 · 기압 · 구멍 입 — <see cref="Field.HoleMouthPa"/>)을 읽고 장에 '몸이 막은 넓이'를 요청한다 — 아래 층은 위를 모른다 (물건은 ④가 PH4c).
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
            if (!a.IsPerson) continue;
            ref var c = ref a.C;
            if (c.Body < 0) continue;
            ref var b = ref _life[c.Body];
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
            // 붙잡기: 깨어 있는 이는 곁의 설비 · 문틀을 붙든다 (본능 — 손힘만큼 더 버틴다)
            bool held = c.Up != 0 && HandholdNear(cell);
            if (held && drag <= grip + HandGrip)
            {
                if (c.HeldAt == 0 || sim.Tick - c.HeldAt > Time.Minutes(1))
                {
                    int hc = sim.Causes.Add(CauseKind.Person, $"{Ko.I(NameOf(a.Id))} 빨아들이는 바람에 곁의 것을 붙잡고 버텼다", sim.Tick, cell, cause, a.Id);
                    sim.Facts.Emit(sim.Tick, FactKind.Fell, cell, 0, hc, a.Id);
                    HoldsHeld++;
                }
                c.HeldAt = sim.Tick;
                continue;
            }
            // 끌려간다: 센 쪽 축으로 · 한 틱에 많아야 세 칸 (끌림이 이긴 만큼 빨리 — 1m 미끄러지며 얻는 빠르기)
            double net = drag - grip - (held ? HandGrip : 0);
            double speed = Math.Sqrt(2 * Math.Max(0, net) / PersonKg * 1.0);
            int steps = Math.Clamp((int)(speed * dt / 4), 1, 3);
            int dx = Math.Abs(wx) >= Math.Abs(wy) ? Math.Sign(wx) : 0, dy = dx == 0 ? Math.Sign(wy) : 0;
            if (c.Up != 0) Fall(sim, a);   // 발이 떨어져 넘어진다
            a.Path.Clear(); a.PathIdx = 0;
            for (int s = 0; s < steps; s++)
            {
                if (s > 0 && F.HoleMouthPa(cell, out int m2) > 0) { dx = L.X(m2) - L.X(cell); dy = L.Y(m2) - L.Y(cell); }   // 구멍 곁에 닿으면 구멍 쪽으로
                else if (s > 0 && (F.WindX[cell] != 0 || F.WindY[cell] != 0)) { double ax = F.WindX[cell], ay = F.WindY[cell]; dx = Math.Abs(ax) >= Math.Abs(ay) ? Math.Sign(ax) : 0; dy = dx == 0 ? Math.Sign(ay) : 0; }
                int x = L.X(cell) + dx, y = L.Y(cell) + dy;
                if (!L.In(x, y)) break;
                int j = L.Index(x, y);
                var kind = L.Kind[j];
                bool open = kind == CellKind.Outside || F.Severed[j] != 0;
                if (open || kind == CellKind.Wall && F.Hole[j] > 0.01f)
                {
                    if (open || F.Hole[j] >= BodyPass)
                    {
                        // 몸이 지나는 구멍 — 우주로 빨려 나가 떠내려간다
                        int k = open ? j : L.In(x + dx, y + dy) ? L.Index(x + dx, y + dy) : j;
                        int sc = sim.Causes.Add(CauseKind.Pressure, $"{Ko.I(NameOf(a.Id))} 빨아들이는 바람에 끌려 구멍 밖 우주로 빨려 나갔다", sim.Tick, j, cause, a.Id);
                        sim.Facts.Emit(sim.Tick, FactKind.WentOutside, j, 1, sc, a.Id);
                        b.Cell = k; c.Cell = k; c.Adrift = 1; c.AdriftDx = (sbyte)dx; c.AdriftDy = (sbyte)dy;
                        b.HurtCause = sc;
                        PeopleSpaced++;
                    }
                    else
                    {
                        // 작은 구멍 — 몸이 빨려 붙어 막는다
                        int pc = sim.Causes.Add(CauseKind.Pressure, $"{Ko.I(NameOf(a.Id))} 구멍에 빨려 붙었다 — 몸이 구멍을 막았다 ({F.Hole[j] * 1e4:0}cm²)", sim.Tick, j, cause, a.Id);
                        sim.Facts.Emit(sim.Tick, FactKind.Hurt, cell, 0.2f, pc, c.Body);
                        c.PinAt = j + 1;
                        b.HurtCause = pc;
                        PeoplePinned++;
                    }
                    break;
                }
                if (kind == CellKind.Wall || L.FurnitureAt[j] >= 0 || kind == CellKind.Door && F.DoorOpen[L.DoorAt[j]] < 0.5f)
                {
                    // 벽 · 설비 · 닫힌 문에 부딪혀 멈춘다 (½mv² — 200J에 가벼운 타박 0.25)
                    double e = 0.5 * PersonKg * speed * speed;
                    if (e >= 50) _life.Strike(sim, c.Body, (float)Math.Min(1.5, e / 800), 0, sim.Causes.Add(CauseKind.Pressure, $"{Ko.I(NameOf(a.Id))} 바람에 끌려가다 부딪혔다", sim.Tick, j, cause, a.Id));
                    break;
                }
                cell = j; b.Cell = j; c.Cell = j;
                PeoplePulled++;
            }
        }
        ApplyPlugs();
    }

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

    /// <summary>구멍에 빨려 붙은 이: 기압 차 × 구멍 넓이에 눌려 있다 — 빨린 자리에 멍이 들고 · 힘이 300N 아래가 되거나 구멍을 덧대면 몸을 뗀다.</summary>
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
        // 빨린 자리: 1kN에 30분이면 타박 1 (깊은 멍 · 부기)
        b.Blunt += (float)(force / 1000 * dt / 1800);
        b.Pain = Math.Min(1, b.Pain + (float)(force / 1000 * dt / 600));
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
