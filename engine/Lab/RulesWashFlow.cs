using ShipEngine;
using ShipView;
using ShipView.Audio;

namespace Lab;

/// <summary>규칙 시험 — 5단 C7 '화장실 소리' 둘째: 쉬익 (변기 물 내림) · 손 씻는 물 · 샤워 물소리는 물 흐름을 따른다 (docs/STAGE5.md C7).</summary>
public static partial class Program
{
    private static void WashFlowSoundRules()
    {
        if (!Do("화장실 소리")) return;
        Console.WriteLine("\n[화장실 소리 — 쉬익 · 손 씻는 물 · 샤워 물소리 (C7)]");
        var legend = new Dictionary<char, string> { ['e'] = "Bed", ['T'] = "Toilet", ['S'] = "Shower", ['k'] = "Sink", ['p'] = "PowerPanel", ['b'] = "Battery", ['w'] = "WaterRecycler" };
        var lay = Worlds.Ascii(WashViewShip, legend);

        // (가) 쉬익: 변기에서 비우면 변기 칸에서 72dB 쉬익 하나 (낸 이 = 그 몸) · 흡입 팬이 망가지면 나지 않는다
        {
            int Whoosh(bool broken)
            {
                var s = Worlds.Full(lay, 3);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>(); var snd = s.Get<Sound>();
                var toilet = lay.Furniture.First(x => x.Type == "Toilet");
                int tc = toilet.Cells[0], p = m.AddPerson(s, tc), body = m[p].C.Body;
                s.Run(2);
                if (broken) net.Broken[net.Devices.First(d => d.Furniture == toilet.Id).Id * 4 + (int)Part.Mechanical] = 1;
                long log0 = snd.Logged;
                life[body].Bladder = 20;
                life.Flush(s, body, toilet.Id, tc);
                var w = snd.LogSince(log0).Where(e => e.Kind == SoundKind.Flush).ToList();
                return w.Count == 1 && w[0].Cell == tc && w[0].Db == Life.FlushDb && w[0].Actor == Sound.BodyActor(body) ? 1 : w.Count == 0 ? 0 : -w.Count;
            }
            int ok = Whoosh(false), off = Whoosh(true);
            Check("화장실 소리 (C7) 쉬익: 변기에서 비우면 변기 칸에서 72dB 쉬익 하나 (낸 이 = 그 몸 — 제 소리에 고개를 돌리지 않는다) · 흡입 팬의 기계 부품이 망가지면 나지 않는다",
                ok == 1 && off == 0, $"멀쩡 {ok} · 팬 망가짐 {off}");
        }

        // (나) 샤워 물소리: 물이 흐르면 (온수기와 상관없이) 50dB · 밸브를 닫고 온수기만 켜면 0 · 물관이 끊기면 0 · 열고 25틱이면 이어지는 소리가 50dB
        {
            var s = Worlds.Full(lay, 3);
            var net = s.Get<Network>(); var snd = s.Get<Sound>();
            var shower = lay.Furniture.First(x => x.Type == "Shower");
            int dev = net.Devices.First(d => d.Furniture == shower.Id).Id, sc = shower.Cells[0];
            s.Run(2);
            net.SetValve(dev, true); net.SetOn(dev, false);
            float cold = net.NoiseDb(dev);
            net.SetValve(dev, false); net.SetOn(dev, true);
            float heaterOnly = net.NoiseDb(dev);
            net.SetValve(dev, true);
            s.Run(25);
            float drone = snd.DroneDb(dev), flowing = net.NoiseDb(dev);
            net.CutLines(s, sc, Network.Bit(LineKind.Water), s.Causes.Add(CauseKind.Setup, "시험: 샤워 칸 밑 물관이 끊겼다", s.Tick, sc));
            float cut = net.NoiseDb(dev);
            Check("화장실 소리 (C7) 샤워 물소리: 밸브만 열고 온수기를 끄면 50dB (찬물도 흐른다) · 닫고 온수기만 켜면 0 · 열고 25틱 → 이어지는 소리 50dB · 물관이 끊기면 0",
                cold == 50 && heaterOnly == 0 && flowing == 50 && drone == 50 && cut == 0,
                $"찬물 {cold} · 온수기만 {heaterOnly} · 흐름 {flowing} · 이어지는 소리 {drone} · 끊김 {cut}");
        }

        // (다) 손 씻는 물: 성실한 이가 변기에서 비운 뒤 손을 씻으면 세면대 칸에서 55dB 쏴 (손 씻음마다 하나 · 낸 이 = 그 몸) · 세면대 물관이 끊기면 둘 다 0
        {
            (long washes, int faucets, bool at) Hands(bool cut)
            {
                var s = Worlds.Full(lay, 11);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>(); var snd = s.Get<Sound>();
                var sink = lay.Furniture.First(x => x.Type == "Sink");
                int kc = sink.Cells[0];
                int p = m.AddPerson(s, Worlds.At(lay, 2, 1));
                m.SetPersonality(p, 0.5f, 0.95f, 0.5f, 0.5f, 0.3f);
                s.Run(2);
                if (cut) net.CutLines(s, kc, Network.Bit(LineKind.Water), s.Causes.Add(CauseKind.Setup, "시험: 세면대 밑 물관이 끊겼다", s.Tick, kc));
                void Calm() { int off = ((int)Time.HourOfDay(s.Tick) + 24 - 10) % 24; ref var c = ref m[p].C; c.ShiftHour = off; c.Sleepy = 0; c.Hunger = 0; c.Thirst = 0; c.Lonely = 0; c.Unfit = 0; c.Bored = 0; }
                long w0 = m.HandWashes, log0 = snd.Logged;
                for (int k = 0; k < 2; k++)
                {
                    life[m[p].C.Body].Bladder = Life.BladderMax * 0.75;
                    long n0 = m.Toilets;
                    for (long t = 0; t < Time.Minutes(20) && m.Toilets == n0; t++) { Calm(); s.Step(); }
                    for (long t = 0; t < Time.Minutes(3); t++) { Calm(); s.Step(); }
                }
                var f = snd.LogSince(log0).Where(e => e.Kind == SoundKind.Faucet).ToList();
                return (m.HandWashes - w0, f.Count, f.Count > 0 && f.All(e => e.Cell == kc && e.Db == Life.FaucetDb && e.Actor == Sound.BodyActor(m[p].C.Body)));
            }
            var ok = Hands(false); var dry = Hands(true);
            Check("화장실 소리 (C7) 손 씻는 물: 성실한 이가 두 번 볼일 뒤 손을 씻으면 손 씻음마다 세면대 칸에서 55dB 쏴 하나 (낸 이 = 그 몸) · 세면대 물관이 끊기면 둘 다 0",
                ok.washes >= 1 && ok.faucets == ok.washes && ok.at && dry.washes == 0 && dry.faucets == 0,
                $"손 씻음 {ok.washes} · 쏴 {ok.faucets} (자리 · 크기 · 낸 이 {ok.at}) / 끊김: 손 씻음 {dry.washes} · 쏴 {dry.faucets}");
        }

        // (라) 화면: 귀를 화장실에 두면 쉬익 · 쏴가 소리로 나고 자막이 나온다
        {
            var show = new LiveShow(Worlds.Full(lay, 3), "시험호");
            var s = show.Sim;
            var snd = s.Get<Sound>();
            show.Advance(1.0 / 60);
            show.Ears.Captions = true;
            var toilet = lay.Furniture.First(x => x.Type == "Toilet"); var sink = lay.Furniture.First(x => x.Type == "Sink");
            int ear = toilet.Cells[0] + lay.W;
            snd.Emit(s, toilet.Cells[0], Life.FlushDb, SoundKind.Flush);
            snd.Emit(s, sink.Cells[0], Life.FaucetDb, SoundKind.Faucet);
            show.Ears.ForceEar = ear;
            int shots = 0; var caps = new HashSet<string>();
            for (int k = 0; k < 240; k++)
            {
                show.Advance(1.0 / 60);
                while (show.Ears.Out.TryDequeue(out var fr)) shots += fr.Shots.Count(v => v is Whoosh or FaucetVoice);
                foreach (var cp in show.Ears.CaptionsNow()) caps.Add(cp.Text);
            }
            Check("화장실 소리 (C7) 화면: 귀를 화장실에 두면 쉬익 · 쏴가 소리로 나고 자막 '쉬익 — 변기 물 내림' · '쏴 — 손 씻는 물'",
                shots >= 2 && caps.Contains("쉬익 — 변기 물 내림") && caps.Contains("쏴 — 손 씻는 물"),
                $"소리 {shots} · 자막 {string.Join(" / ", caps)}");
        }
    }
}
