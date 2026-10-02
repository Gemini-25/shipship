using System.Linq;
using Godot;
using ShipSim.Core;
using FA = ShipSim.View.FixtureArt;

namespace ShipSim.View;

/// <summary>
/// v16.5b 기술 모습 — 외판(용접 비드 · 차폐 코일 · 물주머니 · 젤 벌집 · 늑골 · 물결 판 · 위상 줄 · 납판 · 두꺼운 판 · 난파선 합금 · 찍은 패치) ·
/// 바깥(드론 · 추력기 · 레이저 포탑 · 자기 노즐 · 회전 고리 · 이온 고리 · 복사 날개 · 왜곡 거품 · 역장 · 렌즈 · 수리 떼 · 탐지 접시 · 간섭계) ·
/// 배관(첨가제 띠 · 열 파이프 · 기는 로봇 · 액체 금속 · 빠른 이음) · 망(초전도 간선 서리 · 광섬유) · 빛(더하기 층) · 갈림길 배 이름 문장.
/// 외판 좌표: x = 외판을 따라(−16~16), y = 우주 쪽(+ · 외판 끝 약 26) · 안쪽(−).
/// </summary>
public partial class ShipView
{
    private static void HullXf(CanvasItem ci, in HullSeg h) => ci.DrawSetTransformMatrix(new Transform2D(h.Tn, h.Out, h.C));

    // ═══════════════════════════════ 외판 ═══════════════════════════════

    private void VisHullStatic(CanvasItem ci, in VisPlace p)
    {
        var h = p.Hull;
        var col = p.Col;
        HullXf(ci, h);
        switch (p.Key)
        {
            case "hull.weldseam":
            {
                // 고른 용접 비드: 겹친 비늘 호 + 열 변색 띠
                ci.DrawLine(new Vector2(-16f, 14f), new Vector2(16f, 14f), new Color("#c8a050").WithAlpha(0.18f), 4f);
                for (float x = -15f; x < 16f; x += 2.6f) ci.DrawArc(new Vector2(x, 14f), 1.6f, -Mathf.Pi * 0.5f, Mathf.Pi * 0.5f, 5, new Color("#b8c0cc").WithAlpha(0.7f), 0.8f, true);
                break;
            }
            case "hull.coilring":
            {
                // 차폐 코일: 구리 감기 사선 · 위아래 띠쇠
                ci.DrawLine(new Vector2(-16f, 8f), new Vector2(16f, 8f), FA.Steel4, 1f);
                ci.DrawLine(new Vector2(-16f, 22f), new Vector2(16f, 22f), FA.Steel4, 1f);
                for (float x = -16f; x < 14f; x += 2.6f) ci.DrawLine(new Vector2(x, 8.5f), new Vector2(x + 2.6f, 21.5f), Cu.WithAlpha(0.85f), 1.1f, true);
                break;
            }
            case "hull.waterwall":
            {
                // 물주머니 벽 (안쪽): 푸른 베개 · 누빈 줄 · 꼭지
                FA.Box(ci, RR(-15f, -15.5f, 15f, -6f), new Color("#2a6aa8").WithAlpha(0.7f), 3f, new Color("#9ad0ff").WithAlpha(0.6f));
                ci.DrawLine(new Vector2(-5f, -15f), new Vector2(-5f, -6.5f), new Color(0, 0, 0, 0.25f), 0.6f);
                ci.DrawLine(new Vector2(5f, -15f), new Vector2(5f, -6.5f), new Color(0, 0, 0, 0.25f), 0.6f);
                FA.Dot(ci, new Vector2(12f, -10.5f), 1f, FA.Chrome);
                break;
            }
            case "hull.selfseal":
            {
                // 호박색 젤 벌집 (2줄)
                for (int row = 0; row < 2; row++)
                    for (int i = 0; i < 4; i++)
                    {
                        var o = new Vector2(-12f + i * 8f + row * 4f, -4f + row * 7f);
                        var hex = new Vector2[7];
                        for (int k = 0; k < 7; k++) hex[k] = o + Vector2.FromAngle(k * Mathf.Tau / 6f + Mathf.Pi / 6f) * 3.6f;
                        ci.DrawColoredPolygon(hex[..6], new Color("#d89a30").WithAlpha(0.35f));
                        ci.DrawPolyline(hex, new Color("#f0c060").WithAlpha(0.6f), 0.6f, true);
                    }
                break;
            }
            case "hull.ribs":
            {
                // 탄소 섬유 늑골: 외판을 가로지르는 검은 띠 · 짜임 무늬
                ci.DrawRect(RR(-3.5f, -16f, 3.5f, 24f), new Color("#14161a"));
                for (float y = -15f; y < 23f; y += 2f)
                {
                    ci.DrawLine(new Vector2(-3f, y), new Vector2(0f, y + 1f), new Color(1, 1, 1, 0.12f), 0.6f);
                    ci.DrawLine(new Vector2(0f, y + 1f), new Vector2(3f, y), new Color(1, 1, 1, 0.06f), 0.6f);
                }
                ci.DrawLine(new Vector2(-3.5f, -16f), new Vector2(-3.5f, 24f), col.WithAlpha(0.4f), 0.6f);
                ci.DrawLine(new Vector2(3.5f, -16f), new Vector2(3.5f, 24f), col.WithAlpha(0.4f), 0.6f);
                break;
            }
            case "hull.memoryalloy":
            {
                // 형상 기억 합금: 은빛 물결 판 셋
                for (int k = 0; k < 3; k++)
                {
                    var pts = new Vector2[9];
                    for (int i = 0; i < 9; i++) pts[i] = new Vector2(-16f + i * 4f, 10f + k * 4f + Mathf.Sin(i * 1.4f + k) * 1.2f);
                    ci.DrawPolyline(pts, new Color("#d8e0ea").WithAlpha(0.55f - k * 0.1f), 1f, true);
                }
                break;
            }
            case "hull.phase":
            {
                // 위상 간섭 줄 (정적: 옅게)
                for (float x = -16f; x < 16f; x += 2f)
                {
                    float a = 0.15f + 0.15f * Mathf.Sin(x * 0.6f + h.K);
                    ci.DrawLine(new Vector2(x, 6f), new Vector2(x, 22f), col.WithAlpha(a), 0.8f);
                }
                break;
            }
            case "hull.radlayer":
            {
                // 납판: 둔한 청회색 · 볼트 넷 · 찍힌 글씨 자리
                FA.Box(ci, RR(-14f, 4f, 14f, 20f), new Color("#5a6470"), 1f, new Color("#2a3038"));
                FA.Bolts(ci, RR(-14f, 4f, 14f, 20f), 2.2f, 0.9f);
                ci.DrawRect(RR(-5f, 9f, 5f, 15f), new Color(0, 0, 0, 0.18f), false, 0.7f);
                break;
            }
            case "hull.thickplate":
            {
                // 덧댄 두꺼운 판: 외판보다 바깥으로 · 큰 볼트 · 겹 이음
                FA.Box(ci, RR(-16.5f, 17f, 16.5f, 31f), new Color("#262c36"), 1f, new Color("#4a5260"));
                ci.DrawLine(new Vector2(-16.5f, 24f), new Vector2(16.5f, 24f), new Color(0, 0, 0, 0.4f), 0.8f);
                FA.Bolt(ci, new Vector2(-10f, 20.5f), 1.4f);
                FA.Bolt(ci, new Vector2(10f, 20.5f), 1.4f);
                FA.Bolt(ci, new Vector2(0f, 27.5f), 1.4f);
                break;
            }
            case "hull.wreckalloy":
            {
                // 난파선 합금 덧판: 청동빛 · 비뚤어진 판 · 옛 글씨 토막 · 리벳
                ci.DrawColoredPolygon(new[] { new Vector2(-11f, 3f), new Vector2(12f, 5f), new Vector2(10f, 20f), new Vector2(-12f, 18f) }, new Color("#8a6a3a"));
                ci.DrawPolyline(new[] { new Vector2(-11f, 3f), new Vector2(12f, 5f), new Vector2(10f, 20f), new Vector2(-12f, 18f), new Vector2(-11f, 3f) }, new Color("#c8a060"), 0.8f, true);
                for (int i = 0; i < 4; i++) ci.DrawLine(new Vector2(-7f + i * 3.5f, 9f), new Vector2(-7f + i * 3.5f + (i % 2) * 1.5f, 13f), new Color("#3a2a14"), 0.9f);
                foreach (var rv in new[] { new Vector2(-9f, 5f), new Vector2(9f, 7f), new Vector2(8f, 18f), new Vector2(-10f, 16f) }) FA.Dot(ci, rv, 0.9f, new Color("#d8b880"));
                break;
            }
            case "hull.printpatch":
            {
                // 찍어 붙인 패치: 둥근 테 안 벌집 결
                FA.Box(ci, RR(-10f, 4f, 10f, 18f), col.Darkened(0.4f).WithAlpha(0.7f), 4f, col.WithAlpha(0.7f));
                for (int i = 0; i < 5; i++)
                    for (int j = 0; j < 3; j++)
                    {
                        var o = new Vector2(-7f + i * 3.5f + (j % 2) * 1.75f, 7f + j * 3.2f);
                        ci.DrawArc(o, 1.4f, 0f, Mathf.Tau, 6, col.Lightened(0.2f).WithAlpha(0.6f), 0.5f, true);
                    }
                break;
            }
        }
        NoXf(ci);
        if (p.Key == "hull.radlayer" && h.K % 4 == 0)
            Gfx.TextCentered(ci, Fonts.Bold, h.L(0f, 12f) + new Vector2(0f, Gfx.CenterOffset(Fonts.Bold, 6)), "Pb", 6, new Color("#c8d0dc").WithAlpha(0.7f));
    }

