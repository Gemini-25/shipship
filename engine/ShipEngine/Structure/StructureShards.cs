namespace ShipEngine;

/// <summary>부스러기 알갱이 하나 (세상 틀 — 실수 자리 · 빠르기 · 질량).</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct Shard
{
    public double X, Y, Vx, Vy;
    public float Kg;
    public int Cause;
    /// <summary>떨어져 나온 몸 (나온 지 1초 동안은 그 몸에 닿지 않는다) · 나온 때.</summary>
    public int From;
    public long Born;
    /// <summary>제 회전 각운동량 (떨어져 나온 판 조각이 몸과 함께 돌던 몫 — m·a²/6·ω · 장부).</summary>
    public double Spin;
    /// <summary>만든 차례 (그림의 자취 열쇠 — PH5가 채운다) · 난 때 (틱 안 0~255).</summary>
    public int Id;
    public byte Sub;
}

/// <summary>
/// ④ 칸 강체 (마) 알갱이 (docs/ENGINE.md ④ '바퀴 3 설계 — 칸 강체').
/// <para>
/// 잔칸이 떨어져 나가면 (구멍이 파이고 · 부딪혀 으스러지고 · 갈라진 자리가 찢기고 · 2톤 아래 덩어리가 떨어지면) 그 판의 질량이 부스러기 알갱이가 된다 —
/// 그 자리 몸의 빠르기(v + ω×r)에 튀는 빠르기(구멍은 바깥 쪽 · 나머지는 몸 바깥쪽으로 몇 m/s)를 더해 날아가고 · 몸은 그만큼 반대로 밀리며 질량 · 무게중심 · 회전관성이 준다 (운동량 · 각운동량이 꼭 맞는다).
/// 진공을 곧게 날다가 몸의 남은 잔칸에 들면 ½mv²의 충격 (④ 충격 길 — 금 · 구멍) · 그 몸에 상대 운동량을 넘기고 붙는다 · 배에서 300m 넘게 멀어지면 장부 '우주로'.
/// </para>
/// </summary>
public sealed partial class Structure
{
    public readonly List<Shard> Shards = new();
    public double ShardKgStuck, ShardKgLost, ShardPxGone, ShardPyGone, ShardLGone;
    public long ShardsMade, ShardHits;
    public const double ShardRange = 300, ShardKick = 3, SpallKick = 0.3, BlowSec = 0.05, ShardGrace = 1.0;

    /// <summary>
    /// 기체가 미는 튀는 빠르기 (m/s): 기압 차 × 잔칸 한 면(벽 한 면 2.5m²의 16분의 1) × 0.05초 ÷ 잔칸 질량 — 갈라질 때의 충격량과 같은 어림 (1기압이면 37kg 판이 20m/s쯤).
    /// 그 운동량은 기체가 준 것이라 몸을 밀지 않는다 (장부 '기체'에서 뺀다). 기압 차가 없으면 (끊기만 한 벽 · 찢긴 사이) 0.3m/s로 흩어질 뿐 — 그 반동은 몸에.
    /// </summary>
    private double BlowKick(double kPa, double eachKg) => eachKg <= 0 || kPa <= 0 ? 0 : Math.Min(30, kPa * 1000 * (Field.WallFace / 16) * BlowSec / eachKg);
    public const int ShardMax = 600;
    /// <summary>시험 설정 (찍지 않는다 — 장의 Adiabatic처럼): false면 떨어진 잔칸이 알갱이로 날지 않고 그 자리 빠르기 그대로 곧장 장부 '우주로' (튀지 않는다 — 몸만의 역학을 재는 법칙 시험).</summary>
    public bool Debris = true;

    /// <summary>배의 몸이 아직 없으면 세운다 (처음 부스러기가 날 때 — 세상 틀 = 도면 틀).</summary>
    private void EnsureShip()
    {
        if (Bodies.Count > 0) return;
        Bodies.Add(new RigidBody { Id = 0 });
        Refit0();
    }

