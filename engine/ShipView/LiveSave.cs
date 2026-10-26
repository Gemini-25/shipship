using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShipEngine;

namespace ShipView;

/// <summary>언제 남긴 저장인가 (docs/OBSERVER.md §3.2): 날이 바뀐 틱 · 끌 때 · 항해가 끝날 때.</summary>
public enum SaveKind : byte { Daily, Quit, End }

/// <summary>
/// 이어 보기 카드 (card.json — 글뿐 · 판과 상관없이 읽는다 · docs/OBSERVER.md §3.1): 배 · 번호 · 항로 · 날 · 산 이 · 어제 장 제목 · 끝 갈래 · 판 · 저장한 때 · 되살릴 준비.
/// </summary>
public sealed class VoyageMeta
{
    public string Ship { get; set; } = "";          // 배 도면 열쇠 (Didim)
    public string ShipName { get; set; } = "";      // 배 이름 (디딤호)
    public long Voyage { get; set; }                // 항해 번호 (= 세계 시드)
    public int Route { get; set; }
    public int Day { get; set; }                    // 몇째 날 (1부터)
    public long Tick { get; set; }
    public int Alive { get; set; }                  // 산 사람
    public int Aboard { get; set; }                 // 탄 사람
    public int Robots { get; set; }                 // 로봇 · 드론
    public string Yesterday { get; set; } = "";     // 어제 장 제목
    public string End { get; set; } = "";           // 끝 갈래 (빈 글: 항해 중)
    public int WorldVersion { get; set; }
    public int BundleVersion { get; set; }
    public string SavedAt { get; set; } = "";       // 저장한 때 (벽시계 · 세계 밖)
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SaveKind Kind { get; set; }
    public int People { get; set; }                 // 되살릴 준비: 마음의 사람 · 로봇 수
    public int RobotCount { get; set; }
    public string Slot { get; set; } = "";          // 가장 새 저장 칸 (a · b · c)
    public long Serial { get; set; }                // 묶음 차례 (돌려 쓰는 칸 가운데 가장 새것)
    /// <summary>항해 폴더 이름: 배 열쇠 + 항해 번호.</summary>
    [JsonIgnore] public string Folder => $"{Ship}-{Voyage}";

    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static VoyageMeta? FromJson(string s) { try { return JsonSerializer.Deserialize<VoyageMeta>(s, Json); } catch (JsonException) { return null; } }
}

/// <summary>항해 번호 (docs/OBSERVER.md §3.4): 켤 때마다 새로 뽑는다 (벽시계 · 세계 밖) — 번호가 곧 세계 시드 · 확인용 띄우기는 18 (화면 사진 · CI가 같게).</summary>
public static class VoyageNumber
{
    public const int Min = 1000, Max = 99999;
    public const ulong Check = 18;
    public static ulong Draw() => (ulong)Random.Shared.Next(Min, Max + 1);
    /// <summary>넣은 번호 (숫자만 · 1000 ~ 99999 — 앞의 0은 뗀다) · 아니면 null.</summary>
    public static ulong? Parse(string s)
    {
        s = s.Trim();
        if (s.Length == 0 || s.Length > 6 || !s.All(char.IsAsciiDigit)) return null;
        ulong v = ulong.Parse(s);
        return v >= Min && v <= Max ? v : null;
    }
    /// <summary>확인용 띄우기 인자 (화면 사진 · CI — 늘 같은 항해).</summary>
    public static bool CheckRun(IEnumerable<string> args) => args.Any(a => a.StartsWith("--for") || a.StartsWith("--shots") || a.StartsWith("--shot=") || a.StartsWith("--skip") || a == "--ignite" || a.StartsWith("--panel") || a.StartsWith("--pick") || a.StartsWith("--ui"));
    /// <summary>띄울 번호: --voyage= · --seed= 이 먼저 · 확인용 띄우기 (--for · --shots · --ui · --skip …)는 18 · 아니면 새로 뽑는다.</summary>
    public static ulong ForLaunch(IEnumerable<string> args)
    {
        var l = args.ToList();
        foreach (var a in l)
        {
            if (a.StartsWith("--voyage=") && ulong.TryParse(a[9..], out var v)) return v;
            if (a.StartsWith("--seed=") && ulong.TryParse(a[7..], out var sd)) return sd;
        }
        return CheckRun(l) ? Check : Draw();
    }
}

