using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v17.7 관찰 도구: 평화로운 장면도 잡는 카메라 · 물건 따라가기 · 사진 모드.
// 화면은 세상을 읽기만 한다 (WatchScenes 는 World 밖 — 카메라가 있든 없든 같은 역사).
public partial class Main
{
    private readonly WatchScenes _eye = new();
    private WatchShot? _scene;
    private ulong _sceneUntilMsec;
    private long _sceneSlowUntilTick = -1;

    /// <summary>지금 카메라가 보고 있는 평화로운 장면 (자막용).</summary>
    public WatchShot? Scene => Time.GetTicksMsec() < _sceneUntilMsec ? _scene : null;

    /// <summary>자동 카메라: 사고가 없을 때 평화로운 장면 (선물 · 늦게 온 관객 · 화해 · 완성 · 고백 · 추모)으로 천천히.</summary>
    private void UpdateWatch()
    {
        var shots = _eye.Poll(Sim); // 늘 읽어 둔다 (켜는 순간 지난 일을 한꺼번에 쫓지 않게)
        if (!Settings.Highlight || !Settings.AutoCamera || Camera.FollowTarget != null || PhotoMode) return;
        if (Sim.Tick < _hotUntilTick || _camTarget != null) return; // 사고가 먼저 · 카메라가 움직이는 중
        var best = shots.Where(s => s.At != null).OrderByDescending(s => s.Weight).FirstOrDefault();
        if (best == null || Time.GetTicksMsec() < _sceneUntilMsec && best.Weight <= (_scene?.Weight ?? 0f)) return; // 방금 잡은 장면은 잠깐 둔다
        _scene = best;
        _sceneUntilMsec = Time.GetTicksMsec() + 7000;
        _sceneSlowUntilTick = Sim.Tick + SimTime.Minutes(best.Kind is WatchKind.Memorial or WatchKind.Confession ? 20 : 12);
        _camTarget = ShipView.ToPx(best.At!.Value) + new Vector2(Hud.RightColumnWidth * 0.5f, 0f) / Camera.Zoom.X;
        _camUntilMsec = Time.GetTicksMsec() + 4000;
        _camZoomTarget = Mathf.Max(Camera.Zoom.X, 1.25f);
        string where = best.RoomId >= 0 && best.RoomId < Sim.Ship.Rooms.Count ? Sim.Ship.Rooms[best.RoomId].Name + " · " : "";
        ShowNotice(where + best.Text);
    }

    /// <summary>자동 배속과 이어서: 평화로운 장면을 잡으면 잠깐 3배속으로 (지나면 다시 빨라진다).</summary>
    private int WatchSpeed(int want) => Settings.Highlight && Sim.Tick < _sceneSlowUntilTick && want > 1 ? 1 : want;

    // ─────────────────────────── 물건 따라가기 ───────────────────────────

    private int _itemCursor = -1;
    /// <summary>지금 따라가는 물건 (없으면 -1).</summary>
    public int FollowItem { get; private set; } = -1;

    /// <summary>Q: 손을 많이 거친 물건(선물 · 사진 · 공구 · 작품 · 유품)을 하나씩 따라간다. ⇧Q 앞으로.</summary>
    public void CycleFollowItem(int dir)
    {
        var list = WatchScenes.Notable(Sim);
        if (list.Count == 0) { ShowNotice("아직 따라가 볼 물건이 없다"); return; }
        _itemCursor = ((_itemCursor + dir) % list.Count + list.Count) % list.Count;
        var b = list[_itemCursor];
        FollowItem = b.Id;
        int id = b.Id;
        Camera.FollowTarget = () => FollowItem == id && Sim.Belongings.All.FirstOrDefault(x => x.Id == id) is Belonging it
            ? ShipView.ToPx(WatchScenes.PositionOf(Sim, it)) + new Vector2(Hud.RightColumnWidth * 0.5f, 0f) / Camera.Zoom.X : null;
        if (Camera.Zoom.X < 1.1f) Camera.Zoom = new Vector2(1.3f, 1.3f);
        var names = WatchScenes.Hands(b).Select(h => Sim.Crew.FirstOrDefault(c => c.Id == h)?.Name ?? "?");
        ShowNotice($"{b.Name} — {string.Join(" → ", names)} · {Sim.Belongings.Where(b)}");
    }

    private void UpdateFollowItem()
    {
        if (FollowItem < 0) return;
        if (Camera.FollowTarget == null || Sim.Belongings.All.All(x => x.Id != FollowItem)) FollowItem = -1; // 화면을 끌었거나 물건이 사라졌다
    }

    public void StopFollowItem() { if (FollowItem < 0) return; FollowItem = -1; Camera.FollowTarget = null; }

    // ─────────────────────────── 사진 모드 ───────────────────────────

    public bool PhotoMode { get; private set; }
    private CanvasLayer? _photoLayer;
    private PhotoFrame? _photoFrame;

