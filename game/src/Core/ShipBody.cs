using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v16.3 배 본체: 한 칸 3층(바닥재 / 바닥 아래 배선 · 배관과 점검 뚜껑 / 천장 조명 · 덕트 · 스프링클러) · 칸 상태 · 닳는 바닥 · 벽 층 · 문.
// 기본값은 방 종류로 정한다 — 설계도 ASCII를 바꾸지 않고 모든 배 · 생성 배에서 같은 규칙.
// 기록 = 행동: 뚜껑은 배관 · 배선 수리하는 사람이 실제로 열고(닫는 걸 잊기도), 미끄러짐은 실제로 그 칸을 밟을 때만 굴린다.

[Flags]
public enum UnderFlags : byte { None = 0, Wiring = 1, Pipe = 2, HatchSpot = 4 }

[Flags]
public enum CeilingFlags : byte { None = 0, Light = 1, Duct = 2, Sprinkler = 4 }

/// <summary>칸 상태 (값 + 원인 + 시각).</summary>
public enum CellMark : byte { Wet, Oil, Frost, Glass, Soot, Tape }

public sealed class CellState
{
    public const int Kinds = 6;
    public readonly float[] V = new float[Kinds];
    public readonly string?[] Cause = new string?[Kinds];
    public readonly long[] Since = new long[Kinds];
    public float this[CellMark m] => V[(int)m];
    public bool Empty { get { foreach (var v in V) if (v > 0.01f) return false; return true; } }
}

/// <summary>열린 점검 뚜껑 (바닥 아래 배선 · 배관 홈).</summary>
public sealed class OpenHatch
{
    public Cell Cell { get; init; }
    public int By { get; set; } = -1;
    public int Order { get; set; } = -1;
    public long Since { get; init; }
    public string Why { get; init; } = "";
    /// <summary>일을 마치고 닫는 걸 잊었다 (지나가던 사람이 닫거나, 손보기로 닫는다).</summary>
    public bool Forgotten { get; set; }
}

/// <summary>벽 한 칸의 층: 외판 / 단열재 / 배선 / 안쪽 패널 (얇은 칸막이는 패널 한 겹).</summary>
public sealed class WallBody
{
    public Cell Cell { get; init; }
    public bool Hull { get; set; }
    /// <summary>얇은 칸막이 — 소리가 샌다.</summary>
    public bool Thin { get; set; }
    public bool Wiring { get; set; }
    /// <summary>관측창 (선체 벽) · 태양 폭풍 덮개.</summary>
    public bool Window { get; set; }
    public bool Shutter { get; set; }
    /// <summary>안쪽 방 (칸막이면 한쪽).</summary>
    public int Room { get; set; } = -1;
    /// <summary>단열재 0~1 (상하면 결로 · 차가운 벽).</summary>
    public float Insulation { get; set; } = 1f;
    public bool PanelOff { get; set; }
    public int PanelBy { get; set; } = -1;
    public int PanelOrder { get; set; } = -1;
    public long PanelSince { get; set; }
    public bool PanelForgot { get; set; }
    public int ClaimedBy { get; set; } = -1;

    public Material Inner => Thin ? Material.Partition : Material.Panel;

    /// <summary>소리를 막는 정도 (층을 겹친 만큼, 패널을 떼면 덜 막는다).</summary>
    public float SoundBlock
    {
        get
        {
            float b = Materials.Of(Inner).SoundBlock * (PanelOff ? 0.4f : 1f);
            if (!Thin) b += 0.25f * Insulation * Materials.Of(Material.Insulation).SoundBlock;
            if (Hull) b = MathF.Max(b, Materials.Of(Material.HullPlate).SoundBlock);
            return MathF.Min(1f, b);
        }
    }
}

public enum MountKind : byte { Extinguisher, OxygenMasks, Flashlight, Board }

/// <summary>벽 장착물: 자리를 기억하고, 꺼내면 빈 걸이가 남고, 누군가 채운다.</summary>
public sealed class WallMount
{
    public int Id { get; init; }
    public MountKind Kind { get; init; }
    public Cell Wall { get; init; }
    public Cell Spot { get; init; }
    public int Room { get; init; }
    public bool Present { get; set; } = true;
    public int TakenBy { get; set; } = -1;
    public long TakenAt { get; set; } = -1;
    public int ClaimedBy { get; set; } = -1;

    public static string Name(MountKind k) => k switch
    {
        MountKind.Extinguisher => "소화기", MountKind.OxygenMasks => "산소 마스크함", MountKind.Flashlight => "손전등", _ => "게시판",
    };
    public static ItemKind? Item(MountKind k) => k switch
    {
        MountKind.Extinguisher => ItemKind.Extinguisher, MountKind.OxygenMasks => ItemKind.Filter, MountKind.Flashlight => ItemKind.CellPack, _ => null,
    };
}

/// <summary>엿들은 기억.</summary>
public sealed class HeardMemory
{
    public int Listener { get; init; }
    public int A { get; init; }
    public int B { get; init; }
    public int FromRoom { get; init; }
    public long Tick { get; init; }
    public string What { get; init; } = "";
}

public sealed class BodyStats
{
    public int Falls, Slips, FootIn, Cuts, HatchOpens, HatchForgot, HatchClosedBy, PanelOffs, PanelForgot, PanelRefit,
        Knocks, NoAnswer, Answered, DropIns, Waits, Calls, LetIn, RemoteOk, GaveUp, EmergencyPass, Cranks, SensorPresses, FalseReadings,
        Overheard, Whistles, MicroLeaks, Complaints, Condensation, FireReleases, Bents, SealFails, Gaskets, Fixed, Cleaned, Taped,
        MountUses, MountRefills, ShutterCloses, Detours, LockedSleeps, HeldOpens;
    public float Drained;

    public override string ToString() =>
        $"넘어짐 {Falls}(발 빠짐 {FootIn} · 베임 {Cuts}) · 뚜껑 {HatchOpens}(잊음 {HatchForgot} · 지나가다 닫음 {HatchClosedBy}) · 패널 {PanelOffs}(잊음 {PanelForgot} · 다시 붙임 {PanelRefit}) · " +
        $"노크 {Knocks}(대답 {Answered} · 없음 {NoAnswer}) · 들름 {DropIns} · 기다림 {Waits} · 부름 {Calls} · 열어 줌 {LetIn} · 원격 {RemoteOk} · 포기 {GaveUp} · " +
        $"손으로 {Cranks} · 센서 {SensorPresses} · 오판 {FalseReadings} · 엿들음 {Overheard} · 휘파람 {Whistles} · 문 좀 닫아 {Complaints} · 결로 {Condensation} · " +
        $"화재 해제 {FireReleases} · 기밀 실패 {SealFails} · 손보기 {Fixed} · 청소 {Cleaned} · 테이프 {Taped} · 장착물 {MountUses}/{MountRefills}";
}

