using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v19 모양 있는 배 여덟 척의 선체 바깥 (읽기만 · 모두 그림):
//  화살촉형 — 앞전을 따라 흐르는 붉은 줄 · 뱃머리 탐침 · 날개 끝 섬광
//  망치머리형 — 망치 양 끝에서 도는 감지 접시 · 망치 앞면 불빛 줄
//  원반형 — 원반 테두리를 따라 도는 불빛 · 나셀 바깥의 푸른 빛줄과 앞끝 붉은 흡입구
//  삼지창형 — 갈래 끝 미늘의 노랑 · 검정 경고 줄무늬와 굴착 이빨
//  가오리형 — 흔들리는 긴 꼬리 · 뿔 끝 불빛 · 날개 가장자리의 푸른 빛 점
//  고래형 — 몸통 위아래 둥근 창 불빛 · 꼬리 지느러미 뒤 은은한 빛
//  잠자리형 — 날개 끝 너머로 펼친 반투명 날개막(잎맥) · 머리의 더듬이 둘
//  쐐기형 — 앞전을 따라 뱃머리로 달려가는 흰 항해등 · 뒷면 엔진의 푸른 띠
// 모양 있는 배는 용도 장식(온실 돔 · 접시 · 십자)을 상자 위가 아니라 실제 선체 가장자리에 붙인다.
public partial class ShipView
{
    private int[]? _colTop, _colBot, _rowLeft, _rowRight;
    private int _edgeVer = -1;
    private Ship? _edgeShip;

    private static bool Shaped(ShipFrame f) => f >= ShipFrame.Arrow;

    private void BuildEdges()
    {
        var ship = _world.Ship;
        var g = ship.Grid;
        if (_edgeShip == ship && _edgeVer == _world.Structure.Version && _colTop != null && _colTop.Length == g.Width && _rowLeft!.Length == g.Height) return;
        _edgeShip = ship;
        _edgeVer = _world.Structure.Version;
        _colTop = Enumerable.Repeat(-1, g.Width).ToArray();
        _colBot = Enumerable.Repeat(-1, g.Width).ToArray();
        _rowLeft = Enumerable.Repeat(-1, g.Height).ToArray();
        _rowRight = Enumerable.Repeat(-1, g.Height).ToArray();
        for (int i = 0; i < g.CellCount; i++)
        {
            var c = g.CellAt(i);
            if (g.Kind(c) == TileKind.Void) continue;
            if (ship.RoomAt(c) is Room r && r.Detached) continue;
            if (_colTop[c.X] < 0 || c.Y < _colTop[c.X]) _colTop[c.X] = c.Y;
            if (c.Y > _colBot![c.X]) _colBot[c.X] = c.Y;
            if (_rowLeft[c.Y] < 0 || c.X < _rowLeft[c.Y]) _rowLeft[c.Y] = c.X;
            if (c.X > _rowRight![c.Y]) _rowRight[c.Y] = c.X;
        }
    }

    /// <summary>x 칸 열의 위(또는 아래) 선체 가장자리 (픽셀 · 칸 바깥 테두리).</summary>
    private Vector2 EdgeAt(int x, bool top) => new((x + 0.5f) * T, top ? _colTop![x] * T : (_colBot![x] + 1) * T);

