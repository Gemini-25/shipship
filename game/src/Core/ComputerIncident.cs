using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v23 주컴퓨터 대개편 1단계: 사고 지휘관.
//   주 컴퓨터가 아는 사고(감지기 · 근처 로봇의 눈 · 사람의 무전)마다 지휘 건을 열고, 한 흐름이 처음부터 끝까지 책임진다:
//   탐지 → 동원(가장 빨리 닿는 · 맞는 손부터 · 하던 평소 일은 바로 내려놓는다) → 진행(빈 손은 다시 붙이고 · 막히면 증원) → 확인 → 닫음.
//   우선: 사람 목숨 > 번짐 > 배 구조. 주 컴퓨터는 전권 자동 (묻지 않는다).
//   지휘가 넘어가는 순서: 주 컴퓨터 → (멎으면) 보조 두뇌 = 예비 코어 (감지기는 읽지만 느리다) → (그것도 멎으면) 로봇 대장 = 경험 많은 로봇
//   (감지기 없이 로봇의 눈 · 사람 무전만 · 잠긴 격벽은 못 연다 · 더 느리다) → (움직이는 로봇도 없으면) 사람. 주 컴퓨터가 돌아오면 지휘를 돌려받는다.

/// <summary>누가 지휘하나: 주 컴퓨터 → 보조 두뇌(예비 코어) → 로봇 대장 → 사람.</summary>
public enum CommandTier : byte { Main, Backup, RobotLeader, Human }

public enum CaseKind : byte { Fire, Breach, Casualty }
public enum CasePhase : byte { Working, Verify, Closed }

public sealed class CaseNote
{
    public long Tick { get; init; }
    public string Text { get; init; } = "";
}

/// <summary>지휘 건 하나 (사고 하나).</summary>
public sealed class CommandCase
{
    public int Id { get; init; }
    public CaseKind Kind { get; init; }
    public int RoomId { get; set; } = -1;
    public int CrewId { get; init; } = -1;
    public long Opened { get; init; }
    /// <summary>처음 손이 닿은 때 (-1 아직).</summary>
    public long OnScene { get; set; } = -1;
    public long Resolved { get; set; } = -1;
    public long ClosedAt { get; set; } = -1;
    public CasePhase Phase { get; set; }
    /// <summary>어떻게 알았나 (감지기 · 로봇의 눈 · 무전 …).</summary>
    public string Source { get; set; } = "";
    public float Size { get; set; }
    public float Peak { get; set; }
    public float Best { get; set; } = float.MaxValue;
    public long BetterAt { get; set; }
    public int Sent { get; set; }
    public int Reinforced { get; set; }
    /// <summary>사람 목숨이 걸렸나.</summary>
    public bool Life { get; set; }
    public string Outcome { get; set; } = "";
    /// <summary>v23 천장 소화 설비 — 마지막으로 적은 상태 (바뀔 때만 적는다).</summary>
    public string NozzleSaid { get; set; } = "";
    public List<int> Bots { get; } = new();
    public List<CaseNote> Notes { get; } = new();
    public bool Open => Phase != CasePhase.Closed;
}

public sealed class CommanderStats
{
    public int Opened, Closed, Sent, Reinforced, Refires, NoPath, Freed, Handover, ToBackup, ToLeader, Leaders;
    public string Line() => $"지휘 건 {Opened} (닫음 {Closed}) · 출동 {Sent} · 증원 {Reinforced} · 다시 붙음 {Refires} · 길 막힘 {NoPath} · 사람 몫을 돌림 {Freed} · 보조 두뇌로 {ToBackup} · 로봇 대장으로 {ToLeader} (대장 {Leaders}) · 사람에게 넘김 {Handover}";
}

public sealed class IncidentCommander
{
    /// <summary>시험: 끄면 예전처럼 (긴급 지정 · 작업 목록만).</summary>
    public static bool On = true;
    private readonly World _w;
    public List<CommandCase> Cases { get; } = new();
    public CommanderStats Stats { get; } = new();
    private int _nextId = 1;
    private long _next;

    public IncidentCommander(World w) => _w = w;

    /// <summary>지금 누가 지휘하나.</summary>
    public CommandTier Tier { get; private set; } = CommandTier.Main;
    /// <summary>로봇 대장 (-1 없음).</summary>
    public int LeaderId { get; private set; } = -1;

