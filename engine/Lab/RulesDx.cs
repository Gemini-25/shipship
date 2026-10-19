using ShipEngine;

namespace Lab;

/// <summary>⑦ (다) 진단 — 증상으로 짐작하고 · 틀릴 수 있고 · 다시 보아 바로잡는다. docs/ENGINE.md '장기 · 약 · 수술' (다)</summary>
public static partial class Program
{
    /// <summary>진단 시험 배: 마음 시험 배 + 주컴퓨터 + 오른쪽 방에 치료 침대 하나 · 왼쪽 방(숙소)에 침대 셋.</summary>
    private static Layout DxShip() => Worlds.Ascii(
        Worlds.MindShipComputer.Replace("#.........#....a....a.r.#", "#.........#....a.h..a.r.#").Replace("#..e....q.#", "#..e.e.eq.#"),
        new Dictionary<char, string>(Worlds.MindLegend) { ['h'] = "MedBed" });

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
    }
}
