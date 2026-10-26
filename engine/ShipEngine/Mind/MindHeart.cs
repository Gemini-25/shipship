namespace ShipEngine;

/// <summary>⑧ 충격의 갈래 — 무엇이 그 일을 떠올리게 하나.</summary>
public enum ShockKind : byte { None, Fire, Breach, Air, Death, Hurt, NearDeath }

/// <summary>⑧ 마음 사실(FactKind.Heart)의 크기.</summary>
public enum HeartEvent : byte { None, Nightmare, Attack, Flashback, Fearful, Hardened, Depressed, Anxious, Grew, Counseled, CheckIn, Listened }

/// <summary>마음의 흐름에 드는 것 (<see cref="Mind.HeartKinetics"/>의 입력 — 감정 · 욕구 · 곁사람).</summary>
public struct HeartIn
{
    public double Sad, Weary, Stress, SleepLoss, Lonely, Joy;
    public bool Support;
}

/// <summary>
/// ⑧ 기억 · 정신 건강 (깊이 바퀴 3 — docs/ENGINE.md '기억 · 정신 건강'). 겪은 일이 충격으로 남고 · 위험한 이에게는 외상으로 오래 남아 악몽 · 되살아남이 되고 ·
/// 지나가면 성격이 조금 바뀐다 (겁이 많아지거나 · 맞섰으면 단단해진다). 몇 주에 걸친 슬픔 · 고립의 피로 · 긴장 · 얕은 잠 · 외상이 우울 · 불안이 되어
/// 일손을 놓고 · 끼니를 거르고 · 침대에 틀어박히고 · 까닭 없는 공황 발작을 낳는다.
/// <para>외상 후 스트레스: 외상을 겪은 이의 20~30%가 오래 앓는다 — 심한 일 · 신경성 · 앞선 외상 · 곁사람이 없음이 위험 (Brewin 외 2000 메타 분석) · 대부분은 몇 달에 걸쳐 저절로 낫는다.</para>
/// </summary>
public sealed partial class Mind
{
    public const double ShockHalfDays = 3, TraumaHalfDays = 60, DepressRiseDays = 14, DepressFallDays = 14, AnxietyRiseDays = 7, AnxietyFallDays = 14;
    public long Shocks, Nightmares, Flashbacks, PanicAttacks, Depressions, Anxieties, Hardened, Fearful, Grew, SleepPills, Counsels;
    private long _heartCursor;
    private readonly HashSet<long> _deathSeen = new();   // 사람 × 몸 — 그 죽음을 곁에서 본 충격은 한 번
    private readonly HashSet<long> _deathKnown = new();  // 사람 · 로봇 × 몸 — 그 죽음을 안다 (본 · 들은 · 방송 · 소문 — 믿음이 흐려져도 남는다 · DF2 · MIND 14단 '아는 소식' 당김)
    private readonly List<(int agent, int cause)> _flashQ = new();   // 이번 틱 새로 믿게 된 위험 중 외상의 그 일과 같은 갈래 (믿음을 얻은 곳에서 — 감각 · 말 · 방송)

    /// <summary>
    /// 충격을 더한다 (순수): 남은 몫만큼 오르고 · 오른 몫 × 위험만큼 외상이 남는다 —
    /// 위험 = 0.15 + 0.5 × 신경성 + 0.2 × 앞선 외상 − 0.25 × 곁사람 (0.05 ~ 0.9). 돌려주는 값: 오른 충격.
    /// </summary>
    public static double AddShock(ref AgentCore c, double dose, bool support)
    {
        if (dose <= 0) return 0;
        double before = c.Shock;
        c.Shock = (float)Math.Min(1, c.Shock + dose * (1 - c.Shock));
        double up = c.Shock - before;
        double risk = Math.Clamp(0.15 + 0.5 * c.Neuro + 0.2 * c.Trauma - (support ? 0.25 : 0), 0.05, 0.9);
        c.Trauma = (float)Math.Min(1, c.Trauma + up * risk * (1 - c.Trauma));
        if (c.Shock > c.ShockPeak) c.ShockPeak = c.Shock;
        return up;
    }

