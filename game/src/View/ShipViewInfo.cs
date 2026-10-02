using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v17.3 컵 (사람마다 모양 · 무늬 · 손잡이 — 식탁 위 · 바닥 · 깨진 조각 · 금빛으로 이어 붙인 자국) · 벽에 건 사진 (찍은 날의 장면 · 사람 · 액자) ·
// 완성한 모형 · 늦게 건넨 선물 · 머리맡 독서등 (고장 나 깜빡 · 고친 뒤 따뜻한 빛) · 개수대 그릇 더미 · 설거지 당번표 · 손목 단말 화면 빛.
// 읽기만 한다 (시뮬레이션을 바꾸지 않는다).
public partial class ShipView
{
    private readonly Dictionary<int, (Cell at, Cell dir)?> _rosterSpot = new();

    private void PaintInfo(CanvasItem ci)
    {
        var w = _world;
        var info = w.Info;
        long now = w.Tick;
        bool close = Zoom > 0.7f;

        // 개수대에 쌓인 그릇 · 거품
        if (info.Dirty > 0)
            foreach (var r in w.Ship.RoomsOf(RoomType.Mess))
            {
                var sink = r.Furniture.Where(f => f.Type is FurnitureType.DishWasher or FurnitureType.MealDispenser or FurnitureType.Stove).OrderBy(f => f.Type == FurnitureType.DishWasher ? 0 : 1).ThenBy(f => f.Id).FirstOrDefault();
                if (sink == null) continue;
                bool washing = w.Crew.Any(c => !c.Dead && c.Room == r && c.Job?.Activity is InfoActivity && c.Job.Label == "설거지" && c.Pose == Pose.Working);
                InfoArt.DishPile(ci, ToPx(sink.Center) + new Vector2(T * 0.55f, -T * 0.2f), Math.Min(info.Dirty, 14), washing, _time);
                break;
            }

        // 설거지 당번표 (식당 벽)
        if (info.Roster.Count > 0 && w.Ship.RoomsOf(RoomType.Mess).FirstOrDefault() is Room mess)
        {
            if (!_rosterSpot.TryGetValue(mess.Id, out var spot)) _rosterSpot[mess.Id] = spot = info.WallSpot(mess, mess.Center + new System.Numerics.Vector2(2f, -3f));
            if (spot is var (at, dir)) InfoArt.Roster(ci, CellRect(at).GetCenter() + new Vector2(dir.X, dir.Y) * T * 0.38f, info.Roster, info.OnDuty()?.Id ?? -1, close);
        }

        // 벽에 건 사진
        foreach (var p in info.Photos)
        {
            if (!p.Hung) continue;
            var rc = CellRect(p.Wall);
            var c = rc.GetCenter() + new Vector2(p.WallDir.X, p.WallDir.Y) * T * 0.36f;
            bool side = p.WallDir.X != 0;
            var size = side ? new Vector2(T * 0.34f, T * 0.46f) : new Vector2(T * 0.52f, T * 0.38f);
            InfoArt.Photo(ci, new Rect2(c - size / 2f, size), p, w, _time, close);
        }

        // 컵 · 조각 · 완성한 모형 · 선물
        foreach (var b in w.Belongings.All)
        {
            if (b.Holder >= 0 || b.At is not Cell cell) continue;
            var col = Palette.Crew(b.Owner >= 0 ? b.Owner : 0);
            if (b.Kind == BelongingKind.Mug)
            {
                if (!b.Usable)
                {
                    var k = info.Cases.LastOrDefault(x => x.Cup == b.Id && !x.Swept);
                    if (k != null) InfoArt.Shards(ci, CellRect(cell).GetCenter(), b, k, col, (now - k.Tick) / (float)SimTime.TicksPerSecond);
                    continue;
                }
                var table = info.TableOf(b);
                Vector2 pos;
                if (table is Cell tc)
                {
                    var tcen = CellRect(tc).GetCenter();
                    var toward = CellRect(cell).GetCenter() - tcen;
                    pos = tcen + toward.Normalized() * T * 0.28f + new Vector2((b.Id % 3 - 1) * 3f, 0f);
                }
                else pos = CellRect(cell).GetCenter() + new Vector2((b.Id % 3 - 1) * 5f, (b.Id / 3 % 3 - 1) * 4f);
                float steam = info.OnTable.TryGetValue(b.Id, out var tcup) && !tcup.Left && now - tcup.Since < SimTime.Minutes(25) ? 1f : 0f;
                InfoArt.Cup(ci, pos, b, col, 1f, info.Mended(b), steam, _time);
                continue;
            }
            if (b.Kind == BelongingKind.Artwork && info.Made(b) is Todo t)
                InfoArt.Made(ci, CellRect(cell).GetCenter() + new Vector2(0f, -T * 0.18f), t.What, b.Id, 1f, Palette.Crew(b.Maker >= 0 ? b.Maker : b.Owner), _time);
        }

        // 하던 모형 · 선물 · 컵 붙이기: 손에 든 채 조금씩 (진척만큼 형태가 생긴다)
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Job?.Activity is not InfoActivity || c.Pose is not (Pose.Sitting or Pose.Working)) continue;
            var t = info.TodosOf(c).FirstOrDefault(x => c.Job.Label == InfoSystem.TodoText(x, w));
            if (t == null) continue;
            var hand = CrewPx(c) + new Vector2(c.Facing.X, c.Facing.Y) * CrewRadius * 1.25f;
            if (t.Kind is TodoKind.Model or TodoKind.Gift) InfoArt.Made(ci, hand, t.What, t.Id * 7 + 3, t.Progress, Palette.Crew(c.Id), _time);
            else if (t.Kind == TodoKind.MendCup && w.Belongings.Get(t.Item) is Belonging cup)
            {
                InfoArt.Cup(ci, hand, cup, Palette.Crew(cup.Owner), 0.9f, false, 0f, _time);
                InfoArt.Seams(ci, hand, cup.Id, t.Progress, 0.9f);
            }
        }

        // 머리맡 독서등 (고치려던 사람 · 고친 사람)
        float hour = SimTime.HourOfDay(now);
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Bed is not Furniture bed || bed.Cells.Count == 0) continue;
            bool fixedLamp = info.LampFixed(c);
            bool broken = !fixedLamp && info.TodosOf(c).Any(x => x.Kind == TodoKind.Lamp);
            if (!fixedLamp && !broken) continue;
            var head = ToPx(bed.Center) + new Vector2(-T * 0.32f, -T * 0.32f);
            bool on = c.Room == bed.Room && c.IsAwake && (hour >= 20f || hour < 6f || c.Pose == Pose.Sitting);
            InfoArt.Lamp(ci, head, fixedLamp, on, _time, c.Id, info.TodosOf(c).FirstOrDefault(x => x.Kind == TodoKind.Lamp)?.Progress ?? 1f);
        }

        // 손목 단말 화면 빛 (메신저를 들여다보는 중)
        foreach (var (id, until) in info.Chat.Reading)
        {
            if (until <= now || id >= w.Crew.Count) continue;
            var c = w.Crew[id];
            if (c.Dead || c.CarriedBy != null) continue;
            var p = CrewPx(c) + new Vector2(CrewRadius * 0.55f, CrewRadius * 0.15f);
            InfoArt.Tablet(ci, p, Palette.Crew(c.Id), _time + id);
        }
    }
}

