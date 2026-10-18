namespace ShipEngine;

/// <summary>사고의 교훈 갈래.</summary>
public enum Hazard : byte { Fire, Breach, Air }

/// <summary>
/// ⑧ 마음 바퀴 3 — 오래 남는 배움.
/// <para><b>사고의 교훈</b>: 불 · 구멍 · 나쁜 공기를 곁에서 본 이(같은 방 · 깨어 있다)는 그 위험의 교훈이 쌓이고 (본 것 0.2 · 제 몸으로 겪은 것 0.5 — 남은 몫의 그만큼),
/// 이야기로 들은 이는 조금 (0.08 — ⑨가 요청한다) · 반감기 3주 (되새길수록 남은 몫만큼 다시 오른다 — 간격 효과).
/// 교훈이 있는 이는 같은 위험을 더 일찍 알아채고 더 빨리 움직인다: 불 끄기 × (1 + 0.5 × 불의 교훈) · 구멍 막기 × (1 + 0.5 × 구멍의 교훈) · 살피러 가기 × (1 + 0.4 × 더 큰 교훈) ·
/// 피의 산소가 0.8 + 0.1 × (나쁜 공기 · 구멍의 교훈) 아래면 어지러움을 알아챈다 · 켜 둔 조리대를 잊는 몫 × (1 − 0.5 × 불의 교훈) (사고 뒤 경계심 — 사건 경험이 다음 대응을 빠르게 한다).</para>
/// <para><b>녹스는 솜씨</b>: 솜씨마다 마지막으로 쓴 때와 가장 높았던 값을 안다 — 두 주 넘게 쓰지 않으면 하루 1%씩 바닥(가장 높았던 값의 절반)으로 녹슨다
/// (기술 쇠퇴 메타 분석 Arthur 외 1998: 한 해 안 쓰면 크게 녹는다 · 쓰지 않은 기간이 길수록). 다시 익힐 때는 처음보다 빠르다 — 늘음 × (1 + 2 × 잃은 몫) (에빙하우스의 절약).
/// 주컴퓨터의 주간 비상 훈련(일요일 10시 · 30분)에 깨어 있는 이는 불 끄기 · 봉합 · 치료를 되짚는다 (쓴 것으로 친다 · 교훈 +0.05).</para>
/// </summary>
public sealed partial class Mind
{
    public const double LessonHalfDays = 21, RustAfterDays = 14, RustPerDay = 0.01;
    public static readonly string[] SkillNames = { "고치기", "치료", "불끄기", "봉합", "조리", "조종", "기르기", "연구" };
    public long LessonsLearned, LessonsTold, SkillsRusted, Drills;
    private long _lessonCursor;
    private long[] _skillUsed = Array.Empty<long>();
    private float[] _skillPeak = Array.Empty<float>();

    public static float Lesson(in AgentCore c, Hazard h) => h switch { Hazard.Fire => c.LessonFire, Hazard.Breach => c.LessonBreach, _ => c.LessonAir };
    private static ref float LessonRef(ref AgentCore c, Hazard h)
    {
        if (h == Hazard.Fire) return ref c.LessonFire;
        if (h == Hazard.Breach) return ref c.LessonBreach;
        return ref c.LessonAir;
    }

    private void AddLesson(Agent a, Hazard h, float amount)
    {
        if (!a.IsPerson) return;
        ref float l = ref LessonRef(ref a.C, h);
        float before = l;
        l = Math.Min(1, l + amount * (1 - l));
        if (before < 0.3f && l >= 0.3f) LessonsLearned++;
    }

    /// <summary>⑨가 요청: 이야기로 들은 교훈 (함께 겪은 일을 되새기면 겪은 이도 다시 오른다).</summary>
    public void Teach(int agent, Hazard h, float amount)
    {
        if (agent < 0 || agent >= Agents.Count || !Agents[agent].IsPerson) return;
        AddLesson(Agents[agent], h, amount);
        LessonsTold++;
    }

