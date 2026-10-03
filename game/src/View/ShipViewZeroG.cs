using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v18.4 무중력 · v18.2 생태계 · v18.3 배수 그림 (읽기만 한다):
//  바닥 층 — 배수구(둥근 개수대 구멍 · 네모 바닥 창살 · 세탁 배수관 · 긴 샤워 홈통, 막힐수록 검은 찌꺼기 고리 · 역류하면 회색물 웅덩이와 거품 · 냄새 물결) ·
//   쓰레기통(주방 페달통 · 둥근 통 · 나눠 버리는 배면 세 칸 통, 찰수록 뚜껑이 들리고 넘치면 쓰레기와 날파리) ·
//   화분 여섯 가지(고사리 깃 · 선인장 기둥과 가시 · 벽걸이 아이비 덩굴 · 바질 잎 무더기 · 고무나무 큰 잎 · 난초 꽃대) — 시들면 처지고 누레지고 죽으면 갈색 ·
//   깨진 화분 흙 · 고양이 방석(발자국 무늬) · 밥그릇(사료 알갱이) · 바구미 포대(기어 다니는 점) · 밀폐 통 · 합선 그을음 · 떨어진 자리 충격 자국.
//  사람 위 층 — 떠다니는 것 열세 가지(렌치 · 드라이버 · 통조림 · 책 · 유리병 · 접시 · 사발 · 약통 · 베개 · 화분 · 물방울 · 토사물 · 짐) ·
//   손잡이 쥔 손과 벽 손잡이 · 떠 있는 사람의 흔들림 · 멀미 소용돌이 · 고양이(걷기 · 낮잠 · 조름 · 먹기 · 숨은 눈 · 안긴 · 허우적 · 덮인 담요).
public partial class ShipView
{
    private void PaintEco(CanvasItem ci)
    {
        var w = _world;
        bool close = Zoom > 0.75f;
        long now = w.Tick;
        foreach (var d in w.Drains.Drains)
            EcoArt.Drain(ci, CellRect(d.At).GetCenter(), d.Kind, d.Clog, d.Backflow, d.Stink, d.Id, _time, close);
        bool sorted = w.Culture.Of(CustomKind.SortWaste) != null;
        foreach (var b in w.Drains.Bins)
            EcoArt.Bin(ci, CellRect(b.At).GetCenter(), w.Ship.Rooms[b.RoomId].Kind, b.Fill, b.Food, sorted, b.Id, _time, close);
        foreach (var id in w.Eco.Sealed)
            if (w.Ship.Furniture.FirstOrDefault(f => f.Id == id) is Furniture sf && !sf.Room.Detached) EcoArt.Canisters(ci, FurnitureRect(sf), id, close);
        foreach (var x in w.Eco.Pests)
            if (!x.Treated && x.Pop > 0.12f && w.Ship.Furniture.FirstOrDefault(f => f.Id == x.Shelf) is Furniture pf)
                EcoArt.Weevils(ci, FurnitureRect(pf), x.Pop, x.Found, x.Id, _time, close);
        if (w.Eco.CatBed is Cell cb) EcoArt.CatBed(ci, CellRect(cb).GetCenter(), w.Eco.Cat?.Name.GetHashCode() ?? 0);
        if (w.Eco.Bowl is Cell bw) EcoArt.Bowl(ci, CellRect(bw).GetCenter(), w.Eco.Cat is ShipCat ct && ct.Alive && ct.Hunger < 0.35f, w.Eco.Kibble <= 0);
        foreach (var p in w.Eco.Plants)
            if (!p.Floating) EcoArt.Plant(ci, CellRect(p.At).GetCenter(), p.Kind, p.Health, p.Stage, p.Spilled, p.Fixed, WallToward(p.At), p.Id, _time, now - p.Watered < 120 && p.WateredBy >= 0, close);
        var z = w.ZeroG;
        foreach (var s in z.Shorts)
            if (now - s.Tick < SimTime.Hours(6)) EcoArt.Scorch(ci, ToPx(s.At), now - s.Tick < 90, s.Tick, _time);
        foreach (var t in z.Thuds)
            if (now - t.Tick < SimTime.Minutes(20)) EcoArt.Thud(ci, ToPx(t.At), (now - t.Tick) / (float)SimTime.Minutes(20), t.Broke, t.Hit >= 0);
    }

    private void PaintZeroGOver(CanvasItem ci)
    {
        var w = _world;
        var z = w.ZeroG;
        bool close = Zoom > 0.75f;
        if (z.Weightless)
        {
            foreach (var f in z.Floaters) EcoArt.Floater(ci, ToPx(f.Pos), f.Angle, f.Kind, f.Liters, f.Id, _time, close);
            foreach (var c in w.Crew)
            {
                if (c.Dead || c.Outside || c.Room == null) continue;
                var at = ToPx(c.Position);
                if (z.Grips.TryGetValue(c.Id, out var g)) EcoArt.Grip(ci, at, new Vector2(g.X, g.Y), close);
                else EcoArt.Adrift(ci, at, c.Id, _time, z.Drifting.Contains(c.Id));
                float q = z.Queasy.TryGetValue(c.Id, out var qq) ? qq : 0f;
                if (q > 0.45f && close) EcoArt.Queasy(ci, at + new Vector2(0, -16f), q, _time);
            }
        }
        if (w.Eco.Cat is ShipCat cat)
        {
            int stripe = cat.Name.Length * 7 + cat.Name[0];
            EcoArt.Cat(ci, ToPx(cat.Pos), new Vector2(cat.Facing.X, cat.Facing.Y), cat.State, cat.Hidden, cat.Covered, stripe, _time,
                w.Tick - cat.Purr < 60, cat.State == CatState.Beg && w.Tick - cat.Meow < 200, close);
        }
    }
}

internal static class EcoArt
{
    private const float T = ShipView.T;
    private static readonly Color Steel = new(0.66f, 0.70f, 0.74f), SteelDark = new(0.30f, 0.33f, 0.37f), Grime = new(0.16f, 0.13f, 0.10f, 0.85f);
    private static readonly Color Grey = new(0.52f, 0.55f, 0.50f, 0.75f), Soil = new(0.33f, 0.22f, 0.14f), Terra = new(0.74f, 0.42f, 0.26f), Skin = new(0.93f, 0.76f, 0.62f);

