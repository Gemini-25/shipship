using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v17.5 사고 뒤 며칠의 그림 — 모두 각자 다른 모양 · 움직임:
///   묵은 그을음 냄새(갈색 연기 고리가 천장 쪽으로 천천히 말려 오른다) · 다른 방에서 먹은 자리(쟁반 · 접시 · 숟가락) ·
///   널어 둔 침구(벽 고리 사이 줄 · 아래가 물결치는 천 · 젖으면 짙고 물방울 · 마르면 희고 흔들림 · 빨래집게) · 남은 빨랫줄 고리 ·
///   냉장고 문의 쪽지(테이프 · 줄글 · 버린 수만큼 빨간 X) · 빈 의자의 컵(김이 가끔) · 쓰러진 자리의 종이꽃 ·
///   다시 그린 그림(그을린 귀퉁이 · 새 붓자국 · 떠난 사람과 둘의 서명) · 옮겨 온 독서등(어두운 방의 노란 빛 웅덩이) ·
///   악몽에 뒤척이는 사람(이불 물결) · 놀라 깨 일어나 앉은 사람 · 안고 가는 침구 · 들고 가는 등.
/// 그리기는 시뮬레이션 상태를 읽기만 한다.
/// </summary>
public partial class ShipView
{
    private static readonly Color AfSoot = new("#5a4a3e");
    private static readonly Color AfSheetDry = new("#ece6d6");
    private static readonly Color AfSheetWet = new("#7d8fa3");
    private static readonly Color AfPaper = new("#f3efe2");
    private static readonly Color AfTape = new("#d9c27a");
    private static readonly Color AfRed = new("#d8423a");
    private static readonly Color AfWarm = new("#ffcf7a");
    private static readonly Color AfInk = new("#1a1c22");

