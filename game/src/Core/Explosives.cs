using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v16.13 폭발성 물건 23종 · 연쇄 · 위험 배치 읽기(주 컴퓨터 · 승무원) · 의도적 폭파.
//   물건은 기존 재고(보관함 속 연료통 · 셀팩 · 용제 · 페인트 · 실링폼 · 소화기 · 우주복 · 축전기 · 윤활유 · 탄소 · 금속 가루)와
//   설비(산소 발생기 곁 수소 · 의무실 산소통 · 주방 가스 · 밀가루 · 작업장 용접 가스 · 엔진 추진제 · 냉각 축압기 · 냉매 · 비료 · CO2 · 신호탄 · 메탄 · 채굴용 폭약),
//   벽의 소화기, 이동식 배터리(Portable), 발효 항아리(Cooking)에서 온다 — 위치 · 양 · 안정도 · 조건 · 세기 · 종류.
//   달면(불 · 뜨거운 방 · 곁의 히터/설비) 열이 오르고, 문턱을 넘으면 쉭 소리/아지랑이(알아챌 수 있다) 뒤에 터진다.
//   압력파 · 파편에 맞으면 연쇄 — 물건마다 한 번만 · 연쇄 깊이 6 · 틱마다 둘까지 (끝이 있다).

public enum ExplosiveKind : byte
{
    OxygenTank, HydrogenTank, FuelCan, BatteryCell, GasCylinder, Dust, PressureVessel, FermentJar, Extinguisher, Aerosol, RefrigerantCan,
    SuitO2Pack, WeldingGas, Propellant, Charge, Solvent, PortableBattery, CapacitorPack, Fertilizer, MethaneTank, CO2Cylinder, Flare, Lubricant,
}

/// <summary>물건의 성질: 세기(양 1) · 달아오르는 빠르기 · 터지는 열 · 견디는 압력 · 파편에 뚫릴 확률 · 쉭 소리 뒤 터지기까지(분) · 터지지 않고 타 버릴 확률 · 쉭 소리.</summary>
public sealed record ExplosiveSpec(ExplosiveKind Kind, string Name, BlastKind Blast, float Power, float HeatRate, float CookAt, float ShockAt, float Pierce, float FuseMin, float FuseMax, float Vent, bool Hiss);

public sealed class Explosive
{
    public int Id { get; init; }
    public ExplosiveKind Kind { get; init; }
    public string Key { get; init; } = "";
    public Cell Cell { get; set; }
    public float Amount { get; set; } = 1f;
    public float Stability { get; set; } = 1f;
    public float Heat { get; set; }
    /// <summary>발효 항아리 김 · 압력 (1이면 펑).</summary>
    public float Pressure { get; set; }
    /// <summary>분진: 공기 중에 날린 가루 (불꽃 하나면 터진다).</summary>
    public float Cloud { get; set; }
    /// <summary>보관함 안 (덜 달고 덜 흔들린다).</summary>
    public bool Inside { get; set; }
    public long FuseAt { get; set; } = -1;
    public long PrimedAt { get; set; } = -1;
    public string Why { get; set; } = "";
    public int Depth { get; set; }
    public bool Spent { get; set; }
    public long SpentAt { get; set; } = -1;
    /// <summary>마지막으로 옮겨 둔 사람 (책임).</summary>
    public int Handler { get; set; } = -1;
    public int Carried { get; set; } = -1;
    public bool Moved { get; set; }
    public bool Manual { get; init; }
    public float Risk { get; set; }
    public string RiskWhy { get; set; } = "";
    public float NearHeat { get; set; }
    public long WarnedAt { get; set; } = -1000000;
    public int Container { get; init; } = -1;
    public ItemKind? Item { get; init; }
    public int Mount { get; init; } = -1;
    public int Device { get; init; } = -1;
    public int Batch { get; init; } = -1;
    public int Order { get; set; } = -1;
    /// <summary>폭약: 겨눈 곳.</summary>
    public Cell? Aim { get; set; }
    public bool Armed { get; set; }
    public float Quality { get; set; } = 0.7f;
    public ExplosiveSpec Spec => ExplosiveSet.Spec(Kind);
    public bool Primed => FuseAt >= 0;
    public float Power => Spec.Power * (0.4f + 0.6f * MathF.Min(1.5f, Amount));
}

public enum BreachKind : byte { Door, Rubble, Detach }

/// <summary>의도적 폭파 주문: 만들기 → 설치 → 대피 → 카운트다운 → 터짐 (불발 · 과폭).</summary>
public sealed class BreachOrder
{
    public enum Step : byte { Craft, Place, Clear, Countdown, Dud, Done, Failed }
    public int Id { get; init; }
    public BreachKind Kind { get; init; }
    public Cell Target { get; init; }
    public int DoorId { get; init; } = -1;
    public int RoomId { get; init; } = -1;
    public string Why { get; init; } = "";
    public float Size { get; init; } = 0.4f;
    public Step Stage { get; set; }
    public int ChargeId { get; set; } = -1;
    public int ClaimedBy { get; set; } = -1;
    public int Placer { get; set; } = -1;
    public long DudUntil { get; set; } = -1;
    public int Tries { get; set; }
    public bool Over { get; set; }
    public long Since { get; init; }
    public string Result { get; set; } = "";
}

/// <summary>위험한 자리의 물건을 옮기는 일 (주 컴퓨터 제안 · 알아챈 사람 · 조사 뒤).</summary>
public sealed class SecureOrder
{
    public int Id { get; init; }
    public int Explosive { get; init; }
    public string Why { get; init; } = "";
    public string By { get; init; } = "";
    public int ClaimedBy { get; set; } = -1;
    public bool Done { get; set; }
    public long Since { get; init; }
}

public sealed class ExplosiveStats
{
    public int Primed, Noticed, Unheard, TookCover, ComputerWarnings, ComputerAlerts, Proposals, CrewNoticed, Secured, Burped, Shattered, Vented, Fizzled,
        Duds, Overcharges, Breached, Crafted, Budgeted, Cleared;
    public override string ToString() =>
        $"쉭 · 달아오름 {Primed}(알아챔 {Noticed} · 귀가 울려 못 들음 {Unheard} · 몸을 피함 {TookCover}) · 컴퓨터 경고 {ComputerWarnings}(대피 방송 {ComputerAlerts} · 제안 {Proposals}) · 사람이 알아챔 {CrewNoticed} · 옮김 {Secured} · "
        + $"항아리 김 빼기 {Burped} · 깨진 항아리 {Shattered} · 타 버림 {Vented} · 연쇄 끊김 {Fizzled} · 다음 틱으로 미룸 {Budgeted} · 폭약 {Crafted}(불발 {Duds} · 과폭 {Overcharges} · 뚫음 {Breached} · 비키게 함 {Cleared})";
}

