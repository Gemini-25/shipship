using System;
using System.Linq;

namespace ShipSim.Core;

/// <summary>
/// 방 단위 공기. 사람은 산소를 쓰고 이산화탄소를 내뿜고,
/// 생명유지실의 산소 발생기가 환기망을 통해 "전기가 들어오고 댐퍼가 열린" 방에만 산소를 채우고 CO2를 걸러낸다.
/// 열린 문으로는 두 방의 공기가 섞인다. 선체가 뚫리면 우주로 빠져나간다.
/// 감압된 방을 다시 채우는 공기(질소)는 공기 탱크에서만 온다 — 반복된 파공은 탱크를 비운다.
/// 수치는 게임용으로 실제보다 빠르게 잡았다.
/// </summary>
public sealed class Atmosphere
{
    /// <summary>승무원 한 명이 한 시간에 쓰는 산소 (kPa·칸). 30칸 방이면 시간당 0.5kPa.</summary>
    public const float BreathO2 = 14f;
    public const float BreathCO2 = 12f;

    /// <summary>산소 발생기 한 대의 능력 (kPa·칸/시간).</summary>
    public static float GeneratorCapacity = 110f;

    public const float TargetO2 = 21f;
    public const float TargetCO2 = 0.04f;

    /// <summary>전기가 들어오는 방끼리 환기구로 천천히 섞이는 속도 (시간당 비율).</summary>
    public const float VentMixPerHour = 0.6f;

    /// <summary>공기 탱크에서 재가압하는 최대 속도 (kPa·칸/시간).</summary>
    public const float RefillRate = 5000f;

    private readonly World _world;

    public float O2Produced { get; private set; }
    public float CO2Scrubbed { get; private set; }
    public float O2Capacity { get; private set; }

    /// <summary>v11.1: 돌아가는 산소 발생기가 하나도 없어진 때 (-1이면 있다).</summary>
    public long NoGeneratorSince { get; private set; } = -1;

    /// <summary>공기 탱크 잔량 (kPa·칸). 72칸 방을 한 번 채우는 데 약 7,300.</summary>
    public float Reserve { get; set; } = 26000f;
    public float ReserveCapacity { get; internal set; } = 26000f;

    /// <summary>환기망에 연결된 방 (전기 + 댐퍼 열림).</summary>
    public static bool Vented(Room r) => r.Powered && r.VentOpen && r.DuctLinked;

    public Atmosphere(World world) => _world = world;

    /// <summary>환기관 흐름 세기: 팬이 도는 방 1, 전기가 없어 수동으로만 흐르는 방 0.35.</summary>
    private static float DuctFactor(Room r) => !r.DuctLinked ? 0f : r.Powered ? 1f : 0.35f; // 덕트가 끊긴 방은 환기망에서 빠진다

