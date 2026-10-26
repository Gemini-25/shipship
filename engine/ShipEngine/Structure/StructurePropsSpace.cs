namespace ShipEngine;

/// <summary>
/// ④ 우주의 물체 (docs/PHYSICS.md §3-6 '빠져나간 것' · PH11): 구멍 밖으로 나간 물체 (State 5)는 실린 몸 없이 (Host −1) 세상 틀로 난다 —
/// 나간 틱 끝에 그 몸의 세상 자리 · 점 빠르기 · 각속도로 옮기고 (몸이 없는 배면 처음 나가는 것이 배의 몸을 세운다) · 그 뒤 틱마다 곧게 날며 돈다 (힘이 없다 — 한 걸음에 정확히).
/// 몸의 벽 · 외판 칸에 다가가 닿으면 튄다 (반발 0.3 · 몸이 되받는다) · 몸의 트인 칸에 들면 그 몸에 실린다 (바닥에 대한 빠르기로) · 배 가운데에서 300m를 넘으면 장부 '우주로'.
/// 모두 찍힌 상태로만 정한다 (나는 것은 Props 그대로 — 찍는다).
/// </summary>
public sealed partial class Structure
{
    /// <summary>우주로 간 것으로 칠 거리 (m — 배의 몸 가운데에서) · 몸에 닿아 튈 때의 반발 · 닿음을 살피는 걸음 (m).</summary>
    public const double SpaceGoneM = 300, SpaceBounce = 0.3, SpaceProbeM = 0.2, SpaceHoleM2 = 0.01;

    /// <summary>세상 틀로 나는 물체인가 (구멍 밖 우주 · 실린 몸 없음).</summary>
    public static bool Flying(in Prop p) => p.State == 5 && p.Host == -1;

    /// <summary>이번 틱에 구멍 밖으로 나간 것 (State 5 · 아직 몸 틀): 그 몸의 세상 값으로 옮겨 세상 틀로 날게 한다 (몸이 없는 배면 배의 몸을 세운다 — 세상 틀 = 도면 틀).</summary>
    private void Launch(Span<Prop> all, bool world)
    {
        for (int i = 0; i < all.Length; i++)
        {
            ref var p = ref all[i];
            if (p.State != 5 || p.Host == -1) continue;
            if (!world) EnsureShip();
            int b = world && i < _hostOf.Length ? _hostOf[i] : 0;
            if (b < 0 || b >= Bodies.Count || Bodies[b].Mass <= 0) b = Bodies.Count > 0 && Bodies[0].Mass > 0 ? 0 : -1;
            if (b >= 0)
            {
                var o = Bodies[b];
                double c = Math.Cos(o.Th), s = Math.Sin(o.Th), dx = p.X - o.Lx, dy = p.Y - o.Ly, rx = c * dx - s * dy, ry = s * dx + c * dy;
                double vx = o.Vx - o.W * ry + c * p.Vx - s * p.Vy, vy = o.Vy + o.W * rx + s * p.Vx + c * p.Vy;
                p.X = o.X + rx; p.Y = o.Y + ry; p.Vx = vx; p.Vy = vy; p.W += o.W; p.Th += o.Th;
            }
            p.Host = -1; p.Z = 0; p.Vz = 0; p.PlugAt = 0;
        }
    }

