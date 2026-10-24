using ShipEngine;

namespace Lab;

/// <summary>
/// 계산 일꾼 법칙 (6단 · docs/ENGINE.md §3-10): 주컴퓨터의 봉쇄 상상은 틱 t에 찍어 일꾼에 맡기고 t + K에 받는다 —
/// 1코어 = 4코어 · 생각 중에 저장하고 되살려도 같다 · 복제 안에서는 일꾼을 부르지 않는다 · 일꾼은 정적 계측을 쓰지 않는다.
/// </summary>
public static partial class Program
{
    private static void WorkerLaws()
    {
        if (!Do("일꾼")) return;
        Console.WriteLine("\n[계산 일꾼 — 무거운 셈은 다른 코어에서 · 정해 둔 틱에 받는다]");
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        // 뚫린 방에 쓰러진 사람 · 열린 문 너머 옆 방에도 쓰러진 사람 — 주컴퓨터가 '기다림 · 지금 닫음'을 그려 본다 (규칙 '상상 (가)'와 같은 판)
        Sim Breach(float hole = 0.05f)
        {
            var w = Worlds.Full(lay, 1);
            var mw = w.Get<Mind>(); var lw = w.Get<Life>();
            int pa = mw.AddPerson(w, At(6, 2)), pb = mw.AddPerson(w, At(13, 2));
            foreach (int p in new[] { pa, pb }) { ref var body = ref lw[mw[p].C.Body]; body.Conscious = 0; body.Hypoxia = 0.6f; }
            w.Get<Structure>().RequestDoor(lay.DoorAt[At(10, 3)], 1); w.Run(3);
            w.Get<Field>().SetHole(At(0, 2), hole);
            return w;
        }
        // 맡긴 셈이 생길 때까지 (2분 안)
        bool UntilThinking(Sim w) { var cm = w.Get<ShipMind>(); for (int k = 0; k < Time.Minutes(2); k++) { if (cm.Thinking > 0) return true; w.Step(); } return cm.Thinking > 0; }
        int keep = Par.Threads;
        try
        {
            // 1 · 1코어 = 4코어: 일꾼에서 돌려도 · 그 틱에 바로 돌려도 받는 틱과 결과가 같다 → 세계가 바이트까지 같다
            (byte[] save, int imagined, long onWorker, long startAt, long tookAt) Run(int threads)
            {
                Par.Threads = threads;
                var w = Breach(); var cm = w.Get<ShipMind>();
                UntilThinking(w);
                long start = w.Tick, took = -1;
                for (int k = 0; k < Time.Minutes(1); k++) { int before = cm.Imaginations.Count; w.Step(); if (took < 0 && cm.Imaginations.Count > before) took = w.Tick; }
                return (w.Save(), cm.Imaginations.Count, cm.ThoughtsOnWorker, start, took);
            }
            var one = Run(1); var four = Run(4);
            bool same = one.save.AsSpan().SequenceEqual(four.save);
            Check("일꾼: 1코어 (그 틱에 바로) = 4코어 (일꾼) — 맡긴 틱 + K에 받고 세계가 바이트까지 같다",
                same && one.imagined >= 2 && four.onWorker >= 1 && one.onWorker == 0 && four.tookAt - four.startAt == ShipMind.ThinkTicks,
                $"같음 {same} · 그려 봄 {one.imagined / 2}번 · 일꾼에서 {four.onWorker}번 (1코어 {one.onWorker}) · 맡긴 틱 {four.startAt} → 받은 틱 {four.tookAt} (K = {ShipMind.ThinkTicks})");

            // 0 · 되살린 세계: 구멍으로 바람이 부는 중에 찍어도 이어 돌려 같다 — 틈마다 지난 틱 흐름량 (잘게 나눠 셀지 정한다) · 칸 바람 압력 (마음이 장보다 먼저 읽는다)을 찍는다
            //     (예전엔 둘 다 찍지 않아 되살린 세계가 첫 틱에 갈라졌다 — 주컴퓨터의 상상 · 다시보기 · 저장이 모두 이 왕복에 기댄다)
            {
                Par.Threads = 1;
                int firstBad = -1, at = -1;
                for (int k = 0; k <= 6 && firstBad < 0; k += 2)
                {
                    var x0 = Breach(); x0.Get<ShipMind>().Imagining = true;   // 주컴퓨터 상상 없이 (장 왕복만 — 복제 표시는 찍지 않으니 둘 다)
                    x0.Run(k); var x1 = x0.Clone(); x1.Get<ShipMind>().Imagining = true;
                    for (int q = 0; q < 30 && firstBad < 0; q++) { x0.Step(); x1.Step(); if (!x0.Save().AsSpan().SequenceEqual(x1.Save())) { firstBad = q; at = k; } }
                }
                Check("되살린 세계: 구멍으로 바람이 부는 중에 찍어도 이어 돌려 바이트까지 같다 (틈 흐름량 · 칸 바람 압력을 찍는다)", firstBad < 0,
                    firstBad < 0 ? "구멍 뒤 0 · 2 · 4 · 6틱에 찍어 30틱씩 같음" : $"구멍 뒤 {at}틱에 찍어 {firstBad}틱 뒤 갈라짐");
                Par.Threads = keep;
            }
            // 1′ · 반사: 기압이 빠르게 빠지는 방은 그려 보는 동안 문을 먼저 닫는다 (한 틱 늦은 '지금 닫음'이 옆 방 기압 65 → 42kPa) · 천천히 새는 방은 그대로
            {
                Par.Threads = 4;
                int door = lay.DoorAt[At(10, 3)];
                var fast = Breach(); bool tf = UntilThinking(fast); float fastTarget = fast.Get<Structure>().DoorTarget[door];
                var slow = Breach(0.004f); bool ts = UntilThinking(slow); float slowTarget = slow.Get<Structure>().DoorTarget[door];
                fast.Get<ShipMind>().WaitThoughts(); slow.Get<ShipMind>().WaitThoughts();
                Check("일꾼 반사: 빠른 구멍 (12초에 8kPa 넘게) 방은 두 미래를 그려 보는 동안 문을 먼저 닫고 · 천천히 새는 방은 문을 그대로 둔다",
                    tf && fastTarget == 0 && (!ts || slowTarget > 0), $"빠른 구멍: 생각 {tf} · 문 목표 {fastTarget:0.0} / 천천히: 생각 {ts} · 문 목표 {slowTarget:0.0}");
            }

            // 2 · 생각 중 저장 왕복: 맡긴 셈이 있는 채 찍으면 끝을 기다려 결과를 함께 찍는다 → 되살린 세계 · 찍은 세계 · 찍지 않은 세계가 같다
            Par.Threads = 4;
            var a = Breach(); var c = Breach();
            bool caught = UntilThinking(a); UntilThinking(c);
            var b = a.Clone();   // 찍기 (생각 중)
            a.Run(Time.Minutes(2)); b.Run(Time.Minutes(2)); c.Run(Time.Minutes(2));
            byte[] sa = a.Save(), sb = b.Save(), sc = c.Save();
            Check("일꾼: 생각 중에 찍고 되살려도 같다 — 되살린 세계 = 찍은 세계 = 찍지 않은 세계 (2분 뒤 바이트)",
                caught && sa.AsSpan().SequenceEqual(sb) && sa.AsSpan().SequenceEqual(sc),
                $"생각 중에 찍음 {caught} · 되살린 것 {sa.AsSpan().SequenceEqual(sb)} · 찍지 않은 것 {sa.AsSpan().SequenceEqual(sc)} · 그려 봄 {b.Get<ShipMind>().Imaginations.Count / 2}번");

            // 3 · 복제 안에서는 일꾼을 부르지 않는다 (복제 속 복제가 없다 — 규칙대로만)
            {
                var w = Breach(); var cm = w.Get<ShipMind>();
                cm.Imagining = true;
                w.Run(Time.Minutes(2));
                Check("일꾼: 복제 안의 주컴퓨터는 그려 보지도 · 일꾼에 맡기지도 않는다", cm.ThoughtsStarted == 0 && cm.Imaginations.Count == 0 && cm.Thinking == 0,
                    $"맡김 {cm.ThoughtsStarted} · 그려 봄 {cm.Imaginations.Count} · 남은 셈 {cm.Thinking}");
            }

            // 4 · 일꾼 안 정적 쓰기 없음: 계측을 켠 채 일꾼이 돌아도 계측 표가 그대로다 (주 스레드는 기다리기만)
            {
                Mind.Prof = new(); Field.Prof = new(); Network.Prof = new(); Prof.D = new();
                try
                {
                    var w = Breach(); var cm = w.Get<ShipMind>();
                    bool thinking = UntilThinking(w);
                    string Snap() => string.Join("|", new[] { Mind.Prof, Field.Prof, Network.Prof, Prof.D }.Select(d => $"{d!.Count}:{d.Values.Sum():R}"));
                    string s0 = Snap();
                    cm.WaitThoughts();
                    string s1 = Snap();
                    Check("일꾼: 정적 계측 (마음 · 장 · 망 · 몫)을 켠 채 일꾼이 돌아도 표가 그대로다 (일꾼은 제 스레드 계측만 — 비어 있다)",
                        thinking && cm.ThoughtsOnWorker >= 1 && s0 == s1, $"생각 중 {thinking} · 일꾼에서 {cm.ThoughtsOnWorker}번 · 표 {(s0 == s1 ? "그대로" : $"{s0} → {s1}")}");
                }
                finally { Mind.Prof = null; Field.Prof = null; Network.Prof = null; Prof.D = null; }
            }

            // 5 · 늦은 일꾼 (관전 화면): 받을 틱에 아직 셈 중이면 세계만 그 틱에서 멈추고 그림은 계속 그린다 — 끝나면 이어 간다 (받는 틱 · 결과는 그대로)
            Par.Threads = 4; ShipMind.WorkerDelayMs = 600;
            try
            {
                var show = new ShipView.LiveShow(Breach(), "시험호") { SpeedOverride = 200 };   // 1초에 200틱 (하루 3분 빠르기)
                var cm = show.Sim.Get<ShipMind>();
                double maxMs = 0; int frames = 0, stalled = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < 2.5)
                {
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    show.Advance(1 / 60.0);
                    var fr = show.Render(); show.Recycle(fr.Img);
                    double fms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1" && fms > 50) Console.WriteLine($"    장 {frames}: {fms:0}ms · 틱 {show.Sim.Tick} · 남은 셈 {cm.Thinking} · 기다림 {cm.Waiting(show.Sim.Tick)} · 바쁨 {cm.WorkerBusy}");
                    if (frames >= 10) maxMs = Math.Max(maxMs, fms);   // 처음 몇 장은 코드 첫 번역 (게임은 띄울 때 데운다)
                    frames++;
                    if (cm.Waiting(show.Sim.Tick)) stalled++;
                }
                Check("일꾼: 늦으면 관전 화면은 받을 틱에서 세계만 멈추고 그림은 계속 그린다 (일꾼 0.6초 늦춤 — 처음 10장 뒤 한 장 가장 김 < 200ms) · 끝나면 이어 간다",
                    cm.ThoughtsOnWorker >= 1 && stalled >= 5 && maxMs < 200 && cm.Thinking == 0 && cm.Imaginations.Count >= 2,
                    $"일꾼에서 {cm.ThoughtsOnWorker}번 · 그림 {frames}장 가운데 멈춘 장 {stalled} · 한 장 가장 김 {maxMs:0}ms · 남은 셈 {cm.Thinking} · 그려 봄 {cm.Imaginations.Count / 2}번 · 틱 {show.Sim.Tick}");
            }
            finally { ShipMind.WorkerDelayMs = 0; }
        }
        finally { Par.Threads = keep; }
    }

    /// <summary>규칙 '일꾼을 기다리는 그림 시계' (묶음 '화면'): 받을 틱에 일꾼이 아직 셈 중이라 세계가 멈춘 동안 그리는 때 (지난 틱 + 틱 사이 비율)가 줄지 않는다 —
    /// 예전에는 기다린 장마다 틱 사이 비율을 0으로 되돌려 그 틱을 되감아 다시 재생했다 (한 장에 거의 한 틱 뒤로).</summary>
    private static void WorkerClockRules()
    {
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        int keep = Par.Threads;
        Par.Threads = 4; ShipMind.WorkerDelayMs = 2000;
        try
        {
            // 뚫린 방에 쓰러진 사람 · 열린 문 너머 옆 방에도 — 주컴퓨터가 봉쇄를 그려 본다 (법칙 '일꾼'과 같은 판)
            var w = Worlds.Full(lay, 1);
            var mw = w.Get<Mind>(); var lw = w.Get<Life>();
            int pa = mw.AddPerson(w, At(6, 2)), pb = mw.AddPerson(w, At(13, 2));
            foreach (int q in new[] { pa, pb }) { ref var body = ref lw[mw[q].C.Body]; body.Conscious = 0; body.Hypoxia = 0.6f; }
            w.Get<Structure>().RequestDoor(lay.DoorAt[At(10, 3)], 1); w.Run(3);
            w.Get<Field>().SetHole(At(0, 2), 0.05f);
            var show = new ShipView.LiveShow(w, "시험호") { SpeedOverride = 30 };   // 1초에 30틱 — 장마다 틱 사이 ½
            var cm = w.Get<ShipMind>();
            double last = double.NegativeInfinity, worst = 0;
            int stalled = 0;
            for (int k = 0; k < 240; k++)
            {
                show.Advance(1 / 60.0);
                double t = show.DrawnTick;
                if (cm.Waiting(w.Tick)) stalled++;
                if (t < last) worst = Math.Max(worst, last - t);
                last = Math.Max(last, t);
            }
            Check("일꾼을 기다리는 그림 시계: 받을 틱에 일꾼이 늦어 세계가 멈춘 동안 그리는 때가 줄지 않는다 (틱 사이 비율을 되감지 않는다 — 0.999에 붙잡는다)",
                cm.ThoughtsOnWorker >= 1 && stalled >= 5 && worst == 0,
                $"일꾼에서 {cm.ThoughtsOnWorker}번 · 240장 가운데 멈춘 장 {stalled} · 그리는 때가 가장 많이 준 것 {worst:0.000}틱 · 틱 {w.Tick}");
        }
        finally { ShipMind.WorkerDelayMs = 0; Par.Threads = keep; }
    }
}
