using ShipEngine;
using ShipView;
using ShipView.Audio;

namespace Lab;

/// <summary>소리 확인 (--listen): 관전 화면을 화면 1/60초씩 돌리며 귀가 들은 것을 섞어 WAV로 — build/sound/*.wav</summary>
public static partial class Program
{
    /// <summary>--listen [배] [시드] [화면 초] [건너뛸 게임 시간] → build/sound/배-시드.wav</summary>
    private static void Listen(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--listen").Skip(1).ToArray();
        if (rest.Length > 0 && rest[0] == "demo") { ListenDemo(); return; }
        if (rest.Length > 0 && rest[0] == "hear") { ListenHear(); return; }
        if (rest.Length > 0 && rest[0] == "life") { ListenLife(); return; }
        if (rest.Length > 0 && rest[0] == "music") { ListenMusic(); return; }
        if (rest.Length > 0 && rest[0] == "world") { ListenWorld(); return; }
        if (rest.Length > 0 && rest[0] == "wash") { ListenWash(); return; }
        string ship = rest.Length > 0 ? rest[0] : "Didim";
        ulong seed = rest.Length > 1 ? ulong.Parse(rest[1]) : 18;
        double screenSec = rest.Length > 2 ? double.Parse(rest[2]) : 60;
        double skipHours = rest.Length > 3 ? double.Parse(rest[3]) : 0;
        var show = LiveShow.Crewed(ship, seed);
        if (skipHours > 0) { show.Ears.Enabled = false; while (show.Sim.Tick < Time.Hours(skipHours)) show.Advance(1); show.Ears.Enabled = true; }
        var mix = new Mixer();
        int logged = 0;
        show.Ears.Log = m => { if (logged++ < 60) Console.WriteLine("    " + m); };
        const double dt = 1.0 / 60;
        int per = (int)(Dsp.Rate * dt), frames = (int)(screenSec / dt);
        var all = new float[frames * per * 2];
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double mixMs = 0; int maxVoices = 0;
        for (int f = 0; f < frames; f++)
        {
            show.Advance(dt);
            var t0 = sw.Elapsed.TotalMilliseconds;
            mix.Take(show.Ears);
            mix.Render(all.AsSpan(f * per * 2, per * 2), per);
            mixMs += sw.Elapsed.TotalMilliseconds - t0;
            maxVoices = Math.Max(maxVoices, mix.Voices);
            if (f % (60 * 15) == 0) Console.WriteLine($"  화면 {f * dt,5:0}초 · {show.Status()} · 소리 {mix.Voices}개 · 귀 칸 {show.Ears.EarCell} (짙기 {show.Ears.EarThin:0.00})");
        }
        string path = $"build/sound/{ship.ToLowerInvariant()}-{seed}.wav";
        Mixer.WriteWav(path, all);
        double rms = Math.Sqrt(all.Average(x => (double)x * x)), peak = all.Max(MathF.Abs);
        Console.WriteLine($"{path} · {screenSec:0}초 · 크기(RMS) {20 * Math.Log10(rms + 1e-9):0.0}dB · 꼭대기 {peak:0.00} · 섞기 1/60초마다 {mixMs / frames:0.00}ms · 동시에 많아야 {maxVoices}개");
    }

