using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v18.7 블랙박스 · 숨은 실수 · 사고 조사의 그림 (읽기만):
// 상자 — 벽에 볼트로 박은 주황 상자(반사 띠 두 줄 · 모서리 볼트 넷 · 옆구리 위치 신호기 · 받침 쇠) · 받아 적을 때 빨간 불이 깜박인다 ·
//   지운 흔적이 있으면 봉인 딱지가 찢겨 있고 노란 불이 두 번씩 깜박인다 · 그을리면 검댕과 금 · 칩이 타면 뚜껑이 들리고 녹은 칩에서 연기 · 새 칩은 초록 단자가 반짝인다.
// 설비 곁의 숨은 실수 — 안에 두고 온 렌치 손잡이(사고 뒤엔 그을린 렌치) · 반대로 돌아간 밸브 손잡이와 어긋난 화살표 ·
//   깨진 부품 조각과 규격 꼬리표 · 테이프로 가린 경고등 · 한 칸이 빈 점검표.
// 사람 — 숨긴 사람이 그 설비 방에 들어서면 머리 위 작은 생각 구름(그 실수의 그림) · 몰래 치우는 손전등 원뿔 · 단말 앞의 붉은 화면 줄.
// 사고 조사 자리 — 탁자 위 상자와 떠 있는 기록 띠(시각 눈금 · 정비 · 경보 · 위치 점 · 지운 자리는 빗금과 붉은 괄호 · 칩이 타면 지지직).
public partial class ShipView
{
    private static readonly Color BoxOrange = new(0.98f, 0.45f, 0.08f);

    private void PaintBlackbox(CanvasItem ci)
    {
        var w = _world;
        if (BlackboxSystem.Off) return;
        var bx = w.Blackbox;
        if (bx.Present && bx.Room is Room br && !br.Detached) DrawRecorder(ci, CellRect(bx.At).Position + new Vector2(T * 0.5f, T * 0.3f), 1f, bx, w);
        if (InquirySystem.Off) return;
        foreach (var s in w.Inquiry.Slips)
        {
            if (s.Fixed || s.Settled >= 0 && w.Tick - s.Settled > SimTime.TicksPerDay) continue;
            if (w.Ship.Furniture.FirstOrDefault(f => f.Id == s.Machine) is not Furniture f || f.Room.Detached) continue;
            DrawSlipMark(ci, FurnitureRect(f), s);
        }
        PaintInquirySitting(ci);
        PaintCoverPeople(ci);
    }

