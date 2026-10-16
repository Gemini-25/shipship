using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ShipSim.Core;

/// <summary>
/// 시험의 "첫날 배"를 한 번만 돌린다: 같은 시드 · 같은 배 · 같은 전역 스위치로 하루를 돌린 배는 늘 같으니,
/// 처음 한 번 돌려 상태를 통째로 찍어 두고(StateSave) 다음부터는 되살린다 (한빛호 하루 약 15초 → 되살리기 0.2초).
/// · 열쇠: 이름 · 시드 · 배 · 세계 밖 전역 스위치(켜고 끄는 Off/On · 수치) · 코드 판(어셈블리 지문)
/// · 처음 돌린 배도 찍은 것을 되살려 돌려준다 — 언제나 되살린 배로 시험하니 켜고 끈 결과를 견줄 수 있다
/// · 프로세스 사이에서도 나눈다 (임시 폴더 · SHIPSIM_WARMDIR) — 코드가 바뀌면 지문이 바뀌어 다시 돌린다
/// · 기본으로 켠다 (끄려면 SHIPSIM_WARM=0) — 시험 137개를 켜고 끈 출력을 견줘 시간 줄만 빼고 글자까지 같은 것을 확인했다
///   (처음 견줄 땐 두 개가 달랐다: 전역 속성 스위치가 열쇠에서 빠져, 스위치를 켠 채 만든 첫날 배가 보통 첫날 배 자리에 저장되었다)
/// </summary>
public static class WarmCache
{
    public static readonly bool On = Environment.GetEnvironmentVariable("SHIPSIM_WARM") != "0";
    private static readonly Dictionary<string, byte[]> Mem = new();
    private static readonly string? Dir = On ? Environment.GetEnvironmentVariable("SHIPSIM_WARMDIR") ?? Path.Combine(Path.GetTempPath(), "shipsim-warm") : null;
    private static FieldInfo[]? _switches;
    // 스위치가 아니라 생각 한 번 사이에 쓰고 버리는 칸 (하루 사이 바뀐다 — --warmbench가 찾는다)
    private static readonly HashSet<string> Scratch = new() { "EatActivity.Arrive", "EatActivity.ArriveFor" };
    public static int Hits, DiskHits, Misses;
    public static double BuildMs, ReadMs;

    public static World Get(string label, int seed, string ship, Func<World> make)
    {
        if (!On) return make();
        string key = Hash($"{label}|{seed}|{ship}|{Switches()}|{typeof(World).Assembly.ManifestModule.ModuleVersionId}");
        var sw = Stopwatch.StartNew();
        if (Mem.TryGetValue(key, out var bytes) || TryDisk(key, out bytes))
        {
            try
            {
                var w = StateSave.Read(bytes!);
                ReadMs += sw.Elapsed.TotalMilliseconds;
                Hits++;
                return w;
            }
            catch (Exception) { Mem.Remove(key); } // 판이 맞지 않으면 다시 돌린다
        }
        var fresh = make();
        bytes = StateSave.Write(fresh);
        Mem[key] = bytes;
        SaveDisk(key, bytes);
        var copy = StateSave.Read(bytes);
        BuildMs += sw.Elapsed.TotalMilliseconds;
        Misses++;
        return copy;
    }

    /// <summary>세계 밖에 있는 전역 스위치 · 수치 (시험이 켜고 끄는 것) — 하나라도 다르면 다른 첫날이다.</summary>
    /// <summary>진단: 하루를 돌리는 동안 바뀐 전역 칸 (열쇠에서 빼야 할 것).</summary>
    public static IEnumerable<string> Drift(Action run)
    {
        Switches();
        var before = _switches!.Select(f => f.GetValue(null)?.ToString()).ToArray();
        run();
        for (int i = 0; i < _switches!.Length; i++)
            if (_switches[i].GetValue(null)?.ToString() != before[i]) yield return $"{_switches[i].DeclaringType!.Name}.{Name(_switches[i])}: {before[i]} → {_switches[i].GetValue(null)}";
    }

    private static string Switches()
    {
        _switches ??= typeof(World).Assembly.GetTypes()
            .Where(t => t != typeof(Program) && t != typeof(WarmCache))
            .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(f => !f.IsInitOnly && !f.IsLiteral && !Name(f).StartsWith('_') && !Name(f).Contains('<')
                        && (f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType == typeof(string))
                        && !Name(f).EndsWith("Ticks") && !Name(f).Contains("Stopwatch") && !Name(f).EndsWith("Count") && !Name(f).EndsWith("Version") && !Name(f).StartsWith("Last"))
            .Where(f => !Scratch.Contains($"{f.DeclaringType!.Name}.{Name(f)}"))
            .OrderBy(f => f.DeclaringType!.FullName).ThenBy(f => f.Name).ToArray();
        var sb = new StringBuilder();
        foreach (var f in _switches) sb.Append(f.DeclaringType!.Name).Append('.').Append(Name(f)).Append('=').Append(f.GetValue(null)).Append(';');
        sb.Append(Tuning.Write());
        return sb.ToString();
    }

    /// <summary>필드 이름 — 전역 자동 속성(RoomPlanSystem.ThinkOff { get; set; } 같은 스위치)은 속성 이름으로.
    /// 예전엔 속성 뒤 필드(&lt;ThinkOff&gt;k__BackingField)를 빼서, 스위치를 켠 채 부른 첫날 배가 보통 첫날 배 열쇠로 저장되었다.</summary>
    private static string Name(FieldInfo f) => f.Name.StartsWith('<') && f.Name.EndsWith(">k__BackingField") ? f.Name[1..f.Name.IndexOf('>')] : f.Name;

    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..24];

    private static bool TryDisk(string key, out byte[]? bytes)
    {
        bytes = null;
        if (Dir == null) return false;
        try
        {
            var path = Path.Combine(Dir, key + ".snap");
            if (!File.Exists(path)) return false;
            bytes = File.ReadAllBytes(path);
            DiskHits++;
            return true;
        }
        catch (IOException) { return false; }
    }

    private static bool _pruned;

    private static void SaveDisk(string key, byte[] bytes)
    {
        if (Dir == null) return;
        try
        {
            Directory.CreateDirectory(Dir);
            if (!_pruned)
            {
                // 기본으로 켜니 임시 폴더에 지난 코드 판의 첫날 배가 쌓인다 — 이틀 넘게 안 쓴 것은 지운다 (지금 판은 열쇠가 달라 어차피 안 쓴다)
                _pruned = true;
                foreach (var f in Directory.EnumerateFiles(Dir, "*.snap"))
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > TimeSpan.FromDays(2)) File.Delete(f);
            }
            var tmp = Path.Combine(Dir, $"{key}.{Environment.ProcessId}.tmp");
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, Path.Combine(Dir, key + ".snap"), overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>SHIPSIM_WARM_REPORT=1: 끝에 몇 번 되살렸는지 (시험 출력은 그대로 두려고 오류 출력으로).</summary>
    public static void Report()
    {
        if (Environment.GetEnvironmentVariable("SHIPSIM_WARM_REPORT") != "1") return;
        Console.Error.WriteLine($"[첫날 배] 되살림 {Hits}(파일 {DiskHits}) · 새로 돌림 {Misses} · 돌린 시간 {BuildMs / 1000:0.0}초 · 되살린 시간 {ReadMs / 1000:0.0}초");
    }
}
