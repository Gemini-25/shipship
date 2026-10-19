namespace ShipEngine;

/// <summary>
/// 판단 이관 (가) 모임 — 부르고 · 가고 · 말하고 · 미루는 것은 사람의 판단이다 (docs/ENGINE.md §3 9 '월드 엔진과 판단 엔진을 나눈다').
/// 사회(⑨ 월드 엔진)는 안건이 될 사실(한 사람에게 모인 원한 · 굶은 날 · 매뉴얼 안건 · 되풀이된 마찰 · 숨진 이 · 넘긴 위기)을 세고 ·
/// 모임의 차례를 돌리고 · 표를 세어 결과를 계산할 뿐이다. 마음은 한 시간마다 안건을 보고 · 그 안건으로 부를 사람(가장 가까웠던 이 · 고발한 이 · 대표 …)이
/// 제 판단으로 때가 되었다고 여기면 부른다 (행동 '부르기') · 부름을 들은 이는 저마다 갈지 정하고 · 차례가 온 이는 제 생각을 말하고 (행동 '말하기' — 의견 · 표) ·
/// 부른 뒤 일이 나면 부른 이(자리에 없으면 모이던 이)가 보고 미룬다 (행동 '미루기').
/// </summary>
public sealed partial class Mind
{
    private long _meetSeen = long.MinValue;   // 갈지 정한 모임 (부른 때)
    private bool _callNow;                    // 시험용: 하루 한 번 · 여섯 시간을 기다리지 않는다

    /// <summary>열린 모임의 방 · 칸 · 원인 (없으면 −1 · −1 · 0) — 사회가 연 모임을 읽는다.</summary>
    public int GatherRoom => _soc != null && _soc.Meeting.Kind != CouncilKind.None ? _soc.Meeting.Room : -1;
    public int GatherCell => _soc != null && _soc.Meeting.Kind != CouncilKind.None ? _soc.Meeting.Cell : -1;
    public int GatherCause => _soc != null && _soc.Meeting.Kind != CouncilKind.None ? _soc.Meeting.Cause : 0;
    /// <summary>모이러 가는 마음이 남는 때 (부른 뒤 55분).</summary>
    public long GatherUntil => _soc != null && _soc.Meeting.Kind != CouncilKind.None ? _soc.Meeting.Called + Time.Minutes(Society.CouncilGatherMin + 40) : 0;

    private bool PersonAlive(int i) => i >= 0 && i < Agents.Count && Agents[i].IsPerson && _life![Agents[i].C.Body].Alive != 0;
    private bool PersonAwake(int i)
    {
        if (!PersonAlive(i) || Agents[i].C.Up == 0) return false;
        ref var b = ref _life![Agents[i].C.Body];
        return b.Conscious != 0 && b.Asleep == 0;
    }
    private List<int> Living() { var l = new List<int>(); for (int i = 0; i < Agents.Count; i++) if (PersonAlive(i)) l.Add(i); return l; }

    public static bool CrisisWork(GoalKind g) => g is GoalKind.FightFire or GoalKind.Rescue or GoalKind.Patch or GoalKind.Evacuate or GoalKind.Cpr or GoalKind.Treat
        or GoalKind.Surgery or GoalKind.Brace or GoalKind.GetSuit or GoalKind.Shelter or GoalKind.Flee or GoalKind.Panic or GoalKind.Rewire or GoalKind.Improvise;

    private static string DangerName(BeliefKind k) => k switch
    {
        BeliefKind.Fire => "불", BeliefKind.Smoke => "연기", BeliefKind.Breach => "구멍", BeliefKind.Vacuum => "공기가 빠진 방", BeliefKind.Injured => "쓰러진 사람", _ => "이상한 공기",
    };

