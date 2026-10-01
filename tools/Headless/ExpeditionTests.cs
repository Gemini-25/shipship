using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.12 재료 탐사 원정: 재료가 바닥나 배가 멈추고 → 주 컴퓨터가 예측 · 평가 · 경고하고 → 승무원이 꺼내고 · 자원하고 · 반대하고 → 정해서
// 셋이 우주복을 입고 에어락으로 나가 배에서 사라지고(당직이 밀리고 · 남은 사람이 무전을 기다리고 걱정하고) → 며칠 뒤 재료를 들고 돌아와
// 함께 고생한 사이가 되고 · 전리품을 나르고 · 식탁에서 이야기한다. 부상 · 실종(죽음/기적의 귀환) · 셔틀이면 더 많이 · 결정론.
public static partial class Program
{
    private static readonly ItemKind[] RepairKinds = { ItemKind.Plate, ItemKind.Cable, ItemKind.Sealant, ItemKind.Fuse, ItemKind.Electronics, ItemKind.MetalOre, ItemKind.Silicate, ItemKind.Carbon };

    /// <summary>시험: 수리재를 모두 치운다 (창고 · 호퍼 · 손).</summary>
    private static void StripRepair(World w, int leavePlates = 0)
    {
        foreach (var f in w.Ship.Furniture.Where(f => f.Storage != null))
            foreach (var k in RepairKinds) f.Storage!.Take(k, f.Storage.Count(k));
        foreach (var c in w.Crew) if (c.Carrying is ItemStack s && RepairKinds.Contains(s.Kind)) c.Carrying = null;
        if (leavePlates > 0) VoyageV15.Put(w, ItemKind.Plate, leavePlates);
    }

    private static bool RunUntil(World w, Func<bool> done, long max, long step = 0)
    {
        if (step <= 0) step = SimTime.Minutes(15);
        for (long t = 0; t < max; t += step)
        {
            if (done()) return true;
            Run(w, step);
        }
        return done();
    }

    private static void EnsureSuits(World w, int want)
    {
        int have = w.Ship.FurnitureOf(FurnitureType.SuitLocker).Sum(f => f.Storage!.Count(ItemKind.Suit));
        foreach (var f in w.Ship.FurnitureOf(FurnitureType.SuitLocker))
        {
            if (have >= want) break;
            have += f.Storage!.Add(ItemKind.Suit, want - have);
        }
    }