/// <summary>v17.3 개인 물건 그림 (화면 · 메신저 패널이 같이 쓴다).</summary>
public static class InfoArt
{
    private static float H(int a, int b) => ((uint)(a * 73856093 ^ b * 19349663) % 10007) / 10007f;

    // ───────────────────────────── 컵 ─────────────────────────────

    /// <summary>사람마다 다른 컵: 모양(머그 · 찻잔 · 법랑 컵 · 텀블러) × 무늬(줄 · 점 · 띠 · 별) × 손잡이 방향 · 색.</summary>
    public static void Cup(CanvasItem ci, Vector2 c, Belonging b, Color owner, float scale, bool mended, float steam, float time)
    {
        float s = 5.2f * scale;
        int shape = b.Id % 4, pattern = b.Id / 4 % 4;
        bool left = b.Id % 7 == 3;
        var body = owner.Lightened(0.15f * (b.Id * 37 % 5) / 4f);
        var ink = new Color(0.08f, 0.09f, 0.12f, 0.9f);
        var glaze = new Color(1f, 1f, 1f, 0.55f);
        float hs = left ? -1f : 1f;
        Rect2 r;
        switch (shape)
        {
            case 0: // 키 큰 머그
                r = new Rect2(c.X - s * 0.45f, c.Y - s * 0.65f, s * 0.9f, s * 1.3f);
                ci.DrawRect(r, body);
                ci.DrawArc(c + new Vector2(hs * s * 0.62f, 0f), s * 0.32f, -Mathf.Pi / 2f, Mathf.Pi / 2f, 8, body.Darkened(0.2f), 1.4f, true);
                break;
            case 1: // 받침 있는 찻잔
            {
                ci.DrawLine(new Vector2(c.X - s * 0.8f, c.Y + s * 0.5f), new Vector2(c.X + s * 0.8f, c.Y + s * 0.5f), body.Darkened(0.3f), 1.6f, true);
                var bowl = new[] { new Vector2(c.X - s * 0.6f, c.Y - s * 0.35f), new Vector2(c.X + s * 0.6f, c.Y - s * 0.35f), new Vector2(c.X + s * 0.4f, c.Y + s * 0.4f), new Vector2(c.X - s * 0.4f, c.Y + s * 0.4f) };
                ci.DrawColoredPolygon(bowl, body);
                ci.DrawArc(c + new Vector2(hs * s * 0.62f, -s * 0.02f), s * 0.22f, -Mathf.Pi / 2f, Mathf.Pi / 2f, 6, body.Darkened(0.2f), 1.2f, true);
                r = new Rect2(c.X - s * 0.6f, c.Y - s * 0.35f, s * 1.2f, s * 0.75f);
                break;
            }
            case 2: // 법랑 캠핑 컵 (검은 테 · 이 빠진 자국)
                r = new Rect2(c.X - s * 0.55f, c.Y - s * 0.45f, s * 1.1f, s * 0.95f);
                ci.DrawRect(r, body.Lightened(0.35f));
                ci.DrawLine(new Vector2(r.Position.X, r.Position.Y), new Vector2(r.End.X, r.Position.Y), ink, 1.3f);
                ci.DrawCircle(new Vector2(r.Position.X + s * 0.3f, r.Position.Y + 0.6f), 0.9f, ink);
                ci.DrawArc(c + new Vector2(hs * s * 0.72f, -s * 0.05f), s * 0.28f, -Mathf.Pi / 2f, Mathf.Pi / 2f, 8, ink.WithAlpha(0.7f), 1.1f, true);
                break;
            default: // 뚜껑 달린 텀블러
            {
                var poly = new[] { new Vector2(c.X - s * 0.42f, c.Y - s * 0.7f), new Vector2(c.X + s * 0.42f, c.Y - s * 0.7f), new Vector2(c.X + s * 0.3f, c.Y + s * 0.7f), new Vector2(c.X - s * 0.3f, c.Y + s * 0.7f) };
                ci.DrawColoredPolygon(poly, body);
                ci.DrawRect(new Rect2(c.X - s * 0.47f, c.Y - s * 0.85f, s * 0.94f, s * 0.22f), body.Darkened(0.35f));
                r = new Rect2(c.X - s * 0.4f, c.Y - s * 0.6f, s * 0.8f, s * 1.2f);
                break;
            }
        }
        // 무늬
        var pat = shape == 2 ? body.Darkened(0.25f) : body.Lightened(0.45f);
        switch (pattern)
        {
            case 0:
                for (int i = 1; i <= 2; i++) { float y = r.Position.Y + r.Size.Y * i / 3f; ci.DrawLine(new Vector2(r.Position.X + 0.5f, y), new Vector2(r.End.X - 0.5f, y), pat, 1f); }
                break;
            case 1:
                for (int i = 0; i < 4; i++) ci.DrawCircle(new Vector2(r.Position.X + r.Size.X * (0.25f + 0.5f * (i % 2)), r.Position.Y + r.Size.Y * (0.3f + 0.4f * (i / 2))), 0.8f, pat);
                break;
            case 2:
                ci.DrawRect(new Rect2(r.Position.X, r.Position.Y + r.Size.Y * 0.4f, r.Size.X, r.Size.Y * 0.22f), pat);
                break;
            default:
                Star(ci, r.GetCenter(), s * 0.3f, pat);
                break;
        }
        ci.DrawLine(new Vector2(r.Position.X + 1f, r.Position.Y + 1f), new Vector2(r.Position.X + 1f, r.End.Y - 1f), glaze, 0.8f);
        if (mended) Seams(ci, c, b.Id, 1f, scale);
        if (steam > 0f)
            for (int i = 0; i < 2; i++)
            {
                float t = (time * 0.6f + i * 0.5f + b.Id * 0.13f) % 1f;
                var p0 = c + new Vector2(-1.5f + 3f * i + Mathf.Sin(t * 6f + i) * 1.5f, -s * 0.9f - t * 7f);
                ci.DrawCircle(p0, 1.2f + t, new Color(1f, 1f, 1f, 0.25f * (1f - t) * steam));
            }
    }

