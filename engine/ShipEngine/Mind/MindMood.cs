namespace ShipEngine;

/// <summary>
/// 기분 · 위기 뒤 (깊이 고침 DF7 — MIND 14단 '기분' 당김 · 근거 docs/ENGINE_DEPTH_REVIEW.md §4-5 · §4-6 · §5): 감정 (분 · 시간 단위) 아래에 며칠 가는 층이 있고 ·
/// 방금 겪은 위기는 그날의 이야기 · 곁사람 찾기가 된다.
/// <para><b>기분이 쌓인다</b> — 그 사람이 겪은 감정에서만 (이야기꾼 없음 — 무엇도 기분을 직접 쓰지 않는다): 겪은 일 = 한 틱에 0.12 넘게 솟은 감정이 그 솟은 몫만큼 한 번
/// (기쁨 1배 · 겁 0.5 · 슬픔 0.8 · 화 0.4배 — 나쁜 일은 신경성 0.6~1.4배: 축하 · 반가운 편지 · 구해짐 · 불을 봄 · 다툼 · 거절 · 죽음을 앎) ·
/// 바닥 위로 0.15 넘게 오래 솟아 있는 감정이 시간에 그 넘친 몫의 0.5배 · 낮은 몫도 시간에 0.08배 (잔잔한 일은 감정에서 끝나고 · 잦으면 조금 남는다) ·
/// 몸 (배고픔 · 졸림 · 아픔) · 오래 높은 심심함 · 외로움이 가라앉은 몫을 천천히 더한다 (시간에 넘친 몫의 0.1배).</para>
/// <para><b>흐려진다</b>: 밝은 몫은 하루 · 가라앉은 몫은 하루 반 (신경성 0.7~1.3배)에 반으로 — 큰 일 (축하 · 불 · 죽음)은 하루~며칠 · 작은 일은 분 단위로 남는다.</para>
/// <para><b>감정의 바닥</b>: 밝은 기분 → 기쁨 (밝은 몫의 반 · 가라앉았으면 덜) · 가라앉은 기분 · 외로움 → 슬픔 (가라앉음) ·
/// 배고픔 · 졸림 · 목마름 · 아픔 · 오래 높은 심심함 → 화 (짜증 — 0.45까지: 몸싸움 문턱 0.5는 겪은 일이 넘긴다).</para>
/// <para><b>판단을 바꾼다</b>: 들뜬 이는 말을 더 걸고 · 어울리러 더 가고 · 가라앉은 이는 덜 건다 (MindTalk · MindDecide).</para>
/// <para><b>위기 뒤</b>: 불 · 구멍 · 진공을 처음 믿게 된 이 (본 · 들은 소리 · 냄새 — 제 몸으로 · 아니면 말 · 방송으로)가 그 일을 여섯 시간 기억한다 —
/// 함께 쉬는 이에게 먼저 그 이야기를 꺼내고 (모르는 이에게는 알리고 · 말로만 들은 이는 겪은 이에게 묻는다 — 갓 일어난 일일수록 · 덜 꺼냈을수록 · 겁이 남았을수록) ·
/// 들은 이는 그 일을 안다 (그도 이야기를 옮긴다) · 겪은 이는 한 시간 동안 일손을 덜 잡고 혼자 쉬기보다 사람 곁을 찾는다.</para>
/// </summary>
public sealed partial class Mind
{
    public const float MoodBite = 0.15f, MoodJump = 0.12f;   // 바닥 위로 이만큼 오래 솟은 감정 · 한 틱에 이만큼 솟은 감정 (겪은 일)이 기분에 남는다
    public const double MoodGoodHalfH = 24, MoodBadHalfH = 36, CrisisTalkH = 6;

