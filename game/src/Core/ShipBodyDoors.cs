using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.3 문: 잠금 방식 · 출입 권한 표 · 잠긴 문 앞에서 기다리다 권한자를 부름 · 화재 때 자동 해제(방침) · 선실 노크와 대답 ·
// 잠그고 자기 · 열어 둔 문은 "들러도 된다" · 문틀 변형 · 패킹 노화(미세 누출 · 휘파람) · 센서 고장 · 정전 때 손으로 돌려 열기 ·
// 반대편 진공 표시판(고장이면 오판) · "문 좀 닫아!" · 얇은 칸막이 너머 엿듣기.

public enum LockKind : byte { None, Card, Finger, Key, Captain }

public enum AccessZone : byte { Open, Medicine, Reactor, Captain, Cabin, Security }

public sealed class DoorBody
{
    public int Door { get; init; }
    public LockKind Lock { get; set; }
    public AccessZone Zone { get; set; }
    /// <summary>통제되는 쪽 방 (그 방으로 들어갈 때만 막는다 — 나가는 건 언제나).</summary>
    public int Inner { get; set; } = -1;
    public bool OwnerLocked { get; set; }
    public bool HeldOpen { get; set; }
    public int HeldBy { get; set; } = -1;
    public long NoHoldUntil { get; set; } = -1;
    /// <summary>패킹 1 = 새것 · 0.25 아래면 미세 누출.</summary>
    public float Gasket { get; set; } = 1f;
    public bool Whistling { get; set; }
    public bool SensorBroken { get; set; }
    public bool IndicatorBroken { get; set; }
    /// <summary>고장 난 표시판이 멈춰 있는 값: true면 늘 "정상", false면 늘 "진공".</summary>
    public bool IndicatorSaysSafe { get; set; }
    public int Caller { get; set; } = -1;
    public int Helper { get; set; } = -1;
    public long WaitFrom { get; set; } = -1;
    public long CalledAt { get; set; } = -1;
    public bool Remote { get; set; }
    public long RemoteAt { get; set; } = -1;
    public List<int> Tried { get; } = new();
    public int Pass { get; set; } = -1;
    public long PassUntil { get; set; } = -1;
    public int Knocker { get; set; } = -1;
    public long KnockAt { get; set; } = -1;
    public long OpenSince { get; set; } = -1;
    public long ComplainedAt { get; set; } = -1000000;
    public long EmergencyNoted { get; set; } = -1000000;
    public int LastCycles { get; set; }
    public bool SealFailNoted { get; set; }
    public int ClaimedBy { get; set; } = -1;

    public static string ZoneName(AccessZone z) => z switch
    {
        AccessZone.Medicine => "약품고", AccessZone.Reactor => "원자로실", AccessZone.Captain => "함장실", AccessZone.Cabin => "선실", AccessZone.Security => "보안실", _ => "",
    };
    public static string LockName(LockKind k) => k switch
    {
        LockKind.Card => "카드", LockKind.Finger => "지문", LockKind.Key => "열쇠", LockKind.Captain => "함장 승인", _ => "없음",
    };
}

public sealed partial class BodySystem
{
    public List<DoorBody> Doors { get; } = new();
    private bool _fireRelease;
    private readonly Dictionary<(int, int, int), long> _heardAt = new();
    private readonly Dictionary<int, (int door, long until)> _lockedOut = new();
    private Dictionary<int, List<(int other, List<WallBody> walls)>> _thin = new();

    public DoorBody? DoorOf(Door d) => d.Id < Doors.Count ? Doors[d.Id] : null;
    /// <summary>화재로 출입 통제가 풀려 있다 (방침).</summary>
    public bool FireReleased => _fireRelease;

    private CrewMember? CrewById(int id) => id < 0 ? null : _w.Crew.FirstOrDefault(x => x.Id == id);

    private static int Beds(Room r) => r.Furniture.Count(f => f.Type == FurnitureType.Bed);

    private static bool CabinKind(RoomType k) => k is RoomType.Quarters or RoomType.PrivateCabins or RoomType.QuietQuarters or RoomType.WaterWallCabin;

    private void InitDoors() => EnsureDoors();

    private void EnsureDoors()
    {
        var ship = _w.Ship;
        while (Doors.Count < ship.Doors.Count)
        {
            var d = ship.Doors[Doors.Count];
            uint hsh = unchecked((uint)(d.Id * 2654435761u) ^ (uint)(_w.Seed * 40503));
            var db = new DoorBody { Door = d.Id, Gasket = 0.3f + 0.7f * ((hsh >> 8) % 1000) / 1000f, LastCycles = d.Cycles, IndicatorSaysSafe = (hsh & 1) == 0 };
            AssignZone(db, d);
            Doors.Add(db);
        }
    }

