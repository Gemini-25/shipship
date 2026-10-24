using ShipEngine;
using ShipSim.Art;

namespace ShipView;

/// <summary>
/// 화장실 · 샤워실 설비의 제 그림 (함장 요청 — 옛 도트에 없는 설비): 변기 · 세면대 · 샤워 칸.
/// 칸 하나 32도트 · 비스듬한 시점 (위로 솟은 만큼 윗벽에 걸친다) · 흰 도기 · 강철 · 유리.
/// 5단 A1 — 세계를 읽는다 (읽기만): 변기 몸은 때 3단 × 막힘 (굽는다) · 장마다 덧그림 — 샤워 칸 물줄기 (밸브 · 물관 · 물탱크) · 세면대 물줄기 (손 씻는 걸음 · 물관) ·
/// 변기의 '사용 금지' 불 (주컴퓨터가 켠 불) · 쓰는 동안 닫힌 샤워 커튼 (그 칸에서 [샤워] 걸음인 사람 — 커튼은 세계 값이 아니다 · 커튼 위로 얼굴까지) ·
/// 설비 칸에 고인 물 (발치 웅덩이 — 많이 고이면 곁 바닥처럼 칸에 물이 차고 설비는 그 물에 선다).
/// </summary>
public sealed partial class Painter
{
    /// <summary>설비 종류 → 제 그림 번호 (0 = 옛 도트로).</summary>
    private static int WashKind(string type) => type switch { "Toilet" => 1, "Sink" => 2, "Shower" => 3, _ => 0 };

    /// <summary>위로 솟은 도트 (변기 물통 · 세면대 거울 · 샤워 칸 유리벽).</summary>
    private static int WashRise(int kind) => kind switch { 1 => 6, 2 => 10, _ => 14 };

    private static readonly Rgba WOut = Rgba.Hex("#5d6873"), WPorc = Rgba.Hex("#eef3f6"), WPorcSide = Rgba.Hex("#c4ced7"), WPorcDeep = Rgba.Hex("#9eabb7"),
        WWater = Rgba.Hex("#8fc0dc"), WSteel = Rgba.Hex("#7f8fa0"), WSteelHi = Rgba.Hex("#b3c1cd"), WGlass = Rgba.Hex("#a8dcea"), WTile = Rgba.Hex("#dbe4ea"), WGrout = Rgba.Hex("#aebbc5");
    private static readonly Rgba WGrime = Rgba.Hex("#8a6a3a"), WMurk = Rgba.Hex("#6b5330"), WMurkDark = Rgba.Hex("#4a3820"), WScum = Rgba.Hex("#8f7a4e"),
        WSpray = Rgba.Hex("#b0e2ff"), WSprayCore = Rgba.Hex("#d4f1ff"), WWet = Rgba.Hex("#9fd2ec"), WSign = Rgba.Hex("#ff3b30"), PoolWater = Rgba.Hex("#5aa3bd");
    private static readonly Rgba[] Fold = { Rgba.Hex("#1f7d86"), Rgba.Hex("#36aab1"), Rgba.Hex("#5cc4c6"), Rgba.Hex("#2c9199") };   // 커튼 주름 (x % 4)

    /// <summary>'사용 금지' 불 자리 (변기 몸 그림 좌표 — 흡입 장치 오른쪽 · 짝수 도트라 넓은 보기 16도트에도 한 점으로 남는다).</summary>
    private const int SignX = 20, SignY = 4;

    /// <summary>변기 몸의 상태 (굽는다 — 0~5): 때 3단 (0.3 · 0.65) × 막힘 (막혔다 · 대변이 걸려 있다). 세면대 · 샤워 칸 · 화장실 배열이 없는 배는 0.</summary>
    private static int WashState(Life? life, int fu, int kind)
    {
        if (kind != 1 || life == null || fu >= life.Grime.Length || fu >= life.Clogged.Length || fu >= life.Bowl.Length) return 0;
        float g = life.Grime[fu];
        int grime = g < 0.3f ? 0 : g < 0.65f ? 1 : 2;
        return grime * 2 + (life.Clogged[fu] != 0 || life.Bowl[fu] > 0 ? 1 : 0);
    }

