using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.5b 기술과 설비가 티 나는 그림 — 지금 배의 미감 세트(TechLookTable.Sets)를 화면 색으로 옮긴다.
///   초기: 벽에 리벳 줄 · 늘어진 노출 전선 · 칠 벗겨진 자리(붉은 밑칠) · 끝마개 달린 형광등(가끔 떤다) · 바닥 모서리 줄무늬 철판
///   중간: 이음매 패널과 나사 · 덮개 씌운 배선 트레이 · 짧은 LED 띠 · 방 색 띠 · 고무 걸레받이
///   고급: 이음매 없는 매끈한 판(광택 띠) · 숨은 배선(평평한 점검창만) · 벽과 바닥 사이 간접 조명 · 둥근 안쪽 모서리
/// 설비 테두리(FixtureArtTier.cs) · 기술 모습(TechVisuals*.cs) · 개조 칸 새것 · 설치 순간(TechMoments.cs)도 이 세트를 읽는다.
/// </summary>
public static class TechLook
{
    /// <summary>지금 배의 미감 세트 (ShipView 가 매 프레임 맞춘다 — 그리기만 읽는다).</summary>
    public static LookSet Now { get; private set; } = TechLookTable.Of(LookLevel.Early);
    /// <summary>다음 단계까지 0~1.</summary>
    public static float Toward { get; private set; }

    /// <summary>세트가 바뀔 때마다 하나씩 는다 (텍스처 생성기가 다시 구울 때를 안다).</summary>
    public static int Version { get; private set; }

    internal static void Set(LookSet s, float toward)
    {
        if (s != Now) Version++;
        Now = s;
        Toward = toward;
    }

    public static Color C(uint rgba) => new(rgba);
    public static Color Wall => C(Now.Wall);
    public static Color Edge => C(Now.WallEdge);
    public static Color Panel => C(Now.Panel);
    public static Color Trim => C(Now.Trim);
    public static Color Light => C(Now.Light);
    public static Color Primer => C(Now.Primer);
    public static Color Accent => C(Now.Accent);

    /// <summary>기술 색 (아이콘과 같은 계열).</summary>
    public static Color Field(string key) => TechLookTable.TechOf(key) is EraTech t ? TechIcons.FieldColor(t.Field) : new Color("#8fa6ff");
}

public partial class ShipView
{
    /// <summary>바닥 칸과 벽 칸 사이 면 하나: O = 경계 가운데, Tn = 경계를 따라, Nm = 방 쪽.</summary>
    internal readonly struct Face
    {
        public readonly Cell Floor;
        public readonly Cell Dir;
        public readonly Room Room;
        public readonly Vector2 O, Tn, Nm;
        public readonly bool Hull;
        public readonly bool Open;
        public Face(Cell floor, Cell dir, Room room, bool hull, bool open)
        {
            Floor = floor; Dir = dir; Room = room; Hull = hull; Open = open;
            var r = CellRect(floor);
            Nm = new Vector2(-dir.X, -dir.Y);
            Tn = new Vector2(Mathf.Abs(dir.Y), Mathf.Abs(dir.X));
            O = r.GetCenter() + new Vector2(dir.X, dir.Y) * (T * 0.5f);
        }
        /// <summary>a = 경계를 따라(−16~16), b = 방 쪽(+) · 벽 속(−).</summary>
        public Vector2 L(float a, float b) => O + Tn * a + Nm * b;
        public bool Top => Dir.Y == -1;
        public Vector2[] Quad(float a0, float b0, float a1, float b1) => new[] { L(a0, b0), L(a1, b0), L(a1, b1), L(a0, b1) };
    }

    /// <summary>외벽 칸 하나와 우주 쪽 방향.</summary>
    internal readonly struct HullSeg
    {
        public readonly Cell Cell;
        public readonly Vector2 C, Out, Tn;
        public readonly int K;
        public HullSeg(Cell c, Cell outDir, int k)
        {
            Cell = c; K = k;
            C = CellRect(c).GetCenter();
            Out = new Vector2(outDir.X, outDir.Y);
            Tn = new Vector2(Mathf.Abs(outDir.Y), Mathf.Abs(outDir.X));
        }
        /// <summary>a = 외판을 따라, b = 우주 쪽(+).</summary>
        public Vector2 L(float a, float b) => C + Tn * a + Out * b;
    }

