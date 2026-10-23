using System.Text.RegularExpressions;
using ShipEngine;

namespace ShipView;

/// <summary>
/// 화면 글 금지어 (docs/MIND.md 4단 — 게임 안 글에는 개발 용어를 쓰지 않는다): 속말 · 풍선 · 말 창 · 로봇 카드 (전지 줄 빼고)가 지키는 규칙.
/// 걸리는 것: 로마자 · 숫자 바로 뒤의 단위 (kPa · 몰 · ℃ · % · 틱 …) · 숫자 덩이 둘 이상 (시각 HH:MM과 그 세계의 이름 속 숫자 '화물칸 8' · '로봇 2'는 빼고 센다) · 개발 말 · 못 채운 자리 '{…}' · 빈 글.
/// 엔진 글(원인 글 · 일지 · LastFail)은 찍히는 값이라 엔진에서 고치지 않고 — 화면에서만 정리한다 (<see cref="Clean"/>).
/// </summary>
public static class TextLint
{
    private static readonly Regex Latin = new("[A-Za-z]", RegexOptions.Compiled);
    private static readonly Regex Unit = new(@"\d[\d.,]*\s*(kPa|몰|℃|°C|m/s|km|kg|L\b|ppm|%|틱|ms|µs|dB)", RegexOptions.Compiled);
    private static readonly Regex Clock = new(@"\b\d{1,2}:\d{2}\b", RegexOptions.Compiled);
    private static readonly Regex Num = new(@"\d+([.,]\d+)?", RegexOptions.Compiled);
    private static readonly Regex Dev = new(@"펼침|NaN|null|[{}_]", RegexOptions.Compiled);
    private static readonly Regex GoalHead = new(@"^([A-Z][A-Za-z]+):\s*", RegexOptions.Compiled);
    private static readonly Regex Paren = new(@"\s*\([^()]*\d[^()]*\)", RegexOptions.Compiled);
    private static readonly Regex UnitChunk = new(@"\s*\d[\d.,]*\s*(kPa|몰|℃|°C|m/s|km|kg|ppm|%|틱|ms|µs|dB)", RegexOptions.Compiled);

    /// <summary>걸린 까닭 (없으면 null). names: 그 세계의 방 · 사람 · 로봇 이름 (이름 속 숫자는 세지 않는다).</summary>
    public static string? Bad(string s, IEnumerable<string>? names = null)
    {
        if (string.IsNullOrWhiteSpace(s)) return "빈 글";
        if (Dev.IsMatch(s)) return "개발 말 · 못 채운 자리";
        if (Latin.IsMatch(s)) return "로마자";
        if (Unit.IsMatch(s)) return "숫자와 단위";
        string t = Clock.Replace(s, " ");
        if (names != null) foreach (var n in names.Where(n => n.Any(char.IsDigit)).OrderByDescending(n => n.Length)) t = t.Replace(n, " ");
        if (Num.Matches(t).Count >= 2) return "숫자 덩이 둘 이상";
        return null;
    }

    /// <summary>엔진 글을 화면 글로: 앞머리 영문 목표 'Wash: …' → '씻는다: …' · 숫자가 든 괄호 덩이 · 숫자와 단위 덩이를 뗀다.</summary>
    public static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = GoalHead.Replace(s, m => Enum.TryParse<GoalKind>(m.Groups[1].Value, out var g) && Mind.GoalName(g) is var n && n.Length > 0 ? n + ": " : "");
        s = Paren.Replace(s, "");
        s = UnitChunk.Replace(s, "");
        return s.Trim();
    }
}
