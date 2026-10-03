using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v18.5 도킹 · 난파선 탐사 · 해체.
//  · 붙기: 다른 배(기항지의 거룻배 · 지나가는 상선)나 난파선이 에어락 아래에 붙는다. 격자를 아래로 늘려(증축과 같은 법 — 칸 번호는 그대로)
//    난파선은 작은 격자(벽 · 방 셋 · 칸막이 구멍 · 도킹 구멍 · 우리 배까지 이어진 도킹 통로)로 붙는다. 칸은 우주 그대로(진공 · 전기 없음 · 어둠)라
//    승무원은 우주복을 입고 에어락으로 나가 통로를 따라 들어간다 — 원정과 달리 들어가고 나오는 것이 다 보인다.
//  · 기밀 확인 · 기압 맞추기: 한 사람이 에어락 안쪽에서 도킹 고리를 물리고 압력 유지 시험을 한다. 주컴퓨터가 압력계를 읽어 분당 떨어지는 양 ·
//    저쪽 기압을 알려 준다 (끝까지 시험할지 카드). 이음이 덜 물렸으면 다시 물린다. 시험을 건너뛰고 열면 고리 옆 외판이 샌다 (선체 파공 → 기존 대응).
//    다른 배는 저쪽 기압에 맞춘 뒤 문을 연다 — 안 맞추고 열면 문이 튕겨 곁의 사람이 다친다. 난파선 쪽은 0 — 안쪽 문은 닫아 두고 우주복으로.
//  · 난파선 안: 어둠(헬멧 등만 — 뒤지는 속도가 늦고 잔해에 걸린다 · 어둠을 무서워하는 사람은 마음이 무겁다) · 진공(우주복 산소 · 찢긴 판에 우주복이 긁힌다) ·
//    흔들리는 격벽(자르다 무너진다) · 얼어붙은 연료관(불꽃). 다친 자리는 기억해 둔다 (그 방 위험 — 다음 사람은 조심 · 컴퓨터가 절단을 말린다).
//  · 남은 기록: 함장 일지 · 편지 · 벽에 긁은 글 · 녹음. 찾은 사람이 들고 돌아오면 저녁에 다 같이 모여 읽는다 (녹음은 주컴퓨터가 튼다) → 묵념 →
//    식당 벽에 이름판. 함께 들은 사람끼리 가까워지고 · 마음이 내려앉았다가 가벼워진다 · 연대기에 남는다.
//  · 해체: 다 뒤진 방부터 판 · 골조 · 전선 · 전자 부품을 잘라 온다 (돌아와 창고에 넣는다) — 난파선이 조금씩 작아진다. 증축이 자재를 기다리면 그쪽이 먼저 쓴다.
//  · 다른 배: 물자 교환(남는 것 ↔ 모자란 것 — 해치 앞에 상자) · 공동 작업(저쪽 기관사와 우리 사람이 가장 낡은 설비를 같이 손본다) · 손님이 탄다(승객).
// 난수는 전용(시드 × 소수). 사전은 정렬해서만 돈다. 1분마다.

public enum DockKind : byte { Ship, Wreck }
public enum DockStage : byte { Seal, Open, Salvage, Leaving, Gone }

/// <summary>난파선의 방 하나 (안쪽 칸 X0..X1 × Y0..Y1).</summary>
public sealed class WreckRoom
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    /// <summary>0 조종실 · 1 선실 · 2 화물칸 · 3 기관실.</summary>
    public int Kind { get; init; }
    public int X0 { get; init; }
    public int X1 { get; init; }
    public int Y0 { get; init; }
    public int Y1 { get; init; }
    /// <summary>0 없음 · 1 찢긴 판 · 2 흔들리는 격벽 · 3 얼어붙은 연료관.</summary>
    public int Hazard { get; init; }
    public bool HazardKnown { get; set; }
    public float Search { get; set; }
    public bool Searched { get; set; }
    public int SearchBy { get; set; } = -1;
    public int Pieces { get; init; }
    public int Cut { get; set; }
    public float CutWork { get; set; }
    public int CutBy { get; set; } = -1;
    public bool Collapsed { get; set; }
    public bool Survivor { get; set; }
    public bool Gone => Cut >= Pieces;
    public Cell Spot => new((X0 + X1) / 2, (Y0 + Y1 + 1) / 2);
    public bool Contains(Cell c) => c.X >= X0 && c.X <= X1 && c.Y >= Y0 && c.Y <= Y1;
}

/// <summary>난파선에 남은 기록 하나.</summary>
public sealed class LastRecord
{
    public string Who { get; init; } = "";
    public string Role { get; init; } = "";
    /// <summary>0 함장 일지 · 1 편지 · 2 벽에 긁은 글 · 3 녹음.</summary>
    public int Kind { get; init; }
    public string Text { get; init; } = "";
    public int Room { get; init; }
    public int FoundBy { get; set; } = -1;
    public long FoundAt { get; set; } = -1;
    public bool Home { get; set; }
    public static string KindName(int k) => k switch { 0 => "함장 일지", 1 => "편지", 2 => "벽에 긁은 글", _ => "녹음" };
}

