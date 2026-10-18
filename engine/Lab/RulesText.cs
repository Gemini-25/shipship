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
    }
}
