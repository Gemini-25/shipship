using Godot;
using ShipSim.Core;
using Noise = ShipSim.Core.Noise;

namespace ShipSim.View;

// v17.2 소리 표시: 배 안의 소리(시뮬레이션 정보)를 그 자리에 작은 그림으로 — 종류마다 모양 · 움직임이 다르다 (읽기만 한다).
//  발소리(재질마다: 격자 쩔걱 별 · 타일 또각 꺾쇠 · 카펫 사박 점선 · 고무 점 · 금속판 고리) · 패널 떨림(흔들리는 판과 지그재그) ·
//  베어링(구슬 고리 + 간격마다 퍼지는 물결) · 쉭(가는 김 줄기) · 타닥(불똥 별 + 번개) · 팬(도는 날개) · 물방울(떨어지는 방울과 파문) ·
//  우주복 숨(헬멧 앞 번갈아 피는 김) · 무전(안테나 + 지직선) · 선체 삐걱(벽을 따라 갈라진 물결) · 문 쿵(맞물리는 두 판 + 충격선) ·
//  흥얼(음표) · 휘파람(꼬인 물결) · 톡톡(차례로 튀는 점) · 말소리(겹 호) · 노래(겹친 음표).
//  문 너머로 넘어가는 기계 소리: 열린 문은 밝은 물결이 넘고, 닫힌 문은 흐린 물결이 막대에 막힌다 (먹먹).
public partial class ShipView
{
    private static readonly Color SnMachine = new(1f, 0.72f, 0.35f);
    private static readonly Color SnSoft = new(0.85f, 0.92f, 1f);
    private static readonly Color SnWater = new(0.55f, 0.8f, 1f);

