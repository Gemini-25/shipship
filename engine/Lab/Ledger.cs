using ShipEngine;

namespace Lab;

/// <summary>⑪ 물자 장부 보기 (--ledger): 사람을 태운 배를 며칠 돌리며 여섯 시간마다 장부 한 장 · 장부에 없는 몫 · 산소 분압 · 물의 갈래.</summary>
public static partial class Program
{
    /// <summary>--ledger [배] [날] [시드] [사람] [로봇]</summary>
    private static void LedgerRun(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--ledger").Skip(1).ToArray();
        string ship = rest.Length > 0 ? rest[0] : "Kestrel";
        double days = rest.Length > 1 ? double.Parse(rest[1]) : 2;
        ulong seed = rest.Length > 2 ? ulong.Parse(rest[2]) : 3;
        int people = rest.Length > 3 ? int.Parse(rest[3]) : 6, robots = rest.Length > 4 ? int.Parse(rest[4]) : 2;
        var s = ShipWorld.Crewed(ship, seed, people, robots);
        var work = s.Get<Work>(); var net = s.Get<Network>(); var f = s.Get<Field>(); var life = s.Get<Life>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (long t = 1; t <= Time.Days(days); t++)
        {
            s.Step();
            if (t % Time.Hours(6) != 0) continue;
            Console.WriteLine($"── {Time.Clock(s.Tick)} · {ship} {people}명 · 산소 분압 {net.PpO2:0.00}kPa (발생기 {net.Duty.Where((_, d) => net.Devices[d].Def.Role == "o2gen").DefaultIfEmpty(0).Average():0.00}) · 산 사람 {life.Bodies.Count(b => b.Alive != 0)}");
            foreach (var line in work.Report(s)) Console.WriteLine("   " + line);
            Console.WriteLine($"   물의 갈래: 탱크 {net.WaterTank * 0.018:0}L · 오수 {net.WasteWater * 0.018:0.0}L · 버린 소금물 {net.Brine * 0.018:0.0}L · 공기 습기 {f.Total(f.T.gH2O) * 0.018:0.0}L · 모은 이산화탄소 {net.CO2Store * 0.044:0.00}kg");
            var b = s.Book;
            string F(Res r, Flow fl) => $"{b.Of(r, fl) * 0.018:+0.0;-0.0}";
            Console.WriteLine($"   물 흐름(L): 마심 {F(Res.Water, Flow.Drunk)} · 가름 {F(Res.Water, Flow.Split)} · 사바티에 {F(Res.Water, Flow.Sabatier)} · 재활용 {F(Res.Water, Flow.Recycled)} · 습기 {F(Res.Water, Flow.Condensed)} · 작물 {F(Res.Water, Flow.Watered)} · 샘 {F(Res.Water, Flow.Leaked)}");
            Console.WriteLine($"   장부에 없는 몫: 식량 {work.Other(s, 0):0.000}몰 · 물 {work.Other(s, 1):0.000}몰 · 산소 {work.Other(s, 2):0.0}몰 · 물건 {string.Join(" ", Enumerable.Range(0, b.Items).Select(i => $"{work.Other(s, Book.Bulk + i):0}"))}");
        }
        Console.WriteLine($"({sw.Elapsed.TotalSeconds:0.0}초)");
    }
}