    /// <summary>몸 b의 제 틀 자리 (px, py)에서 질량 kg을 뗀다 (잔칸 — 한 변 25cm 판): 질량 · 무게중심 · 회전관성이 줄고 · 빠르기는 새 무게중심의 v + ω×r (운동량이 꼭 맞는다).</summary>
    private void RemoveMass(int b, double kg, double px, double py, double k = 1.0 / 96) => RemoveMass2(b, kg, px, py, kg * (px * px + py * py + k));

    /// <summary>질량 kg (무게중심 (px, py) · 제 틀 원점에 대한 둘째 모멘트 sRem)을 몸 b에서 뗀다.</summary>
    private void RemoveMass2(int b, double kg, double px, double py, double sRem)
    {
        if (b < 0 || b >= Bodies.Count) return;
        var o = Bodies[b];
        if (o.Mass <= kg * 1.000001) { o.Mass = 0; o.Inertia = 0; o.Asleep = 1; Bodies[b] = o; return; }
        double s = o.Inertia + o.Mass * (o.Lx * o.Lx + o.Ly * o.Ly);   // 원점에 대한 둘째 모멘트
        double m2 = o.Mass - kg, lx = (o.Mass * o.Lx - kg * px) / m2, ly = (o.Mass * o.Ly - kg * py) / m2;
        double s2 = s - sRem;
        var (wx, wy) = WorldOf(b, lx, ly);
        double rx = wx - o.X, ry = wy - o.Y;
        o.Vx -= o.W * ry; o.Vy += o.W * rx;
        o.PVx -= o.PW * ry; o.PVy += o.PW * rx;
        o.X = wx; o.Y = wy; o.Lx = lx; o.Ly = ly;
        o.Mass = m2; o.Inertia = Math.Max(1e-9, s2 - m2 * (lx * lx + ly * ly));
        Bodies[b] = o;
    }

