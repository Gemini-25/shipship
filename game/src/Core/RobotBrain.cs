using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v23 주컴퓨터 대개편 3단계: 로봇 두뇌 (주 컴퓨터보다 약한 움직이는 인공지능).
//   · 경험: 지휘 건을 끝까지 함께하면 그 사건 종류(불 · 파공 · 부상자)의 솜씨가 는다 — 불은 거품을 더 잘 겨누고 · 파공은 더 빨리 봉합한다.
//   · 위험 판단: 지휘 건에서 다친 적이 많은 로봇은 목숨이 걸리지 않은 큰 불이면 덜 불린다 (잃으면 되살리기 힘든 몸도).
//   · 동료와 손발: 여러 번 같이 뛴 로봇끼리는 증원 때 먼저 불리고 · 같은 불에 붙으면 손발이 맞아 거품이 더 잘 먹힌다.
//   · 사람 기억: 다친 사람 곁에 간 적이 있는 로봇은 그 사람이 다시 다치면 먼저 간다.
//   · 말투: 평소엔 제 버릇대로 · 위급하면 짧게 ("기관실 불 · 진입 · 거품 60%").

public sealed partial class Robot
{
    /// <summary>사건 종류마다 끝까지 함께한 지휘 건 수 (불 · 파공 · 부상자).</summary>
    public List<int> CaseExp { get; } = new() { 0, 0, 0 };
    /// <summary>지휘 건에서 몸을 다친 수 (단계가 넘어간 만큼).</summary>
    public int Scares { get; internal set; }
    /// <summary>같이 뛴 로봇 → 횟수.</summary>
    public Dictionary<int, int> Mates { get; } = new();
    /// <summary>곁에 가 도운 사람 → 횟수.</summary>
    public Dictionary<int, int> Known { get; } = new();
}

public static class RobotBrain
{
    public static bool On = true;

    public static int Exp(Robot r, CaseKind k) => r.CaseExp.Count > (int)k ? r.CaseExp[(int)k] : 0;

    /// <summary>말버릇 (번호 · 종류로 정해진다): 또박또박 · 수다 · 무뚝뚝 · 다정.</summary>
    public static int Style(Robot r) => (int)((uint)(r.Id * 2246822519u + (uint)r.Kind * 3266489917u) >> 20) % 4;
    public static string StyleName(Robot r) => Style(r) switch { 0 => "또박또박", 1 => "수다스러움", 2 => "무뚝뚝", _ => "다정함" };

    /// <summary>평소 말: 버릇대로 붙인다.</summary>
    public static string Calm(Robot r, string text, long tick) => !On ? text : Style(r) switch
    {
        0 => $"보고 — {text}",
        1 => (tick / 1500 + r.Id) % 3 == 0 ? $"{text} · 오늘도 바쁘네요" : $"{text} · 금방 할게요",
        2 => text,
        _ => $"{text} · 다들 괜찮죠?",
    };

    /// <summary>위급할 때 말: 짧게 — 자리 · 할 일 · 남은 것.</summary>
    public static string Terse(Robot r, string where, string act) =>
        $"{where} · {act}" + (RobotsV15.Fights(r.Kind) || r.Draft == DraftKind.Fire ? $" · 거품 {r.Foam * 100:0}%" : "") + (r.Battery < 0.4f ? $" · 전지 {r.Battery * 100:0}%" : "");

    /// <summary>불 겨누기: 불 경험이 쌓일수록 (다섯 번까지 +30%) · 손발 맞는 동료가 같은 방에서 뿌리면 +10%.</summary>
    public static float FireMul(Robot r, World w)
    {
        if (!On) return 1f;
        float m = 1f + 0.06f * Math.Min(5, Exp(r, CaseKind.Fire));
        if (r.Mates.Count > 0 && r.Room is Room room)
            foreach (var o in w.Robots.Robots)
                if (o != r && o.FightingFire && o.Room == room && r.Mates.TryGetValue(o.Id, out var n) && n >= 2) { m += 0.1f; break; }
        return m;
    }

