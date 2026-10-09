using System;
using System.Collections.Generic;
using Godot;
using ShipSim.Core;
using Mat = ShipSim.Core.Material;

namespace ShipSim.View;

// v16.5a 재질 그림 (읽기만): 생성기 텍스처로 바닥재 6 · 벽 3 을 깔고(정적 층 — 한 번만), 그 위에 상태 겹치기 7(닳음 · 물기 · 기름 · 그을음 · 녹 · 먼지 · 서리)과
// 흔적 아틀라스(얼룩 · 발자국 · 긁힘 · 테이프 · 쪽지 · 용접 자국 · 탄 자국 · 금)를 얹는다.
// 겹치기 · 흔적은 Core 칸 상태(Body.Marks · Body.Wear · Matter 장 · Fire.Scorch)가 바뀔 때만 다시 그린다 (0.5초마다 지문 비교 — 정적 층 캐시).
// 층 순서: 바탕(바닥 · 벽) → 겹치기(셰이더) → 흔적 → 정적(가구 · 배선 …) → 설비 디테일 → 빛 버퍼(곱하기) → 빛 번짐(더하기) → 동적 → 입자.
public partial class ShipView
{
    private DrawLayer? _lookBase, _lookGrime, _lookDecals;
    private float _grimeTimer;
    private ulong _grimeSig;
    private LookSpec.Lod _lookLod = LookSpec.Lod.Mid;
    private readonly float[][] _ov = new float[LookSpec.OvFiles.Length][];
    private float[] _corner = Array.Empty<float>();
    private readonly List<(LookSpec.Decal d, Vector2 at, float rot, float scale, Color tint)> _decalList = new();

    private static bool LookOn => LookTextures.Ready;

    /// <summary>Init: 바탕 · 겹치기 · 흔적 층 (정적 층보다 아래).</summary>
    private void AddLookUnder()
    {
        LookTextures.Load();
        _lookBase = new DrawLayer { Name = "LookBase", Painter = PaintLookBase };
        _lookGrime = new DrawLayer { Name = "LookGrime", Painter = PaintGrime, Material = LookTextures.OverlayMaterial };
        _lookDecals = new DrawLayer { Name = "LookDecals", Painter = PaintDecals };
        AddChild(Baked(_lookBase)); // v17.7 구워 둔다
        AddChild(_lookGrime);
        AddChild(_lookDecals);
    }

    /// <summary>Init: 입자 층 (맨 위) · 빛 버퍼는 AddLookLight.</summary>
    private void AddLookOver() => AddParticleLayers();

    private void RedrawLook() { _lookBase?.QueueRedraw(); _grimeSig = 0; _grimeTimer = 0f; }

    /// <summary>_Process: 확대 단계 · 겹치기 지문 · 빛 버퍼 · 입자.</summary>
    private void UpdateLook(float dt)
    {
        var lod = LookSpec.LodOf(Zoom);
        if (lod != _lookLod)
        {
            _lookLod = lod;
            if (_lookDecals != null) _lookDecals.Visible = lod != LookSpec.Lod.Far;
        }
        _grimeTimer -= dt;
        if (_grimeTimer <= 0f) { _grimeTimer = 0.5f; ComputeGrime(); }
        UpdateLightMap(dt);
        UpdateParticles(dt);
    }

    // ───────────────────────────── 바탕: 바닥재 · 벽 ─────────────────────────────

    private void PaintLookBase(CanvasItem ci)
    {
        var ship = _world.Ship;
        var g = ship.Grid;
        PaintHullSkin(ci); // v10.9 외판·날개·노즐
        for (int i = 0; i < g.CellCount; i++)
        {
            var c = g.CellAt(i);
            if (g.Kind(c) != TileKind.Void) ci.Box(CellRect(c).Grow(3f), Palette.HullRim);
        }
        if (LookOn) PaintLookCells(ci);
        else PaintLegacyCells(ci);
    }