    private static Img WashBody(int kind, int state = 0)
    {
        int rise = WashRise(kind), w = T, h = T + rise;
        var g = new Img(w, h);
        switch (kind)
        {
            case 1:   // 변기 (진공 변기 — 물통 대신 흡입 장치 · 허벅지 고정대)
            {
                g.Fill(8, 1, 16, 10, WOut); g.Fill(9, 2, 14, 7, WPorc); g.Fill(9, 9, 14, 2, WPorcSide);   // 뒤쪽 흡입 장치
                g.Fill(14, 3, 4, 2, WSteel); g.Set(15, 3, WSteelHi);                                        // 누름 단추
                float cx = 15.5f, cy = 23;
                g.Oval(cx, cy, 10, 11, WOut);
                g.Oval(cx, cy, 9, 10, WPorcSide);
                g.Oval(cx, cy - 0.5f, 8, 9, WPorc);                                                            // 앉는 테
                g.Oval(cx, cy, 5, 6, WPorcDeep);
                int grime = state / 2;
                if (grime > 0)   // 때 (Life.Grime): 대야 테 안쪽에 갈색 고리 · 테에 얼룩 (짙기 0.3 + 0.6 × 그 단의 때)
                {
                    float a = 0.3f + 0.6f * (grime == 1 ? 0.45f : 0.85f);
                    g.Oval(cx, cy, 5, 6, WGrime, a);
                    for (int k = 0; k < 4 + grime * 4; k++)
                    {
                        float ang = k * 2.4f + 0.7f;
                        g.Blend((int)(cx + MathF.Cos(ang) * 6.5f), (int)(cy - 0.5f + MathF.Sin(ang) * 7.5f), WGrime, a * 0.8f);
                    }
                }
                if (state % 2 == 1)   // 막힘 (Clogged · 걸린 대변): 대야 물이 탁한 갈색으로 차오른다 · 떠 있는 덩이
                {
                    g.Oval(cx, cy + 0.5f, 4.5f, 5.5f, WMurk);
                    g.Oval(cx, cy - 1.5f, 3.5f, 1.2f, WScum, 0.7f);
                    g.Set(14, 22, WMurkDark); g.Set(15, 22, WMurkDark); g.Set(17, 25, WMurkDark); g.Set(13, 26, WMurkDark);
                }
                else g.Oval(cx, cy + 0.5f, 3.5f, 4.5f, WWater);                                                // 안
                g.Blend(13, 19, Rgba.Hex("#ffffff"), 0.8f); g.Blend(12, 20, Rgba.Hex("#ffffff"), 0.5f);         // 빛
                foreach (int bx in new[] { 3, 27 }) { g.Fill(bx, 19, 2, 9, WOut); g.Fill(bx, 19, 1, 9, WSteel); g.Fill(bx - 1, 18, 4, 2, WSteelHi); }   // 허벅지 고정대
                break;
            }
            case 2:   // 세면대 (거울 · 상판 · 대야 · 수도꼭지)
            {
                g.Fill(6, 0, 20, 11, WOut);
                for (int y = 1; y < 10; y++)
                    for (int x = 7; x < 25; x++)
                    {
                        float t = (x - 7 + y) / 26f;
                        g.Set(x, y, Rgba.Lerp(Rgba.Hex("#c9eef7"), Rgba.Hex("#4d8ea8"), t));
                    }
                for (int k = 0; k < 5; k++) g.Blend(9 + k, 7 - k, Rgba.Hex("#ffffff"), 0.55f);                 // 거울 빛줄기
                g.Fill(2, 12, 28, 19, WOut);
                g.Fill(3, 13, 26, 14, Rgba.Hex("#d6dde3"));                                                     // 상판
                g.Fill(3, 27, 26, 3, Rgba.Hex("#98a4af"));                                                      // 상판 앞면
                g.Oval(16, 20, 9, 5.5f, WOut);
                g.Oval(16, 20, 8, 4.5f, WPorc);
                g.Oval(16, 20.5f, 6, 3, Rgba.Hex("#b6cfdd"));                                                   // 대야
                g.Set(16, 21, Rgba.Hex("#4a5561"));                                                             // 배수구
                g.Fill(15, 13, 3, 3, WSteel); g.Fill(16, 15, 1, 2, WSteelHi);                                   // 수도꼭지
                g.Fill(11, 14, 2, 2, WSteel); g.Fill(20, 14, 2, 2, WSteel);
                break;
            }
            default:  // 샤워 칸 (푸른 타일 뒷벽 · 샤워기 · 받침 · 왼쪽에 접은 커튼 — 쓰는 동안 닫힌 커튼은 장마다 사람 앞에 얹는다)
            {
                g.Fill(1, 0, 30, 18, WOut);
                g.Fill(2, 1, 28, 16, Rgba.Hex("#b9dfec"));
                for (int y = 1; y < 17; y += 4) g.Fill(2, y, 28, 1, Rgba.Hex("#8fbccd"));
                for (int y = 1; y < 17; y++) for (int x = 2 + (y / 4 % 2) * 2; x < 30; x += 4) g.Set(x, y, Rgba.Hex("#8fbccd"));
                g.Fill(20, 2, 2, 8, WSteel);                                                                     // 관
                g.Oval(21, 10, 4, 2, WSteel); g.Oval(21, 9.5f, 3, 1.2f, WSteelHi);                              // 샤워기
                g.Fill(1, 18, 30, 28, WOut);
                g.Fill(2, 18, 28, 27, Rgba.Hex("#cdd6dd"));                                                     // 받침
                for (int y = 22; y < 45; y += 4) for (int x = 16; x < 29; x += 4) g.Set(x, y, Rgba.Hex("#b4c0c9"));   // 미끄럼 막는 돌기
                g.Oval(22, 32, 3, 2, Rgba.Hex("#56616c")); g.Oval(22, 32, 1.5f, 1, Rgba.Hex("#2f3740"));          // 배수구
                g.Fill(1, 17, 30, 2, WSteelHi); g.Fill(1, 18, 30, 1, WSteel);                                    // 커튼 막대
                Curtain(g, 2, 6);                                                                                // 왼쪽 4칸에 접은 커튼 (아무도 안 쓴다)
                for (int x = 8; x < 30; x += 3) g.Set(x, 18, WSteelHi);                                           // 빈 고리
                break;
            }
        }
        return g;
    }

