using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v18.15 결정에 대한 마음 · 딜레마 · 결정 장부의 그림 (읽기만):
// 사람 위 — 정한 일이 마음에 들면 고개를 끄덕이는 작은 머리(위아래 흔들림 · 초록 잎), 싫으면 고개를 젓는 머리(좌우 흔들림 · 처진 입),
//   선장 결정에 오래 실망한 사람은 금이 간 별, 마음에 걸리는 일이 있는 사람은 엉킨 실타래.
// 바닥 — 정하는 중인 딜레마의 표지판(일마다 다른 그림 24가지: 산소통 · 격리 침대 · 끊긴 신호탑 · 칼 두 자루 · 눈 달린 상자 · 들것 …) —
//   급한 일은 함교 · 그 일이 생긴 방에서 깜박이고, 회의에 오른 일은 모인 자리 위에 · 정한 뒤 한동안 봉인(밀랍 도장 · 고른 쪽 색).
//   밀항자가 숨어 지낸 자리(구겨진 담요 · 빈 깡통 · 부스러기) · 내리겠다는 사람의 침대 옆 꾸린 짐 가방 ·
//   돌아온 결과가 남긴 식당 벽 쪽지(검은 띠를 두른 말 · 아이가 그린 배 · 고맙다는 쪽지).
public partial class ShipView
{
    private static readonly Color VxLike = new(0.55f, 0.85f, 0.5f);
    private static readonly Color VxDislike = new(0.95f, 0.5f, 0.42f);
    private static readonly Color VxInk = new(0.12f, 0.12f, 0.15f, 0.9f);

