using ShipEngine;

namespace Lab;

/// <summary>
/// ⑫ 자연 사건 (docs/ENGINE.md ⑫ — 이야기꾼 대신 · 함장 결정): 위험도 = 항로만 · 미리 알기 = 센서로 잡힌 것만 · 배 안의 일은 세상 법칙에서.
/// 기능만 확인한다 — 빈도 · 크기의 정밀한 조절과 확인은 엔진 개편을 다 마친 뒤 맨 끝에.
/// </summary>
public static partial class Program
{
    private static void NatureRules()
    {
        if (!Do("자연 사건")) return;
        Console.WriteLine("\n[⑫ 자연 사건 — 항로 · 센서]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var legend = new Dictionary<char, string> { ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer", ['e'] = "Bed", ['R'] = "SensorArray" };
        var lay = Worlds.Ascii(WeatherShip, legend);

        // ── 항로: 같은 바위 띠(1.2AU)를 반나절 — 거친 항로(8)는 파편이 잦고 고요한 항로(1)는 드물다 · 그 항로만 지나는 구간(옛 정거장 파편 지대)은 그 항로에만 ·
        //    아침 방송이 지도로 아는 구간을 알린다 · 면을 바꾸는 항로는 출발 점화 Δv가 더 든다 (√(v₁² + v₂² − 2v₁v₂cos Δi)) ──
        {
            (int made, string morning, double dv) Run(int route, double au, bool voyage = true)
            {
                var s = Worlds.Full(lay, 21);
                var o = s.Get<Outside>(); var sm = s.Get<ShipMind>();
                o.Route = route; o.Distance = au;
                if (voyage) o.StartVoyage(s, 1.52);   // 항해 중 (전이 궤도 — 반나절엔 거리가 거의 그대로)
                s.Run(Time.Hours(12));
                double dv = Outside.HohmannDv(1.0, 1.52, o.Rt?.Incline ?? 0).dv1;
                return (o.RockSeq + o.CloudSeq, sm.Morning, dv);
            }
            var calmT = Task.Run(() => Run(1, 1.2)); var wildT = Task.Run(() => Run(8, 1.2));
            var staT = Task.Run(() => Run(8, 1.04)); var usualT = Task.Run(() => Run(4, 1.04, false));   // 깊은 우주 바탕은 항해 중에만 — 가만히 떠 있게
            var calm = calmT.Result; var wild = wildT.Result; var sta = staT.Result; var usual = usualT.Result;
            var (flat, _) = Outside.HohmannDv(1.0, 1.52);
            if (Debug) Console.WriteLine($"    아침 방송(8): {wild.morning}\n    (8 · 1.04AU): {sta.morning}");
            Check("항로: 같은 바위 띠를 반나절 — 거친 항로(8)는 파편이 잦고 고요한 항로(1)는 드물다 · 옛 정거장 파편 지대는 그 항로(8)에만 · 아침 방송이 지나는 구간을 알린다 · 면을 바꾸는 항로(1)는 출발 Δv가 더 든다",
                wild.made >= 2 && calm.made < wild.made && sta.made >= 2 && usual.made == 0 && wild.morning.Contains("작은 바위 띠") && sta.morning.Contains("옛 정거장")
                && calm.dv > flat + 100 && Math.Abs(wild.dv - flat) < 1,
                $"바위 띠 반나절: 항로 8 파편 {wild.made} · 항로 1 {calm.made} / 1.04AU: 항로 8 {sta.made} · 항로 4 {usual.made} / 출발 Δv: 항로 1 {calm.dv:0}m/s · 항로 8 {wild.dv:0} · 면 그대로 {flat:0}");
        }

        // ── 센서로 잡힌 것만: 12분 뒤 닿는 파편 폭풍 — 센서 배열이 돌면 6000km에서 잡혀(사실 '센서에 잡혔다') 닿기 전에 대피하고 ·
        //    센서 배열이 고장 나면 주컴퓨터는 구름을 모른다 (대피 없음 — 예전엔 센서 없이도 알았다) ──
        {
            (long sensed, long shelters, string log) Run(bool broken)
            {
                var s = Worlds.Full(lay, 22);
                var m = s.Get<Mind>(); var o = s.Get<Outside>(); var sm = s.Get<ShipMind>(); var net = s.Get<Network>();
                m.AddPerson(s, Worlds.At(lay, 3, 1));
                s.Run(5);
                if (broken) foreach (var d in net.Devices) if (d.Type == "SensorArray") net.Broken[d.Id * 4 + (int)Part.Electric] = 1;
                long f0 = s.Facts.Total, t0 = s.Tick;
                o.AddCloud("kessler", 10000 * 12 * 60 + 400_000, 400_000, 0, 1, 0, s.Causes.Add(CauseKind.Setup, "파편 폭풍이 항로로 들어온다", s.Tick));
                for (int k = 0; k < Time.Minutes(11); k++) s.Step();
                long sensed = s.Facts.Since(f0).Count(x => x.Kind == FactKind.Sensed && x.Other == (int)TrackKind.Cloud);
                return (sensed, sm.Shelters, string.Join(" / ", sm.Log.Where(l => l.tick >= t0).Select(l => l.text).Take(4)));
            }
            var blindT = Task.Run(() => Run(true)); var seeing = Run(false); var blind = blindT.Result;
            if (Debug) Console.WriteLine($"    {seeing.log}\n    고장: {blind.log}");
            Check("센서로 잡힌 것만: 12분 뒤 닿는 파편 폭풍 — 센서 배열이 돌면 잡혀(사실 '센서에 잡혔다') 닿기 전에 대피 · 센서 배열이 고장 나면 주컴퓨터는 구름을 모른다",
                seeing.sensed == 1 && seeing.shelters == 1 && blind.sensed == 0 && blind.shelters == 0,
                $"센서 돎: 잡힘 {seeing.sensed} · 대피 {seeing.shelters} / 고장: 잡힘 {blind.sensed} · 대피 {blind.shelters}");
        }

        // ── 별 폭발: X선 감시(센서 배열)가 조짐을 잡으면 폭풍 전에 대피 · 센서 배열이 고장 나면 조짐을 모르고 폭풍이 와서 선량계가 오르는 방사선을 잰 뒤에야 ──
        {
            (long beforeStorm, long after, bool flareStarted) Run(bool broken)
            {
                var s = Worlds.Full(lay, 23);
                var m = s.Get<Mind>(); var o = s.Get<Outside>(); var sm = s.Get<ShipMind>(); var net = s.Get<Network>();
                m.AddPerson(s, Worlds.At(lay, 3, 1));
                s.Run(5);
                if (broken) foreach (var d in net.Devices) if (d.Type == "SensorArray") net.Broken[d.Id * 4 + (int)Part.Electric] = 1;
                o.ScheduleFlare(1, s.Causes.Add(CauseKind.Setup, "별의 표면이 끓어오르기 시작했다", s.Tick));
                long before = 0;
                for (int k = 0; k < Time.Minutes(60); k++) { s.Step(); if (o.FlareStart < 0) before = sm.Shelters; }
                return (before, sm.Shelters, o.FlareStart >= 0 || o.Flares > 0);
            }
            var blindT = Task.Run(() => Run(true)); var seeing = Run(false); var blind = blindT.Result;
            Check("별 폭발: X선 감시가 조짐을 잡으면 폭풍 전에 대피 · 센서 배열이 고장 나면 조짐을 모르고 폭풍이 와서 선량계가 재고서야 대피",
                seeing.beforeStorm == 1 && blind.beforeStorm == 0 && blind.after == 1 && blind.flareStarted,
                $"센서 돎: 폭풍 전 대피 {seeing.beforeStorm} / 고장: 폭풍 전 {blind.beforeStorm} · 폭풍 뒤 {blind.after}");
        }

        NatureInsideRules();
        NatureHandsRules();
    }