    /// <summary>지휘관이 도나 (사람이 지휘하면 꺼진다).</summary>
    public bool Active => On && !FleetSystem.Off && RobotsV21.General && Tier != CommandTier.Human;
    /// <summary>잠긴 격벽을 열어 줄 수 있나 (주 컴퓨터 · 보조 두뇌만 — 로봇 대장은 문 제어가 없다).</summary>
    public bool OpensDoors => Active && Tier <= CommandTier.Backup;
    /// <summary>판단 주기 (틱): 주 12초 · 보조 24초 · 로봇 대장 48초.</summary>
    private int Period => Tier switch { CommandTier.Main => 5, CommandTier.Backup => 10, _ => 20 };
    public static string TierName(CommandTier t) => t switch { CommandTier.Main => "주 컴퓨터", CommandTier.Backup => "보조 두뇌", CommandTier.RobotLeader => "로봇 대장", _ => "사람" };
    /// <summary>지휘하는 목소리 (기록 · 무전 앞머리).</summary>
    public string Voice => Tier switch
    {
        CommandTier.Main => _w.Automation.Voice.Call,
        CommandTier.Backup => $"{_w.Automation.Voice.Call}(보조 두뇌)",
        CommandTier.RobotLeader => BotOf(LeaderId) is Robot l ? $"{l.Name}(임시 대장)" : "로봇 대장",
        _ => "사람",
    };

    public IEnumerable<CommandCase> OpenCases => Cases.Where(c => c.Open);
    public CommandCase? Get(int id) => Cases.FirstOrDefault(c => c.Id == id);
    private CommandCase? Find(CaseKind kind, int roomId, int crewId = -1) =>
        Cases.FirstOrDefault(c => c.Open && c.Kind == kind && (kind == CaseKind.Casualty ? c.CrewId == crewId : c.RoomId == roomId));

    private Room? RoomOf(CommandCase k) => k.RoomId >= 0 && k.RoomId < _w.Ship.Rooms.Count ? _w.Ship.Rooms[k.RoomId] : null;
    private CrewMember? CrewOf(CommandCase k) => k.CrewId >= 0 ? _w.Crew.FirstOrDefault(c => c.Id == k.CrewId) : null;
    private Robot? BotOf(int id) => id >= 0 && id < _w.Robots.Robots.Count && _w.Robots.Robots[id].Id == id ? _w.Robots.Robots[id] : _w.Robots.Robots.FirstOrDefault(r => r.Id == id);

    public static string KindName(CaseKind k) => k switch { CaseKind.Fire => "화재", CaseKind.Breach => "파공", _ => "부상자" };

    /// <summary>다섯 틱마다 (12초): 알아채기 · 지휘 건마다 동원 · 확인 · 닫기.</summary>
    public void Tick()
    {
        var w = _w;
        if (w.Tick < _next) return;
        Chain();
        _next = w.Tick + Period;
        if (!Active)
        {
            // 움직이는 로봇도 없다 — 열린 지휘 건을 사람에게 넘긴다
            foreach (var k in Cases.Where(c => c.Open).ToList())
            {
                Stats.Handover++;
                Close(k, "지휘할 손이 없다 — 사람이 지휘를 넘겨받는다");
            }
            return;
        }
        Sense();
        foreach (var k in Cases.Where(c => c.Open).OrderByDescending(Priority).ThenBy(c => c.Id).ToList()) Run(k);
        if (w.Tick >= _postNext) { _postNext = w.Tick + SimTime.Minutes(10); Posts(); }
        int closed = Cases.Count(c => !c.Open);
        for (int i = 0; i < Cases.Count && closed > 40; i++)
            if (!Cases[i].Open) { Cases.RemoveAt(i); i--; closed--; }
    }

