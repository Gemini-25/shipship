using ShipEngine;

namespace Lab;

/// <summary>법칙 시험 — 보존 · 대칭 · 결정론 · 경계. 빨강이면 엔진 버그다.</summary>
public static partial class Program
{
    private static void Laws()
    {
        Console.WriteLine("법칙 시험\n");
        var t = Tables.Default;

        if (Do("표"))
        {
            Console.WriteLine("[② 물질 표]");
            Check("표 읽기 — 물질 · 반응 · 재질 (읽을 때 화학식 · 원소 균형 · 비율을 검사)", t.Substances.Count > 20 && t.Reactions.Count > 20 && t.Materials.Count >= 10,
                $"물질 {t.Substances.Count} · 반응 {t.Reactions.Count} · 재질 {t.Materials.Count} · 기체 성분 {t.GasCount} · 바닥 저장 {t.StoreCount}");
            bool balanced = t.Reactions.All(r => t.Imbalance(r) == null);
            Check("반응식마다 원소가 같다", balanced, string.Join(" ", t.Reactions.Where(r => t.Imbalance(r) != null).Select(r => r.Id)));
            double poly = t.R("burn_poly").DH298 / 2 / 1000;
            double mj = -poly / (t.S("CH2").MolarMass * 1000); // MJ/kg
            Check("고분자 연소열이 실제와 비슷하다 (폴리에틸렌 약 43~46 MJ/kg)", mj > 40 && mj < 48, $"{mj:0.0} MJ/kg");
            double cell = -t.R("burn_cell").DH298 / 1000 / (t.S("Cellulose").MolarMass * 1000);
            Check("섬유 연소열이 실제와 비슷하다 (나무 · 종이 약 15~18 MJ/kg)", cell > 14 && cell < 19, $"{cell:0.0} MJ/kg");
            double h2 = -t.R("electrolysis").DH298 / 2 / 1000;
            Check("물 전기분해는 열을 먹는다 (+286 kJ/몰)", h2 < -280 && h2 > -290, $"{-h2:0} kJ/몰");
        }

        if (Do("혼합물"))
        {
            Console.WriteLine("\n[② 혼합물 반응 보존]");
            var m = new Mix(t);
            m[t.S("CH2")] = 10; m[t.S("O2")] = 40; m[t.S("N2")] = 150; m.T = 700;
            var e0 = t.ElementsOf(m.N); double en0 = m.Energy(t);
            double did = m.React(t, t.R("burn_poly"), 3) + m.React(t, t.R("burn_poly_co"), 1) + m.React(t, t.R("burn_poly_soot"), 1);
            var e1 = t.ElementsOf(m.N); double en1 = m.Energy(t);
            Check("반응 뒤 원소 총량이 같다", e0.Zip(e1).All(p => Rel(p.First, p.Second) < 1e-12), $"반응 {did:0.##}몰");
            Check("반응 뒤 에너지 장부가 같다 (반응열이 온도로)", Rel(en0, en1) < 1e-9 && m.T > 700, $"온도 700 → {m.T:0}K");
            var m2 = new Mix(t); m2[t.S("CH2")] = 1; m2[t.S("O2")] = 0.2;
            double ex = m2.React(t, t.R("burn_poly"), 5);
            Check("반응물이 모자라면 그만큼만 일어난다", Math.Abs(ex - 0.2 / 3) < 1e-12 && m2[t.S("O2")] < 1e-12, $"{ex:0.####}몰");
        }

        if (Do("결정론"))
        {
            Console.WriteLine("\n[① 결정론]");
            ulong Run(int threads)
            {
                int keep = Par.Threads; Par.Threads = threads;
                var s = Worlds.FieldOnly(Worlds.Ascii(Worlds.TwoRooms), 7, f => Stir(f));
                s.Run(300);
                Par.Threads = keep;
                return s.Hash();
            }
            ulong one = Run(1), many = Run(Math.Max(4, Environment.ProcessorCount));
            Check("1코어 = 여러 코어 (바이트까지)", one == many, $"{one:x16} / {many:x16}");
            ulong again = Run(1);
            Check("같은 시드 · 같은 입력이면 같은 지문", one == again);
        }

        if (Do("빈바이트"))
        {
            Console.WriteLine("\n[① 찍기 구조체에 빈 바이트가 없다 (쓰레기 값이 지문을 흔들지 않게)]");
            int Fields(Type ty) => ty.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Sum(f => f.FieldType.IsEnum ? System.Runtime.InteropServices.Marshal.SizeOf(Enum.GetUnderlyingType(f.FieldType)) : System.Runtime.InteropServices.Marshal.SizeOf(f.FieldType));
            foreach (var ty in new[] { typeof(CauseNode), typeof(Fact), typeof(Heard), typeof(Cloud), typeof(Rock) })
            {
                int size = (int)typeof(System.Runtime.CompilerServices.Unsafe).GetMethod("SizeOf")!.MakeGenericMethod(ty).Invoke(null, null)!;
                Check($"{ty.Name}: 크기 {size} = 필드 합 {Fields(ty)}", size == Fields(ty));
            }
        }

        if (Do("찍기"))
        {
            Console.WriteLine("\n[① 찍기 · 되살리기 · 복제]");
            var a = Worlds.FieldOnly(Worlds.Ascii(Worlds.TwoRooms), 3, f => Stir(f));
            a.Run(120);
            byte[] s0 = a.Save();
            var b = a.Clone();
            byte[] s1 = b.Save();
            Check("되살린 세계를 다시 찍으면 바이트까지 같다", s0.AsSpan().SequenceEqual(s1), $"{s0.Length / 1024}KB");
            long split = -1;
            for (int i = 0; i < 300 && split < 0; i++) { a.Step(); b.Step(); if (a.Hash() != b.Hash()) split = a.Tick; }
            Check("복제를 300틱 이어 가도 틱마다 같다", split < 0, split < 0 ? "" : $"틱 {split}에 갈라짐");

            // 사실 흐름: 아직 적은 것보다 많이 물어도(최근 n · 음수 번호) 멈추지 않고 있는 것만 준다 (예전: 배열 밖을 읽어 멈췄다)
            {
                var fx = new Facts();
                fx.Emit(1, FactKind.Ignition, 3);
                int got = -1; string err = "";
                try { got = fx.Recent(20).Count() + fx.Since(-5).Count(); } catch (Exception e) { err = e.GetType().Name; }
                Check("사실 흐름: 적은 것보다 많이 물어도(최근 20개 · 음수 번호) 멈추지 않고 있는 것만 준다", got == 2, err.Length > 0 ? $"멈춤: {err}" : $"{got}개");
            }

            // 저장 판: 맨 앞에 판 번호 — 다른 판 · 판 없는 저장은 엉뚱하게 읽지 않고 분명한 말로 거절한다
            string Refused(byte[] data)
            {
                try { var c = new Sim(a.Setup); c.Load(data); return ""; }
                catch (InvalidDataException e) { return e.Message; }
            }
            var other = (byte[])s0.Clone();
            BitConverter.GetBytes(Sim.SaveVersion + 1).CopyTo(other, 8); // 이름표(8바이트) 다음이 판 번호
            string newer = Refused(other), bare = Refused(s0[12..]), same = Refused(s0);
            Check("저장 판: 다른 판 · 판 번호 없는 저장은 분명한 말로 거절하고 · 같은 판은 읽는다",
                newer.Contains($"저장 판 {Sim.SaveVersion + 1}") && bare.Contains("판 번호가 없는") && same == "", $"다른 판: \"{newer}\" · 판 없음: \"{bare}\"");

            // 저장 모양 지문: 엔진이 저장하는 것(무엇을 어떤 순서 · 꼴로)이 바뀌었는데 판 번호를 그대로 두면 빨강 — 옛 저장을 엉뚱하게 읽는 일을 막는다
            ulong shape = ShipWorld.Crewed("Didim", 1, 6, 2).ShapeOf();
            Check($"저장 모양이 판 {Sim.SaveVersion}의 지문과 같다 (모양이 바뀌면 Sim.SaveVersion을 올리고 SaveShape를 고친다)", shape == Sim.SaveShape,
                $"지금 모양 0x{shape:x16} · 판 {Sim.SaveVersion}의 지문 0x{Sim.SaveShape:x16}");
        }

        if (Do("보존"))
        {
            Console.WriteLine("\n[③ 닫힌 배 보존 — 단열]");
            var s = Worlds.FieldOnly(Worlds.Ascii(Worlds.TwoRooms), 5, f => { f.Adiabatic = true; Stir(f); });
            var f = s.Get<Field>();
            var gas0 = Enumerable.Range(0, f.G).Select(g => f.Total(g)).ToArray();
            var el0 = f.ElementTotals(); double e0 = f.EnergyLedger();
            s.Run(1500); // 한 시간
            var gas1 = Enumerable.Range(0, f.G).Select(g => f.Total(g)).ToArray();
            var el1 = f.ElementTotals(); double e1 = f.EnergyLedger();
            double worstEl = el0.Zip(el1).Max(p => Rel(p.First, p.Second));
            Check("원소 총량이 같다 (흐름 · 확산 · 응결 · 증발 · 그을음)", worstEl < 1e-9, $"가장 큰 차이 {worstEl:0.0e0}");
            double worstN2 = Rel(gas0[f.T.gN2], gas1[f.T.gN2]);
            Check("질소(반응 없는 기체) 몰 수가 같다", worstN2 < 1e-9, $"{worstN2:0.0e0}");
            Check("에너지 장부가 같다", Rel(e0, e1) < 1e-9, $"{Rel(e0, e1):0.0e0}");
            bool neg = f.N.Any(v => v < 0) || f.U.Any(v => v < 0) || f.Store.Any(v => v < 0);
            Check("음수가 없다", !neg);
            // 압력: 문으로 이어진 두 방은 같아진다
            int left = Worlds.At(f.L, 3, 2), right = Worlds.At(f.L, 15, 2);
            Check("열린 문틈으로 이어진 두 방의 압력이 같아진다", Math.Abs(f.Pressure(left) - f.Pressure(right)) < 0.5,
                $"{f.Pressure(left):0.0} / {f.Pressure(right):0.0} kPa");
        }

        if (Do("대칭"))
        {
            Console.WriteLine("\n[③ 대칭]");
            var s = Worlds.FieldOnly(Worlds.Ascii(Worlds.Symmetric), 1, f =>
            {
                f.Adiabatic = true;
                int c = Worlds.At(f.L, 5, 3);
                f.AddGas(c, f.T.gCO2, 30, 293.15);
                f.AddHeat(c, 2e6);
            });
            s.Run(200);
            var f = s.Get<Field>();
            double worst = 0;
            for (int y = 1; y <= 5; y++)
                for (int x = 1; x <= 4; x++)
                {
                    int a = Worlds.At(f.L, x, y), b = Worlds.At(f.L, 10 - x, y);
                    worst = Math.Max(worst, Rel(f.Gas(f.T.gCO2, a), f.Gas(f.T.gCO2, b)));
                    worst = Math.Max(worst, Rel(f.Temp(a), f.Temp(b)));
                }
            Check("가운데 원천의 이산화탄소 · 열이 좌우 똑같이 퍼진다", worst < 1e-9, $"가장 큰 차이 {worst:0.0e0}");
            // 열 없이 이산화탄소만: 1분 뒤 원천 곁이 훨씬 짙다 (압력 고르기가 성분을 순간 이동시키지 않는다)
            var s2 = Worlds.FieldOnly(Worlds.Ascii(Worlds.Symmetric), 1, ff => ff.AddGas(Worlds.At(ff.L, 5, 3), ff.T.gCO2, 30, 293.15));
            s2.Run(25);
            var f2 = s2.Get<Field>();
            int ctr = Worlds.At(f2.L, 5, 3), edge = Worlds.At(f2.L, 1, 3);
            Check("원천 곁이 더 짙다 (1분 — 확산은 칸마다 퍼진다)", f2.Ppm(f2.T.gCO2, ctr) > f2.Ppm(f2.T.gCO2, edge) * 5,
                $"가운데 {f2.Ppm(f2.T.gCO2, ctr):0}ppm · 가장자리 {f2.Ppm(f2.T.gCO2, edge):0}ppm");
        }

        if (Do("진공"))
        {
            Console.WriteLine("\n[③ 진공 경계]");
            var l = Worlds.Ascii(Worlds.Sealed);
            int hole = Worlds.At(l, 0, 2); // 왼쪽 방 왼쪽 외판
            var s = Worlds.FieldOnly(l, 1, f => f.SetHole(hole, 0.01f));
            var f = s.Get<Field>();
            int roomCell = Worlds.At(l, 4, 2), other = Worlds.At(l, 15, 2);
            double p0 = f.Pressure(roomCell), pc0 = f.Pressure(other);
            var trace = new List<double>();
            for (int i = 0; i < 600; i++) { s.Step(); trace.Add(f.Pressure(roomCell)); }
            // 단열 팽창으로 식은 공기가 벽에 데워지며 잠깐 오를 수는 있다 — 1분 뒤부터는 시작보다 늘 낮고, 끝은 절반 아래
            bool down = trace.Skip(25).All(v => v < p0 * 0.95);
            Check("구멍 난 구역의 압력이 떨어진다", down && trace[^1] < p0 * 0.5, $"{p0:0.0} → {trace[^1]:0.0} kPa (24분)");
            Check("닫힌 차압 문 너머 방은 그대로", Math.Abs(f.Pressure(other) - pc0) < 0.5, $"{pc0:0.0} → {f.Pressure(other):0.0} kPa");
            // 오리피스 식과 견주기: 막힘 흐름 τ = n/ṅ
            var l2 = Worlds.Ascii(Worlds.Symmetric);
            int h2 = Worlds.At(l2, 0, 3);
            var s2 = Worlds.FieldOnly(l2, 1, ff => ff.SetHole(h2, 0.005f));
            var f2 = s2.Get<Field>();
            int c2 = Worlds.At(l2, 5, 3);
            double start = f2.Pressure(c2);
            long half = -1;
            for (int i = 0; i < 3000 && half < 0; i++) { s2.Step(); if (f2.Pressure(c2) < start / 2) half = s2.Tick; }
            double tau = 45 * Field.CellVolume / (Field.Cd * 0.005 * 1.3944 * Field.R * Math.Sqrt(293.15));
            double expect = tau * Math.Log(2) / Time.SecondsPerTick;
            Check("반감 시간이 오리피스 식과 맞는다 (±40% — 남은 공기가 단열 팽창으로 식어 흐름이 느려진다)",
                half > 0 && half > expect * 0.8 && half < expect * 1.6, $"{half}틱 · 식 {expect:0}틱");
            // 큰 구멍: 몇 초 안에 거의 진공
            var s3 = Worlds.FieldOnly(Worlds.Ascii(Worlds.Symmetric), 1, ff => ff.SetHole(h2, 2.5f));
            s3.Run(10);
            var f3 = s3.Get<Field>();
            Check("뚫린 외판(2.5m²): 24초 안에 거의 진공", f3.Pressure(c2) < 5, $"{f3.Pressure(c2):0.00} kPa");
            Check("남은 공기는 단열 팽창으로 식는다", s3.Get<Field>().Temp(c2) < 260 || f3.Moles(c2) < 1e-3, $"{f3.Temp(c2):0}K");
        }

        if (Do("바람"))
        {
            Console.WriteLine("\n[③ 바람]");
            var l = Worlds.Ascii(Worlds.Symmetric);
            int hole = Worlds.At(l, 10, 3); // 오른쪽 외판
            var s = Worlds.FieldOnly(l, 1, f => f.SetHole(hole, 0.3f));
            s.Run(2);
            var f = s.Get<Field>();
            int near = Worlds.At(l, 9, 3), far = Worlds.At(l, 2, 3);
            Check("구멍 쪽으로 바람이 분다 (오른쪽 +)", f.WindX[near] > 1 && f.WindX[far] > 0, $"곁 {f.WindX[near]:0.0} m/s · 먼 곳 {f.WindX[far]:0.00} m/s");
            Check("구멍 곁이 더 세다", f.WindX[near] > f.WindX[far] * 2);
        }

        if (Do("문"))
        {
            Console.WriteLine("\n[③ 문 · 구역]");
            var l = Worlds.Ascii(Worlds.TwoRooms);
            var s = Worlds.FieldOnly(l, 1);
            var f = s.Get<Field>();
            s.Step();
            int regions0 = f.Regions.Count;
            f.SetDoor(0, 1f); // 왼쪽 방 ↔ 오른쪽 방 문 활짝
            s.Step();
            Check("문을 열면 두 방이 한 구역이 된다", f.Regions.Count == regions0 - 1, $"{regions0} → {f.Regions.Count}");
            // 진공 방에 문 열기 → 압력이 반씩
            var s2 = Worlds.FieldOnly(l, 1, ff =>
            {
                foreach (int c in ff.L.Rooms.First(r => r.Cells.Contains(Worlds.At(ff.L, 15, 2))).Cells)
                    for (int g = 0; g < ff.G; g++) { ff.N[c * ff.G + g] = 0; ff.U[c] = 0; }
            });
            var f2 = s2.Get<Field>();
            int lc = Worlds.At(l, 3, 2), rc = Worlds.At(l, 15, 2);
            f2.SetDoor(0, 0.3f); // 반쯤 — 틈 흐름
            s2.Run(50);
            Check("반쯤 열린 문틈으로 진공 방이 찬다", f2.Pressure(rc) > 20 && f2.Pressure(lc) < 101, $"왼쪽 {f2.Pressure(lc):0.0} · 오른쪽 {f2.Pressure(rc):0.0} kPa");
        }

        if (Do("열"))
        {
            Console.WriteLine("\n[③ 열]");
            var s = Worlds.FieldOnly(Worlds.Ascii(Worlds.Symmetric), 1, f => f.Adiabatic = true);
            var f = s.Get<Field>();
            int c = Worlds.At(f.L, 5, 3);
            f.AddHeat(c, 5e6); // 뜨거운 공기
            double tg0 = f.Temp(c);
            s.Run(150);
            Check("뜨거운 공기는 바닥 · 이웃으로 식는다", f.Temp(c) < tg0 - 100, $"{tg0:0} → {f.Temp(c):0}K");
            // 외판 복사: 사람 · 장치 열 없이 진공 속 배는 식는다
            var s2 = Worlds.FieldOnly(Worlds.Ascii(Worlds.Symmetric), 1, ff => { ff.Outside.Sun = 0; });
            var f2 = s2.Get<Field>();
            double t0 = f2.Temp(c);
            s2.Run(Time.Hours(12));
            Check("해가 없는 우주에서 배는 천천히 식는다 (단열재 덕분에 느리게)", f2.Temp(c) < t0 && f2.Temp(c) > t0 - 30, $"12시간 {t0:0.0} → {f2.Temp(c):0.0}K");
        }

        if (Do("불"))
        {
            Console.WriteLine("\n[② 칸의 화학 — 불 보존 (단열 · 닫힌 방)]");
            var s = Worlds.WithChem(Worlds.Ascii(Worlds.Lounge, Worlds.Seats), 1, (f, c) =>
            {
                f.Adiabatic = true;
                c.Ignite(Worlds.At(f.L, 3, 3), 3e6, 0);
            });
            var f = s.Get<Field>(); var ch = s.Get<Chemistry>();
            var el0 = f.ElementTotals(); double e0 = f.EnergyLedger(); double co20 = f.Total(f.T.gCO2);
            double peak = 0;
            for (int i = 0; i < Time.Minutes(30); i++) { s.Step(); peak = Math.Max(peak, ch.Hrr.Max()); }
            var el1 = f.ElementTotals(); double e1 = f.EnergyLedger();
            double worst = el0.Zip(el1).Max(p => Rel(p.First, p.Second));
            Check("불이 타도 원소 총량이 같다 (연료 · 산소 → 이산화탄소 · 물 · 일산화탄소 · 그을음)", worst < 1e-9, $"{worst:0.0e0} · 이산화탄소 {co20:0} → {f.Total(f.T.gCO2):0}몰");
            Check("불이 타도 에너지 장부가 같다 (반응열 → 기체 · 바닥 · 이웃)", Rel(e0, e1) < 1e-9, $"{Rel(e0, e1):0.0e0} · 가장 센 불 {peak:0}kW");
            Check("음수가 없다", !f.N.Any(v => v < 0) && !f.Store.Any(v => v < 0) && !f.U.Any(v => v < 0));
        }

        if (Do("소리"))
        {
            Console.WriteLine("\n[③ 소리]");
            var l = Worlds.Ascii(Worlds.Sealed);
            var s = Worlds.Full(l, 1);
            var f = s.Get<Field>(); var snd = s.Get<Sound>();
            int a = Worlds.At(l, 4, 2), near = Worlds.At(l, 8, 2), other = Worlds.At(l, 15, 2);
            snd.Emit(s, a, 100, SoundKind.Bang);
            float dNear = snd.Loudest(near), dOther = snd.Loudest(other);
            Check("소리는 멀수록 작고, 닫힌 차압 문 너머는 더 작다", dNear > dOther + 15 && dOther > 0, $"같은 방 {dNear:0}dB · 문 너머 {dOther:0}dB");
            // 왼쪽 방을 진공으로
            foreach (int c in l.Rooms.First(r => r.Cells.Contains(a)).Cells) { for (int g = 0; g < f.G; g++) f.N[c * f.G + g] = 0; f.U[c] = 0; }
            s.Step(); s.Step(); s.Step();
            snd.Emit(s, a, 120, SoundKind.Bang);
            Check("진공에서는 소리가 전해지지 않는다", snd.Loudest(near) == 0 && snd.Loudest(other) == 0, $"{snd.Loudest(near):0} / {snd.Loudest(other):0}dB");
        }

        if (Do("모든엔진"))
        {
            Console.WriteLine("\n[① 결정론 · 찍기 — 모든 엔진 · 배 디딤호]");
            ulong Run(int threads, out byte[] snap)
            {
                int keep = Par.Threads; Par.Threads = threads;
                var s = ShipWorld.Create("Didim", 9);
                var f = s.Get<Field>();
                var floor = Enumerable.Range(0, f.Cells).Where(i => f.L.Kind[i] == CellKind.Floor && f.L.FurnitureAt[i] >= 0).ToArray();
                s.Get<Chemistry>().Ignite(floor[floor.Length / 2], 3e6, 0);
                int hull = Enumerable.Range(0, f.Cells).First(i => f.L.Hull[i] && f.L.Y(i) == f.L.H / 2);
                s.Get<Structure>().Impact(s, hull, 2e5, 1, 0, 0);
                s.Run(400);
                snap = s.Save();
                Par.Threads = keep;
                return s.Hash();
            }
            ulong one = Run(1, out var snap1), many = Run(Math.Max(4, Environment.ProcessorCount), out _);
            Check("모든 엔진: 1코어 = 여러 코어 (불 · 충돌 · 감압 · 소리 · 빛 · 구조)", one == many, $"{one:x16} / {many:x16}");
            var b = new Sim(ShipWorld.Setup(Layout.Ship("Didim", Tables.Default), 9));
            b.Load(snap1);
            Check("모든 엔진: 되살린 세계의 지문이 같다", b.Hash() == one, $"{snap1.Length / 1024}KB");
            // 원래 세계 ↔ 도중에 찍어 되살린 세계: 이어 돌려도 바이트까지 같다 (다시보기 · 주컴퓨터의 상상이 이것에 기댄다)
            {
                var o = ShipWorld.Crewed("Didim", 9, 6, 3);
                var of = o.Get<Field>();
                var fl = Enumerable.Range(0, of.Cells).Where(i => of.L.Kind[i] == CellKind.Floor && of.L.FurnitureAt[i] >= 0).ToArray();
                o.Get<Chemistry>().Ignite(fl[fl.Length / 2], 3e6, 0);
                o.Get<Structure>().Impact(o, Enumerable.Range(0, of.Cells).First(i => of.L.Hull[i] && of.L.Y(i) == of.L.H / 2), 2e5, 1, 0, 0);
                o.Run(10);
                var r = new Sim(o.Setup); r.Load(o.Save());
                long firstDiff = -1;
                for (int k = 0; k < 400 && firstDiff < 0; k++) { o.Step(); r.Step(); if (k % 20 == 19 && o.Hash() != r.Hash()) firstDiff = o.Tick; }
                Check("모든 엔진: 원래 세계와 도중에 되살린 세계가 이어 돌려도 같다 (사람 6 · 로봇 3 · 불 · 충돌 · 400틱)", firstDiff < 0, firstDiff < 0 ? "" : $"틱 {firstDiff}에 갈라짐");
            }
        }

        if (Do("마음"))
        {
            Console.WriteLine("\n[⑧ 결정론 · 찍기 — 사람 · 로봇이 탄 배]");
            ulong RunCrew(int threads, int ticks, out byte[] snap)
            {
                int keep = Par.Threads; Par.Threads = threads;
                var s = ShipWorld.Crewed("Didim", 5, 6, 2);
                var f = s.Get<Field>();
                var floor = Enumerable.Range(0, f.Cells).Where(i => f.L.Kind[i] == CellKind.Floor && f.L.FurnitureAt[i] >= 0).ToArray();
                s.Run(200);
                s.Get<Chemistry>().Ignite(floor[floor.Length / 3], 3e6, 0);
                s.Run(ticks - 200);
                snap = s.Save();
                Par.Threads = keep;
                return s.Hash();
            }
            ulong one = RunCrew(1, 600, out var snap), many = RunCrew(Math.Max(4, Environment.ProcessorCount), 600, out _);
            Check("마음: 1코어 = 여러 코어 (사람 6 · 로봇 2 · 불)", one == many, $"{one:x16} / {many:x16}");
            // 찍기 → 되살려 이어 돌린 것 = 그냥 이어 돌린 것
            var a = new Sim(ShipWorld.Setup(Layout.Ship("Didim", Tables.Default), 5));
            a.Load(snap);
            var b = a.Clone();
            a.Run(300); b.Run(300);
            Check("마음: 되살린 세계도 같은 길을 간다 (믿음 · 계획 · 길 · 물건까지)", a.Hash() == b.Hash() && a.Get<Mind>().Agents.Count == 8,
                $"{a.Hash():x16} / {b.Hash():x16} · {snap.Length / 1024}KB");
            // 먹으면 음식(포도당)이 냉장고에서 몸으로 옮겨 간다 — 사라지거나 생기지 않는다
            var e = ShipWorld.Crewed("Didim", 5, 4, 0);
            var ef = e.Get<Field>(); var el = e.Get<Life>(); var em = e.Get<Mind>();
            foreach (var ag in em.Agents) ag.C.Hunger = 1;
            double food0 = Enumerable.Range(0, ef.Cells).Sum(i => ef.Stored(ef.T.sFood, i)), rot0 = el.FoodRotted, eaten0 = em.FoodEaten;
            long meals0 = em.Meals;
            for (int k = 0; k < Time.Minutes(40); k++) e.Step();
            double food1 = Enumerable.Range(0, ef.Cells).Sum(i => ef.Stored(ef.T.sFood, i));
            double gone = food0 - food1, eaten = em.FoodEaten - eaten0, rot = el.FoodRotted - rot0;
            Check("먹으면 음식이 냉장고에서 몸으로 — 저장고가 준 만큼 = 먹은 것 + 썩은 것", em.Meals > meals0 && Math.Abs(gone - eaten - rot) < 1e-6,
                $"끼니 {em.Meals - meals0} · 저장고 {food0:0.000}→{food1:0.000}몰 · 먹음 {eaten:0.000} · 썩음 {rot:0.000}");
        }

        if (Do("생명"))
        {
            Console.WriteLine("\n[⑦ 숨 · 체온 보존 — 단열 닫힌 방에 사람 넷]");
            var lay = Worlds.Ascii(Worlds.Symmetric);
            var s = new Sim(new SimSetup(lay, 1, sim => { var ff = new Field(sim.Layout, sim.Tables); sim.Add(ff); sim.Add(new Life(ff)); }));
            var f = s.Get<Field>(); var life = s.Get<Life>();
            f.FillAir(); f.Adiabatic = true;
            foreach (var x in new[] { 2, 4, 6, 8 }) { int b = life.AddBody(Worlds.At(lay, x, 3)); life[b].ActivityW = 120; }
            double[] Elements()
            {
                var e = f.ElementTotals();
                var (g, w, fat) = life.BodyMatter();
                var m = new double[f.T.Substances.Count];
                m[f.T.S("Glucose").Index] = g; m[f.T.S("H2O_l").Index] = w; m[f.T.S("Fat").Index] = fat;
                var be = f.T.ElementsOf(m);
                return e.Zip(be, (a, b) => a + b).ToArray();
            }
            var e0 = Elements(); double en0 = f.EnergyLedger() + life.EnergyLedger();
            s.Run(Time.Hours(2));
            var e1 = Elements(); double en1 = f.EnergyLedger() + life.EnergyLedger();
            double worst = e0.Zip(e1).Max(p => Rel(p.First, p.Second));
            Check("숨 · 땀 · 체온: 원소 총량이 같다 (몸의 양분 · 물 + 장)", worst < 1e-9, $"{worst:0.0e0} · 이산화탄소 {f.Ppm(f.T.gCO2, Worlds.At(lay, 5, 3)):0}ppm");
            Check("숨 · 땀 · 체온: 에너지 장부가 같다 (몸의 열 · 양분 + 장)", Rel(en0, en1) < 1e-9, $"{Rel(en0, en1):0.0e0} · 체온 {life[0].CoreK - 273.15:0.0}℃");
        }

        if (Do("물"))
        {
            Console.WriteLine("\n[③ 물 · 응결]");
            var s = Worlds.FieldOnly(Worlds.Ascii(Worlds.Symmetric), 1, f =>
            {
                f.Adiabatic = true;
                for (int i = 0; i < f.Cells; i++) if (f.L.Kind[i] != CellKind.Outside) f.Ts[i] = 278; // 차가운 바닥 · 벽
                int c = Worlds.At(f.L, 5, 3);
                f.AddGas(c, f.T.gH2O, 40, 293.15); // 김
            });
            var f = s.Get<Field>();
            var h0 = f.ElementTotals()[Array.IndexOf(f.T.ElementSymbols, "H")];
            s.Run(300);
            double water = 0; for (int i = 0; i < f.Cells; i++) water += f.Stored(f.T.sWater, i);
            var h1 = f.ElementTotals()[Array.IndexOf(f.T.ElementSymbols, "H")];
            Check("차가운 바닥에 이슬이 맺힌다", water > 1, $"{water:0.0}몰 ({water * 18 / 1000:0.00}kg)");
            Check("수소 총량이 같다", Rel(h0, h1) < 1e-9);
            // 물웅덩이가 퍼진다
            var s2 = Worlds.FieldOnly(Worlds.Ascii(Worlds.Symmetric), 1, ff => ff.AddStore(Worlds.At(ff.L, 5, 3), ff.T.sWater, 3000));
            var f2 = s2.Get<Field>();
            s2.Run(300);
            int wetCells = 0; for (int i = 0; i < f2.Cells; i++) if (f2.Stored(f2.T.sWater, i) > 10) wetCells++;
            Check("쏟아진 물(54kg)이 바닥을 따라 퍼진다", wetCells > 9, $"젖은 칸 {wetCells}");
        }
    }

    /// <summary>시험용으로 세계를 흔든다: 압력 차 · 뜨거운 곳 · 이산화탄소 · 물웅덩이 · 반쯤 열린 문 · 그을음.</summary>
    internal static void Stir(Field f)
    {
        var t = f.T;
        int a = Worlds.At(f.L, 3, 2), b = Worlds.At(f.L, 18, 3), c = Worlds.At(f.L, 8, 7);
        f.AddGas(a, t.gCO2, 20, 300);
        f.AddHeat(b, 3e6);
        f.AddGas(c, t.gH2O, 15, 330);
        f.AddGas(c, t.gSoot, 2, 400);
        f.AddStore(Worlds.At(f.L, 20, 7), t.sWater, 400);
        // 오른쪽 방은 압력을 낮춘다
        foreach (int i in f.L.Rooms.FirstOrDefault(r => r.Cells.Contains(b))?.Cells ?? Array.Empty<int>())
        {
            for (int g = 0; g < f.G; g++) f.N[i * f.G + g] *= 0.6;
            f.U[i] *= 0.6;
        }
        if (f.L.Doors.Count > 0) f.SetDoor(0, 0.25f);
    }
}
