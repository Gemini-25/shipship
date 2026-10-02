using Godot;
using ShipSim.Core;
using FA = ShipSim.View.FixtureArt;

namespace ShipSim.View;

/// <summary>
/// v16.5b 기술 모습 — 설비에 붙는 것 (Fix: 그 종류 설비 모두) · 모든 설비에 붙는 것 (Machines).
/// 설비 몸체(FixtureArt) 위에 덧단 장치로 그린다: 점검표 판 · 공유 노트 · 협동 로봇 팔 · 누전 차단기 · 부하 화면 · 영점 구슬 · 무아크 차단기 ·
/// 아민 통 · 나선 꼬리표 · 양액 홈관 · 분무 노즐 · 흙 · 옛 씨앗 병 · 증류 드럼 · 응축 코일 · 핵융합 고리 · 극저온 통 · 분광 렌즈 · 옛 신호기 ·
/// 신경망 마디 · 감시 타이머 · 육각 셀 · 선실 덮개 · 나노 주사 · 진단 호 · 초전도 코일 · 진단 창 · D자 코일 · 압력솥 · 아라미드 겹 · 젤 패치 ·
/// 청음 감지기 · 나노 점 · 제어 마디 · 바닥 고정 레일.
/// </summary>
public partial class ShipView
{
    private static readonly Color Cu = new("#b87333");
    private static readonly Color Frost = new("#e8f6ff");

    private static Vector2 Q(Rect2 b, float u, float v) => b.Position + new Vector2(u * b.Size.X, v * b.Size.Y);

    // ═══════════════════════════════ 설비에 붙는 것 (정적) ═══════════════════════════════

