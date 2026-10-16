using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v23 항해의 목적과 끝: 항해마다 의뢰받은 임무가 있다 — 배 종류(가진 방)를 보고 화물 · 탐사 · 구조 · 이민 · 채굴 가운데 하나.
//   기한이 있어 늦어지면 주컴퓨터가 행성 착륙을 건너뛰고 지름길을 고른다. 항해 중간에 갈림길(지름길 ↔ 돌아가는 길)과
//   늦게 오는 통신(며칠 전에 보낸 소식 — 기한 · 목적지 · 할당량 · 보상이 바뀐다 · 구조 요청)이 있다.
//   목적지에 닿으면 임무를 매듭짓고(도착 장면) 항해의 책 한 권을 남긴다 — 다음 항해로 사람 · 로봇 · 주컴퓨터의 경험 · 배가 이어진다.

public enum MissionKind : byte { Cargo, Survey, Rescue, Colony, Mining }

public sealed class VoyageMission
{
    public int Voyage { get; init; }
    public MissionKind Kind { get; init; }
    public string Client { get; init; } = "";
    public string Title { get; set; } = "";
    public string Brief { get; set; } = "";
    public long Start { get; init; }
    public long Deadline { get; set; }
    public int Goal { get; set; }
    public int Progress { get; set; }
    /// <summary>화물 상자 · 이주민 · 구조한 사람 (배에 실린 것).</summary>
    public int Aboard { get; set; }
    public int Lost { get; set; }
    public float Reward { get; set; }
    public List<int> People { get; } = new();
    public List<string> Notes { get; } = new();
    public bool ForkDone { get; set; }
    public int CommsHeard { get; set; }
    public bool Finished { get; set; }
    public string Result { get; set; } = "";
    public bool Success { get; set; }
    public bool Late { get; set; }
}

public sealed class VoyageBook
{
    public int Number { get; init; }
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public float Days { get; init; }
    public string Mission { get; init; } = "";
    public string Result { get; init; } = "";
    public bool Success { get; init; }
    public List<string> Chapters { get; } = new();
    public List<string> Joined { get; } = new();
    public List<string> Left { get; } = new();
    public List<string> Lost { get; } = new();
    public List<string> Planets { get; } = new();
    public List<int> Scenes { get; } = new();
    public string Carried { get; set; } = "";
}

public sealed partial class PurposeSystem
{
    private readonly World _w;
    private readonly Rng _rng;
    /// <summary>시험용: 갈림길 · 통신을 끈다.</summary>
    public static bool Off;
    public VoyageMission? Now { get; private set; }
    public List<VoyageMission> Past { get; } = new();
    public List<VoyageBook> Books { get; } = new();
    public long NextComms { get; private set; } = -1;
    public long LastArrival { get; private set; } = -1;
    private readonly HashSet<int> _crewAtStart = new();
    private long _nextCargo;

    public PurposeSystem(World w)
    {
        _w = w;
        _rng = new Rng(unchecked(w.Seed * 6421 + 97));
    }

    public static string KindName(MissionKind k) => k switch
    {
        MissionKind.Cargo => "화물", MissionKind.Survey => "탐사", MissionKind.Rescue => "구조", MissionKind.Colony => "이민", _ => "채굴",
    };

    /// <summary>배가 가진 방으로 맡을 만한 임무 (무게).</summary>
    public static List<(MissionKind k, float w)> Roles(Ship ship)
    {
        int Count(params RoomType[] t) => ship.Rooms.Count(r => !r.Detached && t.Contains(r.Kind));
        var list = new List<(MissionKind, float)>
        {
            (MissionKind.Cargo, MathF.Min(4f, 1.2f + 0.8f * Count(RoomType.Cargo, RoomType.Storage))),
            (MissionKind.Survey, MathF.Min(4f, 0.3f + 1.2f * Count(RoomType.Lab, RoomType.Observatory, RoomType.ElectronicsLab, RoomType.Archive))),
            (MissionKind.Rescue, MathF.Min(3.5f, 0.3f + 1.0f * Count(RoomType.Medbay, RoomType.Triage, RoomType.Hyperbaric, RoomType.Quarantine))),
            (MissionKind.Colony, MathF.Min(4f, 0.1f + 1.5f * Count(RoomType.School, RoomType.Centrifuge, RoomType.Theater, RoomType.PrivateCabins))),
            (MissionKind.Mining, 0.3f + 0.8f * MathF.Min(3f, ship.FurnitureOf(FurnitureType.Collector).Count() + ship.FurnitureOf(FurnitureType.Refinery).Count())),
        };
        return list;
    }