    /// <summary>커튼 (x0 ~ x1−1 · top ~ 44줄 — 접힌 커튼은 막대 바로 아래 19줄부터): 주름 넷 · 윗단 빛 · 밑단.</summary>
    private static void Curtain(Img g, int x0, int x1, int top = 19)
    {
        for (int x = x0; x < x1; x++)
        {
            g.Fill(x, top, 1, 44 - top, Fold[x % 4]);
            if (x % 4 == 2) g.Set(x, top, Rgba.Hex("#d6f2f1"));
        }
        g.Fill(x0, 43, x1 - x0, 2, Rgba.Hex("#145156"));   // 밑단
    }

    /// <summary>닫힌 커튼의 윗단 (샤워 칸 몸 그림 줄 — 사람 그림의 목줄: 머리 12~22 · 목 23~25 · 몸 26~44줄).</summary>
    private const int CurtainTop = 25;

    /// <summary>쓰는 동안 닫힌 커튼 (샤워 칸 몸 그림 좌표 · 그 사람 바로 뒤에 얹는다): 윗단은 목줄 — 커튼 위로 얼굴까지 머리가 보이고 목 아래 몸은 가린다
    /// (막대 · 고리는 몸 그림 17~18줄에 그대로 — 머리 뒤로 지나간다 · 왼쪽에 접혀 있던 자락은 막대까지 닿아 있다).</summary>
    private static Img ShowerCurtain()
    {
        var g = new Img(T, T + WashRise(3));
        Curtain(g, 2, 30, CurtainTop);
        return g;
    }

    /// <summary>샤워 물줄기 (밸브가 열리고 물이 온다 — 4장): 샤워기(21, 10)에서 받침까지 아래로 흐르는 물방울 줄 · 젖은 받침 빛 · 배수구로 모이는 물결 · 튀는 물.</summary>
    private static Img ShowerStream(int f4)
    {
        var g = new Img(T, T + WashRise(3));
        g.Fill(6, 19, 24, 26, WWet, 0.22f);              // 젖은 받침 (물빛이 비친다)
        g.Oval(22, 32, 5, 2.6f, WSpray, 0.35f);          // 배수구로 모이는 물
        for (int y = 12; y < 36; y++)
        {
            int hw = 2 + (y - 12) / 10;                  // 아래로 갈수록 퍼진다
            for (int x = 21 - hw; x <= 21 + hw; x++)
            {
                int lane = x - 21 + 4;
                if (((y - f4 * 2 + lane * 3) & 7) >= 5) continue;   // 끊어진 물방울 줄 (장마다 2도트씩 내려간다)
                g.Blend(x, y, Math.Abs(x - 21) <= 1 ? WSprayCore : WSpray, 0.9f);
            }
        }
        for (int k = 0; k < 6; k++)                       // 받침에 튀는 물
        {
            int sx = 16 + (k * 5 + f4 * 3) % 11, sy = 33 + (k * 3 + f4) % 4;
            g.Blend(sx, sy, WSprayCore, 0.8f);
        }
        return g;
    }

