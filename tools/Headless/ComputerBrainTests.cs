using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ShipSim.Core;

// v16.16 주컴퓨터 두뇌 2.0: 계획자 · 며칠 앞 예측(확신 · 계기 믿음) · 승무원 모형 · 협상 · 권한 · 윤리 · 실수와 사과 · 결정론 · 성능
public static partial class Program
{
    /// <summary>물 재생기를 멈추고(고칠 부품이 없다) 물탱크를 사흘 치만 남긴다 — 컴퓨터가 믿는 흐름으로.</summary>
    private static float BrainDrought(World w, float days)
    {
        foreach (var f in w.Ship.FurnitureOf(FurnitureType.WaterRecycler).ToList())
            if (!f.Machine!.Stopped) w.Machines.Break(f.Machine, FaultKind.Wrecked);
        float use = MathF.Max(0.5f, w.Water.Consumed); // 재생기가 멎으면 쓰는 만큼 준다
        w.Water.Level = MathF.Min(w.Water.Capacity, w.Water.Capacity * 0.15f + days * 24f * use);
        Run(w, World.SystemInterval * 2);
        BrainBlockRepairs(w);
        return use;
    }

    /// <summary>재생기를 아무도 못 고치게 막으며 돌린다 (5분씩).</summary>
    private static void RunDry(World w, long ticks)
    {
        for (long t = 0; t < ticks; t += SimTime.Minutes(5)) { Run(w, Math.Min(SimTime.Minutes(5), ticks - t)); BrainBlockRepairs(w); }
    }

    private static void BrainBlockRepairs(World w)
    {
        foreach (var o in w.Board.Open.Where(o => o.Target.Furniture?.Type == FurnitureType.WaterRecycler).ToList()) w.Board.Block(o, "시험: 펌프 부품이 없다", 72f);
    }

    private static List<CrewMember> BrainVoters(World w) => w.Crew.Where(c => !c.Dead && !c.IsChild && c.CanAct).ToList();

    private static void BrainTrust(World w, float to)
    {
        foreach (var c in w.Crew.Where(c => !c.Dead)) w.Automation.Trusts.Change(c, to - w.Automation.Trusts.Of(c), "시험", quiet: true);
    }

