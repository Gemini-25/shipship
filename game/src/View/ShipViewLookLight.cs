using System;
using System.Collections.Generic;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v16.5a 2D 조명 (읽기만): 칸마다 4×4 픽셀의 낮은 해상도 빛 버퍼를 0.12초마다 다시 계산해 바닥 · 벽 · 설비 위에 곱한다.
// 방 바탕(켜짐 · 통로 · 정전 어둠) × 방 성격 색온도(기관 청록 · 생활 호박 …) + 천장 조명(Body.Ceiling) · 붉은 비상등(정전) · 비상 조명 설비 ·
// 화면 · 콘솔 · 노심 불빛(LookSpec.Glow) · 깜빡이는 화재(Fire.Fires) · 이동식 작업등 원뿔(Portable.Lights 공개 속성) · 꺼낸 손전등 · 관측창 별빛.
// 빛은 제 방(또는 벽 · 문)에만 닿는다 — 벽을 넘어 새지 않는다. 빠른 깜빡임(불 · 고장 화면)은 더하기 층이 매 프레임 맡는다.
public partial class ShipView
{
    private DrawLayer? _lookLight;
    private ImageTexture? _lightTex;
    private Image? _lightImg;
    private byte[] _lightBytes = Array.Empty<byte>();
    private float[] _lr = Array.Empty<float>(), _lg = Array.Empty<float>(), _lb = Array.Empty<float>();
    private int[] _zone = Array.Empty<int>();
    private float[] _cellR = Array.Empty<float>(), _cellG = Array.Empty<float>(), _cellB = Array.Empty<float>();
    private int _lw, _lh;
    private float _lightTimer;
    private readonly List<LightSrc> _srcs = new();

    /// <summary>빛 하나: 위치(칸) · 반지름(칸) · 색 × 세기 · 닿는 방(-1 = 어디나) · 원뿔 방향(0 이면 사방).</summary>
    private readonly record struct LightSrc(float X, float Y, float R, float Cr, float Cg, float Cb, int Room, float Dx = 0f, float Dy = 0f);

    private const int ZoneDoor = -2, ZoneWall = -3, ZoneVoid = -4;

    /// <summary>빛 버퍼가 켜져 있다 (일반 보기에서만 — 다른 보기는 덧씌운 정보가 잘 보이게 끈다).</summary>
    private bool LookLightOn => LookOn && _lightTex != null && _main.ViewMode == ViewMode.Normal;

