using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.6 제안 → 승인: 방침(컴퓨터 제안)에 따라 위험한 조치는 바로 하지 않고 제안 카드를 낸다 — 근거 · 예상 효과 · 기한.
// 플레이어가 받거나 거절하고(화면 버튼), 기한 안에 안 고르면 지휘하는 사람(현장 지휘 · 선장)이 대신 판단한다. 결과는 다섯 칸 기록으로 채점되고 신뢰가 바뀐다.
// 거절하면 사람이 확인하러 간다 (사람 확인 요청): 가서 보고, 쓰러진 사람이 있으면 업어 나온다 · 틀어진 감지기를 다시 맞춘다.

public enum ProposalState { Pending, Accepted, Rejected, Expired }

public sealed class Proposal
{
    public int Id { get; init; }
    public long Tick { get; init; }
    public long Deadline { get; init; }
    public string Key { get; init; } = "";
    public string Kind { get; init; } = "";
    public int RoomId { get; init; } = -1;
    public string Title { get; init; } = "";
    public string Basis { get; init; } = "";
    public string Effect { get; init; } = "";
    public ProposalState State { get; set; }
    public string DecidedBy { get; set; } = "";
    public string DecideWhy { get; set; } = "";
    public long DecidedAt { get; set; } = -1;
    public string Result { get; set; } = "";
    /// <summary>1 컴퓨터가 맞았다 · −1 틀렸다 · 0 아직.</summary>
    public int Score { get; set; }
    /// <summary>컴퓨터가 믿은 그 방 사람 수 (null = 모른다).</summary>
    public int? Believed { get; init; }
    /// <summary>실제로 그때 안에 있던 사람 (컴퓨터는 모른다 — 채점·신뢰용).</summary>
    public List<int> Inside { get; init; } = new();
    public List<int> Found { get; } = new();
    /// <summary>받으면 할 일 (일반 제안 — 배급 · 대피 · 모듈 …; 소화 제안은 대응 수순이 직접 본다). v16.16 계획자가 여기에 할 일을 싣는다.</summary>
    public Action<World, Proposal>? OnAccept { get; init; }
    /// <summary>일반 제안 채점 (null이면 "안에 사람이 있었나"로 채점).</summary>
    public Func<World, Proposal, (int, string)?>? Grader { get; init; }
    public bool Accepted => State == ProposalState.Accepted || State == ProposalState.Expired && DecideWhy.StartsWith("받");
}

