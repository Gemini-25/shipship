using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v23 연출: 창밖 풍경. 항로 구간마다 창밖이 다르다 (별바다 · 소행성대 · 성운 · 가스 행성 · 혜성 꼬리 · 펄서 · 정거장 …).
//   그 빛이 창가 방에 든다 (색 · 세기 — 화면이 쓴다). 볼 만한 풍경이 오면 처음 여덟 시간 동안 한가한 사람들이 창가에 모여 구경한다.
//   처음 보는 풍경은 연대기에 남는다.

public readonly record struct SceneryLook(string Name, string Line, float R, float G, float B, bool Scenic);

public static class Scenery
{
    public static SceneryLook Of(LegKind k) => k switch
    {
        LegKind.AsteroidBelt => new("소행성대", "구르는 바위들이 창밖을 스친다", 0.22f, 0.20f, 0.18f, true),
        LegKind.Nebula => new("성운", "보랏빛 · 청록 구름이 창을 물들인다", 0.35f, 0.18f, 0.45f, true),
        LegKind.RadiationBelt => new("방사선대", "희미한 초록 띠가 일렁인다", 0.15f, 0.30f, 0.12f, false),
        LegKind.Derelict => new("난파선", "부서진 배의 그림자가 천천히 돈다", 0.18f, 0.18f, 0.20f, true),
        LegKind.Port => new("기항지", "정거장 불빛과 행성의 가장자리", 0.35f, 0.30f, 0.20f, true),
        LegKind.SolarWind => new("태양풍", "빛줄기가 앞에서 뒤로 쏟아진다", 0.40f, 0.32f, 0.15f, false),
        LegKind.CometTrail => new("혜성 꼬리", "푸른 꼬리를 단 혜성이 나란히 난다", 0.20f, 0.32f, 0.45f, true),
        LegKind.GasGiant => new("가스 행성", "띠구름 행성이 창 하나를 가득 채운다", 0.45f, 0.30f, 0.15f, true),
        LegKind.IceRing => new("얼음 고리", "반짝이는 얼음 알갱이 고리를 가로지른다", 0.30f, 0.38f, 0.45f, true),
        LegKind.Pulsar => new("펄서", "숨 쉬듯 깜빡이는 하얀 별", 0.40f, 0.45f, 0.55f, true),
        LegKind.DeepVoid => new("깊은 공허", "별도 거의 없는 검은 바다", 0.05f, 0.06f, 0.09f, false),
        LegKind.TradeLane => new("교역 항로", "다른 배들의 불빛이 줄지어 간다", 0.25f, 0.22f, 0.18f, true),
        LegKind.MagneticField => new("자기장 지대", "오로라 같은 빛이 선을 그린다", 0.18f, 0.35f, 0.30f, true),
        LegKind.Graveyard => new("배들의 무덤", "옛 배들의 잔해가 떠 있다", 0.14f, 0.14f, 0.16f, true),
        LegKind.PatrolLane => new("순찰 항로", "부표 불빛이 차례로 깜빡인다", 0.30f, 0.12f, 0.12f, false),
        LegKind.Perihelion => new("근일점", "해가 커다랗게 떠 창이 하얗게 탄다", 0.60f, 0.50f, 0.30f, true),
        LegKind.SporeCloud => new("포자 구름", "초록 먼지가 반짝이며 흐른다", 0.20f, 0.35f, 0.15f, true),
        LegKind.Lagrange => new("라그랑주 점", "두 천체 사이 고요한 자리", 0.22f, 0.24f, 0.32f, true),
        LegKind.Narrows => new("좁은 물길", "양옆으로 바위벽이 지나간다", 0.16f, 0.15f, 0.14f, false),
        _ => new("별바다", "멀리 은하수 띠가 흐른다", 0.12f, 0.16f, 0.26f, false),
    };

    /// <summary>창이 있는 방 (관측 · 함교 · 휴게 · 식당 · 통신 · 재배 + 선체에 창을 낸 방).</summary>
    public static bool Windowed(World w, Room r) => CurioSystem.HasWindow(r) || w.Body.OpenWindows(r) > 0;
}