    /// <summary>
    /// 마음의 흐름 (순수 — 시험이 몇 주를 한꺼번에 돌린다): 충격은 사흘 · 외상은 두 달(곁사람이 있으면 한 달)에 반으로 ·
    /// 우울 · 불안은 끌림이 문턱을 넘은 만큼 차오르고 (신경성만큼 · 다 찰수록 느리게) 아래면 풀린다.
    /// </summary>
    public static void HeartKinetics(ref AgentCore c, in HeartIn x, double h)
    {
        double days = h / 24;
        c.Shock = (float)(c.Shock * Math.Exp(-0.6931 * days / ShockHalfDays));
        c.Trauma = (float)(c.Trauma * Math.Exp(-0.6931 * days / (TraumaHalfDays * (x.Support ? 0.5 : 1))));
        double vul = 0.6 + 0.8 * c.Neuro;
        double dd = 0.4 * x.Sad + 0.3 * x.Weary + 0.25 * x.Stress + 0.2 * x.SleepLoss + 0.35 * c.Trauma + 0.2 * x.Lonely + 0.1 * c.Depress - 0.3 * x.Joy - 0.3;
        c.Depress = (float)Math.Clamp(c.Depress + (dd > 0 ? dd * vul * (1 - c.Depress) / DepressRiseDays : dd / DepressFallDays) * days, 0, 1);
        double da = 0.5 * x.Stress + 0.45 * c.Trauma + 0.2 * x.Weary + 0.15 * c.Anxiety - 0.25;
        c.Anxiety = (float)Math.Clamp(c.Anxiety + (da > 0 ? da * vul * (1 - c.Anxiety) / AnxietyRiseDays : da / AnxietyFallDays) * days, 0, 1);
    }

    /// <summary>배에 정 0.5 넘는 산 사람이 있나 (곁사람 — 외상의 위험을 덜고 빨리 낫게 한다).</summary>
    private bool HasSupport(int agent)
    {
        if (_soc == null) return false;
        foreach (var o in Agents)
            if (o.Id != agent && o.IsPerson && _life![o.C.Body].Alive != 0 && (_soc.BondOf(agent, o.Id) > 0.5f || _soc.PartnerOf(agent) == o.Id)) return true;
        return false;
    }

    private static ShockKind ShockOf(BeliefKind k) => k switch
    {
        BeliefKind.Fire or BeliefKind.Smoke or BeliefKind.SmokeSmell => ShockKind.Fire,
        BeliefKind.Breach or BeliefKind.Vacuum or BeliefKind.Leak => ShockKind.Breach,
        BeliefKind.BadAir => ShockKind.Air,
        BeliefKind.Dead => ShockKind.Death,
        _ => ShockKind.None,
    };

    public static string ShockText(ShockKind k) => k switch
    {
        ShockKind.Fire => "불에 갇혔던 일", ShockKind.Breach => "공기가 빠지던 일", ShockKind.Air => "숨이 막히던 일",
        ShockKind.Death => "동료가 죽은 일", ShockKind.Hurt => "크게 다친 일", ShockKind.NearDeath => "죽을 뻔했던 일", _ => "그 일",
    };

    /// <summary>지금 겁의 까닭: 둘레의 가장 무서운 믿음 (같은 방은 두 배) · 없으면 피하는 일의 원인.</summary>
    private (int cause, ShockKind kind) FearSource(Agent a)
    {
        ref var c = ref a.C;
        int room = RoomOf(c.Cell), cause = 0;
        var kind = ShockKind.None;
        float best = 0;
        foreach (var b in a.Beliefs)
        {
            if (b.Conf < 0.4f) continue;
            var k = ShockOf(b.Kind);
            if (k == ShockKind.None) continue;
            float w = b.Conf * (RoomOf(b.Cell) == room ? 2 : 1);
            if (w > best) { best = w; kind = k; cause = b.Cause; }
        }
        if (cause == 0) cause = c.GoalCause;
        return (cause, kind);
    }

