using ShipEngine;

namespace Lab;

/// <summary>
/// 화장실 (다) 주컴퓨터 · 로봇 (docs/WASH.md): 변기 조절기가 막힘 · 오물 통 · 물 내린 수를 알린다 → '사용 금지' 불 · 손질 일감 (로봇 먼저) /
/// 주컴퓨터가 멈추면 모르고 써서 넘친다 → 다음 이는 보고 다른 변기로 / 샤워 배급은 물 배급과 함께 (성실할수록 지킨다) / 줄이 길면 빈 곳 · 차례 안내.
/// </summary>
public static partial class Program
{
    // 침실(침대 e · 로봇 독 r · 배전반 p · 전지 b · 주컴퓨터 c) — 화장실 가(변기 T 둘 · 샤워 칸 S · 세면대 k · 퇴비통 o) — 긴 통로 — 화장실 나(변기 T 하나)
    private static readonly string WashCareShip = string.Join("\n", new[]
    {
        "",
        new string('#', 64),
        "#e....r#T..T.......#" + new string('.', 32) + "#T.........#",
        "#......D...........D" + new string('.', 32) + "D..........#",
        "#pbc...#.S.....k.o.#" + new string('.', 32) + "#..........#",
        new string('#', 64),
    });

    private static void WashCareRules()
    {
        if (!Do("화장실 일")) return;
        Console.WriteLine("\n[화장실 (다) — 사용 금지 · 뚫기 · 넘침 · 통 비우기 · 닦기 · 샤워 배급 · 줄 안내]");
        var legend = new Dictionary<char, string> { ['e'] = "Bed", ['r'] = "RobotDock", ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer",
            ['T'] = "Toilet", ['S'] = "Shower", ['k'] = "Sink", ['o'] = "Composter" };
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var lay = Worlds.Ascii(WashCareShip, legend);
        int At(Layout l, int x, int y) => Worlds.At(l, x, y);
        int[] Toilets(Layout l) => l.Furniture.Where(f => f.Type == "Toilet").OrderBy(f => l.X(f.Cells[0])).Select(f => f.Id).ToArray();

        void Calm(Sim s, Mind m, int p, bool drink = false)
        {
            int off = ((int)Time.HourOfDay(s.Tick) + 24 - 10) % 24;
            ref var c = ref m[p].C; c.ShiftHour = off; c.Sleepy = 0; c.Hunger = 0; if (!drink) c.Thirst = 0; c.Lonely = 0; c.Unfit = 0; c.Bored = 0;
        }
        double WaterNear(Sim s, int cell)
        {
            var f = s.Get<Field>(); double w = 0;
            int x0 = s.Layout.X(cell), y0 = s.Layout.Y(cell);
            for (int y = y0 - 1; y <= y0 + 1; y++) for (int x = x0 - 1; x <= x0 + 1; x++) if (s.Layout.In(x, y)) w += f.Stored(f.T.sWater, s.Layout.Index(x, y));
            return w;
        }
        string Said(Sim s, long from = 0) => string.Join(" / ", s.Get<ShipMind>().Log.Where(l => l.tick >= from).Select(l => l.text));

        // ── 막힘: 조절기가 알린 막힌 변기에 주컴퓨터가 '사용 금지' 불을 켜고 방송 → 마려운 이는 그 변기를 고르지 않고 옆 변기로 · 로봇이 뚫으면 불을 끈다 ──
        {
            var s = Worlds.Full(lay, 3);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var sm = s.Get<ShipMind>();
            int r = m.AddRobot(s, At(lay, 6, 1), AgentKind.Robot, At(lay, 6, 1));
            int p = m.AddPerson(s, At(lay, 2, 1));
            s.Run(30);
            var t = Toilets(lay); int near = t[0], next = t[1];
            life.Clog(near, s.Causes.Add(CauseKind.Setup, "시험: 변기가 막혔다", s.Tick, lay.Furniture[near].Cells[0]));
            life[m[p].C.Body].Bladder = Life.BladderMax * 0.75;
            bool signed = false; long unclogAt = -1;
            for (long k = 0; k < Time.Minutes(40) && (unclogAt < 0 || m.Toilets == 0); k++)
            {
                Calm(s, m, p); s.Step();
                signed |= m.WashSign[near] != 0;
                if (unclogAt < 0 && life.Clogged[near] == 0) unclogAt = k;
            }
            s.Run(ShipMind.WashEvery * 2);
            if (Debug) Console.WriteLine($"    {Said(s)}");
            Check("막힘: 조절기가 알린 막힌 변기에 주컴퓨터가 '사용 금지' 불을 켜고 방송한다 → 마려운 이는 옆 변기로 · 로봇이 뚫으면 (걸린 대변은 통으로) 불을 끈다",
                signed && sm.ToiletSigns == 1 && life.Flushes[near] == 0 && life.Flushes[next] >= 1 && m.Unclogged == 1 && unclogAt >= 0 && m.WashSign[near] == 0 && life.Overflows == 0 && Said(s).Contains("막혔습니다"),
                $"불 {signed} · 막힌 변기 쓴 수 {life.Flushes[near]} · 옆 변기 {life.Flushes[next]} · 뚫음 {m.Unclogged} ({(unclogAt < 0 ? "없음" : $"{Time.ToSeconds(unclogAt) / 60:0}분")}) · 지금 불 {m.WashSign[near]} · 넘침 {life.Overflows}");
        }

        // ── 주컴퓨터가 없으면 (멈추면) 모른다: 막힌 변기에 불이 켜지지 않아 모르고 쓰고 넘친다 (바닥 물 · 지린내 · 원인 '넘쳤다' ← '막혔다') →
        //    다음 이는 와 보고 다른 변기로 (막히면 다른 길) ──
        {
            var one = Worlds.Ascii(WashCareShip.Replace("#e....r#T..T", "#e....r#T...").Replace("#pbc...", "#pb...."), legend);   // 화장실 가에 변기 하나 · 주컴퓨터가 없다 (멈춘 것과 같다 — 망가진 주컴퓨터는 사람이 고치러 간다)
            var s = Worlds.Full(one, 5);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>(); var f = s.Get<Field>();
            int p1 = m.AddPerson(s, At(one, 2, 1)), p2 = m.AddPerson(s, At(one, 2, 2));
            s.Run(30);
            var t = Toilets(one); int near = t[0], far = t[1];
            int cell = one.Furniture[near].Cells[0];
            life.Clog(near, s.Causes.Add(CauseKind.Setup, "시험: 변기가 막혔다", s.Tick, cell));
            double w0 = WaterNear(s, cell);
            life[m[p1].C.Body].Bladder = Life.BladderMax * 0.75;
            for (long k = 0; k < Time.Minutes(15) && life.Overflows == 0; k++) { Calm(s, m, p1); Calm(s, m, p2); s.Step(); }
            double spilled = WaterNear(s, cell) - w0;
            var fact = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Overflowed);
            var chain = fact.Kind == FactKind.Overflowed ? s.Causes.Chain(fact.Cause).Select(c => s.Causes.Text(c)).ToList() : new List<string>();
            double nh3 = f.Ppm(f.T.gNH3, cell);
            life[m[p2].C.Body].Bladder = Life.BladderMax * 0.7;
            for (long k = 0; k < Time.Minutes(25) && life.Flushes[far] == 0; k++) { Calm(s, m, p1); Calm(s, m, p2); s.Step(); }
            if (Debug) Console.WriteLine($"    사슬 {string.Join(" ← ", chain)} · {m[p2].LastFail}");
            Check("주컴퓨터가 없으면 (멈추면) 모른다: 막힌 변기에 불이 켜지지 않아 모르고 쓰고 넘친다 (바닥 물 · 지린내 · 원인 '넘쳤다' ← '막혔다' · 사실) → 다음 이는 와 보고 먼 화장실로",
                life.Overflows == 1 && spilled >= Life.BladderMax * 0.6 && nh3 > 0 && chain.Any(x => x.Contains("넘쳤다")) && chain.Any(x => x.Contains("막혔다")) && life.Grime[near] >= 0.99f
                && m.WashSign[near] == 0 && m.WashTurnedAway >= 1 && life.Flushes[far] >= 1 && life.Flushes[near] == 1,
                $"넘침 {life.Overflows} · 바닥 물 +{spilled:0}몰 · 암모니아 {nh3:0.0}ppm · 사슬 {chain.Count} · 때 {life.Grime[near]:0.00} · 돌아섬 {m.WashTurnedAway} · 먼 변기 {life.Flushes[far]} · 넘친 변기 쓴 수 {life.Flushes[near]}");
        }

