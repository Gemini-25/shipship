namespace ShipEngine;

/// <summary>
/// 판단 이관 (라) 비밀 — 제 실수를 털어놓을지 · 숨길지 · 남 탓으로 둘러댈지 · 들은 말을 믿을지 · 기록을 물을지는 사람의 판단이다 (docs/ENGINE.md §3 9).
/// 손보다 상하게 한 장치가 망가지면 (고장의 원인 사슬에 그 실수가 있다) 그 사람은 제 탓인 줄 안다 — 깨어 있으면 정한다:
/// 성실 × (0.5 + 0.5 × 다정) − 0.2 × 신경성 (± 0.15)이 0.35를 넘으면 털어놓는다 · 아니면 0.5 × (1 − 성실) + 0.25 × 신경성 + 0.15 × 외향 − 0.3 × 다정 (± 0.15)이 0.35를 넘으면
/// 사흘 안에 그 장치를 손본 다른 이에게 떠넘긴다 (덜 좋아할수록 · 로봇도 — 아무도 없으면 "원래 낡았다") · 아니면 말하지 않는다.
/// 둘러댄 말을 들은 이 (곁 · 험담 · 따지는 말로): 탓 들은 이는 제가 하지 않은 줄 알고 (거의 다) 기록을 물으려 한다 · 그때 곁에서 본 이는 거짓인 줄 알아챈다 ·
/// 나머지는 반쯤 믿는다 (0.53 + 말한 이를 믿는 만큼 − 탓 들은 이를 믿는 만큼 − 들킨 적) · 의심한 이는 꼼꼼하거나 궁금하면 기록을 물으려 한다.
/// 기록을 물을 때가 되면 깨어 있을 때 주컴퓨터에 묻는다 (자고 있으면 30분 뒤). 사회(월드 엔진)는 그 결과(믿음 · 원망 · 소문 · 들통)만 계산한다.
/// </summary>
public sealed partial class Mind
{
    private long _secretCursor;
    private readonly List<LogCheck> _logChecks = new();   // 기록을 물으려는 이 (Lie = 거짓말의 원인 마디)
    private readonly HashSet<long> _heardNow = new();

    /// <summary>틱마다: 망가진 장치에 제 숨긴 실수가 있으면 정한다 · 기록을 물을 때가 된 이가 묻는다.</summary>
    private void SecretStep(Sim sim)
    {
        var soc = _soc;
        if (soc == null) return;
        _heardNow.Clear();
        foreach (var f in sim.Facts.Since(_secretCursor))
        {
            if (f.Kind == FactKind.MachineFail && soc.Hidden.Count > 0) Realize(sim, f);
            else if (f.Kind == FactKind.Mistake && f.Size >= Network.HandledSize && f.Size < 2) Handled(sim, f);
        }
        _secretCursor = sim.Facts.Total;
        if (_logChecks.Count > 0) AskLogs(sim);
    }

    private bool PersonUp(int i) => PersonAlive(i) && Agents[i].C.Up != 0;

    /// <summary>
    /// 손보다 부품이 상했다 (⑥이 계산한 일): 알아챘나 — 솜씨 · 성실만큼 (0.25 + 0.5 × 수리 솜씨 + 0.25 × 성실) · 알아챘으면 바로 털어놓을지 (고장 뒤와 같은 정직함) · 입을 다물지.
    /// 몰랐으면 아무 일도 없다 — 고장 난 뒤에야 제 탓인 줄 안다.
    /// </summary>
    private void Handled(Sim sim, in Fact f)
    {
        int x = f.Actor;
        if (!PersonUp(x)) return;
        ref var c = ref Agents[x].C;
        if (SocRnd(sim, "알아챔", x, f.Other) >= 0.25f + 0.5f * c.SkRepair + 0.25f * c.Consc) return;   // 모른다
        string dev = _net != null && f.Other >= 0 && f.Other < _net.Devices.Count ? _net.Devices[f.Other].Name : "장치";
        float honest = c.Consc * (0.5f + 0.5f * c.Agree) - 0.2f * c.Neuro + 0.3f * (SocRnd(sim, "털어놓기", x, f.Other) - 0.5f);
        if (honest > 0.35f)
        {
            int cause = sim.Causes.Add(CauseKind.Person, $"{Ko.I(NameOf(x))} 털어놓았다 — {Ko.Eul(dev)} 손보다 부품을 상하게 했다고", sim.Tick, c.Cell, f.Cause, 0, x);
            Act(sim, new SocialAct { Kind = SocialActKind.Confess, Actor = x, Target = -1, Other = f.Cause, Arg = f.Other, Room = RoomOf(c.Cell), Cell = c.Cell, Cause = cause });
            return;
        }
        int kept = sim.Causes.Add(CauseKind.Person, $"{Ko.Eun(NameOf(x))} 아무에게도 말하지 않았다", sim.Tick, c.Cell, f.Cause, 0, x);
        Act(sim, new SocialAct { Kind = SocialActKind.Conceal, Actor = x, Target = -1, Other = f.Cause, Arg = f.Other, Room = RoomOf(c.Cell), Cell = c.Cell, Cause = kept });
    }

