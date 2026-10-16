using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v16.10 증축 보기 (읽기만 — 시뮬레이션을 바꾸지 않는다). 단계마다 생김이 다르다:
// · 제안: 아래 외벽 바깥에 파란 청사진 점선 (안쪽 칸 격자 · 문 자리 X · 무게 중심 화살표).
// · ① 골조: 둘레에 노랑 비계(기둥 · 가로대 · X 버팀) · 에어락에서 늘어진 안전줄 · I형 강재가 외벽에서부터 자라 나온다(볼트) ·
//   안쪽은 골조만 있는 칸(장선 사이로 별이 보인다) · 운석에 휜 강재는 꺾인 붉은 선 + 그을음 · 용접하는 사람 곁 불꽃 · 드론은 푸른 아크.
// · ② 외판: 새 금속판이 강재 위로 덮인다(리벳 · 사선 광택) · 품질이 낮은 이음은 검은 실금.
// · ③ 기밀 시험: 옆 방 벽에 압력계(바늘이 오른다) · 호스 · 시험 중이면 약한 이음에 비눗방울.
// · ④ 배선 · 배관: 케이블 드럼 · 호스 사리 · 문에서 뚜껑까지 늘어진 전선 · 어두운 방에 작업등 원뿔 → 첫 점등(깜빡이다 켜진다).
// · ⑤ 내장: 문에 비닐 막(주름 · 테이프 · 흔들림) · 침대/선반이 뼈대 → 판 → 매트리스로 차오른다 · 잘라 낸 패널 조각과 톱밥.
// · ⑥ 개통: 문 위 삼각 깃발 줄 · 문을 가로지른 리본(개통하면 잘린다) · 색종이 · 놋쇠 이름판.
// · 개통 뒤: 새 패널 광택(희푸른 반사 · 이음선)이 열흘 남짓에 걸쳐 바래 간다. 선외 공사 중지면 비계 모서리에 붉은 경광등.
public partial class ShipView
{
    private static readonly Color AxSteel = new("#9aa7b6"), AxSteelDark = new("#55606d"), AxRust = new("#b4472f"), AxScaffold = new("#e2b13a"),
        AxBlue = new("#4fa3ff"), AxPlate = new("#c9d3de"), AxPlateHi = new("#f2f7fc"), AxSheet = new("#cfe8f5"), AxBrass = new("#c79b3b"),
        AxRope = new("#e9e4d2"), AxSpark = new("#fff3b0"), AxArc = new("#9fd8ff"), AxMattress = new("#d9d2c3"), AxWood = new("#8f6a43");

    private int _annexVersion = -1;

    /// <summary>동적 층: 증축 (청사진 · 비계 · 골조 · 외판 · 시험 · 배선 · 비닐 막 · 설비 · 개통식 · 새 패널).</summary>
    private void PaintAnnex(CanvasItem ci)
    {
        var an = _world.Annex;
        if (an.Version != _annexVersion)
        {
            _annexVersion = an.Version;
            Bounds = ComputeBounds(); // 격자가 아래로 자랐을 수 있다
        }
        if (an.Plans.Count == 0) return;
        bool fine = Zoom > 1.0f;
        foreach (var p in an.Plans)
        {
            if (p.Site == null) continue;
            switch (p.State)
            {
                case "제안": PaintAnnexBlueprint(ci, p, fine); break;
                case "공사":
                    if (p.Enclosed < 0) PaintAnnexShell(ci, p, fine);
                    else PaintAnnexInside(ci, p, fine);
                    break;
                case "개통": PaintAnnexOpened(ci, p, fine); break;
            }
        }
    }

    private static float AxHash(int a, int b) => Mathf.PosMod(Mathf.Sin(a * 12.9898f + b * 78.233f) * 43758.5453f, 1f);

    // ─────────────────────────────── 제안: 청사진 ───────────────────────────────

