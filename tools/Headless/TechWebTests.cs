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
                    allNodes && EraSystem.All.Length == 70 && every.Length == 142 && badPre.Count == 0 && reach.Count == every.Length && icons && visuals && cross * 2 >= withPre,
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

            // 1) 핵융합로 (v23 새 기술표): Ⅲ 시대 전엔 연구할 수 없고, 소형 핵융합로를 익혀야 원자로 III 설계가 생긴다
            {
                bool tiersWas = ShipTiers.On;
                ShipTiers.On = false;
                var w = World.CreateDefault(seed, 0, "Hanbit");
                ShipTiers.On = tiersWas;
                bool AvailP3() => w.Eras.Available.Any(t => t.Id == "p3");
                int unlocked0 = Tech.Unlocked(w, FurnitureType.ReactorCore);
                bool closed0 = !AvailP3() && w.Eras.Era == 1;
                foreach (var t in TechTree.Main.Where(t => t.Era == 1)) w.Eras.LearnNow(t.Id);
                int era2 = w.Eras.Era;
                w.Eras.LearnNow("p2");
                int unlocked1 = Tech.Unlocked(w, FurnitureType.ReactorCore);
                bool closed1 = !AvailP3();
                foreach (var t in TechTree.Main.Where(t => t.Era == 2 && !w.Eras.Known.Contains(t.Id))) w.Eras.LearnNow(t.Id);
                bool openAfter = AvailP3() && w.Eras.Era == 3;
                bool tier0 = TechWeb.TierOk(w, FurnitureType.ReactorCore, 3);
                w.Eras.LearnNow("p3");
                int unlocked2 = Tech.Unlocked(w, FurnitureType.ReactorCore);
                var card = w.Eras.Unlocks.LastOrDefault(u => u.Id == "p3");
                Check("핵융합로 — Ⅲ 시대가 열리기 전엔 연구할 수 없고, 개량 핵분열로 → 소형 핵융합로를 익혀야 원자로 단계가 1 → 2 → 3 (공사 기다림이 카드에)",
                    closed0 && era2 == 2 && closed1 && openAfter && !tier0 && unlocked0 == 1 && unlocked1 == 2 && unlocked2 == 3 && w.Eras.Known.Contains("fusioncore") && card != null && card.Installs.Length >= 1,
                    $"시대 1 → {era2} → {w.Eras.Era} · 원자로 단계 {unlocked0} → {unlocked1} → {unlocked2} · 카드: {string.Join(" / ", card?.Lines ?? Array.Empty<string>())} · 설치: {string.Join(" / ", card?.Installs ?? Array.Empty<string>())}");
            }

            // 2) 갈림길 (v23): 그 기술을 연구할 때 배의 형편으로 기울고(이유가 보인다), 함장이 뒤집을 수 있고, 익히면 배의 이름이 붙는다 · 화학 산소 배는 산소 III이 막힌다
            {
                bool tiersWas = ShipTiers.On;
                ShipTiers.On = false;
                var w = World.CreateDefault(seed, 0, "Mirinae");
                ShipTiers.On = tiersWas;
                foreach (var t in TechTree.Main.Where(t => t.Era == 1)) w.Eras.LearnNow(t.Id);
                w.Eras.Begin("l2", "시험");
                bool leaned = w.Eras.Picks.TryGetValue("oxygen", out var side0) && w.Eras.PickWhy["oxygen"].Length > 0;
                string why0 = w.Eras.PickWhy.GetValueOrDefault("oxygen") ?? "";
                bool flipped = w.Eras.FlipFork("oxygen") && w.Eras.Picks["oxygen"] == 1 - side0 && w.Eras.PickWhy["oxygen"] == "함장이 골랐다";
                if (w.Eras.Picks["oxygen"] != 0) w.Eras.FlipFork("oxygen"); // 화학 산소 쪽으로
                w.Eras.LearnNow("l2");
                var st = w.TechWeb.ForkStates["oxygen"];
                bool chem = w.Eras.Known.Contains("chemox") && !w.Eras.Known.Contains("algaeox") && st.Side == 0;
                bool noFlip = !w.Eras.FlipFork("oxygen");
                bool tierBlocked = !TechWeb.TierOk(w, FurnitureType.OxygenGenerator, 3);
                foreach (var t in TechTree.Main.Where(t => t.Era == 2 && !w.Eras.Known.Contains(t.Id))) w.Eras.LearnNow(t.Id);
                w.Eras.LearnNow("l3");
                int o2 = Tech.Unlocked(w, FurnitureType.OxygenGenerator);
                Check("갈림길 — 산소: 연구를 시작할 때 이 배의 형편으로 기울고(이유), 함장이 뒤집고, 익히면 배의 이름 · 화학 산소 배는 생물 여과를 익혀도 산소 발생기가 II에 머문다",
                    leaned && flipped && chem && noFlip && tierBlocked && o2 == 2 && w.TechWeb.Epithets.Contains("약품 산소의 배"),
                    $"처음 기운 쪽 {(side0 == 0 ? "화학 산소" : "조류 광합성")} ({why0}) · 뒤집음 {flipped} · 익힌 뒤 뒤집기 막힘 {noFlip} · 배 이름 '{w.TechWeb.Identity}' · 산소 발생기 단계 {o2}");
            }

            // 3) 원정 유물 역설계 · ④ 연구자가 실제로 실험한다
            {
                var w = DayOne(seed, "Hanbit");
                var wa = T("wreckalloy");
                bool hidden0 = !w.TechWeb.Visible(wa) && !w.Eras.Available.Any(t => t.Absorbs.Contains("wreckalloy"));
                var spec = Props.Get("derelictplaque");
                var room = w.Ship.LiveRooms.Where(r => !r.Abandoned && Props.Fits(spec.Place, r)).OrderBy(r => r.Id).First();
                w.Props.Place(spec, room, null, "원정 옛 화물선에서 가져왔다", "옛 화물선 명판");
                w.TechWeb.Scan();
                string why = w.TechWeb.Why.GetValueOrDefault("wreckalloy") ?? "";
                bool shown = w.TechWeb.Visible(wa) && w.Eras.Available.Any(t => t.Absorbs.Contains("wreckalloy")) && why.Contains("명판");
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
                bool tiersWas = ShipTiers.On;
                ShipTiers.On = false; // v23 Ⅰ 시대에서 — 안전 배전반(동력) 실험
                var w = DayOne(seed, "Hanbit");
                ShipTiers.On = tiersWas;
                w.Eras.Begin("rcd", "시험");
                int det0 = w.Blast.Stats.Detonations, acc0 = w.TechWeb.Stats.Accidents;
                int adv0 = w.Automation.Book.Acts.Count(a => a.Observe.StartsWith("실험 사고"));
                ExperimentState? trial = null;
                for (int i = 0; i < 48 && w.TechWeb.Stats.Accidents == acc0; i++)
                {
                    Run(w, SimTime.Minutes(30));
                    if (w.TechWeb.Trial is ExperimentState x && x.Tech == w.Eras.Project && x.Force == null) { x.Force = "accident"; x.Style = ResearchStyle.Bold; trial = x; }
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
                var dbg = new List<string>();
                for (int i = 0; i < 96 && x == null; i++)
                {
                    Run(w, SimTime.Minutes(10));
                    if (w.TechWeb.Trial is ExperimentState y && y.Progress > 0.15f && w.Crew[y.Lead].Job?.Activity is ResearchActivity) x = y;
                    if (i % 6 == 0 && Environment.GetEnvironmentVariable("TW_DEBUG") != null)
                        dbg.Add(w.TechWeb.Trial is ExperimentState z ? $"{i / 6}h {z.Tech} {z.Progress:0.00} {w.Crew[z.Lead].Name}:{w.Crew[z.Lead].Job?.Activity?.GetType().Name}" : $"{i / 6}h 없음 {w.Eras.Project}");
                }
                if (dbg.Count > 0) Console.WriteLine("    " + string.Join(" | ", dbg));
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
                    $"[실험 {(x != null ? "잡힘" : "없음")} · 연구 {w.Eras.ProjectTech?.Name} · 점검표 익힘 {w.Eras.Known.Contains("c1")}] 끊김 {s.Interrupts} · 재개 {s.Resumed} · 노트 {s.Notes}(남의 노트 읽음 {s.NotesRead}) · {string.Join(" / ", w.TechWeb.Recent.Where(r => r.text.Contains("끊겼") || r.text.Contains("재개")).Take(2).Select(r => r.text))}");
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

            // 7) 다음 연구 고르기 (v23): 겪은 일이 많은 줄기를 먼저 — 이유가 연대기에 남는다 · 함장 우선 지시가 그보다 앞선다
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                Run(w, SimTime.Hours(1));
                w.History.Fires += 4;
                w.Eras.Begin("m1", "시험"); // 아무거나 하나 하던 중
                w.Eras.LearnNow("m1");
                Run(w, SimTime.Minutes(10));
                var pick = w.Eras.ProjectTech;
                var line = w.History.Events.LastOrDefault(e => e.Text.StartsWith("다음 연구 —"))?.Text ?? "";
                bool fire = pick != null && pick.Branch == Branch.Life && line.Contains("불이");
                var other = w.Eras.Available.First(t => t.Id != pick?.Id && !t.Side);
                bool ordered = w.Eras.SetPriority(other.Id) && w.Eras.Project == other.Id && w.Eras.Priority == other.Id;
                bool kept = pick != null && w.Eras.Partial.ContainsKey(pick.Id) || w.Eras.Partial.Count == 0;
                Check("다음 연구 — 불을 네 번 겪은 배는 생명유지 줄기를 먼저 고르고 이유가 연대기에 남는다 · 함장이 누르면 그것부터 (하던 것은 남겨 둔다)",
                    fire && ordered && kept && w.Eras.Stats.Orders == 1,
                    $"고른 것 {pick?.Name} · {line} · 함장 지시 → {other.Name} ({w.Eras.ProjectWhy})");
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

            // 9) 새 시스템 줄기: 그 시스템이 읽는 배율이 실제로 바뀐다 (v23 크기별 시작 시대는 끄고 — 아무것도 모르는 배에서)
            {
                bool tiersWas = ShipTiers.On;
                ShipTiers.On = false;
                var w = World.CreateDefault(seed, 0, "Hanbit");
                ShipTiers.On = tiersWas;
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
                Check("이틀 — 회의(주 컴퓨터 권고)가 이유를 들어 연구를 고르고 사람들이 실제로 실험한다 (v23)",
                    w.Eras.Stats.Starts >= 1 && w.Eras.ProjectWhy.Length > 0 && s.Experiments >= 2 && (w.Eras.Order.Count >= 1 || w.Eras.Progress > 0f),
                    $"익힌 기술 {w.Eras.Order.Count}: {string.Join(", ", w.Eras.Order.Select(id => TechWeb.Find(id)!.Name))} · 지금 {w.Eras.ProjectTech?.Name} {w.Eras.Fraction * 100:0}% ({w.Eras.ProjectWhy}) · 실험한 사람 {string.Join(", ", tops)} · {s}");
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
