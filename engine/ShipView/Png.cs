using System.IO.Compression;

namespace ShipView;

/// <summary>
/// 아주 작은 PNG 쓰개 · 읽개 (외부 라이브러리 없이): 관찰 장면 그림 (실험실) · 정점 사진 · 사진첩 (docs/OBSERVER.md §4.4 — V5에서 실험실에서 옮겼다).
/// 읽기는 8비트 RGBA (갈래 6) · 비월 없음만 — 이 쓰개가 쓴 꼴 (거르개 0~4는 모두 푼다). 그 밖이면 null.
/// </summary>
public static class Png
{
    /// <summary>RGBA 바이트 (도트 화가의 그림 Img.Px).</summary>
    public static void WriteRgba(string path, int w, int h, byte[] px)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, Encode(w, h, px));
    }

    public static void Write(string path, int w, int h, uint[] rgba)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { uint c = rgba[i]; px[i * 4] = (byte)(c >> 24); px[i * 4 + 1] = (byte)(c >> 16); px[i * 4 + 2] = (byte)(c >> 8); px[i * 4 + 3] = (byte)c; }
        WriteRgba(path, w, h, px);
    }

    /// <summary>PNG 바이트 (RGBA 앞 w × h × 4 바이트 · 거르개 0 · zlib).</summary>
    public static byte[] Encode(int w, int h, ReadOnlySpan<byte> px, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var fs = new MemoryStream();
        fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        Be(ihdr, 0, (uint)w); Be(ihdr, 4, (uint)h);
        ihdr[8] = 8; ihdr[9] = 6; // 8비트 RGBA
        Chunk(fs, "IHDR", ihdr);
        int row = w * 4;
        var raw = new byte[(row + 1) * h];
        for (int y = 0; y < h; y++) px.Slice(y * row, row).CopyTo(raw.AsSpan(y * (row + 1) + 1, row));   // 줄마다 거르개 0
        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, level, leaveOpen: true)) zs.Write(raw);
        Chunk(fs, "IDAT", z.ToArray());
        Chunk(fs, "IEND", Array.Empty<byte>());
        return fs.ToArray();
    }

    /// <summary>PNG를 RGBA로 (8비트 RGBA · 비월 없음 — 아니면 null · 깨졌으면 null).</summary>
    public static (int w, int h, byte[] px)? Decode(byte[] png)
    {
        try
        {
            if (png.Length < 33 || png[0] != 137 || png[1] != 80 || png[2] != 78 || png[3] != 71) return null;
            int at = 8, w = 0, h = 0;
            using var idat = new MemoryStream();
            while (at + 8 <= png.Length)
            {
                int len = (int)Rd(png, at); string type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
                if (len < 0 || at + 12 + len > png.Length) return null;
                if (type == "IHDR")
                {
                    w = (int)Rd(png, at + 8); h = (int)Rd(png, at + 12);
                    if (png[at + 16] != 8 || png[at + 17] != 6 || png[at + 20] != 0 || w <= 0 || h <= 0 || (long)w * h > 1 << 26) return null;
                }
                else if (type == "IDAT") idat.Write(png, at + 8, len);
                else if (type == "IEND") break;
                at += 12 + len;
            }
            if (w == 0) return null;
            int row = w * 4;
            var raw = new byte[(row + 1) * h];
            idat.Position = 0;
            using (var zs = new ZLibStream(idat, CompressionMode.Decompress)) zs.ReadExactly(raw);
            var px = new byte[row * h];
            for (int y = 0; y < h; y++)
            {
                byte f = raw[y * (row + 1)];
                var src = raw.AsSpan(y * (row + 1) + 1, row);
                var dst = px.AsSpan(y * row, row);
                var up = y > 0 ? px.AsSpan((y - 1) * row, row) : Span<byte>.Empty;
                for (int i = 0; i < row; i++)
                {
                    int a = i >= 4 ? dst[i - 4] : 0, b = y > 0 ? up[i] : 0, c = i >= 4 && y > 0 ? up[i - 4] : 0;
                    int v = f switch
                    {
                        0 => 0,
                        1 => a,
                        2 => b,
                        3 => (a + b) / 2,
                        4 => Paeth(a, b, c),
                        _ => -1,
                    };
                    if (v < 0) return null;
                    dst[i] = (byte)(src[i] + v);
                }
            }
            return (w, h, px);
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or ArgumentException) { return null; }

        static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }
    }

    private static uint Rd(byte[] b, int o) => (uint)b[o] << 24 | (uint)b[o + 1] << 16 | (uint)b[o + 2] << 8 | b[o + 3];
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
        var t = _table;
        if (t == null)
        {
            t = new uint[256];
            for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[n] = c; }
            _table = t;   // 다 지은 표만 건다 (다른 일꾼이 같이 써도 반쯤 지은 표를 읽지 않게)
        }
        uint r = 0xFFFFFFFFu;
        foreach (byte b in d) r = t[(r ^ b) & 0xFF] ^ (r >> 8);
        return r ^ 0xFFFFFFFFu;
    }

    public static uint Rgba(double r, double g, double b, double a = 1) =>
        ((uint)Math.Clamp(r * 255, 0, 255) << 24) | ((uint)Math.Clamp(g * 255, 0, 255) << 16) | ((uint)Math.Clamp(b * 255, 0, 255) << 8) | (uint)Math.Clamp(a * 255, 0, 255);
}