    /// <summary>
    /// 소리 시연 (--listen demo): 시험 배 왼쪽 방에 귀를 두고 차례로 — 외침 · 강철 벽 쾅 · 닫힌 문 너머 쾅 · 불 · 작은 틈 · 큰 구멍 · 경보 · 먼 외판 충돌 · 문 열림.
    /// → build/sound/demo.wav (+ 스펙트로그램은 tools/sound/spectro.py)
    /// </summary>
    private static void ListenDemo()
    {
        var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        var show = new LiveShow(Worlds.Full(lay, 3), "시험호");
        var m = show.Sim.Get<Mind>();
        int who = m.AddPerson(show.Sim, At(3, 4));
        const double dt = 1.0 / 60;
        show.Advance(dt);
        show.Ears.ForceEar = At(5, 3);
        show.SpeedOverride = 1;          // 화면 1초에 1틱 (게임 2.4초) — 불 · 바람이 천천히 자란다
        var snd = show.Sim.Get<Sound>();
        var cues = new (double t, string what, Action act)[]
        {
            (1.0, "같은 방에서 외침", () => snd.Emit(show.Sim, At(7, 3), 80, SoundKind.Voice, 0, who)),
            (3.0, "같은 방 강철 벽 쾅 (5kJ)", () => snd.Emit(show.Sim, At(8, 1), 92, SoundKind.Bang)),
            (5.5, "닫힌 문 너머 옆 방 쾅 (같은 세기)", () => snd.Emit(show.Sim, At(15, 1), 92, SoundKind.Bang)),
            (8.0, "같은 방 침대에 불", () => show.Sim.Get<Chemistry>().Ignite(At(3, 5), 3e6, show.Sim.Causes.Add(CauseKind.Setup, "불씨", show.Sim.Tick))),
            (14.0, "벽에 작은 틈 (3cm²)", () => show.Sim.Get<Field>().SetHole(At(0, 2), 0.0003f)),
            (19.0, "틈이 큰 구멍으로 (150cm²)", () => show.Sim.Get<Field>().SetHole(At(0, 2), 0.015f)),
            (24.0, "주컴퓨터 경보", () => snd.Emit(show.Sim, At(5, 2), 90, SoundKind.Alarm, 0, 9999)),
            (26.0, "먼 통로 외판에 파편 (뼈대로)", () => snd.Emit(show.Sim, At(20, 8), 118, SoundKind.Bang)),
            (28.0, "외판에 금 (뼈대로)", () => snd.Emit(show.Sim, At(18, 8), 85, SoundKind.Crack)),
        };
        var mix = new Mixer();
        int per = (int)(Dsp.Rate * dt), frames = (int)(32 / dt);
        var all = new float[frames * per * 2];
        int next = 0;
        for (int f = 0; f < frames; f++)
        {
            double t = f * dt;
            while (next < cues.Length && t >= cues[next].t) { Console.WriteLine($"  {cues[next].t,5:0.0}초 · {cues[next].what}"); cues[next].act(); next++; }
            show.Advance(dt);
            mix.Take(show.Ears);
            mix.Render(all.AsSpan(f * per * 2, per * 2), per);
        }
        Mixer.WriteWav("build/sound/demo.wav", all);
        Console.WriteLine($"build/sound/demo.wav · 32초 · 꼭대기 {all.Max(MathF.Abs):0.00}");
    }

