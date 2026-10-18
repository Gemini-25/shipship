namespace ShipEngine;

/// <summary>
/// ⑧ · ⑩ 판단 엔진 (다) 그림 — 풀이기가 보는 작은 세계 (docs/ENGINE.md 판단 엔진).
/// 문제에 닿는 방만(자리) + 선체 바깥 한 자리 · 자리마다 기압 · 부피 · 구멍 · 사람 · 통풍구 / 문마다 두 자리 · 열림 · 끼임 · 전기 / 일할 몸.
/// 주컴퓨터는 센서로 · 사람은 믿음으로 그린다 — 그림이 틀리면 계획도 틀리고, 해 보다 막혀서 안다.
/// </summary>
public sealed class Picture
{
    public int P;                         // 자리 수 (방들 + 바깥)
    public int Out = -1;                  // 선체 바깥 자리 (-1: 그림에 없다)
    public int[] Room = Array.Empty<int>();       // 자리 → 방 번호 (-1: 바깥)
    public string[] Name = Array.Empty<string>();
    public float[] Cap = Array.Empty<float>();    // 101.325kPa · 20℃에서의 몰 (부피)
    public int[] Cells = Array.Empty<int>();
    public bool[] Vent = Array.Empty<bool>();     // 통풍구가 있고 전기가 들어온다 (되돌리기 · 채우기)
    public bool[] Hull = Array.Empty<bool>();     // 선체 바깥에서 그 방 외판에 닿는다 (구멍을 바깥에서 막는다)
    public bool[] Brain = Array.Empty<bool>();    // 주컴퓨터가 있다 (공기가 빠지면 식지 못해 멈춘다 — 공기냉각)
    public int D;
    public int[] DA = Array.Empty<int>(), DB = Array.Empty<int>();   // 문 → 두 자리
    public int[] Door = Array.Empty<int>();       // 문 → 구조의 문 번호
    public bool[] Ext = Array.Empty<bool>(), Powered = Array.Empty<bool>();
    public float[] Limit = Array.Empty<float>();  // 그 문이 열리는 기압 차 (kPa)
    // 지금 형편
    public float[] KPa = Array.Empty<float>();
    public bool[] Hole = Array.Empty<bool>();
    public byte[] People = Array.Empty<byte>();   // 우주복 없는 사람
    public bool[] Open = Array.Empty<bool>(), Jam = Array.Empty<bool>();
    public bool[] Leaky = Array.Empty<bool>();    // 닫혀도 틈으로 새는 보통 문 (틈을 막지 않았다)
    public bool[] Normal = Array.Empty<bool>();   // 보통 문 (압력 문 · 에어락 문이 아니다 — 열면 막은 틈이 떨어진다)
    public int Body = -1;                 // 일할 몸이 있는 자리 (-1: 없다)
    public bool BodyBreathes;             // 그 몸이 우주복 없는 사람이다 (보관함에서 입을 수 있다)
    public bool[] Lockers = Array.Empty<bool>();  // 우주복 보관함이 있고 우주복이 남았다
    public int Kits = 99;                 // 몸이 쓸 수 있는 봉합 꾸러미 (사람 — 든 것 + 그림 안 보관함 · 로봇은 제 것이 있다)
    public int Goal = -1;                 // 이 자리의 구멍을 막는다
    public float AirValue = 0.02f;        // 잃는 공기 1몰의 값 (분)

    public const float MolPerCellAtm = (float)(Field.CellVolume * 101_325 / (Field.R * 293.15));

    /// <summary>자리 하나를 더한다 (방 번호 · 이름 · 칸 수 · 기압).</summary>
    public int AddPlace(int room, string name, int cells, float kpa, bool vent = false, bool hull = false, bool hole = false, byte people = 0)
    {
        int p = P++;
        Room = Room.Append(room).ToArray(); Name = Name.Append(name).ToArray(); Cells = Cells.Append(cells).ToArray();
        Cap = Cap.Append(cells * MolPerCellAtm).ToArray(); Vent = Vent.Append(vent).ToArray(); Hull = Hull.Append(hull).ToArray(); Brain = Brain.Append(false).ToArray(); Lockers = Lockers.Append(false).ToArray();
        KPa = KPa.Append(kpa).ToArray(); Hole = Hole.Append(hole).ToArray(); People = People.Append(people).ToArray();
        return p;
    }

    public int AddOut() { Out = AddPlace(-1, "선체 바깥", 0, 0); return Out; }