public sealed class ExplosiveSet
{
    public const int MaxDepth = 6;
    private const int PerTick = 2;
    private readonly World _w;
    private readonly BlastSystem _b;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7349 + 409));
    public List<Explosive> All { get; } = new();
    public List<SecureOrder> Secures { get; } = new();
    public List<BreachOrder> Breaches { get; } = new();
    public ExplosiveStats Stats { get; } = new();
    private readonly SortedDictionary<string, long> _gone = new();
    private readonly SortedDictionary<int, string> _asked = new();
    private readonly SortedDictionary<int, int> _experienced = new();
    private int _next = 1, _nextOrder = 1;
    private long _nextSync, _nextScan, _budgetAt = -1;
    private int _budget;

    public ExplosiveSet(World w, BlastSystem b) { _w = w; _b = b; }

    private static readonly ExplosiveSpec[] Specs =
    {
        new(ExplosiveKind.OxygenTank, "산소통", BlastKind.Oxygen, 0.55f, 1f, 0.8f, 0.55f, 0.6f, 0.3f, 1f, 0.1f, true),
        new(ExplosiveKind.HydrogenTank, "수소 탱크", BlastKind.Hydrogen, 0.7f, 1f, 0.7f, 0.45f, 0.7f, 0.1f, 0.5f, 0f, true),
        new(ExplosiveKind.FuelCan, "연료통", BlastKind.Fuel, 0.45f, 0.8f, 0.9f, 0.6f, 0.4f, 0.5f, 2f, 0.5f, false),
        new(ExplosiveKind.BatteryCell, "예비 배터리 셀", BlastKind.Battery, 0.3f, 0.9f, 0.75f, 0.5f, 0.8f, 1f, 3f, 0f, true),
        new(ExplosiveKind.GasCylinder, "버너 가스 실린더", BlastKind.Gas, 0.5f, 1f, 0.8f, 0.5f, 0.6f, 0.3f, 1.2f, 0.2f, true),
        new(ExplosiveKind.Dust, "가루 포대", BlastKind.Dust, 0.55f, 0.3f, 9f, 9f, 0f, 0f, 0.1f, 0f, false),
        new(ExplosiveKind.PressureVessel, "압력 용기", BlastKind.Steam, 0.5f, 0.7f, 0.85f, 0.6f, 0.5f, 0.3f, 1f, 0f, true),
        new(ExplosiveKind.FermentJar, "발효 항아리", BlastKind.Ferment, 0.06f, 0.4f, 9f, 0.3f, 0.9f, 0f, 0.5f, 0f, false),
        new(ExplosiveKind.Extinguisher, "소화기", BlastKind.Powder, 0.3f, 0.8f, 0.9f, 0.7f, 0.5f, 0.3f, 1f, 0f, true),
        new(ExplosiveKind.Aerosol, "스프레이 캔", BlastKind.Aerosol, 0.2f, 1.2f, 0.6f, 0.6f, 0.7f, 0.1f, 0.6f, 0f, false),
        new(ExplosiveKind.RefrigerantCan, "냉매통", BlastKind.Refrigerant, 0.25f, 0.9f, 0.8f, 0.6f, 0.6f, 0.3f, 1f, 0f, true),
        new(ExplosiveKind.SuitO2Pack, "우주복 산소팩", BlastKind.Oxygen, 0.3f, 1f, 0.8f, 0.6f, 0.6f, 0.3f, 1f, 0f, true),
        new(ExplosiveKind.WeldingGas, "용접 가스", BlastKind.Acetylene, 0.6f, 1.1f, 0.65f, 0.35f, 0.6f, 0.3f, 0.8f, 0.1f, true),
        new(ExplosiveKind.Propellant, "추진제 탱크", BlastKind.Propellant, 0.75f, 0.6f, 0.85f, 0.55f, 0.45f, 0.5f, 2f, 0.3f, true),
        new(ExplosiveKind.Charge, "폭약", BlastKind.Charge, 0.6f, 0.3f, 1.5f, 0.6f, 0.3f, 0f, 0.05f, 0.6f, false),
        new(ExplosiveKind.Solvent, "용제통", BlastKind.Fuel, 0.25f, 1f, 0.7f, 0.7f, 0.3f, 0.3f, 1.5f, 0.6f, false),
        new(ExplosiveKind.PortableBattery, "이동식 배터리", BlastKind.Battery, 0.35f, 0.8f, 0.75f, 0.45f, 0.8f, 1.2f, 3.2f, 0f, true),
        new(ExplosiveKind.CapacitorPack, "축전기 묶음", BlastKind.Arc, 0.2f, 0.7f, 0.8f, 0.5f, 0.7f, 0f, 0.1f, 0f, false),
        new(ExplosiveKind.Fertilizer, "질산염 비료", BlastKind.Nitrate, 0.8f, 0.25f, 0.95f, 0.8f, 0.2f, 2f, 6f, 0.4f, false),
        new(ExplosiveKind.MethaneTank, "메탄 탱크", BlastKind.Gas, 0.5f, 1f, 0.75f, 0.5f, 0.6f, 0.2f, 0.8f, 0.2f, true),
        new(ExplosiveKind.CO2Cylinder, "CO2 실린더", BlastKind.ColdGas, 0.35f, 0.8f, 0.85f, 0.6f, 0.6f, 0.3f, 1f, 0f, true),
        new(ExplosiveKind.Flare, "신호탄 상자", BlastKind.Flare, 0.15f, 1.3f, 0.55f, 0.7f, 0.8f, 0f, 0.3f, 0f, false),
        new(ExplosiveKind.Lubricant, "윤활유 드럼", BlastKind.Grease, 0.2f, 0.6f, 0.95f, 0.9f, 0.3f, 1f, 3f, 0.7f, false),
    };

    public static ExplosiveSpec Spec(ExplosiveKind k) => Specs[(int)k];
    public static IReadOnlyList<ExplosiveSpec> AllSpecs => Specs;

    // ───────────────────────────── 원정 채굴용 공개 속성 ─────────────────────────────

    /// <summary>폭약 한 발의 성질 (원정 채굴이 읽는다): 세기 · 한 발에 캐는 양 · 불발 · 과폭 · 만드는 시간 · 재료.</summary>
    public readonly record struct ChargeSpec(float Power, float Yield, float DudChance, float OverChance, float CraftHours, ItemKind Fuel, ItemKind Detonator);

    public static ChargeSpec Mining => new(0.45f, 3f, 0.1f, 0.08f, 0.6f, ItemKind.Fuel, ItemKind.Electronics);

    /// <summary>채굴 한 발: 솜씨 · 바위 단단함 → 캔 양 · 불발 · 과폭 (원정 쪽 난수로 — 배의 결정론을 건드리지 않는다).</summary>
    public static (float yield, bool dud, bool over) MiningShot(Rng r, float skill, float rockHardness)
    {
        var s = Mining;
        bool dud = r.Chance(s.DudChance * (1.4f - 0.8f * skill));
        if (dud) return (0f, true, false);
        bool over = r.Chance(s.OverChance * (1.3f - 0.8f * skill));
        float y = s.Yield * (0.6f + 0.6f * skill) / MathF.Max(0.4f, rockHardness) * (over ? 0.6f : 1f);
        return (y, false, over);
    }

    // ───────────────────────────── 찾기 ─────────────────────────────

    public Explosive? Get(int id) { foreach (var e in All) if (e.Id == id) return e; return null; }

    public Explosive? At(Cell c, Explosive? self = null)
    {
        foreach (var e in All) if (!e.Spent && e.Carried < 0 && e.Cell == c && e != self) return e;
        return null;
    }

    public IEnumerable<Explosive> In(Room r) => All.Where(e => !e.Spent && _w.Ship.RoomAt(e.Cell) == r);

    internal void Experienced(CrewMember c, bool light = false)
    {
        int lv = light ? 1 : 2;
        if (!_experienced.TryGetValue(c.Id, out var o) || o < lv) _experienced[c.Id] = lv;
    }

    /// <summary>폭발을 겪은 사람 (가까이서 2 · 소리만 1): 폭발성 물건을 더 잘 알아본다.</summary>
    public int Experience(CrewMember c) => _experienced.TryGetValue(c.Id, out var v) ? v : 0;

    // ───────────────────────────── 맞추기 (재고 · 설비 · 이동식 장비 · 항아리) ─────────────────────────────

    private static readonly (ItemKind item, ExplosiveKind kind, float per, int min)[] Stock =
    {
        (ItemKind.Fuel, ExplosiveKind.FuelCan, 0.25f, 1), (ItemKind.CellPack, ExplosiveKind.BatteryCell, 0.2f, 1), (ItemKind.Solvent, ExplosiveKind.Solvent, 0.25f, 1),
        (ItemKind.Paint, ExplosiveKind.Aerosol, 0.2f, 1), (ItemKind.Sealant, ExplosiveKind.Aerosol, 0.15f, 1), (ItemKind.Extinguisher, ExplosiveKind.Extinguisher, 0.35f, 1),
        (ItemKind.Suit, ExplosiveKind.SuitO2Pack, 0.4f, 1), (ItemKind.Capacitor, ExplosiveKind.CapacitorPack, 0.2f, 1), (ItemKind.Lubricant, ExplosiveKind.Lubricant, 0.2f, 1),
        (ItemKind.Carbon, ExplosiveKind.Dust, 0.1f, 3), (ItemKind.MetalOre, ExplosiveKind.Dust, 0.1f, 3),
    };

    private readonly List<(string key, ExplosiveKind kind, Cell cell, float amount, bool inside, int container, ItemKind? item, int mount, int device, int batch)> _want = new();

    private void Want(string key, ExplosiveKind kind, Cell cell, float amount, bool inside = false, int container = -1, ItemKind? item = null, int mount = -1, int device = -1, int batch = -1)
    {
        foreach (var x in _want) if (x.key == key) return;
        _want.Add((key, kind, cell, amount, inside, container, item, mount, device, batch));
    }

    private Cell SpotNear(Furniture f, int salt)
    {
        var ship = _w.Ship;
        var cands = new List<Cell>();
        foreach (var fc in f.Cells)
            foreach (var d in Cell.Dirs8)
            {
                var c = fc + d;
                if (cands.Contains(c) || !ship.IsOpenFloor(c) || f.UseSpots.Contains(c) || ship.RoomAt(c) != f.Room) continue;
                bool taken = false;
                foreach (var e in All) if (!e.Spent && e.Cell == c) { taken = true; break; }
                if (!taken) cands.Add(c);
            }
        return cands.Count > 0 ? cands[Math.Abs(salt) % cands.Count] : f.Cells[0];
    }

    private Cell SpotIn(Room r, int salt)
    {
        var ship = _w.Ship;
        // 벽 곁 칸 (가운데를 막지 않게)
        var cands = r.Cells.Where(c => ship.IsOpenFloor(c) && Cell.Dirs4.Any(d => ship.Grid.Kind(c + d) == TileKind.Wall) && At(c) == null).ToList();
        if (cands.Count == 0) cands = r.Cells.Where(c => ship.IsOpenFloor(c)).ToList();
        return cands.Count > 0 ? cands[Math.Abs(salt) % cands.Count] : r.Cells[0];
    }

    /// <summary>원천과 맞춘다 (30분마다 · 폭발 뒤): 새 물건 · 양 · 자리(옮기지 않은 것만) · 사라진 원천.</summary>
    public void Sync()
    {
        var w = _w;
        var ship = w.Ship;
        _want.Clear();
        // 1) 보관함 재고
        foreach (var f in ship.Containers)
        {
            var inv = f.Storage!;
            for (int k = 0; k < Stock.Length; k++)
            {
                var (item, kind, per, min) = Stock[k];
                int n = inv.Count(item);
                if (n < min) continue;
                Want($"inv:{f.Id}:{(int)item}", kind, f.Cells[k % f.Cells.Count], MathF.Min(1.5f, per * n), inside: true, container: f.Id, item: item);
            }
        }
        // 2) 벽에 건 소화기
        foreach (var m in w.Body.Mounts)
            if (m.Kind == MountKind.Extinguisher && m.Present && ship.Grid.Kind(m.Spot) == TileKind.Floor) Want($"mount:{m.Id}", ExplosiveKind.Extinguisher, m.Spot, 0.5f, mount: m.Id);
        // 3) 설비 곁
        bool mining = false;
        foreach (var f in ship.Furniture)
        {
            if (f.Stowed || f.Room.Detached) continue;
            var r = f.Room;
            switch (f.Type)
            {
                case FurnitureType.OxygenGenerator:
                    Want($"fac:{f.Id}:h2", ExplosiveKind.HydrogenTank, SpotNear(f, f.Id), 0.6f);
                    Want($"room:{r.Id}:o2", ExplosiveKind.OxygenTank, SpotNear(f, f.Id + 3), 0.7f);
                    break;
                case FurnitureType.MedBed:
                    Want($"room:{r.Id}:o2", ExplosiveKind.OxygenTank, SpotNear(f, f.Id), 0.8f);
                    break;
                case FurnitureType.Stove or FurnitureType.Oven:
                    Want($"room:{r.Id}:gas", ExplosiveKind.GasCylinder, SpotNear(f, f.Id + 1), 0.6f);
                    Want($"room:{r.Id}:flour", ExplosiveKind.Dust, SpotIn(r, f.Id + 5), 0.5f, inside: true);
                    break;
                case FurnitureType.Workbench or FurnitureType.Lathe or FurnitureType.SolderStation:
                    Want($"room:{r.Id}:weld", ExplosiveKind.WeldingGas, SpotNear(f, f.Id + 2), 0.7f);
                    break;
                case FurnitureType.EngineCore:
                    Want($"fac:{f.Id}:prop", ExplosiveKind.Propellant, SpotNear(f, f.Id), 0.8f);
                    break;
                case FurnitureType.CoolantPump or FurnitureType.HeatExchanger or FurnitureType.WaterRecycler:
                    Want($"room:{r.Id}:vessel", ExplosiveKind.PressureVessel, SpotNear(f, f.Id), 0.6f);
                    break;
                case FurnitureType.Fridge:
                    Want($"room:{r.Id}:refr", ExplosiveKind.RefrigerantCan, SpotNear(f, f.Id + 4), 0.5f);
                    break;
                case FurnitureType.GrowBed:
                    Want($"room:{r.Id}:fert", ExplosiveKind.Fertilizer, SpotIn(r, r.Id * 3 + 1), 0.5f, inside: true);
                    Want($"room:{r.Id}:co2", ExplosiveKind.CO2Cylinder, SpotIn(r, r.Id * 3 + 2), 0.5f);
                    break;
                case FurnitureType.Refinery:
                    Want($"fac:{f.Id}:dust", ExplosiveKind.Dust, SpotNear(f, f.Id), 0.6f);
                    mining = true;
                    break;
                case FurnitureType.Collector:
                    mining = true;
                    break;
            }
        }
        // 4) 방 종류
        foreach (var r in ship.Rooms)
        {
            if (r.Detached) continue;
            switch (r.Kind)
            {
                case RoomType.Airlock or RoomType.EvaPrep or RoomType.Bridge: Want($"room:{r.Id}:flare", ExplosiveKind.Flare, SpotIn(r, r.Id), 0.5f, inside: true); break;
                case RoomType.Recycling: Want($"room:{r.Id}:ch4", ExplosiveKind.MethaneTank, SpotIn(r, r.Id), 0.6f); break;
                case RoomType.GasStorage: Want($"room:{r.Id}:o2", ExplosiveKind.OxygenTank, SpotIn(r, r.Id), 0.9f); Want($"room:{r.Id}:co2", ExplosiveKind.CO2Cylinder, SpotIn(r, r.Id + 1), 0.6f); break;
                case RoomType.PropellantTank: Want($"room:{r.Id}:prop", ExplosiveKind.Propellant, SpotIn(r, r.Id), 1f); break;
            }
        }
        if (mining && ship.Rooms.FirstOrDefault(r => !r.Detached && r.Kind is RoomType.Storage or RoomType.Cargo) is Room store)
            Want("mining", ExplosiveKind.Charge, SpotIn(store, store.Id + 7), 0.5f, inside: true);
        // 5) 이동식 배터리
        foreach (var d in w.Portable.Devices)
            if (d.Kind == PortableKind.Battery && !d.Lost && !d.Broken)
                Want($"dev:{d.Id}", ExplosiveKind.PortableBattery, d.HeldBy?.Cell ?? d.At, 0.3f + 0.7f * d.ChargeFrac, inside: d.Stored, device: d.Id);
        // 6) 발효 항아리
        foreach (var b in w.Cooking.Batches)
            if (b.Jar && !b.Spoiled && b.Portions > 0 && (b.Room ?? ship.RoomsOf(RoomType.Galley).FirstOrDefault()) is Room jr)
            {
                var anchor = jr.Furniture.FirstOrDefault(f => f.Type is FurnitureType.Fridge or FurnitureType.Stove or FurnitureType.Shelf or FurnitureType.Table);
                Want($"jar:{b.Id}", ExplosiveKind.FermentJar, anchor != null ? SpotNear(anchor, b.Id * 5) : SpotIn(jr, b.Id), 0.5f, batch: b.Id);
            }

        // 맞추기
        var keep = new HashSet<string>();
        foreach (var x in _want)
        {
            keep.Add(x.key);
            if (_gone.TryGetValue(x.key, out var until) && until > w.Tick) continue;
            var e = All.FirstOrDefault(a => a.Key == x.key && !a.Spent);
            if (e == null)
            {
                e = new Explosive
                {
                    Id = _next++, Kind = x.kind, Key = x.key, Cell = x.cell, Amount = x.amount, Inside = x.inside, Container = x.container, Item = x.item,
                    Mount = x.mount, Device = x.device, Batch = x.batch, Stability = 0.75f + 0.25f * R.Float(),
                };
                All.Add(e);
                continue;
            }
            e.Amount = x.amount;
            e.Inside = x.inside;
            if (!e.Primed && e.Carried < 0 && (!e.Moved || x.device >= 0)) e.Cell = x.cell;
        }
        All.RemoveAll(e => !e.Manual && !e.Spent && !e.Primed && e.Carried < 0 && !keep.Contains(e.Key));
        All.RemoveAll(e => e.Spent && w.Tick - e.SpentAt > SimTime.TicksPerDay);
    }

    // ───────────────────────────── 시스템 틱 ─────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        if (w.Tick >= _nextSync) { _nextSync = w.Tick + SimTime.Minutes(30); Sync(); }
        if (w.Tick >= _nextScan) { _nextScan = w.Tick + SimTime.Minutes(15); Scan(); }
        var ship = w.Ship;
        for (int i = 0; i < All.Count; i++)
        {
            var e = All[i];
            if (e.Spent) continue;
            var spec = e.Spec;
            // 들고 가는 중: 사람을 따라간다 (사람이 그 일을 놓으면 그 자리에 둔다)
            if (e.Carried >= 0)
            {
                var carrier = w.Crew.FirstOrDefault(c => c.Id == e.Carried);
                if (carrier == null || carrier.Dead || carrier.Down || carrier.Job == null) { e.Carried = -1; if (carrier != null) e.Cell = carrier.Cell; }
                else e.Cell = carrier.Cell;
            }
            var room = ship.RoomAt(e.Cell);
            float fire = 0f, flame = 0f;
            if (w.Fire.Count > 0) { flame = w.Fire.At(e.Cell); foreach (var d in Cell.Dirs8) fire = MathF.Max(fire, w.Fire.At(e.Cell + d)); fire = MathF.Max(fire, flame); }
            float temp = room?.Air.Temperature ?? -40f;
            float jet = 0f;
            foreach (var j in _b.Jets) { float jd = (j.at.Center - e.Cell.Center).Length(); if (jd < 2f) jet += j.heat * 10f * (1f - jd / 2f); }
            float heatIn = flame * 10f + fire * 1.5f + jet + MathF.Max(0f, temp - 45f) / 35f + e.NearHeat; // 불길에 바로 닿으면 몇 분 만에
            e.Heat = MathF.Max(0f, e.Heat + (spec.HeatRate * heatIn * (e.Inside ? 0.6f : 1f) - 0.6f * e.Heat) * dt);
            switch (e.Kind)
            {
                case ExplosiveKind.FermentJar:
                    e.Pressure = MathF.Min(1.2f, e.Pressure + (0.012f + MathF.Max(0f, temp - 18f) * 0.0012f + fire * 0.6f) * dt);
                    if (e.Pressure >= 1f && !e.Primed) Prime(e, "김을 안 빼서 부풀었다", 0);
                    break;
                case ExplosiveKind.Dust:
                    e.Cloud *= MathF.Exp(-dt * 6f);
                    if (e.Cloud > 0.3f && !e.Primed && room != null && Spark(room, e.Cell)) Prime(e, "날린 가루에 불꽃이 튀었다", e.Depth);
                    break;
            }
            if (!e.Primed && e.Heat >= spec.CookAt * (0.6f + 0.4f * e.Stability))
            {
                // 이 방에서 얼마 전 터진 폭발의 불이 달궜다면 그 연쇄의 한 단
                var cause = fire > 0.05f && room != null ? _b.Recent.LastOrDefault(r => r.Room == room.Id && w.Tick - r.Tick < SimTime.Hours(1)) : null;
                Prime(e, fire > 0.05f ? (cause != null ? $"{cause.Spec.Name} 불에 달았다" : "불에 달았다") : "달아올랐다", cause != null ? cause.Depth + 1 : 0);
                if (cause != null) Count(cause);
            }
            if (e.Primed && w.Tick >= e.FuseAt)
            {
                if (_budgetAt == w.Tick && _budget >= PerTick) { e.FuseAt = w.Tick + 1; Stats.Budgeted++; continue; }
                if (_budgetAt != w.Tick) { _budgetAt = w.Tick; _budget = 0; }
                _budget++;
                Go(e);
            }
        }
    }

    /// <summary>분진에 불꽃: 그 방의 불 · 아크 · 단락 · 불똥 (용접 · 폭발).</summary>
    private bool Spark(Room room, Cell at)
    {
        var w = _w;
        if (w.Fire.Count > 0 && w.Fire.CountIn(room) > 0) return true;
        foreach (var f in room.Furniture)
            if (f.Machine is Machine m && (m.Has(FaultKind.ShortCircuit) || m.Heat > 0.85f)) return true;
        return false;
    }

    /// <summary>쉭 · 아지랑이: 곧 터진다 (도화선). 곁의 사람이 알아채면 피한다.</summary>
    internal void Prime(Explosive e, string why, int depth)
    {
        var w = _w;
        var spec = e.Spec;
        if (e.Primed || e.Spent) return;
        float min = spec.FuseMin, max = MathF.Max(spec.FuseMin + 0.02f, spec.FuseMax);
        e.FuseAt = w.Tick + Math.Max(1, SimTime.Minutes(R.Range(min, max)));
        e.PrimedAt = w.Tick;
        e.Why = why;
        e.Depth = depth;
        Stats.Primed++;
        var room = w.Ship.RoomAt(e.Cell);
        if (spec.Power >= 0.1f) w.Log.Add(w.Tick, LogKind.Warning, $"{room?.Name ?? "?"}의 {spec.Name} — {why}" + (spec.Hiss ? " · 쉭 소리" : " · 아지랑이"));
        Notice(e, room);
    }

    /// <summary>
    /// 알아채기: 같은 방에서 깨어 있는 사람 — 쉭 소리는 귀로(이명이면 못 듣는다), 달아오른 통은 눈으로(눈부시면 못 본다).
    /// 알아챈 사람은 소리쳐 곁의 사람에게 알리고, 주 컴퓨터가 있으면 감지기로 읽어 대피 방송을 한다.
    /// </summary>
    private void Notice(Explosive e, Room? room)
    {
        var w = _w;
        var spec = e.Spec;
        if (room == null || spec.Power < 0.1f) return;
        CrewMember? shouter = null;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Down || !c.IsAwake || c.Room != room) continue;
            bool near = (c.Position - e.Cell.Center).Length() < 5f;
            bool sees = near && !_b.Dazzled(c) && (e.Heat > 0.5f || e.Kind == ExplosiveKind.Charge);
            bool hears = spec.Hiss && !_b.Deaf(c);
            if (!sees && !hears) { if (spec.Hiss) Stats.Unheard++; continue; }
            Know(c, e);
            Stats.Noticed++;
            shouter ??= c;
        }
        if (shouter != null)
        {
            shouter.Say(w, Persona.Say(shouter, $"피해! {Ko.IGa(spec.Name)} 터진다!"));
            foreach (var c in w.Crew)
                if (!c.Dead && c.Room != null && !_b.Deaf(c) && (c.Room == room || room.Doors.Any(d => d.Openness > 0.3f && (d.RoomA == c.Room || d.RoomB == c.Room)))) Know(c, e);
        }
        if (w.Automation.Present && w.Automation.MainOnline)
        {
            var b = w.Automation.Speak.Announce($"주 컴퓨터: {room.Name} {spec.Name} 과열 · 압력 이상 — 곁에서 떨어지십시오", room, 2);
            if (b != null)
            {
                Stats.ComputerAlerts++;
                foreach (int id in b.HeardBy) if (w.Crew.FirstOrDefault(x => x.Id == id) is CrewMember h && h.Room == room) Know(h, e);
            }
        }
    }

    private void Know(CrewMember c, Explosive e)
    {
        var w = _w;
        string key = $"boom:{e.Id}";
        if (c.Mind.Knows.ContainsKey(key)) return;
        c.Mind.Knows[key] = (KnowSource.Seen, w.Tick, $"{e.Spec.Name} — 곧 터진다");
        c.Interrupt(w);
    }

    /// <summary>안다 · 아직 위험 (몸을 피할 까닭).</summary>
    public Explosive? Threat(CrewMember c)
    {
        Explosive? best = null;
        float bestD = float.MaxValue;
        foreach (var e in All)
        {
            if (e.Spent) continue;
            bool live = e.Primed || e.Kind == ExplosiveKind.Charge && e.Order >= 0 && Breaches.FirstOrDefault(o => o.Id == e.Order) is { Stage: BreachOrder.Step.Clear or BreachOrder.Step.Countdown };
            if (!live || !c.Mind.Knows.ContainsKey($"boom:{e.Id}")) continue;
            float d = (c.Position - e.Cell.Center).Length();
            float danger = 1.5f + 4.2f * e.Power;
            if (d > danger) continue;
            if (d < bestD) { bestD = d; best = e; }
        }
        return best;
    }

    // ───────────────────────────── 터짐 ─────────────────────────────

    private void Go(Explosive e)
    {
        var w = _w;
        var spec = e.Spec;
        var room = w.Ship.RoomAt(e.Cell);
        // 폭약: 불발 · 과폭
        BreachOrder? order = e.Order >= 0 ? Breaches.FirstOrDefault(o => o.Id == e.Order) : null;
        if (e.Kind == ExplosiveKind.Charge && order != null)
        {
            float dud = 0.12f * (1.5f - e.Quality) * (e.Stability < 0.6f ? 2f : 1f) / (1f + order.Tries);
            if (R.Chance(dud))
            {
                e.FuseAt = -1; e.Armed = false;
                order.Stage = BreachOrder.Step.Dud;
                order.DudUntil = w.Tick + SimTime.Minutes(30);
                order.Tries++;
                Stats.Duds++;
                w.Log.Add(w.Tick, LogKind.Warning, $"폭약 불발 — {room?.Name ?? "?"} · 30분 기다렸다 다가간다");
                w.History.Add(w, HistoryKind.Response, $"{room?.Name ?? "?"}의 폭약이 불발했다 — 30분 기다렸다 다시 꽂는다", room, at: e.Cell);
                return;
            }
            order.Over = R.Chance(0.25f * (1.1f - e.Quality) + (order.Size > 0.5f ? 0.1f : 0f));
        }
        e.Spent = true;
        e.SpentAt = w.Tick;
        e.FuseAt = -1;
        Consume(e);
        _gone[e.Key] = w.Tick + SimTime.TicksPerDay * 3;
        float power = e.Power * (order?.Over == true ? 1.6f : 1f) * (room != null && room.Air.O2 > 22.5f && spec.Blast is BlastKind.Oxygen or BlastKind.Fuel or BlastKind.Gas or BlastKind.Acetylene or BlastKind.Dust ? 1.3f : 1f);
        // 너무 깊은 연쇄: 터지지 않고 탄다 (무한 연쇄 방지)
        if (e.Depth > MaxDepth)
        {
            Stats.Fizzled++;
            if (spec.Blast != BlastKind.Powder && w.Ship.Grid.Kind(e.Cell) == TileKind.Floor) w.Fire.Ignite(e.Cell, 0.35f);
            w.Log.Add(w.Tick, LogKind.Warning, $"{room?.Name ?? "?"}의 {Ko.IGa(spec.Name)} 터지지 않고 타 버렸다");
            return;
        }
        // 불에 단 것 중 일부는 터지지 않고 불기둥으로 탄다
        if (e.Kind != ExplosiveKind.Charge && e.Why.Contains("불에") && R.Chance(spec.Vent))
        {
            Stats.Vented++;
            foreach (var d in Cell.Dirs4.Prepend(new Cell(0, 0))) if (w.Ship.Grid.Kind(e.Cell + d) == TileKind.Floor) w.Fire.Ignite(e.Cell + d, 0.5f);
            _b.Detonate(e.Cell, power * 0.25f, spec.Blast, $"{spec.Name} 불기둥 ({e.Why})", null, e.Depth, e.Handler, e);
            return;
        }
        var rec = _b.Detonate(e.Cell, power, spec.Blast, $"{spec.Name} ({e.Why})", null, e.Depth, e.Handler, e);
        if (order != null && rec != null) Breached(order, e, rec);
    }

    /// <summary>터진 물건의 원천을 비운다: 재고 · 벽 걸이 · 이동식 배터리 · 항아리.</summary>
    private void Consume(Explosive e)
    {
        var w = _w;
        if (e.Container >= 0 && e.Item is ItemKind ik && w.Ship.Furniture.FirstOrDefault(f => f.Id == e.Container)?.Storage is Inventory inv) inv.Take(ik, inv.Count(ik));
        if (e.Mount >= 0 && w.Body.Mounts.FirstOrDefault(m => m.Id == e.Mount) is WallMount wm) { wm.Present = false; wm.TakenBy = -1; }
        if (e.Device >= 0 && w.Portable.Devices.FirstOrDefault(d => d.Id == e.Device) is PortableDevice pd) { pd.Broken = true; pd.Charge = 0f; pd.On = false; }
        if (e.Batch >= 0 && w.Cooking.Batches.FirstOrDefault(b => b.Id == e.Batch) is Batch bt) { bt.Spoiled = true; bt.SpoiledAt = w.Tick; bt.Portions = 0; }
    }

    /// <summary>폭발이 곁의 물건을 흔들고 달군다 → 연쇄 (물건마다 한 번 · 깊이 제한).</summary>
    internal void OnBlast(BlastRecord rec, Explosive? self)
    {
        var w = _w;
        var bs = BlastSystem.Spec(rec.Kind);
        for (int i = 0; i < All.Count; i++)
        {
            var e = All[i];
            if (e.Spent || e == self || e.Carried >= 0) continue;
            float p = _b.PAt(e.Cell) * (e.Inside ? 0.7f : 1f);
            if (p < 0.04f) continue;
            var spec = e.Spec;
            e.Stability = MathF.Max(0f, e.Stability - p * 0.5f);
            e.Heat += p * MathF.Max(0f, bs.Heat) * 1.2f;
            if (e.Kind == ExplosiveKind.Dust)
            {
                // 첫 폭발이 가라앉은 가루를 날리고 → 그 불길이 가루에 옮겨 붙는다 (2차 분진 폭발)
                e.Cloud = MathF.Max(e.Cloud, MathF.Min(1f, p * 2.5f));
                if (bs.Heat > 0.3f && p > 0.08f && !e.Primed && rec.Depth < MaxDepth) { Prime(e, $"{Ko.IGa(bs.Name)} 날린 가루에 불이 붙었다", rec.Depth + 1); Count(rec); }
                continue;
            }
            if (e.Kind == ExplosiveKind.FermentJar && p > 0.2f) { Shatter(e, $"{bs.Name}에 깨졌다"); continue; }
            if (e.Primed || rec.Depth >= MaxDepth) continue;
            if (p >= spec.ShockAt * (0.5f + 0.5f * e.Stability)) { Prime(e, $"{bs.Name}에 맞았다", rec.Depth + 1); e.FuseAt = Math.Min(e.FuseAt, w.Tick + Math.Max(1, SimTime.Minutes(R.Range(0.05f, 0.4f)))); Count(rec); }
            else if (spec.Hiss && e.Kind is ExplosiveKind.BatteryCell or ExplosiveKind.PortableBattery && p > 0.15f && R.Chance(p * 1.5f)) { Prime(e, "압력에 셀이 눌렸다", rec.Depth + 1); Count(rec); }
        }
    }

    private void Count(BlastRecord rec) { rec.Chained++; _b.Stats.Chains++; }

    /// <summary>파편이 물건을 맞혔다: 뚫리면 곧 터진다.</summary>
    internal void OnShard(Explosive e, float s, string cause)
    {
        e.Stability = MathF.Max(0f, e.Stability - s);
        if (e.Kind == ExplosiveKind.FermentJar) { Shatter(e, "파편에 깨졌다"); return; }
        if (e.Primed || e.Spent || _b.DepthNow >= MaxDepth) return;
        if (R.Chance(e.Spec.Pierce * MathF.Min(1f, s * 1.6f)))
        {
            Prime(e, $"파편에 뚫렸다 ({cause})", _b.DepthNow + 1);
            e.FuseAt = Math.Min(e.FuseAt, _w.Tick + Math.Max(1, SimTime.Minutes(R.Range(0.05f, 0.6f))));
            _b.Stats.Chains++;
        }
    }

    /// <summary>항아리가 깨졌다: 국물이 쏟아지고 냄새 · 유리 조각 (터지진 않는다).</summary>
    private void Shatter(Explosive e, string why)
    {
        var w = _w;
        e.Spent = true; e.SpentAt = w.Tick; e.FuseAt = -1;
        Consume(e);
        _gone[e.Key] = w.Tick + SimTime.TicksPerDay * 3;
        Stats.Shattered++;
        w.Body.RaiseMark(e.Cell, CellMark.Wet, 0.8f, "깨진 항아리 국물");
        w.Body.RaiseMark(e.Cell, CellMark.Glass, 0.6f, "깨진 항아리");
        w.Smells.Emit(w.Ship.RoomAt(e.Cell), SmellKind.Foul, 0.5f);
        w.Log.Add(w.Tick, LogKind.Warning, $"발효 항아리가 깨졌다 — {why} · 냄새가 번진다");
    }

    // ───────────────────────────── 위험 배치 읽기 (주 컴퓨터 · 승무원) ─────────────────────────────

    private readonly List<(Cell cell, float value, string name)> _sources = new();

    /// <summary>15분마다: 열원 곁 · 연쇄 무리 · 달아오름 · 상함 → 위험도. 주 컴퓨터가 읽고 경고 · 제안, 사람은 지나가다 알아챈다.</summary>
    private void Scan()
    {
        var w = _w;
        var ship = w.Ship;
        _sources.Clear();
        foreach (var d in w.Portable.Devices)
            if (d.Kind == PortableKind.Heater && d.Placed && d.On && !d.Broken) _sources.Add((d.At, 0.5f, "이동식 히터"));
        foreach (var f in ship.Furniture)
        {
            if (f.Stowed || f.Room.Detached) continue;
            if (f.Type is FurnitureType.Stove or FurnitureType.Oven) _sources.Add((Cell.FromPosition(f.Center), 0.35f, f.Label.Length > 0 ? f.Label : "조리대"));
            else if (f.Machine is Machine m && m.Heat > 0.55f) _sources.Add((Cell.FromPosition(f.Center), 0.45f, $"달아오른 {m.Name}"));
        }
        if (w.Fire.Count > 0) foreach (var (fc, _) in w.Fire.Fires) _sources.Add((fc, 0.7f, "불"));
        foreach (var e in All)
        {
            if (e.Spent) continue;
            var why = new List<string>();
            float risk = 0f, near = 0f;
            foreach (var (sc, v, name) in _sources)
            {
                float d = (sc.Center - e.Cell.Center).Length();
                if (d > 2.6f) continue;
                near += 0.4f * v * (1f - d / 2.7f);
                if (!why.Contains($"{name} 곁")) { risk += v; why.Add($"{name} 곁"); }
            }
            e.NearHeat = e.Inside ? near * 0.5f : near;
            if (e.Heat > 0.35f) { risk += 0.5f; why.Add($"달아오른다 {e.Heat * 100:0}%"); }
            if (e.Kind == ExplosiveKind.FermentJar && e.Pressure > 0.6f) { risk += 0.3f; why.Add("항아리가 부풀었다"); }
            if (e.Power >= 0.25f)
            {
                var mates = All.Where(o => o != e && !o.Spent && o.Power >= 0.2f && !(o.Inside && e.Inside) && (o.Cell.Center - e.Cell.Center).Length() <= 2.2f).Select(o => o.Spec.Name).Distinct().ToList();
                if (mates.Count > 0) { risk += 0.2f + 0.1f * mates.Count; why.Add($"곁에 {string.Join("·", mates)} — 연쇄"); }
            }
            if (e.Stability < 0.5f) { risk += 0.2f; why.Add("상했다"); }
            if (ship.RoomAt(e.Cell) is Room rr && rr.Air.Temperature > 40f) { risk += 0.3f; why.Add($"방이 {rr.Air.Temperature:0}℃"); }
            e.Risk = MathF.Min(1.5f, risk * (e.Kind == ExplosiveKind.FermentJar ? 0.5f : 1f) * MathF.Min(1.2f, 0.4f + e.Power * 1.5f));
            e.RiskWhy = string.Join(" · ", why);
            // 눈에 보이게 달아오르면 곁의 사람이 안다 (피한다)
            if (e.Heat > 0.55f)
                foreach (var c in w.Crew)
                    if (!c.Dead && c.IsAwake && c.Room != null && c.Room == ship.RoomAt(e.Cell) && !_b.Dazzled(c) && (c.Position - e.Cell.Center).Length() < 5f) Know(c, e);
        }
        Computer();
        CrewNotice();
        Secures.RemoveAll(o => o.Done || Get(o.Explosive) is not { Spent: false });
        Breaches.RemoveAll(o => (o.Stage is BreachOrder.Step.Done or BreachOrder.Step.Failed) && w.Tick - o.Since > SimTime.TicksPerDay * 2);
    }

    /// <summary>
    /// 주 컴퓨터가 위험 배치를 읽는다: 가장 위험한 것 하나를 골라 경고하고, 옮기기를 제안한다
    /// (방침이 "묻는다"면 제안 — 받으면 옮기는 일이 생긴다 · 아니면 바로 지시).
    /// </summary>
    private void Computer()
    {
        var w = _w;
        var a = w.Automation;
        // 이미 낸 제안: 받았으면 일로
        foreach (var (eid, key) in _asked.ToList())
        {
            var p = a.Asks.Latest(key);
            if (p == null || p.State == ProposalState.Pending) continue;
            _asked.Remove(eid);
            if (p.Accepted && Get(eid) is Explosive pe && !pe.Spent) Order(pe, $"주 컴퓨터 제안을 받았다 — {pe.RiskWhy}", "주 컴퓨터");
        }
        if (!a.Present || !a.MainOnline) return;
        Explosive? top = null;
        foreach (var e in All)
        {
            if (e.Spent || e.Carried >= 0 || e.Risk < 0.5f || w.Tick - e.WarnedAt < SimTime.Hours(6) || Secures.Any(o => o.Explosive == e.Id && !o.Done) || _asked.ContainsKey(e.Id)) continue;
            if (top == null || e.Risk > top.Risk) top = e;
        }
        if (top == null) return;
        top.WarnedAt = w.Tick;
        Stats.ComputerWarnings++;
        var room = w.Ship.RoomAt(top.Cell);
        string what = $"{room?.Name ?? "?"}의 {top.Spec.Name}";
        if (a.Asks.Needed("explosive"))
        {
            string key = $"blast-risk:{top.Id}";
            a.Asks.Propose(key, "explosive", room, $"{Ko.EulReul(what)} 안전한 곳으로 옮긴다", top.RiskWhy, "연쇄 · 과열 위험이 준다", 20f, null);
            _asked[top.Id] = key;
            Stats.Proposals++;
        }
        else
        {
            a.Speak.Announce($"주 컴퓨터: {what} — {top.RiskWhy}. 옮기기를 권한다", room, 1);
            Order(top, $"주 컴퓨터 경고 — {top.RiskWhy}", "주 컴퓨터");
        }
        w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터가 폭발 위험을 읽었다 — {what} ({top.RiskWhy})");
    }

    /// <summary>지나가던 사람이 알아챈다 (부지런할수록 · 폭발을 겪은 사람일수록).</summary>
    private void CrewNotice()
    {
        var w = _w;
        foreach (var e in All)
        {
            if (e.Spent || e.Carried >= 0 || e.Risk < 0.3f || Secures.Any(o => o.Explosive == e.Id && !o.Done)) continue;
            var room = w.Ship.RoomAt(e.Cell);
            if (room == null) continue;
            foreach (var c in w.Crew)
            {
                if (c.Dead || !c.CanAct || !c.IsAwake || c.Room != room || c.Job?.Urgent == true || c.IsChild) continue;
                int xp = Experience(c);
                if (e.Risk < 0.45f && xp == 0) continue; // 겪어 본 사람은 덜 위험해 보여도 알아본다
                float ch = 0.08f * (0.5f + c.Traits.Diligence) * (xp >= 2 ? 3f : xp == 1 ? 1.8f : 1f);
                if (!R.Chance(ch)) continue;
                Stats.CrewNoticed++;
                Order(e, $"{Ko.IGa(c.Name)} 알아챘다 — {e.RiskWhy}", c.Name);
                c.Say(w, Persona.Say(c, xp > 0 ? $"{Ko.EulReul(e.Spec.Name)} 저기 두면 안 돼 — 그때 같은 일 난다" : $"{Ko.EulReul(e.Spec.Name)} 여기 두면 위험한데"));
                break;
            }
        }
    }

    private void Order(Explosive e, string why, string by)
    {
        if (Secures.Any(o => o.Explosive == e.Id && !o.Done)) return;
        Secures.Add(new SecureOrder { Id = _nextOrder++, Explosive = e.Id, Why = why, By = by, Since = _w.Tick });
    }

    /// <summary>조사 뒤: 그 방의 위험한 배치를 손본다.</summary>
    internal void SafetySweep(Room room, string why)
    {
        foreach (var e in In(room).ToList())
            if (e.Risk >= 0.3f && e.Carried < 0 && !e.Primed) Order(e, $"{why} — {e.RiskWhy}", why);
    }

    /// <summary>안전한 자리: 열원 · 다른 폭발성 물건에서 멀리 (같은 방에 없으면 창고).</summary>
    public Cell? SafeSpot(Explosive e)
    {
        var w = _w;
        var ship = w.Ship;
        Cell? best = null;
        float bestScore = -1f;
        var home = ship.RoomAt(e.Cell);
        foreach (var room in new[] { home, ship.RoomsOf(RoomType.Storage).FirstOrDefault() })
        {
            if (room == null || room.Detached || CosmicEvacuateActivity.Emptying(room, w)) continue; // 우주 대재난으로 비우는 구획엔 두지 않는다
            foreach (var c in room.Cells)
            {
                if (!ship.IsOpenFloor(c) || At(c, e) != null || c == e.Cell) continue;
                float score = 6f;
                foreach (var (sc, _, _) in _sources) score = MathF.Min(score, (sc.Center - c.Center).Length());
                foreach (var o in All) if (o != e && !o.Spent && o.Power >= 0.2f) score = MathF.Min(score, (o.Cell.Center - c.Center).Length() + 0.3f);
                bool wall = Cell.Dirs4.Any(d => ship.Grid.Kind(c + d) == TileKind.Wall);
                score += wall ? 0.4f : 0f;
                if (score > bestScore) { bestScore = score; best = c; }
            }
            if (bestScore >= 3f) break;
        }
        return best;
    }

    // ───────────────────────────── 의도적 폭파 ─────────────────────────────

    /// <summary>폭파 주문: 막힌 문 · 잔해 뚫기 · 비상 분리 (작업대에서 폭약을 만들어 설치 → 대피 → 카운트다운).</summary>
    public BreachOrder? RequestBreach(Cell target, BreachKind kind, string why, float size = 0.4f)
    {
        var w = _w;
        int door = kind == BreachKind.Door ? w.Ship.DoorAt(target)?.Id ?? -1 : -1;
        int room = kind == BreachKind.Detach ? w.Ship.RoomAt(target)?.Id ?? -1 : -1;
        if (kind == BreachKind.Door && door < 0 || kind == BreachKind.Detach && room < 0) return null;
        if (Breaches.Any(o => o.Target == target && o.Stage < BreachOrder.Step.Done)) return null;
        var o = new BreachOrder { Id = _nextOrder++, Kind = kind, Target = target, DoorId = door, RoomId = room, Why = why, Size = Math.Clamp(size, 0.15f, 0.8f), Since = w.Tick };
        Breaches.Add(o);
        w.Log.Add(w.Tick, LogKind.Ship, $"폭파 결정 — {why} ({(kind == BreachKind.Door ? "문" : kind == BreachKind.Rubble ? "잔해" : "비상 분리")})");
        w.History.Add(w, HistoryKind.Decision, $"폭약으로 뚫기로 했다 — {why}", w.Ship.RoomAt(target), at: target);
        return o;
    }

    /// <summary>작업대에서 폭약을 만든다 (재료가 없으면 임시변통 — 불발이 잦다).</summary>
    internal Explosive Craft(BreachOrder o, CrewMember c)
    {
        var w = _w;
        bool fuel = false, det = false;
        foreach (var f in w.Ship.Containers)
        {
            if (!fuel && (f.Storage!.Take(ItemKind.Fuel, 1) > 0 || f.Storage.Take(ItemKind.Solvent, 1) > 0)) fuel = true;
            if (!det && (f.Storage!.Take(ItemKind.Electronics, 1) > 0 || f.Storage.Take(ItemKind.Cable, 1) > 0)) det = true;
            if (fuel && det) break;
        }
        float q = Math.Clamp(0.35f + 0.6f * c.SkillLevel(Skill.Engineering) + (fuel ? 0.1f : -0.2f) + (det ? 0.1f : -0.25f), 0.1f, 1f);
        var e = new Explosive
        {
            Id = _next++, Kind = ExplosiveKind.Charge, Key = $"charge:{o.Id}", Cell = c.Cell, Amount = o.Size / 0.6f, Manual = true, Carried = c.Id, Handler = c.Id,
            Quality = q, Aim = o.Target, Order = o.Id, Stability = 0.9f,
        };
        All.Add(e);
        o.ChargeId = e.Id;
        o.Stage = BreachOrder.Step.Place;
        Stats.Crafted++;
        w.Log.Add(w.Tick, LogKind.Work, $"작업대에서 폭약을 만들었다 (솜씨 {q * 100:0}%" + (!fuel || !det ? " · 재료가 모자라 임시변통" : "") + ")", c.Id);
        return e;
    }

    /// <summary>설치할 칸: 겨눈 곳 곁 바닥 (이쪽에서 닿는 쪽).</summary>
    public Cell? PlaceSpot(BreachOrder o, DistanceField? dist = null)
    {
        var ship = _w.Ship;
        if (o.Kind == BreachKind.Rubble && ship.IsWalkable(o.Target) && (dist == null || dist.Reachable(o.Target))) return o.Target;
        foreach (var dirs in new[] { Cell.Dirs4, Cell.Dirs8 })
            foreach (var d in dirs)
            {
                var c = o.Target + d;
                if (ship.Grid.Kind(c) == TileKind.Floor && ship.FurnitureAt(c) == null && (dist == null || dist.Reachable(c))) return c;
            }
        return null;
    }

    /// <summary>물건을 놓는다 (시험 · 다른 시스템 — 원정에서 가져온 폭약 · 옮겨 온 통).</summary>
    public Explosive Place(ExplosiveKind kind, Cell cell, float amount = 1f, int handler = -1)
    {
        var e = new Explosive { Id = _next++, Kind = kind, Key = $"placed:{_next}", Cell = cell, Amount = amount, Manual = true, Handler = handler, Stability = 0.9f };
        All.Add(e);
        return e;
    }

    /// <summary>불을 댄다 (시험 · 다른 시스템): 쉭 소리 · 아지랑이 뒤에 터진다.</summary>
    public void Ignite(Explosive e, string why) => Prime(e, why, 0);

    /// <summary>대피할 칸: 미리 본 압력파가 거의 닿지 않는 가장 가까운 칸 (닿을 수 있는 곳만).</summary>
    public Cell? CoverSpot(Cell from, float power, BlastKind kind, DistanceField dist, CrewMember who)
    {
        var w = _w;
        var ship = w.Ship;
        var map = _b.Preview(from, power, kind);
        var g = ship.Grid;
        Cell? best = null;
        int bestCost = int.MaxValue;
        foreach (var room in ship.Rooms)
        {
            if (room.Detached) continue;
            foreach (var c in room.Cells)
            {
                if (!ship.IsWalkable(c) || ship.Grid.Kind(c) != TileKind.Floor) continue;
                if (map.TryGetValue(g.Index(c), out float v) && v > 0.03f) continue;
                if ((c.Center - from.Center).Length() < 3f) continue;
                int d = dist.Get(c);
                if (d < 0 || d >= bestCost || w.IsSpotTaken(c, who)) continue;
                best = c; bestCost = d;
            }
        }
        return best;
    }

    private Dictionary<int, float>? _zoneMap;
    private int _zoneFor = -1;
    private Cell _zoneCell;
    private long _zoneAt;

    /// <summary>그 구역에 다른 사람이 없나 (폭파 전 대피 확인).</summary>
    public bool ZoneClear(Explosive e, CrewMember placer, out List<CrewMember> inside)
    {
        var w = _w;
        if (_zoneFor != e.Id || _zoneCell != e.Cell || w.Tick - _zoneAt > 10) { _zoneMap = _b.Preview(e.Cell, e.Power, BlastKind.Charge); _zoneFor = e.Id; _zoneCell = e.Cell; _zoneAt = w.Tick; }
        var map = _zoneMap!;
        var g = w.Ship.Grid;
        inside = new List<CrewMember>();
        foreach (var c in w.Crew)
        {
            if (c.Dead || c == placer || c.Outside) continue;
            if (map.TryGetValue(g.Index(c.Cell), out float v) && v > 0.06f) inside.Add(c);
        }
        return inside.Count == 0;
    }

    /// <summary>비키라고 외친다: 그 구역 사람이 폭약을 알고 몸을 피한다.</summary>
    internal void Warn(Explosive e, CrewMember placer, List<CrewMember> inside)
    {
        var w = _w;
        placer.Say(w, Persona.Say(placer, "폭파한다 — 비켜!"));
        foreach (var c in inside)
        {
            if (_b.Deaf(c) && c.Room != placer.Room) continue; // 귀가 울리면 외침을 못 듣는다
            Know(c, e);
            Stats.Cleared++;
        }
    }

    internal void Arm(BreachOrder o, Explosive e, CrewMember c)
    {
        var w = _w;
        e.Armed = true;
        o.Stage = BreachOrder.Step.Countdown;
        o.Placer = c.Id;
        e.Handler = c.Id;
        e.FuseAt = w.Tick + SimTime.Minutes(1f);
        e.PrimedAt = w.Tick;
        e.Why = $"{o.Why} — {Ko.IGa(c.Name)} 기폭";
        e.Depth = 0;
        c.Say(w, Persona.Say(c, "셋 · 둘 · 하나!"));
        w.Log.Add(w.Tick, LogKind.Work, $"폭약 카운트다운 — {o.Why}", c.Id);
        foreach (var x in w.Crew) if (!x.Dead && x.Room != null && x.Room == w.Ship.RoomAt(e.Cell) && !_b.Deaf(x)) Know(x, e);
    }

    /// <summary>터진 뒤 겨눈 것: 문은 날아가고 · 잔해는 치워지고 · 분리 고리는 끊긴다 (과폭이면 곁도 상한다).</summary>
    private void Breached(BreachOrder o, Explosive e, BlastRecord rec)
    {
        var w = _w;
        var ship = w.Ship;
        o.Stage = BreachOrder.Step.Done;
        var placer = w.Crew.FirstOrDefault(c => c.Id == o.Placer);
        switch (o.Kind)
        {
            case BreachKind.Door when o.DoorId >= 0 && o.DoorId < ship.Doors.Count:
                var d = ship.Doors[o.DoorId];
                d.JammedOpen = true; d.Welded = false; d.Locked = false; d.Blocked = false; d.MotorBroken = true; d.Openness = 1f; d.Bent = 1f;
                _b.MarkBlown(d);
                ship.Rubble.Remove(d.Cell);
                if (w.Body.DoorOf(d) is DoorBody db) db.Gasket = 0f;
                o.Result = "문을 뚫었다";
                break;
            case BreachKind.Rubble:
                foreach (var c in ship.Rubble.Keys.Where(c => (c.Center - o.Target.Center).Length() <= 1.6f).ToList())
                {
                    ship.Rubble.Remove(c);
                    if (ship.DoorAt(c) is Door dd) dd.Blocked = false;
                }
                o.Result = "잔해를 날려 길을 냈다";
                break;
            case BreachKind.Detach when o.RoomId >= 0 && !ship.Rooms[o.RoomId].Detached:
                w.Structure.Detach(ship.Rooms[o.RoomId], "폭약으로 비상 분리", controlled: true);
                o.Result = "고리를 끊어 비상 분리했다";
                break;
        }
        if (o.Over)
        {
            Stats.Overcharges++;
            foreach (var (cell, _) in ship.Walls.ToList())
                if ((cell.Center - e.Cell.Center).Length() <= 1.8f) Hull.Damage(ship, cell, 0.35f);
            o.Result += " — 과폭으로 곁의 벽까지 상했다";
            rec.Blame = o.Placer;
            rec.BlameWhy = "폭약을 너무 세게 쟀다";
        }
        Stats.Breached++;
        w.Paths.Invalidate();
        w.History.Add(w, HistoryKind.Response, $"폭약으로 {o.Result} ({o.Why})" + (placer != null ? $" — {placer.Name}" : ""), ship.RoomAt(o.Target), placer != null ? new[] { placer } : null, o.Target);
        if (placer != null) Life.Diary(w, placer, o.Over ? "폭약을 너무 세게 쟀다. 벽까지 상했다." : $"폭약으로 {o.Result}.");
    }

    // ───────────────────────────── 지문 ─────────────────────────────

    public void Hash(Action<long> I, Action<float> F)
    {
        I(All.Count); I(Stats.Primed); I(Stats.Secured); I(Stats.ComputerWarnings); I(Stats.Breached);
        foreach (var e in All) { I(e.Id); I(e.Cell.X); I(e.Cell.Y); F(e.Heat); F(e.Amount); I(e.Spent ? 1 : 0); }
    }
}