public sealed class ProposalBoard
{
    private readonly World _w;
    private int _next = 1;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6421 + 97));
    public List<Proposal> All { get; } = new();
    public int Accepted, Rejected, ByCaptain, ByPlayer;

    public ProposalBoard(World w) => _w = w;

    public IEnumerable<Proposal> Open => All.Where(p => p.State == ProposalState.Pending);
    public Proposal? Pending(string key) => All.FirstOrDefault(p => p.Key == key && p.State == ProposalState.Pending);
    public Proposal? Latest(string key) => All.LastOrDefault(p => p.Key == key);

    /// <summary>이 조치는 먼저 물어야 하나 (방침 "컴퓨터 제안": 바로 실행 · 위험한 조치는 묻는다 · 모두 묻는다). 판단 근거를 쓰려면 IV 추론부터.</summary>
    public bool Needed(string kind)
    {
        int mode = _w.Policies["computerask"];
        if (mode <= 0 || _w.Automation.Level < 4) return false;
        bool risky = kind is "vacuum" or "inert";
        return risky || mode >= 2;
    }

    public Proposal Propose(string key, string kind, Room? room, string title, string basis, string effect, float minutes, int? believed,
        Action<World, Proposal>? onAccept = null, Func<World, Proposal, (int, string)?>? grader = null)
    {
        var w = _w;
        var p = new Proposal
        {
            Id = _next++, Tick = w.Tick, Deadline = w.Tick + SimTime.Minutes(minutes), Key = key, Kind = kind, RoomId = room?.Id ?? -1,
            Title = title, Basis = basis, Effect = effect, Believed = believed, OnAccept = onAccept, Grader = grader,
            Inside = room == null ? new() : w.Crew.Where(c => !c.Dead && c.Room == room && !c.Outside).Select(c => c.Id).ToList(),
        };
        All.Add(p);
        if (All.Count > 80) All.RemoveAt(0);
        w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터 제안 — {title} · 근거: {basis} · 예상: {effect} · {minutes:0.#}분 안에 받거나 거절 (아니면 지휘하는 사람이 정한다)");
        w.Automation.Book.Add(ActKind.Proposal, room, basis, effect, $"제안: {title}", "승인 · 거절", "p:" + p.Id, 0, minutes + 6f, (world, a) => Grade(p));
        w.Automation.Speak.Announce(w.Automation.Voice.Style($"제안 — {title}. 승인을 기다린다"), room, 1);
        return p;
    }

    /// <summary>받거나 거절한다 (플레이어 화면 버튼 · 선장 대신 판단 · 시험).</summary>
    public void Decide(Proposal p, bool accept, string who, string why = "")
    {
        var w = _w;
        if (p.State != ProposalState.Pending) return;
        p.State = who == "플레이어" || who == "관찰자" ? (accept ? ProposalState.Accepted : ProposalState.Rejected) : ProposalState.Expired;
        p.DecidedBy = who;
        p.DecideWhy = (accept ? "받음" : "거절") + (why != "" ? $" — {why}" : "");
        p.DecidedAt = w.Tick;
        if (accept) Accepted++; else Rejected++;
        if (who == "플레이어" || who == "관찰자") ByPlayer++; else ByCaptain++;
        w.Log.Add(w.Tick, LogKind.Ship, $"{who}: 컴퓨터 제안 \"{p.Title}\" {(accept ? "받음" : "거절")}" + (why != "" ? $" — {why}" : ""));
        if (accept) p.OnAccept?.Invoke(w, p);
        if (!accept && p.RoomId >= 0 && p.OnAccept == null)
        {
            var room = w.Ship.Rooms[p.RoomId];
            w.Automation.RequestCheck(room, $"제안 거절 — {p.Title} 전에 안을 직접 본다", p.Id);
        }
        if (p.State == ProposalState.Expired && !accept && w.Crew.FirstOrDefault(c => c.Name == who) is CrewMember boss)
            w.Automation.Trusts.Change(boss, -0.03f, $"컴퓨터 제안을 믿지 않고 직접 보게 했다 ({p.Title})", quiet: true);
    }

    /// <summary>기한이 지나면 지휘하는 사람이 정한다: 안에 사람이 있는 걸 알거나 컴퓨터를 못 믿으면 거절, 아니면 받는다. 사람이 없으면 컴퓨터가 실행.</summary>
    public void Update()
    {
        var w = _w;
        foreach (var p in All)
        {
            if (p.State != ProposalState.Pending || w.Tick < p.Deadline) continue;
            var cmd = w.Command;
            var boss = cmd.Active && cmd.Commander is CrewMember c0 && c0.CanAct ? c0 : cmd.Captain is CrewMember cap && cap.CanAct ? cap : null;
            if (boss == null) { Decide(p, true, "주 컴퓨터", "기한이 지났고 판단할 사람이 없다 — 컴퓨터가 실행"); continue; }
            Room? room = p.RoomId >= 0 ? w.Ship.Rooms[p.RoomId] : null;
            bool knows = room != null && w.Crew.Any(x => !x.Dead && x.Room == room && (boss.Room == room || boss.Mind.Knows.ContainsKey($"down:{x.Id}")));
            float trust = w.Automation.Trusts.Of(boss);
            float yes = trust + (boss.Value == CrewValue.Efficiency ? 0.1f : boss.Value == CrewValue.People ? -0.12f : boss.Value == CrewValue.Safety ? -0.05f : 0f);
            bool accept = !knows && (yes >= 0.5f || yes >= 0.4f && R.Chance(0.5f));
            string why = knows ? "안에 사람이 있는 걸 안다" : accept ? $"컴퓨터를 믿는다 ({trust * 100:0}%)" : $"컴퓨터 말만으론 못 믿겠다 ({trust * 100:0}%) — 직접 본다";
            Decide(p, accept, boss.Name, why);
        }
    }

    /// <summary>채점 (결정 뒤 몇 분): 받았는데 안에 사람이 있었으면 틀림 · 거절했는데 사람이 있었으면 컴퓨터가 틀렸다 · 비어 있었으면 맞았다.</summary>
    private (int, string)? Grade(Proposal p)
    {
        var w = _w;
        if (p.State == ProposalState.Pending) return null;
        if (w.Tick - p.DecidedAt < SimTime.Minutes(5)) return null;
        if (p.Grader != null)
        {
            if (p.Grader(w, p) is not (int gs, string gw)) return null;
            p.Score = gs; p.Result = gw;
            if (gs < 0) w.Automation.Learn.Remember(p.RoomId, p.Kind, gw);
            return (gs, gw);
        }
        var names = p.Inside.Select(id => w.Crew.FirstOrDefault(c => c.Id == id)?.Name ?? "?").ToList();
        string who = string.Join("·", names);
        (int s, string why) r;
        if (p.Accepted)
        {
            var hurt = p.Inside.Select(id => w.Crew.FirstOrDefault(c => c.Id == id)).Where(c => c != null && (c.Down || c.Dead)).ToList();
            r = hurt.Count > 0 ? (-1, $"틀렸다 — 받았지만 안에 사람이 있었다: {who}") : (1, "맞았다 — 받아서 실행했고 사람을 잃지 않았다");
        }
        else r = names.Count > 0 ? (-1, $"틀렸다 — 거절이 옳았다: 안에 {who} (컴퓨터는 {(p.Believed is int b ? $"{b}명" : "모른다")}이라 믿었다)") : (1, "맞았다 — 거절했지만 방은 비어 있었다");
        p.Score = r.s;
        p.Result = r.why;
        if (r.s < 0) w.Automation.Learn.Remember(p.RoomId, p.Kind, r.why);
        return r;
    }
}

