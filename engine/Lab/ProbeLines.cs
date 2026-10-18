using ShipEngine;

namespace Lab;

/// <summary>⑥ 칸마다 선 · 관 요약 (Lab --lines [배]): 섬 · 간선 · 급전선 · 가지선 · 냉각관 · 물관 — 이어지지 않은 소비처를 찾는다.</summary>
public static partial class Program
{
    private static void ProbeLines(string[] args)
    {
        int at = Array.IndexOf(args, "--lines");
        var keys = at + 1 < args.Length && !args[at + 1].StartsWith("--") ? new[] { args[at + 1] } : new[] { "Hwasal", "Didim", "Salpim", "Nareum", "Soedol", "Saetteul", "Hannuri" };
        ulong seed = at + 2 < args.Length && ulong.TryParse(args[at + 2], out var sd) ? sd : 1;
        foreach (var key in keys)
        {
            var s = ShipWorld.Create(key, seed);
            var net = s.Get<Network>(); var L = s.Layout;
            s.Run(2);
            string Name(int d) => $"{net.Devices[d].Name}#{d}";
            int drops = net.Devices.Count(d => net.DropPath[d.Id].Length > 0), feedable = net.Devices.Count(d => d.Feeder >= 0 && d.Def.Role is not ("reactor" or "aux" or "battery" or "panel"));
            int power = Enumerable.Range(0, L.N).Count(c => (net.LineAt[c] & 1) != 0), cool = Enumerable.Range(0, L.N).Count(c => (net.LineAt[c] & 2) != 0), water = Enumerable.Range(0, L.N).Count(c => (net.LineAt[c] & 4) != 0);
            Console.WriteLine($"[{L.Name}] 섬 {net.Islands} · 간선 {net.Buses.Count} · 급전선 {net.Feeders.Count} · 가지선 {drops}/{feedable} · 전선 칸 {power} · 냉각관 칸 {cool} · 물관 칸 {water} · 공급 {net.Supplied:0.0}/{net.Demanded:0.0}kW · 원자로 {net.ReactorKw:0.0}/{net.ReactorMaxKw:0.0}");
            foreach (var b in net.Buses) Console.WriteLine($"   간선 {Name(b.A)} → {Name(b.B)} {b.Path.Length}칸 · 섬 {net.IslandOf(b.A)}/{net.IslandOf(b.B)}");
            foreach (var d in net.Devices.Where(d => d.Def.Role is "reactor" or "aux" or "battery" or "panel")) Console.WriteLine($"   {Name(d.Id)} 섬 {net.IslandOf(d.Id)}");
            foreach (var d in net.Devices.Where(d => d.Def.Role is "pump" or "reactor" or "computer" or "industry")) Console.WriteLine($"   냉각 {Name(d.Id)} 조각 {net.CoolComp(d.Id)}");
            var dry = L.Furniture.Where(fu => !net.WaterAt(fu.Id)).Select(fu => fu.Type).ToList();
            if (dry.Count > 0) Console.WriteLine($"   물이 안 오는 설비: {string.Join(", ", dry)}");
            var noDrop = net.Devices.Where(d => d.Feeder >= 0 && d.Def.Role is not ("reactor" or "aux" or "battery" or "panel") && net.DropPath[d.Id].Length == 0).Select(d => d.Name).ToList();
            if (noDrop.Count > 0) Console.WriteLine($"   가지선 없는 장치: {string.Join(", ", noDrop)}");
        }
    }
}

public static partial class Program
{
    /// <summary>Lab --lines-facts 배 시드 분 [stuck|nopump] — 그 장면의 망 사실을 차례로 (선 · 관 · 합선 · 차단기 · 원자로).</summary>
    private static void ProbeLinesFacts(string[] args)
    {
        int at = Array.IndexOf(args, "--lines-facts");
        string key = args[at + 1]; ulong seed = ulong.Parse(args[at + 2]); double min = double.Parse(args[at + 3]); string mode = args.Length > at + 4 ? args[at + 4] : "";
        var s = ShipWorld.Create(key, seed);
        var net = s.Get<Network>(); var f = s.Get<Field>();
        if (mode == "stuck")
        {
            var fd = net.Feeders.First(x => x.Path.Length > 4);
            int wet = fd.Path[fd.Path.Length / 2];
            net.BreakerStuck[fd.Id] = 1; f.AddStore(wet, f.T.sPoly, 300); f.AddStore(wet, f.T.sWater, 30);
            Console.WriteLine($"급전선 {fd.Id} · 젖은 칸 {wet}");
        }
        if (mode == "nopump") for (int d = 0; d < net.Devices.Count; d++) if (net.Devices[d].Def.Role == "pump") net.SetOn(d, false);
        int steps = (int)Time.Minutes(min);
        int watch = mode == "stuck" ? net.Feeders.First(x => x.Path.Length > 4).Path[net.Feeders.First(x => x.Path.Length > 4).Path.Length / 2] : -1;
        var chem = s.Get<Chemistry>();
        for (int i = 0; i < steps; i++)
        {
            s.Step();
            if (watch >= 0 && i < 30) Console.WriteLine($"    틱 {s.Tick} 기체 {f.Tf[watch]:0}K 겉 {f.Ts[watch]:0}K · 물 {f.Stored(f.T.sWater, watch):0.0} · 고분자 {f.Stored(f.T.sPoly, watch):0.0} · 산소 {f.Partial(f.T.gO2, watch):0.0}kPa · 불 {chem.Burning[watch]} · 샘 {net.LeakAt(watch, LineKind.Water):0.0} · 합선 칸 {string.Join(",", net.FaultCell)}");
            if (i % 250 == 0) Console.WriteLine($"  {Time.Clock(s.Tick)} 공급 {net.Supplied:0.0}/{net.Demanded:0.0} · 원자로 {net.ReactorKw:0.0}/{net.ReactorMaxKw:0.0} · 섬 {net.Islands} · 스크램 {net.ReactorScram} · 원자로 칸 {f.Ts[net.Devices.First(d => d.Def.Role == "reactor").Cell]:0}K");
        }
        foreach (var x in s.Facts.Since(0).Where(x => x.Kind is FactKind.Short or FactKind.BreakerTrip or FactKind.CableCut or FactKind.PipeLeak or FactKind.Overheat or FactKind.Ignition).Take(40))
            Console.WriteLine($"  {Time.Clock(x.Tick)} {FactNames.Of(x.Kind)} 칸 {x.Cell} · {s.Causes.Story(x.Cause)}");
    }
}

public static partial class Program
{
    /// <summary>선 · 관 글 지도: 칸마다 전선 가닥 수(1~9) · 냉각관 c · 물관 w · 둘 다 x · 끊김 ! (전선 끊김 우선) — 시험 · 조사용.</summary>
    internal static string LinesMap(Network net, Layout L, LineKind show = LineKind.Power)
    {
        var sb = new System.Text.StringBuilder();
        for (int y = 0; y < L.H; y++)
        {
            for (int x = 0; x < L.W; x++)
            {
                int c = L.Index(x, y);
                char ch = L.Kind[c] switch { CellKind.Wall => '#', CellKind.Door => 'D', CellKind.Outside => ' ', _ => '.' };
                byte bit = Network.Bit(show);
                if ((net.LineCut[c] & bit) != 0) ch = '!';
                else if (show == LineKind.Power && net.Strands[c] > 0) ch = (char)('0' + Math.Min(9, (int)net.Strands[c]));
                else if (show != LineKind.Power && (net.LineAt[c] & bit) != 0) ch = show == LineKind.Coolant ? 'c' : show == LineKind.Water ? 'w' : 'd';
                sb.Append(ch);
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
