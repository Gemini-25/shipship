using System;

namespace ShipSim.Core;

/// <summary>승무원 욕구. 모두 0~1.</summary>
public sealed class Needs
{
    /// <summary>포만감. 1 = 배부름, 0 = 굶주림.</summary>
    public float Food { get; set; } = 1f;

    /// <summary>기력. 1 = 쌩쌩함, 0 = 탈진.</summary>
    public float Rest { get; set; } = 1f;

    /// <summary>스트레스. 0 = 평온, 1 = 한계.</summary>
    public float Stress { get; set; } = 0.1f;

    /// <summary>교류. 1 = 충분히 어울림, 0 = 외로움.</summary>
    public float Social { get; set; } = 0.8f;

    public float Hunger => 1f - Food;
    public float Fatigue => 1f - Rest;

    internal void Clamp()
    {
        Food = Curve.Clamp01(Food);
        Rest = Curve.Clamp01(Rest);
        Stress = Curve.Clamp01(Stress);
        Social = Curve.Clamp01(Social);
    }
}

/// <summary>
/// 욕구와 몸 상태가 시간에 따라 변하는 규칙. 숫자는 "게임 1시간당 변화량".
/// 공기가 나쁘면 피로·스트레스가 빨리 쌓이고, 심하면 체력이 깎인다.
/// </summary>
public static class NeedsSystem
{
    public const float FoodDecayAwake = 1f / 9f;
    public const float FoodDecayAsleep = 1f / 24f;
    public const float RestDecayAwake = 1f / 24f;
    public const float RestDecayWorking = 1f / 20f;
    public const float RestGainAsleep = 1f / 10f;
    public const float SocialDecay = 1f / 30f;
    public const float StressWorking = 0.035f;
    public const float StressRelaxing = -0.22f;
    public const float StressSleeping = -0.03f;
    public const float StressIdle = -0.01f;
    public const float StressHungry = 0.06f;
    public const float StressExhausted = 0.06f;
    public const float StressLonely = 0.03f;

    public static void AddInjury(Vitals v, float amount, string cause)
    {
        if (amount <= 0f) return;
        if (v.Injury < 0.05f || amount > 0.05f) v.InjuryCause = cause;
        v.Injury = MathF.Min(1f, v.Injury + amount);
        // v11.3 후유증: 절반을 넘게 다치면 무엇인가 남는다 (재활로 절반까지만 준다)
        if (v.Injury > 0.5f)
        {
            float add = MathF.Min(amount, v.Injury - 0.5f) * 0.25f;
            if (add > 0f)
            {
                v.Scar = MathF.Min(0.3f, v.Scar + add);
                v.ScarFloor = MathF.Max(v.ScarFloor, v.Scar * 0.5f);
                v.ScarCause ??= cause;
            }
        }
    }