    private static float Hash(int n) { uint x = (uint)n * 2654435761u; x ^= x >> 13; x *= 0x5bd1e995; x ^= x >> 15; return (x & 0xffff) / 65535f; }
    private static Vector2 R(Vector2 v, float a) => v.Rotated(a);
    private static void Poly(CanvasItem ci, Vector2 at, float ang, Color c, params Vector2[] pts)
    {
        var a = new Vector2[pts.Length];
        for (int i = 0; i < pts.Length; i++) a[i] = at + R(pts[i], ang);
        ci.DrawColoredPolygon(a, c);
    }
    private static void Blob(CanvasItem ci, Vector2 at, float r, Color c, float wob, float t, int seed)
    {
        var pts = new Vector2[14];
        for (int i = 0; i < 14; i++)
        {
            float a = i * Mathf.Tau / 14f;
            float rr = r * (1f + wob * Mathf.Sin(a * 3f + t * 2.2f + seed) * 0.6f + wob * 0.4f * Mathf.Sin(a * 5f - t * 1.7f));
            pts[i] = at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
        }
        ci.DrawColoredPolygon(pts, c);
    }

    // ── 배수구 (종류마다 다른 쇠붙이) ──
    public static void Drain(CanvasItem ci, Vector2 at, DrainKind k, float clog, bool back, float stink, int id, float t, bool close)
    {
        if (back)
        {
            Blob(ci, at + new Vector2(2f, 3f), 13f, new Color(0.45f, 0.47f, 0.40f, 0.55f), 0.18f, t * 0.3f, id);
            Blob(ci, at, 8f, new Color(0.38f, 0.40f, 0.33f, 0.7f), 0.12f, t * 0.4f, id + 3);
            for (int i = 0; i < 4; i++)
            {
                float ph = (t * 0.8f + i * 0.27f + Hash(id + i)) % 1f;
                ci.DrawArc(at + new Vector2(Mathf.Sin(i * 2.1f + id) * 7f, Mathf.Cos(i * 1.3f) * 5f), 1.2f + 2.2f * ph, 0f, Mathf.Tau, 8, new Color(0.85f, 0.88f, 0.80f, 0.7f * (1f - ph)), 0.8f);
            }
        }
        switch (k)
        {
            case DrainKind.Sink: // 둥근 스테인리스 구멍 · 십자 살 · 둘레 구멍
                ci.DrawCircle(at, 6f, Steel);
                ci.DrawCircle(at, 4.6f, SteelDark);
                ci.DrawLine(at + new Vector2(-4.4f, 0), at + new Vector2(4.4f, 0), Steel, 1.2f);
                ci.DrawLine(at + new Vector2(0, -4.4f), at + new Vector2(0, 4.4f), Steel, 1.2f);
                if (close) for (int i = 0; i < 8; i++) ci.DrawCircle(at + new Vector2(Mathf.Cos(i * 0.785f), Mathf.Sin(i * 0.785f)) * 5.3f, 0.5f, SteelDark);
                break;
            case DrainKind.Floor: // 네모 창살
                ci.DrawRect(new Rect2(at - new Vector2(7f, 7f), new Vector2(14f, 14f)), Steel);
                ci.DrawRect(new Rect2(at - new Vector2(6f, 6f), new Vector2(12f, 12f)), SteelDark);
                for (int i = -2; i <= 2; i++) ci.DrawLine(at + new Vector2(i * 2.4f, -6f), at + new Vector2(i * 2.4f, 6f), Steel, 1f);
                if (close) for (int i = 0; i < 4; i++) ci.DrawCircle(at + new Vector2(i < 2 ? -6.5f : 6.5f, i % 2 == 0 ? -6.5f : 6.5f), 0.7f, SteelDark);
                break;
            case DrainKind.Laundry: // 세탁 배수관: 벽에서 내려온 굽은 관 + 바닥 깔때기
                ci.DrawCircle(at, 5.5f, SteelDark);
                ci.DrawArc(at, 5.5f, 0f, Mathf.Tau, 16, Steel, 1.6f);
                ci.DrawLine(at + new Vector2(-9f, -9f), at + new Vector2(-2f, -2f), new Color(0.75f, 0.78f, 0.82f), 3f);
                ci.DrawArc(at + new Vector2(-9f, -12f), 3f, Mathf.Pi * 0.5f, Mathf.Pi * 1.2f, 6, new Color(0.75f, 0.78f, 0.82f), 3f);
                break;
            default: // 샤워 홈통: 긴 홈 + 가는 틈
                ci.DrawRect(new Rect2(at - new Vector2(13f, 3.5f), new Vector2(26f, 7f)), Steel);
                ci.DrawRect(new Rect2(at - new Vector2(12f, 2.5f), new Vector2(24f, 5f)), SteelDark);
                for (int i = -5; i <= 5; i++) ci.DrawLine(at + new Vector2(i * 2.2f, -2f), at + new Vector2(i * 2.2f, 2f), Steel, 0.7f);
                break;
        }
        if (clog > 0.35f) ci.DrawArc(at, 3.2f, 0f, Mathf.Tau, 12, new Color(Grime, Grime.A * Mathf.Clamp((clog - 0.35f) * 1.6f, 0f, 1f)), 1.5f + 2.5f * clog);
        if (stink > 0.2f)
            for (int i = 0; i < 3; i++)
            {
                float ph = (t * 0.35f + i * 0.33f) % 1f;
                var b = at + new Vector2(-6f + i * 6f, -6f - ph * 16f);
                ci.DrawPolyline(new[] { b, b + new Vector2(2.5f, -3f), b + new Vector2(-1f, -6f), b + new Vector2(2f, -9f) }, new Color(0.55f, 0.62f, 0.30f, 0.6f * stink * (1f - ph)), 1.1f);
            }
    }

