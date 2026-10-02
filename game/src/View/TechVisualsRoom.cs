using Godot;
using ShipSim.Core;
using FA = ShipSim.View.FixtureArt;

namespace ShipSim.View;

/// <summary>
/// v16.5b 기술 모습 — 방 벽에 다는 장치(Room: 표의 방 종류 · 벽 자리 하나) · 모든 방(송풍구 · 감쇠 받침 · 거품 노즐) ·
/// 벽 면을 따라(불연 패널 · 감지 점 · 배출 패널 · 차폐 블록) · 통로(동선 화살표 · 생물 발광 띠) · 바닥(난연 광택 · 미끄럼 방지 돌기).
/// 벽 자리는 면 좌표로 그린다: x = 벽을 따라(−16~16), y = 방 쪽(+) · 벽 속(−). 어느 벽이든 같은 그림이 제자리에 붙는다.
/// </summary>
public partial class ShipView
{
    private static void FaceXf(CanvasItem ci, in Face s) => ci.DrawSetTransformMatrix(new Transform2D(s.Tn, s.Nm, s.O));
    private static void NoXf(CanvasItem ci) => ci.DrawSetTransformMatrix(Transform2D.Identity);
    private static Rect2 RR(float x0, float y0, float x1, float y1) => new(x0, y0, x1 - x0, y1 - y0);

    // ═══════════════════════════════ 방 벽 장치 (정적) ═══════════════════════════════

