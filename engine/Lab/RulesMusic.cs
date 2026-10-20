using ShipEngine;
using ShipView;
using ShipView.Audio;

namespace Lab;

/// <summary>음악 (소리 단계 S5 — docs/SOUND.md 결정 9 · 11 · 12): 쉬는 이가 트는 음악(판단 → 세계의 소리 → 듣는 이 · 자는 이) · 소리 출력 · 바탕 음악이 따라가는 것.</summary>
public static partial class Program
{
    private static void MusicRules()
    {
        if (!Do("음악")) return;
        Console.WriteLine("\n[음악 — 쉬는 이가 트는 음악 · 듣는 이 · 바탕 음악이 따라가는 것]");
        var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);

        // ── 판단: 혼자 쉬는 이 중 악기를 치는 버릇이 있는 이는 악기를 친다 · 곁에 자는 이가 있으면 헤아리는 이는 이어폰을 꽂고 · 헤아리지 않는 이는 그대로 친다 ──
        {
            var s = Worlds.Full(lay, 31);
            var m = s.Get<Mind>(); var life = s.Get<Life>();
            var crowd = Enumerable.Range(0, 8).Select(i => m.AddPerson(s, At(12 + i, 8))).ToList();
            s.Run(2);
            int p = crowd.First(i => m.PlaysInstrument(s, m[i]));
            int sleeper = crowd.First(i => i != p);
            m[p].C.Cell = At(3, 3); life[m[p].C.Body].Cell = At(3, 3);
            byte alone = m.MusicOut(s, m[p]);
            m[sleeper].C.Cell = At(4, 4); life[m[sleeper].C.Body].Cell = At(4, 4); life[m[sleeper].C.Body].Asleep = 1; m[sleeper].C.Goal = GoalKind.Sleep;
            m[p].C.Agree = 0.9f; m[p].C.Consc = 0.8f;
            byte kind = m.MusicOut(s, m[p]);
            m[p].C.Agree = 0.05f; m[p].C.Consc = 0.1f;
            byte rude = m.MusicOut(s, m[p]);
            Check("음악(판단): 악기를 치는 이는 혼자면 악기를 치고 · 곁에 자는 이가 있으면 헤아리는 이는 이어폰을 꽂는다 · 헤아리지 않는 이는 그대로 친다",
                alone == 2 && kind == 0 && rude == 2, $"혼자 {alone} · 곁에 자는 이(헤아림) {kind} · 곁에 자는 이(헤아리지 않음) {rude}  (0 이어폰 · 1 스피커 · 2 악기)");
        }

