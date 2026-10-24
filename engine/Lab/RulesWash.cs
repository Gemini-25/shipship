using ShipEngine;

namespace Lab;

/// <summary>
/// 화장실 · 샤워실 (함장 요청 — docs/WASH.md): 몸은 차오르고 더러워지고 · 한계를 넘으면 지린다 (월드) /
/// 마려우면 변기로 · 차 있으면 줄 · 망가졌으면 다른 변기 · 더러우면 샤워 · 변기 뒤엔 손 (판단).
/// </summary>
public static partial class Program
{
    // 침실(침대 e · 배전반 p · 전지 b) — 문 — 화장실: 변기 T 둘 · 샤워 칸 S · 세면대 k
    private const string WashShip = @"
##################
#e.....#T......T.#
#......D.........#
#pb....#.S....k..#
##################";

    private static void WashRules()
    {
        WashViewRules();   // 5단 A1 '화장실 그림' (RulesWashView.cs — 묶음은 따로)
        if (!Do("화장실")) return;
        Console.WriteLine("\n[화장실 · 샤워실 — 비우기 · 줄 · 다른 변기 · 지림 · 씻기 · 손]");
        var legend = new Dictionary<char, string> { ['e'] = "Bed", ['T'] = "Toilet", ['S'] = "Shower", ['k'] = "Sink", ['p'] = "PowerPanel", ['b'] = "Battery" };
        var lay = Worlds.Ascii(WashShip, legend);
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";

        // 다른 욕구는 잠재우고 빈 시간에 둔다
        void Calm(Sim s, Mind m, int p)
        {
            int off = ((int)Time.HourOfDay(s.Tick) + 24 - 10) % 24;
            ref var c = ref m[p].C; c.ShiftHour = off; c.Sleepy = 0; c.Hunger = 0; c.Thirst = 0; c.Lonely = 0; c.Unfit = 0; c.Bored = 0;
        }

        // ── 비우기: 방광이 반 넘게 찬 이가 침실에서 가장 가까운 변기로 가서 그 위에서 비운다 — 오줌은 오수로 (물 재생기가 되돌린다 · 몰까지 같다) ──
        {
            var s = Worlds.Full(lay, 3);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>();
            int p = m.AddPerson(s, Worlds.At(lay, 2, 1));
            s.Run(2);
            life[m[p].C.Body].Bladder = Life.BladderMax * 0.7;
            double before = life[m[p].C.Body].Bladder, waste0 = net.WasteWater;
            int near = s.Layout.Furniture.First(f => f.Type == "Toilet").Cells[0];
            bool onSeat = false; long at = -1;
            for (long t = 0; t < Time.Minutes(20) && at < 0; t++)
            {
                Calm(s, m, p); s.Step();
                if (m[p].C.Goal == GoalKind.Toilet && m[p].C.Cell == near) onSeat = true;
                if (m.Toilets > 0) at = t;
            }
            double moved = net.WasteWater - waste0;
            Check("화장실: 방광이 반 넘게 찬 이가 가장 가까운 변기로 가서 그 위에서 비운다 — 오줌은 오수로 (몰까지)",
                at >= 0 && onSeat && life[m[p].C.Body].Bladder < 1 && moved >= before - 0.5 && moved <= before + 3 && life.Soilings == 0,
                $"비움 {(at < 0 ? "없음" : $"{Time.ToSeconds(at) / 60:0.0}분")} · 변기 위 {onSeat} · 방광 {before:0.0} → {life[m[p].C.Body].Bladder:0.0}몰 · 오수 +{moved:0.0}몰 · 지림 {life.Soilings}");
        }

        // ── 변기가 없는 배: 용변 봉투 (그 자리에서) — 지리지 않는다 ──
        {
            var bare = Worlds.Ascii(WashShip.Replace('T', '.').Replace('S', '.').Replace('k', '.'), legend);
            var s = Worlds.Full(bare, 3);
            var m = s.Get<Mind>(); var life = s.Get<Life>();
            int p = m.AddPerson(s, Worlds.At(bare, 2, 1));
            s.Run(2);
            life[m[p].C.Body].Bladder = Life.BladderMax * 0.8;
            for (long t = 0; t < Time.Minutes(15) && m.Bags == 0; t++) { Calm(s, m, p); s.Step(); }
            Check("변기가 없는 배: 마려운 이는 그 자리에서 용변 봉투를 쓴다 — 지리지 않는다",
                m.Bags >= 1 && life.Soilings == 0 && life[m[p].C.Body].Bladder < 1, $"봉투 {m.Bags} · 지림 {life.Soilings} · 방광 {life[m[p].C.Body].Bladder:0.0}몰");
        }

        // ── 줄 서기: 마려운 둘 · 쓸 수 있는 변기 하나 (다른 하나는 망가졌다) — 하나가 쓰는 동안 다른 이는 곁에서 기다렸다가 들어간다 (함께 앉지 않는다) ──
        {
            var s = Worlds.Full(lay, 5);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>();
            int p1 = m.AddPerson(s, Worlds.At(lay, 2, 1)), p2 = m.AddPerson(s, Worlds.At(lay, 3, 3));
            s.Run(2);
            var toilets = s.Layout.Furniture.Where(f => f.Type == "Toilet").ToList();
            int far = net.Devices.First(d => d.Furniture == toilets[1].Id).Id;
            net.Broken[far * 4 + (int)Part.Mechanical] = 1;   // 먼 변기는 흡입 팬이 망가졌다
            life[m[p1].C.Body].Bladder = Life.BladderMax * 0.75; life[m[p2].C.Body].Bowel = Life.BowelMax * 0.75;
            int seat = toilets[0].Cells[0]; bool together = false;
            for (long t = 0; t < Time.Minutes(30) && m.Toilets < 2; t++)
            {
                Calm(s, m, p1); Calm(s, m, p2); s.Step();
                together |= m[p1].C.Cell == seat && m[p2].C.Cell == seat;
            }
            Check("줄 서기: 쓸 변기가 하나뿐이면 (다른 하나는 팬이 망가졌다) 하나가 쓰는 동안 다른 이는 곁에서 기다렸다가 들어간다 — 함께 앉지 않고 · 망가진 변기는 쓰지 않는다",
                m.Toilets == 2 && m.Queued >= 1 && !together && life.Soilings == 0,
                $"비움 {m.Toilets} · 줄 {m.Queued} · 함께 앉음 {together} · 지림 {life.Soilings}");
        }

        // ── 참지 못함 (월드): 방광이 한계를 넘으면 그 자리 바닥이 젖고 몸이 더러워진다 (원인 마디 · 사실) → 지린 이는 씻으러 간다 ──
        {
            var s = Worlds.Full(lay, 7);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var f = s.Get<Field>();
            int p = m.AddPerson(s, Worlds.At(lay, 2, 1));
            s.Run(2);
            int cell = m[p].C.Cell;
            double FloorWater() { double w = 0; for (int i = 0; i < s.Layout.N; i++) w += f.Stored(f.T.sWater, i); return w; }
            double floor0 = FloorWater();
            life[m[p].C.Body].Bladder = Life.BladderMax + 1;
            for (int k = 0; k < 30 && life.Soilings == 0; k++) { Calm(s, m, p); s.Step(); }   // 몸은 몇 틱마다 셈한다
            if (Debug) Console.WriteLine($"    방광 {life[m[p].C.Body].Bladder:0.0} · 지림 {life.Soilings} · 젖음 {life[m[p].C.Body].Wet} · 몸 칸 {life[m[p].C.Body].Cell} · 사람 칸 {cell} · 바닥 물 {FloorWater() - floor0:0.0}");
            ref var b = ref life[m[p].C.Body];
            bool soiled = life.Soilings == 1 && b.Wet == 1 && b.Dirt >= 0.3f && FloorWater() - floor0 >= Life.BladderMax * 0.9 && s.Causes.Text(b.SoilCause).Contains("참지 못하고");
            bool fact = s.Facts.Since(0).Any(x => x.Kind == FactKind.Soiled && x.Actor == m[p].C.Body);
            for (long t = 0; t < Time.Minutes(30) && life[m[p].C.Body].Wet != 0; t++) { Calm(s, m, p); s.Step(); }
            Check("참지 못함: 방광이 한계를 넘으면 그 자리 바닥이 젖고 몸이 더러워진다 (원인 '참지 못하고 …' · 사실) → 지린 이는 샤워 칸으로 씻으러 간다",
                soiled && fact && life[m[p].C.Body].Wet == 0 && m.ShowersTaken >= 1,
                $"지림 {soiled} · 사실 {fact} · 씻음 {m.ShowersTaken} · 젖은 옷 {life[m[p].C.Body].Wet} · 더러움 {life[m[p].C.Body].Dirt:0.00}");
        }

        // ── 샤워: 더러운 이 (빈 시간) — 샤워 칸에서 6분 · 물 8L가 물탱크에서 나가 오수로 (김은 공기로) · 온수기를 켰다 끈다 · 더러움이 씻긴다 ──
        {
            var s = Worlds.Full(lay, 9);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>(); var f = s.Get<Field>();
            int p = m.AddPerson(s, Worlds.At(lay, 2, 1));
            s.Run(2);
            life[m[p].C.Body].Dirt = 0.8f;
            var shower = s.Layout.Furniture.First(x => x.Type == "Shower");
            int dev = net.Devices.First(d => d.Furniture == shower.Id).Id;
            double tank0 = net.WaterTank, waste0 = net.WasteWater, h2o0 = f.Ppm(f.T.gH2O, shower.Cells[0]);
            bool heater = false; double h2oPeak = 0;
            for (long t = 0; t < Time.Minutes(30) && m.ShowersTaken == 0; t++)
            {
                Calm(s, m, p); s.Step();
                heater |= net.On[dev] != 0;
                h2oPeak = Math.Max(h2oPeak, f.Ppm(f.T.gH2O, shower.Cells[0]));
            }
            double used = tank0 - net.WaterTank, waste = net.WasteWater - waste0;
            Check("샤워: 더러운 이가 빈 시간에 샤워 칸에서 씻는다 — 물 8L가 물탱크에서 나가 오수로 (조금은 김으로 공기에) · 온수기를 켰다 끈다 · 더러움이 씻긴다",
                m.ShowersTaken == 1 && life[m[p].C.Body].Dirt < 0.1f && used >= Mind.ShowerWater * 0.9 && waste >= used * 0.9 && heater && net.On[dev] == 0 && h2oPeak > h2o0,
                $"샤워 {m.ShowersTaken} · 더러움 0.80 → {life[m[p].C.Body].Dirt:0.00} · 물탱크 −{used:0}몰 · 오수 +{waste:0}몰 · 온수기 켬 {heater} · 끔 {net.On[dev] == 0} · 습기 {h2o0:0} → {h2oPeak:0}ppm");
        }

        // ── 손 씻기: 성실한 이는 변기 뒤에 거의 늘 · 덜 성실한 이는 가끔 (세 번 볼일) ──
        {
            int Washes(float consc)
            {
                var s = Worlds.Full(lay, 11);
                var m = s.Get<Mind>(); var life = s.Get<Life>();
                int p = m.AddPerson(s, Worlds.At(lay, 2, 1));
                m.SetPersonality(p, 0.5f, consc, 0.5f, 0.5f, 0.3f);
                s.Run(2);
                for (int k = 0; k < 4; k++)
                {
                    life[m[p].C.Body].Bladder = Life.BladderMax * 0.75;
                    long n0 = m.Toilets;
                    for (long t = 0; t < Time.Minutes(20) && m.Toilets == n0; t++) { Calm(s, m, p); s.Step(); }
                    for (long t = 0; t < Time.Minutes(3); t++) { Calm(s, m, p); s.Step(); }
                }
                return (int)m.HandWashes;
            }
            int hi = Washes(0.95f), lo = Washes(0.05f);
            Check("손 씻기: 성실한 이는 변기 뒤에 거의 늘 손을 씻고 · 덜 성실한 이는 가끔 (네 번 볼일)", hi >= 3 && lo < hi, $"성실 0.95 → {hi}번 · 0.05 → {lo}번");
        }
    }
}