/// <summary>이어 보기 카드 글 (docs/OBSERVER.md §3.3 — 처음 화면): '디딤호 · 항해 4127 · 43일째 · 6명 가운데 5명' · '어제: …' · '끈 때 10-26 21:40'.</summary>
public static class VoyageCard
{
    /// <summary>첫 줄: 배 · 항해 번호 · 날 · 산 이 (끝난 항해는 '17일 — 모두 잃었다').</summary>
    public static string Title(VoyageMeta m) => m.End.Length > 0
        ? $"{m.ShipName} · 항해 {m.Voyage} · {m.Day}일 — {m.End}"
        : $"{m.ShipName} · 항해 {m.Voyage} · {m.Day}일째 · {m.Aboard}명 가운데 {m.Alive}명";
    /// <summary>둘째 줄: 어제의 장 제목 (아직 하루가 지나지 않았으면 그렇게).</summary>
    public static string Yesterday(VoyageMeta m) => m.Yesterday.Length > 0 ? $"어제: {m.Yesterday}" : "어제: 아직 하루가 지나지 않았다";
    /// <summary>셋째 줄: 끈 때 (월-일 시:분).</summary>
    public static string When(VoyageMeta m) => "끈 때 " + (m.SavedAt.Length >= 16 ? m.SavedAt[5..16] : m.SavedAt);
}

/// <summary>
/// 항해 묶음 한 벌 (save-?.bin — docs/OBSERVER.md §3.1): 머리 ('항해 묶음' · 묶음 판 · 세계 판 · 저장 모양 · 차례) + 마디 (이름 · 길이 · 압축한 바이트 — 모르는 마디는 건너뛴다 · 없는 마디는 빈 채로).
/// 작업 스레드는 날것 마디만 짓고 (<see cref="LiveShow.SaveBundle"/>) · 압축 · 쓰기는 다른 일꾼이 (<see cref="VoyageStore.Write"/>).
/// </summary>
public sealed class VoyageBundle
{
    public const string Magic = "항해 묶음";
    public const int Version = 1;
    public const string CardPart = "카드", WorldPart = "세계", RecordPart = "연출", HandsPart = "손 기록", ScreenPart = "화면";
    public VoyageMeta Meta = new();
    public byte[] World = Array.Empty<byte>();       // 마디 '세계' — Sim.Save 바이트
    public byte[]? Record, Hands, Screen;             // 마디 '연출' · '손 기록' · '화면' (없으면 null)
    /// <summary>모르는 마디 (뒤의 묶음 판이 더한 것 — 읽고 그대로 둔다).</summary>
    public readonly Dictionary<string, byte[]> Other = new();
    /// <summary>항해 폴더에 따로 쓰는 파일 (장면 금고 vault/ — V4): 이름 → 바이트 · 압축했나 · 날것 길이 (묶음 바이트에는 넣지 않는다 · 이미 있는 파일은 다시 쓰지 않는다).</summary>
    public readonly Dictionary<string, (byte[] data, bool packed, int raw)> Files = new();

