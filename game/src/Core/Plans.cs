using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.15 승무원 두뇌 2.0 — ③ 계획: 여러 단계 계획과 전제 조건, 막히면 다른 길, 실패를 기억한다.
// 고치기: 부품 → (믿는) 창고 → 없으면 작업대에서 만들기 → 재료가 없으면 원정 제안 → 고칠 자리에 가져다 둔다. 솜씨가 모자라면 도움을 청한다.
// 정전: 성격 · 믿음 · 감정 · 배운 것으로 길을 고른다 — 차단기 · 발전 쪽 · 컴퓨터에 묻기 · 사람 챙기기 · 제자리 · 밝은 곳 · 하던 일.
// 불 확인: 불이 났다고 믿는 방에 가 본다 (헛소문이면 믿음을 고친다). 알리기: 본 사람이 모르는 사람에게 간다 (SocialMind).
// 무거운 계획은 바뀔 때만 다시 짠다 — 단계 하나가 일(Job) 하나다. 경보 · 위기로 끊기면 그 단계부터 다시 한다.

public enum PlanKind : byte { Fix, Outage, CheckFire, Tell, Help }
public enum StepKind : byte { Fetch, Craft, Trip, Handoff, AskHelp, CheckPanel, ResetPanel, CheckPower, AskComputer, FindPerson, Wait, GoLit, CheckRoom, Tell }
public enum StepState : byte { Todo, Doing, Done, Failed, Skipped }

public sealed class PlanStep
{
    public StepKind Kind;
    public Method Method;
    public StepState State;
    public string Text = "";
    public string? Note;
    public Room? Room;
    public Furniture? At;
    public ItemKind Item;
    public int Count = 1;
    public CrewMember? Who;
    public int Tries;
    /// <summary>"안 되면" 길 — 앞 단계가 되면 건너뛴다.</summary>
    public bool Fallback;
}

public sealed class CrewPlan
{
    public int Id;
    public PlanKind Kind;
    public int Owner;
    public string Goal = "";
    public string Why = "";
    public Method Method;
    public readonly List<PlanStep> Steps = new();
    public int Index;
    public long Since;
    public bool Done, Failed;
    public string? Result;
    public Room? Room;
    public Furniture? Target;
    public ItemKind? Part;
    public WorkOrder? Order;
    public CrewMember? For;
    public Topic FactTopic;
    public int FactId;
    public long DarkSince = -1;
    /// <summary>부품을 손에 넣었다 (꺼냈거나 만들었다) — 다른 일 사이에 선반에 내려놓았어도 배에는 있다.</summary>
    public bool Made;
    public readonly List<string> Trail = new();
    public PlanStep? Step => Index >= 0 && Index < Steps.Count ? Steps[Index] : null;
}

