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
    }
}