public sealed partial class BodySystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7919 + 163));
    public BodyStats Stats { get; } = new();

    // ── 칸 (배열 — 칸 번호 = Grid.Index) ──
    public Material[] Floor { get; private set; } = Array.Empty<Material>();
    public UnderFlags[] Under { get; private set; } = Array.Empty<UnderFlags>();
    public CeilingFlags[] Ceiling { get; private set; } = Array.Empty<CeilingFlags>();
    /// <summary>비상 유도선 (바닥에 붙은 야광 띠).</summary>
    public bool[] Guide { get; private set; } = Array.Empty<bool>();
    public float[] Wear { get; private set; } = Array.Empty<float>();
    public int[] Steps { get; private set; } = Array.Empty<int>();
    private bool[] _hatchOpen = Array.Empty<bool>();

    /// <summary>칸 상태 (드문드문 — 정렬된 칸 번호로).</summary>
    public SortedDictionary<int, CellState> Marks { get; } = new();
    public List<OpenHatch> Hatches { get; } = new();
    public List<WallBody> WallList { get; } = new();
    private readonly Dictionary<Cell, WallBody> _walls = new();
    public List<WallMount> Mounts { get; } = new();
    public List<HeardMemory> Heard { get; } = new();

    private readonly List<(int kind, int count)> _roomSig = new();
    private readonly List<(float drain, float absorb)> _roomDrain = new();
    private int _wallCount = -1;
    private long _nextSlow;

    public const float WearPerStep = 0.0006f;
    public const int HatchCost = 60, TapeCost = 45, GlassCost = 18, OilCost = 8;

    public BodySystem(World w)
    {
        _w = w;
        int n = w.Ship.Grid.CellCount;
        Floor = new Material[n];
        Under = new UnderFlags[n];
        Ceiling = new CeilingFlags[n];
        Guide = new bool[n];
        Wear = new float[n];
        Steps = new int[n];
        _hatchOpen = new bool[n];
        SyncStructure();
        InitDoors();
        InitMounts();
    }

    public WallBody? WallAt(Cell c) => _walls.TryGetValue(c, out var b) ? b : null;
    public bool HatchOpenAt(Cell c) => _w.Ship.Grid.InBounds(c) && _hatchOpen[_w.Ship.Grid.Index(c)];
    public CellState? MarksAt(Cell c) => _w.Ship.Grid.InBounds(c) && Marks.TryGetValue(_w.Ship.Grid.Index(c), out var s) ? s : null;
    public float Mark(Cell c, CellMark m) => MarksAt(c) is CellState s ? s.V[(int)m] : 0f;
    public Material FloorAt(Cell c) => _w.Ship.Grid.InBounds(c) ? Floor[_w.Ship.Grid.Index(c)] : Material.None;

    /// <summary>불이 번지는 빠르기 배율: 카펫 · 고무는 잘 타고, 금속 · 타일 · 격자는 덜, 젖은 바닥은 훨씬 덜 (Fire가 읽는다).</summary>
    public float SpreadMul(Cell c)
    {
        var grid = _w.Ship.Grid;
        if (!grid.InBounds(c)) return 1f;
        int i = grid.Index(c);
        if (Floor[i] == Material.None) return 1f;
        float wet = Marks.Count > 0 && Marks.TryGetValue(i, out var s) ? s.V[(int)CellMark.Wet] : 0f;
        return (0.9f + 0.5f * Materials.Of(Floor[i]).Burn) * (1f - 0.6f * wet);
    }

    /// <summary>지금 그 칸의 미끄러움 (재질 × 상태 × 닳음).</summary>
    public float SlipAt(int i)
    {
        Marks.TryGetValue(i, out var s);
        return Materials.SlipNow(Floor[i], s?.V[0] ?? 0f, s?.V[1] ?? 0f, s?.V[2] ?? 0f, Wear[i]);
    }

    // ───────────────────────────── 기본값: 방 종류로 ─────────────────────────────

    private static UnderFlags UnderFor(RoomType k) => k switch
    {
        RoomType.Corridor or RoomType.Engine or RoomType.Reactor or RoomType.LifeSupport or RoomType.Galley or RoomType.Hydroponics or RoomType.WaterPlant
            or RoomType.Cooling or RoomType.PumpRoom or RoomType.Laundry or RoomType.Medbay or RoomType.AlgaeLab or RoomType.ProteinFarm or RoomType.Decon
            or RoomType.HvacRoom or RoomType.Freezer or RoomType.Garden => UnderFlags.Wiring | UnderFlags.Pipe,
        _ => UnderFlags.Wiring,
    };

    private static bool Cabin(RoomType k) => k is RoomType.Quarters or RoomType.PrivateCabins or RoomType.QuietQuarters or RoomType.WaterWallCabin;

    private static bool ViewRoom(RoomType k) => k is RoomType.Observatory or RoomType.Lounge or RoomType.Mess or RoomType.Bridge or RoomType.Garden
        or RoomType.Chapel or RoomType.Meditation or RoomType.Theater;

    /// <summary>방이 바뀌었으면(개조 · 칸막이 · 합치기) 그 방 칸의 기본값을 다시 (닳음 · 상태는 그대로).</summary>
    private void SyncStructure()
    {
        var ship = _w.Ship;
        var grid = ship.Grid;
        bool changed = false;
        for (int r = 0; r < ship.Rooms.Count; r++)
        {
            var room = ship.Rooms[r];
            var sig = ((int)room.Kind, room.Cells.Count);
            if (r < _roomSig.Count && _roomSig[r] == sig) continue;
            if (r < _roomSig.Count) _roomSig[r] = sig; else _roomSig.Add(sig);
            changed = true;
            var under = UnderFor(room.Kind);
            var floor = Materials.FloorFor(room.Kind);
            while (_roomDrain.Count <= r) _roomDrain.Add((0f, 0f));
            _roomDrain[r] = (Materials.Of(floor).Drain, Materials.Of(floor).Absorb);
            int cy = (room.MinY + room.MaxY) / 2, cx = (room.MinX + room.MaxX) / 2;
            bool wide = room.MaxX - room.MinX >= room.MaxY - room.MinY;
            foreach (var c in room.Cells)
            {
                int i = grid.Index(c);
                Floor[i] = floor;
                var u = under;
                if ((c.X * 3 + c.Y * 5) % 7 == 0 && ship.IsOpenFloor(c)) u |= UnderFlags.HatchSpot;
                Under[i] = u;
                var ce = CeilingFlags.None;
                if ((c.X + c.Y * 2) % 4 == 0) ce |= CeilingFlags.Light;
                if (wide ? c.Y == cy : c.X == cx) ce |= CeilingFlags.Duct;
                if ((c.X * 2 + c.Y) % 5 == 0) ce |= CeilingFlags.Sprinkler;
                Ceiling[i] = ce;
                // 비상 유도선: 통로 한가운데 줄 · 방은 문에서 가운데로
                Guide[i] = room.Kind == RoomType.Corridor ? (wide ? c.Y == cy : c.X == cx) : false;
            }
            if (room.Kind != RoomType.Corridor)
                foreach (var d in room.Doors)
                {
                    // 문에서 방 가운데까지 한 줄
                    var p = d.Cell;
                    for (int k = 0; k < 12; k++)
                    {
                        var step = new Cell(Math.Sign(cx - p.X), k % 2 == 0 ? Math.Sign(cy - p.Y) : 0);
                        if (step.X != 0 && step.Y != 0) step = new Cell(step.X, 0);
                        if (step.X == 0 && step.Y == 0) break;
                        p += step;
                        if (ship.RoomAt(p) != room) break;
                        Guide[grid.Index(p)] = true;
                    }
                }
        }
        if (ship.Walls.Count() != _wallCount || changed) SyncWalls();
    }

    private void SyncWalls()
    {
        var ship = _w.Ship;
        var grid = ship.Grid;
        _wallCount = 0;
        var cells = new List<Cell>();
        foreach (var kv in ship.Walls) { cells.Add(kv.Key); _wallCount++; }
        cells.Sort((a, b) => grid.Index(a).CompareTo(grid.Index(b)));
        var keep = new HashSet<Cell>(cells);
        WallList.RemoveAll(wb => !keep.Contains(wb.Cell));
        foreach (var c in _walls.Keys.Where(c => !keep.Contains(c)).ToList()) _walls.Remove(c);
        foreach (var c in cells)
        {
            var ws = ship.WallAt(c)!;
            if (!_walls.TryGetValue(c, out var wb))
            {
                wb = new WallBody { Cell = c };
                _walls[c] = wb;
                WallList.Add(wb);
            }
            wb.Hull = ws.IsHull;
            Room? a = null, b = null;
            foreach (var d in Cell.Dirs4)
            {
                var r = ship.RoomAt(c + d);
                if (r == null) continue;
                if (a == null) a = r; else if (r != a) b = r;
            }
            wb.Thin = !wb.Hull && a != null && b != null && (a.Partitioned || b.Partitioned || Cabin(a.Kind) && Cabin(b.Kind)
                || Cabin(a.Kind) && b.Kind is RoomType.Lounge or RoomType.Mess or RoomType.Gym || Cabin(b.Kind) && a.Kind is RoomType.Lounge or RoomType.Mess or RoomType.Gym);
            wb.Wiring = a != null && (UnderFor(a.Kind).HasFlag(UnderFlags.Wiring)) && (c.X + c.Y) % 3 != 0;
            bool faceSpace = Cell.Dirs4.Any(d => grid.Kind(c + d) == TileKind.Void);
            wb.Window = wb.Hull && faceSpace && a != null && ViewRoom(a.Kind) && (c.X + c.Y) % 3 == 1;
            wb.Room = a?.Id ?? -1;
        }
        WallList.Sort((x, y) => grid.Index(x.Cell).CompareTo(grid.Index(y.Cell)));
        BuildThin();
    }

    // ───────────────────────────── 칸 상태 ─────────────────────────────

    public void SetMark(Cell c, CellMark m, float v, string cause)
    {
        var grid = _w.Ship.Grid;
        if (!grid.InBounds(c)) return;
        int i = grid.Index(c);
        if (!Marks.TryGetValue(i, out var s))
        {
            if (v <= 0.01f) return;
            s = new CellState();
            Marks[i] = s;
        }
        int k = (int)m;
        if (s.V[k] <= 0.01f && v > 0.01f) { s.Since[k] = _w.Tick; s.Cause[k] = cause; }
        else if (v > s.V[k] + 0.05f) s.Cause[k] = cause;
        s.V[k] = Math.Clamp(v, 0f, 1f);
        if (s.Empty) Marks.Remove(i);
    }

    public void RaiseMark(Cell c, CellMark m, float v, string cause)
    {
        if (v > Mark(c, m)) SetMark(c, m, v, cause);
    }

    // ───────────────────────────── 점검 뚜껑 ─────────────────────────────

    public OpenHatch? OpenHatchAt(Cell c, int by, int order, string why)
    {
        var grid = _w.Ship.Grid;
        if (!grid.InBounds(c)) return null;
        int i = grid.Index(c);
        if (_hatchOpen[i]) return Hatches.FirstOrDefault(h => h.Cell == c);
        var h2 = new OpenHatch { Cell = c, By = by, Order = order, Since = _w.Tick, Why = why };
        Hatches.Add(h2);
        _hatchOpen[i] = true;
        Stats.HatchOpens++;
        return h2;
    }

    public void CloseHatch(OpenHatch h)
    {
        Hatches.Remove(h);
        var grid = _w.Ship.Grid;
        _hatchOpen[grid.Index(h.Cell)] = false;
    }

    /// <summary>일하는 사람 곁의 점검 뚜껑 자리 (없으면 발밑 빈 바닥).</summary>
    private Cell? HatchNear(CrewMember c, UnderFlags need)
    {
        var ship = _w.Ship;
        var grid = ship.Grid;
        Cell? best = null;
        int bestD = int.MaxValue;
        for (int dy = -2; dy <= 2; dy++)
        for (int dx = -2; dx <= 2; dx++)
        {
            var p = new Cell(c.Cell.X + dx, c.Cell.Y + dy);
            if (!grid.InBounds(p) || ship.RoomAt(p) != c.Room || !ship.IsOpenFloor(p)) continue;
            var u = Under[grid.Index(p)];
            if ((u & need) == 0) continue;
            int d = dx * dx + dy * dy + ((u & UnderFlags.HatchSpot) != 0 ? 0 : 6) + (p == c.Cell ? 3 : 0);
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    private static bool PipeWork(WorkKind k) => k is WorkKind.PatchPipe or WorkKind.ReplacePipe or WorkKind.LayBypass or WorkKind.Reline or WorkKind.IsolatePipes
        or WorkKind.CloseValve or WorkKind.OpenValve or WorkKind.RefillCoolant or WorkKind.ShutRoomValve or WorkKind.OpenRoomValve or WorkKind.PumpOut;

    private static bool WireWork(WorkKind k) => k is WorkKind.Rewire or WorkKind.RestoreCircuit or WorkKind.InstallJumper or WorkKind.RemoveJumper
        or WorkKind.RepairNet or WorkKind.Reconnect or WorkKind.IsolatePower or WorkKind.FixLights;

    /// <summary>배관 · 배선 수리하는 사람이 실제로 손을 대는 동안 뚜껑 · 패널을 연다 — 손을 떼면 닫는다 (잊기도).</summary>
    private void TrackRepairs()
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Outside || c.Room == null || c.Job?.Order is not WorkOrder o || c.Job.Current is not WorkToil || c.Pose != Pose.Working) continue;
            bool pipe = PipeWork(o.Kind), wire = WireWork(o.Kind);
            if (!pipe && !wire) continue;
            if (Hatches.Any(h => h.By == c.Id && h.Order == o.Id && !h.Forgotten) || WallList.Any(x => x.PanelBy == c.Id && x.PanelOrder == o.Id && x.PanelOff && !x.PanelForgot)) continue;
            if (wire)
            {
                // 배선은 벽 속으로도 지나간다: 곁의 벽 패널을 뗀다
                WallBody? wb = null;
                foreach (var d in Cell.Dirs4)
                    if (WallAt(c.Cell + d) is WallBody x && x.Wiring && !x.PanelOff) { wb = x; break; }
                if (wb != null)
                {
                    wb.PanelOff = true; wb.PanelBy = c.Id; wb.PanelOrder = o.Id; wb.PanelSince = w.Tick; wb.PanelForgot = false;
                    Stats.PanelOffs++;
                    continue;
                }
            }
            if (HatchNear(c, pipe ? UnderFlags.Pipe : UnderFlags.Wiring) is Cell at)
                OpenHatchAt(at, c.Id, o.Id, pipe ? "배관 수리" : "배선 수리");
        }
        // 손을 뗐다: 닫는다 — 지치거나 서두르거나 덜렁대면 잊는다
        for (int k = Hatches.Count - 1; k >= 0; k--)
        {
            var h = Hatches[k];
            if (h.Forgotten || h.By < 0) continue;
            var by = h.By < w.Crew.Count ? w.Crew.FirstOrDefault(x => x.Id == h.By) : w.Crew.FirstOrDefault(x => x.Id == h.By);
            if (by != null && !by.Dead && by.Job?.Order?.Id == h.Order) continue;
            if (by != null && R.Chance(ForgetChance(by)))
            {
                h.Forgotten = true;
                Stats.HatchForgot++;
                w.Log.Add(w.Tick, LogKind.Ship, $"{w.Ship.RoomAt(h.Cell)?.Name ?? "?"} 점검 뚜껑이 열린 채 남았다 ({h.Why} 뒤)", by.Id);
            }
            else CloseHatch(h);
        }
        foreach (var wb in WallList)
        {
            if (!wb.PanelOff || wb.PanelForgot || wb.PanelBy < 0) continue;
            var by = w.Crew.FirstOrDefault(x => x.Id == wb.PanelBy);
            if (by != null && !by.Dead && by.Job?.Order?.Id == wb.PanelOrder) continue;
            if (by != null && R.Chance(ForgetChance(by) * 1.4f))
            {
                wb.PanelForgot = true;
                Stats.PanelForgot++;
                w.Log.Add(w.Tick, LogKind.Ship, $"{w.Ship.RoomAt(wb.Cell + Cell.Dirs4.FirstOrDefault(d => w.Ship.RoomAt(wb.Cell + d) != null))?.Name ?? "?"} 벽 패널을 떼어 둔 채 갔다 — 배선이 드러나 있다", by.Id);
            }
            else { wb.PanelOff = false; wb.PanelBy = -1; wb.PanelOrder = -1; }
        }
    }

    private float ForgetChance(CrewMember c) =>
        0.08f + 0.25f * (1f - c.Traits.Diligence) + (c.Needs.Rest < 0.3f ? 0.1f : 0f) + (Crisis.Acting(_w) ? 0.15f : 0f);

    // ───────────────────────────── 한 걸음 (Movement.Manners에서) ─────────────────────────────

    private int[] _lastCell = Array.Empty<int>();
    private long[] _fallUntil = Array.Empty<long>(), _holdUntil = Array.Empty<long>();
    private int[] _crankDoor = Array.Empty<int>(), _sensorDoor = Array.Empty<int>(), _dropDoor = Array.Empty<int>();

    private void EnsureCrew(int id)
    {
        if (id < _lastCell.Length) return;
        int n = Math.Max(id + 1, _lastCell.Length * 2 + 8);
        int old = _lastCell.Length;
        Array.Resize(ref _lastCell, n); Array.Resize(ref _fallUntil, n); Array.Resize(ref _holdUntil, n);
        Array.Resize(ref _crankDoor, n); Array.Resize(ref _sensorDoor, n); Array.Resize(ref _dropDoor, n);
        for (int i = old; i < n; i++) { _lastCell[i] = -1; _fallUntil[i] = -1; _holdUntil[i] = -1; _crankDoor[i] = -1; _sensorDoor[i] = -1; _dropDoor[i] = -1; }
    }

    public bool Fallen(CrewMember c) => c.Id < _fallUntil.Length && _fallUntil[c.Id] > _w.Tick;
    public void KnockDown(CrewMember c, string why, float injury) { EnsureCrew(c.Id); Fall(c, c.Cell, why, injury, leg: false); } // v16.13 폭발 압력에 넘어진다

    /// <summary>
    /// 한 걸음 내딛기 전의 배율 (0이면 이번 틱은 서 있다): 넘어져 있음 · 문(잠금 · 노크 · 손으로 돌리기 · 센서) · 열린 뚜껑 곁 · 연기 속 유도선.
    /// 칸에 들어서면 닳음을 쌓고 미끄러짐 · 발 빠짐을 굴린다.
    /// </summary>
    public float Step(CrewMember c, List<Cell> path)
    {
        var w = _w;
        if (c.Outside || c.CarriedBy != null || c.Room == null && w.Ship.Grid.Kind(c.Cell) != TileKind.Door) return 1f;
        long now = w.Tick;
        EnsureCrew(c.Id);
        int id = c.Id;
        if (_fallUntil[id] > now) return 0f;
        if (_fallUntil[id] >= 0 && _fallUntil[id] <= now) { _fallUntil[id] = -1; if (c.Pose == Pose.Sitting) c.Pose = Pose.Walking; }
        if (_holdUntil[id] > now) return 0f;
        var grid = w.Ship.Grid;
        bool urgent = c.Job?.Urgent == true;

        // 칸에 들어섰다
        int ci = grid.Index(c.Cell);
        if (_lastCell[id] != ci)
        {
            _lastCell[id] = ci;
            if (w.Ship.RoomAt(c.Cell) != null) Entered(c, ci, urgent);
            if (_fallUntil[id] > now) return 0f;
        }

        // 문: 다음 칸이 문이거나, 지금 칸을 다 와서 그다음이 문
        var next = path[c.PathIndex];
        Door? door = w.Ship.DoorAt(next);
        int beyondAt = c.PathIndex + 1;
        if (door == null && c.PathIndex + 1 < path.Count && (next.Center - c.Position).LengthSquared() < 0.04f && w.Ship.DoorAt(path[c.PathIndex + 1]) is Door d2)
        { door = d2; beyondAt = c.PathIndex + 2; }
        float mul = 1f;
        if (door != null && !door.IsExternal && !door.Removed)
        {
            var beyond = beyondAt < path.Count ? w.Ship.RoomAt(path[beyondAt]) : null;
            float g = Gate(c, door, beyond, urgent);
            if (g <= 0f) return 0f;
            mul *= g;
        }
        else if (door == null) { _crankDoor[id] = -1; }

        int ni = grid.Index(next);
        if (_hatchOpen[ni]) mul *= 0.5f; // 열린 뚜껑을 타 넘는다 (조심조심)
        if (Marks.Count > 0 && Marks.TryGetValue(ni, out var ms) && (ms.V[(int)CellMark.Glass] > 0.2f || ms.V[(int)CellMark.Oil] > 0.3f)) mul *= 0.8f;

        // 연기 속: 고참은 배를 기억하고, 신입은 유도선을 따라간다
        if (c.Room is Room room && room.Air.Smoke > 0.35f)
        {
            bool veteran = !c.IsChild && !w.Society.Recent(c);
            mul *= veteran ? 0.85f : Guide[ci] || Guide[ni] ? 0.75f : 0.45f;
        }
        return mul;
    }

    private void Entered(CrewMember c, int i, bool urgent)
    {
        var w = _w;
        var cell = w.Ship.Grid.CellAt(i);
        // 닳는 바닥
        Steps[i]++;
        var spec = Materials.Of(Floor[i]);
        Wear[i] = MathF.Min(1f, Wear[i] + spec.Wear * WearPerStep);

        // 발소리: 격자 · 금속판 위를 뛰면 방이 시끄러워진다 (자는 사람 · 엿듣기에 닿는다), 카펫은 조용하다
        if (urgent && spec.Loud > 0.45f && w.Ship.RoomAt(cell) is Room fr) fr.Noise = MathF.Min(1f, fr.Noise + 0.015f * spec.Loud);

        // 열린 점검 뚜껑에 발이 빠진다
        if (_hatchOpen[i] && R.Chance(urgent ? 0.35f : 0.1f))
        {
            Fall(c, cell, "열린 점검 뚜껑에 발이 빠졌다", 0.05f + R.Range(0f, 0.06f), leg: true);
            Stats.FootIn++;
            return;
        }
        // 미끄러짐: 재질 × 상태 × 뛰기
        float slip = SlipAt(i);
        if (slip > 0.25f)
        {
            float run = urgent ? 0.6f : 0.08f;
            if (c.Carrying != null || c.CarryingPerson != null) run *= 1.3f;
            float risk = (slip - 0.25f) * (slip - 0.25f) * run;
            if (R.Chance(risk))
            {
                var ms = Marks.TryGetValue(i, out var st) ? st : null;
                string why = ms == null ? "반들반들한 바닥" : ms.V[(int)CellMark.Oil] > 0.2f ? "기름" : ms.V[(int)CellMark.Frost] > 0.2f ? "서리" : ms.V[(int)CellMark.Wet] > 0.1f ? "물기" : "반들반들한 바닥";
                Fall(c, cell, $"{(urgent ? "뛰다가 " : "")}{spec.Name} 바닥 {why}에 미끄러져 넘어졌다", spec.Hard * (urgent ? 0.07f : 0.035f), leg: false);
                Stats.Slips++;
                return;
            }
        }
        // 유리 조각: 밟으면 베인다 (신발 덕에 드물게)
        if (Marks.Count > 0 && Marks.TryGetValue(i, out var gs) && gs.V[(int)CellMark.Glass] > 0.2f && R.Chance(0.06f * gs.V[(int)CellMark.Glass]))
        {
            NeedsSystem.AddInjury(c.Vitals, 0.02f, "유리 조각에 베임");
            Stats.Cuts++;
            if (c.SaidUntil < w.Tick) c.Say(w, Persona.Say(c, "앗, 유리!"));
        }
        // 곁의 위험을 본다: 잊힌 뚜껑은 닫고, 유리 · 기름은 테이프로 막아 둔다
        if (!urgent) Notice(c, cell);
    }

    private void Fall(CrewMember c, Cell cell, string why, float injury, bool leg)
    {
        var w = _w;
        Stats.Falls++;
        _fallUntil[c.Id] = w.Tick + R.Range(6, 16);
        c.Pose = Pose.Sitting;
        if (injury > 0.01f && R.Chance(leg ? 0.7f : 0.5f)) NeedsSystem.AddInjury(c.Vitals, injury, leg ? "발이 빠져 다침" : "미끄러져 다침");
        w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.IGa(c.Name)} {why} ({w.Ship.RoomAt(cell)?.Name ?? "?"})", c.Id);
        c.Say(w, Persona.Say(c, leg ? "으악, 발이!" : "아이고!"));
    }

    private void Notice(CrewMember c, Cell cell)
    {
        var w = _w;
        var grid = w.Ship.Grid;
        foreach (var d in Cell.Dirs4)
        {
            var p = cell + d;
            if (!grid.InBounds(p)) continue;
            int pi = grid.Index(p);
            if (_hatchOpen[pi])
            {
                var h = Hatches.FirstOrDefault(x => x.Cell == p);
                if (h != null && h.Forgotten && R.Chance(0.3f * (0.5f + c.Traits.Diligence)))
                {
                    CloseHatch(h);
                    Stats.HatchClosedBy++;
                    w.Log.Add(w.Tick, LogKind.Life, $"누가 열어 둔 점검 뚜껑을 닫았다 ({w.Ship.RoomAt(p)?.Name})", c.Id);
                    if (c.SaidUntil < w.Tick) c.Say(w, Persona.Say(c, "누가 뚜껑을 열어 놨어 — 큰일 날 뻔했네"));
                    return;
                }
            }
            if (Marks.Count > 0 && Marks.TryGetValue(pi, out var s) && s.V[(int)CellMark.Tape] < 0.5f
                && (s.V[(int)CellMark.Glass] > 0.3f || s.V[(int)CellMark.Oil] > 0.5f) && R.Chance(0.25f))
            {
                SetMark(p, CellMark.Tape, 1f, $"{Ko.IGa(c.Name)} 테이프로 막았다");
                Stats.Taped++;
                return;
            }
        }
    }

    // ───────────────────────────── 시스템 틱 ─────────────────────────────

    private readonly Dictionary<int, bool> _lightsWere = new();
    private int[] _bodyCost = Array.Empty<int>();

    public void Update(float dt)
    {
        var w = _w;
        long pf = Prof.Now;
        TrackRepairs();
        UpdateShelters(dt);
        UpdateDoors(dt);
        ScanTalk();
        if (w.Tick >= _nextSlow)
        {
            _nextSlow = w.Tick + SimTime.Minutes(1);
            SyncStructure();
            UpdateCells(SimTime.Minutes(1) / (float)SimTime.TicksPerHour);
            UpdateWalls(SimTime.Minutes(1) / (float)SimTime.TicksPerHour);
            UpdateMounts();
        }
        FillPathCost();
        Prof.Lap("sys.Body", pf);
    }

    /// <summary>길찾기에 칸 비용: 열린 뚜껑 · 테이프 · 유리 · 기름 (사람들은 돌아간다).</summary>
    private void FillPathCost()
    {
        var cost = _w.Paths.CellBody;
        Array.Clear(cost);
        foreach (var h in Hatches) cost[_w.Ship.Grid.Index(h.Cell)] += HatchCost;
        foreach (var (i, s) in Marks)
        {
            if (s.V[(int)CellMark.Tape] > 0.5f) cost[i] += TapeCost;
            if (s.V[(int)CellMark.Glass] > 0.2f) cost[i] += GlassCost;
            if (s.V[(int)CellMark.Oil] > 0.3f) cost[i] += OilCost;
        }
        _w.Paths.BodyChanged();
    }

    private void UpdateCells(float dt)
    {
        var w = _w;
        var ship = w.Ship;
        var grid = ship.Grid;
        // 1) 바닥 물 (침수 · 스프링클러) — 격자는 아래로 빠지고, 카펫은 머금는다
        foreach (var room in ship.Rooms)
        {
            if (room.Detached) continue;
            float depth = MoistureSystem.Depth(room);
            bool sprinkling = room.Suppression && room.Powered && w.Fire.Count > 0 && w.Fire.CountIn(room) > 0;
            if (depth > 0.004f || sprinkling)
                foreach (var c in room.Cells)
                {
                    int i = grid.Index(c);
                    var m = Materials.Of(Floor[i]);
                    if (depth > 0.004f) RaiseMark(c, CellMark.Wet, MathF.Min(1f, (0.35f + depth * 4f) * (1f - 0.8f * m.Drain)), "침수");
                    if (sprinkling && (Ceiling[i] & CeilingFlags.Sprinkler) != 0) RaiseMark(c, CellMark.Wet, 0.8f * (1f - 0.7f * m.Drain), "스프링클러");
                }
            // 격자 바닥은 물이 아래 빌지로 빠지고 (침수가 준다), 젖은 카펫은 습기와 냄새를 오래 머금는다
            if (room.Id < _roomDrain.Count)
            {
                var (drain, absorb) = _roomDrain[room.Id];
                if (room.Flood > 0.5f && drain > 0.05f) { float gone = room.Flood * drain * 0.25f * dt; room.Flood -= gone; Stats.Drained += gone; }
                if (absorb > 0.05f && depth <= 0.004f && room.Cells.Count > 0 && Mark(room.Cells[room.Cells.Count / 2], CellMark.Wet) > 0.1f)
                {
                    room.Humidity = MathF.Min(1f, room.Humidity + 0.3f * absorb * dt);
                    room.Smell = MathF.Min(1f, room.Smell + 0.4f * absorb * dt);
                }
            }
            // 2) 서리: 얼어붙은 방
            if (room.Air.Temperature < 1f)
                foreach (var c in room.Cells) RaiseMark(c, CellMark.Frost, Math.Clamp((1f - room.Air.Temperature) / 8f, 0.2f, 1f), "추위");
            // 3) 조명이 나갔다 → 깨진 등 아래 유리 조각
            bool was = _lightsWere.TryGetValue(room.Id, out var lw) && lw;
            if (room.LightsOut && !was)
            {
                int n = 0;
                foreach (var c in room.Cells)
                    if ((Ceiling[grid.Index(c)] & CeilingFlags.Light) != 0 && R.Chance(0.35f) && n < 3) { RaiseMark(c, CellMark.Glass, 0.7f, "깨진 조명"); n++; }
            }
            _lightsWere[room.Id] = room.LightsOut;
            // 4) 기름: 고장 난 기계가 샌다
            if (room.Kind is RoomType.Engine or RoomType.Workshop or RoomType.Cooling or RoomType.PumpRoom or RoomType.Reactor)
                foreach (var f in room.Furniture)
                    if (f.Machine is Machine mc && mc.Faults.Count > 0 && f.UseSpots.Count > 0 && R.Chance(0.02f))
                        RaiseMark(f.UseSpots[0], CellMark.Oil, Mark(f.UseSpots[0], CellMark.Oil) + 0.3f, $"{f.Name} 기름 샘");
        }
        // 5) 그을음 = 불 자국
        foreach (var (cell, sc) in w.Fire.Scorch)
            if (sc > Mark(cell, CellMark.Soot) + 0.05f) SetMark(cell, CellMark.Soot, sc, "화재");
        // 6) 마르기 · 녹기 (카펫은 늦게 마르고, 서리는 녹아 물이 된다)
        List<(int, CellMark, float)>? set = null;
        foreach (var (i, s) in Marks)
        {
            var room = ship.Rooms.Count > 0 ? ship.RoomAt(grid.CellAt(i)) : null;
            var m = Materials.Of(Floor[i]);
            float wet = s.V[(int)CellMark.Wet];
            if (wet > 0f && (room == null || MoistureSystem.Depth(room) <= 0.004f))
                (set ??= new()).Add((i, CellMark.Wet, wet - dt * (1.2f + 2f * m.Drain) * (1f - 0.85f * m.Absorb)));
            float fr = s.V[(int)CellMark.Frost];
            if (fr > 0f && (room == null || room.Air.Temperature > 2f))
            {
                (set ??= new()).Add((i, CellMark.Frost, fr - dt * 2f));
                (set ??= new()).Add((i, CellMark.Wet, MathF.Max(wet, MathF.Min(1f, fr))));
            }
            float tape = s.V[(int)CellMark.Tape];
            if (tape > 0f && s.V[(int)CellMark.Glass] < 0.1f && s.V[(int)CellMark.Oil] < 0.1f && !_hatchOpen[i])
                (set ??= new()).Add((i, CellMark.Tape, 0f)); // 치웠으면 테이프도 걷는다
        }
        if (set != null)
            foreach (var (i, m, v) in set)
                SetMark(grid.CellAt(i), m, MathF.Max(0f, v), m == CellMark.Wet ? Marks.TryGetValue(i, out var s0) ? s0.Cause[(int)CellMark.Wet] ?? "물기" : "물기" : "");
    }

    private void UpdateWalls(float dt)
    {
        var w = _w;
        var ship = w.Ship;
        var grid = ship.Grid;
        bool storm = w.Hazards.StormActive;
        foreach (var wb in WallList)
        {
            if (!wb.Hull) continue;
            var ws = ship.WallAt(wb.Cell);
            if (ws == null) continue;
            // 외판이 찌그러지면 단열재가 눌리고 · 불에 그을리면 탄다
            float hit = MathF.Min(ws.Integrity, 1f - 0.6f * ws.Scorch);
            if (hit < wb.Insulation - 0.05f) wb.Insulation = MathF.Max(0f, hit);
            // 관측창: 태양 폭풍이면 덮개를 내린다
            if (wb.Window && wb.Shutter != storm)
            {
                wb.Shutter = storm;
                if (storm) Stats.ShutterCloses++;
            }
            if (wb.Insulation >= 0.5f) continue;
            // 단열재가 상한 벽: 차갑다 → 결로 (습한 방에서 벽 밑 바닥이 젖는다)
            foreach (var d in Cell.Dirs4)
            {
                var p = wb.Cell + d;
                var room = ship.RoomAt(p);
                if (room == null || room.Detached) continue;
                room.Air.Temperature -= 0.6f * (0.5f - wb.Insulation) * dt * 8f / MathF.Max(4f, room.Volume);
                if (room.Humidity > 0.45f && room.Air.Temperature > 1f)
                {
                    if (Mark(p, CellMark.Wet) < 0.3f) Stats.Condensation++;
                    RaiseMark(p, CellMark.Wet, 0.45f, "결로");
                }
                else if (room.Air.Temperature <= 1f) RaiseMark(p, CellMark.Frost, 0.6f, "차가운 벽");
            }
        }
        // 관측창 경치 → 기분 (덮개를 내리면 없다)
        var views = new int[ship.Rooms.Count];
        foreach (var wb in WallList) if (wb.Window && !wb.Shutter && wb.Room >= 0 && wb.Room < views.Length) views[wb.Room]++;
        foreach (var c in w.Crew)
        {
            if (c.Dead || !c.IsAwake || c.Room is not Room r || r.Id >= views.Length || views[r.Id] == 0) continue;
            c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.02f * Math.Min(3, views[r.Id]) * dt);
        }
    }

    /// <summary>시험 · 화면: 이 방에 보이는 관측창 수.</summary>
    public int WindowsOf(Room r) => WallList.Count(wb => wb.Window && wb.Room == r.Id);

    // ───────────────────────────── 비상 대피실 (독립 산소 · 배터리) ─────────────────────────────

    private readonly Dictionary<int, (float o2, float battery)> _shelter = new();
    public float ShelterCharge(Room r) => _shelter.TryGetValue(r.Id, out var s) ? s.battery : 1f;
    public float ShelterOxygen(Room r) => _shelter.TryGetValue(r.Id, out var s) ? s.o2 : 1f;

    /// <summary>대피실은 배 전원이 끊겨도 자기 배터리로 불을 켜고, 자기 산소통으로 숨 쉴 공기를 지킨다 (한정).</summary>
    private void UpdateShelters(float dt)
    {
        foreach (var room in _w.Ship.Rooms)
        {
            if (room.Kind != RoomType.Shelter || room.Detached) continue;
            var (o2, bat) = _shelter.TryGetValue(room.Id, out var s) ? s : (1f, 1f);
            if (!room.Powered && bat > 0f && !room.BreakerOff) { room.Powered = true; bat = MathF.Max(0f, bat - dt / 24f); }
            else if (room.Powered && bat < 1f) bat = MathF.Min(1f, bat + dt / 6f);
            if (room.Air.O2 < 19f && o2 > 0f && room.Air.Pressure > 30f && !room.Leaking)
            {
                float add = MathF.Min(19f - room.Air.O2, 30f * dt);
                room.Air.O2 += add;
                o2 = MathF.Max(0f, o2 - add / 200f);
            }
            _shelter[room.Id] = (o2, bat);
        }
    }

    // ───────────────────────────── 벽 장착물 ─────────────────────────────

    private void InitMounts()
    {
        var ship = _w.Ship;
        foreach (var room in ship.Rooms)
        {
            if (room.Detached) continue;
            var kinds = new List<MountKind>();
            if (room.Kind is RoomType.Corridor or RoomType.Galley or RoomType.Engine or RoomType.Reactor or RoomType.Power or RoomType.Workshop or RoomType.Hydroponics or RoomType.LifeSupport)
                kinds.Add(MountKind.Extinguisher);
            if (room.Kind is RoomType.Quarters or RoomType.Bridge or RoomType.Medbay or RoomType.Mess or RoomType.PrivateCabins or RoomType.Corridor) kinds.Add(MountKind.OxygenMasks);
            if (room.Kind is RoomType.Engine or RoomType.Power or RoomType.Corridor or RoomType.Reactor or RoomType.Storage) kinds.Add(MountKind.Flashlight);
            if (room.Kind is RoomType.Mess or RoomType.Lounge or RoomType.Corridor) kinds.Add(MountKind.Board);
            if (room.Kind == RoomType.Corridor && room.Cells.Count > 60) kinds.Add(MountKind.Extinguisher);
            int k = 0;
            var used = new HashSet<Cell>();
            foreach (var c in room.Cells.OrderBy(c => (c.X * 7 + c.Y * 11) % 13).ThenBy(c => c.Y).ThenBy(c => c.X))
            {
                if (k >= kinds.Count) break;
                if (!ship.IsOpenFloor(c)) continue;
                foreach (var d in Cell.Dirs4)
                {
                    var wc = c + d;
                    if (ship.Grid.Kind(wc) != TileKind.Wall || used.Contains(wc) || WallAt(wc) is { Window: true }) continue;
                    used.Add(wc);
                    Mounts.Add(new WallMount { Id = Mounts.Count, Kind = kinds[k++], Wall = wc, Spot = c, Room = room.Id });
                    break;
                }
            }
        }
    }

    /// <summary>위기에 벽 장착물을 꺼내 쓴다 (자리를 아는 사람만 — 신입은 모른다).</summary>
    private void UpdateMounts()
    {
        var w = _w;
        foreach (var m in Mounts)
        {
            if (!m.Present || m.Room >= w.Ship.Rooms.Count) continue;
            var room = w.Ship.Rooms[m.Room];
            if (room.Detached) continue;
            bool need = m.Kind switch
            {
                MountKind.Extinguisher => w.Fire.Count > 0 && w.Fire.CountIn(room) > 0,
                MountKind.OxygenMasks => room.Air.Smoke > 0.5f || room.Air.O2 < 15f,
                MountKind.Flashlight => room.Dark,
                _ => false,
            };
            if (!need) continue;
            var by = w.Crew.Where(c => !c.Dead && c.CanAct && c.IsAwake && c.Room == room && !c.IsChild && !w.Society.Recent(c) && (c.Position - m.Spot.Center).LengthSquared() < 25f)
                .OrderBy(c => (c.Position - m.Spot.Center).LengthSquared()).ThenBy(c => c.Id).FirstOrDefault();
            if (by == null) continue;
            m.Present = false; m.TakenBy = by.Id; m.TakenAt = w.Tick;
            Stats.MountUses++;
            string what = MountKind.Extinguisher == m.Kind ? "벽 소화기를 떼어 먼저 뿌렸다" : m.Kind == MountKind.OxygenMasks ? "벽의 산소 마스크함을 열었다" : "벽 걸이에서 손전등을 꺼냈다";
            if (m.Kind == MountKind.Extinguisher) w.Fire.Suppress(m.Spot, 2.5f, 0.35f);
            w.Log.Add(w.Tick, LogKind.Work, $"{what} ({room.Name})", by.Id);
        }
    }
}

