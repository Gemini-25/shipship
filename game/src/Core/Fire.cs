using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>
/// 칸 단위 화재. 산소를 먹고 자라며 연기·CO2·열을 내고, 옆 칸으로 번지고,
/// 설비와 벽을 상하게 하고, 곁에 있는 사람에게 화상을 입힌다.
/// 산소가 모자라면(감압, 밀폐된 작은 방) 저절로 꺼진다.
/// 화재 감지기는 전기가 들어오는 방에서만 울린다. 정전된 방의 불은 누가 보기 전까지 모른다.
/// </summary>
public sealed class FireSystem
{
    private readonly World _world;
    private readonly Dictionary<Cell, float> _fires = new();
    private readonly HashSet<int> _knownRooms = new();

    /// <summary>바닥에 남은 그을음 (흔적).</summary>
    public Dictionary<Cell, float> Scorch { get; } = new();

    private long _lastFireTick = -1_000_000;

    /// <summary>불에 탄 물건 수 (누적).</summary>
    public int ItemsBurned { get; private set; }

    public FireSystem(World world) => _world = world;

    /// <summary>방마다 불을 알게 된 틱 (얼마나 오래 타고 있나: 끌 수 없는 불이면 사출을 논의한다).</summary>
    private readonly Dictionary<int, long> _knownSince = new();

    public float BurningHours(Room r) => _knownSince.TryGetValue(r.Id, out var t) ? (_world.Tick - t) / (float)SimTime.TicksPerHour : 0f;

    /// <summary>방의 불을 모두 없앤다 (방이 떨어져 나가 진공이 됐다).</summary>
    public void ClearRoom(Room room)
    {
        foreach (var c in room.Cells) _fires.Remove(c);
        _knownRooms.Remove(room.Id);
        _knownSince.Remove(room.Id);
    }

    public IReadOnlyDictionary<Cell, float> Fires => _fires;
    public int Count => _fires.Count;
    public float At(Cell c) => _fires.TryGetValue(c, out var v) ? v : 0f;
    public bool IsKnown(Room r) => _knownRooms.Contains(r.Id);

    /// <summary>v16.26 열린 문 곁(두 칸 안)에 깨어 선 사람이 문 너머로 연기가 쏟아지는 걸 본다 (탄 냄새를 따라와 문을 연 사람 · 작은 방은 연기가 금방 차 들어가지 못한다).</summary>
    private CrewMember? DoorWitness(Room room)
    {
        if (room.Air.Smoke < 0.3f) return null;
        foreach (var d in room.Doors)
        {
            if (d.IsExternal || d.Openness < 0.4f) continue;
            var other = d.RoomA == room ? d.RoomB : d.RoomA;
            if (other == null) continue;
            foreach (var c in _world.Crew)
                if (c.Room == other && c.IsAwake && c.CanAct && (c.Position - d.Cell.Center).LengthSquared() < 2.5f * 2.5f) return c;
        }
        return null;
    }
    public int CountIn(Room r) => _fires.Count == 0 ? 0 : _fires.Keys.Count(c => _world.Ship.RoomAt(c) == r); // v14.2 불이 없으면 바로

    public bool Ignite(Cell c, float intensity, Cell? from = null)
    {
        var ship = _world.Ship;
        if (ship.Grid.Kind(c) != TileKind.Floor || ship.RoomAt(c) is not Room room) return false;
        if (room.Air.O2 < 12f) return false;
        intensity *= ModulesV15.FireMul(room); // v15 방화포 함이 있으면 바로 덮는다
        _fires[c] = MathF.Max(At(c), intensity);
        _world.Causes.OnIgnite(c, from); // v12.2 인과 사슬: 무엇이 붙였나 (번져 왔으면 그 불)
        return true;
    }

    /// <summary>소화기로 뿌린다. radius 안의 불 세기를 amount만큼 줄인다.</summary>
    public void Suppress(Cell center, float radius, float amount)
    {
        foreach (var c in _fires.Keys.ToList())
        {
            float dx = c.X - center.X, dy = c.Y - center.Y;
            if (dx * dx + dy * dy > radius * radius) continue;
            float v = _fires[c] - amount;
            if (v <= 0f) _fires.Remove(c);
            else _fires[c] = v;
        }
    }