        // ── 오물 통: 80% 넘게 차면 주컴퓨터가 비우기를 맡긴다 → 로봇이 꽉 찬 통을 들고 퇴비통에 쏟는다 (질량 그대로) ──
        {
            var s = Worlds.Full(lay, 7);
            var m = s.Get<Mind>(); var life = s.Get<Life>();
            int r = m.AddRobot(s, At(lay, 6, 1), AgentKind.Robot, At(lay, 6, 1));
            s.Run(30);
            int near = Toilets(lay)[0];
            int bin = lay.Furniture.First(x => x.Type == "Composter").Id;
            double kg = Life.TankMax * 0.85;
            life.Tank[near] = kg;
            byte ext = (byte)Array.IndexOf(m.ItemNames, "소화기");
            m[r].C.Holding = ext; m[r].C.HoldCharge = 300;   // 불을 끄고 남은 소화기를 든 로봇 (찾아 고친 결함 — 손이 비어야 계획을 짜서 맡은 손질을 끝내 못 했다)
            bool held = false;
            for (long k = 0; k < Time.Minutes(40) && m.Emptied == 0; k++) { s.Step(); held |= m[r].C.Holding == m.iWaste; }
            bool setDown = Worlds.Items(s).Any(x => x.Item == ext && Math.Abs(x.Charge - 300) < 1e-3 && Math.Abs(lay.X(x.Cell) - lay.X(lay.Furniture[near].Cells[0])) + Math.Abs(lay.Y(x.Cell) - lay.Y(lay.Furniture[near].Cells[0])) <= 2);
            Check("오물 통: 80% 넘게 차면 주컴퓨터가 비우기를 맡긴다 → 로봇이 꽉 찬 통을 들고 (빈 통으로 바꿔 끼우고) 퇴비통에 쏟는다 — 질량 그대로 · 들고 있던 소화기는 변기 곁에 내려놓는다",
                m.Emptied == 1 && held && setDown && life.Tank[near] < 1e-9 && Math.Abs(life.Heap[bin] - kg) < 1e-6 && m[r].C.Holding == 255 && m.WashSign[near] == 0,
                $"비움 {m.Emptied} · 통을 든 적 {held} · 소화기 내려놓음 {setDown} · 통 {life.Tank[near]:0.00}kg · 퇴비통 {life.Heap[bin]:0.00}/{kg:0.00}kg · 손 {m[r].C.Holding}");
        }

