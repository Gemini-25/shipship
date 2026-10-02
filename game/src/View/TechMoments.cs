using System.Collections.Generic;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.5b 개조 칸과 설치 순간 (읽기만):
///   개조 칸 — 칸막이 · 승무원 안건 공사 · 모듈 설치가 끝난 방은 새 패널로 밝고 깨끗하다(또렷한 판 이음 · 보호 비닐 모서리 · '새 판' 딱지).
///     날이 갈수록 광택이 죽고 긁힘 · 손때가 늘어 12일쯤 지나면 배의 다른 칸과 같아진다 (TechLookTable.Freshness).
///   설치 · 업그레이드 순간 — 설비 이력 줄(달았다 · 단계 올림 · Mk.3 개량 · Mk.1 임시품 · 정품 복원 · 다시 짜 맞춤 · 옮김)의 때를 읽어
///     몇 초 동안: ① 조립(모서리 꺾쇠가 미끄러져 들어오고 비계 점선 · 볼트가 하나씩 박힌다) ② 불꽃(용접 점이 테두리를 돈다)
///     ③ 첫 점등(세 번 떨다 켜지고 빛 고리가 퍼진다) — 종류마다 색 · 끝 표시가 다르다. 방금 익힌 기술도 그 모습이 처음 붙는 자리에서 같은 순간을 겪는다.
/// </summary>
public partial class ShipView
{
    private readonly Dictionary<string, List<Rect2>> _visRects = new();

    private static float Ease(float x) => 1f - (1f - x) * (1f - x) * (1f - x);

    // ═══════════════════════════════ 개조 칸 (정적) ═══════════════════════════════

    private void PaintRemodelFresh(CanvasItem ci)
    {
        var w = _world;
        var ship = w.Ship;
        var shine = new Color(0.86f, 0.93f, 1f);
        foreach (var r in ship.LiveRooms)
        {
            float f = TechLookTable.Freshness(w.Tick, TechLookTable.RemodelSince(w, r));
            if (f <= 0f) continue;
            float age = 1f - f;
            // 바닥: 새 판 광택 + 또렷한 판 이음 (낡을수록 흐려진다)
            foreach (var c in r.Cells)
            {
                if (!ship.IsOpenFloor(c)) continue;
                var rc = CellRect(c);
                ci.DrawRect(rc, shine.WithAlpha(0.06f * f));
                ci.DrawLine(rc.Position + new Vector2(1f, 1f), new Vector2(rc.End.X - 1f, rc.Position.Y + 1f), new Color(1, 1, 1, 0.16f * f), 1f);
                ci.DrawLine(rc.Position + new Vector2(1f, 1f), new Vector2(rc.Position.X + 1f, rc.End.Y - 1f), new Color(1, 1, 1, 0.1f * f), 1f);
                ci.DrawLine(new Vector2(rc.Position.X + 1f, rc.End.Y - 1f), rc.End - new Vector2(1f, 1f), new Color(0, 0, 0, 0.2f * f), 1f);
                // 빛 반사 사선 (새 판만)
                if (f > 0.5f && (c.X * 3 + c.Y) % 5 == 0)
                    ci.DrawLine(rc.Position + new Vector2(6f, T - 6f), rc.Position + new Vector2(T - 6f, 6f), new Color(1, 1, 1, 0.1f * (f - 0.5f) * 2f), 2f, true);
            }
            // 벽: 밝은 새 패널 · 보호 비닐 모서리
            if (_roomFaces.TryGetValue(r.Id, out var faces))
                foreach (var face in faces)
                {
                    ci.DrawColoredPolygon(face.Quad(-15.5f, -7f, 15.5f, -0.5f), TechLook.Light.Lerp(new Color(0.8f, 0.85f, 0.9f), 0.5f).WithAlpha(0.22f * f));
                    ci.DrawLine(face.L(-15.5f, -6.5f), face.L(15.5f, -6.5f), new Color(1, 1, 1, 0.3f * f), 1f);
                    if (f > 0.7f && FixtureArt.Hash(face.Floor.X, face.Floor.Y, 410) < 0.3f)
                        ci.DrawColoredPolygon(new[] { face.L(12f, -6.5f), face.L(15.5f, -6.5f), face.L(15.5f, -2.5f) }, new Color(1, 1, 1, 0.45f * (f - 0.7f) / 0.3f));
                }
            // 낡아 가는 표: 긁힘 · 손때 (새것일수록 적다)
            int scuffs = (int)(age * 9f);
            int n = r.Cells.Count;
            for (int k = 0; k < scuffs && n > 0; k++)
            {
                var c = r.Cells[(int)(FixtureArt.Hash(r.Id, k, 411) * n) % n];
                if (!ship.IsOpenFloor(c)) continue;
                var p = CellRect(c).Position + new Vector2(FixtureArt.Hash(r.Id, k, 412), FixtureArt.Hash(r.Id, k, 413)) * T;
                var d = Vector2.FromAngle(FixtureArt.Hash(r.Id, k, 414) * Mathf.Pi) * (4f + 6f * FixtureArt.Hash(r.Id, k, 415));
                ci.DrawLine(p - d, p + d, new Color(0f, 0f, 0f, 0.18f + 0.1f * age), 1f, true);
            }
            // '새 판' 딱지 (처음 며칠)
            if (f > 0.75f && _roomSlots.TryGetValue(r.Id, out var slots) && slots.Count > 0)
            {
                var s0 = slots[^1];
                var tag = new Rect2(s0.L(0f, 7f) - new Vector2(11f, 4.5f), new Vector2(22f, 9f));
                Gfx.RoundRect(ci, tag, new Color("#d8f0a0").WithAlpha(0.85f * (f - 0.75f) / 0.25f), 2);
                Gfx.TextCentered(ci, Fonts.Bold, tag.GetCenter(), "새 판", 6, new Color("#1a2010"));
            }
        }
    }

