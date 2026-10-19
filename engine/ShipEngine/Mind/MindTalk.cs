namespace ShipEngine;

/// <summary>
/// 판단 이관 (나) 대화 — 말을 걸지 · 무슨 이야기를 누가 꺼낼지 · 듣는 이가 어떻게 받을지 · 별명을 부를지는 사람의 판단이다 (docs/ENGINE.md §3 9).
/// 1분마다 곁(같은 방 6칸 안)에서 함께 쉬는 깬 둘을 보고: 외향인 만큼 · 잘 아는 만큼 말을 건다 (밤의 규칙이 있으면 자는 이 곁에서 성실한 이는 말을 삼킨다) ·
/// 기억 · 관계 · 성격에서 이야기를 고르고 (따지기 · 슬픔 · 험담 · 요즘 어때 · 고백 · 화해 · 함께 겪은 일 · 걱정 · 잡담) · 듣는 이가 받는다 (사과 · 맞섬 · 감쌈 · 퍼뜨림 · 받아줌 · 거절 · 털어놓음 · 위로 · 어색) ·
/// 행동 '대화'로 낸다. 사회(월드 엔진)는 그 결과(정 · 원한 · 믿음 · 기억 · 소문 · 한 마디 · 말소리)를 계산한다.
/// </summary>
public sealed partial class Mind
{
    private readonly struct TalkOption
    {
        public readonly Topic T; public readonly float W; public readonly int About, Cause, Cell, Speaker;
        public TalkOption(Topic t, float w, int about, int cause, int cell, int speaker) { T = t; W = w; About = about; Cause = cause; Cell = cell; Speaker = speaker; }
    }

    /// <summary>사회와 같은 갈래의 난수 (틱 · 사람 · 열쇠) — 같은 때 같은 둘이면 같은 값.</summary>
    private static float SocRnd(Sim sim, string key, long a, long b = 0) => (float)Rng.Unit(sim.Seed, Rng.Key(key), sim.Tick * 131 + a, b);

    /// <summary>1분마다: 곁에서 함께 쉬는 깬 둘이 말을 나눌지 · 무슨 이야기를 · 어떻게 받을지.</summary>
    private void TalkStep(Sim sim)
    {
        var soc = _soc;
        if (soc == null || sim.Tick % Society.TogetherEvery != 0) return;
        int n = Math.Min(Agents.Count, soc.N);
        for (int i = 0; i < n; i++)
        {
            var a = Agents[i];
            if (!a.IsPerson || a.C.Up == 0 || !Society.Leisure(a.C.Goal) || !PersonAwake(i)) continue;
            for (int j = i + 1; j < n; j++)
            {
                var b = Agents[j];
                if (!b.IsPerson || b.C.Up == 0 || !Society.Leisure(b.C.Goal) || !PersonAwake(j)) continue;
                if (RoomOf(a.C.Cell) != RoomOf(b.C.Cell) || Dist(a.C.Cell, b.C.Cell) > Society.TalkReach) continue;
                if (sim.Tick - soc.LastTalk[i * soc.N + j] < Society.TalkRest) continue;
                // 말을 건다: 분당 약 3% × 외향 × 아는 정도 (어울리러 온 이는 더)
                float p = 0.03f * (0.5f + Math.Max(a.C.Extra, b.C.Extra)) * (a.C.Goal == GoalKind.Socialize || b.C.Goal == GoalKind.Socialize ? 1.6f : 1) * (0.4f + soc.KnownOf(i, j));
                if (SocRnd(sim, "말 걸기", i, j) >= p) continue;
                if (KeepsQuiet(sim, i, j)) continue;   // ⑨ 조용한 밤의 규칙 — 말을 삼킨다
                var o = PickTopic(sim, i, j);
                int sp = o.Speaker, li = sp == i ? j : i;
                var end = Reply(sim, o, sp, li);
                Act(sim, new SocialAct { Kind = SocialActKind.Talk, Actor = sp, Target = li, Other = o.About, Room = RoomOf(o.Cell), Cell = o.Cell, Cause = o.Cause, Topic = o.T, End = end });
                HearInTalk(sim, o.T, end, sp, li, o.Cause);   // (라) 거짓말이 말로 옮겨 갔다 — 들은 이가 받는다
                NickInTalk(sim, i, j);   // ⑨ 이야기하다 별명이 붙는다
            }
        }
    }