        // ── 통을 든 로봇이 멈추면 (전지가 다했다): 통은 그 자리에 떨어지고 그 자리를 주컴퓨터에 알린다 → 다른 로봇이 주워 퇴비통에 쏟는다 — 질량 · 물건 장부 그대로 (찾아 고친 결함 — 떨군 통은 영영 남았다) ──
        {
            var s = Worlds.Full(lay, 17);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>();
            int r1 = m.AddRobot(s, At(lay, 6, 1), AgentKind.Robot, At(lay, 6, 1)), r2 = m.AddRobot(s, At(lay, 5, 1), AgentKind.Robot, At(lay, 6, 1));
            s.Run(30);
            int near = Toilets(lay)[0];
            int bin = lay.Furniture.First(x => x.Type == "Composter").Id;
            double kg = Life.TankMax * 0.85;
            life.Tank[near] = kg;
            int carrier = -1;
            for (long k = 0; k < Time.Minutes(30) && carrier < 0; k++) { s.Step(); foreach (int r in new[] { r1, r2 }) if (m[r].C.Holding == m.iWaste) carrier = r; }
            if (carrier >= 0) net.Robot(m[carrier].C.Mech).Battery = 0;   // 통을 든 채 멈춘다
            bool dropped = false;
            for (long k = 0; k < Time.Minutes(60) && m.Emptied == 0; k++) { s.Step(); dropped |= m.WasteDown.Count > 0; }
            double ledger = Enumerable.Range(0, (int)Flow.Count).Sum(f => s.Book.OfItem(m.iWaste, (Flow)f));   // 떼어 낸 통 +1 · 비운 통 −1
            Check("통을 든 로봇이 멈추면 통은 그 자리에 떨어지고 그 자리를 알린다 → 다른 로봇이 주워 퇴비통에 쏟는다 — 질량 · 물건 장부 그대로",
                carrier >= 0 && dropped && m.Emptied == 1 && Math.Abs(life.Heap[bin] - kg) < 1e-9 && life.WasteCarried < 1e-9 && !Worlds.Items(s).Any(x => x.Item == m.iWaste) && m.WasteDown.Count == 0 && Math.Abs(ledger) < 1e-9,
                $"든 로봇 {carrier} · 알림 {dropped} · 비움 {m.Emptied} · 퇴비통 {life.Heap[bin]:0.000}/{kg:0.000}kg · 옮기는 중 {life.WasteCarried:0.000} · 바닥 통 {Worlds.Items(s).Count(x => x.Item == m.iWaste)} · 장부 {ledger}");
        }