/// <summary>사람 확인 요청 하나 (컴퓨터 믿음을 사람이 눈으로 맞춘다).</summary>
public sealed class RoomCheck
{
    public int Id { get; init; }
    public int RoomId { get; init; }
    public string Why { get; init; } = "";
    public int ProposalId { get; init; } = -1;
    public bool Ghost { get; init; }
    public int CheckerId { get; set; } = -1;
    public long Since { get; init; }
    public bool Seen { get; set; }
    /// <summary>보고, 데려 나올 사람까지 다 데려 나왔다.</summary>
    public bool Done { get; set; }
    public List<int> Found { get; } = new();
}

public sealed partial class AutomationSystem
{
    private ProposalBoard? _asks;
    /// <summary>v16.6 제안 → 승인.</summary>
    public ProposalBoard Asks => _asks ??= new ProposalBoard(_world);

    /// <summary>사람 확인 요청 (가는 사람이 정해져 있다).</summary>
    public List<RoomCheck> Checks { get; } = new();
    public List<RoomCheck> ChecksDone { get; } = new();
    private int _checkNext = 1;
    public int CheckFound, CheckEmpty;

    /// <summary>사람이 가서 보게 한다: 가장 가까운 깨어 있는 사람 (그 방 사람 · 쓰러진 사람 · 아이 · 선체 밖은 빼고).</summary>
    public RoomCheck? RequestCheck(Room room, string why, int proposal = -1, bool ghost = false)
    {
        var w = _world;
        if (Checks.Any(k => k.RoomId == room.Id)) return null;
        var c = room.Center;
        var who = w.Crew.Where(x => x.CanAct && x.IsAwake && !x.IsChild && !x.Outside && x.Room != null && x.Room != room && x.CarryingPerson == null)
            .OrderBy(x => MathF.Abs(x.Cell.X + 0.5f - c.X) + MathF.Abs(x.Cell.Y + 0.5f - c.Y)).ThenBy(x => x.Id).FirstOrDefault()
            ?? w.Crew.Where(x => x.CanAct && !x.IsChild && !x.Outside && x.Room != room).OrderBy(x => x.Id).FirstOrDefault();
        if (who == null) return null;
        var k = new RoomCheck { Id = _checkNext++, RoomId = room.Id, Why = why, ProposalId = proposal, Ghost = ghost, CheckerId = who.Id, Since = w.Tick };
        Checks.Add(k);
        who.Interrupt(w);
        w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터: {Ko.IGa(who.Name)} {room.Name}에 가서 직접 봐 달라 — {why}", who.Id);
        Book.Add(ActKind.Advice, room, why, "감지기만 믿지 않는다", "사람 확인 요청", $"{who.Name} → {room.Name}", "chk:" + k.Id, 0, 20f,
            (world, a) => k.Seen ? (k.Found.Count > 0 || k.Ghost ? (1, k.Ghost ? "확인 — 헛불이었다 (오경보)" : $"확인 — 안에 {k.Found.Count}명이 있었다") : (2, "확인 — 비어 있었다")) : world.Tick - k.Since > SimTime.Minutes(40) ? (2, "확인하지 못했다") : null);
        return k;
    }

