using ShipEngine;
using ShipView;
using ShipView.Audio;

namespace Lab;

/// <summary>귀 (소리 단계 S3 — docs/SOUND.md 결정 13 '나눔'): 큰 소리가 몸(⑦)의 청력을 잠시 떨어뜨리고 · 감각(⑧)은 그만큼 덜 듣고 · 고른 사람의 귀로 들으면 소리 출력이 먹먹하고 삐 소리가 난다.</summary>
public static partial class Program
{
    private static void EarRules()
    {
        if (!Do("귀")) return;
        Console.WriteLine("\n[귀 — 일시 청력 손실 · 덜 들림 · 고른 사람의 귀]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);

        // ── 큰 소리 곁: 3m 곁의 쾅 140dB(기체 폭발만큼)에 귀가 먹먹하고 · 벽 너머 방은 멀쩡하다 · 열두 시간이면 반 넘게 돌아온다 /
        //    폭풍의 넘친 압력 20kPa(소리 크기 180dB)를 받은 이는 15dB 언저리 (시험 배의 외판은 작은 폭약에도 뚫려 진공이 되므로 폭풍은 압력만 차린다) ──
        {
            var s = Worlds.Full(lay, 11);
            var m = s.Get<Mind>(); var life = s.Get<Life>();
            int near = m.AddPerson(s, At(20, 3)), far = m.AddPerson(s, At(5, 3)), blasted = m.AddPerson(s, At(13, 4));
            s.Run(3);
            int cause = s.Causes.Add(CauseKind.Setup, "수소 주머니가 터졌다 (시험 차리기)", s.Tick);
            s.Get<Sound>().Emit(s, At(17, 3), 140, SoundKind.Bang, cause);
            s.Get<Structure>().BlastPeakKPa[At(13, 4)] = 20;
            s.Run(6);
            float a0 = life[m[near].C.Body].HearShift, b0 = life[m[far].C.Body].HearShift, c0 = life[m[blasted].C.Body].HearShift;
            string why = life[m[near].C.Body].HearCause != 0 ? s.Causes.Story(life[m[near].C.Body].HearCause) : "-";
            s.Run(Time.Hours(12));
            float a1 = life[m[near].C.Body].HearShift;
            bool alive = life[m[near].C.Body].Alive != 0;
            Check("귀: 3m 곁의 쾅(140dB)에 청력이 8dB 넘게 떨어지고(원인: 그 소리) · 벽 너머 방은 그대로 · 열두 시간 뒤엔 반 아래로 돌아온다 / 폭풍 20kPa를 받은 이는 12~18dB",
                alive && a0 > 8 && b0 < 1 && a1 < a0 * 0.5f && why.Contains("수소") && c0 > 12 && c0 < 18,
                $"곁 {a0:0.0}dB → 12시간 뒤 {a1:0.0}dB · 벽 너머 {b0:0.0}dB · 폭풍 {c0:0.0}dB · 원인 \"{why}\"");
        }

        // ── 시끄러운 방: 100dB로 우는 기계 곁에서 네 시간 자면 몇 dB 떨어진다 (85dB 넘는 몫의 반까지 · 두 시간에 걸쳐) · 조용한 방은 그대로 ──
        {
            var s = Worlds.Full(lay, 12);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var snd = s.Get<Sound>();
            int loud = m.AddPerson(s, At(3, 5)), quiet = m.AddPerson(s, At(17, 3));
            snd.SetDrone(9999, At(5, 5), 100);   // 시끄러운 기계 (시험 차리기 — 망이 요청하는 이어지는 소리)
            for (int t = 0; t < Time.Hours(4); t++)
            {
                foreach (int p in new[] { loud, quiet }) { life[m[p].C.Body].Asleep = 1; m[p].C.Sleepy = 0.9f; m[p].C.Hunger = 0; m[p].C.Thirst = 0; }
                s.Step();
            }
            float amb = snd.Ambient(m[loud].C.Cell), ls = life[m[loud].C.Body].HearShift, qs = life[m[quiet].C.Body].HearShift;
            if (Debug) Console.WriteLine($"    시끄러운 방: 살아 있음 {life[m[loud].C.Body].Alive} · 몸 칸 {life[m[loud].C.Body].Cell} · 마음 칸 {m[loud].C.Cell} · 몸 칸 바탕 {snd.Ambient(life[m[loud].C.Body].Cell):0}");
            Check("귀: 시끄러운 방(85dB 넘게)에서 네 시간이면 청력이 몇 dB 떨어진다 · 조용한 방은 그대로",
                amb > 90 && ls > 3 && qs == 0, $"바탕 소음 {amb:0}dB → {ls:0.0}dB 떨어짐 · 조용한 방 {qs:0.0}dB");
        }

        // ── 덜 들림: 얕게 자던 이가 3m 곁의 쾅(70dB)에 깬다 — 귀가 40dB 먹먹하면 깨지 않는다 (⑧ 감각이 몸의 값을 읽는다) ──
        {
            (bool woke, long deafened) Run(float shift)
            {
                var s = Worlds.Full(lay, 13);
                var m = s.Get<Mind>(); var life = s.Get<Life>();
                int p = m.AddPerson(s, At(3, 3));
                life[m[p].C.Body].Asleep = 1; m[p].C.Sleepy = 0.05f;
                s.Run(2);
                life[m[p].C.Body].Asleep = 1;
                life[m[p].C.Body].HearShift = shift;
                long d0 = m.Deafened, f0 = s.Facts.Total;
                s.Get<Sound>().Emit(s, At(6, 3), 70, SoundKind.Bang);
                s.Run(2);
                return (s.Facts.Since(f0).Any(f => f.Kind == FactKind.Woke && f.Actor == p), m.Deafened - d0);
            }
            var ok = Run(0); var deaf = Run(40);
            Check("귀: 얕게 자던 이는 3m 곁의 쾅(70dB)에 깨고 · 귀가 40dB 먹먹하면 깨지 않는다 (⑧ 감각이 몸의 값을 읽는다)",
                ok.woke && !deaf.woke && deaf.deafened > 0, $"멀쩡: 깸 {ok.woke} / 먹먹: 깸 {deaf.woke} · 못 들은 소리 {deaf.deafened}");
        }

        // ── 고른 사람의 귀로 듣기 (소리 출력): 먹먹한 이(30dB)를 고르면 모든 소리가 30dB 작고 높은 음이 깎이고 · 삐 소리(이명)가 난다 · 멀쩡하면 없다 ──
        {
            var show = new LiveShow(Worlds.Full(lay, 14), "시험호");
            var m = show.Sim.Get<Mind>(); var life = show.Sim.Get<Life>();
            int p = m.AddPerson(show.Sim, At(5, 3));
            const double dt = 1.0 / 60;
            show.Advance(dt);
            show.Selected = p;
            show.Advance(dt);
            while (show.Ears.Out.TryDequeue(out _)) { }
            show.Ears.Hear(At(8, 3), 80, show.Ears.EarCell, 10, false, out float lp0, out _, out float cut0, out _);
            life[m[p].C.Body].HearShift = 30;
            show.Advance(dt);
            bool ring = false;
            while (show.Ears.Out.TryDequeue(out var fr)) ring |= fr.Loops.Any(u => u.Kind == LoopKind.Ring);
            show.Ears.Hear(At(8, 3), 80, show.Ears.EarCell, 10, false, out float lp1, out _, out float cut1, out _);
            Check("귀(화면): 먹먹한 이(30dB)의 귀로 들으면 같은 소리가 30dB 작고 높은 음이 깎이고 · 삐 소리가 난다 (그 사람의 몸 값 — 소리 출력에서만)",
                Math.Abs(lp0 - lp1 - 30) < 0.5f && cut1 < cut0 / 4 && ring && show.Ears.Deaf == 30,
                $"크기 {lp0:0} → {lp1:0}dB · 거름 {cut0:0} → {cut1:0}Hz · 삐 소리 {ring}");
        }
    }
}