    /// <summary>
    /// 그 사람이 보기에 아직 모일 때가 아닌가 (까닭 — 아니면 null). 그 사람의 판단이다 (아는 것으로만):
    /// 자고 있다 · 제가 아직 위기의 일을 한다 · 겁이 가시지 않았다 · 제가 아는 위험(두 시간 안에 확인한 불 · 연기 · 구멍 · 공기가 빠진 방 · 쓰러진 사람 · 이상한 공기)이 남았다 ·
    /// 동료(사람 · 로봇)가 아직 불 끄기 · 구하기 · 막기 · 돌보기 같은 수습을 한다 (배 안의 일은 외침 · 방송으로 다 안다) · 다들 자고 있다 (깬 이가 반이 안 된다).
    /// 모임 중에는 (meeting) 자는 이를 세지 않는다.
    /// </summary>
    public string? NotYet(Sim sim, int who, bool meeting = false)
    {
        if (!PersonAlive(who)) return "숨졌다";
        if (!PersonAwake(who)) return "자고 있다";
        ref var c = ref Agents[who].C;
        if (CrisisWork(c.Goal)) return $"제가 아직 {GoalName(c.Goal)}";
        if (c.Fear > 0.3f) return "아직 겁이 가시지 않았다";
        foreach (var b in Agents[who].Beliefs)
        {
            if (b.Conf < 0.3f || sim.Tick - b.Seen > Time.Hours(2)) continue;
            if (b.Kind is BeliefKind.Fire or BeliefKind.Smoke or BeliefKind.Vacuum or BeliefKind.Injured
                || b.Kind == BeliefKind.Breach && b.Cell >= 0 && L.Kind[b.Cell] != CellKind.Door || b.Kind == BeliefKind.BadAir && b.Conf >= 0.5f)
                return $"아직 {Ko.I(DangerName(b.Kind))} 있다고 여긴다";
        }
        foreach (var a in Agents)
            if (a.Id != who && a.C.Up != 0 && CrisisWork(a.C.Goal) && (!a.IsPerson || PersonAlive(a.Id))) return $"{Ko.I(NameOf(a.Id))} 아직 {GoalName(a.C.Goal)}";
        if (!meeting)
        {
            int alive = 0, awake = 0;
            for (int i = 0; i < Agents.Count; i++) if (PersonAlive(i)) { alive++; if (PersonAwake(i)) awake++; }
            if (awake * 2 < alive || awake < 2) return "다들 자고 있다";
        }
        return null;
    }

    // ───────────────────────── 1분마다 ─────────────────────────

    /// <summary>부름을 들은 이가 저마다 갈지 정한다 (새 모임을 처음 본 틱에 — 사회가 부름을 받아 연 다음 틱).</summary>
    private void HearCall()
    {
        if (_soc == null) return;
        var m = _soc.Meeting;
        if (m.Kind == CouncilKind.None || m.Called == _meetSeen) return;
        _meetSeen = m.Called;
        CouncilArrays();
        Array.Clear(_gatherWho);
        foreach (int a in Living())
        {
            ref var c = ref Agents[a].C;
            float interest = m.Kind switch
            {
                CouncilKind.Trial => _soc.GrudgeOf(a, m.Subject) + Math.Max(0, _soc.BondOf(a, m.Subject)) + 0.3f * Math.Max(0, _soc.BondOf(a, m.Caller)),
                CouncilKind.Ration => c.Hunger,
                CouncilKind.Manual => 0.2f + 0.3f * c.Consc,   // 배의 일 — 성실한 이일수록 온다
                CouncilKind.Memorial => 0.15f + Math.Max(0, _soc.BondOf(a, m.Subject)),   // 그와의 정만큼
                CouncilKind.Celebration => 0.1f + 0.2f * c.Extra - 0.3f * c.Depress,
                CouncilKind.Rule => 0.2f + Math.Abs(RuleOpinion(a, m.Arg)),   // 그 일을 겪은 이 · 반대하는 이도 온다
                _ => 0.3f,
            };
            if (a == m.Caller || a == m.Accuser || (a == m.Subject && !Society.IsGathering(m.Kind)) || interest + 0.3f * c.Extra + 0.3f * c.Consc + 0.2f >= 0.5f) _gatherWho[a] = 1;
        }
    }

    /// <summary>1분마다 (마음의 한 틱 끝 — 사회가 같은 틱에 결과를 계산한다): 열린 모임이면 미룰지 · 제 차례면 말하고 · 없으면 한 시간마다 안건을 본다.</summary>
    private void GatherStep(Sim sim)
    {
        if (_soc == null || sim.Tick % Society.TogetherEvery != 0) return;
        var m = _soc.Meeting;
        if (m.Kind != CouncilKind.None)
        {
            if (CallOff(sim, m)) return;
            SpeakTurn(sim, m);
            return;
        }
        if (sim.Tick % Time.TicksPerHour == 0) ConsiderGatherings(sim);
    }

