using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v15.9 컴퓨터 모듈 7 → 20 · 칭호·업적 50: 새 모듈은 올리면 게임이 달라지고, 칭호는 조건이 맞으면 한 번만 붙는다
public static partial class Program
{
    private static int RunComputerV15Test(int seed)
    {
        _fails = 0;
        Console.WriteLine($"컴퓨터 모듈 20 · 칭호 50 점검 (v15.9) · 시드 {seed}\n");
        try
        {
            var mods = Enum.GetValues<ComputerModule>();
            var fresh = mods.Where(m => m >= ComputerModule.Foresight).ToList();
            var noRow = fresh.Where(m => ComputerV15.Of(m) == null || AutomationSystem.ModuleName(m) == m.ToString()).ToList();
            var rows = ChronicleV15.Rows;
            Check("목록 — 모듈 20 (새 13은 표·이름이 있다) · 칭호 50 (배 25 · 사람 25, 이름·id 겹침 없음)",
                mods.Length == 20 && ComputerV15.Rows.Length == 13 && fresh.Count == 13 && noRow.Count == 0
                && rows.Length == 50 && rows.Select(r => r.Id).Distinct().Count() == 50 && rows.Select(r => r.Name).Distinct().Count() == 50
                && rows.Count(r => r.Scope == TitleScope.Ship) == 25 && rows.All(r => (r.Ship != null) == (r.Scope == TitleScope.Ship) && (r.Crew != null) == (r.Scope == TitleScope.Crew)),
                $"모듈 {mods.Length} · 새 표 {ComputerV15.Rows.Length} · 표 없음 {string.Join(",", noRow)} · 칭호 {rows.Length}(배 {rows.Count(r => r.Scope == TitleScope.Ship)})");

            // 1) 새 모듈 13: 올리기 전과 뒤 — 같은 배에서 같은 자리를 재 본다
            {
                var w = DayOne(seed, "Hanbit");
                var a = w.Automation;
                var res = new List<(ComputerModule m, bool ok, string detail)>();
                void Probe(ComputerModule m, Func<float> measure, bool higherIsEffect, float ratio = 1f)
                {
                    float off = 0f, on = 0f;
                    try
                    {
                        a.Remove(m);
                        off = measure();
                        a.Install(m);
                        on = measure();
                    }
                    finally { a.Remove(m); }
                    bool ok = higherIsEffect ? on > off * ratio + 1e-6f : on < off * ratio - 1e-6f;
                    res.Add((m, ok, $"{ComputerV15.Name(m)} {off:0.####} → {on:0.####}"));
                }
                var linked = w.Ship.Machines.Where(m => m.Body.Room.DataLinked && !m.Body.Room.Detached && m.Faults.Count == 0 && m.Omen == null && m.Spec.FaultKinds.Length > 0).OrderBy(m => m.Body.Id).ToList();
                int omenAt = 0;
                Probe(ComputerModule.Foresight, () =>
                {
                    var m = linked[omenAt++];
                    var o = new Omen { Kind = OmenKind.Heat, Fault = m.Spec.FaultKinds[0], Since = w.Tick, Due = w.Tick + SimTime.Hours(20) };
                    m.Omen = o;
                    Prevention.Detect(w, m, o, "감지기", null);
                    float gain = (o.Due - w.Tick) / (float)SimTime.TicksPerHour;
                    m.Omen = null;
                    return gain;
                }, true);
                Probe(ComputerModule.MaintPlan, () =>
                {
                    float before = linked.Sum(m => m.Wear);
                    w.Machines.Update(1f);
                    return linked.Sum(m => m.Wear) - before;
                }, false, 0.95f);
                Probe(ComputerModule.WaterPlan, () => { w.Water.Update(w, 0.01f); return w.Water.Consumed; }, false);
                Probe(ComputerModule.PowerShare, () => { w.Power.Update(0.01f); return w.Power.Demand; }, false);
                var fridge = w.Ship.FurnitureOf(FurnitureType.Fridge).OrderBy(f => f.Id).First();
                Probe(ComputerModule.CargoSort, () =>
                {
                    fridge.Storage!.Add(ItemKind.Meal, 40);
                    int before = fridge.Storage.Total;
                    fridge.Machine!.Powered = false;
                    w.Machines.Update(4f);
                    fridge.Machine.Powered = true;
                    return before - fridge.Storage.Total;
                }, false);
                Probe(ComputerModule.RouteForecast, () => w.Sensors.Capability().lead, true);
                var tired = w.Crew.First(c => !c.Dead);
                var probe = new WorkOrder { Kind = WorkKind.Maintain, Skill = Skill.Mechanics };
                Probe(ComputerModule.FatigueAlert, () => { tired.Needs.Rest = 0.1f; return w.Life.MistakeOdds(tired, probe).p; }, false);
                Probe(ComputerModule.AutoCalib, () =>
                {
                    foreach (var m in linked) m.SensorCal = 1f;
                    w.Watch.Update(2f);
                    return linked.Sum(m => 1f - m.SensorCal);
                }, false, 0.8f);
                var cut = w.Ship.Rooms.Where(r => !r.Detached && r.Type != RoomType.Comms && r.Type != RoomType.Corridor).OrderBy(r => r.Id).First();
                Probe(ComputerModule.CommsRelay, () =>
                {
                    cut.DataLinked = false;
                    bool alarm = a.AlarmsIn(cut);
                    cut.DataLinked = true;
                    return alarm ? 1f : 0f;
                }, true);
                var clean = w.Ship.Rooms.Where(r => SoilSystem.CleanRoom(r) && !r.Detached && r.DataLinked).OrderBy(r => w.Crew.Count(c => c.Room == r)).ThenBy(r => r.Id).First();
                Probe(ComputerModule.SoilWatch, () =>
                {
                    var s = w.Soil.RoomSoil(clean);
                    for (int k = 0; k < s.Length; k++) s[k] = 0.8f;
                    w.Soil.Update(4f);
                    return 0.8f * s.Length - s.Sum();
                }, true, 1.2f);
                Probe(ComputerModule.PipeWatch, () =>
                {
                    w.Flow.WaterQuality = 0.5f;
                    w.Flow.Update(1f);
                    float gain = w.Flow.WaterQuality - 0.5f;
                    w.Flow.WaterQuality = 1f;
                    return gain;
                }, true, 1.2f);
                Probe(ComputerModule.Archive, () =>
                {
                    if (w.Culture.Customs.Count == 0) { w.History.Fires = Math.Max(1, w.History.Fires); w.Culture.Update(0.01f); }
                    var cu = w.Culture.Customs.First();
                    cu.Knowers.Add(w.Crew.First(c => !c.Dead).Id);
                    cu.Written = false;
                    w.Culture.Update(0.01f);
                    return cu.Written ? 1f : 0f;
                }, true);
                var door = w.Ship.Doors.Where(d => !d.IsExternal && d.RoomA is Room ra && d.RoomB is Room rb && ra != rb
                                                   && ra.Type != RoomType.Airlock && rb.Type != RoomType.Airlock && ra.DataLinked && rb.DataLinked && !ra.Leaking && !rb.Leaking)
                    .OrderBy(d => d.Id).First();
                Probe(ComputerModule.DoorPressure, () =>
                {
                    var lo = door.RoomA!;
                    lo.Air.O2 *= 0.7f; lo.Air.N2 *= 0.7f; lo.Air.CO2 *= 0.7f;
                    float before = FlowSystem.DoorDelta(lo, door.RoomB!);
                    for (int i = 0; i < 10; i++) a.Update(0.01f);
                    float after = FlowSystem.DoorDelta(lo, door.RoomB!);
                    w.Flow.Equalize(lo, door.RoomB!, 1f); // 다음 재기를 위해 되돌린다
                    return before - after;
                }, true);
                int works = res.Count(r => r.ok);
                Check("새 모듈 — 13 중 11 이상이 올리면 효과가 난다", works >= 11,
                    $"{works}/13 · " + string.Join(" · ", res.Select(r => (r.ok ? "" : "✗") + r.detail)));
            }

            // 2) 설치 경로와 칭호: 겪은 일·연구가 쌓이면 이튿날부터 하루 하나씩 올리고, 조건 맞은 칭호가 한 번만 붙는다
            {
                var w = DayOne(seed, "Mirinae");
                w.Research = Math.Max(w.Research, 500f);
                // 칭호 조건 맞추기 (배 10 · 사람 8 — 기록만 채운다)
                w.History.Meteors = 10; w.History.Fires = Math.Max(w.History.Fires, 5); w.History.Breaches = Math.Max(w.History.Breaches, 5);
                w.Soil.Stats.HandWashes = Math.Max(w.Soil.Stats.HandWashes, 100); w.Flow.Stats.Equalized = Math.Max(w.Flow.Stats.Equalized, 20);
                w.Parts.Stats.Replaced = Math.Max(w.Parts.Stats.Replaced, 40); w.Precursors.Prevented = Math.Max(w.Precursors.Prevented, 10);
                w.Sensors.Warned = Math.Max(w.Sensors.Warned, 5); w.Hazards.Count[0] += 15; w.Automation.Smothered++;
                var hero = w.Crew.First(c => !c.Dead);
                hero.Stats.Repairs = 40; hero.Stats.Chats = 40; hero.Stats.Rescues = 3; hero.Stats.Emergencies = 5; hero.Stats.Taught = 5;
                hero.Stats.Lessons = 10; hero.Stats.MealsCooked = 30; hero.Stats.Harvests = 10;
                float stress0 = hero.Needs.Stress = 0.5f;
                int awards0 = w.Titles.Awards.Count;
                Run(w, SimTime.Hours(3));
                var added = w.Automation.Modules.Where(m => m >= ComputerModule.Foresight).ToList();
                var installLog = w.History.Events.Where(e => e.Text.StartsWith("주 컴퓨터에") && e.Text.EndsWith(")") && added.Any(m => e.Text.Contains(ComputerV15.Name(m)))).Select(e => e.Text).ToList();
                Check("설치 — 이튿날부터 까닭이 있으면 주 컴퓨터에 새 모듈을 하나 올린다 (하루 하나)",
                    added.Count == 1 && installLog.Count == 1,
                    $"올린 것 {string.Join(",", added.Select(ComputerV15.Name))} · {installLog.LastOrDefault()}");
                var got = w.Titles.Awards.Skip(awards0).ToList();
                int shipGot = got.Count(x => x.CrewId < 0), heroGot = got.Count(x => x.CrewId == hero.Id);
                bool logged = w.History.Events.Count(e => e.Kind == HistoryKind.Milestone && e.Text.Contains("붙은 이름")) >= got.Count
                              && w.Log.Entries.Any(e => e.Text.Contains("붙은 이름")) && hero.Diary.Any(d => d.text.Contains("소리를 한다"));
                Check("칭호 — 조건을 맞춘 칭호 10개 이상이 생기고 연대기·일지·일기에 남는다", got.Count >= 10 && shipGot >= 8 && heroGot >= 6 && logged,
                    $"새 칭호 {got.Count} (배 {shipGot} · {hero.Name} {heroGot}) · {w.Titles.Line(hero)} · 스트레스 {stress0:0.00}→{hero.Needs.Stress:0.00}");
                int again = w.Titles.Check();
                Run(w, SimTime.Hours(2));
                var keys = w.Titles.Awards.Select(x => (x.Id, x.CrewId)).ToList();
                Check("중복 없음 — 같은 칭호는 같은 자리에 두 번 붙지 않는다", again == 0 && keys.Distinct().Count() == keys.Count,
                    $"다시 보기 {again} · 전체 {keys.Count} · 겹침 {keys.Count - keys.Distinct().Count()}");
            }

            // 3) 결정론
            {
                uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
                uint x = H(), y = H();
                Check("결정론 — 같은 시드 같은 지문", x == y, $"{x:x8} / {y:x8}");
                uint H2()
                {
                    var w = World.CreateDefault(seed, 0, "Mirinae");
                    foreach (var m in Enum.GetValues<ComputerModule>().Where(m => m >= ComputerModule.Foresight)) w.Automation.Install(m);
                    Run(w, SimTime.Hours(14));
                    w.Titles.Check();
                    return SaveGame.StateHash(w) ^ (uint)(w.Titles.Awards.Count * 7919) ^ (uint)w.Automation.V15Acts.Values.Sum();
                }
                uint p = H2(), q = H2();
                Check("결정론 — 새 모듈을 모두 올린 배도 같은 시드 같은 지문", p == q, $"{p:x8} / {q:x8}");
            }
        }
        catch (Exception e) { Console.WriteLine(e); _fails++; }
        Console.WriteLine(_fails == 0 ? "\n✔ 컴퓨터 모듈 20 · 칭호 50 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
