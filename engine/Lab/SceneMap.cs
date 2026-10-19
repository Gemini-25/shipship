using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>⑤ 별계 지도 관찰 장면 — 추진제가 모자란 디딤호: 주컴퓨터가 붉은 사막 대신 띠 보급소를 고르고 · 창에 떠나 · 닿아 채우고 · 다시 임무로.</summary>
public static partial class Program
{
    private static void MapScene(string dir)
    {
        ulong seed = ulong.TryParse(Environment.GetEnvironmentVariable("SEED"), out ulong sd) ? sd : 5;
        var s = ShipWorld.Crewed("Didim", seed, 4, 2);
        while (Time.HourOfDay(s.Tick) < 10) s.Run(25);
        var L = s.Layout; var o = s.Get<Outside>(); var sm = s.Get<ShipMind>();
        ShipWorld.Embark(s, 4, 60);
        o.PropellantKg = 0.45 * o.PropellantStart;   // 지난 항해에 많이 썼다
        int st = o.PlaceOf("띠 보급소"), mars = o.PlaceOf("붉은 사막");
        o.AlignWindow(st, s.Tick + Time.Hours(2));   // 보급소 창이 곧 열린다
        var painter = new Painter(L);
        var page = new System.Text.StringBuilder();
        int k = 0, cell = L.Furniture.First(f => f.Type == "MainComputer").Cells[0];
        string State() => $"{o.Distance:0.000}AU · 추진제 {o.PropellantKg / o.PropellantStart * 100:0}% (Δv {o.DvLeft / 1000:0.0}km/s) · 머문 곳 {(o.AtPlace >= 0 ? o.Places[o.AtPlace].Name : "전이 궤도")}";
        void Frame(string text)
        {
            int x0 = Math.Max(0, L.X(cell) - 14), y0 = Math.Max(0, L.Y(cell) - 9);
            var img = painter.Frame(s, x0, y0, 29, 19, k);
            Png.WriteRgba(Path.Combine(dir, $"{k:000}.png"), img.W, img.H, img.Px);
            page.Append($"<figure><img src=\"{k:000}.png\"><figcaption>{Time.Clock(s.Tick)} · {System.Net.WebUtility.HtmlEncode(State())}<br>{System.Net.WebUtility.HtmlEncode(text)}</figcaption></figure>");
            Console.WriteLine($"    {Time.Clock(s.Tick)} {text} — {State()}");
            k++;
        }
        Frame("임무: 붉은 사막 — 출발 점화까지 한 시간 · 지난 항해에 추진제를 많이 썼다");
        int seen = sm.Log.Count;
        void Watch(long ticks, Func<string, bool> pick)
        {
            long end = s.Tick + ticks;
            while (s.Tick < end && k < 40)
            {
                s.Step();
                foreach (var (t, txt) in sm.Log.Skip(Math.Min(seen, sm.Log.Count))) if (pick(txt)) Frame("[주컴퓨터] " + txt);
                seen = sm.Log.Count;
            }
        }
        Watch(Time.Hours(3), t => t.StartsWith("다음 들를 곳") || t.Contains("점화") || t.Contains("전이 궤도"));
        // 전이 궤도의 나날은 건너뛴다 — 닿기 바로 전으로
        if (o.VoyageTicks > 0)
        {
            // 건너뛴 만큼 정해진 점화도 당긴다 (중간 고침은 했다고 치고 · 도착 점화는 곧)
            long skip = o.VoyageStart + o.VoyageTicks - s.Tick - Time.Minutes(10);
            o.VoyageStart -= skip;
            for (int i = o.Plan.Count - 1; i >= 0; i--) { var p = o.Plan[i]; if (p.Kind == BurnKind.Correct) { o.TakePlan(i); continue; } p.Due -= skip; p.Deadline -= skip; o.SetPlan(i, p); }
            // 떠난 때를 당겼으니 보급소도 그때 창에 있던 자리로 (건너뛰기만의 손질 — 실제 항해는 창에 떠난 그대로 닿는다)
            o.PlaceTheta0[st] = o.ShipTheta(o.VoyageStart) + Outside.LeadAngle(o.VoyageFrom, o.VoyageTo) - Outside.Mean(o.VoyageTo) * o.VoyageStart;
            Frame($"…{Outside.VoyageDays(1, o.VoyageTo):0}일 뒤 — 띠 보급소에 닿을 때");
        }
        Watch(Time.Hours(5), t => t.Contains("정거장") || t.Contains("보급소") || t.Contains("부탁") || t.Contains("받았다") || t.Contains("채웠다") || t.StartsWith("다음 들를 곳") || t.Contains("도착 점화"));
        Frame($"끝 — 닿은 곳 {o.PlacesReached} · 받은 추진제 {o.FuelTaken / 1000:0}t · 다음: {sm.NextLine}");
        File.WriteAllText(Path.Combine(dir, "index.html"), $"<!doctype html><meta charset=utf-8><title>map</title><style>body{{background:#0b0d12;color:#cfd6e4;font:13px sans-serif;display:flex;flex-wrap:wrap;gap:8px}}figure{{margin:0;width:464px}}img{{width:464px;image-rendering:pixelated}}</style>{page}");
        Console.WriteLine($"  map: {k}장 → {dir}");
    }
}