    /// <summary>충격 한 몫 — 가장 큰 몫이면 그 일이 충격의 원인 · 방 · 갈래가 된다.</summary>
    private void Shock(Sim sim, Agent a, double dose, int cause, ShockKind kind, bool support)
    {
        ref var c = ref a.C;
        float before = c.Shock;
        if (AddShock(ref c, dose, support) <= 0) return;
        if (before < 0.3f && c.Shock >= 0.3f) Shocks++;
        if (dose >= c.ShockTop)
        {
            c.ShockTop = (float)dose; c.ShockAt = sim.Tick; c.ShockRoom = RoomOf(c.Cell);
            if (cause != 0) c.ShockCause = cause;
            if (kind != ShockKind.None) c.ShockKind = kind;
        }
        if (c.Trauma >= 0.3f) c.TraumaHigh = 1;
    }

    /// <summary>그 죽음을 아는가 (본 · 들은 · 방송 · 소문으로 — 제 앎).</summary>
    public bool KnowsDeath(int agent, int body) => _deathKnown.Contains((long)agent << 32 | (uint)body);

    /// <summary>
    /// 죽음을 알게 됐다 (DF2 — 소식은 들은 이만): 처음이면 사람은 행동 '알게 됐다'를 남긴다 (사회가 정 · 원망 · 슬픔의 결과를 셈한다) · 이미 알면 아무것도 하지 않는다 (다시 '발견'하지 않는다).
    /// 처음 안 것이면 참.
    /// </summary>
    private bool KnowDeath(Sim sim, Agent a, int body, BeliefSource src, int cause)
    {
        if (body < 0 || !_deathKnown.Add((long)a.Id << 32 | (uint)body)) return false;
        int dead = AgentOfBody(body);
        if (a.IsPerson && dead >= 0 && dead != a.Id)
            Act(sim, new SocialAct { Kind = SocialActKind.LearnDeath, Actor = a.Id, Target = dead, Other = (int)src, Room = RoomOf(a.C.Cell), Cell = a.C.Cell, Cause = cause });
        return true;
    }

    /// <summary>새로 본 것: 곁에서 본 죽음은 충격 (한 번 · 가까웠으면 더).</summary>
    private void HeartOnFresh(Sim sim, Agent a, in Belief b)
    {
        if (b.Kind == BeliefKind.Dead && b.Src == BeliefSource.Saw && b.Subject >= 0 && _deathSeen.Add((long)a.Id << 32 | (uint)b.Subject))
        {
            int other = AgentOfBody(b.Subject);
            float bond = _soc != null && other >= 0 ? Math.Max(0, _soc.BondOf(a.Id, other)) : 0;
            Shock(sim, a, 0.25 + 0.35 * bond, b.Cause, ShockKind.Death, HasSupport(a.Id));
        }
    }

    /// <summary>믿음을 새로 얻었다 (<see cref="Learn"/>): 외상이 있는 이에게 그 일과 같은 갈래의 위험이면 되살아남을 건다 (이번 틱 끝에).</summary>
    private void FlashCue(Agent a, BeliefKind kind, int cause)
    {
        if (!a.IsPerson || a.C.Trauma < 0.2f) return;
        var k = ShockOf(kind);
        if (k != ShockKind.None && k == a.C.ShockKind) _flashQ.Add((a.Id, cause));
    }

    /// <summary>그날이 되살아났다: 겁이 0.4 + 0.5 × 외상으로 솟는다 · 신경성 0.7 넘고 외상 0.5 넘으면 공황 (30분에 한 번).</summary>
    private void FlashbackQueue(Sim sim)
    {
        foreach (var (id, why) in _flashQ) Flashback(sim, Agents[id], why);
        _flashQ.Clear();
    }

    private void Flashback(Sim sim, Agent a, int why)
    {
        ref var c = ref a.C;
        if (c.Up == 0 || _life![c.Body].Asleep != 0 || c.FlashAt > 0 && sim.Tick - c.FlashAt < Time.Minutes(30)) return;   // 0: 아직 없었다
        c.FlashAt = sim.Tick;
        c.Fear = Math.Max(c.Fear, 0.4f + 0.5f * c.Trauma);
        Flashbacks++;
        int cause = sim.Causes.Add(CauseKind.Person, $"{Ko.I(NameOf(a.Id))} 그날이 되살아났다 — {ShockText(c.ShockKind)}", sim.Tick, c.Cell, c.ShockCause, why, a.Id);
        sim.Facts.Emit(sim.Tick, FactKind.Heart, c.Cell, (float)HeartEvent.Flashback, cause, a.Id);
        a.LastThought = $"그날이 떠오른다 — {ShockText(c.ShockKind)}"; a.LastThoughtTick = sim.Tick;
        if (c.Neuro > 0.7f && c.Trauma > 0.5f && c.Mode == 0) StartPanic(sim, a);
    }

