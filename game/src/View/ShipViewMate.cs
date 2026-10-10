using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.27 주컴퓨터 — 배의 한 구성원 (배 화면 — 읽기만):
///  · 늘린 장비 (모양이 서로 다르다): 코어 랙(본체 옆 유리문 캐비닛 · 푸른 칼날 불 · 굵은 케이블 다발) · 감지기(천장 돔 둘 — 훑는 눈 · 짝끼리 점선) ·
///    새 데이터선(벽을 따라 주황 띠 감긴 굵은 관 · 고정 쇠 · 흐르는 빛) · 방진 받침(설비 밑 노랑 · 검정 고무 받침 · 스프링).
///  · 설치 장면: 열린 천장 판 · 사다리 · 케이블 감개 · 튀는 불꽃 · 진척 눈금 (장비마다 다른 공구).
///  · 정찰: 감지기가 안 닿는 방엔 회색 물결 무늬 · 로봇이 가면 눈 모양 표시와 퍼지는 고리 · 드론은 창밖에서 원뿔 빛.
///  · 훈련: 가정한 방에 점선 불꽃 테두리 · 역할 자리마다 깃발(소화 · 격벽 · 전력 · 의료 · 선외 · 안내) · 초시계.
///  · 정비표: 오늘 손볼 설비에 시각 꼬리표와 커지는 떨림 곡선 · 끝내면 초록 체크.
///  · 식당 화면: 아침 방송 다섯 칸 (공구 · 해 · 세모 · 시계 · 상자) — 갈래마다 다른 그림.
///  · 딜레마: 닫은 문엔 노랑 · 검정 빗금 · 함장에게 물을 땐 컴퓨터실에서 함장까지 물음 점선.
///  · 안부: 부탁받은 동료 머리 위에 접힌 쪽지 · 전하고 나면 작은 하트 둘.
///  · 날씨 보류: 에어락 문에 폭풍 표지 · 기억 검사: 랙 위를 훑는 줄 · 기억이 틀어진 동안 랙 한 칸이 어긋나 깜빡인다.
/// </summary>
public partial class ShipView
{
    private static readonly Color MateTeal = new("#58d6c4");
    private static readonly Color MateOrange = new("#ff9f43");
    private static readonly Color MateHazard = new("#f4c430");

    private void PaintMateWorld(CanvasItem ci)
    {
        if (_world.Automation.MateOrNull is not ShipMate m) return;
        PaintMateGear(ci, m);
        PaintMateScouts(ci, m);
        PaintMateDrill(ci, m);
        PaintMateUpkeep(ci, m);
        PaintMateBriefScreen(ci, m);
        PaintMateDilemma(ci, m);
        PaintMateCare(ci, m);
        PaintMateSky(ci, m);
        PaintMateMemory(ci, m);
    }

    private Room? MateRoom(int id) { foreach (var r in _world.Ship.Rooms) if (r.Id == id) return r; return null; }
    private static Rect2 RoomPx(Room r) => new(r.MinX * T, r.MinY * T, (r.MaxX - r.MinX + 1) * T, (r.MaxY - r.MinY + 1) * T);

    // ───────────── 늘린 장비 · 설치 장면 ─────────────

    private void PaintMateGear(CanvasItem ci, ShipMate m)
    {
        foreach (var u in m.Upgrades)
        {
            if (u.State is not (GearState.Done or GearState.Working or GearState.Approved)) continue;
            if (MateRoom(u.RoomId) is not Room r || r.Detached) continue;
            bool done = u.State == GearState.Done;
            float prog = Mathf.Clamp(u.Done / Mathf.Max(0.1f, u.Need), 0f, 1f);
            switch (u.Kind)
            {
                case MateGear.CoreRack: PaintCoreRack(ci, u, done, prog); break;
                case MateGear.SensorNode: PaintSensorNodes(ci, r, done, prog); break;
                case MateGear.DataLine: PaintNewDataLine(ci, r, done, prog); break;
                default: PaintMounts(ci, r, done, prog); break;
            }
            if (!done) PaintInstallScene(ci, u, prog);
        }
    }

