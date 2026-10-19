using ShipEngine;

namespace Lab;

/// <summary>⑧ 기억 · 정신 건강 — 겪은 일이 충격 · 외상으로 남고 (악몽 · 되살아남) · 성격이 바뀌고 · 몇 주의 짐이 우울 · 불안이 된다. docs/ENGINE.md '기억 · 정신 건강'</summary>
public static partial class Program
{
    private static void MentalRules()
    {
        if (!Do("마음 건강")) return;
        Console.WriteLine("\n[⑧ 기억 · 정신 건강]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";

        // ── 외상 — 같은 일도 사람마다: 10분 동안 겁에 질린 일 (충격 0.5 남짓) — 신경성 0.85 · 곁사람 없는 이는 외상 0.25 넘게 남고 ·
        //    신경성 0.3 · 곁사람 있는 이는 0.08 아래 · 한 달 뒤 충격은 사라지고 (사흘 반감) · 외상은 곁사람 없는 이도 아직 0.15 넘게 남는다 (두 달 반감) ──
        {
            var a = new AgentCore { Neuro = 0.85f }; var b = new AgentCore { Neuro = 0.3f };
            for (int i = 0; i < 10; i++) { Mind.AddShock(ref a, 0.08, false); Mind.AddShock(ref b, 0.08, true); }
            float sa = a.Shock, ta = a.Trauma, tb = b.Trauma;
            var calm = new HeartIn { Weary = 0.2, Lonely = 0.2, SleepLoss = 0.1, Joy = 0.2 };
            for (int d = 0; d < 30 * 24; d++) { Mind.HeartKinetics(ref a, calm, 1); Mind.HeartKinetics(ref b, calm with { Support = true }, 1); }
            Check("외상 — 같은 일도 사람마다: 10분 겁에 질린 일 — 신경성 0.85 · 곁사람 없는 이는 외상이 0.25 넘게 · 신경성 0.3 · 곁사람 있는 이는 0.08 아래 / 한 달 뒤 충격은 사라지고 외상은 곁사람 없는 이에게 0.15 넘게 남는다",
                sa > 0.45 && ta > 0.25 && tb < 0.08 && a.Shock < 0.01 && a.Trauma > 0.15 && b.Trauma < tb,
                $"충격 {sa:0.00} · 외상 {ta:0.00} / {tb:0.00} → 한 달 뒤 충격 {a.Shock:0.000} · 외상 {a.Trauma:0.00} / {b.Trauma:0.000}");
        }

        // ── 우울 · 불안 — 몇 주: 지치고(고립의 피로 0.8) 외롭고 잠이 얕고 외상이 남은 이는 세 주에 우울 0.3을 넘는다 · 기쁨이 있고 곁사람이 있는 같은 이는 덜하다 ·
        //    보통의 날들(피로 0.3 · 외로움 0.2)은 두 달에도 0 · 긴장 0.5와 외상 0.5는 두 주에 불안 0.3을 넘는다 · 깊던 우울은 짐이 걷히면 한 달에 풀린다 ──
        {
            var heavy = new HeartIn { Weary = 0.8, Lonely = 0.6, SleepLoss = 0.4, Sad = 0.2, Stress = 0.3, Joy = 0.05 };
            var normal = new HeartIn { Weary = 0.3, Lonely = 0.2, SleepLoss = 0.15, Stress = 0.05, Joy = 0.2 };
            var h = new AgentCore { Neuro = 0.7f, Trauma = 0.3f }; var hj = new AgentCore { Neuro = 0.7f, Trauma = 0.3f }; var n = new AgentCore { Neuro = 0.7f };
            var x = new AgentCore { Neuro = 0.6f, Trauma = 0.5f }; var r = new AgentCore { Neuro = 0.5f, Depress = 0.5f };
            for (int i = 0; i < 21 * 24; i++) { Mind.HeartKinetics(ref h, heavy, 1); Mind.HeartKinetics(ref hj, heavy with { Joy = 0.5, Support = true }, 1); }
            for (int i = 0; i < 60 * 24; i++) Mind.HeartKinetics(ref n, normal, 1);
            for (int i = 0; i < 14 * 24; i++) { x.Trauma = 0.5f; Mind.HeartKinetics(ref x, normal with { Stress = 0.5 }, 1); }
            for (int i = 0; i < 30 * 24; i++) Mind.HeartKinetics(ref r, normal, 1);
            Check("우울 · 불안 — 몇 주: 지치고 외롭고 잠이 얕고 외상이 남은 이는 세 주에 우울 0.3 넘게 · 기쁨 · 곁사람이 있으면 덜하다 · 보통의 날들은 두 달에도 0 · 긴장 0.5 + 외상 0.5는 두 주에 불안 0.3 넘게 · 깊던 우울(0.5)은 짐이 걷히면 한 달에 0.3 아래",
                h.Depress > 0.3 && hj.Depress < h.Depress - 0.1 && n.Depress < 0.01 && n.Anxiety < 0.01 && x.Anxiety > 0.3 && r.Depress < 0.3,
                $"짐 진 이 우울 {h.Depress:0.00} (기쁨 · 곁사람 {hj.Depress:0.00}) · 보통 {n.Depress:0.000} / {n.Anxiety:0.000} · 불안 {x.Anxiety:0.00} · 풀림 0.5 → {r.Depress:0.00}");
        }

        // ── 악몽: 불에 갇혔던 일(외상 0.9)이 남은 둘이 밤에 자면 — 악몽에 깨고 ('악몽에 깼다' ← 그 일) · 잠이 얕아지고 · 다시 잠든다 ·
        //    같은 방에서 자던 이가 그 소리에 깨도 탓하지 않는다 · 외상이 없는 이는 악몽을 꾸지 않는다 ──
        {
            var lay = DxShip();
            int At(int x, int y) => Worlds.At(lay, x, y);
            var s = Worlds.Full(lay, 5);
            var m = s.Get<Mind>(); var life = s.Get<Life>();
            var hurt = new[] { m.AddPerson(s, At(3, 4), bed: At(3, 5)), m.AddPerson(s, At(5, 4), bed: At(5, 5)) };
            int q = m.AddPerson(s, At(7, 4), bed: At(7, 5));
            int fire = s.Causes.Add(CauseKind.Fire, "식당에 불이 났다 — 갇혔다", s.Tick, At(15, 2));
            foreach (int p in hurt) { ref var c = ref m[p].C; c.Trauma = 0.9f; c.ShockKind = ShockKind.Fire; c.ShockCause = fire; c.ShockRoom = lay.Room[At(15, 2)]; }
            foreach (var ag in m.Agents) if (ag.IsPerson) ag.C.Sleepy = 1;
            long f0 = s.Facts.Total;
            var sleptAgain = new HashSet<int>();
            float loss0 = 0;
            for (long k = 0; k < Time.Hours(9); k++)
            {
                s.Step();
                foreach (var ag in m.Agents) if (ag.IsPerson) { ag.C.Hunger = 0; ag.C.Thirst = 0; }
                foreach (int p in hurt)
                {
                    if (m[p].C.NightmareAt > 0 && life[m[p].C.Body].Asleep != 0 && s.Tick - m[p].C.NightmareAt > 10) sleptAgain.Add(p);
                    loss0 = Math.Max(loss0, m[p].C.SleepLoss);
                }
                if (Debug && k % Time.Hours(1) == 0) Console.WriteLine($"    {Time.Clock(s.Tick)} " + string.Join(" · ", hurt.Select(p => $"잠 {life[m[p].C.Body].Asleep} 얕은 잠 {m[p].C.SleepLoss:0.00} 악몽 때 {m[p].C.NightmareAt}")));
            }
            var nights = s.Facts.Since(f0).Where(f => f.Kind == FactKind.Heart && f.Size == (float)HeartEvent.Nightmare).ToList();
            bool chained = nights.Count > 0 && nights.All(f => hurt.Contains(f.Actor) && s.Causes[f.Cause].Parent == fire);
            int blamed = s.Facts.Since(f0).Count(f => f.Kind == FactKind.Blamed);
            Check("악몽: 불에 갇혔던 일(외상 0.9)이 남은 둘은 밤에 악몽에 깨고 ('악몽에 깼다' ← 그 일) · 잠이 얕아지고 · 다시 잠든다 · 같은 방 사람은 탓하지 않는다 · 외상이 없는 이는 악몽을 꾸지 않는다",
                nights.Count >= 2 && chained && sleptAgain.Count >= 1 && sleptAgain.IsSubsetOf(nights.Select(f => f.Actor)) && loss0 >= 0.3f && blamed == 0 && m.Nightmares == nights.Count,
                $"악몽 {nights.Count} ({string.Join(" · ", nights.Select(f => $"{m.NameOf(f.Actor)} {Time.Clock(f.Tick)}"))}) '{(nights.Count > 0 ? s.Causes.Text(nights[0].Cause) : "")}' ← '{(nights.Count > 0 ? s.Causes.Text(s.Causes[nights[0].Cause].Parent) : "")}' · 다시 잠듦 {sleptAgain.Count} · 얕은 잠 {loss0:0.00} · 탓 {blamed}");
        }

        // ── 되살아남: 불에 갇혔던 일(외상 0.6)이 남은 이는 다른 방의 불을 알게 되면 (보고 · 듣고 · 방송) 겁이 0.7 넘게 솟는다 ('그날이 되살아났다' ← 그 일) ·
        //    외상이 없는 이는 그만큼 겁먹지 않는다 ──
        {
            var lay = DxShip();
            int At(int x, int y) => Worlds.At(lay, x, y);
            var s = Worlds.Full(lay, 3);
            var m = s.Get<Mind>(); var chem = s.Get<Chemistry>();
            int p = m.AddPerson(s, At(3, 2)), q = m.AddPerson(s, At(5, 2));
            m.SetPersonality(q, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f); m.SetPersonality(p, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f);
            int old = s.Causes.Add(CauseKind.Fire, "지난달 식당에 불이 났다 — 갇혔다", s.Tick, At(15, 2));
            { ref var c = ref m[p].C; c.Trauma = 0.6f; c.ShockKind = ShockKind.Fire; c.ShockCause = old; }
            s.Run(5);
            chem.Ignite(At(15, 1), 3e6, 0);
            long f0 = s.Facts.Total;
            float fearP = 0, fearQ = 0;
            for (long k = 0; k < Time.Minutes(6); k++)
            {
                s.Step();
                fearP = Math.Max(fearP, m[p].C.Fear); fearQ = Math.Max(fearQ, m[q].C.Fear);
            }
            var fb = s.Facts.Since(f0).FirstOrDefault(f => f.Kind == FactKind.Heart && f.Size == (float)HeartEvent.Flashback);
            if (Debug) Console.WriteLine("    믿음: " + string.Join(" · ", m[p].Beliefs.Select(b => $"{b.Kind} {b.Conf:0.00} {b.Src}")));
            Check("되살아남: 불에 갇혔던 일(외상 0.6)이 남은 이는 다른 방의 불을 알게 되면 겁이 0.7 넘게 솟는다 ('그날이 되살아났다' ← 그 일) · 외상이 없는 이는 그만큼 겁먹지 않는다",
                fb.Kind == FactKind.Heart && fb.Actor == p && s.Causes[fb.Cause].Parent == old && fearP >= 0.69f && fearQ < fearP && m.Flashbacks >= 1,
                $"되살아남 {(fb.Kind == FactKind.Heart ? Time.Clock(fb.Tick) + " '" + s.Causes.Text(fb.Cause) + "'" : "없음")} · 겁 {fearP:0.00} / 외상 없는 이 {fearQ:0.00}");
        }

        // ── 성격이 바뀐다: 큰일(충격 0.6)이 지나가면 — 그때 맞서지 않은 이는 신경성 +0.05 · 용기 −0.04 ('그 일 뒤로 겁이 많아졌다') ·
        //    불을 끈 이는 용기 +0.04 ('맞서 넘기고 단단해졌다') · 처음 성격에서 0.2 넘게 가지 않는다 ──
        {
            var lay = DxShip();
            int At(int x, int y) => Worlds.At(lay, x, y);
            var s = Worlds.Full(lay, 4);
            var m = s.Get<Mind>();
            int p = m.AddPerson(s, At(3, 2)), q = m.AddPerson(s, At(5, 2)), w = m.AddPerson(s, At(7, 2));
            foreach (int id in new[] { p, q, w }) m.SetPersonality(id, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f);
            int fire = s.Causes.Add(CauseKind.Fire, "식당에 불이 났다", s.Tick, At(15, 2));
            s.Run(Time.Minutes(10));
            float n0 = m[p].C.Neuro, b0 = m[p].C.Brave, bq0 = m[q].C.Brave;
            foreach (int id in new[] { p, q, w })
            {
                ref var c = ref m[id].C;
                c.Shock = 0.29f; c.ShockPeak = 0.6f; c.ShockKind = ShockKind.Fire; c.ShockCause = fire; c.ShockAt = s.Tick - 200;
            }
            m[q].C.CopedAt = s.Tick - 150;   // 그때 불을 껐다
            { ref var c = ref m[w].C; c.Neuro = 0.69f; c.Neuro0 = 0.5f; }   // 이미 많이 바뀌었다
            long f0 = s.Facts.Total;
            s.Run(Time.Minutes(2));
            var notes = s.Facts.Since(f0).Where(f => f.Kind == FactKind.Heart).ToList();
            string Note(int id) => notes.Where(f => f.Actor == id).Select(f => s.Causes.Text(f.Cause)).FirstOrDefault() ?? "";
            Check("성격이 바뀐다: 큰일(충격 0.6)이 지나가면 맞서지 않은 이는 신경성 +0.05 · 용기 −0.04 ('겁이 많아졌다') · 불을 끈 이는 용기 +0.04 ('단단해졌다') · 처음 성격에서 0.2 넘게 가지 않는다",
                Math.Abs(m[p].C.Neuro - n0 - 0.048f) < 0.002f && Math.Abs(m[p].C.Brave - b0 + 0.036f) < 0.002f && Math.Abs(m[p].C.Calm - (1 - m[p].C.Neuro)) < 1e-5
                && Math.Abs(m[q].C.Brave - bq0 - 0.036f) < 0.002f && m[q].C.Neuro == 0.5f && m[w].C.Neuro <= 0.7f + 1e-5
                && Note(p).Contains("겁이 많아졌다") && Note(q).Contains("단단해졌다") && notes.All(f => s.Causes[f.Cause].Parent == fire) && m.Fearful == 2 && m.Hardened == 1,
                $"맞서지 않음: 신경성 {n0:0.000} → {m[p].C.Neuro:0.000} · 용기 {b0:0.000} → {m[p].C.Brave:0.000} '{Note(p)}' / 불을 끔: 용기 {bq0:0.000} → {m[q].C.Brave:0.000} '{Note(q)}' / 이미 0.69: {m[w].C.Neuro:0.000}");
        }

        // ── 곁에서 본 죽음: 같은 방에서 동료가 숨진 것을 본 이는 충격 0.25 넘게 (가까웠으면 더) · 그 죽음이 충격의 원인 · 한 번만 ──
        {
            var lay = DxShip();
            int At(int x, int y) => Worlds.At(lay, x, y);
            var s = Worlds.Full(lay, 6);
            var m = s.Get<Mind>(); var life = s.Get<Life>();
            int p = m.AddPerson(s, At(3, 2)), d = m.AddPerson(s, At(5, 3));
            s.Run(5);
            int why = s.Causes.Add(CauseKind.Setup, "심장이 멎었다", s.Tick, At(5, 3));
            { ref var b = ref life[m[d].C.Body]; b.Alive = 0; b.Conscious = 0; b.DeathCause = why; }
            s.Run(Time.Minutes(3));
            float s1 = m[p].C.Shock;
            s.Run(Time.Minutes(10));
            Check("곁에서 본 죽음: 같은 방에서 동료가 숨진 것을 본 이는 충격 0.25 넘게 · 갈래 '죽음' · 한 번만 (계속 보아도 더 오르지 않는다)",
                s1 >= 0.25f && m[p].C.ShockKind == ShockKind.Death && m[p].C.Shock <= s1 + 1e-4,
                $"충격 {s1:0.00} → {m[p].C.Shock:0.00} · 갈래 {m[p].C.ShockKind} · 원인 '{(m[p].C.ShockCause != 0 ? s.Causes.Text(m[p].C.ShockCause) : "")}'");
        }

        // ── 공황 발작: 불안 1인 여섯이 열여섯 시간 — 깨어 있고 위험이 없을 때 까닭 없이 숨이 가빠 그 자리에 주저앉는다 (2~5분 · 걸음을 멈춘다 · '공황 발작') ·
        //    자는 동안에는 없다 · 불안이 없는 이는 없다 ──
        {
            var lay = DxShip();
            int At(int x, int y) => Worlds.At(lay, x, y);
            var s = Worlds.Full(lay, 8);
            var m = s.Get<Mind>(); var life = s.Get<Life>();
            var anx = new List<int>();
            for (int i = 0; i < 6; i++) anx.Add(m.AddPerson(s, At(12 + i, 7)));
            int calm = m.AddPerson(s, At(4, 8));
            long f0 = s.Facts.Total;
            int moved = 0, asleepAttack = 0, attackTicks = 0;
            var at = new Dictionary<int, int>();
            for (long k = 0; k < Time.Hours(16); k++)
            {
                foreach (int id in anx) m[id].C.Anxiety = 1;
                s.Step();
                foreach (var ag in m.Agents) if (ag.IsPerson) { ag.C.Hunger = 0; ag.C.Thirst = 0; }
                foreach (int id in anx)
                {
                    ref var c = ref m[id].C;
                    if (c.AttackUntil > s.Tick && c.AttackAt < s.Tick - 2)
                    {
                        attackTicks++;
                        if (!at.TryGetValue(id, out int cell)) at[id] = c.Cell; else if (cell != c.Cell) moved++;
                        if (life[c.Body].Asleep != 0) asleepAttack++;
                    }
                    else at.Remove(id);
                }
            }
            var attacks = s.Facts.Since(f0).Where(f => f.Kind == FactKind.Heart && f.Size == (float)HeartEvent.Attack).ToList();
            Check("공황 발작: 불안 1인 여섯이 열여섯 시간 — 깨어 있고 위험이 없을 때 그 자리에 주저앉는다 (걸음을 멈춘다 · '까닭 없이 숨이 가빠') · 자는 동안 · 불안이 없는 이는 없다",
                attacks.Count >= 1 && attacks.All(f => anx.Contains(f.Actor)) && moved == 0 && asleepAttack == 0 && attackTicks > 0
                && s.Causes.Text(attacks[0].Cause).Contains("공황 발작") && m.PanicAttacks == attacks.Count,
                $"발작 {attacks.Count} ({string.Join(" · ", attacks.Take(4).Select(f => $"{m.NameOf(f.Actor)} {Time.Clock(f.Tick)}"))}) · 발작 중 움직임 {moved} · 자며 {asleepAttack} · '{(attacks.Count > 0 ? s.Causes.Text(attacks[0].Cause) : "")}'");
        }

        // ── 우울 — 일손을 놓는다: 우울 0.7인 이는 일하는 시간에 일터 대신 제 침대에 틀어박히고 (일 두 시간 아래) · 같은 맡은 일의 보통 사람은 일한다 ──
        {
            var lay = DxShip();
            int At(int x, int y) => Worlds.At(lay, x, y);
            var s = Worlds.Full(lay, 9);
            var m = s.Get<Mind>();
            int dp = m.AddPerson(s, At(3, 4), role: 0, bed: At(3, 5)), ok = m.AddPerson(s, At(5, 4), role: 0, bed: At(5, 5));
            m[dp].C.ShiftHour = m[ok].C.ShiftHour = (int)Time.HourOfDay(s.Tick) % 24;
            m[dp].C.Station = m[ok].C.Station = lay.Furniture.First(f => f.Type == "Workbench").Id;
            int workD = 0, workO = 0, bedD = 0;
            for (long k = 0; k < Time.Hours(6); k++)
            {
                m[dp].C.Depress = 0.7f;
                s.Step();
                foreach (var ag in m.Agents) if (ag.IsPerson) { ag.C.Hunger = 0; ag.C.Thirst = 0; ag.C.Sleepy = 0; }
                if (m[dp].C.Goal == GoalKind.Work) workD++;
                if (m[ok].C.Goal == GoalKind.Work) workO++;
                if (m[dp].C.Goal == GoalKind.Withdraw) bedD++;
            }
            double H(int t) => t / (double)Time.TicksPerHour;
            Check("우울 — 일손을 놓는다: 우울 0.7인 이는 일하는 시간에 일터 대신 제 침대에 틀어박히고 · 같은 맡은 일의 보통 사람은 일한다 (여섯 시간)",
                H(workD) < 2 && H(bedD) > 2 && H(workO) > 3,
                $"우울한 이 일 {H(workD):0.0}시간 · 침대 {H(bedD):0.0}시간 / 보통 사람 일 {H(workO):0.0}시간");
        }

        // ── (다) 수면제: 10mg이면 그 밤 잠을 돕는 몫 1 · 두 시간 반에 반 · 진통제 20mg만으로는 숨이 얕아지지 않지만 수면제를 함께 먹으면 0.4 넘게 얕아진다 ·
        //    매일 밤 두 주면 내성 1 남짓 (같은 약에 반만 듣는다) · 끊은 밤은 잠이 더 얕다 (되튐) ──
        {
            var b = new Body { Alive = 1, Hypnotic = 10 };
            double aid0 = Life.SleepAid(b);
            Life.DrugKinetics(ref b, Time.Hours(2.5) * Time.SecondsPerTick);
            double aidHalf = Life.SleepAid(b);
            var op = new Body { Alive = 1, Opioid = 20 }; var both = new Body { Alive = 1, Opioid = 20, Hypnotic = 10 };
            double rOp = Life.RespDepression(op), rBoth = Life.RespDepression(both);
            var n = new Body { Alive = 1 };
            for (int d = 0; d < 14; d++) { n.Hypnotic += 10; for (int h = 0; h < 24; h++) Life.DrugKinetics(ref n, 3600); }
            float tol = n.HypnoticTol;
            var dosed = n; dosed.Hypnotic += 10;
            double aidTol = Life.SleepAid(dosed);
            for (int h = 0; h < 24; h++) Life.DrugKinetics(ref n, 3600);
            double rebound = Life.Rebound(n);
            Check("수면제: 10mg이면 잠을 돕는 몫 1 · 두 시간 반에 반 · 진통제 20mg만으로는 숨이 그대로 · 수면제와 함께면 0.4 넘게 얕아진다 · 매일 밤 두 주면 내성 1 남짓 (반만 듣는다) · 끊은 밤은 더 얕다",
                aid0 >= 0.99 && aidHalf is > 0.4 and < 0.55 && rOp == 0 && rBoth > 0.4 && tol is > 0.7f and < 1.4f && aidTol < 0.6 && rebound > 0.2,
                $"돕는 몫 {aid0:0.00} → 두 시간 반 {aidHalf:0.00} · 숨 얕아짐 진통제만 {rOp:0.00} · 함께 {rBoth:0.00} · 두 주 내성 {tol:0.00} → 돕는 몫 {aidTol:0.00} · 끊은 밤 되튐 {rebound:0.00}");
        }

        // ── (다) 친구: 우울 0.6인 이와 그를 아끼는 이(정 0.7)가 함께 쉬면 (여덟 시간 안에) — 아끼는 이가 '요즘 어때' 하고 묻고 털어놓으면 들어 준다 (속마음 · 위로) → 우울이 준다 ──
        {
            var lay = DxShip();
            int At(int x, int y) => Worlds.At(lay, x, y);
            var s = Worlds.Full(lay, 12);
            var m = s.Get<Mind>(); var soc = s.Get<Society>();
            int p = m.AddPerson(s, At(14, 2)), f = m.AddPerson(s, At(16, 2));
            s.Run(2);
            soc.Bond[f * soc.N + p] = 0.7f; soc.Bond[p * soc.N + f] = 0.7f; soc.Known[f * soc.N + p] = soc.Known[p * soc.N + f] = 1;   // 오래 알고 지낸 사이
            float d0 = 0.6f;
            m[p].C.Depress = d0;
            bool heard = false;
            for (long k = 0; k < Time.Hours(8) && !heard; k++)
            {
                s.Step();
                foreach (var ag in m.Agents) if (ag.IsPerson) { ag.C.Hunger = 0; ag.C.Thirst = 0; ag.C.Sleepy = 0; ag.C.Lonely = 0.6f; }
                heard = soc.Sayings.Any(x => x.Topic == Topic.Troubled && x.End == TalkEnd.Comfort && x.Speaker == f && x.Listener == p);
                if (Debug && k % Time.Hours(1) == 0) Console.WriteLine($"    {Time.Clock(s.Tick)} {m[p].C.Goal}@{m.RoomOf(m[p].C.Cell)} / {m[f].C.Goal}@{m.RoomOf(m[f].C.Cell)} 거리 {m.Dist(m[p].C.Cell, m[f].C.Cell)} 말 {string.Join(",", soc.Sayings.Select(x => $"{x.Topic}:{x.End}"))} 정 {soc.BondOf(f, p):0.00} 우울 {m[p].C.Depress:0.00}");
            }
            var say = soc.Sayings.LastOrDefault(x => x.Topic == Topic.Troubled);
            Check("친구: 우울 0.6인 이와 그를 아끼는 이(정 0.7)가 함께 쉬면 (여덟 시간 안에) 아끼는 이가 '요즘 어때' 하고 묻고 털어놓으면 들어 준다 (속마음 · 위로) → 우울이 준다",
                heard && m[p].C.Depress < d0 - 0.04f,
                $"속마음 {(say.Line != 0 ? Time.Clock(say.Tick) + " '" + s.Causes.Text(say.Line) + "' ← '" + s.Causes.Text(s.Causes[say.Line].Parent) + "'" : "없음")} · 우울 {d0:0.00} → {m[p].C.Depress:0.00}");
        }

        // ── (다) 주컴퓨터가 먼저 살핀다 → 사람 상담: 자다가 악몽에 깨는 이 — 주컴퓨터가 그 방 스피커로 말을 걸고 ('요즘 자다가 악몽에 깬다') · 사흘째에도 남으면 '마음 돌봄' ·
        //    로봇이 없으니(로봇은 이야기를 들어 줄 수 없다) 다정한 이에게 부탁해 — 곁에 앉아 30분 들어 준다 → 우울 · 외상이 준다 · 들은 이가 그에게 정이 든다 ──
        {
            var lay = DxShip();
            int At(int x, int y) => Worlds.At(lay, x, y);
            var s = Worlds.Full(lay, 13);
            var m = s.Get<Mind>(); var sm = s.Get<ShipMind>(); var soc = s.Get<Society>();
            int p = m.AddPerson(s, At(14, 2)), kind = m.AddPerson(s, At(4, 8)), cold = m.AddPerson(s, At(18, 8));
            m.SetPersonality(kind, 0.5f, 0.6f, 0.5f, 0.95f, 0.4f); m.SetPersonality(cold, 0.5f, 0.6f, 0.5f, 0.1f, 0.4f);
            int fire = s.Causes.Add(CauseKind.Fire, "식당에 불이 났다 — 갇혔다", s.Tick, At(15, 2));
            s.Run(2);
            { ref var c = ref m[p].C; c.Depress = 0.6f; c.Trauma = 0.5f; c.ShockKind = ShockKind.Fire; c.ShockCause = fire; c.NightmareAt = s.Tick; }
            sm.MoodTalks[p] = 2;   // 이틀 동안 말을 걸었다
            float d0 = m[p].C.Depress, t0 = m[p].C.Trauma, b0 = soc.BondOf(p, kind);
            long f0 = s.Facts.Total;
            int counselor = -1;
            long room = 0;   // 받아 들었을 때 남은 맡김 시간 — 찾아가서 30분 들어 줄 만큼 (관찰 장면: 30분이면 끊겼다)
            for (long k = 0; k < Time.Hours(3) && m.Counsels == 0; k++)
            {
                s.Step();
                foreach (var ag in m.Agents) if (ag.IsPerson) { ag.C.Hunger = 0; ag.C.Thirst = 0; ag.C.Sleepy = 0; }
                if (m[kind].C.AssignedGoal == GoalKind.Counsel && m[kind].C.AssignedUntil > s.Tick && counselor < 0) { counselor = kind; room = m[kind].C.AssignedUntil - s.Tick; }
                if (m[cold].C.AssignedGoal == GoalKind.Counsel && m[cold].C.AssignedUntil > s.Tick) counselor = cold;
                if (Debug && k % 125 == 0) Console.WriteLine($"    {Time.Clock(s.Tick)} 환자 {m[p].C.Goal}@{m[p].C.Cell} · 다정 {m[kind].C.Goal}@{m[kind].C.Cell} 맡음 {m[kind].C.AssignedGoal} 까지 {m[kind].C.AssignedUntil - s.Tick} · {m.Thought(kind)}");
            }
            s.Run(Time.Minutes(40));   // 주컴퓨터가 상담을 받은 것을 읽는다 — 같은 이에게 다시 부탁하지 않는다 (관찰 장면: 사건이 걷히기 전에 한 번 더 부탁했다)
            var check = s.Facts.Since(f0).FirstOrDefault(x => x.Kind == FactKind.Heart && x.Size == (float)HeartEvent.CheckIn);
            var done = s.Facts.Since(f0).FirstOrDefault(x => x.Kind == FactKind.Heart && x.Size == (float)HeartEvent.Counseled);
            Check("주컴퓨터가 먼저 살핀다 → 사람 상담: 악몽에 깨는 이에게 말을 걸고 · 사흘째면 '마음 돌봄'으로 다정한 이에게 부탁해 (로봇은 이야기를 들어 줄 수 없다) · 곁에서 30분 들어 준다 → 우울 · 외상이 준다 · 정이 든다",
                check.Kind == FactKind.Heart && s.Causes.Text(check.Cause).Contains("악몽") && done.Kind == FactKind.Heart && done.Actor == kind && done.Other == p && counselor == kind
                && m[p].C.Depress < d0 - 0.1f && m[p].C.Trauma < t0 - 0.08f && soc.BondOf(p, kind) > b0 + 0.05f && sm.CounselAsks == 1 && !sm.MoodTalks.ContainsKey(p) && room >= Time.Minutes(60) && m.Counsels == 1,
                $"'{(check.Kind == FactKind.Heart ? s.Causes.Text(check.Cause) : "")}' → '{(done.Kind == FactKind.Heart ? Time.Clock(done.Tick) + " " + s.Causes.Text(done.Cause) : "상담 없음")}' · 들어 준 이 {(counselor >= 0 ? m.NameOf(counselor) : "-")} · 우울 {d0:0.00} → {m[p].C.Depress:0.00} · 외상 {t0:0.00} → {m[p].C.Trauma:0.00} · 정 {b0:0.00} → {soc.BondOf(p, kind):0.00} · 맡김 {room / (double)Time.TicksPerHour * 60:0}분 · 상담 {m.Counsels}번");
        }

        // ── (다) 로봇이 수면제를 가져간다: 악몽에 깨고 잠을 설치는 이가 잠이 올 때 — 주컴퓨터가 '잠을 못 잔다'로 로봇을 보내고 · 로봇이 약품고의 수면제 10mg을 준다 (진단은 하지 않는다) →
        //    그 밤 잠을 돕는다 · 원인 사슬 '수면제를 주었다 — 악몽에 깬다' ← 그 일 ──
        {
            var lay = DxShip();
            int At(int x, int y) => Worlds.At(lay, x, y);
            var s = Worlds.Full(lay, 14);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var sm = s.Get<ShipMind>();
            int p = m.AddPerson(s, At(3, 4), bed: At(3, 5));
            int robot = m.AddRobot(s, At(22, 4), dock: At(22, 4));
            int fire = s.Causes.Add(CauseKind.Fire, "식당에 불이 났다 — 갇혔다", s.Tick, At(15, 2));
            s.Run(2);
            { ref var c = ref m[p].C; c.Trauma = 0.5f; c.ShockKind = ShockKind.Fire; c.ShockCause = fire; c.NightmareAt = s.Tick; c.SleepLoss = 0.7f; c.Sleepy = 0.55f; }
            long f0 = s.Facts.Total;
            float aid = 0;
            for (long k = 0; k < Time.Hours(2); k++)
            {
                s.Step();
                foreach (var ag in m.Agents) if (ag.IsPerson) { ag.C.Hunger = 0; ag.C.Thirst = 0; }
                if (life[m[p].C.Body].Asleep != 0) aid = Math.Max(aid, (float)Life.SleepAid(life[m[p].C.Body]));
            }
            var dose = s.Facts.Since(f0).FirstOrDefault(x => x.Kind == FactKind.Dosed && x.Other == (int)Drug.Hypnotic);
            int dx = s.Facts.Since(f0).Count(x => x.Kind == FactKind.Diagnosed);
            Check("로봇이 수면제를 가져간다: 악몽에 깨고 잠을 설치는 이가 잠이 올 때 주컴퓨터가 로봇을 보내 약품고의 수면제 10mg을 준다 (진단은 하지 않는다) → 그 밤 잠을 돕는다 · '수면제를 주었다 — 악몽에 깬다' ← 그 일",
                dose.Kind == FactKind.Dosed && dose.Size == 10 && s.Causes.Text(dose.Cause).Contains("악몽") && s.Causes.Text(dose.Cause).Contains("로봇") && s.Causes[dose.Cause].Parent == fire
                && dx == 0 && aid > 0.5f && m.SleepPills == 1,
                $"'{(dose.Kind == FactKind.Dosed ? Time.Clock(dose.Tick) + " " + s.Causes.Text(dose.Cause) + "' ← '" + s.Causes.Text(s.Causes[dose.Cause].Parent) : "수면제 없음")}' · 진단 {dx} · 자며 돕는 몫 {aid:0.00}");
        }

        // ── 보며 고친 결함 (관찰 장면 '멎었던 심장 뒤의 날들'): 일하는 중인 이에게는 수면제를 주지 않는다 (잘 때가 되어야) ·
        //    심장이 멎었던 일은 '죽을 뻔했던 일'로 남는다 (크게 다친 일이 아니라) ──
        {
            var lay = DxShip();
            int At(int x, int y) => Worlds.At(lay, x, y);
            var s = Worlds.Full(lay, 15);
            var m = s.Get<Mind>(); var life = s.Get<Life>();
            int p = m.AddPerson(s, At(3, 4), bed: At(3, 5)), v = m.AddPerson(s, At(14, 2));
            s.Run(2);
            ref var c = ref m[p].C;
            c.NightmareAt = s.Tick; c.SleepLoss = 0.7f; c.Sleepy = 0.7f;
            c.Goal = GoalKind.Work; bool atWork = m.NeedsSleepAid(s.Tick, m[p]);
            c.Goal = GoalKind.Idle; bool resting = m.NeedsSleepAid(s.Tick, m[p]);
            c.Sleepy = 0.4f; bool early = m.NeedsSleepAid(s.Tick, m[p]);
            life.StopHeart(s, m[v].C.Body, 1, "상한 심장이 갑자기 떨린다", s.Causes.Add(CauseKind.Setup, "상한 심장", s.Tick, At(14, 2)));
            s.Run(Time.Minutes(4));
            Check("보며 고친 결함: 일하는 중이거나 아직 잘 때가 아니면 (졸림 0.5 아래) 수면제를 주지 않는다 · 심장이 멎었던 일은 '죽을 뻔했던 일'로 남는다",
                !atWork && resting && !early && m[v].C.ShockKind == ShockKind.NearDeath && Mind.ShockText(m[v].C.ShockKind) == "죽을 뻔했던 일" && m[v].C.Shock > 0.2f,
                $"일하는 중 {atWork} · 쉬는 중 {resting} · 졸림 0.4 {early} / 멎은 4분: 충격 {m[v].C.Shock:0.00} · '{Mind.ShockText(m[v].C.ShockKind)}'");
        }
    }
}
