using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.26 ⑤ 끊겨도 구획별로 버틴다 · ⑥ 자기 상태 진단 · 기능 재배치 · 스스로 감시.
//  ⑤ 데이터선이 끊기거나(그 방만) 중앙이 통째로 멎으면(예비 제어도 없이) 구역 제어기가 제한된 자율로 돈다:
//     불 감지 → 환기 막기 · 기압이 떨어지면 문 잠그기(마지막 방침이 허락하면) · 생명유지 설비는 그대로 붙잡기.
//     중앙은 그 구획을 "모른다"고 안다 (지휘 탭 · 확인할 방법이 그 방 값을 증상으로 치지 않는다).
//     위험한 원격 명령(시험 운전 · 출력 올리기)은 유효 시간이 지나면 하지 않는다 — 이어지면 실제 상태부터 대조하고,
//     중앙과 현지 판단 차이를 기록하고, 현지가 이미 한 일은 다시 보내지 않는다 (명령 중복 없이 권한을 넘겨받는다).
//  ⑥ 과열 → 긴 예측 · 사고 검토를 줄이고 경보 · 제어는 유지 / 계산이 몰리면 급한 것만 / 예측이 거듭 크게 빗나가면 비교를 떼고 정해진 순서로 /
//     저장장치가 상하면 최근 사건 · 지금 계획 · 방침부터 옮겨 담는다 / 주컴퓨터실이 위험하면 예비로 상태 · 권한을 넘긴다 /
//     센서가 여럿 안 닿으면 확신을 낮추고 순찰을 부탁한다 / 같은 명령을 되풀이하거나 켰다 껐다 하면 스스로 멈추고 원인을 본다.

public sealed class ZoneState
{
    public int RoomId { get; init; }
    public long Since { get; init; }
    /// <summary>끊길 때의 마지막 방침 (자동 격벽 · 배/사람 우선).</summary>
    public string Policy { get; init; } = "";
    public bool AutoSeal { get; init; }
    // 중앙이 끊기기 직전에 믿던 상태
    public bool VentWas { get; init; }
    public bool FireWas { get; init; }
    public int LockedWas { get; init; }
    public bool LeakWas { get; init; }
    public List<(long tick, string what)> Journal { get; } = new();
    public HashSet<string> Did { get; } = new();
}

public sealed class ComputerZones
{
    private readonly World _w;
    private readonly SortedDictionary<int, ZoneState> _alone = new();
    public int Episodes, LocalActs, Expired, Diffs, Reconnects, DupesAvoided;
    public List<(long tick, int room, string text)> Reports { get; } = new();

    public ComputerZones(World w) => _w = w;

    public IEnumerable<ZoneState> Alone => _alone.Values;
    public bool IsAlone(Room r) => _alone.ContainsKey(r.Id);
    public ZoneState? Of(Room r) => _alone.TryGetValue(r.Id, out var z) ? z : null;

    private bool Cut(Room r)
    {
        var a = _w.Automation;
        if (!a.Core.ZoneController(r) || r.Type == RoomType.Corridor) return false;
        return !r.DataLinked || !a.CoreOnline && !a.BackupActive;
    }

    /// <summary>5틱마다 (빠른 규칙): 끊긴 방만 현지 판단 · 이어지면 대조.</summary>
    public void Update()
    {
        var w = _w;
        if (w.Tick % 5 != 0) return;
        var a = w.Automation;
        foreach (var r in w.Ship.Rooms)
        {
            if (r.Detached) { _alone.Remove(r.Id); continue; }
            bool cut = Cut(r);
            if (cut && !_alone.ContainsKey(r.Id)) Start(r);
            if (cut) Local(r, _alone[r.Id]);
            else if (_alone.TryGetValue(r.Id, out var z)) Reconnect(r, z);
        }
    }

    private void Start(Room r)
    {
        var w = _w;
        var a = w.Automation;
        int auto = w.Policies["autoscope"];
        var z = new ZoneState
        {
            RoomId = r.Id, Since = w.Tick, AutoSeal = auto >= 1, Policy = $"{(auto >= 1 ? "샐 때 스스로 잠금" : "경보만")} · {(a.ShipFirst ? "배 우선" : "사람 우선")}",
            VentWas = r.VentOpen, FireWas = w.Fire.IsKnown(r), LockedWas = r.Doors.Count(d => d.Locked), LeakWas = r.Leaking,
        };
        _alone[r.Id] = z;
        Episodes++;
        if (a.CoreOnline) w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: {r.Name}과(와) 끊겼습니다 — 그쪽 사정은 모릅니다. 구역 제어기가 마지막 방침({z.Policy})대로 버틴다고 봅니다");
    }

