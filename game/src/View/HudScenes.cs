using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v23 오늘의 장면: 저녁 22시에 그날 장면 1~3장을 배 위 가운데에 크게 (10초 · 누르면 접는다) · 기록 탭 '사진첩' 쪽.
public partial class Hud
{
    private long _sceneSeenTick = -1;
    private float _sceneAt = -1000f;
    private int _albumPage;
    private const float SceneOpenSec = 10f;
    // v23 하이라이트 다시보기: 점수 높은 장면을 날짜 순으로 한 장씩 (장면마다 그 방으로 화면이 간다)
    private List<int> _reel = new();
    private int _reelAt = -1;
    private float _reelT;
    private const float ReelSec = 4.5f;

    private void StartReel()
    {
        var book = _world.Album;
        _reel = book.Shots.OrderByDescending(s => s.Score).ThenBy(s => s.Id).Take(10).OrderBy(s => s.Tick).Select(s => s.Id).ToList();
        _reelAt = _reel.Count > 0 ? 0 : -1;
        _reelT = _time;
        ReelFocus();
    }

    private void ReelFocus()
    {
        if (_reelAt < 0 || _reelAt >= _reel.Count) return;
        if (_world.Album.Get(_reel[_reelAt]) is SceneShot s && s.RoomId >= 0 && _world.Ship.Rooms.FirstOrDefault(r => r.Id == s.RoomId) is Room room && !room.Detached)
            _main.FocusRoom(room);
    }

