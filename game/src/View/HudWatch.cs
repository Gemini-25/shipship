using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v17.7 항해 결산 카드 · 물건이 거친 손 카드 · 연대기 사진 자리 (어두운 유리 톤 — UiKitCards)
public partial class Hud
{
    public bool VoyageOpen { get; set; }
    private VoyageReview? _voyage;
    private long _voyageTick = -1;
    private float _voyageH = 420f; // 지난 프레임에 그린 내용 높이 (카드를 내용에 맞춘다)

    public void ToggleVoyage()
    {
        VoyageOpen = !VoyageOpen;
        if (VoyageOpen) { ChronicleOpen = false; TechOpen = false; PolicyOpen = false; OpenChain(null); _voyageTick = -1; }
    }

    private static string WatchIcon(WatchKind k) => k switch
    {
        WatchKind.Gift => "star", WatchKind.LateGuest => "seat", WatchKind.Reconcile => "relation", WatchKind.Artwork => "photo",
        WatchKind.Confession => "social", _ => "memory",
    };

    /// <summary>⇧J 항해 결산: 큰 사고 · 사람 · 좋은 순간 · 물건이 거친 손을 한 장으로.</summary>
    private void DrawVoyage(Vector2 mouse)
    {
        if (_voyage == null || _world.Tick - _voyageTick > SimTime.Minutes(10)) { _voyage = VoyageReview.Build(_world); _voyageTick = _world.Tick; } // 10분마다만 다시 모은다
        var v = _voyage;
        float x0 = Margin, y0 = Margin + 52f + 8f + 40f + 8f + 64f + 10f;
        float wdt = Mathf.Min(640f, Screen.X - RightColumnWidth - Margin * 3);
        float maxH = Screen.Y - y0 - LogFullHeight - Margin - 10f;
        float height = Mathf.Min(maxH, _voyageH);
        var card = new Rect2(x0, y0, wdt, height);
        Card(card);
        float x = x0 + Ui.Pad, right = card.End.X - Ui.Pad, y = y0;
        UiKit.CardTitle(this, x, right - 70f, y + 32f, $"{v.Ship} 항해 결산", $"{v.Days}일째 · {v.Alive}/{v.Crew}명", "route");
        Button(new Rect2(right - 64, y0 + 12, 64, 26), "⇧J 닫기", false, mouse, ToggleVoyage, Ui.TextSmall);
        y += 48f;
        UiKit.Stats(this, new Vector2(x, y + 14f), right, new (string, int, string, Tone)[]
        {
            ("clock", v.Days, "일", Tone.Normal), ("incident", v.Incidents, "사고", v.Incidents > 0 ? Tone.Caution : Tone.Normal),
            ("relation", v.Moments, "좋은 순간", Tone.Good), ("crew", v.Alive, "살아 있다", v.Alive < v.Crew ? Tone.Danger : Tone.Good),
        });
        UiKit.ScaleDots(this, new Vector2(right - 90f, y + 14f), v.ByScale);
        y += 36f;
        float colW = (right - x - Ui.S3) * 0.5f, xr = x + colW + Ui.S3;
        float yl = y, yr = y;

        // 왼쪽: 큰 사고 · 사람
        UiKit.Header(this, x, x + colW, yl + 14f, "큰 사고", null, "incident");
        yl += 24f;
        if (v.Big.Count == 0) { Gfx.Text(this, Fonts.Body, new Vector2(x + 4f, yl + 12f), "큰 사고 없이 왔다", Ui.TextSmall, Palette.TextMuted); yl += 20f; }
        foreach (var (tick, text, scale, open) in v.Big)
        {
            var col = UiKit.ScaleColor(scale);
            DrawCircle(new Vector2(x + 6f, yl + 8f), 4f, col);
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 16f, yl + 12f), $"{SimTime.Day(tick)}일", Ui.TextSmall, Palette.TextMuted);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 50f, yl + 12f), UiKit.Fit(text + (open ? " (아직)" : ""), colW - 50f, Ui.TextSmall), Ui.TextSmall, Palette.Text);
            yl += 20f;
        }
        yl += 8f;
        UiKit.Header(this, x, x + colW, yl + 14f, "사람", null, "people");
        yl += 24f;
        foreach (var (id, name, why, gone) in v.People)
        {
            var col = gone ? Palette.TextMuted : Palette.Crew(id);
            Icons.Draw(this, gone ? "dead" : "crew", new Vector2(x + 7f, yl + 8f), 12f, col);
            Gfx.Text(this, Fonts.Bold, new Vector2(x + 18f, yl + 12f), name, Ui.TextSmall, col);
            float nw = Gfx.Width(Fonts.Bold, name, Ui.TextSmall) + 24f;
            Gfx.Text(this, Fonts.Body, new Vector2(x + nw, yl + 12f), UiKit.Fit(why, colW - nw, Ui.TextSmall), Ui.TextSmall, Palette.TextDim);
            yl += 20f;
        }

        // 사진 모드로 찍은 사진 (최근 둘 — 인화지처럼)
        if (PhotoAlbum.All.Count > 0)
        {
            float tw = (colW - Ui.S2) * 0.5f, th = tw * 0.6f;
            if (yl + 32f + th + 14f < y0 + maxH)
            {
                yl += 8f;
                UiKit.Header(this, x, x + colW, yl + 14f, "사진", $"{PhotoAlbum.All.Count}장", "photo");
                yl += 24f;
                var shots = PhotoAlbum.All.TakeLast(2).ToList();
                for (int i = 0; i < shots.Count; i++) DrawPhotoPrint(new Rect2(x + i * (tw + Ui.S2), yl, tw, th + 14f), shots[i]);
                yl += th + 14f;
            }
        }

        // 오른쪽: 좋은 순간 · 물건이 거친 손
        UiKit.Header(this, xr, right, yr + 14f, "좋은 순간", null, "relation");
        yr += 24f;
        if (v.Peace.Count == 0) { Gfx.Text(this, Fonts.Body, new Vector2(xr + 4f, yr + 12f), "아직 적힌 것이 없다", Ui.TextSmall, Palette.TextMuted); yr += 20f; }
        foreach (var (tick, kind, text) in v.Peace)
        {
            Icons.Draw(this, WatchIcon(kind), new Vector2(xr + 7f, yr + 8f), 12f, Palette.Good);
            var lines = UiKit.Wrap(text, right - xr - 18f, Ui.TextSmall, Fonts.Body, 2);
            for (int i = 0; i < lines.Count; i++) Gfx.Text(this, Fonts.Body, new Vector2(xr + 18f, yr + 12f + i * 16f), lines[i], Ui.TextSmall, Palette.Text);
            yr += 6f + lines.Count * 16f;
        }
        yr += 8f;
        UiKit.Header(this, xr, right, yr + 14f, "물건이 거친 손", "Q 따라가기", "bag");
        yr += 24f;
        foreach (var (id, name, hands, now, marks) in v.Items)
        {
            if (yr > y0 + maxH - 60f) break;
            Gfx.Text(this, Fonts.Bold, new Vector2(xr + 4f, yr + 12f), UiKit.Fit(name, right - xr - 60f, Ui.TextSmall), Ui.TextSmall, Palette.Text);
            if (marks > 0) Gfx.TextRight(this, Fonts.Body, new Vector2(right, yr + 12f), $"자국 {marks}", Ui.TextTiny, Palette.TextMuted);
            Gfx.Text(this, Fonts.Body, new Vector2(xr + 4f, yr + 28f), UiKit.Fit(string.Join(" → ", hands), right - xr - 8f, Ui.TextTiny), Ui.TextTiny, Palette.TextDim);
            Gfx.Text(this, Fonts.Body, new Vector2(xr + 4f, yr + 42f), UiKit.Fit(now, right - xr - 8f, Ui.TextTiny), Ui.TextTiny, Palette.TextMuted);
            yr += 50f;
        }

        _voyageH = Mathf.Max(yl, yr) + 4f - y0 + Ui.Pad;
    }

    /// <summary>사진 한 장을 흰 테두리 인화지처럼 (가운데를 잘라 틀에 맞추고 · 아래에 날과 곳).</summary>
    private void DrawPhotoPrint(Rect2 r, PhotoAlbum.Photo p)
    {
        Gfx.RoundRect(this, r, new Color(0.9f, 0.9f, 0.86f, 0.92f), 3f);
        var inner = new Rect2(r.Position + new Vector2(4f, 4f), r.Size - new Vector2(8f, 18f));
        if (inner.Size.X < 4f || inner.Size.Y < 4f) return;
        var ts = p.Texture.GetSize();
        float k = Mathf.Max(inner.Size.X / ts.X, inner.Size.Y / ts.Y);
        DrawTextureRectRegion(p.Texture, inner, new Rect2((ts - inner.Size / k) * 0.5f, inner.Size / k));
        Gfx.Text(this, Fonts.Body, new Vector2(r.Position.X + 5f, r.End.Y - 4f), UiKit.Fit(p.Caption, r.Size.X - 10f, Ui.TextMicro), Ui.TextMicro, new Color(0.2f, 0.2f, 0.22f));
    }

    /// <summary>Q 로 물건을 따라가는 동안: 그 물건이 거친 손 · 남은 자국 · 지금 어디 (화면 위 가운데).</summary>
    private void DrawItemTrail(Vector2 mouse)
    {
        if (_main.FollowItem < 0 || _world.Belongings.All.FirstOrDefault(b => b.Id == _main.FollowItem) is not Belonging b) return;
        var steps = WatchScenes.Trail(_world, b);
        var hands = WatchScenes.Hands(b);
        float wdt = 360f, h = 74f + Mathf.Min(5, steps.Count) * 20f;
        // 오른쪽 아래 (지도 옆 · 대화 카드 · 알림과 겹치지 않게), 자리가 모자라면 지도 위로
        float cx = Screen.X - RightColumnWidth - Margin * 2 - wdt, cy = Screen.Y - Margin - h;
        if (MinimapOpen && _minimapRect.Size.X > 0f && _minimapRect.End.X + Ui.S2 > cx) cy = _minimapRect.Position.Y - h - Ui.S2;
        var card = new Rect2(cx, cy, wdt, h);
        Card(card);
        float x = card.Position.X + Ui.Pad, right = card.End.X - Ui.Pad, y = card.Position.Y;
        UiKit.CardTitle(this, x, right - 30f, y + 28f, b.Name, _world.Belongings.Where(b), "bag");
        Button(new Rect2(right - 24f, y + 10f, 24f, 22f), "×", false, mouse, () => _main.StopFollowItem(), Ui.TextSmall);
        // 거친 손: 이름 동그라미를 화살표로 잇는다
        float hx = x, hy = y + 50f;
        for (int i = 0; i < hands.Count; i++)
        {
            var who = _world.Crew.FirstOrDefault(c => c.Id == hands[i]);
            string name = who?.Name ?? "?";
            var col = who == null ? Palette.TextMuted : who.Dead ? Palette.TextMuted : Palette.Crew(who.Id);
            float nw = Gfx.Width(Fonts.Bold, name, Ui.TextSmall) + 14f;
            if (hx + nw > right) break;
            Gfx.RoundRect(this, new Rect2(hx, hy - 9f, nw, 18f), col.WithAlpha(0.14f), 9f, col.WithAlpha(0.5f));
            Gfx.Text(this, Fonts.Bold, new Vector2(hx + 7f, hy + 4f), name, Ui.TextSmall, col);
            hx += nw;
            if (i < hands.Count - 1) { Gfx.Text(this, Fonts.Body, new Vector2(hx + 3f, hy + 4f), "→", Ui.TextSmall, Palette.TextMuted); hx += 16f; }
        }
        var lines = steps.Where(s => s.Tick >= 0).Select(s => (s.Tick, s.Text)).ToList();
        UiKit.Steps(this, x, right, hy + 16f, lines.Count > 0 ? lines : steps.Select(s => (s.Tick, s.Text)).ToList(), Tone.Info, 5);
    }

    /// <summary>연대기 그날의 사진 자리: 사진 모드로 찍은 사진이 있으면 그것을 건다. 걸었으면 true.</summary>
    private bool DrawAlbumPhoto(Rect2 r, int firstDay, int lastDay)
    {
        if (PhotoAlbum.For(firstDay, lastDay) is not PhotoAlbum.Photo p) return false;
        DrawPhotoPrint(r, p); // 가운데를 잘라 틀에 맞춘다 (늘이지 않는다)
        return true;
    }
}
