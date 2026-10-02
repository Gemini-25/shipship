using System;
using System.Collections.Generic;

namespace ShipSim.Core;

// v16.25 → v16.26 주컴퓨터 계획이 갈래를 "한 걸음"으로 쓰는 창구.
// 읽기만 하는 목록(Options)과, 고른 걸음을 사람들에게 권하는 한 줄(Suggest)뿐이다 — 실행은 기존처럼 사람 · 로봇 · 컴퓨터 손이 한다.
// 판정은 컴퓨터가 갈래를 견줄 때(Compare)와 같은 규칙(ShipCan: 창고 · 근처 물건 · 설비 · 방침)을 쓴다.

/// <summary>계획의 한 걸음 후보: 갈래 · 지금 쓸 수 있나 · 무엇이 모자라나 · 예상 시간 범위 · 위험 · 배가 잃는 것 · 부작용 · 나중 일 · 이 배의 지난 기록.</summary>
public sealed record WayOption(
    Way Way, bool Ready, string Need, float MinLo, float MinHi, float Risk, float ShipCost,
    string Side, string Later, bool Book, bool Leave, string Past)
{
    public string Id => Way.Id;
    public WayBy By => Way.By;
    /// <summary>손을 뗀 뒤 저절로 풀릴 때까지 기다리는 몫 (문 닫고 숨 끊기 · 진공).</summary>
    public float Wait => Way.Resolve;
}

public sealed partial class WaysSystem
{
    /// <summary>
    /// 이 문제를 이 방에서 풀 갈래 목록 (계획용). 같은 시각 같은 상태면 같은 결과 — 난수를 쓰지 않는다.
    /// MinLo~MinHi 는 몇 분 걸릴지 범위: 배의 관행 · 지난 기록이면 아래로, 모르면 넓게.
    /// </summary>
    public List<WayOption> Options(Snag s, Room? room, Cell? at = null, bool readyOnly = false)
    {
        var w = _w;
        Cell pos = at ?? (room != null && room.Cells.Count > 0 ? room.Cells[room.Cells.Count / 2] : default);
        var open = CaseFor(s, room);
        var k = open is { Open: true } ? open : new WayCase { Id = -1, Snag = s, RoomId = room?.Id ?? -1, At = pos, DoorId = StuckDoorOf(room) };
        var list = new List<WayOption>();
        foreach (var way in WaysTable.Of(s))
        {
            var (ok, note, mins) = ShipCan(way, k, room);
            if (readyOnly && !ok) continue;
            var p = PracticeOrNull(way.Id);
            float lo = mins * SpeedMul(null, way), hi = mins * 1.6f + way.Resolve;
            if (p != null && p.Ok > 0 && p.BestMinutes < 9000f) { lo = MathF.Min(lo, p.BestMinutes); hi = MathF.Max(lo + 1f, MathF.Min(hi, p.LastMinutes * 1.3f + way.Resolve)); }
            else if (p == null || p.Uses == 0) hi *= 1.25f; // 이 배에서 해 본 적 없다 — 넓게
            string past = "";
            if (p is { Custom: true }) past = $"이 배의 방식 ({p.Origin})";
            else if (p is { Uses: > 0 }) past = $"{p.Uses}번 · 잘됨 {p.Ok}";
            if (PastOk(s) is WayPast po && po.WayId == way.Id && past.Length == 0) past = $"지난번 {po.Room}에서 통했다";
            list.Add(new WayOption(way, ok, ok ? "" : note, lo, MathF.Max(lo, hi), way.Risk, way.ShipCost, way.Side, way.Later, way.Book, way.Leave, past));
        }
        return list;
    }

    /// <summary>계획이 고른 걸음을 이 문제의 컴퓨터 안으로 건다 — 사람들은 이 안을 보고 고른다(따를지 다른 길로 갈지는 그 사람).</summary>
    public bool Suggest(Snag s, Room? room, string wayId, string why)
    {
        if (WaysTable.Get(wayId) is not Way way || way.Snag != s) return false;
        if (CaseFor(s, room) is not { Open: true } k) return false;
        k.ComputerPick = wayId;
        k.ComputerWhy = why;
        return true;
    }

    /// <summary>그 걸음이 끝났나: 해 본 적 있나 · 잘됐나 · 문제가 풀렸나 (계획의 완료 확인용).</summary>
    public (bool tried, bool ok, bool solved) StepState(Snag s, Room? room, string wayId)
    {
        if (CaseFor(s, room) is not WayCase k) return (false, false, false);
        bool tried = false, ok = false;
        foreach (var t in Tries) if (t.CaseId == k.Id && t.WayId == wayId && t.State == 3) { tried = true; ok |= t.Ok; }
        return (tried, ok, !k.Open);
    }

    private int StuckDoorOf(Room? room)
    {
        if (room == null) return -1;
        foreach (var d in room.Doors) if (!d.IsExternal && Stuck(d)) return d.Id;
        return -1;
    }
}
