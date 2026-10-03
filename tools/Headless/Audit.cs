using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ShipSim.Core;

// v16.23 점검 항해 도구 — 사람이 플레이하며 찾던 문제를 헤드리스로 찾아 보고서로 낸다.
//   dotnet Headless.dll --audit [일수=10] [시드들=7,11,13] [배들=기본 5척+생성 3척] [--jobs=4] [--out=폴더] [--persona=1] [--level=3]
//   예: --audit 2 7 Hanbit  (짧은 확인) · --audit --audit-report=docs/audit/AUDIT_….json (규칙만 고친 뒤 시뮬레이션 없이 다시 쓰기)
// 항해 하나 = 프로세스 하나 (정적 값이 섞이지 않게 · 결정론). 결과는 docs/audit/AUDIT_<날짜>_s<시드>.md + .json, 이전 보고서와 비교.
public static partial class Program
{
    private static readonly string[] AuditDefaultShips =
        { "Kestrel", "Mirinae", "Hanbit", "Eunha", "Cheonma", "gen:mining:ring:10:7", "gen:hospital:spine:16:3", "gen:research:linear:8:5" };

    private static readonly JsonSerializerOptions AuditJson = new()
    {
        IncludeFields = true, WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private static string? AuditOpt(string[] args, string name) => args.FirstOrDefault(a => a.StartsWith(name + "="))?.Split('=', 2)[1];

    private static void AuditStory(string[] args)
    {
        Storyteller.PersonaValue = float.TryParse(AuditOpt(args, "--persona"), out var p) ? p : 1f; // 꾸준형
        Storyteller.LevelValue = float.TryParse(AuditOpt(args, "--level"), out var l) ? l : 3f;     // 보통
    }

    /// <summary>자식 프로세스: 항해 하나를 돌려 JSON 한 줄을 낸다.</summary>
    private static int RunAuditOne(string[] args)
    {
        int i = Array.IndexOf(args, "--audit-one");
        string ship = args[i + 1];
        int seed = int.Parse(args[i + 2]);
        float days = float.Parse(args[i + 3], System.Globalization.CultureInfo.InvariantCulture);
        AuditStory(args);
        var run = AuditOne(ship, seed, days);
        Console.WriteLine("AUDITJSON:" + JsonSerializer.Serialize(run, AuditJson));
        return 0;
    }

    private static string? AuditRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "game", "src", "Core"))) return d.FullName;
        return null;
    }

    private static int RunAudit(string[] args, int seed)
    {
        if (args.Contains("--audittest")) return RunAuditTest(seed);
        if (args.Contains("--audit-one")) return RunAuditOne(args);
        if (AuditOpt(args, "--audit-report") is string again) return AuditReReport(again);
        var sw = Stopwatch.StartNew();
        int ai = Array.IndexOf(args, "--audit");
        var pos = args.Skip(ai + 1).TakeWhile(a => !a.StartsWith("--")).ToList();
        float days = pos.Count > 0 && float.TryParse(pos[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dd) ? dd : 10f;
        int[] seeds = pos.Count > 1 ? pos[1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray() : new[] { 7, 11, 13 };
        string[] ships = pos.Count > 2 ? pos[2].Split(',', StringSplitOptions.RemoveEmptyEntries) : AuditDefaultShips;
        foreach (var s in ships)
            if (ShipCatalog.Find(s) == null) { Console.WriteLine($"모르는 배: {s}"); return 2; }
        int jobs = int.TryParse(AuditOpt(args, "--jobs"), out var j) ? Math.Clamp(j, 1, 8) : Math.Min(4, Environment.ProcessorCount);
        string? root = AuditRoot();
        string outDir = AuditOpt(args, "--out") ?? (root != null ? Path.Combine(root, "docs", "audit") : "audit");
        AuditStory(args);
        string story = $"{Storyteller.PersonaName(Storyteller.Persona)} · {Storyteller.LevelName(Storyteller.Level)}";
        Console.WriteLine($"점검 항해 · {days}일 · 시드 {string.Join(",", seeds)} · 배 {ships.Length}척 · 이야기꾼 {story} · 동시에 {jobs}개");

        // 항해마다 자식 프로세스 (결정론: 결과는 배 · 시드 순으로 다시 늘어놓는다)
        var tasks = ships.SelectMany(s => seeds.Select(sd => (ship: s, seed: sd))).ToList();
        var results = new AuditRun?[tasks.Count];
        string self = typeof(Program).Assembly.Location;
        string host = Environment.ProcessPath ?? "dotnet";
        bool viaDotnet = Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        string extra = string.Join(" ", args.Where(a => a.StartsWith("--persona=") || a.StartsWith("--level=")));
        int next = 0, done = 0;
        var lk = new object();
        var workers = Enumerable.Range(0, Math.Min(jobs, tasks.Count)).Select(_ => System.Threading.Tasks.Task.Run(() =>
        {
            while (true)
            {
                int k;
                lock (lk) { if (next >= tasks.Count) return; k = next++; }
                var (ship, seed) = tasks[k];
                string a = $"--audit-one {ship} {seed} {days.ToString(System.Globalization.CultureInfo.InvariantCulture)} {extra}";
                var psi = new ProcessStartInfo(host, viaDotnet ? $"\"{self}\" {a}" : a) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                AuditRun? run = null;
                string err = "";
                try
                {
                    using var p = Process.Start(psi)!;
                    var errTask = p.StandardError.ReadToEndAsync();
                    string? line;
                    while ((line = p.StandardOutput.ReadLine()) != null)
                        if (line.StartsWith("AUDITJSON:")) run = JsonSerializer.Deserialize<AuditRun>(line["AUDITJSON:".Length..], AuditJson);
                    p.WaitForExit();
                    err = errTask.Result;
                }
                catch (Exception e) { err = e.Message; }
                run ??= new AuditRun { Ship = ship, Seed = seed, Days = days, Error = "자식 프로세스 실패: " + string.Join(" ", err.Split('\n').Take(3)) };
                lock (lk)
                {
                    results[k] = run;
                    done++;
                    Console.WriteLine($"  [{done}/{tasks.Count}] {ship} 시드 {seed} · {run.Wall:0}초 · 사망 {run.Deaths.Count} · 사고 {run.Cases.Count} · 지문 {run.Hash:x8}{(run.Error != null ? " · 예외 " + run.Error : "")}");
                }
            }
        })).ToArray();
        System.Threading.Tasks.Task.WaitAll(workers);

        var report = new AuditReport
        {
            Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), Days = days, Seeds = seeds.ToList(), Ships = ships.ToList(), Story = story,
            Runs = results.Select(r => r!).ToList(), Custom = pos.Count > 2,
        };
        string name = $"AUDIT_{DateTime.Now:yyyyMMdd}_s{string.Join("-", seeds)}_d{days:0.#}{(pos.Count > 2 ? "_" + string.Join("-", ships.Select(s => s.Replace(':', '.'))) : "")}";
        report.Wall = Math.Round(sw.Elapsed.TotalSeconds);
        AuditWrite(outDir, name, report, root);
        return 0;
    }

    /// <summary>규칙을 다시 계산해 보고서(.md · .json)를 쓴다 — 시뮬레이션 없이 --audit-report=이전.json 으로도 (규칙만 고쳤을 때).</summary>
    private static void AuditWrite(string outDir, string name, AuditReport report, string? root)
    {
        var ctx = new AuditCtx { Runs = report.Runs };
        AuditCatalog(ctx);
        if (root != null) { var (h, ex, n) = AuditScanSource(root); ctx.SrcHits = h; ctx.SrcEx = ex; ctx.SrcFiles = n; }
        report.Findings = AuditEvaluate(ctx);
        report.SrcHits = ctx.SrcHits;
        report.SrcEx = ctx.SrcEx;
        Directory.CreateDirectory(outDir);
        var prev = AuditPrevious(outDir, name);
        report.Previous = prev?.file;
        File.WriteAllText(Path.Combine(outDir, name + ".md"), AuditMarkdown(report, ctx, prev?.report));
        File.WriteAllText(Path.Combine(outDir, name + ".json"), JsonSerializer.Serialize(report, AuditJson));
        Console.WriteLine();
        foreach (var line in AuditTop(report.Findings, 10)) Console.WriteLine(line);
        Console.WriteLine($"\n보고서: {Path.Combine(outDir, name + ".md")} · 항해 {report.Wall:0}초");
    }

    private static int AuditReReport(string path)
    {
        var report = JsonSerializer.Deserialize<AuditReport>(File.ReadAllText(path), AuditJson)!;
        AuditWrite(Path.GetDirectoryName(Path.GetFullPath(path))!, Path.GetFileNameWithoutExtension(path), report, AuditRoot());
        return 0;
    }

    public sealed class AuditReport
    {
        public string Date = "", Story = "";
        public string? Previous;
        public float Days;
        public double Wall;
        public bool Custom;
        public List<int> Seeds = new();
        public List<string> Ships = new();
        public List<AFinding> Findings = new();
        public List<AuditRun> Runs = new();
        public Dictionary<string, int> SrcHits = new();
        public List<string> SrcEx = new();
    }

    /// <summary>방 70종이 어디서든 나오나: 카탈로그 배 + 생성기 표본 (설계도만 읽는다 — 시뮬레이션 없음).</summary>
    private static void AuditCatalog(AuditCtx c)
    {
        var temps = ShipCatalog.All.ToList();
        foreach (var p in ShipInfos.GenPurposes)
            foreach (var f in ShipInfos.GenFrames)
                foreach (int n in new[] { 8, 24 })
                    temps.Add(ShipGenerator.Template(p, f, n, 1));
        foreach (int n in new[] { 8, 16, 30 }) temps.Add(ShipGenerator.Template(n, 1));
        foreach (var t in temps)
        {
            try
            {
                var ship = ShipBuilder.FromAscii(t.Name, t.Ascii);
                foreach (var r in ship.Rooms) c.CatalogRooms.Add(r.Kind.ToString());
                c.CatalogShips++;
            }
            catch { }
        }
    }

    private static (string file, AuditReport report)? AuditPrevious(string dir, string name)
    {
        // 같은 설정(시드 · 일수 · 배)의 이전 보고서를 먼저, 없으면 가장 최근 것
        string cfg = name[(name.IndexOf("_s") + 1)..];
        var files = Directory.GetFiles(dir, "AUDIT_*.json").Where(f => Path.GetFileNameWithoutExtension(f) != name)
            .OrderByDescending(f => Path.GetFileNameWithoutExtension(f).EndsWith(cfg) ? 1 : 0).ThenByDescending(f => File.GetLastWriteTimeUtc(f)).ToList();
        foreach (var f in files)
        {
            try { if (JsonSerializer.Deserialize<AuditReport>(File.ReadAllText(f), AuditJson) is AuditReport r) return (Path.GetFileName(f), r); }
            catch { }
        }
        return null;
    }

    private static readonly string[] AuditSevName = { "정보", "낮음", "보통", "높음", "치명" };

    /// <summary>심각도 순, 같으면 규칙 표 순서 (표는 사람이 플레이하며 겪은 문제부터 — 사망 · 공황 · 고장 · 보조 발전기 · 쓰임 · 사고 · 자원 · 컴퓨터 · 글).</summary>
    private static IEnumerable<AFinding> AuditRank(List<AFinding> fs) =>
        fs.Where(f => f.Sev >= 1).OrderByDescending(f => f.Sev).ThenBy(f => fs.IndexOf(f));

    private static IEnumerable<string> AuditTop(List<AFinding> fs, int n)
    {
        yield return "고칠 것 상위 10";
        int i = 0;
        foreach (var f in AuditRank(fs).Take(n))
            yield return $"{++i}. [{AuditSevName[f.Sev]}] {f.Name} — {f.Summary}{(f.Fix != "" ? $" → {f.Fix}" : "")}";
    }

    private static string AuditArrow(AFinding f, AuditReport? prev)
    {
        var p = prev?.Findings.FirstOrDefault(x => x.Id == f.Id);
        if (p == null) return prev == null ? "-" : "새 항목";
        if (Math.Abs(p.Metric - f.Metric) < 1e-6) return "→ 같음";
        bool better = !double.IsNaN(f.Target) ? Math.Abs(f.Metric - f.Target) < Math.Abs(p.Metric - f.Target) : f.LowerBetter ? f.Metric < p.Metric : f.Metric > p.Metric;
        return $"{(better ? "▼ 좋아짐" : "▲ 나빠짐")} {p.Metric:0.###}→{f.Metric:0.###}";
    }

    private static string AuditCell(string s) => s.Replace("|", "/").Replace("\n", " ");

    private static string AuditMarkdown(AuditReport rep, AuditCtx c, AuditReport? prev)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# 점검 항해 보고서 — {rep.Date}");
        sb.AppendLine();
        sb.AppendLine($"- 배 {rep.Ships.Count}척 ({string.Join(", ", c.Runs.GroupBy(r => r.Ship).Select(g => $"{g.Key} {g.First().ShipName} {g.First().Crew}인"))})");
        sb.AppendLine($"- 시드 {string.Join(", ", rep.Seeds)} · {rep.Days}일 · 이야기꾼 {rep.Story} · 승무원 죽음 켬 · 항해 {c.Runs.Count}번 · 걸린 시간 {rep.Wall:0}초");
        sb.AppendLine($"- 이전 보고서: {rep.Previous ?? "없음 (첫 실행)"}");
        sb.AppendLine($"- 지문(결정론): {string.Join(" ", c.Runs.Select(r => $"{AuditCtx.Tag(r)}={r.Hash:x8}"))}");
        sb.AppendLine();
        sb.AppendLine("## 고칠 것 상위 10");
        sb.AppendLine();
        sb.AppendLine("| # | 심각도 | 항목 | 무엇 | 어디를 | 이전 대비 |");
        sb.AppendLine("|---|---|---|---|---|---|");
        int i = 0;
        foreach (var f in AuditRank(rep.Findings).Take(10))
            sb.AppendLine($"| {++i} | {AuditSevName[f.Sev]} | {f.Name} | {AuditCell(f.Summary)} | {AuditCell(f.Fix)} | {AuditArrow(f, prev)} |");
        sb.AppendLine();
        sb.AppendLine("## 규칙 표 (전체)");
        sb.AppendLine();
        sb.AppendLine("| 규칙 | 항목 | 심각도 | 횟수 | 값 | 이전 대비 | 요약 |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var f in rep.Findings)
            sb.AppendLine($"| `{f.Id}` | {f.Name} | {AuditSevName[f.Sev]} | {f.Count} | {f.Metric:0.###} | {AuditArrow(f, prev)} | {AuditCell(f.Summary)} |");
        sb.AppendLine();
        sb.AppendLine("## 예 (시각 · 배 · 방 · 사람)");
        foreach (var f in rep.Findings.Where(f => f.Examples.Count > 0 && f.Examples.Any(e => e.Length > 0)))
        {
            sb.AppendLine();
            sb.AppendLine($"### {f.Name} (`{f.Id}`)");
            foreach (var e in f.Examples.Where(e => e.Length > 0)) sb.AppendLine($"- {e}");
        }
        sb.AppendLine();
        sb.AppendLine("## 규모별 사고당 사망");
        sb.AppendLine();
        sb.AppendLine("목표 (사용자 결정): 보통 재해(운석우 · 화재 · 정전 · 배관 파열)는 가끔 사망 · 배 전체급은 큰 피해 · 우주급만 진짜 생존 위기.");
        sb.AppendLine();
        sb.AppendLine($"부상 심각도: 경상 = 한 번에 다친 양 {AuditSerious:0.00} 미만 · 중상 = 그 이상(깊은 상처는 출혈로 이어진다) · 위중 = 쓰러짐 · 사망. \"중상 이상\" = 중상 + 위중 + 사망.");
        sb.AppendLine();
        sb.AppendLine("| 규모 | 사고 | 사망 | 위중(쓰러짐) | 중상 | 경상 | 사고당 사망 | 사고당 중상 이상 | 사고당 다친 사람 | 평균 시간 | 번진 것 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var s in ScaleTable.Scales)
        {
            var cs = c.Cases.Where(k => k.Peak == (int)s).ToList();
            int de = cs.Sum(k => k.Deaths), dn = cs.Sum(k => k.Downs), se = cs.Sum(k => k.Serious), li = cs.Sum(k => k.Light);
            string Per(int x, string f) => cs.Count == 0 ? "-" : (x / (double)cs.Count).ToString(f);
            sb.AppendLine($"| {ScaleTable.Label(s)} | {cs.Count} | {de} | {dn} | {se} | {li} | {Per(de, "0.000")} | {Per(de + dn + se, "0.00")} | {Per(de + dn + se + li, "0.00")} | {(cs.Count == 0 ? "-" : cs.Average(k => k.Hours).ToString("0.0") + "시간")} | {cs.Count(k => k.Peak > k.Base)} |");
        }
        sb.AppendLine();
        var rs = c.Runs;
        sb.AppendLine($"큰 상처 뒤 (항해 합): 출혈 {rs.Sum(r => r.Bleeds)} · 심정지 {rs.Sum(r => r.Arrests)} (살림 {rs.Sum(r => r.Revived)}) · 그 뒤 숨짐 {rs.Sum(r => r.TraumaDied)} · 불붙는 순간 덴 사람 {rs.Sum(r => r.Flashes)} · 대응 · 수리 중 다침 {rs.Sum(r => r.WorkHurts)} (크게 {rs.Sum(r => r.WorkBad)}) · 컴퓨터가 생체 신호로 부름 {rs.Sum(r => r.Paged)} · 캄캄한 데서 넘어짐 {rs.Sum(r => r.DarkFalls)}");
        sb.AppendLine($"위험이 사람에게 닿은 길 (항해 합): 열사병 {rs.Sum(r => r.HeatStrokes)} (숨짐 {rs.Sum(r => r.HeatDeaths)}) · 큰 피폭 {rs.Sum(r => r.RadSevere)} (쓰러짐 {rs.Sum(r => r.RadCollapses)} · 숨짐 {rs.Sum(r => r.RadDeaths)}) · 가장 큰 피폭 {rs.Select(r => r.MaxDose10).DefaultIfEmpty(0).Max() / 10f:0.0}Sv · 냄새에 늦게 깬 잠 {rs.Sum(r => r.LateWakes)}"); // v16.26
        { var hb = new Dictionary<string, float>(); foreach (var r in rs) foreach (var (k, v) in r.HurtBy) hb[k] = hb.GetValueOrDefault(k) + v; // 통합: 까닭별 다친 양
          sb.AppendLine($"다친 까닭 (부상 합): {string.Join(" · ", hb.OrderByDescending(kv => kv.Value).Take(10).Select(kv => $"{kv.Key} {kv.Value:0.0}"))} · 체력 0.4 밑 {rs.Sum(r => r.LowHp)}번"); }
        sb.AppendLine();
        sb.AppendLine("## 항해별");
        sb.AppendLine();
        sb.AppendLine("| 항해 | 생존 | 사고 | 고장 | 반복 고장 | 정전(10분+) · 보조 켬 | 컴퓨터 조치 맞음/틀림 | 하루 시뮬 | 지문 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var r in c.Runs)
        {
            var aux = r.Aux.Where(a => a.Minutes >= 10 && a.Had).ToList();
            sb.AppendLine($"| {AuditCtx.Tag(r)} | {r.Alive}/{r.Crew} | {r.Cases.Count} | {r.FaultEvents} | {r.Repeats.Count} | {aux.Count} · {aux.Count(a => a.AfterMin >= 0)} | {(r.Comp.Measured ? $"{r.Comp.Right}/{r.Comp.Wrong}" : "측정 안 됨")} | {r.SecPerDay:0.0}초 | {r.Hash:x8}{(r.Error != null ? " 예외" : "")} |");
        }
        sb.AppendLine();
        sb.AppendLine("## 새 규칙 더하기");
        sb.AppendLine();
        sb.AppendLine("tools/Headless/AuditRules.cs 의 `AuditRuleTable` 에 `new(\"id\", \"이름\", 낮을수록좋음, c => F(심각도, 횟수, 값, 요약, 고칠곳, 예))` 한 줄. 날것 값은 AuditProbe.cs(1분마다) → AuditRun(JSON).");
        return sb.ToString();
    }
}