    /// <summary>교훈 · 위험이 점수에 닿는 곱.</summary>
    private static float LessonBoost(in AgentCore c, GoalKind g) => g switch
    {
        GoalKind.FightFire => 1 + 0.5f * c.LessonFire,
        GoalKind.Patch => 1 + 0.5f * c.LessonBreach,
        GoalKind.Investigate => 1 + 0.4f * Math.Max(c.LessonBreach, c.LessonFire),
        _ => 1,
    };

    /// <summary>1분마다: 교훈이 흐려진다 · 곁에서 본 일 · 제 몸으로 겪은 일에서 배운다 · 하루에 한 번 솜씨가 녹는다.</summary>
    private void LessonStep(Sim sim)
    {
        double k = Math.Exp(-25 * Time.SecondsPerTick * 0.6931 / (LessonHalfDays * 86400));
        foreach (var a in Agents)
        {
            if (!a.IsPerson) continue;
            a.C.LessonFire *= (float)k; a.C.LessonBreach *= (float)k; a.C.LessonAir *= (float)k;
        }
        foreach (var f in sim.Facts.Since(_lessonCursor))
        {
            Hazard h; float saw;
            switch (f.Kind)
            {
                case FactKind.Ignition or FactKind.FireSpread or FactKind.Extinguished: h = Hazard.Fire; saw = 0.2f; break;
                case FactKind.WallBreach or FactKind.RegionVented or FactKind.PressureDrop or FactKind.Patched: h = Hazard.Breach; saw = 0.2f; break;
                case FactKind.Toxic: h = Hazard.Air; saw = 0.15f; break;
                case FactKind.Collapsed or FactKind.Hurt when f.Actor >= 0 && _life != null && f.Actor < _life.Bodies.Count:
                {
                    // 제 몸으로 겪었다: 덴 몸은 불 · 숨이 막힌 몸은 나쁜 공기 (기압이 낮았으면 구멍도) — 곁에서 쓰러지는 것을 본 이도 조금
                    var b = _life.Bodies[f.Actor];
                    int who = AgentOfBody(f.Actor);
                    Hazard hh = b.Burn > 0.2f ? Hazard.Fire : b.Hypoxia > 0.3f || b.COHb > 0.2f ? Hazard.Air : (Hazard)255;
                    if ((byte)hh == 255) continue;
                    if (who >= 0) { AddLesson(Agents[who], hh, 0.5f); if (hh == Hazard.Air && F.Pressure(b.Cell) < 60) AddLesson(Agents[who], Hazard.Breach, 0.4f); }
                    foreach (int o in Witnesses(f.Cell, who)) AddLesson(Agents[o], hh, 0.15f);
                    continue;
                }
                default: continue;
            }
            foreach (int o in Witnesses(f.Cell, -1)) AddLesson(Agents[o], h, saw);
        }
        _lessonCursor = sim.Facts.Total;
        if (sim.Tick % Time.TicksPerDay == 0) RustStep(sim);
    }

    /// <summary>그 칸을 본 사람들: 깨어 있고 같은 방이거나 8칸 안.</summary>
    private List<int> Witnesses(int cell, int except)
    {
        var list = new List<int>();
        if (cell < 0 || _life == null) return list;
        int room = RoomOf(cell);
        foreach (var a in Agents)
        {
            if (!a.IsPerson || a.Id == except || a.C.Up == 0) continue;
            var b = _life.Bodies[a.C.Body];
            if (b.Alive == 0 || b.Conscious == 0 || b.Asleep != 0) continue;
            if (room >= 0 && RoomOf(a.C.Cell) == room || Dist(a.C.Cell, cell) <= 8) list.Add(a.Id);
        }
        return list;
    }

    // ───────────────────────── 녹스는 솜씨 ─────────────────────────

    private void SkillArrays(long now)
    {
        int n = Agents.Count * SkillNames.Length;
        if (_skillUsed.Length >= n) return;
        int old = _skillUsed.Length;
        Array.Resize(ref _skillUsed, n); Array.Resize(ref _skillPeak, n);
        for (int i = old; i < n; i++) { int a = i / SkillNames.Length; _skillPeak[i] = Agents[a].IsPerson ? GetSkill(Agents[a].C, SkillNames[i % SkillNames.Length]) : 0; _skillUsed[i] = now; }
    }

