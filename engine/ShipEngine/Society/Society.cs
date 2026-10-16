namespace ShipEngine;

/// <summary>
/// ⑨ 사회 (docs/ENGINE.md §4 ⑨) — 사람 사이의 정(情)과 주컴퓨터에 대한 믿음. 줄거리를 짜지 않는다: 함께 보낸 시간 · 구해 준 일 · 죽음 · 갇힘이
/// 정 · 슬픔 · 믿음을 바꾸고, 그것이 다시 마음(⑧)의 고르기(친구를 먼저 구한다 · 슬퍼서 일손을 놓는다 · 컴퓨터 경보를 덜 믿는다)를 바꾼다.
/// </summary>
public sealed class Society : IEngine
{
    public string Name => "사회";
    public Phase Phase => Phase.Decide;
    public int Every => 1;
    public int Offset => 0;

    public const int TogetherEvery = 25;          // 1분마다 함께 있음을 센다
    public const float FriendAt = 0.5f, CloseAt = 0.8f;

    private readonly Layout L;
    private Mind? _mind;
    private Life? _life;

    /// <summary>정 [나 × 너] (−1 ~ 1) — 나에게서 너로 (구해 준 사람에게 진 빚은 한쪽만 크다).</summary>
    public float[] Bond = Array.Empty<float>();
    /// <summary>사람마다 주컴퓨터를 믿는 정도 (0 ~ 1, 처음 0.8).</summary>
    public float[] TrustComputer = Array.Empty<float>();
    /// <summary>사람마다 슬픔 (0 ~ 1) — 하루 반감.</summary>
    public float[] Grief = Array.Empty<float>();
    public int N;                                  // 배열이 다루는 에이전트 수
    public long Friendships, Griefs, TrustLosses;
    private long _cursor;
    private readonly List<(long tick, int room, int cause)> _sealedIn = new(); // 사람이 안에 있는데 봉쇄된 방 (그 뒤 죽음을 컴퓨터 탓으로)

    public Society(Layout layout) { L = layout; }

    public float BondOf(int a, int b) => a >= 0 && b >= 0 && a < N && b < N ? Bond[a * N + b] : 0;
    public float Trust(int a) => a >= 0 && a < N ? TrustComputer[a] : 1;
    public float GriefOf(int a) => a >= 0 && a < N ? Grief[a] : 0;

    private void Resize(int n)
    {
        if (n == N) return;
        var b = new float[n * n]; var t = new float[n]; var g = new float[n];
        for (int i = 0; i < n; i++) t[i] = 0.8f;
        for (int i = 0; i < Math.Min(n, N); i++)
        {
            t[i] = TrustComputer[i]; g[i] = Grief[i];
            for (int j = 0; j < Math.Min(n, N); j++) b[i * n + j] = Bond[i * N + j];
        }
        Bond = b; TrustComputer = t; Grief = g; N = n;
    }

    public void Step(Sim sim, double dt)
    {
        _mind ??= sim.Find<Mind>(); _life ??= sim.Find<Life>();
        if (_mind == null || _life == null) return;
        Resize(_mind.Agents.Count);
        Events(sim);
        if (sim.Tick % TogetherEvery == 0) Together(sim, TogetherEvery * dt);
    }

    // ───────────────────────── 함께 보낸 시간 ─────────────────────────

    private void Together(Sim sim, double dt)
    {
        var ag = _mind!.Agents;
        double decay = Math.Exp(-dt * 0.6931 / 86400);  // 슬픔은 하루 반감
        for (int i = 0; i < N; i++) Grief[i] = (float)(Grief[i] * decay);
        for (int i = 0; i < N; i++)
        {
            var a = ag[i];
            if (!a.IsPerson || a.C.Up == 0) continue;
            if (!Leisure(a.C.Goal)) continue;
            for (int j = 0; j < N; j++)
            {
                if (j == i) continue;
                var b = ag[j];
                if (!b.IsPerson || b.C.Up == 0) continue;
                if (!Leisure(b.C.Goal)) continue;
                if (_mind.RoomOf(a.C.Cell) != _mind.RoomOf(b.C.Cell) || _mind.Dist(a.C.Cell, b.C.Cell) > 4) continue;
                // 어울림: 시간당 약 0.05 × 맞음 (성격이 비슷하고 둘 다 다정하면 빨리)
                float fit = 1 - Math.Abs(a.C.Sociable - b.C.Sociable) * 0.5f + (a.C.Kindness + b.C.Kindness) * 0.25f;
                float talk = a.C.Goal == GoalKind.Socialize || b.C.Goal == GoalKind.Socialize ? 1.5f : 0.7f;
                Raise(sim, i, j, (float)(0.05 * fit * talk * dt / 3600), 0, "함께 지낸 시간");
            }
        }
    }

    /// <summary>함께 쉬는 때 (어울림 · 쉼 · 밥 · 빈둥거림) — 일하거나 자거나 위급할 때는 정이 쌓이지 않는다.</summary>
    private static bool Leisure(GoalKind g) => g is GoalKind.Socialize or GoalKind.Relax or GoalKind.Eat or GoalKind.Idle;

    /// <summary>정을 더하고 문턱을 넘으면 사실로 남긴다.</summary>
    private void Raise(Sim sim, int a, int b, float delta, int cause, string why)
    {
        int k = a * N + b;
        float before = Bond[k];
        Bond[k] = Math.Clamp(before + delta, -1, 1);
        if (before < FriendAt && Bond[k] >= FriendAt && BondOf(b, a) >= FriendAt * 0.8f)
        {
            Friendships++;
            int c = sim.Causes.Add(CauseKind.Person, $"{Ko.Gwa(_mind!.NameOf(a))} {Ko.I(_mind.NameOf(b))} 가까워졌다 — {why}", sim.Tick, _mind.Agents[a].C.Cell, cause, 0, a);
            sim.Facts.Emit(sim.Tick, FactKind.Relationship, _mind.Agents[a].C.Cell, Bond[k], c, a, b);
        }
    }

