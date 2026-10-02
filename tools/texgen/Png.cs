using System;
using System.IO;
using System.IO.Compression;

namespace ShipSim.TexGen;

/// <summary>
/// v16.5 PNG 읽기 · 쓰기 (RGBA 8비트만). 외부 라이브러리 없이 — 생성기와 헤드리스 시험이 같은 코드를 쓴다.
/// 줄마다 거르개 다섯 가지 중 합이 가장 작은 것을 고른다 (결정론: 같은 그림 → 같은 바이트).
/// </summary>
public static class Png
{
    private static readonly byte[] Sig = { 137, 80, 78, 71, 13, 10, 26, 10 };
    private static readonly uint[] CrcTable = BuildCrc();

    private static uint[] BuildCrc()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint Crc(byte[] type, byte[] data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (var b in type) c = CrcTable[(c ^ b) & 0xff] ^ (c >> 8);
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xff] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static void BE(Stream s, uint v) { s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        BE(s, (uint)data.Length);
        s.Write(t);
        s.Write(data);
        BE(s, Crc(t, data));
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    public static byte[] Encode(int w, int h, byte[] rgba)
    {
        int stride = w * 4;
        var raw = new byte[(stride + 1) * h];
        var trial = new byte[stride];
        var best = new byte[stride];
        for (int y = 0; y < h; y++)
        {
            long bestSum = long.MaxValue;
            int bestF = 0;
            for (int f = 0; f < 5; f++)
            {
                long sum = 0;
                for (int x = 0; x < stride; x++)
                {
                    int cur = rgba[y * stride + x];
                    int a = x >= 4 ? rgba[y * stride + x - 4] : 0;
                    int b = y > 0 ? rgba[(y - 1) * stride + x] : 0;
                    int c = x >= 4 && y > 0 ? rgba[(y - 1) * stride + x - 4] : 0;
                    int v = f switch { 0 => cur, 1 => cur - a, 2 => cur - b, 3 => cur - ((a + b) >> 1), _ => cur - Paeth(a, b, c) };
                    trial[x] = (byte)v;
                    sum += Math.Abs((sbyte)(byte)v);
                }
                if (sum < bestSum) { bestSum = sum; bestF = f; Array.Copy(trial, best, stride); }
            }
            raw[y * (stride + 1)] = (byte)bestF;
            Array.Copy(best, 0, raw, y * (stride + 1) + 1, stride);
        }
        byte[] z;
        using (var ms = new MemoryStream())
        {
            using (var zs = new ZLibStream(ms, CompressionLevel.SmallestSize, leaveOpen: true)) zs.Write(raw);
            z = ms.ToArray();
        }
        using var o = new MemoryStream();
        o.Write(Sig);
        var ihdr = new byte[13];
        ihdr[0] = (byte)(w >> 24); ihdr[1] = (byte)(w >> 16); ihdr[2] = (byte)(w >> 8); ihdr[3] = (byte)w;
        ihdr[4] = (byte)(h >> 24); ihdr[5] = (byte)(h >> 16); ihdr[6] = (byte)(h >> 8); ihdr[7] = (byte)h;
        ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        Chunk(o, "IHDR", ihdr);
        Chunk(o, "IDAT", z);
        Chunk(o, "IEND", Array.Empty<byte>());
        return o.ToArray();
    }

    /// <summary>RGBA 8비트 PNG만 푼다 (다른 형식이면 null).</summary>
    public static (int w, int h, byte[] rgba)? Decode(byte[] png)
    {
        if (png.Length < 33) return null;
        for (int i = 0; i < 8; i++) if (png[i] != Sig[i]) return null;
        int pos = 8, w = 0, h = 0;
        using var idat = new MemoryStream();
        while (pos + 8 <= png.Length)
        {
            int len = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
            string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            int data = pos + 8;
            if (len < 0 || data + len > png.Length) return null;
            if (type == "IHDR")
            {
                w = (png[data] << 24) | (png[data + 1] << 16) | (png[data + 2] << 8) | png[data + 3];
                h = (png[data + 4] << 24) | (png[data + 5] << 16) | (png[data + 6] << 8) | png[data + 7];
                if (png[data + 8] != 8 || png[data + 9] != 6 || png[data + 12] != 0) return null;
            }
            else if (type == "IDAT") idat.Write(png, data, len);
            else if (type == "IEND") break;
            pos = data + len + 4;
        }
        if (w <= 0 || h <= 0) return null;
        int stride = w * 4;
        var raw = new byte[(stride + 1) * h];
        idat.Position = 0;
        using (var zs = new ZLibStream(idat, CompressionMode.Decompress))
        {
            int got = 0;
            while (got < raw.Length) { int n = zs.Read(raw, got, raw.Length - got); if (n <= 0) break; got += n; }
            if (got < raw.Length) return null;
        }
        var o = new byte[stride * h];
        for (int y = 0; y < h; y++)
        {
            int f = raw[y * (stride + 1)];
            for (int x = 0; x < stride; x++)
            {
                int v = raw[y * (stride + 1) + 1 + x];
                int a = x >= 4 ? o[y * stride + x - 4] : 0;
                int b = y > 0 ? o[(y - 1) * stride + x] : 0;
                int c = x >= 4 && y > 0 ? o[(y - 1) * stride + x - 4] : 0;
                o[y * stride + x] = (byte)(f switch { 0 => v, 1 => v + a, 2 => v + b, 3 => v + ((a + b) >> 1), 4 => v + Paeth(a, b, c), _ => v });
            }
        }
        return (w, h, o);
    }

    /// <summary>FNV-1a 64 (그림 · 파일 지문).</summary>
    public static ulong Hash(byte[] data)
    {
        ulong h = 14695981039346656037UL;
        foreach (var b in data) { h ^= b; h *= 1099511628211UL; }
        return h;
    }
}