    /// <summary>이야기를 고른다: 기억 · 관계 · 성격에서 꺼낼 거리와 그 무게 · 꺼낼 사람.</summary>
    private TalkOption PickTopic(Sim sim, int i, int j)
    {
        var soc = _soc!;
        ref var ci = ref Agents[i].C; ref var cj = ref Agents[j].C;
        ref AgentCore C(int x) => ref Agents[x].C;
        var opts = new List<TalkOption>();
        // 두 무리의 지도자가 만났다: 벌어진 사이가 가장 먼저 꺼낼 이야기다 (다정할수록 크게)
        if (soc.Rival(i, j) && soc.LeaderOf(i) == i && soc.LeaderOf(j) == j)
        {
            int sp0 = ci.Agree >= cj.Agree ? i : j;
            opts.Add(new(Topic.Peace, 4 * (0.4f + C(sp0).Agree), sp0 == i ? j : i, 0, C(sp0).Cell, sp0));
        }
        foreach (var (a, b) in new[] { (i, j), (j, i) })
        {
            // 원망하는 사람에게 따진다 (지친 이는 묵혀 둔 일을 더 쉽게 꺼낸다 — ⑧ 고립의 피로)
            if (soc.GrudgeOf(a, b) > 0.25f - 0.1f * C(a).Weary) opts.Add(new(Topic.Blame, 3 * soc.GrudgeOf(a, b) * (0.6f + C(a).Neuro * 0.6f) * (1 + C(a).Weary), b, soc.MemoryCause(a, Topic.Blame, b), soc.MemoryCell(a, Topic.Blame, b), a));
            // 슬픔을 꺼낸다
            if (soc.Grief[a] > 0.25f) { int dead = soc.StrongestAbout(sim, a, Topic.Death); if (dead >= 0) opts.Add(new(Topic.Death, 2 * soc.Grief[a], dead, soc.MemoryCause(a, Topic.Death, dead), soc.MemoryCell(a, Topic.Death, dead), a)); }
            // 그 자리에 없는 이의 험담 (덜 다정할수록)
            int c3 = -1; float g3 = 0.3f;
            for (int x = 0; x < soc.N; x++) if (x != a && x != b && soc.GrudgeOf(a, x) > g3 && RoomOf(C(x).Cell) != RoomOf(C(a).Cell)) { g3 = soc.GrudgeOf(a, x); c3 = x; }
            if (c3 >= 0) opts.Add(new(Topic.Gossip, 1.5f * g3 * (1.2f - C(a).Agree), c3, soc.MemoryCause(a, Topic.Blame, c3), soc.MemoryCell(a, Topic.Blame, c3), a));
            // ⑧ 마음이 힘든 친구에게 '요즘 어때' — 아끼는 이(정 0.5 넘게)가 먼저 묻는다 (다정할수록 · 가까운 이의 처진 얼굴은 먼저 눈에 띈다)
            float low = Math.Max(C(a).Depress, Math.Max(C(a).Trauma, C(a).Anxiety));
            if (low > 0.3f && soc.BondOf(b, a) > 0.5f) opts.Add(new(Topic.Troubled, 5 * low * (0.5f + C(b).Agree), a, C(a).ShockCause, C(b).Cell, b));
        }
        // 마음을 고백한다: 짝이 없는 둘 · 끌림이 깊고 정도 쌓였다 (끌림이 클수록 · 외향일수록 먼저 꺼낸다 · 남몰래 품은 마음이면 그 원인에 잇는다)
        foreach (var (a, b) in new[] { (i, j), (j, i) })
            if (soc.Partner[a] < 0 && soc.Partner[b] < 0 && soc.AttractOf(a, b) >= Society.ConfessAt && soc.BondOf(a, b) >= 0.45f)
                opts.Add(new(Topic.Heart, 6 * (soc.AttractOf(a, b) - 0.5f) * (0.5f + C(a).Extra), b, soc.MemoryCause(a, Topic.Heart, b), C(a).Cell, a));
        // 함께 겪은 일 (같은 원인을 둘 다 기억한다 — 가장 큰 것) · 무서웠던 일일수록 (그 자리를 꺼릴수록) 더 꺼낸다 (감정 나눔 연구)
        var mems = soc.Memories;
        int best = -1; float bw = 0.15f;
        for (int k = 0; k < mems.Count; k++)
        {
            var m = mems[k];
            if (m.Owner != i || m.Topic is Topic.Blame or Topic.Gossip or Topic.Small) continue;
            int mr = RoomOf(m.Cell);
            float w = soc.MemoryWeight(m, sim.Tick) * (1 + Dread(i, mr) + Dread(j, mr));
            if (w <= bw) continue;
            for (int q = 0; q < mems.Count; q++) if (mems[q].Owner == j && mems[q].Cause == m.Cause && mems[q].Topic == m.Topic) { best = k; bw = w; break; }
        }
        if (best >= 0) { var m = mems[best]; opts.Add(new(m.Topic, 2.2f * bw, m.About, m.Cause, m.Cell, ci.Extra >= cj.Extra ? i : j)); }
        // 식량 · 물이 열흘을 못 버틴다 · 배급 중이면 먹을 것 이야기가 잦다 (미네소타 굶주림 실험)
        double food = sim.Book.DaysLeft((int)Res.Food, sim.Tick), water = sim.Book.DaysLeft((int)Res.Water, sim.Tick);
        bool rationed = RationFood > 0 || RationWater > 0;
        if (food < 10 || water < 10 || rationed) opts.Add(new(Topic.Shortage, rationed ? 2f + ci.Hunger + cj.Hunger : 1.2f, -1, 0, ci.Cell, ci.Neuro >= cj.Neuro ? i : j));
        int talker = ci.Extra >= cj.Extra ? i : j;
        opts.Add(new(Topic.Small, 1, -1, 0, ci.Cell, talker));
        if (ci.Goal == GoalKind.Idle || cj.Goal == GoalKind.Idle) opts.Add(new(Topic.Work, 0.5f, -1, 0, ci.Cell, talker));

        float sum = opts.Sum(o => o.W), r = SocRnd(sim, "주제", i, j) * sum;
        var pick = opts[^1];
        foreach (var o in opts) { r -= o.W; if (r <= 0) { pick = o; break; } }
        return pick;
    }