// ═══════════════════════════════ 행동 ═══════════════════════════════

/// <summary>쉭 소리 · 달아오른 통 · 카운트다운을 알면 그 자리를 피한다 (닫힌 문 너머 · 압력파가 닿지 않는 칸).</summary>
public sealed class TakeCoverActivity : Activity
{
    public override string Id => "takecover";
    public override string Label => "몸을 피함";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (c.Outside || c.Down || c.Room == null) return (0f, "—");
        var e = w.Blast.Items.Threat(c);
        if (e == null) return (0f, "—");
        if (e.Kind == ExplosiveKind.Charge && w.Blast.Items.Breaches.FirstOrDefault(o => o.Id == e.Order)?.ClaimedBy == c.Id) return (0f, "내가 설치한 폭약");
        return (e.Primed ? 1.45f : 1.1f, $"{e.Spec.Name} — {(e.Spec.Hiss ? "쉭 소리" : e.Kind == ExplosiveKind.Charge ? "폭파 준비" : "달아올랐다")}, 피한다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var items = w.Blast.Items;
        var e = items.Threat(c);
        if (e == null) return null;
        var spot = items.CoverSpot(e.Cell, MathF.Max(0.3f, e.Power), e.Spec.Blast, dist, c);
        if (spot is not Cell s) return null;
        items.Stats.TookCover++;
        w.Blast.Stats.Avoided++;
        return new Job(this, "몸을 피함", new Toil[]
        {
            new GotoToil(s),
            new WaitToil(SimTime.Minutes(15), Pose.Sitting, e.Cell.Center) { DoneWhen = (cm, world) => e.Spent || !e.Primed && e.Heat < 0.4f && !(e.Kind == ExplosiveKind.Charge && !e.Spent) },
        })
        { Urgent = true, LogText = $"{e.Spec.Name}에서 몸을 피했다", LogKind = LogKind.Warning, InterruptMargin = 0.4f };
    }
}

