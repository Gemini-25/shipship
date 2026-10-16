using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v23 오늘의 장면: 연대기에 장면 후보가 생기면 그 순간 그 자리를 따로 찍어 둔다 (배 그림만 — 화면 글 · 이름표는 빼고).
//   배 그림을 함께 쓰는 작은 화면(480×270)에 카메라 하나를 그 자리에 두고 한 번만 그린 뒤 사진으로 남긴다.
//   user://album 에 남기고, 하루 끝 카드와 사진첩이 꺼내 쓴다. 시뮬레이션은 모른다 (찍든 말든 같은 역사).
public partial class SceneShooter : Node
{
    public const int W = 480, H = 270;
    private Main _main = null!;
    private SubViewport _vp = null!;
    private Camera2D _cam = null!;
    private ColorRect _sky = null!;
    private int _done, _pending = -1, _wait;
    private World? _world;
    private static readonly Dictionary<string, Texture2D?> Cache = new();

    public void Init(Main main)
    {
        _main = main;
        _vp = new SubViewport
        {
            Name = "SceneShot", Size = new Vector2I(W, H), Disable3D = true, TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, World2D = main.GetViewport().World2D,
        };
        _cam = new Camera2D { Name = "SceneCam", Enabled = true, Zoom = new Vector2(1.5f, 1.5f) };
        _vp.AddChild(_cam);
        var back = new CanvasLayer { Name = "SceneSky", Layer = -10 }; // 이 작은 화면에만 — 배 뒤 하늘 (지금 구간 빛깔)
        _sky = new ColorRect { Size = new Vector2(W, H), Color = new Color(0.02f, 0.03f, 0.06f) };
        back.AddChild(_sky);
        _vp.AddChild(back);
        AddChild(_vp);
        Reset(main.Sim);
    }

    /// <summary>불러온 세계 · 미리 돌린 시간의 후보는 그 순간이 지났다 — 찍지 않는다.</summary>
    private void Reset(World w) { _world = w; _done = w.Album.LastOffered; _pending = -1; }

    private static string Key(World w, int id) => $"user://album/{w.ShipKey}_{w.Seed}_{id}.png";

    /// <summary>그 장면의 사진 (없으면 null — 그 순간 화면이 없었다).</summary>
    public static Texture2D? Photo(World w, int id)
    {
        string key = Key(w, id);
        if (Cache.TryGetValue(key, out var t)) return t;
        Texture2D? tex = null;
        if (FileAccess.FileExists(key) && Image.LoadFromFile(ProjectSettings.GlobalizePath(key)) is Image img && !img.IsEmpty()) tex = ImageTexture.CreateFromImage(img);
        Cache[key] = tex;
        return tex;
    }

    public override void _Process(double delta)
    {
        var w = _main.Sim;
        if (w != _world) Reset(w);
        var book = w.Album;
        if (_pending >= 0)
        {
            if (--_wait > 0) return;
            var img = _vp.GetTexture().GetImage();
            if (img != null && !img.IsEmpty())
            {
                DirAccess.MakeDirRecursiveAbsolute("user://album");
                string key = Key(w, _pending);
                img.SavePng(key);
                Cache[key] = ImageTexture.CreateFromImage(img);
            }
            _pending = -1;
            return;
        }
        if (book.LastOffered <= _done) return;
        // 새 후보 가운데 가장 센 것 하나 (한 프레임에 여럿이 생기기도 한다) · 그 순간이 지났으면 찍지 않는다
        SceneShot? s = null;
        foreach (var x in book.Today)
            if (x.Id > _done && x.X >= 0 && w.Tick - x.Tick <= SimTime.Minutes(5) && (s == null || x.Score > s.Score)) s = x;
        _done = book.LastOffered;
        if (s == null) return;
        _cam.Position = ShipView.ToPx(new Cell(s.X, s.Y).Center);
        _cam.Zoom = new Vector2(1.5f, 1.5f);
        var v = w.Voyage.View;
        _sky.Color = new Color(0.02f, 0.03f, 0.06f).Lerp(new Color(v.R, v.G, v.B), v.Scenic ? 0.22f : 0.08f);
        if (s.Wide && w.Ship.LiveRooms.Any()) // 배 전체를 한 장에
        {
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
            foreach (var r in w.Ship.LiveRooms) foreach (var c in r.Cells) { x0 = Mathf.Min(x0, c.X); y0 = Mathf.Min(y0, c.Y); x1 = Mathf.Max(x1, c.X); y1 = Mathf.Max(y1, c.Y); }
            float z = Mathf.Min(W / ((x1 - x0 + 4) * ShipView.T), H / ((y1 - y0 + 4) * ShipView.T));
            _cam.Position = new Vector2((x0 + x1 + 1) * 0.5f, (y0 + y1 + 1) * 0.5f) * ShipView.T;
            _cam.Zoom = new Vector2(z, z);
        }
        _vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        _pending = s.Id;
        _wait = 3;
    }
}