    private void AssignZone(DoorBody db, Door d)
    {
        db.Zone = AccessZone.Open; db.Lock = LockKind.None; db.Inner = -1;
        if (d.IsExternal) return;
        foreach (var r in new[] { d.RoomA, d.RoomB })
        {
            if (r == null || r.Kind == RoomType.Corridor) continue;
            var (z, l) = r.Kind == RoomType.Reactor ? (AccessZone.Reactor, LockKind.Card)
                : r.Kind == RoomType.Security ? (AccessZone.Security, LockKind.Captain)
                : CabinKind(r.Kind) && Beds(r) is >= 1 and <= 2 ? (AccessZone.Cabin, LockKind.Key)
                : r.Kind == RoomType.Storage && r.Doors.Any(o => (o.RoomA == r ? o.RoomB : o.RoomA)?.Kind is RoomType.Medbay or RoomType.Triage) ? (AccessZone.Medicine, LockKind.Card)
                : (AccessZone.Open, LockKind.None);
            if (z == AccessZone.Open) continue;
            db.Zone = z; db.Lock = l; db.Inner = r.Id;
            return;
        }
    }

    /// <summary>방 하나를 출입 통제 구역으로 (개조 · 시험).</summary>
    public void SetZone(Room room, AccessZone zone, LockKind lk)
    {
        foreach (var d in room.Doors)
            if (DoorOf(d) is DoorBody db && !d.IsExternal) { db.Zone = zone; db.Lock = lk; db.Inner = room.Id; }
    }

    /// <summary>선실 주인 (그 방 침대의 주인).</summary>
    public List<CrewMember> Owners(DoorBody db)
    {
        var list = new List<CrewMember>();
        if (db.Inner < 0 || db.Inner >= _w.Ship.Rooms.Count) return list;
        foreach (var f in _w.Ship.Rooms[db.Inner].Furniture)
            if (f.Type == FurnitureType.Bed && f.Owner is CrewMember o && !o.Dead && !list.Contains(o)) list.Add(o);
        list.Sort((a, b) => a.Id.CompareTo(b.Id));
        return list;
    }

    /// <summary>출입 권한 표.</summary>
    public bool Allowed(CrewMember c, DoorBody db)
    {
        bool captain = c.Id == _w.Command.CaptainId;
        return db.Zone switch
        {
            AccessZone.Open => true,
            AccessZone.Medicine => c.Role == CrewRole.Medic || captain,
            AccessZone.Reactor => c.Role is CrewRole.Engineer or CrewRole.Technician or CrewRole.Electrician || captain,
            AccessZone.Captain => captain || Owners(db).Contains(c),
            AccessZone.Security => captain,
            AccessZone.Cabin => Owners(db).Contains(c),
            _ => true,
        };
    }

    /// <summary>지금 잠겨 있나 (화재 해제 방침 · 정전이면 전자 잠금이 풀린다 · 선실은 주인이 잠갔을 때만).</summary>
    public bool Engaged(DoorBody db, Door d)
    {
        if (db.Lock == LockKind.None || db.Zone == AccessZone.Open) return false;
        if (_fireRelease) return false;
        if (db.Lock is LockKind.Card or LockKind.Finger or LockKind.Captain && !d.Powered && !d.MotorBroken) return false; // 전자 잠금: 정전이면 사람 먼저 (풀린다)
        if (db.Zone == AccessZone.Cabin) return db.OwnerLocked;
        return true;
    }

    private bool HasPass(CrewMember c, DoorBody db) => db.Pass == c.Id && _w.Tick < db.PassUntil;

    // ───────────────────────────── 문 앞에서 (한 걸음마다) ─────────────────────────────

