using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>
/// v10.11 대인원 생활: 먹을 것이 며칠치 남았나, 배급.
/// 사람이 많은 배는 사고 한 번에 조리·재배가 밀리면 먹을 것이 며칠 만에 바닥난다. 이틀치 아래로 떨어지면 회의로 배급을 정한다 —
/// 배급 중엔 한 끼를 줄여 허기가 덜 빠지고(먹는 양이 준다), 대신 배고픔에 날카로워지고(스트레스) 손이 조금 느려진다.
/// 나흘치 넘게 열두 시간을 버티면 푼다. 배급을 겪은 시간은 굶주림의 절반으로 쳐서 재배대 증설의 교훈이 된다.
/// </summary>
public sealed class FoodPolicy
{
    /// <summary>배급 중인가.</summary>
    public bool Rationing { get; internal set; }
    public long RationingSince { get; internal set; } = -1;
    public int Rationings { get; internal set; }
    public float RationHours { get; internal set; }

    /// <summary>먹을 것이 넉넉해진 때 (배급을 풀 때를 본다).</summary>
    public long PlentySince { get; internal set; } = -1;

    /// <summary>시험용: 배급을 아예 하지 않는 배 (견줄 때).</summary>
    public bool Disabled { get; set; }

    /// <summary>배급 중 허기가 빠지는 몫.</summary>
    public const float RationDecay = 0.72f;

    /// <summary>v13.2 방침(식량 배급): 이 사람의 허기가 빠지는 몫 — 똑같이 / 일하는 사람 먼저 / 아픈 사람 먼저.</summary>
    public float Decay(World w, CrewMember c)
    {
        if (!Rationing) return 1f;
        return w.Policies["rations"] switch
        {
            1 => c.Job?.Activity is ChoresActivity || w.Command.TeamOf(c) is { Kind: not TeamKind.Reserve } ? 0.88f : 0.64f,
            2 => c.Down || c.CareBed != null || c.Vitals.Injury > 0.2f || c.Vitals.Health < 0.6f || DiseaseSystem.Sick(c) ? 0.92f : 0.66f,
            _ => RationDecay,
        };
    }

    /// <summary>한 사람이 하루에 먹는 몫 (끼니 기준, 식사 1 · 비상식량 1 · 채소는 조리하면 1.5배).</summary>
    public const float MealsPerPersonDay = 2.6f;

    /// <summary>먹을 것 (끼니로 친다): 식사 + 비상식량 + 채소(조리하면 늘어난다).</summary>
    public static float FoodStock(World w) =>
        w.Ship.CountStored(ItemKind.Meal) + w.Ship.CountStored(ItemKind.Ration)
        + w.Ship.CountStored(ItemKind.Produce) * (FoodChain.MealsPerBatch / (float)FoodChain.ProducePerBatch);

    /// <summary>지금 먹을 것이 며칠치인가 (산 사람 수로).</summary>
    public static float FoodDays(World w)
    {
        int crew = w.Crew.Count(c => !c.Dead);
        if (crew == 0) return 99f;
        return FoodStock(w) / (crew * MealsPerPersonDay);
    }

    /// <summary>재배대가 지금 얼마나 대 주나 (익어 가는 작물 · 하루 몇 끼).</summary>
    public static float GrowingPerDay(World w) =>
        w.Ship.FurnitureOf(FurnitureType.GrowBed).Where(f => !f.Room.Abandoned && f.Machine!.Efficiency > 0f && f.Machine.Crop != null)
            .Sum(f => FoodChain.HarvestYield * FoodChain.BedSize(f) * f.Machine!.Efficiency * f.Machine.Rating * 24f / FoodChain.GrowHours * FoodSourceSystem.YieldMul(f) * FoodSourceSystem.GrowMul(f.Machine)) // v16.22 재배실마다
        * (FoodChain.MealsPerBatch / (float)FoodChain.ProducePerBatch);

    public void Update(World w, float dt)
    {
        if (Rationing) RationHours += dt * w.Crew.Count(c => !c.Dead);
        float days = FoodDays(w);
        if (days > 4f) { if (PlentySince < 0) PlentySince = w.Tick; }
        else PlentySince = -1;
    }
}