        // ── 통이 가득 차서 막힌 변기: 뚫어도 내려갈 데가 없다 — 주컴퓨터는 비우기부터 맡기고 그다음 뚫기 → 불이 꺼진다 (찾아 고친 결함 — 뚫기만 되풀이했다) ──
        {
            var s = Worlds.Full(lay, 19);
            var m = s.Get<Mind>(); var life = s.Get<Life>();
            int r = m.AddRobot(s, At(lay, 6, 1), AgentKind.Robot, At(lay, 6, 1));
            s.Run(30);
            int near = Toilets(lay)[0];
            life.Tank[near] = Life.TankMax - 0.2; life.Bowl[near] = 0.4;
            life.Clog(near, s.Causes.Add(CauseKind.Setup, "시험: 오물 통이 가득 차 내려가지 않았다", s.Tick, lay.Furniture[near].Cells[0]));
            bool signed = false;
            for (long k = 0; k < Time.Minutes(90) && (life.Clogged[near] != 0 || m.WashSign[near] != 0 || !signed); k++) { s.Step(); signed |= m.WashSign[near] != 0; }
            Check("통이 가득 차서 막힌 변기: 주컴퓨터는 비우기부터 맡기고 그다음 뚫기 → 걸린 대변은 빈 통으로 · 불이 꺼진다",
                signed && m.Emptied == 1 && m.Unclogged == 1 && life.Clogged[near] == 0 && m.WashSign[near] == 0 && Math.Abs(life.Tank[near] - 0.4) < 1e-9,
                $"불 {signed} · 비움 {m.Emptied} · 뚫음 {m.Unclogged} · 막힘 {life.Clogged[near]} · 지금 불 {m.WashSign[near]} · 통 {life.Tank[near]:0.00}kg");
        }

        // ── 전력을 줄이려 온수기를 꺼도 (주컴퓨터의 부하 덜기) 물 밸브는 그대로 — 찬물이 흘러 씻긴다 (찾아 고친 결함 — 물까지 멈춰 '씻었다'만 남았다) ──
        {
            var s = Worlds.Full(lay, 21);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>();
            int p = m.AddPerson(s, At(lay, 2, 1));
            s.Run(30);
            int dev = net.Devices.First(d => d.Type == "Shower").Id;
            life[m[p].C.Body].Dirt = 0.8f;
            bool cut = false; double meter0 = 0;
            for (long k = 0; k < Time.Minutes(30) && m.ShowersTaken == 0; k++)
            {
                Calm(s, m, p); s.Step();
                if (!cut && net.ValveOpen(dev) && net.On[dev] != 0) { net.SetOn(dev, false); cut = true; meter0 = net.ShowerMeter[dev]; }   // 온수기 전원만 내린다
            }
            double ran = net.ShowerMeter[dev] - meter0;
            Check("전력을 줄이려 온수기를 꺼도 물 밸브는 그대로 — 찬물이 흘러 씻긴다 (밸브와 온수기는 따로)",
                cut && m.ShowersTaken == 1 && ran > Network.ShowerFlow * 200 && life[m[p].C.Body].Dirt < 0.2f && !net.ValveOpen(dev),
                $"온수기 끔 {cut} · 샤워 {m.ShowersTaken} · 끈 뒤 흐른 물 {ran:0}몰 · 더러움 {life[m[p].C.Body].Dirt:0.00} · 밸브 {net.ValveOpen(dev)}");
        }