    /// <summary>지휘가 넘어가는 순서를 따진다: 주 컴퓨터 → 보조 두뇌 → 로봇 대장 → 사람 (넘어갈 때마다 기록 · 연대기).</summary>
    private void Chain()
    {
        var w = _w;
        var a = w.Automation;
        var was = Tier;
        CommandTier now;
        if (a.Present && a.MainOnline) now = CommandTier.Main;
        else if (a.Present && a.CoreOnline) now = CommandTier.Backup;
        else
        {
            var lead = BotOf(LeaderId);
            if (lead == null || !lead.Operational || lead.Wrecked || lead.Runaway)
            {
                // 경험이 가장 많은 로봇이 임시 대장 (한 일 · 숙련 · 개량 단계 · 지켜 준 사람)
                lead = w.Robots.Robots.Where(r => r.Operational && !r.Wrecked && !r.Runaway && r.Aboard == null)
                    .OrderByDescending(r => r.JobsDone + 60f * r.Skill + 25f * r.Mk + 10f * r.Shielded).ThenBy(r => r.Id).FirstOrDefault();
                if (lead != null && lead.Id != LeaderId)
                {
                    LeaderId = lead.Id;
                    Stats.Leaders++;
                    lead.Mind.Say("주 컴퓨터도 보조 두뇌도 멎었다 — 내가 임시 대장이다. 무전으로 부른다", w.Tick);
                    MarkLog.Add(lead.Marks, w.Tick, "임시 대장이 되었다 (주 컴퓨터 · 보조 두뇌가 멎었다)");
                }
            }
            if (lead == null) LeaderId = -1;
            now = lead != null ? CommandTier.RobotLeader : CommandTier.Human;
        }
        if (now == was) return;
        Tier = now;
        if (now == CommandTier.Backup) Stats.ToBackup++;
        if (now == CommandTier.RobotLeader) Stats.ToLeader++;
        if (now != CommandTier.RobotLeader) LeaderId = -1;
        int open = Cases.Count(c => c.Open);
        string text = now switch
        {
            CommandTier.Main => $"주 컴퓨터가 돌아왔다 — 지휘를 돌려받는다 (그동안 {TierName(was)}가 지휘했다 · 열린 지휘 건 {open}개)",
            CommandTier.Backup => $"주 컴퓨터가 멎었다 — 보조 두뇌가 지휘를 잇는다 (느리다 · 열린 지휘 건 {open}개)",
            CommandTier.RobotLeader => $"보조 두뇌도 멎었다 — {Voice}가 무전으로 로봇들을 지휘한다 (감지기 없이 · 잠긴 격벽은 못 연다)",
            _ => "지휘할 컴퓨터도 로봇도 없다 — 사람이 지휘한다",
        };
        w.Log.Add(w.Tick, LogKind.Ship, text);
        w.History.Add(w, HistoryKind.Adaptation, text, null);
        foreach (var k in Cases.Where(c => c.Open)) k.Notes.Add(new CaseNote { Tick = w.Tick, Text = text });
    }

    private long _postNext;
    public int Posted => _w.Robots.Robots.Count(r => r.Post != null);

    /// <summary>불이 잘 나는 방 (설비 종류 · 고장이 잦았던 방).</summary>
    private float Risk(Room room)
    {
        float s = room.Type switch
        {
            RoomType.Galley or RoomType.Engine or RoomType.Reactor or RoomType.Power or RoomType.BatteryRoom or RoomType.FuelCell => 3f,
            RoomType.Storage or RoomType.Workshop or RoomType.Cooling or RoomType.LifeSupport => 2f,
            _ => 0f,
        };
        s += room.Furniture.Count(f => f.Machine != null) * 0.25f + _w.Fleet.Hot(room) / (float)SimTime.Hours(2);
        return s;
    }

    /// <summary>
    /// 거점 배치 (평소 10분마다 · 주 컴퓨터 · 보조 두뇌만): 불이 잘 나는 방 곁(10칸 안)에 로봇이 없으면 한가한 로봇 하나를 문 곁에 세운다.
    /// 사고가 열려 있으면 그대로 둔다 · 위험이 줄었으면 거둔다.
    /// </summary>
    private void Posts()
    {
        var w = _w;
        if (Tier > CommandTier.Backup || Cases.Any(c => c.Open)) return;
        var bots = w.Robots.Robots;
        int cap = Math.Max(1, bots.Count(r => r.Operational) / 8);
        var risky = w.Ship.LiveRooms.Where(r => !r.Abandoned && !r.OffLimits && r.Type != RoomType.Corridor).Select(r => (room: r, risk: Risk(r))).Where(x => x.risk >= 2f)
            .OrderByDescending(x => x.risk).ThenBy(x => x.room.Id).Take(cap).Select(x => x.room).ToList();
        // 위험 목록에서 빠진 방의 거점은 거둔다
        foreach (var r in bots) if (r.Post != null && !risky.Any(x => x.Id == r.PostRoom)) w.Robots.SetPost(r, null, null);
        foreach (var room in risky)
        {
            if (bots.Any(r => r.PostRoom == room.Id && r.Operational)) continue;
            var door = room.Doors.Where(d => !d.Removed).OrderBy(d => d.Cell.X).ThenBy(d => d.Cell.Y).FirstOrDefault();
            var spot = door == null ? (Cell?)null : Cell.Dirs4.Select(d => door.Cell + d).Where(c => w.Ship.IsOpenFloor(c) && w.Ship.RoomAt(c) == room && w.Ship.FurnitureAt(c) == null)
                .OrderBy(c => c.X).ThenBy(c => c.Y).Cast<Cell?>().FirstOrDefault();
            if (spot is not Cell at) continue;
            var field = w.Paths.Flood(at, RobotSystem.Profile);
            if (bots.Any(r => r.Operational && !r.Wrecked && Reach(field, r) is int d && d >= 0 && d <= 100)) continue; // 곁(10칸 안)에 이미 있다
            Robot? pick = null; int bd = int.MaxValue;
            foreach (var r in bots)
            {
                if (!RobotSystem.Callable(w, r) || r.CaseId >= 0 || r.Post != null || r.Steps != null && r.Order != null || RobotsV21.Medical(r.Kind) || RobotsV21.OnCall(r.Kind) || r.Battery < 0.7f || !w.Relays.Reach(r.Room)) continue;
                int d = Reach(field, r);
                if (d < 0) continue;
                d = (int)(d * (RobotsV15.Fights(r.Kind) ? 0.6f : 1f));
                if (d < bd) { bd = d; pick = r; }
            }
            if (pick == null) continue;
            w.Robots.SetPost(pick, room, at);
            w.Log.Add(w.Tick, LogKind.Ship, $"{Voice}: 거점 — {pick.Name}을(를) {room.Name} 곁에 세운다 (불이 잘 나는 방 · 가까이 로봇이 없다)");
        }
    }