    public bool AnyWithin(Cell center, float radius)
    {
        if (_fires.Count == 0) return false; // v14.2 매 틱 사람마다 불린다 — 불이 없으면 바로
        foreach (var c in _fires.Keys)
            if ((c.X - center.X) * (c.X - center.X) + (c.Y - center.Y) * (c.Y - center.Y) <= radius * radius) return true;
        return false;
    }

    /// <summary>알려진 불: 방마다 가장 센 칸과 칸 수.</summary>
    public IEnumerable<(Room room, Cell hottest, int count)> KnownFires()
    {
        var ship = _world.Ship;
        return _fires.GroupBy(kv => ship.RoomAt(kv.Key))
            .Where(g => g.Key != null && _knownRooms.Contains(g.Key.Id))
            .Select(g => (g.Key!, g.OrderByDescending(kv => kv.Value).First().Key, g.Count()));
    }

    public void Update(float dt)
    {
        UpdateCore(dt);
        _world.Paths.HazardChanged(); // v14.2 거리장 캐시: 칸 위험을 다시 채웠다
    }

    private void UpdateCore(float dt)
    {
        var w = _world;
        var ship = w.Ship;
        var cost = w.Paths.CellHazard;
        Array.Clear(cost);

        foreach (var (cell, intensity0) in _fires.ToList())
        {
            var room = ship.RoomAt(cell);
            if (room == null) { _fires.Remove(cell); continue; }
            var air = room.Air;
            float intensity = intensity0;
            float vol = room.Volume;

            // 산소가 있으면 자라고, 없으면 꺼진다
            float o2 = air.O2;
            // 공기가 충분하면 10분 남짓이면 활활 타오른다. 진공에서는 순식간에 꺼진다.
            intensity += (air.Pressure < 30f ? -20f : o2 < 6f ? -10f : o2 < 11f ? -2.5f : 3.5f * (o2 - 11f) / 10f - 0.3f) * dt; // v13.0 질식 소화: 산소 6kPa 아래면 몇 분 만에
            // 자동 소화 장치 (v7 개조): 전기가 있으면 불이 자라는 것보다 빨리 뿌린다
            if (room.Suppression && room.Powered) intensity -= 4f * dt;
            if (intensity <= 0f) { _fires.Remove(cell); continue; }
            intensity = MathF.Min(1f, intensity);
            _fires[cell] = intensity;
            using var because = w.Causes.Because(w.Causes.FireNodeAt(cell)); // v12.2 이 불이 태운 것은 이 불의 자식

            air.O2 = MathF.Max(0f, air.O2 - 400f * intensity * dt / vol);
            air.CO2 += 300f * intensity * dt / vol;
            // v12.2 산소가 모자란 불은 일산화탄소를 더 낸다
            air.CO = MathF.Min(1f, air.CO + (intensity < 0.3f ? 150f : o2 < 16f ? 40f : 14f) * intensity * dt / vol); // 통합: 닫힌 방에서 타면 소리 없이 찬다 · 연기만 피우는 불씨(훈소)가 가장 많이 낸다 (잠든 사람을 깨우지 않는다)
            air.Smoke = MathF.Min(1f, air.Smoke + 120f * intensity * dt / vol);
            air.Temperature = MathF.Min(95f, air.Temperature + 25f * intensity * dt * 30f / vol);

            // 번짐 (같은 방 옆 칸, 열린 문 너머) — v12.2 짙은 산소에서는 빨리 번진다
            float rich = o2 > 21f ? MathF.Pow(o2 / 21f, 1.5f) : 1f;
            if (w.Rng.Chance(4f * intensity * rich * dt * w.Body.SpreadMul(cell) * w.Origin.FireMul)) // v16.3 바닥재가 타는 정도 · 젖은 바닥
            {
                var d = w.Rng.Pick(Cell.Dirs4);
                var n = cell + d;
                var door = ship.DoorAt(n);
                if (door != null && door.Openness > 0.5f) n = n + d;
                if (!_fires.ContainsKey(n)) Ignite(n, 0.15f, cell);
            }

            // 설비 손상 (수명은 영구히 깎인다), 보관함 속 물건이 탄다
            foreach (var dd in Cell.Dirs8.Append(new Cell(0, 0)))
            {
                var f = ship.FurnitureAt(cell + dd);
                if (f?.Machine is Machine m)
                {
                    m.Condition = MathF.Max(0.02f, m.Condition - 0.15f * intensity * dt);
                    m.Fouled = MathF.Min(1f, m.Fouled + 0.1f * intensity * dt); // v12.2 그을음
                    if (m.Feed > 0f) Procedures.DamageLinks(w, m, 0.12f * intensity * dt, 0f, "불"); // v12.1 전선 피복이 탄다
                    if (w.Rng.Chance(0.25f * intensity * dt)) w.Machines.Break(m);
                    if (m.Crop is CropState crop && crop.Growth > 0.02f && w.Rng.Chance(0.6f * intensity * dt))
                    {
                        crop.Growth = 0f;
                        w.Machines.CropsLost++;
                        w.Log.Add(w.Tick, LogKind.Warning, $"{m.Name}의 작물이 불에 탔다");
                    }
                }
                if (f?.Storage is Inventory inv && inv.Total > 0 && w.Rng.Chance(0.5f * intensity * dt))
                {
                    var kinds = inv.Contents.Select(x => x.kind).ToList();
                    var kind = w.Rng.Pick(kinds);
                    int burned = inv.Take(kind, w.Rng.Range(1, 4));
                    ItemsBurned += burned;
                    w.Log.Add(w.Tick, LogKind.Warning, $"{f.Label}의 {ItemKinds.Name(kind)} {burned}개가 불탔다");
                }
            }

            // 배 전체 망: 불이 지나가는 간선을 태운다 (전선 피복이 가장 약하다)
            if (w.Rng.Chance(0.5f * dt * 4f)) w.Net.DamageNear(cell, 1.2f, 0.5f * intensity, "불", fire: true);
            // 배선: 불이 그 방 회로의 전선을 태운다
            if (w.Rng.Chance(0.3f * intensity * dt)
                && ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault()?.Machine is Machine panel
                && !panel.Faults.Any(x => x.Circuit == room.Circuit))
            {
                panel.Faults.Add(new Fault { Kind = FaultKind.ShortCircuit, Since = w.Tick, Circuit = room.Circuit });
                panel.FaultCount++;
                w.Causes.OnFault(panel, panel.Faults[^1]);
                w.History.CircuitFaults++;
                MarkLog.Add(panel.Marks, w.Tick, $"불이 {PowerGrid.CircuitName(room.Circuit)} 회로를 태웠다");
                w.RaiseAlert($"불이 배선을 태웠다 — {PowerGrid.CircuitName(room.Circuit)} 회로 단락 ({room.Name})", room, AlertLevel.Warning, shipWide: true);
                w.Board.RequestScan();
            }
            // 벽 그을음·약화
            foreach (var d4 in Cell.Dirs4)
            {
                if (ship.WallAt(cell + d4) is not WallState ws) continue;
                ws.Scorch = MathF.Min(1f, ws.Scorch + 0.25f * intensity * dt);
                Hull.Damage(ship, cell + d4, 0.01f * intensity * dt);
            }
            Scorch[cell] = MathF.Min(1f, (Scorch.TryGetValue(cell, out var sc) ? sc : 0f) + 0.4f * intensity * dt);

            // 화상
            foreach (var c in w.Crew)
            {
                if (c.Dead) continue;
                float dx = c.Position.X - (cell.X + 0.5f), dy = c.Position.Y - (cell.Y + 0.5f);
                if (dx * dx + dy * dy > 1.6f * 1.6f) continue;
                float protect = c.Suit != null ? 0.3f : 1f;
                float near = dx * dx + dy * dy < 0.6f * 0.6f ? 2.2f : 1f; // 통합: 불길 한가운데 (누운 자리 · 갇힌 자리)는 몇 분이면 깊게 덴다
                c.Vitals.Health -= 0.5f * near * intensity * dt * protect;
                NeedsSystem.AddInjury(c.Vitals, 0.3f * near * intensity * dt * protect, "화상");
            }

            // 길찾기: 불과 그 주변은 가기 싫다
            int idx = ship.Grid.Index(cell);
            cost[idx] += (int)(80 * intensity);
            foreach (var d8 in Cell.Dirs8)
                if (ship.Grid.InBounds(cell + d8)) cost[ship.Grid.Index(cell + d8)] += (int)(25 * intensity);
        }

        // 연기 빠짐: 환기가 되면 빨리, 아니면 아주 천천히
        foreach (var room in ship.Rooms)
            room.Air.Smoke = MathF.Max(0f, room.Air.Smoke - (room.Powered && room.VentOpen ? 1.0f : 0.05f) * dt);

        // 감지: 전기가 있는 방은 감지기가, 없으면 그 방에 깨어 있는 사람이 본다
        var burning = _fires.Keys.Select(c => ship.RoomAt(c)).Where(r => r != null).Distinct().ToList();
        foreach (var room in burning)
        {
            if (_knownRooms.Contains(room!.Id)) continue;
            var witness = w.Crew.FirstOrDefault(c => c.Room == room && c.IsAwake && c.CanAct) ?? DoorWitness(room!); // v16.26 열린 문간에서 연기가 쏟아지는 걸 본 사람도
            bool detector = room.Powered && w.Automation.AlarmsIn(room); // v9.2: 감지기 경보는 주 컴퓨터가 돌린다
            var bot = detector || witness != null ? null : w.Robots.Witness(room); // v10.10: 순찰하던 방재 로봇이 본다
            if (!detector && witness == null && bot == null) continue;
            _knownRooms.Add(room.Id);
            _knownSince[room.Id] = w.Tick;
            bool critical = room.Type is RoomType.Reactor or RoomType.Power or RoomType.LifeSupport or RoomType.Cooling;
            string how = detector ? "화재 감지기 작동" : bot != null ? $"{Ko.IGa(bot.Name)} 발견" : $"{Ko.IGa(witness!.Name)} 발견" + (room.Powered ? " (경보가 돌지 않았다)" : "");
            how = w.Smells.FireFound(room, how, witness); // v16.8 탄 냄새를 따라 먼저 와 있던 사람
            // 역사: 한 방에 한 번 (진화될 때까지)
            room.Fires++;
            // 우주선은 번진 불을 한 번으로 센다 (한 시간 안에 옆방으로 옮겨 붙은 불)
            if (w.Tick - _lastFireTick > SimTime.Hours(1)) w.History.Fires++;
            _lastFireTick = w.Tick;
            w.History.FiresByRoom[room.Id] = w.History.FiresByRoom.GetValueOrDefault(room.Id) + 1;
            MarkLog.Add(room.Marks, w.Tick, $"화재 ({how})");
            w.History.Add(w, HistoryKind.Incident, $"{room.Name} 화재 — {how}", room, witness != null ? new[] { witness } : null);
            w.RaiseAlert($"{room.Name} 화재 — {how}", room, critical ? AlertLevel.Critical : AlertLevel.Warning, shipWide: true);
            if (detector) w.Automation.Book.Add(ActKind.Alarm, room, $"{room.Name} 화재 감지기 · 불 {w.Fire.CountIn(room)}칸", critical ? "핵심 방 — 위급" : "화재", "배 전체 화재 경보", "진화 (소화조)", "fire:" + room.Id, SimTime.Minutes(30), 10f); // v16.0
            w.Board.RequestScan();
        }
        foreach (var id in _knownRooms.ToList())
        {
            var room = ship.Rooms[id];
            if (burning.Contains(room)) continue;
            _knownRooms.Remove(id);
            _knownSince.Remove(id);
            w.Log.Add(w.Tick, LogKind.Ship, $"{room.Name} 불을 다 껐다");
        }
    }
}