    /// <summary>예전 그림 (생성기 그림이 없을 때).</summary>
    private void PaintLegacyCells(CanvasItem ci)
    {
        var ship = _world.Ship;
        var g = ship.Grid;
        for (int i = 0; i < g.CellCount; i++)
        {
            var c = g.CellAt(i);
            var r = CellRect(c);
            switch (g.Kind(c))
            {
                case TileKind.Wall:
                    ci.Box(r, Palette.Wall);
                    if (Textures.Wall != null) ci.DrawTextureRectRegion(Textures.Wall, r, Variant(c), new Color(1, 1, 1, 0.9f));
                    break;
                case TileKind.Floor:
                {
                    var room = ship.RoomAt(c);
                    if (room == null) break;
                    ci.Box(r, Palette.RoomFloor(room.Kind));
                    var (tex, alpha) = Textures.Floor(room.Type);
                    if (tex != null) ci.DrawTextureRectRegion(tex, r, Variant(c), new Color(1, 1, 1, alpha));
                    break;
                }
                case TileKind.Door:
                {
                    var door = ship.DoorAt(c);
                    var room = door?.RoomA ?? door?.RoomB;
                    ci.Box(r, room != null ? Palette.RoomFloor(room.Kind) : Palette.Floor);
                    if (Textures.Plate != null) ci.DrawTextureRectRegion(Textures.Plate, r, Variant(c), new Color(1, 1, 1, 0.8f));
                    break;
                }
            }
        }
    }

    /// <summary>칸 하나의 바닥 그림 (Core 바닥재 + 방 종류 + 벽 둘레).</summary>
    private LookSpec.FloorLook FloorLookAt(Cell c, Room room)
    {
        var g = _world.Ship.Grid;
        var body = _world.Body;
        int i = g.Index(c);
        bool rim = false;
        foreach (var d in Cell.Dirs4) if (_world.Ship.RoomAt(c + d) != room) { rim = true; break; }
        return LookSpec.Floor(i < body.Floor.Length ? body.Floor[i] : Mat.None, room.Kind, rim);
    }

    private void PaintLookCells(CanvasItem ci)
    {
        var ship = _world.Ship;
        var g = ship.Grid;
        var body = _world.Body;
        for (int i = 0; i < g.CellCount; i++)
        {
            var c = g.CellAt(i);
            var r = CellRect(c);
            switch (g.Kind(c))
            {
                case TileKind.Wall:
                {
                    var wb = body.WallAt(c);
                    var wl = wb == null ? LookSpec.WallLook.Panel : wb.Hull ? LookSpec.WallLook.Hull : wb.Thin ? LookSpec.WallLook.Partition : LookSpec.WallLook.Panel;
                    ci.DrawTextureRectRegion(LookTextures.Walls[(int)wl]!, r, LookTextures.Region(c));
                    break;
                }
                case TileKind.Floor:
                {
                    var room = ship.RoomAt(c);
                    if (room == null) break;
                    var fl = FloorLookAt(c, room);
                    // 방 색을 아주 조금 물들인다 (방이 구분되게 — 빛 버퍼의 색온도가 나머지를 맡는다)
                    var tint = Colors.White.Lerp(Palette.Room(room.Kind), 0.1f);
                    ci.DrawTextureRectRegion(LookTextures.Floors[(int)fl]!, r, LookTextures.Region(c), tint);
                    break;
                }
                case TileKind.Door:
                {
                    var door = ship.DoorAt(c);
                    var room = door?.RoomA ?? door?.RoomB;
                    var fl = room != null && LookSpec.Heavy(room.Kind) ? LookSpec.FloorLook.Engine : LookSpec.FloorLook.Metal;
                    ci.DrawTextureRectRegion(LookTextures.Floors[(int)fl]!, r, LookTextures.Region(c), new Color(0.85f, 0.87f, 0.9f));
                    break;
                }
            }
        }
    }