    private float Priority(CommandCase k) => k.Kind switch
    {
        CaseKind.Casualty => k.Life ? 100f : 50f,
        CaseKind.Fire => (k.Life ? 90f : 70f) + MathF.Min(10f, k.Size),
        _ => k.Life ? 80f : 60f,
    };

    // ─────────────────────────────── 알아채기 ───────────────────────────────

    /// <summary>주 컴퓨터가 이 방의 불을 아나 — 감지기 · 소화 수순 · 근처 로봇의 눈 · 깨어 있는 사람의 무전.</summary>
    private string? KnowsFire(Room room)
    {
        var w = _w;
        var a = w.Automation;
        var b = a.Belief;
        if (Tier <= CommandTier.Backup)
        {
            if (b.Reading(room) && b.Of(room).Fire) return "감지기";
            if (a.FireCases.Any(fc => fc.RoomId == room.Id)) return "소화 수순";
        }
        // v23 로봇의 눈 · 사람의 무전은 구역 중계기를 거쳐야 닿는다 (끊긴 구역에서 본 것은 주 컴퓨터가 모른다)
        bool unheard = false;
        foreach (var r in w.Robots.Robots)
            if (!r.Wrecked && r.State != RobotState.Lost && r.Aboard == null && (r.Room == room || w.Fire.AnyWithin(r.Cell, 4f) && w.Ship.RoomAt(r.Cell) is Room rr && rr.Doors.Any(d => d.RoomA == room || d.RoomB == room)))
            {
                if (w.Relays.Reach(r.Room)) return $"{r.Name}의 눈";
                unheard = true;
            }
        foreach (var c in w.Crew)
            if (!c.Dead && c.IsAwake && !c.Outside && c.Room == room)
            {
                if (w.Relays.Reach(c.Room)) return $"{c.Name}의 무전";
                unheard = true;
            }
        if (unheard && _unheardRoom != room.Id) { _unheardRoom = room.Id; w.Relays.Unheard(); }
        return null;
    }

    private int _unheardRoom = -1;

    private void Sense()
    {
        var w = _w;
        if (w.Fire.Count > 0)
            foreach (var room in w.Ship.LiveRooms)
            {
                if (room.Abandoned || w.Fire.CountIn(room) == 0 || Find(CaseKind.Fire, room.Id) != null) continue;
                if (KnowsFire(room) is string src) OpenCase(CaseKind.Fire, room, -1, src, $"{room.Name} 불 {w.Fire.CountIn(room)}칸");
            }
        foreach (var o in w.Board.OpenUnsorted)
        {
            if (o.Kind != WorkKind.SealBreach || o.Target.CurrentRoom is not Room room || room.Detached || Find(CaseKind.Breach, room.Id) != null) continue;
            string? bsrc = Tier <= CommandTier.Backup ? (w.Automation.Belief.Reading(room) ? "기압 감지기" : "봉합 일")
                : !w.Relays.Reach(room) ? null
                : w.Robots.Robots.FirstOrDefault(r => !r.Wrecked && r.State != RobotState.Lost && r.Room == room) is Robot eye0 ? $"{eye0.Name}의 눈"
                : w.Crew.FirstOrDefault(c => !c.Dead && c.IsAwake && c.Room == room) is CrewMember by0 ? $"{by0.Name}의 무전" : null;
            if (bsrc != null) OpenCase(CaseKind.Breach, room, -1, bsrc, $"{room.Name} 파공 — 공기가 샌다");
        }
        foreach (var t in w.Casualty.Open)
        {
            if (t.Closed || w.Crew.FirstOrDefault(c => c.Id == t.CrewId) is not CrewMember pt || pt.Dead || pt.Outside || pt.Away || pt.Room == null) continue;
            if (Find(CaseKind.Casualty, -1, pt.Id) != null) continue;
            string? src = Tier > CommandTier.Backup ? null : t.Paged ? "호출" : w.Automation.Has(ComputerModule.BioMonitor) ? "생체 감시" : null;
            if (w.Relays.Reach(pt.Room))
            {
                src ??= w.Robots.Robots.FirstOrDefault(r => !r.Wrecked && r.State != RobotState.Lost && r.Room == pt.Room) is Robot eye ? $"{eye.Name}의 눈" : null;
                src ??= w.Crew.FirstOrDefault(c => c != pt && !c.Dead && c.IsAwake && c.Room == pt.Room) is CrewMember by ? $"{by.Name}의 무전" : null;
            }
            if (src != null) OpenCase(CaseKind.Casualty, pt.Room, pt.Id, src, $"{pt.Name} 다쳤다 ({t.Cause})");
        }
    }

