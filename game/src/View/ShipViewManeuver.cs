using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v17.0 기동 · 충격과 고정 그림 (읽기만 한다):
//  바닥 층 — 선반마다 다른 걸쇠(쇠막대 · 고무 끈 · 공구 집게 · 냉장고 손잡이 · 상자 끈, 헐거워지면 비뚤어진다) · 카트를 벽 고리에 묶은 끈 ·
//   화구 위 냄비(데우는 김 · 집게 두 짝과 나비 너트) · 묶어 둔 물건의 띠와 고리 · 떨어진 물건(통조림 · 사발 · 접시 · 책 · 병 · 상자 · 공구 · 약통 —
//   깨지면 조각) · 쏟은 자국(채소 건더기가 뜬 국물 · 물 · 흩어진 알약) · 굴러간 카트 바퀴 자국 · 넘어진 히터 · 벽에 기대 둔 빗자루와 쓰레받기.
//  사람 위 층 — 침대 끈(버클) · 벽 손잡이를 쥔 손과 팔 · 컵을 쥔 손 · 넘어진 사람(먼지 · 미끄러진 자국 · 아픈 별 · 떨어진 이불) ·
//   빗자루질(쓸리는 솔) · 상자를 안고 옮기는 사람.
public partial class ShipView
{
    private void PaintManeuver(CanvasItem ci)
    {
        var w = _world;
        var ms = w.Maneuver;
        bool close = Zoom > 0.75f;
        long now = w.Tick;

        // 쏟은 자국 (바닥 맨 아래)
        foreach (var s in ms.Splashes)
        {
            float age = (now - s.Tick) / (float)SimTime.Hours(1);
            ManeuverArt.Splash(ci, CellRect(s.At).GetCenter(), new Vector2(s.Dir.X, s.Dir.Y), s.Kind, s.Size, Mathf.Clamp(1f - age * 0.15f, 0.4f, 1f), s.Tick, close);
        }
        // 굴러간 카트 바퀴 자국
        foreach (var (id, from) in ms.Rolled)
            if (w.Portable.Devices.FirstOrDefault(d => d.Id == id) is PortableDevice cart && cart.Placed)
                ManeuverArt.Skid(ci, CellRect(from).GetCenter(), ToPx(cart.Position));
        // 선반 걸쇠
        foreach (var f in w.Ship.Furniture)
        {
            if (!ManeuverSystem.Shelfish(f.Type) || f.Room.Detached) continue;
            bool latched = ms.Latched.Contains(f.Id);
            if (!latched && !close) continue;
            ManeuverArt.Latch(ci, FurnitureRect(f), f.Type, latched, ms.Loose.Contains(f.Id), close);
        }
        // 카트 끈 · 벽 고리
        foreach (var d in w.Portable.Devices)
        {
            if (!d.Placed) continue;
            if (d.Kind == PortableKind.Cart && ms.Strapped.Contains(d.Id))
                ManeuverArt.CartStrap(ci, ToPx(d.Position), WallToward(d.At), close);
            if (d.Kind == PortableKind.Heater && ms.Tipped.ContainsKey(d.Id))
                ManeuverArt.TippedHeater(ci, ToPx(d.Position), d.Id, _time);
            if (d.Kind == PortableKind.Heater && ms.HeaterOffBy.ContainsKey(d.Id) && close)
                ManeuverArt.SwitchOff(ci, ToPx(d.Position));
        }
        // 화구 위 냄비 (데우는 중 · 집게)
        foreach (var st in w.Ship.FurnitureOf(FurnitureType.Stove))
        {
            bool warm = ms.Warming.Any(x => x.Stove == st.Id && !x.Stopped);
            bool clamp = ms.Clamped.Contains(st.Id);
            if (!warm && !clamp) continue;
            ManeuverArt.Pot(ci, FurnitureRect(st).GetCenter(), warm && !clamp, clamp, _time, close);
        }
        // 묶어 둔 물건
        foreach (var id in ms.Tied)
            if (w.Matter.Get(id) is Article a && a.Fixed && a.CarriedBy < 0)
                ManeuverArt.TieBand(ci, ToPx(a.At.Center + a.Off), a.Angle, ms.Loose.Contains(-1 - a.Id));
        // 떨어진 물건
        foreach (var t in ms.Fallen)
        {
            if (t.ClaimedBy >= 0 && w.Crew.FirstOrDefault(c => c.Id == t.ClaimedBy) is CrewMember cm && cm.Job?.Label == "떨어진 것 줍기" && (cm.Position - t.At.Center).LengthSquared() < 0.3f) continue;
            ManeuverArt.Fallen(ci, CellRect(t.At).GetCenter() + new Vector2(t.Off.X, t.Off.Y) * T, t.Angle, t.Kind, t.Broken, t.Id, close);
        }
        // 빗자루 (벽에 기대 둔 것)
        if (ms.BroomBy < 0 && ms.BroomHome is Cell bh)
            ManeuverArt.BroomRest(ci, CellRect(bh).GetCenter(), WallToward(bh), close);
    }