    /// <summary>구역 제어기의 제한된 자율 (제자리 감지기만 · 원격 판단 없음).</summary>
    private void Local(Room r, ZoneState z)
    {
        var w = _w;
        if (!r.Powered) return; // 제어기도 전기로 돈다
        // 불 감지 → 환기를 막는다
        if (r.VentOpen && !r.DamperJammed && !r.DamperStuck && w.Fire.CountIn(r) > 0)
        {
            r.VentOpen = false;
            Act(r, z, "vent", "불 — 환기를 막았다");
        }
        // 기압이 떨어진다 → 마지막 방침이 허락하면 문을 잠근다
        if (z.AutoSeal && r.Leaking && r.Air.Pressure < 88f)
        {
            int n = 0;
            foreach (var d in r.Doors) if (!d.IsExternal && !d.Removed && d.Powered && !d.Locked && !d.HoldOpen) { d.Locked = true; n++; }
            if (n > 0) Act(r, z, "seal", $"기압이 떨어진다 — 문 {n}개를 잠갔다");
        }
        // 생명유지 · 의무실: 최소 기능은 그대로 붙잡는다 (원격으로 끌 수도 없다)
        if (r.Type is RoomType.LifeSupport or RoomType.Medbay && !z.Did.Contains("hold")) Act(r, z, "hold", "생명유지 설비를 그대로 붙잡는다");
    }

    private void Act(Room r, ZoneState z, string key, string what)
    {
        z.Did.Add(key);
        z.Journal.Add((_w.Tick, what));
        LocalActs++;
        _w.Log.Add(_w.Tick, LogKind.Ship, $"{r.Name} 구역 제어기: {what}");
    }

    /// <summary>다시 이어짐: 실제 상태부터 대조 → 차이를 남기고 · 현지가 한 일은 다시 보내지 않고 · 지난 위험 명령은 버린다.</summary>
    private void Reconnect(Room r, ZoneState z)
    {
        var w = _w;
        var a = w.Automation;
        _alone.Remove(r.Id);
        Reconnects++;
        float minutes = (w.Tick - z.Since) / (float)SimTime.Minutes(1);
        var diffs = new List<string>();
        if (z.VentWas != r.VentOpen) diffs.Add($"환기: 중앙은 {(z.VentWas ? "열림" : "닫힘")}으로 알았다 → 실제 {(r.VentOpen ? "열림" : "닫힘")}" + (z.Did.Contains("vent") ? " (현지가 불 때문에 막았다)" : ""));
        bool fireNow = w.Fire.CountIn(r) > 0;
        if (z.FireWas != fireNow) diffs.Add(fireNow ? "끊긴 사이 불이 났다" : "끊긴 사이 불이 꺼졌다");
        int locked = r.Doors.Count(d => d.Locked);
        if (locked != z.LockedWas) diffs.Add($"잠긴 문: {z.LockedWas} → {locked}" + (z.Did.Contains("seal") ? " (현지가 잠갔다)" : ""));
        if (z.LeakWas != r.Leaking) diffs.Add(r.Leaking ? "끊긴 사이 새기 시작했다" : "끊긴 사이 새는 게 멎었다");
        Diffs += diffs.Count;
        // 현지가 이미 한 일: 중앙 쪽 계획 · 대응이 같은 명령을 다시 보내지 않게 (그대로 받는다)
        DupesAvoided += z.Did.Count(k => k is "vent" or "seal");
        // 끊긴 동안 기다리던 위험한 원격 명령: 유효 시간이 지났으면 하지 않고 상태부터 다시 본다
        foreach (var p in a.Recovery.Plans.Where(p => p.Open && p.RoomId == r.Id).ToList())
            if (p.Step is FixStep s && s.State == FixState.Wait && s.Act.Valid > 0f && minutes > s.Act.Valid)
            {
                Expired++;
                a.Recovery.Replan(p, $"끊긴 {minutes:0}분 동안 미뤄 둔 '{s.Name}'은(는) 유효 시간({s.Act.Valid:0}분)이 지났다 — 하지 않고 상태부터 다시 본다");
            }
        string text = diffs.Count == 0 ? $"{r.Name}과(와) {minutes:0}분 만에 다시 이어졌다 — 대조해 보니 그대로다"
            : $"{r.Name}과(와) {minutes:0}분 만에 다시 이어졌다 — 대조: {string.Join(" · ", diffs)}";
        Reports.Add((w.Tick, r.Id, text));
        if (Reports.Count > 20) Reports.RemoveAt(0);
        if (a.CoreOnline)
        {
            a.Book.Add(ActKind.Zone, r, $"{r.Name} 구역과 다시 이어짐 ({minutes:0}분 끊김)", diffs.Count == 0 ? "중앙이 믿던 것과 같다" : string.Join(" · ", diffs),
                z.Journal.Count > 0 ? $"현지가 한 일 {z.Journal.Count}가지를 그대로 받았다 — 같은 명령은 다시 보내지 않는다" : "상태를 받아 계획을 이어 간다", "", $"zone:{r.Id}:{z.Since}", 0, 10f);
            w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: {text}");
        }
    }

