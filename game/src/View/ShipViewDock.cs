using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v18.5 도킹 · 난파선 · v18.6 승객 보기 (읽기만 — 시뮬레이션을 바꾸지 않는다).
// · 도킹 통로: 주름 진 관(마디마다 밝고 어둡게) · 양 끝 고리 클램프 · 기밀 시험 중엔 노란 고리가 맥박치고, 확인되면 초록 등 · 새면 김이 뿜어 나온다.
//   에어락 안쪽 벽엔 압력계(바늘이 시험 진척을 따라 오르내린다 · 다시 물릴 땐 바늘이 처진다).
// · 난파선: 선체 생김 넷(각진 판 · 둥근 마디 · 트러스 · 줄무늬 화물선) — 그을음 · 찢긴 구멍 · 꺾인 안테나. 안은 거의 검다.
//   방마다 다른 물건: 조종실(꺼진 화면 · 의자 · 깜빡이는 기록기) · 선실(침대 · 떠다니는 컵과 사진 · 서리 낀 수면 캡슐) · 화물칸(끈 맨 상자 · 부서진 상자) · 기관실(배관 · 서리 낀 원자로 · 밸브 핸들).
//   위험: 찢긴 판(은빛 톱니 · 알고 나면 경고 테이프) · 흔들리는 격벽(금 · 먼지 · 무너지면 기울어진 대들보) · 얼어붙은 연료관(얼음 결정 · 김).
//   뒤진 방은 문틀에 주황 X(스프레이) · 찾은 기록 자리는 빈 윤곽 · 자르는 곳엔 불꽃 · 잘라 간 만큼 외판이 사라지고 골조가 드러난다.
//   안에 있는 사람은 헬멧 등 원뿔로 어둠을 조금 밝힌다.
// · 거룻배: 매끈한 선체 · 따뜻한 창 · 이름 띠. 해치 앞에 교환 상자 · 저쪽 사람(청록 외투 · 모자) · 같이 손보는 설비 곁에 저쪽 기관사(공구 상자).
// · 추모: 식당 벽 놋쇠 이름판(새긴 줄 · 리본) · 모이는 동안 작은 촛불.
// · 승객: 사람마다 다른 외투(긴 코트 · 판초 · 조끼 · 작업복) · 무늬(민 · 줄 · 체크 · 점) · 모자(베레 · 챙 넓은 모자 · 방울 털모자) · 짐(여행 가방 · 배낭 · 어깨 가방 · 바구니)
//   · 구해 온 사람은 처음 하루 금빛 보온 담요 · 자원봉사 승객은 초록 완장 · 따지러 가는 사람 머리 위엔 낙서 구름 · 이끄는 손은 손잡은 선.
public partial class ShipView
{
    private static readonly Color DkHull = new("#5d6670"), DkHullDark = new("#2c3239"), DkSoot = new("#14171b"), DkFrost = new("#cfe7f5"), DkAmber = new("#f2a93b"),
        DkGreen = new("#5fd38a"), DkRed = new("#e0533d"), DkBrass = new("#c79b3b"), DkOrange = new("#ff7a1a"), DkTeal = new("#2f8c8c"), DkFoil = new("#e8c860");

    private int _dockVersion = -1;

    private static float DkHash(int a, int b) { uint h = (uint)(a * 73856093 ^ b * 19349663); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (h & 0xffff) / 65535f; }

    /// <summary>동적 층(사람 밑): 통로 · 난파선 · 거룻배 · 압력계 · 교환 상자 · 이름판.</summary>
    private void PaintDock(CanvasItem ci)
    {
        var d = _world.Dock;
        if (d.Version != _dockVersion) { _dockVersion = d.Version; Bounds = ComputeBounds(); }
        foreach (var mv in d.Visits) if (mv.Mourned || d.MemorialOn(mv)) PaintPlaque(ci, mv);
        if (d.Visits.Count == 0 || d.Visits[^1] is not DockVisit v || v.Stage == DockStage.Gone) return;
        bool fine = Zoom > 1.0f;
        if (v.Wreck) PaintWreck(ci, v, fine); else PaintTender(ci, v, fine);
        PaintCollar(ci, v);
        PaintSealGauge(ci, v);
    }

