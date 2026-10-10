using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

namespace ShipSim.View;

// v17.7 실제 게임 프레임 시간 (`--perf=프레임 수`): 지금 배속 그대로 N 프레임을 재서 로그에 남긴다.
//   godot -- --ship=Saeteo --crew=60 --warp=6 --speed=3 --scenario=chaos --perf=300 --shot=a.png
public partial class Main
{
    /// <summary>
    /// 한 프레임에 시뮬레이션이 쓸 수 있는 시간. 넘으면 밀린 틱을 버린다 — 느린 프레임 → 더 많은 틱 → 더 느린 프레임으로 번지지 않게
    /// (틱 순서는 그대로라 결과는 같고, 무거운 순간에만 배속이 잠깐 준다).
    /// </summary>
    private const double SimBudgetMs = 25.0;

    /// <summary>
    /// 60프레임: 이번 프레임에 시뮬레이션이 쓸 수 있는 시간 (6 ~ 25ms). 프레임을 놓치면서 시뮬레이션이 시간을 많이 썼으면 줄이고,
    /// 60프레임에 맞으면 조금씩 늘린다 — 큰 배를 빠른 배속으로 볼 때 화면이 끊기는 대신 배속이 조금 준다 (상단에 실제 배속).
    /// </summary>
    private double _simBudget = SimBudgetMs;
    private double _tickRate = -1;

    /// <summary>지난 몇 초 동안 실제로 흐른 배속 (밀린 틱을 버리면 고른 배속보다 느리다).</summary>
    public float ActualSpeed { get; private set; } = 1f;

    private void FitSimBudget(double delta, double simMs, int ticks)
    {
        if (delta > 1.0 / 55.0 && simMs > 4.0) _simBudget = Math.Max(6.0, _simBudget * 0.85);
        else if (delta < 1.0 / 58.0) _simBudget = Math.Min(SimBudgetMs, _simBudget + 0.25);
        double rate = ticks / Math.Max(1e-4, delta);
        _tickRate = _tickRate < 0 ? rate : _tickRate * 0.97 + rate * 0.03;
        ActualSpeed = (float)(_tickRate / ShipSim.Core.SimTime.TicksPerSecond / (SlowMotion ? 0.3 : 1.0));
    }

    // v19 60프레임: 쓰레기 치우기(GC)를 잘게 — 기본 설정은 몇 초마다 한 번 크게 치워 40~60ms씩 멈췄다.
    //   8MB를 새로 쓸 때마다 젊은 세대만 짧게(1~2ms) 치운다 (시뮬레이션 결과에는 닿지 않는다 — 기억 정리일 뿐).
    private const long GcStepBytes = 8L << 20;
    private long _gcMark;

    private void PacedGc()
    {
        long a = GC.GetTotalAllocatedBytes(false);
        if (_gcMark == 0) { _gcMark = a; return; }
        if (a - _gcMark < GcStepBytes) return;
        GC.Collect(0, GCCollectionMode.Forced, blocking: true, compacting: false);
        _gcMark = GC.GetTotalAllocatedBytes(false);
    }

    private int _perfLeft = -1, _perfFrames;
    private (int g0, int g1, int g2, TimeSpan pause, long alloc) _gc0;
    private ulong _perfLastUs;
    private readonly List<double> _perfMs = new();
    private double _perfCalls, _perfPrims, _perfObjs;

    // --census: 그리는 층을 하나씩 3 프레임 숨겨 그리기 호출 · 도형이 얼마나 주는지 본다 (어느 층이 무거운가)
    private List<CanvasItem>? _census;
    private int _censusAt = -1, _censusFrame;
    private double _censusBaseCalls, _censusBasePrims;