    private void OpenCase(CaseKind kind, Room room, int crewId, string src, string what)
    {
        var w = _w;
        var k = new CommandCase { Id = _nextId++, Kind = kind, RoomId = room.Id, CrewId = crewId, Opened = w.Tick, Source = src, BetterAt = w.Tick };
        Cases.Add(k);
        Stats.Opened++;
        Note(k, $"{what} — {Ko.EuRo(src)} 알았다 · 지휘 건을 연다");
    }

    private void Note(CommandCase k, string text)
    {
        var w = _w;
        k.Notes.Add(new CaseNote { Tick = w.Tick, Text = text });
        if (k.Notes.Count > 30) k.Notes.RemoveAt(0);
        w.Log.Add(w.Tick, LogKind.Ship, $"{Voice}: 지휘 건 #{k.Id} {KindName(k.Kind)} — {text}");
    }

    // ─────────────────────────────── 지휘 건마다 ───────────────────────────────

    private void Run(CommandCase k)
    {
        var w = _w;
        var rs = w.Robots;
        // 빠진 손 (고장 · 부서짐 · 다른 건으로 옮김)
        for (int i = k.Bots.Count - 1; i >= 0; i--)
        {
            var r = BotOf(k.Bots[i]);
            if (r != null && r.CaseId == k.Id && !r.Wrecked && r.Operational) continue;
            if (r != null && r.CaseId == k.Id) rs.Release(r, $"지휘 건 #{k.Id}에서 빠졌다 — {(r.Wrecked ? "부서졌다" : "움직일 수 없다")}");
            if (r != null) Note(k, $"{r.Name} 빠짐 — {(r.Wrecked ? "부서졌다" : r.Fault != null ? "고장" : "움직일 수 없다")}");
            k.Bots.RemoveAt(i);
        }
        switch (k.Kind)
        {
            case CaseKind.Fire: Fire(k); break;
            case CaseKind.Casualty: Casualty(k); break;
            default: Breach(k); break;
        }
    }

    private void Fire(CommandCase k)
    {
        var w = _w;
        var rs = w.Robots;
        var room = RoomOf(k);
        if (room == null || room.Detached) { Close(k, "방이 떨어져 나갔다"); return; }
        int size = w.Fire.CountIn(room);
        k.Size = size;
        k.Peak = MathF.Max(k.Peak, size);
        if (size == 0)
        {
            if (k.Phase == CasePhase.Working)
            {
                k.Phase = CasePhase.Verify;
                k.Resolved = w.Tick;
                Note(k, $"불이 꺼졌다 — 2분 지켜본다 (가장 컸을 때 {k.Peak:0}칸)");
                foreach (var id in k.Bots.Skip(1).ToList()) if (BotOf(id) is Robot r) { rs.Release(r, $"{room.Name} 불이 꺼졌다 — 돌아간다"); k.Bots.Remove(id); }
            }
            else if (w.Tick - k.Resolved >= SimTime.Minutes(2)) Close(k, "불을 껐다");
            return;
        }
        if (k.Phase == CasePhase.Verify) { k.Phase = CasePhase.Working; Stats.Refires++; Note(k, "다시 붙었다 — 다시 동원한다"); }
        k.Life = w.Crew.Any(c => !c.Dead && c.Room == room);
        // 천장 소화 설비: 지휘관이 알면 바로 연다 (감지기가 놓친 불도 로봇의 눈 · 무전으로 알았으면) — 망가졌거나 바닥나면 로봇이 들어간다
        var nz = w.Nozzles;
        if (nz.Has(room))
        {
            if (!room.NozzleOn && OpensDoors && w.Automation.DampersIn(room) && nz.Ready(room)) nz.Open(room, false);
            string st = !room.NozzleOn && nz.Ready(room) ? "" : nz.State(room);
            if (st != k.NozzleSaid && st != "")
            {
                k.NozzleSaid = st;
                Note(k, st switch
                {
                    "뿌리는 중" => "천장 소화 설비가 뿌린다 — 로봇은 남은 불을",
                    "고장" => "천장 소화 설비 고장 — 로봇이 끈다",
                    "바닥남" => "천장 소화 약제가 바닥났다 — 로봇이 끈다",
                    _ => $"천장 소화 설비 {st}",
                });
            }
        }
        int want = Math.Clamp(2 + size / 3, 2, 6) + (k.Life ? 1 : 0);
        if (nz.Spraying(room)) want = Math.Max(1, want - 1); // 천장이 누르는 동안은 한 대 덜
        if (size < k.Best) { k.Best = size; k.BetterAt = w.Tick; }
        else if (k.OnScene >= 0 && w.Tick - Math.Max(k.BetterAt, k.OnScene) > SimTime.Minutes(3) && k.Bots.Count >= want + k.Reinforced && k.Reinforced < 3) // 손이 닿은 뒤에야 진척을 따진다
        {
            k.Reinforced++;
            k.BetterAt = w.Tick;
            Stats.Reinforced++;
            Note(k, $"3분째 줄지 않는다 ({size}칸) — 증원");
        }
        want = Math.Min(8, want + k.Reinforced);
        // 붙은 로봇: 빈손이면 다시 그 방 불로 · 거품이 떨어지면 채우러
        foreach (var id in k.Bots.ToList())
        {
            if (BotOf(id) is not Robot r) continue;
            if (r.Foam <= 0.15f) { rs.Release(r, "거품이 떨어졌다 — 채우러 간다"); k.Bots.Remove(id); Note(k, $"{r.Name} 거품이 떨어져 물러남"); continue; }
            if (RobotSystem.Idle(r)) rs.FightIn(r, room);
            if (k.OnScene < 0 && r.FightingFire && r.Room == room) { k.OnScene = w.Tick; Note(k, $"{r.Name} 현장 도착 — {(w.Tick - k.Opened) / (float)SimTime.Minutes(1):0}분"); }
        }
        if (k.Bots.Count < want) Dispatch(k, room, room.Cells.Where(c => w.Fire.At(c) > 0f).OrderBy(c => c.X).ThenBy(c => c.Y).FirstOrDefault(), want - k.Bots.Count);
    }