    /// <summary>그 고장이 제 숨긴 실수 탓인 줄 안 이가 정한다: 털어놓는다 · 둘러댄다 · 말하지 않는다.</summary>
    private void Realize(Sim sim, in Fact fail)
    {
        var soc = _soc!;
        var chain = sim.Causes.Chain(fail.Cause);
        foreach (var h in soc.Hidden.ToList())
        {
            if (!chain.Contains(h.Cause)) continue;
            int x = h.Who;
            if (!PersonUp(x)) continue;   // 쓰러졌거나 자고 있다 — 사회가 그 일을 묻는다
            ref var c = ref Agents[x].C;
            string dev = _net != null && h.Device >= 0 && h.Device < _net.Devices.Count ? _net.Devices[h.Device].Name : "장치";
            float honest = c.Consc * (0.5f + 0.5f * c.Agree) - 0.2f * c.Neuro + 0.3f * (SocRnd(sim, "털어놓기", x, h.Device) - 0.5f);
            if (honest > 0.35f)
            {
                int cause = sim.Causes.Add(CauseKind.Person, $"{Ko.I(NameOf(x))} 털어놓았다 — {Ko.Eul(dev)} 손보다 상하게 한 것이 자기였다고", sim.Tick, c.Cell, fail.Cause, 0, x);
                Act(sim, new SocialAct { Kind = SocialActKind.Confess, Actor = x, Target = -1, Other = h.Cause, Arg = h.Device, Room = RoomOf(c.Cell), Cell = c.Cell, Cause = cause });
                continue;
            }
            float deceit = 0.5f * (1 - c.Consc) + 0.25f * c.Neuro + 0.15f * c.Extra - 0.3f * c.Agree + 0.3f * (SocRnd(sim, "둘러대기", x, h.Device) - 0.5f);
            if (deceit > Society.LieAt) { TellLie(sim, h, fail, dev); continue; }
            int kept = sim.Causes.Add(CauseKind.Person, $"{Ko.Eun(NameOf(x))} 말하지 않았다 — {Ko.I(dev)} 망가진 것이 제 탓인 줄 안다", sim.Tick, c.Cell, fail.Cause, 0, x);
            Act(sim, new SocialAct { Kind = SocialActKind.KeepSecret, Actor = x, Target = -1, Other = h.Cause, Arg = h.Device, Room = RoomOf(c.Cell), Cell = c.Cell, Cause = kept });
        }
    }

    /// <summary>남 탓으로 둘러댄다: 사흘 안에 그 장치를 손본 다른 이 (덜 좋아할수록 · 로봇은 0.4) — 없으면 "원래 낡았다" · 곁에 있던 이들이 듣는다.</summary>
    private void TellLie(Sim sim, in HiddenMistake h, in Fact fail, string dn)
    {
        var soc = _soc!;
        int x = h.Who;
        ref var c = ref Agents[x].C;
        int scape = -1; float best = 0.2f;
        foreach (var (dev, who, tick) in soc.Hands)
        {
            if (dev != h.Device || who == x || who < 0 || who >= soc.N || sim.Tick - tick > Time.Hours(24 * 3)) continue;
            var o = Agents[who];
            float w = o.IsPerson ? (PersonAlive(who) ? 0.6f - soc.Liking(x, who) : 0) : 0.4f;
            if (w > best) { best = w; scape = who; }
        }
        bool robot = scape >= 0 && !Agents[scape].IsPerson;
        string text = scape >= 0 ? $"{Ko.I(NameOf(x))} 둘러댔다 — {Ko.Eun(dn)} {Ko.I(NameOf(scape))} 손본 뒤로 이상했다고"
                                 : $"{Ko.I(NameOf(x))} 둘러댔다 — {dn} 부품이 원래 낡았다고";
        int cause = sim.Causes.Add(CauseKind.Person, text, sim.Tick, c.Cell, fail.Cause, 0, x);
        Act(sim, new SocialAct { Kind = SocialActKind.TellLie, Actor = x, Target = scape, Other = h.Cause, Arg = h.Device, Value = robot ? 1 : 0, Room = RoomOf(c.Cell), Cell = c.Cell, Cause = cause });
        foreach (int l in soc.Near(c.Cell, x))
            HearLie(sim, l, x, new LieRecord { Cause = cause, Liar = x, Scape = scape, ScapeRobot = (byte)(robot ? 1 : 0), Device = h.Device, Mistake = h.Cause, Fail = fail.Tick, Tick = sim.Tick, W1 = h.W1, W2 = h.W2 });
    }

