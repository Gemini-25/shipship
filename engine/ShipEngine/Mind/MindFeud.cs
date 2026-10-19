namespace ShipEngine;

/// <summary>
/// 판단 이관 (다) 다툼 — 덤비기 · 끝장 보기 · 주먹 · 맞받아치기 · 말리기 · 편들기 · 몰래 방해 · 헤어지기는 사람(말리는 로봇은 로봇)의 판단이다 (docs/ENGINE.md §3 9).
/// 다툼(따지다 맞섬 · 화해를 뿌리침)을 곁에서 본 이는 다음 틱에 편을 들거나 말리고 · 다툰 둘은 그다음 틱에 (말린 뒤의 화로) 덤빌지 정한다 ·
/// 원한이 깊은 둘이 마주치면 화가 난 쪽이 덤빌 수 있다 (1분마다) · 몸싸움 중에는 1분마다 때린 이가 계속 칠지 (끝장을 보려는 이는 쓰러진 뒤에도 두 번 더) ·
/// 맞는 이가 맞받아칠지 · 같은 방의 로봇(먼저) · 사람이 말릴지 · 30분마다 깊이 원망하는 거친 이가 아무도 없을 때 몰래 손을 댈지 · 짝이 헤어질지.
/// 사회(월드 엔진)는 그 행동의 결과(관계 · 소문 · 다침은 ⑦ 생명 · 장치의 마모는 ⑥ 망)만 계산한다.
/// </summary>
public sealed partial class Mind
{
    private readonly HashSet<int> _attackNow = new();

    /// <summary>틱마다: 방금 본 다툼에 끼어들지 · 방금 다툰 둘이 덤빌지 / 1분마다: 마주친 원수 · 몸싸움 · 짝 / 30분마다: 몰래 방해.</summary>
    private void FeudStep(Sim sim)
    {
        var soc = _soc;
        if (soc == null) return;
        _attackNow.Clear();
        // 지난 틱의 다툼을 곁에서 봤다 → 편들기 · 말리기 / 지지난 틱에 다툰 둘 → 덤빌지 (곁사람이 말린 뒤의 화로)
        var argued = Acts.Where(x => x.Kind == SocialActKind.Talk && x.End == TalkEnd.Argue && x.Topic is Topic.Blame or Topic.Peace && x.Tick < sim.Tick).ToList();
        foreach (var act in argued)
        {
            int x = act.Topic == Topic.Peace ? act.Target : act.Actor, y = act.Topic == Topic.Peace ? act.Actor : act.Target;   // 화해를 뿌리친 이 · 따진 이가 앞
            int line = ArgueLine(soc, act.Tick, x, y);
            if (line == 0) continue;   // 말이 끊겼다 (그 틱에 쓰러졌다)
            if (act.Tick == sim.Tick - 1) Onlookers(sim, x, y, line);
            else if (act.Tick == sim.Tick - 2)
            {
                if (act.Topic == Topic.Peace) Escalate(sim, x, y, 0.5f, line);
                else { Escalate(sim, x, y, 1, line); Escalate(sim, y, x, 0.7f, line); }
            }
        }
        if (sim.Tick % Society.TogetherEvery != 0) return;
        Encounters(sim);
        BrawlChoices(sim);
        Breakups(sim);
        if (sim.Tick % (Society.TogetherEvery * 30) == 0) Mischief(sim);
    }

    /// <summary>그 틱에 x가 y에게 한 다툼의 말 (원인 마디 — 없으면 0).</summary>
    private static int ArgueLine(Society soc, long tick, int x, int y)
    {
        for (int k = soc.Sayings.Count - 1; k >= 0; k--)
        {
            var q = soc.Sayings[k];
            if (q.Tick < tick) break;
            if (q.Tick == tick && q.Speaker == x && q.Listener == y && q.End == TalkEnd.Argue) return q.Line;
        }
        return 0;
    }

