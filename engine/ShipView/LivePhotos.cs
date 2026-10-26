using ShipEngine;
using ShipSim.Art;

namespace ShipView;

/// <summary>사진 갈래: 정점 사진 (하이라이트의 정점 · 그 칸 둘레) · 마지막 사진 (숨진 자리 — 묘비) · 함장이 찍은 사진 (F12 · 사진 모드 — 지금 화면).</summary>
public enum PhotoKind : byte { Peak, Last, Screen }

/// <summary>사진 한 장 (사진첩 · 묘비 · ○ 장면의 [사진]). 픽셀은 PNG를 짓는 동안 RGBA · 지은 뒤 PNG 바이트만 쥔다.</summary>
public sealed class Photo
{
    public int Id;                 // 1부터 (사진첩 줄 열쇠 · 파일 이름)
    public PhotoKind Kind;
    public long Tick;              // 찍은 틱
    public long Peak = -1;         // 정점 사진: 찍을 때의 정점 (Highlight.Tick) — 바뀌면 (게임 10초 뒤) 다시 찍는다
    public int Cell = -1, X0, Y0;  // 둘레의 가운데 칸 · 그림 왼쪽 위 칸 (정점 · 마지막)
    public int W, H;               // 픽셀
    public Highlight? Scene;       // ▶ 그 장면 (정점 사진)
    public int Agent = -1;         // 숨진 이 (마지막 사진)
    /// <summary>자막 — 그림 위에 쓰지 않고 곁에 둔다: '디딤호 · 3일째 07:04 · 박서연이 김하늘을 업고 나왔다'.</summary>
    public string Caption = "";
    public string Title = "";      // 사진첩 줄 글
    internal byte[]? Raw, Png;     // RGBA (PNG를 짓는 동안) · PNG 바이트 (다른 일꾼이 짓는다)
    internal int Stamp;            // 다시 찍을 때마다 오른다 (크게 연 그림을 새로)
    /// <summary>곳간 파일 이름 (번호 · 찍은 틱 — 다시 찍으면 이름이 바뀐다: 곳간은 없는 파일만 쓰니 옛 그림이 같은 이름으로 남지 않게).</summary>
    public string File => $"p-{Id}-{Tick}.png";
}

/// <summary>
/// 사진첩 (docs/OBSERVER.md §4.4 · §11 — V5 · 화면만 · 세계는 읽기만): 하이라이트의 정점이 바뀌면 그 칸 둘레 40×22칸을 16도트로 (640×352 — 한 장면에 게임 10초 안 다시 찍지 않는다) ·
/// 숨진 자리 한 장 (묘비 '마지막 사진') · 함장이 찍은 화면 (F12). PNG는 다른 일꾼이 짓는다 · 예순 장을 넘으면 가벼운 정점 사진부터 놓는다 · 묶음 마디 '사진' + photos/*.png.
/// </summary>
public sealed class PhotoBook
{
    public const int ViewW = 40, ViewH = 22, Dots = 16, Keep = 60;
    /// <summary>한 장면을 다시 찍기까지 (게임 10초 — 틱).</summary>
    public static readonly long Again = (long)Math.Ceiling(10 / Time.SecondsPerTick);
    /// <summary>PNG를 다른 일꾼에서 (시험은 곧바로) · 켬 (끄면 정점 · 마지막 사진을 찍지 않는다 — 시험 W1 쌍둥이).</summary>
    public bool PackLater = true, On = true;
    public readonly List<Photo> Photos = new();
    /// <summary>사진첩이 바뀔 때마다 오른다 (기록 판 줄을 다시 짓는다).</summary>
    public int Version { get; private set; }
    private readonly Painter _painter;
    private readonly object _gate = new();
    private int _next = 1;

    public PhotoBook(Painter painter) { _painter = painter; }

    public Photo? Find(int id) => Photos.FirstOrDefault(p => p.Id == id);
    public Photo? PeakOf(Highlight h) { foreach (var p in Photos) if (p.Kind == PhotoKind.Peak && ReferenceEquals(p.Scene, h)) return p; return null; }
    public Photo? LastOf(int agent) { foreach (var p in Photos) if (p.Kind == PhotoKind.Last && p.Agent == agent) return p; return null; }

