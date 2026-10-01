using System;
using System.Collections.Generic;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v16.8 음식 · 냄새 보기 (읽기만 — 시뮬레이션을 바꾸지 않는다).
// 화구 위 냄비는 요리 갈래마다 그릇이 다르다: 국 냄비(손잡이 둘 · 맑은 국물 건더기) · 뚝배기(두꺼운 흙 테 · 붉게 끓는 거품) · 웍(긴 자루 · 튀는 밥알) ·
// 면 냄비(물결 면발) · 죽 냄비(느린 거품) · 오븐 판(칼집 낸 빵 덩이) · 케이크(조각 선) · 프라이팬(지글거리는 전) · 찜기(대나무 결 · 주름 만두) ·
// 항아리(옹기 · 익는 동안 뽀글) · 유리병(절임 조각). 끓으면 김이 오르고 뚜껑이 들썩, 식으면 김이 잦아들고 막이 앉고, 상하면 곰팡이 · 파리,
// 불 위에 남아 타면 바닥이 검어지고 검은 연기 · 불씨. 식탁의 남겨 둔 접시엔 이름표(받을 사람 색 띠 · 확대하면 이름).
// 냄새는 종류마다 다르게 번진다: 빵(금빛 리본이 느리게 감아 오른다) · 요리(주황 짧은 김 가닥) · 탄내(잿빛 연기 덩이 · 그을음 알갱이) · 악취(초록 얼룩 · 지그재그 · 파리) ·
// 커피(갈색 나선이 감겨 오르고 원두 알갱이가 떠다닌다).
// 균 든 냄비는 확대하면 테두리에 꿈틀대는 초록 막대균 · 주컴퓨터가 "먼저 쓰라"고 한 냄비엔 호박색 화면 쪽지(받아들임 — 깜빡이는 점 · 흘려들음 — 구겨진 회색) ·
// 컴퓨터가 화구 때문에 사람을 부르면 화구 위에 하늘색 감시 눈(맥박 고리 · 주사선) · 히터 앞에서 데운 그릇은 아래로 주황 열 물결.
public partial class ShipView
{
    private static readonly Color FogBread = new("#f2c46d"), FogCook = new("#f08a4b"), FogBurnt = new("#7a6d64"), FogFoul = new("#97b83c"), FogCoffee = new("#8b5a3c");
    private static readonly Color FdSteel = new("#9aa5b1"), FdSteelDark = new("#5d6672"), FdClay = new("#4a3328"), FdClayRim = new("#6e4c3a"),
        FdIron = new("#2b2d33"), FdBamboo = new("#c9a66b"), FdBambooDark = new("#8f7146"), FdOnggi = new("#5a3a26"), FdGlass = new("#cfe8ef"), FdPaper = new("#f5ebcf");

