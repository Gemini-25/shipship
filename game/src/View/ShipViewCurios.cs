using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v17.6 색약 팔레트(방 바닥 무늬 · 사람 표식) · 방 스피커 음악(스피커 · 음표 · 벽에서 먹먹해지는 물결)
// v17.9 숨은 것(찾은 물건 · 가까이서만 보이는 반짝임) · 이상 현상(방마다 다른 움직임) · 창밖에 지나가는 것 — 읽기만.

public partial class ShipView
{
    private void PaintAccessUnder(CanvasItem ci)
    {
        var w = _world;
        var ship = w.Ship;
        float zoom = Zoom;
        // 색약 팔레트: 방마다 바닥 무늬 (색이 비슷해도 무늬로 갈린다)
        if (ColorSafe.On)
            foreach (var room in ship.LiveRooms)
            {
                var wv = UiAccess.WeaveOf(room.Kind);
                if (wv == Weave.Plain) continue;
                var col = Palette.Room(room.Kind).WithAlpha(0.22f);
                foreach (var c in room.Cells) ColorSafe.DrawWeave(ci, CellRect(c), wv, col, T * 0.5f);
            }
        // 창밖에 지나가는 것: 배 바깥 하늘을 천천히 가로지른다
        foreach (var e in w.Curios.Active)
        {
            var spec = CurioTable.Of(e.Key);
            if (spec.Kind != CurioKind.Passing) continue;
            float p = e.Progress(w.Tick);
            float fade = Mathf.Clamp(Mathf.Min(p, 1f - p) * 6f, 0f, 1f);
            var b = Bounds;
            var at = new Vector2(Mathf.Lerp(b.End.X + T * 6f, b.Position.X - T * 6f, p), b.Position.Y - T * 3.5f + Mathf.Sin(p * Mathf.Pi) * T * 1.2f);
            float size = spec.Key is "nebula" or "aurora" or "ice_cloud" ? T * 6f : T * 3f;
            if (spec.Key == "aurora") at = new Vector2(b.GetCenter().X, b.Position.Y - T * 2f);
            CurioArt.Draw(ci, spec, at, size, true, _time, 0.85f * fade);
        }
        // 숨은 물건: 찾은 것은 제자리에 그림 · 못 찾은 것은 가까이에서만 가끔 반짝
        foreach (var pl in w.Curios.Placed)
        {
            var spec = CurioTable.Of(pl.Key);
            var c = ToPx(pl.At.Center);
            if (pl.Found)
            {
                if (spec.Kind == CurioKind.Graffiti) CurioArt.Draw(ci, spec, c + new Vector2(0, -T * 0.25f), T * 0.32f, true, _time, 0.55f);
                else
                {
                    ci.DrawCircle(c, T * 0.3f, new Color(spec.Color).WithAlpha(0.08f), true, -1f, true);
                    CurioArt.Draw(ci, spec, c, T * 0.22f, true, _time);
                }
            }
            else if (zoom >= 1.4f)
            {
                float ph = Mathf.PosMod(_time * 0.17f + pl.At.X * 0.37f + pl.At.Y * 0.21f, 1f);
                if (ph < 0.06f)
                {
                    float a = Mathf.Sin(ph / 0.06f * Mathf.Pi);
                    var g = c + new Vector2(T * 0.18f, -T * 0.12f);
                    ci.DrawLine(g + new Vector2(-T * 0.12f, 0), g + new Vector2(T * 0.12f, 0), new Color(1, 1, 0.9f, 0.7f * a), 1.2f, true);
                    ci.DrawLine(g + new Vector2(0, -T * 0.12f), g + new Vector2(0, T * 0.12f), new Color(1, 1, 0.9f, 0.7f * a), 1.2f, true);
                }
            }
        }
        // 이상 현상: 방마다 다른 움직임
        foreach (var e in w.Curios.Active)
        {
            if (e.Room < 0 || e.Room >= ship.Rooms.Count) continue;
            var room = ship.Rooms[e.Room];
            var rc = ToPx(room.Center);
            float p = e.Progress(w.Tick), fade = Mathf.Clamp(Mathf.Min(p, 1f - p) * 5f, 0f, 1f);
            switch (e.Key)
            {
                case "floating_drops":
                    for (int i = 0; i < 9; i++)
                    {
                        float ph = Mathf.PosMod(_time * 0.25f + i * 0.13f, 1f);
                        var cell = room.Cells[(i * 7) % room.Cells.Count];
                        var d = ToPx(cell.Center) + new Vector2(0, T * 0.4f - ph * T * 1.2f);
                        ci.DrawCircle(d, 2.2f, new Color("#5ec8e6").WithAlpha(0.8f * fade * (1f - ph)), true, -1f, true);
                    }
                    break;
                case "frost_fern":
                    foreach (var cell in room.Cells.Where((c, i) => i % 3 == 0).Take(14))
                    {
                        var o = ToPx(cell.Center);
                        for (int k = 0; k < 3; k++) ci.DrawLine(o, o + Vector2.FromAngle(-Mathf.Pi / 2f + (k - 1) * 0.6f) * T * 0.35f, new Color("#e0f4ff").WithAlpha(0.45f * fade), 1f, true);
                    }
                    break;
                case "warm_spot":
                    for (int k = 0; k < 3; k++) { float ph = Mathf.PosMod(_time * 0.5f + k / 3f, 1f); ci.DrawArc(rc, T * (0.2f + ph), 0, Mathf.Tau, 24, new Color("#ff7a5c").WithAlpha(0.5f * (1f - ph) * fade), 1.5f, true); }
                    break;
                case "afterglow":
                    ci.DrawCircle(rc, T * 1.4f, new Color("#ffe8a0").WithAlpha(0.06f * fade + 0.04f * Mathf.Sin(_time * 1.3f) * fade), true, -1f, true);
                    ci.DrawCircle(rc, T * 0.5f, new Color("#ffe8a0").WithAlpha(0.12f * fade), true, -1f, true);
                    break;
                case "empty_steps":
                    for (int i = 0; i < 6; i++)
                    {
                        float ph = Mathf.PosMod(_time * 0.6f - i * 0.15f, 1f);
                        if (ph > 0.5f) continue;
                        var cell = room.Cells[(i * 3 + (int)(_time * 0.6f) * 3) % room.Cells.Count];
                        var o = ToPx(cell.Center) + new Vector2(i % 2 == 0 ? -3 : 3, 0);
                        ci.DrawColoredPolygon(new[] { o + new Vector2(-2, -4), o + new Vector2(2, -4), o + new Vector2(2.5f, 3), o + new Vector2(-2.5f, 3) }, new Color("#9aa3b5").WithAlpha(0.5f * (1f - ph * 2f) * fade));
                    }
                    break;
                case "old_voice":
                    for (int k = 1; k <= 3; k++) ci.DrawArc(rc, T * 0.35f * k, -0.7f, 0.7f, 10, new Color("#6ee7b7").WithAlpha((0.5f + 0.4f * Mathf.Sin(_time * 9f + k)) * fade * 0.6f), 1.4f, true);
                    break;
                case "spinning_needle":
                    ci.DrawArc(rc, T * 0.45f, 0, Mathf.Tau, 20, new Color("#f2b134").WithAlpha(0.5f * fade), 1.2f, true);
                    ci.DrawLine(rc, rc + Vector2.FromAngle(_time * 7f) * T * 0.42f, new Color("#f2b134").WithAlpha(0.9f * fade), 2f, true);
                    break;
                case "lone_door":
                    foreach (var d in room.Doors.Take(1))
                    {
                        var dp = ToPx(d.Cell.Center);
                        ci.DrawRect(new Rect2(dp - new Vector2(T * 0.5f, T * 0.5f), new Vector2(T, T)), new Color("#aab3c5").WithAlpha(0.18f * fade * (0.5f + 0.5f * Mathf.Sin(_time * 2f))), true);
                    }
                    break;
            }
        }
    }

