namespace ShipEngine;

/// <summary>숨긴 실수 하나 — 그 사람만 안다 (본 이는 그 사람이 장치를 손보는 것만 봤다).</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct HiddenMistake
{
    public int Cause;    // 실수의 원인 마디
    public int Who;      // 한 사람 (에이전트)
    public int Device;   // 손댄 장치
    public long Tick;
    public int W1, W2;   // 그때 곁에서 본 이 (−1 없음)
}

/// <summary>남 탓 거짓말 하나.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct LieRecord
{
    public int Cause;    // 거짓말의 원인 마디
    public int Liar, Scape;   // 둘러댄 이 · 탓한 이 (−1: '원래 낡았다')
    public byte ScapeRobot, Exposed;
    public int Device, Mistake;   // 장치 · 숨긴 실수의 원인 마디
    public long Fail, Tick;       // 망가진 때 · 둘러댄 때
    public int W1, W2;
}

/// <summary>거짓말을 들은 이 하나.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct LieHeard
{
    public int Lie, Who;
    public byte Believed;
}

/// <summary>주컴퓨터에 기록을 물어보려는 이.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct LogCheck
{
    public long Due;
    public int Who, Lie;
}

/// <summary>
/// ⑨ 숨긴 실수 (깊이 바퀴 2 — ⑫ 비밀 씨앗): 손보다 부품을 상하게 하고 말하지 않은 사람만 안다 (사실 Mistake · 크기 2).
/// 그 장치가 망가지면 (고장의 원인 사슬에 그 실수가 있다) 그 사람이 제 탓인 줄 안다 — 마음이 무겁다 (긴장 · 슬픔) · 털어놓을지 숨길지는 그 사람이:
/// 성실 × (0.5 + 0.5 × 다정) − 0.2 × 신경성 (± 0.15)이 0.35를 넘으면 털어놓는다 — 배 사람들은 조금 원망하지만 정직함을 더 믿는다 · 마음이 가벼워진다.
/// 숨기면 제 탓의 기억과 무거운 마음이 남는다 (사람들은 모른다 — 관객만 본다).
/// <para><b>거짓말과 속임 (⑧ · ⑨ 바퀴 3)</b>: 숨긴 일(숨긴 실수 · 몰래 한 짓 · 거짓말)은 원인 사슬에 있어도 남이 탓하지 못한다 — 아는 것만 탓한다.
/// 숨긴 이가 털어놓지 않으면 둘러대기도 한다: 0.5 × (1 − 성실) + 0.25 × 신경성 + 0.15 × 외향 − 0.3 × 다정 (± 0.15)이 0.35를 넘으면
/// (거짓말 일기 연구 DePaulo 1996 · 성격과 거짓말 Kashy & DePaulo 1996: 사교적인 이가 더 · 성실 · 다정한 이는 덜 — 잘못을 덮는 거짓말이 '큰 거짓말'의 으뜸 DePaulo 2004)
/// 사흘 안에 그 장치를 손본 다른 이에게 떠넘긴다 (덜 좋아하는 사람일수록 · 로봇도) — 아무도 없으면 "원래 낡은 부품이었다".
/// 듣는 이는 반쯤 믿는다: 0.53 (속임 알아채기 메타 분석 Bond & DePaulo 2006 — 거짓을 거짓으로 맞힌 몫 47%) + 0.4 × (말한 이를 믿는 정도 − 0.5) − 0.3 × (탓 듣는 이를 믿는 정도 − 0.5) − 0.25 × 들킨 적.
/// 믿으면 탓 듣는 이를 원망하고 (험담으로 번진다) · 의심하면 말한 이를 조금 덜 믿고 주컴퓨터 기록을 물어볼 수도 있다.
/// <b>들통</b>: 그때 곁에서 본 이가 그 말을 들으면 (그 사람이 손보던 것을 봤다) · 탓 들은 이나 의심한 이가 주컴퓨터 정비 기록을 물었는데 망가지기 전에 연 이로 둘러댄 이가 나오면.
/// 들키면 믿음이 크게 무너진다 (속임으로 깨진 믿음은 사과로도 다 돌아오지 않는다 — Schweitzer, Hershey & Bradlow 2006): 들은 이 −0.35 · 다른 이 −0.2 (성실할수록 더) · 원망 · 탓 들은 이는 깊게 원망한다.</para>
/// </summary>
public sealed partial class Society
{
    public readonly List<HiddenMistake> Hidden = new();
    public readonly List<LieRecord> Lies = new();
    public readonly List<LieHeard> Heard = new();
    public readonly List<LogCheck> Checks = new();
    private readonly HashSet<int> _secret = new();      // 남이 모르는 원인 마디 (탓할 수 없다)
    private readonly List<(int dev, int who, long tick)> _hands = new();   // 사람들이 아는 손본 이 (고쳤다 · 떼어 냈다 — 최근 사흘)
    public float[] LiarMark = Array.Empty<float>();      // 거짓말이 들킨 횟수 (그 사람 말을 덜 믿는다)
    public long Confessions, SecretsKept, LiesTold, LiesExposed, LiesBelieved, LiesDoubted, LogChecks;
    public const float LieAt = 0.35f, BelieveBase = 0.53f;

