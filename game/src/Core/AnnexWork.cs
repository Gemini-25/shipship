using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v16.10 증축 공정의 실제 일 — 승무원은 우주복을 입고 에어락으로 나가 골조 · 외판을 한 칸씩 용접하고(드론이 거든다),
// 옆 방에서 기밀 시험(비눗물 · 압력계)을 하고, 새 방 바닥 뚜껑을 열어 배선 · 배관을 잇고(첫 점등), 비닐 막을 치고 패널을 잘라(먼지 —
// 가라앉을 때까지 용접을 미룬다 · 무시하면 분진 폭발) 침대를 들이고, 개통식에 모인다.
// 한 사람이 맡은 칸은 다른 사람이 건드리지 않는다 (BusyBy). 끊기면(경보 · 산소 · 운석) 진척은 칸에 남고 다음 사람이 잇는다.

public enum AnnexJob { Frame, Test, Wire, Sheet, Fit, Celebrate }

public sealed partial class AnnexSystem
{
    public sealed record Choice(AnnexPlan Plan, AnnexJob Kind, List<int> Units, Cell Spot, float Score, string Why);

    private readonly Dictionary<int, (int plan, AnnexJob kind)> _doing = new();
    private readonly Dictionary<int, int> _drone = new(); // 드론 → 맡은 칸
    private readonly Dictionary<int, long> _blocked = new();
    private readonly Dictionary<int, bool> _heed = new(); // 이 사람은 먼지 경고를 듣나 (일을 맡을 때 정한다)

    public bool Doing(CrewMember c) => _doing.ContainsKey(c.Id);
    /// <summary>화면: 그 드론이 증축 칸에서 일하나 (칸 번호).</summary>
    public int DroneUnit(Drone d) => _drone.TryGetValue(d.Id, out var i) ? i : -1;

    internal float DoingScore(CrewMember c)
    {
        if (!_doing.TryGetValue(c.Id, out var d)) return 0f;
        return d.kind switch
        {
            AnnexJob.Frame => c.Outside ? 1.05f : 0.85f, // 선체 밖: "돌아간다"(0.95)보다 높게 — 산소 · 운석 경보는 여전히 이긴다
            AnnexJob.Celebrate => 0.6f,
            _ => 0.62f,
        };
    }

    private bool UnitDone(AnnexPlan p, int i) => p.Stage == AnnexStage.Frame ? p.Frame[i] >= 1f : p.Frame[i] >= 1f && p.Plate[i] >= 1f;

    /// <summary>맡을 칸 (골조 → 외판, 휜 골조는 먼저).</summary>
    private List<int> NextUnits(AnnexPlan p, int max, int skip = -1)
    {
        var list = new List<int>();
        for (int i = 0; i < p.Frame.Length && list.Count < max; i++)
        {
            if (p.BusyBy[i] >= 0 || i == skip || UnitDone(p, i)) continue;
            if (_drone.ContainsValue(i)) continue;
            if (!Payable(p, i)) { if (list.Count == 0) Wait(p, i); break; }
            list.Add(i);
        }
        return list;
    }

    /// <summary>그 칸에 쓸 자재가 있나 (처음 세울 때만 — 휜 것을 펴는 데는 들지 않는다). 골조 네 칸에 구조재 하나, 외판 두 칸에 금속판 하나.</summary>
    private bool Payable(AnnexPlan p, int i)
    {
        var w = _w;
        if (p.Frame[i] < 1f) return p.Frame[i] > 0f || i % 4 != 0 || _paid.Contains((p.Id, i, true)) || w.Ship.CountStored(ItemKind.Structure) > 0;
        if (p.Stage != AnnexStage.Plating) return true;
        return p.Plate[i] > 0f || i % 2 != 0 || _paid.Contains((p.Id, i, false)) || w.Ship.CountStored(ItemKind.Plate) > 0;
    }