public sealed class PlanSystem
{
    private readonly World _w;
    private readonly Dictionary<int, CrewPlan> _cur = new();
    private readonly Dictionary<(int crew, int target), long> _cool = new();
    private readonly Dictionary<int, long> _outageDone = new();
    private int _next = 1, _phase;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6007 + 811));

    public List<CrewPlan> Past { get; } = new();
    /// <summary>정전 때 고른 길 (사람 → 방법 · 까닭 · 언제 · 견준 후보).</summary>
    public Dictionary<int, (Method m, string why, long tick, List<(Method m, float s, string why)> options)> Stances { get; } = new();
    public int Started, Finished, Failures, Alternatives, Crafted, Fetched, TripsProposed, HelpAsked, Shared, Handoffs, Breakers, Found, Checked;

    public PlanSystem(World w) => _w = w;

    public CrewPlan? Current(CrewMember c) => _cur.TryGetValue(c.Id, out var p) && !p.Done ? p : null;

    public IEnumerable<CrewPlan> Active => _cur.Values.Where(p => !p.Done);

    /// <summary>계획에 걸린 물건 (둘러볼 때 보관함에서 눈여겨본다).</summary>
    public List<ItemKind> Interest(CrewMember c)
    {
        var list = _interest;
        list.Clear();
        if (Current(c) is not CrewPlan p || p.Part is not ItemKind part) return list;
        list.Add(part);
        if (Recipes.For(part, Station.Workbench) is Recipe r) foreach (var (k, _) in r.Inputs) list.Add(k);
        return list;
    }
    private readonly List<ItemKind> _interest = new();

    // ─────────────────────────── 시작 · 끝 ───────────────────────────

    public CrewPlan Begin(CrewMember c, PlanKind kind, string goal, string why, Method method)
    {
        if (Current(c) is CrewPlan old) Finish(c, old, false, "다른 계획으로 바꿈", quiet: true);
        var p = new CrewPlan { Id = _next++, Kind = kind, Owner = c.Id, Goal = goal, Why = why, Method = method, Since = _w.Tick };
        _cur[c.Id] = p;
        Started++;
        c.NextThinkTick = Math.Min(c.NextThinkTick, _w.Tick + 1);
        return p;
    }

    public PlanStep Add(CrewPlan p, StepKind k, Method m, string text, Room? room = null, Furniture? at = null, CrewMember? who = null)
    {
        var s = new PlanStep { Kind = k, Method = m, Text = text, Room = room, At = at, Who = who, Item = p.Part ?? ItemKind.Plate };
        p.Steps.Add(s);
        return s;
    }

    private PlanStep Insert(CrewPlan p, StepKind k, Method m, string text, Room? room = null, Furniture? at = null, CrewMember? who = null)
    {
        var s = new PlanStep { Kind = k, Method = m, Text = text, Room = room, At = at, Who = who, Item = p.Part ?? ItemKind.Plate };
        p.Steps.Insert(Math.Min(p.Index + 1, p.Steps.Count), s);
        return s;
    }

    public void Finish(CrewMember c, CrewPlan p, bool ok, string result, bool quiet = false)
    {
        var w = _w;
        if (p.Done) return;
        p.Done = true;
        p.Failed = !ok;
        p.Result = result;
        p.Trail.Add($"{SimTime.Clock(w.Tick)} {(ok ? "끝" : "그만")} — {result}");
        _cur.Remove(c.Id);
        Past.Add(p);
        if (Past.Count > 60) Past.RemoveAt(0);
        Finished++;
        if (!ok) Failures++;
        if (quiet) return;
        var emo = w.Brain2.Emotions;
        if (ok) { emo.Feel(c, Feeling.Pride, p.Kind is PlanKind.Fix or PlanKind.Help ? 0.25f : 0.08f, p.Goal); emo.Feel(c, Feeling.Joy, 0.06f, p.Goal); }
        else if (p.Kind is PlanKind.Fix or PlanKind.Help) emo.Feel(c, Feeling.Anger, 0.06f, $"{p.Goal} — {result}");
        w.Log.Add(w.Tick, LogKind.Life, $"계획 {(ok ? "끝" : "멈춤")} — {p.Goal}: {result}", c.Id);
    }

    // ─────────────────────────── 단계 → 일 ───────────────────────────

    /// <summary>지금 단계를 일(Job)로. 못 하는 단계는 실패로 적고 다음 길로 넘어간다 (막히면 다른 길).</summary>
    public Job? JobFor(CrewMember c, CrewPlan p, DistanceField dist, Activity act)
    {
        for (int guard = 0; guard < 10 && !p.Done; guard++)
        {
            var s = p.Step;
            if (s == null) { Complete(c, p); return null; }
            if (s.State is StepState.Done or StepState.Failed or StepState.Skipped) { Advance(c, p); continue; }
            var job = Build(c, p, s, dist, act);
            if (job != null) { s.State = StepState.Doing; return job; }
            if (s.State is StepState.Todo or StepState.Doing) Fail(c, p, s, s.Note ?? "갈 수 없다");
            Advance(c, p);
        }
        return null;
    }

    private void Fail(CrewMember c, CrewPlan p, PlanStep s, string why)
    {
        s.State = StepState.Failed;
        s.Note = why;
        p.Trail.Add($"{SimTime.Clock(_w.Tick)} ✘ {s.Text} — {why}");
        _w.Brain2.Learning.Record(c, s.Method, false);
        Alternatives++;
    }

    private void Ok(CrewMember c, CrewPlan p, PlanStep s, string? note = null)
    {
        s.State = StepState.Done;
        if (note != null) s.Note = note;
        p.Trail.Add($"{SimTime.Clock(_w.Tick)} ✔ {s.Text}{(s.Note != null ? $" — {s.Note}" : "")}");
        if (p.Kind == PlanKind.Outage) foreach (var x in p.Steps) if (x.State == StepState.Todo && x.Fallback) x.State = StepState.Skipped;
    }

    private void Advance(CrewMember c, CrewPlan p)
    {
        while (p.Index < p.Steps.Count && p.Steps[p.Index].State is not StepState.Todo) p.Index++;
        if (p.Index >= p.Steps.Count) Complete(c, p);
    }

    /// <summary>단계를 다 돌았다: 결정적인 단계가 됐으면 성공.</summary>
    private void Complete(CrewMember c, CrewPlan p)
    {
        if (p.Done) return;
        var done = p.Steps.LastOrDefault(s => s.State == StepState.Done);
        bool ok = p.Kind switch
        {
            PlanKind.Fix or PlanKind.Help => p.Steps.Any(s => s.Kind == StepKind.Handoff && s.State == StepState.Done) || p.Steps.Any(s => s.Kind is StepKind.AskHelp && s.State == StepState.Done),
            _ => done != null,
        };
        if (ok && done != null) _w.Brain2.Learning.Record(c, done.Method, true);
        string result = ok ? done?.Note ?? done?.Text ?? "끝" : p.Steps.LastOrDefault(s => s.State == StepState.Failed)?.Note ?? "길이 없다";
        if (!ok && p.Steps.Any(s => s.Kind == StepKind.Trip && s.State == StepState.Done)) result = "재료가 없어 원정을 꺼냈다 — 기다린다";
        Finish(c, p, ok, result);
    }

    /// <summary>일이 끝났을 때 (성공 · 실패 · 중단). 중단이면 그 단계부터 다시 한다.</summary>
    private Action<CrewMember, World, ToilStatus> Done(CrewPlan p, PlanStep s) => (cm, world, status) =>
    {
        if (p.Done) return;
        if (status == ToilStatus.Interrupted) { if (s.State == StepState.Doing) s.State = StepState.Todo; return; }
        if (s.State == StepState.Doing)
        {
            if (status == ToilStatus.Succeeded) Ok(cm, p, s);
            else Fail(cm, p, s, s.Note ?? "막혔다");
        }
        Advance(cm, p);
        cm.NextThinkTick = Math.Min(cm.NextThinkTick, world.Tick + 1);
    };

    private Job Wrap(CrewPlan p, PlanStep s, Activity act, string label, List<Toil> toils, string? log, Room? room = null, Furniture? target = null, bool urgent = false) =>
        new(act, label, toils)
        {
            TargetRoom = room ?? s.Room,
            Target = target,
            LogText = log,
            AlwaysLog = true,
            Urgent = urgent,
            InterruptMargin = 0.2f,
            OnFinished = Done(p, s),
        };

    private Job? Build(CrewMember c, CrewPlan p, PlanStep s, DistanceField dist, Activity act)
    {
        var w = _w;
        switch (s.Kind)
        {
            case StepKind.Fetch: return BuildFetch(c, p, s, dist, act);
            case StepKind.Craft: return BuildCraft(c, p, s, dist, act);
            case StepKind.Trip:
            {
                var toils = new List<Toil> { new DoToil((cm, world) => { ProposeTrip(cm, p, s); return true; }) };
                return Wrap(p, s, act, "원정 이야기", toils, null);
            }
            case StepKind.Handoff: return BuildHandoff(c, p, s, dist, act);
            case StepKind.AskHelp: return BuildAskHelp(c, p, s, dist, act);
            case StepKind.CheckPanel or StepKind.ResetPanel: return BuildPanel(c, p, s, dist, act);
            case StepKind.CheckPower:
            {
                var room = PowerRoom(dist);
                if (room == null || SpotIn(room, dist, c) is not Cell spot) { s.Note = "발전 쪽에 갈 수 없다"; return null; }
                s.Room = room;
                var toils = new List<Toil> { new GotoToil(spot), new DoToil((cm, world) => { CheckPower(cm, p, s); return true; }) };
                return Wrap(p, s, act, "발전 쪽 확인", toils, $"정전 — {Ko.EuRo(room.Name)} 원인을 보러 간다");
            }
            case StepKind.AskComputer:
            {
                var console = w.Ship.Furniture.Where(f => f.Type is FurnitureType.Console or FurnitureType.MainComputer && !f.Stowed && !f.Room.Detached && f.UseSpots.Count > 0)
                    .Select(f => (f, d: f.UseSpots.Select(dist.Get).Where(d => d >= 0).DefaultIfEmpty(int.MaxValue).Min())).Where(x => x.d < int.MaxValue).OrderBy(x => x.d).ThenBy(x => x.f.Id).FirstOrDefault().f;
                if (console == null || UseSpot(console, dist) is not Cell spot) { s.Note = "콘솔이 없다"; return null; }
                s.At = console;
                var toils = new List<Toil> { new GotoToil(spot), new WaitToil(SimTime.Minutes(1), Pose.Standing, console.Center), new DoToil((cm, world) => { AskComputer(cm, p, s); return true; }) };
                return Wrap(p, s, act, "컴퓨터에 묻기", toils, $"{console.Room.Name} 콘솔에서 컴퓨터에 묻는다", console.Room, console);
            }
            case StepKind.FindPerson or StepKind.Tell:
            {
                if (s.Who is not CrewMember who || who.Dead) { s.Note = "그 사람이 없다"; return null; }
                var room = s.Room ?? w.Brain2.Beliefs.WhereIs(c, who, out _);
                if (room == null) room = GuessPlace(c, who, dist);
                if (room != null && !RoomOk(c, room)) { s.Note = $"{room.Name}은(는) 위험하다 — 못 간다"; return null; }
                if (room == null || SpotIn(room, dist, c, who) is not Cell spot) { s.Note = $"{who.Name}이(가) 어디 있는지 모른다"; return null; }
                s.Room = room;
                bool tell = s.Kind == StepKind.Tell;
                var toils = new List<Toil> { new GotoToil(spot), new DoToil((cm, world) => { Meet(cm, p, s); return true; }) };
                return Wrap(p, s, act, tell ? "알리러 감" : "사람 챙기기", toils,
                    tell ? $"{Ko.EuRo(room.Name)} — {who.Name}에게 알리러 간다" : $"{who.Name}이(가) 걱정돼 {Ko.EuRo(room.Name)} 간다", room, urgent: tell);
            }
            case StepKind.Wait:
            {
                var toils = new List<Toil>
                {
                    new WaitToil(SimTime.Minutes(25), Pose.Sitting, null, SimTime.Minutes(2)) { DoneWhen = (cm, world) => cm.Room is not { Dark: true } },
                    new DoToil((cm, world) => { s.Note = cm.Room is { Dark: false } ? "불이 들어왔다" : "한참 기다렸다"; return true; }),
                };
                return Wrap(p, s, act, "웅크리고 기다림", toils, null, c.Room);
            }
            case StepKind.GoLit:
            {
                var room = LitRoom(c, dist);
                if (room == null || SpotIn(room, dist, c) is not Cell spot) { s.Note = "밝은 방을 모른다"; return null; }
                s.Room = room;
                var toils = new List<Toil>
                {
                    new GotoToil(spot),
                    new DoToil((cm, world) =>
                    {
                        world.Brain2.Beliefs.Look(cm);
                        if (cm.Room is { Dark: true } dr) { s.Note = $"{dr.Name}도 캄캄하다"; if (s.Tries++ < 1) Insert(p, StepKind.GoLit, Method.GoLit, "다른 밝은 곳으로"); return false; }
                        s.Note = $"{cm.Room?.Name ?? "?"}은(는) 밝다";
                        return true;
                    }),
                    new WaitToil(SimTime.Minutes(10), Pose.Standing),
                };
                return Wrap(p, s, act, "밝은 곳으로", toils, $"캄캄해서 {Ko.EuRo(room.Name)} 간다", room);
            }
            case StepKind.CheckRoom:
            {
                if (s.Room is Room lr && (lr.Lockdown || lr.OffLimits || lr.EvacuateBy >= 0 || lr.Detached)) { s.Note = $"{lr.Name}은(는) 봉쇄됐다"; return null; }
                if (s.Room is not Room room || SpotIn(room, dist, c) is not Cell spot) { s.Note = "그 방에 갈 수 없다"; return null; }
                var toils = new List<Toil> { new GotoToil(spot), new DoToil((cm, world) => { CheckRoom(cm, p, s); return true; }) };
                return Wrap(p, s, act, "불 확인", toils, $"{room.Name}에 불이 났다고 들었다 — 확인하러 간다", room);
            }
        }
        return null;
    }

    // ── 고치기: 부품 꺼내기 (믿는 보관함 → 없으면 믿음을 고치고 다음 길) ──
    private Job? BuildFetch(CrewMember c, CrewPlan p, PlanStep s, DistanceField dist, Activity act)
    {
        var w = _w;
        if (c.Carrying is ItemStack h && h.Kind == s.Item && h.Count >= s.Count) { Ok(c, p, s, "이미 손에 있다"); SkipToHandoff(p); return null; }
        bool none = false;
        var box = s.At ?? Believed(c, s.Item, dist, out none);
        if (box == null) { s.Note = none ? $"{ItemKinds.Name(s.Item)}이(가) 배에 없다고 안다" : $"{ItemKinds.Name(s.Item)}이(가) 어디 있는지 모른다"; return null; }
        if (UseSpot(box, dist) is not Cell spot) { s.Note = $"{box.Room.Name} {box.Name}에 갈 수 없다"; return null; }
        s.At = box;
        s.Room = box.Room;
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(spot));
        toils.Add(new DoToil((cm, world) =>
        {
            world.Brain2.Beliefs.Look(cm);
            int have = box.Storage?.Count(s.Item) ?? 0;
            if (have < s.Count || cm.Carrying != null)
            {
                world.Brain2.Beliefs.Learn(cm, Topic.Item, (int)s.Item, have > 0 ? box.Id : -1, BeliefSource.Seen, 1f, -1, have);
                s.Note = $"{box.Room.Name} {box.Name}에 {ItemKinds.Name(s.Item)}이(가) 없다";
                return false;
            }
            box.Storage!.Take(s.Item, s.Count);
            cm.Carrying = new ItemStack(s.Item, s.Count);
            world.Brain2.Beliefs.Learn(cm, Topic.Item, (int)s.Item, have - s.Count > 0 ? box.Id : -1, BeliefSource.Seen, 1f, -1, have - s.Count);
            Fetched++;
            p.Made = true;
            s.Note = $"{box.Room.Name}에서 꺼냈다";
            SkipToHandoff(p);
            return true;
        }));
        return Wrap(p, s, act, "부품 꺼내기", toils, $"{ItemKinds.Name(s.Item)} — {box.Room.Name} {box.Name}에 있을 것 같다", box.Room, box);
    }

    private static void SkipToHandoff(CrewPlan p)
    {
        foreach (var x in p.Steps) if (x.State == StepState.Todo && x.Kind is StepKind.Fetch or StepKind.Craft or StepKind.Trip or StepKind.AskHelp) x.State = StepState.Skipped;
    }

    // ── 작업대에서 만들기: 재료를 (믿는 곳에서) 모아 만든다. 재료가 없으면 실패 → 원정 제안 ──
    private Job? BuildCraft(CrewMember c, CrewPlan p, PlanStep s, DistanceField dist, Activity act)
    {
        var w = _w;
        if (Recipes.For(s.Item, Station.Workbench) is not Recipe r) { s.Note = $"{ItemKinds.Name(s.Item)} 만드는 법을 모른다"; return null; }
        var bench = w.Ship.FurnitureOf(FurnitureType.Workbench).Where(f => f.Machine is not { Efficiency: <= 0f } && UseSpot(f, dist) != null)
            .OrderBy(f => dist.Get(UseSpot(f, dist)!.Value)).ThenBy(f => f.Id).FirstOrDefault();
        if (bench == null) { s.Note = "쓸 수 있는 작업대가 없다"; return null; }
        if (c.SkillLevel(r.Skill) < MathF.Max(0.2f, r.MinSkill) && s.Tries == 0 && p.Kind == PlanKind.Fix)
        {
            // 솜씨가 모자라다 — 도움을 청한다 (부탁할 사람이 없으면 내가 서툴게라도)
            s.Tries++;
            Insert(p, StepKind.Craft, Method.CraftPart, $"부탁이 안 되면 서툴게라도 {ItemKinds.Name(s.Item)} 만들기").Tries = 1;
            Insert(p, StepKind.AskHelp, Method.AskHelp, $"{Skills.Name(r.Skill)} 잘하는 사람에게 부탁");
            s.State = StepState.Skipped;
            s.Note = "솜씨가 모자라다";
            return null;
        }
        var toils = Plans.DropOff(c, w, dist);
        bool first = true;
        foreach (var (kind, n) in r.Inputs)
        {
            var box = Believed(c, kind, dist, out bool none);
            if (box == null || UseSpot(box, dist) is not Cell bs) { s.Note = $"재료 {ItemKinds.Name(kind)} {(none ? "없음" : "어디 있는지 모름")}"; return null; }
            bool hand = first;
            first = false;
            toils.Add(new GotoToil(bs));
            toils.Add(new DoToil((cm, world) =>
            {
                int have = box.Storage?.Count(kind) ?? 0;
                if (have < n || hand && cm.Carrying != null)
                {
                    world.Brain2.Beliefs.Learn(cm, Topic.Item, (int)kind, have > 0 ? box.Id : -1, BeliefSource.Seen, 1f, -1, have);
                    s.Note = $"재료 {ItemKinds.Name(kind)}이(가) {box.Room.Name}에 없다";
                    return false;
                }
                box.Storage!.Take(kind, n);
                if (hand) cm.Carrying = new ItemStack(kind, n);
                else cm.Kit.Add((box, kind, n));
                return true;
            }));
        }
        var main = r.Inputs[0].kind;
        float eff = MathF.Max(0.4f, bench.Machine?.Efficiency ?? 1f);
        toils.Add(new GotoToil(UseSpot(bench, dist)!.Value));
        toils.Add(new WorkToil(r.Hours / eff, r.Skill, bench.Center) { CanContinue = (cm, _) => cm.Carrying?.Kind == main && bench.Machine is not { Efficiency: <= 0f } });
        toils.Add(new DoToil((cm, world) =>
        {
            if (cm.Carrying is not ItemStack held || held.Kind != main || held.Count < r.Inputs[0].count) { s.Note = "재료를 놓쳤다"; return false; }
            foreach (var (k, n) in r.Inputs.Skip(1)) if (cm.KitCount(k) < n) { s.Note = "재료가 모자라다"; return false; }
            foreach (var (k, n) in r.Inputs.Skip(1)) cm.UseKit(k, n);
            cm.Carrying = new ItemStack(s.Item, r.Yield);
            world.Parts.Made(s.Item, r.Yield, cm, cm.SkillLevel(r.Skill));
            cm.Practice(r.Skill, 0.02f);
            world.Adapt.PartsMade += r.Yield;
            Crafted++;
            p.Made = true;
            s.Note = $"작업대에서 {ItemKinds.Name(s.Item)}을(를) 만들었다";
            world.Log.Add(world.Tick, LogKind.Work, $"계획대로 — 창고에 없던 {Ko.EulReul(ItemKinds.Name(s.Item))} 작업대에서 만들었다", cm.Id);
            SkipToHandoff(p);
            return true;
        }));
        return Wrap(p, s, act, "부품 만들기", toils, $"{ItemKinds.Name(s.Item)}이(가) 없으면 만든다 — {bench.Room.Name} 작업대", bench.Room, bench);
    }

    private void ProposeTrip(CrewMember c, CrewPlan p, PlanStep s)
    {
        var w = _w;
        TripsProposed++;
        string part = ItemKinds.Name(p.Part ?? ItemKind.Plate) ?? "부품";
        w.Brain2.Goals.Push(c, "trip", "원정을 꺼내 보기", $"{part} 만들 재료가 바닥났다", ActCat.Explore, 48f);
        c.Say(w, Persona.Say(c, $"{part} 만들 재료도 없어. 나가서 구해 와야 해"));
        Life.Diary(w, c, Persona.Say(c, $"{p.Target?.Name ?? "설비"}를 고칠 {part}가 없다. 창고도 작업대도 안 된다. 원정을 꺼내야겠다."));
        w.Log.Add(w.Tick, LogKind.Ship, $"{Ko.IGa(c.Name)} {p.Target?.Name ?? "설비"} 고칠 {part} 재료가 없다며 원정을 꺼내려 한다", c.Id);
        s.Note = "원정을 꺼냈다";
        if (p.Target != null) _cool[(c.Id, p.Target.Id)] = w.Tick + SimTime.Hours(12);
    }

    // ── 고칠 자리 가까운 선반에 가져다 둔다 (보류된 수리를 풀어 준다) ──
    private Job? BuildHandoff(CrewMember c, CrewPlan p, PlanStep s, DistanceField dist, Activity act)
    {
        var w = _w;
        if (c.Carrying is not ItemStack held || held.Kind != s.Item)
        {
            if (!p.Made) { s.Note = "손에 부품이 없다"; return null; }
            // 다른 일 사이에 선반에 내려놓았다 — 배에는 있으니 보류를 푼다
            if (p.Order is WorkOrder po && !po.Closed) { po.BlockedUntil = w.Tick; po.BlockedReason = null; }
            w.Board.RequestScan();
            Handoffs++;
            Ok(c, p, s, $"{ItemKinds.Name(s.Item)}은(는) 선반에 있다 — 이제 고칠 수 있다");
            return null;
        }
        var (box, spot) = Plans.NearestContainer(w, dist, c, f => f.Storage!.Accepts(held.Kind) && f.Storage.Free >= held.Count && f.Type == FurnitureType.Shelf);
        if (box == null) (box, spot) = Plans.NearestContainer(w, dist, c, f => f.Storage!.Accepts(held.Kind) && f.Storage.Free >= held.Count);
        if (box == null) { s.Note = "둘 곳이 없다"; return null; }
        var toils = new List<Toil>
        {
            new GotoToil(spot),
            new PutToil(box),
            new DoToil((cm, world) =>
            {
                world.Brain2.Beliefs.Learn(cm, Topic.Item, (int)s.Item, box.Id, BeliefSource.Seen, 1f, -1, box.Storage!.Count(s.Item));
                if (p.Order is WorkOrder o && !o.Closed) { o.BlockedUntil = world.Tick; o.BlockedReason = null; }
                world.Board.RequestScan();
                Handoffs++;
                s.Note = $"{box.Room.Name} {box.Name}에 {ItemKinds.Name(s.Item)}을(를) 두었다 — 이제 고칠 수 있다";
                if (p.For is CrewMember asker && asker != cm && !asker.Dead)
                {
                    asker.ChangeAffinity(cm, 0.05f);
                    world.Brain2.Emotions.Feel(asker, Feeling.Joy, 0.12f, $"{cm.Name}이(가) 부품을 만들어 줬다", cm);
                    Life.Diary(world, asker, Persona.Say(asker, $"{Ko.IGa(cm.Name)} 부탁한 {ItemKinds.Name(s.Item)}을(를) 만들어 줬다"));
                }
                return true;
            }),
        };
        return Wrap(p, s, act, "부품 가져다 두기", toils, $"{ItemKinds.Name(s.Item)}을(를) {box.Room.Name} 선반에 둔다", box.Room, box);
    }

    // ── 도움 청하기: 솜씨 좋은 (믿는 자리의) 사람에게 가서 부탁한다 → 그 사람이 만들기를 맡는다 (일 나누기) ──
    private Job? BuildAskHelp(CrewMember c, CrewPlan p, PlanStep s, DistanceField dist, Activity act)
    {
        var w = _w;
        var part = p.Part ?? s.Item;
        if (Recipes.For(part, Station.Workbench) is not Recipe r) { s.Note = "만드는 법을 모른다"; return null; }
        var helper = s.Who ?? w.Brain2.Social.Helper(c, r.Skill, MathF.Max(0.35f, r.MinSkill));
        if (helper == null) { s.Note = "부탁할 사람이 없다"; return null; }
        s.Who = helper;
        var room = s.Room ?? w.Brain2.Beliefs.WhereIs(c, helper, out _) ?? GuessPlace(c, helper, dist);
        if (room != null && !RoomOk(c, room)) { s.Note = $"{room.Name}은(는) 지금 들어갈 수 없다"; return null; }
        if (room == null || SpotIn(room, dist, c, helper) is not Cell spot) { s.Note = $"{helper.Name}에게 갈 수 없다"; return null; }
        s.Room = room;
        var toils = new List<Toil>
        {
            new GotoToil(spot),
            new DoToil((cm, world) =>
            {
                world.Brain2.Beliefs.Look(cm);
                if (helper.Room != cm.Room || !helper.CanAct)
                {
                    s.Note = $"{helper.Name}이(가) {room.Name}에 없다";
                    if (s.Tries < 1) { var again = Insert(p, StepKind.AskHelp, Method.AskHelp, $"{helper.Name} 다시 찾아 부탁", Locate(cm, helper), null, helper); again.Tries = 1; }
                    return false;
                }
                if (!world.Brain2.Social.Agrees(helper, cm, out string no)) { s.Note = $"{helper.Name}: {no}"; helper.Say(world, Persona.Say(helper, no)); return false; }
                var hp = Begin(helper, PlanKind.Help, $"{cm.Name} 부탁 — {ItemKinds.Name(part)} 만들기", $"{Ko.IGa(cm.Name)} 부탁했다", Method.CraftPart);
                hp.Part = part; hp.Target = p.Target; hp.Order = p.Order; hp.For = cm;
                Add(hp, StepKind.Craft, Method.CraftPart, $"작업대에서 {ItemKinds.Name(part)} 만들기");
                Add(hp, StepKind.Handoff, Method.Handoff, $"{p.Target?.Name ?? "설비"} 곁 선반에 두기");
                cm.Say(world, Persona.Say(cm, $"{ItemKinds.Name(part)} 좀 만들어 줄래? 난 손이 안 따라가"));
                helper.Say(world, Persona.Say(helper, "그래, 내가 할게"));
                world.Log.Add(world.Tick, LogKind.Life, $"{helper.Name}에게 {ItemKinds.Name(part)} 만들기를 부탁했다 (일 나누기)", cm.Id);
                HelpAsked++;
                Shared++;
                s.Note = $"{Ko.IGa(helper.Name)} 맡았다";
                foreach (var x in p.Steps) if (x.State == StepState.Todo) x.State = StepState.Skipped;
                return true;
            }),
        };
        return Wrap(p, s, act, "도움 청하기", toils, $"{ItemKinds.Name(part)} 만들기를 {helper.Name}에게 부탁하러 간다", room);
    }

    // ── 정전: 배전반 ──
    private Job? BuildPanel(CrewMember c, CrewPlan p, PlanStep s, DistanceField dist, Activity act)
    {
        var w = _w;
        var panel = s.At ?? w.Ship.FurnitureOf(FurnitureType.PowerPanel).Where(f => UseSpot(f, dist) != null).OrderBy(f => dist.Get(UseSpot(f, dist)!.Value)).ThenBy(f => f.Id).FirstOrDefault();
        if (panel?.Machine is not Machine m || UseSpot(panel, dist) is not Cell spot) { s.Note = "배전반에 갈 수 없다"; return null; }
        s.At = panel;
        s.Room = panel.Room;
        int circ = p.Room?.Circuit ?? -1;
        if (s.Kind == StepKind.CheckPanel)
        {
            var toils = new List<Toil>
            {
                new GotoToil(spot),
                new WaitToil(SimTime.Minutes(0.5f), Pose.Standing, panel.Center),
                new DoToil((cm, world) =>
                {
                    bool trip = m.Faults.Any(f => f.Kind == FaultKind.BreakerTrip && (f.Circuit == circ || circ < 0));
                    var cause = Cause(p.Room);
                    if (p.Room != null) world.Brain2.Beliefs.Learn(cm, Topic.Outage, p.Room.Id, (int)cause, BeliefSource.Seen, 0.9f);
                    if (trip) { s.Note = $"{PowerGrid.CircuitName(Math.Max(0, circ))} 회로 차단기가 떨어져 있다"; Insert(p, StepKind.ResetPanel, Method.ResetBreaker, "차단기 올리기", panel.Room, panel); return true; }
                    if (p.Room is { Dark: false }) { s.Note = "벌써 누가 올렸다"; foreach (var x in p.Steps) if (x.State == StepState.Todo) x.State = StepState.Skipped; return true; }
                    s.Note = $"차단기는 멀쩡하다 — {BeliefSystem.OutageName(cause)}";
                    cm.Say(world, Persona.Say(cm, "차단기는 멀쩡한데…"));
                    return false;
                }),
            };
            return Wrap(p, s, act, "배전반 확인", toils, $"정전 — 차단기가 떨어졌을 거라 보고 {panel.Room.Name} 배전반으로", panel.Room, panel);
        }
        var reset = new List<Toil>
        {
            new GotoToil(spot),
            new WorkToil(0.25f, Skill.Electrical, panel.Center)
            {
                CanContinue = (cm, _) =>
                {
                    if (m.Faults.Any(f => f.Kind == FaultKind.BreakerTrip && (f.Circuit == circ || circ < 0))) return true;
                    Ok(cm, p, s, "벌써 누가 올렸다"); // 내가 가는 사이 누가 올렸다 — 실패가 아니다
                    return false;
                },
            },
            new DoToil((cm, world) =>
            {
                int n = m.Faults.RemoveAll(f => f.Kind == FaultKind.BreakerTrip && (f.Circuit == circ || circ < 0));
                foreach (var o in world.Board.Open.Where(o => o.Kind == WorkKind.ResetBreaker && o.Target.Furniture == panel && (o.Circuit == circ || circ < 0)).ToList()) world.Board.Close(o);
                world.Board.RequestScan();
                cm.Practice(Skill.Electrical, 0.02f);
                if (n > 0) { cm.Stats.Repairs++; Breakers++; world.Log.Add(world.Tick, LogKind.Work, $"{PowerGrid.CircuitName(Math.Max(0, circ))} 회로 차단기를 올렸다 (스스로 판단)", cm.Id); }
                s.Note = n > 0 ? "차단기를 올렸다" : "벌써 누가 올렸다";
                return true;
            }),
        };
        return Wrap(p, s, act, "차단기 올리기", reset, null, panel.Room, panel, urgent: true);
    }

    private void CheckPower(CrewMember c, CrewPlan p, PlanStep s)
    {
        var w = _w;
        w.Brain2.Beliefs.Look(c);
        var cause = Cause(p.Room);
        if (p.Room != null) w.Brain2.Beliefs.Learn(c, Topic.Outage, p.Room.Id, (int)cause, BeliefSource.Seen, 0.9f);
        s.Note = cause == OutageCause.Power ? $"배터리 {w.Power.BatteryCharge / MathF.Max(1f, w.Power.BatteryCapacity) * 100f:0}% · 발전이 모자라다" : $"발전은 멀쩡하다 — {BeliefSystem.OutageName(cause)}";
        c.Say(w, Persona.Say(c, s.Note));
        Checked++;
        if (cause == OutageCause.Breaker && c.SkillLevel(Skill.Electrical) >= 0.3f) Insert(p, StepKind.CheckPanel, Method.ResetBreaker, "배전반으로");
    }

    private void AskComputer(CrewMember c, CrewPlan p, PlanStep s)
    {
        var w = _w;
        var a = w.Automation;
        if (!a.Present || !a.MainOnline) { s.Note = "컴퓨터가 대답이 없다"; s.State = StepState.Doing; Fail(c, p, s, s.Note); if (c.SkillLevel(Skill.Electrical) >= 0.3f) Insert(p, StepKind.CheckPanel, Method.ResetBreaker, "직접 배전반으로"); else Insert(p, StepKind.GoLit, Method.GoLit, "밝은 곳으로"); return; }
        float trust = a.Trusts.Of(c);
        if (p.Kind == PlanKind.Outage && p.Room != null)
        {
            var cause = Cause(p.Room);
            w.Brain2.Beliefs.Learn(c, Topic.Outage, p.Room.Id, (int)cause, BeliefSource.Computer, 0.3f + 0.7f * trust, -2);
            s.Note = $"컴퓨터: {p.Room.Name} 정전 — {BeliefSystem.OutageName(cause)}";
            w.Log.Add(w.Tick, LogKind.Ship, $"[콘솔] {a.Voice.Call} → {c.Name}: {p.Room.Name} 정전 원인 — {BeliefSystem.OutageName(cause)}", c.Id);
            if (cause == OutageCause.Breaker && c.SkillLevel(Skill.Electrical) >= 0.3f && trust >= 0.3f) Insert(p, StepKind.CheckPanel, Method.ResetBreaker, "배전반으로");
        }
        else s.Note = "컴퓨터에 물었다";
    }

    private void CheckRoom(CrewMember c, CrewPlan p, PlanStep s)
    {
        var w = _w;
        w.Brain2.Beliefs.Look(c);
        bool fire = s.Room != null && w.Brain2.Beliefs.FireIn(s.Room) > 0;
        Checked++;
        w.Brain2.Learning.Record(c, Method.CheckFire, fire);
        s.Note = fire ? "정말 불이다" : "불이 없었다 — 헛걸음";
        if (fire) c.Interrupt(w);
    }

    /// <summary>사람을 찾아 만났다 (챙기기 · 알리기). 없으면 믿음을 고치고 다른 곳을 짐작한다.</summary>
    private void Meet(CrewMember c, CrewPlan p, PlanStep s)
    {
        var w = _w;
        var who = s.Who!;
        w.Brain2.Beliefs.Look(c);
        bool here = who.Room == c.Room && c.Room != null && !who.Dead;
        if (!here)
        {
            if (s.Room != null) w.Brain2.Beliefs.Missed(c, Topic.Person, who.Id, s.Room.Id);
            s.Note = $"{who.Name}이(가) {s.Room?.Name ?? "거기"}에 없다";
            s.State = StepState.Doing;
            Fail(c, p, s, s.Note);
            if (s.Tries < 1)
            {
                var next = Locate(c, who);
                var again = Insert(p, s.Kind, s.Method, $"{who.Name} 다시 찾기", next, null, who);
                again.Tries = s.Tries + 1;
            }
            return;
        }
        Found++;
        if (s.Kind == StepKind.Tell) { w.Brain2.Social.TellNow(c, who, p.FactTopic, p.FactId); s.Note = $"{who.Name}에게 알렸다"; return; }
        // 챙기기: 서로 안심한다
        var emo = w.Brain2.Emotions;
        c.Say(w, Persona.Say(c, $"{who.Name}, 괜찮아?"));
        who.Say(w, Persona.Say(who, emo.Get(who, Feeling.Fear) > 0.3f ? "와 줘서 다행이야…" : "응, 괜찮아"));
        emo.Feel(who, Feeling.Fear, -0.2f, "");
        emo.Feel(who, Feeling.Joy, 0.15f, $"{Ko.IGa(c.Name)} 챙기러 왔다", c);
        emo.Feel(c, Feeling.Fear, -0.15f, "");
        emo.Feel(c, Feeling.Joy, 0.08f, $"{Ko.IGa(who.Name)} 무사하다");
        who.ChangeAffinity(c, 0.04f);
        c.ChangeAffinity(who, 0.02f);
        s.Note = $"{Ko.IGa(who.Name)} 무사하다";
    }

    /// <summary>그 사람이 어디 있을지 다시 짐작: 컴퓨터에 묻기(믿으면) · 침대 · 일터.</summary>
    private Room? Locate(CrewMember c, CrewMember who)
    {
        var w = _w;
        var a = w.Automation;
        if (a.Present && a.MainOnline && a.Trusts.Of(c) >= 0.35f && who.Room is Room r && (r.DataLinked || a.Has(ComputerModule.BioMonitor)))
        {
            w.Brain2.Beliefs.Learn(c, Topic.Person, who.Id, r.Id, BeliefSource.Computer, 0.3f + 0.7f * a.Trusts.Of(c), -2);
            w.Log.Add(w.Tick, LogKind.Life, $"[손목 단말] {who.Name} 위치를 물었다 — {r.Name}", c.Id);
            return r;
        }
        return who.HomeBed?.Room ?? w.Ship.RoomsOf(who.Stations.Count > 0 ? who.Stations[0] : RoomType.Mess).FirstOrDefault();
    }

    private Room? GuessPlace(CrewMember c, CrewMember who, DistanceField dist)
    {
        var r = who.HomeBed?.Room ?? (who.Stations.Count > 0 ? _w.Ship.RoomsOf(who.Stations[0]).FirstOrDefault() : null);
        if (r != null) _w.Brain2.Beliefs.Learn(c, Topic.Person, who.Id, r.Id, BeliefSource.Guess, 0.3f);
        return r;
    }

    // ─────────────────────────── 정전: 길 고르기 (⑦ 같은 상황 다른 선택) ───────────────────────────

    /// <summary>
    /// 같은 정전에서 사람마다 다른 길: 역할 · 솜씨 · 용기(차단기) · 침착(하던 일) · 두려움(제자리 · 밝은 곳) · 사교성 · 가까운 사람(챙기기) ·
    /// 컴퓨터 신뢰 · 규칙(컴퓨터에 묻기) · 믿는 원인 · 배운 것(헛걸음한 방법은 덜) · 이미 누가 맡았나(일 나누기).
    /// </summary>
    public (Method m, float s, string why, CrewMember? who) ChooseOutage(CrewMember c, Room dark)
    {
        var w = _w;
        var t = c.Traits;
        var learn = w.Brain2.Learning;
        var bel = w.Brain2.Beliefs;
        var emo = w.Brain2.Emotions;
        float fear = emo.Get(c, Feeling.Fear);
        float trust = w.Automation.Present ? w.Automation.Trusts.Of(c) : 0f;
        var opts = new List<(Method m, float s, string why)>();
        var cb = bel.Get(c, Topic.Outage, dark.Id);
        var cause = cb != null && BeliefSystem.Eff(cb, w.Tick) >= 0.4f ? (OutageCause)cb.Value : OutageCause.Unknown;
        string role = CrewRoles.Name(c.Role);

        bool sparky = c.Role is CrewRole.Electrician or CrewRole.Engineer or CrewRole.Technician;
        float br = (sparky ? 0.5f : 0.08f) + 0.4f * c.SkillLevel(Skill.Electrical) + 0.15f * t.Bravery + (c.Value is CrewValue.Efficiency or CrewValue.Freedom ? 0.08f : 0f);
        string brWhy = sparky ? $"{role} — 차단기가 떨어졌을 거라 본다" : "차단기부터 보면 될 것 같다";
        if (cause == OutageCause.Breaker) { br *= 1.25f; brWhy = $"차단기가 떨어졌다고 {SourceWord(cb!)}"; }
        else if (cause != OutageCause.Unknown) { br *= 0.4f; brWhy = $"{BeliefSystem.OutageName(cause)}이라고 믿어 차단기는 아니다"; }
        foreach (var o in w.Crew)
        {
            if (o == c || o.Dead || Current(o) is not CrewPlan op || op.Kind != PlanKind.Outage || op.Method != Method.ResetBreaker) continue;
            if (o.Room == c.Room || bel.Conf(c, Topic.Person, o.Id, op.Steps.FirstOrDefault()?.Room?.Id ?? -9) > 0.3f || c.AffinityTo(o) > 0.2f)
            {
                br *= 0.35f;
                brWhy = $"{Ko.IGa(o.Name)} 벌써 배전반으로 갔다 — 나는 다른 걸";
                break;
            }
        }
        opts.Add((Method.ResetBreaker, br * learn.Bias(c, Method.ResetBreaker), brWhy + Learned(c, Method.ResetBreaker)));

        float pw = (c.Role == CrewRole.Engineer ? 0.45f : c.Role == CrewRole.Technician ? 0.3f : 0.05f) + 0.3f * c.SkillLevel(Skill.Engineering) + 0.1f * t.Diligence;
        string pwWhy = "발전 · 배터리 쪽을 보러";
        if (cause == OutageCause.Power) { pw *= 1.3f; pwWhy = $"발전 쪽 문제라고 {SourceWord(cb!)}"; }
        opts.Add((Method.PowerRoom, pw * learn.Bias(c, Method.PowerRoom), pwWhy + Learned(c, Method.PowerRoom)));

        if (w.Automation.Present)
        {
            float ac = 0.12f + 0.45f * trust + (c.Value == CrewValue.Rules ? 0.2f : 0f) - (c.Value == CrewValue.Freedom ? 0.15f : 0f);
            opts.Add((Method.AskComputer, ac * learn.Bias(c, Method.AskComputer), $"컴퓨터를 믿는다 ({trust * 100f:0}%){(c.Value == CrewValue.Rules ? " · 절차대로" : "")}" + Learned(c, Method.AskComputer)));
        }

        var (who, care) = Loved(c);
        var whereLoved = who != null ? bel.WhereIs(c, who, out _) : null;
        // 챙기러 가는 건 그 사람도 어둠 속일 것 같을 때: 큰 정전이거나 · 그 방이 캄캄하다고 믿거나 · 어디 있는지 모르거나
        if (who != null && (bel.Of(c).DarkWide || whereLoved == null || bel.Believes(c, Topic.Dark, whereLoved.Id, 1, 0.4f)))
        {
            var where = whereLoved;
            float cp = 0.12f + 0.35f * t.Sociability + 0.45f * care + (c.Value == CrewValue.People ? 0.2f : 0f) + 0.15f * (w.Brain2.Goals.Tilt(c, ActCat.Care) - 1f) * 4f;
            opts.Add((Method.CheckPeople, cp * learn.Bias(c, Method.CheckPeople), $"{Ko.IGa(who.Name)} 걱정된다 — {(where != null ? $"{where.Name}에 있을 것" : "어디 있는지 모른다")}" + Learned(c, Method.CheckPeople)));
        }

        bool darkFear = c.Fears.Contains(Fear.Dark);
        float sp = 0.08f + 0.75f * fear + (darkFear ? 0.35f : 0f) + 0.15f * (1f - t.Bravery) - 0.15f * t.Diligence;
        opts.Add((Method.StayPut, sp * learn.Bias(c, Method.StayPut), darkFear ? "어둠이 무섭다 — 움직이지 못한다" : fear > 0.3f ? $"무섭다 ({emo.Of(c).Cause[(int)Feeling.Fear] ?? "정전"})" : "가만히 있는 게 낫다"));

        float gl = 0.1f + 0.35f * fear + (c.Value == CrewValue.Safety ? 0.2f : 0f) + 0.15f * (1f - t.Calm);
        opts.Add((Method.GoLit, gl * learn.Bias(c, Method.GoLit), c.Value == CrewValue.Safety ? "안전이 먼저 — 밝은 곳으로" : "캄캄한 데 있기 싫다"));

        float co = 0.15f + 0.4f * t.Calm + 0.25f * t.Diligence - 0.45f * fear;
        opts.Add((Method.CarryOn, co, $"침착하다 — 하던 일을 마저 한다"));

        opts.Sort((a, b) => b.s.CompareTo(a.s) is int k && k != 0 ? k : ((int)a.m).CompareTo((int)b.m));
        var best = opts[0];
        Stances[c.Id] = (best.m, best.why, w.Tick, opts);
        return (best.m, best.s, best.why, best.m == Method.CheckPeople ? who : null);
    }

    private static string SourceWord(Belief b) => b.Src switch
    {
        BeliefSource.Seen => "직접 봤다",
        BeliefSource.Computer or BeliefSource.Broadcast => "컴퓨터가 그랬다",
        BeliefSource.Told or BeliefSource.Rumor => "들었다",
        _ => "짐작한다",
    };

    private string Learned(CrewMember c, Method m)
    {
        var (ok, bad) = _w.Brain2.Learning.Tally(c, m);
        return bad > ok && bad >= 0.9f ? " · 지난번엔 헛걸음" : ok > bad && ok >= 0.9f ? " · 지난번에 됐다" : "";
    }

    /// <summary>가장 마음 쓰이는 사람: 짝 · 아이 · 부모 · 가까운 사이 · 아이 · 다친 사람 (같은 방이면 뺀다).</summary>
    public (CrewMember? who, float care) Loved(CrewMember c)
    {
        CrewMember? best = null;
        float bv = 0.25f;
        foreach (var o in _w.Crew)
        {
            if (o == c || o.Dead || o.Away || o.Room == c.Room && c.Room != null) continue;
            float v = MathF.Max(0f, c.AffinityTo(o)) + (c.Partner == o.Id ? 0.5f : 0f) + (o.Parents.Contains(c.Id) ? 0.6f : 0f) + (c.Parents.Contains(o.Id) ? 0.4f : 0f)
                      + (o.IsChild ? 0.2f : 0f) + (o.Vitals.Injury > 0.3f ? 0.15f : 0f) + (Memory.AreComrades(c, o) ? 0.15f : 0f);
            if (v > bv || v == bv && best != null && o.Id < best.Id) { bv = v; best = o; }
        }
        return (best, best == null ? 0f : MathF.Min(1f, bv));
    }

    /// <summary>정전 대처를 시작한다 (고른 길 → 단계).</summary>
    public CrewPlan StartOutage(CrewMember c, Room dark, Method m, string why, CrewMember? who)
    {
        var p = Begin(c, PlanKind.Outage, $"정전 — {LearningSystem.Name(m)}", why, m);
        p.Room = dark;
        p.DarkSince = _w.Brain2.Beliefs.Of(c).DarkSince;
        _outageDone[c.Id] = p.DarkSince;
        switch (m)
        {
            case Method.ResetBreaker: Add(p, StepKind.CheckPanel, m, "배전반 보기"); break;
            case Method.PowerRoom: Add(p, StepKind.CheckPower, m, "발전 쪽 보기"); break;
            case Method.AskComputer: Add(p, StepKind.AskComputer, m, "콘솔에서 묻기"); break;
            case Method.CheckPeople: Add(p, StepKind.FindPerson, m, $"{who?.Name ?? "?"} 찾기", null, null, who); break;
            case Method.StayPut: Add(p, StepKind.Wait, m, "제자리에서 기다리기"); break;
            default: Add(p, StepKind.GoLit, m, "밝은 곳으로"); break;
        }
        // 막히면: 배운 대로 다음 길 (헛걸음한 방법은 뒤로)
        if (m is Method.ResetBreaker or Method.PowerRoom or Method.AskComputer)
        {
            var alts = new[] { Method.AskComputer, Method.PowerRoom, Method.GoLit }.Where(x => x != m)
                .OrderByDescending(x => _w.Brain2.Learning.Bias(c, x) * (x == Method.AskComputer ? 0.5f + _w.Automation.Trusts.Of(c) : x == Method.PowerRoom ? 0.5f + c.SkillLevel(Skill.Engineering) : 0.6f)).ThenBy(x => (int)x).ToList();
            var alt = alts[0];
            Add(p, alt == Method.AskComputer ? StepKind.AskComputer : alt == Method.PowerRoom ? StepKind.CheckPower : StepKind.GoLit, alt, $"안 되면 — {LearningSystem.Name(alt)}").Fallback = true;
            p.Trail.Add($"{SimTime.Clock(_w.Tick)} 고른 길: {LearningSystem.Name(m)} — {why}");
        }
        c.Say(_w, Persona.Say(c, m switch
        {
            Method.ResetBreaker => "차단기다. 내가 올리고 올게",
            Method.PowerRoom => "발전 쪽부터 봐야겠어",
            Method.AskComputer => "컴퓨터, 무슨 일이야?",
            Method.CheckPeople => $"{who?.Name ?? "다들"}… 괜찮겠지?",
            Method.StayPut => "아무것도 안 보여…",
            _ => "여긴 너무 어두워",
        }));
        return p;
    }

    /// <summary>이 정전에 이미 길을 골랐나 (같은 정전에 두 번 고르지 않는다).</summary>
    public bool OutageHandled(CrewMember c, long darkSince) => _outageDone.TryGetValue(c.Id, out var t) && t == darkSince;

    public static OutageCause CauseOf(World w, Room? r)
    {
        if (r == null) return OutageCause.Unknown;
        if (r.Powered && r.LightsOut) return OutageCause.Lights;
        if (!r.PowerLinked) return OutageCause.Wiring;
        if (r.BreakerOff) return OutageCause.Wiring;
        foreach (var f in w.Ship.FurnitureOf(FurnitureType.PowerPanel))
            if (f.Machine is Machine m && m.Faults.Any(x => x.Kind == FaultKind.BreakerTrip && x.Circuit == r.Circuit)) return OutageCause.Breaker;
        return OutageCause.Power;
    }

    private OutageCause Cause(Room? r) => CauseOf(_w, r);

    // ─────────────────────────── 고치기 계획 ───────────────────────────

    /// <summary>부품이 없어 보류된 수리를 보고 길을 짠다: 믿는 창고 → 작업대 → 원정 제안 → 가져다 두기.</summary>
    public CrewPlan? StartFix(CrewMember c, WorkOrder o, DistanceField? dist = null)
    {
        var w = _w;
        var f = o.Target.Furniture;
        var fault = f?.Machine?.Faults.FirstOrDefault(x => x.Kind == o.Fault && x.Circuit == o.Circuit);
        if (f == null || fault?.Part is not ItemKind part) return null;
        var p = Begin(c, PlanKind.Fix, $"{f.Name} 고치기 — {ItemKinds.Name(part)} 구하기", o.BlockedReason ?? "부품이 없어 보류된 수리", Method.StorePart);
        p.Target = f;
        p.Part = part;
        p.Order = o;
        p.Room = f.Room;
        var bel = w.Brain2.Beliefs;
        var box = bel.WhereItem(c, part, out float conf, out bool none);
        float storeBias = w.Brain2.Learning.Bias(c, Method.StorePart);
        // 믿는 보관함이 있으면 거기부터 — 지난번에 창고가 비어 헛걸음했고 확신이 약하면 작업대부터
        bool storeFirst = box != null && (conf >= 0.6f || storeBias >= 0.8f);
        if (storeFirst) { var st = Add(p, StepKind.Fetch, Method.StorePart, $"{box!.Room.Name} {box.Name}에서 {ItemKinds.Name(part)} 꺼내기"); st.At = box; }
        else if (!none) Add(p, StepKind.Fetch, Method.StorePart, $"창고에서 {ItemKinds.Name(part)} 찾기");
        Add(p, StepKind.Craft, Method.CraftPart, $"없으면 작업대에서 {ItemKinds.Name(part)} 만들기");
        if (!storeFirst && box != null) { var st = Add(p, StepKind.Fetch, Method.StorePart, $"{box.Room.Name}도 봐 두기"); st.At = box; }
        Add(p, StepKind.Trip, Method.ProposeTrip, "재료도 없으면 원정 제안");
        Add(p, StepKind.Handoff, Method.Handoff, $"{f.Room.Name} 곁 선반에 가져다 두기");
        p.Trail.Add($"{SimTime.Clock(w.Tick)} 시작 — {(storeFirst ? "창고부터 (믿는 곳)" : storeBias < 0.8f ? "지난번 창고 헛걸음 — 작업대부터" : "창고에 없다고 안다 — 작업대부터")}");
        w.Log.Add(w.Tick, LogKind.Life, $"{f.Name} 수리가 부품 때문에 막혔다 — 스스로 길을 짠다 ({string.Join(" → ", p.Steps.Select(s => LearningSystem.Name(s.Method)))})", c.Id);
        return p;
    }

    // ─────────────────────────── 시스템 틱 ───────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        if (!BrainSystem.Enabled) return;
        // 끝난 정전 대처 · 불 확인: 상황이 사라지면 접는다
        foreach (var c in w.Crew)
        {
            if (Current(c) is not CrewPlan p) continue;
            if (c.Dead || c.Away) { Finish(c, p, false, "떠났다", quiet: true); continue; }
            if (p.Kind == PlanKind.Outage && p.Room is Room r && !r.Dark && c.Room == r && p.Step?.Kind is StepKind.Wait or StepKind.GoLit or StepKind.FindPerson)
                Finish(c, p, true, "불이 들어왔다");
            else if (p.Kind == PlanKind.Outage && w.Tick - p.Since > SimTime.Hours(2)) Finish(c, p, false, "너무 오래 걸린다");
            else if (p.Kind is PlanKind.Fix or PlanKind.Help && (p.Order is { Closed: true } || p.Target?.Machine is Machine m && m.Faults.Count == 0) && c.Carrying?.Kind != p.Part)
                Finish(c, p, true, "누가 벌써 고쳤다");
            else if (w.Tick - p.Since > SimTime.Hours(14)) Finish(c, p, false, "흐지부지됐다");
        }
        // 10분에 한 번 · 4분의 1씩: 부품이 없어 막힌 수리를 보고 길을 짠다
        if (w.Tick % (World.SystemInterval * 16) != 0) return;
        _phase++;
        List<WorkOrder>? blocked = null;
        foreach (var c in w.Crew)
        {
            if (((c.Id + _phase) & 3) != 0 || c.Dead || !c.CanAct || !c.IsAwake || c.IsChild || c.Outside || Current(c) != null || c.Job?.Urgent == true) continue;
            if (Crisis.Acting(w)) break;
            blocked ??= w.Board.Open.Where(o => o.Kind == WorkKind.Repair && o.Assignee == null && o.BlockedUntil > w.Tick && o.BlockedReason != null && o.BlockedReason.Contains("없음")).ToList();
            foreach (var o in blocked)
            {
                if (o.Target.Furniture is not Furniture f || f.Room.Detached) continue;
                if (!CrewRoles.Owns(c.Role, o) && c.SkillLevel(o.Skill) < 0.45f) continue;
                if (_cool.TryGetValue((c.Id, f.Id), out var until) && until > w.Tick) continue;
                if (_cur.Values.Any(x => !x.Done && x.Target == f)) continue;
                if (!R.Chance(0.25f + 0.5f * c.Traits.Diligence)) continue;
                _cool[(c.Id, f.Id)] = w.Tick + SimTime.Hours(6);
                StartFix(c, o);
                break;
            }
        }
    }

    // ─────────────────────────── 도우미 ───────────────────────────

    /// <summary>그 물건이 있다고 믿는 보관함 — 모르면 컴퓨터에 묻고(믿으면), 그래도 모르면 창고 선반을 짐작한다.</summary>
    public Furniture? Believed(CrewMember c, ItemKind k, DistanceField dist, out bool none)
    {
        var w = _w;
        var bel = w.Brain2.Beliefs;
        var f = bel.WhereItem(c, k, out float conf, out none);
        if (f != null && conf >= 0.25f && !f.Stowed && !f.Room.Detached) return f;
        if (none) return null;
        var a = w.Automation;
        if (a.Present && a.MainOnline && a.Trusts.Of(c) >= 0.25f)
        {
            var (box, _) = Plans.NearestContainer(w, dist, c, x => x.Storage!.Count(k) > 0);
            bel.Learn(c, Topic.Item, (int)k, box?.Id ?? -1, BeliefSource.Computer, 0.3f + 0.65f * a.Trusts.Of(c), -2, box?.Storage!.Count(k) ?? 0);
            f = bel.WhereItem(c, k, out _, out none);
            return none ? null : f;
        }
        var (guess, _) = Plans.NearestContainer(w, dist, c, x => x.Type == FurnitureType.Shelf && x.Storage!.Accepts(k));
        if (guess != null) bel.Learn(c, Topic.Item, (int)k, guess.Id, BeliefSource.Guess, 0.3f);
        return guess;
    }

    private static Cell? UseSpot(Furniture f, DistanceField dist)
    {
        Cell? best = null;
        int bc = int.MaxValue;
        foreach (var s in f.UseSpots)
        {
            int d = dist.Get(s);
            if (d < 0 || d >= bc) continue;
            best = s;
            bc = d;
        }
        return best;
    }

    /// <summary>들어가도 되는 방: 봉쇄 · 출입 제한 · 대피 지시 · 대응 중이 아니고, 위험하다고 믿지 않는 방.</summary>
    public bool RoomOk(CrewMember c, Room r) =>
        !r.Detached && !r.OffLimits && !r.Lockdown && r.EvacuateBy < 0 && !r.ResponseHold && !r.Purging && !r.Inerting && _w.Brain2.Beliefs.SafeEnough(c, r);

    private Cell? SpotIn(Room r, DistanceField dist, CrewMember c, CrewMember? near = null)
    {
        Cell? best = null;
        float bc = float.MaxValue;
        foreach (var cell in r.Cells)
        {
            int d = dist.Get(cell);
            if (d < 0 || !_w.Ship.IsOpenFloor(cell) || _w.IsSpotTaken(cell, c)) continue;
            float cost = near != null && near.Room == r ? (cell.Center - near.Position).LengthSquared() * 10f + d * 0.01f : d;
            if (cost >= bc) continue;
            best = cell;
            bc = cost;
        }
        return best;
    }

    private Room? PowerRoom(DistanceField dist)
    {
        foreach (var t in new[] { RoomType.Power, RoomType.BatteryRoom, RoomType.Reactor, RoomType.Engine })
            foreach (var r in _w.Ship.RoomsOf(t))
                if (r.Cells.Any(cell => dist.Get(cell) >= 0)) return r;
        return null;
    }

    /// <summary>밝다고 믿는 가장 가까운 방 (캄캄하다고 믿는 방 · 위험하다고 믿는 방은 뺀다 — 모르는 방은 밝을 거라 짐작).</summary>
    private Room? LitRoom(CrewMember c, DistanceField dist)
    {
        var bel = _w.Brain2.Beliefs;
        Room? best = null;
        int bc = int.MaxValue;
        foreach (var r in _w.Ship.Rooms)
        {
            if (r == c.Room || !RoomOk(c, r) || r.Type == RoomType.Corridor || bel.Believes(c, Topic.Dark, r.Id, 1, 0.4f) || !bel.SafeEnough(c, r)) continue;
            foreach (var cell in r.Cells)
            {
                int d = dist.Get(cell);
                if (d < 0 || d >= bc || !_w.Ship.IsOpenFloor(cell)) continue;
                best = r;
                bc = d;
            }
        }
        return best;
    }

    public long Hash()
    {
        long h = 41;
        foreach (var (id, p) in _cur) h = h * 31 + id * 7 + p.Index + (int)p.Kind * 3 + p.Steps.Count;
        h = h * 31 + Started * 3 + Finished * 5 + Crafted * 7;
        return h;
    }
}

