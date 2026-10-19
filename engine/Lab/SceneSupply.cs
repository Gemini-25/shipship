using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>⑪ 공급 사슬 관찰 장면 — 원료가 모자란 디딤호에서 두 장치가 함께 망가지면: 무엇을 먼저 만들고 · 갈아 낸 부품이 정제기를 거쳐 다시 원료가 되나.</summary>
public static partial class Program
{
    private static void SupplyScene(string dir)
    {
        ulong seed = ulong.TryParse(Environment.GetEnvironmentVariable("SEED"), out ulong sd) ? sd : 5;
        var s = ShipWorld.Crewed("Didim", seed, 4, 2);
        while (Time.HourOfDay(s.Tick) < 10) s.Run(25);
        var L = s.Layout; var m = s.Get<Mind>(); var net = s.Get<Network>(); var w = s.Get<Work>(); var sm = s.Get<ShipMind>();
        // 오래 항해한 배: 갈래 부품은 다 썼고 · 원료는 작업대에 둘 · 전자 소자는 없다 · 로봇 주머니도 비었다
        string[] kinds = { "전기 부품", "기계 부품", "봉함재", "거르개", "원료", "전자 소자" };
        foreach (string it in kinds) for (int fu = 0; fu < L.Furniture.Count; fu++) m.Stock[fu * m.ItemCount + m.ItemIndex(it)] = 0;
        int bench = L.Furniture.FindIndex(f => f.Type == "Workbench");
        m.Stock[bench * m.ItemCount + m.ItemIndex("원료")] = 2;
        foreach (var a in m.Agents) if (!a.IsPerson) a.C.Spares = 0;
        var o2 = net.Devices.First(d => d.Def.Role == "o2gen"); var disp = net.Devices.First(d => d.Type == "MealDispenser");
        net.Age(o2.Id, Part.Seal, 1.0, s.Causes.Add(CauseKind.Setup, "산소 발생기의 봉함이 오래되어 굳었다", s.Tick, o2.Cell));
        net.Age(disp.Id, Part.Mechanical, 1.0, s.Causes.Add(CauseKind.Setup, "배식기 모터 베어링이 닳았다", s.Tick, disp.Cell));
        var painter = new Painter(L);
        var page = new System.Text.StringBuilder();
        int k = 0;
        string Stock() => string.Join(" · ", kinds.Concat(new[] { "헌 부품" }).Select(it => $"{it} {m.TotalOf(m.ItemIndex(it))}"));
        void Frame(int cell, string text)
        {
            int x0 = Math.Max(0, L.X(cell) - 14), y0 = Math.Max(0, L.Y(cell) - 9);
            var img = painter.Frame(s, x0, y0, 29, 19, k);
            Png.WriteRgba(Path.Combine(dir, $"{k:000}.png"), img.W, img.H, img.Px);
            page.Append($"<figure><img src=\"{k:000}.png\"><figcaption>{Time.Clock(s.Tick)} · {System.Net.WebUtility.HtmlEncode(Stock())}<br>{System.Net.WebUtility.HtmlEncode(text)}</figcaption></figure>");
            Console.WriteLine($"    {Time.Clock(s.Tick)} {text}");
            k++;
        }
        Frame(o2.Cell, $"산소 발생기 봉함 · 배식기 모터가 망가졌다 — 원료 둘");
        long end = s.Tick + Time.Hours(10), seen = s.Facts.Total;
        int logSeen = sm.Log.Count; long crafted = w.Crafted, hauled = m.Hauled, recycled = w.Recycled;
        while (s.Tick < end && k < 40)
        {
            s.Step();
            foreach (var (t, txt) in sm.Log.Skip(Math.Min(logSeen, sm.Log.Count)))
                if (txt.StartsWith("원료가 모자라") || txt.StartsWith("막힌 곳")) Frame(bench >= 0 ? L.Furniture[bench].Cells[0] : o2.Cell, $"[주컴퓨터] {txt}");
            logSeen = sm.Log.Count;
            foreach (var f in s.Facts.Since(seen))
                if (f.Kind == FactKind.Repaired && f.Other is int d && (d == o2.Id || d == disp.Id)) Frame(f.Cell, s.Causes.Text(f.Cause));
            seen = s.Facts.Total;
            if (w.Crafted > crafted) { crafted = w.Crafted; Frame(L.Furniture[bench].Cells[0], $"작업대에서 만들었다 (만든 수 {crafted})"); }
            if (m.Hauled > hauled) { hauled = m.Hauled; Frame(L.Furniture.First(f => f.Type == "Refinery").Cells[0], "헌 부품을 정제기에 넣었다"); }
            if (w.Recycled > recycled) { recycled = w.Recycled; Frame(L.Furniture.First(f => f.Type == "Refinery").Cells[0], $"정제기가 헌 부품을 녹여 원료로 (녹인 수 {recycled})"); }
        }
        Frame(disp.Cell, $"끝 — 산소 발생기 {(net.Broken[o2.Id * 4 + (int)Part.Seal] == 0 ? "고침" : "아직")} · 배식기 {(net.Broken[disp.Id * 4 + (int)Part.Mechanical] == 0 ? "고침" : "아직")} · 밀린 만들기 {sm.SupplyDeferred}");
        File.WriteAllText(Path.Combine(dir, "index.html"), $"<!doctype html><meta charset=utf-8><title>supply</title><style>body{{background:#0b0d12;color:#cfd6e4;font:13px sans-serif;display:flex;flex-wrap:wrap;gap:8px}}figure{{margin:0;width:464px}}img{{width:464px;image-rendering:pixelated}}</style>{page}");
        Console.WriteLine($"  supply: {k}장 → {dir}");
    }
}
