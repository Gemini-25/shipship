using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;
using FA = ShipSim.View.FixtureArt;

namespace ShipSim.View;

/// <summary>
/// 압축-마 기술 30의 배 모습 (TechWebV18 · TechLookTable 의 "압축-마" 줄).
/// 방 벽 장치(절임 병 · 온도 기록판 · 곡물통 · 기름통 · 끈 고리 판 · 발언 막대 · 악단 벽보 · 세척 표지)는 면 좌표(x −16~16, y 방 쪽 +)로,
/// 설비 곁 덧붙이(발효종 병 · 냄비 감지 고리 · 깃털 막대 · 끈 · 누름틀 · 흙 관 · 맑기 눈금 · 덩어리 더미 · 코일 판 · 조임 고리 · 버클 · 예비 기억 상자 · 둘째 기록 상자 · 주파수 화면 · 촛불 · 손목띠 · 투구)는 설비 칸에,
/// 외판(분사구 꼬리표 · 덧창) · 바깥(자이로 고리 · 접안 고리 · 추적 안테나)은 선체에 그린다. 그리기는 Core 를 바꾸지 않는다.
/// </summary>
public partial class ShipView
{
    private static HashSet<string>? _v18Keys;
    private static HashSet<string> V18Keys => _v18Keys ??= new HashSet<string>(TechWebV18.Nodes.Select(n => n.Visual));
    private static readonly Color VBrine = new("#c8d870"), VPickle = new("#5a8a3a"), VFrost = new("#dff4ff"), VGrain = new("#d8b878"), VTether = new("#3a6ab0");

    /// <summary>설비 칸 안의 한 점 (0~1).</summary>
    private static Vector2 At(Rect2 b, float u, float v) => b.Position + new Vector2(b.Size.X * u, b.Size.Y * v);

