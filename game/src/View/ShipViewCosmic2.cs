using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v19 우주급 고유 장면 2단계 그림 (22종) — 배 위(세계 좌표)에 그 재난에만 있는 것.
//   양성자 시계 · 오로라 띠 · 회전하는 펄서 빔 · 스윙바이 궤적 · 달아오른 방 · 서리 · 항성풍 줄기 · 혜성 얼음 길 · 불붙은 파편 ·
//   불어나는 파편 · 먼지 안개 · 검은 원 · 떠다니는 컨테이너 · 섬광 · 배기 불꽃 · 기뢰 · 조준선 · 화물선 · 별 없는 밤 ·
//   뒤집힌 비트 · 번개 · 중력파 물결. 그리기는 읽기만 한다 (결정론).

public partial class ShipView
{
    private Vector2 FromSide(CosmicEvent e, float k)
    {
        float half = Bounds.Size.Length() * 0.5f;
        return ShipCenterPx() + new Vector2(Mathf.Cos(e.Side), Mathf.Sin(e.Side)) * half * k;
    }

    private Vector2 ThingPx(CosmicThing t)
    {
        float half = Bounds.Size.Length() * 0.5f;
        return ShipCenterPx() + new Vector2(Mathf.Cos(t.Ang), Mathf.Sin(t.Ang)) * half * t.Dist;
    }

