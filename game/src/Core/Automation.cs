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
public sealed partial class AutomationSystem
{
    /// <summary>이 온도를 넘으면 과열 정지할 수 있다 (℃).</summary>
    public const float OverheatC = 38f;

    /// <summary>과열 정지한 컴퓨터를 다시 켜려면 방이 이만큼 식어야 한다.</summary>
    public const float RestartC = 32f;

    private readonly World _world;
    public AutomationSystem(World world) => _world = world;

    private System.Collections.Generic.IReadOnlyList<Furniture> Bodies => _world.Ship.AllOfType(FurnitureType.MainComputer); // 통합 성능: 종류별 목록 (같은 것 · 같은 순서)

    /// <summary>자동화를 맡는 컴퓨터: 배에 붙어 있는 것 중 가장 잘 도는 것 (함교 것을 잃으면 임시 제어 컴퓨터).</summary>
    public Furniture? ComputerBody
    {
        get
        {
            // 통합 성능: 붙어 있는 것 → 잘 도는 것 → 번호 순 (줄 세워 첫째를 고르던 것과 같은 답, 목록을 만들지 않는다)
            Furniture? best = null;
            int bd = 0; float be = 0f;
            foreach (var f in Bodies)
            {
                int d = f.Room.Detached ? 1 : 0;
                float e = f.Machine!.Efficiency;
                if (best == null || d < bd || d == bd && (e > be || e == be && f.Id < best.Id)) { best = f; bd = d; be = e; }
            }
            return best;
        }
    }
    public Machine? Computer => ComputerBody is Furniture f && !f.Room.Detached ? f.Machine : null;

    /// <summary>쓸 수 있는 컴퓨터가 아예 없다 (떨어져 나갔거나 부서졌거나 뜯겼다).</summary>
    public bool Gone => Present && !Bodies.Any(f => !f.Room.Detached && !f.Machine!.Has(FaultKind.Wrecked) && !f.Machine.Has(FaultKind.Stripped));
    public long GoneSince { get; private set; } = -1;

    /// <summary>주 컴퓨터가 돈다 (모든 자동화).</summary>
    public bool MainOnline { get; private set; } = true;

    /// <summary>예비 제어기가 설치됐다 (개조). v16.20 구역 소형 제어기 — 첫날부터 있다.</summary>
    public bool Backup { get; set; } = true;

    /// <summary>주 컴퓨터가 멈췄을 때 예비 제어기가 격벽·댐퍼·경보를 맡고 있다.</summary>
    public bool BackupActive { get; private set; }

    /// <summary>배관망이 없는 시험용 배처럼 주 컴퓨터가 없는 배는 예전처럼 늘 자동이다.</summary>
    public bool Present => ComputerBody != null;

    public bool Doors => !Present || MainOnline || BackupActive;
    public bool Dampers => !Present || MainOnline || BackupActive;
    public bool Alarms => !Present || MainOnline || BackupActive;
    public bool Priority => !Present || CoreOnline; // v16.20 부하 우선순위는 핵심 고리 (예비 코어도)

    /// <summary>v12.3 그 방까지 데이터선이 이어져 있어야 자동으로 한다 (끊기면 그 방만 손으로).</summary>
    public bool DoorsIn(Room r) => Doors && r.DataLinked;