    /// <summary>F2: 화면 글을 모두 숨기고 위아래에 검은 띠. Enter 로 찍는다.</summary>
    public void TogglePhotoMode()
    {
        PhotoMode = !PhotoMode;
        if (_photoLayer == null)
        {
            _photoLayer = new CanvasLayer { Name = "Photo", Layer = 15 };
            AddChild(_photoLayer);
            _photoFrame = new PhotoFrame { Name = "PhotoFrame" };
            _photoLayer.AddChild(_photoFrame);
        }
        _photoFrame!.Visible = PhotoMode;
        _photoFrame.Open = PhotoMode ? 0f : 1f;
        if (GetNodeOrNull<CanvasLayer>("UI") is CanvasLayer ui) ui.Visible = !PhotoMode;
        if (GetNodeOrNull<CanvasLayer>("Labels") is CanvasLayer labels) labels.Visible = !PhotoMode;
    }

    /// <summary>사진 모드에서 찍는다: 띠 사이를 잘라 그날 연대기의 사진 자리에 붙인다.</summary>
    public void TakePhoto()
    {
        if (!PhotoMode || _photoFrame == null) return;
        var img = GetViewport().GetTexture().GetImage();
        var inner = _photoFrame.Inner(img.GetSize());
        var shot = img.GetRegion(inner);
        string caption = PhotoCaption();
        PhotoAlbum.Add(Sim.Tick, shot, caption);
        _photoFrame.Flash = 1f;
        ShowNotice($"사진 한 장 — {SimTime.Day(Sim.Tick)}일 연대기에 붙였다");
    }

    /// <summary>사진 밑에 적을 한 줄: 날 · 시각 · 화면 가운데 방 · 그 방에 있는 사람.</summary>
    private string PhotoCaption()
    {
        var cell = ShipView.CellAtPx(Camera.GetScreenCenterPosition());
        var room = Sim.Ship.RoomAt(cell);
        var who = room == null ? new List<string>() : Sim.Crew.Where(c => !c.Dead && c.Room == room).Select(c => c.Name).Take(3).ToList();
        return $"{SimTime.Day(Sim.Tick)}일 {SimTime.Clock(Sim.Tick)}" + (room != null ? $" · {room.Name}" : "") + (who.Count > 0 ? " · " + string.Join("·", who) : "");
    }

    /// <summary>사진 모드 키 (켜져 있을 때 먼저 받는다): F2 나가기 · Enter/PrintScreen 찍기 · Esc 나가기.</summary>
    private bool PhotoKey(InputEventKey key)
    {
        if (key.Keycode == Key.F2) { TogglePhotoMode(); return true; }
        if (!PhotoMode) return false;
        switch (key.Keycode)
        {
            case Key.Enter or Key.KpEnter or Key.Print: TakePhoto(); return true;
            case Key.Escape: TogglePhotoMode(); return true;
        }
        return false;
    }

    /// <summary>매 프레임 (하이라이트 뒤): 장면 카메라 · 물건 따라가기.</summary>
    private void UpdateObservers()
    {
        UpdateWatch();
        UpdateFollowItem();
    }
}

/// <summary>사진 모드의 위아래 검은 띠 · 셔터 번쩍임.</summary>
public partial class PhotoFrame : Control
{
    public float Open, Flash;
    private const float Bar = 0.11f;

    /// <summary>띠 사이 (찍히는 곳).</summary>
    public Rect2I Inner(Vector2I size)
    {
        int h = (int)(size.Y * Bar);
        return new Rect2I(0, h, size.X, size.Y - 2 * h);
    }

    public override void _Process(double delta)
    {
        var s = GetViewportRect().Size;
        if (Size != s) Size = s;
        Open = Mathf.Min(1f, Open + (float)delta * 3f);
        Flash = Mathf.Max(0f, Flash - (float)delta * 2.5f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var s = Size;
        float h = s.Y * Bar * Mathf.SmoothStep(0f, 1f, Open);
        this.Box(new Rect2(0, 0, s.X, h), Colors.Black);
        this.Box(new Rect2(0, s.Y - h, s.X, h), Colors.Black);
        if (Flash > 0f) this.Box(new Rect2(0, h, s.X, s.Y - 2 * h), new Color(1, 1, 1, 0.55f * Flash));
    }
}

/// <summary>찍은 사진 (화면 쪽 — 역사와 무관). 날마다 마지막 한 장이 연대기의 사진 자리에 걸린다. user://photos 에도 남긴다.</summary>
public static class PhotoAlbum
{
    public sealed record Photo(long Tick, int Day, ImageTexture Texture, string Caption);
    public static List<Photo> All { get; } = new();

    public static void Add(long tick, Image img, string caption)
    {
        int day = SimTime.Day(tick);
        All.Add(new Photo(tick, day, ImageTexture.CreateFromImage(img), caption));
        if (All.Count > 60) All.RemoveAt(0);
        DirAccess.MakeDirRecursiveAbsolute("user://photos");
        img.SavePng($"user://photos/{day}일_{SimTime.Clock(tick).Replace(":", "")}.png");
    }

    /// <summary>그 날들 사이에 찍은 마지막 사진.</summary>
    public static Photo? For(int firstDay, int lastDay) => All.LastOrDefault(p => p.Day >= firstDay && p.Day <= lastDay);
}
