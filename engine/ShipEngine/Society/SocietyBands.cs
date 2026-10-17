using System.Runtime.InteropServices;

namespace ShipEngine;

/// <summary>⑨ 무리 — 서로 정이 0.4 넘는 사이로 이어진 셋 이상 (한 시간마다 정의 그물에서 저절로).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Band
{
    public int Id;
    public int Center;     // 이름이 되는 사람 (무리 안에서 정을 가장 많이 주고받는 이 — "○○네")
    public int Leader;     // 무리 안에서 가장 존경받는 이
    public int Size;
    public long Formed;
}

public sealed partial class Society
{
    public const float BandAt = 0.4f, RiftAt = 0.3f;
    public const int BandMax = 8;
    /// <summary>무리 (한 시간마다 다시 찾는다) · 사람마다 무리 번호 (−1 없음) · 존경 (남들이 그를 믿고 · 빚지고 · 아끼는 정도 + 솜씨).</summary>
    public List<Band> Bands = new();
    public int[] BandOf = Array.Empty<int>();
    public float[] Respect = Array.Empty<float>();
    /// <summary>배 전체에서 가장 믿고 따르는 사람 (−1 없음).</summary>
    public int Head = -1;
    public long BandsFormed, BandsBroken, Rifts;
    private int _nextBand = 1;
    private readonly List<long> _rift = new();   // 사이가 벌어진 두 무리 (작은 번호 × 2^32 + 큰 번호)

    public int BandIdOf(int a) => a >= 0 && a < BandOf.Length ? BandOf[a] : -1;
    public Band? BandById(int id) { foreach (var b in Bands) if (b.Id == id) return b; return null; }
    public bool SameBand(int a, int b) => BandIdOf(a) >= 0 && BandIdOf(a) == BandIdOf(b);
    /// <summary>a가 속한 무리의 지도자 (−1 없음).</summary>
    public int LeaderOf(int a) { int g = BandIdOf(a); return g >= 0 && BandById(g) is Band b ? b.Leader : -1; }
    /// <summary>s가 a를 이끄는 사람인가 (a의 무리 지도자 · 배 전체에서 따르는 이).</summary>
    public bool Leads(int s, int a) => s >= 0 && s != a && (LeaderOf(a) == s || Head == s);
    public string BandName(int id) => BandById(id) is Band b ? $"{_mind!.NameOf(b.Center)}네" : "";

    /// <summary>들은 말을 얼마나 믿나 — 말한 이를 믿는 만큼 (보통 1) · 나를 이끄는 이의 말이면 더.</summary>
    public float Heed(int listener, int speaker)
    {
        // (로봇 · 드론의 믿음 값은 늘 0.5 그대로 → 1 · 무리에도 들지 않는다 — 복제 세계에서도 같게 마음 엔진을 따로 묻지 않는다)
        if (speaker < 0 || speaker >= N || listener < 0 || listener >= N) return 1;
        float h = 0.6f + 0.8f * TrustIn(listener, speaker);
        if (LeaderOf(listener) == speaker) h += 0.3f;
        else if (Head == speaker) h += 0.15f;
        return Math.Clamp(h, 0.2f, 1.4f);
    }

    /// <summary>members 사이에서 i가 받는 존경: 남들이 i를 믿는 정도 + 진 빚 + 정 − 원한 (평균) + 솜씨.</summary>
    private float RespectAmong(int i, List<int> members)
    {
        float s = 0; int n = 0;
        foreach (int j in members)
        {
            if (j == i) continue;
            s += TrustIn(j, i) + 0.5f * Math.Min(1, DebtOf(j, i)) + 0.3f * Math.Max(0, BondOf(j, i)) - 0.5f * GrudgeOf(j, i);
            n++;
        }
        ref var c = ref C(i);
        float skill = Math.Max(Math.Max(Math.Max(c.SkRepair, c.SkMedic), Math.Max(c.SkFire, c.SkSeal)), Math.Max(Math.Max(c.SkCook, c.SkPilot), Math.Max(c.SkFarm, c.SkScience)));
        return (n > 0 ? s / n : 0) + 0.15f * skill;
    }

    /// <summary>시험 · 화면: 한 시간을 기다리지 않고 지금 무리를 다시 찾는다.</summary>
    public void Regroup(Sim sim) { _mind ??= sim.Find<Mind>(); _life ??= sim.Find<Life>(); if (_mind != null && _life != null) FormBands(sim); }