    /// <summary>봉합 일 시간 배율: 파공 경험 다섯 번까지 −25%.</summary>
    public static float SealMul(Robot r) => On ? 1f - 0.05f * Math.Min(5, Exp(r, CaseKind.Breach)) : 1f;

    /// <summary>지휘관이 고를 때 거리 배율: 경험 · 손발 · 아는 사람 · 남은 힘 (목숨이 걸리면 아끼지 않는다).</summary>
    public static float PickMul(World w, Robot r, CommandCase k, IReadOnlyList<int> onScene)
    {
        if (!On) return 1f;
        float m = 1f - 0.04f * Math.Min(5, Exp(r, k.Kind));
        foreach (var id in onScene)
            if (r.Mates.TryGetValue(id, out var n) && n >= 2) { m *= 0.85f; break; }
        if (k.Kind == CaseKind.Casualty && k.CrewId >= 0 && r.Known.TryGetValue(k.CrewId, out var kn) && kn >= 1) m *= 0.7f;
        if (!k.Life)
        {
            float frail = (1f - r.Condition) + 0.15f * RobotBody.Damaged(r);
            m *= 1f + frail;
            if (k.Kind == CaseKind.Fire && k.Size >= 6 && r.Scares >= 3 && !RobotsV15.Fireproof(r.Kind)) m *= 1.3f; // 덴 적이 많다 — 큰 불엔 망설인다
        }
        return MathF.Max(0.3f, m);
    }

    /// <summary>지휘 건이 닫혔다: 끝까지 함께한 로봇의 경험 · 동료 · 사람 기억.</summary>
    public static void AfterCase(World w, CommandCase k, bool success)
    {
        if (!On) return;
        var bots = k.Served.Select(id => w.Robots.Robots.FirstOrDefault(r => r.Id == id)).Where(r => r != null && !r.Wrecked).Cast<Robot>().ToList();
        foreach (var r in bots)
        {
            if (success)
            {
                while (r.CaseExp.Count < 3) r.CaseExp.Add(0);
                r.CaseExp[(int)k.Kind]++;
                if (r.CaseExp[(int)k.Kind] == 5) MarkLog.Add(r.Marks, w.Tick, $"{IncidentCommander.KindName(k.Kind)} 다섯 번째 — 이제 손에 익었다");
            }
            foreach (var o in bots) if (o != r) r.Mates[o.Id] = r.Mates.TryGetValue(o.Id, out var n) ? n + 1 : 1;
            if (k.Kind == CaseKind.Casualty && k.CrewId >= 0) r.Known[k.CrewId] = r.Known.TryGetValue(k.CrewId, out var c) ? c + 1 : 1;
        }
    }

    /// <summary>로봇 카드 한 줄: 경험 · 동료 · 아는 사람.</summary>
    public static string Line(World w, Robot r)
    {
        var bits = new List<string>();
        int f = Exp(r, CaseKind.Fire), b = Exp(r, CaseKind.Breach), c = Exp(r, CaseKind.Casualty);
        if (f + b + c > 0) bits.Add($"불 {f} · 파공 {b} · 부상 {c}");
        if (r.Scares > 0) bits.Add($"다친 적 {r.Scares}");
        var mate = r.Mates.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).FirstOrDefault();
        if (mate.Value >= 2 && w.Robots.Robots.FirstOrDefault(x => x.Id == mate.Key) is Robot m) bits.Add($"손발: {m.Name}({mate.Value})");
        var known = r.Known.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).FirstOrDefault();
        if (known.Value >= 1 && w.Crew.FirstOrDefault(x => x.Id == known.Key) is CrewMember p) bits.Add($"챙기는 사람: {p.Name}");
        bits.Add(StyleName(r));
        return string.Join(" · ", bits);
    }
}