    /// <summary>
    /// 세상 틀로 나는 것을 한 틱 (dt) 날린다: 곧게 · 돌며 — 몸의 칸에 닿으면 (그 몸 틀에서 다가가는 쪽일 때만) 튀거나 (벽 · 외판 · 문) 실린다 (트인 칸) · 300m를 넘으면 장부 '우주로'.
    /// </summary>
    private void Fly(Sim sim, double dt)
    {
        bool gone = false;
        var all = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Props);
        double cx = Bodies.Count > 0 ? Bodies[0].X : L.W / 2.0, cy = Bodies.Count > 0 ? Bodies[0].Y : L.H / 2.0;
        for (int i = 0; i < all.Length; i++)
        {
            ref var p = ref all[i];
            if (!Flying(p)) continue;
            if (p.Kg <= 0) FillBody(ref p);
            double len = Math.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy) * dt;
            int n = Math.Max(1, (int)Math.Ceiling(len / SpaceProbeM));
            double h = dt / n, t = 0;
            for (int k = 0; k < n && Flying(p); k++)
            {
                double x0 = p.X, y0 = p.Y;
                p.X += p.Vx * h; p.Y += p.Vy * h; p.Th += p.W * h; t += h;
                if (len > 0) Touch(sim, ref p, x0, y0);
            }
            if (Flying(p) && t < dt) { p.X += p.Vx * (dt - t); p.Y += p.Vy * (dt - t); p.Th += p.W * (dt - t); }
            if (Flying(p) && ((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy) > SpaceGoneM * SpaceGoneM)) { p.Host = -2; gone = true; }
        }
        if (!gone) return;
        foreach (var p in Props)
        {
            if (p.State != 5 || p.Host != -2) continue;
            PropPxGone += p.Kg * p.Vx; PropPyGone += p.Kg * p.Vy; PropLGone += p.Kg * (p.X * p.Vy - p.Y * p.Vx) + p.Inertia * p.W;
            if (p.Kind == 0) sim.Book.AddItem(p.What, Flow.Spaced, -1);
            PropSpaced++;
            Track?.Gone.Add(p);
        }
        Props.RemoveAll(p => p.State == 5 && p.Host == -2);
    }

    /// <summary>나는 것이 (x0, y0)에서 지금 자리로 왔다: 몸마다 그 틀로 옮겨 그 몸의 칸에 새로 들었으면 — 벽 · 외판 · 닫힌 문이면 튀고 · 트인 칸이면 그 몸에 실린다.</summary>
    private void Touch(Sim sim, ref Prop p, double x0, double y0)
    {
        for (int b = 0; b < Bodies.Count; b++)
        {
            var o = Bodies[b];
            if (o.Mass <= 0) continue;
            var (lx, ly) = LocalOf(b, p.X, p.Y);
            int ix = (int)Math.Floor(lx), iy = (int)Math.Floor(ly);
            if (!L.In(ix, iy)) continue;
            int c = L.Index(ix, iy);
            if (BodyOf(c) != b || L.Kind[c] == CellKind.Outside) continue;
            if (L.Kind[c] == CellKind.Wall && F.Hole[c] >= SpaceHoleM2) continue;   // 뚫린 벽 칸은 지나간다 (나간 그 구멍 · 큰 구멍)
            var (px, py) = LocalOf(b, x0, y0);
            int jx = (int)Math.Floor(px), jy = (int)Math.Floor(py);
            if (jx == ix && jy == iy) continue;   // 이미 그 칸에 있었다 (나간 구멍 칸 — 들어가는 것이 아니다)
            double cs = Math.Cos(o.Th), sn = Math.Sin(o.Th), rx = p.X - o.X, ry = p.Y - o.Y;
            double ux = p.Vx - (o.Vx - o.W * ry), uy = p.Vy - (o.Vy + o.W * rx);   // 몸에 대한 세상 빠르기
            double lvx = cs * ux + sn * uy, lvy = -sn * ux + cs * uy;                // 몸 틀
            bool open = F.Passable(c) && !(L.Kind[c] == CellKind.Wall);
            if (open)
            {
                // 그 몸에 실린다 — 바닥에 대한 빠르기 · 제 돎은 몸의 돎을 뺀다 (세상 운동량은 그대로)
                p.X = lx; p.Y = ly; p.Vx = lvx; p.Vy = lvy; p.W -= o.W; p.Th -= o.Th;
                p.Host = 0; p.State = 1; p.Asleep = 0; p.Still = 0; p.Since = _now;
                return;
            }
            // 벽 · 외판 · 닫힌 문: 들어온 면의 법선으로 튄다 (다가갈 때만) — 충격량은 그 몸이 되받는다
            double nx = jx < ix ? -1 : jx > ix ? 1 : 0, ny = nx == 0 ? (jy < iy ? -1 : 1) : 0;   // 몸 틀에서 칸 밖으로 (들어온 쪽)
            double vn = lvx * nx + lvy * ny;
            if (vn >= 0) continue;
            double j = -(1 + SpaceBounce) * vn * p.Kg;
            lvx += j / p.Kg * nx; lvy += j / p.Kg * ny;
            double wvx = cs * lvx - sn * lvy, wvy = sn * lvx + cs * lvy;
            p.Vx = wvx + (o.Vx - o.W * ry); p.Vy = wvy + (o.Vy + o.W * rx);
            double gjx = -(cs * nx - sn * ny) * j, gjy = -(sn * nx + cs * ny) * j;   // 몸이 받는 충격량 (세상 틀)
            Push(b, p.X, p.Y, gjx, gjy, p.Cause);
            p.X = x0; p.Y = y0;   // 칸 밖 (지난 자리)으로
            Bounces++;
            return;
        }
    }
}
