namespace ShipEngine;

/// <summary>⑦ (라) 수술의 종류.</summary>
public enum Op : byte { None, Bleed, Appendix, Explore, ArtHeart, ArtLung, TxHeart, TxLiver, TxLung }

/// <summary>
/// ⑦ 숨은 병 둘 · 수술 (깊이 바퀴 3 (라-1) — docs/ENGINE.md '장기 · 약 · 수술').
/// <para><b>속 출혈</b>: 한 번에 0.3 넘는 큰 타박으로 타박이 0.5를 넘으면 열에 넷 — 0.1 ~ 0.3 L/시 · 0.05 아래면 저절로 멎고 (한 시간 반 반감) · 그 위면 수술로만 멎는다.</para>
/// <para><b>맹장염</b> (몸의 확률 · 시험): 하루에 1로 차오르고 (항생제를 먹는 동안은 절반) · 배가 아프고 (0.3 + 0.4 × 맹장) · 열 (+1℃ × 맹장) — 1이면 터져 배 안이 곪는다 (곪음 +0.8).</para>
/// <para><b>수술</b> (마음이 요청): 잘되면 고치고 · 못 되면 합병증 (반은 출혈 −0.8L · 반은 감염 +0.4 — 소독기가 있으면 반) · 꿰매어 덮은 수술 상처가 남는다.</para>
/// </summary>
public sealed partial class Life
{
    public const float BleedInSelfStop = 0.05f;
    public const double RegenDays = 4, RegenHours = 72, DonorHours = 6;
    public long InsideBleeds, Appendicitis, Ruptures, Operations, OpComplications;

    public static string OpName(Op op) => op switch
    {
        Op.Bleed => "속 출혈 수술", Op.Appendix => "맹장 수술", Op.ArtHeart => "인공 심장", Op.ArtLung => "인공 폐",
        Op.TxHeart => "심장 이식", Op.TxLiver => "간 이식", Op.TxLung => "폐 이식", _ => "수술",
    };

    /// <summary>남은 손상 (낫지 않는 바닥 가운데 가장 큰 것): 뇌 40% · 심장 50% · 폐 흉터.</summary>
    public static double LastingDamage(in Body b) => Math.Max(BrainKeep * b.BrainPeak, Math.Max(HeartKeep * b.HeartPeak, b.LungScar));

    /// <summary>재생 젤이 드는 몸 (남은 손상 0.15 넘게 · 젤이 없다).</summary>
    public static bool NeedsRegen(in Body b) => b.Alive != 0 && b.Regen <= 0 && LastingDamage(b) > 0.15;

    /// <summary>재생 젤 (순수 — 시험이 바로 돌린다): 치료 침대에 누운 동안만 — 낫지 않던 바닥(가장 컸던 것 · 흉터)이 나흘에 e배로 준다 · 젤은 누운 시간만큼 닳는다.</summary>
    public static void RegenKinetics(ref Body b, double dt, bool onBed)
    {
        if (b.Regen <= 0 || !onBed || b.Alive == 0) return;
        double k = Math.Exp(-dt / (RegenDays * 86400));
        b.BrainPeak = (float)(b.BrainPeak * k); b.HeartPeak = (float)(b.HeartPeak * k); b.LungScar = (float)(b.LungScar * k);
        b.Regen = (float)Math.Max(0, b.Regen - dt / 3600);
    }

    /// <summary>내줄 수 있는 장기 (여섯 시간 안에 숨진 이 · 상한 정도 0.2 아래 · 아직 내주지 않았다) — 없으면 −1.</summary>
    public int FindDonor(long now, Op op)
    {
        byte flag = op switch { Op.TxHeart => 32, Op.TxLiver => 64, Op.TxLung => 128, _ => 0 };
        if (flag == 0) return -1;
        for (int i = 0; i < Bodies.Count; i++)
        {
            var d = Bodies[i];
            if (d.Alive != 0 || (d.Implant & flag) != 0 || now - d.DiedAt > Time.Hours(DonorHours) || now < d.DiedAt) continue;
            float organ = op switch { Op.TxHeart => d.Heart, Op.TxLiver => d.Liver, _ => Math.Max(d.LungDamage, d.LungScar) };
            if (organ < 0.2f) return i;
        }
        return -1;
    }

    /// <summary>맹장염의 배 아픔.</summary>
    public static double AppendixPain(in Body b) => b.Appendix > 0 ? 0.3 + 0.4 * b.Appendix : 0;

