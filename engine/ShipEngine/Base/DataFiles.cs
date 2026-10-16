using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShipEngine;

/// <summary>
/// 데이터 표: engine/data/**.json 이 코어에 내장 자원으로 들어 있다 (실행 중 파일 경로가 필요 없다 — Godot · 실험실 · 시험 모두 같다).
/// 자원 이름은 "ShipEngine.Data." + 하위 폴더 + 파일 이름.
/// </summary>
public static class DataFiles
{
    public static readonly JsonSerializerOptions Json = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };

    private const string Prefix = "ShipEngine.Data.";

    private static string Norm(string s) => s.Replace('\\', '/');

    /// <summary>내장된 표 이름들 (예: "substances.json", "ships/mirinae.json").</summary>
    public static IEnumerable<string> Names(string folder = "")
    {
        folder = Norm(folder);
        foreach (var n in typeof(DataFiles).Assembly.GetManifestResourceNames())
        {
            if (!n.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            string rel = Norm(n[Prefix.Length..]);
            if (folder.Length == 0 || rel.StartsWith(folder, StringComparison.Ordinal)) yield return rel;
        }
    }

    public static bool Has(string name) => Names().Contains(Norm(name));

    public static string Text(string name)
    {
        name = Norm(name);
        var asm = typeof(DataFiles).Assembly;
        string? res = asm.GetManifestResourceNames().FirstOrDefault(n => n.StartsWith(Prefix, StringComparison.Ordinal) && Norm(n[Prefix.Length..]) == name)
                      ?? throw new FileNotFoundException($"데이터 표가 없다: {name}");
        using var s = asm.GetManifestResourceStream(res)!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public static T Load<T>(string name)
        => JsonSerializer.Deserialize<T>(Text(name), Json) ?? throw new InvalidDataException($"데이터 표를 읽지 못했다: {name}");
}
