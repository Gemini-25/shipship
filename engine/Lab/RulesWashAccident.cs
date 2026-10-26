using ShipEngine;

namespace Lab;

/// <summary>규칙 시험 — 5단 '화장실 사고' (docs/STAGE5.md C16 ~): 온수기 조절기 고장 · 물 온도 · 샤워 물에 데임 …</summary>
public static partial class Program
{
    /// <summary>샤워 칸 둘 (C23 ~): 가까운 S (8,1) · 먼 S (27,1 — 줄을 서는 편이 나을 만큼 멀다) · 세면대 · 침대 · 전기.</summary>
    private const string ShowerTwoShip = @"
##############################
#e.....#S..................S.#
#......D.....................#
#pb....#...k.................#
##############################";

    private static void WashAccidentRules()
    {
        if (!Do("화장실 사고")) return;
        Console.WriteLine("\n[화장실 사고 — 조절기 고장 (C16)]");
        var legend = new Dictionary<char, string> { ['e'] = "Bed", ['T'] = "Toilet", ['S'] = "Shower", ['k'] = "Sink", ['p'] = "PowerPanel", ['b'] = "Battery" };
        var lay = Worlds.Ascii(WashShip, legend);

        // C16 조절기 고장 (월드): 밸브 · 온수기를 켜고 2분 — ① 접점 붙음 → 물 325 ± 1K · 부하 3kW · 김이 오른다 / ② 열린 채 굳음 → 293K · 부하 0 · 김 0 /
        //     ③ 고장 사이 0.02시간으로 10분 → 저절로 고장 · 원인 글 '조절기' · 사실 '조절기 고장' / ④ 전기 부품을 고치면 조절기도 새것 → 313K
        {
            (double k, double demand, double steam, Sim s, int dev) Run(int fault)
            {
                var s = Worlds.Full(lay, 3);
                var net = s.Get<Network>(); var f = s.Get<Field>();
                var shower = lay.Furniture.First(x => x.Type == "Shower");
                int dev = net.Devices.First(d => d.Furniture == shower.Id).Id, sc = shower.Cells[0];
                s.Run(2);
                if (fault > 0) net.BreakThermostat(s, dev, fault == 1, s.Causes.Add(CauseKind.Setup, "시험: 온수기 조절기가 고장 났다", s.Tick, sc));
                double h0 = f.Gas(f.T.gH2O, sc);
                net.SetValve(dev, true); net.SetOn(dev, true);
                s.Run(Time.Minutes(2));
                return (net.ShowerWaterK(dev), net.Demanded, f.Gas(f.T.gH2O, sc) - h0, s, dev);
            }
            var stuck = Run(1); var open = Run(2); var ok = Run(0);
            Check("화장실 사고 (C16) 조절기: 밸브 · 온수기를 켜고 2분 — 접점 붙음은 325 ± 1K · 부하 3kW (멀쩡할 때와 같다) · 김이 오른다 / 열린 채 굳음은 293K · 부하 3kW 적다 · 김이 없다 (방 습기가 저절로 오르내리는 몫만) / 멀쩡하면 313K",
                Math.Abs(stuck.k - 325) <= 1 && stuck.steam > 0 && Math.Abs(stuck.demand - ok.demand) < 0.01 && Math.Abs(open.k - 293.15) < 0.5 && Math.Abs(ok.demand - open.demand - 3) < 0.05 && open.steam < 0.05 * stuck.steam && Math.Abs(ok.k - 313) < 0.5,
                $"붙음 {stuck.k:0.0}K · 김 +{stuck.steam:0.00}몰 · 부하 {stuck.demand:0.00}kW / 열림 {open.k:0.0}K · 김 +{open.steam:0.0000} · 부하 {open.demand:0.00}kW / 멀쩡 {ok.k:0.0}K · 부하 {ok.demand:0.00}kW");

            var s3 = Worlds.Full(lay, 3);
            var n3 = s3.Get<Network>();
            var sh3 = lay.Furniture.First(x => x.Type == "Shower");
            int d3 = n3.Devices.First(d => d.Furniture == sh3.Id).Id;
            s3.Run(2);
            n3.ThermoMtbfH = 0.02;
            n3.SetValve(d3, true); n3.SetOn(d3, true);
            for (int k = 0; k < (int)Time.Minutes(10) && (n3.ThermoFault.Length <= d3 || n3.ThermoFault[d3] == 0); k++) s3.Step();
            byte fault = n3.ThermoFault.Length > d3 ? n3.ThermoFault[d3] : (byte)0;
            string why = fault != 0 ? s3.Causes.Text(n3.ThermoCause[d3]) : "";
            bool fact = s3.Facts.Since(0).Any(x => x.Kind == FactKind.ThermoStuck && x.Other == d3);
            n3.Repair(d3, Part.Electric);
            n3.SetValve(d3, true); n3.SetOn(d3, true);
            double fixedK = n3.ShowerWaterK(d3);
            Check("화장실 사고 (C16) 조절기: 고장 사이 0.02시간이면 데우며 흐르는 10분 안에 저절로 고장 난다 (원인 글 '조절기' · 사실 '조절기 고장') · 전기 부품을 고치면 조절기도 새것 (313K)",
                fault is 1 or 2 && why.Contains("조절기") && fact && n3.ThermoFault[d3] == 0 && Math.Abs(fixedK - 313) < 0.5,
                $"고장 {fault} · '{why}' · 사실 {fact} · 고친 뒤 {n3.ThermoFault[d3]} · {fixedK:0.0}K");
        }

        // C17 샤워 물에 데임 (몸에 직접): 333K 3초치 → 화상 0.1 ~ 0.35 · 원인 '뜨거웠다' · 사실 '다쳤다' / 325K 5초치 → 0 · 사실 없음 / 325K 60초치 → 0.15 ~ 0.2 / 313K 6분치 → 0 · 더러움은 씻긴다
        {
            (float burn, int hurt, string why, float dirt) Rinse(double k, double sec)
            {
                var s = Worlds.Full(lay, 3);
                var life = s.Get<Life>();
                int b = life.AddBody(Worlds.At(lay, 9, 3));
                life[b].Dirt = 0.8f;
                s.Run(1);
                float burn0 = life[b].Burn; int f0 = s.Facts.Since(0).Count(x => x.Kind == FactKind.Hurt);
                int thermo = s.Causes.Add(CauseKind.Machine, "시험: 온수기 조절기 접점이 붙었다", s.Tick);
                life.Rinse(s, b, Network.ShowerFlow * sec, k, thermo);
                var hurt = s.Facts.Since(0).Where(x => x.Kind == FactKind.Hurt).Skip(f0).ToList();
                return (life[b].Burn - burn0, hurt.Count, hurt.Count > 0 ? s.Causes.Story(hurt[0].Cause) : "", life[b].Dirt);
            }
            var a = Rinse(333, 3); var b5 = Rinse(325, 5); var b60 = Rinse(325, 60); var ok = Rinse(313, 360);
            Check("화장실 사고 (C17) 샤워 물에 데임: 333K 3초치 → 화상 0.1 ~ 0.35 · '뜨거웠다' (← 조절기) · 사실 '다쳤다' / 325K 5초치 → 0 · 사실 없음 / 325K 60초치 → 0.15 ~ 0.2 / 313K 6분치 → 0 · 더러움이 씻긴다",
                a.burn is >= 0.1f and <= 0.35f && a.hurt == 1 && a.why.Contains("뜨거웠다") && a.why.Contains("조절기") && b5.burn == 0 && b5.hurt == 0 && b60.burn is >= 0.15f and <= 0.2f && ok.burn == 0 && ok.dirt < 0.1f,
                $"333K 3초 {a.burn:0.000} (사실 {a.hurt} · '{a.why}') / 325K 5초 {b5.burn:0.000} ({b5.hurt}) / 60초 {b60.burn:0.000} / 313K 6분 {ok.burn:0.000} · 더러움 0.8 → {ok.dirt:0.00}");
        }

        // C17 검토 고침 — 마음 길로: 조절기 접점이 붙은 샤워 칸에서 뜨거움을 느끼지 못하는 더러운 이가 끝까지 씻으면 덴다 (예전: 물을 잠그고 온수기를 끈 뒤에 물 온도를 읽어 293K — 늘 0)
        {
            var s = Worlds.Full(lay, 9);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>();
            int p = m.AddPerson(s, Worlds.At(lay, 2, 1));
            s.Run(2);
            m.ShowerTooHotK = 400;   // 뜨거움을 느끼지 못하는 이 (C24 뒤 — 느끼면 첫 물에 물러선다: 아래 'C24 뜨거운 샤워')
            var shower = lay.Furniture.First(x => x.Type == "Shower");
            int dev = net.Devices.First(d => d.Furniture == shower.Id).Id;
            net.BreakThermostat(s, dev, true, s.Causes.Add(CauseKind.Setup, "시험: 온수기 조절기 접점이 붙었다", s.Tick, shower.Cells[0]));
            life[m[p].C.Body].Dirt = 0.8f;
            float burn0 = life[m[p].C.Body].Burn;
            for (long t = 0; t < Time.Minutes(30) && m.ShowersTaken == 0; t++)
            {
                int off = ((int)Time.HourOfDay(s.Tick) + 24 - 10) % 24;
                ref var c = ref m[p].C; c.ShiftHour = off; c.Sleepy = 0; c.Hunger = 0; c.Thirst = 0; c.Lonely = 0; c.Unfit = 0; c.Bored = 0;
                s.Step();
            }
            float burn = life[m[p].C.Body].Burn - burn0;
            Check("화장실 사고 (C17 검토 고침): 조절기 접점이 붙은 샤워 칸 (≈ 325K)에서 뜨거움을 느끼지 못하는 더러운 이가 마음대로 끝까지 씻으면 덴다 — 물을 잠그기 전의 물 온도로",
                m.ShowersTaken == 1 && burn > 0.1f, $"샤워 {m.ShowersTaken} · 화상 +{burn:0.000}");
        }

        // C18 무중력의 샤워 물: 침실에 중력 발생기 — 밸브를 열고 발생기를 끄면 1분 뒤 샤워 물이 배수구로 가지 않고 방울로 떠 있다 (오수가 늘지 않는다) ·
        //     원인 글 '떠다닌다' (← 중력이 꺼졌다) · 그 칸 둘레 물의 까닭 (SpillNear)이 그 원인 / 발생기를 다시 켜면 원인을 지운다
        {
            var lg = new Dictionary<char, string>(legend) { ['G'] = "GravityGenerator" };
            var glay = Worlds.Ascii(WashShip.Replace("#e.....#T", "#e..G..#T"), lg);
            var s = Worlds.Full(glay, 3);
            var net = s.Get<Network>(); var f = s.Get<Field>();
            var shower = glay.Furniture.First(x => x.Type == "Shower");
            int dev = net.Devices.First(d => d.Furniture == shower.Id).Id, sc = shower.Cells[0];
            int gen = net.Devices.First(d => d.Type == "GravityGenerator").Id;
            s.Run(Time.Minutes(1));
            net.SetOn(gen, false);
            s.Run(6);
            net.SetValve(dev, true);
            double drops0 = Enumerable.Range(0, glay.N).Sum(c => f.Drops[f.T.sWater * glay.N + c]), waste0 = net.WasteWater;
            s.Run(Time.Minutes(1));
            double drops = Enumerable.Range(0, glay.N).Sum(c => f.Drops[f.T.sWater * glay.N + c]) - drops0, waste = net.WasteWater - waste0;
            int fc = dev < net.ShowerFloatCause.Length ? net.ShowerFloatCause[dev] : 0;
            string why = fc != 0 ? s.Causes.Story(fc) : "";
            int near = net.SpillNear(sc);
            net.SetValve(dev, false);
            net.SetOn(gen, true);
            s.Run(Time.Minutes(1));
            int after = dev < net.ShowerFloatCause.Length ? net.ShowerFloatCause[dev] : 0;
            Check("화장실 사고 (C18) 무중력 샤워 물: 밸브를 열고 중력 발생기를 끄면 1분 뒤 샤워 물이 방울로 떠 있다 (오수가 늘지 않는다) · 원인 '떠다닌다' ← '꺼졌다' · 그 칸 물의 까닭 = 그 원인 · 중력이 돌아오면 지운다",
                drops > 10 && waste < 1 && why.Contains("떠다닌다") && why.Contains("꺼졌다") && near == fc && after == 0,
                $"방울 +{drops:0.0}몰 · 오수 +{waste:0.0} · '{why}' · 물의 까닭 {(near == fc && fc != 0 ? "그 원인" : near.ToString())} · 돌아온 뒤 {after}");
        }

        // C19 오물이 썩는다 (WashShip · 정화기 없음 · 때 0): 오물 통 28kg 2시간 → 화장실 메탄 ≥ 10ppm · 악취 0 / 봉함을 망가뜨리면 1시간 안에 악취 ≥ 0.004ppm /
        //     변기에 0.4kg가 걸려 막히면 1시간 안에 악취 ≥ 0.004ppm · 산소가 준다
        {
            (double ch4, double stench, double o2drop) Rot(double tank, bool seal, double bowl, double hours)
            {
                var s = Worlds.Full(lay, 3);
                var life = s.Get<Life>(); var net = s.Get<Network>(); var f = s.Get<Field>();
                s.Run(2);
                var toilet = lay.Furniture.Where(x => x.Type == "Toilet").OrderBy(x => lay.X(x.Cells[0])).First();
                int room = lay.Room[toilet.Cells[0]];
                foreach (var fu in lay.Furniture) if (fu.Type == "Toilet") life.Grime[fu.Id] = 0;
                life.Tank[toilet.Id] = tank;
                if (bowl > 0) { life.Bowl[toilet.Id] = bowl; life.Clog(toilet.Id, s.Causes.Add(CauseKind.Setup, "시험: 변기가 막혔다", s.Tick)); }
                if (seal) net.Broken[net.Devices.First(d => d.Furniture == toilet.Id).Id * 4 + (int)Part.Seal] = 1;
                double Ppm(int g) { double n = 0, all = 0; foreach (int c in lay.Rooms[room].Cells) { n += f.Gas(g, c); all += f.Moles(c); } return n / Math.Max(1e-9, all) * 1e6; }
                double O2() { double n = 0; foreach (int c in lay.Rooms[room].Cells) n += f.Gas(f.T.gO2, c); return n; }
                double ch40 = Ppm(f.T.gCH4), o20 = O2();
                s.Run(Time.Hours(hours));
                return (Ppm(f.T.gCH4) - ch40, Ppm(f.T.gStench), o20 - O2());
            }
            var tank = Rot(28, false, 0, 2); var seal = Rot(28, true, 0, 1); var bowl = Rot(0, false, 0.4, 1); var none = Rot(0, false, 0, 1);   // 맞수: 같은 시드 · 오물 없음 (옆 방과 섞이는 공기 몫을 뺀다)
            Check("화장실 사고 (C19) 오물이 썩는다: 닫힌 오물 통 28kg 2시간 → 화장실 메탄 10ppm 넘게 · 악취 0 / 통의 봉함이 망가지면 1시간 안에 악취 0.004ppm 넘게 / 변기에 0.4kg가 걸려 막히면 1시간 안에 악취 0.004ppm 넘게 · 산소가 준다 (오물 없는 맞수보다)",
                tank.ch4 >= 10 && tank.stench == 0 && seal.stench >= 0.004 && bowl.stench >= 0.004 && bowl.o2drop - none.o2drop > 0,
                $"통 메탄 +{tank.ch4:0.0}ppm · 악취 {tank.stench:0.0000} / 봉함 악취 {seal.stench:0.0000}ppm / 걸린 것 악취 {bowl.stench:0.0000}ppm · 산소 −{(bowl.o2drop - none.o2drop) * 1000:0.00}밀리몰 (오물 없는 맞수보다)");
        }

        // C20 포자 기침 (DF1이 셈을 지었다 — 마시는 포자 = (제 칸 + 방 평균) / 2): 화장실의 다른 칸이 모두 곰팡이 0.8 · 곰팡이 없는 칸에 선 이가 1시간 → 기침 1 넘게 · 증상 기침 ·
        //     기침 소리의 원인 글에 '곰팡이' (← 그 방에서 가장 짙은 곰팡이 마디 — C20) / 곰팡이 없는 배는 0
        {
            (long coughs, bool sym, string why) Spore(bool mold)
            {
                var s = Worlds.Full(lay, 3);
                var life = s.Get<Life>(); var snd = s.Get<Sound>();
                var toilet = lay.Furniture.First(x => x.Type == "Toilet");
                int room = lay.Room[toilet.Cells[0]];
                var cells = lay.Rooms[room].Cells.Where(c => lay.FurnitureAt[c] < 0).ToArray();
                int stand = cells[cells.Length / 2];
                if (mold) foreach (int c in lay.Rooms[room].Cells.Where(c => c != stand)) life.Mold[c] = 0.8f;
                int b = life.AddBody(stand);
                s.Run(2);
                long c0 = life.Coughs, log0 = snd.Logged;
                for (int k = 0; k < (int)Time.Hours(1); k++) { life[b].Cell = stand; s.Step(); }
                var cough = snd.LogSince(log0).Where(e => e.Kind == SoundKind.Cough && e.Actor == Sound.BodyActor(b)).ToList();
                string why = cough.Count > 0 && cough[^1].Cause != 0 ? s.Causes.Story(cough[^1].Cause) : "";
                return (life.Coughs - c0, (Life.Symptoms(life[b]) & Sym.Cough) != 0, why);
            }
            var m = Spore(true); var none = Spore(false);
            Check("화장실 사고 (C20) 포자 기침: 화장실의 다른 칸이 모두 곰팡이 0.8 · 곰팡이 없는 칸에 1시간 — 기침 1 넘게 · 증상 '기침' · 기침 소리의 원인 글에 '곰팡이' / 곰팡이 없는 배는 0",
                m.coughs >= 1 && m.sym && m.why.Contains("곰팡이") && none.coughs == 0,
                $"기침 {m.coughs} · 증상 {m.sym} · '{m.why}' / 곰팡이 없음 {none.coughs}");
        }

        // C23 샤워 칸 사용 금지 불 (사람이 본다 · 비트): 샤워 칸 둘 (가까운 S (8,1) · 먼 S (27,1)) · 더러운 이 —
        //     (가) 걸어가던 중 가까운 칸 불 (비트 1)이 켜지면 먼 칸에서 씻는다 · (나) 줄 선 중 켜져도 먼 칸 · (다) 비트 2를 켰다 꺼도 비트 1 불은 남는다
        //     (예전: 사람 · 줄 안내가 변기 불만 보아 불 켜진 샤워 칸에 들어가 틀었다)
        {
            var two = Worlds.Ascii(ShowerTwoShip, legend);
            int TAt(int x, int y) => Worlds.At(two, x, y);
            (long nearUse, long farUse, bool ok) Shower(bool queued)
            {
                var s = Worlds.Full(two, 5);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>();
                var sh = two.Furniture.Where(x => x.Type == "Shower").OrderBy(x => two.X(x.Cells[0])).ToList();
                int near = sh[0].Id, far = sh[1].Id, dn = net.Devices.First(d => d.Furniture == near).Id, df = net.Devices.First(d => d.Furniture == far).Id;
                int a = m.AddPerson(s, TAt(2, 1)), b = queued ? m.AddPerson(s, TAt(9, 2)) : -1;
                s.Run(2);
                void Calm(int p) { int off = ((int)Time.HourOfDay(s.Tick) + 24 - 10) % 24; ref var c = ref m[p].C; c.ShiftHour = off; c.Sleepy = 0; c.Hunger = 0; c.Thirst = 0; c.Lonely = 0; c.Unfit = 0; c.Bored = 0; }
                if (queued) { life[m[b].C.Body].Dirt = 0.95f; for (long t = 0; t < Time.Minutes(10) && m[b].C.Cell != sh[0].Cells[0]; t++) { Calm(b); s.Step(); } }   // B가 먼저 가까운 칸에서 씻는다
                life[m[a].C.Body].Dirt = 0.9f;
                double n0 = net.ShowerMeter[dn], f0 = net.ShowerMeter[df];
                bool signed = false;
                for (long t = 0; t < Time.Minutes(40); t++)
                {
                    Calm(a); if (queued) Calm(b);
                    s.Step();
                    var ac = m[a].C;
                    bool walking = ac.Goal == GoalKind.Wash && ac.GoalTarget == near && ac.StepIndex < m[a].Plan.Count && m[a].Plan[ac.StepIndex].Kind == StepKind.GoTo && ac.StepIndex == 0;
                    bool inQueue = ac.Goal == GoalKind.Wash && ac.GoalTarget == near && ac.StepIndex == 1;
                    if (!signed && (queued ? inQueue && s.Tick - ac.StepStart > 5 : walking && two.X(ac.Cell) >= 4)) { m.SignWash(near, 1, true); signed = true; }
                    if (signed && m.ShowersTaken >= (queued ? 2 : 1) && life[m[a].C.Body].Dirt < 0.5f) break;
                }
                // 비트: 2를 켰다 끈다 — 1은 남는다
                m.SignWash(near, 2, true); m.SignWash(near, 2, false);
                bool kept = m.WashSign[near] == 2 && signed;   // 비트 1 → 값 2 (1 << 1)
                return ((long)(net.ShowerMeter[dn] - n0), (long)(net.ShowerMeter[df] - f0), kept && life[m[a].C.Body].Dirt < 0.5f);
            }
            var walk = Shower(false); var line = Shower(true);
            Check("화장실 사고 (C23) 샤워 칸 사용 금지 불: 걸어가던 이 · 줄 선 이는 불 켜진 가까운 칸이 아닌 먼 칸에서 씻는다 · 비트 2를 켰다 꺼도 비트 1 불은 남는다",
                walk.nearUse == 0 && walk.farUse > 0 && walk.ok && line.farUse > 0 && line.ok,
                $"걸어가던 이: 가까운 칸 물 {walk.nearUse}몰 · 먼 칸 {walk.farUse}몰 · 씻김+불 {walk.ok} / 줄 선 이: 먼 칸 {line.farUse}몰 · 씻김+불 {line.ok}");
        }

        // C24 샤워 물을 느낀다: 샤워 칸 둘 — (가) 가까운 칸 조절기 접점이 붙었다 (325K): 첫 물에 잠그고 물러선다 (30초 안 · 화상 < 0.02 · 그 칸 샤워 0 · 알림 '뜨겁다' 1) → 30분 안에 먼 칸에서 씻는다 ·
        //     둘 다 붙었으면 물수건 / (나) 가까운 칸 조절기가 열린 채 굳었다 (293K): 2분 헹구고 잠근다 (≥ 120초치 · 더러움 0.8 → 0.5 아래 · 알림 '찬물')
        //     (예전: 6분을 다 쓰고 · 알림이 없었다)
        {
            var two = Worlds.Ascii(ShowerTwoShip, legend);
            (double nearSec, double burn, int hotReports, int coldReports, bool farWashed, long wipes, float dirt, bool closed, double stopSec) Feel(int fault, bool both)
            {
                var s = Worlds.Full(two, 5);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>();
                var sh = two.Furniture.Where(x => x.Type == "Shower").OrderBy(x => two.X(x.Cells[0])).ToList();
                int dn = net.Devices.First(d => d.Furniture == sh[0].Id).Id, df = net.Devices.First(d => d.Furniture == sh[1].Id).Id;
                int a = m.AddPerson(s, Worlds.At(two, 2, 1));
                s.Run(2);
                int cause = s.Causes.Add(CauseKind.Setup, "시험: 조절기가 고장 났다", s.Tick);
                net.BreakThermostat(s, dn, fault == 1, cause);
                if (both) net.BreakThermostat(s, df, fault == 1, cause);
                void Calm() { int off = ((int)Time.HourOfDay(s.Tick) + 24 - 10) % 24; ref var c = ref m[a].C; c.ShiftHour = off; c.Sleepy = 0; c.Hunger = 0; c.Thirst = 0; c.Lonely = 0; c.Unfit = 0; c.Bored = 0; }
                life[m[a].C.Body].Dirt = 0.8f;
                float burn0 = life[m[a].C.Body].Burn; long w0 = m.Wipes; double n0 = net.ShowerMeter[dn], f0 = net.ShowerMeter[df];
                long opened = -1, shut = -1;
                for (long t = 0; t < Time.Minutes(40); t++)
                {
                    Calm(); s.Step();
                    if (opened < 0 && net.ShowerMeter[dn] > n0) opened = s.Tick;
                    if (opened >= 0 && shut < 0 && !net.ValveOpen(dn)) shut = s.Tick;
                }
                return ((net.ShowerMeter[dn] - n0) / Network.ShowerFlow, life[m[a].C.Body].Burn - burn0, m.WashReports.Count(r => r.Kind == 1 && r.Where == dn), m.WashReports.Count(r => r.Kind == 2 && r.Where == dn),
                    net.ShowerMeter[df] - f0 > Network.ShowerFlow * 200, m.Wipes - w0, life[m[a].C.Body].Dirt, !net.ValveOpen(dn), shut >= 0 && opened >= 0 ? Time.ToSeconds(shut - opened) : -1);
            }
            var hot = Feel(1, false); var hot2 = Feel(1, true); var cold = Feel(2, false);
            Check("화장실 사고 (C24) 뜨거운 샤워: 첫 물에 잠그고 물러선다 (30초 안 · 화상 < 0.02 · 알림 '뜨겁다' 하나) → 30분 안에 먼 칸에서 씻는다 · 둘 다 뜨거우면 물수건",
                hot.stopSec >= 0 && hot.stopSec <= 30 && hot.burn < 0.02f && hot.hotReports == 1 && hot.farWashed && hot.dirt < 0.5f && hot2.wipes >= 1,
                $"물러서기까지 {hot.stopSec:0}초 · 그 칸 물 {hot.nearSec:0}초치 · 화상 {hot.burn:0.000} · 알림 {hot.hotReports} · 먼 칸에서 씻음 {hot.farWashed} · 더러움 {hot.dirt:0.00} / 둘 다: 물수건 {hot2.wipes}");
            Check("화장실 사고 (C24) 찬물 샤워: 처음 느낀 물이 찬물이면 2분 헹구고 잠근다 (≥ 120초치 · 6분이 아니다 · 더러움 0.8 → 0.5 아래 · 밸브 잠김 · 알림 '찬물' 하나)",
                cold.nearSec >= 115 && cold.nearSec <= 200 && cold.dirt < 0.5f && cold.closed && cold.coldReports == 1,
                $"헹군 물 {cold.nearSec:0}초치 · 더러움 {cold.dirt:0.00} · 밸브 잠김 {cold.closed} · 알림 {cold.coldReports}");
        }

        // C24 (다) 몸이 떴다: 씻는 중에 중력 발생기가 꺼지면 (C18 도면) — 1분 안에 쓰던 샤워를 잠그고 나온다 (씻은 것으로 치지 않는다 · 예전: 6분 동안 물을 틀어 두었다)
        {
            var lg = new Dictionary<char, string>(legend) { ['G'] = "GravityGenerator" };
            var glay = Worlds.Ascii(WashShip.Replace("#e.....#T", "#e..G..#T"), lg);
            var s = Worlds.Full(glay, 3);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>();
            var shower = glay.Furniture.First(x => x.Type == "Shower");
            int dev = net.Devices.First(d => d.Furniture == shower.Id).Id, gen = net.Devices.First(d => d.Type == "GravityGenerator").Id;
            int p = m.AddPerson(s, Worlds.At(glay, 2, 1));
            s.Run(2);
            life[m[p].C.Body].Dirt = 0.8f;
            long off = -1, shut = -1; long taken0 = m.ShowersTaken;
            for (long t = 0; t < Time.Minutes(30) && shut < 0; t++)
            {
                int o = ((int)Time.HourOfDay(s.Tick) + 24 - 10) % 24;
                ref var c = ref m[p].C; c.ShiftHour = o; c.Sleepy = 0; c.Hunger = 0; c.Thirst = 0; c.Lonely = 0; c.Unfit = 0; c.Bored = 0;
                s.Step();
                if (off < 0 && net.ValveOpen(dev) && net.ShowerMeter[dev] > 0) { net.SetOn(gen, false); off = s.Tick; }
                if (off >= 0 && !net.ValveOpen(dev)) shut = s.Tick;
            }
            Check("화장실 사고 (C24) 무중력 샤워: 씻는 중 중력이 꺼져 몸이 뜨면 1분 안에 쓰던 샤워를 잠그고 나온다 (씻은 것으로 치지 않는다)",
                off >= 0 && shut >= 0 && shut - off <= Time.Minutes(1) && m.ShowersFloated >= 1 && m.ShowersTaken == taken0,
                $"끈 틱 {off} · 잠근 틱 {shut} ({(shut >= 0 && off >= 0 ? Time.ToSeconds(shut - off) : -1):0}초) · 떠서 그침 {m.ShowersFloated} · 샤워 {m.ShowersTaken - taken0}");
        }

        // C25 샤워 칸 알림 → 사용 금지 · 갈기 (주컴퓨터): 샤워 칸 둘 + 주컴퓨터 · 로봇 독 —
        //     (가) 가까운 칸 조절기가 붙어 A가 '뜨겁다'고 알리면: 밸브 잠금 · 불 (비트 1) · 방송 · 미리 갈기 일감 → 로봇이 전기 부품을 간다 → 불 꺼짐 · '고쳤습니다' → B가 그 칸에서 6분 씻고 데지 않는다
        //     (나) 부품이 없으면: 불은 켜진 채 · '부품이 없어' 방송 · 일감 0 / (다) 찬물 알림: 주컴퓨터가 전력 때문에 끈 온수기면 일감 0 · 아니면 (조절기가 열린 채 굳음) 일감 1
        //     (예전: 알림 길이 없었다)
        {
            var art = ShowerTwoShip.Replace("#e.....#S", "#e..c..#S").Replace("#pb....#...k.................#", "#pb....#...k......r..........#");
            var lg = new Dictionary<char, string>(legend) { ['c'] = "MainComputer", ['r'] = "RobotDock" };
            var cl = Worlds.Ascii(art, lg);
            (bool sign, bool said, bool fixedOk, bool off, bool saidFixed, float burnB, long showersB, int jobs, bool noPart, bool kept) Note(int mode)   // 0 뜨거움 → 갈기 · 1 부품 없음 · 2 찬물 (전력으로 끔) · 3 찬물 (조절기)
            {
                var s = Worlds.Full(cl, 5);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>(); var sm = s.Get<ShipMind>();
                var sh = cl.Furniture.Where(x => x.Type == "Shower").OrderBy(x => cl.X(x.Cells[0])).ToList();
                int near = sh[0].Id, dn = net.Devices.First(d => d.Furniture == near).Id;
                int r = m.AddRobot(s, Worlds.At(cl, 18, 3), AgentKind.Robot, Worlds.At(cl, 18, 3));
                int a = m.AddPerson(s, Worlds.At(cl, 2, 1));
                s.Run(2);
                if (mode == 1) m[r].C.Spares = 0;
                int cause = s.Causes.Add(CauseKind.Setup, "시험: 조절기가 고장 났다", s.Tick);
                if (mode != 2) net.BreakThermostat(s, dn, mode is 0 or 1, cause);
                if (mode == 2) { net.SetOn(dn, false); sm.Shed[dn] = 1; }   // 주컴퓨터가 전력 때문에 꺼 둔 온수기 — 찬물
                void Calm(int p) { int off = ((int)Time.HourOfDay(s.Tick) + 24 - 10) % 24; ref var c = ref m[p].C; c.ShiftHour = off; c.Sleepy = 0; c.Hunger = 0; c.Thirst = 0; c.Lonely = 0; c.Unfit = 0; c.Bored = 0; }
                life[m[a].C.Body].Dirt = 0.8f;
                bool sign = false, said = false, saidFixed = false, noPart = false; int jobs = 0;
                long bc0 = sm.Broadcasts.Count;
                for (long t = 0; t < Time.Minutes(40); t++)
                {
                    Calm(a); if (mode == 2) { sm.Shed[dn] = 1; net.SetOn(dn, false); }
                    s.Step();
                    sign |= (m.WashSign[near] & 2) != 0;
                    jobs = Math.Max(jobs, sm.Jobs.Count(j => j.Kind == GoalKind.Maintain && j.Target / 4 == dn));
                    foreach (var (_, text) in sm.Broadcasts) { said |= text.Contains("쓰지 마세요"); saidFixed |= text.Contains("고쳤습니다"); noPart |= text.Contains("부품이 없어"); }
                }
                bool fixedOk = net.ThermoFault[dn] == 0, off = (m.WashSign[near] & 2) == 0;
                // 고친 뒤 B가 그 칸에서 씻는다
                float burnB = 0; long showersB = 0;
                if (mode == 0)
                {
                    int b = m.AddPerson(s, Worlds.At(cl, 9, 2));
                    s.Run(2);
                    life[m[b].C.Body].Dirt = 0.8f;
                    float b0 = life[m[b].C.Body].Burn; long n0 = m.ShowersTaken; double meter0 = net.ShowerMeter[dn];
                    for (long t = 0; t < Time.Minutes(20) && m.ShowersTaken == n0; t++) { Calm(a); Calm(b); s.Step(); }
                    burnB = life[m[b].C.Body].Burn - b0; showersB = (long)((net.ShowerMeter[dn] - meter0) / Network.ShowerFlow);
                }
                return (sign, said, fixedOk, off, saidFixed, burnB, showersB, jobs, noPart, (m.WashSign[near] & 2) != 0);
            }
            var fix = Note(0); var none = Note(1); var shed = Note(2); var coldT = Note(3);
            Check("화장실 사고 (C25) 조절기 알림 → 갈기: '뜨겁다' 알림에 주컴퓨터가 밸브를 잠그고 사용 금지 불 · '쓰지 마세요' 방송 · 미리 갈기 일감 → 로봇이 갈아 조절기가 새것 · 불 꺼짐 · '고쳤습니다' → 다른 이가 그 칸에서 씻고 데지 않는다",
                fix.sign && fix.said && fix.jobs >= 1 && fix.fixedOk && fix.off && fix.saidFixed && fix.showersB >= 300 && fix.burnB < 0.02f,
                $"불 {fix.sign} · 방송 {fix.said} · 일감 {fix.jobs} · 고침 {fix.fixedOk} · 불 꺼짐 {fix.off} · '고쳤습니다' {fix.saidFixed} · B 그 칸 물 {fix.showersB}초치 · 화상 {fix.burnB:0.000}");
            Check("화장실 사고 (C25) 부품 없음: 갈 부품이 없으면 불은 켜진 채 · '부품이 없어' 방송 · 일감 0",
                none.sign && none.kept && none.noPart && none.jobs == 0, $"불 {none.sign} · 켜진 채 {none.kept} · 방송 {none.noPart} · 일감 {none.jobs}");
            Check("화장실 사고 (C25) 찬물 알림: 주컴퓨터가 전력 때문에 끈 온수기면 일감 0 (불도 없다) · 조절기가 열린 채 굳은 찬물이면 미리 갈기 일감 1 (불은 켜지 않는다)",
                shed.jobs == 0 && !shed.sign && coldT.jobs >= 1 && !coldT.sign, $"끈 온수기: 일감 {shed.jobs} · 불 {shed.sign} / 조절기: 일감 {coldT.jobs} · 불 {coldT.sign}");
        }

        // C26 중력이 무너지면 샤워 칸 잠금 (주컴퓨터): C18 도면 + 주컴퓨터 — 씻는 중 중력 발생기가 고장 나 꺼지면 1분 안에 밸브가 잠기고 불 (비트 2) · 방송 · 물수건 /
        //     발생기를 고쳐 중력이 돌아오면 비트 2만 꺼진다 (미리 켠 비트 1은 남는다) (예전: 빈 칸 2분 잠금뿐 — 불이 없었다)
        {
            var lg = new Dictionary<char, string>(legend) { ['G'] = "GravityGenerator", ['c'] = "MainComputer" };
            var glay = Worlds.Ascii(WashShip.Replace("#e.....#T", "#e..G..#T").Replace("#pb....#", "#pbc...#"), lg);
            var s = Worlds.Full(glay, 3);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>(); var sm = s.Get<ShipMind>();
            var shower = glay.Furniture.First(x => x.Type == "Shower");
            int dev = net.Devices.First(d => d.Furniture == shower.Id).Id, gen = net.Devices.First(d => d.Type == "GravityGenerator").Id;
            int p = m.AddPerson(s, Worlds.At(glay, 2, 1));
            s.Run(2);
            life[m[p].C.Body].Dirt = 0.8f;
            void Calm() { int o = ((int)Time.HourOfDay(s.Tick) + 24 - 10) % 24; ref var c = ref m[p].C; c.ShiftHour = o; c.Sleepy = 0; c.Hunger = 0; c.Thirst = 0; c.Lonely = 0; c.Unfit = 0; c.Bored = 0; }
            long broke = -1, shut = -1; bool signed = false, said = false; long w0 = m.Wipes;
            for (long t = 0; t < Time.Minutes(40); t++)
            {
                Calm(); s.Step();
                if (broke < 0 && net.ValveOpen(dev) && net.ShowerMeter[dev] > 0) { m.ShowerTooHotK = 400; m.SignWash(shower.Id, 1, true); net.Broken[gen * 4 + (int)Part.Electric] = 1; broke = s.Tick; }   // 비트 1은 미리 (다른 까닭) · 사람이 먼저 잠그지 않게 느끼지 못하는 이
                if (broke >= 0 && shut < 0 && !net.ValveOpen(dev)) shut = s.Tick;
                signed |= (m.WashSign[shower.Id] & 4) != 0;
                foreach (var (_, text) in sm.Broadcasts) said |= text.Contains("샤워 칸 물을 잠갔습니다");
            }
            long wiped = m.Wipes - w0;
            net.Broken[gen * 4 + (int)Part.Electric] = 0;   // 고쳤다 — 주컴퓨터가 다시 켠다
            s.Run(Time.Minutes(10));
            byte after = m.WashSign[shower.Id];
            Check("화장실 사고 (C26) 무중력 샤워 잠금: 씻는 중 중력이 무너지면 주컴퓨터가 1분 안에 샤워 밸브를 잠그고 사용 금지 불 (비트 2) · 방송 · 물수건 / 중력이 돌아오면 비트 2만 꺼진다 (비트 1은 남는다)",
                broke >= 0 && shut >= 0 && shut - broke <= Time.Minutes(1) && signed && said && wiped >= 1 && after == 2,
                $"잠금 {(shut >= 0 && broke >= 0 ? Time.ToSeconds(shut - broke) : -1):0}초 · 불 {signed} · 방송 {said} · 물수건 {wiped} · 돌아온 뒤 불 비트 {after}");
        }
    }
}
