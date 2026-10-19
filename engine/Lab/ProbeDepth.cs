using ShipEngine;

namespace Lab;

public static partial class Program
{
    /// <summary>
    /// --probe-facts 배 시드 시작시 끝시 [종류,…]: 재미 측정과 같은 세계(침대마다 사람 · 충전독마다 로봇 · 항로 ROUTE (기본 4))를 돌려 그 사이의 큰 사실과 원인 사슬을 적는다.
    /// </summary>
    internal static void ProbeFacts(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--probe-facts").Skip(1).ToArray();
        string ship = rest.Length > 0 ? rest[0] : "Didim";
        ulong seed = rest.Length > 1 ? ulong.Parse(rest[1]) : 11;
        double from = rest.Length > 2 ? double.Parse(rest[2]) : 0, to = rest.Length > 3 ? double.Parse(rest[3]) : 24;
        var kinds = rest.Length > 4 ? rest[4].Split(',').Select(Enum.Parse<FactKind>).ToHashSet() : null;
        var lay = Layout.Ship(ship, Tables.Default);
        int beds = lay.Furniture.Count(f => f.Type == "Bed"), docks = lay.Furniture.Count(f => f.Type == "RobotDock");
        var s = ShipWorld.Crewed(ship, seed, Math.Max(2, beds), docks, 0);
        ShipWorld.Embark(s, RouteEnv());
        var mind = s.Get<Mind>();
        long cursor = 0;
        while (s.Tick < Time.Hours(to))
        {
            s.Step();
            foreach (var f in s.Facts.Since(cursor))
            {
                if (f.Tick < Time.Hours(from)) continue;
                if (kinds != null ? !kinds.Contains(f.Kind) : Director.Weight(f.Kind) < 12) continue;
                string who = f.Actor >= 0 && f.Actor < mind.Agents.Count ? mind.NameOf(f.Actor) + " " : "";
                string room = f.Cell >= 0 && lay.Room[f.Cell] >= 0 ? lay.Rooms[lay.Room[f.Cell]].Name : "";
                Console.WriteLine($"  {Time.Clock(f.Tick)} {f.Kind} {who}{room} 크기 {f.Size:0.##} · {s.Causes.Story(f.Cause)}");
            }
            cursor = s.Facts.Total;
        }
    }
}
