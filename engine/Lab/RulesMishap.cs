using ShipEngine;

namespace Lab;

/// <summary>
/// 일상 물리 (5단 C8 ~ — docs/STAGE5.md): 고인 물의 원인 · 흔들림 · 쏟음 · 미끄러짐 … 작은 배 · 몇 분.
/// </summary>
public static partial class Program
{
    private static void MishapRules()
    {
        if (!Do("일상 물리")) return;
        Console.WriteLine("\n[일상 물리 — 고인 물의 원인]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";

        // ── C8 고인 물 원인: 지린 칸과 퍼진 이웃 칸의 원인 = 지림 마디 · 닦아 마르면 0 · 불 끄기 물은 뿌린 까닭을 단다 ──
        {
            var legend = new Dictionary<char, string> { ['e'] = "Bed", ['T'] = "Toilet", ['S'] = "Shower", ['k'] = "Sink", ['p'] = "PowerPanel", ['b'] = "Battery" };
            var lay = Worlds.Ascii(WashShip, legend);
            var s = Worlds.Full(lay, 7);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var f = s.Get<Field>();
            int p = m.AddPerson(s, Worlds.At(lay, 3, 2));
            s.Run(2);
            life[m[p].C.Body].Bladder = Life.BladderMax + 400;   // 넘친 몫이 얇은 막(55몰)을 넘어 이웃 칸으로 퍼진다
            for (int k = 0; k < 30 && life.Soilings == 0; k++) s.Step();
            int soil = life[m[p].C.Body].SoilCause, at = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Soiled).Cell;
            s.Run(40);
            var wet = Enumerable.Range(0, lay.N).Where(c => f.Stored(f.T.sWater, c) > 5).ToList();
            int sameCause = wet.Count(c => f.WetCause[c] == soil), atCause = f.WetCause[at];
            if (Debug) foreach (int c in wet) Console.WriteLine($"      칸 ({lay.X(c)},{lay.Y(c)}) 물 {f.Stored(f.T.sWater, c):0.0}몰 · 원인 {f.WetCause[c]}");
            for (int c = 0; c < lay.N; c++) f.TakeStore(c, f.T.sWater, f.Stored(f.T.sWater, c));   // 닦았다
            s.Run(Field.SlowEvery);
            int left = Enumerable.Range(0, lay.N).Count(c => f.WetCause[c] != 0);
            int douse = s.Causes.Add(CauseKind.Setup, "불을 끄려고 소화기 물을 뿌렸다", s.Tick);
            int fire = Worlds.At(lay, 12, 2);
            s.Get<Chemistry>().Douse(fire, Extinguishant.Water, 30, douse);
            Check("고인 물 원인: 지린 칸과 퍼진 이웃 칸의 원인 = 지림 마디 · 닦아 마르면 지운다 · 불 끄기 물은 뿌린 까닭",
                soil != 0 && wet.Count >= 3 && sameCause == wet.Count && atCause == soil && left == 0 && f.WetCause[fire] == douse,
                $"젖은 칸 {wet.Count} · 지림 원인 {sameCause} · 닦은 뒤 원인 남은 칸 {left} · 불 끄기 물 {(f.WetCause[fire] == douse ? "원인 있음" : "모름")}");
        }

        // ── C8 잇기는 마른 칸만 (검토가 찾음): 원인을 모르는 물이 이미 고인 칸은 원인 있는 이웃에서 조금 받아도 그 원인을 잇지 않는다 · 곁의 마른 칸은 잇는다 ──
        {
            var lay = Worlds.Ascii(WashShip, new Dictionary<char, string> { ['e'] = "Bed", ['T'] = "Toilet", ['S'] = "Shower", ['k'] = "Sink", ['p'] = "PowerPanel", ['b'] = "Battery" });
            var s = Worlds.Full(lay, 7);
            var f = s.Get<Field>();
            int src = Worlds.At(lay, 11, 1), old = Worlds.At(lay, 12, 1), dry = Worlds.At(lay, 10, 1);
            int leak = s.Causes.Add(CauseKind.Leak, "샌 물관 — 이음매가 헐거웠다", s.Tick, src);
            f.AddStore(src, f.T.sWater, 400, leak);
            f.AddStore(old, f.T.sWater, 120);   // 원인을 모르는 물 (원인 없이 고인)
            s.Run(Field.SlowEvery * 3);
            double got = f.Stored(f.T.sWater, old) - 120;
            Check("고인 물 원인: 원인을 모르는 물이 고인 칸은 원인 있는 이웃에서 물을 받아도 원인을 잇지 않고 · 마른 이웃 칸은 잇는다 (마른 칸이 젖을 때만)",
                got > 0 && f.WetCause[old] == 0 && f.Stored(f.T.sWater, dry) > Field.DryMol && f.WetCause[dry] == leak,
                $"고인 칸 받은 물 {got:0.0}몰 · 원인 {(f.WetCause[old] == 0 ? "모름" : f.WetCause[old] == leak ? "샌 물관" : f.WetCause[old])} · 마른 칸 물 {f.Stored(f.T.sWater, dry):0.0}몰 · 원인 {(f.WetCause[dry] == leak ? "샌 물관" : f.WetCause[dry])}");
        }

        // ── C8 샌 물관 → 합선: 샌 물관 물이 고인 칸의 전선이 합선되면 원인 사슬이 샌 물관까지 이어진다 ──
        {
            var s = ShipWorld.Create("Didim", 5);
            var n = s.Get<Network>(); var f = s.Get<Field>();
            byte wt = Network.Bit(LineKind.Water);
            var (fd, cell) = n.Feeders.SelectMany(x => x.Path.Select(c => (x, c))).FirstOrDefault(t => (n.LineAt[t.c] & wt) != 0 && s.Layout.Room[t.c] >= 0);
            int leak = s.Causes.Add(CauseKind.Leak, "샌 물관 — 이음매가 헐거웠다", s.Tick, cell);
            n.LeakPipe(s, cell, LineKind.Water, 1, leak);
            Fact shortF = default;
            for (int k = 0; k < Time.Minutes(10) && shortF.Kind != FactKind.Short; k++) { s.Step(); shortF = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Short); }
            var chain = shortF.Kind == FactKind.Short ? s.Causes.Chain(shortF.Cause) : new List<int>();
            Check("고인 물 원인: 샌 물관 물에 닿은 전선이 합선되면 원인 사슬에 '샌 물관'",
                fd != null && shortF.Kind == FactKind.Short && chain.Contains(leak) && f.WetCause[cell] == leak,
                $"샌 칸 {cell} ({s.Layout.Kind[cell]}) · 합선 {(shortF.Kind == FactKind.Short ? $"{Time.ToSeconds(shortF.Tick):0}초 · 칸 {shortF.Cell} 물 {f.Stored(f.T.sWater, shortF.Cell):0}몰" : "없음")} · 샌 칸 물 {f.Stored(f.T.sWater, cell):0.0}몰 · \"{(shortF.Kind == FactKind.Short ? s.Causes.Story(shortF.Cause) : "-")}\"");
        }
    }
}
