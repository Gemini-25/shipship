namespace ShipEngine;

/// <summary>
/// ⑦ 장기 · 심정지 (깊이 바퀴 3 (가) — docs/ENGINE.md '장기 · 약 · 수술').
/// <para><b>뇌</b>: 산소 부족이 깊으면 (Hypoxia 1.5 넘게) 분마다 0.25 × 넘은 만큼 상한다 — 0.5 넘으면 깨어나지 못하고 · 1이면 죽는다 ·
/// 한 달에 걸쳐 가장 컸던 것의 40%까지만 낫는다 (저산소 뇌 손상은 다 돌아오지 않는다).</para>
/// <para><b>심장</b>: 피의 산소가 0.5 아래로 오래면 (심근 허혈) · 멎었다 다시 뛰면 +0.2 (기절 심근) — 지치기 쉽고 · 느리고 · 하루에 0.3 × 심장² 꼴로 저절로 떨린다 (부정맥).
/// 가장 컸던 것의 50%까지만 낫는다.</para>
/// <para><b>폐 흉터</b>: 폐 손상이 0.4를 넘은 몫의 절반은 흉터로 남는다 (섬유화).</para>
/// <para><b>심정지</b>: 감전(30%) · 체온 30℃ 아래(잔떨림) · 피가 2.8L 아래 · 깊은 산소 부족(Hypoxia 2 — 멎음) · 상한 심장(저절로 — 잔떨림).
/// 피가 돌지 않아 산소 부족이 분에 0.35씩 (가슴 압박을 받으면 0.12) 쌓인다 — 4분이면 뇌가 상하기 시작하고 8분 반이면 죽는다 (압박을 받으면 12분 · 25분).
/// 잔떨림은 압박 없이 8분이면 멎음이 된다 (압박을 받는 동안은 2.5배 더디게). 제세동기 한 번: 잔떨림이면 0.5 × e^(−압박 없던 분/6 − 압박 받은 분/20) ·
/// 멎음은 압박 중 분에 0.03으로 다시 뛴다. (구급 통계: 목격된 잔떨림 심정지는 3분 안의 제세동으로 반이 산다 — 1분 늦을 때마다 7 ~ 10% 준다)</para>
/// </summary>
public sealed partial class Life
{
    public const float BrainHypoxia = 1.5f, BrainRate = 0.25f, BrainKeep = 0.4f, HeartKeep = 0.5f, ScarFrom = 0.4f;
    public const double OrganHealDays = 10;                       // 낫는 몫이 e로 줄어드는 날
    public const double ArrestRate = 0.35, CprRate = 0.12;        // 심정지 중 산소 부족이 쌓이는 빠르기 (분에)
    public const double FibMinutes = 8, ShockBase = 0.5, AsystoleRevive = 0.03, ShockChance = 0.3;
    public long Arrests, Revives, Shocks, ShockMisses;

    /// <summary>심장이 멎는다 — kind 1 잔떨림 (제세동이 듣는다) · 2 멎음. 원인 마디는 parent에 잇는다.</summary>
    public void StopHeart(Sim sim, int id, byte kind, string why, int parent)
    {
        if (id < 0 || id >= Bodies.Count) return;
        ref var b = ref this[id];
        if (b.Alive == 0 || b.Arrest != 0) return;
        b.Arrest = kind; b.NoFlow = 0; b.LowFlow = 0; b.CprAt = long.MinValue / 2;
        b.Conscious = 0;
        int cause = sim.Causes.Add(CauseKind.Biology, why, sim.Tick, b.Cell, parent);
        b.ArrestCause = cause; b.HurtCause = cause;
        Arrests++;
        sim.Facts.Emit(sim.Tick, FactKind.Arrest, b.Cell, kind, cause, actor: id);
    }

    /// <summary>마음의 요청: 가슴을 누른다 (이 걸음 동안 피가 조금 돈다).</summary>
    public void Compress(Sim sim, int id)
    {
        if (id < 0 || id >= Bodies.Count) return;
        ref var b = ref this[id];
        if (b.Alive != 0 && b.Arrest != 0) b.CprAt = sim.Tick;
    }

    /// <summary>마음의 요청: 제세동기로 친다 — 잔떨림일 때만 듣는다 (멎음 · 뛰는 심장에는 치지 않는다). 다시 뛰면 true.</summary>
    public bool Defibrillate(Sim sim, int id, int cause)
    {
        if (id < 0 || id >= Bodies.Count) return false;
        ref var b = ref this[id];
        if (b.Alive == 0 || b.Arrest != 1) return false;
        b.ShockAt = sim.Tick; Shocks++;
        double p = Math.Min(0.95, ShockBase * Math.Exp(-b.NoFlow / 6.0 - b.LowFlow / 20.0) * EpiBoost(sim, b));
        if (Rng.Unit(sim.Seed, Rng.Key("제세동"), sim.Tick, id) >= p) { ShockMisses++; return false; }
        Revive(sim, id, ref b, 1, "제세동기로 멎은 심장이 다시 뛴다", cause);
        return true;
    }