    private static int RunComputerBrainTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"주컴퓨터 두뇌 2.0 점검 (v16.16) · 시드 {seed}\n");
        var sw = Stopwatch.StartNew();
        try
        {
            // 1) 사흘 뒤 물 부족 예측 → 계획 고침(고장) → 회의 안건 → 회의가 반대 → 반대한 사람에게 설명 → 근거를 바꿔 다시 → 통과
            {
                var w = DayOne(seed, "Mirinae");
                var a = w.Automation;
                Run(w, SimTime.Hours(4));
                var plan0 = a.Planner.PlanOf("water");
                string mode0 = plan0?.Mode ?? "(없음)";
                int revs0 = a.Planner.Revisions.Count;
                float use = BrainDrought(w, 3f);
                RunDry(w, SimTime.Minutes(20));
                var plan = a.Planner.PlanOf("water");
                var rev = a.Planner.Revisions.Skip(revs0).FirstOrDefault(r => r.Key == "water");
                Check("계획 고침 — 물 재생기가 멎자 물 계획이 \"유지\"에서 바뀌고 까닭(고장)이 남는다", mode0 == "유지" && rev != null && rev.Why.Contains("고장") && plan != null && plan.Mode is "대책" or "위기",
                    rev != null ? $"{rev.From} → {rev.To} · {rev.Why}" : $"처음 {mode0} · 지금 {plan?.Mode} · 고침 {a.Planner.Revisions.Count - revs0}");
                var fc = a.Outlook.Get("water");
                Check("앞날 예측 — \"사흘 뒤 물 부족 (확신 n%)\" (며칠 뒤 · 확신도)", fc != null && fc.DaysToShort is > 2.3f and < 3.7f && fc.Line.Contains("사흘 뒤") && fc.Confidence is > 0.05f and < 0.95f,
                    fc != null ? $"{fc.Line} · {fc.Basis} · 하루 소모 {use * 24f:0}L" : "예측 없음");
                var p1 = a.Planner.Pitches.LastOrDefault(p => p.Key == "water");
                Check("계획이 실제 안건으로 — 원정(또는 절수)을 다음 회의 안건으로 올리고 방송한다 (권한: 자원 = 제안)", p1 != null && p1.Via == "회의" && p1.Open && a.Speak.Recent.Any(b => b.Text.Contains("회의에 올린다")),
                    p1 != null ? $"[{p1.Via}] {p1.Option} · 근거({p1.Arg}): {p1.Basis}" : "안건 없음");
                // 회의: 컴퓨터를 반쯤 믿는 사람들 — 첫 안건은 반대
                BrainTrust(w, 0.42f);
                w.Meetings.Hold(MeetingKind.Regular, BrainVoters(w), "시험 회의 1");
                Check("회의가 반대한다 — 첫 안건 부결 · 반대한 사람의 까닭이 남는다", p1 != null && p1.State == "부결" && p1.Objectors.Count > 0,
                    p1 != null ? $"{p1.State} (찬 {p1.Yes} · 반 {p1.No}) · 반대 까닭: {string.Join(" / ", p1.Objectors.Take(3).Select(o => $"{w.Crew[o.id].Name}: {o.why}"))}" : "");
                var expl = a.Apps.Messages.Where(m => m.Kind == "설명").ToList();
                Check("반대한 사람에게 설명한다 (개인 메시지 · 설명을 들은 사람은 다음에 조금 더 듣는다)", expl.Count > 0 && a.Planner.Explanations > 0,
                    expl.Count > 0 ? $"{expl.Count}통 · {w.Crew[expl[0].CrewId].Name}: {expl[0].Text}" : "설명 없음");
                Pitch? win = null;
                for (int round = 0; round < 3 && win == null; round++)
                {
                    RunDry(w, SimTime.Hours(2.2f));
                    var open = a.Planner.OpenPitch("water");
                    if (open == null) continue;
                    w.Meetings.Hold(MeetingKind.Regular, BrainVoters(w), $"시험 회의 {round + 2}");
                    if (open.State == "통과") win = open;
                }
                var all = a.Planner.Pitches.Where(p => p.Key == "water").ToList();
                win ??= all.LastOrDefault(p => p.State == "통과" && p != p1); // 정기 회의가 먼저 정했을 수도 있다
                Check("근거를 바꿔 다시 설득 — 다른 근거 갈래(사례 · 대안 · 가치)로 다시 올려 통과 · 세 번까지만", win != null && p1 != null && win.Arg != p1.Arg && win.Basis != p1.Basis && win.Attempt <= ShipPlanner.MaxAttempts,
                    string.Join(" → ", all.Select(p => $"{p.Attempt}:{p.Option}/{p.Arg}/{p.State}({p.Yes}:{p.No} {string.Join(",", p.Objectors.Take(3).Select(o => o.why))})")) + (win != null ? $" · 통과 근거: {win.Basis}" : ""));
                bool acted = win != null && (win.Option is "절수" or "엄격 절수" ? w.Policies["water"] >= 1 : w.Expedition.Pending != null || w.Expedition.Current != null || w.Expedition.Past.Count > 0);
                Check("통과한 대책을 실제로 한다 (방침 물 · 원정)", acted, $"방침 물 {w.Policies["water"]} · 원정 {(w.Expedition.Current != null ? "출발" : w.Expedition.Pending != null ? "기다림" : "없음")} · 계획: {a.Planner.PlanOf("water")?.Action}");
                Check("배우기 — 거절된 대책은 다음에 덜 고른다 (같은 상황 · 다른 조치)", a.Planner.Strategy.TryGetValue("water:short", out var st) && st.Values.Any(s => s.Rejected > 0) && (win == null || p1 == null || win.Option != p1.Option || win.Arg != p1.Arg),
                    string.Join(" · ", a.Planner.Strategy.GetValueOrDefault("water:short")?.Select(kv => $"{kv.Key} 시도 {kv.Value.Tries} 받음 {kv.Value.Accepted} 거절 {kv.Value.Rejected}") ?? Array.Empty<string>()));
                Console.WriteLine($"    말투: {a.Authority.Persona} · 배운 것: {string.Join(" / ", a.Authority.LearnedList.TakeLast(3).Select(l => l.Text))}");
            }

            // 2) 권한: 회의가 자동 실행을 준다 → 권한 안에서 직접 · 틀린 조치 → 사과 · 신뢰 일부 회복 → 스스로 권한 축소를 청한다 → 회의가 거둔다 → 제안만
            {
                var w = DayOne(seed, "Mirinae");
                var a = w.Automation;
                w.Policies.Set("expedition", 2, "시험: 원정 없이");
                Run(w, SimTime.Hours(3));
                a.Authority.QueueReview(Domain.Resources, AuthLevel.Auto, "시험: 자원 대책을 맡겨 보자");
                BrainTrust(w, 0.72f);
                w.Meetings.Hold(MeetingKind.Regular, BrainVoters(w), "시험 회의 — 권한");
                Check("회의가 권한을 준다 — 자원: 제안 → 자동 실행", a.Authority.Granted0(Domain.Resources) == AuthLevel.Auto,
                    $"자원 {ComputerAuthority.LevelName(a.Authority.Granted0(Domain.Resources))} · 바뀜 {a.Authority.Changes.Count} · {a.Authority.Changes.LastOrDefault()?.Why}");
                BrainDrought(w, 3f);
                a.Planner.Force("시험: 물 재생기 고장");
                Run(w, World.SystemInterval * 2);
                var auto = a.Planner.Pitches.LastOrDefault(p => p.Key == "water");
                Check("권한 안에서만 자동 실행 — 묻지 않고 방침 물을 \"아낀다\"로 · 방송", auto != null && auto.Via == "자동" && w.Policies["water"] >= 1 && a.Asks.Pending("plan:water") == null,
                    auto != null ? $"[{auto.Via}] {auto.Option} · 방침 물 {w.Policies["water"]} · {a.Planner.PlanOf("water")?.Action}" : "없음");
                // 틀린 조치: 계기 값이 낮은 채로 멈췄다 (방사선 손상) — 실제 물탱크는 가득
                foreach (var f in w.Ship.FurnitureOf(FurnitureType.WaterRecycler)) f.Machine!.Faults.Clear();
                w.Policies.Set("water", 0, "시험");
                w.Water.Level = w.Water.Capacity;
                Run(w, SimTime.Hours(6.5f));
                var g = ShipForecast.Models.First(m => m.Key == "water").Gauge(w)!;
                w.Water.Level = w.Water.Capacity * 0.16f;
                a.Outlook.Update(force: true);
                a.Belief.Break(g, SensorFault.Stuck, "시험: 방사선 데이터 손상");
                w.Water.Level = w.Water.Capacity;
                foreach (var c in w.Crew.Where(c => c.Room == g).ToList()) Put(w, c, w.Ship.RoomsOf(RoomType.Mess).First());
                BrainDrought(w, 9f);
                w.Water.Level = w.Water.Capacity;
                a.Planner.Force("시험: 다시 고장");
                Run(w, World.SystemInterval * 2);
                var bad = a.Planner.Pitches.LastOrDefault(p => p.Key == "water");
                float before = a.Trusts.Average();
                RunDry(w, SimTime.Hours(7));
                var mk = a.Authority.Mistakes.LastOrDefault();
                Check("틀린 조치를 인정 — 사과 방송 · 되돌리기", bad != null && bad.Via == "자동" && mk != null && a.Speak.Recent.Any(b => b.Text.Contains("사과")) && w.Policies["water"] == 0,
                    mk != null ? $"{mk.What} · 까닭: {mk.Cause} · {mk.Fix} · 방침 물 {w.Policies["water"]}" : $"실수 없음 · 조치 {bad?.Via}/{bad?.Option}/{bad?.State} · 믿은 {bad?.BelievedDays:0.0}일 · 실제 {bad?.TrueDays:0.0}일");
                Check("신뢰가 일부 회복 — 틀려서 떨어졌다가 사과로 조금 돌아온다 (전부는 아니다)", mk != null && mk.TrustLow < mk.TrustBefore - 0.01f && mk.TrustAfter > mk.TrustLow + 0.005f && mk.TrustAfter < mk.TrustBefore,
                    mk != null ? $"{mk.TrustBefore * 100:0.0}% → {mk.TrustLow * 100:0.0}% → {mk.TrustAfter * 100:0.0}% (사과 들은 사람 {mk.Heard}) · 조치 전 {before * 100:0.0}%" : "");
                Check("실수 뒤 스스로 권한 축소를 청한다 (회의 안건)", a.Authority.Reviews.Any(r => r.Domain == Domain.Resources && r.BySelf && r.To == AuthLevel.Propose),
                    string.Join(" / ", a.Authority.Reviews.Select(r => $"{r.Domain} → {r.To}: {r.Why}")));
                w.Meetings.Hold(MeetingKind.Regular, BrainVoters(w), "시험 회의 — 권한 거두기");
                Check("회의가 권한을 거둔다 — 자원: 자동 실행 → 제안", a.Authority.Granted0(Domain.Resources) == AuthLevel.Propose,
                    $"자원 {ComputerAuthority.LevelName(a.Authority.Granted0(Domain.Resources))} · {a.Authority.Changes.LastOrDefault()?.Why}");
                // 같은 물 부족이 또 와도 이제는 제안만 (방침을 직접 바꾸지 않는다)
                a.Belief.Of(g).Fault = SensorFault.None;
                foreach (var f in w.Ship.FurnitureOf(FurnitureType.WaterRecycler)) f.Machine!.Faults.Clear();
                Run(w, SimTime.Hours(2.2f));
                w.Policies.Set("water", 0, "시험");
                BrainDrought(w, 3f);
                a.Planner.Force("시험: 세 번째 고장");
                RunDry(w, SimTime.Hours(2.2f));
                var ask = a.Planner.Pitches.LastOrDefault(p => p.Key == "water");
                Check("권한을 거두면 제안만 — 같은 상황에서 직접 하지 않고 안건 · 제안으로", ask != null && ask != bad && ask.Via is "회의" or "함장" && w.Policies["water"] == 0,
                    ask != null ? $"[{ask.Via}] {ask.Option} · {ask.State} · 방침 물 {w.Policies["water"]}" : "없음");
            }

            // 3) 감지기 하나가 자꾸 틀리면 덜 믿는다 · 교정을 부른다 · 피곤한 사람에게 일을 덜 맡긴다 · 위기 계획 고침 · 사생활 ↔ 안전
            {
                var w = DayOne(seed, "Mirinae");
                var a = w.Automation;
                a.Install(ComputerModule.BioMonitor);
                a.Install(ComputerModule.Roster);
                Run(w, SimTime.Hours(9)); // 모형이 서도록 (하루 넘게 본 사람에게만 부탁한다)
                var m = ShipForecast.Models.First(x => x.Key == "water");
                var g = m.Gauge(w)!;
                var L = a.Outlook.Ledger("water");
                float conf0 = a.Outlook.Get("water")?.Confidence ?? 0f;
                float rel0 = L.Reliability;
                foreach (var f in g.Furniture) if (f.Machine is Machine mm) mm.SensorCal = 0.35f;
                var eye = w.Crew.Where(c => !c.Dead && !c.IsChild && c.CanAct).OrderBy(c => c.Id).First();
                for (int i = 0; i < 14 && !L.Suspect; i++)
                {
                    Put(w, eye, g);
                    eye.Needs.Rest = MathF.Max(eye.Needs.Rest, 0.8f);
                    Run(w, 2);
                    a.Outlook.Update(force: true);
                }
                var fc = a.Outlook.Get("water");
                Check("감지기가 자꾸 틀리면 덜 믿는다 — 사람 눈금과 어긋날 때마다 계기 믿음 ↓ · 확신 ↓ · 흐름으로 셈한다", L.Disagree >= 3 && L.Suspect && L.Reliability < rel0 && fc != null && fc.Confidence < conf0 && fc.DeadReckon,
                    $"계기 믿음 {rel0 * 100:0}% → {L.Reliability * 100:0}% (어긋남 {L.Disagree} · {L.LastWhy}) · 확신 {conf0 * 100:0}% → {fc?.Confidence * 100:0}% · 추측 항법 {fc?.DeadReckon}");
                Run(w, SimTime.Minutes(12));
                var cal = w.Board.Open.FirstOrDefault(o => o.Kind == WorkKind.Calibrate && o.Target.Room == g);
                Check("의심하는 계기는 교정을 부른다 (교정 작업 · 그 방 감지기 믿음도 낮춘다 → 위험한 조치 전에 사람을 먼저)", cal != null && a.Belief.Of(g).Trust < 0.5f && a.Authority.LearnedList.Any(l => l.Text.Contains("덜 믿는다")),
                    $"교정 작업 {(cal != null ? cal.Detail : "없음")} · 방 감지기 믿음 {a.Belief.Of(g).Trust * 100:0}%");
                w.Watch.Calibrate(g, eye);
                a.Outlook.Update(force: true);
                Check("교정하면 다시 믿는다 (사람이 계기를 맞추면 어긋난 기록을 반쯤 지운다)", !L.Suspect && a.Authority.LearnedList.Any(l => l.Text.Contains("다시 믿는다")),
                    $"계기 믿음 {L.Reliability * 100:0}% · {a.Authority.LearnedList.LastOrDefault(l => l.Kind == "계기")?.Text}");
                // 피곤한 사람
                var adults = w.Crew.Where(c => !c.Dead && !c.IsChild && c.CanAct).OrderBy(c => c.Id).ToList();
                var tired = adults[1];
                var fresh = adults[2];
                var team0 = a.Planner.Emergency.FireTeam.ToList();
                var teamMember = team0.Count > 0 ? w.Crew.First(c => c.Id == team0[0]) : tired;
                foreach (var t in new[] { tired, teamMember }) { t.Needs.Rest = 0.1f; }
                fresh.Needs.Rest = 0.95f;
                tired.CoveringUntil = w.Tick + SimTime.Hours(3); // 근무 중 (지쳤는데 일하는 사람)
                a.CrewModel.Update(force: true);
                foreach (var t in new[] { tired, teamMember }) { t.Needs.Rest = 0.1f; }
                int crisisRevs = a.Planner.Emergency.Revisions;
                a.Planner.Force("시험: 사람이 지쳤다");
                Run(w, World.SystemInterval * 2);
                var o = w.Board.Open.FirstOrDefault(x => x.Urgency < 0.6f);
                float bt = o != null ? a.CrewModel.RequestBias(tired, o) : 0f, bf = o != null ? a.CrewModel.RequestBias(fresh, o) : 0f;
                a.Apps.Roster.Clear();
                foreach (var t in new[] { tired, teamMember }) { t.Needs.Rest = 0.1f; }
                a.Apps.MakeRoster(SimTime.Day(w.Tick));
                int dutiesTired = a.Apps.Roster.Count(s => s.CrewId == tired.Id);
                Check("피곤한 사람에게 일을 덜 맡긴다 — 모형이 지친 걸 보고 쉬라고 부탁 · 당번표에서 뺀다 · 일 고르기에서 밀린다", a.CrewModel.RestNow(tired) < 0.3f && a.CrewModel.RestAsked(tired) && dutiesTired == 0 && (o == null || bt < bf) && a.CrewModel.Best(Skill.Mechanics) != tired,
                    $"{tired.Name} 짐작 기력 {a.CrewModel.RestNow(tired) * 100:0}% · 쉬라는 부탁 {a.CrewModel.RestAsked(tired)} · 당번 {dutiesTired} · 일 끌림 {bt:+0.00;-0.00} (↔ {fresh.Name} {bf:+0.00;-0.00}) · {a.CrewModel.Summary(tired)}");
                var teamNow = a.Planner.Emergency.FireTeam;
                Check("위기 대응 계획도 고친다 — 소화조가 지치면 다른 사람으로", team0.Count == 0 || !teamNow.Contains(teamMember.Id) && a.Planner.Emergency.Revisions > crisisRevs,
                    $"소화조 {string.Join("·", team0.Select(id => w.Crew[id].Name))} → {string.Join("·", teamNow.Select(id => w.Crew[id].Name))} · {a.Planner.Revisions.LastOrDefault(r => r.Key == "crisis")?.Why}");
                // 싫어하는 당번 (투덜댐을 듣고 배운다)
                // 가장 싫어하는 당번을 가진 사람 (참는 성격은 빼고) — 그 당번을 네 번 맡긴다
                var (grump, duty) = adults.Where(c => !c.Habits.Contains(Habit.Patient) && !c.Habits.Contains(Habit.Follower))
                    .SelectMany(c => CrewModelBook.Duties.Select(d => (c, d))).OrderByDescending(x => CrewModelBook.TrueAversion(x.c, x.d)).ThenBy(x => x.c.Id).First();
                if (grump.Room == null || !grump.Room.DataLinked) { Put(w, grump, w.Ship.RoomsOf(RoomType.Mess).First()); Run(w, 2); }
                float av0 = a.CrewModel.Of(grump).Aversion0(duty);
                for (int i = 0; i < 4; i++) a.CrewModel.OnDuty(grump, duty);
                float av = a.CrewModel.Of(grump).Aversion0(duty);
                var calm = adults.Where(c => c != grump).OrderBy(c => CrewModelBook.TrueAversion(c, duty)).First();
                for (int i = 0; i < 4; i++) a.CrewModel.OnDuty(calm, duty);
                Check("승무원 습관을 배운다 — 싫은 당번에 투덜대면 싫어한다고 짐작하고 · 군말 없는 사람은 괜찮다고 짐작한다 (짐작은 다음 당번표에 쓴다)",
                    CrewModelBook.TrueAversion(grump, duty) > 0.55f && av > av0 + 0.15f && av - a.CrewModel.Of(calm).Aversion0(duty) > 0.2f,
                    $"{duty}: {grump.Name} 실제 {CrewModelBook.TrueAversion(grump, duty) * 100:0}% · 짐작 {av0 * 100:0}% → {av * 100:0}% (투덜댐 {a.CrewModel.Of(grump).Grumbles.GetValueOrDefault(duty)}) ↔ {calm.Name} 실제 {CrewModelBook.TrueAversion(calm, duty) * 100:0}% · 짐작 {a.CrewModel.Of(calm).Aversion0(duty) * 100:0}%");
                // 사생활 ↔ 안전
                w.Policies.Set("privacy", 1, "시험");
                var q = w.Ship.RoomsOf(RoomType.Quarters).FirstOrDefault();
                var sleeper = adults[3];
                if (q != null)
                {
                    Put(w, sleeper, q);
                    Run(w, 2);
                    var prof = a.CrewModel.Of(sleeper);
                    prof.RestSeen = w.Tick - SimTime.Hours(16);
                    prof.Rest = 0.2f;
                    a.CrewModel.Update(force: true);
                }
                var eth = a.Authority.Ethics.LastOrDefault(e => e.Kind == "사생활 ↔ 안전");
                Check("윤리 갈등 — 사생활 ↔ 안전: 개인 공간 방침이어도 오래 못 본 지친 사람은 생체 신호만 보고 기록 · 알린다", q == null || eth != null && a.Apps.Messages.Any(mm => mm.Kind == "사생활" && mm.CrewId == sleeper.Id),
                    eth != null ? $"{eth.Situation} · {eth.Choice} · {eth.Basis}" : "없음");
            }

            // 4) 긴 항해: 저절로 돌려도 조르지 않고(자원마다 사흘에 세 번까지) · 계획 · 예측 · 모형이 쌓인다
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                Run(w, SimTime.TicksPerDay * 4);
                var a = w.Automation;
                var pl = a.Planner;
                int worst = ShipForecast.Models.Max(m => pl.Pitches.Count(p => p.Key == m.Key && w.Tick - p.Tick < SimTime.TicksPerDay * 3));
                int seen = w.Crew.Count(c => a.CrewModel.Knows(c));
                Check("긴 항해 — 나흘 동안 계획 · 예측 · 승무원 모형이 쌓이고, 같은 자원으로 사흘에 세 번 넘게 조르지 않는다",
                    pl.Replans >= 40 && a.Outlook.Samples >= 40 && a.Outlook.Graded > 20 && seen >= w.Crew.Count(c => !c.Dead) / 2 && worst <= ShipPlanner.MaxAttempts,
                    $"다시 세움 {pl.Replans} · 예측 {a.Outlook.Samples}번 (채점 {a.Outlook.Graded} · 맞힘 {a.Outlook.Hits}) · 본 사람 {seen}/{w.Crew.Count(c => !c.Dead)} · 안건/제안 {pl.Pitches.Count} (자원마다 최대 {worst}) · 고침 {pl.Revisions.Count} · 부탁 {a.CrewModel.WorkAsks} · 쉼 {a.CrewModel.RestAsks} · 사과 {a.Authority.Apologies} · 배움 {a.Authority.LearnedList.Count}");
                Console.WriteLine($"    계획: {string.Join(" / ", pl.Plans.Select(p => $"{p.Name} {p.Mode}"))} · 예측: {string.Join(" / ", a.Outlook.All.Select(f => f.Line))}");
                foreach (var l in a.Authority.LearnedList.TakeLast(4)) Console.WriteLine($"    배움 {SimTime.Day(l.Tick)}일 {SimTime.Clock(l.Tick)} [{l.Kind}] {l.Text}");
                foreach (var p in pl.Pitches.TakeLast(4)) Console.WriteLine($"    안건 {SimTime.Day(p.Tick)}일 {p.Option} [{p.Arg} · {p.Via}] {p.State} — {p.Basis}");
                foreach (var r in pl.Revisions.TakeLast(6)) Console.WriteLine($"    고침 {SimTime.Day(r.Tick)}일 {SimTime.Clock(r.Tick)} [{r.Key}] {r.From} → {r.To} · {r.Why}");
            }

            // 5) 결정론 · 성능 (30인 배)
            {
                uint H()
                {
                    var w = World.CreateDefault(seed, 0, "Hanbit");
                    Run(w, SimTime.TicksPerDay + SimTime.Hours(6));
                    return SaveGame.StateHash(w);
                }
                AutomationSystem.BrainStopwatchTicks = 0;
                var t0 = Stopwatch.GetTimestamp();
                uint h1 = H();
                long total = Stopwatch.GetTimestamp() - t0;
                long brain = AutomationSystem.BrainStopwatchTicks;
                uint h2 = H();
                Check("결정론 — 같은 시드 두 번 같은 지문", h1 == h2, $"{h1:x8} / {h2:x8}");
                float share = brain / (float)Math.Max(1, total);
                Check("성능 — 두뇌 2.0은 간격으로만 돈다 (30인 배 하루 반 동안 전체 시간의 3% 아래)", share < 0.03f, $"두뇌 {brain * 1000.0 / Stopwatch.Frequency:0}ms / 전체 {total * 1000.0 / Stopwatch.Frequency:0}ms ({share * 100:0.00}%)");
            }
        }
        catch (Exception e)
        {
            Check("예외 없이", false, e.ToString());
        }
        Console.WriteLine($"\n{(_fails == 0 ? "모두 통과" : $"실패 {_fails}")} · {sw.Elapsed.TotalSeconds:0.0}초");
        return _fails == 0 ? 0 : 1;
    }
}
