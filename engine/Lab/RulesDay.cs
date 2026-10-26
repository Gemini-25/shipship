using ShipEngine;

namespace Lab;

/// <summary>
/// 평시 하루 — 디딤호(6명 · 로봇 2) 하루를 한 번만 돌려 마음(먹고 자고 일함) · 관계(대화 · 다툼) · 물자(장부 · 사바티에 · 공조 응결)를 함께 본다.
/// 예전엔 세 묶음이 같은 하루를 저마다 돌렸다 (하루 셋 → 하나).
/// </summary>
public static partial class Program
{
    private static void DayRules()
    {
        if (!Do("평시 하루")) return;
        Console.WriteLine("\n[평시 하루 — 디딤호 6명 · 로봇 2]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        double Rh(Sim s)
        {
            var f = s.Get<Field>(); double sum = 0; int n = 0;
            foreach (var room in s.Layout.Rooms) foreach (int c in room.Cells)
                if (f.Pressure(c) > 50) { sum += f.Partial(f.T.gH2O, c) / Water.SatKPa(f.Temp(c)); n++; }
            return n > 0 ? sum / n : 0;
        }

        var day = ShipWorld.Crewed("Didim", 3, 6, 2);
        var m = day.Get<Mind>(); var life = day.Get<Life>();
        var people = m.Agents.Where(a => a.IsPerson).ToList();
        var robots = m.Agents.Where(a => a.C.Kind == AgentKind.Robot).ToList();
        var docked = new int[m.Agents.Count]; var robotGoals = new Dictionary<GoalKind, int>();   // DF5: 로봇이 충전독에 서 있는 틱 · 로봇이 한 일
        long loiter = 0;   // 씻는 방에서 우두커니 서 있는 사람 틱
        double wastePeak = 0; string morningWater = "", morningCalc = "";   // DF6: 오수 통 · 아침 방송의 물 날 수와 주컴퓨터 셈
        var meals = new int[m.Agents.Count]; var sleep = new int[m.Agents.Count]; var work = new int[m.Agents.Count]; var starving = new int[m.Agents.Count];
        var lastAte = people.ToDictionary(a => a.Id, a => a.C.AteAt);
        // 판단 이관 (바) 감사: 하루 동안 마음이 한 사회적 행동 · 사회가 낸 '행동' 사실을 모두 모은다 (행동은 두 틱 뒤 비우고 · 사실은 고리라 틱마다)
        var acts = new List<SocialAct>(); var deeds = new List<Fact>();
        long actCur = m.ActsTotal, factCur = day.Facts.Total;
        static bool Deed(in Fact f) => f.Kind is FactKind.Talk or FactKind.Fight or FactKind.Sabotage or FactKind.Lie or FactKind.Confessed
            || f.Kind == FactKind.Meeting && f.Size >= 0 || f.Kind == FactKind.Relationship && f.Size == -1 || f.Kind == FactKind.Culture && (int)f.Size == (int)CultureEvent.Nickname;
        var dir = new Director(day.Layout) { KeepRing = false };   // ⑬ 이야기 편집: 날이 바뀌면 지난 하루가 한 장이 된다
        var dir3 = new Director(day.Layout) { KeepRing = false, DayMinutes = 3 };   // 게임처럼 하루 3분 (PH12 — 물체 볼거리가 하루 길이를 늘리지 않는다)
        var dir3off = new Director(day.Layout) { KeepRing = false, DayMinutes = 3, PropShowOn = false };   // 물체 볼거리를 끈 연출 (견줌)
        double dayScreen = -1, dayScreenOff = -1;
        long startTick = day.Tick;
        // DF7 마음에 남는 것: 깬 이의 감정을 10분마다 떠 본다 (모두 0.05 아래인 몫) · 새벽 한 시 반에 이 하루를 갈라 한쪽에만 주방 기름불을 낸다 (같은 씨 · 같은 때 — 그 뒤 네 시간의 대화 주제 · 기분을 견준다 ·
        //   새벽 근무 · 밤에 깨어 쉬는 이가 다섯 — 보고서 §4-5의 '새벽 두 시 휴게실')
        long FireAt = Time.Minutes(90);
        int feltZero = 0, feltN = 0; double eveFelt = 0; int eveN = 0;
        Task<(int fireTalks, int talks, int breachTalks, double felt, string first, string topics, int recall, string recallFirst)>? fireEve = null;
        static double Felt(Mind fm, Life fl) { double s = 0; int n = 0; foreach (var a in fm.Agents) if (a.IsPerson && a.C.Up != 0 && fl[a.C.Body].Asleep == 0) { s += a.C.Fear + a.C.Sad + a.C.Anger + a.C.Stress - a.C.Joy; n++; } return n > 0 ? s / n : 0; }
        static string Topics(Society so, long from) => string.Join(" · ", so.Sayings.Where(y => y.Tick >= from).GroupBy(y => y.Topic).Select(g => $"{TalkTable.TopicName(g.Key)} {g.Count()}"));
        static (int, int, int, double, string, string, int, string) FireEvening(Sim s)
        {
            var fm = s.Get<Mind>(); var fl = s.Get<Life>(); var fso = s.Get<Society>();
            var stove = s.Layout.Furniture.First(fu => fu.Type == "Stove");
            int oil = s.Causes.Add(CauseKind.Setup, "저녁을 짓던 조리대의 기름에 불이 붙었다", s.Tick, stove.Cells[0]);
            long t0 = s.Tick, f0 = s.Facts.Total;
            s.Get<Chemistry>().Ignite(stove.Cells[0], 3e6, oil);
            var shelf = s.Layout.Furniture.FirstOrDefault(fu => fu.Type == "Shelf" && fu.Room == stove.Room);   // 튄 기름이 곁의 선반에도 옮아붙었다 (관찰 장면 '문화'와 같은 불)
            if (shelf != null) s.Get<Chemistry>().Ignite(shelf.Cells[0], 3e6, s.Causes.Add(CauseKind.Setup, "튄 기름불이 주방 선반에 옮아붙었다", s.Tick, shelf.Cells[0], oil));
            double sum = 0; int n = 0;
            for (int k = 0; k < Time.Hours(4); k++) { s.Step(); if (k >= Time.Minutes(30) && k % Time.Minutes(10) == 0) { sum += Felt(fm, fl); n++; } }
            var talks = fso.Sayings.Where(y => y.Tick >= t0 && y.Topic == Topic.Fire).ToList();
            if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1")
            {
                foreach (var f in s.Facts.Since(f0).Where(f => f.Kind is FactKind.Ignition or FactKind.Extinguished or FactKind.FireSpread)) Console.WriteLine($"    (불) {Time.Clock(f.Tick)} {FactNames.Of(f.Kind)} {s.Causes.Text(f.Cause)} · {(f.Actor >= 0 && f.Actor < fm.Agents.Count ? fm.NameOf(f.Actor) : "-")}");
                foreach (var a in fm.Agents.Where(a => a.IsPerson)) Console.WriteLine($"    (불) {fm.NameOf(a.Id)} 위기 {(a.C.CrisisAt > 0 ? Time.Clock(a.C.CrisisAt) : "-")} {a.C.CrisisTopic} 겪음 {a.C.CrisisSaw} 나눔 {a.C.CrisisTold} · 기분 {a.C.MoodGood:0.00}/{a.C.MoodBad:0.00} · 목표 {a.C.Goal} · 잠 {fl[a.C.Body].Asleep}");
                foreach (var y in fso.Sayings.Where(y => y.Tick >= t0)) Console.WriteLine($"    (불) {Time.Clock(y.Tick)} {fm.NameOf(y.Speaker)} → {fm.NameOf(y.Listener)} {y.Topic}/{y.End} '{s.Causes.Text(y.Line)}'");
            }
            // 지난 일을 떠올리는 줄 ('불/맞장구' · '불/칭찬' — "그날 이후로 불꽃만 봐도 움찔해.")이 불이 난 두 시간 안에 나왔나 (DF7 검토: 꺼지고 몇 분 만에 방금 일을 '그날'로 말했다)
            var past = TalkTable.Load().Lines.Where(kv => kv.Key is "불/맞장구" or "불/칭찬").SelectMany(kv => kv.Value)
                .Select(l => new System.Text.RegularExpressions.Regex("^" + string.Join(".*", System.Text.RegularExpressions.Regex.Split(l, @"\{[^}]*\}").Select(System.Text.RegularExpressions.Regex.Escape)) + "$")).ToList();
            var recall = talks.Where(y => y.Tick < t0 + Time.Hours(2) && past.Any(rx => rx.IsMatch(s.Causes.Text(y.Line)))).ToList();
            return (talks.Count, fso.Sayings.Count(y => y.Tick >= t0), fso.Sayings.Count(y => y.Tick >= t0 && y.Topic == Topic.Breach), n > 0 ? sum / n : 0, talks.Count > 0 ? $"{Time.Clock(talks[0].Tick)} {s.Causes.Text(talks[0].Line)}" : "", Topics(fso, t0),
                recall.Count, recall.Count > 0 ? $"{Time.Clock(recall[0].Tick)} {s.Causes.Text(recall[0].Line)}" : "");
        }
        long ResumeAt = Time.Hours(22); Task<Chapter?>? resumed = null;
        for (int k = 0; k < Time.Hours(24); k++)
        {
            day.Step();
            if (k % Time.Minutes(10) == 0)
                foreach (var a in people) if (a.C.Up != 0 && life[a.C.Body].Asleep == 0) { feltN++; if (a.C.Fear < 0.05f && a.C.Sad < 0.05f && a.C.Anger < 0.05f && a.C.Joy < 0.05f && a.C.Stress < 0.05f) feltZero++; }
            if (k == FireAt) { var fork = day.Clone(); fireEve = Task.Run(() => FireEvening(fork)); }
            if (k > FireAt && k < FireAt + Time.Hours(4) && k - FireAt >= Time.Minutes(30) && (k - FireAt) % Time.Minutes(10) == 0) { eveFelt += Felt(m, life); eveN++; }
            dir.Observe(day); dir3.Observe(day); dir3off.Observe(day);
            if (k == ResumeAt)
            {
                // S3 이어 보기: 날 가운데 (밤 열 시) 세계 · 연출 기록을 찍어 되살리고 다른 코어에서 날이 바뀔 때까지 — 같은 장이 지어지나 (장 재료 · 하이라이트 · 하루의 결을 담는다)
                var rw = new StateWriter(1 << 16); dir.SaveRecord(rw);
                var backW = new Sim(day.Setup); backW.Load(day.Save());
                var backD = new Director(day.Layout) { KeepRing = false };
                bool loaded = backD.LoadRecord(new StateReader(rw.ToArray()), backW);
                resumed = Task.Run(() => { if (!loaded) return (Chapter?)null; while (backW.Tick < Time.TicksPerDay) { backW.Step(); backD.Observe(backW); } return backD.Chapters.FirstOrDefault(c => !c.Week); });
            }
            if (day.Tick % Time.TicksPerDay == 0 && dayScreen < 0) { dayScreen = dir3.ScreenSeconds; dayScreenOff = dir3off.ScreenSeconds; }   // 첫 하루 (자정까지 — 하루 예산은 날마다)
            acts.AddRange(m.ActsSince(actCur)); actCur = m.ActsTotal;
            foreach (var f in day.Facts.Since(factCur))
            {
                if (Deed(f)) deeds.Add(f);
                if (Debug && f.Kind == FactKind.Ignition) Console.WriteLine($"    {Time.Clock(day.Tick)} 불: {string.Join(" ← ", day.Causes.Chain(f.Cause).Select(c => day.Causes.Text(c)))}");
            }
            factCur = day.Facts.Total;
            { var nw = day.Get<Network>(); wastePeak = Math.Max(wastePeak, nw.WasteWater * 0.018); }
            if (Time.HourOfDay(day.Tick) is double hh && hh >= 7 && hh < 7.001 && morningWater == "")
            {
                var smm = day.Get<ShipMind>();
                morningWater = System.Text.RegularExpressions.Regex.Match(smm.Morning, @"물 ([0-9.]+일 치|줄지 않음|999일 넘게)").Groups[1].Value;
                double wd = smm.WaterDaysLeft(day);
                morningCalc = double.IsInfinity(wd) ? "줄지 않음" : wd > 999 ? "999일 넘게" : $"{wd:0.#}일 치";
            }
            foreach (var r in robots) { if (r.C.Goal == GoalKind.Charge && r.C.Cell == r.C.GoalTarget) docked[r.Id]++; robotGoals[r.C.Goal] = robotGoals.GetValueOrDefault(r.C.Goal) + 1; }
            foreach (var a in people) if (a.C.Goal == GoalKind.Idle && a.C.Up != 0 && m.RoomOf(a.C.Cell) is int wr && wr >= 0 && day.Layout.Rooms[wr].Type == "Washroom") loiter++;
            foreach (var a in people)
            {
                if (a.C.AteAt != lastAte[a.Id]) { meals[a.Id]++; lastAte[a.Id] = a.C.AteAt; }
                if (life[a.C.Body].Asleep != 0) sleep[a.Id]++;
                if (a.C.Goal == GoalKind.Work) work[a.Id]++;
                if (a.C.Hunger >= 0.99f || a.C.Thirst >= 0.9f) starving[a.Id]++;
                if (Debug && k % 375 == 0 && (a.C.Hunger > 0.8f || a.C.Thirst > 0.8f))
                    Console.WriteLine($"    {Time.Clock(day.Tick)} {m.NameOf(a.Id)} 배고픔 {a.C.Hunger:0.00} 목마름 {a.C.Thirst:0.00} 목표 {a.C.Goal} · {m.Thought(a.Id)} · 양분 {life[a.C.Body].Reserve:0.0}");
            }
            if (Debug && k % Time.Hours(1) == 0)
                Console.WriteLine($"    {Time.Clock(day.Tick)} " + string.Join(" · ", people.Select(a => $"{m.NameOf(a.Id)}:{a.C.Goal}@{(m.RoomOf(a.C.Cell) >= 0 ? day.Layout.Rooms[m.RoomOf(a.C.Cell)].Name : "-")} 기분 {a.C.MoodGood:0.00}/{a.C.MoodBad:0.00} 감정 겁{a.C.Fear:0.00} 슬{a.C.Sad:0.00} 화{a.C.Anger:0.00} 기{a.C.Joy:0.00} 긴{a.C.Stress:0.00} 외{a.C.Lonely:0.00} 배{a.C.Hunger:0.00} 졸{a.C.Sleepy:0.00} 심{a.C.BoredLong:0.00}")));
        }

        Check("하루 3분 (물체 볼거리 PH12): 평시 하루의 화면 시간이 물체 볼거리를 끈 연출의 ±5% — 깨어 움직이는 물체 · 떨어진 물건의 느린 틱도 하루 느린 예산 안 (늦은 조짐은 예산을 넘겨 그대로)",
            dayScreenOff > 0 && Math.Abs(dayScreen - dayScreenOff) <= 0.05 * dayScreenOff,
            $"첫 하루 (자정까지) 화면 {dayScreen:0.0}초 · 끈 연출 {dayScreenOff:0.0}초 · 물체 볼거리 {dir3.PropScreenSec:0.0}초 · 조짐 {dir3.OmenScreenSec:0}초 · 위험 {dir3.DangerScreenSec:0}초 · 결정적 {dir3.CriticalScreenSec:0}초");

        // ── 마음: 굶거나 목마른 사람 없이 먹고 자고 일한다 · 벽에 금 없음 ──
        {
            double sleepH(int id) => sleep[id] / (double)Time.TicksPerHour;
            // 첫날은 졸림이 사람마다 달라 잠이 하루 경계에 걸린다 — 4~12시간
            bool ok = people.All(a => life[a.C.Body].Alive != 0 && starving[a.Id] < Time.Hours(1) && meals[a.Id] is >= 2 and <= 6 && sleepH(a.Id) is >= 4 and <= 12);
            Check("평시 하루 (디딤호 6명 · 로봇 2): 모두 살아 있고 하루 2~6끼 · 한 시간 넘게 굶주리거나 목마른 사람 없음 · 잠 4~12시간 · 일하고 · 벽에 금이 가지 않는다",
                ok && day.Facts.CountOf(FactKind.WallCrack) == 0 && people.Count(a => work[a.Id] > Time.Hours(2)) >= 4,
                string.Join(" · ", people.Select(a => $"{m.NameOf(a.Id)} 끼니 {meals[a.Id]} 잠 {sleepH(a.Id):0.0}시간 일 {work[a.Id] / (double)Time.TicksPerHour:0.0}시간 굶주린 {starving[a.Id] / (double)Time.TicksPerHour:0.0}시간"))
                + $" · 금 {day.Facts.CountOf(FactKind.WallCrack)}");
        }

        // ── DF6 물 · 열 경보 (보고서 §4-3 · §4-7): 물 재생기가 하루 든 오수를 다 거른다 · 운동한 건강한 이에게 열 경보 0 · 방송 · 일지 · 배급이 같은 날 수 ──
        {
            var nw = day.Get<Network>(); var smw = day.Get<ShipMind>();
            double waste = nw.WasteWater * 0.018;
            Check("6명 배 오수가 쌓이지 않는다: 디딤호 하루 — 물 재생기가 드는 오수를 다 걸러 하루 끝 오수 통이 15L 아래 (예전엔 처리량 하루 39L가 6명의 오수 52L에 못 미쳐 날마다 쌓였다 — 엿새에 128L)",
                waste < 15, $"하루 끝 오수 {waste:0.0}L (가장 많을 때 {wastePeak:0.0}L) · 거른 {nw.WaterProcessed * 0.018:0}L · 되찾은 {nw.WaterRecovered * 0.018:0}L");
            var fevers = smw.Log.Where(l => l.text.Contains("열이 있습니다")).ToList();
            Check("운동한 건강한 이에게 열 경보 0: 평시 하루 (앓는 이 없음) — 쉬는 몸에서 30분 사이 두 번 재어 둘 다 높을 때만 열 · 사람마다 하루 한 번 (예전엔 운동 뒤 30분이 지나자마자 37.7℃로 아홉 번)",
                people.All(a => life[a.C.Body].Ill == 0) && fevers.Count == 0, $"열 경보 {fevers.Count}번{(fevers.Count > 0 ? $" (처음 {Time.Clock(fevers[0].tick)} {fevers[0].text})" : "")}");
            Check("날 수는 한 셈: 아침 방송의 물 날 수가 주컴퓨터가 배급을 정하는 셈과 같다 (예전엔 방송 · 일지 · 배급이 따로 셈해 같은 날 '387.8일 치' · '28일 치')",
                morningWater.Length > 0 && morningWater == morningCalc, $"방송 '물 {morningWater}' · 셈 '{morningCalc}'");
        }

        // ── DF5 평시 로봇 (보고서 §4-10): 할 일이 없으면 충전독에서 놀지 않고 돈다 (순회 · 나르기 · 거두기) — 충전독에 서 있는 몫이 하루의 절반 아래 ──
        {
            double share = robots.Sum(r => docked[r.Id]) / (double)Math.Max(1, robots.Count * Time.Hours(24));
            Check("평시 하루 로봇 충전독 시간 ≤ 50%: 할 일이 없는 로봇은 배를 돈다 (예전 94~99%)",
                robots.Count == 2 && share <= 0.5,
                $"충전독 {share:P0} ({string.Join(" · ", robots.Select(r => $"{m.NameOf(r.Id)} {docked[r.Id] / (double)Time.Hours(24):P0}"))}) · 한 일 {string.Join(" · ", robotGoals.OrderByDescending(x => x.Value).Select(x => $"{Mind.GoalName(x.Key)} {x.Value / (double)Math.Max(1, robots.Count * Time.Hours(24)):P0}"))}");
        }

        // ── 판단 이관 (바) 감사: 사회가 낸 '행동' 사실(대화 · 모임 열기 · 몸싸움 · 몰래 방해 · 헤어짐 · 별명 · 거짓말 · 털어놓음)마다 그 틱(두 틱 안)에 행위자의 판단 행동이 있다 ──
        {
            bool Backed(in Fact f)
            {
                var (kind, byTarget) = f.Kind switch
                {
                    FactKind.Talk => (SocialActKind.Talk, false), FactKind.Meeting => (SocialActKind.CallMeeting, false), FactKind.Fight => (SocialActKind.Attack, false),
                    FactKind.Sabotage => (SocialActKind.Sabotage, false), FactKind.Relationship => (SocialActKind.BreakUp, false), FactKind.Lie => (SocialActKind.TellLie, false),
                    FactKind.Confessed => (SocialActKind.Confess, false), _ => (SocialActKind.Nickname, true),   // 별명: 사실의 행위자는 불린 이 · 행동은 부른 이의 것
                };
                long t = f.Tick; int who = f.Actor;
                bool talk = f.Kind == FactKind.Talk;   // 대화의 대답(듣는 이의 한 마디)은 같은 '대화' 행동 안에 마음이 정했다 — 행동의 대상
                return acts.Any(x => (x.Kind == kind || talk && x.Kind == SocialActKind.Mediate) && x.Tick <= t && x.Tick >= t - 2 && (byTarget ? x.Target == who : x.Actor == who || talk && x.Target == who));   // 말림도 그 사람의 말 (말린 이의 한 마디가 대화 사실로 남는다)
            }
            var orphans = deeds.Where(f => !Backed(f)).ToList();
            string counts = string.Join(" · ", deeds.GroupBy(f => f.Kind).Select(g => $"{FactNames.Of(g.Key)} {g.Count()}"));
            if (Debug) foreach (var f in orphans.Take(8)) Console.WriteLine($"    판단 없는 행동 사실: {Time.Clock(f.Tick)} {f.Kind} {f.Size} · {m.NameOf(f.Actor)} · {day.Causes.Story(f.Cause)}");
            Check("판단 이관 감사 (평시 하루): 사회가 낸 행동 사실(대화 · 모임 열기 · 몸싸움 · 몰래 방해 · 헤어짐 · 별명 · 거짓말 · 털어놓음)마다 그 틱에 행위자의 판단 행동이 있다 — 사회 엔진은 아무 행동도 지어내지 않는다",
                deeds.Count > 0 && orphans.Count == 0,
                $"행동 사실 {deeds.Count} ({counts}) · 마음의 행동 {acts.Count} · 판단 없는 것 {orphans.Count}{(orphans.Count > 0 ? $" (처음: {FactNames.Of(orphans[0].Kind)} {Time.Clock(orphans[0].Tick)})" : "")}");
        }

        // ── ⑧ 마음 건강: 보통의 하루에는 아무도 우울 · 불안해지지 않고 · 악몽 · 공황 발작 · 되살아남이 없다 ──
        Check("평시 하루의 마음: 아무도 우울 · 불안 0.05를 넘지 않고 · 악몽 · 공황 발작 · 되살아남이 없다",
            people.All(a => a.C.Depress < 0.05f && a.C.Anxiety < 0.05f) && m.Nightmares == 0 && m.PanicAttacks == 0 && m.Flashbacks == 0,
            $"우울 많아야 {people.Max(a => a.C.Depress):0.000} · 불안 {people.Max(a => a.C.Anxiety):0.000} · 충격 {people.Max(a => a.C.Shock):0.00} · 악몽 {m.Nightmares} · 발작 {m.PanicAttacks} · 되살아남 {m.Flashbacks}");

        // ── DF7 마음에 남는 것 (보고서 §4-5 · §4-6): 평시의 감정이 늘 0이 아니다 · 기름불이 난 판은 대화 주제와 기분이 다르다 · 함께 웃은 둘은 서로를 좋은 일로 기억한다 ──
        {
            Check("평시 감정 0의 몫 ≤ 절반: 깬 이의 감정 (겁 · 슬픔 · 화 · 기쁨 · 긴장)이 모두 0.05 아래인 때가 하루 잰 것의 절반을 넘지 않는다 — 몸 · 욕구 · 겪은 일이 남긴 기분이 감정의 바닥 (예전 85%)",
                feltN > 0 && feltZero * 2 <= feltN, $"모두 0.05 아래 {feltZero}/{feltN} ({100.0 * feltZero / Math.Max(1, feltN):0}%)");
            var so = day.Get<Society>();
            var eve = fireEve!.Result;
            long eveFrom = startTick + FireAt;
            int calmFire = so.Sayings.Count(y => y.Tick >= eveFrom && y.Tick < eveFrom + Time.Hours(4) && y.Topic == Topic.Fire);
            double calm = eveN > 0 ? eveFelt / eveN : 0;
            Check("기름불이 난 판은 다르다: 같은 하루를 새벽 한 시 반에 갈라 한쪽에만 주방 기름불 — 그 네 시간 이야기의 절반 넘게가 불 이야기 (안 난 쪽은 0 · 연기에 닫은 문을 '구멍' 이야기로 하지 않는다) · 30분 뒤부터 깬 이의 마음 (겁 + 슬픔 + 화 + 긴장 − 기쁨)이 0.03 넘게 무겁다",
                eve.fireTalks >= 2 && eve.fireTalks * 2 > eve.talks && eve.breachTalks == 0 && calmFire == 0 && eve.felt - calm >= 0.03,
                $"불 이야기 {eve.fireTalks}/{eve.talks} (처음 '{eve.first}') · 구멍 이야기 {eve.breachTalks} · 안 난 쪽 {calmFire} / 마음 {eve.felt:0.000} vs {calm:0.000} / 주제 — 불: {eve.topics} · 안 남: {Topics(so, eveFrom)}");
            Check("불이 난 두 시간은 '방금 일'이다: 기름불 판 첫 두 시간의 불 이야기에 지난 일을 떠올리는 줄 ('그날 이후로 불꽃만 봐도 움찔해.' · '그날 정말 고마웠어.')이 없다 — 함께 끈 불의 기억도 여섯 시간은 방금 겪은 일로 이야기한다",
                eve.fireTalks >= 2 && eve.recall == 0, $"지난 일 줄 {eve.recall} (처음 '{eve.recallFirst}') · 불 이야기 {eve.fireTalks}");
            int laughs = so.Sayings.Count(y => y.End == TalkEnd.Laugh && y.Topic is Topic.Small or Topic.Joy && y.Speaker != y.Listener);
            int good = so.Memories.Count(x => x.Topic == Topic.Joy && x.About >= 0 && x.About != x.Owner);
            Check("함께 웃은 둘은 서로를 좋은 일로 기억한다: 평시 하루에 실컷 웃은 대화가 있으면 좋은 일 기억이 남는다 (예전엔 탓 · 다툼 기억뿐)",
                laughs >= 1 && good >= 1, $"웃은 대화 {laughs} · 좋은 일 기억 {good} · 기억 갈래 {string.Join(" · ", so.Memories.GroupBy(x => x.Topic).Select(g => $"{TalkTable.TopicName(g.Key)} {g.Count()}"))}");
        }

        // ── ⑨ 문화: 보통의 하루에는 금기 · 추모 · 규칙을 어기는 일이 없다 (함께 먹는 때는 하루로는 관습이 되지 않는다 — 사흘이 걸린다) ──
        {
            var so = day.Get<Society>();
            Check("평시 하루의 문화: 금기 · 숨진 이의 침대 · 추모 · 규칙을 어긴 일이 없고 · 하루 만에 관습이 서지 않는다",
                so.Taboos == 0 && so.BedTaboos == 0 && so.Memorials == 0 && so.RuleBreaks == 0 && so.Customs == 0,
                $"금기 {so.Taboos} · 침대 {so.BedTaboos} · 추모 {so.Memorials} · 축하 {so.Celebrations} · 별명 {so.Nicknames} · 관습 {so.Customs} · 어김 {so.RuleBreaks} · 이 배의 관습: {so.CultureLine(day) ?? "없음"}");
        }

        // ── 망: 사람이 탄 하루 끝 — 공급이 수요를 채우고 · 방 온도가 288~300K · 원자로 멀쩡 ──
        {
            var net = day.Get<Network>(); var f = day.Get<Field>();
            var vented = f.L.Rooms.Where(r => net.Vent[r.Id] >= 0).ToList();
            var temps = vented.Select(r => f.Temp(net.Vent[r.Id])).ToList();
            var hottest = vented.OrderByDescending(r => f.Temp(net.Vent[r.Id])).First();
            Check("평시 하루 끝: 공급이 수요를 채우고 · 방 온도가 288~300K · 원자로 멀쩡", net.Supplied >= net.Demanded - 1e-6 && temps.All(t => t > 288 && t < 300) && !net.ReactorScram,
                $"수요 {net.Demanded:0.0}kW · 원자로 {net.ReactorKw:0.0}kW · 냉각 {net.CoolingKw:0}kW · 방 온도 {temps.Min():0.0}~{temps.Max():0.0}K (가장 더운 곳 {hottest.Name}) · 장치 {net.Devices.Count} · 급전선 {net.Feeders.Count}");
        }

        // ── 주컴퓨터 일지: 22시 일지 한 장 · 7시 아침 방송 — 평시엔 '큰 일 없이 지나갔다' ──
        {
            var sm = day.Get<ShipMind>();
            string j = sm.Journal.Count > 0 ? sm.Journal[^1].text : "";
            if (Debug) Console.WriteLine($"    일지:\n      {j.Replace("\n", "\n      ")}\n    아침 방송: {sm.Morning}");
            Check("평시 하루의 주컴퓨터: 22시 일지 한 장('큰 일 없이 지나갔다' · 배 산소 · 식량 · 물 며칠 치) · 7시 아침 방송",
                sm.Journal.Count == 1 && Time.HourOfDay(sm.Journal[0].tick) == 22 && j.StartsWith("큰 일 없이 지나갔다") && j.Contains("식량 ") && j.Contains("물 ") && sm.Morning.StartsWith("좋은 아침입니다."),
                $"일지 {sm.Journal.Count}장 · \"{j.Replace("\n", " / ")}\" · 방송 \"{sm.Morning}\"");
        }

        // ── 관계: 말은 자주 나누고 · 몸싸움은 없다 (갈등은 드물게만 끝까지 간다) ──
        {
            var so = day.Get<Society>();
            if (Debug) foreach (var x in so.Sayings) Console.WriteLine($"    {Time.Clock(x.Tick)} {m.NameOf(x.Speaker)} → {m.NameOf(x.Listener)} {x.Topic}/{x.End} @ {(m.RoomOf(x.Cell) >= 0 ? day.Layout.Rooms[m.RoomOf(x.Cell)].Name : "-")}");
            Check("평소의 하루 (디딤호 6명): 대화는 많고 · 다툼은 드물고 · 몸싸움 · 몰래 방해는 없다",
                so.Talks >= 10 && so.Arguments <= 3 && so.Fights == 0 && so.Sabotages == 0,   // 대화 수는 시드마다 10~16 (화장실 (다) 앞뒤 시드 다섯: 11·16·15·14·11 → 10·15·12·14·13 — 시드 3이 문턱에 걸려 있었다)
                $"대화 {so.Talks} · 다툼 {so.Arguments} · 사과 {so.Apologies} · 소문 {so.Rumors} · 몸싸움 {so.Fights} · 방해 {so.Sabotages} · 가까워짐 {so.Friendships}");
        }

        // ── 물자 장부: 처음 재고 + 적은 흐름 = 지금 재고 · 며칠 버티나 ──
        {
            if (Debug) { var nn = day.Get<Network>(); Console.WriteLine($"    샤워 {m.ShowersTaken} · 틀어 둠 {m.ShowersLeftOn} · 주컴퓨터가 잠금 {day.Get<ShipMind>().ShowersCut} · 계량기 {nn.ShowerMeter.Sum():0}몰 · 씻은 물 {-day.Book.Of(Res.Water, Flow.Washed):0}몰 · 손 {m.HandWashes} · 마신 {m.WaterDrunk:0}몰 · 변기 {m.Toilets} · 줄 {m.Queued} · 돌아섬 {m.QueueGaveUp} · 안내 {day.Get<ShipMind>().WashCalls}/{day.Get<ShipMind>().WashOrders} · 닦기 {m.Scrubbed} · 손질 일 {day.Get<ShipMind>().Log.Count(l => l.text.Contains("변기"))}"); }
            var w = day.Get<Work>(); var b = day.Book;
            double worst = 0; string worstName = "";
            for (int k = 0; k < b.Kinds; k++)
            {
                if (k == (int)Res.O2) continue; // 산소는 아래에서 (공기는 새어 나갈 수 있다)
                double o = Math.Abs(w.Other(day, k)) / Math.Max(1, Math.Abs(w.Stock(k)));
                if (o > worst) { worst = o; worstName = k < Book.Bulk ? ((Res)k).ToString() : m.ItemNames[k - Book.Bulk]; }
            }
            double o2Other = w.Other(day, (int)Res.O2), breathed = -b.Of(Res.O2, Flow.Breathed);
            var (foodDays, _) = w.DaysLeft(day, (int)Res.Food);
            var (waterDays, waterNoMake) = w.DaysLeft(day, (int)Res.Water);
            var smd = day.Get<ShipMind>();
            Check("물자 장부: 디딤호 하루 — 처음 재고 + 적은 흐름 = 지금 재고 (식량 · 물 · 물건 정확히 · 산소는 숨의 1% 안) · 며칠 버티나를 센다 · 샤워 물은 오수 통에서 되찾으니 물 배급을 열지 않는다 (찾아 고친 결함 — 아침 샤워 몇 번에 '30일 아래'로 보였다)",
                worst < 1e-9 && Math.Abs(o2Other) < breathed * 0.01 && foodDays is > 150 and < 400 && waterDays > waterNoMake && smd.RationWaterOn == 0 && m.ShowersTaken >= 3, // 탐사선: 180일 치 포장 식량
                $"가장 큰 어긋남 {worst:0e0} ({worstName}) · 산소 {o2Other:0.0}몰 / 숨 {breathed:0}몰 · 식량 {foodDays:0.0}일 · 물 {waterDays:0}일 (되찾지 못하면 {waterNoMake:0.0}일) · 샤워 {m.ShowersTaken} · 물 배급 {smd.RationWaterOn}");
            Check("평시 하루의 화장실: 줄을 선 이가 없으면 주컴퓨터는 줄 안내를 하지 않는다 (지나가는 이 · 손 씻는 이를 기다리는 이로 치던 결함) · 다 쓴 이는 씻는 방에서 우두커니 서 있지 않는다 (둘이 변기 곁에 17분 서 있어 줄로 읽혔다 — DF5)",
                (m.Queued > 0 || smd.WashCalls + smd.WashOrders == 0) && loiter <= Time.Minutes(2), $"줄 {m.Queued} · 안내 {smd.WashCalls}/{smd.WashOrders} · 씻는 방에서 우두커니 {Time.ToSeconds(loiter) / 60:0}분");
        }

        // ── 사바티에: 갈라 낸 수소와 걸러 모은 이산화탄소로 가른 물의 절반 가까이를 되찾는다 (ISS — 수소가 모자라 절반을 넘지 못한다) ──
        {
            var b = day.Book;
            double split = -b.Of(Res.Water, Flow.Split), back = b.Of(Res.Water, Flow.Sabatier);
            Check("사바티에: 산소를 만들려고 가른 물의 40~50%를 이산화탄소 + 수소에서 되찾는다",
                split > 0 && back / split is > 0.4 and <= 0.5, $"가른 물 {split * 0.018:0.0}L · 되찾음 {back * 0.018:0.0}L ({back / Math.Max(1e-9, split):0.00}) · 버린 메탄 {day.Get<Network>().VentedCH4 * 0.016:0.00}kg");
        }

        // ── 공조 응결: 제습기가 없는 배도 공조 열교환기가 숨 · 땀의 습기를 물로 모은다 → 습도 60% 아래 ──
        {
            double rh = Rh(day), cond = day.Book.Of(Res.Water, Flow.Condensed);
            Check("공조 응결: 제습기 없는 디딤호 — 열교환기가 하루 몇 L의 습기를 물탱크로 되찾고 · 방 습도는 60% 아래",
                cond * 0.018 > 3 && rh < 0.6, $"모은 물 {cond * 0.018:0.0}L/일 · 평균 습도 {rh * 100:0}%");
        }

        // ── ⑬ 이야기 편집: 날이 바뀌는 순간 지난 하루가 한 장이 된다 (제목 · 하루의 결 · 그 날의 때) ──
        {
            var ch = dir.Chapters.FirstOrDefault(c => !c.Week);
            static int ArcMax(string arc) => arc.Contains("결정적 순간") ? 3 : arc.Contains("위험") ? 2 : arc.Contains("조짐") ? 1 : 0;
            Check("하루의 장: 날이 바뀌면 지난 하루를 한 장으로 묶는다 — '1일째 — …' 제목 · 하루의 결 · 그 날의 처음부터 날이 바뀐 때까지 · 날 띠의 분위기 (그날 가장 높이 오른 것 — 하루의 결보다 낮지 않다 · V3)",
                ch != null && ch.Title.StartsWith("1일째 — ") && ch.Arc.Length > 0 && ch.From == startTick + 1 && ch.To == Time.TicksPerDay && dir.Chapters.Count(c => !c.Week) == 1 && ch.Mood >= ArcMax(ch.Arc) && ch.Mood <= 3,
                ch == null ? $"장 없음 (시작 {startTick} · 끝 {day.Tick})" : $"\"{ch.Title}\" · {Time.Clock(ch.From)}~{Time.Clock(ch.To)} · 장면 {ch.Scenes.Count} · 분위기 {ch.Mood} · {string.Join(" / ", ch.Lines)}");
            // S3 이어 보기 (docs/OBSERVER.md §16): 밤 열 시에 찍어 되살린 연출이 날이 바뀔 때 같은 장을 짓는다 (제목 · 줄 · 장면 · 주인공)
            static string Of(Chapter? c) => c == null ? "" : $"{c.Title} / {string.Join(" / ", c.Lines)} / 장면 {string.Join(" ", c.Scenes.Select(h => $"{h.Tick}:{h.Title}"))} / 주인공 {string.Join(" ", c.Cast)}";
            var back = resumed?.Result;
            Check("이어 보기 S3: 날 가운데 (밤 열 시) 세계 · 연출 기록을 찍어 되살리면 날이 바뀔 때 같은 장 — 제목 · 줄 · 장면 · 주인공 (장 재료 · 하이라이트 · 하루의 결을 담는다)",
                ch != null && back != null && Of(ch) == Of(back), back == null ? "되살린 연출이 장을 짓지 못했다" : Of(ch) == Of(back) ? $"같음 — 장면 {back.Scenes.Count} · 주인공 {back.Cast.Count}" : $"원래 '{Of(ch)}' ↔ 되살림 '{Of(back)}'");
        }

        // ── 소리 단계 S6: 열 수축의 딱 — 외판이 해를 받고 그늘에 들며 늘고 준다 (너무 잦으면 안 된다) ──
        {
            long ticks = day.Get<Structure>().ThermalTicks;
            Check("열 수축: 디딤호 하루의 딱 소리는 배 전체에서 시간당 몇 번 (하루 20 ~ 1,000번)", ticks >= 20 && ticks <= 1000, $"하루 {ticks}번");
        }

        // ── 음식 향은 쌓이지 않는다 (검토 ④-2): 하루 내내 밥을 지어도 정화기의 숯이 걷는다 — 예전엔 없애는 곳이 없어 이틀 만에 배 전체가 13.5ppm ──
        {
            var fa = day.Get<Field>();
            double mol = 0, ppm = 0; int n = 0;
            for (int c = 0; c < day.Layout.N; c++) if (fa.Moles(c) > 1) { ppm += fa.Ppm(fa.T.gAroma, c); n++; }
            ppm /= Math.Max(1, n);
            mol = fa.Total(fa.T.gAroma);
            if (Debug)
                foreach (var r in day.Layout.Rooms.Select((r, i) => (r, i)).OrderByDescending(x => x.r.Cells.Sum(c => fa.Gas(fa.T.gAroma, c))).Take(5))
                    Console.WriteLine($"    향 {r.r.Name} {r.r.Cells.Sum(c => fa.Gas(fa.T.gAroma, c)):0.0000}몰 · 통풍구 {day.Get<Network>().Vent[r.i]} · 막음 {day.Get<Network>().Damper[r.i]}");
            double held = day.Get<Network>().ScrubHeld()[fa.T.Gases[fa.T.gAroma].Index], caught = held / Math.Max(1e-12, held + mol);
            // 덕트가 배 공기를 정화기로 돌리는 시간 상수는 열 몇 시간 (ISS TCCS도 시간당 15m³ — 하루 남짓) — 끼니마다 오르고 숯이 걷어 2ppm 아래에 머문다
            Check("음식 향이 쌓이지 않는다: 하루 밥을 지은 디딤호에서 낸 향의 4할 넘게 정화기 숯에 붙잡히고 · 배 평균 2ppm 아래 (예전엔 걷는 곳이 없어 나흘에 13.5ppm)",
                m.Cooked > 0 && caught > 0.4 && ppm < 2,
                $"밥 짓기 {m.Cooked} · 숯에 {held:0.000}몰 ({caught:P0}) · 공기에 {mol:0.000}몰 · 배 평균 {ppm:0.00}ppm");
        }

        // ── 긴 왕복 (검토 ⑤-2): 하루 끝(125의 배수 틱 — 주컴퓨터가 물자를 묻기 7틱 앞)에서 찍어 되살린 세계와 원래 세계를 150틱 나란히 —
        //    일과 물자 엔진이 늦게 이으면 되살린 쪽 주컴퓨터만 '마실 물 · 식량 · 산소 0일 치'를 보고 7틱 만에 갈라졌다. 맨 끝에 둔다 (원래 세계를 더 돌린다) ──
        {
            var back = new Sim(day.Setup); back.Load(day.Save());
            long at = day.Tick, diff = -1;
            for (int k = 0; k < 150 && diff < 0; k++) { day.Step(); back.Step(); if (!day.Save().AsSpan().SequenceEqual(back.Save())) diff = day.Tick; }   // 바이트 그대로 견준다 (지문을 셀 까닭이 없다 — 지문은 바이트마다 곱셈 하나라 틱마다 두 번이면 묶음의 4%)
            Check("긴 왕복: 하루 끝에 되살린 세계가 원래 세계와 150틱 바이트까지 같다 (주컴퓨터가 물자를 묻는 틱을 지나도)", diff < 0,
                $"되살린 틱 {at} (125 나머지 {at % 125})" + (diff < 0 ? "" : $" · 틱 {diff}에 갈라짐"));
        }

        // ── S8 이어 보기 카드 (docs/OBSERVER.md §16): 하루가 지난 디딤호를 묶음으로 — 카드 글 '디딤호 · 항해 3 · 2일째 · 6명 가운데 6명' · '어제: 1일째 — …' · '끈 때 …' · 개발 말 0 ──
        {
            var show = new ShipView.LiveShow(day, "디딤호");
            var rw = new StateWriter(1 << 16); dir.SaveRecord(rw); show.Dir.LoadRecord(new StateReader(rw.ToArray()), day);   // 하루를 본 연출을 화면에 옮긴다
            var meta = show.SaveBundle(ShipView.SaveKind.Daily).Meta;
            int alive = people.Count(a => life[a.C.Body].Alive != 0);
            string t = ShipView.VoyageCard.Title(meta), y = ShipView.VoyageCard.Yesterday(meta), w = ShipView.VoyageCard.When(meta);
            static bool DevFree(string x) => !System.Text.RegularExpressions.Regex.IsMatch(x, "[A-Za-z{}_]|NaN|null");
            Check("이어 보기 S8: 카드 글 — '디딤호 · 항해 3 · 2일째 · 6명 가운데 n명' · '어제: 1일째 — …' (지난 장 제목) · '끈 때 월-일 시:분' · 개발 말 0",
                t == $"디딤호 · 항해 3 · 2일째 · 6명 가운데 {alive}명" && y.StartsWith("어제: 1일째 — ") && w.StartsWith("끈 때 ") && DevFree(t) && DevFree(y) && DevFree(w),
                $"'{t}' / '{y}' / '{w}'");
        }
    }
}