    /// <summary>l이 거짓말을 들었다 (from에게서 — 둘러댄 이 · 험담으로 옮긴 이 · 따지는 말) — 그 사람이 받는다.</summary>
    private void HearLie(Sim sim, int l, int from, in LieRecord lie)
    {
        var soc = _soc!;
        if (lie.Exposed != 0 || l == lie.Liar || !PersonUp(l)) return;
        int li = soc.LieOf(lie.Cause);
        if (li >= 0) foreach (var hh in soc.Heard) if (hh.Lie == li && hh.Who == l) return;
        if (!_heardNow.Add(((long)lie.Cause << 20) | (uint)l)) return;
        ref var cl = ref Agents[l].C;
        int how; float due = 0;
        if (l == lie.Scape)
        {
            // 탓 듣는 이: 제가 하지 않은 줄 안다 — 주컴퓨터 기록을 물으려 한다 (거의 다)
            how = 3;
            if (SocRnd(sim, "기록 묻기", l, lie.Cause) < 0.5f + 0.25f * cl.Consc + 0.25f * cl.Open) due = 20 + (int)(100 * SocRnd(sim, "언제 묻나", l, lie.Cause));
        }
        else if (l == lie.W1 || l == lie.W2) how = 2;   // 그때 곁에서 봤다 — 그 사람이 손보던 것을 안다
        else
        {
            float p = Society.BelieveBase + 0.4f * (soc.TrustIn(l, from) - 0.5f) - 0.25f * (from < soc.LiarMark.Length ? soc.LiarMark[from] : 0);
            if (lie.Scape >= 0 && lie.ScapeRobot == 0) p -= 0.3f * (soc.TrustIn(l, lie.Scape) - 0.5f);
            p = Math.Clamp(p, 0.1f, 0.9f);
            how = SocRnd(sim, "믿음", l, lie.Cause) < p ? 1 : 0;
            if (how == 0 && SocRnd(sim, "기록 묻기", l, lie.Cause) < 0.15f + 0.25f * cl.Consc + 0.2f * cl.Open) due = 30 + (int)(150 * SocRnd(sim, "언제 묻나", l, lie.Cause));   // 의심한다 — 꼼꼼하거나 궁금하면 묻는다
        }
        Act(sim, new SocialAct { Kind = SocialActKind.HearLie, Actor = l, Target = from, Other = how, Arg = lie.Device, Value = due, Room = RoomOf(cl.Cell), Cell = cl.Cell, Cause = lie.Cause });
        if (due > 0) _logChecks.Add(new LogCheck { Due = sim.Tick + Time.Minutes(due), Who = l, Lie = lie.Cause });
    }

    /// <summary>대화에서 거짓말이 전해졌다: 따지는 말을 들은 이가 그 거짓말의 탓 들은 이면 · 험담이 퍼졌고 그 험담이 거짓말이면 (TalkStep이 부른다).</summary>
    private void HearInTalk(Sim sim, Topic t, TalkEnd end, int a, int b, int cause)
    {
        var soc = _soc!;
        int li = cause != 0 ? soc.LieOf(cause) : -1;
        if (li < 0) return;
        var lie = soc.Lies[li];
        if (t == Topic.Blame && b == lie.Scape || t == Topic.Gossip && end == TalkEnd.Spread) HearLie(sim, b, a, lie);
    }

    /// <summary>기록을 물을 때가 된 이가 주컴퓨터에 묻는다 — 깨어 있으면 (자고 있으면 30분 뒤) · 이미 들통났거나 숨졌으면 그만둔다.</summary>
    private void AskLogs(Sim sim)
    {
        var soc = _soc!;
        for (int i = _logChecks.Count - 1; i >= 0; i--)
        {
            var k = _logChecks[i];
            if (k.Due > sim.Tick) continue;
            int li = soc.LieOf(k.Lie);
            if (li < 0 || soc.Lies[li].Exposed != 0 || !PersonAlive(k.Who)) { _logChecks.RemoveAt(i); continue; }
            if (!PersonAwake(k.Who)) { k.Due = sim.Tick + Time.Minutes(30); _logChecks[i] = k; continue; }
            _logChecks.RemoveAt(i);
            var lie = soc.Lies[li];
            string dn = _net != null && lie.Device >= 0 && lie.Device < _net.Devices.Count ? _net.Devices[lie.Device].Name : "장치";
            int cause = sim.Causes.Add(CauseKind.Person, $"{Ko.I(NameOf(k.Who))} 주컴퓨터에 {dn} 정비 기록을 물었다", sim.Tick, Agents[k.Who].C.Cell, lie.Cause, 0, k.Who);
            float hours = (float)Math.Ceiling((sim.Tick - lie.Fail) / (double)Time.TicksPerHour) + 24 * 3;   // 망가지기 사흘 전부터
            Act(sim, new SocialAct { Kind = SocialActKind.AskLog, Actor = k.Who, Target = -1, Other = -1, Arg = lie.Device, Value = hours, Room = RoomOf(Agents[k.Who].C.Cell), Cell = Agents[k.Who].C.Cell, Cause = cause });
        }
    }

    private void SaveSecrets(StateWriter w) { w.I64(_secretCursor); w.List(_logChecks); }
    private void LoadSecrets(StateReader r) { _secretCursor = r.I64(); _logChecks.Clear(); _logChecks.AddRange(r.List<LogCheck>()); }
}