    private void PaintAnnexBlueprint(CanvasItem ci, AnnexPlan p, bool fine)
    {
        var s = p.Site;
        var outer = new Rect2((s.X0 - 1) * T, (s.Y0 + 1) * T, (s.Width + 2) * T, (s.Depth + 1) * T);
        var blue = AxBlue with { A = 0.55f + 0.15f * Mathf.Sin(_time * 2f) };
        PaintDashedRect(ci, outer, blue, 2f, 7f);
        PaintDashedRect(ci, outer.Grow(-T), blue with { A = blue.A * 0.6f }, 1f, 4f);
        if (fine)
            for (int x = s.X0 + 1; x <= s.X1; x++) ci.DrawLine(new Vector2(x * T, (s.Y0 + 1) * T), new Vector2(x * T, (s.Y0 + 1 + s.Depth) * T), blue with { A = 0.18f }, 1f);
        // 문 자리: X
        var d = CellRect(s.Door).GetCenter();
        ci.DrawLine(d + new Vector2(-6f, -6f), d + new Vector2(6f, 6f), blue, 2f);
        ci.DrawLine(d + new Vector2(6f, -6f), d + new Vector2(-6f, 6f), blue, 2f);
        // 설비 자리 (침대 · 선반 윤곽)
        int n = p.Fixtures;
        for (int i = 0, x = s.X0; i < n && x <= s.X1; x += 2)
        {
            if (x == s.Door.X) { x--; continue; }
            var r = CellRect(new Cell(x, s.Y0 + s.Depth)).Grow(-5f);
            ci.Box(r, blue with { A = 0.35f }, false, 1f);
            i++;
        }
        if (fine) Gfx.TextCentered(ci, Fonts.Bold, outer.GetCenter(), $"증축 안 · {p.UseName}", 9, blue with { A = 0.9f });
    }

    private static void PaintDashedRect(CanvasItem ci, Rect2 r, Color col, float width, float dash)
    {
        void Dash(Vector2 a, Vector2 b)
        {
            float len = a.DistanceTo(b);
            var dir = (b - a) / Mathf.Max(0.01f, len);
            for (float t = 0f; t < len; t += dash * 2f) ci.DrawLine(a + dir * t, a + dir * Mathf.Min(len, t + dash), col, width);
        }
        Dash(r.Position, new Vector2(r.End.X, r.Position.Y));
        Dash(new Vector2(r.End.X, r.Position.Y), r.End);
        Dash(r.End, new Vector2(r.Position.X, r.End.Y));
        Dash(new Vector2(r.Position.X, r.End.Y), r.Position);
    }

    // ─────────────────────────────── ①②③ 선외: 비계 · 골조 · 외판 · 시험 ───────────────────────────────

