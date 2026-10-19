using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>④ 열 · 진동 관찰 장면 — 디딤호의 닳은 펌프가 떨다 볼트가 풀려 덜컹거리고 · 곁 벽의 금이 자라다 · 주컴퓨터가 로봇을 보내 조이면 멎는가.</summary>
public static partial class Program
{
    private static void VibrationScene(string dir)
    {
        ulong seed = ulong.TryParse(Environment.GetEnvironmentVariable("SEED"), out ulong sd) ? sd : 5;
        var s = ShipWorld.Crewed("Didim", seed, 4, 2);
        while (Time.HourOfDay(s.Tick) < 10) s.Run(25);
        var L = s.Layout; var m = s.Get<Mind>(); var net = s.Get<Network>(); var st = s.Get<Structure>(); var sm = s.Get<ShipMind>();
        // 기계 부품이 있는 장치 가운데 가장 세게 떠는 갈래 (펌프 · 엔진 · 보조 발전기 …) — 곁 칸에 벽이 있는 것
        int Near(int cell)
        {
            int best = -1, bd = int.MaxValue;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                int x = L.X(cell) + dx, y = L.Y(cell) + dy;
                if (x < 0 || y < 0 || x >= L.W || y >= L.H) continue;
                int c = L.Index(x, y);
                if (L.Kind[c] == CellKind.Wall && !L.Hull[c] && Math.Abs(dx) + Math.Abs(dy) < bd) { bd = Math.Abs(dx) + Math.Abs(dy); best = c; }
            }
            return best;
        }
        var dev = net.Devices.Where(d => d.Has(Part.Mechanical) && net.Working(d.Id) && Near(d.Cell) >= 0).OrderByDescending(d => Network.VibRole(d.Def.Role)).First();
        int wall = Near(dev.Cell);
        net.Age(dev.Id, Part.Mechanical, 0.7, s.Causes.Add(CauseKind.Setup, $"{dev.Name}의 베어링이 오래 돌아 닳았다", s.Tick, dev.Cell));
        net.Loose[dev.Id] = 0.97f;
        st.SeedCrack(s, wall, 0.05, s.Causes.Add(CauseKind.Setup, "지난 항해에 부딪혀 칸막이에 금이 갔다", s.Tick, wall));
        var painter = new Painter(L);
        var page = new System.Text.StringBuilder();
        int k = 0;
        string State() => $"진동 {net.VibMPa(dev.Id):0.0}MPa · 풀림 {Math.Min(1, net.Loose[dev.Id]) * 100:0}% · 벽의 금 {st.Crack[wall] * 1000:0.00}mm · 모터 닳음 {net.WearFraction(dev.Id, Part.Mechanical) * 100:0.0}%";
        void Frame(string text)
        {
            int x0 = Math.Max(0, L.X(dev.Cell) - 14), y0 = Math.Max(0, L.Y(dev.Cell) - 9);
            var img = painter.Frame(s, x0, y0, 29, 19, k);
            Png.WriteRgba(Path.Combine(dir, $"{k:000}.png"), img.W, img.H, img.Px);
            page.Append($"<figure><img src=\"{k:000}.png\"><figcaption>{Time.Clock(s.Tick)} · {System.Net.WebUtility.HtmlEncode(State())}<br>{System.Net.WebUtility.HtmlEncode(text)}</figcaption></figure>");
            Console.WriteLine($"    {Time.Clock(s.Tick)} {text} — {State()}");
            k++;
        }
        Frame($"{dev.Name}의 베어링이 닳아 떤다 · 곁 칸막이에 5cm 금");
        long end = s.Tick + Time.Hours(8), seen = s.Facts.Total, nextLook = s.Tick + Time.Hours(1);
        int worker = -1;
        while (s.Tick < end && k < 30)
        {
            s.Step();
            foreach (var f in s.Facts.Since(seen))
            {
                if (f.Kind == FactKind.Rattle && f.Other == dev.Id) Frame(s.Causes.Text(f.Cause));
                if (f.Kind == FactKind.Repaired && f.Other == dev.Id && f.Size == 10) Frame(s.Causes.Text(f.Cause));
            }
            seen = s.Facts.Total;
            var a = m.Agents.FirstOrDefault(x => x.C.Goal == GoalKind.Tighten);
            if (a != null && a.Id != worker) { worker = a.Id; Frame($"[주컴퓨터] 진동 센서 — {dev.Name}의 볼트 조이기를 {m.NameOf(a.Id)}에게 맡겼다"); }
            if (s.Tick >= nextLook) { nextLook += Time.Hours(1); Frame("한 시간 뒤"); }
        }
        Frame($"끝 — 덜컹 {net.Rattles} · 조임 {net.Tightened} · 진동이 키운 금 {st.VibGrowths}번");
        File.WriteAllText(Path.Combine(dir, "index.html"), $"<!doctype html><meta charset=utf-8><title>vibration</title><style>body{{background:#0b0d12;color:#cfd6e4;font:13px sans-serif;display:flex;flex-wrap:wrap;gap:8px}}figure{{margin:0;width:464px}}img{{width:464px;image-rendering:pixelated}}</style>{page}");
        Console.WriteLine($"  vibration: {k}장 → {dir}");
    }
}
