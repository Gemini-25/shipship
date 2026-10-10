using System.Text.RegularExpressions;

namespace ShipSim.Core;

// v16.24 일기 말 다듬기 — 믿음 · 계획 기록에서 따온 메모 같은 말(이름표 · 괄호 · "~없음")을 사람이 쓴 문장으로.
//  "침실에 불 줄 알았는데 — 침실 불 없음 (직접 봄)" → "침실에 불이 난 줄 알았는데, 가 보니 침실 불은 없었다"
//  "정전 — 발전 쪽 확인 — 해냈다." → "정전 — 발전 쪽 확인했다."
// 말투(Persona.Say)는 이 뒤에 입혀지므로 사람마다 다른 맛은 그대로다. 글만 바꾼다 (결정론과 상관없다).
public static class DiaryWords
{
    private static readonly Regex Paren = new(@"\s*\([^()]*\)");
    private static readonly Regex Believed = new(@"(\S+(?: \S+)?) 줄 알았는데 — ");
    private static readonly Regex Done = new(@"(확인|수리|점검|정리|청소|보고|교대|치료|설치|준비|대피|정비|배달|운반|소독|환기|차단|복구) — 해냈다");

    /// <summary>메모 끝말 · 지난 일 · 꾸밈꼴 · 앞말에 조사를 붙이나(주어가 앞말).</summary>
    private static readonly (string memo, string past, string adn, bool subj)[] Ends =
    {
        ("불 없음", "불은 없었다", "불이 없는", false),
        ("구멍 없음", "구멍은 없었다", "구멍이 없는", false),
        ("불 켜짐", "불이 켜져 있었다", "불이 켜진", false),
        ("공기 나쁨", "공기가 나빴다", "공기가 나쁜", false),
        ("공기 괜찮음", "공기는 괜찮았다", "공기가 괜찮은", false),
        ("물 참", "물이 찼다", "물이 찬", false),
        ("바닥 마름", "바닥은 말라 있었다", "바닥이 마른", false),
        ("바닥 미끄러움", "바닥이 미끄러웠다", "바닥이 미끄러운", false),
        ("이상 기미", "이상한 기미가 있었다", "이상한 기미가 있는", false),
        ("어디 있는지 모름", "어디 있는지 몰랐다", "어디 있는지 모르는", true),
        ("쓰러짐", "쓰러져 있었다", "쓰러진", true),
        ("괜찮음", "괜찮았다", "괜찮은", true),
        ("캄캄함", "캄캄했다", "캄캄한", true),
        ("위험", "위험하다고 했다", "위험한", true),
        ("없음", "없었다", "없는", true),
    };

    private static readonly Regex[] EndRes = BuildEnds();

    private static Regex[] BuildEnds()
    {
        var r = new Regex[Ends.Length];
        for (int i = 0; i < Ends.Length; i++) r[i] = new Regex(@"(\S+) " + Regex.Escape(Ends[i].memo) + @"(?=[.,]|$)");
        return r;
    }

    public static string Plain(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = Paren.Replace(s, "");
        // "X 줄 알았는데 — Y" : X를 꾸밈꼴로 · 뒤는 "가 보니"
        s = Believed.Replace(s, m => Adnominal(m.Groups[1].Value) + " 줄 알았는데, 가 보니 ");
        s = s.Replace("에 불 줄 알았는데", "에 불이 난 줄 알았는데").Replace("에 구멍 줄 알았는데", "에 구멍이 난 줄 알았는데");
        s = Done.Replace(s, "$1했다");
        s = s.Replace(" — 뜻대로 안 됐다", ", 뜻대로 안 됐다");
        // 문장 끝의 메모 끝말 → 지난 일
        for (int i = 0; i < Ends.Length; i++)
        {
            var e = Ends[i];
            s = EndRes[i].Replace(s, m => (e.subj ? Ko.EunNeun(m.Groups[1].Value) : m.Groups[1].Value) + " " + e.past);
        }
        s = s.Replace("요즘 마음: ", "요즘 마음에 둔 것 — ");
        return s.Replace("  ", " ").Trim();
    }

    private static string Adnominal(string x)
    {
        if (x.EndsWith("에 불")) return x + "이 난";
        if (x.EndsWith("에 구멍")) return x + "이 난";
        foreach (var (memo, _, adn, subj) in Ends)
        {
            if (!x.EndsWith(" " + memo)) continue;
            string head = x[..^(memo.Length + 1)];
            return (subj ? Ko.IGa(head) : head) + " " + adn;
        }
        return x;
    }
}