    /// <summary>계단진 가장자리를 바깥에서 감싸는 볼록 껍질 위의 점 (칸마다 하나) — 비스듬한 외판을 따라가되 선체 안으로 파고들지 않는다.</summary>
    private List<Vector2> EdgeLine(int x0, int x1, bool top, float off)
    {
        var pts = new List<Vector2>();
        for (int x = x0; x <= x1; x++)
        {
            if (x < 0 || x >= _colTop!.Length || _colTop[x] < 0) continue;
            float y = top ? -_colTop[x] * T : (_colBot![x] + 1) * T; // 위쪽은 뒤집어 '가장 큰 값' 껍질로
            pts.Add(new Vector2(x * T, y));
            pts.Add(new Vector2((x + 1) * T, y));
        }
        var hull = new List<Vector2>();
        foreach (var p in pts)
        {
            while (hull.Count >= 2)
            {
                var o = hull[^2]; var q = hull[^1];
                float cross = (q.X - o.X) * (p.Y - o.Y) - (q.Y - o.Y) * (p.X - o.X);
                if (cross >= 0f) hull.RemoveAt(hull.Count - 1); else break;
            }
            hull.Add(p);
        }
        var outp = new List<Vector2>();
        int k = 0;
        for (int x = x0; x <= x1; x++)
        {
            if (x < 0 || x >= _colTop!.Length || _colTop[x] < 0 || hull.Count < 2) continue;
            float px = (x + 0.5f) * T;
            while (k < hull.Count - 2 && hull[k + 1].X < px) k++;
            var a = hull[k]; var b = hull[k + 1];
            float u = b.X - a.X > 0.01f ? Mathf.Clamp((px - a.X) / (b.X - a.X), 0f, 1f) : 0f;
            float y = Mathf.Lerp(a.Y, b.Y, u);
            outp.Add(new Vector2(px, top ? -y - off : y + off));
        }
        return outp;
    }

    /// <summary>껍질 위의 점이 실제 선체 가장자리 가까이 있는지 (오목한 곳 위 허공에 불빛이 뜨지 않게).</summary>
    private bool NearEdge(Vector2 p, bool top, float tol = T * 1.6f)
    {
        int x = Mathf.Clamp((int)(p.X / T), 0, _colTop!.Length - 1);
        if (_colTop[x] < 0) return false;
        return Mathf.Abs(EdgeAt(x, top).Y - p.Y) < tol;
    }

    private (int x0, int x1) Span()
    {
        int x0 = System.Array.FindIndex(_colTop!, v => v >= 0), x1 = System.Array.FindLastIndex(_colTop!, v => v >= 0);
        return (x0, x1);
    }

    /// <summary>가장 위(top) 또는 가장 아래 칸들 중 가운데 칸.</summary>
    private Vector2 Extreme(bool top, int x0 = 0, int x1 = int.MaxValue)
    {
        int best = top ? int.MaxValue : -1;
        var xs = new List<int>();
        for (int x = System.Math.Max(0, x0); x <= System.Math.Min(_colTop!.Length - 1, x1); x++)
        {
            int v = top ? _colTop[x] : _colBot![x];
            if (v < 0) continue;
            if (top ? v < best : v > best) { best = v; xs.Clear(); }
            if (v == best) xs.Add(x);
        }
        if (xs.Count == 0) return Vector2.Zero;
        return EdgeAt(xs[xs.Count / 2], top);
    }

    private void Lamp(CanvasItem ci, Vector2 p, Color c, float a, float size = 1f)
    {
        ci.Circle(p, 8f * size, c.WithAlpha(0.12f * a), true, -1f, true);
        ci.Circle(p, 4f * size, c.WithAlpha(0.35f * a), true, -1f, true);
        ci.Circle(p, 1.9f * size, c.Lightened(0.4f).WithAlpha(a), true, -1f, true);
    }

    /// <summary>엔진 코어 뒤 은은한 불꽃 (원자로가 돌 때만 · 천천히 숨쉰다).</summary>
    private void PaintIdlePlumes(CanvasItem ci)
    {
        if (!_world.Power.ReactorOnline) return;
        foreach (var f in _world.Ship.FurnitureOf(FurnitureType.EngineCore))
        {
            if (f.Room.Detached) continue;
            float x = (f.MinX - 1) * T - NozzleLen - 10f;
            float y0 = f.MinY * T + 6f, y1 = (f.MaxY + 1) * T - 6f;
            float mid = (y0 + y1) * 0.5f, half = (y1 - y0) * 0.5f;
            float pulse = 0.75f + 0.25f * Mathf.Sin(_time * 3.1f + f.Id);
            for (int k = 4; k >= 1; k--)
            {
                float len = T * (0.9f + k * 0.55f) * pulse, w = half * (1.15f - k * 0.18f);
                var col = new Color(0.45f + 0.1f * (4 - k), 0.72f + 0.06f * (4 - k), 1f, 0.07f + 0.05f * (4 - k));
                ci.Poly(new[] { new Vector2(x, mid - w), new Vector2(x - len, mid), new Vector2(x, mid + w) }, col);
            }
        }
    }

