using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v16.11 선외 위험의 행동: 표류(추진팩 · 기다림) · 응급 패치(혼자 · 동료) · 그늘에 숨기 · 에어락으로 서두르기 ·
//   구조 EVA(구조줄 · 추진팩 · 붙잡아 끌고 오기 · 시신 거두기) · 에어락 마중 · 우주복 수리.

/// <summary>선체 밖에서 살아남기: 떠내려가면 추진팩 · 새면 패치 · 경보면 그늘이나 에어락.</summary>
public sealed class EvaSurviveActivity : Activity
{
    public override string Id => "evasurvive";
    public override string Label => "선외 생존";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!c.Outside || c.Dead || c.Down || c.Aboard != null) return (0f, "—");
        var er = w.EvaRisk;
        if (er.Of(c) is not EvaPerson p) return (0f, "—");
        var wv = c.Suit?.Wear;
        if (p.Adrift) return (6f, p.TowedBy >= 0 || p.TowDrone >= 0 ? "끌려가는 중 — 줄을 꼭 붙잡는다" : "선체에서 떨어져 떠내려간다 — 추진팩 · 무전");
        if (p.OffHullSince >= 0) return (4f, "선체에서 떨어져 있다 — 추진팩으로 돌아간다");
        if (c.CarryingPerson != null) return (0f, "—");
        if (wv != null && er.NeedsPatch(c)) return (5f, $"우주복 {(wv.Visor >= 1f && !wv.VisorShield ? "바이저 깨짐" : $"{EvaRiskSystem.PartName(wv.BreachPart)} {EvaRiskSystem.BreachName(wv.Breach)}")} — 응급 패치");
        if (wv != null && wv.Leaking) return (3.2f, "패치가 없다 — 당장 에어락으로");
        if (wv != null && wv.PatchKit > 0 && er.BuddyInNeed(c) is CrewMember b) return (2.4f, $"{b.Name} 우주복이 샌다 — 내 패치로 막아 준다");
        if (p.PlanFor >= 0 && p.Plan == EvaPlan.Shelter && p.ShelterAt != null) return (1.6f, "운석 경보 — 선체 그늘로");
        if (p.PlanFor >= 0 && p.Plan == EvaPlan.ToAirlock) return (1.55f, "운석 경보 — 에어락으로 서두른다");
        if (!p.Lines && p.Tethers > 0 && c.Job?.Activity is not EvaRescueActivity) return (2f, "생명줄 없이 매달려 있다 — 에어락으로");
        return (0f, "—");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var er = w.EvaRisk;
        if (er.Of(c) is not EvaPerson p) return null;
        var wv = c.Suit?.Wear;
        if (p.Adrift)
            return new Job(this, "표류", new Toil[] { new DriftToil() }) { Urgent = true, InterruptMargin = 0.5f, LogText = "선체에서 떨어져 떠내려간다", LogKind = LogKind.Warning };
        if (p.OffHullSince >= 0)
        {
            var spot = er.NearestHandhold(c.Position);
            if (spot is not Cell s) return null;
            return new Job(this, "선체로 돌아가기", new Toil[] { new JetToil((cm, world) => s.Center, 0.05f, 0.45f) }) { Urgent = true, InterruptMargin = 0.5f };
        }
        if (wv != null && er.NeedsPatch(c))
        {
            float mins = er.PatchMinutes(c, c, false);
            var toils = new List<Toil>
            {
                new WaitToil(SimTime.Minutes(mins), Pose.Working, null) { EveryTick = (cm, world) => { if (world.EvaRisk.Of(cm) is EvaPerson q && q.PatchStart < 0) { q.PatchStart = world.Tick; q.PatchBy = cm.Id; } } },
                new DoToil((cm, world) => { world.EvaRisk.TryPatch(cm, cm, false); return true; }),
            };
            return new Job(this, "응급 패치", toils) { Urgent = true, InterruptMargin = 0.6f, LogText = $"우주복을 응급 패치한다 ({wv.Describe()})", LogKind = LogKind.Warning, OnFinished = (cm, world, st) => { if (world.EvaRisk.Of(cm) is EvaPerson q) q.PatchStart = -1; } };
        }
        if (wv != null && wv.PatchKit > 0 && er.BuddyInNeed(c) is CrewMember b)
        {
            float mins = er.PatchMinutes(b, c, false);
            int bid = b.Id;
            var toils = new List<Toil>
            {
                new GotoToil(b.Cell),
                new WaitToil(SimTime.Minutes(mins), Pose.Working, b.Position) { EveryTick = (cm, world) => { if (world.EvaRisk.Of(bid) is EvaPerson q && q.PatchStart < 0) { q.PatchStart = world.Tick; q.PatchBy = cm.Id; } } },
                new DoToil((cm, world) => { if (world.Crew.FirstOrDefault(x => x.Id == bid) is CrewMember bb && !bb.Dead && (bb.Position - cm.Position).Length() < 2.5f) world.EvaRisk.TryPatch(bb, cm, false); return true; }),
            };
            return new Job(this, "동료 패치", toils) { Urgent = true, InterruptMargin = 0.5f, LogText = $"{Ko.IGa(b.Name)} 우주복이 샌다 — 내 패치를 들고 간다", LogKind = LogKind.Warning, OnFinished = (cm, world, st) => { if (world.EvaRisk.Of(bid) is EvaPerson q && q.PatchBy == cm.Id) q.PatchStart = -1; } };
        }
        if (p.PlanFor >= 0 && p.Plan == EvaPlan.Shelter && p.ShelterAt is Cell sh)
        {
            var toils = new List<Toil>
            {
                new GotoToil(sh),
                new WaitToil(SimTime.Minutes(15), Pose.Working, null) { DoneWhen = (cm, world) => world.EvaRisk.Of(cm) is not EvaPerson q || q.PlanFor < 0 },
            };
            return new Job(this, "선체 그늘", toils) { Urgent = true, InterruptMargin = 0.5f, LogText = "운석 경보 — 선체 그늘에 몸을 붙인다", LogKind = LogKind.Warning };
        }
        // 에어락으로 서두른다 (경보 · 새는 우주복 · 끊긴 생명줄)
        var go = new List<Toil>();
        p.HurryUntil = Math.Max(p.HurryUntil, w.Tick + SimTime.Minutes(10));
        WorkPlanners.EvaInFor(w, go);
        if (go.Count == 0) return null;
        string why = wv != null && wv.Leaking ? "우주복이 샌다 — 에어락으로 서두른다" : p.PlanFor >= 0 ? "운석 경보 — 에어락으로 서두른다" : "생명줄이 끊겼다 — 에어락으로";
        return new Job(this, "에어락으로", go) { Urgent = true, InterruptMargin = 0.4f, LogText = why, LogKind = LogKind.Warning };
    }
}