    // ═══════════════════════════════ 설치 · 업그레이드 순간 (동적) ═══════════════════════════════

    private void PaintTechMoments(CanvasItem ci)
    {
        var w = _world;
        long now = w.Tick;
        foreach (var f in w.Ship.Furniture)
        {
            if (f.Stowed || f.Room.Detached || f.Machine is not Machine m || m.Marks.Count == 0) continue;
            // 최근 줄만 본다 (오래된 줄을 만나면 멈춘다)
            MomentKind kind = MomentKind.None;
            long tick = -1;
            for (int i = m.Marks.Count - 1; i >= 0; i--)
            {
                var mk = m.Marks[i];
                if (now - mk.Tick >= TechLookTable.MomentTicks) break;
                var k = TechLookTable.Classify(m, mk.Text);
                if (k != MomentKind.None) { kind = k; tick = mk.Tick; break; }
            }
            float ph = TechLookTable.MomentPhase(now, tick);
            if (ph < 0f) continue;
            var r = FurnitureRect(f);
            if (!_fixView.Intersects(r.Grow(T))) continue;
            PaintMoment(ci, r.Grow(-2f), kind, ph, MomentColor(kind, m), f.Id, _time);
        }
        foreach (var (key, tick) in TechLookTable.RecentLearned(w))
        {
            float ph = TechLookTable.MomentPhase(now, tick);
            if (ph < 0f || !_visRects.TryGetValue(key, out var rects)) continue;
            int id = 0;
            foreach (char ch in key) id = unchecked(id * 31 + ch) & 0xFFFF; // 늘 같은 번호 (string.GetHashCode 는 실행마다 다르다)
            for (int i = 0; i < rects.Count && i < 12; i++)
                if (_fixView.Intersects(rects[i])) PaintMoment(ci, rects[i], MomentKind.Learned, ph, TechLook.Field(key), id + i, _time);
        }
    }

    private static Color MomentColor(MomentKind k, Machine m) => k switch
    {
        MomentKind.Upgrade => Hud.TierColor(m.Tier),
        MomentKind.Mk3 => new Color("#5fe0d0"),
        MomentKind.Mk1 => new Color("#e0b64a"),
        MomentKind.Restore => new Color("#e8f4ff"),
        MomentKind.Reassemble => new Color("#ffb347"),
        _ => new Color("#9fe0ff"),
    };