    private void CensusStep()
    {
        if (_census == null)
        {
            if (!OS.GetCmdlineUserArgs().Contains("--census")) return;
            _census = new List<CanvasItem>();
            void Walk(Node n) { foreach (var ch in n.GetChildren()) { if (ch is CanvasItem ci && ci.Visible && (ch is DrawLayer || ch.GetChildCount() == 0)) _census.Add(ci); Walk(ch); } }
            Walk(GetTree().Root);
            _censusAt = -1; _censusFrame = 0;
            if (_screenshotFrames >= 0) _screenshotFrames = (_census.Count + 2) * 4 + 30;
        }
        double calls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame), prims = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        if (++_censusFrame < 4) return;
        _censusFrame = 0;
        if (_censusAt < 0) { _censusBaseCalls = calls; _censusBasePrims = prims; GD.Print($"층 조사 · 전체 그리기 호출 {calls:0} · 도형 {prims:0}"); }
        else if (_censusAt < _census.Count)
        {
            var c = _census[_censusAt];
            GD.Print($"층 조사 · {c.GetPath()} · 호출 {_censusBaseCalls - calls:0} · 도형 {_censusBasePrims - prims:0}");
            c.Visible = true;
        }
        _censusAt++;
        if (_censusAt < _census.Count) _census[_censusAt].Visible = false;
    }

    private void PerfStart()
    {
        if (OS.GetCmdlineUserArgs().Contains("--nobake")) BakedLayer.Enabled = false; // 구워 두지 않고 예전처럼 (비교용)
        WatchArgs();
        var arg = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--perf="));
        if (arg == null || !int.TryParse(arg[7..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n <= 0) return;
        _perfFrames = n;
        _perfLeft = n + 20; // 처음 20 프레임은 자리 잡는 시간 (재지 않는다)
        if (_screenshotFrames >= 0 && _screenshotFrames < _perfLeft + 2) _screenshotFrames = _perfLeft + 2;
    }

    // 화면 시험: --photo (사진 모드) · --snapat=N (N 프레임에 찍고 연대기를 연다) · --follow (물건 따라가기) · --voyage (항해 결산)
    private int _snapAt = -1;
    private void WatchArgs()
    {
        var args = OS.GetCmdlineUserArgs();
        if (args.Contains("--photo")) TogglePhotoMode();
        if (args.Contains("--follow")) CycleFollowItem(1);
        if (args.Contains("--voyage")) Hud.ToggleVoyage();
        if (args.FirstOrDefault(a => a.StartsWith("--snapat=")) is string sa && int.TryParse(sa[9..], out int n))
        {
            if (!PhotoMode) TogglePhotoMode();
            _snapAt = n;
            if (_screenshotFrames >= 0 && _screenshotFrames < n + 15) _screenshotFrames = n + 15;
        }
    }

    private void SnapStep()
    {
        if (_snapAt < 0 || --_snapAt > 0) return;
        _snapAt = -1;
        TakePhoto();
        TogglePhotoMode();
        if (!Hud.VoyageOpen) Hud.ChronicleOpen = true; // --voyage 와 함께면 결산 카드에서 사진을 본다
    }

    private void PerfFrame()
    {
        SnapStep();
        CensusStep();
        if (_perfLeft < 0) return;
        ulong now = Time.GetTicksUsec();
        FrameProbe.EndFrame(); // v19 지난 프레임에 우리 코드가 쓴 CPU
        if (_perfLeft <= _perfFrames && _perfLastUs > 0)
        {
            _perfMs.Add((now - _perfLastUs) / 1000.0);
            _perfCalls += Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
            _perfPrims += Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
            _perfObjs += Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
        }
        _perfLastUs = now;
        if (_perfLeft == _perfFrames + 1) { FrameProbe.Reset(); FrameProbe.On = true; _gc0 = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalPauseDuration(), GC.GetTotalAllocatedBytes()); }
        if (--_perfLeft > 0) return;
        FrameProbe.On = false;
        var s = _perfMs.OrderBy(x => x).ToArray();
        double P(double q) => s.Length == 0 ? 0 : s[Math.Min(s.Length - 1, (int)(q * s.Length))];
        GD.Print($"프레임 {s.Length}개 · {Speeds[SpeedIndex]}배속 · 확대 {Camera.Zoom.X:0.00} · 승무원 {Sim.Crew.Count}명 · 평균 {s.DefaultIfEmpty(0).Average():0.0}ms · 중앙 {P(0.5):0.0}ms · p95 {P(0.95):0.0}ms · 최대 {s.DefaultIfEmpty(0).Max():0.0}ms · {1000.0 / Math.Max(0.1, s.DefaultIfEmpty(0).Average()):0}fps · 그리기 호출 {_perfCalls / Math.Max(1, s.Length):0} · 도형 {_perfPrims / Math.Max(1, s.Length):0} · 물체 {_perfObjs / Math.Max(1, s.Length):0}");
        var cpu = FrameProbe.FrameMs.OrderBy(x => x).ToArray();
        double C(double q) => cpu.Length == 0 ? 0 : cpu[Math.Min(cpu.Length - 1, (int)(q * cpu.Length))];
        GD.Print($"GC · 0세대 {GC.CollectionCount(0) - _gc0.g0} · 1세대 {GC.CollectionCount(1) - _gc0.g1} · 2세대 {GC.CollectionCount(2) - _gc0.g2} · 멈춘 시간 {(GC.GetTotalPauseDuration() - _gc0.pause).TotalMilliseconds:0}ms · 할당 {(GC.GetTotalAllocatedBytes() - _gc0.alloc) / 1e6 / Math.Max(1, s.Length):0.00}MB/프레임 · 힙 {GC.GetTotalMemory(false) / 1e6:0}MB");
        GD.Print($"우리 코드 CPU · 프레임 {cpu.Length}개 · 평균 {cpu.DefaultIfEmpty(0).Average():0.0}ms · 중앙 {C(0.5):0.0}ms · p95 {C(0.95):0.0}ms · p99 {C(0.99):0.0}ms · 최대 {cpu.DefaultIfEmpty(0).Max():0.0}ms · 12ms 넘은 프레임 {cpu.Count(x => x > 12):0}");
        foreach (var sf in FrameProbe.SlowFrames) GD.Print("  느린 프레임 · " + sf);
        foreach (var (key, ms, calls, max, kb) in FrameProbe.Report(s.Length).Take(70))
            GD.Print($"  프레임 · {key,-16} {ms,6:0.00}ms/프레임 · {calls}번 · 최대 {max:0.0}ms · 할당 {kb:0.0}KB/프레임");
        _perfLeft = -1;
    }
}