    /// <summary>아래 층 (사람 아래): 냄새 · 쟁반 · 침구 · 쪽지 · 컵 · 꽃 · 그림 · 등불.</summary>
    private void PaintAfterUnder(CanvasItem ci)
    {
        var w = _world;
        var af = w.After;
        bool fine = Zoom > 1.15f;
        // 묵은 그을음 냄새: 방 몇 군데에서 갈색 고리가 천천히 말려 오른다
        foreach (var r in w.Ship.Rooms)
        {
            float s = af.StaleSoot(r);
            if (s < 0.05f || r.Detached || r.Cells.Count == 0) continue;
            for (int i = r.Id % 5; i < r.Cells.Count; i += 7)
            {
                var c = CellRect(r.Cells[i]).GetCenter();
                float ph = Mathf.PosMod(_time * 0.12f + i * 0.37f, 1f);
                var p = c + new Vector2(Mathf.Sin(_time * 0.5f + i) * 5f, -ph * 14f);
                float a = s * 0.35f * Mathf.Sin(ph * Mathf.Pi);
                ci.Arc(p, 3f + 4f * ph, ph * 4f, ph * 4f + 4.2f, 10, AfSoot.WithAlpha(a), 1.4f, true);
                ci.Arc(p + new Vector2(3f, -2f), 2f + 3f * ph, -ph * 3f, -ph * 3f + 3.5f, 8, AfSoot.WithAlpha(a * 0.7f), 1f, true);
            }
        }
        // 다른 방에서 먹은 자리: 쟁반
        foreach (var (tick, at, _) in af.AwayPlates)
        {
            float age = (w.Tick - tick) / (float)SimTime.TicksPerHour;
            if (age > 10f) continue;
            float a = Mathf.Clamp(1f - age / 10f, 0f, 1f) * 0.85f;
            var c = CellRect(at).GetCenter() + new Vector2(6f, 5f);
            var tray = new Rect2(c - new Vector2(6f, 4f), new Vector2(12f, 8f));
            Gfx.RoundRect(ci, tray, new Color(0.62f, 0.66f, 0.7f, a), 2f, new Color(0.3f, 0.32f, 0.36f, a));
            ci.Circle(c + new Vector2(-1.5f, 0f), 2.6f, new Color(0.95f, 0.95f, 0.92f, a));
            ci.Circle(c + new Vector2(-1.5f, 0f), 1.4f, new Color(0.78f, 0.6f, 0.35f, a * 0.8f));
            ci.DrawLine(c + new Vector2(3f, -2.5f), c + new Vector2(3.5f, 2.5f), new Color(0.82f, 0.84f, 0.86f, a), 0.9f, true);
        }
        // 빨랫줄 고리 (흔적) · 널어 둔 침구
        foreach (var t in af.Traces)
            if (t.Kind == AfterTraceKind.DryHooks && !t.Gone) AfHook(ci, t.At, 0.8f);
        foreach (var l in af.Lines)
        {
            if (l.Done) continue;
            AfHook(ci, l.Hook, 1f);
            AfSheet(ci, l, fine);
        }
        // 냉장고 쪽지
        foreach (var t in af.Traces)
        {
            if (t.Kind != AfterTraceKind.FridgeNote || t.Gone) continue;
            var fs = af.Fridges.LastOrDefault(f => f.Fridge == t.Item && f.Done == t.Tick);
            AfNote(ci, CellRect(t.At).GetCenter() + new Vector2(6f, -5f), fs?.Thrown ?? 1, fs?.Kept ?? 0, fine);
        }
        // 빈 의자: 컵
        foreach (var es in af.Seats)
        {
            if (!es.Active || !es.Cup) continue;
            var f = w.Ship.Furniture.FirstOrDefault(x => x.Id == es.Seat);
            if (f == null) continue;
            AfCup(ci, FurnitureRect(f).GetCenter() + new Vector2(0f, -2f), es.Seat);
        }
        // 종이꽃
        foreach (var p in af.Places)
            if (p.FlowerBy >= 0) AfFlower(ci, CellRect(p.At).GetCenter() + new Vector2(-6f, 6f), p.Id);
        // 다시 그린 그림
        foreach (var rp in af.Repaints)
        {
            var prop = w.Props.Placed.FirstOrDefault(x => x.Id == rp.Prop);
            if (prop == null) continue;
            AfRepaint(ci, PropAt(prop), rp, fine);
        }
        // 옮겨 온 독서등: 어두운 방이면 노란 빛 웅덩이
        foreach (var lm in af.Lamps)
        {
            if (!lm.Active || lm.Carrying >= 0) continue;
            var prop = w.Props.Placed.FirstOrDefault(x => x.Id == lm.Prop);
            if (prop == null || prop.RoomId < 0 || prop.RoomId >= w.Ship.Rooms.Count) continue;
            var room = w.Ship.Rooms[prop.RoomId];
            var c = PropAt(prop);
            bool dark = room.LightsOut || !room.Powered;
            float flick = 0.92f + 0.08f * Mathf.Sin(_time * 7f + lm.Id);
            if (dark)
            {
                for (int k = 4; k >= 1; k--) ci.Circle(c, T * 0.55f * k, AfWarm.WithAlpha(0.07f * flick));
                ci.Circle(c, 5f, AfWarm.WithAlpha(0.5f * flick));
            }
            // 전선이 탁자 모서리로 늘어진다
            ci.Polyline(new[] { c + new Vector2(2f, 4f), c + new Vector2(7f, 9f), c + new Vector2(13f, 9f) }, AfInk.WithAlpha(0.6f), 0.8f, true);
        }
    }

    /// <summary>소품이 그려지는 자리 (PaintRoomProps와 같은 식).</summary>
    private static Vector2 PropAt(PlacedProp p)
    {
        var cell = CellRect(p.At).GetCenter();
        if (p.Wall >= 0)
        {
            var d = Cell.Dirs4[p.Wall];
            return cell - new Vector2(-d.X, -d.Y) * (T * 0.5f - 6f);
        }
        return cell + new Vector2((p.Id % 2 == 0 ? -1f : 1f) * 7f, (p.Id / 2 % 2 == 0 ? -1f : 1f) * 6f);
    }

    private void AfHook(CanvasItem ci, Cell wall, float a)
    {
        var c = CellRect(wall).GetCenter();
        // 벽에 박은 작은 고리 둘 (나사 머리 + 갈고리)
        foreach (float dx in new[] { -9f, 9f })
        {
            var p = c + new Vector2(dx, 0f);
            ci.Circle(p, 1.6f, new Color(0.62f, 0.64f, 0.68f, a));
            ci.Arc(p + new Vector2(0f, 2f), 1.8f, 0f, Mathf.Pi, 6, new Color(0.45f, 0.47f, 0.5f, a), 0.9f, true);
        }
    }

