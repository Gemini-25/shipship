namespace ShipEngine;

/// <summary>매뉴얼 한 항목 — '이럴 때는 이렇게' (방법은 ⑩이 번호로 안다 · 회의는 넣고 뺀다).</summary>
public sealed class ManualEntry
{
    public int Key;            // 방법 번호 (⑩ 주컴퓨터가 매긴다)
    public string Name = "";   // 그 방법의 생각 사슬 (회의에서 말한 것)
    public long Adopted;       // 넣은 때 (부결이면 표결한 때)
    public byte Yes, Of;       // 찬성 · 모인 이
    public int Wins, Fails;    // 넣은 뒤(또는 부결 뒤) 통한 번 · 매뉴얼대로 했다가 틀어진 번
    public byte Removed;       // 0 매뉴얼에 있다 · 1 뺐다 · 2 부결
    public int Proposer = -1;  // 지은 이 (−1 주컴퓨터)
    public int Cause;
}

/// <summary>
/// ⑨ 회의가 매뉴얼을 고친다 (판단 엔진 (바) · docs/ENGINE.md '(바) 설계'). 매뉴얼은 배의 것 — 회의가 넣고 뺀다.
/// ⑩ 주컴퓨터는 사실로 알린다 (지어낸 방법이 통했다 `MethodWorked` · 매뉴얼대로 했다가 틀어졌다 `MethodFailed`) · 매뉴얼을 읽기만 한다 (층 약속).
/// 통한 방법은 '매뉴얼에 넣자' 안건 · 두 번 넘게 · 통한 것보다 많이 틀어진 매뉴얼 방법은 '빼자' · 부결된 방법은 두 번 더 통하면 다시 올린다.
/// </summary>
public sealed partial class Society
{
    public readonly List<ManualEntry> Manual = new();
    public long ManualVotes, ManualAdded, ManualRemoved, ManualRejected;
    // 안건 줄: 방법 번호 · 넣자(1)/빼자(0) · 지은 이 · 잃은 공기 · 원인
    private readonly List<(int key, byte add, int proposer, float lost, int cause)> _manualAgenda = new();
    private (int key, byte add, int proposer, float lost, int cause) _agendaNow;

    /// <summary>매뉴얼에 있는 방법인가 (⑩이 읽는다).</summary>
    public bool InManual(int key) => Manual.Any(e => e.Key == key && e.Removed == 0);

    /// <summary>사실을 읽어 안건을 세운다 (CouncilStep이 사실을 훑을 때).</summary>
    private void ManualFact(Sim sim, in Fact f)
    {
        int key = f.Other;
        if (key == 0) return;
        var e = Manual.FirstOrDefault(x => x.Key == key);
        if (f.Kind == FactKind.MethodWorked)
        {
            if (e != null) e.Wins++;
            bool queued = _manualAgenda.Any(a => a.key == key) || Meeting.Kind == CouncilKind.Manual && _agendaNow.key == key;
            // 아직 없는 방법 · 부결된 방법은 그 뒤 두 번 더 통하면 다시
            if (!queued && (e == null || e.Removed == 2 && e.Wins >= 2 || e.Removed == 1 && e.Wins >= 3))
                _manualAgenda.Add((key, 1, f.Actor, f.Size, f.Cause));
        }
        else if (f.Kind == FactKind.MethodFailed && e != null && e.Removed == 0)
        {
            e.Fails++;
            bool queued = _manualAgenda.Any(a => a.key == key && a.add == 0);
            if (!queued && e.Fails >= 2 && e.Fails > e.Wins) _manualAgenda.Add((key, 0, f.Actor, 0, f.Cause));
        }
    }

    private bool TryManual(Sim sim)
    {
        if (_manualAgenda.Count == 0) return false;
        var a = _manualAgenda[0];
        var people = People();
        int caller = a.proposer >= 0 && a.proposer < N && Alive(a.proposer) ? a.proposer
            : Rep >= 0 && Alive(Rep) ? Rep : people.OrderByDescending(i => Trust(i)).ThenBy(i => i).First();
        string name = MethodName(sim, a.key, a.cause);
        string text = a.add == 1
            ? $"{Ko.I(_mind!.NameOf(caller))} 회의를 열었다 — 지난 사고에 통한 방법을 매뉴얼에 넣자: {name}"
            : $"{Ko.I(_mind!.NameOf(caller))} 회의를 열었다 — 매뉴얼의 방법이 두 번이나 틀어졌다 · 빼자: {name}";
        if (!Open(sim, CouncilKind.Manual, -1, caller, -1, text, a.cause)) return false;
        _manualAgenda.RemoveAt(0);
        _agendaNow = a;
        return true;
    }

    /// <summary>방법의 이름: 그 방법이 통했다는 원인 마디의 말 ('… — 이름') · 매뉴얼에 있으면 그 이름.</summary>
    private string MethodName(Sim sim, int key, int cause)
    {
        var e = Manual.FirstOrDefault(x => x.Key == key);
        if (e != null && e.Name.Length > 0) return e.Name;
        string t = cause > 0 ? sim.Causes.Text(cause) : "";
        int k = t.IndexOf(" — ", StringComparison.Ordinal);
        return k >= 0 ? t[(k + 3)..] : t;
    }