    /// <summary>듣는 이(b)가 받는다 — 그 사람의 성격 · 관계 · 아는 것으로 (말을 꺼낸 이 a와 함께 흘러가는 잡담은 둘의 맞음으로).</summary>
    private TalkEnd Reply(Sim sim, in TalkOption o, int a, int b)
    {
        var soc = _soc!;
        ref var ca = ref Agents[a].C; ref var cb = ref Agents[b].C;
        float r = SocRnd(sim, "대화 끝", a, b);
        switch (o.T)
        {
            case Topic.Blame:
            {
                // 사과하나: 다정하고 · 덜 불안하고 · a를 아끼고 · 실제로 제 탓일수록 (그는 제가 한 일을 안다) · 지친 이는 덜 숙인다
                bool guilty = o.Cause != 0 && soc.Culprit(sim, o.Cause, -1) == b;
                float p = cb.Agree * (1 - cb.Neuro * 0.5f) * (0.7f + Math.Max(0, soc.BondOf(b, a)) + soc.DebtOf(b, a)) * (guilty ? 1.2f : 0.5f) * (1 - 0.4f * cb.Weary);
                return r < p ? TalkEnd.Apology : TalkEnd.Argue;
            }
            case Topic.Gossip:
            {
                int x = o.About;
                // 감싸기: 아끼는 사람이거나 같은 무리면 (무리는 덜 다정해도 제 사람을 감싼다)
                return soc.BondOf(b, x) > 0.5f && cb.Agree > 0.4f || soc.SameBand(b, x) && !soc.SameBand(a, x) && cb.Agree > 0.2f ? TalkEnd.Defend : TalkEnd.Spread;
            }
            case Topic.Heart:
                // 받아 주나: 듣는 이도 끌리고 · 정이 있고 · 앙금이 없다 (짝이 이미 있으면 아니다)
                return soc.Partner[b] < 0 && soc.AttractOf(b, a) >= Society.AcceptAt && soc.BondOf(b, a) >= 0.35f && soc.GrudgeOf(b, a) < 0.2f ? TalkEnd.Accept : TalkEnd.Reject;
            case Topic.Peace:
            {
                // 받아들이나: 둘 다 다정하고 · 듣는 이의 원한이 덜할수록
                float p = (ca.Agree + cb.Agree) * 0.5f * (1.2f - soc.GrudgeOf(b, a)) * (1 - cb.Neuro * 0.3f);
                return r < p ? TalkEnd.Accept : TalkEnd.Argue;
            }
            case Topic.Troubled:
                // a가 묻고 b가 털어놓나: b가 a를 아낄수록 · 외향일수록
                return r < 0.45f + 0.5f * Math.Max(0, soc.BondOf(b, a)) + 0.2f * cb.Extra ? TalkEnd.Comfort : TalkEnd.Awkward;
            case Topic.Death:
                return r < 0.4f + cb.Agree * 0.6f ? TalkEnd.Comfort : TalkEnd.Awkward;
            case Topic.Fire or Topic.Breach or Topic.Rescue or Topic.Hurt or Topic.Fight:
                // 함께 겪은 일: 그때 자기를 구한 · 위기를 넘긴 상대에게 고맙다 · 아니면 맞장구
                if (o.T == Topic.Hurt) return TalkEnd.Comfort;
                return o.About == b && o.T is Topic.Fire or Topic.Breach or Topic.Rescue ? TalkEnd.Praise : TalkEnd.Agree;
            case Topic.Shortage:
                return TalkEnd.Worry;
            default:
            {
                // 잡담 · 일: 성격이 맞으면 웃고 · 거친 둘은 부딪친다 (⑧ 고립의 피로 — 같은 얼굴 · 같은 말에 지친 둘은 더 부딪친다)
                float worn = (ca.Weary + cb.Weary) * 0.5f;
                float fit = 1 - Math.Abs(ca.Extra - cb.Extra) * 0.5f + (ca.Agree + cb.Agree) * 0.25f + Math.Max(0, soc.BondOf(a, b)) * 0.5f - 0.6f * worn;
                if (fit > r * 1.4f) return r < 0.25f && o.T == Topic.Small ? TalkEnd.Laugh : TalkEnd.Agree;
                return ca.Agree + cb.Agree < 0.7f + 0.6f * worn ? TalkEnd.Friction : TalkEnd.Awkward;
            }
        }
    }

