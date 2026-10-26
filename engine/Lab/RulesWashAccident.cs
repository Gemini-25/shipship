using ShipEngine;

namespace Lab;

/// <summary>규칙 시험 — 5단 '화장실 사고' (docs/STAGE5.md C16 ~): 온수기 조절기 고장 · 물 온도 · 샤워 물에 데임 …</summary>
public static partial class Program
{
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

        // C17 검토 고침 — 마음 길로: 조절기 접점이 붙은 샤워 칸에서 더러운 이가 끝까지 씻으면 덴다 (예전: 물을 잠그고 온수기를 끈 뒤에 물 온도를 읽어 293K — 늘 0)
        {
            var s = Worlds.Full(lay, 9);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>();
            int p = m.AddPerson(s, Worlds.At(lay, 2, 1));
            s.Run(2);
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
            Check("화장실 사고 (C17 검토 고침): 조절기 접점이 붙은 샤워 칸 (≈ 325K)에서 더러운 이가 마음대로 끝까지 씻으면 덴다 — 물을 잠그기 전의 물 온도로",
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
    }
}
