using ShipEngine;
using ShipView;
using ShipView.Audio;

namespace Lab;

/// <summary>소리 마무리 (소리 단계 S6 — docs/SOUND.md 결정 7 · 14 · 15 · 나눔 '행성 바깥 소리'): 열 수축의 딱 · 외판 너머 바람 · 방송 알림음 · 소리 자막.</summary>
public static partial class Program
{
    private static void SoundWorldRules()
    {
        if (!Do("소리 마무리")) return;
        Console.WriteLine("\n[소리 마무리 — 열 수축의 딱 · 외판 너머 바람 · 방송 알림음 · 소리 자막]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";

        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);

        // ── 열 수축의 딱: 외판 겉면이 3K 바뀌면 그대로 · 25K 바뀌면 딱 (44~56dB) · 곁의 사람은 '벽이 버틴다'로 듣지 않는다 ──
        {
            var s = Worlds.Full(lay, 41);
            var st = s.Get<Structure>(); var f = s.Get<Field>(); var snd = s.Get<Sound>(); var m = s.Get<Mind>();
            int p = m.AddPerson(s, At(1, 2));
            s.Run(3);
            int hull = At(0, 2);
            st.TsSlip[hull] = (float)f.Ts[hull];
            float base0 = st.TsSlip[hull];
            f.Ts[hull] += 3; s.Step();
            bool quiet = st.TsSlip[hull] == base0;
            long e0 = snd.Logged;
            f.Ts[hull] += 25; s.Step();
            var tick = snd.LogSince(e0).Where(e => e.Kind == SoundKind.Tick && m.Dist(e.Cell, At(1, 2)) <= 1).ToList();
            bool strain = m[p].Beliefs.Any(b => b.Kind == BeliefKind.Strain);
            Check("열 수축: 외판 겉면이 3K 바뀌면 조용하고 · 25K 바뀌면 딱 (44~56dB) · 곁의 사람은 벽이 버틴다고 믿지 않는다 (끼익과 다르다)",
                quiet && tick.Count == 1 && tick[0].Db >= 44 && tick[0].Db <= 56 && !strain,
                $"3K 조용 {quiet} · 25K 딱 {tick.Count}번 ({(tick.Count > 0 ? tick[0].Db : 0):0}dB) · 버틴다는 믿음 {strain}");
        }

        // (평소의 딱 — 하루에 몇 번인지는 '평시 하루' 묶음이 본다)

        // ── 외판 너머 바람: 푸른 행성 모래 폭풍은 외판 곁 방에서 30dB 넘게 들리고 · 잔잔한 날 · 붉은 사막(옅은 대기)의 폭풍은 거의 안 들리고 · 우주엔 없다 ──
        (float outIn, float amb, LiveShow show) Storm(string? planet, bool storm)
        {
            var show = new LiveShow(Worlds.Full(lay, 42), "시험호");
            var s = show.Sim;
            var o = s.Get<Outside>();
            if (planet != null) { o.Land(planet, s.Tick); if (storm) { o.Storm = true; o.StormUntil = s.Tick + Time.Hours(10); } }
            s.Run(Time.Minutes(3));
            var snd = s.Get<Sound>();
            int room = lay.Room[At(15, 3)];   // 기계가 없는 조용한 방 (외판 곁)
            return (snd.OutsideIn(room), snd.RoomAmbient(room), show);
        }
        var earthStorm = Storm("earthlike", true); var earthCalm = Storm("earthlike", false); var marsStorm = Storm("marslike", true); var space = Storm(null, false);
        Check("외판 너머 바람: 푸른 행성 모래 폭풍은 외판 곁 방에 30dB 넘게 들어오고 방의 바탕 소음이 오른다 · 잔잔한 날 · 붉은 사막의 폭풍(0.6kPa)은 15dB 아래 · 우주엔 없다",
            earthStorm.outIn > 30 && earthStorm.amb > earthCalm.amb + 3 && earthCalm.outIn < 15 && marsStorm.outIn < 15 && space.outIn == 0,
            $"푸른 행성 폭풍 {earthStorm.outIn:0}dB (바탕 {earthCalm.amb:0} → {earthStorm.amb:0}dB) · 잔잔 {earthCalm.outIn:0} · 붉은 사막 폭풍 {marsStorm.outIn:0} · 우주 {space.outIn:0}");
        {
            var show = earthStorm.show;
            show.Ears.ForceEar = At(15, 3); show.Ears.Captions = true;
            show.Advance(1.0 / 60);
            LoopUpdate? wind = null;
            while (show.Ears.Out.TryDequeue(out var fr)) foreach (var u in fr.Loops) if (u.Kind == LoopKind.Wind) wind = u;
            var caps = show.Ears.CaptionsNow();
            Check("외판 너머 바람(소리 출력): 그 방에서 들으면 바람 소리(모래 · 먹먹하게 700Hz 아래)가 나고 자막에 '모래 폭풍 — 외판 너머'",
                wind is { } w && w.A == 1 && w.Cutoff <= 700 && w.Gain > 0 && caps.Any(c => c.Text.StartsWith("모래 폭풍")),
                $"바람 {(wind == null ? "없음" : $"모래 {wind.Value.A:0} · 세기 {wind.Value.B:0.00} · 거름 {wind.Value.Cutoff:0}Hz · 크기 {wind.Value.Gain:0.000}")} · 자막 {string.Join(" / ", caps.Select(c => c.Text))}");
        }

        // ── 방송 알림음: 깨어 있는 이만 있는 방에서 띵동 · 자는 이가 있는 방은 울리지 않는다 (자막은 화면에) · 아무도 없는 방도 조용 ──
        {
            var show = new LiveShow(Worlds.Full(lay, 43), "시험호");
            var s = show.Sim;
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var cm = s.Get<ShipMind>(); var snd = s.Get<Sound>();
            int awake = m.AddPerson(s, At(3, 3)), sleeper = m.AddPerson(s, At(15, 3)), friend = m.AddPerson(s, At(17, 3));
            void Pin()
            {   // 제자리에 머문다 (시험 차리기 — 계획의 몸처럼)
                foreach (var (i, cell) in new[] { (awake, At(3, 3)), (sleeper, At(15, 3)), (friend, At(17, 3)) })
                {
                    ref var c = ref m[i].C;
                    c.AssignedGoal = GoalKind.Idle; c.AssignedArg = Mind.PlanBound; c.AssignedTarget = cell; c.AssignedUntil = s.Tick + 100;
                    c.Hunger = 0; c.Thirst = 0; c.Bored = 0;
                }
            }
            for (int i = 0; i < 12; i++) { Pin(); s.Step(); }
            life[m[sleeper].C.Body].Asleep = 1; m[sleeper].C.Sleepy = 0.6f;
            foreach (int i in new[] { awake, friend }) { life[m[i].C.Body].Asleep = 0; m[i].C.Sleepy = 0; }   // 깨어 있다 (시험 차리기)
            show.Advance(1.0 / 60);
            long e0 = snd.Logged, f0 = s.Facts.Total, held0 = cm.ChimesHeld;
            foreach (int i in new[] { awake, friend }) { life[m[i].C.Body].Asleep = 0; m[i].C.Sleepy = 0; }
            cm.Broadcast(s, "시험 방송 — 내일 아침 물탱크를 점검합니다");
            for (int i = 0; i < 3; i++) { Pin(); life[m[sleeper].C.Body].Asleep = 1; s.Step(); }
            var chimes = snd.LogSince(e0).Where(e => e.Kind == SoundKind.Chime).ToList();
            bool woke = s.Facts.Since(f0).Any(x => x.Kind == FactKind.Woke && x.Actor == sleeper);
            show.Advance(1.0 / 60);
            var frame = show.Render();
            Check("방송 알림음: 깨어 있는 이만 있는 방에서 띵동 (60dB) · 자는 이가 있는 방은 울리지 않아 깨지 않는다 · 화면엔 방송 자막",
                chimes.Count == 1 && m.RoomOf(chimes[0].Cell) == m.RoomOf(At(3, 3)) && cm.ChimesHeld - held0 == 1 && !woke && frame.Broadcast.Contains("물탱크"),
                $"띵동 {chimes.Count}번 ({string.Join(",", chimes.Select(c => lay.Rooms[m.RoomOf(c.Cell)].Name))}) · 참은 방 {cm.ChimesHeld - held0} · 자던 이 깸 {woke} · 자막 \"{frame.Broadcast}\"");
        }

        // ── 소리 자막: 켜면 들린 소리마다 무엇이 · 어느 쪽에서 (동쪽 쾅 → '→ 쾅' · 서쪽 딸그락 두 번 → '← 딸그락 ×2') · 끄면 없다 ──
        {
            var show = new LiveShow(Worlds.Full(lay, 44), "시험호");
            var s = show.Sim; var snd = s.Get<Sound>();
            show.Advance(1.0 / 60);
            show.Ears.ForceEar = At(5, 3); show.Ears.Remap(At(5, 3));
            show.Ears.Captions = true;
            snd.Emit(s, At(8, 3), 80, SoundKind.Bang);
            snd.Emit(s, At(2, 3), 55, SoundKind.Clink);
            show.Advance(1.0 / 60);
            snd.Emit(s, At(2, 3), 55, SoundKind.Clink);
            show.Advance(1.0 / 60);
            var on = show.Ears.CaptionsNow();
            show.Ears.Captions = false;
            snd.Emit(s, At(8, 3), 80, SoundKind.Bang);
            show.Advance(1.0 / 60);
            var off = show.Ears.CaptionsNow();
            string Line(SoundCaption c) => $"{c.Dir} {c.Text}{(c.Count > 1 ? $" ×{c.Count}" : "")}";
            Check("소리 자막: 켜면 동쪽 쾅은 '→ 쾅' · 서쪽 딸그락 두 번은 '← 딸그락 ×2' · 끄면 자막이 없다",
                on.Any(c => c.Text == "쾅" && c.Dir == "→") && on.Any(c => c.Text == "딸그락" && c.Dir == "←" && c.Count == 2) && off.Length == 0,
                $"켬: {string.Join(" / ", on.Select(Line))} · 끔: {off.Length}줄");
        }
    }
}