    /// <summary>기분이 올리는 감정의 바닥 (순수): 기쁨 · 슬픔 (가라앉음) · 화 (짜증).</summary>
    public static (float joy, float sad, float anger) MoodFloors(in AgentCore c, float pain)
    {
        float joy = 0.5f * c.MoodGood * (1 - 0.5f * c.MoodBad);
        float sad = 0.4f * c.MoodBad + 0.5f * Math.Max(0, c.Lonely - 0.5f);
        float irk = 1.5f * Math.Max(0, c.Hunger - 0.6f) + 1.3f * Math.Max(0, c.Sleepy - 0.7f) + 0.6f * Math.Max(0, c.Thirst - 0.5f)
                  + 0.9f * Math.Max(0, c.BoredLong - 0.6f) + 0.4f * pain + 0.15f * c.MoodBad;
        return (joy, sad, Math.Min(0.45f, irk * (0.6f + 0.8f * c.Neuro)));
    }

    /// <summary>
    /// 한 틱의 기분 (순수 — 시험이 몇 시간을 한꺼번에 볼 수 있게): 오래 높은 심심함을 따라가고 · 겪은 일이 쌓이고 · 흐려지고 · 감정의 바닥을 올린다.
    /// 겪은 일 = 지난 틱 뒤로 갑자기 솟은 감정 (0.12 넘게 — 축하 · 반가운 편지 · 구해짐 · 불을 봄 · 다툼 · 거절 · 죽음을 앎)이 한 번 · 바닥 위로 오래 솟아 있는 감정 (0.15 넘게)이 시간마다 조금.
    /// 바닥은 기분이 이미 설명하는 몫이라 쌓임에 넣지 않는다 (제 바닥으로 기분을 다시 키우지 않는다).
    /// </summary>
    public static void MoodKinetics(ref AgentCore c, float pain, double h)
    {
        c.BoredLong += (float)((c.Bored - c.BoredLong) * Math.Min(1, h / 3));
        var (jf, sf, af) = MoodFloors(c, pain);
        double joy = Math.Max(0, c.Joy - jf), sad = Math.Max(0, c.Sad - sf), anger = Math.Max(0, c.Anger - af);
        static double Jump(float now, float was) => now - was >= MoodJump ? now - was : 0;
        double vul = 0.6 + 0.8 * c.Neuro;
        double good = Jump(c.Joy, c.JoyWas)
                    + h * (0.08 * joy + 0.5 * Math.Max(0, joy - MoodBite));
        double bad = (0.5 * Jump(c.Fear, c.FearWas) + 0.8 * Jump(c.Sad, c.SadWas) + 0.4 * Jump(c.Anger, c.AngerWas)) * vul
                   + h * (0.08 * Math.Max(Math.Max(sad, 0.5 * anger), Math.Max(c.Fear, 0.5 * c.Stress))
                        + 0.5 * (Math.Max(0, c.Fear - MoodBite) + Math.Max(0, sad - MoodBite) + 0.5 * Math.Max(0, anger - 0.3) + 0.5 * Math.Max(0, c.Stress - 0.4))
                        + 0.1 * (Math.Max(0, c.Hunger - 0.6) + Math.Max(0, c.Sleepy - 0.7) + Math.Max(0, c.BoredLong - 0.6) + Math.Max(0, c.Lonely - 0.6) + 0.5 * pain));
        c.MoodGood = (float)Math.Clamp(c.MoodGood * Math.Exp(-0.6931 * h / MoodGoodHalfH) + good * (1 - c.MoodGood), 0, 1);
        c.MoodBad = (float)Math.Clamp(c.MoodBad * Math.Exp(-0.6931 * h / (MoodBadHalfH * (0.7 + 0.6 * c.Neuro))) + bad * (1 - c.MoodBad), 0, 1);
        (jf, sf, af) = MoodFloors(c, pain);
        if (c.Joy < jf) c.Joy = jf;
        if (c.Sad < sf) c.Sad = sf;
        if (c.Anger < af) c.Anger = af;
        c.JoyWas = c.Joy; c.FearWas = c.Fear; c.SadWas = c.Sad; c.AngerWas = c.Anger;
    }