    internal void Hash(Action<long> I, Action<float> F)
    {
        I(_alone.Count); I(Episodes); I(LocalActs); I(Expired); I(Diffs); I(Reconnects); I(DupesAvoided);
    }
}

/// <summary>⑥ 자기 상태 진단 · 기능 재배치 · 스스로 감시.</summary>
public sealed class ComputerSelf
{
    private readonly World _w;
    private long _next;
    private readonly Dictionary<string, List<long>> _hits = new();
    private readonly Dictionary<string, long> _holds = new();
    private readonly HashSet<string> _noted = new();
    public int Stops, Toggles, Relocations, StorageSaves, PatrolAsks, Detaches;
    /// <summary>분석 기능 이상 — 비교를 떼고 정해진 순서로 (단순 규칙).</summary>
    public bool Simple { get; private set; }
    private long _simpleUntil;
    public bool Hot { get; private set; }
    public bool Crowded { get; private set; }
    public bool Moved { get; private set; }
    /// <summary>센서가 닿는 몫 0~1 (낮으면 확신을 낮춘다).</summary>
    public float Sight { get; private set; } = 1f;
    public List<(long tick, string text)> Notes { get; } = new();
    public List<Room> PatrolRooms { get; } = new();
    public string Status => Simple ? "분석 기능을 떼고 정해진 순서로" : Moved ? "예비 연산기로 넘겨 돈다" : Hot ? "달아올라 긴 예측 · 검토를 줄였다" : Crowded ? "계산이 몰려 급한 것만" : Sight < 0.7f ? $"센서가 {Sight * 100:0}%만 닿는다 — 확신을 낮췄다" : "정상";

    public ComputerSelf(World w) => _w = w;

    /// <summary>이 기능을 지금 줄였나 (예측 · 검토 · 설비 — 경보 · 제어 · 냉각 · 문은 줄이지 않는다).</summary>
    public bool Thin(string what) => what switch
    {
        "검토" or "예측" => Hot || Crowded || Moved,
        "설비" => Crowded,
        _ => false,
    };

    // ───────────── 스스로 감시: 같은 명령 · 켰다 껐다 ─────────────

    private static string Class(string what)
    {
        if (what.Contains("켬") || what.Contains("끔")) return "켜고 끔";
        if (what.Contains("올림") || what.Contains("내림")) return "올리고 내림";
        if (what.Contains("닫") || what.Contains("열")) return "열고 닫음";
        if (what.Contains("출력")) return "출력";
        var s = new string(what.Where(ch => !char.IsDigit(ch) && ch != '%' && ch != '.').ToArray());
        int i = s.IndexOf(" (", StringComparison.Ordinal);
        return i > 0 ? s[..i] : s;
    }

    private static string Key(CmdTarget t, int id, string what) => $"{t}:{id}:{Class(what)}";

