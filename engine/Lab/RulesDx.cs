using ShipEngine;

namespace Lab;

/// <summary>⑦ (다) 진단 — 증상으로 짐작하고 · 틀릴 수 있고 · 다시 보아 바로잡는다. docs/ENGINE.md '장기 · 약 · 수술' (다)</summary>
public static partial class Program
{
    /// <summary>진단 시험 배: 마음 시험 배 + 주컴퓨터 + 오른쪽 방에 치료 침대 하나 · 왼쪽 방(숙소)에 침대 셋.</summary>
    private static Layout DxShip() => Worlds.Ascii(
        Worlds.MindShipComputer.Replace("#.........#....a....a.r.#", "#.........#....a.h..a.r.#").Replace("#..e....q.#", "#..e.e.eq.#"),
        new Dictionary<char, string>(Worlds.MindLegend) { ['h'] = "MedBed" });

    /// <summary>수술 시험 배: 진단 시험 배 + 오른쪽 방에 진단 장치.</summary>
    private static Layout SurgShip() => Worlds.Ascii(
        Worlds.MindShipComputer.Replace("#.........#....a....a.r.#", "#.........#....a.h..a.r.#").Replace("#..e....q.#", "#..e.e.eq.#").Replace("#..s...c..#....a....a.s.#", "#..s...c..#.x..a....a.s.#"),
        new Dictionary<char, string>(Worlds.MindLegend) { ['h'] = "MedBed", ['x'] = "DiagnosticScanner" });