    private void Wait(AnnexPlan p, int i)
    {
        var w = _w;
        Stats.Waits++;
        var room = RoomOf(p.Site.AttachRoom);
        string what = p.Frame[i] < 1f ? "구조재" : "금속판";
        w.Automation.Book.Add(ActKind.Advice, room, $"증축 {(p.Frame[i] < 1f ? "골조" : "외판")} — {what} 없음", "공사가 멈췄다", "원정 · 해체로 자재를 구한다",
            $"{what}를 구해 오세요", "annex:wait", SimTime.Hours(6));
        if (w.Tick % SimTime.Hours(12) < World.SystemInterval)
            w.Expedition.ComputerRequest(p.Frame[i] < 1f ? MatCat.Structure : MatCat.Repair, approved: true);
    }

    /// <summary>이 사람이 지금 맡을 증축 일.</summary>
    public Choice? Choose(CrewMember c, DistanceField dist)
    {
        var w = _w;
        var p = Active;
        if (p == null || !c.CanAct || c.Away) return null;
        if (_blocked.TryGetValue(c.Id, out var until) && w.Tick < until) return null;
        var room = RoomOf(p.RoomId);
        // 개통식: 아이도 온다
        if (p.Stage == AnnexStage.Opening)
        {
            if (room == null || p.Celebrated.Contains(c.Id) || !c.IsAwake) return null;
            var cells = room.Cells.Where(x => w.Ship.IsWalkable(x) && dist.Reachable(x)).OrderBy(x => x.Y).ThenBy(x => x.X).ToList();
            if (cells.Count == 0) return null;
            var spot = cells[(c.Id * 7) % cells.Count];
            float sc = 0.55f + 0.25f * c.Traits.Sociability + (p.Hands.ContainsKey(c.Id) ? 0.12f : 0f) + (c.Id == p.Proposer ? 0.2f : 0f) - 0.3f * MathF.Max(0f, 0.3f - c.Needs.Rest);
            return new Choice(p, AnnexJob.Celebrate, new List<int>(), spot, sc, $"'{room.Name}' 개통식");
        }
        if (c.IsChild) return null;
        bool shift = SimTime.InWindow(SimTime.HourOfDay(w.Tick), c.Schedule.WorkStart, c.Schedule.WorkLength);
        bool tired = c.Needs.Rest < 0.22f || c.Needs.Stress > 0.78f;
        float s = shift ? 0.4f + 0.15f * c.Traits.Diligence : 0.14f + 0.1f * c.Traits.Diligence;
        if (p.Proposer == c.Id) s += 0.12f;
        else if (p.For.Contains(c.Id)) s += 0.06f;
        else if (p.Against.Contains(c.Id)) s -= 0.08f;
        bool cot = c.Bed == null || c.Bed.Type == FurnitureType.Cot;
        if (cot && p.Use == RoomType.Quarters) s += 0.1f; // 내 침대가 걸렸다
        s -= 0.25f * c.Needs.Stress + (tired ? 0.25f : 0f);
        switch (p.Stage)
        {
            case AnnexStage.Frame or AnnexStage.Plating:
            {
                if (EvaHalted || QuietHours || c.Vitals.Injury > 0.35f || c.Suit is { Oxygen: < 2f } || w.EvaRisk.RefusesFor(c, false)) return null;
                int out_ = 0;
                foreach (var kv in _doing) if (kv.Value.kind == AnnexJob.Frame) out_++;
                if (out_ >= 2) return null;
                var units = NextUnits(p, 3);
                if (units.Count == 0) return null;
                if (c.Role is CrewRole.Technician or CrewRole.Engineer) s += 0.08f;
                s += 0.05f * c.Traits.Bravery;
                bool frame = p.Frame[units[0]] < 1f;
                return new Choice(p, AnnexJob.Frame, units, p.Site.Shell[units[0]], s, $"증축 {(frame ? "골조" : "외판")} 용접 (EVA) — {units.Count}칸");
            }
            case AnnexStage.Pressure:
            {
                if (p.FullTest == null || p.PressureBy >= 0 || !dist.Reachable(p.Site.DoorInner)) return null;
                return new Choice(p, AnnexJob.Test, new List<int>(), p.Site.DoorInner, s + 0.05f, $"증축 기밀 시험{(p.FullTest == true ? " (끝까지)" : " (간단히)")} · 가압");
            }
            case AnnexStage.Utilities:
            {
                if (p.WireBy >= 0 || room == null || !dist.Reachable(p.Site.DoorOuter)) return null;
                if (c.Role is CrewRole.Electrician or CrewRole.Engineer) s += 0.1f;
                return new Choice(p, AnnexJob.Wire, new List<int>(), p.Site.DoorOuter, s, "증축 배선 · 배관 잇기");
            }
            case AnnexStage.FitOut:
            {
                if (QuietHours || room == null) return null;
                if (p.Sheet < 1f && p.SheetBy < 0 && dist.Reachable(p.Site.DoorInner))
                    return new Choice(p, AnnexJob.Sheet, new List<int>(), p.Site.DoorInner, s + 0.06f, "증축 문에 비닐 막");
                for (int i = 0; i < p.Fit.Length; i++)
                {
                    if (p.Fit[i] >= 1f || p.FitBy[i] >= 0) continue;
                    var at = FitSpot(p, i, dist);
                    if (at is not Cell sp) continue;
                    return new Choice(p, AnnexJob.Fit, new List<int> { i }, sp, s, $"증축 내장 — {p.FixtureName} #{i + 1}");
                }
                return null;
            }
        }
        return null;
    }

