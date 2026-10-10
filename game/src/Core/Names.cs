using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>
/// v12.0 승무원 이름: 시드마다 다르게. 흔한 성은 더 자주, 이름은 요즘 이름과 조금 앞 세대 이름을 섞고,
/// 넷 중 하나쯤은 다른 나라에서 온 승무원 (한글로 짧게). 배의 난수와 따로 굴려서 이름이 바뀌어도 배의 역사는 같다.
/// 한 배 안에서는 성+이름도, 이름만도 겹치지 않는다 (부를 때 헷갈리지 않게).
/// </summary>
public static class NameGen
{
    // 성: (성, 무게) — 흔한 성일수록 무겁다
    private static readonly (string s, int w)[] Surnames =
    {
        ("김", 20), ("이", 15), ("박", 9), ("최", 5), ("정", 5), ("강", 3), ("조", 3), ("윤", 3), ("장", 3), ("임", 3),
        ("한", 2), ("오", 2), ("서", 2), ("신", 2), ("권", 2), ("황", 2), ("안", 2), ("송", 2), ("전", 2), ("홍", 2),
        ("유", 2), ("고", 2), ("문", 1), ("양", 1), ("손", 1), ("배", 1), ("백", 1), ("허", 1), ("남", 1), ("심", 1),
        ("노", 1), ("하", 1), ("곽", 1), ("성", 1), ("차", 1), ("주", 1), ("우", 1), ("구", 1), ("민", 1), ("류", 1),
        ("나", 1), ("진", 1), ("지", 1), ("엄", 1), ("채", 1), ("원", 1), ("천", 1), ("방", 1), ("공", 1), ("현", 1),
        ("함", 1), ("변", 1), ("염", 1), ("여", 1), ("추", 1), ("도", 1), ("소", 1), ("석", 1), ("선", 1), ("설", 1),
        ("마", 1), ("길", 1), ("연", 1), ("위", 1), ("표", 1), ("명", 1), ("기", 1), ("반", 1), ("왕", 1), ("금", 1),
        ("옥", 1), ("육", 1), ("인", 1), ("맹", 1), ("제", 1), ("모", 1), ("탁", 1), ("국", 1), ("은", 1), ("용", 1),
        ("예", 1), ("봉", 1), ("태", 1), ("목", 1), ("형", 1), ("피", 1), ("감", 1), ("동", 1), ("온", 1), ("호", 1),
        ("범", 1), ("승", 1), ("시", 1), ("황보", 1), ("남궁", 1), ("제갈", 1), ("선우", 1), ("독고", 1),
    };

    private static readonly string[] Given =
    {
        // 요즘 이름
        "서준", "하준", "도윤", "시우", "은우", "지호", "예준", "유준", "수호", "이준", "주원", "지후", "건우", "우진", "선우", "서진",
        "연우", "민준", "현우", "지훈", "은호", "태오", "이안", "로운", "시윤", "준서", "정우", "윤우", "도현", "승현", "재윤", "한결",
        "이든", "하람", "다온", "온유", "태민", "건호", "승우", "재민", "동현", "성민", "진우", "상현", "태윤", "준혁", "민재", "우빈",
        "서아", "하윤", "지안", "하은", "서윤", "지우", "수아", "아윤", "지유", "이서", "채원", "윤서", "다은", "소율", "시아", "하린",
        "예린", "수빈", "유나", "나은", "서연", "민서", "가은", "예나", "연서", "지원", "채아", "아린", "세아", "은서", "하율", "다인",
        "소윤", "서하", "채윤", "유진", "수연", "지민", "혜원", "새봄", "초아", "가람", "누리", "라온", "한별", "은솔", "이슬", "나래",
        "단비", "하늘", "다솜", "아라", "해솔", "솔비", "슬기", "예솔", "보라", "도하", "태이", "세빈", "지오", "하진", "로아", "시온",
        // 앞 세대 이름
        "영수", "경민", "상훈", "미정", "은영", "정희", "성호", "민정", "지영", "현주", "동욱", "재훈", "혜진", "수정", "용준", "세훈",
        "명수", "윤희", "은정", "경수", "태호", "진영", "선희", "기현", "종민", "미경", "상철", "혜숙", "광수", "순영", "정훈", "미란",
    };

