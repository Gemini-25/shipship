using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>
/// 선체. 벽 강도가 떨어지면 단계적으로 나빠진다:
/// 정상 → 찌그러짐(0.85) → 균열(0.6, 쉬익 새기 시작) → 미세 누출(0.35) → 파공(0.1 미만).
/// 새는 방은 격벽이 잠기고 환기 댐퍼가 닫혀 구획이 격리된다. 전기가 없으면 자동으로 안 된다.
/// </summary>
public static class Hull
{
    /// <summary>파공 넓이 1 = 시간당 이만큼의 "칸"이 빠져나가는 속도. 72칸 방이면 1.4분 만에 기압이 1/e.</summary>
    public const float LeakPerBreach = 3000f;

    /// <summary>강도 → 구멍 크기. 찌그러짐·균열까지는 새지 않고, 0.35 아래부터 샌다.</summary>
    public static float BreachFromIntegrity(float i)
    {
        if (i >= 0.35f) return 0f;
        if (i >= 0.1f) return 0.004f + 0.056f * (0.35f - i) / 0.25f;
        return 0.3f + 0.7f * Math.Clamp((0.1f - i) / 0.1f, 0f, 1f);
    }

    /// <summary>봉합은 하루에 이만큼 약해진다. 0.3 밑으로 떨어지면 떨어져 나간다 — 임시 조치는 임시일 뿐.</summary>
    public const float PatchDecayPerDay = 0.02f;

    /// <summary>벽에 붙어 있는 방 (안쪽).</summary>
    public static Room? InsideRoom(Ship ship, Cell wall)
    {
        foreach (var d in Cell.Dirs4)
            if (ship.RoomAt(wall + d) is Room r) return r;
        return null;
    }

    public static int SealantFor(WallState w) => w.Breach >= 0.25f ? 2 : 1;

    /// <summary>용접 한 번이 남기는 피로.</summary>
    public static float WeldFatigue(float skill) => 0.08f - 0.03f * skill;

    /// <summary>용접해서 얻을 게 있는지 (피로가 쌓인 벽은 더 올라가지 않는다).</summary>
    public static bool WorthWelding(WallState w) =>
        w.Patched || w.MaxIntegrity * 0.95f - w.Integrity > 0.08f;

    /// <summary>벽에 충격. 봉합한 곳에 큰 충격이 오면 봉합이 떨어진다.</summary>
    public static void Damage(Ship ship, Cell cell, float amount)
    {
        if (ship.WallAt(cell) is not WallState w || amount <= 0f) return;
        amount *= 1f + MathF.Max(0f, 1f - w.MaxIntegrity) * 1.5f; // 피로한 벽은 더 크게 상한다
        if (w.Reinforced) amount *= 0.7f; // 보강판을 덧댄 벽은 덜 상한다 (v7)
        float before = w.Integrity;
        w.Integrity = MathF.Max(0f, w.Integrity - amount);
        // v8: 외판이 다 뚫리고도 남은 충격은 골조를 상하게 한다 → 구조 연결 상실 (외판이 골조에서 뜯겨 나감)
        if (w.IsHull && amount > before) w.Frame = MathF.Max(0f, w.Frame - (amount - before) * 1.1f);
        if (w.Patched && (amount > 0.15f || w.FrameLost)) w.Patched = false;
        if (!w.Patched) w.Breach = BreachOf(w);
    }

    /// <summary>벽의 구멍 크기: 골조를 잃었으면 활짝 열린 구멍.</summary>
    public static float BreachOf(WallState w) => !w.IsHull ? 0f : w.FrameLost ? 1f : BreachFromIntegrity(w.Integrity);

    /// <summary>
    /// 골조 재건 (v8, 밖에서): 골조와 외판을 새로 세운다. 새것만은 못하다.
    /// </summary>
    public static void RebuildFrame(WallState w, float skill)
    {
        w.Frame = 0.75f + 0.2f * skill;
        w.MaxIntegrity = MathF.Min(w.MaxIntegrity, 0.85f);
        w.Integrity = 0.55f + 0.2f * skill;
        w.Patched = false;
        w.Breach = BreachOf(w);
        w.Replacements++;
    }

    /// <summary>
    /// 환기 댐퍼를 열어 둬야 하는지. 새는 방은 닫아 다른 방 공기가 빨려 나가지 않게,
    /// 불난 방은 닫아 연기가 번지지 않고 불이 산소를 못 받게 한다.
    /// </summary>
    public static bool WantVentOpen(World w, Room r) =>
        !r.Leaking && !w.Fire.IsKnown(r) && !r.Abandoned && !r.Detached && !r.VentSealed && !w.Structure.DuctOpen && !w.Sensors.Sealing(r)
        && !r.Purging && !r.Inerting && !w.Automation.KeepDamperShut(r) // v13.0 소화 대응 중
        && w.Hazards.GasSource(r) == null; // v11.2: 가스가 새는 방은 막아 둔다 (막고 나면 열어 세정기로 걸러 낸다)

