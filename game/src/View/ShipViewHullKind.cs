using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v18.8 배 모양 · 용도마다 선체 바깥이 다르다 (읽기만):
//  뼈대 — 바퀴형(천천히 도는 테두리 · 바퀴살 · 굴대) · 고리형(선체를 감싸 도는 관) · 척추형(배 밑의 격자 용골) ·
//         화물선형(위아래로 쌓은 색색 컨테이너 · 골판 · 고정 쇠) · 누더기형(색이 다른 덧댄 판 · 리벳 · 용접 자국).
//  용도 — 예인선(뱃머리 집게 팔 · 감긴 밧줄) · 구조선(붉은 사선 띠 · 도는 경광등) · 급유선(배 밑 둥근 탱크 · 서리) ·
//         농업선(위쪽 유리 온실 돔 · 잎) · 채굴선(뱃머리 굴착 머리) · 연구선(작은 접시 줄) · 병원선(흰 띠의 붉은 십자).
public partial class ShipView
{
    private Ship? _kindFor;
    private int _kindRooms = -1;
    private Rect2 _kindBox;

    private Rect2 KindBox()
    {
        var ship = _world.Ship;
        if (_kindFor == ship && _kindRooms == ship.Rooms.Count) return _kindBox;
        _kindFor = ship;
        _kindRooms = ship.Rooms.Count;
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var r in ship.Rooms)
        {
            if (r.Detached) continue;
            foreach (var c in r.Cells)
            {
                x0 = Mathf.Min(x0, c.X * T); y0 = Mathf.Min(y0, c.Y * T);
                x1 = Mathf.Max(x1, (c.X + 1) * T); y1 = Mathf.Max(y1, (c.Y + 1) * T);
            }
        }
        _kindBox = x0 > x1 ? new Rect2() : new Rect2(x0 - T, y0 - T, x1 - x0 + 2 * T, y1 - y0 + 2 * T);
        return _kindBox;
    }

    private void PaintHullKind(CanvasItem ci)
    {
        if (_world.Origin.Info is not ShipInfo info) return;
        var b = KindBox();
        if (b.Size.X <= 0f) return;
        float t = _time;
        var metal = new Color("#6f7888");
        var dark = new Color("#3c434f");
        if (Shaped(info.Frame)) // v19 모양 있는 배: 실제 선체 가장자리를 따라 그린다 (상자 기준 장식은 뱃머리 것만)
        {
            PaintShapeKind(ci, info);
            if (info.Purpose is not (ShipPurpose.Mining or ShipPurpose.Tug)) return;
        }
        switch (info.Frame)
        {
            case ShipFrame.Wheel:
            {
                var hub = b.GetCenter();
                float rad = Mathf.Max(b.Size.X, b.Size.Y) * 0.58f;
                float spin = t * 0.04f;
                ci.Arc(hub, rad, 0f, Mathf.Tau, 96, metal.WithAlpha(0.55f), 7f, true);
                ci.Arc(hub, rad - 6f, 0f, Mathf.Tau, 96, dark.WithAlpha(0.5f), 2f, true);
                for (int i = 0; i < 12; i++) // 테두리 마디 (도는 게 보인다)
                {
                    var a = Vector2.FromAngle(spin + i * Mathf.Tau / 12f);
                    ci.DrawLine(hub + a * (rad - 4f), hub + a * (rad + 4f), new Color("#9aa3b2"), 2f, true);
                    if (i % 3 == 0) ci.Circle(hub + a * (rad + 5f), 2.2f, Mathf.PosMod(t + i, 2f) < 0.15f ? new Color("#7cf0a0") : dark, true, -1f, true);
                }
                for (int i = 0; i < 4; i++) // 바퀴살 (선체 뒤로 비친다)
                {
                    var a = Vector2.FromAngle(spin + Mathf.Pi / 4f + i * Mathf.Pi / 2f);
                    ci.DrawLine(hub + a * T * 1.5f, hub + a * (rad - 7f), metal.WithAlpha(0.35f), 5f, true);
                }
                ci.Circle(hub, T * 1.2f, dark.WithAlpha(0.4f), true, -1f, true); // 굴대
                break;
            }
            case ShipFrame.Ring:
            {
                var r = b.Grow(T * 0.6f);
                var pts = new[] { r.Position, r.Position + new Vector2(r.Size.X, 0f), r.End, r.Position + new Vector2(0f, r.Size.Y), r.Position };
                for (int i = 0; i < 4; i++)
                {
                    var a = pts[i];
                    var z = pts[i + 1];
                    float len = a.DistanceTo(z);
                    for (float d = 0f; d < len; d += 14f) ci.DrawLine(a.Lerp(z, d / len), a.Lerp(z, Mathf.Min(1f, (d + 9f) / len)), metal.WithAlpha(0.6f), 3f, true);
                }
                break;
            }
            case ShipFrame.Spine:
            {
                float y = b.End.Y + T * 0.6f;
                ci.DrawLine(new Vector2(b.Position.X, y), new Vector2(b.End.X, y), metal, 3f, true);
                ci.DrawLine(new Vector2(b.Position.X, y + 10f), new Vector2(b.End.X, y + 10f), metal, 3f, true);
                for (float x = b.Position.X; x < b.End.X - 10f; x += 20f)
                {
                    ci.DrawLine(new Vector2(x, y), new Vector2(x + 10f, y + 10f), dark, 1.5f, true);
                    ci.DrawLine(new Vector2(x + 10f, y + 10f), new Vector2(x + 20f, y), dark, 1.5f, true);
                }
                break;
            }
            case ShipFrame.Cargo:
            {
                Color[] box = { new("#9c3f2e"), new("#2f7a7a"), new("#c49a2c"), new("#4f6aa0") };
                foreach (float y in new[] { b.Position.Y - T * 1.1f, b.End.Y + T * 0.1f })
                {
                    int i = 0;
                    for (float x = b.Position.X + T * 3f; x < b.End.X - T * 3f; x += T * 2.2f, i++)
                    {
                        var r = new Rect2(x, y, T * 2f, T);
                        ci.Box(r, box[(i * 7 + (int)(y / T)) % box.Length]);
                        for (float gx = x + 4f; gx < x + T * 2f - 2f; gx += 5f) ci.DrawLine(new Vector2(gx, y + 2f), new Vector2(gx, y + T - 2f), new Color(0, 0, 0, 0.22f), 1f); // 골판
                        ci.Box(r, dark, false, 1.2f);
                        ci.Circle(new Vector2(x + 3f, y + T * 0.5f), 1.6f, new Color("#d8d8d8"), true, -1f, true); // 고정 쇠
                    }
                }
                break;
            }
            case ShipFrame.Patchwork:
            {
                Color[] plate = { new("#7d6a55"), new("#56677a"), new("#8a8a6a"), new("#6b4f4f") };
                for (int i = 0; i < 9; i++)
                {
                    uint h = (uint)(i * 2654435761u + (uint)_world.Seed);
                    bool top = i % 2 == 0;
                    float x = b.Position.X + T * 2f + (h % 1000) / 1000f * (b.Size.X - T * 6f);
                    float w = T * (1.2f + h / 1000 % 3 * 0.6f);
                    var r = new Rect2(x, top ? b.Position.Y + T * 0.15f : b.End.Y - T * 0.85f, w, T * 0.7f);
                    ci.Box(r, plate[h / 7 % plate.Length].WithAlpha(0.9f));
                    for (float rx = r.Position.X + 3f; rx < r.End.X - 1f; rx += 6f) // 리벳
                    {
                        ci.Circle(new Vector2(rx, r.Position.Y + 2.5f), 0.9f, new Color("#c9c1b0"), true, -1f, true);
                        ci.Circle(new Vector2(rx, r.End.Y - 2.5f), 0.9f, new Color("#c9c1b0"), true, -1f, true);
                    }
                    var seam = new Vector2[6];
                    for (int k = 0; k < 6; k++) seam[k] = new Vector2(r.End.X + (k % 2) * 2f, r.Position.Y + k * r.Size.Y / 5f); // 용접 자국
                    ci.Polyline(seam, new Color("#d08a3a").WithAlpha(0.8f), 1f, true);
                }
                break;
            }
        }
        var nose = new Vector2(b.End.X, b.GetCenter().Y);
        switch (info.Purpose)
        {
            case ShipPurpose.Tug:
            {
                // 뱃머리 집게 팔 둘 · 감긴 밧줄 · 늘어진 줄
                for (int k = -1; k <= 1; k += 2)
                {
                    var a = nose + new Vector2(0f, k * T * 0.8f);
                    var m = a + new Vector2(T * 1.4f, k * T * 0.5f);
                    var tip = m + new Vector2(T * 0.9f, -k * T * 0.7f);
                    ci.Polyline(new[] { a, m, tip }, new Color("#d9a31e"), 5f, true);
                    ci.Circle(m, 3.5f, dark, true, -1f, true);
                }
                var drum = nose + new Vector2(-T * 0.8f, -b.Size.Y * 0.5f + T * 0.3f);
                ci.Circle(drum, T * 0.45f, new Color("#5b4632"), true, -1f, true);
                for (int i = 1; i <= 3; i++) ci.Arc(drum, T * 0.12f * i, 0f, Mathf.Tau, 14, new Color("#c9b48a"), 1f, true);
                float sag = Mathf.Sin(t * 0.8f) * 6f;
                ci.Polyline(new[] { drum, drum + new Vector2(T * 1.5f, T * 0.8f + sag), nose + new Vector2(T * 2.3f, 0f) }, new Color("#c9b48a"), 1.4f, true);
                break;
            }
            case ShipPurpose.Rescue:
            {
                // 위 선체에 붉은 사선 띠 · 도는 경광등
                for (float x = b.Position.X + T * 3f; x < b.End.X - T * 2f; x += 12f)
                    ci.DrawLine(new Vector2(x, b.Position.Y + T * 0.95f), new Vector2(x + 7f, b.Position.Y + T * 0.15f), new Color("#d23c3c").WithAlpha(0.85f), 4f, true);
                var bea = new Vector2(b.GetCenter().X, b.Position.Y - 4f);
                ci.Circle(bea, 4f, new Color("#ff9a3c"), true, -1f, true);
                float ang = t * 4f;
                ci.Poly(new[] { bea, bea + Vector2.FromAngle(ang - 0.3f) * 46f, bea + Vector2.FromAngle(ang + 0.3f) * 46f }, new Color(1f, 0.6f, 0.2f, 0.18f));
                break;
            }
            case ShipPurpose.Tanker:
            {
                // 배 밑 둥근 탱크 (띠 · 서리 반짝임)
                int n = Mathf.Max(2, (int)(b.Size.X / (T * 5f)));
                for (int i = 0; i < n; i++)
                {
                    var c = new Vector2(b.Position.X + T * 3f + i * (b.Size.X - T * 6f) / Mathf.Max(1, n - 1), b.End.Y + T * 0.9f);
                    ci.Circle(c, T * 0.95f, new Color("#9fb0bf"), true, -1f, true);
                    ci.Arc(c, T * 0.95f, 0f, Mathf.Tau, 24, dark, 1.5f, true);
                    ci.DrawLine(c + new Vector2(-T * 0.9f, 0f), c + new Vector2(T * 0.9f, 0f), dark.WithAlpha(0.6f), 1.5f);
                    ci.Arc(c, T * 0.6f, -2.4f, -1.6f, 8, new Color(1f, 1f, 1f, 0.6f + 0.3f * Mathf.Sin(t * 1.5f + i)), 1.5f, true);
                }
                break;
            }
            case ShipPurpose.Farm:
            {
                // 위쪽 유리 온실 돔 · 안의 잎 · 초록 빛
                int n = Mathf.Max(2, (int)(b.Size.X / (T * 6f)));
                for (int i = 0; i < n; i++)
                {
                    var c = new Vector2(b.Position.X + T * 3.5f + i * (b.Size.X - T * 7f) / Mathf.Max(1, n - 1), b.Position.Y + T * 0.2f);
                    ci.Circle(c, T * 1.1f, new Color(0.45f, 0.85f, 0.5f, 0.18f + 0.05f * Mathf.Sin(t + i)), true, -1f, true);
                    ci.Arc(c, T * 1.1f, Mathf.Pi, Mathf.Tau, 18, new Color("#bfe8ff").WithAlpha(0.7f), 1.5f, true);
                    for (int k = -1; k <= 1; k++) ci.Poly(new[] { c + new Vector2(k * 9f, 0f), c + new Vector2(k * 9f - 4f, -10f), c + new Vector2(k * 9f + 4f, -10f) }, new Color("#5fae4e"));
                    ci.DrawLine(c + new Vector2(-T * 1.1f, 0f), c + new Vector2(T * 1.1f, 0f), metal, 2f);
                }
                break;
            }
            case ShipPurpose.Mining:
            {
                var tip = nose + new Vector2(T * 1.8f, 0f);
                ci.Poly(new[] { nose + new Vector2(0f, -T * 0.7f), tip, nose + new Vector2(0f, T * 0.7f) }, new Color("#8a7a5c"));
                for (int i = 0; i < 4; i++)
                {
                    float u = Mathf.PosMod(i * 0.25f + t * 0.5f, 1f);
                    var p = nose.Lerp(tip, u);
                    ci.DrawLine(p + new Vector2(0f, -T * 0.7f * (1f - u)), p + new Vector2(4f, T * 0.7f * (1f - u)), dark, 1.5f, true);
                }
                break;
            }
            case ShipPurpose.Research:
                for (int i = 0; i < 3; i++)
                {
                    var c = new Vector2(b.Position.X + b.Size.X * (0.35f + i * 0.15f), b.Position.Y - 2f);
                    float ang = -Mathf.Pi / 2f + Mathf.Sin(t * 0.3f + i) * 0.4f;
                    ci.DrawLine(c, c + Vector2.FromAngle(ang) * 9f, metal, 2f, true);
                    ci.Arc(c + Vector2.FromAngle(ang) * 12f, 6f, ang + Mathf.Pi - 1f, ang + Mathf.Pi + 1f, 10, new Color("#c8d0dc"), 2f, true);
                }
                break;
            case ShipPurpose.Hospital:
                for (float x = b.Position.X + T * 4f; x < b.End.X - T * 3f; x += T * 6f)
                {
                    var c = new Vector2(x, b.Position.Y + T * 0.55f);
                    ci.Box(new Rect2(c - new Vector2(10f, 8f), new Vector2(20f, 16f)), new Color("#e9edf2").WithAlpha(0.85f));
                    ci.Box(new Rect2(c - new Vector2(2.5f, 6f), new Vector2(5f, 12f)), new Color("#d23c3c"));
                    ci.Box(new Rect2(c - new Vector2(6f, 2.5f), new Vector2(12f, 5f)), new Color("#d23c3c"));
                }
                break;
        }
    }
}