    private void PaintManeuverOver(CanvasItem ci)
    {
        var w = _world;
        var ms = w.Maneuver;
        bool close = Zoom > 0.75f;
        long now = w.Tick;
        // 침대 끈
        foreach (var id in ms.Bunks)
            if (w.Ship.Furniture.FirstOrDefault(f => f.Id == id) is Furniture bed) ManeuverArt.BunkStraps(ci, FurnitureRect(bed), close);
        // 벽 손잡이를 쥔 사람
        foreach (var (cid, g) in ms.Grips)
        {
            if (w.Crew.FirstOrDefault(c => c.Id == cid) is not CrewMember c || c.Dead) continue;
            var a = CellRect(g.at).GetCenter();
            var wallC = CellRect(g.wall).GetCenter();
            var edge = a + (wallC - a) * 0.5f;
            ManeuverArt.Handhold(ci, edge, (wallC - a).Normalized(), CrewPx(c), Palette.Crew(c.Id), ms.Active && ms.Pending!.Hit, _time);
        }
        // 컵을 쥔 손
        foreach (var id in ms.HeldCups)
            if (w.Info.OnTable.TryGetValue(id, out var tc)) ManeuverArt.CupHand(ci, CellRect(tc.Spot).GetCenter(), _time);
        // 넘어진 사람
        foreach (var t in ms.Tumbles)
        {
            if (t.Up && now - t.Tick > SimTime.Minutes(8)) continue;
            var c = w.Crew.FirstOrDefault(x => x.Id == t.Crew);
            var at = c != null && !t.Up ? CrewPx(c) : ToPx(t.At);
            float age = (now - t.Tick) / (float)SimTime.Minutes(8);
            ManeuverArt.Tumble(ci, at, new Vector2(t.Dir.X, t.Dir.Y), t.Hurt, t.FromBed, Mathf.Clamp(age, 0f, 1f), t.Up, _time);
        }
        // 빗자루질 · 짐 옮기기
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Job?.Activity is not ManeuverActivity) continue;
            if (c.Job.Label == "빗자루질" && ms.BroomBy == c.Id) ManeuverArt.Sweeping(ci, CrewPx(c), c.Pose == Pose.Working, _time + c.Id);
            else if (c.Job.Label == "짐 옮기기" && c.Pose == Pose.Walking) ManeuverArt.CarryCrate(ci, CrewPx(c), c.Id);
        }
    }

    /// <summary>그 칸에서 가장 가까운 벽 쪽 (화면 방향 · 없으면 위).</summary>
    private Vector2 WallToward(Cell at)
    {
        foreach (var d in Cell.Dirs4)
            if (_world.Ship.Grid.Kind(at + d) == TileKind.Wall) return new Vector2(d.X, d.Y);
        foreach (var d in Cell.Dirs4)
            if (_world.Ship.Grid.Kind(at + d + d) == TileKind.Wall) return new Vector2(d.X, d.Y) * 2f;
        return new Vector2(0, -1);
    }
}

internal static class ManeuverArt
{
    private const float T = ShipView.T;
    private static readonly Color Steel = new(0.62f, 0.66f, 0.70f), SteelDark = new(0.32f, 0.35f, 0.39f), Hazard = new(0.95f, 0.78f, 0.15f);
    private static readonly Color Web = new(0.22f, 0.40f, 0.62f), WebEdge = new(0.12f, 0.22f, 0.36f), Buckle = new(0.78f, 0.80f, 0.82f);
    private static readonly Color Bungee = new(0.85f, 0.22f, 0.20f), Skin = new(0.93f, 0.76f, 0.62f);

    private static Vector2 R(Vector2 v, float a) => v.Rotated(a);
    private static void Poly(CanvasItem ci, Vector2 at, float ang, Color c, params Vector2[] pts)
    {
        var a = new Vector2[pts.Length];
        for (int i = 0; i < pts.Length; i++) a[i] = at + R(pts[i], ang);
        ci.Poly(a, c);
    }
    private static float Hash(int n) { uint x = (uint)n * 2654435761u; x ^= x >> 13; x *= 0x5bd1e995; x ^= x >> 15; return (x & 0xffff) / 65535f; }