    /// <summary>부른 뒤 일이 났으면 부른 이가 보고 미룬다 (부른 이가 깨어 있지 않으면 모이던 이 가운데 깬 이가 제 판단으로).</summary>
    private bool CallOff(Sim sim, in Council m)
    {
        if (_soc!.LastCrisis <= m.Called) return false;
        int judge = PersonAwake(m.Caller) ? m.Caller : -1;
        if (judge < 0)
            for (int i = 0; i < Agents.Count && judge < 0; i++)
                if (PersonAwake(i) && (i < _gatherWho.Length && _gatherWho[i] != 0 || RoomOf(Agents[i].C.Cell) == m.Room)) judge = i;
        if (judge < 0) return false;
        string? why = !PersonAlive(m.Caller) ? $"부른 {Ko.I(NameOf(m.Caller))} 숨졌다" : NotYet(sim, judge, meeting: true);
        if (why == null) return false;
        int cause = sim.Causes.Add(CauseKind.Society, $"{Ko.I(NameOf(judge))} {Ko.Eul(Society.MeetName(m.Kind))} 미뤘다 — {why}", sim.Tick, m.Cell, m.Cause, 0, judge);
        Act(sim, new SocialAct { Kind = SocialActKind.CallOff, Meet = m.Kind, Actor = judge, Target = m.Subject, Other = -1, Room = m.Room, Cell = m.Cell, Cause = cause });
        return true;
    }

    /// <summary>제 차례가 온 이가 말한다: 의견 (찬성 쪽 + · 반대 쪽 −) · 선거면 고른 후보 · 한 마디.</summary>
    private void SpeakTurn(Sim sim, in Council m)
    {
        int a = _soc!.SpeakerNow;
        if (a < 0 || !PersonAwake(a)) return;   // 깨어 있지 않으면 말하지 못한다 (사회가 차례를 넘긴다)
        float op = Opinion(sim, a, m);
        int pick = m.Kind == CouncilKind.Election ? Vote(a) : -1;
        string name = NameOf(a), x = m.Subject >= 0 ? NameOf(m.Subject) : "";
        string line = m.Kind switch
        {
            CouncilKind.Trial when a == m.Subject => Agents[a].C.Consc > 0.6f && _soc.GrudgeSum(m.Subject) > 1.5f ? "제 잘못입니다 — 벌을 받겠습니다" : "억울합니다 — 제 말도 들어 주세요",
            CouncilKind.Trial when op > 0.5f => $"{Ko.Eun(x)} 벌을 받아야 한다",
            CouncilKind.Trial when op > 0 => $"잘못은 했다 — 그래도 가볍게 하자",
            CouncilKind.Trial when op > -0.4f => "증거가 모자라다",
            CouncilKind.Trial => $"나는 {Ko.Eul(x)} 믿는다",
            CouncilKind.Ration when op > 0.4f => "이러다 다 굶어 죽는다 — 배급은 그만두자",
            CouncilKind.Ration when op > 0 => "조금만 더 먹게 하자",
            CouncilKind.Ration => "참아야 한다 — 식량이 바닥나면 끝이다",
            CouncilKind.Manual => ManualLine(op),
            CouncilKind.Memorial => MemorialLine(sim, a, m),
            CouncilKind.Celebration => FeastLine(sim, a, m),
            CouncilKind.Rule => RuleLine(m.Arg, op),
            _ when pick == a => "제가 맡겠습니다",
            _ => "누가 맡든 따르겠다",
        };
        int said = sim.Causes.Add(CauseKind.Society, $"{name}: \"{line}\"", sim.Tick, Agents[a].C.Cell, m.Cause, 0, a);
        Act(sim, new SocialAct { Kind = SocialActKind.Speak, Meet = m.Kind, Actor = a, Target = m.Subject, Other = -1, Arg = pick, Room = m.Room, Cell = Agents[a].C.Cell, Value = op, Cause = said });
    }

