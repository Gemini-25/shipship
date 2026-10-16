using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.15 승무원 두뇌 2.0 — ⑥ 배우기: 잘된 방법 · 실패한 방법 · 남의 시범 · 어느 출처가 맞았나를 기억하고 다음 선택이 바뀐다.
// 한 번 헛걸음한 방법(차단기인 줄 알고 배전반에 갔는데 아니었다)은 다음 정전에 덜 고른다. 옆에서 남이 잘하는 걸 보면 조금 따라 배운다.

/// <summary>사람이 고르는 방법 (같은 목표에 여러 길).</summary>
public enum Method : byte
{
    StorePart, CraftPart, ProposeTrip, Handoff, AskHelp,
    ResetBreaker, PowerRoom, AskComputer, CheckPeople, StayPut, GoLit, CarryOn,
    CheckFire, Tell,
}

public sealed class LearningSystem
{
    private readonly World _w;
    private readonly Dictionary<int, Dictionary<Method, (float ok, float bad, long last)>> _m = new();
    private readonly Dictionary<int, float[]> _src = new();
    public int Records, Demos, Fails;

    public LearningSystem(World w) => _w = w;

    public static string Name(Method m) => m switch
    {
        Method.StorePart => "창고에서 부품 꺼내기",
        Method.CraftPart => "작업대에서 만들기",
        Method.ProposeTrip => "원정 제안",
        Method.Handoff => "부품 가져다 두기",
        Method.AskHelp => "도움 청하기",
        Method.ResetBreaker => "차단기 올리기",
        Method.PowerRoom => "발전 쪽 확인",
        Method.AskComputer => "컴퓨터에 묻기",
        Method.CheckPeople => "사람 챙기기",
        Method.StayPut => "제자리에서 기다리기",
        Method.GoLit => "밝은 곳으로",
        Method.CarryOn => "하던 일 계속",
        Method.CheckFire => "불 확인하러",
        _ => "알리러",
    };

    private Dictionary<Method, (float ok, float bad, long last)> Book(CrewMember c)
    {
        if (!_m.TryGetValue(c.Id, out var b)) _m[c.Id] = b = new();
        return b;
    }

    /// <summary>해 본 결과를 남긴다 (demo = 남이 하는 걸 봤다 — 절반만).</summary>
    public void Record(CrewMember c, Method m, bool ok, bool demo = false)
    {
        if (!BrainSystem.Enabled) return;
        var b = Book(c);
        var (o, x, _) = b.TryGetValue(m, out var v) ? v : (0f, 0f, 0L);
        float wgt = demo ? 0.5f : 1f;
        b[m] = ok ? (o + wgt, x, _w.Tick) : (o, x + wgt, _w.Tick);
        Records++;
        if (demo) Demos++;
        if (!ok && !demo) Fails++;
        if (!demo && c.Room is Room r)
        {
            // 남의 시범: 같은 방에서 깨어 있던 사람이 보고 배운다
            foreach (var o2 in _w.Crew)
                if (o2 != c && !o2.Dead && o2.IsAwake && o2.Room == r && !o2.IsChild) Record(o2, m, ok, demo: true);
            if (!ok) MarkLog.Add(c.Memory.Marks, _w.Tick, $"{Name(m)} — 이번엔 안 됐다");
        }
    }

    public (float ok, float bad) Tally(CrewMember c, Method m) => _m.TryGetValue(c.Id, out var b) && b.TryGetValue(m, out var v) ? (v.ok, v.bad) : (0f, 0f);

    /// <summary>그 방법을 얼마나 고르고 싶나 (1 = 그대로): 잘됐으면 오르고, 실패했으면 (최근일수록) 내려간다.</summary>
    public float Bias(CrewMember c, Method m)
    {
        if (!_m.TryGetValue(c.Id, out var b) || !b.TryGetValue(m, out var v)) return 1f;
        float recent = _w.Tick - v.last < SimTime.TicksPerDay * 3 ? 1f : 0.6f;
        float x = 1f + 0.4f * (v.ok - 1.6f * v.bad * recent) / (1f + v.ok + v.bad);
        return Math.Clamp(x, 0.3f, 1.5f);
    }

    /// <summary>출처 신뢰 (소문 · 방송 · 엿들음 …): 맞았던 출처는 더, 틀렸던 출처는 덜 믿는다.</summary>
    public float SourceTrust(CrewMember c, BeliefSource s) => _src.TryGetValue(c.Id, out var a) ? a[(int)s] : 1f;

    public void Credit(CrewMember c, BeliefSource s, bool right)
    {
        if (s == BeliefSource.Seen) return;
        if (!_src.TryGetValue(c.Id, out var a)) { _src[c.Id] = a = new float[9]; Array.Fill(a, 1f); }
        a[(int)s] = right ? MathF.Min(1.2f, a[(int)s] * 1.03f) : MathF.Max(0.5f, a[(int)s] * 0.9f);
    }

    /// <summary>화면용: 이 사람이 배운 것 (잘된 · 안 된 방법).</summary>
    public List<string> Lines(CrewMember c, int n = 3)
    {
        var list = new List<string>();
        if (!_m.TryGetValue(c.Id, out var b)) return list;
        foreach (var (m, v) in b.OrderByDescending(kv => kv.Value.last).ThenBy(kv => kv.Key))
        {
            if (list.Count >= n) break;
            list.Add(v.bad > v.ok ? $"{Name(m)}: 안 됐다 ×{v.bad:0.#}" : $"{Name(m)}: 잘됐다 ×{v.ok:0.#}");
        }
        return list;
    }

    public long Hash()
    {
        long h = 37;
        foreach (var (id, b) in _m) { h = h * 31 + id; foreach (var (m, v) in b) h = h * 31 + (int)m * 7 + (int)(v.ok * 10) + (int)(v.bad * 100); }
        return h;
    }
}