    /// <summary>
    /// 칸 c에서 떨어져 나간 잔칸들(removed)이 부스러기가 된다: 잔칸 여섯 개마다 알갱이 하나 (그 잔칸들의 가운데) · 그 자리 몸의 빠르기 + 튀는 빠르기 (kx, ky — 제 틀 방향 · 0이면 몸 바깥쪽) ·
    /// 몸은 그 질량을 잃고 튀는 만큼 반대로 밀린다. 몸이 없는 칸(우주가 된 칸)의 잔칸은 붙어 있던 몸(찢긴 끝의 주인)에서 · 그것도 없으면 배의 틀에서.
    /// </summary>
    private void Shed(Sim sim, int c, ushort removed, int cause, double kx, double ky, double kick = ShardKick, int owner = int.MinValue, bool byGas = false, bool away = false)
    {
        int n = SubCount(removed);
        double each = _smass.Length > c ? _smass[c] / 16 : 0;
        if (n == 0 || each <= 0) return;
        EnsureShip();
        int b = owner != int.MinValue ? owner : BodyOf(c);
        bool attached = b >= 0 && b < Bodies.Count && Bodies[b].Mass > 0;
        int frame = attached ? b : 0;
        var bits = new List<int>();
        for (int i = 0; i < 16; i++) if ((removed >> i & 1) != 0) bits.Add(i);
        int groups = Math.Max(1, (bits.Count + 5) / 6);
        for (int g = 0; g < groups; g++)
        {
            double kg = 0, px = 0, py = 0, sRem = 0;
            for (int k = g; k < bits.Count; k += groups)
            {
                int i = bits[k];
                double qx = L.X(c) + (i % SubN + 0.5) / SubN, qy = L.Y(c) + (i / SubN + 0.5) / SubN;
                kg += each; px += qx; py += qy; sRem += each * (qx * qx + qy * qy + 1.0 / 96);
            }
            int cnt = (bits.Count - g + groups - 1) / groups;
            px /= cnt; py /= cnt;
            double iOwn = sRem - kg * (px * px + py * py);   // 그 알갱이 제 무게중심에 대한 회전관성 (잔칸들이 흩어진 만큼)
            if (attached) RemoveMass2(b, kg, px, py, sRem);
            var o = Bodies[frame];
            var (wx, wy) = WorldOf(frame, px, py);
            double vx = o.Vx - o.W * (wy - o.Y), vy = o.Vy + o.W * (wx - o.X);
            double spin = iOwn * o.W;
            // 시험용으로 끈 때 · 평면 밖으로 떠난 것 (빠지는 공기가 바닥 판을 아래로 날린다 — 평면 안 빠르기는 그 자리 몸의 빠르기 그대로) → 곧장 장부 '우주로'
            if (!Debris || away) { ShardKgLost += kg; ShardPxGone += kg * vx; ShardPyGone += kg * vy; ShardLGone += kg * (wx * vy - wy * vx) + spin; if (away) ShardsMade++; continue; }
            // 튀는 쪽: 주어진 방향 (제 틀) · 없으면 몸 무게중심에서 바깥쪽 — 해시로 조금 흩뜨린다
            double cs = Math.Cos(o.Th), sn = Math.Sin(o.Th);
            double dx = kx, dy = ky;
            if (dx == 0 && dy == 0) { dx = px - o.Lx; dy = py - o.Ly; }
            double ang = ((DotHash(c * 16 + g, 31 + (int)(ShardsMade & 1023)) % 1000) / 1000.0 - 0.5) * (byGas ? 0.6 : 1.2);   // 빠지는 공기가 민 것은 한 쪽으로 (±17°) · 흩어지는 것은 ±34°
            double ca = Math.Cos(ang), sa = Math.Sin(ang), ddx = dx * ca - dy * sa, ddy = dx * sa + dy * ca;
            double dl = Math.Sqrt(ddx * ddx + ddy * ddy);
            if (dl < 1e-9) { ddx = 1; ddy = 0; dl = 1; }
            double speed = kick * (0.5 + (DotHash(c * 16 + g, 37) % 1000) / 1000.0);
            double kwx = (cs * ddx - sn * ddy) / dl * speed, kwy = (sn * ddx + cs * ddy) / dl * speed;
            if (byGas) { GasPx -= kg * kwx; GasPy -= kg * kwy; GasL -= wx * kg * kwy - wy * kg * kwx; }   // 기체가 밀었다 (몸이 아니라)
            else if (attached) Push(b, wx, wy, -kg * kwx, -kg * kwy);
            Shards.Add(new Shard { X = wx, Y = wy, Vx = vx + kwx, Vy = vy + kwy, Kg = (float)kg, Cause = cause, From = frame, Born = sim.Tick, Spin = spin });
            ShardsMade++;
        }
        // 너무 많으면 가장 오래된 것부터 장부 '우주로'
        while (Shards.Count > ShardMax) Lose(0);
    }

    /// <summary>칸 하나가 통째로 부스러기가 된다 (2톤 아래 덩어리): 남은 잔칸은 알갱이들로 · 설비는 칸 가운데의 알갱이 하나로 — 그 몸에서 뗀다.</summary>
    private void ShedLump(Sim sim, int c, int cause, double kx, double ky, double kick)
    {
        int b = BodyOf(c);
        if (Sub[c] != 0) { Shed(sim, c, Sub[c], cause, kx, ky, kick, b); Sub[c] = 0; }
        double fur = _mass[c] - _smass[c];
        if (fur <= 0) return;
        EnsureShip();
        bool attached = b >= 0 && b < Bodies.Count && Bodies[b].Mass > 0;
        int frame = attached ? b : 0;
        double cx = L.X(c) + 0.5, cy = L.Y(c) + 0.5;
        if (attached) RemoveMass(b, fur, cx, cy, 1.0 / 6);
        var o = Bodies[frame];
        var (wx, wy) = WorldOf(frame, cx, cy);
        double vx = o.Vx - o.W * (wy - o.Y), vy = o.Vy + o.W * (wx - o.X), spin = fur / 6 * o.W;
        if (!Debris) { ShardKgLost += fur; ShardPxGone += fur * vx; ShardPyGone += fur * vy; ShardLGone += fur * (wx * vy - wy * vx) + spin; return; }
        Shards.Add(new Shard { X = wx, Y = wy, Vx = vx, Vy = vy, Kg = (float)fur, Cause = cause, From = frame, Born = sim.Tick, Spin = spin });
        ShardsMade++;
    }