    /// <summary>틀 · 압축 (다른 일꾼에서 — 화면 60프레임 · Brotli 빠르게).</summary>
    public byte[] ToBytes()
    {
        var ms = new MemoryStream(World.Length / 3 + (1 << 16));
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            bw.Write(Magic); bw.Write(Version); bw.Write(Meta.WorldVersion); bw.Write(Sim.SaveShape); bw.Write(Meta.Serial);
            var parts = new List<(string name, byte[] raw)> { (CardPart, System.Text.Encoding.UTF8.GetBytes(Meta.ToJson())), (WorldPart, World) };
            if (Record != null) parts.Add((RecordPart, Record));
            if (Hands != null) parts.Add((HandsPart, Hands));
            if (Screen != null) parts.Add((ScreenPart, Screen));
            foreach (var (k, v) in Other.OrderBy(x => x.Key, StringComparer.Ordinal)) parts.Add((k, v));
            bw.Write(parts.Count);
            foreach (var (name, raw) in parts)
            {
                var packed = Pack(raw);
                bw.Write(name); bw.Write(raw.Length); bw.Write(packed.Length); bw.Write(packed);
            }
        }
        return ms.ToArray();
    }

    internal static byte[] Pack(byte[] raw) => Pack(raw, raw.Length);

    internal static byte[] Pack(byte[] raw, int length)
    {
        var o = new MemoryStream(length / 3 + 64);
        using (var z = new BrotliStream(o, CompressionLevel.Fastest, leaveOpen: true)) z.Write(raw, 0, length);
        return o.ToArray();
    }

    internal static byte[] Unpack(byte[] packed, int rawLength)
    {
        var raw = new byte[rawLength];
        using var z = new BrotliStream(new MemoryStream(packed), CompressionMode.Decompress);
        z.ReadExactly(raw);
        return raw;
    }

    /// <summary>머리 (차례 · 세계 판) — 깨졌으면 null.</summary>
    public static (int bundleVersion, int worldVersion, ulong shape, long serial)? Header(Stream s)
    {
        try
        {
            using var br = new BinaryReader(s, System.Text.Encoding.UTF8, leaveOpen: true);
            if (br.ReadString() != Magic) return null;
            return (br.ReadInt32(), br.ReadInt32(), br.ReadUInt64(), br.ReadInt64());
        }
        catch (Exception e) when (e is IOException or EndOfStreamException or FormatException) { return null; }
    }

    /// <summary>읽는다 — 깨졌거나 세계 판이 다르면 null · why에 까닭 (화면 글).</summary>
    public static VoyageBundle? FromBytes(byte[] data, out string why)
    {
        try
        {
            using var ms = new MemoryStream(data, writable: false);
            var head = Header(ms);
            if (head == null) { why = "저장이 깨졌다"; return null; }
            if (head.Value.worldVersion != Sim.SaveVersion) { why = VoyageStore.VersionText(head.Value.worldVersion); return null; }
            using var br = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);
            var b = new VoyageBundle();
            int n = br.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                string name = br.ReadString(); int rawLen = br.ReadInt32(), packedLen = br.ReadInt32();
                var packed = br.ReadBytes(packedLen);
                if (packed.Length != packedLen) { why = "저장이 깨졌다"; return null; }
                var raw = Unpack(packed, rawLen);
                switch (name)
                {
                    case CardPart: b.Meta = VoyageMeta.FromJson(System.Text.Encoding.UTF8.GetString(raw)) ?? new VoyageMeta(); break;
                    case WorldPart: b.World = raw; break;
                    case RecordPart: b.Record = raw; break;
                    case HandsPart: b.Hands = raw; break;
                    case ScreenPart: b.Screen = raw; break;
                    default: b.Other[name] = raw; break;   // 모르는 마디 — 건너뛴다 (그대로 둔다)
                }
            }
            if (b.World.Length == 0) { why = "저장이 깨졌다"; return null; }
            why = "";
            return b;
        }
        catch (Exception e) when (e is IOException or EndOfStreamException or InvalidDataException or InvalidOperationException or FormatException or ArgumentException or OverflowException)
        {
            why = "저장이 깨졌다"; return null;
        }
    }
}

/// <summary>
/// 항해 묶음 곳간 (user://voyages — docs/OBSERVER.md §3.1 · 3.2): 항해마다 폴더 · 최근 셋을 돌려 쓴다 (임시 이름에 쓴 뒤 바꾼다 — 쓰다 꺼져도 앞의 둘은 멀쩡) · card.json은 가장 새것을 가리킨다.
/// 읽기는 가장 새것부터 (깨졌으면 그다음). 카드의 세계 판이 지금 판과 다르면 세계를 읽지 않는다.
/// </summary>
public sealed class VoyageStore
{
    public readonly string Root;
    public static readonly string[] SlotNames = { "a", "b", "c" };
    public const string CardFile = "card.json";
    public VoyageStore(string root) { Root = root; }
    /// <summary>저장 파일 (save-?.bin)을 통째로 읽은 수 (시험 — 판이 다르면 세계를 읽지 않는다).</summary>
    public int BinReads { get; private set; }

