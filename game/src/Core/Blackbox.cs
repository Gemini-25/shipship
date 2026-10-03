using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.7 블랙박스: 배의 감지 기록(문 · 밸브 · 전력 · 위치 · 명령 · 정비 · 경보)을 남기는 주황색 상자.
//  · 설치: 서버실 → 함교 → 통신실 순으로 한 곳 · 벽 모서리에 볼트로 박는다 (그림은 ShipViewBlackbox.cs).
//  · 받아 적기: 1분마다 사람이 있는 방 · 문 잠김과 열림 · 분전함 · 밸브 · 전기 · 주 컴퓨터가 내린 명령 · 설비 경보를 비교해 바뀐 것만 남긴다.
//    정비를 마친 사람 · 경보를 끈 사람 · 밸브를 돌린 사람은 그 자리 단말 기록으로 이름이 남는다.
//  · 지우기: 권한이 있는 사람은 단말에서 구간을 지울 수 있다 — 지운 줄은 사라지지만 상자 안쪽 봉인 기록에
//    "언제 · 어떤 권한으로 · 어디 단말에서" 지웠는지가 남는다 (솜씨가 좋으면 권한 표시까지 지운다). 지운 자리는 빈 구간으로 보인다.
//  · 망가짐: 상자가 있는 방에 불 · 폭발 · 감압이 오면 닳는다 → 기록 칩이 타면 읽을 수 없다 (그때는 사람들이 본 것뿐) →
//    전기 솜씨가 있는 사람이 새 칩으로 갈아 끼운다 (옛 기록은 없다).
//  · 주 컴퓨터는 상자를 읽어 빈 구간을 찾아낸다 (InquirySystem).

public enum BoxKind : byte { Place, Door, Lock, Valve, Breaker, Power, Order, Work, Alarm, Silence, Fire, Repair }

public readonly record struct BoxEntry(long Tick, BoxKind Kind, short Room, short Who, int Ref, byte Val);

/// <summary>지운 구간 하나 (빈 구간). Wiper 는 세계의 진실이라 기록에는 없다 — 권한 · 시각 · 단말만 남는다.</summary>
public sealed class BoxWipe
{
    public int Id { get; init; }
    public long From { get; init; }
    public long To { get; init; }
    public int Room { get; init; } = -1;
    public long At { get; init; }
    public int Wiper { get; init; } = -1;
    public string Access { get; init; } = "";
    public int Terminal { get; init; } = -1;
    public int Slip { get; init; } = -1;
    public int Removed { get; set; }
    public long Noticed { get; set; } = -1;
}

public sealed class BlackboxStats
{
    public int Entries, Wipes, WipedLines, Damaged, Wrecked, Repaired, Reads, GapsSeen;
}

public sealed class BlackboxSystem
{
    private readonly World _w;
    public static bool Off { get; set; }
    public BlackboxSystem(World w) => _w = w;
    public BlackboxStats Stats { get; } = new();

    public const int Capacity = 9000;
    private readonly List<BoxEntry> _log = new();
    public IReadOnlyList<BoxEntry> Log => _log;
    public List<BoxWipe> Wipes { get; } = new();
    private int _wipeId;

    // ── 상자 ──
    public int RoomId { get; private set; } = -1;
    public Cell At { get; private set; }
    public float Health { get; private set; } = 1f;
    public bool Present => RoomId >= 0;
    /// <summary>기록 칩이 타서 읽을 수도 · 적을 수도 없다.</summary>
    public bool Wrecked { get; private set; }
    public long WreckedAt { get; private set; } = -1;
    public long FreshSince { get; private set; }
    public int Repairer { get; private set; } = -1;
    public Room? Room => RoomId >= 0 && RoomId < _w.Ship.Rooms.Count ? _w.Ship.Rooms[RoomId] : null;
    public bool Recording => Present && !Wrecked && !(Room?.Detached ?? true);
    /// <summary>화면: 방금 받아 적었다 (불빛이 깜박인다).</summary>
    public long LastWrite { get; private set; } = -1;

    private long _next;
    private readonly Dictionary<int, int> _room = new();
    private readonly Dictionary<int, (bool open, bool locked, long t)> _doors = new();
    private readonly Dictionary<int, byte> _flags = new();
    private readonly Dictionary<int, int> _faults = new();
    private int _lastAct = -1;

    public static long UpdateTicks;

