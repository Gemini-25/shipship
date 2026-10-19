namespace ShipEngine;

/// <summary>
/// ⑦ 생명 — 몸이 내는 소리 (소리 단계 S2 · docs/SOUND.md 결정 8): 코골이 · 기침. 판단이 정하지 않는다 — 몸의 상태에서 저절로 난다.
/// 소리 장(③)에 요청하고 · 들은 이의 귀(⑧)가 알아듣는다. 낸 이는 몸 번호로 적는다 (<see cref="Sound.BodyActor"/> — 생명은 사람 번호를 모른다).
/// </summary>
public sealed partial class Life
{
    /// <summary>코골이: 열에 넷이 고는 체질 (습관성 코골이 — 남 40% · 여 24% 어림) · 1m에서 44 ~ 60dB (가볍게 ~ 심하게) · 앓아 코가 막히면 +4 · 수면제(근육이 풀린다) +3.</summary>
    public const double SnorerShare = 0.4, SnoreMinDb = 44, SnoreSpanDb = 16;
    /// <summary>기침 크기 (1m · dB).</summary>
    public const float CoughDb = 62;

    /// <summary>이 몸의 코골이 체질 (0이면 골지 않는다 · 1이면 가장 심하다) — 몸마다 정해진 값 (목젖 · 혀 · 코의 생김새).</summary>
    public double SnoreTrait(Sim sim, int body)
    {
        double u = Rng.Unit(sim.Seed, Rng.Key("코골이 체질"), 0, body);
        return u < SnorerShare ? 1 - u / SnorerShare : 0;
    }

    /// <summary>
    /// 기침의 잦기 (시간에 몇 번): 옮는 병을 앓으면 15 · 폐 손상만큼 40 × 손상 · 숨 쉬는 높이의 자극 기체만큼 (그을음 1,500ppm · 암모니아 50 · 오존 1 · 염화수소 10 · 플루오린화수소 6ppm이면 한 몫 — 많아야 두 몫) 120 × 몫.
    /// (자극 문턱: NIOSH · ACGIH — 암모니아 25ppm · 염화수소 5ppm에서 목이 따갑다)
    /// </summary>
    public double CoughRate(in Body b)
    {
        if (b.Alive == 0 || b.Conscious == 0 || b.Suit != 0 || b.Cell < 0 || b.Cell >= L.N) return 0;
        bool low = b.Low != 0;
        int c = b.Cell;
        double irritant = F.HeadPpm(T.gSoot, c, low) / 1500 + F.HeadPpm(T.gNH3, c, low) / 50 + F.HeadPpm(T.gO3, c, low) / 1
                        + F.HeadPpm(T.gHCl, c, low) / 10 + (F.HeadPpm(T.gHF, c, low) + 2 * F.HeadPpm(T.gPOF3, c, low)) / 6;
        if (b.Mask != 0) irritant = 0;
        return (b.Ill == 2 ? 15 : 0) + 40 * b.LungDamage + 120 * Math.Min(2, irritant);
    }