    /// <summary>문 하나 (두 자리 · 구조 문 번호 · 전기 · 바깥문 · 열림 · 끼임).</summary>
    public int AddDoor(int a, int b, int door = -1, bool powered = true, bool open = false, bool jam = false, bool leaky = false, bool normal = false)
    {
        normal |= leaky;
        int k = D++;
        bool ext = a == Out || b == Out;
        DA = DA.Append(a).ToArray(); DB = DB.Append(b).ToArray(); Door = Door.Append(door).ToArray();
        Ext = Ext.Append(ext).ToArray(); Powered = Powered.Append(powered).ToArray();
        Limit = Limit.Append(powered ? (ext ? 5f : 30f) : 2f).ToArray();
        Open = Open.Append(open).ToArray(); Jam = Jam.Append(jam).ToArray(); Leaky = Leaky.Append(leaky).ToArray(); Normal = Normal.Append(normal).ToArray();
        return k;
    }

    public string DoorName(int k) => DA[k] == Out || DB[k] == Out ? $"{Name[DA[k] == Out ? DB[k] : DA[k]]} 바깥문" : $"{Name[DA[k]]}–{Name[DB[k]]} 문";
}

/// <summary>계획의 한 걸음 (행동 · 대상 — 문이면 그림의 문 번호 · 방이면 자리 번호).</summary>
public struct SolveStep
{
    public ActKind Act;
    public int Obj;
    public override readonly string ToString() => $"{Act}:{Obj}";
}

/// <summary>풀이 결과.</summary>
public sealed class SolveResult
{
    public bool Found;
    public List<SolveStep> Steps = new();
    public float Cost, Minutes, AirLost, AirBack, Risk;
    public int Expanded;
    public bool BudgetOut;
    /// <summary>기본 절차서에 없는 행동(되돌리기 · 채우기 · 내보내기 · 바로잡기)을 썼다 — 지어낸 방법.</summary>
    public bool Invented;
    public bool FromOutside;
}

/// <summary>
/// ⑧ · ⑩ 판단 엔진 (다) 풀이기 — 그림에서 앞으로, 값이 싼 것부터 찾는다 (결정론: 같은 그림 · 같은 지식 · 같은 막힌 방법이면 같은 계획).
/// 원리마다 풀이 법칙 하나 (아래 Apply) — 모르는 원리는: 그 원리를 쓰는 행동을 떠올리지 못하고(needs) · 그 원리가 막는 것을 모른 채 된다고 여긴다(limits).
/// 값 = 분 + 잃는 공기 몰 × 공기 값 + 다칠 위험 × 1000. 깊이(걸음 수) · 펼침 예산을 넘지 않는다.
/// </summary>
public static class Solver
{
    public const float RiskCost = 1000f, BreatheKPa = 50f, CoolKPa = 30f;
    /// <summary>시험용: 펼친 그림이 어떤 걸음으로 왔나 (null이면 세지 않는다).</summary>
    [ThreadStatic] public static int[]? CountActs;

    private sealed class Node
    {
        public float[] KPa = Array.Empty<float>();
        public bool[] Hole = Array.Empty<bool>();
        public byte[] People = Array.Empty<byte>();
        public bool[] Open = Array.Empty<bool>(), Jam = Array.Empty<bool>(), Leaky = Array.Empty<bool>();
        public int Body, Parent = -1, Depth;
        public bool Suited;
        public int Kits;
        public SolveStep Via;
        public float Cost, Min, Lost, Back, Risk;

        public Node Copy() => new()
        {
            KPa = (float[])KPa.Clone(), Hole = (bool[])Hole.Clone(), People = (byte[])People.Clone(), Open = (bool[])Open.Clone(), Jam = (bool[])Jam.Clone(), Leaky = (bool[])Leaky.Clone(),
            Body = Body, Suited = Suited, Kits = Kits, Cost = Cost, Min = Min, Lost = Lost, Back = Back, Risk = Risk, Depth = Depth,
        };

        public string Key()
        {
            var sb = new System.Text.StringBuilder(KPa.Length * 3 + Open.Length + 4);
            for (int p = 0; p < KPa.Length; p++) { sb.Append((char)(Math.Clamp((int)MathF.Round(KPa[p]), 0, 200) + 32)); sb.Append(Hole[p] ? 'h' : '-'); sb.Append((char)(People[p] + 32)); }
            for (int d = 0; d < Open.Length; d++) sb.Append(Open[d] ? (Jam[d] ? 'J' : 'o') : (Jam[d] ? 'j' : Leaky[d] ? 'l' : 'c'));
            sb.Append((char)(Body + 64)); sb.Append(Suited ? 's' : '-'); sb.Append((char)(Math.Min(Kits, 60) + 48));
            return sb.ToString();
        }
    }

    private sealed class ByCost : IComparer<(float cost, int seq)>
    {
        public static readonly ByCost I = new();
        public int Compare((float cost, int seq) a, (float cost, int seq) b) => a.cost != b.cost ? a.cost.CompareTo(b.cost) : a.seq.CompareTo(b.seq);
    }