    /// <summary>
    /// 그 칸 둘레 (40×22칸 · bounds 안으로 당긴다)를 16도트로 찍는다 — 화면 화가로 (틱 끝 자세 · 겹쳐 보기 없이 · 다음 장은 화면이 제 값을 다시 넣는다).
    /// 정점 사진은 그 장면의 사진을 바꾼다 (한 장면 한 장). 끈 사진첩이면 null.
    /// </summary>
    public Photo? Take(Sim w, int cell, long tick, string caption, PhotoKind kind, Highlight? scene = null, int agent = -1, string title = "", (int x0, int y0, int x1, int y1)? bounds = null, int frame = 0)
    {
        if (!On) return null;
        var L = w.Layout;
        var b = bounds ?? (0, 0, L.W - 1, L.H - 1);
        int cx = cell >= 0 ? L.X(cell) : (b.x0 + b.x1) / 2, cy = cell >= 0 ? L.Y(cell) : (b.y0 + b.y1) / 2;
        int x0 = Fit(cx, b.x0, b.x1, ViewW), y0 = Fit(cy, b.y0, b.y1, ViewH);
        var p = _painter;
        p.Show = Overlay.None; p.ChainCells = null; p.Poses = null; p.SkyTh = double.NaN; p.ShipX = p.ShipY = double.NaN; p.TickFrac = 1; p.Strikes = null; p.Sub = 1; p.Still = false;
        var img = p.FrameFar(w, x0, y0, ViewW, ViewH, frame, Dots);
        var photo = kind == PhotoKind.Peak && scene != null ? PeakOf(scene) : null;
        if (photo == null) { photo = new Photo { Id = _next++, Kind = kind }; Photos.Add(photo); }
        photo.Tick = tick; photo.Peak = scene?.Tick ?? -1; photo.Cell = cell; photo.X0 = x0; photo.Y0 = y0; photo.Scene = scene; photo.Agent = agent; photo.Caption = caption; photo.Title = title.Length > 0 ? title : caption;
        Put(photo, img.W, img.H, img.Px);
        Trim();
        return photo;
    }

    /// <summary>함장이 찍은 화면 (F12 · 사진 모드 — 메인 스레드가 넘긴 RGBA): 켬과 상관없이 넣는다.</summary>
    public Photo Add(int w, int h, byte[] rgba, long tick, string caption, string title)
    {
        var photo = new Photo { Id = _next++, Kind = PhotoKind.Screen, Tick = tick, Caption = caption, Title = title.Length > 0 ? title : caption };
        Photos.Add(photo);
        Put(photo, w, h, rgba.Length == w * h * 4 ? rgba : rgba.AsSpan(0, w * h * 4).ToArray());
        Trim();
        return photo;
    }

    private static int Fit(int c, int lo, int hi, int n) => hi - lo + 1 <= n ? lo - (n - (hi - lo + 1)) / 2 : Math.Clamp(c - n / 2, lo, hi - n + 1);

    /// <summary>픽셀을 넣고 PNG를 짓는다 (다른 일꾼 — 그사이 다시 찍었으면 버린다).</summary>
    private void Put(Photo photo, int w, int h, byte[] px)
    {
        lock (_gate) { photo.W = w; photo.H = h; photo.Raw = px; photo.Png = null; photo.Stamp++; }
        Version++;
        if (!PackLater) { var png = Png.Encode(w, h, px); lock (_gate) { photo.Png = png; photo.Raw = null; } return; }
        Task.Run(() =>
        {
            var png = Png.Encode(w, h, px);
            lock (_gate) if (ReferenceEquals(photo.Raw, px)) { photo.Png = png; photo.Raw = null; }
        });
    }

    /// <summary>예순 장 넘게: 가벼운 정점 사진 (장면 무게 작은 것 · 같으면 이른 것)부터 → 이른 함장 사진 → 이른 마지막 사진.</summary>
    private void Trim()
    {
        while (Photos.Count > Keep)
        {
            var drop = Photos.Where(p => p.Kind == PhotoKind.Peak).OrderBy(p => p.Scene?.Score ?? 0).ThenBy(p => p.Tick).FirstOrDefault()
                ?? Photos.Where(p => p.Kind == PhotoKind.Screen).OrderBy(p => p.Tick).FirstOrDefault()
                ?? Photos.OrderBy(p => p.Tick).First();
            Photos.Remove(drop);
            Version++;
        }
    }

    private Photo? _decodedOf; private byte[]? _decodedPng, _decoded;