    private void VisHullLive(CanvasItem ci, in VisPlace p)
    {
        var h = p.Hull;
        HullXf(ci, h);
        switch (p.Key)
        {
            case "hull.coilring":
            {
                // 장 반짝임: 코일을 따라 흐르는 빛 (자기장이 돈다)
                float q = Mathf.PosMod(_time * 0.8f + h.K * 0.13f, 1f);
                ci.DrawLine(new Vector2(-16f + 32f * q, 8.5f), new Vector2(-16f + 32f * q + 2.6f, 21.5f), new Color("#ffd0a0").WithAlpha(0.6f), 1.4f, true);
                break;
            }
            case "hull.waterwall":
            {
                float q = Mathf.Sin(_time * 0.9f + h.K * 0.7f);
                ci.DrawLine(new Vector2(-12f + q * 3f, -13.5f), new Vector2(-4f + q * 3f, -13.5f), new Color(1, 1, 1, 0.3f), 0.8f, true);
                break;
            }
            case "hull.phase":
            {
                for (float x = -16f; x < 16f; x += 4f)
                {
                    float a = 0.25f + 0.25f * Mathf.Sin(x * 0.4f - _time * 2f + h.K * 0.5f);
                    ci.DrawLine(new Vector2(x, 6f), new Vector2(x, 22f), p.Col.Lightened(0.3f).WithAlpha(a), 0.8f);
                }
                break;
            }
        }
        NoXf(ci);
    }

    // ═══════════════════════════════ 바깥 ═══════════════════════════════

    private (float l, float r, float t, float b) HullBox()
    {
        float l = float.MaxValue, r = float.MinValue, t = float.MaxValue, b = float.MinValue;
        foreach (var h in _hullSegs) { l = Mathf.Min(l, h.C.X); r = Mathf.Max(r, h.C.X); t = Mathf.Min(t, h.C.Y); b = Mathf.Max(b, h.C.Y); }
        return (l - 16f, r + 16f, t - 16f, b + 16f);
    }