        // ── 닦기: 조절기의 물 내린 수가 지난번 닦은 뒤로 12번이면 (안은 못 본다) 로봇이 닦는다 — 때가 없어지고 둘레에 고인 물도 오수로 ──
        {
            var s = Worlds.Full(lay, 9);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var f = s.Get<Field>(); var net = s.Get<Network>();
            int r = m.AddRobot(s, At(lay, 6, 1), AgentKind.Robot, At(lay, 6, 1));
            s.Run(30);
            int near = Toilets(lay)[0], cell = lay.Furniture[near].Cells[0];
            life.Flushes[near] = 12; life.Grime[near] = 0.5f;
            f.AddStore(cell, f.T.sWater, 40);
            double waste0 = net.WasteWater;
            for (long k = 0; k < Time.Minutes(40) && m.Scrubbed == 0; k++) s.Step();
            Check("닦기: 물 내린 수가 지난번 닦은 뒤로 12번이면 (안은 못 본다 — 쓴 수로) 로봇이 변기를 닦는다 — 때가 없어지고 둘레에 고인 물은 오수로",
                m.Scrubbed == 1 && life.Grime[near] == 0 && life.ScrubbedAt[near] == 12 && WaterNear(s, cell) < 1 && net.WasteWater - waste0 >= 39,
                $"닦음 {m.Scrubbed} · 때 {life.Grime[near]:0.00} · 닦은 때의 수 {life.ScrubbedAt[near]} · 고인 물 {WaterNear(s, cell):0.0}몰 · 오수 +{net.WasteWater - waste0:0}몰");
        }

        // ── 샤워 배급: 물 배급을 열면 '샤워는 3분 · 이틀에 한 번'도 — 성실한 이는 3분(물 4L)만 쓰고 이틀은 다시 씻지 않는다 (지리면 씻는다) ·
        //    성실하지 않은 이는 6분을 다 쓰기도 한다 ──
        {
            var s = Worlds.Full(lay, 11);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var sm = s.Get<ShipMind>(); var net = s.Get<Network>();
            sm.Policy.RationBelow = 1e9f;   // 시험: 장부가 여섯 시간 쌓이면 곧바로 물 배급 (보통으로 마시면 '며칠'이 무엇이든)
            int keeper = m.AddPerson(s, At(lay, 2, 1)), b1 = m.AddPerson(s, At(lay, 3, 1)), b2 = m.AddPerson(s, At(lay, 4, 1));
            m.SetPersonality(keeper, 0.5f, 0.95f, 0.5f, 0.5f, 0.3f); m.SetPersonality(b1, 0.5f, 0f, 0.5f, 0.5f, 0.3f); m.SetPersonality(b2, 0.5f, 0f, 0.5f, 0.5f, 0.3f);
            int w = m.AddPerson(s, At(lay, 3, 2));   // 여섯 시간 안에 한 번 씻는 이 (장부에 물 쓴 것이 남게)
            var ps = new[] { keeper, b1, b2, w };
            life[m[w].C.Body].Dirt = 0.8f;
            for (long k = 0; k < Time.Hours(9) && sm.RationWaterOn == 0; k++) { foreach (int p in ps) { Calm(s, m, p, drink: true); if (p != w) life[m[p].C.Body].Dirt = 0; } s.Step(); }
            bool rule = m.ShowerRuleMin == ShipMind.ShowerRuleMin && Said(s).Contains("샤워는 3분");
            // 성실한 이가 먼저 (혼자)
            life[m[keeper].C.Body].Dirt = 0.8f;
            double tank0 = net.WaterTank; long n0 = m.ShowersTaken;
            for (long k = 0; k < Time.Minutes(30) && m.ShowersTaken == n0; k++) { foreach (int p in ps) Calm(s, m, p); s.Step(); }
            double used = tank0 - net.WaterTank;
            long showered = m[keeper].C.ShoweredAt;
            // 덜 성실한 둘
            foreach (int p in new[] { b1, b2 }) life[m[p].C.Body].Dirt = 0.8f;
            for (long k = 0; k < Time.Minutes(60) && m.ShowersTaken < n0 + 3; k++) { foreach (int p in ps) Calm(s, m, p); s.Step(); }
            // 성실한 이: 다시 더러워져도 이틀 안에는 씻지 않는다 — 지리면 씻는다
            life[m[keeper].C.Body].Dirt = 0.8f;
            for (long k = 0; k < Time.Hours(2); k++) { foreach (int p in ps) Calm(s, m, p); s.Step(); }
            bool waited = m[keeper].C.ShoweredAt == showered;
            life[m[keeper].C.Body].Wet = 1;
            for (long k = 0; k < Time.Minutes(30) && m[keeper].C.ShoweredAt == showered; k++) { foreach (int p in ps) Calm(s, m, p); s.Step(); }
            Check("샤워 배급: 물 배급을 열면 '샤워는 3분 · 이틀에 한 번'도 — 성실한 이는 3분(물 4L)만 쓰고 이틀 안엔 다시 씻지 않는다 (지리면 씻는다) · 덜 성실한 이는 6분을 다 쓰기도",
                rule && used > Mind.ShowerWater * 0.4 && used < Mind.ShowerWater * 0.6 && waited && m[keeper].C.ShoweredAt != showered && m.ShowerOverRule >= 1,
                $"배급 {sm.RationWaterOn} · 규칙 {m.ShowerRuleMin}분 · 성실한 이의 물 {used:0}/{Mind.ShowerWater:0}몰 · 이틀 기다림 {waited} · 지리고 씻음 {m[keeper].C.ShoweredAt != showered} · 넘겨 쓴 번 {m.ShowerOverRule}");
        }

