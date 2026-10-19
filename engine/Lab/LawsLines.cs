using ShipEngine;

namespace Lab;

/// <summary>⑥ 칸마다 선 · 관의 법칙: 아무것도 안 끊긴 배는 하나로 이어진다 (섬 하나 · 소비처마다 관이 닿는다) · 끊긴 칸 너머로는 흐르지 않는다 (따로 센 이어짐과 같다).</summary>
public static partial class Program
{
    private static void LineLaws()
    {
        if (!Do("선과 관")) return;
        Console.WriteLine("\n[⑥ 칸마다 선 · 관]");
        // ── 배 일곱 척: 처음에는 모두 이어져 있다 ──
        var bad = new List<string>();
        foreach (var key in new[] { "Hwasal", "Didim", "Salpim", "Nareum", "Soedol", "Saetteul", "Hannuri" })
        {
            var s = ShipWorld.Create(key, 1);
            var net = s.Get<Network>(); var L = s.Layout;
            s.Run(1);
            if (net.Islands != 1) bad.Add($"{L.Name} 섬 {net.Islands}");
            foreach (var d in net.Devices)
            {
                if (d.Feeder >= 0 && d.Def.Role is not ("reactor" or "aux" or "battery" or "panel") && net.DropPath[d.Id].Length == 0) bad.Add($"{L.Name} {d.Name} 가지선 없음");
                if (d.Def.Role is "reactor" or "computer" or "industry" && net.Devices.Any(p => p.Def.Role == "pump") && net.CoolComp(d.Id) is int cc && (cc < 0 || !net.Devices.Any(p => p.Def.Role == "pump" && net.CoolComp(p.Id) == cc)))
                    bad.Add($"{L.Name} {d.Name} 냉각관이 펌프에 안 닿음");
            }
            foreach (var fu in L.Furniture) if (!net.WaterAt(fu.Id)) bad.Add($"{L.Name} {fu.Type} 물이 안 옴");
            if (Math.Abs(net.Supplied - net.Demanded) > 1e-6) bad.Add($"{L.Name} 공급 {net.Supplied:0.0}/{net.Demanded:0.0}");
        }
        Check("배 일곱 척: 아무것도 안 끊기면 섬 하나 · 장치마다 가지선 · 원자로 · 냉각판 장치는 펌프와 이어진 냉각관 · 물 쓰는 설비마다 물이 온다 · 공급이 수요를 채운다",
            bad.Count == 0, string.Join(" · ", bad.Take(6)));

        // ── 끊긴 칸 너머로는 흐르지 않는다: 디딤호 관 칸을 마구 끊고 · 따로 센 이어짐(관 방향을 따라 끊긴 칸을 빼고)과 견준다 ──
        {
            var s = ShipWorld.Create("Didim", 2);
            var net = s.Get<Network>(); var L = s.Layout;
            s.Run(1);
            var pipeCells = Enumerable.Range(0, L.N).Where(c => (net.LineAt[c] & 0b110) != 0).ToList();
            int cause = s.Causes.Add(CauseKind.Setup, "시험: 관을 끊는다", s.Tick);
            for (int k = 0; k < 9; k++) net.CutLines(s, pipeCells[(int)(Rng.Unit(7, Rng.Key("법칙 관 끊기"), k, 0) * pipeCells.Count)], 0b110, cause);
            bool Reach(LineKind kind, IEnumerable<int> from, int to)
            {
                byte bit = Network.Bit(kind);
                var seen = new HashSet<int>(); var q = new Queue<int>();
                foreach (int f in from) if ((net.LineCut[f] & bit) == 0 && (net.LineAt[f] & bit) != 0 && seen.Add(f)) q.Enqueue(f);
                while (q.Count > 0)
                {
                    int c = q.Dequeue();
                    if (c == to) return true;
                    int dirs = net.LineDir[c * 4 + (int)kind];
                    for (int d = 0; d < 4; d++)
                    {
                        if ((dirs & (1 << d)) == 0) continue;
                        int j = d switch { 0 => c - L.W, 1 => c + 1, 2 => c + L.W, _ => c - 1 };
                        if ((net.LineCut[j] & bit) == 0 && seen.Add(j)) q.Enqueue(j);
                    }
                }
                return false;
            }
            int wrong = 0, checkedN = 0;
            var tanks = L.Furniture.Where(fu => fu.Type == "WaterRecycler").Select(fu => fu.Cells[0]).ToList();
            foreach (var fu in L.Furniture)
            {
                int c = fu.Cells[0];
                if ((net.LineAt[c] & Network.Bit(LineKind.Water)) == 0) continue;
                checkedN++;
                if (net.WaterAt(fu.Id) != Reach(LineKind.Water, tanks, c)) wrong++;
            }
            var pumps = net.Devices.Where(d => d.Def.Role == "pump").ToList();
            foreach (var d in net.Devices.Where(d => d.Def.Role is "reactor" or "computer" or "industry"))
                foreach (var p in pumps)
                {
                    checkedN++;
                    bool same = net.CoolComp(d.Id) >= 0 && net.CoolComp(d.Id) == net.CoolComp(p.Id);
                    if (same != Reach(LineKind.Coolant, new[] { p.Cell }, d.Cell)) wrong++;
                }
            Check("끊긴 칸 너머로는 흐르지 않는다: 관 아홉 칸을 끊은 디딤호 — 물이 오나 · 펌프와 이어졌나가 관 방향을 따라 따로 센 이어짐과 모두 같다",
                wrong == 0 && checkedN > 10, $"견준 쌍 {checkedN} · 다름 {wrong} · 끊긴 칸 {Enumerable.Range(0, L.N).Count(c => net.LineCut[c] != 0)}");
        }
    }
}