    /// <summary>조립 → 불꽃 → 첫 점등. ph 0~1.</summary>
    internal static void PaintMoment(CanvasItem ci, Rect2 r, MomentKind kind, float ph, Color col, int id, float time)
    {
        var c = r.GetCenter();
        // ① 조립: 모서리 꺾쇠가 밖에서 미끄러져 들어오고, 비계 점선이 걷히고, 볼트가 하나씩 박힌다
        if (ph < 0.45f)
        {
            float e = Ease(ph / 0.45f);
            float off = (1f - e) * 12f;
            var steel = new Color("#c8d0dc");
            for (int k = 0; k < 4; k++)
            {
                float sx = (k & 1) == 0 ? -1f : 1f, sy = (k & 2) == 0 ? -1f : 1f;
                var p = new Vector2(sx < 0 ? r.Position.X : r.End.X, sy < 0 ? r.Position.Y : r.End.Y) + new Vector2(sx, sy) * off;
                ci.DrawLine(p, p - new Vector2(sx * 7f, 0f), steel.WithAlpha(0.9f), 2f, true);
                ci.DrawLine(p, p - new Vector2(0f, sy * 7f), steel.WithAlpha(0.9f), 2f, true);
                if (e * 4f > k + 0.5f) // 박힌 볼트 (막 박힌 것은 번쩍)
                {
                    var b = p - new Vector2(sx * 3f, sy * 3f);
                    float just = Mathf.Clamp(1f - (e * 4f - k - 0.5f) * 2f, 0f, 1f);
                    ci.DrawCircle(b, 1.4f + just * 2f, new Color(1f, 1f, 0.85f, 0.4f + 0.6f * just), true, -1f, true);
                }
            }
            var g = r.Grow(5f);
            for (float x = g.Position.X; x < g.End.X; x += 6f)
            {
                ci.DrawLine(new Vector2(x, g.Position.Y), new Vector2(x + 3f, g.Position.Y), new Color(1f, 0.85f, 0.3f, 0.5f * (1f - e)), 1f);
                ci.DrawLine(new Vector2(x, g.End.Y), new Vector2(x + 3f, g.End.Y), new Color(1f, 0.85f, 0.3f, 0.5f * (1f - e)), 1f);
            }
            for (float y = g.Position.Y; y < g.End.Y; y += 6f)
            {
                ci.DrawLine(new Vector2(g.Position.X, y), new Vector2(g.Position.X, y + 3f), new Color(1f, 0.85f, 0.3f, 0.5f * (1f - e)), 1f);
                ci.DrawLine(new Vector2(g.End.X, y), new Vector2(g.End.X, y + 3f), new Color(1f, 0.85f, 0.3f, 0.5f * (1f - e)), 1f);
            }
        }
        // ② 불꽃: 용접 점이 테두리를 따라 돈다 · 흰 심 · 튀는 불똥이 떨어진다
        if (ph >= 0.2f && ph < 0.7f)
        {
            float q = (ph - 0.2f) / 0.5f;
            float per = 2f * (r.Size.X + r.Size.Y);
            float d = Mathf.PosMod(q * per * 1.5f, per);
            Vector2 p = d < r.Size.X ? r.Position + new Vector2(d, 0f)
                : d < r.Size.X + r.Size.Y ? new Vector2(r.End.X, r.Position.Y + d - r.Size.X)
                : d < 2f * r.Size.X + r.Size.Y ? new Vector2(r.End.X - (d - r.Size.X - r.Size.Y), r.End.Y)
                : new Vector2(r.Position.X, r.End.Y - (d - 2f * r.Size.X - r.Size.Y));
            float flick = 0.7f + 0.3f * FixtureArt.Hash(id, (int)(time * 30f), 420);
            ci.DrawCircle(p, 7f * flick, new Color(1f, 0.9f, 0.6f, 0.18f), true, -1f, true);
            ci.DrawCircle(p, 2.2f, new Color(1f, 1f, 0.95f, 0.95f), true, -1f, true);
            for (int k = 0; k < 6; k++)
            {
                var dir = Vector2.FromAngle(FixtureArt.Hash(id, (int)(time * 20f) * 7 + k, 421) * Mathf.Tau);
                float len = 3f + 7f * FixtureArt.Hash(id, k, 422);
                ci.DrawLine(p + dir * 1.5f, p + dir * len, new Color("#ffe08a").WithAlpha(0.85f), 1f, true);
            }
            for (int k = 0; k < 4; k++) // 떨어지는 불똥
            {
                float fp = Mathf.PosMod(time * 1.8f + k * 0.25f, 1f);
                var fall = p + new Vector2((FixtureArt.Hash(id, k, 423) - 0.5f) * 10f * fp, 14f * fp * fp);
                ci.DrawCircle(fall, 0.9f, new Color("#ff8a3c").WithAlpha(1f - fp), true, -1f, true);
            }
        }
        // ③ 첫 점등: 세 번 떨다 켜지고 빛 고리가 퍼진다 + 종류 표시
        if (ph >= 0.6f)
        {
            float q = (ph - 0.6f) / 0.4f;
            bool on = q > 0.42f || Mathf.PosMod(q * 14f, 2f) > 1.1f;
            if (on) Gfx.RoundRect(ci, r, col.WithAlpha(0.08f + 0.22f * (1f - q)), 4);
            float rad = Mathf.Max(r.Size.X, r.Size.Y) * 0.5f + q * 22f;
            if (q > 0.42f) ci.DrawArc(c, rad, 0f, Mathf.Tau, 40, col.WithAlpha(0.75f * (1f - q)), 2f, true);
            float a = Mathf.Clamp((q - 0.4f) * 3f, 0f, 1f) * (1f - Mathf.Clamp((q - 0.85f) * 6.6f, 0f, 1f));
            if (a <= 0f) return;
            var top = new Vector2(c.X, r.Position.Y - 6f - q * 6f);
            switch (kind)
            {
                case MomentKind.Upgrade: // 오르는 갈매기 둘
                    for (int k = 0; k < 2; k++)
                    {
                        var t0 = top + new Vector2(0f, -k * 4f);
                        ci.DrawPolyline(new[] { t0 + new Vector2(-4f, 3f), t0, t0 + new Vector2(4f, 3f) }, col.WithAlpha(a), 1.6f, true);
                    }
                    break;
                case MomentKind.Mk3: // 네 갈래 반짝임
                    ci.DrawLine(top + new Vector2(-5f, 0f), top + new Vector2(5f, 0f), col.WithAlpha(a), 1.4f, true);
                    ci.DrawLine(top + new Vector2(0f, -5f), top + new Vector2(0f, 5f), col.WithAlpha(a), 1.4f, true);
                    ci.DrawCircle(top, 1.6f, Colors.White.WithAlpha(a), true, -1f, true);
                    break;
                case MomentKind.Mk1: // 테이프 롤
                    ci.DrawArc(top, 3.5f, 0f, Mathf.Tau, 14, col.WithAlpha(a), 2f, true);
                    ci.DrawLine(top + new Vector2(3.5f, 0f), top + new Vector2(8f, 3f), col.WithAlpha(a), 2f, true);
                    break;
                case MomentKind.Restore: // 체크
                    ci.DrawPolyline(new[] { top + new Vector2(-4f, 0f), top + new Vector2(-1f, 3f), top + new Vector2(5f, -3f) }, col.WithAlpha(a), 1.8f, true);
                    break;
                case MomentKind.Reassemble: // 맞물린 화살표
                    ci.DrawArc(top, 4f, 0.3f, 2.8f, 8, col.WithAlpha(a), 1.4f, true);
                    ci.DrawArc(top, 4f, 3.4f, 5.9f, 8, col.WithAlpha(a), 1.4f, true);
                    break;
                case MomentKind.Learned: // 퍼지는 점 여섯
                    for (int k = 0; k < 6; k++)
                        ci.DrawCircle(c + Vector2.FromAngle(k * Mathf.Tau / 6f + q) * rad * 0.8f, 1.5f, col.WithAlpha(a), true, -1f, true);
                    break;
                default: // 플러그
                    ci.DrawRect(new Rect2(top - new Vector2(3f, 2f), new Vector2(6f, 4f)), col.WithAlpha(a));
                    ci.DrawLine(top + new Vector2(-1.5f, -2f), top + new Vector2(-1.5f, -5f), col.WithAlpha(a), 1f);
                    ci.DrawLine(top + new Vector2(1.5f, -2f), top + new Vector2(1.5f, -5f), col.WithAlpha(a), 1f);
                    break;
            }
        }
    }
}