    public string FolderOf(VoyageMeta m) => Path.Combine(Root, m.Folder);
    private static string SlotPath(string folder, string slot) => Path.Combine(folder, $"save-{slot}.bin");

    /// <summary>항해들 (card.json이 있는 폴더 — 가장 최근에 저장한 것 먼저).</summary>
    public List<VoyageMeta> List()
    {
        var l = new List<(VoyageMeta m, DateTime at)>();
        if (!Directory.Exists(Root)) return new();
        foreach (var d in Directory.GetDirectories(Root))
        {
            string card = Path.Combine(d, CardFile);
            if (!File.Exists(card)) continue;
            try { if (VoyageMeta.FromJson(File.ReadAllText(card)) is { } m) l.Add((m, File.GetLastWriteTimeUtc(card))); }
            catch (IOException) { }
        }
        return l.OrderByDescending(x => x.at).ThenByDescending(x => x.m.SavedAt, StringComparer.Ordinal).Select(x => x.m).ToList();
    }

    public VoyageMeta? Latest() => List().FirstOrDefault();

    /// <summary>다음에 쓸 칸: 빈 칸 (깨진 칸) 먼저 · 아니면 가장 오래된 칸 (머리의 차례가 가장 작은 것).</summary>
    public string Rotate(VoyageMeta m)
    {
        string folder = FolderOf(m);
        string pick = SlotNames[0]; long oldest = long.MaxValue;
        foreach (var slot in SlotNames)
        {
            var serial = SerialOf(SlotPath(folder, slot));
            if (serial == null) return slot;
            if (serial.Value < oldest) { oldest = serial.Value; pick = slot; }
        }
        return pick;
    }

    private static long? SerialOf(string path)
    {
        if (!File.Exists(path)) return null;
        try { using var fs = File.OpenRead(path); return VoyageBundle.Header(fs)?.serial; }
        catch (IOException) { return null; }
    }

    /// <summary>항해 폴더는 다섯까지 (docs/OBSERVER.md §3.2).</summary>
    public const int KeepVoyages = 5;

    /// <summary>새 항해 폴더를 만들면 다섯을 넘나 — 넘으면 지울 가장 오래된 항해 (아니면 null).</summary>
    public VoyageMeta? WouldPrune(string newFolder)
    {
        var l = List();
        return l.Count < KeepVoyages || l.Any(m => m.Folder == newFolder) ? null : l[^1];
    }

