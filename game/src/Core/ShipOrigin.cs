using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.9 배의 내력이 세계에 남긴 것: 설계사 · 시작 상태 · 숨은 이야기 · 갈라짐과 다시 이음 · 침대 교대.
//
// 세계를 만들 때 한 번(Apply):
//  · 설계사 — 군용: 핵심 설비 Mk.3 · 전력/데이터 보조 간선(이중 배선) / 민간: 보조 간선 없음(단일 고장점) · 싼 부품(수명 짧음)
//             / 개척민: 임시로 이은 전선 · 임시 간선 · 손으로 만든 부품.
//  · 시작 상태 — 설비 마모 · 부품이 돈 시간 · 떼어 온 중고 부품 · 외벽 용접 자국 · 그을음 · 막힌 구역(용접한 문 · 포기한 방).
//  · 시작 화물 — 화물칸 · 창고 선반에.
//  · 숨은 이야기 — 중고 이상이면 패널 뒤 쪽지 · 숨겨 둔 술병 · 벽 낙서 (설비 곁 · 막힌 구역 안).
//  · 침대 교대 — 침대가 모자란 배(우편선)는 둘이 한 침대를 열두 시간 엇갈려 쓴다.
// 시스템 틱(Update):
//  · 정비하다 발견 → 일기 · 혼잣말 → 곁의 사람에게 이야기 (친한 사람에게 먼저) → 들은 사람과 가까워진다.
//  · 쪽지에 적힌 요령 → 그 설비를 정비할 때 더 잘 된다. 술병 → 저녁에 식당·휴게실에서 나눠 마신다. 낙서 → 한 줄 보탠다.
//  · 막힌 구역을 다시 열면(구획 재개방 작업) 용접을 끊고 — 안에 남은 것을 찾는다.
//  · 배가 둘로 갈리면(연결 통로 봉쇄 · 분리) 같은 쪽끼리 가까워지고 건너편 친구를 걱정한다 → 다시 이어지면 다시 만난다.
//  · 침대 교대: 엇갈려 일어날 때 인계를 나눈다 (사이가 나쁘면 투덜댄다).
// 예전 배(내력 없음)는 아무것도 바꾸지 않는다.

public enum FindKind { Note, Bottle, Graffiti }

/// <summary>배에 숨은 이야기 하나 (전 승무원이 남긴 것).</summary>
public sealed class HiddenFind
{
    public int Id { get; init; }
    public FindKind Kind { get; init; }
    public int RoomId { get; set; }
    public Cell At { get; set; }
    /// <summary>벽 쪽 (Cell.Dirs4 번호, 그림용).</summary>
    public int Wall { get; set; } = -1;
    public string Text { get; init; } = "";
    public string Author { get; init; } = "";
    /// <summary>요령이 적힌 설비 (쪽지) — 몸체 Id.</summary>
    public int Machine { get; init; } = -1;
    public bool TipUsed { get; set; }
    public bool Found { get; set; }
    public long FoundAt { get; set; } = -1;
    public int FoundBy { get; set; } = -1;
    /// <summary>막힌 구역 안에 있다 (다시 열어야 찾는다).</summary>
    public bool Sealed { get; init; }
    /// <summary>술병: 나눠 마셨다 (빈 병이 식탁에 남는다).</summary>
    public bool Shared { get; set; }
    /// <summary>낙서에 보탠 사람.</summary>
    public List<string> Added { get; } = new();
    /// <summary>아는 사람 (찾은 사람 · 들은 사람 · 함께 마신 사람).</summary>
    public HashSet<int> Knows { get; } = new();

    public string KindName => Kind switch { FindKind.Note => "쪽지", FindKind.Bottle => "술병", _ => "낙서" };
}

public sealed class OriginStats
{
    public int Found, Told, Tips, Toasts, Scribbles, Reopened, Splits, Reunions, Handovers, Grumbles;
    public int Rounds, RoundFixes, Squeezes, SqueezeBonds, SqueezeSpats, Ranks, Advice, CultureBorn, Calls; // v16.9 한 바퀴 · 좁은 배 · 컴퓨터 판단 · 정비 문화 · 건너편 교신
    public int RankFixes; // v16.9 컴퓨터가 순위에 올린 설비를 사람이 먼저 손본 횟수
    public string Summary() =>
        $"숨은 이야기 찾음 {Found} · 전함 {Told} · 쪽지 요령 {Tips} · 건배 {Toasts} · 낙서 보탬 {Scribbles} · 막힌 구역 열림 {Reopened} · 갈라짐 {Splits}(다시 이음 {Reunions}) · 침대 인계 {Handovers}(투덜 {Grumbles})"
        + $" · 한 바퀴 {Rounds}(손봄 {RoundFixes}) · 비좁아 마주침 {Squeezes}(웃음 {SqueezeBonds} · 짜증 {SqueezeSpats}) · 컴퓨터 정비 순위 {Ranks}(먼저 손봄 {RankFixes}) · 조언 {Advice} · 정비 문화 {CultureBorn} · 건너편 교신 {Calls}";
}