    private float Opinion(Sim sim, int a, in Council m)
    {
        var soc = _soc!;
        ref var c = ref Agents[a].C;
        float op;
        switch (m.Kind)
        {
            case CouncilKind.Trial:
            {
                int x = m.Subject;
                if (a == x) return -1;
                float ev = 0;
                foreach (var mem in soc.Memories)
                    if (mem.Owner == a && mem.About == x && mem.Topic is Topic.Blame or Topic.Fight or Topic.Gossip or Topic.Hurt or Topic.Death) ev = Math.Max(ev, 0.6f * soc.MemoryWeight(mem, sim.Tick));
                if (x < soc.LiarMark.Length && soc.LiarMark[x] > 0) ev += 0.2f;                 // 거짓말이 들킨 적이 있다
                if (sim.Tick - soc.LogShownAt(x) < Time.Hours(72)) ev += 0.3f;                   // 주컴퓨터 정비 기록에 그가 나왔다
                op = 1.2f * soc.GrudgeOf(a, x) + 0.5f * (c.Consc - 0.5f) - 0.4f * (c.Agree - 0.5f) - 0.6f * Math.Max(0, soc.BondOf(a, x)) - (soc.SameBand(a, x) ? 0.4f : 0) + ev - 0.15f;
                break;
            }
            case CouncilKind.Ration:
                op = c.Hunger + 0.3f * (1 - c.Consc) - 0.4f * soc.Trust(a) - 0.3f;
                break;
            case CouncilKind.Manual:
                op = ManualOpinion(a);
                break;
            case CouncilKind.Rule:
                op = RuleOpinion(a, m.Arg);
                break;
            default:
                return 0;
        }
        // 동조 (Asch 1951): 대표가 먼저 말했으면 그를 믿는 만큼 그 의견 쪽으로
        if (soc.RepSaid is float rop && soc.Rep >= 0 && a != soc.Rep) op += 0.3f * soc.TrustIn(a, soc.Rep) * (rop - op);
        return Math.Clamp(op, -1, 1);
    }

    /// <summary>선거: 후보(존경 셋) 가운데 — 저라면 나서고 (외향만큼) · 남이면 믿음 + 정 − 원한 + 같은 무리.</summary>
    private int Vote(int a)
    {
        var soc = _soc!;
        int pick = -1; float ps = float.MinValue;
        foreach (int c in soc.Candidates())
        {
            float s = c == a ? 0.6f + 0.3f * Agents[a].C.Extra : soc.TrustIn(a, c) + soc.BondOf(a, c) - soc.GrudgeOf(a, c) + (soc.SameBand(a, c) ? 0.2f : 0);
            if (s > ps) { ps = s; pick = c; }
        }
        return pick;
    }

    // ───────────────────────── 한 시간마다: 부를까 ─────────────────────────

    /// <summary>안건을 보고 부를 사람이 정한다 — 추모 · 축하 먼저 (하루 한 번 제한이 없다) · 회의는 하루 한 번까지 · 셋 넘게 살아 있을 때.</summary>
    private void ConsiderGatherings(Sim sim)
    {
        if (MessRoom() < 0) return;
        if (ConsiderMemorial(sim) || ConsiderCelebration(sim)) return;
        if (!_callNow && sim.Tick - _soc!.LastCouncil < Time.Hours(20)) return;   // 회의는 하루 한 번까지 (다들 그렇게 여긴다)
        if (Living().Count < 3) return;
        if (!ConsiderTrial(sim) && !ConsiderRation(sim) && !ConsiderManual(sim) && !ConsiderRule(sim)) ConsiderElection(sim);
    }

    /// <summary>부른다 — 부를 이가 제 판단으로 아직 때가 아니면 부르지 않는다 (까닭을 돌려준다).</summary>
    private string? Call(Sim sim, CouncilKind kind, int caller, int subject, int accuser, string text, int parent, int arg = 0)
    {
        int room = MessRoom();
        if (room < 0) return "모일 곳이 없다";
        if (NotYet(sim, caller) is string notYet) return notYet;
        int cell = RoomMiddle(room);
        int cause = sim.Causes.Add(CauseKind.Society, text, sim.Tick, Agents[caller].C.Cell, parent, 0, caller);
        Act(sim, new SocialAct { Kind = SocialActKind.CallMeeting, Meet = kind, Actor = caller, Target = subject, Other = accuser, Arg = arg, Room = room, Cell = cell, Cause = cause });
        return null;
    }

    private int RoomMiddle(int room)
    {
        var cells = L.Rooms[room].Cells;
        double mx = cells.Average(c => L.X(c)), my = cells.Average(c => L.Y(c));
        int best = cells[0]; double bd = double.MaxValue;
        foreach (int c in cells) { if (L.Kind[c] != CellKind.Floor || L.FurnitureAt[c] >= 0) continue; double d = Math.Abs(L.X(c) - mx) + Math.Abs(L.Y(c) - my); if (d < bd) { bd = d; best = c; } }
        return best;
    }