/// <summary>폭발 뒤 · 폭발 전의 일: 다친 사람 살피기(구조) · 현장 조사 · 위험한 자리의 물건 옮기기 · 발효 항아리 김 빼기 · 폭발 자리 추모.</summary>
public sealed class BlastResponseActivity : Activity
{
    public override string Id => "blastresponse";
    public override string Label => "폭발 대응";

    private enum Task { Rescue, Investigate, Secure, Burp, Mourn }

    private static (Task task, object target, Cell spot, float score, string why)? Pick(CrewMember c, World w, DistanceField dist)
    {
        var b = w.Blast;
        var ship = w.Ship;
        (Task, object, Cell, float, string)? best = null;
        float bestScore = 0f;
        void Consider(Task t, object target, Cell at, float value, string why)
        {
            var spot = Near(w, at, dist);
            if (spot is not Cell s) return;
            // 우주 대재난으로 비우는 구획 · 파편 줄에서는 구조 말고는 손대지 않는다 (옮기다 파편을 맞는다)
            if (t != Task.Rescue && (ship.RoomAt(at) is Room ar && CosmicEvacuateActivity.Emptying(ar, w) || ship.RoomAt(s) is Room sr && CosmicEvacuateActivity.Emptying(sr, w))) return;
            if (!CrisisCrewSystem.Off && ship.RoomAt(at) is Room vr && (vr.Purging || vr.Inerting || vr.EvacuateBy >= 0)) return; // v16.21 소화 경보로 비우는 방에는 다시 들어가지 않는다
            float sc = value - dist.Get(s) / 4000f;
            if (sc > bestScore) { bestScore = sc; best = (t, target, s, sc, why); }
        }
        foreach (var rec in b.Recent)
        {
            long age = w.Tick - rec.Tick;
            if (age > SimTime.Hours(12)) continue;
            bool knows = rec.Heard.Contains(c.Id) || c.Mind.Knows.ContainsKey($"blast:{rec.Id}") || w.Alerts.Any(a => a.Tick >= rec.Tick && a.Tick - rec.Tick < 30);
            if (!knows) continue;
            var room = rec.Room >= 0 ? ship.Rooms[rec.Room] : null;
            bool safe = room != null && !room.Leaking && Atmosphere.Danger(room) < 0.15f && w.Fire.CountIn(room) == 0;
            if (age < SimTime.Hours(2)) // 불 · 연기가 걷힐 때까지 기다렸다가도 살핀다
                foreach (int id in rec.Hurt.Concat(rec.Fell).Distinct())
                {
                    if (id == c.Id || b.Helped.Contains((rec.Id, id))) continue;
                    if (b.RescueBy.TryGetValue((rec.Id, id), out int by) && by != c.Id && w.Crew.FirstOrDefault(x => x.Id == by) is CrewMember r0 && r0.Job?.Activity is BlastResponseActivity) continue; // 다른 사람이 이미 달려가고 있다
                    var v = w.Crew.FirstOrDefault(x => x.Id == id);
                    if (v == null || v.Dead || v.Outside || v.CarriedBy != null) continue;
                    if (w.Fire.AnyWithin(v.Cell, 1.2f) || v.Room != null && Atmosphere.Danger(v.Room) > 0.6f) continue;
                    Consider(Task.Rescue, (rec, v), v.Cell, 1.1f + 0.2f * c.AffinityTo(v), $"폭발에 다친 {Ko.EulReul(v.Name)} 살핀다");
                }
            if (!rec.Investigated && (rec.Investigator < 0 || rec.Investigator == c.Id) && age >= SimTime.Minutes(30) && safe && rec.Power >= 0.1f)
                Consider(Task.Investigate, rec, rec.At, (age < SimTime.Hours(6) ? 0.8f : 0.5f) + 0.35f * c.SkillLevel(Skill.Engineering) + (rec.Hurt.Count > 0 ? 0.25f : 0f), $"{room!.Name} 폭발 자리를 살핀다 — 왜 터졌나");
        }
        foreach (var o in b.Items.Secures)
        {
            if (o.Done || o.ClaimedBy >= 0 && o.ClaimedBy != c.Id) continue;
            if (b.Items.Get(o.Explosive) is not Explosive e || e.Spent || e.Primed || e.Carried >= 0) continue;
            Consider(Task.Secure, o, e.Cell, 0.62f, $"{Ko.EulReul(e.Spec.Name)} 안전한 곳으로 ({o.By})");
        }
        foreach (var e in b.Items.All)
            if (!e.Spent && e.Kind == ExplosiveKind.FermentJar && e.Pressure > 0.55f && !e.Primed && e.Carried < 0)
                Consider(Task.Burp, e, e.Cell, 0.28f + 0.25f * c.SkillLevel(Skill.Cooking) + (e.Pressure > 0.85f ? 0.25f : 0f), "부푼 발효 항아리 김을 뺀다");
        foreach (var s in b.Scars)
            if (s.Memorial && !s.Visited.Contains(c.Id) && w.Tick - s.Tick > SimTime.Hours(14))
                Consider(Task.Mourn, s, s.At, 0.2f, "폭발 자리에 들러 묵념한다");
        return best;
    }

