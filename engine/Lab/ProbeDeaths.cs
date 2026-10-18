using ShipEngine;

namespace Lab;

/// <summary>관찰: 재미 측정과 같은 세계로 돌려 죽음마다 원인 사슬 · 그 앞 15분 주컴퓨터 기록 · 그 사람의 생각. 쓰기: Lab --probe-deaths [배] [시드] [날수]</summary>
public static partial class Program
{
    internal static void ProbeDeaths(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--probe-deaths").Skip(1).ToArray();
        string ship = rest.Length > 0 ? rest[0] : "Didim";
        ulong seed = rest.Length > 1 ? ulong.Parse(rest[1]) : 11;
        double days = rest.Length > 2 ? double.Parse(rest[2]) : 3;
        var lay = Layout.Ship(ship, Tables.Default);
        int beds = lay.Furniture.Count(f => f.Type == "Bed"), docks = lay.Furniture.Count(f => f.Type == "RobotDock");
        var s = ShipWorld.Crewed(ship, seed, Math.Max(2, beds), docks, 0);
        if (Environment.GetEnvironmentVariable("FUN_STAY") != "1") ShipWorld.Embark(s);
        var st = s.Get<Storyteller>(); st.Enabled = true;
        var m = s.Get<Mind>(); var sm = s.Get<ShipMind>(); var life = s.Get<Life>();
        string Where(int c) => c < 0 ? "-" : $"({s.Layout.X(c)},{s.Layout.Y(c)}){(m.RoomOf(c) >= 0 ? s.Layout.Rooms[m.RoomOf(c)].Name : "")}";
        long cursor = 0, end = (long)(days * Time.Hours(24));
        for (long t = 0; t < end; t++)
        {
            s.Step();
            foreach (var f in s.Facts.Since(cursor))
            {
                if (f.Kind == FactKind.StorySeed) Console.WriteLine($"  {Time.Clock(f.Tick)} 씨앗 · {s.Causes.Story(f.Cause)}");
                if (f.Kind != FactKind.Died) continue;
                Console.WriteLine($"\n✝ {Time.Clock(f.Tick)} {Where(f.Cell)} · {s.Causes.Story(f.Cause)}");
                foreach (var x in sm.Log.Where(x => x.tick >= f.Tick - Time.Minutes(15))) Console.WriteLine($"    {Time.Clock(x.tick)} {x.text}");
            }
            cursor = s.Facts.Total;
            if (t % Time.Minutes(30) == 0 && s.Tick >= Time.Hours(double.Parse(Environment.GetEnvironmentVariable("WATCH_FROM") ?? "9999")))
            {
                var f = s.Get<Field>(); var net = s.Get<Network>();
                var rooms = s.Layout.Rooms.Where(r => m.Agents.Any(a => a.IsPerson && life.Bodies[a.C.Body].Alive != 0 && m.RoomOf(a.C.Cell) == r.Id))
                    .Select(r => $"{r.Name} O₂ {r.Cells.Average(c => f.Partial(s.Tables.gO2, c)):0.0}kPa CO₂ {r.Cells.Average(c => f.Ppm(s.Tables.gCO2, c)):0}ppm {r.Cells.Average(c => f.Pressure(c)):0}kPa");
                var o2 = net.Devices.Where(d => d.Def.Role is "o2gen" or "scrubber").Select(d => $"{d.Name}{(net.Working(d.Id) ? "○" : "×")}{(net.On[d.Id] == 0 ? "꺼짐" : "")}{(Enumerable.Range(0, 4).Any(p => net.Broken[d.Id * 4 + p] != 0) ? "고장" : "")}");
                Console.WriteLine($"  · {Time.Clock(s.Tick)} 전력 {net.Supplied:0.0}/{net.Demanded:0.0}kW · 예비 공기 {net.AirReserve:0} · {string.Join(" ", o2)} · 물 {net.WaterTank * 0.018:0}L · 배 산소 {net.PpO2:0.0}kPa · 일 몫 {string.Join("/", net.Devices.Where(d => d.Def.Role == "o2gen").Select(d => net.Duty[d.Id].ToString("0.00")))} · 공급 {string.Join("/", net.Devices.Where(d => d.Def.Role == "o2gen").Select(d => net.Supply[d.Id].ToString("0.00")))} · 구멍 {Enumerable.Range(0, s.Layout.N).Count(c => f.Hole[c] > 0)}곳 {Enumerable.Range(0, s.Layout.N).Sum(c => f.Hole[c]) * 1e4:0.0}cm² · 배 기압 {s.Layout.Rooms.Where(r => r.Cells.Length > 0).Average(r => r.Cells.Average(c => f.Pressure(c))):0}kPa · 주컴퓨터 {(sm.Online ? "켜짐" : "멈춤")} · 사건 {string.Join(",", sm.Incidents.Where(x => x.Active != 0).Select(x => x.Kind))}\n      {string.Join(" | ", rooms)}");
            }
        }
        Console.WriteLine($"\n사람 {m.Agents.Count(a => a.IsPerson)} · 산 사람 {m.Agents.Count(a => a.IsPerson && life.Bodies[a.C.Body].Alive != 0)} · 죽음 {life.Deaths}");
    }
}