    /// <summary>항해 하나를 지운다 (그 폴더 통째로).</summary>
    public void Delete(VoyageMeta m)
    {
        string d = FolderOf(m);
        try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>쓴다 (압축 · 쓰기 — 다른 일꾼에서 불러도 된다): 가장 오래된 칸에 임시 이름으로 쓰고 바꾼 뒤 카드가 그 칸을 가리킨다 · 새 항해 폴더가 다섯을 넘기면 가장 오래된 항해를 지운다 (번호 창이 미리 알린다).</summary>
    public VoyageMeta Write(VoyageBundle b)
    {
        string folder = FolderOf(b.Meta);
        if (!Directory.Exists(folder)) for (int k = 0; k < 8 && WouldPrune(b.Meta.Folder) is { } old; k++) Delete(old);
        Directory.CreateDirectory(folder);
        long serial = 0;
        foreach (var slot in SlotNames) serial = Math.Max(serial, SerialOf(SlotPath(folder, slot)) ?? 0);
        b.Meta.Slot = Rotate(b.Meta); b.Meta.Serial = serial + 1;
        b.Meta.WorldVersion = Sim.SaveVersion; b.Meta.BundleVersion = VoyageBundle.Version;
        WriteVault(folder, b);   // 금고 파일이 먼저 (저장 칸이 없는 파일을 가리키지 않게)
        WriteSafe(SlotPath(folder, b.Meta.Slot), b.ToBytes());
        WriteSafe(Path.Combine(folder, CardFile), System.Text.Encoding.UTF8.GetBytes(b.Meta.ToJson()));
        return b.Meta;
    }

    public const string VaultFolder = "vault";

    /// <summary>장면 금고 파일 (vault/k-틱.bin · d-날.bin — 압축 바이트): 없는 것만 쓰고 · 이번 목록에 없는 옛 파일은 지운다 (앞선 저장 칸이 가리키던 칸은 이어 볼 때 건너뛴다).</summary>
    private static void WriteVault(string folder, VoyageBundle b)
    {
        string dir = Path.Combine(folder, VaultFolder);
        if (b.Files.Count == 0 && !Directory.Exists(dir)) return;
        Directory.CreateDirectory(dir);
        foreach (var (name, (data, packed, _)) in b.Files)
        {
            string path = Path.Combine(dir, name);
            if (!File.Exists(path)) WriteSafe(path, packed ? data : VoyageBundle.Pack(data));
        }
        foreach (var f in Directory.GetFiles(dir, "*.bin"))
            if (!b.Files.ContainsKey(Path.GetFileName(f))) try { File.Delete(f); } catch (IOException) { }
    }

    /// <summary>그 항해의 금고 파일 (압축 바이트 — 없거나 못 읽으면 null).</summary>
    public byte[]? VaultFile(VoyageMeta m, string name)
    {
        if (name.Contains('/') || name.Contains('\\') || name.Contains("..")) return null;
        string path = Path.Combine(FolderOf(m), VaultFolder, name);
        try { return File.Exists(path) ? File.ReadAllBytes(path) : null; } catch (IOException) { return null; }
    }

    /// <summary>임시 이름에 쓴 뒤 바꾼다 (쓰다 꺼져도 옛 파일은 멀쩡 — 남은 임시 파일은 읽지 않는다).</summary>
    private static void WriteSafe(string path, byte[] bytes)
    {
        string tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None)) { fs.Write(bytes); fs.Flush(true); }
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>저장 셋을 가장 새것부터 읽는다 (깨진 칸은 bundle null · why에 까닭).</summary>
    public List<(string slot, VoyageBundle? bundle, string why)> Read(VoyageMeta m) => ReadEach(m).ToList();

    /// <summary>가장 새것부터 하나씩 (이어 보기는 처음 읽힌 것에서 멈춘다).</summary>
    public IEnumerable<(string slot, VoyageBundle? bundle, string why)> ReadEach(VoyageMeta m)
    {
        string folder = FolderOf(m);
        var slots = SlotNames.Select(s => (s, serial: SerialOf(SlotPath(folder, s)))).Where(x => x.serial != null).OrderByDescending(x => x.serial).ToList();
        foreach (var (slot, _) in slots)
        {
            byte[] data;
            try { data = File.ReadAllBytes(SlotPath(folder, slot)); BinReads++; }
            catch (IOException) { continue; }
            var b = VoyageBundle.FromBytes(data, out string why);
            yield return (slot, b, why);
        }
    }

    /// <summary>이어 볼 수 있나 — "" 되면 · 아니면 화면 글 (카드만 읽는다 · 세계는 읽지 않는다).</summary>
    public static string Why(VoyageMeta m) => m.WorldVersion == Sim.SaveVersion ? "" : VersionText(m.WorldVersion);

    public static string VersionText(int worldVersion) => worldVersion < Sim.SaveVersion
        ? "이 항해는 앞선 판에서 남겼다 — 지금 판에서는 이어 볼 수 없다"
        : "이 항해는 새 판에서 남겼다 — 지금 판에서는 이어 볼 수 없다";

    public const string BrokenText = "이 항해의 저장이 모두 깨졌다 — 이어 볼 수 없다";
}

/// <summary>
/// 이어 보기 (docs/OBSERVER.md §3.6 · V1): 항해 손 기록 · 묶음 짓기 · 묶음에서 되살리기. 세계는 그대로 (세계 찍기를 그대로 쓴다 — 화면만).
/// </summary>
public sealed partial class LiveShow
{
    /// <summary>항해의 모든 손 (한 손 장부 256을 넘은 옛 손까지 — 다시보기 · 이어 보기가 먹인다 · 손마다 35바이트).</summary>
    public IReadOnlyList<HandOrder> HandLog => _handLog;
    private readonly List<HandOrder> _handLog = new();
    private int _handSeq;   // 손 기록에 받은 마지막 차례

    /// <summary>이어 볼 때 옮겨 오지 못한 것 (화면 한 줄씩).</summary>
    public readonly List<string> ResumeNotes = new();

