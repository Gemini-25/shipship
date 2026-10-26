using ShipEngine;
using ShipSim.Art;

namespace ShipView;

/// <summary>
/// 두 곳 나눠 보기 (docs/OBSERVER.md §8-12 · docs/UI2.md U9 — 화면만): 함장이 사람 · 로봇을 따라가거나 손으로 카메라를 옮긴 동안 ·
/// 연출이 화면 밖 다른 곳에서 위험 · 결정적 순간을 보고 있으면 그 자리를 구석 작은 창에 (32도트 10 × 6칸 · 0.2초마다 한 장) · 누르면 연출을 따라 그리로.
/// 떠오르면 4초는 두고 · 연출이 그 일을 놓은 뒤 2초에 닫는다 (깜빡이지 않게).
/// </summary>
public sealed partial class LiveShow
{
    public const double InsetMinSec = 4, InsetLingerSec = 2, InsetEverySec = 0.2;
    public const int InsetCellsW = 10, InsetCellsH = 6;
    private int _insetCell = -1, _insetVer;
    private double _insetOn, _insetSeen, _insetDrawn;
    private Img? _insetImg;
    private string _insetTitle = "";

    /// <summary>재기용: 구석 창 한 장 그리기 (ms · 마지막 것).</summary>
    public double InsetMs;
    /// <summary>구석 작은 창의 자리 (칸 — 없으면 -1).</summary>
    public int InsetCell => _insetCell;

    /// <summary>지금 화면이 그 칸을 보나.</summary>
    private bool Sees(int cell)
    {
        var L = World.Layout;
        if (cell < 0 || cell >= L.N) return false;
        var (l, t, w, h) = Camera();
        double x = L.X(cell) + 0.5, y = L.Y(cell) + 0.5;
        return x >= l && x <= l + w && y >= t && y <= t + h;
    }

    /// <summary>이 장의 구석 창 (작업 스레드 — Render에서 큰 그림 뒤에).</summary>
    private void InsetStep()
    {
        var vd = View;
        bool mine = _replay == null && !Overview && (Selected >= 0 && FollowSelected || HandCam);   // 함장이 고른 곳을 보는 중 (연출을 따르지 않는다)
        int fc = vd.FocusCell;
        bool want = mine && fc >= 0 && vd.Mood is "위험" or "결정적 순간" && !Sees(fc);
        if (want)
        {
            if (_insetCell < 0) { _insetOn = _anim; _insetImg = null; }
            _insetCell = fc; _insetSeen = _anim;
        }
        else if (_insetCell >= 0 && (!mine || Sees(_insetCell) || _anim - _insetSeen > InsetLingerSec && _anim - _insetOn > InsetMinSec)) _insetCell = -1;
        if (_insetCell < 0) { _insetTitle = ""; return; }
        if (_insetImg != null && _anim - _insetDrawn < InsetEverySec) return;
        var L = World.Layout;
        int x0 = Math.Clamp(L.X(_insetCell) - InsetCellsW / 2, 0, Math.Max(0, L.W - InsetCellsW)), y0 = Math.Clamp(L.Y(_insetCell) - InsetCellsH / 2, 0, Math.Max(0, L.H - InsetCellsH));
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _insetImg = Painter.Frame(World, x0, y0, InsetCellsW, InsetCellsH, (int)(_anim * AnimFps));   // 새 그림판 (메인이 옮겨 가는 동안 덮어쓰지 않게 — 0.2초마다 250KB)
        InsetMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _insetVer++; _insetDrawn = _anim;
        int room = L.Room[_insetCell];
        _insetTitle = $"{(room >= 0 ? L.Rooms[room].Name : "통로")} — {vd.Mood} · 누르면 그리로";
    }

    /// <summary>구석 작은 창을 눌렀다: 연출을 따라 그리로 (고른 이는 그대로 — 카드는 남는다).</summary>
    public void InsetGo()
    {
        if (_insetCell < 0) return;
        FollowSelected = false; Follow = true; Overview = false;
        _insetCell = -1; _insetTitle = "";
    }
}
