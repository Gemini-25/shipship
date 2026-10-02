using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ShipSim.Core;

// v16.15 승무원 두뇌 2.0: 믿음 · 목표 층 · 계획 · 감정 · 사회적 추론 · 배우기 · 성격 · 말 · 주 컴퓨터와 잇기
public static partial class Program
{
    private static void BrainTrait(CrewMember c, string name, float v)
    {
        if (name == "Calm") c.Traits.Calm = v;
        else typeof(Personality).GetProperty(name)!.SetValue(c.Traits, v);
    }

    private static void BrainPut(World w, CrewMember c, Room r, int k = 0)
    {
        c.EndJob(w, ToilStatus.Interrupted);
        var cells = r.Cells.Where(w.Ship.IsOpenFloor).ToList();
        c.Position = cells[(k * 3) % cells.Count].Center;
        c.PreviousPosition = c.Position;
        c.Pose = Pose.Standing;
        c.Path = null;
        c.NextThinkTick = w.Tick + 1;
    }

    private static World BrainDay(int seed, string ship, float hour = 10f)
    {
        var w = DayOne(seed, ship);
        Run(w, SimTime.Hours(hour));
        return w;
    }

    private static int RunBrainTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"승무원 두뇌 2.0 점검 (v16.15) · 시드 {seed}\n");
        bool debug = Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "1";
        try
        {
            // ── 1) 같은 정전에서 다섯 사람이 믿는 것 · 목표 · 감정 · 성격대로 다르게 + 틀린 믿음으로 엉뚱한 방 ──
            {
                var w = BrainDay(seed, "Hanbit");
                var adults = w.Crew.Where(c => c.CanAct && !c.IsChild && !c.Outside).ToList();
                // 한 회로의 방들 (복도 · 원자로 · 발전 쪽 빼고)
                var byCircuit = w.Ship.LiveRooms.Where(r => r.Type is not (RoomType.Corridor or RoomType.Reactor or RoomType.Power or RoomType.Cooling or RoomType.Bridge) && r.Cells.Count(w.Ship.IsOpenFloor) >= 4)
                    .GroupBy(r => r.Circuit).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First();
                int circ = byCircuit.Key;
                var darkRooms = byCircuit.OrderBy(r => r.Id).Take(3).ToList();
                var sparkyA = adults.FirstOrDefault(c => c.Role is CrewRole.Electrician or CrewRole.Engineer) ?? adults[0];
                var others = adults.Where(c => c != sparkyA && c.Role is not (CrewRole.Electrician or CrewRole.Engineer or CrewRole.Technician)).ToList();
                if (others.Count < 5) others = adults.Where(c => c != sparkyA).ToList();
                var (A, B, C, D, E) = (sparkyA, others[0], others[1], others[2], others[3]);
                var F = others[4]; // C가 아끼는 사람 — 실제로는 밝은 방에 있다
                // 성격 · 가치관 · 두려움 · 신뢰 (같은 정전, 다른 사람)
                BrainTrait(A, "Bravery", 0.85f); BrainTrait(A, "Diligence", 0.7f); BrainTrait(A, "Calm", 0.6f); A.Value = CrewValue.Efficiency; A.Fears.Clear();
                BrainTrait(B, "Bravery", 0.1f); BrainTrait(B, "Calm", 0.15f); BrainTrait(B, "Diligence", 0.3f); B.Value = CrewValue.Safety; if (!B.Fears.Contains(Fear.Dark)) B.Fears.Add(Fear.Dark);
                BrainTrait(C, "Sociability", 0.95f); BrainTrait(C, "Bravery", 0.5f); BrainTrait(C, "Calm", 0.5f); C.Value = CrewValue.People; C.Fears.Clear();
                BrainTrait(D, "Bravery", 0.45f); BrainTrait(D, "Calm", 0.5f); BrainTrait(D, "Sociability", 0.3f); D.Value = CrewValue.Rules; D.Fears.Clear();
                w.Automation.Trusts.Change(D, 0.9f - w.Automation.Trusts.Of(D), "시험");
                BrainTrait(E, "Calm", 0.95f); BrainTrait(E, "Diligence", 0.95f); BrainTrait(E, "Bravery", 0.5f); BrainTrait(E, "Sociability", 0.2f); E.Value = CrewValue.Efficiency; E.Fears.Clear();
                foreach (var x in new[] { A, B, D, E }) foreach (var o in w.Crew) if (o != x) x.Affinity[o.Id] = Math.Min(x.AffinityTo(o), 0.1f);
                foreach (var o in w.Crew) if (o != C && o != F) C.Affinity[o.Id] = Math.Min(C.AffinityTo(o), 0.1f);
                C.Affinity[F.Id] = 0.85f;
                // 자리: 다섯은 캄캄해질 방에, F는 다른 회로의 밝은 방에 — C는 F가 (옛 정보로) 캄캄해질 방에 있다고 믿는다
                BrainPut(w, A, darkRooms[0], 0); BrainPut(w, B, darkRooms[0], 1); BrainPut(w, C, darkRooms[1 % darkRooms.Count], 0); BrainPut(w, D, darkRooms[2 % darkRooms.Count], 0); BrainPut(w, E, darkRooms[1 % darkRooms.Count], 1);
                var lit = w.Ship.LiveRooms.Where(r => r.Circuit != circ && r.Type is RoomType.Mess or RoomType.Lounge or RoomType.Quarters or RoomType.Hydroponics or RoomType.Workshop && r.Cells.Count(w.Ship.IsOpenFloor) >= 4).OrderBy(r => r.Id).First();
                BrainPut(w, F, lit, 0);
                var staleRoom = darkRooms[2 % darkRooms.Count];
                foreach (var x in new[] { A, B, C, D, E }) x.Needs.Stress = 0.2f;
                Run(w, 2);
                w.Brain2.Beliefs.Learn(C, Topic.Person, F.Id, staleRoom.Id, BeliefSource.Seen, 0.95f);
                // 정전: 배전반 차단기가 떨어진다
                var panel = w.Ship.FurnitureOf(FurnitureType.PowerPanel).First().Machine!;
                panel.Faults.Add(new Fault { Kind = FaultKind.BreakerTrip, Since = w.Tick, Circuit = circ });
                var five = new[] { A, B, C, D, E };
                var labels = new Dictionary<int, string>();
                var rooms = new Dictionary<int, HashSet<string>>();
                bool cWentStale = false;
                for (int m = 0; m < 30; m++)
                {
                    Run(w, SimTime.Minutes(0.5f));
                    foreach (var x in five)
                    {
                        if (!rooms.TryGetValue(x.Id, out var hs)) rooms[x.Id] = hs = new HashSet<string>();
                        if (x.Room != null) hs.Add(x.Room.Name);
                        if (m == 6) labels[x.Id] = x.Job?.Label ?? "대기";
                    }
                    if (C.Room == staleRoom && F.Room != staleRoom) cWentStale = true;
                }
                bool dark = darkRooms.All(r => r.Dark);
                var st = w.Brain2.Plans.Stances;
                string Stance(CrewMember x) => st.TryGetValue(x.Id, out var s) ? $"{LearningSystem.Name(s.m)} — {s.why}" : "(안 고름)";
                var picks = five.Where(x => st.ContainsKey(x.Id)).Select(x => st[x.Id].m).ToList();
                int distinct = picks.Distinct().Count();
                Check("같은 정전 — 다섯 사람이 성격 · 믿음 · 감정 · 목표대로 다른 길을 고른다",
                    picks.Count == 5 && distinct >= 4,
                    $"방 {string.Join("·", darkRooms.Select(r => r.Name))} 캄캄 {dark} · " + string.Join(" | ", five.Select(x => $"{x.Name}({CrewRoles.Name(x.Role)}): {Stance(x)} → {labels.GetValueOrDefault(x.Id, "?")}")));
                var cBook = w.Brain2.Beliefs.Of(C);
                var cPlan = w.Brain2.Plans.Past.Concat(w.Brain2.Plans.Active).Where(p => p.Owner == C.Id && p.Kind == PlanKind.Outage).ToList();
                bool corrected = cBook.Corrected > 0 && (w.Brain2.Beliefs.Get(C, Topic.Person, F.Id)?.Value ?? -9) != staleRoom.Id;
                Check("틀린 믿음 — 옛 정보로 엉뚱한 방에 가서 보고 믿음을 고친다",
                    cWentStale && corrected,
                    $"{C.Name}: {F.Name}이(가) {staleRoom.Name}에 있다고 믿음(옛 정보) → 다녀간 방 {string.Join("·", rooms.GetValueOrDefault(C.Id) ?? new())} · 고침 {cBook.Corrected} ({cBook.LastCorrectionText}) · 계획 {string.Join(" / ", cPlan.SelectMany(p => p.Trail))}");
                Run(w, SimTime.Minutes(20));
                var aPlans = w.Brain2.Plans.Past.Concat(w.Brain2.Plans.Active).Where(p => p.Owner == A.Id).ToList();
                Check("정전 — 믿는 원인으로 움직인다 (차단기라 믿은 사람이 배전반에서 올린다 · 컴퓨터를 믿는 사람은 묻는다)",
                    !panel.Faults.Any(f => f.Kind == FaultKind.BreakerTrip && f.Circuit == circ) && (w.Brain2.Plans.Breakers > 0 || aPlans.Count > 0 || A.Job?.Order?.Kind == WorkKind.ResetBreaker),
                    $"차단기 올림(스스로) {w.Brain2.Plans.Breakers} · {A.Name}: {string.Join(" / ", aPlans.SelectMany(p => p.Trail))} · {D.Name}의 정전 원인 믿음 {(w.Brain2.Beliefs.Get(D, Topic.Outage, darkRooms[2 % darkRooms.Count].Id) is Belief db ? w.Brain2.Beliefs.Describe(db) + " · " + BeliefSystem.SourceName(db.Src) : "없음")}");
                if (debug) foreach (var x in five) Console.WriteLine($"    {x.Name} 후보: {string.Join(" · ", st.GetValueOrDefault(x.Id).options?.Select(o => $"{LearningSystem.Name(o.m)} {o.s:0.00}") ?? Array.Empty<string>())}");
            }

            // ── 2) 한 번 실패한 방법은 다음엔 피한다 (차단기인 줄 알았는데 발전 쪽 → 다음 정전엔 다른 길) ──
            {
                var w = BrainDay(seed, "Hanbit");
                var a = w.Crew.FirstOrDefault(c => c.CanAct && c.Role is CrewRole.Electrician) ?? w.Crew.First(c => c.CanAct && c.Role is CrewRole.Engineer or CrewRole.Technician);
                var room = w.Ship.LiveRooms.Where(r => r.Type is not (RoomType.Corridor or RoomType.Reactor or RoomType.Power or RoomType.Cooling or RoomType.Bridge or RoomType.LifeSupport) && r.Cells.Count(w.Ship.IsOpenFloor) >= 4)
                    .OrderBy(r => r.Id).First();
                BrainTrait(a, "Bravery", 0.8f); BrainTrait(a, "Calm", 0.7f); BrainTrait(a, "Diligence", 0.6f); a.Fears.Clear(); a.Value = CrewValue.Freedom;
                w.Automation.Trusts.Change(a, 0.2f - w.Automation.Trusts.Of(a), "시험");
                foreach (var o in w.Crew) if (o != a) a.Affinity[o.Id] = Math.Min(a.AffinityTo(o), 0.1f);
                Method Outage(int round)
                {
                    BrainPut(w, a, room, round);
                    Run(w, 2);
                    room.LightsOut = true; room.LightsOutSince = w.Tick; // 조명 고장 (차단기가 아니다)
                    Method? pick = null;
                    for (int m = 0; m < 40 && pick == null; m++)
                    {
                        Run(w, SimTime.Minutes(0.5f));
                        if (w.Brain2.Plans.Current(a) is CrewPlan p && p.Kind == PlanKind.Outage) pick = p.Method;
                    }
                    Run(w, SimTime.Minutes(25));
                    room.LightsOut = false;
                    Run(w, SimTime.Minutes(40)); // 불이 들어오고 마음을 가라앉힌다
                    return pick ?? Method.CarryOn;
                }
                var first = Outage(0);
                var tally = w.Brain2.Learning.Tally(a, Method.ResetBreaker);
                var second = Outage(1);
                var trail = string.Join(" / ", w.Brain2.Plans.Past.Where(p => p.Owner == a.Id && p.Kind == PlanKind.Outage).SelectMany(p => p.Trail));
                Check("배우기 — 한 번 헛걸음한 방법(차단기)은 다음 정전에 피한다",
                    first == Method.ResetBreaker && tally.bad >= 1f && second != Method.ResetBreaker,
                    $"{a.Name}({CrewRoles.Name(a.Role)}) 처음 {LearningSystem.Name(first)} → 실패 {tally.bad:0.#} → 다음 {LearningSystem.Name(second)} · 치우침 {w.Brain2.Learning.Bias(a, Method.ResetBreaker):0.00} · {trail}");
                int demos = w.Brain2.Learning.Demos;
                Check("배우기 — 남의 시범 (같은 방에서 본 사람도 조금 배운다) · 출처 신뢰", demos >= 0 && w.Brain2.Learning.Records > 0, $"기록 {w.Brain2.Learning.Records} · 시범 {demos} · {string.Join(" · ", w.Brain2.Learning.Lines(a))}");
            }

            // ── 3) 불을 본 사람이 모르는 사람에게 알리러 간다 (경보가 닿지 않는 방) ──
            {
                var w = BrainDay(seed, "Mirinae");
                w.Policies.Set("inertfire", 0, "시험");
                w.Policies.Set("vacuumfire", 0, "시험");
                w.Policies.Set("command", 0, "시험"); // 지휘 · 무전이 없는 배: 본 사람만 알릴 수 있다
                var room = StoreRoom(w);
                ClearRoom(w, room);
                foreach (var l in w.Net.Links.Where(l => l.Kind == NetKind.Data && (l.Door?.RoomA == room || l.Door?.RoomB == room)).ToList()) w.Net.Hurt(l, 1f, "시험");
                Run(w, SimTime.Minutes(1));
                var awake = w.Crew.Where(c => c.CanAct && c.IsAwake && !c.IsChild).ToList();
                var witness = awake[0];
                BrainTrait(witness, "Sociability", 0.95f); BrainTrait(witness, "Bravery", 0.25f); witness.Value = CrewValue.People; witness.Fears.Clear();
                int Gap(Room r) => Math.Abs(r.Cells[0].X - room.Cells[0].X) + Math.Abs(r.Cells[0].Y - room.Cells[0].Y);
                var far = w.Ship.LiveRooms.Where(r => r != room && r.Type is not (RoomType.Corridor or RoomType.Airlock) && r.Cells.Count(w.Ship.IsOpenFloor) >= 3
                        && !r.Doors.Any(d => d.RoomA == room || d.RoomB == room) && Gap(r) >= 6).OrderBy(Gap).ThenBy(r => r.Id).First();
                var friend = awake.Skip(1).First();
                witness.Affinity[friend.Id] = 0.8f;
                BrainPut(w, friend, far, 0);
                friend.HoldUntil = w.Tick + SimTime.Hours(1); friend.HoldWhy = "시험 — 그 방에서 기다림";
                Run(w, 3);
                w.Brain2.Beliefs.Learn(witness, Topic.Person, friend.Id, far.Id, BeliefSource.Seen, 1f, -1, 0);
                BrainPut(w, witness, room, 0);
                BigFire(w, room, 3);
                bool told = false;
                string fsrc = "";
                for (int m = 0; m < 40 && !told && w.Brain2.Social.AlreadyKnew == 0; m++)
                {
                    Run(w, SimTime.Minutes(0.5f));
                    var fb = w.Brain2.Beliefs.Get(friend, Topic.Fire, room.Id);
                    if (fb != null && fb.Src == BeliefSource.Told) { told = true; fsrc = $"{BeliefSystem.SourceName(fb.Src)} · {w.Brain2.Beliefs.CrewById(fb.From)?.Name}"; }
                    if (debug && m % 2 == 0)
                    {
                        var te = witness.LastEvaluations.FirstOrDefault(e => e.Activity is TellActivity);
                        var top = witness.LastEvaluations.FirstOrDefault();
                        var wb = w.Brain2.Beliefs.Get(witness, Topic.Fire, room.Id);
                        Console.WriteLine($"    {m * 0.5f}분 {witness.Name}: {witness.Job?.Label} @{witness.Room?.Name} · 알리기 {te.Score:0.00} ({te.Reason}) · 1위 {top.Activity?.Label} {top.Score:0.00} ({top.Reason}) · 믿음 {(wb != null ? $"{BeliefSystem.SourceName(wb.Src)} 경보{wb.Alarmed}" : "-")} · {friend.Name} 앎 {(friend.Mind.Knows.TryGetValue($"fire:{room.Id}", out var fk) ? MindSystem.SourceName(fk.src) : "-")} · 데이터선 {room.DataLinked}");
                    }
                }
                var wp = w.Brain2.Plans.Past.Concat(w.Brain2.Plans.Active).Where(p => p.Owner == witness.Id && p.Kind == PlanKind.Tell).ToList();
                bool friendKnows = friend.Mind.Knows.ContainsKey($"fire:{room.Id}");
                Check("알리기 — 불을 본 사람이 모르는 사람에게 알리러 간다 (경보가 안 닿는 방)",
                    w.Brain2.Social.Tells + w.Brain2.Social.AlreadyKnew > 0 && wp.Count > 0 && (told || friendKnows),
                    $"{witness.Name} → 알림 {w.Brain2.Social.Tells} (이미 앎 {w.Brain2.Social.AlreadyKnew}) · {far.Name}까지 · 계획 {string.Join(" / ", wp.SelectMany(p => p.Trail).Take(4))} · {friend.Name}: 믿음 출처 {fsrc} · Mind 앎 {friendKnows} · 설득 {w.Brain2.Social.Persuasions}");
                var toldWho = wp.FirstOrDefault()?.For ?? friend;
                var wbel = w.Brain2.Beliefs.Get(witness, Topic.Fire, room.Id);
                var guess = wbel != null && w.Brain2.Social.ThinksKnows(witness, toldWho, wbel);
                var tb = w.Brain2.Beliefs.Get(toldWho, Topic.Fire, room.Id);
                Check("사회적 추론 — 알려 준 사람은 이제 안다고 짐작한다 (그 사람의 믿음에는 출처 · 누구에게서가 남는다)", guess && tb != null && tb.Value == 1,
                    $"{witness.Name}의 짐작: {toldWho.Name}도 안다 = {guess} · {toldWho.Name}의 믿음 {(tb != null ? $"{w.Brain2.Beliefs.Describe(tb)} · {BeliefSystem.SourceName(tb.Src)}{(tb.From >= 0 ? $" ({w.Brain2.Beliefs.CrewById(tb.From)?.Name})" : "")}" : "없음")}");
            }

            // ── 4) 틀린 믿음(소문) — 불이 났다고 믿는 방에 가서 보고 고친다 · 컴퓨터를 믿는 사람은 손목 단말로 먼저 고친다 ──
            {
                var w = BrainDay(seed, "Mirinae");
                var room = StoreRoom(w);
                var crew = w.Crew.Where(c => c.CanAct && c.IsAwake && !c.IsChild && c.Room != room).ToList();
                var s1 = crew[0];
                var teller = crew[1];
                BrainTrait(s1, "Bravery", 0.85f); BrainTrait(s1, "Diligence", 0.8f); s1.Fears.Clear();
                w.Automation.Trusts.Change(s1, 0.08f - w.Automation.Trusts.Of(s1), "시험");
                w.Brain2.Beliefs.Learn(s1, Topic.Fire, room.Id, 1, BeliefSource.Rumor, 0.85f, teller.Id);
                s1.NextThinkTick = w.Tick + 1;
                bool reached = false;
                for (int m = 0; m < 40 && !reached; m++) { Run(w, SimTime.Minutes(0.5f)); if (s1.Room == room) reached = true; }
                Run(w, SimTime.Minutes(3));
                var b1 = w.Brain2.Beliefs.Get(s1, Topic.Fire, room.Id);
                var (ok, bad) = w.Brain2.Learning.Tally(s1, Method.CheckFire);
                Check("틀린 믿음 — 헛소문을 믿고 그 방에 가 보고 믿음을 고친다 (출처를 덜 믿게 된다)",
                    reached && b1 != null && b1.Value == 0 && b1.Src == BeliefSource.Seen && bad >= 1f,
                    $"{s1.Name}: {room.Name} 불(소문 · {teller.Name}) → 도착 {reached} → 지금 {(b1 != null ? w.Brain2.Beliefs.Describe(b1) + " · " + BeliefSystem.SourceName(b1.Src) : "?")} · 헛걸음 {bad:0.#} · 소문 신뢰 {w.Brain2.Learning.SourceTrust(s1, BeliefSource.Rumor):0.00} · 말 「{s1.Said}」");
                // 컴퓨터를 믿는 사람: 가는 길에 손목 단말로 "불이 없다" — 믿는 만큼 듣는다
                var s2 = crew[2];
                BrainTrait(s2, "Bravery", 0.85f); BrainTrait(s2, "Diligence", 0.8f); s2.Fears.Clear();
                w.Automation.Trusts.Change(s2, 0.95f - w.Automation.Trusts.Of(s2), "시험");
                var far = w.Ship.LiveRooms.Where(r => r != room && r.Type != RoomType.Corridor).OrderByDescending(r => Math.Abs(r.Cells[0].X - room.Cells[0].X) + Math.Abs(r.Cells[0].Y - room.Cells[0].Y)).First();
                BrainPut(w, s2, far, 0);
                Run(w, 2);
                w.Brain2.Beliefs.Learn(s2, Topic.Fire, room.Id, 1, BeliefSource.Rumor, 0.85f, teller.Id);
                int heeded0 = w.Brain2.Beliefs.NudgesHeeded;
                bool s2reached = false;
                for (int m = 0; m < 30; m++) { Run(w, SimTime.Minutes(0.5f)); if (s2.Room == room) s2reached = true; }
                var b2 = w.Brain2.Beliefs.Get(s2, Topic.Fire, room.Id);
                Check("주 컴퓨터 — 승무원의 믿음을 짐작해 틀린 길을 손목 단말로 바로잡는다 (믿는 만큼 듣는다)",
                    w.Brain2.Beliefs.NudgesHeeded > heeded0 && b2 != null && b2.Value == 0 && !s2reached,
                    $"{s2.Name}(신뢰 {w.Automation.Trusts.Of(s2) * 100:0}%) 알림 {w.Brain2.Beliefs.Nudges} · 들음 {w.Brain2.Beliefs.NudgesHeeded} · 도착 {s2reached} · {(b2 != null ? w.Brain2.Beliefs.Describe(b2) + " · " + BeliefSystem.SourceName(b2.Src) : "?")}");
            }

            // ── 5) 계획이 막히면 다른 길: 부품 없음(믿었던 선반이 비었다) → 작업대 · 재료도 없으면 원정 제안 · 솜씨가 모자라면 부탁 ──
            {
                var w = BrainDay(seed, "Hanbit");
                var pump = w.Ship.FurnitureOf(FurnitureType.CoolantPump).First();
                var part = ItemKind.Bearing;
                foreach (var f in w.Ship.Containers) { int n = f.Storage!.Count(part); if (n > 0) f.Storage.Take(part, n); }
                var shelf = w.Ship.Containers.Where(f => f.Type == FurnitureType.Shelf && f.Storage!.Accepts(part)).OrderBy(f => f.Id).First();
                var worker = w.Crew.Where(c => c.CanAct && c.IsAwake && !c.IsChild && c.SkillLevel(Skill.Mechanics) >= 0.3f).OrderByDescending(c => c.SkillLevel(Skill.Mechanics)).First();
                BrainTrait(worker, "Diligence", 0.95f);
                w.Brain2.Beliefs.Learn(worker, Topic.Item, (int)part, shelf.Id, BeliefSource.Seen, 1f, -1, 2); // 어제 본 대로: 저 선반에 베어링 둘
                w.Machines.Break(pump.Machine!, FaultKind.BearingWear);
                Run(w, SimTime.Minutes(1));
                var order = w.Board.Open.FirstOrDefault(o => o.Kind == WorkKind.Repair && o.Target.Furniture == pump && o.Fault == FaultKind.BearingWear);
                CrewPlan? plan = order != null ? w.Brain2.Plans.StartFix(worker, order) : null;
                worker.NextThinkTick = w.Tick + 1;
                for (int m = 0; m < 16 && plan is { Done: false }; m++) Run(w, SimTime.Minutes(30));
                string steps = plan == null ? "계획 없음" : string.Join(" / ", plan.Trail);
                bool fetchFailed = plan != null && plan.Steps.Any(s => s.Kind == StepKind.Fetch && s.State == StepState.Failed);
                bool crafted = plan != null && plan.Steps.Any(s => s.Kind == StepKind.Craft && s.State == StepState.Done);
                bool handed = plan != null && plan.Steps.Any(s => s.Kind == StepKind.Handoff && s.State == StepState.Done);
                var wb = w.Brain2.Beliefs.Get(worker, Topic.Item, (int)part);
                Check("계획 — 믿었던 선반에 부품이 없다 → 믿음을 고치고 작업대에서 만들어 고칠 자리에 둔다",
                    fetchFailed && crafted && handed,
                    $"{worker.Name}: {steps} · 베어링 믿음 {(wb != null ? w.Brain2.Beliefs.Describe(wb) : "?")} · 만든 수 {w.Brain2.Plans.Crafted}");
                // 재료(금속판)마저 없다 → 원정 제안 (중기 목표 → 원정 이야기를 꺼내는 마음)
                var w2 = BrainDay(seed, "Hanbit");
                var pump2 = w2.Ship.FurnitureOf(FurnitureType.CoolantPump).First();
                foreach (var f in w2.Ship.Containers) foreach (var k in new[] { part, ItemKind.Plate }) { int n = f.Storage!.Count(k); if (n > 0) f.Storage.Take(k, n); }
                var worker2 = w2.Crew.Where(c => c.CanAct && c.IsAwake && !c.IsChild && c.SkillLevel(Skill.Mechanics) >= 0.3f).OrderByDescending(c => c.SkillLevel(Skill.Mechanics)).First();
                w2.Machines.Break(pump2.Machine!, FaultKind.BearingWear);
                Run(w2, SimTime.Minutes(1));
                float init0 = w2.Expedition.Initiative(worker2, MatCat.Parts);
                var order2 = w2.Board.Open.FirstOrDefault(o => o.Kind == WorkKind.Repair && o.Target.Furniture == pump2);
                var plan2 = order2 != null ? w2.Brain2.Plans.StartFix(worker2, order2) : null;
                worker2.NextThinkTick = w2.Tick + 1;
                for (int m = 0; m < 12 && plan2 is { Done: false }; m++) Run(w2, SimTime.Minutes(20));
                float init1 = w2.Expedition.Initiative(worker2, MatCat.Parts);
                Check("계획 — 재료도 없으면 원정을 꺼낸다 (중기 목표 → 원정 제안 의지)",
                    plan2 != null && plan2.Steps.Any(s => s.Kind == StepKind.Trip && s.State == StepState.Done) && w2.Brain2.Goals.Has(worker2, "trip") && init1 > init0,
                    $"{worker2.Name}: {(plan2 == null ? "계획 없음" : string.Join(" / ", plan2.Trail))} · 원정 의지 {init0:0.00} → {init1:0.00}");
                // 솜씨가 모자라면 잘하는 사람에게 부탁 (일 나누기)
                var w3 = BrainDay(seed, "Hanbit");
                var pump3 = w3.Ship.FurnitureOf(FurnitureType.CoolantPump).First();
                foreach (var f in w3.Ship.Containers) { int n = f.Storage!.Count(part); if (n > 0) f.Storage.Take(part, n); }
                var clumsy = w3.Crew.Where(c => c.CanAct && c.IsAwake && !c.IsChild).OrderBy(c => c.SkillLevel(Skill.Mechanics)).First();
                for (int i = 0; i < clumsy.SkillLevels.Length; i++) clumsy.SkillLevels[i] = Math.Min(clumsy.SkillLevels[i], 0.1f);
                var expert = w3.Crew.Where(c => c != clumsy && c.CanAct && c.IsAwake && !c.IsChild).OrderByDescending(c => c.SkillLevel(Skill.Mechanics)).First();
                clumsy.Affinity[expert.Id] = 0.6f; expert.Affinity[clumsy.Id] = 0.5f;
                BrainPut(w3, expert, w3.Ship.RoomsOf(RoomType.Mess).First(), 0); expert.HoldUntil = w3.Tick + SimTime.Hours(1); expert.HoldWhy = "시험 — 식당에서 기다림";
                Run(w3, 2);
                w3.Brain2.Beliefs.Learn(clumsy, Topic.Person, expert.Id, w3.Ship.RoomsOf(RoomType.Mess).First().Id, BeliefSource.Seen, 1f);
                w3.Brain2.Beliefs.Learn(clumsy, Topic.Item, (int)part, -1, BeliefSource.Seen, 1f);
                w3.Machines.Break(pump3.Machine!, FaultKind.BearingWear);
                Run(w3, SimTime.Minutes(1));
                var order3 = w3.Board.Open.FirstOrDefault(o => o.Kind == WorkKind.Repair && o.Target.Furniture == pump3);
                var plan3 = order3 != null ? w3.Brain2.Plans.StartFix(clumsy, order3) : null;
                for (int m = 0; m < 12 && w3.Brain2.Plans.HelpAsked == 0; m++) Run(w3, SimTime.Minutes(5));
                var help = w3.Brain2.Plans.Active.Concat(w3.Brain2.Plans.Past).FirstOrDefault(p => p.Kind == PlanKind.Help);
                Check("사회적 추론 — 솜씨가 모자라면 믿는 자리에 있는 잘하는 사람에게 부탁한다 (일 나누기)",
                    w3.Brain2.Plans.HelpAsked > 0 && help is { Kind: PlanKind.Help },
                    $"{clumsy.Name}: {(plan3 == null ? "계획 없음" : string.Join(" / ", plan3.Trail))} · 맡은 사람 {(help != null ? w3.Brain2.Beliefs.CrewById(help.Owner)?.Name : "-")}: {help?.Goal ?? "없음"}");
            }

            // ── 6) 감정이 생기고 가라앉는다 · 판단을 바꾼다 (두려움 → 대피 쪽 · 분노 → 덜 따름 · 자부심) ──
            {
                var w = BrainDay(seed, "Mirinae");
                var c = w.Crew.First(x => x.CanAct && x.IsAwake && !x.IsChild);
                c.Fears.Clear();
                var emo = w.Brain2.Emotions;
                var room = StoreRoom(w);
                w.Brain2.Beliefs.Learn(c, Topic.Fire, room.Id, 1, BeliefSource.Seen, 1f);
                float fear0 = emo.Get(c, Feeling.Fear);
                float tilt0 = emo.Tilt(c, ActCat.Survival);
                Run(w, SimTime.Hours(3));
                float fear1 = emo.Get(c, Feeling.Fear);
                c.Stats.Rescues++;
                Run(w, SimTime.Minutes(1));
                float pride0 = emo.Get(c, Feeling.Pride);
                Run(w, SimTime.Hours(20));
                float pride1 = emo.Get(c, Feeling.Pride);
                float obey0 = w.Minds.Obedience(c);
                emo.Feel(c, Feeling.Anger, 0.7f, "시험 — 억울한 비난");
                float obey1 = w.Minds.Obedience(c);
                Check("감정 — 사건으로 생기고 성격대로 가라앉는다 (두려움 · 자부심)",
                    fear0 >= 0.15f && fear1 < fear0 * 0.3f && pride0 >= 0.2f && pride1 < pride0 * 0.6f,
                    $"{c.Name}: 불을 봄 두려움 {fear0:0.00} → 3시간 {fear1:0.00} · 구함 자부심 {pride0:0.00} → 20시간 {pride1:0.00}");
                Check("감정 → 판단 — 두려우면 대피 쪽 점수가 오르고, 화나면 명령을 덜 듣는다 (Mind 분노와 같은 값)",
                    tilt0 > 1.05f && obey1 < obey0 && Math.Abs(c.Mind.Anger - emo.Get(c, Feeling.Anger)) < 0.001f,
                    $"대피 쪽 기울기 {tilt0:0.00} · 복종 {obey0:0.00} → 화난 뒤 {obey1:0.00} · 분노 {c.Mind.Anger:0.00}");
                // 말 · 일기: 감정 · 믿음 · 목표가 묻어난다
                Run(w, SimTime.Hours(0.5f));
                string? phrase = emo.Phrase(c);
                var w2 = BrainDay(seed, "Hanbit", 12f);
                Run(w2, SimTime.Hours(11));
                int diaries = w2.Brain2.Diaries;
                var sample = w2.Crew.SelectMany(x => x.Diary).Where(d => d.text.Contains("오늘은") || d.text.Contains("요즘 마음") || d.text.Contains("언젠가")).Select(d => d.text).FirstOrDefault();
                Check("말 — 대사 · 일기가 감정 · 믿음 · 목표를 담는다", phrase != null && diaries > 0 && sample != null,
                    $"「{phrase}」 · 일기 {diaries}편 · 예: {sample}");
            }

            // ── 7) 믿음 공통 API — 흩어진 "아는 것"이 한 곳으로 (Mind.Knows · 방송 · 엿들음 · 소문) · 방송은 믿는 만큼 ──
            {
                var w = BrainDay(seed, "Mirinae");
                var room = StoreRoom(w);
                ClearRoom(w, room);
                var hi = w.Crew.First(c => c.CanAct && c.IsAwake && !c.IsChild);
                var lo = w.Crew.First(c => c != hi && c.CanAct && c.IsAwake && !c.IsChild);
                w.Automation.Trusts.Change(hi, 0.95f - w.Automation.Trusts.Of(hi), "시험");
                w.Automation.Trusts.Change(lo, 0.1f - w.Automation.Trusts.Of(lo), "시험");
                BigFire(w, room, 2);
                Run(w, World.SystemInterval * 2);
                w.Automation.Speak.Announce("시험 — 창고 화재", room, 2);
                Run(w, World.SystemInterval * 2);
                float ch = w.Brain2.Beliefs.Conf(hi, Topic.Fire, room.Id), cl = w.Brain2.Beliefs.Conf(lo, Topic.Fire, room.Id);
                var hb = w.Brain2.Beliefs.Get(hi, Topic.Fire, room.Id);
                int mindAgree = w.Crew.Count(c => c.Mind.Knows.ContainsKey($"fire:{room.Id}") && w.Brain2.Beliefs.Believes(c, Topic.Fire, room.Id));
                int mindTotal = w.Crew.Count(c => c.Mind.Knows.ContainsKey($"fire:{room.Id}"));
                Check("믿음 공통 API — 사고를 아는 사람(Mind.Knows)은 모두 믿음 장부에도 · 출처 · 확신 · 시각이 남는다",
                    mindTotal > 0 && mindAgree == mindTotal && hb != null,
                    $"Mind 앎 {mindTotal} · 믿음도 {mindAgree} · {hi.Name}: {(hb != null ? $"{w.Brain2.Beliefs.Describe(hb)} · {BeliefSystem.SourceName(hb.Src)} · {hb.Conf:0.00}" : "?")} · 들여옴 {w.Brain2.Beliefs.Imports}");
                var (gh, ghWhy) = w.Brain2.Beliefs.ComputerGuess(hi, Topic.Fire, room.Id);
                var sleeper = w.Crew.FirstOrDefault(c => !c.Dead && c.Pose == Pose.Sleeping);
                var unaware = w.Brain2.Beliefs.ComputerThinksUnaware(Topic.Fire, room.Id);
                Check("주 컴퓨터 ↔ 믿음 — 방송은 신뢰만큼 믿고, 컴퓨터는 누가 알고 모를지 짐작한다 (읽기 API)",
                    gh >= 0.5f && (sleeper == null || unaware.Contains(sleeper) || w.Automation.Speak.Recent.Last().HeardBy.Contains(sleeper.Id)),
                    $"화재 믿음 확신: 믿는 {hi.Name} {ch:0.00} · 못 믿는 {lo.Name} {cl:0.00} · 컴퓨터 짐작({hi.Name}) {gh:0.00} {ghWhy} · 모를 것 같은 사람 {string.Join("·", unaware.Select(c => c.Name))}");
                // 대피는 믿음으로: 위험하다고 믿는 방은 실제로 괜찮아도 피한다
                var safe = w.Ship.RoomsOf(RoomType.Mess).First();
                w.Brain2.Beliefs.Learn(lo, Topic.Air, safe.Id, 1, BeliefSource.Rumor, 0.9f);
                Check("대피 — 위험하다고 믿는 방(소문)은 실제로 괜찮아도 대피처로 고르지 않는다",
                    !w.Brain2.Beliefs.SafeEnough(lo, safe) && w.Brain2.Beliefs.SafeEnough(hi, safe), $"{lo.Name}: {safe.Name} 공기 나쁨(소문) → 피함 · {hi.Name}: 괜찮다");
            }

            // ── 8) 목표 층 · 승무원 카드 (읽기 전용) ──
            {
                var w = BrainDay(seed, "Hanbit");
                Run(w, SimTime.Hours(1));
                var c = w.Crew.First(x => !x.Dead && !x.IsChild);
                var longs = w.Brain2.Goals.Layer(c, GoalLayer.Long).ToList();
                var shorts = w.Brain2.Goals.Short(c);
                float tiltBefore = w.Brain2.Goals.Tilt(c, longs.First().Cat);
                Check("목표 층 — 장기(꿈) · 중기 · 단기가 있고 장기 목표가 그 행동 종류의 점수를 기울인다",
                    longs.Count > 0 && tiltBefore > 1.05f,
                    $"{c.Name}: 꿈 {string.Join(" · ", longs.Select(g => $"{g.Text}({g.Why})"))} · 중기 {string.Join(" · ", w.Brain2.Goals.Layer(c, GoalLayer.Mid).Select(g => g.Text))} · 단기 {string.Join(" · ", shorts.Select(g => g.Text))} · 기울기 {tiltBefore:0.00}");
                w.Brain2.Beliefs.Learn(c, Topic.Fire, StoreRoom(w).Id, 1, BeliefSource.Rumor, 0.7f);
                w.Brain2.Emotions.Feel(c, Feeling.Joy, 0.5f, "시험");
                var card = CrewWhy.Brain2(c, w);
                uint h0 = SaveGame.StateHash(w);
                CrewWhy.Brain2(c, w);
                Check("승무원 카드 — 믿음 · 목표 · 계획 · 감정이 보인다 (읽기 전용 · 지문 불변)",
                    card.Beliefs.Count > 0 && card.Goals.Count > 0 && card.Emotions.Count == 6 && SaveGame.StateHash(w) == h0,
                    $"믿음 {card.Beliefs.Count} (예: {card.Beliefs.FirstOrDefault().Text}) · 목표 {card.Goals.Count} · 계획 {card.Plan.Count} · 감정 {string.Join(" ", card.Emotions.Select(e => $"{EmotionSystem.Name(e.f)} {e.v:0.0}"))} · 배움 {card.Lessons.Count}");
            }

            // ── 9) 결정론 ──
            {
                uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
                uint a = H(), b = H();
                Check("결정론 — 같은 시드 같은 지문", a == b, $"{a:x8} / {b:x8}");
            }

            // ── 10) 성능: 30명 하루 (두뇌 2.0 끔 ↔ 켬) ──
            {
                double Time(bool on, out int beliefs, out long thinks)
                {
                    BrainSystem.Enabled = on;
                    var w = World.CreateDefault(seed, 0, "Cheonma");
                    long t0 = Brain.ThinkCount;
                    var sw = Stopwatch.StartNew();
                    Run(w, SimTime.TicksPerDay);
                    sw.Stop();
                    thinks = Brain.ThinkCount - t0;
                    beliefs = w.Brain2.Beliefs.Books.Sum(x => x.book.Count);
                    BrainSystem.Enabled = true;
                    return sw.Elapsed.TotalSeconds;
                }
                if (debug)
                {
                    Prof.On = true; Prof.Reset();
                    var pw = World.CreateDefault(seed, 0, "Cheonma");
                    Run(pw, SimTime.TicksPerDay);
                    Prof.On = false;
                    foreach (var (key, ms, calls, _) in Prof.Report().Where(r => r.key.Contains("brain2") || r.key is "score.plan" or "score.outage" or "score.firebelief" or "score.tell" or "crew.think" or "sys.Daily").OrderByDescending(r => r.ms))
                        Console.WriteLine($"    {key}: {ms:0}ms ({calls})");
                }
                double off = Math.Min(Time(false, out _, out long th0), Time(false, out _, out _));
                double on = Math.Min(Time(true, out int beliefs, out long th1), Time(true, out _, out _));
                double ratio = on / Math.Max(0.001, off);
                Check("성능 — 30명 하루가 예전 두뇌보다 크게 늘지 않는다 (나눠 보기 · 바뀔 때만 계획)", ratio < 1.3,
                    $"천마 30명 하루: 끔 {off:0.0}초 · 켬 {on:0.0}초 (×{ratio:0.00}) · 판단 {th0} → {th1} · 믿음 {beliefs}줄");
            }
        }
        finally { BrainSystem.Enabled = true; }
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