    /// <summary>
    /// 다툼을 곁에서 본 이들 (같은 방이거나 6칸 안 · 깨어 있는 사람 · 많아야 둘이 입을 연다 — 작은 승무원 사회의 다툼은 곁사람이 끼어들며 커지거나 가라앉는다):
    /// 한쪽을 훨씬 아끼면 (정 · 같은 무리 − 원망) 편을 든다 — 덜 다정할수록 쉽게 / 둘 다 미워하지 않는 다정하고 용감한 이는 말린다. 나머지는 모른 척한다.
    /// </summary>
    private void Onlookers(Sim sim, int a, int b, int cause)
    {
        var soc = _soc!;
        int spoke = 0, n = soc.N;
        foreach (int i in soc.Near(Agents[a].C.Cell, a, 6))
        {
            if (i == b || !Agents[i].IsPerson || spoke >= 2) continue;
            ref var ci = ref Agents[i].C;
            float fa = soc.BondOf(i, a) - soc.GrudgeOf(i, a) + (soc.SameBand(i, a) ? 0.3f : 0), fb = soc.BondOf(i, b) - soc.GrudgeOf(i, b) + (soc.SameBand(i, b) ? 0.3f : 0);
            int side = fa - fb > 0.3f && fa > 0.3f ? a : fb - fa > 0.3f && fb > 0.3f ? b : -1;
            float r = SocRnd(sim, "곁에서 본 다툼", i, a * n + b);
            if (side >= 0 && r < 0.4f + 0.4f * (1 - ci.Agree))
            {
                int foe = side == a ? b : a;
                Act(sim, new SocialAct { Kind = SocialActKind.TakeSide, Actor = i, Target = foe, Other = side, Arg = side == a ? 1 : 0, Room = RoomOf(ci.Cell), Cell = ci.Cell, Cause = cause });
                spoke++;
            }
            else if (side < 0 && ci.Agree > 0.55f && soc.GrudgeOf(i, a) < 0.3f && soc.GrudgeOf(i, b) < 0.3f && r < ci.Agree * (0.5f + 0.5f * ci.Brave))
            {
                Act(sim, new SocialAct { Kind = SocialActKind.Mediate, Actor = i, Target = a, Other = b, Room = RoomOf(ci.Cell), Cell = ci.Cell, Cause = cause });
                spoke++;
            }
        }
    }

    /// <summary>x가 y에게 덤비나: 원한 · 거친 성격 · 불안 · 긴장 · 화가 모두 겹쳐야 (드물게) · 따르는 이가 보는 앞에서는 참는다. 끝장을 보려는 것은 더 드물다.</summary>
    private void Escalate(Sim sim, int x, int y, float scale, int cause)
    {
        var soc = _soc!;
        if (!PersonAwake(x) || !PersonAwake(y) || _attackNow.Contains(x) || _attackNow.Contains(y) || soc.InBrawl(x, out _, out _) || soc.InBrawl(y, out _, out _)) return;
        if (RoomOf(Agents[x].C.Cell) != RoomOf(Agents[y].C.Cell) || Dist(Agents[x].C.Cell, Agents[y].C.Cell) > 4) return;
        ref var cx = ref Agents[x].C;
        float g = soc.GrudgeOf(x, y);
        if (g < 0.5f) return;
        float pressure = (g - 0.5f) * 2 * (1 - cx.Agree) * (1 - cx.Agree) * (0.3f + cx.Neuro) * (0.4f + cx.Stress) * cx.Anger * 1.5f * scale * (1 + cx.Weary);   // 지친 이는 덜 참는다
        if (soc.LeaderWatching(x, y)) pressure *= 0.25f;
        if (SocRnd(sim, "덤빈다", x, y) >= pressure) return;
        bool lethal = g > 0.9f && cx.Agree < 0.25f && cx.Neuro > 0.6f && (cx.Stress > 0.6f || soc.DeathBlame(x, y));
        int c = sim.Causes.Add(CauseKind.Person, lethal ? $"{Ko.I(NameOf(x))} {Ko.Eul(NameOf(y))} 죽일 듯이 덤볐다" : $"{Ko.I(NameOf(x))} {NameOf(y)}에게 주먹을 휘둘렀다",
            sim.Tick, cx.Cell, cause != 0 ? cause : soc.MemoryCause(x, Topic.Blame, y), 0, x);
        _attackNow.Add(x); _attackNow.Add(y);
        Act(sim, new SocialAct { Kind = SocialActKind.Attack, Actor = x, Target = y, Other = -1, Arg = lethal ? 1 : 0, Room = RoomOf(cx.Cell), Cell = cx.Cell, Cause = c });
    }