    /// <summary>코어 랙: 유리문 캐비닛 · 푸른 칼날 불 · 본체로 가는 굵은 케이블 다발.</summary>
    private void PaintCoreRack(CanvasItem ci, GearUpgrade u, bool done, float prog)
    {
        var c = ToPx(u.Spot.Center);
        var box = new Rect2(c + new Vector2(-T * 0.38f, -T * 0.45f), new Vector2(T * 0.76f, T * 0.9f));
        float h = done ? 1f : prog;
        // 몸통 (설치 중엔 아래부터 차오른다)
        var body = new Rect2(box.Position + new Vector2(0, box.Size.Y * (1f - h)), new Vector2(box.Size.X, box.Size.Y * h));
        ci.Box(body, new Color("#141c28"));
        ci.Box(box, new Color("#4b5a72"), false, 1.2f);
        // 칼날 여섯 · 푸른 불이 차례로
        for (int k = 0; k < 6; k++)
        {
            float y = box.Position.Y + 3f + k * (box.Size.Y - 6f) / 6f;
            if (y < body.Position.Y) continue;
            ci.Box(new Rect2(box.Position.X + 3f, y, box.Size.X - 6f, (box.Size.Y - 6f) / 6f - 1.5f), new Color("#1f2b3d"));
            bool on = done && Mathf.PosMod(_time * 3f - k * 0.4f, 2.4f) < 1.2f;
            ci.Circle(new Vector2(box.End.X - 5f, y + 2.2f), 1f, new Color("#7fb8ff").WithAlpha(on ? 1f : 0.25f), true, -1f, true);
        }
        // 유리문 반사
        ci.DrawLine(box.Position + new Vector2(4f, 2f), box.Position + new Vector2(10f, box.Size.Y - 3f), new Color(1f, 1f, 1f, 0.12f), 2f, true);
        // 손잡이
        ci.DrawLine(new Vector2(box.Position.X + 2f, box.GetCenter().Y - 4f), new Vector2(box.Position.X + 2f, box.GetCenter().Y + 4f), new Color("#9aa6b8"), 1.4f, true);
        // 본체로 가는 케이블 다발 (세 가닥 · 흐르는 빛)
        if (_compBody != null && done)
        {
            var to = ToPx(_compBody.Center);
            for (int k = -1; k <= 1; k++)
            {
                var a0 = new Vector2(box.GetCenter().X + k * 2f, box.End.Y);
                var mid = (a0 + to) * 0.5f + new Vector2(0, T * 0.35f);
                ci.Polyline(new[] { a0, mid, to + new Vector2(k * 2f, 0) }, new Color("#2c3646"), 1.6f, true);
                float q = Mathf.PosMod(_time * 0.8f + k * 0.3f, 1f);
                var dot = q < 0.5f ? a0.Lerp(mid, q * 2f) : mid.Lerp(to, (q - 0.5f) * 2f);
                ci.Circle(dot, 1.2f, new Color("#7fb8ff").WithAlpha(0.8f), true, -1f, true);
            }
        }
    }

    /// <summary>감지기 증설: 천장 돔 둘 (훑는 눈) · 짝끼리 점선 — 하나가 틀어져도 다른 하나가 본다.</summary>
    private void PaintSensorNodes(CanvasItem ci, Room r, bool done, float prog)
    {
        var rp = RoomPx(r);
        var p1 = rp.Position + new Vector2(rp.Size.X * 0.25f, 7f);
        var p2 = rp.Position + new Vector2(rp.Size.X * 0.75f, 7f);
        int n = done ? 2 : prog > 0.5f ? 1 : 0;
        var pts = new[] { p1, p2 };
        for (int i = 0; i < 2; i++)
        {
            var p = pts[i];
            if (i >= n) { ci.Arc(p, 3.5f, Mathf.Pi, Mathf.Tau, 10, new Color("#5c6b80").WithAlpha(0.6f), 0.8f, true); continue; } // 비어 있는 받침
            ci.Box(new Rect2(p + new Vector2(-4.5f, -2.5f), new Vector2(9f, 2f)), new Color("#3a4558"));
            ci.Arc(p, 4f, 0f, Mathf.Pi, 12, new Color("#9fb2c8"), 1.2f, true);
            ci.Circle(p + new Vector2(0, 1.6f), 2.6f, new Color("#1a2230"), true, -1f, true);
            float sweep = Mathf.Sin(_time * 1.6f + i * 1.3f) * 1.8f;
            ci.Circle(p + new Vector2(sweep, 1.8f), 1f, MateTeal, true, -1f, true);
            // 훑는 부채꼴
            if (done)
            {
                float ang = Mathf.Pi * 0.5f + Mathf.Sin(_time * 1.6f + i * 1.3f) * 0.6f;
                ci.DrawLine(p, p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * T * 0.9f, MateTeal.WithAlpha(0.18f), 3f, true);
            }
        }
        if (done) ci.DrawDashedLine(p1 + new Vector2(5f, 0), p2 - new Vector2(5f, 0), MateTeal.WithAlpha(0.35f), 0.8f, 3f, true, true);
    }