    /// <summary>위(−1) · 아래(+1) 외판에서 x에 가장 가까운 자리 (외판 바깥 끝).</summary>
    private Vector2 HullAt(float x, int side)
    {
        HullSeg? best = null;
        float bd = float.MaxValue;
        foreach (var h in _hullSegs)
        {
            if (h.Out.Y != side) continue;
            float d = Mathf.Abs(h.C.X - x);
            if (d < bd) { bd = d; best = h; }
        }
        return best is HullSeg s ? s.L(0f, 24f) : new Vector2(x, side < 0 ? HullBox().t : HullBox().b);
    }

    private void VisExteriorStatic(CanvasItem ci, in VisPlace p)
    {
        if (_hullSegs.Count == 0) return;
        var (l, r, t, b) = HullBox();
        var col = p.Col;
        float wdt = r - l, mid = (l + r) * 0.5f, cy = (t + b) * 0.5f;
        switch (p.Key)
        {
            case "exterior.drones":
            {
                // 드론 거치대 둘 (위 외판): 받침 · 집게 두 개
                foreach (float f in new[] { 0.35f, 0.65f })
                {
                    var o = HullAt(l + wdt * f, -1);
                    ci.DrawRect(new Rect2(o + new Vector2(-6f, -3f), new Vector2(12f, 3f)), FA.Steel3);
                    ci.DrawLine(o + new Vector2(-5f, -3f), o + new Vector2(-6f, -7f), FA.Steel4, 1.2f);
                    ci.DrawLine(o + new Vector2(5f, -3f), o + new Vector2(6f, -7f), FA.Steel4, 1.2f);
                    FA.Led(ci, o + new Vector2(0f, -1.5f), col, 0.6f, 0.8f);
                }
                break;
            }
            case "exterior.rcs":
            {
                // 자세 제어 추력기 넷 (모서리): 상자 · 세 방향 노즐
                foreach (var (x, side) in new[] { (l + wdt * 0.12f, -1), (r - wdt * 0.12f, -1), (l + wdt * 0.12f, 1), (r - wdt * 0.12f, 1) })
                {
                    var o = HullAt(x, side);
                    ci.DrawRect(new Rect2(o - new Vector2(4f, 3f), new Vector2(8f, 6f)), FA.Steel2);
                    ci.DrawRect(new Rect2(o - new Vector2(4f, 3f), new Vector2(8f, 6f)), FA.Steel4, false, 0.8f);
                    ci.DrawLine(o + new Vector2(0f, side * 3f), o + new Vector2(0f, side * 6f), FA.Chrome, 2f);
                    ci.DrawLine(o + new Vector2(-4f, 0f), o + new Vector2(-6.5f, 0f), FA.Chrome, 1.6f);
                    ci.DrawLine(o + new Vector2(4f, 0f), o + new Vector2(6.5f, 0f), FA.Chrome, 1.6f);
                }
                break;
            }
            case "exterior.pdlaser":
            {
                // 점 방어 레이저 포탑 둘: 반구 · 포신 (위 · 아래)
                foreach (int side in new[] { -1, 1 })
                {
                    var o = HullAt(mid + wdt * 0.1f, side);
                    ci.DrawArc(o, 6f, side < 0 ? Mathf.Pi : 0f, side < 0 ? Mathf.Tau : Mathf.Pi, 12, FA.Steel4, 2f, true);
                    ci.DrawCircle(o, 4f, FA.Steel2, true, -1f, true);
                    ci.DrawCircle(o, 1.6f, new Color("#ff5c4c").WithAlpha(0.7f), true, -1f, true);
                }
                break;
            }
            case "engine.magnozzle":
            {
                // 노즐을 감은 자기 코일 고리 셋 (구리)
                foreach (var f in _world.Ship.FurnitureOf(FurnitureType.EngineCore))
                {
                    float xw = (f.MinX - 1) * T + 2f;
                    float y0 = f.MinY * T + 8f, y1 = (f.MaxY + 1) * T - 8f;
                    float xe = xw - NozzleLen - 10f;
                    for (int k = 1; k <= 3; k++)
                    {
                        float x = Mathf.Lerp(xw, xe, k / 4f);
                        float spread = Mathf.Lerp(0f, 10f, k / 4f) + 4f;
                        ci.DrawSetTransform(new Vector2(x, (y0 + y1) * 0.5f), 0f, new Vector2(0.28f, 1f));
                        ci.DrawArc(Vector2.Zero, (y1 - y0) * 0.5f + spread, 0f, Mathf.Tau, 28, Cu, 3f, true);
                        ci.DrawArc(Vector2.Zero, (y1 - y0) * 0.5f + spread, 3.6f, 5.4f, 10, Cu.Lightened(0.4f), 1f, true);
                        ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                    }
                }
                break;
            }
            case "hull.centrifuge":
            {
                // 배를 두른 회전 고리: 세로로 선 띠 (위아래로 외판 밖까지) · 바퀴살 자국
                float rx = 18f, ry = (b - t) * 0.5f + 34f;
                ci.DrawSetTransform(new Vector2(mid, cy), 0f, new Vector2(rx / ry, 1f));
                ci.DrawArc(Vector2.Zero, ry, 0f, Mathf.Tau, 64, new Color("#2a303a"), 9f * ry / rx * 0.35f, true);
                ci.DrawArc(Vector2.Zero, ry, 0f, Mathf.Tau, 64, col.WithAlpha(0.35f), 1.2f, true);
                ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                break;
            }
            case "exterior.ionring":
            {
                // 뱃머리 이온 편향 고리: 굵은 호 · 방출 마디 다섯
                var o = _noseTip;
                ci.DrawArc(o, 24f, -1.1f, 1.1f, 20, FA.Steel3, 4f, true);
                ci.DrawArc(o, 24f, -1.1f, 1.1f, 20, col.WithAlpha(0.6f), 1f, true);
                for (int i = 0; i < 5; i++) ci.DrawCircle(o + Vector2.FromAngle(-1f + i * 0.5f) * 24f, 2f, new Color("#9fd8ff"), true, -1f, true);
                break;
            }
            case "exterior.radiator":
            {
                // 복사 냉각 날개 (위 · 아래): 큰 사다리꼴 판 · 핀 줄 · 관
                foreach (int side in new[] { -1, 1 })
                {
                    var root = HullAt(l + wdt * 0.42f, side);
                    float len = 54f;
                    var poly = new[] { root + new Vector2(-16f, 0f), root + new Vector2(16f, 0f), root + new Vector2(10f, side * len), root + new Vector2(-24f, side * len) };
                    ci.DrawColoredPolygon(poly, new Color("#2a2420"));
                    ci.DrawPolyline(poly.Append(poly[0]).ToArray(), new Color("#6a5a4a"), 1f, true);
                    for (int k = 1; k < 7; k++)
                    {
                        float f = k / 7f;
                        ci.DrawLine(root + new Vector2(-16f - 8f * f, side * len * f), root + new Vector2(16f - 6f * f, side * len * f), new Color("#4a3a30"), 1f);
                    }
                    ci.DrawLine(root, root + new Vector2(-6f, side * len), Cu.WithAlpha(0.7f), 1.4f);
                }
                break;
            }
            case "engine.warp":
            {
                // 공간 왜곡 거품 윤곽 (점선 타원)
                float rx = wdt * 0.5f + 70f, ry = (b - t) * 0.5f + 56f;
                for (int i = 0; i < 48; i += 2)
                {
                    float a0 = i * Mathf.Tau / 48f, a1 = (i + 1) * Mathf.Tau / 48f;
                    ci.DrawLine(new Vector2(mid + Mathf.Cos(a0) * rx, cy + Mathf.Sin(a0) * ry), new Vector2(mid + Mathf.Cos(a1) * rx, cy + Mathf.Sin(a1) * ry), col.WithAlpha(0.3f), 1f, true);
                }
                break;
            }
            case "exterior.forcefield":
            {
                // 외판 밖 육각 역장 (옅게 · 두 칸마다)
                foreach (var h in _hullSegs)
                {
                    if (h.K % 2 != 0) continue;
                    var o = h.L(0f, 32f);
                    var hex = new Vector2[7];
                    for (int k = 0; k < 7; k++) hex[k] = o + Vector2.FromAngle(k * Mathf.Tau / 6f) * 6f;
                    ci.DrawPolyline(hex, col.WithAlpha(0.18f), 0.7f, true);
                }
                break;
            }
            case "exterior.gravlens":
            {
                // 뱃머리 앞 렌즈 고리 셋 (세로 타원)
                for (int k = 0; k < 3; k++)
                {
                    var o = _noseTip + new Vector2(38f + k * 16f, 0f);
                    ci.DrawSetTransform(o, 0f, new Vector2(0.35f, 1f));
                    ci.DrawArc(Vector2.Zero, 26f + k * 6f, 0f, Mathf.Tau, 32, col.WithAlpha(0.35f - k * 0.08f), 2.4f, true);
                    ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                }
                break;
            }
            case "exterior.swarm":
            {
                // 쉬고 있는 수리 로봇 떼 (외판 위 작은 점 무리)
                for (int g = 0; g < 4; g++)
                {
                    if (_hullSegs.Count == 0) break;
                    var h = _hullSegs[(int)(FA.Hash(g, 3, 470) * _hullSegs.Count) % _hullSegs.Count];
                    for (int k = 0; k < 4; k++) FA.Dot(ci, h.L((k - 1.5f) * 3f, 26f + (k % 2) * 2f), 1.1f, new Color("#f2994a"));
                }
                break;
            }
            case "exterior.salvagescan":
            {
                // 잔해 탐지 접시 (아래 외판 · 뱃머리 쪽): 기둥 · 접시
                var o = HullAt(r - wdt * 0.2f, 1);
                ci.DrawLine(o, o + new Vector2(0f, 8f), FA.Steel4, 1.6f);
                ci.DrawArc(o + new Vector2(0f, 12f), 6f, Mathf.Pi * 1.1f, Mathf.Pi * 1.9f, 10, FA.Chrome, 2f, true);
                FA.Dot(ci, o + new Vector2(0f, 8.5f), 1.2f, col);
                break;
            }
            case "sensor.gravwave":
            {
                // 중력파 간섭계: 위 외판의 L자 두 팔 · 끝 거울
                var hub = HullAt(r - wdt * 0.3f, -1) + new Vector2(0f, -6f);
                var e1 = hub + new Vector2(-wdt * 0.35f, 0f);
                var e2 = hub + new Vector2(0f, -46f);
                ci.DrawLine(hub, e1, FA.Steel3, 3f);
                ci.DrawLine(hub, e2, FA.Steel3, 3f);
                ci.DrawLine(hub, e1, col.WithAlpha(0.35f), 0.8f);
                ci.DrawLine(hub, e2, col.WithAlpha(0.35f), 0.8f);
                ci.DrawRect(new Rect2(hub - new Vector2(3f, 3f), new Vector2(6f, 6f)), FA.Steel4);
                ci.DrawRect(new Rect2(e1 - new Vector2(1.5f, 3f), new Vector2(3f, 6f)), FA.Chrome);
                ci.DrawRect(new Rect2(e2 - new Vector2(3f, 1.5f), new Vector2(6f, 3f)), FA.Chrome);
                break;
            }
        }
    }