    // ───────────────────────────── 상태 겹치기: 칸마다 양 ─────────────────────────────

    private float[] Ov(LookSpec.Ov o) => _ov[(int)o];

    private void ComputeGrime()
    {
        if (!LookOn || _lookGrime == null) return;
        var w = _world;
        var ship = w.Ship;
        var g = ship.Grid;
        var body = w.Body;
        int n = g.CellCount;
        for (int k = 0; k < _ov.Length; k++)
        {
            if (_ov[k] == null || _ov[k].Length != n) _ov[k] = new float[n];
            else Array.Clear(_ov[k]);
        }
        float rough = ShipInfos.Rough(w.Origin.Start);
        var wear = body.Wear;
        var steps = body.Steps;
        float[] oWear = Ov(LookSpec.Ov.Wear), oWet = Ov(LookSpec.Ov.Wet), oOil = Ov(LookSpec.Ov.Oil), oSoot = Ov(LookSpec.Ov.Soot),
            oRust = Ov(LookSpec.Ov.Rust), oDust = Ov(LookSpec.Ov.Dust), oFrost = Ov(LookSpec.Ov.Frost);
        for (int i = 0; i < n; i++)
        {
            var c = g.CellAt(i);
            if (g.Kind(c) != TileKind.Floor) continue;
            var room = ship.RoomAt(c);
            if (room == null || room.Detached) continue;
            var m = i < body.Floor.Length ? body.Floor[i] : Materials.FloorFor(room.Kind);
            float h = LookSpec.H(c.X, c.Y, 7);
            float edge = 0f;
            foreach (var d in Cell.Dirs4) if (g.Kind(c + d) == TileKind.Wall) edge += 0.34f;
            float wv = i < wear.Length ? wear[i] : 0f;
            int st = i < steps.Length ? steps[i] : 0;
            // 닳음: 밟은 만큼 (낡은 배는 처음부터 조금)
            oWear[i] = Mathf.Clamp(wv * 1.15f + rough * 0.2f * h, 0f, 1f);
            // 먼지: 원소 장의 가루 + 발길 없는 벽 가장자리 (낡은 배일수록 · 밟히면 사라진다)
            float quiet = 1f - Mathf.Min(1f, st / 250f);
            oDust[i] = Mathf.Clamp(w.Matter.DustAt(c) + rough * (0.18f + 0.45f * edge) * quiet * (0.55f + 0.45f * h), 0f, 1f);
            // 녹: 금속 바닥 × 낡음 × 습한 방
            if (m is Mat.Grate or Mat.MetalPlate)
                oRust[i] = Mathf.Clamp(rough * 0.55f * LookSpec.H(c.X, c.Y, 8) * (0.4f + edge) + Mathf.Max(0f, room.Humidity - 0.65f) * 1.4f, 0f, 1f);
            // 서리: 차가운 방은 벽 가장자리부터 언다
            float temp = room.Air.Temperature;
            if (temp < 3f) oFrost[i] = Mathf.Clamp((3f - temp) / 14f * (0.5f + 0.7f * edge), 0f, 1f);
        }
        // Core 칸 상태: 물기 · 기름 · 그을음 · 서리 (오래 젖은 금속은 녹슨다 — 물 × 산소 × 시간)
        foreach (var (i, s) in body.Marks)
        {
            if (i < 0 || i >= n) continue;
            float wet = s.V[(int)CellMark.Wet];
            oWet[i] = Mathf.Max(oWet[i], wet);
            oOil[i] = Mathf.Max(oOil[i], s.V[(int)CellMark.Oil]);
            oSoot[i] = Mathf.Max(oSoot[i], s.V[(int)CellMark.Soot]);
            oFrost[i] = Mathf.Max(oFrost[i], s.V[(int)CellMark.Frost]);
            if (wet > 0.3f && i < body.Floor.Length && body.Floor[i] is Mat.Grate or Mat.MetalPlate)
            {
                long since = w.Tick - s.Since[(int)CellMark.Wet];
                if (since > SimTime.Hours(4)) oRust[i] = Mathf.Max(oRust[i], Mathf.Min(0.75f, since / (float)SimTime.Hours(48)));
            }
        }
        // 원소 장: 쏟은 액체 · 기름 웅덩이
        foreach (var (i, sp) in w.Matter.Spills)
        {
            if (i < 0 || i >= n) continue;
            if (sp.Kind == Mat.Oil) oOil[i] = Mathf.Max(oOil[i], Mathf.Min(1f, sp.Liters / 2f));
            else oWet[i] = Mathf.Max(oWet[i], Mathf.Min(1f, sp.Liters / 3f));
        }
        // 불: 그을린 자리 · 타는 칸
        foreach (var (cell, amt) in w.Fire.Scorch)
            if (g.InBounds(cell)) oSoot[g.Index(cell)] = Mathf.Max(oSoot[g.Index(cell)], Mathf.Min(1f, amt));
        foreach (var (cell, v) in w.Fire.Fires)
            if (g.InBounds(cell)) oSoot[g.Index(cell)] = Mathf.Max(oSoot[g.Index(cell)], Mathf.Min(1f, 0.35f + 0.5f * v));

        BuildDecals(rough);

        // 지문: 양을 16단계로 묶어 바뀐 칸이 있을 때만 다시 그린다
        ulong sig = 1469598103934665603UL;
        for (int k = 0; k < _ov.Length; k++)
        {
            var a = _ov[k];
            for (int i = 0; i < n; i++)
            {
                int q = (int)(a[i] * 16f);
                if (q != 0) sig = (sig ^ ((ulong)(i * 8 + k) | ((ulong)q << 40))) * 1099511628211UL;
            }
        }
        foreach (var d in _decalList) sig = (sig ^ (ulong)((int)d.d * 7919 + (int)d.at.X * 31 + (int)d.at.Y * 131 + (int)(d.tint.A * 16f))) * 1099511628211UL;
        sig ^= (ulong)n << 48;
        if (sig != _grimeSig)
        {
            _grimeSig = sig;
            _lookGrime.QueueRedraw();
            _lookDecals?.QueueRedraw();
        }
    }