    /// <summary>조용한 밤: 22~6시에 자는 이가 곁(같은 방 6칸 안)에 있으면 — 성실한 이일수록 말을 걸지 않는다 (덜 성실한 이는 어긴다) · 삼키면 행동 '말을 삼켰다'.</summary>
    private bool KeepsQuiet(Sim sim, int i, int j)
    {
        var soc = _soc!;
        if (!soc.QuietRule) return false;
        double hour = Time.HourOfDay(sim.Tick);
        if (hour < 22 && hour >= 6) return false;
        bool sleeper = false;
        int room = RoomOf(Agents[i].C.Cell);
        for (int k = 0; k < Agents.Count && !sleeper; k++)
            if (k != i && k != j && PersonAlive(k) && _life![Agents[k].C.Body].Asleep != 0 && RoomOf(Agents[k].C.Cell) == room && Dist(Agents[k].C.Cell, Agents[i].C.Cell) <= Society.TalkReach) sleeper = true;
        if (!sleeper) return false;
        if (SocRnd(sim, "밤의 규칙", i, j) >= 0.2f + 0.8f * Math.Min(Agents[i].C.Consc, Agents[j].C.Consc)) return false;   // 어긴다
        Act(sim, new SocialAct { Kind = SocialActKind.KeepQuiet, Actor = i, Target = j, Other = -1, Room = room, Cell = Agents[i].C.Cell });
        return true;
    }

    /// <summary>
    /// 이야기를 나눈 둘 — 한쪽이 별명이 붙을 만큼 일을 쌓았고 · 다른 쪽이 그 일을 알면 그렇게 부르기 시작한다 (부른 이의 판단):
    /// 기리는 별명은 그를 아끼거나 (정 0.2 넘게) 외향인 이 (0.5 넘게)가 · 놀리는 별명은 덜 다정한 이 (친화 0.6 아래)가 꺼낸다.
    /// </summary>
    private void NickInTalk(Sim sim, int i, int j)
    {
        var soc = _soc!;
        foreach (var (x, by) in new[] { (i, j), (j, i) })
        {
            if (x >= soc.Nick.Length || soc.Nick[x] != 0 || !PersonAlive(x) || !PersonAwake(by)) continue;
            for (int kind = 1; kind <= 5; kind++)
            {
                if (soc.DeedCount(x, (Nickname)kind) < Society.DeedsFor[kind] || !KnowsDeeds(by, x, (Nickname)kind)) continue;
                ref var cb = ref Agents[by].C;
                bool tease = kind == (int)Nickname.Forgetful;
                if (tease ? cb.Agree >= 0.6f : soc.BondOf(by, x) <= 0.2f && cb.Extra <= 0.5f) continue;
                Act(sim, new SocialAct { Kind = SocialActKind.Nickname, Actor = by, Target = x, Other = -1, Arg = kind, Room = RoomOf(Agents[by].C.Cell), Cell = Agents[by].C.Cell });
                return;
            }
        }
    }

    /// <summary>그 사람이 x의 그 일을 아나: 함께 겪었거나 들어서 기억한다 (x에 대한 기억) · 함께 지내 잘 안다 (아는 정도 0.3 넘게) ·
    /// 고친 일 · 켜 둔 조리대는 배 사람이 다 안다 (일감판 · 주컴퓨터가 끈 조리대).</summary>
    private bool KnowsDeeds(int by, int x, Nickname kind)
    {
        var soc = _soc!;
        if (kind is Nickname.Handy or Nickname.Forgetful || soc.KnownOf(by, x) > 0.3f) return true;
        foreach (var m in soc.Memories) if (m.Owner == by && m.About == x && m.Topic is Topic.Rescue or Topic.Fire or Topic.Breach) return true;
        return false;
    }
}