    public bool Secret(int cause) => _secret.Contains(cause);
    public int LieOf(int cause) { for (int i = 0; i < Lies.Count; i++) if (Lies[i].Cause == cause) return i; return -1; }

    private void Reveal(Sim sim, in Fact f)
    {
        var chain = sim.Causes.Chain(f.Cause);
        for (int i = Hidden.Count - 1; i >= 0; i--)
        {
            var h = Hidden[i];
            if (!chain.Contains(h.Cause)) continue;
            Hidden.RemoveAt(i);
            int x = h.Who;
            if (!Person(x)) continue;
            ref var c = ref C(x);
            c.Stress = Math.Min(1, c.Stress + 0.25f); c.Sad = Math.Min(1, c.Sad + 0.1f);
            string dev = DevName(h.Device);
            float honest = c.Consc * (0.5f + 0.5f * c.Agree) - 0.2f * c.Neuro + 0.3f * (Rnd(sim, "털어놓기", x, h.Device) - 0.5f);
            if (honest > 0.35f)
            {
                Confessions++;
                _secret.Remove(h.Cause);
                int cause = sim.Causes.Add(CauseKind.Person, $"{Ko.I(_mind!.NameOf(x))} 털어놓았다 — {Ko.Eul(dev)} 손보다 상하게 한 것이 자기였다고", sim.Tick, c.Cell, f.Cause, 0, x);
                sim.Facts.Emit(sim.Tick, FactKind.Confessed, c.Cell, 1, cause, x, h.Device);
                for (int o = 0; o < N; o++)
                {
                    if (o == x || !Person(o)) continue;
                    Blame(sim, o, x, 0.1f, cause, c.Cell);
                    Add(TrustOf, o, x, 0.12f);                     // 정직함을 더 믿는다 (원망이 덜어 간 0.03보다 크게)
                    Remember(sim, o, Topic.Work, x, cause, 0.4f, c.Cell);
                }
                c.Stress = Math.Max(0, c.Stress - 0.15f);           // 털어놓으니 가볍다
                continue;
            }
            SecretsKept++;
            float deceit = 0.5f * (1 - c.Consc) + 0.25f * c.Neuro + 0.15f * c.Extra - 0.3f * c.Agree + 0.3f * (Rnd(sim, "둘러대기", x, h.Device) - 0.5f);
            if (deceit > LieAt) { TellLie(sim, h, f); continue; }
            int kept = sim.Causes.Add(CauseKind.Person, $"{Ko.Eun(_mind!.NameOf(x))} 말하지 않았다 — {Ko.I(dev)} 망가진 것이 제 탓인 줄 안다", sim.Tick, c.Cell, f.Cause, 0, x);
            _secret.Add(kept);
            sim.Facts.Emit(sim.Tick, FactKind.Mistake, c.Cell, 3, kept, x, h.Device);
            Remember(sim, x, Topic.Blame, x, kept, 0.7f, c.Cell);   // 제 탓의 기억
            c.Stress = Math.Min(1, c.Stress + 0.1f);
        }
    }