    /// <summary>
    /// 숨진 이를 기린다 — 깨어 있는 이 가운데 그와 가장 가까웠던 이 (정 0.2 넘게 — 없으면 대표 · 가장 외향인 이)가
    /// 하루는 지나야 한다고 여기고 (여섯 시간) · 제 판단으로 아직 때가 아니면 (수습 중 · 아는 위험 · 겁 · 다들 잔다) 미룬다 —
    /// 수습 때문에 미루면 한 번 '추모를 미뤘다'고 말한다 (행동 '미루기'). 사흘이 지나면 그만둔다.
    /// </summary>
    private bool ConsiderMemorial(Sim sim)
    {
        var soc = _soc!;
        foreach (var (dead, at, cause) in soc.Mourning)
        {
            if (sim.Tick - at > Time.Days(3)) continue;
            if (sim.Tick - at < Time.Hours(Society.MournAfterHours) && !_callNow) continue;
            var people = Living().Where(PersonAwake).ToList();
            if (people.Count < 2) continue;
            int caller = -1; float bb = 0.2f;
            foreach (int j in people) if (soc.BondOf(j, dead) > bb) { bb = soc.BondOf(j, dead); caller = j; }
            if (caller < 0) caller = soc.Rep >= 0 && people.Contains(soc.Rep) ? soc.Rep : people.OrderByDescending(j => Agents[j].C.Extra).ThenBy(j => j).First();
            string? why = Call(sim, CouncilKind.Memorial, caller, dead, -1, $"{Ko.I(NameOf(caller))} 사람들을 식당으로 불렀다 — {Ko.Eul(NameOf(dead))} 기리자", cause);
            if (why == null) return true;
            if (!why.Contains("자고") && !soc.MournDeferred(dead))
            {
                int c = sim.Causes.Add(CauseKind.Society, $"{Ko.I(NameOf(caller))} {NameOf(dead)}의 추모를 미뤘다 — {why}", sim.Tick, Agents[caller].C.Cell, cause, 0, caller);
                Act(sim, new SocialAct { Kind = SocialActKind.DeferMemorial, Meet = CouncilKind.Memorial, Actor = caller, Target = dead, Other = -1, Room = -1, Cell = Agents[caller].C.Cell, Cause = c });
            }
        }
        return false;
    }

    /// <summary>
    /// 위기를 넘기고 (여섯 시간 — 모두 무사한지 보고) · 반가운 소식이면 바로 — 가장 외향인 깬 이가 정한다:
    /// 제 판단으로 아직 때가 아니거나 (수습 · 위험 · 겁 · 다들 잔다) · 깬 이의 반 넘게 아직 일하는 중이면 기다린다. 하루 한 번.
    /// </summary>
    private bool ConsiderCelebration(Sim sim)
    {
        var soc = _soc!;
        var f = soc.Feast;
        if (f.from < 0 || f.weight < 2 || sim.Tick - f.from > Time.Hours(48)) return false;
        if (!_callNow && (!f.news && sim.Tick - soc.LastCrisis < Time.Hours(Society.FeastAfterHours) || sim.Tick - soc.LastFeast < Time.Hours(20))) return false;
        var people = Living().Where(PersonAwake).ToList();
        if (people.Count < 2) return false;
        if (people.Count(j => Agents[j].C.Goal == GoalKind.Work) * 2 > people.Count) return false;   // 아직 다들 일한다 — 일을 마친 저녁 · 쉬는 때에
        int caller = people.OrderByDescending(j => Agents[j].C.Extra).ThenBy(j => j).First();
        string why = f.news ? "반가운 소식이다 — 한잔하자" : "모두 무사했다 — 한잔하자";
        return Call(sim, CouncilKind.Celebration, caller, -1, -1, $"{Ko.I(NameOf(caller))} 사람들을 불렀다 — {why}", f.cause, f.hero) == null;
    }

    /// <summary>재판: 한 사람에게 원한이 모였다 (모두의 원망 1 넘게 · 둘 넘게 원망한다 · 닷새 안에 다시 세우지 않는다) — 가장 원망하는 이가 고발하고 · 대표(피고가 아니면)나 고발한 이가 부른다.</summary>
    private bool ConsiderTrial(Sim sim)
    {
        var soc = _soc!;
        var people = Living();
        int best = -1, accuser = -1; float bs = 0;
        foreach (int x in people)
        {
            if (sim.Tick - soc.TriedAt(x) < Time.Hours(24 * 5)) continue;
            float sum = 0, am = 0; int n = 0, acc = -1;
            foreach (int j in people)
            {
                if (j == x) continue;
                float g = soc.GrudgeOf(j, x);
                sum += g; if (g >= Society.GrudgeAt) n++;
                if (g > am) { am = g; acc = j; }
            }
            if (sum >= 1.0f && n >= 2 && sum > bs) { bs = sum; best = x; accuser = acc; }
        }
        if (best < 0) return false;
        int root = 0; long rt = long.MinValue;
        foreach (var m in soc.Memories)
            if (m.Owner == accuser && m.About == best && m.Topic is Topic.Blame or Topic.Fight or Topic.Gossip or Topic.Hurt or Topic.Death && m.Tick > rt) { rt = m.Tick; root = m.Cause; }
        int caller = soc.Rep >= 0 && PersonAlive(soc.Rep) && soc.Rep != best ? soc.Rep : accuser;
        return Call(sim, CouncilKind.Trial, caller, best, accuser,
            $"{Ko.I(NameOf(caller))} 회의를 소집했다 — {Ko.I(NameOf(accuser))} {Ko.Eul(NameOf(best))} 고발했다 (모두의 원망 {bs:0.0})", root) == null;
    }