/// <summary>표류: 회전을 멈추고 → 멀어지는 것을 멈추고 → 돌아간다 (연료가 모자라면 무전으로 부르며 기다린다).</summary>
public sealed class DriftToil : Toil
{
    public override void Begin(CrewMember c, World w)
    {
        c.Path = null;
        c.Destination = null;
        c.Pose = Pose.Standing;
    }

    public override ToilStatus Tick(CrewMember c, World w) => w.EvaRisk.DriftTick(c) ? ToilStatus.Running : ToilStatus.Succeeded;
}

/// <summary>추진팩으로 곧장 난다 (선체 밖 · 격자 밖도). 목표가 없으면 건너뛴다. 연료가 떨어지면 구조줄을 당겨 돌아가거나 떠내려간다.</summary>
public sealed class JetToil : Toil
{
    private readonly Func<CrewMember, World, Vector2?> _goal;
    private readonly float _speed;
    private readonly float _reach;

    public JetToil(Func<CrewMember, World, Vector2?> goal, float speed, float reach)
    {
        _goal = goal;
        _speed = speed;
        _reach = reach;
    }

    public override void Begin(CrewMember c, World w)
    {
        c.Path = null;
        c.Destination = null;
        c.Pose = Pose.Standing;
    }

    public override ToilStatus Tick(CrewMember c, World w) => w.EvaRisk.JetTick(c, _goal(c, w), _speed, _reach);
}

