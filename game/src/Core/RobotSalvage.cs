using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v22 기계의 상처 (2) — 잔해와 되살리기: 부서진 로봇은 사라지지 않고 그 자리에 잔해로 남는다.
//   ① 회수: 쉬던 운반 · 정비 로봇이 잔해를 끌어 정비실(로봇 정비실 · 작업장)로 옮긴다.
//   ② 해체: 정비 로봇이 뜯어 살아남은 부위는 부품으로(모터 · 센서 · 전자재 · 셀 팩 · 로봇 팔다리 · 금속판), 나머지는 고철(금속 원료)로.
//   ③ 조립: 함대가 처음보다 줄었고 부품이 모이면, 정비 로봇이 부서진 로봇의 빈 충전대에서 새 로봇을 짜 맞춘다 (잔해의 이름을 잇는다).
//   ④ 뜯어 살리기: 급한 로봇(의료 · 구조 · 방재 · 정비)이 부품이 없어 멈춰 있으면 주 컴퓨터가 정한다 —
//      잔해에 쓸 만한 부위가 있으면 거기서 · 없으면 덜 급한 로봇의 부위를 떼어 살린다 (떼인 로봇은 멈추고 · 왜 그랬는지 남는다).

public sealed partial class Robot
{
    /// <summary>v22 잔해를 해체해 부품으로 돌렸다 (바닥에 더는 없다).</summary>
    public bool Salvaged { get; internal set; }
    /// <summary>v22 잔해가 정비실에 옮겨져 해체를 기다린다.</summary>
    public bool AtShop { get; internal set; }
    /// <summary>v22 누가 지금 이 잔해를 다루나 (끌거나 해체하는 로봇 Id · -1 없음).</summary>
    public int SalvageBy { get; internal set; } = -1;
    /// <summary>v22 잔해에서 나온 부품으로 다시 짜 맞춘 로봇 (몇 번째 몸인가 · 0이면 처음 몸).</summary>
    public int Rebuilt { get; internal set; }
    /// <summary>v22 이 몸이 이어받은 잔해의 이름 (""면 처음부터 실은 로봇).</summary>
    public string FromWreck { get; internal set; } = "";
    /// <summary>v22 그 충전대에 새 몸을 짜 맞췄다 (잔해의 자리를 이었다).</summary>
    public bool Succeeded { get; internal set; }
}

public sealed class SalvageStats
{
    public int Hauled, Dismantled, PartsBack, Scrap, Built, FromWrecks, FromRobots, BuildShort;
    public string Line() => $"잔해 회수 {Hauled} · 해체 {Dismantled} (부품 {PartsBack} · 고철 {Scrap}) · 조립 {Built} (부품 모자람 {BuildShort}) · 뜯어 살리기 {FromWrecks + FromRobots} (잔해 {FromWrecks} · 다른 로봇 {FromRobots})";
}

public sealed partial class RobotSystem
{
    public SalvageStats Salvage { get; } = new();
    private long _salvageNext;
    private int _fleetTarget = -1;
    /// <summary>v22 떼어 둔 로봇 팔다리가 누구의 것이었나 (꺼내 쓴 순서대로 — 의수 · 의족에 이름이 남는다).</summary>
    public List<string> LimbSources { get; } = new();

    /// <summary>떼어 둔 로봇 팔다리 하나의 주인 이름 (모르면 "잔해").</summary>
    public string TakeLimbSource()
    {
        if (LimbSources.Count == 0) return "잔해";
        var s = LimbSources[0];
        LimbSources.RemoveAt(0);
        return s;
    }

    /// <summary>짜 맞춘 새 몸 — 로봇 목록을 도는 중에 넣지 않고 다음 분 첫머리에 넣는다.</summary>
    private readonly List<Robot> _newBots = new();