    private static bool Floorish(ShipGrid g, Cell c) => g.Kind(c) is TileKind.Floor or TileKind.Door;

    private void PaintGrime(CanvasItem ci)
    {
        if (!LookOn) return;
        var g = _world.Ship.Grid;
        var body = _world.Body;
        int W = g.Width, H = g.Height, n = g.CellCount;
        if (_corner.Length != (W + 1) * (H + 1)) _corner = new float[(W + 1) * (H + 1)];
        var pts = new Vector2[4];
        var cols = new Color[4];
        var uvs = new Vector2[4];
        for (int k = 0; k < _ov.Length; k++)
        {
            var tex = LookTextures.Overlays[k];
            var a = _ov[k];
            if (tex == null || a == null || a.Length != n) continue;
            bool any = false;
            for (int i = 0; i < n && !any; i++) any = a[i] > 0.01f;
            if (!any) continue;
            // 꼭짓점 양: 둘레 네 칸(바닥만)의 최댓값과 평균을 반씩 — 웅덩이가 칸 경계를 넘어 부드럽게 이어진다
            for (int y = 0; y <= H; y++)
            for (int x = 0; x <= W; x++)
            {
                float mx = 0f, sum = 0f;
                int cnt = 0;
                for (int dy = -1; dy <= 0; dy++)
                for (int dx = -1; dx <= 0; dx++)
                {
                    var c = new Cell(x + dx, y + dy);
                    if (!g.InBounds(c) || !Floorish(g, c)) continue;
                    float v = a[g.Index(c)];
                    mx = Mathf.Max(mx, v); sum += v; cnt++;
                }
                _corner[y * (W + 1) + x] = cnt == 0 ? 0f : 0.5f * mx + 0.5f * sum / cnt;
            }
            for (int i = 0; i < n; i++)
            {
                var c = g.CellAt(i);
                if (!Floorish(g, c)) continue;
                float c00 = _corner[c.Y * (W + 1) + c.X], c10 = _corner[c.Y * (W + 1) + c.X + 1],
                    c11 = _corner[(c.Y + 1) * (W + 1) + c.X + 1], c01 = _corner[(c.Y + 1) * (W + 1) + c.X];
                if (Mathf.Max(Mathf.Max(c00, c10), Mathf.Max(c11, c01)) < 0.02f) continue;
                var mat = i < body.Floor.Length ? body.Floor[i] : Mat.MetalPlate;
                var st = LookSpec.OvStyle((LookSpec.Ov)k, mat);
                var r = CellRect(c);
                var (u0, u1) = LookTextures.Uv(c);
                pts[0] = r.Position; pts[1] = new Vector2(r.End.X, r.Position.Y); pts[2] = r.End; pts[3] = new Vector2(r.Position.X, r.End.Y);
                uvs[0] = u0; uvs[1] = new Vector2(u1.X, u0.Y); uvs[2] = u1; uvs[3] = new Vector2(u0.X, u1.Y);
                cols[0] = LookTextures.OvColor(c00, st); cols[1] = LookTextures.OvColor(c10, st); cols[2] = LookTextures.OvColor(c11, st); cols[3] = LookTextures.OvColor(c01, st);
                ci.DrawPrimitive(pts, cols, uvs, tex);
            }
        }
    }