    private float Gate(CrewMember c, Door d, Room? beyond, bool urgent)
    {
        var w = _w;
        long now = w.Tick;
        var db = DoorOf(d);
        if (db == null) return 1f;
        bool entering = beyond != null && db.Inner == beyond.Id && c.Room?.Id != db.Inner && db.Zone != AccessZone.Open;
        if (entering)
        {
            bool pass = HasPass(c, db) || Allowed(c, db);
            if (!pass && urgent)
            {
                pass = true; // 급한 일: 비상 해제 손잡이
                if (Engaged(db, d) && now - db.EmergencyNoted > SimTime.Hours(1))
                {
                    db.EmergencyNoted = now;
                    Stats.EmergencyPass++;
                    w.Log.Add(now, LogKind.Warning, $"{DoorBody.ZoneName(db.Zone)} 문을 비상 해제하고 들어갔다", c.Id);
                }
            }
            if (!pass)
            {
                if (_lockedOut.TryGetValue(c.Id, out var lo) && lo.door == d.Id && now < lo.until) { GiveUp(c, db, null); return 0f; }
                if (db.Zone is AccessZone.Cabin or AccessZone.Captain) return Knock(c, d, db, Engaged(db, d));
                if (Engaged(db, d)) return Blocked(c, d, db);
            }
            else if (db.Caller == c.Id) ClearCall(db);
        }

        // 정전 · 구동기 고장: 손으로 돌려 연다 (시간이 더 걸린다)
        if (!d.Powered && d.Openness < 0.8f && !d.Locked && !d.JammedOpen && !d.Blocked)
        {
            d.Request();
            c.Pose = Pose.Working;
            Locomotion.Face(c, d.Cell.Center);
            if (_crankDoor[c.Id] != d.Id)
            {
                _crankDoor[c.Id] = d.Id;
                Stats.Cranks++;
                if (c.SaidUntil < now) c.Say(w, Persona.Say(c, d.MotorBroken ? "구동기가 죽었네 — 손으로 돌린다" : "정전이다 — 문을 손으로 돌려 연다"));
            }
            return 0f;
        }
        if (c.Pose == Pose.Working && _crankDoor[c.Id] == d.Id) c.Pose = Pose.Walking;

        // 센서가 먹통: 서서 버튼을 누른다
        if (db.SensorBroken && d.Powered && d.Openness < 0.05f && _sensorDoor[c.Id] != d.Id)
        {
            _sensorDoor[c.Id] = d.Id;
            _holdUntil[c.Id] = now + 3;
            Stats.SensorPresses++;
            Locomotion.Face(c, d.Cell.Center);
            if (c.SaidUntil < now) c.Say(w, Persona.Say(c, "센서가 먹통이네 — 버튼을 누른다"));
            return 0f;
        }
        return 1f;
    }

    /// <summary>선실: 주인이 안에 있으면 노크하고 대답을 기다린다 (자는 사람은 대답이 없다) · 열어 둔 문은 들러도 된다.</summary>
    private float Knock(CrewMember c, Door d, DoorBody db, bool locked)
    {
        var w = _w;
        long now = w.Tick;
        if (HasPass(c, db)) return 1f;
        if (!locked && (db.HeldOpen || d.Openness >= 0.5f))
        {
            if (_dropDoor[c.Id] != d.Id) { _dropDoor[c.Id] = d.Id; Stats.DropIns++; }
            return 1f; // 열어 둔 선실 문: "들러도 된다"
        }
        var inside = Owners(db).Where(o => o.Room?.Id == db.Inner).ToList();
        if (inside.Count == 0)
        {
            if (!locked) return 1f; // 빈 선실 (잠그지 않았다)
            if (c.SaidUntil < now) c.Say(w, Persona.Say(c, $"{DoorBody.ZoneName(db.Zone)} 문이 잠겼네 — 나중에"));
            GiveUp(c, db, $"{DoorBody.ZoneName(db.Zone)} 문이 잠겨 있어 돌아섰다");
            return 0f;
        }
        c.Pose = Pose.Standing;
        Locomotion.Face(c, d.Cell.Center);
        if (db.Knocker != c.Id || now - db.KnockAt > SimTime.Minutes(3))
        {
            db.Knocker = c.Id; db.KnockAt = now;
            Stats.Knocks++;
            c.Say(w, Persona.Say(c, $"똑똑 — {inside[0].Name}, 있어?"));
            return 0f;
        }
        if (now - db.KnockAt < 5) return 0f;
        var awake = inside.FirstOrDefault(o => o.IsAwake && o.CanAct);
        if (awake != null)
        {
            Stats.Answered++;
            awake.Say(w, Persona.Say(awake, locked ? "잠깐만 — 지금 열게" : "들어와"));
            db.Pass = c.Id; db.PassUntil = now + SimTime.Minutes(5);
            db.Knocker = -1;
            if (locked) db.OwnerLocked = false;
            return 1f;
        }
        Stats.NoAnswer++;
        if (c.SaidUntil <= now + SimTime.Minutes(3)) c.Say(w, Persona.Say(c, "자나 보다 — 나중에 와야지"));
        GiveUp(c, db, $"{Ko.IGa(inside[0].Name)} 자는지 선실 문을 두드려도 대답이 없었다");
        db.Knocker = -1;
        return 0f;
    }

