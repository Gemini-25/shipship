using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShipView;

/// <summary>큰 일 멈춤 갈래 (docs/OBSERVER.md §6.3): 죽음 · 쓰러짐 · 외판 구멍 (기본 켬) · 불 · ☆ 눈여겨보는 이의 일 (기본 끔).</summary>
public enum StopKind : byte { Died, Collapsed, Breach, Fire, Star }

/// <summary>창이 뒤로 가면 (docs/OBSERVER.md §6.4): 그대로 · 느리게 (하루 24분) · 멈춤 (기본).</summary>
public enum AwayMode : byte { Keep, Slow, Pause }

/// <summary>
/// 함장마다의 보기 설정 (docs/OBSERVER.md §3.1 profile.json · §6.3 ~ 6.6 — V7 · 항해를 넘는다 · 세계 밖): 큰 일 멈춤 갈래 · 창이 뒤로 가면 · 본 길잡이.
/// 띄우는 쪽 (게임)이 항해 곳간 곁 profile.json에서 읽어 항해마다 넘긴다 · 바뀌면 <see cref="Dirty"/> — 작업 스레드가 틱 사이에 쓴다 (임시 이름에 쓴 뒤 바꾼다).
/// 모르는 칸은 건너뛰고 · 빠진 칸은 기본값 · 깨진 파일은 새 설정으로 (예외로 멈추지 않는다).
/// </summary>
public sealed class ViewPrefs
{
    public const string FileName = "profile.json";
    public const int Version = 1;
    public static readonly string[] StopNames = { "죽음", "쓰러짐", "외판 구멍", "불", "☆ 눈여겨보는 이의 일" };
    public static readonly string[] AwayNames = { "그대로 간다", "느리게 (하루 24분)", "멈춘다" };
    public static readonly bool[] StopDefaults = { true, true, true, false, false };

    public int V { get; set; } = Version;
    /// <summary>큰 일 멈춤 갈래마다 켬 (<see cref="StopKind"/> 차례).</summary>
    public bool[] Stops { get; set; } = (bool[])StopDefaults.Clone();
    /// <summary>창이 뒤로 가면 (<see cref="AwayMode"/>).</summary>
    public int Away { get; set; } = (int)AwayMode.Pause;
    /// <summary>본 길잡이 (열쇠 — 함장마다 한 번).</summary>
    public List<string> HintsSeen { get; set; } = new();
    /// <summary>바뀌었다 (띄우는 쪽이 쓰고 끈다).</summary>
    [JsonIgnore] public bool Dirty;

    public bool Stop(StopKind k) => (int)k < Stops.Length && Stops[(int)k];
    public AwayMode AwayIs => (AwayMode)Math.Clamp(Away, 0, 2);

    public void SetStop(StopKind k, bool on)
    {
        if ((int)k >= Stops.Length) Stops = StopDefaults.Select((d, i) => i < Stops.Length ? Stops[i] : d).ToArray();
        if (Stops[(int)k] == on) return;
        Stops[(int)k] = on; Dirty = true;
    }

    public void SetAway(AwayMode m) { if (Away == (int)m) return; Away = (int)m; Dirty = true; }

    /// <summary>길잡이를 처음 보나 (보면 적어 둔다).</summary>
    public bool FirstHint(string key)
    {
        if (HintsSeen.Contains(key)) return false;
        HintsSeen.Add(key); Dirty = true;
        return true;
    }

    /// <summary>사람이 없는 띄우기 (실험실 · 관찰 장면 · 확인용 띄우기 — [계속]을 누를 이가 없다): 큰 일에 멈추지 않는다 · 길잡이는 그대로 (함장의 설정은 띄우는 쪽이 넘긴다).</summary>
    public static ViewPrefs Unattended() => new() { Stops = new bool[StopDefaults.Length] };

    public string ToJson() => JsonSerializer.Serialize(this, VoyageMeta.Json);

    public static ViewPrefs FromJson(string s)
    {
        ViewPrefs? p = null;
        try { p = JsonSerializer.Deserialize<ViewPrefs>(s, VoyageMeta.Json); } catch (JsonException) { }
        p ??= new ViewPrefs();
        if (p.Stops == null || p.Stops.Length < StopDefaults.Length) p.Stops = StopDefaults.Select((d, i) => p.Stops != null && i < p.Stops.Length ? p.Stops[i] : d).ToArray();   // 앞 판 설정 · 빠진 갈래는 기본
        p.HintsSeen ??= new List<string>();
        p.Away = Math.Clamp(p.Away, 0, 2);
        return p;
    }

    /// <summary>읽기 (없거나 깨졌으면 새 설정).</summary>
    public static ViewPrefs Load(string path)
    {
        try { return File.Exists(path) ? FromJson(File.ReadAllText(path)) : new ViewPrefs(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new ViewPrefs(); }
    }

    /// <summary>쓰기 (임시 이름에 쓴 뒤 바꾼다 — 쓰다 꺼져도 앞 설정은 멀쩡) · 못 쓰면 다음에 다시.</summary>
    public bool Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, ToJson());
            File.Move(tmp, path, true);
            Dirty = false;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}