// ───────────────────────────── 손보기: 뚜껑 · 패널 · 문 · 유리 · 기름 · 빈 걸이 ─────────────────────────────

public sealed class BodyUpkeepActivity : Activity
{
    public override string Id => "body-upkeep";
    public override string Label => "배 손보기";

    private enum Task { Hatch, Panel, Gasket, Sensor, Frame, Glass, Oil, Mount, Soot }

    private static (Task task, object target, Cell spot, int cost, float value)? Pick(CrewMember c, World w, DistanceField dist)
    {
        var b = w.Body;
        (Task, object, Cell, int, float)? best = null;
        float bestScore = 0f;
        void Consider(Task t, object target, Cell spot, float value)
        {
            int d = dist.Get(spot);
            if (d < 0) return;
            float s = value - d / 3000f;
            if (s > bestScore) { bestScore = s; best = (t, target, spot, d, value); }
        }
        foreach (var h in b.Hatches)
            if (h.Forgotten && Near(w, h.Cell) is Cell sp) Consider(Task.Hatch, h, sp, 0.55f);
        foreach (var wb in b.WallList)
            if (wb.PanelOff && wb.PanelForgot && wb.ClaimedBy < 0 && Near(w, wb.Cell) is Cell sp) Consider(Task.Panel, wb, sp, 0.35f + 0.2f * c.SkillLevel(Skill.Electrical));
        foreach (var db in b.Doors)
        {
            if (db.ClaimedBy >= 0 || db.Door >= w.Ship.Doors.Count) continue;
            var d = w.Ship.Doors[db.Door];
            if (d.Removed || d.IsExternal) continue;
            if (db.Gasket < 0.25f && Near(w, d.Cell) is Cell s1) Consider(Task.Gasket, db, s1, (db.Whistling ? 0.45f : 0.25f) + 0.15f * c.SkillLevel(Skill.Mechanics));
            if ((db.SensorBroken || db.IndicatorBroken) && Near(w, d.Cell) is Cell s2) Consider(Task.Sensor, db, s2, 0.3f + 0.2f * c.SkillLevel(Skill.Electrical));
            if (d.Bent > 0.3f && Near(w, d.Cell) is Cell s3) Consider(Task.Frame, db, s3, 0.35f + 0.2f * c.SkillLevel(Skill.Mechanics));
        }
        foreach (var (i, s) in b.Marks)
        {
            var cell = w.Ship.Grid.CellAt(i);
            if (s.V[(int)CellMark.Glass] > 0.2f) Consider(Task.Glass, cell, cell, 0.45f);
            else if (s.V[(int)CellMark.Oil] > 0.3f) Consider(Task.Oil, cell, cell, 0.3f);
            else if (s.V[(int)CellMark.Soot] > 0.4f && w.Fire.Count == 0) Consider(Task.Soot, cell, cell, 0.18f); // 불 꺼진 뒤 그을음 닦기
        }
        foreach (var m in b.Mounts)
            if (!m.Present && m.ClaimedBy < 0 && WallMount.Item(m.Kind) is ItemKind ik && w.Ship.CountStored(ik) > 0) Consider(Task.Mount, m, m.Spot, 0.25f);
        return best;
    }