    private DrawLayer? _techStatic, _techGlow;
    private int _techSig = int.MinValue, _techGeoVersion = -1, _techFrame, _techRemodelSig;
    private readonly List<Face> _faces = new();
    private readonly List<HullSeg> _hullSegs = new();
    private readonly Dictionary<int, List<Face>> _roomFaces = new();
    private readonly Dictionary<int, List<Face>> _roomSlots = new();
    private List<VisualRow>? _techActive;

    /// <summary>Init 에서 한 줄: 기술 모습 정적 층 · 간접 조명 층 (더하기 섞기).</summary>
    private void AddTechLookLayers()
    {
        _techStatic = new DrawLayer { Name = "TechLook", Painter = PaintTechStatic };
        AddChild(Baked(_techStatic)); // v17.7 구워 둔다
        _techGlow = new DrawLayer { Name = "TechGlow", Painter = PaintTechGlow, Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add } };
        AddChild(_techGlow);
    }

    /// <summary>매 프레임: 세트 · 익힌 기술 · 갈림길 · 개조 칸 새것 단계가 바뀌면 다시 그린다.</summary>
    private void UpdateTechLook()
    {
        if (_techStatic == null) return;
        var w = _world;
        var lv = TechLookTable.LevelOf(w);
        float toward = TechLookTable.Toward(w);
        int sig = (int)lv * 7919 + w.Eras.Known.Count * 131 + w.Structure.Version * 31 + (int)(toward * 4f);
        foreach (var f in TechWeb.Forks) sig = unchecked(sig * 17 + (w.TechWeb?.Side(f.Id) ?? -1) + 2);
        if (_techFrame++ % 30 == 0) // 개조 칸 새것 단계는 천천히 바뀐다 — 30프레임마다만 센다
        {
            int rs = 0;
            foreach (var r in w.Ship.LiveRooms)
            {
                float fr = TechLookTable.Freshness(w.Tick, TechLookTable.RemodelSince(w, r));
                if (fr > 0f) rs = unchecked(rs * 31 + r.Id * 13 + (int)(fr * 8f) + 1);
            }
            _techRemodelSig = rs;
        }
        sig = unchecked(sig * 31 + _techRemodelSig);
        if (sig != _techSig)
        {
            bool levelChanged = TechLook.Now.Level != lv;
            TechLook.Set(TechLookTable.Of(lv), toward);
            _techActive = TechLookTable.Active(w);
            if (_techSig != int.MinValue && levelChanged) RedrawStatic(); // 설비 테두리도 세트를 따른다
            _techSig = sig;
            _techStatic.QueueRedraw();
        }
        _techGlow!.QueueRedraw(); // 간접 조명 · 형광등 떨림 · 기술 빛 (가벼운 층 — 매 프레임)
    }

    /// <summary>면 · 외벽 · 방마다 벽 자리 (구조가 바뀔 때만).</summary>
    private void BuildTechGeometry()
    {
        var ship = _world.Ship;
        var g = ship.Grid;
        _faces.Clear(); _hullSegs.Clear(); _roomFaces.Clear(); _roomSlots.Clear();
        for (int i = 0; i < g.CellCount; i++)
        {
            var c = g.CellAt(i);
            var k = g.Kind(c);
            if (k == TileKind.Floor && ship.RoomAt(c) is Room room && !room.Detached)
            {
                foreach (var d in Cell.Dirs4)
                {
                    var wc = c + d;
                    if (g.Kind(wc) != TileKind.Wall) continue;
                    bool hull = Cell.Dirs4.Any(dd => g.Kind(wc + dd) == TileKind.Void);
                    bool open = ship.IsOpenFloor(c) && !Cell.Dirs4.Any(dd => g.Kind(c + dd) == TileKind.Door);
                    var f = new Face(c, d, room, hull, open);
                    _faces.Add(f);
                    if (!_roomFaces.TryGetValue(room.Id, out var l)) _roomFaces[room.Id] = l = new List<Face>();
                    l.Add(f);
                }
            }
            else if (k == TileKind.Wall)
            {
                foreach (var d in Cell.Dirs4)
                    if (g.Kind(c + d) == TileKind.Void) _hullSegs.Add(new HullSeg(c, d, _hullSegs.Count));
            }
        }
        foreach (var (id, l) in _roomFaces)
            _roomSlots[id] = l.Where(f => f.Open).OrderBy(f => f.Top ? 0 : f.Dir.Y == 1 ? 2 : 1).ThenBy(f => f.Floor.Y).ThenBy(f => f.Floor.X).ToList();
        _techGeoVersion = _world.Structure.Version;
    }

    // ═══════════════════════════════ 정적 층 ═══════════════════════════════

    private void PaintTechStatic(CanvasItem ci)
    {
        if (_techGeoVersion != _world.Structure.Version) BuildTechGeometry();
        var s = TechLook.Now;
        PaintLookFloor(ci, s);
        foreach (var f in _faces) PaintLookFace(ci, f, s);
        PaintRemodelFresh(ci); // TechMoments.cs — 개조 칸 새 패널
        PaintVisualsStatic(ci); // TechVisuals.cs — 익힌 기술의 배 모습
    }

    /// <summary>
    /// 바닥 판 이음 (무늬는 텍스처 생성기 몫 — 여기는 판 크기 · 죔쇠만): 초기 한 칸 판 · 모서리 리벳 / 중간 두 칸 판 · 나사 / 고급 세 칸 판 · 가는 빛 이음.
    /// 가구 밑은 건너뛴다 (정적 층이 가구 위에 있다).
    /// </summary>
    private void PaintLookFloor(CanvasItem ci, LookSet s)
    {
        var ship = _world.Ship;
        int step = Mathf.Max(1, Mathf.RoundToInt(s.PlateScale + 0.25f));
        var seam = s.Frame == FrameStyle.Seamless ? TechLook.C(s.Trim).WithAlpha(0.12f) : new Color(0, 0, 0, s.Frame == FrameStyle.Riveted ? 0.32f : 0.24f);
        var fast = TechLook.C(s.WallEdge);
        foreach (var room in ship.LiveRooms)
            foreach (var c in room.Cells)
            {
                if (!ship.IsOpenFloor(c)) continue;
                var r = CellRect(c);
                bool vx = Mathf.PosMod(c.X, step) == 0, hy = Mathf.PosMod(c.Y, step) == 0;
                if (vx) ci.DrawLine(r.Position, new Vector2(r.Position.X, r.End.Y), seam, 1f);
                if (hy) ci.DrawLine(r.Position, new Vector2(r.End.X, r.Position.Y), seam, 1f);
                if (!(vx && hy)) continue;
                switch (s.Frame)
                {
                    case FrameStyle.Riveted:
                        foreach (var d in new[] { new Vector2(2.5f, 2.5f), new Vector2(-2.5f, 2.5f), new Vector2(2.5f, -2.5f), new Vector2(-2.5f, -2.5f) })
                            ci.DrawCircle(r.Position + d, 0.9f, fast.WithAlpha(0.45f), true, -1f, true);
                        break;
                    case FrameStyle.Seamed:
                        ci.DrawCircle(r.Position + new Vector2(3f, 3f), 0.8f, fast.WithAlpha(0.4f), true, -1f, true);
                        break;
                    default:
                        ci.DrawLine(r.Position + new Vector2(1f, 1f), r.Position + new Vector2(T * 0.6f, 1f), new Color(1, 1, 1, 0.06f + 0.06f * s.Gloss), 1f);
                        break;
                }
            }
    }

    /// <summary>벽 면 하나를 세트대로: 리벳 · 노출 전선 · 벗겨진 칠 / 이음매 · 트레이 · 색 띠 / 매끈 광택 · 점검창 · 둥근 모서리.</summary>
    private static void PaintLookFace(CanvasItem ci, in Face f, LookSet s)
    {
        int h = f.Floor.X * 73 + f.Floor.Y * 191 + f.Dir.X * 7 + f.Dir.Y * 3;
        var edge = TechLook.C(s.WallEdge);
        var panel = TechLook.C(s.Panel);
        float half = T * 0.5f;
        switch (s.Frame)
        {
            case FrameStyle.Riveted:
            {
                // 벽 쪽 띠: 덧댄 철판 + 리벳 줄
                ci.DrawColoredPolygon(f.Quad(-half, -7f, half, -1f), panel.WithAlpha(0.55f));
                for (float a = -half + s.RivetStep * 0.5f; a < half; a += s.RivetStep)
                {
                    ci.DrawCircle(f.L(a, -4f), 1.1f, edge.Lightened(0.15f), true, -1f, true);
                    ci.DrawCircle(f.L(a + 0.4f, -3.6f), 0.5f, new Color(0, 0, 0, 0.5f), true, -1f, true);
                }
                // 벗겨진 칠: 붉은 밑칠이 드러난 얼룩
                if (FixtureArt.Hash(h, 1, 401) < s.Peel)
                {
                    float a0 = (FixtureArt.Hash(h, 2, 401) - 0.5f) * 18f;
                    var pts = new Vector2[6];
                    for (int k = 0; k < 6; k++)
                    {
                        float ang = k * Mathf.Tau / 6f;
                        float rr = 2.2f + 2.2f * FixtureArt.Hash(h, k, 402);
                        pts[k] = f.L(a0 + Mathf.Cos(ang) * rr * 1.6f, -4f + Mathf.Sin(ang) * rr * 0.7f);
                    }
                    ci.DrawColoredPolygon(pts, TechLook.C(s.Primer).WithAlpha(0.75f));
                    ci.DrawPolyline(pts.Append(pts[0]).ToArray(), new Color(0.85f, 0.85f, 0.8f, 0.35f), 0.8f, true);
                }
                // 바닥 모서리 줄무늬 철판 (킥 플레이트)
                for (float a = -half + 2f; a < half - 2f; a += 5f)
                    ci.DrawLine(f.L(a, 1.5f), f.L(a + 2.5f, 3.5f), new Color(1, 1, 1, 0.07f), 1f, true);
                // 노출 전선: 두 가닥이 클립 사이로 처진다 (두 면 중 하나)
                if (s.Wires == WireStyle.Exposed && (f.Floor.X + f.Floor.Y) % 2 == 0 && !f.Hull)
                {
                    var a = f.L(-half, -8.5f);
                    var b = f.L(half, -8.5f);
                    FixtureArt.Cable(ci, a, b, 0.9f, new Color("#2a2420"), 1.6f);
                    FixtureArt.Cable(ci, a + f.Nm * 1.6f, b + f.Nm * 1.6f, 0.6f, new Color("#7a3a22"), 1.1f);
                    ci.DrawRect(new Rect2(f.L(-half, -10f), new Vector2(2.2f, 2.2f)), new Color("#9aa3b5"));
                }
                break;
            }
            case FrameStyle.Seamed:
            {
                ci.DrawColoredPolygon(f.Quad(-half, -6f, half, -1f), panel.WithAlpha(0.5f));
                // 이음매 (칸 두 개 반마다) + 나사 둘
                if ((f.Floor.X + f.Floor.Y) % 3 == 0)
                {
                    ci.DrawLine(f.L(-half + 1f, -6f), f.L(-half + 1f, -1f), new Color(0, 0, 0, 0.45f), 1f);
                    ci.DrawCircle(f.L(-half + 4f, -4.5f), 0.9f, edge, true, -1f, true);
                    ci.DrawCircle(f.L(-half + 4f, -2.2f), 0.9f, edge, true, -1f, true);
                }
                // 방 색 띠
                ci.DrawLine(f.L(-half, -2.2f), f.L(half, -2.2f), Palette.Room(f.Room.Kind).WithAlpha(0.35f), 1.4f);
                // 덮개 씌운 배선 트레이
                if (s.Wires == WireStyle.Tray && !f.Hull)
                {
                    ci.DrawColoredPolygon(f.Quad(-half, -11f, half, -7.5f), new Color("#3a4250"));
                    ci.DrawLine(f.L(-half, -11f), f.L(half, -11f), edge.WithAlpha(0.5f), 0.8f);
                    if (f.Floor.X % 2 == 0) ci.DrawCircle(f.L(0f, -9.2f), 0.8f, edge, true, -1f, true);
                }
                // 고무 걸레받이
                ci.DrawColoredPolygon(f.Quad(-half, 0f, half, 2.2f), new Color(0.07f, 0.08f, 0.1f, 0.6f));
                break;
            }
            default:
            {
                // 이음매 없는 판: 광택 띠 · 둥근 안쪽 모서리 · 평평한 점검창 (배선은 안 보인다)
                ci.DrawColoredPolygon(f.Quad(-half, -6f, half, -0.5f), panel.Lightened(0.08f).WithAlpha(0.45f));
                ci.DrawLine(f.L(-half, -4.6f), f.L(half, -4.6f), new Color(1, 1, 1, 0.08f + 0.1f * s.Gloss), 1.2f);
                ci.DrawLine(f.L(-half, -0.8f), f.L(half, -0.8f), TechLook.C(s.Trim).WithAlpha(0.55f), 1f);
                if (FixtureArt.Hash(h, 3, 403) < 0.12f)
                {
                    var q = f.Quad(-5f, -5.5f, 5f, -1.5f);
                    ci.DrawPolyline(q.Append(q[0]).ToArray(), new Color(1, 1, 1, 0.12f), 0.8f, true);
                }
                break;
            }
        }
    }

    // ═══════════════════════════════ 간접 조명 층 (더하기) ═══════════════════════════════

    private void PaintTechGlow(CanvasItem ci)
    {
        if (_main.ViewMode != ViewMode.Normal) return;
        if (_techGeoVersion != _world.Structure.Version) return;
        var s = TechLook.Now;
        var light = TechLook.C(s.Light);
        foreach (var f in _faces)
        {
            var room = f.Room;
            if (!room.Powered || room.LightsOut || room.Detached) continue;
            switch (s.Lamp)
            {
                case LampStyle.Fluorescent:
                {
                    // 위 벽에 네 칸마다 형광등: 끝마개 달린 관 · 가끔 떤다 (낡은 안정기)
                    if (!f.Top || (f.Floor.X - room.MinX) % s.LampEvery != 1) break;
                    float fl = FixtureArt.Hash(f.Floor.X, (int)(_time * 9f), 404) < s.Flicker ? 0.35f : 1f;
                    var a = f.L(-11f, 3.5f);
                    var b = f.L(11f, 3.5f);
                    ci.DrawLine(a, b, light.WithAlpha(0.55f * fl), 2.2f, true);
                    var poly = new[] { f.L(-13f, 2f), f.L(13f, 2f), f.L(20f, 26f), f.L(-20f, 26f) };
                    ci.DrawPolygon(poly, new[] { light.WithAlpha(0.16f * fl), light.WithAlpha(0.16f * fl), light.WithAlpha(0f), light.WithAlpha(0f) });
                    break;
                }
                case LampStyle.LedStrip:
                {
                    if ((f.Floor.X + f.Floor.Y) % s.LampEvery != 0) break;
                    ci.DrawLine(f.L(-6f, 1.5f), f.L(6f, 1.5f), light.WithAlpha(0.6f), 1.4f, true);
                    var poly = new[] { f.L(-8f, 1f), f.L(8f, 1f), f.L(12f, 15f), f.L(-12f, 15f) };
                    ci.DrawPolygon(poly, new[] { light.WithAlpha(0.12f), light.WithAlpha(0.12f), light.WithAlpha(0f), light.WithAlpha(0f) });
                    break;
                }
                default:
                {
                    // 간접 조명: 벽과 바닥 사이 이어진 은은한 띠
                    var poly = new[] { f.L(-16f, 0f), f.L(16f, 0f), f.L(16f, 9f), f.L(-16f, 9f) };
                    ci.DrawPolygon(poly, new[] { light.WithAlpha(0.13f * s.Glow), light.WithAlpha(0.13f * s.Glow), light.WithAlpha(0f), light.WithAlpha(0f) });
                    break;
                }
            }
        }
        PaintVisualsGlow(ci); // TechVisuals.cs — 생물 발광 · 역장 · 노심 같은 빛
    }

    // ═══════════════════════════════ 동적 층 (PaintDynamic 이 부른다) ═══════════════════════════════

    private void PaintTechLookLive(CanvasItem ci)
    {
        if (_techGeoVersion != _world.Structure.Version) return;
        PaintVisualsLive(ci); // TechVisuals.cs
        PaintTechMoments(ci); // TechMoments.cs — 설치 · 업그레이드 · 익힌 기술 순간
    }
}