    // 다른 나라에서 온 승무원 (한글로 적는다)
    private static readonly string[] ForeignGiven =
    {
        "엘레나", "라훌", "아나", "켄지", "아마라", "노아", "이반", "레아", "미라", "오마르", "소피아", "마테오", "레일라", "루카", "아이샤", "다비드",
        "니나", "타로", "유키", "파티마", "잉가", "밀라", "에밀", "야스민", "사샤", "아델", "나디아", "린", "웨이", "하나", "카이", "지아",
        "투안", "마야", "오스카", "리나", "에단", "올가", "사라", "빅토르", "아리아", "모건", "테오", "줄리아", "아콰", "비크람", "산티아고", "하미드",
    };

    private static readonly string[] ForeignSurname =
    {
        "실바", "메타", "모리", "오비", "켈러", "소콜", "뒤부아", "하산", "로시", "노박", "첸", "왕", "리", "팜", "사토", "다나카",
        "가르시아", "로페스", "오카포", "칸", "샤", "베르그", "이바노프", "마르틴", "블랑", "아리프", "누르", "오코너", "반스", "응우옌", "쿠마르", "페레이라",
        "요한손", "니엘센", "코바치", "오르테가", "무어", "레예스", "아사모아", "타나", "볼코바", "라이트", "콜", "스톤", "피셔", "베일리", "아다무", "올센",
    };

    /// <summary>한 배의 이름 n개 (시드마다 다르다).</summary>
    public static List<string> ForShip(int seed, int n)
    {
        var rng = new Rng(unchecked(seed * 1_000_003 ^ 0x4E414D45));
        var used = new HashSet<string>();
        var names = new List<string>(n);
        for (int i = 0; i < n; i++) names.Add(Next(rng, used));
        return names;
    }

    /// <summary>나중에 합류하는 사람 (구조한 생존자 등): 이미 있는 이름과 겹치지 않게.</summary>
    public static string Newcomer(int seed, int k, IEnumerable<string> existing)
    {
        var rng = new Rng(unchecked(seed * 7_919 + 104_729 * (k + 1) ^ 0x53555256));
        var used = new HashSet<string>();
        foreach (var e in existing) Remember(e, used);
        return Next(rng, used);
    }

    private static void Remember(string full, HashSet<string> used)
    {
        used.Add(full);
        used.Add(GivenOf(full));
    }

    /// <summary>부르는 이름 (성을 뺀 것: "김서준" → "서준", "엘레나 뒤부아" → "엘레나").</summary>
    public static string GivenOf(string full)
    {
        int sp = full.IndexOf(' ');
        if (sp > 0) return full[..sp];
        foreach (var (s, _) in Surnames.Where(x => x.s.Length == 2))
            if (full.StartsWith(s) && full.Length > 2) return full[2..];
        return full.Length > 1 ? full[1..] : full;
    }

    private static string Next(Rng rng, HashSet<string> used)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            string full, given;
            if (rng.Chance(0.25f))
            {
                given = ForeignGiven[rng.Range(0, ForeignGiven.Length)];
                // 성+이름이 너무 길면 (화면의 이름표) 짧은 성으로
                string sur = ForeignSurname[rng.Range(0, ForeignSurname.Length)];
                for (int k = 0; k < 6 && given.Length + sur.Length > 6; k++) sur = ForeignSurname[rng.Range(0, ForeignSurname.Length)];
                full = given.Length + sur.Length > 6 ? given : $"{given} {sur}";
            }
            else
            {
                given = Given[rng.Range(0, Given.Length)];
                full = Surname(rng) + given;
            }
            if (used.Contains(full) || used.Contains(given)) continue;
            used.Add(full);
            used.Add(given);
            return full;
        }
        return $"승무원{used.Count}";
    }

    private static string Surname(Rng rng)
    {
        int total = Surnames.Sum(x => x.w);
        int r = rng.Range(0, total);
        foreach (var (s, w) in Surnames)
        {
            r -= w;
            if (r < 0) return s;
        }
        return "김";
    }
}
