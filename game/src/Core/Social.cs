using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>
/// 대화. 외로우면 쉬거나 서성이는 동료에게 다가가 이야기를 나눈다.
/// 호감이 쌓이고, 둘 다 예민할 때는 말다툼으로 끝나기도 한다.
/// </summary>
public sealed class ChatActivity : Activity
{
    public override string Id => "chat";
    public override string Label => "대화";

    private static bool Available(CrewMember o) =>
        o.IsAwake && !o.IsMoving && o.Vitals.Health > 0.4f
        && (o.Job?.Activity is RelaxActivity or WanderActivity or EatActivity or null);

    private static IEnumerable<(CrewMember who, Cell spot, float value)> Partners(CrewMember c, World w, DistanceField dist)
    {
        foreach (var o in w.Crew)
        {
            if (o == c || !Available(o)) continue;
            Cell? best = null;
            int bestCost = int.MaxValue;
            foreach (var d in Cell.Dirs8)
            {
                var s = o.Cell + d;
                int cost = dist.Get(s);
                if (cost < 0 || !w.Ship.IsOpenFloor(s) || w.IsSpotTaken(s, c) || cost >= bestCost) continue;
                best = s;
                bestCost = cost;
            }
            if (best is not Cell spot) continue;
            float value = c.AffinityTo(o) + 0.3f - bestCost / 1500f;
            yield return (o, spot, value);
        }
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var best = Partners(c, w, dist).OrderByDescending(p => p.value).FirstOrDefault();
        if (best.who == null) return (0f, "이야기할 사람 없음");
        float lonely = 1f - c.Needs.Social;
        float score = 0.08f + lonely * 0.55f * (0.5f + c.Traits.Sociability) + best.value * 0.12f;
        if (SimTime.InWindow(Hour(w), c.Schedule.WorkStart, c.Schedule.WorkLength)) score -= 0.1f;
        if (SimTime.InWindow(Hour(w), c.Schedule.SleepStart, c.Schedule.SleepLength)) score -= 0.4f;
        return (MathF.Max(0f, score), $"{Ko.WaGwa(best.who.Name)} 이야기하고 싶음");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var options = Partners(c, w, dist).ToList();
        if (options.Count == 0) return null;
        var (partner, spot, _) = options.OrderByDescending(p => p.value + w.Rng.Range(0f, 0.3f)).First();

        int length = SimTime.Minutes(w.Rng.Range(15f, 28f));
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(spot));
        toils.Add(new WaitToil(length, Pose.Standing, partner.Position)
        {
            EveryTick = (cm, world) =>
            {
                const float dt = 1f / SimTime.TicksPerHour;
                if ((partner.Position - cm.Position).Length() > 2.2f || !partner.IsAwake) return;
                cm.TalkingTo = partner;
                partner.TalkingTo = cm;
                cm.Facing = System.Numerics.Vector2.Normalize(partner.Position - cm.Position + new System.Numerics.Vector2(0.0001f, 0f));
                cm.Needs.Social += 1.4f * dt;
                partner.Needs.Social += 1.1f * dt;
                float warmth = 0.12f * (0.6f + 0.4f * (cm.Traits.Sociability + partner.Traits.Sociability) * 0.5f);
                cm.ChangeAffinity(partner, warmth * dt);
                partner.ChangeAffinity(cm, warmth * dt);
            },
            DoneWhen = (_, _) => !partner.IsAwake || (partner.IsMoving && partner.Job?.Activity is not ChatActivity),
        });
        toils.Add(new DoToil((cm, world) =>
        {
            if (partner.TalkingTo == cm) partner.TalkingTo = null;
            cm.TalkingTo = null;
            cm.Stats.Chats++;
            // 둘 다 예민하면 말다툼으로 끝난다
            if (cm.Needs.Stress > 0.55f && partner.Needs.Stress > 0.45f && world.Rng.Chance(0.35f))
            {
                cm.ChangeAffinity(partner, -0.08f);
                partner.ChangeAffinity(cm, -0.06f);
                cm.Needs.Stress += 0.05f;
                partner.Needs.Stress += 0.05f;
                world.Log.Add(world.Tick, LogKind.Life, $"{Ko.WaGwa(partner.Name)} 말다툼을 했다", cm.Id);
            }
            return true;
        }));
        return new Job(this, "대화", toils)
        {
            LogText = $"{partner.Name}에게 말을 건다",
            TargetRoom = partner.Room,
        };
    }
}