    public void Update(float dtHours)
    {
        var ship = _world.Ship;

        // 0) 떨어져 나간 방은 우주다. 댐퍼가 열린 채 떨어졌으면 환기관이 우주로 열려 다른 방 공기가 빨려 나간다.
        foreach (var r in ship.Rooms)
        {
            if (!r.Detached) continue;
            r.Air.O2 = 0f; r.Air.N2 = 0f; r.Air.CO2 = 0f; r.Air.Smoke = 0f; r.Air.Toxin = 0f; r.Air.CO = 0f;
            r.Air.Temperature = -20f;
        }

        // 1) 호흡
        foreach (var c in _world.Crew)
        {
            var room = c.Room;
            if (room == null || c.Dead) continue;
            room.Air.O2 = MathF.Max(0f, room.Air.O2 - BreathO2 * dtHours / room.Volume);
            room.Air.CO2 += BreathCO2 * dtHours / room.Volume;
        }

        // 2) 생명유지: 산소 보충, CO2 제거 (환기팬이 도는 방만)
        float capacity = ship.FurnitureOf(FurnitureType.OxygenGenerator)
            .Sum(f => f.Machine!.Efficiency * f.Machine.Rating) * GeneratorCapacity * (1f + Modules.Bonus(_world, FurnitureType.Scrubber)) * ErasV15.Mul(_world, "o2"); // v10.6 세정 모듈 · v15.5 흡착제·광합성
        O2Capacity = capacity;
        bool anyGen = ship.FurnitureOf(FurnitureType.OxygenGenerator).Any(f => !f.Stowed && !f.Room.Detached && !f.Room.Abandoned && !f.Machine!.Has(FaultKind.Wrecked));
        if (anyGen) NoGeneratorSince = -1;
        else if (NoGeneratorSince < 0) NoGeneratorSince = _world.Tick;
        float budget = capacity * dtHours;
        float needO2 = 0f, needCO2 = 0f;
        foreach (var r in ship.Rooms)
        {
            if (!Vented(r) || r.Leaking) continue;
            needO2 += MathF.Max(0f, TargetO2 - r.Air.O2) * r.Volume;
            needCO2 += MathF.Max(0f, r.Air.CO2 - TargetCO2) * r.Volume;
        }
        float giveO2 = MathF.Min(budget, needO2);
        float takeCO2 = MathF.Min(budget, needCO2);
        foreach (var r in ship.Rooms)
        {
            if (!Vented(r) || r.Leaking) continue;
            if (needO2 > 0f) r.Air.O2 += giveO2 * (MathF.Max(0f, TargetO2 - r.Air.O2) * r.Volume / needO2) / r.Volume;
            if (needCO2 > 0f) r.Air.CO2 -= takeCO2 * (MathF.Max(0f, r.Air.CO2 - TargetCO2) * r.Volume / needCO2) / r.Volume;
        }
        O2Produced = dtHours > 0 ? giveO2 / dtHours : 0f;
        CO2Scrubbed = dtHours > 0 ? takeCO2 / dtHours : 0f;

        // 2-b) 재가압: 기압이 모자란 방에 공기 탱크에서 채운다 (새는 방에는 안 넣는다)
        float refillEff = ship.FurnitureOf(FurnitureType.OxygenGenerator).Select(f => f.Machine!.Efficiency).DefaultIfEmpty(0f).Max();
        float refillBudget = MathF.Min(Reserve, RefillRate * MathF.Max(0.3f, refillEff) * dtHours);
        if (refillEff > 0f && refillBudget > 0f)
        {
            // v13.0 공기 구역: 탱크가 절반 아래면 지킬 구역부터 채운다
            bool zoneOnly = _world.Automation.ZoneActive && Reserve < ReserveCapacity * 0.5f;
            bool Fill(Room r) => Vented(r) && !r.Leaking && (!zoneOnly || _world.Automation.InZone(r));
            float need = 0f;
            foreach (var r in ship.Rooms)
                if (Fill(r)) need += MathF.Max(0f, 99f - r.Air.Pressure) * r.Volume;
            if (need > 1f)
            {
                float give = MathF.Min(refillBudget, need);
                foreach (var r in ship.Rooms)
                {
                    if (!Fill(r)) continue;
                    float share = give * MathF.Max(0f, 99f - r.Air.Pressure) * r.Volume / need / r.Volume;
                    r.Air.N2 += share * 0.79f;
                    r.Air.O2 += share * 0.21f;
                }
                Reserve -= give;
            }
        }

        // 3) 환기망: 댐퍼가 열린 방끼리 평균 쪽으로 섞인다. 전기가 없는 방은 팬이 안 돌아 느리게(수동 흐름)만.
        //    감압된 방의 댐퍼가 열려 있으면 환기관을 타고 다른 방 공기까지 빨려 나간다 — 그래서 댐퍼를 닫는다.
        float vol = 0f, o2 = 0f, co2 = 0f, n2 = 0f, smoke = 0f, toxin = 0f, co = 0f;
        foreach (var r in ship.Rooms)
        {
            if (!r.VentOpen) continue;
            float wv = r.Volume * DuctFactor(r);
            vol += wv; o2 += r.Air.O2 * wv; co2 += r.Air.CO2 * wv; n2 += r.Air.N2 * wv;
            smoke += r.Air.Smoke * wv;
            toxin += r.Air.Toxin * wv;
            co += r.Air.CO * wv;
        }
        if (vol > 0f)
        {
            float k = 1f - MathF.Exp(-VentMixPerHour * dtHours);
            foreach (var r in ship.Rooms)
            {
                if (!r.VentOpen) continue;
                float kr = k * DuctFactor(r);
                r.Air.O2 += (o2 / vol - r.Air.O2) * kr;
                r.Air.CO2 += (co2 / vol - r.Air.CO2) * kr;
                r.Air.N2 += (n2 / vol - r.Air.N2) * kr;
                r.Air.Smoke += (smoke / vol - r.Air.Smoke) * kr;
                r.Air.Toxin += (toxin / vol - r.Air.Toxin) * kr;
                r.Air.CO += (co / vol - r.Air.CO) * kr;
            }
        }

        // 4) 열린 문: 두 방이 섞인다
        foreach (var d in ship.Doors)
        {
            if (d.Openness < 0.05f || d.RoomA == null || d.RoomB == null) continue;
            Mix(d.RoomA, d.RoomB, 0.25f * d.Openness);
        }

        // 5) 새는 곳: 기압에 비례해 우주로 빠져나간다 (지수 감소)
        foreach (var r in ship.Rooms)
        {
            if (r.Air.Leak <= 0f) continue;
            float keep = MathF.Exp(-r.Air.Leak * dtHours / r.Volume);
            r.Air.O2 *= keep; r.Air.N2 *= keep; r.Air.CO2 *= keep; r.Air.Smoke *= keep; r.Air.Toxin *= keep; r.Air.CO *= keep;
            r.Air.Temperature += (-20f - r.Air.Temperature) * (1f - keep) * 0.5f;
        }

        // 5-b) v13.0 진공 소화: 배기 밸브로 몇 분 만에 비운다 · 질식 소화: 불활성 가스가 산소를 밀어낸다 (기압은 그대로)
        foreach (var r in ship.Rooms)
        {
            if (r.Detached) continue;
            if (r.Purging)
            {
                float keep = MathF.Exp(-AutomationSystem.PurgeRate * dtHours); // 2~3분이면 30kPa 아래
                r.Air.O2 *= keep; r.Air.N2 *= keep; r.Air.CO2 *= keep; r.Air.Smoke *= keep; r.Air.Toxin *= keep; r.Air.CO *= keep;
                r.Air.Temperature += (-20f - r.Air.Temperature) * (1f - keep) * 0.5f;
            }
            if (r.Flushing && r.Air.O2 < 20.5f && Reserve > 0f)
            {
                // 질식 소화 뒤: 탱크 공기를 넣고 그만큼 배기 — 기압은 그대로, 산소만 돌아온다 (10분쯤)
                float dx = MathF.Min(MathF.Min(21f - r.Air.O2, 120f * dtHours), Reserve / MathF.Max(1f, r.Volume) * 0.21f);
                if (dx > 0f)
                {
                    float air = dx / 0.21f;
                    float scale = MathF.Max(0f, 1f - air / MathF.Max(1f, r.Air.Pressure));
                    r.Air.N2 *= scale; r.Air.CO2 *= scale; r.Air.CO *= scale; r.Air.Smoke *= scale; r.Air.O2 *= scale;
                    r.Air.O2 += air * 0.21f; r.Air.N2 += air * 0.79f;
                    Reserve -= air * r.Volume;
                }
            }
            if (r.Inerting && r.Air.O2 > 4f)
            {
                float want = MathF.Min(r.Air.O2 - 4f, AutomationSystem.InertRate * dtHours);
                float can = MathF.Min(want, _world.Automation.InertGas / MathF.Max(1f, r.Volume));
                if (can > 0f)
                {
                    r.Air.O2 -= can; r.Air.N2 += can;
                    r.Air.Smoke *= 0.97f;
                    _world.Automation.InertGas -= can * r.Volume;
                }
            }
        }

        // 6) 온도: 전기가 있으면 21℃로 조절, 없으면 서서히 식는다. 원자로·엔진은 열이 난다.
        float reactorRatio = _world.Power.CoolingCapacity > 0.1f ? _world.Power.ReactorOutput / _world.Power.CoolingCapacity : 1f;
        foreach (var r in ship.Rooms)
        {
            float target = r.Powered ? 21f : 6f;
            if (r.Type == RoomType.Reactor) target += 6f + 20f * MathF.Max(0f, reactorRatio - 0.7f);
            if (r.Type == RoomType.Engine) target += 3f;
            target += _world.Automation.HeatFor(r); // v9.2: 주 컴퓨터 열 (환기가 끊기면 크게)
            if (r.Type == RoomType.Galley && _world.Ship.FurnitureOf(FurnitureType.Stove).Any(s => s.Machine!.Active)) target += 3f;
            float rate = r.Powered ? 1.2f : 0.12f;
            r.Air.Temperature += (target - r.Air.Temperature) * (1f - MathF.Exp(-rate * dtHours));
        }

        // 7) 길찾기용 위험 비용
        foreach (var r in ship.Rooms) UpdateHazard(r);
    }

