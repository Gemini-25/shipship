using ShipEngine;

namespace ShipView;

/// <summary>지난 장면을 다시 볼 길 (docs/OBSERVER.md §4.3): ● 바로 (고리 · 금고) · ◐ 감아 (0시 세계 · 앞선 금고 찍기에서 — 몇 초) · ○ 사진만 (이레가 넘었다).</summary>
public enum SceneWay : byte { Now, Rewind, Photo }

/// <summary>금고 한 칸: 그 장면 (0시 세계는 null) · 찍은 틱 · 날 (장면은 정점의 날) · 날것 길이 · 바이트 (압축했으면 Packed).</summary>
public sealed class VaultEntry
{
    public Highlight? H;
    public long Tick;
    public int Day;
    public int RawLength;
    public byte[] Data = Array.Empty<byte>();
    public bool Packed;
    /// <summary>묶음 파일 이름 (vault/ 아래 — 같은 찍기는 같은 이름).</summary>
    public string File => H == null ? $"d-{Day}.bin" : $"k-{Tick}.bin";
}

/// <summary>
/// 장면 금고 (docs/OBSERVER.md §4.1 · 4.2 — V4 · 화면만): 하이라이트가 생기면 그 다시보기가 쓸 고리 찍기를 복사해 둔다 (시작이 당겨지면 더 앞의 찍기로) ·
/// 날마다 0시 세계 · 하루 다섯 (그날 장의 '다시볼 장면 다섯'과 같은 줄 — 무게 순) · 이레치 · 압축 (Brotli 빠르게)은 다른 일꾼 ·
/// 바이트 한도 (96MB)를 넘으면 하루 셋 → 그래도 넘치면 가벼운 것부터. 놓아도 원인 글 · 장 줄은 남는다.
/// 세계는 읽지 않는다 — 산 세계의 찍기 바이트를 받아 둘 뿐 (되살리면 같은 세계).
/// </summary>
public sealed class SceneVault
{
    public const int PerDay = 5, TightPerDay = 3, KeepDays = 7;
    public long MaxBytes = 96L << 20;
    /// <summary>압축을 다른 일꾼에서 (시험은 곧바로) · 켬 (끄면 화면이 금고를 채우지 않는다 — 시험 K5).</summary>
    public bool PackLater = true, On = true;
    public readonly List<VaultEntry> Scenes = new(), DayWorlds = new();
    private readonly object _gate = new();
    private int _trimmed = int.MinValue;
    private bool _dirty;

    /// <summary>금고가 쥔 바이트 (압축한 것은 압축 길이).</summary>
    public long Bytes { get { lock (_gate) return Scenes.Sum(e => (long)e.Data.Length) + DayWorlds.Sum(e => (long)e.Data.Length); } }

    public VaultEntry? Of(Highlight h) => Scenes.FirstOrDefault(e => ReferenceEquals(e.H, h));
    public bool Has(Highlight h) => Of(h) != null;

    /// <summary>
    /// 그 장면의 고리 찍기를 금고에. 이미 그보다 앞의 찍기가 있으면 그대로 · 시작이 당겨져 더 앞의 찍기가 오면 바꾼다.
    /// lender (연출)가 있으면 고리 칸을 빌린다 — 복사하지 않고 (≤ 1ms) 다른 일꾼이 압축한 뒤 돌려준다 (그동안 고리는 그 칸을 다시 쓰지 않는다) · 없으면 복사한다.
    /// </summary>
    public void Pin(Highlight h, (long tick, byte[] snap, int len) ring, Director? lender = null)
    {
        var e = Of(h);
        if (e != null && e.Tick <= ring.tick) return;
        if (e == null) { e = new VaultEntry { H = h }; Scenes.Add(e); }
        e.Day = (int)(Math.Max(0, h.Tick) / Time.TicksPerDay);
        if (lender != null && PackLater) { lender.Lent[ring.snap] = 0; Put(e, ring.tick, ring.snap, ring.len, lender); return; }
        var raw = new byte[ring.len];
        Buffer.BlockCopy(ring.snap, 0, raw, 0, ring.len);
        Put(e, ring.tick, raw, raw.Length, null);
    }

    /// <summary>날이 바뀐 틱의 세계 (날마다 저장과 같은 바이트) — 이레치.</summary>
    public void DayWorld(long tick, byte[] bytes)
    {
        int day = (int)(Math.Max(0, tick) / Time.TicksPerDay);
        DayWorlds.RemoveAll(x => x.Day == day);
        var e = new VaultEntry { Day = day };
        DayWorlds.Add(e);
        Put(e, tick, bytes, bytes.Length, null);
    }