    /// <summary>잠긴 문 앞: 기다리다가 권한 있는 사람을 부른다 (함장 승인은 무전으로) — 아무도 안 오면 돌아선다.</summary>
    private float Blocked(CrewMember c, Door d, DoorBody db)
    {
        var w = _w;
        long now = w.Tick;
        string zone = DoorBody.ZoneName(db.Zone);
        c.Pose = Pose.Standing;
        Locomotion.Face(c, d.Cell.Center);
        if (db.Caller != c.Id)
        {
            if (db.Caller >= 0 && CrewById(db.Caller) is CrewMember other && other.CanAct && now - db.WaitFrom < SimTime.Minutes(15) && (other.Position - d.Cell.Center).LengthSquared() < 9f)
            {
                // 먼저 온 사람이 이미 불렀다 — 같이 기다린다
                if (now - db.WaitFrom > SimTime.Minutes(15)) GiveUp(c, db, null);
                return 0f;
            }
            db.Caller = c.Id; db.WaitFrom = now; db.Helper = -1; db.Remote = false; db.Tried.Clear();
            Stats.Waits++;
            if (c.SaidUntil < now) c.Say(w, Persona.Say(c, $"{zone} 문이 잠겼네 ({DoorBody.LockName(db.Lock)})"));
            return 0f;
        }
        long waited = now - db.WaitFrom;
        if (db.Helper < 0 && waited >= 3)
        {
            var h = FindHelper(c, d, db);
            if (h != null)
            {
                db.Helper = h.Id; db.CalledAt = now; db.Tried.Add(h.Id);
                db.Remote = db.Lock == LockKind.Captain;
                db.RemoteAt = now + (db.Remote ? R.Range(8, 25) : 0);
                Stats.Calls++;
                c.Say(w, Persona.Say(c, db.Remote ? $"함장님, {zone} 출입 승인 부탁드립니다" : $"{h.Name}, {zone} 좀 열어 줄래요?"));
                w.Log.Add(now, LogKind.Life, $"잠긴 {zone} 문 앞에서 기다리다 {Ko.EulReul(h.Name)} 불렀다 ({DoorBody.LockName(db.Lock)})", c.Id);
                if (!db.Remote) h.NextThinkTick = now;
            }
            else if (waited > SimTime.Minutes(4)) { GiveUp(c, db, $"잠긴 {zone} 문 앞에서 기다렸지만 열어 줄 사람이 없어 돌아섰다"); return 0f; }
        }
        if (db.Helper >= 0)
        {
            var h = CrewById(db.Helper);
            if (db.Remote)
            {
                if (h != null && h.CanAct && h.IsAwake && now >= db.RemoteAt) Grant(c, db, h, remote: true);
                else if (h == null || !h.IsAwake) db.Helper = -1;
            }
            else if (h == null || !h.CanAct || !h.IsAwake || now - db.CalledAt > SimTime.Minutes(3) && h.Job?.Activity is not OpenDoorActivity)
                db.Helper = -1; // 못 온다 — 다른 사람을
        }
        if (waited > SimTime.Minutes(15)) GiveUp(c, db, $"잠긴 {zone} 문 앞에서 한참 기다리다 돌아섰다");
        return 0f;
    }

    private CrewMember? FindHelper(CrewMember c, Door d, DoorBody db)
    {
        var w = _w;
        if (db.Lock == LockKind.Captain)
            return w.Command.Captain is CrewMember cap && cap != c && cap.CanAct && cap.IsAwake && !db.Tried.Contains(cap.Id) ? cap : null;
        CrewMember? best = null;
        float bestD = float.MaxValue;
        foreach (var x in w.Crew)
        {
            if (x == c || x.Dead || !x.CanAct || !x.IsAwake || x.Outside || x.IsChild || x.Job?.Urgent == true || db.Tried.Contains(x.Id) || !Allowed(x, db)) continue;
            float dd = (x.Position - d.Cell.Center).LengthSquared();
            if (dd < bestD) { bestD = dd; best = x; }
        }
        return best;
    }

    private void Grant(CrewMember caller, DoorBody db, CrewMember helper, bool remote)
    {
        var w = _w;
        string zone = DoorBody.ZoneName(db.Zone);
        db.Pass = caller.Id; db.PassUntil = w.Tick + SimTime.Minutes(5);
        if (remote) Stats.RemoteOk++; else Stats.LetIn++;
        w.Log.Add(w.Tick, LogKind.Life, remote ? $"함장이 무전으로 {zone} 출입을 승인했다 — {caller.Name}" : $"{Ko.IGa(helper.Name)} {zone} 문을 열어 줬다 — {caller.Name}", helper.Id);
        if (!remote) helper.Say(w, Persona.Say(helper, "자, 들어가"));
        caller.Say(w, Persona.Say(caller, "고마워요!"));
        ClearCall(db);
    }

    private static void ClearCall(DoorBody db) { db.Caller = -1; db.Helper = -1; db.WaitFrom = -1; db.Remote = false; db.Tried.Clear(); }