    /// <summary>v13.2 방침(컴퓨터 자동 실행)이 허락할 때만 격벽을 스스로 닫는다 (경보만이면 사람이 손으로).</summary>
    public bool AutoDoorsIn(Room r) => DoorsIn(r) && _world.Policies["autoscope"] >= 1;
    public bool DampersIn(Room r) => Dampers && r.DataLinked;
    public bool AlarmsIn(Room r) => Alarms && (r.DataLinked || ComputerV15.Relay(_world)); // v15.9 통신 중계
    public bool DroneControl => !Present || MainOnline;
    public bool Rods => !Present || CoreOnline; // v16.20 자동 제어봉도 핵심 고리

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
        if (m == null || m.Body.Room != room || !m.Powered && !Core.OnUps || m.Stopped && !Core.BackupCore) return 0f;
        float busy = MathF.Max(0f, Load - 0.6f); // v16.6 연산 부하가 크면 더 달아오른다
        return (Ventilated(room) ? 3f + 8f * busy : 24f + 6f * busy) * Core.HeatMul * (m.Stopped ? 0.3f : 1f); // v16.20 안전 모드 · 절전 · 예비 코어만
    }

    public void Update(float dt)
    {
        var w = _world;
        TrackUnattended(); // 사람이 없으면 로봇이 손일을 맡는다 (주 컴퓨터가 없어도 로봇은 제 판단으로)
        if (!Present) return;
        var m = Computer;

        // v16.20 과열: 멈추는 대신 안전 모드 (ComputerCore.cs) · UPS · 품질
        Core.Before(m, dt);

        bool main = m != null && Core.MainHealth(m) > 0.25f && !Rebooting; // v16.6 재부팅 중에는 주 코어가 쉰다 (v16.20 예비 코어가 핵심 고리를 붙잡는다)
        Core.After(m, main, dt);
        var panelRoom = w.Ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault()?.Room;
        BackupActive = !main && (Core.BackupCore || Backup && panelRoom != null && panelRoom.Powered);
        if (main != MainOnline)
        {
            MainOnline = main;
            if (!main)
            {
                Outages++;
                OfflineSince = w.Tick;
                string why = m == null ? "함교와 끊겼다" : Rebooting ? $"재부팅 — {RebootWhy}" : !m.Powered && !Core.OnUps ? "전기가 없다" : m.Faults.FirstOrDefault()?.Name ?? "멈췄다";
                if (Core.BackupCore) // v16.20 예비 코어가 붙잡는다 — 판단(예측 · 원인 추정)만 쉰다
                    w.RaiseAlert($"주 컴퓨터 본체 정지({why}) — 예비 연산기가 격벽·댐퍼·경보와 급한 곳 전기를 붙잡는다 · 앞일 예측은 쉰다", m?.Body.Room, Rebooting ? AlertLevel.Notice : AlertLevel.Warning, shipWide: !Rebooting);
                else
                    w.RaiseAlert($"주 컴퓨터 정지({why}) — 자동화 꺼짐: 격벽·댐퍼·화재 경보·부하 관리를 손으로" +
                             (BackupActive ? " · 방 제어기가 격벽·댐퍼·경보를 맡는다" : ""), m?.Body.Room, AlertLevel.Critical, shipWide: true);
                w.History.Add(w, HistoryKind.Damage, Core.BackupCore ? $"주 컴퓨터 본체가 멎었다 — {why} (예비 연산기가 붙잡았다)" : $"자동화가 꺼졌다 — 주 컴퓨터 {why}", m?.Body.Room);
            }
            else
            {
                float hours = (w.Tick - OfflineSince) / (float)SimTime.TicksPerHour;
                bool temp = m?.Grade == MachineGrade.Mk1 && m.Body.Room.Type != RoomType.Bridge && m.Body.Room.Kind != RoomType.ComputerRoom; // v16.22 주컴퓨터실
                w.Log.Add(w.Tick, LogKind.Ship, (temp ? $"{m!.Body.Room.Name}의 임시 제어 컴퓨터가 돈다" : "주 컴퓨터 복구") + $" — 자동화가 돌아왔다 ({hours:0.0}시간 만)");
                if (hours >= 0.5f) w.History.Add(w, HistoryKind.Response, $"자동화가 {hours:0.0}시간 만에 돌아왔다" + (temp ? " (임시 제어 컴퓨터)" : ""), m?.Body.Room);
            }
            w.Board.RequestScan();
        }
        if (!main) OfflineHours += dt;
        RemoteHands(); // 보조 발전기 원격 시동 (Unattended.cs)
        V16(dt); // v16.0 ④ · v16.6 다섯 칸 기록 · 믿는 배 · 제안 · 신뢰 · 연산 자원 · 방송 · 새 모듈
        Think(dt); // v12.5 등급·수동 조종·예측·방침
        Respond(dt); // v13.0 대응 수순 (화재 · 공기 구역)
        bool gone = Gone;
        if (gone && GoneSince < 0) GoneSince = w.Tick;
        else if (!gone) GoneSince = -1;
    }
}
