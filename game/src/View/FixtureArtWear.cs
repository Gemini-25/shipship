using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.5c 설비가 겪은 일이 몸에 남는다 (읽기만):
///   낡음(정적 · 마모 단계) — 고장 모양마다 다른 자국이 고장 나는 자리에 쌓인다
///     (불꽃 · 열 · 연기 → 그을음 · 열 변색 / 샘 · 김 → 녹 줄 · 물때 / 갈림 · 걸림 → 기름때 · 쇳가루 / 깜빡임 → 손때 · 먼지 / 가스 → 누런 잔여물)
///     + 칠 벗겨진 모서리 · 먼지. 수명이 깎이면 긁힘 · 찌그러짐이 는다.
///   순간(동적) — 막 고장 난 순간의 큰 터짐 · 고치는 사람이 있으면 열린 점검창과 고장 모양에 맞는 연장
///     (용접 불빛 · 실링폼 · 렌치 · 진단 탐침).
/// 마모 · 수명은 단계로 묶어 Signature 에 넣는다 — 단계가 바뀔 때만 정적 몸체를 다시 그린다 (성능).
/// </summary>
public static partial class FixtureArt
{
    private static readonly Color Soot = new(0.07f, 0.05f, 0.04f);
    private static readonly Color Rust = new("#8a4a24");
    private static readonly Color RustLight = new("#b8682e");
    private static readonly Color Mineral = new("#d8d2c0");
    private static readonly Color Oil = new(0.04f, 0.04f, 0.03f);
    private static readonly Color Residue = new("#a8a03a");
    private static readonly Color TemperGold = new("#c8a050");
    private static readonly Color TemperBlue = new("#4a5aa8");
    private static readonly Color ChipMetal = new("#7a8494");
    private static readonly Color Arc = new("#cfe6ff");
    private static readonly Color Foam = new("#f0ead8");

    /// <summary>마모 단계 0~4 (0.25 마다).</summary>
    internal static int WearStep(Machine m) => Mathf.Clamp((int)(m.Wear * 4f), 0, 4);

    /// <summary>상처 단계 0~5 (수명 0.2 마다 깎인 만큼).</summary>
    internal static int ScarStep(Machine m) => Mathf.Clamp((int)((1f - m.Condition) * 5f), 0, 5);

    // ═══════════════════════════════ 정적: 낡음 · 상처 ═══════════════════════════════

