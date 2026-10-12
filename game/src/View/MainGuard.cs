using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v22 연출 2: 파괴 · 막아 선 순간 카메라.
//   로봇이 사람을 몸으로 막았거나 부서진 순간 — 잠깐 느리게 흐르고 (자동 카메라가 켜져 있으면) 그 자리로 가까이 다가간다.
//   화면은 세상을 읽기만 한다 (순간 목록은 저장하지 않는 화면용).
public partial class Main
{
    private int _guardSerial = -1;
    private Vector2? _guardCam;
    private ulong _guardCamUntil;
    private float _guardZoom;

    private void UpdateGuardCam(double delta)
    {
        var rs = Sim.Robots;
        if (_guardSerial < 0 || _guardSerial > rs.MomentSerial) _guardSerial = rs.MomentSerial; // 처음 · 되감기 · 불러오기: 지난 일은 쫓지 않는다
        int fresh = rs.MomentSerial - _guardSerial;
        _guardSerial = rs.MomentSerial;
        if (fresh > 0 && Replaying == null)
        {
            var ms = rs.Moments;
            // 새 순간들 가운데 부서진 순간이 먼저 · 사람을 막은 순간이 그다음
            var pick = ms.Skip(System.Math.Max(0, ms.Count - fresh)).OrderByDescending(m => (m.Wrecked ? 2 : 0) + (m.Crew >= 0 ? 1 : 0)).First();
            if (Sim.Tick - pick.Tick < SimTime.Minutes(2))
            {
                Impact(pick.Wrecked ? 1700 : 1100); // 잠깐 느리게
                ShowNotice(pick.Text);
                if (Settings.AutoCamera && Camera.FollowTarget == null && !PhotoMode)
                {
                    _guardCam = ShipView.ToPx(pick.At) + new Vector2(Hud.RightColumnWidth * 0.5f, 0f) / Camera.Zoom.X;
                    _guardCamUntil = Time.GetTicksMsec() + 2600;
                    _guardZoom = Mathf.Max(Camera.Zoom.X, pick.Wrecked ? 1.9f : 1.6f);
                }
            }
        }
        if (_guardCam is Vector2 t)
        {
            float k = 1f - Mathf.Exp(-5f * (float)delta);
            Camera.Position = Camera.Position.Lerp(t, k);
            float z = Mathf.Lerp(Camera.Zoom.X, Mathf.Min(_guardZoom, CameraRig.MaxZoom), k * 0.7f);
            Camera.Zoom = new Vector2(z, z);
            if (Time.GetTicksMsec() > _guardCamUntil || Camera.FollowTarget != null) _guardCam = null;
        }
    }
}