    /// <summary>1분마다: 충격의 몫 (겁 · 숨이 끊길 뻔함 · 큰 아픔) · 마음의 흐름 · 악몽 · 공황 발작 · 그 방 · 일이 지나가면 성격.</summary>
    private void HeartStep(Sim sim)
    {
        if (_life == null) return;
        long t = sim.Tick;
        const double h = 1.0 / 60;
        foreach (var f in sim.Facts.Since(_heartCursor))
            if (f.Kind is FactKind.Extinguished or FactKind.Rescue or FactKind.Patched && f.Actor >= 0 && f.Actor < Agents.Count && Agents[f.Actor].IsPerson)
                Agents[f.Actor].C.CopedAt = f.Tick;   // 위험에 맞섰다
        _heartCursor = sim.Facts.Total;
        foreach (var a in Agents)
        {
            if (!a.IsPerson) continue;
            ref var c = ref a.C;
            ref var b = ref _life[c.Body];
            if (b.Alive == 0) continue;
            bool support = HasSupport(a.Id);
            // 충격의 몫
            float peak = Math.Max(c.FearMax, c.Fear);
            c.FearMax = 0;
            if (peak > 0.6f)
            {
                var (cause, kind) = FearSource(a);
                Shock(sim, a, (peak - 0.6) / 0.4 * 0.15, cause, kind, support);   // 겁에 질린 1분 (0.9면 0.11) — 몇 분이면 충격 0.3 남짓
            }
            bool near = b.Hypoxia > 0.5f || b.Arrest != 0 || b.Conscious == 0 && (b.Burn + b.Blunt > 0.3f || b.BloodL < 4);
            if (near) Shock(sim, a, 0.1, b.Arrest != 0 ? b.ArrestCause : b.HurtCause != 0 ? b.HurtCause : FearSource(a).cause,
                b.Arrest != 0 ? ShockKind.NearDeath : b.Hypoxia > 0.5f && b.Burn + b.Blunt < 0.3f ? ShockKind.Air : ShockKind.Hurt, support);   // 심장이 멎었던 일은 '죽을 뻔했던 일'
            if (Life.FeltPain(b) > 0.7) Shock(sim, a, 0.03, b.HurtCause, ShockKind.Hurt, support);
            // 마음의 흐름
            float d0 = c.Depress, x0 = c.Anxiety;
            var x = new HeartIn
            {
                Sad = Math.Max(c.Sad, _soc?.GriefOf(a.Id) ?? 0), Weary = c.Weary, Stress = c.Stress, SleepLoss = c.SleepLoss, Lonely = c.Lonely, Joy = c.Joy, Support = support,
            };
            HeartKinetics(ref c, x, h);
            if (d0 < 0.3f && c.Depress >= 0.3f) HeartNote(sim, a, HeartEvent.Depressed, "마음이 가라앉았다 — 일도 사람도 귀찮다");
            if (x0 < 0.3f && c.Anxiety >= 0.3f) HeartNote(sim, a, HeartEvent.Anxious, "늘 마음이 졸인다 — 무언가 잘못될 것만 같다");
            // 일이 지나갔다 (충격이 가장 컸던 때의 반 아래): 성격이 바뀐다
            if (c.ShockPeak > 0 && c.Shock < 0.5f * c.ShockPeak)
            {
                if (c.ShockPeak >= 0.3f) Drift(sim, a);
                c.ShockPeak = 0; c.ShockTop = 0;
            }
            // 외상 뒤 성장: 크게 앓던 외상이 곁사람과 함께 나았다
            if (c.TraumaHigh != 0 && c.Trauma < 0.1f)
            {
                c.TraumaHigh = 0;
                if (support)
                {
                    c.Agree = Math.Min(Math.Max(c.Agree, c.Agree0 + 0.2f), c.Agree + 0.03f); c.Kindness = c.Agree;
                    Grew++;
                    HeartNote(sim, a, HeartEvent.Grew, $"{ShockText(c.ShockKind)}을 견디고 남의 아픔을 더 헤아리게 됐다");
                }
            }
            bool asleep = b.Asleep != 0 && b.Conscious != 0;
            if (asleep) { Nightmare(sim, a); if (b.Asleep != 0) SleepTalkRoll(sim, a); continue; }
            if (c.Up == 0) continue;
            HumRoll(sim, a);   // 소리 단계 S2: 기분 좋은 이의 흥얼거림
            // 그 방에 들어섰다 (여섯 시간에 한 번)
            if (c.Trauma > 0.3f && c.ShockRoom >= 0 && RoomOf(c.Cell) == c.ShockRoom && (c.RoomFlashAt == 0 || t - c.RoomFlashAt > Time.Hours(6)))
            {
                c.RoomFlashAt = t;
                c.Fear = Math.Max(c.Fear, 0.3f + 0.4f * c.Trauma);
            }
            PanicAttackRoll(sim, a);
        }
    }