    // ───────────────────────────── 흔적: Core 상태 · 방 성격 · 배 내력에서 자리를 정한다 ─────────────────────────────

    private void AddDecal(LookSpec.Decal d, Cell c, float rot, float alpha, float scale = 1f, float jitter = 0.18f, Color? tint = null)
    {
        float jx = (LookSpec.H(c.X, c.Y, 40 + (int)d) - 0.5f) * T * jitter, jy = (LookSpec.H(c.X, c.Y, 80 + (int)d) - 0.5f) * T * jitter;
        var col = tint ?? Colors.White;
        _decalList.Add((d, CellRect(c).GetCenter() + new Vector2(jx, jy), rot, scale, new Color(col.R, col.G, col.B, Mathf.Clamp(alpha, 0f, 1f))));
    }

    private void BuildDecals(float rough)
    {
        _decalList.Clear();
        var w = _world;
        var ship = w.Ship;
        var g = ship.Grid;
        var body = w.Body;
        int n = g.CellCount;
        float[] oWet = Ov(LookSpec.Ov.Wet), oOil = Ov(LookSpec.Ov.Oil);
        for (int i = 0; i < n; i++)
        {
            var c = g.CellAt(i);
            var kind = g.Kind(c);
            if (kind == TileKind.Wall)
            {
                BuildWallDecals(c, rough);
                continue;
            }
            if (kind != TileKind.Floor) continue;
            var room = ship.RoomAt(c);
            if (room == null || room.Detached) continue;
            var rk = room.Kind;
            float h1 = LookSpec.H(c.X, c.Y, 1), h2 = LookSpec.H(c.X, c.Y, 2), h3 = LookSpec.H(c.X, c.Y, 3), h4 = LookSpec.H(c.X, c.Y, 4);
            float wear = i < body.Wear.Length ? body.Wear[i] : 0f;
            int steps = i < body.Steps.Length ? body.Steps[i] : 0;
            bool horiz = ship.RoomAt(c + new Cell(1, 0)) == room && ship.RoomAt(c + new Cell(-1, 0)) == room;
            float walk = horiz ? Mathf.Pi * 0.5f : 0f;
            if (h4 < 0.5f) walk += Mathf.Pi;
            // 발자국: 닳은 길 · 젖은 / 기름 칸 곁을 지나간 발 (물 × 걸음 · 기름 × 걸음)
            float nearWet = 0f, nearOil = 0f;
            foreach (var d in Cell.Dirs4)
            {
                var nc = c + d;
                if (!g.InBounds(nc)) continue;
                nearWet = Mathf.Max(nearWet, oWet[g.Index(nc)]);
                nearOil = Mathf.Max(nearOil, oOil[g.Index(nc)]);
            }
            if (oOil[i] < 0.1f && nearOil > 0.3f && steps > 8 && h1 < 0.75f) AddDecal(LookSpec.Decal.OilPrints, c, walk, 0.55f + 0.4f * nearOil);
            else if (oWet[i] < 0.1f && nearWet > 0.3f && steps > 8 && h1 < 0.7f) AddDecal(LookSpec.Decal.WetPrints, c, walk, 0.5f + 0.4f * nearWet);
            else if (wear > 0.12f && h1 < 0.12f + 0.35f * wear) AddDecal(LookSpec.Decal.Prints, c, walk, 0.25f + 0.55f * wear);
            // 바퀴 자국: 카트가 지나는 중작업 방 · 화물 칸
            if ((LookSpec.Heavy(rk) || rk is RoomType.Storage) && wear > 0.15f && h2 < 0.12f + 0.2f * rough) AddDecal(LookSpec.Decal.Wheels, c, walk, 0.35f + 0.4f * wear);
            // 얼룩: 방 성격대로 (주방 · 식당 — 음식 · 커피 / 함교 · 휴게 — 커피 / 물 쓰는 방 — 물때 / 기관 — 기름 방울)
            float stainP = 0.025f + 0.09f * rough;
            if (h3 < stainP)
            {
                var sd = rk switch
                {
                    RoomType.Galley or RoomType.Mess => h4 < 0.6f ? LookSpec.Decal.Food : LookSpec.Decal.Coffee,
                    RoomType.Bridge or RoomType.Comms or RoomType.Lounge or RoomType.Navigation or RoomType.MeetingRoom or RoomType.Quarters or RoomType.School => LookSpec.Decal.Coffee,
                    RoomType.Laundry or RoomType.WaterPlant or RoomType.Hydroponics or RoomType.Decon or RoomType.AlgaeLab => LookSpec.Decal.Mineral,
                    _ when LookSpec.EngineZone(rk) || LookSpec.Heavy(rk) => LookSpec.Decal.OilDrops,
                    _ => h4 < 0.5f ? LookSpec.Decal.Coffee : LookSpec.Decal.Mineral,
                };
                AddDecal(sd, c, h2 * Mathf.Tau, 0.55f + 0.35f * rough, 0.8f + 0.3f * h4, 0.4f);
            }
            // 긁힘: 무거운 것을 끄는 방 (낡을수록 많다)
            if ((LookSpec.Heavy(rk) || rk is RoomType.Storage || LookSpec.EngineZone(rk)) && h4 < 0.04f + 0.14f * rough)
                AddDecal(h1 < 0.4f ? LookSpec.Decal.Scratches : h1 < 0.7f ? LookSpec.Decal.Drag : LookSpec.Decal.Gouge, c, walk + (h3 - 0.5f) * 0.6f, 0.45f + 0.35f * rough, 1f, 0.3f);
            // 의자를 돌린 자리: 콘솔 · 작업대 · 탁자 아래 칸
            if (ship.FurnitureAt(c + new Cell(0, -1)) is Furniture up && up.Type is FurnitureType.Console or FurnitureType.Workbench or FurnitureType.Table or FurnitureType.NavComputer
                && ship.FurnitureAt(c) == null && h2 < 0.55f)
                AddDecal(LookSpec.Decal.Swirl, c, h3 * Mathf.Tau, 0.35f + 0.3f * Mathf.Min(1f, wear * 3f + rough), 1.1f, 0.1f);
            // 테이프: Core 칸 상태 (임시로 막은 자리 — 오래되면 들뜬다)
            if (body.MarksAt(c) is CellState cs && cs.V[(int)CellMark.Tape] > 0.05f)
            {
                float tape = cs.V[(int)CellMark.Tape];
                AddDecal(tape < 0.35f ? LookSpec.Decal.TapeLoose : h1 < 0.5f ? LookSpec.Decal.TapeX : LookSpec.Decal.TapePatch, c, (h2 - 0.5f) * 0.8f, 0.6f + 0.4f * tape, 1f, 0.1f);
            }
            // 불탄 자리
            if (w.Fire.Scorch.TryGetValue(c, out var sc) && sc > 0.12f) AddDecal(sc > 0.45f ? LookSpec.Decal.Burn : LookSpec.Decal.Scorch, c, h3 * Mathf.Tau, 0.5f + 0.5f * Mathf.Min(1f, sc), 1.1f + 0.3f * Mathf.Min(1f, sc), 0.3f);
            // 깨진 유리 칸 · 낡은 타일의 금
            if (body.MarksAt(c) is CellState gs && gs.V[(int)CellMark.Glass] > 0.1f) AddDecal(LookSpec.Decal.Shatter, c, h2 * Mathf.Tau, 0.55f + 0.4f * gs.V[(int)CellMark.Glass]);
            else if (i < body.Floor.Length && body.Floor[i] == Mat.Tile && h2 < 0.05f * rough) AddDecal(LookSpec.Decal.Crack, c, h3 * Mathf.Tau, 0.55f);
            // 분필 표시: 통로 갈림길 (낡은 배의 손 표시)
            if (rk == RoomType.Corridor && h3 > 0.985f - 0.03f * rough) AddDecal(LookSpec.Decal.Chalk, c, walk, 0.55f);
        }
        // 문 앞: 기관 구역 · 에어락 쪽 갈매기 표시 · 용접한 문 · 봉쇄 테이프
        foreach (var door in ship.Doors)
        {
            if (door.Removed) continue;
            var rooms = new[] { door.RoomA, door.RoomB };
            bool hazard = false, sealedOff = false;
            foreach (var rm in rooms)
            {
                if (rm == null) continue;
                hazard |= LookSpec.EngineZone(rm.Kind) || rm.Kind is RoomType.Airlock or RoomType.Reactor;
                sealedOff |= rm.Lockdown || rm.Unbreathable;
            }
            float rot = door.ConnectsVertically ? 0f : Mathf.Pi * 0.5f;
            if (door.Welded) AddDecal(LookSpec.Decal.WeldBead, door.Cell, rot, 0.95f, 1f, 0f);
            if (sealedOff) AddDecal(LookSpec.Decal.HazardTape, door.Cell, rot + 0.15f, 0.95f, 1.05f, 0f);
            if (!hazard) continue;
            foreach (var d in Cell.Dirs4)
            {
                var nc = door.Cell + d;
                if (g.Kind(nc) != TileKind.Floor || ship.RoomAt(nc) is not Room nr || !(LookSpec.EngineZone(nr.Kind) || nr.Kind == RoomType.Airlock)) continue;
                // 갈매기는 문 쪽을 가리킨다
                AddDecal(LookSpec.Decal.Chevron, nc, Mathf.Atan2(-d.Y, -d.X), 0.75f, 0.85f, 0f);
            }
        }
        // 발판 표시: 도킹 · 셔틀 · 드론 칸 가운데
        foreach (var room in ship.Rooms)
        {
            if (room.Detached || room.Cells.Count == 0 || room.Kind is not (RoomType.DockingBay or RoomType.ShuttleBay or RoomType.DroneBay or RoomType.RobotBay)) continue;
            var mid = new Cell((room.MinX + room.MaxX) / 2, (room.MinY + room.MaxY) / 2);
            if (g.Kind(mid) == TileKind.Floor && ship.FurnitureAt(mid) == null) AddDecal(LookSpec.Decal.Spot, mid, 0f, 0.8f, 1.6f, 0f);
        }
        // 숨은 이야기 쪽지 (배의 내력 — 찾으면 핀이 꽂힌 채 남는다)
        foreach (var f in w.Origin.Finds)
            if (f.Kind == FindKind.Note && g.InBounds(f.At)) AddDecal(f.Found ? LookSpec.Decal.PinNote : LookSpec.Decal.Sticky, f.At, (LookSpec.H(f.Id, 3, 9) - 0.5f) * 0.5f, f.Found ? 0.95f : 0.4f, f.Found ? 0.55f : 0.35f, 0.4f); // 못 찾은 쪽지는 작고 흐릿하게 (눈썰미 있는 사람만)
        // 빈 장착물 자리: 가져간 소화기 · 손전등 자리에 나사 구멍
        foreach (var mt in body.Mounts)
            if (!mt.Present && g.InBounds(mt.Wall)) AddDecal(LookSpec.Decal.ScrewHoles, mt.Wall, 0f, 0.8f, 0.6f, 0f);
    }