    // ── 걸쇠: 선반마다 다르다 ──
    public static void Latch(CanvasItem ci, Rect2 r, FurnitureType type, bool on, bool loose, bool close)
    {
        var c = r.GetCenter();
        float wdt = r.Size.X, hgt = r.Size.Y;
        if (!on)
        {
            // 풀린 걸쇠: 옆에 매달린 고리만 (가까이서)
            ci.Arc(new Vector2(r.End.X - 4f, c.Y), 2.6f, 0f, Mathf.Tau, 10, SteelDark, 1.2f, true);
            return;
        }
        float tilt = loose ? 0.18f : 0f;
        switch (type)
        {
            case FurnitureType.Bookshelf: // 고무 끈 지그재그 · 양 끝 갈고리
            {
                int n = Mathf.Max(3, (int)(wdt / 9f));
                var pts = new Vector2[n + 1];
                for (int i = 0; i <= n; i++) pts[i] = new Vector2(r.Position.X + 3f + (wdt - 6f) * i / n, c.Y + (i % 2 == 0 ? -hgt * 0.18f : hgt * 0.18f) + (loose ? i * 1.5f : 0f));
                ci.Polyline(pts, Bungee, 2f, true);
                ci.Arc(pts[0], 2.2f, 0.5f, 5.5f, 8, SteelDark, 1.4f, true);
                ci.Arc(pts[^1], 2.2f, -2.6f, 2.6f, 8, SteelDark, 1.4f, true);
                break;
            }
            case FurnitureType.ToolWall: // 공구마다 작은 집게
            {
                int n = Mathf.Max(2, (int)(wdt / 11f));
                for (int i = 0; i < n; i++)
                {
                    var p = new Vector2(r.Position.X + 6f + (wdt - 12f) * i / Mathf.Max(1, n - 1), c.Y + (i % 2 == 0 ? -3f : 3f));
                    ci.Box(new Rect2(p - new Vector2(2.4f, 1.6f), new Vector2(4.8f, 3.2f)), Hazard);
                    ci.DrawLine(p + new Vector2(-2.4f, 1.6f), p + new Vector2(-1f, 4f), SteelDark, 1f, true);
                    ci.DrawLine(p + new Vector2(2.4f, 1.6f), p + new Vector2(1f, 4f), SteelDark, 1f, true);
                }
                break;
            }
            case FurnitureType.Fridge: // 문 손잡이 걸쇠 (레버를 내렸다)
            {
                var p = new Vector2(r.End.X - 5f, c.Y);
                ci.Box(new Rect2(p - new Vector2(2f, 6f), new Vector2(4f, 12f)), SteelDark);
                ci.DrawLine(p, p + R(new Vector2(0f, 7f), loose ? -0.6f : 0f), Hazard, 2.4f, true);
                ci.Circle(p, 1.6f, Buckle);
                break;
            }
            case FurnitureType.SupplyCache: // 상자 끈 X자 · 버클
            {
                ci.DrawLine(r.Position + new Vector2(3, 3), r.End - new Vector2(3, 3), Web, 3f, true);
                ci.DrawLine(new Vector2(r.End.X - 3, r.Position.Y + 3), new Vector2(r.Position.X + 3, r.End.Y - 3), Web, 3f, true);
                ci.Box(new Rect2(c - new Vector2(3f, 2.5f), new Vector2(6f, 5f)), Buckle);
                ci.Box(new Rect2(c - new Vector2(3f, 2.5f), new Vector2(6f, 5f)), SteelDark, false, 1f);
                break;
            }
            default: // 선반: 앞을 가로지르는 쇠막대 · 양 끝 브래킷 · 노랑-검정 걸쇠 탭
            {
                var a = new Vector2(r.Position.X + 3f, c.Y + hgt * 0.22f);
                var b = new Vector2(r.End.X - 3f, c.Y + hgt * 0.22f + (loose ? wdt * tilt : 0f));
                ci.DrawLine(a, b, SteelDark, 3.2f, true);
                ci.DrawLine(a, b, Steel, 1.6f, true);
                ci.Box(new Rect2(a - new Vector2(2f, 3f), new Vector2(4f, 6f)), SteelDark);
                if (!loose) ci.Box(new Rect2(b - new Vector2(2f, 3f), new Vector2(4f, 6f)), SteelDark);
                var tab = a.Lerp(b, 0.5f);
                ci.Box(new Rect2(tab - new Vector2(3.5f, 2.5f), new Vector2(7f, 5f)), Hazard);
                if (close) { ci.DrawLine(tab + new Vector2(-3.5f, 2.5f), tab + new Vector2(-0.5f, -2.5f), Colors.Black, 1f); ci.DrawLine(tab + new Vector2(0.5f, 2.5f), tab + new Vector2(3.5f, -2.5f), Colors.Black, 1f); }
                if (close) { ci.Circle(a, 0.9f, Buckle); if (!loose) ci.Circle(b, 0.9f, Buckle); }
                break;
            }
        }
    }

