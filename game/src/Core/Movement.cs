using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v14.5 움직임에 주변 사람과 일의 사정이 묻어난다.
// 좁은 통로에서는 비켜서고(환자를 업은 사람 · 급히 뛰는 사람 · 손이 찬 사람이 먼저), 통로에서 일하는 사람이 오래 막고 있으면 돌아간다.
// 자는 사람 곁은 조용히 걷고, 뛰어 지나가면 선잠을 깨운다. 위험한 문 앞에서는 계기를 보고 짝을 기다린다.
// 경보가 울리면 들고 있던 짐을 그 자리에 내려놓고 가며, 조용해지면 챙기러 돌아온다. 큰 소리가 나면 움찔하며 그쪽을 본다.

/// <summary>한 사람의 걸음 상태 (몸짓 · 비켜서기 · 문 앞 확인 · 움찔).</summary>
public sealed class Gait
{
    /// <summary>이때까지 비켜서 있다 (멈춤).</summary>
    public long YieldUntil { get; internal set; } = -1;
    public int YieldTo { get; internal set; } = -1;
    public string? YieldWhy { get; internal set; }
    /// <summary>비켜선 쪽 (칸 단위 · 화면에만 — 몸을 벽 쪽으로 붙인다).</summary>
    public Vector2 Aside { get; internal set; }

    /// <summary>일하는 사람에게 막혀 기다린 틱.</summary>
    internal int Blocked { get; set; }
    internal int BlockedBy { get; set; } = -1;

    /// <summary>자는 사람 곁을 조용히 지나는 중 (마지막 틱).</summary>
    public long QuietTick { get; internal set; } = -100;
    public bool Quiet(World w) => w.Tick - QuietTick <= 1;

    /// <summary>문 앞에서 계기를 본다 (이때까지 멈춤).</summary>
    public long DoorCheckUntil { get; internal set; } = -1;
    public Door? CheckedDoor { get; internal set; }
    public long CheckedAt { get; internal set; } = -100000;
    public string? DoorReading { get; internal set; }

    /// <summary>큰 소리에 움찔 (이때까지 멈추고 그쪽을 본다).</summary>
    public long StartleUntil { get; internal set; } = -1;
    public Vector2 StartleAt { get; internal set; }
    public string? StartleWhy { get; internal set; }
    public long StartleTick { get; internal set; } = -100000;

    /// <summary>화면에 남겨 두는 틱 (멈춤은 몇 초지만 눈에 보이게 조금 더).</summary>
    public const int Linger = 40;

    /// <summary>지금(방금) 걸음을 화면에 한 줄로.</summary>
    public string? Line(CrewMember c, World w)
    {
        if (w.Tick - StartleTick < Linger && StartleWhy != null) return $"움찔 — {StartleWhy}";
        if (w.Tick - CheckedAt < Linger && DoorReading != null) return $"문 앞 확인 — {DoorReading}";
        if (w.Tick - YieldUntil < Linger / 2 && YieldWhy != null) return $"비켜섰다 — {YieldWhy}";
        if (Quiet(w)) return "자는 사람 곁이라 조용히 걷는다";
        return null;
    }
}

/// <summary>경보에 내려놓고 간 짐 (나중에 챙긴다).</summary>
public sealed class Stash
{
    public int Id { get; init; }
    public Cell Cell { get; init; }
    public ItemStack Stack { get; set; }
    public int Owner { get; init; }
    public long Tick { get; init; }
    public string Why { get; init; } = "";
}

public sealed class MoveStats
{
    public int Yields, ForPatient, ForUrgent, ForLoad, Squeezes, Reroutes, QuietPasses, Woke, DoorChecks, BuddyWaits, SetDowns, PickedUp, Startles;
    public string Summary() =>
        $"비켜섬 {Yields}(환자 {ForPatient} · 급한 사람 {ForUrgent} · 짐 {ForLoad}) · 비집고 지나감 {Squeezes} · 돌아감 {Reroutes} · 조용히 {QuietPasses} · 깨움 {Woke} · 문 앞 확인 {DoorChecks}(짝 기다림 {BuddyWaits}) · 내려놓음 {SetDowns}(챙김 {PickedUp}) · 움찔 {Startles}";
}

