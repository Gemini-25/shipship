using System;
using System.IO;
using System.Linq;
using System.Text;

namespace ShipSim.TexGen;

// v16.5 텍스처 생성기 실행:
//   dotnet run --project tools/texgen -c Release            → game/assets/textures/look_*.png (+ Godot .import)
//   dotnet run --project tools/texgen -c Release -- --check → 파일이 생성기와 같은지만 본다 (다르면 1)
//   ... -- --preview <파일.png>                           → 바닥 · 겹치기 · 흔적 · 빛을 한 장에 모아 본다 (눈으로 확인)
public static class Program
{
    public static int Main(string[] args)
    {
        string? outDir = null, preview = null;
        bool check = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--out" && i + 1 < args.Length) outDir = args[++i];
            else if (args[i] == "--check") check = true;
            else if (args[i] == "--preview" && i + 1 < args.Length) preview = args[++i];
        }
        outDir ??= FindTextures();
        if (outDir == null) { Console.Error.WriteLine("game/assets/textures 를 찾지 못했다 (--out 으로 정할 것)"); return 2; }
        var all = Gen.All();
        int bad = 0;
        foreach (var (name, pix) in all)
        {
            var png = Png.Encode(pix.W, pix.H, pix.Rgba8());
            var path = Path.Combine(outDir, name + ".png");
            ulong h = Png.Hash(pix.Rgba8());
            bool same = File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(png);
            if (check)
            {
                if (!same) { bad++; Console.WriteLine($"✘ {name} — 파일이 생성기와 다르다"); }
                else Console.WriteLine($"✔ {name} {pix.W}×{pix.H} {h:x16}");
                continue;
            }
            if (!same) File.WriteAllBytes(path, png);
            var imp = ImportFile(name, name.StartsWith("look_ov_", StringComparison.Ordinal));
            var impPath = path + ".import";
            if (!File.Exists(impPath) || File.ReadAllText(impPath) != imp) File.WriteAllText(impPath, imp);
            Console.WriteLine($"{(same ? "=" : "+")} {name} {pix.W}×{pix.H} {h:x16} ({png.Length / 1024}KB)");
        }
        if (preview != null) { Preview.Write(preview, all.ToDictionary(a => a.name, a => a.pix)); Console.WriteLine($"미리보기 → {preview}"); }
        return bad == 0 ? 0 : 1;
    }

    private static string? FindTextures()
    {
        var d = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
        {
            var c = Path.Combine(d.FullName, "game", "assets", "textures");
            if (Directory.Exists(c)) return c;
        }
        return null;
    }

    /// <summary>Godot 4 가져오기 파일 (uid 는 이름에서 정한다 — 같은 이름 같은 uid).</summary>
    public static string ImportFile(string name, bool overlay)
    {
        string res = $"res://assets/textures/{name}.png";
        string md5 = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(res))).ToLowerInvariant();
        ulong id = Png.Hash(Encoding.UTF8.GetBytes("shipsim-look:" + name)) & 0x7FFF_FFFF_FFFF_FFFFUL;
        var uid = new StringBuilder();
        while (id != 0) { uint c = (uint)(id % 36); uid.Insert(0, c < 26 ? (char)('a' + c) : (char)('0' + c - 26)); id /= 36; }
        string dest = $"res://.godot/imported/{name}.png-{md5}.ctex";
        return $"[remap]\n\nimporter=\"texture\"\ntype=\"CompressedTexture2D\"\nuid=\"uid://{uid}\"\npath=\"{dest}\"\nmetadata={{\n\"vram_texture\": false\n}}\n\n" +
               $"[deps]\n\nsource_file=\"{res}\"\ndest_files=[\"{dest}\"]\n\n" +
               "[params]\n\ncompress/mode=0\ncompress/high_quality=false\ncompress/lossy_quality=0.7\ncompress/uastc_level=0\ncompress/rdo_quality_loss=0.0\n" +
               "compress/hdr_compression=1\ncompress/normal_map=0\ncompress/channel_pack=0\nmipmaps/generate=true\nmipmaps/limit=-1\nroughness/mode=0\nroughness/src_normal=\"\"\n" +
               "process/channel_remap/red=0\nprocess/channel_remap/green=1\nprocess/channel_remap/blue=2\nprocess/channel_remap/alpha=3\n" +
               $"process/fix_alpha_border={(overlay ? "false" : "true")}\nprocess/premult_alpha=false\nprocess/normal_map_invert_y=false\nprocess/hdr_as_srgb=false\n" +
               "process/hdr_clamp_exposure=false\nprocess/size_limit=0\ndetect_3d/compress_to=1\n";
    }
}
