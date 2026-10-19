using ShipEngine;

namespace Lab;

/// <summary>관찰: 연출이 세계를 바꾸지 않나 — 같은 세계를 연출을 붙여 · 떼고 함께 굴려 시간마다 지문을 견준다. 쓰기: Lab --director-check [배] [시드] [시간]</summary>
public static partial class Program
{
    internal static void DirectorCheck(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--director-check").Skip(1).ToArray();
        string ship = rest.Length > 0 ? rest[0] : "Hwasal";
        ulong seed = rest.Length > 1 ? ulong.Parse(rest[1]) : 11;
        double hours = rest.Length > 2 ? double.Parse(rest[2]) : 24;
        Sim Make()
        {
            var lay = Layout.Ship(ship, Tables.Default);
            int beds = lay.Furniture.Count(f => f.Type == "Bed"), docks = lay.Furniture.Count(f => f.Type == "RobotDock");
            var s = ShipWorld.Crewed(ship, seed, Math.Max(2, beds), docks, 0);
            ShipWorld.Embark(s, RouteEnv());
            return s;
        }
        var a = Make(); var b = Make();
        var d = new Director(b.Layout) { KeepRing = false };
        long end = (long)(hours * Time.TicksPerHour);
        for (long t = 0; t < end; t++)
        {
            a.Step(); b.Step(); d.Observe(b);
            if (t % Time.TicksPerHour == Time.TicksPerHour - 1)
            {
                ulong ha = a.Hash(), hb = b.Hash();
                if (ha != hb) { Console.WriteLine($"  ✘ {Time.Clock(a.Tick)} 연출을 붙인 세계가 달라졌다 (지문 {ha:x} / {hb:x}) · 미리 보기 {d.Foresights}번"); return; }
            }
        }
        Console.WriteLine($"  ✔ {hours}시간 내내 같다 · 미리 보기 {d.Foresights}번");
    }
}