    private static Cell? Near(World w, Cell at, DistanceField dist)
    {
        if (w.Ship.IsWalkable(at) && w.Ship.Grid.Kind(at) == TileKind.Floor && dist.Reachable(at) && w.Ship.FurnitureAt(at) == null) return at;
        foreach (var d in Cell.Dirs8)
        {
            var c = at + d;
            if (w.Ship.IsWalkable(c) && w.Ship.Grid.Kind(c) == TileKind.Floor && dist.Reachable(c)) return c;
        }
        return null;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (c.Outside || c.IsChild && c.Age < 10f || !c.CanAct) return (0f, "—");
        if (w.Blast.Recent.Count == 0 && w.Blast.Items.Secures.Count == 0 && w.Blast.Scars.Count == 0 && !w.Blast.Items.All.Any(e => e.Kind == ExplosiveKind.FermentJar && e.Pressure > 0.55f)) return (0f, "—");
        var p = Pick(c, w, dist);
        if (p == null) return (0f, "—");
        float s = p.Value.score;
        if (p.Value.task is Task.Burp or Task.Mourn or Task.Investigate && Bedtime(c, w)) s *= 0.4f;
        return (s, p.Value.why);
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var p = Pick(c, w, dist);
        if (p == null) return null;
        var (task, target, spot, _, why) = p.Value;
        var items = w.Blast.Items;
        var toils = new List<Toil>();
        switch (task)
        {
            case Task.Rescue:
                var (rec, v) = ((BlastRecord, CrewMember))target;
                w.Blast.RescueBy[(rec.Id, v.Id)] = c.Id;
                toils.Add(new GotoToil(spot));
                toils.Add(new WorkToil(0.1f, Skill.Medicine, v.Position));
                toils.Add(new DoToil((cm, world) =>
                {
                    if (v.Dead) return false;
                    v.Vitals.Injury = MathF.Max(0f, v.Vitals.Injury - 0.03f - 0.04f * cm.SkillLevel(Skill.Medicine));
                    v.Needs.Stress = MathF.Max(0f, v.Needs.Stress - 0.12f);
                    v.ChangeAffinity(cm, 0.06f); cm.ChangeAffinity(v, 0.04f);
                    world.Blast.Stats.Rescues++;
                    world.Blast.Helped.Add((rec.Id, v.Id));
                    world.Blast.RescueBy.Remove((rec.Id, v.Id));
                    MarkLog.Add(v.Memory.Marks, world.Tick, $"폭발 뒤 {Ko.IGa(cm.Name)} 달려와 살펴 주었다");
                    world.Log.Add(world.Tick, LogKind.Life, $"폭발에 다친 {Ko.EulReul(v.Name)} 살폈다", cm.Id);
                    Life.Diary(world, v, $"{Ko.IGa(cm.Name)} 제일 먼저 달려왔다.");
                    return true;
                }));
                return new Job(this, "폭발 구조", toils) { Urgent = true, LogText = why, LogKind = LogKind.Warning, OnFinished = (cm, world, st) => { if (world.Blast.RescueBy.TryGetValue((rec.Id, v.Id), out int by) && by == cm.Id) world.Blast.RescueBy.Remove((rec.Id, v.Id)); } };
            case Task.Investigate:
                var r2 = (BlastRecord)target;
                r2.Investigator = c.Id;
                toils.Add(new GotoToil(spot));
                toils.Add(new WorkToil(0.3f, Skill.Engineering, r2.At.Center));
                toils.Add(new DoToil((cm, world) => { world.Blast.Investigate(r2, cm); return true; }));
                return new Job(this, "폭발 조사", toils) { LogText = why, InterruptMargin = 0.35f, OnFinished = (cm, world, st) => { if (!r2.Investigated && r2.Investigator == cm.Id) r2.Investigator = -1; } };
            case Task.Secure:
                var o = (SecureOrder)target;
                if (items.Get(o.Explosive) is not Explosive e) return null;
                o.ClaimedBy = c.Id;
                toils.Add(new GotoToil(spot));
                toils.Add(new WorkToil(0.06f, Skill.Mechanics, e.Cell.Center));
                toils.Add(new DoToil((cm, world) => { if (e.Spent || e.Primed) return false; e.Carried = cm.Id; e.Handler = cm.Id; return true; }));
                toils.Add(new GotoToilLate(cm => items.SafeSpot(e)));
                toils.Add(new WorkToil(0.04f, Skill.Mechanics, null));
                toils.Add(new DoToil((cm, world) =>
                {
                    e.Carried = -1; e.Cell = cm.Cell; e.Moved = true; e.Handler = cm.Id; e.Risk = 0f;
                    o.Done = true;
                    items.Stats.Secured++;
                    world.Log.Add(world.Tick, LogKind.Work, $"{Ko.EulReul(e.Spec.Name)} 안전한 곳으로 옮겼다 ({o.Why})", cm.Id);
                    return true;
                }));
                return new Job(this, "폭발성 물건 옮기기", toils)
                {
                    LogText = why,
                    OnFinished = (cm, world, st) => { if (!o.Done && o.ClaimedBy == cm.Id) o.ClaimedBy = -1; if (e.Carried == cm.Id) { e.Carried = -1; e.Cell = cm.Cell; e.Moved = true; } },
                };
            case Task.Burp:
                var j = (Explosive)target;
                toils.Add(new GotoToil(spot));
                toils.Add(new WorkToil(0.04f, Skill.Cooking, j.Cell.Center));
                toils.Add(new DoToil((cm, world) => { if (j.Spent) return false; j.Pressure = 0.12f; items.Stats.Burped++; world.Smells.Emit(cm.Room, SmellKind.Foul, 0.08f); return true; }));
                return new Job(this, "항아리 김 빼기", toils) { LogText = why };
            case Task.Mourn:
                var sc = (BlastScar)target;
                toils.Add(new GotoToil(spot));
                toils.Add(new WaitToil(30, Pose.Standing, sc.At.Center));
                toils.Add(new DoToil((cm, world) =>
                {
                    sc.Visited.Add(cm.Id);
                    cm.Needs.Stress = MathF.Max(0f, cm.Needs.Stress - 0.08f);
                    Life.Diary(world, cm, "폭발 자리에 들렀다. 그을음이 아직 남아 있다.");
                    return true;
                }));
                return new Job(this, "폭발 자리 묵념", toils) { LogText = why };
        }
        return null;
    }
}