    // ── 카트 끈: 짐 위로 두 줄 · 벽 고리까지 ──
    public static void CartStrap(CanvasItem ci, Vector2 at, Vector2 wall, bool close)
    {
        var half = new Vector2(T * 0.32f, T * 0.22f);
        ci.DrawLine(at + new Vector2(-half.X, -half.Y * 0.4f), at + new Vector2(half.X, -half.Y * 0.4f), Web, 2.6f, true);
        ci.DrawLine(at + new Vector2(-half.X, half.Y * 0.4f), at + new Vector2(half.X, half.Y * 0.4f), Web, 2.6f, true);
        ci.Box(new Rect2(at + new Vector2(half.X * 0.3f, -half.Y * 0.4f - 2f), new Vector2(5f, 4f)), Buckle);
        var ring = at + wall * T * 0.75f;
        ci.DrawLine(at + wall * T * 0.25f, ring, WebEdge, 2f, true);
        ci.Arc(ring, 3f, 0f, Mathf.Tau, 12, Steel, 1.6f, true);
        if (close) ci.Circle(ring - wall * 1.5f, 1.4f, SteelDark);
    }

    public static void Skid(CanvasItem ci, Vector2 from, Vector2 to)
    {
        var d = to - from;
        if (d.LengthSquared() < 4f) return;
        var n = d.Normalized().Orthogonal() * 5f;
        var col = new Color(0.1f, 0.1f, 0.1f, 0.28f);
        ci.DrawLine(from + n, to + n, col, 1.6f, true);
        ci.DrawLine(from - n, to - n, col, 1.6f, true);
    }

    public static void TippedHeater(CanvasItem ci, Vector2 at, int id, float time)
    {
        // 옆으로 누운 몸통 · 바닥을 향한 열선의 붉은 빛 · 그을린 부채꼴
        float ang = Hash(id) > 0.5f ? 1.45f : -1.45f;
        ci.Circle(at + new Vector2(0, 5f), 9f, new Color(0.1f, 0.07f, 0.05f, 0.35f));
        Poly(ci, at, ang, new Color(0.55f, 0.52f, 0.48f), new Vector2(-6f, -8f), new Vector2(6f, -8f), new Vector2(6f, 8f), new Vector2(-6f, 8f));
        for (int i = -1; i <= 1; i++) ci.DrawLine(at + R(new Vector2(-4f, i * 4f), ang), at + R(new Vector2(4f, i * 4f), ang), new Color(0.25f, 0.22f, 0.2f), 1f);
        ci.DrawLine(at + R(new Vector2(-6f, 8f), ang), at + R(new Vector2(6f, 8f), ang), new Color(1f, 0.35f, 0.1f, 0.4f + 0.2f * Mathf.Sin(time * 3f)), 1.6f);
    }

    public static void SwitchOff(CanvasItem ci, Vector2 at)
    {
        var p = at + new Vector2(7f, -8f);
        ci.Box(new Rect2(p, new Vector2(4f, 6f)), new Color(0.15f, 0.15f, 0.15f));
        ci.Box(new Rect2(p + new Vector2(0.8f, 3.4f), new Vector2(2.4f, 2f)), new Color(0.75f, 0.75f, 0.75f));
    }

    // ── 화구 위 냄비 ──
    public static void Pot(CanvasItem ci, Vector2 at, bool steaming, bool clamped, float time, bool close)
    {
        ci.Circle(at, 9.5f, new Color(0.24f, 0.25f, 0.27f));
        ci.Circle(at, 7.6f, new Color(0.78f, 0.45f, 0.18f)); // 국물
        for (int i = 0; i < 5; i++) ci.Circle(at + new Vector2(Mathf.Cos(i * 1.7f) * 4f, Mathf.Sin(i * 2.3f) * 4f), 1.2f, i % 2 == 0 ? new Color(0.35f, 0.6f, 0.25f) : new Color(0.95f, 0.6f, 0.2f));
        ci.Arc(at, 9.5f, 0f, Mathf.Tau, 24, new Color(0.55f, 0.57f, 0.6f), 1.4f, true);
        ci.DrawLine(at + new Vector2(-9.5f, 0f), at + new Vector2(-13f, 0f), SteelDark, 2.4f); // 손잡이
        ci.DrawLine(at + new Vector2(9.5f, 0f), at + new Vector2(13f, 0f), SteelDark, 2.4f);
        if (steaming)
            for (int i = 0; i < 3; i++)
            {
                float ph = (time * 0.7f + i * 0.33f) % 1f;
                var b = at + new Vector2(-5f + i * 5f, -8f - ph * 12f);
                ci.Arc(b, 2.2f + ph * 2f, 0f, Mathf.Pi, 6, new Color(1, 1, 1, 0.35f * (1f - ph)), 1.2f, true);
            }
        if (!clamped) return;
        // 집게 두 짝 (냄비 테를 물고 화구 틀에 걸린다) · 나비 너트
        foreach (var s in new[] { -1f, 1f })
        {
            var p = at + new Vector2(0f, s * 10.5f);
            ci.Arc(p, 3.6f, s > 0 ? Mathf.Pi : 0f, s > 0 ? Mathf.Tau : Mathf.Pi, 8, Hazard, 2.2f, true);
            ci.DrawLine(p, p + new Vector2(0f, s * 4.5f), SteelDark, 2f);
            if (close) { ci.DrawLine(p + new Vector2(-2.5f, s * 4.5f), p + new Vector2(2.5f, s * 4.5f), Steel, 1.6f); ci.Circle(p + new Vector2(0, s * 4.5f), 1f, SteelDark); }
        }
    }