    /// <summary>ComputerCommand.Line 훅: 명령을 센다 (사람 · 방송 · 단말은 빼고).</summary>
    public void Note(CmdTarget t, int id, string what, Room? room = null)
    {
        if (t is CmdTarget.Crew or CmdTarget.Broadcast or CmdTarget.Terminal or CmdTarget.Self or CmdTarget.Robot or CmdTarget.Drone) return;
        var w = _w;
        string key = Key(t, id, what);
        if (!_hits.TryGetValue(key, out var list)) _hits[key] = list = new();
        list.Add(w.Tick);
        list.RemoveAll(x => w.Tick - x > SimTime.Hours(2));
        string cls = Class(what);
        int limit = cls == "출력" ? 10 : cls is "켜고 끔" or "열고 닫음" or "올리고 내림" ? 4 : 4;
        if (list.Count < limit || _holds.TryGetValue(key, out var h) && h > w.Tick) return;
        _holds[key] = w.Tick + SimTime.Hours(1);
        Stops++;
        if (cls is "켜고 끔" or "열고 닫음") Toggles++;
        var a = w.Automation;
        string name = t == CmdTarget.Breaker ? $"{PowerGrid.CircuitName(id)} 회로 차단기" : t == CmdTarget.Machine ? w.Ship.Furniture.FirstOrDefault(f => f.Id == id)?.Name ?? "설비" : t == CmdTarget.Reactor ? "원자로" : "그 대상";
        string text = cls is "켜고 끔" or "열고 닫음" ? $"{name}을(를) 두 시간 안에 {list.Count}번 {cls}했다 — 오락가락한다" : $"{name}에 같은 명령({what})을 두 시간 안에 {list.Count}번 보냈다";
        Notes.Add((w.Tick, text + " — 멈추고 원인을 본다"));
        if (Notes.Count > 20) Notes.RemoveAt(0);
        a.Book.Add(ActKind.Check, room, text, "되풀이해도 낫지 않는다 — 명령이 아니라 원인이 문제다", "한 시간 동안 이 명령을 멈췄다", "사람이 가서 원인을 봐 달라", "self:" + key, SimTime.Hours(1), 60f);
        w.Log.Add(w.Tick, LogKind.Warning, $"{a.Voice.Call}: {a.Manner.Speak(text + " — 스스로 멈추고 원인부터 보겠습니다")}");
    }

    /// <summary>이 명령을 보내도 되나 (스스로 멈춰 둔 것은 안 된다).</summary>
    public bool Allow(CmdTarget t, int id, string what) => !(_holds.TryGetValue(Key(t, id, what), out var h) && h > _w.Tick);
    public bool Allow(int machineId, string what) => Allow(CmdTarget.Machine, machineId, what);
    /// <summary>계획이 원격으로 건 일도 센다.</summary>
    public void Note(int machineId, string what) { }

    // ───────────── 자기 진단 (1분마다) ─────────────