    /// <summary>RGBA (PNG를 짓는 중이면 그 바이트 · 지었으면 풀어서 — 마지막 한 장은 붙잡아 둔다). 못 풀면 빈 배열.</summary>
    public byte[] Pixels(Photo p)
    {
        byte[]? raw, png;
        lock (_gate) { raw = p.Raw; png = p.Png; }
        if (raw != null) return raw;
        if (png == null) return Array.Empty<byte>();
        if (ReferenceEquals(_decodedOf, p) && ReferenceEquals(_decodedPng, png) && _decoded != null) return _decoded;
        var d = ShipView.Png.Decode(png);
        if (d == null || d.Value.w != p.W || d.Value.h != p.H) return Array.Empty<byte>();
        _decodedOf = p; _decodedPng = png; _decoded = d.Value.px;
        return _decoded;
    }

    /// <summary>PNG가 다 지어질 때까지 (저장 · 시험).</summary>
    public void WaitPacked(int ms = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until) { lock (_gate) if (Photos.All(p => p.Png != null)) return; Thread.Sleep(2); }
    }

    // ── 묶음 마디 '사진' (목록 — 바이트는 photos/ 아래 PNG 파일) ──
    public const string Part = "사진";

    /// <summary>목록을 쓴다 (사진마다 번호 · 갈래 · 때 · 자리 · 장면 번호 · 사람 · 자막 · 파일 이름) · 바이트는 files에 (PNG · 아직이면 RGBA — 쓰는 일꾼이 짓는다).</summary>
    public byte[] SaveIndex(IReadOnlyList<Highlight> highlights, Dictionary<string, (byte[] data, bool png, int w, int h)> files)
    {
        var w = new StateWriter(1 << 12);
        w.Tag("사진"); w.I32(1);
        var hid = new Dictionary<Highlight, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < highlights.Count; i++) hid[highlights[i]] = i;
        w.I32(_next); w.I32(Photos.Count);
        foreach (var p in Photos)
        {
            byte[]? raw, png;
            lock (_gate) { raw = p.Raw; png = p.Png; }
            w.I32(p.Id); w.Byte((byte)p.Kind); w.I64(p.Tick); w.I64(p.Peak); w.I32(p.Cell); w.I32(p.X0); w.I32(p.Y0); w.I32(p.W); w.I32(p.H);
            w.I32(p.Scene != null && hid.TryGetValue(p.Scene, out int si) ? si : -1); w.I32(p.Agent); w.Str(p.Caption); w.Str(p.Title); w.Str(p.File);
            files[p.File] = png != null ? (png, true, p.W, p.H) : (raw ?? Array.Empty<byte>(), false, p.W, p.H);
        }
        w.Tag("사진 끝");
        return w.ToArray();
    }

    /// <summary>목록을 읽어 사진첩을 채운다 (file: 파일 이름 → PNG 바이트 — 없는 파일의 사진은 건너뛴다). 읽은 장 수 (깨졌으면 -1).</summary>
    public int LoadIndex(byte[] index, IReadOnlyList<Highlight> highlights, Func<string, byte[]?> file)
    {
        Photos.Clear(); Version++;
        try
        {
            var r = new StateReader(index);
            r.Tag("사진");
            if (r.I32() != 1) return -1;
            int next = r.I32(), n = r.I32(), got = 0;
            for (int i = 0; i < n; i++)
            {
                var p = new Photo { Id = r.I32(), Kind = (PhotoKind)r.Byte(), Tick = r.I64(), Peak = r.I64(), Cell = r.I32(), X0 = r.I32(), Y0 = r.I32(), W = r.I32(), H = r.I32() };
                int si = r.I32(); p.Agent = r.I32(); p.Caption = r.Str() ?? ""; p.Title = r.Str() ?? ""; string name = r.Str() ?? "";
                var data = file(name);
                if (data == null) continue;
                p.Scene = si >= 0 && si < highlights.Count ? highlights[si] : null;
                p.Png = data;
                Photos.Add(p);
                got++;
            }
            r.Tag("사진 끝");
            _next = Math.Max(next, Photos.Count > 0 ? Photos.Max(p => p.Id) + 1 : 1);
            return got;
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or ArgumentException or IndexOutOfRangeException) { Photos.Clear(); return -1; }
    }
}