    public static void TieBand(CanvasItem ci, Vector2 at, float ang, bool loose)
    {
        Poly(ci, at, ang + (loose ? 0.4f : 0f), Web, new Vector2(-8f, -1.6f), new Vector2(8f, -1.6f), new Vector2(8f, 1.6f), new Vector2(-8f, 1.6f));
        var hook = at + R(new Vector2(8.5f, 0f), ang);
        ci.Arc(hook, 2.2f, -1.2f, 4.2f, 8, Steel, 1.3f, true); // 고리
    }

    // ── 떨어진 물건 (종류마다) ──
    public static void Fallen(CanvasItem ci, Vector2 at, float ang, FallenKind k, bool broken, int id, bool close)
    {
        float h = Hash(id);
        ci.Circle(at + new Vector2(1.5f, 2f), 5f, new Color(0, 0, 0, 0.18f));
        switch (k)
        {
            case FallenKind.Can: // 옆으로 누운 통조림 · 라벨 띠 · 테
            {
                var lab = Color.FromHsv(h, 0.6f, 0.85f);
                Poly(ci, at, ang, Steel, new Vector2(-6f, -3.5f), new Vector2(6f, -3.5f), new Vector2(6f, 3.5f), new Vector2(-6f, 3.5f));
                Poly(ci, at, ang, lab, new Vector2(-3f, -3.5f), new Vector2(3f, -3.5f), new Vector2(3f, 3.5f), new Vector2(-3f, 3.5f));
                ci.DrawLine(at + R(new Vector2(6f, -3.5f), ang), at + R(new Vector2(6f, 3.5f), ang), SteelDark, 1.4f);
                if (close) ci.DrawLine(at + R(new Vector2(-6f, -3.5f), ang), at + R(new Vector2(-6f, 3.5f), ang), SteelDark, 1.4f);
                break;
            }
            case FallenKind.Bowl:
            case FallenKind.Plate:
            {
                var white = new Color(0.93f, 0.92f, 0.88f);
                var rim = k == FallenKind.Bowl ? new Color(0.25f, 0.42f, 0.75f) : new Color(0.65f, 0.55f, 0.35f);
                if (!broken)
                {
                    if (k == FallenKind.Bowl) { ci.Circle(at, 5.5f, white); ci.Arc(at, 5.5f, 0f, Mathf.Tau, 16, rim, 1.4f, true); ci.Circle(at, 2.5f, new Color(0.85f, 0.84f, 0.8f)); }
                    else { ci.Circle(at, 6.5f, white); ci.Arc(at, 4.6f, 0f, Mathf.Tau, 16, rim, 0.9f, true); }
                    break;
                }
                for (int i = 0; i < 4; i++)
                {
                    float a0 = ang + i * 1.6f + h;
                    var p = at + R(new Vector2(3f + (i % 2) * 3f, 0f), a0);
                    Poly(ci, p, a0, white, new Vector2(-2.6f, -1.6f), new Vector2(2.8f, -0.6f), new Vector2(0.4f, 2.2f));
                    ci.DrawLine(p + R(new Vector2(-2.6f, -1.6f), a0), p + R(new Vector2(2.8f, -0.6f), a0), rim, 0.9f);
                }
                break;
            }
            case FallenKind.Book: // 펼쳐져 엎어진 책 (책등 색 · 쪽 줄)
            {
                var cover = Color.FromHsv(0.05f + h * 0.6f, 0.55f, 0.6f);
                Poly(ci, at, ang, cover, new Vector2(-7f, -5f), new Vector2(7f, -5f), new Vector2(7f, 5f), new Vector2(-7f, 5f));
                Poly(ci, at, ang, new Color(0.96f, 0.94f, 0.86f), new Vector2(-6f, -4f), new Vector2(-0.6f, -4f), new Vector2(-0.6f, 4f), new Vector2(-6f, 4f));
                ci.DrawLine(at + R(new Vector2(0f, -5f), ang), at + R(new Vector2(0f, 5f), ang), cover.Darkened(0.4f), 1.6f);
                if (close) for (int i = -2; i <= 2; i++) ci.DrawLine(at + R(new Vector2(-5.4f, i * 1.5f), ang), at + R(new Vector2(-1.4f, i * 1.5f), ang), new Color(0.5f, 0.5f, 0.5f, 0.6f), 0.6f);
                break;
            }
            case FallenKind.Bottle:
            {
                var glass = new Color(0.25f, 0.55f, 0.35f, 0.9f);
                if (!broken)
                {
                    Poly(ci, at, ang, glass, new Vector2(-6f, -3f), new Vector2(2f, -3f), new Vector2(4f, -1.4f), new Vector2(7f, -1.4f), new Vector2(7f, 1.4f), new Vector2(4f, 1.4f), new Vector2(2f, 3f), new Vector2(-6f, 3f));
                    ci.DrawLine(at + R(new Vector2(-4.5f, -1.6f), ang), at + R(new Vector2(0.5f, -1.6f), ang), new Color(1, 1, 1, 0.45f), 1f); // 빛
                    break;
                }
                Poly(ci, at, ang, glass, new Vector2(3f, -1.4f), new Vector2(7f, -1.4f), new Vector2(7f, 1.4f), new Vector2(3f, 1.4f)); // 남은 병목
                for (int i = 0; i < 5; i++) { var p = at + R(new Vector2(-2f - i * 1.5f, (i % 3 - 1) * 3f), ang + i); Poly(ci, p, ang + i * 1.3f, glass, new Vector2(-1.6f, -1f), new Vector2(1.8f, 0f), new Vector2(0f, 1.6f)); }
                break;
            }
            case FallenKind.Box: // 찌그러진 종이 상자 · 테이프 십자
            {
                var card = new Color(0.66f, 0.5f, 0.32f);
                Poly(ci, at, ang, card, new Vector2(-7f, -6f), new Vector2(6f, -6f), new Vector2(7f, -3f), new Vector2(7f, 6f), new Vector2(-7f, 6f));
                ci.DrawLine(at + R(new Vector2(-7f, 0f), ang), at + R(new Vector2(7f, 0f), ang), new Color(0.82f, 0.74f, 0.55f), 2.2f);
                ci.DrawLine(at + R(new Vector2(0f, -6f), ang), at + R(new Vector2(0f, 6f), ang), new Color(0.82f, 0.74f, 0.55f), 2.2f);
                ci.DrawLine(at + R(new Vector2(6f, -6f), ang), at + R(new Vector2(7f, -3f), ang), card.Darkened(0.4f), 1f); // 찌그러진 모서리
                break;
            }
            case FallenKind.Tool: // 렌치
            {
                var a = at + R(new Vector2(-7f, 0f), ang);
                var b = at + R(new Vector2(5f, 0f), ang);
                ci.DrawLine(a, b, Steel, 2.6f, true);
                ci.Arc(b + R(new Vector2(2.2f, 0f), ang), 3f, ang + 0.7f, ang + Mathf.Tau - 0.7f, 10, Steel, 2.2f, true);
                ci.Circle(a, 2.2f, Steel);
                if (close) ci.Circle(a, 0.9f, SteelDark);
                break;
            }
            default: // 약통 (주황 몸 · 흰 뚜껑)
            {
                Poly(ci, at, ang, new Color(0.95f, 0.55f, 0.15f, 0.92f), new Vector2(-4.5f, -3f), new Vector2(3f, -3f), new Vector2(3f, 3f), new Vector2(-4.5f, 3f));
                Poly(ci, at, ang, new Color(0.96f, 0.96f, 0.96f), new Vector2(3f, -3.4f), new Vector2(5.5f, -3.4f), new Vector2(5.5f, 3.4f), new Vector2(3f, 3.4f));
                if (close) Poly(ci, at, ang, new Color(1, 1, 1, 0.85f), new Vector2(-3.5f, -1.5f), new Vector2(1.5f, -1.5f), new Vector2(1.5f, 1.5f), new Vector2(-3.5f, 1.5f));
                break;
            }
        }
    }

