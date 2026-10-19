using ShipEngine;

namespace Lab;

/// <summary>② 사고 화학 (docs/ENGINE.md ② '바퀴 3 설계 — 사고 화학'): 재질마다 다른 독가스 · 몸 (ISO 13571 · Purser).</summary>
public static partial class Program
{
    private static void ToxRules()
    {
        if (!Do("사고 화학")) return;
        Console.WriteLine("\n[② 사고 화학 — 독가스]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";

        // ── (가) 달궈진 피복: 불꽃 없이도 염소가 빠져 염화수소가 방에 찬다 (탈염화수소 250℃~ — 타기 전에 매캐하다) ──
        {
            var lay = Worlds.Ascii(Worlds.Lounge, Worlds.Seats);
            int hot = Worlds.At(lay, 8, 2), far = Worlds.At(lay, 2, 4);
            var s = Worlds.WithChem(lay, 1, (f, c) => f.AddStore(hot, f.T.sPvc, 1.0));   // 전선 열네 가닥 몫
            var f = s.Get<Field>(); var ch = s.Get<Chemistry>();
            for (int i = 0; i < Time.Minutes(10); i++) { f.Tf[hot] = 600; s.Step(); }   // 과부하로 달아오른 전선 (327℃)
            Check("달궈진 피복: 327℃에서 10분이면 불꽃 없이도 피복의 염소 대부분이 염화수소로 빠져 방 반대편까지 매캐하다 (냄새 문턱 1ppm · 해로운 5ppm을 훨씬 넘게)",
                ch.BurningCount == 0 && f.Stored(f.T.sPvc, hot) < 0.4 && f.Ppm(f.T.gHCl, far) > 20,
                $"불 {ch.BurningCount} · 남은 피복 {f.Stored(f.T.sPvc, hot):0.00}몰 · 먼 구석 염화수소 {f.Ppm(f.T.gHCl, far):0}ppm");
        }

        // ── (가) 폼이 산소 모자라게 타면 시안화수소 ──
        {
            var lay = Worlds.Ascii(Worlds.Lounge, Worlds.Seats);
            int bed = Worlds.At(lay, 8, 3), far = Worlds.At(lay, 2, 4);
            var s = Worlds.WithChem(lay, 1, (f, c) => { f.AddStore(bed, f.T.sPu, 45); c.Ignite(bed, 3e6, 0); });   // 침대 매트리스 폼 10kg
            var f = s.Get<Field>();
            double hcn = 0, o2min = 100;
            for (int i = 0; i < Time.Minutes(40); i++) { s.Step(); hcn = Math.Max(hcn, f.Ppm(f.T.gHCN, far)); o2min = Math.Min(o2min, f.Partial(f.T.gO2, far)); }
            Check("폼(폴리우레탄)이 닫힌 방에서 산소가 모자라게 타면 시안화수소가 방에 찬다 (먼 구석 100ppm 넘게 — 20분이면 쓰러지는 짙기)",
                hcn > 100, $"먼 구석 시안화수소 최고 {hcn:0}ppm · 산소 최저 {o2min:0.0}kPa");
        }

        // ── (가) 시안화수소 속 몸 (Purser): 200ppm이면 쉬는 사람도 몇 분 만에 쓰러지고 · 400ppm이면 숨진다 · 20ppm은 한 시간 버틴다 ──
        {
            var lay = Worlds.Ascii(Worlds.Sealed);
            int a = Worlds.At(lay, 4, 3);
            Sim Hcn(double ppm) => Worlds.Full(lay, 1, w => { var f = w.Get<Field>(); foreach (int c in lay.Rooms.First(r => r.Cells.Contains(a)).Cells) f.AddGas(c, f.T.gHCN, f.Moles(c) * ppm * 1e-6, f.Temp(c)); });
            long Down(Sim w, Life life, int b, double hours) { for (int i = 0; i < Time.Hours(hours); i++) { w.Step(); if (life[b].Conscious == 0) return w.Tick; } return -1; }
            var s1 = Hcn(200); var l1 = s1.Get<Life>(); int b1 = l1.AddBody(a); long t0 = s1.Tick; long d1 = Down(s1, l1, b1, 0.5);
            var s2 = Hcn(20); var l2 = s2.Get<Life>(); int b2 = l2.AddBody(a); long d2 = Down(s2, l2, b2, 1);
            var s3 = Hcn(400); var l3 = s3.Get<Life>(); int b3 = l3.AddBody(a);
            for (int i = 0; i < Time.Minutes(15) && l3[b3].Alive != 0; i++) s3.Step();
            string why = l3[b3].Alive == 0 ? s3.Causes.Text(l3[b3].DeathCause) : "";
            double min1 = d1 > 0 ? Time.ToSeconds(d1 - t0) / 60 : -1;
            Check("시안화수소 속 몸 (Purser): 200ppm이면 쉬는 사람도 3~10분에 쓰러지고 · 400ppm이면 숨지며 (원인 '시안화수소') · 20ppm은 한 시간 버틴다",
                min1 > 3 && min1 < 10 && d2 < 0 && l3[b3].Alive == 0 && why.Contains("시안화수소"),
                $"200ppm 쓰러짐 {min1:0.0}분 · 20ppm {(d2 < 0 ? "버팀" : "쓰러짐")} (FED {l2[b2].CN:0.00}) · 400ppm \"{why}\"");
        }

        // ── (가) 염화수소: 짙으면 눈 · 목 · 폐를 찌른다 (폐 손상이 쌓인다) ──
        {
            var lay = Worlds.Ascii(Worlds.Sealed);
            int a = Worlds.At(lay, 4, 3);
            var s = Worlds.Full(lay, 1, w => { var f = w.Get<Field>(); foreach (int c in lay.Rooms.First(r => r.Cells.Contains(a)).Cells) f.AddGas(c, f.T.gHCl, f.Moles(c) * 1500e-6, f.Temp(c)); });
            var life = s.Get<Life>(); int b = life.AddBody(a);
            s.Run(Time.Minutes(30));
            Check("염화수소 1500ppm 속 30분: 폐가 상하고 아프다 (암모니아의 두 배쯤 독하다)", life[b].LungDamage > 0.2 && life[b].Pain > 0.3,
                $"폐 손상 {life[b].LungDamage:0.00} · 아픔 {life[b].Pain:0.00}");
        }

        // ── (다) 산소와 불: 몰분율 · 한계 산소 지수(LOI) · 기압 ──
        {
            var lay = Worlds.Ascii(Worlds.Lounge, Worlds.Seats);
            int bed = Worlds.At(lay, 8, 3);
            // 대기를 바꾼다: 기압 kpa · 산소 몰분율 x (나머지는 질소 — 다른 기체는 그대로)
            void Atmos(Field f, double kpa, double x)
            {
                for (int c = 0; c < lay.N; c++)
                {
                    double n = f.Moles(c); if (n < 1e-9) continue;
                    double want = n * kpa / f.Pressure(c), t = f.Temp(c);
                    double other = n - f.Gas(f.T.gO2, c) - f.Gas(f.T.gN2, c);
                    f.RemoveGas(c, f.T.gO2, f.Gas(f.T.gO2, c)); f.RemoveGas(c, f.T.gN2, f.Gas(f.T.gN2, c));
                    f.AddGas(c, f.T.gO2, want * x, t); f.AddGas(c, f.T.gN2, Math.Max(0, want * (1 - x) - other), t);
                }
            }
            // 고분자 100몰(1.4kg)에 불을 붙여 몇 분 — 탄 몫 · 아직 타나
            // holdK: 불을 붙이지 않고 겉면을 그 온도로 달궈 둔다 (붙나) · keepK: 붙인 불의 겉면이 둘레(뜨거운 방)에서 받아 그 아래로 식지 않는다
            (double burned, bool alive) Fire(double kpa, double x, double minutes, double holdK = 0, double keepK = 0)
            {
                var s = Worlds.WithChem(lay, 1, (f, c) => { Atmos(f, kpa, x); f.AddStore(bed, f.T.sPoly, 100); if (holdK <= 0) c.Ignite(bed, 3e6, 0); });
                var f = s.Get<Field>(); var ch = s.Get<Chemistry>();
                for (int k = 0; k < Time.Minutes(minutes); k++) { if (holdK > 0 && ch.BurningCount == 0) f.Tf[bed] = holdK; if (keepK > 0) f.Tf[bed] = Math.Max(f.Tf[bed], keepK); s.Step(); }
                return (1 - f.Stored(f.T.sPoly, bed) / 100, ch.BurningCount > 0);
            }
            var air = Fire(101.3, 0.21, 3); var thin = Fire(50, 0.21, 3); var vac = Fire(5, 0.21, 1);
            Check("같은 산소 몰분율이면 기압이 낮아도 탄다 (NASA 시험 — 50kPa · 21%도 1기압과 비슷하게) · 5kPa에서는 불꽃이 서지 못한다",
                thin.burned > 0.6 * air.burned && !vac.alive && vac.burned < 0.05 * air.burned,
                $"3분에 탄 몫: 1기압 {air.burned:P0} · 50kPa {thin.burned:P0} / 5kPa: {(vac.alive ? "탄다" : "꺼짐")} · {vac.burned:P1}");
            var rich = Fire(56.5, 0.34, 2); var air2 = Fire(101.3, 0.21, 2);
            var apollo = Fire(34, 1.0, 2, holdK: 570); var airHot = Fire(101.3, 0.21, 2, holdK: 570);
            Check("산소가 짙으면 더 세게 탄다 (탐사 대기 56.5kPa · 34% — 2분에 1.5배 넘게) · 발화 온도가 낮아진다 (아폴로 1호 — 100% 산소 34kPa면 297℃ 겉면에 붙고 · 공기에서는 안 붙는다)",
                rich.burned > 1.5 * air2.burned && apollo.alive && !airHot.alive,
                $"2분에 탄 몫: 34% {rich.burned:P0} · 21% {air2.burned:P0} / 297℃ 겉면: 100% 산소 {(apollo.alive ? "붙음" : "안 붙음")} · 공기 {(airHot.alive ? "붙음" : "안 붙음")}");
            var cold = Fire(101.3, 0.12, 5); var hot = Fire(101.3, 0.12, 5, keepK: 750); var low = Fire(101.3, 0.08, 2, keepK: 750);
            Check("한계 산소 지수 (고분자 17.4%): 산소 12%면 차가운 방의 작은 불은 스스로 꺼지지만 · 둘레가 뜨거워(477℃) 한계가 낮아지면 5분 넘게 타고 · 8%는 뜨거워도 꺼진다 (바닥 — 뜨거운 방의 불꽃 한계)",
                !cold.alive && hot.alive && !low.alive, $"12% 차가운 방: {(cold.alive ? "탄다" : "꺼짐")} ({cold.burned:P0}) · 12% 뜨거운 둘레: {(hot.alive ? "탄다" : "꺼짐")} ({hot.burned:P0}) · 8% 뜨거운 둘레: {(low.alive ? "탄다" : "꺼짐")}");
        }

        // ── (나) 냄새 · 증상 · 센서 ──
        var mlay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
        int MAt(int x, int y) => Worlds.At(mlay, x, y);
        int right = mlay.Room[MAt(12, 2)];
        void Fill(Sim w, int gas, int room, double ppm) { var f = w.Get<Field>(); foreach (int c in w.Layout.Rooms[room].Cells) f.AddGas(c, gas, f.Moles(c) * ppm * 1e-6, f.Temp(c)); }
        BeliefSource? Knows(Mind m, int a, int room)
        {
            BeliefSource? got = null;   // 제 코 · 몸으로 안 것이 있으면 그것 (말로 들은 믿음은 외친 사람 자리에 따로 생긴다)
            foreach (var b in m[a].Beliefs)
                if (b.Kind == BeliefKind.BadAir && b.Conf >= 0.3f && mlay.Room[b.Cell] == room && (got == null || b.Src is BeliefSource.Smelled or BeliefSource.Felt)) got = b.Src;
            return got;
        }

        // 쓴 아몬드 냄새는 맡는 사람만 맡는다 → 맡은 사람이 외쳐 못 맡는 사람도 말로 안다 · 염화수소는 누구나 맡는다
        {
            int[] spots = { MAt(12, 2), MAt(13, 3), MAt(17, 2), MAt(18, 3), MAt(12, 4), MAt(17, 4) };
            ulong seed = 1;
            while (spots.Select((_, i) => Mind.SmellsAlmond(seed, i)).Distinct().Count() < 2) seed++;   // 맡는 사람 · 못 맡는 사람이 다 있는 세계
            (int smelled, int told, int wrong, int n, string why) Run(int gas, double ppm)
            {
                var s = Worlds.Full(mlay, seed); var m = s.Get<Mind>();
                var ids = spots.Select(c => m.AddPerson(s, c)).ToArray();
                Fill(s, gas, right, ppm);   // 다 같은 방에 있을 때 (몇 틱만 지나도 밥 먹으러 나간다)
                s.Run(Time.Minutes(40 / 60.0));
                int sm = 0, tl = 0, wr = 0, n = 0;
                foreach (int a in ids)
                {
                    bool can = gas == s.Get<Field>().T.gHCl || Mind.SmellsAlmond(s.Seed, a);
                    var src = Knows(m, a, right);
                    if (can) { n++; if (src == BeliefSource.Smelled) sm++; else wr++; }
                    else if (src == BeliefSource.Smelled) wr++;
                    else if (src is BeliefSource.Told) tl++;
                }
                var note = s.Causes.Text(m[ids.First(a => gas == s.Get<Field>().T.gHCl || Mind.SmellsAlmond(s.Seed, a))].Beliefs.FirstOrDefault(b => b.Kind == BeliefKind.BadAir).Cause);
                return (sm, tl, wr, n, note);
            }
            var probe = Worlds.Full(mlay, seed).Get<Field>().T;
            var cn = Run(probe.gHCN, 5);
            var cl = Run(probe.gHCl, 3);
            int deaf = spots.Length - cn.n;
            Check("시안화수소 5ppm: 쓴 아몬드 냄새는 맡는 사람만 맡는다 (유전 — 사람마다 다르다) → 그 사람이 외쳐 못 맡는 사람도 말로 안다 · 염화수소 3ppm은 누구나 맡는다",
                cn.wrong == 0 && cn.smelled == cn.n && cn.told == deaf && cl.wrong == 0 && cl.smelled == spots.Length && cn.why.Contains("아몬드") && cl.why.Contains("매캐한"),
                $"시안화수소: 맡음 {cn.smelled}/{cn.n} · 못 맡는 {deaf}명 중 말로 앎 {cn.told} · 어긋남 {cn.wrong} (\"{cn.why}\") / 염화수소: 맡음 {cl.smelled}/{spots.Length} (\"{cl.why}\")");
        }

        // 못 맡는 사람은 늦게 안다 — 몸에 쌓여 어지러울 때 (100ppm: 맡는 사람은 바로 나가고 · 못 맡는 사람은 몇 분 뒤 · 쓰러지기 전에)
        {
            (double leftMin, bool felt, bool up) Alone(bool smeller)
            {
                ulong seed = 1;
                while (Mind.SmellsAlmond(seed, 0) != smeller) seed++;
                var s = Worlds.Full(mlay, seed); var m = s.Get<Mind>(); var life = s.Get<Life>();
                int a = m.AddPerson(s, MAt(16, 3));
                s.Run(5);
                Fill(s, s.Get<Field>().T.gHCN, right, 100);
                long t0 = s.Tick, left = -1;
                for (int k = 0; k < Time.Minutes(15) && left < 0; k++) { s.Step(); if (mlay.Room[m[a].C.Cell] != right) left = s.Tick; }
                return (left > 0 ? Time.ToSeconds(left - t0) / 60 : -1, m[a].Beliefs.Any(b => b.Kind == BeliefKind.BadAir && b.Src == BeliefSource.Felt), life[m[a].C.Body].Conscious != 0);
            }
            var yes = Alone(true); var no = Alone(false);
            Check("시안화수소 100ppm에 혼자: 냄새를 맡는 사람은 1분 안에 나가고 · 못 맡는 사람은 어지러워진 뒤에야 (몇 분 늦게) 나간다 — 둘 다 쓰러지기 전에",
                yes.leftMin > 0 && yes.leftMin < 1 && no.felt && no.leftMin > yes.leftMin + 1 && no.leftMin < 12 && yes.up && no.up,
                $"맡는 사람 {yes.leftMin:0.0}분 · 못 맡는 사람 {no.leftMin:0.0}분 (어지러움 {no.felt}) · 깨어 있음 {yes.up}/{no.up}");
        }

        // 자던 사람: 염화수소 30ppm은 코와 목을 찔러 깨우지만 · 시안화수소 30ppm은 깨우지 못한다 (불난 밤에 잠든 채 숨지는 까닭)
        {
            bool Woke(Func<Field, int> gas)
            {
                var s = Worlds.Full(mlay, 1); var m = s.Get<Mind>(); var life = s.Get<Life>();
                int bed = MAt(3, 5);
                int p = m.AddPerson(s, bed, -1, bed);
                life[m[p].C.Body].Asleep = 1; m[p].C.Sleepy = 1;
                m[p].Plan.Add(new PlanStep { Kind = StepKind.Use, Arg = (byte)UseKind.Sleep, Value = Time.Hours(8) });
                m[p].C.Goal = GoalKind.Sleep;
                s.Run(5);
                Fill(s, gas(s.Get<Field>()), mlay.Room[bed], 30);
                for (int k = 0; k < Time.Minutes(5); k++) { s.Step(); if (life[m[p].C.Body].Asleep == 0) return true; }
                return false;
            }
            bool hcl = Woke(f => f.T.gHCl), hcn = Woke(f => f.T.gHCN);
            Check("자던 사람: 염화수소 30ppm에는 깬다 · 시안화수소 30ppm에는 깨지 않는다 (찌르지 않는다)", hcl && !hcn, $"염화수소 {(hcl ? "깸" : "안 깸")} · 시안화수소 {(hcn ? "깸" : "안 깸")}");
        }

        // 주컴퓨터 공기 센서: 시안화수소를 재 기체 이름 · 짙기와 할 일을 방송한다
        {
            var clay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
            int room = clay.Room[Worlds.At(clay, 12, 2)];
            var s = Worlds.Full(clay, 1); var cm = s.Get<ShipMind>();
            s.Run(Time.Minutes(10 / 60.0));
            Fill(s, s.Get<Field>().T.gHCN, room, 80);
            long at = -1;
            for (int k = 0; k < Time.Minutes(1) && at < 0; k++) { s.Step(); if (cm.Incidents.Any(x => x.Active != 0 && x.Kind == IncidentKind.Toxic && x.Room == room)) at = k; }
            var said = cm.Log.Select(x => x.text).LastOrDefault(t => t.Contains("독한 기체")) ?? "";
            Check("주컴퓨터 공기 센서: 시안화수소 80ppm을 1분 안에 잡아 '시안화수소 · 짙기 · 숨을 참고 나오세요'를 방송한다",
                at >= 0 && said.Contains("시안화수소") && said.Contains("숨을 참고"), $"{(at >= 0 ? $"{Time.ToSeconds(at):0}초" : "못 잡음")} · \"{said}\"");
        }

        // 불보다 먼저: 달아오른 PVC 물건 — 불꽃이 없어도 염화수소를 공기 센서가 잡는다 (ISS CSA-CP의 염화수소는 불의 이른 신호)
        {
            var clay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
            int room = clay.Room[Worlds.At(clay, 17, 3)];
            var s = Worlds.Full(clay, 1); var cm = s.Get<ShipMind>(); var f = s.Get<Field>(); var ch = s.Get<Chemistry>(); var net = s.Get<Network>();
            int hot = clay.Rooms[room].Cells.First(c => net.Strands[c] == 0 && clay.Furniture.All(fu => Array.IndexOf(fu.Cells, c) < 0));   // 선이 지나지 않는 칸 (달군 선은 끊겨 센서가 먼저 죽는다)
            f.AddStore(hot, f.T.sPvc, 1.0);
            long at = -1;
            for (int k = 0; k < Time.Minutes(15) && at < 0; k++) { f.Tf[hot] = 560; s.Step(); if (cm.Incidents.Any(x => x.Active != 0 && x.Kind == IncidentKind.Toxic && x.Room == room)) at = k; }
            var said = cm.Log.Select(x => x.text).LastOrDefault(t => t.Contains("독한 기체")) ?? "";
            if (Debug) foreach (var (tk, tx) in cm.Log.TakeLast(10)) Console.WriteLine($"    주컴 {Time.Clock(tk)} {tx}");
            Check("불보다 먼저: 287℃로 달아오른 PVC는 불꽃 없이도 염화수소를 내고 · 주컴퓨터 공기 센서가 그것으로 먼저 안다 ('염화수소 · 입을 가리고')",
                at >= 0 && ch.BurningCount == 0 && said.Contains("염화수소"), $"{(at >= 0 ? $"{Time.ToSeconds(at) / 60:0.0}분" : "15분 동안 못 잡음")} · 불 {ch.BurningCount} · 남은 피복 {f.Stored(f.T.sPvc, hot):0.00}몰 · 방 염화수소 {s.Layout.Rooms[room].Cells.Max(c => f.Ppm(f.T.gHCl, c)):0.0}ppm (센서 {cm.RoomHCl[room]:0.0}) · \"{said}\"");
        }
    }
}