    private void Casualty(CommandCase k)
    {
        var w = _w;
        var pt = CrewOf(k);
        if (pt == null || pt.Dead) { Close(k, pt?.Dead == true ? "숨졌다" : "사라졌다"); return; }
        var t = w.Casualty.Of(pt);
        if (t == null) { Close(k, "고비를 넘겼다"); return; }
        k.RoomId = pt.Room?.Id ?? k.RoomId;
        k.Life = w.Casualty.Urgent(pt);
        int want = k.Life ? 2 : 1;
        foreach (var id in k.Bots.ToList())
        {
            if (BotOf(id) is not Robot r) continue;
            if (r.Draft != DraftKind.Rescue || r.DraftUntil < w.Tick + SimTime.Minutes(5)) { r.Draft = DraftKind.Rescue; r.DraftUntil = w.Tick + SimTime.Minutes(30); } // 지휘 건이 열린 동안은 구조 지정을 놓지 않는다
            if (RobotSystem.Idle(r) || r.Steps != null && r.StepIndex < r.Steps.Count && r.Steps[r.StepIndex] is RWait) w.Robots.GoTo(r, pt, $"{Ko.EulReul(pt.Name)} 지키러 — 지휘 건 #{k.Id}");
            if (k.OnScene < 0 && Vector2.Distance(r.Position, pt.Position) < 1.6f) { k.OnScene = w.Tick; Note(k, $"{r.Name} {pt.Name} 곁에 닿았다 — {(w.Tick - k.Opened) / (float)SimTime.Minutes(1):0}분"); }
        }
        if (k.Bots.Count < want) Dispatch(k, pt.Room, pt.Cell, want - k.Bots.Count);
        // 치료 일: 의사 로봇이 있으면 직접 붙는다 (사람 몫이어도 함께 — 사람 의사가 자고 있는 밤)
        foreach (var o in w.Board.OpenUnsorted.Where(o => o.Kind == WorkKind.Treat && o.Target.Crew == pt && o.Robot == null).ToList())
        {
            if (o.Assignee is CrewMember doc && doc.Job?.Order == o && doc.Room == pt.Room) continue; // 사람 의사가 이미 곁에서 치료 중
            var medic = w.Robots.Robots.Where(r => RobotSystem.Callable(w, r) && RobotSystem.Special(r.Kind, WorkKind.Treat) && (r.CaseId < 0 || r.CaseId == k.Id) && r.Order == null)
                .OrderBy(r => (r.Position - pt.Position).LengthSquared()).ThenBy(r => r.Id).FirstOrDefault();
            if (medic == null) break;
            if (medic.CaseId < 0) { w.Robots.Enlist(medic, k, DraftKind.Rescue, pt.Room, $"{pt.Name} 치료"); k.Bots.Add(medic.Id); k.Sent++; Stats.Sent++; }
            if (w.Robots.WorkOn(medic, o)) Note(k, $"{medic.Name} — {o.Title}");
        }
    }