    /// <summary>원한이 깊은 둘이 마주쳤다 (함께 쉬던 곳 · 6칸 안) — 화가 난 쪽이 먼저 덤빌 수 있다.</summary>
    private void Encounters(Sim sim)
    {
        var soc = _soc!;
        int n = Math.Min(Agents.Count, soc.N);
        for (int i = 0; i < n; i++)
        {
            var a = Agents[i];
            if (!a.IsPerson || a.C.Up == 0 || !Society.Leisure(a.C.Goal) || a.C.Anger <= 0.5f) continue;
            for (int j = 0; j < n; j++)
            {
                if (j == i || soc.GrudgeOf(i, j) <= 0.5f) continue;
                var b = Agents[j];
                if (!b.IsPerson || b.C.Up == 0 || !Society.Leisure(b.C.Goal)) continue;
                if (RoomOf(a.C.Cell) != RoomOf(b.C.Cell) || Dist(a.C.Cell, b.C.Cell) > Society.TalkReach) continue;
                Escalate(sim, i, j, 0.2f, 0);
            }
        }
    }

    /// <summary>
    /// 몸싸움 중 1분마다: 때린 이 — 곁에 있고 · 화가 남았으면 (덤빌 때 정한 만큼) 또 친다 · 맞는 이가 쓰러지면 그만둔다 (끝장을 보려는 이는 쓰러진 뒤에도 두 번 더) /
    /// 말리는 이 — 같은 방의 로봇이 먼저 · 사람은 이끄는 이(겁이 좀 많아도) · 맞는 이의 무리(제 편을 감싸러) · 다정하고 용감한 이 / 맞는 이 — 거칠고 화났고 용감하면 맞받아친다.
    /// </summary>
    private void BrawlChoices(Sim sim)
    {
        var soc = _soc!;
        foreach (var br in soc.Brawls)
        {
            int a = br.A, b = br.B;
            if (br.Start == sim.Tick || !PersonAwake(a)) continue;
            ref var vb = ref _life![Agents[b].C.Body];
            ref var ca = ref Agents[a].C; ref var cb = ref Agents[b].C;
            if (vb.Alive == 0 || RoomOf(ca.Cell) != RoomOf(cb.Cell) || Dist(ca.Cell, cb.Cell) > 3) continue;   // 떨어졌다 — 칠 수 없다
            bool down = vb.Conscious == 0;
            bool more = sim.Tick < br.Until && (!down || br.Lethal != 0 && (br.DownAt == 0 || sim.Tick - br.DownAt < Time.Minutes(2)));
            if (!more) continue;   // 손을 거둔다
            // 말리는 이
            int room = RoomOf(ca.Cell), stopper = -1;
            foreach (var ag in Agents)
                if (!ag.IsPerson && ag.C.Up != 0 && ag.C.Kind == AgentKind.Robot && RoomOf(ag.C.Cell) == room) { stopper = ag.Id; break; }
            float brave = br.Lethal != 0 ? 0.5f : 0.3f;
            if (stopper < 0)
                for (int i = 0; i < soc.N; i++)
                {
                    if (i == a || i == b || !PersonAwake(i) || RoomOf(Agents[i].C.Cell) != room || soc.GrudgeOf(i, b) >= 0.5f) continue;
                    ref var ci = ref Agents[i].C;
                    bool lead = soc.Leads(i, a) || soc.Leads(i, b);
                    if (lead && ci.Brave > brave * 0.4f || soc.SameBand(i, b) && !soc.SameBand(i, a) && ci.Brave > brave * 0.7f || ci.Agree > 0.45f && ci.Brave > brave) { stopper = i; break; }
                }
            if (stopper >= 0)
            {
                Act(sim, new SocialAct { Kind = SocialActKind.StopFight, Actor = stopper, Target = a, Other = b, Room = room, Cell = Agents[stopper].C.Cell, Cause = br.Cause });
                continue;
            }
            Act(sim, new SocialAct { Kind = SocialActKind.Strike, Actor = a, Target = b, Other = -1, Arg = br.Lethal, Room = room, Cell = ca.Cell, Cause = br.Cause });
            if (!down && cb.Agree < 0.4f && cb.Anger > 0.4f && cb.Brave > 0.4f)
                Act(sim, new SocialAct { Kind = SocialActKind.HitBack, Actor = b, Target = a, Other = -1, Room = room, Cell = cb.Cell, Cause = br.Cause });
        }
    }