public sealed partial class WorkBoard
{
    /// <summary>v10.11 배급: 먹을 것이 이틀치 아래면 회의로 정하고, 나흘치 넘게 열두 시간을 버티면 푼다.</summary>
    private void ScanLiving(Poster post)
    {
        var w = _world;
        var f = w.Food;
        int crew = w.Crew.Count(c => !c.Dead);
        if (crew < 2 || f.Disabled) return;
        // 배급표는 주방(조리대)에, 없으면 식당 배식기에 붙인다
        var board = w.Ship.FurnitureOf(FurnitureType.Stove).FirstOrDefault(s => !s.Room.Abandoned && s.UseSpots.Count > 0)
                    ?? w.Ship.FurnitureOf(FurnitureType.MealDispenser).FirstOrDefault(s => !s.Room.Abandoned && s.UseSpots.Count > 0);
        if (board == null) return;
        float days = FoodPolicy.FoodDays(w);
        float grow = FoodPolicy.GrowingPerDay(w);
        float need = crew * FoodPolicy.MealsPerPersonDay;
        float below = w.Policies["rations"] == 3 ? 4f : 2f; // v13.2 방침(식량 배급: 줄인다) — 나흘치 아래면 미리
        below = MathF.Max(below, w.Automation.RationLead); // v16.6 식단 계획 모듈 — 바닥나는 날을 먼저 보고 하루 앞당긴다
        if (!f.Rationing && days < below && grow < need * 1.05f)
            post(WorkKind.Ration, WorkTarget.Of(board), 0.6f + MathF.Min(0.3f, (2f - days) * 0.2f), Skill.Cooking,
                $"먹을 것 {days:0.0}일치 ({FoodPolicy.FoodStock(w):0}끼 · {crew}명) · 재배대가 하루 {grow:0}끼를 대는데 {need:0}끼를 먹는다");
        if (f.Rationing && f.PlentySince >= 0 && w.Tick - f.PlentySince > SimTime.Hours(12) && (w.Policies["rations"] != 3 || days > 6f))
            post(WorkKind.EndRation, WorkTarget.Of(board), 0.35f, Skill.Cooking, $"먹을 것 {days:0.0}일치 — 열두 시간째 넉넉하다");
    }
}

public static partial class WorkPlanners
{
    /// <summary>배급표를 붙인다/뗀다 (주방에서 배식기 몫을 다시 짠다).</summary>
    private static Job SetRation(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at)
    {
        bool on = o.Kind == WorkKind.Ration;
        var board = o.Target.Furniture!;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.25f, Skill.Cooking, board.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            var f = world.Food;
            world.Board.Close(o);
            if (on == f.Rationing) return true;
            float days = FoodPolicy.FoodDays(world);
            if (on)
            {
                f.Rationing = true;
                f.RationingSince = world.Tick;
                f.Rationings++;
                world.Adapt.Rationings++;
                world.History.Add(world, HistoryKind.Adaptation,
                    $"{Ko.IGa(cm.Name)} 배급표를 붙였다 — 먹을 것이 {days:0.0}일치뿐이다: 한 끼씩 줄여 먹는다 (허기는 덜 빠지고, 대신 날카로워진다)", board.Room, new[] { cm });
                world.RaiseAlert($"배급 시작 — 먹을 것 {days:0.0}일치", board.Room, AlertLevel.Warning, shipWide: true);
                foreach (var x in world.Crew.Where(x => !x.Dead)) MarkLog.Add(x.Memory.Marks, world.Tick, $"배급이 시작됐다 ({days:0.0}일치)");
            }
            else
            {
                float hours = (world.Tick - f.RationingSince) / (float)SimTime.TicksPerHour;
                f.Rationing = false;
                world.History.Add(world, HistoryKind.Response, $"{Ko.IGa(cm.Name)} 배급표를 뗐다 ({hours / 24f:0.0}일 만) — 먹을 것 {days:0.0}일치", board.Room, new[] { cm }, log: true);
            }
            world.Board.RequestScan();
            return true;
        }));
        return Wrap(a, o, c, w, on ? "배급" : "배급 해제", toils, on ? "주방에 배급표를 붙이러 간다" : "주방의 배급표를 떼러 간다");
    }
}

public static partial class Council
{
    /// <summary>배급 회의의 압박: 며칠치가 적을수록, 굶는 사람이 있을수록.</summary>
    internal static float RationPressure(World w)
    {
        float days = FoodPolicy.FoodDays(w);
        bool starving = w.Crew.Any(c => !c.Dead && c.Needs.Food < 0.1f);
        return 0.5f + MathF.Min(0.4f, (2f - days) * 0.25f) + (starving ? 0.15f : 0f);
    }

    /// <summary>배급에 대한 찬반: 부엌을 맡은 사람은 찬성, 많이 먹는 사람·다친 사람을 돌보는 의무관은 반대.</summary>
    internal static void RationTerms(World w, CrewMember c, List<(float v, string why)> terms, float pressure)
    {
        var t = c.Traits;
        float days = FoodPolicy.FoodDays(w);
        bool hurt = w.Crew.Any(x => !x.Dead && (x.Down || x.Vitals.Injury > 0.25f));
        if (c.Role is CrewRole.Botanist or CrewRole.Cook || c.Stations.Contains(RoomType.Galley))
            terms.Add((0.3f, $"먹을 것이 {days:0.0}일치뿐이다"));
        if (c.Role is CrewRole.Engineer or CrewRole.Pilot) terms.Add((0.1f, "아껴야 버틴다"));
        if (t.Appetite > 1.08f) terms.Add((-0.25f * (t.Appetite - 0.9f) / 0.3f, "배고파서는 일을 못 한다"));
        if (c.Role == CrewRole.Medic && hurt) terms.Add((-0.2f, "다친 사람은 먹어야 낫는다"));
        terms.Add((0.12f * t.Diligence, "있는 만큼 나눠 먹자"));
        terms.Add((-0.1f * (1f - t.Calm), "배고프면 다들 날카로워진다"));
        terms.Add((MathF.Max(0f, pressure - 0.75f), "곧 바닥난다"));
    }
}
