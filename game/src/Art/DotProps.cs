using System;
using System.Collections.Generic;

namespace ShipSim.Art;

/// <summary>벽에 붙은 소품 13가지.</summary>
public enum DotProp : byte { Vent, Panel, Extinguisher, Gauge, Hazard, Locker, Screen, Plant, MedBox, ToolBoard, Sign, Tank, Pipe }

/// <summary>소품이 붙은 벽 쪽: 북쪽 벽(앞면에 걸려 정면으로 보임) · 서/동 벽(모서리만 보임) · 남쪽 벽(윗모서리만 보임).</summary>
public enum PropSide : byte { Face, Side, Back }

/// <summary>
/// 도트 소품: 벽에 붙은 것(환기구 · 계기판 · 소화기 · 압력계 · 경고판 · 사물함 · 화면 · 화분 · 구급함 · 공구판 · 번호판 · 탱크 · 배관)과
/// 바닥 꾸밈(환기 격자 · 배수구 · 깔개 · 침대 발치 매트 · 경고 띠 틀). 3×5 도트 글자도 여기.
/// </summary>
public static class DotProps
{
    /// <summary>앞면 그림: 가로 한 칸 · 위 10줄은 벽 앞면 위, 아래 8줄은 바닥 (서 있는 것은 맨 아랫줄이 발).</summary>
    public const int FaceW = 32, FaceH = 18;
    /// <summary>옆 · 뒤 벽 모서리 띠 두께.</summary>
    public const int Strip = 4;

    /// <summary>바닥에 서 있는 것 (어느 벽이든 정면 그림을 세운다).</summary>
    public static bool Standing(DotProp p) => p is DotProp.Extinguisher or DotProp.Plant or DotProp.Tank or DotProp.Locker;

    /// <summary>
    /// 벽 소품 그림. Face · 서 있는 것: FaceW × FaceH. Side: Strip × 24 (세로 띠). Back: 24 × Strip (가로 띠).
    /// accent: 방 색표(번호판 · 사물함 줄) · liquid: 탱크 속 색표 · code: 번호판 글자.
    /// </summary>
    public static Img Wall(DotProp p, PropSide side, int accent, int liquid, int seed, string code = "")
    {
        if (side == PropSide.Face || Standing(p)) return Front(p, accent, liquid, seed, code);
        return EdgeStrip(p, side == PropSide.Side, accent, seed);
    }

