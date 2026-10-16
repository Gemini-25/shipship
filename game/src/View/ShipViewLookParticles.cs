using System;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v16.5a 입자 (읽기만 · 화면 전용 난수): 김 · 물방울 · 불꽃 · 연기 · 먼지 · 결로.
// 모두 Core 상태에서 나온다 — 불(Fire.Fires) → 연기 · 불씨 · (젖은 칸이면) 김 / 조리 설비 가동 → 김 / 쏟은 국 · 뜨거운 칸의 물 → 김 /
// 천장 누수(Matter.Drips) → 떨어지는 물방울 → 튀는 물 (무중력이면 떠돈다) / 젖은 칸 × 전기 장 → 불꽃 / 가루 장 · 작업등 원뿔 → 먼지 /
// 결로 칸 → 맺혀 흘러내리는 방울 / 방 연기 → 느린 연기. 바람(Matter.DraftField)이 연기 · 김 · 먼지를 민다.
// 상한(LookSpec.MaxParticles) · 화면 밖은 만들지 않는다 · 멀리서 보면 끈다.
public partial class ShipView
{
    private DrawLayer? _partMix, _partAdd;

    private struct Particle
    {
        public Vector2 P, V;
        public float Life, Max, Size, Grow, Alpha, Gravity, FloorY;
        public LookSpec.Part Kind;
    }

    private readonly Particle[] _parts = new Particle[LookSpec.MaxParticles];
    private int _partCount;
    private uint _prng = 0x9E3779B9u;

    /// <summary>화면 전용 난수 (xorshift — 시뮬레이션 난수와 섞이지 않는다).</summary>
    private float PR()
    {
        _prng ^= _prng << 13; _prng ^= _prng >> 17; _prng ^= _prng << 5;
        return (_prng & 0xffffff) / 16777216f;
    }

    private bool PChance(float p) => PR() < p;

