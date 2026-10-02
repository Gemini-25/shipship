using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v12.6 인접성: 방이 내는 소음·진동·냄새·방사선이 벽과 문을 넘어 옆방으로 번진다.
// 엔진실 옆 침실은 잠을 설치고, 재활용실 옆 식당은 밥맛이 없고, 파쇄실 옆 재배실은 작물이 덜 자란다.
// 방음(조용한 침실)·차폐(대피소·물벽 선실)가 막는다. 태양 폭풍은 배 전체에 방사선을 뿌린다.

public sealed class AmbienceSystem
{
    private readonly World _w;
    private readonly Dictionary<int, List<(Room room, bool door)>> _adj = new();
    private int _adjRooms = -1;
    private long _adjAt = -1;

    /// <summary>태양 폭풍의 양성자 비 (0~1): 처음 세 시간이 가장 세고, 그다음은 약하게 남는다.</summary>
    public float StormPower
    {
        get
        {
            var h = _w.Hazards;
            if (!h.StormActive || h.StormSince < 0) return 0f;
            float hours = (_w.Tick - h.StormSince) / (float)SimTime.TicksPerHour;
            return hours < 3f ? h.StormPeak : 0.3f * h.StormPeak; // v16.26 폭풍마다 세기가 다르다 (센 폭풍은 바깥 방도 위험하다)
        }
    }
    public bool Storm => _w.Hazards.StormActive;

    public AmbienceSystem(World w) => _w = w;

    public IReadOnlyList<(Room room, bool door)> Neighbors(Room r)
    {
        Rebuild();
        return _adj.TryGetValue(r.Id, out var l) ? l : (IReadOnlyList<(Room, bool)>)Array.Empty<(Room, bool)>();
    }

    private void Rebuild()
    {
        var ship = _w.Ship;
        if (_adjRooms == ship.Rooms.Count && _w.Tick - _adjAt < SimTime.Hours(6)) return;
        _adjRooms = ship.Rooms.Count;
        _adjAt = _w.Tick;
        _adj.Clear();
        _exposure.Clear();
        var g = ship.Grid;
        var pairs = new Dictionary<(int, int), bool>();
        for (int i = 0; i < g.CellCount; i++)
        {
            var c = g.CellAt(i);
            var k = g.Kind(c);
            if (k != TileKind.Wall && k != TileKind.Door) continue;
            var near = new List<int>(4);
            foreach (var d in Cell.Dirs4)
            {
                int id = g.RoomId(c + d);
                if (id >= 0 && !near.Contains(id)) near.Add(id);
            }
            for (int a = 0; a < near.Count; a++)
                for (int b = a + 1; b < near.Count; b++)
                {
                    var key = near[a] < near[b] ? (near[a], near[b]) : (near[b], near[a]);
                    pairs[key] = (pairs.TryGetValue(key, out var dd) && dd) || k == TileKind.Door;
                }
        }
        foreach (var ((a, b), door) in pairs)
        {
            if (a >= ship.Rooms.Count || b >= ship.Rooms.Count) continue;
            if (!_adj.TryGetValue(a, out var la)) _adj[a] = la = new();
            if (!_adj.TryGetValue(b, out var lb)) _adj[b] = lb = new();
            la.Add((ship.Rooms[b], door));
            lb.Add((ship.Rooms[a], door));
        }
    }

    /// <summary>이 방이 지금 내는 것 — 기계가 멈추면 조용해진다, 일하는 사람이 있으면 시끄럽다.</summary>
    private (float n, float v, float s, float r) Own(Room room)
    {
        var (n, v, s, r) = RoomCatalog.Emits(room.Kind);
        if (room.Detached) return (0, 0, 0, 0);
        bool machines = room.Furniture.Any(f => f.Machine != null);
        if (machines)
        {
            float run = room.Furniture.Where(f => f.Machine != null).Average(f => f.Machine!.Active ? 1f : 0.25f);
            n *= run; v *= run;
        }
        if (!room.Powered) { n *= 0.4f; v *= 0.4f; }
        if (room.Type == RoomType.Reactor)
        {
            var core = room.Furniture.FirstOrDefault(f => f.Type == FurnitureType.ReactorCore)?.Machine;
            if (core != null) r = 0.06f + 0.6f * MathF.Max(0f, core.Heat - 0.6f); // 평소엔 차폐가 막는다, 달궈지면 샌다
        }
        int people = _w.Crew.Count(c => !c.Dead && c.Room == room && c.Pose == Pose.Working);
        if (people > 0) n = MathF.Min(1f, n + 0.05f * people);
        n = MathF.Max(n, _w.Portable.NoiseIn(room)); s *= _w.Portable.SmellMul(room); // v16.7 이동식 장비 소리 · 공기청정기
        n = MathF.Max(n, _w.Annex?.NoiseIn(room) ?? 0f); // v16.10 증축 공사 (용접 · 망치)
        return (n, v, s, r);
    }