    private void AfSheet(CanvasItem ci, DryLine l, bool fine)
    {
        var hook = CellRect(l.Hook).GetCenter();
        var at = CellRect(l.At).GetCenter();
        var dir = (at - hook).Normalized();
        var side = new Vector2(-dir.Y, dir.X);
        var a = hook + dir * (T * 0.5f) - side * 11f;
        var b = hook + dir * (T * 0.5f) + side * 11f;
        ci.DrawLine(a, b, new Color(0.85f, 0.85f, 0.8f, 0.9f), 0.8f, true); // 줄
        float wet = l.Wet;
        var col = AfSheetDry.Lerp(AfSheetWet, Mathf.Clamp(wet, 0f, 1f));
        float sway = Mathf.Sin(_time * (wet > 0.3f ? 0.6f : 1.6f) + l.Id) * (wet > 0.3f ? 0.6f : 1.8f);
        int n = 7;
        var pts = new Vector2[n * 2];
        float depth = 13f + 4f * wet; // 젖은 천은 늘어진다
        for (int i = 0; i < n; i++)
        {
            float u = i / (float)(n - 1);
            var top = a.Lerp(b, u);
            pts[i] = top;
            float wave = Mathf.Sin(u * Mathf.Tau * 1.5f + _time * 0.8f + l.Id) * 1.4f;
            pts[n * 2 - 1 - i] = top + dir * (depth + wave) + side * sway * u;
        }
        ci.Poly(pts, col.WithAlpha(0.92f));
        ci.Polyline(pts.Skip(n).Append(pts[0]).ToArray(), col.Darkened(0.35f).WithAlpha(0.8f), 0.7f, true);
        // 줄무늬 (침구 무늬)
        for (int k = 1; k < 4; k++)
        {
            var p0 = a.Lerp(b, k / 4f);
            ci.DrawLine(p0 + dir * 2f, p0 + dir * (depth - 2f) + side * sway * (k / 4f), col.Darkened(0.15f).WithAlpha(0.5f), 0.6f, true);
        }
        // 빨래집게
        foreach (float u in new[] { 0.15f, 0.85f })
        {
            var p = a.Lerp(b, u);
            ci.Box(new Rect2(p - new Vector2(1f, 1.5f), new Vector2(2f, 3.5f)), new Color(0.75f, 0.55f, 0.3f));
        }
        // 젖었으면 물방울이 떨어진다
        if (wet > 0.25f && fine)
            for (int k = 0; k < 3; k++)
            {
                float ph = Mathf.PosMod(_time * 0.9f + k * 0.33f + l.Id * 0.1f, 1f);
                var p = a.Lerp(b, 0.25f + 0.25f * k) + dir * (depth + 1f + ph * 8f);
                ci.Circle(p, 0.9f, new Color(0.55f, 0.75f, 0.95f, 0.8f * (1f - ph) * wet));
            }
    }

    private void AfNote(CanvasItem ci, Vector2 c, int thrown, int kept, bool fine)
    {
        var r = new Rect2(c - new Vector2(5f, 6f), new Vector2(10f, 12f));
        ci.Box(r, AfPaper);
        ci.Box(r, new Color(0.55f, 0.52f, 0.45f, 0.8f), false, 0.6f);
        ci.Box(new Rect2(r.Position + new Vector2(2.5f, -1.5f), new Vector2(5f, 2.6f)), AfTape.WithAlpha(0.85f)); // 테이프
        for (int i = 0; i < 3; i++) ci.DrawLine(r.Position + new Vector2(1.5f, 3.5f + i * 2.4f), r.Position + new Vector2(8.5f - i, 3.5f + i * 2.4f), AfInk.WithAlpha(0.55f), 0.5f);
        int x = Mathf.Min(thrown, 3);
        for (int i = 0; i < x; i++)
        {
            var p = r.Position + new Vector2(2.2f + i * 2.8f, 10.2f);
            ci.DrawLine(p - new Vector2(0.9f, 0.9f), p + new Vector2(0.9f, 0.9f), AfRed, 0.7f, true);
            ci.DrawLine(p + new Vector2(-0.9f, 0.9f), p + new Vector2(0.9f, -0.9f), AfRed, 0.7f, true);
        }
        if (kept > 0 && fine) ci.Arc(r.End - new Vector2(2f, 2f), 1.2f, 0f, Mathf.Tau, 6, new Color(0.3f, 0.6f, 0.35f), 0.6f, true);
    }