    /// <summary>
    /// 소리 깊이 시연 (--listen hear — 바퀴 2): 소리 시험 배의 조용한 방에 귀를 두고 — 닫힌 문 너머 기계실 펌프 둘의 웅웅 · 한 펌프가 곧 망가질 만큼 낡아 딱딱 · 끽끽 ·
    /// 기계실의 외침(롬바르 — 시끄러워 크게) · 이 방 의자 불의 우르릉 · 통로 수소 주머니 폭발(결정적 순간 빠르기 — 늦게 닿는 쾅) · 기계실 배터리 열폭주(펑 · 분출 쉬익).
    /// → build/sound/hear.wav · 엔진의 바탕 소음 · 들은 소리를 한 줄씩.
    /// </summary>
    private static void ListenHear()
    {
        var lay = Worlds.Ascii(HearShip, HearLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        var show = new LiveShow(Worlds.Full(lay, 3), "시험호");
        var sim = show.Sim;
        var m = sim.Get<Mind>(); var n = sim.Get<Network>(); var snd = sim.Get<Sound>(); var f = sim.Get<Field>();
        int worker = m.AddPerson(sim, At(5, 4));
        const double dt = 1.0 / 60;
        show.Advance(dt);
        show.Ears.ForceEar = At(15, 4);
        show.SpeedOverride = 1;
        int pump = n.Devices.First(d => d.Def.Role == "pump").Id;
        var bat = n.Devices.First(d => d.Def.Role == "battery");
        var cues = new (double t, string what, Action act)[]
        {
            (0.5, "닫힌 문 너머 기계실: 냉각 펌프 둘", () => { }),
            (4.0, "한 펌프가 곧 망가질 만큼 낡는다 (딱딱 · 끽끽)", () => n.AgeLeft(pump, Part.Mechanical, 3)),
            (12.0, "기계실에서 외침 (시끄러워 크게)", () => snd.Emit(sim, At(5, 4), m.VoiceDb(At(5, 4), true), SoundKind.Voice, 0, worker)),
            (14.0, "이 방 의자에 불", () => sim.Get<Chemistry>().Ignite(At(15, 2), 3e6, sim.Causes.Add(CauseKind.Setup, "불씨", sim.Tick))),
            (24.0, "통로 수소 주머니에 불꽃 — 결정적 순간 빠르기 (늦게 닿는 쾅)", () =>
            {
                show.SpeedOverride = 0.5;
                foreach (int c in new[] { At(20, 7), At(21, 7), At(20, 8), At(21, 8) }) f.AddGas(c, f.T.gH2, 12, f.Temp(c));
                sim.Get<Chemistry>().Ignite(At(20, 7), 2e4, sim.Causes.Add(CauseKind.Setup, "불꽃", sim.Tick));
            }),
            (30.0, "기계실 배터리 열폭주", () => { show.SpeedOverride = 1; f.AddSurfaceHeat(bat.Cell, (460 - f.Tf[bat.Cell]) * Field.SurfaceC); }),
        };
        var mix = new Mixer();
        int logged = 0;
        show.Ears.Log = msg => { if (logged++ < 40) Console.WriteLine("      " + msg); };
        int per = (int)(Dsp.Rate * dt), frames = (int)(40 / dt);
        var all = new float[frames * per * 2];
        int next = 0;
        for (int fr = 0; fr < frames; fr++)
        {
            double t = fr * dt;
            while (next < cues.Length && t >= cues[next].t)
            {
                cues[next].act();
                Console.WriteLine($"  {cues[next].t,5:0.0}초 · {cues[next].what} — 바탕 소음 귀 {snd.Ambient(At(15, 4)):0}dB · 기계실 {snd.Ambient(At(5, 4)):0}dB · 펌프 {n.NoiseDb(pump):0}dB(낡은 몫 +{n.WornDb(pump):0.0})");
                next++;
            }
            show.Advance(dt);
            mix.Take(show.Ears);
            mix.Render(all.AsSpan(fr * per * 2, per * 2), per);
        }
        Mixer.WriteWav("build/sound/hear.wav", all);
        Console.WriteLine($"build/sound/hear.wav · 40초 · 꼭대기 {all.Max(MathF.Abs):0.00} · 일하던 이: {m.Thought(worker)} · 믿음 {string.Join(", ", m[worker].Beliefs.Where(b => b.Conf > 0.2f).Select(b => Mind.BeliefName(b.Kind)))}");
    }

    /// <summary>
    /// 생활 소리 견본 (--listen life — 소리 단계 S2): 시험 배 왼쪽 방에 귀를 두고 차례로 — 컵 · 식기 · 냄비 · 렌치 · 용접 · 망치 · 코골이(가볍게 · 심하게) · 잠꼬대 ·
    /// 기침 · 웃음(둘) · 흥얼거림 · 닫힌 문 너머 옆 방의 망치(먹먹하게). → build/sound/life.wav
    /// </summary>
    private static void ListenLife()
    {
        var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        var show = new LiveShow(Worlds.Full(lay, 3), "시험호");
        var sim = show.Sim;
        var m = sim.Get<Mind>();
        int a = m.AddPerson(sim, At(3, 4)), b = m.AddPerson(sim, At(7, 4));
        const double dt = 1.0 / 60;
        show.Advance(dt);
        show.Ears.ForceEar = At(5, 3);
        show.SpeedOverride = 1;
        var snd = sim.Get<Sound>();
        int bodyA = m[a].C.Body;
        void E(int x, int y, float db, SoundKind k, int actor = -1) => snd.Emit(sim, At(x, y), db, k, 0, actor);
        var cues = new (double t, string what, Action act)[]
        {
            (0.5, "컵 (45dB)", () => E(6, 3, 45, SoundKind.Clink, a)),
            (1.8, "식기 (50dB)", () => E(6, 4, 50, SoundKind.Clink, a)),
            (3.2, "냄비 (58dB)", () => E(7, 2, 58, SoundKind.Clink, b)),
            (5.0, "렌치 (72dB · 3m)", () => E(8, 3, 72, SoundKind.Tool, b)),
            (7.0, "드라이버 (58dB)", () => E(7, 3, 58, SoundKind.Tool, b)),
            (8.8, "용접 (78dB)", () => E(8, 4, 78, SoundKind.Weld, b)),
            (11.0, "망치 (85dB)", () => E(8, 2, 85, SoundKind.Hammer, b)),
            (13.5, "코골이 가볍게 (46dB)", () => E(3, 4, 46, SoundKind.Snore, Sound.BodyActor(bodyA))),
            (16.0, "코골이 심하게 (60dB)", () => E(3, 4, 60, SoundKind.Snore, Sound.BodyActor(bodyA))),
            (18.5, "잠꼬대 (50dB)", () => E(3, 4, 50, SoundKind.SleepTalk, a)),
            (20.5, "기침 (62dB)", () => E(7, 4, 62, SoundKind.Cough, Sound.BodyActor(m[b].C.Body))),
            (22.5, "웃음 둘 (65 · 63dB)", () => { E(6, 4, 65, SoundKind.Laugh, a); E(7, 4, 63, SoundKind.Laugh, b); }),
            (25.0, "흥얼거림 (48dB)", () => E(6, 3, 48, SoundKind.Hum, b)),
            (28.5, "닫힌 문 너머 옆 방의 망치 (85dB)", () => E(15, 2, 85, SoundKind.Hammer)),
        };
        var mix = new Mixer { MusicGain = 0 };
        int per = (int)(Dsp.Rate * dt), frames = (int)(31 / dt);
        var all = new float[frames * per * 2];
        int next = 0;
        for (int f = 0; f < frames; f++)
        {
            double t = f * dt;
            while (next < cues.Length && t >= cues[next].t) { Console.WriteLine($"  {cues[next].t,5:0.0}초 · {cues[next].what}"); cues[next].act(); next++; }
            show.Advance(dt);
            mix.Take(show.Ears);
            mix.Render(all.AsSpan(f * per * 2, per * 2), per);
        }
        Mixer.WriteWav("build/sound/life.wav", all);
        Console.WriteLine($"build/sound/life.wav · 31초 · 꼭대기 {all.Max(MathF.Abs):0.00}");
    }

    /// <summary>
    /// 화장실 물소리 (--listen wash — 5단 C7): 화장실 문 곁에 귀를 두고 — 변기 쉬익 · 세면대 쏴 · 샤워 밸브를 열어 둔 쏴아 (온수기 없이 · 6초) · 잠근 뒤 · 먼 변기 쉬익. → build/sound/wash.wav
    /// </summary>
    private static void ListenWash()
    {
        var legend = new Dictionary<char, string> { ['e'] = "Bed", ['T'] = "Toilet", ['S'] = "Shower", ['k'] = "Sink", ['p'] = "PowerPanel", ['b'] = "Battery", ['w'] = "WaterRecycler" };
        var lay = Worlds.Ascii(WashViewShip, legend);
        var show = new LiveShow(Worlds.Full(lay, 3), "시험호");
        var sim = show.Sim;
        var net = sim.Get<Network>(); var snd = sim.Get<Sound>();
        const double dt = 1.0 / 60;
        show.Advance(dt);
        var toilets = lay.Furniture.Where(x => x.Type == "Toilet").ToArray();
        var sink = lay.Furniture.First(x => x.Type == "Sink"); var shower = lay.Furniture.First(x => x.Type == "Shower");
        int dev = net.Devices.First(d => d.Furniture == shower.Id).Id;
        show.Ears.ForceEar = toilets[0].Cells[0] + lay.W + 1;
        show.SpeedOverride = 1;
        var cues = new (double t, string what, Action act)[]
        {
            (0.5, "변기 쉬익 (72dB · 곁)", () => snd.Emit(sim, toilets[0].Cells[0], Life.FlushDb, SoundKind.Flush)),
            (3.0, "세면대 쏴 (55dB)", () => snd.Emit(sim, sink.Cells[0], Life.FaucetDb, SoundKind.Faucet)),
            (5.5, "샤워 밸브를 연다 (찬물 · 50dB)", () => { net.SetValve(dev, true); net.SetOn(dev, false); }),
            (12.0, "샤워 밸브를 잠근다", () => net.SetValve(dev, false)),
            (14.0, "먼 변기 쉬익", () => snd.Emit(sim, toilets[^1].Cells[0], Life.FlushDb, SoundKind.Flush)),
        };
        var mix = new Mixer { MusicGain = 0 };
        int per = (int)(Dsp.Rate * dt), frames = (int)(17 / dt);
        var all = new float[frames * per * 2];
        int next = 0;
        for (int f = 0; f < frames; f++)
        {
            double t = f * dt;
            while (next < cues.Length && t >= cues[next].t) { Console.WriteLine($"  {cues[next].t,5:0.0}초 · {cues[next].what}"); cues[next].act(); next++; }
            show.Advance(dt);
            mix.Take(show.Ears);
            mix.Render(all.AsSpan(f * per * 2, per * 2), per);
        }
        Mixer.WriteWav("build/sound/wash.wav", all);
        Console.WriteLine($"build/sound/wash.wav · 17초 · 꼭대기 {all.Max(MathF.Abs):0.00}");
    }

    /// <summary>
    /// 음악 견본 (--listen music — 소리 단계 S5): 배 안 음악 (세계의 소리 — 같은 방 · 벽 너머) 다음에 바탕 음악이 따라가는 것.
    /// 0~12초 기타 (치는 이가 기쁘다 — 장조) · 12~24초 같은 기타 (가라앉았다 — 단조 · 느리게) · 24~36초 작은 스피커 노래 · 36~48초 옆 방에서 듣는 기타 (열어 둔 문 너머 — 작고 먹먹하게) /
    /// 48초부터 20초씩 바탕 음악: 낮 · 밤 · 축하 자리 · 추모 모임 · 파편 구간 앞 불안 → build/sound/music.wav
    /// </summary>
    private static void ListenMusic()
    {
        var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        var show = new LiveShow(Worlds.Full(lay, 5), "시험호");
        var m = show.Sim.Get<Mind>();
        int who = m.AddPerson(show.Sim, At(3, 3));
        const double dt = 1.0 / 60;
        show.Advance(dt);
        show.SpeedOverride = 1;
        var mix = new Mixer { MusicGain = 0 };
        int door = lay.Doors.First(d => d.Cell == At(10, 3)).Id;
        var cues = new (double t, string what, Action act)[]
        {
            (0, "같은 방 · 기타 (기쁘다)", () => { show.Ears.ForceEar = At(6, 3); m[who].C.Playing = 2; m[who].C.Joy = 0.8f; m[who].C.Stress = 0; m[who].C.Sad = 0; }),
            (12, "같은 방 · 기타 (가라앉았다)", () => { m[who].C.Joy = 0.1f; m[who].C.Sad = 0.8f; m[who].C.Stress = 0.4f; }),
            (24, "같은 방 · 작은 스피커 노래", () => { m[who].C.Playing = 1; m[who].C.Joy = 0.6f; m[who].C.Sad = 0; m[who].C.Stress = 0; }),
            (36, "옆 방에서 · 기타 (열어 둔 문 너머 — 닫힌 기밀문 너머로는 35dB 깎여 들리지 않는다)", () => { m[who].C.Playing = 2; show.Ears.ForceEar = At(13, 3); }),
            (48, "바탕 음악 · 낮 (14시)", () => { m[who].C.Playing = 0; mix.MusicGain = Mixer.MusicLevel; }),
            (68, "바탕 음악 · 밤 (1시)", () => { }),
            (88, "바탕 음악 · 축하 자리", () => { }),
            (108, "바탕 음악 · 추모 모임", () => { }),
            (128, "바탕 음악 · 파편 구간 하루 앞", () => { }),
        };
        MusicCue Cue(double t) => t < 68 ? new MusicCue { Set = true, Hour = 14 } : t < 88 ? new MusicCue { Set = true, Hour = 1 } : t < 108 ? new MusicCue { Set = true, Hour = 19, Festive = 1, Valence = 0.5f }
            : t < 128 ? new MusicCue { Set = true, Hour = 16, Mourn = 1, Valence = -0.5f } : new MusicCue { Set = true, Hour = 11, Unease = 0.8f, Arousal = 0.3f };
        int per = (int)(Dsp.Rate * dt), frames = (int)(148 / dt);
        var all = new float[frames * per * 2];
        int next = 0;
        for (int f = 0; f < frames; f++)
        {
            double t = f * dt;
            while (next < cues.Length && t >= cues[next].t) { Console.WriteLine($"  {cues[next].t,5:0}초 · {cues[next].what}"); cues[next].act(); next++; }
            if (m[who].C.Playing != 0) m[who].C.PlayedAt = show.Sim.Tick;   // 쉬며 트는 중 (시험 차리기)
            if (t >= 36 && t < 48) show.Sim.Get<Structure>().RequestDoor(door, 1);   // 문을 열어 둔다 (시험 차리기)
            ref var wc = ref m[who].C;   // 그 자리에 머문다 (시험 차리기 — 계획의 몸처럼)
            wc.AssignedGoal = GoalKind.Idle; wc.AssignedArg = Mind.PlanBound; wc.AssignedTarget = At(3, 3); wc.AssignedUntil = show.Sim.Tick + 100;
            wc.Hunger = 0; wc.Thirst = 0; wc.Sleepy = 0; wc.Bored = 0;
            show.Advance(dt);
            mix.Take(show.Ears);
            if (t >= 48) mix.Music.SetCue(Cue(t));
            mix.Render(all.AsSpan(f * per * 2, per * 2), per);
        }
        Mixer.WriteWav("build/sound/music.wav", all);
        Console.WriteLine($"build/sound/music.wav · 148초 · 꼭대기 {all.Max(MathF.Abs):0.00}");
    }

    /// <summary>
    /// 소리 마무리 견본 (--listen world — 소리 단계 S6): 외판 곁 방에서 — 열 수축의 딱 셋 · 주컴퓨터 방송 띵동 · 푸른 행성에 내려 잔잔한 바람 → 모래 폭풍 (외판 너머 · 먹먹하게).
    /// 소리 자막을 켜고 들린 것을 한 줄씩 찍는다 → build/sound/world.wav
    /// </summary>
    private static void ListenWorld()
    {
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        var show = new LiveShow(Worlds.Full(lay, 6), "시험호");
        var s = show.Sim;
        var m = s.Get<Mind>(); var life = s.Get<Life>();
        int who = m.AddPerson(s, At(16, 3));   // 그 방에 깨어 있는 이 (방송 알림음은 깨어 있는 이가 있는 방에서만)
        const double dt = 1.0 / 60;
        show.Advance(dt);
        show.SpeedOverride = 1;
        show.Ears.ForceEar = At(15, 3); show.Ears.Captions = true;
        var mix = new Mixer { MusicGain = 0 };
        var f = s.Get<Field>(); var st = s.Get<Structure>(); var o = s.Get<Outside>();
        int hull = At(15, 0);
        var cues = new (double t, string what, Action act)[]
        {
            (1.0, "외판이 식으며 딱", () => { st.TsSlip[hull] = (float)f.Ts[hull]; f.Ts[hull] -= 14; }),
            (2.2, "또 딱 (이음매)", () => { int c = At(16, 0); st.TsSlip[c] = (float)f.Ts[c]; f.Ts[c] -= 20; }),
            (3.5, "옆 외판 딱", () => { int c = At(13, 0); st.TsSlip[c] = (float)f.Ts[c]; f.Ts[c] -= 30; }),
            (5.0, "주컴퓨터 방송 띵동", () => s.Get<ShipMind>().Broadcast(s, "견본 방송")),
            (9.0, "푸른 행성에 내렸다 — 잔잔한 바람 (외판 너머)", () => o.Land("earthlike", s.Tick)),
            (20.0, "모래 폭풍이 온다", () => { o.Storm = true; o.StormUntil = s.Tick + Time.Hours(10); }),
        };
        int per = (int)(Dsp.Rate * dt), frames = (int)(36 / dt);
        var all = new float[frames * per * 2];
        int next = 0;
        var said = new HashSet<string>();
        for (int k = 0; k < frames; k++)
        {
            double t = k * dt;
            ref var wc = ref m[who].C;   // 제자리 · 깨어 있다 (견본 차리기)
            wc.AssignedGoal = GoalKind.Idle; wc.AssignedArg = Mind.PlanBound; wc.AssignedTarget = At(16, 3); wc.AssignedUntil = s.Tick + 100;
            wc.Hunger = 0; wc.Thirst = 0; wc.Sleepy = 0; wc.Bored = 0; life[wc.Body].Asleep = 0;
            while (next < cues.Length && t >= cues[next].t) { Console.WriteLine($"  {cues[next].t,5:0.0}초 · {cues[next].what}"); cues[next].act(); next++; }
            show.Advance(dt);
            mix.Take(show.Ears);
            mix.Render(all.AsSpan(k * per * 2, per * 2), per);
            foreach (var c in show.Ears.CaptionsNow()) if (said.Add(c.Text + c.Dir)) Console.WriteLine($"        자막: {c.Dir} {c.Text}");
        }
        Mixer.WriteWav("build/sound/world.wav", all);
        Console.WriteLine($"build/sound/world.wav · 36초 · 꼭대기 {all.Max(MathF.Abs):0.00}");
    }

    /// <summary>소리마다 크기 (--voices): 같은 크기(Gain 1)로 만들었을 때 가장 센 0.1초의 RMS · 꼭대기 · 길이 — 소리끼리 기준을 맞춘다 (목표: 가장 센 0.1초 RMS 0.25).</summary>
    internal static (double rms, double peak, double sec) VoiceLevel(Voice v, double maxSec = 4)
    {
        int n = (int)(Dsp.Rate * maxSec), win = Dsp.Rate / 10;
        var x = new float[n]; int len = n;
        for (int i = 0; i < n; i++) { if (v.Done) { len = i; break; } x[i] = v.Next(); }
        double best = 0, peak = 0;
        for (int s = 0; s + win <= Math.Max(len, win); s += win / 2)
        {
            double e = 0; for (int i = s; i < Math.Min(n, s + win); i++) e += (double)x[i] * x[i];
            best = Math.Max(best, Math.Sqrt(e / win));
        }
        for (int i = 0; i < len; i++) peak = Math.Max(peak, Math.Abs(x[i]));
        return (best, peak, len / (double)Dsp.Rate);
    }

    internal static IEnumerable<(string name, Func<Voice> make)> AllVoices() => new (string, Func<Voice>)[]
    {
        ("쾅 강철 5kJ", () => new Hit(Acoustic.Of("steel"), 5, 1)), ("쩍", () => new Crack(Acoustic.Of("steel"), 2)), ("끼익", () => new Creak(Acoustic.Of("steel"), 0.5f, 3)),
        ("외침", () => new Shout(140, false, 4)), ("비명", () => new Shout(140, true, 5)), ("발소리", () => new Step(Acoustic.Of("steel"), 6)), ("문", () => new Door(7)),
        ("경보 띵띵띵", () => new Alarm(false)), ("사이렌", () => new Alarm(true)), ("끽끽", () => new Grind(8)),
        ("컵", () => new Clink(9, 0.8f)), ("식기", () => new Clink(10)), ("냄비", () => new Clink(11, 1.6f)), ("렌치", () => new Ratchet(12)), ("드라이버", () => new Ratchet(13, 1.3f)),
        ("용접", () => new Weld(14)), ("망치", () => new Hammer(15)), ("코골이", () => new Snore(16, 0.5f)), ("잠꼬대", () => new Mumble(140, 17)),
        ("기침", () => new CoughVoice(140, 18)), ("웃음", () => new LaughVoice(140, 19)), ("흥얼", () => new HumVoice(140, 20)), ("낮은 울림", () => new Boom(0.5f)),
        ("기타 (기쁨)", () => Tuned(new Tune(21, 2), 0.6f)), ("기타 (가라앉음)", () => Tuned(new Tune(21, 2), -0.6f)), ("스피커 노래", () => Tuned(new Tune(22, 1), 0.3f)),
        ("딱 (열 수축)", () => new Ping(23)), ("띵동 (방송)", () => new Chime()), ("쉬익 (변기)", () => new Whoosh(24)), ("쏴 (손 씻는 물)", () => new FaucetVoice(25)),
    };

    private static Voice Tuned(Tune t, float mood) { t.Set(mood); return t; }

    private static void VoiceLevels()
    {
        foreach (var (name, make) in AllVoices())
        {
            var (rms, peak, sec) = VoiceLevel(make());
            Console.WriteLine($"  {name,-10} 센 0.1초 RMS {rms:0.000} ({20 * Math.Log10(rms + 1e-9):0.0}dB) · 꼭대기 {peak:0.00} · {sec:0.00}초");
        }
        foreach (var (name, loop) in LoopVoices())
        {
            for (int i = 0; i < Dsp.Rate * 3; i++) loop.Next();   // 따라 오를 때까지
            var (rms, peak, _) = VoiceLevel(loop, 1);
            Console.WriteLine($"  {name,-10} (이어 도는) RMS {rms:0.000} ({20 * Math.Log10(rms + 1e-9):0.0}dB) · 꼭대기 {peak:0.00}");
        }
    }

    internal static IEnumerable<(string name, Voice v)> LoopVoices()
    {
        var room = new RoomTone(1); room.Set(1);
        var jet = new Jet(2); jet.Set(1000, 1);
        var jet2 = new Jet(3); jet2.Set(6000, 1);
        var fire = new Fire(4); fire.Set(250);
        var pump = new Machine("pump", 5); pump.Set(1, 0);
        var hvac = new Machine("hvac", 6); hvac.Set(1, 0);
        var breath = new Breath(7); breath.Set(0.3f, 0.35f);
        var breeze = new Wind(8); breeze.Set(0, 0.2f);
        var storm = new Wind(9); storm.Set(1, 0.9f);
        var spray = new Spray(10); spray.Set(1, 0);
        var sprayBody = new Spray(11); sprayBody.Set(1, 1);
        return new (string, Voice)[] { ("방 공기 A=1", room), ("쉬익 1kHz", jet), ("쉬익 6kHz", jet2), ("불 250kW", fire), ("펌프", pump), ("공조", hvac), ("우주복 숨", breath), ("바람", breeze), ("모래 폭풍", storm), ("샤워 물", spray), ("샤워 물 (사람)", sprayBody) };
    }
}