    private void VisV18Static(CanvasItem ci, in VisPlace p)
    {
        if (p.Row.Anchor == VAnchor.Exterior) { VisV18OutsideStatic(ci, p); return; }
        var b = p.Bound.Grow(-3f);
        switch (p.Row.Anchor)
        {
            case VAnchor.Room: FaceXf(ci, p.Face); break;
            case VAnchor.Hull: HullXf(ci, p.Hull); break;
        }
        switch (p.Key)
        {
            // ── 방 벽 장치 ──
            case "galley.picklejars":
            {
                ci.Box(RR(-14f, 2f, 14f, 3.2f), FA.Steel4); // 선반
                for (int i = 0; i < 5; i++)
                {
                    float x0 = -12f + i * 5.4f;
                    FA.Box(ci, RR(x0, -6f, x0 + 4f, 2f), VBrine.WithAlpha(0.45f), 1f, new Color(1, 1, 1, 0.4f));
                    ci.Box(RR(x0 + 0.5f, -3f, x0 + 3.5f, 1.5f), i % 2 == 0 ? VPickle : new Color("#c84a2a")); // 절인 것
                    ci.Box(RR(x0 - 0.2f, -7f, x0 + 4.2f, -6f), FA.Brass);
                }
                FA.Box(ci, RR(6f, 5f, 13f, 12f), new Color("#e8e0d0"), 2f, new Color("#a89070")); // 소금 자루
                break;
            }
            case "galley.templog":
            {
                FA.Box(ci, RR(-9f, -7f, 9f, 9f), new Color("#e8ecf0"), 1f, FA.Chrome);
                for (int i = 0; i < 5; i++) ci.DrawLine(new Vector2(-7f, -4f + i * 2.6f), new Vector2(-1f + FA.Hash(p.K, i, 1801) * 6f, -4f + i * 2.6f), new Color("#2a5aa8"), 0.6f);
                ci.DrawLine(new Vector2(9f, -6f), new Vector2(12f, 2f), new Color("#c8a050"), 0.8f); // 매단 연필
                for (int i = 0; i < 6; i++) FA.Dot(ci, new Vector2(-8f + FA.Hash(p.K, i, 1802) * 16f, -6f + FA.Hash(p.K, i, 1803) * 3f), 0.6f, VFrost.WithAlpha(0.7f)); // 서리
                break;
            }
            case "storage.sealbins":
            {
                for (int i = 0; i < 3; i++)
                {
                    float x0 = -13f + i * 9f;
                    FA.Box(ci, RR(x0, -4f, x0 + 8f, 9f), new Color("#d8dce0").WithAlpha(0.9f), 2f, new Color("#8a929e"));
                    ci.Box(RR(x0 + 1f, 2f, x0 + 7f, 8f), VGrain); // 곡물
                    ci.Box(RR(x0 - 0.4f, -5.4f, x0 + 8.4f, -3.6f), new Color("#2a2a2e")); // 고무 패킹 뚜껑
                    FA.Dot(ci, new Vector2(x0 + 0.6f, -4.5f), 0.7f, FA.Chrome); // 잠금 집게
                    FA.Dot(ci, new Vector2(x0 + 7.4f, -4.5f), 0.7f, FA.Chrome);
                }
                break;
            }
            case "galley.greasejug":
            {
                FA.Box(ci, RR(-6f, -2f, 4f, 11f), new Color("#c8a040").WithAlpha(0.7f), 2f, new Color("#8a6a2a")); // 기름통
                ci.Box(RR(-5f, 4f, 3f, 10f), new Color("#a07a20").WithAlpha(0.8f));
                ci.Poly(new[] { new Vector2(-4f, -6f), new Vector2(2f, -6f), new Vector2(-0.4f, -2f), new Vector2(-1.6f, -2f) }, FA.Steel4); // 깔때기
                FA.Box(ci, RR(6f, -1f, 13f, 4f), new Color("#f0e8c8"), 0.5f); // 손글씨 딱지
                ci.DrawLine(new Vector2(7f, 1.5f), new Vector2(12f, 1.5f), new Color("#3a3a3a"), 0.5f);
                break;
            }
            case "airlock.tetherboard":
            {
                FA.Box(ci, RR(-12f, -7f, 12f, 4f), new Color("#3a4250"), 1f, FA.Steel4);
                for (int i = 0; i < 4; i++)
                {
                    float x = -9f + i * 6f;
                    FA.Dot(ci, new Vector2(x, -4f), 0.9f, FA.Chrome); // 걸이
                    ci.Arc(new Vector2(x, 0f), 2.6f, 0f, Mathf.Pi, 8, VTether, 1f, true); // 감긴 끈
                    FA.Ring(ci, new Vector2(x, 3.4f), 1f, FA.Chrome, 0.6f, 8); // 고리쇠
                }
                break;
            }
            case "mess.talkingstick":
            {
                FA.Dot(ci, new Vector2(-8f, -3f), 0.9f, FA.Chrome); // 걸이 둘
                FA.Dot(ci, new Vector2(4f, -3f), 0.9f, FA.Chrome);
                ci.DrawLine(new Vector2(-10f, -2f), new Vector2(6f, -2f), new Color("#8a5a2a"), 2f); // 새긴 막대
                for (int i = 0; i < 5; i++) ci.DrawLine(new Vector2(-8f + i * 3f, -3f), new Vector2(-7f + i * 3f, -1f), new Color("#4a2a12"), 0.5f);
                FA.Box(ci, RR(8f, -6f, 14f, 6f), new Color("#e8e2d4"), 0.5f); // 차례 판
                for (int i = 0; i < 4; i++) FA.Dot(ci, new Vector2(9.5f, -4f + i * 2.6f), 0.5f, i == 1 ? FA.Danger : new Color("#3a3a3a"));
                break;
            }
            case "lounge.bandposter":
            {
                FA.Box(ci, RR(-13f, -7f, -1f, 6f), new Color("#2a1a3a"), 0.5f); // 벽보
                ci.Circle(new Vector2(-7f, -1.5f), 3f, new Color("#e05aa8")); // 동그란 무늬
                ci.DrawLine(new Vector2(-12f, 4f), new Vector2(-2f, 4f), new Color("#f8e070"), 0.8f);
                ci.DrawLine(new Vector2(6f, 12f), new Vector2(6f, 0f), FA.Steel4, 0.8f); // 보면대
                ci.Poly(new[] { new Vector2(2f, 0f), new Vector2(10f, 0f), new Vector2(9f, -5f), new Vector2(3f, -5f) }, FA.Steel3);
                for (int i = 0; i < 3; i++) ci.DrawLine(new Vector2(3.5f, -4f + i * 1.3f), new Vector2(8.5f, -4f + i * 1.3f), new Color(1, 1, 1, 0.5f), 0.4f); // 악보
                break;
            }
            case "workshop.eyewashsign":
            {
                FA.Box(ci, RR(-9f, -7f, 3f, 5f), new Color("#1f8a4a"), 1f); // 초록 표지
                ci.Circle(new Vector2(-3f, -1f), 3.2f, Colors.White);
                ci.Circle(new Vector2(-3f, -1f), 1.4f, new Color("#1f8a4a")); // 눈 모양
                FA.Box(ci, RR(5f, -6f, 13f, 7f), new Color("#ece6d6"), 0.5f); // 점검표
                for (int i = 0; i < 4; i++) { ci.DrawLine(new Vector2(6.5f, -3.5f + i * 2.6f), new Vector2(11.5f, -3.5f + i * 2.6f), new Color("#3a3a3a"), 0.4f); FA.Dot(ci, new Vector2(6f, -3.5f + i * 2.6f), 0.4f, FA.Good); }
                break;
            }
            // ── 설비 곁 ──
            case "oven.starter":
            {
                var j = At(b, 0.95f, 0.05f);
                FA.Box(ci, new Rect2(j - new Vector2(2.4f, 0f), new Vector2(4.8f, 6f)), new Color("#f0e8d8").WithAlpha(0.8f), 1f, FA.Chrome); // 발효종 병
                ci.Box(new Rect2(j + new Vector2(-1.8f, 2.4f), new Vector2(3.6f, 3f)), new Color("#e8d8a8"));
                ci.Arc(At(b, 0.05f, 0.95f), 3f, Mathf.Pi, Mathf.Tau, 10, new Color("#a8844f"), 1.4f, true); // 버들 바구니
                ci.DrawLine(At(b, 0.05f, 0.95f) - new Vector2(3f, 0f), At(b, 0.05f, 0.95f) + new Vector2(3f, 0f), new Color("#8a6a3a"), 0.8f);
                break;
            }
            case "stove.potring":
            {
                var c = b.GetCenter();
                FA.Ring(ci, c, Mathf.Min(b.Size.X, b.Size.Y) * 0.32f, new Color("#d0402e").WithAlpha(0.6f), 1f, 20); // 감지 고리
                FA.Box(ci, new Rect2(At(b, 0.82f, 0.02f), new Vector2(5f, 3.4f)), new Color("#0b1f29"), 0.5f, FA.Steel4); // 작은 경고 화면
                break;
            }
            case "cattower.toys":
            {
                var s = At(b, 0.9f, 0.9f);
                ci.DrawLine(s, s + new Vector2(-2f, -9f), new Color("#c8a46a"), 0.8f); // 깃털 막대
                ci.Box(new Rect2(At(b, 0.02f, 0.8f), new Vector2(6f, 4f)), new Color("#8a6a4a")); // 긁개 판
                for (int i = 0; i < 4; i++) ci.DrawLine(At(b, 0.02f, 0.8f) + new Vector2(0.6f + i * 1.4f, 0.4f), At(b, 0.02f, 0.8f) + new Vector2(0.6f + i * 1.4f, 3.6f), new Color("#5a3a20"), 0.4f);
                break;
            }
            case "plantrack.straps":
            {
                for (int i = 0; i < 3; i++)
                {
                    var c = At(b, 0.2f + i * 0.3f, 0.3f);
                    FA.Ring(ci, c, 2.8f, new Color("#3a8a4a"), 0.8f, 12); // 받침 고리
                    ci.DrawLine(c + new Vector2(-2.8f, 0f), c + new Vector2(2.8f, 1.6f), new Color("#3a8a4a"), 0.6f);
                }
                break;
            }
            case "insectfarm.press":
            {
                var s = At(b, 0.02f, 0.95f);
                FA.Box(ci, new Rect2(s - new Vector2(0f, 4f), new Vector2(5f, 4f)), FA.Steel3, 0.5f, FA.Steel4); // 누름틀
                ci.DrawLine(s + new Vector2(2.5f, -4f), s + new Vector2(2.5f, -7f), FA.Chrome, 0.8f);
                FA.Ring(ci, At(b, 0.85f, 0.02f), 2.6f, new Color("#a8844f"), 0.8f, 12); // 채반
                for (int i = 0; i < 5; i++) FA.Dot(ci, At(b, 0.85f, 0.02f) + new Vector2(-1.5f + FA.Hash(p.K, i, 1810) * 3f, -1.5f + FA.Hash(p.K, i, 1811) * 3f), 0.4f, new Color("#7a5a3a"));
                break;
            }
            case "composter.loop":
            {
                var a = At(b, 0.9f, 0.2f);
                ci.Polyline(new[] { a, a + new Vector2(5f, 0f), a + new Vector2(6f, 6f), a + new Vector2(6f, 14f) }, new Color("#6a4a2a"), 1.6f, true); // 흙 관
                ci.DrawLine(At(b, 0.5f, 0.15f), At(b, 0.5f, -0.1f), FA.Chrome, 0.7f); // 온도 꽂이
                FA.Dot(ci, At(b, 0.5f, -0.1f), 1f, FA.Ember);
                break;
            }
            case "greywater.gauge":
            {
                var r = new Rect2(At(b, -0.05f, 0.1f), new Vector2(2.4f, b.Size.Y * 0.8f));
                ci.Box(r, FA.Steel2); // 맑기 눈금 띠
                for (int i = 0; i < 5; i++) ci.DrawLine(r.Position + new Vector2(0f, r.Size.Y * i / 4f), r.Position + new Vector2(2.4f, r.Size.Y * i / 4f), Colors.White.WithAlpha(0.5f), 0.4f);
                break;
            }
            case "compactor.bales":
            {
                for (int i = 0; i < 3; i++)
                {
                    var o = At(b, 1f, 0.55f) + new Vector2(1f + (i % 2) * 3f, i * 3f);
                    FA.Box(ci, new Rect2(o, new Vector2(5f, 3f)), new Color("#7a6a50"), 0.5f, new Color("#4a3a2a"));
                    ci.DrawLine(o + new Vector2(1.6f, 0f), o + new Vector2(1.6f, 3f), new Color("#c8c8c8"), 0.4f); // 철사
                }
                break;
            }
            case "magboots.coils":
            {
                var r = new Rect2(At(b, 0.05f, 0.98f), new Vector2(b.Size.X * 0.9f, 2.6f));
                ci.Box(r, FA.Steel1); // 코일 판
                for (int i = 0; i < 6; i++) FA.Ring(ci, r.Position + new Vector2(2f + i * r.Size.X / 6f, 1.3f), 1f, FA.Copper, 0.5f, 8);
                break;
            }
            case "cargonet.ratchet":
            {
                foreach (var (u, v) in new[] { (0.02f, 0.02f), (0.98f, 0.02f), (0.02f, 0.98f), (0.98f, 0.98f) })
                {
                    var c = At(b, u, v);
                    FA.Box(ci, new Rect2(c - new Vector2(1.6f, 1.2f), new Vector2(3.2f, 2.4f)), new Color("#e0a020"), 0.5f); // 조임 고리
                    ci.DrawLine(c, c + new Vector2(u < 0.5f ? 2.4f : -2.4f, v < 0.5f ? 2.4f : -2.4f), new Color("#d0402e"), 0.6f);
                }
                break;
            }
            case "crashseat.harness":
            {
                var c = b.GetCenter();
                FA.Ring(ci, c, 3f, new Color("#6ee7b7").WithAlpha(0.6f), 1f, 14); // 빛나는 버클
                ci.Box(new Rect2(c + new Vector2(-6f, -7f), new Vector2(3f, 2.2f)), new Color("#2a2a2a")); // 어깨 패드
                ci.Box(new Rect2(c + new Vector2(3f, -7f), new Vector2(3f, 2.2f)), new Color("#2a2a2a"));
                break;
            }
            case "server.bootcache":
            {
                var o = At(b, 1f, 0.3f);
                FA.Box(ci, new Rect2(o, new Vector2(4f, 7f)), new Color("#1a3a6a"), 0.5f, FA.Steel4); // 예비 기억 상자
                ci.DrawLine(o + new Vector2(0f, 3.5f), o + new Vector2(-2f, 3.5f), new Color("#2a5aa8"), 0.8f);
                break;
            }
            case "vault.mirror":
            {
                var o = At(b, -0.1f, 0.85f);
                FA.Box(ci, new Rect2(o - new Vector2(4f, 3f), new Vector2(5f, 4f)), new Color("#e8641e"), 0.8f, new Color("#9a3a10")); // 둘째 기록 상자
                ci.DrawLine(o, At(b, 0.25f, 0.2f), new Color("#5fd0c8").WithAlpha(0.7f), 0.6f); // 광섬유
                break;
            }
            case "listening.scope":
            {
                var o = At(b, 0.55f, -0.12f);
                FA.Box(ci, new Rect2(o, new Vector2(8f, 3.6f)), new Color("#06140c"), 0.5f, FA.Steel4); // 주파수 화면
                for (int i = 0; i < 5; i++) ci.DrawLine(o + new Vector2(1f + i * 1.5f, 3.2f), o + new Vector2(1f + i * 1.5f, 3.2f - 1f - FA.Hash(p.K, i, 1820) * 1.8f), FA.Good.WithAlpha(0.6f), 0.8f);
                break;
            }
            case "memorial.candles":
            {
                for (int i = 0; i < 4; i++) ci.Box(new Rect2(At(b, 0.15f + i * 0.22f, 1.02f), new Vector2(1.6f, 2.6f)), new Color("#f0e8d0")); // 촛불 줄
                ci.Poly(new[] { At(b, 0.92f, 0.95f), At(b, 1.05f, 0.9f), At(b, 1.0f, 1.05f) }, new Color("#f0e0f0")); // 종이꽃
                break;
            }
            case "clothesrack.ions":
            {
                var o = At(b, 1f, 0.1f);
                FA.Box(ci, new Rect2(o, new Vector2(2.4f, 8f)), FA.Steel3, 0.5f); // 손목띠 걸이
                for (int i = 0; i < 3; i++) FA.Ring(ci, o + new Vector2(1.2f, 1.6f + i * 2.4f), 0.9f, new Color("#3a6ab0"), 0.5f, 8);
                break;
            }
            case "heatsuit.visor":
            {
                var o = At(b, 0.85f, 0.85f);
                ci.Box(new Rect2(o - new Vector2(4f, 0f), new Vector2(8f, 1f)), FA.Steel4); // 투구 선반
                ci.Circle(o - new Vector2(0f, 2.6f), 2.6f, new Color("#c8ccd4"));
                ci.Box(new Rect2(o - new Vector2(1.8f, 3.4f), new Vector2(3.6f, 1.6f)), new Color("#d8a830")); // 금빛 얼굴창
                break;
            }
            // ── 외판 ──
            case "hull.rcstags":
            {
                FA.Box(ci, RR(-4f, 10f, 4f, 15f), FA.Steel3, 1f, FA.Steel4); // 분사구 덮개
                ci.DrawLine(new Vector2(2f, 15f), new Vector2(4f, 20f), new Color("#c8c0a8"), 0.5f); // 꼬리표 끈
                FA.Box(ci, RR(3f, 19f, 7f, 22f), new Color("#d0402e"), 0.5f); // 시험 꼬리표
                break;
            }
            case "hull.shutters":
            {
                for (int i = 0; i < 4; i++) ci.Box(RR(-10f + i * 5f, 9f, -6f + i * 5f, 16f), new Color("#5a6270").Lightened(i % 2 * 0.1f)); // 접힌 덧창
                ci.DrawLine(new Vector2(-11f, 8.5f), new Vector2(11f, 8.5f), FA.Chrome, 0.8f); // 경첩 줄
                break;
            }
        }
        NoXf(ci);
    }

