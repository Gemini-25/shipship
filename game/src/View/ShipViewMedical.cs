using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// 의료 1차 — 사람 곁의 그림 (저마다 다르게):
///   수혈 중: 곁에 링거 봉 + 붉은 혈액 주머니 · 방울이 줄을 타고 내려간다
///   수술 뒤 이틀: 가슴 · 배에 흰 붕대 십자 (머리 수술이면 머리에 감은 붕대)
///   손이 굳음: 손에 감은 흰 압박 붕대 · 다리 절음: 곁에 짚은 지팡이 (걸을 때 짚는 박자에 맞춰 기운다)
///   헌혈한 날: 팔꿈치 안쪽 솜과 붉은 점 · 진통제 의존: 손이 가늘게 떨린다
/// 그리기는 시뮬레이션 상태를 읽기만 한다.
/// </summary>
public partial class ShipView
{
    private void PaintMedical(CanvasItem ci, CrewMember c, Vector2 p, float s)
    {
        if (c.Dead) return;
        var w = _world;
        float t = (float)(w.Tick % 100000) / SimTime.TicksPerSecond;
        // 수혈 중 (방금 피를 받았다 — 사십 분)
        long given = w.Blood.GivenAt(c);
        if (w.Tick - given < SimTime.Minutes(40))
        {
            var pole = p + new Vector2(10f, -12f) * s;
            var steel = new Color("#a8b2c0");
            ci.DrawLine(pole, pole + new Vector2(0f, 16f) * s, steel, 1.2f * s, true);
            ci.DrawLine(pole + new Vector2(-3f, 16f) * s, pole + new Vector2(3f, 16f) * s, steel, 1.2f * s, true);
            var bag = new Rect2(pole + new Vector2(-2.5f, -1f) * s, new Vector2(5f, 6f) * s);
            Gfx.RoundRect(ci, bag, new Color("#a0141e"), 1.5f * s, new Color("#d8323c"), 1);
            var spout = new Vector2(bag.GetCenter().X, bag.End.Y);
            ci.DrawLine(spout, p + new Vector2(3f, -2f) * s, new Color(0.8f, 0.2f, 0.25f, 0.6f), 0.6f * s, true);
            float ph = Mathf.PosMod(t * 0.8f, 1f);
            ci.DrawCircle(spout + new Vector2(0f, ph * 5f * s), 0.7f * s, new Color(0.85f, 0.2f, 0.25f, 1f - ph));
        }
        // 수술 뒤 이틀: 붕대
        if (w.Recovery.PostOps.TryGetValue(c.Id, out var op) && w.Tick - op < SimTime.TicksPerDay * 2)
        {
            var white = new Color(0.97f, 0.97f, 0.95f, 0.9f);
            var last = w.Surgery.Done.FindLast(k => k.Patient == c.Id);
            if (last != null && last.Part == BodyPart.Head)
                ci.DrawArc(p + new Vector2(0f, -6f) * s, 4.2f * s, Mathf.Pi * 1.05f, Mathf.Pi * 1.95f, 10, white, 1.6f * s, true);
            else
            {
                ci.DrawLine(p + new Vector2(-2.5f, 1f) * s, p + new Vector2(2.5f, 1f) * s, white, 1.4f * s, true);
                ci.DrawLine(p + new Vector2(0f, -1.5f) * s, p + new Vector2(0f, 3.5f) * s, white, 1.4f * s, true);
            }
        }
        bool gave = w.Blood.LastGave.TryGetValue(c.Id, out var g) && w.Tick - g < SimTime.Hours(12);
        if (c.Ailments.Count == 0 && !gave) return;
        foreach (var a in c.Ailments)
        {
            if (a.Id == "stiffhand") // 손에 감은 압박 붕대
            {
                var hand = p + new Vector2(6f, 3f) * s;
                ci.DrawCircle(hand, 2f * s, new Color(0.95f, 0.95f, 0.92f, 0.95f));
                ci.DrawLine(hand + new Vector2(-2f, -0.5f) * s, hand + new Vector2(2f, 0.5f) * s, new Color("#c8c0b0"), 0.5f * s, true);
            }
            else if (a.Id == "limp") // 지팡이 — 걸을 때 짚는 박자
            {
                float lean = c.Pose == Pose.Walking ? 0.25f * Mathf.Sin(t * 5f) : 0.1f;
                var top = p + new Vector2(7f, -2f) * s;
                var wood = new Color("#6a4a2a");
                ci.DrawLine(top, top + new Vector2(2f + lean * 6f, 11f) * s, wood, 1.3f * s, true);
                ci.DrawArc(top + new Vector2(-1.5f, 0f) * s, 1.5f * s, Mathf.Pi, Mathf.Tau, 6, wood, 1.3f * s, true);
            }
            else if (a.Id == "pkdep" && c.Pose != Pose.Sleeping) // 손이 가늘게 떤다
            {
                float j = Mathf.Sin(t * 37f) * 0.6f;
                ci.DrawCircle(p + new Vector2(-6f + j, 3f) * s, 1.3f * s, Palette.Crew(c.Id).Lightened(0.3f).WithAlpha(0.7f));
            }
        }
        if (gave) // 헌혈한 팔: 솜 · 붉은 점
        {
            var arm = p + new Vector2(-5f, 0f) * s;
            ci.DrawCircle(arm, 1.4f * s, new Color(1f, 1f, 1f, 0.95f));
            ci.DrawCircle(arm, 0.5f * s, new Color("#c0202a"));
        }
    }
}
