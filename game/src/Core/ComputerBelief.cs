using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.6 컴퓨터가 믿는 배: 감지기 · 문 감지기 · 데이터선으로 만든 배 모형 (방별 사람 수 · 불 · 기압 · 산소 + 마지막 갱신 시각).
// 세계 ≠ 컴퓨터가 아는 것: 데이터선이 끊기거나 방에 전기가 없으면 값이 멈추고(낡은 믿음), 감지기가 틀어지면 틀린 믿음이 된다.
//   문 감지기 틀어짐(Blind) — 드나든 사람을 못 세 사람이 없다고 믿는다 · 값 멈춤(Stuck) — 방사선 데이터 손상 · 헛불(Ghost) — 업데이트 버그 · 오경보.
// 컴퓨터의 조치는 이 믿음으로 한다 (소화 수순의 "안에 누가 있나"). 오경보가 잦은 감지기는 덜 믿는다 (사람 확인을 먼저 부른다).

public enum SensorFault { None, Blind, Stuck, Ghost }

public sealed class RoomBelief
{
    public int People { get; set; }
    public bool Fire { get; set; }
    public float Pressure { get; set; } = 101f;
    public float O2 { get; set; } = 21f;
    public long Updated { get; set; } = -1;
    public SensorFault Fault { get; set; }
    public long FaultSince { get; set; } = -1;
    public string FaultWhy { get; set; } = "";
    /// <summary>감지기를 얼마나 믿나 (오경보·틀린 값이 드러날 때마다 준다 · 하루에 조금씩 회복).</summary>
    public float Trust { get; set; } = 1f;
    public int FalseAlarms { get; set; }
    public int Misreads { get; set; }
}