    /// <summary>바깥 장치 (선체 둘레에 붙는다).</summary>
    private void VisV18OutsideStatic(CanvasItem ci, in VisPlace p)
    {
        if (_hullSegs.Count == 0) return;
        var (l, r, t, bt) = HullBox();
        float wdt = r - l, mid = (l + r) * 0.5f;
        switch (p.Key)
        {
            case "exterior.gyro":
            {
                var o = HullAt(l + wdt * 0.25f, 1) + new Vector2(0f, 10f);
                FA.Box(ci, new Rect2(o - new Vector2(3f, 10f), new Vector2(6f, 6f)), FA.Steel2, 1f, FA.Steel4); // 받침 기둥
                FA.Ring(ci, o, 7f, FA.Steel4, 2.2f, 28); // 자이로 고리
                FA.Ring(ci, o, 4f, FA.Steel3, 1.2f, 20);
                FA.Dot(ci, o, 1.4f, FA.Chrome);
                break;
            }
            case "exterior.dockring":
            {
                var o = HullAt(r - wdt * 0.15f, -1) + new Vector2(0f, -8f);
                FA.Box(ci, new Rect2(o - new Vector2(5f, -2f), new Vector2(10f, 6f)), FA.Steel2, 1f, FA.Steel4); // 접안 목
                FA.Ring(ci, o, 7f, FA.Steel4, 2.4f, 28); // 바깥 씰
                FA.Ring(ci, o, 5f, new Color("#2a2a2e"), 1.6f, 24); // 안쪽 씰
                for (int i = 0; i < 6; i++) FA.Dot(ci, o + Vector2.FromAngle(i * Mathf.Tau / 6f) * 7f, 0.8f, FA.WarnYellow); // 집게
                break;
            }
            case "exterior.wreckwatch":
            {
                var o = HullAt(mid - wdt * 0.1f, -1) + new Vector2(0f, -3f);
                ci.DrawLine(o, o + new Vector2(0f, -9f), FA.Steel4, 1.2f); // 돛대
                ci.Arc(o + new Vector2(0f, -10f), 4f, Mathf.Pi * 1.1f, Mathf.Pi * 1.9f, 10, FA.Chrome, 1.4f, true); // 접시
                FA.Box(ci, new Rect2(o - new Vector2(3f, 1f), new Vector2(6f, 3f)), FA.Steel3, 0.5f);
                break;
            }
        }
    }