    private void PaintAnnexShell(CanvasItem ci, AnnexPlan p, bool fine)
    {
        var w = _world;
        var s = p.Site;
        float built = p.Frame.Length == 0 ? 0f : p.Frame.Average();
        float plated = p.Plate.Length == 0 ? 0f : p.Plate.Average();
        // 안쪽: 골조만 있는 칸 — 장선 사이로 우주가 보인다 (외판이 차면 갑판이 덮인다)
        foreach (var c in s.Inside)
        {
            var r = CellRect(c);
            ci.Box(r, new Color(0.02f, 0.03f, 0.06f, 0.55f));
            if (AxHash(c.X, c.Y) > 0.82f) ci.Circle(r.Position + new Vector2(AxHash(c.Y, c.X) * T, AxHash(c.X + 3, c.Y) * T), 0.9f, new Color(1f, 1f, 1f, 0.6f));
            float jo = Mathf.Clamp(built * 1.3f - (c.Y - s.Y0 - 1) * 0.25f, 0f, 1f);
            if (jo > 0f)
            {
                ci.DrawLine(new Vector2(r.Position.X + 2f, r.Position.Y + T * 0.5f), new Vector2(r.Position.X + 2f + (T - 4f) * jo, r.Position.Y + T * 0.5f), AxSteelDark, 2f);
                if (fine) ci.DrawLine(new Vector2(r.GetCenter().X, r.Position.Y), new Vector2(r.GetCenter().X, r.Position.Y + T * jo), AxSteelDark with { A = 0.7f }, 1f);
            }
            float deck = Mathf.Clamp(plated * 1.4f - (c.Y - s.Y0 - 1) * 0.3f, 0f, 1f);
            if (deck > 0f) ci.Box(new Rect2(r.Position, new Vector2(T, T * deck)), new Color(0.42f, 0.47f, 0.53f, 0.75f));
        }
        // 비계: 둘레 바깥 한 칸 (기둥 · 가로대 · X 버팀)
        PaintScaffold(ci, p, fine);
        // 둘레 강재 · 외판
        for (int i = 0; i < s.Shell.Count; i++) PaintMember(ci, p, i, fine);
        // 용접: 사람 (그 칸에 붙어 일하는 중) · 드론 (아크)
        for (int i = 0; i < s.Shell.Count; i++)
        {
            int by = p.BusyBy[i];
            if (by < 0 || by >= w.Crew.Count) continue;
            var cm = w.Crew[by];
            var at = CellRect(s.Shell[i]).GetCenter();
            if (cm.Pose != Pose.Working || ToPx(cm.Position).DistanceTo(at) > T * 1.6f) continue;
            PaintSparks(ci, at, i, 1f);
        }
        foreach (var d in w.Drones.Drones)
        {
            int u = w.Annex.DroneUnit(d);
            if (u < 0 || u >= s.Shell.Count || d.State != DroneState.Working) continue;
            var a = ToPx(d.Position);
            var b = CellRect(s.Shell[u]).GetCenter();
            var mid = (a + b) * 0.5f + new Vector2(Mathf.Sin(_time * 31f) * 4f, Mathf.Cos(_time * 27f) * 4f);
            ci.Polyline(new[] { a, mid, b }, AxArc with { A = 0.85f }, 1.6f, true);
            ci.Circle(b, 3.5f + Mathf.Sin(_time * 40f), AxArc with { A = 0.35f }, true, -1f, true);
            PaintSparks(ci, b, u + 17, 0.7f);
        }
        // ③ 기밀 시험: 옆 방 벽에 압력계 · 호스 · 비눗방울
        if (p.Stage == AnnexStage.Pressure) PaintPressureTest(ci, p, fine);
        // 선외 공사 중지: 비계 모서리 붉은 경광등
        if (w.Annex.EvaHalted && p.Stage <= AnnexStage.Plating)
        {
            float on = Mathf.PosMod(_time * 2f, 1f) < 0.5f ? 1f : 0.25f;
            foreach (var cx in new[] { s.X0 - 2, s.X1 + 2 })
            {
                var lp = new Vector2((cx + 0.5f) * T, (s.Y0 + 1.3f) * T);
                ci.Circle(lp, 9f, new Color(1f, 0.15f, 0.1f, 0.25f * on), true, -1f, true);
                ci.Circle(lp, 3.5f, new Color(1f, 0.25f, 0.15f, on), true, -1f, true);
            }
            if (fine) Gfx.TextCentered(ci, Fonts.Bold, new Vector2((s.X0 + s.X1 + 1) * 0.5f * T, (s.Y0 + s.Depth + 3.2f) * T), $"선외 공사 중지 — {w.Annex.HaltWhy}", 9, new Color(1f, 0.45f, 0.35f));
        }
        if (fine)
        {
            string stage = p.Stage switch { AnnexStage.Frame => $"① 골조 {built * 100:0}%", AnnexStage.Plating => $"② 외판 {plated * 100:0}%", _ => $"③ 기밀 시험 {p.Pressure * 100:0}%" };
            Gfx.TextCentered(ci, Fonts.Bold, new Vector2((s.X0 + s.X1 + 1) * 0.5f * T, (s.Y0 + s.Depth + 2.6f) * T), stage, 9, AxScaffold);
        }
    }

    private void PaintScaffold(CanvasItem ci, AnnexPlan p, bool fine)
    {
        var s = p.Site;
        float x0 = (s.X0 - 1.5f) * T, x1 = (s.X1 + 2.5f) * T, y0 = (s.Y0 + 1f) * T, y1 = (s.Y0 + s.Depth + 2.5f) * T;
        var col = AxScaffold with { A = 0.8f };
        // 기둥
        for (float x = x0; x <= x1 + 0.1f; x += T) { ci.DrawLine(new Vector2(x, y1), new Vector2(x, y1 - 4f), col, 2f); }
        foreach (var x in new[] { x0, x1 }) ci.DrawLine(new Vector2(x, y0), new Vector2(x, y1), col, 2f);
        // 가로대 · X 버팀 (옆 · 아래)
        ci.DrawLine(new Vector2(x0, y1), new Vector2(x1, y1), col, 2f);
        for (float y = y0; y < y1 - 1f; y += T)
        {
            ci.DrawLine(new Vector2(x0, y), new Vector2(x0, y + T), col, 1.5f);
            ci.DrawLine(new Vector2(x1, y), new Vector2(x1, y + T), col, 1.5f);
            if (fine)
            {
                ci.DrawLine(new Vector2(x0 - 4f, y), new Vector2(x0 + 4f, Mathf.Min(y1, y + T)), col with { A = 0.5f }, 1f);
                ci.DrawLine(new Vector2(x1 + 4f, y), new Vector2(x1 - 4f, Mathf.Min(y1, y + T)), col with { A = 0.5f }, 1f);
            }
        }
        for (float x = x0; x < x1 - 1f; x += T)
            if (fine) ci.DrawLine(new Vector2(x, y1 - 4f), new Vector2(x + T, y1 + 4f), col with { A = 0.5f }, 1f);
        // 안전줄: 외부 해치에서 비계 모서리까지 늘어진 줄
        var hatch = DroneSystem.Hatch(_world);
        if (hatch != null)
        {
            var a = CellRect(hatch.Cell).GetCenter();
            var b = new Vector2(x0, y0 + T * 0.5f);
            if (a.DistanceTo(b) < T * 40f)
            {
                var pts = new Vector2[12];
                for (int k = 0; k < pts.Length; k++)
                {
                    float t = k / (float)(pts.Length - 1);
                    pts[k] = a.Lerp(b, t) + new Vector2(0f, Mathf.Sin(t * Mathf.Pi) * T * 0.8f + Mathf.Sin(_time * 1.3f + t * 4f) * 2f);
                }
                ci.Polyline(pts, AxRope with { A = 0.55f }, 1f, true);
            }
        }
    }