    // ── 쏟은 자국 ──
    public static void Splash(CanvasItem ci, Vector2 at, Vector2 dir, SplashKind k, float size, float fade, long seed, bool close)
    {
        var d = dir.LengthSquared() > 0.01f ? dir.Normalized() : Vector2.Right;
        float s = T * 0.45f * size;
        switch (k)
        {
            case SplashKind.Soup: // 주황 국물 웅덩이가 밀린 쪽으로 번진다 · 건더기
            {
                var col = new Color(0.78f, 0.42f, 0.14f, 0.55f * fade);
                ci.Circle(at, s * 0.7f, col);
                ci.Circle(at + d * s * 0.7f, s * 0.5f, col);
                ci.Circle(at + d * s * 1.25f + d.Orthogonal() * s * 0.25f, s * 0.3f, col);
                for (int i = 0; i < (close ? 9 : 4); i++)
                {
                    float h = Hash((int)seed + i * 31);
                    var p = at + d * s * (h * 1.3f) + d.Orthogonal() * s * (Hash((int)seed + i * 7) - 0.5f) * 0.9f;
                    ci.Box(new Rect2(p, new Vector2(2f, 2f)), i % 3 == 0 ? new Color(0.3f, 0.6f, 0.25f, fade) : i % 3 == 1 ? new Color(0.95f, 0.65f, 0.2f, fade) : new Color(0.9f, 0.85f, 0.7f, fade));
                }
                break;
            }
            case SplashKind.Water:
            {
                var col = new Color(0.55f, 0.75f, 0.95f, 0.35f * fade);
                ci.Circle(at, s * 0.6f, col);
                ci.Circle(at + d * s * 0.6f, s * 0.4f, col);
                ci.Arc(at, s * 0.6f, -0.6f, 0.9f, 8, new Color(1, 1, 1, 0.3f * fade), 1f, true);
                break;
            }
            default: // 흩어진 알약 (캡슐 두 색)
                for (int i = 0; i < 10; i++)
                {
                    float h = Hash((int)seed + i * 13);
                    var p = at + d * s * h * 1.4f + d.Orthogonal() * s * (Hash((int)seed + i * 29) - 0.5f) * 1.2f;
                    float a = h * 6f;
                    ci.DrawLine(p, p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 2.2f, new Color(0.95f, 0.95f, 0.95f, fade), 1.8f);
                    ci.DrawLine(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 1.1f, p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 2.2f, new Color(0.85f, 0.25f, 0.3f, fade), 1.8f);
                }
                break;
        }
    }

