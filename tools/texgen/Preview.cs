using System;
using System.Collections.Generic;
using System.IO;

namespace ShipSim.TexGen;

/// <summary>
/// 눈으로 확인하는 미리보기 (게임과 같은 규칙을 흉내): 16×8칸 · 칸 64px.
/// 기관실(격자 + 기관 구역판 · 청록 빛) | 주방(타일 · 물기 · 기름 · 얼룩 · 호박 빛) | 침실(카펫 · 닳은 길 · 서리) + 정전된 함교(고무 · 붉은 비상등).
/// 겹치기는 화면 셰이더와 같은 식: a = clamp((마스크 − (1 − 양)) / 부드러움, 0, 1) × 진하기.
/// </summary>
public static class Preview
{
    private const int Cs = 64, W = 16, H = 8;

    public static void Write(string path, Dictionary<string, Pix> tex)
    {
        var o = new Pix(W * Cs, H * Cs);
        string[] map =
        {
            "HHHHHHHHHHHHHHHH",
            "HEggEPtttTPccccH",
            "HEggEPtttTPccccH",
            "HEggEPttttPccccH",
            "HEggEPttttPrrrrH",
            "HEggEPttttPrrrrH",
            "HEggEPttttPrrrrH",
            "HHHHHHHHHHHHHHHH",
        };
        Pix? T(char ch) => ch switch
        {
            'H' => tex["look_wall_hull"], 'P' => tex["look_wall_partition"], 'E' => tex["look_floor_engine"], 'g' => tex["look_floor_grate"],
            't' or 'T' => tex["look_floor_tile"], 'c' => tex["look_floor_carpet"], 'r' => tex["look_floor_rubber"], 'm' => tex["look_floor_metal"], _ => null,
        };
        // 바닥 · 벽
        for (int cy = 0; cy < H; cy++)
        for (int cx = 0; cx < W; cx++)
        {
            var t = T(map[cy][cx]);
            if (t == null) continue;
            for (int y = 0; y < Cs; y++)
            for (int x = 0; x < Cs; x++)
            {
                int si = t.I((cx & 3) * Cs + x, (cy & 3) * Cs + y), di = o.I(cx * Cs + x, cy * Cs + y);
                o.R[di] = t.R[si]; o.G[di] = t.G[si]; o.B[di] = t.B[si]; o.A[di] = 1f;
            }
        }
        // 겹치기 (칸마다 양)
        void Ov(string name, int cx, int cy, float amt, float maxA, float soft, float rim = 0f, float dark = 0f)
        {
            var t = tex[name];
            for (int y = 0; y < Cs; y++)
            for (int x = 0; x < Cs; x++)
            {
                int si = t.I((cx & 3) * Cs + x, (cy & 3) * Cs + y), di = o.I(cx * Cs + x, cy * Cs + y);
                float m = t.A[si] - (1f - amt);
                float a = Math.Clamp(m / soft, 0f, 1f) * maxA;
                if (a <= 0f) continue;
                float rr = rim * (1f - Math.Clamp(m / (soft * 3f), 0f, 1f));
                float k = 1f - dark;
                o.R[di] += (t.R[si] * k + rr - o.R[di]) * a; o.G[di] += (t.G[si] * k + rr - o.G[di]) * a; o.B[di] += (t.B[si] * k + rr - o.B[di]) * a;
            }
        }
        for (int cy = 1; cy <= 6; cy++) { Ov("look_ov_dust", 1, cy, 0.55f, 0.6f, 0.3f); Ov("look_ov_rust", 4, cy, 0.25f + 0.1f * cy, 0.85f, 0.12f); Ov("look_ov_wear", 2, cy, 0.5f, 0.35f, 0.25f); }
        Ov("look_ov_oil", 3, 3, 0.7f, 0.85f, 0.05f, -0.1f); Ov("look_ov_oil", 3, 4, 0.4f, 0.85f, 0.05f, -0.1f);
        foreach (var (cx, cy, a) in new[] { (6, 4, 0.8f), (7, 4, 0.95f), (7, 5, 0.6f), (6, 5, 0.4f), (8, 4, 0.3f) }) Ov("look_ov_wet", cx, cy, a, 0.55f, 0.05f, 0.3f);
        foreach (var (cx, cy, a) in new[] { (8, 1, 0.9f), (9, 1, 0.6f), (8, 2, 0.5f) }) Ov("look_ov_soot", cx, cy, a, 0.9f, 0.35f);
        foreach (var (cx, cy, a) in new[] { (14, 1, 0.9f), (14, 2, 0.6f), (13, 1, 0.5f) }) Ov("look_ov_frost", cx, cy, a, 0.8f, 0.08f, 0.25f);
        for (int cx = 11; cx <= 14; cx++) Ov("look_ov_wear", cx, 2, 0.75f, 0.45f, 0.3f, 0f, 0.7f);
        // 흔적
        var dec = tex["look_decals"];
        void Decal(int slot, int cx, int cy)
        {
            int sx = (slot % 8) * 64, sy = (slot / 8) * 64;
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                int si = dec.I(sx + x, sy + y), di = o.I(cx * Cs + x, cy * Cs + y);
                float a = dec.A[si];
                o.R[di] += (dec.R[si] - o.R[di]) * a; o.G[di] += (dec.G[si] - o.G[di]) * a; o.B[di] += (dec.B[si] - o.B[di]) * a;
            }
        }
        Decal(1, 8, 3); Decal(0, 9, 2); Decal(5, 8, 5); Decal(16, 5, 2); Decal(13, 4, 6); Decal(20, 10, 4); Decal(4, 12, 3); Decal(24, 9, 1); Decal(8, 2, 5); Decal(12, 12, 5); Decal(7, 3, 2);
        // 빛 (화면 빛 버퍼와 같은 수): 방 바탕 0.45 × 색온도 + 천장 등((x + 2y) % 4 == 0 칸, 세기 0.5 · 반지름 1.8칸) · 정전 방은 0.06 + 붉은 비상등 · 불빛
        (float r, float g, float b) Temp(int cx) => cx <= 4 ? (0.72f, 0.96f, 1f) : cx <= 9 ? (1f, 0.84f, 0.62f) : (1f, 0.84f, 0.62f);
        var lights = new List<(float x, float y, float r, float cr, float cg, float cb, int zone)>();
        int Zone(int cx, int cy) => cx >= 1 && cx <= 4 ? 0 : cx >= 6 && cx <= 9 ? 1 : cx >= 11 && cx <= 14 ? (cy >= 4 ? 3 : 2) : -1;
        for (int cy = 1; cy < H - 1; cy++)
        for (int cx = 1; cx < W - 1; cx++)
        {
            int z = Zone(cx, cy);
            if (z < 0 || (cx + 2 * cy) % 4 != 0) continue;
            if (z == 3) { lights.Add((cx + 0.5f, cy + 0.5f, 2.4f, 0.95f * 0.55f, 0.14f * 0.55f, 0.09f * 0.55f, z)); continue; }
            var t = Temp(cx);
            lights.Add((cx + 0.5f, cy + 0.5f, 1.8f, t.r * 0.5f, t.g * 0.5f, t.b * 0.5f, z));
        }
        lights.Add((8.5f, 1.2f, 3f, 0.75f, 0.41f, 0.15f, 1)); // 불
        for (int y = 0; y < o.H; y++)
        for (int x = 0; x < o.W; x++)
        {
            int cx = x / Cs, cy = y / Cs;
            int z = Zone(cx, cy);
            bool wall = z < 0;
            var t = Temp(cx);
            float lr, lg, lb;
            if (z == 3) { lr = 0.048f; lg = 0.054f; lb = 0.078f; }
            else { float amb = wall ? 0.41f : 0.45f; lr = amb * t.r; lg = amb * t.g; lb = amb * t.b; }
            float px = x / (float)Cs, py = y / (float)Cs;
            foreach (var l in lights)
            {
                if (!wall && l.zone != z) continue;
                float d2 = ((px - l.x) * (px - l.x) + (py - l.y) * (py - l.y)) / (l.r * l.r);
                if (d2 >= 1f) continue;
                float f = 1f - d2; f *= f;
                if (wall) f *= 0.6f;
                lr += f * l.cr; lg += f * l.cg; lb += f * l.cb;
            }
            int i = o.I(x, y);
            o.R[i] *= MathF.Min(1f, lr); o.G[i] *= MathF.Min(1f, lg); o.B[i] *= MathF.Min(1f, lb);
        }
        File.WriteAllBytes(path, Png.Encode(o.W, o.H, o.Rgba8()));
    }
}