    /// <summary>짝이 헤어진다: 한쪽의 원망이 0.45를 넘거나 한쪽의 정이 0.15 아래로 식었다 (둘 다 깨어 있을 때 — 말로 끝낸다).</summary>
    private void Breakups(Sim sim)
    {
        var soc = _soc!;
        for (int i = 0; i < soc.N; i++)
        {
            int j = soc.PartnerOf(i);
            if (j < 0 || j < i || j >= Agents.Count) continue;
            if (!PersonAlive(i) || !PersonAlive(j) || Agents[i].C.Up == 0 || Agents[j].C.Up == 0) continue;
            if (Math.Max(soc.GrudgeOf(i, j), soc.GrudgeOf(j, i)) > 0.45f || Math.Min(soc.BondOf(i, j), soc.BondOf(j, i)) < 0.15f)
                Act(sim, new SocialAct { Kind = SocialActKind.BreakUp, Actor = i, Target = j, Other = -1, Room = RoomOf(Agents[i].C.Cell), Cell = Agents[i].C.Cell });
        }
    }

    /// <summary>30분마다: 몰래 방해 — 깊이 원망하고 · 성실하지 않고 · 거친 사람이, 아무도 없을 때 상대가 일하는 장치에 몰래 손을 댄다 (나중에 고장 난다 · 아무도 모른다).</summary>
    private void Mischief(Sim sim)
    {
        var soc = _soc!;
        if (_net == null) return;
        int n = Math.Min(Agents.Count, soc.N);
        for (int x = 0; x < n; x++)
        {
            if (!PersonAwake(x)) continue;
            ref var cx = ref Agents[x].C;
            if (cx.Consc > 0.35f || cx.Agree > 0.3f) continue;
            for (int y = 0; y < n; y++)
            {
                if (y == x || soc.GrudgeOf(x, y) < 0.7f || !PersonAlive(y) || Agents[y].C.Up == 0 || Agents[y].C.Station < 0) continue;
                int fu = Agents[y].C.Station;
                int dev = _net.Devices.FindIndex(d => d.Furniture == fu);
                if (dev < 0 || RoomOf(cx.Cell) != L.Furniture[fu].Room) continue;
                bool alone = true;
                for (int i = 0; i < n; i++) if (i != x && PersonAwake(i) && RoomOf(Agents[i].C.Cell) == L.Furniture[fu].Room) { alone = false; break; }
                if (!alone || SocRnd(sim, "몰래", x, y) > (soc.GrudgeOf(x, y) - 0.7f) * 1.5f) continue;
                int c = sim.Causes.Add(CauseKind.Person, $"{Ko.I(NameOf(x))} {NameOf(y)}의 {_net.Devices[dev].Name}에 몰래 손을 댔다", sim.Tick, cx.Cell, soc.MemoryCause(x, Topic.Blame, y), 0, x);
                Act(sim, new SocialAct { Kind = SocialActKind.Sabotage, Actor = x, Target = y, Other = -1, Arg = dev, Room = RoomOf(cx.Cell), Cell = cx.Cell, Cause = c });
                break;
            }
        }
    }
}