        // ── 씻다 말고 나가면: 성실한 이는 물을 잠그고 나간다 · 틀어 둔 샤워는 물이 계속 흐른다 (물탱크 → 오수 — 열은 물과 함께 배수구로) ·
        //    주컴퓨터가 빈 샤워 칸을 2분 넘게 보면 잠근다 (원인 '잠갔다' ← '틀어 둔 채' · 틀어 둔 이는 조심이 오른다) — 칸이 달아오르지 않는다 (찾아 고친 결함) ──
        {
            var s = Worlds.Full(lay, 15);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>(); var sm = s.Get<ShipMind>(); var f = s.Get<Field>();
            int p = m.AddPerson(s, At(lay, 2, 1));
            m.SetPersonality(p, 0.5f, 0.95f, 0.5f, 0.5f, 0.3f);
            s.Run(30);
            var shower = lay.Furniture.First(x => x.Type == "Shower");
            int dev = net.Devices.First(d => d.Furniture == shower.Id).Id, cell = shower.Cells[0];
            life[m[p].C.Body].Dirt = 0.8f;
            long showering = 0;
            for (long k = 0; k < Time.Minutes(20) && showering < 25; k++)
            {
                Calm(s, m, p); s.Step();
                if (m[p].C.Goal == GoalKind.Wash && m[p].C.StepIndex < m[p].Plan.Count && m[p].Plan[m[p].C.StepIndex].Kind == StepKind.Use && (UseKind)m[p].Plan[m[p].C.StepIndex].Arg == UseKind.Shower) showering++;
            }
            bool wasOn = net.ValveOpen(dev);
            m[p].Plan.Clear(); life[m[p].C.Body].Dirt = 0; life[m[p].C.Body].Wet = 0;   // 씻다 말고 나간다 (다시 씻을 까닭은 없다)
            for (int k = 0; k < 3; k++) { Calm(s, m, p); s.Step(); }
            bool closed = !net.ValveOpen(dev) && net.On[dev] == 0 && m.ShowersLeftOn == 0;
            // 틀어 둔 샤워 (그 사람이 깜빡했다고 친다)
            float careful0 = m[p].C.Careful;
            net.SetValve(dev, true); net.SetOn(dev, true);
            net.OnCause[dev] = s.Causes.Add(CauseKind.Person, "시험: 샤워 물을 틀어 둔 채 나갔다", s.Tick, cell, 0, 0, p);
            life[m[p].C.Body].Bladder = Life.BladderMax * 0.75;   // 샤워 칸에 서 있던 이가 변기로 간다 (칸이 빈다)
            double tank0 = net.WaterTank, waste0 = net.WasteWater, hot = 0; long cutAt = -1;
            for (long k = 0; k < Time.Minutes(8) && cutAt < 0; k++) { Calm(s, m, p); s.Step(); hot = Math.Max(hot, f.Temp(cell) - 273.15); if (!net.ValveOpen(dev)) cutAt = k; }
            for (int k = 0; k < ShipMind.WashEvery * 2; k++) { Calm(s, m, p); s.Step(); }
            double ran = tank0 - net.WaterTank;
            var cut = s.Facts.Since(0).LastOrDefault(x => x.Kind == FactKind.Switched);
            var chain = cut.Kind == FactKind.Switched ? s.Causes.Chain(cut.Cause).Select(c => s.Causes.Text(c)).ToList() : new List<string>();
            Check("씻다 말고 나가면: 성실한 이는 물을 잠그고 나간다 · 틀어 둔 샤워는 물이 흐르고 (물탱크 → 오수) · 주컴퓨터가 빈 샤워 칸을 2분 넘게 보면 잠근다 (원인 '잠갔다' ← '틀어 둔 채' · 틀어 둔 이는 조심이 오른다) · 칸이 달아오르지 않는다",
                showering >= 25 && wasOn && closed && sm.ShowersCut == 1 && cutAt > 0 && cutAt < Time.Minutes(5) && ran > Network.ShowerFlow * 60 && net.WasteWater - waste0 >= ran * 0.95
                && chain.Any(x => x.Contains("잠갔다")) && chain.Any(x => x.Contains("틀어 둔")) && m[p].C.Careful > careful0 && hot < 35,
                $"쓰던 중 {showering}틱 · 나가며 잠금 {closed} · 주컴퓨터가 잠금 {sm.ShowersCut} ({(cutAt < 0 ? "없음" : $"{Time.ToSeconds(cutAt) / 60:0.0}분")}) · 흐른 물 {ran:0}몰 · 오수 +{net.WasteWater - waste0:0}몰 · 사슬 {chain.Count} · 조심 {careful0:0.00} → {m[p].C.Careful:0.00} · 가장 더운 {hot:0.0}℃");
        }

