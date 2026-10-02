using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v12.2 하이라이트 모드 (L): 평온하면 빠르게 넘기다가, 사고나 주목할 일이 나면 1배속으로 보여 준다.
/// 자동 카메라는 그 현장으로 옮겨 가고, 폭발·사망·분리 같은 결정적인 순간엔 잠깐 느리게 흐른다.
/// </summary>
public partial class Main
{
    private int _seenCauseNodes;
    private long _hotUntilTick = -1;
    private long _calmSinceTick = -1;
    private ulong _slowUntilMsec;
    private Vector2? _camTarget;
    private ulong _camUntilMsec;
    private float _camZoomTarget;

    public bool Highlight => Settings.Highlight;
    public bool SlowMotion => Settings.Highlight && Settings.AutoCamera && Time.GetTicksMsec() < _slowUntilMsec;

    public void ToggleHighlight()
    {
        Settings.Highlight = !Settings.Highlight;
        Settings.Save();
        _seenCauseNodes = Sim.Causes.Nodes.Count;
        ShowNotice(Settings.Highlight ? "자동 배속 — 평온하면 빠르게, 사고가 나면 1배속으로 (L로 끄기)" : "자동 배속 끔 — 배속을 손으로");
    }

    private static bool Hot(CauseKind k) => k is CauseKind.Impact or CauseKind.Explosion or CauseKind.Fire or CauseKind.Breach
        or CauseKind.Scram or CauseKind.Casualty or CauseKind.Death or CauseKind.Detach or CauseKind.Hazard;

    /// <summary>매 프레임: 새 고리를 보고 배속·카메라·슬로모션을 정한다.</summary>
    private void UpdateHighlight(double delta)
    {
        var log = Sim.Causes;
        if (_seenCauseNodes > log.Nodes.Count) _seenCauseNodes = 0; // 되감기·불러오기
        CauseNode? focus = null;
        for (; _seenCauseNodes < log.Nodes.Count; _seenCauseNodes++)
        {
            var n = log.Nodes[_seenCauseNodes];
            if (!Hot(n.Kind)) continue;
            // 관찰자가 방금 일으킨 사고도 보여 준다
            long hold = n.Kind is CauseKind.Death or CauseKind.Explosion or CauseKind.Scram or CauseKind.Detach ? SimTime.Minutes(40) : SimTime.Minutes(20);
            _hotUntilTick = System.Math.Max(_hotUntilTick, Sim.Tick + hold);
            if (n.Kind is CauseKind.Explosion or CauseKind.Death or CauseKind.Detach) _slowUntilMsec = Time.GetTicksMsec() + 2600;
            if (focus == null || n.Kind is CauseKind.Death or CauseKind.Explosion) focus = n;
        }
        if (!Settings.Highlight) return;

        // 카메라: 새 현장으로 부드럽게 (사람이 카메라를 잡고 있지 않을 때)
        if (focus?.At is System.Numerics.Vector2 at && Settings.AutoCamera && Camera.FollowTarget == null)
        {
            _camTarget = ShipView.ToPx(at) + new Vector2(Hud.RightColumnWidth * 0.5f, 0f) / Camera.Zoom.X;
            _camUntilMsec = Time.GetTicksMsec() + 3500;
            _camZoomTarget = Mathf.Max(Camera.Zoom.X, focus.Kind is CauseKind.Death or CauseKind.Explosion ? 1.35f : 1.1f);
        }
        if (_camTarget is Vector2 t)
        {
            float k = 1f - Mathf.Exp(-3.5f * (float)delta);
            Camera.Position = Camera.Position.Lerp(t, k);
            float z = Mathf.Lerp(Camera.Zoom.X, Mathf.Min(_camZoomTarget, CameraRig.MaxZoom), k * 0.6f);
            Camera.Zoom = new Vector2(z, z);
            if (Time.GetTicksMsec() > _camUntilMsec) _camTarget = null;
        }

        // 배속: 방금 일 → 1배속, 번지는 중 → 3배속, 평온 → 10배속, 오래 평온 → 30배속
        bool serious = log.Incidents.Any(i => i.Open && i.Nodes.Any(id => log.Node(id) is { Open: true } n2
            && n2.Kind is CauseKind.Fire or CauseKind.Breach or CauseKind.Suffocation or CauseKind.Gas or CauseKind.Casualty or CauseKind.Scram));
        if (Sim.Tick < _hotUntilTick || serious) _calmSinceTick = -1;
        else if (_calmSinceTick < 0) _calmSinceTick = Sim.Tick;
        int want = Sim.Tick < _hotUntilTick ? 0 : serious ? 1 : Sim.Tick - _calmSinceTick < SimTime.Hours(1) ? 2 : 3;
        if (want != SpeedIndex) SpeedIndex = want; // 일시정지는 건드리지 않는다
    }
}