    private void VisFixStatic(CanvasItem ci, in VisPlace p)
    {
        var r = p.Bound;
        var b = r.Grow(-3f);
        var c = b.GetCenter();
        var col = p.Col;
        float m = Mathf.Min(b.Size.X, b.Size.Y);
        switch (p.Key)
        {
            case "console.checklist":
            {
                // 점검표 판: 갈색 판 · 흰 종이 · 쇠 집게 · 체크 세 줄
                var board = new Rect2(Q(b, 1f, 0.08f) - new Vector2(9f, 0f), new Vector2(8f, 11f));
                FA.Box(ci, board, new Color("#6a4a2a"), 1f);
                ci.DrawRect(board.Grow(-1f), new Color("#ece6d6"));
                ci.DrawRect(new Rect2(board.Position.X + 2.5f, board.Position.Y - 1.2f, 3f, 2.2f), FA.Chrome);
                for (int i = 0; i < 3; i++)
                {
                    float y = board.Position.Y + 3.4f + i * 2.5f;
                    ci.DrawPolyline(new[] { new Vector2(board.Position.X + 1.4f, y), new Vector2(board.Position.X + 2.4f, y + 0.9f), new Vector2(board.Position.X + 3.6f, y - 0.9f) }, col.Darkened(0.25f), 0.8f, true);
                    ci.DrawLine(new Vector2(board.Position.X + 4.3f, y), new Vector2(board.End.X - 1.2f, y), new Color(0, 0, 0, 0.4f), 0.6f);
                }
                break;
            }
            case "console.predict":
            {
                // 예측 화면: 지난 곡선(실선) · 앞날(점선) · 지금 선
                var scr = new Rect2(Q(b, 0.12f, 0.04f), new Vector2(b.Size.X * 0.76f, Mathf.Max(7f, b.Size.Y * 0.24f)));
                FA.Box(ci, scr, new Color("#071018"), 1.5f, col.WithAlpha(0.5f));
                Vector2 Curve(float t) => new(scr.Position.X + 1.5f + t * (scr.Size.X - 3f), scr.GetCenter().Y + Mathf.Sin(t * 5.2f + 0.6f) * scr.Size.Y * 0.28f + t * scr.Size.Y * 0.12f);
                for (int i = 0; i < 10; i++)
                {
                    float t0 = i / 10f, t1 = (i + 1) / 10f;
                    if (t0 < 0.6f) ci.DrawLine(Curve(t0), Curve(t1), col, 1f, true);
                    else if (i % 2 == 0) ci.DrawLine(Curve(t0), Curve(t1), col.WithAlpha(0.6f), 1f, true);
                }
                ci.DrawLine(new Vector2(Curve(0.6f).X, scr.Position.Y + 1f), new Vector2(Curve(0.6f).X, scr.End.Y - 1f), new Color(1, 1, 1, 0.3f), 0.7f);
                break;
            }
            case "workshop.notes":
            {
                // 펼친 공유 노트: 두 쪽 · 등 · 줄 · 연필
                var pg = Q(b, 0.16f, 0.3f);
                var left = new Rect2(pg, new Vector2(7f, 9f));
                var right = new Rect2(pg + new Vector2(7.4f, 0f), new Vector2(7f, 9f));
                ci.DrawRect(left.Grow(0.6f), new Color(0, 0, 0, 0.3f));
                ci.DrawRect(left, new Color("#efe8d4"));
                ci.DrawRect(right, new Color("#f4eedc"));
                ci.DrawLine(pg + new Vector2(7.2f, 0f), pg + new Vector2(7.2f, 9f), new Color("#8a7a5a"), 0.8f);
                for (int i = 0; i < 4; i++)
                {
                    ci.DrawLine(pg + new Vector2(1f, 2f + i * 2f), pg + new Vector2(6f - (i % 2), 2f + i * 2f), col.Darkened(0.4f).WithAlpha(0.6f), 0.5f);
                    ci.DrawLine(pg + new Vector2(8.4f, 2f + i * 2f), pg + new Vector2(13f - (i % 3), 2f + i * 2f), new Color(0.2f, 0.2f, 0.3f, 0.5f), 0.5f);
                }
                ci.DrawLine(pg + new Vector2(13f, 10.5f), pg + new Vector2(18f, 6f), new Color("#e0b64a"), 1.3f, true);
                ci.DrawLine(pg + new Vector2(12.4f, 11f), pg + new Vector2(13f, 10.5f), new Color("#2a2420"), 1.3f, true);
                break;
            }
            case "workshop.cobot":
            {
                // 협동 로봇 팔의 받침: 볼트 넷 · 분야 색 고리 · 어깨 관절
                var bs = Q(b, 0.86f, 0.24f);
                ci.DrawCircle(bs, 5f, new Color("#20252e"), true, -1f, true);
                ci.DrawArc(bs, 5f, 0f, Mathf.Tau, 18, col, 1.2f, true);
                for (int i = 0; i < 4; i++) FA.Bolt(ci, bs + Vector2.FromAngle(i * Mathf.Pi / 2f + 0.78f) * 3.6f, 0.55f);
                ci.DrawCircle(bs, 2.2f, new Color("#d8dee8"), true, -1f, true);
                break;
            }
            case "panel.rcd":
            {
                // 누전 차단기 셋: 회색 모듈 · 노란 시험 단추 · 손잡이
                for (int i = 0; i < 3; i++)
                {
                    var mod = new Rect2(Q(b, 0.14f + i * 0.25f, 0.68f), new Vector2(b.Size.X * 0.2f, b.Size.Y * 0.26f));
                    FA.Box(ci, mod, new Color("#c8ccd2"), 1f, new Color("#7a808a"));
                    ci.DrawCircle(mod.Position + new Vector2(mod.Size.X * 0.5f, mod.Size.Y * 0.28f), 1.2f, new Color("#f2d230"), true, -1f, true);
                    ci.DrawRect(new Rect2(mod.Position.X + mod.Size.X * 0.35f, mod.Position.Y + mod.Size.Y * 0.55f, mod.Size.X * 0.3f, mod.Size.Y * 0.3f), new Color("#30343a"));
                }
                break;
            }
            case "panel.smartgrid":
            {
                // 부하 곡선 화면 + 망 표시
                var scr = new Rect2(Q(b, 0.55f, 0.08f), new Vector2(b.Size.X * 0.38f, b.Size.Y * 0.3f));
                FA.Box(ci, scr, new Color("#06140e"), 1f, col.WithAlpha(0.6f));
                FA.Grille(ci, scr.Grow(-1f), 3f, new Color(1, 1, 1, 0.05f), 0.5f);
                var prev = scr.Position + new Vector2(1f, scr.Size.Y * 0.7f);
                for (int i = 1; i <= 6; i++)
                {
                    var q = scr.Position + new Vector2(1f + i * (scr.Size.X - 2f) / 6f, scr.Size.Y * (0.25f + 0.5f * FA.Hash(i, 3, 430)));
                    ci.DrawLine(prev, q, col, 0.9f, true);
                    prev = q;
                }
                break;
            }
            case "panel.zeropoint":
            {
                // 영점 구슬 받침: 두 집게 호 (구슬은 떠서 빛난다 — 움직임 층)
                var o = Q(b, 0.5f, 0.32f);
                ci.DrawArc(o, 5.5f, Mathf.Pi * 0.15f, Mathf.Pi * 0.85f, 10, FA.Chrome, 1.4f, true);
                ci.DrawArc(o, 5.5f, Mathf.Pi * 1.15f, Mathf.Pi * 1.85f, 10, FA.Chrome, 1.4f, true);
                ci.DrawCircle(o, 6.5f, new Color(0, 0, 0, 0.25f), false, 0.8f, true);
                break;
            }
            case "panel.safegrid":
            {
                // 무아크 배전: 초록 고체 차단기 넷 + 방열 핀 블록
                for (int i = 0; i < 4; i++)
                {
                    var br = new Rect2(Q(b, 0.04f, 0.1f + i * 0.2f), new Vector2(b.Size.X * 0.14f, b.Size.Y * 0.15f));
                    ci.DrawRect(br, new Color("#14241a"));
                    ci.DrawRect(new Rect2(br.Position.X, br.Position.Y, 1.6f, br.Size.Y), col.Lerp(new Color("#6ee07a"), 0.6f));
                }
                var blk = new Rect2(Q(b, 0.04f, 0.9f), new Vector2(b.Size.X * 0.2f, 3f));
                ci.DrawRect(blk, new Color("#0c0e10"));
                FA.Vents(ci, blk, 4, true, new Color(1, 1, 1, 0.25f), 0.6f);
                break;
            }
            case "lifesupport.amine":
            {
                // 아민 흡착 통 둘: 둥근 통 · 거품 눈금 · 차오른 띠
                for (int i = 0; i < 2; i++)
                {
                    var o = Q(b, 0.07f, 0.28f + i * 0.44f);
                    FA.Can(ci, o, 3.8f, new Color("#2a3a34"), col);
                    ci.DrawArc(o, 2.6f, Mathf.Pi * 0.1f, Mathf.Pi * 0.9f, 8, col.WithAlpha(0.7f), 1.4f, true);
                    ci.DrawCircle(o + new Vector2(-1f, -1f), 0.7f, Colors.White.WithAlpha(0.7f), true, -1f, true);
                    ci.DrawCircle(o + new Vector2(1f, -0.4f), 0.5f, Colors.White.WithAlpha(0.6f), true, -1f, true);
                }
                break;
            }
            case "growbed.genecrops":
            {
                // 나선 꼬리표 말뚝 셋 + 밝은 잎 점
                for (int i = 0; i < 3; i++)
                {
                    var top = Q(b, 0.2f + i * 0.3f, 0.06f);
                    ci.DrawLine(top, top + new Vector2(0f, 6f), new Color("#c8b890"), 1f);
                    var tag = new Rect2(top + new Vector2(-2.5f, -3f), new Vector2(5f, 4f));
                    ci.DrawRect(tag, Colors.White.WithAlpha(0.85f));
                    for (int k = 0; k < 4; k++)
                    {
                        float x = tag.Position.X + 0.5f + k;
                        ci.DrawLine(new Vector2(x, tag.Position.Y + 2f + Mathf.Sin(k * 1.6f) * 1.4f), new Vector2(x + 1f, tag.Position.Y + 2f + Mathf.Sin((k + 1) * 1.6f) * 1.4f), col.Darkened(0.3f), 0.6f, true);
                        ci.DrawLine(new Vector2(x, tag.Position.Y + 2f - Mathf.Sin(k * 1.6f) * 1.4f), new Vector2(x + 1f, tag.Position.Y + 2f - Mathf.Sin((k + 1) * 1.6f) * 1.4f), new Color("#c060c0"), 0.6f, true);
                    }
                }
                for (int i = 0; i < 6; i++) FA.Dot(ci, Q(b, 0.12f + 0.15f * i, 0.45f + 0.25f * FA.Hash(p.K, i, 431)), 1.4f, FA.LeafLight.Lightened(0.25f).WithAlpha(0.8f));
                break;
            }
            case "growbed.nft":
            {
                // 양액 홈관 둘: 흰 관 · 끝 마개 · 구멍
                foreach (float v in new[] { 0.1f, 0.9f })
                {
                    var a0 = Q(b, 0.02f, v);
                    var a1 = Q(b, 0.98f, v);
                    FA.Pipe(ci, a0, a1, 2.4f, new Color("#e8ecef"));
                    for (int i = 1; i < 6; i++) FA.Dot(ci, a0.Lerp(a1, i / 6f), 0.7f, new Color("#2a3a30"));
                }
                break;
            }
            case "growbed.mist":
            {
                // 분무 노즐 머리 (양 끝): T자 관 · 고운 구멍
                foreach (float u in new[] { 0.03f, 0.97f })
                {
                    var o = Q(b, u, 0.5f);
                    ci.DrawLine(o + new Vector2(0f, -5f), o + new Vector2(0f, 5f), FA.Chrome, 2f, true);
                    ci.DrawCircle(o, 2.4f, new Color("#9fb4cc"), true, -1f, true);
                    for (int k = -1; k <= 1; k++) FA.Dot(ci, o + new Vector2(0f, k * 1.2f), 0.4f, new Color("#1a2330"));
                }
                break;
            }
            case "growbed.soil":
            {
                // 검은 흙 · 덩이 · 퇴비통
                var bed = new Rect2(Q(b, 0.08f, 0.22f), new Vector2(b.Size.X * 0.84f, b.Size.Y * 0.56f));
                FA.Box(ci, bed, new Color("#2a1d12").WithAlpha(0.85f), 2f);
                for (int i = 0; i < 14; i++)
                    FA.Dot(ci, bed.Position + new Vector2(FA.Hash(p.K, i, 432), FA.Hash(p.K, i, 433)) * bed.Size, 0.8f + 1.1f * FA.Hash(p.K, i, 434), new Color("#4a3420").WithAlpha(0.9f));
                var bin = new Rect2(Q(b, 0.9f, 0.82f), new Vector2(5f, 5f));
                FA.Box(ci, bin, new Color("#3a4a2a"), 1f, new Color("#7a8a4a"));
                ci.DrawLine(bin.Position + new Vector2(0.5f, 1.5f), bin.Position + new Vector2(4.5f, 1.5f), new Color("#a0b060"), 0.8f);
                break;
            }
            case "growbed.heirloom":
            {
                // 옛 씨앗 병 셋 + 빨강 · 보라 열매
                var seeds = new[] { new Color("#c0392b"), new Color("#7a3fa0"), new Color("#e0b64a") };
                for (int i = 0; i < 3; i++)
                {
                    var j = new Rect2(Q(b, 0.12f + i * 0.12f, 0.86f) - new Vector2(2f, 3f), new Vector2(4f, 5f));
                    FA.Box(ci, j, new Color(0.7f, 0.85f, 0.9f, 0.35f), 1f, new Color(1, 1, 1, 0.5f));
                    ci.DrawRect(new Rect2(j.Position.X, j.Position.Y - 1f, 4f, 1.2f), new Color("#8a6a3a"));
                    for (int k = 0; k < 3; k++) FA.Dot(ci, j.Position + new Vector2(1f + k, 3.5f - (k % 2)), 0.5f, seeds[i]);
                }
                for (int i = 0; i < 5; i++) FA.Dot(ci, Q(b, 0.5f + 0.09f * i, 0.35f + 0.3f * FA.Hash(p.K, i, 435)), 1.6f, i % 2 == 0 ? new Color("#d0402e") : new Color("#8a4ab0"));
                break;
            }
            case "waterplant.vcd":
            {
                // 증기 압축 증류 드럼: 둥근 몸 · 압축 고리 · 가운데 축
                var o = Q(b, 0.16f, 0.24f);
                float rad = Mathf.Clamp(m * 0.18f, 4f, 7f);
                ci.DrawCircle(o + new Vector2(1f, 1.4f), rad, new Color(0, 0, 0, 0.3f), true, -1f, true);
                ci.DrawCircle(o, rad, new Color("#1a2a36"), true, -1f, true);
                ci.DrawArc(o, rad, 0f, Mathf.Tau, 20, col, 1.2f, true);
                ci.DrawArc(o, rad * 0.6f, 0f, Mathf.Tau, 16, new Color("#3a6a88"), 1f, true);
                ci.DrawCircle(o, 1.2f, FA.Chrome, true, -1f, true);
                break;
            }
            case "waterplant.heat":
            {
                // 폐열 증류: 구리 응축 코일 (지그재그) · 받이 컵
                var top = Q(b, 0.92f, 0.12f);
                var pts = new Vector2[9];
                for (int i = 0; i < 9; i++) pts[i] = top + new Vector2((i % 2 == 0 ? -2.5f : 2.5f), i * b.Size.Y * 0.07f);
                ci.DrawPolyline(pts, Cu, 1.4f, true);
                ci.DrawPolyline(pts, Cu.Lightened(0.35f).WithAlpha(0.5f), 0.5f, true);
                var cup = new Rect2(pts[^1] + new Vector2(-3f, 1.5f), new Vector2(6f, 4f));
                FA.Box(ci, cup, new Color("#9ad0ff").WithAlpha(0.35f), 1f, FA.Chrome);
                break;
            }
            case "engine.fusion":
            {
                // 자홍빛 핵융합 고리: 겹 타원 · 자석 덩이 여섯
                ci.DrawSetTransform(c, 0f, new Vector2(1f, 0.55f));
                ci.DrawArc(Vector2.Zero, m * 0.42f, 0f, Mathf.Tau, 36, new Color("#3a1a40"), 4f, true);
                ci.DrawArc(Vector2.Zero, m * 0.42f, 0f, Mathf.Tau, 36, col.Lerp(new Color("#ff5ad8"), 0.6f).WithAlpha(0.6f), 1.5f, true);
                ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                for (int i = 0; i < 6; i++)
                {
                    var d = Vector2.FromAngle(i * Mathf.Tau / 6f);
                    ci.DrawRect(new Rect2(c + new Vector2(d.X * m * 0.42f, d.Y * m * 0.23f) - new Vector2(1.5f, 1.5f), new Vector2(3f, 3f)), new Color("#5a4a6a"));
                }
                break;
            }
            case "sensor.quantum":
            {
                // 극저온 통 (서리 고리) + 얽힌 점 쌍
                var o = Q(b, 0.14f, 0.14f);
                FA.Can(ci, o, 4.2f, new Color("#1a2a3a"), Frost);
                ci.DrawArc(o, 5.6f, 0f, Mathf.Tau, 20, Frost.WithAlpha(0.35f), 1.6f, true);
                var a1 = Q(b, 0.82f, 0.8f);
                var a2 = a1 + new Vector2(5f, -3f);
                ci.DrawLine(a1, a2, col.WithAlpha(0.4f), 0.6f, true);
                FA.Dot(ci, a1, 1.2f, col.Darkened(0.3f));
                FA.Dot(ci, a2, 1.2f, col.Darkened(0.3f));
                break;
            }
            case "sensor.multispec":
            {
                // 무지개 렌즈 넷
                var cols = new[] { new Color("#ff5a5a"), new Color("#6ee07a"), new Color("#5a8aff"), new Color("#b07aff") };
                for (int i = 0; i < 4; i++)
                {
                    var o = Q(b, 0.68f + (i % 2) * 0.16f, 0.72f + (i / 2) * 0.16f);
                    ci.DrawCircle(o, 2.4f, new Color("#0b1119"), true, -1f, true);
                    ci.DrawCircle(o, 1.6f, cols[i].WithAlpha(0.55f), true, -1f, true);
                    ci.DrawArc(o, 2.4f, 0f, Mathf.Tau, 12, FA.Chrome, 0.7f, true);
                }
                break;
            }
            case "sensor.beacon":
            {
                // 옛 신호기 부속: 주황 상자 · 철망 · 붉은 돔
                var bx = new Rect2(Q(b, 0.06f, 0.72f), new Vector2(8f, 6f));
                FA.Box(ci, bx, new Color("#a8541e"), 1f, new Color("#e08a3a"));
                FA.Grille(ci, new Rect2(bx.Position + new Vector2(1f, 1f), new Vector2(4f, 4f)), 1.4f, new Color(0, 0, 0, 0.5f), 0.5f);
                ci.DrawCircle(bx.Position + new Vector2(6.5f, 1.5f), 1.8f, new Color("#7a1a1a"), true, -1f, true);
                break;
            }
            case "server.neural":
            {
                // 신경망 마디 여섯과 잇는 선 (꺼진 빛)
                var nodes = NeuralNodes(b);
                for (int i = 0; i < nodes.Length; i++)
                    for (int j = i + 1; j < nodes.Length; j++)
                        if ((i + j) % 2 == 1) ci.DrawLine(nodes[i], nodes[j], col.WithAlpha(0.22f), 0.6f, true);
                foreach (var n in nodes) { FA.Dot(ci, n, 1.6f, new Color("#1a1430")); ci.DrawArc(n, 1.6f, 0f, Mathf.Tau, 10, col.WithAlpha(0.7f), 0.6f, true); }
                break;
            }
            case "server.watchdog":
            {
                // 감시 타이머: 검은 숫자판 · 붉은 테
                var box = new Rect2(Q(b, 0.66f, 0.8f), new Vector2(11f, 5f));
                FA.Box(ci, box, new Color("#100808"), 1f, new Color("#a02a2a"));
                ci.DrawLine(box.Position + new Vector2(5.5f, 1.4f), box.Position + new Vector2(5.5f, 1.8f), new Color("#ff5a4a"), 0.8f);
                ci.DrawLine(box.Position + new Vector2(5.5f, 3.2f), box.Position + new Vector2(5.5f, 3.6f), new Color("#ff5a4a"), 0.8f);
                break;
            }
            case "battery.solid":
            {
                // 육각 고체 셀 무늬 (3×2)
                float hr = Mathf.Clamp(m * 0.12f, 2.6f, 4.5f);
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j < 2; j++)
                    {
                        var o = c + new Vector2((i - 1) * hr * 1.8f, (j - 0.5f) * hr * 1.6f + (i % 2) * hr * 0.4f);
                        var hex = new Vector2[7];
                        for (int k = 0; k < 7; k++) hex[k] = o + Vector2.FromAngle(k * Mathf.Tau / 6f) * hr;
                        ci.DrawColoredPolygon(hex[..6], new Color("#0e1a1c").WithAlpha(0.8f));
                        ci.DrawPolyline(hex, col.WithAlpha(0.75f), 0.8f, true);
                    }
                break;
            }
            case "quarters.pods":
            {
                // 개인 선실 덮개: 머리 쪽 반을 덮는 둥근 껍데기 · 문 이음 · 작은 등
                bool wide = b.Size.X >= b.Size.Y;
                var shell = wide ? new Rect2(b.Position, new Vector2(b.Size.X * 0.46f, b.Size.Y)) : new Rect2(b.Position, new Vector2(b.Size.X, b.Size.Y * 0.46f));
                FA.Box(ci, shell.Grow(1f), new Color(0.75f, 0.82f, 0.9f, 0.28f), 8f, new Color(0.9f, 0.95f, 1f, 0.55f), 1);
                var seam0 = wide ? new Vector2(shell.End.X - 3f, shell.Position.Y + 2f) : new Vector2(shell.Position.X + 2f, shell.End.Y - 3f);
                var seam1 = wide ? new Vector2(shell.End.X - 3f, shell.End.Y - 2f) : new Vector2(shell.End.X - 2f, shell.End.Y - 3f);
                ci.DrawLine(seam0, seam1, new Color(1, 1, 1, 0.4f), 0.8f);
                FA.Led(ci, shell.Position + new Vector2(3f, 3f), col, 0.8f, 1f);
                break;
            }
            case "medbay.nano":
            {
                // 나노 주사 거치대: 기둥 · 은빛 병 · 관
                var top = Q(b, 0.96f, 0.08f);
                ci.DrawLine(top, top + new Vector2(0f, 9f), FA.Chrome, 1f);
                FA.Box(ci, new Rect2(top + new Vector2(-1.8f, 0.5f), new Vector2(3.6f, 5f)), new Color("#c8d0dc"), 1.5f, col);
                FA.Cable(ci, top + new Vector2(0f, 5.5f), Q(b, 0.7f, 0.3f), 1.2f, new Color(0.85f, 0.9f, 1f, 0.5f), 0.6f);
                break;
            }
            case "medbay.diagai":
            {
                // 진단 AI 스캔 호: 침대를 가로지르는 반원 틀 + 눈
                bool wide = b.Size.X >= b.Size.Y;
                var a0 = wide ? Q(b, 0.5f, -0.05f) : Q(b, -0.05f, 0.5f);
                var a1 = wide ? Q(b, 0.5f, 1.05f) : Q(b, 1.05f, 0.5f);
                ci.DrawLine(a0, a1, new Color("#d8dee8").WithAlpha(0.6f), 2.6f, true);
                ci.DrawLine(a0, a1, col.WithAlpha(0.5f), 0.8f, true);
                ci.DrawCircle(a0, 2.4f, new Color("#101418"), true, -1f, true);
                ci.DrawCircle(a0, 1.2f, col, true, -1f, true);
                break;
            }
            case "reactor.coils":
            {
                // 노심을 감은 초전도 코일 (구리 감기 열여섯)
                float rad = m * 0.4f;
                for (int i = 0; i < 16; i++)
                {
                    var d = Vector2.FromAngle(i * Mathf.Tau / 16f);
                    ci.DrawLine(c + d * (rad - 2.5f), c + d * (rad + 2.5f), Cu, 2.2f, true);
                    ci.DrawLine(c + d * (rad - 2.5f), c + d * (rad + 2.5f), Cu.Lightened(0.4f).WithAlpha(0.4f), 0.6f, true);
                }
                ci.DrawArc(c, rad + 3.5f, 0f, Mathf.Tau, 40, Frost.WithAlpha(0.25f), 0.8f, true);
                break;
            }
            case "reactor.diag":
            {
                // 진단 창 셋: 각진 틀 · 렌즈
                for (int i = 0; i < 3; i++)
                {
                    var d = Vector2.FromAngle(-Mathf.Pi / 2f + i * Mathf.Tau / 3f);
                    var o = c + d * m * 0.46f;
                    ci.DrawRect(new Rect2(o - new Vector2(2.5f, 2.5f), new Vector2(5f, 5f)), new Color("#2a3040"));
                    ci.DrawCircle(o, 1.5f, col.WithAlpha(0.7f), true, -1f, true);
                }
                break;
            }
            case "reactor.tokamak":
            {
                // D자 코일 넷 (밖은 둥글고 안은 곧다)
                for (int i = 0; i < 4; i++)
                {
                    float ang = i * Mathf.Pi / 2f + Mathf.Pi / 4f;
                    var d = Vector2.FromAngle(ang);
                    var n = new Vector2(-d.Y, d.X);
                    var o = c + d * m * 0.3f;
                    var pts = new Vector2[9];
                    for (int k = 0; k < 9; k++)
                    {
                        float t = -Mathf.Pi / 2f + k * Mathf.Pi / 8f;
                        pts[k] = o + d * Mathf.Cos(t) * m * 0.12f + n * Mathf.Sin(t) * m * 0.1f;
                    }
                    ci.DrawPolyline(pts, new Color("#6a7aa0"), 1.6f, true);
                    ci.DrawLine(pts[0], pts[^1], new Color("#6a7aa0"), 1.6f, true);
                }
                break;
            }
            case "galley.pressure":
            {
                // 압력솥: 둥근 솥 · 잠금 손잡이 · 추
                var o = Q(b, 0.28f, 0.5f);
                FA.Can(ci, o, 6f, new Color("#7a828e"), new Color("#c8d0dc"));
                ci.DrawLine(o + new Vector2(-8.5f, 0f), o + new Vector2(-5.5f, 0f), new Color("#1a1a1a"), 2f, true);
                ci.DrawLine(o + new Vector2(5.5f, 0f), o + new Vector2(8.5f, 0f), new Color("#1a1a1a"), 2f, true);
                ci.DrawLine(o + new Vector2(-4f, 0f), o + new Vector2(4f, 0f), new Color(0, 0, 0, 0.4f), 0.8f);
                ci.DrawCircle(o, 1.6f, new Color("#2a2a2a"), true, -1f, true);
                break;
            }
            case "suit.aramid":
            {
                // 우주복 몸통에 노란 아라미드 겹 (빗살 짜임)
                var torso = new Rect2(c - new Vector2(5f, 3f), new Vector2(10f, 11f));
                ci.DrawRect(torso, new Color("#d8b030").WithAlpha(0.35f));
                for (float x = torso.Position.X; x < torso.End.X; x += 2f)
                    ci.DrawLine(new Vector2(x, torso.Position.Y), new Vector2(x + 2f, torso.End.Y), new Color("#f0d060").WithAlpha(0.55f), 0.5f);
                for (float x = torso.Position.X; x < torso.End.X; x += 2f)
                    ci.DrawLine(new Vector2(x + 2f, torso.Position.Y), new Vector2(x, torso.End.Y), new Color("#a08020").WithAlpha(0.45f), 0.5f);
                break;
            }
            case "suit.selfpatch":
            {
                // 푸른 젤 패치 셋 (둥근 덩이 · 광택)
                for (int i = 0; i < 3; i++)
                {
                    var o = c + new Vector2((FA.Hash(p.K, i, 436) - 0.5f) * 10f, (FA.Hash(p.K, i, 437) - 0.3f) * 14f);
                    ci.DrawCircle(o, 2f, new Color("#3a8fd9").WithAlpha(0.7f), true, -1f, true);
                    ci.DrawCircle(o + new Vector2(-0.6f, -0.6f), 0.7f, Colors.White.WithAlpha(0.6f), true, -1f, true);
                }
                break;
            }
        }
    }

    private static Vector2[] NeuralNodes(Rect2 b) => new[]
    {
        Q(b, 0.2f, 0.25f), Q(b, 0.2f, 0.75f), Q(b, 0.5f, 0.15f), Q(b, 0.5f, 0.5f), Q(b, 0.5f, 0.85f), Q(b, 0.8f, 0.5f),
    };

    // ═══════════════════════════════ 설비에 붙는 것 (움직임) ═══════════════════════════════

    private void VisFixLive(CanvasItem ci, in VisPlace p)
    {
        var b = p.Bound.Grow(-3f);
        var c = b.GetCenter();
        var col = p.Col;
        float m = Mathf.Min(b.Size.X, b.Size.Y);
        bool on = Running(p.F);
        float t = _time + p.K * 0.61f;
        switch (p.Key)
        {
            case "console.predict":
            {
                if (!on) break;
                var scr = new Rect2(Q(b, 0.12f, 0.04f), new Vector2(b.Size.X * 0.76f, Mathf.Max(7f, b.Size.Y * 0.24f)));
                float ph = Mathf.PosMod(t * 0.2f, 1f);
                float x = scr.Position.X + 1.5f + (0.6f + 0.4f * ph) * (scr.Size.X - 3f);
                ci.DrawCircle(new Vector2(x, scr.GetCenter().Y + Mathf.Sin((0.6f + 0.4f * ph) * 5.2f + 0.6f) * scr.Size.Y * 0.28f + (0.6f + 0.4f * ph) * scr.Size.Y * 0.12f), 1.2f, col.Lightened(0.4f), true, -1f, true);
                break;
            }
            case "workshop.cobot":
            {
                // 두 마디 팔이 집었다 놓는다 (설비가 서면 접힌다)
                var bs = Q(b, 0.86f, 0.24f);
                float a1 = on ? Mathf.Pi * 0.75f + Mathf.Sin(t * 0.9f) * 0.6f : Mathf.Pi * 0.6f;
                float a2 = on ? a1 + 0.9f + Mathf.Sin(t * 1.3f + 1f) * 0.7f : a1 + 2.2f;
                var j1 = bs + Vector2.FromAngle(a1) * m * 0.32f;
                var j2 = j1 + Vector2.FromAngle(a2) * m * 0.26f;
                ci.DrawLine(bs, j1, new Color("#e8ecef"), 3f, true);
                ci.DrawLine(bs, j1, col.WithAlpha(0.5f), 1f, true);
                ci.DrawLine(j1, j2, new Color("#d8dee8"), 2.4f, true);
                ci.DrawCircle(j1, 1.8f, new Color("#30343a"), true, -1f, true);
                float g = on ? 1.5f + Mathf.Abs(Mathf.Sin(t * 1.8f)) * 1.5f : 2.5f;
                var n = Vector2.FromAngle(a2 + Mathf.Pi / 2f);
                ci.DrawLine(j2 + n * g, j2 + n * g + Vector2.FromAngle(a2) * 2.5f, new Color("#9aa3b5"), 1f, true);
                ci.DrawLine(j2 - n * g, j2 - n * g + Vector2.FromAngle(a2) * 2.5f, new Color("#9aa3b5"), 1f, true);
                break;
            }
            case "panel.smartgrid":
            {
                if (!on) break;
                var scr = new Rect2(Q(b, 0.55f, 0.08f), new Vector2(b.Size.X * 0.38f, b.Size.Y * 0.3f));
                float ph = Mathf.PosMod(t * 0.5f, 1f);
                ci.DrawLine(new Vector2(scr.Position.X + ph * scr.Size.X, scr.Position.Y + 1f), new Vector2(scr.Position.X + ph * scr.Size.X, scr.End.Y - 1f), col.WithAlpha(0.6f), 0.6f);
                for (int i = 0; i < 3; i++) // 망으로 나가는 데이터 점
                {
                    float q = Mathf.PosMod(t * 0.8f + i / 3f, 1f);
                    FA.Dot(ci, Q(b, 0.74f, 0.4f).Lerp(Q(b, 1.1f, 0.4f + i * 0.2f), q), 0.8f, col.WithAlpha(1f - q));
                }
                break;
            }
            case "panel.zeropoint":
            {
                var o = Q(b, 0.5f, 0.32f) + new Vector2(0f, on ? Mathf.Sin(t * 1.4f) * 1.2f : 1.5f);
                ci.DrawCircle(o, 3.2f, (on ? col.Lightened(0.5f) : new Color("#3a3a40")).WithAlpha(0.95f), true, -1f, true);
                if (on) ci.DrawArc(o, 4.5f, t * 2f, t * 2f + 2.2f, 10, Colors.White.WithAlpha(0.6f), 0.7f, true);
                break;
            }
            case "panel.safegrid":
            {
                for (int i = 0; i < 4; i++)
                {
                    var br = new Rect2(Q(b, 0.04f, 0.1f + i * 0.2f), new Vector2(b.Size.X * 0.14f, b.Size.Y * 0.15f));
                    bool blink = on && FA.Hash(p.K, i + (int)(t * 0.5f) * 5, 438) > 0.92f;
                    FA.Led(ci, br.Position + new Vector2(br.Size.X - 1.5f, br.Size.Y * 0.5f), new Color("#6ee07a"), on ? (blink ? 0.3f : 0.9f) : 0f, 0.8f);
                }
                break;
            }
            case "growbed.nft":
            {
                if (!on) break;
                foreach (float v in new[] { 0.1f, 0.9f })
                    for (int i = 0; i < 4; i++)
                    {
                        float q = Mathf.PosMod(t * 0.3f + i / 4f + v, 1f);
                        FA.Dot(ci, Q(b, 0.04f + 0.92f * q, v), 0.9f, new Color("#9ae0a0").WithAlpha(0.85f));
                    }
                break;
            }
            case "growbed.mist":
            {
                if (!on) break;
                foreach (float u in new[] { 0.03f, 0.97f })
                    for (int k = 0; k < 4; k++)
                    {
                        float q = Mathf.PosMod(t * 0.7f + k / 4f, 1f);
                        var dir = new Vector2(u < 0.5f ? 1f : -1f, (k - 1.5f) * 0.35f);
                        FA.Dot(ci, Q(b, u, 0.5f) + dir * q * b.Size.X * 0.25f, 1.2f + 3f * q, new Color(0.88f, 0.96f, 1f, 0.25f * (1f - q)));
                    }
                break;
            }
            case "waterplant.vcd":
            {
                var o = Q(b, 0.16f, 0.24f);
                float rad = Mathf.Clamp(m * 0.18f, 4f, 7f) * 0.8f;
                float a = on ? t * 4f : 0.4f;
                for (int k = 0; k < 3; k++) ci.DrawArc(o, rad, a + k * Mathf.Tau / 3f, a + k * Mathf.Tau / 3f + 0.9f, 6, col.Lightened(0.3f).WithAlpha(on ? 0.9f : 0.3f), 1.4f, true);
                break;
            }
            case "waterplant.heat":
            {
                var top = Q(b, 0.92f, 0.12f);
                if (on)
                    for (int k = 0; k < 3; k++)
                    {
                        float q = Mathf.PosMod(t * 0.6f + k / 3f, 1f);
                        FA.Dot(ci, top + new Vector2(Mathf.Sin(t + k) * 2f, -2f - q * 12f), 1.5f + 2.5f * q, new Color(1, 1, 1, 0.22f * (1f - q)));
                    }
                float dq = Mathf.PosMod(t * 1.1f, 1f);
                FA.Dot(ci, top + new Vector2(0f, 8f * b.Size.Y * 0.07f + 6f + dq * 4f), 0.8f, FA.WaterLight.WithAlpha(on ? 0.9f * (1f - dq) : 0f));
                break;
            }
            case "engine.fusion":
            {
                bool burn = _world.Propulsion.Burning || _world.Propulsion.CourseBurnVisible;
                ci.DrawSetTransform(c, 0f, new Vector2(1f, 0.55f));
                float a = t * (burn ? 3f : 0.8f);
                for (int k = 0; k < 2; k++)
                    ci.DrawArc(Vector2.Zero, m * 0.42f, a + k * Mathf.Pi, a + k * Mathf.Pi + 1.1f, 10, new Color("#ff8ae8").WithAlpha((burn ? 0.95f : 0.45f) * (on ? 1f : 0.2f)), 2f, true);
                ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                break;
            }
            case "sensor.quantum":
            {
                // 얽힌 점 쌍: 둘이 늘 같이 깜빡인다
                bool flash = on && Mathf.PosMod(t * 1.3f, 1f) < 0.25f;
                var a1 = Q(b, 0.82f, 0.8f);
                FA.Led(ci, a1, col, flash ? 1f : 0.15f, 1.1f);
                FA.Led(ci, a1 + new Vector2(5f, -3f), col, flash ? 1f : 0.15f, 1.1f);
                if (on) FA.Dot(ci, Q(b, 0.14f, 0.14f) + new Vector2(Mathf.Sin(t) * 3f, -6f - Mathf.PosMod(t * 4f, 6f)), 1.2f, Frost.WithAlpha(0.25f));
                break;
            }
            case "sensor.multispec":
            {
                if (!on) break;
                int lit = (int)(t * 2f) % 4;
                var o = Q(b, 0.68f + (lit % 2) * 0.16f, 0.72f + (lit / 2) * 0.16f);
                ci.DrawCircle(o, 2f, Colors.White.WithAlpha(0.35f), true, -1f, true);
                break;
            }
            case "sensor.beacon":
            {
                var o = Q(b, 0.06f, 0.72f) + new Vector2(6.5f, 1.5f);
                float ph = Mathf.PosMod(t * 0.9f, 1f);
                if (on && ph < 0.12f) { FA.Dot(ci, o, 1.8f, new Color("#ff3a2a")); FA.Dot(ci, o, 5f, new Color("#ff3a2a").WithAlpha(0.25f)); }
                break;
            }
            case "server.neural":
            {
                if (!on) break;
                var nodes = NeuralNodes(b);
                for (int k = 0; k < 3; k++)
                {
                    int i = (int)(t * 1.5f + k * 2) % nodes.Length, j = (i + 1 + k) % nodes.Length;
                    float q = Mathf.PosMod(t * 1.5f + k * 2f, 1f);
                    FA.Dot(ci, nodes[i].Lerp(nodes[j], q), 1f, col.Lightened(0.4f));
                    FA.Led(ci, nodes[j], col, q > 0.85f ? 1f : 0.25f, 1.2f);
                }
                break;
            }
            case "server.watchdog":
            {
                // 숫자가 줄다가 '쓰다듬'으면 다시 99로 (멎으면 00에서 붉게 깜빡)
                var box = new Rect2(Q(b, 0.66f, 0.8f), new Vector2(11f, 5f));
                int n = on ? 99 - (int)(Mathf.PosMod(t * 9f, 60f)) : 0;
                var red = new Color("#ff5a4a").WithAlpha(on ? 0.95f : (Mathf.Sin(t * 6f) > 0f ? 0.95f : 0.2f));
                Seg(ci, box.Position + new Vector2(1.2f, 0.8f), n / 10, red);
                Seg(ci, box.Position + new Vector2(6.6f, 0.8f), n % 10, red);
                break;
            }
            case "medbay.nano":
            {
                var top = Q(b, 0.96f, 0.08f);
                float q = Mathf.PosMod(t * 0.7f, 1f);
                FA.Dot(ci, top + new Vector2(0f, 1f + 4f * q), 0.7f, Colors.White.WithAlpha(0.8f * (1f - q)));
                FA.Dot(ci, top.Lerp(Q(b, 0.7f, 0.3f), q), 0.8f, col.WithAlpha(on ? 0.8f : 0f));
                break;
            }
            case "medbay.diagai":
            {
                if (!on || p.F is not Furniture f) break;
                bool wide = b.Size.X >= b.Size.Y;
                bool occupied = _world.Crew.Exists(x => x.Room == f.Room && f.Cells.Contains(x.Cell));
                float q = 0.5f + 0.5f * Mathf.Sin(t * (occupied ? 1.6f : 0.4f));
                var a0 = wide ? Q(b, 0.05f + 0.9f * q, 0f) : Q(b, 0f, 0.05f + 0.9f * q);
                var a1 = wide ? Q(b, 0.05f + 0.9f * q, 1f) : Q(b, 1f, 0.05f + 0.9f * q);
                ci.DrawLine(a0, a1, col.WithAlpha(occupied ? 0.75f : 0.25f), 1.2f, true);
                break;
            }
            case "reactor.diag":
            {
                if (!on || Mathf.PosMod(t * 0.5f, 1f) > 0.3f) break;
                for (int i = 0; i < 3; i++)
                {
                    var d = Vector2.FromAngle(-Mathf.Pi / 2f + i * Mathf.Tau / 3f);
                    ci.DrawLine(c + d * m * 0.46f, c + d * m * 0.08f, new Color("#ff5ad8").WithAlpha(0.7f), 0.8f, true);
                }
                break;
            }
            case "galley.pressure":
            {
                var o = Q(b, 0.28f, 0.5f);
                bool cooking = on && p.F?.Machine is { Active: true };
                float wob = cooking ? Mathf.Sin(t * 14f) * 0.6f : 0f;
                ci.DrawCircle(o + new Vector2(wob, 0f), 1.6f, new Color("#4a4a4a"), true, -1f, true);
                if (cooking && Mathf.PosMod(t * 0.5f, 1f) < 0.5f)
                    for (int k = 0; k < 3; k++)
                    {
                        float q = Mathf.PosMod(t * 1.5f + k / 3f, 1f);
                        FA.Dot(ci, o + new Vector2(Mathf.Sin(t * 3f + k) * 1.5f, -2f - 12f * q), 1f + 2.5f * q, new Color(1, 1, 1, 0.35f * (1f - q)));
                    }
                break;
            }
        }
    }

    /// <summary>일곱 토막 숫자 하나 (3.6 × 3.6).</summary>
    private static void Seg(CanvasItem ci, Vector2 o, int d, Color col)
    {
        // 토막: 위 · 오른위 · 오른아래 · 아래 · 왼아래 · 왼위 · 가운데
        int[] masks = { 0b0111111, 0b0000110, 0b1011011, 0b1001111, 0b1100110, 0b1101101, 0b1111101, 0b0000111, 0b1111111, 0b1101111 };
        int mk = masks[Mathf.Clamp(d, 0, 9)];
        float wv = 3.4f, h = 1.7f;
        var segs = new (Vector2, Vector2)[]
        {
            (o, o + new Vector2(wv, 0f)), (o + new Vector2(wv, 0f), o + new Vector2(wv, h)), (o + new Vector2(wv, h), o + new Vector2(wv, 2f * h)),
            (o + new Vector2(0f, 2f * h), o + new Vector2(wv, 2f * h)), (o + new Vector2(0f, h), o + new Vector2(0f, 2f * h)), (o, o + new Vector2(0f, h)),
            (o + new Vector2(0f, h), o + new Vector2(wv, h)),
        };
        for (int i = 0; i < 7; i++) if ((mk & (1 << i)) != 0) ci.DrawLine(segs[i].Item1, segs[i].Item2, col, 0.7f);
    }

    // ═══════════════════════════════ 모든 설비에 붙는 것 ═══════════════════════════════

    private void VisMachineStatic(CanvasItem ci, in VisPlace p)
    {
        var r = p.Bound.Grow(-4f); // 설비 칸
        var col = p.Col;
        switch (p.Key)
        {
            case "machine.listener":
            {
                // 청음 감지기: 둥근 패드 · 십자 · 선 한 가닥
                var o = r.Position + new Vector2(4.5f, r.Size.Y - 4.5f);
                ci.DrawCircle(o, 2.4f, new Color("#1a1e26"), true, -1f, true);
                ci.DrawArc(o, 2.4f, 0f, Mathf.Tau, 12, col, 0.8f, true);
                ci.DrawLine(o + new Vector2(-1.2f, 0f), o + new Vector2(1.2f, 0f), col.WithAlpha(0.8f), 0.5f);
                ci.DrawLine(o + new Vector2(0f, -1.2f), o + new Vector2(0f, 1.2f), col.WithAlpha(0.8f), 0.5f);
                FA.Cable(ci, o + new Vector2(2f, 0f), new Vector2(r.Position.X + 12f, r.End.Y - 1f), 0.8f, new Color("#2a2a2a"), 0.7f);
                break;
            }
            case "machine.nano":
            {
                // 은빛 나노 막: 모서리에 번진 은 얼룩
                for (int i = 0; i < 4; i++)
                {
                    var o = r.Position + new Vector2(FA.Hash(p.K, i, 440), FA.Hash(p.K, i, 441)) * r.Size;
                    FA.Dot(ci, o, 1.4f + FA.Hash(p.K, i, 442), new Color(0.82f, 0.86f, 0.92f, 0.22f));
                }
                break;
            }
            case "panel.nodes":
            {
                // 작은 제어 마디: 검은 상자 · 초록 판 · 선
                var box = new Rect2(r.End - new Vector2(8f, 6f), new Vector2(6f, 4.5f));
                FA.Box(ci, box, new Color("#14181e"), 1f, col.WithAlpha(0.7f));
                ci.DrawRect(new Rect2(box.Position + new Vector2(1f, 1f), new Vector2(2.5f, 2.5f)), new Color("#1f5a2a"));
                ci.DrawLine(box.Position + new Vector2(3f, 4.5f), new Vector2(box.Position.X + 3f, r.End.Y + 2f), col.WithAlpha(0.6f), 0.7f);
                break;
            }
            case "floor.mounts":
            {
                // 바닥 고정 레일 둘 · 볼트 넷 (설비를 볼트 넷으로 뗐다 붙인다)
                var g = p.Bound.Grow(-1f);
                bool wide = g.Size.X >= g.Size.Y;
                var rail = new Color("#5a6270");
                if (wide)
                {
                    ci.DrawLine(new Vector2(g.Position.X + 2f, g.Position.Y + 1.2f), new Vector2(g.End.X - 2f, g.Position.Y + 1.2f), rail, 1.6f);
                    ci.DrawLine(new Vector2(g.Position.X + 2f, g.End.Y - 1.2f), new Vector2(g.End.X - 2f, g.End.Y - 1.2f), rail, 1.6f);
                }
                else
                {
                    ci.DrawLine(new Vector2(g.Position.X + 1.2f, g.Position.Y + 2f), new Vector2(g.Position.X + 1.2f, g.End.Y - 2f), rail, 1.6f);
                    ci.DrawLine(new Vector2(g.End.X - 1.2f, g.Position.Y + 2f), new Vector2(g.End.X - 1.2f, g.End.Y - 2f), rail, 1.6f);
                }
                FA.Bolts(ci, g, 1.5f, 0.9f);
                break;
            }
        }
    }

    private void VisMachineLive(CanvasItem ci, in VisPlace p)
    {
        var r = p.Bound.Grow(-4f);
        var col = p.Col;
        bool on = Running(p.F);
        float t = _time + p.K * 0.37f;
        switch (p.Key)
        {
            case "machine.listener":
            {
                if (!on) break;
                var o = r.Position + new Vector2(4.5f, r.Size.Y - 4.5f);
                float q = Mathf.PosMod(t * 1.2f, 1f);
                ci.DrawArc(o, 3f + q * 5f, -0.7f, 0.7f, 6, col.WithAlpha(0.5f * (1f - q)), 0.7f, true);
                ci.DrawArc(o, 3f + q * 5f, Mathf.Pi - 0.7f, Mathf.Pi + 0.7f, 6, col.WithAlpha(0.5f * (1f - q)), 0.7f, true);
                break;
            }
            case "machine.nano":
            {
                // 테두리를 따라 기는 은빛 점 (고장 나 있으면 고장 자리로 몰린다)
                bool fault = p.F?.Machine is { Faults.Count: > 0 };
                float per = 2f * (r.Size.X + r.Size.Y);
                for (int k = 0; k < 5; k++)
                {
                    float d = Mathf.PosMod((fault ? t * 0.2f : t * 0.08f) * per + k * per / 5f, per);
                    Vector2 q = d < r.Size.X ? r.Position + new Vector2(d, 0f)
                        : d < r.Size.X + r.Size.Y ? new Vector2(r.End.X, r.Position.Y + d - r.Size.X)
                        : d < 2f * r.Size.X + r.Size.Y ? new Vector2(r.End.X - (d - r.Size.X - r.Size.Y), r.End.Y)
                        : new Vector2(r.Position.X, r.End.Y - (d - 2f * r.Size.X - r.Size.Y));
                    if (fault) q = q.Lerp(r.GetCenter(), 0.4f + 0.2f * Mathf.Sin(t * 3f + k));
                    FA.Dot(ci, q, 0.8f, new Color(0.9f, 0.93f, 1f, 0.85f));
                }
                break;
            }
            case "panel.nodes":
            {
                var box = new Rect2(r.End - new Vector2(8f, 6f), new Vector2(6f, 4.5f));
                bool ping = on && Mathf.PosMod(t * 0.7f, 1f) < 0.1f;
                FA.Led(ci, box.Position + new Vector2(4.6f, 1.5f), ping ? col.Lightened(0.4f) : new Color("#6ee07a"), on ? 0.9f : 0f, 0.7f);
                if (ping) ci.DrawArc(box.GetCenter(), 5f, 0f, Mathf.Tau, 12, col.WithAlpha(0.4f), 0.6f, true);
                break;
            }
        }
    }
}