    /// <summary>확인 요청 정리 (사람이 쓰러졌거나 너무 오래되면 다른 사람에게).</summary>
    private void UpdateChecks()
    {
        var w = _world;
        for (int i = Checks.Count - 1; i >= 0; i--)
        {
            var k = Checks[i];
            var who = w.Crew.FirstOrDefault(c => c.Id == k.CheckerId);
            bool stale = w.Tick - k.Since > SimTime.Minutes(40);
            if (k.Done || stale || who == null || !who.CanAct)
            {
                Checks.RemoveAt(i);
                ChecksDone.Add(k);
                if (ChecksDone.Count > 30) ChecksDone.RemoveAt(0);
                if (!k.Seen && !stale && who != null && !who.CanAct) RequestCheck(w.Ship.Rooms[k.RoomId], k.Why, k.ProposalId, k.Ghost);
            }
        }
    }

    /// <summary>확인하러 간 사람이 그 방을 봤다: 믿음과 실제를 맞추고, 안에 있던 사람에게 알린다.</summary>
    internal void Inspected(RoomCheck k, CrewMember by)
    {
        var w = _world;
        var room = w.Ship.Rooms[k.RoomId];
        k.Seen = true;
        var b = Belief.Of(room);
        var inside = w.Crew.Where(c => !c.Dead && c.Room == room && c != by && !c.Outside).ToList();
        bool wrongPeople = b.People != inside.Count || b.Fault == SensorFault.Blind;
        bool ghost = b.Fault == SensorFault.Ghost && w.Fire.CountIn(room) == 0;
        foreach (var c in inside) k.Found.Add(c.Id);
        if (ghost) { b.FalseAlarms++; w.Log.Add(w.Tick, LogKind.Work, $"{room.Name} — 불은 없었다. 감지기 오경보 (오경보 {b.FalseAlarms}번째 · 이 감지기를 덜 믿는다)", by.Id); Trusts.Change(by, -0.03f, $"{room.Name} 헛불 경보에 뛰어갔다", quiet: true); }
        Belief.Checked(room, by, wrongPeople || ghost);
        if (inside.Count > 0)
        {
            CheckFound++;
            string names = string.Join("·", inside.Select(c => c.Name));
            w.Log.Add(w.Tick, LogKind.Work, $"{Ko.EulReul(room.Name)} 직접 봤다 — 안에 {names}" + (wrongPeople ? " (컴퓨터는 비었다고 믿었다)" : ""), by.Id);
            foreach (var c in inside)
            {
                c.Mind.Knows[$"fire:{room.Id}"] = (KnowSource.Seen, w.Tick, $"{room.Name} 불");
                if (!c.Down) c.Interrupt(w);
            }
            if (wrongPeople)
            {
                w.History.Add(w, HistoryKind.Response, $"{Ko.IGa(by.Name)} {room.Name}에 직접 가 봤다 — 컴퓨터가 빈 방이라 믿은 곳에 {names}", room, inside.Append(by));
                Trusts.Change(by, -0.06f, $"컴퓨터가 빈 방이라던 {room.Name}에 {names}");
                w.Minds.ComputerResult(-0.04f, $"{room.Name} 문 감지기가 틀어져 사람을 못 셌다");
            }
            if (k.ProposalId >= 0 && Asks.All.FirstOrDefault(p => p.Id == k.ProposalId) is Proposal pr)
                foreach (var c in inside)
                {
                    pr.Found.Add(c.Id);
                    if (pr.Inside.Contains(c.Id))
                        Trusts.Change(c, -0.25f, $"컴퓨터는 내가 {room.Name}에 있는 줄 몰랐다 — {Ko.EulReul(pr.Title)} 하려 했다. {Ko.IGa(by.Name)} 직접 보러 와서 살았다");
                }
        }
        else
        {
            CheckEmpty++;
            w.Log.Add(w.Tick, LogKind.Work, $"{Ko.EulReul(room.Name)} 직접 봤다 — 아무도 없다" + (b.Fault == SensorFault.None && !ghost ? " (컴퓨터 믿음이 맞았다)" : ""), by.Id);
        }
    }
}