    /// <summary>세면대 물줄기 (손 씻는 동안 · 물이 온다 — 4장): 수도꼭지(16, 16)에서 대야로 1도트 줄기 · 흔들리는 가장자리 · 대야에 이는 물결.</summary>
    private static Img SinkStream(int f4)
    {
        var g = new Img(T, T + WashRise(2));
        for (int y = 17; y <= 20; y++) g.Set(16, y, WSprayCore);
        g.Set(15 + (f4 & 1) * 2, 18 + (f4 >> 1), WSpray);
        int r = 2 + f4 % 3;                               // 퍼지는 물결
        g.Set(16 - r, 21, WSpray); g.Set(16 + r, 21, WSpray); g.Set(17 - r, 22, WSpray); g.Set(15 + r, 22, WSpray);
        g.Set(16, 21, WSprayCore);
        return g;
    }

    /// <summary>'사용 금지' 빨간 불 2×2 (깜빡이지 않는다 · 빛을 내면 둘레가 붉게).</summary>
    private static void SignLamp(Img img, int x, int y, int size, bool glow)
    {
        if (glow) img.Glow(x + size / 2f, y + size / 2f, Math.Max(2, size * 2.5f), WSign, 0.3f);
        img.Fill(x, y, size, size, WSign);
    }

    /// <summary>샤워 칸에 물이 흐르나 (밸브 · 물관 · 물탱크 — 세계를 읽기만).</summary>
    private bool ShowerFlows(Network? net, int fu) { int d = DeviceOf(net, fu); return d >= 0 && net!.ShowerRunning(d); }

    /// <summary>세면대에서 물이 나오나: 지금 손 씻는 걸음인 이가 있고 물관으로 물이 오고 물탱크가 비지 않았다 (세면대 물은 세계가 손 씻기 끝에 한 번에 꺼낸다 — 8단에서 밸브로).</summary>
    private bool SinkFlows(Network? net, int fu) => _sinkUse != null && _sinkUse[fu] && (net == null || net.WaterAt(fu) && net.WaterTank > 0);

    /// <summary>변기의 '사용 금지' 불 (⑩ 주컴퓨터가 켠 불 — 비트 하나라도 서면).</summary>
    private static bool SignOn(Mind? mind, int fu) => mind != null && fu < mind.WashSign.Length && mind.WashSign[fu] != 0;

    private bool[]? _sinkUse;
    /// <summary>세면대마다 지금 손 씻는 걸음인 이가 있나 (장마다 한 번 · 설비 고리 전에 — 쓰는 이는 곁 칸에 서므로 칸으로 찾지 않는다).</summary>
    private void SinkUsers(Mind? mind)
    {
        if (!_hasSink) return;
        _sinkUse ??= new bool[L.Furniture.Count];
        Array.Clear(_sinkUse);
        if (mind == null) return;
        foreach (var a in mind.Agents)
        {
            if (!a.IsPerson || a.C.StepIndex >= a.Plan.Count) continue;
            var st = a.Plan[a.C.StepIndex];
            if (st.Kind == StepKind.Use && (UseKind)st.Arg == UseKind.Hands && (uint)st.Target < (uint)_sinkUse.Length && _washKind[st.Target] == 2) _sinkUse[st.Target] = true;
        }
    }

    /// <summary>그 사람이 샤워 칸 안에서 씻는 중이면 그 샤워 칸 (아니면 −1): 지금 걸음이 그 샤워 칸의 [샤워]이고 그 칸에 서 있다 — 들어서는 이 · 지나가는 이 · 쓰러진 이 · 로봇은 닫지 않는다.</summary>
    private int ShowerIn(Agent a)
    {
        ref var c = ref a.C;
        if (!a.IsPerson || c.Up == 0 || c.CarriedBy >= 0 || c.StepIndex >= a.Plan.Count) return -1;
        var st = a.Plan[c.StepIndex];
        if (st.Kind != StepKind.Use || (UseKind)st.Arg != UseKind.Shower || (uint)st.Target >= (uint)_washKind.Length || _washKind[st.Target] != 3) return -1;
        var cells = L.Furniture[st.Target].Cells;
        return cells.Length > 0 && cells[0] == c.Cell ? st.Target : -1;
    }

    private Img CurtainImg() => Cached(Key(5, 9210), 0, static _ => ShowerCurtain());
    private Img ShowerStreamImg(int frame) => Cached(Key(5, 9200 + (frame & 3)), frame & 3, static f4 => ShowerStream(f4));
    private Img SinkStreamImg(int frame) => Cached(Key(5, 9220 + (frame & 3)), frame & 3, static f4 => SinkStream(f4));

