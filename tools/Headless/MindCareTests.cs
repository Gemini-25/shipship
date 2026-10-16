using System;
using System.Linq;
using ShipSim.Core;

// v23 마음 돌봄 점검 (--mindcaretest):
//   말벗 로봇을 싣고 뜬다 · 지친 사람에게 쉬라고 권한다 · 마음 병에 말벗 로봇이 찾아간다 · 돌봄이 회복을 당긴다
//   · 깊으면 사람(상담 자격자)이 이어받는다 · 말벗 로봇이 없으면 간호 · 의사 로봇이 대신 · 환자가 늘면 오락실로 고치는 안
//   · 오락실 설비(오락기 · 가상 고향 의자) · 오락실에서 쉬면 향수병이 풀린다 · 붐비면 말벗 로봇을 더 · 향수병 한도 · 저장 · 끄면 예전처럼.
public static partial class Program
{
    private static int RunMindCareTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"마음 돌봄 점검 (v23) · 시드 {seed}\n");
        try
        {
            GenerationSystem.QuietOff = true;
            PlanetSystem.Off = true; PurposeSystem.Off = true;
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(2));
            var m = w.MindCare;
            var crew = w.Crew.Where(c => !c.Dead && !c.IsChild && !c.Passenger).OrderBy(c => c.Id).ToList();
            var bot = w.Robots.Robots.FirstOrDefault(r => r.Kind == RobotKind.Companion);

            // 1) 말벗 로봇
            Check("세 사람 넘는 배는 말벗 로봇 한 대를 싣고 뜬다", bot != null && m.Companions == 1,
                bot == null ? "없음" : $"{bot.Name} · {bot.Dock.Room.Name} · 로봇 {w.Robots.Robots.Count}대");
            if (bot == null) throw new Exception("말벗 로봇 없음");

            // 2) 지침 → 쉬라고 권한다
            var tired = crew[1];
            tired.Needs.Stress = 0.75f;
            for (int i = 0; i < 8 && m.Stats.Nudges == 0; i++) { tired.Needs.Stress = MathF.Max(tired.Needs.Stress, 0.7f); Run(w, SimTime.Hours(1)); }
            Check("긴장이 높은 사람 — 주컴퓨터가 쉬라고 권한다 (쉼 · 취미 점수가 오른다)", m.Stats.Nudges >= 1 && m.Cases.TryGetValue(tired.Id, out var tc) && tc.Level >= 1,
                $"권함 {m.Stats.Nudges} · {w.Automation.Reasoning.LastOrDefault(r => r.text.Contains("쉬세요")).text}");

            // 3) 마음 병 → 말벗 로봇
            var sad = crew[2];
            var dep = w.Ailments.Catch(sad, "depression", null, "시험");
            for (int i = 0; i < 30 && m.Stats.RobotSessions == 0; i++)
            {
                Run(w, SimTime.Hours(1));
            }
            m.Cases.TryGetValue(sad.Id, out var sc);
            var seen = w.Log.Entries.LastOrDefault(e => e.Text.Contains("말벗 로봇") && e.Text.Contains("곁에서"));
            Check("우울증 — 말벗 로봇이 찾아가 곁에 앉아 이야기를 듣는다", dep != null && sc != null && sc.RobotSessions >= 1 && seen.Text != null,
                $"{sad.Name} 단계 {sc?.Level} · 말벗 {m.Stats.RobotSessions} · {seen.Text}");

            // 4) 돌봄이 회복을 당긴다
            float gain = dep == null ? 0f : m.CureGain(sad, dep);
            Check("돌봄을 받으면 '곁에 누가 있어야' 낫는 병이 빨리 낫는다", gain > 0.5f, $"하루 회복 +{gain:0.00} · {m.CardLine(sad)}");

            // 5) 깊으면 사람이 이어받는다 (상담 자격자)
            var scared = crew[3];
            var pro = crew.First(c => c != scared && c != sad && c != tired && c.CanAct);
            if (!pro.Quals.Contains(Qual.Counseling)) pro.Quals.Add(Qual.Counseling);
            w.Ailments.Catch(scared, "ptsd", null, "시험");
            scared.Memory.Trauma = 0.7f;
            for (int i = 0; i < 36 && m.Stats.HumanSessions == 0; i++) { scared.Memory.Trauma = MathF.Max(scared.Memory.Trauma, 0.65f); Run(w, SimTime.Hours(1)); }
            var talk = w.Log.Entries.LastOrDefault(e => e.Text.Contains("오래 이야기를 나눴다"));
            Check("외상 후 스트레스(깊음) — 로봇만으로는 모자라 상담 자격자가 이어받는다", m.Stats.Escalations >= 1 && m.Stats.HumanSessions >= 1 && talk.Text != null,
                $"넘김 {m.Stats.Escalations} · 상담 {m.Stats.HumanSessions} · {talk.Text} · {w.Automation.Reasoning.LastOrDefault(r => r.text.Contains("이야기를 나눠")).text}");

            // 6) 말벗 로봇이 없으면 간호 · 의사 로봇이 대신
            bot.Wrecked = true;
            bot.State = RobotState.Lost;
            int stand0 = m.Stats.StandIns;
            var lonely = crew[4];
            w.Ailments.Catch(lonely, "insomnia", null, "시험");
            for (int i = 0; i < 30 && m.Stats.StandIns == stand0; i++) Run(w, SimTime.Hours(1));
            Check("말벗 로봇이 부서지면 간호 · 의사 로봇이 대신 간다 (덜 듣는다)", m.Stats.StandIns > stand0,
                $"대신 {m.Stats.StandIns - stand0} · {w.Log.Entries.LastOrDefault(e => e.Text.Contains("곁에서") && !e.Text.Contains("말벗 로봇")).Text}");

            // 7) 불면 — 잠 처방
            for (int i = 0; i < 26 && m.Stats.SleepAids == 0; i++) Run(w, SimTime.Hours(1));
            Check("불면 — 밤에 잠 처방(조명 · 안대 · 백색 소음)을 해 잠을 덜 설친다", m.Stats.SleepAids >= 1, $"처방 {m.Stats.SleepAids} · 처방 중 {m.Aided(lonely)}");

            // 8) 오락실로 고치는 안 (승무원이 벌써 고쳤으면 그 기록)
            var (want, why) = m.ArcadeWant();
            var plan = Evolution.Candidates(w).FirstOrDefault(p => p.Kind == UpgradeKind.Repurpose && (RoomType)p.Circuit == RoomType.Arcade);
            var built = w.Ship.KindOf(RoomType.Arcade).FirstOrDefault();
            var builtLog = w.Log.Entries.LastOrDefault(e => e.Text.Contains("오락실로 고쳤다"));
            Check("마음이 지친 사람이 늘면 휴게실 하나를 오락실로 고친다 (안 → 개조)", built != null && builtLog.Text != null || want > 0f && plan != null,
                built != null ? $"벌써 고쳤다: {builtLog.Text}" : $"{want:0.00} {why} · {plan?.Why}");

            // 9) 오락실 설비
            var arcade = built;
            if (arcade == null && w.Ship.RoomsOf(RoomType.Lounge).FirstOrDefault(r => r.Special == null) is Room lounge)
            {
                lounge.FormerPurposes.Add(lounge.Name);
                lounge.Special = RoomType.Arcade; // 개조를 마친 것과 같다
                arcade = lounge;
            }
            var mods = Modules.Candidates(w).Where(p => p.Kind == UpgradeKind.Module && (FurnitureType)p.Circuit is FurnitureType.ArcadeCabinet or FurnitureType.VrChair).ToList();
            bool onlyArcade = mods.All(p => p.Target.Room?.Kind == RoomType.Arcade);
            var fixer = crew.First(c => c.CanAct);
            bool Have(FurnitureType t) => arcade != null && arcade.Furniture.Any(f => f.Type == t);
            bool put = arcade != null
                       && (Have(FurnitureType.ArcadeCabinet) || Modules.Install(w, arcade, FurnitureType.ArcadeCabinet, fixer, Cell.FromPosition(arcade.Center)))
                       && (Have(FurnitureType.VrChair) || Modules.Install(w, arcade, FurnitureType.VrChair, fixer, Cell.FromPosition(arcade.Center)));
            Check("오락실이 생기면 오락기 · 가상 고향 의자를 들인다 (오락실에만)", arcade != null && (mods.Count >= 1 || Have(FurnitureType.ArcadeCabinet)) && onlyArcade && put && arcade.Name == "오락실",
                $"{arcade?.Name} · 후보 {string.Join(", ", mods.Select(p => $"{Modules.Name((FurnitureType)p.Circuit)}@{p.Target.Room?.Name}"))} · 쉼 배율 {AmbienceSystem.RelaxFactor(arcade):0.00}");

            // 10) 오락실에서 쉬면 향수병이 풀린다
            var home = crew.FirstOrDefault(c => !c.BornAboard && c.CanAct && c != scared) ?? crew[0];
            w.Homeland.Homesick[home.Id] = 0.8f;
            float h0 = w.Homeland.Of(home);
            int plays0 = m.Stats.HomeViews;
            for (int i = 0; i < 36 && m.Stats.HomeViews <= plays0; i++) // 잠 · 대화로도 조금씩 준다 — 의자를 쓸 때까지 본다
            {
                home.Needs.Stress = MathF.Max(home.Needs.Stress, 0.6f);
                Run(w, SimTime.Hours(1));
                if (Environment.GetEnvironmentVariable("MC_DEBUG") != null)
                    Console.WriteLine($"    {SimTime.Clock(w.Tick)} {home.Name} {home.Job?.Label} @{home.Room?.Name} · 스트레스 {home.Needs.Stress:0.00} · {string.Join(" / ", (home.LastEvaluations ?? System.Array.Empty<Evaluation>()).Take(3).Select(e => $"{e.Activity.Label} {e.Score:0.00}"))}");
            }
            Check("지치고 고향이 그리운 사람은 오락실로 가고 · 가상 고향 의자에서 향수병이 풀린다", m.Stats.Plays >= 1 && m.Stats.HomeViews > plays0 && w.Homeland.Of(home) < h0,
                $"{home.Name} 향수병 {h0 * 100:0} → {w.Homeland.Of(home) * 100:0} · 오락실 {m.Stats.Plays}시간 · 고향 풍경 {m.Stats.HomeViews}");

            // 11) 붐비면 말벗 로봇을 더
            bot.Wrecked = false; bot.State = RobotState.Docked;
            foreach (var c in crew.Skip(5).Take(5)) w.Ailments.Catch(c, "depression", null, "시험");
            bool more = false;
            for (int i = 0; i < 60 && !more; i++) { Run(w, SimTime.Hours(1)); more = m.Candidates().Any(); }
            Check("환자가 말벗 로봇 한 대에 셋 넘게 · 이틀째면 한 대 더 짜 들이자는 안", more, $"마음 병 {m.Cases.Values.Count(x => x.Level >= 2)}명 · 말벗 {m.Companions}대 · {m.Candidates().FirstOrDefault()?.Why}");

            // 12) 향수병 한도
            var w2 = World.CreateDefault(seed + 1, 0, "Hanbit");
            var hc = w2.Crew.First(c => !c.BornAboard && !c.Passenger && !Life.Has(c, Habit.Homesick));
            Run(w2, SimTime.Hours(1));
            w2.Homeland.Homesick[hc.Id] = 0.7f;
            Run(w2, SimTime.TicksPerDay * 15);
            Check("향수병은 한도(보통 75%)로 천천히 다가가 멈춘다", w2.Homeland.Of(hc) <= 0.76f, $"{hc.Name} 70% → 15일 뒤 {w2.Homeland.Of(hc) * 100:0.0}% (한도 {HomelandSystem.Cap(hc) * 100:0})");

            // 13) 저장
            var snap = StateSave.Write(w);
            Check("마음 돌봄 기록도 상태 저장에 든다", snap.Length > 0, $"{snap.Length / 1024}KB · {m.Stats.Line()}");

            // 14) 끄면 예전처럼
            MindCareSystem.Off = true;
            var w3 = World.CreateDefault(seed, 0, "Hanbit");
            MindCareSystem.Off = false;
            Check("끄면 말벗 로봇 없이 예전 구성", !w3.Robots.Robots.Any(r => r.Kind == RobotKind.Companion) && w3.Robots.Robots.Count == w.Robots.Robots.Count(r => r.Kind != RobotKind.Companion) - 0,
                $"로봇 {w3.Robots.Robots.Count}대");
        }
        catch (Exception e) { Check("예외 없이", false, e.ToString()); }
        finally { GenerationSystem.QuietOff = false; PlanetSystem.Off = false; PurposeSystem.Off = false; MindCareSystem.Off = false; }
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