    private void PaintCosmicScenes2(CanvasItem ci, CosmicEvent e)
    {
        var w = _world;
        var cs = w.Cosmic;
        bool impact = e.Phase == CosmicPhase.Impact;
        bool near = e.Phase is CosmicPhase.Brace or CosmicPhase.Impact;
        var c = ShipCenterPx();
        float half = Bounds.Size.Length() * 0.5f;
        var dir = new Vector2(Mathf.Cos(e.Side), Mathf.Sin(e.Side));
        var side = new Vector2(-dir.Y, dir.X);
        switch (e.Kind)
        {
            case CosmicKind.SuperFlare when near:
            {
                // 주황 플레어 고리가 솟는다 · 양성자 시계
                var src = FromSide(e, 1.3f);
                for (int i = 0; i < 3; i++)
                {
                    float r = half * (0.18f + 0.07f * i) * (1f + 0.08f * Mathf.Sin(_time * 2f + i));
                    ci.Arc(src, r, e.Side + Mathf.Pi * 0.55f, e.Side + Mathf.Pi * 1.45f, 18, new Color(1f, 0.6f - 0.12f * i, 0.2f, 0.55f), 3f, true);
                }
                if (impact && e.SceneNext > w.Tick)
                {
                    float min = (e.SceneNext - w.Tick) / (float)SimTime.Minutes(1);
                    Gfx.TextCentered(ci, Fonts.Bold, new Vector2(c.X, Bounds.Position.Y - T * 0.9f), $"양성자 {min:0}분", 18, new Color("#ffb347"));
                }
                foreach (var room in w.Ship.Rooms.Where(r => !r.Detached && cs.Covered(r)))
                {
                    var rr = RoomRect(room).Grow(-T * 0.15f);
                    for (float x = rr.Position.X; x < rr.End.X; x += T * 0.6f) ci.DrawLine(new Vector2(x, rr.Position.Y), new Vector2(x + T * 0.3f, rr.End.Y), new Color(0.35f, 0.55f, 0.3f, 0.35f), 2f);
                }
                break;
            }
            case CosmicKind.CoronalMass when impact:
            {
                // 오로라 띠: 배 위아래로 일렁이는 초록 · 보라 커튼
                foreach (float y0 in new[] { Bounds.Position.Y - T * 0.6f, Bounds.End.Y + T * 0.6f })
                    for (int band = 0; band < 2; band++)
                    {
                        var col = band == 0 ? new Color(0.3f, 1f, 0.55f, 0.32f) : new Color(0.75f, 0.4f, 1f, 0.26f);
                        var pts = new Vector2[24];
                        for (int i = 0; i < pts.Length; i++)
                        {
                            float x = Bounds.Position.X + Bounds.Size.X * i / (pts.Length - 1f);
                            pts[i] = new Vector2(x, y0 + Mathf.Sin(x * 0.012f + _time * (1.2f + band) + band) * T * 0.7f);
                        }
                        ci.Polyline(pts, col, T * 0.35f, true);
                    }
                break;
            }
            case CosmicKind.PulsarBeam when impact:
            {
                // 배를 쓸고 도는 빔 — 빔 속일 때 밝다
                bool on = cs.PulseNow(e);
                float a = _time * 1.7f;
                var d2 = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                ci.DrawLine(c - d2 * half * 1.3f, c + d2 * half * 1.3f, new Color(0.7f, 0.9f, 1f, on ? 0.75f : 0.12f), on ? T * 0.6f : 3f, true);
                if (on) ci.Box(Bounds, new Color(0.75f, 0.9f, 1f, 0.08f), true);
                Gfx.TextCentered(ci, Fonts.Bold, new Vector2(c.X, Bounds.Position.Y - T * 0.9f), on ? "빔 — 엎드려라" : $"다음 빔 {cs.PulseWait(e):0}분", 18, new Color("#a8d8ff"));
                break;
            }
            case CosmicKind.NeutronStar when near:
            {
                var star = FromSide(e, 1.25f);
                CosmicArt.Glow(ci, star, T * 1.6f, new Color("#9fc8ff"), 5, 0.12f);
                ci.Circle(star, T * 0.35f, new Color("#e8f4ff"), true, -1f, true);
                ci.Arc(star, T * 2.4f, 0f, Mathf.Tau, 32, new Color(0.6f, 0.75f, 1f, 0.35f), 2f, true); // 휘는 별빛 고리
                if (e.SceneChoice == 1) // 바짝 스쳐 도는 궤적
                    ci.Arc(star, half * 0.55f, e.Side + Mathf.Pi * 0.6f, e.Side + Mathf.Pi * 1.4f, 24, new Color("#ffd166").WithAlpha(0.7f), 2f, true);
                break;
            }
            case CosmicKind.RedGiantShell when impact:
                foreach (var room in w.Ship.Rooms)
                {
                    if (room.Detached || room.Air.Temperature < 28f) continue;
                    float k = Mathf.Clamp((room.Air.Temperature - 28f) / 20f, 0f, 1f);
                    var rr = RoomRect(room);
                    ci.Box(rr, new Color(1f, 0.3f, 0.15f, 0.08f + 0.18f * k), true);
                    for (int i = 0; i < 3; i++) // 아지랑이
                    {
                        float y = rr.Position.Y + rr.Size.Y * (0.25f + 0.25f * i);
                        var pts = new Vector2[8];
                        for (int j = 0; j < 8; j++) { float x = rr.Position.X + rr.Size.X * j / 7f; pts[j] = new Vector2(x, y + Mathf.Sin(x * 0.15f + _time * 4f + i) * 2.5f); }
                        ci.Polyline(pts, new Color(1f, 0.7f, 0.4f, 0.25f * k), 1.5f, true);
                    }
                }
                break;
            case CosmicKind.BinaryEclipse when impact:
                foreach (var room in w.Ship.Rooms)
                {
                    if (room.Detached || room.Air.Temperature > 6f) continue;
                    var rr = RoomRect(room);
                    float k = Mathf.Clamp((6f - room.Air.Temperature) / 16f, 0.2f, 1f);
                    for (int i = 0; i < 10; i++) // 서리 결정
                    {
                        var p = rr.Position + new Vector2(CosmicArt.N(room.Id, i), i % 2 == 0 ? 0.04f : 0.96f) * rr.Size;
                        float s = T * 0.25f * k;
                        ci.DrawLine(p - new Vector2(s, 0), p + new Vector2(s, 0), new Color(0.85f, 0.95f, 1f, 0.7f), 1f);
                        ci.DrawLine(p - new Vector2(s * 0.5f, s * 0.8f), p + new Vector2(s * 0.5f, s * 0.8f), new Color(0.85f, 0.95f, 1f, 0.7f), 1f);
                        ci.DrawLine(p - new Vector2(-s * 0.5f, s * 0.8f), p + new Vector2(-s * 0.5f, s * 0.8f), new Color(0.85f, 0.95f, 1f, 0.7f), 1f);
                    }
                    if (e.SceneRooms.Contains(room.Id)) Gfx.TextCentered(ci, Fonts.Bold, rr.GetCenter(), "관 터짐", 12, new Color("#5ec8e6"));
                }
                break;
            case CosmicKind.WolfRayetWind when impact:
            {
                // 바람 쪽에서 흘러드는 청백색 줄기
                for (int i = 0; i < 14; i++)
                {
                    float t = (_time * 0.6f + CosmicArt.N(e.Id, i)) % 1f;
                    var start = c + dir * half * (1.3f - 2.4f * t) + side * half * (CosmicArt.N(e.Id, i + 40) - 0.5f) * 1.6f;
                    ci.DrawLine(start, start - dir * T * 2.2f, new Color(0.7f, 0.85f, 1f, 0.45f), 2f, true);
                }
                foreach (var room in w.Ship.Rooms.Where(r => !r.Detached && cs.Plated(r)))
                    ci.Box(RoomRect(room).Grow(-T * 0.1f), new Color("#c9b27a"), false, 3f);
                break;
            }
            case CosmicKind.CometCore when near:
            {
                var core = FromSide(e, 1.35f);
                ci.Circle(core, T * 0.5f, new Color("#d8f4ff"), true, -1f, true);
                CosmicArt.Glow(ci, core, T * 1.4f, new Color("#9be7ff"), 4, 0.1f);
                ci.DrawLine(core, core + dir * half * 0.8f, new Color(0.6f, 0.85f, 1f, 0.3f), T * 0.4f, true); // 이온 꼬리
                if (impact && e.SceneChoice == 1 && w.Ship.FurnitureOf(FurnitureType.DroneDock).FirstOrDefault(f => !f.Room.Detached) is Furniture dock)
                {
                    var dp = CellRect(dock.Cells[0]).GetCenter();
                    for (int i = 0; i < 8; i++) // 얼음 반짝임이 거치대로
                    {
                        float t = (_time * 0.4f + i / 8f) % 1f;
                        ci.Circle(core.Lerp(dp, t), 2.5f, new Color(0.85f, 0.97f, 1f, 0.8f), true, -1f, true);
                    }
                }
                break;
            }
            case CosmicKind.ShatteredPlanet when impact:
                for (int i = 0; i < 12; i++)
                {
                    float t = (_time * 0.25f + CosmicArt.N(e.Id, i)) % 1f;
                    var p = c + dir * half * (1.4f - 2.6f * t) + side * half * (CosmicArt.N(e.Id, i + 30) - 0.5f) * 1.5f;
                    ci.Circle(p, 3f + 3f * CosmicArt.N(e.Id, i + 60), new Color(1f, 0.5f, 0.2f, 0.85f), true, -1f, true);
                    ci.DrawLine(p, p + dir * T * 0.9f, new Color(1f, 0.6f, 0.3f, 0.35f), 2f, true);
                }
                break;
            case CosmicKind.Kessler when impact:
            {
                int n = Math.Min(40, (int)(6f * Math.Max(1f, e.SceneLevel)));
                for (int i = 0; i < n; i++)
                {
                    float t = (_time * (0.5f + 0.4f * CosmicArt.N(e.Id, i)) + CosmicArt.N(e.Id, i + 7)) % 1f;
                    float a = CosmicArt.N(e.Id, i + 13) * Mathf.Tau;
                    var d2 = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    var p = c + d2 * half * (1.5f - 3f * t) + new Vector2(-d2.Y, d2.X) * half * (CosmicArt.N(e.Id, i + 21) - 0.5f);
                    ci.DrawLine(p, p - d2 * T * 0.8f, new Color(0.85f, 0.85f, 0.9f, 0.55f), 1.5f, true);
                }
                if (e.SceneLevel > 0f) Gfx.TextCentered(ci, Fonts.Bold, new Vector2(c.X, Bounds.Position.Y - T * 0.9f), e.Avoided ? "파편 지대를 빠져나왔다" : $"파편 밀도 ×{e.SceneLevel:0.0}", 16, new Color("#d0d0dc"));
                break;
            }
            case CosmicKind.HyperDust:
            {
                if (impact)
                    for (int i = 0; i < 60; i++)
                    {
                        float t = (_time * 0.8f + CosmicArt.N(e.Id, i)) % 1f;
                        var p = c + dir * half * (1.3f - 2.6f * t) + side * half * (CosmicArt.N(e.Id, i + 70) - 0.5f) * 1.8f;
                        ci.Circle(p, 1.4f, new Color(0.85f, 0.8f, 0.7f, 0.55f), true);
                    }
                foreach (var room in w.Ship.Rooms.Where(r => !r.Detached && cs.Frosted(r)))
                    ci.Box(RoomRect(room).Grow(-T * 0.05f), new Color(0.95f, 0.95f, 0.92f, 0.5f), false, 4f); // 젖빛 창
                break;
            }
            case CosmicKind.RoguePlanet when near:
            {
                var p = FromSide(e, 1.55f);
                float r = half * 0.55f;
                ci.Circle(p, r, new Color(0.02f, 0.02f, 0.04f, 0.92f), true, -1f, true); // 별을 가리는 검은 원
                ci.Arc(p, r, 0f, Mathf.Tau, 48, new Color(0.45f, 0.5f, 0.7f, 0.35f), 2f, true);
                for (int i = 0; i < 5; i++) // 작은 위성 부스러기
                {
                    float a = _time * 0.2f + i * 1.3f;
                    ci.Circle(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * 1.2f, 3f, new Color("#8b7a66"), true);
                }
                break;
            }
            case CosmicKind.StationCollapse when near:
            {
                var st = FromSide(e, 1.45f);
                for (int i = 0; i < 6; i++) // 부서진 바퀴살
                {
                    float a = _time * 0.15f + i * Mathf.Tau / 6f;
                    if (i % 2 == 1) continue;
                    ci.Arc(st, T * 2.2f, a, a + 0.7f, 8, new Color("#c0c8d8"), 4f, true);
                }
                foreach (var t in e.SceneThings)
                {
                    var p = ThingPx(t);
                    var col = t.State == 1 ? new Color("#7bd88f") : t.State == 2 ? new Color(0.6f, 0.6f, 0.6f, 0.5f) : new Color("#ffb347");
                    ci.Box(new Rect2(p - new Vector2(T * 0.35f, T * 0.25f), new Vector2(T * 0.7f, T * 0.5f)), col, true);
                }
                break;
            }
            case CosmicKind.AntimatterBreach when impact:
            {
                float since = (w.Tick - e.Arrive) / (float)SimTime.Minutes(1);
                float k = Mathf.Clamp(1f - since / 6f, 0f, 1f);
                if (k > 0f)
                {
                    ci.Box(Bounds.Grow(T * 6f), new Color(1f, 1f, 1f, 0.55f * k), true);
                    ci.Arc(FromSide(e, 1.4f), half * (1.2f - k), 0f, Mathf.Tau, 48, new Color(1f, 1f, 1f, 0.8f * k), 4f, true);
                }
                break;
            }
            case CosmicKind.FusionRunaway when near:
            {
                foreach (var room in w.Ship.Rooms.Where(r => !r.Detached && cs.Scorching(r)))
                {
                    var rr = RoomRect(room).Grow(-T * 0.08f);
                    ci.Box(rr, new Color(1f, 0.35f, 0.2f, 0.7f), false, 3f);
                    Gfx.TextCentered(ci, Fonts.Bold, rr.GetCenter(), "비운다", 12, new Color("#ff7a50"));
                }
                if (impact) // 배기 불꽃 원뿔
                {
                    var src = FromSide(e, 1.6f);
                    float sweep = Mathf.Sin(_time * 1.5f) * 0.25f;
                    var d2 = (-dir).Rotated(sweep);
                    var s2 = new Vector2(-d2.Y, d2.X);
                    ci.Poly(new[] { src, src + d2 * half * 1.3f + s2 * half * 0.35f, src + d2 * half * 1.3f - s2 * half * 0.35f }, new Color(0.4f, 0.7f, 1f, 0.22f));
                }
                break;
            }
            case CosmicKind.MineField when impact:
                foreach (var t in e.SceneThings)
                {
                    var p = ThingPx(t);
                    if (t.State == 2)
                    {
                        float since = (w.Tick - t.At) / (float)SimTime.Minutes(1);
                        if (since < 4f) ci.Arc(p, T * (0.5f + since), 0f, Mathf.Tau, 20, new Color(1f, 0.6f, 0.2f, 1f - since / 4f), 3f, true);
                        continue;
                    }
                    bool blink = t.State == 0 && ((int)(_time * 3f) % 2 == 0);
                    var col = t.State == 1 ? new Color(0.55f, 0.55f, 0.55f) : blink ? new Color("#ff3b3b") : new Color("#7a1a1a");
                    ci.Circle(p, T * 0.3f, col, true, -1f, true);
                    for (int k = 0; k < 6; k++) { float a = k * Mathf.Tau / 6f; ci.DrawLine(p, p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * T * 0.48f, col, 2f); }
                }
                break;
            case CosmicKind.OrbitalEmp when near:
            {
                var src = FromSide(e, 1.7f);
                if (e.SceneChoice == 1)
                {
                    float r = half * (0.75f + 0.05f * Mathf.Sin(_time * 3f));
                    ci.Arc(c, r, 0f, Mathf.Tau, 48, new Color("#7bd88f").WithAlpha(0.5f), 2f, true); // 식별 신호
                }
                else
                {
                    for (int i = 0; i < 6; i++) // 점선 조준선
                    {
                        float t0 = i / 6f, t1 = t0 + 0.08f;
                        ci.DrawLine(src.Lerp(c, t0), src.Lerp(c, t1), new Color(1f, 0.3f, 0.3f, 0.7f), 2f, true);
                    }
                    ci.Arc(c, T * 1.2f, 0f, Mathf.Tau, 24, new Color(1f, 0.3f, 0.3f, 0.6f), 2f, true);
                }
                break;
            }
            case CosmicKind.Freighter when near:
            {
                float approach = e.Phase == CosmicPhase.Impact ? 1.05f : 1.6f;
                var p = FromSide(e, approach);
                var rot = e.Side + Mathf.Pi * 0.5f;
                var u = new Vector2(Mathf.Cos(rot), Mathf.Sin(rot));
                var v = new Vector2(-u.Y, u.X);
                float L = half * 0.7f, H = half * 0.16f;
                ci.Poly(new[] { p - u * L - v * H, p + u * L - v * H, p + u * L * 1.1f, p + u * L + v * H, p - u * L + v * H }, new Color(0.25f, 0.27f, 0.32f, 0.9f));
                for (int i = 0; i < 5; i++) ci.Box(new Rect2(p - u * L * 0.8f + u * L * 0.35f * i - v * H * 0.6f, new Vector2(T * 0.6f, T * 0.6f)), new Color("#6b5636"), true);
                if (e.SceneChoice is 1 or 2 && w.Ship.FurnitureOf(FurnitureType.DroneDock).FirstOrDefault(f => !f.Room.Detached) is Furniture dock)
                    ci.DrawLine(CellRect(dock.Cells[0]).GetCenter(), p, new Color("#ffd166").WithAlpha(0.7f), 2f, true); // 견인줄
                break;
            }
            case CosmicKind.DarkNebula when impact:
            {
                ci.Box(Bounds.Grow(T * 8f), new Color(0f, 0f, 0.02f, 0.35f), true); // 별이 사라진 밤
                if (e.SceneChoice == 1)
                    foreach (var room in w.Ship.Rooms.Where(r => !r.Detached && r.Type == RoomType.Mess))
                        CosmicArt.Glow(ci, RoomRect(room).GetCenter(), T * 2.5f, new Color("#ffcf8a"), 5, 0.08f); // 등불
                break;
            }
            case CosmicKind.CosmicRayShower when impact:
            {
                int b = (int)(_time * 6f);
                for (int i = 0; i < 10; i++)
                {
                    float a = CosmicArt.N(b, i) * Mathf.Tau;
                    var d2 = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    var p = c + new Vector2(CosmicArt.N(b, i + 5) - 0.5f, CosmicArt.N(b, i + 9) - 0.5f) * Bounds.Size;
                    ci.DrawLine(p - d2 * T * 1.5f, p + d2 * T * 1.5f, new Color(0.9f, 0.95f, 1f, 0.6f), 1f, true);
                }
                foreach (int id in e.SceneRooms.Distinct())
                    if (id < w.Ship.Rooms.Count && !w.Ship.Rooms[id].Detached)
                        Gfx.TextCentered(ci, Fonts.Bold, RoomRect(w.Ship.Rooms[id]).GetCenter(), "?", 20, new Color("#a8d8ff"));
                break;
            }
            case CosmicKind.IonNebula when impact:
            {
                float k = Mathf.Clamp(e.SceneLevel, 0f, 1f);
                ci.Box(Bounds.Grow(T * 0.3f), new Color(0.4f, 0.6f, 1f, 0.15f + 0.5f * k), false, 3f + 4f * k); // 쌓인 전하
                if (cs.FlashNow > 0.3f)
                {
                    var a = Bounds.Position + new Vector2(CosmicArt.N((int)(_time * 4f), 1) * Bounds.Size.X, 0f);
                    var pts = new Vector2[7];
                    for (int i = 0; i < 7; i++) pts[i] = a + new Vector2((CosmicArt.N((int)(_time * 20f), i) - 0.5f) * T * 2f, Bounds.Size.Y * i / 6f);
                    ci.Polyline(pts, new Color(0.8f, 0.9f, 1f, 0.9f), 2.5f, true);
                }
                if (cs.Grounded && w.Ship.RoomsOf(RoomType.Airlock).FirstOrDefault(r => !r.Detached) is Room air)
                {
                    var rr = RoomRect(air);
                    ci.DrawLine(new Vector2(rr.GetCenter().X, rr.Position.Y), new Vector2(rr.GetCenter().X, rr.Position.Y - T * 2.5f), new Color("#c9b27a"), 3f); // 방전 막대
                }
                break;
            }
            case CosmicKind.GravityWave when impact:
            {
                var src = FromSide(e, 2f);
                for (int i = 0; i < 5; i++)
                {
                    float r = half * ((_time * 0.35f + i * 0.4f) % 2.6f + 0.6f);
                    ci.Arc(src, r, e.Side + Mathf.Pi * 0.65f, e.Side + Mathf.Pi * 1.35f, 28, new Color(0.75f, 0.7f, 1f, 0.35f), 2.5f, true);
                }
                break;
            }
        }
    }
}
