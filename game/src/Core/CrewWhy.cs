using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>v16.2 이유 사슬의 고리 종류: 동기 · 장소 · 걸림 · 지금 단계.</summary>
public enum WhyKind { Need, Place, Obstacle, Step }

public readonly record struct WhyStep(WhyKind Kind, string Text);

/// <summary>v16.2 손 · 발을 바꾸는 원인이 어느 시스템에서 왔나 (화면이 아이콘을 고른다).</summary>
public enum WhySource { Body, Air, Light, Rest, Food, Mind, Morale, Illness, Tool, Load, Helper, Training, Memory, Soil }

/// <summary>여러 시스템에 걸친 영향 하나: 원인 → … → 결과 (Hurts면 느려진다).</summary>
public readonly record struct WhyLink(WhySource Source, string[] Steps, bool Hurts);

/// <summary>
/// v16.2 승무원 카드의 "왜" — 지금 하는 일을 동기 → 장소 → 걸림 → 지금 단계로 잇는다
/// ("배고픔 → 식당 → 자리 없음 → 기다림").
/// 읽기 전용이다: 시뮬레이션 상태를 바꾸지 않고, 난수도 쓰지 않는다 (화면이 있든 없든 결과가 같다).
/// </summary>
public static class CrewWhy
{
    /// <summary>지금 하는 일의 이유 사슬. 맨 앞이 동기, 맨 뒤가 지금 단계.</summary>
    public static List<WhyStep> Chain(CrewMember c, World w)
    {
        var list = new List<WhyStep>();
        if (c.Dead) { list.Add(new(WhyKind.Step, "사망")); return list; }
        if (c.CarriedBy is CrewMember by)
        {
            list.Add(new(WhyKind.Need, "쓰러짐"));
            list.Add(new(WhyKind.Step, $"{by.Name}에게 업혀 감"));
            return list;
        }
        if (c.Down)
        {
            list.Add(new(WhyKind.Need, c.Vitals.InjuryCause is string ic && c.Vitals.Injury > 0.05f ? $"쓰러짐 · {ic}" : "쓰러짐"));
            if (c.CareBed != null) list.Add(new(WhyKind.Place, c.CareBed.Room.Name));
            list.Add(new(WhyKind.Step, c.CareBed != null ? "치료 침대에 누움" : "구조를 기다림"));
            return list;
        }
        var job = c.Job;
        if (job == null)
        {
            list.Add(new(WhyKind.Need, "할 일을 고르는 중"));
            if (c.HoldUntil > w.Tick && c.HoldWhy != null) list.Add(new(WhyKind.Obstacle, c.HoldWhy));
            list.Add(new(WhyKind.Step, "대기"));
            return list;
        }

        list.Add(new(WhyKind.Need, Motive(c, job)));
        if (Place(job) is Room place) list.Add(new(WhyKind.Place, place.Name));
        foreach (var o in Obstacles(c, w, job)) list.Add(new(WhyKind.Obstacle, o));
        list.Add(new(WhyKind.Step, StepText(c, job)));
        return list;
    }

    /// <summary>사슬을 한 줄로 ("배고픔 → 식당 → 자리 없음 → 기다림").</summary>
    public static string Line(CrewMember c, World w) => string.Join(" → ", Chain(c, w).Select(s => s.Text));

    /// <summary>그 일을 고른 까닭: 고를 때 매긴 점수의 이유 첫 마디 (없으면 맡은 작업 · 일 이름).</summary>
    public static string Motive(CrewMember c, Job job)
    {
        string? reason = null;
        foreach (var e in c.LastEvaluations)
            if (e.Activity == job.Activity) { reason = e.Reason; break; }
        reason ??= c.JobReason;
        if (string.IsNullOrWhiteSpace(reason))
            return job.Order is WorkOrder o ? $"작업 · {o.Title}" : job.Urgent ? $"긴급 · {job.Label}" : job.Label;
        string first = reason.Split(" · ")[0].Trim();
        return first.Length > 0 ? first : job.Label;
    }