/// <summary>v16.6 사람 확인: 컴퓨터가 부른 사람이 그 방에 가서 보고, 쓰러진 사람이 있으면 업어 안전한 곳으로.
/// 각오하고 들어간다 (불 옆이라도 확인을 마칠 때까지 버틴다) — 끊기면 다시 이어서 (이미 봤으면 데려 나오기부터).</summary>
public sealed class CheckRoomActivity : Activity
{
    public override string Id => "roomcheck";
    public override string Label => "직접 확인";

    private static RoomCheck? Mine(CrewMember c, World w)
    {
        foreach (var k in w.Automation.Checks) if (k.CheckerId == c.Id && !k.Done) return k;
        return null;
    }

    private static CrewMember? Patient(World w, Room room, CrewMember cm) =>
        w.Crew.Where(x => x.Down && !x.Dead && x.Room == room && x.CarriedBy == null && x != cm)
            .OrderBy(x => (x.Position - cm.Position).LengthSquared()).ThenBy(x => x.Id).FirstOrDefault();

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (w.Automation.Checks.Count == 0 || c.Down || c.Outside) return (0f, "—");
        if (Mine(c, w) is not RoomCheck k) return (0f, "—");
        var room = w.Ship.Rooms[k.RoomId];
        return (1.25f, k.Seen ? $"컴퓨터 확인 요청 — {room.Name}에서 쓰러진 사람을 데려 나온다" : $"컴퓨터 확인 요청 — {room.Name} ({k.Why})");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (Mine(c, w) is not RoomCheck k) return null;
        var room = w.Ship.Rooms[k.RoomId];
        if (k.Seen && Patient(w, room, c) == null) { k.Done = true; return null; }
        Cell? look = null;
        int best = int.MaxValue;
        foreach (var cell in room.Cells)
        {
            int d = dist.Get(cell);
            if (d < 0 || d >= best || !w.Ship.IsWalkable(cell)) continue;
            best = d; look = cell;
        }
        if (look is not Cell at) return null;
        // 내려놓을 곳: 그 방 밖, 불·위험이 없는 가장 가까운 바닥
        Cell? safe = null;
        int sb = int.MaxValue;
        foreach (var r in w.Ship.LiveRooms)
        {
            if (r == room || Atmosphere.Danger(r) > 0.1f || r.Leaking || w.Fire.CountIn(r) > 0 || r.EvacuateBy >= 0) continue;
            foreach (var cell in r.Cells)
            {
                int d = dist.Get(cell);
                if (d < 0 || d >= sb || !w.Ship.IsOpenFloor(cell) || w.IsSpotTaken(cell, c)) continue;
                sb = d; safe = cell;
            }
        }
        CrewMember? patient = k.Seen ? Patient(w, room, c) : null;
        var toils = new List<Toil>
        {
            new DoToil((cm, world) => { cm.Dashing = true; return true; }), // 각오하고 들어간다
            new GotoToil(at),
        };
        if (!k.Seen)
        {
            toils.Add(new WaitToil(SimTime.Minutes(0.15f), Pose.Standing));
            toils.Add(new DoToil((cm, world) =>
            {
                world.Automation.Inspected(k, cm);
                patient = Patient(world, room, cm);
                if (patient == null) k.Done = true;
                return true;
            }));
        }
        toils.Add(new GotoToilLate(cm => patient?.Cell));
        toils.Add(new DoToil((cm, world) =>
        {
            if (patient == null) return true;
            if (!patient.Down || patient.Dead || patient.CarriedBy != null || (patient.Position - cm.Position).Length() > 2.2f) { patient = null; return true; }
            patient.CarriedBy = cm;
            cm.CarryingPerson = patient;
            world.Log.Add(world.Tick, LogKind.Work, $"확인하러 들어간 {room.Name}에서 쓰러진 {Ko.EulReul(patient.Name)} 업었다", cm.Id);
            return true;
        }));
        toils.Add(new GotoToilLate(cm => patient != null && safe is Cell s ? s : null));
        toils.Add(new DoToil((cm, world) =>
        {
            if (patient == null || cm.CarryingPerson != patient || safe is not Cell s) { if (Patient(world, room, cm) == null) k.Done = true; return true; }
            patient.CarriedBy = null;
            cm.CarryingPerson = null;
            patient.Position = s.Center;
            patient.PreviousPosition = s.Center;
            patient.Room = world.Ship.RoomAt(s);
            patient.LaidSafe = true;
            cm.Stats.Rescues++;
            cm.Stats.Emergencies++;
            patient.ChangeAffinity(cm, 0.2f);
            world.Relations.Remember(patient, cm, RelationReason.SavedMe, $"컴퓨터가 빈 방이라던 {room.Name}에서 나를 업어 나왔다");
            MarkLog.Add(patient.Memory.Marks, world.Tick, $"{Ko.IGa(cm.Name)} {room.Name}에서 업어 나왔다");
            MarkLog.Add(cm.Memory.Marks, world.Tick, $"직접 확인하러 들어간 {room.Name}에서 {Ko.EulReul(patient.Name)} 업어 나왔다");
            world.History.Add(world, HistoryKind.Response, $"{Ko.IGa(cm.Name)} 직접 확인하러 들어간 {room.Name}에서 {Ko.EulReul(patient.Name)} 업어 나왔다", room, new[] { cm, patient });
            world.Automation.Trusts.Saved(patient, cm, room);
            if (Patient(world, room, cm) == null) k.Done = true; // 남은 사람이 있으면 다시 들어간다
            return true;
        }));
        return new Job(this, "직접 확인", toils) { LogText = k.Seen ? $"{room.Name}에서 쓰러진 사람을 데려 나온다" : $"컴퓨터 확인 요청 — {room.Name}에 가서 본다", LogKind = LogKind.Work, Urgent = true };
    }
}