    /// <summary>자는 1분마다 0.25/60 × max(외상, 충격의 반) — 악몽에 깬다 (두 시간에 한 번까지 · 잠이 얕아진다 · 소리친다 60dB).</summary>
    private void Nightmare(Sim sim, Agent a)
    {
        ref var c = ref a.C;
        double load = Math.Max(c.Trauma, 0.5 * c.Shock);
        if (load < 0.05 || c.NightmareAt > 0 && sim.Tick - c.NightmareAt < Time.Hours(2)) return;
        if (Rng.Unit(sim.Seed, Rng.Key("악몽"), sim.Tick, a.Id) >= 0.25 / 60 * load * (1 - 0.5 * Life.SleepAid(_life![c.Body]))) return;   // 수면제는 악몽을 반으로
        c.NightmareAt = sim.Tick;
        Nightmares++;
        int cause = sim.Causes.Add(CauseKind.Person, $"{Ko.I(NameOf(a.Id))} 악몽에 깼다 — {ShockText(c.ShockKind)}", sim.Tick, c.Cell, c.ShockCause, 0, a.Id);
        Wake(sim, a, "악몽", cause);
        c.SleepLoss = Math.Min(1, c.SleepLoss + 0.3f);
        c.Fear = Math.Max(c.Fear, 0.5f);
        _snd?.Emit(sim, c.Cell, 60, SoundKind.Scream, cause, a.Id);
        sim.Facts.Emit(sim.Tick, FactKind.Heart, c.Cell, (float)HeartEvent.Nightmare, cause, a.Id);
        a.LastThought = "악몽을 꿨다"; a.LastThoughtTick = sim.Tick;
    }

    /// <summary>공황 발작: 깨어 있고 둘레에 위험이 없을 때 한 시간마다 0.06 × 불안² — 그 자리에 2~5분 주저앉는다 (겁 0.8 · 긴장 +0.2).</summary>
    private void PanicAttackRoll(Sim sim, Agent a)
    {
        ref var c = ref a.C;
        long t = sim.Tick;
        if (c.Anxiety < 0.2f || c.Mode != 0 || c.Fear >= 0.5f || c.AttackUntil > t || c.AttackAt > 0 && t - c.AttackAt < Time.Hours(1)) return;
        if (c.Goal is GoalKind.Flee or GoalKind.Evacuate or GoalKind.Panic or GoalKind.Down || BelievedRoomBad(a, RoomOf(c.Cell))) return;
        if (Rng.Unit(sim.Seed, Rng.Key("공황 발작"), t, a.Id) >= 0.06 / 60 * c.Anxiety * c.Anxiety) return;
        c.AttackAt = t;
        c.AttackUntil = t + Time.Minutes(2) + (long)(Rng.Unit(sim.Seed, Rng.Key("발작 길이"), t, a.Id) * Time.Minutes(3));
        c.Fear = Math.Max(c.Fear, 0.8f);
        c.Stress = Math.Min(1, c.Stress + 0.2f);
        PanicAttacks++;
        int root = c.Trauma > 0.2f && c.ShockCause != 0 ? c.ShockCause : 0;
        int cause = sim.Causes.Add(CauseKind.Person, $"{Ko.I(NameOf(a.Id))} 까닭 없이 숨이 가빠 주저앉았다 (공황 발작){(root != 0 ? " — " + ShockText(c.ShockKind) + " 뒤로" : "")}", t, c.Cell, root, 0, a.Id);
        c.GoalCause = cause;
        a.Plan.Clear(); a.Path.Clear(); c.StepIndex = 0;   // 하던 걸음을 멈춘다 (생각이 주저앉기를 고른다)
        sim.Facts.Emit(t, FactKind.Heart, c.Cell, (float)HeartEvent.Attack, cause, a.Id);
        a.LastThought = "숨이 안 쉬어진다"; a.LastThoughtTick = t;
    }