    private void Install()
    {
        var w = _w;
        Room? r = null;
        foreach (var t in new[] { RoomType.ServerRoom, RoomType.Bridge, RoomType.Comms })
        {
            r = w.Ship.Rooms.FirstOrDefault(x => !x.Detached && x.Kind == t);
            if (r != null) break;
        }
        r ??= w.Ship.Rooms.FirstOrDefault(x => !x.Detached && x.Type != RoomType.Corridor);
        if (r == null || r.Cells.Count == 0) return;
        RoomId = r.Id;
        // 벽 모서리 (가구가 없는 칸 중 가장 위 · 왼쪽)
        var taken = new HashSet<Cell>(r.Furniture.SelectMany(f => f.Cells));
        At = r.Cells.Where(c => !taken.Contains(c)).OrderBy(c => c.Y).ThenBy(c => c.X).DefaultIfEmpty(r.Cells[0]).First();
        FreshSince = w.Tick;
    }

    // ───────────────────────────── 적기 ─────────────────────────────

    public void Note(BoxKind k, Room? r, CrewMember? who, int refId = -1, byte val = 0) => Note(k, r?.Id ?? -1, who?.Id ?? -1, refId, val);

    public void Note(BoxKind k, int room, int who, int refId = -1, byte val = 0)
    {
        if (Off || !Recording) return;
        _log.Add(new BoxEntry(_w.Tick, k, (short)room, (short)who, refId, val));
        Stats.Entries++;
        LastWrite = _w.Tick;
        if (_log.Count > Capacity) _log.RemoveRange(0, 1500);
    }