    private static Room? Place(Job job) => job.TargetRoom ?? job.Target?.Room ?? job.Order?.Target.CurrentRoom;

    /// <summary>일을 막거나 늦추는 것: 자리 · 침대가 없음 · 길이 막힘 · 보류 · 붙잡힘 · 비켜섬 · 설비가 멈춤.</summary>
    public static List<string> Obstacles(CrewMember c, World w, Job job)
    {
        var list = new List<string>();
        if (job.Activity is EatActivity && job.Label == "식사")
        {
            bool seated = job.Reservations.Any(f => f.Type == FurnitureType.Seat);
            if (!seated) list.Add(w.Ship.RoomsOf(RoomType.Mess).Any(r => !r.OffLimits) ? "자리 없음" : "식당 없음");
        }
        if (job.Activity is EatActivity && w.Food.Rationing) list.Add("배급 중");
        if (job.Activity is SleepActivity && job.Label == "쪽잠" && c.Bed == null) list.Add("침대 없음");
        if (c.PathBlocked) list.Add("길이 막힘");
        if (job.Order is { } order && order.BlockedUntil > w.Tick && order.BlockedReason != null) list.Add($"보류 · {order.BlockedReason}");
        if (c.HoldUntil > w.Tick && c.HoldWhy != null) list.Add(c.HoldWhy);
        if (job.Target?.Machine is { } m && m.Efficiency <= 0f && job.Order == null) list.Add($"{job.Target.Name} 멈춤");
        if (Place(job) is Room r)
        {
            if (r.OffLimits || r.Lockdown) list.Add($"{r.Name} 출입 제한");
            if (r.Leaking) list.Add($"{r.Name} 공기가 샌다");
            else if (r.Unbreathable) list.Add($"{r.Name} 숨쉬기 어렵다");
            if (r.Dark && c.Suit == null) list.Add($"{r.Name} 캄캄함");
        }
        if (c.Gait.Line(c, w) is string gait && gait.StartsWith("비켜섰다")) list.Add(gait);
        return list;
    }

    /// <summary>지금 단계: 가는 중 · 기다림 · 하는 중 · 진척.</summary>
    public static string StepText(CrewMember c, Job job)
    {
        switch (job.Current)
        {
            case null: return "준비";
            case GotoToil or GotoToilLate: return c.IsMoving ? "가는 중" : "도착";
            case WaitToil wt:
                if (c.Pose == Pose.Sleeping) return "잠";
                if (wt.EveryTick == null) return "기다림";
                return c.Pose == Pose.Standing && job.Activity is EatActivity ? "서서 먹는 중" : $"{job.Label} 중";
            case TakeToil: return "꺼내는 중";
            case PutToil: return "놓는 중";
            default:
                return job.Current.Progress is float p ? $"{job.Label} {p * 100f:0}%" : $"{job.Label} 중";
        }
    }

    // ─────────────────────────── 여러 시스템에 걸친 영향 ───────────────────────────