    /// <summary>배급 반란: 배급 중 이틀 넘게 다들 배고프다 — 대표나 가장 배고픈 이가 부른다 (이미 배급을 거부하는 이가 있으면 아니다).</summary>
    private bool ConsiderRation(Sim sim)
    {
        var soc = _soc!;
        if (RationFood <= 0 || soc.HungerFrom == Society.NoHunger || sim.Tick - soc.HungerFrom < Time.Hours(48)) return false;
        var people = Living();
        foreach (int i in people) if (Defying(i, sim.Tick)) return false;
        int hungriest = people.OrderByDescending(i => Agents[i].C.Hunger).ThenBy(i => i).First();
        int caller = soc.Rep >= 0 && PersonAlive(soc.Rep) ? soc.Rep : hungriest;
        return Call(sim, CouncilKind.Ration, caller, -1, -1, $"{Ko.I(NameOf(caller))} 회의를 소집했다 — 배고픈 지 이틀째, 배급을 이대로 따를 수 없다", RationCause) == null;
    }

    /// <summary>매뉴얼 안건 (사회가 쌓은 통한 방법 · 틀어진 방법) — 지은 이 · 대표 · 주컴퓨터를 가장 믿는 이가 부른다.</summary>
    private bool ConsiderManual(Sim sim)
    {
        var soc = _soc!;
        if (soc.ManualAgendaHead is not var (key, add, proposer, _, cause)) return false;
        var people = Living();
        int caller = proposer >= 0 && PersonAlive(proposer) ? proposer
            : soc.Rep >= 0 && PersonAlive(soc.Rep) ? soc.Rep : people.OrderByDescending(i => soc.Trust(i)).ThenBy(i => i).First();
        string name = soc.MethodName(sim, key, cause);
        string text = add == 1
            ? $"{Ko.I(NameOf(caller))} 회의를 소집했다 — 지난 사고에 통한 방법을 매뉴얼에 넣자: {name}"
            : $"{Ko.I(NameOf(caller))} 회의를 소집했다 — 매뉴얼의 방법이 두 번이나 틀어졌다 · 빼자: {name}";
        return Call(sim, CouncilKind.Manual, caller, -1, -1, text, cause, key) == null;
    }

    /// <summary>배의 규칙: 사흘에 말소리에 깬 일 셋 → 가장 많이 깬 이가 · 사흘에 켜 둔 조리대 둘 → 켜 둔 적 없는 성실한 이(대표)가 꺼낸다 (부결되면 사흘 뒤 다시).</summary>
    private bool ConsiderRule(Sim sim)
    {
        var soc = _soc!;
        var people = Living();
        var wakes = soc.WakeLog; var stoves = soc.StoveLog;
        if (!soc.QuietRule && wakes.Count >= 3 && sim.Tick - soc.AskedQuiet > Time.Days(Society.RuleWindowDays))
        {
            int caller = people.OrderByDescending(j => wakes.Count(x => x.woken == j)).ThenBy(j => j).First();
            if (Call(sim, CouncilKind.Rule, caller, -1, -1, $"{Ko.I(NameOf(caller))} 회의를 소집했다 — 밤마다 말소리에 잠을 깬다, 규칙을 정하자", wakes[^1].cause, 1) == null) return true;
        }
        if (!soc.StoveRule && stoves.Count >= 2 && sim.Tick - soc.AskedStove > Time.Days(Society.RuleWindowDays))
        {
            bool Forgot(int j) => stoves.Any(x => x.who == j);
            int caller = soc.Rep >= 0 && PersonAlive(soc.Rep) && !Forgot(soc.Rep) ? soc.Rep : people.OrderBy(j => Forgot(j) ? 1 : 0).ThenByDescending(j => Agents[j].C.Consc).ThenBy(j => j).First();
            return Call(sim, CouncilKind.Rule, caller, -1, -1, $"{Ko.I(NameOf(caller))} 회의를 소집했다 — 조리대를 켜 둔 채 떠나는 일이 잦다, 규칙을 정하자", stoves[^1].cause, 2) == null;
        }
        return false;
    }