    /// <summary>시스템 틱.</summary>
    public void Update(float dt)
    {
        var w = _w;
        Rebuild();
        var rooms = w.Ship.Rooms;
        var own = new (float n, float v, float s, float r)[rooms.Count];
        for (int i = 0; i < rooms.Count; i++) own[i] = Own(rooms[i]);
        float storm = StormPower;
        if (storm > 0f && w.Eras.Has("magshield")) storm *= 0.5f; // v12.8 자기장 차폐 (v16.0: 방마다 반씩 줄던 버그 — 반복문 밖에서 한 번만)
        for (int i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            var (n, v, s, r) = own[i];
            foreach (var (o, door) in Neighbors(room))
            {
                var e = own[o.Id];
                float k = door ? 0.55f : 0.4f;
                n = MathF.Max(n, e.n * k);
                v = MathF.Max(v, e.v * 0.6f);
                s = MathF.Max(s, e.s * (door ? 0.5f : 0.15f));
                r = MathF.Max(r, e.r * 0.35f);
            }
            var tags = RoomCatalog.Tags(room.Kind);
            if ((tags & RoomTag.Quiet) != 0) { n *= 0.35f; v *= 0.6f; }
            // 태양 폭풍: 바깥벽에 닿은 방이 가장 세다. 대피소·물벽은 거의 막는다
            if (storm > 0f)
            {
                float sr = storm * Exposure(room) * (storm > 1.2f ? 1.3f : 1f) + 0.12f * storm * MathF.Min(3, w.Body.OpenWindows(room)); // v16.26 덮개가 안 내려간 창가 · 통합: 센 폭풍은 안쪽 방까지 파고든다
                r = MathF.Max(r, sr);
            }
            r = MathF.Max(r, w.Cosmic.Radiation(room)); // v18.13 우주 대재난 방사선 (물벽이 덜고 · 대피소가 막는다)
            if ((tags & RoomTag.Shielded) != 0) r *= 0.15f;
            else if (storm > 0f && (room.Type == RoomType.Storage || room.UsedAs == RoomType.Storage)) r *= 1f - 0.45f * Facilities.Factor(room, "shelter"); // 선반 뒤 (겸용의 대가) · v16.17 선반을 들여 창고로 쓰는 방도
            s = MathF.Max(s, w.Smells.Unpleasant(room)); // v16.8 탄내 · 악취 (Smell.cs가 공기를 타고 퍼뜨린 것)
            s = MathF.Max(s, 0.8f * w.After.StaleSoot(room)); // v17.5 며칠 남는 묵은 그을음 냄새
            float a = MathF.Min(1f, dt * 4f); // 몇 분에 걸쳐 바뀐다
            room.Noise += (n - room.Noise) * a;
            room.Vibration += (v - room.Vibration) * a;
            room.Smell += (s - room.Smell) * a;
            room.Radiation += (r - room.Radiation) * MathF.Min(1f, dt * 8f);
        }
        // 방사선은 몸에 쌓인다 (우주복은 조금 막는다)
        foreach (var c in w.Crew)
        {
            if (c.Dead) continue;
            float rad = c.Outside ? 4f * storm + 0.02f + w.Cosmic.OutsideRad : c.Room?.Radiation ?? 0f; // v18.13 선체 밖 · 통합: 선체가 막아 주지 못한다 (안쪽 방의 네 배)
            if (c.Suit != null) rad *= 0.7f;
            if (rad > 0.1f) c.Dose += (rad - 0.05f) * 0.5f * dt;
            else c.Dose = MathF.Max(0f, c.Dose - 0.005f * dt); // 몸이 아주 천천히 회복한다
            if (c.Dose > 1f && w.Rng.Chance(0.05f * dt)) NeedsSystem.AddInjury(c.Vitals, 0.02f * MathF.Min(3f, c.Dose), "방사선");
            // 체력 단련: 운동하면 오르고 가만있으면 아주 천천히 빠진다
            float gym = c.Job?.Activity is RelaxActivity && c.Pose == Pose.Sitting ? Facilities.Factor(c.Room, "exercise") : 0f;
            if (HobbyActivity.Exercising(c, w)) gym = MathF.Max(gym, MathF.Max(0.7f, Facilities.Factor(c.Room, "exercise"))); // v14.3 운동 취미
            c.Fitness = Math.Clamp(c.Fitness + (gym > 0f ? 0.08f * gym : -0.004f * (w.Eras.Has("gravity") ? 0.3f : 1f)) * dt, 0f, 1f); // v12.8 인공 중력
        }
    }