    /// <summary>벽에 박은 주황 상자 (상태마다 모양이 다르다).</summary>
    private void DrawRecorder(CanvasItem ci, Vector2 c, float k, BlackboxSystem bx, World w)
    {
        float wd = T * 0.56f * k, ht = T * 0.4f * k;
        var r = new Rect2(c - new Vector2(wd / 2, ht / 2), wd, ht);
        bool wreck = bx.Wrecked;
        float hp = bx.Health;
        var body = wreck ? new Color(0.32f, 0.17f, 0.08f) : BoxOrange.Lerp(new Color(0.35f, 0.2f, 0.1f), Mathf.Clamp(1f - hp, 0f, 0.6f));
        // 받침 쇠 (벽에 닿는 쪽)
        ci.Box(new Rect2(r.Position + new Vector2(-2f * k, ht * 0.25f), new Vector2(wd + 4f * k, ht * 0.5f)), new Color(0.4f, 0.42f, 0.45f));
        ci.Box(new Rect2(r.Position + new Vector2(1f, 2f), r.Size), new Color(0, 0, 0, 0.3f));
        ci.Box(r, body);
        // 반사 띠 두 줄 (비스듬히)
        var band = wreck ? new Color(0.45f, 0.42f, 0.38f, 0.5f) : new Color(0.95f, 0.95f, 0.9f, 0.85f);
        for (int i = 0; i < 2; i++)
        {
            float x0 = r.Position.X + wd * (0.18f + i * 0.36f);
            ci.Poly(new[]
            {
                new Vector2(x0, r.Position.Y), new Vector2(x0 + wd * 0.12f, r.Position.Y),
                new Vector2(x0 + wd * 0.02f, r.End.Y), new Vector2(x0 - wd * 0.1f, r.End.Y),
            }, band);
        }
        ci.Box(r, new Color(0.15f, 0.08f, 0.03f), false, 1.2f);
        // 볼트 넷
        foreach (var q in new[] { r.Position + new Vector2(2.5f, 2.5f) * k, new Vector2(r.End.X - 2.5f * k, r.Position.Y + 2.5f * k), new Vector2(r.Position.X + 2.5f * k, r.End.Y - 2.5f * k), r.End - new Vector2(2.5f, 2.5f) * k })
        {
            ci.Circle(q, 1.4f * k, new Color(0.25f, 0.25f, 0.27f));
            ci.DrawLine(q - new Vector2(0.9f, 0) * k, q + new Vector2(0.9f, 0) * k, new Color(0.6f, 0.6f, 0.62f), 0.6f);
        }
        // 옆구리 위치 신호기 (작은 원통 · 고리)
        var bc = new Vector2(r.End.X + 3.2f * k, c.Y);
        ci.Box(new Rect2(bc - new Vector2(2.2f, 4.5f) * k, new Vector2(4.4f, 9f) * k), new Color(0.2f, 0.22f, 0.25f));
        ci.Circle(bc + new Vector2(0, -4.5f * k), 2.2f * k, new Color(0.3f, 0.32f, 0.35f));
        ci.Arc(bc + new Vector2(0, 5.5f * k), 1.6f * k, 0f, Mathf.Tau, 10, new Color(0.7f, 0.7f, 0.72f), 0.8f);
        var led = new Vector2(r.Position.X + wd * 0.84f, r.Position.Y + ht * 0.22f);
        if (wreck)
        {
            // 들린 뚜껑 · 녹은 칩 · 금 · 연기
            ci.Poly(new[] { r.Position, r.Position + new Vector2(wd * 0.7f, -ht * 0.35f), r.Position + new Vector2(wd * 0.78f, -ht * 0.2f), r.Position + new Vector2(wd * 0.1f, ht * 0.12f) }, new Color(0.25f, 0.13f, 0.06f));
            ci.Circle(c + new Vector2(-wd * 0.1f, 0), ht * 0.18f, new Color(0.35f, 0.35f, 0.33f));
            ci.DrawLine(r.Position + new Vector2(wd * 0.3f, ht), r.Position + new Vector2(wd * 0.5f, ht * 0.4f), new Color(0.05f, 0.03f, 0.02f), 1f);
            ci.DrawLine(r.Position + new Vector2(wd * 0.5f, ht * 0.4f), r.Position + new Vector2(wd * 0.45f, ht * 0.1f), new Color(0.05f, 0.03f, 0.02f), 1f);
            for (int i = 0; i < 3; i++)
            {
                float ph = (_time * 0.5f + i * 0.33f) % 1f;
                ci.Circle(c + new Vector2(-wd * 0.1f + Mathf.Sin(_time + i) * 2f, -ht * 0.4f - ph * T * 0.5f), (1.5f + ph * 3f) * k, new Color(0.3f, 0.3f, 0.3f, 0.35f * (1f - ph)));
            }
            return;
        }
        if (hp < 0.6f)
        {
            // 그을음 · 금
            ci.Poly(Ellipse(c + new Vector2(wd * 0.15f, ht * 0.1f), wd * 0.3f, ht * 0.35f), new Color(0.1f, 0.06f, 0.03f, 0.55f));
            ci.DrawLine(r.Position + new Vector2(wd * 0.6f, 0), r.Position + new Vector2(wd * 0.52f, ht * 0.6f), new Color(0.08f, 0.04f, 0.02f), 0.9f);
        }
        // 받아 적는 불 (방금 적었으면 빨리) · 지운 흔적이 있으면 노란 불이 두 번씩
        bool fresh = w.Tick - bx.LastWrite < SimTime.Minutes(2);
        bool blink = Mathf.Sin(_time * (fresh ? 9f : 3f)) > 0.2f;
        ci.Circle(led, 1.6f * k, blink ? new Color(1f, 0.15f, 0.1f) : new Color(0.35f, 0.05f, 0.04f));
        if (blink) ci.Circle(led, 3.2f * k, new Color(1f, 0.2f, 0.1f, 0.25f));
        var wipe = bx.Wipes.LastOrDefault(g => g.At >= bx.FreshSince && w.Tick - g.At < SimTime.TicksPerDay * 2);
        if (wipe != null)
        {
            // 찢긴 봉인 딱지 (지운 흔적) · 노란 불 두 번씩
            var seal = new Vector2(r.Position.X + wd * 0.18f, r.End.Y - ht * 0.28f);
            ci.Box(new Rect2(seal - new Vector2(3.5f, 2f) * k, new Vector2(3.2f, 4f) * k), new Color(0.85f, 0.85f, 0.8f));
            ci.Box(new Rect2(seal + new Vector2(0.6f, -1.6f) * k, new Vector2(3f, 3.6f) * k), new Color(0.85f, 0.85f, 0.8f));
            ci.Circle(seal + new Vector2(-1.8f, 0) * k, 1.1f * k, new Color(0.75f, 0.1f, 0.1f));
            float ph = (_time * 1.3f) % 1f;
            if (ph < 0.1f || ph is > 0.2f and < 0.3f) ci.Circle(led + new Vector2(-4f * k, 0), 1.4f * k, new Color(1f, 0.85f, 0.1f));
        }
        if (bx.Repairer >= 0 && w.Tick - bx.FreshSince < SimTime.TicksPerDay)
        {
            // 새 칩: 초록 단자 반짝
            var chip = new Vector2(r.Position.X + wd * 0.62f, r.End.Y - ht * 0.3f);
            ci.Box(new Rect2(chip - new Vector2(2.5f, 1.5f) * k, new Vector2(5f, 3f) * k), new Color(0.1f, 0.45f, 0.2f));
            if (Mathf.Sin(_time * 2f) > 0.7f) ci.DrawLine(chip + new Vector2(-2f, -2.5f) * k, chip + new Vector2(2f, -2.5f) * k, new Color(1, 1, 1, 0.8f), 0.8f);
        }
    }

