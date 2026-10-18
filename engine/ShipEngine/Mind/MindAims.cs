namespace ShipEngine;

/// <summary>⑧ 인생 목표 (바퀴 3) — 사람마다 하나.</summary>
public enum Aim : byte { None, Master, Home, Love, Guard, Know }

/// <summary>
/// ⑧ 인생 목표 (docs/ENGINE.md ⑧ 바퀴 3). 사람마다 하나 — 성격에서 무게대로 뽑는다 (따로 갈래 낸 난수):
/// 솜씨(성실) — 맡은 일의 솜씨를 끝까지 익힌다: 일 · 고치기 · 정비 · 만들기를 1.25배 더 고르고 맡은 일의 솜씨가 1.5배 빨리 는다 · 으뜸 솜씨가 끝(0.99)에 닿으면 이룬다 /
/// 귀향(신경성 · 외로움) — 항해를 마치고 돌아간다: 점화마다 기쁘고(나아간다) · 놓친 점화에 크게 무너진다 · 도착 점화에 이룬다 /
/// 짝(외향 · 친화) — 평생 짝을 찾는다: 어울리기 1.3배 · 짝을 맺으면 이루고 · 헤어지면 잃는다 /
/// 배를 지킨다(용기) — 불 · 구멍 · 쓰러진 이 · 버티는 문 · 다친 이에 1.4배 먼저 나선다 · 제 손으로 불을 끄거나 구멍을 막거나 사람을 구하면 이룬다 /
/// 알아낸다(열림) — 살피러 가기 1.3배 · 연구 솜씨 1.5배 · 다른 배 · 별 폭발 · 처음 보는 일에 기쁘다 · 연구 솜씨 0.7에 이룬다.
/// 이루면 기쁨 +0.4 · 슬픔 · 긴장이 준다 · 잃으면 슬픔 +0.35 · 긴장 +0.2 — 사실 Aim (크기 1 이룸 · −1 흔들림 · Other: 목표).
/// </summary>
public sealed partial class Mind
{
    public long AimsMet, AimsShaken;
    private long _aimCursor;

    public static string AimName(Aim a) => a switch
    {
        Aim.Master => "솜씨", Aim.Home => "귀향", Aim.Love => "짝", Aim.Guard => "배를 지킨다", Aim.Know => "알아낸다", _ => "",
    };

    /// <summary>성격에서 인생 목표를 뽑는다 — 성실 → 솜씨 · 신경성 → 귀향 · 외향 × 친화 → 짝 · 용기 → 배를 지킨다 · 열림 → 알아낸다 (제곱한 무게 — 두드러진 성격이 고른다).</summary>
    private static Aim PickAim(ulong seed, int person, in AgentCore c)
    {
        Span<float> w = stackalloc float[6];
        w[(int)Aim.Master] = c.Consc * c.Consc;
        w[(int)Aim.Home] = c.Neuro * c.Neuro;
        w[(int)Aim.Love] = c.Extra * c.Agree * 1.4f;
        w[(int)Aim.Guard] = c.Brave * c.Brave;
        w[(int)Aim.Know] = c.Open * c.Open;
        float sum = 0; for (int i = 1; i < 6; i++) sum += w[i];
        float u = (float)Rng.Unit(seed, Rng.Key("인생 목표"), person, 0) * sum;
        for (int i = 1; i < 6; i++) { u -= w[i]; if (u <= 0) return (Aim)i; }
        return Aim.Know;
    }

    /// <summary>인생 목표가 목표 점수에 주는 무게.</summary>
    private static float AimBoost(in AgentCore c, GoalKind g) => (Aim)c.Aim switch
    {
        Aim.Guard when g is GoalKind.FightFire or GoalKind.Patch or GoalKind.Rescue or GoalKind.Brace or GoalKind.Treat => 1.4f,
        Aim.Master when g is GoalKind.Work or GoalKind.Repair or GoalKind.Maintain or GoalKind.Craft => 1.25f,
        Aim.Love when g == GoalKind.Socialize => 1.3f,
        Aim.Know when g == GoalKind.Investigate => 1.3f,
        _ => 1f,
    };

    /// <summary>솜씨 이름 하나를 익힐 때의 빠르기 (인생 목표 '솜씨'면 맡은 일의 솜씨 · '알아낸다'면 연구가 1.5배).</summary>
    private float Practice(in AgentCore c, string skill)
    {
        if ((Aim)c.Aim == Aim.Know && skill == "연구") return 1.5f;
        if ((Aim)c.Aim == Aim.Master && c.Role < RoleNames.Length && P.Roles[RoleNames[c.Role]].Skills.ContainsKey(skill)) return 1.5f;
        return 1f;
    }

    /// <summary>'솜씨' 목표가 익히려는 솜씨: 맡은 일의 솜씨 가운데 지금 가장 높은 것 (없으면 고치기).</summary>
    private string AimSkill(in AgentCore c)
    {
        if (c.Role >= RoleNames.Length) return "고치기";
        string best = "고치기"; float bv = -1;
        foreach (var sk in P.Roles[RoleNames[c.Role]].Skills.Keys) { float v = GetSkill(c, sk); if (v > bv) { bv = v; best = sk; } }
        return best;
    }
    public const float MasteryAt = 0.99f;