    private const string CrateShip = @"
############
#e...F..L..#
#..........#
############";

    /// <summary>⑫ 자연 사건 (나) — 배 안의 일은 세상 법칙에서: 만들 때의 흠 · 차단기 접점 · 식량 상자의 감기 · 출항 때 잠복.</summary>
    private static void NatureInsideRules()
    {
        Console.WriteLine("\n[⑫ 자연 사건 (나) — 배 안]");

        // ── 만들 때의 흠: 설계한 배의 외판 몇 칸에 검사로 못 찾는 작은 흠 (0.2~4mm — 새지 않는다 · 같은 시드는 같은 흠) · 응력 주기마다 파리스 법칙으로 자란다 ──
        {
            var a = ShipWorld.Create("hwasal", 31); var b = ShipWorld.Create("hwasal", 31); var other = ShipWorld.Create("hwasal", 32);
            var st = a.Get<Structure>();
            var flaws = Enumerable.Range(0, a.Layout.N).Where(c => st.Crack[c] > 0).ToList();
            bool same = flaws.SequenceEqual(Enumerable.Range(0, a.Layout.N).Where(c => b.Get<Structure>().Crack[c] > 0)) && flaws.All(c => st.Crack[c] == b.Get<Structure>().Crack[c]);
            bool differs = !flaws.SequenceEqual(Enumerable.Range(0, a.Layout.N).Where(c => other.Get<Structure>().Crack[c] > 0));
            bool small = flaws.All(c => a.Layout.Hull[c] && st.Crack[c] <= 0.004f && a.Get<Field>().Hole[c] <= 0);
            int big = flaws.OrderByDescending(c => st.Crack[c]).FirstOrDefault(-1);
            float a0 = big >= 0 ? st.Crack[big] : 0;
            if (big >= 0) st.LoadCycle(a, big, 200, 3000);
            float a1 = big >= 0 ? st.Crack[big] : 0;
            string why = big >= 0 ? a.Causes.Story(st.CrackCause[big]) : "";
            Check("만들 때의 흠: 설계한 배의 외판 몇 칸에 검사로 못 찾는 작은 흠 (새지 않는다 · 같은 시드는 같은 흠 · 다른 시드는 다른 흠) · 응력 주기마다 자란다",
                flaws.Count > 0 && same && differs && small && a1 > a0 && why.Contains("흠"),
                $"흠 {flaws.Count}칸 · 같은 시드 같음 {same} · 다른 시드 다름 {differs} · 가장 큰 흠 {a0 * 1000:0.0}mm → 200MPa 3000번 뒤 {a1 * 1000:0.0}mm · \"{why}\"");
        }

        // ── 차단기 접점: 합선 전류를 끊는 아크가 접점을 녹이면(보이지 않는다) 다시 올리는 순간 눌어붙어 다음 합선에 떨어지지 않는다 — 아크가 계속된다 ──
        {
            var lay = Worlds.Ascii(NetShip, NetLegend);
            int left = lay.Room[Worlds.At(lay, 4, 2)];
            (bool tripped, bool weld, bool stuck, bool arced, string why) Run(double chance)
            {
                var s = Worlds.Full(lay, 41);
                var net = s.Get<Network>(); var f = s.Get<Field>();
                net.WeldChance = chance;
                s.Run(3);
                int fd = net.RoomFeeder[left], wet = net.Feeders[fd].Path[1];
                f.AddStore(wet, f.T.sWater, 60);
                s.Run(10);
                bool tripped = net.Breaker[fd] == 0, weld = net.BreakerWeld[fd] != 0;
                f.TakeStore(wet, f.T.sWater, f.Stored(f.T.sWater, wet));
                s.Run(3);
                net.ResetBreaker(fd);
                bool stuck = net.BreakerStuck[fd] != 0;
                f.AddStore(wet, f.T.sWater, 60);
                bool arced = false;
                for (int k = 0; k < 6; k++) { s.Step(); arced |= net.IsStuckFault(fd); }
                return (tripped, weld, stuck, arced, weld ? s.Causes.Story(net.BreakerWeld[fd]) : "");
            }
            var okT = Task.Run(() => Run(0)); var worn = Run(1); var ok = okT.Result;
            Check("차단기 접점: 합선 전류를 끊다 접점이 녹으면 다시 올리는 순간 눌어붙는다 — 다음 합선에 떨어지지 않고 아크가 계속된다 · 멀쩡한 접점은 다시 떨어진다",
                worn.tripped && worn.weld && worn.stuck && worn.arced && ok.tripped && !ok.weld && !ok.stuck && !ok.arced && worn.why.Contains("접점"),
                $"상한 접점: 처음 떨어짐 {worn.tripped} · 상함 {worn.weld} · 올리자 눌어붙음 {worn.stuck} · 다음 합선 아크 {worn.arced} / 멀쩡: 눌어붙음 {ok.stuck} · 아크 {ok.arced} · \"{worn.why}\"");
        }

        // ── 식량 상자의 감기: 사람이 식량 보관함에서 상자를 꺼내 열면 — 그 상자에 바이러스가 묻어 있었으면(시드에서) 연 이가 잠복에 든다 ──
        {
            var lay = Worlds.Ascii(CrateShip, new Dictionary<char, string> { ['e'] = "Bed", ['F'] = "Fridge", ['L'] = "FoodLocker" });
            (bool ill, long opened, string why) Run(double chance)
            {
                var s = Worlds.Full(lay, 42);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var f = s.Get<Field>();
                life.CrateColdChance = chance;
                int p = m.AddPerson(s, Worlds.At(lay, 3, 2));
                s.Run(5);
                var fridge = lay.Furniture.First(fu => fu.Type == "Fridge"); var locker = lay.Furniture.First(fu => fu.Type == "FoodLocker");
                foreach (int c in fridge.Cells) f.TakeStore(c, s.Tables.sFood, f.Stored(s.Tables.sFood, c));
                m[p].C.Sleepy = 0;
                m.AssignJob(p, GoalKind.Restock, fridge.Id, locker.Id, s.Tick + Time.Hours(2));   // 일감판처럼 나르기를 맡긴다 (사람은 일하는 시간에만 받는다 — 시험은 자정)
                for (int k = 0; k < Time.Hours(1) && life.CratesOpened == 0; k++) s.Step();
                ref var body = ref life[m[p].C.Body];
                return (body.Ill != 0, life.CratesOpened, body.Ill != 0 ? s.Causes.Story(body.IllCause) : "");
            }
            var cleanT = Task.Run(() => Run(0)); var dirty = Run(1); var clean = cleanT.Result;
            Check("식량 상자의 감기: 사람이 식량 상자를 꺼내 열면 — 묻어 있던 바이러스가 연 이에게 옮아 잠복에 든다 (원인: 그 상자) · 깨끗한 상자는 아무 일 없다",
                dirty.opened > 0 && dirty.ill && dirty.why.Contains("식량 상자") && clean.opened > 0 && !clean.ill,
                $"묻은 상자: 연 상자 {dirty.opened} · 잠복 {dirty.ill} · \"{dirty.why}\" / 깨끗한 상자: 연 상자 {clean.opened} · 잠복 {clean.ill}");
        }

        // ── 출항 때 잠복: 고향에서 옮은 감기를 잠복한 채 탄 이 (시드에서) — 항해에 나설 때 한 번 ──
        {
            var s = ShipWorld.Crewed("hwasal", 43, 3);
            var life = s.Get<Life>();
            life.BoardColdChance = 1;
            ShipWorld.Embark(s);
            var ill = Enumerable.Range(0, life.Bodies.Count).Where(i => life[i].Ill == 1).ToList();
            string why = ill.Count > 0 ? s.Causes.Story(life[ill[0]].IllCause) : "";
            var s2 = ShipWorld.Crewed("hwasal", 43, 3); s2.Get<Life>().BoardColdChance = 0; ShipWorld.Embark(s2);
            int none = Enumerable.Range(0, s2.Get<Life>().Bodies.Count).Count(i => s2.Get<Life>()[i].Ill != 0);
            Check("출항 때 잠복: 항해에 나설 때 고향에서 옮은 감기를 잠복한 채 탄 이가 있을 수 있다 (시드에서 · 한 사람)",
                ill.Count == 1 && why.Contains("잠복") && none == 0,
                $"잠복한 이 {ill.Count} · \"{why}\" / 몫 0: {none}");
        }
    }