    private void AfCup(CanvasItem ci, Vector2 c, int id)
    {
        var body = new Rect2(c - new Vector2(2.6f, 2.8f), new Vector2(5.2f, 5.6f));
        ci.Box(body, new Color(0.9f, 0.88f, 0.82f));
        ci.Box(body, new Color(0.35f, 0.33f, 0.3f, 0.8f), false, 0.6f);
        ci.Arc(c + new Vector2(3.4f, 0f), 1.6f, -Mathf.Pi / 2f, Mathf.Pi / 2f, 6, new Color(0.35f, 0.33f, 0.3f, 0.9f), 0.8f, true);
        ci.DrawLine(body.Position + new Vector2(0.6f, 1f), body.Position + new Vector2(4.6f, 1f), new Color(0.35f, 0.22f, 0.12f, 0.8f), 0.8f); // 비어 있지 않은 컵
        float ph = Mathf.PosMod(_time * 0.25f + id * 0.17f, 1f);
        if (ph < 0.35f) // 아주 가끔 김
        {
            float a = Mathf.Sin(ph / 0.35f * Mathf.Pi) * 0.45f;
            ci.Arc(c + new Vector2(0f, -5f - ph * 10f), 1.5f, 0f, Mathf.Pi, 5, new Color(1f, 1f, 1f, a), 0.7f, true);
        }
    }

    private void AfFlower(CanvasItem ci, Vector2 c, int id)
    {
        var petal = Color.FromHsv(Mathf.PosMod(id * 0.37f, 1f) * 0.2f + 0.9f, 0.35f, 0.95f);
        ci.DrawLine(c + new Vector2(0f, 1.5f), c + new Vector2(2.5f, 6f), new Color(0.35f, 0.55f, 0.3f), 0.8f, true);
        for (int k = 0; k < 5; k++)
        {
            float ang = k * Mathf.Tau / 5f;
            ci.Circle(c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 2f, 1.5f, petal);
        }
        ci.Circle(c, 1.1f, new Color(0.95f, 0.85f, 0.4f));
        // 접은 종이 자국
        ci.DrawLine(c + new Vector2(-2.5f, -0.5f), c + new Vector2(2.5f, 0.5f), petal.Darkened(0.25f), 0.4f);
    }

    private void AfRepaint(CanvasItem ci, Vector2 c, Repaint rp, bool fine)
    {
        // 그을린 귀퉁이 (일부러 남긴 것)
        var corner = c + new Vector2(-5.5f, -4.5f);
        ci.Poly(new[] { corner, corner + new Vector2(5f, 0f), corner + new Vector2(0f, 4.5f) }, new Color(0.12f, 0.08f, 0.05f, 0.85f));
        ci.Polyline(new[] { corner + new Vector2(5f, 0f), corner + new Vector2(3.4f, 1.6f), corner + new Vector2(2.2f, 2.4f), corner + new Vector2(0f, 4.5f) }, new Color(0.45f, 0.25f, 0.1f, 0.8f), 0.6f, true);
        // 새 붓자국
        var h = Mathf.PosMod(rp.Prop * 0.29f, 1f);
        for (int k = 0; k < 3; k++)
        {
            var col = Color.FromHsv(Mathf.PosMod(h + k * 0.18f, 1f), 0.65f, 0.95f, 0.9f);
            var p0 = c + new Vector2(-2f + k * 1.8f, 2.5f - k * 1.6f);
            ci.DrawLine(p0, p0 + new Vector2(3.2f, -1.2f), col, 1.1f, true);
        }
        if (rp.ForDead && fine)
        {
            // 서명 둘: 떠난 사람 · 이어 그린 사람
            var s0 = c + new Vector2(1.5f, 3.6f);
            ci.Polyline(new[] { s0, s0 + new Vector2(0.8f, -0.8f), s0 + new Vector2(1.6f, 0f), s0 + new Vector2(2.4f, -0.8f) }, Palette.Crew(rp.Maker).WithAlpha(0.9f), 0.5f, true);
            var s1 = s0 + new Vector2(0f, 1.4f);
            ci.Polyline(new[] { s1, s1 + new Vector2(1f, -0.6f), s1 + new Vector2(2f, 0.2f), s1 + new Vector2(3f, -0.5f) }, Palette.Crew(rp.Painter).WithAlpha(0.9f), 0.5f, true);
        }
    }