    private string DevName(int d) => _net != null && d >= 0 && d < _net.Devices.Count ? _net.Devices[d].Name : "장치";

    /// <summary>남 탓으로 둘러댄다: 사흘 안에 그 장치를 손본 다른 이 (덜 좋아할수록 · 로봇은 0.4) — 없으면 "원래 낡았다".</summary>
    private void TellLie(Sim sim, in HiddenMistake h, in Fact fail)
    {
        int x = h.Who;
        ref var c = ref C(x);
        var mind = _mind!;
        int scape = -1; float best = 0.2f;
        foreach (var (dev, who, tick) in _hands)
        {
            if (dev != h.Device || who == x || who < 0 || who >= N || sim.Tick - tick > Time.Hours(24 * 3)) continue;
            var o = mind.Agents[who];
            float w = o.IsPerson ? (Alive(who) ? 0.6f - Liking(x, who) : 0) : 0.4f;
            if (w > best) { best = w; scape = who; }
        }
        bool robot = scape >= 0 && !mind.Agents[scape].IsPerson;
        string dn = DevName(h.Device);
        string text = scape >= 0 ? $"{Ko.I(mind.NameOf(x))} 둘러댔다 — {Ko.Eun(dn)} {Ko.I(mind.NameOf(scape))} 손본 뒤로 이상했다고"
                                 : $"{Ko.I(mind.NameOf(x))} 둘러댔다 — {dn} 부품이 원래 낡았다고";
        int cause = sim.Causes.Add(CauseKind.Person, text, sim.Tick, c.Cell, fail.Cause, 0, x);
        _secret.Add(cause);
        LiesTold++;
        Lies.Add(new LieRecord { Cause = cause, Liar = x, Scape = scape, ScapeRobot = (byte)(robot ? 1 : 0), Device = h.Device, Mistake = h.Cause, Fail = fail.Tick, Tick = sim.Tick, W1 = h.W1, W2 = h.W2 });
        sim.Facts.Emit(sim.Tick, FactKind.Lie, c.Cell, scape < 0 ? 0 : robot ? 2 : 1, cause, x, scape);
        c.Stress = Math.Min(1, c.Stress + 0.1f);                       // 거짓말은 무겁다
        Remember(sim, x, Topic.Blame, x, cause, 0.6f, c.Cell);
        int li = Lies.Count - 1;
        foreach (int l in Near(c.Cell, x)) Hear(sim, li, l, x);
    }

    /// <summary>l이 거짓말을 듣는다 (from에게서 — 둘러댄 이 또는 험담으로 옮긴 이).</summary>
    private void Hear(Sim sim, int li, int l, int from)
    {
        var lie = Lies[li];
        if (lie.Exposed != 0 || l == lie.Liar || !Person(l)) return;
        foreach (var hh in Heard) if (hh.Lie == li && hh.Who == l) return;
        ref var cl = ref C(l);
        int cell = cl.Cell;
        if (l == lie.Scape)
        {
            // 탓 듣는 이: 제가 하지 않은 줄 안다 — 화가 나고 · 둘러댄 이를 원망하고 · 주컴퓨터 기록을 물어본다 (거의 다)
            Heard.Add(new LieHeard { Lie = li, Who = l, Believed = 0 });
            Add(Grudge, l, lie.Liar, 0.4f); Add(TrustOf, l, lie.Liar, -0.3f);
            cl.Anger = Math.Min(1, cl.Anger + 0.3f);
            Remember(sim, l, Topic.Blame, lie.Liar, lie.Cause, 0.8f, cell);
            if (Rnd(sim, "기록 묻기", l, lie.Cause) < 0.5f + 0.25f * cl.Consc + 0.25f * cl.Open)
                Checks.Add(new LogCheck { Due = sim.Tick + Time.Minutes(20 + (int)(100 * Rnd(sim, "언제 묻나", l, lie.Cause))), Who = l, Lie = li });
            return;
        }
        if (l == lie.W1 || l == lie.W2) { Heard.Add(new LieHeard { Lie = li, Who = l, Believed = 0 }); Expose(sim, li, l, 1); return; }
        float p = BelieveBase + 0.4f * (TrustIn(l, from) - 0.5f) - 0.25f * LiarMark[from];
        if (lie.Scape >= 0 && lie.ScapeRobot == 0) p -= 0.3f * (TrustIn(l, lie.Scape) - 0.5f);
        p = Math.Clamp(p, 0.1f, 0.9f);
        bool believe = Rnd(sim, "믿음", l, lie.Cause) < p;
        Heard.Add(new LieHeard { Lie = li, Who = l, Believed = (byte)(believe ? 1 : 0) });
        if (believe)
        {
            LiesBelieved++;
            if (lie.Scape >= 0 && lie.ScapeRobot == 0) Blame(sim, l, lie.Scape, 0.35f, lie.Cause, cell);
            else if (lie.ScapeRobot != 0) { TrustComputer[l] = Math.Clamp(TrustComputer[l] - 0.05f, 0, 1); Remember(sim, l, Topic.Work, lie.Scape, lie.Cause, 0.4f, cell); }
            else Remember(sim, l, Topic.Work, -1, lie.Cause, 0.3f, cell);
        }
        else
        {
            // 의심한다: 말한 이를 조금 덜 믿고 · 꼼꼼하거나 궁금한 이는 주컴퓨터 기록을 물어본다
            LiesDoubted++;
            Add(TrustOf, l, from, -0.05f);
            if (Rnd(sim, "기록 묻기", l, lie.Cause) < 0.15f + 0.25f * cl.Consc + 0.2f * cl.Open)
                Checks.Add(new LogCheck { Due = sim.Tick + Time.Minutes(30 + (int)(150 * Rnd(sim, "언제 묻나", l, lie.Cause))), Who = l, Lie = li });
        }
    }