    private readonly Dictionary<int, float> _exposure = new();

    /// <summary>바깥에 드러난 정도 0.35~1: 선체 벽에 닿은 칸이 많을수록 (통로처럼 배 안쪽 방은 낮다).</summary>
    public float Exposure(Room room)
    {
        Rebuild();
        if (_exposure.TryGetValue(room.Id, out var e)) return e;
        int hull = room.Cells.Count(c => Cell.Dirs4.Any(d => _w.Ship.WallAt(c + d) is { IsHull: true }));
        e = Math.Clamp(0.35f + 2.2f * hull / (float)Math.Max(1, room.Cells.Count), 0.35f, 1f);
        _exposure[room.Id] = e;
        return e;
    }

    // ── 사람에게 미치는 것 ──

    /// <summary>잠의 질 (1 = 제대로 잔다). 소음·진동·냄새가 깎는다. 조용한 침실은 조금 더 깊다.</summary>
    public static float SleepFactor(Room? room)
    {
        if (room == null) return 1f;
        float f = 1f - 0.4f * room.Noise - 0.3f * room.Vibration - 0.2f * room.Smell;
        if (room.Kind is RoomType.QuietQuarters or RoomType.PrivateCabins or RoomType.WaterWallCabin) f += 0.08f;
        f += ModulesV15.SleepAdd(room); // v15 암막 커튼 · 방음재 · 백색 소음기
        f += Props.SleepAdd(room); // v15.8 베개 · 수면등 · 모빌 …
        return Math.Clamp(f, 0.45f, 1.25f);
    }

    /// <summary>깨어 있을 때 쌓이는 스트레스 (시간당).</summary>
    public static float Stress(Room? room, bool working)
    {
        if (room == null) return 0f;
        float s = 0.03f * room.Smell + (working ? 0.004f : 0.012f) * room.Noise + 0.008f * room.Vibration + 0.05f * room.Radiation;
        return s;
    }

    /// <summary>쉬는 방의 효과 — 관측실·정원·극장·기도실·명상실은 휴게실보다 더 풀린다.</summary>
    public static float RelaxFactor(Room? room) => room?.Kind switch
    {
        RoomType.Observatory or RoomType.Garden => 1.35f,
        RoomType.Theater or RoomType.Chapel or RoomType.Meditation => 1.25f,
        RoomType.Gym or RoomType.Centrifuge => 1.1f,
        _ => 1f,
    } * (room == null ? 1f : 1f - 0.3f * room.Noise - 0.3f * room.Smell) * ModulesV15.RelaxMul(room) // v15 커피 머신 · 영사기 · 수조 …
      * Props.RelaxMul(room); // v15.8 화분 · 러그 · 액자 …

    /// <summary>작물 성장 — 진동과 방사선이 깎는다.</summary>
    public static float CropFactor(Room room) => Math.Clamp(1f - 0.3f * room.Vibration - 0.6f * room.Radiation, 0.3f, 1f);
}