    // ── 쓰레기통 ──
    public static void Bin(CanvasItem ci, Vector2 at, RoomType room, float fill, float food, bool sorted, int id, float t, bool close)
    {
        bool over = fill >= 1f;
        if (over) // 넘친 쓰레기: 구긴 종이 · 찌그러진 캔 · 껍질
            for (int i = 0; i < 5; i++)
            {
                var p = at + new Vector2(Mathf.Cos(i * 1.7f + id) * (8f + 3f * Hash(id + i)), 6f + Mathf.Sin(i * 2.3f) * 4f);
                if (i % 3 == 0) ci.DrawCircle(p, 2.2f, new Color(0.92f, 0.90f, 0.84f));
                else if (i % 3 == 1) ci.DrawRect(new Rect2(p, new Vector2(3.5f, 2f)), Steel);
                else ci.DrawArc(p, 2f, 0f, 2.5f, 5, new Color(0.85f, 0.70f, 0.20f), 1.3f);
            }
        ci.DrawCircle(at + new Vector2(1.5f, 2f), 7.5f, new Color(0, 0, 0, 0.2f));
        float lid = Mathf.Clamp((fill - 0.6f) * 1.3f, 0f, 0.6f);
        if (sorted) // 세 칸 통: 금속(회색) · 플라스틱(노랑) · 음식물(갈색)
        {
            var cols = new[] { new Color(0.55f, 0.58f, 0.62f), new Color(0.92f, 0.78f, 0.22f), new Color(0.52f, 0.36f, 0.20f) };
            for (int i = 0; i < 3; i++)
            {
                var r = new Rect2(at + new Vector2(-10.5f + i * 7f, -6f), new Vector2(6.5f, 12f));
                ci.DrawRect(r, new Color(0.25f, 0.28f, 0.30f));
                ci.DrawRect(new Rect2(r.Position + new Vector2(0.5f, -1.5f - lid * 4f), new Vector2(5.5f, 3f)), cols[i]);
                if (close) ci.DrawLine(r.Position + new Vector2(2f, 3f), r.Position + new Vector2(4.5f, 3f), cols[i], 1f);
            }
        }
        else if (room is RoomType.Galley or RoomType.Mess) // 주방 페달통: 네모 · 페달 · 경첩 뚜껑
        {
            ci.DrawRect(new Rect2(at - new Vector2(6.5f, 7f), new Vector2(13f, 14f)), new Color(0.80f, 0.82f, 0.84f));
            ci.DrawRect(new Rect2(at + new Vector2(-3f, 6f), new Vector2(6f, 2.5f)), SteelDark);
            Poly(ci, at + new Vector2(-6.5f, -7f), -lid, new Color(0.62f, 0.64f, 0.66f), new Vector2(0, -1.5f), new Vector2(13f, -1.5f), new Vector2(13f, 1f), new Vector2(0, 1f));
        }
        else // 둥근 통 · 고무 뚜껑
        {
            ci.DrawCircle(at, 6.5f, new Color(0.28f, 0.42f, 0.36f));
            ci.DrawArc(at, 6.5f, 0f, Mathf.Tau, 16, new Color(0.18f, 0.28f, 0.24f), 1.2f);
            ci.DrawCircle(at + new Vector2(lid * 6f, -lid * 4f), 5f, new Color(0.22f, 0.34f, 0.29f));
            if (close) ci.DrawLine(at + new Vector2(-2f + lid * 6f, -lid * 4f), at + new Vector2(2f + lid * 6f, -lid * 4f), new Color(0.6f, 0.7f, 0.65f), 1.4f);
        }
        if (fill > 0.85f && !sorted) ci.DrawCircle(at + new Vector2(-2f, -4f - lid * 3f), 2.4f, new Color(0.90f, 0.88f, 0.80f)); // 삐져나온 봉지
        if (over && food > 0.2f) // 날파리
            for (int i = 0; i < 4; i++)
            {
                float a = t * (2.5f + i * 0.7f) + i * 1.6f;
                ci.DrawCircle(at + new Vector2(Mathf.Cos(a) * (7f + i), -8f + Mathf.Sin(a * 1.3f) * 4f), 0.8f, new Color(0.05f, 0.05f, 0.05f, 0.9f));
            }
    }

    // ── 밀폐 통 (바구미 방제 뒤 선반) ──
    public static void Canisters(CanvasItem ci, Rect2 r, int id, bool close)
    {
        for (int i = 0; i < 3; i++)
        {
            var c = r.Position + new Vector2(r.Size.X * (0.22f + 0.28f * i), r.Size.Y * 0.62f);
            ci.DrawRect(new Rect2(c - new Vector2(3.5f, 5f), new Vector2(7f, 10f)), new Color(0.82f, 0.88f, 0.92f, 0.85f));
            ci.DrawRect(new Rect2(c - new Vector2(4f, 6.5f), new Vector2(8f, 2.2f)), new Color(0.20f, 0.45f, 0.70f));
            if (close) { ci.DrawLine(c + new Vector2(3.8f, -5.5f), c + new Vector2(3.8f, -2f), SteelDark, 1f); ci.DrawRect(new Rect2(c - new Vector2(3f, 1f), new Vector2(6f, 4f)), new Color(0.85f, 0.75f, 0.45f, 0.8f)); }
        }
    }

    // ── 바구미 포대 ──
    public static void Weevils(CanvasItem ci, Rect2 r, float pop, bool found, int id, float t, bool close)
    {
        var c = r.GetCenter() + new Vector2(0, 2f);
        Poly(ci, c, 0f, new Color(0.72f, 0.60f, 0.40f), new Vector2(-7f, 6f), new Vector2(-6f, -5f), new Vector2(-3f, -7f), new Vector2(3f, -7f), new Vector2(6f, -5f), new Vector2(7f, 6f));
        ci.DrawLine(c + new Vector2(-4f, -6f), c + new Vector2(4f, -6f), new Color(0.45f, 0.35f, 0.22f), 1.2f); // 묶은 끈
        if (close) for (int i = 0; i < 3; i++) ci.DrawLine(c + new Vector2(-5f, -2f + i * 3f), c + new Vector2(5f, -2f + i * 3f), new Color(0.62f, 0.50f, 0.32f), 0.6f); // 마대 결
        int n = 2 + (int)(pop * 14f);
        for (int i = 0; i < n; i++)
        {
            float ph = t * (0.12f + 0.05f * Hash(id + i)) + Hash(id * 7 + i);
            var p = c + new Vector2(Mathf.Sin(ph * 6.2f) * 7f, Mathf.Cos(ph * 4.1f + i) * 6f);
            ci.DrawCircle(p, close ? 0.9f : 0.7f, new Color(0.18f, 0.10f, 0.06f));
            if (close) ci.DrawLine(p, p + new Vector2(Mathf.Cos(ph * 9f), Mathf.Sin(ph * 9f)) * 1.4f, new Color(0.18f, 0.10f, 0.06f), 0.5f); // 주둥이
        }
        if (pop > 0.6f) for (int i = 0; i < 6; i++) ci.DrawCircle(c + new Vector2(-8f + i * 3f, 9f + (i % 2)), 0.6f, new Color(0.85f, 0.80f, 0.62f)); // 흘러나온 가루
        if (found) { ci.DrawLine(r.Position + new Vector2(2f, 3f), r.Position + new Vector2(r.Size.X - 2f, 3f), new Color(0.92f, 0.20f, 0.18f), 2.2f); }
    }

