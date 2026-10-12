using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v23 주컴퓨터 대개편 2단계: 눈의 그물.
//   · 로봇의 눈: 무전이 닿는 로봇은 지나가는 방을 늘 본다 (사람 · 불 · 산소 · 기압 · 온도) → 주 컴퓨터 장부에 "로봇이 봄"으로 올린다.
//     감지기가 죽었거나 데이터선이 끊긴 방도 로봇이 지나가면 다시 안다.
//   · 대조: 로봇이 본 것과 감지기 값이 2분 30초 넘게 어긋나면 (헛불 · 사람이 있는데 0명 · 기압 · 산소 값 멈춤) 감지기를 의심하고 그 로봇이 그 자리에서 다시 맞춘다.
//   · 정찰: 주 컴퓨터가 20분 넘게 모르는 방 (감지기 값이 없고 아무도 안 지나간) 가운데 중요한 방은 가까운 한가한 로봇을 보낸다 (두 대까지 · 방마다 30분에 한 번).

public sealed class EyeStats
{
    public int Seen, Caught, Ghosts, Blinds, Stucks, Scouts, ScoutFound;
    public string Line() => $"로봇 눈 보고 {Seen} · 감지기 의심 {Caught} (헛불 {Ghosts} · 사람 못 셈 {Blinds} · 값 멈춤 {Stucks}) · 정찰 {Scouts} (가서 찾은 것 {ScoutFound})";
}

public sealed class EyeNet
{
    /// <summary>시험: 끄면 예전처럼 (경보가 났을 때만 로봇이 확인).</summary>
    public static bool On = true;
    private readonly World _w;
    public EyeStats Stats { get; } = new();
    /// <summary>방 → 어긋남을 처음 본 때 (1분 넘게 이어지면 감지기를 의심한다).</summary>
    private readonly Dictionary<int, long> _odd = new();
    /// <summary>방 → 이때까지 다시 정찰하지 않는다.</summary>
    private readonly Dictionary<int, long> _scoutCalm = new();
    /// <summary>정찰 중인 로봇 → 방.</summary>
    private readonly Dictionary<int, int> _scouts = new();
    private long _next, _nextScout;

    public EyeNet(World w) => _w = w;

    public int Scouting => _scouts.Count;

    public void Tick()
    {
        var w = _w;
        if (!On || w.Tick < _next) return;
        _next = w.Tick + 10;
        var a = w.Automation;
        if (!a.Present || !a.CoreOnline || !RobotsV21.General || FleetSystem.Off) return;
        var facts = a.Facts;
        var seen = new HashSet<int>();
        foreach (var r in w.Robots.Robots)
        {
            if (!r.Operational || r.Wrecked || r.Aboard != null || r.State is not (RobotState.Active or RobotState.Docked) || r.Room is not Room room || room.Detached) continue;
            if (!w.Relays.Reach(room) || !seen.Add(room.Id)) continue;
            facts.Saw(room, r);
            Stats.Seen++;
            Cross(room, r);
            if (_scouts.TryGetValue(r.Id, out var target) && target == room.Id) { _scouts.Remove(r.Id); }
        }
        // 정찰을 마쳤거나 다른 일로 빠진 로봇은 장부에서 지운다
        foreach (var id in _scouts.Keys.ToList())
        {
            var r = w.Robots.Robots.FirstOrDefault(x => x.Id == id);
            if (r == null || !r.Operational || r.CaseId >= 0 || r.Steps == null) _scouts.Remove(id);
        }
        if (w.Tick >= _nextScout) { _nextScout = w.Tick + SimTime.Minutes(2); Scout(); }
    }

