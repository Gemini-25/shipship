using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.18 사고 · 재난 다섯 규모: 표 · 번지며 오르는 규모 · 규모별 대응 · 컴퓨터 판정 · 이야기꾼 완급 · 연쇄 표시 · 도감
public static partial class Program
{
    private static int RunScaleTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"사고 규모 점검 (v16.18) · 시드 {seed}\n");

        // ── 1) 표 하나: 모든 사고 종류가 규모를 가진다 (빠짐 없음) ──
        {
            var bad = ScaleTable.Unclassified();
            int H(IEnumerable<string> keys) => keys.Count(k => ScaleTable.Row(k) != null);
            int hz = H(Enum.GetValues<HazardKind>().Select(k => k.ToString())), hzN = Enum.GetValues<HazardKind>().Length;
            int cos = CosmicCatalog.All.Count(s => ScaleTable.Row("cosmic:" + s.Id)?.Base == IncidentScale.Cosmic);
            int cause = H(Enum.GetValues<CauseKind>().Select(k => "cause:" + k)), causeN = Enum.GetValues<CauseKind>().Length;
            int fault = H(Enum.GetValues<FaultKind>().Select(k => "fault:" + k)), faultN = Enum.GetValues<FaultKind>().Length;
            int blow = H(Enum.GetValues<BlowKind>().Where(k => k != BlowKind.None).Select(k => "blow:" + k)), blowN = Enum.GetValues<BlowKind>().Length - 1;
            int blast = H(Enum.GetValues<BlastKind>().Select(k => "blast:" + k)), blastN = Enum.GetValues<BlastKind>().Length;
            int ail = H(AilmentSystem.All.Select(a => "ail:" + a.Id)), ailN = AilmentSystem.All.Length;
            int wound = H(Enum.GetValues<WoundKind>().Select(k => "wound:" + k)), woundN = Enum.GetValues<WoundKind>().Length;
            int eva = H(Enum.GetValues<SuitBreach>().Where(b => b != SuitBreach.None).Select(b => "eva:" + b)), evaN = Enum.GetValues<SuitBreach>().Length - 1;
            int story = H(new[] { "meteor", "bigmeteor", "fire", "break", "pipe", "unrest" });
            var groups = ScaleTable.Scales.Select(s => ScaleTable.Group(s).Count()).ToArray();
            bool ok = bad.Count == 0 && hz == hzN && cos == CosmicCatalog.All.Length && cause == causeN && fault == faultN && blow == blowN && blast == blastN
                      && ail == ailN && wound == woundN && eva == evaN && story == 6 && groups.All(g => g > 0)
                      && ScaleTable.Row("bigmeteor")!.Base == IncidentScale.Ship && ScaleTable.Row("cause:Outage")!.Base == IncidentScale.System
                      && ScaleTable.Row("wound:slip")!.Base == IncidentScale.Personal && ScaleTable.Row("cause:Scram")!.Base == IncidentScale.Ship;
            Check("모든 사고 종류가 규모를 가진다 (빠짐 없음)", ok,
                $"표 {ScaleTable.All.Length}줄 · 사고 {hz}/{hzN} · 우주급 {cos}/{CosmicCatalog.All.Length} · 사슬 {cause}/{causeN} · 고장 {fault}/{faultN} · 과열 {blow}/{blowN} · 폭발 {blast}/{blastN} · 질병 {ail}/{ailN} · 부상 {wound}/{woundN} · 선외 {eva}/{evaN} · 이야기꾼 {story}/6 · 규모별 {string.Join("/", groups)}"
                + (bad.Count > 0 ? $" · 빠짐/겹침 {string.Join(",", bad)}" : ""));
        }

        // ── 2) 작은 누수(②) → 정전(③) → 원자로 정지(④): 규모가 오르고 · 대응 인원이 단계마다 늘고 · 방송 문구가 규모에 맞다 ──
        {
            var w = DayOne(seed, "Hanbit");
            var power = w.Ship.FurnitureOf(FurnitureType.PowerPanel).First().Room;
            var cooling = w.Ship.FurnitureOf(FurnitureType.CoolantPump).Select(f => f.Room).Distinct().ToList();
            var texts = new string[5];
            var calledAt = new int[5];
            NetLink? PickLink(Func<List<Room>, bool> ok)
            {
                w.Net.EnsureBuilt();
                foreach (var l in w.Net.Links.Where(l => l.Kind == NetKind.Power && !l.Cut).OrderBy(l => l.Id))
                {
                    float keep = l.Integrity;
                    l.Integrity = 0.1f;
                    var down = w.Net.Downstream(l);
                    l.Integrity = keep;
                    if (ok(down)) return l;
                }
                return null;
            }
            // ② 배전실 바닥에 물이 조금 샌다
            w.Moisture.AddWater(power, power.Cells.Count * 20f * 0.2f);
            ScaleCase? k = null;
            for (int i = 0; i < 30 && k == null; i++) { Run(w, SimTime.Minutes(1)); k = w.Scale.OpenCases.FirstOrDefault(x => x.Key == "cause:Flood" && x.RoomId == power.Id); }
            if (k == null) { Check("누수 → 사건", false, "침수 사건이 잡히지 않았다"); return 1; }
            int floodNode = k.Root;
            Run(w, SimTime.Minutes(12));
            var s2 = k.Now;
            texts[(int)k.Now] = k.Broadcast; calledAt[(int)k.Now] = k.Called;
            Console.WriteLine($"  ② {SimTime.Clock(w.Tick)} {k.Name} — {ScaleTable.Label(k.Now)} · 부름 {k.Called} · 붙음 {k.StageResponders[(int)k.Now]} · 방송 \"{k.Broadcast}\"");
            // ③ 물이 간선 접속함에 스며 누전 → 여러 방 정전
            // 주 컴퓨터 방은 살려 둔다 (컴퓨터가 멎으면 사람이 판정한다 — 그건 아래 따로 본다)
            var compRoom = w.Automation.ComputerBody?.Room;
            var link1 = PickLink(d => d.Count >= 2 && !d.Any(r => cooling.Contains(r) || r.Type == RoomType.Reactor || r == compRoom));
            using (w.Causes.Because(floodNode))
            {
                if (link1 != null) w.Net.Hurt(link1, 1f, "물이 간선 접속함에 스며 누전");
                else
                {
                    // 물이 배전반 단자에 닿아 한 회로가 단락 (냉각 · 주 컴퓨터가 없는 회로 가운데 방이 가장 많은 것)
                    var panel = w.Ship.FurnitureOf(FurnitureType.PowerPanel).First().Machine!;
                    int circ = Enumerable.Range(1, PowerGrid.CircuitCount - 1)
                        .Where(i => i != (compRoom?.Circuit ?? -1) && !cooling.Any(r => r.Circuit == i))
                        .OrderByDescending(i => w.Ship.Rooms.Count(r => !r.Detached && r.Circuit == i)).ThenBy(i => i).First();
                    var f = new Fault { Kind = FaultKind.ShortCircuit, Since = w.Tick, Circuit = circ };
                    panel.Faults.Add(f);
                    w.Causes.OnFault(panel, f);
                    w.RaiseAlert($"{PowerGrid.CircuitName(circ)} 회로 단락 — 배전반에 물이 닿았다", power, AlertLevel.Critical, shipWide: true);
                }
            }
            for (int i = 0; i < 15 && k.Now < IncidentScale.System; i++) Run(w, SimTime.Minutes(1));
            var s3 = k.Now;
            texts[(int)IncidentScale.System] = k.Now == IncidentScale.System ? k.Broadcast : texts[(int)IncidentScale.System]; calledAt[(int)IncidentScale.System] = k.StageCalled[(int)IncidentScale.System];
            Run(w, SimTime.Minutes(10));
            Console.WriteLine($"  ③ {SimTime.Clock(w.Tick)} {ScaleTable.Label(s3)} · 끊은 간선 {(link1 != null ? link1.Room.Name : "없음 → 배전반 단락")} · 부름 {k.StageCalled[2]} · 붙음 {k.StageResponders[2]} · 방송 \"{k.Broadcast}\"");
            // ④ 정전이 냉각실로 번진다 → 냉각 펌프가 서고 원자로가 긴급 정지
            int outage = w.Causes.Nodes.Where(n => n.Incident == k.Root && n.Kind == CauseKind.Outage).Select(n => n.Id).DefaultIfEmpty(floodNode).Last();
            var link2 = PickLink(d => d.Any(r => cooling.Contains(r)) && !d.Contains(compRoom!)) ?? PickLink(d => d.Any(r => cooling.Contains(r)));
            using (w.Causes.Because(outage))
            {
                if (link2 != null) w.Net.Hurt(link2, 1f, "정전 뒤 과부하로 냉각실 간선 접속함이 탔다");
                else foreach (var p in w.Ship.FurnitureOf(FurnitureType.CoolantPump).ToList()) w.Machines.Break(p.Machine!, FaultKind.PumpSeized);
            }
            for (int i = 0; i < 30 && k.Now < IncidentScale.Ship; i++) Run(w, SimTime.Minutes(1));
            var s4 = k.Now;
            texts[(int)IncidentScale.Ship] = k.Now == IncidentScale.Ship ? k.Broadcast : ""; calledAt[(int)IncidentScale.Ship] = k.StageCalled[(int)IncidentScale.Ship];
            for (int dbg = 0; dbg < 4; dbg++)
            {
                Run(w, SimTime.Minutes(10));
                if (Environment.GetEnvironmentVariable("SCALE_DBG") == "1")
                    foreach (var c in w.Crew.Where(c => !c.Dead))
                    {
                        var ev = c.LastEvaluations.Take(3).Select(e => $"{e.Activity.Label} {e.Score:0.00}").ToList();
                        var mu = c.LastEvaluations.FirstOrDefault(e => e.Activity is MusterActivity);
                        Console.WriteLine($"     [{SimTime.Clock(w.Tick)}] {c.Name} {(c.IsAwake ? "깸" : "잠")} 일={c.Job?.Label ?? "-"} 방={c.Room?.Name} 일꾼={k.Workers.Contains(c.Id)} 들음={(k.BroadcastId >= 0 && w.Automation.Speak.Heard(c, k.BroadcastId))} 믿음={w.Automation.Trusts.Of(c):0.00} 소집={mu.Score:0.00}:{mu.Reason} | {string.Join(" / ", ev)}");
                    }
            }
            Console.WriteLine($"  ④ {SimTime.Clock(w.Tick)} {ScaleTable.Label(s4)} · 끊은 간선 {(link2 != null ? link2.Room.Name : "없음 → 펌프 고착")} · 원자로 {(w.Power.ReactorOnline ? "돎" : "멈춤")} · 부름 {k.StageCalled[3]} · 붙음 {k.StageResponders[3]} · 모임 {k.Mustered.Count} · 방송 \"{texts[3]}\"");
            Console.WriteLine($"     단계: {string.Join(" → ", k.Steps.Select(st => $"{SimTime.Clock(st.Tick)} {ScaleTable.Mark(st.To)} {st.Why}"))}");
            foreach (var line in w.Causes.Tree(w.Causes.IncidentOf(k.Root)!).Take(24))
                Console.WriteLine("     " + line);

            var order = k.Steps.Select(st => st.To).ToList();
            int iR = order.IndexOf(IncidentScale.Room), iS = order.IndexOf(IncidentScale.System), iSh = order.IndexOf(IncidentScale.Ship);
            Check("작은 누수(②)가 정전(③)으로, 정전이 원자로 정지(④)로 — 규모가 오른다", s2 == IncidentScale.Room && s3 == IncidentScale.System && s4 == IncidentScale.Ship && iR >= 0 && iR < iS && iS < iSh,
                $"② {ScaleTable.Name(s2)} → ③ {ScaleTable.Name(s3)} → ④ {ScaleTable.Name(s4)} · 단계 {string.Join(",", order.Select(ScaleTable.Mark))}");
            Check("대응 인원이 단계마다 는다 (부름 · 실제로 붙은 사람)", k.StageCalled[1] < k.StageCalled[2] && k.StageCalled[2] < k.StageCalled[3] && k.StageResponders[3] > k.StageResponders[1],
                $"부름 {k.StageCalled[1]} → {k.StageCalled[2]} → {k.StageCalled[3]} · 붙음 {k.StageResponders[1]} → {k.StageResponders[2]} → {k.StageResponders[3]}");
            bool texted = (texts[1] ?? "").Contains("방 규모") && (texts[2] ?? "").Contains("계통") && texts[3].Contains("배 전체") && texts[3].Contains("전원 소집")
                          && w.Automation.Speak.Recent.Any(b => b.Text.Contains("배 전체"));
            Check("컴퓨터 방송 문구가 규모에 맞다", texted, $"② \"{texts[1]}\" · ③ \"{texts[2]}\" · ④ \"{texts[3]}\"");
            int scram = w.Causes.Nodes.Where(n => n.Incident == k.Root && n.Kind == CauseKind.Scram).Select(n => n.Id).DefaultIfEmpty(-1).First();
            var acts = w.Automation.Book.Acts.Where(a => a.Key.StartsWith("scale:")).ToList();
            Check("주 컴퓨터가 규모를 판정 · 계획한다 (다섯 칸 기록 · 제안)", acts.Count >= 2 && k.JudgedBy == "주 컴퓨터" && k.Suggest.Length > 0 && acts.Any(a => a.Judge.Contains("규모")),
                $"기록 {acts.Count} · {string.Join(" / ", acts.Take(3).Select(a => $"{a.Judge} → {a.Act} · 요청 {a.Request}"))} · 판정 {k.JudgedBy} · 계획 \"{k.Plan}\"");
            Check("연쇄에 규모가 붙는다 (② 누수 → ③ 정전 → ④ 원자로 정지)",
                w.Scale.NodeScale(floodNode) == IncidentScale.Room && w.Scale.NodeScale(outage) == IncidentScale.System && scram >= 0 && w.Scale.NodeScale(scram) == IncidentScale.Ship,
                $"누수 {w.Scale.NodeScale(floodNode)} · 정전 {w.Scale.NodeScale(outage)} · 원자로 정지 {(scram >= 0 ? w.Scale.NodeScale(scram)?.ToString() : "없음")}");
            var halted = w.Crew.Where(c => c.LastEvaluations.Any(e => e.Reason.Contains("일상을 멈춘다"))).Select(c => c.Name).ToList();
            Check("승무원이 배 전체를 느낀다 — 모이고 · 두려워하고 · 일상을 멈춘다", k.Mustered.Count >= 2 && k.Feared.Count >= 2 && w.Scale.Halts > 0,
                $"점호 {k.Mustered.Count}명 · 두려움 {k.Feared.Count}명 · 일상 멈춤 {w.Scale.Halts}번 ({string.Join("·", halted.Take(4))}) · 모일 곳 {(k.MusterRoom >= 0 ? w.Ship.Rooms[k.MusterRoom].Name : "-")}");
            // ⑥ 도감
            bool codex = w.Scale.SeenOf("cause:Flood") >= 1 && w.Scale.SeenOf("cause:Outage") >= 1 && w.Scale.SeenOf("cause:Scram") >= 1 && w.Scale.Experienced(IncidentScale.Ship) >= 1
                         && ScaleTable.Scales.All(s => Codex.Incidents(s).Any());
            Check("도감에 규모별로 · 겪은 횟수", codex, string.Join(" · ", ScaleTable.Scales.Select(s => $"{ScaleTable.Mark(s)} {Codex.Incidents(s).Count()}종 · 겪음 {w.Scale.Experienced(s)}"))
                + $" · 침수 {w.Scale.SeenOf("cause:Flood")} · 정전 {w.Scale.SeenOf("cause:Outage")} · 원자로 정지 {w.Scale.SeenOf("cause:Scram")}");
        }

        // ── 3) 같은 불도 방 하나면 방, 번지면 계통 ──
        {
            var w = DayOne(seed, "Hanbit");
            var galley = w.Ship.RoomsOf(RoomType.Galley).First();
            var next = galley.Doors.Select(d => d.RoomA == galley ? d.RoomB : d.RoomA).FirstOrDefault(r => r != null && !r.Detached && r.Type != RoomType.Corridor)
                       ?? galley.Doors.Select(d => d.RoomA == galley ? d.RoomB : d.RoomA).First(r => r != null)!;
            Incidents.Fire(w, galley.Cells.First(w.Ship.IsOpenFloor));
            Run(w, SimTime.Minutes(3));
            var k = w.Scale.OpenCases.FirstOrDefault(x => x.Key == "cause:Fire");
            var one = k?.Now;
            if (k != null) using (w.Causes.Because(k.Root)) w.Fire.Ignite(next.Cells.First(w.Ship.IsOpenFloor), 0.35f);
            Run(w, SimTime.Minutes(3));
            Check("같은 불도 방 하나면 방, 번지면 계통", one == IncidentScale.Room && k!.Peak == IncidentScale.System,
                $"{galley.Name} 불 {one} → {next.Name}까지 {k?.Peak} · 방송 \"{k?.Broadcast}\"");
        }

        // ── 3b) 주 컴퓨터가 멎었으면 사람이 판정한다 (늦게 · 외쳐서) ──
        {
            var w = DayOne(seed, "Hanbit");
            Hazards.Apply(w, HazardKind.ComputerFault, default, -1);
            Run(w, SimTime.Minutes(2));
            bool down = !w.Automation.MainOnline;
            var galley = w.Ship.RoomsOf(RoomType.Galley).First();
            var next = galley.Doors.Select(d => d.RoomA == galley ? d.RoomB : d.RoomA).First(r => r != null)!;
            Incidents.Fire(w, galley.Cells.First(w.Ship.IsOpenFloor));
            Run(w, SimTime.Minutes(2));
            var k = w.Scale.OpenCases.FirstOrDefault(x => x.Key == "cause:Fire");
            if (k != null) using (w.Causes.Because(k.Root)) w.Fire.Ignite(next.Cells.First(w.Ship.IsOpenFloor), 0.35f);
            Run(w, SimTime.Minutes(3));
            string early = k?.JudgedBy ?? "";
            Run(w, SimTime.Minutes(5));
            Check("주 컴퓨터가 멎으면 사람이 늦게 판정한다", down && k != null && k.Peak >= IncidentScale.System && early == "" && k.JudgedBy.Length > 0 && k.JudgedBy != "주 컴퓨터" && k.ShoutAt >= 0 && w.Scale.HumanJudged > 0,
                $"컴퓨터 {(down ? "멎음" : "돎")} · {k?.Name} {k?.Peak} · 3분 뒤 판정 \"{early}\" → 8분 뒤 \"{k?.JudgedBy}\" · {k?.Plan}");
        }

        // ── 4) 개인 사고: 혼자 · 곁의 사람 ──
        {
            var w = DayOne(seed, "Hanbit");
            var c = w.Crew.Where(x => x.CanAct && !x.IsChild && x.Room != null).OrderByDescending(x => w.Crew.Count(y => y != x && y.Room == x.Room)).ThenBy(x => x.Id).First();
            NeedsSystem.AddInjury(c.Vitals, 0.12f, "젖은 바닥에 미끄러져 다침");
            Run(w, SimTime.Minutes(3));
            var k = w.Scale.Cases.LastOrDefault(x => x.CrewId == c.Id);
            Check("개인 사고 — 혼자 · 곁의 사람 (방송 없음)", k != null && k.Key == "wound:slip" && k.Peak == IncidentScale.Personal && k.Workers.Count <= 1 && k.BroadcastId < 0,
                k == null ? "사건 없음" : $"{k.Name} · {ScaleTable.Label(k.Peak)} · 곁의 사람 {string.Join("·", k.Workers.Select(id => w.Crew[id].Name))} · \"{k.Broadcast}\"");
        }

        // ── 5) 이야기꾼이 큰 사고 뒤 쉬어 간다 ──
        {
            float pv = Storyteller.PersonaValue, lv = Storyteller.LevelValue;
            Storyteller.PersonaValue = (float)StoryPersona.Random; Storyteller.LevelValue = 3;
            var w = DayOne(seed, "Mirinae");
            int Bigs(int n) { int b = 0; for (int i = 0; i < n; i++) if (ScaleTable.OfKey(w.Story.Sample(StoryPersona.Random, 0f, 1f).key) >= IncidentScale.System) b++; return b; }
            int before = Bigs(300);
            float bigMul0 = w.Scale.PaceMul(nameof(HazardKind.CoolantLoss)), smallMul0 = w.Scale.PaceMul("fire");
            Hazards.Apply(w, HazardKind.CoolantLoss, default, -1);
            Run(w, SimTime.Minutes(5));
            bool breather = w.Scale.Breather(StoryPersona.Random, out var why);
            int after = Bigs(300);
            float bigMul = w.Scale.PaceMul(nameof(HazardKind.CoolantLoss)), smallMul = w.Scale.PaceMul("fire");
            // 이야기꾼 차례를 지금으로 당긴다 (시험): 큰 사고가 뜨거우면 쉬어 간다
            var nextProp = typeof(Storyteller).GetProperty("Next")!;
            int firedHot = w.Story.Fired;
            nextProp.SetValue(w.Story, w.Tick);
            Run(w, SimTime.Minutes(2));
            bool heldHot = w.Story.Fired == firedHot && w.Story.LastWhy.Contains("숨 돌릴");
            string hotWhy = w.Story.LastWhy;
            long t0 = w.Tick;
            int fired0 = w.Story.Fired;
            string? rested = null;
            long calmAt = -1;
            for (int h = 0; h < 72; h++)
            {
                Run(w, SimTime.Hours(1));
                if (w.Story.LastWhy.Contains("숨 돌릴")) rested ??= $"{SimTime.Day(w.Tick)}일 {SimTime.Clock(w.Tick)} {w.Story.LastWhy}";
                if (calmAt < 0 && w.Scale.LastBigEnd >= 0) { calmAt = w.Scale.LastBigEnd; nextProp.SetValue(w.Story, w.Tick); } // 가라앉자마자 차례가 와도 쉬어 간다
            }
            long restEnd = calmAt >= 0 ? calmAt + SimTime.Hours(ScaleSystem.RestHours(StoryPersona.Random, 3)) : w.Tick;
            int inRest = w.Story.Journal.Count(j => j.tick > t0 && j.tick < restEnd);
            Check("이야기꾼이 큰 사고 뒤 쉬어 간다 (그 뒤엔 작은 것부터)", breather && heldHot && after < before && bigMul < 0.2f * bigMul0 && smallMul / bigMul > smallMul0 / bigMul0 && calmAt >= 0 && inRest == 0 && w.Story.Fired > fired0,
                $"큰 것 고를 몫 {before}/300 → {after}/300 · 무게 냉각 상실 ×{bigMul0:0.##}→×{bigMul:0.##} · 작은 불 ×{smallMul0:0.##}→×{smallMul:0.##} · 차례에 \"{hotWhy}\" · 기다림 {w.Scale.Rests}번 ({rested ?? "-"}) · 가라앉음 {(calmAt >= 0 ? SimTime.Clock(calmAt) : "아직")} · 쉬는 동안 낸 사고 {inRest} · 사흘 동안 {w.Story.Fired - fired0}");
            Storyteller.PersonaValue = pv; Storyteller.LevelValue = lv;
        }

        // ── 6) 우주급: 대피 · 항로 변경 · 일상 중단 ──
        {
            var w = DayOne(seed, "Hanbit");
            var e = w.Cosmic.Force(CosmicKind.BigAsteroid, 3f, close: true);
            ScaleCase? k = null;
            for (int i = 0; i < 6 * 60 && (k == null || !k.Hot); i++) { Run(w, SimTime.Minutes(1)); k = w.Scale.OpenCases.FirstOrDefault(x => x.CosmicId == e.Id); }
            Run(w, SimTime.Minutes(20));
            Check("우주급 — 판정 · 대피 · 항로 변경 · 일상 중단", k != null && k.Now == IncidentScale.Cosmic && k.Broadcast.Contains("우주급") && (k.Suggest.Contains("항로") || k.Suggest.Contains("대피")) && k.Feared.Count > 0,
                k == null ? "사건 없음" : $"{k.Name} · {e.PhaseName} · \"{k.Broadcast}\" · 제안 {k.Suggest} · 두려움 {k.Feared.Count} · 일상 멈춤 {w.Scale.Halts}");
        }

        // ── 7) 결정론 ──
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint a = H(), b = H();
            uint F() { var w = DayOne(seed, "Hanbit"); var p = w.Ship.FurnitureOf(FurnitureType.PowerPanel).First().Room; w.Moisture.AddWater(p, p.Cells.Count * 8f); Hazards.Apply(w, HazardKind.CoolantLoss, default, -1); Run(w, SimTime.Hours(4)); return SaveGame.StateHash(w); }
            uint c = F(), d = F();
            Check("결정론 (평소 · 큰 사고)", a == b && c == d, $"지문 {a:x8}/{b:x8} · {c:x8}/{d:x8}");
        }

        Console.WriteLine(_fails == 0 ? "\n✔ 사고 규모 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