public sealed partial class VoyageSystem
{
    /// <summary>지금 풍경이 시작된 때 · 이번 풍경을 이미 보러 간 사람.</summary>
    public long ViewSince { get; private set; } = -1;
    public List<int> ViewSeen { get; } = new();
    /// <summary>한 번이라도 본 풍경 (처음 보면 연대기에).</summary>
    public List<int> KindsSeen { get; } = new();
    public int ViewGathered { get; internal set; }
    public int ViewDone { get; internal set; }

    public SceneryLook View => Scenery.Of(Current.Kind);

    /// <summary>풍경이 볼 만하고 아직 여덟 시간이 안 지났다.</summary>
    public bool ViewFresh => View.Scenic && ViewSince >= 0 && _w.Tick - ViewSince < SimTime.Hours(8);

    /// <summary>구간에 들어섰다: 새 풍경 (볼 만하면 알린다 · 처음 보면 연대기에).</summary>
    internal void OnScenery()
    {
        var w = _w;
        ViewSince = w.Tick;
        ViewSeen.Clear();
        var v = View;
        if (!v.Scenic) return;
        w.Log.Add(w.Tick, LogKind.Ship, $"창밖 — {v.Name}: {v.Line}");
        if (!KindsSeen.Contains((int)Current.Kind))
        {
            KindsSeen.Add((int)Current.Kind);
            w.History.Add(w, HistoryKind.Milestone, $"처음 본 창밖 — {v.Name} ({v.Line})", null);
            w.Purpose?.Data(1, $"처음 본 {v.Name}");
        }
    }

    /// <summary>시험 · 화면 확인: 구간을 바로 바꾼다.</summary>
    public void JumpTo(LegKind kind)
    {
        int at = Legs.FindIndex(Index, l => l.Kind == kind);
        if (at < 0) { Legs.Insert(Index + 1, new Leg { Kind = kind, Name = KindName(kind), Days = 2f }); at = Index + 1; }
        while (Index < at) { Progress = Current.Days; Update(0f); }
    }
}

/// <summary>창밖 풍경 구경: 볼 만한 구간에 들어선 뒤 여덟 시간 동안 · 한가한 사람이 창가에 가서 본다 (마음이 조금 풀린다).</summary>
public sealed class LegViewActivity : Activity
{
    public override string Id => "legview";
    public override string Label => "창밖 풍경";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var vs = w.Voyage;
        if (!vs.ViewFresh || vs.ViewSeen.Contains(c.Id) || c.IsChild && !c.IsAwake) return (0f, "—");
        if (c.Job?.Urgent == true || OnShift(c, w) && w.Board.Open.Any(o => o.Urgency >= 0.9f)) return (0f, "—");
        float h = SimTime.HourOfDay(w.Tick);
        float evening = h >= 18f && h < 22f ? 0.12f : 0f;
        return (0.22f + 0.2f * (1f - c.Needs.Stress) + evening + (OnShift(c, w) ? -0.15f : 0f), $"창밖에 {vs.View.Name}");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var vs = w.Voyage;
        var v = vs.View;
        Cell? best = null;
        int bd = int.MaxValue;
        foreach (var r in w.Ship.LiveRooms)
        {
            if (!Scenery.Windowed(w, r) || r.OffLimits || r.Abandoned) continue;
            foreach (var cell in r.Cells)
            {
                if (!w.Ship.IsWalkable(cell) || w.IsSpotTaken(cell, c)) continue;
                int d = dist.Get(cell);
                if (d >= 0 && d < bd) { bd = d; best = cell; }
            }
        }
        if (best is not Cell spot) return null;
        vs.ViewSeen.Add(c.Id);
        vs.ViewGathered++;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(spot));
        toils.Add(new DoToil((cm, world) =>
        {
            // 창가에 닿는 순간 본 것이다 (그 뒤 구경은 다른 일에 끊겨도 된다)
            cm.Needs.Stress = MathF.Max(0f, cm.Needs.Stress - 0.05f);
            world.Voyage.ViewDone++;
            MarkLog.Add(cm.Memory.Marks, world.Tick, $"창밖 {v.Name}을 봤다 — {v.Line}");
            return true;
        }));
        toils.Add(new WaitToil(SimTime.Minutes(20), Pose.Standing, null, minTicks: SimTime.Minutes(8)));
        return new Job(this, "창밖 풍경", toils) { LogText = $"창밖 {Ko.EulReul(v.Name)} 보러 창가로" };
    }
}
