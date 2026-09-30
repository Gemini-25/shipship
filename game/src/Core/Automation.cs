using System;
using System.Linq;

namespace ShipSim.Core;

/// <summary>
/// 주 컴퓨터와 자동화 (v9.2).
///
/// 함교의 주 컴퓨터가 우주선의 "반사 신경"을 맡는다: 감압이면 격벽을 잠그고, 불·감압이면 환기 댐퍼를 닫고,
/// 감지기 경보를 온 배에 돌리고, 전기가 모자라면 우선순위대로 부하를 끊고, 드론을 관제하고, 냉각이 줄면 제어봉을 빨리 내린다.
///
/// 고리 (원칙 2): 컴퓨터는 전기와 **환기**(열을 빼 줘야 한다)가 있어야 돌고, 환기 댐퍼는 컴퓨터가 움직이고, 환기는 생명유지실(전기)이 돌린다.
/// 환기가 끊기면(댐퍼가 걸리거나 닫히거나, 생명유지실이 정전이거나, 함교가 감압되면) 함교가 달아올라 과열 정지한다.
/// 비상수단: 사람이 손으로 — 격벽을 돌려 닫고, 댐퍼를 열고 닫고, 불은 눈으로 보고, 회로는 사람이 내린다. 개조로 예비 제어기(격벽·댐퍼·경보만).
/// </summary>
public sealed class AutomationSystem
{
    /// <summary>이 온도를 넘으면 과열 정지할 수 있다 (℃).</summary>
    public const float OverheatC = 38f;

    /// <summary>과열 정지한 컴퓨터를 다시 켜려면 방이 이만큼 식어야 한다.</summary>
    public const float RestartC = 32f;

    private readonly World _world;
    public AutomationSystem(World world) => _world = world;

    private System.Collections.Generic.IEnumerable<Furniture> Bodies => _world.Ship.Furniture.Where(f => f.Type == FurnitureType.MainComputer);

    /// <summary>자동화를 맡는 컴퓨터: 배에 붙어 있는 것 중 가장 잘 도는 것 (함교 것을 잃으면 임시 제어 컴퓨터).</summary>
    public Furniture? ComputerBody => Bodies.OrderBy(f => f.Room.Detached ? 1 : 0).ThenByDescending(f => f.Machine!.Efficiency).ThenBy(f => f.Id).FirstOrDefault();
    public Machine? Computer => ComputerBody is Furniture f && !f.Room.Detached ? f.Machine : null;

    /// <summary>쓸 수 있는 컴퓨터가 아예 없다 (떨어져 나갔거나 부서졌거나 뜯겼다).</summary>
    public bool Gone => Present && !Bodies.Any(f => !f.Room.Detached && !f.Machine!.Has(FaultKind.Wrecked) && !f.Machine.Has(FaultKind.Stripped));
    public long GoneSince { get; private set; } = -1;

    /// <summary>주 컴퓨터가 돈다 (모든 자동화).</summary>
    public bool MainOnline { get; private set; } = true;

    /// <summary>예비 제어기가 설치됐다 (개조).</summary>
    public bool Backup { get; set; }

    /// <summary>주 컴퓨터가 멈췄을 때 예비 제어기가 격벽·댐퍼·경보를 맡고 있다.</summary>
    public bool BackupActive { get; private set; }

    /// <summary>배관망이 없는 시험용 배처럼 주 컴퓨터가 없는 배는 예전처럼 늘 자동이다.</summary>
    public bool Present => ComputerBody != null;

    public bool Doors => !Present || MainOnline || BackupActive;
    public bool Dampers => !Present || MainOnline || BackupActive;
    public bool Alarms => !Present || MainOnline || BackupActive;
    public bool Priority => !Present || MainOnline;

    /// <summary>v12.3 그 방까지 데이터선이 이어져 있어야 자동으로 한다 (끊기면 그 방만 손으로).</summary>
    public bool DoorsIn(Room r) => Doors && r.DataLinked;
    public bool DampersIn(Room r) => Dampers && r.DataLinked;
    public bool AlarmsIn(Room r) => Alarms && r.DataLinked;
    public bool DroneControl => !Present || MainOnline;
    public bool Rods => !Present || MainOnline;

