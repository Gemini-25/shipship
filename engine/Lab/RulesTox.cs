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
                var around = lay.Room[bed] >= 0 ? lay.Rooms[lay.Room[bed]].Cells : new int[4];
                if (lay.Room[bed] < 0) lay.Neighbors(bed, around);
                for (int k = 0; k < Time.Minutes(minutes); k++)
                {
                    if (holdK > 0 && ch.BurningCount == 0) f.Tf[bed] = holdK;
                    if (keepK > 0)
                    {
                        f.Tf[bed] = Math.Max(f.Tf[bed], keepK);
                        foreach (int j in around) if (j >= 0 && f.Moles(j) > 1 && f.Temp(j) < keepK) f.AddHeat(j, (keepK - f.Temp(j)) * f.GasC(j));   // 뜨거운 방: 불꽃을 먹이는 방 공기도 그 온도
                    }
                    s.Step();
                }
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

        // ── (라) 플루오린화수소: 찌르는 냄새 · 자던 이를 깨운다 · 폐를 다친다 (배터리가 뿜은 PF₅가 물과 만나서) ──
        {
            var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
            int room = lay.Room[Worlds.At(lay, 12, 2)];
            var s = Worlds.Full(lay, 3); var m = s.Get<Mind>(); var f = s.Get<Field>(); var life = s.Get<Life>();
            int awake = m.AddPerson(s, Worlds.At(lay, 12, 2)), sleeper = m.AddPerson(s, Worlds.At(lay, 17, 4));
            life[m[sleeper].C.Body].Asleep = 1; m[sleeper].C.Sleepy = 1;
            foreach (int c in s.Layout.Rooms[room].Cells) f.AddGas(c, f.T.gHF, f.Moles(c) * 60e-6, f.Temp(c));
            long woke = -1;
            for (int k = 0; k < Time.Minutes(3); k++) { s.Step(); if (woke < 0 && life[m[sleeper].C.Body].Asleep == 0) woke = k; }
            var smell = m[awake].Beliefs.Where(b => b.Kind == BeliefKind.BadAir && b.Src == BeliefSource.Smelled).Select(b => s.Causes.Text(b.Cause)).FirstOrDefault() ?? "";
            // 피할 곳이 없으면 (배 전체가 같은 공기) 마신 만큼 폐를 다친다
            foreach (int c in s.Layout.Rooms.SelectMany(r => r.Cells)) f.AddGas(c, f.T.gHF, Math.Max(0, f.Moles(c) * 60e-6 - f.Gas(f.T.gHF, c)), f.Temp(c));
            s.Run(Time.Minutes(5));
            float lung = Math.Min(life[m[awake].C.Body].LungDamage, life[m[sleeper].C.Body].LungDamage);
            Check("플루오린화수소 60ppm: 코와 눈을 찌르는 냄새를 맡고 · 자던 이도 곧 깬다 (그 방을 피한다) · 피할 곳이 없으면 폐를 다친다",
                smell.Contains("찌르는") && woke >= 0 && woke < Time.Minutes(1) && lung > 0,
                $"냄새 \"{smell}\" · 깸 {(woke < 0 ? "못 깼다" : $"{Time.ToSeconds(woke):0}초")} · 배 전체 5분 뒤 폐 손상 {lung:0.0000}");
        }

        // ── (라) 리튬 전지 열폭주: 셀 묶음 여덟 · SEI · 안전 밸브 · 폭주 · 번짐 ──
        {
            var blay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
            (Sim s, Network net, Device bat) Pack(ulong seed = 1)
            {
                var s = Worlds.Full(blay, seed); var net = s.Get<Network>();
                return (s, net, net.Devices.First(d => d.Type == "Battery"));
            }
            float MaxCell(Network net, Device b) => Enumerable.Range(0, Network.CellGroups).Max(k => net.CellK[b.Id * Network.CellGroups + k]);
            int Stage(Network net, Device b, int st) => Enumerable.Range(0, Network.CellGroups).Count(k => net.CellStage[b.Id * Network.CellGroups + k] >= st);
            void Hold(Sim s, Device b, double k) { var f = s.Get<Field>(); foreach (int x in s.Layout.Furniture[b.Furniture].Cells) f.Tf[x] = Math.Max(f.Tf[x], k); }

            // 80℃: SEI가 조금 데우지만 겉으로 빠지는 열이 더 크다 — 한 시간 두어도 스스로 폭주하지 않는다
            {
                var (s, net, b) = Pack();
                for (int k = 0; k < Time.Hours(1); k++) { Hold(s, b, 353); s.Step(); }
                Check("80℃로 한 시간 데워 둔 배터리: SEI가 조금 데우지만 빠지는 열이 더 커 스스로 폭주하지 않는다 (셀은 겉면 언저리)",
                    Stage(net, b, 1) == 0 && MaxCell(net, b) < 365 && net.HasCells(b), $"셀 최고 {MaxCell(net, b) - 273:0}℃ · 뿜음 {Stage(net, b, 1)} · 폭주 {Stage(net, b, 2)}");
            }

            // 불 곁: 겉면이 계속 달궈지면 셀이 오른다 → 150℃ 안전 밸브 (불붙는 기체 · 플루오린화수소) → 200℃ 폭주 → 불
            {
                var (s, net, b) = Pack(); var f = s.Get<Field>(); var ch = s.Get<Chemistry>();
                int room = s.Layout.Room[b.Cell];
                long vent = -1, run = -1; double h2 = 0, co = 0, hf = 0;
                for (int k = 0; k < Time.Minutes(40); k++)
                {
                    if (run < 0) Hold(s, b, 650);
                    s.Step();
                    if (vent < 0 && Stage(net, b, 1) > 0) vent = k;
                    if (run < 0 && Stage(net, b, 2) > 0) run = k;
                    h2 = Math.Max(h2, s.Layout.Rooms[room].Cells.Max(c => f.Ppm(f.T.gH2, c)));
                    co = Math.Max(co, s.Layout.Rooms[room].Cells.Max(c => f.Ppm(f.T.gCO, c)));
                    hf = Math.Max(hf, s.Layout.Rooms[room].Cells.Max(c => f.Ppm(f.T.gHF, c)));
                }
                Check("불 곁에서 달궈진 배터리: 셀이 150℃에 안전 밸브를 열어 수소 · 일산화탄소 · 플루오린화수소를 뿜고 → 뒤이어 200℃에 폭주해 불이 난다",
                    vent >= 0 && run > vent && h2 > 1000 && co > 300 && hf > 5 && ch.Ignitions > 0 && net.IsBroken(b.Id, Part.Electric),
                    $"뿜음 {Time.ToSeconds(vent) / 60:0.0}분 · 폭주 {Time.ToSeconds(run) / 60:0.0}분 · 방 최고 수소 {h2:0} · 일산화탄소 {co:0} · 플루오린화수소 {hf:0.0}ppm · 발화 {ch.Ignitions}");
            }

            // 찌그러짐: 묶음 하나가 속 합선 → 곧 폭주 → 그 열이 이웃 묶음을 달궈 차례로 번진다 (몇 분 간격)
            {
                var (s, net, b) = Pack();
                net.CrushCell(b, s.Causes.Add(CauseKind.Setup, "파편이 배터리를 찌그러뜨렸다", s.Tick));
                var at = new long[Network.CellGroups]; Array.Fill(at, -1);
                for (int k = 0; k < Time.Hours(1); k++)
                {
                    s.Step();
                    for (int g = 0; g < Network.CellGroups; g++) if (at[g] < 0 && net.CellStage[b.Id * Network.CellGroups + g] == 2) at[g] = k;
                }
                int n = at.Count(t => t >= 0);
                double gap = at[1] >= 0 && at[0] >= 0 ? Time.ToSeconds(at[1] - at[0]) / 60 : -1;
                Check("찌그러진 배터리: 묶음 하나가 곧 폭주하고 · 그 열에 이웃 묶음이 몇 분 뒤 따라 폭주한다 (한 시간에 절반 넘게 번진다)",
                    at[0] >= 0 && at[0] < Time.Minutes(1) && gap > 1 && gap < 20 && n >= 4,
                    $"첫 폭주 {Time.ToSeconds(at[0]):0}초 · 이웃 {gap:0.0}분 뒤 · 한 시간에 {n}/{Network.CellGroups}");
            }

            // 다 쓴 배터리: 찌그러져도 뿜기만 하고 불은 나지 않는다 (스스로 데울 에너지가 없다 — 낮은 충전은 덜 위험하다)
            {
                var (s, net, b) = Pack(); var ch = s.Get<Chemistry>();
                net.Charge[b.Id] = 0;
                net.CrushCell(b, 0);
                s.Run(Time.Minutes(20));
                Check("다 쓴 배터리를 찌그러뜨리면: 안전 밸브로 뿜기는 하지만 폭주 불은 나지 않는다 (스스로 데울 저장 에너지가 없다)",
                    Stage(net, b, 1) >= 1 && ch.Ignitions == 0, $"뿜음 {Stage(net, b, 1)} · 발화 {ch.Ignitions}");
            }
        }

        // ── (마) 누출: 산소 조절 센서 노화 (짙은 산소) · 산소 발생기 수소 샘 · 샘에서 폭발 · 피하기까지 원인 사슬 ──
        {
            (Sim s, Network net, ShipMind sm, Field f, int ls) Hw(ulong seed = 1)
            {
                var s = ShipWorld.Create("Hwasal", seed);
                return (s, s.Get<Network>(), s.Get<ShipMind>(), s.Get<Field>(), s.Layout.Rooms.FindIndex(r => r.Name == "생명유지실"));
            }
            void SetO2(Field f, IEnumerable<int> cells, double frac)
            {
                foreach (int c in cells)
                {
                    double n = f.Moles(c), o2 = f.Gas(f.T.gO2, c), want = n * frac;
                    if (want > o2) { f.AddGas(c, f.T.gO2, want - o2, f.Temp(c)); f.RemoveGas(c, f.T.gN2, Math.Min(f.Gas(f.T.gN2, c), want - o2)); }
                }
            }
            var o2gens = (Network net) => net.Devices.Where(d => d.Def.Role == "o2gen").ToList();

            // 늙은 조절 센서는 낮게 읽는다 → 배 산소가 목표를 넘어도 발생기는 쉬지 않는다 (바른 센서면 멈춘다)
            {
                var (s, net, sm, f, ls) = Hw();
                SetO2(f, s.Layout.Rooms.SelectMany(r => r.Cells), 0.225);
                net.O2Gain = 0.75; s.Run(30); float dutyOld = o2gens(net).Max(d => net.Duty[d.Id]);
                net.O2Gain = 1; s.Run(30); float dutyOk = o2gens(net).Max(d => net.Duty[d.Id]);
                Check("산소 조절 센서가 늙어 낮게 읽으면 (0.75) 배 산소가 목표를 넘어도 발생기가 쉬지 않는다 — 바른 센서면 멈춘다",
                    dutyOld > 0.99f && dutyOk < 0.01f, $"배 산소 분압 {net.PpO2:0.0}kPa · 늙은 센서 일 몫 {dutyOld:0.00} · 바른 센서 {dutyOk:0.00}");
            }

            // 주컴퓨터가 제 방 센서와 견줘 짙은 산소(23.5%)를 잡는다 → 알리고 · 조절 센서를 다시 맞춘다 · 주컴퓨터가 없으면 아무도 모른다
            (bool raised, string said, double gain, string why, bool smelled) Rich(bool computer)
            {
                var (s, net, sm, f, ls) = Hw(2);
                var m = s.Get<Mind>();
                if (!computer) { var mc = net.Devices.First(d => d.Type == "MainComputer"); net.On[mc.Id] = 0; }
                s.Run(5);
                net.SetO2Drift(s, 0.75, 0);
                SetO2(f, s.Layout.Rooms[ls].Cells, 0.27);
                s.Run(Time.Minutes(2));
                var x = sm.Incidents.FirstOrDefault(i => i.Kind == IncidentKind.O2Rich);
                string said = sm.Log.Select(l => l.Item2).FirstOrDefault(t => t.Contains("산소가 짙다")) ?? "";
                bool smelled = m.Agents.Any(a => a.IsPerson && a.Beliefs.Any(b => b.Kind == BeliefKind.BadAir && b.Src is BeliefSource.Smelled or BeliefSource.Felt));
                return (x.Id != 0, said, net.O2Gain, x.Id != 0 ? s.Causes.Story(net.O2CalCause) : "", smelled);
            }
            var on = Rich(true); var off = Rich(false);
            Check("짙은 산소 (27%): 주컴퓨터가 제 방 센서로 잡아 '산소가 짙다'를 알리고 · 늙은 조절 센서를 다시 맞춘다 · 주컴퓨터가 꺼져 있으면 아무도 모른다 (사람은 못 느낀다)",
                on.raised && on.said.Contains("%") && on.gain > 0.99 && on.why.Contains("센서") && !off.raised && off.gain < 0.8 && !on.smelled && !off.smelled,
                $"켜짐: \"{on.said}\" · 센서 {on.gain:0.00} · {on.why} / 꺼짐: 사건 {(off.raised ? "있음" : "없음")} · 센서 {off.gain:0.00}");

            // 산소 발생기 봉함이 터지면 갈라 낸 수소가 방으로 샌다 (원인 마디 · 쉬익) → 주컴퓨터 불붙는 기체 경보 (폭발 하한 25%) · 그 방 배기
            {
                var (s, net, sm, f, ls) = Hw(3);
                var gen = o2gens(net)[0];
                s.Run(5);
                net.SetO2Drift(s, 0.5, 0);   // 발생기가 쉬지 않고 돈다 (물을 갈라 수소가 나온다)
                net.AgeLeft(gen.Id, Part.Seal, 0.001, s.Causes.Add(CauseKind.Setup, "시험: 봉함이 낡았다", s.Tick));
                s.Run(Time.Minutes(1));
                string leak = net.SealLeakCause[gen.Id] != 0 ? s.Causes.Text(net.SealLeakCause[gen.Id]) : "";
                double h2a = s.Layout.Rooms[ls].Cells.Sum(c => f.Gas(f.T.gH2, c));
                foreach (int c in s.Layout.Rooms[ls].Cells) f.AddGas(c, f.T.gH2, f.Moles(c) * 0.015, f.Temp(c));   // 몇 시간 쌓인 셈 (닫힌 방)
                s.Run(Time.Minutes(1));
                var x = sm.Incidents.FirstOrDefault(i => i.Kind == IncidentKind.Flammable && i.Room == ls);
                string said = sm.Log.Select(l => l.Item2).FirstOrDefault(t => t.Contains("불붙는 기체")) ?? "";
                Check("산소 발생기 봉함이 터지면 수소가 방으로 샌다 (원인 마디) → 수소 1.5%: 주컴퓨터가 '불붙는 기체'를 알리고 그 방을 배기한다",
                    leak.Contains("수소") && h2a > 0 && x.Id != 0 && said.Contains("수소") && net.Exhaust[ls] != 0,
                    $"샘 \"{leak}\" · 1분에 샌 수소 {h2a:0.00}몰 · 방송 \"{said}\" · 배기 {net.Exhaust[ls]}");
            }

            // 새어 나온 수소에 불꽃이 튀면 터진다 — 폭발의 원인 사슬이 불씨와 봉함 샘 둘 다에 닿는다
            {
                var (s, net, sm, f, ls) = Hw(4);
                var ch = s.Get<Chemistry>();
                var gen = o2gens(net)[0];
                s.Run(5);
                net.SetO2Drift(s, 0.5, 0);
                net.AgeLeft(gen.Id, Part.Seal, 0.001, 0);
                s.Run(Time.Minutes(1));
                foreach (int c in s.Layout.Rooms[ls].Cells) f.AddGas(c, f.T.gH2, f.Moles(c) * 0.09, f.Temp(c));
                long from = s.Facts.Total;
                ch.Ignite(s.Layout.Rooms[ls].Cells[3], 2e4, s.Causes.Add(CauseKind.Setup, "정전기 불꽃", s.Tick));
                s.Run(5);
                var boom = s.Facts.Since(from).FirstOrDefault(x => x.Kind == FactKind.Ignition && x.Other == f.T.gH2);
                string story = boom.Cause != 0 ? s.Causes.Story(boom.Cause) : "";
                string spark = boom.Cause != 0 && s.Causes[boom.Cause].Parent2 != 0 ? s.Causes.Story(s.Causes[boom.Cause].Parent2) : "";
                Check("새어 나온 수소에 불꽃 → 터진다: 원인 사슬의 뿌리는 봉함 샘 · 둘째 부모는 불꽃",
                    boom.Cause != 0 && story.Contains("새어 나온 수소") && story.Contains("봉함") && spark.Contains("불꽃"), $"{story} (불씨: {spark})");
            }

            // 냉매(암모니아)가 새는 방을 피하는 사람의 원인 사슬이 그 샘에 닿는다
            {
                var (s, net, sm, f, ls) = Hw(5);
                var m = s.Get<Mind>();
                var pump = net.Devices.First(d => d.Def.Role == "pump" && d.Room == ls);
                s.Run(5);
                net.AgeLeft(pump.Id, Part.Seal, 0.001, 0);
                for (int k = 0; k < Time.Minutes(2) && (net.SealLeakCause[pump.Id] == 0 || f.Ppm(f.T.gNH3, s.Layout.Index(28, 9)) < 10); k++) s.Step();
                int p = m.AddPerson(s, s.Layout.Index(28, 9));   // 샘이 시작된 방에 들어선 사람 (주컴퓨터 방송보다 제 코가 먼저)
                s.Run(5);
                var b = m[p].Beliefs.FirstOrDefault(x => x.Kind == BeliefKind.BadAir && x.Src is BeliefSource.Smelled or BeliefSource.Felt);   // 제 코로 안 것 (주컴퓨터 방송이 아니라)
                string story = b.Cause != 0 ? s.Causes.Story(b.Cause) : "";
                Check("암모니아가 새는 방을 피하는 사람: 제 코로 안 나쁜 공기 믿음의 원인 사슬이 그 펌프의 봉함 샘에 닿는다 (불 > 샘 > 달궈진 곳)",
                    story.Contains("암모니아가 새어"), story == "" ? $"믿음 없음 ({string.Join(",", m[p].Beliefs.Where(x => x.Kind == BeliefKind.BadAir).Select(x => x.Src))})" : story);
            }
        }

        // ── (바) 소화제: 이산화탄소는 산소를 밀어내 끄고 (식히지 못한다) · 물은 식혀 끈다 · 전기가 흐르는 곳의 물은 감전 ──
        {
            var lay = Worlds.Ascii(Worlds.Lounge, Worlds.Seats);
            int bed = Worlds.At(lay, 8, 3);
            // 고분자 침대 불(붙은 지 1분 · 1m² · 100kW 남짓)에 소화기 하나를 그 칸과 둘레에 다 뿜는다 (이산화탄소 155몰을 15초에 · 물 500몰을 20초에)
            (bool knocked, bool outAfter, bool relit, double tf, double minX) Douse(bool co2)
            {
                var s = Worlds.WithChem(lay, 1, (f, c) => { f.AddStore(bed, f.T.sPoly, 100); c.Ignite(bed, 3e6, 0); });
                var f = s.Get<Field>(); var ch = s.Get<Chemistry>();
                s.Run(Time.Minutes(1));
                var cells = new List<int> { bed }; var nb = new int[4]; lay.Neighbors(bed, nb); foreach (int j in nb) if (j >= 0 && f.Moles(j) > 1) cells.Add(j);
                double left = co2 ? 155 : 500, rate = co2 ? 10 : 25, minX = 1;
                bool knocked = false;
                while (left > 0)
                {
                    double n = Math.Min(left, rate * Time.SecondsPerTick); left -= n;
                    foreach (int c in cells) ch.Douse(c, co2 ? Extinguishant.CO2 : Extinguishant.Water, n / cells.Count);
                    s.Step(); minX = Math.Min(minX, ch.FeedAir(bed).x);
                    if (ch.Burning[bed] == 0) knocked = true;
                }
                double tf = f.Tf[bed];
                s.Run(Time.Minutes(0.5));
                bool outAfter = ch.Burning[bed] == 0;
                bool relit = !outAfter;
                for (int k = 0; k < Time.Minutes(8); k++) { s.Step(); if (ch.Burning[bed] != 0) relit = true; }
                return (knocked, outAfter, relit, tf, minX);
            }
            var gas = Douse(true); var wet = Douse(false);
            Check("이산화탄소 소화기: 분사가 불꽃이 들이쉬는 공기에 섞여 뿜는 동안 불꽃을 눌러 끈다 — 식히지 못해 뜨거운 고체(침대)는 기체가 걷히면 다시 붙는다 · 물은 식혀 끄고 다시 붙지 않는다",
                gas.knocked && gas.relit && wet.outAfter && !wet.relit && gas.tf > wet.tf + 150,
                $"이산화탄소: 눌러 끔 {gas.knocked} · 둘레 산소 최저 {gas.minX:P0} · 다 뿜은 때 겉면 {gas.tf - 273:0}℃ · 다시 붙음 {gas.relit} / 물: 꺼짐 {wet.outAfter} · 겉면 {wet.tf - 273:0}℃ · 다시 붙음 {wet.relit}");
        }

        // 고르기: 전기가 흐르는 곳의 불 (차단기가 눌어붙어 아크가 이어진다 — 아크 위험 씨앗) — 로봇과 전기를 아는 사람은 이산화탄소 소화기 · 모르는 사람은 가까운 물 소화기를 뿌려 감전된다
        {
            var mlay2 = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
            (double water, double gas, float burn, string hurt, bool firstGas) Fight(bool robot, float knows)
            {
                var s = Worlds.Full(mlay2, 7); var m = s.Get<Mind>(); var f = s.Get<Field>(); var ch = s.Get<Chemistry>(); var net = s.Get<Network>();
                var dev = net.Devices.Where(d => d.Type == "MealDispenser").OrderByDescending(d => s.Layout.X(d.Cell)).First();   // 배식기 — 물 소화기(옷장)가 가깝고 이산화탄소 소화기(배전반)는 멀다
                int fd = dev.Feeder;
                net.BreakerStuck[fd] = 1; net.MainStuck[net.Feeders[fd].Panel] = 1; net.FaultCell[fd] = dev.Cell;   // 눌어붙은 차단기 · 윗단 보호도 낡았다 — 아크가 그 칸에 이어진다
                int at = Worlds.At(mlay2, 16, 8);   // 아래 복도 (배식기 곁)
                // 소화기 둘을 같은 복도에: 물 소화기는 가까운 작업대(2칸) · 이산화탄소 소화기는 먼 배식기(15칸) — 다른 곳의 것은 치운다
                int bench = s.Layout.Furniture.FindIndex(fu => fu.Type == "Workbench"), far = s.Layout.Furniture.FindIndex(fu => fu.Type == "MealDispenser" && s.Layout.X(fu.Cells[0]) < 5);
                for (int fu = 0; fu < s.Layout.Furniture.Count; fu++) { m.Stock[fu * m.ItemCount + m.iExt] = 0; m.Stock[fu * m.ItemCount + m.iCO2] = 0; }
                m.Stock[bench * m.ItemCount + m.iExt] = 1; m.Stock[far * m.ItemCount + m.iCO2] = 1;
                int who = robot ? m.AddRobot(s, at, AgentKind.Robot, at) : m.AddPerson(s, at);
                if (!robot) m[who].C.KnElectric = knows;
                s.Run(5);
                long from = s.Facts.Total;
                f.AddStore(dev.Cell, f.T.sPoly, 60);
                int fire = s.Causes.Add(CauseKind.Setup, "배식기 회로의 아크", s.Tick);
                ch.Ignite(dev.Cell, 3e6, fire);
                m.Assign(s, who, GoalKind.FightFire, dev.Cell, new Belief { Kind = BeliefKind.Fire, Cell = dev.Cell, Subject = -1, Conf = 1f, Cause = fire }, s.Tick + Time.Minutes(10));   // 불을 끄러 간다
                bool firstGas = false;
                for (int k = 0; k < Time.Minutes(4); k++) { s.Step(); if (m.Co2Sprayed + m.WaterSprayed > 0 && !firstGas && m.WaterSprayed == 0) firstGas = true; }   // 먼저 든 것이 이산화탄소였나
                float burn = robot ? 0 : s.Get<Life>()[m[who].C.Body].Burn;
                string hurt = s.Facts.Since(from).Where(x => x.Kind == FactKind.Hurt).Select(x => s.Causes.Text(x.Cause)).FirstOrDefault() ?? "";
                return (m.WaterSprayed, m.Co2Sprayed, burn, hurt, firstGas);
            }
            var rb = Fight(true, 0); var pro = Fight(false, 0.8f); var naive = Fight(false, 0);
            Check("아크가 이어지는 배식기의 불: 로봇과 전기를 아는 사람은 멀어도 이산화탄소 소화기를 먼저 든다 · 모르는 사람은 가까운 물 소화기를 뿌려 감전된다 (화상 · 원인 마디)",
                rb.gas > 0 && rb.firstGas && pro.gas > 0 && pro.water == 0 && !pro.hurt.Contains("감전") && naive.water > 0 && naive.hurt.Contains("감전") && naive.burn > 0.1f,
                $"로봇: 이산화탄소 {rb.gas:0} · 물 {rb.water:0} / 아는 사람: 이산화탄소 {pro.gas:0} · 물 {pro.water:0} · 다침 \"{pro.hurt}\" / 모르는 사람: 물 {naive.water:0} · 화상 {naive.burn:0.00} · \"{naive.hurt}\"");
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