    private void VisV18Live(CanvasItem ci, in VisPlace p)
    {
        var b = p.Bound.Grow(-3f);
        float t = _time + p.K * 0.53f;
        bool on = p.Room is not Room rm || rm.Powered && !rm.LightsOut;
        switch (p.Key)
        {
            case "stove.potring":
            {
                bool hot = p.F != null && _world.Maneuver.PotOn(p.F);
                var c = b.GetCenter();
                if (hot && on) FA.Ring(ci, c, Mathf.Min(b.Size.X, b.Size.Y) * 0.32f + FA.Pulse(t, 4f), new Color("#ff5c6c").WithAlpha(0.5f), 0.8f, 20); // 냄비를 지켜본다
                FA.Led(ci, At(b, 0.82f, 0.02f) + new Vector2(2.5f, 1.7f), hot ? FA.Amber : FA.Good, on ? 0.7f : 0.05f, 0.8f);
                break;
            }
            case "cattower.toys":
            {
                var s = At(b, 0.9f, 0.9f) + new Vector2(-2f, -9f);
                var tip = s + Vector2.FromAngle(-Mathf.Pi * 0.5f + Mathf.Sin(t * 2.3f) * 0.6f) * 3f;
                FA.Line(ci, s, tip, new Color("#c8a46a"), 0.5f);
                FA.Dot(ci, tip, 1.1f, new Color("#e05aa8")); // 흔들리는 깃털
                break;
            }
            case "greywater.gauge":
            {
                var r = new Rect2(At(b, -0.05f, 0.1f), new Vector2(2.4f, b.Size.Y * 0.8f));
                float q = _world.Flow.WaterQuality;
                float h = r.Size.Y * Mathf.Clamp(q, 0f, 1f);
                ci.Box(new Rect2(r.Position + new Vector2(0.4f, r.Size.Y - h), new Vector2(1.6f, h)), new Color("#7a5a2a").Lerp(new Color("#4aa3e0"), q).WithAlpha(0.85f)); // 탁하면 갈색
                break;
            }
            case "magboots.coils":
            {
                var r = new Rect2(At(b, 0.05f, 0.98f), new Vector2(b.Size.X * 0.9f, 2.6f));
                int i = (int)Mathf.PosMod(t * 3f, 6f);
                if (on) FA.Dot(ci, r.Position + new Vector2(2f + i * r.Size.X / 6f, 1.3f), 1.2f, new Color("#ffb070").WithAlpha(0.8f)); // 빛이 돈다
                break;
            }
            case "server.bootcache":
            {
                var o = At(b, 1f, 0.3f);
                for (int i = 0; i < 3; i++) FA.Led(ci, o + new Vector2(2f, 1.5f + i * 2f), new Color("#5a9aff"), on && FA.Hash(p.K, i + (int)(t * 5f), 1830) < 0.6f ? 0.9f : 0.1f, 0.5f);
                break;
            }
            case "vault.mirror":
            {
                var a = At(b, -0.1f, 0.85f);
                var z = At(b, 0.25f, 0.2f);
                float q = Mathf.PosMod(t * 0.8f, 1f);
                if (on) FA.Dot(ci, a.Lerp(z, q), 0.6f, FA.Cyan.WithAlpha(1f - q)); // 사본이 흐른다
                break;
            }
            case "listening.scope":
            {
                var o = At(b, 0.55f, -0.12f);
                if (!on) break;
                for (int i = 0; i < 5; i++) { float h = 1f + 1.8f * (0.5f + 0.5f * Mathf.Sin(t * (4f + i) + i)); FA.Line(ci, o + new Vector2(1f + i * 1.5f, 3.2f), o + new Vector2(1f + i * 1.5f, 3.2f - h), FA.Good, 0.8f); }
                break;
            }
            case "memorial.candles":
            {
                for (int i = 0; i < 4; i++)
                {
                    var c = At(b, 0.15f + i * 0.22f, 1.02f) + new Vector2(0.8f, 0f);
                    float fl = 0.7f + 0.3f * Mathf.Sin(t * (6f + i) + i);
                    ci.Poly(new[] { c + new Vector2(-0.6f, 0f), c + new Vector2(0.6f, 0f), c + new Vector2(Mathf.Sin(t + i) * 0.3f, -2f * fl) }, new Color("#ffcc66").WithAlpha(0.8f));
                }
                break;
            }
            case "clothesrack.ions":
            {
                var o = At(b, 1f, 0.1f);
                if (on) for (int i = 0; i < 3; i++) { float q = Mathf.PosMod(t * 1.2f + i / 3f, 1f); FA.Dot(ci, o + new Vector2(-1f - q * 4f, 2f + i * 2.4f), 0.4f, FA.Cyan.WithAlpha(0.7f * (1f - q))); } // 이온이 흩어진다
                break;
            }
            case "exterior.gyro":
            {
                if (_hullSegs.Count == 0) break;
                var (l, r, _, _) = HullBox();
                var o = HullAt(l + (r - l) * 0.25f, 1) + new Vector2(0f, 10f);
                float a = t * 1.4f;
                for (int k = 0; k < 3; k++) FA.Line(ci, o + Vector2.FromAngle(a + k * Mathf.Tau / 3f) * 4f, o + Vector2.FromAngle(a + k * Mathf.Tau / 3f) * 7f, FA.Chrome, 0.8f); // 도는 바퀴살
                break;
            }
            case "exterior.wreckwatch":
            {
                if (_hullSegs.Count == 0) break;
                var (l, r, _, _) = HullBox();
                var o = HullAt((l + r) * 0.5f - (r - l) * 0.1f, -1) + new Vector2(0f, -13f);
                float a = -Mathf.Pi * 0.5f + Mathf.Sin(t * 0.7f) * 1.1f;
                ci.Polygon(new[] { o, o + Vector2.FromAngle(a - 0.12f) * 40f, o + Vector2.FromAngle(a + 0.12f) * 40f }, new[] { FA.Good.WithAlpha(0.18f), FA.Good.WithAlpha(0f), FA.Good.WithAlpha(0f) }); // 훑는 부채꼴
                break;
            }
        }
    }
}