    /// <summary>1분마다: 이룸 · 흔들림을 살피고 · 사실에 마음이 움직인다.</summary>
    private void AimStep(Sim sim)
    {
        long t = sim.Tick;
        foreach (var f in sim.Facts.Since(_aimCursor))
        {
            switch (f.Kind)
            {
                case FactKind.Burn:
                    foreach (var a in Agents)
                    {
                        if (!a.IsPerson || (Aim)a.C.Aim != Aim.Home || !AliveAwake(a)) continue;
                        ref var c = ref a.C;
                        if (f.Other < 0)   // 정해진 점화를 놓쳤다 — 집이 멀어진다
                        {
                            c.Sad = Math.Min(1, c.Sad + 0.35f); c.Stress = Math.Min(1, c.Stress + 0.2f);
                            AimFact(sim, a, -1, $"{Ko.I(NameOf(a.Id))} 무너졌다 — 점화를 놓쳐 집이 멀어졌다", f.Cause);
                        }
                        else if (f.Other is (int)BurnKind.Arrive or (int)BurnKind.Capture && c.AimMet == 0)
                            Met(sim, a, "항해를 마치고 닿았다", f.Cause);
                        else if (f.Other is (int)BurnKind.Depart or (int)BurnKind.Correct) c.Joy = Math.Min(1, c.Joy + 0.1f);   // 나아간다
                    }
                    break;
                case FactKind.Contact or FactKind.Flare or FactKind.DebrisNear:
                    foreach (var a in Agents)
                        if (a.IsPerson && (Aim)a.C.Aim == Aim.Know && AliveAwake(a)) { a.C.Joy = Math.Min(1, a.C.Joy + 0.08f); a.C.Bored = Math.Max(0, a.C.Bored - 0.2f); }
                    break;
                case FactKind.Extinguished or FactKind.Patched or FactKind.Rescue:
                    if (f.Actor >= 0 && f.Actor < Agents.Count && Agents[f.Actor].IsPerson && (Aim)Agents[f.Actor].C.Aim == Aim.Guard && Agents[f.Actor].C.AimMet == 0)
                        Met(sim, Agents[f.Actor], f.Kind switch { FactKind.Extinguished => "제 손으로 불을 껐다", FactKind.Patched => "제 손으로 구멍을 막았다", _ => "제 손으로 사람을 구했다" }, f.Cause);
                    break;
            }
        }
        _aimCursor = sim.Facts.Total;
        foreach (var a in Agents)
        {
            if (!a.IsPerson || a.C.Aim == 0 || !AliveAwake(a)) continue;
            ref var c = ref a.C;
            switch ((Aim)c.Aim)
            {
                case Aim.Master when c.AimMet == 0 && GetSkill(c, AimSkill(c)) >= MasteryAt: Met(sim, a, $"{AimSkill(c)} 솜씨를 끝까지 익혔다", 0); break;
                case Aim.Know when c.AimMet == 0 && c.SkScience >= 0.7f: Met(sim, a, "배우고 싶던 것을 알게 됐다", 0); break;
                case Aim.Love:
                    int mate = _soc?.PartnerOf(a.Id) ?? -1;
                    if (c.AimMet == 0 && mate >= 0) Met(sim, a, $"{Ko.Gwa(NameOf(mate))} 평생 함께하기로 했다", 0);
                    else if (c.AimMet == 1 && mate < 0)
                    {
                        c.AimMet = 0;
                        c.Sad = Math.Min(1, c.Sad + 0.35f); c.Stress = Math.Min(1, c.Stress + 0.2f);
                        AimFact(sim, a, -1, $"{Ko.I(NameOf(a.Id))} 짝을 잃었다 — 바라던 삶이 멀어졌다", 0);
                    }
                    break;
            }
        }
    }

    private bool AliveAwake(Agent a) => a.IsPerson && _life != null && _life[a.C.Body].Alive != 0;

    private void Met(Sim sim, Agent a, string what, int parent)
    {
        ref var c = ref a.C;
        c.AimMet = 1;
        c.Joy = Math.Min(1, c.Joy + 0.4f); c.Sad = Math.Max(0, c.Sad - 0.2f); c.Stress = Math.Max(0, c.Stress - 0.2f);
        AimsMet++;
        AimFact(sim, a, 1, $"{Ko.I(NameOf(a.Id))} 오래 바라던 것을 이뤘다 — {what}", parent);
    }

    private void AimFact(Sim sim, Agent a, int size, string text, int parent)
    {
        if (size < 0) AimsShaken++;
        int cause = sim.Causes.Add(CauseKind.Person, text, sim.Tick, a.C.Cell, parent, 0, a.Id);
        sim.Facts.Emit(sim.Tick, FactKind.Aim, a.C.Cell, size, cause, a.Id, a.C.Aim);
    }

    private void SaveAims(StateWriter w) { w.I64(AimsMet); w.I64(AimsShaken); w.I64(_aimCursor); }
    private void LoadAims(StateReader r) { AimsMet = r.I64(); AimsShaken = r.I64(); _aimCursor = r.I64(); }
}