    private static int RunExpeditionTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"재료 탐사 원정 점검 (v16.12) · 시드 {seed}\n");
        float rate0 = CollectionSystem.RatePerHour;
        try
        {
            Check("목록 — 목적지 8종 · 일지 사건 40+ · 재료 갈래 6", ExpeditionSites.All.Length == 8 && ExpeditionSites.EventCount >= 40 && ExpeditionSystem.Cats.Length == 6,
                $"목적지 {ExpeditionSites.All.Length} · 사건 {ExpeditionSites.EventCount}");

            // ── 1) 재료 바닥 → 컴퓨터 예측 → 정지 → 제안(자원 · 반대) → 결정 → 셋이 출발 → 원정 중 → 귀환 → 나르기 → 식탁 ──
            {
                var w = DayOne(seed, "Hanbit");
                CollectionSystem.RatePerHour = 0f; // 시험: 채집 팔이 멎었다 (재료가 다시 들어오지 않는다)
                var x = w.Expedition;
                EnsureSuits(w, 5);
                // 자원할 사람 · 반대할 사람을 꾸민다 (성격은 그대로 — 가장 대담한 기술자 · 가장 겁 많은 사람)
                var adults = w.Crew.Where(c => !c.Dead && !c.IsChild).ToList();
                var bold = adults.Where(c => c.Role is CrewRole.Engineer or CrewRole.Technician or CrewRole.Electrician).OrderByDescending(c => c.Traits.Bravery).First();
                bold.Needs.Stress = 0f; bold.Needs.Rest = 1f; bold.Practice(Skill.Mechanics, 0.5f); bold.Memory.Trauma = 0f;
                var timid = adults.Where(c => c != bold && w.Command.Captain != c).OrderBy(c => c.Traits.Bravery).First();
                timid.Value = CrewValue.Safety; timid.Memory.Trauma = 0.3f; timid.Memory.TraumaCause ??= "지난 감압";
                foreach (var o in adults) if (o != timid) timid.ChangeAffinity(o, 0.7f);

                // (a) 주 컴퓨터 예측: 수리재가 조금씩 줄어든다 → 바닥나기 전에 알린다
                StripRepair(w, leavePlates: 8);
                for (int i = 0; i < 6 && x.Stats.Forecasts == 0; i++) { Run(w, SimTime.Hours(3)); Life.Take(w, ItemKind.Plate, 1); }
                Run(w, SimTime.Hours(2));
                var fc = x.Forecasts.FirstOrDefault(f => f.cat == MatCat.Repair);
                Check("주 컴퓨터 — 소모 속도로 바닥날 날을 예측해 방송 · 다섯 칸 기록", x.Stats.Forecasts > 0 && w.Automation.Book.Acts.Any(a => a.Key.StartsWith("exp:fc")),
                    fc.text ?? $"예측 없음 (컴퓨터 {(w.Automation.Present ? w.Automation.MainOnline ? "켜짐" : "멎음" : "없음")} · 소모 {x.Rate[0]:0.0}/일 · 남은 {x.DaysLeft(MatCat.Repair):0.0}일)");
                Check("주 컴퓨터 — 후보를 평가한다 (점수 · 한 줄)", x.Sites.Count > 0 && x.Sites.All(s => s.CompNote != ""),
                    string.Join(" / ", x.Sites.Take(4).Select(s => $"{s.Name}({s.Spec.Name}) {s.CompScore:0.0}: {s.CompNote}")));

                // (b) 바닥 → 정지
                StripRepair(w);
                bool halted = RunUntil(w, () => x.Halted, SimTime.Hours(10));
                Check("재료 바닥 → 배가 멈춘다 (엔진 정지 · 사유 · 연대기)", halted && x.HaltCat == MatCat.Repair && w.History.Events.Any(e => e.Text.StartsWith("엔진을 껐다")),
                    halted ? $"{x.HaltWhy} · {x.HaltBy}" : $"멈추지 않음 · 수리재 {x.Stock(MatCat.Repair)}");
                float done0 = w.Voyage.DoneDays;
                Run(w, SimTime.Hours(1));
                Check("멈춘 배 — 일정이 밀린다 · 식은 엔진은 회피가 늦다 · 해적 눈에 띈다", w.Voyage.DoneDays == done0 && x.ColdStartMinutes > 0f && x.OutsideMul("해적") > 1f,
                    $"진행 {done0:0.000} → {w.Voyage.DoneDays:0.000}일 · 점화 +{x.ColdStartMinutes}분 · 해적 ×{x.OutsideMul("해적")}");

                // (c) 제안: 승무원이 꺼내고 · 자원하고 · 반대한다 · 컴퓨터가 권하거나 말린다
                RunUntil(w, () => x.Proposals.Count > 0, SimTime.Hours(4));
                var p = x.Proposals.FirstOrDefault();
                Check("제안 — 알아챈 승무원이 꺼낸다 (아니면 컴퓨터)", p != null && (p.ProposerId >= 0 || p.Source == "주 컴퓨터"),
                    p != null ? $"{p.Source} · {p.Why} · {p.Site.Name}" : "제안 없음");
                if (p == null) throw new Exception("제안이 없어 더 볼 수 없다");
                Check("승무원 — 스스로 자원한다 (까닭과 함께)", p.Volunteers.Count > 0 && p.Volunteers.All(v => v.why != ""),
                    string.Join(" · ", p.Volunteers.Select(v => $"{w.Crew[v.who].Name}: {v.why}")));
                Check("승무원 — 반대한다 (안전 · 겁 · 데인 기억 · 보낼 사람이 소중하다)", p.Objectors.Count > 0 && p.Objectors.All(v => v.why != ""),
                    string.Join(" · ", p.Objectors.Select(v => $"{w.Crew[v.who].Name}: {v.why}")));
                var (vy, vwhy) = x.Opinion(w.Crew[p.Volunteers[0].who], p);
                var (oy, owhy) = x.Opinion(w.Crew[p.Objectors[0].who], p);
                Check("찬반 — 자원한 사람은 찬성 · 반대한 사람은 반대 (까닭)", vy && !oy, $"{w.Crew[p.Volunteers[0].who].Name}: {(vy ? "찬" : "반")}({vwhy}) · {w.Crew[p.Objectors[0].who].Name}: {(oy ? "찬" : "반")}({owhy})");
                Check("주 컴퓨터 — 원정에 의견을 낸다 (권함 · 괜찮음 · 다른 곳)", p.Computer != null, p.Computer ?? "의견 없음");
                // 셋이 가게: 큰 목적지(수리재를 채우는)를 관찰자가 원정 창에서 고른다
                if (p.Open && p.Team.Count != 3)
                {
                    var open = x.Sites.Where(s => !s.Taken).ToList();
                    int idx = open.FindIndex(s => (s.Yield >= 12f || s.Risk >= 0.4f) && ExpeditionSites.Fills(s.Kind, MatCat.Repair) >= 0.25f && s.Expires > w.Tick + SimTime.Hours(30));
                    if (idx < 0) idx = open.FindIndex(s => s.Yield >= 12f || s.Risk >= 0.4f);
                    if (idx >= 0 && idx < 6) { Player.Policy(w, "expsite", idx + 1); Run(w, SimTime.Hours(1)); }
                }
                Console.WriteLine($"    원정대 {string.Join("·", p.Team.Select(id => w.Crew[id].Name))} → {p.Site.Name} · 우주복 {w.Ship.FurnitureOf(FurnitureType.SuitLocker).Sum(f => f.Storage!.Count(ItemKind.Suit))}");

                // (d) 결정: 회의 · 함장 (오래 안 정해지면 관찰자 지시)
                bool decided = RunUntil(w, () => !p.Open, SimTime.Hours(30));
                string by = p.DecidedBy;
                if (!decided || p.State != "보낸다")
                {
                    Player.Policy(w, "expedition", 1);
                    RunUntil(w, () => x.Current != null, SimTime.Hours(14));
                }
                Check("결정 — 회의 · 함장 · 관찰자 중 하나가 정한다 (찬반 · 까닭)", decided && by != "", $"{p.State} — {by} · {p.DecideWhy}");
                var t = x.Current;
                if (t == null) throw new Exception("원정대가 꾸려지지 않았다");
                int Aboard() => w.Ship.FurnitureOf(FurnitureType.SuitLocker).Sum(f => f.Storage!.Count(ItemKind.Suit)) + w.Crew.Count(c => !c.Dead && !c.Away && c.Suit != null);
                int suits0 = Aboard();
                bool gone = RunUntil(w, () => t.Phase == TripPhase.Away, SimTime.Hours(8), SimTime.Minutes(5));
                var team = t.Members.Select(m => w.Crew[m.Id]).ToList();
                int suits1 = Aboard();
                Check("출발 — 셋이 우주복을 입고 에어락으로 걸어 나가 배에서 사라진다", gone && team.Count == 3 && team.All(c => c.Away && c.Room == null && !c.CanAct && !c.IsAwake),
                    $"{string.Join("·", team.Select(c => c.Name))} · {t.Phase} · 장비 {ExpeditionSystem.GearText(t)}");
                Check("빈 장비 걸이 — 우주복이 배에서 셋 줄었다 · 장비는 재고에서", t.SuitsAtCall - t.SuitsLeft == team.Count && team.All(c => c.Suit != null) && t.Gear.Count >= 2, $"배의 우주복 {t.SuitsAtCall}→{t.SuitsLeft} · {ExpeditionSystem.GearText(t)}");
                var subs = w.Crew.Where(c => !c.Dead && !c.Away && c.CoveringUntil > w.Tick).ToList();
                Check("당직이 밀린다 — 남은 사람이 근무를 대신 선다 · 야간 당직에 원정대는 없다", subs.Count > 0 && team.All(c => !w.Society.OnNightWatch(c)),
                    string.Join(" · ", t.Members.Where(m => m.CoveredBy >= 0).Select(m => $"{w.Crew[m.Id].Name}→{w.Crew[m.CoveredBy].Name}")));
                var pairs = new List<(CrewMember a, CrewMember b, float aff)>();
                for (int i = 0; i < team.Count; i++) for (int j = i + 1; j < team.Count; j++) pairs.Add((team[i], team[j], team[i].AffinityTo(team[j])));

                // (e) 원정 중: 일지 · 무전(들은 사람만) · 걱정 · 무전 기다리기
                Run(w, SimTime.Hours(30));
                var heard = t.Radio.Where(r => !r.Lost).ToList();
                int stay = w.Crew.Count(c => !c.Dead && !c.Away);
                Check("원정 일지 — 하루 한두 줄 (사건 표에서)", t.Journal.Count >= 2, string.Join(" / ", t.Journal.Select(j => $"{j.Day}일 {j.Text}").TakeLast(3)));
                Check("무전 — 정해 둔 시간에 · 통신실 · 함교에 있던 사람만 듣는다", t.Radio.Count >= 1 && heard.All(r => r.HeardBy.Count < stay),
                    string.Join(" / ", t.Radio.Select(r => r.Lost ? $"잡음({r.Text})" : $"\"{r.Text}\" 들은 사람 {r.HeardBy.Count}/{stay} · 전해 들은 사람 {r.Rumor.Count}")));
                Check("남은 사람 — 걱정한다 (소식을 못 들은 만큼) · 무전 앞에서 기다린다", x.Worry.Values.Any(v => v > 0.05f) && (x.Stats.RadioWaits > 0 || x.Stats.Worries > 0),
                    $"걱정 큰 사람 {x.Worry.Count(kv => kv.Value > 0.3f)} · 무전 기다림 {x.Stats.RadioWaits} · 걱정 일기 {x.Stats.Worries}");

                // (f) 귀환
                bool back = RunUntil(w, () => x.Current == null, SimTime.TicksPerDay * 6);
                Check("귀환 — 재료를 들고 에어락으로 돌아온다 · 전리품이 쌓인다", back && team.All(c => !c.Away && c.Room != null) && t.LootTotal > 0 && (x.Spoils.Count > 0 || x.Stats.Hauled > 0),
                    $"{t.Site.Name} · {x.LootText(t)} · 쌓인 것 {x.SpoilsCount} · 우주복 {string.Join("/", t.Members.Select(m => TripMember.Stage(m.SuitDamage)))}");
                bool bonded = pairs.All(q => q.a.AffinityTo(q.b) > q.aff) && w.Relations.All.Any(r => r.Reason == RelationReason.SharedHardship && team.Any(c => c.Id == r.Who));
                Check("함께 고생한 사이 — 서로 가까워지고 · 까닭이 남는다", bonded, string.Join(" · ", pairs.Select(q => $"{q.a.Name}→{q.b.Name} {q.aff:0.00}→{q.a.AffinityTo(q.b):0.00}")));
                Check("기억 · 일기 · 연대기 · 칭호", team.All(c => c.Memory.Marks.Any(m => m.Text.StartsWith("원정에서 돌아왔다"))) && w.History.Events.Any(e => e.Text.StartsWith("원정대 귀환")) && team.Any(c => x.Led(c) >= 1),
                    w.History.Events.LastOrDefault(e => e.Text.StartsWith("원정대 귀환"))?.Text ?? "");
                if (Environment.GetEnvironmentVariable("EXPDBG") != null)
                {
                    foreach (var f in w.Ship.Containers.Where(f => f.Storage!.Accepts(ItemKind.MetalOre)))
                        Console.WriteLine($"    [dbg] 상자 {f.Type} {f.Room.Name} 빈칸 {f.Storage!.Free}/{f.Storage.Capacity} · 자리 {f.UseSpots.Count}");
                    Console.WriteLine($"    [dbg] 더미 {x.SpoilsAt} 방 {x.SpoilsRoom} · 든 사람 " + string.Join(",", w.Crew.Where(c => c.Carrying != null).Select(c => $"{c.Name}:{c.Carrying}:{c.ActivityLabel}")));
                }
                if (Environment.GetEnvironmentVariable("EXPDBG") != null)
                    for (int k = 0; k < 6; k++)
                    {
                        Run(w, SimTime.Hours(2));
                        Console.WriteLine($"    [dbg] {SimTime.Clock(w.Tick)} 위기 {Crisis.Level(w)} · 더미 {x.SpoilsCount} · 이야기 {x.StoryTime} · 든 사람 " + string.Join(",", w.Crew.Where(c => c.Carrying != null).Select(c => $"{c.Name}:{c.Carrying}:{c.ActivityLabel}")));
                        foreach (var c in w.Crew.Where(c => !c.Dead && !c.Away).Take(5))
                            Console.WriteLine($"      {c.Name}: {c.ActivityLabel} | " + string.Join(" · ", c.LastEvaluations.Take(4).Select(e => $"{e.Activity.Id} {e.Score:0.00}")) + " | 원정 " + string.Join("", c.LastEvaluations.Where(e => e.Activity is ExpeditionActivity).Select(e => $"{e.Score:0.00} {e.Reason}")));
                    }
                bool hauled = RunUntil(w, () => x.Spoils.Count == 0, SimTime.TicksPerDay);
                Check("전리품을 날라 창고에 넣는다 (사람이 들고 간다)", hauled && x.Stats.Hauled > 0, $"나른 것 {x.Stats.Hauled} · 수리재 {x.Stock(MatCat.Repair)}");
                bool story = RunUntil(w, () => x.Stats.Stories > 0, SimTime.Hours(36));
                Check("식탁 이야기 — 저녁에 모여 원정 이야기를 한다 · 들은 사람이 가까워진다", story && t.Listeners > 0, story ? $"\"{t.Story}\" · 들은 사람 {t.Listeners}" : "이야기 없음");
                bool resumed = RunUntil(w, () => !x.Halted, SimTime.TicksPerDay * 4);
                Check("다시 엔진을 켠다 (재료가 찼거나 · 더는 기다릴 수 없다)", resumed && x.Stats.Resumed >= 1, w.History.Events.LastOrDefault(e => e.Text.StartsWith("엔진을 다시 켰다"))?.Text ?? "");
                Check("주 컴퓨터 — 출발 전 위험 경고 · 수확 예측과 견줘 신뢰가 바뀐다", x.Stats.Warnings > 0 && t.CompWarn != "" && team.Any(c => w.Automation.Trusts.Changed(c)),
                    $"경고 {x.Stats.Warnings}{(t.CompWarn != "" ? $" · {t.CompWarn}" : "")} · 예상 {t.CompEstimate:0} · 실제 {t.LootTotal}");
                Check("승무원이 다르게 행동한다 — 우주복 미리 점검 · 무전 기다림 · 마중 · 나르기 · 이야기", x.Stats.Hauled > 0 && x.Stats.Stories > 0 && (x.Stats.Prepped + x.Stats.RadioWaits + x.Stats.Greeted) > 0,
                    $"점검 {x.Stats.Prepped} · 무전 {x.Stats.RadioWaits} · 마중 {x.Stats.Greeted} · 나름 {x.Stats.Hauled} · 이야기 {x.Stats.Stories}");
            }

            // ── 2) 부상 · 실종 → 죽음과 추모 ──
            {
                var w = DayOne(seed, "Hanbit");
                CollectionSystem.RatePerHour = 0f;
                var x = w.Expedition;
                EnsureSuits(w, 5);
                Player.AllowDeath(w, true);
                Player.Policy(w, "expedition", 1); // 관찰자 지시: 당장 보낸다
                StripRepair(w);
                bool away = RunUntil(w, () => x.Current is { Phase: TripPhase.Away }, SimTime.Hours(20));
                var t = x.Current;
                if (!away || t == null) throw new Exception("원정대가 나가지 못했다");
                var team = t.Members.Select(m => w.Crew[m.Id]).ToList();
                var ctx = new ExpCtx(w, t, new Rng(seed), team.ToList());
                var hurt = team[0];
                ctx.Hurt(hurt, 0.25f, "원정 중 미세 운석 파편");
                var lost = team.Count > 1 ? team[^1] : null;
                if (lost != null) ctx.Missing(lost, "생명줄이 끊겨 표류");
                t.ReturnAt = w.Tick + SimTime.Hours(6);
                RunUntil(w, () => x.Current == null, SimTime.Hours(10));
                Check("부상 — 다쳐서 돌아온다 (상처 · 원인)", !hurt.Away && hurt.Vitals.Injury > 0.1f && hurt.Vitals.Wounds.Any(), $"{hurt.Name} 부상 {hurt.Vitals.Injury:0.00} · {hurt.Vitals.InjuryCause}");
                if (lost != null)
                {
                    Check("실종 — 돌아오지 못한 사람 · 위급 경보", lost.Away && x.Stats.Missing >= 1 && w.Alerts.Any(a => a.Text.Contains("실종")), $"{lost.Name} · {t.Members.First(m => m.Id == lost.Id).MissingWhy}");
                    RunUntil(w, () => lost.Dead, SimTime.TicksPerDay * 3);
                    Check("죽음 처리 · 추모 (죽을 수 있는 배)", lost.Dead && (w.Life.Memorial.Any(m => m.name == lost.Name) || w.History.Events.Any(e => e.Text.Contains("끝내 돌아오지 않았다"))),
                        w.History.Events.LastOrDefault(e => e.Text.Contains("끝내 돌아오지"))?.Text ?? $"{lost.Name} 살아 있음");
                }
            }

            // ── 3) 셔틀이면 더 많이 · 더 빨리 · 실종자가 기적처럼 돌아온다(죽지 않는 배) ──
            {
                var w = DayOne(seed, "Hanbit");
                var x = w.Expedition;
                var site = x.Sites.FirstOrDefault() ?? throw new Exception("후보 없음");
                var crew3 = w.Crew.Where(c => !c.Dead && !c.IsChild).Take(3).ToList();
                var ta = new Trip { Id = 900, Site = site };
                var tb = new Trip { Id = 901, Site = site, Shuttle = true };
                foreach (var c in crew3) { ta.Members.Add(new TripMember { Id = c.Id }); tb.Members.Add(new TripMember { Id = c.Id }); }
                var ca = new ExpCtx(w, ta, new Rng(seed * 3 + 1), crew3);
                var cb = new ExpCtx(w, tb, new Rng(seed * 3 + 1), crew3);
                for (int i = 0; i < 12; i++) { ca.Haul(4); cb.Haul(4); }
                Check("셔틀 규칙 — 같은 곳 · 같은 솜씨면 셔틀 짐칸이 더 싣는다", tb.LootTotal > ta.LootTotal, $"걸어서 {ta.LootTotal} · 셔틀 {tb.LootTotal}");

                x.ShuttleOwned = true;
                CollectionSystem.RatePerHour = 0f;
                EnsureSuits(w, 5);
                float prop0 = w.Propulsion.Propellant;
                Player.Policy(w, "expedition", 1);
                StripRepair(w);
                bool away = RunUntil(w, () => x.Current is { Phase: TripPhase.Away }, SimTime.Hours(20));
                var t = x.Current;
                if (!away || t == null) throw new Exception("셔틀 원정이 나가지 못했다");
                float walk = t.Site.Dist * 2f + 0.6f + 0.15f * t.Site.Yield / Math.Max(1, t.Members.Count);
                Check("셔틀로 나간다 — 추진제를 쓰고 · 더 빨리 · 사람을 더 태운다", t.Shuttle && w.Propulsion.Propellant < prop0 && t.Planned < SimTime.Hours(walk * 24f) && t.Members.Count >= 3,
                    $"추진제 {prop0:0}→{w.Propulsion.Propellant:0}kg · 예정 {t.Planned / (float)SimTime.TicksPerDay:0.0}일 (걸어서면 {walk:0.0}일) · {t.Members.Count}명");
                var team = t.Members.Select(m => w.Crew[m.Id]).ToList();
                var lost = team[^1];
                new ExpCtx(w, t, new Rng(seed), team.ToList()).Missing(lost, "생명줄이 끊겨 표류");
                RunUntil(w, () => x.Current == null, SimTime.TicksPerDay * 5);
                Check("셔틀 원정 귀환 — 재료를 싣고 온다", x.Past.Contains(t) && t.LootTotal > 0, x.LootText(t));
                bool late = RunUntil(w, () => !lost.Away, SimTime.Hours(48));
                Check("실종 — 죽지 않는 배에서는 며칠 뒤 표류하다 돌아온다 (다치고 · 데인 기억)", late && !lost.Dead && lost.Vitals.Injury > 0.1f && lost.Memory.Trauma > 0f,
                    w.History.Events.LastOrDefault(e => e.Text.Contains("만에 돌아왔다"))?.Text ?? $"{lost.Name} 아직 {(lost.Away ? "실종" : "?")}");
            }

            // ── 4) 결정론 ──
            {
                uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
                uint a = H(), b = H();
                Check("결정론 — 같은 시드 같은 지문", a == b, $"{a:x8} / {b:x8}");
                uint E()
                {
                    var w = DayOne(seed, "Hanbit");
                    CollectionSystem.RatePerHour = 0f;
                    EnsureSuits(w, 5);
                    Player.Policy(w, "expedition", 1);
                    StripRepair(w);
                    Run(w, SimTime.Hours(40));
                    return SaveGame.StateHash(w) ^ (uint)(w.Expedition.Current?.Journal.Count ?? -1) * 2654435761u;
                }
                uint e1 = E(), e2 = E();
                Check("결정론 — 원정 중인 배도 같은 시드 같은 지문 (일지 · 무전 · 걱정)", e1 == e2, $"{e1:x8} / {e2:x8}");
            }
        }
        catch (Exception e) { Console.WriteLine(e); _fails++; }
        finally { CollectionSystem.RatePerHour = rate0; }
        Console.WriteLine(_fails == 0 ? "\n✔ 재료 탐사 원정 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
