using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>새 화면 확인: 세계를 도트 그림으로 뽑는다 (--shot · --film).</summary>
public static partial class Program
{
    /// <summary>배 한 장 (--shot [배] [시드] [시간] [x y w h]) → build/shots/배.png</summary>
    private static void Shot(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--shot").Skip(1).ToArray();
        string ship = rest.Length > 0 ? rest[0] : "Didim";
        ulong seed = rest.Length > 1 ? ulong.Parse(rest[1]) : 3;
        double hours = rest.Length > 2 ? double.Parse(rest[2]) : 1;
        var lay = Layout.Ship(ship, Tables.Default);
        int beds = lay.Furniture.Count(f => f.Type == "Bed"), docks = lay.Furniture.Count(f => f.Type == "RobotDock");
        var s = ShipWorld.Crewed(ship, seed, Math.Max(2, beds), docks, 0);
        s.Run(Time.Hours(hours));
        int x = rest.Length > 6 ? int.Parse(rest[3]) : 0, y = rest.Length > 6 ? int.Parse(rest[4]) : 0;
        int w = rest.Length > 6 ? int.Parse(rest[5]) : lay.W, h = rest.Length > 6 ? int.Parse(rest[6]) : lay.H;
        var p = new Painter(lay);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var img = p.Frame(s, x, y, w, h, (int)s.Tick);
        double ms = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        p.Frame(s, x, y, w, h, (int)s.Tick + 1);
        string path = $"build/shots/{ship.ToLowerInvariant()}.png";
        Png.WriteRgba(path, img.W, img.H, img.Px);
        Console.WriteLine($"{path} · {img.W}×{img.H} · 처음 {ms:0}ms · 다음 장 {sw.Elapsed.TotalMilliseconds:0}ms");
    }

    private sealed record FilmFrame(long Tick, string Png, string Mood, string Main, List<string> Lines);
    private sealed record FilmShot(long Tick, int Scene, int Cell, string Main);
    private sealed record FilmScene(Highlight H, List<FilmFrame> Frames);
    private sealed record FilmShip(string Name, string Key, ulong Seed, string Dir, List<FilmScene> Scenes, int AllHighlights, int Crew, long Deaths);

    /// <summary>
    /// 하이라이트 필름 (--film 배[:시드][,배:시드…] [날] [장면 수]): 재미 측정기처럼 이야기꾼을 켜고 돌려 하이라이트(한 소동 = 한 장면)를 찾은 뒤,
    /// 같은 시드로 한 번 더 돌리며(결정론 — 같은 일이 그대로 일어난다) 장면마다 첫 조짐 → 큰 일이 난 순간들(그 자리) → 뒤를 그린다.
    /// → build/film/film.html (그림을 품은 한 장)
    /// </summary>
    private static void Film(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--film").Skip(1).ToArray();
        string list = rest.Length > 0 ? rest[0] : "Didim:18";
        int argAt = 1;
        if (!list.Contains(':') && !list.Contains(',') && rest.Length > 1 && ulong.TryParse(rest[1], out var single)) { list = $"{list}:{single}"; argAt = 2; }
        double days = rest.Length > argAt ? double.Parse(rest[argAt]) : 2;
        int maxScenes = rest.Length > argAt + 1 ? int.Parse(rest[argAt + 1]) : 6;
        var ships = new List<FilmShip>();
        foreach (var item in list.Split(','))
        {
            var parts = item.Split(':');
            ships.Add(FilmOne(parts[0], parts.Length > 1 ? ulong.Parse(parts[1]) : 18, days, maxScenes));
        }
        string path = ships.Count == 1 ? Path.Combine(ships[0].Dir, "film.html") : "build/film/film.html";
        File.WriteAllText(path, FilmHtml(ships, days));
        Console.WriteLine($"{path} · 배 {ships.Count} · 장면 {ships.Sum(x => x.Scenes.Count)} · 그림 {ships.Sum(x => x.Scenes.Sum(c => c.Frames.Count))} · {new FileInfo(path).Length / 1024}KB");
    }