    /// <summary>항해 번호 (= 세계 시드).</summary>
    public long Voyage => (long)Sim.Setup.Seed;

    /// <summary>'지난번까지' 카드 (이어 본 첫 장 — 멈춘 채 · 스페이스나 [계속]으로 간다): 줄들 (빈 배열이면 없다).</summary>
    public string[] ResumeCard = Array.Empty<string>();

    /// <summary>이어 본 첫 장: 멈추고 '지난번까지' 카드 — 배 · 번호 · 날 · 마지막 장 셋 · 산 이 · 옮겨 오지 못한 것.</summary>
    public void ShowResumeCard()
    {
        var l = new List<string> { $"지난번까지 — {ShipName} · 항해 {Voyage} · {Sim.Tick / Time.TicksPerDay + 1}일째" };
        var days = Dir.Chapters.Where(c => !c.Week).TakeLast(3).Select(c => c.Title).ToList();
        if (days.Count == 0) days.Add("아직 지나간 날이 없다");
        l.AddRange(days);
        var mind = Sim.Find<Mind>(); var life = Sim.Find<Life>();
        int all = 0, alive = 0;
        if (mind != null && life != null) foreach (var a in mind.Agents) if (a.IsPerson) { all++; if (a.C.Body >= 0 && life.Bodies[a.C.Body].Alive != 0) alive++; }
        l.Add($"산 이 — {all}명 가운데 {alive}명");
        l.AddRange(ResumeNotes);
        ResumeCard = l.ToArray();
        Paused = true;
    }

    /// <summary>[계속] · 스페이스: 카드를 닫고 다시 간다.</summary>
    public void ResumeGo() { ResumeCard = Array.Empty<string>(); Paused = false; }

    /// <summary>화면 마디 판: 2 — 끝난 항해 (결산 · V3 — 판 1도 읽는다).</summary>
    public const int ScreenVersion = 2;

    /// <summary>산 세계가 한 틱 돈 뒤: 한 손 장부의 새 손 (받은 차례가 더 큰 것)을 항해 손 기록에 옮긴다.</summary>
    private void LogHands()
    {
        if (Sim.Find<Hand>() is not { } h || h.Done.Count == 0 || h.Done[^1].Seq <= _handSeq) return;
        int i = h.Done.Count;
        while (i > 0 && h.Done[i - 1].Seq > _handSeq) i--;
        for (; i < h.Done.Count; i++) _handLog.Add(h.Done[i]);
        _handSeq = h.Done[^1].Seq;
    }