    /// <summary>바닥에 남은 잔해 (떨어져 나간 방 · 떠내려간 조각에 실린 것은 아니다).</summary>
    public static bool IsWreck(Robot r) => r.Wrecked && !r.Salvaged && r.Aboard == null && r.Room is { Detached: false };

    /// <summary>움직이는 로봇 수 (잔해 · 잃은 것 빼고).</summary>
    public int Alive => Robots.Count(r => !r.Wrecked && r.State != RobotState.Lost) + _newBots.Count;

    /// <summary>잔해를 끌어다 해체하는 방 (로봇 정비실 → 작업장 → 창고).</summary>
    private Room? Shop() =>
        _world.Ship.RoomsOf(RoomType.RobotBay).Concat(_world.Ship.RoomsOf(RoomType.Workshop)).Concat(_world.Ship.RoomsOf(RoomType.Storage))
            .FirstOrDefault(r => !r.Detached && !r.Abandoned && !r.OffLimits && !r.Leaking);

    /// <summary>5분마다: 잔해 회수 · 해체 · 뜯어 살리기 · 조립.</summary>
    private void SalvageTick()
    {
        var w = _world;
        if (_newBots.Count > 0) { Robots.AddRange(_newBots); _newBots.Clear(); }
        if (w.Tick < _salvageNext) return;
        _salvageNext = w.Tick + SimTime.Minutes(5);
        if (_fleetTarget < 0) _fleetTarget = Alive;
        foreach (var r in Robots) if (r.SalvageBy >= 0 && (Robots.FirstOrDefault(x => x.Id == r.SalvageBy) is not Robot b || b.Steps == null || b.State != RobotState.Active)) r.SalvageBy = -1; // 맡은 로봇이 일을 놓았다
        var shop = Shop();
        foreach (var wreck in Robots)
        {
            if (!IsWreck(wreck) || wreck.SalvageBy >= 0) continue;
            if (!wreck.AtShop) { if (shop != null) TryHaulWreck(wreck, shop); }
            else TryDismantle(wreck);
        }
        TryCannibalize();
        TryBuild();
    }

    // ─────────────────────────────── ① 회수 ───────────────────────────────

    private void TryHaulWreck(Robot wreck, Room shop)
    {
        var w = _world;
        if (wreck.Room == shop) { wreck.AtShop = true; return; }
        Robot? best = null; float bd = float.MaxValue;
        foreach (var r in Robots)
        {
            if (!Free(r) || !(Carrier(r.Kind) || Fixer(r.Kind) || r.Kind == RobotKind.Lifter || RobotsV15.Base(r.Kind) is RobotKind.Hauler or RobotKind.Maintainer)) continue;
            float d = Far(r.Position, wreck.Position);
            if (d < bd) { bd = d; best = r; }
        }
        if (best == null) return;
        var dist = w.Paths.Flood(best.Cell, Profile);
        Cell? near = null; int nc = int.MaxValue;
        foreach (var d in Cell.Dirs8)
        {
            var s = wreck.Cell + d;
            if (!w.Ship.IsWalkable(s)) continue;
            int dd = dist.Get(s);
            if (dd < 0 || dd >= nc) continue;
            nc = dd; near = s;
        }
        var drop = shop.Cells.Where(c => w.Ship.IsOpenFloor(c) && dist.Reachable(c)).OrderBy(c => c.Y).ThenBy(c => c.X).Cast<Cell?>().FirstOrDefault();
        if (near is not Cell at || drop is not Cell spot) return;
        var wr = wreck;
        var steps = new List<RobotStep>
        {
            new RGoto(at),
            new RDo((rb, world) =>
            {
                if (!IsWreck(wr) || wr.TowBot != null || rb.Hauling != null) return false;
                wr.TowBot = rb; rb.Hauling = wr;
                SetState(wr, RobotState.Towed);
                wr.Doing = $"{Ko.IGa(rb.Name)} 잔해를 끌고 간다";
                return true;
            }),
            new RGoto(spot),
            new RDo((rb, world) =>
            {
                if (wr.TowBot == rb) { wr.TowBot = null; rb.Hauling = null; }
                SetState(wr, RobotState.Lost);
                wr.Position = spot.Center; wr.PreviousPosition = wr.Position; wr.Room = world.Ship.RoomAt(spot);
                wr.AtShop = true;
                wr.SalvageBy = -1;
                wr.Doing = $"{wr.Room?.Name ?? "정비실"}에서 해체를 기다린다";
                Salvage.Hauled++;
                MarkLog.Add(wr.Marks, world.Tick, $"{Ko.IGa(rb.Name)} 잔해를 {Ko.EuRo(wr.Room?.Name ?? "정비실")} 끌고 왔다");
                world.Log.Add(world.Tick, LogKind.Work, $"{Ko.IGa(rb.Name)} {wr.Name}의 잔해를 {Ko.EuRo(wr.Room?.Name ?? "정비실")} 끌고 왔다");
                return true;
            }),
        };
        if (best.State == RobotState.Active) Release(best);
        wreck.SalvageBy = best.Id;
        Begin(best, steps, $"{wreck.Name}의 잔해 끌어 오기");
        FleetSystem.Stage(best, steps, null);
        best.Mind.Say($"{Ko.IGa(wreck.Name)} 부서진 자리 — 잔해를 정비실로", w.Tick);
    }