    private void PaintAccessOver(CanvasItem ci)
    {
        var w = _world;
        var ship = w.Ship;
        // 방 스피커 음악: 스피커 · 퍼지는 물결 · 음표 (먹먹하게 듣는 방엔 흐린 물결)
        foreach (var t in w.Music.Playing)
        {
            if (t.Room >= ship.Rooms.Count) continue;
            var room = ship.Rooms[t.Room];
            var sp = ToPx(t.At) + new Vector2(0, -T * 0.42f);
            var box = new Rect2(sp - new Vector2(T * 0.22f, T * 0.12f), new Vector2(T * 0.44f, T * 0.24f));
            ci.DrawRect(box, new Color("#1b2230"), true);
            ci.DrawRect(box, new Color("#5f6879"), false, 1f);
            for (int k = 0; k < 3; k++) ci.DrawCircle(box.Position + new Vector2(T * (0.09f + k * 0.13f), T * 0.12f), T * 0.04f, new Color("#8d93a6"), true, -1f, true);
            var col = t.Memorial ? new Color("#9fb4ff") : Palette.Crew(Math.Max(0, t.By)).Lightened(0.2f);
            float loud = t.Loud;
            for (int k = 0; k < 3; k++)
            {
                float ph = Mathf.PosMod(_time * 0.7f + k / 3f, 1f);
                ci.DrawArc(sp, T * (0.4f + ph * 2.2f * loud * 2f), 0.2f, Mathf.Pi - 0.2f, 16, col.WithAlpha(0.35f * (1f - ph)), 1.2f, true);
            }
            for (int k = 0; k < 2; k++)
            {
                float ph = Mathf.PosMod(_time * 0.4f + k * 0.5f + t.Id * 0.17f, 1f);
                var np = sp + new Vector2((k == 0 ? -1 : 1) * T * (0.3f + ph * 0.5f), T * 0.2f - ph * T * 0.9f);
                PaintMusicNote(ci, np, T * 0.14f, col.WithAlpha(0.8f * (1f - ph)), t.Genre);
            }
        }
        // 벽 · 문 너머로 먹먹하게 듣는 사람: 머리 위 흐린 물결
        foreach (var (id, (tune, level, muffled)) in w.Music.Listening)
        {
            if (!muffled) continue;
            var c = w.Crew.FirstOrDefault(x => x.Id == id);
            if (c == null || c.Dead) continue;
            var p = CrewPx(c) + new Vector2(0, -T * 0.9f);
            float a = Mathf.Clamp(level * 3f, 0.15f, 0.6f);
            var pts = Enumerable.Range(0, 7).Select(i => p + new Vector2(-T * 0.25f + i * T * 0.083f, Mathf.Sin(_time * 3f + i) * 1.5f)).ToArray();
            ci.DrawPolyline(pts, new Color("#9aa3b5").WithAlpha(a), 1.2f, true);
        }
        // 색약 팔레트: 사람마다 표식 (모양 + 채움)
        if (ColorSafe.On)
            foreach (var c in w.Crew)
            {
                if (c.Dead || c.Away) continue;
                var p = CrewPx(c) + new Vector2(T * 0.38f, -T * 0.55f);
                ColorSafe.DrawMark(ci, c.Id, p, T * 0.13f, Palette.Crew(c.Id), new Color("#0b0e13"));
            }
    }

