using ShipEngine;

namespace Lab;

/// <summary>생활 소리 (소리 단계 S2 — docs/SOUND.md 결정 8 · 13): 잠 깊이만큼의 깨우는 문턱 · 발소리 · 코골이 · 기침 · 식기 · 공구 · 흥얼거림.</summary>
public static partial class Program
{
    private static void LifeSoundRules()
    {
        if (!Do("생활 소리")) return;
        Console.WriteLine("\n[생활 소리 — 잠 깊이 · 발소리 · 코골이 · 기침 · 식기 · 공구 · 흥얼거림]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);

        // 자는 사람 하나를 칸 cell에 눕힌다 (졸림 sleepy — 남은 잠의 압력 · 깊이)
        int Sleeper(Sim s, int cell, float sleepy)
        {
            var m = s.Get<Mind>(); var life = s.Get<Life>();
            int p = m.AddPerson(s, cell);
            life[m[p].C.Body].Asleep = 1; m[p].C.Sleepy = sleepy; m[p].C.Hunger = 0; m[p].C.Thirst = 0;
            return p;
        }
        bool WokeSince(Sim s, int who, long f0) => s.Facts.Since(f0).Any(f => f.Kind == FactKind.Woke && f.Actor == who);

        // ── 잠 깊이: 같은 소리(2m에서 62dB 말 — 귀에 55dB 남짓)에 아침 가까이 얕게 자던 이는 깨고 · 잠든 지 얼마 안 된 깊은 잠은 깨지 않는다 ──
        {
            (bool woke, float threshold) Run(float sleepy)
            {
                var s = Worlds.Full(lay, 5);
                int p = Sleeper(s, At(3, 5), sleepy);
                s.Run(3);
                var m = s.Get<Mind>(); s.Get<Life>()[m[p].C.Body].Asleep = 1;
                float th = m.WakeDb(m[p]);
                long f0 = s.Facts.Total;
                s.Get<Sound>().Emit(s, At(5, 5), 62, SoundKind.Voice);
                s.Run(4);
                return (WokeSince(s, p, f0), th);
            }
            var light = Run(0.05f); var deep = Run(0.9f);
            Check("잠 깊이: 같은 말소리(귀에 55dB 남짓)에 얕은 잠(아침 가까이)은 깨고 · 깊은 잠(잠든 처음)은 깨지 않는다 — 깨우는 문턱 50 ~ 65dB",
                light.woke && !deep.woke && light.threshold < 53 && deep.threshold > 62 && Sound.BodyOf(Mind.Computer) < 0 && Sound.BodyActor(0) != Mind.Computer,   // 몸 소리 번호가 주컴퓨터(−2)와 겹치지 않는다 (겹쳐 0번 몸이 경보를 제 소리로 여겼다)
                $"얕은 잠 문턱 {light.threshold:0}dB · 깸 {light.woke} / 깊은 잠 문턱 {deep.threshold:0}dB · 깸 {deep.woke}");
        }

        // ── 발소리: 배고픈 이가 방을 나와 통로를 걸어 배식기로 간다 — 문 앞 통로 바닥에서 얕게 자던 이는 발소리에 깨고 · 깊이 자던 이는 그대로 잔다 ──
        {
            (bool woke, long steps) Run(float sleepy)
            {
                var s = Worlds.Full(lay, 6);
                var m = s.Get<Mind>();
                int sleeper = Sleeper(s, At(14, 8), sleepy);
                int walker = m.AddPerson(s, At(17, 3));
                m[walker].C.Hunger = 1; m[walker].C.Sleepy = 0;
                long f0 = s.Facts.Total, st0 = m.StepsHeard;
                for (int t = 0; t < Time.Minutes(6); t++) { s.Get<Life>()[m[sleeper].C.Body].Asleep = 1; s.Step(); if (WokeSince(s, sleeper, f0)) break; }
                return (WokeSince(s, sleeper, f0), m.StepsHeard - st0);
            }
            var light = Run(0.02f); var deep = Run(0.9f);
            Check("발소리: 통로를 지나는 발소리(55dB)에 문 앞에서 얕게 자던 이는 깨고 · 깊이 자던 이는 깨지 않는다 (발소리는 곁에 자는 이가 있을 때만 셈한다)",
                light.woke && !deep.woke && light.steps > 0 && deep.steps > 0,
                $"얕은 잠: 깸 {light.woke} · 발소리 {light.steps}번 / 깊은 잠: 깸 {deep.woke} · 발소리 {deep.steps}번");
        }

        // ── 코골이: 코를 고는 이(몸의 체질)와 한 방에서 자면 잠이 얕다 (잠자리 소리 35dB 넘게 — ⑦ 잠의 질) · 고는 사람이 없으면 그대로 ──
        {
            (ulong seed, double snorer, double other)? Find(Func<double, double, bool> want)
            {
                for (ulong seed = 1; seed < 80; seed++)
                {
                    var s = Worlds.Full(lay, seed);
                    var life = s.Get<Life>();
                    double a = life.SnoreTrait(s, 0), b = life.SnoreTrait(s, 1);
                    if (want(a, b)) return (seed, b, a);
                }
                return null;
            }
            var loud = Find((a, b) => a == 0 && b > 0.6); var quiet = Find((a, b) => a == 0 && b == 0);
            (float loss, float noise, long snores) Run(ulong seed)
            {
                var s = Worlds.Full(lay, seed);
                var m = s.Get<Mind>(); var life = s.Get<Life>();
                int listener = Sleeper(s, At(3, 5), 0.5f), snorer = Sleeper(s, At(5, 5), 0.5f);
                float maxNoise = 0;
                for (int t = 0; t < Time.Minutes(40); t++)
                {
                    life[m[listener].C.Body].Asleep = 1; life[m[snorer].C.Body].Asleep = 1; m[listener].C.Sleepy = 0.5f; m[snorer].C.Sleepy = 0.5f;
                    s.Step();
                    maxNoise = Math.Max(maxNoise, m[listener].C.SleepNoise);
                }
                return (m[listener].C.SleepLoss, maxNoise, life.Snores);
            }
            if (loud == null || quiet == null) Check("코골이: 시험할 씨앗을 찾았다", false, "없음");
            else
            {
                var a = Run(loud.Value.seed); var b = Run(quiet.Value.seed);
                Check("코골이: 코를 고는 이(체질 열에 넷 · 1m 44~60dB)와 2m 곁에서 자면 잠자리 소리가 35dB를 넘어 잠이 얕아진다 · 고는 이가 없으면 조용하다",
                    a.snores > 50 && b.snores == 0 && a.noise > 38 && a.loss > b.loss + 0.05f,
                    $"고는 이 곁: 잠자리 소리 {a.noise:0}dB · 잠 설침 {a.loss:0.00} · 코골이 {a.snores}번 / 조용한 방: {b.noise:0}dB · {b.loss:0.00} · {b.snores}번 (체질 {loud.Value.snorer:0.00})");
            }
        }

        // ── 기침: 연기(그을음 3,000ppm)를 마시는 이는 기침한다 (소리 · 몸) · 맑은 공기에서는 하지 않는다 · 앓는 이는 맑은 공기에서도 가끔 ──
        {
            (long coughs, long logged) Run(bool smoke, bool ill)
            {
                var s = Worlds.Full(lay, 7);
                var m = s.Get<Mind>(); var f = s.Get<Field>(); var life = s.Get<Life>();
                int p = m.AddPerson(s, At(6, 3));
                if (ill) { ref var b = ref life[m[p].C.Body]; b.Ill = 2; b.IllH = 1; }
                var roomCells = Enumerable.Range(0, lay.N).Where(c => lay.IsSpace(c)).ToArray();   // 배 전체 (연기를 피해 나가도 연기 속)
                void TopUp() { foreach (int c in roomCells) { double want = 0.003 * f.Moles(c) - f.Gas(f.T.gSoot, c); if (want > 0) f.AddGas(c, f.T.gSoot, want, f.Temp(c)); } }   // 불이 계속 연기를 낸다 (주컴퓨터의 환기가 걷어 내는 만큼 채운다 · 사람은 연기를 피해 방을 나간다)
                long l0 = s.Get<Sound>().Logged;
                for (int t = 0; t < Time.Minutes(smoke ? 3 : 30); t++) { if (smoke) TopUp(); s.Step(); }
                if (Debug && smoke) { var bb = life[m[p].C.Body]; Console.WriteLine($"    그을음 머리 높이 {f.HeadPpm(f.T.gSoot, bb.Cell, bb.Low != 0):0}ppm · 기침 잦기 {life.CoughRate(bb):0}/시"); }
                long logged = s.Get<Sound>().LogSince(l0).Count(e => e.Kind == SoundKind.Cough && Sound.BodyOf(e.Actor) == m[p].C.Body);
                return (life.Coughs, logged);
            }
            var smoky = Run(true, false); var clean = Run(false, false); var sick = Run(false, true);
            Check("기침: 연기 속(그을음 3,000ppm)에서는 몇 분에 여러 번 기침하고(62dB) · 맑은 공기에서는 하지 않는다 · 앓는 이는 맑은 공기에서도 30분에 한두 번 넘게",
                SmokyOk(smoky) && clean.coughs == 0 && sick.coughs >= 2,
                $"연기 3분: {smoky.coughs}번 (소리 {smoky.logged}) / 맑음 30분: {clean.coughs}번 / 앓음 30분: {sick.coughs}번");
            static bool SmokyOk((long coughs, long logged) x) => x.coughs >= 4 && x.logged == x.coughs;
        }

        // ── 일하는 소리: 배고픈 이가 먹으면 식기 소리 · 덜컹거리는 펌프의 볼트를 조이는 로봇은 렌치 소리를 낸다 (③ 소리 장의 기록 · 낸 이) ──
        {
            var s = Worlds.Full(lay, 8);
            var m = s.Get<Mind>();
            int eater = m.AddPerson(s, At(6, 7));
            m[eater].C.Hunger = 1;
            long l0 = s.Get<Sound>().Logged;
            s.Run(Time.Minutes(30));
            int clinks = s.Get<Sound>().LogSince(l0).Count(e => e.Kind == SoundKind.Clink && e.Actor == eater);
            var vlay = Worlds.Ascii(VibShip, VibLegend);
            var v = Worlds.Full(vlay, 4);
            var net = v.Get<Network>();
            int pump = net.Devices.First(d => d.Type == "CoolantPump").Id;
            v.Run(25);
            int robot = v.Get<Mind>().AddRobot(v, Worlds.At(vlay, 12, 1), AgentKind.Robot, Worlds.At(vlay, 12, 1));
            net.Loose[pump] = 0.999f;
            long v0 = v.Get<Sound>().Logged;
            int tools = 0;
            for (int t = 0; t < Time.Minutes(40); t++) { v.Step(); foreach (var e in v.Get<Sound>().LogSince(v0)) if (e.Kind == SoundKind.Tool && e.Actor == robot) tools++; v0 = v.Get<Sound>().Logged; }
            Check("일하는 소리: 먹는 이의 식기 소리(50dB · 3틱마다) · 볼트를 조이는 로봇의 렌치 소리(72dB · 2틱마다) — 판단이 정한 일이 소리를 낸다",
                clinks >= 2 && tools >= 2 && net.Tightened == 1,
                $"식기 {clinks}번 · 렌치 {tools}번 · 조임 {net.Tightened}");
        }

        // ── 흥얼거림: 기분 좋은 이(기쁨 0.9 · 긴장 0)가 혼자 있으면 가끔 흥얼거린다 (48dB) · 우울한 이는 흥얼거리지 않는다 ──
        {
            long Run(float joy)
            {
                // 흥얼거리는 버릇이 있는 이 (사람마다 — 씨앗에서): 버릇이 있는 씨앗을 고른다
                for (ulong seed = 1; seed < 40; seed++)
                {
                    var s = Worlds.Full(lay, seed);
                    var m = s.Get<Mind>();
                    int p = m.AddPerson(s, At(17, 3));
                    m.SetPersonality(p, 1, 0.5f, 1, 0.5f, 0.2f);
                    if (Rng.Unit(s.Seed, Rng.Key("흥얼 버릇"), 0, p) > 0.65) continue;
                    long h0 = m.Hums;
                    for (int t = 0; t < Time.Hours(1); t++) { m[p].C.Joy = joy; m[p].C.Stress = 0; m[p].C.Fear = 0; s.Step(); }
                    return m.Hums - h0;
                }
                return -1;
            }
            long happy = Run(0.9f), sad = Run(0.1f);
            Check("흥얼거림: 기분 좋은 이가 혼자 있으면 한 시간에 몇 번 흥얼거리고 · 기분이 가라앉은 이는 하지 않는다",
                happy >= 3 && sad == 0, $"기쁨 0.9: {happy}번 · 기쁨 0.1: {sad}번");
        }
    }
}