    // ─────────────────────────────── ② 해체 ───────────────────────────────

    private void TryDismantle(Robot wreck)
    {
        var w = _world;
        var best = Robots.Where(r => Free(r) && Fixer(r.Kind) && !RobotBody.Gripless(r)).OrderBy(r => Far(r.Position, wreck.Position)).FirstOrDefault();
        if (best == null) return;
        var dist = w.Paths.Flood(best.Cell, Profile);
        Cell? near = null; int nc = int.MaxValue;
        foreach (var d in Cell.Dirs8)
        {
            var s = wreck.Cell + d;
            if (!w.Ship.IsWalkable(s)) continue;
            int dd = dist.Get(s);
            if (dd < 0 || dd >= nc) continue;
            nc = dd; near = s;
        }
        if (near is not Cell at) return;
        var wr = wreck;
        var steps = new List<RobotStep>
        {
            new RGoto(at),
            new RWork(1.5f, null, wr.Position),
            new RDo((rb, world) => { Dismantle(rb, wr); return true; }),
        };
        if (best.State == RobotState.Active) Release(best);
        wreck.SalvageBy = best.Id;
        Begin(best, steps, $"{wreck.Name}의 잔해 해체");
        FleetSystem.Stage(best, steps, null);
        best.Mind.Say($"{wreck.Name} — 쓸 만한 부위를 떼어 둔다", w.Tick);
    }

    /// <summary>잔해 한 부위가 돌려주는 것 (살아남았으면 부품 · 아니면 고철).</summary>
    internal static (ItemKind kind, int count) PartYield(Robot r, BotPart p)
    {
        float hp = r.Parts[(int)p];
        if (hp < 0.25f) return (ItemKind.MetalOre, p == BotPart.Frame ? 2 : 1);
        return p switch
        {
            BotPart.Frame => (ItemKind.Plate, hp >= 0.75f ? 2 : 1),
            BotPart.Drive => (ItemKind.Motor, 1),
            BotPart.ArmL or BotPart.ArmR => (ItemKind.RobotLimb, 1),
            BotPart.Sensor => (ItemKind.Sensor, 1),
            BotPart.Brain => (ItemKind.Electronics, 1),
            BotPart.Power => (hp >= 0.6f ? ItemKind.PowerController : ItemKind.CellPack, 1),
            _ => (r.Kind is RobotKind.Safety or RobotKind.Firefighter or RobotKind.Sealer ? ItemKind.Nozzle : r.Kind == RobotKind.Plumber ? ItemKind.Hose : r.Kind == RobotKind.Cleaner ? ItemKind.Brush : ItemKind.Gear, 1),
        };
    }