// ─────────────────────────── 활동: 계획대로 · 정전 대처 · 불 확인 ───────────────────────────

/// <summary>v16.15 계획대로: 지금 계획의 다음 단계를 한다 (끊기면 그 단계부터 다시).</summary>
public sealed class PlanActivity : Activity
{
    public static readonly PlanActivity Instance = new();
    public override string Id => "plan";
    public override string Label => "계획대로";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!BrainSystem.Enabled || w.Brain2.Plans.Current(c) is not CrewPlan p || p.Step is not PlanStep s) return (0f, "—");
        if (c.Outside || c.IsChild && p.Kind != PlanKind.Outage) return (0f, "—");
        float pr = p.Kind switch
        {
            PlanKind.Tell => 1.05f + 0.2f * c.Traits.Sociability,
            PlanKind.Outage => s.Kind == StepKind.Wait ? 0.85f : 0.95f + (s.Kind == StepKind.ResetPanel ? 0.25f : 0f),
            PlanKind.CheckFire => 0.7f + 0.2f * c.Traits.Bravery,
            _ => 0.45f + 0.3f * c.Traits.Diligence + (p.Order?.Urgency ?? 0.3f) * 0.3f,
        };
        pr *= w.Brain2.Goals.Tilt(c, ActCat.Plan);
        return (pr, $"계획: {p.Goal} — {s.Text}");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist) =>
        w.Brain2.Plans.Current(c) is CrewPlan p ? w.Brain2.Plans.JobFor(c, p, dist, this) : null;
}