    private static Cell? Near(World w, Cell c)
    {
        foreach (var d in Cell.Dirs4) if (w.Ship.IsWalkable(c + d) && w.Ship.Grid.Kind(c + d) == TileKind.Floor) return c + d;
        return null;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (c.IsChild || Crisis.Acting(w) || c.Needs.Rest < 0.2f) return (0f, "—");
        var p = Pick(c, w, dist);
        if (p == null) return (0f, "손볼 곳 없음");
        float s = p.Value.value * (OnShift(c, w) ? 1f : 0.5f) * (0.6f + 0.6f * c.Traits.Diligence);
        return (s, Name(p.Value.task));
    }

    private static string Name(Task t) => t switch
    {
        Task.Hatch => "열린 점검 뚜껑 닫기", Task.Panel => "떼어 둔 벽 패널 다시 붙이기", Task.Gasket => "문 패킹 갈기", Task.Sensor => "문 센서 · 표시판 고치기",
        Task.Frame => "휜 문틀 펴기", Task.Glass => "유리 조각 쓸기", Task.Oil => "기름 닦기", Task.Soot => "그을음 닦기", _ => "빈 걸이 채우기",
    };

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var p = Pick(c, w, dist);
        if (p == null) return null;
        var (task, target, spot, _, _) = p.Value;
        var b = w.Body;
        var toils = Plans.DropOff(c, w, dist);
        ItemKind? need = null;
        if (task == Task.Gasket) need = w.Ship.CountStored(ItemKind.Gasket) > 0 ? ItemKind.Gasket : w.Ship.CountStored(ItemKind.Seal) > 0 ? ItemKind.Seal : w.Ship.CountStored(ItemKind.Tape) > 0 ? ItemKind.Tape : null;
        if (task == Task.Mount && target is WallMount wm) need = WallMount.Item(wm.Kind);
        if (need is ItemKind ik)
        {
            var (box, bs) = Plans.NearestContainer(w, dist, c, f => f.Storage!.Count(ik) > 0);
            if (box == null) return null;
            toils.Add(new GotoToil(bs));
            toils.Add(new TakeToil(box, ik, 1));
        }
        else if (task == Task.Gasket) return null;
        // 맡았다 (둘이 같은 곳으로 가지 않게)
        switch (target)
        {
            case WallBody wb0: wb0.ClaimedBy = c.Id; break;
            case DoorBody db0: db0.ClaimedBy = c.Id; break;
            case WallMount m0: m0.ClaimedBy = c.Id; break;
        }
        toils.Add(new GotoToil(spot));
        float hours = task switch
        {
            Task.Hatch => 0.03f, Task.Panel => 0.3f, Task.Gasket => 0.5f, Task.Sensor => 0.4f, Task.Frame => 0.8f,
            Task.Glass or Task.Oil or Task.Soot => 0.15f * (1f + 2f * Materials.Of(b.FloorAt(spot)).CleanHard), _ => 0.05f,
        };
        var skill = task is Task.Panel or Task.Sensor ? Skill.Electrical : Skill.Mechanics;
        var face = target switch { WallBody x => x.Cell.Center, DoorBody x => w.Ship.Doors[x.Door].Cell.Center, WallMount x => x.Wall.Center, OpenHatch x => x.Cell.Center, Cell x => x.Center, _ => (Vector2?)null };
        toils.Add(new WorkToil(hours, skill, face));
        toils.Add(new DoToil((cm, world) => world.Body.Finish(cm, task.ToString(), target, need)));
        return new Job(this, Name(task), toils)
        {
            LogText = Name(task),
            OnFinished = (cm, world, st) =>
            {
                switch (target)
                {
                    case WallBody x when x.ClaimedBy == cm.Id: x.ClaimedBy = -1; break;
                    case DoorBody x when x.ClaimedBy == cm.Id: x.ClaimedBy = -1; break;
                    case WallMount x when x.ClaimedBy == cm.Id: x.ClaimedBy = -1; break;
                }
            },
        };
    }
}