    private void DrawReel()
    {
        if (_reelAt < 0) return;
        if (_time - _reelT > ReelSec) { _reelAt++; _reelT = _time; if (_reelAt >= _reel.Count) { _reelAt = -1; return; } ReelFocus(); }
        if (_world.Album.Get(_reel[_reelAt]) is not SceneShot s) { _reelAt = -1; return; }
        float t = _time - _reelT, a = Mathf.Clamp(t / 0.3f, 0f, 1f) * Mathf.Clamp((ReelSec - t) / 0.3f, 0f, 1f);
        float right = _plan.Side.Empty ? ColX + RightColumnWidth : ColX - UiLayout.Gap;
        float pw = MathF.Min(420f, right - Margin * 2f - 36f), ph = pw * SceneShooter.H / SceneShooter.W;
        float w = pw + 36f, h = 50f + ph + 62f;
        float cx = (Margin + right) * 0.5f, y0 = Margin + UiLayout.TopBarH + 50f;
        var card = new Rect2(cx - w * 0.5f, y0, w, h);
        Card(card);
        float x = card.Position.X + 18f, y = y0 + 26f;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), "다시보기", Ui.TextTitle, Palette.Text);
        Gfx.TextRight(this, Fonts.Body, new Vector2(card.End.X - 18f, y), $"{_reelAt + 1}/{_reel.Count} · 누르면 그만", Ui.TextTiny, Palette.TextMuted);
        y += 14f;
        DrawSceneFrame(new Rect2(x, y, pw, ph), s, a, 1.2f);
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y + ph + 20f), UiKit.Fit($"{s.Day}일 {SimTime.Clock(s.Tick)} · {s.Title}", pw, Ui.TextSmall, Fonts.Bold), Ui.TextSmall, Palette.Text.WithAlpha(a));
        Gfx.Text(this, Fonts.Body, new Vector2(x, y + ph + 40f), UiKit.Fit(s.Text, pw, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim.WithAlpha(a));
        // 진행 막대
        this.Box(new Rect2(x, card.End.Y - 8f, pw * Mathf.Clamp(t / ReelSec, 0f, 1f), 2f), SceneTint(s.Kind));
        _buttons.Add((card, () => _reelAt = -1));
    }

    private static Color SceneTint(HistoryKind k) => k switch
    {
        HistoryKind.Death => new Color("#b7a6d9"), HistoryKind.Casualty => new Color("#ff8fa0"), HistoryKind.Incident or HistoryKind.Damage => new Color("#ff7a4a"),
        HistoryKind.Response => new Color("#ffb35c"), HistoryKind.Bond => new Color("#ffc6d9"), HistoryKind.Milestone => new Color("#8fd0ff"),
        HistoryKind.Structure => new Color("#9fe0b0"), _ => new Color("#c9d3e0"),
    };

    private void DrawSceneDayCard(Vector2 mouse)
    {
        var book = _world.Album;
        if (book.LastPickTick < 0 || book.LastPick.Count == 0) return;
        if (book.LastPickTick != _sceneSeenTick)
        {
            _sceneSeenTick = book.LastPickTick;
            _sceneAt = _world.Tick - book.LastPickTick < SimTime.Hours(1) ? _time : -1000f; // 불러온 세계의 지난 장면은 띄우지 않는다
        }
        if (_time - _sumAt < SumOpenSec + 0.4f) _sceneAt = Mathf.Max(_sceneAt, _time - 0.01f); // 결산 카드가 떠 있으면 기다린다
        float t = _time - _sceneAt;
        if (t < 0f || t > SceneOpenSec + 0.4f) return;
        float a = Mathf.Clamp(t / 0.3f, 0f, 1f) * Mathf.Clamp((SceneOpenSec + 0.4f - t) / 0.4f, 0f, 1f);
        var shots = book.LastPick.Select(book.Get).Where(s => s != null).Cast<SceneShot>().ToList();
        if (shots.Count == 0) return;
        float right = _plan.Side.Empty ? ColX + RightColumnWidth : ColX - UiLayout.Gap;
        float avail = right - Margin * 2f;
        float pw = MathF.Min(230f, (avail - 36f - (shots.Count - 1) * 12f) / shots.Count), ph = pw * SceneShooter.H / SceneShooter.W;
        float w = shots.Count * pw + (shots.Count - 1) * 12f + 36f, h = 54f + ph + 52f;
        float cx = (Margin + right) * 0.5f, y0 = Margin + UiLayout.TopBarH + 66f - (1f - a) * 14f;
        var card = new Rect2(cx - w * 0.5f, y0, w, h);
        Card(card);
        float x = card.Position.X + 18f, y = y0 + 26f;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), "오늘의 장면", Ui.TextTitle, Palette.Text.WithAlpha(a));
        Gfx.TextRight(this, Fonts.Body, new Vector2(card.End.X - 18f, y), $"{shots[0].Day}일 · 사진첩에 남았다", Ui.TextTiny, Palette.TextMuted.WithAlpha(a));
        y += 16f;
        foreach (var s in shots)
        {
            DrawSceneFrame(new Rect2(x, y, pw, ph), s, a, 1f);
            Gfx.Text(this, Fonts.Bold, new Vector2(x, y + ph + 18f), UiKit.Fit($"{SimTime.Clock(s.Tick)} {s.Title}", pw, Ui.TextSmall, Fonts.Bold), Ui.TextSmall, Palette.Text.WithAlpha(a));
            string sub = s.Text.Contains(" · ") ? s.Text[(s.Text.IndexOf(" · ", StringComparison.Ordinal) + 3)..] : s.RoomId >= 0 ? RoomName(s) : "배 전체";
            Gfx.Text(this, Fonts.Body, new Vector2(x, y + ph + 36f), UiKit.Fit(sub, pw, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim.WithAlpha(a));
            x += pw + 12f;
        }
        _buttons.Add((card, () => _sceneAt = _time - SceneOpenSec)); // 누르면 접는다
    }

    private static string SceneKindName(HistoryKind k) => k switch
    {
        HistoryKind.Death => "추모", HistoryKind.Casualty => "부상", HistoryKind.Incident => "사고", HistoryKind.Damage => "손상", HistoryKind.Response => "대응",
        HistoryKind.Bond => "인연", HistoryKind.Milestone => "이정표", HistoryKind.Decision => "결정", HistoryKind.Structure => "선체", HistoryKind.Upgrade => "개량",
        HistoryKind.Adaptation => "적응", HistoryKind.Recovery => "수습", HistoryKind.Lesson => "교훈", _ => "기억",
    };

    private string RoomName(SceneShot s) => s.RoomId >= 0 && s.RoomId < _world.Ship.Rooms.Count ? _world.Ship.Rooms[s.RoomId].Name : "배";

    /// <summary>장면 한 장: 찍어 둔 화면 (없으면 그 방 이름과 색으로 빈 틀).</summary>
    private void DrawSceneFrame(Rect2 r, SceneShot s, float a, float k)
    {
        var tint = SceneTint(s.Kind);
        if (SceneShooter.Photo(_world, s.Id) is Texture2D tex)
        {
            DrawTextureRect(tex, r, false, new Color(1, 1, 1, a));
            this.Box(new Rect2(r.Position, new Vector2(r.Size.X, 3f * k)), tint.WithAlpha(0.9f * a));
        }
        else
        {
            Gfx.RoundRect(this, r, new Color(0.08f, 0.1f, 0.14f, 0.95f * a), 4f, tint.WithAlpha(0.5f * a));
            Gfx.TextCentered(this, Fonts.Bold, r.GetCenter() - new Vector2(0, 8f), SceneKindName(s.Kind), Ui.TextTitle, tint.WithAlpha(a));
            Gfx.TextCentered(this, Fonts.Body, r.GetCenter() + new Vector2(0, 14f), UiKit.Fit(s.RoomId >= 0 ? RoomName(s) : "배 전체", r.Size.X - 12f, Ui.TextSmall, Fonts.Body), Ui.TextSmall, Palette.TextDim.WithAlpha(a));
            Gfx.TextCentered(this, Fonts.Body, r.GetCenter() + new Vector2(0, 32f), "사진은 남지 않았다", Ui.TextMicro, Palette.TextMuted.WithAlpha(a));
        }
    }

    /// <summary>기록 탭 '사진첩': 날마다 고른 장면 — 새 날이 위.</summary>
    private void DrawAlbumPage(Vector2 mouse)
    {
        var book = _world.Album;
        var card = PageCard(_dockBody.Size.Y);
        float x = card.Position.X + 18f, y = card.Position.Y + 30f, right = card.End.X - 18f, bottom = card.End.Y - 12f, wd = right - x;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), "사진첩", Ui.TextTitle, Palette.Text);
        Gfx.TextRight(this, Fonts.Body, new Vector2(right, y), $"장면 {book.Shots.Count}장 · 오늘 후보 {book.Today.Count}", Ui.TextTiny, Palette.TextMuted);
        if (book.Shots.Count > 0)
        {
            var reel = new Rect2(x + 90f, y - 16f, 96f, 22f);
            Gfx.RoundRect(this, reel, reel.HasPoint(mouse) ? Palette.Accent.WithAlpha(0.25f) : new Color(1, 1, 1, 0.06f), 4f, Palette.Accent.WithAlpha(0.6f));
            Gfx.TextCentered(this, Fonts.Bold, reel.GetCenter() + new Vector2(0, 5f), _reelAt >= 0 ? "■ 그만" : "▶ 다시보기", Ui.TextTiny, Palette.Accent);
            _buttons.Add((reel, () => { if (_reelAt >= 0) _reelAt = -1; else StartReel(); }));
        }
        y += 14f;
        if (book.Shots.Count == 0)
        {
            Gfx.Text(this, Fonts.Body, new Vector2(x, y + 20f), "아직 남은 장면이 없다 — 저녁 22시에 그날 장면을 고른다.", Ui.TextSmall, Palette.TextDim);
            if (book.Today.Count > 0) Gfx.Text(this, Fonts.Body, new Vector2(x, y + 40f), UiKit.Fit("오늘 후보: " + string.Join(" · ", book.Today.OrderByDescending(s => s.Score).Take(3).Select(s => s.Title)), wd, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextMuted);
            return;
        }
        var days = book.Shots.GroupBy(s => s.Day).OrderByDescending(g => g.Key).ToList();
        float pw = MathF.Min(170f, (wd - 60f - 2 * 10f) / 3f), ph = pw * SceneShooter.H / SceneShooter.W, rowH = ph + 46f;
        int perPage = Math.Max(1, (int)((bottom - y - 30f) / rowH));
        int pages = (days.Count + perPage - 1) / perPage;
        _albumPage = Math.Clamp(_albumPage, 0, Math.Max(0, pages - 1));
        foreach (var g in days.Skip(_albumPage * perPage).Take(perPage))
        {
            y += 8f;
            Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 16f), $"{g.Key}일", Ui.TextSmall, Palette.TextDim);
            float px = x + 60f;
            foreach (var s in g.OrderBy(s => s.Tick))
            {
                var r = new Rect2(px, y, pw, ph);
                DrawSceneFrame(r, s, 1f, 0.8f);
                Gfx.Text(this, Fonts.Body, new Vector2(px, y + ph + 15f), UiKit.Fit($"{SimTime.Clock(s.Tick)} {s.Title}", pw, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.Text);
                if (r.HasPoint(mouse)) { var sh = s; var at = new Vector2(r.Position.X, r.End.Y + 4f); _tip = () => UiKit.Tooltip(this, at, Screen, sh.Title, new[] { new TipLine(sh.Text, Palette.TextDim), new TipLine($"{sh.Day}일 {SimTime.Clock(sh.Tick)} · {RoomName(sh)}", Palette.TextMuted) }); }
                px += pw + 10f;
            }
            y += rowH;
        }
        if (pages > 1)
        {
            float by = bottom - 14f;
            var prev = new Rect2(x, by - 14f, 70f, 20f);
            var next = new Rect2(right - 70f, by - 14f, 70f, 20f);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 8f, by), "◀ 최근", Ui.TextTiny, _albumPage > 0 ? Palette.Accent : Palette.TextMuted);
            Gfx.TextRight(this, Fonts.Body, new Vector2(right - 8f, by), "지난날 ▶", Ui.TextTiny, _albumPage < pages - 1 ? Palette.Accent : Palette.TextMuted);
            Gfx.TextCentered(this, Fonts.Body, new Vector2((x + right) * 0.5f, by), $"{_albumPage + 1}/{pages}", Ui.TextTiny, Palette.TextMuted);
            _buttons.Add((prev, () => _albumPage = Math.Max(0, _albumPage - 1)));
            _buttons.Add((next, () => _albumPage++));
        }
    }
}