    private static FilmShip FilmOne(string ship, ulong seed, double days, int maxScenes)
    {
        long end = (long)(days * Time.Hours(24));
        Sim Make()
        {
            var lay = Layout.Ship(ship, Tables.Default);
            int beds = lay.Furniture.Count(f => f.Type == "Bed"), docks = lay.Furniture.Count(f => f.Type == "RobotDock");
            var w = ShipWorld.Crewed(ship, seed, Math.Max(2, beds), docks, 0);
            var st = w.Get<Storyteller>(); st.Enabled = true; st.Style = StoryStyle.Balanced; st.Level = 4;
            return w;
        }
        // 1회차: 하이라이트 · 큰 일 찾기
        var s1 = Make();
        var d1 = new Director(s1.Layout) { KeepRing = false };
        var big = new List<(long tick, int cell, FactKind kind, int cause)>();
        long cur1 = 0;
        for (long t = 0; t < end; t++)
        {
            s1.Step(); d1.Observe(s1);
            foreach (var f in s1.Facts.Since(cur1))
                if (f.Cell >= 0 && Director.Weight(f.Kind) >= 8 && f.Kind != FactKind.Impact) big.Add((f.Tick, f.Cell, f.Kind, f.Cause));
            cur1 = s1.Facts.Total;
        }
        var picks = d1.Highlights.OrderByDescending(h => h.Score).Take(maxScenes).OrderBy(h => h.Tick).ToList();
        Console.WriteLine($"{ship} {seed}: 하이라이트 {d1.Highlights.Count} → 장면 {picks.Count}");
        // 장면마다 찍을 때: 첫 조짐 · 큰 일이 난 순간들(갈래마다 먼저 · 1분 넘게 떨어진 것 · 그 자리에서) · 끝
        var shots = new List<FilmShot>();
        for (int k = 0; k < picks.Count; k++)
        {
            var h = picks[k];
            long S = Math.Max(h.StartTick, h.Tick - Time.Minutes(30)), E = Math.Min(end - 1, h.Last + Time.Minutes(5));
            var cands = big.Where(b => b.tick >= S && b.tick <= E).ToList();
            var chosen = new List<(long tick, int cell, FactKind kind, int cause)>();
            bool Free(long t) => chosen.All(c => Math.Abs(c.tick - t) > 25) && Math.Abs(t - S) > 25 && Math.Abs(E - t) > 25;
            foreach (var g in cands.GroupBy(b => b.kind).OrderByDescending(g => Director.Weight(g.Key)))
            {
                var b = g.OrderBy(x => x.tick).First();
                if (chosen.Count < 7 && Free(b.tick)) chosen.Add(b);
            }
            foreach (var b in cands.OrderByDescending(b => Director.Weight(b.kind)).ThenBy(b => b.tick))
                if (chosen.Count < 7 && Free(b.tick)) chosen.Add(b);
            string Say(FactKind kind, int cause) => $"[{FactNames.Of(kind)}] {(cause != 0 ? s1.Causes.Text(cause) : "")}";
            shots.Add(new FilmShot(S, k, h.Cell, "첫 조짐"));
            foreach (var b in chosen) shots.Add(new FilmShot(b.tick, k, b.cell, Say(b.kind, b.cause)));
            shots.Add(new FilmShot(E, k, h.Cell, "뒤"));
        }
        shots = shots.Where(x => x.Tick >= 0 && x.Tick < end).OrderBy(x => x.Tick).ThenBy(x => x.Scene).ToList();
        string dir = $"build/film/{ship.ToLowerInvariant()}-{seed}";
        Directory.CreateDirectory(dir);
        foreach (var old in Directory.GetFiles(dir, "*.png")) File.Delete(old);
        // 2회차: 같은 세계를 다시 돌리며 그린다
        var s2 = Make();
        var d2 = new Director(s2.Layout) { KeepRing = false };
        var painter = new Painter(s2.Layout);
        var scenes = picks.Select(h => new FilmScene(h, new List<FilmFrame>())).ToList();
        var lastAt = new long[picks.Count];
        int si = 0;
        long last = shots.Count > 0 ? shots[^1].Tick : 0;
        var L = s2.Layout;
        var inside = Enumerable.Range(0, L.N).Where(c => L.Kind[c] != CellKind.Outside).ToList();
        int bx0 = inside.Min(L.X), bx1 = inside.Max(L.X), by0 = inside.Min(L.Y), by1 = inside.Max(L.Y);
        for (long t = 0; t <= last && si < shots.Count; t++)
        {
            s2.Step(); d2.Observe(s2);
            while (si < shots.Count && shots[si].Tick == s2.Tick)
            {
                var shot = shots[si];
                int k = shot.Scene;
                const int vw = 24, vh = 14;
                int cx = L.X(shot.Cell), cy = L.Y(shot.Cell);
                // 배 안쪽으로 붙인다 (배를 둘러싼 칸 ± 2칸 안에서)
                int x0 = Math.Clamp(cx - vw / 2, bx0 - 2, Math.Max(bx0 - 2, bx1 + 3 - vw)), y0 = Math.Clamp(cy - vh / 2, by0 - 2, Math.Max(by0 - 2, by1 + 3 - vh));
                var img = painter.Frame(s2, x0, y0, vw, vh, (int)s2.Tick);
                string name = $"s{k + 1}-{scenes[k].Frames.Count + 1}.png";
                Png.WriteRgba(Path.Combine(dir, name), img.W, img.H, img.Px);
                // 그 사이 장면 둘레에서 일어난 다른 일 (무게 순 둘)
                var lines = s2.Facts.Since(0).Where(f => f.Tick > (lastAt[k] == 0 ? s2.Tick - 50 : lastAt[k]) && f.Tick <= s2.Tick && Director.Weight(f.Kind) >= 8
                        && f.Cell >= 0 && Math.Max(Math.Abs(L.X(f.Cell) - cx), Math.Abs(L.Y(f.Cell) - cy)) <= 14)
                    .OrderByDescending(f => Director.Weight(f.Kind)).ThenBy(f => f.Tick)
                    .Select(f => $"[{FactNames.Of(f.Kind)}] {(f.Cause != 0 ? s2.Causes.Text(f.Cause) : "")}").Where(l => l != shot.Main).Distinct().Take(2).ToList();
                lastAt[k] = s2.Tick;
                scenes[k].Frames.Add(new FilmFrame(s2.Tick, name, d2.Mood, shot.Main, lines));
                si++;
            }
        }
        var mind1 = s1.Get<Mind>();
        return new FilmShip(L.Name, ship, seed, dir, scenes, d1.Highlights.Count, mind1.Agents.Count(a => a.IsPerson), s1.Get<Life>().Deaths);
    }