    /// <summary>로봇이 본 것 ↔ 감지기 값. 1분 넘게 어긋나면 감지기를 의심하고 그 로봇이 다시 맞춘다.</summary>
    private void Cross(Room room, Robot r)
    {
        var w = _w;
        var a = w.Automation;
        var b = a.Belief.Of(room);
        if (!a.Belief.Reading(room) || b.Updated < 0) { _odd.Remove(room.Id); return; }
        int people = BeliefModel.Actual(w, room);
        bool fire = w.Fire.Count > 0 && w.Fire.CountIn(room) > 0;
        string? why = null; int kind = 0;
        if (b.Fire && !fire) { why = "감지기는 불이라는데 로봇 눈엔 불이 없다"; kind = 1; }
        else if (b.People == 0 && people > 0) { why = $"감지기는 0명이라는데 로봇 눈엔 {people}명"; kind = 2; }
        else if (MathF.Abs(b.Pressure - room.Air.Pressure) > 15f) { why = $"기압 감지기 {b.Pressure:0} ↔ 로봇이 잰 {room.Air.Pressure:0}kPa"; kind = 3; }
        else if (MathF.Abs(b.O2 - room.Air.O2) > 3f) { why = $"산소 감지기 {b.O2:0.0} ↔ 로봇이 잰 {room.Air.O2:0.0}kPa"; kind = 3; }
        if (why == null) { _odd.Remove(room.Id); return; }
        if (!_odd.TryGetValue(room.Id, out var since)) { _odd[room.Id] = w.Tick; return; }
        if (w.Tick - since < SimTime.Minutes(2.5f)) return; // 연산이 넘치면 감지기를 네 번에 한 번 읽는다 (1분 남짓 늦는 믿음은 고장이 아니다)
        _odd.Remove(room.Id);
        Stats.Caught++;
        if (kind == 1) Stats.Ghosts++; else if (kind == 2) Stats.Blinds++; else Stats.Stucks++;
        w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: {room.Name} 감지기가 {r.Name}의 눈과 계속 다르다 ({why}) — 감지기를 의심한다");
        if (kind == 3) { b.Misreads++; b.Trust = MathF.Max(0.2f, b.Trust - 0.25f); } // 값이 멈춘 감지기는 덜 믿는다 (헛불 · 사람 못 셈은 확인하며 깎는다)
        a.RobotSaw(room, r, why, kind == 1);
    }

    /// <summary>주 컴퓨터가 모르는 중요한 방에 가까운 한가한 로봇을 보낸다.</summary>
    private void Scout()
    {
        var w = _w;
        var a = w.Automation;
        if (!w.Commander.OpensDoors || _scouts.Count >= 2 || w.Commander.OpenCases.Any(k => k.Life)) return;
        var facts = a.Facts;
        foreach (var room in w.Ship.LiveRooms.OrderBy(r => r.Id))
        {
            if (_scouts.Count >= 2) break;
            if (room.Abandoned || room.OffLimits || room.Type == RoomType.Corridor || room.Air.Pressure < 40f) continue;
            if (_scoutCalm.TryGetValue(room.Id, out var calm) && w.Tick < calm) continue;
            if (!facts.Unknown(room, FactKind.Fire) || !facts.Unknown(room, FactKind.People)) continue;
            var last = facts.Get(room, FactKind.People);
            bool matters = last.Value > 0.5f || room.Type is RoomType.LifeSupport or RoomType.Medbay or RoomType.Bridge or RoomType.Reactor or RoomType.Galley or RoomType.Quarters;
            if (!matters) continue;
            _scoutCalm[room.Id] = w.Tick + SimTime.Minutes(30);
            string why = last.At < 0 ? "한 번도 소식이 없다" : $"{(w.Tick - last.At) / (float)SimTime.Minutes(1):0}분째 모른다";
            var r = w.Robots.Dispatch(room, $"정찰 — {room.Name} {why}");
            if (r == null) continue;
            _scouts[r.Id] = room.Id;
            Stats.Scouts++;
            if (w.Fire.CountIn(room) > 0 || BeliefModel.Actual(w, room) > 0) Stats.ScoutFound++;
            w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: {room.Name} — {why} · {Ko.EulReul(r.Name)} 정찰 보낸다");
        }
    }
}