    private static Img Front(DotProp p, int accent, int liquid, int seed, string code)
    {
        var d = new Dot(FaceW, FaceH);
        int cx = FaceW / 2;
        switch (p)
        {
            case DotProp.Vent:
                d.Part = 1; d.Rect(cx - 9, 2, 18, 7, Ramps.Steel, 2);
                d.Part = 2; for (int x = cx - 8; x < cx + 8; x += 2) d.Rect(x, 3, 1, 5, Ramps.Panel, 0);
                break;
            case DotProp.Panel:
                d.Part = 1; d.Rect(cx - 7, 1, 14, 8, Ramps.Panel, 3);
                d.Part = 2; d.Rect(cx - 5, 3, 6, 3, Ramps.Panel, 1);
                d.Mark(cx + 2, 3, Ramps.Led, 3); d.Mark(cx + 4, 3, Ramps.Hazard, 3); d.Mark(cx + 2, 6, Ramps.Alarm, 3);
                d.Part = 3; d.Rect(cx + 4, 5, 2, 3, Ramps.Steel, 4);
                break;
            case DotProp.Extinguisher:
                d.Part = 1; d.Rect(cx - 3, 6, 6, 11, Ramps.Alarm, 3);
                d.Part = 2; d.Rect(cx - 2, 4, 4, 2, Ramps.Rubber, 2);
                d.Part = 3; d.Line(cx + 1, 4, cx + 4, 7, Ramps.Rubber, 3); d.Line(cx + 4, 7, cx + 4, 11, Ramps.Rubber, 3);
                d.Part = 4; d.Rect(cx - 3, 10, 6, 2, Ramps.Bandage, 2);
                break;
            case DotProp.Gauge:
                d.Part = 1; d.Oval(cx, 4.5f, 4.5f, 4.5f, Ramps.Steel, 3);
                d.Part = 2; d.Oval(cx, 4.5f, 3.2f, 3.2f, Ramps.Bandage, 3);
                {
                    float a = -2.4f + DotHash.F(seed, 5) * 2.8f;
                    d.Line(cx, 5, cx + (int)MathF.Round(MathF.Cos(a) * 3f), 5 + (int)MathF.Round(MathF.Sin(a) * 3f), Ramps.Alarm, 2, mark: true);
                    d.Mark(cx + 2, 3, Ramps.Alarm, 3); d.Mark(cx - 2, 3, Ramps.Led, 2);
                }
                break;
            case DotProp.Hazard:
                d.Part = 1; d.Rect(cx - 10, 2, 20, 6, Ramps.Hazard, 3);
                for (int x = cx - 10; x < cx + 10; x++)
                    for (int y = 2; y < 8; y++)
                        if (((x + y) & 7) < 3) d.Mark(x, y, Ramps.Ink, 1);
                break;
            case DotProp.Locker:
                d.Part = 1; d.Rect(cx - 8, 0, 16, 17, Ramps.Steel, 3);
                d.Part = 2; d.Rect(cx - 7, 1, 7, 15, Ramps.Steel, 3); d.Rect(cx + 1, 1, 6, 15, Ramps.Steel, 3);
                d.MarkRect(cx - 7, 1, 14, 1, accent, 3);
                for (int y = 3; y < 7; y += 2) { d.MarkRect(cx - 5, y, 3, 1, Ramps.Panel, 1); d.MarkRect(cx + 3, y, 3, 1, Ramps.Panel, 1); }
                d.Mark(cx - 1, 9, Ramps.Steel, 5); d.Mark(cx + 2, 9, Ramps.Steel, 5);
                if ((seed & 3) == 0) d.MarkRect(cx + 3, 11, 3, 2, Ramps.Bandage, 3); // 붙여 둔 쪽지
                break;
            case DotProp.Screen:
                d.Part = 1; d.Rect(cx - 10, 0, 20, 9, Ramps.Panel, 2);
                d.Part = 2; d.Rect(cx - 9, 1, 18, 7, Ramps.Glass, 0);
                for (int k = 0; k < 3; k++)
                {
                    int len = 4 + (int)(DotHash.F(seed, k) * 10);
                    d.MarkRect(cx - 7, 2 + k * 2, len, 1, accent, 3);
                }
                d.Mark(cx + 7, 6, accent, 4);
                break;
            case DotProp.Plant:
                d.Part = 1; d.Rect(cx - 4, 11, 8, 6, Ramps.Wood, 2);
                d.Part = 2; d.Rect(cx - 5, 10, 10, 2, Ramps.Wood, 3);
                d.Part = 3;
                for (int k = 0; k < 7; k++)
                {
                    float a = -MathF.PI / 2 + (k - 3) * 0.45f + (DotHash.F(seed, k) - 0.5f) * 0.3f;
                    float len = 5 + DotHash.F(seed, k + 9) * 4;
                    float x = cx, y = 10;
                    for (int s = 0; s < len; s++) { x += MathF.Cos(a); y += MathF.Sin(a) * 0.9f; d.Set((int)x, (int)y, Ramps.Leaf, 2 + (s & 1)); d.Set((int)x + 1, (int)y, Ramps.Leaf, 2); }
                }
                break;
            case DotProp.MedBox:
                d.Part = 1; d.Rect(cx - 6, 1, 12, 8, Ramps.Bandage, 2);
                d.MarkRect(cx - 1, 2, 2, 6, Ramps.Alarm, 3); d.MarkRect(cx - 3, 4, 6, 2, Ramps.Alarm, 3);
                break;
            case DotProp.ToolBoard:
                d.Part = 1; d.Rect(cx - 12, 0, 24, 9, Ramps.Wood, 1);
                for (int x = cx - 11; x < cx + 12; x += 3) for (int y = 1; y < 9; y += 3) d.Mark(x, y, Ramps.Wood, 0);
                d.Part = 2;
                d.Line(cx - 9, 2, cx - 9, 7, Ramps.Steel, 4); d.Rect(cx - 10, 1, 3, 2, Ramps.Steel, 4);   // 렌치
                d.Line(cx - 4, 2, cx - 4, 7, Ramps.Steel, 3); d.Rect(cx - 5, 6, 3, 2, Ramps.Alarm, 3);    // 드라이버
                d.Rect(cx + 1, 2, 4, 2, Ramps.Steel, 3); d.Line(cx + 3, 4, cx + 3, 7, Ramps.Wood, 3);     // 망치
                d.Line(cx + 8, 2, cx + 10, 7, Ramps.Steel, 4); d.Line(cx + 10, 2, cx + 8, 7, Ramps.Steel, 4); // 가위
                break;
            case DotProp.Sign:
            {
                int tw = TextWidth(code);
                int w = Math.Max(14, tw + 4);
                d.Part = 1; d.Rect(cx - w / 2, 1, w, 8, accent, 1);
                d.MarkRect(cx - w / 2, 1, w, 1, accent, 3);
                Text(d, cx - tw / 2, 3, code, Ramps.Bandage, 3);
                break;
            }
            case DotProp.Tank:
                d.Part = 1; d.Rect(cx - 6, 1, 12, 16, Ramps.Steel, 3);
                d.Part = 2; d.Rect(cx - 7, 0, 14, 2, Ramps.Steel, 4); d.Rect(cx - 7, 15, 14, 2, Ramps.Steel, 2);
                d.Part = 3; d.Rect(cx - 2, 4, 4, 9, Ramps.Glass, 1);
                d.MarkRect(cx - 2, 7, 4, 6, liquid, 3);
                d.Mark(cx - 2, 7, liquid, 4);
                break;
            case DotProp.Pipe:
                d.Part = 1; d.Rect(0, 1, FaceW, 3, Ramps.Steel, 3);
                d.Part = 2; d.Rect(0, 5, FaceW, 3, Ramps.SteelWarm, 3);
                d.Part = 3; d.Rect(cx - 6, 0, 2, 5, Ramps.Steel, 4); d.Rect(cx + 5, 4, 2, 5, Ramps.SteelWarm, 4);
                d.Part = 4; d.Oval(cx, 3, 2.5f, 2.5f, Ramps.Alarm, 2); d.Mark(cx, 3, Ramps.Alarm, 4);
                break;
        }
        d.Bevel();
        return d.ToImg();
    }