    public void Update(float dt)
    {
        var w = _w;
        if (Off || w.Tick < _next) return;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _next = w.Tick + SimTime.Minutes(1);
        if (!Present) Install();
        if (!Present) return;
        Wear();
        if (Recording) Poll();
        UpdateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    /// <summary>불 · 감압 · 떨어져 나감이 상자를 닳게 한다. 기록 칩이 타면 고칠 때까지 아무것도 남지 않는다.</summary>
    private void Wear()
    {
        var w = _w;
        var r = Room;
        if (r == null) return;
        float hit = 0f;
        if (r.Detached) hit = 1f;
        else
        {
            float f = w.Fire.At(At);
            if (f > 0f) hit += 0.05f + 0.1f * f;
            else if (w.Fire.CountIn(r) > 0) hit += 0.012f;
            if (r.Air.Pressure < 30f) hit += 0.01f;
        }
        if (hit > 0f && !Wrecked)
        {
            if (Health > 0.6f && Health - hit <= 0.6f) Stats.Damaged++;
            Health = MathF.Max(0f, Health - hit);
            if (Health < 0.3f) Wreck(r.Detached ? "떨어져 나간 방과 함께 사라졌다" : w.Fire.CountIn(r) > 0 ? "불에 기록 칩이 탔다" : "부서졌다");
        }
        // 갈아 끼우기: 망가지고 반나절 · 그 방이 조용해지면 전기 솜씨가 있는 사람이 새 칩을 넣는다
        if (Wrecked && !r.Detached && w.Tick - WreckedAt > SimTime.Hours(12) && w.Fire.CountIn(r) == 0 && r.Air.Pressure > 80f)
        {
            var hand = w.Crew.Where(c => !c.Dead && c.CanAct && !c.IsChild && c.IsAwake && !c.Outside).OrderByDescending(c => c.SkillLevel(Skill.Electrical)).ThenBy(c => c.Id).FirstOrDefault();
            if (hand != null && hand.SkillLevel(Skill.Electrical) > 0.3f) Repair(hand);
        }
    }

    public void Wreck(string why)
    {
        var w = _w;
        if (Wrecked) return;
        Wrecked = true;
        WreckedAt = w.Tick;
        Health = MathF.Min(Health, 0.25f);
        Stats.Wrecked++;
        w.History.Add(w, HistoryKind.Damage, $"블랙박스가 {why} — 그때까지의 기록을 읽을 수 없다", Room, null, At, log: true);
    }

    public void Repair(CrewMember by)
    {
        var w = _w;
        Wrecked = false;
        Health = 1f;
        FreshSince = w.Tick;
        Repairer = by.Id;
        _log.Clear();
        Stats.Repaired++;
        Note(BoxKind.Repair, Room, by);
        w.Log.Add(w.Tick, LogKind.Work, $"블랙박스에 새 기록 칩을 갈아 끼웠다 — 그전 기록은 남지 않았다", by.Id);
        Life.Diary(w, by, "블랙박스 칩을 갈았다. 탄 칩은 상자째 보관함에 넣어 뒀다.");
    }

    /// <summary>1분마다: 바뀐 것만 적는다 (사람이 있는 방 · 문 · 분전함 · 밸브 · 전기 · 명령 · 경보).</summary>
    private void Poll()
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Dead) continue;
            int r = c.Outside ? -2 : c.Room?.Id ?? -1;
            if (!_room.TryGetValue(c.Id, out var was) || was != r) { _room[c.Id] = r; if (r != -1) Note(BoxKind.Place, r, c.Id); }
        }
        foreach (var d in w.Ship.Doors)
        {
            bool open = d.Openness > 0.5f, locked = d.Locked;
            if (!_doors.TryGetValue(d.Id, out var s)) { _doors[d.Id] = (open, locked, w.Tick); continue; }
            if (locked != s.locked) Note(BoxKind.Lock, d.RoomA?.Id ?? d.RoomB?.Id ?? -1, -1, d.Id, (byte)(locked ? 1 : 0));
            bool openEdge = open && !s.open && w.Tick - s.t > SimTime.Minutes(4);
            if (openEdge) Note(BoxKind.Door, d.RoomA?.Id ?? d.RoomB?.Id ?? -1, -1, d.Id, 1);
            _doors[d.Id] = (open, locked, openEdge || open != s.open && !open ? w.Tick : s.t);
        }
        foreach (var r in w.Ship.Rooms)
        {
            if (r.Detached) continue;
            byte f = (byte)((r.BreakerOff ? 1 : 0) | (r.ValveShut ? 2 : 0) | (r.Powered ? 4 : 0));
            if (!_flags.TryGetValue(r.Id, out var was)) { _flags[r.Id] = f; continue; }
            if (was == f) continue;
            _flags[r.Id] = f;
            if (((was ^ f) & 1) != 0) Note(BoxKind.Breaker, r.Id, -1, -1, (byte)(r.BreakerOff ? 1 : 0));
            if (((was ^ f) & 2) != 0) Note(BoxKind.Valve, r.Id, -1, -1, (byte)(r.ValveShut ? 1 : 0));
            if (((was ^ f) & 4) != 0) Note(BoxKind.Power, r.Id, -1, -1, (byte)(r.Powered ? 1 : 0));
        }
        var acts = w.Automation.Book.Acts;
        for (int i = 0; i < acts.Count; i++)
        {
            var a = acts[i];
            if (a.Id <= _lastAct) continue;
            _lastAct = a.Id;
            if (a.Kind is ActKind.Door or ActKind.Valve or ActKind.Breaker or ActKind.Damper or ActKind.Bulkhead or ActKind.Suppress or ActKind.Shed or ActKind.Alarm or ActKind.Broadcast)
                Note(BoxKind.Order, a.RoomId, -1, a.Id);
        }
        foreach (var m in w.Ship.Machines)
        {
            int n = m.Faults.Count;
            _faults.TryGetValue(m.Body.Id, out var was);
            if (n == was) continue;
            _faults[m.Body.Id] = n;
            if (n > was) Note(BoxKind.Alarm, m.Body.Room.Id, -1, m.Body.Id);
        }
        if (Room is Room br && w.Fire.CountIn(br) > 0) Note(BoxKind.Fire, br.Id, -1);
    }

    // ───────────────────────────── 지우기 ─────────────────────────────

    /// <summary>단말에서 구간을 지운다. 지운 줄은 사라지고, 봉인 기록에 시각 · 권한 · 단말이 남는다 (솜씨가 좋으면 권한 표시를 지운다).</summary>
    public BoxWipe? Wipe(CrewMember who, long from, long to, int room, Room terminal, int slip, bool hideAccess)
    {
        var w = _w;
        if (!Recording) return null;
        int before = _log.Count;
        _log.RemoveAll(e => e.Tick >= from && e.Tick <= to && (room < 0 || e.Room == room || e.Kind == BoxKind.Place && e.Who >= 0));
        var wp = new BoxWipe
        {
            Id = _wipeId++, From = from, To = to, Room = room, At = w.Tick, Wiper = who.Id, Access = hideAccess ? "" : AccessOf(who),
            Terminal = terminal.Id, Slip = slip, Removed = before - _log.Count,
        };
        Wipes.Add(wp);
        Stats.Wipes++;
        Stats.WipedLines += wp.Removed;
        LastWrite = w.Tick;
        return wp;
    }

    /// <summary>단말 권한 (지울 수 있는지 · 기록에 남는 표시).</summary>
    public string AccessOf(CrewMember c)
    {
        if (_w.Command.CaptainId == c.Id) return "함장 권한";
        return c.Role switch
        {
            CrewRole.Engineer or CrewRole.Technician => "기관 권한",
            CrewRole.Electrician => "전기 권한",
            CrewRole.Pilot => "항해 권한",
            CrewRole.Medic => "의무 권한",
            _ => "",
        };
    }

    /// <summary>블랙박스 구간을 지울 수 있나 (권한 · 솜씨).</summary>
    public bool CanWipe(CrewMember c) => Recording && AccessOf(c) is "함장 권한" or "기관 권한" or "전기 권한" && c.SkillLevel(Skill.Electrical) >= 0.3f;

    /// <summary>지울 단말이 있는 방 (상자 방 · 함교 · 통신실).</summary>
    public Room? Terminal()
    {
        var w = _w;
        foreach (var t in new[] { RoomType.ServerRoom, RoomType.Bridge, RoomType.Comms })
            if (w.Ship.Rooms.FirstOrDefault(x => !x.Detached && x.Kind == t) is Room r) return r;
        return Room;
    }

    // ───────────────────────────── 읽기 ─────────────────────────────

    /// <summary>구간의 기록 (지운 줄은 없다). 칩이 탔으면 null.</summary>
    public List<BoxEntry>? Read(long from, long to, Func<BoxEntry, bool>? pick = null)
    {
        if (!Present || Wrecked) return null;
        Stats.Reads++;
        var list = new List<BoxEntry>();
        foreach (var e in _log) if (e.Tick >= from && e.Tick <= to && (pick == null || pick(e))) list.Add(e);
        return list;
    }

    /// <summary>그 구간에 걸친 빈 구간 (지운 자리).</summary>
    public IEnumerable<BoxWipe> GapsIn(long from, long to) => Present && !Wrecked ? Wipes.Where(x => x.To >= from && x.From <= to && x.At >= FreshSince) : Enumerable.Empty<BoxWipe>();

    /// <summary>그 시각에 그 방에 있던 사람 (위치 기록으로).</summary>
    public List<int> WhoWasIn(int room, long at)
    {
        var last = new SortedDictionary<int, int>();
        foreach (var e in _log)
        {
            if (e.Tick > at) break;
            if (e.Kind == BoxKind.Place && e.Who >= 0) last[e.Who] = e.Room;
        }
        return last.Where(kv => kv.Value == room).Select(kv => kv.Key).ToList();
    }

    public string RoomName(int id) => id >= 0 && id < _w.Ship.Rooms.Count ? _w.Ship.Rooms[id].Name : id == -2 ? "선외" : "어딘가";
    private string CrewName(int id) => _w.Crew.FirstOrDefault(c => c.Id == id)?.Name ?? "누군가";
    private string ThingName(int fid) => _w.Ship.Furniture.FirstOrDefault(f => f.Id == fid)?.Label ?? "설비";

    /// <summary>한 줄 (사람이 읽는 말).</summary>
    public string Line(BoxEntry e)
    {
        string t = SimTime.Clock(e.Tick), r = RoomName(e.Room);
        return e.Kind switch
        {
            BoxKind.Place => $"{t} {r} — {CrewName(e.Who)} 위치",
            BoxKind.Door => $"{t} {r} 문 열림",
            BoxKind.Lock => $"{t} {r} 문 {(e.Val == 1 ? "잠김" : "풀림")}",
            BoxKind.Valve => e.Who >= 0 ? $"{t} {r} {ThingName(e.Ref)} 밸브 — {CrewName(e.Who)} 단말" : $"{t} {r} 밸브 {(e.Val == 1 ? "잠김" : "열림")}",
            BoxKind.Breaker => $"{t} {r} 분전함 {(e.Val == 1 ? "내림" : "올림")}",
            BoxKind.Power => $"{t} {r} 전기 {(e.Val == 1 ? "들어옴" : "끊김")}",
            BoxKind.Order => $"{t} {r} 주 컴퓨터 명령",
            BoxKind.Work => $"{t} {r} {ThingName(e.Ref)} 정비 마침 — {CrewName(e.Who)} 단말" + (e.Val == 1 ? " (손댄 시간이 짧다)" : ""),
            BoxKind.Alarm => $"{t} {r} {ThingName(e.Ref)} 경보",
            BoxKind.Silence => $"{t} {r} {ThingName(e.Ref)} 경보 끔 — {CrewName(e.Who)} 단말",
            BoxKind.Fire => $"{t} {r} 상자 곁에 불",
            BoxKind.Repair => $"{t} {r} 새 기록 칩 — {CrewName(e.Who)}",
            _ => t,
        };
    }

    /// <summary>빈 구간 한 줄 (지운 사람 이름은 없다 — 권한 · 시각 · 단말이 단서).</summary>
    public string GapLine(BoxWipe g) =>
        $"{SimTime.Clock(g.From)}–{SimTime.Clock(g.To)} 기록이 비어 있다 · {SimTime.Clock(g.At)}에 {RoomName(g.Terminal)} 단말에서 지움 · " + (g.Access != "" ? g.Access : "권한 표시까지 지워짐");

    public void Hash(Action<long> I, Action<float> F)
    {
        I(_log.Count); I(Wipes.Count); I(RoomId); F(Health); I(Wrecked ? 1 : 0);
        I(Stats.Entries); I(Stats.Wipes); I(Stats.Repaired);
        if (_log.Count > 0) { var e = _log[^1]; I(e.Tick); I((int)e.Kind); I(e.Who); }
    }
}
