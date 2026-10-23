using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>화장실 (다) 관찰 장면 — 새뜰호(40명 · 화장실 둘)의 아침: 막힌 변기에 '사용 금지' 불이 켜지고 로봇이 뚫는가 · 80% 찬 오물 통을 퇴비통으로 비우는가 · 줄이 길면 주컴퓨터가 안내하는가.</summary>
public static partial class Program
{
    private static void WashScene(string dir)
    {
        ulong seed = ulong.TryParse(Environment.GetEnvironmentVariable("SEED"), out ulong sd) ? sd : 7;
        var s = ShipWorld.Crewed("Saetteul", seed, 40, 10);
        while (Time.HourOfDay(s.Tick) < 5.5) s.Run(25);
        var L = s.Layout; var m = s.Get<Mind>(); var life = s.Get<Life>(); var sm = s.Get<ShipMind>();
        var toilets = L.Furniture.Where(f => f.Type == "Toilet" && f.Cells.Length > 0).ToList();
        var clogged = toilets[0]; var full = toilets.Last();
        life.Clog(clogged.Id, s.Causes.Add(CauseKind.Setup, "어젯밤 누가 물티슈를 버려 변기 관이 막혔다", s.Tick, clogged.Cells[0]));
        life.Tank[full.Id] = Life.TankMax * 0.85;
        var painter = new Painter(L);
        var page = new System.Text.StringBuilder();
        int k = 0;
        string State() => $"사용 금지 {m.WashSign.Count(x => x != 0)} · 뚫음 {m.Unclogged} · 통 비움 {m.Emptied} · 닦음 {m.Scrubbed} · 줄 {m.Queued} · 안내 {sm.WashCalls}/{sm.WashOrders} · 샤워 {m.ShowersTaken} · 넘침 {life.Overflows}";
        void Frame(int cell, string text)
        {
            int x0 = Math.Clamp(L.X(cell) - 14, 0, Math.Max(0, L.W - 29)), y0 = Math.Clamp(L.Y(cell) - 9, 0, Math.Max(0, L.H - 19));
            var img = painter.Frame(s, x0, y0, 29, 19, k);
            Png.WriteRgba(Path.Combine(dir, $"{k:000}.png"), img.W, img.H, img.Px);
            page.Append($"<figure><img src=\"{k:000}.png\"><figcaption>{Time.Clock(s.Tick)} · {System.Net.WebUtility.HtmlEncode(State())}<br>{System.Net.WebUtility.HtmlEncode(text)}</figcaption></figure>");
            Console.WriteLine($"    {Time.Clock(s.Tick)} {text} — {State()}");
            k++;
        }
        Frame(clogged.Cells[0], "변기 하나가 막혔다 (아무도 모른다) · 다른 변기의 오물 통은 85%");
        long end = s.Tick + Time.Hours(5);
        long logTick = s.Tick;
        var care = new HashSet<int>();
        while (s.Tick < end && k < 40)
        {
            s.Step();
            foreach (var (tick, t) in sm.Log.Where(l => l.tick >= logTick).ToList())   // 기록은 300줄만 남는다 — 때로 고른다
                if (t.Contains("변기") || t.Contains("줄이 깁니다") || t.Contains("차례를") || t.Contains("샤워 칸")) Frame(t.Contains("변기 하나가") ? clogged.Cells[0] : full.Cells[0], $"[주컴퓨터] {t}");
            logTick = s.Tick;
            foreach (var a in m.Agents)
                if (a.C.Goal == GoalKind.ToiletCare && a.Plan.Count > a.C.StepIndex && a.Plan[a.C.StepIndex].Kind == StepKind.Sanitize && care.Add(a.Id * 8 + a.Plan[a.C.StepIndex].Arg))
                    Frame(a.C.Cell, $"{m.NameOf(a.Id)} — {m.Thought(a.Id)}");
        }
        if (Environment.GetEnvironmentVariable("WASH_DBG") == "1")
        {
            foreach (var j in sm.Jobs) Console.WriteLine($"      일감 {j.Kind} 대상 {j.Target} Arg {j.Arg} 맡은 {j.Agent} 까지 {Time.Clock(j.Until)}");
            foreach (var a in m.Agents.Where(x => !x.IsPerson)) Console.WriteLine($"      {m.NameOf(a.Id)} {a.C.Kind} {a.C.Goal} 맡은 {a.C.AssignedGoal}/{a.C.AssignedTarget} 까지 {Time.Clock(a.C.AssignedUntil)} 전지 {a.C.Battery:0.00} '{a.LastFail}' {m.Thought(a.Id)}");
        }
        Frame(clogged.Cells[0], $"끝 — 막힌 변기 {(life.Clogged[clogged.Id] == 0 ? "뚫렸다" : "그대로")} · 통 {life.Tank[full.Id]:0.0}kg · 쌓인 오물 {life.Heap.Sum():0.0}kg");
        File.WriteAllText(Path.Combine(dir, "index.html"), $"<!doctype html><meta charset=utf-8><title>wash</title><style>body{{background:#0b0d12;color:#cfd6e4;font:13px sans-serif;display:flex;flex-wrap:wrap;gap:8px}}figure{{margin:0;width:464px}}img{{width:464px;image-rendering:pixelated}}</style>{page}");
        Console.WriteLine($"  wash: {k}장 → {dir}");
    }
}