    private static void DxRules()
    {
        if (!Do("진단")) return;
        Console.WriteLine("\n[⑦ 진단]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";

        // ── 증상 · 숨은 병 · 닮은 병 · 맞힐 몫: 일산화탄소 0.14는 두통 · 메스꺼움 (열 없음) — 모르는 눈에는 감기 · 감기는 열 · 기침 · 상한 심장은 가슴 아픔 — 체함으로 보인다 ·
        //    몸 지식 0.1인 이가 맨눈으로 일산화탄소를 맞힐 몫은 0.2 남짓 · 의료 침대 검사면 0.7 · 아는 이(0.9)가 검사하면 0.98 · 로봇은 기체 센서로 0.8 남짓 ──
        {
            var co = new Body { Alive = 1, COHb = 0.14f, SpO2 = 0.97f };
            var cold = new Body { Alive = 1, Ill = 2, SpO2 = 0.97f };
            var heart = new Body { Alive = 1, Heart = 0.4f, SpO2 = 0.97f };
            Sym sc = Life.Symptoms(co), sl = Life.Symptoms(cold), sh = Life.Symptoms(heart);
            double naive = Life.DxChance(Ail.CO, co, 0.1, false, false, false), bed = Life.DxChance(Ail.CO, co, 0.1, true, false, false),
                expert = Life.DxChance(Ail.CO, co, 0.9, true, false, false), robot = Life.DxChance(Ail.CO, co, 0.3, false, false, true);
            Check("진단 — 증상 · 숨은 병 · 맞힐 몫: 일산화탄소 0.14는 두통 · 메스꺼움 (열 없음) — 모르는 눈엔 감기 · 감기는 열 · 기침 · 상한 심장은 가슴 아픔 — 체함 / 맨눈 0.2 남짓 · 의료 침대 0.7 · 아는 이 + 검사 0.98 · 로봇 기체 센서 0.8 남짓",
                (sc & (Sym.Headache | Sym.Nausea)) == (Sym.Headache | Sym.Nausea) && (sc & Sym.Fever) == 0 && Life.Ailment(co) == Ail.CO && Life.LookAlike(Ail.CO) == Ail.Cold
                && (sl & (Sym.Fever | Sym.Cough)) == (Sym.Fever | Sym.Cough) && Life.Ailment(cold) == Ail.Cold
                && (sh & Sym.ChestPain) != 0 && Life.Ailment(heart) == Ail.Heart && Life.LookAlike(Ail.Heart) == Ail.Indigestion
                && naive is > 0.15 and < 0.3 && bed is > 0.6 and < 0.8 && expert >= 0.98 && robot is > 0.75 and < 0.9,
                $"일산화탄소: {Life.SymText(sc)} → {Life.AilName(Life.LookAlike(Ail.CO))} / 감기: {Life.SymText(sl)} / 심장: {Life.SymText(sh)} → {Life.AilName(Life.LookAlike(Ail.Heart))} / 맞힐 몫 맨눈 {naive:0.00} · 침대 {bed:0.00} · 아는 이 {expert:0.00} · 로봇 {robot:0.00}");
        }

        // ── 감기로 보았다 — 실은 일산화탄소: 숙소에 일산화탄소가 140ppm (주컴퓨터 알림 150 아래) · 셋이 두통 · 메스꺼움 →
        //    몸 지식 0.05인 이가 보고 (셋 가운데 하나라도) 감기로 본다 → 그 이는 숙소 제 침대에 눕는다 (독한 방) → 네 시간이 지나도 낫지 않아 주컴퓨터가 다시 보게 하고 ·
        //    로봇이 기체 센서로 다시 보니 일산화탄소 → 치료 침대로 · 주컴퓨터가 진료 기록으로 숙소를 '일산화탄소'로 알린다 · 잘못 본 이는 배운다 (몸 지식 +0.05) ──
        {
            var lay = DxShip();
            int At(int x, int y) => Worlds.At(lay, x, y);
            var s = Worlds.Full(lay, 21);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var sm = s.Get<ShipMind>(); var f = s.Get<Field>();
            sm.Policy.AskPeople = 0;
            int room = lay.Room[At(3, 5)];
            var pats = new[] { m.AddPerson(s, At(2, 2), bed: At(3, 5)), m.AddPerson(s, At(5, 2), bed: At(5, 5)), m.AddPerson(s, At(7, 3), bed: At(7, 5)) };
            int carer = m.AddPerson(s, At(12, 7));
            { ref var cc = ref m[carer].C; cc.KnBody = 0.05f; cc.SkMedic = 0.1f; }
            float kn0 = m[carer].C.KnBody;
            void KeepCO()
            {
                foreach (int c in lay.Rooms[room].Cells)
                {
                    double want = 140e-6 * f.Moles(c), have = f.Ppm(f.T.gCO, c) * 1e-6 * f.Moles(c);
                    if (want > have) f.AddGas(c, f.T.gCO, want - have, f.Temp(c));
                }
            }
            s.Run(5);
            foreach (int p in pats) life[m[p].C.Body].COHb = 0.14f;
            int next = 0, robot = -1;
            long t0 = s.Tick, firstSleptInRoom = -1;
            int wrongFirst = -1;
            for (long k = 0; k < Time.Hours(7); k++)
            {
                if (k % Time.Minutes(1) == 0) KeepCO();
                s.Step();
                foreach (var ag in m.Agents) if (ag.IsPerson) { ag.C.Hunger = 0; ag.C.Thirst = 0; }
                ref var uc = ref m[carer].C;
                if (next < pats.Length && (uc.AssignedGoal != GoalKind.Treat || uc.AssignedUntil <= s.Tick))
                {
                    int vb = m[pats[next]].C.Body;
                    m.Assign(s, carer, GoalKind.Treat, vb, new Belief { Kind = BeliefKind.Injured, Cell = life[vb].Cell, Subject = vb, Conf = 1 }, s.Tick + Time.Minutes(40));
                    next++;
                }
                if (wrongFirst < 0) foreach (int p in pats) if (m[p].C.DxAil == Ail.Cold && m[p].C.DxBy == carer) wrongFirst = p;
                if (wrongFirst >= 0 && firstSleptInRoom < 0 && m[wrongFirst].C.Goal == GoalKind.Sleep && lay.Room[m[wrongFirst].C.Cell] == room) firstSleptInRoom = s.Tick - t0;
                if (robot < 0 && s.Tick - t0 >= Time.Hours(3)) robot = m.AddRobot(s, At(8, 5), AgentKind.Robot, At(8, 5));
            }
            var w = wrongFirst >= 0 ? m[wrongFirst] : null;
            bool corrected = w != null && w.C.DxAil == Ail.CO && w.C.DxBy == robot;
            string why = w != null ? s.Causes.Text(w.C.DxCause) : "";
            bool onBed = w != null && lay.Furniture.Any(fu => fu.Type == "MedBed" && fu.Cells.Contains(w.C.Cell));
            string log = string.Join(" / ", sm.Log.Select(l => l.text).Where(t => t.Contains("일산화탄소")).Take(3));
            if (Debug)
            {
                foreach (int p in pats) Console.WriteLine($"    {m.NameOf(p)}: 기록 {Life.AilName(m[p].C.DxAil)} (본 이 {m[p].C.DxBy}) · 일산화탄소 {life[m[p].C.Body].COHb:0.00} · 목표 {m[p].C.Goal} · '{s.Causes.Text(m[p].C.DxCause)}'");
                Console.WriteLine($"    일지 (일산화탄소): {string.Join(" / ", sm.Log.Select(l => l.text).Where(t => t.Contains("일산화탄소")))} · 진료 알림 {sm.ChartAlerts}");
            }
            Check("진단 — 감기로 보았다, 실은 일산화탄소: 숙소 140ppm (알림 아래) · 서툰 이(몸 지식 0.05)가 셋 가운데 하나라도 감기로 보고 · 그 이는 숙소 제 침대에 눕는다 → 네 시간 뒤 로봇이 다시 보니 일산화탄소 · 치료 침대로 · 주컴퓨터가 진료 기록으로 숙소를 알린다 · 잘못 본 이는 배운다",
                w != null && firstSleptInRoom >= 0 && corrected && why.Contains("다시 보니") && why.Contains("감기") && onBed && log.Contains("진료 기록") && m[carer].C.KnBody > kn0 + 0.04f,
                $"감기로 본 이 {(w != null ? m.NameOf(w.Id) : "없음")} · 숙소에서 잠 {(firstSleptInRoom >= 0 ? $"{firstSleptInRoom / (double)Time.Minutes(1):0}분" : "못 봄")} · 바로잡음 {corrected} '{why}' · 치료 침대 {onBed} · 일지 '{log}' · 몸 지식 {kn0:0.00} → {m[carer].C.KnBody:0.00}");
        }

        // ── (라-1) 숨은 병 둘: 타박 0.6을 맞은 마흔 가운데 열에 넷쯤 속 출혈 · 속 출혈 0.25는 여섯 시간에 피 1.5L를 앗고 (창백함 · 숨참 — 타박상으로 보인다) · 0.03은 저절로 멎는다 /
        //    맹장염 0.8은 다섯 시간 남짓에 터져 배 안이 곪고 (원인: 맹장이 터졌다) · 항생제를 먹으면 아직 터지지 않는다 · 맹장염 0.5는 배가 아프고 열 · 메스꺼움 — 체함으로 보인다 ──
        {
            var lay = SurgShip();
            int At(int x, int y) => Worlds.At(lay, x, y);
            var s = Worlds.Full(lay, 23);
            var life = s.Get<Life>();
            var hit = Enumerable.Range(0, 40).Select(i => life.AddBody(At(12 + i % 10, 1 + i / 10))).ToArray();
            int big = life.AddBody(At(2, 1)), small = life.AddBody(At(4, 1)), rup = life.AddBody(At(6, 1)), held = life.AddBody(At(8, 1)), ap = life.AddBody(At(2, 3));
            s.Run(5);
            int cause = s.Causes.Add(CauseKind.Story, "짐이 무너져 몸을 쳤다", s.Tick);
            foreach (int id in hit) life.Strike(s, id, 0.6f, 0, cause);
            int bleeders = hit.Count(id => life[id].BleedIn > 0);
            life[big].BleedIn = 0.25f; life[small].BleedIn = 0.03f;
            int seed = s.Causes.Add(CauseKind.Story, "맹장", s.Tick);
            foreach (int id in new[] { rup, held, ap }) life.StartAppendicitis(s, id, seed);
            life[rup].Appendix = 0.8f; life[held].Appendix = 0.8f; life[held].Abx = 24; life[ap].Appendix = 0.5f;
            var apBody = life[ap]; Sym apSym = Life.Symptoms(apBody); double apPain = Life.FeltPain(apBody), apFever = Life.FeverK(apBody);
            for (long k = 0; k < Time.Hours(6); k++) { s.Step(); if (k % 25 == 0) for (int b = 0; b < life.Bodies.Count; b++) { if (life[b].Reserve < 1) life.Eat(b, 1.15); if (life[b].Water < Mind.BodyWater0) life.Drink(b, 5); } }
            var bb = life[big];
            Sym bs = Life.Symptoms(bb);
            string why = s.Causes.Text(life[rup].InfectCause);
            Check("수술 (라-1) — 숨은 병 둘: 타박 0.6 마흔에 열에 넷쯤 속 출혈 · 0.25는 여섯 시간에 피 1.5L (창백함 · 숨참 — 타박상으로 보인다) · 0.03은 저절로 멎는다 / 맹장염 0.8은 다섯 시간 남짓에 터져 곪고 · 항생제면 아직 · 0.5는 배 아픔 · 열 · 메스꺼움 — 체함으로 보인다",
                bleeders is >= 9 and <= 24 && bb.BloodL < 3.7f && (bs & (Sym.Pale | Sym.Breathless)) == (Sym.Pale | Sym.Breathless) && Life.Ailment(bb) == Ail.InternalBleed && Life.LookAlike(Ail.InternalBleed) == Ail.Bruise
                && life[small].BleedIn == 0 && life[rup].Appendix == 0 && life[rup].Infect >= 0.8f && why.Contains("맹장이 터져") && life[held].Appendix is > 0.85f and < 1
                && apPain > 0.45 && apFever > 0.4 && (apSym & Sym.Nausea) != 0 && Life.Ailment(apBody) == Ail.Appendicitis && Life.LookAlike(Ail.Appendicitis) == Ail.Indigestion,
                $"속 출혈 {bleeders}/40 · 0.25 → 피 {bb.BloodL:0.00}L ({Life.SymText(bs)}) · 0.03 → {life[small].BleedIn:0.000} / 터짐: 맹장 {life[rup].Appendix:0.00} · 곪음 {life[rup].Infect:0.00} '{why}' · 항생제 {life[held].Appendix:0.00} / 맹장 0.5: 아픔 {apPain:0.00} · 열 {apFever:0.00}K · {Life.SymText(apSym)}");
        }

        // ── (라-1) 수술: 오른쪽 방(치료 침대 · 진단 장치)에서 속 출혈 0.25인 이 — 주컴퓨터가 '몸이 좋지 않다'로 부르고 로봇이 보아 속 출혈 → '수술' 사건 →
        //    다친 이가 치료 침대에 눕고 · 로봇이 마취하고 수술해 피가 멎는다 / 약품고에 진통제가 없으면 마취 없이 연다 (원인 마디 · 아픔 1) ──
        {
            (bool done, bool byRobot, bool anes, float bleedIn, float blood, bool alive, string why, string log, float painMax) Run(bool noOpioid)
            {
                var lay = SurgShip();
                int At(int x, int y) => Worlds.At(lay, x, y);
                var s = Worlds.Full(lay, 24);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var sm = s.Get<ShipMind>();
                sm.Policy.AskPeople = 0;
                int p = m.AddPerson(s, At(14, 2));
                int robot = m.AddRobot(s, At(22, 4), AgentKind.Robot, At(22, 4));
                if (noOpioid) foreach (var fu in lay.Furniture) m.Stock[fu.Id * m.ItemCount + m.iOpioid] = 0;
                s.Run(5);
                int vb = m[p].C.Body;
                life.Strike(s, vb, 0.2f, 0, s.Causes.Add(CauseKind.Story, "선반에 배를 부딪혔다", s.Tick));
                life[vb].BleedIn = 0.25f;
                float painMax = 0;
                for (long k = 0; k < Time.Hours(4); k++)
                {
                    s.Step();
                    foreach (var ag in m.Agents) if (ag.IsPerson) { ag.C.Hunger = 0; ag.C.Thirst = 0; }
                    if (m[robot].C.Goal == GoalKind.Surgery && m[robot].C.OpAnesAt >= m[robot].C.GoalSince) painMax = Math.Max(painMax, life[vb].Pain);
                }
                var op = s.Facts.Since(0).Where(f => f.Kind == FactKind.Surgery).ToList();
                bool anes = s.Facts.Since(0).Any(f => f.Kind == FactKind.Dosed && f.Actor == vb && s.Causes.Text(f.Cause).Contains("마취"));
                string why = op.Count > 0 ? s.Causes.Text(op[0].Cause) : "";
                string noA = s.Causes.Count > 0 ? string.Join(" / ", Enumerable.Range(1, s.Causes.Count - 1).Select(i => s.Causes.Text(i)).Where(t => t.Contains("마취 없이")).Take(1)) : "";
                string log = string.Join(" / ", sm.Log.Select(l => l.text).Where(t => t.Contains("수술") || t.Contains("좋지 않다")).Take(3));
                if (Debug && life[vb].Alive == 0) Console.WriteLine($"    숨짐: '{s.Causes.Text(life[vb].DeathCause)}' · 진통제 {life[vb].Opioid:0}mg");
                if (Debug) Console.WriteLine($"    진통제 없음 {noOpioid}: 수술 {op.Count} · '{why}' · 마취 {anes} · {noA} · 속 출혈 {life[vb].BleedIn:0.000} · 피 {life[vb].BloodL:0.00} · 아픔 가장 {painMax:0.00} · {log} · 기록 {Life.AilName(m[p].C.DxAil)}");
                return (op.Count > 0, op.Count > 0 && op[0].Actor == robot, anes, life[vb].BleedIn, life[vb].BloodL, life[vb].Alive != 0, why + (noA.Length > 0 ? " · " + noA : ""), log, painMax);
            }
            var w = Run(false); var n = Run(true);
            Check("수술 (라-1) — 로봇이 속 출혈을 멈춘다: 주컴퓨터가 '몸이 좋지 않다'로 부르고 · 로봇이 보아 속 출혈 → '수술' 사건 → 치료 침대에서 마취하고 수술해 피가 멎는다 (산 채로) / 진통제가 없으면 마취 없이 연다 (아픔 1)",
                w.done && w.byRobot && w.anes && w.bleedIn == 0 && w.alive && w.blood > 3 && w.log.Contains("수술") && n.done && !n.anes && n.why.Contains("마취 없이") && n.painMax >= 0.99f,
                $"마취: 수술 {w.done} (로봇 {w.byRobot}) · 마취 {w.anes} · 속 출혈 {w.bleedIn:0.000} · 피 {w.blood:0.00}L · '{w.why}' · '{w.log}' / 진통제 없음: 수술 {n.done} · 마취 {n.anes} · 아픔 {n.painMax:0.00} · '{n.why}'");
        }

        // ── (라-2) 재생 젤: 뇌 0.5 · 심장 0.6 · 폐 흉터 0.3이 남은 몸 — 치료 침대에서 젤 72시간이면 낫지 않던 바닥이 반 아래로 (한 달 뒤 뇌 0.1 · 심장 0.15 · 폐 0.15 남짓) ·
        //    젤 없이는 뇌 0.2 · 심장 0.3 · 폐 0.3 그대로 · 젤을 발라도 침대에 눕지 않으면 듣지 않는다 ──
        {
            Body Run(bool gel, bool bed)
            {
                var b = new Body { Alive = 1, Brain = 0.2f, BrainPeak = 0.5f, Heart = 0.3f, HeartPeak = 0.6f, LungScar = 0.3f, LungDamage = 0.3f, Regen = gel ? 72 : 0 };
                for (int h = 0; h < 33 * 24; h++) { Life.RegenKinetics(ref b, 3600, bed && h < 24 * 6); Life.HealOrgans(ref b, 3600); Life.LungHeal(ref b, 3600); }
                return b;
            }
            var g = Run(true, true); var n = Run(false, true); var off = Run(true, false);
            Check("수술 (라-2) — 재생 젤: 뇌 0.5 · 심장 0.6 · 폐 흉터 0.3이 남은 몸 — 치료 침대에서 젤 72시간이면 한 달 뒤 뇌 · 심장 · 폐가 바닥 반 아래로 · 젤 없이는 그대로 · 침대에 눕지 않으면 듣지 않는다",
                g.Brain < 0.12f && g.Heart < 0.17f && g.LungDamage < 0.16f && g.Regen <= 0 && Math.Abs(n.Brain - 0.2f) < 0.01f && Math.Abs(n.Heart - 0.3f) < 0.01f && Math.Abs(n.LungDamage - 0.3f) < 0.01f
                && off.Regen == 72 && Math.Abs(off.Heart - 0.3f) < 0.01f,
                $"젤: 뇌 {g.Brain:0.000} · 심장 {g.Heart:0.000} · 폐 {g.LungDamage:0.000} (젤 남음 {g.Regen:0}) / 없이: {n.Brain:0.00} · {n.Heart:0.00} · {n.LungDamage:0.00} / 침대 밖: 심장 {off.Heart:0.00} · 젤 {off.Regen:0}");
        }

        // ── (라-2) 인공 심장 · 간 이식: 심장 바닥 0.4인 이 — 주컴퓨터가 '인공 심장' 수술을 잡고 로봇이 치료 침대에서 넣는다 /
        //    간이 0.7 상한 이 — 한 시간 전에 숨진 동료의 성한 간을 이식한다 (원인 사슬: 그 죽음) · 일곱 시간 전에 숨졌으면 이식하지 않는다 ──
        {
            (string why, float organ, byte implant, byte donated, string chain, bool planned) Run(bool liver, double diedHoursAgo)
            {
                var lay = SurgShip();
                int At(int x, int y) => Worlds.At(lay, x, y);
                var s = Worlds.Full(lay, 25);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var sm = s.Get<ShipMind>();
                sm.Policy.AskPeople = 0;
                int p = m.AddPerson(s, At(14, 2));
                int robot = m.AddRobot(s, At(22, 4), AgentKind.Robot, At(22, 4));
                int donor = liver ? m.AddPerson(s, At(20, 2)) : -1;
                s.Run(Time.Hours(diedHoursAgo) + 5);
                int vb = m[p].C.Body;
                if (liver)
                {
                    ref var d = ref life[m[donor].C.Body];
                    d.Alive = 0; d.Conscious = 0; d.DiedAt = s.Tick - Time.Hours(diedHoursAgo);
                    d.DeathCause = s.Causes.Add(CauseKind.Biology, "죽었다 — 감압 사고", d.DiedAt, d.Cell);
                    life[vb].Liver = 0.7f; life[vb].LiverPeak = 0.7f;
                }
                else { life[vb].Heart = 0.4f; life[vb].HeartPeak = 0.8f; }
                bool planned = m.PlannedOp(s.Tick, m[p], life).op != Op.None;
                for (long k = 0; k < Time.Hours(3); k++) { s.Step(); foreach (var ag in m.Agents) if (ag.IsPerson && life[ag.C.Body].Alive != 0) { ag.C.Hunger = 0; ag.C.Thirst = 0; } }
                var op = s.Facts.Since(0).FirstOrDefault(f => f.Kind == FactKind.Surgery);
                string why = op.Kind == FactKind.Surgery ? s.Causes.Text(op.Cause) : "";
                string chain = op.Kind == FactKind.Surgery ? s.Causes.Text(s.Causes[op.Cause].Parent) : "";
                if (Debug) Console.WriteLine($"    {(liver ? $"간 이식 ({diedHoursAgo}시간 전)" : "인공 심장")}: 잡음 {planned} · '{why}' ← '{chain}' · 심장 {life[vb].Heart:0.00} · 간 {life[vb].Liver:0.00} · 붙인 것 {life[vb].Implant} · {string.Join(" / ", sm.Log.Select(l => l.text).Where(t => t.Contains("수술")).Take(2))}");
                return (why, liver ? life[vb].Liver : life[vb].Heart, life[vb].Implant, liver ? life[m[donor].C.Body].Implant : (byte)0, chain, planned);
            }
            var h = Run(false, 0); var l = Run(true, 1); var late = Run(true, 7);
            bool hOk = h.why.Contains("인공 심장") && (h.why.Contains("잘됐다") ? h.organ == 0 && (h.implant & 1) != 0 : h.organ > 0.3f);
            bool lOk = l.why.Contains("간을") && l.why.Contains("이식") && l.chain.Contains("감압") && (l.donated & 64) != 0 && (l.why.Contains("잘됐다") ? l.organ < 0.05f && (l.implant & 8) != 0 : true);
            Check("수술 (라-2) — 인공 심장 · 간 이식: 심장 바닥 0.4 — 주컴퓨터가 '인공 심장'을 잡고 로봇이 치료 침대에서 넣는다 / 간 0.7 — 한 시간 전에 숨진 동료의 간을 이식 (원인 사슬: 그 죽음) · 일곱 시간 전이면 잡지 않는다",
                hOk && lOk && !late.planned && late.why == "",
                $"인공 심장: '{h.why}' · 심장 {h.organ:0.00} · 붙인 것 {h.implant} / 간 이식: '{l.why}' ← '{l.chain}' · 간 {l.organ:0.00} · 내준 것 {l.donated} / 일곱 시간 전: 잡음 {late.planned}");
        }
    }
}
