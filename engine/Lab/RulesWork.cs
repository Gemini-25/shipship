using ShipEngine;

namespace Lab;

/// <summary>⑪ 일과 물자 규칙 시험 — 일감판 · 만들기 · 예방 정비 · 청소 · 저절로 만드는 설비.</summary>
public static partial class Program
{
    private static void WorkRules()
    {
        if (!Do("일과물자")) return;
        Console.WriteLine("\n[일과 물자]");
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);

        // ── 모자란 물건: 일감판이 로봇에게 맡기고 → 작업대에서 원료로 만든다 ──
        {
            var s = Worlds.Full(lay, 7);
            var m = s.Get<Mind>(); var w = s.Get<Work>();
            m.AddRobot(s, At(8, 7), AgentKind.Robot, At(8, 7));
            int bench = s.Layout.Furniture.FindIndex(f => f.Type == "Workbench");
            int ore = m.ItemIndex("원료"), patch = m.ItemIndex("봉합 꾸러미");
            m.Stock[bench * m.ItemCount + ore] = 4;
            int before = m.TotalOf(patch);
            s.Run(Time.Hours(3));
            Check("모자란 봉합 꾸러미: 일감판이 로봇에게 맡겨 작업대에서 원료로 만든다", m.TotalOf(patch) > before && m.TotalOf(ore) < 4 && w.Crafted > 0,
                $"봉합 꾸러미 {before} → {m.TotalOf(patch)} · 원료 4 → {m.TotalOf(ore)} · 만든 것 {w.Crafted}");
        }

        // ── 예방 정비: 표준 수명의 80%를 쓴 부품은 고장 나기 전에 간다 (숨은 한계는 모른다) ──
        {
            var s = Worlds.Full(lay, 7);
            var m = s.Get<Mind>(); var net = s.Get<Network>(); var w = s.Get<Work>();
            m.AddRobot(s, At(8, 7), AgentKind.Robot, At(8, 7));
            int dev = -1, part = -1;
            foreach (var d in net.Devices)
                for (int p = 0; p < 4 && dev < 0; p++)
                {
                    double life = d.Life((Part)p);
                    if (life <= 0) continue;
                    net.Wear[d.Id * 4 + p] = (float)(0.85 * life);
                    if (net.WearFraction(d.Id, (Part)p) < 0.95f) { dev = d.Id; part = p; }
                    else net.Wear[d.Id * 4 + p] = 0;
                }
            s.Run(Time.Hours(2));
            var fact = s.Facts.Since(0).Where(f => f.Kind == FactKind.Repaired).Select(f => s.Causes.Text(f.Cause)).FirstOrDefault(t => t.Contains("미리 갈았다")) ?? "";
            Check("예방 정비: 표준 수명의 80%를 쓴 부품은 고장 나기 전에 로봇이 미리 간다", dev >= 0 && net.Wear[dev * 4 + part] < 100 && net.Broken[dev * 4 + part] == 0 && w.Maintained > 0,
                dev < 0 ? "알맞은 부품이 없다" : $"{fact} · 닳음 {net.WearFraction(dev, (Part)part):0.00}");
        }

        // ── 곰팡이: 짙어지면 닦는다 ──
        {
            var s = Worlds.Full(lay, 7);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var w = s.Get<Work>();
            m.AddRobot(s, At(8, 7), AgentKind.Robot, At(8, 7));
            int c = At(17, 3);
            life.Mold[c] = 0.6f;
            s.Run(Time.Hours(1));
            Check("곰팡이가 짙어지면 일감판이 맡겨 닦아 낸다", life.Mold[c] < 0.05f && w.Cleaned > 0, $"곰팡이 0.6 → {life.Mold[c]:0.00} · 닦은 곳 {w.Cleaned}");
        }

        // ── 정제기: 전기를 먹고 원료를 조금씩 만든다 ──
        {
            var s = ShipWorld.Create("Kestrel", 7);
            var m = s.Get<Mind>(); var w = s.Get<Work>();
            int ore = m.ItemIndex("원료");
            int before = m.TotalOf(ore);
            s.Run(Time.Hours(5));
            Check("정제기는 전기를 먹고 원료를 조금씩 만든다 (많아야 8)", w.Made > 0 && m.TotalOf(ore) > before, $"원료 {before} → {m.TotalOf(ore)} · 만든 것 {w.Made}");
        }
    }
}