    // ── 고양이 방석 · 밥그릇 ──
    public static void CatBed(CanvasItem ci, Vector2 at, int seed)
    {
        ci.DrawCircle(at, 11f, new Color(0.55f, 0.32f, 0.36f));
        ci.DrawCircle(at, 8f, new Color(0.78f, 0.56f, 0.58f));
        ci.DrawArc(at, 10f, 0f, Mathf.Tau, 20, new Color(0.45f, 0.25f, 0.28f), 1f);
        var paw = new Color(0.62f, 0.40f, 0.44f);
        ci.DrawCircle(at + new Vector2(0, 1.5f), 2.2f, paw);
        for (int i = 0; i < 4; i++) ci.DrawCircle(at + new Vector2(-3f + i * 2f, -2f - (i is 1 or 2 ? 1f : 0f)), 0.9f, paw);
    }

    public static void Bowl(CanvasItem ci, Vector2 at, bool full, bool noKibble)
    {
        ci.DrawCircle(at, 5.5f, new Color(0.25f, 0.55f, 0.75f));
        ci.DrawCircle(at, 4f, new Color(0.15f, 0.35f, 0.50f));
        if (full) for (int i = 0; i < 6; i++) ci.DrawCircle(at + new Vector2(Mathf.Cos(i * 1.05f) * 2f, Mathf.Sin(i * 1.05f) * 2f), 1.1f, noKibble ? new Color(0.8f, 0.7f, 0.5f) : new Color(0.55f, 0.32f, 0.16f));
        else ci.DrawCircle(at + new Vector2(1.5f, 1f), 0.6f, new Color(0.55f, 0.32f, 0.16f));
    }