/// <summary>v16.15 정전 대처: 내 방이 캄캄해진 걸 알아채면 (믿음) 성격 · 믿음 · 감정 · 배운 것대로 길을 고른다.</summary>
public sealed class OutageActivity : Activity
{
    public override string Id => "outage";
    public override string Label => "정전 대처";

    private static Room? DarkRoom(CrewMember c, World w)
    {
        var book = w.Brain2.Beliefs.Of(c);
        if (book.DarkSince < 0 || w.Tick - book.DarkSince > SimTime.Hours(1.5f)) return null;
        if (c.Room is Room r && w.Brain2.Beliefs.Believes(c, Topic.Dark, r.Id, 1, 0.5f)) return r;
        return null;
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!BrainSystem.Enabled || c.Outside || c.IsChild || !c.IsAwake || c.Down) return (0f, "—");
        if (DarkRoom(c, w) is not Room dark) return (0f, "—");
        var plans = w.Brain2.Plans;
        long since = w.Brain2.Beliefs.Of(c).DarkSince;
        if (plans.OutageHandled(c, since) || plans.Current(c) is { Kind: PlanKind.Outage or PlanKind.Tell }) return (0f, "이미 대처 중");
        // 대피 · 피난 중인 사람은 어둠 때문에 자리를 뜨지 않는다 (대피소 · 방공 · 격리)
        if (c.Job?.Activity is Activity ja && BrainSystem.Cat(ja) == ActCat.Survival) return (0f, "피하는 중 — 정전보다 급하다");
        var (m, s, why, _) = plans.ChooseOutage(c, dark);
        if (m == Method.CarryOn) return (0f, $"정전 — {why}");
        // 큰 위기 중에는 문제를 푸는 길(차단기 · 발전 · 컴퓨터)만 — 사람 챙기기 · 밝은 곳 찾기는 위기 대응에 맡긴다
        if (Crisis.Acting(w) && m is not (Method.ResetBreaker or Method.PowerRoom or Method.AskComputer)) return (0f, $"위기 중 — {why}");
        return (MathF.Min(1.25f, 0.7f + 0.4f * s), $"정전 — {why}");
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        if (DarkRoom(c, w) is not Room dark) return null;
        var plans = w.Brain2.Plans;
        var (m, _, why, who) = plans.ChooseOutage(c, dark);
        if (m == Method.CarryOn) return null;
        var p = plans.StartOutage(c, dark, m, why, who);
        return plans.JobFor(c, p, dist, PlanActivity.Instance);
    }
}

