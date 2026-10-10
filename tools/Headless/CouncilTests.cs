using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ShipSim.Core;

// v18.18 승무원이 스스로 여는 회의 · 파벌 · 재판 · 선거
public static partial class Program
{
    private static bool CouncilUntil(World w, Func<bool> done, long maxTicks, int step = 60)
    {
        for (long t = 0; t < maxTicks; t += step)
        {
            Run(w, step);
            if (done()) return true;
        }
        return done();
    }

    /// <summary>배의 먹을 것을 며칠치로 줄인다 (식사 · 비상식량 · 채소를 덜어 낸다).</summary>
    private static void TrimFood(World w, float days)
    {
        int crew = w.Crew.Count(c => !c.Dead);
        foreach (var k in new[] { ItemKind.Produce, ItemKind.Meal, ItemKind.Ration })
            while (FoodPolicy.FoodDays(w) > days && w.Ship.CountStored(k) > 0) Life.Take(w, k, 1);
    }

    private static int RunCouncilTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"승무원이 여는 회의 · 파벌 · 재판 · 선거 점검 (v18.18) · 시드 {seed}\n");
        var minutes = new List<string>();
        string only = Environment.GetEnvironmentVariable("SHIPSIM_SCENE") ?? "";
        bool Do(string k) => only == "" || only.Contains(k);

        // ── 1) 배급을 줄이자: 먹을 것이 줄자 누군가 스스로 안건을 내고, 서명을 받으러 다니고, 정기 회의에서 파벌이 갈린다
        World w1 = DayOne(seed, "Hanbit");
        Motion? rat = null;
        {
            var w = w1;
            TrimFood(w, 3.5f);
            foreach (var f in w.Ship.FurnitureOf(FurnitureType.GrowBed).Where((_, i) => i % 3 != 0)) f.Machine!.Crop = null; // 작물 셋 중 둘이 시들었다
            // 일하는 사람 절반은 배가 고프다
            // 통합7 두 무리는 저녁 정기 회의(19시) 때 깨어 있는 사람끼리 번갈아 나눈다 — 번호 홀짝으로만 나누면 배고픈 쪽이 그 시각 자거나 당직이라 반대표가 한 장만 남는 날이 있었다
            var meetUp = w.Crew.Where(c => !c.Dead && !c.IsChild && !SimTime.InWindow(w.Meetings.Hour + 0.5f, c.Schedule.SleepStart, c.Schedule.SleepLength) && !SimTime.InWindow(w.Meetings.Hour + 0.5f, c.Schedule.WorkStart, c.Schedule.WorkLength)).OrderBy(c => c.Id).ToList();
            var hungrySide = new HashSet<int>(meetUp.Where((c, i) => i % 2 == 0).Select(c => c.Id).Concat(w.Crew.Where(c => !meetUp.Contains(c) && c.Id % 2 == 0).Select(c => c.Id)));
            foreach (var c in w.Crew.Where(c => !c.Dead && !c.IsChild && hungrySide.Contains(c.Id))) c.Needs.Food = 0.3f;
            // 통합6 두 무리를 분명히 세운다 (그날그날 배고픔 · 일정으로 표가 한쪽으로 쏠리지 않게): 배고픈 쪽은 자유를, 창고를 걱정하는 쪽은 안전을 중히 여긴다
            foreach (var c in w.Crew.Where(c => !c.Dead && !c.IsChild)) c.Value = hungrySide.Contains(c.Id) ? CrewValue.Freedom : CrewValue.Safety;
            int walked = 0;
            var asked = new HashSet<int>();
            long readyAt = -1;
            int signersAtReady = 0;
            bool heldEarly = false;
            for (int i = 0; i < 60 * 40 && (rat == null || rat.Decided < 0); i++)
            {
                Run(w, 60);
                rat ??= w.Motions.All.FirstOrDefault(m => m.Policy == "rations" && m.To == 3);
                foreach (var c in w.Crew.Where(c => !c.Dead && !c.IsChild && hungrySide.Contains(c.Id))) c.Needs.Food = MathF.Min(c.Needs.Food, 0.35f); // 요즘 몫이 적어 늘 배가 고프다 (통합6 먹고 와도 금방 — 회의 때도 배고픈 채로)
                foreach (var c in w.Crew.Where(c => !c.Dead && !c.IsChild && !hungrySide.Contains(c.Id))) c.Needs.Food = MathF.Max(c.Needs.Food, 0.8f); // 통합6 다른 쪽은 아직 배가 부르다 (창고만 걱정한다)
                if (rat == null) { if (i % 30 == 0) TrimFood(w, 3.5f); continue; }
                // 통합7 의논하는 동안에도 '사흘치 남짓'은 그대로 둔다 — 한쪽을 시험이 매시간 배고프게 하니 끼니마다 먹어 회의 날엔 창고가 0일치가 되고, 그러면 진 쪽도 다툴 거리가 없어 불만이 안 남았다
                if (i % 30 == 0 && rat.Decided < 0 && FoodPolicy.FoodDays(w) < 2.5f)
                    foreach (var box in w.Ship.Containers.Where(f => f.Storage!.Accepts(ItemKind.Ration)).OrderBy(f => f.Id))
                    {
                        while (FoodPolicy.FoodDays(w) < 3f && box.Storage!.Add(ItemKind.Ration, 1) > 0) { }
                        if (FoodPolicy.FoodDays(w) >= 3f) break;
                    }
                foreach (var c in w.Crew) if (c.Job?.Activity is PetitionActivity) { walked++; asked.Add(c.Id); }
                if (Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "1" && w.Motions.Now is Sitting dbgS)
                    Console.WriteLine($"     [{SimTime.Clock(w.Tick)}] {dbgS.Motion.Title} {dbgS.Venue.Name} 부름 {dbgS.Invited.Count} · " + string.Join(" | ", w.Crew.Where(c => !c.Dead && !c.IsChild).Select(c => $"{c.Name}{(dbgS.Invited.Contains(c.Id) ? "" : "(안 부름)")} {c.Room?.Name}:{c.ActivityLabel}")));
                if (readyAt < 0 && rat.Stage != MotionStage.Signing) { readyAt = w.Tick; signersAtReady = rat.Signers.Count; }
                if (rat.Decided >= 0 && (readyAt < 0 || rat.Decided < readyAt)) heldEarly = true;
            }
            var prop = rat != null ? w.Crew.First(c => c.Id == rat.Proposer) : null;
            Check("배급 — 먹을 것이 줄자 누군가 스스로 '배급을 줄이자' 안건을 냈다 (동기: 창고 · 남은 날)", rat != null,
                rat != null ? $"{prop!.Name}({CommandSystem.StyleName(CommandSystem.StyleOf(prop))} · {MeetingSystem.ValueName(prop.Value)}): {rat.Why}" : "안건 없음");
            Check("서명 — 종이를 들고 사람을 찾아다니며 서명을 받는다 · 서명이 다 모이기 전엔 회의에 오르지 않는다",
                rat != null && walked > 0 && rat.Asked.Count > rat.Signers.Count - 1 && signersAtReady >= rat.Need && !heldEarly,
                rat != null ? $"찾아다닌 틱 {walked} · 부탁 {rat.Asked.Count - 1}명 · 서명 {rat.Signers.Count}/{rat.Need} · 거절 {rat.Refused.Count} · 회의 전 서명 {signersAtReady}" : "");
            var item = rat?.Item;
            if (item != null) minutes.Add($"[정기 회의] {item.Title} → {item.Outcome} · " + string.Join(" / ", item.Speeches.Select(s => $"{w.Crew.First(c => c.Id == s.Who).Name}: {s.Text}")));
            if (item != null && Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "1")
                foreach (var v in item.Votes) { var c = w.Crew.First(x => x.Id == v.who); Console.WriteLine($"     {c.Name} {MeetingSystem.ValueName(c.Value)} 배고픔 {c.Needs.Hunger:0.00} 식욕 {c.Traits.Appetite:0.00} {c.Role} → {(v.yes ? "찬" : "반")} {rat!.Final.GetValueOrDefault(c.Id):+0.00;-0.00} {v.why}"); }
            var fac = rat?.FactionIds.Select(id => w.Motions.Factions.First(f => f.Id == id)).ToList() ?? new List<Faction>();
            bool split = item != null && item.Yes > 0 && item.No > 0;
            Check("파벌 — 배급 안건에 양쪽이 갈리고, 강하게 선 사람끼리 별명 붙은 파벌이 생긴다", split && fac.Count >= 1,
                item != null ? $"찬성 {item.Yes} · 반대 {item.No} · 파벌: {string.Join(" / ", fac.Select(f => $"{f.Name}({string.Join("·", f.Members.Select(id => w.Crew.First(c => c.Id == id).Name))})"))}" : "결정 없음");
            Check("주 컴퓨터 — 식량 예측을 근거로 내놓지만 표는 없다", item?.Computer != null && item.Votes.All(v => v.who >= 0 && w.Crew.Any(c => c.Id == v.who)) && item.Votes.Count == item.Yes + item.No,
                item != null ? $"{item.Computer} · 표 {item.Votes.Count} (찬 {item.Yes} 반 {item.No})" : "");
            // 진 쪽 불만이 며칠 간다
            if (rat?.Decided >= 0)
            {
                var gr = w.Motions.Grudges.Where(g => g.Motion == rat.Id).ToList();
                var holders = gr.Select(g => w.Crew.First(c => c.Id == g.Who)).ToList();
                float s0 = holders.Count > 0 ? holders.Average(c => c.Needs.Stress) : 0f;
                Run(w, SimTime.TicksPerDay);
                bool still = gr.Count > 0 && gr.All(g => g.Until - g.Since >= SimTime.Hours(48)) && gr.Any(g => g.Until > w.Tick);
                bool diary = holders.Any(c => c.Diary.Any(d => d.Item2.Contains("마음에 걸린다") || d.Item2.Contains("속이 끓는다") || d.Item2.Contains("공기가 다르다") || d.Item2.Contains("더 말했어야")));
                Check("소수파 — 진 쪽은 며칠 불만을 품는다 (하루 뒤에도 남아 일기에 쓴다)", still && diary,
                    $"불만 {gr.Count}명 ({string.Join("·", holders.Select(c => c.Name))}) · 기한 {(gr.Count > 0 ? gr.Min(g => (g.Until - g.Since) / (float)SimTime.TicksPerDay) : 0):0.#}일~ · 일기 {diary}");
                // 다음 안건에서 앙금: 이긴 쪽이 낸 새 안건을 진 쪽이 꺼린다
                var g0 = gr.FirstOrDefault(g => g.Until > w.Tick);
                if (g0 != null && w.Crew.FirstOrDefault(c => c.Id == g0.Against) is CrewMember winner)
                {
                    var holder = w.Crew.First(c => c.Id == g0.Who);
                    w.Motions.Quiet = true;
                    var next = w.Motions.Propose(winner, MotionKind.Practice, SittingKind.Regular, "밥 먹기 전에 손을 씻자", "병이 한 사람씩 옮아 갔다", custom: CustomKind.HandWash);
                    var (s, why) = w.Motions.Opinion(holder, next);
                    // 앙금이 없었다면 — 통합7 그 사람의 앙금을 모두 잠시 걷는다 (하나만 걷으면 다른 안건의 앙금이 대신 잡혀 차이가 0이 됐다)
                    var mine = w.Motions.Grudges.Where(g => g.Who == holder.Id).Select(g => (g, until: g.Until)).ToList();
                    foreach (var (g, _) in mine) g.Until = w.Tick;
                    var (sNo, _) = w.Motions.Opinion(holder, next);
                    foreach (var (g, u) in mine) g.Until = u;
                    next.Stage = MotionStage.Dropped;
                    Check("앙금 — 지난 안건에 진 사람은 이긴 쪽이 낸 다음 안건을 꺼린다", s < sNo - 0.1f, $"{holder.Name} → {winner.Name}의 안건: 앙금 없으면 {sNo:+0.00;-0.00} · 지금 {s:+0.00;-0.00} '{why}'");
                }
                else Check("앙금 — 지난 안건에 진 사람은 이긴 쪽이 낸 다음 안건을 꺼린다", false, "불만 없음");
            }
        }

        // ── 2) 서명이 모여야 회의가 열린다 (긴급 회의) · 모자라면 접힌다
        if (Do("2"))
        {
            var w = DayOne(seed + 1, "Hanbit");
            w.Motions.Quiet = true;
            var c0 = w.Crew.Where(c => !c.Dead && !c.IsChild && c.CanAct).OrderByDescending(c => c.Traits.Sociability).First();
            var bad = w.Motions.Propose(c0, MotionKind.Celebration, SittingKind.Feast, "다 같이 한 끼 — 잔치를 열자", "요즘 다들 얼굴이 굳었다");
            var never = w.Motions.Propose(w.Crew.First(c => c != c0 && !c.Dead && !c.IsChild), MotionKind.RuleChange, SittingKind.Emergency, "물을 엄격하게 아끼자", "물탱크가 마음에 걸린다", "water", 2);
            never.Need = 99;
            long convened = -1; int signersAt = 0; bool earlyOpen = false;
            CouncilUntil(w, () =>
            {
                if (w.Motions.Now is Sitting s && s.Motion == bad && convened < 0) { convened = w.Tick; signersAt = bad.Signers.Count; }
                if (w.Motions.Now is Sitting s2 && s2.Motion == never) earlyOpen = true;
                return bad.Decided >= 0 && never.Stage == MotionStage.Dropped;
            }, SimTime.TicksPerDay * 2, 30);
            Check("소집 — 서명이 다 모여야 잔치 의논이 열리고, 끝내 모자란 안건은 회의 없이 접힌다", convened >= 0 && signersAt >= bad.Need && !earlyOpen && never.Stage == MotionStage.Dropped,
                $"잔치 의논 서명 {signersAt}/{bad.Need} → {(convened >= 0 ? SimTime.Clock(convened) : "안 열림")} · 결과 {bad.Outcome} · 물 안건 {never.Signers.Count}/{never.Need} {never.Stage}");
            var past = w.Motions.Past.LastOrDefault(p => p.Motion == bad);
            if (past != null)
            {
                minutes.Add($"[잔치 의논] " + string.Join(" / ", past.Script.Select(l => $"{(l.Who < 0 ? "주 컴퓨터" : w.Crew.First(c => c.Id == l.Who).Name)}: {l.Text}")));
                Check("모여 앉기 — 따로 연 회의에 부른 사람 대부분이 그 방에 와서 앉고, 말풍선이 차례로 뜬다", past.Present.Count >= 3 && past.Script.Count >= 3,
                    $"{past.Venue.Name} · 부름 {past.Invited.Count} · 온 사람 {past.Present.Count} · 발언 {past.Script.Count} · 손 {past.Hands.Count}/{past.Voters.Count}");
            }
            // 사고 조사: 사람을 잃은 뒤 — 컴퓨터가 그날 기록을 내놓고, 정한 사람에게 책임을 묻는다
            var asker = w.Crew.Where(c => !c.Dead && !c.IsChild && c.CanAct).OrderByDescending(c => c.Value == CrewValue.Safety ? 1 : 0).ThenBy(c => c.Id).First();
            w.History.Add(w, HistoryKind.Casualty, $"{asker.Name}의 동료가 기관실에서 쓰러졌다", null, null);
            var inq = w.Motions.Propose(asker, MotionKind.Crisis, SittingKind.Inquiry, "기관실 일 — 왜 그렇게 됐나", "다시는 이러면 안 된다");
            CouncilUntil(w, () => inq.Decided >= 0 && w.Motions.Now == null, SimTime.TicksPerDay * 2, 30);
            Check("사고 조사 — 서명이 모이면 조사 자리가 열리고, 주 컴퓨터가 그날 기록을 근거로 낸다", inq.Decided >= 0 && inq.Item?.Computer?.Contains("쓰러졌다") == true,
                $"{inq.Title} → {inq.Outcome} · {inq.Item?.Computer}");
        }

        // ── 3) 재판: 배급을 빼돌린 사람 — 본 사람만 증언하고, 처벌을 표결로 정한다 (비밀 투표 뒤 추측)
        if (Do("3"))
        {
            var w = w1;
            w.Motions.Quiet = true;
            if (w.Policies["rations"] != 3) w.Policies.Set("rations", 3, "시험");
            // 자연스럽게: 배고픈 사람이 보는 눈이 없을 때 창고로 간다
            var adults = w.Crew.Where(c => !c.Dead && !c.IsChild && c.CanAct).ToList();
            var thief = adults.Where(c => c.Value != CrewValue.Rules && c.Id != w.Command.CaptainId).OrderBy(c => c.Traits.Diligence).First();
            thief.Needs.Food = 0.25f;
            w.Motions.Tempt(thief, 12f);
            int t0 = w.Motions.Stats.Thefts;
            CouncilUntil(w, () => w.Motions.Stats.Thefts > t0, SimTime.Hours(14), 20);
            var nat = w.Motions.Thefts.LastOrDefault(t => t.Thief == thief.Id);
            Check("빼돌리기 — 배급이 줄어 배고픈 사람이 보는 눈이 없는 틈에 창고에서 꺼내 먹는다", nat != null,
                nat != null ? $"{thief.Name} · {SimTime.Clock(nat.Tick)} {nat.Room} {nat.Item} · 본 사람 {nat.Witnesses.Count} · 결정 위반 {(nat.Breach >= 0 ? "예" : "아니오")}" : "없음");
            // 재판 장면: 이번엔 누가 그 방에 있다 (본 사람과 못 본 사람이 갈린다)
            var box = w.Ship.Containers.Where(f => f.Storage != null && f.Storage.Count(ItemKind.Ration) + f.Storage.Count(ItemKind.Meal) > 0)
                .OrderByDescending(f => adults.Count(c => c != thief && c.IsAwake && c.Room == f.Room)).ThenBy(f => f.Id).First();
            var there = adults.Where(c => c != thief && c.IsAwake && c.Room == box.Room).ToList();
            if (there.Count == 0) { var wit0 = adults.First(c => c != thief && c.IsAwake); there.Add(wit0); }
            var th = w.Motions.Steal(thief, box)!;
            foreach (var o in there) w.Motions.See(o, th);
            var accuser = w.Crew.First(c => c.Id == th.Witnesses.OrderBy(id => w.Crew.First(c => c.Id == id).AffinityTo(thief)).First());
            accuser.ChangeAffinity(thief, -0.4f);
            accuser.Value = CrewValue.Rules; accuser.Needs.Food = MathF.Max(accuser.Needs.Food, 0.8f); // 통합6 본 사람은 정한 몫을 지켜야 한다고 믿는다 (1번 장면의 배고픈 무리에서 뽑혀도 고발할 까닭이 분명하게)
            foreach (var c in adults.Where(c => c != thief && c != accuser)) c.ChangeAffinity(thief, -0.15f);
            w.Motions.Quiet = false;
            Motion? trial = null;
            long dbgAt = 0;
            CouncilUntil(w, () =>
            {
                if (Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "1" && w.Tick >= dbgAt && trial == null)
                {
                    dbgAt = w.Tick + SimTime.Hours(1);
                    var mv = w.Motions.Motive(accuser, true);
                    Console.WriteLine($"     [{SimTime.Clock(w.Tick)}] {accuser.Name} 깨어 {accuser.IsAwake} 행동 {accuser.CanAct} {accuser.Job?.Label} · 위기 {Crisis.Acting(w)} · 열린 안건 {w.Motions.Open.Count()} (내 것 {w.Motions.Open.Count(m => m.Proposer == accuser.Id)}) · 표 금지 {w.Motions.NoVote(accuser)} · 동기 {mv?.s:0.00} · 고발됨 {th.Accused} · 사이 {accuser.AffinityTo(thief):0.00}/{w.Relations.Trust(accuser, thief):0.00} · 최근 안건 {string.Join(",", w.Motions.All.Where(m => m.Proposer == accuser.Id).Select(m => SimTime.Clock(m.Born) + m.Title))}");
                }
                if (Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "1" && w.Tick >= dbgAt && trial != null && w.Crew.FirstOrDefault(c => c.Id == trial.Proposer) is CrewMember pr)
                {
                    dbgAt = w.Tick + SimTime.Hours(1);
                    Console.WriteLine($"     [{SimTime.Clock(w.Tick)}] {pr.Name} {pr.Job?.Label} · {trial.Stage} 서명 {trial.Signers.Count}/{trial.Need} 부탁 {trial.Asked.Count} · " + string.Join(", ", pr.LastEvaluations.OrderByDescending(e => e.Score).Take(4).Select(e => $"{e.Activity.Id}:{e.Score:0.00}")));
                }
                return (trial ??= w.Motions.All.FirstOrDefault(m => m.Kind == MotionKind.Accusation && m.Target == thief.Id)) != null && trial.Decided >= 0 && w.Motions.Now == null;
            }, SimTime.TicksPerDay * 2, 30);
            var past = w.Motions.Past.LastOrDefault(p => p.Motion == trial);
            var witLines = past?.Script.Where(l => l.Role is LineRole.Witness or LineRole.Accuser).ToList() ?? new();
            // v19 도둑이 그 뒤로 또 꺼내 먹다 다른 사람 눈에 띄기도 한다 — 그 도둑의 어느 절도든 본 사람이면 '본 사람'이다
            bool Saw(int who) => w.Motions.Thefts.Any(t => t.Thief == thief.Id && t.Witnesses.Contains(who));
            bool onlySaw = witLines.Count > 0 && witLines.All(l => Saw(l.Who));
            bool others = past != null && past.Script.Where(l => l.Who >= 0 && !Saw(l.Who) && l.Role is LineRole.Hearsay).All(l => l.Text.Contains("못") || l.Text.Contains("들은"));
            Check("고발 — 그 자리에서 본 사람이 스스로 고발하고 서명을 모아 재판을 연다", trial != null && Saw(trial.Proposer) && past != null,
                trial != null ? $"{w.Crew.First(c => c.Id == trial.Proposer).Name} → {trial.Title} · 서명 {trial.Signers.Count}/{trial.Need} (부탁 {trial.Asked.Count - 2} · 거절 {trial.Refused.Count} · {trial.Stage}) · {(past != null ? $"{past.Venue.Name} 재판 · {past.Present.Count}명" : "재판 안 열림")}" : "고발 없음");
            Check("증언 — 본 사람만 '봤다'고 증언하고, 못 본 사람은 들은 말뿐이다", onlySaw && others,
                $"본 사람 {string.Join("·", w.Motions.Thefts.Where(t => t.Thief == thief.Id).SelectMany(t => t.Witnesses).Distinct().Select(id => w.Crew.First(c => c.Id == id).Name))} · 증언 {witLines.Count}줄 ({string.Join(" / ", witLines.Select(l => w.Crew.First(c => c.Id == l.Who).Name))})");
            if (past != null) minutes.Add("[재판] " + string.Join(" / ", past.Script.Select(l => $"{(l.Who < 0 ? "주 컴퓨터" : w.Crew.First(c => c.Id == l.Who).Name)}: {l.Text}")) + $" → {trial!.Outcome}");
            var mem = w.Relations.All.Where(x => x.Who == thief.Id && x.Reason is RelationReason.TestifiedAgainstMe or RelationReason.ForgaveMe).ToList();
            Check("처벌 — 표결로 벌을 정하고 (용서 · 경고 · 근무 추가 · 배급 감소 · 특권 박탈), 처벌받은 사람과의 사이가 바뀐다",
                trial?.Decided >= 0 && mem.Count > 0,
                trial != null ? $"{trial.Outcome} · {thief.Name}의 기억: {string.Join(" / ", mem.Select(x => $"{w.Crew.First(c => c.Id == x.About).Name} {RelationSystem.Name(x.Reason)}"))}" : "");
            bool compNoVote = past != null && past.Script.Any(l => l.Who < 0 && l.Role == LineRole.Computer) && past.Voters.All(id => id >= 0) && past.Hands.All(id => id >= 0);
            Check("주 컴퓨터 — 재판에 창고 수량 기록을 내놓지만 누가 했는지는 모르고 표도 없다", compNoVote && (past?.Script.First(l => l.Who < 0).Text.Contains("기록에 없") ?? false),
                past?.Script.FirstOrDefault(l => l.Who < 0).Text ?? "");
            Check("비밀 투표 — 끝나고 저마다 누가 어느 쪽에 넣었을지 짐작한다 (틀리기도 한다)", trial != null && trial.Secret && trial.Guesses.Count > 0,
                trial != null ? $"추측 {trial.Guesses.Count} · 틀림 {trial.Guesses.Count(g => g.thinksAgainst != g.truth)} · " + string.Join(" / ", trial.Guesses.Take(4).Select(g => $"{w.Crew.First(c => c.Id == g.who).Name}→{w.Crew.First(c => c.Id == g.about).Name} {(g.thinksAgainst ? "저쪽" : "같은 쪽")}{(g.thinksAgainst == g.truth ? "" : "(틀림)")}")) : "");
            // 결정을 어기면 다시 안건: 배급 결정을 어긴 빼돌리기 · 벌 근무를 빼먹으면 다시 회의에
            var dutyWho = adults.First(c => c != thief && c != accuser);
            w.Motions.Duty[dutyWho.Id] = (2f, w.Tick + SimTime.Minutes(5), trial?.Id ?? -1);
            dutyWho.Mind.Anger = 0.8f;
            Run(w, SimTime.Minutes(30));
            var again = w.Motions.All.FirstOrDefault(m => m.Kind == MotionKind.Punishment && m.Target == dutyWho.Id);
            Check("감시 — 회의가 정한 벌을 어기면 (벌 근무를 빼먹으면) 다시 안건이 되고, 주 컴퓨터가 빈 기록을 적는다",
                again != null && w.Automation.Reasoning.Any(r => r.text.Contains("벌 근무")) && (th.Breach >= 0 || rat?.Passed != true),
                again != null ? $"{w.Crew.First(c => c.Id == again.Proposer).Name}: {again.Title} — {again.Why} · 배급 결정 위반 {(th.Breach >= 0 ? "기록됨" : "—")}" : "안건 없음");
        }

        // ── 4) 선장 불신임 선거 → 후보 연설 → 비밀 투표 → 새 선장의 방침 (위험 감수 · 컴퓨터에 맡길 몫)
        if (Do("4"))
        {
            var w = DayOne(seed + 2, "Hanbit");
            w.Motions.Quiet = true;
            var cap = w.Command.Captain!;
            w.Command.Trust = 0.15f;
            foreach (var c in w.Crew.Where(c => c != cap && !c.Dead)) c.ChangeAffinity(cap, -0.3f);
            var adults = w.Crew.Where(c => !c.Dead && !c.IsChild && c.CanAct && c != cap).ToList();
            var top = adults.OrderByDescending(CommandSystem.Leadership).ThenBy(c => c.Id).Take(3).Select(MotionSystem.Platform).ToList();
            int risk0 = Enumerable.Range(0, 3).FirstOrDefault(r => top.All(p => p.risk != r), -1);
            int auto0 = Enumerable.Range(0, 3).FirstOrDefault(a => top.All(p => p.computer != a), -1);
            if (risk0 >= 0) w.Policies.Set("risktaking", risk0, "시험");
            if (auto0 >= 0) w.Policies.Set("autoscope", auto0, "시험");
            int r0 = w.Policies["risktaking"], a0 = w.Policies["autoscope"];
            var prop = adults.OrderBy(c => c.AffinityTo(cap)).ThenBy(c => c.Id).First();
            var m = w.Motions.Propose(prop, MotionKind.Confidence, SittingKind.Election, $"선장 {cap.Name} 불신임", "다들 선장을 못 믿는다", target: cap.Id);
            long dbg4 = 0;
            CouncilUntil(w, () =>
            {
                if (Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "1" && w.Tick >= dbg4)
                {
                    dbg4 = w.Tick + SimTime.Hours(2);
                    Console.WriteLine($"     [{SimTime.Clock(w.Tick)}] {prop.Name} {prop.ActivityLabel} · {m.Stage} 서명 {m.Signers.Count}/{m.Need} 부탁 {m.Asked.Count} 거절 {m.Refused.Count} · " + string.Join(", ", prop.LastEvaluations.OrderByDescending(e => e.Score).Take(4).Select(e => $"{e.Activity.Id}:{e.Score:0.00}")));
                }
                return m.Decided >= 0 && w.Motions.Now == null;
            }, SimTime.TicksPerDay * 2, 30);
            var past = w.Motions.Past.LastOrDefault(p => p.Motion == m);
            var win = w.Crew.FirstOrDefault(c => c.Id == m.Winner);
            int speeches = past?.Script.Count(l => l.Role == LineRole.Candidate) ?? 0;
            Check("선거 — 불신임 서명이 모여 선거가 열리고, 후보들이 연설하고, 비밀 투표로 새 선장을 뽑는다",
                m.Decided >= 0 && m.Passed && win != null && w.Command.CaptainId == win.Id && win != cap && speeches >= 1 && m.Secret,
                $"서명 {m.Signers.Count}/{m.Need} · {m.Outcome} · 연설 {speeches} · 지금 선장 {w.Command.Captain?.Name} · 표심 " + string.Join(" ", m.Final.Select(kv => $"{w.Crew.First(c => c.Id == kv.Key).Name}{kv.Value:+0.00;-0.00}")));
            if (past != null) minutes.Add("[선장 선거] " + string.Join(" / ", past.Script.Select(l => $"{(l.Who < 0 ? "주 컴퓨터" : w.Crew.First(c => c.Id == l.Who).Name)}: {l.Text}")) + $" → {m.Outcome}");
            bool changed = win != null && (w.Policies["risktaking"] != r0 || w.Policies["autoscope"] != a0)
                           && w.Policies["risktaking"] == MotionSystem.Platform(win).risk && w.Policies["autoscope"] == MotionSystem.Platform(win).computer;
            Check("새 선장의 방침 — 위험 감수 · 컴퓨터에 맡길 몫이 새 선장 뜻대로 바뀐다", changed,
                win != null ? $"위험 감수 {PolicySystem.Spec("risktaking").Options[r0]} → {w.Policies.Option("risktaking")} · 컴퓨터 자동 실행 {PolicySystem.Spec("autoscope").Options[a0]} → {w.Policies.Option("autoscope")}" : "");
            var lost = w.Motions.Grudges.Where(g => g.Motion == m.Id).ToList();
            Check("진 쪽 — 내려온 선장과 진 후보 쪽이 불만을 품고, 누가 불신임에 넣었는지 짐작한다",
                lost.Any(g => g.Who == cap.Id) && m.Guesses.Any(g => g.who == cap.Id),
                $"불만 {lost.Count}명 · 내려온 선장의 추측 {m.Guesses.Count(g => g.who == cap.Id)}건 · 파벌 {string.Join(" / ", w.Motions.Factions.Where(f => !f.Gone).Select(f => f.Name))}");
        }

        // ── 5) 스스로 굴러가는 사흘 (안건 · 파벌 · 흩어짐)
        if (Do("5"))
        {
            var w = DayOne(seed + 3, "Hanbit");
            Run(w, SimTime.TicksPerDay * 3);
            var st = w.Motions.Stats;
            Console.WriteLine($"   사흘: {st.Line()}");
            Check("저절로 — 사흘 동안 승무원이 낸 안건이 서명을 거쳐 회의에 오른다", st.Proposed >= 2 && st.Signed >= 2 && st.Passed + st.Failed >= 1,
                string.Join(" / ", w.Motions.All.Take(6).Select(m => $"{m.Title} [{m.Stage}{(m.Decided >= 0 ? " " + m.Outcome : "")}]")));
            Run(w, SimTime.TicksPerDay * 3);
            Console.WriteLine($"   엿새: {st.Line()}");
            // 통합6 안건이 잦은 배는 파벌이 다음 안건마다 다시 뭉쳐 오래 간다 — 나흘 더 (한동안 같이 설 일이 없으면 흩어진다)
            for (int d = 0; d < 4 && !w.Motions.Factions.Any(f => f.Gone); d++) Run(w, SimTime.TicksPerDay);
            var gone = w.Motions.Factions.Where(f => f.Gone).ToList();
            Check("파벌은 생겼다 흩어진다 — 다음 안건에서 갈라서거나 한동안 같이 설 일이 없으면", st.FactionsBorn >= 1 && gone.Count >= 1,
                string.Join(" / ", w.Motions.Factions.Select(f => $"{f.Name}{(f.Gone ? $" (흩어짐: {f.GoneWhy})" : $" (이김 {f.Wins} · 짐 {f.Losses})")}")));
        }

        // ── 6) 결정론 · 성능
        if (Do("6"))
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint h1 = H(), h2 = H();
            Check("결정론 (같은 시드 · 같은 지문)", h1 == h2, $"{h1:x8} / {h2:x8}");
            if (Environment.GetEnvironmentVariable("SHIPSIM_NOPERF") != "1")
            {
                double Day(bool off)
                {
                    MotionSystem.Off = off;
                    var w = World.CreateDefault(seed, 0, "Cheonma");
                    Run(w, SimTime.Hours(2));
                    MotionSystem.UpdateTicks = 0;
                    var sw = Stopwatch.StartNew();
                    Run(w, SimTime.TicksPerDay);
                    MotionSystem.Off = false;
                    return sw.Elapsed.TotalSeconds;
                }
                double t0 = Math.Min(Day(true), Day(true)), t1 = Math.Min(Day(false), Day(false));
                double own = MotionSystem.UpdateTicks * 1.0 / Stopwatch.Frequency;
                Console.WriteLine($"   성능 (30인 하루): 끔 {t0:0.0}초 · 켬 {t1:0.0}초 ({(t1 / t0 - 1) * 100:+0;-0}%) · 이 시스템 틱 {own * 1000:0}ms");
                Check("성능 · 30인 배 하루 ±10%", t1 < t0 * 1.10, $"{t0:0.0} → {t1:0.0}초");
            }
        }

        Console.WriteLine();
        foreach (var l in minutes) Console.WriteLine("   " + (l.Length > 900 ? l[..900] + "…" : l));
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}건");
        return _fails == 0 ? 0 : 1;
    }
}