    /// <summary>새 데이터선: 벽을 따라 주황 띠 감긴 굵은 관 · 고정 쇠 · 흐르는 빛.</summary>
    private void PaintNewDataLine(CanvasItem ci, Room r, bool done, float prog)
    {
        var rp = RoomPx(r);
        float y = rp.Position.Y + 3.5f;
        float x0 = rp.Position.X + 3f, x1 = rp.End.X - 3f;
        float xe = done ? x1 : Mathf.Lerp(x0, x1, prog);
        ci.DrawLine(new Vector2(x0, y), new Vector2(xe, y), new Color("#22303f"), 3.2f, true);
        // 주황 띠 (차폐 표시) · 고정 쇠
        for (float x = x0 + 4f; x < xe; x += 9f)
        {
            ci.DrawLine(new Vector2(x, y - 1.6f), new Vector2(x + 1.5f, y + 1.6f), MateOrange, 1f, true);
            if ((int)((x - x0) / 9f) % 3 == 0) ci.Box(new Rect2(x - 1f, y - 2.6f, 2f, 5.2f), new Color("#8d97a6"));
        }
        if (!done)
        {
            // 늘어진 끝 (아직 안 이은 선)
            ci.Polyline(new[] { new Vector2(xe, y), new Vector2(xe + 4f, y + 5f), new Vector2(xe + 2f, y + 9f) }, new Color("#22303f"), 2.4f, true);
            return;
        }
        float q = Mathf.PosMod(_time * 0.5f, 1f);
        ci.Circle(new Vector2(Mathf.Lerp(x0, x1, q), y), 1.3f, new Color("#ffd29a"), true, -1f, true);
    }

    /// <summary>방진 받침: 설비 밑 노랑 · 검정 고무 받침 · 스프링.</summary>
    private void PaintMounts(CanvasItem ci, Room r, bool done, float prog)
    {
        int k = 0;
        foreach (var f in r.Furniture)
        {
            if (f.Machine == null) continue;
            if (!done && k >= Mathf.CeilToInt(prog * 4f)) break;
            k++;
            var c = ToPx(f.Center);
            float half = T * 0.42f;
            for (int s = -1; s <= 1; s += 2)
            {
                var foot = new Rect2(c + new Vector2(s * half - 3f, T * 0.38f), new Vector2(6f, 3f));
                ci.Box(foot, new Color("#1b1b1b"));
                ci.DrawLine(foot.Position + new Vector2(1f, 3f), foot.Position + new Vector2(3f, 0f), MateHazard, 1f, true);
                ci.DrawLine(foot.Position + new Vector2(3.5f, 3f), foot.Position + new Vector2(5.5f, 0f), MateHazard, 1f, true);
                // 스프링 지그재그
                var sp = new List<Vector2>();
                for (int z = 0; z < 5; z++) sp.Add(foot.Position + new Vector2(z % 2 == 0 ? 1f : 5f, -1f - z * 1.1f));
                ci.Polyline(sp.ToArray(), new Color("#c0c8d4"), 0.7f, true);
            }
        }
    }