    private void PaintValuesOver(CanvasItem ci)
    {
        if (ValueSystem.Off) return;
        var w = _world;
        var vs = w.Values;
        bool fine = Zoom > 0.6f;
        long now = w.Tick;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Away || vs.Peek(c) is not Outlook o) continue;
            var p = CrewPx(c);
            float r = CrewRadius;
            var at = p + new Vector2(r * 1.25f, -r * 1.9f);
            // 방금 정한 일에 대한 반응 (한 시간 남짓)
            if (o.Last is Stand st && now - st.Tick < SimTime.Minutes(60))
            {
                float fade = 1f - (now - st.Tick) / (float)SimTime.Minutes(60);
                NodHead(ci, at, st.Liked, Mathf.Clamp(fade * 1.5f, 0f, 1f), c.Id, MathF.Abs(st.S) > 0.4f);
                continue;
            }
            if (!fine) continue;
            if (c.Id != w.Command.CaptainId && o.Captain < -0.45f) CrackedStar(ci, at, 5.5f, c.Id);
            else if (o.Conscience > 0.3f) Tangle(ci, at, 5f, c.Id);
        }
    }

    /// <summary>작은 머리: 끄덕임(위아래 · 잎) · 고개 젓기(좌우 · 처진 입).</summary>
    private void NodHead(CanvasItem ci, Vector2 at, bool liked, float a, int seed, bool strong)
    {
        float t = _time * (strong ? 7f : 5f) + seed;
        var off = liked ? new Vector2(0, Mathf.Sin(t) * 1.6f) : new Vector2(Mathf.Sin(t) * 2.2f, 0);
        var h = at + off;
        var col = liked ? VxLike : VxDislike;
        ci.DrawCircle(h, 5.2f, new Color(0.96f, 0.86f, 0.74f, a));
        ci.DrawArc(h, 5.2f, 0f, Mathf.Tau, 18, col with { A = a }, 1.1f, true);
        ci.DrawCircle(h + new Vector2(-1.8f, -0.8f), 0.7f, VxInk with { A = a });
        ci.DrawCircle(h + new Vector2(1.8f, -0.8f), 0.7f, VxInk with { A = a });
        if (liked)
        {
            ci.DrawArc(h + new Vector2(0, 0.6f), 2.2f, 0.2f, Mathf.Pi - 0.2f, 6, VxInk with { A = a }, 0.9f, true);
            // 위아래 흔들림 자국 · 잎
            ci.DrawArc(h + new Vector2(-7.5f, 0), 3f, -0.9f, 0.9f, 5, col with { A = a * 0.7f }, 0.9f, true);
            var leaf = h + new Vector2(5.5f, -5.5f);
            ci.DrawColoredPolygon(new[] { leaf, leaf + new Vector2(3f, -2.2f), leaf + new Vector2(4.2f, 0.6f), leaf + new Vector2(1.2f, 1.6f) }, col with { A = a });
            ci.DrawLine(leaf, leaf + new Vector2(3.4f, -0.6f), new Color(0.25f, 0.45f, 0.2f, a), 0.6f);
        }
        else
        {
            ci.DrawArc(h + new Vector2(0, 3f), 2.2f, Mathf.Pi + 0.3f, Mathf.Tau - 0.3f, 6, VxInk with { A = a }, 0.9f, true);
            // 좌우 흔들림 자국 (양옆 괄호)
            ci.DrawArc(h + new Vector2(-1.5f, 0), 8f, Mathf.Pi - 0.5f, Mathf.Pi + 0.5f, 5, col with { A = a * 0.7f }, 0.9f, true);
            ci.DrawArc(h + new Vector2(1.5f, 0), 8f, -0.5f, 0.5f, 5, col with { A = a * 0.7f }, 0.9f, true);
            if (strong) // 치켜 올라간 눈썹
            {
                ci.DrawLine(h + new Vector2(-3f, -3.2f), h + new Vector2(-0.8f, -2.2f), VxInk with { A = a }, 0.8f);
                ci.DrawLine(h + new Vector2(3f, -3.2f), h + new Vector2(0.8f, -2.2f), VxInk with { A = a }, 0.8f);
            }
        }
    }

    /// <summary>금이 간 별 (선장 결정에 오래 실망한 사람).</summary>
    private void CrackedStar(CanvasItem ci, Vector2 c, float s, int seed)
    {
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++) { float ang = -Mathf.Pi / 2 + i * Mathf.Pi / 5f; float rr = i % 2 == 0 ? s : s * 0.45f; pts[i] = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr; }
        ci.DrawColoredPolygon(pts, new Color(0.82f, 0.72f, 0.38f, 0.8f));
        var outline = pts.Append(pts[0]).ToArray();
        ci.DrawPolyline(outline, new Color(0.45f, 0.36f, 0.15f, 0.9f), 0.8f, true);
        float jig = Mathf.Sin(_time * 0.7f + seed) * 0.4f;
        ci.DrawPolyline(new[] { c + new Vector2(-0.5f, -s), c + new Vector2(0.8f + jig, -s * 0.3f), c + new Vector2(-0.6f, s * 0.15f), c + new Vector2(0.9f, s * 0.7f) }, new Color(0.15f, 0.12f, 0.1f, 0.95f), 0.9f, true);
    }

    /// <summary>엉킨 실타래 (마음에 걸리는 일).</summary>
    private void Tangle(CanvasItem ci, Vector2 c, float s, int seed)
    {
        var col = new Color(0.62f, 0.62f, 0.75f, 0.85f);
        for (int k = 0; k < 3; k++)
        {
            var pts = new Vector2[9];
            for (int i = 0; i < 9; i++)
            {
                float ang = i * 0.9f + k * 2.1f + seed * 0.3f + _time * 0.2f;
                pts[i] = c + new Vector2(Mathf.Cos(ang) * s * (0.5f + 0.4f * Mathf.Sin(i + k)), Mathf.Sin(ang * 1.3f) * s * 0.7f);
            }
            ci.DrawPolyline(pts, col, 0.8f, true);
        }
        ci.DrawLine(c + new Vector2(s * 0.6f, s * 0.5f), c + new Vector2(s * 1.4f, s * 1.3f + Mathf.Sin(_time + seed)), col, 0.8f, true);
    }

    private void PaintValuesFloor(CanvasItem ci)
    {
        if (ValueSystem.Off) return;
        var w = _world;
        var vs = w.Values;
        bool close = Zoom > 0.9f;
        long now = w.Tick;
        // 밀항자가 숨어 지낸 자리 (사흘 동안 남는다)
        foreach (var (room, cell, tick) in vs.Nests)
        {
            if (now - tick > SimTime.TicksPerDay * 3) continue;
            Nest(ci, ToPx(cell.Center), close, (int)(tick % 97));
        }
        // 내리겠다는 사람의 침대 옆 짐 가방
        foreach (var c in w.Crew)
            if (!c.Dead && !c.Away && vs.Peek(c) is { LeaveAsked: >= 0 } && c.HomeBed is Furniture bed && bed.Cells.Count > 0)
                Motif(ci, "bag", ToPx(bed.Cells[0].Center) + new Vector2(T * 0.38f, T * 0.25f), T * 0.32f, new Color(0.45f, 0.5f, 0.38f), close, c.Id);
        // 식당 벽 쪽지
        int k = 0;
        foreach (var (tick, roomId, kind, text) in vs.Keepsakes)
        {
            if (w.Ship.Rooms.FirstOrDefault(r => r.Id == roomId) is not Room rm || rm.Cells.Count == 0) continue;
            var top = rm.Cells.OrderBy(cc => cc.Y).ThenBy(cc => cc.X).Skip(k % Math.Max(1, rm.Cells.Count(cc => cc.Y == rm.Cells.Min(q => q.Y)))).First();
            Note(ci, ToPx(top.Center) + new Vector2(0, -T * 0.3f), kind, close, k);
            k++;
        }
        // 정하는 중 · 막 정한 딜레마의 표지판
        foreach (var d in vs.Dilemmas)
        {
            bool open = d.Stage < 2;
            if (!open && (d.DecidedAt < 0 || now - d.DecidedAt > SimTime.Hours(2))) continue;
            Room? place = null;
            if (d.Stage == 1 && w.Motions.Now is Sitting s && s.Motion.Id == d.Motion) place = s.Venue;
            place ??= d.Room >= 0 ? w.Ship.Rooms.FirstOrDefault(r => r.Id == d.Room) : null;
            place ??= w.Ship.RoomsOf(RoomType.Bridge).FirstOrDefault();
            if (place == null || place.Cells.Count == 0) continue;
            var cx = place.Cells.Aggregate(System.Numerics.Vector2.Zero, (acc, q) => acc + q.Center) / place.Cells.Count;
            var at = ToPx(cx) + new Vector2(0, -T * 0.9f);
            float pulse = open ? 0.75f + 0.25f * Mathf.Sin(_time * 3f) : 0.6f;
            // 받침판 (팔각) — 급한 일은 붉은 테, 회의 일은 놋쇠 테
            var rim = d.Spec.Urgent ? new Color(0.9f, 0.4f, 0.3f, pulse) : new Color(0.85f, 0.7f, 0.35f, pulse);
            var plate = new Vector2[8];
            for (int i = 0; i < 8; i++) { float ang = Mathf.Pi / 8 + i * Mathf.Pi / 4; plate[i] = at + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * T * 0.62f; }
            ci.DrawColoredPolygon(plate, new Color(0.1f, 0.12f, 0.16f, 0.85f));
            ci.DrawPolyline(plate.Append(plate[0]).ToArray(), rim, 1.6f, true);
            Motif(ci, d.Spec.Motif, at, T * 0.45f, new Color(0.9f, 0.92f, 0.95f), close, d.Id);
            if (!open && d.ChoseA is bool a)
            {
                // 밀랍 도장: 고른 쪽 (A 짙은 초록 · B 짙은 자주)
                var seal = at + new Vector2(T * 0.45f, T * 0.45f);
                var wax = a ? new Color(0.25f, 0.5f, 0.3f) : new Color(0.5f, 0.2f, 0.35f);
                for (int i = 0; i < 7; i++) ci.DrawCircle(seal + new Vector2(Mathf.Cos(i * 0.9f), Mathf.Sin(i * 0.9f)) * 3.2f, 2.6f, wax);
                ci.DrawCircle(seal, 4f, wax.Lightened(0.15f));
                ci.DrawArc(seal, 2.4f, 0f, Mathf.Tau, 10, wax.Darkened(0.4f), 0.8f, true);
            }
            else if (d.Stage == 1)
            {
                // 회의에 오른 일: 표지판 아래 작은 투표 손 세 개
                for (int i = -1; i <= 1; i++)
                {
                    var hnd = at + new Vector2(i * 7f, T * 0.75f + Mathf.Sin(_time * 4f + i) * 1.2f);
                    ci.DrawRect(new Rect2(hnd - new Vector2(1.5f, 0), new Vector2(3, 5)), new Color(0.95f, 0.85f, 0.72f));
                    ci.DrawCircle(hnd, 2f, new Color(0.95f, 0.85f, 0.72f));
                }
            }
        }
    }

    /// <summary>밀항자가 숨어 지낸 자리: 구겨진 담요 · 빈 깡통 · 부스러기.</summary>
    private void Nest(CanvasItem ci, Vector2 p, bool close, int seed)
    {
        var blanket = new Color(0.45f, 0.36f, 0.3f, 0.9f);
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++) { float ang = i * Mathf.Tau / 10f; float rr = T * (0.34f + 0.08f * Mathf.Sin(i * 2.7f + seed)); pts[i] = p + new Vector2(Mathf.Cos(ang) * rr, Mathf.Sin(ang) * rr * 0.7f); }
        ci.DrawColoredPolygon(pts, blanket);
        for (int i = 0; i < 3; i++) ci.DrawArc(p + new Vector2(-T * 0.15f + i * T * 0.12f, 0), T * 0.12f, 0.3f, 2.4f, 6, blanket.Darkened(0.35f), 1f, true);
        var can = p + new Vector2(T * 0.32f, T * 0.12f);
        ci.DrawRect(new Rect2(can - new Vector2(3, 4), new Vector2(6, 8)), new Color(0.7f, 0.72f, 0.75f));
        ci.DrawRect(new Rect2(can - new Vector2(3, 1.5f), new Vector2(6, 3)), new Color(0.75f, 0.3f, 0.25f));
        if (!close) return;
        ci.DrawArc(can + new Vector2(0, -4), 3f, Mathf.Pi, Mathf.Tau, 6, new Color(0.5f, 0.52f, 0.55f), 0.8f, true);
        for (int i = 0; i < 6; i++) ci.DrawCircle(p + new Vector2(Mathf.Sin(i * 3.1f + seed) * T * 0.4f, Mathf.Cos(i * 1.7f) * T * 0.3f), 0.8f, new Color(0.85f, 0.75f, 0.5f));
    }

    /// <summary>식당 벽 쪽지: 0 검은 띠를 두른 말 · 1 고맙다는 쪽지(아이가 그린 배).</summary>
    private void Note(CanvasItem ci, Vector2 p, int kind, bool close, int seed)
    {
        float tilt = (seed % 3 - 1) * 0.12f;
        ci.DrawSetTransform(p, tilt, Vector2.One);
        var paper = kind == 0 ? new Color(0.88f, 0.86f, 0.8f) : new Color(0.98f, 0.94f, 0.8f);
        ci.DrawRect(new Rect2(-8, -6, 16, 12), paper);
        ci.DrawCircle(new Vector2(0, -6), 1.4f, new Color(0.8f, 0.25f, 0.2f)); // 압정
        if (kind == 0)
        {
            ci.DrawLine(new Vector2(-8, -6), new Vector2(-3, -6), new Color(0.1f, 0.1f, 0.1f), 2f);
            ci.DrawLine(new Vector2(-8, -6), new Vector2(-8, -1), new Color(0.1f, 0.1f, 0.1f), 2f);
            if (close) for (int i = 0; i < 3; i++) ci.DrawLine(new Vector2(-5, -2 + i * 3), new Vector2(6 - i * 2, -2 + i * 3), new Color(0.3f, 0.3f, 0.32f), 0.7f);
        }
        else
        {
            // 아이가 그린 배 (삐뚤한 선 · 해)
            ci.DrawPolyline(new[] { new Vector2(-6, 2), new Vector2(-3, -2), new Vector2(4, -2), new Vector2(6, 2), new Vector2(-6, 2) }, new Color(0.25f, 0.45f, 0.8f), 0.9f);
            ci.DrawCircle(new Vector2(5, -4), 1.6f, new Color(0.95f, 0.7f, 0.2f));
            if (close) { ci.DrawCircle(new Vector2(-1.5f, 4), 0.9f, new Color(0.9f, 0.35f, 0.4f)); ci.DrawCircle(new Vector2(0.2f, 4), 0.9f, new Color(0.9f, 0.35f, 0.4f)); }
        }
        ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>딜레마마다 다른 그림 (24가지) — s = 반 크기.</summary>
    private void Motif(CanvasItem ci, string key, Vector2 c, float s, Color col, bool close, int seed)
    {
        float t = _time + seed * 0.37f;
        Color dim = col with { A = col.A * 0.6f };
        void L(float x1, float y1, float x2, float y2, float wd = 1.4f) => ci.DrawLine(c + new Vector2(x1, y1) * s, c + new Vector2(x2, y2) * s, col, wd, true);
        void O(float x, float y, float r, bool fill = false) { if (fill) ci.DrawCircle(c + new Vector2(x, y) * s, r * s, col); else ci.DrawArc(c + new Vector2(x, y) * s, r * s, 0f, Mathf.Tau, 16, col, 1.2f, true); }
        void Poly(params float[] xy) { var pts = new Vector2[xy.Length / 2 + 1]; for (int i = 0; i < xy.Length / 2; i++) pts[i] = c + new Vector2(xy[2 * i], xy[2 * i + 1]) * s; pts[^1] = pts[0]; ci.DrawPolyline(pts, col, 1.3f, true); }
        switch (key)
        {
            case "air": // 산소통 + 둘로 갈리는 화살
                Poly(-0.25f, -0.6f, 0.25f, -0.6f, 0.25f, 0.7f, -0.25f, 0.7f); L(-0.1f, -0.75f, 0.1f, -0.75f, 2f);
                L(0.4f, 0f, 0.75f, -0.35f); L(0.4f, 0f, 0.75f, 0.35f); O(0f, 0.1f, 0.12f, true); break;
            case "quarantine": // 침대 + 점선 울타리
                Poly(-0.5f, 0.1f, 0.5f, 0.1f, 0.5f, 0.35f, -0.5f, 0.35f); O(-0.32f, -0.02f, 0.12f, true);
                for (int i = 0; i < 8; i++) { float a0 = i * Mathf.Tau / 8f + t * 0.3f; ci.DrawArc(c, s * 0.85f, a0, a0 + 0.4f, 3, col, 1f, true); } break;
            case "beacon": // 신호탑 + 끊긴 물결
                L(0f, 0.7f, 0f, -0.3f); L(-0.3f, 0.7f, 0f, 0.2f); L(0.3f, 0.7f, 0f, 0.2f); O(0f, -0.4f, 0.1f, true);
                ci.DrawArc(c + new Vector2(0, -0.4f) * s, s * 0.35f, -2.3f, -0.8f, 6, col, 1.1f, true);
                ci.DrawArc(c + new Vector2(0, -0.4f) * s, s * 0.6f, -2.2f, -1.7f, 4, dim, 1.1f, true); ci.DrawArc(c + new Vector2(0, -0.4f) * s, s * 0.6f, -1.4f, -0.9f, 4, dim, 1.1f, true); break;
            case "pirate": // 엇갈린 칼 두 자루 + 손잡이
                L(-0.6f, 0.6f, 0.5f, -0.5f); L(0.6f, 0.6f, -0.5f, -0.5f); L(-0.65f, 0.4f, -0.4f, 0.65f, 2f); L(0.65f, 0.4f, 0.4f, 0.65f, 2f); O(0f, -0.7f, 0.12f); break;
            case "stowaway": // 상자 틈의 두 눈
                Poly(-0.6f, -0.45f, 0.6f, -0.45f, 0.6f, 0.55f, -0.6f, 0.55f); L(-0.6f, 0.05f, 0.6f, 0.05f, 1f);
                { float blink = Mathf.Sin(t * 0.9f) > 0.92f ? 0.2f : 1f; ci.DrawCircle(c + new Vector2(-0.2f, -0.12f) * s, 0.08f * s * blink + 0.5f, col); ci.DrawCircle(c + new Vector2(0.2f, -0.12f) * s, 0.08f * s * blink + 0.5f, col); } break;
            case "stretcher": // 들것 + 십자
                L(-0.75f, 0.2f, 0.75f, 0.2f, 2f); L(-0.75f, 0.45f, 0.75f, 0.45f, 1f); L(-0.6f, 0.2f, -0.6f, 0.6f); L(0.6f, 0.2f, 0.6f, 0.6f);
                L(0f, -0.65f, 0f, -0.15f, 2.2f); L(-0.25f, -0.4f, 0.25f, -0.4f, 2.2f); break;
            case "flask": // 플라스크 + 올라오는 방울
                Poly(-0.15f, -0.6f, 0.15f, -0.6f, 0.15f, -0.15f, 0.5f, 0.6f, -0.5f, 0.6f, -0.15f, -0.15f);
                for (int i = 0; i < 3; i++) { float y = 0.4f - ((t * 0.5f + i * 0.33f) % 1f) * 1.2f; O(-0.1f + i * 0.1f, y, 0.07f, true); } break;
            case "logbook": // 펼친 장부 + 번진 얼룩
                Poly(-0.7f, -0.4f, 0f, -0.3f, 0.7f, -0.4f, 0.7f, 0.5f, 0f, 0.6f, -0.7f, 0.5f); L(0f, -0.3f, 0f, 0.6f, 1f);
                for (int i = 0; i < 3; i++) L(-0.55f, -0.15f + i * 0.2f, -0.15f, -0.1f + i * 0.2f, 0.8f);
                ci.DrawCircle(c + new Vector2(0.35f, 0.1f) * s, 0.2f * s, dim); break;
            case "eye": // 반은 렌즈, 반은 사람 눈
                ci.DrawArc(c, s * 0.6f, Mathf.Pi * 0.5f, Mathf.Pi * 1.5f, 10, col, 1.3f, true); O(0f, 0f, 0.22f, true);
                Poly(0f, -0.6f, 0.65f, 0f, 0f, 0.6f); L(0f, -0.75f, 0f, 0.75f, 0.8f); break;
            case "bowl": // 그릇 + 숟가락 + 김
                ci.DrawArc(c + new Vector2(0, -0.05f) * s, s * 0.6f, 0f, Mathf.Pi, 10, col, 1.5f, true); L(-0.6f, -0.05f, 0.6f, -0.05f);
                L(0.3f, -0.1f, 0.7f, -0.7f, 1.6f); ci.DrawArc(c + new Vector2(-0.2f, -0.45f) * s, s * 0.15f, -Mathf.Pi, 0f, 5, dim, 1f, true); break;
            case "scale": // 저울 (한쪽이 기운다)
            {
                float tip = Mathf.Sin(t * 0.8f) * 0.12f;
                L(0f, -0.6f, 0f, 0.6f); L(-0.3f, 0.6f, 0.3f, 0.6f, 2f); L(-0.6f, -0.4f + tip, 0.6f, -0.4f - tip);
                ci.DrawArc(c + new Vector2(-0.6f, -0.1f + tip) * s, s * 0.22f, 0f, Mathf.Pi, 6, col, 1.2f, true);
                ci.DrawArc(c + new Vector2(0.6f, -0.1f - tip) * s, s * 0.22f, 0f, Mathf.Pi, 6, col, 1.2f, true); break;
            }
            case "door": // 문 + 문틈의 불꽃
                Poly(-0.45f, -0.7f, 0.45f, -0.7f, 0.45f, 0.7f, -0.45f, 0.7f); O(0.3f, 0f, 0.06f, true);
                { float fl = Mathf.Sin(t * 6f) * 0.08f; ci.DrawColoredPolygon(new[] { c + new Vector2(0.5f, 0.6f) * s, c + new Vector2(0.75f + fl, 0.1f) * s, c + new Vector2(0.6f, -0.2f + fl) * s, c + new Vector2(0.95f, 0.6f) * s }, new Color(1f, 0.55f, 0.2f, 0.9f)); } break;
            case "crate": // 리본 묶인 짐 + 바깥 화살
                Poly(-0.5f, -0.4f, 0.4f, -0.4f, 0.4f, 0.5f, -0.5f, 0.5f); L(-0.05f, -0.4f, -0.05f, 0.5f, 1f); L(-0.5f, 0.05f, 0.4f, 0.05f, 1f);
                O(-0.05f, -0.5f, 0.1f); L(0.55f, 0f, 0.9f, 0f); L(0.9f, 0f, 0.75f, -0.15f); L(0.9f, 0f, 0.75f, 0.15f); break;
            case "wreck": // 부러진 선체 + 이름표
                Poly(-0.8f, 0.1f, -0.1f, -0.2f, 0f, 0.15f, -0.7f, 0.45f); Poly(0.15f, -0.1f, 0.8f, -0.35f, 0.85f, 0.05f, 0.2f, 0.3f);
                L(0.2f, 0.5f, 0.45f, 0.75f, 0.8f); ci.DrawRect(new Rect2(c + new Vector2(0.35f, 0.6f) * s, new Vector2(0.3f, 0.18f) * s), dim); break;
            case "radio": // 무전기 + 지직
                Poly(-0.5f, -0.3f, 0.5f, -0.3f, 0.5f, 0.6f, -0.5f, 0.6f); L(0.3f, -0.3f, 0.5f, -0.8f); O(-0.2f, 0.15f, 0.18f);
                ci.DrawPolyline(new[] { c + new Vector2(0.6f, -0.1f) * s, c + new Vector2(0.75f, -0.25f) * s, c + new Vector2(0.8f, 0.05f) * s, c + new Vector2(0.95f, -0.15f) * s }, dim, 1f, true); break;
            case "vial": // 약병 + 떨어지는 방울
                Poly(-0.2f, -0.4f, 0.2f, -0.4f, 0.2f, 0.5f, -0.2f, 0.5f); L(-0.25f, -0.5f, 0.25f, -0.5f, 2.2f); L(-0.2f, 0.15f, 0.2f, 0.15f, 1f);
                { float y = 0.55f + ((t * 0.6f) % 1f) * 0.3f; O(0f, y, 0.06f, true); } break;
            case "wrench": // 스패너 + 초승달
                L(-0.6f, 0.6f, 0.25f, -0.25f, 2.2f); ci.DrawArc(c + new Vector2(0.35f, -0.35f) * s, s * 0.22f, -2.6f, 1.2f, 8, col, 2f, true);
                ci.DrawArc(c + new Vector2(-0.5f, -0.5f) * s, s * 0.22f, 0.8f, 4.4f, 8, dim, 1.4f, true); break;
            case "family": // 어른 둘 · 아이 하나
                O(-0.45f, -0.35f, 0.14f, true); L(-0.45f, -0.2f, -0.45f, 0.5f, 2f); O(0.45f, -0.35f, 0.14f, true); L(0.45f, -0.2f, 0.45f, 0.5f, 2f);
                O(0f, 0.05f, 0.1f, true); L(0f, 0.15f, 0f, 0.55f, 1.6f); L(-0.45f, 0.1f, 0f, 0.3f, 0.9f); L(0.45f, 0.1f, 0f, 0.3f, 0.9f); break;
            case "parcel": // 소포 + 금지 도장
                Poly(-0.55f, -0.35f, 0.55f, -0.35f, 0.55f, 0.45f, -0.55f, 0.45f); L(-0.55f, 0.05f, 0.55f, 0.05f, 0.9f);
                O(0.35f, -0.4f, 0.22f); L(0.2f, -0.55f, 0.5f, -0.25f, 1f); break;
            case "drop": // 갈라지는 물방울
                ci.DrawArc(c + new Vector2(0, 0.2f) * s, s * 0.4f, -0.3f, Mathf.Pi + 0.3f, 10, col, 1.4f, true); L(-0.38f, 0.08f, 0f, -0.7f); L(0.38f, 0.08f, 0f, -0.7f);
                L(0f, -0.2f, 0f, 0.55f, 0.8f); break;
            case "key": // 열쇠
                O(-0.4f, 0f, 0.25f); L(-0.15f, 0f, 0.7f, 0f, 1.8f); L(0.45f, 0f, 0.45f, 0.25f, 1.6f); L(0.65f, 0f, 0.65f, 0.2f, 1.6f); break;
            case "cross": // 의료 십자 + 되돌아가는 화살
                L(-0.2f, -0.6f, -0.2f, 0.2f, 2.6f); L(-0.6f, -0.2f, 0.2f, -0.2f, 2.6f);
                ci.DrawArc(c + new Vector2(0.15f, 0.25f) * s, s * 0.45f, 0.2f, 2.8f, 8, col, 1.2f, true); L(-0.3f, 0.4f, -0.4f, 0.15f, 1.2f); break;
            case "chip": // 칩 + 고리 줄
                Poly(-0.4f, -0.4f, 0.4f, -0.4f, 0.4f, 0.4f, -0.4f, 0.4f);
                for (int i = -1; i <= 1; i++) { L(-0.55f, i * 0.2f, -0.4f, i * 0.2f, 0.9f); L(0.4f, i * 0.2f, 0.55f, i * 0.2f, 0.9f); }
                O(0.7f, 0.6f, 0.15f); L(0.4f, 0.4f, 0.6f, 0.5f, 0.9f); break;
            case "bag": // 꾸린 짐 가방 (손잡이 · 지퍼)
                ci.DrawColoredPolygon(new[] { c + new Vector2(-0.7f, -0.1f) * s, c + new Vector2(0.7f, -0.1f) * s, c + new Vector2(0.8f, 0.5f) * s, c + new Vector2(-0.8f, 0.5f) * s }, col);
                ci.DrawArc(c + new Vector2(0, -0.1f) * s, s * 0.32f, Mathf.Pi, Mathf.Tau, 8, col.Darkened(0.3f), 1.6f, true);
                if (close) { L(-0.6f, 0.05f, 0.6f, 0.05f, 0.7f); ci.DrawCircle(c + new Vector2(0.6f, 0.05f) * s, 1.2f, new Color(0.85f, 0.8f, 0.5f)); }
                break;
            default:
                O(0f, 0f, 0.5f); break;
        }
    }
}
