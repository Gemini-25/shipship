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

    private int _perfLeft = -1, _perfFrames;
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
        Hud.ChronicleOpen = true;
    }

    private void PerfFrame()
    {
        SnapStep();
        CensusStep();
        if (_perfLeft < 0) return;
        ulong now = Time.GetTicksUsec();
        if (_perfLeft <= _perfFrames && _perfLastUs > 0)
        {
            _perfMs.Add((now - _perfLastUs) / 1000.0);
            _perfCalls += Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
            _perfPrims += Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
            _perfObjs += Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
        }
        _perfLastUs = now;
        if (_perfLeft == _perfFrames + 1) { FrameProbe.Reset(); FrameProbe.On = true; }
        if (--_perfLeft > 0) return;
        FrameProbe.On = false;
        var s = _perfMs.OrderBy(x => x).ToArray();
        double P(double q) => s.Length == 0 ? 0 : s[Math.Min(s.Length - 1, (int)(q * s.Length))];
        GD.Print($"프레임 {s.Length}개 · {Speeds[SpeedIndex]}배속 · 승무원 {Sim.Crew.Count}명 · 평균 {s.DefaultIfEmpty(0).Average():0.0}ms · 중앙 {P(0.5):0.0}ms · p95 {P(0.95):0.0}ms · 최대 {s.DefaultIfEmpty(0).Max():0.0}ms · {1000.0 / Math.Max(0.1, s.DefaultIfEmpty(0).Average()):0}fps · 그리기 호출 {_perfCalls / Math.Max(1, s.Length):0} · 도형 {_perfPrims / Math.Max(1, s.Length):0} · 물체 {_perfObjs / Math.Max(1, s.Length):0}");
        foreach (var (key, ms, calls) in FrameProbe.Report(s.Length).Take(40))
            GD.Print($"  프레임 · {key,-16} {ms,6:0.00}ms/프레임 · {calls}번");
        _perfLeft = -1;
    }
}