    private static readonly string[] Clients = { "은하 운송 조합", "변경 개척청", "자유 탐사 학회", "궤도 구조 본부", "북쪽 광업 연합", "이주 협회 '새뜰'", "상선 길드 '두레'" };

    /// <summary>새 항해: 임무를 받는다 (항로를 짠 뒤).</summary>
    public void Begin(MissionKind? force = null)
    {
        var w = _w;
        var v = w.Voyage;
        var roles = Roles(w.Ship);
        if (v.Number == 1) roles.RemoveAll(r => r.k == MissionKind.Colony); // 이주민은 두 번째 항해부터 (첫 항해는 배를 길들인다)
        float sum = roles.Sum(r => r.w), x = _rng.Range(0f, sum);
        var kind = roles.Last().k;
        foreach (var (k, wt) in roles) { x -= wt; if (x <= 0f) { kind = k; break; } }
        if (force is MissionKind fk) kind = fk;
        float days = v.TotalDays;
        int size = w.Crew.Count(c => !c.Dead);
        var m = new VoyageMission
        {
            Voyage = v.Number, Kind = kind, Client = Clients[_rng.Range(0, Clients.Length)], Start = w.Tick,
            Deadline = w.Tick + (long)((days * 1.18f + 1f) * SimTime.TicksPerDay),
        };
        switch (kind)
        {
            case MissionKind.Cargo:
                m.Goal = m.Aboard = 6 + size / 2 + _rng.Range(0, 5);
                m.Title = $"{v.Destination}까지 화물 {m.Goal}상자";
                m.Brief = $"{m.Client}의 화물을 기한 안에 상하지 않게 나른다 — 화물칸이 불타거나 뚫리면 상자를 잃는다";
                m.Reward = 6f * m.Goal;
                break;
            case MissionKind.Survey:
                m.Goal = 5 + _rng.Range(0, 4);
                m.Title = $"항로 관측 자료 {m.Goal}건";
                m.Brief = $"{m.Client}가 청했다 — 볼 만한 구간 · 내려앉은 행성 · 살핀 곳마다 자료가 쌓인다";
                m.Reward = 10f * m.Goal;
                break;
            case MissionKind.Rescue:
                m.Goal = 1;
                m.Title = $"조난선 구조 대기 — {v.Destination}로 데려간다";
                m.Brief = $"{m.Client}의 구조선으로 나선다 — 항로 어딘가에서 조난 신호가 오면 들러 사람을 태워 목적지로";
                m.Reward = 50f;
                break;
            case MissionKind.Colony:
                m.Goal = Math.Clamp(World.MaxCrew - size, 0, 2 + size / 4);
                m.Title = $"이주민 {m.Goal}명을 {v.Destination}로";
                m.Brief = $"{m.Client}의 이주민을 태워 새 터까지 — 모두 무사히 내려야 한다";
                m.Reward = 14f * Math.Max(1, m.Goal);
                break;
            default:
                m.Goal = 3 + _rng.Range(0, 4);
                m.Title = $"희귀 소재 {m.Goal}개를 {v.Destination}에";
                m.Brief = $"{m.Client}의 할당량 — 소행성대 · 행성 · 원정에서 캐서 목적지까지 싣고 간다";
                m.Reward = 12f * m.Goal;
                break;
        }
        Now = m;
        _crewAtStart.Clear();
        foreach (var c in w.Crew.Where(c => !c.Dead && !c.LeftShip)) _crewAtStart.Add(c.Id);
        if (kind == MissionKind.Colony) BoardColonists(m);
        if (kind == MissionKind.Mining) m.Progress = 0;
        NextComms = w.Tick + SimTime.Hours(_rng.Range(30f, 80f));
        if (v.Number == 1) NextComms = Math.Max(NextComms, SimTime.TicksPerDay * 8 + _rng.Range(0, SimTime.TicksPerDay * 2)); // 첫 항해는 배를 길들이는 동안 조용히
        Note(m, $"임무를 받았다 — {m.Title} ({m.Client}) · 기한 {(m.Deadline - w.Tick) / (float)SimTime.TicksPerDay:0.0}일");
        w.Log.Add(w.Tick, LogKind.Ship, $"{v.Number}번째 항해의 임무 — {KindName(kind)}: {m.Title} · 기한 {(m.Deadline - w.Tick) / (float)SimTime.TicksPerDay:0.0}일 ({m.Client})");
    }

    private void Note(VoyageMission m, string text)
    {
        m.Notes.Add($"{SimTime.Day(_w.Tick)}일 {SimTime.Clock(_w.Tick)} {text}");
        if (m.Notes.Count > 30) m.Notes.RemoveAt(0);
    }

