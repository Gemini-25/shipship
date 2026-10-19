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
        NatureMailRules();
        NaturePastRules();
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

        // ── 윗단 보호 장치의 접점: 눌어붙은 차단기 아래 아크를 20초 뒤 끊은 배전반 주 차단기도 같은 법칙으로 상할 수 있다 (보이지 않는다)
        //    상한 주 차단기는 다음엔 끊어 주지 않는다 — 그 뒤는 '눌어붙은 차단기의 합선 (윗단 보호도 낡았다)' 시험이 본다 (로봇이 손으로 끊는다) ──
        {
            (long cutAt, bool mainWorn) Run(double chance)
            {
                var s = ShipWorld.Create("Hwasal", 13);
                var f = s.Get<Field>(); var net = s.Get<Network>();
                net.WeldChance = chance;
                s.Run(Time.Minutes(2));
                int fd = Enumerable.Range(0, net.Feeders.Count).OrderByDescending(k => net.Feeders[k].Path.Length).First();
                var path = net.Feeders[fd].Path;
                net.BreakerStuck[fd] = 1;                           // 아래 차단기는 이미 눌어붙어 있다
                f.AddStore(path[path.Length / 2], f.T.sWater, 60);
                long cutAt = -1;
                for (int k = 0; k < Time.Minutes(1) && cutAt < 0; k++) { s.Step(); if (net.Breaker[fd] == 0) cutAt = k; }
                return (cutAt, net.MainStuck[net.Feeders[fd].Panel] != 0);
            }
            var okT = Task.Run(() => Run(0)); var worn = Run(1); var ok = okT.Result;
            Check("윗단 보호 장치의 접점: 눌어붙은 차단기 아래 아크를 끊은 배전반 주 차단기도 큰 전류에 접점이 상할 수 있다 (몫 1: 상함 · 몫 0: 멀쩡)",
                worn.cutAt >= 0 && worn.mainWorn && ok.cutAt >= 0 && !ok.mainWorn,
                $"몫 1: 끊음 {(worn.cutAt >= 0 ? $"{Time.ToSeconds(worn.cutAt):0}초" : "없음")} · 주 차단기 상함 {worn.mainWorn} / 몫 0: 끊음 {(ok.cutAt >= 0 ? $"{Time.ToSeconds(ok.cutAt):0}초" : "없음")} · 상함 {ok.mainWorn}");
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
        var hurtT = Task.Run(() => Run(0, 3, 0.5f, 0.5f, 0.5f));   // 솜씨 1이면 몫 × 0.4 — 3이면 반드시 (1이면 40%: 고친 틱의 난수에 따라 갈렸다)
        var calm = Run(0, 0, 0.5f, 0.5f, 0.5f);
        var honest = honestT.Result; var closed = closedT.Result; var hurt = hurtT.Result;
        if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") Console.WriteLine($"    {honest.why}\n    {hurt.hurtWhy}");
        Check("손보다 상함: 펌프를 미리 갈다 다른 부품을 상하게 했다 (세상이 솜씨 · 졸림 · 서두름으로 계산 — 사실 '실수') · 알아챈 정직한 이는 털어놓고 · 입이 무거운 이는 말하지 않는다 (그 사람의 판단)",
            honest.handled == 1 && honest.confessed == 1 && honest.concealed == 0 && closed.handled == 1 && closed.confessed == 0 && closed.concealed == 1 && calm.handled == 0 && honest.why.Contains("상하게"),
            $"정직한 이: 상함 {honest.handled} · 털어놓음 {honest.confessed} / 입이 무거운 이: 상함 {closed.handled} · 말하지 않음 {closed.concealed} / 몫 0: 상함 {calm.handled} · \"{honest.why}\"");
        Check("일하다 다침: 손보다 공구가 미끄러지면 세상(⑦)이 다치게 한다 (원인: 그 손봄) · 몫 0이면 다치지 않는다",
            hurt.blunt > 0.2f && hurt.hurtWhy.Contains("미끄러져") && calm.blunt < 0.01f,
            $"미끄러짐: 타박 {hurt.blunt:0.00} · \"{hurt.hurtWhy}\" / 몫 0: 타박 {calm.blunt:0.00}");
    }

    /// <summary>⑫ 자연 사건 (라) — 고향 편지 (통신 창 · 안테나 · 빛의 늦음 → 받은 이의 마음) · 교통량의 조난선.</summary>
    private static void NatureMailRules()
    {
        Console.WriteLine("\n[⑫ 자연 사건 (라) — 고향 편지 · 조난선]");
        var legend = new Dictionary<char, string> { ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer", ['e'] = "Bed", ['R'] = "SensorArray" };
        var lay = Worlds.Ascii(WeatherShip, legend);

        // ── 고향 편지: 저녁 통신 창에 안테나(센서 배열)가 돌면 사람마다 편지 — 소식은 시드에서 · 빛이 고향에서 오는 만큼 늦게 · 받은 이의 마음이 받아들인다 /
        //    안테나가 멈췄으면 편지는 고향에 쌓였다가 안테나가 돌아온 다음 창에 온다 · 항해에 나서지 않은 배(시험)는 고향과 잇지 않았다 ──
        {
            var s = Worlds.Full(lay, 61);
            var m = s.Get<Mind>(); var o = s.Get<Outside>(); var soc = s.Get<Society>(); var net = s.Get<Network>();
            var people = new[] { m.AddPerson(s, Worlds.At(lay, 3, 1)), m.AddPerson(s, Worlds.At(lay, 5, 2)), m.AddPerson(s, Worlds.At(lay, 25, 2)) };
            o.Route = 4; o.StartVoyage(s, 1.52); o.VoyageStart = s.Tick - Time.Days(100);   // 출항한 지 백 날 (고향이 멀어졌다)
            soc.LetterChance = 1; soc.MailHour = 0.1;
            var arrays = net.Devices.Where(d => d.Type == "SensorArray").Select(d => d.Id).ToList();
            foreach (int d in arrays) net.Broken[d * 4 + (int)Part.Electric] = 1;   // 안테나가 멈췄다
            s.Run(Time.Hours(0.15));
            long blocked = soc.Letters;
            foreach (int d in arrays) net.Repair(d, Part.Electric);
            soc.MailHour = 0.2;   // 다음 창
            var before = people.Select(p => (m[p].C.Joy, m[p].C.Sad)).ToArray();
            long f0 = s.Facts.Total;
            s.Run(Time.Hours(0.1));
            var letters = s.Facts.Since(f0).Where(x => x.Kind == FactKind.Letter).ToList();
            bool felt = letters.All(l => { int i = Array.IndexOf(people, l.Actor); var (j0, s0) = before[i]; return l.Size > 0 ? m[l.Actor].C.Joy > j0 : l.Size < 0 ? m[l.Actor].C.Sad > s0 : m[l.Actor].C.Joy >= j0; });
            double light = o.HomeLightMinutes(s.Tick);
            string one = letters.Count > 0 ? s.Causes.Text(letters[0].Cause) : "";
            var still = Worlds.Full(lay, 61); var sm = still.Get<Mind>(); sm.AddPerson(still, Worlds.At(lay, 3, 1));
            still.Get<Society>().LetterChance = 1; still.Get<Society>().MailHour = 0.1; still.Run(Time.Hours(0.2));
            if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") foreach (var l in letters) Console.WriteLine($"    {s.Causes.Text(l.Cause)}");
            Check("고향 편지: 저녁 통신 창에 안테나가 돌면 사람마다 편지 (소식은 시드에서 · 빛이 고향에서 오는 만큼 늦게) · 받은 이의 마음이 받아들인다 · 안테나가 멈춘 창엔 오지 않고 다음 창에 · 항해하지 않은 배는 고향과 잇지 않았다",
                blocked == 0 && letters.Count == 3 && felt && light > 1 && one.Contains("편지") && one.Contains("분") && still.Get<Society>().Letters == 0,
                $"안테나 멈춤: 편지 {blocked} / 돌아온 창: 편지 {letters.Count} · 마음이 움직임 {felt} · 고향까지 빛 {light:0.0}분 · \"{one}\" / 항해 전 배: {still.Get<Society>().Letters}");
        }

        // ── 조난선은 교통량대로: 지나는 배 중 몇은 조난선 — 붐비는 항로(8)는 고요한 항로(1)보다 자주 만난다 ──
        {
            (int boats, int distress) Run(int route)
            {
                var s = Worlds.Full(lay, 62);
                var o = s.Get<Outside>();
                o.Route = route; o.TrafficOn = true; o.Distance = 1.52 - 0.01;   // 행성 궤도 곁 (교통이 붐빈다)
                int distress = 0; var seen = new HashSet<int>();
                for (int k = 0; k < Time.Days(2); k++)
                {
                    s.Step();
                    foreach (var c in o.Contacts) if (seen.Add(c.Id) && c.Kind == ContactKind.Distress) distress++;
                }
                return (o.ContactSeq, distress);
            }
            var calmT = Task.Run(() => Run(1)); var busy = Run(8); var calm = calmT.Result;
            Check("조난선은 교통량대로: 붐비는 항로(8)는 지나는 배를 고요한 항로(1)보다 자주 만난다 (그중 몇은 조난선 — 갈지는 주컴퓨터 · 함장 방침)",
                busy.boats > calm.boats && busy.boats >= 2,
                $"이틀 · 행성 궤도 곁: 항로 8 배 {busy.boats} (조난선 {busy.distress}) · 항로 1 배 {calm.boats} (조난선 {calm.distress})");
        }
    }

    /// <summary>⑫ 자연 사건 (마) — 출항 과거사: 승무원을 태울 때 한 번 (시드에서) · 첫날부터 사람 판에 · 항해 중에는 아무것도 심지 않는다.</summary>
    private static void NaturePastRules()
    {
        Console.WriteLine("\n[⑫ 자연 사건 (마) — 출항 과거사]");
        var kinds = new int[5]; bool applied = true, same = true, shown = false; int ties = 0;
        string story = "";
        foreach (ulong seed in new ulong[] { 71, 72, 73 })
        {
            var s = ShipWorld.Crewed("hwasal", seed, 8);
            var so = s.Get<Society>(); var m = s.Get<Mind>();
            var again = ShipWorld.Crewed("hwasal", seed, 8).Get<Society>();
            same &= so.Past.Count == again.Past.Count && so.Past.Zip(again.Past).All(p => p.First.A == p.Second.A && p.First.B == p.Second.B && p.First.Kind == p.Second.Kind);
            int N = so.N;
            foreach (var t in so.Past)
            {
                kinds[t.Kind]++; ties++;
                applied &= t.Kind switch
                {
                    1 => so.Known[t.A * N + t.B] >= 0.4f,
                    2 => so.Bond[t.A * N + t.B] >= 0.3f,
                    3 => so.GrudgeOf(t.A, t.B) >= Society.GrudgeAt,
                    _ => so.Attract[t.A * N + t.B] >= 0.45f && so.Attract[t.B * N + t.A] < 0.45f,
                };
                if (t.Kind == 3 && story.Length == 0) story = s.Causes.Text(t.Cause);
            }
            var past0 = so.Past.Count;
            s.Run(Time.Hours(1));
            same &= so.Past.Count == past0;   // 항해 중에는 심지 않는다
            shown |= m.Agents.Where(a => a.IsPerson).Any(a => so.PastLine(a.Id).StartsWith("지난 일"));
        }
        Check("출항 과거사: 승무원을 태울 때 사람 사이 지난 일 (아는 사이 · 오랜 벗 · 묵은 앙금 · 남몰래 좋아함 — 시드에서 · 같은 시드는 같다) · 정 · 앙금 · 끌림 값이 첫날부터 · 사람 판에 '지난 일' · 항해 중엔 심지 않는다",
            ties > 0 && kinds[1] > 0 && kinds[2] > 0 && kinds[3] > 0 && kinds[4] > 0 && applied && same && shown && story.Length > 0,
            $"세 시드 8명씩: 아는 사이 {kinds[1]} · 오랜 벗 {kinds[2]} · 묵은 앙금 {kinds[3]} · 남몰래 {kinds[4]} · 값 맞음 {applied} · 같은 시드 같고 한 시간 뒤 그대로 {same} · 사람 판 {shown} · \"{story}\"");
    }
}