    /// <summary>안건에 대한 생각: 넣자 — 지은 이를 믿는 정도 · 성실 · 그 분야 앎 · 잃은 공기 / 빼자 — 틀어진 몫 · 성실 · 지은 이를 믿는 정도.</summary>
    private float ManualOpinion(int a)
    {
        var g = _agendaNow;
        ref var c = ref C(a);
        if (g.add == 1 && a == g.proposer) return 0.8f;
        float trust = g.proposer < 0 ? Trust(a) : TrustIn(a, g.proposer);
        if (g.add == 1)
            return 0.8f * (trust - 0.45f) + 0.3f * (c.Consc - 0.5f) + 0.4f * (c.KnPressure - 0.3f) - Math.Min(0.3f, g.lost / 3000f);
        var e = Manual.FirstOrDefault(x => x.Key == g.key);
        float failShare = e == null ? 0 : e.Fails / (float)Math.Max(1, e.Fails + e.Wins);
        return 0.5f * failShare + 0.3f * (c.Consc - 0.5f) - 0.3f * (trust - 0.5f);
    }

    private string ManualLine(float op) => _agendaNow.add == 1
        ? op > 0.15f ? "통했던 방법이다 — 매뉴얼에 넣자" : op > 0 ? "조건을 붙여 매뉴얼에 넣자" : _agendaNow.proposer < 0 ? "주컴퓨터가 지어낸 것을 어떻게 믿나 — 그때그때 정하자" : "한 번 통했다고 매뉴얼에 넣기는 이르다"
        : op > 0 ? "두 번이나 틀어졌다 — 빼자" : "운이 나빴을 뿐이다 — 그대로 두자";

    private void ManualDecide(Sim sim, in Council m)
    {
        var g = _agendaNow;
        int yes = 0;
        for (int k = 0; k < _attend.Count; k++) if (_opinion[k] > 0) yes++;
        bool pass = yes * 2 > _attend.Count;
        string name = MethodName(sim, g.key, g.cause);
        var e = Manual.FirstOrDefault(x => x.Key == g.key);
        ManualVotes++;
        int size;
        string what;
        if (g.add == 1)
        {
            if (e == null) { e = new ManualEntry { Key = g.key, Name = name, Proposer = g.proposer }; Manual.Add(e); }
            e.Adopted = sim.Tick; e.Yes = (byte)yes; e.Of = (byte)_attend.Count; e.Wins = 0; e.Fails = 0;
            e.Removed = (byte)(pass ? 0 : 2);
            if (pass) ManualAdded++; else ManualRejected++;
            size = pass ? 1 : 0;
            what = pass ? $"회의가 '{name}'을 매뉴얼에 넣었다 — {_attend.Count}명 중 {yes}명 찬성" : $"회의가 '{name}'을 매뉴얼에 넣지 않기로 했다 — {_attend.Count}명 중 {yes}명만 찬성";
        }
        else
        {
            if (pass && e != null) { e.Removed = 1; e.Wins = 0; e.Fails = 0; ManualRemoved++; }
            size = pass ? -1 : 0;
            what = pass ? $"회의가 '{name}'을 매뉴얼에서 뺐다 — {_attend.Count}명 중 {yes}명 찬성" : $"회의가 '{name}'을 매뉴얼에 두기로 했다 — {_attend.Count}명 중 {yes}명만 빼자고 했다";
        }
        int c = sim.Causes.Add(CauseKind.Society, what, sim.Tick, m.Cell, m.Cause, 0, m.Caller);
        if (e != null) e.Cause = c;
        sim.Facts.Emit(sim.Tick, FactKind.ManualChanged, m.Cell, size, c, m.Caller, g.key);
        _agendaNow = default;
    }

    private void SaveManual(StateWriter w)
    {
        w.I32(Manual.Count);
        foreach (var e in Manual) { w.I32(e.Key); w.Str(e.Name); w.I64(e.Adopted); w.Byte(e.Yes); w.Byte(e.Of); w.I32(e.Wins); w.I32(e.Fails); w.Byte(e.Removed); w.I32(e.Proposer); w.I32(e.Cause); }
        w.I32(_manualAgenda.Count);
        foreach (var a in _manualAgenda) { w.I32(a.key); w.Byte(a.add); w.I32(a.proposer); w.F32(a.lost); w.I32(a.cause); }
        w.I32(_agendaNow.key); w.Byte(_agendaNow.add); w.I32(_agendaNow.proposer); w.F32(_agendaNow.lost); w.I32(_agendaNow.cause);
        w.I64(ManualVotes); w.I64(ManualAdded); w.I64(ManualRemoved); w.I64(ManualRejected);
    }

    private void LoadManual(StateReader r)
    {
        Manual.Clear();
        int n = r.I32();
        for (int i = 0; i < n; i++)
            Manual.Add(new ManualEntry { Key = r.I32(), Name = r.Str() ?? "", Adopted = r.I64(), Yes = r.Byte(), Of = r.Byte(), Wins = r.I32(), Fails = r.I32(), Removed = r.Byte(), Proposer = r.I32(), Cause = r.I32() });
        _manualAgenda.Clear();
        n = r.I32();
        for (int i = 0; i < n; i++) _manualAgenda.Add((r.I32(), r.Byte(), r.I32(), r.F32(), r.I32()));
        _agendaNow = (r.I32(), r.Byte(), r.I32(), r.F32(), r.I32());
        ManualVotes = r.I64(); ManualAdded = r.I64(); ManualRemoved = r.I64(); ManualRejected = r.I64();
    }
}