    private void GiveUp(CrewMember c, DoorBody db, string? why)
    {
        var w = _w;
        c.PathBlocked = true;
        if (db.Caller == c.Id) ClearCall(db);
        _lockedOut[c.Id] = (db.Door, w.Tick + SimTime.Hours(1));
        if (why == null) return;
        Stats.GaveUp++;
        w.Log.Add(w.Tick, LogKind.Life, why, c.Id);
    }

    /// <summary>나를 부른 문 (열어 주러 간다).</summary>
    public DoorBody? CallFor(CrewMember c) => Doors.FirstOrDefault(db => db.Helper == c.Id && !db.Remote && db.Caller >= 0);

    internal bool LetIn(CrewMember helper, DoorBody db)
    {
        if (db.Caller < 0 || db.Helper != helper.Id) return true;
        if (CrewById(db.Caller) is CrewMember caller) Grant(caller, db, helper, remote: false);
        return true;
    }

    /// <summary>문의 바깥쪽(통제 구역이 아닌 쪽) 칸.</summary>
    public Cell? OuterCell(Door d, DoorBody db)
    {
        var dirs = d.ConnectsVertically ? new[] { new Cell(0, -1), new Cell(0, 1) } : new[] { new Cell(-1, 0), new Cell(1, 0) };
        foreach (var dir in dirs)
        {
            var p = d.Cell + dir;
            if (_w.Ship.RoomAt(p) is Room r && r.Id != db.Inner && _w.Ship.IsWalkable(p)) return p;
        }
        return null;
    }

    /// <summary>문 너머 표시판 (Movement가 읽는다): 고장이면 멈춘 값을 보여 준다 — 오판.</summary>
    public string? Reading(Door d, string? real)
    {
        var db = DoorOf(d);
        if (db == null || !db.IndicatorBroken) return real;
        string? shown = db.IndicatorSaysSafe ? null : "진공 표시";
        if ((shown == null) != (real == null)) Stats.FalseReadings++;
        return shown;
    }

    // ───────────────────────────── 문 상태 (시스템 틱) ─────────────────────────────

    private bool LocksAtNight(CrewMember o) => o.Traits.Calm < 0.42f || o.Memory.Trauma > 0.3f || unchecked((uint)(o.Id * 2654435761u + (uint)_w.Seed)) % 3u == 0u;

