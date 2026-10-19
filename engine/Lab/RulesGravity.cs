using ShipEngine;

namespace Lab;

/// <summary>인공 중력 · 무중력 (바퀴 3) — 발생기 · 몸마다 바닥 중력. docs/ENGINE.md '인공 중력 · 무중력'</summary>
public static partial class Program
{
    private static void GravityRules()
    {
        if (!Do("중력")) return;
        Console.WriteLine("\n[인공 중력 · 무중력]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var legend = Worlds.MindLegend; legend['G'] = "GravityGenerator";
        var lay = Worlds.Ascii(Worlds.MindShip.Replace("#.........#....a....a.r.#", "#....G....#....a....a.r.#"), legend);
        int At(int x, int y) => Worlds.At(lay, x, y);

        // ── (가) 발생기: 켜진 배는 1g · 끄면 10초 남짓에 무중력 (사실 · 원인 '꺼졌다') · 다시 켜면 30초에 1g / 발생기가 없는 시험 배는 바탕 1g ──
        {
            var s = Worlds.Full(lay, 1); var f = s.Get<Field>(); var net = s.Get<Network>();
            int gen = net.Devices.First(d => d.Type == "GravityGenerator").Id;
            s.Step();
            float first = f.Grav[At(15, 2)];   // 새 배는 처음부터 1g (장이 서는 30초는 켤 때만 — 처음 30초를 무중력으로 떠나던 결함)
            s.Run(Time.Minutes(1));
            float start = Math.Min(first, f.Grav[At(15, 2)]);
            long f0 = s.Facts.Total;
            net.SetOn(gen, false);
            s.Run(5);   // 12초
            float off = f.Grav[At(15, 2)], offFar = f.Grav[At(3, 8)];
            var lost = s.Facts.Since(f0).FirstOrDefault(x => x.Kind == FactKind.Gravity && x.Size == 0);
            net.SetOn(gen, true);
            s.Run(6);   // 14초 — 아직 반쯤
            float half = f.Grav[At(15, 2)];
            s.Run(8);   // 33초
            float on = f.Grav[At(15, 2)];
            var back = s.Facts.Since(f0).FirstOrDefault(x => x.Kind == FactKind.Gravity && x.Size == 1);
            var plain = Worlds.Full(Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend), 1); plain.Run(Time.Minutes(1));
            float bare = plain.Get<Field>().Grav.Min();
            string why = lost.Kind == FactKind.Gravity ? s.Causes.Text(lost.Cause) : "";
            Check("인공 중력 발생기: 켜진 배는 1g · 끄면 12초에 무중력 (배 어디나 · 사실 '중력이 사라진다' — 꺼졌다) · 다시 켜면 반쯤에서 30초 남짓에 1g (사실 '돌아온다') / 발생기가 없는 시험 배는 바탕 1g",
                start == 1 && off == 0 && offFar == 0 && why.Contains("꺼졌다") && half > 0.3f && half < 0.7f && on == 1 && back.Kind == FactKind.Gravity && bare == 1,
                $"처음 {start:0.00}g · 끄고 12초 {off:0.00} (먼 칸 {offFar:0.00}) · \"{why}\" · 켜고 14초 {half:0.00} · 33초 {on:0.00} · 돌아옴 {back.Kind == FactKind.Gravity} / 발생기 없는 배 {bare:0.00}g");
        }

        // ── 전기가 끊기면 · 망가지면 무중력 — 원인 사슬이 그 까닭에 닿는다 ──
        {
            (float g, string why) Run(bool breakIt)
            {
                var s = Worlds.Full(lay, 2); var f = s.Get<Field>(); var net = s.Get<Network>();
                var gen = net.Devices.First(d => d.Type == "GravityGenerator");
                s.Run(Time.Minutes(1));
                long f0 = s.Facts.Total;
                if (breakIt) net.Age(gen.Id, Part.Mechanical, 1.2, s.Causes.Add(CauseKind.Story, "발생기 회전자 축받이가 닳았다", s.Tick));
                else net.Breaker[gen.Feeder] = 0;
                s.Run(Time.Minutes(1));
                var lost = s.Facts.Since(f0).FirstOrDefault(x => x.Kind == FactKind.Gravity && x.Size == 0);
                return (f.Grav[At(15, 2)], lost.Kind == FactKind.Gravity ? s.Causes.Story(lost.Cause) : "");
            }
            var cut = Run(false); var broke = Run(true);
            Check("전기가 끊기면 (차단기) 무중력 · '전기가 끊겼다' / 발생기가 망가지면 무중력 · 원인 사슬이 그 고장에 닿는다",
                cut.g == 0 && cut.why.Contains("전기가 끊겼다") && broke.g == 0 && broke.why.Contains("망가졌다") && broke.why.Contains("→"),
                $"차단기: {cut.g:0.00}g · \"{cut.why}\" / 고장: {broke.g:0.00}g · \"{broke.why}\"");
        }

        // ── (나) 몸 · 움직임: 무중력에서 사람은 손잡이를 잡고 떠 간다 (처음 0.4m/s · 떠서 8시간 지낸 이는 1m/s) · 로봇은 자석 발로 0.6배 ──
        {
            (float person, float vet, float robot, float walk) Speeds()
            {
                var s = Worlds.Full(lay, 4); var net = s.Get<Network>(); var m = s.Get<Mind>();
                int p = m.AddPerson(s, At(2, 8)), v = m.AddPerson(s, At(2, 7)), r = m.AddRobot(s, At(3, 8), AgentKind.Robot, At(3, 8));
                m[v].C.ZeroGH = 9;
                s.Run(3);
                float walk = (float)m.Pace(p), rwalk = (float)m.Pace(r);
                net.SetOn(net.Devices.First(d => d.Type == "GravityGenerator").Id, false);
                s.Run(6);
                return ((float)m.Pace(p), (float)m.Pace(v), (float)(m.Pace(r) / rwalk), walk);
            }
            var sp = Speeds();
            Check("무중력의 움직임: 처음인 이는 손잡이를 잡고 0.4m/s로 떠 가고 (1g 걸음보다 느리다) · 떠서 8시간 넘게 지낸 이는 1m/s · 로봇은 자석 발로 0.6배",
                Math.Abs(sp.person - 0.4f) < 0.01f && sp.walk > 1 && Math.Abs(sp.vet - 1f) < 0.01f && Math.Abs(sp.robot - 0.6f) < 0.01f,
                $"1g 걸음 {sp.walk:0.00} · 무중력 처음 {sp.person:0.00} · 익숙한 이 {sp.vet:0.00} · 로봇 {sp.robot:0.00}배");
        }

        // ── 떠오름 · 내려앉음: 침대 밖에서 자던 이는 떠다니다 부딪혀 깨고 (침대의 이는 그대로) · 중력이 돌아오면 서툰 이는 바닥에 떨어져 넘어지기도 한다 (익숙한 이는 아니다) ──
        {
            var s = Worlds.Full(lay, 5); var net = s.Get<Network>(); var m = s.Get<Mind>(); var life = s.Get<Life>();
            int gen = net.Devices.First(d => d.Type == "GravityGenerator").Id;
            int bed = s.Layout.Furniture.First(fu => fu.Type == "Bed").Cells[0];
            int inBed = m.AddPerson(s, bed), onFloor = m.AddPerson(s, At(13, 3));
            var novices = Enumerable.Range(0, 8).Select(i => m.AddPerson(s, At(12 + i, i % 2 == 0 ? 2 : 4))).ToArray();
            var vets = Enumerable.Range(0, 8).Select(i => m.AddPerson(s, At(2 + i % 6, 7 + i / 6))).ToArray();
            foreach (int v in vets) m[v].C.ZeroGH = 10;
            foreach (int who in new[] { inBed, onFloor }) { life[m[who].C.Body].Asleep = 1; m[who].C.Sleepy = 1; }   // 제자리에서 잠든 채로 (침대 · 바닥)
            s.Run(2);
            long f0 = s.Facts.Total;
            net.SetOn(gen, false);
            for (int k = 0; k < 6; k++) { s.Step(); if (Debug) Console.WriteLine($"    {k} g {s.Get<Field>().Grav[m[onFloor].C.Cell]:0.00} 바닥 떠 {m[onFloor].C.Floated} 잠 {life[m[onFloor].C.Body].Asleep} · {m.Thought(onFloor)} / 침대 떠 {m[inBed].C.Floated} 잠 {life[m[inBed].C.Body].Asleep}"); }
            var woke = s.Facts.Since(f0).Where(x => x.Kind == FactKind.Woke).ToList();
            if (Debug) foreach (var w in woke) Console.WriteLine($"    깸 {m.NameOf(w.Actor)} ({(w.Actor == inBed ? "침대" : w.Actor == onFloor ? "바닥" : "?")}) · {s.Causes.Story(w.Cause)} · 깨운 이 {w.Other}");
            bool bedSleeps = !woke.Any(x => x.Actor == inBed), floorWoke = woke.Any(x => x.Actor == onFloor);
            s.Run(Time.Minutes(2));
            net.SetOn(gen, true);
            s.Run(Time.Minutes(1));
            var falls = s.Facts.Since(f0).Where(x => x.Kind == FactKind.Fell).ToList();
            int nf = falls.Count(x => novices.Contains(x.Actor)), vf = falls.Count(x => vets.Contains(x.Actor));
            string why = falls.Count > 0 ? s.Causes.Story(falls[0].Cause) : "";
            Check("떠오름 · 내려앉음: 침대 밖에서 자던 이는 떠다니다 부딪혀 깨고 침대(침낭)의 이는 그대로 잔다 · 중력이 돌아오면 서툰 이는 바닥에 떨어져 넘어지기도 한다 (원인: 돌아온 중력) · 익숙한 이는 넘어지지 않는다",
                bedSleeps && floorWoke && nf >= 1 && nf < novices.Length && vf == 0 && why.Contains("다시 돌아") && why.Contains("바닥으로 떨어져"),
                $"침대 {(bedSleeps ? "잠" : "깸")} · 바닥 {(floorWoke ? "깸" : "잠")} · 서툰 이 넘어짐 {nf}/{novices.Length} · 익숙한 이 {vf}/{vets.Length} · \"{why}\"");
        }

        // ── 주컴퓨터: 꺼진 발생기는 다시 켜고 · 망가져 무너진 중력이 돌아올 때 '바닥 쪽을 잡으세요'를 알린다 — 들은 이는 덜 넘어진다 (주컴퓨터를 끈 배와 견준다) ──
        {
            var clay = Worlds.Ascii(Worlds.MindShipComputer.Replace("#.........#....a....a.r.#", "#....G....#....a....a.r.#"), legend);
            (int falls, bool restarted, string log, float g) Run(bool computer, ulong seed)
            {
                var s = Worlds.Full(clay, seed); var net = s.Get<Network>(); var m = s.Get<Mind>(); var sm = s.Get<ShipMind>(); var f = s.Get<Field>();
                var gen = net.Devices.First(d => d.Type == "GravityGenerator");
                if (!computer) net.SetOn(net.Devices.First(d => d.Type == "MainComputer").Id, false);   // 주컴퓨터를 끈 배 (망가뜨리면 합선이 같은 회로의 발생기까지 끊는다)
                var crew = Enumerable.Range(0, 8).Select(i => m.AddPerson(s, Worlds.At(clay, 12 + i, i % 2 == 0 ? 2 : 4))).ToArray();
                s.Run(3);
                long f0 = s.Facts.Total, t0 = s.Tick;
                net.SetOn(gen.Id, false);
                s.Run(Time.Minutes(0.5));
                bool restarted = net.On[gen.Id] != 0;
                if (!computer) net.SetOn(gen.Id, true);
                s.Run(Time.Minutes(1));
                long f1 = s.Facts.Total;
                net.Broken[gen.Id * 4 + (int)Part.Electric] = 1;   // 이번에는 망가진다 — 다시 켤 수 없다 · 45초 뒤 고쳐진다 (두 배 같은 때)
                s.Run(Time.Minutes(0.75));
                net.Repair(gen.Id, Part.Electric);
                s.Run(Time.Minutes(1.5));
                if (Debug) Console.WriteLine($"    {(computer ? "주컴퓨터" : "끈 배")} {seed}: 내려앉음 {m.Landings} · 방송 듣고 잡음 {m.Braced} · 넘어짐 {m.LandFalls}");
                int falls = s.Facts.Since(f1).Count(x => x.Kind == FactKind.Fell && crew.Contains(x.Actor));
                return (falls, restarted, string.Join(" / ", sm.Log.Where(l => l.tick >= t0).Select(l => l.text).Where(l => l.Contains("중력"))), f.Grav[Worlds.At(clay, 15, 2)]);
            }
            // 넘어짐은 운이 낀다 — 시드 셋을 더해 견준다 (8시드: 주컴퓨터 10 · 멈춘 배 32)
            var on = Run(true, 1); var off = Run(false, 1);
            for (ulong sd = 2; sd <= 3; sd++) { var a2 = Run(true, sd); var b2 = Run(false, sd); on.falls += a2.falls; off.falls += b2.falls; on.restarted &= a2.restarted; on.g = Math.Min(on.g, a2.g); }
            if (Debug) Console.WriteLine($"    주컴퓨터: {on.log}");
            Check("주컴퓨터: 꺼진 발생기는 곧 다시 켜고 · 무너질 때 '손잡이를 잡으세요' · 돌아올 때 '바닥 쪽을 잡으세요' — 들은 이는 반도 안 넘어진다 (주컴퓨터를 끈 배보다)",
                on.restarted && on.log.Contains("다시 켰다") && on.log.Contains("손잡이를 잡으세요") && on.log.Contains("바닥 쪽을 잡으세요") && on.g == 1 && on.falls * 2 < off.falls,
                $"다시 켬 {on.restarted} · 넘어짐: 주컴퓨터 {on.falls} · 끈 배 {off.falls} (8명 × 시드 셋 · 망가졌다 고쳐진 뒤) · \"{on.log}\"");
        }

        // ── (다) 무중력의 물: 퍼지지 않고 · 막을 넘는 몫은 방울로 떠 떠돌다 벽 · 설비에 들러붙고 · 중력이 서면 그 자리에 떨어진다 (1g면 바닥을 따라 퍼진다) ──
        {
            (int wet, double peak, double stuck, double atSpot, double fell, int newWet, double left) Run(bool zeroG)
            {
                var s = Worlds.Full(lay, 3); var f = s.Get<Field>(); var net = s.Get<Network>();
                var gen = net.Devices.First(d => d.Type == "GravityGenerator");
                int w = f.T.sWater, spot = At(17, 3);
                s.Run(3);
                if (zeroG) { net.SetOn(gen.Id, false); s.Run(15); }
                f.AddStore(spot, w, 1500);   // 27L — 물관이 터졌다
                double peak = 0;
                for (int k = 0; k < Time.Minutes(10); k++)
                {
                    s.Step(); peak = Math.Max(peak, f.Drops.Sum());
                    string? shot = Environment.GetEnvironmentVariable("GRAV_SHOT");   // 그림으로 보기: GRAV_SHOT=폴더
                    if (shot != null && zeroG && k == Time.Minutes(2)) { var img = new ShipView.Painter(lay).Frame(s, 10, 0, 15, 7, k); Png.WriteRgba(Path.Combine(shot, "drops.png"), img.W, img.H, img.Px); }
                }
                var wetBefore = Enumerable.Range(0, f.Cells).Where(i => f.Stored(w, i) > 1).ToHashSet();
                double atSpot = f.Stored(w, spot), stuck = f.DropsStuck;
                if (zeroG) { net.SetOn(gen.Id, true); s.Run(Time.Minutes(1)); }
                int newWet = Enumerable.Range(0, f.Cells).Count(i => f.Stored(w, i) > 1 && !wetBefore.Contains(i));
                if (Debug) Console.WriteLine($"    {(zeroG ? "무중력" : "1g")}: 젖은 칸 {wetBefore.Count} · 가장 많이 뜬 방울 {peak:0} · 들러붙음 {stuck:0} · 쏟은 칸 {atSpot:0} · 떨어짐 {f.DropsFell:0} · 새로 젖은 칸 {newWet} · 남은 방울 {f.Drops.Sum():0.0}");
                return (wetBefore.Count, peak, stuck, atSpot, f.DropsFell, newWet, f.Drops.Sum());
            }
            var one = Run(false); var zero = Run(true);
            Check("무중력의 물: 쏟아진 물은 퍼지지 않고 막을 넘는 몫이 방울로 떠 · 떠돌다 벽 · 설비에 들러붙고 · 중력이 서면 그 자리에 떨어진다 (1g면 바닥을 따라 퍼지고 방울은 없다)",
                one.peak == 0 && one.wet >= 9 && zero.peak > 400 && zero.stuck > 100 && zero.atSpot < 300 && zero.fell > 50 && zero.newWet > 0 && zero.left < 1,
                $"1g: 젖은 칸 {one.wet} · 방울 {one.peak:0} / 무중력: 가장 많이 뜬 방울 {zero.peak:0}몰 · 들러붙음 {zero.stuck:0} · 쏟은 칸에 남음 {zero.atSpot:0} · 중력이 서자 떨어짐 {zero.fell:0} (새로 젖은 칸 {zero.newWet}) · 남은 방울 {zero.left:0.0}");
        }

        // ── (다) 떠다니던 물방울이 전선에 들러붙어 합선 (원인 '물방울') · 우주로 트인 구멍은 방울을 빨아낸다 ──
        {
            var s = Worlds.Full(lay, 4); var f = s.Get<Field>(); var net = s.Get<Network>();
            var gen = net.Devices.First(d => d.Type == "GravityGenerator");
            s.Run(3);
            net.SetOn(gen.Id, false); s.Run(15);
            var path = net.Feeders.SelectMany(fd => fd.Path).ToHashSet();
            int spill = Enumerable.Range(0, f.Cells).Where(i => f.L.Kind[i] == CellKind.Floor && f.RegionOf[i] >= 0 && !path.Contains(i) && f.L.FurnitureAt[i] < 0)
                .OrderBy(i => path.Min(p => Math.Abs(f.L.X(p) - f.L.X(i)) + Math.Abs(f.L.Y(p) - f.L.Y(i))) == 2 ? 0 : 1).ThenBy(i => i).First();   // 전선에서 두 칸 떨어진 바닥
            long f0 = s.Facts.Total;
            f.AddStore(spill, f.T.sWater, 1200);
            s.Run(Time.Minutes(15));
            var sh = s.Facts.Since(f0).FirstOrDefault(x => x.Kind == FactKind.Short);
            string why = sh.Kind == FactKind.Short ? s.Causes.Story(sh.Cause) : "";

            var s2 = Worlds.Full(lay, 4); var f2 = s2.Get<Field>(); var net2 = s2.Get<Network>();
            s2.Run(3);
            net2.SetOn(net2.Devices.First(d => d.Type == "GravityGenerator").Id, false); s2.Run(15);
            f2.AddStore(At(21, 3), f2.T.sWater, 1500);
            s2.Run(Time.Minutes(3));
            double floatAtHole = f2.Drops.Sum();
            f2.SetHole(At(24, 3), 0.02f);   // 외판에 200cm² 구멍 (돌풍이 방울을 끌고 나간다)
            for (int k = 0; k < Time.Minutes(3); k++) { s2.Step(); if (Debug && k % 10 == 0 && k < 40) Console.WriteLine($"      {k} 기압 {f2.Pressure(At(21, 3)):0} 바람 {f2.WindX[At(23, 3)]:0.00},{f2.WindX[At(18, 3)]:0.00} 방울 {f2.Drops.Sum():0} 빠짐 {f2.DropsVented:0}"); }
            if (Debug) Console.WriteLine($"    구멍 날 때 떠 있던 방울 {floatAtHole:0}");
            if (Debug) Console.WriteLine($"    합선: {why} · 구멍: 빠진 방울 {f2.DropsVented:0}몰 · 남은 방울 {f2.Drops.Sum():0}");
            Check("떠다니던 물방울이 전선이 지나는 칸에 들러붙어 합선한다 (원인 '물방울') · 우주로 트인 구멍은 떠 있는 방울을 빨아낸다",
                why.Contains("물방울") && f2.DropsVented > 50, $"\"{why}\" · 구멍으로 빠진 방울 {f2.DropsVented:0}몰");
        }

        // ── (다) 그을음: 무중력에서는 가라앉는 몫이 없어 오래 떠 있다 (한 시간 뒤 떠 있는 그을음 — 1g보다 많다) ──
        {
            double Airborne(bool zeroG)
            {
                var s = Worlds.Full(lay, 5); var f = s.Get<Field>(); var net = s.Get<Network>();
                s.Run(3);
                if (zeroG) { net.SetOn(net.Devices.First(d => d.Type == "GravityGenerator").Id, false); s.Run(15); }
                var room = f.L.Rooms[f.L.Room[At(17, 3)]].Cells;
                foreach (int c in room) f.AddGas(c, f.T.gSoot, 0.05, f.Temp(c));
                double a0 = room.Sum(c => f.Gas(f.T.gSoot, c));
                s.Run(Time.Hours(1));
                return f.Total(f.T.gSoot) / a0;
            }
            double one = Airborne(false), zero = Airborne(true);
            Check("그을음: 무중력에서는 오래 떠 있다 — 한 시간 뒤 떠 있는 몫이 1g보다 많다", zero > one * 1.2 && zero < 1, $"한 시간 뒤 떠 있는 몫: 1g {one:0.00} · 무중력 {zero:0.00}");
        }

        // ── (라) 무중력의 불: 부력이 방 공기를 끌어들이지 않아 불꽃은 제 둘레 공기만 먹고 사그라든다 (1g보다 훨씬 적게 탄다) · 공조 바람이 닿는 자리는 계속 탄다 ──
        {
            (double kj, int burning) Fire(int mode)   // 0: 1g · 1: 무중력 · 2: 무중력 + 공조 바람
            {
                var s = Worlds.WithChem(lay, 6); var f = s.Get<Field>(); var ch = s.Get<Chemistry>();
                int seat = At(20, 4);
                if (mode > 0) Array.Fill(f.Grav, 0f);
                s.Run(3);
                ch.Ignite(seat, 3e6, s.Causes.Add(CauseKind.Story, "의자 밑에서 불씨가 튀었다", s.Tick));
                double kj = 0;
                Span<int> nb = stackalloc int[4];
                for (int k = 0; k < Time.Minutes(15); k++)
                {
                    if (mode == 2) { f.Airflow[seat] = 0.5f; f.L.Neighbors(seat, nb); foreach (int j in nb) if (j >= 0) f.Airflow[j] = 0.25f; }   // 의자 곁 통풍구
                    s.Step();
                    kj += ch.Hrr.Sum() * Time.SecondsPerTick;
                    if (Debug && mode == 1 && k % 75 == 0) { var fa = ch.FeedAir(seat); Console.WriteLine($"      {k / 25}분 불 {ch.Hrr.Sum():0}kW · 칸 산소 {ch.O2Frac(seat):0.000} · 먹는 공기 {fa.x:0.000}"); }
                }
                if (Debug) Console.WriteLine($"    불 {(mode == 0 ? "1g" : mode == 1 ? "무중력" : "무중력 + 공조")}: 탄 열 {kj / 1000:0}MJ · 타는 칸 {ch.BurningCount}");
                return (kj, ch.BurningCount);
            }
            var g1 = Fire(0); var g0 = Fire(1); var fan = Fire(2);
            Check("무중력의 불: 불꽃이 제 둘레 공기만 먹어 15분 동안 1g의 절반도 못 타고 스스로 꺼진다 · 공조 바람이 닿는 자리는 바람 없는 곳보다 훨씬 많이 탄다",
                g0.kj < g1.kj * 0.5 && g0.burning == 0 && fan.kj > g0.kj * 1.5,
                $"탄 열: 1g {g1.kj / 1000:0}MJ · 무중력 {g0.kj / 1000:0}MJ (남은 불 {g0.burning}칸) · 무중력 + 공조 바람 {fan.kj / 1000:0}MJ");
        }

        // ── (라) 무중력의 숨: 통풍구 바람이 닿지 않는 침대에서 자면 날숨이 얼굴 앞에 고여 머리가 아파 깬다 (원인 사슬이 무너진 중력에 닿는다) · 공조 바람이 닿는 침대 · 1g는 푹 잔다 ──
        {
            bool atVent = false; int staleWakes = 0;
            (bool woke, float face, string why) Sleep(int mode)   // 0: 1g · 1: 무중력 (방에 통풍구 하나) · 2: 무중력 + 침대에 공조 바람
            {
                var s = Worlds.Full(lay, 7); var f = s.Get<Field>(); var net = s.Get<Network>(); var m = s.Get<Mind>(); var life = s.Get<Life>();
                int bed = s.Layout.Furniture.First(fu => fu.Type == "Bed").Cells[0];
                int p = m.AddPerson(s, bed);
                life[m[p].C.Body].Asleep = 1; m[p].C.Sleepy = 1;
                s.Run(2);
                long f0 = s.Facts.Total;
                if (mode > 0) net.SetOn(net.Devices.First(d => d.Type == "GravityGenerator").Id, false);
                float face = 0;
                int vent = At(7, 2);   // 방의 통풍구 (공조 바람이 닿는 칸 — 이 시험 배에는 덕트가 없어 손으로 둔다)
                for (int k = 0; k < Time.Hours(2); k++)
                {
                    if (mode == 2) f.Airflow[m[p].C.Cell] = 0.5f;
                    if (mode == 1) f.Airflow[vent] = 0.5f;
                    s.Step();
                    face = Math.Max(face, life[m[p].C.Body].FaceCO2);
                    if (Debug && mode == 1 && k % 300 == 0) { ref var bd = ref life[m[p].C.Body]; Console.WriteLine($"      {k / 25}분 얼굴 {bd.FaceCO2:0.00} 잠 {bd.Asleep} 칸 {m[p].C.Cell} (침대 {bed} · 통풍구 {vent}) · {m.Thought(p)}"); }
                }
                var wakes = s.Facts.Since(f0).Where(x => x.Kind == FactKind.Woke && x.Actor == p && s.Causes.Story(x.Cause).Contains("날숨")).ToList();
                var w = wakes.FirstOrDefault();
                string why = w.Kind == FactKind.Woke ? s.Causes.Story(w.Cause) : "";
                atVent = m[p].C.Cell == vent && life[m[p].C.Body].Asleep != 0; staleWakes = wakes.Count;
                if (Debug && mode == 1) Console.WriteLine("      날숨에 깸: " + string.Join(" · ", wakes.Select(x => $"{x.Tick}틱 칸 {x.Cell}")));
                if (Debug) Console.WriteLine($"    잠 {(mode == 0 ? "1g" : mode == 1 ? "무중력" : "무중력 + 공조")}: 깸 {w.Kind == FactKind.Woke} · 얼굴 앞 {face:0.00}kPa · 아픔 {life[m[p].C.Body].Pain:0.00} · \"{why}\" · {m.Thought(p)}");
                return (w.Kind == FactKind.Woke, face, why);
            }
            var one = Sleep(0); var zero = Sleep(1); bool zeroAtVent = atVent; int zeroWakes = staleWakes; var fan = Sleep(2);
            Check("무중력의 숨: 통풍구 바람이 닿지 않는 침대에서는 날숨이 얼굴 앞에 고여 머리가 아파 깨고 (원인: 무너진 중력) · 다시는 거기서 자지 않고 통풍구 앞에 침낭을 묶고 잔다 · 침대에 공조 바람이 닿으면 · 1g면 그대로 잔다",
                zero.woke && zero.why.Contains("날숨") && zero.why.Contains("중력") && zero.face > 1 && zeroAtVent && zeroWakes == 1 && !fan.woke && fan.face < 0.3 && !one.woke && one.face == 0,
                $"무중력: 깸 {zeroWakes}번 · 얼굴 앞 {zero.face:0.0}kPa · \"{zero.why}\" · 두 시간 뒤 통풍구 앞에서 잠 {zeroAtVent} / 공조 바람: 깸 {fan.woke} · {fan.face:0.00}kPa / 1g: 깸 {one.woke}");
        }

        // ── 배가 갈라지면: 발생기가 있는 쪽은 1g · 떨어져 나간 조각은 무중력 (사실 · '발생기가 없는 조각') ──
        {
            var dl = Worlds.Ascii(Dumbbell.Replace("#.......#####.......#\n#....e..#", "#..G....#####.......#\n#....e..#"), legend);
            var s = Worlds.Full(dl, 3); var f = s.Get<Field>(); var st = s.Get<Structure>();
            s.Run(Time.Minutes(1));
            long f0 = s.Facts.Total;
            int rock = s.Causes.Add(CauseKind.Story, "커다란 운석이 통로를 꿰뚫었다", s.Tick);
            for (int y = 2; y <= 4; y++) st.Destroy(s, Worlds.At(dl, 10, y), rock);
            s.Run(Time.Minutes(1));
            float left = f.Grav[Worlds.At(dl, 3, 2)], right = f.Grav[Worlds.At(dl, 16, 2)];
            var lost = s.Facts.Since(f0).FirstOrDefault(x => x.Kind == FactKind.Gravity && x.Size == 0);
            string why = lost.Kind == FactKind.Gravity ? s.Causes.Story(lost.Cause) : "";
            if (Debug) Console.WriteLine($"    몸 {st.Bodies.Count} · 왼쪽 몸 {st.BodyOf(Worlds.At(dl, 3, 2))} · 오른쪽 몸 {st.BodyOf(Worlds.At(dl, 16, 2))}");
            Check("배가 갈라지면: 발생기가 있는 쪽은 1g · 떨어져 나간 조각은 무중력 (사실 '중력이 사라진다' — 발생기가 없는 조각)",
                st.Bodies.Count >= 2 && left == 1 && right == 0 && why.Contains("조각에는") && why.Contains("운석"),
                $"몸 {st.Bodies.Count} · 발생기 쪽 {left:0.00}g · 조각 {right:0.00}g · \"{why}\"");
        }
    }
}