    // ── 화분 여섯 가지 ──
    public static void Plant(CanvasItem ci, Vector2 at, PlantKind k, float health, int stage, bool spilled, bool hung, Vector2 wall, int id, float t, bool watering, bool close)
    {
        float droop = stage switch { 0 => 0f, 1 => 0.25f, 2 => 0.7f, _ => 1.1f };
        Color leaf = stage switch
        {
            0 => new Color(0.22f, 0.62f, 0.28f), 1 => new Color(0.38f, 0.60f, 0.26f), 2 => new Color(0.62f, 0.60f, 0.26f), _ => new Color(0.45f, 0.33f, 0.20f),
        };
        if (k == PlantKind.Fig) leaf = leaf.Darkened(0.25f);
        float sway = stage < 3 ? Mathf.Sin(t * 0.8f + id) * 0.04f : 0f;
        var pot = at;
        if (spilled)
        {
            for (int i = 0; i < 7; i++) ci.DrawCircle(at + new Vector2(-6f + i * 2.4f, 5f + Mathf.Sin(i * 2f) * 2.5f), 1.5f, Soil);
            pot = at + new Vector2(3f, 2f);
            Poly(ci, pot, 1.2f, Terra, new Vector2(-5f, -4f), new Vector2(5f, -4f), new Vector2(4f, 4f), new Vector2(-4f, 4f));
            ci.DrawLine(pot + new Vector2(-3f, -3f), pot + new Vector2(1f, 3f), Terra.Darkened(0.4f), 0.8f); // 금
            return;
        }
        switch (k)
        {
            case PlantKind.Ivy: // 벽걸이 화분 · 받침 쇠 · 늘어진 덩굴과 하트 잎
            {
                var wv = wall == Vector2.Zero ? new Vector2(0, -1) : wall;
                pot = at + wv * 9f;
                ci.DrawLine(pot + wv * 4f, pot - wv * 2f, SteelDark, 2f);
                ci.DrawRect(new Rect2(pot - new Vector2(5f, 3.5f), new Vector2(10f, 7f)), new Color(0.90f, 0.90f, 0.86f));
                for (int v = 0; v < 3; v++)
                {
                    var p = pot + new Vector2(-4f + v * 4f, 2f);
                    for (int s = 0; s < 4; s++)
                    {
                        var n = p - wv * (4f + droop * 2f) + new Vector2(Mathf.Sin(s * 1.3f + v + sway * 10f) * 2f, 0);
                        ci.DrawLine(p, n, leaf.Darkened(0.3f), 0.8f);
                        ci.DrawCircle(n + new Vector2(-0.8f, 0), 1.4f, leaf); ci.DrawCircle(n + new Vector2(0.8f, 0), 1.4f, leaf);
                        p = n;
                    }
                }
                return;
            }
            case PlantKind.Cactus: // 네모 사기 화분 · 기둥 셋 · 가시 · 싱싱하면 꽃
                ci.DrawRect(new Rect2(at + new Vector2(-5f, 1f), new Vector2(10f, 7f)), new Color(0.85f, 0.85f, 0.88f));
                ci.DrawRect(new Rect2(at + new Vector2(-5f, 1f), new Vector2(10f, 1.5f)), new Color(0.35f, 0.55f, 0.75f));
                for (int i = 0; i < 3; i++)
                {
                    float h = (i == 1 ? 11f : 7f) * (1f - 0.25f * droop);
                    var b = at + new Vector2(-3f + i * 3f, 1f);
                    var top = b + new Vector2((i - 1) * (1.5f + 2f * droop), -h);
                    ci.DrawLine(b, top, leaf, 3.2f);
                    ci.DrawCircle(top, 1.6f, leaf);
                    if (close) for (int s = 1; s < 4; s++) ci.DrawCircle(b.Lerp(top, s / 4f) + new Vector2(1.8f, 0), 0.4f, new Color(0.95f, 0.95f, 0.85f));
                }
                if (stage == 0 && close) ci.DrawCircle(at + new Vector2(0, -10f), 1.8f, new Color(0.95f, 0.45f, 0.60f));
                return;
        }
        // 나머지는 둥근 흙 화분 (고사리 · 바질은 작은 것 · 고무나무는 큰 것 · 난초는 가는 것)
        float pr = k == PlantKind.Fig ? 7f : k == PlantKind.Orchid ? 4.5f : 5.5f;
        ci.DrawCircle(at + new Vector2(1f, 1.5f), pr + 0.5f, new Color(0, 0, 0, 0.18f));
        ci.DrawCircle(at, pr, k == PlantKind.Orchid ? new Color(0.88f, 0.88f, 0.92f) : Terra);
        ci.DrawCircle(at, pr - 1.4f, Soil);
        if (k == PlantKind.Basil) ci.DrawRect(new Rect2(at + new Vector2(-pr, pr - 2f), new Vector2(pr * 2f, 2f)), new Color(0.82f, 0.30f, 0.22f)); // 띠
        switch (k)
        {
            case PlantKind.Fern: // 깃처럼 갈라진 잎 여럿
                for (int i = 0; i < 7; i++)
                {
                    float a = i * Mathf.Tau / 7f + sway;
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) + droop * 0.6f).Normalized();
                    var tip = at + dir * (12f - 2f * droop);
                    ci.DrawLine(at, tip, leaf.Darkened(0.2f), 0.9f);
                    if (close) for (int s = 1; s < 5; s++) { var m = at.Lerp(tip, s / 5f); var n = dir.Orthogonal() * (2.4f - s * 0.35f); ci.DrawLine(m - n, m + n, leaf, 1f); }
                    else ci.DrawLine(at, tip, leaf, 2.2f);
                }
                break;
            case PlantKind.Basil: // 둥근 잎 무더기
                for (int i = 0; i < 9; i++)
                {
                    var p = at + new Vector2(Mathf.Cos(i * 2.4f) * (2f + i * 0.5f), Mathf.Sin(i * 2.4f) * (2f + i * 0.5f) + droop * 2f);
                    ci.DrawCircle(p, 2.2f - droop * 0.5f, i % 2 == 0 ? leaf : leaf.Lightened(0.15f));
                    if (close) ci.DrawLine(p - new Vector2(1.2f, 0), p + new Vector2(1.2f, 0), leaf.Darkened(0.3f), 0.4f);
                }
                break;
            case PlantKind.Fig: // 큰 반들 잎 다섯 · 가운데 줄기
                for (int i = 0; i < 5; i++)
                {
                    float a = i * 1.26f + 0.3f + sway;
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) + droop * 0.8f).Normalized();
                    var c = at + dir * 7f;
                    Poly(ci, c, dir.Angle(), leaf, new Vector2(-5f, 0), new Vector2(0, -2.8f), new Vector2(6f, 0), new Vector2(0, 2.8f));
                    ci.DrawLine(c - dir * 5f, c + dir * 5f, leaf.Lightened(0.25f), 0.5f);
                    if (close && stage == 0) ci.DrawCircle(c + dir.Orthogonal() * 1f, 0.8f, new Color(1, 1, 1, 0.35f)); // 윤기
                }
                break;
            default: // 난초: 넓은 잎 둘 · 휜 꽃대 · 꽃
                Poly(ci, at, 0.2f, leaf, new Vector2(0, 0), new Vector2(9f, -2f), new Vector2(10f, 1f));
                Poly(ci, at, Mathf.Pi - 0.2f, leaf, new Vector2(0, 0), new Vector2(9f, -2f), new Vector2(10f, 1f));
                var stemTop = at + new Vector2(5f, -13f + droop * 8f);
                ci.DrawPolyline(new[] { at, at + new Vector2(1f, -8f), stemTop }, new Color(0.35f, 0.45f, 0.25f), 0.9f);
                if (stage < 2)
                    for (int i = 0; i < 3; i++)
                    {
                        var f = stemTop + new Vector2(-i * 2.6f, i * 2.2f);
                        for (int p = 0; p < 5; p++) ci.DrawCircle(f + new Vector2(Mathf.Cos(p * 1.26f), Mathf.Sin(p * 1.26f)) * 1.6f, 1.1f, new Color(0.92f, 0.62f, 0.85f));
                        ci.DrawCircle(f, 0.7f, new Color(0.95f, 0.85f, 0.30f));
                    }
                break;
        }
        if (stage == 2) for (int i = 0; i < 2; i++) ci.DrawCircle(at + new Vector2(6f + i * 2f, 6f), 1.1f, new Color(0.70f, 0.55f, 0.22f)); // 떨어진 잎
        if (watering) for (int i = 0; i < 4; i++) ci.DrawLine(at + new Vector2(-8f + i, -10f + ((t * 30f + i * 4f) % 8f)), at + new Vector2(-8f + i, -8f + ((t * 30f + i * 4f) % 8f)), new Color(0.55f, 0.75f, 0.95f, 0.8f), 1f);
    }

    // ── 합선 그을음 · 충격 자국 ──
    public static void Scorch(CanvasItem ci, Vector2 at, bool fresh, long seed, float t)
    {
        for (int i = 0; i < 6; i++)
        {
            float a = i * 1.05f + (seed % 7);
            ci.DrawLine(at, at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (4f + 2f * Hash((int)seed + i)), new Color(0.10f, 0.08f, 0.06f, 0.6f), 1.2f);
        }
        if (!fresh) return;
        for (int i = 0; i < 5; i++)
        {
            float a = t * 13f + i * 1.3f;
            ci.DrawLine(at, at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (5f + 3f * Mathf.Abs(Mathf.Sin(t * 20f + i))), new Color(1f, 0.92f, 0.45f, 0.9f), 1f);
        }
        ci.DrawCircle(at, 2.5f, new Color(0.75f, 0.90f, 1f, 0.8f));
    }

    public static void Thud(CanvasItem ci, Vector2 at, float age, bool broke, bool hit)
    {
        float a = 1f - age;
        ci.DrawArc(at, 4f + 8f * age, 0f, Mathf.Tau, 16, new Color(0.9f, 0.9f, 0.85f, 0.45f * a), 1.2f);
        if (broke) for (int i = 0; i < 5; i++) ci.DrawLine(at, at + new Vector2(Mathf.Cos(i * 1.25f), Mathf.Sin(i * 1.25f)) * 6f, new Color(0.85f, 0.95f, 1f, 0.6f * a), 0.8f);
        if (hit) ci.DrawCircle(at + new Vector2(0, -6f), 2f, new Color(0.95f, 0.30f, 0.25f, 0.7f * a));
    }

    // ── 떠다니는 것 열세 가지 ──
    public static void Floater(CanvasItem ci, Vector2 at, float ang, DriftKind k, float liters, int id, float t, bool close)
    {
        var p = at + new Vector2(Mathf.Sin(t * 0.9f + id) * 1.5f, Mathf.Cos(t * 0.7f + id * 1.3f) * 1.5f); // 공기 따라 살짝
        ci.DrawCircle(at + new Vector2(5f, 9f), 3.5f, new Color(0, 0, 0, 0.10f)); // 바닥에서 먼 옅은 그림자
        float h = Hash(id);
        switch (k)
        {
            case DriftKind.Wrench: // 자루 · 양쪽 턱
                Poly(ci, p, ang, Steel, new Vector2(-6f, -1.2f), new Vector2(6f, -1.2f), new Vector2(6f, 1.2f), new Vector2(-6f, 1.2f));
                ci.DrawArc(p + R(new Vector2(-7.5f, 0), ang), 2.4f, ang + 0.6f, ang + Mathf.Tau - 0.6f, 8, Steel, 1.6f);
                ci.DrawArc(p + R(new Vector2(7.5f, 0), ang), 2.4f, ang + Mathf.Pi + 0.6f, ang + Mathf.Pi + Mathf.Tau - 0.6f, 8, Steel, 1.6f);
                if (close) ci.DrawLine(p + R(new Vector2(-3f, 0), ang), p + R(new Vector2(3f, 0), ang), SteelDark, 0.6f);
                break;
            case DriftKind.Driver: // 줄무늬 손잡이 · 가는 대 · 날
                Poly(ci, p, ang, Color.FromHsv(h, 0.7f, 0.85f), new Vector2(-6f, -1.8f), new Vector2(-1f, -1.8f), new Vector2(-1f, 1.8f), new Vector2(-6f, 1.8f));
                if (close) for (int i = 0; i < 3; i++) ci.DrawLine(p + R(new Vector2(-5f + i * 1.5f, -1.8f), ang), p + R(new Vector2(-5f + i * 1.5f, 1.8f), ang), new Color(0, 0, 0, 0.3f), 0.5f);
                ci.DrawLine(p + R(new Vector2(-1f, 0), ang), p + R(new Vector2(6.5f, 0), ang), Steel, 1f);
                ci.DrawLine(p + R(new Vector2(6.5f, -0.9f), ang), p + R(new Vector2(6.5f, 0.9f), ang), SteelDark, 0.8f);
                break;
            case DriftKind.Can:
                Poly(ci, p, ang, Steel, new Vector2(-4f, -5f), new Vector2(4f, -5f), new Vector2(4f, 5f), new Vector2(-4f, 5f));
                Poly(ci, p, ang, Color.FromHsv(h, 0.6f, 0.85f), new Vector2(-4f, -2.5f), new Vector2(4f, -2.5f), new Vector2(4f, 2.5f), new Vector2(-4f, 2.5f));
                ci.DrawLine(p + R(new Vector2(-4f, -5f), ang), p + R(new Vector2(4f, -5f), ang), SteelDark, 1f);
                break;
            case DriftKind.Book: // 펼쳐져 펄럭이는 책
            {
                float flap = Mathf.Sin(t * 2f + id) * 0.25f;
                var cov = Color.FromHsv(h, 0.55f, 0.6f);
                Poly(ci, p, ang - flap, cov, new Vector2(0, -5f), new Vector2(-7f, -4f), new Vector2(-7f, 4f), new Vector2(0, 5f));
                Poly(ci, p, ang + flap, cov.Darkened(0.15f), new Vector2(0, -5f), new Vector2(7f, -4f), new Vector2(7f, 4f), new Vector2(0, 5f));
                Poly(ci, p, ang + flap, new Color(0.96f, 0.94f, 0.88f), new Vector2(0, -4f), new Vector2(5.5f, -3.2f), new Vector2(5.5f, 3.2f), new Vector2(0, 4f));
                if (close) for (int i = 0; i < 3; i++) ci.DrawLine(p + R(new Vector2(1f, -2f + i * 2f), ang + flap), p + R(new Vector2(4.5f, -2f + i * 2f), ang + flap), new Color(0.4f, 0.4f, 0.4f, 0.5f), 0.5f);
                break;
            }
            case DriftKind.Bottle:
                Poly(ci, p, ang, new Color(0.55f, 0.80f, 0.70f, 0.7f), new Vector2(-6f, -2.8f), new Vector2(2f, -2.8f), new Vector2(4f, -1.2f), new Vector2(7f, -1.2f), new Vector2(7f, 1.2f), new Vector2(4f, 1.2f), new Vector2(2f, 2.8f), new Vector2(-6f, 2.8f));
                Poly(ci, p, ang, new Color(0.85f, 0.25f, 0.2f), new Vector2(7f, -1.4f), new Vector2(8.5f, -1.4f), new Vector2(8.5f, 1.4f), new Vector2(7f, 1.4f));
                ci.DrawLine(p + R(new Vector2(-5f, -1.6f), ang), p + R(new Vector2(1f, -1.6f), ang), new Color(1, 1, 1, 0.6f), 0.8f);
                break;
            case DriftKind.Plate:
                ci.DrawCircle(p, 6f, new Color(0.94f, 0.94f, 0.92f));
                ci.DrawArc(p, 4.2f, 0f, Mathf.Tau, 16, new Color(0.75f, 0.78f, 0.82f), 0.8f);
                if (close) ci.DrawArc(p, 5.4f, ang, ang + 1.2f, 6, new Color(0.30f, 0.45f, 0.75f), 1f); // 테 무늬
                break;
            case DriftKind.Bowl:
                ci.DrawCircle(p, 5f, new Color(0.85f, 0.80f, 0.70f));
                ci.DrawCircle(p, 3.6f, new Color(0.62f, 0.55f, 0.45f));
                ci.DrawArc(p, 5f, ang, ang + 2f, 8, new Color(1, 1, 1, 0.5f), 1f);
                break;
            case DriftKind.PillJar: // 주황 반투명 통 · 흰 뚜껑 · 알약
                Poly(ci, p, ang, new Color(0.95f, 0.55f, 0.15f, 0.75f), new Vector2(-3f, -3f), new Vector2(3f, -3f), new Vector2(3f, 4f), new Vector2(-3f, 4f));
                Poly(ci, p, ang, new Color(0.96f, 0.96f, 0.96f), new Vector2(-3.4f, -5f), new Vector2(3.4f, -5f), new Vector2(3.4f, -3f), new Vector2(-3.4f, -3f));
                if (close) for (int i = 0; i < 3; i++) ci.DrawCircle(p + R(new Vector2(-1.5f + i * 1.5f, 1.5f), ang), 0.7f, new Color(1, 1, 1, 0.9f));
                break;
            case DriftKind.Pillow: // 푹신한 네모 · 박음질 · 모서리 술
                Poly(ci, p, ang, new Color(0.88f, 0.88f, 0.95f), new Vector2(-7f, -4.5f), new Vector2(0, -5.5f), new Vector2(7f, -4.5f), new Vector2(7.5f, 4.5f), new Vector2(0, 5.5f), new Vector2(-7.5f, 4.5f));
                if (close) ci.DrawPolyline(new[] { p + R(new Vector2(-5.5f, -3f), ang), p + R(new Vector2(5.5f, -3f), ang), p + R(new Vector2(5.5f, 3f), ang), p + R(new Vector2(-5.5f, 3f), ang), p + R(new Vector2(-5.5f, -3f), ang) }, new Color(0.6f, 0.6f, 0.75f), 0.5f);
                break;
            case DriftKind.Pot: // 흙 화분 · 흩날리는 흙 · 잎
                ci.DrawCircle(p, 5f, Terra);
                ci.DrawCircle(p, 3.6f, Soil);
                for (int i = 0; i < 4; i++) { float a = ang + i * 1.57f; ci.DrawLine(p, p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 8f, new Color(0.25f, 0.6f, 0.3f), 2f); }
                for (int i = 0; i < 5; i++) ci.DrawCircle(p + new Vector2(Mathf.Cos(t + i * 1.2f) * (8f + i), Mathf.Sin(t * 0.8f + i) * (7f + i)), 0.8f, Soil);
                break;
            case DriftKind.Droplet: // 출렁이는 물방울 · 빛
            {
                float r = 2.5f + 4f * Mathf.Sqrt(Mathf.Max(0.05f, liters));
                Blob(ci, p, r, new Color(0.45f, 0.70f, 0.95f, 0.6f), 0.12f, t, id);
                ci.DrawCircle(p + new Vector2(-r * 0.35f, -r * 0.35f), r * 0.25f, new Color(1, 1, 1, 0.7f));
                break;
            }
            case DriftKind.Sick:
                Blob(ci, p, 4.5f, new Color(0.72f, 0.70f, 0.30f, 0.75f), 0.2f, t, id);
                for (int i = 0; i < 3; i++) ci.DrawCircle(p + new Vector2(Mathf.Cos(i * 2f + t) * 2f, Mathf.Sin(i * 2f + t) * 2f), 0.9f, new Color(0.55f, 0.40f, 0.20f));
                break;
            default: // 짐 꾸러미 · 테이프 십자
                Poly(ci, p, ang, Color.FromHsv(0.08f + 0.1f * h, 0.45f, 0.70f), new Vector2(-5f, -4f), new Vector2(5f, -4f), new Vector2(5f, 4f), new Vector2(-5f, 4f));
                ci.DrawLine(p + R(new Vector2(-5f, 0), ang), p + R(new Vector2(5f, 0), ang), new Color(0.9f, 0.85f, 0.55f), 1.4f);
                ci.DrawLine(p + R(new Vector2(0, -4f), ang), p + R(new Vector2(0, 4f), ang), new Color(0.9f, 0.85f, 0.55f), 1.4f);
                break;
        }
    }

    // ── 손잡이 · 떠 있는 몸 · 멀미 ──
    public static void Grip(CanvasItem ci, Vector2 at, Vector2 wall, bool close)
    {
        var bar = at + wall * 13f;
        var side = new Vector2(-wall.Y, wall.X);
        ci.DrawLine(bar - side * 5f, bar + side * 5f, new Color(0.95f, 0.75f, 0.15f), 2.2f); // 노란 벽 손잡이
        if (close) { ci.DrawCircle(bar - side * 5f, 1.2f, SteelDark); ci.DrawCircle(bar + side * 5f, 1.2f, SteelDark); }
        ci.DrawLine(at + wall * 4f, bar, Skin.Darkened(0.1f), 2f); // 뻗은 팔
        ci.DrawCircle(bar, 2f, Skin); // 쥔 손
    }

    public static void Adrift(CanvasItem ci, Vector2 at, int id, float t, bool sleeper)
    {
        float s = Mathf.Sin(t * 1.1f + id);
        ci.DrawArc(at + new Vector2(0, 2f), 12f + s, 0f, Mathf.Tau, 20, new Color(0.7f, 0.85f, 1f, 0.22f), 1f);
        for (int i = 0; i < 2; i++) ci.DrawLine(at + new Vector2(-14f - i * 3f, 4f + s * 2f), at + new Vector2(-10f - i * 3f, 4f + s * 2f), new Color(0.8f, 0.9f, 1f, 0.35f), 1f);
        if (sleeper) Poly(ci, at, Mathf.Sin(t * 0.5f + id) * 0.3f, new Color(0.45f, 0.55f, 0.75f, 0.55f), new Vector2(-9f, -5f), new Vector2(9f, -5f), new Vector2(10f, 6f), new Vector2(-10f, 6f)); // 둥둥 뜬 이불
    }

    public static void Queasy(CanvasItem ci, Vector2 at, float q, float t)
    {
        var c = new Color(0.55f, 0.80f, 0.35f, 0.4f + 0.5f * q);
        for (int i = 0; i < 3; i++) ci.DrawArc(at, 2f + i * 1.8f, t * 3f + i * 1.4f, t * 3f + i * 1.4f + 3.6f, 8, c, 1f);
    }

    // ── 고양이 ──
    public static void Cat(CanvasItem ci, Vector2 at, Vector2 face, CatState st, bool hidden, bool covered, int coat, float t, bool purr, bool meow, bool close)
    {
        Color fur = (coat % 4) switch { 0 => new Color(0.92f, 0.58f, 0.22f), 1 => new Color(0.16f, 0.15f, 0.17f), 2 => new Color(0.62f, 0.62f, 0.64f), _ => new Color(0.95f, 0.92f, 0.86f) };
        Color stripe = fur.Darkened(0.35f), eye = new Color(0.85f, 0.95f, 0.35f);
        if (face == Vector2.Zero) face = new Vector2(1, 0);
        float ang = face.Angle();
        if (st == CatState.Dead)
        {
            if (covered) { Poly(ci, at, 0.2f, new Color(0.55f, 0.62f, 0.78f), new Vector2(-9f, -5f), new Vector2(8f, -6f), new Vector2(10f, 5f), new Vector2(-8f, 6f)); ci.DrawLine(at + new Vector2(-7f, 0), at + new Vector2(8f, -1f), new Color(0.45f, 0.5f, 0.65f), 0.8f); return; }
            Poly(ci, at, 0f, fur.Darkened(0.3f), new Vector2(-7f, -3f), new Vector2(6f, -3f), new Vector2(6f, 3f), new Vector2(-7f, 3f));
            ci.DrawCircle(at + new Vector2(8f, 0), 3.2f, fur.Darkened(0.3f));
            return;
        }
        if (hidden && st == CatState.Hide) // 가구 밑 어둠 속 두 눈 · 꼬리 끝
        {
            ci.DrawCircle(at, 6f, new Color(0, 0, 0, 0.45f));
            float blink = Mathf.Sin(t * 0.7f) > 0.95f ? 0.2f : 1f;
            ci.DrawCircle(at + new Vector2(-1.8f, -0.5f), 1.1f * blink, eye);
            ci.DrawCircle(at + new Vector2(1.8f, -0.5f), 1.1f * blink, eye);
            ci.DrawArc(at + new Vector2(6f, 4f), 3f, 0f, 2f + Mathf.Sin(t * 2f) * 0.5f, 6, fur, 1.6f);
            return;
        }
        if (st == CatState.Nap) // 동그랗게 말린 몸 · 꼬리로 감싼다 · 숨쉬기
        {
            float br = 1f + 0.05f * Mathf.Sin(t * 1.6f);
            ci.DrawCircle(at, 6.5f * br, fur);
            ci.DrawArc(at, 6.8f * br, 0.4f, 3.6f, 10, fur.Darkened(0.15f), 2.2f);
            if (close) for (int i = 0; i < 3; i++) ci.DrawArc(at, 3f + i * 1.3f, -0.6f + i * 0.3f, 0.2f + i * 0.3f, 4, stripe, 0.8f);
            ci.DrawCircle(at + new Vector2(3.5f, -2.5f), 3f, fur);
            Poly(ci, at + new Vector2(3.5f, -2.5f), 0f, fur, new Vector2(0.5f, -2.5f), new Vector2(2.2f, -4.8f), new Vector2(2.8f, -1.8f));
            if (purr) for (int i = 0; i < 2; i++) ci.DrawArc(at, 9f + i * 2.5f, -0.5f, 0.5f, 5, new Color(1, 0.9f, 0.6f, 0.4f), 0.8f);
            return;
        }
        bool flail = st == CatState.Float;
        float step = flail ? Mathf.Sin(t * 12f) : st is CatState.Roam or CatState.Beg ? Mathf.Sin(t * 8f) : 0f;
        // 다리
        for (int i = 0; i < 4; i++)
        {
            var hip = new Vector2(i < 2 ? 4f : -4f, i % 2 == 0 ? -2.5f : 2.5f);
            var foot = flail ? hip * 1.9f + new Vector2(step * 1.5f * (i % 2 == 0 ? 1 : -1), step * 1.2f) : hip + new Vector2(step * 1.6f * (i % 2 == 0 ? 1 : -1), (i % 2 == 0 ? -1.5f : 1.5f));
            ci.DrawLine(at + R(hip, ang), at + R(foot, ang), fur.Darkened(0.1f), 1.6f);
        }
        // 몸 · 줄무늬
        Poly(ci, at, ang, fur, new Vector2(-6f, -3f), new Vector2(4f, -3.2f), new Vector2(6f, 0), new Vector2(4f, 3.2f), new Vector2(-6f, 3f), new Vector2(-7f, 0));
        if (close && coat % 4 == 0) for (int i = 0; i < 3; i++) ci.DrawLine(at + R(new Vector2(-4f + i * 2.5f, -3f), ang), at + R(new Vector2(-4.5f + i * 2.5f, 3f), ang), stripe, 0.9f);
        // 꼬리 (걸으면 세우고 · 조를 땐 흔들고 · 허우적이면 휘두른다)
        float wag = st == CatState.Beg ? Mathf.Sin(t * 4f) * 0.6f : flail ? Mathf.Sin(t * 9f) : 0.25f;
        var tb = at + R(new Vector2(-7f, 0), ang);
        ci.DrawPolyline(new[] { tb, tb + R(new Vector2(-4f, wag * 3f), ang), tb + R(new Vector2(-7f, wag * 6f - 2f), ang) }, fur.Darkened(0.05f), 1.8f);
        // 머리 · 귀 · 눈
        var hd = at + R(new Vector2(st == CatState.Eat ? 8.5f : 7.5f, 0), ang);
        ci.DrawCircle(hd, 3.4f, fur);
        Poly(ci, hd, ang, fur, new Vector2(0.5f, -2.5f), new Vector2(1.5f, -5.2f), new Vector2(3f, -2f));
        Poly(ci, hd, ang, fur, new Vector2(0.5f, 2.5f), new Vector2(1.5f, 5.2f), new Vector2(3f, 2f));
        if (close)
        {
            ci.DrawCircle(hd + R(new Vector2(1.6f, -1.2f), ang), 0.7f, flail ? new Color(1, 1, 1) : eye);
            ci.DrawCircle(hd + R(new Vector2(1.6f, 1.2f), ang), 0.7f, flail ? new Color(1, 1, 1) : eye);
            for (int s = -1; s <= 1; s += 2) ci.DrawLine(hd + R(new Vector2(3f, s * 1f), ang), hd + R(new Vector2(5.5f, s * 2.2f), ang), new Color(1, 1, 1, 0.6f), 0.4f); // 수염
        }
        if (meow) for (int i = 0; i < 2; i++) ci.DrawArc(hd + R(new Vector2(4f, 0), ang), 2.5f + i * 2f, ang - 0.6f, ang + 0.6f, 5, new Color(1, 1, 1, 0.6f - i * 0.2f), 0.9f);
        if (purr && st != CatState.Float) ci.DrawArc(at, 10f, ang + 2.4f, ang + 3.9f, 5, new Color(1, 0.9f, 0.6f, 0.4f), 0.8f);
    }
}