    private Cell? FitSpot(AnnexPlan p, int i, DistanceField dist)
    {
        var w = _w;
        var f = p.FixtureSpots[i];
        foreach (var d in new[] { new Cell(0, -1), new Cell(-1, 0), new Cell(1, 0) })
        {
            var n = f + d;
            if (w.Ship.RoomAt(n)?.Id == p.RoomId && w.Ship.IsWalkable(n) && dist.Reachable(n) && !p.FixtureSpots.Contains(n)) return n;
        }
        return null;
    }

    /// <summary>한 틱 일한 만큼 (사람).</summary>
    private void Work(CrewMember c, AnnexPlan p, AnnexJob kind, int i, Cell at)
    {
        var w = _w;
        if (p.State != "공사") return;
        if ((c.Position - at.Center).LengthSquared() > 2.3f) return;
        float sk = kind == AnnexJob.Wire ? c.SkillLevel(Skill.Electrical) : c.SkillLevel(Skill.Mechanics);
        float rate = (0.6f + 0.8f * sk) * (1f - 0.3f * c.Vitals.Injury) / SimTime.TicksPerHour;
        p.Hands[c.Id] = p.Hands.GetValueOrDefault(c.Id) + 1f / SimTime.TicksPerHour;
        switch (kind)
        {
            case AnnexJob.Frame:
                if (EvaHalted) return;
                if (p.Frame[i] < 1f) { p.Frame[i] = MathF.Min(1f, p.Frame[i] + rate / FrameHours); if (p.Frame[i] >= 1f) p.Bent[i] = false; }
                else if (p.Stage == AnnexStage.Plating && p.Plate[i] < 1f) p.Plate[i] = MathF.Min(1f, p.Plate[i] + rate / PlateHours);
                break;
            case AnnexJob.Test: p.Pressure = MathF.Min(1f, p.Pressure + rate / (p.FullTest == true ? TestHours : QuickTestHours)); break;
            case AnnexJob.Wire: p.Utilities = MathF.Min(1f, p.Utilities + rate / WireHours); break;
            case AnnexJob.Sheet: p.Sheet = MathF.Min(1f, p.Sheet + rate / SheetHours); break;
            case AnnexJob.Fit:
            {
                var cell = p.FixtureSpots[i];
                if (w.Matter.DustAt(cell) >= 0.3f)
                {
                    // 먼지가 뿌옇다: 용접 불꽃이 닿으면 분진 폭발 — 컴퓨터 말을 듣는 사람은 가라앉기를 기다린다
                    if (_heed.GetValueOrDefault(c.Id, true)) return;
                    if (p.Fit[i] >= 0.5f && p.Fit[i] < 0.5f + rate / FitHours + 1e-5f && RoomOf(p.RoomId) is Room rr)
                    {
                        int before = w.Matter.Stats.DustBlasts;
                        w.Matter.Spark(cell, rr, "증축 내장 용접 불꽃");
                        if (w.Matter.Stats.DustBlasts > before) { p.DustBlasts++; Stats.DustBlasts++; w.History.Add(w, HistoryKind.Incident, $"증축 내장 중 분진 폭발 — {Ko.IGa(c.Name)} 먼지 속에서 용접했다", rr, new[] { c }, at: cell, log: true); }
                    }
                }
                p.Fit[i] = MathF.Min(1f, p.Fit[i] + rate / FitHours);
                break;
            }
        }
    }