/// <summary>
/// 사진 (docs/OBSERVER.md §4.4 · §11 — V5): 산 세계가 나아갈 때마다 최근 하이라이트의 정점 사진 · 숨진 이의 마지막 사진 · 자막 사진 (F12) ·
/// 크게 보기 (사진첩 · 묘비 · ○ 장면의 [사진] — 가운데 카드 · 자막은 곁에 · ▶ 그 장면) · 사진 모드 (판 · 띠 숨김 · 세계 멈춤 · 손 카메라 · [찍기] [나가기]).
/// </summary>
public sealed partial class LiveShow
{
    public readonly PhotoBook Photos;
    /// <summary>사진 모드 (Shift+F12 · 보기 묶음 단추): 판 · 띠를 숨기고 세계를 멈춘다 (고를 수 있다) · 카메라는 손으로.</summary>
    public bool PhotoMode { get; private set; }
    private bool _photoWasPaused;
    private long _photoFacts, _diedSeen;
    private Photo? _photoShown;
    private Img? _photoImg;
    private int _photoStamp = -1, _photoVer;

    /// <summary>사진첩 줄 열쇠 (기록 판 작은 탭 2 — 0은 머리 줄 [폴더 열기]).</summary>
    public static long KeyAlbum(int id) => KeyRecord(2, id);
    /// <summary>사진의 때 ('3일째 07:04').</summary>
    public static string PhotoWhen(long tick) => DayClock(tick);
    private string PhotoCaption(long tick, string text) => $"{ShipName} · {DayClock(tick)}{(text.Length > 0 ? " · " + text : "")}";

    /// <summary>산 세계가 나아간 뒤 (Advance · FastForward): 최근 하이라이트 (30분 안에 이어진 것)의 정점이 바뀌었으면 찍는다 (그 장면을 10초 안에 다시 찍지는 않는다) · 숨진 이의 자리.</summary>
    private void PhotoTick()
    {
        if (!Photos.On) { _photoFacts = Sim.Facts.Total; return; }
        var hs = Dir.Highlights;
        int frame = (int)(_anim * AnimFps);
        for (int i = hs.Count - 1; i >= 0 && i >= hs.Count - 8; i--)
        {
            var h = hs[i];
            if (Sim.Tick - h.Last > Time.Minutes(30)) break;
            var p = Photos.PeakOf(h);
            if (p != null && (p.Peak == h.Tick || Sim.Tick - p.Tick < PhotoBook.Again)) continue;
            Photos.Take(Sim, h.Cell, Sim.Tick, PhotoCaption(h.Tick, Director.Short(h.Title)), PhotoKind.Peak, h, -1, h.Title, _bbox, frame);
        }
        long died = Sim.Facts.CountOf(FactKind.Died);
        if (died != _diedSeen && Sim.Find<Mind>() is { } mind)
            foreach (var f in Sim.Facts.Since(_photoFacts))
            {
                if (f.Kind != FactKind.Died) continue;
                int a = mind.Agents.FindIndex(x => x.IsPerson && x.C.Body == f.Actor);
                if (a < 0 || Photos.LastOf(a) != null) continue;
                string title = $"{mind.NameOf(a)}의 마지막 모습";
                Photos.Take(Sim, f.Cell >= 0 ? f.Cell : mind.Agents[a].C.Cell, Sim.Tick, PhotoCaption(f.Tick, title), PhotoKind.Last, null, a, title, _bbox, frame);
            }
        _diedSeen = died;
        _photoFacts = Sim.Facts.Total;
    }

    /// <summary>자막 사진 (F12 — 메인 스레드가 넘긴 뷰포트 RGBA): 사진첩에 · 자막 '디딤호 · 항해 4127 · 3일째 07:04 · 아래 줄 (다시보기면 컷)' · 알림 (누르면 사진첩).</summary>
    public void ScreenPhoto(int w, int h, byte[] rgba)
    {
        if (w <= 0 || h <= 0 || rgba.Length < w * h * 4) return;
        string line = _replayOf != null ? (ShotNow is { } sn ? $"다시보기 — {_replayOf.Title} · {sn.Kind}" : $"다시보기 — {_replayOf.Title}") : Story();
        Photos.Add(w, h, rgba, World.Tick, $"{ShipName} · 항해 {Voyage} · {DayClock(World.Tick)}{(line.Length > 0 ? " · " + line : "")}", line.Length > 0 ? line : "지켜보던 화면");
        Notify("사진 — 사진첩에 넣었다 (누르면 사진첩)", 4, PanelKind.Record);
        _noticeAlbum = true;
    }

