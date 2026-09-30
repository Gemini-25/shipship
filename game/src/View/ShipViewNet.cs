using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v12.1 배 전체 망: 방과 방을 잇는 전력 간선·급수관·환기 덕트. 평소엔 옅게, 해당 보기에서는 굵게, 끊긴 토막은 붉게.
public partial class ShipView
{
    private static readonly Color NetPower = new("#e8b84a");
    private static readonly Color NetWater = new("#4f9fdc");
    private static readonly Color NetAir = new("#8fcfc4");
    private static readonly Color NetData = new("#b58cff");

    private static Color NetColor(NetKind k) => k switch { NetKind.Power => NetPower, NetKind.Water => NetWater, NetKind.Data => NetData, _ => NetAir };

    private void PaintNet(CanvasItem ci, ViewMode mode)
    {
        var net = _world.Net;
        if (net.Links.Count == 0) return;
        bool strong = mode is ViewMode.Power or ViewMode.Pipes or ViewMode.Air or ViewMode.Sensors;
        foreach (var l in net.Links)
        {
            if (l.Room.Detached || l.Cells.Count == 0) continue;
            bool focus = mode switch { ViewMode.Power => l.Kind == NetKind.Power, ViewMode.Pipes => l.Kind == NetKind.Water, ViewMode.Air => l.Kind == NetKind.Air, ViewMode.Sensors => l.Kind == NetKind.Data, _ => false };
            bool hurt = l.Integrity < 0.95f;
            if (!strong && !hurt) continue; // 평소엔 다친 토막만 보인다 (바닥 밑)
            float lane = l.Kind switch { NetKind.Power => -0.3f, NetKind.Data => -0.15f, NetKind.Water => 0f, _ => 0.3f } * T;
            var off = new Vector2(lane, lane * 0.6f);
            var baseCol = NetColor(l.Kind);
            var col = l.Cut ? Palette.Danger : hurt ? baseCol.Lerp(Palette.Warning, 1f - l.Integrity) : baseCol;
            float a = focus ? 0.85f : strong ? 0.22f : 0.55f;
            float wdt = focus ? 2.4f : 1.3f;
            var pts = l.Cells.Select(c => CellRect(c).GetCenter() + off).ToArray();
            if (pts.Length == 1) { ci.DrawCircle(pts[0], wdt, col.WithAlpha(a)); continue; }
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                if (l.Cut && i == pts.Length / 2 - (pts.Length > 2 ? 0 : 0))
                {
                    // 끊긴 자리: 틈 + 튀는 불꽃(전선) / 물방울(급수) / 바람(덕트)
                    var mid = (pts[i] + pts[i + 1]) * 0.5f;
                    var dir = (pts[i + 1] - pts[i]).Normalized();
                    ci.DrawLine(pts[i], mid - dir * 4f, col.WithAlpha(a), wdt, true);
                    ci.DrawLine(mid + dir * 4f, pts[i + 1], col.WithAlpha(a), wdt, true);
                    float ph = Mathf.PosMod(_time * 3f + l.Id * 0.37f, 1f);
                    if (l.Kind is NetKind.Power or NetKind.Data && ph < 0.35f)
                        for (int k = 0; k < 3; k++)
                        {
                            float ang = (l.Id * 1.7f + k * 2.1f + _time * 9f) % Mathf.Tau;
                            ci.DrawLine(mid, mid + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (3f + 4f * ph), new Color(1f, 0.9f, 0.5f, 0.9f - ph), 1f, true);
                        }
                    else if (l.Kind == NetKind.Water)
                        ci.DrawCircle(mid + new Vector2(0, 2f + 6f * ph), 1.4f, NetWater.WithAlpha(0.8f - 0.6f * ph));
                    else if (l.Kind == NetKind.Air)
                        ci.DrawArc(mid, 2f + 5f * ph, 0f, Mathf.Pi, 6, NetAir.WithAlpha(0.6f - 0.5f * ph), 1f, true);
                    ci.DrawLine(mid + new Vector2(-3, -3), mid + new Vector2(3, 3), Palette.Danger.WithAlpha(0.9f), 1.5f, true);
                    ci.DrawLine(mid + new Vector2(-3, 3), mid + new Vector2(3, -3), Palette.Danger.WithAlpha(0.9f), 1.5f, true);
                    continue;
                }
                if (l.Temp) // 임시로 이은 토막: 점선
                {
                    var d = pts[i + 1] - pts[i];
                    for (float s = 0; s < d.Length(); s += 6f) ci.DrawLine(pts[i] + d.Normalized() * s, pts[i] + d.Normalized() * Mathf.Min(d.Length(), s + 3f), col.WithAlpha(a), wdt, true);
                }
                else ci.DrawLine(pts[i], pts[i + 1], col.WithAlpha(a), wdt, true);
            }
            // 흐름: 이어져 있고 이 망을 보고 있으면 작은 점이 흐른다
            if (focus && !l.Cut && pts.Length > 1)
            {
                float ph = Mathf.PosMod(_time * (l.Kind == NetKind.Power ? 1.8f : 0.9f) + l.Id * 0.13f, 1f);
                int seg = Mathf.Min(pts.Length - 2, (int)(ph * (pts.Length - 1)));
                float u = ph * (pts.Length - 1) - seg;
                ci.DrawCircle(pts[seg].Lerp(pts[seg + 1], u), 1.6f, Colors.White.WithAlpha(0.7f));
            }
        }
        // 이 보기에서 망이 끊겨 못 받는 방: 테두리 빗금
        if (strong)
            foreach (var room in _world.Ship.LiveRooms)
            {
                var k = mode switch { ViewMode.Power => NetKind.Power, ViewMode.Pipes => NetKind.Water, ViewMode.Sensors => NetKind.Data, _ => NetKind.Air };
                if (UtilityNet.Fed(k, room)) continue;
                if (k == NetKind.Water && !UtilityNet.NeedsWater(room)) continue;
                FillRoom(ci, room, Palette.Danger.WithAlpha(0.1f));
            }
    }
}