    /// <summary>설치 장면: 장비마다 다른 공구 — 랙(손수레 · 나사), 감지기(사다리 · 열린 천장 판), 데이터선(케이블 감개), 받침(잭).</summary>
    private void PaintInstallScene(CanvasItem ci, GearUpgrade u, float prog)
    {
        var c = ToPx(u.Spot.Center);
        bool working = u.State == GearState.Working && u.Workers.Count > 0;
        // 진척 눈금
        var bar = new Rect2(c + new Vector2(-T * 0.4f, -T * 0.7f), new Vector2(T * 0.8f, 3f));
        ci.Box(bar, new Color(0f, 0f, 0f, 0.55f));
        ci.Box(new Rect2(bar.Position, new Vector2(bar.Size.X * prog, bar.Size.Y)), MateHazard);
        switch (u.Kind)
        {
            case MateGear.SensorNode:
                // 사다리 + 열린 천장 판
                var lb = c + new Vector2(T * 0.3f, T * 0.4f);
                ci.DrawLine(lb, lb + new Vector2(-3f, -T * 0.8f), new Color("#c9a26b"), 1.2f, true);
                ci.DrawLine(lb + new Vector2(5f, 0), lb + new Vector2(2f, -T * 0.8f), new Color("#c9a26b"), 1.2f, true);
                for (int k = 1; k < 5; k++) ci.DrawLine(lb + new Vector2(-k * 0.6f, -k * T * 0.16f), lb + new Vector2(5f - k * 0.6f, -k * T * 0.16f), new Color("#c9a26b"), 0.8f, true);
                ci.Box(new Rect2(c + new Vector2(-6f, -T * 0.55f), new Vector2(12f, 4f)), new Color("#0d1117"));
                break;
            case MateGear.DataLine:
                // 케이블 감개 (돌아간다)
                var sc = c + new Vector2(-T * 0.3f, T * 0.25f);
                ci.Circle(sc, 5f, new Color("#5a4632"), true, -1f, true);
                ci.Circle(sc, 3.2f, MateOrange.Darkened(0.2f), true, -1f, true);
                float ang = working ? _time * 4f : 0f;
                ci.DrawLine(sc, sc + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 4.5f, new Color("#2b2118"), 1f, true);
                break;
            case MateGear.CoreRack:
                // 손수레에 실린 칼날 상자 · 나사 몇 개
                var cart = new Rect2(c + new Vector2(T * 0.35f, T * 0.1f), new Vector2(10f, 6f));
                ci.Box(cart, new Color("#6b7686"));
                ci.Circle(cart.Position + new Vector2(2f, 7f), 1.4f, new Color("#222"), true, -1f, true);
                ci.Circle(cart.Position + new Vector2(8f, 7f), 1.4f, new Color("#222"), true, -1f, true);
                for (int k = 0; k < 3; k++) ci.Circle(c + new Vector2(-T * 0.2f + k * 3f, T * 0.4f), 0.7f, new Color("#c8ced8"), true, -1f, true);
                break;
            default:
                // 설비를 들어 올리는 잭
                var jb = c + new Vector2(-T * 0.45f, T * 0.4f);
                ci.Box(new Rect2(jb, new Vector2(6f, 2.5f)), new Color("#c0392b"));
                ci.DrawLine(jb + new Vector2(3f, 0), jb + new Vector2(3f, -5f - 2f * Mathf.Sin(_time * 3f)), new Color("#c8ced8"), 1.2f, true);
                break;
        }
        // 튀는 불꽃 (일하는 동안만)
        if (working && Mathf.PosMod(_time * 2.3f, 1f) < 0.35f)
            for (int k = 0; k < 4; k++)
            {
                float a = _time * 9f + k * 1.7f;
                ci.DrawLine(c, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a) - 0.5f) * (3f + k), new Color("#ffe08a"), 0.8f, true);
            }
    }

    // ───────────── 정찰 ─────────────

    private void PaintMateScouts(CanvasItem ci, ShipMate m)
    {
        var w = _world;
        foreach (var s in m.Scouts)
        {
            if (MateRoom(s.RoomId) is not Room r || r.Detached) continue;
            bool going = s.Seen < 0 && !s.Failed && w.Tick - s.Sent < SimTime.Hours(3);
            bool justSaw = s.Seen >= 0 && w.Tick - s.Seen < SimTime.Minutes(20);
            if (!going && !justSaw) continue;
            var rp = RoomPx(r);
            if (going)
            {
                // 못 보는 방: 회색 물결 무늬
                for (float y = rp.Position.Y + 6f; y < rp.End.Y; y += 9f)
                {
                    var pts = new Vector2[8];
                    for (int i = 0; i < 8; i++) pts[i] = new Vector2(rp.Position.X + i * rp.Size.X / 7f, y + Mathf.Sin(_time * 2f + i + y) * 1.2f);
                    ci.Polyline(pts, new Color(0.6f, 0.65f, 0.72f, 0.18f), 1f, true);
                }
                if (s.Drone)
                {
                    // 드론이 창밖에서 비추는 원뿔 빛
                    var tip = new Vector2(rp.Position.X - T * 0.6f, rp.GetCenter().Y);
                    var cone = new[] { tip, new Vector2(rp.Position.X + T * 0.6f, rp.GetCenter().Y - T * 0.6f), new Vector2(rp.Position.X + T * 0.6f, rp.GetCenter().Y + T * 0.6f) };
                    ci.Poly(cone, new Color(0.7f, 0.9f, 1f, 0.12f + 0.05f * Mathf.Sin(_time * 4f)));
                    ci.Circle(tip, 2.5f, new Color("#cfe8ff"), true, -1f, true);
                }
                else if (w.Robots.Robots.FirstOrDefault(x => x.Id == s.RobotId) is Robot rb)
                {
                    var p = ToPx(rb.Position) + new Vector2(0, -T * 0.45f);
                    PaintEye(ci, p, MateTeal);
                }
            }
            else
            {
                // 봤다: 방 가운데에서 퍼지는 고리 + 눈
                float q = (w.Tick - s.Seen) / (float)SimTime.Minutes(20);
                ci.Arc(rp.GetCenter(), T * (0.4f + 1.4f * q), 0f, Mathf.Tau, 32, MateTeal.WithAlpha(0.5f * (1f - q)), 1.4f, true);
                PaintEye(ci, rp.GetCenter() + new Vector2(0, -T * 0.4f), MateTeal.WithAlpha(1f - q));
            }
        }
    }

    private void PaintEye(CanvasItem ci, Vector2 p, Color col)
    {
        var up = new Vector2[7]; var dn = new Vector2[7];
        for (int i = 0; i < 7; i++) { float x = -5f + i * 10f / 6f; float y = 3f * (1f - x * x / 25f); up[i] = p + new Vector2(x, -y); dn[i] = p + new Vector2(x, y); }
        ci.Polyline(up, col, 1f, true);
        ci.Polyline(dn, col, 1f, true);
        ci.Circle(p, 1.4f + 0.3f * Mathf.Sin(_time * 5f), col, true, -1f, true);
    }

    // ───────────── 훈련 ─────────────

    private void PaintMateDrill(CanvasItem ci, ShipMate m)
    {
        var d = m.ActiveDrill;
        if (d == null) return;
        if (MateRoom(d.RoomId) is Room scene)
        {
            var rp = RoomPx(scene).Grow(-2f);
            var col = d.Kind == "불" ? new Color("#ff7a59") : new Color("#7fc8ff");
            ci.DrawDashedLine(rp.Position, new Vector2(rp.End.X, rp.Position.Y), col, 1.4f, 5f, true, true);
            ci.DrawDashedLine(new Vector2(rp.End.X, rp.Position.Y), rp.End, col, 1.4f, 5f, true, true);
            ci.DrawDashedLine(rp.End, new Vector2(rp.Position.X, rp.End.Y), col, 1.4f, 5f, true, true);
            ci.DrawDashedLine(new Vector2(rp.Position.X, rp.End.Y), rp.Position, col, 1.4f, 5f, true, true);
            // 가정한 사고 그림: 불이면 속이 빈 불꽃 · 감압이면 바깥으로 향한 화살 셋
            var c = rp.GetCenter();
            if (d.Kind == "불")
                ci.Polyline(new[] { c + new Vector2(-6, 6), c + new Vector2(-4, -2), c + new Vector2(-1, 2), c + new Vector2(1, -8), c + new Vector2(4, 0), c + new Vector2(6, -3), c + new Vector2(6, 6), c + new Vector2(-6, 6) }, col, 1.2f, true);
            else
                for (int k = 0; k < 3; k++) { float a = k * Mathf.Tau / 3f + _time; var e = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 9f; ci.DrawLine(c, e, col, 1.2f, true); ci.Circle(e, 1.5f, col, true, -1f, true); }
            // 초시계
            var sw = new Vector2(rp.End.X - 8f, rp.Position.Y + 9f);
            float frac = Mathf.Clamp((_world.Tick - d.Start) / (float)SimTime.Minutes(60), 0f, 1f);
            ci.Circle(sw, 5f, new Color("#10151d"), true, -1f, true);
            ci.Arc(sw, 5f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * frac, 20, col, 1.4f, true);
            ci.DrawLine(sw + new Vector2(0, -5f), sw + new Vector2(0, -7f), new Color("#c8ced8"), 1.2f, true);
        }
        foreach (var (id, cell) in d.Spot)
        {
            var p = ToPx(cell.Center);
            bool there = d.Arrive.ContainsKey(id);
            var role = d.Role.GetValueOrDefault(id);
            var col = RoleColor(role);
            // 깃대 + 역할 깃발 (모양이 역할마다 다르다)
            ci.DrawLine(p + new Vector2(0, 6f), p + new Vector2(0, -8f), new Color("#c8ced8"), 1f, true);
            var flag = role switch
            {
                StationRole.Fire => new[] { p + new Vector2(0, -8f), p + new Vector2(7f, -6f), p + new Vector2(0, -3f) },
                StationRole.Bulkhead => new[] { p + new Vector2(0, -8f), p + new Vector2(6f, -8f), p + new Vector2(6f, -3f), p + new Vector2(0, -3f) },
                StationRole.Power => new[] { p + new Vector2(0, -8f), p + new Vector2(6f, -8f), p + new Vector2(2.5f, -5.5f), p + new Vector2(6f, -3f), p + new Vector2(0, -3f) },
                StationRole.Medical => new[] { p + new Vector2(0, -8f), p + new Vector2(6f, -5.5f), p + new Vector2(0, -3f), p + new Vector2(3f, -5.5f) },
                _ => new[] { p + new Vector2(0, -8f), p + new Vector2(5f, -7f), p + new Vector2(5f, -4f), p + new Vector2(0, -3f) },
            };
            ci.Poly(flag, col.WithAlpha(there ? 1f : 0.45f + 0.3f * Mathf.Sin(_time * 4f + id)));
            if (there) ci.Polyline(new[] { p + new Vector2(-3f, 4f), p + new Vector2(-1f, 6f), p + new Vector2(3f, 2f) }, Palette.Good, 1.2f, true);
        }
    }

    private static Color RoleColor(StationRole r) => r switch
    {
        StationRole.Fire => new Color("#ff6b4a"), StationRole.Bulkhead => new Color("#f4c430"), StationRole.Power => new Color("#ffd166"),
        StationRole.Medical => new Color("#ff8fa3"), StationRole.Eva => new Color("#7fc8ff"), StationRole.Guide => new Color("#b8f28c"), _ => new Color("#c8ced8"),
    };

    // ───────────── 정비표 ─────────────

    private void PaintMateUpkeep(CanvasItem ci, ShipMate m)
    {
        var w = _world;
        int day = SimTime.Day(w.Tick);
        if (Zoom < 0.6f) return;
        foreach (var s in m.Slots)
        {
            if (s.Day != day) continue;
            var mach = w.Ship.Machines.FirstOrDefault(x => x.Body.Id == s.MachineId);
            if (mach == null || mach.Body.Room.Detached) continue;
            var c = ToPx(mach.Body.Center) + new Vector2(T * 0.35f, -T * 0.55f);
            var tag = new Rect2(c, new Vector2(16f, 9f));
            ci.Box(tag, s.Done ? new Color("#1d3b2a") : s.Missed ? new Color("#3b1d1d") : new Color("#1b2433"));
            ci.Box(tag, s.Done ? Palette.Good : s.Missed ? Palette.Danger : MateHazard, false, 0.8f);
            ci.Circle(tag.Position + new Vector2(-1.5f, 4.5f), 1.2f, new Color("#9aa6b8"), true, -1f, true); // 꼬리표 구멍
            if (s.Done) { ci.Polyline(new[] { tag.Position + new Vector2(4f, 4.5f), tag.Position + new Vector2(7f, 7f), tag.Position + new Vector2(12f, 2f) }, Palette.Good, 1.2f, true); continue; }
            // 커지는 떨림 곡선 (추세)
            var pts = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float x = i / 9f;
                pts[i] = tag.Position + new Vector2(2f + x * 12f, 4.5f + Mathf.Sin(_time * 8f + i * 1.4f) * (0.5f + 3f * x));
            }
            ci.Polyline(pts, s.Missed ? Palette.Danger : MateHazard, 0.8f, true);
            if (Zoom > 1.2f) Gfx.Text(ci, Fonts.Body, tag.Position + new Vector2(0, 17f), $"{s.Hour:0}시", 7, new Color("#c8ced8"));
        }
    }

    // ───────────── 식당 화면 (아침 방송) ─────────────

    private void PaintMateBriefScreen(CanvasItem ci, ShipMate m)
    {
        var w = _world;
        var b = m.LastBriefing;
        if (b == null || b.Day != SimTime.Day(w.Tick)) return;
        var mess = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Mess or RoomType.Galley or RoomType.Lounge && !r.Detached).OrderBy(r => r.Type == RoomType.Mess ? 0 : 1).ThenBy(r => r.Id).FirstOrDefault();
        if (mess == null) return;
        var rp = RoomPx(mess);
        bool powered = mess.Powered;
        var scr = new Rect2(rp.Position + new Vector2(rp.Size.X * 0.5f - 22f, 2f), new Vector2(44f, 14f));
        ci.Box(scr.Grow(1.2f), new Color("#3a4558"));
        ci.Box(scr, powered ? new Color("#0b1622") : new Color("#05070a"));
        if (!powered) return;
        float fresh = Mathf.Clamp(1f - (w.Tick - b.Tick) / (float)SimTime.Hours(1), 0f, 1f);
        if (fresh > 0f) ci.Box(scr.Grow(2f + 2f * Mathf.Sin(_time * 3f)), CompViolet.WithAlpha(0.25f * fresh), false, 1f);
        int n = Math.Min(5, b.Lines.Count);
        for (int i = 0; i < n; i++)
        {
            var cell = new Vector2(scr.Position.X + 4.5f + i * 8.8f, scr.GetCenter().Y);
            PaintBriefGlyph(ci, b.Lines[i].Kind, cell, b);
        }
        // 흐르는 글줄 (읽히지 않을 만큼 작은 줄 · 가까이 보면 글)
        if (Zoom > 1.6f) Gfx.Text(ci, Fonts.Body, new Vector2(scr.Position.X, scr.End.Y + 7f), ChronicleBook.Short(b.Lines[(int)(_time / 3f) % b.Lines.Count].Text, 26), 6, new Color("#9fd8ff"));
    }

    private void PaintBriefGlyph(CanvasItem ci, string kind, Vector2 c, Briefing b)
    {
        switch (kind)
        {
            case "정비": // 렌치
                ci.DrawLine(c + new Vector2(-2.5f, 2.5f), c + new Vector2(1.5f, -1.5f), new Color("#c8ced8"), 1.2f, true);
                ci.Arc(c + new Vector2(2f, -2f), 1.6f, 0.6f, 5.2f, 8, new Color("#c8ced8"), 1f, true);
                break;
            case "날씨": // 해 또는 운석
                bool storm = _world.Automation.MateOrNull?.LastSky is SkyForecast f && f.Held;
                ci.Circle(c, 1.8f, storm ? MateOrange : new Color("#ffd166"), true, -1f, true);
                for (int k = 0; k < 6; k++) { float a = k * Mathf.Tau / 6f + _time * 0.5f; ci.DrawLine(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 2.5f, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 3.5f, storm ? MateOrange : new Color("#ffd166"), 0.6f, true); }
                break;
            case "주의": // 세모
                ci.Polyline(new[] { c + new Vector2(0, -3f), c + new Vector2(3f, 2.5f), c + new Vector2(-3f, 2.5f), c + new Vector2(0, -3f) }, MateHazard, 1f, true);
                ci.DrawLine(c + new Vector2(0, -1.2f), c + new Vector2(0, 0.8f), MateHazard, 0.8f, true);
                break;
            case "일정": // 시계
                ci.Arc(c, 3f, 0f, Mathf.Tau, 14, new Color("#9fd8ff"), 0.8f, true);
                ci.DrawLine(c, c + new Vector2(0, -2f), new Color("#9fd8ff"), 0.8f, true);
                ci.DrawLine(c, c + new Vector2(1.5f, 0.5f), new Color("#9fd8ff"), 0.8f, true);
                break;
            default: // 물자 상자 (바닥이 가까우면 붉다)
                ci.Box(new Rect2(c + new Vector2(-3f, -2.5f), new Vector2(6f, 5f)), new Color("#c9a26b"), false, 0.8f);
                ci.DrawLine(c + new Vector2(-3f, -0.5f), c + new Vector2(3f, -0.5f), new Color("#c9a26b"), 0.6f, true);
                if (Mathf.Sin(_time * 4f) > 0f) ci.Circle(c + new Vector2(3f, -3f), 0.9f, Palette.Danger, true, -1f, true);
                break;
        }
    }

    // ───────────── 딜레마 ─────────────

    private void PaintMateDilemma(CanvasItem ci, ShipMate m)
    {
        var w = _world;
        foreach (var d in m.Dilemmas)
        {
            if (d.Released >= 0 && w.Tick - d.Released > SimTime.Minutes(5)) continue;
            if (MateRoom(d.RoomId) is not Room r) continue;
            // 닫은 문: 노랑 · 검정 빗금
            if (d.Closed >= 0 && d.Released < 0)
                foreach (var dr in r.Doors)
                {
                    if (!d.Locked.Contains(dr.Id)) continue;
                    var p = ToPx(dr.Cell.Center);
                    for (int k = -2; k <= 2; k++) ci.DrawLine(p + new Vector2(k * 3f - 3f, 5f), p + new Vector2(k * 3f + 3f, -5f), k % 2 == 0 ? MateHazard : new Color("#111"), 1.6f, true);
                }
            // 함장에게 물음 (결정 전): 컴퓨터실 → 함장 점선 + 물음표
            if (d.Decided < 0 && d.AskedCaptain && _compBody != null && w.Crew.FirstOrDefault(c => c.Id == d.CaptainId) is CrewMember cap && !cap.Dead)
            {
                var a = ToPx(_compBody.Center); var b = ToPx(cap.Position);
                ci.DrawDashedLine(a, b, CompAmber.WithAlpha(0.7f), 1.2f, 4f, true, true);
                var q = b + new Vector2(0, -T * 0.8f);
                ci.Arc(q + new Vector2(0, -2f), 2.4f, -Mathf.Pi, Mathf.Pi * 0.4f, 10, CompAmber, 1.2f, true);
                ci.Circle(q + new Vector2(0, 3.5f), 0.9f, CompAmber, true, -1f, true);
            }
            else if (d.Decided < 0) // 연락이 안 된다: 방 위에 끊긴 고리
            {
                var c = RoomPx(r).GetCenter() + new Vector2(0, -T * 0.6f);
                ci.Arc(c, 5f, 0.4f, Mathf.Pi - 0.4f, 10, CompAmber, 1.2f, true);
                ci.Arc(c, 5f, Mathf.Pi + 0.4f, Mathf.Tau - 0.4f, 10, CompAmber, 1.2f, true);
            }
        }
    }

    // ───────────── 안부 ─────────────

    private void PaintMateCare(CanvasItem ci, ShipMate m)
    {
        var w = _world;
        foreach (var h in m.CareHints)
        {
            if (w.Crew.FirstOrDefault(c => c.Id == h.To) is not CrewMember f || f.Dead || f.Room == null) continue;
            var p = ToPx(f.Position) + new Vector2(T * 0.25f, -T * 0.7f);
            if (!h.Delivered)
            {
                // 접힌 쪽지
                var env = new Rect2(p, new Vector2(7f, 5f));
                ci.Box(env, new Color("#f4efe1"));
                ci.Polyline(new[] { env.Position, env.GetCenter() + new Vector2(0, 0.5f), new Vector2(env.End.X, env.Position.Y) }, new Color("#8a7d63"), 0.6f, true);
            }
            else if (w.Tick - h.Tick < SimTime.Hours(14) && w.Crew.FirstOrDefault(c => c.Id == h.About) is CrewMember t && (t.Position - f.Position).LengthSquared() < 9f)
            {
                for (int k = 0; k < 2; k++)
                {
                    var hc = p + new Vector2(k * 5f, -Mathf.PosMod(_time * 3f + k * 2f, 6f));
                    ci.Circle(hc + new Vector2(-1f, 0), 1.2f, h.Found ? new Color("#888") : new Color("#ff8fa3"), true, -1f, true);
                    ci.Circle(hc + new Vector2(1f, 0), 1.2f, h.Found ? new Color("#888") : new Color("#ff8fa3"), true, -1f, true);
                    ci.Poly(new[] { hc + new Vector2(-2.1f, 0.4f), hc + new Vector2(2.1f, 0.4f), hc + new Vector2(0, 2.8f) }, h.Found ? new Color("#888") : new Color("#ff8fa3"));
                }
            }
        }
    }

    // ───────────── 날씨 보류 · 기억 ─────────────

    private void PaintMateSky(CanvasItem ci, ShipMate m)
    {
        if (!m.EvaHold) return;
        foreach (var r in _world.Ship.LiveRooms)
        {
            if (r.Type != RoomType.Airlock) continue;
            var c = RoomPx(r).Position + new Vector2(8f, 8f);
            ci.Circle(c, 5.5f, new Color("#1b2433"), true, -1f, true);
            ci.Arc(c, 5.5f, 0f, Mathf.Tau, 16, MateOrange, 1f, true);
            // 구름 + 번개
            ci.Circle(c + new Vector2(-1.5f, -1f), 1.8f, new Color("#c8ced8"), true, -1f, true);
            ci.Circle(c + new Vector2(1.2f, -1.4f), 2.1f, new Color("#c8ced8"), true, -1f, true);
            ci.Polyline(new[] { c + new Vector2(0.5f, 0.5f), c + new Vector2(-0.8f, 2.4f), c + new Vector2(0.6f, 2.4f), c + new Vector2(-0.6f, 4.3f) }, MateHazard, 0.9f, true);
        }
    }

    private void PaintMateMemory(CanvasItem ci, ShipMate m)
    {
        if (_compBody == null) return;
        var w = _world;
        var r = new Rect2(ToPx(new System.Numerics.Vector2(_compBody.Cells.Min(c => c.X), _compBody.Cells.Min(c => c.Y))), new Vector2((_compBody.Cells.Max(c => c.X) - _compBody.Cells.Min(c => c.X) + 1) * T, (_compBody.Cells.Max(c => c.Y) - _compBody.Cells.Min(c => c.Y) + 1) * T));
        bool corrupt = false;
        foreach (var e in m.Memory.Values) if (e.Corrupt) { corrupt = true; break; }
        if (corrupt)
        {
            // 틀어진 칸: 한 칼날이 옆으로 밀려 깜빡인다
            float jit = Mathf.Sin(_time * 23f) > 0.2f ? 2f : 0f;
            var blade = new Rect2(r.Position.X + 6f + jit, r.Position.Y + r.Size.Y * 0.55f, r.Size.X * 0.5f - 9f, 3f);
            ci.Box(blade, new Color("#ff5c8a").WithAlpha(0.7f));
            ci.Box(blade.Grow(1f), new Color("#ff5c8a").WithAlpha(0.25f), false, 1f);
        }
        if (m.LastMemoryCheck >= 0 && w.Tick - m.LastMemoryCheck < SimTime.Minutes(30))
        {
            // 기억 검사: 위에서 아래로 훑는 줄
            float q = Mathf.PosMod(_time * 0.7f, 1f);
            float y = r.Position.Y + 4f + q * (r.Size.Y - 8f);
            ci.DrawLine(new Vector2(r.Position.X + 4f, y), new Vector2(r.End.X - 4f, y), new Color("#9fe8c4").WithAlpha(0.85f), 1.2f, true);
        }
    }
}