    private void PaintSounds(CanvasItem ci)
    {
        if (HearingSystem.Off) return;
        int lod = Puppet.Lod(Zoom);
        if (lod == 0) return;
        ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        var hs = _world.Hearing;
        float s = 1f;
        foreach (var src in hs.Sources)
        {
            if (src.Kind == Noise.Step && lod < 2) continue;
            var p = ToPx(src.At);
            float a = Mathf.Clamp(0.35f + src.Level, 0.35f, 1f);
            float t = _time + src.Owner * 0.37f;
            switch (src.Kind)
            {
                case Noise.Step: PaintStepMark(ci, p, (ShipSim.Core.Material)src.Sub, a, t, s); break;
                case Noise.Rattle:
                {
                    float k = Mathf.Sin(t * 40f) * 1.2f;
                    var r = new Rect2(p + new Vector2(-5f + k, -7f), new Vector2(10f, 5f));
                    ci.DrawRect(r, new Color(0.7f, 0.74f, 0.78f, 0.55f * a), false, 1f);
                    for (int i = 0; i < 2; i++)
                    {
                        float x0 = p.X + (i == 0 ? -9f : 7f);
                        ci.DrawPolyline(new[] { new Vector2(x0, p.Y - 9f), new Vector2(x0 + 1.5f, p.Y - 7f), new Vector2(x0, p.Y - 5f), new Vector2(x0 + 1.5f, p.Y - 3f) }, SnMachine.WithAlpha(0.7f * a), 1f, true);
                    }
                    break;
                }
                case Noise.Bearing:
                {
                    // 구슬 고리 + 주기마다 퍼지는 물결 (간격이 설비마다 다르다)
                    float per = Mathf.Max(0.4f, src.Period);
                    float ph = Mathf.PosMod(t, per) / per;
                    var c = p + new Vector2(8f, -8f);
                    ci.DrawArc(c, 3f, 0f, Mathf.Tau, 12, SnMachine.WithAlpha(0.8f * a), 1f, true);
                    for (int i = 0; i < 6; i++) ci.DrawCircle(c + Vector2.FromAngle(i * Mathf.Tau / 6f + t * 2f) * 3f, 0.8f, SnMachine.WithAlpha(0.9f * a), true, -1f, true);
                    for (int i = 0; i < 2; i++)
                    {
                        float q = Mathf.PosMod(ph + i * 0.5f, 1f);
                        ci.DrawArc(c, 5f + q * 12f, -0.9f, 0.9f, 8, SnMachine.WithAlpha((1f - q) * 0.7f * a), 1.2f, true);
                        ci.DrawArc(c, 5f + q * 12f, Mathf.Pi - 0.9f, Mathf.Pi + 0.9f, 8, SnMachine.WithAlpha((1f - q) * 0.7f * a), 1.2f, true);
                    }
                    break;
                }
                case Noise.Hiss:
                {
                    for (int i = 0; i < 3; i++)
                    {
                        var pts = new Vector2[6];
                        for (int j = 0; j < 6; j++) pts[j] = p + new Vector2(6f + j * 2.4f, -6f + (i - 1) * 3f + Mathf.Sin(t * 12f + j + i) * 0.8f);
                        ci.DrawPolyline(pts, SnSoft.WithAlpha(0.55f * a), 0.8f, true);
                    }
                    break;
                }
                case Noise.Crackle:
                {
                    if (Mathf.PosMod(t * 3f, 1f) > 0.55f) break;
                    var c = p + new Vector2(-8f, -9f);
                    for (int i = 0; i < 5; i++) ci.DrawLine(c, c + Vector2.FromAngle(i * 1.25f + t) * 3.5f, new Color(1f, 0.95f, 0.5f, 0.9f * a), 0.9f, true);
                    ci.DrawPolyline(new[] { c + new Vector2(2, 2), c + new Vector2(5, 4), c + new Vector2(3.5f, 6), c + new Vector2(7, 9) }, new Color(1f, 0.9f, 0.3f, 0.8f * a), 1f, true);
                    break;
                }
                case Noise.Fan:
                {
                    var c = p + new Vector2(-6f, -6f);
                    ci.DrawArc(c, 4.5f, 0f, Mathf.Tau, 14, new Color(0.7f, 0.78f, 0.85f, 0.35f), 0.8f, true);
                    for (int i = 0; i < 3; i++)
                    {
                        float ang = t * 12f + i * Mathf.Tau / 3f;
                        ci.DrawArc(c + Vector2.FromAngle(ang) * 2f, 2f, ang, ang + 2.2f, 6, new Color(0.8f, 0.88f, 0.95f, 0.55f), 1.1f, true);
                    }
                    break;
                }
                case Noise.Drip:
                {
                    float per = Mathf.Max(0.8f, src.Period);
                    float ph = Mathf.PosMod(t, per) / per;
                    var top = p + new Vector2(0, -12f);
                    if (ph < 0.6f)
                    {
                        var dp = top + new Vector2(0, ph / 0.6f * 10f);
                        ci.DrawCircle(dp, 1.3f, SnWater.WithAlpha(0.9f), true, -1f, true);
                        ci.DrawColoredPolygon(new[] { dp + new Vector2(-1.1f, -0.2f), dp + new Vector2(1.1f, -0.2f), dp + new Vector2(0, -2.6f) }, SnWater.WithAlpha(0.9f));
                    }
                    else
                    {
                        float q = (ph - 0.6f) / 0.4f;
                        ci.DrawSetTransform(top + new Vector2(0, 11f), 0f, new Vector2(1f, 0.4f));
                        ci.DrawArc(Vector2.Zero, 2f + q * 6f, 0f, Mathf.Tau, 14, SnWater.WithAlpha(0.7f * (1f - q)), 1f, true);
                        ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                    }
                    break;
                }
                case Noise.Breath:
                {
                    float per = Mathf.Max(0.8f, src.Period);
                    float ph = Mathf.PosMod(t, per) / per;
                    var c = p + new Vector2(0, -12f);
                    float r = ph < 0.5f ? ph * 2f : (1f - ph) * 2f;
                    ci.DrawCircle(c + new Vector2(-2f, 0), 1f + 1.6f * r, new Color(0.92f, 0.96f, 1f, 0.45f * r), true, -1f, true);
                    ci.DrawCircle(c + new Vector2(2f, -1f), 0.8f + 1.2f * (1f - r), new Color(0.92f, 0.96f, 1f, 0.35f * (1f - r)), true, -1f, true);
                    break;
                }
                case Noise.Radio:
                {
                    var c = p + new Vector2(9f, -10f);
                    ci.DrawLine(c, c + new Vector2(0, -6f), new Color("#c8d0d8"), 1f, true);
                    ci.DrawCircle(c + new Vector2(0, -6f), 1f, new Color("#ff6a5a"), true, -1f, true);
                    for (int i = 1; i <= 2; i++) ci.DrawArc(c + new Vector2(0, -6f), 2f + i * 2.4f, -0.8f, 0.8f, 6, SnSoft.WithAlpha(0.7f / i), 0.9f, true);
                    ci.DrawPolyline(new[] { c + new Vector2(3, 0), c + new Vector2(4.5f, -1.5f), c + new Vector2(6, 0), c + new Vector2(7.5f, -1.5f) }, SnSoft.WithAlpha(Mathf.PosMod(t * 5f, 1f) < 0.5f ? 0.8f : 0.3f), 0.8f, true);
                    break;
                }
                case Noise.Creak:
                {
                    var pts = new Vector2[7];
                    for (int i = 0; i < 7; i++) pts[i] = p + new Vector2(-9f + i * 3f, (i % 2 == 0 ? -1.5f : 1.5f) + Mathf.Sin(t * 3f + i) * 0.6f);
                    ci.DrawPolyline(pts, new Color(0.85f, 0.8f, 0.7f, 0.7f), 1f, true);
                    for (int i = 1; i < 6; i += 2) ci.DrawLine(pts[i], pts[i] + new Vector2(0.8f, src.Sub == 1 ? 2.2f : -2.2f), new Color(0.85f, 0.8f, 0.7f, 0.5f), 0.7f, true);
                    break;
                }
                case Noise.DoorShut:
                {
                    ci.DrawRect(new Rect2(p + new Vector2(-5f, -6f), new Vector2(3.6f, 12f)), new Color(0.75f, 0.8f, 0.85f, 0.6f));
                    ci.DrawRect(new Rect2(p + new Vector2(1.4f, -6f), new Vector2(3.6f, 12f)), new Color(0.75f, 0.8f, 0.85f, 0.6f));
                    for (int i = -1; i <= 1; i++) ci.DrawLine(p + new Vector2(0, -8f + i * 0), p + new Vector2(i * 3f, -12f), new Color(1, 1, 1, 0.7f), 0.9f, true);
                    break;
                }
                case Noise.Hum: PaintNote(ci, p + new Vector2(5f, -14f - Mathf.PosMod(t * 3f, 5f)), 1f, GsNote.WithAlpha(0.6f), false); break;
                case Noise.Sing: PaintNote(ci, p + new Vector2(-6f, -15f - Mathf.PosMod(t * 3f, 5f)), 1f, GsNote.WithAlpha(0.7f), true); break;
                case Noise.Whistle:
                {
                    var pts = new Vector2[9];
                    for (int i = 0; i < 9; i++) pts[i] = p + new Vector2(5f + i * 1.6f, -12f + Mathf.Sin(t * 9f + i * 0.9f) * 1.6f);
                    ci.DrawPolyline(pts, SnSoft.WithAlpha(0.6f), 0.8f, true);
                    ci.DrawArc(pts[0], 1.2f, 0f, Mathf.Tau, 8, SnSoft.WithAlpha(0.6f), 0.7f, true);
                    break;
                }
                case Noise.Tap:
                {
                    int on = (int)(t * 7f) % 3;
                    for (int i = 0; i < 3; i++) ci.DrawLine(p + new Vector2(6f + i * 2.4f, -6f), p + new Vector2(6f + i * 2.4f, i == on ? -8.6f : -7f), new Color(1, 1, 1, i == on ? 0.85f : 0.35f), 0.8f, true);
                    break;
                }
                case Noise.Voice:
                {
                    if (lod < 2) break;
                    for (int i = 1; i <= 2; i++) ci.DrawArc(p, 9f + i * 2.5f, -2.2f, -0.9f, 6, new Color(1, 1, 1, (src.Sub == 1 ? 0.8f : 0.4f) / i), src.Sub == 1 ? 1.4f : 0.8f, true);
                    break;
                }
            }
        }
        // 문 너머로 넘는 기계 소리 (열리면 밝게 넘고 · 닫히면 막대에 막혀 흐리다)
        foreach (var src in hs.Sources)
        {
            if (src.Kind is not (Noise.Bearing or Noise.Rattle or Noise.Hiss or Noise.Crackle) || src.Room < 0 || src.Room >= _world.Ship.Rooms.Count) continue;
            var room = _world.Ship.Rooms[src.Room];
            foreach (var d in room.Doors)
            {
                var o = d.RoomA == room ? d.RoomB : d.RoomA;
                if (o == null || o.Detached) continue;
                var dp = ToPx(d.Cell.Center);
                var dir = (ToPx(o.Center) - ToPx(room.Center)).Normalized();
                float t = _time;
                if (d.Openness >= 0.5f)
                    for (int i = 0; i < 2; i++)
                    {
                        float q = Mathf.PosMod(t * 0.8f + i * 0.5f, 1f);
                        ci.DrawArc(dp + dir * q * 10f, 5f, dir.Angle() - 0.8f, dir.Angle() + 0.8f, 6, SnMachine.WithAlpha(0.6f * (1f - q)), 1.1f, true);
                    }
                else
                {
                    ci.DrawArc(dp - dir * 4f, 4f, dir.Angle() - 0.7f, dir.Angle() + 0.7f, 6, SnMachine.WithAlpha(0.25f), 1f, true);
                    var n = new Vector2(-dir.Y, dir.X);
                    ci.DrawLine(dp + n * 4f, dp - n * 4f, new Color(0.6f, 0.65f, 0.7f, 0.6f), 1.6f, true);
                    ci.DrawLine(dp + n * 4f + dir * 2f, dp - n * 4f + dir * 2f, new Color(0.6f, 0.65f, 0.7f, 0.35f), 1.2f, true);
                }
            }
        }
    }