    private void BuildWallDecals(Cell c, float rough)
    {
        var ship = _world.Ship;
        var body = _world.Body;
        var wb = body.WallAt(c);
        if (wb == null) return;
        float h1 = LookSpec.H(c.X, c.Y, 21), h2 = LookSpec.H(c.X, c.Y, 22), h3 = LookSpec.H(c.X, c.Y, 23);
        Room? inner = wb.Room >= 0 && wb.Room < ship.Rooms.Count ? ship.Rooms[wb.Room] : null;
        if (inner is { Detached: true }) return;
        var rk = inner?.Kind ?? RoomType.Corridor;
        // 쪽지 · 점검표: 사람이 머무는 방 · 정비하는 방 벽
        if (!wb.Hull && inner != null && h1 < 0.035f + 0.03f * rough)
        {
            var nd = LookSpec.EngineZone(rk) || LookSpec.Heavy(rk) ? LookSpec.Decal.Checklist
                : rk is RoomType.Galley or RoomType.Mess or RoomType.Quarters or RoomType.Lounge or RoomType.PrivateCabins ? (h2 < 0.5f ? LookSpec.Decal.Sticky : LookSpec.Decal.PinNote)
                : rk is RoomType.Bridge or RoomType.Comms or RoomType.Medbay or RoomType.Lab ? LookSpec.Decal.Checklist : LookSpec.Decal.Sticky;
            AddDecal(nd, c, (h3 - 0.5f) * 0.4f, 0.95f, 0.5f, 0.35f);
        }
        // 녹물: 습한 방 · 기관 구역의 낡은 벽
        if (inner != null && (LookSpec.EngineZone(rk) || inner.Humidity > 0.7f) && h2 < 0.08f * rough + Mathf.Max(0f, inner.Humidity - 0.7f))
            AddDecal(LookSpec.Decal.RustRun, c, 0f, 0.6f + 0.3f * rough, 0.9f, 0.15f);
        // 낡은 배의 옛 수리 자국: 외판 덧댐 · 열변색 · 불똥
        if (wb.Hull && h3 < 0.06f * rough)
            AddDecal(h1 < 0.4f ? LookSpec.Decal.WeldPatch : h1 < 0.7f ? LookSpec.Decal.HeatRing : LookSpec.Decal.Spatter, c, 0f, 0.85f, 0.7f, 0.2f);
    }

    private void PaintDecals(CanvasItem ci)
    {
        if (!LookOn || LookTextures.Decals is not Texture2D atlas) return;
        var rect = new Rect2(-T * 0.5f, -T * 0.5f, T, T);
        foreach (var d in _decalList)
        {
            ci.DrawSetTransform(d.at, d.rot, new Vector2(d.scale, d.scale));
            ci.DrawTextureRectRegion(atlas, rect, LookTextures.DecalRegion(d.d), d.tint);
        }
        ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }
}