/// <summary>의도적 폭파: 작업대에서 폭약을 만들어 겨눈 곳에 설치하고, 사람을 비키게 하고, 숨어서 카운트다운 → 불발이면 30분 기다렸다 다시.</summary>
public sealed class BlastingActivity : Activity
{
    public override string Id => "blasting";
    public override string Label => "폭파 작업";

    private static BreachOrder? Mine(CrewMember c, World w)
    {
        foreach (var o in w.Blast.Items.Breaches)
        {
            if (o.Stage >= BreachOrder.Step.Done || o.Stage == BreachOrder.Step.Countdown || o.ClaimedBy >= 0 && o.ClaimedBy != c.Id) continue;
            if (o.Stage == BreachOrder.Step.Dud && w.Tick < o.DudUntil) continue;
            return o;
        }
        return null;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (w.Blast.Items.Breaches.Count == 0 || c.IsChild || !c.CanAct || c.Outside) return (0f, "—");
        var o = Mine(c, w);
        if (o == null) return (0f, "—");
        float skill = c.SkillLevel(Skill.Engineering);
        return (0.55f + 0.35f * skill + (o.ClaimedBy == c.Id ? 0.2f : 0f), $"폭파 — {o.Why}" + (o.Stage == BreachOrder.Step.Dud ? " (불발 — 다시 꽂는다)" : ""));
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var items = w.Blast.Items;
        var o = Mine(c, w);
        if (o == null || items.PlaceSpot(o, dist) is not Cell place) return null;
        var toils = new List<Toil>();
        Explosive? e = o.ChargeId >= 0 ? items.Get(o.ChargeId) : null;
        if (e == null || e.Spent)
        {
            // 만들기: 작업대
            var bench = w.Ship.FurnitureOf(FurnitureType.Workbench).Select(f => (f, s: f.UseSpots.FirstOrDefault(s => dist.Reachable(s)))).FirstOrDefault(x => x.s != default);
            if (bench.f == null) return null;
            toils.Add(new GotoToil(bench.s));
            toils.Add(new WorkToil(ExplosiveSet.Mining.CraftHours, Skill.Engineering, bench.f.Center));
            toils.Add(new DoToil((cm, world) => { e = items.Craft(o, cm); return true; }));
        }
        else if (o.Stage == BreachOrder.Step.Place || e.Carried < 0 && o.Stage < BreachOrder.Step.Countdown && e.Cell != place)
        {
            // 두고 간 폭약을 다시 든다
            var ec = e;
            toils.Add(new GotoToil(ec.Cell));
            toils.Add(new DoToil((cm, world) => { if (ec.Spent) return false; ec.Carried = cm.Id; return true; }));
        }
        if (o.Stage != BreachOrder.Step.Dud && o.Stage != BreachOrder.Step.Countdown)
        {
            // 설치
            toils.Add(new GotoToil(place));
            toils.Add(new WorkToil(0.25f, Skill.Engineering, o.Target.Center));
            toils.Add(new DoToil((cm, world) =>
            {
                if (e == null || e.Spent) return false;
                e.Carried = -1; e.Cell = place; e.Handler = cm.Id;
                o.Stage = BreachOrder.Step.Clear;
                o.Placer = cm.Id;
                world.Log.Add(world.Tick, LogKind.Work, $"폭약을 설치했다 — {o.Why}", cm.Id);
                return true;
            }));
        }
        else if (o.Stage == BreachOrder.Step.Dud)
        {
            // 불발: 다가가 기폭 장치를 다시 꽂는다
            toils.Add(new GotoToil(place));
            toils.Add(new WorkToil(0.2f, Skill.Engineering, o.Target.Center));
            toils.Add(new DoToil((cm, world) => { if (e == null || e.Spent) return false; e.Quality = MathF.Min(1f, e.Quality + 0.25f); o.Stage = BreachOrder.Step.Clear; return true; }));
        }
        // 숨을 곳 · 비키게 하기 · 카운트다운
        float power = o.Size;
        toils.Add(new GotoToilLate(cm => items.CoverSpot(place, power, BlastKind.Charge, w.Paths.Flood(cm.Cell, cm.PathProfile), cm)));
        toils.Add(new DoToil((cm, world) =>
        {
            if (e == null || e.Spent) return false;
            if (!items.ZoneClear(e, cm, out var inside)) items.Warn(e, cm, inside);
            return true;
        }));
        toils.Add(new WaitToil(SimTime.Minutes(12), Pose.Standing, place.Center) { DoneWhen = (cm, world) => e != null && items.ZoneClear(e, cm, out _) });
        toils.Add(new DoToil((cm, world) => { if (e == null || e.Spent) return false; items.Arm(o, e, cm); return true; }));
        toils.Add(new WaitToil(SimTime.Minutes(3), Pose.Sitting, place.Center) { DoneWhen = (cm, world) => e == null || e.Spent || o.Stage == BreachOrder.Step.Dud });
        o.ClaimedBy = c.Id;
        return new Job(this, "폭파 작업", toils)
        {
            LogText = $"폭파 — {o.Why}",
            AlwaysLog = true,
            OnFinished = (cm, world, st) =>
            {
                if (o.ClaimedBy == cm.Id && o.Stage != BreachOrder.Step.Countdown) o.ClaimedBy = -1;
                if (e != null && e.Carried == cm.Id) { e.Carried = -1; e.Cell = cm.Cell; }
                if (o.Stage == BreachOrder.Step.Dud) o.ClaimedBy = -1;
            },
        };
    }
}