        // ── 세계: 쉬며 음악을 트는 이(계획 — 쉬기 · 음악)는 10초마다 음악 소리(66dB)를 낸다 · 곁에 잠든 이가 있으면 헤아리는 이는 소리를 내지 않는다 ──
        {
            (long played, long emitted) Run(bool sleeperNear)
            {
                var s = Worlds.Full(lay, 32);
                var m = s.Get<Mind>(); var life = s.Get<Life>();
                var crowd = Enumerable.Range(0, 8).Select(i => m.AddPerson(s, At(12 + i, 8))).ToList();
                s.Run(2);
                int p = crowd.First(i => m.PlaysInstrument(s, m[i]));
                int other = crowd.First(i => i != p);
                m[p].C.Cell = At(3, 3); life[m[p].C.Body].Cell = At(3, 3); m[p].C.Agree = 0.9f; m[p].C.Consc = 0.8f;
                if (sleeperNear) { m[other].C.Cell = At(4, 4); life[m[other].C.Body].Cell = At(4, 4); }
                long e0 = s.Get<Sound>().Logged, p0 = m.MusicPlayed;
                for (int t = 0; t < 40; t++)
                {
                    ref var c = ref m[p].C;
                    if (c.Goal != GoalKind.Relax || c.Hobby != (byte)Hobby.Music)
                    {   // 쉬기 · 음악 계획을 차린다 (시험 차리기 — 고르는 것은 '쉼' 시험이 본다)
                        m[p].Plan.Clear(); m[p].Plan.Add(new PlanStep { Kind = StepKind.Use, Arg = (byte)UseKind.Relax, Value = Time.Minutes(45), Cell = c.Cell, Target = (int)Hobby.Music });
                        c.Goal = GoalKind.Relax; c.Hobby = (byte)Hobby.Music; c.StepIndex = 0; c.StepStart = s.Tick;
                    }
                    c.Hunger = 0; c.Thirst = 0; c.Sleepy = 0; c.Bored = 0.8f;
                    if (sleeperNear) { life[m[other].C.Body].Asleep = 1; m[other].C.Sleepy = 0.9f; m[other].C.Goal = GoalKind.Sleep; }
                    s.Step();
                }
                long emitted = s.Get<Sound>().LogSince(e0).Count(e => e.Kind == SoundKind.Music && e.Actor == p);
                return (m.MusicPlayed - p0, emitted);
            }
            var free = Run(false); var hush = Run(true);
            Check("음악(세계): 쉬며 악기를 치는 이는 10초마다 음악 소리를 세계에 낸다 (100초에 여러 번) · 곁에 잠든 이가 있으면 헤아리는 이는 이어폰 — 소리가 없다",
                free.emitted >= 5 && hush.emitted == 0, $"혼자: 낸 음악 소리 {free.emitted} / 곁에 잠든 이: {hush.emitted}");
        }

        // ── 듣는 이: 깨어 쉬는 이는 들려오는 음악에 기분이 나아지고 (같은 씨앗 · 음악 없이와 견줌) · 얕게 자던 이는 깨며 그 사람을 원망한다 ──
        {
            (float joy, float stress, long enjoyed, bool woke, string why) Run(bool music, bool sleep)
            {
                var s = Worlds.Full(lay, 33);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var snd = s.Get<Sound>();
                int player = m.AddPerson(s, At(3, 3)), listener = m.AddPerson(s, sleep ? At(4, 3) : At(6, 3));
                s.Run(2);
                long f0 = s.Facts.Total, e0 = m.MusicEnjoyed;
                for (int t = 0; t < Time.Minutes(20); t++)
                {
                    foreach (int i in new[] { player, listener })
                    {
                        ref var c = ref m[i].C; c.Hunger = 0; c.Thirst = 0; c.Bored = 0;
                        if (i == listener && sleep && t < 2) { life[c.Body].Asleep = 1; c.Sleepy = 0.05f; }
                        else if (i == listener && !sleep) { c.Sleepy = 0; c.Stress = Math.Max(c.Stress, t == 0 ? 0.4f : 0); }
                    }
                    if (music && t % Mind.MusicEvery == 0) snd.Emit(s, m[player].C.Cell, Mind.InstrumentDb, SoundKind.Music, 0, player);
                    s.Step();
                }
                var woke = s.Facts.Since(f0).FirstOrDefault(f => f.Kind == FactKind.Woke && f.Actor == listener);
                string why = woke.Cause != 0 ? s.Causes.Story(woke.Cause) : "-";
                return (m[listener].C.Joy, m[listener].C.Stress, m.MusicEnjoyed - e0, woke.Kind == FactKind.Woke, why);
            }
            var with = Run(true, false); var without = Run(false, false);
            Check("음악(듣는 이): 깨어 있는 이는 3m 곁에서 들려오는 기타에 20분이면 기쁨이 오르고 긴장이 풀린다 (음악 없이와 견줌)",
                with.enjoyed > 50 && with.joy > without.joy + 0.02f && with.stress < without.stress - 0.02f,
                $"기쁨 {without.joy:0.00} → {with.joy:0.00} · 긴장 {without.stress:0.00} → {with.stress:0.00} · 즐긴 번 {with.enjoyed}");
            var night = Run(true, true);
            Check("음악(잠): 곁에서 얕게 자던 이는 음악 소리에 깨고 · 깬 까닭에 친 사람의 음악 소리가 남는다 (⑨가 원망한다)",
                night.woke && night.why.Contains("음악 소리"), $"깸 {night.woke} · 까닭 \"{night.why}\"");
        }