    public static void Update(CrewMember c, World w)
    {
        const float dt = 1f / SimTime.TicksPerHour;
        var n = c.Needs;
        var v = c.Vitals;
        bool asleep = c.Pose is Pose.Sleeping or Pose.Down;
        bool relaxing = c.Pose == Pose.Sitting && c.Job?.Activity is RelaxActivity;
        var air = c.Room?.Air;

        // ── 공기 → 혈중 산소 (우주복이면 탱크에서) ──
        bool suited = c.Suit is { Oxygen: > 0f };
        if (c.Suit != null) c.Suit.Oxygen = MathF.Max(0f, c.Suit.Oxygen - dt * c.Suit.Leak);
        // 선체 밖은 진공이다 (v8: EVA, 떨어져 나간 방에서 튕겨 나감)
        bool vacuum = c.Outside;
        float pressure = vacuum ? 0f : air?.Pressure ?? 101f;
        float targetOx = suited ? 1f : vacuum ? 0f : air == null ? 1f : Curve.Clamp01((air.O2 - 8f) / 9f);
        float oxRate = !suited && pressure < 40f ? 25f : 3f; // 진공에서는 몇 분 만에
        v.Oxygen += (targetOx - v.Oxygen) * MathF.Min(1f, oxRate * dt);
        float co2 = suited ? 0f : air?.CO2 ?? 0f;
        bool hypoxic = v.Oxygen < 0.85f;
        bool stuffy = co2 > 1f;

        // ── 욕구 ──
        // v10.11 배급: 한 끼씩 줄여 먹으니 허기가 덜 빠진다 (그만큼 날카로워진다 — 아래)
        n.Food -= (asleep ? FoodDecayAsleep : FoodDecayAwake) * c.Traits.Appetite * (w.Food.Rationing ? FoodPolicy.RationDecay : 1f) * dt;

        float restMul = (hypoxic ? 1.6f : 1f) * (stuffy ? 1.5f : 1f);
        // 침대가 아닌 곳(바닥, 의자)에서 자면 덜 쉰다
        var under = asleep ? w.Ship.FurnitureAt(c.Cell) : null;
        bool rough = asleep && !(under != null && FurnitureTypes.Sleepable(under.Type));
        // 손보지 않은 간이침대는 침대만 못하다 (v7: 침실 정비로 나아진다)
        bool cot = under is { Type: FurnitureType.Cot, Improved: false };
        // 무서운 방에서 자면 깊이 못 잔다 (v7)
        float dread = asleep ? c.Memory.FearOf(c.Room) : 0f;
        if (asleep) n.Rest += RestGainAsleep * (stuffy ? 0.7f : 1f) * (rough ? 0.75f : 1f) * (cot ? 0.92f : 1f) * (1f - 0.3f * dread) * dt;
        else n.Rest -= (c.Pose == Pose.Working ? RestDecayWorking : RestDecayAwake) * restMul * dt;

        if (!asleep) n.Social -= SocialDecay * (0.6f + 0.8f * c.Traits.Sociability) * dt;

        float stress = c.Pose switch
        {
            Pose.Working => StressWorking,
            Pose.Sleeping => StressSleeping,
            _ => StressIdle,
        };
        if (relaxing) stress = StressRelaxing;
        if (n.Food < 0.15f) stress += StressHungry;
        if (n.Rest < 0.15f) stress += StressExhausted;
        if (n.Social < 0.2f) stress += StressLonely;
        if (hypoxic) stress += 0.06f;
        if (vacuum && !c.Dead) stress += 0.04f * (1.3f - c.Traits.Bravery); // 발밑이 우주다
        if (rough) stress += 0.01f;
        if (stuffy) stress += 0.04f;
        if (air != null && !suited && (air.Temperature < 12f || air.Temperature > 32f)) stress += 0.03f;
        if (air != null && air.Smoke > 0.2f) stress += 0.08f;
        if (air != null && !suited && air.Toxin > 0.1f) stress += 0.1f; // v11.2 매캐한 냄새
        if (w.Food.Rationing && !asleep) stress += 0.012f * c.Traits.Appetite; // v10.11 배급: 늘 조금 배고프다
        stress += 0.05f * v.Injury; // 아프다

        // ── v7 기억: 악몽, 긴장(기저치), 전우 ──
        var mem = c.Memory;
        if (dread > 0.1f) stress += 0.05f * dread; // 거기서 겪은 일이 꿈에 나온다
        if (stress > 0f && mem.Trauma > 0f) stress *= 1f + 1.5f * mem.Trauma; // 긴장한 사람은 쉽게 지친다
        if (stress > 0f && c.Pose == Pose.Working && mem.Comrades.Count > 0)
            foreach (var o in w.Crew)
                if (!o.Dead && o != c && mem.Comrades.Contains(o.Id) && (o.Position - c.Position).LengthSquared() < 16f)
                {
                    stress *= 0.75f; // 전우 곁에서 일하면 덜 힘들다
                    break;
                }
        n.Stress += stress * dt;

        // ── 체력 ──
        float damage = 0f;
        // 저산소: 혈중 산소가 30% 밑이면 의식을 잃고 몇십 분 안에 위험해진다
        if (v.Oxygen < 0.3f) damage += 1.2f;
        else if (v.Oxygen < 0.6f) damage += 0.15f * (1f - v.Oxygen / 0.6f) + 0.03f;
        if (co2 > 3f) damage += 0.05f * (co2 - 3f);
        if (n.Food <= 0.01f) damage += 0.02f;
        if (air != null && !suited && (air.Temperature < 0f || air.Temperature > 50f)) damage += 0.05f;
        if (!suited && pressure < 25f)                                   // 감압 손상 (폐·고막 — 오래 남는다)
        {
            damage += 1.5f;
            AddInjury(v, 0.3f * dt, "감압");
        }
        if (!suited && air != null && air.Smoke > 0.4f) damage += 0.08f * air.Smoke; // 연기 흡입
        if (!suited && air != null && air.Toxin > 0.15f)                  // v11.2 유독 가스 흡입 (폐에 남는다)
        {
            damage += 0.2f * air.Toxin;
            AddInjury(v, 0.04f * air.Toxin * dt, "유독 가스");
        }

        // 쓰러진 채 치료 침대에 눕혀졌으면 침대가 돌봐 준다
        bool inCare = c.Down && c.CareBed?.Machine is Machine bed && bed.Efficiency > 0f;
        if (damage > 0f) v.Health -= damage * dt;
        else if (inCare) v.Health += 0.12f * dt;
        else if (n.Food > 0.2f && n.Rest > 0.2f && !c.Down) v.Health += (asleep ? 0.05f : 0.02f) * dt;
        else if (c.Down && n.Food > 0.1f) v.Health += 0.01f * dt;

        // 부상은 며칠에 걸쳐 낫는다 (치료 침대에서 훨씬 빨리)
        if (damage <= 0f && v.Injury > 0f)
        {
            bool resting = inCare || (c.Job?.Activity is RecoverActivity && c.Pose == Pose.Sleeping);
            float perDay = resting ? 0.3f : asleep ? 0.08f : 0.03f;
            v.Injury = MathF.Max(0f, v.Injury - perDay / 24f * dt);
            if (v.Injury == 0f) v.InjuryCause = null;
        }
        v.Health = Math.Clamp(v.Health, w.CrewCanDie ? 0f : 0.02f, v.MaxHealth);

        n.Clamp();
        if (n.Stress < mem.Trauma) n.Stress = mem.Trauma; // 긴장은 쉬어도 이 아래로 풀리지 않는다

        switch (c.Pose)
        {
            case Pose.Sleeping: c.Stats.TicksAsleep++; break;
            case Pose.Working: c.Stats.TicksWorking++; break;
        }
        if (relaxing) c.Stats.TicksRelaxing++;
        if (n.Food <= 0.02f) c.Stats.TicksStarving++;
    }
}