    /// <summary>⑫ 자연 사건 (다) — 세상이 계산하는 사고: 손보다 다른 부품을 상하게 함(⑥) · 일하다 다침(⑦) → 알아챘는지 · 말할지는 그 사람이 (⑧).</summary>
    private static void NatureHandsRules()
    {
        Console.WriteLine("\n[⑫ 자연 사건 (다) — 손보다 상함 · 일하다 다침]");
        var lay = Worlds.Ascii(HearShip, HearLegend);
        (int handled, int confessed, int concealed, float blunt, string why, string hurtWhy) Run(double chance, double slip, float consc, float agree, float neuro)
        {
            var s = Worlds.Full(lay, 51);
            var m = s.Get<Mind>(); var n = s.Get<Network>(); var life = s.Get<Life>();
            n.HandledChance = chance; life.SlipChance = slip; life.ScaldChance = 0;
            int pump = n.Devices.First(d => d.Def.Role == "pump").Id;
            int p = m.AddPerson(s, Worlds.At(lay, 6, 4));
            s.Run(3);
            ref var c = ref m[p].C;
            c.SkRepair = 1; c.Consc = consc; c.Agree = agree; c.Neuro = neuro; c.Station = -1; c.Sleepy = 0; c.Stress = 0;
            long f0 = s.Facts.Total;
            m.AssignJob(p, GoalKind.Maintain, pump * 4 + (int)Part.Mechanical, 0, s.Tick + Time.Hours(2));   // 일감판처럼 미리 갈기를 맡긴다
            int done = -1;
            for (int k = 0; k < Time.Hours(1.5) && (done < 0 || k < done + 10); k++)
            {
                s.Step();
                m[p].C.Hunger = 0; m[p].C.Thirst = 0; m[p].C.Sleepy = 0;
                if (done < 0 && s.Facts.Since(f0).Any(x => x.Kind == FactKind.Repaired && x.Actor == p)) done = k;
            }
            var fs = s.Facts.Since(f0).ToList();
            var mis = fs.Where(x => x.Kind == FactKind.Mistake && x.Actor == p && x.Size == Network.HandledSize).ToList();
            var hurt = fs.FirstOrDefault(x => x.Kind == FactKind.Hurt && x.Actor == p);
            return (mis.Count, fs.Count(x => x.Kind == FactKind.Confessed && x.Actor == p), fs.Count(x => x.Kind == FactKind.Mistake && x.Actor == p && x.Size == 2),
                life[m[p].C.Body].Blunt, mis.Count > 0 ? s.Causes.Story(mis[0].Cause) : "", hurt.Kind == FactKind.Hurt ? s.Causes.Story(hurt.Cause) : "");
        }
        var honestT = Task.Run(() => Run(10, 0, 0.95f, 0.9f, 0.1f)); var closedT = Task.Run(() => Run(10, 0, 0.6f, 0f, 1f));   // 몫 10 — 솜씨가 좋아도 반드시
        var hurtT = Task.Run(() => Run(0, 1, 0.5f, 0.5f, 0.5f)); var calm = Run(0, 0, 0.5f, 0.5f, 0.5f);
        var honest = honestT.Result; var closed = closedT.Result; var hurt = hurtT.Result;
        if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") Console.WriteLine($"    {honest.why}\n    {hurt.hurtWhy}");
        Check("손보다 상함: 펌프를 미리 갈다 다른 부품을 상하게 했다 (세상이 솜씨 · 졸림 · 서두름으로 계산 — 사실 '실수') · 알아챈 정직한 이는 털어놓고 · 입이 무거운 이는 말하지 않는다 (그 사람의 판단)",
            honest.handled == 1 && honest.confessed == 1 && honest.concealed == 0 && closed.handled == 1 && closed.confessed == 0 && closed.concealed == 1 && calm.handled == 0 && honest.why.Contains("상하게"),
            $"정직한 이: 상함 {honest.handled} · 털어놓음 {honest.confessed} / 입이 무거운 이: 상함 {closed.handled} · 말하지 않음 {closed.concealed} / 몫 0: 상함 {calm.handled} · \"{honest.why}\"");
        Check("일하다 다침: 손보다 공구가 미끄러지면 세상(⑦)이 다치게 한다 (원인: 그 손봄) · 몫 0이면 다치지 않는다",
            hurt.blunt > 0.2f && hurt.hurtWhy.Contains("미끄러져") && calm.blunt < 0.01f,
            $"미끄러짐: 타박 {hurt.blunt:0.00} · \"{hurt.hurtWhy}\" / 몫 0: 타박 {calm.blunt:0.00}");
    }
}