    /// <summary>한 시간마다: 정의 그물에서 무리를 찾고 (앞 무리와 반 넘게 겹치면 같은 무리) · 지도자를 고르고 · 무리 사이 앙금을 잰다.</summary>
    private void FormBands(Sim sim)
    {
        var mind = _mind!;
        if (BandOf.Length != N) { var nb = Enumerable.Repeat(-1, N).ToArray(); for (int i = 0; i < Math.Min(N, BandOf.Length); i++) nb[i] = BandOf[i]; BandOf = nb; }
        if (Respect.Length != N) Respect = new float[N];
        var people = new List<int>();
        for (int i = 0; i < N; i++) if (Alive(i)) people.Add(i);
        foreach (int i in people) Respect[i] = RespectAmong(i, people);
        var groups = Split(people, BandAt);
        var old = Bands; var oldOf = BandOf;
        var next = new List<Band>(); var nextOf = Enumerable.Repeat(-1, N).ToArray();
        foreach (var g in groups)
        {
            if (g.Count < 3) continue;
            g.Sort();
            int center = g[0], leader = g[0]; float bc = float.MinValue, br = float.MinValue;
            foreach (int i in g)
            {
                float sum = 0; foreach (int j in g) if (j != i) sum += BondOf(i, j) + BondOf(j, i);
                if (sum > bc) { bc = sum; center = i; }
                float r = RespectAmong(i, g);
                if (r > br) { br = r; leader = i; }
            }
            // 앞 무리 이어받기: 가장 많이 겹치는 앞 무리와 반 넘게 겹치면
            int match = -1, most = 0;
            foreach (var ob in old)
            {
                int ov = 0; foreach (int i in g) if (i < oldOf.Length && oldOf[i] == ob.Id) ov++;
                if (ov > most && ov * 2 >= Math.Min(ob.Size, g.Count) && !next.Any(x => x.Id == ob.Id)) { most = ov; match = old.IndexOf(ob); }
            }
            var band = match >= 0 ? old[match] : new Band { Id = _nextBand++, Formed = sim.Tick };
            int prevLeader = match >= 0 ? band.Leader : -1;
            if (prevLeader >= 0 && g.Contains(prevLeader) && RespectAmong(prevLeader, g) > br - 0.05f) leader = prevLeader; // 지도자는 쉽게 바뀌지 않는다
            band.Center = match >= 0 && g.Contains(band.Center) ? band.Center : center; // 이름은 쉽게 바뀌지 않는다
            band.Leader = leader; band.Size = g.Count;
            next.Add(band);
            foreach (int i in g) nextOf[i] = band.Id;
            string name = $"{mind.NameOf(band.Center)}네";
            if (match < 0)
            {
                BandsFormed++;
                string who = string.Join(" · ", g.Where(i => i != band.Center).Take(4).Select(mind.NameOf));
                int c = sim.Causes.Add(CauseKind.Person, $"{name}가 생겼다 — {Ko.Gwa(mind.NameOf(band.Center))} {who}{(g.Count > 5 ? " 들" : "")} · {Ko.I(mind.NameOf(leader))} 이끈다", sim.Tick, C(band.Center).Cell, 0, 0, band.Center);
                sim.Facts.Emit(sim.Tick, FactKind.Relationship, C(band.Center).Cell, 2, c, band.Center, leader);
            }
            else if (prevLeader != leader)
            {
                int c = sim.Causes.Add(CauseKind.Person, $"{name}는 이제 {Ko.Eul(mind.NameOf(leader))} 따른다{(prevLeader >= 0 && Alive(prevLeader) ? $" — {mind.NameOf(prevLeader)}보다 더 믿는다" : "")}", sim.Tick, C(leader).Cell, 0, 0, leader);
                sim.Facts.Emit(sim.Tick, FactKind.Relationship, C(leader).Cell, 3, c, leader, prevLeader);
            }
        }
        foreach (var ob in old)
            if (!next.Any(x => x.Id == ob.Id))
            {
                BandsBroken++;
                int who = ob.Center >= 0 && ob.Center < N ? ob.Center : 0;
                int c = sim.Causes.Add(CauseKind.Person, $"{_mind!.NameOf(who)}네가 흩어졌다", sim.Tick, C(who).Cell, 0, 0, who);
                sim.Facts.Emit(sim.Tick, FactKind.Relationship, C(who).Cell, -2, c, who, -1);
                _rift.RemoveAll(k => (int)(k >> 32) == ob.Id || (int)(k & 0xffffffff) == ob.Id);
            }
        Bands = next; BandOf = nextOf;
        // 배 전체에서 따르는 이: 존경이 가장 높고 · 모두의 평균보다 0.12 넘게 높은 사람 (함께 겪은 일로 얻는다) · 지금 따르는 이를 0.05 넘게 앞서야 바뀐다
        float mean = people.Count > 0 ? people.Average(i => Respect[i]) : 0;
        int head = -1; float hr = mean + 0.12f;
        foreach (int i in people) if (Respect[i] > hr) { hr = Respect[i]; head = i; }
        if (Head >= 0 && Alive(Head) && head != Head && Respect[Head] > mean + 0.12f && Respect[Head] > hr - 0.05f) head = Head;
        if (head != Head && head >= 0)
        {
            int c = sim.Causes.Add(CauseKind.Person, $"배 사람들이 {Ko.Eul(mind.NameOf(head))} 가장 믿고 따른다", sim.Tick, C(head).Cell, 0, 0, head);
            sim.Facts.Emit(sim.Tick, FactKind.Relationship, C(head).Cell, 3, c, head, Head);
        }
        Head = head;
        Rift(sim);
    }