    /// <summary>금빛 이음매 (이어 붙인 자국 — 컵마다 다른 갈래).</summary>
    public static void Seams(CanvasItem ci, Vector2 c, int id, float progress, float scale)
    {
        float s = 5.2f * scale;
        var gold = new Color(0.98f, 0.8f, 0.3f, 0.95f);
        int n = 2 + id % 2;
        for (int k = 0; k < n; k++)
        {
            var pts = new List<Vector2>();
            float y0 = -s * 0.5f + s * (0.3f + 0.35f * k);
            int segs = 4;
            int shown = Math.Max(1, (int)Math.Ceiling(segs * Math.Clamp(progress, 0f, 1f)));
            for (int i = 0; i <= shown; i++) pts.Add(c + new Vector2(-s * 0.5f + s * i / segs, y0 + (H(id, k * 10 + i) - 0.5f) * s * 0.4f));
            if (pts.Count >= 2) ci.DrawPolyline(pts.ToArray(), gold, 0.9f, true);
        }
    }

    /// <summary>깨진 컵 조각: 컵마다 조각 수 · 모양 · 흩어짐이 다르고, 한 조각에는 손잡이가 붙어 있다 · 쏟은 음료 자국.</summary>
    public static void Shards(CanvasItem ci, Vector2 c, Belonging b, CupCase k, Color owner, float ageSec)
    {
        var body = owner.Lightened(0.15f * (b.Id * 37 % 5) / 4f);
        // 쏟은 음료 (차 · 커피 · 물)
        var spill = (b.Id % 3) switch { 0 => new Color(0.36f, 0.22f, 0.12f, 0.35f), 1 => new Color(0.55f, 0.4f, 0.18f, 0.3f), _ => new Color(0.5f, 0.7f, 0.9f, 0.28f) };
        DrawBlob(ci, c + new Vector2(2f, 3f), 7f + 2f * H(k.Id, 1), spill, k.Id);
        int n = 4 + (k.Id + b.Id) % 4;
        for (int i = 0; i < n; i++)
        {
            float ang = Mathf.Tau * (i + H(k.Id, i)) / n;
            float rad = 3f + 9f * H(b.Id, i + 3);
            var p = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
            float sz = 1.6f + 2.6f * H(k.Id + b.Id, i + 7);
            float rot = H(b.Id, i + 11) * Mathf.Tau;
            int corners = 3 + (i + b.Id) % 2;
            var poly = new Vector2[corners];
            for (int j = 0; j < corners; j++)
            {
                float a = rot + Mathf.Tau * j / corners + (H(i, j + b.Id) - 0.5f) * 0.8f;
                poly[j] = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sz * (0.6f + 0.6f * H(j, i + k.Id));
            }
            ci.DrawColoredPolygon(poly, i % 3 == 0 ? body.Lightened(0.35f) : body);
            ci.DrawLine(poly[0], poly[1], new Color(1f, 1f, 1f, 0.6f), 0.7f, true); // 유약이 반짝인다
            if (i == 0) ci.DrawArc(p + new Vector2(sz * 0.9f, 0f), sz * 0.7f, -Mathf.Pi / 2f, Mathf.Pi / 2f, 6, body.Darkened(0.2f), 1.2f, true); // 손잡이가 붙은 조각
        }
        // 깨진 순간: 짧게 튀는 선
        if (ageSec >= 0f && ageSec < 1.4f)
        {
            float t = ageSec / 1.4f;
            for (int i = 0; i < 8; i++)
            {
                var d = new Vector2(Mathf.Cos(i * Mathf.Tau / 8f), Mathf.Sin(i * Mathf.Tau / 8f));
                ci.DrawLine(c + d * (4f + 10f * t), c + d * (7f + 14f * t), new Color(1f, 1f, 1f, 0.8f * (1f - t)), 1f, true);
            }
        }
    }