    /// <summary>칸 하나를 마쳤다 (자재를 쓰고 · 외판 품질을 매긴다).</summary>
    private void FinishUnit(CrewMember c, AnnexPlan p, int i)
    {
        var w = _w;
        if (i < 0 || i >= p.Frame.Length) return;
        if (p.BusyBy[i] == c.Id) p.BusyBy[i] = -1;
        if (p.Frame[i] >= 1f && Paid(p, i, frame: true)) Stats.Members++;
        if (p.Plate[i] >= 1f && p.PlateQ[i] <= 0f)
        {
            Paid(p, i, frame: false);
            float sk = c.SkillLevel(Skill.Mechanics);
            p.PlateQ[i] = Math.Clamp(0.32f + 0.55f * sk + R.Range(-0.15f, 0.12f) - 0.12f * c.Needs.Stress - 0.1f * c.Vitals.Injury, 0.2f, 1f);
            Stats.Plates++;
        }
        c.Practice(Skill.Mechanics, 0.01f);
    }

    private readonly HashSet<(int plan, int unit, bool frame)> _paid = new();
    private bool Paid(AnnexPlan p, int i, bool frame)
    {
        if (!_paid.Add((p.Id, i, frame))) return false;
        if (frame && i % 4 == 0) ItemsV15.Use(_w, ItemKind.Structure);
        if (!frame && i % 2 == 0) ItemsV15.Use(_w, ItemKind.Plate);
        return true;
    }

    /// <summary>일이 끝났다 (성공이든 끊김이든) — 맡은 칸을 놓는다.</summary>
    internal void Finished(CrewMember c, ToilStatus st)
    {
        _doing.Remove(c.Id);
        foreach (var p in Plans)
        {
            for (int i = 0; i < p.BusyBy.Length; i++) if (p.BusyBy[i] == c.Id) p.BusyBy[i] = -1;
            for (int i = 0; i < p.FitBy.Length; i++) if (p.FitBy[i] == c.Id) p.FitBy[i] = -1;
            if (p.PressureBy == c.Id) p.PressureBy = -1;
            if (p.WireBy == c.Id) p.WireBy = -1;
            if (p.SheetBy == c.Id) p.SheetBy = -1;
        }
        if (st == ToilStatus.Failed) _blocked[c.Id] = _w.Tick + SimTime.Minutes(20);
    }

    // ─────────────────────────────── 일 짜기 ───────────────────────────────