    /// <summary>설비 곁의 숨은 실수 (실수마다 다른 그림 · 사고 전엔 은근히 · 사고 뒤엔 그을리거나 깨진 채).</summary>
    private void DrawSlipMark(CanvasItem ci, Rect2 fr, Slip s)
    {
        bool bit = s.Bit;
        var p = new Vector2(fr.End.X - 4f, fr.Position.Y + 5f);
        switch (s.Kind)
        {
            case SlipKind.ToolLeft when s.TraceLeft:
            {
                // 뚜껑 틈으로 삐져나온 렌치 손잡이 (사고 뒤엔 그을린 렌치)
                var steel = bit ? new Color(0.18f, 0.16f, 0.14f) : new Color(0.72f, 0.74f, 0.78f);
                var a = p + new Vector2(-2f, 2f); var b = p + new Vector2(6f, -5f);
                ci.DrawLine(a, b, steel, 2.2f, true);
                ci.Arc(b, 2.6f, -2.2f, 1.6f, 8, steel, 1.6f, true);
                ci.Circle(a, 1.4f, bit ? new Color(0.3f, 0.12f, 0.05f) : new Color(0.75f, 0.2f, 0.15f));
                if (!bit && Mathf.Sin(_time * 1.7f + s.Id) > 0.92f) ci.DrawLine(b + new Vector2(-2, -2), b + new Vector2(2, 2), new Color(1, 1, 1, 0.9f), 1f);
                if (bit) ci.Circle(p, 4f, new Color(0.08f, 0.05f, 0.03f, 0.35f));
                break;
            }
            case SlipKind.WrongValve when s.TraceLeft:
            {
                // 손잡이 바퀴(바큇살 셋) · 어긋난 화살표 (돌린 방향 · 맞는 방향)
                var vc = new Vector2(fr.Position.X + 5f, fr.End.Y - 5f);
                ci.Arc(vc, 3.6f, 0f, Mathf.Tau, 14, new Color(0.25f, 0.45f, 0.85f), 1.4f, true);
                for (int i = 0; i < 3; i++) { float ang = i * Mathf.Tau / 3f + (bit ? 0.6f : 0f); ci.DrawLine(vc, vc + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 3.6f, new Color(0.25f, 0.45f, 0.85f), 1f); }
                ci.Arc(vc, 6f, -0.4f, 1.1f, 8, new Color(0.9f, 0.2f, 0.15f, 0.9f), 1f, true);
                ci.DrawLine(vc + new Vector2(6f, 0) .Rotated(1.1f), vc + new Vector2(6f, 0).Rotated(1.1f) + new Vector2(-2f, -1f), new Color(0.9f, 0.2f, 0.15f), 1f);
                ci.Arc(vc, 6f, Mathf.Pi + 0.4f, Mathf.Pi + 1.5f, 8, new Color(0.3f, 0.85f, 0.4f, 0.5f), 1f, true);
                break;
            }
            case SlipKind.WrongPart when s.TraceLeft:
            {
                if (!bit)
                {
                    // 끼운 부품의 꼬리표 (글씨 줄 색이 다르다)
                    ci.Box(new Rect2(p + new Vector2(-4f, 0), new Vector2(5f, 3.5f)), new Color(0.95f, 0.85f, 0.4f));
                    ci.DrawLine(p + new Vector2(-3.5f, 1.8f), p + new Vector2(0f, 1.8f), new Color(0.8f, 0.2f, 0.15f), 0.8f);
                    break;
                }
                // 깨진 부품 조각 (흩어진 세모) · 규격 꼬리표
                for (int i = 0; i < 5; i++)
                {
                    float ang = i * 1.3f + s.Id;
                    var q = fr.GetCenter() + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (fr.Size.X * 0.45f + 3f + i * 1.5f);
                    ci.Poly(new[] { q, q + new Vector2(2.5f, 0.6f).Rotated(ang), q + new Vector2(0.6f, 2.2f).Rotated(ang) }, new Color(0.62f, 0.64f, 0.68f));
                }
                ci.Box(new Rect2(p + new Vector2(-4f, 0), new Vector2(5f, 3.5f)), new Color(0.95f, 0.85f, 0.4f, 0.8f));
                break;
            }
            case SlipKind.SilencedAlarm:
            {
                // 테이프로 가린 경고등 (X 자 테이프 · 사고 뒤엔 빛이 새지 않는다)
                var lc = new Vector2(fr.Position.X + fr.Size.X * 0.5f, fr.Position.Y + 3f);
                ci.Circle(lc, 2.6f, bit ? new Color(0.2f, 0.1f, 0.05f) : new Color(0.55f, 0.35f, 0.05f));
                if (!bit && Mathf.Sin(_time * 4f + s.Id) > 0.6f) ci.Circle(lc, 4.5f, new Color(1f, 0.6f, 0.1f, 0.12f));
                ci.DrawLine(lc + new Vector2(-3.5f, -2f), lc + new Vector2(3.5f, 2f), new Color(0.85f, 0.82f, 0.7f), 1.6f);
                ci.DrawLine(lc + new Vector2(-3.5f, 2f), lc + new Vector2(3.5f, -2f), new Color(0.85f, 0.82f, 0.7f), 1.6f);
                break;
            }
            case SlipKind.SkippedStep:
            {
                // 점검표 (한 칸만 비었다)
                var cp = new Vector2(fr.Position.X + 2f, fr.Position.Y + 2f);
                ci.Box(new Rect2(cp, new Vector2(6f, 8f)), new Color(0.9f, 0.88f, 0.8f));
                ci.Box(new Rect2(cp + new Vector2(1.8f, -1f), new Vector2(2.4f, 1.4f)), new Color(0.4f, 0.4f, 0.42f));
                for (int i = 0; i < 4; i++)
                {
                    var row = cp + new Vector2(1f, 2f + i * 1.6f);
                    if (i == 2) ci.Box(new Rect2(row, new Vector2(1.2f, 1.1f)), new Color(0.75f, 0.2f, 0.15f), false, 0.6f);
                    else ci.DrawLine(row + new Vector2(0, 0.6f), row + new Vector2(1.2f, 0.6f), new Color(0.2f, 0.55f, 0.25f), 0.7f);
                    ci.DrawLine(row + new Vector2(2f, 0.6f), row + new Vector2(4.5f, 0.6f), new Color(0.45f, 0.45f, 0.45f), 0.5f);
                }
                break;
            }
        }
    }