public sealed class MovementSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7333 + 67));
    public MoveStats Stats { get; } = new();
    public List<Stash> Stashes { get; } = new();
    private int _stashId;

    // 틱마다: 칸 → 그 칸의 사람 (Id+1) · 자는 사람이 있는 방
    private int[] _occ = Array.Empty<int>();
    private readonly List<int> _touched = new();
    private readonly HashSet<Room> _sleepRooms = new();
    private readonly Dictionary<int, long> _roomMarked = new();
    private bool _calm = true;

    public MovementSystem(World w) => _w = w;

    /// <summary>승무원이 움직이기 전에 (틱마다): 누가 어느 칸에 있나 · 어느 방에 자는 사람이 있나.</summary>
    public void BeginTick()
    {
        var w = _w;
        var g = w.Ship.Grid;
        if (_occ.Length != g.CellCount) { _occ = new int[g.CellCount]; _touched.Clear(); }
        foreach (int i in _touched) _occ[i] = 0;
        _touched.Clear();
        _sleepRooms.Clear();
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.CarriedBy != null || c.Outside || c.Room == null) continue;
            if (!g.InBounds(c.Cell)) continue;
            int i = g.Index(c.Cell);
            if (_occ[i] == 0) { _occ[i] = c.Id + 1; _touched.Add(i); }
            if (c.Pose == Pose.Sleeping && !c.Down) _sleepRooms.Add(c.Room);
            // 비켜섰던 몸은 천천히 길 가운데로 돌아온다
            if (c.Gait.YieldUntil < w.Tick && c.Gait.Aside != Vector2.Zero)
                c.Gait.Aside = c.Gait.Aside.LengthSquared() < 0.002f ? Vector2.Zero : c.Gait.Aside * 0.93f;
        }
        if (w.Tick % 25 == 0) _calm = Crisis.Level(w) < CrisisLevel.Emergency; // 비상(불·감압·쓰러짐)일 때만 — 작은 경보로는 걸음을 바꾸지 않는다
    }

    private CrewMember? At(Cell cell)
    {
        var g = _w.Ship.Grid;
        if (!g.InBounds(cell)) return null;
        int v = _occ.Length > 0 ? _occ[g.Index(cell)] : 0;
        return v > 0 ? _w.Crew[v - 1] : null;
    }

    /// <summary>가는 방향으로 보아 폭이 두 칸 이하인 곳 (문 · 복도 · 설비 사이) — 마주치면 누군가 비켜야 한다.</summary>
    public bool Tight(Cell cell, Cell dir)
    {
        var ship = _w.Ship;
        if (ship.DoorAt(cell) != null) return true;
        if (dir.X == 0 && dir.Y == 0) return false;
        int px = -dir.Y, py = dir.X;
        int width = 1;
        for (int s = 1; s <= 2 && ship.IsWalkable(new Cell(cell.X + px * s, cell.Y + py * s)); s++) width++;
        for (int s = 1; s <= 2 && ship.IsWalkable(new Cell(cell.X - px * s, cell.Y - py * s)); s++) width++;
        return width <= 2;
    }

    private static Cell Dir(Cell from, Cell to) => new(Math.Sign(to.X - from.X), Math.Sign(to.Y - from.Y));

    /// <summary>길에서 먼저 가는 순서: 환자를 업은 사람 → 급한 사람 → 손이 찬 사람 → 일하러 가는 사람 → 나머지.</summary>
    public static int Priority(CrewMember c) =>
        c.CarryingPerson != null ? 5
        : c.Job?.Urgent == true || c.Job?.Order is WorkOrder o && (WorkKinds.IsEmergency(o.Kind) || o.Kind is WorkKind.Rescue or WorkKind.Treat or WorkKind.SafetyWatch) ? 4 // 비상 대응 중인 사람끼리는 서로 비켜 주느라 늦지 않는다
        : c.Carrying != null ? 3 : c.Job?.Order != null ? 2 : 1;

    private static string Why(CrewMember other) => other.CarryingPerson is CrewMember p ? $"{p.Name}을(를) 업은 {other.Name}"
        : other.Job?.Urgent == true ? $"급히 가는 {other.Name}" : other.Carrying is ItemStack s ? $"{ItemKinds.Name(s.Kind)}을(를) 든 {other.Name}" : other.Name;

    /// <summary>
    /// 한 걸음 내딛기 전에: 주변 사람과 문 너머 사정에 맞춰 이번 틱의 걸음 배율 (0이면 이번 틱은 서 있다).
    /// </summary>
    public float Manners(CrewMember c, List<Cell> path)
    {
        var w = _w;
        var gt = c.Gait;
        if (c.Outside || c.CarriedBy != null || c.PathIndex >= path.Count) return 1f;
        long now = w.Tick;
        if (gt.StartleUntil >= now) { Locomotion.Face(c, gt.StartleAt); return 0f; }
        if (gt.YieldUntil >= now) return 0f;
        if (gt.DoorCheckUntil >= now) return 0f;
        var next = path[c.PathIndex];
        bool urgent = c.Job?.Urgent == true;
        float mul = 1f;

        // ── 문 앞: 너머가 위험하면 계기를 보고 (짝이 있으면 기다리고) 들어간다 ──
        if (w.Ship.DoorAt(next) is Door door && (gt.CheckedDoor != door || now - gt.CheckedAt > SimTime.Minutes(5)))
        {
            var beyond = c.PathIndex + 1 < path.Count ? w.Ship.RoomAt(path[c.PathIndex + 1]) : null;
            if (beyond != null && beyond != c.Room && Hazard(beyond) is string reading)
            {
                gt.CheckedDoor = door; gt.CheckedAt = now;
                gt.DoorReading = reading;
                gt.DoorCheckUntil = now + (urgent ? 1 : 2);
                Locomotion.Face(c, next.Center);
                Stats.DoorChecks++;
                // 위험한 일을 맡은 조: 감시자가 곁에 올 때까지 (조금) 기다린다
                if (w.Command.TeamOf(c) is { Hazard: true } team && team.Worker == c.Id && team.Watcher >= 0 && team.Watcher < w.Crew.Count
                    && w.Crew[team.Watcher] is { CanAct: true } buddy && (buddy.Position - c.Position).LengthSquared() > 16f)
                {
                    gt.DoorCheckUntil = now + 6;
                    Stats.BuddyWaits++;
                    c.Say(w, Persona.Say(c, $"{buddy.Name}, 준비됐어? 문 너머 {reading}"));
                }
                else if (c.SaidUntil < now) c.Say(w, $"문 너머 {reading}");
                return 0f;
            }
        }

        // ── 좁은 곳에서 마주친 사람 ──
        var other = At(next);
        var dir = Dir(c.Cell, next);
        if (dir.X == 0 && dir.Y == 0) dir = new Cell(Math.Sign((int)MathF.Round(c.Facing.X)), Math.Sign((int)MathF.Round(c.Facing.Y)));
        bool tight = other != null && Tight(next, dir);
        // 넓은 곳에서 마주 오면: 순서가 낮은 쪽이 멈추지 않고 몸만 살짝 비킨다
        if (other != null && !tight && other != c && other.IsMoving && !other.Down && other.CarriedBy == null && Vector2.Dot(other.Facing, c.Facing) < -0.2f
            && (Priority(c) < Priority(other) || Priority(c) == Priority(other) && c.Id > other.Id))
        {
            gt.Aside = new Vector2(-c.Facing.Y, c.Facing.X) * 0.25f;
            mul *= 0.9f;
        }
        if (other != null && other != c && !other.Down && other.CarriedBy == null && other != c.CarryingPerson && tight)
        {
            if (other.IsMoving)
            {
                // 마주 오는 사람: 순서가 낮은 쪽이 비켜선다 (같으면 나중에 탄 사람이)
                bool headOn = Vector2.Dot(other.Facing, c.Facing) < -0.2f;
                if (headOn && other.Gait.YieldUntil < now)
                {
                    int pc = Priority(c), po = Priority(other);
                    bool iYield = pc < po || pc == po && c.Id > other.Id;
                    if (iYield && !(gt.YieldTo == other.Id && now - gt.YieldUntil < 20))
                    {
                        StepAside(c, other, po);
                        return 0f;
                    }
                    mul *= 0.85f; // 비켜선 사람 곁을 스쳐 지난다
                }
            }
            else if (other.Gait.YieldUntil < now)
            {
                // 서 있는 사람: 그냥 서 있으면 몸을 비켜 주고, 일하는 중이면 잠깐 기다렸다가 — 오래 막으면 돌아가거나 비집고 지나간다
                bool busy = other.Pose is Pose.Working or Pose.Sitting or Pose.Sleeping || other.Job?.Current is WorkToil; // 자는 사람은 비켜 주지 않는다 (돌아간다)
                if (!busy)
                {
                    var side = new Vector2(-c.Facing.Y, c.Facing.X);
                    other.Gait.Aside = side * 0.3f;
                    other.Gait.YieldUntil = now + 2;
                    other.Gait.YieldTo = c.Id;
                    other.Gait.YieldWhy = $"{c.Name}이(가) 지나간다";
                    Locomotion.Face(other, c.Position);
                    mul *= 0.7f;
                }
                else
                {
                    if (gt.BlockedBy != other.Id) { gt.BlockedBy = other.Id; gt.Blocked = 0; }
                    gt.Blocked++;
                    if (gt.Blocked < (urgent ? 2 : 4)) return 0f;
                    if (gt.Blocked == (urgent ? 2 : 4) && Reroute(c, path, next, other)) return 0f;
                    if (gt.Blocked == (urgent ? 2 : 4) || gt.Blocked % 25 == 0)
                    {
                        Stats.Squeezes++;
                        if (c.SaidUntil < now) c.Say(w, Persona.Say(c, urgent ? "비켜! 급해" : "잠깐 지나갈게요"));
                        if (urgent) other.Gait.Aside = new Vector2(-c.Facing.Y, c.Facing.X) * 0.25f;
                    }
                    mul *= 0.35f;
                }
            }
        }
        else if (gt.Blocked > 0 && (other == null || other.Id != gt.BlockedBy)) { gt.Blocked = 0; gt.BlockedBy = -1; }

        // ── 자는 사람이 있는 방: 조용히 (급하면 뛰고 — 그 소리에 선잠이 깬다) ──
        if (c.Room is Room room && _sleepRooms.Contains(room))
        {
            if (!urgent && _calm)
            {
                if (!gt.Quiet(w)) Stats.QuietPasses++;
                gt.QuietTick = now;
                mul *= 0.7f;
            }
            else if (urgent) Disturb(c, room);
        }
        return mul;
    }

    /// <summary>문 너머의 위험 (계기에 보이는 것) — 없으면 null.</summary>
    private string? Hazard(Room r)
    {
        var a = r.Air;
        if (r.Detached) return null;
        if (a.Pressure < 75f) return $"{a.Pressure:0}kPa";
        if (_w.Fire.CountIn(r) > 0) return $"불 · {a.Temperature:0}℃";
        if (a.Smoke > 0.3f) return "연기";
        if (a.O2 < 16f) return $"산소 {a.O2:0.0}kPa";
        if (a.Temperature > 45f) return $"{a.Temperature:0}℃";
        if (a.CO > 0.05f || a.Toxin > 0.2f) return "독한 공기";
        return null;
    }

    private void StepAside(CrewMember c, CrewMember other, int otherPriority)
    {
        var w = _w;
        var gt = c.Gait;
        // 옆 칸이 비어 있으면 그쪽으로 몸을 빼고, 벽뿐이면 벽에 붙는다
        var side = new Vector2(-c.Facing.Y, c.Facing.X);
        var sideCell = new Cell((int)MathF.Floor(c.Position.X + side.X * 0.9f), (int)MathF.Floor(c.Position.Y + side.Y * 0.9f));
        if (!w.Ship.IsWalkable(sideCell)) { side = -side; sideCell = new Cell((int)MathF.Floor(c.Position.X + side.X * 0.9f), (int)MathF.Floor(c.Position.Y + side.Y * 0.9f)); }
        gt.Aside = side * (w.Ship.IsWalkable(sideCell) ? 0.4f : 0.22f);
        gt.YieldUntil = w.Tick + (otherPriority >= 4 ? 4 : 3);
        gt.YieldTo = other.Id;
        gt.YieldWhy = Why(other);
        Locomotion.Face(c, other.Position);
        Stats.Yields++;
        if (otherPriority == 5)
        {
            Stats.ForPatient++;
            if (other.SaidUntil < w.Tick) other.Say(w, Persona.Say(other, "비켜 줘, 환자야!"));
        }
        else if (otherPriority == 4) Stats.ForUrgent++;
        else if (otherPriority == 3) Stats.ForLoad++;
        if (otherPriority <= 3 && c.SaidUntil < w.Tick && R.Chance(0.25f)) c.Say(w, Persona.Say(c, "먼저 가요"));
    }

    /// <summary>일하는 사람이 통로를 막고 있다: 크게 돌지 않는 다른 길이 있으면 그리로.</summary>
    private bool Reroute(CrewMember c, List<Cell> path, Cell blocked, CrewMember worker)
    {
        var w = _w;
        var goal = c.Destination ?? path[^1];
        int left = path.Count - c.PathIndex;
        var alt = w.Paths.FindAvoiding(c.Cell, goal, c.PathProfile, blocked);
        if (alt == null || alt.Contains(blocked) || alt.Count > left + 14) return false;
        c.Path = alt;
        c.PathIndex = 0;
        c.Gait.Blocked = 0;
        Stats.Reroutes++;
        if (worker.Room is Room room && (!_roomMarked.TryGetValue(room.Id, out var at) || w.Tick - at > SimTime.Hours(6)))
        {
            _roomMarked[room.Id] = w.Tick;
            MarkLog.Add(room.Marks, w.Tick, $"{Ko.IGa(worker.Name)} 통로에서 일해 {Ko.IGa(c.Name)} 돌아갔다");
        }
        return true;
    }

    /// <summary>뛰어 지나가는 소리에 선잠이 깬다 (잠꾸러기는 잘 안 깬다).</summary>
    private void Disturb(CrewMember runner, Room room)
    {
        var w = _w;
        foreach (var s in w.Crew)
        {
            if (s.Room != room || s.Pose != Pose.Sleeping || s.Down || s.Dead) continue;
            if ((s.Position - runner.Position).LengthSquared() > 16f) continue;
            float p = Life.Has(s, Habit.HeavySleeper) ? 0.01f : s.Needs.Rest > 0.7f ? 0.08f : 0.03f;
            if (!R.Chance(p)) continue;
            Stats.Woke++;
            s.Needs.Rest = MathF.Max(0f, s.Needs.Rest - 0.02f);
            s.Gait.StartleAt = runner.Position; s.Gait.StartleWhy = $"{Ko.IGa(runner.Name)} 뛰어 지나갔다"; s.Gait.StartleTick = w.Tick;
            s.Jolt(w);
        }
    }

    // ───────────────────────────── 큰 소리 ─────────────────────────────

    /// <summary>폭발 · 파열 · 충돌 같은 큰 소리: 가까운 사람은 움찔하며 그쪽을 보고, 자던 사람은 깬다.</summary>
    public void Bang(Room? room, Vector2 at, float loud, string why)
    {
        var w = _w;
        float radius = 6f + 12f * Math.Clamp(loud, 0f, 1f);
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Down || c.Outside || c.Room == null) continue;
            float d = (c.Position - at).Length();
            if (d > radius && c.Room != room) continue;
            var gt = c.Gait;
            bool close = d < radius * 0.5f || c.Room == room;
            gt.StartleAt = at;
            gt.StartleWhy = why;
            gt.StartleTick = w.Tick;
            Stats.Startles++;
            bool fearful = c.Fears.Contains(Fear.Noise) || c.Fears.Contains(Fear.Machines) && why.Contains("터") || c.IsChild;
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + (close ? 0.04f : 0.015f) * loud * (fearful ? 2.5f : 1f));
            if (c.Pose == Pose.Sleeping)
            {
                if (loud >= 0.4f || close) { Stats.Woke++; c.Jolt(w); }
                continue;
            }
            gt.StartleUntil = w.Tick + (close ? 2 : 1) + (fearful ? 1 : 0);
            Locomotion.Face(c, at);
            // 가까이서 났고 하던 일이 급하지 않으면 무슨 일인지 다시 생각한다 (멀리서 난 소리엔 움찔만)
            if (close && c.Job?.Urgent != true) c.NextThinkTick = Math.Min(c.NextThinkTick, w.Tick + 3);
            if (close && loud >= 0.6f) MarkLog.Add(c.Memory.Marks, w.Tick, $"가까이서 큰 소리 — {why}");
        }
    }

    // ───────────────────────────── 내려놓은 짐 ─────────────────────────────

    /// <summary>경보: 보관함이 멀면 들고 있던 짐을 그 자리에 내려놓는다.</summary>
    public bool SetDown(CrewMember c, string why)
    {
        var w = _w;
        if (c.Carrying is not ItemStack s || c.Room == null || c.Outside || !CanSetDown(s.Kind)) return false;
        Stashes.Add(new Stash { Id = ++_stashId, Cell = c.Cell, Stack = s, Owner = c.Id, Tick = w.Tick, Why = why });
        c.Carrying = null;
        Stats.SetDowns++;
        w.Log.Add(w.Tick, LogKind.Life, $"{ItemKinds.Name(s.Kind)} {s.Count}개를 {c.Room.Name} 바닥에 내려놓고 간다 ({why})", c.Id);
        return true;
    }

    /// <summary>경보에 내려놓고 가도 되는 짐: 먹을 것 · 원료 (공구·수리재·부품은 그 일에 쓸 수 있어 들고 간다).</summary>
    public static bool CanSetDown(ItemKind k) => ItemKinds.IsFood(k) || k is ItemKind.MetalOre or ItemKind.Silicate or ItemKind.Carbon or ItemKind.Ice or ItemKind.Rare;

    /// <summary>지금 경보 중이라 짐을 들고 보관함까지 돌아갈 틈이 없나.</summary>
    public bool Hurry(World w) => Crisis.Level(w) >= CrisisLevel.Emergency;

    public Stash? StashFor(CrewMember c, DistanceField dist)
    {
        var w = _w;
        Stash? best = null;
        int bestD = int.MaxValue;
        foreach (var s in Stashes)
        {
            // 내 짐은 곧장, 남의 짐은 네 시간 넘게 그대로면
            if (s.Owner != c.Id && w.Tick - s.Tick < SimTime.Hours(4)) continue;
            if (w.Ship.RoomAt(s.Cell) is not Room r || r.Detached || !w.Ship.IsWalkable(s.Cell)) continue;
            int d = dist.Get(s.Cell);
            if (d < 0 || d >= bestD) continue;
            best = s; bestD = d;
        }
        return best;
    }

    internal bool PickUp(CrewMember c, Stash s)
    {
        var w = _w;
        if (!Stashes.Contains(s) || c.Carrying != null) return false;
        Stashes.Remove(s);
        c.Carrying = s.Stack;
        Stats.PickedUp++;
        if (s.Owner != c.Id && s.Owner >= 0 && s.Owner < w.Crew.Count && w.Crew[s.Owner] is { Dead: false } owner)
            MarkLog.Add(c.Memory.Marks, w.Tick, $"{Ko.IGa(owner.Name)} 두고 간 {ItemKinds.Name(s.Stack.Kind)}을(를) 챙겼다");
        return true;
    }
}

