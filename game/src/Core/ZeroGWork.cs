using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v18.4 무중력에서 사람이 하는 일: 중력 장치 고치기 · 떠다니는 것 붙잡아 넣기 (방송을 들은 사람이 짚은 방부터 · 물방울은 수건으로).
public sealed partial class ZeroGSystem
{
    /// <summary>고칠 자리: 회전 고리 방의 모터 쪽 · 없으면 배전실 · 함교 콘솔.</summary>
    public Cell? RepairSpot()
    {
        var w = _w;
        Room? r = RingRoom ?? w.Ship.RoomsOf(RoomType.Power).FirstOrDefault() ?? w.Ship.RoomsOf(RoomType.Engine).FirstOrDefault() ?? w.Ship.RoomsOf(RoomType.Bridge).FirstOrDefault();
        if (r == null) return null;
        var f = r.Furniture.Where(x => x.UseSpots.Count > 0 && (Electric(x.Type) || x.Type is FurnitureType.EngineCore or FurnitureType.Workbench)).OrderBy(x => x.Id).FirstOrDefault();
        if (f != null) return f.UseSpots[0];
        return r.Cells.Where(c => w.Ship.IsWalkable(c)).OrderBy(c => c.X * 1000 + c.Y).Cast<Cell?>().FirstOrDefault();
    }

    internal void Repaired(CrewMember c)
    {
        var w = _w;
        if (!Weightless || !NeedsRepair) return;
        NeedsRepair = false; RepairBy = -1; RepairDone = 1f; Stats.Repairs++;
        w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(c.Name)} 떠다니며 {(Ring ? "회전 고리 베어링" : "중력 판 제어기")}를 고쳤다", c.Id);
        MarkLog.Add(c.Memory.Marks, w.Tick, "무중력에서 중력 장치를 고쳤다");
        ScheduleRestore(true, $"{c.Name}님이 고쳤습니다");
    }

    /// <summary>붙잡을 것: 들은 방 (컴퓨터가 짚은) · 내 방 — 전기 곁 물방울 · 단단한 것 먼저.</summary>
    public Drifter? CatchTarget(CrewMember c)
    {
        if (!Weightless || c.Room == null) return null;
        int room = Knew.Contains(c.Id) && OrderRoom >= 0 ? OrderRoom : c.Room.Id;
        Drifter? best = null; float bs = float.MinValue;
        foreach (var f in Floaters)
        {
            if (f.ClaimedBy >= 0 && f.ClaimedBy != c.Id) continue;
            if (f.RoomId != room && f.RoomId != c.Room.Id) continue;
            float s = (f.Hard ? 2f : 0f) + (f.Wet ? 1.5f : 0f) + (f.Kind == DriftKind.Sick ? 1f : 0f) - 0.1f * (f.Pos - c.Position).Length() + (f.RoomId == room ? 0.5f : 0f);
            if (s > bs) { bs = s; best = f; }
        }
        return best;
    }

    internal bool Reach(CrewMember c, Drifter f) => Floaters.Contains(f) && (f.Pos - c.Position).LengthSquared() < 3.4f;

    public bool Ready(CrewMember c) => Weightless && Queasy.GetValueOrDefault(c.Id) < 0.75f;
}

public sealed class ZeroGActivity : Activity
{
    public override string Id => "zerog";
    public override string Label => "무중력";

    private static bool Fixer(CrewMember c) => c.SkillLevel(Skill.Mechanics) >= 0.3f || c.SkillLevel(Skill.Engineering) >= 0.35f;

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var z = w.ZeroG;
        if (ZeroGSystem.Off || !z.Weightless || !c.CanAct || c.Outside || c.Room == null || c.IsChild) return (0f, "—");
        if (z.NeedsRepair && (z.RepairBy < 0 || z.RepairBy == c.Id) && Fixer(c) && z.RepairSpot() is Cell rs && dist.Reachable(rs))
            return (1.6f, z.Ring ? "멈춘 회전 고리를 고치러 간다" : "중력 판 제어기를 고치러 간다");
        if (!z.Ready(c)) return (0f, "—");
        if (z.CatchTarget(c) is Drifter f)
        {
            bool heard = z.Knew.Contains(c.Id);
            float s = (heard ? 1.1f : 0.7f) + MathF.Min(0.3f, 0.1f * z.Exposures.GetValueOrDefault(c.Id));
            return (s, f.Wet ? $"떠다니는 {Ko.EulReul(f.Name)} 수건으로 감싼다" : heard ? $"방송대로 떠다니는 {Ko.EulReul(f.Name)} 붙잡아 넣는다" : $"떠다니는 {Ko.EulReul(f.Name)} 붙잡는다");
        }
        return (0f, "—");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var z = w.ZeroG;
        if (z.NeedsRepair && (z.RepairBy < 0 || z.RepairBy == c.Id) && Fixer(c) && z.RepairSpot() is Cell rs)
        {
            z.RepairBy = c.Id;
            var toils = new List<Toil>
            {
                new GotoToil(rs),
                new WorkToil(z.Ring ? 1.4f : 1.1f, Skill.Mechanics, null) { CanContinue = (cm, world) => world.ZeroG.Weightless && world.ZeroG.NeedsRepair },
                new DoToil((cm, world) => { world.ZeroG.Repaired(cm); return true; }),
            };
            return new Job(this, "중력 장치 수리", toils) { InterruptMargin = 0.6f, OnFinished = (cm, world, _) => { if (world.ZeroG.RepairBy == cm.Id) world.ZeroG.RepairBy = -1; } };
        }
        if (z.CatchTarget(c) is not Drifter f) return null;
        f.ClaimedBy = c.Id;
        Cell? Where(CrewMember cm) => w.ZeroG.Floaters.Contains(f) && w.Ship.IsWalkable(Cell.FromPosition(f.Pos)) ? Cell.FromPosition(f.Pos) : null;
        var list = new List<Toil>
        {
            new GotoToilLate(Where),
            new GotoToilLate(Where), // 그새 흘러갔으면 다시
            new DoToil((cm, world) => { if (world.ZeroG.Reach(cm, f)) world.ZeroG.Catch(f, cm); return true; }),
        };
        return new Job(this, f.Wet ? "물방울 감싸기" : "떠다니는 것 붙잡기", list) { InterruptMargin = 0.4f, OnFinished = (cm, world, _) => { if (f.ClaimedBy == cm.Id) f.ClaimedBy = -1; } };
    }
}
