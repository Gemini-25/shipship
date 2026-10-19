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
                int root = s.Causes.Add(CauseKind.Setup, "심장이 갑자기 떨린다", s.Tick);
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
                m.Stock[locker * m.ItemCount + m.iAed] = 1; m.Stock[locker * m.ItemCount + m.iEpi] = 4;   // 약품고 대신 보관함
                s.Run(5);
                int vb = m[v].C.Body;
                int root = s.Causes.Add(CauseKind.Setup, "숨은 심장병", s.Tick);
                life.StopHeart(s, vb, 1, "심장이 갑자기 떨린다 — 잔떨림", root);
                bool pressed = false;
                long first = -1, t0 = s.Tick;
                for (long k = 0; k < Time.Hours(1); k++)
                {
                    s.Step();
                    if (first < 0 && life.Shocks > 0) first = (s.Tick - t0) * 3600 / Time.TicksPerHour;
                    foreach (var a in m.Agents) if (a.IsPerson) { a.C.Hunger = 0; a.C.Thirst = 0; a.C.Sleepy = 0; }
                    if (f >= 0 && m[f].C.Goal == GoalKind.Cpr && s.Tick - life[vb].CprAt <= 1) pressed = true;
                }
                bool shocked = s.Facts.Since(0).Any(x => x.Kind == FactKind.Revived && x.Size == 1);
                string log = string.Join(" / ", sm.Log.Select(l => l.text).Where(t => t.Contains("심정지")).Take(2));
                if (Debug) Console.WriteLine($"    동료 {friend} 로봇 {robot}: 삶 {life[vb].Alive} · 의식 {life[vb].Conscious} · 멎음 {life[vb].Arrest} · 뇌 {life[vb].Brain:0.00} · 누름 {pressed} · 제세동 {shocked} · {log} · 동료 목표 {(f >= 0 ? m[f].C.Goal : GoalKind.Idle)} · 제세동 {life.Shocks}번 (빗나감 {life.ShockMisses}) · 첫 제세동 {first / 60.0:0.0}분");
                return (life[vb].Alive != 0, life[vb].Conscious != 0, pressed, shocked, log);
            }
            var both = Run(true, true); var alone = Run(false, false);
            Check("함께 살린다: 심장이 떨린 이 — 곁의 동료가 다가가 가슴을 누르고 · 주컴퓨터가 '심정지'를 알려 로봇이 제세동기를 가져와 쳐서 산다 / 혼자면 죽는다",
                both.alive && both.awake && both.pressed && both.shocked && both.log.Contains("심정지") && !alone.alive,
                $"함께: 삶 {both.alive} · 깨어남 {both.awake} · 동료가 누름 {both.pressed} · 제세동 {both.shocked} · '{both.log}' / 혼자: 삶 {alone.alive}");
        }

        // ── 약 (나): 진통제 — 크게 덴 이(화상 0.9)에게 10mg이면 아픔이 크게 줄고 · 내성 2인 이에게 같은 10mg은 반도 안 듣고 ·
        //    처음 맞는 이에게 70mg은 숨이 얕아져 쓰러지지만 · 내성 2인 이에게 70mg은 견딘다 ──
        {
            var s = Worlds.Full(lay, 11);
            var life = s.Get<Life>();
            int naive = life.AddBody(At(12, 1)), tol = life.AddBody(At(14, 1)), od = life.AddBody(At(16, 1)), odTol = life.AddBody(At(18, 1)), ctl = life.AddBody(At(20, 1));
            int cause = s.Causes.Add(CauseKind.Person, "의무관이 진통제를 놓았다", s.Tick);
            foreach (int id in new[] { naive, tol, od, odTol, ctl }) life[id].Burn = 0.9f;
            life[tol].OpioidTol = 2; life[odTol].OpioidTol = 2;
            s.Run(5);
            life.Give(s, naive, Drug.Opioid, 10, cause); life.Give(s, tol, Drug.Opioid, 10, cause);
            life.Give(s, od, Drug.Opioid, 70, cause); life.Give(s, odTol, Drug.Opioid, 70, cause);
            bool odDown = false, odTolDown = false;
            for (long k = 0; k < Time.Minutes(30); k++) { s.Step(); Keep(s, life); odDown |= life[od].Conscious == 0; odTolDown |= life[odTol].Conscious == 0; }
            double pn = Life.FeltPain(life[naive]), pt = Life.FeltPain(life[tol]), pc = Life.FeltPain(life[ctl]);
            Check("약 — 진통제 용량 · 과용 · 내성: 크게 덴 이에게 10mg은 아픔을 크게 덜고 · 내성 2인 이에게 같은 10mg은 반도 안 듣고 · 처음 맞는 이에게 70mg은 숨이 얕아져 쓰러지고 · 내성 2인 이는 70mg을 견딘다",
                pc - pn > 0.3 && pc - pt < 0.5 * (pc - pn) && odDown && !odTolDown,
                $"아픔: 안 맞음 {pc:0.00} · 10mg {pn:0.00} · 내성 2에 10mg {pt:0.00} / 70mg 처음 쓰러짐 {odDown} (피의 산소 {life[od].SpO2:0.00}) · 내성 2 쓰러짐 {odTolDown}");
        }

        // ── 약 (나): 사흘 동안 네 시간마다 10mg — 내성이 1 넘게 쌓이고 · 끊은 뒤 하루면 다친 데 없이도 금단으로 아프다 ──
        {
            var b = new Body { Alive = 1 };
            for (int d = 0; d < 18; d++) { b.Opioid += 10; for (int m = 0; m < 240; m++) Life.DrugKinetics(ref b, 60); }
            float tolAfter = b.OpioidTol, dep = b.OpioidDep;
            for (int m = 0; m < 24 * 60; m++) Life.DrugKinetics(ref b, 60);
            Check("약 — 쓸수록 내성 · 끊으면 금단: 사흘 동안 네 시간마다 10mg이면 내성이 1 넘게 쌓이고 · 끊은 뒤 하루면 다친 데 없이도 금단으로 아프다 (0.2 넘게)",
                tolAfter > 1 && dep > 0.3 && Life.FeltPain(b) > 0.2,
                $"내성 {tolAfter:0.00} · 의존 {dep:0.00} · 끊고 하루 뒤 아픔 {Life.FeltPain(b):0.00}");
        }

        // ── 약 (나): 해열제 — 곪아 열이 나는 이에게 1g이면 한 시간 반 뒤 체온이 내리고 · 열다섯 시간 동안 시간마다 1g(15g)이면 간이 상하고 · 여섯 시간마다 1g(하루 4g) 이틀은 간이 멀쩡하다 ──
        {
            var s = Worlds.Full(lay, 13);
            var life = s.Get<Life>();
            int fe = life.AddBody(At(12, 1)), fc = life.AddBody(At(14, 1));
            int cause = s.Causes.Add(CauseKind.Person, "해열제를 먹였다", s.Tick);
            foreach (int id in new[] { fe, fc }) { life[id].Infect = 0.6f; }
            for (long k = 0; k < Time.Hours(3); k++) { s.Step(); Keep(s, life); }
            life.Give(s, fe, Drug.Apap, 1000, cause);
            for (long k = 0; k < Time.Hours(1.5); k++) { s.Step(); Keep(s, life); }
            double dT = life[fc].CoreK - life[fe].CoreK;
            var over = new Body { Alive = 1 }; var safe = new Body { Alive = 1 };
            for (int h = 0; h < 48; h++)
            {
                if (h < 15) { over.Apap += 1000; over.ApapDay += 1000; }
                if (h % 6 == 0) { safe.Apap += 1000; safe.ApapDay += 1000; }
                for (int m = 0; m < 60; m++) { Life.DrugKinetics(ref over, 60); Life.DrugKinetics(ref safe, 60); }
            }
            Check("약 — 해열제 · 간: 곪아 열이 나는 이에게 1g이면 한 시간 반 뒤 0.4℃ 넘게 내리고 · 시간마다 1g씩 15g이면 간이 크게 상하고(0.5 넘게) · 여섯 시간마다 1g(하루 4g) 이틀은 간이 멀쩡하다",
                dT > 0.4 && over.LiverPeak > 0.5 && safe.LiverPeak < 0.02f,
                $"열 내림 {dT:0.00}℃ · 15g 간 가장 {over.LiverPeak:0.00} · 하루 4g 간 {safe.LiverPeak:0.000}");
        }

        // ── 약 (나): 항생제 내성균 — 곪음 0.8에 사흘 한 벌이면 걷히고 · 하루치만 먹고 끊으면 곪음이 남아 내성균이 되고 · 내성균이면 같은 한 벌로도 다 걷히지 않는다 /
        //    배 안에서: 열두 시간치만 먹인 이는 내성균이 되고 원인 사슬에 '약이 모자랐다'가 남는다 ──
        {
            Body Course(float hours, byte resist)
            {
                var b = new Body { Alive = 1, Infect = 0.8f, Resist = resist, Abx = hours };
                for (int m = 0; m < 72 * 60; m++) Life.AbxKinetics(ref b, 60);
                return b;
            }
            var full = Course(72, 0); var part = Course(24, 0); var res = Course(72, 1);
            var s = Worlds.Full(lay, 14);
            var life = s.Get<Life>();
            int pb = life.AddBody(At(12, 1));
            life[pb].Infect = 0.5f; life[pb].Dressed = 1000;
            s.Run(5);
            int cause = s.Causes.Add(CauseKind.Person, "약품고에 항생제가 모자라 열두 시간치만 먹였다", s.Tick);
            life.Give(s, pb, Drug.Abx, 12, cause);
            for (long k = 0; k < Time.Hours(12.5); k++) { s.Step(); Keep(s, life); }
            string why = s.Causes.Text(life[pb].InfectCause), root = s.Causes.Text(s.Causes[life[pb].InfectCause].Parent);
            Check("약 — 항생제 내성균: 곪음 0.8에 사흘 한 벌이면 걷히고 · 하루치만 먹고 끊으면 곪음이 남아 내성균이 되고 · 내성균이면 같은 한 벌로도 다 걷히지 않는다 / 배 안에서 열두 시간치만 먹인 이는 내성균 (원인: 모자랐다)",
                full.Infect < 0.01f && part.Resist == 1 && part.Infect > 0.05f && res.Infect > 0.05f && life[pb].Resist == 1 && why.Contains("내성균") && root.Contains("모자라"),
                $"한 벌: {full.Infect:0.00} / 하루치: {part.Infect:0.00} · 내성균 {part.Resist} / 내성균에 한 벌: {res.Infect:0.00} / 배 안: 내성균 {life[pb].Resist} 곪음 {life[pb].Infect:0.00} · '{why}' ← '{root}'");
        }

        // ── 약 (나): 해독제 · 아드레날린 — 시안화수소 1.5에 쓰러진 이는 해독제를 맞으면 20분에 0.3 아래 · 안 맞으면 1 넘게 남는다 /
        //    잔떨림 마흔 몸에 누르며 2분에 제세동기 한 번 — 아드레날린을 맞은 쪽이 더 많이 살아난다 ──
        {
            var s = Worlds.Full(lay, 15);
            var life = s.Get<Life>();
            int ad = life.AddBody(At(12, 1)), nd = life.AddBody(At(14, 1));
            int cause = s.Causes.Add(CauseKind.Person, "해독제를 놓았다", s.Tick);
            life[ad].CN = 1.5f; life[nd].CN = 1.5f;
            s.Run(5);
            life.Give(s, ad, Drug.Antidote, 1, cause);
            for (long k = 0; k < Time.Minutes(20); k++) { s.Step(); Keep(s, life); }
            int Revived(bool epi)
            {
                var w = Worlds.Full(lay, 16);
                var lf = w.Get<Life>();
                var ids = Enumerable.Range(0, 40).Select(i => lf.AddBody(At(12 + i % 10, 1 + i / 10))).ToArray();
                w.Run(5);
                int root = w.Causes.Add(CauseKind.Setup, "잔떨림", w.Tick);
                foreach (int id in ids) { lf.StopHeart(w, id, 1, "잔떨림", root); if (epi) lf.Give(w, id, Drug.Epi, 1, root); }
                for (long k = 0; k < Time.Minutes(2); k++) { w.Step(); foreach (int id in ids) lf.Compress(w, id); }
                foreach (int id in ids) lf.Defibrillate(w, id, root);
                return ids.Count(id => lf[id].Arrest == 0);
            }
            int withEpi = Revived(true), noEpi = Revived(false);
            Check("약 — 해독제 · 아드레날린: 시안화수소 1.5에 해독제를 맞으면 20분에 0.3 아래 · 안 맞으면 1 넘게 남는다 / 잔떨림 마흔에 제세동기 한 번 — 아드레날린을 맞은 쪽이 더 살아난다",
                life[ad].CN < 0.3f && life[nd].CN > 1 && withEpi > noEpi + 4,
                $"해독제 {life[ad].CN:0.00} · 없이 {life[nd].CN:0.00} / 다시 뜀: 아드레날린 {withEpi}/40 · 없이 {noEpi}/40");
        }

        // ── 약품고 — 돌보는 이의 약: 크게 데고 곪아 열이 나는 이 둘 (하나는 내성 2 · 하나는 한 시간 전에 진통제를 맞았다) · 약품고에 항생제 넷 ·
        //    로봇은 내성만큼 늘려 30mg · 한 시간 전에 맞은 이에게는 주지 않고 · 해열제 1g · 항생제는 먼저 돌본 이에게 사흘 치 · 다음 이에게는 남은 하루치만 (원인: 모자랐다) /
        //    서툰 사람(솜씨 0.1)은 내성을 모르고 10mg (열에 둘은 두 배) ──
        {
            (List<(int body, Drug d, float amt, string why)> doses, int abxLeft, int a, int b) Run(bool robot)
            {
                var s = Worlds.Full(lay, 17);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var sm = s.Get<ShipMind>();
                sm.Policy.AskPeople = 0;
                int pa = m.AddPerson(s, At(3, 4)), pb = robot ? m.AddPerson(s, At(5, 4)) : -1;
                int carer = robot ? m.AddRobot(s, At(3, 7), AgentKind.Robot, At(3, 7)) : m.AddPerson(s, At(6, 4));
                int locker = lay.Furniture.First(f => f.Type == "SuitLocker").Id;
                foreach (var (it, n) in new[] { (m.iMedkit, 2), (m.iOpioid, 3), (m.iApap, 3), (m.iAbx, 4) }) m.Stock[locker * m.ItemCount + it] = n;
                s.Run(5);
                int hurt = s.Causes.Add(CauseKind.Setup, "조리대 불에 데었다", s.Tick);
                foreach (int p in new[] { pa, pb }.Where(p => p >= 0))
                {
                    ref var b = ref life[m[p].C.Body];
                    b.Burn = 0.75f; b.Pain = 0.3f; b.Infect = 0.5f; b.HurtCause = hurt;
                }
                { ref var b = ref life[m[pa].C.Body]; b.OpioidTol = 2; b.OpioidDep = 1; b.Opioid = 7; }   // 늘 맞던 이 (약이 떨어지면 금단으로 쓰러진다)
                if (pb >= 0) { ref var b = ref life[m[pb].C.Body]; b.Opioid = 1.5f; b.OpioidAt = s.Tick - Time.Hours(1); }
                if (!robot)
                {
                    ref var cc = ref m[carer].C; cc.SkMedic = 0.1f;
                    m.Assign(s, carer, GoalKind.Treat, m[pa].C.Body, new Belief { Kind = BeliefKind.Injured, Cell = life[m[pa].C.Body].Cell, Subject = m[pa].C.Body, Conf = 1 }, s.Tick + Time.Minutes(30));
                }
                long t0 = s.Tick;
                for (long k = 0; k < Time.Hours(1); k++)
                {
                    s.Step();
                    foreach (var ag in m.Agents) if (ag.IsPerson) { ag.C.Hunger = 0; ag.C.Thirst = 0; ag.C.Sleepy = 0; }
                }
                var doses = s.Facts.Since(0).Where(f => f.Kind == FactKind.Dosed && f.Tick > t0).Select(f => (f.Actor, (Drug)f.Other, f.Size, s.Causes.Text(f.Cause))).ToList();
                if (Debug) foreach (var d in doses) Console.WriteLine($"    {(robot ? "로봇" : "서툰 이")}: 몸 {d.Item1} · {d.Item2} {d.Item3} · {d.Item4}");
                if (Debug) Console.WriteLine($"    {(robot ? "로봇" : "서툰 이")} 일지: {string.Join(" / ", sm.Log.Select(l => l.text).Take(6))} · 돌봄 {m.Treats} · 돌보는 이 목표 {m[carer].C.Goal}");
                return (doses, m.Stock[locker * m.ItemCount + m.iAbx], m[pa].C.Body, pb >= 0 ? m[pb].C.Body : -1);
            }
            var r = Run(true); var u = Run(false);
            float Mg(List<(int body, Drug d, float amt, string why)> ds, int b, Drug d) => ds.Where(x => x.body == b && x.d == d).Sum(x => x.amt);
            var abx = r.doses.Where(x => x.d == Drug.Abx).OrderByDescending(x => x.amt).ToList();
            Check("약품고 — 돌보는 이의 약: 로봇은 내성 2인 이에게 30mg · 한 시간 전에 맞은 이에게는 주지 않고 · 둘 다 해열제 1g · 항생제 넷은 사흘 치 + 남은 하루치 (원인: 모자랐다) / 서툰 사람은 내성을 모르고 10mg (열에 둘은 20mg)",
                Mg(r.doses, r.a, Drug.Opioid) == 30 && Mg(r.doses, r.b, Drug.Opioid) == 0 && Mg(r.doses, r.a, Drug.Apap) == 1000 && Mg(r.doses, r.b, Drug.Apap) == 1000
                && abx.Count == 2 && abx[0].amt == 72 && abx[1].amt == 24 && abx[1].why.Contains("모자라") && r.abxLeft == 0
                && Mg(u.doses, u.a, Drug.Opioid) is 10 or 20,
                $"로봇: 진통제 {Mg(r.doses, r.a, Drug.Opioid)} · {Mg(r.doses, r.b, Drug.Opioid)}mg · 해열제 {Mg(r.doses, r.a, Drug.Apap)} · {Mg(r.doses, r.b, Drug.Apap)} · 항생제 {string.Join("+", abx.Select(x => x.amt))}시간 '{(abx.Count > 1 ? abx[1].why : "")}' · 남은 항생제 {r.abxLeft} / 서툰 이: 진통제 {Mg(u.doses, u.a, Drug.Opioid)}mg");
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
