using System;
using System.Linq;

namespace ShipSim.Core;

// v17.1 인형 사양: 화면이 그릴 자세 · 손에 든 것 · 팔 상태를 시뮬레이션 상태에서 읽어 정한다 (읽기만 — 결정론과 상관없다).
// 그림(View/ShipViewPuppet)과 헤드리스 시험(--looktest)이 같은 판정을 쓴다.

public enum PuppetPose : byte { Stand, Walk, Run, Sit, Kneel, Work, Lie, Crawl, Sleep }
public enum HeldThing : byte { None, Tool, Scissors, Cup, Box, Crate, Extinguisher, Mop, Plate, MedKit, Cable, Suit, Packet, Person }
/// <summary>팔: 멀쩡 · 다쳐 늘어뜨림 · 잃음 · 의수.</summary>
public enum ArmState : byte { Ok, Hurt, Lost, Prosthetic }

public readonly record struct PuppetSpec(PuppetPose Pose, HeldThing Held, bool TwoHands, ArmState Left, ArmState Right, bool Suit, float Misfit, bool Limp);

public static class Puppet
{
    /// <summary>확대 단계: 0 점 + 색 · 1 단순 인형 · 2 자세한 인형.</summary>
    public static int Lod(float zoom) => ZoomDetail.Lod(zoom); // v16.24 확대 3단계 (UiZoom)

    private static bool Says(string? label, params string[] keys)
    {
        if (label == null) return false;
        foreach (var k in keys) if (label.Contains(k, StringComparison.Ordinal)) return true;
        return false;
    }

    public static PuppetPose PoseOf(World w, CrewMember c)
    {
        if (c.Dead || c.Down) return PuppetPose.Lie;
        if (w.Body.Crawling(c)) return PuppetPose.Crawl;
        if (c.Pose == Pose.Sleeping) return PuppetPose.Sleep;
        if (c.Pose == Pose.Sitting) return PuppetPose.Sit;
        if (c.IsMoving || c.Pose == Pose.Walking)
            return c.Job?.Urgent == true || c.Dashing || c.Job?.Activity is JogActivity or EvacuateActivity or PanicActivity ? PuppetPose.Run : PuppetPose.Walk;
        if (c.Pose == Pose.Working)
        {
            string? label = c.Job?.Label;
            if (c.Job?.Activity is SweepClipsActivity || Says(label, "치료", "구조", "봉합", "걸레", "닦", "쓸기", "물기", "수확", "패치", "응급")) return PuppetPose.Kneel;
            foreach (var o in w.Crew)
                if (o != c && o.Down && !o.Dead && o.CarriedBy == null && (o.Position - c.Position).LengthSquared() < 2.6f) return PuppetPose.Kneel; // 쓰러진 사람 곁
            return PuppetPose.Work;
        }
        return PuppetPose.Stand;
    }

    public static HeldThing HeldOf(World w, CrewMember c)
    {
        if (c.CarryingPerson != null) return HeldThing.Person;
        if (c.Job?.Current is SprayToil) return HeldThing.Extinguisher;
        var job = c.Job;
        string? label = job?.Label;
        if (c.Carrying is ItemStack st)
            return st.Kind switch
            {
                ItemKind.Extinguisher => HeldThing.Extinguisher,
                ItemKind.Meal => HeldThing.Plate,
                ItemKind.Produce => HeldThing.Crate,
                ItemKind.Ration or ItemKind.Coffee or ItemKind.TeaLeaf or ItemKind.Spice or ItemKind.Vitamin => HeldThing.Packet,
                ItemKind.MedKit or ItemKind.Bandage or ItemKind.Disinfectant => HeldThing.MedKit,
                ItemKind.Cable or ItemKind.Fiber or ItemKind.Hose or ItemKind.Thread => HeldThing.Cable,
                ItemKind.Suit => HeldThing.Suit,
                ItemKind.Rag or ItemKind.Soap or ItemKind.Detergent => HeldThing.Mop,
                ItemKind.Structure or ItemKind.Plate or ItemKind.Motor or ItemKind.Pump or ItemKind.Fuel or ItemKind.MetalOre or ItemKind.Silicate or ItemKind.Carbon or ItemKind.Ice or ItemKind.ReactorControl => HeldThing.Crate,
                _ => st.Count > 4 ? HeldThing.Crate : HeldThing.Box,
            };
        if (job?.Activity is HaircutActivity && c.Pose == Pose.Working && w.Body2.SessionOf(c) is HairSession hs && (hs.Barber == c.Id)) return HeldThing.Scissors;
        if (job?.Activity is SweepClipsActivity || Says(label, "걸레", "닦", "쓸기", "물기")) return HeldThing.Mop;
        if (job?.Activity is EatActivity && c.Pose == Pose.Sitting) return HeldThing.Plate;
        if (Says(label, "커피", "차 한 잔") || (Life.Has(c, Habit.CoffeeAddict) || Life.Has(c, Habit.TeaLover)) && job?.Activity is RelaxActivity or ChatActivity
            && c.Room?.Type is RoomType.Mess or RoomType.Lounge or RoomType.Galley) return HeldThing.Cup;
        if (c.Pose == Pose.Working)
        {
            if (Says(label, "치료", "응급")) return HeldThing.MedKit;
            if (job?.Order != null || job?.Activity is ChoresActivity or MendActivity or RoomWorkActivity or SuitFitActivity or SuitMendActivity or BodyUpkeepActivity or PartTestActivity) return HeldThing.Tool;
        }
        return HeldThing.None;
    }

    public static ArmState Arm(CrewMember c, BodyPart side)
    {
        var st = ArmState.Ok;
        foreach (var x in c.Vitals.Wounds)
        {
            if (x.Part != side) continue;
            if (x.Lost) return x.Prosthetic ? ArmState.Prosthetic : ArmState.Lost;
            if (Wounds.Severity(c.Vitals, x) > 0.25f) st = ArmState.Hurt;
        }
        return st;
    }

    public static PuppetSpec Of(World w, CrewMember c)
    {
        var held = HeldOf(w, c);
        var left = Arm(c, BodyPart.LeftArm);
        var right = Arm(c, BodyPart.RightArm);
        bool two = held is HeldThing.Crate or HeldThing.Suit or HeldThing.Person || held == HeldThing.Box && c.Carrying is { Count: > 4 };
        // 한 팔을 못 쓰면 양손 짐도 한 팔로 끌어안는다
        if (two && (left is ArmState.Lost or ArmState.Hurt || right is ArmState.Lost or ArmState.Hurt)) two = false;
        var l = w.Body2.Peek(c);
        return new PuppetSpec(PoseOf(w, c), held, two, left, right, c.Suit != null, l?.Misfit ?? 0f, Wounds.LegFactor(c.Vitals) < 0.9f);
    }
}
