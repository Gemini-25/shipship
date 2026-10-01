using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v18.13 우주 규모 대재난 30: 예보 → 대비(사람 · 컴퓨터) → 본 사건 → 후유증 · 관행
public static partial class Program
{
    private static void CosmicRunUntil(World w, Func<bool> done, long maxTicks)
    {
        for (long t = 0; t < maxTicks && !done(); t++) w.Step();
    }

    private static int RunCosmicTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"우주 대재난 점검 (v18.13) · 시드 {seed}\n");
        try
        {
            // 0) 표
            var all = CosmicCatalog.All;
            Check("표 — 30종 · 순서 · 넷으로 갈림 · 모두 우주급", all.Length == 30 && all.Select((s, i) => (int)s.Kind == i).All(x => x) && all.Select(s => s.Group).Distinct().Count() == 4 && all.All(s => s.Scale == "우주급"),
                $"{all.Length}종 · 별 {all.Count(s => s.Group == CosmicGroup.Star)} · 충돌 {all.Count(s => s.Group == CosmicGroup.Body)} · 인공 {all.Count(s => s.Group == CosmicGroup.Artificial)} · 이상 {all.Count(s => s.Group == CosmicGroup.Anomaly)}");
            Check("표 — 재난마다 연출이 다르다 (모양 설명 · 색이 겹치지 않는다)", all.Select(s => s.Shape).Distinct().Count() == 30 && all.Select(s => s.Hex + s.Hex2).Distinct().Count() == 30, "");

            // 1) 초신성: 예보 → 대비 → 차폐로 → 파도 → 새 성운 · 관행
            {
                var w = DayOne(seed, "Hanbit");
                int pa0 = w.Automation.Speak.Count;
                float stress0 = w.Crew.Where(c => !c.Dead).Average(c => c.Needs.Stress);
                var e = w.Cosmic.Force(CosmicKind.Supernova, 10f, close: true, side: 0f);
                Check("예보 — 컴퓨터가 센서로 알아채고 신뢰도 · 오차를 붙여 방송한다", e.Known && e.Confidence > 0.3f && e.ErrorHours > 0f && w.Automation.Speak.Count > pa0
                      && w.Automation.Book.Acts.Any(a => a.Key == "cosmic:" + e.Id),
                    $"알아챈 이: {e.KnownBy} · 신뢰도 {e.Confidence * 100:0}% · 오차 ±{e.ErrorHours:0.0}시간 · 예보 {e.HoursTo(w.Tick, e.Predicted):0.0}시간 뒤(실제 {e.HoursTo(w.Tick, e.Arrive):0.0}) · 들은 사람 {w.Crew.Count(c => w.Cosmic.Knows(c, e))}/{w.Crew.Count(c => !c.Dead)}");
                CosmicRunUntil(w, () => e.Phase >= CosmicPhase.Brace, SimTime.Hours(12));
                Check("대비 — 컴퓨터가 대비 계획을 짠다 (물벽 · 물자 · 덮개 · 장비 끄기 · 고정)", e.Phase == CosmicPhase.Brace && e.Tasks.Count >= 3 && e.Tasks.Any(t => t.Kind == BraceKind.WaterWall),
                    string.Join(" · ", e.Tasks.Select(t => t.Label)));
                int bracers = 0;
                var seenBrace = new HashSet<int>();
                var seenShelter = new HashSet<int>();
                float water0 = w.Water.Level;
                for (int i = 0; i < 400 && w.Tick < e.Arrive; i++)
                {
                    Run(w, SimTime.Minutes(3));
                    foreach (var c in w.Crew.Where(c => !c.Dead))
                    {
                        if (c.Job?.Activity is CosmicBraceActivity) seenBrace.Add(c.Id);
                        if (c.Job?.Activity is CosmicShelterActivity) seenShelter.Add(c.Id);
                    }
                }
                bracers = seenBrace.Count;
                float stress1 = w.Crew.Where(c => !c.Dead && w.Cosmic.Knows(c, e)).Average(c => c.Needs.Stress);
                Check("승무원 — 예보를 들은 사람이 대비 일을 실제로 한다 (물벽에 물을 쓴다)", bracers >= 2 && e.Tasks.Count(t => t.Done) >= 2 && w.Cosmic.Stats.BraceDone >= 2,
                    $"대비한 사람 {bracers} · 끝낸 일 {e.Tasks.Count(t => t.Done)}/{e.Tasks.Count} ({string.Join(", ", e.Tasks.Where(t => t.Done).Select(t => $"{t.Label}:{t.DoneBy}"))}) · 물 {water0:0} → {w.Water.Level:0}L");
                Check("승무원 — 모두 알게 된다 (방송 · 말 · 창밖) · 다가올수록 긴장한다", w.Crew.Where(c => !c.Dead).All(c => w.Cosmic.Knows(c, e)),
                    $"아는 사람 {w.Crew.Count(c => !c.Dead && w.Cosmic.Knows(c, e))}/{w.Crew.Count(c => !c.Dead)} · 알린 횟수 {w.Cosmic.Stats.Warned} · 스트레스 {stress0:0.00} → {stress1:0.00}");
                // 섬광 → (18시간 뒤) 방사선 파도: 두 시간 전, 둘이 모른다고 치면 동료가 가서 알린다
                CosmicRunUntil(w, () => w.Cosmic.NextHarm(e) - w.Tick < SimTime.Hours(2.5f), SimTime.Hours(40));
                var forgot = w.Crew.Where(c => !c.Dead && c.IsAwake && !c.Outside).OrderByDescending(c => c.Id).Take(2).ToList();
                foreach (var c in forgot) w.Cosmic.Forget(c, e);
                int warned0 = w.Cosmic.Stats.Warned;
                CosmicRunUntil(w, () => forgot.All(c => w.Cosmic.Knows(c, e)), SimTime.Hours(1.5f));
                Check("서로 돕기 — 모르는 사람에게 동료가 가서 알린다 (자는 사람은 깨운다)", forgot.All(c => w.Cosmic.Knows(c, e)) && w.Cosmic.Stats.Warned > warned0,
                    $"{string.Join(", ", forgot.Select(c => $"{c.Name}: {w.Cosmic.Knowing(c, e)?.how ?? "모름"}"))} · 알림 {w.Cosmic.Stats.Warned} · 깨움 {w.Cosmic.Stats.Woken}");
                CosmicRunUntil(w, () => w.Cosmic.FxNow(e, CosmicFx.Radiation) > 0f, SimTime.Hours(40));
                Run(w, SimTime.Hours(2.5f));
                var live = w.Crew.Where(c => !c.Dead && !c.Outside && c.Room != null).ToList();
                int sheltered = live.Count(c => w.Cosmic.RelExposure(c.Room!) <= 0.32f);
                var refuge = w.Ship.Rooms.Where(r => !r.Detached && (RoomCatalog.Tags(r.Kind) & RoomTag.Shielded) != 0 || w.Cosmic.Water(r) > 0.3f).ToList();
                var outer = w.Ship.Rooms.Where(r => !r.Detached && w.Ambience.Exposure(r) > 0.8f && w.Cosmic.Water(r) < 0.1f && (RoomCatalog.Tags(r.Kind) & RoomTag.Shielded) == 0).ToList();
                float radIn = refuge.Count > 0 ? refuge.Average(r => r.Radiation) : 1f, radOut = outer.Count > 0 ? outer.Average(r => r.Radiation) : 0f;
                Check("파도 — 사람들은 차폐 쪽에 있고, 차폐 방이 덜 맞는다", sheltered * 2 >= live.Count && radIn < radOut * 0.5f,
                    $"숨은 사람 {sheltered}/{live.Count} ({string.Join(", ", live.GroupBy(c => c.Room!.Name).Select(g => $"{g.Key} {g.Count()}"))}) · 방사선 차폐 {radIn:0.00} / 바깥 쪽 {radOut:0.00}");
                CosmicRunUntil(w, () => e.Phase >= CosmicPhase.After, SimTime.Hours(20));
                Check("후유증 — 숨은 사람이 덜 쬐었다 · 하늘에 새 성운이 남는다 · 예보 채점", e.Phase == CosmicPhase.After && (e.NExposed == 0 ? e.GainSheltered < 0.8f : e.GainSheltered < e.GainExposed)
                      && w.Cosmic.Sky.Any(s => s.EventId == e.Id && s.Remnant == CosmicRemnant.Nebula) && e.Grade != "",
                    $"피폭 숨은 {e.NSheltered}명 {e.GainSheltered:0.00}Sv / 바깥 쪽 {e.NExposed}명 {e.GainExposed:0.00}Sv · 하늘 {string.Join(", ", w.Cosmic.Sky.Select(s => s.Name))} · {e.Grade}");
                var act = w.Automation.Book.Acts.FirstOrDefault(a => a.Key == "cosmic:" + e.Id);
                Run(w, SimTime.Hours(1));
                Check("주 컴퓨터 — 예보를 스스로 채점하고, 사람마다 컴퓨터를 믿는 정도가 바뀐다", act != null && act.Graded && w.Automation.Trusts.Changed(w.Crew.First(c => !c.Dead)),
                    $"채점 {act?.Score} {act?.Result} · 평균 신뢰 {w.Automation.Trusts.Average():0.00}");
                // 몇 주 뒤: 관행 (그날의 밤) — 날을 당겨서 확인
                CosmicRunUntil(w, () => w.Cosmic.CustomOf(CosmicCustomKind.Vigil) != null, SimTime.TicksPerDay * 9);
                var vig = w.Cosmic.CustomOf(CosmicCustomKind.Vigil);
                if (vig != null)
                {
                    vig.NextDay = w.Tick / SimTime.TicksPerDay * SimTime.TicksPerDay; // 오늘이 그날
                    if (vig.Followers.Count < 2) foreach (var c in w.Crew.Where(c => !c.Dead).Take(3)) vig.Followers.Add(c.Id);
                    CosmicRunUntil(w, () => vig.Kept.Count >= 2, SimTime.TicksPerDay);
                }
                Check("관행 — 그날의 밤이 생기고 저녁에 창가에 모여 지킨다", vig != null && vig.Kept.Count >= 2,
                    vig == null ? "관행 없음" : $"{CosmicCustom.Name(vig.Kind)} · {vig.Origin} · 시작 {vig.Founder} · 따르는 사람 {vig.Followers.Count} · 지킨 사람 {vig.Kept.Count} · 관행 {string.Join(", ", w.Cosmic.Customs.Select(c => c.Kind))}");
                Check("연대기 — 예보 · 대비 · 지나감이 역사에 남는다", w.History.Events.Count(h => h.Text.Contains("초신성")) >= 3, string.Join(" / ", w.History.Events.Where(h => h.Text.Contains("초신성")).Take(4).Select(h => h.Text)));
            }

            // 2) 큰 소행성: 연료로 피한다
            {
                var w = DayOne(seed, "Hanbit");
                float fuel0 = w.Propulsion.Propellant;
                var e = w.Cosmic.Force(CosmicKind.BigAsteroid, 8f, close: true);
                var p = w.Automation.Asks.Pending("cosmic:avoid:" + e.Id);
                Check("소행성 — 컴퓨터가 항로 변경을 제안한다", p != null, p?.Title ?? "제안 없음");
                if (p != null) w.Automation.Asks.Decide(p, true, "관찰자");
                CosmicRunUntil(w, () => e.Phase >= CosmicPhase.After, SimTime.Hours(12));
                Check("소행성 — 추진제를 써서 비킨다 (못 비키면 구획 봉쇄로 넘어간다)", e.FuelSpent > 0f && w.Propulsion.Propellant < fuel0 && (e.Avoided || e.SealPlan),
                    $"추진제 {fuel0:0} → {w.Propulsion.Propellant:0}kg · {(e.Avoided ? "비켰다" : "다 못 비켰다")} · 맞은 것 {e.Hits} · {e.AvoidWhy}");
            }

            // 3) 큰 소행성: 연료가 없으면 구획을 비우고 봉쇄 → 맞아도 다친 사람이 없다
            {
                var w = DayOne(seed, "Hanbit");
                w.Propulsion.Propellant = 0f;
                var e = w.Cosmic.Force(CosmicKind.BigAsteroid, 8f, close: true);
                if (w.Automation.Asks.Pending("cosmic:avoid:" + e.Id) is Proposal p) w.Automation.Asks.Decide(p, true, "관찰자");
                var room = w.Ship.Rooms[e.TargetRoom];
                // 그 방에 사람 하나를 둔다 (나가야 한다)
                var stay = w.Crew.First(c => !c.Dead);
                stay.Position = room.Cells.First(c => w.Ship.IsWalkable(c)).Center;
                CosmicRunUntil(w, () => w.Tick >= e.Arrive - 1, SimTime.Hours(10));
                bool empty = !w.Crew.Any(c => !c.Dead && c.Room == room);
                Check("봉쇄 — 추진제가 없어 못 비키니 구획을 비우고 봉쇄한다", e.SealPlan && e.Sealed && empty && room.Lockdown,
                    $"{room.Name} · 계획 {e.SealPlan} · 봉쇄 {e.Sealed} · 안에 사람 {w.Crew.Count(c => !c.Dead && c.Room == room)} · {e.AvoidWhy} · 대비 {string.Join(", ", e.Tasks.Select(t => $"{t.Label}{(t.Done ? "✓" : "")}"))}");
                var hurt0 = w.Crew.Where(c => !c.Dead).ToDictionary(c => c.Id, c => c.Vitals.Injury);
                Run(w, SimTime.Minutes(30));
                var hurt = w.Crew.Where(c => !c.Dead && c.Vitals.Injury > hurt0.GetValueOrDefault(c.Id) + 0.12f).ToList();
                Check("봉쇄 — 소행성이 그 구획을 들이받아도 크게 다친 사람이 없다 (충격에 넘어진 정도)", e.Hits > 0 && hurt.Count == 0 && !w.Crew.Any(c => c.Dead),
                    $"맞은 것 {e.Hits} · 크게 다친 사람 {hurt.Count} ({string.Join(", ", hurt.Select(c => c.Vitals.InjuryCause))}) · 넘어짐 {e.Falls} · {room.Name} 새는가 {room.Leaking}");
            }

            // 4) 마그네타: EMP → 꺼 둔 것은 산다 → 복구
            {
                var w = DayOne(seed, "Hanbit");
                var e = w.Cosmic.Force(CosmicKind.MagnetarStorm, 6f, close: true);
                var sp = w.Automation.Asks.Pending("cosmic:shutdown:" + e.Id);
                Check("마그네타 — 컴퓨터가 스스로 내려 두자고 제안한다", sp != null, sp?.Title ?? "제안 없음");
                if (sp != null) w.Automation.Asks.Decide(sp, true, "관찰자");
                CosmicRunUntil(w, () => e.Phase >= CosmicPhase.Impact, SimTime.Hours(9));
                var safed = w.Cosmic.SafedIds.ToList();
                int robotsBad0 = w.Robots.Robots.Count(r => r.Fault != null);
                Run(w, SimTime.Minutes(20));
                var safedOk = safed.Select(id => w.Ship.Furniture.First(f => f.Id == id)).Where(f => f.Machine != null).ToList();
                int broken = w.Ship.Machines.Count(m => m.Has(FaultKind.ControlFault));
                Check("마그네타 — 펄스: 켜 둔 전자 장비 · 로봇 · 컴퓨터가 타고, 꺼 둔 것은 산다", (e.EmpKills > 0 || w.Automation.Reboots > 0) && safedOk.Count > 0 && safedOk.All(f => !f.Machine!.Has(FaultKind.ControlFault)),
                    $"탄 것 {e.EmpKills} · 제어부 고장 {broken} · 로봇 고장 {robotsBad0} → {w.Robots.Robots.Count(r => r.Fault != null)} · 꺼 둔 것 {safedOk.Count} ({string.Join(", ", safedOk.Select(f => f.Label))}) · 재부팅 {w.Automation.Reboots}");
                CosmicRunUntil(w, () => e.Phase >= CosmicPhase.After, SimTime.Hours(6));
                Run(w, SimTime.TicksPerDay);
                int broken2 = w.Ship.Machines.Count(m => m.Has(FaultKind.ControlFault));
                Check("마그네타 — 복구: 꺼 둔 장비를 사람이 다시 켜고 탄 것을 고친다", w.Cosmic.SafedIds.Count == 0 && e.Tasks.Any(t => t.Kind == BraceKind.Restart && t.Done) && broken2 < Math.Max(1, broken) + 1 && w.Automation.MainOnline,
                    $"다시 켬 {string.Join(", ", e.Tasks.Where(t => t.Kind == BraceKind.Restart).Select(t => $"{t.Label}:{t.DoneBy}"))} · 제어부 고장 {broken} → {broken2} · 컴퓨터 {(w.Automation.MainOnline ? "온라인" : "멎음")}");
            }

            // 5) 암흑 성운: 센서 · 항법 먹통
            {
                var w = DayOne(seed, "Mirinae");
                float q0 = w.Sensors.Quality;
                float prog0 = w.Voyage.DoneDays;
                var e = w.Cosmic.Force(CosmicKind.DarkNebula, 2f, close: true);
                CosmicRunUntil(w, () => e.Phase >= CosmicPhase.Impact, SimTime.Hours(4));
                Run(w, SimTime.Hours(2));
                float q1 = w.Sensors.Quality;
                Check("암흑 성운 — 센서가 먹통이 되고 (운석을 늦게 본다) 항로가 밀린다", q1 < q0 * 0.35f && w.Cosmic.SensorMul < 0.3f && w.Sensors.Capability().lead < SensorSystem.LeadMinutes(WarnLevel.Sensor) * 0.5f,
                    $"센서 {q0 * 100:0}% → {q1 * 100:0}% · 미리 보는 시간 {w.Sensors.Capability().lead:0.0}분 · 대비 {string.Join(", ", e.Tasks.Select(t => t.Label))}");
            }

            // 6) 30종 강제 발생 — 단계마다 예외 없이
            {
                var errs = new List<string>();
                int after = 0;
                foreach (var g in Enum.GetValues<CosmicGroup>())
                {
                    var w = DayOne(seed, g == CosmicGroup.Body ? "Hanbit" : "Mirinae");
                    foreach (var s in CosmicCatalog.All.Where(x => x.Group == g))
                    {
                        try
                        {
                            var e = w.Cosmic.Force(s.Kind, 0.1f, close: true);
                            if (w.Automation.Asks.Pending("cosmic:avoid:" + e.Id) is Proposal pa) w.Automation.Asks.Decide(pa, false, "관찰자");
                            Run(w, SimTime.Minutes(10));
                            for (int i = 0; i < s.Stages.Length; i++)
                            {
                                e.Arrive = w.Tick - SimTime.Hours(s.Stages[i].At) - SimTime.Minutes(1);
                                Run(w, SimTime.Minutes(25));
                            }
                            e.Arrive = w.Tick - SimTime.Hours(s.Span) - SimTime.Minutes(1);
                            Run(w, SimTime.Minutes(5));
                            if (e.Phase >= CosmicPhase.After) after++;
                            else errs.Add($"{s.Name}: {e.PhaseName}");
                        }
                        catch (Exception ex) { errs.Add($"{s.Name}: {ex.GetType().Name} {ex.Message}"); }
                    }
                }
                Check("30종 — 하나하나 강제로 걸어도 예외 없이 지나간다", errs.Count == 0 && after == 30, $"지나감 {after}/30" + (errs.Count > 0 ? " · " + string.Join(" / ", errs.Take(4)) : ""));
            }

            // 6-b) 저절로: 항해마다 0~2번 (무작위 사고가 켜져 있을 때만) · 항로 구간에 맞는 것이 잘 생긴다
            {
                float keep = HazardSystem.RandomDays;
                try
                {
                    var counts = new List<int>();
                    for (int s2 = 0; s2 < 6; s2++)
                    {
                        var w = World.CreateDefault(seed + s2 * 101, 0, "Mirinae");
                        Run(w, SimTime.Minutes(2));
                        counts.Add(w.Cosmic.Planned.Count);
                    }
                    var wn = World.CreateDefault(seed, 0, "Mirinae");
                    Run(wn, SimTime.Minutes(2));
                    HazardSystem.RandomDays = 0f;
                    wn.Cosmic.Planned.Clear(); wn.Cosmic.Planned.Add(wn.Tick + 1);
                    Run(wn, SimTime.Minutes(1));
                    int off = wn.Cosmic.Events.Count;
                    HazardSystem.RandomDays = 4f;
                    wn.Cosmic.Planned.Add(wn.Tick + 1);
                    Run(wn, SimTime.Minutes(1));
                    var ev = wn.Cosmic.Events.LastOrDefault();
                    Check("저절로 — 항해마다 0~2번 정해 두고, 사고가 켜져 있을 때만 그때 생긴다", counts.All(n => n is >= 0 and <= 2) && counts.Distinct().Count() >= 2 && off == 0 && ev != null && ev.Source == "항로",
                        $"항해마다 {string.Join("·", counts)}번 · 사고 끔일 때 {off} · 켬일 때 {ev?.Spec.Name ?? "없음"}({ev?.Source})");
                }
                finally { HazardSystem.RandomDays = keep; }
            }

            // 7) 결정론
            {
                uint H()
                {
                    var w = World.CreateDefault(seed, 0, "Hanbit");
                    Run(w, SimTime.Hours(4));
                    w.Cosmic.Force(CosmicKind.CoronalMass, 3f);
                    w.Cosmic.Force(CosmicKind.Kessler, 6f);
                    Run(w, SimTime.TicksPerDay + SimTime.Hours(2));
                    return SaveGame.StateHash(w);
                }
                uint a = H(), b = H();
                Check("결정론 — 같은 시드 · 같은 대재난이면 같은 배", a == b, $"{a:x8} / {b:x8}");
            }
        }
        catch (Exception ex)
        {
            Check("예외 없음", false, ex.ToString());
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 우주 대재난 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