    private void AddParticleLayers()
    {
        _partMix = new DrawLayer { Name = "Particles", Painter = PaintParticlesMix };
        _partAdd = new DrawLayer { Name = "ParticlesAdd", Painter = PaintParticlesAdd, Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add } };
        AddChild(_partMix);
        AddChild(_partAdd);
    }

    private Rect2 VisibleWorld() => GetGlobalTransformWithCanvas().AffineInverse() * GetViewportRect();

    private void Spawn(LookSpec.Part kind, Vector2 p, Vector2 v, float life, float size, float grow, float alpha, float gravity = 0f, float floorY = float.MaxValue)
    {
        if (_partCount >= _parts.Length) return;
        _parts[_partCount++] = new Particle { Kind = kind, P = p, V = v, Max = life, Size = size, Grow = grow, Alpha = alpha, Gravity = gravity, FloorY = floorY };
    }

    private Vector2 DraftPx(Cell c) => _world.Matter.DraftAt(c) is System.Numerics.Vector2 d ? new Vector2(d.X, d.Y) * T * 0.6f : Vector2.Zero;

    private void UpdateParticles(float dt)
    {
        if (_partMix == null || _partAdd == null) return;
        dt = Mathf.Min(dt, 0.1f);
        bool on = LookOn && _lookLod != LookSpec.Lod.Far;
        if (!on)
        {
            if (_partCount > 0) { _partCount = 0; _partMix.QueueRedraw(); _partAdd.QueueRedraw(); }
            return;
        }
        // 1) 움직이기
        var g = _world.Ship.Grid;
        for (int i = 0; i < _partCount; i++)
        {
            ref var p = ref _parts[i];
            p.Life += dt;
            if (p.Life >= p.Max) { _parts[i] = _parts[--_partCount]; i--; continue; }
            p.V.Y += p.Gravity * dt;
            if (p.Kind is LookSpec.Part.Smoke or LookSpec.Part.Steam or LookSpec.Part.Dust)
            {
                var c = CellAtPx(p.P);
                if (g.InBounds(c)) p.V += DraftPx(c) * dt * 1.5f; // 바람이 민다
                p.V *= 1f - 0.6f * dt;
            }
            p.P += p.V * dt;
            if (p.Kind == LookSpec.Part.Drop && p.P.Y >= p.FloorY)
            {
                var at = new Vector2(p.P.X, p.FloorY);
                _parts[i] = _parts[--_partCount]; i--;
                Spawn(LookSpec.Part.Splash, at, Vector2.Zero, 0.35f, 7f, 22f, 0.8f);
            }
        }
        // 2) 만들기 (보이는 곳만)
        var vis = VisibleWorld().Grow(T * 2f);
        float rate = _lookLod == LookSpec.Lod.Near ? 1f : 0.5f;
        EmitFire(dt * rate, vis);
        EmitSteam(dt * rate, vis);
        EmitWater(dt * rate, vis);
        EmitDustAndSparks(dt * rate, vis);
        _partMix.QueueRedraw();
        _partAdd.QueueRedraw();
    }

    private void EmitFire(float dt, Rect2 vis)
    {
        var w = _world;
        foreach (var (cell, v) in w.Fire.Fires)
        {
            var ctr = CellRect(cell).GetCenter();
            if (!vis.HasPoint(ctr) || w.Ship.RoomAt(cell) is { Detached: true }) continue;
            float s = Mathf.Min(1f, v);
            var draft = DraftPx(cell);
            if (PChance(dt * (2.5f + 4f * s)))
                Spawn(LookSpec.Part.Smoke, ctr + new Vector2((PR() - 0.5f) * T * 0.6f, -4f), draft + new Vector2((PR() - 0.5f) * 8f, -14f - 10f * PR()), 2.2f + 1.4f * PR(), 12f + 6f * s, 16f, 0.5f + 0.2f * s);
            if (PChance(dt * (2f + 6f * s)))
                Spawn(LookSpec.Part.Ember, ctr + new Vector2((PR() - 0.5f) * T * 0.5f, 0f), new Vector2((PR() - 0.5f) * 24f, -30f - 30f * PR()), 0.7f + 0.6f * PR(), 5f + 3f * PR(), -3f, 0.95f, 18f);
            if (PChance(dt * 1.2f * s))
                Spawn(LookSpec.Part.Spark, ctr, Vector2.Right.Rotated(PR() * Mathf.Tau) * (50f + 50f * PR()), 0.3f, 8f, 0f, 1f, 120f);
            // 불 × 물: 젖은 칸에서 타면 김이 솟는다
            if (w.Body.Mark(cell, CellMark.Wet) > 0.2f && PChance(dt * 3f))
                Spawn(LookSpec.Part.Steam, ctr + new Vector2((PR() - 0.5f) * T * 0.6f, 0f), draft + new Vector2(0f, -20f), 1.6f, 12f, 18f, 0.55f);
        }
        // 방에 고인 연기: 느리게 떠도는 회색 뭉치
        foreach (var room in w.Ship.Rooms)
        {
            if (room.Detached || room.Air.Smoke < 0.25f || room.Cells.Count == 0) continue;
            if (!PChance(dt * room.Air.Smoke * 3f)) continue;
            var c = room.Cells[(int)(PR() * room.Cells.Count) % room.Cells.Count];
            var ctr = CellRect(c).GetCenter();
            if (!vis.HasPoint(ctr)) continue;
            Spawn(LookSpec.Part.Smoke, ctr, DraftPx(c) + new Vector2((PR() - 0.5f) * 6f, -3f), 3.5f + 2f * PR(), 20f, 10f, 0.25f * Mathf.Min(1f, room.Air.Smoke));
        }
    }

    private void EmitSteam(float dt, Rect2 vis)
    {
        var w = _world;
        // 조리 · 세척 설비가 돌면 김
        foreach (var f in w.Ship.Furniture)
        {
            if (f.Stowed || f.Room.Detached || !f.Room.Powered) continue;
            if (f.Type is not (FurnitureType.Stove or FurnitureType.Oven or FurnitureType.CoffeeMachine or FurnitureType.DishWasher or FurnitureType.WashingMachine
                or FurnitureType.Autoclave or FurnitureType.DeconShower)) continue;
            if (f.Machine is Machine m && (!m.Powered || !m.Active || m.Parked || m.Faults.Count > 0)) continue;
            var top = new Vector2((f.MinX + f.MaxX + 1) * 0.5f * T, f.MinY * T + T * 0.35f);
            if (!vis.HasPoint(top) || !PChance(dt * (f.Type == FurnitureType.Stove ? 1.6f : 0.7f))) continue;
            Spawn(LookSpec.Part.Steam, top + new Vector2((PR() - 0.5f) * T * 0.5f, 0f), DraftPx(new Cell(f.MinX, f.MinY)) + new Vector2((PR() - 0.5f) * 6f, -16f), 1.5f + PR(), 9f, 16f, 0.42f);
        }
        // 뜨거운 국 · 뜨거운 칸에 쏟은 물: 김 (물 × 열)
        foreach (var (i, s) in w.Body.Marks)
        {
            float wet = s.V[(int)CellMark.Wet];
            if (wet < 0.1f) continue;
            var c = w.Ship.Grid.CellAt(i);
            var ctr = CellRect(c).GetCenter();
            if (!vis.HasPoint(ctr)) continue;
            bool soup = s.Cause[(int)CellMark.Wet] is "엎지른 국" or "쏟은 음식" && w.Tick - s.Since[(int)CellMark.Wet] < SimTime.Minutes(20);
            bool hot = w.Matter.HeatAt(c) > 18f;
            if ((soup || hot) && PChance(dt * (hot ? 2.5f : 1f)))
                Spawn(LookSpec.Part.Steam, ctr + new Vector2((PR() - 0.5f) * T * 0.6f, (PR() - 0.5f) * T * 0.4f), DraftPx(c) + new Vector2(0f, -12f), 1.3f, 8f, 14f, 0.4f * wet + 0.15f);
            // 결로: 칸 윗변에 방울이 맺혀 흘러내린다
            if (s.Cause[(int)CellMark.Wet] == "결로" && PChance(dt * (0.4f + 0.8f * wet)))
            {
                var r = CellRect(c);
                Spawn(LookSpec.Part.Bead, new Vector2(r.Position.X + 3f + PR() * (T - 6f), r.Position.Y + 3f), new Vector2(0f, 3f + 5f * PR()), 2.2f + PR(), 4f + 3f * PR(), 0.6f, 0.9f);
            }
        }
    }

    private void EmitWater(float dt, Rect2 vis)
    {
        var w = _world;
        float grav = 260f * w.Matter.Gravity;
        foreach (var (at, lph, _, until) in w.Matter.Drips)
        {
            if (until < w.Tick) continue;
            var r = CellRect(at);
            var ctr = r.GetCenter();
            if (!vis.HasPoint(ctr) || !PChance(dt * Mathf.Clamp(lph / 2f, 0.3f, 4f))) continue;
            // 천장에서 떨어져 바닥에 튄다 (무중력이면 둥둥 떠다닌다)
            var v = grav > 1f ? new Vector2(0f, 8f) : new Vector2((PR() - 0.5f) * 10f, (PR() - 0.5f) * 10f);
            Spawn(LookSpec.Part.Drop, new Vector2(ctr.X + (PR() - 0.5f) * 8f, r.Position.Y - 4f), v, grav > 1f ? 2f : 4f, 6f, 0f, 0.9f, grav, ctr.Y + 6f);
        }
    }

    private void EmitDustAndSparks(float dt, Rect2 vis)
    {
        var w = _world;
        var g = w.Ship.Grid;
        // 전기 × 물: 젖은 칸에 전기가 흐르면 불꽃이 튄다
        foreach (var (i, live) in w.Matter.LiveField)
        {
            if (live < 0.2f || i < 0 || i >= g.CellCount) continue;
            var c = g.CellAt(i);
            var ctr = CellRect(c).GetCenter();
            if (!vis.HasPoint(ctr) || w.Body.Mark(c, CellMark.Wet) < 0.15f || !PChance(dt * 3f * live)) continue;
            for (int k = 0; k < 3; k++)
                Spawn(LookSpec.Part.Spark, ctr + new Vector2((PR() - 0.5f) * 8f, (PR() - 0.5f) * 8f), Vector2.Right.Rotated(PR() * Mathf.Tau) * (60f + 70f * PR()), 0.22f + 0.15f * PR(), 7f, 0f, 1f, 150f);
        }
        // 가루 장: 떠도는 먼지
        foreach (var (i, dust) in w.Matter.DustField)
        {
            if (dust < 0.12f || i < 0 || i >= g.CellCount) continue;
            var c = g.CellAt(i);
            var ctr = CellRect(c).GetCenter();
            if (!vis.HasPoint(ctr) || !PChance(dt * 2f * dust)) continue;
            Spawn(LookSpec.Part.Dust, ctr + new Vector2((PR() - 0.5f) * T, (PR() - 0.5f) * T), DraftPx(c) + new Vector2((PR() - 0.5f) * 6f, (PR() - 0.5f) * 6f), 3f + 2f * PR(), 10f, 2f, 0.55f * Mathf.Min(1f, dust + 0.3f));
        }
        // 작업등 원뿔 속 먼지 (낡은 배일수록 — 빛 × 먼지)
        float rough = ShipInfos.Rough(w.Origin.Start);
        foreach (var d in w.Portable.Lights)
        {
            if (!d.Directional || !PChance(dt * (0.6f + 2f * rough) * d.LightIntensity)) continue;
            var pos = ToPx(d.LightPos);
            var dir = (ToPx(d.Aim) - pos).Normalized();
            if (dir == Vector2.Zero || !vis.HasPoint(pos)) continue;
            var p = pos + dir.Rotated((PR() - 0.5f) * 1f) * d.LightRadius * T * (0.2f + 0.6f * PR());
            Spawn(LookSpec.Part.Dust, p, new Vector2((PR() - 0.5f) * 5f, (PR() - 0.5f) * 5f), 2.5f + 2f * PR(), 8f, 1f, 0.7f);
        }
    }

    private void DrawParticle(CanvasItem ci, Texture2D atlas, in Particle p)
    {
        float t = p.Life / p.Max;
        float fade = Mathf.Min(1f, t / 0.15f) * (t > 0.6f ? 1f - (t - 0.6f) / 0.4f : 1f);
        float size = p.Size + p.Grow * p.Life;
        if (size <= 0.5f) return;
        var col = new Color(1f, 1f, 1f, p.Alpha * fade);
        if (p.Kind == LookSpec.Part.Spark)
        {
            ci.DrawSetTransform(p.P, p.V.Angle(), Vector2.One);
            ci.DrawTextureRectRegion(atlas, new Rect2(-size, -size * 0.5f, size * 2f, size), LookTextures.PartRegion(p.Kind), col);
            ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            return;
        }
        ci.DrawTextureRectRegion(atlas, new Rect2(p.P - new Vector2(size, size) * 0.5f, size, size), LookTextures.PartRegion(p.Kind), col);
    }

    private void PaintParticlesMix(CanvasItem ci)
    {
        if (LookTextures.Particles is not Texture2D atlas) return;
        for (int i = 0; i < _partCount; i++)
            if (_parts[i].Kind is not (LookSpec.Part.Spark or LookSpec.Part.Ember)) DrawParticle(ci, atlas, _parts[i]);
    }

    private void PaintParticlesAdd(CanvasItem ci)
    {
        if (LookTextures.Particles is not Texture2D atlas) return;
        for (int i = 0; i < _partCount; i++)
            if (_parts[i].Kind is LookSpec.Part.Spark or LookSpec.Part.Ember) DrawParticle(ci, atlas, _parts[i]);
    }
}