    private void VisExteriorLive(CanvasItem ci, in VisPlace p)
    {
        if (_hullSegs.Count == 0) return;
        var (l, r, t, b) = HullBox();
        var col = p.Col;
        float wdt = r - l, mid = (l + r) * 0.5f, cy = (t + b) * 0.5f;
        float tm = _time;
        switch (p.Key)
        {
            case "exterior.drones":
            {
                // 배를 도는 드론 셋 (깜빡이는 등)
                float rx = wdt * 0.5f + 40f, ry = (b - t) * 0.5f + 30f;
                for (int k = 0; k < 3; k++)
                {
                    float a = tm * 0.15f + k * Mathf.Tau / 3f;
                    var o = new Vector2(mid + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry);
                    ci.DrawColoredPolygon(new[] { o + new Vector2(0f, -3f), o + new Vector2(3f, 0f), o + new Vector2(0f, 3f), o + new Vector2(-3f, 0f) }, new Color("#c8d0dc"));
                    for (int j = 0; j < 4; j++) FA.Dot(ci, o + Vector2.FromAngle(j * Mathf.Pi / 2f + Mathf.Pi / 4f) * 4f, 1.2f, new Color(1, 1, 1, 0.35f));
                    FA.Led(ci, o, col, Mathf.PosMod(tm * 2f + k, 1f) < 0.2f ? 1f : 0.2f, 0.8f);
                }
                break;
            }
            case "exterior.rcs":
            {
                int which = (int)(tm * 0.4f) % 4;
                float q = Mathf.PosMod(tm * 0.4f, 1f) * 4f % 1f;
                if (q > 0.3f) break;
                var (x, side) = which switch { 0 => (l + wdt * 0.12f, -1), 1 => (r - wdt * 0.12f, -1), 2 => (l + wdt * 0.12f, 1), _ => (r - wdt * 0.12f, 1) };
                var o = HullAt(x, side) + new Vector2(0f, side * 7f);
                for (int k = 0; k < 3; k++) FA.Dot(ci, o + new Vector2((k - 1) * 1.5f, side * q * 30f), 1.5f + q * 10f, new Color(1, 1, 1, 0.4f * (1f - q / 0.3f)));
                break;
            }
            case "exterior.pdlaser":
            {
                foreach (int side in new[] { -1, 1 })
                {
                    var o = HullAt(mid + wdt * 0.1f, side);
                    float a = (side < 0 ? -Mathf.Pi / 2f : Mathf.Pi / 2f) + Mathf.Sin(tm * 0.6f + side) * 0.9f;
                    ci.DrawLine(o, o + Vector2.FromAngle(a) * 9f, FA.Chrome, 1.6f, true);
                    float ph = Mathf.PosMod(tm * 0.23f + (side < 0 ? 0f : 0.5f), 1f);
                    if (ph < 0.04f) ci.DrawLine(o + Vector2.FromAngle(a) * 9f, o + Vector2.FromAngle(a) * 260f, new Color("#ff3a3a").WithAlpha(0.8f * (1f - ph / 0.04f)), 1.4f, true);
                }
                break;
            }
            case "engine.magnozzle":
            {
                bool burn = _world.Propulsion.Burning || _world.Propulsion.CourseBurnVisible;
                foreach (var f in _world.Ship.FurnitureOf(FurnitureType.EngineCore))
                {
                    float xw = (f.MinX - 1) * T + 2f;
                    float y0 = f.MinY * T + 8f, y1 = (f.MaxY + 1) * T - 8f;
                    float xe = xw - NozzleLen - 10f;
                    int k = 1 + (int)(tm * (burn ? 6f : 1.5f)) % 3;
                    float x = Mathf.Lerp(xw, xe, k / 4f);
                    float spread = Mathf.Lerp(0f, 10f, k / 4f) + 4f;
                    ci.DrawSetTransform(new Vector2(x, (y0 + y1) * 0.5f), 0f, new Vector2(0.28f, 1f));
                    ci.DrawArc(Vector2.Zero, (y1 - y0) * 0.5f + spread, 0f, Mathf.Tau, 28, new Color("#8ad0ff").WithAlpha(burn ? 0.8f : 0.3f), 1.6f, true);
                    ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                }
                break;
            }
            case "hull.centrifuge":
            {
                float ry = (b - t) * 0.5f + 34f;
                for (int k = 0; k < 6; k++)
                {
                    float a = tm * 0.5f + k * Mathf.Tau / 6f;
                    float y = cy + Mathf.Sin(a) * ry;
                    float x = mid + Mathf.Cos(a) * 18f;
                    if (Mathf.Cos(a) < 0f) continue; // 앞쪽 반만 보인다
                    ci.DrawLine(new Vector2(x - 2f, y), new Vector2(x + 2f, y), p.Col.Lightened(0.3f).WithAlpha(0.7f), 2f);
                }
                break;
            }
            case "exterior.ionring":
            {
                var o = _noseTip;
                for (int i = 0; i < 4; i++)
                {
                    if (FA.Hash(i, (int)(tm * 10f), 471) < 0.6f) continue;
                    var a0 = o + Vector2.FromAngle(-1f + i * 0.5f) * 24f;
                    var a1 = o + Vector2.FromAngle(-1f + (i + 1) * 0.5f) * 24f;
                    var m0 = (a0 + a1) * 0.5f + (a0 - o).Normalized() * (4f + 3f * FA.Hash(i, (int)(tm * 20f), 472));
                    ci.DrawPolyline(new[] { a0, m0, a1 }, new Color("#9fd8ff").WithAlpha(0.85f), 1f, true);
                }
                break;
            }
            case "exterior.radiator":
            {
                float load = Mathf.Clamp(_world.Power.ReactorOutput / Mathf.Max(1f, _world.Power.ReactorRated), 0f, 1f);
                foreach (int side in new[] { -1, 1 })
                {
                    var root = HullAt(l + wdt * 0.42f, side);
                    float len = 54f;
                    var poly = new[] { root + new Vector2(-16f, 0f), root + new Vector2(16f, 0f), root + new Vector2(10f, side * len), root + new Vector2(-24f, side * len) };
                    var hot = new Color("#ff6a2a").WithAlpha(0.08f + 0.28f * load);
                    ci.DrawPolygon(poly, new[] { hot, hot, hot.WithAlpha(0.02f), hot.WithAlpha(0.02f) });
                }
                break;
            }
            case "engine.warp":
            {
                float rx = wdt * 0.5f + 70f, ry = (b - t) * 0.5f + 56f;
                float q = Mathf.PosMod(tm * 0.2f, 1f);
                ci.DrawSetTransform(new Vector2(mid, cy), 0f, new Vector2(1f, ry / rx));
                ci.DrawArc(Vector2.Zero, rx * (0.85f + 0.15f * q), 0f, Mathf.Tau, 64, col.WithAlpha(0.25f * (1f - q)), 1.5f, true);
                ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                for (int k = 0; k < 3; k++)
                {
                    float a = tm * 0.3f + k * 2.1f;
                    ci.DrawArc(new Vector2(mid + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry), 8f, a + 1.2f, a + 2.4f, 6, Colors.White.WithAlpha(0.3f), 1f, true);
                }
                break;
            }
            case "exterior.forcefield":
            {
                // 뱃머리에서 꼬리로 쓸고 가는 밝은 띠
                float sweep = r - Mathf.PosMod(tm * 120f, wdt + 80f);
                foreach (var h in _hullSegs)
                {
                    if (h.K % 2 != 0) continue;
                    float d = Mathf.Abs(h.C.X - sweep);
                    if (d > 40f) continue;
                    var o = h.L(0f, 32f);
                    var hex = new Vector2[7];
                    for (int k = 0; k < 7; k++) hex[k] = o + Vector2.FromAngle(k * Mathf.Tau / 6f) * 6f;
                    ci.DrawPolyline(hex, col.Lightened(0.3f).WithAlpha(0.7f * (1f - d / 40f)), 1f, true);
                }
                break;
            }
            case "exterior.gravlens":
            {
                // 렌즈를 지나며 휘는 별빛
                for (int k = 0; k < 5; k++)
                {
                    float q = Mathf.PosMod(tm * 0.15f + k / 5f, 1f);
                    float y0 = (FA.Hash(k, 1, 473) - 0.5f) * 80f;
                    float x = _noseTip.X + 140f - q * 160f;
                    float bend = Mathf.Exp(-Mathf.Pow((x - _noseTip.X - 54f) / 24f, 2f)) * 10f * Mathf.Sign(y0);
                    FA.Dot(ci, new Vector2(x, _noseTip.Y + y0 + bend), 1f, Colors.White.WithAlpha(0.7f * (1f - q)));
                }
                break;
            }
            case "exterior.swarm":
            {
                int n = _hullSegs.Count;
                for (int g = 0; g < 3; g++)
                {
                    float q = tm * 0.6f + g * n / 3f;
                    var h = _hullSegs[(int)q % n];
                    float fr = q - Mathf.Floor(q);
                    for (int k = 0; k < 4; k++)
                        FA.Dot(ci, h.L(-16f + 32f * fr + (k - 1.5f) * 3f, 26f + Mathf.Sin(tm * 6f + k) * 1.2f), 1.1f, new Color("#f2994a"));
                    if (Mathf.PosMod(tm * 3f + g, 1f) < 0.15f) FA.Dot(ci, h.L(-16f + 32f * fr, 24f), 2f, new Color("#ffe08a").WithAlpha(0.8f));
                }
                break;
            }
            case "exterior.salvagescan":
            {
                var o = HullAt(r - wdt * 0.2f, 1) + new Vector2(0f, 12f);
                float a = Mathf.Pi / 2f + Mathf.Sin(tm * 0.5f) * 0.9f;
                var poly = new[] { o, o + Vector2.FromAngle(a - 0.25f) * 120f, o + Vector2.FromAngle(a + 0.25f) * 120f };
                ci.DrawPolygon(poly, new[] { col.WithAlpha(0.18f), col.WithAlpha(0f), col.WithAlpha(0f) });
                break;
            }
            case "sensor.gravwave":
            {
                var hub = HullAt(r - wdt * 0.3f, -1) + new Vector2(0f, -6f);
                float q = Mathf.PosMod(tm * 0.8f, 1f);
                float back = q < 0.5f ? q * 2f : 2f - q * 2f;
                FA.Dot(ci, hub + new Vector2(-wdt * 0.35f * back, 0f), 1.2f, col.Lightened(0.4f));
                FA.Dot(ci, hub + new Vector2(0f, -46f * back), 1.2f, col.Lightened(0.4f));
                break;
            }
        }
    }