    /// <summary>기록 물을 때가 된 이가 주컴퓨터에 묻는다 (사실 '기록을 물었다' — 주컴퓨터가 답한다). 자고 있으면 30분 뒤.</summary>
    private void LogChecksStep(Sim sim)
    {
        for (int i = Checks.Count - 1; i >= 0; i--)
        {
            var k = Checks[i];
            if (k.Due > sim.Tick) continue;
            var lie = Lies[k.Lie];
            if (lie.Exposed != 0 || !Person(k.Who)) { Checks.RemoveAt(i); continue; }
            if (!Awake(k.Who)) { k.Due = sim.Tick + Time.Minutes(30); Checks[i] = k; continue; }
            Checks.RemoveAt(i);
            LogChecks++;
            int cause = sim.Causes.Add(CauseKind.Person, $"{Ko.I(_mind!.NameOf(k.Who))} 주컴퓨터에 {DevName(lie.Device)} 정비 기록을 물었다", sim.Tick, C(k.Who).Cell, lie.Cause, 0, k.Who);
            float hours = (float)Math.Ceiling((sim.Tick - lie.Fail) / (double)Time.TicksPerHour) + 24 * 3;
            sim.Facts.Emit(sim.Tick, FactKind.LogAsked, C(k.Who).Cell, hours, cause, k.Who, lie.Device);
        }
    }

    /// <summary>주컴퓨터의 답 하나: 망가지기 전에 그 장치를 연 이가 둘러댄 이면 들통난다.</summary>
    private void LogShown(Sim sim, in Fact f)
    {
        if (f.Actor < 0) return;
        long at = f.Tick - (long)Math.Round(f.Size * Time.TicksPerHour);
        int asker = f.Cause > 0 ? sim.Causes[sim.Causes[f.Cause].Parent].Actor : -1;
        for (int li = 0; li < Lies.Count; li++)
        {
            var lie = Lies[li];
            if (lie.Exposed != 0 || lie.Device != f.Other || lie.Liar != f.Actor || at > lie.Fail) continue;
            Expose(sim, li, asker, 2, f.Cause);
        }
    }