        // ── 물이 나오지 않는 샤워 (물관이 끊겼다 — 씻는 이는 틀어 보고야 안다): 씻은 것으로 치지 않고 그 샤워 칸은 한 시간 피한다 → 제 침대에서 물수건 · 갈아입기 (찾아 고친 결함) ──
        {
            var s = ShipWorld.Crewed("Hwasal", 3, 1, 0);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>();
            int p = m.Agents.First(x => x.IsPerson).Id;
            int dev = net.Devices.First(d => d.Type == "Shower").Id;
            // 다른 방에서 끊긴 물관 (보이지 않는 곳 · 그 방 밸브를 잠갔다 — DF9: 끊긴 관은 분에 65L를 뿜어 물탱크가 몇 분에 비고 배급이 선다): 샤워에 물이 안 오는 가장 가까운 칸
            byte wt = Network.Bit(LineKind.Water);
            int showerRoom = s.Layout.Room[net.Devices[dev].Cell];
            foreach (int cut in Enumerable.Range(0, s.Layout.N).Where(c => (net.LineAt[c] & wt) != 0 && s.Layout.Room[c] >= 0 && s.Layout.Room[c] != showerRoom)
                .OrderBy(c => Math.Abs(s.Layout.X(c) - s.Layout.X(net.Devices[dev].Cell)) + Math.Abs(s.Layout.Y(c) - s.Layout.Y(net.Devices[dev].Cell))).Take(40))
            {
                var t = s.Clone(); var tn = t.Get<Network>();
                tn.CutLines(t, cut, wt, 0); t.Run(2);
                if (tn.WaterAtDevice(dev)) continue;
                net.CutLines(s, cut, wt, s.Causes.Add(CauseKind.Setup, "시험: 샤워로 가는 물관이 다른 방에서 끊겼다", s.Tick, cut));
                net.SetValve(s.Layout.Room[cut], LineKind.Water, true);   // 그 방 물 밸브를 잠갔다 (물탱크가 비지 않게 — 배급 없이 샤워를 틀어 보게)
                break;
            }
            s.Run(2);
            bool dry = !net.WaterAtDevice(dev);
            life[m[p].C.Body].Dirt = 0.8f; life[m[p].C.Body].Wet = 1;
            bool knew = false;
            for (long k = 0; k < Time.Minutes(40) && (m.Wipes == 0 || life[m[p].C.Body].Wet != 0); k++) { Calm(s, m, p); s.Step(); knew |= m[p].LastFail.Contains("물이 나오지 않는다"); }
            Check("물이 나오지 않는 샤워 (물관이 끊겼다): 틀어 보고 안다 — 씻은 것으로 치지 않고 그 샤워 칸은 한 시간 피한다 → 제 침대에서 물수건으로 닦고 갈아입는다",
                dry && knew && m.ShowersTaken == 0 && m.Wipes >= 1 && life[m[p].C.Body].Wet == 0,
                $"물관 끊김 {dry} · 알아챔 {knew} · 샤워 {m.ShowersTaken} · 물수건 {m.Wipes} · 젖은 옷 {life[m[p].C.Body].Wet}");
        }