    private void PaintCollar(CanvasItem ci, DockVisit v)
    {
        float x = (v.DockX + 0.5f) * T;
        float y0 = v.CollarTop * T, y1 = (v.Y0 + (v.Wreck ? 0.6f : 0.2f)) * T;
        // 주름 관
        var tube = new Rect2(x - T * 0.32f, y0, T * 0.64f, y1 - y0);
        ci.DrawRect(tube, new Color(0.24f, 0.27f, 0.31f));
        for (float y = y0; y < y1; y += 5f)
            ci.DrawRect(new Rect2(tube.Position.X, y, tube.Size.X, 2.2f), new Color(0.55f, 0.6f, 0.66f, 0.75f));
        // 양 끝 고리
        Color ring = v.Leaked ? DkRed : v.Checked ? DkGreen : DkAmber with { A = 0.55f + 0.4f * Mathf.Sin(_time * 4f) };
        foreach (float yy in new[] { y0, y1 })
        {
            ci.DrawRect(new Rect2(x - T * 0.42f, yy - 3f, T * 0.84f, 6f), DkHullDark);
            for (int b = -1; b <= 1; b += 2) ci.DrawCircle(new Vector2(x + b * T * 0.36f, yy), 2.2f, new Color("#a9b4bf"));
            ci.DrawRect(new Rect2(x - 3f, yy - 1.5f, 6f, 3f), ring);
        }
        // 새는 고리: 김이 뿜어 나온다
        if (v.Leaked && _world.Ship.WallAt(v.LeakWall) is { Breach: > 0f })
        {
            var at = CellRect(v.LeakWall).GetCenter();
            for (int i = 0; i < 7; i++)
            {
                float t = (_time * 0.9f + i / 7f) % 1f;
                var p = at + new Vector2((DkHash(i, 3) - 0.5f) * 14f, 0f) + new Vector2(DkHash(i, 9) - 0.5f, 1f) * (t * 26f);
                ci.DrawCircle(p, 1.5f + 3f * t, DkFrost with { A = 0.5f * (1f - t) });
            }
        }
    }

    private void PaintSealGauge(CanvasItem ci, DockVisit v)
    {
        if (v.Stage != DockStage.Seal || v.HatchDoor < 0 || _world.Dock.Inner(_world.Ship.Doors[v.HatchDoor]) is not Cell inner) return;
        var c = CellRect(inner).GetCenter() + new Vector2(T * 0.3f, -T * 0.32f);
        ci.DrawCircle(c, 7f, new Color("#e9e4d6"));
        ci.DrawArc(c, 7f, 0f, Mathf.Tau, 20, DkHullDark, 1.5f, true);
        ci.DrawArc(c, 5.5f, Mathf.Pi * 0.75f, Mathf.Pi * 1.05f, 6, DkRed, 1.6f, true);
        float sag = v.LeakRead > 0.4f ? 0.35f : 0f;
        float a = Mathf.Pi * (0.8f + 1.4f * Mathf.Clamp(v.SealWork - sag + 0.03f * Mathf.Sin(_time * 7f), 0f, 1f));
        ci.DrawLine(c, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 5.5f, DkSoot, 1.4f, true);
        ci.DrawCircle(c, 1.2f, DkSoot);
        // 호스: 계기에서 해치까지
        var h = CellRect(_world.Ship.Doors[v.HatchDoor].Cell).GetCenter();
        ci.DrawPolyline(new[] { c + new Vector2(0f, 7f), (c + h) * 0.5f + new Vector2(0f, 6f), h }, new Color("#3b3f45"), 1.6f, true);
    }

    // ─────────────────────────────── 난파선 ───────────────────────────────

