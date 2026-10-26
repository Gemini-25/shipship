namespace ShipEngine;

/// <summary>
/// ⑦ 손 · 겉면 장염 균 (5단 C21 — docs/STAGE5.md): 몸마다 손에 묻은 균 (몫) · 설비마다 겉면 균 — 만지면 30%씩 맞바꾼다 (양 보존) ·
/// 배탈 앓는 이가 변기를 쓰면 겉면 +1 · 손 +0.8 · 손은 2시간 · 겉면은 3일 × (1 + 2 × 때)에 반 · 물로 씻으면 손 × 0.05 · 물수건 × 0.5 · 닦으면 겉면 0 ·
/// 먹을 때 min(1, 손 × 0.4) 몫으로 옮는다 (0.05 아래는 옮지 않는다). 아직 앓지 않는 이 (배탈 0)의 GutCause는 손에 묻은 균이 어디서 왔나를 쥔다 (옮으면 그 마디를 부모로).
/// </summary>
public sealed partial class Life
{
    /// <summary>만질 때 맞바꾸는 몫 · 물로 씻은 뒤 남는 손 균 · 물수건 · 먹을 때 옮길 몫 (손 균 1에) · 옮는 문턱 · 손 · 겉면 반감 (초).</summary>
    public const double TouchShare = 0.3, HandWashKeep = 0.05, HandWipeKeep = 0.5, GermFloor = 0.05, HandHalfSec = 7200, SurfHalfSec = 3 * 86400;
    public double GutInfect = 0.4;

    /// <summary>손 ↔ 설비 겉면 (30%씩 · 양 보존) — 겉면에서 묻은 균의 까닭은 아직 앓지 않는 이의 GutCause에 (문턱을 넘을 만큼 묻었을 때만).</summary>
    public void Touch(int body, int furniture)
    {
        if (body < 0 || body >= Bodies.Count || furniture < 0) return;
        WashArrays();
        if (furniture >= SurfGerm.Length) return;
        ref var b = ref this[body];
        double h = b.HandGerm, s = SurfGerm[furniture];
        if (h <= 0 && s <= 0) return;
        b.HandGerm = (float)(h + TouchShare * (s - h));
        SurfGerm[furniture] = (float)(s + TouchShare * (h - s));
        if (s > h && b.Gut == 0 && b.HandGerm >= GermFloor && SurfCause[furniture] != 0) b.GutCause = SurfCause[furniture];
        if (h > s && SurfGerm[furniture] >= GermFloor && SurfCause[furniture] == 0) SurfCause[furniture] = b.Gut == 2 ? b.GutCause : b.Gut == 0 ? b.GutCause : 0;
    }

    /// <summary>변기 (월드가 스스로): 배탈 앓는 이가 쓰면 겉면 +1 · 손 +0.8 (원인 '배탈 앓는 이가 변기를 썼다' ← 그 배탈) · 그다음 만진다.</summary>
    private void ToiletGerms(Sim sim, int body, int toilet, int cell)
    {
        if (body < 0 || body >= Bodies.Count) return;
        ref var b = ref this[body];
        if (b.Gut == 2)
        {
            if (SurfCause[toilet] == 0 || SurfGerm[toilet] < GermFloor) SurfCause[toilet] = sim.Causes.Add(CauseKind.Biology, "배탈을 앓는 이가 변기를 써 겉면에 장염 균이 묻었다", sim.Tick, cell, b.GutCause);
            SurfGerm[toilet] += 1;
            b.HandGerm += 0.8f;
        }
        Touch(body, toilet);
    }

    /// <summary>⑧의 요청: 설비 (급식기 · 보관함)에서 먹는다 — 만지고 · 먹고 · 손 균이 문턱을 넘으면 min(1, 손 × 0.4) 몫으로 배탈이 잠복한다.</summary>
    public void EatBy(Sim sim, int body, double mol, int furniture)
    {
        if (body < 0 || body >= Bodies.Count) return;
        Touch(body, furniture);
        Eat(body, mol);
        ref var b = ref this[body];
        if (b.Gut != 0 || b.HandGerm < GermFloor || b.Alive == 0) return;
        if (Rng.Unit(sim.Seed, Rng.Key("손 균 옮음"), sim.Tick, body) >= Math.Min(1, b.HandGerm * GutInfect)) return;
        b.Gut = 1;
        b.GutCause = sim.Causes.Add(CauseKind.Biology, "씻지 않은 손으로 먹다 장염 균이 옮았다", sim.Tick, b.Cell, b.GutCause);
    }

    /// <summary>⑧의 요청: 물수건으로 손을 닦는다 (손 균 × 0.5 — 지린 옷은 그대로).</summary>
    public void WipeHands(int body) { if (body >= 0 && body < Bodies.Count) this[body].HandGerm *= (float)HandWipeKeep; }

    /// <summary>줄어듦 (5분마다 · 공개 — 시험이 큰 dt로): 손은 2시간 · 겉면은 3일 × (1 + 2 × 때)에 반 · 문턱 아래로 마른 겉면은 까닭을 지운다.</summary>
    public void GermStep(double dt)
    {
        WashArrays();
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Bodies);
        float hk = (float)Math.Pow(0.5, dt / HandHalfSec);
        for (int i = 0; i < span.Length; i++) if (span[i].HandGerm > 0) { span[i].HandGerm *= hk; if (span[i].HandGerm < 1e-6f) span[i].HandGerm = 0; }
        for (int fu = 0; fu < SurfGerm.Length; fu++)
        {
            if (SurfGerm[fu] <= 0) continue;
            SurfGerm[fu] *= (float)Math.Pow(0.5, dt / (SurfHalfSec * (1 + 2 * Grime[fu])));
            if (SurfGerm[fu] < 1e-6f) SurfGerm[fu] = 0;
            if (SurfGerm[fu] < GermFloor) SurfCause[fu] = 0;
        }
    }
}