    /// <summary>
    /// 지금 이 사람의 손 · 발을 바꾸고 있는 것들 — 원인이 여러 시스템에 걸치면 사슬로 잇는다
    /// ("미끄러져 허리를 삐었다 → 부상 40% → 작업 10% 느림"). 배율은 작업(WorkToil) · 걸음(Locomotion.Speed)이 실제로 쓰는 식 그대로다.
    /// </summary>
    public static List<WhyLink> Influences(CrewMember c, World w)
    {
        var list = new List<WhyLink>();
        if (c.Dead) return list;
        var v = c.Vitals;
        string Pct(float mul) => $"{MathF.Abs(1f - mul) * 100f:0}%";
        void Add(WhySource src, float work, float walk, params string[] steps)
        {
            var effect = new List<string>();
            if (MathF.Abs(work - 1f) >= 0.005f) effect.Add(work < 1f ? $"작업 {Pct(work)} 느림" : $"작업 {Pct(work)} 빠름");
            if (MathF.Abs(walk - 1f) >= 0.005f) effect.Add(walk < 1f ? $"걸음 {Pct(walk)} 느림" : $"걸음 {Pct(walk)} 빠름");
            if (effect.Count == 0) return;
            list.Add(new WhyLink(src, steps.Append(string.Join(" · ", effect)).ToArray(), work * walk < 1f));
        }

        // 몸: 부상 · 팔다리 상처 · 후유증 (사고 → 부상 → 손발)
        if (v.Injury > 0.01f || Wounds.HandFactor(v) < 0.999f || Wounds.LegFactor(v) < 0.999f)
        {
            float work = (1f - 0.25f * v.Injury) * Wounds.HandFactor(v);
            float walk = (1f - 0.2f * v.Injury) * Wounds.LegFactor(v);
            if (v.InjuryCause is string cause && v.Injury > 0.01f) Add(WhySource.Body, work, walk, cause, $"부상 {v.Injury * 100f:0}%");
            else Add(WhySource.Body, work, walk, "팔다리 상처");
        }
        if (v.Scar > 0.01f) Add(WhySource.Body, 1f - 0.3f * v.Scar, 1f, v.ScarCause ?? "지난 부상", $"후유증 {v.Scar * 100f:0}%");
        // 공기: 방 산소 → 혈중 산소 → 손발
        if (v.Oxygen < 0.85f)
            Add(WhySource.Air, 0.8f, 0.8f, c.Room is Room r ? $"{r.Name} 산소 {r.Air.O2:0.0} kPa" : "숨 쉴 공기", $"혈중 산소 {v.Oxygen * 100f:0}%");
        // 전기 · 조명: 정전 → 캄캄함 → 손
        if (c.Room is Room here && here.Dark && c.Suit == null)
            Add(WhySource.Light, 0.8f, 1f, here.Powered ? $"{here.Name} 조명이 꺼짐" : $"{here.Name} 정전", "캄캄함");
        // 잠 · 먹을 것 · 마음
        if (c.Needs.Rest < 0.2f) Add(WhySource.Rest, 0.75f, c.Needs.Rest < 0.15f ? 0.7f : 1f, "잠이 모자람", $"기력 {c.Needs.Rest * 100f:0}%");
        if (c.Needs.Food < 0.1f) Add(WhySource.Food, 1f, 0.8f, "굶주림");
        if (w.Food.Rationing && c.Needs.Food < 0.5f) Add(WhySource.Food, 0.94f, 1f, "식량 배급", "배고픔");
        if (c.Needs.Stress > 0.7f)
            Add(WhySource.Mind, 0.8f, 1f, c.Memory.TraumaCause is string tc && c.Memory.Trauma >= 0.03f ? $"{tc}의 기억" : "쌓인 일", $"스트레스 {c.Needs.Stress * 100f:0}%");
        if (MathF.Abs(w.Society.WorkFactor - 1f) >= 0.01f) Add(WhySource.Morale, w.Society.WorkFactor, 1f, "배의 사기");
        // 앓는 것 (질병 · 만성 · 마음)
        if (c.Ailments.Count > 0 && (MathF.Abs(c.Fx.WorkMul - 1f) >= 0.005f || MathF.Abs(c.Fx.WalkMul - 1f) >= 0.005f))
            Add(WhySource.Illness, c.Fx.WorkMul, c.Fx.WalkMul, string.Join(" · ", c.Ailments.Select(a => AilmentSystem.Spec(a.Id).Name).Distinct().Take(2)));
        // 물건 · 도구 · 짐 · 동료
        if (MathF.Abs(c.ToolFactor - 1f) >= 0.01f) Add(WhySource.Tool, c.ToolFactor, 1f, c.ToolFactor > 1f ? "손에 익은 제 공구" : "남의 · 낯선 공구");
        if (c.CarryingPerson is CrewMember carried) Add(WhySource.Load, 1f, 0.6f, $"{Ko.EulReul(carried.Name)} 업음");
        else if (c.Carrying is ItemStack held) Add(WhySource.Load, 1f, 0.9f, $"{held} 들고 감");
        if (c.Helper is Robot helper && helper.Helping == c) Add(WhySource.Helper, 1f + RobotsV15.AssistBonus(helper.Kind), 1f, "로봇이 거듦");
        if (c.Job?.Urgent == true)
        {
            if (c.Drilled(w)) Add(WhySource.Training, 1.12f, 1f, "비상 훈련");
            if (w.Society.IsVeteran(c)) Add(WhySource.Training, 1.1f, 1f, "베테랑");
        }
        // 기억 · 겁: 무서운 방은 돌아서 간다 (길이 길어진다)
        if (c.Memory.AnyFear && c.IsMoving)
        {
            int worst = -1;
            for (int i = 0; i < c.Memory.Fear.Length && i < w.Ship.Rooms.Count; i++)
                if (c.Memory.Fear[i] >= 0.35f && (worst < 0 || c.Memory.Fear[i] > c.Memory.Fear[worst])) worst = i;
            if (worst >= 0)
                list.Add(new WhyLink(WhySource.Memory, new[] { c.Memory.FearCause[worst] ?? "겪은 일", $"{w.Ship.Rooms[worst].Name}이 무섭다", "돌아서 감" }, true));
        }
        // 묻은 것 · 따르는 관행 (행동을 바꾸는 것만 화면에 이미 한 줄로 있다 — 여기서는 원인만)
        if (c.Soil.Line() is string soil) list.Add(new WhyLink(WhySource.Soil, new[] { soil, "씻을 때를 찾는다" }, false));
        return list;
    }