    /// <summary>들통: 믿음이 크게 무너진다 · 탓 들은 이의 누명이 벗겨진다 · 둘러댄 이는 부끄럽다.</summary>
    private void Expose(Sim sim, int li, int by, int how, int parent = 0)
    {
        var lie = Lies[li];
        if (lie.Exposed != 0) return;
        lie.Exposed = (byte)how; Lies[li] = lie;
        LiesExposed++;
        _secret.Remove(lie.Cause); _secret.Remove(lie.Mistake);
        var mind = _mind!;
        int x = lie.Liar;
        string liar = mind.NameOf(x), dn = DevName(lie.Device);
        string text = how == 1 && by >= 0 ? $"{Ko.I(mind.NameOf(by))} 봤다 — {Ko.Eul(dn)} 손보던 것은 {Ko.Was(liar)} · {liar}의 말은 거짓이었다"
                                          : $"주컴퓨터 기록에 {Ko.I(liar)} 있었다 — 망가지기 전에 {Ko.Eul(dn)} 연 것은 {Ko.Was(liar)} · {liar}의 말은 거짓이었다";
        int cell = x < mind.Agents.Count ? mind.Agents[x].C.Cell : -1;
        int cause = sim.Causes.Add(CauseKind.Person, text, sim.Tick, cell, parent != 0 ? parent : lie.Cause, 0, by >= 0 ? by : x);
        sim.Facts.Emit(sim.Tick, FactKind.LieExposed, cell, how, cause, x, by);
        if (x < LiarMark.Length) LiarMark[x] += 1;
        for (int o = 0; o < N; o++)
        {
            if (o == x || !Person(o)) continue;
            bool heard = false, believed = false;
            foreach (var hh in Heard) if (hh.Lie == li && hh.Who == o) { heard = true; believed = hh.Believed != 0; }
            if (believed && lie.Scape >= 0 && lie.ScapeRobot == 0)
            {
                Grudge[o * N + lie.Scape] *= 0.3f;                 // 누명이 벗겨진다
                Add(TrustOf, o, lie.Scape, 0.1f);
            }
            float drop = (heard ? 0.35f : 0.2f) * (0.6f + 0.8f * C(o).Consc);
            Add(TrustOf, o, x, -drop);
            if (o == lie.Scape) { Add(Grudge, o, x, 0.5f); Warm(sim, o, x, -0.3f); Remember(sim, o, Topic.Blame, x, cause, 1, cell); }
            else if (heard) Blame(sim, o, x, 0.4f, cause, cell);
            else { Add(Grudge, o, x, 0.15f); Remember(sim, o, Topic.Blame, x, cause, 0.5f, cell); }
        }
        if (Person(x))
        {
            ref var cx = ref C(x);
            cx.Stress = Math.Min(1, cx.Stress + 0.3f); cx.Sad = Math.Min(1, cx.Sad + 0.2f);   // 들켰다 — 부끄럽다
        }
    }

    private void SaveSecrets(StateWriter w)
    {
        w.List(Hidden); w.I64(Confessions); w.I64(SecretsKept);
        w.List(Lies); w.List(Heard); w.List(Checks); w.Arr(_secret.OrderBy(k => k).ToArray()); w.Arr(LiarMark);
        w.I32(_hands.Count); foreach (var (d, who, t) in _hands) { w.I32(d); w.I32(who); w.I64(t); }
        w.I64(LiesTold); w.I64(LiesExposed); w.I64(LiesBelieved); w.I64(LiesDoubted); w.I64(LogChecks);
    }
    private void LoadSecrets(StateReader r)
    {
        Hidden.Clear(); Hidden.AddRange(r.List<HiddenMistake>()); Confessions = r.I64(); SecretsKept = r.I64();
        Lies.Clear(); Lies.AddRange(r.List<LieRecord>()); Heard.Clear(); Heard.AddRange(r.List<LieHeard>()); Checks.Clear(); Checks.AddRange(r.List<LogCheck>());
        _secret.Clear(); foreach (int k in r.Arr<int>()) _secret.Add(k); LiarMark = r.Arr<float>();
        _hands.Clear(); int n = r.I32(); for (int i = 0; i < n; i++) { int d = r.I32(); int who = r.I32(); long t = r.I64(); _hands.Add((d, who, t)); }
        LiesTold = r.I64(); LiesExposed = r.I64(); LiesBelieved = r.I64(); LiesDoubted = r.I64(); LogChecks = r.I64();
    }
}
