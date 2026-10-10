using System;
using System.Linq;
using ShipSim.Core;
public static partial class Program
{
    private static int RunAutoDebug(int seed)
    {
        var w = DayOne(seed, "Mirinae");
        for (int t = 0; t < 48 && !w.Crew.Any(c => c.CanAct && c.IsAwake && c.SkillLevel(Skill.Electrical) >= 0.5f && WatchLog.OnShift(c, w)); t++) Run(w, SimTime.Minutes(30));
        w.Ship.RoomsOf(RoomType.Lounge).First().BreakerOff = true;
        for (int i = 0; i < 6; i++)
        {
            Run(w, SimTime.Minutes(3));
            var orders = w.Board.Open.Where(o => o.Kind == WorkKind.ManualControl).ToList();
            Console.WriteLine($"{SimTime.Clock(w.Tick)} 수동 조종 일감 {orders.Count} · {string.Join(" ", orders.Select(o => $"[{o.Assignee?.Name ?? "-"} 막힘:{o.BlockedReason} min{o.MinSkill}]"))} · 등급 {w.Automation.Level} · 위기 {Crisis.Level(w)}");
            foreach (var c in w.Crew)
                if (c.SkillLevel(Skill.Electrical) >= 0.45f) Console.WriteLine($"   {c.Name} 전기 {c.SkillLevel(Skill.Electrical):0.00} 깸 {c.IsAwake} 근무 {WatchLog.OnShift(c, w)} 일 {c.Job?.Order?.Kind.ToString() ?? c.Job?.Label ?? "-"} 자세 {c.Pose} 소원 {string.Join(",", w.Board.Open.Where(o => o.Kind == WorkKind.ManualControl).Select(o => ChoresActivity.Appeal(c, w, o, w.Paths.Flood(c.Cell, new PathProfile(c.PathProfile.HazardScale, false, true)), out _).ToString("0.00")))}");
        }
        return 0;
    }
}