    /// <summary>항해 묶음의 날것 마디를 짓는다 (작업 스레드 · 틱 사이 — 주컴퓨터 일꾼이 셈 중이면 찍기가 끝을 기다려 결과를 함께 담는다). 압축 · 쓰기는 <see cref="VoyageStore.Write"/> (다른 일꾼).</summary>
    public VoyageBundle SaveBundle(SaveKind kind)
    {
        LogHands();
        var b = new VoyageBundle { World = Sim.Save() };
        var rw = new StateWriter(1 << 16); Dir.SaveRecord(rw); b.Record = rw.ToArray();
        var hw = new StateWriter(1 << 12); hw.Tag("손 기록"); hw.List(_handLog); b.Hands = hw.ToArray();
        b.Screen = SaveScreen();
        if (Vault.Scenes.Count + Vault.DayWorlds.Count > 0) b.Other[SceneVault.Part] = Vault.SaveIndex(Dir.Highlights, b.Files);   // 장면 금고 목록 (바이트는 vault/ 파일 — V4)
        var mind = Sim.Find<Mind>(); var life = Sim.Find<Life>();
        int aboard = 0, alive = 0, robots = 0;
        if (mind != null)
            foreach (var a in mind.Agents)
            {
                if (!a.IsPerson) { robots++; continue; }
                aboard++;
                if (life != null && a.C.Body >= 0 && a.C.Body < life.Bodies.Count && life.Bodies[a.C.Body].Alive != 0) alive++;
            }
        b.Meta = new VoyageMeta
        {
            Ship = Sim.Setup.Layout.Key, ShipName = ShipName, Voyage = (long)Sim.Setup.Seed, Route = Sim.Find<Outside>()?.Route ?? 0,
            Day = (int)(Sim.Tick / Time.TicksPerDay) + 1, Tick = Sim.Tick, Alive = alive, Aboard = aboard, Robots = robots,
            Yesterday = Dir.Chapters.LastOrDefault(c => !c.Week)?.Title ?? "", End = EndText(Ended), WorldVersion = Sim.SaveVersion, BundleVersion = VoyageBundle.Version,
            SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), Kind = kind, People = aboard, RobotCount = robots,
        };
        return b;
    }

    /// <summary>화면 마디: 고른 빠르기 · 하루 목표 · 따라 보기 하루 · 주컴퓨터 말 기록 512 · 판단 일지 32칸 (찍지 않는 화면 고리 — 이어 본 뒤에도 비지 않게).</summary>
    private byte[] SaveScreen()
    {
        var w = new StateWriter(1 << 16);
        w.Tag("화면"); w.I32(ScreenVersion);
        w.F64(SpeedOverride); w.F64(DayMinutes); w.F64(FollowDayMinutes);
        var notes = Sim.Find<ShipMind>()?.Notes;
        w.I32(notes?.Count ?? 0); w.I64(notes?.Added ?? 0);
        for (int k = (notes?.Count ?? 0) - 1; k >= 0; k--)
        {
            ref var n = ref notes!.Newest(k);
            w.I64(n.Tick); w.Byte((byte)n.Kind); w.I32(n.Robot); w.I32(n.Incident); w.Byte(n.Reason); w.Str(n.Text);
        }
        var mind = Sim.Find<Mind>();
        w.I32(mind?.Agents.Count ?? 0);
        if (mind != null)
            foreach (var a in mind.Agents)
            {
                var jr = a.Judged;
                w.I32(jr?.Count ?? 0); w.I64(jr?.Added ?? 0);
                for (int k = (jr?.Count ?? 0) - 1; k >= 0; k--)
                {
                    ref var j = ref jr!.Newest(k);
                    w.I64(j.Tick); w.Byte((byte)j.Goal); w.I32(j.Target); w.I32(j.Cell); w.Byte(j.Method); w.Byte(j.Held); w.Byte((byte)j.Out); w.Byte((byte)j.Why); w.I32(j.By); w.Byte(j.Reason); w.Str(j.Note);
                }
            }
        w.Byte((byte)Ended);
        w.Tag("화면 끝");
        return w.ToArray();
    }

    private void LoadScreen(byte[] data)
    {
        var r = new StateReader(data);
        r.Tag("화면");
        int ver = r.I32();
        if (ver is < 1 or > ScreenVersion) { ResumeNotes.Add("화면 설정은 옮겨 오지 못했다"); return; }
        SpeedOverride = r.F64(); DayMinutes = r.F64(); FollowDayMinutes = r.F64();
        var sm = Sim.Find<ShipMind>();
        int nn = r.I32(); long nAdded = r.I64();
        if (nn > 0 && sm != null)
        {
            var ring = sm.Notes = new Ring<ComputerNote>(512);
            for (int i = 0; i < nn; i++) ring.Add(new ComputerNote { Tick = r.I64(), Kind = (NoteKind)r.Byte(), Robot = r.I32(), Incident = r.I32(), Reason = r.Byte(), Text = r.Str() ?? "" });
            ring.Rebase(nAdded);
        }
        else for (int i = 0; i < nn; i++) { r.I64(); r.Byte(); r.I32(); r.I32(); r.Byte(); r.Str(); }
        var mind = Sim.Find<Mind>();
        int na = r.I32();
        for (int ai = 0; ai < na; ai++)
        {
            int nj = r.I32(); long jAdded = r.I64();
            Ring<Judgment>? ring = nj > 0 && mind != null && ai < mind.Agents.Count ? mind.Agents[ai].Judged = new Ring<Judgment>(Mind.JudgedKeep) : null;
            for (int i = 0; i < nj; i++)
            {
                var j = new Judgment { Tick = r.I64(), Goal = (GoalKind)r.Byte(), Target = r.I32(), Cell = r.I32(), Method = r.Byte(), Held = r.Byte(), Out = (JudgeOut)r.Byte(), Why = (SnagKind)r.Byte(), By = r.I32(), Reason = r.Byte(), Note = r.Str() };
                ring?.Add(j);
            }
            ring?.Rebase(jAdded);
        }
        if (ver >= 2) Ended = (VoyageEnd)r.Byte();
        r.Tag("화면 끝");
    }

    /// <summary>곳간의 가장 새 저장에서 이어 본다 (깨졌으면 그다음) — 이어 볼 수 없으면 null · why에 화면 글. 카드의 판이 다르면 세계를 읽지 않는다 (예외로 멈추지 않는다).</summary>
    public static LiveShow? Resume(VoyageStore store, VoyageMeta meta, out string why)
    {
        why = VoyageStore.Why(meta);
        if (why.Length > 0) return null;
        string last = "";
        foreach (var (_, b, w) in store.ReadEach(meta))
        {
            if (b == null) { last = w; continue; }
            try { return Resume(b, null, name => store.VaultFile(meta, name)); }
            catch (Exception e) when (e is InvalidDataException or IOException or EndOfStreamException or ArgumentException or IndexOutOfRangeException or InvalidOperationException) { last = "저장이 깨졌다"; }
        }
        why = last.Contains("판") ? last : VoyageStore.BrokenText;
        return null;
    }

    /// <summary>묶음에서 이어 본다 (setup: 세계 준비 — 없으면 배 열쇠 · 번호로 짓는다): 세계 → 새 화면 → 연출 기록 · 손 기록 · 화면 마디. 빠진 마디는 한 줄씩 <see cref="ResumeNotes"/>에.</summary>
    public static LiveShow Resume(VoyageBundle b, SimSetup? setup = null, Func<string, byte[]?>? files = null)
    {
        var m = b.Meta;
        setup ??= ShipWorld.Setup(Layout.Ship(m.Ship, Tables.Default), (ulong)m.Voyage);
        var sim = new Sim(setup);
        sim.Load(b.World);
        string name = m.ShipName.Length > 0 ? m.ShipName : sim.Layout.Name;
        var show = new LiveShow(sim, name);
        bool record = false;
        if (b.Record != null)
            try { record = show.Dir.LoadRecord(new StateReader(b.Record), sim); }
            catch (Exception e) when (e is InvalidDataException or EndOfStreamException or ArgumentException or IndexOutOfRangeException) { show = new LiveShow(sim, name); }   // 반쯤 읽은 연출은 버리고 새로
        if (!record) show.ResumeNotes.Add("지난 장면 기록은 옮겨 오지 못했다");
        if (b.Hands != null)
        {
            var r = new StateReader(b.Hands); r.Tag("손 기록");
            show._handLog.AddRange(r.List<HandOrder>());
            show._handSeq = show._handLog.Count > 0 ? show._handLog[^1].Seq : 0;
        }
        else show.ResumeNotes.Add("함장의 손 기록은 옮겨 오지 못했다");
        show.LogHands();   // 세계의 한 손 장부에만 있는 손 (손 기록이 없던 묶음)
        if (b.Screen != null) show.LoadScreen(b.Screen);
        else show.ResumeNotes.Add("화면 설정은 옮겨 오지 못했다");
        if (b.Other.TryGetValue(SceneVault.Part, out var vault))
        {
            // 장면 금고 (V4): 곳간의 vault/ 파일 · 아니면 묶음이 쥔 파일 (시험 · 곧바로 이어 보기)
            files ??= name => b.Files.TryGetValue(name, out var f) ? (f.packed ? f.data : VoyageBundle.Pack(f.data)) : null;
            if (show.Vault.LoadIndex(vault, show.Dir.Highlights, files) < 0) show.ResumeNotes.Add("지난 장면 금고는 옮겨 오지 못했다");
        }
        show._lastHighlights = show.Dir.Highlights.Count; show._lastTitle = show._lastHighlights > 0 ? show.Dir.Highlights[^1].Title : "";   // 이어 본 첫 장에 지난 하이라이트를 띄우지 않는다
        show._chaptersSeen = show.Dir.Chapters.Count;   // 지난 날의 장 카드도
        return show;
    }
}