    /// <summary>
    /// 풀이: 그림 pic에서 목표(Goal 자리의 구멍이 막힘)에 닿는 가장 싼 걸음 차례.
    /// computer: 주컴퓨터가 푼다 (주컴퓨터 일 + 몸 하나를 부린다) · 아니면 몸 혼자 (사람).
    /// banned: 고르지 않을 걸음 (막힌 방법 — 까닭이 아직 그대로인 것).
    /// </summary>
    public static SolveResult Solve(Picture pic, Know know, bool computer, int maxDepth = 10, int budget = 4000, Func<SolveStep, bool>? banned = null)
    {
        var kt = KnowledgeTable.Load();
        var res = new SolveResult();
        var nodes = new List<Node>();
        var root = new Node
        {
            KPa = (float[])pic.KPa.Clone(), Hole = (bool[])pic.Hole.Clone(), People = (byte[])pic.People.Clone(),
            Open = (bool[])pic.Open.Clone(), Jam = (bool[])pic.Jam.Clone(), Leaky = (bool[])pic.Leaky.Clone(), Body = pic.Body, Kits = pic.Kits,
        };
        Settle(pic, root, know);
        root.Lost = 0; root.Back = 0; root.Risk = 0; root.Cost = 0;   // 지금 형편은 값에 넣지 않는다
        nodes.Add(root);
        var best = new Dictionary<string, float> { [root.Key()] = 0 };
        var pq = new PriorityQueue<int, (float, int)>(ByCost.I);
        int seq = 0;
        var hops = Hops(pic);
        var on = OnWay(pic);   // 몸에서 목표 · 바깥까지 이어지는 자리 (막다른 곁방은 길로 쓰지 않는다 — 봉하기 · 틈 막기 · 내보내기만)
        float H(Node x) => pic.Goal < 0 || !x.Hole[pic.Goal] ? 0 : x.Body < 0 || hops[x.Body] == int.MaxValue ? 1e6f : hops[x.Body] * 1f + (float)kt.A[(int)ActKind.Patch].Minutes;   // 남은 문 수 × 지나기 1분 + 막기 — 아래로 어림 (A*)
        pq.Enqueue(0, (H(root), seq++));
        var acts = Enum.GetValues<ActKind>();
        while (pq.TryDequeue(out int ni, out var pri))
        {
            var n = nodes[ni];
            if (best.TryGetValue(n.Key(), out float bc) && bc < n.Cost - 1e-4f) continue;   // 더 싼 길로 같은 그림에 이미 닿았다
            if (pic.Goal >= 0 && !n.Hole[pic.Goal])
            {
                res.Found = true;
                for (int i = ni; nodes[i].Parent >= 0; i = nodes[i].Parent) res.Steps.Add(nodes[i].Via);
                res.Steps.Reverse();
                res.Cost = n.Cost; res.Minutes = n.Min; res.AirLost = n.Lost; res.AirBack = n.Back; res.Risk = n.Risk;
                res.Invented = res.Steps.Any(s => s.Act is ActKind.Pump or ActKind.Fill or ActKind.Clear or ActKind.Unjam);
                res.FromOutside = res.Steps.Any(s => s.Act == ActKind.Patch) && n.Body == pic.Out && pic.Out >= 0;
                break;
            }
            if (++res.Expanded > budget) { res.BudgetOut = true; break; }
            if (CountActs != null && n.Parent >= 0) CountActs[(int)n.Via.Act]++;
            if (n.Depth >= maxDepth) continue;
            foreach (var a in acts)
            {
                if (!kt.CanThink(a, know)) continue;   // 쓰는 원리를 모르면 떠올리지 못한다
                int count = DoorAct(a) ? pic.D : pic.P;
                for (int o = 0; o < count; o++)
                {
                    var step = new SolveStep { Act = a, Obj = o };
                    if (!Worth(pic, n, step, on, computer, kt)) continue;
                    if (banned != null && banned(step)) continue;
                    var m = n.Copy();
                    if (!Apply(pic, m, step, know, computer, kt)) continue;
                    m.Cost = m.Min + m.Lost * pic.AirValue + m.Risk * RiskCost;
                    m.Depth = n.Depth + 1;
                    string key = m.Key();
                    if (best.TryGetValue(key, out float c) && c <= m.Cost + 1e-4f) continue;
                    best[key] = m.Cost;
                    m.Parent = ni; m.Via = step;
                    nodes.Add(m);
                    float h = H(m);
                    if (h >= 1e6f) continue;   // 몸이 목표에 닿을 길이 그림에 없다
                    pq.Enqueue(nodes.Count - 1, (m.Cost + h, seq++));
                }
            }
        }
        return res;
    }