        // ── 줄 안내: 변기 하나인 화장실에 넷이 몰리면 — 다른 화장실에 빈 변기가 있으면 뒤에 온 이를 그리로 보내고 (그 사람은 옮겨 간다) ·
        //    없으면 온 차례를 알린다 (차례를 들은 이는 더 오래 기다린다 — 아무도 돌아서지 않고 지리지 않는다) ──
        {
            (long calls, long orders, long moved, int farUsed, long gaveUp, long soiled, string said) Run(bool farToilet)
            {
                string art = WashCareShip.Replace("#e....r#T..T", "#e....r#T...");
                if (!farToilet) art = art.Replace("#T.........#", "#..........#");
                var l = Worlds.Ascii(art, legend);
                var s = Worlds.Full(l, 13);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var sm = s.Get<ShipMind>();
                var ps = new[] { m.AddPerson(s, At(l, 2, 1)), m.AddPerson(s, At(l, 3, 1)), m.AddPerson(s, At(l, 2, 2)), m.AddPerson(s, At(l, 3, 2)) };
                s.Run(30);
                foreach (int p in ps) life[m[p].C.Body].Bladder = Life.BladderMax * 0.8;
                var t = Toilets(l);
                long n0 = m.Toilets;
                for (long k = 0; k < Time.Minutes(30) && m.Toilets < n0 + 4; k++) { foreach (int p in ps) Calm(s, m, p); s.Step(); }
                if (Debug) Console.WriteLine($"    변기 {string.Join(",", t.Select(x => $"{x}@{l.X(l.Furniture[x].Cells[0])}:{life.Flushes[x]}"))} · 옮김 {m.WashMoved}");
                return (sm.WashCalls, sm.WashOrders, m.WashMoved, farToilet ? life.Flushes[t[1]] : 0, m.QueueGaveUp, life.Soilings, Said(s));
            }
            var a = Run(true); var b = Run(false);
            if (Debug) Console.WriteLine($"    {a.said}\n    {b.said}");
            Check("줄 안내: 변기 하나인 화장실에 넷이 몰리면 다른 화장실의 빈 변기로 뒤에 온 이를 보낸다 (옮겨 가 거기서 본다) · 빈 곳이 없으면 차례를 알린다 (아무도 돌아서지 않고 지리지 않는다)",
                a.calls >= 1 && a.moved >= 1 && a.farUsed >= 1 && a.said.Contains("비어 있는 곳으로") && b.orders >= 1 && b.calls == 0 && b.said.Contains("차례를 알립니다") && b.gaveUp == 0 && b.soiled == 0,
                $"빈 곳 안내 {a.calls} · 옮긴 이 {a.moved} · 먼 변기 {a.farUsed} / 차례 안내 {b.orders} · 돌아섬 {b.gaveUp} · 지림 {b.soiled}");
        }
    }
}