    /// <summary>갈래마다 다른 음표 (피아노 · 옛 노래 · 재즈 · 전자음 · 민요 · 합창).</summary>
    private static void PaintMusicNote(CanvasItem ci, Vector2 p, float s, Color col, byte genre)
    {
        switch (genre % 6)
        {
            case 0: ci.DrawCircle(p, s * 0.6f, col, true, -1f, true); ci.DrawLine(p + new Vector2(s * 0.55f, 0), p + new Vector2(s * 0.55f, -s * 2f), col, 1.2f, true); break;
            case 1: ci.DrawCircle(p, s * 0.55f, col, true, -1f, true); ci.DrawLine(p + new Vector2(s * 0.5f, 0), p + new Vector2(s * 0.5f, -s * 2f), col, 1.2f, true); ci.DrawLine(p + new Vector2(s * 0.5f, -s * 2f), p + new Vector2(s * 1.2f, -s * 1.4f), col, 1.2f, true); break;
            case 2: ci.DrawCircle(p, s * 0.5f, col, true, -1f, true); ci.DrawCircle(p + new Vector2(s * 1.3f, -s * 0.3f), s * 0.5f, col, true, -1f, true); ci.DrawLine(p + new Vector2(s * 0.45f, -s * 1.8f), p + new Vector2(s * 1.75f, -s * 2.1f), col, 1.6f, true); break;
            case 3: ci.DrawRect(new Rect2(p - new Vector2(s * 0.5f, s * 0.5f), new Vector2(s, s)), col, true); ci.DrawLine(p + new Vector2(s * 0.5f, 0), p + new Vector2(s * 0.5f, -s * 2f), col, 1.2f, true); break;
            case 4: ci.DrawArc(p, s * 0.6f, 0, Mathf.Tau, 10, col, 1.2f, true); ci.DrawLine(p + new Vector2(s * 0.6f, 0), p + new Vector2(s * 0.6f, -s * 2f), col, 1.2f, true); break;
            default: for (int k = 0; k < 3; k++) ci.DrawCircle(p + new Vector2(k * s * 0.8f, -k * s * 0.3f), s * 0.35f, col, true, -1f, true); break;
        }
    }
}
