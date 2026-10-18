using ShipEngine;

namespace Lab;

/// <summary>④ 구조 바퀴 2 — 금이 자란다 · 어디든 부서진다 · 배가 갈라진다. docs/ENGINE.md ④</summary>
public static partial class Program
{
    /// <summary>두 방이 좁은 통로(차압 문 둘)로 이어진 배 — 통로를 끊으면 두 조각. 왼쪽 방: 주컴퓨터 · 배전반 · 배터리 · 침대 / 오른쪽 방: 침대 · 배터리 · 배전반.</summary>
    internal const string Dumbbell = @"
#########   #########
#..c.p..#   #.p...e.#
#.......#####.......#
#.b.....P...P.....b.#
#.......#####.......#
#....e..#   #.......#
#########   #########";

    /// <summary>작은 방 하나 (바닥 · 천장 판 시험) · 옆 방은 문으로.</summary>
    internal const string Cell2 = @"
###########
#...#.....#
#.u.P.....#
#...#.....#
###########";

    private static void CrackRules()
    {
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        if (Do("금 자람"))
        {
            Console.WriteLine("\n[④ 금 — 자라고 · 새고 · 찢어진다]");
            var lay = Worlds.Ascii(Worlds.TwoRooms);
            int hull = Worlds.At(lay, 4, 0), part = Worlds.At(lay, 10, 1);
            var leftRoom = lay.Rooms.First(r => r.Cells.Contains(Worlds.At(lay, 3, 2)));
            var rightRoom = lay.Rooms.First(r => r.Cells.Contains(Worlds.At(lay, 15, 2)));
            void Scale(Field f, RoomDef room, double k) { foreach (int c in room.Cells) { for (int g = 0; g < f.G; g++) f.N[c * f.G + g] *= k; f.U[c] *= k; } f.MarkDirtyAll(); }

            // ── 압력 순환마다 자란다 (에어락처럼 방을 비웠다 채우기 셋) — 엔진의 금 = 파리스 법칙으로 셈한 것 ──
            {
                var s = FieldStructure(lay, 1, sim => sim.Get<Structure>().SeedCrack(hull, 0.08, 0));
                var st = s.Get<Structure>(); var f = s.Get<Field>();
                var keep = new double[f.N.Length]; var keepU = new double[f.U.Length];
                double a0 = st.Crack[hull];
                s.Run(3);
                double sigma = st.Sigma(hull, 101.3);
                for (int k = 0; k < 3; k++)
                {
                    Array.Copy(f.N, keep, keep.Length); Array.Copy(f.U, keepU, keepU.Length);
                    Scale(f, leftRoom, 0.001); s.Run(3);                  // 비웠다
                    Array.Copy(keep, f.N, keep.Length); Array.Copy(keepU, f.U, keepU.Length); f.MarkDirtyAll(); s.Run(3); // 다시 채웠다
                }
                var m = Tables.Default.Materials[lay.Material[hull]].Def;
                double a = a0;
                for (int k = 0; k < 6; k++) { double mb = st.Bulge(hull, a); a += 0.5 * m.ParisC * Math.Pow(mb * sigma * Math.Sqrt(Math.PI * a), m.ParisM); }
                Check("외판의 금은 압력 순환마다 자란다 — 비웠다 채우기 셋(반 주기 여섯)이 파리스 법칙으로 셈한 만큼 (5% 안 — 금은 float) · 한 기압을 늘 받치는 동안은 그대로",
                    st.Crack[hull] > a0 && Rel(st.Crack[hull] - a0, a - a0) < 0.05,
                    $"σ {sigma:0}MPa · {a0 * 1000:0.000} → {st.Crack[hull] * 1000:0.0000}mm (+{(st.Crack[hull] - a0) * 1e6:0.000}µm · 셈 +{(a - a0) * 1e6:0.000}µm — 한 주기 0.26µm)");
            }

            // ── 새어 나옴이 먼저: 판을 뚫지 않은 금(4mm)이 순환으로 자라 판(10mm)을 뚫으면 가늘게 샌다 (쉬익 · 금이 갔다) — 찢어지지는 않는다 · 막으면 멈춘다 ──
            {
                var s = Worlds.Full(lay, 1, sim => sim.Get<Structure>().SeedCrack(hull, 0.004, sim.Causes.Add(CauseKind.Impact, "작은 파편이 외판에 흠을 냈다", 0, hull)));
                var st = s.Get<Structure>(); var f = s.Get<Field>();
                s.Run(2);
                bool before = f.Hole[hull] > 0;
                int cycles = 0;
                while (f.Hole[hull] <= 0 && cycles < 3_000_000) { st.LoadCycle(s, hull, st.Sigma(hull, 101.3), 10_000); cycles += 10_000; s.Step(); }
                double leakArea = f.Hole[hull];
                long cracks = s.Facts.CountOf(FactKind.WallCrack);
                double v0 = f.VentedTotal;
                s.Run(Time.Minutes(10));
                double p = f.VentedTotal - v0;
                bool torn = f.Hole[hull] >= 2;
                st.Patch(hull, 0);
                s.Run(5);
                bool sealed_ = f.Hole[hull] <= 0;
                st.LoadCycle(s, hull, st.Sigma(hull, 101.3), 1000); s.Run(2);
                Check("새어 나옴이 먼저: 판을 뚫지 않은 4mm 흠이 순환으로 자라 판(10mm)을 뚫으면 가늘게 샌다 (금이 갔다) · 찢어지지 않는다 · 덧판으로 막으면 그 길이까지는 새지 않는다",
                    !before && leakArea > 0 && leakArea < 1e-3 && cracks > 0 && !torn && p > 0.5 && sealed_ && f.Hole[hull] <= 0,
                    $"뚫기까지 {cycles:N0}주기 (에어락 하루 네 번이면 {cycles / 4.0 / 365:N0}년) · 금 {st.Crack[hull] * 1000:0.0}mm · 샌 넓이 {leakArea * 1e4:0.000}cm² · 10분에 샌 공기 {p:0.0}몰 · 찢김 {torn} · 막음 {sealed_}");
            }

            // ── 차가운 강철은 부서지기 쉽다 (연성-취성 전이 — 리버티선): 오른쪽 방이 빠져 한 기압을 받치는 칸막이의 6cm 금 — 293K는 버티고 · 200K는 찢어진다 ·
            //    찢어짐은 벽 줄을 따라 달리다 문틀에서 멈춘다 (그 너머 판은 그대로) ──
            (bool torn, bool doorSide, bool beyond, string story) Cold(double tempK)
            {
                var s = Worlds.Full(lay, 1);
                var st = s.Get<Structure>(); var f = s.Get<Field>();
                int cause = s.Causes.Add(CauseKind.Impact, "파편이 칸막이에 흠을 냈다", 0, part);
                st.SeedCrack(part, 0.06, cause);
                Scale(f, rightRoom, 0);
                for (int y = 0; y <= 5; y++) f.Ts[Worlds.At(lay, 10, y)] = tempK;
                s.Run(3);
                return (f.Hole[part] >= 2, f.Hole[Worlds.At(lay, 10, 2)] >= 2, f.Hole[Worlds.At(lay, 10, 4)] > 0 || f.Hole[Worlds.At(lay, 10, 5)] > 0, s.Causes.Story(st.DamageCause[Worlds.At(lay, 10, 2)]));
            }
            var warm = Cold(293); var cold = Cold(200);
            Check("차가운 강철은 부서지기 쉽다: 한 기압을 받치는 칸막이의 6cm 금 — 293K는 버티고 · 200K는 찢어진다 (파괴 인성 144 → 39MPa√m) · 찢어짐은 벽 줄을 따라 달리다 문틀에서 멈춘다 · 원인 사슬은 흠까지",
                !warm.torn && cold.torn && cold.doorSide && !cold.beyond && cold.story.Contains("흠") && cold.story.Contains("찢어"),
                $"293K 찢김 {warm.torn} / 200K 찢김 {cold.torn} · 아래 칸 {cold.doorSide} · 문 너머 {cold.beyond} · \"{cold.story}\"");

            // ── 응력 부식: 한 기압을 받치는 칸막이의 2cm 금 — 습한 방(상대 습도 90%)에서는 시간에 따라 자라고 · 마른 방은 그대로 ──
            var sealedLay = Worlds.Ascii(Worlds.Sealed);
            int spart = Worlds.At(sealedLay, 10, 1);
            double Scc(double rh)
            {
                var s = FieldStructure(sealedLay, 1, sim => sim.Get<Structure>().SeedCrack(spart, 0.02, 0));
                var st = s.Get<Structure>(); var f = s.Get<Field>();
                var lr = sealedLay.Rooms.First(r => r.Cells.Contains(Worlds.At(sealedLay, 3, 2))); var rr = sealedLay.Rooms.First(r => r.Cells.Contains(Worlds.At(sealedLay, 15, 2)));
                Scale(f, rr, 0);
                foreach (int c in lr.Cells) { double want = rh * Water.SatKPa(293.15) * 1000 * Field.CellVolume / (Field.R * 293.15); double have = f.Gas(f.T.gH2O, c); if (want > have) f.AddGas(c, f.T.gH2O, want - have, 293.15); else f.RemoveGas(c, f.T.gH2O, have - want); }
                s.Run(Time.Hours(2));
                return st.Crack[spart] - 0.02;
            }
            double wet = Scc(0.9), dry = Scc(0.3);
            Check("응력 부식: 한 기압을 받치는 칸막이의 2cm 금 — 습한 방(90%)에서는 두 시간에 자라고 · 마른 방(30%)은 그대로 (강철 1e-10m/s 언저리)",
                wet > 1e-7 && Math.Abs(dry) < 1e-9, $"습함 +{wet * 1e6:0.00}µm · 마름 +{Math.Abs(dry) * 1e6:0.00}µm");
        }

        if (Do("어디든 부서짐"))
        {
            Console.WriteLine("\n[④ 어디든 부서진다 — 바닥 · 천장 판 · 문의 열 끼임 · 설비]");
            // ── 수소 폭연이 작은 방의 바닥 · 천장 판을 뚫는다 — 그 방은 우주로 빠진다 · 그 방 설비가 폭발 압력에 부서진다 · 원인 사슬은 수소까지 ──
            {
                var lay = Worlds.Ascii(Cell2, new() { ['u'] = "CoolantPump" });
                var s = Worlds.Full(lay, 1, sim =>
                {
                    var ff = sim.Get<Field>();
                    foreach (int c in ff.L.Rooms.First(r => r.Cells.Contains(Worlds.At(ff.L, 1, 1))).Cells) ff.AddGas(c, ff.T.gH2, 40, 293);
                });
                s.Run(2);
                int spark = Worlds.At(lay, 1, 1);
                s.Get<Chemistry>().Ignite(spark, 5e4, s.Causes.Add(CauseKind.Electric, "스위치에서 불꽃이 튀었다", s.Tick, spark));
                double peak = 0; var f = s.Get<Field>();
                for (int k = 0; k < 30; k++) { s.Step(); peak = Math.Max(peak, f.Pressure(Worlds.At(lay, 2, 2))); }
                var st = s.Get<Structure>(); var net = s.Get<Network>();
                var room = lay.Rooms.First(r => r.Cells.Contains(spark));
                int holes = room.Cells.Count(c => f.Hole[c] > 0);
                int hc = room.Cells.FirstOrDefault(c => f.Hole[c] > 0, -1);
                string story = hc >= 0 ? s.Causes.Story(st.DamageCause[hc]) : "";
                s.Run(Time.Minutes(1));
                int pump = net.Devices.First(d => d.Type == "CoolantPump").Id;
                Check("수소 폭연이 작은 방의 바닥 · 천장 판을 뚫는다 (바닥 판 정격 300kPa를 넘는 압력) — 그 방은 우주로 빠지고 · 폭발 압력에 냉각 펌프가 부서진다 · 원인 사슬은 불꽃 → 수소까지",
                    holes > 0 && f.Pressure(spark) < 5 && net.IsBroken(pump, Part.Mechanical) && story.Contains("수소") && story.Contains("바닥"),
                    $"최고 {peak:0}kPa · 뚫린 바닥 {holes}칸 · 방 {f.Pressure(spark):0.0}kPa · 펌프 부서짐 {net.IsBroken(pump, Part.Mechanical)} · \"{story}\"");
            }
            // ── 문의 열 끼임: 불에 달군 문틀(강철 650K — 늘어난 몫 4.2mm > 틈 4mm)은 끼여 열라는 요청에도 안 열린다 · 식으면 풀린다 ──
            {
                var lay = Worlds.Ascii(Worlds.TwoRooms);
                var s = Worlds.Full(lay, 1);
                var st = s.Get<Structure>(); var f = s.Get<Field>(); var ch = s.Get<Chemistry>();
                int dc = lay.Doors[0].Cell;
                ch.HeatCause[dc] = s.Causes.Add(CauseKind.Fire, "문 곁 의자에 불이 붙었다", 0, dc);
                f.Ts[dc] = 650;
                s.Run(2);
                st.RequestDoor(0, 1); s.Run(10);
                bool jam = st.DoorJam[0] != 0 && f.DoorOpen[0] < 0.01f;
                string story = s.Causes.Story(st.DamageCause[dc]);
                f.Ts[dc] = 300; s.Run(2);
                st.RequestDoor(0, 1); s.Run(10);
                Check("불에 달군 문틀(650K — 늘어난 몫 4.2mm가 틈 4mm를 넘는다)은 끼여 안 열린다 · 식으면 풀려 열린다 · 원인 사슬은 불까지",
                    jam && story.Contains("불") && story.Contains("끼었다") && st.DoorJam[0] == 0 && f.DoorOpen[0] > 0.5f,
                    $"달궜을 때 끼임 {jam} · \"{story}\" · 식은 뒤 문 {f.DoorOpen[0]:0.0}");
            }
            // ── 파편이 외판을 뚫고 들어와 방의 냉각 펌프를 부순다 (남은 에너지 · 100J/kg) — 망가진 펌프의 원인 사슬이 파편까지 ──
            {
                var lay = Worlds.Ascii(Cell2, new() { ['u'] = "CoolantPump" });
                var s = Worlds.Full(lay, 1);
                var st = s.Get<Structure>(); var net = s.Get<Network>();
                int cause = s.Causes.Add(CauseKind.Story, "파편 하나가 날아왔다", 0);
                st.Impact(s, Worlds.At(lay, 2, 0), 200_000, 0, 1, cause);
                s.Run(30);
                int pump = net.Devices.First(d => d.Type == "CoolantPump").Id;
                var fail = s.Facts.Since(0).Where(x => x.Kind == FactKind.MachineFail).ToList();
                string story = fail.Count > 0 ? s.Causes.Story(fail[0].Cause) : "";
                Check("파편(200kJ)이 외판을 뚫고 들어와 냉각 펌프를 부순다 — 펌프의 부품이 망가지고 · 원인 사슬이 파편까지",
                    net.IsBroken(pump, Part.Mechanical) && story.StartsWith("파편 하나가 날아왔다") && story.Contains("부서졌다"), $"펌프 부서짐 {net.IsBroken(pump, Part.Mechanical)} · \"{story}\"");
            }
        }

        if (Do("배가 갈라짐"))
        {
            Console.WriteLine("\n[④ 배가 갈라진다 — 조각마다 공기 · 전력 · 망이 끊기고 · 닫힌 방의 사람은 버틴다]");
            var lay = Worlds.Ascii(Dumbbell, Worlds.MindLegend);
            var s = Worlds.Full(lay, 2);
            s.Get<Mind>().Crew(s, 2, 0, 0);
            var st = s.Get<Structure>(); var f = s.Get<Field>(); var net = s.Get<Network>(); var light = s.Get<Light>(); var life = s.Get<Life>(); var mind = s.Get<Mind>();
            var sm = s.Find<ShipMind>();
            int left = lay.Room[Worlds.At(lay, 3, 3)], right = lay.Room[Worlds.At(lay, 15, 3)];
            s.Run(Time.Minutes(2));
            float lampBefore = light.Lamp[right];
            int cause = s.Causes.Add(CauseKind.Story, "커다란 운석이 통로를 꿰뚫었다", s.Tick);
            for (int y = 2; y <= 4; y++) st.Destroy(s, Worlds.At(lay, 10, y), cause);
            s.Run(Time.Hours(1));
            int fragRight = st.FragmentOf(Worlds.At(lay, 15, 3));
            var split = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.ShipSplit);
            string story = split.Cause != 0 ? s.Causes.Story(split.Cause) : "";
            var rightPeople = mind.Agents.Where(a => a.IsPerson && lay.Room[a.C.Cell] == right).ToList();
            bool alive = rightPeople.Count > 0 && rightPeople.All(a => life.Bodies[a.C.Body].Alive != 0 && life.Bodies[a.C.Body].Conscious != 0);
            bool seen = sm != null && sm.RoomSeen[right] != 0;
            if (Debug) Console.WriteLine($"    오른쪽 사람 {rightPeople.Count} · 조각 {st.Fragments.Count} · 오른쪽 기압 {f.Pressure(Worlds.At(lay, 15, 3)):0.0}kPa · 왼쪽 {f.Pressure(Worlds.At(lay, 3, 3)):0.0}");
            Check("배가 갈라진다: 통로가 끊기면 오른쪽 방이 조각으로 떨어져 나간다 (사이 칸은 우주) · 원인 사슬은 운석까지",
                st.Fragments.Count == 1 && fragRight == 1 && f.Severed[Worlds.At(lay, 10, 3)] != 0 && story.StartsWith("커다란 운석") && story.Contains("갈라졌다"),
                $"조각 {st.Fragments.Count} · 오른쪽 방 조각 {fragRight} · 질량 {(st.Fragments.Count > 0 ? st.Fragments[0].MassT : 0):0.0}t · 멀어지는 빠르기 {(st.Fragments.Count > 0 ? st.Fragments[0].Speed : 0):0.00}m/s · \"{story}\"");
            Check("조각마다 전력 · 망이 끊긴다: 떨어진 방은 어둡고(배터리 · 배전반이 있어도 배의 전기에서 빠진다) · 주컴퓨터는 그 방을 못 본다 · 배는 그대로 밝다",
                lampBefore > 100 && light.Lamp[right] < 50 && light.Lamp[left] > 100 && !seen,
                $"오른쪽 조명 {lampBefore:0} → {light.Lamp[right]:0}lx · 왼쪽 {light.Lamp[left]:0}lx · 주컴퓨터가 오른쪽을 봄 {seen}");
            Check("떨어진 조각의 닫힌 방(차압 문)에 남은 사람은 한 시간 뒤에도 깨어 있다 — 남은 공기로 버틴다 · 배로 건너갈 길은 없다",
                alive && f.Pressure(Worlds.At(lay, 15, 3)) > 95, $"사람 {rightPeople.Count} · 깨어 있음 {alive} · 방 {f.Pressure(Worlds.At(lay, 15, 3)):0.0}kPa");

            // ── 점화의 단면 하중: 멀쩡한 배는 2g에도 버틴다 · 달아오르고 녹슨 목(벽 한 칸)은 3g에 끊긴다 ──
            (int frags, double sig) Burn(bool weak, double g)
            {
                var s2 = FieldStructure(lay, 2);   // 바깥(⑤)이 없는 세계 — 점화의 관성 하중을 시험이 직접 넣는다 (바깥이 있으면 틱마다 제 점화 값을 넣는다)
                var st2 = s2.Get<Structure>(); var f2 = s2.Get<Field>();
                if (weak)
                {
                    int c0 = s2.Causes.Add(CauseKind.Fire, "통로에 큰 불이 났다", 0);
                    st2.Destroy(s2, Worlds.At(lay, 10, 2), c0); st2.Destroy(s2, Worlds.At(lay, 10, 3), c0);
                    int neck = Worlds.At(lay, 10, 4);
                    f2.Ts[neck] = 1600; st2.Corrode[neck] = 0.9f;
                }
                s2.Run(2);
                st2.ThrustG = g; f2.AccelX = g * 9.81;
                s2.Run(30);
                return (st2.Fragments.Count, st2.Sigma(Worlds.At(lay, 10, 4), 0));
            }
            var whole = Burn(false, 2); var neckBurn = Burn(true, 3);
            Check("점화의 단면 하중 (선체 거더): 멀쩡한 배는 2g에도 버틴다 (통로 단면 응력 몇 MPa) · 달아오르고 녹슨 목 한 칸은 3g에 끊겨 배가 갈라진다",
                whole.frags == 0 && neckBurn.frags == 1, $"멀쩡함 2g: 조각 {whole.frags} · 통로 응력 {whole.sig:0.0}MPa / 약한 목 3g: 조각 {neckBurn.frags}");
        }
    }
}