    public Job? Plan(CrewMember c, World w, DistanceField dist, Activity act)
    {
        if (Choose(c, dist) is not Choice ch) return null;
        var p = ch.Plan;
        var attach = w.Ship.Rooms[p.Site.AttachRoom];
        var toils = ShipSim.Core.Plans.DropOff(c, w, dist);
        Toil Step(AnnexJob kind, int i, Cell at, float hours, Vector2 face) => new WaitToil(SimTime.Hours(MathF.Max(0.2f, hours) * 3f) + 10, Pose.Working, face)
        {
            EveryTick = (cm, world) => world.Annex.Work(cm, p, kind, i, at),
            DoneWhen = (cm, world) => p.State != "공사" || kind switch
            {
                AnnexJob.Frame => world.Annex.EvaHalted || UnitDone(p, i),
                AnnexJob.Test => p.Pressure >= 1f,
                AnnexJob.Wire => p.Utilities >= 1f,
                AnnexJob.Sheet => p.Sheet >= 1f,
                _ => p.Fit[i] >= 1f,
            },
        };
        string label;
        string log;
        switch (ch.Kind)
        {
            case AnnexJob.Frame:
            {
                if (!WorkPlanners.AnnexEvaOut(c, w, dist, toils, out var blocked))
                {
                    _blocked[c.Id] = w.Tick + SimTime.Minutes(30);
                    return null;
                }
                foreach (int i in ch.Units)
                {
                    p.BusyBy[i] = c.Id;
                    var cell = p.Site.Shell[i];
                    int ii = i;
                    toils.Add(new GotoToil(cell, cm => !w.Annex.EvaHalted && p.State == "공사" && !UnitDone(p, ii)));
                    toils.Add(Step(AnnexJob.Frame, ii, cell, p.Frame[ii] < 1f ? FrameHours : PlateHours, cell.Center));
                    toils.Add(new DoToil((cm, world) => { world.Annex.FinishUnit(cm, p, ii); return true; }));
                }
                WorkPlanners.AnnexEvaIn(w, toils);
                Stats.EvaTrips++;
                bool frame = p.Frame[ch.Units[0]] < 1f;
                label = frame ? "증축 골조 (EVA)" : "증축 외판 (EVA)";
                log = $"우주복을 입고 선체 밖으로 — 증축 {(frame ? "골조" : "외판")} {ch.Units.Count}칸 용접";
                break;
            }
            case AnnexJob.Test:
            {
                p.PressureBy = c.Id;
                if (p.Pressure <= 0f) ItemsV15.Use(w, ItemKind.Sealant);
                toils.Add(new GotoToil(ch.Spot));
                toils.Add(Step(AnnexJob.Test, -1, ch.Spot, p.FullTest == true ? TestHours : QuickTestHours, p.Site.Door.Center));
                toils.Add(new DoToil((cm, world) => { world.Annex.FinishTest(cm, p); return true; }));
                label = "증축 기밀 시험";
                log = p.FullTest == true ? "외벽 너머 증축 칸에 압력을 걸고 이음마다 비눗물을 바른다" : "압력계만 보고 기밀 시험을 간단히";
                break;
            }
            case AnnexJob.Wire:
            {
                p.WireBy = c.Id;
                toils.Add(new GotoToil(ch.Spot));
                toils.Add(new DoToil((cm, world) =>
                {
                    var hatchAt = p.Site.Inside.FirstOrDefault(x => x != p.Site.DoorOuter && world.Ship.IsOpenFloor(x) && !world.Body.HatchOpenAt(x));
                    if (hatchAt != default && world.Body.OpenHatchAt(hatchAt, cm.Id, -1, "증축 — 배선 · 배관 잇기") != null) world.Body.SetMark(hatchAt, CellMark.Tape, 1f, "공사 테이프");
                    return true;
                }));
                toils.Add(Step(AnnexJob.Wire, -1, ch.Spot, WireHours, p.Site.Center));
                toils.Add(new DoToil((cm, world) => { world.Annex.FinishWire(cm, p); return true; }));
                label = "증축 배선 · 배관";
                log = "새 방 바닥 뚜껑을 열고 케이블 · 호스를 잇는다";
                break;
            }
            case AnnexJob.Sheet:
            {
                p.SheetBy = c.Id;
                toils.Add(new GotoToil(ch.Spot));
                toils.Add(Step(AnnexJob.Sheet, -1, ch.Spot, SheetHours, p.Site.Door.Center));
                toils.Add(new DoToil((cm, world) =>
                {
                    if (p.Sheet >= 1f) world.Log.Add(world.Tick, LogKind.Work, $"{Ko.IGa(cm.Name)} 증축 문에 비닐 막을 쳤다 (먼지가 {attach.Name}로 넘어오지 않게)", cm.Id);
                    return true;
                }));
                label = "비닐 막";
                log = "증축 문에 비닐 막을 친다";
                break;
            }
            case AnnexJob.Fit:
            {
                int i = ch.Units[0];
                p.FitBy[i] = c.Id;
                var cell = p.FixtureSpots[i];
                var au = w.Automation;
                _heed[c.Id] = !(au.Present && au.MainOnline) ? c.Traits.Calm > 0.45f || c.Value == CrewValue.Safety : au.Trusts.Of(c) >= 0.3f || c.Value == CrewValue.Safety;
                toils.Add(new GotoToil(ch.Spot));
                toils.Add(new DoToil((cm, world) =>
                {
                    // 패널을 자른다: 먼지 (비닐 막이 없으면 옆 방까지)
                    if (p.Fit[i] <= 0f)
                    {
                        world.Matter.Raise(cell, 0.5f, "증축 내장 — 패널 자르기");
                        if (p.Sheet < 1f && world.Ship.RoomAt(p.Site.DoorInner) is Room) world.Matter.Raise(p.Site.DoorInner, 0.3f, "증축 먼지 (비닐 막 없음)");
                        if (au.Present && au.MainOnline)
                            au.Book.Add(ActKind.Advice, world.Ship.RoomAt(cell), $"증축 내장 — 먼지 {world.Matter.DustAt(cell) * 100:0}%", "먼지 속 용접 불꽃 = 분진 폭발",
                                "가라앉을 때까지 용접을 미룬다", "환기 · 기다리기", "annex:dust:" + p.Id, SimTime.Minutes(30));
                    }
                    return true;
                }));
                toils.Add(Step(AnnexJob.Fit, i, ch.Spot, FitHours * 1.6f, cell.Center));
                toils.Add(new DoToil((cm, world) => { world.Annex.FinishFixture(cm, p, i); return true; }));
                label = $"증축 내장 — {p.FixtureName}";
                log = $"새 방에 {p.FixtureName}를 들인다 (자르고 · 먼지가 가라앉으면 용접)";
                break;
            }
            default: // 개통식
            {
                var room = RoomOf(p.RoomId)!;
                toils.Add(new GotoToil(ch.Spot));
                toils.Add(new WaitToil(SimTime.Minutes(20), Pose.Standing, room.Center)
                {
                    EveryTick = (cm, world) => cm.Needs.Social = MathF.Min(1f, cm.Needs.Social + 0.25f / SimTime.TicksPerHour),
                    DoneWhen = (cm, world) => p.Stage != AnnexStage.Opening,
                });
                toils.Add(new DoToil((cm, world) => { world.Annex.Celebrate(cm, p); return true; }));
                label = "개통식";
                log = $"'{room.Name}' 개통식에 간다";
                break;
            }
        }
        _doing[c.Id] = (p.Id, ch.Kind);
        return new Job(act, label, toils)
        {
            TargetRoom = RoomOf(p.RoomId) ?? attach, LogText = log, OnFinished = (cm, world, st) => world.Annex.Finished(cm, st),
        };
    }