        // ── 소리 출력: 같은 방에서는 크고 밝게 · 닫힌 기밀문 너머로는 들리지 않고 · 열어 둔 문 너머로는 작고 먹먹하게 · 치는 이의 기분이 곡에 실린다 ──
        {
            var show = new LiveShow(Worlds.Full(lay, 34), "시험호");
            var m = show.Sim.Get<Mind>();
            int p = m.AddPerson(show.Sim, At(3, 3));
            const double dt = 1.0 / 60;
            show.Advance(dt);
            int door = lay.Doors.First(d => d.Cell == At(10, 3)).Id;
            void Pin()
            {   // 그 자리에 머문다 (시험 차리기 — 계획의 몸처럼)
                ref var wc = ref m[p].C;
                wc.AssignedGoal = GoalKind.Idle; wc.AssignedArg = Mind.PlanBound; wc.AssignedTarget = At(3, 3); wc.AssignedUntil = show.Sim.Tick + 100;
                wc.Hunger = 0; wc.Thirst = 0; wc.Sleepy = 0; wc.Bored = 0;
            }
            LoopUpdate? Tune(int ear)
            {
                Pin();
                show.Ears.ForceEar = ear;
                m[p].C.Playing = 2; m[p].C.PlayedAt = show.Sim.Tick;
                while (show.Ears.Out.TryDequeue(out _)) { }
                show.Ears.Remap(ear);
                show.Advance(dt);
                LoopUpdate? got = null;
                while (show.Ears.Out.TryDequeue(out var fr)) foreach (var u in fr.Loops) if (u.Kind == LoopKind.Tune) got = u;
                return got;
            }
            m[p].C.Joy = 0.9f; m[p].C.Stress = 0; m[p].C.Sad = 0;
            var same = Tune(At(6, 3));
            var shut = Tune(At(13, 3));
            show.Sim.Get<Structure>().RequestDoor(door, 1);
            for (int i = 0; i < 4; i++) { Pin(); show.Sim.Step(); }
            var open = Tune(At(13, 3));
            m[p].C.Joy = 0.05f; m[p].C.Sad = 0.9f; m[p].C.Stress = 0.5f;
            var sad = Tune(At(6, 3));
            Check("음악(소리 출력): 같은 방은 크고 밝게 · 닫힌 기밀문 너머엔 들리지 않고 · 열어 둔 문 너머엔 작고 먹먹하게 · 기쁜 이는 장조(기분 +) · 가라앉은 이는 단조(기분 −)",
                same is { } a && shut == null && open is { } o && o.Gain < a.Gain * 0.6f && o.Cutoff < a.Cutoff && a.B > 0.3f && sad is { } d && d.B < -0.3f,
                $"같은 방 크기 {same?.Gain:0.000} 거름 {same?.Cutoff:0} / 닫힌 문 {(shut == null ? "없음" : "들림")} / 열린 문 {open?.Gain:0.000} 거름 {open?.Cutoff:0} / 기분 {same?.B:0.0} → {sad?.B:0.0}");
        }