    /// <summary>선거: 대표가 없다 (이틀 뒤부터) · 숨졌다 · 임기가 끝났다 · 다들 믿지 않는다 — 가장 존경받는 이가 부른다.</summary>
    private bool ConsiderElection(Sim sim)
    {
        var soc = _soc!;
        int rep = soc.Rep;
        bool need = _callNow || (rep < 0 ? sim.Tick > Time.Hours(48) : !PersonAlive(rep) || soc.RepUntil <= sim.Tick || soc.MeanTrustIn(rep) < 0.35f);
        if (!need) return false;
        var people = Living();
        int caller = people.OrderByDescending(i => i < soc.Respect.Length ? soc.Respect[i] : 0).ThenBy(i => i).First();
        string why = rep < 0 ? "대표를 뽑자" : !PersonAlive(rep) ? $"{NameOf(rep)}의 자리를 이을 대표를 뽑자" : soc.RepUntil <= sim.Tick ? "대표의 임기가 끝났다" : $"{Ko.Eul(NameOf(rep))} 더는 믿지 못한다 — 대표를 다시 뽑자";
        return Call(sim, CouncilKind.Election, caller, -1, -1, $"{Ko.I(NameOf(caller))} 회의를 소집했다 — {why}", 0) == null;
    }

    // ───────────────────────── 생각과 말 ─────────────────────────

    /// <summary>매뉴얼 안건에 대한 생각: 넣자 — 지은 이를 믿는 정도 · 성실 · 그 분야 앎 · 잃은 공기 / 빼자 — 틀어진 몫 · 성실 · 지은 이를 믿는 정도.</summary>
    private float ManualOpinion(int a)
    {
        var soc = _soc!;
        var g = soc.AgendaNow;
        ref var c = ref Agents[a].C;
        if (g.add == 1 && a == g.proposer) return 0.8f;
        float trust = g.proposer < 0 ? soc.Trust(a) : soc.TrustIn(a, g.proposer);
        if (g.add == 1)
            return 0.8f * (trust - 0.45f) + 0.3f * (c.Consc - 0.5f) + 0.4f * (c.KnPressure - 0.3f) - Math.Min(0.3f, g.lost / 3000f);
        var e = soc.Manual.FirstOrDefault(x => x.Key == g.key);
        float failShare = e == null ? 0 : e.Fails / (float)Math.Max(1, e.Fails + e.Wins);
        return 0.5f * failShare + 0.3f * (c.Consc - 0.5f) - 0.3f * (trust - 0.5f);
    }

    private string ManualLine(float op) => _soc!.AgendaNow.add == 1
        ? op > 0.15f ? "통했던 방법이다 — 매뉴얼에 넣자" : op > 0 ? "조건을 붙여 매뉴얼에 넣자" : _soc.AgendaNow.proposer < 0 ? "주컴퓨터가 지어낸 것을 어떻게 믿나 — 그때그때 정하자" : "한 번 통했다고 매뉴얼에 넣기는 이르다"
        : op > 0 ? "두 번이나 틀어졌다 — 빼자" : "운이 나빴을 뿐이다 — 그대로 두자";

    /// <summary>규칙에 대한 의견: 그 일을 겪은 만큼 + 성실 − 말하기 좋아하는 만큼 (밤 규칙) · 제가 일을 낸 이는 덜 반긴다.</summary>
    private float RuleOpinion(int a, int rule)
    {
        var soc = _soc!;
        ref var c = ref Agents[a].C;
        if (rule == 1)
        {
            int woken = soc.WakeLog.Count(x => x.woken == a), waker = soc.WakeLog.Count(x => x.by == a);
            return 0.5f * Math.Min(1, woken / 2f) - 0.4f * Math.Min(1, waker / 2f) + 0.5f * (c.Consc - 0.5f) - 0.6f * (c.Extra - 0.5f) + 0.05f;
        }
        int forgot = soc.StoveLog.Count(x => x.who == a);
        return 0.15f + 0.5f * (c.Consc - 0.5f) - 0.3f * Math.Min(1, forgot) + 0.2f * c.LessonFire;
    }