    /// <summary>기분 한 마디 (화면 — 사람 카드): 들떴다 · 기분이 좋다 · 가라앉았다 · 짜증이 난다 (없으면 빈 글).</summary>
    public static string MoodWord(in AgentCore c)
    {
        if (c.MoodBad >= 0.25f && c.MoodBad > c.MoodGood) return c.MoodBad >= 0.5f ? "마음이 무겁다" : "기분이 가라앉았다";
        if (c.MoodGood >= 0.25f) return c.MoodGood >= 0.5f ? "들떴다" : "기분이 좋다";
        if (c.Anger >= 0.2f && c.Anger <= 0.45f && c.Hunger + c.Sleepy > 1.2f) return "짜증이 난다";
        return "";
    }

    /// <summary>들뜬 · 가라앉은 이가 말을 거는 몫 (둘 중 더 들뜬 이 · 둘 다 가라앉았으면 덜).</summary>
    private static float MoodTalk(in AgentCore a, in AgentCore b) => (1 + 0.8f * Math.Max(a.MoodGood, b.MoodGood)) * (1 - 0.4f * Math.Min(a.MoodBad, b.MoodBad));

    // ───────────────────────── 위기 뒤 ─────────────────────────

    private static Topic CrisisOf(BeliefKind k) => k switch
    {
        BeliefKind.Fire => Topic.Fire,
        BeliefKind.Breach or BeliefKind.Vacuum => Topic.Breach,
        _ => Topic.Small,
    };

    /// <summary>위기를 믿게 됐다 (<see cref="Learn"/> — 감각 · 말 · 방송): 같은 일 (같은 갈래 · 두 시간 안이거나 같은 원인 — 남아 있는 구멍을 다시 봐도 새 일이 아니다)이면 제 몸으로 겪었는지만 더하고 · 새 일이면 그 일을 기억한다.</summary>
    private void NoteCrisis(Agent a, long tick, BeliefKind kind, int cell, int cause, BeliefSource src)
    {
        var t = CrisisOf(kind);
        if (!a.IsPerson || t == Topic.Small) return;
        ref var c = ref a.C;
        byte saw = (byte)(src is BeliefSource.Saw or BeliefSource.Heard or BeliefSource.Smelled or BeliefSource.Felt ? 1 : 0);
        if (c.CrisisAt > 0 && c.CrisisTopic == t && (tick - c.CrisisAt < Time.Hours(2) || cause != 0 && cause == c.CrisisCause))
        {
            c.CrisisSaw |= saw;
            if (c.CrisisCause == 0) c.CrisisCause = cause;
            return;
        }
        c.CrisisTopic = t; c.CrisisAt = tick; c.CrisisCause = cause; c.CrisisCell = cell; c.CrisisSaw = saw; c.CrisisTold = 0;
    }

    /// <summary>갓 일어난 위기를 아는가 (여섯 시간 안) · 그 무게 (갓 일어났을수록 1).</summary>
    private static float CrisisFresh(in AgentCore c, long now) =>
        c.CrisisAt <= 0 || now - c.CrisisAt > Time.Hours(CrisisTalkH) ? 0 : 1 - (float)((now - c.CrisisAt) / (double)Time.Hours(CrisisTalkH));

    /// <summary>둘이 같은 위기를 아는가 (같은 갈래 · 두 시간 안에 알았다).</summary>
    private static bool SameCrisis(in AgentCore x, in AgentCore y) => x.CrisisAt > 0 && y.CrisisAt > 0 && x.CrisisTopic == y.CrisisTopic && Math.Abs(x.CrisisAt - y.CrisisAt) < Time.Hours(2);

