using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.14 기술 트리를 그물처럼: 선행 · 갈림길(회의) · 열리는 조건 · 실험(사람이 한다) · 조합 · 부작용 · 새 시스템 줄기 · 컴퓨터 추천
public static partial class Program
{
    private static int RunTechWebTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"기술 그물 점검 (v16.14) · 시드 {seed}\n");
        try
        {
            static void Learn(World w, string id) { w.Eras.Known.Add(id); w.Eras.Order.Add(id); }
            static EraTech T(string id) => TechWeb.Find(id)!;
            var every = TechWeb.Every;

            // 0) 그물의 모양
            {
                bool allNodes = every.All(t => TechWeb.Nodes.ContainsKey(t.Id)) && TechWeb.Nodes.Count == every.Length;
                var badPre = every.Where(t => TechWeb.Node(t.Id).Pre.Any(p => TechWeb.Find(p) is not EraTech q || q.Era > t.Era || p == t.Id)).Select(t => t.Id).ToList();
                var reach = new HashSet<string>();
                for (bool grew = true; grew;)
                {
                    grew = false;
                    foreach (var t in every)
                        if (!reach.Contains(t.Id) && TechWeb.Node(t.Id).Pre.All(reach.Contains) && (TechWeb.Node(t.Id).Combo?.All(reach.Contains) ?? true)) { reach.Add(t.Id); grew = true; }
                }
                int withPre = every.Count(t => TechWeb.Node(t.Id).Pre.Length > 0);
                int cross = every.Count(t => TechWeb.Node(t.Id).Pre.Any(p => T(p).Field != t.Field));
                bool icons = every.Select(t => TechWeb.Node(t.Id).Icon).Distinct().Count() == every.Length;
                bool visuals = every.Select(TechWeb.Visual).Distinct().Count() == every.Length;
                Check("그물 — 시대 기술 70 + 새 41 · 선행은 같거나 앞 시대 · 고리 없이 모두 닿는다 · 아이콘과 배 모습 열쇠가 기술마다 다르다",
                    allNodes && EraSystem.All.Length == 70 && every.Length == 111 && badPre.Count == 0 && reach.Count == every.Length && icons && visuals && cross * 2 >= withPre,
                    $"마디 {TechWeb.Nodes.Count} · 선행 있는 기술 {withPre} (그중 분야를 넘는 것 {cross}) · 닿는 기술 {reach.Count}/{every.Length}" + (badPre.Count > 0 ? $" · 잘못 {string.Join(",", badPre)}" : ""));
                var gates = TechWeb.Nodes.Values.Where(n => n.Gate != null).Select(n => n.Gate!.Kind).Distinct().Count();
                int combos = TechWeb.Nodes.Values.Count(n => n.Combo != null);
                var lines = new[] { "body.", "cook.|food.", "portable.", "eva.", "exp.", "blast.", "cosmic.", "room." }
                    .Select(pf => TechWeb.Rows.Count(r => r.Fx.Any(f => pf.Split('|').Any(p => f.key.StartsWith(p))))).ToList();
                int computer = TechWeb.Rows.Count(r => r.Tech.Field == TechField.Computing && r.Tech.Id is "centralcpu" or "distctrl" or "watchdog");
                Check("숫자 — 갈림길 7쌍(≥6) · 조합 숨은 기술 10(≥8) · 열리는 조건 6가지 · 새 시스템마다 기술 줄기 2~3",
                    TechWeb.Forks.Length >= 6 && combos >= 8 && gates == 6 && lines.All(n => n >= 2) && computer == 3,
                    $"갈림길 {TechWeb.Forks.Length} · 조합 {combos} · 조건 종류 {gates} · 줄기 본체/음식/이동식/선외/원정/폭발/대재난/방 {string.Join("/", lines)} · 컴퓨터 {computer}");
            }

            // 1) 핵융합로: 세 분야를 거쳐야
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                w.Research = 140f; // 핵융합 시대
                var fc = T("fusioncore");
                var pre = TechWeb.Node("fusioncore").Pre.Select(T).ToList();
                bool AvailFc() => w.Eras.Available.Any(t => t.Id == "fusioncore");
                foreach (var id in new[] { "rcd", "weldcode", "vibelisten", "checklist" }) Learn(w, id);
                bool tier0 = TechWeb.TierOk(w, FurnitureType.ReactorCore, 3);
                int unlocked0 = Tech.Unlocked(w, FurnitureType.ReactorCore);
                var steps = new List<string>();
                foreach (var p in pre) { steps.Add($"{p.Name}({EraSystem.Fields(p.Field)}) 전 {(AvailFc() ? "열림" : "닫힘")}"); Learn(w, p.Id); }
                bool openAfter = AvailFc();
                bool driveLocked = !w.Eras.Available.Any(t => t.Id == "fusiondrive");
                Learn(w, "fusioncore");
                Learn(w, "rcsthruster");
                int unlocked1 = Tech.Unlocked(w, FurnitureType.ReactorCore);
                bool driveOpen = w.Eras.Available.Any(t => t.Id == "fusiondrive");
                Check("핵융합로 — 초전도 자석(동력) · 플라스마 진단(센서) · 방사선 차폐(선체)를 다 익혀야 열리고, 원자로 III 설계 · 핵융합 추진도 그 뒤에",
                    pre.Select(p => p.Field).Distinct().Count() == 3 && steps.All(s => s.EndsWith("닫힘")) && openAfter && driveLocked && !tier0 && unlocked0 == 2 && unlocked1 >= 3 && driveOpen,
                    string.Join(" → ", steps) + $" → 다 익힌 뒤 {(openAfter ? "열림" : "닫힘")} · 원자로 단계 {unlocked0} → {unlocked1} · {fc.Effect}");
            }

            // 2) 갈림길: 실제 회의가 고른다 · 조류를 고른 배는 화학 산소가 잠기고 비싸진다 · 약점이 거듭되면 다시 꺼낸다
            {
                var w = World.CreateDefault(seed, 0, "Mirinae");
                Run(w, SimTime.Hours(2));
                w.Research = 60f;
                foreach (var id in new[] { "amine", "seedbank" }) Learn(w, id);
                Run(w, SimTime.Hours(1) + 1); // 한 시간 훑기: 갈림길이 안건에 오른다
                var st = w.TechWeb.ForkStates["oxygen"];
                bool pending = st.PendingSince >= 0 && st.Side < 0;
                bool hidden = !w.Eras.Available.Where(t => !w.TechWeb.Undecided(t)).Any(t => t.Id is "chemox" or "algaeox"); // 정하기 전엔 회의가 연구로 고르지 않는다
                var attendees = w.Crew.Where(c => !c.Dead && c.CanAct && !c.IsChild).ToList();
                var rec = w.Meetings.Hold(MeetingKind.Regular, attendees, "식당");
                var item = rec.Items.FirstOrDefault(i => i.Topic == "techfork:oxygen");
                bool decided = st.Side >= 0 && item != null && item.Votes.Count >= 2 && item.Speeches.Count > 0;
                Check("갈림길 회의 — 산소: 화학 산소냐 조류 광합성이냐를 회의가 토론 · 표결로 정하고 배의 이름이 붙는다",
                    pending && hidden && decided && w.TechWeb.Epithets.Count == 1,
                    $"안건 {(pending ? "올라옴" : "없음")} · {item?.Title} · 찬성 {item?.Yes} 반대 {item?.No} · 발언: {string.Join(" / ", item?.Speeches.Take(3).Select(s => $"{w.Crew[s.Who].Name}: {s.Text}") ?? Array.Empty<string>())}"
                    + $" · 컴퓨터: {(item?.Computer ?? "없음")} → {w.TechWeb.Identity} ({st.Why})");

                var w2 = World.CreateDefault(seed, 0, "Hanbit");
                w2.Research = 60f;
                foreach (var id in new[] { "amine", "seedbank" }) Learn(w2, id);
                w2.TechWeb.Scan();
                w2.TechWeb.Decide(TechWeb.Forks.First(f => f.Id == "oxygen"), 1, "시험 — 조류를 골랐다", "시험", 3, 1);
                var chem = T("chemox");
                bool locked = !w2.Eras.Available.Any(t => t.Id == "chemox") && w2.TechWeb.Locked(chem) && w2.Eras.Available.Any(t => t.Id == "algaeox" || !w2.TechWeb.GateMet(T("algaeox")));
                float cost = w2.TechWeb.CostOf(chem);
                bool tierOk = TechWeb.TierOk(w2, FurnitureType.OxygenGenerator, 3);
                w2.Hazards.Count[(int)HazardKind.WaterContamination] += 1;
                w2.Hazards.Count[(int)HazardKind.MoldOutbreak] += 1;
                Run(w2, SimTime.Hours(1) + 1);
                var st2 = w2.TechWeb.ForkStates["oxygen"];
                bool reopened = st2.Reopened && w2.Eras.Available.Any(t => t.Id == "chemox") && Math.Abs(w2.TechWeb.CostOf(chem) - 2f * chem.Cost) < 0.01f;
                var w3 = World.CreateDefault(seed, 0, "Hanbit");
                w3.TechWeb.Decide(TechWeb.Forks.First(f => f.Id == "oxygen"), 0, "시험", "시험", 1, 0);
                bool chemShip = !TechWeb.TierOk(w3, FurnitureType.OxygenGenerator, 3) && w3.TechWeb.Locked(T("algaeox")) && w3.TechWeb.Locked(T("algaeox")) && !w3.Eras.Available.Any(t => t.Id == "algaebio");
                Check("갈림길 — 조류 광합성을 고른 배는 화학 산소가 잠기고(값 두 배), 조류의 약점(물 오염 · 곰팡이)이 거듭되자 다시 꺼낸다 · 화학 산소를 고른 배는 조류 광합성조(산소 III)가 막힌다",
                    locked && Math.Abs(cost - 2f * chem.Cost) < 0.01f && tierOk && reopened && chemShip && w2.TechWeb.Epithets.Contains("초록 숨의 배"),
                    $"화학 산소 잠김 {locked} · 값 {chem.Cost:0} → {cost:0} · 다시 꺼냄 {st2.Reopened} · 배 이름 '{w2.TechWeb.Identity}' · 화학 산소 배의 산소 III {(TechWeb.TierOk(w3, FurnitureType.OxygenGenerator, 3) ? "열림" : "막힘")}");
            }

            // 3) 원정 유물 역설계 · ④ 연구자가 실제로 실험한다
            {
                var w = DayOne(seed, "Hanbit");
                var wa = T("wreckalloy");
                bool hidden0 = !w.TechWeb.Visible(wa) && !w.Eras.Available.Contains(wa);
                var spec = Props.Get("derelictplaque");
                var room = w.Ship.LiveRooms.Where(r => !r.Abandoned && Props.Fits(spec.Place, r)).OrderBy(r => r.Id).First();
                w.Props.Place(spec, room, null, "원정 옛 화물선에서 가져왔다", "옛 화물선 명판");
                w.TechWeb.Scan();
                string why = w.TechWeb.Why.GetValueOrDefault("wreckalloy") ?? "";
                bool shown = w.TechWeb.Visible(wa) && w.Eras.Available.Contains(wa) && why.Contains("명판");
                w.Eras.Begin("wreckalloy", "시험 — 유물이 궁금하다");
                var leads = new HashSet<string>();
                bool sawRelic = false;
                for (int h = 0; h < 72 && !w.Eras.Known.Contains("wreckalloy"); h++)
                {
                    Run(w, SimTime.Hours(1));
                    if (w.TechWeb.Trial is ExperimentState x)
                    {
                        sawRelic |= x.RelicSeen;
                        if (w.Crew[x.Lead].Job?.Activity is ResearchActivity) leads.Add($"{w.Crew[x.Lead].Name}({TechWebSystem.StyleName(x.Style)} · {x.LeadWhy})");
                    }
                }
                var s = w.TechWeb.Stats;
                bool learned = w.Eras.Known.Contains("wreckalloy");
                Check("원정 유물 역설계 — 명판을 배에 두자 숨은 기술이 보이고, 연구자가 유물을 살펴본 뒤 작업대에서 실험해 익힌다",
                    hidden0 && shown && learned && s.RelicStudies >= 1 && s.Experiments >= 1 && (sawRelic || s.RelicStudies >= 1) && leads.Count >= 1,
                    $"보임 {shown} ({why}) · 익힘 {learned} (D{w.Day}) · 실험 {s.Experiments}(성공 {s.Successes} · 실패 {s.Failures} · 돌파구 {s.Breakthroughs} · 사고 {s.Accidents}) · 유물 연구 {s.RelicStudies} · 연구자 {string.Join(", ", leads.Take(2))}"
                    + $" · 노트 {w.TechWeb.Notes.Count} · {w.TechWeb.Recent.LastOrDefault().text}");
            }

            // 4) 실험이 잘못되어 작은 사고 (분야마다 다르다 — 동력은 방전 폭발) · 컴퓨터가 읽는다 · 놀란 연구자는 신중해진다
            {
                var w = DayOne(seed, "Hanbit");
                w.Eras.Begin("rcd", "시험");
                int det0 = w.Blast.Stats.Detonations, acc0 = w.TechWeb.Stats.Accidents;
                int adv0 = w.Automation.Book.Acts.Count(a => a.Observe.StartsWith("실험 사고"));
                ExperimentState? trial = null;
                for (int i = 0; i < 48 && w.TechWeb.Stats.Accidents == acc0; i++)
                {
                    Run(w, SimTime.Minutes(30));
                    if (w.TechWeb.Trial is ExperimentState x && x.Tech == "rcd" && x.Force == null) { x.Force = "accident"; x.Style = ResearchStyle.Bold; trial = x; }
                }
                var s = w.TechWeb.Stats;
                var lead = trial != null ? w.Crew[trial.Lead] : null;
                bool hit = s.Accidents == acc0 + 1 && trial != null && w.Blast.Stats.Detonations > det0;
                bool hist = w.History.Events.Any(e => e.Text.StartsWith("실험 사고"));
                bool comp = w.Automation.Book.Acts.Count(a => a.Observe.StartsWith("실험 사고")) > adv0;
                bool alive = w.Crew.All(c => !c.Dead);
                bool shaken = lead != null && w.TechWeb.StyleOf(lead) == ResearchStyle.Cautious;
                bool noted = w.TechWeb.Notes.Any(n => n.Kind == 3) || w.TechWeb.Stats.NotesBurned > 0; // 사고 노트가 남거나 · 사고 불에 탔다
                bool fear = lead != null && w.TechWeb.Marks.Any(m => m.Kind == 0) && lead.Memory.Fear.Max() > 0f;
                Check("실험 사고 — 대담한 연구자의 동력 실험이 방전 폭발(작게)을 내고, 연대기 · 주 컴퓨터가 읽고, 연구자는 사흘 신중해진다 (아무도 죽지 않는다)",
                    hit && hist && comp && alive && shaken && noted && fear,
                    $"사고 {acc0} → {s.Accidents} · 폭발 {det0} → {w.Blast.Stats.Detonations} · {w.History.Events.LastOrDefault(e => e.Text.StartsWith("실험 사고"))?.Text}"
                    + $" · 컴퓨터: {w.Automation.Book.Acts.LastOrDefault(a => a.Observe.StartsWith("실험 사고"))?.Judge} · {lead?.Name} 다음 버릇 {(lead != null ? TechWebSystem.StyleName(w.TechWeb.StyleOf(lead)) : "?")}"
                    + (hit && hist && comp && alive && shaken && noted && fear ? "" : $" · [폭발 {hit} 연대기 {hist} 컴퓨터 {comp} 생존 {alive} 신중 {shaken} 노트 {noted} 두려움 {fear}]"));
            }

            // 5) 경보에 끊기고 노트를 펴고 이어 한다
            {
                var w = DayOne(seed, "Mirinae");
                w.Eras.Begin("checklist", "시험");
                ExperimentState? x = null;
                for (int i = 0; i < 96 && x == null; i++)
                {
                    Run(w, SimTime.Minutes(10));
                    if (w.TechWeb.Trial is ExperimentState y && y.Progress > 0.15f && w.Crew[y.Lead].Job?.Activity is ResearchActivity) x = y;
                }
                bool paused = false;
                if (x != null)
                {
                    var lead = w.Crew[x.Lead];
                    var far = w.Ship.LiveRooms.Where(r => r != lead.Room && !r.Abandoned && r.Type != RoomType.Corridor).OrderByDescending(r => (r.Center - lead.Position).LengthSquared()).First();
                    Player.Fire(w, far.Cells[far.Cells.Count / 2]);
                    for (int i = 0; i < 24 && !(paused = x.Paused); i++) Run(w, SimTime.Minutes(5));
                    for (int i = 0; i < 72 && w.TechWeb.Stats.Resumed == 0 && w.TechWeb.Trial == x; i++) Run(w, SimTime.Minutes(20));
                }
                var s = w.TechWeb.Stats;
                Check("중단과 재개 — 불 경보에 실험이 끊겨 작업대에 노트가 남고, 다시 와서 노트를 펴고 이어 한다",
                    x != null && paused && s.Interrupts >= 1 && s.Resumed >= 1 && s.Notes >= 1,
                    $"끊김 {s.Interrupts} · 재개 {s.Resumed} · 노트 {s.Notes}(남의 노트 읽음 {s.NotesRead}) · {string.Join(" / ", w.TechWeb.Recent.Where(r => r.text.Contains("끊겼") || r.text.Contains("재개")).Take(2).Select(r => r.text))}");
            }

            // 6) 조합 숨은 기술: A + B를 익히면 C가 보인다
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                var bl = T("biolamp");
                bool h0 = !w.TechWeb.Visible(bl);
                Learn(w, "biofilter");
                w.TechWeb.Scan();
                bool h1 = !w.TechWeb.Visible(bl);
                Learn(w, "algaeox");
                w.TechWeb.Scan();
                bool shown = w.TechWeb.Visible(bl) && w.TechWeb.Why["biolamp"].Contains("생물 여과") && w.TechWeb.Why["biolamp"].Contains("조류 광합성");
                var w2 = World.CreateDefault(seed, 0, "Hanbit");
                foreach (var n in TechWeb.Nodes.Values.Where(n => n.Combo != null)) foreach (var p in n.Combo!) if (!w2.Eras.Known.Contains(p)) Learn(w2, p);
                w2.TechWeb.Scan();
                int all = TechWeb.Nodes.Values.Count(n => n.Combo != null && w2.TechWeb.Visible(T(n.Id)));
                Check("조합 숨은 기술 — 생물 여과만으로는 안 보이고, 조류 광합성까지 익히자 생물 발광 조명이 보인다 (조합 10개 모두 같은 규칙)",
                    h0 && h1 && shown && all == TechWeb.Nodes.Values.Count(n => n.Combo != null) && w2.TechWeb.Stats.Combos == all,
                    $"{bl.Name}: {w.TechWeb.Why.GetValueOrDefault("biolamp")} · 조합 {all}개 드러남");
            }

            // 7) 주 컴퓨터 추천: 이 배의 기록을 근거로 · 회의가 믿음만큼 따른다
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                Run(w, SimTime.Hours(1));
                w.History.Fires += 3;
                w.Hazards.Count[(int)HazardKind.GroundFault] += 2;
                var options = w.Eras.Available.Where(t => !w.TechWeb.Undecided(t)).ToList();
                string? rec = w.TechWeb.Advise(options, _ => 0f);
                var act = w.Automation.Book.Acts.LastOrDefault(a => a.Observe.StartsWith("연구 후보"));
                bool why = rec != null && w.TechWeb.RecWhy.Length > 0 && (w.TechWeb.RecWhy.Contains("불") || w.TechWeb.RecWhy.Contains("누전"));
                foreach (var c in w.Crew) w.Automation.Trusts.Change(c, 0.4f, "시험", quiet: true);
                w.Eras.Begin(null!, ""); // 다음 틱에 회의가 고른다
                Run(w, SimTime.Minutes(10));
                var pickLine = w.History.Events.LastOrDefault(e => e.Text.Contains("다음 연구는"))?.Text ?? "";
                Check("컴퓨터 추천 — 불 3번 · 누전 2번을 겪은 배에 근거를 들어 다음 연구를 권하고, 컴퓨터를 믿는 회의는 그대로 고른다",
                    why && act != null && pickLine.Contains("컴퓨터 추천대로") && w.TechWeb.Stats.Followed >= 1,
                    $"추천 {TechWeb.Find(rec)?.Name}: {w.TechWeb.RecWhy} · 연대기: {pickLine}");
            }

            // 8) 부작용 연쇄: 서툰 이틀 · 늘어난 설비의 새 위험 · 새 관행 (주 컴퓨터가 예보한다)
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                Run(w, SimTime.Hours(1));
                foreach (var id in new[] { "rcd", "checklist", "vibelisten", "smartgrid" }) Learn(w, id);
                w.Eras.Begin("fibernet", "시험");
                w.Eras.Boost(500f);
                float r0 = w.Eras.RiskMul(nameof(HazardKind.CableTrayFire));
                Run(w, SimTime.Minutes(10));
                float fresh = w.Eras.RiskMul(nameof(HazardKind.CableTrayFire)) / r0;
                Run(w, SimTime.Hours(17));
                bool custom = w.Culture.Of(CustomKind.FireCheck) != null;
                float later = w.Eras.RiskMul(nameof(HazardKind.CableTrayFire)) / r0;
                bool forecast = w.Automation.Book.Acts.Any(a => a.Key.StartsWith("techchain:fibernet"));
                Run(w, SimTime.TicksPerDay * 2);
                float settled = w.Eras.RiskMul(nameof(HazardKind.CableTrayFire)) / r0;
                Check("부작용 연쇄 — 광섬유 감지망: 갓 익힌 이틀은 서툴러 케이블 화재가 더 잦고, 케이블이 는 뒤로 소화기 자리를 챙기는 관행이 생기고 · 컴퓨터가 예보한다",
                    w.Eras.Known.Contains("fibernet") && fresh > 1.2f * 1.2f - 0.01f && custom && forecast && later > settled && settled > 1.2f,
                    $"케이블 화재 무게 ×{fresh:0.00}(익힌 날) → ×{later:0.00}(17시간) → ×{settled:0.00}(사흘) · 관행 '{(custom ? CultureSystem.Name(CustomKind.FireCheck) : "없음")}' · {w.TechWeb.Recent.LastOrDefault(r => r.text.StartsWith("부작용")).text}");
            }

            // 9) 새 시스템 줄기: 그 시스템이 읽는 배율이 실제로 바뀐다
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                var keys = TechWeb.Keys.Where(k => !k.StartsWith("lab.")).ToList();
                bool ones = keys.All(k => TechWeb.Mul(w, k) == 1f);
                var cell = w.Ship.Rooms.SelectMany(r => r.Cells).First(c => w.Body.FloorAt(c) != Material.None);
                float spread0 = w.Body.SpreadMul(cell);
                var shelter = w.Ship.LiveRooms.OrderByDescending(r => w.Cosmic.RelExposure(r)).First();
                float expo0 = w.Cosmic.RelExposure(shelter);
                foreach (var r in TechWeb.Rows.Where(r => r.Fx.Any(f => keys.Contains(f.key)))) Learn(w, r.Tech.Id);
                var wrong = keys.Where(k =>
                {
                    float expect = 1f;
                    foreach (var r in TechWeb.Rows) foreach (var f in r.Fx) if (f.key == k) expect *= f.mul;
                    return MathF.Abs(TechWeb.Mul(w, k) - expect) > 1e-4f;
                }).ToList();
                float spread1 = w.Body.SpreadMul(cell), expo1 = w.Cosmic.RelExposure(shelter);
                Check("새 시스템 줄기 — 배 본체 · 음식 · 이동식 장비 · 선외 · 원정 · 폭발 · 대재난 · 방 공사가 읽는 배율 (익히기 전 ×1)",
                    ones && wrong.Count == 0 && spread1 < spread0 && (expo0 <= 0f || expo1 < expo0),
                    string.Join(" · ", keys.Select(k => $"{TechWeb.KeyName(k)} ×{TechWeb.Mul(w, k):0.##}")) + $" · 바닥 불 번짐 {spread0:0.00} → {spread1:0.00} · {shelter.Name} 노출 {expo0:0.00} → {expo1:0.00}"
                    + (wrong.Count > 0 ? $" · 틀림 {string.Join(",", wrong)}" : ""));
            }

            // 10) 긴 흐름: 이틀 — 회의가 고르고 · 사람이 실험하고 · 그물이 열린다
            {
                var w = World.CreateDefault(seed, 0, "Mirinae");
                Run(w, SimTime.TicksPerDay * 2);
                var s = w.TechWeb.Stats;
                var tops = w.TechWeb.Experiments.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{w.Crew[kv.Key].Name} {kv.Value}").ToList();
                Check("이틀 — 회의가 연구를 고르고(컴퓨터 추천) 사람들이 실제로 실험한다",
                    s.Advices >= 1 && s.Experiments >= 2 && w.Eras.Known.Count >= 1,
                    $"익힌 기술 {w.Eras.Known.Count}: {string.Join(", ", w.Eras.Order.Select(id => TechWeb.Find(id)!.Name))} · 실험한 사람 {string.Join(", ", tops)} · {s}");
            }

            // 11) 결정론
            {
                uint H()
                {
                    var w = World.CreateDefault(seed, 0, "Hanbit");
                    Run(w, SimTime.TicksPerDay + SimTime.Hours(6));
                    return SaveGame.StateHash(w);
                }
                uint a = H(), b = H();
                Check("결정론 — 같은 시드 같은 지문", a == b, $"{a:x8} / {b:x8}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"예외: {ex}");
            _fails++;
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 기술 그물 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
