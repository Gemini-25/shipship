using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>⑫ 긴 줄기 관찰 장면 — 붉은 사막으로 가는 디딤호: 바위 띠 사흘 앞 방송 · 사람들의 긴장 · 들 때 문을 닫고 · 빠져나와 안도 · 태양 합 예고.</summary>
public static partial class Program
{
    private static void LongArcScene(string dir)
    {
        ulong seed = ulong.TryParse(Environment.GetEnvironmentVariable("SEED"), out ulong sd) ? sd : 5;
        var s = ShipWorld.Crewed("Didim", seed, 4, 2);
        while (Time.HourOfDay(s.Tick) < 9) s.Run(25);
        var L = s.Layout; var o = s.Get<Outside>(); var sm = s.Get<ShipMind>(); var m = s.Get<Mind>(); var st = s.Get<Structure>();
        ShipWorld.Embark(s, 4, 10);
        var painter = new Painter(L);
        var page = new System.Text.StringBuilder();
        int k = 0, cell = L.Furniture.First(f => f.Type == "MainComputer").Cells[0];
        string Crew() => string.Join(" · ", m.Agents.Where(a => a.IsPerson).Select(a => $"{m.NameOf(a.Id)} 긴장 {a.C.Stress:0.00}"));
        var fld = s.Get<Field>();
        int Shut() => L.Doors.Count(d => !d.External && fld.DoorOpen[d.Id] > 0);
        void Frame(string text)
        {
            int x0 = Math.Max(0, L.X(cell) - 14), y0 = Math.Max(0, L.Y(cell) - 9);
            var img = painter.Frame(s, x0, y0, 29, 19, k);
            Png.WriteRgba(Path.Combine(dir, $"{k:000}.png"), img.W, img.H, img.Px);
            page.Append($"<figure><img src=\"{k:000}.png\"><figcaption>{Time.Clock(s.Tick)} · {o.Distance:0.000}AU · 열린 배 안 문 {Shut()}<br>{System.Net.WebUtility.HtmlEncode(Crew())}<br>{System.Net.WebUtility.HtmlEncode(text)}</figcaption></figure>");
            Console.WriteLine($"    {Time.Clock(s.Tick)} {text} — {Crew()} · 열린 문 {Shut()}");
            k++;
        }
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
        bool Cal(string t) => t.Contains("듭니다") || t.Contains("지나기 시작") || t.Contains("빠져나왔") || t.Contains("태양 합") || t.Contains("전이 궤도") || t.StartsWith("아침 방송");
        Frame("임무: 붉은 사막 — 10분 뒤 출발 점화");
        Watch(Time.Hours(2), t => Cal(t) || t.Contains("출발 점화를 마쳤다"));
        // 건너뛰기 (그 사이의 나날) — 떠난 때를 당긴다 · 정해진 점화도 함께
        void Skip(long to, string what)
        {
            long d = to - s.Tick;
            o.VoyageStart -= d;
            for (int i = o.Plan.Count - 1; i >= 0; i--) { var p = o.Plan[i]; if (p.Due < to) { o.TakePlan(i); continue; } p.Due -= d; p.Deadline -= d; o.SetPlan(i, p); }
            for (int i = 0; i < sm.Calendar.Count; i++) { var e = sm.Calendar[i]; e.From -= d; e.To -= d; sm.Calendar[i] = e; }
            Frame($"…{what}");
        }
        var belt = sm.Calendar.FirstOrDefault(e => e.Zone >= 0);
        if (belt.To > 0)
        {
            Skip(belt.From - Time.Days(3) - Time.Minutes(20), $"{(belt.From - Time.Days(3) - s.Tick) / (double)Time.TicksPerDay:0}일 뒤 — 바위 띠 사흘 앞");
            Watch(Time.Hours(1), Cal);
            belt = sm.Calendar.First(e => e.Zone >= 0);
            Skip(belt.From - Time.Minutes(20), "사흘 뒤 — 바위 띠에 들 때");
            Watch(Time.Hours(2), t => Cal(t) || t.Contains("바위") || t.Contains("구멍"));
            belt = sm.Calendar.First(e => e.Zone >= 0);
            Skip(belt.To - Time.Minutes(20), $"57일 뒤 — 바위 띠를 나올 때");
            Watch(Time.Hours(1), Cal);
        }
        var conj = sm.Calendar.FirstOrDefault(e => e.Zone < 0);
        Frame($"끝 — 알린 수 {sm.Notices} · 구간에 닫은 문 {sm.ZoneDoorsShut} · 다음 태양 합: 떠난 지 {(conj.To > 0 ? (conj.From - o.VoyageStart) / (double)Time.TicksPerDay : 0):0}일째부터 {(conj.To - conj.From) / (double)Time.TicksPerDay:0}일");
        File.WriteAllText(Path.Combine(dir, "index.html"), $"<!doctype html><meta charset=utf-8><title>longarc</title><style>body{{background:#0b0d12;color:#cfd6e4;font:13px sans-serif;display:flex;flex-wrap:wrap;gap:8px}}figure{{margin:0;width:464px}}img{{width:464px;image-rendering:pixelated}}</style>{page}");
        Console.WriteLine($"  longarc: {k}장 → {dir}");
    }
}