    /// <summary>
    /// 방금 겪은 일 이야기의 거리 (PickTopic): 둘 중 갓 일어난 위기를 아는 이가 있으면 — 모르는 이에게는 알리고 (1) · 말로만 들은 이는 겪은 이에게 묻고 (2) · 둘 다 겪었으면 함께 이야기한다 (3).
    /// 무게 = 6 × 갓 일어난 몫 ÷ (1 + 나눈 수) × (1 + 겁 + 가라앉은 기분).
    /// </summary>
    private void CrisisTopic(Sim sim, int i, int j, List<TalkOption> opts)
    {
        ref var ci = ref Agents[i].C; ref var cj = ref Agents[j].C;
        float fi = CrisisFresh(ci, sim.Tick), fj = CrisisFresh(cj, sim.Tick);
        if (fi <= 0 && fj <= 0) return;
        int x = fi >= fj ? i : j, y = x == i ? j : i;   // 더 갓 겪은 이 (그 일을 꺼낸다)
        ref var cx = ref Agents[x].C; ref var cy = ref Agents[y].C;
        bool both = SameCrisis(cx, cy) && CrisisFresh(cy, sim.Tick) > 0;
        int kind = !both ? 1 : cx.CrisisSaw != cy.CrisisSaw ? 2 : 3;
        int sp = kind == 2 ? (cx.CrisisSaw == 0 ? x : y) : x;   // 말로만 들은 이가 묻는다
        int knower = kind == 2 ? (sp == x ? y : x) : x;
        ref var ck = ref Agents[knower].C;
        int told = both ? Math.Max(cx.CrisisTold, cy.CrisisTold) : cx.CrisisTold;
        float w = 6 * Math.Max(fi, fj) / (1 + told) * (1 + Math.Max(cx.Fear, cy.Fear) + Math.Max(cx.MoodBad, cy.MoodBad));
        opts.Add(new(ck.CrisisTopic, w, -1, ck.CrisisCause, ck.CrisisCell, sp, kind));
    }

    /// <summary>위기 이야기를 나눴다 (TalkStep): 둘 다 한 번 더 나눴고 · 알림이면 모르던 듣는 이가 그 일을 안다 (들은 것 — 그도 옮긴다 · 조금 놀란다).</summary>
    private void HearCrisis(int sp, int li, int kind)
    {
        ref var cs = ref Agents[sp].C; ref var cl = ref Agents[li].C;
        if (kind == 1)
        {
            cl.CrisisTopic = cs.CrisisTopic; cl.CrisisAt = cs.CrisisAt; cl.CrisisCause = cs.CrisisCause; cl.CrisisCell = cs.CrisisCell; cl.CrisisSaw = 0; cl.CrisisTold = 0;
            cl.Fear = Math.Max(cl.Fear, 0.2f * (0.5f + cl.Neuro));
        }
        if (cs.CrisisTold < 200) cs.CrisisTold++;
        if (cl.CrisisTold < 200) cl.CrisisTold++;
    }

    /// <summary>
    /// 겪은 이의 쉼 (MindDecide 선택지): 제 몸으로 위기를 겪고 한 시간이 안 됐으면 — 일손을 덜 잡고 (0.4배) · 깬 이가 있으면 사람 곁을 찾는다 (겁 · 가라앉은 기분만큼) ·
    /// 겁이 남았으면 혼자 쉬기를 덜 고른다. 말로만 들은 이는 아직 그 이야기를 못 나눴으면 사람을 찾아 묻는다.
    /// </summary>
    private void CrisisOptions(Agent a)
    {
        ref var c = ref a.C;
        long now = _nowTick;
        if (c.CrisisAt <= 0 || now - c.CrisisAt > Time.Hours(1) || !SomeoneFree(a)) return;
        float fresh = 1 - (float)((now - c.CrisisAt) / (double)Time.Hours(1));
        if (c.CrisisSaw != 0) Add(GoalKind.Socialize, -1, -1, (0.8f + c.Fear + c.MoodBad + 0.3f * c.Extra) * fresh);
        else if (c.CrisisTold == 0) Add(GoalKind.Socialize, -1, -1, (0.5f + 0.4f * c.Extra + 0.5f * c.Neuro) * fresh);
    }

    /// <summary>겪은 이의 쉼이 다른 선택지를 누른다 (일 0.4배 · 겁이 남았으면 혼자 쉬기 0.5배).</summary>
    private float CrisisDamp(Agent a, GoalKind g)
    {
        ref var c = ref a.C;
        if (!a.IsPerson || c.CrisisSaw == 0 || c.CrisisAt <= 0 || _nowTick - c.CrisisAt > Time.Hours(1)) return 1;
        return g == GoalKind.Work ? 0.4f : g == GoalKind.Relax && (c.Fear > 0.2f || c.MoodBad > 0.2f) ? 0.5f : 1;
    }
}