    // ═══════════════════════════════ 배관 ═══════════════════════════════

    private static Vector2 Perp(Vector2[] pts, int i)
    {
        var d = i + 1 < pts.Length ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1];
        d = d.Normalized();
        return new Vector2(-d.Y, d.X);
    }

    private void VisPipeStatic(CanvasItem ci, in VisPlace p)
    {
        var pts = p.Pts!;
        var col = p.Col;
        switch (p.Key)
        {
            case "pipe.coolantdope":
            {
                // 첨가제 색띠 (네 칸마다) + 투입 병
                for (int i = 0; i < pts.Length; i += 4)
                {
                    var n = Perp(pts, i);
                    ci.DrawLine(pts[i] - n * 3.2f, pts[i] + n * 3.2f, col, 2.2f, true);
                }
                var bo = pts[0] + Perp(pts, 0) * 7f;
                FA.Box(ci, new Rect2(bo - new Vector2(2.2f, 3.5f), new Vector2(4.4f, 7f)), col.WithAlpha(0.7f), 1.5f, Colors.White.WithAlpha(0.5f));
                ci.DrawRect(new Rect2(bo - new Vector2(1.4f, 4.6f), new Vector2(2.8f, 1.4f)), new Color("#2a2a2a"));
                break;
            }
            case "pipe.heatpipe":
            {
                // 나란히 가는 구리 열 파이프 + 핀
                var off = new Vector2[pts.Length];
                for (int i = 0; i < pts.Length; i++) off[i] = pts[i] + Perp(pts, i) * 4f;
                ci.DrawPolyline(off, Cu.Darkened(0.3f), 2.4f, true);
                ci.DrawPolyline(off, Cu, 1.4f, true);
                for (int i = 0; i < off.Length; i += 2)
                {
                    var n = Perp(pts, i);
                    ci.DrawLine(off[i] - n * 2.6f, off[i] + n * 2.6f, Cu.Lightened(0.25f).WithAlpha(0.7f), 0.8f);
                }
                break;
            }
            case "pipe.crawler":
            {
                // 기는 로봇의 레일 집게 (세 칸마다)
                for (int i = 1; i < pts.Length; i += 3)
                {
                    var n = Perp(pts, i);
                    ci.DrawLine(pts[i] - n * 3.5f, pts[i] + n * 3.5f, FA.Steel4, 1.2f);
                    FA.Dot(ci, pts[i] + n * 3.5f, 0.8f, col);
                }
                break;
            }
            case "pipe.liquidmetal":
            {
                // 은빛 액체 금속 덧관
                ci.DrawPolyline(pts, new Color("#d8e0ea").WithAlpha(0.5f), 3f, true);
                ci.DrawPolyline(pts, new Color("#ffffff").WithAlpha(0.35f), 0.8f, true);
                break;
            }
            case "pipe.couplers":
            {
                // 빠른 이음 고리 (두 칸마다 · 파랑 · 주황 번갈아)
                for (int i = 1; i < pts.Length; i += 2)
                {
                    var n = Perp(pts, i);
                    var c2 = (i / 2) % 2 == 0 ? new Color("#3a8fd9") : new Color("#f2994a");
                    ci.DrawLine(pts[i] - n * 3.6f, pts[i] + n * 3.6f, c2, 3f, true);
                    ci.DrawLine(pts[i] - n * 3.6f, pts[i] + n * 3.6f, c2.Lightened(0.4f).WithAlpha(0.6f), 0.8f, true);
                }
                break;
            }
        }
    }

    private void VisPipeLive(CanvasItem ci, in VisPlace p)
    {
        var pts = p.Pts!;
        if (pts.Length < 2) return;
        bool flow = p.Pipe is PipeSegment s && PipeFlowing(s);
        switch (p.Key)
        {
            case "pipe.crawler":
            {
                // 관을 따라 오가는 작은 로봇 (다리 넷 · 앞 등)
                float q = 0.5f + 0.5f * Mathf.Sin(_time * 0.25f + p.K * 1.3f);
                float fi = q * (pts.Length - 1);
                int i = Mathf.Clamp((int)fi, 0, pts.Length - 2);
                var o = pts[i].Lerp(pts[i + 1], fi - i);
                var d = (pts[i + 1] - pts[i]).Normalized();
                var n = new Vector2(-d.Y, d.X);
                ci.DrawColoredPolygon(new[] { o - d * 3f - n * 2f, o + d * 3f - n * 2f, o + d * 3f + n * 2f, o - d * 3f + n * 2f }, new Color("#f2994a"));
                for (int k = -1; k <= 1; k += 2) { ci.DrawLine(o + d * k * 2f + n * 2f, o + d * k * 2.6f + n * 3.6f, FA.Steel4, 0.6f); ci.DrawLine(o + d * k * 2f - n * 2f, o + d * k * 2.6f - n * 3.6f, FA.Steel4, 0.6f); }
                FA.Led(ci, o + d * 3f * Mathf.Sign(Mathf.Cos(_time * 0.25f + p.K * 1.3f)), new Color("#fff0c0"), 0.8f, 0.7f);
                break;
            }
            case "pipe.liquidmetal":
            {
                if (!flow) break;
                for (int k = 0; k < pts.Length; k += 2)
                {
                    float q = Mathf.PosMod(_time * 0.8f + k * 0.37f, 1f);
                    int i = Mathf.Min(k, pts.Length - 2);
                    FA.Dot(ci, pts[i].Lerp(pts[i + 1], q), 1.2f, new Color("#f0f4fa"));
                }
                break;
            }
        }
    }

    // ═══════════════════════════════ 배 전체 망 ═══════════════════════════════

    private void VisNetStatic(CanvasItem ci, in VisPlace p)
    {
        var pts = p.Pts!;
        var col = p.Col;
        switch (p.Key)
        {
            case "trunk.superconduct":
            {
                // 초전도 간선: 서리 십자 · 세 칸마다 푸른 냉각 고리
                for (int i = 0; i < pts.Length; i++)
                {
                    var o = pts[i];
                    ci.DrawLine(o + new Vector2(-1.6f, -1.6f), o + new Vector2(1.6f, 1.6f), Frost.WithAlpha(0.5f), 0.6f);
                    ci.DrawLine(o + new Vector2(-1.6f, 1.6f), o + new Vector2(1.6f, -1.6f), Frost.WithAlpha(0.5f), 0.6f);
                    if (i % 3 == 1)
                    {
                        var n = Perp(pts, i);
                        ci.DrawLine(o - n * 3f, o + n * 3f, new Color("#5aa8ff"), 2.2f, true);
                    }
                }
                break;
            }
            case "wall.fiber":
            {
                // 광섬유 감지망: 가는 청록 선
                ci.DrawPolyline(pts, col.WithAlpha(0.35f), 0.7f, true);
                foreach (var o in pts) FA.Dot(ci, o, 0.6f, col.WithAlpha(0.5f));
                break;
            }
        }
    }

    private void VisNetLive(CanvasItem ci, in VisPlace p)
    {
        var pts = p.Pts!;
        if (p.Key != "wall.fiber" || pts.Length < 2 || p.Link is not NetLink l || l.Cut) return;
        switch (p.Key)
        {
            case "wall.fiber":
            {
                // 빛 신경: 선을 따라 달리는 빛 알갱이
                float q = Mathf.PosMod(_time * 0.9f + l.Id * 0.21f, 1f);
                float fi = q * (pts.Length - 1);
                int i = Mathf.Clamp((int)fi, 0, pts.Length - 2);
                FA.Dot(ci, pts[i].Lerp(pts[i + 1], fi - i), 1.2f, p.Col.Lightened(0.5f));
                break;
            }
        }
    }

    // ═══════════════════════════════ 빛 (더하기 층) ═══════════════════════════════

    private void VisGlow(CanvasItem ci, in VisPlace p)
    {
        var col = p.Col;
        float t = _time + p.K * 0.5f;
        switch (p.Key)
        {
            case "panel.zeropoint":
                if (Running(p.F)) FA.Dot(ci, Q(p.Bound.Grow(-3f), 0.5f, 0.32f), 11f, col.WithAlpha(0.18f + 0.06f * Mathf.Sin(t * 1.4f)));
                break;
            case "engine.fusion":
                FA.Dot(ci, p.Bound.GetCenter(), Mathf.Min(p.Bound.Size.X, p.Bound.Size.Y) * 0.55f, new Color("#ff5ad8").WithAlpha(Running(p.F) ? 0.1f : 0.02f));
                break;
            case "sensor.beacon":
                if (Running(p.F) && Mathf.PosMod((_time + p.K * 0.61f) * 0.9f, 1f) < 0.12f) FA.Dot(ci, Q(p.Bound.Grow(-3f), 0.06f, 0.72f) + new Vector2(6.5f, 1.5f), 22f, new Color("#ff3a2a").WithAlpha(0.25f));
                break;
            case "bridge.aicaptain":
                FaceXf(ci, p.Face);
                FA.Dot(ci, new Vector2(0f, 4f), 12f, col.WithAlpha(0.08f + 0.05f * Mathf.Sin(t * 0.9f)));
                NoXf(ci);
                break;
            case "workshop.forge":
                FaceXf(ci, p.Face);
                FA.Dot(ci, new Vector2(0f, 6f), 18f, new Color("#ff8a3c").WithAlpha(0.12f + 0.05f * Mathf.Sin(t * 2.3f)));
                NoXf(ci);
                break;
            case "lifesupport.algaetank":
                FaceXf(ci, p.Face);
                FA.Dot(ci, new Vector2(0f, 6f), 15f, new Color("#5fe07a").WithAlpha(0.09f));
                NoXf(ci);
                break;
            case "medbay.vat":
                FaceXf(ci, p.Face);
                FA.Dot(ci, new Vector2(0f, 5f), 11f, new Color("#ff9ab8").WithAlpha(0.1f));
                NoXf(ci);
                break;
            case "lounge.homeworld":
            {
                // 창에서 쏟아지는 하늘빛
                FaceXf(ci, p.Face);
                var sky = new Color("#9ad0ff");
                ci.DrawPolygon(new[] { new Vector2(-13f, 4f), new Vector2(13f, 4f), new Vector2(18f, 26f), new Vector2(-18f, 26f) }, new[] { sky.WithAlpha(0.12f), sky.WithAlpha(0.12f), sky.WithAlpha(0f), sky.WithAlpha(0f) });
                NoXf(ci);
                break;
            }
            case "corridor.biolamp":
            {
                float br = 0.5f + 0.5f * Mathf.Sin(_time * 0.7f + p.Face.Floor.X * 0.4f + p.Face.Floor.Y * 0.3f);
                FaceXf(ci, p.Face);
                var teal = new Color("#5fe0d0");
                ci.DrawPolygon(new[] { new Vector2(-16f, -4f), new Vector2(16f, -4f), new Vector2(16f, 12f), new Vector2(-16f, 12f) }, new[] { teal.WithAlpha(0.14f * br + 0.04f), teal.WithAlpha(0.14f * br + 0.04f), teal.WithAlpha(0f), teal.WithAlpha(0f) });
                NoXf(ci);
                break;
            }
            case "workshop.assembler":
                FaceXf(ci, p.Face);
                FA.Dot(ci, new Vector2(0f, 3f), 12f, col.WithAlpha(0.07f));
                NoXf(ci);
                break;
        }
    }

    // ═══════════════════════════════ 갈림길 — 배 이름 문장 ═══════════════════════════════

    /// <summary>고른 갈림길마다 그 기술 아이콘을 외판 위(뱃머리 쪽)에 문장처럼 단다 + 배 이름 (가장 최근 것).</summary>
    private void PaintIdentityCrest(CanvasItem ci)
    {
        if (_world.TechWeb is not TechWebSystem tw || _hullSegs.Count == 0) return;
        var chosen = new System.Collections.Generic.List<EraTech>();
        foreach (var f in TechWeb.Forks)
        {
            int side = tw.Side(f.Id);
            if (side < 0) continue;
            if (TechWeb.Find(side == 0 ? f.A : f.B) is EraTech t) chosen.Add(t);
        }
        if (chosen.Count == 0) return;
        var (l, r, top, _) = HullBox();
        var anchor = HullAt(r - (r - l) * 0.18f, -1) + new Vector2(0f, -14f);
        float w = chosen.Count * 16f + 8f;
        var plate = new Rect2(anchor - new Vector2(w, 9f), new Vector2(w, 18f));
        Gfx.RoundRect(ci, plate, new Color("#10141c").WithAlpha(0.92f), 4, new Color("#c8a050"), 1);
        for (int i = 0; i < chosen.Count; i++)
            TechIcons.Draw(ci, chosen[i], plate.Position + new Vector2(12f + i * 16f, 9f), 6.2f, 1);
        if (tw.Epithets.Count > 0)
            Gfx.TextRight(ci, Fonts.Bold, new Vector2(plate.End.X, plate.Position.Y - 3f), tw.Epithets[^1], 8, new Color("#e8d8a8"));
    }
}