    /// <summary>
    /// 길에 놓인 자리: 문이 하나뿐인 자리(막다른 곁방)를 거듭 떼어 내고 남은 것 — 몸 · 목표 · 바깥은 늘 남긴다.
    /// 떼어 낸 곁방으로 가는 문은 열거나 지나지 않는다 (길이 아니다) · 그 방의 공기를 다루지 않는다.
    /// </summary>
    private static bool[] OnWay(Picture pic)
    {
        var on = new bool[pic.P];
        Array.Fill(on, true);
        var deg = new int[pic.P];
        for (int k = 0; k < pic.D; k++) { deg[pic.DA[k]]++; deg[pic.DB[k]]++; }
        bool Keep(int p) => p == pic.Body || p == pic.Goal || p == pic.Out;
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int p = 0; p < pic.P; p++)
            {
                if (!on[p] || Keep(p) || deg[p] > 1) continue;
                on[p] = false; changed = true;
                for (int k = 0; k < pic.D; k++)
                {
                    int o = pic.DA[k] == p ? pic.DB[k] : pic.DB[k] == p ? pic.DA[k] : -1;
                    if (o >= 0 && on[o]) deg[o]--;
                }
            }
        }
        return on;
    }

    /// <summary>복사하기 전에 거른다: 해 볼 만한 걸음인가 (안 되는 것이 뻔하거나 길이 아닌 곳).</summary>
    private static bool Worth(Picture pic, Node n, SolveStep s, bool[] on, bool computer, KnowledgeTable kt)
    {
        int o = s.Obj;
        bool door = DoorAct(s.Act);
        bool near = door && n.Body >= 0 && (pic.DA[o] == n.Body || pic.DB[o] == n.Body);
        bool path = door && on[pic.DA[o]] && on[pic.DB[o]];
        bool touch = door && (on[pic.DA[o]] || on[pic.DB[o]]);
        return s.Act switch
        {
            ActKind.Open => !n.Open[o] && path,
            ActKind.Close => n.Open[o] && touch,
            ActKind.Go => n.Open[o] && near && path,
            ActKind.Unjam => n.Jam[o] && near && path,
            ActKind.Tape => false,                                                     // 문 하나씩은 '봉하기'로 묶어 찾는다 (가짓수)
            ActKind.Seal => n.Body == o && PumpWants(pic, n, o, on) && NeedsSeal(pic, n, o),
            ActKind.Pump => computer && on[o] && o != pic.Out && pic.Vent[o],
            ActKind.Fill => computer && on[o] && o != pic.Out && pic.Vent[o] && n.People[o] > 0 && n.KPa[o] < 95,   // 계획 중의 채우기는 사람이 있는 방을 지킬 때만 (막은 뒤의 채우기는 실행이 붙인다)
            ActKind.Clear => computer && on[o] && n.People[o] > 0,
            ActKind.Patch => n.Hole[o] && n.Body >= 0 && (n.Body == o || n.Body == pic.Out),
            ActKind.Suit => o == n.Body && pic.BodyBreathes && !n.Suited && o >= 0 && o != pic.Out && pic.Lockers[o],
            _ => true,
        };
    }

    /// <summary>봉할 것이 있나 (열린 문 · 공기가 있는 쪽으로 난 틈).</summary>
    private static bool NeedsSeal(Picture pic, Node n, int p)
    {
        for (int d = 0; d < pic.D; d++)
        {
            if (pic.DA[d] != p && pic.DB[d] != p) continue;
            int other = pic.DA[d] == p ? pic.DB[d] : pic.DA[d];
            if (n.Open[d] || n.Leaky[d] && other != pic.Out && n.KPa[other] > (float)Network.PumpFloorKPa + 5) return true;
        }
        return false;
    }

    /// <summary>
    /// 묶은 걸음을 푼다: 봉하기 → 그때의 그림대로 문 닫기 · 문틈 막기 하나씩 (실행 · 글).
    /// </summary>
    public static List<(SolveStep step, float minutes)> Expand(Picture pic, IReadOnlyList<SolveStep> steps, Know know, bool computer)
    {
        var kt = KnowledgeTable.Load();
        var n = new Node { KPa = (float[])pic.KPa.Clone(), Hole = (bool[])pic.Hole.Clone(), People = (byte[])pic.People.Clone(), Open = (bool[])pic.Open.Clone(), Jam = (bool[])pic.Jam.Clone(), Leaky = (bool[])pic.Leaky.Clone(), Body = pic.Body, Kits = pic.Kits };
        Settle(pic, n, know);
        var res = new List<(SolveStep, float)>();
        foreach (var s in steps)
        {
            float before = n.Min;
            if (s.Act == ActKind.Seal)
                for (int d = 0; d < pic.D; d++)
                {
                    if (pic.DA[d] != s.Obj && pic.DB[d] != s.Obj) continue;
                    int other = pic.DA[d] == s.Obj ? pic.DB[d] : pic.DA[d];
                    if (n.Open[d]) res.Add((new SolveStep { Act = ActKind.Close, Obj = d }, (float)kt.A[(int)ActKind.Close].Minutes));
                    if (n.Leaky[d] && other != pic.Out && n.KPa[other] > (float)Network.PumpFloorKPa + 5) res.Add((new SolveStep { Act = ActKind.Tape, Obj = d }, (float)kt.A[(int)ActKind.Tape].Minutes));
                }
            Apply(pic, n, s, know, computer, kt);
            if (s.Act != ActKind.Seal) res.Add((s, n.Min - before));   // 그때의 그림으로 셈한 걸린 분 (되돌리기 · 채우기는 그 방 공기만큼)
        }
        return res;
    }

    /// <summary>그 방은 공기를 되돌릴 만한가 (길에 놓였고 · 통풍구 · 사람 없음 · 구멍 없음 · 아직 공기가 있다) — 그 방 문틈을 막는 것이 쓸모 있다.</summary>
    private static bool PumpWants(Picture pic, Node n, int p, bool[] on) =>
        p != pic.Out && on[p] && pic.Vent[p] && n.People[p] == 0 && !(Breathes(pic, n) && n.Body == p) && !n.Hole[p] && n.KPa[p] > (float)Network.PumpFloorKPa + 1;

    /// <summary>자리마다 목표까지 (안에서 — 또는 바깥에서 외판에 닿으면 바깥까지) 지나야 할 문 수의 하한 (문 상태와 상관없이).</summary>
    private static int[] Hops(Picture pic)
    {
        var d = new int[pic.P];
        Array.Fill(d, int.MaxValue);
        var q = new Queue<int>();
        if (pic.Goal >= 0) { d[pic.Goal] = 0; q.Enqueue(pic.Goal); }
        if (pic.Out >= 0 && pic.Goal >= 0 && pic.Hull[pic.Goal]) { d[pic.Out] = 0; q.Enqueue(pic.Out); }   // 바깥에서 막을 수 있으면 바깥도 목표
        while (q.Count > 0)
        {
            int p = q.Dequeue();
            for (int k = 0; k < pic.D; k++)
            {
                int o = pic.DA[k] == p ? pic.DB[k] : pic.DB[k] == p ? pic.DA[k] : -1;
                if (o >= 0 && d[o] == int.MaxValue) { d[o] = d[p] + 1; q.Enqueue(o); }
            }
        }
        return d;
    }

    /// <summary>문에 하는 행동인가 (대상이 문).</summary>
    public static bool DoorAct(ActKind a) => a is ActKind.Open or ActKind.Close or ActKind.Go or ActKind.Unjam or ActKind.Tape;

    /// <summary>계획을 그림 위에서 다시 밟는다 — 모든 걸음이 되고 목표에 닿으면 참 (법칙 시험).</summary>
    public static bool Replay(Picture pic, IReadOnlyList<SolveStep> steps, Know know, bool computer)
    {
        var kt = KnowledgeTable.Load();
        var n = new Node { KPa = (float[])pic.KPa.Clone(), Hole = (bool[])pic.Hole.Clone(), People = (byte[])pic.People.Clone(), Open = (bool[])pic.Open.Clone(), Jam = (bool[])pic.Jam.Clone(), Leaky = (bool[])pic.Leaky.Clone(), Body = pic.Body, Kits = pic.Kits };
        Settle(pic, n, know);
        foreach (var s in steps) if (!kt.CanThink(s.Act, know) || !Apply(pic, n, s, know, computer, kt)) return false;
        return pic.Goal < 0 || !n.Hole[pic.Goal];
    }

    /// <summary>걸음의 글 (지식 표의 글에 문 · 방 이름을 채운다 — 조사는 이름에 맞춰).</summary>
    public static string Text(Picture pic, SolveStep s, int bodyAt = -1)
    {
        string t = KnowledgeTable.Load().A[(int)s.Act].Text;
        if (DoorAct(s.Act))
        {
            string door = pic.DoorName(s.Obj);
            int other = bodyAt == pic.DA[s.Obj] ? pic.DB[s.Obj] : bodyAt == pic.DB[s.Obj] ? pic.DA[s.Obj] : pic.DB[s.Obj];
            t = t.Replace("{문을}", Ko.Eul(door)).Replace("{문}", door).Replace("{너머로}", Ko.Euro(pic.Name[other])).Replace("{너머}", pic.Name[other]);
        }
        else
        {
            string room = pic.Name[s.Obj];
            t = t.Replace("{방을}", Ko.Eul(room)).Replace("{방}", room);
        }
        return t;
    }

    /// <summary>계획 전체의 글 ('→'로 잇는다 · 몸의 자리를 따라가며).</summary>
    public static string Describe(Picture pic, IReadOnlyList<SolveStep> steps)
    {
        int body = pic.Body;
        var parts = new List<string>();
        foreach (var s in steps)
        {
            parts.Add(Text(pic, s, body));
            if (s.Act == ActKind.Go) body = pic.DA[s.Obj] == body ? pic.DB[s.Obj] : pic.DA[s.Obj];
        }
        return string.Join(" → ", parts);
    }

    // ───────────────────────── 풀이 법칙 (원리마다 하나) ─────────────────────────

    /// <summary>
    /// 열린 문으로 이어진 자리들을 한 덩어리로 (열린문): 덩어리에 구멍이나 바깥이 있으면 다 0kPa로 (트인방 — 그 공기는 잃는다) · 아니면 부피로 고르게.
    /// 우주복 없는 사람이 있는 자리가 숨 쉴 기압(50kPa) 아래로 가면 위험 (숨). space[p]: 그 자리가 지금 우주로 트였나.
    /// </summary>
    private static bool[] Settle(Picture pic, Node n, Know know)
    {
        int P = pic.P;
        var root = new int[P];
        for (int p = 0; p < P; p++) root[p] = p;
        int Find(int x) { while (root[x] != x) x = root[x] = root[root[x]]; return x; }
        for (int d = 0; d < pic.D; d++) if (n.Open[d]) { int a = Find(pic.DA[d]), b = Find(pic.DB[d]); if (a != b) root[Math.Max(a, b)] = Math.Min(a, b); }
        var space = new bool[P];
        var groupSpace = new bool[P];
        var sumN = new double[P]; var sumCap = new double[P];
        for (int p = 0; p < P; p++)
        {
            int r = Find(p);
            if (p == pic.Out || n.Hole[p]) groupSpace[r] = true;
            if (p != pic.Out) { sumN[r] += pic.Cap[p] * n.KPa[p] / 101.325; sumCap[r] += pic.Cap[p]; }
        }
        for (int p = 0; p < P; p++)
        {
            int r = Find(p);
            space[p] = groupSpace[r];
            if (p == pic.Out) continue;
            float was = n.KPa[p];
            float now = groupSpace[r] ? 0f : sumCap[r] > 0 ? (float)(sumN[r] / sumCap[r] * 101.325) : was;
            if (groupSpace[r] && was > 0.5f) n.Lost += pic.Cap[p] * was / 101.325f;
            if (now < BreatheKPa && was >= BreatheKPa - 0.5f && know.Has(Principle.Breath))
            {
                n.Risk += n.People[p];
                if (Breathes(pic, n) && n.Body == p) n.Risk += 1;
            }
            if (now < CoolKPa && was >= CoolKPa - 0.5f && pic.Brain[p] && know.Has(Principle.AirCool)) n.Risk += 1.5f;   // 주컴퓨터가 식지 못해 멈춘다
            n.KPa[p] = now;
        }
        return space;
    }

    /// <summary>몸이 숨을 쉬어야 하나 (우주복 없는 사람).</summary>
    private static bool Breathes(Picture pic, Node n) => pic.BodyBreathes && !n.Suited;

    private static bool AllClosed(Picture pic, Node n, int p)
    {
        for (int d = 0; d < pic.D; d++) if ((pic.DA[d] == p || pic.DB[d] == p) && n.Open[d]) return false;
        return true;
    }

    private static bool Apply(Picture pic, Node n, SolveStep s, Know know, bool computer, KnowledgeTable kt)
    {
        var def = kt.A[(int)s.Act];
        bool byComputer = computer && def.Who is "computer" or "any";
        bool byBody = n.Body >= 0 && def.Who is "body" or "any";
        int o = s.Obj;
        bool[] space = s.Act is ActKind.Open or ActKind.Pump or ActKind.Fill ? Settle(pic, n, know) : Array.Empty<bool>();   // 지금 덩어리 (값은 그대로 — 이미 고르다)
        switch (s.Act)
        {
            case ActKind.Open:
            {
                if (n.Open[o]) return false;
                bool near = byBody && (n.Body == pic.DA[o] || n.Body == pic.DB[o]);
                bool remote = byComputer && pic.Powered[o];
                if (!near && !remote) return false;
                if (n.Jam[o] && know.Has(Principle.Jammed)) return false;                 // 끼인 문은 안 열린다 (상식)
                float ka = pic.DA[o] == pic.Out ? 0 : n.KPa[pic.DA[o]], kb = pic.DB[o] == pic.Out ? 0 : n.KPa[pic.DB[o]];
                float dp = MathF.Abs(ka - kb);
                float minutes = (float)def.Minutes;
                if (know.Has(Principle.DoorLimit) && dp > pic.Limit[o])                  // 기압 차가 큰 문은 안 열린다
                {
                    if (!know.Has(Principle.Valve) || !pic.Powered[o]) return false;
                    int hi = ka > kb ? pic.DA[o] : pic.DB[o], lo = ka > kb ? pic.DB[o] : pic.DA[o];
                    if (pic.Ext[o])
                    {
                        // 바깥문 밸브: 방 쪽이 닫혀 있고 사람이 없을 때만 그 방 공기를 우주로 뺀다 (맞물림)
                        if (!AllClosedBut(pic, n, hi, o) || n.People[hi] > 0 || Breathes(pic, n) && n.Body == hi) return false;
                        n.Lost += pic.Cap[hi] * n.KPa[hi] / 101.325f;
                        n.KPa[hi] = 0;
                    }
                    else
                    {
                        if (space[lo] || space[hi]) return false;                          // 너머가 우주로 트였으면 밸브가 열리지 않는다
                        float m = (pic.Cap[hi] * n.KPa[hi] + pic.Cap[lo] * n.KPa[lo]) / Math.Max(1e-3f, pic.Cap[hi] + pic.Cap[lo]);
                        if (m < BreatheKPa && know.Has(Principle.Breath)) n.Risk += n.People[hi] + n.People[lo];
                        n.KPa[hi] = n.KPa[lo] = m;
                    }
                    minutes += 3 + dp / 15;
                }
                n.Open[o] = true;
                if (pic.Leaky.Length > o && know.Has(Principle.DoorGap) && !pic.Ext[o]) n.Leaky[o] = PicLeaky(pic, o);   // 열면 막은 틈이 떨어진다
                n.Min += minutes;
                Settle(pic, n, know);
                return true;
            }
            case ActKind.Close:
            {
                if (!n.Open[o] || n.Jam[o] && know.Has(Principle.Jammed)) return false;
                bool near = byBody && (n.Body == pic.DA[o] || n.Body == pic.DB[o]);
                if (!near && !(byComputer && pic.Powered[o])) return false;
                n.Open[o] = false;
                n.Min += (float)def.Minutes;
                return true;
            }
            case ActKind.Go:
            {
                if (!byBody || !n.Open[o] || n.Body != pic.DA[o] && n.Body != pic.DB[o]) return false;
                int to = n.Body == pic.DA[o] ? pic.DB[o] : pic.DA[o];
                if (Breathes(pic, n) && know.Has(Principle.Breath) && (to == pic.Out || n.KPa[to] < BreatheKPa)) n.Risk += 1;
                n.Body = to;
                n.Min += (float)def.Minutes;
                return true;
            }
            case ActKind.Unjam:
            {
                if (!byBody || !n.Jam[o] || n.Body != pic.DA[o] && n.Body != pic.DB[o]) return false;
                n.Jam[o] = false;
                n.Min += (float)def.Minutes;
                return true;
            }
            case ActKind.Pump:
            {
                // 되돌리기: 닫힌 방 · 사람 없음 · 통풍구 · 전기 → 20kPa까지 예비 탱크로 (주컴퓨터는 사람이 있는 방의 공기를 빼지 않는다)
                if (!byComputer || o == pic.Out || !pic.Vent[o] || space[o] || !AllClosed(pic, n, o) || n.People[o] > 0 || Breathes(pic, n) && n.Body == o) return false;
                float floor = (float)Network.PumpFloorKPa;
                // 문틈: 닫혀도 새는 보통 문 너머에 공기가 있으면 빼는 만큼 그리로 들어온다 — 먼저 틈을 막아야 한다
                if (know.Has(Principle.DoorGap))
                    for (int d = 0; d < pic.D; d++)
                    {
                        if (!n.Leaky[d] || pic.DA[d] != o && pic.DB[d] != o) continue;
                        int other = pic.DA[d] == o ? pic.DB[d] : pic.DA[d];
                        if (other != pic.Out && n.KPa[other] > floor + 5) return false;
                    }
                if (n.KPa[o] <= floor + 1) return false;
                float moles = pic.Cap[o] * (n.KPa[o] - floor) / 101.325f;
                n.Back += moles;
                if (pic.Brain[o] && n.KPa[o] >= CoolKPa && know.Has(Principle.AirCool)) n.Risk += 1.5f;
                n.KPa[o] = floor;
                n.Min += moles / (float)(Math.Max(1, pic.Cells[o]) * Network.PumpMolPerCellSec) / 60f;
                return true;
            }
            case ActKind.Fill:
            {
                if (!byComputer || o == pic.Out || !pic.Vent[o] || space[o] || !AllClosed(pic, n, o) || n.KPa[o] >= 90) return false;
                float moles = pic.Cap[o] * (101.3f - n.KPa[o]) / 101.325f;
                n.KPa[o] = 101.3f;
                n.Min += moles / (Math.Max(1, pic.Cells[o]) * 0.06f) / 60f;
                return true;
            }
            case ActKind.Clear:
            {
                if (!byComputer || o == pic.Out || n.People[o] == 0) return false;
                // 숨 쉴 만한 옆 자리로 (열린 문이든 닫힌 문이든 — 사람은 문을 열고 나간다 · 기압 차가 크면 못 간다)
                int dest = -1;
                for (int d = 0; d < pic.D && dest < 0; d++)
                {
                    int other = pic.DA[d] == o ? pic.DB[d] : pic.DB[d] == o ? pic.DA[d] : -1;
                    if (other < 0 || other == pic.Out || n.KPa[other] < 80 || n.Jam[d]) continue;
                    if (!n.Open[d] && MathF.Abs(n.KPa[other] - n.KPa[o]) > pic.Limit[d]) continue;
                    dest = other;
                }
                if (dest < 0) return false;
                n.People[dest] += n.People[o];
                n.People[o] = 0;
                n.Min += (float)def.Minutes;
                return true;
            }
            case ActKind.Tape:
            {
                if (!byBody || n.Open[o] || !n.Leaky[o] || n.Body != pic.DA[o] && n.Body != pic.DB[o] || n.Kits <= 0) return false;
                n.Kits--; n.Leaky[o] = false;
                n.Min += (float)def.Minutes;
                return true;
            }
            case ActKind.Suit:
            {
                if (!byBody || n.Suited || !pic.BodyBreathes || n.Body < 0 || n.Body == pic.Out || !pic.Lockers[n.Body]) return false;
                n.Suited = true;
                n.Min += (float)def.Minutes;
                return true;
            }
            case ActKind.Seal:
            {
                // 봉하기: 그 방의 열린 문을 닫고 · 공기가 있는 쪽으로 난 보통 문 틈을 막는다 (몸이 그 방 안에)
                if (!byBody || n.Body != o || !NeedsSeal(pic, n, o)) return false;
                var close = kt.A[(int)ActKind.Close]; var tape = kt.A[(int)ActKind.Tape];
                for (int d = 0; d < pic.D; d++)
                {
                    if (pic.DA[d] != o && pic.DB[d] != o) continue;
                    if (n.Open[d]) { if (n.Jam[d]) return false; n.Open[d] = false; n.Min += (float)close.Minutes; }
                    int other = pic.DA[d] == o ? pic.DB[d] : pic.DA[d];
                    if (n.Leaky[d] && other != pic.Out && n.KPa[other] > (float)Network.PumpFloorKPa + 5)
                    {
                        if (n.Kits <= 0) return false;   // 봉합 꾸러미가 모자란다
                        n.Kits--; n.Leaky[d] = false; n.Min += (float)tape.Minutes;
                    }
                }
                return true;
            }
            case ActKind.Patch:
            {
                if (!byBody || o == pic.Out || !n.Hole[o]) return false;
                bool inside = n.Body == o, outside = n.Body == pic.Out && pic.Hull[o];
                if (!inside && !outside) return false;
                if (n.Kits <= 0) return false;   // 봉합 꾸러미가 있어야 막는다 (사람 몸 — 틈을 막느라 다 썼으면 못 막는다)
                n.Kits--;
                if (Breathes(pic, n) && know.Has(Principle.Breath) && (outside || n.KPa[o] < BreatheKPa)) n.Risk += 1;
                n.Hole[o] = false;
                n.Min += (float)def.Minutes;
                Settle(pic, n, know);
                return true;
            }
        }
        return false;
    }

    /// <summary>그 문이 원래 새는 보통 문인가 (그림을 그릴 때 틈을 막지 않은 보통 문으로 적은 것 — 열면 막은 것이 떨어져 다시 샌다).</summary>
    private static bool PicLeaky(Picture pic, int d) => pic.Normal.Length > d && pic.Normal[d];

    private static bool AllClosedBut(Picture pic, Node n, int p, int except)
    {
        for (int d = 0; d < pic.D; d++) if (d != except && (pic.DA[d] == p || pic.DB[d] == p) && n.Open[d]) return false;
        return true;
    }
}