    // ───────────────────────── 일어난 일 ─────────────────────────

    private void Events(Sim sim)
    {
        var mind = _mind!;
        foreach (var f in sim.Facts.Since(_cursor))
        {
            switch (f.Kind)
            {
                case FactKind.Rescue when f.Actor >= 0 && f.Other >= 0 && f.Other < N:
                {
                    // 업혀 나온 사람은 구한 이에게 목숨을 빚진다 (로봇에게도) · 본 사람들도 그를 다시 본다
                    int v = f.Other, r = f.Actor;
                    float before = BondOf(v, r);
                    Bond[v * N + r] = Math.Clamp(before + 0.45f, -1, 1);
                    if (mind.Agents[r].IsPerson) Bond[r * N + v] = Math.Clamp(BondOf(r, v) + 0.2f, -1, 1);
                    int room = mind.RoomOf(f.Cell);
                    foreach (var o in mind.Agents)
                        if (o.IsPerson && o.Id != v && o.Id != r && o.C.Up != 0 && mind.RoomOf(o.C.Cell) == room)
                            Bond[o.Id * N + r] = Math.Clamp(BondOf(o.Id, r) + 0.1f, -1, 1);
                    int c = sim.Causes.Add(CauseKind.Person, $"{Ko.I(mind.NameOf(v))} {mind.NameOf(r)}에게 목숨을 빚졌다", sim.Tick, f.Cell, f.Cause, 0, v);
                    sim.Facts.Emit(sim.Tick, FactKind.Relationship, f.Cell, Bond[v * N + r], c, v, r);
                    break;
                }
                case FactKind.ComputerSeal when f.Size > 0:
                    _sealedIn.Add((f.Tick, f.Other, f.Cause));
                    if (_sealedIn.Count > 32) _sealedIn.RemoveAt(0);
                    break;
                case FactKind.Died:
                {
                    int dead = mind.AgentOfBody(f.Actor);
                    if (dead < 0) break;
                    // 슬픔: 배에 함께 탄 사람 0.15 + 정
                    for (int i = 0; i < N; i++)
                    {
                        var p = mind.Agents[i];
                        if (i == dead || !p.IsPerson || _life![p.C.Body].Alive == 0) continue;
                        float g = Math.Clamp(0.15f + Math.Max(0, BondOf(i, dead)), 0, 1);
                        if (g > Grief[i]) Grief[i] = g;
                        p.C.Sad = Math.Max(p.C.Sad, g);
                        p.C.Stress = Math.Min(1, p.C.Stress + g * 0.5f);
                        if (BondOf(i, dead) >= FriendAt * 0.6f)
                        {
                            Griefs++;
                            int c = sim.Causes.Add(CauseKind.Person, $"{Ko.I(mind.NameOf(i))} {Ko.Eul(mind.NameOf(dead))} 잃었다", sim.Tick, p.C.Cell, f.Cause, 0, i);
                            sim.Facts.Emit(sim.Tick, FactKind.Grieved, p.C.Cell, g, c, i, dead);
                        }
                    }
                    // 사람이 있는 채 봉쇄된 방에서 30분 안에 죽었다 → 주컴퓨터를 덜 믿는다 (정이 깊을수록 더)
                    int room = mind.RoomOf(f.Cell);
                    foreach (var (tick, sroom, scause) in _sealedIn)
                    {
                        if (sroom != room || f.Tick - tick > Time.Minutes(30)) continue;
                        for (int i = 0; i < N; i++)
                        {
                            var p = mind.Agents[i];
                            if (i == dead || !p.IsPerson || _life![p.C.Body].Alive == 0) continue;
                            TrustComputer[i] = Math.Clamp(TrustComputer[i] - 0.25f - 0.4f * Math.Max(0, BondOf(i, dead)), 0, 1);
                        }
                        TrustLosses++;
                        int c = sim.Causes.Add(CauseKind.Person, $"주컴퓨터가 닫은 문 안에서 {Ko.I(mind.NameOf(dead))} 죽었다 — 사람들이 주컴퓨터를 덜 믿는다", sim.Tick, f.Cell, scause, f.Cause);
                        sim.Facts.Emit(sim.Tick, FactKind.TrustLost, f.Cell, 0, c, dead, -2);
                        break;
                    }
                    break;
                }
            }
        }
        _cursor = sim.Facts.Total;
    }

    // ───────────────────────── 찍기 ─────────────────────────

    public void Save(StateWriter w)
    {
        w.I32(N); w.Arr(Bond); w.Arr(TrustComputer); w.Arr(Grief);
        w.I64(Friendships); w.I64(Griefs); w.I64(TrustLosses); w.I64(_cursor);
        w.I32(_sealedIn.Count); foreach (var (t, r, c) in _sealedIn) { w.I64(t); w.I32(r); w.I32(c); }
    }

    public void Load(StateReader r)
    {
        N = r.I32(); Bond = r.Arr<float>(); TrustComputer = r.Arr<float>(); Grief = r.Arr<float>();
        Friendships = r.I64(); Griefs = r.I64(); TrustLosses = r.I64(); _cursor = r.I64();
        _sealedIn.Clear(); int n = r.I32(); for (int i = 0; i < n; i++) { long t = r.I64(); int room = r.I32(); int c = r.I32(); _sealedIn.Add((t, room, c)); }
    }
}
