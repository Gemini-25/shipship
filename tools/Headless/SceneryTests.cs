using System;
using System.Linq;
using ShipSim.Core;

// v23 창밖 풍경 점검 (--scenerytest): 구간 20종 풍경 · 볼 만한 구간에 들어서면 알림 · 처음 보면 연대기 · 사람들이 창가에 모여 구경 · 저장.
public static partial class Program
{
    private static int RunSceneryTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"창밖 풍경 점검 (v23) · 시드 {seed}\n");
        try
        {
            var kinds = Enum.GetValues<LegKind>();
            var names = kinds.Select(k => Scenery.Of(k).Name).ToList();
            Check("구간마다 창밖 풍경 — 이름이 다 다르다", names.Distinct().Count() == kinds.Length && names.All(n => n.Length > 0), $"{kinds.Length}종 · 볼 만한 것 {kinds.Count(k => Scenery.Of(k).Scenic)} · {string.Join(" · ", names.Take(8))} …");

            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.Hours(9)); // 저녁 무렵
            w.Voyage.JumpTo(LegKind.GasGiant);
            bool logged = w.Log.Entries.Any(e => e.Text.StartsWith("창밖 — 가스 행성"));
            bool hist = w.History.Events.Any(e => e.Text.Contains("처음 본 창밖 — 가스 행성"));
            Check("볼 만한 구간에 들어서면 알리고 · 처음 보면 연대기에", w.Voyage.Current.Kind == LegKind.GasGiant && logged && hist && w.Voyage.ViewFresh, $"{w.Voyage.View.Name} · 알림 {(logged ? "있음" : "없음")} · 연대기 {(hist ? "있음" : "없음")}");
            for (int m = 0; m < 6 * 60; m += 10)
            {
                Run(w, SimTime.Minutes(10));
                if (Environment.GetEnvironmentVariable("SC_DEBUG") == "1")
                    Console.WriteLine($"    {SimTime.Clock(w.Tick)} 보는 중 {w.Crew.Count(c => c.Job?.Activity is LegViewActivity)} · 다 봄 {w.Voyage.ViewDone} · " + string.Join(" / ", w.Crew.Where(c => c.Job?.Activity is LegViewActivity).Select(c => $"{c.Name}:{c.Job!.Label}@{c.Room?.Name}")));
            }
            var watchers = w.Crew.Where(c => c.Memory.Marks.Any(m => m.Text.Contains("창밖 가스 행성"))).Select(c => c.Name).ToList();
            Check("사람들이 창가에 모여 구경한다 (마음이 조금 풀린다)", w.Voyage.ViewGathered >= 2 && watchers.Count >= 2, $"보러 간 사람 {w.Voyage.ViewGathered} · 다 본 사람 {watchers.Count} ({string.Join(" · ", watchers.Take(6))})");
            Run(w, SimTime.Hours(3));
            int before = w.Voyage.ViewGathered;
            Run(w, SimTime.Hours(2));
            Check("여덟 시간이 지나면 더 모이지 않는다", !w.Voyage.ViewFresh && w.Voyage.ViewGathered == before, $"{before} → {w.Voyage.ViewGathered}");

            var w2 = StateSave.Read(StateSave.Write(w));
            Check("저장 · 되살림 — 지금 구간 · 본 풍경 · 본 사람", w2.Voyage.Current.Kind == LegKind.GasGiant && w2.Voyage.KindsSeen.SequenceEqual(w.Voyage.KindsSeen) && w2.Voyage.ViewSeen.Count == w.Voyage.ViewSeen.Count,
                $"{w2.Voyage.View.Name} · 본 풍경 {w2.Voyage.KindsSeen.Count} · 본 사람 {w2.Voyage.ViewSeen.Count}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"  ✗ 예외: {e}");
            _fails++;
        }
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
