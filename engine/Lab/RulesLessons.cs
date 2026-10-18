using ShipEngine;

namespace Lab;

/// <summary>⑧ 바퀴 3 — 오래 남는 배움: 사고의 교훈 (보면 배우고 · 겪어 본 이가 먼저 안다 · 3주에 반) · 녹스는 솜씨 · 다시 익히기 (docs/ENGINE.md ⑧ 바퀴 3).</summary>
public static partial class Program
{
    private static void LessonRules()
    {
        if (!Do("오래 남는 배움")) return;
        Console.WriteLine("\n[⑧ 오래 남는 배움]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var legend = new Dictionary<char, string> { ['x'] = "OxygenMaskBox", ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer", ['e'] = "Bed" };
        var lay = Worlds.Ascii(BreathShip, legend);
        int At(int x, int y) => Worlds.At(lay, x, y);

        // ── 겪어 본 이가 먼저 안다: 배 공기가 천천히 빠진다 (20분에 100 → 45kPa) — 구멍을 겪어 본 이(교훈 0.8)는 어지러움을 더 일찍 알아채 마스크를 먼저 쓴다 ──
        {
            long Masked(float lesson)
            {
                var s = Worlds.Full(lay, 6);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var f = s.Get<Field>(); s.Get<Network>().AirReserve = 0;
                int p = m.AddPerson(s, At(6, 2));
                m.SetPersonality(p, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f);
                m[p].C.LessonBreach = lesson;
                s.Run(2);
                var cells = Enumerable.Range(0, s.Layout.N).Where(c => s.Layout.IsSpace(c)).ToList();
                double k1 = Math.Pow(0.45, 1.0 / Time.Minutes(20));
                for (int t = 0; t < Time.Minutes(40); t++)
                {
                    if (t < Time.Minutes(20)) foreach (int c in cells) for (int g = 0; g < f.G; g++) f.RemoveGas(c, g, f.N[c * f.G + g] * (1 - k1));
                    s.Step();
                    if (life[m[p].C.Body].Mask != 0) return t;
                }
                return long.MaxValue;
            }
            long seen = Masked(0.8f), naive = Masked(0);
            Check("겪어 본 이가 먼저 안다: 배 공기가 천천히 빠질 때 구멍을 겪어 본 이(교훈 0.8)는 어지러움을 더 일찍 알아채 마스크를 먼저 쓴다",
                seen < naive && naive - seen >= Time.Minutes(1),
                $"마스크까지 — 겪어 본 이 {(seen == long.MaxValue ? "못 씀" : $"{seen * 2.4 / 60:0.0}분")} · 처음인 이 {(naive == long.MaxValue ? "못 씀" : $"{naive * 2.4 / 60:0.0}분")}");
        }

        // ── 보면 배운다 · 3주에 반: 같은 방에서 불이 붙는 것을 본 깨어 있는 이는 불의 교훈이 쌓이고 · 다른 방 이는 그대로 / 하루 뒤 0.5^(1/21)만큼 흐려진다 ──
        {
            var s = Worlds.Full(lay, 7);
            var m = s.Get<Mind>();
            int a = m.AddPerson(s, At(6, 2)), b = m.AddPerson(s, At(24, 2));
            s.Run(30);
            s.Facts.Emit(s.Tick, FactKind.Ignition, At(4, 1), 0, s.Causes.Add(CauseKind.Fire, "불이 붙었다", s.Tick, At(4, 1)));
            s.Run(30);
            float la = m[a].C.LessonFire, lb = m[b].C.LessonFire;
            m[a].C.LessonFire = 0.6f;
            s.Run((int)Time.Hours(24));
            float after = m[a].C.LessonFire, expect = 0.6f * (float)Math.Pow(0.5, 1.0 / Mind.LessonHalfDays);
            Check("보면 배운다 · 3주에 반: 같은 방에서 불이 붙는 것을 본 이는 불의 교훈이 쌓이고 · 다른 방 이는 그대로 / 교훈은 반감기 3주로 흐려진다 (하루 0.6 → 0.58)",
                la >= 0.15f && lb == 0 && Math.Abs(after - expect) < 0.01f,
                $"본 이 {la:0.00} · 다른 방 {lb:0.00} / 하루 뒤 {after:0.000} (기대 {expect:0.000})");
        }

        // ── 녹스는 솜씨 · 다시 익히기: 두 주 넘게 안 쓴 고치기 솜씨(0.8)는 하루 1%씩 바닥(0.4)으로 · 녹은 이는 다시 익힐 때 처음보다 빠르다 ──
        {
            var s = Worlds.Full(lay, 8);
            var m = s.Get<Mind>();
            int a = m.AddPerson(s, At(6, 2)), b = m.AddPerson(s, At(24, 2));
            m[a].C.SkRepair = 0.8f; m[b].C.SkRepair = 0.6f;   // b는 0.6보다 높았던 적이 없다
            s.Run(30);
            float peak = m.SkillPeak(a, 0);
            m.SetSkillUsed(a, 0, -Time.Hours(24 * 30));   // 서른 날 전에 마지막으로 고쳤다
            s.Run((int)(Time.TicksPerDay * 3 - s.Tick % Time.TicksPerDay + 5));   // 0시를 세 번 지난다
            float rusty = m[a].C.SkRepair, fresh = m[b].C.SkRepair;
            float expect = 0.4f + 0.4f * (float)Math.Pow(1 - Mind.RustPerDay, 3);
            m[a].C.SkRepair = 0.6f;
            float ra = m.Relearn(a, 0), rb = m.Relearn(b, 0);
            Check("녹스는 솜씨: 서른 날 안 쓴 고치기 솜씨(0.8)는 0시마다 바닥(가장 높았던 값의 절반)으로 1%씩 · 두 주가 안 된 이는 그대로 / 같은 0.6에서 녹은 이(전에 0.8)는 처음 익히는 이보다 빠르다",
                peak >= 0.8f - 1e-4 && Math.Abs(rusty - expect) < 0.003f && Math.Abs(fresh - 0.6f) < 1e-4 && ra > rb + 0.3f,
                $"사흘 뒤 녹은 이 {rusty:0.000} (기대 {expect:0.000}) · 쓴 이 {fresh:0.000} / 다시 익히는 배율 — 녹은 이 {ra:0.00} · 처음인 이 {rb:0.00}");
        }
    }
}