    /// <summary>감전 — 화상 0.15 · 30%는 심장이 떤다 (잔떨림).</summary>
    public void Electrocute(Sim sim, int id, int cause)
    {
        Scald(sim, id, 0.15f, cause);
        if (id < 0 || id >= Bodies.Count) return;
        if (Rng.Unit(sim.Seed, Rng.Key("감전 심정지"), sim.Tick, id) < ShockChance)
            StopHeart(sim, id, 1, "감전으로 심장이 떨린다 — 잔떨림", cause);
    }

    private void Revive(Sim sim, int id, ref Body b, byte how, string why, int parent)
    {
        b.Arrest = 0; b.NoFlow = 0; b.LowFlow = 0;
        b.Heart = Math.Min(1, b.Heart + 0.2f);   // 기절 심근
        Revives++;
        int cause = sim.Causes.Add(CauseKind.Biology, why, sim.Tick, b.Cell, parent != 0 ? parent : b.ArrestCause, parent != 0 ? b.ArrestCause : 0);
        sim.Facts.Emit(sim.Tick, FactKind.Revived, b.Cell, how, cause, actor: id);
    }

    /// <summary>심정지 중 한 걸음: 압박을 받았나 · 잔떨림이 멎음으로 · 멎음이 압박 중 다시 뛰나. 압박을 받았으면 true.</summary>
    private bool ArrestStep(Sim sim, int id, ref Body b, double dt)
    {
        bool cpr = sim.Tick - b.CprAt <= Every;
        double min = dt / 60;
        if (cpr) b.LowFlow += (float)min; else b.NoFlow += (float)min;
        if (b.Arrest == 1 && b.NoFlow + 0.4 * b.LowFlow >= FibMinutes) b.Arrest = 2;   // 잔떨림이 지쳐 멎는다
        else if (b.Arrest == 2 && cpr && Rng.Unit(sim.Seed, Rng.Key("압박 소생"), sim.Tick, id) < AsystoleRevive * EpiBoost(sim, b) * min)
            Revive(sim, id, ref b, 2, "가슴 압박 끝에 심장이 다시 뛴다", 0);
        return cpr;
    }

    /// <summary>심정지의 방아쇠 (뛰는 심장만): 깊은 산소 부족 · 저체온 · 많이 흘린 피 · 상한 심장의 부정맥.</summary>
    private void ArrestTriggers(Sim sim, int id, ref Body b, double dt, int env)
    {
        if (b.Arrest != 0 || b.Alive == 0) return;
        if (b.Hypoxia >= 2) StopHeart(sim, id, 2, "산소가 모자라 심장이 멎었다", env != 0 ? env : b.HurtCause);
        else if (b.CoreK < 303.15) StopHeart(sim, id, 1, "몸이 식어 심장이 떨린다 — 저체온", env != 0 ? env : b.HurtCause);
        else if (b.BloodL < 2.8f) StopHeart(sim, id, 2, "피를 많이 흘려 심장이 멎었다", b.HurtCause);
        else if (b.Heart > 0.05f && Rng.Unit(sim.Seed, Rng.Key("부정맥"), sim.Tick, id) < 0.3 * b.Heart * b.Heart * dt / 86400)
            StopHeart(sim, id, 1, "상한 심장이 갑자기 떨린다 — 부정맥", b.HurtCause);
    }

    /// <summary>장기가 낫는다 (산소가 넉넉할 때): 뇌는 가장 컸던 것의 40% · 심장은 50% 위로 · 폐는 흉터 위로.</summary>
    public static void HealOrgans(ref Body b, double dt)
    {
        if (b.Brain > b.BrainPeak) b.BrainPeak = b.Brain;
        if (b.Heart > b.HeartPeak) b.HeartPeak = b.Heart;
        if (b.LungDamage > ScarFrom) b.LungScar = Math.Max(b.LungScar, 0.5f * (b.LungDamage - ScarFrom));
        if (b.Hypoxia > 0.3f || b.Arrest != 0) return;
        double k = Math.Exp(-dt / (OrganHealDays * 86400));
        float bf = BrainKeep * b.BrainPeak, hf = HeartKeep * b.HeartPeak;
        if (b.Brain > bf) b.Brain = (float)(bf + (b.Brain - bf) * k);
        if (b.Heart > hf) b.Heart = (float)(hf + (b.Heart - hf) * k);
    }

    /// <summary>폐가 낫는다 (해로운 것을 마시지 않을 때) — 두 주에 1 · 흉터 아래로는 낫지 않는다.</summary>
    public static void LungHeal(ref Body b, double dt) => b.LungDamage = (float)Math.Max(b.LungScar, b.LungDamage - dt / (14 * 86400));

    /// <summary>뇌 · 심장이 걸음에 주는 몫 (화면 · 마음).</summary>
    public static double OrganPace(in Body b) => (1 - 0.3 * b.Heart) * (1 - 0.3 * Math.Min(1, b.Brain));
}