public sealed class DockVisit
{
    public int Id { get; init; }
    public DockKind Kind { get; init; }
    public string Name { get; init; } = "";
    /// <summary>선체 생김 (0~3 — 그림이 다르다).</summary>
    public int Hull { get; init; }
    public long Arrived { get; init; }
    public DockStage Stage { get; set; }
    public long StageSince { get; set; }
    // 기밀 · 기압
    /// <summary>도킹 고리가 실제로 얼마나 잘 물렸나 (사람은 시험해야 안다).</summary>
    public float SealQ { get; set; }
    public float SealWork { get; set; }
    public int SealBy { get; set; } = -1;
    public int Reseats { get; set; }
    /// <summary>주컴퓨터가 읽은 압력 강하 (kPa/분, −1 = 아직).</summary>
    public float LeakRead { get; set; } = -1f;
    public bool? FullTest { get; set; }
    public int CardId { get; set; } = -1;
    public bool Checked { get; set; }
    public bool Skipped { get; set; }
    public bool Leaked { get; set; }
    public Cell LeakWall { get; set; }
    /// <summary>저쪽 기압 (kPa).</summary>
    public float OtherKpa { get; init; }
    public float Equalized { get; set; }
    public bool DoorJolt { get; set; }
    public long Opened { get; set; } = -1;
    // 자리
    public int HatchDoor { get; set; } = -1;
    public int X0 { get; set; }
    public int Y0 { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public int DockX { get; set; }
    public int CollarTop { get; set; }
    public List<WreckRoom> Rooms { get; } = new();
    public List<LastRecord> Records { get; } = new();
    public List<string> Crew { get; } = new();
    public HashSet<Cell> Moored { get; } = new();
    // 들고 온 것
    public SortedDictionary<ItemKind, int> Salvaged { get; } = new();
    public List<string> Trades { get; } = new();
    public int JointMachine { get; set; } = -1;
    public float JointWork { get; set; }
    public int JointBy { get; set; } = -1;
    public bool JointDone { get; set; }
    public bool TradeDone { get; set; }
    public int TradeBy { get; set; } = -1;
    public float TradeWork { get; set; }
    public List<int> Boarded { get; } = new();
    // 추모
    public long MemorialAt { get; set; } = -1;
    public int MemorialRoom { get; set; } = -1;
    public Cell Plaque { get; set; }
    public bool Mourned { get; set; }
    public List<int> Mourners { get; } = new();
    public int Reader { get; set; } = -1;
    public long LeaveAt { get; set; } = -1;
    public bool Wreck => Kind == DockKind.Wreck;
    public bool Covers(Cell c) => c.X >= X0 - 1 && c.X <= X0 + W && c.Y >= Y0 - 1 && c.Y <= Y0 + H;
    public WreckRoom? RoomAt(Cell c) => Rooms.FirstOrDefault(r => r.Contains(c));
}

public sealed class DockStats
{
    public int Docked, Wrecks, Ships, SealChecks, Reseats, Skips, Leaks, Jolts, Searches, Records, Memorials, Mourners, Pieces, Items, Hurts, Collapses,
        Trips, DarkTrips, Trades, Joint, Survivors, Guests, ComputerReads;
}

public sealed partial class DockingSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7867 + 389));
    private long _next;
    public static bool Off;
    public static long UpdateTicks;
    public List<DockVisit> Visits { get; } = new();
    public DockStats Stats { get; } = new();
    public int Version { get; private set; }
    /// <summary>EVA로 들고 오는 것 (사람 → 방 번호 · 조각 수).</summary>
    private readonly SortedDictionary<int, (int visit, int room, int pieces, bool survivor)> _carry = new();

    public DockingSystem(World w) { _w = w; }

    public DockVisit? Active => Visits.Count > 0 && Visits[^1].Stage != DockStage.Gone ? Visits[^1] : null;
    public bool Blocks(Cell c) => Active is DockVisit v && v.Covers(c);
    private bool ComputerOn => _w.Automation.Present && _w.Automation.MainOnline;
    private Room? HatchRoom(DockVisit v) => v.HatchDoor >= 0 && v.HatchDoor < _w.Ship.Doors.Count ? Inner(_w.Ship.Doors[v.HatchDoor]) is Cell c ? _w.Ship.RoomAt(c) : null : null;

    internal Cell? Inner(Door hatch)
    {
        foreach (var d in Cell.Dirs4)
            if (_w.Ship.RoomAt(hatch.Cell + d) != null && _w.Ship.IsWalkable(hatch.Cell + d)) return hatch.Cell + d;
        return null;
    }

    // ─────────────────────────────── 붙기 ───────────────────────────────

    /// <summary>항로의 난파선 구간 (Voyage).</summary>
    public void OnDerelict(Leg leg) { if (!Off && Active == null) Arrive(DockKind.Wreck, leg.Name); }

    /// <summary>기항지: 거룻배가 붙는다 (물자 · 손님 · 같이 손보기).</summary>
    public void OnPort(Leg leg) { if (!Off && Active == null) Arrive(DockKind.Ship, VoyageV15.PortOf(leg.Name).Name + " 거룻배"); }

    public DockVisit? Arrive(DockKind kind, string name)
    {
        var w = _w;
        var hatch = DroneSystem.Hatch(w);
        if (hatch == null || Inner(hatch) == null || Active != null) return null;
        var v = new DockVisit
        {
            Id = Visits.Count, Kind = kind, Name = name, Hull = R.Range(0, 4), Arrived = w.Tick, StageSince = w.Tick, HatchDoor = hatch.Id,
            SealQ = kind == DockKind.Wreck ? R.Range(0.3f, 0.95f) : R.Range(0.5f, 1f),
            OtherKpa = kind == DockKind.Wreck ? 0f : R.Range(86f, 97f),
        };
        if (!Place(v)) return null;
        Visits.Add(v);
        Stats.Docked++;
        if (v.Wreck) { Stats.Wrecks++; Fill(v); } else Stats.Ships++;
        var room = HatchRoom(v);
        string what = v.Wreck ? $"난파선 {v.Name} — 신호 없음 · 에어락 아래에 붙였다" : $"{v.Name}{(v.Name.EndsWith("배") ? "가" : "이")} 에어락 아래에 붙었다";
        w.RaiseAlert(what + " · 기밀부터 확인한다", room, AlertLevel.Notice, shipWide: true);
        w.History.Add(w, HistoryKind.Decision, v.Wreck ? $"{v.Name}에 붙었다 — 선체는 차갑고 불빛 하나 없다" : $"{Ko.WaGwa(v.Name)} 도킹했다", room, log: true);
        if (ComputerOn)
        {
            string scan = v.Wreck ? $"저쪽 기압 0 · 선체 영하 {R.Range(60, 140)}도 · 전기 없음 · 방 {v.Rooms.Count}칸" : $"저쪽 기압 {v.OtherKpa:0}kPa · 우리 101kPa";
            w.Automation.Book.Add(ActKind.Advice, room, $"도킹 — {v.Name}: {scan}", "고리를 물린 뒤 압력 유지 시험부터", "도킹 고리 기밀 확인", v.Wreck ? "안쪽 문은 닫아 둔다 · 들어갈 사람은 우주복" : "저쪽 기압에 맞춘 뒤 연다", "dock:" + v.Id, SimTime.Hours(1));
            w.Automation.Speak.Announce(w.Automation.Voice.Style(v.Wreck ? $"{v.Name}에 붙었다. 저쪽은 진공이다 — 에어락 안쪽 문은 내가 잠가 둔다" : $"{v.Name} 도킹. 기압을 맞추기 전엔 문을 열지 말 것"), room, 1);
            Stats.ComputerReads++;
            SealCard(v);
        }
        Version++;
        return v;
    }

    /// <summary>격자를 아래로 늘리고 난파선(또는 거룻배)을 붙인다 — 칸 번호는 그대로.</summary>
    private bool Place(DockVisit v)
    {
        var w = _w;
        var g = w.Ship.Grid;
        int yb = -1;
        for (int y = g.Height - 1; y >= 0 && yb < 0; y--)
            for (int x = 0; x < g.Width; x++)
                if (g.Kind(new Cell(x, y)) != TileKind.Void) { yb = y; break; }
        if (yb < 0) return false;
        v.W = Math.Min(v.Wreck ? 16 : 12, g.Width - 4);
        v.H = v.Wreck ? 5 : 4;
        if (v.W < 10) return false;
        // 도킹 구멍: 에어락에서 가장 가까운, 맨 아래 외벽이 있는 줄
        int hx = _w.Ship.Doors[v.HatchDoor].Cell.X;
        int dock = -1;
        for (int k = 0; k < g.Width && dock < 0; k++)
            foreach (int x in new[] { hx - k, hx + k })
                if (x > 2 && x < g.Width - 3 && g.Kind(new Cell(x, yb)) != TileKind.Void) { dock = x; break; }
        if (dock < 0) return false;
        v.DockX = dock;
        v.X0 = Math.Clamp(dock - v.W / 2, 1, g.Width - 1 - v.W);
        v.Y0 = yb + 3;
        v.CollarTop = yb + 1;
        int need = v.Y0 + v.H + 2 - g.Height;
        if (need > 0)
        {
            g.GrowRows(need);
            w.Body.GrowCells();
            w.Paths.Grow();
            w.Structure.Touch();
            w.Log.Add(w.Tick, LogKind.Ship, $"{v.Name}{(v.Wreck ? "을" : "를")} 아래에 붙였다 — 선외 작업 구역이 {need}줄 넓어졌다");
        }
        if (!v.Wreck) return true;
        // 방 셋: 칸막이 두 줄 (가운데 줄에 구멍)
        int ix0 = v.X0 + 1, ix1 = v.X0 + v.W - 2, iy0 = v.Y0 + 1, iy1 = v.Y0 + v.H - 2;
        int span = (ix1 - ix0 + 1 - 2) / 3;
        int a0 = ix0, a1 = ix0 + span - 1, b0 = a1 + 2, b1 = b0 + span - 1, c0 = b1 + 2, c1 = ix1;
        var kinds = new[] { 0, 1, 2, 3 }.OrderBy(_ => R.Float()).Take(3).OrderBy(k => k == 0 ? 0 : 1).ToArray();
        int[] hz = { 0, 1, 2, 3 };
        for (int i = 0; i < 3; i++)
        {
            var (x0, x1) = i == 0 ? (a0, a1) : i == 1 ? (b0, b1) : (c0, c1);
            int kind = kinds[i];
            v.Rooms.Add(new WreckRoom
            {
                Id = i, Kind = kind, Name = kind switch { 0 => "조종실", 1 => "선실", 2 => "화물칸", _ => "기관실" }, X0 = x0, X1 = x1, Y0 = iy0, Y1 = iy1,
                Hazard = i == 0 ? (R.Chance(0.5f) ? 1 : 0) : hz[R.Range(1, 4)], Pieces = 2 + (x1 - x0 + 1) / 2,
            });
            for (int x = x0; x <= x1; x++) for (int y = iy0; y <= iy1; y++) v.Moored.Add(new Cell(x, y));
        }
        int mid = (iy0 + iy1) / 2;
        v.Moored.Add(new Cell(a1 + 1, mid));
        v.Moored.Add(new Cell(b1 + 1, mid));
        for (int y = v.CollarTop; y <= v.Y0; y++) v.Moored.Add(new Cell(dock, y));
        foreach (var c in v.Moored) w.Paths.MooredCells.Add(c);
        w.Paths.Invalidate();
        return true;
    }

    /// <summary>난파선의 옛 승무원 · 남은 기록 · 생존자.</summary>
    private void Fill(DockVisit v)
    {
        var w = _w;
        int n = R.Range(3, 6);
        var taken = w.Crew.Select(c => c.Name).ToList();
        for (int i = 0; i < n; i++) { var nm = NameGen.Newcomer(w.Seed, 300 + v.Id * 10 + i, taken); taken.Add(nm); v.Crew.Add(nm); }
        int day = R.Range(9, 70);
        foreach (var r in v.Rooms)
        {
            string who = v.Crew[r.Id % v.Crew.Count], other = v.Crew[(r.Id + 1) % v.Crew.Count];
            var (kind, role, text) = r.Kind switch
            {
                0 => (0, "함장", $"{day}일째. 주 동력이 끊겼다. 예비 전지로 엿새는 버틴다. 구조 신호는 계속 보낸다 — 누가 이걸 듣는다면, {other}에게 미안하다고 전해 주시오."),
                1 => (1, "항해사", $"{other}에게 — 창밖에 별이 너무 많아. 돌아가면 그 바닷가에 꼭 같이 가자. 산소가 얼마 안 남았대. 무섭지는 않아."),
                2 => (2, "갑판원", $"{who} 여기 있었다 — 남은 물은 다 선실로 보냈다. {day}일, 아직 살아 있다."),
                _ => (3, "기관사", $"기관실 {who}. 냉각수가 다 샜다. 원자로는 내렸다. 다들 구명정으로 — 나는 문을 닫고 간다."),
            };
            v.Records.Add(new LastRecord { Who = who, Role = role, Kind = kind, Text = text, Room = r.Id });
        }
        var cabin = v.Rooms.FirstOrDefault(r => r.Kind == 1);
        if (cabin != null && R.Chance(0.4f)) cabin.Survivor = true;
    }

    // ─────────────────────────────── 기밀 · 기압 ───────────────────────────────

    private void SealCard(DockVisit v)
    {
        var w = _w;
        var card = w.Automation.Asks.Propose("dock:" + v.Id, "docktest", HatchRoom(v), "도킹 고리 압력 유지 시험 — 끝까지",
            $"{v.Name} · 고리 이음 {(v.Wreck ? "휘어 있을 수 있다 (난파선)" : "저쪽 규격")} · 저쪽 {v.OtherKpa:0}kPa", "30분 압력 유지 → 떨어지면 다시 물린다 (여는 게 늦어진다)",
            20f, null, (world, pr) => { v.FullTest = true; },
            (world, pr) => v.Leaked ? (v.FullTest == true ? -1 : 1, v.FullTest == true ? "틀렸다 — 끝까지 했는데도 샜다" : "맞았다 — 건너뛴 이음이 샜다")
                : v.Opened >= 0 && world.Tick - v.Opened > SimTime.Hours(6) ? (v.FullTest == true ? 1 : -1, v.FullTest == true ? "맞았다 — 고리는 멀쩡하다" : "틀렸다 — 짧게 봐도 됐다") : null);
        v.CardId = card.Id;
    }

    /// <summary>기밀 확인 손길 (에어락 안쪽 · 사람 한 명).</summary>
    internal void SealWorkTick(CrewMember c, DockVisit v)
    {
        float rate = (0.75f + 0.5f * c.RawSkill(Skill.Mechanics)) / SimTime.Minutes(v.FullTest == true ? 35 : 20);
        if (!ComputerOn) rate *= 0.7f; // 손으로 압력계를 본다
        v.SealWork = MathF.Min(1f, v.SealWork + rate);
        v.SealBy = c.Id;
    }

    internal void FinishSeal(CrewMember c, DockVisit v)
    {
        var w = _w;
        if (v.Stage != DockStage.Seal || v.SealWork < 1f) return;
        v.SealBy = -1;
        Stats.SealChecks++;
        float bar = v.FullTest == true ? 0.7f : 0.5f;
        float drop = MathF.Max(0f, (0.85f - v.SealQ) * 2.4f);
        if (ComputerOn) { v.LeakRead = drop; Stats.ComputerReads++; }
        if (v.SealQ < bar)
        {
            v.SealQ = MathF.Min(1f, v.SealQ + 0.3f + 0.2f * c.RawSkill(Skill.Mechanics));
            v.Reseats++;
            Stats.Reseats++;
            v.SealWork = 0f;
            c.Say(w, Persona.Say(c, "바늘이 내려간다 — 고리를 다시 물려야겠다"));
            if (ComputerOn)
                w.Automation.Book.Add(ActKind.Advice, HatchRoom(v), $"도킹 고리 압력 유지: 분당 {drop:0.0}kPa 떨어진다", "이음이 덜 물렸다 — 이대로 열면 고리 옆이 샌다", "고리 다시 물리기", "한 번 더 시험", "dock:leak:" + v.Id, SimTime.Minutes(20));
            w.Log.Add(w.Tick, LogKind.Work, $"도킹 고리 압력이 샌다 — 다시 물린다 ({v.Name})", c.Id);
            Version++;
            return;
        }
        v.Checked = true;
        c.Say(w, Persona.Say(c, v.Wreck ? "고리는 단단하다. 저쪽은 진공 — 우주복 입고 들어가자" : "고리 단단하다. 기압 맞추고 연다"));
        if (ComputerOn)
        {
            w.Automation.Book.Add(ActKind.Advice, HatchRoom(v), $"도킹 고리 압력 유지: 분당 {drop:0.0}kPa — 괜찮다", v.Wreck ? "저쪽 기압 0 · 통로는 진공" : $"저쪽 {v.OtherKpa:0}kPa — 밸브로 맞춘다", "문 열기", "", "dock:ok:" + v.Id, SimTime.Minutes(20));
            Stats.ComputerReads++;
        }
        Open(v, c);
    }

    /// <summary>문을 연다 (다른 배: 기압을 맞춘 뒤 · 난파선: 통로로 나갈 길이 열린다).</summary>
    private void Open(DockVisit v, CrewMember? by)
    {
        var w = _w;
        var room = HatchRoom(v);
        if (!v.Checked)
        {
            // 시험 없이 열었다: 덜 물린 고리는 곁의 외판을 샌다
            v.Skipped = true;
            Stats.Skips++;
            if (v.SealQ < 0.6f) Leak(v);
        }
        if (!v.Wreck && v.Equalized < 1f && !ComputerOn)
        {
            // 기압을 안 맞추고 열면 문이 튕긴다
            v.DoorJolt = true;
            Stats.Jolts++;
            if (by != null)
            {
                NeedsSystem.AddInjury(by.Vitals, 0.08f, "튕긴 해치 문");
                Stats.Hurts++;
                by.Say(w, Persona.Say(by, "귀가 먹먹하다 — 기압을 덜 맞췄다"));
            }
        }
        v.Equalized = 1f;
        v.Stage = DockStage.Open;
        v.StageSince = w.Tick;
        v.Opened = w.Tick;
        v.LeaveAt = w.Tick + (v.Wreck ? SimTime.TicksPerDay * 2 : SimTime.Hours(8));
        w.Log.Add(w.Tick, LogKind.Ship, v.Wreck ? $"{v.Name} — 도킹 통로가 열렸다 (진공 · 어둠)" : $"{v.Name} — 해치를 열었다 · 저쪽 사람들이 건너온다", by?.Id ?? -1);
        if (!v.Wreck) BoardGuests(v);
        Version++;
    }

    private void Leak(DockVisit v)
    {
        var w = _w;
        var hatch = w.Ship.Doors[v.HatchDoor];
        foreach (var d in Cell.Dirs8)
        {
            var c = hatch.Cell + d;
            if (w.Ship.WallAt(c) is not WallState ws || !ws.IsHull || ws.Breach > 0f) continue;
            ws.Breach = 0.06f + 0.1f * (0.6f - v.SealQ);
            MarkLog.Add(ws.Marks, w.Tick, $"도킹 고리 옆 이음이 샌다 ({v.Name})");
            v.Leaked = true;
            v.LeakWall = c;
            Stats.Leaks++;
            var room = HatchRoom(v);
            w.RaiseAlert($"{room?.Name ?? "에어락"} — 도킹 고리 옆이 샌다", room, AlertLevel.Critical, shipWide: true);
            w.History.Add(w, HistoryKind.Damage, $"{v.Name} 도킹 — 기밀 시험을 건너뛰고 열었다가 고리 옆 외판이 샜다", room, at: c, log: true);
            w.Board.RequestScan();
            return;
        }
    }

    // ─────────────────────────────── 틱 ───────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        if (Off || w.Tick < _next) return;
        _next = w.Tick + SimTime.Minutes(1);
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (Active is DockVisit v)
        {
            switch (v.Stage)
            {
                case DockStage.Seal:
                    if (v.FullTest == null && v.CardId >= 0 && w.Automation.Asks.All.FirstOrDefault(x => x.Id == v.CardId) is { } pr && pr.State != ProposalState.Pending) v.FullTest = pr.Accepted;
                    if (!v.Wreck && v.Checked && v.Equalized < 1f) v.Equalized = MathF.Min(1f, v.Equalized + 0.1f);
                    // 아무도 손을 못 대고 반나절 — 기다리다 못해 연다 (위기 중이면 더 기다린다)
                    if (w.Tick - v.StageSince > SimTime.Hours(10) && !Crisis.Acting(w)) Open(v, null);
                    break;
                case DockStage.Open:
                case DockStage.Salvage:
                    if (v.Wreck) WreckTick(v); else ShipTick(v);
                    break;
                case DockStage.Leaving:
                    if (w.Crew.All(c => c.Dead || !InWreck(c, v))) Release(v);
                    break;
            }
            Memorial(v);
        }
        UpdateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    public bool InWreck(CrewMember c, DockVisit v) => c.Outside && v.Covers(c.Cell);
    public WreckRoom? WreckRoomOf(CrewMember c) => Active is DockVisit v && v.Wreck && c.Outside ? v.RoomAt(c.Cell) : null;

    private void WreckTick(DockVisit v)
    {
        var w = _w;
        // 어둠 · 진공 · 위험: 안에 있는 사람마다
        foreach (var c in w.Crew)
        {
            if (c.Dead || !c.Outside || v.RoomAt(c.Cell) is not WreckRoom r) continue;
            if (c.Fears.Contains(Fear.Dark)) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.01f);
            bool moving = c.IsMoving;
            // 어둠: 헬멧 등만으로는 발밑 잔해가 안 보인다
            if (moving && R.Chance(r.HazardKnown ? 0.004f : 0.012f))
            {
                Stats.DarkTrips++;
                w.EvaRisk.HitPerson(c, 0.05f, c.Position + new Vector2(0.5f, 0f), "어둠 속 잔해에 걸렸다");
                MarkLog.Add(c.Memory.Marks, w.Tick, $"{v.Name} 어둠 속에서 잔해에 걸렸다");
            }
            if (r.Hazard == 1 && R.Chance(r.HazardKnown ? 0.01f : 0.03f))
            {
                Stats.Hurts++;
                w.EvaRisk.HitPerson(c, 0.12f, r.Spot.Center, "찢긴 판 모서리");
                Know(v, r, c, "찢긴 판 모서리에 우주복이 긁혔다");
            }
            // 산소: 주컴퓨터가 안에 있는 사람의 탱크를 본다
            if (c.Suit is SuitState s && s.Oxygen < 1f && ComputerOn)
                w.Automation.Book.Add(ActKind.Advice, null, $"{c.Name} 우주복 산소 {s.Oxygen * 60f:0}분", "난파선 안 — 돌아올 시간", "복귀", "", "dock:o2:" + c.Id, SimTime.Minutes(30));
        }
        if (v.Stage == DockStage.Open && v.Rooms.All(r => r.Searched))
        {
            v.Stage = DockStage.Salvage;
            v.StageSince = w.Tick;
            w.Log.Add(w.Tick, LogKind.Ship, $"{v.Name} — 다 뒤졌다 · 이제 잘라 온다");
            Version++;
        }
        if (v.Stage == DockStage.Salvage && v.Rooms.All(r => r.Gone || r.Collapsed) || w.Tick > v.LeaveAt) Leave(v);
    }

    private void Know(DockVisit v, WreckRoom r, CrewMember c, string what)
    {
        var w = _w;
        MarkLog.Add(c.Memory.Marks, w.Tick, $"{v.Name} {r.Name}: {what}");
        if (r.HazardKnown) return;
        r.HazardKnown = true;
        w.Log.Add(w.Tick, LogKind.Warning, $"{v.Name} {r.Name} — {what}", c.Id);
        c.Say(w, Persona.Say(c, r.Hazard switch { 1 => "여기 판이 칼날이다 — 조심해", 2 => "이 격벽, 흔들린다", _ => "연료관이 얼어 있다 — 불꽃 조심" }));
        if (ComputerOn)
        {
            w.Automation.Book.Add(ActKind.Advice, null, $"{v.Name} {r.Name}: {what}", r.Hazard == 2 ? "격벽이 버티지 못한다 — 절단은 그 방 밖에서부터" : "그 방은 천천히", "주의 표시", "", "dock:hz:" + v.Id + ":" + r.Id, SimTime.Hours(2));
            Stats.ComputerReads++;
        }
        Version++;
    }

    private void ShipTick(DockVisit v)
    {
        var w = _w;
        if (v.JointMachine < 0 && !v.JointDone)
        {
            var m = w.Ship.Machines.Where(x => x.Faults.Count == 0 && !x.Body.Room.Detached).OrderByDescending(x => x.Wear).ThenBy(x => x.Body.Id).FirstOrDefault();
            if (m != null && m.Wear > 0.1f) v.JointMachine = m.Body.Id; else v.JointDone = true;
        }
        if (w.Tick > v.LeaveAt || v.JointDone && v.TradeDone && w.Tick - v.Opened > SimTime.Hours(3)) Leave(v);
    }

    /// <summary>같이 손보기 손길.</summary>
    internal void JointTick(CrewMember c, DockVisit v)
    {
        v.JointBy = c.Id;
        v.JointWork = MathF.Min(1f, v.JointWork + (0.8f + 0.4f * c.RawSkill(Skill.Mechanics)) / SimTime.Hours(1.2f));
    }

    internal void FinishJoint(CrewMember c, DockVisit v)
    {
        var w = _w;
        if (v.JointDone || v.JointWork < 1f || v.JointMachine < 0) return;
        if (JointOf(v) is not Machine m) { v.JointDone = true; return; }
        m.Wear = MathF.Max(0.03f, m.Wear * 0.35f);
        m.Condition = MathF.Max(m.Condition, 0.9f);
        v.JointDone = true;
        v.JointBy = -1;
        Stats.Joint++;
        c.Practice(Skill.Mechanics, 0.02f);
        w.Log.Add(w.Tick, LogKind.Work, $"{v.Name} 기관사와 같이 {Ko.EulReul(m.Body.Name)} 손봤다 — 저쪽 요령을 하나 배웠다", c.Id);
        MarkLog.Add(m.Body.Room.Marks, w.Tick, $"{v.Name} 기관사와 같이 손봤다 ({c.Name})");
        Version++;
    }

    public Machine? JointOf(DockVisit v) => v.JointMachine < 0 ? null : _w.Ship.Machines.FirstOrDefault(x => x.Body.Id == v.JointMachine);

    internal void TradeTick(CrewMember c, DockVisit v)
    {
        v.TradeBy = c.Id;
        v.TradeWork = MathF.Min(1f, v.TradeWork + 1f / SimTime.Minutes(40));
    }

    /// <summary>물자 교환: 남는 것 둘을 내주고 모자란 것 둘을 받는다 (해치 앞 상자).</summary>
    internal void FinishTrade(CrewMember c, DockVisit v)
    {
        var w = _w;
        if (v.TradeDone || v.TradeWork < 1f) return;
        v.TradeDone = true;
        v.TradeBy = -1;
        var ship = w.Ship;
        var give = new[] { ItemKind.MetalOre, ItemKind.Ice, ItemKind.Silicate, ItemKind.Carbon, ItemKind.Produce }.OrderByDescending(ship.CountStored).ThenBy(k => (int)k).Take(2).ToList();
        var want = new[] { ItemKind.Filter, ItemKind.Sealant, ItemKind.Cable, ItemKind.Fuse, ItemKind.MedKit, ItemKind.Gasket, ItemKind.Lubricant }.OrderBy(ship.CountStored).ThenBy(k => (int)k).Take(2).ToList();
        int gave = 0;
        foreach (var k in give) for (int i = 0; i < 3; i++) if (ItemsV15.Use(w, k)) gave++;
        foreach (var k in want)
        {
            int got = Store(k, gave > 0 ? 2 : 1);
            if (got > 0) v.Trades.Add($"{ItemKinds.Name(k)} {got}");
        }
        Stats.Trades++;
        w.Log.Add(w.Tick, LogKind.Work, $"{v.Name}{(v.Name.EndsWith("배") ? "와" : "과")} 물자를 바꿨다 — {string.Join(" · ", give.Select(ItemKinds.Name))}을 주고 {string.Join(" · ", v.Trades)}", c.Id);
        Version++;
    }

    internal int Store(ItemKind k, int n)
    {
        int put = 0;
        foreach (var box in _w.Ship.Containers.Where(f => f.Storage!.Accepts(k)))
        {
            put += box.Storage!.Add(k, n - put);
            if (put >= n) break;
        }
        return put;
    }

    private void BoardGuests(DockVisit v)
    {
        var w = _w;
        var hatch = w.Ship.Doors[v.HatchDoor];
        if (Inner(hatch) is not Cell at) return;
        int n = w.Crew.Count(c => !c.Dead) >= World.MaxCrew - 1 ? 0 : R.Range(1, 3);
        for (int i = 0; i < n; i++)
        {
            var p = w.Passengers.Board(at, v.Name, rescued: false);
            if (p == null) break;
            v.Boarded.Add(p.Id);
            Stats.Guests++;
        }
    }

    // ─────────────────────────────── 뒤지기 · 자르기 ───────────────────────────────

    internal void SearchTick(CrewMember c, DockVisit v, WreckRoom r)
    {
        r.SearchBy = c.Id;
        float rate = (0.7f + 0.6f * c.Traits.Diligence) / SimTime.Minutes(45);
        rate *= 0.6f; // 어둠: 헬멧 등 하나
        if (c.Fears.Contains(Fear.Dark)) rate *= 0.75f;
        if (c.Room == null && c.Suit?.Wear is SuitWear sw && sw.Leaking) rate *= 0.5f;
        r.Search = MathF.Min(1f, r.Search + rate);
    }

    internal void FinishSearch(CrewMember c, DockVisit v, WreckRoom r)
    {
        var w = _w;
        r.SearchBy = -1;
        if (r.Searched || r.Search < 1f) return;
        r.Searched = true;
        Stats.Searches++;
        var lines = new List<string>();
        foreach (var rec in v.Records.Where(x => x.Room == r.Id && x.FoundBy < 0))
        {
            rec.FoundBy = c.Id;
            rec.FoundAt = w.Tick;
            Stats.Records++;
            lines.Add($"{rec.Role} {rec.Who}의 {LastRecord.KindName(rec.Kind)}");
            MarkLog.Add(c.Memory.Marks, w.Tick, $"{v.Name} {r.Name}에서 {rec.Who}의 {LastRecord.KindName(rec.Kind)}을 찾았다");
            Life.Diary(w, c, Persona.Say(c, $"{v.Name}에서 {rec.Who}의 {LastRecord.KindName(rec.Kind)}을 찾았다. 헬멧 등으로 비춰 읽었다 — \"{rec.Text}\""));
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.05f);
        }
        int pieces = 0;
        bool survivor = false;
        if (r.Survivor)
        {
            r.Survivor = false;
            survivor = true;
            lines.Add("얼어붙은 수면 캡슐 — 아직 숨이 붙어 있다");
            c.Say(w, Persona.Say(c, "여기 사람이 있다 — 살아 있어!"));
        }
        else if (R.Chance(0.6f)) pieces = 1;
        (int visit, int room, int pieces, bool survivor) cur = _carry.TryGetValue(c.Id, out var had) ? had : (v.Id, r.Id, 0, false);
        _carry[c.Id] = (v.Id, r.Id, cur.pieces + pieces, cur.survivor || survivor);
        if (lines.Count > 0) w.Log.Add(w.Tick, LogKind.Life, $"{v.Name} {r.Name}을 뒤졌다 — {string.Join(" · ", lines)}", c.Id);
        else w.Log.Add(w.Tick, LogKind.Life, $"{v.Name} {r.Name}을 뒤졌다 — 얼어붙은 컵 · 떠다니는 종이 · 쓸 만한 건 별로 없다", c.Id);
        Version++;
    }

    internal void CutTick(CrewMember c, DockVisit v, WreckRoom r)
    {
        var w = _w;
        r.CutBy = c.Id;
        r.CutWork = MathF.Min(1f, r.CutWork + (0.7f + 0.5f * c.RawSkill(Skill.Mechanics)) / SimTime.Minutes(40));
        // 흔들리는 격벽 · 얼어붙은 연료관: 자르는 중에 터진다
        if (r.Hazard == 2 && !r.Collapsed && R.Chance(r.HazardKnown ? 0.0015f : 0.004f))
        {
            r.Collapsed = true;
            Stats.Collapses++;
            Stats.Hurts++;
            w.EvaRisk.HitPerson(c, 0.22f, r.Spot.Center + new Vector2(0f, -1f), "무너진 격벽");
            Know(v, r, c, "자르던 격벽이 무너졌다");
            w.History.Add(w, HistoryKind.Casualty, $"{v.Name} {r.Name} — 자르던 격벽이 무너져 {Ko.IGa(c.Name)} 깔렸다", null, new[] { c }, log: true);
        }
        else if (r.Hazard == 3 && R.Chance(r.HazardKnown ? 0.002f : 0.006f))
        {
            Stats.Hurts++;
            w.EvaRisk.HitPerson(c, 0.1f, r.Spot.Center, "얼어붙은 연료관 불꽃");
            Know(v, r, c, "절단기 불꽃에 남은 연료가 번쩍했다");
        }
    }

    internal void FinishCut(CrewMember c, DockVisit v, WreckRoom r)
    {
        var w = _w;
        r.CutBy = -1;
        if (r.CutWork < 1f || r.Gone) return;
        r.CutWork = 0f;
        r.Cut++;
        (int visit, int room, int pieces, bool survivor) cur = _carry.TryGetValue(c.Id, out var had) ? had : (v.Id, r.Id, 0, false);
        _carry[c.Id] = (v.Id, r.Id, cur.pieces + 1, cur.survivor);
        Version++;
    }

    /// <summary>EVA에서 돌아왔다: 들고 온 조각을 창고에 · 기록을 내놓는다 · 생존자를 태운다.</summary>
    internal void Deliver(CrewMember c)
    {
        var w = _w;
        if (!_carry.Remove(c.Id, out var got)) return;
        var v = Visits[got.visit];
        var r = v.Rooms[got.room];
        var lines = new List<string>();
        for (int i = 0; i < got.pieces; i++)
        {
            var k = r.Kind switch
            {
                0 => R.Chance(0.5f) ? ItemKind.Electronics : ItemKind.Sensor,
                1 => R.Chance(0.5f) ? ItemKind.Plate : ItemKind.Cable,
                2 => R.Chance(0.6f) ? ItemKind.Structure : ItemKind.Plate,
                _ => R.Chance(0.5f) ? ItemKind.Pump : ItemKind.Cable,
            };
            int n = Store(k, 1) > 0 ? 1 : 0;
            if (n == 0) continue;
            v.Salvaged[k] = v.Salvaged.GetValueOrDefault(k) + 1;
            Stats.Items++;
            lines.Add(ItemKinds.Name(k));
        }
        Stats.Pieces += got.pieces;
        if (lines.Count > 0) w.Log.Add(w.Tick, LogKind.Work, $"{v.Name}에서 잘라 온 것을 창고에 넣었다 — {string.Join(" · ", lines)}", c.Id);
        foreach (var rec in v.Records.Where(x => x.FoundBy == c.Id && !x.Home)) rec.Home = true;
        if (got.survivor && w.Ship.RoomAt(c.Cell) != null)
        {
            var p = w.Passengers.Board(c.Cell, v.Name, rescued: true);
            if (p != null)
            {
                Stats.Survivors++;
                v.Boarded.Add(p.Id);
                p.Affinity[c.Id] = 0.6f;
                w.History.Add(w, HistoryKind.Bond, $"{v.Name}의 수면 캡슐에서 {Ko.EulReul(p.Name)} 데려왔다 ({c.Name})", null, new[] { c, p }, log: true);
            }
        }
        // 기록을 가져왔으면 저녁에 다 같이 읽는다
        if (v.Records.Any(x => x.Home) && v.MemorialAt < 0) ScheduleMemorial(v);
        Version++;
    }

    public bool Carrying(CrewMember c) => _carry.ContainsKey(c.Id);

    private void Leave(DockVisit v)
    {
        var w = _w;
        if (v.Stage == DockStage.Leaving) return;
        v.Stage = DockStage.Leaving;
        v.StageSince = w.Tick;
        if (v.Wreck && w.Crew.Any(c => !c.Dead && InWreck(c, v)) && ComputerOn)
            w.Automation.Speak.Announce(w.Automation.Voice.Style($"{v.Name}에서 떨어진다 — 안에 있는 사람은 돌아와라"), HatchRoom(v), 1);
        Version++;
    }

    private void Release(DockVisit v)
    {
        var w = _w;
        foreach (var c in v.Moored) w.Paths.MooredCells.Remove(c);
        w.Paths.Invalidate();
        v.Stage = DockStage.Gone;
        v.StageSince = w.Tick;
        string got = v.Salvaged.Count > 0 ? " · " + string.Join(" · ", v.Salvaged.Select(kv => $"{ItemKinds.Name(kv.Key)} {kv.Value}")) : "";
        w.Log.Add(w.Tick, LogKind.Ship, v.Wreck ? $"{v.Name}에서 떨어졌다{got}" : $"{v.Name}{(v.Name.EndsWith("배") ? "가" : "이")} 떠났다");
        Version++;
    }

    // ─────────────────────────────── 추모 ───────────────────────────────

    private void ScheduleMemorial(DockVisit v)
    {
        var w = _w;
        long day0 = w.Tick - w.Tick % SimTime.TicksPerDay;
        long at = day0 + SimTime.Hours(19);
        if (at < w.Tick + SimTime.Hours(1)) at = w.Tick + SimTime.Hours(1);
        if (at > w.Tick + SimTime.Hours(10)) at = w.Tick + SimTime.Hours(2);
        var mess = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Mess or RoomType.Lounge).OrderBy(r => r.Id).FirstOrDefault() ?? HatchRoom(v);
        if (mess == null) return;
        v.MemorialAt = at;
        v.MemorialRoom = mess.Id;
        var wall = mess.Cells.Where(c => w.Ship.Grid.Kind(c + new Cell(0, -1)) == TileKind.Wall).OrderBy(c => c.X).ThenBy(c => c.Y).ToList();
        v.Plaque = wall.Count > 0 ? wall[wall.Count / 2] : mess.Cells[0];
        var finder = v.Records.Where(x => x.Home).Select(x => w.Crew[x.FoundBy]).FirstOrDefault(c => !c.Dead);
        v.Reader = finder?.Id ?? -1;
        int hh = (int)(at % SimTime.TicksPerDay / SimTime.TicksPerHour);
        w.Log.Add(w.Tick, LogKind.Life, $"{v.Name}에서 가져온 기록 — {hh}시에 {mess.Name}에서 다 같이 읽기로 했다", finder?.Id ?? -1);
        if (ComputerOn) w.Automation.Speak.Announce(w.Automation.Voice.Style($"{hh}시, {mess.Name}. {v.Name} 사람들의 마지막 기록을 같이 듣자"), mess, 1);
        Version++;
    }

    public bool MemorialOn(DockVisit v) => v.MemorialAt >= 0 && !v.Mourned && _w.Tick >= v.MemorialAt - SimTime.Minutes(30) && _w.Tick < v.MemorialAt + SimTime.Minutes(40);
    public DockVisit? MemorialNow => Visits.FirstOrDefault(MemorialOn);

    private void Memorial(DockVisit v)
    {
        var w = _w;
        if (v.MemorialAt < 0 || v.Mourned || w.Tick < v.MemorialAt + SimTime.Minutes(25)) return;
        var room = w.Ship.Rooms[v.MemorialRoom];
        var here = w.Crew.Where(c => !c.Dead && c.Room == room && c.IsAwake).OrderBy(c => c.Id).ToList();
        if (here.Count < 2 && w.Tick < v.MemorialAt + SimTime.Hours(3)) return; // 사람이 모일 때까지 조금 더
        v.Mourned = true;
        Stats.Memorials++;
        Stats.Mourners += here.Count;
        foreach (var c in here) v.Mourners.Add(c.Id);
        var reader = here.FirstOrDefault(c => c.Id == v.Reader) ?? (w.Command.Captain is CrewMember cap && here.Contains(cap) ? cap : here.FirstOrDefault());
        var recs = v.Records.Where(x => x.Home).ToList();
        foreach (var rec in recs)
        {
            if (rec.Kind == 3 && ComputerOn) { w.Automation.Speak.Announce($"({rec.Who}의 목소리) {rec.Text}", room, 1); Stats.ComputerReads++; }
            else reader?.Say(w, $"\"{rec.Text}\"");
        }
        string names = string.Join(" · ", v.Crew);
        foreach (var c in here)
        {
            c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.06f);
            c.Needs.Social = MathF.Min(1f, c.Needs.Social + 0.3f);
            MarkLog.Add(c.Memory.Marks, w.Tick, $"{v.Name} 사람들({names})의 마지막 기록을 같이 들었다");
            foreach (var o in here) if (o != c) c.Affinity[o.Id] = MathF.Min(1f, c.AffinityTo(o) + 0.04f);
        }
        if (reader != null) Life.Diary(w, reader, Persona.Say(reader, $"{v.Name} 사람들의 기록을 읽었다. 목이 메었다. 이름판을 식당 벽에 걸었다 — {names}"));
        MarkLog.Add(room.Marks, w.Tick, $"{v.Name} 이름판 ({names})");
        w.History.Add(w, HistoryKind.Memory, $"{v.Name} — 남은 기록 {recs.Count}개를 가져와 {here.Count}명이 함께 읽고 묵념했다 · {room.Name} 벽에 이름판 ({names})", room, here, log: true);
        Version++;
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Visits.Count);
        foreach (var v in Visits)
        {
            I((int)v.Stage); F(v.SealQ); F(v.SealWork); I(v.Reseats); I(v.Leaked ? 1 : 0); I(v.Opened); I(v.MemorialAt); I(v.Mourned ? 1 : 0);
            foreach (var r in v.Rooms) { F(r.Search); I(r.Cut); I(r.Collapsed ? 1 : 0); }
            foreach (var kv in v.Salvaged) { I((int)kv.Key); I(kv.Value); }
        }
        I(_carry.Count);
        I(Stats.Items); I(Stats.Hurts); I(Stats.Guests);
    }
}