    /// <summary>개통식: 모인 사람끼리 · 낸 사람의 한마디 · 지은 사람에게 박수.</summary>
    private void Celebrate(CrewMember c, AnnexPlan p)
    {
        var w = _w;
        if (p.Celebrated.Contains(c.Id)) return;
        p.Celebrated.Add(c.Id);
        Stats.Celebrated++;
        c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.04f);
        foreach (var id in p.Celebrated)
            if (id != c.Id && Crew(id) is CrewMember o && !o.Dead) { c.ChangeAffinity(o, 0.006f); o.ChangeAffinity(c, 0.006f); }
        var room = RoomOf(p.RoomId);
        if (c.Id == p.Proposer) c.Say(w, Persona.Say(c, "다들 고생했다 — 이제 간이침대는 없다"));
        else if (p.Hands.GetValueOrDefault(c.Id) > 1f) c.Say(w, Persona.Say(c, "저 골조, 내가 밖에 매달려 붙였다"));
        else if (p.Celebrated.Count == 1) c.Say(w, Persona.Say(c, "새 패널 냄새가 난다"));
        if (room != null) Life.Diary(w, c, Persona.Say(c, $"'{room.Name}' 개통식에 갔다."));
    }

    // ─────────────────────────────── 드론 ───────────────────────────────

    /// <summary>드론 용접: 일감 없는 드론 한 대가 골조 칸으로 나가 용접한다 (선외 공사가 멈추면 끝내고 돌아온다).</summary>
    private void DroneWork(AnnexPlan p, float dt)
    {
        var w = _w;
        var ds = w.Drones;
        bool eva = p.Stage is AnnexStage.Frame or AnnexStage.Plating && p.Enclosed < 0 && !QuietHours; // 밤에는 드론 용접도 멈춘다 (주 컴퓨터가 몬다)
        foreach (var d in ds.Drones)
        {
            if (!_drone.TryGetValue(d.Id, out int idx)) continue;
            if (!eva || EvaHalted || p.State != "공사" || d.State is not (DroneState.Outbound or DroneState.Working))
            {
                if (d.State == DroneState.Working) d.WorkDone = d.WorkNeeded;
                _drone.Remove(d.Id);
                continue;
            }
            if (d.State != DroneState.Working) continue;
            if (d.WorkNeeded < 1f) d.WorkNeeded = 1.4f;
            if (UnitDone(p, idx))
            {
                _drone.Remove(d.Id);
                var nx = NextUnits(p, 1);
                if (nx.Count == 0) { d.WorkDone = d.WorkNeeded; continue; }
                _drone[d.Id] = idx = nx[0]; // 이웃 칸이라 그 자리에서 팔을 뻗는다
            }
            float rate = 0.7f * RobotsV15.Work(d.Kind) * d.Quirk.Work * dt;
            if (p.Frame[idx] < 1f) { p.Frame[idx] = MathF.Min(1f, p.Frame[idx] + rate / FrameHours); if (p.Frame[idx] >= 1f) p.Bent[idx] = false; }
            else if (p.Stage == AnnexStage.Plating && p.Plate[idx] < 1f) p.Plate[idx] = MathF.Min(1f, p.Plate[idx] + rate / PlateHours);
            if (p.Frame[idx] >= 1f && Paid(p, idx, frame: true)) Stats.Members++;
            if (p.Plate[idx] >= 1f && p.PlateQ[idx] <= 0f) { Paid(p, idx, frame: false); p.PlateQ[idx] = 0.62f + R.Range(-0.05f, 0.08f); Stats.Plates++; }
        }
        if (!eva || EvaHalted || _drone.Count > 0 || w.Tick % SimTime.Minutes(10) >= World.SystemInterval || !ds.Controlled) return;
        foreach (var d in ds.Drones)
        {
            var b = RobotsV15.Base(d.Kind);
            if (b is not (DroneKind.Build or DroneKind.Repair) || d.State != DroneState.Docked || !d.Operational || d.Battery < 0.7f || d.Order != null || !DroneSystem.DockWorking(d)) continue;
            var nx = NextUnits(p, 1);
            if (nx.Count == 0) return;
            if (!ds.AnnexSortie(d, p.Site.Shell[nx[0]].Center, $"증축 {(p.Frame[nx[0]] < 1f ? "골조" : "외판")} 용접")) continue;
            _drone[d.Id] = nx[0];
            p.DroneSorties++;
            Stats.DroneWelds++;
            return;
        }
    }
}