    private void UpdateDoors(float dt)
    {
        var w = _w;
        var ship = w.Ship;
        long now = w.Tick;
        EnsureDoors();
        bool fire = w.Policies["doorfire"] == 0 && w.Fire.Count > 0;
        if (fire && !_fireRelease)
        {
            Stats.FireReleases++;
            if (Doors.Any(x => x.Lock != LockKind.None)) w.Log.Add(now, LogKind.Warning, "화재 — 방침대로 출입 통제 문을 모두 풀었다");
        }
        _fireRelease = fire;
        bool slow = now >= _nextSlow;
        foreach (var db in Doors)
        {
            var d = ship.Doors[db.Door];
            if (d.Removed) continue;
            if (db.Pass >= 0 && now >= db.PassUntil) db.Pass = -1;
            if (db.Caller >= 0 && (CrewById(db.Caller) is not CrewMember cl || !cl.CanAct || now - db.WaitFrom > SimTime.Minutes(20))) ClearCall(db);

            // 선실: 자는 주인은 잠그고 (성격), 깨어 있는 붙임성 좋은 주인은 문을 열어 둔다 ("들러도 된다")
            if (db.Zone is AccessZone.Cabin or AccessZone.Captain && db.Inner >= 0)
            {
                var owners = Owners(db);
                if (slow && db.Zone != AccessZone.Medicine)
                {
                    bool cap = owners.Count == 1 && owners[0].Id == w.Command.CaptainId;
                    if (cap && db.Zone == AccessZone.Cabin) { db.Zone = AccessZone.Captain; db.Lock = LockKind.Finger; }
                    else if (!cap && db.Zone == AccessZone.Captain) { db.Zone = AccessZone.Cabin; db.Lock = LockKind.Key; }
                }
                bool sleeping = false, locks = false;
                CrewMember? host = null;
                foreach (var o in owners)
                {
                    if (o.Room?.Id != db.Inner) continue;
                    if (o.Pose == Pose.Sleeping) { sleeping = true; if (LocksAtNight(o)) locks = true; }
                    else if (host == null && o.IsAwake && o.Traits.Sociability > 0.62f) host = o;
                }
                if (locks && !db.OwnerLocked) Stats.LockedSleeps++;
                db.OwnerLocked = locks || db.Zone == AccessZone.Captain && !sleeping && host == null && owners.Count > 0 && owners[0].Room?.Id != db.Inner;
                bool hold = host != null && !sleeping && now >= db.NoHoldUntil;
                if (hold && !db.HeldOpen) { Stats.HeldOpens++; db.HeldBy = host!.Id; }
                db.HeldOpen = hold;
                d.HoldOpen = hold;
            }

            // 패킹: 여닫을수록 · 시간이 갈수록 삭는다
            int cyc = d.Cycles - db.LastCycles;
            db.LastCycles = d.Cycles;
            db.Gasket = MathF.Max(0f, db.Gasket - cyc * 0.0002f - dt * 0.0004f);
            if (cyc > 0 && R.Chance(0.0002f * cyc)) db.SensorBroken = true;

            // 휜 문틀: 격벽이 끝까지 닫히지 않는다 → 기밀 실패
            if (d.Bent > 0.3f && d.Locked && !db.SealFailNoted)
            {
                db.SealFailNoted = true;
                Stats.SealFails++;
                w.Log.Add(now, LogKind.Warning, $"{d.RoomA?.Name ?? "?"}·{d.RoomB?.Name ?? "?"} 사이 문이 끝까지 닫히지 않는다 — 문틀이 휘어 기밀 실패");
            }
            if (d.Bent <= 0.3f) db.SealFailNoted = false;

            // 삭은 패킹: 닫힌 문으로 공기가 조금씩 새고, 압력 차가 크면 휘파람 소리
            var ra = d.RoomA; var rb = d.RoomB;
            if (db.Gasket < 0.25f && d.Openness < 0.05f && ra != null && rb != null && !ra.Detached && !rb.Detached)
            {
                float dp = MathF.Abs(ra.Air.Pressure - rb.Air.Pressure);
                if (dp > 2f) { Seep(ra, rb, (0.25f - db.Gasket) * 0.6f * dt); Stats.MicroLeaks++; }
                bool wh = dp > 6f;
                if (wh)
                {
                    ra.Noise = MathF.Min(1f, ra.Noise + 0.08f);
                    rb.Noise = MathF.Min(1f, rb.Noise + 0.08f);
                    if (!db.Whistling)
                    {
                        Stats.Whistles++;
                        w.Log.Add(now, LogKind.Warning, $"{ra.Name}·{rb.Name} 사이 문 패킹이 삭아 바람이 샌다 — 휘파람 소리");
                    }
                }
                db.Whistling = wh;
            }
            else if (d.Openness >= 0.05f || db.Gasket >= 0.25f) db.Whistling = false;

            // 열어 둔 문: 냉기 · 소음 · 냄새가 샌다 → "문 좀 닫아!"
            if (d.Openness >= 0.5f) { if (db.OpenSince < 0) db.OpenSince = now; }
            else db.OpenSince = -1;
            if (db.OpenSince >= 0 && now - db.OpenSince > SimTime.Minutes(10) && now - db.ComplainedAt > SimTime.Hours(1) && ra != null && rb != null) Complain(d, db, ra, rb);

            // 옆 벽이 찌그러졌다 → 문틀이 휜다
            if (slow && d.Bent < 0.5f)
            {
                var perp = d.ConnectsVertically ? new[] { new Cell(1, 0), new Cell(-1, 0) } : new[] { new Cell(0, 1), new Cell(0, -1) };
                foreach (var p in perp)
                    if (ship.WallAt(d.Cell + p) is WallState ws && ws.Integrity < 0.6f)
                    {
                        d.Bent = 0.6f;
                        Stats.Bents++;
                        w.Log.Add(now, LogKind.Warning, $"충격에 {d.RoomA?.Name ?? "?"}·{d.RoomB?.Name ?? "?"} 사이 문틀이 휘었다 — 끝까지 닫히지 않는다");
                        break;
                    }
            }
        }
        if (slow)
        {
            var stale = _lockedOut.Where(kv => kv.Value.until <= now).Select(kv => kv.Key).ToList();
            foreach (var k in stale) _lockedOut.Remove(k);
        }
    }