    /// <summary>사고 조사 자리: 탁자 위 상자와 떠 있는 기록 띠.</summary>
    private void PaintInquirySitting(CanvasItem ci)
    {
        var w = _world;
        if (w.Motions.Now is not Sitting sit || sit.Kind != SittingKind.Inquiry || sit.Opened < 0) return;
        var cs = w.Inquiry.Cases.FirstOrDefault(k => k.Motion == sit.Motion.Id);
        if (cs == null) return;
        var bx = w.Blackbox;
        var s = w.Inquiry.Get(cs.Slip);
        var mid = ToPx(sit.Venue.Center);
        // 탁자 위 상자 (작게)
        DrawRecorder(ci, mid + new Vector2(0, T * 0.15f), 0.7f, bx, w);
        // 기록 띠 (사고 앞 한 시간 ~ 사고 뒤 10분)
        long from = (s?.Tick ?? cs.Start) - SimTime.Hours(1), to = (s?.BitAt ?? cs.Start) + SimTime.Minutes(10);
        float len = T * 3.2f, y = mid.Y - T * 0.75f, x0 = mid.X - len / 2;
        float X(long t) => x0 + len * Mathf.Clamp((t - from) / (float)Math.Max(1, to - from), 0f, 1f);
        bool dead = bx.Wrecked || !bx.Present;
        float flick = dead ? (Mathf.Sin(_time * 23f) > 0.3f ? 0.25f : 0.6f) : 0.8f + 0.1f * Mathf.Sin(_time * 2f);
        var holo = new Color(0.35f, 0.85f, 1f, 0.35f * flick);
        ci.Box(new Rect2(x0, y - 4f, len, 8f), holo);
        ci.Box(new Rect2(x0, y - 4f, len, 8f), new Color(0.5f, 0.9f, 1f, 0.7f * flick), false, 1f);
        for (long t = from - from % SimTime.Minutes(15) + SimTime.Minutes(15); t < to; t += SimTime.Minutes(15))
            ci.DrawLine(new Vector2(X(t), y + 4f), new Vector2(X(t), y + (t % SimTime.Hours(1) == 0 ? 8f : 6f)), new Color(0.5f, 0.9f, 1f, 0.6f * flick), 0.8f);
        if (dead)
        {
            // 지지직 (읽을 수 없는 기록)
            for (int i = 0; i < 9; i++)
            {
                float jx = x0 + ((i * 37 + (int)(_time * 20f) * 13) % 97) / 97f * len;
                ci.DrawLine(new Vector2(jx, y - 3f), new Vector2(jx + 4f, y + 3f), new Color(0.8f, 0.9f, 1f, 0.5f), 0.8f);
            }
            return;
        }
        var rec = bx.Read(from, to, e => e.Kind != BoxKind.Door && (e.Kind != BoxKind.Place || cs.Rooms.Contains(e.Room)));
        if (rec != null)
            foreach (var e in rec)
            {
                var col = e.Kind switch
                {
                    BoxKind.Work => new Color(1f, 0.85f, 0.25f), BoxKind.Alarm => new Color(1f, 0.25f, 0.2f), BoxKind.Silence => new Color(1f, 0.55f, 0.1f),
                    BoxKind.Valve => new Color(0.3f, 0.5f, 1f), BoxKind.Place => new Color(0.6f, 0.95f, 1f, 0.6f), _ => new Color(0.7f, 0.8f, 0.9f, 0.5f),
                };
                float h = e.Kind is BoxKind.Work or BoxKind.Alarm or BoxKind.Silence ? 7f : 3.5f;
                ci.DrawLine(new Vector2(X(e.Tick), y - h), new Vector2(X(e.Tick), y + h * 0.4f), col, e.Kind == BoxKind.Place ? 0.8f : 1.6f);
            }
        // 지운 자리: 빗금 · 붉은 괄호 (지운 시각에 작은 칼자국)
        foreach (var g in bx.GapsIn(from, to))
        {
            float a = X(g.From), b = X(g.To);
            ci.Box(new Rect2(a, y - 4f, b - a, 8f), new Color(0.05f, 0.06f, 0.08f, 0.85f));
            for (float hx = a; hx < b; hx += 3f) ci.DrawLine(new Vector2(hx, y + 4f), new Vector2(Mathf.Min(b, hx + 3f), y - 4f), new Color(0.9f, 0.3f, 0.25f, 0.5f), 0.7f);
            var red = new Color(1f, 0.25f, 0.2f, 0.95f);
            ci.Polyline(new[] { new Vector2(a + 2f, y - 6f), new Vector2(a, y - 6f), new Vector2(a, y + 6f), new Vector2(a + 2f, y + 6f) }, red, 1.2f);
            ci.Polyline(new[] { new Vector2(b - 2f, y - 6f), new Vector2(b, y - 6f), new Vector2(b, y + 6f), new Vector2(b - 2f, y + 6f) }, red, 1.2f);
        }
        // 사고 시각 (붉은 쐐기)
        float bxp = X(s?.BitAt ?? cs.Start);
        ci.Poly(new[] { new Vector2(bxp, y - 6f), new Vector2(bxp - 3f, y - 11f), new Vector2(bxp + 3f, y - 11f) }, new Color(1f, 0.3f, 0.2f, 0.9f));
    }