/// <summary>구조 EVA · 시신 거두기 · 에어락 마중.</summary>
public sealed class EvaRescueActivity : Activity
{
    public override string Id => "evarescue";
    public override string Label => "구조 EVA";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var er = w.EvaRisk;
        if (er.People.Count == 0 || !c.CanAct || c.IsChild || c.Aboard != null) return (0f, "—");
        if (er.Of(c) is EvaPerson me && (me.Adrift || me.OffHullSince >= 0)) return (0f, "—");
        if (c.CarryingPerson != null) return (0f, "—");
        var v = er.RescueTarget(c, out var tp);
        if (v != null && tp != null)
        {
            if (c.Vitals.Health < 0.45f || c.Vitals.Injury > 0.5f) return (0f, "몸이 성치 않아 나가지 못한다");
            if (c.Suit == null && w.Ship.FurnitureOf(FurnitureType.SuitLocker).Sum(f => f.Storage!.Count(ItemKind.Suit)) == 0) return (0f, "남은 우주복이 없다");
            if (EvaRiskSystem.HatchOuter(w) == null) return (0f, "에어락이 없다");
            float aff = MathF.Max(0f, c.AffinityTo(v));
            if (er.RefusesFor(c, true)) return (0f, "선외 공포 — 구하러 나가지 못한다");
            float range = er.FromHull(v.Position);
            if (range > EvaRiskSystem.RescueLineLength && c.Traits.Bravery < 0.55f && aff < 0.45f && tp.AssignedId != c.Id) return (0f, $"구조줄({EvaRiskSystem.RescueLineLength:0}칸)이 닿지 않는다 — 줄 없이 나갈 용기가 없다");
            if (v.Dead) return (0.5f + 0.4f * aff, $"{v.Name}의 시신을 거둬 온다");
            float s = 1.75f + 0.5f * aff + (tp.AssignedId == c.Id ? 0.8f : 0f) + (c.Outside ? 0.4f : 0f);
            return (s, $"{v.Name} 표류 — 구하러 나간다 ({range:0}칸)" + (tp.AssignedId == c.Id ? " · 지목받았다" : ""));
        }
        if (!c.Outside && er.ReceiveTarget(c) is CrewMember r) return (1.15f, $"{r.Name} — 에어락 앞에서 기다린다");
        return (0f, "—");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var er = w.EvaRisk;
        var v = er.RescueTarget(c, out var tp);
        if (v == null || tp == null)
        {
            if (c.Outside || er.ReceiveTarget(c) is not CrewMember r) return null;
            return er.PlanReceive(this, c, r, dist);
        }
        if (EvaRiskSystem.HatchOuter(w) is not Cell outer) return null;
        var toils = new List<Toil>();
        if (!c.Outside)
        {
            toils = Plans.DropOff(c, w, dist);
            er.PlanningRescue = true;
            bool ok = WorkPlanners.EvaOutFor(c, w, dist, toils, out _);
            er.PlanningRescue = false;
            if (!ok) return null;
            toils.Add(new GotoToil(outer));
        }
        int vid = v.Id;
        toils.Add(new DoToil((cm, world) => world.EvaRisk.BeginRescue(cm, vid)));
        toils.Add(new JetToil((cm, world) => world.EvaRisk.RescueGoal(cm, vid), 0.05f, 0.55f));
        toils.Add(new DoToil((cm, world) => world.EvaRisk.Grab(cm, vid)));
        toils.Add(new JetToil((cm, world) => outer.Center, 0.034f, 0.45f));
        toils.Add(new DoToil((cm, world) => world.EvaRisk.Latch(cm, vid)));
        WorkPlanners.EvaInFor(w, toils);
        toils.Add(new DoToil((cm, world) => world.EvaRisk.SetDown(cm, vid)));
        return new Job(this, v.Dead ? "시신 거두기" : "구조 EVA", toils)
        {
            Urgent = !v.Dead,
            InterruptMargin = 0.6f,
            LogText = v.Dead ? $"선체 밖으로 {v.Name}의 시신을 거두러 간다" : $"떠내려가는 {Ko.EulReul(v.Name)} 구하러 나간다 — 구조줄 · 추진팩",
            LogKind = LogKind.Warning,
            OnFinished = (cm, world, st) => world.EvaRisk.EndRescue(cm, vid, st),
        };
    }
}

/// <summary>보관함에 걸린 상한 우주복을 고친다 (테이프 · 접착제 · 실링폼 · 바이저 유리).</summary>
public sealed class SuitMendActivity : Activity
{
    public override string Id => "suitmend";
    public override string Label => "우주복 수리";

    /// <summary>상한 우주복을 입고 들어왔다: 벗어 "수리 대기"로 건다.</summary>
    private static bool WearingDamaged(CrewMember c) =>
        c.Suit is SuitState s && !c.Outside && c.Room != null && (s.Wear.Breach >= SuitBreach.Tear || s.Wear.Visor >= 0.2f || s.Wear.Scuff > 0.3f || s.Wear.Patches > 0);

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var er = w.EvaRisk;
        if (WearingDamaged(c) && c.CanAct && Atmosphere.Danger(c.Room!) <= 0.1f && !c.Room!.Leaking)
            return (1.25f, $"상한 우주복을 입고 있다 — {c.Suit!.Wear.Describe()} · 벗어 수리 대기로");
        if (er.DamagedSuits.Count == 0 || !c.CanAct || c.IsChild || c.Outside || c.Room == null) return (0f, "—");
        bool tech = c.Role is CrewRole.Technician or CrewRole.Engineer;
        if (!tech && c.SkillLevel(Skill.Mechanics) < 0.35f) return (0f, "—");
        if (Crisis.Acting(w)) return (0f, "위기 중");
        var st = er.MendTarget(c, dist, out var why);
        if (st == null) return (0f, why);
        int good = w.Ship.FurnitureOf(FurnitureType.SuitLocker).Sum(f => f.Storage!.Count(ItemKind.Suit)) - er.DamagedSuits.Count;
        float s = 0.36f + (tech ? 0.08f : 0f) + (good <= 0 ? 0.45f : 0f);
        if (!OnShift(c, w)) s *= 0.5f;
        return (s, $"상한 우주복 — {st.Wear.Describe()}" + (good <= 0 ? " · 멀쩡한 우주복이 없다" : ""));
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var er = w.EvaRisk;
        if (WearingDamaged(c))
        {
            var (box, at0) = Plans.NearestContainer(w, dist, c, f => f.Type == FurnitureType.SuitLocker && f.Storage!.Free > 0);
            if (box != null)
            {
                var hang = Plans.DropOff(c, w, dist);
                hang.Add(new GotoToil(at0));
                hang.Add(new WaitToil(SimTime.Minutes(4), Pose.Working, box.Center));
                hang.Add(new DoToil((cm, world) =>
                {
                    if (cm.Suit == null) return true;
                    if (box.Storage!.Add(ItemKind.Suit, 1) == 0) return false;
                    world.EvaRisk.OnStow(cm, box);
                    cm.Suit = null;
                    return true;
                }));
                return new Job(this, "상한 우주복 걸기", hang) { TargetRoom = box.Room, LogText = "상한 우주복을 벗어 \"수리 대기\"로 건다", LogKind = LogKind.Work };
            }
        }
        var st = er.MendTarget(c, dist, out _);
        if (st == null) return null;
        var locker = w.Ship.Furniture.FirstOrDefault(f => f.Id == st.LockerId);
        var spot = locker?.UseSpots.Where(dist.Reachable).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
        if (locker == null || spot is not Cell at) return null;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new DoToil((cm, world) => { st.MenderId = cm.Id; return true; }));
        toils.Add(new WorkToil(EvaRiskSystem.MendHours(st.Wear), Skill.Mechanics, locker.Center));
        toils.Add(new DoToil((cm, world) =>
        {
            if (!world.EvaRisk.DamagedSuits.Contains(st)) return true;
            if (!EvaRiskSystem.Consume(world, EvaRiskSystem.MendCost(st.Wear))) { cm.Say(world, "수리 재료가 모자라다"); return false; }
            world.EvaRisk.Mended(st, cm);
            return true;
        }));
        return new Job(this, "우주복 수리", toils)
        {
            TargetRoom = locker.Room,
            LogText = $"상한 우주복을 고친다 — {st.Wear.Describe()}",
            LogKind = LogKind.Work,
            AlwaysLog = true,
            OnFinished = (cm, world, s) => { if (st.MenderId == cm.Id) st.MenderId = -1; },
        };
    }
}