    /// <summary>실제로 새는 넓이 (봉합하면 0).</summary>
    public static float EffectiveBreach(WallState w) => w.Patched ? 0f : w.Breach;
}

public sealed class HullSystem
{
    private readonly World _world;
    private readonly Dictionary<int, float> _lastPressure = new();
    private readonly Dictionary<int, long> _released = new();
    private readonly Dictionary<int, long> _trapped = new(); // v12.5 사람이 남은 채 닫힌 방
    public Dictionary<int, long> Trapped => _trapped;
    private readonly HashSet<Cell> _breached = new();

    public HullSystem(World world) => _world = world;

    /// <summary>새지 않던 외벽이 새기 시작했다: 벽·방·우주선의 역사에 한 번 센다.</summary>
    private void NoteBreach(Cell cell, WallState wall)
    {
        var h = _world.History;
        var room = Hull.InsideRoom(_world.Ship, cell);
        wall.Breaches++;
        MarkLog.Add(wall.Marks, _world.Tick, $"뚫렸다 ({wall.Stage})");
        // 방과 우주선은 한 번의 충돌을 한 번으로 센다 (한 시간 안에 여러 칸이 뚫려도)
        int key = room?.Id ?? -1;
        if (_roomBreach.TryGetValue(key, out var last) && _world.Tick - last < SimTime.Hours(1)) return;
        _roomBreach[key] = _world.Tick;
        h.Breaches++;
        if (room == null) return;
        room.Breaches++;
        h.BreachesByRoom[room.Id] = h.BreachesByRoom.GetValueOrDefault(room.Id) + 1;
    }

    private readonly Dictionary<int, long> _roomBreach = new();

