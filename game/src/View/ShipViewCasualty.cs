using System;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.24 큰 상처 뒤 그림 — 저마다 다르게:
///   출혈: 몸 밑에 번지는 검붉은 웅덩이 (흐른 시간만큼 넓게 · 빠를수록 짙게) + 떨어지는 방울 · 누르고 있으면 흰 거즈 뭉치
///   화상 쇼크: 덴 쪽에 일렁이는 열기 물결 (주황) · 식혀 감쌌으면 엷은 하늘색 젖은 천
///   심정지: 가슴 위 끊긴 심전도 선 (평평한 줄이 깜빡) · 곁에서 누르면 박자에 맞춰 퍼지는 고리
/// 그리기는 시뮬레이션 상태를 읽기만 한다.
/// </summary>
public partial class ShipView
{
    private void PaintTrauma(CanvasItem ci, CrewMember c, Vector2 p, float s)
    {
        if (_world.Casualty.Open.Count == 0 || c.Dead) return;
        var t = _world.Casualty.Of(c);
        if (t == null) return;
        float mins = (_world.Tick - t.Since) / (float)SimTime.TicksPerHour * 60f;
        var at = c.CarriedBy is CrewMember carrier ? CrewPx(carrier) : p;
        switch (t.Kind)
        {
            case TraumaKind.Bleed:
            {
                // 웅덩이: 겹친 타원 셋 (불규칙한 가장자리) — 흐른 시간만큼 넓어진다
                float r = Mathf.Clamp(4f + 0.35f * mins * (0.5f + t.Rate * 3f), 4f, 15f) * s;
                var dark = new Color("#5a0d14").WithAlpha(0.75f);
                var wet = new Color("#8e1a24").WithAlpha(0.55f);
                ci.DrawSetTransform(at + new Vector2(2f, 6f) * s, 0.3f, new Vector2(1.4f, 0.75f));
                ci.Circle(Vector2.Zero, r, dark, true, -1f, true);
                ci.Circle(new Vector2(r * 0.45f, -r * 0.2f), r * 0.6f, wet, true, -1f, true);
                ci.Circle(new Vector2(-r * 0.5f, r * 0.15f), r * 0.45f, dark, true, -1f, true);
                ci.Arc(new Vector2(-r * 0.2f, -r * 0.3f), r * 0.35f, 3.6f, 4.6f, 6, new Color("#ff9aa0").WithAlpha(0.35f), 1f, true); // 젖은 빛
                ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                // 떨어지는 방울 (빠를수록 자주)
                float period = Mathf.Clamp(1.4f - t.Rate * 3f, 0.35f, 1.4f);
                float ph = (_time + c.Id * 0.37f) % period / period;
                var drop = at + new Vector2(5f * s, -2f * s + ph * 9f * s);
                ci.Circle(drop, 1.3f * s, new Color("#a3121f").WithAlpha(1f - ph), true, -1f, true);
                // 누르고 있다: 흰 거즈 뭉치 (스스로 · 곁의 사람)
                if (t.SelfPressed || t.Helper >= 0)
                {
                    var g = at + new Vector2(4f, 1f) * s;
                    ci.Box(new Rect2(g.X - 2.5f * s, g.Y - 2f * s, 5f * s, 4f * s), new Color("#f2efe6"));
                    ci.DrawLine(g + new Vector2(-2.5f, 0f) * s, g + new Vector2(2.5f, 0f) * s, new Color("#c74a52").WithAlpha(0.8f), 1f, true);
                }
                break;
            }
            case TraumaKind.BurnShock:
            {
                // 열기 물결: 위로 일렁이는 주황 호 셋
                for (int i = 0; i < 3; i++)
                {
                    float ph = (_time * 0.8f + i / 3f + c.Id * 0.13f) % 1f;
                    var o = at + new Vector2((i - 1) * 4f * s + Mathf.Sin(_time * 3f + i) * 1.5f * s, -4f * s - ph * 10f * s);
                    ci.Arc(o, 3f * s, Mathf.Pi * 1.1f, Mathf.Pi * 1.9f, 6, new Color("#ff8a3d").WithAlpha(0.55f * (1f - ph)), 1.2f, true);
                }
                // 덴 자리: 붉게 부은 얼룩
                ci.Circle(at + new Vector2(-3f, 2f) * s, 3.2f * s, new Color("#d9573a").WithAlpha(0.5f), true, -1f, true);
                if (t.Helper >= 0) ci.Box(new Rect2(at.X - 5f * s, at.Y, 8f * s, 3f * s), new Color("#bfe3f2").WithAlpha(0.8f)); // 젖은 천
                break;
            }
            case TraumaKind.Arrest:
            {
                // 끊긴 심전도: 평평한 줄 (깜빡) — 곁에서 누르면 줄이 박자에 맞춰 튄다
                var o = at + new Vector2(-9f, -15f) * s;
                bool cpr = t.Helper >= 0;
                float beat = cpr ? Mathf.Max(0f, Mathf.Sin(_time * 12f)) : 0f;
                var line = cpr ? new Color("#ffe27a") : Palette.Danger.WithAlpha(0.5f + 0.5f * Mathf.Abs(Mathf.Sin(_time * 2.5f)));
                ci.Box(new Rect2(o.X - 2f, o.Y - 5f * s, 22f * s, 10f * s), new Color("#10141b").WithAlpha(0.75f));
                ci.DrawLine(o + new Vector2(0f, 0f), o + new Vector2(7f * s, 0f), line, 1.4f, true);
                ci.DrawLine(o + new Vector2(7f * s, 0f), o + new Vector2(9f * s, -4f * s * beat), line, 1.4f, true);
                ci.DrawLine(o + new Vector2(9f * s, -4f * s * beat), o + new Vector2(11f * s, 3f * s * beat), line, 1.4f, true);
                ci.DrawLine(o + new Vector2(11f * s, 3f * s * beat), o + new Vector2(18f * s, 0f), line, 1.4f, true);
                if (cpr) ci.Arc(at, (8f + 6f * beat) * s, 0f, Mathf.Tau, 20, new Color("#ffe27a").WithAlpha(0.45f * (1f - beat * 0.5f)), 1.3f, true); // 누르는 박자
                break;
            }
        }
    }
}