    /// <summary>몸체 위에 쌓인 낡음 (정적 층 · PaintBody 가 몸체 바로 뒤에 부른다).</summary>
    private static void Weathering(in Fix x, Art a)
    {
        if (x.M is not Machine m) return;
        int ws = WearStep(m), ss = ScarStep(m);
        if (ws == 0 && ss == 0) return;
        var ci = x.Ci;
        var e = x.P(a.Eu, a.Ev);
        float k = ws / 4f;

        if (ws > 0)
        {
            switch (a.Fault)
            {
                case Look.Sparks:
                case Look.Smoke:
                    // 그을음: 불꽃 · 연기가 나던 자리 위로 번진 검댕 (연기는 위로 오르니 위쪽이 길다)
                    for (int i = 0; i < 2 + ws * 2; i++)
                    {
                        var p = e + new Vector2((Hash(x.Id, i, 201) - 0.5f) * x.Px(14f), -Hash(x.Id, i, 202) * x.Px(12f) * k);
                        Dot(ci, p, x.Px(1.5f + 3.5f * Hash(x.Id, i, 203)) * (0.6f + k), Soot.WithAlpha(0.16f + 0.1f * k));
                    }
                    if (a.Fault == Look.Sparks)
                        for (int i = 0; i < ws; i++) // 탄 점 (불똥 맞은 자리)
                            Dot(ci, e + new Vector2(Hash(x.Id, i, 204) - 0.5f, Hash(x.Id, i, 205) - 0.5f) * x.Px(16f), 0.8f, Soot.WithAlpha(0.7f));
                    break;
                case Look.Heat:
                    // 열 변색: 금빛 → 푸른 띠 (뜨거워졌다 식은 쇠)
                    ci.Arc(e, x.Px(5f + 2f * ws), 0f, Mathf.Tau, 20, TemperGold.WithAlpha(0.18f + 0.07f * ws), x.Px(2.2f), true);
                    ci.Arc(e, x.Px(8f + 2.5f * ws), 0f, Mathf.Tau, 24, TemperBlue.WithAlpha(0.12f + 0.05f * ws), x.Px(1.8f), true);
                    Dot(ci, e, x.Px(3f + ws), Soot.WithAlpha(0.18f + 0.06f * ws));
                    break;
                case Look.Leak:
                case Look.Steam:
                    // 녹 줄: 새던 자리에서 아래로 흘러내린 자국 · 김이 나던 곳은 하얀 물때 테
                    for (int i = 0; i < 1 + ws; i++)
                    {
                        float dx = (Hash(x.Id, i, 206) - 0.5f) * x.Px(10f);
                        float len = x.Px(5f + 9f * Hash(x.Id, i, 207)) * (0.5f + k);
                        var top = e + new Vector2(dx, 0f);
                        Line(ci, top, top + new Vector2(dx * 0.15f, len), (i % 2 == 0 ? Rust : RustLight).WithAlpha(0.35f + 0.12f * ws), x.Px(1.2f));
                        Dot(ci, top + new Vector2(dx * 0.15f, len), x.Px(1.3f), Rust.WithAlpha(0.4f + 0.1f * ws));
                    }
                    if (a.Fault == Look.Steam) ci.Arc(e, x.Px(4f + ws), Mathf.Pi * 1.1f, Mathf.Pi * 1.9f, 10, Mineral.WithAlpha(0.3f + 0.08f * ws), x.Px(1.4f), true);
                    else Dot(ci, e + new Vector2(0f, x.Px(3f)), x.Px(2f + ws), Rust.WithAlpha(0.14f + 0.05f * ws)); // 녹 번진 얼룩
                    break;
                case Look.Grind:
                case Look.Jam:
                {
                    // 기름때: 번들거리는 검은 얼룩과 가장자리 반사 · 갈리던 곳엔 쇳가루
                    ci.DrawSetTransform(e, Hash(x.Id, 0, 208) * Mathf.Pi, new Vector2(1f, 0.55f));
                    ci.Circle(Vector2.Zero, x.Px(3f + 1.8f * ws), Oil.WithAlpha(0.28f + 0.08f * ws), true, -1f, true);
                    ci.Arc(Vector2.Zero, x.Px(2.4f + 1.4f * ws), Mathf.Pi * 1.15f, Mathf.Pi * 1.45f, 6, new Color(1, 1, 1, 0.14f), 1f, true);
                    ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                    if (a.Fault == Look.Grind)
                        for (int i = 0; i < 2 * ws; i++)
                            Dot(ci, e + Vector2.FromAngle(Hash(x.Id, i, 209) * Mathf.Tau) * x.Px(4f + 7f * Hash(x.Id, i, 210)), 0.6f, Chrome.WithAlpha(0.55f));
                    else // 걸리던 곳: 억지로 밀어 넣은 긁힌 자국 두 줄
                        for (int i = 0; i < 2; i++)
                            Line(ci, e + x.U * x.Px(-5f) + x.V * x.Px(i * 2f - 1f), e + x.U * x.Px(5f) + x.V * x.Px(i * 2f - 1f), ChipMetal.WithAlpha(0.25f + 0.08f * ws), 0.8f);
                    break;
                }
                case Look.Flicker:
                {
                    // 손때 · 먼지: 자주 누르던 앞쪽 가장자리에 번들거리는 손자국, 위쪽 모서리에 먼지
                    var front = x.C + x.Front * (Mathf.Abs(x.Front.X) > 0.5f ? x.B.Size.X : x.B.Size.Y) * 0.36f;
                    for (int i = 0; i < 1 + ws; i++)
                    {
                        var p = front + new Vector2(Hash(x.Id, i, 211) - 0.5f, Hash(x.Id, i, 212) - 0.5f) * x.Px(12f);
                        ci.DrawSetTransform(p, Hash(x.Id, i, 213) * Mathf.Pi, new Vector2(1f, 0.7f));
                        ci.Arc(Vector2.Zero, x.Px(2f), 0f, Mathf.Tau, 10, new Color(1, 1, 1, 0.06f + 0.02f * ws), 0.8f, true);
                        ci.Arc(Vector2.Zero, x.Px(1.1f), 0f, Mathf.Tau, 8, new Color(1, 1, 1, 0.05f + 0.02f * ws), 0.7f, true);
                        ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                    }
                    for (int i = 0; i < 3 * ws; i++)
                        Dot(ci, x.P(Hash(x.Id, i, 214), Hash(x.Id, i, 215) * 0.15f), 0.6f, Powder.WithAlpha(0.3f));
                    break;
                }
                case Look.Gas:
                    // 누런 잔여물: 새던 이음매 둘레에 테가 겹겹이 앉았다
                    for (int i = 0; i < ws; i++)
                        ci.Arc(e, x.Px(3f + i * 2.5f), Hash(x.Id, i, 216) * Mathf.Tau, Hash(x.Id, i, 216) * Mathf.Tau + Mathf.Pi * (1.2f + 0.5f * Hash(x.Id, i, 217)), 12, Residue.WithAlpha(0.32f - i * 0.05f), x.Px(1.3f), true);
                    Dot(ci, e, x.Px(1.6f + 0.6f * ws), Residue.WithAlpha(0.3f));
                    break;
            }

            // 모든 설비: 칠 벗겨진 모서리 (손 · 수레가 스치는 앞쪽 모서리부터)
            for (int i = 0; i < ws * 2; i++)
            {
                float u = Hash(x.Id, i, 218);
                bool frontEdge = i % 3 != 2;
                var p = frontEdge ? x.C + x.Front * ((Mathf.Abs(x.Front.X) > 0.5f ? x.B.Size.X : x.B.Size.Y) * 0.5f - 1f) + new Vector2(Mathf.Abs(x.Front.Y), Mathf.Abs(x.Front.X)) * (u - 0.5f) * (Mathf.Abs(x.Front.X) > 0.5f ? x.B.Size.Y : x.B.Size.X) * 0.9f
                    : x.P(u, Hash(x.Id, i, 219) > 0.5f ? 0.02f : 0.98f);
                ci.Box(new Rect2(p - new Vector2(1f, 0.6f), new Vector2(1.4f + 1.6f * Hash(x.Id, i, 220), 1.2f)), ChipMetal.WithAlpha(0.45f));
            }
        }

        if (ss > 0)
        {
            // 긁힘: 가는 밝은 선 (수명이 깎일수록 많다)
            for (int i = 0; i < ss * 2; i++)
            {
                var p0 = x.P(0.1f + 0.8f * Hash(x.Id, i, 221), 0.1f + 0.8f * Hash(x.Id, i, 222));
                var d = Vector2.FromAngle(Hash(x.Id, i, 223) * Mathf.Pi);
                float len = x.Px(3f + 6f * Hash(x.Id, i, 224));
                Line(ci, p0, p0 + d * len, new Color(1, 1, 1, 0.1f + 0.02f * ss), 0.7f);
                Line(ci, p0 + new Vector2(0.6f, 0.6f), p0 + d * len + new Vector2(0.6f, 0.6f), new Color(0, 0, 0, 0.18f), 0.6f);
            }
            if (ss >= 3)
            {
                // 찌그러짐: 반달 그림자 + 밝은 테 (빛이 왼쪽 위에서)
                var dp = x.P(0.2f + 0.6f * Hash(x.Id, 1, 225), 0.25f + 0.5f * Hash(x.Id, 1, 226));
                float r = x.Px(3f + 0.6f * ss);
                ci.Arc(dp, r, Mathf.Pi * 0.1f, Mathf.Pi * 0.9f, 10, new Color(0, 0, 0, 0.35f), x.Px(1.6f), true);
                ci.Arc(dp, r, Mathf.Pi * 1.1f, Mathf.Pi * 1.9f, 10, new Color(1, 1, 1, 0.12f), x.Px(1f), true);
            }
            if (ss >= 4) // 다시 박은 나사 하나 (색이 다르다)
                Bolt(ci, x.P(0.08f + 0.84f * Hash(x.Id, 2, 227), 0.1f), 0.9f);
        }
    }

