using System.Runtime.InteropServices;
using ShipSim.Art;

namespace ShipView;

/// <summary>
/// 빠른 얹기 (60프레임): 옛 <see cref="Img.Draw"/> · <see cref="Img.Fill"/>는 도트마다 소수 섞기를 한다 — 한 장 41×24칸(130만 도트)에 우주 칠하기 14ms · 바닥 얹기 14ms.
/// 불투명 도트(알파 255 · 덧 알파 1)는 그대로 옮기고 (섞기 결과와 바이트까지 같다) · 투명 도트는 건너뛰고 · 반투명만 예전처럼 섞는다.
/// </summary>
public sealed partial class Painter
{
    /// <summary>그림을 얹는다 — <see cref="Img.Draw"/>와 같은 결과.</summary>
    private static void Put(Img dst, Img src, int dx, int dy, bool flipX = false, float alpha = 1f)
    {
        if (flipX || alpha < 1f) { dst.Draw(src, dx, dy, flipX, alpha); return; }
        int x0 = Math.Max(0, -dx), y0 = Math.Max(0, -dy), x1 = Math.Min(src.W, dst.W - dx), y1 = Math.Min(src.H, dst.H - dy);
        if (x0 >= x1 || y0 >= y1) return;
        var s = MemoryMarshal.Cast<byte, uint>(src.Px.AsSpan());
        var d = MemoryMarshal.Cast<byte, uint>(dst.Px.AsSpan());
        for (int y = y0; y < y1; y++)
        {
            int si = y * src.W, di = (dy + y) * dst.W + dx;
            for (int x = x0; x < x1; x++)
            {
                uint c = s[si + x];
                uint a = c >> 24;
                if (a == 0) continue;
                if (a == 255) { d[di + x] = c; continue; }
                dst.Blend(dx + x, dy + y, new Rgba((byte)c, (byte)(c >> 8), (byte)(c >> 16), (byte)a));
            }
        }
    }

    /// <summary>불투명한 색으로 네모를 칠한다 — 알파 255 · 덧 알파 1의 <see cref="Img.Fill"/>과 같은 결과.</summary>
    private static void FillOpaque(Img dst, int x, int y, int w, int h, Rgba c)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y), x1 = Math.Min(dst.W, x + w), y1 = Math.Min(dst.H, y + h);
        if (x0 >= x1 || y0 >= y1) return;
        uint v = (uint)c.R | (uint)c.G << 8 | (uint)c.B << 16 | (uint)c.A << 24;
        var d = MemoryMarshal.Cast<byte, uint>(dst.Px.AsSpan());
        for (int yy = y0; yy < y1; yy++) d.Slice(yy * dst.W + x0, x1 - x0).Fill(v);
    }
}
