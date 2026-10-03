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
        amount *= Storyteller.InjuryScale; // v12.4 난이도: 부상 강도
        if (v.Injury < 0.05f || amount > MathF.Max(0.05f, v.Injury * 0.5f)) v.InjuryCause = cause; // 큰 상처를 낸 것이 사인으로 남는다 (작은 상처가 덮어쓰지 않는다)
        v.Injury = MathF.Min(1f, v.Injury + amount);
        ShipSim.Core.Wounds.Add(v, amount, cause); // v12.7 어디를 어떻게
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
        bool relaxing = c.Pose == Pose.Sitting && c.Job?.Activity is RelaxActivity || c.Job?.Activity is HobbyActivity && c.Pose is Pose.Sitting or Pose.Standing && c.Job.Current is WaitToil; // v14.3 취미 (운동은 서서)
        var air = c.Room?.Air;

        // ── 공기 → 혈중 산소 (우주복이면 탱크에서) ──
        bool suited = c.Suit is { Oxygen: > 0f };
        if (c.Suit != null) c.Suit.Oxygen = MathF.Max(0f, c.Suit.Oxygen - dt * c.Suit.Leak);
        // 선체 밖은 진공이다 (v8: EVA, 떨어져 나간 방에서 튕겨 나감)
        bool vacuum = c.Outside;
        float pressure = vacuum ? 0f : air?.Pressure ?? 101f;
        float targetOx = suited ? 1f : vacuum ? 0f : air == null ? 1f : Curve.Clamp01((air.O2 - 8f) / 9f);
        // v12.2 일산화탄소는 핏속 산소 자리를 빼앗는다 (모르는 채로 졸리고, 쓰러진다)
        if (!suited && air != null && air.CO > 0.05f) targetOx *= 1f - 0.85f * MathF.Min(1f, air.CO);
        bool masked = !suited && air != null && pressure >= 40f && (air.O2 < 17f || air.Smoke > 0.4f) && w.CrisisCrew.Masked(c); // v16.21 산소 마스크 (진공에서는 소용없다)
        if (masked) targetOx = MathF.Max(targetOx, 0.92f);
        float oxRate = !suited && pressure < 40f ? 25f : 3f; // 진공에서는 몇 분 만에
        v.Oxygen += (targetOx - v.Oxygen) * MathF.Min(1f, oxRate * dt);
        float co2 = suited ? 0f : air?.CO2 ?? 0f;
        bool hypoxic = v.Oxygen < 0.85f;
        bool stuffy = co2 > 1f;

        // ── 욕구 ──
        // v10.11 배급: 한 끼씩 줄여 먹으니 허기가 덜 빠진다 (그만큼 날카로워진다 — 아래)
        float appetite = 1f, socialMul = 1f, restMulH = 1f, stressMul = 1f; // v14.0 습관
        foreach (var h in c.Habits) { var hs = Persona.Of(h); appetite *= hs.Appetite; socialMul *= hs.Social; restMulH *= hs.Rest; stressMul *= hs.Stress; }
        n.Food -= (asleep ? FoodDecayAsleep : FoodDecayAwake) * c.Traits.Appetite * appetite * w.Food.Decay(w, c) * dt;

        float restMul = (hypoxic ? 1.6f : 1f) * (stuffy ? 1.5f : 1f);
        // 침대가 아닌 곳(바닥, 의자)에서 자면 덜 쉰다
        var under = asleep ? w.Ship.FurnitureAt(c.Cell) : null;
        bool rough = asleep && !(under != null && FurnitureTypes.Sleepable(under.Type));
        // 손보지 않은 간이침대는 침대만 못하다 (v7: 침실 정비로 나아진다)
        bool cot = under is { Type: FurnitureType.Cot, Improved: false };
        // 무서운 방에서 자면 깊이 못 잔다 (v7)
        float dread = asleep ? c.Memory.FearOf(c.Room) : 0f;
        if (asleep) n.Rest += RestGainAsleep * restMulH * (1f - c.Fx.Sleep) * (c.Comfy && under != null && under == c.Bed ? 1.05f : 1f) * (stuffy ? 0.7f : 1f) * (rough ? 0.75f : 1f) * (cot ? 0.92f : 1f) * (1f - 0.3f * dread) * AmbienceSystem.SleepFactor(c.Room) * dt; // v12.6 옆방 소음·진동
        else n.Rest -= (c.Pose == Pose.Working ? RestDecayWorking : RestDecayAwake) * restMul * ShipSim.Core.Wounds.LungLoad(v) * dt; // v12.7 폐를 다치면 쉽게 지친다

        if (!asleep) n.Social -= SocialDecay * (0.6f + 0.8f * c.Traits.Sociability) * socialMul * dt;

        float stress = c.Pose switch
        {
            Pose.Working => StressWorking,
            Pose.Sleeping => StressSleeping,
            _ => StressIdle,
        };
        if (relaxing) stress = StressRelaxing * AmbienceSystem.RelaxFactor(c.Room) * (c.Hobbies.Count > 0 && Persona.HobbyIn(c, c.Room) != null ? 1.3f : 1f); // v12.6 관측실·정원은 더 풀린다 · v14.0 취미의 방
        else if (!asleep && !suited) stress += AmbienceSystem.Stress(c.Room, c.Pose == Pose.Working); // v12.6 시끄럽고 냄새나는 방
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
        if (stress > 0f) stress *= stressMul; // v14.0 습관 (낙천가는 덜, 걱정 많은 사람은 더)
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
            damage += pressure < 8f && v.Oxygen < 0.3f ? 5f : 1.5f; // v16.24 정신을 잃은 채 거의 진공이면 몇 분 — 우주복을 든 사람이 늦으면 못 산다
            AddInjury(v, 0.3f * dt, "감압");
        }
        if (!suited && !masked && air != null && air.Smoke > 0.4f) { damage += (air.Smoke > 0.7f ? 1.5f : 0.45f) * air.Smoke; AddInjury(v, 0.25f * air.Smoke * dt, "연기 흡입"); } // 통합: 짙은 연기는 사십 분 남짓이면 정신을 잃는다 (깨지 못한 잠 · 갇힌 사람) // 연기 흡입 (폐에 남는다) (v16.24 짙은 연기는 한 시간 남짓이면 쓰러진다)
        if (!suited && air != null && air.Toxin > 0.15f)                  // v11.2 유독 가스 흡입 (폐에 남는다)
        {
            damage += (air.Toxin > 0.4f ? 0.6f : 0.2f) * air.Toxin; // v16.24 짙으면 폐가 빨리 상한다
            AddInjury(v, 0.04f * air.Toxin * dt, "유독 가스");
        }

        // 쓰러진 채 치료 침대에 눕혀졌으면 침대가 돌봐 준다
        bool inCare = c.Down && c.CareBed?.Machine is Machine bed && bed.Efficiency > 0f;
        if (damage > 0f) v.Health -= damage * dt;
        else if (inCare) v.Health += 0.12f * w.Perils.MarrowMul(c) * dt; // 통합6 골수가 무너진 몸은 침대도 기운을 못 채운다
        else if (n.Food > 0.2f && n.Rest > 0.2f && !c.Down) v.Health += (asleep ? 0.05f : 0.02f) * (0.8f + 0.4f * c.Fitness) * w.Perils.MarrowMul(c) * dt; // v12.6 단련한 몸이 빨리 회복
        else if (c.Down && n.Food > 0.1f) v.Health += 0.01f * w.Perils.MarrowMul(c) * dt;

        // 부상은 며칠에 걸쳐 낫는다 (치료 침대에서 훨씬 빨리)
        if (damage <= 0f && v.Injury > 0f)
        {
            bool resting = inCare || (c.Job?.Activity is RecoverActivity && c.Pose == Pose.Sleeping);
            float perDay = (resting ? 0.3f : asleep ? 0.08f : 0.03f) * (w.Eras.Has("triage") ? 1.25f : 1f) * ErasV15.Mul(w, "heal"); // v12.8 응급 분류법 · v15.5 재생 의학
            perDay *= w.Recovery.HealMul(c); // 의료 1차 고정한 뼈 · 그냥 둔 골절 · 간이침대
            v.Injury = MathF.Max(0f, v.Injury - perDay / 24f * dt);
            if (v.Injury == 0f) { v.InjuryCause = null; ShipSim.Core.Wounds.Tidy(v); }
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