public sealed partial class BodySystem
{
    /// <summary>손보기를 마쳤다.</summary>
    internal bool Finish(CrewMember c, string task, object target, ItemKind? used)
    {
        var w = _w;
        if (used is ItemKind ik)
        {
            if (c.Carrying is not ItemStack held || held.Kind != ik) return false;
            c.Carrying = held.Count > 1 ? new ItemStack(ik, held.Count - 1) : null;
        }
        Stats.Fixed++;
        switch (target)
        {
            case OpenHatch h:
                if (Hatches.Contains(h)) CloseHatch(h);
                w.Log.Add(w.Tick, LogKind.Work, $"열린 채 남은 점검 뚜껑을 닫았다 ({w.Ship.RoomAt(h.Cell)?.Name})", c.Id);
                break;
            case WallBody wb:
                wb.PanelOff = false; wb.PanelForgot = false; wb.PanelBy = -1; wb.PanelOrder = -1;
                Stats.PanelRefit++;
                w.Log.Add(w.Tick, LogKind.Work, "떼어 둔 벽 패널을 다시 붙였다", c.Id);
                break;
            case DoorBody db:
                var d = w.Ship.Doors[db.Door];
                if (task == "Gasket") { db.Gasket = used == ItemKind.Tape ? 0.45f : 1f; db.Whistling = false; Stats.Gaskets++; }
                else if (task == "Sensor") { db.SensorBroken = false; db.IndicatorBroken = false; }
                else if (task == "Frame") d.Bent = 0f;
                w.Log.Add(w.Tick, LogKind.Work, $"{d.RoomA?.Name ?? "?"}·{d.RoomB?.Name ?? "?"} 사이 문을 손봤다 ({(task == "Gasket" ? used == ItemKind.Tape ? "패킹 대신 테이프" : "패킹 교체" : task == "Sensor" ? "센서 · 표시판" : "문틀")})", c.Id);
                break;
            case Cell cell:
                if (task == "Soot" && w.Fire.Scorch.TryGetValue(cell, out var sc)) w.Fire.Scorch[cell] = sc * 0.3f; // 닦으면 불 자국도 옅어진다
                if (task == "Soot") SetMark(cell, CellMark.Soot, 0f, "");
                SetMark(cell, CellMark.Glass, 0f, "");
                SetMark(cell, CellMark.Oil, 0f, "");
                SetMark(cell, CellMark.Tape, 0f, "");
                Stats.Cleaned++;
                break;
            case WallMount m:
                m.Present = true; m.TakenBy = -1;
                Stats.MountRefills++;
                w.Log.Add(w.Tick, LogKind.Work, $"빈 걸이에 {WallMount.Name(m.Kind)}을(를) 채웠다", c.Id);
                break;
        }
        return true;
    }
}