    private static void PaintStepMark(CanvasItem ci, Vector2 p, ShipSim.Core.Material mat, float a, float t, float s)
    {
        var c = p + new Vector2(0, 9f);
        switch (mat)
        {
            case ShipSim.Core.Material.Grate: // 쩔걱: 작은 별
                for (int i = 0; i < 4; i++) ci.DrawLine(c, c + Vector2.FromAngle(i * Mathf.Pi / 4f) * 2.6f, new Color(0.85f, 0.88f, 0.92f, 0.7f * a), 0.8f, true);
                for (int i = 0; i < 4; i++) ci.DrawLine(c, c - Vector2.FromAngle(i * Mathf.Pi / 4f) * 2.6f, new Color(0.85f, 0.88f, 0.92f, 0.7f * a), 0.8f, true);
                break;
            case ShipSim.Core.Material.Tile: // 또각: 꺾쇠 둘
                ci.DrawPolyline(new[] { c + new Vector2(-3, -1), c + new Vector2(-1.5f, 1), c + new Vector2(0, -1) }, new Color(1, 1, 1, 0.65f * a), 0.8f, true);
                ci.DrawPolyline(new[] { c + new Vector2(1, -1), c + new Vector2(2.5f, 1), c + new Vector2(4, -1) }, new Color(1, 1, 1, 0.45f * a), 0.8f, true);
                break;
            case ShipSim.Core.Material.Carpet: // 사박: 흐린 점선 호
                for (int i = 0; i < 4; i++) ci.DrawCircle(c + new Vector2(-3f + i * 2f, Mathf.Abs(i - 1.5f) * 0.6f), 0.45f, new Color(1, 1, 1, 0.3f * a), true, -1f, true);
                break;
            case ShipSim.Core.Material.Rubber: // 고무: 작은 점 하나
                ci.DrawCircle(c, 0.7f, new Color(1, 1, 1, 0.35f * a), true, -1f, true);
                break;
            case ShipSim.Core.Material.MetalPlate: // 금속판: 울리는 고리
                ci.DrawArc(c, 2.2f + Mathf.PosMod(t * 4f, 1f) * 1.5f, 0f, Mathf.Tau, 10, new Color(0.85f, 0.9f, 1f, 0.6f * a), 0.8f, true);
                break;
            default:
                ci.DrawArc(c, 2f, 0.3f, 2.8f, 6, new Color(1, 1, 1, 0.4f * a), 0.8f, true);
                break;
        }
    }
}