    private void PaintFood(CanvasItem ci, ViewMode mode)
    {
        var w = _world;
        bool strong = mode == ViewMode.Ambience || _main.SecondaryView == ViewMode.Ambience;
        PaintSmellFog(ci, strong);
        float z = Zoom;
        bool fine = z > 1.15f;
        var ck = w.Cooking;
        var used = new HashSet<int>();
        // 1) 화구: 끓는 냄비 · 불 위에 남아 타는 냄비
        foreach (var st in w.Ship.FurnitureOf(FurnitureType.Stove))
        {
            var c = FurnitureRect(st).GetCenter();
            float scorch = ck.Scorch(st);
            if (scorch > 0f)
            {
                DrawBurningPot(ci, c, 10f, scorch, st.Id, fine);
                if (ck.Watch.Calling(st)) DrawWatchEye(ci, c + new Vector2(0f, -16f), st.Id, fine);
                used.Add(st.Id);
                continue;
            }
            if (ck.CookingAt(st) is DishRecipe r)
            {
                DrawBurnerGlow(ci, c, 12f, 1f);
                DrawVessel(ci, r, c, 10f, 1f, true, false, fine, st.Id);
                used.Add(st.Id);
            }
        }
        // 2) 냄비 묶음: 화구 곁에 놓인 냄비 · 냉장고의 보관 통 · 항아리
        var slot = new Dictionary<int, int>();
        foreach (var b in ck.Batches)
        {
            if (b.Stove == null || b.Room == null || b.Room.Detached) continue;
            var spec = b.Spec;
            int k = slot.TryGetValue(b.Stove.Id, out var n) ? n : 0;
            slot[b.Stove.Id] = k + 1;
            var box = FurnitureRect(b.Stove);
            float heat = Mathf.Clamp((b.Temp - 30f) / 55f, 0f, 1f);
            if (b.Jar)
            {
                var at = new Vector2(box.End.X + 7f + 11f * (k % 3), box.End.Y - 8f - 14f * (k / 3));
                float prog = b.Ready(w.Tick) ? 1f : Mathf.Clamp((w.Tick - b.Cooked) / (float)Math.Max(1, b.ReadyAt - b.Cooked), 0f, 1f);
                DrawJar(ci, spec, at, 7f, prog, b.Spoiled, b.Portions / (float)Math.Max(1, b.Made), fine, b.Id);
                if (b.Germy && z > 1.6f) DrawGerms(ci, at, 7f, b.Id);
            }
            else if (b.InFridge)
            {
                var at = new Vector2(box.Position.X - 7f - 10f * (k % 2), box.Position.Y + 8f + 9f * (k / 2));
                DrawTub(ci, spec, at, 6f, b.Spoiled, fine, b.Id);
                if (b.Germy && z > 1.6f) DrawGerms(ci, at, 6f, b.Id);
                if (b.Flagged || b.FlagIgnored) DrawComputerNote(ci, at + new Vector2(-6f, -9f), b.Flagged, b.Id, fine);
            }
            else
            {
                bool onBurner = used.Contains(b.Stove.Id);
                var at = onBurner || k > 0 ? new Vector2(box.End.X + 6f + 14f * k, box.GetCenter().Y + (k % 2 == 0 ? -5f : 6f)) : box.GetCenter();
                float s = onBurner || k > 0 ? 7f : 10f;
                DrawVessel(ci, spec, at, s, heat, false, b.Spoiled, fine, b.Id);
                if (fine && z > 1.8f) PortionPips(ci, at + new Vector2(0f, s + 4f), b.Portions, b.Made);
                if (b.Germy && z > 1.6f) DrawGerms(ci, at, s, b.Id);
                if (b.Flagged || b.FlagIgnored) DrawComputerNote(ci, at + new Vector2(s + 2f, -s - 3f), b.Flagged, b.Id, fine);
            }
        }
        // 3) 남겨 둔 접시 (이름표)
        var plateSlot = new Dictionary<int, int>();
        foreach (var p in ck.Plates)
        {
            int k = plateSlot.TryGetValue(p.Table.Id, out var n) ? n : 0;
            plateSlot[p.Table.Id] = k + 1;
            var box = FurnitureRect(p.Table);
            var at = box.Position + new Vector2(9f + 15f * (k % Math.Max(1, (int)(box.Size.X / 15f))), 9f + 14f * (k / Math.Max(1, (int)(box.Size.X / 15f))));
            DrawSavedPlate(ci, p, at, 6.5f, fine, z > 2f || _main.SelectedRoom == p.Table.Room);
        }
        // 4) 먹는 사람의 그릇 (김 · 식었으면 김 없이)
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Pose != Pose.Sitting || c.Job?.Activity is not EatActivity || ck.EatingNow(c) is not DishRecipe r) continue;
            var pos = ToPx(c.Position) + new Vector2(0f, -CrewRadius - 3f);
            DrawBowl(ci, r, pos, 4f, ck.EatingCold(c) ? 0f : 0.8f, c.Id);
            if (ck.EatingByHeater(c)) HeatRipple(ci, pos + new Vector2(0f, 5f), c.Id);
        }
    }

    // ── 그리기 도구 ──

    private static Vector2[] Ell(Vector2 c, float rx, float ry, int n = 18, float rot = 0f)
    {
        var pts = new Vector2[n];
        float cr = Mathf.Cos(rot), sr = Mathf.Sin(rot);
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.Tau / n;
            float x = Mathf.Cos(a) * rx, y = Mathf.Sin(a) * ry;
            pts[i] = c + new Vector2(x * cr - y * sr, x * sr + y * cr);
        }
        return pts;
    }

    private static float Hash01(int a, int b) => Mathf.PosMod(Mathf.Sin(a * 12.9898f + b * 78.233f) * 43758.547f, 1f);

    /// <summary>요리마다의 색 (국물 · 빵 · 소스).</summary>
    private static Color FoodColor(DishRecipe r) => r.Id switch
    {
        "vegsoup" => new Color("#d9a441"), "doenjang" => new Color("#a2703b"), "seaweed" => new Color("#35563a"), "beansprout" => new Color("#e6dfb4"),
        "leftsoup" => new Color("#b98b52"), "kimchistew" => new Color("#c0392b"), "budae" => new Color("#d35400"), "stew" => new Color("#8e5a2b"),
        "curry" => new Color("#d4a017"), "sujebi" => new Color("#e6d3a3"), "ramen" => new Color("#e09a3a"), "porridge" => new Color("#efe6cf"),
        "friedrice" => new Color("#e8c170"), "bibim" => new Color("#c7623a"), "chazuke" => new Color("#c9d68a"), "japchae" => new Color("#6b4b3a"),
        "dumpling" => new Color("#f3eadb"), "bread" => new Color("#c68642"), "barleybread" => new Color("#8b5a2b"), "pancake" => new Color("#d9b35b"),
        "hotcake" => new Color("#e3b36a"), "coffeecake" => new Color("#6f4e37"), "tteok" => new Color("#f5f0e6"), "kimchi" => new Color("#c8412f"),
        "pickle" => new Color("#8fbf4a"), "sikhye" => new Color("#efe2b8"), _ => new Color("#d0a060"),
    };

    /// <summary>김: S자로 흔들리며 오르는 가닥 (뜨거울수록 많고 진하다).</summary>
    private void Steam(CanvasItem ci, Vector2 top, float heat, int seed, float scale = 1f)
    {
        if (heat <= 0.05f) return;
        int n = 1 + (int)(heat * 3f);
        for (int i = 0; i < n; i++)
        {
            float t = Mathf.PosMod(_time * (0.35f + 0.15f * heat) + i / (float)n + Hash01(seed, i), 1f);
            var pts = new Vector2[6];
            float x0 = (i - (n - 1) * 0.5f) * 3.5f * scale;
            for (int j = 0; j < pts.Length; j++)
            {
                float u = j / (float)(pts.Length - 1);
                float y = -(t * 10f + u * 7f) * scale;
                pts[j] = top + new Vector2(x0 + Mathf.Sin(_time * 2.2f + u * 4f + i + seed) * 2.2f * scale * (0.4f + u), y);
            }
            ci.DrawPolyline(pts, new Color(1f, 1f, 1f, 0.42f * heat * (1f - t)), 1.6f * scale, true);
        }
    }

    private void Bubbles(CanvasItem ci, Vector2 c, float r, Color col, int seed, float rate, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float t = Mathf.PosMod(_time * rate + Hash01(seed, i), 1f);
            var p = c + new Vector2((Hash01(seed + 7, i) - 0.5f) * 1.5f * r, (Hash01(seed + 3, i) - 0.5f) * 1.5f * r);
            float br = (0.18f + 0.22f * t) * r;
            ci.DrawArc(p, br, 0f, Mathf.Tau, 10, col.Lightened(0.35f).WithAlpha(0.9f * (1f - t)), 1f, true);
        }
    }

    private void Flies(CanvasItem ci, Vector2 c, float r, int seed)
    {
        for (int i = 0; i < 3; i++)
        {
            float a = _time * (3.1f + i) + i * 2.1f + seed;
            var p = c + new Vector2(Mathf.Sin(a) * r, Mathf.Sin(a * 2f) * r * 0.5f - r * 0.6f); // 8자로 맴돈다
            ci.DrawCircle(p, 1.1f, new Color(0.08f, 0.08f, 0.08f, 0.9f), true, -1f, true);
            ci.DrawLine(p + new Vector2(-1.4f, -0.8f), p + new Vector2(1.4f, -0.8f), new Color(0.85f, 0.9f, 1f, 0.5f * Mathf.Abs(Mathf.Sin(_time * 40f + i))), 0.8f);
        }
    }

    private void Stink(CanvasItem ci, Vector2 c, float h, float a, int seed)
    {
        for (int i = 0; i < 2; i++)
        {
            var pts = new Vector2[5];
            float x = c.X + (i == 0 ? -3f : 3f);
            for (int j = 0; j < 5; j++)
                pts[j] = new Vector2(x + ((j & 1) == 0 ? -1.5f : 1.5f) + Mathf.Sin(_time * 3f + seed + i) * 0.8f, c.Y - j * h / 4f);
            ci.DrawPolyline(pts, FogFoul.WithAlpha(0.8f * a), 1.2f, true);
        }
    }

    private void DrawBurnerGlow(CanvasItem ci, Vector2 c, float r, float power)
    {
        float pulse = 0.75f + 0.25f * Mathf.Sin(_time * 7f);
        ci.DrawCircle(c, r, new Color(1f, 0.45f, 0.15f, 0.22f * power * pulse), true, -1f, true);
        for (int i = 0; i < 10; i++) // 화구 불꽃 고리
        {
            var d = Vector2.FromAngle(i * Mathf.Tau / 10f);
            ci.DrawLine(c + d * (r - 2f), c + d * (r + 0.5f + 1.5f * Mathf.Abs(Mathf.Sin(_time * 11f + i))), new Color(0.35f, 0.6f, 1f, 0.7f * power), 1.2f, true);
        }
    }

    // ── 그릇 (요리 갈래마다 실루엣이 다르다) ──

    private void DrawVessel(CanvasItem ci, DishRecipe r, Vector2 c, float s, float heat, bool cooking, bool spoiled, bool fine, int seed)
    {
        var food = FoodColor(r);
        if (spoiled) food = food.Lerp(new Color("#6f7d3a"), 0.6f);
        else if (heat < 0.15f) food = food.Darkened(0.18f); // 식으면 탁해진다
        var shadow = new Color(0f, 0f, 0f, 0.3f);
        ci.DrawCircle(c + new Vector2(1.5f, 2f), s * 1.05f, shadow, true, -1f, true);
        switch (r.Kind)
        {
            case DishKind.Soup or DishKind.Noodle or DishKind.Porridge:
            {
                // 국 냄비: 강철 몸통 · 밝은 테 · 양쪽 손잡이
                ci.DrawRect(new Rect2(c.X - s * 1.35f, c.Y - s * 0.18f, s * 0.35f, s * 0.36f), FdSteelDark);
                ci.DrawRect(new Rect2(c.X + s, c.Y - s * 0.18f, s * 0.35f, s * 0.36f), FdSteelDark);
                ci.DrawCircle(c, s, FdSteel, true, -1f, true);
                ci.DrawArc(c, s, 0f, Mathf.Tau, 24, FdSteel.Lightened(0.35f), 1.2f, true);
                ci.DrawCircle(c, s * 0.8f, food, true, -1f, true);
                if (r.Kind == DishKind.Noodle)
                    for (int i = 0; i < 3; i++) // 면발 물결
                    {
                        var pts = new Vector2[7];
                        for (int j = 0; j < 7; j++) pts[j] = c + new Vector2((j - 3) * s * 0.2f, (i - 1) * s * 0.28f + Mathf.Sin(j * 1.6f + i + (cooking ? _time * 4f : 0f)) * s * 0.08f);
                        ci.DrawPolyline(pts, new Color("#f4e3a1"), 1.2f, true);
                    }
                else if (r.Kind == DishKind.Porridge)
                    ci.DrawArc(c, s * 0.45f, 0.4f, 2.6f, 10, food.Lightened(0.3f), 1f, true); // 저은 자국
                else
                    for (int i = 0; i < 5; i++) // 건더기
                        ci.DrawCircle(c + new Vector2((Hash01(seed, i) - 0.5f) * s, (Hash01(seed, i + 9) - 0.5f) * s), s * 0.11f,
                            i % 2 == 0 ? new Color("#4e9a3a") : new Color("#e07a3a"), true, -1f, true);
                if (cooking && r.Kind == DishKind.Soup && fine)
                {
                    // 뚜껑이 반쯤 걸쳐 들썩인다
                    float lift = Mathf.Abs(Mathf.Sin(_time * 9f)) * 1.2f;
                    ci.DrawArc(c + new Vector2(s * 0.3f, -lift), s * 0.95f, -1.9f, 0.4f, 14, FdSteel.Lightened(0.2f), 2.2f, true);
                    ci.DrawCircle(c + new Vector2(s * 0.55f, -s * 0.45f - lift), s * 0.12f, FdSteelDark, true, -1f, true);
                }
                if (cooking || heat > 0.75f) Bubbles(ci, c, s * 0.7f, food, seed, r.Kind == DishKind.Porridge ? 0.5f : 1.4f, r.Kind == DishKind.Porridge ? 2 : 4);
                else if (heat < 0.15f && fine) ci.DrawArc(c, s * 0.55f, 0.2f, 2.2f, 10, new Color(1f, 1f, 1f, 0.18f), 1f, true); // 식은 막
                break;
            }
            case DishKind.Stew:
            {
                // 뚝배기: 두꺼운 흙 테 · 받침 · 붉게 끓는 거품
                ci.DrawCircle(c, s * 1.12f, FdClay.Darkened(0.3f), true, -1f, true);
                ci.DrawCircle(c, s, FdClay, true, -1f, true);
                ci.DrawArc(c, s * 0.92f, 0f, Mathf.Tau, 24, FdClayRim, 2.2f, true);
                ci.DrawCircle(c, s * 0.74f, food, true, -1f, true);
                if (fine) for (int i = 0; i < 3; i++) ci.DrawRect(new Rect2(c + new Vector2((i - 1) * s * 0.35f - s * 0.1f, (Hash01(seed, i) - 0.5f) * s * 0.6f), new Vector2(s * 0.2f, s * 0.16f)), new Color("#f2e6c9")); // 두부
                if (cooking || heat > 0.6f) Bubbles(ci, c, s * 0.6f, food, seed, 1.9f, 5);
                break;
            }
            case DishKind.Rice:
            {
                // 웍: 넓고 얕은 검은 쇠 · 긴 자루 · 튀는 밥알
                ci.DrawLine(c + new Vector2(s * 1.1f, 0f), c + new Vector2(s * 2.1f, -s * 0.25f), new Color("#3b2a22"), 2.4f, true);
                ci.DrawCircle(c, s * 1.15f, FdIron, true, -1f, true);
                ci.DrawArc(c, s * 1.15f, 0f, Mathf.Tau, 24, FdIron.Lightened(0.25f), 1f, true);
                ci.DrawCircle(c, s * 0.82f, food, true, -1f, true);
                if (r.Id == "bibim")
                    for (int i = 0; i < 5; i++) // 비빔밥 고명 다섯 갈래
                        ci.DrawColoredPolygon(new[] { c, c + Vector2.FromAngle(i * Mathf.Tau / 5f) * s * 0.8f, c + Vector2.FromAngle((i + 1) * Mathf.Tau / 5f) * s * 0.8f },
                            new[] { new Color("#3f8f3a"), new Color("#e8c13a"), new Color("#c0392b"), new Color("#7a4a2a"), new Color("#f2efe6") }[i]);
                for (int i = 0; i < 7; i++)
                {
                    float hop = cooking ? Mathf.Max(0f, Mathf.Sin(_time * 8f + i * 1.3f)) * s * 0.5f : 0f;
                    ci.DrawCircle(c + new Vector2((Hash01(seed, i) - 0.5f) * s * 1.2f, (Hash01(seed + 5, i) - 0.5f) * s * 1.2f - hop), 0.9f, new Color("#fbf6e9"), true, -1f, true);
                }
                break;
            }
            case DishKind.Pan:
            {
                // 프라이팬: 검은 원판 · 자루 · 노릇한 전 · 지글거리는 기름 튐
                ci.DrawLine(c + new Vector2(-s * 1.05f, 0f), c + new Vector2(-s * 2.2f, s * 0.2f), new Color("#3a3a40"), 2.6f, true);
                ci.DrawCircle(c, s * 1.05f, FdIron, true, -1f, true);
                ci.DrawCircle(c, s * 0.82f, food, true, -1f, true);
                for (int i = 0; i < 6; i++) ci.DrawCircle(c + new Vector2((Hash01(seed, i) - 0.5f) * s * 1.1f, (Hash01(seed, i + 4) - 0.5f) * s * 1.1f), s * 0.09f, food.Darkened(0.35f), true, -1f, true);
                if (r.Id == "pancake") for (int i = 0; i < 3; i++) ci.DrawLine(c + new Vector2(-s * 0.4f, (i - 1) * s * 0.3f), c + new Vector2(s * 0.4f, (i - 1) * s * 0.3f + 0.6f), new Color("#4e9a3a"), 1.2f); // 부추
                if (cooking)
                    for (int i = 0; i < 4; i++)
                    {
                        float t = Mathf.PosMod(_time * 2.5f + i * 0.27f, 1f);
                        var d = Vector2.FromAngle(i * 1.7f + 0.3f);
                        ci.DrawCircle(c + d * s * (0.8f + 0.6f * t), 0.8f, new Color(1f, 0.95f, 0.6f, 1f - t), true, -1f, true);
                    }
                break;
            }
            case DishKind.Dumpling:
            {
                // 찜기: 대나무 결 · 찌는 동안 뚜껑 · 열면 주름 만두
                ci.DrawCircle(c, s * 1.1f, FdBambooDark, true, -1f, true);
                ci.DrawCircle(c, s, FdBamboo, true, -1f, true);
                if (cooking)
                {
                    for (int i = -3; i <= 3; i++) ci.DrawLine(c + new Vector2(i * s * 0.26f, -s * 0.9f * Mathf.Cos(i * 0.4f)), c + new Vector2(i * s * 0.26f, s * 0.9f * Mathf.Cos(i * 0.4f)), FdBambooDark, 0.8f);
                    ci.DrawArc(c, s * 0.5f, 0f, Mathf.Tau, 16, FdBambooDark, 0.8f, true);
                }
                else
                    for (int i = 0; i < 4; i++)
                    {
                        var p = c + Vector2.FromAngle(i * Mathf.Tau / 4f + 0.6f) * s * 0.45f;
                        ci.DrawColoredPolygon(Ell(p, s * 0.32f, s * 0.22f, 12, i * 0.8f), food);
                        if (fine) for (int j = -1; j <= 1; j++) ci.DrawLine(p + new Vector2(j * s * 0.1f, -s * 0.12f), p + new Vector2(j * s * 0.12f, s * 0.02f), food.Darkened(0.25f), 0.7f); // 주름
                    }
                break;
            }
            case DishKind.Bread or DishKind.Cake:
            {
                // 오븐 판: 둥근 모서리 쇠판 · 빵은 칼집 낸 타원, 케이크는 둥글게 조각 선
                Gfx.RoundRect(ci, new Rect2(c - new Vector2(s * 1.3f, s * 0.95f), new Vector2(s * 2.6f, s * 1.9f)), FdSteelDark, 2f, FdSteel, 1);
                if (r.Kind == DishKind.Bread)
                    for (int i = 0; i < 3; i++)
                    {
                        var p = c + new Vector2((i - 1) * s * 0.8f, 0f);
                        ci.DrawColoredPolygon(Ell(p, s * 0.34f, s * 0.7f, 14), food);
                        ci.DrawColoredPolygon(Ell(p + new Vector2(-s * 0.06f, -s * 0.1f), s * 0.2f, s * 0.45f, 10), food.Lightened(0.18f));
                        for (int j = -1; j <= 1; j++) ci.DrawLine(p + new Vector2(-s * 0.18f, j * s * 0.3f - s * 0.08f), p + new Vector2(s * 0.18f, j * s * 0.3f + s * 0.08f), food.Darkened(0.35f), 1f, true); // 칼집
                    }
                else
                {
                    ci.DrawCircle(c, s * 0.8f, food, true, -1f, true);
                    ci.DrawArc(c, s * 0.8f, 0f, Mathf.Tau, 20, food.Lightened(0.25f), 1.2f, true);
                    for (int i = 0; i < 6; i++) ci.DrawLine(c, c + Vector2.FromAngle(i * Mathf.Tau / 6f) * s * 0.8f, food.Darkened(0.3f), 0.8f, true);
                    if (r.Id == "tteok") for (int i = 0; i < 4; i++) ci.DrawCircle(c + Vector2.FromAngle(i * 1.57f + 0.4f) * s * 0.45f, s * 0.12f, new Color("#f2a6b8"), true, -1f, true);
                }
                if (cooking) ci.DrawRect(new Rect2(c - new Vector2(s * 1.3f, s * 0.95f), new Vector2(s * 2.6f, s * 1.9f)), new Color(1f, 0.6f, 0.2f, 0.12f + 0.08f * Mathf.Sin(_time * 3f))); // 오븐의 열기
                break;
            }
            default:
                DrawJar(ci, r, c, s * 0.8f, 1f, spoiled, 1f, fine, seed);
                break;
        }
        if (spoiled)
        {
            for (int i = 0; i < 5; i++) ci.DrawCircle(c + new Vector2((Hash01(seed, i + 20) - 0.5f) * s, (Hash01(seed, i + 30) - 0.5f) * s), s * 0.13f, new Color("#d8e6c0").WithAlpha(0.8f), true, -1f, true); // 곰팡이
            Flies(ci, c, s * 1.2f, seed);
            Stink(ci, c + new Vector2(0f, -s), s * 1.2f, 1f, seed);
        }
        else Steam(ci, c + new Vector2(0f, -s * 0.7f), cooking ? 1f : heat, seed, s / 10f);
    }

    /// <summary>불 위에 남은 냄비: 바닥이 검게 타 들어가고, 붉은 열 · 검은 연기 · 불씨가 튄다.</summary>
    private void DrawBurningPot(CanvasItem ci, Vector2 c, float s, float scorch, int seed, bool fine)
    {
        DrawBurnerGlow(ci, c, s * 1.25f, 1f);
        ci.DrawCircle(c, s * (1.15f + 0.1f * Mathf.Sin(_time * 5f)), new Color(1f, 0.3f, 0.05f, 0.25f + 0.35f * scorch), true, -1f, true);
        ci.DrawCircle(c, s, FdSteel.Lerp(new Color("#1b1714"), scorch), true, -1f, true);
        ci.DrawCircle(c, s * 0.78f, new Color("#3a2416").Lerp(new Color("#0d0b0a"), scorch), true, -1f, true);
        if (fine) for (int i = 0; i < 6; i++) ci.DrawArc(c, s * (0.3f + 0.08f * i), Hash01(seed, i) * 6f, Hash01(seed, i) * 6f + 1.2f, 6, new Color(0f, 0f, 0f, 0.6f), 1f, true); // 눌어붙은 자국
        int puffs = 3 + (int)(scorch * 5f);
        for (int i = 0; i < puffs; i++)
        {
            float t = Mathf.PosMod(_time * 0.45f + i / (float)puffs, 1f);
            var p = c + new Vector2(Mathf.Sin(_time * 1.3f + i * 2.1f) * (3f + 6f * t), -s * 0.6f - t * 26f);
            float pr = (2.5f + 6f * t) * (0.7f + 0.5f * scorch);
            ci.DrawCircle(p, pr, new Color(0.18f, 0.16f, 0.15f, (0.55f + 0.3f * scorch) * (1f - t)), true, -1f, true);
        }
        if (scorch > 0.65f)
            for (int i = 0; i < 4; i++) // 냄비 가에 불꽃이 핀다
            {
                var d = Vector2.FromAngle(i * Mathf.Tau / 4f + _time * 0.7f);
                var b = c + d * s;
                float hgt = 3f + 3f * Mathf.Abs(Mathf.Sin(_time * 13f + i * 3f));
                ci.DrawColoredPolygon(new[] { b - d.Orthogonal() * 1.8f, b + d.Orthogonal() * 1.8f, b + d * hgt }, new Color(1f, 0.55f + 0.3f * Mathf.Sin(_time * 20f + i), 0.1f, 0.9f));
            }
    }

    /// <summary>항아리(옹기) · 유리병: 익는 동안 뽀글, 다 익으면 금빛 둘레, 덜어 낸 만큼 내용물이 준다.</summary>
    private void DrawJar(CanvasItem ci, DishRecipe r, Vector2 c, float s, float progress, bool spoiled, float left, bool fine, int seed)
    {
        var food = spoiled ? new Color("#6f7d3a") : FoodColor(r);
        ci.DrawColoredPolygon(Ell(c + new Vector2(1.5f, s * 1.1f), s * 0.9f, s * 0.25f, 12), new Color(0f, 0f, 0f, 0.3f));
        if (r.Kind == DishKind.Ferment)
        {
            // 옹기: 불룩한 몸 · 좁은 입 · 뚜껑 · 반들한 빛
            var body = new[]
            {
                c + new Vector2(-s * 0.45f, -s), c + new Vector2(s * 0.45f, -s), c + new Vector2(s * 0.95f, -s * 0.35f), c + new Vector2(s, s * 0.3f),
                c + new Vector2(s * 0.6f, s * 1.05f), c + new Vector2(-s * 0.6f, s * 1.05f), c + new Vector2(-s, s * 0.3f), c + new Vector2(-s * 0.95f, -s * 0.35f),
            };
            ci.DrawColoredPolygon(body, FdOnggi);
            ci.DrawPolyline(new[] { body[0], body[7], body[6], body[5], body[4], body[3], body[2], body[1], body[0] }, FdOnggi.Lightened(0.25f), 1f, true);
            ci.DrawArc(c + new Vector2(-s * 0.35f, -s * 0.15f), s * 0.5f, 3.4f, 4.4f, 8, new Color(1f, 1f, 1f, 0.35f), 1.2f, true); // 유약 빛
            if (fine) ci.DrawLine(c + new Vector2(-s * 0.8f, s * 0.2f), c + new Vector2(s * 0.8f, s * 0.2f), FdOnggi.Darkened(0.3f), 1f); // 띠 무늬
            ci.DrawRect(new Rect2(c.X - s * 0.55f, c.Y - s * 1.2f, s * 1.1f, s * 0.28f), FdOnggi.Darkened(0.2f)); // 뚜껑
            ci.DrawCircle(c + new Vector2(0f, -s * 1.3f), s * 0.14f, FdOnggi.Lightened(0.15f), true, -1f, true);
            // 얼마나 남았나: 입구에 비치는 김치 빛
            ci.DrawRect(new Rect2(c.X - s * 0.4f, c.Y - s * 0.98f, s * 0.8f * left, s * 0.12f), food);
        }
        else
        {
            // 유리병: 투명한 몸 · 쇠 뚜껑 띠 · 안의 절임 조각
            var jar = new Rect2(c - new Vector2(s * 0.7f, s), new Vector2(s * 1.4f, s * 2f));
            ci.DrawRect(jar, FdGlass.WithAlpha(0.35f));
            float fill = Mathf.Clamp(left, 0.1f, 1f);
            var liquid = new Rect2(jar.Position.X, jar.End.Y - jar.Size.Y * 0.85f * fill, jar.Size.X, jar.Size.Y * 0.85f * fill);
            ci.DrawRect(liquid, food.WithAlpha(0.55f));
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector2(liquid.Position.X + liquid.Size.X * Hash01(seed, i), liquid.Position.Y + liquid.Size.Y * Hash01(seed, i + 3));
                ci.DrawCircle(p, s * 0.22f, food.Lightened(0.25f), true, -1f, true);
                if (fine) ci.DrawArc(p, s * 0.14f, 0f, Mathf.Tau, 8, food.Darkened(0.3f), 0.7f, true); // 오이 씨 테
            }
            ci.DrawRect(jar, FdGlass.WithAlpha(0.8f), false, 1f);
            ci.DrawRect(new Rect2(jar.Position.X - 1f, jar.Position.Y - s * 0.3f, jar.Size.X + 2f, s * 0.32f), new Color("#c0c7cf"));
            ci.DrawLine(jar.Position + new Vector2(2f, 3f), jar.Position + new Vector2(2f, jar.Size.Y - 3f), new Color(1f, 1f, 1f, 0.45f), 1f); // 유리 빛
        }
        if (spoiled) { Flies(ci, c, s * 1.3f, seed); Stink(ci, c + new Vector2(0f, -s * 1.3f), s, 1f, seed); return; }
        if (progress < 1f)
        {
            // 익는 중: 뚜껑 틈으로 작은 거품이 올라온다 · 진척 고리
            for (int i = 0; i < 3; i++)
            {
                float t = Mathf.PosMod(_time * 0.6f + i / 3f + Hash01(seed, i), 1f);
                ci.DrawArc(c + new Vector2((i - 1) * s * 0.35f, -s * 1.4f - t * s * 1.2f), s * (0.1f + 0.12f * t), 0f, Mathf.Tau, 8, new Color(1f, 0.95f, 0.85f, 0.7f * (1f - t)), 0.8f, true);
            }
            ci.DrawArc(c + new Vector2(s * 1.1f, -s * 1.1f), s * 0.32f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * progress, 12, new Color("#ffd166"), 1.2f, true);
        }
        else ci.DrawArc(c, s * 1.25f, 0f, Mathf.Tau, 20, new Color(1f, 0.85f, 0.4f, 0.35f + 0.15f * Mathf.Sin(_time * 2f + seed)), 1f, true); // 다 익었다
    }

    /// <summary>냉장고의 보관 통: 뚜껑 걸쇠 · 요리 색 띠 (상하면 부풀고 초록).</summary>
    private void DrawTub(CanvasItem ci, DishRecipe r, Vector2 c, float s, bool spoiled, bool fine, int seed)
    {
        var box = new Rect2(c - new Vector2(s, s * 0.7f), new Vector2(s * 2f, s * 1.4f));
        float bulge = spoiled ? 1.2f + 0.4f * Mathf.Sin(_time * 1.5f) : 0f;
        Gfx.RoundRect(ci, box.Grow(bulge), new Color("#dfe7ee").WithAlpha(0.9f), 2f, new Color("#8aa0b4"), 1);
        ci.DrawRect(new Rect2(box.Position.X + 1f, box.Position.Y + box.Size.Y * 0.45f, box.Size.X - 2f, box.Size.Y * 0.5f), (spoiled ? new Color("#6f7d3a") : FoodColor(r)).WithAlpha(0.8f));
        ci.DrawRect(new Rect2(box.Position.X - 0.5f, box.Position.Y - 1.2f, box.Size.X + 1f, 2f), new Color("#4f86c6")); // 뚜껑
        if (fine) { ci.DrawRect(new Rect2(box.Position.X + 1f, box.Position.Y - 2f, 1.5f, 3f), new Color("#2f5d8c")); ci.DrawRect(new Rect2(box.End.X - 2.5f, box.Position.Y - 2f, 1.5f, 3f), new Color("#2f5d8c")); } // 걸쇠
        ci.DrawCircle(c + new Vector2(s * 0.9f, -s * 0.9f), 1.4f, new Color(0.7f, 0.9f, 1f, 0.6f + 0.3f * Mathf.Sin(_time * 1.2f + seed)), true, -1f, true); // 서리
        if (spoiled) Flies(ci, c, s * 1.4f, seed);
    }

    /// <summary>남은 그릇 수 (작은 점).</summary>
    private static void PortionPips(CanvasItem ci, Vector2 c, int left, int made)
    {
        int n = Math.Min(made, 12);
        for (int i = 0; i < n; i++)
            ci.DrawCircle(c + new Vector2((i - (n - 1) * 0.5f) * 2.6f, 0f), 0.9f, i < left ? new Color("#f2b134") : new Color(1f, 1f, 1f, 0.2f), true, -1f, true);
    }

    /// <summary>남겨 둔 접시: 흰 접시 · 요리 · 덮은 랩의 빛 · 받을 사람 색 띠 이름표 (확대하면 이름) · 따뜻하면 김.</summary>
    private void DrawSavedPlate(CanvasItem ci, Plate p, Vector2 c, float s, bool fine, bool name)
    {
        var r = p.Spec;
        float heat = Mathf.Clamp((p.Temp - 30f) / 55f, 0f, 1f);
        ci.DrawCircle(c + new Vector2(1f, 1.5f), s * 1.05f, new Color(0f, 0f, 0f, 0.28f), true, -1f, true);
        ci.DrawCircle(c, s, new Color("#eef1f4"), true, -1f, true);
        ci.DrawArc(c, s * 0.82f, 0f, Mathf.Tau, 20, new Color("#c9d1da"), 0.8f, true);
        if (!p.Eaten)
        {
            var food = p.Spoiled ? new Color("#6f7d3a") : heat < 0.15f ? FoodColor(r).Darkened(0.15f) : FoodColor(r);
            if (r.Kind is DishKind.Bread or DishKind.Cake) ci.DrawColoredPolygon(Ell(c, s * 0.42f, s * 0.6f, 12, 0.5f), food);
            else if (r.Kind is DishKind.Dumpling) for (int i = 0; i < 3; i++) ci.DrawColoredPolygon(Ell(c + Vector2.FromAngle(i * 2.1f) * s * 0.35f, s * 0.28f, s * 0.18f, 10, i), food);
            else { ci.DrawCircle(c, s * 0.62f, food, true, -1f, true); if (r.Kind is DishKind.Soup or DishKind.Stew) ci.DrawCircle(c, s * 0.3f, food.Lightened(0.2f), true, -1f, true); }
            if (fine) ci.DrawArc(c, s * 0.9f, -2.6f, -1.2f, 8, new Color(1f, 1f, 1f, 0.55f), 1f, true); // 덮어 둔 랩
            if (p.Reheating) for (int i = 0; i < 3; i++) ci.DrawArc(c, s * (1.1f + 0.25f * Mathf.PosMod(_time * 2f + i / 3f, 1f)), -0.6f, 0.6f, 6, new Color(1f, 0.7f, 0.3f, 0.6f), 1f, true); // 데우는 중
            if (p.Spoiled) Flies(ci, c, s * 1.3f, p.Id);
            else Steam(ci, c + new Vector2(0f, -s * 0.5f), heat, p.Id, 0.6f);
        }
        else for (int i = 0; i < 4; i++) ci.DrawCircle(c + new Vector2((Hash01(p.Id, i) - 0.5f) * s, (Hash01(p.Id, i + 5) - 0.5f) * s), 0.7f, FoodColor(r).Darkened(0.2f), true, -1f, true); // 다 먹은 접시의 부스러기
        // 이름표: 접시 가에 기대 세운 쪽지 · 받을 사람 색 띠 · 찾았으면 비스듬히 넘어간다
        var tagAt = c + new Vector2(s * 0.9f, -s * 0.9f);
        float tilt = p.Found ? 0.5f : -0.15f;
        var ax = Vector2.FromAngle(tilt);
        var ay = ax.Orthogonal();
        var tag = new[] { tagAt, tagAt + ax * 9f, tagAt + ax * 9f + ay * 6f, tagAt + ay * 6f };
        ci.DrawColoredPolygon(tag, FdPaper);
        ci.DrawColoredPolygon(new[] { tagAt, tagAt + ax * 2.6f, tagAt + ax * 2.6f + ay * 6f, tagAt + ay * 6f }, Palette.Crew(p.For));
        ci.DrawPolyline(new[] { tag[0], tag[1], tag[2], tag[3], tag[0] }, new Color(0.3f, 0.25f, 0.15f, 0.7f), 0.6f, true);
        ci.DrawLine(c + new Vector2(s * 0.6f, -s * 0.6f), tagAt + ay * 3f, new Color(0.4f, 0.4f, 0.4f, 0.6f), 0.5f); // 묶은 끈
        var who = p.For >= 0 && p.For < _world.Crew.Count ? _world.Crew[p.For].Name : "";
        if (name && who.Length > 0)
            Gfx.Pill(ci, Fonts.Body, tagAt + new Vector2(5f, -8f), $"{who} 몫", 8, new Color(0.15f, 0.12f, 0.08f), FdPaper.WithAlpha(0.92f));
        else if (fine && who.Length > 0)
            Gfx.TextCentered(ci, Fonts.Body, tagAt + ax * 5.8f + ay * 3f, who.Substring(0, 1), 5, new Color(0.15f, 0.12f, 0.08f));
    }

    /// <summary>먹는 사람 머리 위의 작은 그릇 (김이 나면 따뜻한 것, 없으면 식은 채).</summary>
    private void DrawBowl(CanvasItem ci, DishRecipe r, Vector2 c, float s, float heat, int seed)
    {
        ci.DrawColoredPolygon(new[] { c + new Vector2(-s, 0f), c + new Vector2(s, 0f), c + new Vector2(s * 0.6f, s * 0.8f), c + new Vector2(-s * 0.6f, s * 0.8f) }, new Color("#e9edf2"));
        ci.DrawColoredPolygon(Ell(c, s, s * 0.32f, 12), FoodColor(r));
        Steam(ci, c + new Vector2(0f, -1f), heat, seed, 0.45f);
    }

    // ── 균 · 컴퓨터 쪽지 · 감시 눈 · 히터 열 ──

    /// <summary>균: 그릇 테두리에 꿈틀대는 짧은 초록 막대균과 알균 (확대해야 보인다 — 먹는 사람은 모른다).</summary>
    private void DrawGerms(CanvasItem ci, Vector2 c, float s, int seed)
    {
        for (int i = 0; i < 5; i++)
        {
            float a = Hash01(seed, i + 11) * Mathf.Tau + _time * 0.3f;
            var p = c + Vector2.FromAngle(a) * s * (0.9f + 0.15f * Mathf.Sin(_time * 2f + i));
            var d = Vector2.FromAngle(a + 1.2f + Mathf.Sin(_time * 3f + i) * 0.5f);
            var col = new Color(0.45f, 0.85f, 0.25f, 0.85f);
            if (i % 2 == 0) { ci.DrawLine(p - d * 1.6f, p + d * 1.6f, col, 1.1f, true); ci.DrawLine(p + d * 1.6f, p + d * 2.4f + d.Orthogonal() * Mathf.Sin(_time * 9f + i), col.WithAlpha(0.5f), 0.5f, true); } // 막대균과 꼬리
            else { ci.DrawCircle(p, 0.8f, col, true, -1f, true); ci.DrawCircle(p + d * 1.3f, 0.6f, col, true, -1f, true); } // 알균 둘
        }
    }

    /// <summary>주컴퓨터 쪽지: 호박색 작은 화면 · 받아들였으면 깜빡이는 점과 "먼저" 줄, 흘려들었으면 구겨진 회색 쪽지.</summary>
    private void DrawComputerNote(CanvasItem ci, Vector2 at, bool taken, int seed, bool fine)
    {
        if (taken)
        {
            var box = new Rect2(at - new Vector2(4.5f, 3f), new Vector2(9f, 6f));
            Gfx.RoundRect(ci, box, new Color("#2b2418"), 1.2f, new Color("#f2a93b"), 1);
            float blink = Mathf.PosMod(_time * 1.6f + seed * 0.1f, 1f) < 0.5f ? 1f : 0.3f;
            ci.DrawCircle(box.Position + new Vector2(2f, 2f), 0.9f, new Color(1f, 0.66f, 0.2f, blink), true, -1f, true);
            for (int i = 0; i < 2; i++) ci.DrawLine(box.Position + new Vector2(3.6f, 1.8f + i * 2f), box.Position + new Vector2(8f - i * 1.5f, 1.8f + i * 2f), new Color(1f, 0.8f, 0.4f, 0.85f), 0.7f); // 글줄
            if (fine) Gfx.TextCentered(ci, Fonts.Body, at + new Vector2(0f, -5f), "먼저", 5, new Color("#f2a93b"));
        }
        else
        {
            var pts = new[] { at + new Vector2(-4f, -2.5f), at + new Vector2(-1f, -3.2f), at + new Vector2(3.8f, -2f), at + new Vector2(4.2f, 2.6f), at + new Vector2(0.5f, 3.2f), at + new Vector2(-3.6f, 2.2f) };
            ci.DrawColoredPolygon(pts, new Color("#9aa0a6").WithAlpha(0.8f));
            ci.DrawLine(at + new Vector2(-2.5f, -1.5f), at + new Vector2(2.5f, 1.8f), new Color(0.35f, 0.35f, 0.38f, 0.8f), 0.6f); // 구김
            ci.DrawLine(at + new Vector2(-2f, 1.6f), at + new Vector2(2.6f, -1.4f), new Color(0.35f, 0.35f, 0.38f, 0.8f), 0.6f);
        }
    }

    /// <summary>주컴퓨터 감시 눈: 화구 위 하늘색 렌즈 · 맥박 고리 · 위아래로 훑는 주사선.</summary>
    private void DrawWatchEye(CanvasItem ci, Vector2 c, int seed, bool fine)
    {
        var cyan = new Color("#5fd3f3");
        float t = Mathf.PosMod(_time * 1.2f + seed * 0.13f, 1f);
        ci.DrawArc(c, 4f + 6f * t, 0f, Mathf.Tau, 20, cyan.WithAlpha(0.7f * (1f - t)), 1f, true); // 맥박
        ci.DrawColoredPolygon(new[] { c + new Vector2(-5f, 0f), c + new Vector2(0f, -3f), c + new Vector2(5f, 0f), c + new Vector2(0f, 3f) }, new Color("#14303a"));
        ci.DrawCircle(c, 1.8f, cyan, true, -1f, true);
        ci.DrawCircle(c + new Vector2(-0.6f, -0.6f), 0.5f, Colors.White, true, -1f, true);
        float sy = Mathf.Sin(_time * 4f) * 2.6f;
        ci.DrawLine(c + new Vector2(-4f, sy), c + new Vector2(4f, sy), cyan.WithAlpha(0.6f), 0.6f); // 주사선
        if (fine) for (int i = 0; i < 3; i++) ci.DrawArc(c + new Vector2(6f, -4f), 1.5f + i * 1.6f, -1.1f, 0.2f, 5, cyan.WithAlpha(0.5f + 0.3f * Mathf.Sin(_time * 5f - i)), 0.6f, true); // 부르는 전파
    }

    /// <summary>이동식 히터 앞에서 데운 그릇: 아래로 주황 열 물결이 일렁인다.</summary>
    private void HeatRipple(CanvasItem ci, Vector2 c, int seed)
    {
        for (int i = 0; i < 3; i++)
        {
            var pts = new Vector2[6];
            for (int j = 0; j < pts.Length; j++)
                pts[j] = c + new Vector2(-4f + j * 1.6f, i * 1.6f + Mathf.Sin(_time * 6f + j + i + seed) * 0.7f);
            ci.DrawPolyline(pts, new Color(1f, 0.55f, 0.2f, 0.65f - 0.18f * i), 0.8f, true);
        }
    }

    // ── 냄새 안개 (종류마다 모양 · 움직임이 다르다) ──

    private void PaintSmellFog(CanvasItem ci, bool strong)
    {
        var w = _world;
        var sm = w.Smells;
        float baseA = strong ? 1f : 0.35f;
        foreach (var room in w.Ship.LiveRooms)
        {
            if (room.Detached) continue;
            float bread = sm.Level(room, SmellKind.Bread), cook = sm.Level(room, SmellKind.Cooking), burnt = sm.Level(room, SmellKind.Burnt), foul = sm.Level(room, SmellKind.Foul), coffee = sm.Level(room, SmellKind.Coffee);
            if (bread < 0.04f && cook < 0.05f && burnt < 0.03f && foul < 0.08f && coffee < 0.05f) continue;
            var box = RoomBox(room);
            int seed = room.Id * 7;
            if (strong)
            {
                var dom = sm.Dominant(room, out var dv);
                if (dom is SmellKind k) FillRoom(ci, room, (k switch { SmellKind.Bread => FogBread, SmellKind.Cooking => FogCook, SmellKind.Burnt => FogBurnt, SmellKind.Coffee => FogCoffee, _ => FogFoul }).WithAlpha(0.05f + 0.3f * dv));
            }
            // 빵: 금빛 리본이 느리게 감아 오른다
            if (bread >= 0.04f)
            {
                int n = 1 + (int)(bread * 4f);
                for (int i = 0; i < n; i++)
                {
                    float t = Mathf.PosMod(_time * 0.12f + i / (float)n + Hash01(seed, i), 1f);
                    float x0 = box.Position.X + box.Size.X * (0.15f + 0.7f * Hash01(seed + 1, i));
                    var pts = new Vector2[10];
                    for (int j = 0; j < pts.Length; j++)
                    {
                        float u = j / (float)(pts.Length - 1);
                        pts[j] = new Vector2(x0 + Mathf.Sin(u * 5f + _time * 0.9f + i) * 9f, box.End.Y - 6f - (box.Size.Y - 12f) * Mathf.Clamp(t * 0.7f + u * 0.3f, 0f, 1f));
                    }
                    ci.DrawPolyline(pts, FogBread.WithAlpha(MathF.Min(1f, bread * 2.2f) * 0.75f * baseA * Mathf.Sin(t * Mathf.Pi)), 3.2f, true);
                }
            }
            // 요리: 주황 짧은 김 가닥이 여럿, 빠르게
            if (cook >= 0.05f)
            {
                int n = 2 + (int)(cook * 6f);
                for (int i = 0; i < n; i++)
                {
                    float t = Mathf.PosMod(_time * 0.3f + i * 0.37f + Hash01(seed + 2, i), 1f);
                    var p = new Vector2(box.Position.X + box.Size.X * (0.1f + 0.8f * Hash01(seed + 3, i)), box.End.Y - 6f - (box.Size.Y - 12f) * t);
                    ci.DrawArc(p + new Vector2(Mathf.Sin(_time * 2.5f + i) * 3f, 0f), 3.5f + 3f * t, 3.6f, 5.8f, 8, FogCook.WithAlpha(MathF.Min(1f, cook * 2.5f) * 0.7f * baseA * (1f - t)), 2f, true);
                }
            }
            // 탄내: 잿빛 연기 덩이가 옆으로 흐르고 그을음 알갱이가 떨어진다
            if (burnt >= 0.03f)
            {
                int n = 2 + (int)(burnt * 6f);
                for (int i = 0; i < n; i++)
                {
                    float t = Mathf.PosMod(_time * 0.08f + i / (float)n, 1f);
                    var p = new Vector2(box.Position.X + box.Size.X * t, box.Position.Y + box.Size.Y * (0.2f + 0.5f * Hash01(seed + 4, i)) + Mathf.Sin(_time + i) * 4f);
                    ci.DrawCircle(p, 6f + 8f * burnt + 2f * Mathf.Sin(_time * 1.7f + i), FogBurnt.WithAlpha(MathF.Min(1f, burnt * 2f) * 0.32f * baseA * Mathf.Sin(t * Mathf.Pi)), true, -1f, true);
                    float f = Mathf.PosMod(_time * 0.5f + Hash01(seed + 5, i), 1f);
                    ci.DrawRect(new Rect2(p.X + 4f, p.Y + f * 18f, 1.4f, 1.4f), new Color(0.1f, 0.08f, 0.07f, MathF.Min(1f, burnt * 3f) * baseA * (1f - f)));
                }
            }
            // 악취: 바닥 가까이 초록 얼룩 · 지그재그 · 파리
            if (foul >= 0.08f)
            {
                int n = 1 + (int)(foul * 4f);
                for (int i = 0; i < n; i++)
                {
                    var p = new Vector2(box.Position.X + box.Size.X * (0.15f + 0.7f * Hash01(seed + 6, i)), box.End.Y - 8f - 6f * Hash01(seed + 7, i));
                    float pulse = 0.8f + 0.2f * Mathf.Sin(_time * 1.3f + i);
                    ci.DrawColoredPolygon(Ell(p, 9f * pulse, 4f * pulse, 14, 0.2f * i), FogFoul.WithAlpha(MathF.Min(1f, foul * 2f) * 0.22f * baseA));
                    Stink(ci, p + new Vector2(0f, -4f), 12f, MathF.Min(1f, foul * 2f) * baseA, seed + i);
                    if (foul > 0.25f) Flies(ci, p + new Vector2(0f, -8f), 6f, seed + i);
                }
            }
            // 커피: 갈색 나선이 한 점에서 감겨 오르고, 작은 원두(가운데 홈)가 둥실 떠다닌다
            if (coffee >= 0.05f)
            {
                int n = 1 + (int)(coffee * 3f);
                for (int i = 0; i < n; i++)
                {
                    var b0 = new Vector2(box.Position.X + box.Size.X * (0.2f + 0.6f * Hash01(seed + 8, i)), box.End.Y - 7f);
                    var pts = new Vector2[14];
                    for (int j = 0; j < pts.Length; j++)
                    {
                        float u = j / (float)(pts.Length - 1);
                        float a = u * 9f + _time * 1.4f + i;
                        pts[j] = b0 + new Vector2(Mathf.Cos(a) * (2f + 5f * u), -u * (box.Size.Y * 0.55f));
                    }
                    ci.DrawPolyline(pts, FogCoffee.WithAlpha(MathF.Min(1f, coffee * 2.5f) * 0.6f * baseA), 1.6f, true);
                    float f = Mathf.PosMod(_time * 0.2f + Hash01(seed + 9, i), 1f);
                    var bean = b0 + new Vector2(Mathf.Sin(_time + i) * 6f, -f * box.Size.Y * 0.6f);
                    ci.DrawColoredPolygon(Ell(bean, 1.8f, 1.2f, 10, 0.6f + i), new Color("#4a2c1a").WithAlpha((1f - f) * baseA));
                    ci.DrawLine(bean + Vector2.FromAngle(0.6f + i) * -1.2f, bean + Vector2.FromAngle(0.6f + i) * 1.2f, new Color("#c8955f").WithAlpha((1f - f) * baseA), 0.5f); // 원두 홈
                }
            }
        }
    }
}
