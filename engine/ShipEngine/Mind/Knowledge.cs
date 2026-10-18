namespace ShipEngine;

/// <summary>⑧ · ⑩ 판단 엔진 — 분야 (knowledge.json domains의 차례). 상식은 모두가 안다.</summary>
public enum Domain : byte { Common, Pressure, Electric, Chemistry, Body, Structure }

/// <summary>원리 — 풀이 법칙 하나씩 (Solver의 뜻 · knowledge.json principles의 이름과 짝).</summary>
public enum Principle : byte { OpenDoor, SpaceZero, Breath, Jammed, DoorLimit, Valve, PumpBack, Fill, Unjam, Patch, AirCool, DoorGap }

/// <summary>행동 — 그림 위의 한 걸음 (knowledge.json actions의 이름과 짝).</summary>
public enum ActKind : byte { Open, Close, Go, Unjam, Pump, Fill, Clear, Patch, Tape, Seal }

public sealed class PrincipleDef { public string Domain = ""; public double Need; public string Text = ""; }
public sealed class ActionDef { public string Who = "any"; public string Domain = ""; public List<string> Needs = new(), Limits = new(); public double Minutes; public string Text = ""; }
public sealed class ManualDef { public string Text = ""; public List<string> Methods = new(); }

/// <summary>
/// 지식 표 (engine/data/knowledge.json) — 분야 · 원리 · 행동 · 기본 비상 절차서. 원리의 뜻은 코드(Solver)가 · 누가 아는지와 글은 표가 정한다.
/// 표의 원리 · 행동 이름은 코드의 것과 하나도 빠짐없이 짝이어야 한다 (새 원리 = 표 한 줄 + 법칙 하나).
/// </summary>
public sealed class KnowledgeTable
{
    public static readonly string[] DomainNames = { "상식", "기압", "전기", "화학", "몸", "구조" };
    public static readonly string[] PrincipleNames = { "열린문", "트인방", "숨", "끼인문", "기압차문", "밸브", "되돌리기", "채우기", "바로잡기", "구멍막기", "공기냉각", "문틈" };
    public static readonly string[] ActionNames = { "open", "close", "go", "unjam", "pump", "fill", "clear", "patch", "tape", "seal" };

    public List<string> Domains = new();
    public Dictionary<string, PrincipleDef> Principles = new();
    public Dictionary<string, ActionDef> Actions = new();
    public Dictionary<string, ManualDef> Manual = new();

    // 풀어 둔 것
    public PrincipleDef[] P = Array.Empty<PrincipleDef>();
    public Domain[] PDomain = Array.Empty<Domain>();
    public ActionDef[] A = Array.Empty<ActionDef>();
    public uint[] ANeeds = Array.Empty<uint>(), ALimits = Array.Empty<uint>();
    public Domain[] ADomain = Array.Empty<Domain>();

    private static KnowledgeTable? _cache;
    public static KnowledgeTable Load() => _cache ??= Resolve(DataFiles.Load<KnowledgeTable>("knowledge.json"));

    private static KnowledgeTable Resolve(KnowledgeTable k)
    {
        if (!k.Domains.SequenceEqual(DomainNames)) throw new InvalidDataException($"지식 표: 분야는 {string.Join(" · ", DomainNames)} 차례여야 한다");
        Domain Dom(string what, string d) { int i = Array.IndexOf(DomainNames, d); return i >= 0 ? (Domain)i : throw new InvalidDataException($"지식 표: {what}의 분야 '{d}'를 모른다"); }
        foreach (var n in k.Principles.Keys) if (Array.IndexOf(PrincipleNames, n) < 0) throw new InvalidDataException($"지식 표: 원리 '{n}'의 뜻(풀이 법칙)이 코드에 없다");
        foreach (var n in k.Actions.Keys) if (Array.IndexOf(ActionNames, n) < 0) throw new InvalidDataException($"지식 표: 행동 '{n}'의 뜻이 코드에 없다");
        k.P = PrincipleNames.Select(n => k.Principles.TryGetValue(n, out var p) ? p : throw new InvalidDataException($"지식 표: 원리 '{n}'가 없다")).ToArray();
        k.PDomain = PrincipleNames.Select(n => Dom($"원리 '{n}'", k.Principles[n].Domain)).ToArray();
        k.A = ActionNames.Select(n => k.Actions.TryGetValue(n, out var a) ? a : throw new InvalidDataException($"지식 표: 행동 '{n}'가 없다")).ToArray();
        k.ADomain = ActionNames.Select(n => Dom($"행동 '{n}'", k.Actions[n].Domain)).ToArray();
        uint Mask(string act, List<string> names)
        {
            uint m = 0;
            foreach (var p in names) { int i = Array.IndexOf(PrincipleNames, p); if (i < 0) throw new InvalidDataException($"지식 표: 행동 '{act}'가 쓰는 원리 '{p}'가 없다"); m |= 1u << i; }
            return m;
        }
        k.ANeeds = ActionNames.Select(n => Mask(n, k.Actions[n].Needs)).ToArray();
        k.ALimits = ActionNames.Select(n => Mask(n, k.Actions[n].Limits)).ToArray();
        foreach (var (n, a) in k.Actions) if (a.Who is not ("any" or "body" or "computer")) throw new InvalidDataException($"지식 표: 행동 '{n}'의 누가 '{a.Who}'를 모른다");
        foreach (var (n, p) in k.Principles) if (p.Need < 0 || p.Need > 1) throw new InvalidDataException($"지식 표: 원리 '{n}'의 문턱은 0~1");
        return k;
    }

    /// <summary>분야를 아는 정도 → 아는 원리 (상식은 늘 · 문턱을 넘게 알면).</summary>
    public Know KnowOf(ReadOnlySpan<float> level)
    {
        uint bits = 0;
        for (int i = 0; i < P.Length; i++)
        {
            var d = PDomain[i];
            float l = d == Domain.Common ? 1f : (int)d < level.Length ? level[(int)d] : 0f;
            if (l >= P[i].Need) bits |= 1u << i;
        }
        return new Know { Bits = bits };
    }

    /// <summary>그 행동을 떠올릴 수 있나 (쓰는 원리를 다 안다).</summary>
    public bool CanThink(ActKind a, Know k) => (ANeeds[(int)a] & ~k.Bits) == 0;
}

/// <summary>아는 원리 (비트) — 주컴퓨터는 다 안다.</summary>
public struct Know
{
    public uint Bits;
    public readonly bool Has(Principle p) => (Bits & (1u << (int)p)) != 0;
    public static Know All => new() { Bits = (1u << KnowledgeTable.PrincipleNames.Length) - 1 };
    public Know Without(Principle p) => new() { Bits = Bits & ~(1u << (int)p) };
}