    /// <summary>숨긴 사람의 몸짓: 그 설비 방에 들면 생각 구름 · 몰래 치우는 손전등 · 단말 앞의 붉은 화면.</summary>
    private void PaintCoverPeople(CanvasItem ci)
    {
        var w = _world;
        foreach (var s in w.Inquiry.Slips)
        {
            if (!s.Open || !s.Hidden) continue;
            var c = w.Crew.FirstOrDefault(x => x.Id == s.Who);
            if (c == null || c.Dead || c.Room == null) continue;
            var p = CrewPx(c);
            float r = CrewRadius;
            if (c.Job?.Activity is CoverActivity && w.Inquiry.TaskOf(c) is CoverTask t)
            {
                if (t.Kind == CoverTaskKind.Tidy && c.Pose == Pose.Working)
                {
                    var dir = (ToPx(t.At.Center) - p);
                    var d = dir.Length() > 1f ? dir.Normalized() : new Vector2(1, 0);
                    ci.Poly(new[] { p + d * r * 0.6f, p + d * r * 3f + d.Orthogonal() * r * 1.1f, p + d * r * 3f - d.Orthogonal() * r * 1.1f }, new Color(1f, 0.95f, 0.6f, 0.22f));
                    ci.Circle(p + d * r * 0.6f, 1.6f, new Color(1f, 0.95f, 0.7f));
                }
                else if (t.Kind == CoverTaskKind.Wipe && c.Pose == Pose.Working)
                {
                    var sc = p + new Vector2(r * 1.2f, -r * 0.9f);
                    ci.Box(new Rect2(sc, new Vector2(r * 1.3f, r * 0.9f)), new Color(0.08f, 0.1f, 0.14f, 0.9f));
                    int off = (int)(_time * 6f) % 4;
                    for (int i = 0; i < 4; i++)
                        ci.DrawLine(sc + new Vector2(2f, 2f + ((i + off) % 4) * r * 0.2f), sc + new Vector2(r * (i == 1 ? 0.6f : 1.1f), 2f + ((i + off) % 4) * r * 0.2f), i == 2 ? new Color(1f, 0.3f, 0.25f) : new Color(0.5f, 0.9f, 1f, 0.7f), 0.8f);
                }
                continue;
            }
            if (c.Room.Id != s.RoomId || !c.IsAwake) continue;
            // 생각 구름 (작은 거품 둘 · 구름 · 그 실수의 그림)
            var cc = p + new Vector2(r * 1.1f, -r * 2.1f);
            var fog = new Color(0.82f, 0.84f, 0.88f, 0.55f);
            ci.Circle(p + new Vector2(r * 0.5f, -r * 1.1f), 1.2f, fog);
            ci.Circle(p + new Vector2(r * 0.8f, -r * 1.5f), 1.8f, fog);
            ci.Poly(Ellipse(cc, r * 0.9f, r * 0.6f), fog);
            var ink = new Color(0.25f, 0.27f, 0.32f, 0.85f);
            switch (s.Kind)
            {
                case SlipKind.ToolLeft: ci.DrawLine(cc + new Vector2(-3f, 2f), cc + new Vector2(3f, -2f), ink, 1.4f); ci.Arc(cc + new Vector2(3f, -2f), 1.6f, -2.2f, 1.6f, 6, ink, 1f); break;
                case SlipKind.WrongValve: ci.Arc(cc, 2.6f, 0f, Mathf.Tau, 10, ink, 1f); ci.DrawLine(cc, cc + new Vector2(2.6f, 0), ink, 0.8f); break;
                case SlipKind.WrongPart: ci.Poly(new[] { cc + new Vector2(-2.5f, 2f), cc + new Vector2(2.5f, 1.5f), cc + new Vector2(0, -2.5f) }, ink); break;
                case SlipKind.SilencedAlarm: ci.Circle(cc, 2f, new Color(0.9f, 0.55f, 0.1f, 0.8f)); ci.DrawLine(cc + new Vector2(-2.5f, -2f), cc + new Vector2(2.5f, 2f), ink, 1f); break;
                default: ci.Box(new Rect2(cc - new Vector2(2f, 2.5f), new Vector2(4f, 5f)), ink, false, 0.8f); ci.DrawLine(cc + new Vector2(-1f, 0), cc + new Vector2(1f, 0), new Color(0.8f, 0.2f, 0.15f), 0.8f); break;
            }
        }
    }
}