    private static string FilmHtml(List<FilmShip> ships, double days)
    {
        string E(string x) => System.Net.WebUtility.HtmlEncode(x);
        var html = new System.Text.StringBuilder();
        html.Append("<title>새 엔진 하이라이트 필름</title>\n<style>\n");
        html.Append("""
:root{--bg:#07090f;--panel:#0f141e;--line:#1f2838;--fg:#dfe6f0;--dim:#8a97ab;--accent:#ffb347;--warn:#ff6b5a;--ship:#7cc4ff;color-scheme:dark}
body{background:var(--bg);color:var(--fg);font-family:"Noto Sans KR",system-ui,sans-serif;margin:0;padding-inline:16px;padding-block:24px 48px}
main{max-width:1180px;margin:0 auto;display:grid;gap:44px}
header h1{font-size:1.7rem;margin:0 0 8px;text-wrap:balance}
header p{color:var(--dim);margin:0;line-height:1.65;max-width:72ch}
.ship{display:grid;gap:28px}
.ship>h2{font-size:1.3rem;margin:0;color:var(--ship);text-wrap:balance}
.ship>h2 small{color:var(--dim);font-size:.85rem;font-weight:400;margin-left:8px}
section{display:grid;gap:12px}
section h3{font-size:1rem;margin:0;line-height:1.55;text-wrap:balance;font-weight:600}
section h3 .k{color:var(--accent);font-weight:700;margin-right:6px}
section h3 .t{color:var(--dim);font-variant-numeric:tabular-nums;margin-right:8px;font-weight:400}
.strip{display:grid;grid-template-columns:repeat(auto-fill,minmax(270px,1fr));gap:12px}
figure{margin:0;background:var(--panel);border:1px solid var(--line);border-radius:6px;overflow:hidden;display:grid;align-content:start;min-width:0}
figure img{width:100%;height:auto;display:block;image-rendering:pixelated;max-width:100%}
figcaption{padding:8px 10px 10px;font-size:.82rem;line-height:1.5;display:grid;gap:3px;min-width:0}
figcaption .when{color:var(--dim);font-variant-numeric:tabular-nums}
figcaption .mood{color:var(--accent)}
figcaption .mood.d{color:var(--warn)}
figcaption .main{overflow-wrap:anywhere;font-weight:600}
figcaption .ev{overflow-wrap:anywhere;color:var(--dim)}
footer{color:var(--dim);font-size:.8rem;line-height:1.6}
code{font-size:.85em}
</style>

""");
        html.Append($"<main><header><h1>새 엔진 하이라이트 필름</h1><p>새 엔진이 배마다 {days}일 동안 저절로 만든 일 가운데 연출이 고른 장면입니다. 짜 넣은 줄거리는 없습니다 — 이야기꾼은 원인(운석 · 파편 구름 · 낡은 부품 · 외판 피로 …)만 심고, 그 뒤는 물리 · 사람 · 로봇 · 주컴퓨터가 서로 부딪혀 생깁니다. 한 소동은 한 장면으로 묶고, 첫 조짐부터 뒤처리까지 큰 일이 난 순간을 그 자리에서 그렸습니다. 같은 시드로 다시 돌려 그렸으니(결정론) 1회차와 똑같은 일이 일어납니다. 그림은 옛 게임의 도트 화가 그대로입니다.</p></header>\n");
        foreach (var sh in ships)
        {
            html.Append($"<div class=\"ship\"><h2>{E(sh.Name)}<small>사람 {sh.Crew} · 시드 {sh.Seed} · 하이라이트 {sh.AllHighlights} · 죽음 {sh.Deaths}</small></h2>\n");
            if (sh.Scenes.Count == 0) html.Append("<p class=\"ev\">이 배에는 이틀 동안 하이라이트가 될 만한 일이 없었습니다.</p>");
            foreach (var sc in sh.Scenes)
            {
                var h = sc.H;
                html.Append($"<section><h3><span class=\"t\">{Time.Clock(h.Tick)}</span><span class=\"k\">{E(Director.FactText(h.Kind))}</span>{E(h.Title)}</h3><div class=\"strip\">\n");
                foreach (var fr in sc.Frames)
                {
                    string b64 = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(sh.Dir, fr.Png)));
                    long dt = fr.Tick - h.Tick;
                    string rel = dt == 0 ? "정점" : dt < 0 ? $"{(-dt) * Time.SecondsPerTick / 60:0.#}분 전" : $"{dt * Time.SecondsPerTick / 60:0.#}분 뒤";
                    bool danger = fr.Mood is "위험" or "결정적 순간";
                    html.Append($"<figure><img src=\"data:image/png;base64,{b64}\" alt=\"{E(sh.Name)} {E(Time.Clock(fr.Tick))} {E(fr.Main)}\" loading=\"lazy\"><figcaption><span class=\"when\">{Time.Clock(fr.Tick)} · {rel} · <span class=\"mood{(danger ? " d" : "")}\">{E(fr.Mood)}</span></span><span class=\"main\">{E(fr.Main)}</span>");
                    foreach (var l in fr.Lines) html.Append($"<span class=\"ev\">{E(l)}</span>");
                    html.Append("</figcaption></figure>\n");
                }
                html.Append("</div></section>\n");
            }
            html.Append("</div>\n");
        }
        html.Append($"<footer>이야기꾼 '고른' · 가혹함 4 · {days}일 · 칸 하나 = 32도트 · 만든 명령: <code>Lab --film {string.Join(",", ships.Select(x => $"{x.Key}:{x.Seed}"))} {days}</code></footer></main>\n");
        return html.ToString();
    }
}