    /// <summary>최근 기억 n개 (지나온 일 · 일기를 시간순으로 합쳐 최근 것부터).</summary>
    public static List<(long tick, string text)> RecentMemories(CrewMember c, int n = 3)
    {
        var all = new List<(long tick, string text)>();
        foreach (var m in c.Memory.Marks) all.Add((m.Tick, m.Text));
        foreach (var d in c.Diary) all.Add(d);
        return all.OrderByDescending(x => x.tick).ThenBy(x => x.text, StringComparer.Ordinal).Take(n).ToList();
    }

    /// <summary>관계 n개: 가장 강한 사이(좋든 나쁘든)부터, 그 사람이 기억하는 까닭과 함께.</summary>
    public static List<(CrewMember who, float value, string word, string? why)> TopRelations(CrewMember c, World w, int n = 3)
    {
        return c.Relations(w).Where(r => !r.who.Dead)
            .OrderByDescending(r => MathF.Abs(r.value)).ThenBy(r => r.who.Id).Take(n)
            .Select(r => (r.who, r.value, RelationWord(r.value), w.Relations.Why(c, r.who)?.Text))
            .ToList();
    }

    public static string RelationWord(float v) => v > 0.5f ? "각별함" : v > 0.25f ? "친함" : v > -0.05f ? "보통" : v > -0.3f ? "서먹함" : "불편함";

    /// <summary>지닌 물건: 손에 든 것 · 가방(작업 재료) · 개인 물건.</summary>
    public static List<string> Things(CrewMember c, World w, int n = 6)
    {
        var list = new List<string>();
        if (c.Carrying is ItemStack held) list.Add($"손에 {held}");
        foreach (var (_, kind, count) in c.Kit) list.Add($"가방 {ItemKinds.Name(kind)} {count}");
        if (c.Suit != null) list.Add($"우주복 · 산소 {c.Suit.Oxygen:0.0}시간");
        foreach (var b in w.Belongings.Of(c).OrderBy(b => b.Id))
            list.Add($"{b.Name} · {w.Belongings.Where(b)}");
        return list.Take(n).ToList();
    }
}