    /// <summary>사진을 크게 연다 (가운데 카드 — 자막은 곁에 · ▶ 그 장면).</summary>
    public void ShowPhoto(int id) { if (Photos.Find(id) is { } p) { _photoShown = p; _photoStamp = -1; } }
    public void ClosePhoto() { _photoShown = null; _photoImg = null; }

    /// <summary>사진 카드의 [▶ 그 장면]: 다시본다 (카드 · 판을 닫는다).</summary>
    public bool PhotoScene()
    {
        var h = _photoShown?.Scene;
        if (h == null) return false;
        if (!StartReplay(h)) { Notify(PhotoWhy); return false; }
        ClosePhoto(); ClosePanel();
        return true;
    }

    /// <summary>크게 연 사진의 그림 (다시 찍었으면 새로 · 놓은 사진이면 닫는다).</summary>
    private Img? PhotoNow()
    {
        var p = _photoShown;
        if (p == null) return null;
        if (!Photos.Photos.Contains(p)) { ClosePhoto(); return null; }
        if (_photoImg == null || _photoStamp != p.Stamp)
        {
            var px = Photos.Pixels(p);
            if (px.Length < p.W * p.H * 4) { ClosePhoto(); return null; }
            var img = new Img(p.W, p.H);
            Buffer.BlockCopy(px, 0, img.Px, 0, p.W * p.H * 4);
            _photoImg = img; _photoStamp = p.Stamp; _photoVer++;
        }
        return _photoImg;
    }

    /// <summary>사진 모드를 켜고 끈다: 켜면 세계를 멈추고 (스페이스로 고를 수 있다) 카메라는 손으로 · 끄면 멈춤을 되돌린다.</summary>
    public void SetPhotoMode(bool on)
    {
        if (on == PhotoMode) return;
        PhotoMode = on;
        if (on)
        {
            _photoWasPaused = Paused; Paused = true;
            Follow = false; Overview = false; FollowSelected = false; _handAt = _anim;
            ClosePhoto();
        }
        else Paused = _photoWasPaused;
    }

    /// <summary>그 장면을 사진으로만 볼 수 있으면 그 정점 사진 (○ 줄의 [사진]).</summary>
    private Photo? PhotoOnly(Highlight h) => WayOf(h, out _) == SceneWay.Photo ? Photos.PeakOf(h) : null;

    /// <summary>사진첩: 머리 (장 수 · [폴더 열기]) → 사진마다 한 줄 (날 차례 · 누르면 크게 · ▶ 그 장면).</summary>
    private void AlbumRows(List<PanelRow> rows)
    {
        var list = Photos.Photos.OrderBy(p => p.Tick).ThenBy(p => p.Id).ToList();
        int peak = list.Count(p => p.Kind == PhotoKind.Peak), last = list.Count(p => p.Kind == PhotoKind.Last), mine = list.Count - peak - last;
        string kinds = string.Join(" · ", new[] { peak > 0 ? $"정점 {peak}" : "", last > 0 ? $"마지막 {last}" : "", mine > 0 ? $"함장이 찍은 {mine}" : "" }.Where(x => x.Length > 0));
        rows.Add(new PanelRow(list.Count == 0 ? "사진첩은 아직 비어 있다" : $"사진 {list.Count}장 — {kinds}",
            "볼 만한 일이 나면 그 자리를 한 장씩 남긴다 · 지금 화면은 F12 · 사진 모드는 Shift+F12", false, Key: KeyAlbum(0), Act: list.Count > 0 ? "폴더 열기" : "", ActOn: list.Count > 0));
        foreach (var p in list)
        {
            string kind = p.Kind switch { PhotoKind.Peak => "정점 사진", PhotoKind.Last => "마지막 사진", _ => "함장이 찍은 사진" } + " · 누르면 크게";
            if (p.Scene is { } h)
            {
                var (mark, _, can) = WayRow(h);
                rows.Add(new PanelRow($"{PhotoWhen(p.Tick)}  {p.Title}", kind, false, Key: KeyAlbum(p.Id), Act: "▶ 다시보기", ActOn: can, Why: PhotoWhy, Mark: mark));
            }
            else rows.Add(new PanelRow($"{PhotoWhen(p.Tick)}  {p.Title}", kind, false, Key: KeyAlbum(p.Id)));
        }
    }
}