    private void PaintWreck(CanvasItem ci, DockVisit v, bool fine)
    {
        var outer = new Rect2(v.X0 * T, v.Y0 * T, v.W * T, v.H * T);
        float cut = v.Rooms.Count == 0 ? 0f : v.Rooms.Sum(r => r.Pieces == 0 ? 0f : (float)r.Cut / r.Pieces) / v.Rooms.Count;
        // 선체 (생김 넷)
        var hullCol = v.Hull switch { 0 => DkHull, 1 => new Color("#6f6a62"), 2 => new Color("#4f5a52"), _ => new Color("#6a5548") };
        ci.DrawRect(outer, hullCol);
        switch (v.Hull)
        {
            case 0: // 각진 판 · 리벳
                for (float x = outer.Position.X + 8f; x < outer.End.X; x += T * 0.9f) ci.DrawLine(new Vector2(x, outer.Position.Y), new Vector2(x, outer.End.Y), DkHullDark, 1f);
                if (fine) for (float x = outer.Position.X + 4f; x < outer.End.X; x += 9f) { ci.DrawCircle(new Vector2(x, outer.Position.Y + 3f), 0.9f, DkHullDark); ci.DrawCircle(new Vector2(x, outer.End.Y - 3f), 0.9f, DkHullDark); }
                break;
            case 1: // 둥근 마디
                for (float x = outer.Position.X + T; x < outer.End.X; x += T * 1.6f) ci.DrawArc(new Vector2(x, outer.GetCenter().Y), outer.Size.Y * 0.55f, -0.6f, 0.6f, 10, DkHullDark, 2f, true);
                break;
            case 2: // 트러스 · 덧댄 판
                for (float x = outer.Position.X; x < outer.End.X - T; x += T) ci.DrawLine(new Vector2(x, outer.Position.Y), new Vector2(x + T, outer.Position.Y + 6f), DkHullDark, 1.2f);
                for (int i = 0; i < 4; i++) ci.DrawRect(new Rect2(outer.Position.X + DkHash(v.Id, i) * (outer.Size.X - 14f), outer.End.Y - 9f, 12f, 7f), new Color("#7d8a80"));
                break;
            default: // 줄무늬 화물선
                for (float x = outer.Position.X; x < outer.End.X; x += 14f) ci.DrawColoredPolygon(new[] { new Vector2(x, outer.End.Y - 6f), new Vector2(x + 7f, outer.End.Y - 6f), new Vector2(x + 3f, outer.End.Y), new Vector2(x - 4f, outer.End.Y) }, new Color("#c9a13a", 0.8f));
                break;
        }
        // 그을음 · 찢긴 구멍 · 꺾인 안테나
        var hole = outer.End - new Vector2(T * 0.6f, outer.Size.Y * 0.6f);
        ci.DrawCircle(hole, T * 0.9f, DkSoot with { A = 0.55f });
        ci.DrawColoredPolygon(new[] { hole + new Vector2(-8, -10), hole + new Vector2(4, -6), hole + new Vector2(10, 4), hole + new Vector2(-2, 9), hole + new Vector2(-9, 2) }, new Color(0.01f, 0.01f, 0.03f));
        var ant = new Vector2(outer.Position.X + T * 1.2f, outer.Position.Y);
        ci.DrawLine(ant, ant + new Vector2(0f, -T * 0.7f), DkHullDark, 1.5f);
        ci.DrawLine(ant + new Vector2(0f, -T * 0.7f), ant + new Vector2(T * 0.45f, -T * 0.45f), DkHullDark, 1.5f);
        // 안: 방마다
        foreach (var r in v.Rooms) PaintWreckRoom(ci, v, r, fine);
        // 칸막이 구멍 · 도킹 구멍
        foreach (var c in v.Moored) if (c.Y >= v.Y0 && v.RoomAt(c) == null) ci.DrawRect(CellRect(c), new Color(0.03f, 0.035f, 0.05f));
        // 잘라 간 만큼: 외판이 사라지고 골조만 (오른쪽부터)
        if (cut > 0f)
        {
            float w = outer.Size.X * cut * 0.85f;
            var bare = new Rect2(outer.End.X - w, outer.Position.Y, w, outer.Size.Y);
            ci.DrawRect(bare, new Color(0f, 0f, 0f, 0.82f));
            for (float x = bare.Position.X + 3f; x < bare.End.X; x += 10f) ci.DrawLine(new Vector2(x, bare.Position.Y), new Vector2(x, bare.End.Y), new Color("#4a5059"), 1.5f);
            ci.DrawLine(bare.Position, new Vector2(bare.End.X, bare.Position.Y), new Color("#4a5059"), 2f);
            ci.DrawLine(new Vector2(bare.Position.X, bare.End.Y), bare.End, new Color("#4a5059"), 2f);
            if (fine) for (float y = bare.Position.Y; y < bare.End.Y; y += 4f) ci.DrawLine(new Vector2(bare.Position.X, y), new Vector2(bare.Position.X + 2f, y + 2f), DkOrange with { A = 0.5f }, 1f); // 절단 자국
        }
        if (fine) Gfx.TextCentered(ci, Fonts.Bold, new Vector2(outer.GetCenter().X, outer.Position.Y - 6f), v.Name, 9, new Color(0.8f, 0.83f, 0.88f, 0.75f));
    }