    /// <summary>가까운 사이: 서로 정 (작은 쪽)이 at을 넘는다 — 이미 같은 무리면 0.1 아래까지 버틴다 (생겼다 흩어졌다 하지 않게).</summary>
    private bool Tie(int i, int j, float at)
    {
        float b = Math.Min(BondOf(i, j), BondOf(j, i));
        return b >= at || b >= at - 0.1f && BandIdOf(i) >= 0 && BandIdOf(i) == BandIdOf(j);
    }

    /// <summary>pool을 무리로 나눈다: 가까운 사이로 이어진 덩어리 → 무리 안에 가까운 사이가 둘 못 되는 이는 빠진다 (사슬이 아니라 얽힌 무리) → 여덟이 넘으면 더 가까운 사이로 다시 나눈다.</summary>
    private List<List<int>> Split(List<int> pool, float at)
    {
        var res = new List<List<int>>();
        var left = new HashSet<int>(pool);
        // 얽힘: 무리 안에 가까운 사이가 둘 못 되는 이를 되풀이해 뺀다
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (int i in left.OrderBy(v => v).ToList())
            {
                int ties = 0;
                foreach (int j in left) if (j != i && Tie(i, j, at) && ++ties >= 2) break;
                if (ties < 2) { left.Remove(i); changed = true; }
            }
        }
        var seen = new HashSet<int>();
        foreach (int s in left.OrderBy(v => v))
        {
            if (!seen.Add(s)) continue;
            var g = new List<int> { s };
            for (int q = 0; q < g.Count; q++)
                foreach (int j in left.OrderBy(v => v))
                    if (!seen.Contains(j) && Tie(g[q], j, at)) { seen.Add(j); g.Add(j); }
            if (g.Count > BandMax && at < 0.9f) res.AddRange(Split(g, at + 0.1f));
            else if (g.Count >= 3) res.Add(g);
        }
        return res;
    }

    /// <summary>두 무리 사이의 앙금: 서로에 대한 원한의 평균 — 0.3을 넘으면 '사이가 벌어졌다' (0.15 아래로 내려가면 풀린다).</summary>
    public float RiftOf(int g1, int g2)
    {
        float s = 0; int n = 0;
        for (int i = 0; i < N; i++)
        {
            if (BandIdOf(i) != g1) continue;
            for (int j = 0; j < N; j++) if (BandIdOf(j) == g2) { s += GrudgeOf(i, j) + GrudgeOf(j, i); n += 2; }
        }
        return n > 0 ? s / n : 0;
    }

    private void Rift(Sim sim)
    {
        for (int x = 0; x < Bands.Count; x++)
            for (int y = x + 1; y < Bands.Count; y++)
            {
                int g1 = Math.Min(Bands[x].Id, Bands[y].Id), g2 = Math.Max(Bands[x].Id, Bands[y].Id);
                long key = ((long)g1 << 32) | (uint)g2;
                float r = RiftOf(g1, g2);
                bool had = _rift.Contains(key);
                if (!had && r > RiftAt)
                {
                    _rift.Add(key); Rifts++;
                    int c = sim.Causes.Add(CauseKind.Person, $"{BandName(g1)}와 {BandName(g2)} 사이가 벌어졌다 — 서로 탓하고 미워한다", sim.Tick, C(Bands[x].Center).Cell, 0, 0, Bands[x].Center);
                    sim.Facts.Emit(sim.Tick, FactKind.Relationship, C(Bands[x].Center).Cell, -3, c, Bands[x].Center, Bands[y].Center);
                }
                else if (had && r < 0.15f) _rift.Remove(key);
            }
    }

    /// <summary>두 사람의 무리 사이가 벌어졌나.</summary>
    public bool Rival(int a, int b)
    {
        int g1 = BandIdOf(a), g2 = BandIdOf(b);
        if (g1 < 0 || g2 < 0 || g1 == g2) return false;
        return _rift.Contains(((long)Math.Min(g1, g2) << 32) | (uint)Math.Max(g1, g2));
    }

    /// <summary>방에 x · y 말고 깨어 있는 '이끄는 이'가 있나 (x를 이끄는 사람).</summary>
    private bool LeaderWatching(int x, int y)
    {
        int room = _mind!.RoomOf(C(x).Cell);
        foreach (int l in new[] { LeaderOf(x), Head })
            if (l >= 0 && l != x && l != y && Awake(l) && _mind.RoomOf(C(l).Cell) == room) return true;
        return false;
    }

    private void SaveBands(StateWriter w)
    {
        w.List(Bands); w.Arr(BandOf); w.Arr(Respect); w.I32(Head); w.I32(_nextBand);
        w.I64(BandsFormed); w.I64(BandsBroken); w.I64(Rifts);
        w.I32(_rift.Count); foreach (long k in _rift) w.I64(k);
    }

    private void LoadBands(StateReader r)
    {
        Bands = r.List<Band>(); BandOf = r.Arr<int>(); Respect = r.Arr<float>(); Head = r.I32(); _nextBand = r.I32();
        BandsFormed = r.I64(); BandsBroken = r.I64(); Rifts = r.I64();
        _rift.Clear(); int n = r.I32(); for (int i = 0; i < n; i++) _rift.Add(r.I64());
    }
}