    private void AddLookLight()
    {
        _lookLight = new DrawLayer
        {
            Name = "LookLight", Painter = PaintLightMap, TextureFilter = TextureFilterEnum.Linear,
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Mul },
        };
        AddChild(_lookLight);
    }

    private void PaintLightMap(CanvasItem ci)
    {
        if (_lightTex == null || !LookOn) return;
        var g = _world.Ship.Grid;
        ci.DrawTextureRect(_lightTex, new Rect2(0, 0, g.Width * T, g.Height * T), false);
    }

    private void UpdateLightMap(float dt)
    {
        bool on = LookOn && _main.ViewMode == ViewMode.Normal;
        if (_lookLight != null && _lookLight.Visible != on) _lookLight.Visible = on;
        if (!on) return;
        _lightTimer -= dt;
        if (_lightTimer > 0f && _lightTex != null) return;
        _lightTimer = LookSpec.LightRefresh;
        ComputeLight();
    }

    private void ComputeLight()
    {
        var w = _world;
        var ship = w.Ship;
        var g = ship.Grid;
        var body = w.Body;
        int W = g.Width, H = g.Height, k = LookSpec.LightPx, n = g.CellCount;
        int lw = W * k, lh = H * k;
        if (lw != _lw || lh != _lh || _lightTex == null)
        {
            _lw = lw; _lh = lh;
            _lr = new float[lw * lh]; _lg = new float[lw * lh]; _lb = new float[lw * lh];
            _lightBytes = new byte[lw * lh * 3];
            _lightImg = Image.CreateEmpty(lw, lh, false, Image.Format.Rgb8);
            _lightTex = ImageTexture.CreateFromImage(_lightImg);
            _lookLight?.QueueRedraw();
        }
        if (_zone.Length != n) { _zone = new int[n]; _cellR = new float[n]; _cellG = new float[n]; _cellB = new float[n]; }

        // 1) 방 바탕: 켜짐 × 색온도 · 정전은 푸르스름한 어둠
        int rooms = ship.Rooms.Count;
        Span<float> rr = rooms <= 512 ? stackalloc float[rooms] : new float[rooms];
        Span<float> rg = rooms <= 512 ? stackalloc float[rooms] : new float[rooms];
        Span<float> rb = rooms <= 512 ? stackalloc float[rooms] : new float[rooms];
        for (int r = 0; r < rooms; r++)
        {
            var room = ship.Rooms[r];
            bool unlit = !room.Powered || room.LightsOut;
            if (room.Detached) { rr[r] = rg[r] = rb[r] = 1f; continue; }
            if (unlit) { rr[r] = LookSpec.AmbientDark * 0.8f; rg[r] = LookSpec.AmbientDark * 0.9f; rb[r] = LookSpec.AmbientDark * 1.3f; continue; }
            var (tr, tg, tb) = LookSpec.Temperature(room.Kind);
            float amb = room.Type == RoomType.Corridor ? LookSpec.AmbientCorridor : LookSpec.AmbientLit;
            if (room.PowerFlow < 0.75f) amb *= 0.55f + 0.45f * room.PowerFlow / 0.75f; // 전압 강하: 흐리다
            rr[r] = amb * tr; rg[r] = amb * tg; rb[r] = amb * tb;
        }
        for (int i = 0; i < n; i++)
        {
            var c = g.CellAt(i);
            switch (g.Kind(c))
            {
                case TileKind.Floor:
                {
                    int id = g.RoomId(c);
                    _zone[i] = id;
                    if (id >= 0 && id < rooms) { _cellR[i] = rr[id]; _cellG[i] = rg[id]; _cellB[i] = rb[id]; }
                    else { _cellR[i] = _cellG[i] = _cellB[i] = 0.5f; }
                    break;
                }
                case TileKind.Door: _zone[i] = ZoneDoor; break;
                case TileKind.Wall: _zone[i] = ZoneWall; break;
                default: _zone[i] = ZoneVoid; _cellR[i] = _cellG[i] = _cellB[i] = 1f; break;
            }
        }
        // 벽 · 문: 맞닿은 바닥의 밝기 (벽 윗면은 방 빛을 받는다 · 바깥만 닿은 외판은 별빛 정도)
        for (int i = 0; i < n; i++)
        {
            if (_zone[i] != ZoneWall && _zone[i] != ZoneDoor) continue;
            var c = g.CellAt(i);
            float sr = 0f, sg = 0f, sb = 0f;
            int cnt = 0;
            foreach (var d in Cell.Dirs8)
            {
                var nc = c + d;
                if (!g.InBounds(nc)) continue;
                int j = g.Index(nc);
                if (_zone[j] < 0) continue;
                sr += _cellR[j]; sg += _cellG[j]; sb += _cellB[j]; cnt++;
            }
            float wm = _zone[i] == ZoneWall ? 0.92f : 1f;
            if (cnt > 0) { _cellR[i] = sr / cnt * wm; _cellG[i] = sg / cnt * wm; _cellB[i] = sb / cnt * wm; }
            else { _cellR[i] = 0.5f; _cellG[i] = 0.52f; _cellB[i] = 0.58f; }
        }
        for (int cy = 0; cy < H; cy++)
        for (int cx = 0; cx < W; cx++)
        {
            int i = cy * W + cx;
            float r = _cellR[i], gg = _cellG[i], b = _cellB[i];
            for (int py = 0; py < k; py++)
            {
                int row = (cy * k + py) * lw + cx * k;
                for (int px = 0; px < k; px++) { _lr[row + px] = r; _lg[row + px] = gg; _lb[row + px] = b; }
            }
        }

        // 2) 빛 모으기
        BuildLightSources();
        foreach (var s in _srcs) Splat(s, W, H);

        // 3) 바이트로
        for (int p = 0, q = 0; p < _lr.Length; p++, q += 3)
        {
            _lightBytes[q] = (byte)(Mathf.Clamp(_lr[p], 0f, 1f) * 255f);
            _lightBytes[q + 1] = (byte)(Mathf.Clamp(_lg[p], 0f, 1f) * 255f);
            _lightBytes[q + 2] = (byte)(Mathf.Clamp(_lb[p], 0f, 1f) * 255f);
        }
        _lightImg!.SetData(lw, lh, false, Image.Format.Rgb8, _lightBytes);
        _lightTex!.Update(_lightImg);
    }

    private void Splat(in LightSrc s, int W, int H)
    {
        int k = LookSpec.LightPx;
        float rad = s.R * k;
        int x0 = Math.Max(0, (int)MathF.Floor(s.X * k - rad)), x1 = Math.Min(W * k - 1, (int)MathF.Ceiling(s.X * k + rad));
        int y0 = Math.Max(0, (int)MathF.Floor(s.Y * k - rad)), y1 = Math.Min(H * k - 1, (int)MathF.Ceiling(s.Y * k + rad));
        float inv = 1f / (s.R * s.R);
        bool cone = s.Dx != 0f || s.Dy != 0f;
        for (int py = y0; py <= y1; py++)
        {
            float fy = (py + 0.5f) / k - s.Y;
            int cy = py / k;
            for (int px = x0; px <= x1; px++)
            {
                float fx = (px + 0.5f) / k - s.X;
                float d2 = (fx * fx + fy * fy) * inv;
                if (d2 >= 1f) continue;
                int zone = _zone[cy * W + px / k];
                if (zone == ZoneVoid) continue;
                if (s.Room >= 0 && zone >= 0 && zone != s.Room) continue; // 벽 너머 다른 방에는 닿지 않는다
                float f = 1f - d2;
                f *= f;
                if (zone == ZoneWall) f *= 0.6f;
                if (cone)
                {
                    float len = MathF.Sqrt(fx * fx + fy * fy);
                    float dot = len < 0.01f ? 1f : (fx * s.Dx + fy * s.Dy) / len;
                    f *= 0.18f + 0.82f * Mathf.SmoothStep(0.55f, 0.92f, dot);
                }
                int p = py * _lw + px;
                _lr[p] += s.Cr * f; _lg[p] += s.Cg * f; _lb[p] += s.Cb * f;
            }
        }
    }

    private void BuildLightSources()
    {
        _srcs.Clear();
        var w = _world;
        var ship = w.Ship;
        var g = ship.Grid;
        var body = w.Body;
        // 천장 조명 · 정전 비상등
        foreach (var room in ship.Rooms)
        {
            if (room.Detached || room.Cells.Count == 0) continue;
            bool unlit = !room.Powered || room.LightsOut;
            var (tr, tg, tb) = LookSpec.Temperature(room.Kind);
            if (!unlit)
            {
                float flow = room.PowerFlow < 0.75f ? 0.45f + 0.55f * room.PowerFlow / 0.75f : 1f;
                float s = LookSpec.CeilingPool * flow * (room.Type == RoomType.Corridor ? 0.85f : 1f);
                foreach (var c in room.Cells)
                {
                    int i = g.Index(c);
                    if (i >= body.Ceiling.Length || (body.Ceiling[i] & CeilingFlags.Light) == 0) continue;
                    float fl = s;
                    if (room.PowerFlow < 0.55f && Mathf.Sin(_time * 9f + c.X * 1.7f + c.Y * 0.9f) > 0.6f) fl *= 0.35f; // 낮은 전압에 형광등이 떤다
                    _srcs.Add(new LightSrc(c.X + 0.5f, c.Y + 0.5f, LookSpec.CeilingRadius, tr * fl, tg * fl, tb * fl, room.Id));
                }
            }
            else if (!room.LightsOut)
            {
                // 정전: 축전지 비상등만 — 붉게, 천천히 숨 쉬듯
                float pulse = 0.72f + 0.28f * Mathf.Sin(_time * 2.2f + room.Id);
                var (er, eg, eb) = LookSpec.Emergency;
                int nth = 0;
                foreach (var c in room.Cells)
                {
                    int i = g.Index(c);
                    if (i >= body.Ceiling.Length || (body.Ceiling[i] & CeilingFlags.Light) == 0) continue;
                    if (nth++ % 2 == 1) continue;
                    _srcs.Add(new LightSrc(c.X + 0.5f, c.Y + 0.5f, 2.4f, er * 0.55f * pulse, eg * 0.55f * pulse, eb * 0.55f * pulse, room.Id));
                }
            }
        }
        // 설비 불빛: 화면 · 콘솔 · 노심 · 재배등 · 화구 · 수조 … (전기가 있고 가동 중일 때 · 고장 나면 떤다)
        foreach (var f in ship.Furniture)
        {
            if (f.Stowed || f.Room.Detached) continue;
            if (LookSpec.Glow(f.Type) is not { } glow) continue;
            var (gr, gg, gb, rad, str, flick) = glow;
            bool roomUnlit = !f.Room.Powered || f.Room.LightsOut;
            float s = str;
            if (f.Type == FurnitureType.EmergencyLight)
            {
                if (!roomUnlit || f.Machine is Machine em && em.Faults.Count > 0) continue; // 비상 조명은 어두울 때만 (축전지)
            }
            else
            {
                if (!f.Room.Powered) continue;
                if (f.Machine is Machine m)
                {
                    if (!m.Powered || !m.Active || m.Parked) continue;
                    if (m.Faults.Count > 0) s *= Mathf.Sin(_time * 7f + f.Id * 2.3f) > 0.2f ? 0.15f : 0.8f; // 고장: 지직거린다
                }
            }
            if (flick > 0f) s *= 1f - flick * (0.5f + 0.5f * Mathf.Sin(_time * 5.3f + f.Id * 1.37f));
            float cx = (f.MinX + f.MaxX + 1) * 0.5f, cy = (f.MinY + f.MaxY + 1) * 0.5f;
            _srcs.Add(new LightSrc(cx, cy, rad + 0.25f * Mathf.Max(f.Width, f.Height), gr * s, gg * s, gb * s, f.Room.Id));
        }
        // 화재: 깜빡이는 주황 (불 세기만큼 넓고 밝다)
        var (fr, fg, fb) = LookSpec.FireLight;
        foreach (var (cell, v) in w.Fire.Fires)
        {
            float flick = 0.72f + 0.28f * Mathf.Sin(_time * 11f + cell.X * 1.3f) * Mathf.Sin(_time * 7.3f + cell.Y * 2.1f);
            float s = (0.3f + 0.5f * Mathf.Min(1f, v)) * flick;
            _srcs.Add(new LightSrc(cell.X + 0.5f, cell.Y + 0.5f, 2.2f + 1.2f * Mathf.Min(1f, v), fr * s, fg * s, fb * s, g.RoomId(cell)));
        }
        // 이동식 광원 (Core 공개 속성 그대로): 작업등은 겨눈 쪽 원뿔 · 히터는 붉은 열빛
        foreach (var d in w.Portable.Lights)
        {
            var room = w.Portable.RoomOf(d);
            if (room is { Detached: true }) continue;
            var pos = d.LightPos;
            float s = 0.8f * d.LightIntensity;
            float dx = 0f, dy = 0f;
            if (d.Directional)
            {
                var dir = d.Aim - pos;
                float len = dir.Length();
                if (len > 0.01f) { dx = dir.X / len; dy = dir.Y / len; }
            }
            _srcs.Add(new LightSrc(pos.X, pos.Y, Mathf.Max(0.8f, d.LightRadius), d.LightColor.X * s, d.LightColor.Y * s, d.LightColor.Z * s, room?.Id ?? -1, dx, dy));
        }
        // 꺼내 든 손전등 (벽 장착물 → 그 사람 곁, 걷는 쪽으로)
        foreach (var mt in body.Mounts)
        {
            if (mt.Kind != MountKind.Flashlight || mt.TakenBy < 0) continue;
            var who = w.Crew.Find(c => c.Id == mt.TakenBy);
            if (who?.Room is not Room wr || wr.Detached) continue;
            var mv = who.Position - who.PreviousPosition;
            float ml = mv.Length();
            _srcs.Add(new LightSrc(who.Position.X, who.Position.Y, 3f, 0.62f, 0.6f, 0.5f, wr.Id, ml > 0.001f ? mv.X / ml : 0f, ml > 0.001f ? mv.Y / ml : 0f));
        }
        // 관측창: 차가운 별빛 (덮개를 내리면 없다)
        foreach (var wb in body.WallList)
        {
            if (!wb.Window || wb.Shutter || wb.Room < 0 || wb.Room >= ship.Rooms.Count || ship.Rooms[wb.Room].Detached) continue;
            _srcs.Add(new LightSrc(wb.Cell.X + 0.5f, wb.Cell.Y + 0.5f, 2.2f, 0.12f, 0.16f, 0.26f, wb.Room));
        }
    }

    // ───────────────────────── 더하기 층: 매 프레임 깜빡임 ─────────────────────────

    /// <summary>불길 위로 일렁이는 번짐 · 고장 난 화면의 지직거림 (빛 버퍼는 느리니 빠른 깜빡임만 여기서).</summary>
    private void PaintLookGlow(CanvasItem ci)
    {
        if (!LookLightOn || Textures.Light is not Texture2D tex) return;
        foreach (var (cell, v) in _world.Fire.Fires)
        {
            var room = _world.Ship.RoomAt(cell);
            if (room is { Detached: true }) continue;
            float flick = 0.6f + 0.4f * Mathf.Sin(_time * 17f + cell.X * 3.1f) * Mathf.Sin(_time * 23f + cell.Y * 1.7f);
            float size = T * (2.4f + 1.4f * Mathf.Min(1f, v)) * (0.9f + 0.15f * flick);
            var ctr = CellRect(cell).GetCenter() + new Vector2(Mathf.Sin(_time * 5f + cell.Y) * 2f, -3f);
            ci.DrawTextureRect(tex, new Rect2(ctr - new Vector2(size, size) * 0.5f, size, size), false, new Color(1f, 0.5f, 0.15f, 0.2f * flick * Mathf.Min(1f, 0.4f + v)));
        }
    }
}