    private void PaintShapeKind(CanvasItem ci, ShipInfo info)
    {
        BuildEdges();
        var (x0, x1) = Span();
        if (x0 < 0) return;
        float t = _time;
        var metal = new Color("#6f7888");
        var dark = new Color("#3c434f");
        bool lit = _world.Power.BatteryPercent > 0.02f || _world.Power.ReactorOnline;
        PaintIdlePlumes(ci);
        float axis = (Extreme(true).Y + Extreme(false).Y) * 0.5f;
        switch (info.Frame)
        {
            case ShipFrame.Arrow:
            {
                // 날개 끝(가장 위 · 아래 칸)에서 뱃머리까지 앞전을 따라 흐르는 붉은 줄
                foreach (bool top in new[] { true, false })
                {
                    var tip = Extreme(top);
                    int tx = Mathf.Clamp((int)(tip.X / T), x0, x1);
                    var line = EdgeLine(tx, x1, top, 4f);
                    if (line.Count > 1)
                    {
                        ci.Polyline(line.ToArray(), new Color("#c0392b").WithAlpha(0.85f), 3f, true);
                        ci.Polyline(line.Select(p => p + new Vector2(0f, top ? -4f : 4f)).ToArray(), new Color("#d8dee8").WithAlpha(0.35f), 1.2f, true);
                    }
                    if (lit) Lamp(ci, tip + new Vector2(-T * 0.2f, top ? -6f : 6f), Colors.White, Mathf.PosMod(t + (top ? 0f : 0.15f), 1.4f) < 0.1f ? 1f : 0.12f);
                }
                // 뱃머리 탐침
                var nose = new Vector2((x1 + 1) * T, axis);
                ci.DrawLine(nose, nose + new Vector2(T * 1.6f, 0f), metal, 2f, true);
                ci.DrawLine(nose + new Vector2(T * 0.5f, -3f), nose + new Vector2(T * 0.5f, 3f), metal, 1.5f, true);
                if (lit) Lamp(ci, nose + new Vector2(T * 1.6f, 0f), new Color("#ff4d4d"), 0.55f + 0.45f * Mathf.Sin(t * 3f), 0.8f);
                break;
            }
            case ShipFrame.Hammerhead:
            {
                // 망치 양 끝 감지 접시 (천천히 고개를 돌린다)
                foreach (bool top in new[] { true, false })
                {
                    var end = Extreme(top) + new Vector2(0f, top ? -4f : 4f);
                    float dir = top ? -1f : 1f;
                    float sweep = Mathf.Sin(t * 0.35f + (top ? 0f : 2f)) * 0.6f;
                    var mast = end + new Vector2(0f, dir * T * 0.9f);
                    ci.DrawLine(end, mast, metal, 3f, true);
                    float ang = (top ? -Mathf.Pi / 2f : Mathf.Pi / 2f) + sweep;
                    ci.Arc(mast + Vector2.FromAngle(ang) * 6f, 13f, ang + Mathf.Pi - 1.15f, ang + Mathf.Pi + 1.15f, 14, new Color("#c8d0dc"), 3f, true);
                    ci.DrawLine(mast, mast + Vector2.FromAngle(ang) * 14f, metal.Lightened(0.2f), 1.5f, true);
                    if (lit) Lamp(ci, mast + Vector2.FromAngle(ang) * 15f, new Color("#7cf0a0"), Mathf.PosMod(t * 0.8f + (top ? 0f : 0.5f), 1f) < 0.2f ? 1f : 0.25f, 0.7f);
                }
                // 망치 앞면 불빛 줄 (오른쪽 끝 열들 · 세 칸마다)
                int front = _rowRight!.Max();
                for (int y = 0; y < _rowRight.Length; y++)
                {
                    if (_rowRight[y] < front - 8 || _rowRight[y] < 0 || y % 3 != 0) continue;
                    var p = new Vector2((_rowRight[y] + 1) * T + 5f, (y + 0.5f) * T);
                    if (lit) Lamp(ci, p, new Color("#ffcf6b"), 0.35f + 0.25f * Mathf.Sin(t * 1.5f + y * 0.4f), 0.6f);
                }
                break;
            }
            case ShipFrame.Saucer:
            {
                // 원반: 배 앞쪽 절반의 칸들로 둥근 테두리를 잡는다
                int mid = (x0 + x1) / 2;
                float cx0 = float.MaxValue, cx1 = float.MinValue, cy0 = float.MaxValue, cy1 = float.MinValue;
                for (int x = mid; x <= x1; x++)
                {
                    if (_colTop![x] < 0) continue;
                    cx0 = Mathf.Min(cx0, x * T); cx1 = Mathf.Max(cx1, (x + 1) * T);
                    cy0 = Mathf.Min(cy0, _colTop[x] * T); cy1 = Mathf.Max(cy1, (_colBot![x] + 1) * T);
                }
                float rr = (cy1 - cy0) * 0.5f;
                var c = new Vector2(cx1 - rr, (cy0 + cy1) * 0.5f);
                float rad = rr + 9f;
                ci.Arc(c, rad, 0f, Mathf.Tau, 120, dark.WithAlpha(0.6f), 2f, true);
                int n = 40;
                float run = Mathf.PosMod(t * 0.35f, 1f) * n;
                for (int i = 0; i < n; i++)
                {
                    var p = c + Vector2.FromAngle(i * Mathf.Tau / n) * rad;
                    float d = Mathf.PosMod(i - run, n);
                    float a = d < 4f ? 1f - d / 4f : 0.12f;
                    if (lit) ci.Circle(p, 2.2f, new Color("#9fe3ff").WithAlpha(0.25f + 0.75f * a), true, -1f, true);
                }
                // 나셀 (배 뒤쪽 절반의 맨 위 · 맨 아래): 바깥 가장자리 푸른 빛줄 · 앞끝 붉은 흡입구
                foreach (bool top in new[] { true, false })
                {
                    var e = Extreme(top, x0, mid);
                    int row = (int)((top ? e.Y : e.Y - T) / T);
                    int nx0 = int.MaxValue, nx1 = -1;
                    for (int x = x0; x <= mid; x++)
                        if (_colTop![x] >= 0 && (top ? _colTop[x] <= row + 1 : _colBot![x] >= row - 1)) { nx0 = System.Math.Min(nx0, x); nx1 = System.Math.Max(nx1, x); }
                    if (nx1 < 0) continue;
                    var line = EdgeLine(nx0 + 1, nx1 - 2, top, 3f);
                    if (line.Count > 1)
                    {
                        float glow = lit ? 0.55f + 0.25f * Mathf.Sin(t * 2.2f + (top ? 0f : 1f)) : 0.15f;
                        ci.Polyline(line.ToArray(), new Color(0.35f, 0.75f, 1f, 0.25f * glow), 9f, true);
                        ci.Polyline(line.ToArray(), new Color(0.6f, 0.9f, 1f, glow), 2.5f, true);
                    }
                    var nose = new Vector2((nx1 + 1) * T + 2f, (row + (top ? 2.5f : -1.5f)) * T);
                    if (lit) Lamp(ci, nose, new Color("#ff5a3c"), 0.7f + 0.3f * Mathf.Sin(t * 4f), 1.4f);
                }
                break;
            }
            case ShipFrame.Trident:
            {
                // 갈래 끝 셋: 위 · 아래 미늘 끝 (가장 위 · 아래 칸 중 가장 앞) — 경고 줄무늬 · 굴착 이빨
                foreach (bool top in new[] { true, false })
                {
                    var tip = Extreme(top);
                    float dir = top ? -1f : 1f;
                    var basep = tip + new Vector2(0f, dir * 3f);
                    for (int k = 0; k < 4; k++)
                    {
                        var a = basep + new Vector2(-T * 0.9f + k * 9f, 0f);
                        ci.DrawLine(a, a + new Vector2(6f, dir * 7f), k % 2 == 0 ? new Color("#f2b134") : new Color("#1b1d22"), 5f, true);
                    }
                    for (int k = 0; k < 3; k++) // 굴착 이빨 (천천히 돈다)
                    {
                        float ang = t * 1.6f + k * Mathf.Tau / 3f;
                        var hub = basep + new Vector2(T * 0.6f, dir * T * 0.45f);
                        ci.DrawLine(hub, hub + Vector2.FromAngle(ang) * 9f, new Color("#a99a7a"), 3f, true);
                    }
                    ci.Circle(basep + new Vector2(T * 0.6f, dir * T * 0.45f), 4f, dark, true, -1f, true);
                }
                break;
            }
            case ShipFrame.Manta:
            {
                // 흔들리는 긴 꼬리 (축 줄 맨 뒤에서)
                int ay = (int)(axis / T);
                int lx = _rowLeft![Mathf.Clamp(ay, 0, _rowLeft.Length - 1)];
                var root = new Vector2(lx * T - 2f, axis);
                var pts = new Vector2[16];
                for (int i = 0; i < pts.Length; i++)
                    pts[i] = root + new Vector2(-i * T * 0.85f, Mathf.Sin(t * 1.3f - i * 0.45f) * i * 1.1f);
                for (int i = 0; i < pts.Length - 1; i++)
                    ci.DrawLine(pts[i], pts[i + 1], new Color("#2d3542"), Mathf.Lerp(7f, 1.2f, i / (float)pts.Length), true);
                if (lit) Lamp(ci, pts[^1], new Color("#7cf0e0"), 0.4f + 0.3f * Mathf.Sin(t * 2f), 0.6f);
                // 날개 가장자리 빛 점 (축에서 멀리 떨어진 열만)
                foreach (bool top in new[] { true, false })
                {
                    var line = EdgeLine(x0, x1, top, 5f);
                    for (int i = 0; i < line.Count; i += 2)
                    {
                        if (Mathf.Abs(line[i].Y - axis) < T * 13f || !NearEdge(line[i], top)) continue;
                        float a = 0.3f + 0.3f * Mathf.Sin(t * 1.4f + i * 0.35f);
                        if (lit) ci.Circle(line[i], 2f, new Color(0.45f, 0.95f, 0.85f, a), true, -1f, true);
                    }
                }
                // 뿔 끝: 축 가까운 줄 중 가장 앞으로 튀어나온 칸 (위 · 아래)
                foreach (int dir in new[] { -1, 1 })
                {
                    int best = -1, by = -1;
                    for (int dy = 6; dy <= 16; dy++)
                    {
                        int y = ay + dir * dy;
                        if (y < 0 || y >= _rowRight!.Length) continue;
                        if (_rowRight[y] > best) { best = _rowRight[y]; by = y; }
                    }
                    if (best > 0 && lit) Lamp(ci, new Vector2((best + 1) * T + 4f, (by + 0.5f) * T), new Color("#7cf0e0"), 0.6f + 0.4f * Mathf.Sin(t * 2.6f + dir), 1.1f);
                }
                break;
            }
            case ShipFrame.Whale:
            {
                // 몸통 위아래 둥근 창 불빛 (네 칸마다 · 따뜻한 빛)
                foreach (bool top in new[] { true, false })
                {
                    var line = EdgeLine(x0, x1, top, 5f);
                    for (int i = 0; i < line.Count; i += 4)
                    {
                        if (Mathf.Abs(line[i].Y - axis) < T * 8f || !NearEdge(line[i], top)) continue; // 가는 꼬리 통로 · 오목한 곳은 건너뛴다
                        if (lit) ci.Circle(line[i], 2.6f, new Color(1f, 0.82f, 0.55f, 0.35f + 0.2f * Mathf.Sin(t * 0.7f + i)), true, -1f, true);
                        ci.Arc(line[i], 3.6f, 0f, Mathf.Tau, 10, dark, 1f, true);
                    }
                }
                // 꼬리 지느러미 뒤 은은한 빛
                var fl = new Vector2(x0 * T - T * 0.5f, axis);
                if (lit) for (int k = 3; k >= 1; k--) ci.Circle(fl, T * (0.9f + k * 0.8f), new Color(0.4f, 0.7f, 1f, 0.05f), true, -1f, true);
                break;
            }
            case ShipFrame.Dragonfly:
            {
                // 날개막: 축에서 멀리 뻗은 열들(날개)을 묶어, 끝 너머로 반투명 막과 잎맥
                foreach (bool top in new[] { true, false })
                {
                    var runs = new List<(int a, int b)>();
                    int start = -1;
                    for (int x = x0; x <= x1 + 1; x++)
                    {
                        bool wing = x <= x1 && _colTop![x] >= 0 && Mathf.Abs(EdgeAt(x, top).Y - axis) > T * 10f;
                        if (wing && start < 0) start = x;
                        if (!wing && start >= 0) { runs.Add((start, x - 1)); start = -1; }
                    }
                    for (int wi = 0; wi < runs.Count; wi++)
                    {
                        var (a, b) = runs[wi];
                        float dir = top ? -1f : 1f;
                        float lean = wi == runs.Count - 1 ? 1f : -1f; // 앞날개는 앞으로, 뒷날개는 뒤로
                        var ea = EdgeAt(a, top); var eb = EdgeAt(b, top);
                        float edgeY = top ? Mathf.Min(ea.Y, eb.Y) : Mathf.Max(ea.Y, eb.Y);
                        float len = T * 9f, wide = (b - a + 1) * T * 0.55f;
                        var c0 = new Vector2((ea.X + eb.X) * 0.5f, edgeY + dir * 4f);
                        var tip = c0 + new Vector2(lean * T * 2.6f, dir * len);
                        var poly = new List<Vector2>();
                        for (int k = 0; k <= 18; k++)
                        {
                            float u = k / 18f;
                            var along = c0.Lerp(tip, u);
                            float w = wide * Mathf.Sin(Mathf.Pi * Mathf.Lerp(0.12f, 1f, u)) * (1f - 0.35f * u);
                            poly.Add(along + new Vector2(-w, 0f));
                        }
                        for (int k = 18; k >= 0; k--)
                        {
                            float u = k / 18f;
                            var along = c0.Lerp(tip, u);
                            float w = wide * Mathf.Sin(Mathf.Pi * Mathf.Lerp(0.12f, 1f, u)) * (1f - 0.35f * u);
                            poly.Add(along + new Vector2(w, 0f));
                        }
                        float shimmer = 0.10f + 0.04f * Mathf.Sin(t * 1.7f + wi);
                        ci.Poly(poly.ToArray(), new Color(0.6f, 0.85f, 1f, shimmer));
                        ci.Polyline(poly.Append(poly[0]).ToArray(), new Color(0.75f, 0.9f, 1f, 0.35f), 1.2f, true);
                        for (int v = -2; v <= 2; v++) // 잎맥
                            ci.DrawLine(c0 + new Vector2(v * wide * 0.25f, 0f), tip + new Vector2(v * wide * 0.08f, -dir * len * 0.08f * System.Math.Abs(v)), new Color(0.75f, 0.9f, 1f, 0.22f), 1f, true);
                        for (int h = 1; h <= 4; h++)
                        {
                            var p = c0.Lerp(tip, h / 5f);
                            float w = wide * 0.8f * Mathf.Sin(Mathf.Pi * Mathf.Lerp(0.12f, 1f, h / 5f));
                            ci.DrawLine(p - new Vector2(w, 0f), p + new Vector2(w, 0f), new Color(0.75f, 0.9f, 1f, 0.15f), 1f, true);
                        }
                    }
                }
                // 머리 더듬이
                var head = new Vector2((x1 + 1) * T, axis);
                foreach (int s in new[] { -1, 1 })
                {
                    float sway = Mathf.Sin(t * 1.1f + s) * 4f;
                    var p1 = head + new Vector2(-T * 0.6f, s * T * 0.9f);
                    var p2 = p1 + new Vector2(T * 1.2f, s * T * 0.9f + sway);
                    var p3 = p2 + new Vector2(T * 0.9f, s * T * 0.2f + sway);
                    ci.Polyline(new[] { p1, p2, p3 }, metal, 2f, true);
                    if (lit) Lamp(ci, p3, new Color("#ffcf6b"), 0.6f + 0.4f * Mathf.Sin(t * 2.4f + s), 0.6f);
                }
                break;
            }
            case ShipFrame.Wedge:
            {
                // 앞전을 따라 뱃머리로 달려가는 흰 항해등
                foreach (bool top in new[] { true, false })
                {
                    var line = EdgeLine(x0 + 2, x1, top, 5f);
                    int n = line.Count;
                    float run = Mathf.PosMod(t * 18f, n + 20);
                    for (int i = 0; i < n; i += 3)
                    {
                        if (!NearEdge(line[i], top)) continue;
                        float d = run - i;
                        float a = d >= 0f && d < 9f ? 1f - d / 9f : 0.1f;
                        if (lit) ci.Circle(line[i], 2f, new Color(1f, 1f, 1f, 0.2f + 0.8f * a), true, -1f, true);
                    }
                }
                // 뒷면 엔진의 푸른 띠
                if (lit)
                {
                    float y0 = Extreme(true).Y, y1 = Extreme(false).Y;
                    float g = 0.35f + 0.15f * Mathf.Sin(t * 2f);
                    ci.DrawLine(new Vector2(x0 * T - 4f, y0 + T * 2f), new Vector2(x0 * T - 4f, y1 - T * 2f), new Color(0.45f, 0.75f, 1f, g * 0.5f), 8f, true);
                    ci.DrawLine(new Vector2(x0 * T - 4f, y0 + T * 2f), new Vector2(x0 * T - 4f, y1 - T * 2f), new Color(0.7f, 0.9f, 1f, g), 2f, true);
                }
                break;
            }
        }
        // 용도 장식 (실제 선체 가장자리에): 연구선 — 가장 위 · 아래 칸에 작은 접시 / 농업선 — 위 가장자리 온실 돔
        if (info.Purpose == ShipPurpose.Research && info.Frame != ShipFrame.Hammerhead)
        {
            foreach (bool top in new[] { true, false })
            {
                var p = Extreme(top) + new Vector2(T * 0.8f, top ? -3f : 3f);
                float ang = (top ? -Mathf.Pi / 2f : Mathf.Pi / 2f) + Mathf.Sin(t * 0.3f + (top ? 0 : 1)) * 0.4f;
                ci.DrawLine(p, p + Vector2.FromAngle(ang) * 9f, metal, 2f, true);
                ci.Arc(p + Vector2.FromAngle(ang) * 12f, 6f, ang + Mathf.Pi - 1f, ang + Mathf.Pi + 1f, 10, new Color("#c8d0dc"), 2f, true);
            }
        }
        if (info.Purpose == ShipPurpose.Farm)
        {
            var line = EdgeLine(x0, x1, true, 0f);
            for (int i = line.Count / 5; i < line.Count; i += System.Math.Max(4, line.Count / 6))
            {
                var c = line[i];
                if (!NearEdge(c, true)) continue;
                ci.Circle(c, T * 0.8f, new Color(0.45f, 0.85f, 0.5f, 0.18f + 0.05f * Mathf.Sin(t + i)), true, -1f, true);
                ci.Arc(c, T * 0.8f, Mathf.Pi, Mathf.Tau, 16, new Color("#bfe8ff").WithAlpha(0.7f), 1.5f, true);
                for (int k = -1; k <= 1; k++) ci.Poly(new[] { c + new Vector2(k * 7f, 0f), c + new Vector2(k * 7f - 3f, -8f), c + new Vector2(k * 7f + 3f, -8f) }, new Color("#5fae4e"));
            }
        }
    }
}