    /// <summary>닫힌 문 틈으로 기체가 조금씩 섞인다 (부피를 따져 총량을 지킨다).</summary>
    private static void Seep(Room a, Room b, float k)
    {
        k = Math.Clamp(k, 0f, 0.5f);
        float va = MathF.Max(1f, a.Volume), vb = MathF.Max(1f, b.Volume);
        static void Mix(ref float ga, ref float gb, float va, float vb, float k)
        {
            float eq = (ga * va + gb * vb) / (va + vb);
            float moveA = (eq - ga) * k;
            ga += moveA;
            gb -= moveA * va / vb;
        }
        float o2a = a.Air.O2, o2b = b.Air.O2, n2a = a.Air.N2, n2b = b.Air.N2, ca = a.Air.CO2, cb = b.Air.CO2;
        Mix(ref o2a, ref o2b, va, vb, k); Mix(ref n2a, ref n2b, va, vb, k); Mix(ref ca, ref cb, va, vb, k);
        a.Air.O2 = MathF.Max(0f, o2a); b.Air.O2 = MathF.Max(0f, o2b);
        a.Air.N2 = MathF.Max(0f, n2a); b.Air.N2 = MathF.Max(0f, n2b);
        a.Air.CO2 = MathF.Max(0f, ca); b.Air.CO2 = MathF.Max(0f, cb);
    }

    private void Complain(Door d, DoorBody db, Room ra, Room rb)
    {
        var w = _w;
        float dT = MathF.Abs(ra.Air.Temperature - rb.Air.Temperature), dN = MathF.Abs(ra.Noise - rb.Noise), dS = MathF.Abs(ra.Smell - rb.Smell);
        if (dT < 4f && dN < 0.25f && dS < 0.25f) return;
        Room victim = dT >= 4f ? (ra.Air.Temperature > rb.Air.Temperature ? ra : rb) : dN >= 0.25f ? (ra.Noise < rb.Noise ? ra : rb) : (ra.Smell < rb.Smell ? ra : rb);
        Room source = victim == ra ? rb : ra;
        CrewMember? who = null;
        foreach (var x in w.Crew)
            if (!x.Dead && x.CanAct && x.IsAwake && x.Room == victim && x.Job?.Urgent != true && !x.IsChild) { who = x; break; }
        if (who == null) return;
        db.ComplainedAt = w.Tick;
        Stats.Complaints++;
        string why = dT >= 4f ? "찬바람" : dN >= 0.25f ? "소음" : "냄새";
        who.Say(w, Persona.Say(who, $"문 좀 닫아! {why}이 들어오잖아"));
        w.Log.Add(w.Tick, LogKind.Life, $"\"문 좀 닫아!\" — {source.Name} 쪽 {why}이(가) {victim.Name}(으)로 샌다", who.Id);
        if (db.HeldOpen && CrewById(db.HeldBy) is CrewMember holder && holder != who)
        {
            who.ChangeAffinity(holder, -0.04f);
            holder.ChangeAffinity(who, -0.02f);
        }
        db.HeldOpen = false;
        d.HoldOpen = false;
        db.NoHoldUntil = w.Tick + SimTime.Hours(2);
    }

    // ───────────────────────────── 얇은 칸막이 · 엿듣기 ─────────────────────────────

    private void BuildThin()
    {
        var ship = _w.Ship;
        var map = new Dictionary<int, List<(int other, List<WallBody> walls)>>();
        void Add(int a, int b, WallBody wb)
        {
            if (!map.TryGetValue(a, out var list)) map[a] = list = new();
            int k = list.FindIndex(x => x.other == b);
            if (k < 0) list.Add((b, new List<WallBody> { wb }));
            else list[k].walls.Add(wb);
        }
        foreach (var wb in WallList)
        {
            if (!wb.Thin) continue;
            Room? a = null, b = null;
            foreach (var d in Cell.Dirs4)
            {
                var r = ship.RoomAt(wb.Cell + d);
                if (r == null) continue;
                if (a == null) a = r; else if (r != a) b = r;
            }
            if (a == null || b == null) continue;
            Add(a.Id, b.Id, wb);
            Add(b.Id, a.Id, wb);
        }
        _thin = map;
    }

    /// <summary>이 방과 얇은 칸막이로 붙은 방들.</summary>
    public IEnumerable<int> ThinNeighbors(Room r) => _thin.TryGetValue(r.Id, out var l) ? l.Select(x => x.other) : Enumerable.Empty<int>();

    private void ScanTalk()
    {
        var w = _w;
        if (_thin.Count == 0) return;
        foreach (var a in w.Crew)
        {
            if (a.TalkingTo is not CrewMember b || a.Id > b.Id || a.Dead || b.Dead || a.Room is not Room r || b.Room != r) continue;
            if (!_thin.TryGetValue(r.Id, out var list)) continue;
            foreach (var (other, walls) in list)
            foreach (var l in w.Crew)
            {
                if (l == a || l == b || l.Dead || !l.IsAwake || l.Outside || l.Room?.Id != other) continue;
                float best = float.MaxValue;
                WallBody? via = null;
                foreach (var wb in walls)
                {
                    float d2 = (l.Position - wb.Cell.Center).LengthSquared();
                    if (d2 < best) { best = d2; via = wb; }
                }
                if (via == null || best > 12.25f) continue;
                float leak = MathF.Max(0f, 0.6f - via.SoundBlock) / 0.35f;
                float noise = 1f - 0.6f * Math.Clamp(w.Ship.Rooms[other].Noise, 0f, 1f);
                if (leak <= 0f || !R.Chance(0.06f * leak * noise)) continue;
                Overhear(l, a, b, r);
            }
        }
    }