    // ── 빗자루 ──
    public static void BroomRest(CanvasItem ci, Vector2 at, Vector2 wall, bool close)
    {
        var w = wall.LengthSquared() > 0.01f ? wall.Normalized() : Vector2.Up;
        var top = at + w * T * 0.46f + w.Orthogonal() * 3f;
        var foot = at - w * T * 0.05f + w.Orthogonal() * 6f;
        ci.DrawLine(top, foot, new Color(0.55f, 0.38f, 0.2f), 2f, true); // 나무 자루
        var n = (foot - top).Normalized();
        var o = n.Orthogonal();
        Poly(ci, foot, 0f, new Color(0.8f, 0.68f, 0.35f), -o * 2.5f, o * 2.5f, o * 5f + n * 6f, -o * 5f + n * 6f); // 솔
        if (close) for (int i = -2; i <= 2; i++) ci.DrawLine(foot + o * i * 1.2f, foot + o * i * 2.1f + n * 6f, new Color(0.55f, 0.45f, 0.2f), 0.6f);
        // 쓰레받기
        var pan = at - w.Orthogonal() * 7f;
        Poly(ci, pan, 0f, new Color(0.25f, 0.45f, 0.6f), new Vector2(-4f, -3f), new Vector2(4f, -3f), new Vector2(5f, 3f), new Vector2(-5f, 3f));
        ci.DrawLine(pan + new Vector2(0f, -3f), pan + new Vector2(0f, -7f), new Color(0.2f, 0.35f, 0.5f), 1.6f);
    }

    public static void Sweeping(CanvasItem ci, Vector2 at, bool working, float time)
    {
        float sw = working ? Mathf.Sin(time * 7f) * 0.5f : 0.15f;
        var hand = at + new Vector2(5f, -2f);
        var foot = at + new Vector2(10f + 6f * sw, 9f);
        ci.DrawLine(hand, foot, new Color(0.55f, 0.38f, 0.2f), 2f, true);
        var n = (foot - hand).Normalized();
        var o = n.Orthogonal();
        Poly(ci, foot, 0f, new Color(0.8f, 0.68f, 0.35f), -o * 2.5f, o * 2.5f, o * 5f + n * 5f, -o * 5f + n * 5f);
        if (working)
            for (int i = 0; i < 3; i++) ci.Circle(foot + n * 6f + o * (i - 1) * 3f + new Vector2(sw * 3f, 0), 0.9f, new Color(0.85f, 0.9f, 0.95f, 0.7f)); // 쓸려 가는 조각
    }

    // ── 사람 위 ──
    public static void BunkStraps(CanvasItem ci, Rect2 bed, bool close)
    {
        bool wide = bed.Size.X >= bed.Size.Y;
        for (int i = 1; i <= 2; i++)
        {
            float f = i / 3f;
            Vector2 a, b;
            if (wide) { a = new Vector2(bed.Position.X + bed.Size.X * f, bed.Position.Y + 2f); b = new Vector2(a.X, bed.End.Y - 2f); }
            else { a = new Vector2(bed.Position.X + 2f, bed.Position.Y + bed.Size.Y * f); b = new Vector2(bed.End.X - 2f, a.Y); }
            ci.DrawLine(a, b, Web, 3f, true);
            ci.DrawLine(a, b, WebEdge, 0.8f, true);
            var mid = a.Lerp(b, 0.5f);
            ci.Box(new Rect2(mid - new Vector2(2.6f, 2.6f), new Vector2(5.2f, 5.2f)), Buckle);
            if (close) ci.Box(new Rect2(mid - new Vector2(1.2f, 1.2f), new Vector2(2.4f, 2.4f)), SteelDark);
        }
    }