    /// <summary>큰 타박이 속 출혈을 남기나 (한 번에 0.3 넘게 · 타박 0.5 넘게 · 열에 넷).</summary>
    private void BleedInside(Sim sim, int id, ref Body b, float before, int cause)
    {
        if (b.Alive == 0 || b.BleedIn > 0 || b.Blunt - before < 0.3f || b.Blunt <= 0.5f) return;
        double u = Rng.Unit(sim.Seed, Rng.Key("속 출혈"), sim.Tick, id);
        if (u >= 0.4) return;
        b.BleedIn = (float)(0.1 + 0.5 * u);   // 0.1 ~ 0.3 L/시
        InsideBleeds++;
        b.HurtCause = sim.Causes.Add(CauseKind.Biology, "크게 부딪혀 배 속에서 피가 난다 — 겉으로는 멍뿐", sim.Tick, b.Cell, cause);
    }

    /// <summary>맹장염이 시작된다 (시험) — 떼어 낸 이는 걸리지 않는다.</summary>
    public void StartAppendicitis(Sim sim, int id, int cause)
    {
        if (id < 0 || id >= Bodies.Count) return;
        ref var b = ref this[id];
        if (b.Alive == 0 || b.Appendix != 0) return;
        b.Appendix = 0.05f; Appendicitis++;
        b.AppendixCause = sim.Causes.Add(CauseKind.Biology, "배가 살살 아프기 시작했다 — 맹장염", sim.Tick, b.Cell, cause);
    }

    /// <summary>속 출혈 · 맹장염 한 걸음.</summary>
    private void InsideStep(Sim sim, int id, ref Body b, double dt)
    {
        if (b.BleedIn > 0)
        {
            if (b.BleedIn < BleedInSelfStop) b.BleedIn = (float)(b.BleedIn * Math.Exp(-dt / (1.5 * 3600 / 0.693)));   // 한 시간 반 반감
            if (b.BleedIn < 0.005f) b.BleedIn = 0;
        }
        if (b.Appendix > 0)
        {
            b.Appendix += (float)(dt / 86400 * (b.Abx > 0 ? 0.5 : 1));
            if (b.Appendix >= 1)
            {
                b.Appendix = 0; Ruptures++;
                b.Infect = Math.Max(0, b.Infect) + 0.8f;
                b.InfectCause = sim.Causes.Add(CauseKind.Biology, "맹장이 터져 배 안이 곪았다", sim.Tick, b.Cell, b.AppendixCause);
                sim.Facts.Emit(sim.Tick, FactKind.Sick, b.Cell, 3, b.InfectCause, actor: id);
            }
        }
    }

    /// <summary>
    /// 마음의 요청: 수술 — 잘되면 속 출혈이 멎고 · 맹장을 떼어 낸다 / 못 되면 합병증 (bleed 참이면 출혈 · 아니면 감염).
    /// 꿰매어 덮은 수술 상처 (타박 0.3 · 덮개 10일) · 수술 뒤 곪을 몫 (소독기가 없으면 세 배).
    /// </summary>
    public void Operate(Sim sim, int id, Op op, bool ok, bool bleed, bool sterile, int cause, int donor = -1)
    {
        if (id < 0 || id >= Bodies.Count) return;
        ref var b = ref this[id];
        if (b.Alive == 0) return;
        Operations++;
        if (donor >= 0 && donor < Bodies.Count && op is Op.TxHeart or Op.TxLiver or Op.TxLung)
            this[donor].Implant |= (byte)(op == Op.TxHeart ? 32 : op == Op.TxLiver ? 64 : 128);   // 잘되든 못 되든 그 장기는 쓰였다
        if (ok)
        {
            var d = donor >= 0 && donor < Bodies.Count ? Bodies[donor] : default;
            switch (op)
            {
                case Op.Bleed: b.BleedIn = 0; break;
                case Op.Appendix: b.Appendix = -1; break;
                case Op.ArtHeart: b.Heart = 0; b.HeartPeak = 0; b.Implant |= 1; break;
                case Op.ArtLung: b.LungDamage = 0; b.LungScar = 0; b.Implant |= 2; break;
                case Op.TxHeart: b.Heart = d.Heart; b.HeartPeak = d.Heart; b.Implant |= 4; break;
                case Op.TxLiver: b.Liver = d.Liver; b.LiverPeak = d.Liver; b.Implant |= 8; break;
                case Op.TxLung: b.LungDamage = d.LungDamage; b.LungScar = d.LungScar; b.Implant |= 16; break;
            }
        }
        else
        {
            OpComplications++;
            if (bleed) { b.BloodL -= 0.8f; if (op == Op.Bleed) b.BleedIn *= 0.5f; }
            else { b.Infect = Math.Max(0, b.Infect) + (sterile ? 0.2f : 0.4f); b.InfectCause = cause; }
            if (op == Op.Appendix && !bleed) b.Appendix = -1;
        }
        b.Infect = Math.Max(0, b.Infect) + (sterile ? 0.03f : 0.09f);
        b.Blunt = Math.Max(b.Blunt, 0.3f);
        b.Dressed = (float)Math.Max(b.Dressed, DressHours);
        b.HurtCause = cause;
    }
}