    // ═══════════════════════════════ 동적: 고장 순간 · 고치는 중 ═══════════════════════════════

    /// <summary>막 고장 난 지 이만큼은 크게 터진다 (틱).</summary>
    private static readonly int BurstTicks = SimTime.Minutes(4);

    /// <summary>고장 순간 · 고치는 중 (동적 층 · PaintState 가 상태 효과 뒤에 부른다).</summary>
    private static void Moments(in Fix x, Art a, Machine m, Vector2 e)
    {
        if (m.Faults.Count == 0 || x.W == null) return;
        var ci = x.Ci;
        long newest = long.MinValue;
        foreach (var q in m.Faults) if (q.Since > newest) newest = q.Since;
        long age = x.W.Tick - newest;

        // 고장 순간: 고장 모양마다 다른 큰 터짐 (짧게)
        if (age >= 0 && age < BurstTicks)
        {
            float k = 1f - age / (float)BurstTicks;
            switch (a.Fault)
            {
                case Look.Sparks:
                case Look.Grind:
                    Dot(ci, e, x.Px(10f) * k + 2f, SparkHot.WithAlpha(0.3f * k));
                    for (int i = 0; i < (x.Lod == 0 ? 4 : 10); i++)
                    {
                        var d = Vector2.FromAngle(Hash(x.Id, i, 230) * Mathf.Tau);
                        float r = x.Px(4f + 16f * (1f - k) * (0.5f + Hash(x.Id, i, 231)));
                        Line(ci, e + d * r * 0.6f, e + d * r, (a.Fault == Look.Sparks ? SparkHot : Ember).WithAlpha(k), 1.3f);
                    }
                    break;
                case Look.Smoke:
                case Look.Heat:
                    // 확 피어오르는 검은 덩어리
                    for (int i = 0; i < 4; i++)
                        Dot(ci, e + new Vector2((i - 1.5f) * x.Px(4f), -(1f - k) * x.Px(16f) * (0.6f + 0.2f * i)), x.Px(4f + 6f * (1f - k)), Soot.WithAlpha(0.4f * k));
                    if (a.Fault == Look.Heat) Dot(ci, e, x.Px(8f), Ember.WithAlpha(0.4f * k));
                    break;
                case Look.Leak:
                case Look.Steam:
                case Look.Gas:
                {
                    // 확 뿜는 줄기 (터진 이음매)
                    var col = a.Fault == Look.Leak ? LeakBlue : a.Fault == Look.Gas ? GasTint : SteamWhite;
                    var dir = Vector2.FromAngle(-Mathf.Pi * 0.5f + (Hash(x.Id, 0, 232) - 0.5f) * 1.6f);
                    for (int i = 0; i < 6; i++)
                    {
                        float ph = Mathf.PosMod(x.T * 3f + i / 6f, 1f);
                        Dot(ci, e + dir * x.Px(22f) * ph * (0.4f + 0.6f * (1f - k)) + dir.Orthogonal() * Mathf.Sin(i * 2.1f) * x.Px(2f), x.Px(1.5f + 3f * ph), col.WithAlpha(0.6f * k * (1f - ph)));
                    }
                    break;
                }
                case Look.Flicker:
                    // 펑 하고 나가는 화면: 흰 번쩍임 → 가운데로 줄어드는 선
                    ci.Box(new Rect2(e - new Vector2(x.Px(9f), x.Px(6f) * k), new Vector2(x.Px(18f), x.Px(12f) * k + 1f)), NoiseWhite.WithAlpha(0.5f * k));
                    break;
                case Look.Jam:
                {
                    // 덜컥 멈춤: 몸체가 한 번 튀는 테
                    float s = 1f + 0.08f * k * Mathf.Sin(age * 0.9f);
                    Gfx.RoundRect(ci, new Rect2(x.C - x.B.Size * 0.5f * s, x.B.Size * s), new Color(0, 0, 0, 0f), 4, Danger.WithAlpha(0.6f * k), 2);
                    break;
                }
            }
        }

        // 고치는 중: 쓰는 사람이 붙어 일하면 점검창이 열리고, 고장 모양에 맞는 연장이 보인다
        if (x.User is not CrewMember fixer || fixer.Pose != Pose.Working) return;
        var hatch = new Rect2(e - new Vector2(x.Px(5f), x.Px(4f)), new Vector2(x.Px(10f), x.Px(8f)));
        ci.Box(hatch, new Color(0.02f, 0.02f, 0.03f, 0.75f));
        Line(ci, hatch.Position, new Vector2(hatch.End.X, hatch.Position.Y), Steel4.WithAlpha(0.8f), 1f);
        // 연 덮개가 옆으로 젖혀졌다
        var lid = new Rect2(new Vector2(hatch.End.X + 1f, hatch.Position.Y - 1f), new Vector2(x.Px(3f), hatch.Size.Y + 2f));
        ci.Box(lid, Steel3);
        if (x.Lod == 0) return;
        float t = x.T;
        switch (a.Fault)
        {
            case Look.Sparks:
            case Look.Heat:
            case Look.Smoke:
            {
                // 용접 · 납땜 불빛: 푸른 흰 깜빡임과 튀는 불똥
                float on = Hash(x.Id, (int)(t * 18f), 233) > 0.35f ? 1f : 0.2f;
                Dot(ci, hatch.GetCenter(), x.Px(6f), Arc.WithAlpha(0.18f * on));
                Dot(ci, hatch.GetCenter(), x.Px(1.6f), new Color(1, 1, 1, 0.95f * on));
                for (int i = 0; i < 3; i++)
                {
                    float ph = Mathf.PosMod(t * 2.4f + i / 3f, 1f);
                    var d = Vector2.FromAngle(Mathf.Pi * (0.15f + 0.7f * Hash(x.Id, i + (int)(t * 2.4f) * 3, 234)));
                    Dot(ci, hatch.GetCenter() + d * x.Px(3f + 9f * ph) + new Vector2(0f, ph * ph * x.Px(6f)), 0.8f, SparkHot.WithAlpha(1f - ph));
                }
                break;
            }
            case Look.Leak:
            case Look.Steam:
            case Look.Gas:
            {
                // 실링폼: 이음매를 따라 부풀어 오르는 흰 거품 방울
                float grow = Mathf.PosMod(t * 0.3f, 1f);
                for (int i = 0; i < 5; i++)
                    Dot(ci, hatch.Position + new Vector2(hatch.Size.X * (0.12f + 0.19f * i), hatch.Size.Y * 0.55f), x.Px(1f + 1.4f * Mathf.Clamp(grow * 5f - i, 0f, 1f)), Foam.WithAlpha(0.9f));
                Line(ci, hatch.End + new Vector2(1f, 1f), hatch.End + new Vector2(x.Px(6f), x.Px(4f)), WarnYellow, 2f); // 실링폼 통 꼭지
                break;
            }
            case Look.Grind:
            case Look.Jam:
            {
                // 렌치: 반 바퀴씩 앞뒤로 비튼다
                float ang = -0.5f + 0.9f * Mathf.Abs(Mathf.Sin(t * 3f));
                var pivot = hatch.GetCenter();
                var tip = pivot + Vector2.FromAngle(ang) * x.Px(10f);
                Line(ci, pivot, tip, Chrome, x.Px(1.8f));
                ci.Arc(pivot, x.Px(2.2f), ang + 0.6f, ang + Mathf.Tau - 0.6f, 10, Chrome, x.Px(1.2f), true);
                break;
            }
            case Look.Flicker:
            {
                // 진단 탐침: 집게 두 개와 번갈아 깜빡이는 초록 · 빨강 불
                var c0 = hatch.GetCenter();
                Line(ci, c0 + new Vector2(-x.Px(2f), 0f), c0 + new Vector2(-x.Px(7f), x.Px(9f)), new Color("#c03030"), 1f);
                Line(ci, c0 + new Vector2(x.Px(2f), 0f), c0 + new Vector2(x.Px(4f), x.Px(10f)), new Color("#202020"), 1f);
                bool tick = Mathf.PosMod(t * 2f, 1f) < 0.5f;
                Led(ci, c0 + new Vector2(-x.Px(7f), x.Px(10f)), tick ? Good : Danger, 0.9f, 1.3f);
                break;
            }
        }
    }
}