    public int Outages { get; set; }
    public float OfflineHours { get; set; }
    public long OfflineSince { get; private set; }
    public int Overheats { get; set; }

    /// <summary>함교가 환기되는지 (댐퍼가 열리고, 생명유지실이 환기 팬을 돌린다).</summary>
    public bool Ventilated(Room room)
    {
        var life = _world.Ship.LiveRooms.FirstOrDefault(r => r.Type == RoomType.LifeSupport);
        return room.VentOpen && (life == null || life.Powered) && room.Air.Pressure > 40f;
    }

    /// <summary>주 컴퓨터가 방을 데우는 정도 (℃, 대기 온도 목표에 더한다).</summary>
    public float HeatFor(Room room)
    {
        var m = Computer;
        if (m == null || m.Body.Room != room || !m.Powered || m.Stopped) return 0f;
        return Ventilated(room) ? 3f : 24f;
    }

    public void Update(float dt)
    {
        var w = _world;
        if (!Present) return;
        var m = Computer;

        // 과열: 환기가 끊겨 방이 달아오르면 스스로 멈춘다 (식어야 다시 켠다)
        if (m != null && m.Powered && !m.Stopped && !m.Has(FaultKind.Overheat))
        {
            float t = m.Body.Room.Air.Temperature;
            if (t > OverheatC && w.Rng.Chance(MathF.Min(1f, (t - OverheatC) / 8f) * 3f * dt))
            {
                Overheats++;
                w.Machines.Break(m, FaultKind.Overheat);
                MarkLog.Add(m.Marks, w.Tick, $"과열 정지 ({t:0}℃)");
                w.History.Add(w, HistoryKind.Damage, $"주 컴퓨터 과열 정지 — {m.Body.Room.Name} {t:0}℃ (환기가 끊겼다)", m.Body.Room);
            }
        }

        bool main = m != null && m.Efficiency > 0.25f;
        var panelRoom = w.Ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault()?.Room;
        BackupActive = Backup && !main && panelRoom != null && panelRoom.Powered;
        if (main != MainOnline)
        {
            MainOnline = main;
            if (!main)
            {
                Outages++;
                OfflineSince = w.Tick;
                string why = m == null ? "함교와 끊겼다" : !m.Powered ? "전기가 없다" : m.Faults.FirstOrDefault()?.Name ?? "멈췄다";
                w.RaiseAlert($"주 컴퓨터 정지({why}) — 자동화 꺼짐: 격벽·댐퍼·화재 경보·부하 관리를 손으로" +
                             (BackupActive ? " · 예비 제어기가 격벽·댐퍼·경보를 맡는다" : ""), m?.Body.Room, AlertLevel.Critical, shipWide: true);
                w.History.Add(w, HistoryKind.Damage, $"자동화가 꺼졌다 — 주 컴퓨터 {why}", m?.Body.Room);
            }
            else
            {
                float hours = (w.Tick - OfflineSince) / (float)SimTime.TicksPerHour;
                bool temp = m?.Grade == MachineGrade.Mk1 && m.Body.Room.Type != RoomType.Bridge;
                w.Log.Add(w.Tick, LogKind.Ship, (temp ? $"{m!.Body.Room.Name}의 임시 제어 컴퓨터가 돈다" : "주 컴퓨터 복구") + $" — 자동화가 돌아왔다 ({hours:0.0}시간 만)");
                if (hours >= 0.5f) w.History.Add(w, HistoryKind.Response, $"자동화가 {hours:0.0}시간 만에 돌아왔다" + (temp ? " (임시 제어 컴퓨터)" : ""), m?.Body.Room);
            }
            w.Board.RequestScan();
        }
        if (!main) OfflineHours += dt;
        bool gone = Gone;
        if (gone && GoneSince < 0) GoneSince = w.Tick;
        else if (!gone) GoneSince = -1;
    }
}