    private void BoardColonists(VoyageMission m)
    {
        var w = _w;
        var dock = w.Ship.RoomsOf(RoomType.Airlock).FirstOrDefault()?.Cells.FirstOrDefault(w.Ship.IsOpenFloor) ?? w.Ship.LiveRooms.SelectMany(r => r.Cells).First(w.Ship.IsOpenFloor);
        for (int i = 0; i < m.Goal; i++)
        {
            if (w.Passengers.Board(dock, w.Voyage.Origin, rescued: false) is not CrewMember c) break;
            c.Joined = $"{w.Voyage.Origin}에서 탄 이주민 ({m.Client})";
            m.People.Add(c.Id);
        }
        m.Aboard = m.People.Count;
        m.Goal = m.People.Count;
    }

    // ─────────────────────────── 항해 중 ───────────────────────────

    /// <summary>기한까지 남은 날 · 남은 항로 (일).</summary>
    public float DaysLeft => Now == null ? 99f : (Now.Deadline - _w.Tick) / (float)SimTime.TicksPerDay;
    public float RouteLeft => _w.Voyage.TotalDays - _w.Voyage.DoneDays;
    /// <summary>늦어지고 있다 (남은 길이 남은 기한의 85%를 넘는다).</summary>
    public bool Behind => Now != null && !Now.Finished && RouteLeft > DaysLeft * 0.85f;

    public void Update(float dt)
    {
        var w = _w;
        if (Now == null && !w.Voyage.Finishing) Begin(); // 새 항해 — 사람이 다 탄 뒤에 임무를 받는다
        var m = Now;
        if (m == null || m.Finished) return;
        if (w.Tick >= _nextCargo) { _nextCargo = w.Tick + SimTime.Minutes(10); WatchCargo(); }
        // 화물 · 이주민 · 구조한 사람을 센다
        if (m.Kind is MissionKind.Colony or MissionKind.Rescue)
        {
            int alive = m.People.Count(id => id < w.Crew.Count && !w.Crew[id].Dead && !w.Crew[id].LeftShip);
            if (alive < m.Aboard) { m.Lost += m.Aboard - alive; Note(m, $"{(m.Kind == MissionKind.Colony ? "이주민" : "구조한 사람")} {m.Aboard - alive}명을 잃었다"); }
            m.Aboard = alive;
        }
        if (m.Kind == MissionKind.Mining) m.Progress = Math.Min(m.Goal, w.Ship.CountStored(ItemKind.Rare));
        if (Off) return;
        if (w.Voyage.Number == 1 && w.Tick < SimTime.TicksPerDay * 8) return;
        // 갈림길: 항로의 절반쯤
        if (!m.ForkDone && w.Voyage.DoneDays >= w.Voyage.TotalDays * 0.4f && !w.Planet.Holding && w.Voyage.Index < w.Voyage.Legs.Count - 2) Fork();
        // 늦게 오는 통신
        if (w.Tick >= NextComms && !w.Planet.Holding && m.CommsHeard < 2) { Comms(); NextComms = w.Tick + SimTime.Hours(_rng.Range(40f, 90f)); }
    }

    /// <summary>화물칸이 불타거나 뚫렸다 — 상자를 잃는다 (불 · 파공이 부른다).</summary>
    public void CargoHit(Room room, string why)
    {
        var m = Now;
        if (m == null || m.Kind != MissionKind.Cargo || m.Finished || m.Aboard <= 0) return;
        if (room.Kind is not (RoomType.Cargo or RoomType.Storage)) return;
        int n = Math.Min(m.Aboard, 1 + _rng.Range(0, 2));
        m.Aboard -= n; m.Lost += n;
        Note(m, $"{room.Name} {why} — 화물 {n}상자를 잃었다 (남은 {m.Aboard})");
        _w.Log.Add(_w.Tick, LogKind.Ship, $"{room.Name} {why} — 맡은 화물 {n}상자가 상했다 (남은 {m.Aboard}/{m.Goal})");
    }

    /// <summary>탐사 자료 (볼 만한 구간 · 행성 · 살핀 곳).</summary>
    public void Data(int n, string what)
    {
        var m = Now;
        if (m == null || m.Kind != MissionKind.Survey || m.Finished) return;
        m.Progress += n;
        Note(m, $"자료 {n}건 — {what} ({m.Progress}/{m.Goal})");
    }

    /// <summary>구조 임무: 조난자를 태웠다 (늦게 온 구조 요청에 들렀다).</summary>
    public void Rescued(IEnumerable<CrewMember> people, string where)
    {
        var m = Now;
        if (m == null) return;
        foreach (var c in people) { m.People.Add(c.Id); m.Aboard++; }
        if (m.Kind == MissionKind.Rescue) m.Progress = m.Aboard;
        Note(m, $"{where}에서 {m.People.Count}명을 태웠다");
    }
}
