using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ShipSim.Core;

// v16.5c 설비 그림 표 점검 (Godot 없이 확인할 수 있는 것만): View/FixtureArt*.cs 소스를 읽어
//   모든 FurnitureType 이 표에 한 줄씩 · 그림 함수가 종류마다 따로 (이름 · 내용 · 그리는 순서가 겹치지 않는다)
//   · 몸체는 도형이 충분히 많다 (사각형 하나 + 색 금지) · 움직임은 상태를 읽는다 · Core 상태를 바꾸지 않는다 · ShipView 가 표를 부른다.
public static partial class Program
{
    private static string FixArtHere([CallerFilePath] string p = "") => p;

    private static string? FixArtViewDir()
    {
        var cands = new List<string>();
        var here = FixArtHere();
        if (here.Length > 0) cands.Add(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here) ?? ".", "..", "..", "game", "src", "View")));
        var d = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (int i = 0; i < 6 && d != null; i++, d = d.Parent) cands.Add(Path.Combine(d.FullName, "game", "src", "View"));
        return cands.FirstOrDefault(c => File.Exists(Path.Combine(c, "FixtureArt.cs")));
    }

    /// <summary>static void Name(in Fix x) { ... } 의 몸통 (괄호 짝 맞춤).</summary>
    private static Dictionary<string, string> FixArtFunctions(string src)
    {
        var res = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(src, @"static\s+void\s+(\w+)\s*\(\s*in\s+Fix\s+x\s*\)"))
        {
            int open = src.IndexOf('{', m.Index + m.Length);
            if (open < 0) continue;
            int depth = 0, end = open;
            for (int i = open; i < src.Length; i++)
            {
                if (src[i] == '{') depth++;
                else if (src[i] == '}' && --depth == 0) { end = i; break; }
            }
            var name = m.Groups[1].Value;
            res[res.ContainsKey(name) ? name + "#dup" + res.Count : name] = src.Substring(open, end - open + 1);
        }
        return res;
    }

    private static string FixArtNormalize(string body) =>
        Regex.Replace(Regex.Replace(body, @"//[^\n]*", ""), @"\s+", "");

    private static readonly Regex FixArtDrawCall = new(@"\b(Box|Dot|Ring|Line|Pipe|Can|Glass|Gauge|Knob|Cable|Vents|Grille|Bevel|Fan|Stripes|Plate|Bolts?|Led|Tag|Shimmer|DrawRect|DrawCircle|DrawArc|DrawLine|DrawPolyline|DrawColoredPolygon|DrawPolygon|RoundRect)\s*\(");

    private static int RunFixArtCheck(int seed)
    {
        _fails = 0;
        Console.WriteLine($"설비 그림 표 점검 (v16.5c) · 시드 {seed}\n");
        try
        {
            var dir = FixArtViewDir();
            Check("소스 — game/src/View/FixtureArt.cs 를 찾았다", dir != null, dir ?? "없음");
            if (dir == null) { Console.WriteLine($"\n✘ {_fails}개 실패"); return 1; }
            var files = Directory.GetFiles(dir, "FixtureArt*.cs").OrderBy(f => f, StringComparer.Ordinal).ToList();
            var src = string.Join("\n", files.Select(File.ReadAllText));

            // 1) 표: 모든 종류가 한 줄씩
            var rows = Regex.Matches(src, @"t\[FurnitureType\.(\w+)\]\s*=\s*new\(\s*(\w+)\s*,\s*(\w+)\s*,\s*(\w+)\s*,\s*Look\.(\w+)\s*,")
                .Select(m => (type: m.Groups[1].Value, body: m.Groups[2].Value, life: m.Groups[3].Value, fine: m.Groups[4].Value, look: m.Groups[5].Value)).ToList();
            var all = Enum.GetNames<FurnitureType>();
            var missing = all.Except(rows.Select(r => r.type)).ToList();
            var dupTypes = rows.GroupBy(r => r.type).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            var unknown = rows.Select(r => r.type).Except(all).ToList();
            Check($"표 — FurnitureType {all.Length}종이 모두 표에 한 줄씩 있다", missing.Count == 0 && dupTypes.Count == 0 && unknown.Count == 0 && rows.Count == all.Length,
                $"표 {rows.Count}줄 · 파일 {files.Count}개" + (missing.Count > 0 ? $" · 빠짐: {string.Join(",", missing)}" : "") + (dupTypes.Count > 0 ? $" · 겹침: {string.Join(",", dupTypes)}" : "") + (unknown.Count > 0 ? $" · 모름: {string.Join(",", unknown)}" : ""));

            // 2) 함수: 종류마다 따로 (같은 함수를 두 종류가 나눠 쓰지 않는다) · 모두 정의돼 있다
            var fns = FixArtFunctions(src);
            var refs = rows.SelectMany(r => new[] { r.body, r.life, r.fine }).ToList();
            var shared = refs.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            var undefined = refs.Where(n => !fns.ContainsKey(n)).Distinct().ToList();
            var doubleDef = fns.Keys.Where(k => k.Contains("#dup")).ToList();
            Check("함수 — 몸체 · 움직임 · 디테일 함수가 종류마다 따로 있고 모두 정의돼 있다 (중복 이름 없음)", shared.Count == 0 && undefined.Count == 0 && doubleDef.Count == 0 && refs.Count == all.Length * 3,
                $"함수 {refs.Distinct().Count()}개" + (shared.Count > 0 ? $" · 같이 씀: {string.Join(",", shared)}" : "") + (undefined.Count > 0 ? $" · 정의 없음: {string.Join(",", undefined)}" : "") + (doubleDef.Count > 0 ? $" · 두 번 정의: {doubleDef.Count}" : ""));

            // 3) 내용 중복 없음: 몸체 · 움직임 · 디테일 각각 글자가 같은 함수가 없다
            int DupBodies(Func<(string type, string body, string life, string fine, string look), string> pick) =>
                rows.Where(r => fns.ContainsKey(pick(r))).GroupBy(r => FixArtNormalize(fns[pick(r)])).Count(g => g.Count() > 1);
            int db = DupBodies(r => r.body), dl = DupBodies(r => r.life), df = DupBodies(r => r.fine);
            Check("중복 — 내용이 같은 그림 함수가 없다 (몸체 · 움직임 · 디테일)", db == 0 && dl == 0 && df == 0, $"몸체 {db} · 움직임 {dl} · 디테일 {df}");

            // 4) 실루엣: 몸체마다 그리는 순서(도형 종류의 줄)가 다르고, 도형이 충분히 많다 (사각형 하나 + 색 금지)
            var seqs = rows.Where(r => fns.ContainsKey(r.body)).Select(r => (r.type, seq: string.Join(",", FixArtDrawCall.Matches(fns[r.body]).Select(m => m.Groups[1].Value)), n: FixArtDrawCall.Matches(fns[r.body]).Count)).ToList();
            var sameSeq = seqs.GroupBy(s => s.seq).Where(g => g.Count() > 1).Select(g => string.Join("=", g.Select(s => s.type))).ToList();
            var thin = seqs.Where(s => s.n < 6).Select(s => $"{s.type}({s.n})").ToList();
            Check("실루엣 — 몸체마다 도형 순서가 다르고 도형이 6개 이상 (색만 바꾼 사각형 없음)", sameSeq.Count == 0 && thin.Count == 0,
                $"평균 도형 {seqs.Average(s => s.n):0.0}개 · 최소 {seqs.Min(s => s.n)}" + (sameSeq.Count > 0 ? $" · 같은 순서: {string.Join(" ", sameSeq)}" : "") + (thin.Count > 0 ? $" · 적음: {string.Join(",", thin)}" : ""));

            // 5) 디테일: 가까이서 보이는 것 (볼트 · 글씨 · 바느질)
            var noFine = rows.Where(r => fns.ContainsKey(r.fine) && FixArtDrawCall.Matches(fns[r.fine]).Count < 2).Select(r => r.type).ToList();
            Check("디테일 — 가까이 층에 종류마다 2개 이상 그린다", noFine.Count == 0, noFine.Count > 0 ? string.Join(",", noFine) : $"{rows.Count}종");

            // 6) 상태: 움직임 함수가 상태(가동 · 대기 · 고장 · 꺼짐)나 승무원 · 배 상태를 읽는다
            var stateWords = new Regex(@"x\.(Glow|Spin|On|Lit|Dead|St|Eff|W|M|User|Occupant|Ang)\b|SittingIn\(x\)|SleepingIn\(x\)|SomeoneWorkingIn\(x\)|x\.F\.(Room|Owner|Storage|Improved|AuxHelm)");
            var deaf = rows.Where(r => fns.ContainsKey(r.life) && !stateWords.IsMatch(fns[r.life])).Select(r => r.type).ToList();
            Check("상태 — 움직임이 모두 상태 · 사람 · 방을 읽는다 (가동 · 대기 · 고장 · 꺼짐에 따라 다르게)", deaf.Count == 0, deaf.Count > 0 ? string.Join(",", deaf) : $"{rows.Count}종");
            var looks = rows.GroupBy(r => r.look).ToDictionary(g => g.Key, g => g.Count());
            var lookEnum = Regex.Match(src, @"enum\s+Look\s*\{([^}]*)\}").Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var fxCases = lookEnum.Where(l => !Regex.IsMatch(src, $@"case\s+Look\.{l}\s*:")).ToList();
            Check("고장 효과 — 고장 모양 종류마다 그리는 길이 있고 여러 모양을 고루 쓴다", fxCases.Count == 0 && looks.Count >= 7,
                string.Join(" · ", looks.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}")) + (fxCases.Count > 0 ? $" · 그림 없음: {string.Join(",", fxCases)}" : ""));

            // 7) 결정론: 그림은 Core 를 바꾸지 않는다 (대입 · 난수 · 호출 없음)
            var writes = Regex.Matches(src, @"x\.(F|M|W)(\.\w+)+\s*(=|\+=|-=|\+\+|--)(?!=)").Select(m => m.Value).ToList();
            bool rnd = src.Contains("System.Random") || src.Contains("new Random(") || Regex.IsMatch(src, @"\bR\.(Float|Range|Chance|Pick)\(");
            var calls = Regex.Matches(src, @"\.(Break|Repair|Step|Add|Remove|Take|Put|Use)\(").Select(m => m.Value).Distinct().ToList();
            Check("결정론 — 그림이 Core 상태를 바꾸지 않는다 (대입 · 난수 · 바꾸는 호출 없음)", writes.Count == 0 && !rnd && calls.Count == 0,
                (writes.Count > 0 ? $"대입: {string.Join(" ", writes.Take(3))} " : "") + (rnd ? "난수 " : "") + (calls.Count > 0 ? $"호출: {string.Join(" ", calls)}" : "깨끗하다"));

            // 8) 연결: ShipView 가 표를 부르고(정적 몸체 · 동적 움직임 · 디테일 층), 단계 모양(ShipViewTiers)은 그대로
            var view = File.ReadAllText(Path.Combine(dir, "ShipView.cs"));
            bool hookBody = view.Contains("FixtureArt.PaintBody(ci, f)"), hookLife = view.Contains("PaintFixtureLife(ci, f)"), hookFine = view.Contains("AddFixtureFineLayer()"), hookLod = view.Contains("UpdateFixtureLod()");
            bool tiers = src.Contains("PaintTierLife(ci, f, m, t)") && src.Contains("PaintFusion(ci, f, t)") && src.Contains("int Tier") && src.Contains("MachineGrade Grade");
            Check("연결 — 정적 몸체 · 동적 움직임 · 디테일 층 · 확대 단계가 표를 부르고, 단계 모양 · tier/grade 자리는 남는다", hookBody && hookLife && hookFine && hookLod && tiers,
                $"몸체 {hookBody} · 움직임 {hookLife} · 디테일 {hookFine} · 확대 {hookLod} · 단계 {tiers}");

            // 9) 성능: 화면 밖은 건너뛰고, 멀리서는 줄인다 (LINQ 를 매 프레임 그림 함수에서 쓰지 않는다)
            bool cull = src.Contains("_fixView.Intersects"), lod = Regex.Matches(src, @"x\.Lod\b").Count >= 30;
            var linq = fns.Where(kv => kv.Key.EndsWith("Life") && Regex.IsMatch(kv.Value, @"\.(Where|Select|Any|OrderBy|FirstOrDefault|Sum)\(|\.Count\(\w+\s*=>")).Select(kv => kv.Key).ToList();
            Check("성능 — 화면 밖 건너뛰기 · 멀리서 줄이기 · 움직임 함수에 LINQ 없음", cull && lod && linq.Count == 0,
                $"건너뛰기 {cull} · Lod 쓰는 곳 {Regex.Matches(src, @"x\.Lod\b").Count}" + (linq.Count > 0 ? $" · LINQ: {string.Join(",", linq)}" : ""));

            // 10) 장면: 그림이 읽는 상태가 Core 에서 실제로 생긴다 (가동 · 대기 · 고장 정지 · 고장 · 전기 없음)
            {
                var w = DayOne(seed, "Hanbit");
                var ms = w.Ship.Machines.Where(m => !m.Body.Room.Detached).OrderBy(m => m.Body.Id).ToList();
                int running = ms.Count(m => m.Faults.Count == 0 && m.Active && m.Efficiency > 0.01f);
                int standby = ms.Count(m => !m.Active);
                var target = ms.FirstOrDefault(m => m.Faults.Count == 0 && m.Spec.FaultKinds.Any() && m.Body.Type == FurnitureType.CoolantPump)
                             ?? ms.First(m => m.Faults.Count == 0 && m.Spec.FaultKinds.Any());
                var fault = w.Machines.Break(target, target.Spec.FaultKinds.First());
                bool broke = fault != null && target.Faults.Count > 0;
                int types = ms.Select(m => m.Body.Type).Distinct().Count();
                Check("장면 — 가동 · 대기 · 고장이 실제로 생겨 그림 상태가 갈린다", running > 0 && standby > 0 && broke,
                    $"설비 {ms.Count}대 ({types}종) · 가동 {running} · 대기 {standby} · 고장 낸 것 {FurnitureTypes.Name(target.Body.Type)} → 멈춤 {target.Stopped}");
            }

            // 11) 결정론 (시뮬레이션 지문은 그림과 상관없이 같다)
            {
                uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
                uint a = H(), b = H();
                Check("결정론 — 같은 시드 같은 지문", a == b, $"{a:x8} / {b:x8}");
            }
        }
        catch (Exception e) { Console.WriteLine(e); _fails++; }
        Console.WriteLine(_fails == 0 ? "\n✔ 설비 그림 표 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