    /// <summary>칸에 넣는다 (raw의 앞 len 바이트 — 압축 전 · 빌린 고리 칸이면 압축 뒤 돌려준다).</summary>
    private void Put(VaultEntry e, long tick, byte[] raw, int len, Director? lender)
    {
        lock (_gate) { e.Tick = tick; e.RawLength = len; e.Data = raw; e.Packed = false; }
        _dirty = true;
        if (!PackLater) { lock (_gate) { e.Data = VoyageBundle.Pack(raw, len); e.Packed = true; } return; }
        Task.Run(() =>
        {
            var packed = VoyageBundle.Pack(raw, len);
            lock (_gate) if (ReferenceEquals(e.Data, raw)) { e.Data = packed; e.Packed = true; }   // 그사이 다른 찍기로 바뀌었으면 버린다
            lender?.Lent.TryRemove(raw, out _);
        });
    }

    /// <summary>그 장면을 놓는다 (시험 · 손으로 지운 장면).</summary>
    public void Drop(Highlight h) => Scenes.RemoveAll(e => ReferenceEquals(e.H, h));

    /// <summary>
    /// 오늘 (날 번호)에 맞춰 줄인다: 이레가 넘은 장면 · 0시 세계를 놓고 · 하루 다섯 (무게 순 — 같으면 이른 것) · 바이트 한도를 넘으면 하루 셋 → 그래도 넘치면 가벼운 것부터.
    /// </summary>
    public void Trim(int today)
    {
        if (today == _trimmed && !_dirty) return;   // 바뀐 것이 없다 (틱마다 부른다)
        _trimmed = today; _dirty = false;
        int oldest = today - (KeepDays - 1);
        Scenes.RemoveAll(e => e.Day < oldest);
        DayWorlds.RemoveAll(e => e.Day < oldest);
        PerDayKeep(PerDay);
        if (Bytes > MaxBytes) PerDayKeep(TightPerDay);
        while (Bytes > MaxBytes && Scenes.Count > 0)
        {
            var light = Scenes.OrderBy(e => e.H!.Score).ThenByDescending(e => e.H!.Tick).First();
            Scenes.Remove(light);
        }
    }

    private void PerDayKeep(int keep)
    {
        foreach (var g in Scenes.GroupBy(e => e.Day).ToList())
            foreach (var e in g.OrderByDescending(x => x.H!.Score).ThenBy(x => x.H!.Tick).Skip(keep)) Scenes.Remove(e);
    }

    /// <summary>날것 바이트 (압축했으면 푼다 — ≤ 0.3초).</summary>
    public byte[] Raw(VaultEntry e)
    {
        byte[] d; bool packed; int len;
        lock (_gate) { d = e.Data; packed = e.Packed; len = e.RawLength; }
        if (packed) return VoyageBundle.Unpack(d, len);
        return d.Length == len ? d : d.AsSpan(0, len).ToArray();   // 빌린 고리 칸 (압축 전)
    }

    /// <summary>그 장면을 감아 갈 닻: 시작 (첫 조짐) 앞의 가장 가까운 0시 세계 · 다른 장면의 금고 찍기 (없으면 null).</summary>
    public VaultEntry? Anchor(Highlight h)
    {
        VaultEntry? best = null;
        foreach (var e in DayWorlds.Concat(Scenes))
            if (e.Tick <= h.StartTick && (best == null || e.Tick > best.Tick)) best = e;
        return best;
    }