    /// <summary>위 층 (사람 위): 악몽에 뒤척임 · 놀라 깸 · 안고 가는 침구 · 들고 가는 등.</summary>
    private void PaintAfterOver(CanvasItem ci)
    {
        var w = _world;
        var af = w.After;
        if (af.Minds.Count == 0 && af.Lamps.Count == 0) return;
        float rad = CrewRadius;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.CarriedBy != null) continue;
            var m = af.Peek(c);
            if (m == null) continue;
            var p = ToPx(c.Position);
            if (af.Tossing(c))
            {
                // 이불이 물결친다 · 몸을 뒤척인다
                float t = _time * 2.2f + c.Id;
                for (int k = 0; k < 3; k++)
                {
                    var q = p + new Vector2(-rad * 0.9f + k * rad * 0.9f, rad * 0.55f + Mathf.Sin(t + k) * 1.5f);
                    ci.Arc(q, rad * 0.45f, Mathf.Pi * 1.1f, Mathf.Pi * 1.9f, 6, new Color(0.75f, 0.78f, 0.9f, 0.55f), 1f, true);
                }
                float jit = Mathf.Sin(t * 3f) * 1.2f;
                ci.DrawLine(p + new Vector2(rad + 1f, -2f + jit), p + new Vector2(rad + 4f, -4f + jit), new Color(0.9f, 0.9f, 1f, 0.45f), 0.8f, true);
            }
            if (af.SatUp(c))
            {
                // 놀라 일어나 앉았다: 머리가 위로 · 숨 고르는 짧은 선
                var head = p + new Vector2(0f, -rad * 0.9f);
                ci.Circle(head, rad * 0.42f, Palette.Crew(c.Id).WithAlpha(0.85f));
                for (int k = -1; k <= 1; k++)
                    ci.DrawLine(head + new Vector2(k * 3f, -rad * 0.55f), head + new Vector2(k * 4.5f, -rad * 0.95f), new Color(1f, 1f, 1f, 0.6f), 0.8f, true);
            }
            if (m.CarryBundle >= 0)
            {
                // 둘둘 만 침구
                var b = p + new Vector2(rad * 0.8f, rad * 0.2f);
                var roll = new Rect2(b - new Vector2(5f, 3f), new Vector2(10f, 6f));
                Gfx.RoundRect(ci, roll, AfSheetDry.Lerp(AfSheetWet, af.BedWet(w.Ship.Furniture.FirstOrDefault(f => f.Id == m.CarryBundle) ?? w.Ship.Furniture[0])), 3f, new Color(0.4f, 0.4f, 0.45f));
                ci.DrawLine(roll.Position + new Vector2(3f, 0.5f), roll.Position + new Vector2(3f, 5.5f), new Color(0.6f, 0.6f, 0.68f), 0.7f);
                ci.DrawLine(roll.Position + new Vector2(7f, 0.5f), roll.Position + new Vector2(7f, 5.5f), new Color(0.6f, 0.6f, 0.68f), 0.7f);
            }
        }
        foreach (var lm in af.Lamps)
        {
            if (lm.Carrying < 0) continue;
            var who = w.Crew.FirstOrDefault(x => x.Id == lm.Carrying);
            if (who == null) continue;
            var h = ToPx(who.Position) + new Vector2(-rad * 0.85f, rad * 0.1f);
            // 들고 가는 독서등 (갓 · 기둥 · 늘어진 전선)
            ci.DrawLine(h + new Vector2(0f, 4f), h + new Vector2(0f, -1f), AfInk, 1f, true);
            ci.Poly(new[] { h + new Vector2(-3f, -1f), h + new Vector2(3f, -1f), h + new Vector2(2f, -4.5f), h + new Vector2(-2f, -4.5f) }, AfWarm);
            ci.Polyline(new[] { h + new Vector2(0f, 4f), h + new Vector2(2f, 7f), h + new Vector2(-1f, 9f) }, AfInk.WithAlpha(0.7f), 0.7f, true);
        }
    }
}