    private static void DrawBlob(CanvasItem ci, Vector2 c, float r, Color col, int seed)
    {
        var pts = new Vector2[10];
        for (int i = 0; i < pts.Length; i++)
        {
            float a = Mathf.Tau * i / pts.Length;
            pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.7f) * r * (0.7f + 0.5f * H(seed, i));
        }
        ci.DrawColoredPolygon(pts, col);
    }

    private static void Star(CanvasItem ci, Vector2 c, float r, Color col)
    {
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float a = -Mathf.Pi / 2f + i * Mathf.Pi / 5f;
            float rr = i % 2 == 0 ? r : r * 0.45f;
            pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
        }
        ci.DrawColoredPolygon(pts, col);
    }

    // ───────────────────────────── 사진 ─────────────────────────────

    /// <summary>사진 한 장: 액자(찍은 사람마다) · 배경(장소 · 창밖) · 그날의 사람들(그 사람 색 · 머리 모양) · 그날의 물건(케이크 · 안전모 · 식탁).</summary>
    public static void Photo(CanvasItem ci, Rect2 r, PhotoInfo p, World w, float time, bool detail)
    {
        int frame = p.Taker % 4;
        var fcol = frame switch { 0 => new Color(0.55f, 0.37f, 0.2f), 1 => new Color(0.12f, 0.12f, 0.14f), 2 => new Color(0.92f, 0.9f, 0.86f), _ => new Color(0.78f, 0.62f, 0.3f) };
        ci.DrawRect(r.Grow(1.6f), new Color(0f, 0f, 0f, 0.35f));
        ci.DrawRect(r.Grow(1.2f), fcol);
        var img = r.Grow(-0.6f);
        bool window = p.Scene == PhotoScene.Window;
        var bg = window ? new Color(0.04f, 0.05f, 0.12f) : p.Place switch
        {
            RoomType.Mess => new Color(0.82f, 0.68f, 0.5f),
            RoomType.Lounge => new Color(0.36f, 0.6f, 0.62f),
            RoomType.Quarters => new Color(0.62f, 0.56f, 0.72f),
            RoomType.Bridge => new Color(0.2f, 0.28f, 0.45f),
            _ => new Color(0.5f, 0.55f, 0.6f),
        };
        ci.DrawRect(img, bg);
        if (window)
        {
            // 별 · 성운 (사진마다 다른 하늘)
            var neb = Color.FromHsv(H(p.Id, 3), 0.6f, 0.8f, 0.45f);
            DrawBlob(ci, img.Position + img.Size * new Vector2(0.35f + 0.3f * H(p.Id, 1), 0.45f), img.Size.Y * 0.32f, neb, p.Id);
            DrawBlob(ci, img.Position + img.Size * new Vector2(0.6f, 0.55f + 0.2f * H(p.Id, 2)), img.Size.Y * 0.2f, neb.Lightened(0.3f).WithAlpha(0.35f), p.Id + 5);
            for (int i = 0; i < 12; i++) ci.DrawRect(new Rect2(img.Position + img.Size * new Vector2(H(p.Id, i + 10), H(p.Id, i + 30)), new Vector2(0.8f, 0.8f)), new Color(1f, 1f, 0.95f, 0.5f + 0.5f * H(i, p.Id)));
        }
        else
        {
            // 바닥 · 식탁 선
            float floorY = img.Position.Y + img.Size.Y * 0.72f;
            ci.DrawRect(new Rect2(img.Position.X, floorY, img.Size.X, img.End.Y - floorY), bg.Darkened(0.25f));
            int n = Math.Max(1, p.People.Length);
            for (int i = 0; i < p.People.Length; i++)
            {
                int id = p.People[i];
                var pc = Palette.Crew(id);
                float x = img.Position.X + img.Size.X * (i + 0.5f) / n;
                float y = floorY - img.Size.Y * (0.08f + 0.06f * ((i + p.Id) % 2));
                float hr = Math.Max(1.1f, img.Size.Y * 0.11f);
                ci.DrawArc(new Vector2(x, y + hr * 2.1f), hr * 1.5f, Mathf.Pi, Mathf.Tau, 8, pc, hr * 1.2f);
                ci.DrawCircle(new Vector2(x, y), hr, new Color(0.93f, 0.8f, 0.68f));
                ci.DrawArc(new Vector2(x, y - hr * 0.1f), hr, Mathf.Pi * 1.05f, Mathf.Tau * 0.97f, 6, Hair(id), hr * 0.7f);
                if (p.Scene == PhotoScene.Work) ci.DrawArc(new Vector2(x, y - hr * 0.3f), hr * 1.05f, Mathf.Pi, Mathf.Tau, 6, new Color(0.98f, 0.8f, 0.2f), hr * 0.6f); // 안전모
                if (w.Crew.FirstOrDefault(c => c.Id == id) is { Dead: true } && detail) ci.DrawLine(new Vector2(x - hr, y - hr * 1.6f), new Vector2(x + hr, y - hr * 1.6f), new Color(0f, 0f, 0f, 0.6f), 0.6f);
            }
            switch (p.Scene)
            {
                case PhotoScene.Birthday:
                {
                    var cake = new Rect2(img.GetCenter().X - img.Size.X * 0.14f, floorY - img.Size.Y * 0.08f, img.Size.X * 0.28f, img.Size.Y * 0.16f);
                    ci.DrawRect(cake, new Color(0.98f, 0.94f, 0.88f));
                    ci.DrawRect(new Rect2(cake.Position.X, cake.Position.Y + cake.Size.Y * 0.45f, cake.Size.X, cake.Size.Y * 0.18f), new Color(0.9f, 0.4f, 0.5f));
                    for (int i = 0; i < 3; i++)
                    {
                        float cx = cake.Position.X + cake.Size.X * (0.25f + 0.25f * i);
                        ci.DrawLine(new Vector2(cx, cake.Position.Y), new Vector2(cx, cake.Position.Y - 2f), new Color(0.5f, 0.75f, 0.95f), 0.6f);
                        ci.DrawCircle(new Vector2(cx, cake.Position.Y - 2.6f), 0.7f + 0.2f * Mathf.Sin(time * 9f + i), new Color(1f, 0.85f, 0.3f));
                    }
                    for (int i = 0; i < 5; i++) ci.DrawRect(new Rect2(img.Position + img.Size * new Vector2(H(p.Id, i), 0.08f + 0.2f * H(i, p.Id)), new Vector2(1f, 1f)), Color.FromHsv(H(i, 7), 0.7f, 1f)); // 종이 꽃가루
                    break;
                }
                case PhotoScene.Meal:
                    ci.DrawLine(new Vector2(img.Position.X, floorY - 1f), new Vector2(img.End.X, floorY - 1f), new Color(0.45f, 0.3f, 0.18f), 1.4f);
                    break;
                case PhotoScene.Work:
                    ci.DrawCircle(img.Position + img.Size * new Vector2(0.85f, 0.25f), 1.4f + 0.6f * Mathf.Sin(time * 5f), new Color(1f, 0.9f, 0.5f, 0.8f)); // 용접 불꽃
                    break;
                default:
                    ci.DrawRect(new Rect2(img.Position.X + img.Size.X * 0.1f, img.Position.Y + img.Size.Y * 0.12f, img.Size.X * 0.8f, 0.8f), bg.Lightened(0.3f)); // 벽의 띠
                    break;
            }
        }
        // 가장자리를 조금 어둡게 · 반사
        ci.DrawRect(new Rect2(img.Position, new Vector2(img.Size.X, 1f)), new Color(0f, 0f, 0f, 0.18f));
        ci.DrawLine(img.Position + new Vector2(img.Size.X * 0.6f, 0f), img.Position + new Vector2(img.Size.X, img.Size.Y * 0.4f), new Color(1f, 1f, 1f, 0.12f), 1.4f);
        // 떠난 사람이 있는 사진: 액자 귀퉁이에 검은 리본
        if (p.People.Any(id => w.Crew.FirstOrDefault(c => c.Id == id) is { Dead: true }))
            ci.DrawColoredPolygon(new[] { r.Position + new Vector2(-1.2f, -1.2f), r.Position + new Vector2(4f, -1.2f), r.Position + new Vector2(-1.2f, 4f) }, new Color(0.05f, 0.05f, 0.05f));
    }

    private static Color Hair(int id) => (id * 7 % 5) switch { 0 => new Color(0.1f, 0.08f, 0.06f), 1 => new Color(0.35f, 0.22f, 0.12f), 2 => new Color(0.75f, 0.6f, 0.35f), 3 => new Color(0.55f, 0.55f, 0.58f), _ => new Color(0.6f, 0.25f, 0.15f) };

    // ───────────────────────────── 만든 것 ─────────────────────────────

    /// <summary>모형 · 선물: 이름에 따라 실루엣이 다르고 (탐사선 · 화물선 · 등대 · 정거장 · 범선 · 기관실 · 장갑 · 나무 새 · 악보 · 책갈피 · 오르골), 진척만큼만 형태가 있다.</summary>
    public static void Made(CanvasItem ci, Vector2 c, string name, int seed, float progress, Color tint, float time)
    {
        float s = 6f;
        progress = Math.Clamp(progress, 0f, 1f);
        var solid = tint.Lightened(0.2f);
        var ghost = new Color(1f, 1f, 1f, 0.3f);
        var ink = new Color(0.08f, 0.09f, 0.12f, 0.9f);
        void Part(int idx, int total, Action<Color> draw) { draw(idx < Mathf.CeilToInt(progress * total - 0.001f) || progress >= 1f ? solid : ghost); }
        if (name.Contains("탐사선"))
        {
            Part(0, 4, col => ci.DrawColoredPolygon(new[] { c + new Vector2(0, -s), c + new Vector2(s * 0.35f, -s * 0.3f), c + new Vector2(s * 0.35f, s * 0.6f), c + new Vector2(-s * 0.35f, s * 0.6f), c + new Vector2(-s * 0.35f, -s * 0.3f) }, col));
            Part(1, 4, col => ci.DrawColoredPolygon(new[] { c + new Vector2(-s * 0.35f, s * 0.2f), c + new Vector2(-s * 0.75f, s * 0.75f), c + new Vector2(-s * 0.35f, s * 0.6f) }, col));
            Part(2, 4, col => ci.DrawColoredPolygon(new[] { c + new Vector2(s * 0.35f, s * 0.2f), c + new Vector2(s * 0.75f, s * 0.75f), c + new Vector2(s * 0.35f, s * 0.6f) }, col));
            Part(3, 4, col => ci.DrawCircle(c + new Vector2(0, -s * 0.2f), s * 0.16f, col == ghost ? ghost : new Color(0.6f, 0.85f, 1f)));
        }
        else if (name.Contains("화물선"))
        {
            Part(0, 4, col => ci.DrawRect(new Rect2(c.X - s, c.Y - s * 0.2f, s * 2f, s * 0.55f), col));
            for (int i = 0; i < 3; i++) { int ii = i; Part(1 + i, 4, col => ci.DrawRect(new Rect2(c.X - s * 0.8f + ii * s * 0.55f, c.Y - s * 0.6f, s * 0.45f, s * 0.38f), col == ghost ? ghost : Color.FromHsv(H(seed, ii), 0.55f, 0.85f))); }
        }
        else if (name.Contains("등대"))
        {
            Part(0, 3, col => ci.DrawColoredPolygon(new[] { c + new Vector2(-s * 0.3f, s * 0.8f), c + new Vector2(s * 0.3f, s * 0.8f), c + new Vector2(s * 0.18f, -s * 0.5f), c + new Vector2(-s * 0.18f, -s * 0.5f) }, col == ghost ? ghost : new Color(0.95f, 0.95f, 0.92f)));
            Part(1, 3, col => { for (int i = 0; i < 2; i++) ci.DrawRect(new Rect2(c.X - s * 0.27f + i * s * 0.03f, c.Y + s * (0.05f + 0.35f * i), s * 0.54f - i * s * 0.06f, s * 0.14f), col == ghost ? ghost : new Color(0.85f, 0.2f, 0.2f)); });
            Part(2, 3, col => ci.DrawCircle(c + new Vector2(0, -s * 0.65f), s * 0.18f, col == ghost ? ghost : new Color(1f, 0.9f, 0.4f, 0.7f + 0.3f * Mathf.Sin(time * 2f + seed))));
        }
        else if (name.Contains("정거장"))
        {
            Part(0, 3, col => ci.DrawArc(c, s * 0.8f, 0f, Mathf.Tau, 18, col, 1.6f, true));
            Part(1, 3, col => { for (int i = 0; i < 3; i++) { float a = i * Mathf.Tau / 3f; ci.DrawLine(c, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s * 0.8f, col, 1f); } });
            Part(2, 3, col => ci.DrawCircle(c, s * 0.25f, col));
        }
        else if (name.Contains("범선"))
        {
            Part(0, 3, col => ci.DrawColoredPolygon(new[] { c + new Vector2(-s, s * 0.2f), c + new Vector2(s, s * 0.2f), c + new Vector2(s * 0.6f, s * 0.6f), c + new Vector2(-s * 0.6f, s * 0.6f) }, col == ghost ? ghost : new Color(0.55f, 0.35f, 0.2f)));
            Part(1, 3, col => ci.DrawLine(c + new Vector2(0, s * 0.2f), c + new Vector2(0, -s), ink, 1f));
            Part(2, 3, col => ci.DrawColoredPolygon(new[] { c + new Vector2(0.5f, -s * 0.9f), c + new Vector2(s * 0.75f, s * 0.05f), c + new Vector2(0.5f, s * 0.05f) }, col == ghost ? ghost : new Color(0.96f, 0.94f, 0.88f)));
        }
        else if (name.Contains("장갑"))
        {
            for (int g = 0; g < 2; g++)
            {
                int gg = g;
                Part(g, 2, col =>
                {
                    var o = c + new Vector2((gg - 0.5f) * s * 0.9f, 0f);
                    ci.DrawRect(new Rect2(o.X - s * 0.3f, o.Y - s * 0.2f, s * 0.6f, s * 0.7f), col);
                    ci.DrawCircle(o + new Vector2(gg == 0 ? -s * 0.35f : s * 0.35f, 0f), s * 0.15f, col);
                    for (int i = 0; i < 3; i++) ci.DrawLine(new Vector2(o.X - s * 0.25f, o.Y + s * 0.1f * i), new Vector2(o.X + s * 0.25f, o.Y + s * 0.1f * i), col.Darkened(0.25f), 0.6f); // 뜨개 결
                });
            }
        }
        else if (name.Contains("나무 새"))
        {
            Part(0, 2, col => DrawBlob(ci, c, s * 0.6f, col == ghost ? ghost : new Color(0.7f, 0.5f, 0.3f), seed));
            Part(1, 2, col => { ci.DrawColoredPolygon(new[] { c + new Vector2(s * 0.5f, -s * 0.2f), c + new Vector2(s * 0.9f, -s * 0.1f), c + new Vector2(s * 0.5f, 0f) }, col == ghost ? ghost : new Color(0.55f, 0.38f, 0.2f)); ci.DrawCircle(c + new Vector2(s * 0.3f, -s * 0.25f), 0.7f, ink); });
        }
        else if (name.Contains("악보") || name.Contains("책갈피"))
        {
            Part(0, 2, col => ci.DrawRect(new Rect2(c.X - s * 0.45f, c.Y - s * 0.7f, s * 0.9f, s * 1.4f), col == ghost ? ghost : new Color(0.96f, 0.93f, 0.85f)));
            Part(1, 2, col =>
            {
                if (name.Contains("악보")) for (int i = 0; i < 4; i++) ci.DrawLine(new Vector2(c.X - s * 0.35f, c.Y - s * 0.4f + i * s * 0.25f), new Vector2(c.X + s * 0.35f, c.Y - s * 0.4f + i * s * 0.25f), ink.WithAlpha(0.6f), 0.5f);
                else ci.DrawCircle(c + new Vector2(0, -s * 0.2f), s * 0.25f, col == ghost ? ghost : new Color(0.85f, 0.35f, 0.5f));
            });
        }
        else if (name.Contains("오르골"))
        {
            Part(0, 2, col => ci.DrawRect(new Rect2(c.X - s * 0.6f, c.Y - s * 0.3f, s * 1.2f, s * 0.8f), col == ghost ? ghost : new Color(0.6f, 0.4f, 0.25f)));
            Part(1, 2, col => { ci.DrawLine(c + new Vector2(s * 0.6f, 0f), c + new Vector2(s * 0.9f, -s * 0.2f), new Color(0.8f, 0.75f, 0.5f), 1.2f); if (col != ghost) ci.DrawCircle(c + new Vector2(0, -s * 0.5f - 2f * Mathf.Abs(Mathf.Sin(time * 2f))), 0.9f, new Color(1f, 0.95f, 0.7f, 0.7f)); });
        }
        else // 기관실 축소 모형
        {
            Part(0, 3, col => ci.DrawRect(new Rect2(c.X - s * 0.8f, c.Y - s * 0.4f, s * 1.6f, s * 0.9f), col));
            Part(1, 3, col => { ci.DrawLine(c + new Vector2(-s * 0.6f, -s * 0.4f), c + new Vector2(-s * 0.6f, -s * 0.8f), new Color(0.75f, 0.75f, 0.8f), 1.2f); ci.DrawLine(c + new Vector2(-s * 0.6f, -s * 0.8f), c + new Vector2(s * 0.3f, -s * 0.8f), new Color(0.75f, 0.75f, 0.8f), 1.2f); });
            Part(2, 3, col => ci.DrawCircle(c + new Vector2(s * 0.3f, 0f), s * 0.25f, col == ghost ? ghost : new Color(0.4f, 0.9f, 0.6f, 0.6f + 0.4f * Mathf.Sin(time * 3f))));
        }
        if (progress < 1f) ci.DrawArc(c, s * 1.15f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * progress, 16, tint.WithAlpha(0.35f), 0.8f, true);
    }

    // ───────────────────────────── 독서등 ─────────────────────────────

    /// <summary>머리맡 독서등: 고장 — 테이프 감은 줄 · 불규칙하게 깜빡 · 고친 뒤 — 따뜻한 원뿔 빛.</summary>
    public static void Lamp(CanvasItem ci, Vector2 p, bool fixedLamp, bool on, float time, int seed, float progress)
    {
        var metal = new Color(0.62f, 0.64f, 0.68f);
        ci.DrawCircle(p, 2.6f, metal.Darkened(0.3f));
        var arm = new[] { p, p + new Vector2(2f, -5f), p + new Vector2(6f, -6f) };
        ci.DrawPolyline(arm, metal, 1.1f, true);
        var head = p + new Vector2(6f, -6f);
        ci.DrawColoredPolygon(new[] { head + new Vector2(-2f, -1.5f), head + new Vector2(2.5f, -1f), head + new Vector2(3.5f, 2.5f), head + new Vector2(-1f, 2.5f) }, fixedLamp ? new Color(0.25f, 0.45f, 0.35f) : new Color(0.45f, 0.42f, 0.4f));
        if (!fixedLamp)
        {
            // 테이프 감은 자리 (고치는 만큼 테이프가 늘어난다)
            int tapes = 1 + (int)(progress * 3f);
            for (int i = 0; i < tapes; i++) ci.DrawLine(p + new Vector2(0.6f + i * 0.8f, -1.5f - i * 1.3f), p + new Vector2(2.2f + i * 0.8f, -1.1f - i * 1.3f), new Color(0.95f, 0.95f, 0.85f), 1.2f);
            float flick = Mathf.Sin(time * 17f + seed) * Mathf.Sin(time * 5.3f + seed * 1.7f);
            if (on && flick > 0.35f) ci.DrawCircle(head + new Vector2(0.8f, 2.4f), 1.1f, new Color(1f, 0.9f, 0.6f, 0.8f));
            return;
        }
        if (!on) return;
        ci.DrawColoredPolygon(new[] { head + new Vector2(-1f, 2.5f), head + new Vector2(3.5f, 2.5f), head + new Vector2(9f, 14f), head + new Vector2(-6f, 14f) }, new Color(1f, 0.85f, 0.55f, 0.13f));
        ci.DrawCircle(head + new Vector2(1.2f, 2.6f), 1.3f, new Color(1f, 0.92f, 0.7f, 0.95f));
    }

    // ───────────────────────────── 개수대 · 당번표 · 단말 ─────────────────────────────

    /// <summary>개수대 그릇 더미: 쌓인 만큼 접시 탑이 높아지고, 씻는 중이면 거품이 오른다.</summary>
    public static void DishPile(CanvasItem ci, Vector2 p, int n, bool washing, float time)
    {
        int stacks = n > 8 ? 3 : n > 3 ? 2 : 1;
        int k = 0;
        for (int s = 0; s < stacks; s++)
        {
            var b = p + new Vector2(s * 6.5f - (stacks - 1) * 3.2f, 0f);
            int here = (n + stacks - 1 - s) / stacks;
            for (int i = 0; i < here; i++, k++)
            {
                var c = b + new Vector2((k % 3 - 1) * 0.6f, -i * 1.5f);
                ci.DrawRect(new Rect2(c.X - 2.8f, c.Y - 0.7f, 5.6f, 1.4f), k % 4 == 3 ? new Color(0.75f, 0.85f, 0.95f) : new Color(0.95f, 0.95f, 0.92f));
                ci.DrawLine(new Vector2(c.X - 2.8f, c.Y + 0.7f), new Vector2(c.X + 2.8f, c.Y + 0.7f), new Color(0.4f, 0.5f, 0.6f, 0.6f), 0.5f);
            }
        }
        if (n >= 6) ci.DrawArc(p + new Vector2(5f, -n * 0.5f - 2f), 1.4f, 0f, Mathf.Tau, 8, new Color(0.7f, 0.5f, 0.3f), 1f); // 꼭대기에 얹힌 컵
        if (!washing) return;
        for (int i = 0; i < 6; i++)
        {
            float t = (time * 0.7f + i * 0.17f) % 1f;
            ci.DrawArc(p + new Vector2(-4f + i * 1.6f + Mathf.Sin(t * 8f + i) * 1.2f, -2f - t * 9f), 0.8f + t * 0.8f, 0f, Mathf.Tau, 8, new Color(1f, 1f, 1f, 0.6f * (1f - t)), 0.6f, true);
        }
    }

    /// <summary>설거지 당번표: 벽에 붙은 종이 한 장 — 사람마다 그 사람 색 칸, 오늘 줄은 밝게, 핀 하나.</summary>
    public static void Roster(CanvasItem ci, Vector2 c, List<int> ids, int today, bool detail)
    {
        int rows = Math.Min(7, ids.Count);
        var r = new Rect2(c.X - 6f, c.Y - 2f - rows * 1.6f, 12f, 4f + rows * 3.2f);
        ci.DrawRect(r.Grow(0.6f), new Color(0f, 0f, 0f, 0.25f));
        ci.DrawRect(r, new Color(0.97f, 0.95f, 0.86f));
        ci.DrawCircle(new Vector2(r.GetCenter().X, r.Position.Y + 0.8f), 1f, new Color(0.85f, 0.2f, 0.2f));
        for (int i = 0; i < rows; i++)
        {
            float y = r.Position.Y + 2.5f + i * 3.2f;
            if (ids[i] == today) ci.DrawRect(new Rect2(r.Position.X + 0.5f, y - 0.6f, r.Size.X - 1f, 2.6f), new Color(1f, 0.9f, 0.4f, 0.6f));
            ci.DrawRect(new Rect2(r.Position.X + 1.2f, y, 2f, 1.4f), Palette.Crew(ids[i]));
            if (detail) ci.DrawLine(new Vector2(r.Position.X + 4f, y + 0.7f), new Vector2(r.End.X - 1.5f, y + 0.7f), new Color(0.3f, 0.3f, 0.35f, 0.6f), 0.5f);
        }
    }

    /// <summary>손목 단말 화면: 작은 판 · 푸른 빛이 얼굴 쪽으로.</summary>
    public static void Tablet(CanvasItem ci, Vector2 p, Color band, float time)
    {
        ci.DrawCircle(p, 6f, new Color(0.5f, 0.8f, 1f, 0.07f));
        ci.DrawRect(new Rect2(p.X - 2.2f, p.Y - 1.6f, 4.4f, 3.2f), new Color(0.08f, 0.1f, 0.14f));
        ci.DrawRect(new Rect2(p.X - 1.7f, p.Y - 1.1f, 3.4f, 2.2f), new Color(0.55f, 0.85f, 1f, 0.75f + 0.15f * Mathf.Sin(time * 3f)));
        ci.DrawLine(new Vector2(p.X - 1.2f, p.Y - 0.3f), new Vector2(p.X + 0.8f, p.Y - 0.3f), new Color(1f, 1f, 1f, 0.7f), 0.4f);
        ci.DrawLine(new Vector2(p.X - 1.2f, p.Y + 0.4f), new Vector2(p.X + 0.2f, p.Y + 0.4f), new Color(1f, 1f, 1f, 0.5f), 0.4f);
        ci.DrawLine(new Vector2(p.X - 2.2f, p.Y + 1.6f), new Vector2(p.X + 2.2f, p.Y + 1.6f), band, 0.8f);
    }
}
