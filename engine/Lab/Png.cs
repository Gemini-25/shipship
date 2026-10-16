using System.IO.Compression;

namespace Lab;

/// <summary>아주 작은 PNG 쓰개 (관찰 장면 그림용 — 외부 라이브러리 없이).</summary>
public static class Png
{
    public static void Write(string path, int w, int h, uint[] rgba)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = File.Create(path);
        fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        Be(ihdr, 0, (uint)w); Be(ihdr, 4, (uint)h);
        ihdr[8] = 8; ihdr[9] = 6; // 8비트 RGBA
        Chunk(fs, "IHDR", ihdr);
        using var raw = new MemoryStream();
        for (int y = 0; y < h; y++)
        {
            raw.WriteByte(0);
            for (int x = 0; x < w; x++)
            {
                uint c = rgba[y * w + x];
                raw.WriteByte((byte)(c >> 24)); raw.WriteByte((byte)(c >> 16)); raw.WriteByte((byte)(c >> 8)); raw.WriteByte((byte)c);
            }
        }
        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true)) raw.WriteTo(zs);
        Chunk(fs, "IDAT", z.ToArray());
        Chunk(fs, "IEND", Array.Empty<byte>());
    }

    private static void Be(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; Be(len, 0, (uint)data.Length); s.Write(len);
        var td = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) td[i] = (byte)type[i];
        Array.Copy(data, 0, td, 4, data.Length);
        s.Write(td);
        var crc = new byte[4]; Be(crc, 0, Crc(td)); s.Write(crc);
    }

    private static uint[]? _table;
    private static uint Crc(byte[] d)
    {
        if (_table == null)
        {
            _table = new uint[256];
            for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; _table[n] = c; }
        }
        uint r = 0xFFFFFFFFu;
        foreach (byte b in d) r = _table[(r ^ b) & 0xFF] ^ (r >> 8);
        return r ^ 0xFFFFFFFFu;
    }

    public static uint Rgba(double r, double g, double b, double a = 1) =>
        ((uint)Math.Clamp(r * 255, 0, 255) << 24) | ((uint)Math.Clamp(g * 255, 0, 255) << 16) | ((uint)Math.Clamp(b * 255, 0, 255) << 8) | (uint)Math.Clamp(a * 255, 0, 255);
}