    private void Overhear(CrewMember l, CrewMember a, CrewMember b, Room room)
    {
        var w = _w;
        long now = w.Tick;
        var key = (l.Id, a.Id, b.Id);
        if (_heardAt.TryGetValue(key, out var t) && now - t < SimTime.Hours(2)) return;
        _heardAt[key] = now;
        string what;
        if (w.Relations.OverheardRumor(a, l) || w.Relations.OverheardRumor(b, l)) what = "고장 기미 이야기";
        else if (w.Relations.All.LastOrDefault(m => (m.Who == a.Id || m.Who == b.Id) && m.About == l.Id) is RelationMemory me)
        {
            // 내 이야기를 하더라 — 들은 대로 마음이 기운다
            if (CrewById(me.Who) is CrewMember sp) l.ChangeAffinity(sp, me.Weight > 0f ? 0.05f : -0.08f);
            what = me.Weight > 0f ? $"{Ko.IGa(CrewById(me.Who)?.Name ?? "?")} 내 칭찬을 하더라" : $"{Ko.IGa(CrewById(me.Who)?.Name ?? "?")} 내 흉을 보더라";
        }
        else if (w.Relations.All.LastOrDefault(m => (m.Who == a.Id || m.Who == b.Id) && m.Weight < 0f) is RelationMemory g)
            what = $"{Ko.IGa(CrewById(g.Who)?.Name ?? "?")} {CrewById(g.About)?.Name ?? "?"} 이야기를 하더라 — {g.Text}";
        else what = "사는 이야기";
        Heard.Add(new HeardMemory { Listener = l.Id, A = a.Id, B = b.Id, FromRoom = room.Id, Tick = now, What = what });
        if (Heard.Count > 400) Heard.RemoveAt(0);
        Stats.Overheard++;
        if (l.SaidUntil < now) l.Say(w, Persona.Say(l, "(칸막이 너머로 다 들리네…)"));
        w.Log.Add(now, LogKind.Life, $"{room.Name}의 {Ko.WaGwa(a.Name)} {b.Name} 이야기가 칸막이 너머로 들렸다 — {what}", l.Id);
    }
}

public sealed partial class RelationSystem
{
    /// <summary>v16.3 칸막이 너머로 엿들은 고장 이야기: 소문 출처가 "엿들음"으로 남는다 (판단 없이 관측만).</summary>
    internal bool OverheardRumor(CrewMember speaker, CrewMember listener)
    {
        var w = _w;
        var n = w.Watch.OpenNotes.FirstOrDefault(x => x.Holders.ContainsKey(speaker.Id) && !x.Holders.ContainsKey(listener.Id));
        if (n == null) return false;
        Stats.Rumors++;
        n.Holders[listener.Id] = false;
        n.Trail.Add($"{SimTime.Clock(w.Tick)} {Ko.IGa(listener.Name)} {speaker.Name}의 말을 칸막이 너머로 엿들음 (관측만)");
        return true;
    }
}

/// <summary>v16.3 잠긴 문 앞에서 부른 사람에게 가서 문을 열어 준다 (카드 · 지문 · 열쇠).</summary>
public sealed class OpenDoorActivity : Activity
{
    public override string Id => "open-door";
    public override string Label => "문 열어 주기";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (w.Body.CallFor(c) is not DoorBody db) return (0f, "—");
        var caller = w.Crew.FirstOrDefault(x => x.Id == db.Caller);
        return (0.95f, $"{Ko.IGa(caller?.Name ?? "누군가")} {DoorBody.ZoneName(db.Zone)} 문 앞에서 부른다");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (w.Body.CallFor(c) is not DoorBody db) return null;
        var d = w.Ship.Doors[db.Door];
        if (w.Body.OuterCell(d, db) is not Cell spot || dist.Get(spot) < 0) return null;
        var toils = new List<Toil>
        {
            new GotoToil(spot),
            new WaitToil(3, Pose.Working, d.Cell.Center), // 카드를 대고 · 손가락을 대고 · 열쇠를 돌린다
            new DoToil((cm, world) => world.Body.LetIn(cm, db)),
        };
        return new Job(this, "문 열어 주기", toils) { LogText = $"{DoorBody.ZoneName(db.Zone)} 문을 열어 주러 간다" };
    }
}