    private void Dismantle(Robot by, Robot wr)
    {
        var w = _world;
        if (!IsWreck(wr)) return;
        var got = new List<string>();
        int parts = 0, scrap = 0;
        foreach (var p in RobotBody.All)
        {
            if (!RobotBody.Has(wr.Kind, p)) continue;
            var (k, n) = PartYield(wr, p);
            int put = Stash(k, n, wr.Position);
            if (put <= 0) continue;
            if (k == ItemKind.MetalOre) scrap += put; else { parts += put; got.Add($"{ItemKinds.Name(k)}{(put > 1 ? $" {put}" : "")}"); }
            if (k == ItemKind.RobotLimb) for (int i = 0; i < put; i++) LimbSources.Add(wr.Name); // 이 팔다리는 누구의 것이었나
            wr.Parts[(int)p] = 0f;
        }
        wr.Salvaged = true;
        wr.SalvageBy = -1;
        wr.Doing = "해체해 부품으로 돌렸다";
        Salvage.Dismantled++;
        Salvage.PartsBack += parts;
        Salvage.Scrap += scrap;
        string list = got.Count > 0 ? string.Join(" · ", got) : "쓸 만한 부위가 없었다";
        MarkLog.Add(wr.Marks, w.Tick, $"{Ko.IGa(by.Name)} 해체했다 — {list}");
        w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(by.Name)} {wr.Name}의 잔해를 해체했다 — {list}" + (scrap > 0 ? $" · 고철 {scrap}" : ""));
        w.History.Add(w, HistoryKind.Damage, $"{wr.Name}의 잔해를 해체해 {list}" + (scrap > 0 ? $" · 고철 {scrap}" : ""), wr.Room, log: false);
    }

    /// <summary>가까운 보관함부터 넣는다 (넣은 개수).</summary>
    private int Stash(ItemKind k, int n, System.Numerics.Vector2 near)
    {
        int left = n;
        foreach (var f in _world.Ship.Containers.Where(f => f.Storage != null && f.Storage.Accepts(k) && f.Storage.Free > 0 && !f.Room.Detached).OrderBy(f => (f.Center - near).LengthSquared()))
        {
            left -= f.Storage!.Add(k, left);
            if (left <= 0) break;
        }
        return n - left;
    }

    // ─────────────────────────────── ③ 조립 ───────────────────────────────

    /// <summary>새 몸 한 대에 드는 것 (팔 달린 종류는 로봇 팔다리 둘 — 없으면 모터로).</summary>
    internal static (ItemKind kind, int count)[] BuildParts(RobotKind k) => RobotBody.HasArms(k)
        ? new[] { (ItemKind.Plate, 3), (ItemKind.Motor, 1), (ItemKind.RobotLimb, 2), (ItemKind.Sensor, 1), (ItemKind.Electronics, 2), (ItemKind.CellPack, 1), (ItemKind.Cable, 2) }
        : new[] { (ItemKind.Plate, 3), (ItemKind.Motor, 1), (ItemKind.Sensor, 1), (ItemKind.Electronics, 2), (ItemKind.CellPack, 1), (ItemKind.Cable, 2) };

    private bool HasAll((ItemKind kind, int count)[] need, out string missing)
    {
        var sh = _world.Ship;
        var miss = need.Where(x => sh.CountStored(x.kind) < x.count && !(x.kind == ItemKind.RobotLimb && sh.CountStored(ItemKind.Motor) >= x.count + 1)).Select(x => ItemKinds.Name(x.kind)).ToList();
        missing = string.Join(" · ", miss);
        return miss.Count == 0;
    }

    private void TryBuild()
    {
        var w = _world;
        if (Alive >= _fleetTarget) return;
        if (Robots.Any(r => r.Steps != null && r.Doing.StartsWith("새 몸 조립", StringComparison.Ordinal))) return; // 한 번에 한 대
        // 잔해를 해체해 비운 충전대 (가장 오래된 것부터 · 이미 새 몸이 들어선 자리는 빼고)
        var seat = Robots.Where(r => r.Salvaged && !r.Succeeded && !r.Dock.Room.Detached && r.Dock.Machine is not { Faults.Count: > 0 }).OrderBy(r => r.Id).FirstOrDefault();
        if (seat == null) return;
        var need = BuildParts(seat.Kind);
        if (!HasAll(need, out var missing))
        {
            if (w.Tick % SimTime.Hours(6) < SimTime.Minutes(5)) { Salvage.BuildShort++; w.Automation.Reason("bot-build", $"{seat.Name}의 자리를 새 몸으로 채우려는데 {Ko.IGa(missing)} 모자라다", SimTime.Hours(6)); }
            return;
        }
        var best = Robots.Where(r => Free(r) && Fixer(r.Kind) && !RobotBody.Gripless(r)).OrderBy(r => Far(r.Position, seat.Dock.Center)).FirstOrDefault();
        if (best == null) return;
        var spot = seat.Dock.UseSpots.Where(w.Ship.IsWalkable).Cast<Cell?>().FirstOrDefault();
        if (spot is not Cell at) return;
        var old = seat;
        var steps = new List<RobotStep>
        {
            new RGoto(at),
            new RWork(6f, null, old.Dock.Center),
            new RDo((rb, world) => Build(rb, old)),
        };
        if (best.State == RobotState.Active) Release(best);
        Begin(best, steps, $"새 몸 조립 — {old.Name}의 자리");
        FleetSystem.Stage(best, steps, null);
        best.Mind.Say($"{old.Name}의 충전대에 새 몸을 짜 맞춘다", w.Tick);
        w.Automation.Reason("bot-build", $"함대가 {_fleetTarget}대에서 {Alive}대로 줄었다 — 부품이 모여 {old.Name}의 자리에 새 몸을 짜 맞춘다", SimTime.Hours(6));
    }

    private bool Build(Robot by, Robot old)
    {
        var w = _world;
        if (old.Succeeded) return true;
        var need = BuildParts(old.Kind);
        if (!HasAll(need, out var missing)) { Salvage.BuildShort++; w.Log.Add(w.Tick, LogKind.Warning, $"{by.Name}: 새 몸 조립을 미뤘다 — {Ko.IGa(missing)} 모자라다"); return false; }
        foreach (var (k, n) in need)
        {
            int left = n;
            if (k == ItemKind.RobotLimb) while (left > 0 && w.Ship.CountStored(ItemKind.RobotLimb) > 0 && ItemsV15.Use(w, ItemKind.RobotLimb)) { left--; TakeLimbSource(); }
            if (k == ItemKind.RobotLimb && left > 0) { for (int i = 0; i < left + 1; i++) ItemsV15.Use(w, ItemKind.Motor); left = 0; } // 떼어 둔 팔다리가 없으면 모터로 새로
            for (; left > 0; left--) ItemsV15.Use(w, k);
        }
        int life = old.Rebuilt + 1;
        string baseName = RobotsV15.Bot(old.Kind)?.Name ?? KindName(old.Kind);
        int n2 = Robots.Count(x => x.Kind == old.Kind) + _newBots.Count(x => x.Kind == old.Kind) + 1;
        var r = new Robot
        {
            Id = Robots.Count + _newBots.Count, Kind = old.Kind, Dock = old.Dock, Slot = old.Slot,
            Name = $"{baseName} {n2}", Condition = 0.95f,
        };
        r.Rebuilt = life;
        r.FromWreck = old.Name;
        r.Position = r.DockPosition;
        r.PreviousPosition = r.Position;
        r.Room = old.Dock.Room;
        r.NextPatrol = w.Tick + SimTime.Minutes(30);
        for (int i = 0; i < RobotBody.Count; i++) r.Parts[i] = 0.95f;
        _newBots.Add(r);
        old.Succeeded = true;
        Salvage.Built++;
        MarkLog.Add(r.Marks, w.Tick, $"{Ko.IGa(by.Name)} {old.Name}의 잔해에서 나온 부품으로 짜 맞췄다 ({life}번째 몸)");
        w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(by.Name)} 새 몸을 짜 맞췄다 — {r.Name} ({old.Name}의 자리 · {life}번째 몸)");
        w.History.Add(w, HistoryKind.Damage, $"{old.Name}의 자리에 새 몸 {Ko.IGa(r.Name)} 들어섰다 — 잔해에서 나온 부품으로", r.Room, log: false);
        return true;
    }

    // ─────────────────────────────── ④ 뜯어 살리기 ───────────────────────────────

    /// <summary>얼마나 급한 로봇인가 (의료 · 구조 · 방재가 가장 · 그다음 정비).</summary>
    internal static float Worth(Robot r) =>
        RobotsV21.Medical(r.Kind) || r.Kind == RobotKind.Rescuer ? 3f
        : RobotsV15.Fights(r.Kind) || RobotsV15.Base(r.Kind) == RobotKind.Safety ? 2.8f
        : Fixer(r.Kind) ? 2.5f
        : 1f;

    private void TryCannibalize()
    {
        var w = _world;
        foreach (var t in Robots)
        {
            if (t.Wrecked || t.Fault is not RobotFault f || t.State is not (RobotState.Stalled or RobotState.Docked) || w.Tick - t.StateSince < SimTime.Minutes(30)) continue;
            if (t.FaultPart < 0 || Worth(t) < 2.5f) continue;
            var need = FaultParts(f);
            if (need.Length == 0 || need.All(x => w.Ship.CountStored(x.kind) >= x.count)) continue; // 부품이 있으면 고치러 온다
            var p = (BotPart)t.FaultPart;
            // 잔해에서 먼저
            var wreck = Robots.Where(x => IsWreck(x) && x.SalvageBy < 0 && x.Parts[(int)p] >= 0.25f && RobotBody.Has(x.Kind, p)).OrderBy(x => Far(x.Position, t.Position)).FirstOrDefault();
            Robot? donor = wreck;
            // 쓸 만한 부위가 남은 잔해가 있으면 (끌어 가는 중 · 해체하는 중이어도) 거기서 나올 부품을 기다린다 — 멀쩡한 로봇은 잔해가 하나도 없을 때만
            if (donor == null && Robots.Any(x => IsWreck(x) && RobotBody.Has(x.Kind, p) && x.Parts[(int)p] >= 0.25f)) continue;
            // 임시로 되살릴 재료라도 있으면 그게 먼저 (멀쩡한 로봇을 세우는 건 마지막)
            var mk = MakeshiftParts(f);
            if (donor == null && mk.Length > 0 && mk.All(x => w.Ship.CountStored(x.kind) >= x.count)) continue;
            if (donor == null)
                donor = Robots.Where(x => x != t && !x.Wrecked && x.State != RobotState.Lost && RobotBody.Has(x.Kind, p) && x.Parts[(int)p] >= 0.5f && Worth(x) < Worth(t)
                                          && (x.State is RobotState.Docked or RobotState.Stalled) && x.Hauling == null && x.TowBot == null && x.Steps == null)
                    .OrderBy(x => x.Fault != null ? 0 : 1).ThenBy(x => Worth(x)).ThenBy(x => Far(x.Position, t.Position)).FirstOrDefault();
            if (donor == null) continue;
            var best = Robots.Where(r => r != t && r != donor && Free(r) && Fixer(r.Kind) && !RobotBody.Gripless(r)).OrderBy(r => Far(r.Position, donor.Position)).FirstOrDefault();
            if (best == null) continue;
            var dist = w.Paths.Flood(best.Cell, Profile);
            Cell? near = null; int nc = int.MaxValue;
            foreach (var d in Cell.Dirs8)
            {
                var s = donor.Cell + d;
                if (!w.Ship.IsWalkable(s)) continue;
                int dd = dist.Get(s);
                if (dd < 0 || dd >= nc) continue;
                nc = dd; near = s;
            }
            if (near is not Cell at) continue;
            var dn = donor; var tg = t; var part = p;
            bool fromWreck = donor.Wrecked;
            var steps = new List<RobotStep>
            {
                new RGoto(at),
                new RWork(0.4f, null, dn.Position),
                new RDo((rb, world) => { TakePart(rb, dn, tg, part, need); return true; }),
            };
            if (best.State == RobotState.Active) Release(best);
            if (fromWreck) donor.SalvageBy = best.Id;
            Begin(best, steps, $"{dn.Name}의 {RobotBody.Name(dn.Kind, p)} 떼어 오기 — {tg.Name}를 살리려고");
            FleetSystem.Stage(best, steps, null);
            string why = fromWreck
                ? $"{tg.Name} {FaultName(f)} — {string.Join(" · ", need.Select(x => ItemKinds.Name(x.kind)))}이 배에 없다 · {dn.Name}의 잔해에 쓸 만한 {RobotBody.Name(dn.Kind, p)}가 남았다"
                : $"{tg.Name} {FaultName(f)} — 부품이 배에 없다 · 덜 급한 {Ko.EulReul(dn.Name)} 세워 {RobotBody.Name(dn.Kind, p)}를 떼어 {Ko.EulReul(tg.Name)} 살린다";
            w.Automation.Reason($"bot-cannibal:{tg.Id}", why, SimTime.Hours(2));
            if (!fromWreck) w.Log.Add(w.Tick, LogKind.Warning, $"주 컴퓨터: {why}");
            return; // 한 번에 하나
        }
    }

    private void TakePart(Robot by, Robot donor, Robot target, BotPart p, (ItemKind kind, int count)[] need)
    {
        var w = _world;
        bool fromWreck = donor.Wrecked;
        if (fromWreck) donor.SalvageBy = -1;
        if (donor.Parts[(int)p] < 0.25f) return;
        donor.Parts[(int)p] = 0f;
        foreach (var (k, n) in need) Stash(k, n, by.Position);
        string part = RobotBody.Name(donor.Kind, p);
        if (fromWreck)
        {
            Salvage.FromWrecks++;
            MarkLog.Add(donor.Marks, w.Tick, $"{Ko.IGa(by.Name)} {Ko.EulReul(part)} 떼어 갔다 — {target.Name}에게");
            w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(by.Name)} {donor.Name}의 잔해에서 {Ko.EulReul(part)} 떼어 왔다 — {Ko.EulReul(target.Name)} 살리려고");
            return;
        }
        Salvage.FromRobots++;
        MarkLog.Add(donor.Marks, w.Tick, $"{Ko.EulReul(part)} 떼였다 — {Ko.EulReul(target.Name)} 살리려고 (주 컴퓨터)");
        MarkLog.Add(target.Marks, w.Tick, $"{donor.Name}의 {Ko.EulReul(part)} 받았다");
        w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.IGa(by.Name)} {donor.Name}의 {Ko.EulReul(part)} 떼어 왔다 — {Ko.IGa(donor.Name)} 멈춰 섰다 · {Ko.EulReul(target.Name)} 살린다");
        w.History.Add(w, HistoryKind.Damage, $"주 컴퓨터가 {Ko.EulReul(target.Name)} 살리려고 {donor.Name}의 {Ko.EulReul(part)} 떼게 했다", donor.Room, log: false);
        if (donor.Fault == null && (donor.State is RobotState.Docked or RobotState.Active))
        {
            donor.FaultPart = (int)p;
            _bodyBreaking = true;
            try { Break(donor, RobotBody.FaultFor(p)); } finally { _bodyBreaking = false; }
        }
    }
}