public static partial class WorkPlanners
{
    /// <summary>v16.11 구조 EVA가 쓰는 에어락 나가기 (선외 공포 판단 포함).</summary>
    internal static bool EvaOutFor(CrewMember c, World w, DistanceField dist, List<Toil> toils, out string? blocked) => EvaOut(c, w, dist, toils, out blocked);

    internal static void EvaInFor(World w, List<Toil> toils) => EvaIn(w, toils);
}

public sealed partial class EvaRiskSystem
{
    // ─────────────────────────────── 패치 ───────────────────────────────

    public bool NeedsPatch(CrewMember c) =>
        c.Suit is SuitState s && s.Wear.PatchKit > 0 && (s.Wear.Breach >= SuitBreach.MicroLeak && !s.Wear.Patched || s.Wear.Visor >= 1f && !s.Wear.VisorShield);

    /// <summary>패치가 없거나 쓰러져서 스스로 못 막는 옆 사람 (내 패치로 막아 준다).</summary>
    public CrewMember? BuddyInNeed(CrewMember c)
    {
        foreach (var p in People)
        {
            if (p.Id == c.Id || p.Adrift || p.PatchStart >= 0 && p.PatchBy != c.Id) continue;
            var b = Crew(p.Id);
            if (b == null || b.Dead || b.Suit is not SuitState s || !s.Wear.Leaking) continue;
            if ((b.Position - c.Position).LengthSquared() > 9f) continue;
            if (b.CanAct && s.Wear.PatchKit > 0) continue; // 스스로 막을 수 있다
            return b;
        }
        return null;
    }

    public float PatchMinutes(CrewMember victim, CrewMember by, bool spinning)
    {
        var wv = victim.Suit!.Wear;
        float m = wv.Visor >= 1f && !wv.VisorShield ? 2f : wv.Breach == SuitBreach.Puncture ? 3f : 1.5f;
        if (spinning) m *= 1.6f;
        m /= Wounds.HandFactor(by.Vitals);
        if (by != victim) m *= 0.7f;
        return m;
    }

