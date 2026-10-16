using System;
using System.Linq;

namespace ShipSim.Core;

/// <summary>
/// 문 구동기와 조명 (v9.4). 기획서의 연쇄 끝부분 "… 전력 제한 → 자동문 멈춤 → 부상자 이동이 늦어짐"을 설비 고장으로도 만든다.
///
/// 문: 열릴 때마다 조금씩 닳고, 운석 파편·옆 칸의 불에 구동기가 망가진다. 망가진 문은 전기가 있어도 손으로 천천히 열고,
///     격벽이 저절로 잠기지 않는다 (감압이면 누가 돌려 닫아야 한다). 고치기: 모터 1, 없으면 케이블 1 + 금속판 1로 임시 구동기(느리다).
/// 조명: 운석·불·배선 단락에 나간다. 캄캄한 방(정전 포함)에서는 일이 느리고(헬멧 등이 있는 우주복은 괜찮다), 길을 꺼리고, 오래 있으면 불안하다.
///     고치기: 케이블 1. 없으면 캄캄한 채로.
/// </summary>
public sealed class FixturesSystem
{
    /// <summary>문이 한 번 열릴 때 구동기가 망가질 확률 (닳을수록 조금씩 오른다).</summary>
    public const float WearPerCycle = 1f / 9000f;

    private readonly World _world;
    public FixturesSystem(World world) => _world = world;

    public int DoorFailures { get; set; }
    public int LightFailures { get; set; }

    public void Update(float dt)
    {
        var w = _world;
        var ship = w.Ship;
        foreach (var d in ship.Doors)
        {
            int n = d.PendingCycles;
            d.PendingCycles = 0;
            if (d.IsExternal || d.Removed || d.Welded || d.MotorBroken) continue;
            // 닳음: 많이 여닫은 문일수록 (임시 구동기는 더 잘 망가진다)
            float p = WearPerCycle * (1f + d.Cycles / 6000f) * (d.MotorMk1 ? 3f : 1f);
            for (int i = 0; i < n; i++)
                if (w.Rng.Chance(p)) { BreakDoor(d, "닳아서"); break; }
            // 옆 칸의 불: 구동기 배선이 탄다
            if (!d.MotorBroken && w.Fire.AnyWithin(d.Cell, 1.5f) && w.Rng.Chance(0.35f * dt))
                BreakDoor(d, "불에 구동기가 탔다");
        }
        foreach (var r in ship.LiveRooms)
        {
            // 불이 번진 방은 조명이 녹아내린다
            if (!r.LightsOut && w.Fire.CountIn(r) >= 2 && w.Rng.Chance(0.3f * dt)) LightsFail(r, "불에 조명이 녹았다");
            // 캄캄한 방에 오래 있으면 불안하다 (자는 사람·우주복 헬멧 등 제외)
            if (!r.Dark) continue;
            foreach (var c in w.Crew)
                if (c.Room == r && c.CanAct && c.IsAwake && c.Suit == null)
                    c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.03f * dt * (1.2f - c.Traits.Calm));
        }
    }

    public void BreakDoor(Door d, string why)
    {
        var w = _world;
        if (d.MotorBroken || d.IsExternal) return;
        d.MotorBroken = true;
        d.MotorMk1 = false;
        d.MotorBreaks++;
        DoorFailures++;
        string where = $"{d.RoomA?.Name ?? "?"}·{d.RoomB?.Name ?? "?"}";
        w.Log.Add(w.Tick, LogKind.Warning, $"{where} 사이 문 구동기 고장 ({why}) — 손으로 천천히 연다");
        if (why != "닳아서") w.History.Add(w, HistoryKind.Damage, $"{where} 사이 문 구동기가 망가졌다 — {why}", d.RoomA ?? d.RoomB, at: d.Cell);
        w.Board.RequestScan();
    }

    public void LightsFail(Room r, string why)
    {
        var w = _world;
        if (r.LightsOut || r.Type == RoomType.Corridor && w.Rng.Chance(0.5f)) return;
        r.LightsOut = true;
        r.LightsOutSince = w.Tick;
        LightFailures++;
        MarkLog.Add(r.Marks, w.Tick, $"조명이 나갔다 ({why})");
        w.Log.Add(w.Tick, LogKind.Warning, $"{r.Name} 조명이 나갔다 — {why}");
        w.History.Add(w, HistoryKind.Damage, $"{r.Name} 조명이 나갔다 — {why}", r);
        w.Board.RequestScan();
    }

    /// <summary>운석 파편이 지나간 칸: 문 구동기와 그 방 조명.</summary>
    public void OnDebris(Cell cell, float strength, Room? room)
    {
        var w = _world;
        if (w.Ship.DoorAt(cell) is Door d && w.Rng.Chance(0.6f * strength)) BreakDoor(d, "운석 파편");
        if (room != null && w.Rng.Chance(0.25f * strength)) LightsFail(room, "운석 파편");
    }
}