        // ── 바탕 음악: 낮 · 밤 · 추모 · 축하 · 위험 · 불안 · 고른 사람의 기분 ──
        {
            (int set, float sec, float spark, float cut, float tension) Play(MusicCue cue, string mood = "")
            {
                var sc = new Score(7);
                if (mood.Length > 0) sc.SetMood(mood);
                sc.SetCue(cue);
                for (int i = 0; i < Dsp.Rate * 26; i++) sc.Next();
                return (sc.ChordSet, sc.ChordSec, sc.SparkChance, sc.Cut, sc.Tension);
            }
            var day = Play(new MusicCue { Set = true, Hour = 14 });
            var night = Play(new MusicCue { Set = true, Hour = 1 });
            var mourn = Play(new MusicCue { Set = true, Hour = 14, Mourn = 1 });
            var fest = Play(new MusicCue { Set = true, Hour = 19, Festive = 1 });
            var festDanger = Play(new MusicCue { Set = true, Hour = 19, Festive = 1 }, "위험");
            var uneasy = Play(new MusicCue { Set = true, Hour = 14, Unease = 0.9f });
            var sadP = Play(new MusicCue { Set = true, Hour = 14, Valence = -0.6f });
            var gladP = Play(new MusicCue { Set = true, Hour = 14, Valence = 0.6f });
            Check("바탕 음악: 낮은 잔잔 · 밤은 화음이 1.5배 느리고 어둡고 맑은 음이 드물다 · 추모는 애가(맑은 음 없음) · 축하는 밝음 — 위험이면 축하 중에도 어두움 · 파편 구간 앞은 어두움 · 고른 이가 가라앉으면 애가 · 기쁘면 밝음",
                day.set == Score.SetCalm && night.set == Score.SetCalm && night.sec > day.sec * 1.4f && night.cut < day.cut && night.spark < day.spark * 0.5f
                && mourn.set == Score.SetLament && mourn.spark == 0 && fest.set == Score.SetBright && festDanger.set == Score.SetDark
                && uneasy.set == Score.SetDark && uneasy.tension > 0.15f && sadP.set == Score.SetLament && gladP.set == Score.SetBright,
                $"낮 {day.set}·{day.sec:0}초·맑은 음 {day.spark:0.00}·거름 {day.cut:0} / 밤 {night.set}·{night.sec:0}초·{night.spark:0.00}·{night.cut:0} / 추모 {mourn.set} / 축하 {fest.set} · 축하+위험 {festDanger.set} / 불안 {uneasy.set}·긴장 {uneasy.tension:0.00} / 가라앉음 {sadP.set} · 기쁨 {gladP.set}  (0 잔잔 · 1 어두움 · 2 밝음 · 3 애가)");
        }

        // ── 바탕 음악이 읽는 세계: 추모 모임 · 파편 구간 사흘 앞 · 고른 사람의 기분 ──
        {
            var show = new LiveShow(Worlds.Full(lay, 35), "시험호");
            var m = show.Sim.Get<Mind>();
            int p = m.AddPerson(show.Sim, At(3, 3));
            show.Advance(1.0 / 60);
            var w = show.Sim;
            var calm = Acoustics.CueOf(show, -1);
            w.Get<Society>().Meeting = new Council { Kind = CouncilKind.Memorial, Room = -1, Cell = At(3, 3), Caller = p, Subject = -1, Accuser = -1 };
            w.Get<ShipMind>().Calendar.Add(new ShipMind.CalEvent { From = w.Tick + Time.Days(1), To = w.Tick + Time.Days(2), Zone = 0 });
            m[p].C.Joy = 0.9f; m[p].C.Stress = 0; m[p].C.Fear = 0; m[p].C.Sad = 0;
            var cue = Acoustics.CueOf(show, p);
            Check("바탕 음악(세계): 추모 모임이 열리면 애가 · 파편 구간 하루 앞이면 불안(0.4~1) · 고른 사람이 기쁘면 기분 + · 시계를 따른다",
                calm.Mourn == 0 && cue.Mourn == 1 && cue.Unease > 0.4f && cue.Unease < 1 && cue.Valence > 0.5f && Math.Abs(cue.Hour - (w.Tick % Time.TicksPerDay) / (float)Time.TicksPerHour) < 0.01f,
                $"추모 {calm.Mourn} → {cue.Mourn} · 불안 {cue.Unease:0.00} · 기분 {cue.Valence:0.00} · 시각 {cue.Hour:0.0}");
            w.Get<Society>().Meeting = default;
        }
    }
}
