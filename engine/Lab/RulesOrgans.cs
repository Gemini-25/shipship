using ShipEngine;

namespace Lab;

/// <summary>⑦ 생명 (바퀴 3) — 장기 · 심정지 · 약 · 진단 · 수술. docs/ENGINE.md '장기 · 약 · 수술'</summary>
public static partial class Program
{
    private static void OrganRules()
    {
        if (!Do("장기")) return;
        Console.WriteLine("\n[⑦ 장기 · 심정지]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        // 몸만 놓은 세계: 공기 · 겉면을 22℃로 붙들고 · 먹고 마시게 한다
        void Keep(Sim s, Life life)
        {
            if (s.Tick % 25 == 0)
            {
                var f = s.Get<Field>();
                for (int c = 0; c < lay.N; c++) if (lay.IsSpace(c)) { f.AddHeat(c, (295.15 - f.Temp(c)) * f.GasC(c)); f.Ts[c] = 295.15; f.Tf[c] = 295.15; }
            }
            for (int b = 0; b < life.Bodies.Count; b++) { if (life[b].Reserve < 1) life.Eat(b, 1.15); if (life[b].Water < Mind.BodyWater0) life.Drink(b, 5); }
        }

        // ── 심정지와 도움: 잔떨림으로 멎은 열 몸 — 아무도 없으면 10분 안에 모두 죽고 (뇌가 크게 상한 채) ·
        //    1분 뒤부터 가슴을 누르면 10분에도 모두 살아 있고 · 누르면서 4분에 제세동기(2분마다 다시)면 한 시간 뒤 반 넘게 살아 깨어 있다 (뇌 손상 작게) ──
        {
            (int alive10, int alive60, int awake60, float brain, float heart, int deadNoHelp) Run(int help)
            {
                var s = Worlds.Full(lay, 7);
                var life = s.Get<Life>();
                var ids = Enumerable.Range(0, 10).Select(i => life.AddBody(At(12 + i, 2))).ToArray();
                s.Run(5);
                int root = s.Causes.Add(CauseKind.Story, "심장이 갑자기 떨린다", s.Tick);
                foreach (int id in ids) life.StopHeart(s, id, 1, "잔떨림 — 심장이 멎었다", root);
                long t0 = s.Tick; int alive10 = 0;
                for (long k = 0; k < Time.Hours(1); k++)
                {
                    s.Step(); Keep(s, life);
                    long m = s.Tick - t0;
                    foreach (int id in ids)
                    {
                        var b = life[id];
                        if (b.Alive == 0 || b.Arrest == 0) continue;
                        if (help >= 1 && m >= Time.Minutes(1)) life.Compress(s, id);
                        if (help >= 2 && m >= Time.Minutes(4) && b.Arrest == 1 && s.Tick - b.ShockAt >= Time.Minutes(2)) life.Defibrillate(s, id, root);
                    }
                    if (m == Time.Minutes(10)) alive10 = ids.Count(id => life[id].Alive != 0);
                }
                var live = ids.Where(id => life[id].Alive != 0).ToArray();
                return (alive10, live.Length, live.Count(id => life[id].Conscious != 0), live.Length > 0 ? live.Select(id => life[id].Brain).OrderBy(x => x).ElementAt(live.Length / 2) : 0, live.Length > 0 ? live.Average(id => life[id].Heart) : 0,
                    ids.Count(id => life[id].Alive == 0 && life[id].Brain >= 0.5f));
            }
            var none = Run(0); var cpr = Run(1); var aed = Run(2);
            if (Debug) Console.WriteLine($"    없음 {none} / 압박 {cpr} / 압박+제세동 {aed}");
            Check("심정지와 도움: 잔떨림 열 몸 — 아무도 없으면 10분 안에 모두 죽고(뇌가 크게 상한 채) · 가슴을 누르면 10분에도 모두 살아 있고 · 누르며 4분에 제세동기면 한 시간 뒤 반 넘게 깨어 있다 (살아난 이 대부분은 뇌 손상이 작고 · 심장 +0.2 — 늦게 살아난 이는 뇌가 상한다)",
                none.alive10 == 0 && none.deadNoHelp >= 8 && cpr.alive10 == 10 && aed.awake60 >= 6 && aed.brain < 0.1f && aed.heart >= 0.15f,
                $"없음: 10분 {none.alive10}명 (뇌 상한 채 죽음 {none.deadNoHelp}) / 압박: 10분 {cpr.alive10} · 한 시간 {cpr.alive60} / 압박+제세동: 한 시간 {aed.alive60} · 깨어남 {aed.awake60} · 뇌 가운데값 {aed.brain:0.00} · 심장 평균 {aed.heart:0.00}");
        }

        // ── 함께 살린다: 오른쪽 방에서 한 사람의 심장이 떤다 — 곁의 동료가 다가가 가슴을 누르고 · 주컴퓨터가 '심정지'를 알려 로봇이 보관함의 제세동기를 가져와 친다 → 산다 /
        //    혼자면 죽는다 (고치기 전: 안전한 방에 쓰러진 이에게 아무도 다가가지 않았다 — 로봇도 '치료는 의무관의 몫'으로 돌아갔다) ──
        {
            (bool alive, bool awake, bool pressed, bool shocked, string log) Run(bool friend, bool robot)
            {
                var s = Worlds.Full(lay, 5);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var sm = s.Get<ShipMind>();
                sm.Policy.AskPeople = 0;
                int v = m.AddPerson(s, At(14, 2));
                int f = friend ? m.AddPerson(s, At(18, 3)) : -1;
                if (robot) m.AddRobot(s, At(3, 7), AgentKind.Robot, At(3, 7));
                int locker = lay.Furniture.First(fu => fu.Type == "SuitLocker").Id;
                m.Stock[locker * m.ItemCount + m.iAed] = 1;
                s.Run(5);
                int vb = m[v].C.Body;
                int root = s.Causes.Add(CauseKind.Story, "숨은 심장병", s.Tick);
                life.StopHeart(s, vb, 1, "심장이 갑자기 떨린다 — 잔떨림", root);
                bool pressed = false;
                for (long k = 0; k < Time.Hours(1); k++)
                {
                    s.Step();
                    foreach (var a in m.Agents) if (a.IsPerson) { a.C.Hunger = 0; a.C.Thirst = 0; a.C.Sleepy = 0; }
                    if (f >= 0 && m[f].C.Goal == GoalKind.Cpr && s.Tick - life[vb].CprAt <= 1) pressed = true;
                }
                bool shocked = s.Facts.Since(0).Any(x => x.Kind == FactKind.Revived && x.Size == 1);
                string log = string.Join(" / ", sm.Log.Select(l => l.text).Where(t => t.Contains("심정지")).Take(2));
                if (Debug) Console.WriteLine($"    동료 {friend} 로봇 {robot}: 삶 {life[vb].Alive} · 의식 {life[vb].Conscious} · 멎음 {life[vb].Arrest} · 뇌 {life[vb].Brain:0.00} · 누름 {pressed} · 제세동 {shocked} · {log} · 동료 목표 {(f >= 0 ? m[f].C.Goal : GoalKind.Idle)}");
                return (life[vb].Alive != 0, life[vb].Conscious != 0, pressed, shocked, log);
            }
            var both = Run(true, true); var alone = Run(false, false);
            Check("함께 살린다: 심장이 떨린 이 — 곁의 동료가 다가가 가슴을 누르고 · 주컴퓨터가 '심정지'를 알려 로봇이 제세동기를 가져와 쳐서 산다 / 혼자면 죽는다",
                both.alive && both.awake && both.pressed && both.shocked && both.log.Contains("심정지") && !alone.alive,
                $"함께: 삶 {both.alive} · 깨어남 {both.awake} · 동료가 누름 {both.pressed} · 제세동 {both.shocked} · '{both.log}' / 혼자: 삶 {alone.alive}");
        }

        // ── 장기가 남는다: 뇌 0.4 · 심장 0.4 · 폐 0.9로 상한 몸이 60일 동안 낫으면 — 뇌는 0.16 · 심장은 0.2 · 폐는 흉터 0.25 위로 남는다 ──
        {
            var b = new Body { Alive = 1, Brain = 0.4f, Heart = 0.4f, LungDamage = 0.9f };
            for (int d = 0; d < 60 * 24; d++) { Life.HealOrgans(ref b, 3600); Life.LungHeal(ref b, 3600); }
            Check("장기가 남는다: 뇌 0.4 · 심장 0.4 · 폐 0.9 — 60일 뒤 뇌는 가장 컸던 것의 40% · 심장 50% · 폐는 흉터(0.4 넘은 몫의 절반) 위로 남는다",
                Math.Abs(b.Brain - 0.16f) < 0.01f && Math.Abs(b.Heart - 0.2f) < 0.01f && Math.Abs(b.LungDamage - 0.25f) < 0.01f,
                $"뇌 {b.Brain:0.000} · 심장 {b.Heart:0.000} · 폐 {b.LungDamage:0.000} (흉터 {b.LungScar:0.000})");
        }

        // ── 심정지의 방아쇠: 감전된 40명 가운데 30%쯤 잔떨림 · 체온 29.7℃는 잔떨림 · 피 2.7L는 멎음 · 상한 심장(1.0)은 열두 시간에 몇이 저절로 떨고 · 성한 심장은 하나도 ──
        {
            var s = Worlds.Full(lay, 9);
            var life = s.Get<Life>();
            int cause = s.Causes.Add(CauseKind.Electric, "물에 젖은 배전반을 만졌다", s.Tick);
            var zap = Enumerable.Range(0, 40).Select(i => life.AddBody(At(12 + i % 10, 1 + i / 10))).ToArray();
            int cold = life.AddBody(At(2, 1)), bled = life.AddBody(At(4, 1));
            var sick = Enumerable.Range(0, 30).Select(i => life.AddBody(At(1 + i % 20, 7 + i / 20))).ToArray();
            var well = Enumerable.Range(0, 30).Select(i => life.AddBody(At(1 + i % 20, 8))).ToArray();
            s.Run(5);
            foreach (int id in zap) life.Electrocute(s, id, cause);
            life[cold].CoreK = 302.85; life[bled].BloodL = 2.7f;
            foreach (int id in sick) { life[id].Heart = 1; life[id].HeartPeak = 1; }
            int zapped = zap.Count(id => life[id].Arrest == 1);
            s.Run(10);
            byte coldA = life[cold].Arrest, bledA = life[bled].Arrest;
            int sickN = 0, wellN = 0;
            for (long k = 0; k < Time.Hours(12); k++)
            {
                s.Step(); Keep(s, life);
                foreach (int id in sick) if (life[id].Arrest != 0) { sickN++; life[id].Arrest = 0; life[id].Conscious = 1; life[id].Hypoxia = 0; }
                foreach (int id in well) if (life[id].Arrest != 0) wellN++;
            }
            Check("심정지의 방아쇠: 감전 40명 가운데 30%쯤 잔떨림 · 체온 29.7℃ 잔떨림 · 피 2.7L 멎음 · 상한 심장 30명은 열두 시간에 몇이 저절로 떨고 성한 심장 30명은 아무도",
                zapped is >= 6 and <= 20 && coldA == 1 && bledA == 2 && sickN >= 2 && wellN == 0,
                $"감전 잔떨림 {zapped}/40 · 저체온 {coldA} · 실혈 {bledA} · 부정맥 {sickN}/30 (성한 심장 {wellN})");
        }
    }
}