    /// <summary>일이 지나갔다: 맞섰으면 (그 일 한 시간 앞부터 불 끄기 · 구하기 · 막기) 용기 +0.06 × 충격 · 아니면 신경성 +0.08 × 충격 · 용기 −0.06 × 충격 (처음 성격에서 ±0.2 안).</summary>
    private void Drift(Sim sim, Agent a)
    {
        ref var c = ref a.C;
        float s = c.ShockPeak;
        static float Move(float v, float d, float v0) => d >= 0 ? Math.Min(Math.Max(v, v0 + 0.2f), v + d) : Math.Max(Math.Min(v, v0 - 0.2f), v + d);
        if (c.CopedAt > 0 && c.CopedAt >= c.ShockAt - Time.Hours(1))
        {
            c.Brave = Math.Clamp(Move(c.Brave, 0.06f * s, c.Brave0), 0.02f, 0.98f);
            Hardened++;
            HeartNote(sim, a, HeartEvent.Hardened, $"{ShockText(c.ShockKind)}을 맞서 넘기고 단단해졌다");
        }
        else
        {
            c.Neuro = Math.Clamp(Move(c.Neuro, 0.08f * s, c.Neuro0), 0.02f, 0.98f); c.Calm = 1 - c.Neuro;
            c.Brave = Math.Clamp(Move(c.Brave, -0.06f * s, c.Brave0), 0.02f, 0.98f);
            Fearful++;
            HeartNote(sim, a, HeartEvent.Fearful, $"{ShockText(c.ShockKind)} 뒤로 겁이 많아졌다");
        }
    }

    /// <summary>마음의 변화를 원인 마디 · 사실로 남긴다 (그 일에 잇는다).</summary>
    private void HeartNote(Sim sim, Agent a, HeartEvent e, string what)
    {
        ref var c = ref a.C;
        if (e == HeartEvent.Depressed) Depressions++;
        if (e == HeartEvent.Anxious) Anxieties++;
        int cause = sim.Causes.Add(CauseKind.Person, $"{Ko.I(NameOf(a.Id))} {what}", sim.Tick, c.Cell, c.Trauma > 0.2f || e is HeartEvent.Fearful or HeartEvent.Hardened or HeartEvent.Grew ? c.ShockCause : 0, 0, a.Id);
        sim.Facts.Emit(sim.Tick, FactKind.Heart, c.Cell, (float)e, cause, a.Id);
    }

    /// <summary>⑧ 마음이 고르는 것: 공황 발작 중이면 그 자리에 주저앉고 · 우울이 깊으면 (0.5 넘게) 일하는 시간에 제 침대에 틀어박힌다.</summary>
    private void HeartOptions(Agent a, int here, bool workHours)
    {
        ref var c = ref a.C;
        if (c.AttackUntil > _nowTick) Add(GoalKind.Attack, -1, here, 12, c.GoalCause);
        if (c.Depress > 0.5f && workHours && c.Bed >= 0 && !Taken(a, c.Bed)) Add(GoalKind.Withdraw, c.Bed, c.Bed, 1.5f * c.Depress);
    }