    private void PaintWreckRoom(CanvasItem ci, DockVisit v, WreckRoom r, bool fine)
    {
        var w = _world;
        var rr = new Rect2(r.X0 * T, r.Y0 * T, (r.X1 - r.X0 + 1) * T, (r.Y1 - r.Y0 + 1) * T);
        ci.DrawRect(rr, new Color(0.035f, 0.04f, 0.055f));
        if (fine) for (float x = rr.Position.X + T; x < rr.End.X; x += T) ci.DrawLine(new Vector2(x, rr.End.Y - 2f), new Vector2(x, rr.End.Y), new Color(1f, 1f, 1f, 0.06f), 1f);
        var cpos = rr.GetCenter();
        Color dim = new(0.32f, 0.34f, 0.38f);
        switch (r.Kind)
        {
            case 0: // 조종실: 꺼진 화면 · 의자
                ci.DrawArc(new Vector2(cpos.X, rr.Position.Y + 2f), rr.Size.X * 0.42f, 0.25f, Mathf.Pi - 0.25f, 14, dim, 4f, true);
                for (int i = -1; i <= 1; i++) ci.DrawRect(new Rect2(cpos.X + i * 12f - 4f, rr.Position.Y + 9f, 8f, 5f), new Color(0.06f, 0.08f, 0.1f));
                ci.DrawRect(new Rect2(cpos.X - 4f, cpos.Y + 3f, 8f, 8f), dim.Darkened(0.3f));
                break;
            case 1: // 선실: 침대 · 떠다니는 컵과 사진
                for (int i = 0; i < 2; i++) { var b = new Rect2(rr.Position.X + 3f + i * 26f, rr.End.Y - 11f, 22f, 8f); ci.DrawRect(b, dim); ci.DrawRect(new Rect2(b.Position.X, b.Position.Y, 6f, 8f), dim.Lightened(0.15f)); }
                {
                    var cup = cpos + new Vector2(Mathf.Sin(_time * 0.4f) * 6f, Mathf.Cos(_time * 0.33f) * 4f - 6f);
                    ci.DrawRect(new Rect2(cup.X - 2f, cup.Y - 2.5f, 4f, 5f), new Color("#8f8f9a"));
                    var ph = cpos + new Vector2(Mathf.Cos(_time * 0.27f) * 10f, Mathf.Sin(_time * 0.31f) * 3f + 2f);
                    ci.DrawSetTransform(ph, 0.3f * Mathf.Sin(_time * 0.2f), Vector2.One);
                    ci.DrawRect(new Rect2(-4f, -3f, 8f, 6f), new Color("#d9d2c3"));
                    ci.DrawRect(new Rect2(-3f, -2f, 6f, 3f), new Color("#6f8fb0"));
                    ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                }
                if (r.Survivor || v.Boarded.Count > 0 && r.Searched) // 수면 캡슐 (서리 · 푸른 빛 → 연 뒤엔 빈 캡슐)
                {
                    var pod = new Rect2(rr.End.X - 18f, rr.Position.Y + 4f, 14f, 24f);
                    Gfx.RoundRect(ci, pod, DkFrost with { A = 0.85f }, 6f);
                    if (r.Survivor) ci.DrawRect(pod.Grow(-3f), new Color("#6fc3ff", 0.35f + 0.15f * Mathf.Sin(_time * 1.5f)));
                    else ci.DrawRect(pod.Grow(-3f), new Color(0.05f, 0.06f, 0.08f));
                }
                break;
            case 2: // 화물칸: 끈 맨 상자 · 부서진 상자
                for (int i = 0; i < 3; i++)
                {
                    var bx = new Rect2(rr.Position.X + 4f + i * 15f, rr.End.Y - 13f - (i == 1 ? 11f : 0f), 12f, 11f);
                    ci.DrawRect(bx, new Color("#5a4a35"));
                    ci.DrawLine(bx.Position, bx.End, new Color("#2d2419"), 1f);
                    ci.DrawLine(new Vector2(bx.GetCenter().X, bx.Position.Y), new Vector2(bx.GetCenter().X, bx.End.Y), new Color("#c9a13a", 0.7f), 1.2f);
                }
                ci.DrawColoredPolygon(new[] { cpos + new Vector2(8, -6), cpos + new Vector2(16, -9), cpos + new Vector2(14, -1) }, new Color("#5a4a35"));
                break;
            default: // 기관실: 배관 · 서리 낀 원자로 · 밸브
                ci.DrawLine(new Vector2(rr.Position.X, rr.Position.Y + 6f), new Vector2(rr.End.X, rr.Position.Y + 6f), dim, 3f);
                ci.DrawLine(new Vector2(rr.Position.X + 8f, rr.Position.Y + 6f), new Vector2(rr.Position.X + 8f, rr.End.Y), dim, 3f);
                Gfx.RoundRect(ci, new Rect2(cpos.X - 8f, cpos.Y - 10f, 16f, 22f), new Color("#3f4650"), 5f);
                ci.DrawRect(new Rect2(cpos.X - 8f, cpos.Y - 10f, 16f, 5f), DkFrost with { A = 0.55f });
                ci.DrawArc(new Vector2(rr.End.X - 9f, cpos.Y), 4f, 0f, Mathf.Tau, 10, new Color("#9a3b2e"), 1.6f, true);
                break;
        }
        // 위험
        switch (r.Hazard)
        {
            case 1:
            {
                var a = new Vector2(rr.Position.X + rr.Size.X * 0.7f, rr.Position.Y);
                var pts = new[] { a, a + new Vector2(5f, 9f), a + new Vector2(1f, 11f), a + new Vector2(8f, 17f), a + new Vector2(-3f, 12f), a + new Vector2(-6f, 4f) };
                ci.DrawColoredPolygon(pts, new Color("#b9c2cc"));
                if (r.HazardKnown) for (int i = 0; i < 4; i++) ci.DrawLine(a + new Vector2(-8f + i * 4f, 20f), a + new Vector2(-6f + i * 4f, 23f), i % 2 == 0 ? DkAmber : DkSoot, 2f);
                break;
            }
            case 2:
            {
                var bx = r.X1 >= v.X0 + v.W - 2 ? rr.Position.X : rr.End.X - 3f;
                if (!r.Collapsed)
                {
                    ci.DrawPolyline(new[] { new Vector2(bx, rr.Position.Y + 3f), new Vector2(bx + 3f, rr.Position.Y + 9f), new Vector2(bx - 1f, rr.Position.Y + 15f), new Vector2(bx + 2f, rr.End.Y - 4f) }, new Color("#1b1d20"), 1.5f);
                    for (int i = 0; i < 5; i++) { float t = (_time * 0.25f + i * 0.2f) % 1f; ci.DrawCircle(new Vector2(bx + (DkHash(i, r.Id) - 0.5f) * 10f, rr.Position.Y + t * rr.Size.Y), 0.9f, new Color(0.7f, 0.68f, 0.6f, 0.4f * (1f - t))); }
                }
                else ci.DrawLine(new Vector2(bx, rr.Position.Y), new Vector2(bx + (bx > rr.GetCenter().X ? -1f : 1f) * rr.Size.X * 0.6f, rr.End.Y - 2f), new Color("#4a4f57"), 5f);
                break;
            }
            case 3:
            {
                var y = rr.End.Y - 6f;
                ci.DrawLine(new Vector2(rr.Position.X, y), new Vector2(rr.End.X, y), new Color("#8a6d3b"), 3f);
                for (float x = rr.Position.X + 3f; x < rr.End.X; x += 7f)
                {
                    ci.DrawLine(new Vector2(x, y - 3f), new Vector2(x + 2f, y + 3f), DkFrost, 1f);
                    ci.DrawLine(new Vector2(x + 2f, y - 3f), new Vector2(x, y + 3f), DkFrost, 1f);
                }
                if (r.HazardKnown) ci.DrawCircle(new Vector2(rr.End.X - 6f, y - 8f), 3f, DkAmber with { A = 0.6f + 0.3f * Mathf.Sin(_time * 3f) });
                break;
            }
        }
        // 남은 기록: 못 찾은 건 깜빡이고, 찾은 자리는 빈 윤곽
        foreach (var rec in v.Records.Where(x => x.Room == r.Id))
        {
            var at = rr.Position + new Vector2(rr.Size.X * (0.25f + 0.15f * rec.Kind), rr.Size.Y * 0.35f);
            if (rec.FoundBy >= 0) { ci.DrawRect(new Rect2(at.X - 3f, at.Y - 2f, 6f, 4f), new Color(1f, 1f, 1f, 0.15f), false, 1f); continue; }
            float blink = Mathf.Sin(_time * (rec.Kind == 3 ? 5f : 2f) + rec.Kind) > 0.3f ? 1f : 0.25f;
            switch (rec.Kind)
            {
                case 0: ci.DrawRect(new Rect2(at.X - 3f, at.Y - 2f, 6f, 4f), new Color("#2d3138")); ci.DrawCircle(at + new Vector2(2f, -1f), 1f, DkAmber with { A = blink }); break;
                case 1: ci.DrawSetTransform(at + new Vector2(0f, Mathf.Sin(_time * 0.6f) * 2f), 0.2f, Vector2.One); ci.DrawRect(new Rect2(-3f, -2f, 6f, 4f), new Color("#e8e2d0", 0.85f)); ci.DrawLine(new Vector2(-2f, 0f), new Vector2(2f, 0f), new Color("#6a6a70"), 0.6f); ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One); break;
                case 2: for (int i = 0; i < 4; i++) ci.DrawLine(at + new Vector2(-4f + i * 2.5f, -3f), at + new Vector2(-3f + i * 2.5f, 3f), new Color(0.75f, 0.75f, 0.8f, 0.35f), 0.8f); break;
                default: ci.DrawRect(new Rect2(at.X - 2.5f, at.Y - 3.5f, 5f, 7f), new Color("#1e2126")); ci.DrawCircle(at + new Vector2(0f, -2f), 1f, DkRed with { A = blink }); break;
            }
        }
        // 뒤진 방: 입구 문틀에 주황 X
        if (r.Searched)
        {
            var m = new Vector2(rr.Position.X + 6f, rr.Position.Y + 6f);
            ci.DrawLine(m + new Vector2(-3f, -3f), m + new Vector2(3f, 3f), DkOrange, 1.6f);
            ci.DrawLine(m + new Vector2(3f, -3f), m + new Vector2(-3f, 3f), DkOrange, 1.6f);
        }
        // 자르는 곳: 불꽃
        if (r.CutBy >= 0 && r.CutBy < w.Crew.Count && w.Dock.WreckRoomOf(w.Crew[r.CutBy]) == r)
        {
            var at = CrewPx(w.Crew[r.CutBy]) + new Vector2(0f, -T * 0.5f);
            ci.DrawCircle(at, 3f, new Color("#e6f4ff", 0.8f));
            for (int i = 0; i < 6; i++) { float t = (_time * 2.5f + i / 6f) % 1f; var dir = new Vector2(DkHash(i, 1) - 0.5f, DkHash(i, 2) - 0.3f).Normalized(); ci.DrawLine(at + dir * t * 12f, at + dir * (t * 12f + 3f), new Color("#ffd27a", 1f - t), 1f); }
        }
    }

    // ─────────────────────────────── 거룻배 ───────────────────────────────

    private void PaintTender(CanvasItem ci, DockVisit v, bool fine)
    {
        var w = _world;
        var body = new Rect2(v.X0 * T, v.Y0 * T, v.W * T, v.H * T);
        var hull = v.Hull switch { 0 => new Color("#d7dde4"), 1 => new Color("#c2cbd2"), 2 => new Color("#e3d9c7"), _ => new Color("#b9c7d6") };
        Gfx.RoundRect(ci, body, hull, T * 0.8f, new Color("#6a7480"), 2);
        var stripe = v.Hull switch { 0 => new Color("#2f6fb0"), 1 => new Color("#b0442f"), 2 => DkTeal, _ => new Color("#7a4fb0") };
        ci.DrawRect(new Rect2(body.Position.X + T * 0.6f, body.GetCenter().Y + 4f, body.Size.X - T * 1.2f, 4f), stripe);
        for (float x = body.Position.X + T; x < body.End.X - T; x += T * 0.9f) Gfx.RoundRect(ci, new Rect2(x, body.Position.Y + 8f, 10f, 7f), new Color("#ffd98a", 0.85f), 3f); // 따뜻한 창
        ci.DrawColoredPolygon(new[] { new Vector2(body.End.X, body.Position.Y + 8f), new Vector2(body.End.X + 14f, body.GetCenter().Y), new Vector2(body.End.X, body.End.Y - 8f) }, new Color("#7a8591"));
        float flick = 0.6f + 0.4f * Mathf.Sin(_time * 9f);
        ci.DrawCircle(new Vector2(body.End.X + 16f, body.GetCenter().Y), 3f, new Color("#9fd8ff", 0.5f * flick));
        if (fine) Gfx.TextCentered(ci, Fonts.Bold, new Vector2(body.GetCenter().X, body.End.Y - 6f), v.Name, 9, stripe.Darkened(0.3f));
        if (v.Stage != DockStage.Open || v.HatchDoor < 0 || w.Dock.Inner(w.Ship.Doors[v.HatchDoor]) is not Cell inner) return;
        var ip = CellRect(inner).GetCenter();
        // 교환 상자
        if (!v.TradeDone)
            for (int i = 0; i < 3; i++)
            {
                var bx = new Rect2(ip.X - 14f + i * 9f, ip.Y + 6f - (i == 1 ? 7f : 0f), 8f, 7f);
                ci.DrawRect(bx, i == 1 ? stripe.Lightened(0.2f) : new Color("#8d7650"));
                ci.DrawLine(bx.Position + new Vector2(0f, 2f), new Vector2(bx.End.X, bx.Position.Y + 2f), new Color(1f, 1f, 1f, 0.4f), 1f);
            }
        PaintVisitor(ci, ip + new Vector2(-T * 0.4f, -T * 0.2f), stripe, false);
        if (!v.JointDone && v.JointBy >= 0 && w.Dock.JointOf(v) is Machine m && v.JointWork > 0f)
            PaintVisitor(ci, new Vector2(m.Body.Center.X, m.Body.Center.Y) * T + new Vector2(T * 0.55f, T * 0.1f), stripe, true);
    }

    /// <summary>저쪽 배 사람: 청록 외투 · 챙 모자 · (기관사면) 공구 상자.</summary>
    private void PaintVisitor(CanvasItem ci, Vector2 p, Color stripe, bool tools)
    {
        float bob = Mathf.Sin(_time * 2f + p.X) * 0.8f;
        ci.DrawColoredPolygon(new[] { p + new Vector2(-6f, 9f + bob), p + new Vector2(6f, 9f + bob), p + new Vector2(4f, -2f + bob), p + new Vector2(-4f, -2f + bob) }, DkTeal);
        ci.DrawLine(p + new Vector2(-5f, 4f + bob), p + new Vector2(5f, 4f + bob), stripe, 1.5f);
        ci.DrawCircle(p + new Vector2(0f, -6f + bob), 4.2f, new Color("#e2b997"));
        ci.DrawRect(new Rect2(p.X - 6f, p.Y - 10f + bob, 12f, 2f), new Color("#22343a"));
        ci.DrawRect(new Rect2(p.X - 3.5f, p.Y - 13f + bob, 7f, 3.5f), new Color("#22343a"));
        if (tools) { ci.DrawRect(new Rect2(p.X + 6f, p.Y + 4f, 9f, 6f), DkRed.Darkened(0.2f)); ci.DrawLine(new Vector2(p.X + 8f, p.Y + 4f), new Vector2(p.X + 13f, p.Y + 4f), DkSoot, 1.2f); }
    }

    // ─────────────────────────────── 추모 ───────────────────────────────

    private void PaintPlaque(CanvasItem ci, DockVisit v)
    {
        var r = CellRect(v.Plaque);
        var plate = new Rect2(r.Position.X + 4f, r.Position.Y - 2f, T - 8f, 11f);
        Gfx.RoundRect(ci, plate, DkBrass, 2f, DkBrass.Darkened(0.4f), 1);
        for (int i = 0; i < Math.Min(4, v.Crew.Count); i++) ci.DrawLine(new Vector2(plate.Position.X + 3f, plate.Position.Y + 3f + i * 2f), new Vector2(plate.End.X - 3f - DkHash(i, v.Id) * 6f, plate.Position.Y + 3f + i * 2f), DkBrass.Darkened(0.45f), 0.8f);
        ci.DrawLine(new Vector2(plate.End.X - 4f, plate.Position.Y), new Vector2(plate.End.X - 2f, plate.End.Y + 4f), new Color("#2b2b33"), 1.5f); // 검은 리본
        if (!v.Mourned) // 모이는 동안 작은 촛불
            for (int i = 0; i < 3; i++)
            {
                var c = new Vector2(plate.Position.X + 5f + i * 8f, plate.End.Y + 9f);
                ci.DrawRect(new Rect2(c.X - 1.5f, c.Y, 3f, 5f), new Color("#efe7d6"));
                ci.DrawCircle(c + new Vector2(0f, -1.5f + 0.4f * Mathf.Sin(_time * 8f + i)), 1.8f, new Color("#ffcf6b", 0.9f));
            }
    }

    // ─────────────────────────────── 사람 위: 헬멧 등 · 승객 ───────────────────────────────

    /// <summary>동적 층(사람 위): 난파선 안의 헬멧 등 원뿔 · 승객 차림 · 이끄는 손.</summary>
    private void PaintDockOver(CanvasItem ci)
    {
        var w = _world;
        var d = w.Dock;
        if (d.Active is DockVisit v && v.Wreck)
            foreach (var c in w.Crew)
            {
                if (c.Dead || d.WreckRoomOf(c) == null) continue;
                var p = CrewPx(c);
                var f = c.Facing.ToGodot();
                if (f.LengthSquared() < 0.01f) f = Vector2.Right;
                f = f.Normalized();
                var side = new Vector2(-f.Y, f.X);
                float len = T * 2.4f;
                ci.DrawPolygon(new[] { p, p + f * len + side * len * 0.45f, p + f * len - side * len * 0.45f }, new[] { new Color(1f, 0.95f, 0.8f, 0.28f), new Color(1f, 0.95f, 0.8f, 0f), new Color(1f, 0.95f, 0.8f, 0f) });
                ci.DrawCircle(p + f * 4f + new Vector2(0f, -6f), 1.6f, new Color("#fff4cf"));
            }
        if (w.Passengers.All.Count > 0) PaintPassengers(ci);
    }

    private void PaintPassengers(CanvasItem ci)
    {
        var w = _world;
        float s = CrewRadius / 9.5f;
        bool fine = Zoom > 0.8f;
        foreach (var p in w.Passengers.All.Values)
        {
            var c = w.Crew[p.Id];
            if (c.Dead || c.Away || c.Down) continue;
            var at = CrewPx(c) + new Vector2(c.Gait.Aside.X, c.Gait.Aside.Y) * T;
            int look = p.Look;
            int coat = look & 3, pat = (look >> 2) & 3, hat = (look >> 4) & 3, bag = (look >> 6) & 3;
            var main = Color.FromHsv(((look >> 8) & 255) / 255f, 0.45f, 0.62f);
            var second = Color.FromHsv((((look >> 8) & 255) / 255f + 0.45f) % 1f, 0.35f, 0.85f);
            bool sleeping = c.Pose == Pose.Sleeping;
            // 외투
            if (!sleeping)
            {
                Vector2[] shape = coat switch
                {
                    0 => new[] { at + new Vector2(-6f, -1f) * s, at + new Vector2(6f, -1f) * s, at + new Vector2(8f, 11f) * s, at + new Vector2(-8f, 11f) * s }, // 긴 코트
                    1 => new[] { at + new Vector2(-9f, 3f) * s, at + new Vector2(0f, -4f) * s, at + new Vector2(9f, 3f) * s, at + new Vector2(0f, 8f) * s }, // 판초
                    2 => new[] { at + new Vector2(-6f, -2f) * s, at + new Vector2(-2f, -2f) * s, at + new Vector2(-2f, 6f) * s, at + new Vector2(-6f, 6f) * s }, // 조끼(한 쪽)
                    _ => new[] { at + new Vector2(-5f, -2f) * s, at + new Vector2(5f, -2f) * s, at + new Vector2(5f, 9f) * s, at + new Vector2(-5f, 9f) * s }, // 작업복
                };
                ci.DrawColoredPolygon(shape, main with { A = 0.92f });
                if (coat == 2) ci.DrawColoredPolygon(shape.Select(v => new Vector2(2f * at.X - v.X, v.Y)).ToArray(), main with { A = 0.92f });
                if (coat == 3) ci.DrawLine(at + new Vector2(-5f, 3f) * s, at + new Vector2(5f, 3f) * s, second, 1.6f * s);
                if (fine)
                    switch (pat)
                    {
                        case 1: for (int i = 0; i < 3; i++) ci.DrawLine(at + new Vector2(-5f, 1f + i * 3f) * s, at + new Vector2(5f, 1f + i * 3f) * s, second with { A = 0.7f }, 0.9f); break;
                        case 2: for (int i = -1; i <= 1; i++) { ci.DrawLine(at + new Vector2(i * 3f, -1f) * s, at + new Vector2(i * 3f, 9f) * s, second with { A = 0.5f }, 0.8f); ci.DrawLine(at + new Vector2(-5f, 2f + (i + 1) * 3f) * s, at + new Vector2(5f, 2f + (i + 1) * 3f) * s, second with { A = 0.5f }, 0.8f); } break;
                        case 3: for (int i = 0; i < 5; i++) ci.DrawCircle(at + new Vector2(-4f + (i % 3) * 4f, 1f + (i / 3) * 4f) * s, 0.9f * s, second); break;
                    }
            }
            // 모자
            var head = at + new Vector2(0f, -9f) * s;
            switch (hat)
            {
                case 1: ci.DrawCircle(head + new Vector2(1.5f, -3f) * s, 4.2f * s, second); ci.DrawCircle(head + new Vector2(4f, -5f) * s, 1f * s, second.Darkened(0.3f)); break; // 베레
                case 2: ci.DrawRect(new Rect2(head.X - 7f * s, head.Y - 3f * s, 14f * s, 2f * s), main.Darkened(0.3f)); ci.DrawRect(new Rect2(head.X - 3.5f * s, head.Y - 7f * s, 7f * s, 4.5f * s), main.Darkened(0.3f)); break; // 챙 넓은 모자
                case 3: ci.DrawArc(head + new Vector2(0f, -2f) * s, 4.5f * s, Mathf.Pi, Mathf.Tau, 10, second, 3f * s, true); ci.DrawCircle(head + new Vector2(0f, -7.5f) * s, 1.8f * s, Colors.White); break; // 방울 털모자
            }
            // 짐
            if (!sleeping)
                switch (bag)
                {
                    case 0: ci.DrawRect(new Rect2(at.X + 7f * s, at.Y + 2f * s, 6f * s, 8f * s), new Color("#7a4a2b")); ci.DrawRect(new Rect2(at.X + 8.5f * s, at.Y + 0.5f * s, 3f * s, 1.5f * s), DkSoot); break; // 여행 가방
                    case 1: Gfx.RoundRect(ci, new Rect2(at.X - 4f * s, at.Y - 4f * s, 8f * s, 9f * s), second.Darkened(0.35f) with { A = 0.85f }, 3f * s); break; // 배낭
                    case 2: ci.DrawLine(at + new Vector2(-5f, -4f) * s, at + new Vector2(5f, 5f) * s, new Color("#3a2c22"), 1.2f * s); ci.DrawRect(new Rect2(at.X + 4f * s, at.Y + 4f * s, 5f * s, 4f * s), new Color("#a07a4a")); break; // 어깨 가방
                    default: ci.DrawRect(new Rect2(at.X - 12f * s, at.Y + 3f * s, 6f * s, 4f * s), new Color("#b08b55")); ci.DrawArc(new Vector2(at.X - 9f * s, at.Y + 3f * s), 3f * s, Mathf.Pi, Mathf.Tau, 6, new Color("#b08b55"), 1f * s); break; // 바구니
                }
            // 구해 온 사람: 처음 하루 금빛 보온 담요
            if (p.Rescued && w.Tick - p.BoardedAt < SimTime.TicksPerDay)
            {
                float sh = 0.75f + 0.25f * Mathf.Sin(_time * 3f + p.Id);
                ci.DrawColoredPolygon(new[] { at + new Vector2(-8f, -3f) * s, at + new Vector2(8f, -3f) * s, at + new Vector2(6f, 6f) * s, at + new Vector2(-6f, 6f) * s }, DkFoil with { A = 0.7f * sh });
            }
            // 자원봉사: 초록 완장
            if (p.Volunteer && !sleeping)
            {
                ci.DrawRect(new Rect2(at.X - 8.5f * s, at.Y - 1f * s, 3.5f * s, 3f * s), Colors.White);
                ci.DrawRect(new Rect2(at.X - 7.6f * s, at.Y - 0.6f * s, 1.6f * s, 2.2f * s), DkGreen);
            }
            // 따지러 가는 사람: 낙서 구름
            if (p.Gripe != "" && fine)
            {
                var cl = head + new Vector2(8f, -12f) * s;
                for (int i = 0; i < 4; i++) ci.DrawCircle(cl + new Vector2(Mathf.Cos(i * 1.6f) * 4f, Mathf.Sin(i * 1.6f) * 2.5f) * s, 3f * s, new Color(0.25f, 0.25f, 0.3f, 0.85f));
                ci.DrawPolyline(new[] { cl + new Vector2(-4f, 0f) * s, cl + new Vector2(-1f, -2f) * s, cl + new Vector2(1f, 2f) * s, cl + new Vector2(4f, -1f) * s }, DkRed, 1.2f * s);
            }
            // 손잡고 이끄는 선
            if (w.Passengers.Following(c) is { } fw && fw.who >= 0 && fw.who < w.Crew.Count)
            {
                var lp = CrewPx(w.Crew[fw.who]);
                ci.DrawLine(at + new Vector2(0f, 2f), lp + new Vector2(0f, 2f), new Color("#ffe3a8", 0.85f), 2f, true);
                ci.DrawCircle((at + lp) * 0.5f + new Vector2(0f, 2f), 2.2f, new Color("#e2b997"));
            }
        }
    }
}