/// <summary>v16.15 믿음대로 불 확인: 불이 났다고 믿는(들은 · 오래된) 방에 가 본다 — 용감 · 성실하면 가고, 겁 많으면 피한다. 헛소문이면 믿음을 고친다.</summary>
public sealed class FireBeliefActivity : Activity
{
    public override string Id => "firebelief";
    public override string Label => "불 확인";

    private static (Belief? b, float s, string why) Pick(CrewMember c, World w)
    {
        var bel = w.Brain2.Beliefs;
        Belief? best = null;
        float bs = 0f;
        string bw = "";
        float fear = w.Brain2.Emotions.Get(c, Feeling.Fear);
        float will = 0.45f + 0.35f * c.Traits.Bravery + 0.15f * c.Traits.Diligence + (c.Background is Background.Firefighter or Background.SafetyInspector ? 0.2f : 0f)
                     - 0.45f * fear - (c.Fears.Contains(Fear.Fire) ? 0.3f : 0f);
        if (will < 0.5f) return (null, 0f, "");
        foreach (var b in bel.Dangers(c, 0.4f))
        {
            if (b.Topic != Topic.Fire || c.Room?.Id == b.Id || c.Mind.Knows.ContainsKey($"fire:{b.Id}")) continue;
            if (w.Brain2.Plans.Past.Any(p => p.Owner == c.Id && p.Kind == PlanKind.CheckFire && p.Room?.Id == b.Id && w.Tick - p.Since < SimTime.Minutes(40))) continue;
            float s = will * bel.Eff(b) * w.Brain2.Learning.Bias(c, Method.CheckFire);
            if (s <= bs) continue;
            best = b;
            bs = s;
            bw = $"{bel.RoomById(b.Id)?.Name ?? "?"}에 불이 났다고 {BeliefSystem.SourceName(b.Src)}{(b.From >= 0 && bel.CrewById(b.From) is CrewMember f ? $"({f.Name})" : "")} — 확인하러";
        }
        return (best, bs, bw);
    }

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!BrainSystem.Enabled || c.Outside || c.IsChild || !c.IsAwake || c.Down || w.Brain2.Plans.Current(c) != null) return (0f, "—");
        var (b, s, why) = Pick(c, w);
        return b == null ? (0f, "—") : (MathF.Min(1f, 0.35f + 0.6f * s), why);
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var (b, _, why) = Pick(c, w);
        if (b == null || w.Brain2.Beliefs.RoomById(b.Id) is not Room room) return null;
        var plans = w.Brain2.Plans;
        var p = plans.Begin(c, PlanKind.CheckFire, $"{room.Name} 불 확인", why, Method.CheckFire);
        p.Room = room;
        plans.Add(p, StepKind.CheckRoom, Method.CheckFire, $"{room.Name}에 가 보기", room);
        c.Say(w, Persona.Say(c, b.Src is BeliefSource.Rumor or BeliefSource.Told or BeliefSource.Overheard ? $"{room.Name}에 불이 났다던데 — 가 봐야겠어" : $"{room.Name} 불, 아직이면 큰일이야"));
        return plans.JobFor(c, p, dist, PlanActivity.Instance);
    }
}