/// <summary>경보가 지나가면 내려놓고 간 짐을 챙겨 보관함에 넣는다.</summary>
public sealed class ReclaimActivity : Activity
{
    public override string Id => "reclaim";
    public override string Label => "두고 간 짐 챙기기";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (w.Movement.Stashes.Count == 0 || c.IsChild || w.Movement.Hurry(w)) return (0f, "—");
        if (w.Movement.StashFor(c, dist) is not Stash s) return (0f, "—");
        bool mine = s.Owner == c.Id;
        float score = (mine ? 0.42f : 0.22f) + 0.1f * c.Traits.Diligence;
        if (Bedtime(c, w)) score -= 0.3f;
        return (MathF.Max(0f, score), mine ? $"아까 두고 간 {ItemKinds.Name(s.Stack.Kind)}" : $"바닥에 굴러다니는 {ItemKinds.Name(s.Stack.Kind)}");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (w.Movement.StashFor(c, dist) is not Stash s) return null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(s.Cell));
        toils.Add(new DoToil((cm, world) => world.Movement.PickUp(cm, s)));
        toils.Add(new WaitToil(2, Pose.Standing));
        // 집어 든 짐은 다음 일을 짤 때 보관함에 먼저 넣는다 (모든 계획이 손부터 비운다)
        return new Job(this, "두고 간 짐 챙기기", toils) { LogText = s.Owner == c.Id ? $"두고 간 {ItemKinds.Name(s.Stack.Kind)}을(를) 챙기러 간다" : null, LogKind = LogKind.Life };
    }
}