    /// <summary>솜씨를 썼다 (가장 높았던 값을 새로 적는다) · 다시 익히는 배율 (잃은 몫이 클수록 빨리 — 에빙하우스의 절약).</summary>
    private float UseSkill(Agent a, int skill, long t)
    {
        if (!a.IsPerson) return 1;
        SkillArrays(t);
        int i = a.Id * SkillNames.Length + skill;
        _skillUsed[i] = t;
        float v = GetSkill(a.C, SkillNames[skill]);
        float relearn = 1 + 2 * Math.Max(0, _skillPeak[i] - v);
        if (v > _skillPeak[i]) _skillPeak[i] = v;
        return relearn;
    }

    /// <summary>하루에 한 번: 두 주 넘게 안 쓴 솜씨는 하루 1%씩 바닥(가장 높았던 값의 절반)으로.</summary>
    private void RustStep(Sim sim)
    {
        SkillArrays(sim.Tick);
        foreach (var a in Agents)
        {
            if (!a.IsPerson) continue;
            for (int k = 0; k < SkillNames.Length; k++)
            {
                int i = a.Id * SkillNames.Length + k;
                if (sim.Tick - _skillUsed[i] < Time.Hours(24 * RustAfterDays)) continue;
                float v = GetSkill(a.C, SkillNames[k]), floor = 0.5f * _skillPeak[i];
                if (v <= floor) continue;
                float nv = floor + (v - floor) * (float)(1 - RustPerDay);
                if (v - floor > 0.05f && nv - floor <= 0.05f) SkillsRusted++;
                SetSkill(ref a.C, SkillNames[k], nv);
            }
        }
    }

    /// <summary>⑩이 요청: 비상 훈련 — 깨어 있는 이는 불 끄기 · 봉합 · 치료 절차를 되짚는다 (쓴 것으로 친다 · 교훈이 조금).</summary>
    public void Drill(Sim sim)
    {
        Drills++;
        foreach (var a in Agents)
        {
            if (!a.IsPerson || a.C.Up == 0 || _life == null || _life.Bodies[a.C.Body].Asleep != 0) continue;
            UseSkill(a, 2, sim.Tick); UseSkill(a, 3, sim.Tick); UseSkill(a, 1, sim.Tick);
            AddLesson(a, Hazard.Fire, 0.05f); AddLesson(a, Hazard.Breach, 0.05f);
        }
    }

    /// <summary>다시 익히는 배율 (가장 높았던 값에서 잃은 몫이 클수록 — 에빙하우스의 절약).</summary>
    public float Relearn(int agent, int skill) { SkillArrays(0); int i = agent * SkillNames.Length + skill; return 1 + 2 * Math.Max(0, _skillPeak[i] - GetSkill(Agents[agent].C, SkillNames[skill])); }
    public float SkillPeak(int agent, int skill) { SkillArrays(0); return _skillPeak[agent * SkillNames.Length + skill]; }
    public long SkillUsedAt(int agent, int skill) { SkillArrays(0); return _skillUsed[agent * SkillNames.Length + skill]; }
    /// <summary>시험 · 되살리기용: 그 솜씨를 마지막으로 쓴 때를 바꾼다.</summary>
    public void SetSkillUsed(int agent, int skill, long tick) { SkillArrays(tick); _skillUsed[agent * SkillNames.Length + skill] = tick; }

    private void SaveLessons(StateWriter w)
    {
        w.I64(LessonsLearned); w.I64(LessonsTold); w.I64(SkillsRusted); w.I64(Drills); w.I64(_lessonCursor);
        w.Arr(_skillUsed); w.Arr(_skillPeak);
    }
    private void LoadLessons(StateReader r)
    {
        LessonsLearned = r.I64(); LessonsTold = r.I64(); SkillsRusted = r.I64(); Drills = r.I64(); _lessonCursor = r.I64();
        _skillUsed = r.Arr<long>(); _skillPeak = r.Arr<float>();
    }
}