    /// <summary>응급 패치 한 장: 솜씨 · 침착함 · 공포 · 회전 · 다친 팔 · 동료 손이 가른다.</summary>
    public bool TryPatch(CrewMember victim, CrewMember by, bool spinning)
    {
        var w = _w;
        if (victim.Suit is not SuitState s) return false;
        var wv = s.Wear;
        var p = Of(victim);
        if (p != null) p.PatchStart = -1;
        bool visor = wv.Visor >= 1f && !wv.VisorShield;
        if (!visor && (wv.Breach < SuitBreach.MicroLeak || wv.Patched)) return true;
        // 패치는 입은 사람 것을 먼저, 없으면 도와주는 사람 것을
        var kit = wv.PatchKit > 0 ? wv : by.Suit?.Wear;
        if (kit == null || kit.PatchKit <= 0) return false;
        kit.PatchKit--;
        float panic = Of(by)?.Panic ?? 0f;
        float chance = 0.5f + 0.35f * by.SkillLevel(Skill.Mechanics) + 0.15f * by.Traits.Calm - 0.3f * panic
                       - (wv.Breach == SuitBreach.Puncture ? 0.22f : 0f) - (visor ? 0.1f : 0f) - (spinning ? 0.25f : 0f)
                       - (1f - Wounds.HandFactor(by.Vitals)) + (by != victim ? 0.15f : 0f);
        bool ok = R.Chance(Math.Clamp(chance, 0.08f, 0.95f));
        string what = visor ? "깨진 바이저 (해가리개를 내리고 테이프)" : $"{PartName(wv.BreachPart)} {BreachName(wv.Breach)}";
        if (ok)
        {
            if (visor) wv.VisorShield = true; else wv.Patched = true;
            wv.Patches++;
            Stats.Patches++;
            if (by != victim) { Stats.BuddyPatches++; victim.ChangeAffinity(by, 0.15f); w.Relations.Remember(victim, by, RelationReason.SavedMe, "선체 밖에서 새는 내 우주복을 막아 줬다"); }
            w.Log.Add(w.Tick, LogKind.Work, by == victim ? $"응급 패치 — {Ko.EulReul(what)} 막았다 (남은 패치 {kit.PatchKit})" : $"{Ko.EulReul(victim.Name)} 응급 패치 — {Ko.EulReul(what)} 막아 줬다", by.Id);
            Say(by, by == victim ? "막았다… 숨 쉴 만하다" : $"{victim.Name}, 막았어 — 천천히 숨 쉬어", RadioTone.Chat, victim.Id);
            Life.Diary(w, victim, Persona.Say(victim, by == victim ? $"쉭 하고 새는 소리를 들으며 {PartName(wv.BreachPart)}에 테이프를 감았다" : $"{Ko.IGa(by.Name)} 내 우주복에 패치를 붙여 줬다"));
            by.Practice(Skill.Mechanics, 0.02f);
        }
        else
        {
            Stats.PatchFails++;
            w.Log.Add(w.Tick, LogKind.Warning, $"패치가 붙지 않았다 — {what} (남은 패치 {kit.PatchKit})", by.Id);
            Say(by, kit.PatchKit > 0 ? "안 붙어 — 한 장 더!" : "패치가 다 떨어졌어 — 들어간다!", RadioTone.Hurt, victim.Id);
            if (p != null) p.Panic = MathF.Min(1f, p.Panic + 0.2f);
        }
        return ok;
    }

    // ─────────────────────────────── 표류 ───────────────────────────────