    /// <summary>
    /// 몸이 내는 소리 (생명 한 걸음마다 — 5틱): 자는 이 가운데 코를 고는 체질은 숨마다 드르렁 (누운 채 · 공기가 있는 곳 · 우주복 · 마스크 없이) ·
    /// 목 · 폐가 따가운 이는 콜록 (잦기는 <see cref="CoughRate"/>).
    /// </summary>
    private void BodyNoise(Sim sim, double dt)
    {
        var snd = sim.Find<Sound>();
        if (snd == null) return;
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Bodies);
        for (int i = 0; i < span.Length; i++)
        {
            ref var b = ref span[i];
            if (b.Alive == 0 || b.Conscious == 0 || b.Suit != 0 || b.Cell < 0 || b.Cell >= L.N) continue;
            if (b.Asleep != 0 && b.Mask == 0)
            {
                double trait = SnoreTrait(sim, i);
                if (trait > 0 && Rng.Unit(sim.Seed, Rng.Key("코골이"), sim.Tick, i) < 0.8)
                {
                    double db = SnoreMinDb + SnoreSpanDb * trait + (b.Ill == 2 ? 4 : 0) + (b.Hypnotic > 0 ? 3 : 0);
                    Snores++;
                    snd.Emit(sim, b.Cell, (float)db, SoundKind.Snore, 0, Sound.BodyActor(i));
                }
            }
            double rate = CoughRate(b);
            if (rate > 0 && Rng.Unit(sim.Seed, Rng.Key("기침"), sim.Tick, i) < Math.Min(0.9, rate * dt / 3600))
            {
                Coughs++;
                snd.Emit(sim, b.Cell, CoughDb, SoundKind.Cough, b.HurtCause, Sound.BodyActor(i));
            }
        }
    }

    /// <summary>확인용 셈 (찍지 않는다).</summary>
    public long Snores, Coughs;

    // ───────────────────────── 귀 (소리 단계 S3) ─────────────────────────

    /// <summary>
    /// 일시 청력 손실 (dB): 귀에 닿은 가장 큰 소리가 110dB를 넘은 몫의 0.6배가 한꺼번에 오른다 (충격음 — 125dB이면 9dB) ·
    /// 폭풍의 넘친 압력은 그 압력의 소리 크기(20·log(p/20µPa) — 15kPa 177dB)가 150dB를 넘은 몫의 반 (15kPa 14dB · 35kPa 17dB · 100kPa 22dB) ·
    /// 85dB 넘는 곳에 오래 있으면 (넘는 몫의 반)까지 두 시간에 걸쳐 오른다 (기계실 95dB → 5dB) · 많아야 60dB.
    /// 돌아오기: 6 × (1 + 손실/20)시간에 1/e — 작은 손실은 하루 · 큰 손실은 며칠 (ISO 1999 · 충격음 일시 청력 손실 자료에서 어림).
    /// 우주복 투구는 12dB를 막는다.
    /// </summary>
    public const double ImpulseFrom = 110, ImpulseGain = 0.6, BlastSplFrom = 150, BlastGain = 0.5, SteadyFrom = 85, SteadyGain = 0.5, SteadyRiseH = 2, RecoverH = 6, MaxShift = 60;

    /// <summary>넘친 압력 kPa의 소리 크기 (dB SPL — 기준 20µPa).</summary>
    public static double SplOfKPa(double kpa) => kpa > 0 ? 20 * Math.Log10(kpa * 1000 / 2e-5) : 0;

    private void Ears(Sim sim, double dt)
    {
        var snd = sim.Find<Sound>();
        if (snd == null) return;
        var st = sim.Find<Structure>();
        long since = sim.Tick - Every + 1;
        double h = dt / 3600;
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Bodies);
        for (int i = 0; i < span.Length; i++)
        {
            ref var b = ref span[i];
            if (b.Alive == 0 || b.Cell < 0 || b.Cell >= L.N) continue;
            double guard = b.Suit != 0 ? 12 : 0;
            var (peak, cause) = snd.PeakSince(b.Cell, since);
            double shift = b.HearShift;
            double jump = peak - guard > ImpulseFrom ? ImpulseGain * (peak - guard - ImpulseFrom) : 0;
            if (st != null && st.BlastPeakKPa[b.Cell] > 0)
            {
                double spl = SplOfKPa(st.BlastPeakKPa[b.Cell]) - guard;
                if (spl > BlastSplFrom && BlastGain * (spl - BlastSplFrom) > jump) { jump = BlastGain * (spl - BlastSplFrom); cause = st.DamageCause[b.Cell] != 0 ? st.DamageCause[b.Cell] : cause; }
            }
            if (jump > 1) { shift += jump; if (cause != 0) b.HearCause = cause; }
            double steady = snd.Ambient(b.Cell) - guard;
            double target = steady > SteadyFrom ? SteadyGain * (steady - SteadyFrom) : 0;
            if (target > shift) shift += (target - shift) * (1 - Math.Exp(-h / SteadyRiseH));
            else { shift *= Math.Exp(-h / (RecoverH * (1 + shift / 20))); if (shift < 0.05) shift = 0; }   // 다 돌아왔다 (오르는 중엔 자르지 않는다 — 한 걸음 0.01dB씩 오른다)
            b.HearShift = (float)Math.Clamp(shift, 0, MaxShift);
        }
        if (st != null) Array.Clear(st.BlastPeakKPa);
    }
}