    /// <summary>둘레 칸 하나: 강재(진척만큼 · 휘었으면 꺾인 붉은 선) · 외판(덮인 만큼 · 리벳 · 광택 · 약한 이음 실금).</summary>
    private void PaintMember(CanvasItem ci, AnnexPlan p, int i, bool fine)
    {
        var s = p.Site;
        var cell = s.Shell[i];
        var r = CellRect(cell);
        var c = r.GetCenter();
        bool side = cell.X == s.X0 - 1 || cell.X == s.X1 + 1;
        float f = p.Frame[i];
        if (f <= 0f && !p.Bent[i]) { ci.Box(r.Grow(-T * 0.4f), AxSteel with { A = 0.15f }, false, 1f); return; }
        if (p.Bent[i] && f < 1f)
        {
            // 휜 강재: 꺾인 선 + 그을음
            ci.Circle(c, T * 0.42f, new Color(0.05f, 0.04f, 0.03f, 0.45f), true, -1f, true);
            var k = side ? new[] { new Vector2(c.X - 2f, r.Position.Y), c + new Vector2(6f, -2f), c + new Vector2(-5f, 4f), new Vector2(c.X + 3f, r.End.Y) }
                         : new[] { new Vector2(r.Position.X, c.Y + 2f), c + new Vector2(-3f, 6f), c + new Vector2(4f, -5f), new Vector2(r.End.X, c.Y - 2f) };
            ci.Polyline(k, AxRust, 3f, true);
        }
        // I형 강재: 외벽 쪽에서 자라 나온다 (옆은 위에서 아래로 · 바닥은 왼쪽에서 오른쪽으로)
        float len = Mathf.Clamp(f, 0f, 1f) * T;
        if (side)
        {
            ci.Box(new Rect2(c.X - 3f, r.Position.Y, 6f, len), AxSteel);
            ci.Box(new Rect2(c.X - 1f, r.Position.Y, 2f, len), AxSteelDark);
            if (fine && f >= 1f) { ci.DrawLine(new Vector2(c.X - 7f, r.Position.Y + 2f), new Vector2(c.X + 7f, r.Position.Y + 2f), AxSteel, 2f); }
        }
        else
        {
            ci.Box(new Rect2(r.Position.X, c.Y - 3f, len, 6f), AxSteel);
            ci.Box(new Rect2(r.Position.X, c.Y - 1f, len, 2f), AxSteelDark);
            if (fine && f >= 1f) ci.DrawLine(new Vector2(r.Position.X + 2f, c.Y - 7f), new Vector2(r.Position.X + 2f, c.Y + 7f), AxSteel, 2f);
        }
        if (fine && f >= 1f) { ci.Circle(c + new Vector2(-4f, -4f), 1.2f, AxSteelDark, true, -1f, true); ci.Circle(c + new Vector2(4f, 4f), 1.2f, AxSteelDark, true, -1f, true); }
        // 외판
        float pl = p.Plate[i];
        if (pl > 0f)
        {
            var pr = side ? new Rect2(r.Position, new Vector2(T, T * pl)) : new Rect2(r.Position, new Vector2(T * pl, T));
            ci.Box(pr, AxPlate);
            ci.DrawLine(pr.Position + new Vector2(3f, pr.Size.Y - 3f), pr.Position + new Vector2(Mathf.Min(pr.Size.X - 3f, 14f), 3f), AxPlateHi with { A = 0.7f }, 2f); // 사선 광택
            if (fine)
                for (int k = 0; k < 4; k++)
                {
                    var rv = pr.Position + new Vector2(k % 2 == 0 ? 3f : pr.Size.X - 3f, k < 2 ? 3f : pr.Size.Y - 3f);
                    if (pr.HasPoint(rv)) ci.Circle(rv, 1.1f, AxSteelDark, true, -1f, true); // 리벳
                }
            if (pl >= 1f && p.PlateQ[i] > 0f && p.PlateQ[i] < 0.55f) // 약한 이음: 실금
                ci.Polyline(new[] { r.Position + new Vector2(T * 0.2f, T * 0.15f), r.Position + new Vector2(T * 0.45f, T * 0.45f), r.Position + new Vector2(T * 0.4f, T * 0.6f), r.Position + new Vector2(T * 0.75f, T * 0.85f) },
                    new Color(0.08f, 0.08f, 0.1f, 0.85f), 1.2f, true);
        }
    }