    /// <summary>옆 · 뒤 벽: 벽에 붙은 판의 모서리만 보인다 (vertical = 서/동 벽의 세로 띠).</summary>
    private static Img EdgeStrip(DotProp p, bool vertical, int accent, int seed)
    {
        int L = 24;
        var d = new Dot(vertical ? Strip : L, vertical ? L : Strip);
        void Band(int from, int len, int ramp, int shade)
        {
            if (vertical) d.Rect(0, from, Strip - 1, len, ramp, shade);
            else d.Rect(from, 0, len, Strip - 1, ramp, shade);
        }
        void Dot1(int at, int ramp, int shade)
        {
            if (vertical) d.Mark(1, at, ramp, shade); else d.Mark(at, 1, ramp, shade);
        }
        d.Part = 1;
        switch (p)
        {
            case DotProp.Vent: Band(3, 18, Ramps.Steel, 2); for (int k = 4; k < 20; k += 2) Dot1(k, Ramps.Panel, 0); break;
            case DotProp.Panel: Band(5, 14, Ramps.Panel, 3); Dot1(8, Ramps.Led, 3); Dot1(11, Ramps.Hazard, 3); break;
            case DotProp.Gauge: Band(8, 8, Ramps.Steel, 3); Dot1(11, Ramps.Bandage, 3); break;
            case DotProp.Hazard: Band(2, 20, Ramps.Hazard, 3); for (int k = 2; k < 22; k += 4) { Dot1(k, Ramps.Ink, 1); Dot1(k + 1, Ramps.Ink, 1); } break;
            case DotProp.Screen: Band(2, 20, Ramps.Panel, 2); for (int k = 4; k < 20; k += 3) Dot1(k, accent, 3); break;
            case DotProp.MedBox: Band(6, 12, Ramps.Bandage, 2); Dot1(11, Ramps.Alarm, 3); Dot1(12, Ramps.Alarm, 3); break;
            case DotProp.ToolBoard: Band(0, 24, Ramps.Wood, 1); for (int k = 3; k < 22; k += 5) Dot1(k, Ramps.Steel, 4); break;
            case DotProp.Sign: Band(4, 16, accent, 1); Dot1(8, Ramps.Bandage, 3); Dot1(12, Ramps.Bandage, 3); break;
            case DotProp.Pipe:
                if (vertical) { d.Rect(0, 0, 2, L, Ramps.Steel, 3); d.Part = 2; d.Rect(2, 0, 2, L, Ramps.SteelWarm, 3); }
                else { d.Rect(0, 0, L, 2, Ramps.Steel, 3); d.Part = 2; d.Rect(0, 2, L, 2, Ramps.SteelWarm, 3); }
                break;
            default: Band(6, 12, Ramps.Steel, 3); break;
        }
        d.Bevel();
        return d.ToImg();
    }