    /// <summary>우주가 된 사이 칸에 남은 잔칸 중 어느 몸에도 붙지 않은 것은 부스러기로 (그 칸이 속했던 몸 parent에서 뗀다).</summary>
    private void ShedOrphans(Sim sim, int c, int parent, int cause)
    {
        ushort m = Sub[c], orphan = 0;
        for (int i = 0; i < 16; i++) if ((m >> i & 1) != 0 && TornOwner(c, i % SubN, i / SubN) < 0) orphan |= (ushort)(1 << i);
        if (orphan == 0) return;
        Shed(sim, c, orphan, cause, 0, 0, SpallKick, parent);
        Sub[c] = (ushort)(m & ~orphan);
    }

    /// <summary>알갱이 하나를 장부 '우주로'로 (그 운동량 · 각운동량도).</summary>
    private void Lose(int i)
    {
        var s = Shards[i];
        ShardKgLost += s.Kg;
        ShardPxGone += s.Kg * s.Vx; ShardPyGone += s.Kg * s.Vy; ShardLGone += s.Kg * (s.X * s.Vy - s.Y * s.Vx) + s.Spin;
        Shards.RemoveAt(i);
    }

    /// <summary>잔걸음마다 알갱이가 곧게 난다 (알갱이마다 한 걸음에 20cm 넘지 않게 잘게 — 잔칸을 건너뛰지 않게) · 몸의 남은 잔칸에 들면 부딪힌다 · 배에서 멀어지면 우주로.</summary>
    private void MoveShards(Sim sim, double h)
    {
        if (Shards.Count == 0) return;
        var ship = Bodies.Count > 0 ? Bodies[0] : default;
        for (int i = Shards.Count - 1; i >= 0; i--)
        {
            var s = Shards[i];
            double sp = Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy);
            int n = Math.Clamp((int)Math.Ceiling(sp * h / 0.2), 1, 50_000);   // 300m/s도 한 걸음 20cm (벽을 건너뛰지 않게)
            double hh = h / n, age = (sim.Tick - s.Born) * Time.SecondsPerTick;
            bool gone = false;
            for (int k = 0; k < n && !gone; k++)
            {
                s.X += s.Vx * hh; s.Y += s.Vy * hh;
                Shards[i] = s;
                for (int b = 0; b < Bodies.Count; b++)
                {
                    if (Bodies[b].Mass <= 0 || b == s.From && age < ShardGrace) continue;
                    var (lx, ly) = LocalOf(b, s.X, s.Y);
                    int cx = (int)Math.Floor(lx), cy = (int)Math.Floor(ly);
                    if (!L.In(cx, cy)) continue;
                    int c = L.Index(cx, cy);
                    if (!SolidIn(b, c) || !SubAt(c, Math.Clamp((int)((lx - cx) * SubN), 0, SubN - 1), Math.Clamp((int)((ly - cy) * SubN), 0, SubN - 1))) continue;
                    Hit(sim, i, b, c);
                    gone = true;
                    break;
                }
            }
            if (gone) continue;
            double ddx = s.X - ship.X, ddy = s.Y - ship.Y;
            if (ddx * ddx + ddy * ddy > ShardRange * ShardRange) Lose(i);
        }
    }

    /// <summary>알갱이가 몸에 부딪혔다: 상대 운동량을 넘기고 붙는다 · ½mv²이 크면 ④ 충격 길 (금 · 구멍).</summary>
    private void Hit(Sim sim, int i, int b, int c)
    {
        var s = Shards[i];
        var o = Bodies[b];
        double vbx = o.Vx - o.W * (s.Y - o.Y), vby = o.Vy + o.W * (s.X - o.X);
        double rvx = s.Vx - vbx, rvy = s.Vy - vby;
        double e = 0.5 * s.Kg * (rvx * rvx + rvy * rvy);
        Push(b, s.X, s.Y, s.Kg * rvx, s.Kg * rvy);
        // 붙은 질량은 몸과 함께 간다 — 장부에는 붙을 때의 운동량으로 (몸 질량에는 넣지 않는다)
        ShardKgStuck += s.Kg;
        ShardPxGone += s.Kg * vbx; ShardPyGone += s.Kg * vby; ShardLGone += s.Kg * (s.X * vby - s.Y * vbx) + s.Spin;
        Shards.RemoveAt(i);
        ShardHits++;
        if (e < 100) return;
        double cs = Math.Cos(o.Th), sn = Math.Sin(o.Th);
        double lvx = cs * rvx + sn * rvy, lvy = -sn * rvx + cs * rvy;
        int dx = Math.Abs(lvx) >= Math.Abs(lvy) ? Math.Sign(lvx) : 0, dy = dx == 0 ? Math.Sign(lvy) : 0;
        int cause = sim.Causes.Add(CauseKind.Impact, $"날아온 부스러기({s.Kg:0.#}kg · {Math.Sqrt(rvx * rvx + rvy * rvy):0.0}m/s)가 부딪혔다", sim.Tick, c, s.Cause);
        Impact(sim, c, e, dx, dy, cause, Math.Cbrt(s.Kg / 7850.0));   // 크기: 같은 무게 강철 정육면체의 한 변
    }

    /// <summary>요청 (시험 · 다른 배의 잔해): 세상 자리 (x, y)에서 질량 kg의 부스러기를 (vx, vy)로 던진다.</summary>
    public void Throw(Sim sim, double x, double y, double vx, double vy, double kg, int cause)
    {
        EnsureShip();
        Shards.Add(new Shard { X = x, Y = y, Vx = vx, Vy = vy, Kg = (float)kg, Cause = cause, From = -1, Born = sim.Tick });
        ShardsMade++;
    }

    /// <summary>남은 판 · 골조 질량 (잔칸만큼 — 우주가 된 칸 · 테두리까지) — 법칙 장부.</summary>
    public double StructRemain()
    {
        double m = 0;
        for (int c = 0; c < L.N; c++) if (_smass[c] > 0) m += _smass[c] * SubCount(Sub[c]) / 16.0;
        return m;
    }
    public double StructInitial => _smassTotal;
    public double ShardKgFlying { get { double m = 0; foreach (var s in Shards) m += s.Kg; return m; } }

    /// <summary>알갱이 운동량 (날고 있는 것 + 우주로 · 붙은 것의 장부).</summary>
    public (double px, double py, double l) ShardMomentum()
    {
        double px = ShardPxGone, py = ShardPyGone, l = ShardLGone;
        foreach (var s in Shards) { px += s.Kg * s.Vx; py += s.Kg * s.Vy; l += s.Kg * (s.X * s.Vy - s.Y * s.Vx) + s.Spin; }
        return (px, py, l);
    }

    private void SaveShards(StateWriter w)
    {
        w.Tag("부스러기"); w.List(Shards);
        w.F64(ShardKgStuck); w.F64(ShardKgLost); w.F64(ShardPxGone); w.F64(ShardPyGone); w.F64(ShardLGone); w.I64(ShardsMade); w.I64(ShardHits);
    }

    private void LoadShards(StateReader r)
    {
        r.Tag("부스러기"); Shards.Clear(); Shards.AddRange(r.List<Shard>());
        ShardKgStuck = r.F64(); ShardKgLost = r.F64(); ShardPxGone = r.F64(); ShardPyGone = r.F64(); ShardLGone = r.F64(); ShardsMade = r.I64(); ShardHits = r.I64();
    }
}