public sealed class BeliefModel
{
    private readonly World _w;
    private readonly List<RoomBelief> _rooms = new();
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7919 + 211));
    private long _faultNext;
    public int Corruptions, Blinds, Ghosts, Repairs;

    public BeliefModel(World w) => _w = w;

    public RoomBelief Of(Room r)
    {
        while (_rooms.Count <= r.Id) _rooms.Add(new RoomBelief());
        return _rooms[r.Id];
    }

    /// <summary>감지기가 값을 보내오는 방 (데이터선 또는 통신 중계 · 주 컴퓨터).</summary>
    public bool Reading(Room r) => !r.Detached && (r.DataLinked || ComputerV15.Relay(_w)) && _w.Automation.MainOnline; // 문 감지기는 데이터선 전원으로 돈다

    /// <summary>컴퓨터가 아는 그 방 사람 수 — 모르면 null (데이터선이 끊겼고 생체 감시도 없다 · 감지기를 못 믿는다).</summary>
    public int? PeopleIn(Room r)
    {
        var b = Of(r);
        if (!r.DataLinked && !_w.Automation.Has(ComputerModule.BioMonitor)) return null;
        if (b.Trust < 0.5f) return null; // 오경보·틀린 값이 잦은 문 감지기 — 사람이 가서 보기 전엔 모른다
        if (b.Updated < 0) return null;
        return b.People;
    }

    /// <summary>쓰러진 사람 경보: 문 감지기가 그 방 사람을 볼 수 있나 (틀어지면 못 본다).</summary>
    public bool SeesPeople(Room r) => Of(r).Fault != SensorFault.Blind;

    public static int Actual(World w, Room r)
    {
        int n = 0;
        foreach (var c in w.Crew) if (!c.Dead && c.Room == r && !c.Outside) n++;
        return n;
    }

    /// <summary>믿음과 실제가 다른가 (사람 수 · 불 · 기압 15kPa · 산소 3kPa).</summary>
    public bool Diverged(Room r, out string why)
    {
        var b = Of(r);
        var w = _w;
        why = "";
        if (r.Detached || b.Updated < 0) return false;
        int actual = Actual(w, r);
        bool fire = w.Fire.CountIn(r) > 0;
        var parts = new List<string>();
        if (b.People != actual) parts.Add($"사람 {b.People} ↔ 실제 {actual}");
        if (b.Fire != fire) parts.Add(b.Fire ? "불 있다고 믿음 ↔ 없음" : "불 모름 ↔ 타는 중");
        if (MathF.Abs(b.Pressure - r.Air.Pressure) > 15f) parts.Add($"기압 {b.Pressure:0} ↔ {r.Air.Pressure:0}");
        if (MathF.Abs(b.O2 - r.Air.O2) > 3f) parts.Add($"산소 {b.O2:0.0} ↔ {r.Air.O2:0.0}");
        why = string.Join(" · ", parts);
        return parts.Count > 0;
    }

    public int DivergedCount() => _w.Ship.LiveRooms.Count(r => Diverged(r, out _));

    public void Break(Room r, SensorFault f, string why)
    {
        var b = Of(r);
        b.Fault = f;
        b.FaultSince = _w.Tick;
        b.FaultWhy = why;
        if (f == SensorFault.Blind) Blinds++;
        else if (f == SensorFault.Ghost) Ghosts++;
        else if (f == SensorFault.Stuck) Corruptions++;
    }

    /// <summary>사람이 가서 확인했다 — 틀어진 감지기를 다시 맞춘다 (틀린 값이 드러난 만큼 덜 믿는다).</summary>
    public void Checked(Room r, CrewMember by, bool wrong)
    {
        var b = Of(r);
        if (wrong)
        {
            b.Misreads++;
            b.Trust = MathF.Max(0.2f, b.Trust - 0.25f);
        }
        if (b.Fault != SensorFault.None)
        {
            _w.Log.Add(_w.Tick, LogKind.Work, $"{r.Name} {FaultName(b.Fault)} 감지기를 다시 맞췄다 ({b.FaultWhy})", by.Id);
            b.Fault = SensorFault.None;
            b.FaultWhy = "";
            Repairs++;
        }
        Read(r, b);
    }

    public static string FaultName(SensorFault f) => f switch { SensorFault.Blind => "문 감지기 틀어짐", SensorFault.Stuck => "값 멈춤", SensorFault.Ghost => "헛불", _ => "정상" };

    private void Read(Room r, RoomBelief b)
    {
        var w = _w;
        if (b.Fault == SensorFault.Stuck && b.Updated >= 0) return; // 값이 깨져 그대로 (갱신 시각만 거짓으로 새롭다)
        b.People = b.Fault == SensorFault.Blind ? 0 : Actual(w, r);
        b.Fire = w.Fire.IsKnown(r) && w.Fire.CountIn(r) > 0 || b.Fault == SensorFault.Ghost;
        b.Pressure = r.Air.Pressure;
        b.O2 = r.Air.O2;
        b.Updated = w.Tick;
    }

    /// <summary>시스템 틱마다: 감지기가 값을 보내오는 방만 믿음을 갱신한다. 생체 감시는 데이터선이 끊겨도 사람 수를 안다.</summary>
    public void Update(float dt)
    {
        var w = _w;
        var a = w.Automation;
        bool bio = a.MainOnline && a.Has(ComputerModule.BioMonitor);
        bool lag = a.Load > 1f && w.Tick / World.SystemInterval % 4 != 0; // 연산이 넘치면 감지기를 네 번에 한 번만 읽는다 (믿음이 낡는다)
        foreach (var r in w.Ship.Rooms)
        {
            if (r.Detached) continue;
            var b = Of(r);
            if (Reading(r) && !lag) Read(r, b);
            else if (bio && b.Fault != SensorFault.Blind) b.People = Actual(w, r);
            if (b.Fault == SensorFault.Ghost && w.Tick - b.FaultSince > SimTime.Minutes(20)) { b.Fault = SensorFault.None; b.FaultWhy = ""; } // 헛불은 잠깐
        }
        if (w.Tick < _faultNext) return;
        _faultNext = w.Tick + SimTime.Hours(1);
        // 한 시간마다: 감지기 믿음이 조금씩 돌아온다 · 오래 틀어진 값은 교정 모듈이 잡는다
        foreach (var r in w.Ship.LiveRooms)
        {
            var b = Of(r);
            b.Trust = MathF.Min(1f, b.Trust + 0.02f);
            if (b.Fault is SensorFault.Stuck or SensorFault.Blind && a.Active(ComputerModule.AutoCalib) && r.DataLinked && w.Tick - b.FaultSince > SimTime.Hours(6))
            {
                w.Log.Add(w.Tick, LogKind.Ship, $"감지기 자동 교정 — {r.Name} {FaultName(b.Fault)}을 스스로 잡았다");
                b.Fault = SensorFault.None;
                Repairs++;
            }
        }
        // 감지기가 틀어지는 일: 교정이 많이 틀어진 설비가 있는 방의 문 감지기 (드물다) · 방사선 폭풍 (값이 깨진다)
        if (!a.MainOnline) return;
        foreach (var r in w.Ship.LiveRooms)
        {
            var b = Of(r);
            if (b.Fault != SensorFault.None || r.Type == RoomType.Corridor) continue;
            float cal = 1f; int n = 0;
            foreach (var f in r.Furniture) if (f.Machine is Machine m) { cal = MathF.Min(cal, m.SensorCal); n++; }
            if (n > 0 && cal < 0.45f && R.Chance(0.01f * ComputerV15.DriftMul(w, r))) Break(r, SensorFault.Blind, $"교정 {cal * 100:0}% 설비 옆 문 감지기");
        }
        if (w.Hazards.StormActive && R.Chance(0.25f))
        {
            var rooms = w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor && Of(r).Fault == SensorFault.None).ToList();
            if (rooms.Count > 0)
            {
                var r = R.Pick(rooms);
                Break(r, SensorFault.Stuck, "방사선 데이터 손상");
                w.Log.Add(w.Tick, LogKind.Warning, $"방사선 — {r.Name} 감지기 값이 깨졌다 (컴퓨터는 {SimTime.Clock(w.Tick)} 값을 그대로 믿는다)");
            }
        }
    }
}

public sealed partial class AutomationSystem
{
    private BeliefModel? _belief;
    /// <summary>v16.6 컴퓨터가 믿는 배.</summary>
    public BeliefModel Belief => _belief ??= new BeliefModel(_world);
}