    public void Update(float dt)
    {
        var ship = _world.Ship;
        foreach (var r in ship.Rooms) r.BreachArea = 0f;
        foreach (var (cell, wall) in ship.Walls)
        {
            if (!wall.IsHull) continue;
            if (wall.Breach <= 0f) { _breached.Remove(cell); continue; }
            if (_breached.Add(cell)) NoteBreach(cell, wall);
            if (wall.Patched)
            {
                wall.PatchQuality -= Hull.PatchDecayPerDay * dt / 24f;
                if (wall.PatchQuality < 0.3f)
                {
                    wall.Patched = false;
                    var room0 = Hull.InsideRoom(ship, cell);
                    _world.RaiseAlert($"{room0?.Name ?? "선체"} 임시 봉합이 떨어져 나갔다", room0, AlertLevel.Critical, shipWide: true);
                    MarkLog.Add(wall.Marks, _world.Tick, "임시 봉합이 떨어져 나갔다");
                    _world.History.Add(_world, HistoryKind.Damage, $"{room0?.Name ?? "선체"} 임시 봉합이 떨어져 나갔다", room0, at: cell);
                    _world.Board.RequestScan();
                }
            }
            float eff = Hull.EffectiveBreach(wall);
            foreach (var d in Cell.Dirs4)
                if (ship.RoomAt(cell + d) is Room r) r.BreachArea += eff;
        }

        foreach (var room in ship.Rooms)
        {
            if (room.Detached) continue; // 떨어져 나간 방은 우주선 밖이다
            bool wasLeaking = room.Air.Leak > 0f;
            room.Air.Leak = room.BreachArea * Hull.LeakPerBreach;
            if (room.Leaking && !wasLeaking) room.LeakingSince = _world.Tick;

            // 기압 변화 (kPa/시간)
            float p = room.Air.Pressure;
            float rate = _lastPressure.TryGetValue(room.Id, out var last) && dt > 0f ? (p - last) / dt : 0f;
            _lastPressure[room.Id] = p;

            // 감압 감지 → 격벽 잠금: 구멍이 났거나, 80kPa 아래에서 빠르게 빠지고 있을 때 (옆방 격벽이 열려 공기가 빨려 갈 때)
            // (격벽을 막 푼 직후엔 옆방과 기압이 섞이며 잠깐 빠지는 게 당연하니 한 시간은 기압 변화로 다시 잠그지 않는다)
            bool settling = _released.TryGetValue(room.Id, out var releasedAt) && _world.Tick - releasedAt < SimTime.Hours(1);
            bool depress = room.Leaking || (p < 80f && rate < -5f && !settling);
            if (depress && !room.Lockdown)
            {
                room.Lockdown = true;
                int locked = 0;
                bool auto = _world.Automation.DoorsIn(room); // v9.2: 격벽 자동 잠금은 주 컴퓨터가 한다 (v12.3 데이터선이 그 방까지 닿아야)
                // v12.5 사람 우선: 안에 사람이 있으면 2분 기다린다 (배 우선이면 바로)
                var inside = _world.Crew.Where(c => !c.Dead && c.Room == room && !c.Outside).ToList();
                if (auto && inside.Count > 0 && !_world.Automation.ShipFirst)
                {
                    room.LockPendingUntil = _world.Tick + SimTime.Minutes(2);
                    _world.RaiseAlert($"{room.Name} 감압! 안에 {string.Join("·", inside.Select(c => c.Name))} — 격벽 폐쇄 대기 2분 (사람 우선)", room, AlertLevel.Critical, shipWide: true);
                    _world.Automation.Reason($"lock:{room.Id}", $"{room.Name} 감압 · 안에 {inside.Count}명 — 방침(사람 우선)에 따라 격벽 폐쇄를 2분 늦춘다 · 그동안 옆방 공기도 빠진다", SimTime.Minutes(10));
                    _world.Board.RequestScan();
                    continue;
                }
                if (auto && inside.Count > 0) _trapped[room.Id] = _world.Tick; // 배 우선: 사람이 안에 있는 채 닫는다
                foreach (var d in room.Doors)
                {
                    if (d.IsExternal || !d.Powered || !auto) continue;
                    d.Locked = true;
                    locked++;
                }
                bool unlockedAny = room.Doors.Any(d => !d.IsExternal && !d.Locked);
                _world.RaiseAlert($"{room.Name} 감압! " + (!auto ? (_world.Automation.Doors ? "데이터선이 끊겨 격벽이 저절로 닫히지 않는다" : "자동화가 꺼져 격벽이 저절로 닫히지 않는다") : unlockedAny ? "일부 격벽이 전기·구동기가 없어 안 닫힘" : "격벽 폐쇄"),
                    room, AlertLevel.Critical, shipWide: true);
                _world.Board.RequestScan();
            }
            // 해제: 새지 않고 기압이 90을 넘었거나, 공기 탱크가 비어 더 올라갈 수 없는데 안정됐을 때
            else if (room.Lockdown && !room.Abandoned && !room.Leaking && !room.ResponseHold && rate > -1f && (p > 90f || _world.Air.Reserve < 1f))
            {
                room.Lockdown = false;
                _released[room.Id] = _world.Tick;
                foreach (var d in room.Doors)
                {
                    var other = d.RoomA == room ? d.RoomB : d.RoomA;
                    if (!d.IsExternal && (other == null || !other.Lockdown)) d.Locked = false;
                }
                _world.Log.Add(_world.Tick, LogKind.Ship, p > 90f ? $"{room.Name} 재가압 완료 · 격벽 해제"
                    : $"{room.Name} 격벽 해제 — 공기 탱크가 비어 {p:0}kPa에서 더 오르지 않는다");
            }
            // v12.5 기다리던 격벽: 사람이 다 나왔거나 시간이 다 됐으면 닫는다
            if (room.LockPendingUntil >= 0)
            {
                bool left = !_world.Crew.Any(c => !c.Dead && c.Room == room && !c.Outside);
                if (left || _world.Tick >= room.LockPendingUntil || !room.Lockdown)
                {
                    if (room.Lockdown && !left)
                    {
                        _trapped[room.Id] = _world.Tick; // 닫히는 순간 안에 남은 사람 — 한 시간 안에 쓰러지면 센다
                    }
                    // 기다리는 사이 옆방까지 빠졌나
                    if (room.Doors.Any(d => (d.RoomA == room ? d.RoomB : d.RoomA) is Room o && !o.Leaking && o.Air.Pressure < 85f)) _world.Automation.LateSeals++;
                    room.LockPendingUntil = -1;
                    if (room.Lockdown) _world.Log.Add(_world.Tick, LogKind.Ship, $"{room.Name} 격벽 폐쇄" + (left ? " — 모두 빠져나왔다" : " — 기다림이 끝났다"));
                }
                else continue;
            }
            // 잠긴 방의 문에 전기가 다시 들어오면 마저 닫는다 (손으로 닫지 못한 문)
            if (room.Lockdown && !room.Abandoned && _world.Automation.DoorsIn(room))
                foreach (var d in room.Doors)
                    if (!d.IsExternal && d.Powered && !d.Locked) d.Locked = true;

            // 환기 댐퍼: 전기가 있으면 자동, 없으면 그 자리에 멈춘다
            bool want = Hull.WantVentOpen(_world, room);
            if (room.DamperJammed && _world.Fire.CountIn(room) == 0) room.DamperJammed = false; // 식으면 풀린다
            if (room.VentOpen != want && room.Powered && !room.DamperJammed && !room.DamperStuck && _world.Automation.DampersIn(room))
            {
                room.VentOpen = want;
                string why = want ? "" : room.Leaking ? " (감압)" : _world.Fire.IsKnown(room) ? " (화재)" : _world.Structure.DuctOpen ? " (환기망 격리)" : "";
                _world.Log.Add(_world.Tick, LogKind.Ship, $"{room.Name} 환기 댐퍼 자동 {(want ? "개방" : "폐쇄")}{why}");
            }
        }
    }
}