    private void VisSlotStatic(CanvasItem ci, in VisPlace p)
    {
        var col = p.Col;
        FaceXf(ci, p.Face);
        switch (p.Key)
        {
            case "medbay.triage":
            {
                // 분류 꼬리표 걸이 + 바닥 구역 셋
                ci.DrawLine(new Vector2(-12f, -3f), new Vector2(12f, -3f), FA.Chrome, 1.2f);
                var tags = new[] { new Color("#d0402e"), new Color("#e0b64a"), new Color("#4aa860"), new Color("#1a1a1a") };
                for (int i = 0; i < 4; i++)
                {
                    float x = -9f + i * 6f;
                    ci.DrawLine(new Vector2(x, -3f), new Vector2(x, -1f), new Color(1, 1, 1, 0.5f), 0.5f);
                    FA.Box(ci, RR(x - 2f, -1f, x + 2f, 4f), tags[i], 0.8f, new Color(1, 1, 1, 0.3f));
                }
                ci.DrawRect(RR(-14f, 7f, -6f, 14f), new Color("#d0402e").WithAlpha(0.5f), false, 1f);
                ci.DrawRect(RR(-4f, 7f, 4f, 14f), new Color("#e0b64a").WithAlpha(0.5f), false, 1f);
                ci.DrawRect(RR(6f, 7f, 14f, 14f), new Color("#4aa860").WithAlpha(0.5f), false, 1f);
                break;
            }
            case "storage.seedvault":
            {
                // 서리 낀 종자 서랍장: 3×2 서랍 · 손잡이 · 눈꽃
                FA.Box(ci, RR(-11f, -6f, 11f, 9f), new Color("#2a3a48"), 1.5f, new Color("#9fc8e0"));
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j < 2; j++)
                    {
                        var d = RR(-10f + i * 7f, -4f + j * 6.5f, -4f + i * 7f, 1.5f + j * 6.5f);
                        ci.DrawRect(d, new Color("#344a5c"));
                        ci.DrawLine(new Vector2(d.Position.X + 2f, d.GetCenter().Y), new Vector2(d.End.X - 2f, d.GetCenter().Y), FA.Chrome, 0.8f);
                    }
                for (int i = 0; i < 9; i++) FA.Dot(ci, new Vector2(-10f + FA.Hash(p.K, i, 450) * 20f, -5f + FA.Hash(p.K, i, 451) * 13f), 0.6f, Frost.WithAlpha(0.6f));
                for (int k = 0; k < 3; k++)
                {
                    var d = Vector2.FromAngle(k * Mathf.Pi / 3f) * 2.4f;
                    ci.DrawLine(new Vector2(8.5f, -8.5f) - d, new Vector2(8.5f, -8.5f) + d, col.Lightened(0.3f), 0.7f, true);
                }
                break;
            }
            case "galley.sink":
            {
                // 개수대: 철판 · 둥근 우묵 · 수도꼭지 · 초록 비누
                FA.Box(ci, RR(-12f, -6f, 12f, 9f), new Color("#8a929e"), 1.5f, new Color("#c8d0dc"));
                FA.Box(ci, RR(-7f, -3f, 7f, 7f), new Color("#2a3038"), 3f, new Color("#5a6270"));
                ci.DrawCircle(new Vector2(0f, 4f), 0.9f, new Color("#0a0c10"), true, -1f, true);
                ci.DrawLine(new Vector2(0f, -6f), new Vector2(0f, -1.5f), FA.Chrome, 1.4f, true);
                ci.DrawCircle(new Vector2(0f, -5f), 1.4f, FA.Chrome, true, -1f, true);
                FA.Box(ci, RR(8.5f, -4f, 11f, 1f), new Color("#6ec08a"), 0.8f);
                break;
            }
            case "workshop.toolboard":
            {
                // 구멍판 + 공구 윤곽 (스패너 · 망치 · 드라이버)
                FA.Box(ci, RR(-14f, -7f, 14f, 1.5f), new Color("#a88a5a"), 1f, new Color("#6a5232"));
                for (float x = -12f; x < 13f; x += 3f)
                    for (float y = -5.5f; y < 1f; y += 3f) FA.Dot(ci, new Vector2(x, y), 0.45f, new Color(0, 0, 0, 0.45f));
                var tool = new Color("#30343a");
                ci.DrawLine(new Vector2(-11f, -4f), new Vector2(-6f, -1f), tool, 1.4f, true);
                ci.DrawArc(new Vector2(-11.5f, -4.3f), 1.6f, 0.6f, 5.6f, 8, tool, 1.1f, true);
                ci.DrawLine(new Vector2(-1f, -5.5f), new Vector2(-1f, 0f), tool, 1.2f);
                ci.DrawLine(new Vector2(-3.5f, -5.5f), new Vector2(1.5f, -5.5f), tool, 2.2f);
                ci.DrawLine(new Vector2(6f, -6f), new Vector2(6f, -2.5f), tool, 0.8f);
                ci.DrawLine(new Vector2(6f, -2.5f), new Vector2(6f, 0.5f), col.Darkened(0.2f), 2f);
                ci.DrawLine(new Vector2(10f, -6f), new Vector2(12f, 0f), tool, 1f);
                break;
            }
            case "medbay.screen":
            {
                // 원격 진료 화면: 심박 선 · 십자
                FA.Box(ci, RR(-11f, -7f, 11f, 3f), new Color("#06120e"), 1.5f, col.WithAlpha(0.7f));
                var ecg = new[] { new Vector2(-10f, -2f), new Vector2(-4f, -2f), new Vector2(-2.5f, -5.5f), new Vector2(-1f, 1.5f), new Vector2(0.5f, -2f), new Vector2(10f, -2f) };
                ci.DrawPolyline(ecg, new Color("#6ee07a"), 0.8f, true);
                ci.DrawLine(new Vector2(7f, -6f), new Vector2(7f, -3.5f), col, 0.8f);
                ci.DrawLine(new Vector2(5.8f, -4.8f), new Vector2(8.2f, -4.8f), col, 0.8f);
                break;
            }
            case "workshop.printer":
            {
                // 적층 제작기: 철 틀 · 바닥판 · 가로대
                var frame = new Color("#5a6270");
                ci.DrawRect(RR(-9f, -6f, 9f, 11f), new Color(0.08f, 0.09f, 0.11f, 0.85f));
                ci.DrawRect(RR(-9f, -6f, 9f, 11f), frame, false, 1.2f);
                ci.DrawRect(RR(-7f, 8f, 7f, 9.5f), new Color("#2f3a48"));
                ci.DrawLine(new Vector2(-9f, -2f), new Vector2(9f, -2f), frame, 1f);
                break;
            }
            case "lifesupport.biofilter":
            {
                // 생물막 여과 기둥: 층층 초록 막
                FA.Box(ci, RR(-5f, -6f, 5f, 14f), new Color(0.1f, 0.2f, 0.15f, 0.8f), 3f, new Color("#9fd0b0"));
                for (int i = 0; i < 5; i++)
                    ci.DrawRect(RR(-4f, -4f + i * 3.6f, 4f, -2.2f + i * 3.6f), col.Lerp(new Color("#3a6a2a"), i / 5f).WithAlpha(0.6f));
                ci.DrawRect(RR(-6f, -7f, 6f, -5f), FA.Chrome);
                break;
            }
            case "medbay.vat":
            {
                // 재생 배양관: 분홍 액 · 흐린 그림자
                FA.Box(ci, RR(-6f, -6f, 6f, 14f), new Color("#ff9ab8").WithAlpha(0.35f), 5f, new Color(1, 1, 1, 0.55f), 1);
                ci.DrawCircle(new Vector2(0f, 1f), 2f, new Color("#7a3a50").WithAlpha(0.5f), true, -1f, true);
                ci.DrawLine(new Vector2(0f, 3f), new Vector2(0f, 9f), new Color("#7a3a50").WithAlpha(0.45f), 2.5f, true);
                ci.DrawRect(RR(-7f, -7f, 7f, -5f), FA.Steel3);
                ci.DrawRect(RR(-7f, 13f, 7f, 15f), FA.Steel3);
                break;
            }
            case "medbay.cryo":
            {
                // 저온 캡슐: 서리 테 · 얼음빛 창
                FA.Box(ci, RR(-13f, -5f, 13f, 10f), new Color("#c8d8e8"), 6f, Frost);
                FA.Box(ci, RR(-8f, -1f, 8f, 6f), new Color("#7ab8e0").WithAlpha(0.7f), 3f, new Color(1, 1, 1, 0.6f));
                for (int i = 0; i < 8; i++) FA.Dot(ci, new Vector2(-12f + FA.Hash(p.K, i, 452) * 24f, -4f + FA.Hash(p.K, i, 453) * 13f), 0.6f, Colors.White.WithAlpha(0.8f));
                break;
            }
            case "lifesupport.algaetank":
            {
                // 초록 광생물 관 넷
                for (int i = 0; i < 4; i++)
                {
                    float x = -12f + i * 8f;
                    FA.Box(ci, RR(x - 2.6f, -6f, x + 2.6f, 12f), new Color("#2f8a3a").WithAlpha(0.6f), 2.6f, new Color("#a0f0b0").WithAlpha(0.7f), 1);
                }
                ci.DrawLine(new Vector2(-14f, -6.5f), new Vector2(14f, -6.5f), FA.Chrome, 1.4f);
                break;
            }
            case "crew.exosuit":
            {
                // 외골격 거치대: 기둥 둘 · 등뼈 · 팔다리 · 노란 관절
                ci.DrawLine(new Vector2(-10f, -6f), new Vector2(-10f, 12f), FA.Steel4, 1.5f);
                ci.DrawLine(new Vector2(10f, -6f), new Vector2(10f, 12f), FA.Steel4, 1.5f);
                var frame = new Color("#3a3f48");
                ci.DrawLine(new Vector2(0f, -4f), new Vector2(0f, 5f), frame, 2f);
                ci.DrawLine(new Vector2(-6f, -2f), new Vector2(6f, -2f), frame, 1.8f);
                ci.DrawLine(new Vector2(-6f, -2f), new Vector2(-7f, 5f), frame, 1.4f);
                ci.DrawLine(new Vector2(6f, -2f), new Vector2(7f, 5f), frame, 1.4f);
                ci.DrawLine(new Vector2(0f, 5f), new Vector2(-3f, 12f), frame, 1.6f);
                ci.DrawLine(new Vector2(0f, 5f), new Vector2(3f, 12f), frame, 1.6f);
                foreach (var j in new[] { new Vector2(-6f, -2f), new Vector2(6f, -2f), new Vector2(0f, 5f), new Vector2(-1.5f, 8.5f), new Vector2(1.5f, 8.5f) })
                    FA.Dot(ci, j, 1.2f, new Color("#e0b64a"));
                break;
            }
            case "bridge.aicaptain":
            {
                // AI 부함장 눈: 받침판 · 렌즈 · 홍채 고리
                FA.Box(ci, RR(-9f, -7f, 9f, -1f), FA.Steel2, 2f, FA.Steel4);
                ci.DrawCircle(new Vector2(0f, 3f), 7f, new Color("#05070a"), true, -1f, true);
                ci.DrawArc(new Vector2(0f, 3f), 7f, 0f, Mathf.Tau, 28, col, 1.4f, true);
                ci.DrawArc(new Vector2(0f, 3f), 4.5f, 0f, Mathf.Tau, 20, col.WithAlpha(0.45f), 0.8f, true);
                ci.DrawArc(new Vector2(0f, 3f), 6f, 3.6f, 4.6f, 6, new Color(1, 1, 1, 0.3f), 1f, true);
                break;
            }
            case "lifesupport.loop":
            {
                // 재활용 분류함 넷 (파랑 · 초록 · 노랑 · 회색 뚜껑) + 순환 화살표
                var lids = new[] { new Color("#3a7ad9"), new Color("#4aa860"), new Color("#e0b64a"), new Color("#8a8f99") };
                for (int i = 0; i < 4; i++)
                {
                    float x0 = -14f + i * 7f;
                    FA.Box(ci, RR(x0, -2f, x0 + 6f, 9f), new Color("#22272e"), 1f, lids[i].Darkened(0.3f));
                    ci.DrawRect(RR(x0, -3.5f, x0 + 6f, -1.5f), lids[i]);
                    ci.DrawLine(new Vector2(x0 + 1.5f, 2f), new Vector2(x0 + 4.5f, 2f), lids[i].WithAlpha(0.6f), 0.8f);
                }
                for (int k = 0; k < 3; k++)
                {
                    float a0 = k * Mathf.Tau / 3f - Mathf.Pi / 2f;
                    var o = new Vector2(10f, -6f);
                    ci.DrawArc(o, 2.6f, a0, a0 + 1.6f, 5, col, 0.9f, true);
                    var tip = o + Vector2.FromAngle(a0 + 1.6f) * 2.6f;
                    ci.DrawLine(tip, tip + Vector2.FromAngle(a0 + 1.6f + 2.3f) * 1.4f, col, 0.9f, true);
                }
                break;
            }
            case "workshop.assembler":
            {
                // 분자 조립 상자: 유리 벽 · 4×4 빛 격자 (꺼짐)
                FA.Box(ci, RR(-8f, -5f, 8f, 11f), new Color(0.06f, 0.07f, 0.12f, 0.9f), 2f, col.WithAlpha(0.8f));
                for (int i = 1; i < 4; i++)
                {
                    ci.DrawLine(new Vector2(-8f + i * 4f, -5f), new Vector2(-8f + i * 4f, 11f), col.WithAlpha(0.18f), 0.5f);
                    ci.DrawLine(new Vector2(-8f, -5f + i * 4f), new Vector2(8f, -5f + i * 4f), col.WithAlpha(0.18f), 0.5f);
                }
                break;
            }
            case "galley.vat":
            {
                // 배양 단백질 통: 둥근 통 · 분홍 배양액 · 젓개 축
                FA.Can(ci, new Vector2(0f, 4f), 8f, new Color("#6a727e"), new Color("#c8d0dc"));
                ci.DrawCircle(new Vector2(0f, 4f), 6f, new Color("#e08a9a").WithAlpha(0.8f), true, -1f, true);
                ci.DrawCircle(new Vector2(0f, 4f), 1.4f, FA.Chrome, true, -1f, true);
                break;
            }
            case "lifesupport.photosynth":
            {
                // 잎맥 무늬 판: 굵은 줄기 + 갈래
                FA.Box(ci, RR(-14f, -7f, 14f, 3f), new Color("#1a2a14"), 1.5f, new Color("#a8c860"));
                ci.DrawLine(new Vector2(-13f, -2f), new Vector2(13f, -2f), col.Lerp(new Color("#e0d070"), 0.4f), 1.1f, true);
                for (int i = 0; i < 6; i++)
                {
                    float x = -10f + i * 4.2f;
                    float sgn = i % 2 == 0 ? -1f : 1f;
                    ci.DrawLine(new Vector2(x, -2f), new Vector2(x + 3f, -2f + sgn * 4f), col.Lerp(new Color("#e0d070"), 0.4f).WithAlpha(0.7f), 0.6f, true);
                }
                break;
            }
            case "server.quantum":
            {
                // 금빛 층층 냉각대 (샹들리에) · 가는 관
                var gold = new Color("#d8b050");
                for (int i = 0; i < 3; i++)
                {
                    float hw = 9f - i * 2.5f;
                    float y = -5f + i * 5f;
                    FA.Box(ci, RR(-hw, y, hw, y + 2f), gold, 0.8f, gold.Lightened(0.3f));
                    for (int k = -2; k <= 2; k++) ci.DrawLine(new Vector2(k * hw * 0.35f, y + 2f), new Vector2(k * hw * 0.3f, y + 5f), Cu.WithAlpha(0.8f), 0.5f);
                }
                FA.Can(ci, new Vector2(0f, 12f), 2.5f, new Color("#3a4a5a"), Frost);
                break;
            }
            case "robotbay.replicator":
            {
                // 복제 요람: U자 틀 · 반쯤 짜인 로봇 (윤곽과 반만 채운 몸)
                ci.DrawPolyline(new[] { new Vector2(-12f, -6f), new Vector2(-12f, 12f), new Vector2(12f, 12f), new Vector2(12f, -6f) }, FA.Steel4, 1.6f, true);
                ci.DrawRect(RR(-5f, 0f, 5f, 9f), new Color("#c8d0dc"), false, 0.8f);
                ci.DrawRect(RR(-5f, 4.5f, 5f, 9f), new Color("#8a929e"));
                ci.DrawArc(new Vector2(0f, -2.5f), 2.5f, 0f, Mathf.Tau, 12, new Color("#c8d0dc"), 0.8f, true);
                ci.DrawLine(new Vector2(-12f, 4f), new Vector2(-6f, 4f), col.WithAlpha(0.6f), 0.8f);
                break;
            }
            case "lounge.homeworld":
            {
                // 지구 풍경 창: 하늘 · 둥근 땅 · 바다
                FA.Box(ci, RR(-14f, -7f, 14f, 4f), new Color("#5a9ad9"), 2f, FA.Steel4, 2);
                ci.DrawRect(RR(-13f, -6f, 13f, -2f), new Color("#8ac0f0"));
                ci.DrawColoredPolygon(new[] { new Vector2(-13f, 3f), new Vector2(-13f, 0f), new Vector2(-5f, -1.5f), new Vector2(4f, -0.5f), new Vector2(13f, -2f), new Vector2(13f, 3f) }, new Color("#4a9a4a"));
                ci.DrawColoredPolygon(new[] { new Vector2(-2f, 3f), new Vector2(1f, 0.5f), new Vector2(8f, 1f), new Vector2(9f, 3f) }, new Color("#3a7ad0"));
                break;
            }
            case "workshop.forge":
            {
                // 플라스마 도가니: 검은 그릇 · 달아오른 속 · 집게
                ci.DrawCircle(new Vector2(0f, 4f), 7f, new Color("#1a1412"), true, -1f, true);
                ci.DrawArc(new Vector2(0f, 4f), 7f, 0f, Mathf.Tau, 24, new Color("#5a4a3a"), 1.6f, true);
                ci.DrawCircle(new Vector2(0f, 4f), 4f, new Color("#ff8a3c").WithAlpha(0.8f), true, -1f, true);
                ci.DrawLine(new Vector2(8f, -6f), new Vector2(5f, 1f), FA.Steel4, 1.2f, true);
                ci.DrawLine(new Vector2(10f, -6f), new Vector2(6f, 2f), FA.Steel4, 1.2f, true);
                break;
            }
            case "bridge.hypernav":
            {
                // 별 지도 투사기: 받침 원판 · 별 점
                ci.DrawCircle(new Vector2(0f, 8f), 5f, FA.Steel2, true, -1f, true);
                ci.DrawArc(new Vector2(0f, 8f), 5f, 0f, Mathf.Tau, 20, col, 1f, true);
                for (int i = 0; i < 7; i++) FA.Dot(ci, new Vector2(-10f + FA.Hash(p.K, i, 454) * 20f, -5f + FA.Hash(p.K, i, 455) * 9f), 0.6f, new Color(1, 1, 1, 0.5f));
                break;
            }
            case "lifesupport.candles":
            {
                // 산소 양초 보관함: 붉은 뚜껑 두 줄 · 산화제 마름모
                FA.Box(ci, RR(-12f, -6f, 12f, 10f), new Color("#3a1a18"), 1.5f, new Color("#a04030"));
                for (int i = 0; i < 5; i++)
                    for (int j = 0; j < 2; j++) FA.Can(ci, new Vector2(-9f + i * 4.2f, -2f + j * 6f), 1.7f, new Color("#c03a2a"), new Color("#ff8a6a"));
                var d = new Vector2(9.5f, 7f);
                ci.DrawColoredPolygon(new[] { d + new Vector2(0f, -2.6f), d + new Vector2(2.6f, 0f), d + new Vector2(0f, 2.6f), d + new Vector2(-2.6f, 0f) }, new Color("#f2d230"));
                ci.DrawCircle(d, 0.9f, new Color("#1a1a1a"), true, -1f, true);
                break;
            }
            case "lifesupport.algaetrough":
            {
                // 얕은 조류 수조: 긴 테 · 초록 물
                FA.Box(ci, RR(-15f, 0f, 15f, 9f), new Color("#2a6a2a").WithAlpha(0.8f), 2f, new Color("#8a929e"), 1);
                ci.DrawLine(new Vector2(-14f, 2f), new Vector2(14f, 2f), new Color("#8fd65a").WithAlpha(0.5f), 0.7f);
                break;
            }
            case "bridge.mainframe":
            {
                // 중앙 컴퓨터 기둥: 베이지 함 · 테이프 릴 둘 · 불빛 줄
                FA.Box(ci, RR(-10f, -6f, 10f, 12f), new Color("#c8bea0"), 1.5f, new Color("#7a7058"));
                foreach (float x in new[] { -4.5f, 4.5f })
                {
                    ci.DrawCircle(new Vector2(x, 1f), 3.6f, new Color("#2a2a2a"), true, -1f, true);
                    ci.DrawCircle(new Vector2(x, 1f), 1f, FA.Chrome, true, -1f, true);
                }
                ci.DrawRect(RR(-8f, 7.5f, 8f, 10f), new Color("#1a1a1a"));
                break;
            }
            case "quarters.oldstation":
            {
                // 엽서 셋 (핀) · 뜨개 덮개 (지그재그)
                var cards = new[] { new Color("#e8c890"), new Color("#a8d0e8"), new Color("#e8a0a0") };
                for (int i = 0; i < 3; i++)
                {
                    var o = new Vector2(-9f + i * 7f, -4f);
                    ci.DrawSetTransformMatrix(new Transform2D(p.Face.Tn, p.Face.Nm, p.Face.O) * new Transform2D((i - 1) * 0.2f, o));
                    ci.DrawRect(RR(-2.6f, -2f, 2.6f, 2f), cards[i]);
                    ci.DrawRect(RR(-2f, -1.4f, 0.4f, 0.6f), cards[i].Darkened(0.3f));
                    FA.Dot(ci, new Vector2(0f, -1.8f), 0.6f, new Color("#d0402e"));
                }
                FaceXf(ci, p.Face);
                ci.DrawRect(RR(-12f, 3f, 12f, 9f), new Color("#8a5a7a").WithAlpha(0.6f));
                for (float x = -12f; x < 12f; x += 3f)
                {
                    ci.DrawLine(new Vector2(x, 4.5f), new Vector2(x + 1.5f, 6f), new Color("#e8c8d8").WithAlpha(0.6f), 0.7f, true);
                    ci.DrawLine(new Vector2(x + 1.5f, 6f), new Vector2(x + 3f, 4.5f), new Color("#e8c8d8").WithAlpha(0.6f), 0.7f, true);
                }
                break;
            }
            case "comms.convoy":
            {
                // 선단 깃발 셋 (배마다 다른 색) · 연결 표시등
                ci.DrawLine(new Vector2(-13f, -4f), new Vector2(13f, -4f), FA.Chrome, 0.9f);
                var flags = new[] { new Color("#e0623e"), new Color("#5ec8e6"), new Color("#a394ff") };
                for (int i = 0; i < 3; i++)
                {
                    float x = -10f + i * 7f;
                    ci.DrawColoredPolygon(new[] { new Vector2(x, -4f), new Vector2(x + 5f, -4f), new Vector2(x + 2.5f, 2f) }, flags[i]);
                    FA.Dot(ci, new Vector2(x + 2.5f, -2.5f), 0.7f, Colors.White.WithAlpha(0.7f));
                }
                FA.Box(ci, RR(9.5f, -6f, 13.5f, -1f), FA.Steel1, 1f, col);
                break;
            }
            case "workshop.safetysign":
            {
                // 노란 삼각 표지 (!) · 눈 세척대 (초록 판 · 흰 십자 · 그릇)
                ci.DrawColoredPolygon(new[] { new Vector2(-8f, -7.5f), new Vector2(-3f, 0f), new Vector2(-13f, 0f) }, new Color("#f2d230"));
                ci.DrawPolyline(new[] { new Vector2(-8f, -7.5f), new Vector2(-3f, 0f), new Vector2(-13f, 0f), new Vector2(-8f, -7.5f) }, new Color("#1a1a1a"), 0.7f, true);
                ci.DrawLine(new Vector2(-8f, -5f), new Vector2(-8f, -2f), new Color("#1a1a1a"), 1f);
                FA.Dot(ci, new Vector2(-8f, -0.9f), 0.5f, new Color("#1a1a1a"));
                FA.Box(ci, RR(3f, -7f, 11f, -1f), new Color("#2a8a4a"), 1f);
                ci.DrawLine(new Vector2(7f, -6f), new Vector2(7f, -2f), Colors.White, 1f);
                ci.DrawLine(new Vector2(5f, -4f), new Vector2(9f, -4f), Colors.White, 1f);
                ci.DrawArc(new Vector2(7f, 3f), 3f, 0f, Mathf.Pi, 8, FA.Chrome, 1.2f, true);
                break;
            }
            case "bridge.twinholo":
            {
                // 홀로그램 받침 (배는 움직임 층에서 돈다)
                ci.DrawCircle(new Vector2(0f, 8f), 4f, FA.Steel2, true, -1f, true);
                ci.DrawArc(new Vector2(0f, 8f), 4f, 0f, Mathf.Tau, 16, col, 0.9f, true);
                ci.DrawArc(new Vector2(0f, 8f), 2.2f, 0f, Mathf.Tau, 12, col.WithAlpha(0.5f), 0.6f, true);
                break;
            }
            case "workshop.torch":
            {
                // 절단기 거치대 · 감긴 호스
                FA.Box(ci, RR(-12f, -6f, -4f, 4f), FA.Steel1, 1f, FA.Steel4);
                ci.DrawLine(new Vector2(-8f, -4f), new Vector2(-8f, 3f), new Color("#c8a050"), 1.6f);
                ci.DrawLine(new Vector2(-8f, 3f), new Vector2(-8f, 5f), new Color("#3a3a3a"), 1f);
                for (int k = 0; k < 3; k++) ci.DrawArc(new Vector2(6f, 4f), 2f + k * 1.6f, 0f, Mathf.Tau, 14, new Color("#3a3f8a"), 0.9f, true);
                FA.Cable(ci, new Vector2(-8f, -4f), new Vector2(3f, 2f), 1f, new Color("#3a3f8a"), 0.9f);
                break;
            }
            case "galley.vacuum":
            {
                // 진공 포장기 (봉합 막대) · 납작한 봉지 더미
                FA.Box(ci, RR(-12f, -5f, 2f, 5f), new Color("#d8dee8"), 1.5f, new Color("#8a929e"));
                ci.DrawRect(RR(-11f, -1f, 1f, 0.5f), new Color("#2a2a2a"));
                FA.Led(ci, new Vector2(0f, -3.5f), new Color("#6ee07a"), 0.7f, 0.6f);
                for (int i = 0; i < 3; i++)
                {
                    var bg = RR(4f + i * 0.6f, -2f + i * 3f, 14f + i * 0.4f, 1.6f + i * 3f);
                    ci.DrawRect(bg, new Color(0.85f, 0.9f, 0.95f, 0.45f));
                    ci.DrawLine(bg.Position + new Vector2(1.5f, 1.8f), bg.Position + new Vector2(5f, 1.2f), new Color(1, 1, 1, 0.5f), 0.5f);
                }
                break;
            }
            case "portable.led":
            {
                // 작업등 충전 걸이: 걸쇠 셋 · 작은 등
                ci.DrawLine(new Vector2(-12f, -4f), new Vector2(12f, -4f), FA.Steel4, 1.2f);
                for (int i = 0; i < 3; i++)
                {
                    float x = -8f + i * 8f;
                    ci.DrawLine(new Vector2(x, -4f), new Vector2(x, -2f), FA.Chrome, 0.7f);
                    FA.Box(ci, RR(x - 2.5f, -2f, x + 2.5f, 4f), new Color("#e0b64a"), 1f, new Color("#7a5a2a"));
                    ci.DrawCircle(new Vector2(x, 2.4f), 1.4f, new Color("#f8f4e8"), true, -1f, true);
                }
                break;
            }
            case "portable.supercap":
            {
                // 셀 충전대: 칸 다섯
                FA.Box(ci, RR(-12f, -6f, 12f, 8f), FA.Steel1, 1.5f, col.WithAlpha(0.7f));
                for (int i = 0; i < 5; i++) ci.DrawRect(RR(-10f + i * 4.4f, -4f, -7f + i * 4.4f, 6f), new Color("#0c1014"));
                break;
            }
            case "bridge.expmap":
            {
                // 원정 지도판: 남색 판 · 점선 길 · 붉은 핀
                FA.Box(ci, RR(-14f, -7f, 14f, 3f), new Color("#14284a"), 1f, new Color("#c8a050"));
                var route = new[] { new Vector2(-12f, 0f), new Vector2(-6f, -4f), new Vector2(0f, -1f), new Vector2(6f, -5f), new Vector2(12f, -2f) };
                for (int i = 0; i + 1 < route.Length; i++)
                    for (int k = 0; k < 4; k += 2)
                        ci.DrawLine(route[i].Lerp(route[i + 1], k / 4f), route[i].Lerp(route[i + 1], (k + 1) / 4f), new Color("#e8e0c8"), 0.6f);
                foreach (var pin in route) FA.Dot(ci, pin, 0.9f, new Color("#e04030"));
                break;
            }
        }
        NoXf(ci);
    }

    // ═══════════════════════════════ 방 벽 장치 (움직임) ═══════════════════════════════

    private void VisSlotLive(CanvasItem ci, in VisPlace p)
    {
        var col = p.Col;
        bool on = p.Room is not Room r || r.Powered && !r.LightsOut;
        float t = _time + p.K * 0.53f + (p.Room?.Id ?? 0) * 0.29f;
        FaceXf(ci, p.Face);
        switch (p.Key)
        {
            case "galley.sink":
            {
                float q = Mathf.PosMod(t * 0.8f, 1f);
                FA.Dot(ci, new Vector2(0f, -1f + q * 5f), 0.7f, FA.WaterLight.WithAlpha(0.9f * (1f - q)));
                break;
            }
            case "medbay.screen":
            {
                if (!on) break;
                float q = Mathf.PosMod(t * 0.6f, 1f);
                float x = -10f + q * 20f;
                float y = x > -4f && x < 0.5f ? -2f - Mathf.Sin((x + 4f) / 4.5f * Mathf.Pi) * 3f : -2f;
                FA.Dot(ci, new Vector2(x, y), 1f, new Color("#c8ffd0"));
                break;
            }
            case "workshop.printer":
            {
                float prog = on ? Mathf.PosMod(t * 0.12f, 1f) : 0.3f;
                int layers = 1 + (int)(prog * 7f);
                for (int k = 0; k < layers; k++) ci.DrawRect(RR(-4f + k * 0.3f, 7f - k * 1.2f, 4f - k * 0.3f, 8f - k * 1.2f), col.WithAlpha(0.85f));
                float nx = on ? -4f + 8f * (0.5f + 0.5f * Mathf.Sin(t * 3f)) : 0f;
                ci.DrawLine(new Vector2(nx, -2f), new Vector2(nx, 6f - layers * 1.2f), FA.Chrome, 0.8f);
                FA.Dot(ci, new Vector2(nx, 6.5f - layers * 1.2f), 0.9f, new Color("#ffb070").WithAlpha(on ? 1f : 0.3f));
                break;
            }
            case "lifesupport.biofilter":
            {
                if (!on) break;
                for (int k = 0; k < 4; k++)
                {
                    float q = Mathf.PosMod(t * 0.35f + k / 4f, 1f);
                    FA.Dot(ci, new Vector2(-2.5f + 5f * FA.Hash(p.K, k, 456), 12f - q * 17f), 0.7f + 0.5f * q, Colors.White.WithAlpha(0.6f * (1f - q)));
                }
                break;
            }
            case "medbay.vat":
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 1.2f);
                ci.DrawCircle(new Vector2(0f, 4f), 3f + pulse, new Color("#ffc0d0").WithAlpha(on ? 0.15f + 0.15f * pulse : 0.05f), true, -1f, true);
                for (int k = 0; k < 3; k++)
                {
                    float q = Mathf.PosMod(t * 0.4f + k / 3f, 1f);
                    FA.Dot(ci, new Vector2(-3f + k * 3f, 12f - q * 16f), 0.6f, Colors.White.WithAlpha(0.6f * (1f - q)));
                }
                break;
            }
            case "medbay.cryo":
            {
                for (int k = 0; k < 3; k++)
                {
                    float q = Mathf.PosMod(t * 0.25f + k / 3f, 1f);
                    FA.Dot(ci, new Vector2(-12f + 24f * FA.Hash(p.K, k, 457) + q * 4f, 10f + q * 6f), 1.5f + 3f * q, Frost.WithAlpha(0.25f * (1f - q)));
                }
                break;
            }
            case "lifesupport.algaetank":
            {
                for (int i = 0; i < 4; i++)
                {
                    float q = Mathf.PosMod(t * (0.3f + 0.07f * i) + i * 0.31f, 1f);
                    FA.Dot(ci, new Vector2(-12f + i * 8f, 11f - q * 16f), 0.8f, new Color("#d8ffc8").WithAlpha(on ? 0.8f * (1f - q) : 0.2f));
                }
                break;
            }
            case "bridge.aicaptain":
            {
                float br = on ? 0.5f + 0.5f * Mathf.Sin(t * 0.9f) : 0f;
                ci.DrawCircle(new Vector2(0f, 3f), 2.2f + br, col.Lightened(0.3f).WithAlpha(on ? 0.6f + 0.4f * br : 0.1f), true, -1f, true);
                break;
            }
            case "workshop.assembler":
            {
                if (!on) break;
                int n = (int)(t * 4f) % 16;
                for (int k = 0; k <= n; k++)
                    FA.Dot(ci, new Vector2(-6f + (k % 4) * 4f, -3f + (k / 4) * 4f), 0.9f, col.Lightened(0.3f).WithAlpha(k == n ? 1f : 0.45f));
                break;
            }
            case "galley.vat":
            {
                float a = on ? t * 1.6f : 0.4f;
                var d = Vector2.FromAngle(a) * 5f;
                ci.DrawLine(new Vector2(0f, 4f) - d, new Vector2(0f, 4f) + d, FA.Chrome, 1.2f, true);
                break;
            }
            case "lifesupport.photosynth":
            {
                if (!on) break;
                float q = Mathf.PosMod(t * 0.4f, 1f);
                FA.Dot(ci, new Vector2(-13f + 26f * q, -2f), 1f, new Color("#fff0a0").WithAlpha(0.9f));
                int i = (int)(q * 6f);
                FA.Dot(ci, new Vector2(-10f + i * 4.2f + 3f, -2f + (i % 2 == 0 ? -4f : 4f)), 0.7f, new Color("#fff0a0").WithAlpha(0.6f));
                break;
            }
            case "server.quantum":
            {
                for (int k = 0; k < 3; k++)
                {
                    float q = Mathf.PosMod(t * 0.3f + k / 3f, 1f);
                    FA.Dot(ci, new Vector2((k - 1) * 4f, 12f + q * 4f), 1f + 2f * q, Frost.WithAlpha(0.3f * (1f - q)));
                }
                if (on && FA.Hash(p.K, (int)(t * 3f), 458) > 0.7f) FA.Dot(ci, new Vector2(0f, 0f), 0.8f, Colors.White);
                break;
            }
            case "robotbay.replicator":
            {
                if (!on) break;
                float q = Mathf.PosMod(t * 0.5f, 1f);
                var tip = new Vector2(-6f + 1f * Mathf.Sin(t * 3f), 4.5f - q * 4f);
                ci.DrawLine(new Vector2(-12f, 4f), tip, FA.Steel4, 1f, true);
                if (Mathf.PosMod(t * 4f, 1f) < 0.5f)
                    for (int k = 0; k < 3; k++) ci.DrawLine(tip, tip + Vector2.FromAngle(FA.Hash(p.K, k + (int)(t * 8f), 459) * Mathf.Tau) * 2.5f, new Color("#ffe08a"), 0.6f, true);
                break;
            }
            case "lounge.homeworld":
            {
                for (int k = 0; k < 3; k++)
                {
                    float x = Mathf.PosMod(t * 0.8f + k * 9f, 30f) - 15f;
                    if (x < -12f || x > 12f) continue;
                    FA.Dot(ci, new Vector2(x, -4.5f + k * 0.8f), 1.3f, new Color(1, 1, 1, 0.7f));
                    FA.Dot(ci, new Vector2(x + 1.5f, -4.2f + k * 0.8f), 1f, new Color(1, 1, 1, 0.6f));
                }
                break;
            }
            case "workshop.forge":
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 2.3f);
                ci.DrawCircle(new Vector2(0f, 4f), 2.5f + pulse * 1.2f, new Color("#ffe0a0").WithAlpha(on ? 0.7f : 0.15f), true, -1f, true);
                for (int k = 0; on && k < 3; k++)
                {
                    float q = Mathf.PosMod(t * 0.9f + k / 3f, 1f);
                    FA.Dot(ci, new Vector2(Mathf.Sin(t * 2f + k) * 3f, 3f - q * 10f), 0.6f, FA.Ember.WithAlpha(1f - q));
                }
                break;
            }
            case "bridge.hypernav":
            {
                if (!on) break;
                float a = t * 0.5f;
                var prev = new Vector2(0f, 1f);
                for (int i = 1; i <= 5; i++)
                {
                    var q = new Vector2(Mathf.Cos(a + i * 1.1f) * (2f + i * 1.8f), -1f + Mathf.Sin(a + i * 1.1f) * 3f);
                    ci.DrawLine(prev, q, col.WithAlpha(0.7f), 0.7f, true);
                    prev = q;
                }
                FA.Dot(ci, prev, 1f, col.Lightened(0.4f));
                ci.DrawColoredPolygon(new[] { new Vector2(-4f, 8f), new Vector2(4f, 8f), new Vector2(10f, -5f), new Vector2(-10f, -5f) }, col.WithAlpha(0.06f));
                break;
            }
            case "lifesupport.algaetrough":
            {
                for (int k = 0; k < 3; k++)
                {
                    float q = Mathf.PosMod(t * 0.3f + k / 3f, 1f);
                    float x = -12f + 24f * FA.Hash(p.K, k, 460);
                    ci.DrawArc(new Vector2(x, 4.5f), 1f + q * 3.5f, 0f, Mathf.Tau, 12, new Color("#c8ffb0").WithAlpha(0.4f * (1f - q)), 0.5f, true);
                }
                break;
            }
            case "bridge.mainframe":
            {
                foreach (float x in new[] { -4.5f, 4.5f })
                {
                    float a = on ? t * (x < 0 ? 2.2f : -1.7f) : 0f;
                    for (int k = 0; k < 3; k++) ci.DrawLine(new Vector2(x, 1f), new Vector2(x, 1f) + Vector2.FromAngle(a + k * Mathf.Tau / 3f) * 3f, new Color("#8a8f99"), 0.6f);
                }
                for (int i = 0; i < 6; i++) FA.Led(ci, new Vector2(-6.5f + i * 2.6f, 8.8f), i % 2 == 0 ? new Color("#ffb347") : new Color("#6ee07a"), on && FA.Hash(i, (int)(t * 5f), 461) > 0.4f ? 0.9f : 0.1f, 0.6f);
                break;
            }
            case "comms.convoy":
            {
                bool link = on && Mathf.PosMod(t * 0.7f, 1f) < 0.6f;
                FA.Led(ci, new Vector2(11.5f, -3.5f), col, link ? 1f : 0.15f, 1f);
                break;
            }
            case "bridge.twinholo":
            {
                if (!on) break;
                float sx = Mathf.Cos(t * 0.7f);
                var ship = new[] { new Vector2(-6f, 0f), new Vector2(4f, -1.5f), new Vector2(7f, 0f), new Vector2(4f, 1.5f), new Vector2(-6f, 0f) };
                var pts = new Vector2[ship.Length];
                for (int i = 0; i < ship.Length; i++) pts[i] = new Vector2(ship[i].X * sx, ship[i].Y * 1.6f - 2f + ship[i].X * 0.15f * Mathf.Sin(t * 0.7f));
                ci.DrawPolyline(pts, col.WithAlpha(0.85f), 0.7f, true);
                ci.DrawLine(new Vector2(-4f * sx, -2f), new Vector2(-4f * sx, 7f), col.WithAlpha(0.15f), 0.5f);
                ci.DrawLine(new Vector2(4f * sx, -2f), new Vector2(4f * sx, 7f), col.WithAlpha(0.15f), 0.5f);
                break;
            }
            case "workshop.torch":
            {
                float fl = 0.7f + 0.3f * FA.Hash(p.K, (int)(t * 20f), 462);
                FA.Dot(ci, new Vector2(-8f, 5.8f), 0.9f * fl, new Color("#8ac8ff").WithAlpha(on ? 0.9f : 0f));
                break;
            }
            case "portable.led":
            {
                for (int i = 0; i < 3; i++)
                {
                    bool full = FA.Hash(p.K, i, 463) > 0.4f || Mathf.PosMod(t * 0.05f + i * 0.3f, 1f) > 0.7f;
                    FA.Led(ci, new Vector2(-8f + i * 8f + 2f, -1f), full ? new Color("#6ee07a") : new Color("#ff5c4c"), on ? 0.9f : 0f, 0.5f);
                }
                break;
            }
            case "portable.supercap":
            {
                for (int i = 0; i < 5; i++)
                {
                    float lv = on ? Mathf.PosMod(t * 0.08f + i * 0.21f, 1f) : 0.3f;
                    ci.DrawRect(RR(-9.5f + i * 4.4f, 6f - 10f * lv, -7.5f + i * 4.4f, 6f), col.WithAlpha(0.85f));
                }
                break;
            }
        }
        NoXf(ci);
    }

    // ═══════════════════════════════ 모든 방 ═══════════════════════════════

    private Cell RoomMid(Room r)
    {
        var best = r.Cells[0];
        float bd = float.MaxValue;
        foreach (var c in r.Cells)
        {
            if (!_world.Ship.IsOpenFloor(c)) continue;
            float d = Mathf.Abs(c.X + 0.5f - r.Center.X) + Mathf.Abs(c.Y + 0.5f - r.Center.Y);
            if (d < bd) { bd = d; best = c; }
        }
        return best;
    }

    private void VisRoomAllStatic(CanvasItem ci, in VisPlace p)
    {
        var r = p.Room!;
        var col = p.Col;
        switch (p.Key)
        {
            case "duct.zones":
            {
                // 구역 색 송풍구: 네모 틀 · 방사 날개 · 구역 색 테
                var zone = col.Lerp(Palette.Room(r.Kind), 0.5f);
                var o = CellRect(RoomMid(r)).GetCenter();
                FA.Box(ci, new Rect2(o - new Vector2(7f, 7f), new Vector2(14f, 14f)), new Color("#1e232b").WithAlpha(0.8f), 2f, zone.WithAlpha(0.8f));
                for (int k = 0; k < 8; k++)
                {
                    var d = Vector2.FromAngle(k * Mathf.Tau / 8f);
                    ci.DrawLine(o + d * 2f, o + d * 6f, new Color("#8a929e"), 0.8f, true);
                }
                ci.DrawCircle(o, 1.6f, zone, true, -1f, true);
                break;
            }
            case "hull.damper":
            {
                // 관성 감쇠 받침: 방 네 모서리에 비스듬한 피스톤 (통 · 막대 · 받침판)
                var rr = RoomRect(r).Grow(-2f);
                var corners = new[] { rr.Position, new Vector2(rr.End.X, rr.Position.Y), new Vector2(rr.Position.X, rr.End.Y), rr.End };
                foreach (var cpt in corners)
                {
                    var into = (rr.GetCenter() - cpt).Normalized();
                    var cell = CellAtPx(cpt + into * 6f);
                    if (_world.Ship.RoomAt(cell) != r) continue;
                    ci.DrawRect(new Rect2(cpt + into * 1f - new Vector2(2.2f, 2.2f), new Vector2(4.4f, 4.4f)), FA.Steel4);
                    ci.DrawLine(cpt + into * 2f, cpt + into * 8f, new Color("#3a4250"), 3f, true);
                    ci.DrawLine(cpt + into * 8f, cpt + into * 12f, FA.Chrome, 1.4f, true);
                    ci.DrawCircle(cpt + into * 12.5f, 1.2f, col, true, -1f, true);
                }
                break;
            }
            case "wall.foamnozzle":
            {
                // 천장 폭발 억제 거품 노즐: 흰 돔 · 붉은 테 · 잇는 가는 관
                int n = Mathf.Clamp(r.Cells.Count / 12, 1, 4);
                var rr = RoomRect(r);
                var prev = Vector2.Zero;
                for (int i = 0; i < n; i++)
                {
                    var o = new Vector2(rr.Position.X + rr.Size.X * (i + 0.5f) / n, rr.Position.Y + rr.Size.Y * 0.5f + 0.5f);
                    if (_world.Ship.RoomAt(CellAtPx(o)) != r) continue;
                    if (prev != Vector2.Zero) ci.DrawLine(prev, o, new Color(0.85f, 0.85f, 0.9f, 0.25f), 0.8f);
                    ci.DrawCircle(o, 2.6f, new Color("#e8ecef"), true, -1f, true);
                    ci.DrawArc(o, 2.6f, 0f, Mathf.Tau, 12, new Color("#d0402e"), 0.8f, true);
                    FA.Dot(ci, o, 0.7f, col.Darkened(0.3f));
                    prev = o;
                }
                break;
            }
        }
    }

    private void VisRoomAllLive(CanvasItem ci, in VisPlace p)
    {
        var r = p.Room!;
        switch (p.Key)
        {
            case "duct.zones":
            {
                // 바람 결: 송풍구에서 퍼지는 가는 줄 (정전이면 멎는다)
                if (!r.Powered) break;
                var o = CellRect(RoomMid(r)).GetCenter();
                for (int k = 0; k < 4; k++)
                {
                    float q = Mathf.PosMod(_time * 0.5f + k / 4f + r.Id * 0.17f, 1f);
                    var d = Vector2.FromAngle(k * Mathf.Pi / 2f + 0.4f);
                    ci.DrawLine(o + d * (8f + q * 14f), o + d * (12f + q * 14f), new Color(0.85f, 0.95f, 1f, 0.25f * (1f - q)), 0.8f, true);
                }
                break;
            }
        }
    }

    // ═══════════════════════════════ 벽 면을 따라 ═══════════════════════════════

    private void VisWallStatic(CanvasItem ci, in VisPlace p)
    {
        var col = p.Col;
        FaceXf(ci, p.Face);
        switch (p.Key)
        {
            case "wall.fireproof":
            {
                // 불연 광물 패널 · 붉은 팽창 띠 · 불꽃에 빗금
                ci.DrawRect(RR(-15.5f, -9f, 15.5f, -1f), new Color("#b8aca8").WithAlpha(0.55f));
                for (float x = -14f; x < 15f; x += 2.5f) ci.DrawLine(new Vector2(x, -8.5f), new Vector2(x + 1.2f, -1.5f), new Color(1, 1, 1, 0.12f), 0.5f);
                ci.DrawLine(new Vector2(-15.5f, -1.6f), new Vector2(15.5f, -1.6f), new Color("#d0502e"), 1.2f);
                ci.DrawColoredPolygon(new[] { new Vector2(10f, -3f), new Vector2(11.4f, -7.5f), new Vector2(12.8f, -3f) }, new Color("#ff8a3c"));
                ci.DrawLine(new Vector2(9.5f, -7.5f), new Vector2(13.3f, -3f), col.Darkened(0.4f), 0.8f);
                break;
            }
            case "wall.sensormesh":
            {
                // 감지 점 격자 (5×2)
                for (int i = 0; i < 5; i++)
                    for (int j = 0; j < 2; j++)
                    {
                        var o = new Vector2(-12f + i * 6f, -6.5f + j * 3.5f);
                        FA.Dot(ci, o, 0.9f, new Color("#14181e"));
                        ci.DrawArc(o, 1.1f, 0f, Mathf.Tau, 8, col.WithAlpha(0.6f), 0.4f, true);
                        if (i < 4) ci.DrawLine(o + new Vector2(1.1f, 0f), o + new Vector2(4.9f, 0f), col.WithAlpha(0.15f), 0.4f);
                    }
                break;
            }
            case "wall.blastvent":
            {
                // 배출 패널: 칼집 낸 X · 경첩 · 밖으로 향한 노란 화살표
                FA.Box(ci, RR(-10f, -13f, 10f, -1f), new Color("#3a3f48"), 1f, new Color("#e0b64a"));
                ci.DrawLine(new Vector2(-9f, -12f), new Vector2(9f, -2f), new Color(0, 0, 0, 0.5f), 0.7f);
                ci.DrawLine(new Vector2(-9f, -2f), new Vector2(9f, -12f), new Color(0, 0, 0, 0.5f), 0.7f);
                ci.DrawRect(RR(-10.5f, -12f, -9f, -10f), FA.Chrome);
                ci.DrawRect(RR(-10.5f, -4f, -9f, -2f), FA.Chrome);
                ci.DrawLine(new Vector2(4f, 3f), new Vector2(4f, -6f), new Color("#e0b64a"), 1.2f);
                ci.DrawPolyline(new[] { new Vector2(2f, -4f), new Vector2(4f, -7f), new Vector2(6f, -4f) }, new Color("#e0b64a"), 1.2f, true);
                break;
            }
            case "shelter.cellar":
            {
                // 두꺼운 차폐 블록 (벽돌 쌓기) · 세 번째마다 대피 표지
                for (int row = 0; row < 3; row++)
                    for (int i = 0; i < 4; i++)
                    {
                        float x0 = -16f + i * 8f + (row % 2) * 4f;
                        var bk = RR(Mathf.Max(-16f, x0), -14f + row * 4.4f, Mathf.Min(16f, x0 + 7.6f), -10f + row * 4.4f);
                        ci.DrawRect(bk, new Color("#5a5e66"));
                        ci.DrawRect(bk, new Color(0, 0, 0, 0.4f), false, 0.6f);
                    }
                if (p.K % 3 == 0)
                {
                    var o = new Vector2(0f, 4f);
                    ci.DrawCircle(o, 3.4f, new Color("#f2d230"), true, -1f, true);
                    for (int k = 0; k < 3; k++) ci.DrawArc(o, 2f, k * Mathf.Tau / 3f - 0.5f, k * Mathf.Tau / 3f + 0.5f, 5, new Color("#1a1a1a"), 1.8f, true);
                    FA.Dot(ci, o, 0.7f, new Color("#1a1a1a"));
                }
                break;
            }
        }
        NoXf(ci);
    }

    private void VisWallLive(CanvasItem ci, in VisPlace p)
    {
        if (p.Key != "wall.sensormesh") return;
        bool on = p.Room is Room r && r.Powered;
        if (!on) return;
        FaceXf(ci, p.Face);
        switch (p.Key)
        {
            case "wall.sensormesh":
            {
                // 감지 물결이 벽을 따라 쓸고 간다
                float q = Mathf.PosMod(_time * 0.6f - p.Face.Floor.X * 0.11f - p.Face.Floor.Y * 0.07f, 1f);
                int i = (int)(q * 5f);
                FA.Led(ci, new Vector2(-12f + i * 6f, -6.5f), p.Col, 0.9f, 0.7f);
                FA.Led(ci, new Vector2(-12f + i * 6f, -3f), p.Col, 0.6f, 0.7f);
                break;
            }
        }
        NoXf(ci);
    }

    // ═══════════════════════════════ 통로 ═══════════════════════════════

    private bool CorridorAlong(in Face f)
    {
        var r = f.Room;
        bool horiz = r.MaxX - r.MinX >= r.MaxY - r.MinY;
        return horiz ? f.Dir.Y == -1 : f.Dir.X == -1;
    }

    private void VisCorridorStatic(CanvasItem ci, in VisPlace p)
    {
        var col = p.Col;
        switch (p.Key)
        {
            case "corridor.flowlines":
            {
                // 동선 화살표: 두 줄 (오른쪽으로 다닌다) — 통로 긴 쪽 벽 하나에서만
                if (!CorridorAlong(p.Face)) break;
                FaceXf(ci, p.Face);
                for (int lane = 0; lane < 2; lane++)
                {
                    float y = lane == 0 ? 9f : 23f;
                    float dir = lane == 0 ? 1f : -1f;
                    for (int k = -1; k <= 1; k += 2)
                    {
                        float x = k * 6f;
                        ci.DrawPolyline(new[] { new Vector2(x - 2.5f * dir, y - 3f), new Vector2(x + 1.5f * dir, y), new Vector2(x - 2.5f * dir, y + 3f) }, col.WithAlpha(0.45f), 1.6f, true);
                    }
                }
                NoXf(ci);
                break;
            }
            case "corridor.biolamp":
            {
                // 생물 발광 띠: 물결 관 · 빛 알갱이
                FaceXf(ci, p.Face);
                var pts = new Vector2[9];
                for (int i = 0; i < 9; i++) pts[i] = new Vector2(-16f + i * 4f, -3.5f + Mathf.Sin(i * 1.3f + p.K) * 1.2f);
                ci.DrawPolyline(pts, new Color("#1a4a48"), 2.6f, true);
                ci.DrawPolyline(pts, col.Lerp(new Color("#5fe0d0"), 0.6f).WithAlpha(0.6f), 1f, true);
                for (int i = 0; i < 4; i++) FA.Dot(ci, pts[1 + i * 2], 0.8f, new Color("#a0fff0").WithAlpha(0.8f));
                NoXf(ci);
                break;
            }
        }
    }

    private void VisCorridorLive(CanvasItem ci, in VisPlace p)
    {
        switch (p.Key)
        {
            case "corridor.biolamp":
            {
                // 숨 쉬듯 밝아졌다 어두워진다 (어두운 통로에서 더 또렷하다)
                float br = 0.5f + 0.5f * Mathf.Sin(_time * 0.7f + p.Face.Floor.X * 0.4f + p.Face.Floor.Y * 0.3f);
                FaceXf(ci, p.Face);
                for (int i = 0; i < 4; i++) FA.Dot(ci, new Vector2(-12f + i * 8f, -3.5f + Mathf.Sin((1 + i * 2) * 1.3f + p.K) * 1.2f), 1.2f + br, new Color("#a0fff0").WithAlpha(0.25f + 0.4f * br));
                NoXf(ci);
                break;
            }
        }
    }

    // ═══════════════════════════════ 바닥 ═══════════════════════════════

    private void VisFloorStatic(CanvasItem ci, in VisPlace p)
    {
        var rc = CellRect(p.Cell);
        switch (p.Key)
        {
            case "floor.retardant":
            {
                // 난연 코팅: 비스듬한 광택 · 주홍 테
                ci.DrawRect(rc.Grow(-2f), new Color("#ff9a5c").WithAlpha(0.35f), false, 0.8f);
                ci.DrawLine(rc.Position + new Vector2(5f, T - 9f), rc.Position + new Vector2(T - 9f, 5f), new Color(1, 1, 1, 0.12f), 3f, true);
                ci.DrawLine(rc.Position + new Vector2(9f, T - 5f), rc.Position + new Vector2(T - 5f, 9f), new Color(1, 1, 1, 0.06f), 1.5f, true);
                break;
            }
            case "floor.grip":
            {
                // 미끄럼 방지 돌기 4×4 (위 왼쪽에 빛)
                for (int i = 0; i < 4; i++)
                    for (int j = 0; j < 4; j++)
                    {
                        var o = rc.Position + new Vector2(5f + i * 7.3f, 5f + j * 7.3f);
                        FA.Dot(ci, o, 1.3f, new Color(0, 0, 0, 0.35f));
                        FA.Dot(ci, o - new Vector2(0.4f, 0.4f), 0.6f, new Color(1, 1, 1, 0.2f));
                    }
                break;
            }
        }
    }
}