    private void Breach(CommandCase k)
    {
        var w = _w;
        var room = RoomOf(k);
        if (room == null || room.Detached) { Close(k, "방이 떨어져 나갔다"); return; }
        var orders = w.Board.OpenUnsorted.Where(o => o.Kind == WorkKind.SealBreach && o.Target.CurrentRoom == room).ToList();
        if (orders.Count == 0 && !room.Leaking)
        {
            if (k.Phase == CasePhase.Working) { k.Phase = CasePhase.Verify; k.Resolved = w.Tick; Note(k, "봉합됐다 — 2분 지켜본다"); }
            else if (w.Tick - k.Resolved >= SimTime.Minutes(2)) Close(k, "파공을 막았다");
            return;
        }
        if (k.Phase == CasePhase.Verify) { k.Phase = CasePhase.Working; Note(k, "다시 샌다"); }
        k.Life = w.Crew.Any(c => !c.Dead && c.Room == room && c.Suit == null);
        // 사람 몫으로 묶였는데 손대지 않는 봉합 일은 로봇에게 돌린다 (자는 사람 · 다른 일을 하는 사람)
        foreach (var o in orders)
            if (o.Assignee is CrewMember c && c.Job?.Order != o && w.Tick - o.Posted > SimTime.Minutes(1))
            {
                o.Assignee = null;
                Stats.Freed++;
                Note(k, $"{c.Name} 몫이던 {o.Title} — 손대지 않아 로봇에게 돌린다");
            }
        foreach (var id in k.Bots)
        {
            if (BotOf(id) is not Robot r) continue;
            bool idle = RobotSystem.Idle(r);
            if (idle)
            {
                // 봉합 일에 직접 붙는다 — 로봇이 아직 없는 일부터 (사람 몫이어도 함께)
                bool on = false;
                foreach (var o in orders.Where(x => x.Robot == null).OrderBy(x => x.Assignee == null ? 0 : 1).ThenBy(x => x.Id))
                    if (w.Robots.WorkOn(r, o)) { on = true; break; }
                if (!on) w.Robots.Steer(r);
            }
            if (k.OnScene < 0 && r.Order is { Kind: WorkKind.SealBreach } && r.Room == room) { k.OnScene = w.Tick; Note(k, $"{r.Name} 봉합 시작 — {(w.Tick - k.Opened) / (float)SimTime.Minutes(1):0}분"); }
        }
        int want = Math.Min(3, 1 + orders.Count) + (k.Life ? 1 : 0);
        if (k.Bots.Count < want) Dispatch(k, room, orders.FirstOrDefault()?.Target.Cell ?? Cell.FromPosition(room.Center), want - k.Bots.Count);
    }

    // ─────────────────────────────── 동원 ───────────────────────────────

