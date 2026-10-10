using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.16 주컴퓨터 두뇌 2.0 — 배 화면 (읽기만): 컴퓨터가 부탁한 사람 머리 위 톱니 쪽지 + 맡긴 일 쪽으로 점선 · 쉬라는 부탁을 받은 사람의 초승달 ·
/// 의심하는 계기가 달린 방의 깨진 다이얼 · 회의 안건이 걸린 자원의 작은 클립보드(함교 위).
/// </summary>
public partial class ShipView
{
    private void PaintBrainAsks(CanvasItem ci)
    {
        var w = _world;
        var a = w.Automation;
        if (!a.Present || !a.MainOnline) return;
        var cm = a.CrewModel;
        foreach (var ask in cm.Asks.Values)
        {
            if (w.Crew.FirstOrDefault(c => c.Id == ask.CrewId) is not CrewMember c || c.Dead || c.Room == null) continue;
            var p = CrewPx(c) + new Vector2(0, -T * 0.75f);
            BrainIcons.AskChip(ci, p, 5.5f, _time);
            var o = w.Board.All.FirstOrDefault(x => x.Id == ask.OrderId && !x.Closed);
            if (o?.Target.CurrentRoom is Room r && r != c.Room) ci.DrawDashedLine(p, ToPx(r.Center), BrainIcons.Ask.WithAlpha(0.45f), 1.1f, 5f);
            if (Zoom > 1.1f) Gfx.TextCentered(ci, Fonts.Bold, p + new Vector2(0, -11f), ask.Title.Length > 14 ? ask.Title[..14] + "…" : ask.Title, 8, BrainIcons.Ask);
        }
        foreach (var id in cm.Resting)
        {
            if (w.Crew.FirstOrDefault(c => c.Id == id) is not CrewMember c || c.Dead || c.Room == null) continue;
            BrainIcons.RestChip(ci, CrewPx(c) + new Vector2(T * 0.45f, -T * 0.7f), 5f, _time);
        }
        foreach (var m in ShipForecast.Models)
        {
            var L = a.Outlook.Ledger(m.Key);
            if (!L.Suspect || L.RoomId < 0 || L.RoomId >= w.Ship.Rooms.Count) continue;
            var room = w.Ship.Rooms[L.RoomId];
            if (room.Detached) continue;
            var c = ToPx(room.Center) + new Vector2(-T * 0.8f, -T * 0.5f);
            ci.Circle(c, 9f, new Color(0.1f, 0.04f, 0.04f, 0.8f), true, -1f, true);
            BrainIcons.Learned(ci, c + new Vector2(0, 3f), 6f, "계기", false, _time);
            ci.DrawLine(c + new Vector2(-6f, -6f), c + new Vector2(6f, 6f), Palette.Danger.WithAlpha(0.7f), 1.2f, true); // 금 간 유리
            if (Zoom > 0.9f) Gfx.TextCentered(ci, Fonts.Bold, c + new Vector2(0, 16f), $"{m.Name} 계기 의심 {L.Reliability * 100:0}%", 8, Palette.Danger);
        }
        // 회의를 기다리는 컴퓨터 안건: 회의실(없으면 식당) 위 클립보드
        var motion = a.Planner.Pitches.LastOrDefault(p => p.Open && p.Via == "회의");
        if (motion != null && (w.Ship.RoomsOf(RoomType.MeetingRoom).FirstOrDefault() ?? w.Ship.RoomsOf(RoomType.Mess).FirstOrDefault()) is Room hall)
        {
            var c = ToPx(hall.Center) + new Vector2(T * 0.9f, -T * 0.6f + Mathf.Sin(_time * 2f) * 1.5f);
            ci.Circle(c, 11f, new Color(0.04f, 0.08f, 0.12f, 0.85f), true, -1f, true);
            ci.Arc(c, 11f, 0f, Mathf.Tau, 24, BrainIcons.Auth.WithAlpha(0.7f), 1.2f, true);
            BrainIcons.PlanBoard(ci, c, 5.5f, a.Planner.PlanOf(motion.Key)?.Mode ?? "대책", false, _time);
            for (int k = 0; k < ShipPlanner.MaxAttempts; k++) ci.Circle(c + new Vector2(-6f + k * 6f, 15f), 1.8f, k < motion.Attempt ? BrainIcons.Auth : new Color(1, 1, 1, 0.15f), true, -1f, true);
            if (Zoom > 0.8f) Gfx.TextCentered(ci, Fonts.Bold, c + new Vector2(0, 25f), $"컴퓨터 안건: {motion.Option} ({motion.Arg})", 9, BrainIcons.Auth);
        }
    }
}
