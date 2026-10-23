using System.Text.RegularExpressions;
using ShipEngine;

namespace Lab;

/// <summary>글: 데이터 표의 자리 채움({곳} · {상대} …) 뒤에 조사를 그냥 붙이면 받침과 어긋난다 ('둘레 통로을') — 조사를 고르는 자리({곳을})를 쓴다.</summary>
public static partial class Program
{
    private static void TextRules()
    {
        if (!Do("글 조사")) return;
        Console.WriteLine("\n[글 — 자리 채움 뒤 조사]");
        var re = new Regex(@"\{[^}]+\}(을|를|이|가|은|는|과|와|으로|로)(?=[\s!?.,""…])");
        var bad = new List<string>();
        foreach (var n in DataFiles.Names().Where(n => n.EndsWith(".json") && !n.StartsWith("ships/")))
            foreach (Match mt in re.Matches(DataFiles.Text(n))) bad.Add($"{n}: {mt.Value}");
        Check("데이터 글의 자리 채움 뒤에 조사를 그냥 붙이지 않는다 ('{곳}을' → '{곳을}' — 받침에 맞춰 고른다)",
            bad.Count == 0, bad.Count == 0 ? $"표 {DataFiles.Names().Count(n => n.EndsWith(".json") && !n.StartsWith("ships/"))}개 · 0곳" : string.Join(" · ", bad.Take(5)));

        // 화면 글 금지어 (docs/MIND.md 4단): 속말 · 풍선 · 말 창 · 로봇 카드가 지킨다 — 영문 목표 · 개발 말 · 숫자와 단위 · 숫자 덩이 둘 이상 (시각 · 이름 속 숫자는 빼고)
        {
            string[] bads = { "Wash: 샤워 칸에서 물이 나오지 않는다", "펼침 12", "산소 97%", "기압 101.3kPa", "{곳으로} 가자", "" };
            string[] goods = { "몰라. 나도 처음 봐", "09:14 막혔다 — 문이 끼었다", "로봇 2 → 화물칸 8 불 · 가장 가까웠다" };
            var names = new[] { "로봇 2", "화물칸 8" };
            var caught = bads.Where(x => ShipView.TextLint.Bad(x) != null).ToList();
            var falsePos = goods.Where(x => ShipView.TextLint.Bad(x, names) != null).Select(x => $"{x} ({ShipView.TextLint.Bad(x, names)})").ToList();
            string cleaned = ShipView.TextLint.Clean("Wash: 샤워 칸에서 물이 나오지 않는다 (셈 30)");
            var lineBad = new List<string>();
            foreach (var (sit, st, l) in ShipView.Inner.AllLines())
            {
                string f = l.Replace("{곳으로}", "식당으로").Replace("{곳에}", "식당에").Replace("{곳}", "식당").Replace("{일}", "불을 끈다").Replace("{까닭}", "문이 끼었다").Replace("{취미}", "책을 읽는다");
                if (ShipView.TextLint.Bad(f) is string why) lineBad.Add($"{sit}/{st}: {f} ({why})");
            }
            Check("화면 글 금지어: 영문 목표 · 개발 말 · 숫자와 단위 · 못 채운 자리 · 빈 글은 걸리고 · '몰라' · 시각 · 이름 속 숫자는 걸리지 않는다 · 정리기는 'Wash: …' → '씻는다: …' (숫자 괄호를 뗀다) · 속말 표의 모든 줄은 자리를 채운 뒤 걸리지 않는다",
                caught.Count == bads.Length && falsePos.Count == 0 && cleaned == "씻는다: 샤워 칸에서 물이 나오지 않는다" && ShipView.TextLint.Bad(cleaned) == null && lineBad.Count == 0,
                $"걸림 {caught.Count}/{bads.Length} · 헛걸림 [{string.Join(" | ", falsePos)}] · 정리 '{cleaned}' · 표 줄 걸림 {lineBad.Count} {string.Join(" | ", lineBad.Take(3))}");
        }
    }
}
