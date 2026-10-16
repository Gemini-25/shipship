using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

// v16.23 점검 — 게임 안 글에 개발 · 기획 용어가 새지 않았나 (기록 · 경보 · 말풍선 · 일기 · 컴퓨터 기록 · 사고 이름).
// 표에 한 줄 더하면 새 낱말을 잡는다.
public static partial class Program
{
    private static readonly (string Name, Regex Re)[] AuditTerms =
    {
        ("페일세이프", new Regex("페일 ?세이프")),
        ("이중화", new Regex("이중화")),
        ("트리아지", new Regex("트리아지")),
        ("모듈", new Regex("모듈")),
        ("AI", new Regex("(?<![A-Za-z])AI(?![A-Za-z])")),
        ("두뇌 2.0", new Regex("두뇌 ?2\\.0")),
        ("v1x.x", new Regex("(?<![A-Za-z0-9])v1[0-9]\\.[0-9]+")),
        ("상호작용", new Regex("상호 ?작용")),
        ("구획화", new Regex("구획화")),
        ("젤다", new Regex("젤다")),
        ("리던던시", new Regex("리던던시")),
        ("폴백", new Regex("폴백")),
        ("디버그", new Regex("디버그")),
        ("NPC", new Regex("(?<![A-Za-z])NPC(?![A-Za-z])")),
        ("스폰", new Regex("스폰")),
        ("쿨다운", new Regex("쿨 ?다운")),
        ("플레이어", new Regex("플레이어")),
    };

    /// <summary>글 하나를 훑어 낱말마다 센다 (예는 낱말마다 앞의 몇 개만).</summary>
    private static void AuditScanText(string? text, string where, Dictionary<string, int> hits, List<string> ex, HashSet<string> seen)
    {
        if (string.IsNullOrEmpty(text) || !seen.Add(text)) return;
        foreach (var (name, re) in AuditTerms)
        {
            if (!re.IsMatch(text)) continue;
            int n = hits.GetValueOrDefault(name);
            hits[name] = n + 1;
            if (ex.Count < 40 && ex.Count(e => e.StartsWith($"[{name}]")) < 3)
                ex.Add($"[{name}] {where}: {(text.Length > 90 ? text[..90] + "…" : text)}");
        }
    }

    private static readonly Regex AuditLiteral = new("@?\\$?\"(?:[^\"\\\\]|\\\\.)*\"");
    private static readonly Regex AuditHangul = new("[가-힣]");

    /// <summary>소스의 한국어 문자열 리터럴 정적 훑기 (주석은 뺀다): 낱말 → (수, 예 file:line).</summary>
    private static (Dictionary<string, int> hits, List<string> ex, int files) AuditScanSource(string root)
    {
        var hits = new Dictionary<string, int>();
        var ex = new List<string>();
        int files = 0;
        foreach (var dir in new[] { "game/src/Core", "game/src/View" })
        {
            string d = Path.Combine(root, dir);
            if (!Directory.Exists(d)) continue;
            foreach (var file in Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                files++;
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string t = line.TrimStart();
                    if (t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*") || !AuditHangul.IsMatch(line)) continue;
                    foreach (Match m in AuditLiteral.Matches(line))
                    {
                        // 리터럴 앞에 줄 주석이 있으면 주석 속이다
                        int c = CommentStart(line);
                        if (c >= 0 && c < m.Index) break;
                        string s = m.Value;
                        if (!AuditHangul.IsMatch(s)) continue;
                        foreach (var (name, re) in AuditTerms)
                        {
                            if (!re.IsMatch(s)) continue;
                            hits[name] = hits.GetValueOrDefault(name) + 1;
                            if (ex.Count(e => e.StartsWith($"[{name}]")) < 4)
                                ex.Add($"[{name}] {Path.GetRelativePath(root, file)}:{i + 1} {(s.Length > 70 ? s[..70] + "…\"" : s)}");
                        }
                    }
                }
            }
        }
        return (hits, ex, files);
    }

    /// <summary>문자열 밖의 첫 // 위치 (없으면 -1).</summary>
    private static int CommentStart(string line)
    {
        bool str = false;
        for (int i = 0; i < line.Length - 1; i++)
        {
            char ch = line[i];
            if (str) { if (ch == '\\') i++; else if (ch == '"') str = false; continue; }
            if (ch == '"') str = true;
            else if (ch == '\'' && i + 2 < line.Length && line[i + 2] == '\'') i += 2;
            else if (ch == '/' && line[i + 1] == '/') return i;
        }
        return -1;
    }
}