    public static void Handhold(CanvasItem ci, Vector2 edge, Vector2 toward, Vector2 who, Color sleeve, bool straining, float time)
    {
        var o = toward.Orthogonal();
        // 벽 손잡이 (U자 쇠막대 · 노란 테)
        var a = edge - o * 6f; var b = edge + o * 6f;
        ci.DrawLine(a, a - toward * 4f, SteelDark, 2f);
        ci.DrawLine(b, b - toward * 4f, SteelDark, 2f);
        ci.DrawLine(a - toward * 4f, b - toward * 4f, Hazard, 2.6f, true);
        // 팔 · 쥔 손
        var grip = edge - toward * 4f + o * (straining ? Mathf.Sin(time * 9f) * 1.2f : 0f);
        ci.DrawLine(who, grip, sleeve, 3f, true);
        ci.Circle(grip, 2.6f, Skin);
    }

    public static void CupHand(CanvasItem ci, Vector2 cup, float time)
    {
        ci.Arc(cup, 6.5f, -2.2f, 0.6f, 10, Skin, 3f, true); // 감싼 손가락
        ci.Circle(cup + new Vector2(5.5f, -3f), 2.2f, Skin);
    }

    public static void Tumble(CanvasItem ci, Vector2 at, Vector2 dir, bool hurt, bool fromBed, float age, bool up, float time)
    {
        var d = dir.LengthSquared() > 0.01f ? dir.Normalized() : Vector2.Right;
        float fade = 1f - age;
        // 미끄러진 자국 (뒤쪽으로)
        ci.DrawLine(at - d * 16f, at - d * 4f, new Color(0.2f, 0.2f, 0.2f, 0.25f * fade), 3f, true);
        if (fromBed) // 바닥에 떨어진 이불
        {
            Poly(ci, at + d * 6f, d.Angle(), new Color(0.45f, 0.55f, 0.75f, 0.8f), new Vector2(-8f, -6f), new Vector2(9f, -5f), new Vector2(8f, 7f), new Vector2(-9f, 6f));
            for (int i = -1; i <= 1; i++) ci.DrawLine(at + d * 6f + R(new Vector2(-7f, i * 3.5f), d.Angle()), at + d * 6f + R(new Vector2(7f, i * 3.5f), d.Angle()), new Color(0.9f, 0.9f, 0.95f, 0.6f), 1f);
        }
        if (up) return;
        // 먼지 (퍼지며 옅어진다)
        for (int i = 0; i < 5; i++)
        {
            float a = i * 1.26f + age * 2f;
            ci.Circle(at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (8f + age * 10f), 2.4f + age * 2f, new Color(0.75f, 0.72f, 0.66f, 0.35f * fade));
        }
        // 손바닥을 짚은 자국
        ci.Circle(at + d * 9f + d.Orthogonal() * 4f, 2f, new Color(Skin, 0.9f));
        if (hurt) // 아픈 별 (머리 위를 돈다)
            for (int i = 0; i < 3; i++)
            {
                float a = time * 3f + i * Mathf.Tau / 3f;
                var p = at + new Vector2(Mathf.Cos(a) * 9f, -12f + Mathf.Sin(a) * 3f);
                ci.DrawLine(p - new Vector2(2f, 0), p + new Vector2(2f, 0), Hazard, 1.2f);
                ci.DrawLine(p - new Vector2(0, 2f), p + new Vector2(0, 2f), Hazard, 1.2f);
            }
    }

    public static void CarryCrate(CanvasItem ci, Vector2 at, int id)
    {
        var p = at + new Vector2(0f, -6f);
        ci.Box(new Rect2(p - new Vector2(6f, 5f), new Vector2(12f, 9f)), new Color(0.42f, 0.46f, 0.38f));
        ci.Box(new Rect2(p - new Vector2(6f, 5f), new Vector2(12f, 9f)), new Color(0.2f, 0.22f, 0.18f), false, 1f);
        ci.DrawLine(p + new Vector2(-6f, -1f), p + new Vector2(6f, -1f), new Color(0.2f, 0.22f, 0.18f), 1f);
        ci.Circle(p + new Vector2(-6.5f, 2f), 2f, Skin);
        ci.Circle(p + new Vector2(6.5f, 2f), 2f, Skin);
    }
}
