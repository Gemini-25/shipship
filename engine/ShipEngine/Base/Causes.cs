namespace ShipEngine;

/// <summary>원인의 종류.</summary>
public enum CauseKind : byte
{
    None,
    Story,      // 이야기꾼이 심은 것
    Wear,       // 마모 · 피로 · 낡음
    Corrosion,  // 부식
    Impact,     // 바깥 물체의 충돌
    Pressure,   // 압력 차 (하중)
    Heat,       // 열 (과열 · 열 약화)
    Fire,       // 불
    Breach,     // 구멍
    Leak,       // 샘 (배관 · 탱크)
    Electric,   // 전기 (과부하 · 합선 · 불꽃)
    Reaction,   // 화학 반응
    Person,     // 사람의 행동
    Robot,      // 로봇 · 드론
    Computer,   // 주컴퓨터의 결정
    Machine,    // 장치 고장
    Weather,    // 행성 날씨
    Flare,      // 별 폭발 · 방사선
    Biology,    // 미생물 · 곰팡이 · 작물
    Setup,      // 처음부터 그랬다 (시험 장면)
    Route,      // ⑤ 항로 — 정해진 점화 · 위험 구간
    Contact,    // ⑤ 다른 배
    Society,    // ⑨ 사람 사이에서 생긴 것 (탓 · 정 · 무리 · 회의의 결정 · 대화) — 한 사람의 행동이 아니라 탓할 사람이 아니다
    Supply,     // ⑪ 물자 · 일 (떨어진 물자 · 막힌 공정 · 배급)
}

/// <summary>원인 마디: 무엇 · 어디 · 언제 · 부모 원인 (둘까지).</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)] // 찍기는 바이트를 그대로 쓴다 — 정렬용 빈 바이트(쓰레기 값)가 없게
public struct CauseNode
{
    public CauseKind Kind;
    public int Cell;      // 칸 번호 (-1: 배 전체 · 바깥)
    public long Tick;
    public int Parent;    // 0: 없음
    public int Parent2;
    public int Text;      // 글 번호
    public int Actor;     // 누가 (-1: 없음)
}

/// <summary>
/// 원인 그래프. 상태를 크게 바꾸는 모든 것에 원인 마디를 붙인다 — 연출의 원인 사슬 · 사고 조사 · 믿음이 읽는다.
/// 번호 0은 '원인 없음'. 마디는 병렬 구역 밖에서만 더한다 (번호가 늘 같은 순서로 붙게).
/// </summary>
public sealed class Causes : IState
{
    private readonly List<CauseNode> _nodes = new() { default };
    private readonly List<string> _texts = new() { "" };
    private readonly Dictionary<string, int> _textIds = new() { [""] = 0 };

    public int Count => _nodes.Count;
    public CauseNode this[int id] => (uint)id < (uint)_nodes.Count ? _nodes[id] : default;
    public string Text(int id) => _texts[this[id].Text];

    public int Add(CauseKind kind, string text, long tick, int cell = -1, int parent = 0, int parent2 = 0, int actor = -1)
    {
        if (!_textIds.TryGetValue(text, out int t)) { t = _texts.Count; _texts.Add(text); _textIds[text] = t; }
        // 같은 자리에서 같은 일이 또 일어나면 (금이 더 자람 · 불이 또 번짐) 새 마디를 달지 않고 그 마디를 잇는다 — 이야기가 '금이 갔다 → 금이 갔다 → …'로 되풀이되지 않게
        // (몇 마디 위까지: '뜨거워 약해졌다 → 찢어졌다 → 뜨거워 약해졌다 → …'처럼 번갈아 되풀이되는 것도)
        if (parent2 == 0)
            for (int p = parent, k = 0; p > 0 && p < _nodes.Count && k < 6; p = _nodes[p].Parent, k++)
                if (_nodes[p].Text == t && _nodes[p].Cell == cell) return p;
        _nodes.Add(new CauseNode { Kind = kind, Cell = cell, Tick = tick, Parent = parent, Parent2 = parent2, Text = t, Actor = actor });
        return _nodes.Count - 1;
    }

    /// <summary>뿌리부터 이 마디까지 (첫 부모를 따라).</summary>
    public List<int> Chain(int id, int max = 64)
    {
        var list = new List<int>();
        var seen = new HashSet<int>();
        while (id > 0 && list.Count < max && seen.Add(id)) { list.Add(id); id = this[id].Parent; }
        list.Reverse();
        return list;
    }

    public int Root(int id) { var c = Chain(id); return c.Count > 0 ? c[0] : 0; }
    public int Depth(int id) => Chain(id).Count;

    /// <summary>"합선 → 피복이 탄다 → 연기" 처럼.</summary>
    public string Story(int id)
    {
        // 같은 말이 잇따르면 한 번만 (불이 칸마다 번진 것: '옆 칸으로 번졌다 ×3')
        var parts = new List<string>();
        string? last = null; int n = 0;
        foreach (int c in Chain(id))
        {
            string t = Text(c);
            if (t == last) { n++; continue; }
            if (last != null) parts.Add(n > 1 ? $"{last} ×{n}" : last);
            last = t; n = 1;
        }
        if (last != null) parts.Add(n > 1 ? $"{last} ×{n}" : last);
        return string.Join(" → ", parts);
    }

    public void Save(StateWriter w)
    {
        w.Tag("causes");
        w.List(_nodes);
        w.I32(_texts.Count);
        foreach (var s in _texts) w.Str(s);
    }

    public void Load(StateReader r)
    {
        r.Tag("causes");
        _nodes.Clear(); _nodes.AddRange(r.List<CauseNode>());
        _texts.Clear(); _textIds.Clear();
        int n = r.I32();
        for (int i = 0; i < n; i++) { string s = r.Str() ?? ""; _texts.Add(s); _textIds.TryAdd(s, i); }
    }
}
