namespace ShipEngine;

/// <summary>
/// 작은 값부터 꺼내는 줄 (칸 int · 값 float) — .NET <see cref="PriorityQueue{TElement, TPriority}"/>&lt;int, float&gt; (기본 비교 · 네 갈래 더미)와 같은 셈:
/// 넣을 때 올리기 · 꺼낼 때 마지막 것을 뿌리에서 내리기를 그대로 해 같은 값이 여럿이면 꺼내는 차례까지 같다 (비교도 float.CompareTo 그대로 — NaN은 가장 작다).
/// 소리 퍼짐 · 길찾기가 쓴다 (마음 위기 틱 예산 고침): 처음부터 다 옮긴 기계어로 — 일반화 줄은 첫 위기 한가운데에서도 덜 옮긴 코드로 돌았다 (마흔 명이 한꺼번에 외친 틱).
/// </summary>
public sealed class Heap4
{
    private (int Element, float Priority)[] _nodes = new (int, float)[64];
    private int _size;

    public int Count => _size;

    public void Clear() => _size = 0;

    /// <summary>a.CompareTo(b) &lt; 0</summary>
    private static bool Less(float a, float b) => a < b || float.IsNaN(a) && !float.IsNaN(b);
    /// <summary>a.CompareTo(b) &lt;= 0</summary>
    private static bool LessEq(float a, float b) => !(a > b) && (float.IsNaN(a) || !float.IsNaN(b));

    [System.Runtime.CompilerServices.MethodImpl(Structure.Hot)]
    public void Enqueue(int element, float priority)
    {
        int at = _size;
        if (_nodes.Length == at) Array.Resize(ref _nodes, 2 * at);
        _size = at + 1;
        var nodes = _nodes;
        while (at > 0)
        {
            int parent = (at - 1) >> 2;
            var p = nodes[parent];
            if (!Less(priority, p.Priority)) break;
            nodes[at] = p;
            at = parent;
        }
        nodes[at] = (element, priority);
    }

    [System.Runtime.CompilerServices.MethodImpl(Structure.Hot)]
    public bool TryDequeue(out int element, out float priority)
    {
        if (_size == 0) { element = 0; priority = 0; return false; }
        var nodes = _nodes;
        (element, priority) = nodes[0];
        int size = --_size;
        if (size > 0)
        {
            var node = nodes[size];
            int at = 0, i;
            while ((i = (at << 2) + 1) < size)
            {
                var min = nodes[i];
                int minAt = i, upper = Math.Min(i + 4, size);
                while (++i < upper)
                {
                    var c = nodes[i];
                    if (Less(c.Priority, min.Priority)) { min = c; minAt = i; }
                }
                if (LessEq(node.Priority, min.Priority)) break;
                nodes[at] = min;
                at = minAt;
            }
            nodes[at] = node;
        }
        return true;
    }
}
