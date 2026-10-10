using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ShipSim.Core;

// v16.27 주컴퓨터 — 배의 한 구성원: PLAN v16.27 "확인" 칸을 하나씩 헤드리스로 일으켜 본다
public static partial class Program
{
    private static void MateRunTo(World w, int day, float hour)
    {
        long target = (long)(day - 1) * SimTime.TicksPerDay + SimTime.Hours(hour);
        while (w.Tick < target) w.Step();
    }

    private static int RunComputerCrewTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"주컴퓨터 — 배의 한 구성원 (v16.27) · 시드 {seed}\n");

        // ── 1) 아침마다 정비 · 날씨 · 주의할 곳 방송 + 저녁 일지가 연대기에 ──
        if (Sec(1))
        {
            var w = DayOne(seed, "Hanbit");
            var mate = w.Automation.Mate;
            var worn = w.Ship.Machines.Where(m => m.Body.Room.DataLinked && !m.Body.Room.Detached && m.Faults.Count == 0).OrderBy(m => m.Body.Id).First();
            worn.Wear = 0.72f; // 닳은 설비 하나 (정비표 · 주의할 곳)
            MateRunTo(w, 3, 10.9f);
            var b = mate.LastBriefing;
            Check("아침 방송이 나갔다 (오늘)", b != null && b.Day == 3, b == null ? "없음" : $"{b.Day}일 · {b.Text}");
            if (b != null)
            {
                var kinds = b.Lines.Select(l => l.Kind).ToHashSet();
                Check("방송에 정비 · 날씨 · 주의할 곳이 다 있다", kinds.Contains("정비") && kinds.Contains("날씨") && kinds.Contains("주의"), string.Join(" | ", b.Lines.Select(l => $"[{l.Kind}] {l.Text}")));
                Check("방송을 들은 사람이 있다 (들은 만큼 안다)", b.Heard.Count > 0, $"들은 사람 {b.Heard.Count}명");
                Check("게임 안 말에 개발 용어가 없다", !b.Text.Contains("AI") && !b.Text.Contains("트리아지") && !b.Text.Contains("v16"), b.Text);
            }
            MateRunTo(w, 3, 21.6f);
            var eve = mate.Evenings.LastOrDefault();
            Check("저녁 항해 일지를 썼다", eve.Text != null && eve.Text.StartsWith("3일"), eve.Text ?? "없음");
            var chapters = ChronicleBook.Chapters(w, false, ChronFilter.All);
            var ch3 = chapters.FirstOrDefault(c => c.FirstDay == 3);
            bool inChron = ch3 != null && eve.Text != null && ch3.ShipLog.Any(l => eve.Text.Contains(l.Text.TrimEnd('…').Split(" — ")[0]) || l.Tick == eve.Tick);
            Check("저녁 일지가 연대기 '장'의 컴퓨터 일지 칸에 있다", inChron, ch3 == null ? "3일 장 없음" : string.Join(" / ", ch3.ShipLog.Select(l => l.Text)));
        }

        // ── 2) 진동 추세로 고장 전에 정비가 잡힌다 ──
        if (Sec(2))
        {
            var w = DayOne(seed, "Hanbit");
            var mate = w.Automation.Mate;
            var pump = w.Ship.FurnitureOf(FurnitureType.CoolantPump).Select(f => f.Machine!).Where(m => m.Body.Room.DataLinked && m.Faults.Count == 0).OrderBy(m => m.Body.Id).FirstOrDefault()
                       ?? w.Ship.Machines.First(m => m.Body.Room.DataLinked && m.Faults.Count == 0);
            w.Policies.Set("maint", 1, "시험 — 고장 나면 고친다 (정비표만 미리 손본다)");
            pump.Wear = 0.38f;
            long start = w.Tick, serviced = -1;
            float wearAtService = -1f;
            UpkeepSlot? slot = null;
            bool faultBefore = false;
            for (long t = 0; t < SimTime.TicksPerDay * 4 && serviced < 0; t++)
            {
                if (t % SimTime.TicksPerHour == 0 && pump.LastServiced <= start) pump.Wear = MathF.Min(1f, pump.Wear + 0.012f); // 베어링이 빨리 닳는다 (진동이 커진다)
                w.Step();
                slot ??= mate.Slots.FirstOrDefault(s => s.MachineId == pump.Body.Id);
                if (pump.Faults.Count > 0) faultBefore = true;
                if (pump.LastServiced > start) { serviced = w.Tick; wearAtService = pump.Wear; }
            }
            var tr = mate.Trends.GetValueOrDefault(pump.Body.Id);
            Check("진동 추세를 읽어 남은 수명 범위를 냈다", slot != null, slot == null ? $"정비표에 없음 · 추세 {tr?.Now:0.00} 기울기 {tr?.Slope * 24:0.000}/일 · 수명 {tr?.LifeLo:0.0}~{tr?.LifeHi:0.0}" : $"{slot.Machine}: 남은 수명 {slot.LifeLo:0.0}~{slot.LifeHi:0.0}일 · {slot.Why}");
            if (slot != null)
            {
                Check("한가한 시간에 잡았다 (10 · 14 · 16시)", slot.Hour is 10f or 14f or 16f, $"{slot.Day}일 {slot.Hour}시");
                Check("미루면 위험이 얼마나 커지나를 적었다", slot.RiskLate > slot.RiskNow, $"지금 {slot.RiskNow * 100:0.0}% → 이틀 미루면 {slot.RiskLate * 100:0.0}%");
            }
            Check("고장 전에 정비했다 (마모 문턱 0.88 전에)", serviced > 0 && !faultBefore && wearAtService < 0.88f, serviced > 0 ? $"{SimTime.Clock(serviced)} 정비 (그때 마모 → {pump.Wear:0.00}) · 정비 전 고장 {faultBefore}" : $"정비 안 됨 · 마모 {pump.Wear:0.00} · 고장 {pump.Faults.Count}");
        }

        // ── 3) 12일 뒤 부족을 미리 알려 원정이 나간다 ──
        if (Sec(3))
        {
            bool off = MeetingSystem.MaidenOff;
            MeetingSystem.MaidenOff = true;
            var w = World.CreateDefault(seed, 0, "Hanbit");
            int plates = w.Ship.CountStored(ItemKind.Plate);
            foreach (var f in w.Ship.Containers) { if (plates >= 34) break; plates += f.Storage!.Add(ItemKind.Plate, 34 - plates); }
            Run(w, SimTime.TicksPerDay);
            MeetingSystem.MaidenOff = off;
            Player.Policy(w, "*", 0);
            var mate = w.Automation.Mate;
            foreach (var r in Recipes.AllFor(ItemKind.Plate)) foreach (var (k, _) in r.Inputs) Life.Take(w, k, w.Ship.CountStored(k)); // 원료도 바닥 (만들 수 없다)
            // 가까운 기항지를 지운다 (원정이 답인 길)
            var v = w.Voyage;
            for (int i = v.Index; i < v.Legs.Count - 1; i++) if (v.Legs[i].Kind == LegKind.Port) v.Legs[i] = new Leg { Kind = LegKind.Cruise, Name = "빈 바다", Days = v.Legs[i].Days };
            SupplyPlan? plan = null;
            long t0 = w.Tick;
            for (long t = 0; t < SimTime.TicksPerDay * 4 && (plan == null || w.Expedition.Current == null); t++)
            {
                if (t % (SimTime.TicksPerHour * 8) == SimTime.TicksPerHour) Life.Take(w, ItemKind.Plate, 1); // 하루 금속판 석 장씩 (외벽 땜질)
                w.Step();
                plan ??= mate.Supplies.FirstOrDefault(s => s.Key == "plate");
            }
            Check("금속판이 며칠 뒤 바닥나는지 미리 셈했다", plan != null && plan.Warned is >= 6f and <= 16f, plan == null ? $"계획 없음 · 남은 날 {mate.DaysLeft("plate"):0.0}" : $"{plan.Warned:0}일 뒤 (지금 {plan.DaysLeft:0}일) · {string.Join(" / ", plan.Options.Select(o => $"{o.opt} {o.score:0.00}"))} → {plan.Choice}");
            Check("원정을 골랐다", plan?.Choice == "원정", plan?.Choice ?? "—");
            Check("원정이 나간다", w.Expedition.Current != null || w.Expedition.Proposals.Any(p => p.Tick >= t0), $"원정 {(w.Expedition.Current is Trip tr ? $"{tr.Site.Name} {tr.Phase}" : "없음")} · 결과 {plan?.Result} · 카드 {plan?.Card?.State} {plan?.Card?.DecidedBy} {plan?.Card?.DecideWhy} · 자원 권한 {w.Automation.Authority.Level(Domain.Resources)} · 원정지 {w.Expedition.Sites.Count(s => !s.Taken)}");
        }

        // ── 4) 사각지대에 로봇 · 드론을 보내 직접 본다 ──
        if (Sec(4))
        {
            var w = DayOne(seed, "Hanbit");
            var mate = w.Automation.Mate;
            var room = w.Ship.LiveRooms.Where(r => r.Type is not RoomType.Corridor && r.Furniture.Any(f => f.Machine != null) && !r.OffLimits).OrderByDescending(r => r.Cells.Count).ThenBy(r => r.Id).First();
            w.Automation.Belief.Break(room, SensorFault.Blind, "시험 — 문 감지기 틀어짐");
            ScoutRun? run = null;
            for (int i = 0; i < SimTime.Hours(3) && (run == null || run.Seen < 0); i++) { w.Step(); run ??= mate.Scouts.FirstOrDefault(s => s.RoomId == room.Id); }
            Check("감지기가 안 닿는 방에 정찰을 보냈다", run != null, run == null ? "없음" : $"{run.By} {run.Unit} → {run.Room}");
            Check("직접 보고 믿음을 고쳤다", run != null && run.Seen >= 0 && w.Automation.Belief.Of(room).Updated >= run.Sent, run == null ? "—" : $"{(run.Seen >= 0 ? run.Found : "못 봄")}");
            // 로봇이 다 지쳤으면 드론이 바깥에서 들여다본다
            var w2 = DayOne(seed, "Hanbit");
            foreach (var r in w2.Robots.Robots) r.Battery = 0.2f;
            var room2 = w2.Ship.LiveRooms.Where(r => r.Type is not RoomType.Corridor && r.Joints.Count > 0 && !r.OffLimits).OrderByDescending(r => r.Cells.Count).ThenBy(r => r.Id).FirstOrDefault();
            if (room2 != null && w2.Drones.CanInspect())
            {
                w2.Automation.Belief.Break(room2, SensorFault.Blind, "시험");
                ScoutRun? run2 = null;
                for (int i = 0; i < SimTime.Hours(4) && (run2 == null || run2.Seen < 0 && !run2.Failed); i++)
                {
                    w2.Step(); run2 ??= w2.Automation.Mate.Scouts.FirstOrDefault(s => s.RoomId == room2.Id);
                    if (Environment.GetCommandLineArgs().Contains("--matedebug") && i % SimTime.Minutes(20) == 0)
                        Console.WriteLine($"      {SimTime.Clock(w2.Tick)} 일감 {string.Join(" / ", w2.Board.Open.Where(o => o.Kind == WorkKind.InspectHull).Select(o => $"{o.Title} 드론 {o.Drone?.Name} 사람 {o.Assignee?.Name} 막힘 {o.BlockedUntil > w2.Tick}"))} · 드론 {string.Join(" / ", w2.Drones.Drones.Select(d => $"{d.Name} {d.Kind} {d.State} {d.Doing} 배터리 {d.Battery:0.00}"))}");
                }
                Check("로봇이 없으면 드론이 정찰한다", run2 is { Drone: true, Seen: >= 0 }, run2 == null ? "없음" : $"{run2.By} · {(run2.Seen >= 0 ? run2.Found : run2.Failed ? "실패" : "가는 중")}");
            }
            else Console.WriteLine("    (드론 점검기가 없는 배 — 드론 정찰은 건너뜀)");
        }

        // ── 5) 반복 고장 자리에 개조 공사가 회의를 거쳐 들어간다 ──
        if (Sec(5))
        {
            var w = DayOne(seed, "Hanbit");
            var mate = w.Automation.Mate;
            foreach (var c in w.Crew.Where(c => !c.Dead)) w.Automation.Trusts.Change(c, 0.15f, "시험", quiet: true);
            foreach (var f in w.Ship.Containers) { if (f.Storage!.Add(ItemKind.Plate, 3) > 0) break; }
            foreach (var f in w.Ship.Containers) { if (f.Storage!.Add(ItemKind.Sensor, 2) > 0) break; }
            foreach (var f in w.Ship.Containers) { if (f.Storage!.Add(ItemKind.Cable, 3) > 0) break; }
            MateRunTo(w, 2, 7f); // 첫 원인 분석 (기준을 잡는다)
            var m = w.Ship.Machines.Where(x => !x.Body.Room.Detached && x.Body.Room.Type != RoomType.Corridor).OrderBy(x => x.Body.Room.Id).ThenBy(x => x.Body.Id).First();
            m.FaultCount += 3; // 같은 방이 자꾸 고장 났다
            m.Body.Room.Noise = MathF.Max(m.Body.Room.Noise, 0.4f);
            GearUpgrade? u = null;
            for (long t = 0; t < SimTime.TicksPerDay * 2 && (u == null || u.State != GearState.Done && u.State != GearState.Rejected); t++) { w.Step(); u ??= mate.Upgrades.FirstOrDefault(x => x.RoomId == m.Body.Room.Id); }
            Check("반복 고장의 원인을 따져 개조안을 냈다", u != null, u == null ? "없음" : $"{u.Room} {u.Name} — {u.Cause} ({u.Evidence})");
            Check("회의가 표결했다", u != null && u.Decided >= 0 && w.Meetings.Minutes.Any(r => r.Items.Any(i => i.Topic.StartsWith("computer:gear", StringComparison.Ordinal))), u == null ? "—" : $"찬성 {u.Yes} · 반대 {u.No} → {u.State}");
            Check("공사가 끝났다 (설치)", u?.State == GearState.Done, u == null ? "—" : $"{u.State} · 진척 {u.Done:0.0}/{u.Need:0.0} · 일꾼 {string.Join(",", u.Workers.Keys)}");
            if (u?.State == GearState.Done) Check("개조가 실제로 듣는다", u.Kind == MateGear.Mount ? mate.FaultMul(m) < 1f : u.Kind == MateGear.SensorNode ? mate.SensorNode(m.Body.Room) : u.Kind == MateGear.DataLine ? mate.Hardened(m.Body.Room) : mate.CapacityMul > 1f, $"{u.Name} · 고장률 ×{mate.FaultMul(m):0.0}");
        }

        // ── 6) 기억이 틀어진 컴퓨터를 승무원이 바로잡는다 · 약속을 지켰나 사람들이 기억한다 ──
        if (Sec(6))
        {
            var w = DayOne(seed, "Hanbit");
            var mate = w.Automation.Mate;
            w.Policies.Set("maint", 1, "시험 — 정비표만 미리 손본다");
            var targets = w.Ship.Machines.Where(x => !x.Body.Room.Detached && x.Body.Room.DataLinked && x.Faults.Count == 0 && x.Body.Type != FurnitureType.ReactorCore).OrderBy(x => x.Body.Id).Take(3).ToList();
            foreach (var t in targets) t.Wear = 0.66f;
            MateRunTo(w, 2, 3.8f);
            var e = mate.CorruptMemory(targets[0]); // 방사선이 기억 칸 하나를 틀었다: "어제 정비했다"
            MateRunTo(w, 2, 4.5f);
            bool skipped = !mate.Slots.Any(s => s.MachineId == targets[0].Body.Id && !s.Done);
            bool othersIn = mate.Slots.Any(s => s.MachineId == targets[2].Body.Id || s.MachineId == targets[1].Body.Id);
            Check("틀어진 기억으로 잘못 판단했다 (정비 안 한 설비를 했다고 믿고 정비표에서 뺐다)", e != null && e.Corrupt && skipped && targets[0].LastServiced < e.Value, $"{e?.Label} — 기억 속 정비 {SimTime.Clock(e?.Value ?? 0)} · 실제 {(targets[0].LastServiced > 0 ? SimTime.Clock(targets[0].LastServiced) : "없음")} · 표 {string.Join(",", mate.Slots.Select(s => s.Machine))} · 다른 둘 {string.Join(" / ", targets.Skip(1).Select(t => mate.Trends.TryGetValue(t.Body.Id, out var tr) ? $"{t.Name} n{tr.N} 지금 {tr.Now:0.00} 수명 {tr.LifeLo:0.0}" : $"{t.Name} 추세 없음"))}");
            var other = mate.CorruptMemory(targets[1]); // 다른 칸도 틀어져 있다 (말로는 안 나온다 — 검사가 찾아야)
            other!.Claimed = 1; // 방송엔 안 나간 칸
            MateRunTo(w, 2, 7.5f);
            var claim = mate.LastBriefing?.Lines.FirstOrDefault(l => l.Text.Contains(targets[0].Name));
            Check("아침 방송에 틀린 말이 그대로 나갔다", claim?.Text != null, claim?.Text ?? mate.LastBriefing?.Text ?? "—");
            for (int d = 0; d < 3 && mate.Corrections == 0; d++) { MateRunTo(w, SimTime.Day(w.Tick) + 1, 7.5f); }
            Check("승무원이 아침 방송을 듣고 바로잡았다", mate.Corrections > 0 && e != null && !e.Corrupt, $"바로잡음 {mate.Corrections} · {w.Log.Entries.Where(l => l.Text.Contains("아무도 안 손봤는데")).Select(l => l.Text).LastOrDefault()}");
            Check("자기 기억을 검사해 다른 틀어진 칸도 찾았다", mate.MemoryChecks > 0 && !other.Corrupt && mate.LastMemoryNote.Contains(" 1칸") , mate.LastMemoryNote);
            var pr = mate.Promised("memory");
            Check("다음엔 이렇게 — 약속을 남겼다", pr != null, pr?.Text ?? "—");
            // 다시 틀어진다 → 약속대로 맞춰 보고 말하나
            mate.CorruptMemory(targets[2]);
            mate.Brief(SimTime.Day(w.Tick));
            var hearer = w.Crew.Where(c => !c.Dead && pr != null && pr.Heard.Contains(c.Id)).OrderBy(c => c.Id).FirstOrDefault();
            var mem = hearer != null ? mate.Remembers(hearer) : default;
            Check("약속을 지켰고 들은 사람이 기억한다", pr != null && pr.Kept > 0 && mem.kept > 0, $"지킴 {pr?.Kept} · {hearer?.Name} 기억 {mem.kept}/{mem.broken}");
            float before = hearer != null ? w.Automation.Trusts.Of(hearer) : 0f;
            mate.ForceOverload = true;
            mate.CorruptMemory(targets[1]);
            mate.Brief(SimTime.Day(w.Tick));
            mate.ForceOverload = false;
            var mem2 = hearer != null ? mate.Remembers(hearer) : default;
            Check("어긴 약속도 기억하고 믿음이 깎인다", pr != null && pr.Broken > 0 && mem2.broken > 0 && hearer != null && w.Automation.Trusts.Of(hearer) < before, $"어김 {pr?.Broken} · 기억 {mem2.kept}/{mem2.broken} · 믿음 {before:0.00} → {(hearer != null ? w.Automation.Trusts.Of(hearer) : 0f):0.00}");
        }

        // ── 7) 훈련 결과로 비상 배치표가 바뀐다 ──
        if (Sec(7))
        {
            var w = DayOne(seed, "Hanbit");
            var mate = w.Automation.Mate;
            MateRunTo(w, 2, 10f);
            var bill = w.CrisisCrew.Bill;
            int v0 = bill.Version;
            var lazy = w.Crew.Where(c => !c.Dead && !c.IsChild && w.CrisisCrew.BillRole(c) != StationRole.None && c.IsAwake).OrderBy(c => c.Id).FirstOrDefault();
            if (lazy != null) { lazy.Needs.Stress = 1f; lazy.Needs.Rest = 0.3f; lazy.Value = CrewValue.Freedom; w.Automation.Trusts.Change(lazy, -0.4f, "시험", quiet: true); }
            var d = mate.PlanDrill("불", w.Tick + SimTime.Minutes(5));
            var jobs0 = new Dictionary<int, string>();
            for (int i = 0; i < SimTime.Minutes(75) && !(d?.Done ?? true); i++)
            {
                w.Step();
                if (Environment.GetCommandLineArgs().Contains("--matedebug") && d != null && d.Begun && i % SimTime.Minutes(1) == 0)
                    foreach (var (id, sp) in d.Spot) { var c = w.Crew.First(x => x.Id == id); string jb = $"{c.Job?.Activity?.Id}/{c.Job?.Label}/{c.Pose}"; if (jobs0.GetValueOrDefault(id) != jb) { jobs0[id] = jb; Console.WriteLine($"      {SimTime.Clock(w.Tick)} {c.Name} [{d.Role[id]}] {jb} 거리 {(c.Cell.X - sp.X) * (c.Cell.X - sp.X) + (c.Cell.Y - sp.Y) * (c.Cell.Y - sp.Y)} 도착 {d.Arrive.ContainsKey(id)}"); } }
            }
            Check("한가한 날 훈련을 했다 (사람이 실제로 자리로 걸었다)", d != null && d.Done && d.Arrive.Count > 0, d == null ? "—" : d.Summary);
            Check("진지한 사람과 귀찮아하는 사람이 갈렸다", d != null && d.Serious.Count > 0 && d.Skipped.Count + d.Grumbled.Count > 0, d == null ? "—" : $"진지 {d.Serious.Count} · 투덜 {d.Grumbled.Count} · 안 옴 {d.Skipped.Count}");
            Check("훈련 결과로 배치표가 바뀌었다", bill.Version > v0 && d != null && d.Changes.Count > 0, $"판 {v0} → {bill.Version} · {string.Join(" · ", d?.Changes ?? new List<string>())}");
            Check("자기 계획도 시험했다 (집결 셈을 고쳤다)", d != null && d.Expect > 0f && MathF.Abs(mate.MusterFactor - 1f) > 0.001f, $"셈 {d?.Expect:0.0}분 · 실제 {d?.Actual:0.0}분 · 보정 ×{mate.MusterFactor:0.00}");
        }

        // ── 8) 몰래 살핀 게 들켜 신뢰가 깎인다 ──
        if (Sec(8))
        {
            var w = DayOne(seed, "Hanbit");
            var mate = w.Automation.Mate;
            MateRunTo(w, 2, 12f);
            var people = w.Crew.Where(c => !c.Dead && !c.IsChild && c.CanAct).OrderBy(c => c.Id).ToList();
            var sad = people[0];
            var friend = people[1];
            sad.ChangeAffinity(friend, 0.6f); friend.ChangeAffinity(sad, 0.5f);
            sad.Value = CrewValue.Freedom;
            Life.Diary(w, sad, "그냥 좀 그래… 잠이 안 온다");
            w.Brain2.Emotions.Feel(sad, Feeling.Sadness, 0.4f, "시험");
            // 겉으로 드러날 만큼은 아니게 (슬픔 0.55 · 외로움이 크면 일기 없이도 '말수가 줄었다'로 보인다 — 그날 지낸 일에 따라 갈렸다)
            float sv = w.Brain2.Emotions.Get(sad, Feeling.Sadness);
            if (sv > 0.45f) w.Brain2.Emotions.Feel(sad, Feeling.Sadness, 0.45f - sv, "");
            sad.Needs.Social = MathF.Max(sad.Needs.Social, 0.5f);
            sad.Needs.Stress = 0.7f;
            w.Automation.Character.Nudge(0f, 0.8f, "시험 — 사람을 먼저 챙긴다");
            mate.SnoopBoost = 1f;
            mate.SetCare(1); // 회의가 '잠 · 끼니까지'로 정했다
            mate.CareOf(sad).SleepPrev = 7f; sad.Needs.Food = 0.9f;
            float t0 = w.Automation.Trusts.Of(sad);
            mate.CareNow();
            var hint = mate.CareHints.LastOrDefault(h => h.About == sad.Id);
            Check("허락보다 더 들여다봤다 (일기)", hint is { Snooped: true }, hint == null ? $"넌지시 없음 · 살핌 범위 {ShipMate.CareName(mate.CareLevel)}" : $"{hint.Line} · 몰래 {hint.Snooped}");
            Check("친한 동료에게 넌지시 알렸다", hint != null && hint.To == friend.Id, hint == null ? "—" : $"→ {w.Crew.First(c => c.Id == hint.To).Name}");
            for (int i = 0; i < SimTime.Hours(6) && !(hint?.Delivered ?? true); i++) w.Step();
            Check("동료가 찾아가 말을 걸었다 (진짜 행동)", hint?.Delivered == true, hint == null ? "—" : $"전함 {hint.Delivered}");
            Check("몰래 본 게 들켜 신뢰가 깎였다", hint?.Found == true && w.Automation.Trusts.Of(sad) < t0 - 0.1f && mate.KnowsSnoop.Contains(sad.Id), $"믿음 {t0:0.00} → {w.Automation.Trusts.Of(sad):0.00} · 아는 사람 {mate.KnowsSnoop.Count}");
            Check("사과하고 약속했다", mate.Promised("privacy") != null, mate.Promised("privacy")?.Text ?? "—");
            int lvl = mate.CareLevel;
            MateRunTo(w, 3, 21f);
            Check("회의가 살핌 범위를 다시 정했다", w.Meetings.Minutes.Any(r => r.Items.Any(i => i.Topic == "computer:care")), $"범위 {ShipMate.CareName(lvl)} → {ShipMate.CareName(mate.CareLevel)}");
        }

        // ── 9) 거절당하면 까닭을 짐작해 다른 사람 · 다른 설명 · 함장 ──
        if (Sec(9))
        {
            var w = DayOne(seed, "Hanbit");
            var mate = w.Automation.Mate;
            var cm = w.Automation.CrewModel;
            MateRunTo(w, 2, 14f);
            var m = w.Ship.Machines.Where(x => !x.Body.Room.Detached && x.Faults.Count == 0 && x.Body.Type != FurnitureType.ReactorCore).OrderBy(x => x.Body.Id).First();
            m.Wear = 0.7f;
            WorkOrder? o = null;
            for (int i = 0; i < SimTime.Minutes(30) && o == null; i++) { w.Step(); o = w.Board.Open.FirstOrDefault(x => x.Target.Furniture == m.Body && x.Kind == WorkKind.Maintain); }
            var sleeper = w.Crew.Where(c => !c.Dead && !c.IsChild && cm.Ready(c) && c != w.Command.Captain).OrderBy(c => c.Pose == Pose.Sleeping ? 0 : 1).ThenBy(c => c.Id).FirstOrDefault();
            if (o != null && sleeper != null)
            {
                foreach (var r in w.Robots.Robots) r.Battery = 0.1f; // 로봇이 먼저 가져가지 않게
                cm.Ask(sleeper, o, "시험 — 한밤의 부탁", 0.3f);
                w.Board.Block(o, null, 0.6f); // 다른 손이 먼저 닿지 않게 (밤 — 일감이 기다린다)
                RefusalCase? rc = null;
                for (int i = 0; i < SimTime.Hours(2) && rc == null; i++) { w.Step(); rc = mate.Refusals.FirstOrDefault(x => x.OrderId == o.Id); }
                Check("부탁을 안 따른 걸 알아채고 까닭을 짐작했다", rc != null, rc == null ? $"없음 · 일감 맡은 이 {o.Assignee?.Name ?? o.Robot?.Name ?? "없음"} · 닫힘 {o.Closed} · 부탁 남음 {cm.Asks.ContainsKey(sleeper.Id)}" : $"{sleeper.Name}: 짐작 '{rc.Guess}' · 실제 '{rc.Truth}'");
                Check("다른 사람에게 넘겼다", rc != null && rc.Next.StartsWith("다른 사람", StringComparison.Ordinal) && cm.Asks.Values.Any(a => a.OrderId == o.Id), rc?.Next ?? "—");
                // 두 번째도 막히면 함장에게
                if (rc != null && rc.Alt >= 0 && w.Crew.FirstOrDefault(c => c.Id == rc.Alt) is CrewMember alt && o.Assignee == null)
                {
                    var rc2 = mate.Refused(alt, new WorkAsk(alt.Id, o.Id, o.Title, "시험", w.Tick - 1, w.Tick - 10));
                    Check("또 막히면 함장에게 갔다", rc2 != null && rc2.Next.StartsWith("함장", StringComparison.Ordinal), rc2?.Next ?? "—");
                }
            }
            else Console.WriteLine($"    (정비 일감 {o != null} · 자는 사람 {sleeper != null} — 건너뜀)");
        }

        // ── 10) 권한 밖 딜레마 → 함장 · 함장과 연락이 안 되면 컴퓨터 성격대로 ──
        if (Sec(10))
        {
            foreach (bool reach in new[] { true, false })
            {
                var w = DayOne(seed, "Hanbit");
                var mate = w.Automation.Mate;
                MateRunTo(w, 2, 11f);
                var cap = w.Command.Captain;
                var room = w.Ship.LiveRooms.Where(r => r.Type is not RoomType.Corridor && r.Doors.Count(d => !d.Removed && !d.IsExternal) >= 1 && cap?.Room != r).OrderBy(r => r.Id).First();
                var two = w.Crew.Where(c => !c.Dead && !c.IsChild && c != cap).OrderBy(c => c.Id).Take(2).ToList();
                foreach (var c in two) { c.EndJob(w, ToilStatus.Interrupted); Teleport(w, c, room.Cells.First(w.Ship.IsOpenFloor)); }
                if (!reach && cap != null) { w.Automation.Character.Nudge(0f, -0.8f, "시험 — 배를 먼저 지킨다"); cap.Away = true; }
                for (int i = 0; i < 3; i++) w.Step();
                var d = mate.StartDilemma(room, two.Where(c => c.Room == room).ToList(), 8, "불");
                for (int i = 0; i < SimTime.Minutes(6) && d.Decided < 0; i++) w.Step();
                if (reach) Check("권한 밖 딜레마를 함장에게 물었다", d.AskedCaptain && d.By.StartsWith("함장", StringComparison.Ordinal), $"{d.Room} · {d.Decision} · {d.By}");
                else Check("함장과 연락이 안 되면 컴퓨터가 제 성격대로 정했다", !d.AskedCaptain && d.By.StartsWith("주 컴퓨터", StringComparison.Ordinal) && d.Decision == "닫는다", $"{d.Unreachable} · {d.Decision} · {d.By}");
                if (cap != null) cap.Away = false;
            }
        }

        // ── 11) 첫날도 분명한 피로엔 쉬라고 한다 ──
        if (Sec(11))
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(9));
            var mate = w.Automation.Mate;
            CrewMember? c = null;
            for (int i = 0; i < SimTime.Hours(4) && c == null; i++) { w.Step(); c = w.Crew.Where(x => !x.Dead && !x.IsChild && x.IsAwake && x.CanAct && x.Job?.Order != null && x.Room is { DataLinked: true } && x.Room.Type is not (RoomType.Quarters or RoomType.QuietQuarters)).OrderBy(x => x.Id).FirstOrDefault(); }
            c ??= w.Crew.First(x => !x.Dead && !x.IsChild);
            c.Needs.Rest = 0.12f; // 일하다 꾸벅꾸벅 졸기 시작했다 (첫날 — 컴퓨터는 이 사람을 오래 본 적이 없다)
            int before = mate.EarlyRests;
            bool known = w.Automation.CrewModel.Ready(c);
            mate.CheckFatigue();
            Check("첫날 오전에도 분명히 지친 사람에게 쉬라고 했다", mate.EarlyRests > before && w.Automation.CrewModel.RestAsked(c), $"{c.Name} 기력 {c.Needs.Rest * 100:0}% · 쉬라 {w.Automation.CrewModel.RestAsked(c)} · 30번 넘게 본 사람 {known} · {w.Tick / (float)SimTime.TicksPerHour:0.0}시간째 · {c.Room?.Name} 읽힘 {(c.Room != null && w.Automation.Belief.Reading(c.Room))} · 깸 {c.IsAwake} · {c.Job?.Activity?.Id} · 켜짐 {w.Automation.MainOnline} · 알아챔 {mate.EarlyRests - before}");
        }

        // ── 14) 과부하로 덜 급한 경보를 놓치고 나중에 인정한다 ──
        if (Sec(14))
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(10));
            var mate = w.Automation.Mate;
            mate.ForceOverload = true;
            var room = w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor).OrderBy(r => r.Id).First();
            for (int i = 0; i < 6; i++) { w.RaiseAlert($"{room.Name} 습도가 높다 ({i + 1})", room, AlertLevel.Warning, false); w.Step(); }
            Run(w, SimTime.Minutes(20));
            int missed = mate.MissedAlarms.Count;
            mate.ForceOverload = false;
            Run(w, SimTime.Minutes(20));
            Check("일이 몰린 사이 덜 급한 경보를 놓쳤다", missed > 0, $"놓친 경보 {missed}건");
            Check("숨이 트이자 늦게 본 걸 인정했다 (약속도)", mate.Admitted > 0 && mate.MissedAlarms.All(x => x.Admitted >= 0) && mate.Promised("alarm") != null, $"인정 {mate.Admitted} · 약속 {mate.Promised("alarm")?.Text}");
        }

        // ── 15) 성격 탓 실수: 과감하면 낙관해 정비를 미루다 놓친다 → 사고 뒤 검토 ──
        if (Sec(15))
        {
            var w = DayOne(seed, "Hanbit");
            var mate = w.Automation.Mate;
            w.Automation.Character.Nudge(-0.9f, 0f, "시험 — 과감해졌다");
            w.Policies.Set("maint", 1, "시험");
            var m = w.Ship.Machines.Where(x => x.Body.Room.DataLinked && x.Faults.Count == 0 && x.Body.Type != FurnitureType.ReactorCore).OrderBy(x => x.Body.Id).First();
            m.Wear = 0.4f;
            UpkeepSlot? slot = null;
            for (long t = 0; t < SimTime.TicksPerDay * 2 && slot == null; t++) { if (t % SimTime.TicksPerHour == 0) m.Wear = MathF.Min(1f, m.Wear + 0.01f); w.Step(); slot = mate.Slots.FirstOrDefault(s => s.MachineId == m.Body.Id && !s.Done); }
            if (slot != null && w.Tick < slot.At)
            {
                w.Machines.Break(m); // 정비표의 날보다 먼저 멎었다
                for (int i = 0; i < SimTime.Hours(2); i++) w.Step();
            }
            var rv = mate.Reviews.LastOrDefault();
            Check("과감한 성격이 남은 수명을 넉넉히 봤다가 놓친 것이 사고 뒤 검토에 남는다", rv != null && rv.Text.Contains("낙관") && w.Automation.Review.Reviews.Any(r => r.Title.StartsWith("정비")), rv?.Text ?? $"검토 없음 · 칸 {(slot == null ? "없음" : $"{slot.Day}일 {slot.Hour}시 (수명 {slot.LifeLo:0.0}~{slot.LifeHi:0.0})")}");
        }

        // ── 16) 우주 날씨 예보: 선외 일 미룸 · 드론 들임 · 대피소 점검 · 가끔 빗나감 → 약속 ──
        if (Sec(16))
        {
            var w = DayOne(seed, "Hanbit");
            var mate = w.Automation.Mate;
            MateRunTo(w, 2, 9f);
            var f = mate.MakeForecast(storm: 0.6f, shower: 0.05f);
            Check("폭풍 예보가 높으면 선외 일을 미룬다", f.Held && mate.EvaHold, mate.SkyLine(f));
            for (int i = 0; i < SimTime.Hours(3) && mate.ShelterChecks == 0; i++) w.Step();
            Check("대피소를 사람이 가서 점검했다", mate.ShelterChecks > 0, $"점검 {mate.ShelterChecks}");
            Run(w, SimTime.TicksPerDay + SimTime.Minutes(30));
            Check("폭풍이 안 오면 빗나간 예보로 남는다 (가끔 빗나감)", f.Judged && !f.Hit && f.Verdict.Contains("빗나갔다"), f.Verdict);
            var f2 = mate.MakeForecast(storm: 0.04f, shower: 0.02f);
            w.Hazards.StartStorm();
            Run(w, SimTime.TicksPerDay + SimTime.Minutes(30));
            Check("못 본 폭풍은 인정하고 약속한다", f2.Judged && f2.StormCame && !f2.Hit && mate.Promised("weather") != null, $"{f2.Verdict} · 약속 {mate.Promised("weather")?.Text}");
        }

        // ── 99) 점검용: 하루 동안 몇 번이나 나섰나 (--only=99) ──
        if (Environment.GetCommandLineArgs().Any(a => a == "--only=99"))
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.TicksPerDay * 2);
            var m = w.Automation.Mate;
            Console.WriteLine($"    정찰 {m.Scouts.Count} ({string.Join(", ", m.Scouts.Select(x => $"{SimTime.Clock(x.Sent)} {x.Room} {x.By}"))}) · 정비표 {m.Slots.Count} · 물자 {m.Supplies.Count} ({string.Join(", ", m.Supplies.Select(x => $"{x.Name} {x.Choice}"))}) · 넌지시 {m.Hints} · 근무 조정 {m.Adjusts} · 바로 쉬라 {m.EarlyRests} · 거절 {m.Refusals.Count} · 예보 보류 {m.Forecasts.Count(f => f.Held)}/{m.Forecasts.Count} · 훈련 {m.Drills.Count} · 개조 {m.Upgrades.Count}");
        }

        // ── 12) 결정론 ──
        if (Sec(12))
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint h1 = H(), h2 = H();
            Check("결정론 (같은 시드 → 같은 지문)", h1 == h2, $"{h1:X8} / {h2:X8}");
        }

        // ── 13) 성능: 30인 배 하루 ──
        if (Sec(13))
        {
            double Day(bool off)
            {
                ShipMate.Off = off;
                var w = World.CreateDefault(seed, 0, "Cheonma");
                Run(w, SimTime.Hours(2));
                var sw = Stopwatch.StartNew();
                Run(w, SimTime.TicksPerDay);
                ShipMate.Off = false;
                return sw.Elapsed.TotalSeconds;
            }
            double t0 = Math.Min(Day(true), Day(true)), t1 = Math.Min(Day(false), Day(false));
            Console.WriteLine($"  성능 (30인 하루): 끔 {t0:0.0}초 · 켬 {t1:0.0}초 ({(t1 / t0 - 1) * 100:+0;-0}%)");
            Check("성능 · 30인 배 하루가 ±10% 안", t1 < t0 * 1.1, $"{t0:0.0} → {t1:0.0}초");
        }

        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}건");
        return _fails == 0 ? 0 : 1;
    }
}