    // ───────────────────────────── 바닥 꾸밈 ─────────────────────────────

    /// <summary>바닥 환기 격자 (댐퍼 자리) 18 × 18.</summary>
    public static Img Grate()
    {
        var d = new Dot(18, 18);
        d.Part = 1; d.Rect(0, 0, 18, 18, Ramps.Panel, 3);
        d.Part = 2; d.Rect(2, 2, 14, 14, Ramps.Panel, 0);
        d.Part = 3; for (int x = 3; x < 16; x += 3) d.Rect(x, 2, 1, 14, Ramps.Steel, 2);
        d.Bevel();
        d.Mark(1, 1, Ramps.Steel, 4); d.Mark(16, 1, Ramps.Steel, 4); d.Mark(1, 16, Ramps.Steel, 3); d.Mark(16, 16, Ramps.Steel, 3);
        return d.ToImg(outline: false);
    }

    /// <summary>바닥 배수구 11 × 11.</summary>
    public static Img Drain()
    {
        var d = new Dot(11, 11);
        d.Part = 1; d.Oval(5f, 5f, 5f, 5f, Ramps.Steel, 2);
        d.Part = 2; d.Oval(5f, 5f, 3.6f, 3.6f, Ramps.Panel, 0);
        d.Part = 3; d.Rect(2, 5, 7, 1, Ramps.Steel, 3); d.Rect(5, 2, 1, 7, Ramps.Steel, 3);
        d.Bevel();
        return d.ToImg(outline: false);
    }

    /// <summary>깔개 (w × h) — 테두리 · 마름모 무늬 · 짧은 쪽 술. ramp: 바탕 색표.</summary>
    public static Img Rug(int w, int h, int ramp, int seed, bool fringe = true)
    {
        var d = new Dot(w, h);
        int f = fringe ? 2 : 0;
        bool across = w >= h; // 술은 짧은 쪽에
        int x0 = across ? f : 0, y0 = across ? 0 : f, rw = across ? w - 2 * f : w, rh = across ? h : h - 2 * f;
        d.Part = 1; d.Rect(x0, y0, rw, rh, ramp, 1);
        d.MarkRect(x0 + 1, y0 + 1, rw - 2, 1, ramp, 3); d.MarkRect(x0 + 1, y0 + rh - 2, rw - 2, 1, ramp, 3);
        d.MarkRect(x0 + 1, y0 + 1, 1, rh - 2, ramp, 3); d.MarkRect(x0 + rw - 2, y0 + 1, 1, rh - 2, ramp, 3);
        int alt = Ramps.Accent[(int)(DotHash.U(seed, 3) % (uint)Ramps.Accent.Length)];
        int cx = x0 + rw / 2, cy = y0 + rh / 2, step = Math.Clamp(Math.Min(rw, rh) / 3, 8, 16);
        for (int y = y0 + 3; y < y0 + rh - 3; y++)
            for (int x = x0 + 3; x < x0 + rw - 3; x++)
            {
                int m = Math.Abs(((x - cx) % step + step) % step - step / 2) + Math.Abs(((y - cy) % step + step) % step - step / 2);
                if (m == step / 2 - 1) d.Mark(x, y, alt, 2);
                else if (m == 0) d.Mark(x, y, ramp, 3);
            }
        if (fringe)
            for (int k = 1; k < (across ? h : w) - 1; k += 2)
            {
                if (across) { d.Mark(0, k, Ramps.Bandage, 1, true); d.Mark(1, k, Ramps.Bandage, 1, true); d.Mark(w - 1, k, Ramps.Bandage, 1, true); d.Mark(w - 2, k, Ramps.Bandage, 1, true); }
                else { d.Mark(k, 0, Ramps.Bandage, 1, true); d.Mark(k, 1, Ramps.Bandage, 1, true); d.Mark(k, h - 1, Ramps.Bandage, 1, true); d.Mark(k, h - 2, Ramps.Bandage, 1, true); }
            }
        return d.ToImg(outline: false);
    }