    private bool BuildHeart(Agent a, Option o, List<PlanStep> plan, int here)
    {
        if (o.Goal == GoalKind.Attack)
        {
            plan.Add(Use(UseKind.None, Math.Max(1, a.C.AttackUntil - _nowTick)));
            return true;
        }
        if (o.Target != here) plan.Add(Go(o.Target));
        plan.Add(Use(UseKind.None, Time.Minutes(30)));
        return true;
    }

    // ── (다) 낫는 길 ──

    /// <summary>마음이 가벼워진다 (친구와의 말 · 주컴퓨터 · 상담이 요청한다): 우울 · 외상 · 불안을 덜어 낸다.</summary>
    public void Eased(int agent, float depress, float trauma, float anxiety)
    {
        if (agent < 0 || agent >= Agents.Count || !Agents[agent].IsPerson) return;
        ref var c = ref Agents[agent].C;
        c.Depress = Math.Max(0, c.Depress - depress); c.Trauma = Math.Max(0, c.Trauma - trauma); c.Anxiety = Math.Max(0, c.Anxiety - anxiety);
    }

    /// <summary>
    /// 수면제가 필요한가 (주컴퓨터가 읽어 로봇을 보낸다 · 돌보는 이가 준다): 깨어 있고 잘 때가 다가오는데 (졸림 0.5 넘게 · 일하는 중이 아니다) — 잠의 질이 0.5 아래거나 이틀 안에 악몽에 깼다 ·
    /// 스무 시간 안에 먹지 않았다 · 약품고에 있다.
    /// </summary>
    public bool NeedsSleepAid(long now, Agent a)
    {
        if (!a.IsPerson || _life == null) return false;
        ref var c = ref a.C;
        var b = _life[c.Body];
        if (b.Alive == 0 || b.Conscious == 0 || b.Asleep != 0 || c.Sleepy < 0.5f || c.Goal == GoalKind.Work || now - b.HypnoticAt < Time.Hours(20)) return false;   // 잘 때가 되었다 (일하는 중에는 주지 않는다)
        bool bad = c.SleepLoss > 0.5f || c.NightmareAt > 0 && now - c.NightmareAt < Time.Hours(48);
        return bad && TotalOf(iHypnotic) > 0;
    }

    private static string SleepWhy(long now, in AgentCore c) => c.NightmareAt > 0 && now - c.NightmareAt < Time.Hours(48) ? "악몽에 깬다" : "잠을 설친다";

    /// <summary>수면제를 준다 (10mg — 네 시간 안에 진통제를 맞았으면 솜씨 있는 이는 반만 · 서툰 이는 모르고 다 준다: 숨이 더 얕아진다).</summary>
    private void SleepPill(Sim sim, Agent a, int vb, string who, bool skilled, int parent)
    {
        int va = AgentOfBody(vb);
        if (va < 0 || !NeedsSleepAid(sim.Tick, Agents[va]) || !TakeOne(a.C.Cell, iHypnotic, sim)) return;
        bool opioid = sim.Tick - _life!.Bodies[vb].OpioidAt < Time.Hours(4);
        double mg = skilled && opioid ? 5 : 10;
        string note = opioid ? skilled ? " · 진통제를 맞은 뒤라 반만" : " · 진통제를 맞은 것을 몰랐다" : "";
        int k = sim.Causes.Add(a.IsPerson ? CauseKind.Person : CauseKind.Robot, $"{Ko.I(NameOf(a.Id))} {who}에게 수면제 {mg:0}mg을 주었다 — {SleepWhy(sim.Tick, Agents[va].C)}{note}",
            sim.Tick, a.C.Cell, parent, 0, a.Id);
        _life.Give(sim, vb, Drug.Hypnotic, mg, k);
        SleepPills++;
    }

