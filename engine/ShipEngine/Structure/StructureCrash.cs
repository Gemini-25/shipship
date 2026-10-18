namespace ShipEngine;

/// <summary>
/// ④ 칸 강체 (다) 부딪힘 (docs/ENGINE.md ④ '바퀴 3 설계 — 칸 강체').
/// <para>
/// 잔걸음마다 몸끼리 감싸는 상자가 겹치면 작은 몸의 겉 칸을 상대 몸의 틀로 옮겨 (칸마다 네 점 — 50cm) 상대의 구조 칸에 들었나 본다.
/// 닿은 점들의 가운데 · 상대 겉면의 바깥 방향(이웃 칸의 기울기)으로 충격량 j = −(1+e)v·n / (1/m₁ + 1/m₂ + (r₁×n)²/I₁ + (r₂×n)²/I₂) (e 0.1 — 금속이 찌그러진다) · 마찰 0.4.
/// 밀어 떼지는 않는다 (운동량 · 각운동량이 꼭 맞게) — 다가오는 동안만 민다.
/// </para>
/// <para>
/// 잃은 운동 에너지는 닿은 칸들이 반씩 받는다 — 칸마다 으스러지는 에너지 (배 충돌의 미노르스키 식: 강철 1m³를 찌그러뜨리는 데 47.2MJ ×
/// 그 칸의 판 부피 · 재질 강도 · 열 · 녹)와 견줘: 넘으면 통째로 부서지고 · 15% 넘게면 찢겨 뚫리고 · 0.2% 넘게면 찌그러져 금이 간다 · 나머지는 열.
/// </para>
/// </summary>
public sealed partial class Structure
{
    public const double CrushJPerM3 = 47.2e6, Restitution = 0.1, CrashMu = 0.4, BumpSec = 0.2;
    public long Crashes;
    private List<int>[] _edge = Array.Empty<List<int>>();
    private int _edgeVersion = -1;
    private readonly List<(double x, double y, int ca, int cb, double nx, double ny)> _contacts = new();

    private bool SolidIn(int b, int c) => c >= 0 && c < L.N && BodyOf(c) == b && Solid(c);

    /// <summary>몸의 겉 칸 (구조가 남은 칸 중 여덟 이웃에 제 몸이 아닌 칸이 있는 것) — 그물이 바뀔 때만 다시 센다.</summary>
    private List<int> Edge(int b)
    {
        if (_edgeVersion != _latticeVersion || _edge.Length != Bodies.Count)
        {
            _edge = new List<int>[Bodies.Count];
            for (int k = 0; k < _edge.Length; k++) _edge[k] = new List<int>();
            for (int c = 0; c < L.N; c++)
            {
                int k = BodyOf(c);
                if (k < 0 || k >= _edge.Length || !Solid(c)) continue;
                int x = L.X(c), y = L.Y(c);
                bool edge = false;
                for (int dy = -1; dy <= 1 && !edge; dy++)
                    for (int dx = -1; dx <= 1 && !edge; dx++)
                        if ((dx != 0 || dy != 0) && !(L.In(x + dx, y + dy) && SolidIn(k, L.Index(x + dx, y + dy)))) edge = true;
                if (edge) _edge[k].Add(c);
            }
            _edgeVersion = _latticeVersion;
        }
        return _edge[b];
    }