    private static void Mix(Room a, Room b, float rate)
    {
        float va = a.Volume, vb = b.Volume;
        float w = va * vb / (va + vb);
        float Flow(float pa, float pb) => (pa - pb) * w * rate;
        float f;
        f = Flow(a.Air.O2, b.Air.O2); a.Air.O2 -= f / va; b.Air.O2 += f / vb;
        f = Flow(a.Air.N2, b.Air.N2); a.Air.N2 -= f / va; b.Air.N2 += f / vb;
        f = Flow(a.Air.CO2, b.Air.CO2); a.Air.CO2 -= f / va; b.Air.CO2 += f / vb;
        f = Flow(a.Air.Temperature, b.Air.Temperature) * 0.3f; a.Air.Temperature -= f / va; b.Air.Temperature += f / vb;
        f = Flow(a.Air.Smoke, b.Air.Smoke); a.Air.Smoke -= f / va; b.Air.Smoke += f / vb;
        f = Flow(a.Air.Toxin, b.Air.Toxin); a.Air.Toxin -= f / va; b.Air.Toxin += f / vb;
        f = Flow(a.Air.CO, b.Air.CO); a.Air.CO -= f / va; b.Air.CO += f / vb;
    }

    private static void UpdateHazard(Room r)
    {
        var air = r.Air;
        int cost = 0;
        if (air.O2 < 17f) cost += 6;
        if (air.O2 < 13f) cost += 15;
        if (air.CO2 > 1.5f) cost += 5;
        if (air.CO2 > 3f) cost += 15;
        if (air.Temperature > 40f || air.Temperature < 5f) cost += 12;
        if (air.Smoke > 0.2f) cost += (int)(air.Smoke * 20f);
        if (air.Toxin > 0.1f) cost += (int)(MathF.Min(1f, air.Toxin) * 30f); // v11.2 유독 가스
        if (r.CoKnown || air.CO > 0.3f) cost += (int)(MathF.Min(1f, air.CO) * 40f); // v12.2 일산화탄소 (경보가 울렸거나 머리가 아플 만큼)
        if (r.BackdraftKnown) cost += 60; // v12.2 역화 위험 — 문을 열지 않는다
        if (air.Pressure < 70f) cost += 20;
        if (r.Dark) cost += r.Powered ? 3 : 2; // v9.4: 조명이 나간 방도 꺼린다
        r.HazardCost = cost;
        r.Unbreathable = air.Pressure < 40f;
    }