/// <summary>v16.10 증축 공사 (선외 골조 · 외판 · 기밀 시험 · 배선 · 비닐 막 · 내장 · 개통식).</summary>
public sealed class AnnexWorkActivity : Activity
{
    public override string Id => "annexwork";
    public override string Label => "증축 공사";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        if (!c.CanAct) return (0f, "—");
        var an = w.Annex;
        if (c.Job?.Activity == this && an.DoingScore(c) is float ds && ds > 0f) return (ds, c.Job.Label);
        if (an.Active == null || c.Outside) return (0f, "—");
        if (an.Choose(c, dist) is not AnnexSystem.Choice ch) return (0f, "할 일 없음");
        float s = ch.Score;
        if (c.Pose == Pose.Sleeping) s *= 0.2f;
        return (MathF.Max(0f, s), ch.Why);
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist) => w.Annex.Plan(c, w, dist, this);
}

public static partial class WorkPlanners
{
    /// <summary>v16.10 증축: 에어락으로 나가고 들어오는 길 (StructureWork의 선외 일과 같다).</summary>
    internal static bool AnnexEvaOut(CrewMember c, World w, DistanceField dist, List<Toil> toils, out string? blocked) => EvaOut(c, w, dist, toils, out blocked);
    internal static void AnnexEvaIn(World w, List<Toil> toils) => EvaIn(w, toils);
}

public sealed partial class DroneSystem
{
    /// <summary>v16.10 증축: 일감 없이 골조 칸으로 나가 용접한다 (도착하면 증축 쪽이 일할 시간을 늘리고 진척을 채운다 — 끝나면 돌아온다).</summary>
    internal bool AnnexSortie(Drone d, Vector2 target, string what)
    {
        if (Hatch(_world) == null || d.State != DroneState.Docked) return false;
        Launch(d, target, what);
        return true;
    }
}