    /// <summary>돌보러 왔는데 다친 데 · 아픔 · 병이 없고 잠만 설친다: 수면제만 주고 마친다 (진단하지 않는다).</summary>
    private bool SleepOnly(Sim sim, Agent a, int vb)
    {
        var b = _life!.Bodies[vb];
        int va = AgentOfBody(vb);
        if (va < 0 || !NeedsSleepAid(sim.Tick, Agents[va]) || Life.NeedsDressing(b) || Life.FeltPain(b) >= 0.4 || Life.Ailment(b) != Ail.None || HasDrugFor(b)) return false;
        bool skilled = !a.IsPerson || a.C.SkMedic >= 0.5f;
        SleepPill(sim, a, vb, NameOf(va), skilled, Agents[va].C.ShockCause);   // 그 일이 잠을 앗았다
        if (a.C.AssignedGoal == GoalKind.Treat && a.C.AssignedTarget == vb) a.C.AssignedUntil = sim.Tick;   // 맡은 일을 마쳤다
        return true;
    }

    /// <summary>상담 계획: 그 사람 곁으로 가서 30분 이야기를 들어 준다 (그 사람은 기다린다 — 돌봄을 기다리듯).</summary>
    private bool BuildCounsel(Agent a, Option o, List<PlanStep> plan)
    {
        int vb = o.Target;
        if (vb < 0 || vb >= _life!.Bodies.Count || _life.Bodies[vb].Alive == 0 || vb == a.C.Body) return false;
        int cell = _life.Bodies[vb].Cell;
        plan.Add(Go(cell, 1));
        plan.Add(Use(UseKind.Counsel, Time.Minutes(30), cell, vb));
        return true;
    }

    /// <summary>상담을 마쳤다: 우울 −0.1 · 외상 −0.08 · 불안 −0.1 × (0.5 + 들어 준 이의 친화) — 원인 마디 '이야기를 들어 주었다' · 사실 (⑨가 정을 셈한다).</summary>
    private void CounselDone(Sim sim, Agent a, PlanStep st)
    {
        ref var c = ref a.C;
        int pa = AgentOfBody(st.Target);
        if (pa < 0 || _life![st.Target].Alive == 0) return;
        float k = 0.5f + (a.IsPerson ? c.Agree : 0.3f);
        Eased(pa, 0.1f * k, 0.08f * k, 0.1f * k);
        Counsels++;
        ref var pc = ref Agents[pa].C;
        int cause = sim.Causes.Add(CauseKind.Person, $"{Ko.I(NameOf(a.Id))} {NameOf(pa)}의 이야기를 들어 주었다 — 한결 가벼워졌다", sim.Tick, c.Cell, c.GoalCause != 0 ? c.GoalCause : pc.ShockCause, 0, a.Id);
        sim.Facts.Emit(sim.Tick, FactKind.Heart, c.Cell, (float)HeartEvent.Counseled, cause, a.Id, pa);
        Agents[pa].LastThought = "이야기를 하고 나니 한결 낫다"; Agents[pa].LastThoughtTick = sim.Tick;
        if (c.AssignedGoal == GoalKind.Counsel && c.AssignedTarget == st.Target) c.AssignedUntil = sim.Tick;   // 맡은 일을 마쳤다
    }

    private void SaveHeart(StateWriter w)
    {
        w.I64(Shocks); w.I64(Nightmares); w.I64(Flashbacks); w.I64(PanicAttacks); w.I64(Depressions); w.I64(Anxieties); w.I64(Hardened); w.I64(Fearful); w.I64(Grew); w.I64(SleepPills); w.I64(Counsels);
        w.I64(_heartCursor); w.Arr(_deathSeen.OrderBy(x => x).ToArray()); w.Arr(_deathKnown.OrderBy(x => x).ToArray());
    }

    private void LoadHeart(StateReader r)
    {
        Shocks = r.I64(); Nightmares = r.I64(); Flashbacks = r.I64(); PanicAttacks = r.I64(); Depressions = r.I64(); Anxieties = r.I64(); Hardened = r.I64(); Fearful = r.I64(); Grew = r.I64(); SleepPills = r.I64(); Counsels = r.I64();
        _heartCursor = r.I64(); _deathSeen.Clear(); foreach (long v in r.Arr<long>()) _deathSeen.Add(v);
        _deathKnown.Clear(); foreach (long v in r.Arr<long>()) _deathKnown.Add(v);
    }
}