    private void PaintSparks(CanvasItem ci, Vector2 at, int seed, float strength)
    {
        int frame = (int)(_time * 24f);
        ci.Circle(at, 6f * strength, new Color(1f, 0.85f, 0.4f, 0.35f), true, -1f, true);
        ci.Circle(at, 2.2f, new Color(1f, 1f, 0.95f, 0.95f), true, -1f, true);
        for (int k = 0; k < 7; k++)
        {
            float a = AxHash(seed * 31 + k, frame) * Mathf.Tau;
            float l = (4f + 9f * AxHash(k, frame + seed)) * strength;
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.7f + 0.35f);
            ci.DrawLine(at + dir * 2f, at + dir * l, AxSpark with { A = 0.9f - 0.08f * k }, 1.2f, true);
        }
    }

    private void PaintPressureTest(CanvasItem ci, AnnexPlan p, bool fine)
    {
        var w = _world;
        var s = p.Site;
        var g = CellRect(s.DoorInner).GetCenter() + new Vector2(T * 0.55f, -T * 0.25f);
        // 호스: 압력계 → 외벽(문 자리)
        var wall = CellRect(s.Door).GetCenter();
        ci.Polyline(new[] { g + new Vector2(0f, 7f), (g + wall) * 0.5f + new Vector2(6f, 4f), wall }, new Color(0.15f, 0.15f, 0.18f), 2.5f, true);
        // 압력계: 흰 원판 · 눈금 · 바늘
        ci.Circle(g, 7.5f, new Color(0.95f, 0.95f, 0.92f), true, -1f, true);
        ci.Arc(g, 7.5f, 0f, Mathf.Tau, 20, new Color(0.2f, 0.2f, 0.22f), 1.4f, true);
        if (fine) for (int k = 0; k <= 4; k++) { float a = Mathf.Pi * (0.75f + 1.5f * k / 4f); ci.DrawLine(g + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 5f, g + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 7f, new Color(0.2f, 0.2f, 0.22f), 1f); }
        float na = Mathf.Pi * (0.75f + 1.5f * p.Pressure) + (p.PressureBy >= 0 ? Mathf.Sin(_time * 9f) * 0.03f : 0f);
        ci.DrawLine(g, g + new Vector2(Mathf.Cos(na), Mathf.Sin(na)) * 6f, new Color(0.85f, 0.15f, 0.1f), 1.5f, true);
        // 비눗방울: 시험 중 · 끝까지 하는 시험이면 약한 이음마다
        if (p.PressureBy >= 0 && p.FullTest == true)
            for (int i = 0; i < s.Shell.Count; i++)
            {
                if (p.PlateQ[i] >= 0.55f) continue;
                var c = CellRect(s.Shell[i]).GetCenter();
                for (int k = 0; k < 3; k++)
                {
                    float ph = Mathf.PosMod(_time * 0.7f + k * 0.33f + i * 0.13f, 1f);
                    ci.Arc(c + new Vector2((k - 1) * 5f, -ph * 10f), 1.5f + 2.5f * ph, 0f, Mathf.Tau, 10, new Color(1f, 1f, 1f, 0.8f * (1f - ph)), 1f, true);
                }
            }
    }

    // ─────────────────────────────── ④⑤⑥ 안쪽: 배선 · 비닐 막 · 설비 · 개통식 ───────────────────────────────

    private void PaintAnnexInside(CanvasItem ci, AnnexPlan p, bool fine)
    {
        var w = _world;
        var s = p.Site;
        if (p.RoomId < 0 || p.RoomId >= w.Ship.Rooms.Count) return;
        var room = w.Ship.Rooms[p.RoomId];
        var dIn = CellRect(s.DoorOuter).GetCenter();
        if (p.Stage == AnnexStage.Utilities)
        {
            // 케이블 드럼 · 호스 사리 · 문에서 뚜껑까지 늘어진 전선
            var drum = dIn + new Vector2(-T * 0.7f, T * 0.15f);
            ci.Circle(drum, 7f, new Color(0.45f, 0.3f, 0.18f), true, -1f, true);
            for (int k = 1; k <= 3; k++) ci.Arc(drum, 7f - k * 1.6f, 0f, Mathf.Tau, 14, new Color(0.08f, 0.08f, 0.1f), 1f, true);
            var coil = dIn + new Vector2(T * 0.75f, T * 0.2f);
            for (int k = 0; k < 3; k++) ci.Arc(coil, 3f + k * 1.8f, 0f, Mathf.Tau, 14, new Color(0.2f, 0.5f, 0.85f), 1.4f, true);
            var hatch = w.Body.Hatches.FirstOrDefault(h => room.Cells.Contains(h.Cell));
            if (hatch != null)
            {
                var hc = CellRect(hatch.Cell).GetCenter();
                ci.Polyline(new[] { drum, (drum + hc) * 0.5f + new Vector2(0f, 6f + Mathf.Sin(_time) * 1.5f), hc }, new Color(0.08f, 0.08f, 0.1f), 2f, true);
            }
            // 작업등 원뿔 (어두운 방)
            if (p.WireBy >= 0 && p.WireBy < w.Crew.Count && room.Dark)
            {
                var cm = w.Crew[p.WireBy];
                var a = ToPx(cm.Position);
                var dir = (CellRect(hatch?.Cell ?? s.Inside[0]).GetCenter() - a).Normalized();
                if (dir == Vector2.Zero) dir = Vector2.Down;
                var side = new Vector2(-dir.Y, dir.X);
                ci.Poly(new[] { a, a + dir * T * 2.2f + side * T * 0.9f, a + dir * T * 2.2f - side * T * 0.9f }, new Color(1f, 0.92f, 0.6f, 0.22f));
            }
        }
        // 첫 점등: 깜빡이다 켜진다 (3분)
        if (p.FirstLight >= 0)
        {
            long dt = w.Tick - p.FirstLight;
            if (dt < SimTime.Minutes(3))
            {
                float k = dt < SimTime.Minutes(1.2f) ? (Mathf.Sin(_time * 37f) > 0.2f ? 0.5f : 0.05f) : 0.35f * (1f - (dt - SimTime.Minutes(1.2f)) / (float)SimTime.Minutes(1.8f));
                foreach (var c in room.Cells) ci.Box(CellRect(c), new Color(1f, 0.96f, 0.8f, k));
            }
        }
        // 비닐 막: 문에 · 주름 · 테이프 · 흔들림
        if (p.Sheet > 0f && p.Stage <= AnnexStage.Opening && p.Stage >= AnnexStage.FitOut) PaintPlasticSheet(ci, s, p.Sheet, fine);
        // 설비: 뼈대 → 판 → 매트리스 (선반: 기둥 → 칸)
        for (int i = 0; i < p.FixtureSpots.Count; i++)
        {
            float f = p.Fit[i];
            if (f <= 0f || f >= 1f && w.Ship.FurnitureAt(p.FixtureSpots[i]) != null) continue;
            PaintFixtureBuild(ci, p, i, f, fine);
            if (p.FitBy[i] >= 0 && p.FitBy[i] < w.Crew.Count && w.Crew[p.FitBy[i]].Pose == Pose.Working && w.Matter.DustAt(p.FixtureSpots[i]) < 0.3f && f > 0.45f)
                PaintSparks(ci, CellRect(p.FixtureSpots[i]).GetCenter(), i + 40, 0.6f);
        }
        if (p.Stage == AnnexStage.Opening) PaintOpening(ci, p, room, fine);
        PaintFreshPanels(ci, p, room, 1f);
    }

    private void PaintPlasticSheet(CanvasItem ci, AnnexSite s, float progress, bool fine)
    {
        var r = CellRect(s.Door);
        float h = T * Mathf.Clamp(progress, 0f, 1f);
        float sway = Mathf.Sin(_time * 1.7f) * 2.5f;
        var top = r.Position;
        var pts = new[] { top, top + new Vector2(T, 0f), top + new Vector2(T + sway * 0.4f, h), top + new Vector2(sway, h) };
        ci.Poly(pts, AxSheet with { A = 0.42f });
        if (fine)
            for (int k = 1; k < 4; k++) // 주름
            {
                float x = top.X + T * k / 4f;
                ci.DrawLine(new Vector2(x, top.Y + 1f), new Vector2(x + sway * (0.3f + 0.2f * k), top.Y + h - 1f), new Color(1f, 1f, 1f, 0.45f), 1f, true);
            }
        ci.Box(new Rect2(top, new Vector2(T, 3f)), new Color(0.95f, 0.75f, 0.2f, 0.9f)); // 테이프
        ci.Box(new Rect2(top + new Vector2(-1f, 0f), new Vector2(3f, h)), new Color(0.95f, 0.75f, 0.2f, 0.7f));
        ci.Box(new Rect2(top + new Vector2(T - 2f, 0f), new Vector2(3f, h)), new Color(0.95f, 0.75f, 0.2f, 0.7f));
    }

    private void PaintFixtureBuild(CanvasItem ci, AnnexPlan p, int i, float f, bool fine)
    {
        var r = CellRect(p.FixtureSpots[i]).Grow(-3f);
        // 잘라 낸 패널 조각 · 톱밥
        for (int k = 0; k < 3; k++)
        {
            var o = r.Position + new Vector2(AxHash(i, k) * r.Size.X, r.Size.Y + 1f - AxHash(k, i) * 4f);
            ci.Box(new Rect2(o, new Vector2(4f + k, 2f)), new Color(0.6f, 0.64f, 0.7f, 0.8f));
        }
        if (p.Use == RoomType.Storage)
        {
            ci.Box(new Rect2(r.Position, new Vector2(3f, r.Size.Y)), AxSteel);
            ci.Box(new Rect2(r.Position + new Vector2(r.Size.X - 3f, 0f), new Vector2(3f, r.Size.Y)), AxSteel);
            int shelves = Mathf.FloorToInt(f * 4f);
            for (int k = 0; k < shelves; k++) ci.Box(new Rect2(r.Position + new Vector2(0f, r.Size.Y * (k + 0.5f) / 4f), new Vector2(r.Size.X, 2f)), AxSteelDark);
            return;
        }
        // 침대: 뼈대(가장자리) → 판(가로 살) → 매트리스 · 베개
        ci.Box(r, AxWood with { A = 0.9f }, false, 2f);
        int slats = Mathf.FloorToInt(Mathf.Clamp(f * 2f, 0f, 1f) * 5f);
        for (int k = 0; k < slats; k++) ci.DrawLine(r.Position + new Vector2(2f, (k + 0.5f) * r.Size.Y / 5f), r.Position + new Vector2(r.Size.X - 2f, (k + 0.5f) * r.Size.Y / 5f), AxWood, 1.5f);
        if (f > 0.5f)
        {
            float m = (f - 0.5f) * 2f;
            ci.Box(new Rect2(r.Position + new Vector2(1.5f, 1.5f), new Vector2((r.Size.X - 3f) * m, r.Size.Y - 3f)), AxMattress);
            if (f > 0.85f) ci.Box(new Rect2(r.Position + new Vector2(2.5f, 2.5f), new Vector2(r.Size.X * 0.35f, r.Size.Y * 0.35f)), Colors.White);
        }
    }

    private void PaintOpening(CanvasItem ci, AnnexPlan p, Room room, bool fine)
    {
        var s = p.Site;
        var door = CellRect(s.Door);
        // 삼각 깃발 줄: 문 양옆 벽 위로 늘어진다
        var a = door.Position + new Vector2(-T * 1.5f, -2f);
        var b = door.Position + new Vector2(T * 2.5f, -2f);
        Color[] cols = { new("#e94b3c"), new("#f2c230"), new("#3fa34d"), new("#2f6fae"), new("#f7f2e8") };
        for (int k = 0; k < 9; k++)
        {
            float t0 = k / 9f, t1 = (k + 1) / 9f;
            var p0 = a.Lerp(b, t0) + new Vector2(0f, Mathf.Sin(t0 * Mathf.Pi) * 6f);
            var p1 = a.Lerp(b, t1) + new Vector2(0f, Mathf.Sin(t1 * Mathf.Pi) * 6f);
            var tip = (p0 + p1) * 0.5f + new Vector2(Mathf.Sin(_time * 3f + k) * 1f, 7f);
            ci.Poly(new[] { p0, p1, tip }, cols[k % cols.Length]);
        }
        ci.DrawLine(a, b, AxRope, 1f);
        // 리본: 문을 가로지른다 (나비매듭)
        var c = door.GetCenter();
        ci.DrawLine(new Vector2(door.Position.X - 2f, c.Y), new Vector2(door.End.X + 2f, c.Y), new Color("#c8281e"), 3f);
        ci.Poly(new[] { c, c + new Vector2(-6f, -5f), c + new Vector2(-6f, 5f) }, new Color("#e0362a"));
        ci.Poly(new[] { c, c + new Vector2(6f, -5f), c + new Vector2(6f, 5f) }, new Color("#e0362a"));
        // 색종이
        for (int k = 0; k < 24; k++)
        {
            float ph = Mathf.PosMod(_time * 0.35f + AxHash(k, 7), 1f);
            var cell = room.Cells[k % room.Cells.Count];
            var at = CellRect(cell).Position + new Vector2(AxHash(k, 3) * T, ph * T * 1.4f - T * 0.2f);
            ci.Box(new Rect2(at, new Vector2(2.5f, 1.6f)), cols[k % cols.Length] with { A = 1f - ph });
        }
    }

    // ─────────────────────────────── 개통 뒤: 이름판 · 새 패널 광택 ───────────────────────────────

    private void PaintAnnexOpened(CanvasItem ci, AnnexPlan p, bool fine)
    {
        var w = _world;
        if (p.RoomId < 0 || p.RoomId >= w.Ship.Rooms.Count) return;
        var room = w.Ship.Rooms[p.RoomId];
        if (room.Detached) return;
        float fresh = w.Annex.Fresh(room);
        PaintFreshPanels(ci, p, room, fresh);
        // 잘린 리본 끝 (개통 뒤 하루)
        var door = CellRect(p.Site.Door);
        if (w.Tick - p.Opened < SimTime.TicksPerDay)
        {
            ci.DrawLine(new Vector2(door.Position.X - 2f, door.GetCenter().Y), new Vector2(door.Position.X + 6f, door.GetCenter().Y + 5f), new Color("#c8281e"), 2.5f);
            ci.DrawLine(new Vector2(door.End.X + 2f, door.GetCenter().Y), new Vector2(door.End.X - 6f, door.GetCenter().Y + 6f), new Color("#c8281e"), 2.5f);
        }
        // 놋쇠 이름판: 문 오른쪽 옆 벽 (옛 외벽)
        if (p.Name == null || Zoom < 0.6f) return;
        var at = CellRect(p.Site.Door + new Cell(1, 0));
        var plate = new Rect2(at.Position + new Vector2(2f, T * 0.28f), new Vector2(T * 2.4f, T * 0.44f));
        ci.Box(plate, AxBrass);
        ci.Box(plate, new Color(0.45f, 0.32f, 0.1f), false, 1.2f);
        ci.Circle(plate.Position + new Vector2(3f, plate.Size.Y * 0.5f), 1.3f, new Color(0.35f, 0.25f, 0.08f), true, -1f, true);
        ci.Circle(plate.End - new Vector2(3f, plate.Size.Y * 0.5f), 1.3f, new Color(0.35f, 0.25f, 0.08f), true, -1f, true);
        if (fine) Gfx.TextCentered(ci, Fonts.Bold, plate.GetCenter() + new Vector2(0f, 0.5f), p.Name, 7, new Color(0.22f, 0.15f, 0.04f));
    }

    /// <summary>새 패널: 희푸른 반사 · 또렷한 이음선 (바래 간다).</summary>
    private void PaintFreshPanels(CanvasItem ci, AnnexPlan p, Room room, float fresh)
    {
        if (fresh <= 0.03f) return;
        foreach (var c in room.Cells)
        {
            var r = CellRect(c);
            ci.Box(r, new Color(0.85f, 0.93f, 1f, 0.13f * fresh));
            ci.DrawLine(r.Position + new Vector2(4f, T - 4f), r.Position + new Vector2(T * 0.55f, 4f), new Color(1f, 1f, 1f, 0.22f * fresh), 2f);
            ci.Box(r, new Color(0.9f, 0.96f, 1f, 0.25f * fresh), false, 1f);
        }
        foreach (var c in p.Site.Shell)
        {
            var r = CellRect(c);
            ci.DrawLine(r.Position + new Vector2(1f, 1f), r.Position + new Vector2(T - 1f, 1f), new Color(0.95f, 0.98f, 1f, 0.5f * fresh), 1.2f);
        }
    }
}
