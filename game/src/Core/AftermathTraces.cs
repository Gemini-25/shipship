using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v17.5 흔적 목록: 배의 모양과 생활 패턴만 보고 무슨 일을 겪었는지 읽는다.
//  · 일반 얼룩(물 · 기름 · 서리 · 유리 · 그을음 · 테이프 · 그을린 바닥)은 방마다 묶어 한 줄로.
//  · 사람 · 물건 · 사건과 이어진 흔적만 따로: 빈 의자와 컵 · 종이꽃 · 냉장고 쪽지 · 빨랫줄 고리 · 옮겨 온 등 · 다시 그린 그림 · 굳은 식탁 자리 ·
//    그을린 벽판 · 추모 액자 · 큰 사고 자국 · 다들 비켜 걷는 자리.
//  · 생활 패턴: 다른 방에서 먹는 사람 · 혼자 먹는 사람 · 비워 둔 자리.
// 짐작(Infer)은 기록(History)을 보지 않는다 — 흔적과 생활만 본다.

public sealed record TraceLine(int Room, string Text, bool Linked, string Kind);

public sealed partial class AftermathSystem
{
    private static readonly string[] StainName = { "물 얼룩", "기름 자국", "서리 자국", "유리 조각", "그을음", "테이프 땜질", "그을린 바닥" };

    /// <summary>방마다 일반 얼룩 칸 수 (칸 상태 6종 + 불에 그을린 바닥).</summary>
    public SortedDictionary<int, int[]> Stains()
    {
        var w = _w;
        var counts = new SortedDictionary<int, int[]>();
        int width = w.Ship.Grid.Width;
        foreach (var (idx, st) in w.Body.Marks)
        {
            var cell = new Cell(idx % width, idx / width);
            if (w.Ship.RoomAt(cell) is not Room r) continue;
            for (int k = 0; k < CellState.Kinds; k++)
            {
                if (st.V[k] < 0.1f) continue;
                if (!counts.TryGetValue(r.Id, out var a)) counts[r.Id] = a = new int[7];
                a[k]++;
            }
        }
        foreach (var (cell, v) in w.Fire.Scorch)
        {
            if (v < 0.1f || w.Ship.RoomAt(cell) is not Room r) continue;
            if (!counts.TryGetValue(r.Id, out var a)) counts[r.Id] = a = new int[7];
            a[6]++;
        }
        return counts;
    }

    /// <summary>흔적 목록 (묶은 얼룩 · 이어진 흔적 · 생활 패턴).</summary>
    public List<TraceLine> TraceList()
    {
        var w = _w;
        var list = new List<TraceLine>();
        foreach (var (rid, a) in Stains())
        {
            var parts = new List<string>();
            for (int k = 0; k < a.Length; k++) if (a[k] > 0) parts.Add($"{StainName[k]} {a[k]}칸");
            if (parts.Count > 0) list.Add(new TraceLine(rid, $"{RoomById(rid)?.Name}: {string.Join(" · ", parts)}", false, "stain"));
        }
        foreach (var t in Traces) if (!t.Gone) list.Add(new TraceLine(t.Room, t.Text, true, t.Kind.ToString()));
        foreach (var p in w.Props.Placed)
            if (p.Spec.Source == PropSource.Event)
                list.Add(new TraceLine(p.RoomId, $"{RoomById(p.RoomId)?.Name}의 {p.Name}" + (p.Origin != "" ? $" — {p.Origin}" : ""), true, "prop:" + p.Spec.Id));
        foreach (var mt in w.Major.Traces)
            list.Add(new TraceLine(mt.Room, $"{RoomById(mt.Room)?.Name}: {MajorIncidentSystem.Spec(mt.Kind).Name} 자국 ({SimTime.Day(mt.Tick)}일)", true, "major:" + MajorIncidentSystem.Spec(mt.Kind).Key));
        foreach (var p in Places)
        {
            int n = 0;
            foreach (var c in w.Crew) if (!c.Dead && PlaceWeight(c, p) >= 0.3f) n++;
            if (n > 0) list.Add(new TraceLine(p.Room, $"{RoomById(p.Room)?.Name}: {n}명이 비켜 걷는 자리 — {p.Text}", true, p.Kind == 0 ? "place:death" : "place:major"));
        }
        // 생활 패턴
        foreach (var su in Setups)
            if (su.Kind == 0 && w.Tick - su.Last < SimTime.TicksPerDay && su.Plan < 0)
                list.Add(new TraceLine(su.Room, $"{RoomById(su.Room)?.Name}에서 끼니를 먹는 사람이 있다 ({su.Users.Count}명 · {su.Why})", true, "life:away"));
        foreach (var (id, m) in Minds)
            if (m.Stage == 2 && Crew(id) is CrewMember c && !c.Dead) list.Add(new TraceLine(c.Bed?.Room.Id ?? -1, $"{Ko.EunNeun(c.Name)} 요즘 선실에서 혼자 먹는다", true, "life:alone"));
        foreach (var es in Seats)
            if (es.Active) list.Add(new TraceLine(es.Room, $"{RoomById(es.Room)?.Name}에 아무도 앉지 않는 의자가 있다" + (es.Cup ? " (컵 하나)" : ""), true, "life:seat"));
        return list;
    }

    /// <summary>흔적과 생활 패턴만 보고 겪은 일을 짐작한다 — 불 · 물 · 정전 · 죽음 · 큰 사고.</summary>
    public SortedSet<string> Infer()
    {
        var w = _w;
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (_, a) in Stains())
        {
            if (a[(int)CellMark.Soot] >= 3 || a[6] >= 2) set.Add("불");
            if (a[(int)CellMark.Wet] >= 6) set.Add("물");
        }
        foreach (var l in TraceList())
        {
            switch (l.Kind)
            {
                case nameof(AfterTraceKind.Repainted): case nameof(AfterTraceKind.DiningCorner): case "prop:scorch": case "life:away": set.Add("불"); break;
                case nameof(AfterTraceKind.DryHooks): set.Add("물"); break;
                case nameof(AfterTraceKind.FridgeNote): case nameof(AfterTraceKind.PersonalLamp): case nameof(AfterTraceKind.LampFixture): set.Add("정전"); break;
                case nameof(AfterTraceKind.EmptySeat): case nameof(AfterTraceKind.SpotFlower): case "prop:memorial": case "place:death": case "life:seat": set.Add("죽음"); break;
                default:
                    if (l.Kind.StartsWith("major:", StringComparison.Ordinal)) set.Add("큰 사고");
                    break;
            }
        }
        foreach (var r in Repaints) if (r.ForDead) set.Add("죽음");
        for (int i = 0; i < _soot.Length; i++) if (_soot[i] > 0.1f) set.Add("불");
        if (WetBeds.Count > 0 || Lines.Any(l => !l.Done)) set.Add("물");
        return set;
    }
}