    /// <summary>경고 띠 틀 (w × h, 띠 두께 t) — 노랑 · 검정 빗금.</summary>
    public static Img HazardFrame(int w, int h, int t)
    {
        var d = new Dot(w, h);
        d.Part = 1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (x >= t && x < w - t && y >= t && y < h - t) continue;
                bool black = ((x + y) / 4 & 1) == 1;
                d.Mark(x, y, black ? Ramps.Ink : Ramps.Hazard, black ? 1 : 3, onEmpty: true);
            }
        // 닳은 자리
        for (int k = 0; k < (w + h) / 6; k++)
        {
            int x = (int)(DotHash.F(w, k) * w), y = DotHash.F(h, k + 50) < 0.5f ? (int)(DotHash.F(k, 7) * t) : h - 1 - (int)(DotHash.F(k, 8) * t);
            d.Tone(x, y, -1);
        }
        return d.ToImg(outline: false);
    }

    // ───────────────────────────── 3 × 5 글자 ─────────────────────────────

    private static readonly Dictionary<char, string> Glyphs = new()
    {
        ['0'] = "111101101101111", ['1'] = "010110010010111", ['2'] = "111001111100111", ['3'] = "111001111001111", ['4'] = "101101111001001",
        ['5'] = "111100111001111", ['6'] = "111100111101111", ['7'] = "111001010010010", ['8'] = "111101111101111", ['9'] = "111101111001111",
        ['A'] = "010101111101101", ['B'] = "110101110101110", ['C'] = "011100100100011", ['D'] = "110101101101110", ['E'] = "111100110100111",
        ['F'] = "111100110100100", ['G'] = "011100101101011", ['H'] = "101101111101101", ['I'] = "111010010010111", ['J'] = "001001001101010",
        ['K'] = "101101110101101", ['L'] = "100100100100111", ['M'] = "101111111101101", ['N'] = "110101101101101", ['O'] = "010101101101010",
        ['P'] = "110101110100100", ['Q'] = "010101101110011", ['R'] = "110101110101101", ['S'] = "011100010001110", ['T'] = "111010010010010",
        ['U'] = "101101101101111", ['V'] = "101101101101010", ['W'] = "101101111111101", ['X'] = "101101010101101", ['Y'] = "101101010010010",
        ['Z'] = "111001010100111", ['-'] = "000000111000000", ['.'] = "000000000000010",
    };

    public static int TextWidth(string s) => s.Length == 0 ? 0 : s.Length * 4 - 1;

    /// <summary>3 × 5 도트 글자 (대문자 · 숫자 · - . 만, 그 밖은 빈칸).</summary>
    public static void Text(Dot d, int x, int y, string s, int ramp, int shade)
    {
        foreach (char ch0 in s)
        {
            char ch = char.ToUpperInvariant(ch0);
            if (Glyphs.TryGetValue(ch, out var g))
                for (int i = 0; i < 15; i++)
                    if (g[i] == '1') d.Mark(x + i % 3, y + i / 3, ramp, shade);
            x += 4;
        }
    }
}