    /// <summary>가까운 선체 손잡이 칸 (EVA로 닿는 우주 칸).</summary>
    public Cell? NearestHandhold(Vector2 at)
    {
        var w = _w;
        var grid = w.Ship.Grid;
        var c0 = Cell.FromPosition(new Vector2(Math.Clamp(at.X, 0.5f, grid.Width - 0.5f), Math.Clamp(at.Y, 0.5f, grid.Height - 0.5f)));
        for (int r = 0; r < Math.Max(grid.Width, grid.Height); r++)
        {
            Cell? best = null;
            float bd = float.MaxValue;
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                var c = new Cell(c0.X + dx, c0.Y + dy);
                if (!w.Paths.IsSpace(c)) continue;
                float d = (c.Center - at).LengthSquared();
                if (d < bd) { bd = d; best = c; }
            }
            if (best != null) return best;
        }
        return null;
    }

    private float WasteOf(CrewMember c, EvaPerson p) => (1.0f + 0.8f * p.Panic) * (1.15f - 0.3f * c.SkillLevel(Skill.Mechanics));

    /// <summary>돌아가는 데 드는 연료 (회전 멈추기 + 멀어지는 것 멈추기 + 가고 서기).</summary>
    public float FuelToReturn(CrewMember c, EvaPerson p, Vector2 target)
    {
        float spin = MathF.Abs(p.SpinRate) * 0.0002f;
        var to = target - c.Position;
        float dist = to.Length();
        float cruise = MathF.Min(55f, 15f + dist * 2f);
        var dir = dist > 0.01f ? to / dist : Vector2.Zero;
        float away = MathF.Max(0f, -Vector2.Dot(p.Vel, dir));
        return (spin + (away + 2f * cruise) * FuelPerDv) * WasteOf(c, p);
    }

    /// <summary>표류 한 틱 (정신이 있을 때): 패치 → 회전 멈추기 → 돌아가기 / 멀어지는 것만 멈추기 / 기다리기.</summary>
    internal bool DriftTick(CrewMember c)
    {
        var w = _w;
        if (Of(c) is not EvaPerson p || !p.Adrift) return false;
        c.Pose = Pose.Standing;
        if (p.TowedBy >= 0 || p.TowDrone >= 0) { p.Mode = DriftMode.Wait; return true; }
        var wv = c.Suit?.Wear;
        // 1) 새면 먼저 막는다 (돌면서 붙이기는 어렵다)
        if (wv != null && NeedsPatch(c))
        {
            bool spinning = MathF.Abs(p.SpinRate) > 25f;
            if (p.PatchStart < 0) { p.PatchStart = w.Tick; p.PatchBy = c.Id; }
            if (w.Tick - p.PatchStart >= SimTime.Minutes(PatchMinutes(c, c, spinning))) TryPatch(c, c, spinning);
            return true;
        }
        float fuel = wv?.Fuel ?? 0f;
        var hold = NearestHandhold(c.Position);
        var target = hold?.Center ?? Anchor;
        float need = FuelToReturn(c, p, target);
        bool comingForMe = p.RescuerId >= 0 || p.TowDrone >= 0 || w.Drones.Drones.Any(d => d.Hurt.FetchPerson == p.Id && d.State != DroneState.Docked);
        const float hr = 1f / SimTime.TicksPerHour;
        const float accel = 1300f; // 칸/시간²
        float dvTick = accel * hr;
        var to = target - c.Position;
        float dist = to.Length();
        var dir = dist > 0.01f ? to / dist : Vector2.Zero;
        float waste = WasteOf(c, p);
        if (fuel > 0.005f && MathF.Abs(p.SpinRate) > 25f)
        {
            p.Mode = DriftMode.Stabilize;
            float d = MathF.Min(MathF.Abs(p.SpinRate), 900f * hr * 60f);
            p.SpinRate -= MathF.Sign(p.SpinRate) * d;
            wv!.Fuel = MathF.Max(0f, wv.Fuel - d * 0.0002f * waste);
            p.Thrust = 0.6f;
            p.ThrustDir = new Vector2(MathF.Cos(p.Spin), MathF.Sin(p.Spin));
            return true;
        }
        if (fuel > 0.005f && (fuel >= need && !comingForMe || p.Mode == DriftMode.Return && fuel > 0.005f))
        {
            if (p.Mode != DriftMode.Return) w.Log.Add(w.Tick, LogKind.Warning, $"추진팩으로 돌아간다 (연료 {fuel * 100:0}% · 필요 {need * 100:0}%)", c.Id);
            p.Mode = DriftMode.Return;
            if (dist < 0.55f)
            {
                p.Adrift = false;
                p.Vel = Vector2.Zero;
                p.SpinRate = 0f;
                c.Position = target;
                OnSelfReturn(c, p);
                return false;
            }
            float cruise = MathF.Min(55f, 6f + dist * 30f);
            var want = dir * cruise;
            var dv = want - p.Vel;
            float dl = dv.Length();
            if (dl > 0.001f)
            {
                var step = dl > dvTick ? dv / dl * dvTick : dv;
                p.Vel += step;
                wv!.Fuel = MathF.Max(0f, wv.Fuel - step.Length() * FuelPerDv * waste);
                p.Thrust = 1f;
                p.ThrustDir = step / MathF.Max(0.001f, step.Length());
            }
            // 몸을 가는 쪽으로 돌린다
            p.Spin += (MathF.Atan2(dir.Y, dir.X) - p.Spin) * 0.02f;
            c.Facing = dir;
            return true;
        }
        // 멀어지는 것만이라도 멈춘다 (구조가 쉬워진다)
        float awaySpeed = -Vector2.Dot(p.Vel, dir);
        if (fuel > 0.005f && awaySpeed > 1.5f && c.SkillLevel(Skill.Mechanics) + c.Traits.Calm > 0.6f)
        {
            if (p.Mode != DriftMode.Brake) w.Log.Add(w.Tick, LogKind.Warning, $"돌아갈 연료는 없다 — 멀어지는 것만 멈춘다 (연료 {fuel * 100:0}%)", c.Id);
            p.Mode = DriftMode.Brake;
            float dl = p.Vel.Length();
            var step = dl > dvTick ? -p.Vel / dl * dvTick : -p.Vel;
            p.Vel += step;
            wv!.Fuel = MathF.Max(0f, wv.Fuel - step.Length() * FuelPerDv * waste);
            p.Thrust = 1f;
            p.ThrustDir = step / MathF.Max(0.001f, step.Length());
            return true;
        }
        if (p.Mode != DriftMode.Wait && p.Mode != DriftMode.Tumble) Stats.Waits++;
        p.Mode = fuel <= 0.005f && MathF.Abs(p.SpinRate) > 25f ? DriftMode.Tumble : DriftMode.Wait;
        // 겁에 질려 추진팩을 헛되이 쏘기도 한다
        if (wv != null && wv.Fuel > 0.01f && p.Panic > 0.6f && R.Chance(0.004f))
        {
            wv.Fuel = MathF.Max(0f, wv.Fuel - 0.05f);
            p.SpinRate += R.Range(-80f, 80f);
            p.Thrust = 1f;
            w.Log.Add(w.Tick, LogKind.Warning, "겁에 질려 추진팩을 헛되이 쐈다 — 몸이 더 돈다", c.Id);
        }
        return true;
    }

    /// <summary>추진팩 비행 한 틱 (구조 · 선체로 돌아가기).</summary>
    internal ToilStatus JetTick(CrewMember c, Vector2? goal, float speed, float reach)
    {
        var w = _w;
        if (goal is not Vector2 g) return ToilStatus.Succeeded;
        var p = Of(c);
        var wv = c.Suit?.Wear;
        var d = g - c.Position;
        float len = d.Length();
        if (len <= reach) return ToilStatus.Succeeded;
        if (p != null && p.Adrift) return ToilStatus.Failed;
        if (wv == null || wv.Fuel <= 0f)
        {
            if (p == null) return ToilStatus.Failed;
            // 연료가 떨어졌다: 구조줄이 닿아 있으면 줄을 당겨 돌아가고, 아니면 떠내려간다
            if (FromHull(c.Position) <= RescueLineLength && NearestHandhold(c.Position) is Cell rail)
            {
                var back = rail.Center - c.Position;
                float bl = back.Length();
                if (OnHull(c) || bl < 0.6f) return ToilStatus.Failed;
                c.Position += back / bl * 0.012f;
                c.Facing = back / bl;
                return ToilStatus.Running;
            }
            w.Log.Add(w.Tick, LogKind.Warning, "추진팩 연료가 떨어졌다 — 구조줄도 닿지 않는다", c.Id);
            StartDrift(c, p, d / len * 3f, R.Range(-60f, 60f), "추진팩 연료가 떨어졌다");
            return ToilStatus.Failed;
        }
        float step = MathF.Min(speed * (c.CarryingPerson != null ? 0.75f : 1f), len);
        var dir = d / len;
        c.Position += dir * step;
        c.Facing = dir;
        wv.Fuel = MathF.Max(0f, wv.Fuel - step * FuelPerCell);
        if (p != null)
        {
            p.Thrust = 1f;
            p.ThrustDir = dir;
            p.RescueLine = p.Rescuer && FromHull(c.Position) <= RescueLineLength;
        }
        return ToilStatus.Running;
    }

    // ─────────────────────────────── 구조 EVA ───────────────────────────────

    internal bool BeginRescue(CrewMember cm, int vid)
    {
        var w = _w;
        var p = Of(vid);
        var v = Crew(vid);
        if (p == null || v == null) return true;
        if (p.RescuerId >= 0 && p.RescuerId != cm.Id && Crew(p.RescuerId) is CrewMember other && other.Job?.Activity is EvaRescueActivity) return false;
        p.RescuerId = cm.Id;
        var me = Of(cm) ?? Begin(cm);
        me.Rescuer = true;
        me.RescueLine = FromHull(v.Position) <= RescueLineLength;
        if (!v.Dead) Say(cm, me.RescueLine ? $"{v.Name}, 지금 간다 — 버텨!" : $"{v.Name}, 구조줄이 안 닿는다 — 줄 풀고 간다", RadioTone.Chat, vid);
        w.Log.Add(w.Tick, LogKind.Warning, (me.RescueLine ? "구조줄을 걸고" : "구조줄을 풀고") + $" 추진팩을 켠다 — {v.Name}까지 {(v.Position - cm.Position).Length():0}칸", cm.Id);
        return true;
    }

    internal Vector2? RescueGoal(CrewMember cm, int vid)
    {
        var p = Of(vid);
        var v = Crew(vid);
        if (p == null || v == null || v.CarriedBy != null && v.CarriedBy != cm || p.TowDrone >= 0) return null;
        if (!p.Adrift && !(v.Dead && v.Outside)) return null;
        return v.Position;
    }

    internal bool Grab(CrewMember cm, int vid)
    {
        var w = _w;
        var p = Of(vid);
        var v = Crew(vid);
        if (p == null || v == null || (v.Position - cm.Position).Length() > 1.2f || v.CarriedBy != null || p.TowDrone >= 0) return true;
        if (!p.Adrift && !(v.Dead && v.Outside)) return true;
        v.CarriedBy = cm;
        cm.CarryingPerson = v;
        p.TowedBy = cm.Id;
        p.Vel = Vector2.Zero;
        p.SpinRate *= 0.1f;
        if (!v.Dead) Say(cm, $"잡았다! {v.Name}, 내 줄에 걸었어", RadioTone.Chat, vid);
        w.Log.Add(w.Tick, LogKind.Work, v.Dead ? $"{v.Name}의 시신을 붙잡았다" : $"떠내려가던 {Ko.EulReul(v.Name)} 붙잡았다 — 끌고 돌아간다", cm.Id);
        return true;
    }

    internal bool Latch(CrewMember cm, int vid)
    {
        var p = Of(vid);
        var v = Crew(vid);
        if (p == null || v == null || cm.CarryingPerson != v) return true;
        p.Adrift = false;
        p.Vel = Vector2.Zero;
        p.SpinRate = 0f;
        OnCrewRescue(cm, v);
        // 정신이 있으면 자기 발로 (손잡이를 잡고) 들어간다
        if (!v.Dead && !v.Down)
        {
            v.CarriedBy = null;
            cm.CarryingPerson = null;
            p.TowedBy = -1;
            v.Position = cm.Position + new Vector2(0.3f, 0f);
            v.NextThinkTick = _w.Tick + 1;
        }
        return true;
    }

    internal bool SetDown(CrewMember cm, int vid)
    {
        var w = _w;
        var v = Crew(vid);
        if (v == null || cm.CarryingPerson != v) return true;
        v.CarriedBy = null;
        cm.CarryingPerson = null;
        v.Position = cm.Position;
        v.PreviousPosition = cm.Position;
        v.Room = w.Ship.RoomAt(cm.Cell);
        if (Of(v) is EvaPerson p) { p.TowedBy = -1; p.Adrift = false; }
        w.Log.Add(w.Tick, LogKind.Work, v.Dead ? $"{v.Name}의 시신을 에어락 안에 눕혔다" : $"{Ko.EulReul(v.Name)} 에어락 안에 눕혔다 — 치료를", cm.Id);
        w.Board.RequestScan();
        return true;
    }

    internal void EndRescue(CrewMember cm, int vid, ToilStatus st)
    {
        if (Of(vid) is EvaPerson vp && vp.RescuerId == cm.Id) vp.RescuerId = -1;
        if (Of(cm) is EvaPerson me) { me.Rescuer = false; me.RescueLine = false; }
        if (Of(vid) is EvaPerson q && q.AssignedId == cm.Id && !q.Adrift) q.AssignedId = -1;
    }

    /// <summary>선체 밖에서 다쳤거나 떠내려간 사람 — 이 사람이 알고, 마중 나갈 만한 사이면.</summary>
    internal CrewMember? ReceiveTarget(CrewMember c)
    {
        if (c.Dead || !c.CanAct || c.IsChild) return null;
        foreach (var p in People)
        {
            if (!p.Incident || !p.KnownBy.Contains(c.Id)) continue;
            var v = Crew(p.Id);
            if (v == null || v.Dead || v == c || p.Lost || p.Missing) continue;
            if (!(p.Adrift || p.HitAt >= 0 || v.Down || v.Suit?.Wear.Leaking == true)) continue;
            if (c.Role == CrewRole.Medic || c.AffinityTo(v) > 0.45f) return v;
        }
        return null;
    }

    internal Job? PlanReceive(Activity a, CrewMember c, CrewMember v, DistanceField dist)
    {
        var w = _w;
        if (HatchInner(w) is not Cell inner) return null;
        var room = w.Ship.RoomAt(inner);
        Cell? spot = null;
        int best = int.MaxValue;
        foreach (var r in new[] { room }.Concat(room?.Doors.Select(d => d.RoomA == room ? d.RoomB : d.RoomA) ?? Enumerable.Empty<Room?>()))
        {
            if (r == null) continue;
            foreach (var cell in r.Cells)
            {
                if (cell == inner || !w.Ship.IsOpenFloor(cell) || w.IsSpotTaken(cell, c)) continue;
                int d = dist.Get(cell);
                if (d < 0) continue;
                int key = (int)((cell.Center - inner.Center).LengthSquared() * 10f);
                if (key < best) { best = key; spot = cell; }
            }
        }
        if (spot is not Cell s) return null;
        int vid = v.Id;
        var toils = new List<Toil>
        {
            new GotoToil(s),
            new WaitToil(SimTime.Minutes(40), Pose.Standing, inner.Center)
            {
                DoneWhen = (cm, world) => world.Crew.FirstOrDefault(x => x.Id == vid) is not CrewMember x || x.Dead || !x.Outside || world.EvaRisk.Of(vid) is not EvaPerson q || q.Missing,
            },
        };
        return new Job(a, "에어락 마중", toils) { InterruptMargin = 0.3f, LogText = $"무전을 듣고 에어락으로 — {Ko.EulReul(v.Name)} 기다린다", LogKind = LogKind.Life };
    }

    // ─────────────────────────────── 우주복 수리 ───────────────────────────────

    internal StoredSuit? MendTarget(CrewMember c, DistanceField dist, out string why)
    {
        var w = _w;
        why = "—";
        foreach (var st in DamagedSuits)
        {
            if (st.MenderId >= 0 && st.MenderId != c.Id && Crew(st.MenderId) is CrewMember o && o.Job?.Activity is SuitMendActivity) continue;
            var locker = w.Ship.Furniture.FirstOrDefault(f => f.Id == st.LockerId);
            if (locker == null || locker.Room.Detached || !locker.UseSpots.Any(dist.Reachable)) { why = "보관함에 닿을 수 없다"; continue; }
            var need = MendCost(st.Wear);
            if (need.Any(n => w.Ship.CountStored(n.kind) < n.count)) { why = $"수리 재료가 없다 ({string.Join(" · ", need.Select(n => ItemKinds.Name(n.kind)))})"; continue; }
            return st;
        }
        return null;
    }

    /// <summary>배 안 보관함에서 재료를 쓴다 (모자라면 아무것도 쓰지 않는다).</summary>
    public static bool Consume(World w, (ItemKind kind, int count)[] need)
    {
        foreach (var (k, n) in need) if (w.Ship.CountStored(k) < n) return false;
        foreach (var (k, n) in need)
        {
            int left = n;
            foreach (var box in w.Ship.Containers)
            {
                left -= box.Storage!.Take(k, left);
                if (left <= 0) break;
            }
        }
        return true;
    }

    public bool RefusesFor(CrewMember c, bool rescue) => RefusesQuiet(c, rescue);

    /// <summary>표류 중인 사람 (정신이 없으면 매 틱 여기서만 움직인다) — 그림 · 시험용.</summary>
    public bool Adrift(CrewMember c) => Of(c) is EvaPerson p && p.Adrift;
}