    /// <summary>압축이 다 끝날 때까지 (저장 · 시험).</summary>
    public void WaitPacked(int ms = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until) { lock (_gate) if (Scenes.All(e => e.Packed) && DayWorlds.All(e => e.Packed)) return; Thread.Sleep(2); }
    }

    // ── 묶음 마디 '금고' (목록 — 바이트는 vault/ 아래 파일) ──
    public const string Part = "금고";

    /// <summary>목록을 쓴다: 갈래 (0 장면 · 1 0시 세계) · 하이라이트 번호 · 틱 · 날 · 날것 길이 · 파일 이름 (바이트는 files에 — 압축 전이면 쓰는 일꾼이 압축한다).</summary>
    public byte[] SaveIndex(IReadOnlyList<Highlight> highlights, Dictionary<string, (byte[] data, bool packed, int raw)> files)
    {
        var w = new StateWriter(1 << 12);
        w.Tag("금고"); w.I32(1);
        var hid = new Dictionary<Highlight, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < highlights.Count; i++) hid[highlights[i]] = i;
        var all = Scenes.Where(e => e.H != null && hid.ContainsKey(e.H)).Concat(DayWorlds).ToList();
        w.I32(all.Count);
        foreach (var e in all)
        {
            byte[] d; bool packed; int len;
            lock (_gate) { d = e.Data; packed = e.Packed; len = e.RawLength; }
            w.Byte((byte)(e.H == null ? 1 : 0)); w.I32(e.H != null ? hid[e.H] : -1); w.I64(e.Tick); w.I32(e.Day); w.I32(len); w.Str(e.File);
            files[e.File] = (packed || d.Length == len ? d : d.AsSpan(0, len).ToArray(), packed, len);   // 빌린 고리 칸은 여기서 떼어 준다 (쓰는 일꾼이 읽는 동안 고리가 다시 쓰지 않게)
        }
        w.Tag("금고 끝");
        return w.ToArray();
    }

    /// <summary>목록을 읽어 금고를 채운다 (files: 파일 이름 → 압축 바이트 — 없는 파일의 칸은 건너뛴다). 읽은 칸 수 (깨졌으면 -1).</summary>
    public int LoadIndex(byte[] index, IReadOnlyList<Highlight> highlights, Func<string, byte[]?> file)
    {
        Scenes.Clear(); DayWorlds.Clear();
        try
        {
            var r = new StateReader(index);
            r.Tag("금고");
            if (r.I32() != 1) return -1;
            int n = r.I32(), got = 0;
            for (int i = 0; i < n; i++)
            {
                byte kind = r.Byte(); int hi = r.I32(); long tick = r.I64(); int day = r.I32(); int len = r.I32(); string name = r.Str() ?? "";
                var data = file(name);
                if (data == null || kind == 0 && (hi < 0 || hi >= highlights.Count)) continue;
                var e = new VaultEntry { H = kind == 0 ? highlights[hi] : null, Tick = tick, Day = day, RawLength = len, Data = data, Packed = true };
                (kind == 0 ? Scenes : DayWorlds).Add(e);
                got++;
            }
            r.Tag("금고 끝");
            return got;
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or ArgumentException or IndexOutOfRangeException) { Scenes.Clear(); DayWorlds.Clear(); return -1; }
    }
}

/// <summary>
/// 장면 금고 · 감기 (docs/OBSERVER.md §4 — V4): 산 세계가 나아갈 때마다 새 · 커진 하이라이트를 금고에 · 날이 바뀐 틱에 0시 세계 ·
/// 다시보기는 고리 → 금고 → 닻 + 감기 (다른 일꾼이 감고 · 항해 손 기록을 그 틱에 먹인다 · 감는 동안 산 세계는 멈춘다 · '감는 중 — 12초 [그만]') → 없으면 사진만.
/// </summary>
public sealed partial class LiveShow
{
    public readonly SceneVault Vault = new();
    private int _vaultDays = -1;
    private bool _dayWorldDue;

    /// <summary>산 세계가 나아간 뒤 (Advance · FastForward): 최근 하이라이트 (30분 안에 이어진 것)를 금고에 · 날이 바뀌었으면 0시 세계 (주컴퓨터 일꾼이 셈 중이면 다음으로) · 줄이기.</summary>
    private void VaultTick()
    {
        if (!Vault.On) return;
        var hs = Dir.Highlights;
        for (int i = hs.Count - 1; i >= 0 && i >= hs.Count - 8; i--)
        {
            var h = hs[i];
            if (Sim.Tick - h.Last > Time.Minutes(30)) break;
            if (Dir.RingFor(h) is { } r) Vault.Pin(h, r, Dir);   // 고리 칸을 빌린다 (복사 없이 — 압축은 다른 일꾼)
        }
        int days = Dir.Chapters.Count;   // 날이 바뀌면 장이 는다 (한 주 장이 함께 늘어도 한 번)
        if (_vaultDays >= 0 && days > _vaultDays) _dayWorldDue = true;
        _vaultDays = days;
        if (_dayWorldDue && !(Sim.Find<ShipMind>()?.WorkerBusy ?? false)) { _dayWorldDue = false; Vault.DayWorld(Sim.Tick, Sim.Save()); }
        Vault.Trim((int)(Sim.Tick / Time.TicksPerDay));
    }

