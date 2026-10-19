using ShipEngine;

namespace Lab;

/// <summary>⑦ 생명 (바퀴 2) — 돌봄 · 상처 감염 · 옮는 병 · 잠의 질 · 몸무게. docs/DEPTH.md</summary>
public static partial class Program
{
    private static void CareRules()
    {
        if (!Do("몸의 돌봄")) return;
        Console.WriteLine("\n[⑦ 몸의 돌봄]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        int right = lay.Room[At(15, 1)];
        // 몸만 놓은 세계: 공조 대신 방 공기 · 겉면을 22℃로 붙들고 · 먹고 마시게 한다 (사람의 마음은 없다 — 몸은 제자리)
        void Keep(Sim s, Life life)
        {
            if (s.Tick % 25 == 0)
            {
                var f = s.Get<Field>();
                for (int c = 0; c < lay.N; c++) if (lay.IsSpace(c)) { f.AddHeat(c, (295.15 - f.Temp(c)) * f.GasC(c)); f.Ts[c] = 295.15; f.Tf[c] = 295.15; }
            }
            for (int b = 0; b < life.Bodies.Count; b++) { if (life[b].Reserve < 1) life.Eat(b, 1.15); if (life[b].Water < Mind.BodyWater0) life.Drink(b, 5); }
        }

        // ── 돌봄: 안전한 방에서 크게 덴 이(화상 0.9 — 저절로 낫지 않는다)를 주컴퓨터가 '다친 사람'으로 알고 로봇이 구급 상자로 돌본다 → 낫기 시작한다
        //    대조: 로봇이 없고 돌볼 솜씨 있는 이도 없으면 그대로 ──
        {
            (bool treated, float burn0, float burn, float dressed, float abx, string log) Run(bool robot)
            {
                var s = Worlds.Full(lay, 3);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var sm = s.Get<ShipMind>();
                sm.Policy.AskPeople = 0;
                int p = m.AddPerson(s, At(3, 4));
                if (robot) m.AddRobot(s, At(3, 7), AgentKind.Robot, At(3, 7));
                int locker = lay.Furniture.First(f => f.Type == "SuitLocker").Id;
                m.Stock[locker * m.ItemCount + m.iMedkit] = 2;
                s.Run(5);
                ref var b = ref life[m[p].C.Body];
                b.Burn = 0.9f; b.HurtCause = s.Causes.Add(CauseKind.Story, "조리대에 팔을 크게 데었다", s.Tick, b.Cell);
                float burn0 = b.Burn;
                for (int k = 0; k < Time.Hours(6); k++) { s.Step(); ref var c = ref m[p].C; c.Hunger = 0; c.Thirst = 0; c.Sleepy = 0; }
                var bb = life[m[p].C.Body];
                bool treated = s.Facts.Since(0).Any(f => f.Kind == FactKind.Treated && f.Other == p);
                string log = string.Join(" / ", sm.Log.Select(l => l.text).Where(t => t.Contains("다친") || t.Contains("돌봐")));
                if (Debug) Console.WriteLine($"    로봇 {robot}: 돌봄 {treated} · 화상 {burn0:0.00} → {bb.Burn:0.000} · 덮음 {bb.Dressed:0} · 항생제 {bb.Abx:0} · 곪음 {bb.Infect:0.00} · {log}");
                return (treated, burn0, bb.Burn, bb.Dressed, bb.Abx, log);
            }
            var cared = Run(true); var alone = Run(false);
            Check("돌봄: 안전한 방에서 크게 덴 이(화상 0.9)를 주컴퓨터가 '다친 사람'으로 알고 로봇이 구급 상자로 돌본다(덮개 · 항생제) → 낫기 시작한다 · 돌볼 이가 없으면 그대로",
                cared.treated && cared.dressed > 200 && cared.abx > 0 && cared.burn < cared.burn0 - 0.01f && cared.log.Contains("다친 사람") && !alone.treated && alone.burn >= alone.burn0 - 1e-4f,
                $"로봇: 돌봄 {cared.treated} · 화상 {cared.burn0:0.00} → {cared.burn:0.000} · 덮음 {cared.dressed:0}시간 · 항생제 {cared.abx:0}시간 / 혼자: 화상 {alone.burn:0.000}");
        }

        // ── 상처 감염: 덮지 않은 화상(0.7) 열 몸 가운데 하루 만에 여럿이 곪고 · 덮은 열 몸은 하나도 안 곪는다 ──
        {
            var s = Worlds.Full(lay, 4);
            var life = s.Get<Life>();
            var bare = Enumerable.Range(0, 10).Select(i => life.AddBody(At(12 + i, 2))).ToArray();
            var dressed = Enumerable.Range(0, 10).Select(i => life.AddBody(At(12 + i, 4))).ToArray();
            foreach (int b in bare.Concat(dressed)) life[b].Burn = 0.7f;
            foreach (int b in dressed) life.Treat(s, b, true);
            for (int k = 0; k < Time.Days(1); k++) { Keep(s, life); s.Step(); }
            int infBare = bare.Count(b => life[b].Infect > 0), infDressed = dressed.Count(b => life[b].Infect > 0);
            if (Debug) Console.WriteLine($"    곪음: 덮지 않음 {infBare}/10 · 덮음 {infDressed}/10 · 곪은 이의 체온 {string.Join(",", bare.Where(b => life[b].Infect > 0).Select(b => (life[b].CoreK - 273.15).ToString("0.0")))}");
            Check("상처 감염: 덮지 않은 화상(0.7) 열 몸 가운데 하루 만에 여럿이 곪고(하루 확률 0.35) · 덮은 열 몸은 하나도 곪지 않는다",
                infBare >= 2 && infDressed == 0, $"덮지 않음 {infBare}/10 · 덮음 {infDressed}/10");
        }

        // ── 곪으면 열 · 패혈증: 곪음 0.6이면 체온이 38℃ 넘게 · 0.98이면 몇 시간에 패혈증으로 쓰러진다 (원인: 곪은 상처) · 항생제를 받은 몸은 걷어 낸다 ──
        {
            var s = Worlds.Full(lay, 5);
            var life = s.Get<Life>();
            int fever = life.AddBody(At(12, 2)), septic = life.AddBody(At(14, 2)), abx = life.AddBody(At(16, 2));
            foreach (int b in new[] { fever, septic, abx }) { life[b].Burn = 0.5f; life[b].Dressed = 0; }
            life[fever].Infect = 0.6f; life[septic].Infect = 0.98f; life[abx].Infect = 0.98f;
            int root = s.Causes.Add(CauseKind.Story, "덮지 않은 상처가 곪았다", s.Tick, life[septic].Cell);
            life[septic].InfectCause = root;
            life.Treat(s, abx, true);
            float maxT = 0; long downAt = -1;
            for (int k = 0; k < Time.Hours(10); k++)
            {
                Keep(s, life); s.Step();
                maxT = Math.Max(maxT, (float)(life[fever].CoreK - 273.15));
                if (downAt < 0 && life[septic].Conscious == 0) downAt = s.Tick;
            }
            bool chain = downAt >= 0 && s.Facts.Since(0).Any(f => f.Kind == FactKind.Collapsed && f.Actor == septic && s.Causes.Root(f.Cause) == root);
            if (Debug) Console.WriteLine($"    열 {maxT:0.0}℃ · 패혈증 쓰러짐 {(downAt >= 0 ? Time.Clock(downAt) : "-")} (원인 사슬 {chain}) · 항생제 몸 곪음 {life[abx].Infect:0.00} 의식 {life[abx].Conscious}");
            Check("곪으면 열(38℃ 넘게) · 곪음이 1을 넘으면 패혈증으로 쓰러진다 (원인 사슬 끝이 곪은 상처) · 항생제를 받은 몸은 곪음을 걷어 내며 쓰러지지 않는다",
                maxT > 38 && chain && life[abx].Infect < 0.98f && life[abx].Conscious != 0, $"열 {maxT:0.0}℃ · 패혈증 {(downAt >= 0 ? "쓰러짐" : "없음")} · 항생제 몸 곪음 0.98 → {life[abx].Infect:0.00}");
        }

        // ── 옮는 병: 앓는 이 곁 2칸의 셋은 하루에 거의 다 옮고 · 다른 방 셋은 옮지 않는다 · 잠복 2일 뒤 앓고(열) · 앓은 지 5일이면 낫는다(면역) ──
        {
            var s = Worlds.Full(lay, 6);
            var life = s.Get<Life>();
            int sick = life.AddBody(At(16, 2));
            var near = new[] { life.AddBody(At(15, 2)), life.AddBody(At(17, 2)), life.AddBody(At(16, 3)) };
            var far = new[] { life.AddBody(At(3, 2)), life.AddBody(At(4, 2)), life.AddBody(At(5, 3)) };
            life.CatchCold(s, sick, s.Causes.Add(CauseKind.Story, "보급선에서 감기가 옮아 왔다", s.Tick));
            life[sick].Ill = 2; life[sick].IllH = 0;
            for (int k = 0; k < Time.Days(1); k++) { Keep(s, life); s.Step(); }
            int nearIll = near.Count(b => life[b].Ill != 0), farIll = far.Count(b => life[b].Ill != 0);
            // 단계: 잠복 끝 → 앓음(열) · 앓음 끝 → 나음
            int e = near.FirstOrDefault(b => life[b].Ill == 1, -1);
            bool stages = false; float feverC = 0;
            if (e >= 0)
            {
                life[e].IllH = (float)Life.IncubateHours - 0.2f;
                for (int k = 0; k < Time.Hours(3); k++) { Keep(s, life); s.Step(); feverC = Math.Max(feverC, (float)(life[e].CoreK - 273.15)); }
                bool sickNow = life[e].Ill == 2;
                life[e].IllH = (float)Life.SickHours - 0.1f;
                for (int k = 0; k < Time.Minutes(30); k++) { Keep(s, life); s.Step(); }
                stages = sickNow && life[e].Ill == 3;
            }
            if (Debug) Console.WriteLine($"    옮음: 곁 {nearIll}/3 · 다른 방 {farIll}/3 · 단계 {stages} · 앓을 때 체온 {feverC:0.0}℃ · 옮은 수 {life.Colds}");
            Check("옮는 병: 앓는 이 곁 2칸의 셋 가운데 하루에 둘 넘게 옮고 · 다른 방 셋은 옮지 않는다 · 잠복 2일 뒤 앓고(열 37.5℃ 넘게) · 앓은 지 5일이면 낫는다(면역)",
                nearIll >= 2 && farIll == 0 && stages && feverC > 37.5f, $"곁 {nearIll}/3 · 다른 방 {farIll}/3 · 단계 {stages} · 열 {feverC:0.0}℃");
        }

        // ── 앓는 이: 덜 성실한 이는 일하는 시간에도 일을 놓고 눕고 · 성실한 이는 무리해 일한다 · 주컴퓨터가 '열이 있습니다 — 쉬세요' ──
        {
            (int inBed, int atWork, string log) Run(float consc)
            {
                var s = Worlds.Full(lay, 7);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var sm = s.Get<ShipMind>();
                int p = m.AddPerson(s, At(14, 8));
                m.SetPersonality(p, 0.5f, consc, 0.5f, 0.5f, 0.5f);
                m[p].C.Station = lay.Furniture.First(f => f.Type == "Workbench").Id;
                s.Run(5);
                m[p].C.ShiftHour = (int)Time.HourOfDay(s.Tick);
                m[p].C.Bed = lay.Furniture.First(f => f.Type == "Bed").Cells[0];
                life.CatchCold(s, m[p].C.Body, s.Causes.Add(CauseKind.Story, "감기가 옮아 왔다", s.Tick));
                life[m[p].C.Body].Ill = 2;
                int inBed = 0, atWork = 0;
                for (int k = 0; k < Time.Hours(3); k++)
                {
                    s.Step(); ref var c = ref m[p].C; c.Hunger = 0; c.Thirst = 0; c.Bored = 0; c.Lonely = 0;
                    if (k > Time.Hours(1)) { if (c.Goal == GoalKind.Sleep) inBed++; if (c.Goal == GoalKind.Work) atWork++; }
                }
                string log = string.Join(" / ", sm.Log.Select(l => l.text).Where(t => t.Contains("열이")));
                if (Debug) Console.WriteLine($"    성실 {consc}: 누움 {inBed} · 일 {atWork} · 체온 {life[m[p].C.Body].CoreK - 273.15:0.0}℃ · {m.Thought(p)} · {log}");
                return (inBed, atWork, log);
            }
            var lazy = Run(0.1f); var keen = Run(0.95f);
            Check("앓는 이: 덜 성실한 이는 일하는 시간에도 눕고 · 성실한 이는 무리해 일한다 · 주컴퓨터가 '열이 있습니다 — 쉬세요 · 다른 분은 가까이 가지 마세요'",
                lazy.inBed > lazy.atWork && keen.atWork > keen.inBed && lazy.log.Contains("열이 있습니다") && lazy.log.Contains("가까이 가지 마세요"),
                $"덜 성실: 누움 {lazy.inBed} · 일 {lazy.atWork} / 성실: 누움 {keen.inBed} · 일 {keen.atWork}");
        }

        // ── 운동하고 난 몸의 열은 병이 아니다: 건강한 이가 힘든 운동으로 체온이 37.6℃를 넘어도 주컴퓨터는 '열이 있습니다'를 알리지 않는다 (관찰 장면 '중력이 꺼진 식당'이 드러낸 결함) ──
        {
            var s = Worlds.Full(lay, 9);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var sm = s.Get<ShipMind>();
            int p = m.AddPerson(s, At(14, 8));
            m[p].C.Bed = lay.Furniture.First(f => f.Type == "Bed").Cells[0];
            s.Run(5);
            m[p].C.ShiftHour = ((int)Time.HourOfDay(s.Tick) + 12) % 24;   // 쉬는 시간 (운동은 근무 밖에)
            float peak = 0;
            for (int k = 0; k < Time.Hours(2); k++)
            {
                ref var c = ref m[p].C; c.Unfit = 1; c.Hunger = 0; c.Thirst = 0; c.Sleepy = 0; c.Bored = 0;   // 운동하고 싶은 몸
                s.Step();
                peak = Math.Max(peak, (float)(life[m[p].C.Body].CoreK - 273.15));
            }
            string log = string.Join(" / ", sm.Log.Select(l => l.text).Where(t => t.Contains("열이")));
            Check("운동하고 난 몸의 열은 병이 아니다: 힘든 운동으로 체온이 37.6℃를 넘어도 주컴퓨터는 '열이 있습니다'를 알리지 않는다",
                m.Exercised > 0 && peak > 37.6 && log == "", $"운동 {m.Exercised}번 · 가장 높은 체온 {peak:0.00}℃ · 주컴퓨터: \"{log}\"");
        }

        // ── 잠의 질: 잠자리 곁에서 50dB 기계 소리가 되풀이되면 잠이 얕아(깨지는 않는다) 여섯 시간 뒤 졸림이 덜 풀린다 ──
        //    (조용한 잠자리 — 주컴퓨터(서버 · 1m 58dB)가 없는 같은 배: 주컴퓨터 곁 침대는 이어지는 바탕 소음 58dB만으로 잠이 얕다 · 소리 바퀴 2)
        {
            var quietLay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
            (float sleepy, float loss) Run(bool noisy)
            {
                var s = Worlds.Full(quietLay, 8);
                var m = s.Get<Mind>(); var snd = s.Get<Sound>();
                int bed = quietLay.Furniture.First(f => f.Type == "Bed").Id;
                int p = m.AddPerson(s, quietLay.Furniture[bed].Cells[0]);
                m[p].C.Bed = quietLay.Furniture[bed].Cells[0];
                s.Run(5);
                m[p].C.Sleepy = 1; m[p].C.Hunger = 0; m[p].C.Thirst = 0;
                float loss = 0; int n = 0;
                for (int k = 0; k < Time.Hours(6); k++)
                {
                    if (noisy && k % 25 == 0) snd.Emit(s, quietLay.Furniture[bed].Cells[0], 50, SoundKind.Machine);
                    s.Step(); m[p].C.Hunger = 0; m[p].C.Thirst = 0;
                    if (k > Time.Hours(1)) { loss += m[p].C.SleepLoss; n++; }
                }
                if (Debug) Console.WriteLine($"    {(noisy ? "시끄러움" : "조용함")}: 졸림 {m[p].C.Sleepy:0.00} · 얕음 {loss / Math.Max(1, n):0.00} · {m.Thought(p)}");
                return (m[p].C.Sleepy, loss / Math.Max(1, n));
            }
            var quiet = Run(false); var loud = Run(true);
            Check("잠의 질: 잠자리 곁 50dB 기계 소리가 되풀이되면 잠이 얕아(깨지는 않는다) 여섯 시간 뒤 졸림이 덜 풀린다 (WHO 밤 소음 40dB)",
                loud.loss > quiet.loss + 0.15f && loud.sleepy > quiet.sleepy + 0.05f, $"졸림 조용함 {quiet.sleepy:0.00} · 시끄러움 {loud.sleepy:0.00} / 얕음 {quiet.loss:0.00} · {loud.loss:0.00}");
        }

        // ── 몸무게: 지방을 9kg 잃은 몸(처음 70kg의 13%)은 걸음이 느리고 · 빨리 지친다 ──
        {
            var s = Worlds.Full(lay, 9);
            var m = s.Get<Mind>(); var life = s.Get<Life>();
            int thin = m.AddPerson(s, At(3, 2)), well = m.AddPerson(s, At(4, 2));
            life[m[thin].C.Body].Fat = Life.Fat0 - 35;
            s.Run(5);
            foreach (int p in new[] { thin, well }) m[p].C.Sleepy = 0;
            for (int k = 0; k < Time.Hours(4); k++) { s.Step(); foreach (int p in new[] { thin, well }) { ref var c = ref m[p].C; c.Hunger = 0; c.Thirst = 0; } }
            double lossT = Life.WeightLoss(life[m[thin].C.Body]);
            if (Debug) Console.WriteLine($"    잃은 몸무게 {lossT:P0} · 걸음 {m.BodyPace(m[thin]):0.00} / {m.BodyPace(m[well]):0.00} · 졸림 {m[thin].C.Sleepy:0.00} / {m[well].C.Sleepy:0.00}");
            Check("몸무게: 지방을 9kg 잃은 몸(13%)은 걸음이 느리고(0.96배 아래) · 네 시간에 더 졸리다 — 잃지 않은 몸은 그대로",
                lossT > 0.12 && m.BodyPace(m[thin]) < 0.96 && m.BodyPace(m[well]) > 0.999 && m[thin].C.Sleepy > m[well].C.Sleepy + 0.01f,
                $"잃은 몸무게 {lossT:P0} · 걸음 {m.BodyPace(m[thin]):0.00} · 졸림 {m[thin].C.Sleepy:0.00} / {m[well].C.Sleepy:0.00}");
        }
    }
}