    /// <summary>이 사람에게 이 방이 얼마나 위험한지. 우주복을 입으면 숨 쉬는 문제는 사라진다.</summary>
    public static float DangerFor(CrewMember c, Room r)
    {
        if (c.Suit is { Oxygen: > 0.05f })
            return MathF.Max(Curve.Smooth(r.Air.Temperature, 50f, 80f), 0f);
        return Danger(r);
    }

    /// <summary>방 공기가 사람에게 얼마나 위험한지 0~1.</summary>
    public static float Danger(Room r)
    {
        var air = r.Air;
        float d = 0f;
        d = MathF.Max(d, Curve.Smooth(17f - air.O2, 0f, 6f));
        d = MathF.Max(d, Curve.Smooth(air.CO2, 2f, 5f));
        d = MathF.Max(d, Curve.Smooth(air.Temperature, 42f, 60f));
        d = MathF.Max(d, Curve.Smooth(80f - air.Pressure, 0f, 40f));
        d = MathF.Max(d, air.Smoke);
        d = MathF.Max(d, Curve.Smooth(air.Toxin, 0.1f, 0.6f)); // v11.2 유독 가스 (우주복이면 괜찮다)
        if (r.CoKnown || air.CO > 0.3f) d = MathF.Max(d, Curve.Smooth(air.CO, 0.08f, 0.5f)); // v12.2 일산화탄소 (알아야 피한다)
        if (r.BackdraftKnown) d = MathF.Max(d, 0.6f); // v12.2 역화 위험
        return d;
    }
}