    /// <summary>그 장면을 다시 볼 길 · ◐면 감는 어림 초 (닻에서 시작까지 × 화면 없는 틱 시간).</summary>
    public SceneWay WayOf(Highlight h, out double sec)
    {
        sec = 0;
        if (Dir.RingFor(h) != null || Vault.Has(h)) return SceneWay.Now;
        if (Vault.Anchor(h) is { } a) { sec = Math.Max(0, h.StartTick - a.Tick) * Math.Max(0.05, _tickMs) / 1000; return SceneWay.Rewind; }
        return SceneWay.Photo;
    }

    /// <summary>판 줄의 표 (1 ● · 2 ◐ · 3 ○) · 풀이 · ▶ 켜짐.</summary>
    private (byte mark, string sub, bool can) WayRow(Highlight h)
    {
        var way = WayOf(h, out double sec);
        return way switch
        {
            SceneWay.Now => (1, "", true),
            SceneWay.Rewind => (2, $"약 {Math.Max(1, Math.Ceiling(sec)):0}초 감는다 — 금고에서 놓친 장면", true),
            _ => (3, PhotoWhy, false),
        };
    }
    public const string PhotoWhy = "이레가 넘어 다시 볼 수 없다 — 사진만 남았다";

    // ── 감기 (다른 일꾼 — 산 세계는 멈춘다) ──
    private Task<Sim?>? _rewind;
    private CancellationTokenSource? _rewindStop;
    private Highlight? _rewindOf;
    private long _rewindFrom, _rewindTo;
    private long _rewindAt;
    public bool Rewinding => _rewind != null;

    /// <summary>닻 (0시 세계 · 다른 장면 찍기)에서 그 장면의 시작까지 감는다 — 다른 일꾼 · 항해 손 기록을 그 틱에 먹인다 (다시보기와 같은 먹이기). 끝나면 Advance가 다시보기로 잇는다.</summary>
    private bool StartRewind(Highlight h, VaultEntry a)
    {
        StopRewind();
        LogHands();
        var hands = _handLog.Where(o => o.Tick >= a.Tick).ToList();
        var setup = Sim.Setup; var bytes = Vault.Raw(a);
        long target = Math.Max(a.Tick, h.StartTick);
        var stop = _rewindStop = new CancellationTokenSource();
        _rewindOf = h; _rewindFrom = a.Tick; _rewindTo = target; Interlocked.Exchange(ref _rewindAt, a.Tick);
        _rewind = Task.Run(() =>
        {
            var w = new Sim(setup);
            w.Load(bytes);
            var hand = w.Find<Hand>();
            var feed = hand == null ? new List<HandOrder>() : hands.Where(o => o.Seq > hand.Seq).ToList();
            while (w.Tick < target)
            {
                if (stop.IsCancellationRequested) return null;
                if (hand != null) foreach (var o in feed) if (o.Tick == w.Tick) hand.Enqueue(o);
                w.Step();
                Interlocked.Exchange(ref _rewindAt, w.Tick);
            }
            return w;
        });
        Disarm();
        return true;
    }

    /// <summary>[그만]: 감기를 버린다 (산 세계로).</summary>
    public void StopRewind()
    {
        if (_rewind == null) return;
        _rewindStop?.Cancel();
        _rewind = null; _rewindOf = null; _rewindStop = null;
    }

    /// <summary>감기가 끝났나 (Advance 앞머리): 끝났으면 다시보기로 잇는다.</summary>
    private void PollRewind()
    {
        if (_rewind == null || !_rewind.IsCompleted) return;
        var w = _rewind.IsCompletedSuccessfully ? _rewind.Result : null;
        var h = _rewindOf;
        _rewind = null; _rewindOf = null; _rewindStop = null;
        if (w != null && h != null) BeginReplay(h, w);
        else if (h != null) Notify("그 장면을 감지 못했다 — 사진만 남았다");
    }

    /// <summary>시험: 감기가 끝날 때까지 기다린다 (끝났으면 true).</summary>
    public bool WaitRewind(int ms) => _rewind == null || _rewind.Wait(ms);

    /// <summary>'감는 중' 띠 글 · 남은 어림 초 (감는 중이 아니면 빈 글).</summary>
    private (string text, double left) RewindNow()
    {
        if (_rewind == null || _rewindOf == null) return ("", 0);
        long at = Interlocked.Read(ref _rewindAt);
        double left = Math.Max(0, _rewindTo - at) * Math.Max(0.05, _tickMs) / 1000;
        int pct = (int)(100 * (at - _rewindFrom) / Math.Max(1.0, _rewindTo - _rewindFrom));
        return ($"감는 중 — 약 {Math.Max(1, Math.Ceiling(left)):0}초 ({pct}%) · {HourMin(_rewindOf.StartTick)} {_rewindOf.Title}", left);
    }
}