    /// <summary>
    /// 가장 빨리 닿는 · 맞는 로봇부터 n대: 그 자리에서 거꾸로 길을 재 (잠긴 격벽은 연다) · 특기 · 덩치 · 방열을 본다.
    /// 의료 로봇은 다친 사람 몫으로 남겨 둔다 · 급한 일을 하던 로봇은 건드리지 않는다 · 하던 평소 일은 바로 내려놓는다.
    /// </summary>
    private void Dispatch(CommandCase k, Room? room, Cell at, int n)
    {
        var w = _w;
        var rs = w.Robots;
        var field = w.Paths.Flood(at, OpensDoors ? RobotSystem.UrgentProfile : RobotSystem.Profile);
        bool hurtOpen = Cases.Any(c => c.Open && c.Kind == CaseKind.Casualty);
        var picks = new List<(Robot r, float cost)>();
        var near = new List<(Robot r, int d)>();
        foreach (var r in rs.Robots)
        {
            if (!RobotSystem.Callable(w, r) || r.CaseId >= 0) continue;
            if (!w.Relays.Reach(r.Room)) continue; // v23 끊긴 구역의 로봇은 부를 수 없다 (제 판단으로 움직인다)
            if (r.Order is WorkOrder o && o.Urgency >= 0.9f) continue; // 다른 급한 일을 하는 중
            bool med = RobotsV21.Medical(r.Kind);
            if (med && k.Kind != CaseKind.Casualty && hurtOpen) continue;
            int d = Reach(field, r);
            if (d < 0) continue;
            // 맞는 손은 조금 멀어도 (거리 배율) — 초동은 가장 가까운 손이 따로 맡는다
            float mul = k.Kind switch
            {
                CaseKind.Fire => (RobotsV15.Fights(r.Kind) ? 0.6f : 1f) * (RobotsV15.Fireproof(r.Kind) ? 0.85f : 1f) * (med ? 3f : 1f),
                CaseKind.Casualty => med || r.Kind is RobotKind.Stretcher or RobotKind.Rescuer ? 0.4f : 1.5f,
                _ => RobotSystem.Special(r.Kind, WorkKind.SealBreach) ? 0.5f : 1f,
            };
            picks.Add((r, d * mul + d * 0.0001f));
            near.Add((r, d));
        }
        var chosen = new List<Robot>();
        // 초동: 아무도 안 붙었으면 가장 가까운 손 하나부터 (불난 방 정비대에 선 로봇이 15분 서 있던 일)
        if (k.Bots.Count == 0 && near.Count > 0 && (k.Kind != CaseKind.Casualty || near.Min(x => x.d) < 400))
            chosen.Add(near.OrderBy(x => x.d).ThenBy(x => x.r.Id).First().r);
        chosen.AddRange(picks.Where(p => !chosen.Contains(p.r)).OrderBy(p => p.cost).ThenBy(p => p.r.Id).Take(n - chosen.Count).Select(p => p.r));
        if (chosen.Count == 0) return;
        var pt = CrewOf(k);
        var sent = new List<string>();
        foreach (var r in chosen)
        {
            string why = k.Kind switch
            {
                CaseKind.Fire => $"{room?.Name} 불 끄러",
                CaseKind.Casualty => $"{pt?.Name} 곁으로",
                _ => $"{room?.Name} 파공 막으러",
            };
            rs.Enlist(r, k, k.Kind switch { CaseKind.Fire => DraftKind.Fire, CaseKind.Casualty => DraftKind.Rescue, _ => DraftKind.Breach }, room, why);
            bool ok = k.Kind switch
            {
                CaseKind.Fire => room != null && rs.FightIn(r, room),
                CaseKind.Casualty => pt != null && rs.GoTo(r, pt, $"{Ko.EulReul(pt.Name)} 지키러 — 지휘 건 #{k.Id}"),
                _ => true,
            };
            if (k.Kind == CaseKind.Breach) rs.Steer(r);
            if (!ok)
            {
                Stats.NoPath++;
                rs.Release(r, "갈 길이 막혔다 — 지휘 건에서 빠진다");
                continue;
            }
            k.Bots.Add(r.Id);
            k.Sent++;
            Stats.Sent++;
            sent.Add(r.Name);
        }
        if (sent.Count > 0) Note(k, $"{(k.Sent > sent.Count ? "더 보낸다" : "출동")}: {string.Join(" · ", sent)}");
    }

    /// <summary>로봇까지의 거리 — 정비대에 앉은 로봇은 정비대 곁 칸으로 잰다 (정비대 칸은 걷는 칸이 아니다).</summary>
    private static int Reach(DistanceField field, Robot r)
    {
        int d = field.Get(r.Cell);
        if (d >= 0) return d;
        int best = -1;
        foreach (var s in r.Dock.UseSpots.Concat(r.Dock.Cells.SelectMany(c => Cell.Dirs4.Select(x => c + x))))
        {
            int ds = field.Get(s);
            if (ds >= 0 && (best < 0 || ds < best)) best = ds;
        }
        return best;
    }

    private void Close(CommandCase k, string outcome)
    {
        var w = _w;
        foreach (var id in k.Bots) if (BotOf(id) is Robot r && r.CaseId == k.Id) w.Robots.Release(r, $"지휘 건 #{k.Id} 닫힘 — {outcome}");
        k.Bots.Clear();
        k.Phase = CasePhase.Closed;
        k.ClosedAt = w.Tick;
        k.Outcome = outcome;
        Stats.Closed++;
        string first = k.OnScene >= 0 ? $"첫 도착 {(k.OnScene - k.Opened) / (float)SimTime.Minutes(1):0}분" : "닿은 손 없음";
        Note(k, $"닫음 — {outcome} · {first} · 출동 {k.Sent}대 · {(w.Tick - k.Opened) / (float)SimTime.Minutes(1):0}분");
        if (k.Kind != CaseKind.Fire || k.Peak >= 3 || k.Life)
            w.History.Add(w, HistoryKind.Response, $"{TierName(Tier)} 지휘 건 #{k.Id} {KindName(k.Kind)} — {outcome} ({first} · 출동 {k.Sent}대)", RoomOf(k), CrewOf(k) is CrewMember c ? new[] { c } : null);
    }

    public void Hash(Action<int> I, Action<float> F)
    {
        I(Cases.Count); I(Stats.Sent); I(Stats.Opened); I((int)Tier); I(LeaderId);
        foreach (var k in Cases) { I(k.Id); I((int)k.Phase); I(k.Bots.Count); F(k.Size); }
    }
}