    public void Update()
    {
        var w = _w;
        if (w.Tick < _next) return;
        _next = w.Tick + SimTime.Minutes(1);
        var a = w.Automation;
        var core = a.Core;
        var m = a.Computer;
        // 과열: 긴 예측 · 검토를 줄인다 (경보 · 제어는 그대로)
        bool hot = core.SafeMode || (m?.Body.Room.Air.Temperature ?? 20f) > AutomationSystem.OverheatC - 4f;
        if (hot != Hot) { Hot = hot; Say(hot ? "달아오른다 — 긴 예측 · 사고 검토를 줄이고 경보 · 제어는 그대로 둔다" : "식었다 — 줄였던 예측 · 검토를 되돌린다"); }
        // 계산 몰림: 급한 것만
        bool crowded = a.Load > 1.05f;
        if (crowded != Crowded) { Crowded = crowded; Say(crowded ? $"계산이 몰린다 ({a.Load * 100:0}%) — 냉각 · 문 · 경보부터, 설비 계획은 미룬다" : "계산에 여유가 돌아왔다"); }
        // 분석 기능 이상: 예측이 거듭 크게 빗나가면 떼고 정해진 순서로
        if (!Simple && a.Review.MissStreak >= 3)
        {
            Simple = true;
            _simpleUntil = w.Tick + SimTime.Hours(6);
            Detaches++;
            a.Review.MissStreak = 0;
            Say("예측이 세 번 크게 빗나갔다 — 수순 비교를 떼고 정해진 순서(가장 안전한 쪽)로 한다 · 여섯 시간 뒤 다시 붙인다");
        }
        else if (Simple && w.Tick >= _simpleUntil) { Simple = false; Say("수순 비교를 다시 붙였다"); }
        // 저장 손상: 최근 사건 · 지금 계획 · 방침부터 옮겨 담는다
        if (m != null && m.Has(FaultKind.StorageFault))
        {
            if (_noted.Add("storage:" + m.Faults.First(f => f.Kind == FaultKind.StorageFault).Since))
            {
                int old = Math.Max(0, a.Book.Acts.Count - 60);
                if (old > 0) a.Book.Acts.RemoveRange(0, old);
                int tl = Math.Max(0, a.Foresee.Timeline.Count - 20);
                if (tl > 0) a.Foresee.Timeline.RemoveRange(0, tl);
                StorageSaves++;
                Say($"저장장치가 상했다 — 최근 사건 · 지금 계획 {a.Recovery.Plans.Count(p => p.Open)}개 · 방침 · 이 배에서 배운 값부터 옮겨 담았다 (오래된 기록 {old + tl}건은 버렸다)");
            }
        }
        // 주컴퓨터실 위험: 예비로 상태 · 권한을 넘긴다
        var room = m?.Body.Room;
        bool danger = room != null && (room.Leaking || w.Fire.IsKnown(room) || room.Air.Temperature > 45f || room.Air.Pressure < 70f);
        if (danger && !Moved)
        {
            Moved = true;
            Relocations++;
            Say($"{room!.Name}이(가) 위험하다 — 지금 계획 · 명령 상태를 예비 연산기로 넘긴다 (보낸 명령은 다시 보내지 않는다)");
        }
        else if (!danger && Moved) { Moved = false; Say("주컴퓨터실이 안전해졌다 — 본체로 되돌린다"); }
        // 센서망 단절: 확신을 낮추고 순찰을 부탁한다
        int live = 0, blind = 0;
        PatrolRooms.Clear();
        foreach (var r in w.Ship.LiveRooms)
        {
            if (r.Type == RoomType.Corridor) continue;
            live++;
            if (core.Reach(r) == 0 || a.Belief.Of(r).Fault == SensorFault.Blind) { blind++; if (PatrolRooms.Count < 2) PatrolRooms.Add(r); }
        }
        float sight = live == 0 ? 1f : 1f - blind / (float)live;
        if (sight < 0.75f && Sight >= 0.75f) { PatrolAsks++; Say($"센서가 {blind}곳에 안 닿는다 — 확신을 낮추고 {string.Join(" · ", PatrolRooms.Select(r => r.Name))} 순찰을 부탁한다"); }
        Sight = sight;
        if (sight >= 0.75f) PatrolRooms.Clear();
    }

    private void Say(string text)
    {
        var w = _w;
        Notes.Add((w.Tick, text));
        if (Notes.Count > 20) Notes.RemoveAt(0);
        w.Log.Add(w.Tick, LogKind.Ship, $"{w.Automation.Voice.Call}: {w.Automation.Manner.Speak(text)}");
    }

    internal void Hash(Action<long> I, Action<float> F)
    {
        I(Stops); I(Toggles); I(Relocations); I(StorageSaves); I(PatrolAsks); I(Detaches); I(Simple ? 1 : 0); I(_holds.Count); F(Sight);
    }
}

public sealed partial class WorkBoard
{
    /// <summary>v16.26 ⑥ 센서가 안 닿는 방 순찰 부탁 (ComputerSelf).</summary>
    private void ScanSelf(Poster post)
    {
        if (_world.Automation.SelfOrNull is not ComputerSelf s || s.PatrolRooms.Count == 0) return;
        foreach (var r in s.PatrolRooms) if (!r.Detached && !r.OffLimits) post(WorkKind.PreventiveCheck, WorkTarget.OfRoom(r), 0.6f, Skill.Mechanics, "주 컴퓨터: 센서가 안 닿는다 — 한 바퀴 돌아봐 달라");
    }
}

public sealed partial class AutomationSystem
{
    private ComputerZones? _zones;
    private ComputerSelf? _self;
    /// <summary>v16.26 ⑤ 끊겨도 구획별로 버틴다.</summary>
    public ComputerZones Zones => _zones ??= new ComputerZones(_world);
    /// <summary>v16.26 ⑥ 자기 진단 · 기능 재배치 · 스스로 감시.</summary>
    public ComputerSelf SelfWatch => _self ??= new ComputerSelf(_world);
    internal ComputerSelf? SelfOrNull => _self;
}