    /// <summary>화장실 설비의 장마다 덧그림 (32도트 · 굽지 않는다): 샤워 칸 · 세면대 물줄기 · 변기의 '사용 금지' 불 (빛을 켜면 스스로 빛난다). (x, y)는 몸 그림 자리.</summary>
    private void WashOver(Img img, Network? net, Mind? mind, int fu, int wk, int x, int y, int frame)
    {
        if (wk == 3 && ShowerFlows(net, fu)) Put(img, ShowerStreamImg(frame), x, y);
        else if (wk == 2 && SinkFlows(net, fu)) Put(img, SinkStreamImg(frame), x, y);
        else if (wk == 1 && SignOn(mind, fu))
        {
            int lx = x + SignX, ly = y + SignY;
            if (Lighting) _emissive.Add(im => SignLamp(im, lx, ly, 2, true));
            else SignLamp(img, lx, ly, 2, false);
        }
    }

    /// <summary>화장실 설비 칸에 고인 물 (지린 오줌 · 넘친 변기): 설비를 덮지 않고 칸 22~31줄(대야 · 세면대 앞판 아래 — 발치)에 타원 웅덩이 · 짙기 min(0.85, 물/60) · 크기는 물의 양만큼 (약 52몰에 다 자란다).</summary>
    private static void FootPool(Img img, int c, int px, int py, double water, float fade = 1)
    {
        float a = (float)Math.Min(0.85, water / 60) * fade;
        float rx = (float)Math.Min(13, 6 + water / 60 * 8), ry = Math.Min(4.5f, rx * 0.35f);
        float cx = px + T / 2 + (DotHash.F(c, 9, 3) - 0.5f) * 4, cy = py + 26.5f;
        Puddle(img, cx, cy, rx, ry, PoolWater, a);
    }

    /// <summary>설비 칸의 물이 이만큼 넘으면 (웅덩이가 다 자란 뒤 — 물관이 터지거나 넘친 물이 쌓였다) 칸 전체로 번진다: 짙기 (물 − 50) / 40 · 84몰부터 곁 바닥과 같은 0.85.</summary>
    private const double FloodAt = 50;

    /// <summary>화장실 설비 몸 그림 (상태대로 굽는다 — 설비 고리 · 물에 선 설비가 함께 쓴다).</summary>
    private Img WashBodyImg(Life? life, int i)
    {
        var (_, _, bw, bh) = _fixBox[i];
        int wk = _washKind[i], ws = WashState(life, i, wk);
        return Cached(Key(5, 9100 + wk * 16 + ws, bw, bh, 0), (wk, ws), static q => Shadowed(WashBody(q.wk, q.ws), WashRise(q.wk)));
    }

    /// <summary>화장실 설비 칸 c에 고인 물 (칸 자리 px, py — 32도트 Draw와 넓은 보기가 함께 쓴다): 발치 웅덩이 ·
    /// FloodAt몰 넘게 고이면 곁 바닥과 같은 물 타일을 설비 몸이 선 자리만 빼고 깐다 (설비가 물에 서 있다 — 몸 도트의 알파만큼 덜 칠한다 · 물줄기 · 불은 몸 안쪽이라 가리지 않는다).</summary>
    private void WashWater(Img img, Life? life, int c, int px, int py, double water, int frame)
    {
        float a = (float)Math.Clamp((water - FloodAt) / 40, 0, 0.85);
        FootPool(img, c, px, py, water, 1 - a * 0.7f);   // 물이 칸에 차면 몸에 걸친 웅덩이는 옅어진다 (밑동이 물에 잠긴 빛만)
        if (a <= 0) return;
        int i = L.FurnitureAt[c];
        var body = WashBodyImg(life, i);
        var (bx, by, _, _) = _fixBox[i];
        int ox = (L.X(c) - bx) * T, oy = (L.Y(c) - by) * T + WashRise(_washKind[i]);   // 이 칸이 몸 그림에서 놓인 자리
        var tile = Cached(Key(6, frame & 3, c & 7), (f4: frame & 3, c8: c & 7), static q => DotFx.WaterTile(q.f4, q.c8));
        for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                var w = tile.Get(x, y);
                if (w.A == 0) continue;
                float k = a * (1 - body.Get(x + ox, y + oy).A / 255f);
                if (k > 0.004f) img.Blend(px + x, py + y, w, k);
            }
    }
}