    /// <summary>몸의 세상 틀 상자.</summary>
    private (double x0, double y0, double x1, double y1) WorldBox(int b)
    {
        var o = Bodies[b];
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var (px, py) in new[] { (o.MinX, o.MinY), (o.MaxX + 1, o.MinY), (o.MinX, o.MaxY + 1), (o.MaxX + 1, o.MaxY + 1) })
        {
            var (wx, wy) = WorldOf(b, px, py);
            x0 = Math.Min(x0, wx); y0 = Math.Min(y0, wy); x1 = Math.Max(x1, wx); y1 = Math.Max(y1, wy);
        }
        return (x0, y0, x1, y1);
    }

    /// <summary>잔걸음마다: 몸 쌍마다 상자가 겹치면 닿았나 본다 (몸 번호 순서 — 결정론).</summary>
    private void Contacts(Sim sim)
    {
        int n = Bodies.Count;
        for (int a = 0; a < n; a++)
        {
            if (Bodies[a].Mass <= 0) continue;
            var ba = WorldBox(a);
            for (int b = a + 1; b < n; b++)
            {
                if (Bodies[b].Mass <= 0 || Bodies[a].Asleep != 0 && Bodies[b].Asleep != 0) continue;
                var bb = WorldBox(b);
                if (ba.x1 < bb.x0 || bb.x1 < ba.x0 || ba.y1 < bb.y0 || bb.y1 < ba.y0) continue;
                // 겉 칸이 적은 몸의 점을 상대 틀로
                if (Edge(a).Count <= Edge(b).Count) Collide(sim, a, b); else Collide(sim, b, a);
            }
        }
    }

    /// <summary>몸 a의 겉 칸 점들이 몸 b의 구조 칸에 들었나 → 들었고 다가오면 충격량 · 잃은 에너지는 닿은 칸에.</summary>
    private void Collide(Sim sim, int a, int b)
    {
        _contacts.Clear();
        foreach (int c in Edge(a))
        {
            double cx = L.X(c), cy = L.Y(c);
            for (int s = 0; s < 4; s++)
            {
                double px = cx + 0.25 + 0.5 * (s & 1), py = cy + 0.25 + 0.5 * (s >> 1);
                var (wx, wy) = WorldOf(a, px, py);
                var (lx, ly) = LocalOf(b, wx, wy);
                int bx = (int)Math.Floor(lx), by = (int)Math.Floor(ly);
                if (!L.In(bx, by)) continue;
                int cb = L.Index(bx, by);
                if (!SolidIn(b, cb)) continue;
                // b 겉면의 바깥 방향: 이웃 중 b의 구조가 아닌 칸 쪽 (b의 틀) — 다 차 있으면 b의 무게중심에서 그 점 쪽
                double nx = 0, ny = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if ((dx != 0 || dy != 0) && !(L.In(bx + dx, by + dy) && SolidIn(b, L.Index(bx + dx, by + dy)))) { nx += dx; ny += dy; }
                if (nx == 0 && ny == 0) { nx = lx - Bodies[b].Lx; ny = ly - Bodies[b].Ly; }
                var ob = Bodies[b];
                double cs = Math.Cos(ob.Th), sn = Math.Sin(ob.Th);
                _contacts.Add((wx, wy, c, cb, cs * nx - sn * ny, sn * nx + cs * ny));
            }
        }
        if (_contacts.Count == 0) return;
        double px0 = 0, py0 = 0, nx0 = 0, ny0 = 0;
        foreach (var k in _contacts) { px0 += k.x; py0 += k.y; double l = Math.Sqrt(k.nx * k.nx + k.ny * k.ny); if (l > 0) { nx0 += k.nx / l; ny0 += k.ny / l; } }
        px0 /= _contacts.Count; py0 /= _contacts.Count;
        double nl = Math.Sqrt(nx0 * nx0 + ny0 * ny0);
        if (nl < 1e-9) { nx0 = Bodies[a].X - Bodies[b].X; ny0 = Bodies[a].Y - Bodies[b].Y; nl = Math.Sqrt(nx0 * nx0 + ny0 * ny0); }
        if (nl < 1e-12) return;
        nx0 /= nl; ny0 /= nl;   // b에서 a 쪽
        var A = Bodies[a]; var B = Bodies[b];
        double rax = px0 - A.X, ray = py0 - A.Y, rbx = px0 - B.X, rby = py0 - B.Y;
        double vax = A.Vx - A.W * ray, vay = A.Vy + A.W * rax, vbx = B.Vx - B.W * rby, vby = B.Vy + B.W * rbx;
        double rvx = vax - vbx, rvy = vay - vby, vn = rvx * nx0 + rvy * ny0;
        if (vn >= 0) return;   // 멀어지는 중
        double ran = rax * ny0 - ray * nx0, rbn = rbx * ny0 - rby * nx0;
        double kn = 1 / A.Mass + 1 / B.Mass + ran * ran / A.Inertia + rbn * rbn / B.Inertia;
        double j = -(1 + Restitution) * vn / kn;
        // 마찰 (쿨롱 — 미끄러지는 쪽 반대로 · μj까지)
        double tx = -ny0, ty = nx0, vt = rvx * tx + rvy * ty;
        double rat = rax * ty - ray * tx, rbt = rbx * ty - rby * tx;
        double kt = 1 / A.Mass + 1 / B.Mass + rat * rat / A.Inertia + rbt * rbt / B.Inertia;
        double jt = Math.Clamp(-vt / kt, -CrashMu * j, CrashMu * j);
        double ke0 = Ke(A) + Ke(B);
        double ix = j * nx0 + jt * tx, iy = j * ny0 + jt * ty;
        // 스치듯 닿음(5cm/s 아래)은 사실로 남기지 않는다 — 부딪힘이면 원인을 먼저 세워 두 몸을 그 원인으로 민다 (그 덜컹임에 넘어진 사람이 부딪힘에 잇는다)
        int cause = -vn >= 0.05 ? CrashCause(sim, a, b, -vn) : 0;
        Push(a, px0, py0, ix, iy, cause);
        Push(b, px0, py0, -ix, -iy, cause);
        double ke1 = Ke(Bodies[a]) + Ke(Bodies[b]);
        double lost = Math.Max(0, ke0 - ke1);
        double dv = Math.Sqrt(ix * ix + iy * iy);
        var oa = Bodies[a]; oa.Bump = (float)Math.Max(oa.Bump, dv / oa.Mass); Bodies[a] = oa;
        var ob2 = Bodies[b]; ob2.Bump = (float)Math.Max(ob2.Bump, dv / ob2.Mass); Bodies[b] = ob2;
        if (cause != 0) Crash(sim, a, b, cause, lost);
    }

    /// <summary>부딪힘의 원인 마디 (잃은 에너지는 뒤에 사실 · 칸에 — 마디 글에는 상대 빠르기 · 질량).</summary>
    private int CrashCause(Sim sim, int a, int b, double speed)
    {
        int mover = a == 0 ? b : b == 0 ? a : Math.Sqrt(Bodies[a].Vx * Bodies[a].Vx + Bodies[a].Vy * Bodies[a].Vy) >= Math.Sqrt(Bodies[b].Vx * Bodies[b].Vx + Bodies[b].Vy * Bodies[b].Vy) ? a : b;
        string what = a == 0 || b == 0 ? "떨어져 나간 조각이 배에 부딪혔다" : "떨어져 나간 조각끼리 부딪혔다";
        int hit = -1;
        foreach (var k in _contacts) if (hit < 0 || k.cb < hit) hit = k.cb;
        return sim.Causes.Add(CauseKind.Impact, $"{what} (상대 {speed:0.0}m/s · {Bodies[mover].Mass / 1000:0.#}t)", sim.Tick, hit, BodyCauseOf(mover));
    }

    private static double Ke(RigidBody o) => 0.5 * o.Mass * (o.Vx * o.Vx + o.Vy * o.Vy) + 0.5 * o.Inertia * o.W * o.W;

    /// <summary>부딪혔다: 사실 · 소리 · 원인 · 닿은 칸이 잃은 에너지를 반씩 받아 찌그러지고 · 찢기고 · 부서진다.</summary>
    private void Crash(Sim sim, int a, int b, int cause, double lost)
    {
        Crashes++;
        int mover = a == 0 ? b : b == 0 ? a : Math.Sqrt(Bodies[a].Vx * Bodies[a].Vx + Bodies[a].Vy * Bodies[a].Vy) >= Math.Sqrt(Bodies[b].Vx * Bodies[b].Vx + Bodies[b].Vy * Bodies[b].Vy) ? a : b;
        var cellsA = new SortedSet<int>(); var cellsB = new SortedSet<int>();
        foreach (var k in _contacts) { cellsA.Add(k.ca); cellsB.Add(k.cb); }
        int hit = cellsB.Min;
        sim.Facts.Emit(sim.Tick, FactKind.Crash, hit, (float)(lost / 1000), cause, -1, mover);
        sim.Find<Sound>()?.Emit(sim, NearestAir(hit), (float)(90 + 10 * Math.Log10(Math.Max(1, lost / 1000))), SoundKind.Bang, cause);
        foreach (int c in cellsA) Crush(sim, c, lost / 2 / cellsA.Count, cause);
        foreach (int c in cellsB) Crush(sim, c, lost / 2 / cellsB.Count, cause);
    }

    /// <summary>
    /// 칸이 부딪힌 에너지를 받는다 (미노르스키: 강철 1m³를 으스러뜨리는 데 47.2MJ — 칸의 판 부피 · 재질 강도 · 열 · 녹만큼):
    /// 넘으면 통째로 · 15% 넘게면 찢겨 뚫린다 (넓이 ∝ 에너지) · 0.2% 넘게면 찌그러져 금이 간다 · 받은 에너지는 열이 된다.
    /// </summary>
    private void Crush(Sim sim, int c, double e, int cause)
    {
        if (e <= 0 || !Solid(c)) return;
        var m = T.Materials[L.Material[c]].Def;
        double vol = L.Kind[c] switch { CellKind.Wall => 0.075, CellKind.Floor => 0.02, CellKind.Door => 0.015, _ => 0 };
        if (vol <= 0) return;
        double crush = CrushJPerM3 * vol * Math.Max(0.05, m.Strength / 250.0) * Math.Max(0.2, HeatFactor(c, m)) * (1 - Corrode[c]);
        double r = e / crush;
        AddHeatTo(c, e);
        DamageCause[c] = cause;
        if (r >= 1) { Destroy(sim, c, cause); return; }
        if (r >= 0.15)
        {
            float area = (float)Math.Min(2.4, 2.5 * r);
            if (L.Kind[c] == CellKind.Floor) { if (area > F.Hole[c]) { F.SetHole(c, area); if (area >= DeckGone) _latticeDirty = true; } }
            else Open(sim, c, Math.Max(F.Hole[c], area), sim.Causes.Add(CauseKind.Impact, "부딪힌 자리가 찢겨 뚫렸다", sim.Tick, c, cause), FactKind.WallBreach);
            return;
        }
        if (r >= 0.002)
        {
            Fatigue[c] = (float)Math.Min(1, Fatigue[c] + r);
            if (L.Kind[c] == CellKind.Wall) SeedCrack(sim, c, 0.002 + 0.05 * Math.Sqrt(r), sim.Causes.Add(CauseKind.Impact, "부딪힌 자리가 찌그러져 금이 갔다", sim.Tick, c, cause));
        }
    }

    /// <summary>몸을 흔든 원인 (몸 번호로).</summary>
    private int BodyCauseOf(int b)
    {
        if (b < 0 || b >= Bodies.Count) return 0;
        var o = Bodies[b];
        return o.PushCause != 0 ? o.PushCause : o.TumbleCause != 0 ? o.TumbleCause : b > 0 && b <= Fragments.Count ? Fragments[b - 1].Cause : 0;
    }
}