public sealed partial class ShipOriginSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7457 + 919));
    private long _next = -1, _nextSplit = -1;
    private int _nextId;

    public ShipOriginSystem(World w) => _w = w;

    /// <summary>이 배의 내력 (예전 배는 null).</summary>
    public ShipInfo? Info { get; private set; }
    public ShipDesigner Designer => Info?.Designer ?? ShipDesigner.Civilian;
    public ShipStart Start => Info?.Start ?? ShipStart.Used;
    public bool Active => Info != null;
    public List<HiddenFind> Finds { get; } = new();
    /// <summary>처음부터 막혀 있던 방.</summary>
    public List<int> SealedRooms { get; } = new();
    public OriginStats Stats { get; } = new();
    /// <summary>지금 배가 갈라져 있나 (사람이 있는 덩어리 둘 이상).</summary>
    public bool Split { get; private set; }
    /// <summary>갈라진 동안의 덩어리 번호 (승무원 Id → 덩어리).</summary>
    private readonly Dictionary<int, int> _side = new();
    /// <summary>침대 교대 짝 (늦게 자는 사람 → 먼저 자는 사람).</summary>
    public List<(int a, int b)> BedShares { get; } = new();
    private readonly Dictionary<int, bool> _wasAwake = new();

    private static readonly string[] FormerCrew = { "김말순", "도재혁", "임하나", "오세준", "강보람", "주태식", "남궁연", "변기철", "서다래", "황보철" };

    // ───────────────────────────── 세계를 만들 때 ─────────────────────────────

    public void Apply(ShipTemplate t)
    {
        if (t.Legacy) return;
        Info = t.Info!;
        var w = _w;
        var ship = w.Ship;
        var info = Info;
        var rng = new Rng(unchecked(w.Seed * 5821 + t.Key.Length * 131 + (int)info.Start * 17 + (int)info.Designer * 7 + 1));
        string former = info.FormerName ?? ship.Name;

        // 1) 설계사
        switch (info.Designer)
        {
            case ShipDesigner.Military:
                foreach (var m in ship.Machines)
                    if (m.Body.Type is FurnitureType.ReactorCore or FurnitureType.CoolantPump or FurnitureType.OxygenGenerator or FurnitureType.PowerPanel or FurnitureType.MainComputer)
                        m.Grade = MachineGrade.Mk3;
                if (w.Net.SourceRoom(NetKind.Power) is Room ps)
                    foreach (var r in w.Net.RingTargets(NetKind.Power).Take(4).ToList()) w.Net.AddRing(NetKind.Power, ps, r);
                if (w.Net.SourceRoom(NetKind.Data) is Room ds)
                    foreach (var r in w.Net.RingTargets(NetKind.Data).Take(2).ToList()) w.Net.AddRing(NetKind.Data, ds, r);
                break;
            case ShipDesigner.Civilian:
                if (w.Net.Rings.Count > 0) { w.Net.Rings.Clear(); w.Net.EnsureBuilt(); } // 단일 고장점
                foreach (var m in ship.Machines)
                    foreach (var p in w.Parts.Of(m)) p.Lot.Quality *= 0.85f; // 싼 부품
                break;
            case ShipDesigner.Settler:
                foreach (var m in ship.Machines)
                {
                    if (!m.Spec.Critical && !Crisis.PowerChain(m.Body.Type) && rng.Chance(0.18f)) { m.Spliced = true; m.Feed = rng.Range(0.55f, 0.8f); MarkLog.Add(m.Marks, w.Tick, "전 주인이 임시로 이어 둔 전선"); }
                    foreach (var p in w.Parts.Of(m))
                        if (rng.Chance(0.3f))
                            p.Lot = new PartLot { Id = 900_000 + _nextId++, Kind = p.Lot.Kind, Origin = PartOrigin.Handmade, Batch = "개척민 손", From = former, Maker = FormerCrew[rng.Range(0, FormerCrew.Length)],
                                Quality = rng.Range(0.7f, 1.15f), Made = w.Tick };
                }
                // 임시로 이은 선은 곁가지에만 — 원자로 · 냉각 · 배전 · 생명유지 같은 줄기와 공급원 방은 처음 지은 그대로 (거기가 가늘면 배 전체가 첫날 멈춘다)
                foreach (var l in w.Net.Links)
                {
                    if (l.Room.Type is RoomType.Reactor or RoomType.Cooling or RoomType.Power or RoomType.LifeSupport || l.Room.Kind == RoomType.WaterPlant || w.Net.SourceRoom(l.Kind) == l.Room) continue;
                    if (rng.Chance(0.08f)) { l.Temp = true; l.Integrity = rng.Range(0.62f, 0.85f); l.Cause = "개척민 임시 개조"; }
                }
                break;
        }

        // 2) 시작 상태: 설비 마모 · 부품 내력
        var (wLo, wHi, cLo, cHi, age) = info.Start switch
        {
            ShipStart.New => (0f, 0.08f, 0.97f, 1f, 0.03f),
            ShipStart.Used => (0.2f, 0.5f, 0.8f, 0.95f, 0.35f),
            ShipStart.WarScarred => (0.3f, 0.6f, 0.72f, 0.9f, 0.45f),
            ShipStart.Derelict => (0.45f, 0.75f, 0.6f, 0.85f, 0.6f),
            _ => (0.55f, 0.85f, 0.55f, 0.8f, 0.75f),
        };
        foreach (var m in ship.Machines)
        {
            m.Wear = rng.Range(wLo, wHi);
            m.Condition = rng.Range(cLo, cHi);
            if (m.Crop != null) continue;
            foreach (var p in w.Parts.Of(m))
            {
                p.Hours = p.Life * MathF.Max(0f, age * rng.Range(0.5f, 1.4f));
                if (info.Start is ShipStart.Junk or ShipStart.Derelict or ShipStart.WarScarred && rng.Chance(info.Start == ShipStart.Junk ? 0.35f : 0.2f))
                    p.Lot = new PartLot { Id = 900_000 + _nextId++, Kind = p.Lot.Kind, Origin = PartOrigin.Salvage, Batch = "떼어 온 것", From = $"{former} 이전의 배",
                        Quality = rng.Range(0.75f, 1.05f), Made = w.Tick, PriorHours = p.Life * rng.Range(0.2f, 0.6f) };
            }
            if (info.Start != ShipStart.New && rng.Chance(0.15f)) MarkLog.Add(m.Marks, w.Tick, $"{former} 시절부터 — 고친 자국이 여럿");
        }

        // 외벽: 용접 자국 · 그을음 · 피로
        var hull = ship.Walls.Where(kv => kv.Value.IsHull).Select(kv => kv.Key).OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
        float weldShare = info.Start switch { ShipStart.Junk => 0.14f, ShipStart.WarScarred => 0.08f, ShipStart.Derelict => 0.1f, ShipStart.Used => 0.03f, _ => 0f };
        foreach (var c in hull)
        {
            var ws = ship.WallAt(c)!;
            if (rng.Chance(weldShare))
            {
                ws.Welds = rng.Range(1, info.Start == ShipStart.Junk ? 4 : 3);
                ws.MaxIntegrity = MathF.Max(0.6f, 1f - 0.08f * ws.Welds);
                ws.Integrity = MathF.Min(ws.Integrity, ws.MaxIntegrity);
                ws.Breaches += 1;
            }
        }
        if (info.Start == ShipStart.WarScarred && hull.Count > 0)
        {
            // 한쪽 현에 몰린 그을음과 땜질 (그때 뚫린 자리)
            var center = hull[rng.Range(0, hull.Count)];
            foreach (var c in hull)
            {
                int d = Math.Abs(c.X - center.X) + Math.Abs(c.Y - center.Y);
                if (d > 9) continue;
                var ws = ship.WallAt(c)!;
                ws.Scorch = MathF.Max(ws.Scorch, 0.9f - d * 0.08f);
                if (d <= 4) { ws.Welds = Math.Max(ws.Welds, 2); ws.MaxIntegrity = MathF.Min(ws.MaxIntegrity, 0.8f); ws.Integrity = MathF.Min(ws.Integrity, 0.8f); }
            }
        }

        // 막힌 구역
        int seal = info.Start switch { ShipStart.Derelict => 1, ShipStart.WarScarred => 1, _ => 0 };
        if (seal > 0) foreach (var r in SealCandidates().Take(seal).ToList()) Seal(r, info.Start == ShipStart.Derelict ? "오래 비어 있던 구역 — 문이 용접돼 있다" : "그때 막아 둔 구역 — 문이 용접돼 있다");

        // 3) 시작 화물
        var shelves = ship.Furniture.Where(f => f.Type == FurnitureType.Shelf && f.Storage != null && f.Room.Type == RoomType.Storage)
            .OrderBy(f => f.Room.Kind == RoomType.Cargo ? 0 : 1).ThenBy(f => f.Id).ToList();
        foreach (var (kind, count) in info.CargoItems)
        {
            int left = count;
            foreach (var f in shelves)
            {
                if (left <= 0) break;
                left -= f.Storage!.Add(kind, left);
            }
        }

        // 4) 숨은 이야기
        int finds = info.Start switch { ShipStart.New => 0, ShipStart.Used => 2, ShipStart.Junk => 3, ShipStart.WarScarred => 3, _ => 5 };
        for (int i = 0; i < finds; i++) Hide((FindKind)(i % 3), rng, former);
        foreach (int rid in SealedRooms) Hide(FindKind.Note, rng, former, ship.Rooms[rid]);

        // 5) 침대 교대 (침대가 모자란 작은 배)
        if (info.SharedBed) ShareBeds();
        ApplyBody(rng); // v16.9 배 본체: 닳은 바닥 · 기름 자국 · 그을음 (바닥재는 방 종류로 이미 정해져 있다)

        w.Log.Add(w.Tick, LogKind.Ship, $"{ship.Name} — {ShipInfos.Name(info.Designer)} 설계 {ShipInfos.Name(info.Purpose)} · {ShipInfos.Name(info.Frame)} · {ShipInfos.Name(info.Start)}"
            + $" · {ShipInfos.Year - info.Built}년 된 배" + (info.FormerName != null ? $" (예전 이름 {info.FormerName})" : ""));
    }

    private IEnumerable<Room> SealCandidates()
    {
        var ship = _w.Ship;
        bool Bad(RoomType k) => k is RoomType.Shelter or RoomType.Quarantine or RoomType.QuarantineLock or RoomType.Triage or RoomType.Hyperbaric
            or RoomType.PrivateCabins or RoomType.QuietQuarters or RoomType.WaterWallCabin or RoomType.WaterPlant or RoomType.BatteryRoom or RoomType.HvacRoom
            or RoomType.FuelCell or RoomType.PumpRoom or RoomType.Substation or RoomType.ServerRoom or RoomType.BackupBridge or RoomType.Navigation
            or RoomType.Security or RoomType.CraneControl or RoomType.HeatStorage or RoomType.DroneBay or RoomType.Cargo;
        int Pref(RoomType k) => k switch { RoomType.Crusher => 0, RoomType.Morgue => 1, RoomType.Archive => 2, RoomType.Laundry => 3, RoomType.Recycling => 4, _ => 9 };
        return ship.Rooms.Where(r => r.Special is RoomType k && !Bad(k) && r.Doors.All(d => !d.IsExternal) && r.Doors.Count > 0
                                     && !r.Furniture.Any(f => f.Type == FurnitureType.Bed || f.Type == FurnitureType.SuitLocker))
            .OrderBy(r => Pref(r.Kind)).ThenBy(r => r.Id);
    }

    private void Seal(Room room, string why)
    {
        var w = _w;
        room.Abandoned = true;
        room.AbandonedSince = w.Tick;
        room.AbandonReason = why;
        room.VentOpen = false;
        room.Lockdown = true;
        foreach (var d in room.Doors)
            if (!d.IsExternal) { d.Locked = true; d.Welded = true; }
        MarkLog.Add(room.Marks, w.Tick, $"처음부터 막혀 있었다 — {why}");
        SealedRooms.Add(room.Id);
    }

    private static readonly string[] NoteTexts =
    {
        "\"다음 사람에게 — 이 설비는 발로 한 번 차면 돈다. 미안하다.\"",
        "\"여기서 보낸 열한 해. 고장 난 것들을 고치면서 나도 고쳐졌다.\"",
        "\"왼쪽 패널 밑에 예비 퓨즈 둘. 함장한테는 비밀.\"",
        "\"딸아이 생일 — 다음 기항지에서는 꼭 내린다.\"",
        "\"이 배는 시끄럽지만 정직하다. 잘 부탁해.\"",
        "\"베어링 소리가 바뀌면 사흘 안에 간다. 기다리지 말 것.\"",
    };
    private static readonly string[] SealedNotes =
    {
        "\"분진이 터졌다. 다들 탈출정으로. 나는 기록을 남기고 간다.\"",
        "\"좌현이 뚫렸을 때 우리는 여기 숨어 있었다. 다섯이 들어가 넷이 나왔다.\"",
        "\"이 방은 막는다. 다시 열 사람에게 — 공기부터 확인할 것.\"",
    };
    private static readonly string[] BottleTexts = { "고향 소주 한 병", "반쯤 남은 위스키 — 병목에 '손대지 마'", "약초 담금주 — 라벨에 '무사 귀환 때 딸 것'", "배에서 담근 감자 술" };
    private static readonly string[] GraffitiTexts = { "\"{0} 여기 다녀감\"", "날짜를 세는 금 — 마흔세 줄", "웃는 얼굴과 '힘내'", "\"{1} 정비팀 만세\"", "누군가의 고향 지도", "\"이 밸브 믿지 마\"" };

    /// <summary>숨은 것 하나를 놓는다: 쪽지는 설비 곁 패널 뒤, 술병은 창고 · 침실 · 엔진실 구석, 낙서는 벽 (막힌 구역이면 그 안).</summary>
    private void Hide(FindKind kind, Rng rng, string former, Room? inRoom = null)
    {
        var ship = _w.Ship;
        string author = FormerCrew[rng.Range(0, FormerCrew.Length)];
        Room? room = inRoom;
        Furniture? at = null;
        if (room == null)
        {
            var rooms = ship.Rooms.Where(r => !r.Detached && !r.Abandoned && r.Type != RoomType.Corridor && r.Type != RoomType.Airlock).ToList();
            var pref = kind switch
            {
                FindKind.Note => rooms.Where(r => r.Furniture.Any(f => f.Machine != null && f.Machine.Crop == null && f.UseSpots.Count > 0)).ToList(),
                FindKind.Bottle => rooms.Where(r => r.Type is RoomType.Storage or RoomType.Quarters or RoomType.Engine or RoomType.Workshop or RoomType.Cooling).ToList(),
                _ => rooms.Where(r => r.Type is RoomType.Workshop or RoomType.Quarters or RoomType.Engine or RoomType.Cooling or RoomType.Reactor or RoomType.Power or RoomType.LifeSupport).ToList(),
            };
            if (pref.Count == 0) pref = rooms;
            pref = pref.Where(r => Finds.All(f => f.RoomId != r.Id)).DefaultIfEmpty(pref[0]).ToList();
            room = pref[rng.Range(0, pref.Count)];
        }
        Cell cell;
        if (kind == FindKind.Note && room.Furniture.Where(f => f.Machine != null && f.Machine.Crop == null && f.UseSpots.Count > 0).OrderBy(f => f.Id).ToList() is { Count: > 0 } ms)
        {
            at = ms[rng.Range(0, ms.Count)];
            cell = at.UseSpots[0];
        }
        else
        {
            var cells = room.Cells.Where(c => ship.IsOpenFloor(c) && Cell.Dirs4.Any(d => ship.Grid.Kind(c + d) == TileKind.Wall && ship.DoorAt(c + d) == null))
                .OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
            if (cells.Count == 0) cells = room.Cells.Where(ship.IsOpenFloor).ToList();
            if (cells.Count == 0) return;
            cell = cells[rng.Range(0, cells.Count)];
        }
        int wall = -1;
        for (int i = 0; i < 4 && wall < 0; i++)
            if (ship.Grid.Kind(cell + Cell.Dirs4[i]) == TileKind.Wall) wall = i;
        string text = kind switch
        {
            FindKind.Note => inRoom != null ? SealedNotes[(int)Start % SealedNotes.Length] : NoteTexts[rng.Range(0, NoteTexts.Length)],
            FindKind.Bottle => BottleTexts[rng.Range(0, BottleTexts.Length)],
            _ => string.Format(GraffitiTexts[rng.Range(0, GraffitiTexts.Length)], author, former),
        };
        Finds.Add(new HiddenFind
        {
            Id = Finds.Count, Kind = kind, RoomId = room.Id, At = cell, Wall = wall, Text = text, Author = author,
            Machine = at?.Id ?? -1, Sealed = inRoom != null,
        });
    }

    /// <summary>침대가 모자라면 둘이 한 침대를 열두 시간 엇갈려 쓴다 (간이침대는 접는다).</summary>
    private void ShareBeds()
    {
        var w = _w;
        var beds = w.Ship.FurnitureOf(FurnitureType.Bed).OrderBy(b => b.MinX).ToList();
        if (beds.Count == 0) return;
        var owners = w.Crew.Where(c => c.Bed != null && c.Bed.Type == FurnitureType.Bed).ToList();
        int k = 0;
        foreach (var c in w.Crew.Where(c => c.Bed == null || c.Bed.Type == FurnitureType.Cot).ToList())
        {
            if (owners.Count == 0) break;
            var partner = owners[k++ % owners.Count];
            if (c.Bed is Furniture cot && cot.Type == FurnitureType.Cot) w.Ship.Stow(cot);
            c.Bed = partner.Bed;
            c.HomeBed = partner.Bed;
            c.Schedule = Schedule.FromBedtime(SimTime.Wrap(partner.Schedule.SleepStart + 12f));
            BedShares.Add((c.Id, partner.Id));
            w.Body.SetZone(partner.Bed!.Room, AccessZone.Open, LockKind.None); // 둘 다 주인 — 교대로 쓰는 선실은 잠그지 않는다 (v16.3 선실 노크 · 잠금에 막히지 않게)
            MarkLog.Add(partner.Bed!.Room.Marks, w.Tick, $"{Ko.WaGwa(partner.Name)} {Ko.IGa(c.Name)} 침대 하나를 교대로 쓴다");
        }
        w.Paths.Invalidate();
    }

    // ───────────────────────────── 시스템 틱 ─────────────────────────────

    public void Update(float dt)
    {
        if (Info == null) return;
        var w = _w;
        if (_next < 0) _next = w.Tick + SimTime.Minutes(10);
        if (w.Tick >= _next)
        {
            _next = w.Tick + SimTime.Minutes(10);
            Reopened();
            Discover();
            UseTips();
            Toast();
            Tell();
            Handover();
            Squeeze(); // v16.9 좁은 배에서 자꾸 마주친다 → 관계
        }
        Watch(); // v16.9 비켜서기 관찰 (매 틱 · 사람 수만큼)
        if (w.Tick >= _nextHour) { _nextHour = w.Tick + SimTime.Hours(1); Hourly(); } // v16.9 주컴퓨터 정비 순위 · 조언 · 정비 문화
        if (_nextSplit < 0) _nextSplit = w.Tick + SimTime.Minutes(30);
        if (w.Tick >= _nextSplit)
        {
            _nextSplit = w.Tick + SimTime.Minutes(30);
            Sides();
        }
    }

    private static bool Able(CrewMember c) => !c.Dead && !c.Down && !c.Outside && c.IsAwake && c.CanAct && c.Room != null;

    /// <summary>막힌 구역이 다시 열렸으면(구획 재개방) 남은 용접을 끊는다.</summary>
    private void Reopened()
    {
        var ship = _w.Ship;
        foreach (int rid in SealedRooms)
        {
            var room = ship.Rooms[rid];
            if (room.Abandoned || room.Detached) continue;
            bool any = false;
            foreach (var d in room.Doors)
                if (d.Welded && !d.IsExternal) { d.Welded = false; d.Locked = false; any = true; }
            if (any)
            {
                Stats.Reopened++;
                _w.History.Add(_w, HistoryKind.Adaptation, $"처음부터 막혀 있던 {Ko.EulReul(room.Name)} 용접을 끊고 열었다 — 먼지 냄새", room, log: true);
            }
        }
    }

    /// <summary>정비 · 수리 · 작업 중에 곁(세 칸)에 숨은 것을 찾는다. 다시 연 막힌 구역은 들어가 둘러보다 찾는다.</summary>
    private void Discover()
    {
        var w = _w;
        foreach (var f in Finds)
        {
            if (f.Found) continue;
            var room = w.Ship.Rooms[f.RoomId];
            if (room.Abandoned || room.Detached) continue;
            CrewMember? who = null;
            foreach (var c in w.Crew)
            {
                if (!Able(c) || c.IsChild || c.Room != room) continue;
                int d = Math.Abs(c.Cell.X - f.At.X) + Math.Abs(c.Cell.Y - f.At.Y);
                bool working = c.Pose == Pose.Working && c.Job != null && (c.Job.Order != null || c.Job.Target?.Machine != null);
                int reach = w.Culture.Follows(c, CustomKind.MaintainerWay) || RoundsToday(c) ? 5 : 3; // v16.9 소리부터 듣는 사람 · 한 바퀴 도는 사람은 패널 틈을 더 잘 본다
                if (working && d <= reach || f.Sealed && d <= 4) { who = c; break; }
            }
            if (who == null) continue;
            Found(f, who);
        }
    }

    private void Found(HiddenFind f, CrewMember c)
    {
        var w = _w;
        f.Found = true;
        f.FoundAt = w.Tick;
        f.FoundBy = c.Id;
        f.Knows.Add(c.Id);
        Stats.Found++;
        var room = w.Ship.Rooms[f.RoomId];
        string how = f.Sealed ? "다시 연 구역을 둘러보다" : f.Kind == FindKind.Graffiti ? "패널을 떼어 내다" : "정비하다 패널 뒤에서";
        string what = f.Kind switch
        {
            FindKind.Note => $"전 승무원 {f.Author}의 쪽지를 찾았다 — {f.Text}",
            FindKind.Bottle => $"{Ko.IGa(f.Author)} 숨겨 둔 술병을 찾았다 — {f.Text}",
            _ => $"벽 낙서를 찾았다 — {f.Text}",
        };
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} {room.Name}에서 {how} {what}", c.Id);
        w.History.Add(w, HistoryKind.Memory, $"{Ko.IGa(c.Name)} {room.Name}에서 {what}", room, new[] { c });
        MarkLog.Add(room.Marks, w.Tick, $"{c.Name}: {f.KindName} 발견");
        Life.Diary(w, c, Persona.Say(c, f.Kind switch
        {
            FindKind.Note => $"{room.Name} 패널 뒤에서 {f.Author}라는 사람의 쪽지를 찾았다. {f.Text} — 이 배에도 우리 전에 살던 사람들이 있었다.",
            FindKind.Bottle => $"{Ko.IGa(f.Author)} 숨겨 둔 술을 찾았다. 오늘 저녁에 다 같이 나눠야겠다.",
            _ => $"{room.Name} 벽에 낙서가 있었다. {f.Text} — 웃음이 났다.",
        }));
        c.Say(w, Persona.Say(c, f.Kind switch { FindKind.Note => "어? 쪽지가 있네…", FindKind.Bottle => "이게 왜 여기 있지? 술이잖아!", _ => "누가 여기다 낙서를 했네" }));
        switch (f.Kind)
        {
            case FindKind.Note:
            {
                var b = w.Belongings.Seed2(c, BelongingKind.Journal, $"{room.Name} 패널 뒤에서 찾음");
                b.Name = $"전 승무원 {f.Author}의 쪽지";
                MarkLog.Add(b.Marks, w.Tick, f.Text);
                c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.03f);
                break;
            }
            case FindKind.Graffiti:
                c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.04f);
                if (c.Habits.Contains(Habit.Joker) || c.Habits.Contains(Habit.Cheerful) || R.Chance(0.35f))
                {
                    f.Added.Add(c.Name); // 한 줄 보탠다
                    Stats.Scribbles++;
                    w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} 낙서 옆에 제 이름을 한 줄 보탰다", c.Id);
                }
                break;
        }
    }

    /// <summary>쪽지에 적힌 요령: 아는 사람이 그 설비에서 일하면 마모가 더 풀린다 (한 번).</summary>
    private void UseTips()
    {
        var w = _w;
        foreach (var f in Finds)
        {
            if (!f.Found || f.TipUsed || f.Machine < 0) continue;
            var body = w.Ship.Furniture[f.Machine];
            if (body.Machine is not Machine m) continue;
            foreach (var c in w.Crew)
            {
                if (!Able(c) || !f.Knows.Contains(c.Id) || c.Pose != Pose.Working || c.Job?.Target != body) continue;
                f.TipUsed = true;
                m.Wear = MathF.Max(0f, m.Wear - 0.2f);
                m.Fouled = MathF.Max(0f, m.Fouled - 0.2f);
                Stats.Tips++;
                MarkLog.Add(m.Marks, w.Tick, $"{c.Name}: {f.Author}의 쪽지대로");
                w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(c.Name)} {f.Author}의 쪽지에 적힌 대로 {Ko.EulReul(m.Name)} 손봤다 — 소리가 달라졌다", c.Id);
                c.Say(w, Persona.Say(c, "쪽지에 적힌 대로 해 보자"));
                break;
            }
        }
    }

    /// <summary>술병: 찾은 사람이 저녁에 식당 · 휴게실에서 곁의 사람들과 나눠 마신다.</summary>
    private void Toast()
    {
        var w = _w;
        float hour = SimTime.HourOfDay(w.Tick);
        if (hour < 17f || hour > 23.5f) return;
        foreach (var f in Finds)
        {
            if (!f.Found || f.Shared || f.Kind != FindKind.Bottle) continue;
            var c = w.Crew[f.FoundBy];
            if (!Able(c) || c.Job?.Urgent == true || c.Room!.Type is not (RoomType.Mess or RoomType.Lounge)) continue;
            var mates = w.Crew.Where(o => o != c && Able(o) && !o.IsChild && o.Room == c.Room && o.Job?.Urgent != true).ToList();
            if (mates.Count == 0) continue;
            f.Shared = true;
            Stats.Toasts++;
            var all = mates.Append(c).ToList();
            foreach (var o in all)
            {
                f.Knows.Add(o.Id);
                o.Needs.Stress = MathF.Max(0f, o.Needs.Stress - 0.06f);
                o.Needs.Social = MathF.Min(1f, o.Needs.Social + 0.12f);
                if (o != c) { o.ChangeAffinity(c, 0.04f); c.ChangeAffinity(o, 0.03f); }
            }
            // 빈 병은 식탁에 남는다
            var table = c.Room.Furniture.Where(x => x.Type == FurnitureType.Table).OrderBy(x => x.Id).FirstOrDefault();
            f.RoomId = c.Room.Id;
            f.At = table?.Cells[0] ?? c.Cell;
            f.Wall = -1;
            c.Say(w, Persona.Say(c, $"{f.Author}에게 — 건배!"));
            w.History.Add(w, HistoryKind.Bond, $"{Ko.IGa(c.Name)} {Ko.IGa(f.Author)} 숨겨 둔 술을 {c.Room.Name}에서 {Ko.WaGwa(string.Join(", ", mates.Select(m => m.Name)))} 나눠 마셨다", c.Room, all, log: true);
            foreach (var o in all) Life.Diary(w, o, Persona.Say(o, $"{Ko.IGa(c.Name)} 찾은 옛 승무원의 술을 다 같이 나눠 마셨다. {f.Author}이라는 사람도 이렇게 마셨겠지."));
        }
    }

    /// <summary>이야기를 전한다: 아는 사람이 곁(두 칸 반)의 모르는 사람에게 — 친할수록 · 수다스러울수록 잘 전한다. 들은 사람과 조금 가까워진다.</summary>
    private void Tell()
    {
        var w = _w;
        int told = 0;
        foreach (var f in Finds)
        {
            if (!f.Found || f.Knows.Count >= w.Crew.Count(c => !c.Dead)) continue;
            foreach (int id in f.Knows.OrderBy(x => x).ToList())
            {
                if (told >= 3) return;
                var c = w.Crew[id];
                if (!Able(c) || c.Job?.Urgent == true || c.IsChild) continue;
                foreach (var o in w.Crew)
                {
                    if (o == c || f.Knows.Contains(o.Id) || !Able(o) || o.Room != c.Room) continue;
                    if ((o.Position - c.Position).Length() > 2.5f) continue;
                    float p = 0.25f + 0.4f * c.Traits.Sociability + 0.5f * MathF.Max(0f, c.AffinityTo(o));
                    if (!R.Chance(p)) continue;
                    f.Knows.Add(o.Id);
                    Stats.Told++;
                    told++;
                    c.Say(w, Persona.Say(c, f.Kind switch
                    {
                        FindKind.Note => $"그거 알아? {f.Author}라는 사람이 남긴 쪽지가 있었어 — {f.Text}",
                        FindKind.Bottle => $"{Ko.IGa(f.Author)} 숨겨 둔 술 얘기 들었어?",
                        _ => $"{w.Ship.Rooms[f.RoomId].Name} 벽에 낙서 봤어? {f.Text}",
                    }));
                    o.ChangeAffinity(c, 0.02f);
                    c.ChangeAffinity(o, 0.01f);
                    if (R.Chance(0.4f)) Life.Diary(w, o, Persona.Say(o, $"{c.Name}한테 이 배의 옛 이야기를 들었다 — {f.Author}."));
                    break;
                }
            }
        }
    }

    /// <summary>침대 교대: 먼저 자던 사람이 일어나고 다음 사람이 들어갈 때 인계를 나눈다 (사이가 나쁘면 투덜댄다).</summary>
    private void Handover()
    {
        var w = _w;
        foreach (var (a, b) in BedShares)
        {
            var x = w.Crew[a];
            var y = w.Crew[b];
            if (x.Dead || y.Dead) continue;
            if (y.Bed is Furniture sb && sb.Room.Doors.Any(d => w.Body.DoorOf(d) is DoorBody db && db.Zone != AccessZone.Open)) w.Body.SetZone(sb.Room, AccessZone.Open, LockKind.None); // 구조가 바뀌어 다시 선실이 됐으면
            foreach (var p in new[] { x, y })
            {
                bool awake = p.IsAwake;
                bool was = !_wasAwake.TryGetValue(p.Id, out var v) || v;
                _wasAwake[p.Id] = awake;
                if (was || !awake) continue;
                // p가 막 일어났다 → 짝은 곧 잔다
                var q = p == x ? y : x;
                if (!Able(q)) continue;
                Stats.Handovers++;
                float aff = q.AffinityTo(p);
                if (aff < -0.15f || q.Habits.Contains(Habit.NeatFreak) && R.Chance(0.3f))
                {
                    Stats.Grumbles++;
                    q.Needs.Stress = MathF.Min(1f, q.Needs.Stress + 0.03f);
                    q.Say(w, Persona.Say(q, $"{p.Name}, 침대 좀 정리하고 나와"));
                    q.ChangeAffinity(p, -0.01f);
                }
                else
                {
                    q.Needs.Social = MathF.Min(1f, q.Needs.Social + 0.05f);
                    p.Say(w, Persona.Say(p, $"{q.Name}, 교대할게 — 별일 없었어?"));
                    p.ChangeAffinity(q, 0.01f);
                    q.ChangeAffinity(p, 0.01f);
                }
            }
        }
    }

    /// <summary>배가 둘로 갈렸나: 용접 · 떨어져 나간 문을 빼고 이어진 방 덩어리마다 사람이 있으면 갈라짐.</summary>
    private void Sides()
    {
        var w = _w;
        var ship = w.Ship;
        int n = ship.Rooms.Count;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        int Find(int a) { while (parent[a] != a) a = parent[a] = parent[parent[a]]; return a; }
        foreach (var d in ship.Doors)
        {
            if (d.IsExternal || d.Removed || d.Welded || d.RoomA == null || d.RoomB == null || d.RoomA.Detached || d.RoomB.Detached) continue;
            parent[Find(d.RoomA.Id)] = Find(d.RoomB.Id);
        }
        var side = new Dictionary<int, int>();
        foreach (var c in w.Crew)
            if (!c.Dead && !c.Outside && c.Room != null && !c.Room.Detached) side[c.Id] = Find(c.Room.Id);
        int groups = side.Values.Distinct().Count();
        if (groups >= 2)
        {
            if (!Split)
            {
                Split = true;
                Stats.Splits++;
                _side.Clear();
                foreach (var kv in side) _side[kv.Key] = kv.Value;
                w.History.Add(w, HistoryKind.Structure, $"배가 {groups}쪽으로 갈렸다 — 건너편으로 갈 길이 없다", log: true);
                OnSplit(side); // v16.9 주컴퓨터가 쪽마다 무엇이 있고 없는지 읽는다
            }
            Call(side); // v16.9 걱정되는 사람은 통신기로 건너편을 부른다
            // 같은 쪽끼리 가까워지고 · 건너편 친구를 걱정한다
            foreach (var c in w.Crew)
            {
                if (!side.TryGetValue(c.Id, out int s)) continue;
                float worry = 0f;
                foreach (var o in w.Crew)
                {
                    if (o == c || !side.TryGetValue(o.Id, out int so)) continue;
                    if (so == s) { if (Able(c) && Able(o) && o.Room == c.Room) c.ChangeAffinity(o, 0.006f); }
                    else worry += MathF.Max(0f, c.AffinityTo(o));
                }
                if (worry > 0f) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + MathF.Min(0.03f, 0.01f * worry));
            }
        }
        else if (Split)
        {
            Split = false;
            Stats.Reunions++;
            var met = new List<CrewMember>();
            foreach (var c in w.Crew)
            {
                if (c.Dead || !_side.TryGetValue(c.Id, out int was)) continue;
                c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.15f);
                foreach (var o in w.Crew)
                    if (o != c && _side.TryGetValue(o.Id, out int wo) && wo != was) c.ChangeAffinity(o, 0.02f);
                met.Add(c);
            }
            w.History.Add(w, HistoryKind.Recovery, "갈렸던 배가 다시 이어졌다 — 건너편 사람들과 다시 만났다", null, met, log: true);
            if (met.FirstOrDefault(Able) is CrewMember sp) sp.Say(w, Persona.Say(sp, "다시 이어졌다! 다들 무사해?"));
            _side.Clear();
        }
    }
}