    private static string RuleLine(int rule, float op) => rule == 1
        ? (op > 0.3f ? "밤엔 조용히 하자 — 잠 좀 자자" : op > 0 ? "자는 사람 곁에서만이라도 말을 아끼자" : "말 한마디도 못 하면 숨이 막힌다")
        : (op > 0.3f ? "조리대 켜 둔 채 떠나는 일, 이제 그만하자" : op > 0 ? "끄고 가는 것쯤은 지킬 수 있다" : "규칙까지 만들 일은 아니다");

    private string MemorialLine(Sim sim, int a, in Council m)
    {
        var soc = _soc!;
        int dead = m.Subject;
        string x = NameOf(dead);
        if (soc.DebtOf(a, dead) > 0.2f) return $"{Ko.I(x)} 나를 살렸다 — 갚을 길이 없다";
        int k = (int)(Rng.Unit(sim.Seed, Rng.Key("추모"), m.Cause, dead) * 5) + soc.SpokenCount;   // 앞사람과 다른 말 (한 모임에서 차례로 돈다)
        if (soc.GrudgeOf(a, dead) > Society.GrudgeAt) return "서운한 것도 있었지만 — 이제 다 내려놓는다";
        if (soc.BondOf(a, dead) > Society.FriendAt)
            return (k % 5) switch { 0 => $"{Ko.Eun(x)} 늘 먼저 웃던 사람이었다", 1 => "같이 늦게까지 이야기하던 밤이 생각난다", 2 => "아직도 식당에서 그 목소리가 들리는 것 같다",
                3 => $"{x}에게 못 한 말이 많다", _ => "그 자리가 빈 식탁이 아직 낯설다" };
        string? nick = soc.NickOf(dead);
        if (nick != null && k % 2 == 0) return $"우리 {Ko.Eul(nick)} 잊지 않겠다";
        return (k % 3) switch { 0 => "좋은 동료였다", 1 => $"{x}의 몫까지 해내겠다", _ => "잊지 않겠다" };
    }

    private string FeastLine(Sim sim, int a, in Council m)
    {
        var soc = _soc!;
        int hero = m.Arg;
        if (a == m.Caller) return hero >= 0 && hero != a && PersonAlive(hero) ? $"{soc.NickOf(hero) ?? NameOf(hero)}에게 한잔!" : "살아 있으니 됐다 — 한잔하자";
        if (a == hero) return "다들 함께해서 넘긴 거다";
        int k = (int)(Rng.Unit(sim.Seed, Rng.Key("축하"), m.Cause, m.Caller) * 4) + soc.SpokenCount;   // 앞사람과 다른 말
        return (k % 4) switch
        {
            0 when hero >= 0 && hero != a && PersonAlive(hero) => $"{soc.NickOf(hero) ?? NameOf(hero)} 아니었으면 큰일 날 뻔했다",
            0 or 1 => "살아 있으니 됐다", 2 => "오늘 밥은 맛있다", _ => "다음엔 덜 무섭기를",
        };
    }

    // ───────────────────────── 시험용 ─────────────────────────

    /// <summary>시험용: 지금 회의 안건을 본다 (하루 한 번 · 선거의 이틀을 기다리지 않는다 — 부르는 사람의 판단은 그대로 거친다) · 사회가 바로 읽는다.</summary>
    public void CallCouncilNow(Sim sim)
    {
        Bind(sim);
        if (_soc == null || _soc.Meeting.Kind != CouncilKind.None || MessRoom() < 0) return;
        _callNow = true;
        try { if (Living().Count >= 3 && !ConsiderTrial(sim) && !ConsiderRation(sim) && !ConsiderManual(sim) && !ConsiderRule(sim)) ConsiderElection(sim); }
        finally { _callNow = false; }
        _soc.TakeActs(sim);
    }

    /// <summary>시험용: 지금 추모 · 축하를 본다 (여섯 시간 · 하루 한 번을 기다리지 않는다 — 부르는 사람의 판단은 그대로 거친다) · 사회가 바로 읽는다.</summary>
    public void CallGatheringNow(Sim sim)
    {
        Bind(sim);
        if (_soc == null || _soc.Meeting.Kind != CouncilKind.None || MessRoom() < 0) return;
        _callNow = true;
        try { if (!ConsiderMemorial(sim)) ConsiderCelebration(sim); }
        finally { _callNow = false; }
        _soc.TakeActs(sim);
    }
}
