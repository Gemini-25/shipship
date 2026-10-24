using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using ShipEngine;
using ShipSim.Art;

namespace ShipView;

/// <summary>
/// 화장실 · 샤워실 설비의 제 그림 (함장 요청 — 옛 도트에 없는 설비): 변기 · 세면대 · 샤워 칸.
/// 칸 하나 32도트 · 비스듬한 시점 (위로 솟은 만큼 윗벽에 걸친다) · 흰 도기 · 강철 · 유리.
/// 5단 A1 — 세계를 읽는다 (읽기만): 변기 몸은 때 3단 × 막힘 (굽는다) · 장마다 덧그림 — 샤워 칸 물줄기 (밸브 · 물관 · 물탱크) · 세면대 물줄기 (손 씻는 걸음 · 물관) ·
/// 변기의 '사용 금지' 불 (주컴퓨터가 켠 불) · 쓰는 동안 닫힌 샤워 커튼 (그 칸에서 [샤워] 걸음인 사람 — 커튼은 세계 값이 아니다 · 커튼 위로 얼굴까지) ·
/// 설비 칸에 고인 물 (발치 웅덩이 — 많이 고이면 곁 바닥처럼 칸에 물이 차고 설비는 그 물에 선다).
/// 5단 A2 — 세계 습도를 읽는다 (Field.RelHumidity · 장마다 · 굽지 않는다): 뜨거운 물이 흐르는 샤워기 위로 오르는 흰 김 · 습도 0.9 넘는 바닥 칸의 흰 안개 (사람 위에) ·
/// 세면대 칸 습도 0.85 넘으면 거울 김 서림 — 샤워가 끝나도 세계 습도가 빠지는 만큼 오래 남는다 (제습기가 꺼진 화장실은 늘 뿌옇다).
/// A2 검토 고침: 안개는 한 번 셈한 물결 표 · 네 점씩 정수 섞기 · 칸 줄마다 여러 코어 (바닥 모두 습도 0.97인 배의 '김 · 안개' 단계 10.4 · 18.2 · 12.5 → 약 0.6 · 0.8 · 0.7ms) ·
/// 김은 그 방의 공기에만 (벽 뚜껑 · 바깥 · 옆방에 그리지 않는다) · 천장에 닿으면 앞으로 굴러 나온다.
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

    /// <summary>김 · 안개 · 김 서림의 흰 빛 (색표 — 어두움 → 밝음: 김 덩이는 4단 · 빛 받은 속은 5단 · 가장자리는 3단).</summary>
    private static readonly int SteamRamp = Ramps.Add("#9fb1bf", "#c8d5de", "#e3ebf0", "#f2f6f9", "#fbfdfe", "#ffffff");
    private static readonly Rgba FogTint = Rgba.Hex("#f4f8fb");
    /// <summary>김이 한 번 오르는 장 수 (8장에 1초 — 2초에 샤워기에서 두 칸 위까지 오르며 퍼지고 옅어진다).</summary>
    private const int SteamLife = 16;

    /// <summary>김 뭉치 한 장 (size × size — 흰 덩이 셋이 겹친 뭉게구름 · 점묘 가장자리: 연기 뭉치와 같은 꼴을 흰 색표로). 짙기는 얹을 때.</summary>
    private Img SteamPuffImg(int size, int seed) => Cached(Key(5, 9240 + size * 4 + (seed & 3)), (size, s4: seed & 3), static q => DotFx.SmokeSprite(q.size, q.s4, SteamRamp));

    /// <summary>김 (뜨거운 물이 흐르는 샤워 칸 — 밸브 · 물 · 온수기가 돈다): 샤워기(몸 그림 21, 10)에서 흰 뭉치 3~6개(습할수록 많다)가 오르며 커지고 옅어진다 (2초에 두 칸 위까지).
    /// 짙기 0.25 + 0.5 × clamp((습도 − 0.5) / 0.45) — 뭉치를 두른 엷은 겉의 짙기 · 속은 0.55 더 짙다 (갓 나온 김은 어느 방에서나 희다 · 습한 방은 겉까지 짙게 엉긴다) · 오를수록 옅어진다.
    /// 김은 그 샤워 칸 방의 공기에만 얹는다 (<see cref="RoomAir"/> — 벽 뚜껑 · 바깥 · 옆방에는 없다 — A2 검토): 오를 높이는 방에 맞춘다 — 샤워기에서 천장 (위 벽 앞면 위쪽 줄)까지 head도트
    /// (<see cref="SteamHead"/>)를 넘으면 더 오르지 않고 천장을 따라 앞으로 (화면 아래로) 굴러 나온다 (벽에 붙은 샤워 칸은 칸 위로 피어 앞으로 번진다 · 천장에 닿은 윗자락은 잘린다).
    /// (gx, gy)는 몸 그림 자리 (32도트 그림판) · (ox, oy)는 그림판 (0, 0)의 도면 32도트 자리 · k는 줄임 (넓은 보기 16도트 2 · 8도트 4).</summary>
    private void Steam(Img img, double rh, int room, int head, int gx, int gy, int ox, int oy, int frame, int seed, int k = 1)
    {
        float t = (float)Math.Clamp((rh - 0.5) / 0.45, 0, 1), den = 0.25f + 0.5f * t;
        int n = 3 + (int)MathF.Round(3 * t);
        for (int q = 0; q < n; q++)
        {
            float ph = (frame + q * (float)SteamLife / n) % SteamLife / SteamLife;   // 이 뭉치의 나이 (0 샤워기 → 1 사라짐 · 뭉치마다 고르게 어긋난다)
            int size = 10 + 2 * (int)(ph * 6);                                                                 // 10 → 20도트 (2도트씩 — 이웃 뭉치와 겹쳐 한 줄기로)
            float up = 3 + ph * 34, top = head - 2, rise = Math.Min(up, top) - Math.Max(0, up - top) * 0.5f;   // 천장 (head − 2)에 닿으면 남은 만큼의 반을 앞으로 굴러 나온다
            float sway = MathF.Sin(ph * 5f + q * 1.7f) * 2.2f + (DotHash.F(seed, q, 3) - 0.5f) * 8 * ph;   // 흔들리며 옆으로 번진다
            float thin = (1.15f - 0.6f * ph) * (ph < 0.7f ? 1 : 1 - (ph - 0.7f) / 0.3f * 0.9f);              // 오를수록 옅다 · 끝에 사라진다
            float cx = gx + 21.5f + sway, cy = gy + 10 - rise;
            int sd = seed * 7 + q;
            var halo = DownOf(SteamPuffImg(size + 6, sd), k);
            var core = DownOf(SteamPuffImg(size, sd + 1), k);
            PutAir(img, halo, FloorDiv((int)cx - (size + 6) / 2, k), FloorDiv((int)cy - (size + 6) / 2, k), Math.Min(0.95f, den * thin), room, ox, oy, k);
            PutAir(img, core, FloorDiv((int)cx - size / 2, k), FloorDiv((int)cy - size / 2 - 1, k), Math.Min(0.95f, (den + 0.55f) * thin), room, ox, oy, k);   // 짙은 속
        }
    }

    /// <summary>뭉치를 짙기 alpha로 얹되 방 room의 공기로 보이는 도트에만 (<see cref="RoomAir"/> — 도트 가운데의 도면 자리로 본다). (ox, oy)는 그림판 (0, 0)의 도면 32도트 자리.</summary>
    private void PutAir(Img dst, Img src, int dx, int dy, float alpha, int room, int ox, int oy, int k)
    {
        int x0 = Math.Max(0, -dx), y0 = Math.Max(0, -dy), x1 = Math.Min(src.W, dst.W - dx), y1 = Math.Min(src.H, dst.H - dy);
        for (int y = y0; y < y1; y++)
        {
            int wy = (dy + y) * k + oy + k / 2;
            for (int x = x0; x < x1; x++)
            {
                var c = src.Get(x, y);
                if (c.A == 0 || !RoomAir(room, (dx + x) * k + ox + k / 2, wy)) continue;
                dst.Blend(dx + x, dy + y, c, alpha);
            }
        }
    }

    /// <summary>샤워 칸 sc의 샤워기(칸 위 4도트)에서 천장까지 화면 도트: 바로 위가 같은 방 바닥 칸이 아니면 위 벽 앞면 위쪽 줄까지 10 · 한 칸이면 42 · 두 칸 넘게면 74 (김이 다 올라도 닿지 않는다).</summary>
    private int SteamHead(int sc)
    {
        int x = L.X(sc), y = L.Y(sc), room = L.Room[sc], up = 0;
        while (up < 2 && L.In(x, y - 1 - up) && L.Kind[L.Index(x, y - 1 - up)] == CellKind.Floor && L.Room[L.Index(x, y - 1 - up)] == room) up++;
        return T - ArtWalls.CapH - 4 + up * T;   // 샤워기 (샤워 칸 칸 위 4도트)에서 위 벽 칸의 뚜껑 끝 (그 칸 위에서 18도트)까지
    }

    /// <summary>도면 32도트 자리 (wx, wy)가 방 room의 공기로 보이는 곳인가 (비스듬한 시점): 그 방 바닥 · 설비 칸 · 그 방 바닥을 바로 아래에 둔 벽 · 문 칸의 앞면 (칸 아래 14도트 — 천장 높이까지).
    /// 벽 뚜껑 · 바깥 · 옆방 바닥은 아니다.</summary>
    private bool RoomAir(int room, int wx, int wy)
    {
        int cx = wx >> 5, cy = wy >> 5;   // T = 32 (음수 자리도 내림)
        if (!L.In(cx, cy)) return false;
        int c = L.Index(cx, cy);
        if (L.Kind[c] == CellKind.Floor) return L.Room[c] == room;
        if (L.Kind[c] is not (CellKind.Wall or CellKind.Door) || (wy & (T - 1)) < ArtWalls.CapH || !L.In(cx, cy + 1)) return false;
        int b = c + L.W;
        return L.Kind[b] == CellKind.Floor && L.Room[b] == room;
    }

    /// <summary>안개 · 거울 김 서림이 서는 습도 (넘으면 — 관찰 장면 · 그리기 예산도 같은 값으로 센다).</summary>
    public const double FogRh = 0.9, MirrorRh = 0.85;
    /// <summary>포화 수증기압의 1K 표 (250 ~ 499K · <see cref="Water.SatKPa"/>): 칸 온도의 내림 자리 값 — 포화 수증기압은 온도가 오를수록 크니 그 칸의 값보다 작거나 같다.</summary>
    private static readonly double[] SatFloor = Enumerable.Range(250, 250).Select(t => Water.SatKPa(t)).ToArray();

    /// <summary>안개를 볼 칸의 상대 습도 (장마다 칸마다 — 먼저 exp · 나눗셈 여럿 없이 거른다 · <see cref="FogSieve"/>): 습도가 FogRh를 넘을 수 없는 칸은 0 ·
    /// 나머지는 수증기 분압 ÷ 포화 수증기압 (<see cref="Field.RelHumidity"/>와 같은 식 — 열용량을 성분 넷씩 함께 더하는 차례만 달라 1e-15 안쪽) · 기체가 거의 없거나 표 밖 온도 (드물다)는 RelHumidity 그대로.
    /// (예산 고침 — 바닥 칸마다 RelHumidity (나눗셈 여럿 · exp)를 셈했다: 평시 판 '김 · 안개' 단계 16 · 8도트 0.11~0.16 · 0.15~0.22 → 0.06~0.07 · 0.06~0.08ms ·
    /// 바닥 모두 안개인 배의 훑기는 거름 뒤 RelHumidity를 또 부르던 것보다 0.18 · 0.22 → 0.11 · 0.15ms)</summary>
    private double FogRhAt(Field f, int c)
    {
        if (NoFogSieve) return f.RelHumidity(c);
        if (!FogSieve(f, c, out double p, out double t)) return 0;   // 거의 모든 칸이 여기서 빠진다
        return double.IsNaN(t) ? f.RelHumidity(c) : p / Water.SatKPa(t);
    }

    /// <summary>안개 칸 거름: false면 습도가 FogRh를 넘을 수 없다 (수증기 분압 ≤ FogRh × 내림 온도의 포화 수증기압 — 맺음 끝 1e-9) · true면 p (수증기 분압 kPa) · t (기체 온도 K)를 건넨다
    /// (기체가 거의 없거나 표 밖 온도면 t = NaN — RelHumidity로). 따로 둔 메서드 — 256비트 셈이 끝나면 위쪽 절반을 비우고 돌아간다 (같은 메서드 안에서 이어 부른 exp는 SSE 전환 값으로 몇 배 느렸다).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool FogSieve(Field f, int c, out double p, out double t)
    {
        var cv = _gasCv ??= f.T.Gases.Select(g => g.C).ToArray();   // Field의 성분별 cv와 같은 표
        int G = f.G, g0 = 0;
        ref double nc = ref f.N[c * G];
        double gc = 0;
        if (Vector256.IsHardwareAccelerated && !NoVector)
        {
            var acc = Vector256<double>.Zero;
            ref double cv0 = ref cv[0];
            for (; g0 + 4 <= G; g0 += 4) acc += Vector256.LoadUnsafe(ref nc, (nuint)g0) * Vector256.LoadUnsafe(ref cv0, (nuint)g0);
            gc = Vector256.Sum(acc);
        }
        for (; g0 < G; g0++) gc += Unsafe.Add(ref nc, g0) * cv[g0];
        p = t = double.NaN;
        if (!(gc > 2e-6)) return true;   // 기체가 거의 없다 (RelHumidity는 1e-6 아래면 바닥 온도 — 그 언저리는 그대로 셈한다) · NaN
        double tk = f.U[c] / gc;
        if (!(tk >= 250 && tk < 499)) return true;
        t = tk;
        p = f.N[c * G + f.T.gH2O] * Field.R * tk / Field.CellVolume / 1000;   // 수증기 분압 kPa (= 압력 × 몰 분율 — 셈 차례만 다르다)
        return p > FogRh * SatFloor[(int)tk - 250] * (1 - 1e-9);
    }
    private double[]? _gasCv;
    /// <summary>시험용: 안개 칸 거름을 끈다 (칸마다 RelHumidity — 거름 · 빠른 습도 셈이 그림을 바꾸지 않는지 견준다).</summary>
    public static bool NoFogSieve;

    /// <summary>이 장에 안개를 칠할 칸 (칸 x · 짙기 /256 — 칸 줄 차례) · 칸 줄 (첫 자리 · 칸 수 · y): 습도는 한 스레드로 훑고 (칸마다 한 번 · 싸다) 칠하기만 나눈다.</summary>
    private readonly List<(int x, uint aq)> _fog = new();
    private readonly List<(int start, int n, int y)> _fogRows = new();
    /// <summary>안개 칸이 이만큼 넘으면 칸 줄마다 여러 코어에서 칠한다 (적으면 한 스레드가 싸다 — 나누는 값이 칠하는 값보다 크다).</summary>
    private const int FogPar = 24;

    /// <summary>안개 물결 짙기 표 (0.6 ~ 1.1을 256배한 정수 · 256도트 = 8칸 주기 — 가로는 도트 · 세로는 4도트 덩이 64줄): 한 번 셈해 둔다 (장마다 덩이마다 sin 셋을 부르던 것 — A2 검토).</summary>
    private static readonly int[] FogWave = MakeFogWave();
    private static int[] MakeFogWave()
    {
        var w = new int[256 * 64];
        const float K = MathF.PI * 2 / 256;   // 물결 수는 8칸에 정수 번 (칸 경계 · 표 끝에서 이어진다)
        for (int by = 0; by < 64; by++)
            for (int x = 0; x < 256; x++)
            {
                float y = by * 4;
                w[by * 256 + x] = (int)MathF.Round(256 * (0.85f + 0.15f * MathF.Sin(x * 4 * K + MathF.Sin(y * 4 * K) * 1.6f) + 0.1f * MathF.Sin(y * 3 * K - x * 2 * K)));
            }
        return w;
    }

    /// <summary>안개 빛 (FogTint)을 한 줄 토막 (그림판 py줄 · px0 + xa .. px0 + xb − 1)에 섞는다 — 점 x의 짙기는 덩이 짙기 wb[x ≫ sh] / 256 (덩이 하나 = 2^sh 도트):
    /// 불투명한 바탕은 정수로 (AVX2면 여덟 점씩 — 덩이 짙기를 한 번 읽어 여덟 점으로 편다 · 아니면 네 점씩 · 한 점씩과 비트까지 같다 · <see cref="Img.Blend"/>와는 1 안쪽 차) ·
    /// 반투명 바탕 (드물다)은 예전처럼 <see cref="Img.Blend"/>. (예산 고침 — 네 점마다 덩이 짙기를 하나씩 모아 만들던 것: 같은 판에서 번갈아 잰 '김 · 안개' 단계 16 · 8도트 0.82~1.02 · 0.66~0.91 → 0.66~0.77 · 0.48~0.75ms)</summary>
    private static void VeilRow(Img img, Span<uint> d, int px0, int py, ReadOnlySpan<uint> wb, int sh, int xa, int xb)
    {
        uint crb = FogTint.R | (uint)FogTint.B << 16, cg = FogTint.G;
        int at = py * img.W + px0, x = xa;
        if (Avx2.IsSupported && !NoVector && xb - xa >= 8)
        {
            for (; x < xb && (x & 7) != 0; x++) VeilDot(img, d, at + x, px0 + x, py, wb[x >> sh], crb, cg);   // 여덟 점 자리에 맞춘다 (왼쪽 끝이 잘린 칸)
            Vector256<uint> vRB = Vector256.Create(0x00FF00FFu), vA = Vector256.Create(0xFF000000u), vFF = Vector256.Create(0xFFu), v256 = Vector256.Create(256u), vcrb = Vector256.Create(crb), vcg = Vector256.Create(cg);
            Vector256<uint> spread = sh == 1 ? Vector256.Create(0u, 0, 1, 1, 2, 2, 3, 3) : Vector256.Create(0u, 0, 0, 0, 1, 1, 1, 1);   // 덩이 → 점 (2 · 4점 덩이)
            ref uint row = ref d[at];
            ref uint wr = ref MemoryMarshal.GetReference(wb);
            for (; x + 8 <= xb; x += 8)
            {
                var q = Vector256.LoadUnsafe(ref row, (nuint)x);
                if (!Vector256.EqualsAll(q & vA, vA)) { for (int j = x; j < x + 8; j++) VeilDot(img, d, at + j, px0 + j, py, wb[j >> sh], crb, cg); continue; }
                var w = sh == 0 ? Vector256.LoadUnsafe(ref wr, (nuint)x)
                    : sh == 1 ? Avx2.PermuteVar8x32(Vector128.LoadUnsafe(ref wr, (nuint)(x >> 1)).ToVector256Unsafe(), spread)   // 덩이 넷
                    : Avx2.PermuteVar8x32(Vector128.CreateScalarUnsafe(Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<uint, byte>(ref Unsafe.Add(ref wr, x >> 2)))).AsUInt32().ToVector256Unsafe(), spread);   // 덩이 둘
                var inv = v256 - w;
                ((((q & vRB) * inv + vcrb * w) >> 8 & vRB) | (((q >> 8) & vFF) * inv + vcg * w) >> 8 << 8 | vA).StoreUnsafe(ref row, (nuint)x);
            }
        }
        else if (Vector128.IsHardwareAccelerated && !NoVector)
        {
            for (; x < xb && (x & 3) != 0; x++) VeilDot(img, d, at + x, px0 + x, py, wb[x >> sh], crb, cg);   // 네 점 자리에 맞춘다
            Vector128<uint> vRB = Vector128.Create(0x00FF00FFu), vA = Vector128.Create(0xFF000000u), vFF = Vector128.Create(0xFFu), v256 = Vector128.Create(256u), vcrb = Vector128.Create(crb), vcg = Vector128.Create(cg);
            ref uint row = ref d[at];
            for (; x + 4 <= xb; x += 4)
            {
                var q = Vector128.LoadUnsafe(ref row, (nuint)x);
                if (!Vector128.EqualsAll(q & vA, vA)) { for (int j = x; j < x + 4; j++) VeilDot(img, d, at + j, px0 + j, py, wb[j >> sh], crb, cg); continue; }
                var w = sh == 2 ? Vector128.Create(wb[x >> 2]) : sh == 1 ? Vector128.Create(wb[x >> 1], wb[x >> 1], wb[(x >> 1) + 1], wb[(x >> 1) + 1]) : Vector128.Create(wb[x], wb[x + 1], wb[x + 2], wb[x + 3]);
                var inv = v256 - w;
                ((((q & vRB) * inv + vcrb * w) >> 8 & vRB) | (((q >> 8) & vFF) * inv + vcg * w) >> 8 << 8 | vA).StoreUnsafe(ref row, (nuint)x);
            }
        }
        for (; x < xb; x++) VeilDot(img, d, at + x, px0 + x, py, wb[x >> sh], crb, cg);
    }

    /// <summary>한 점 (d[i] — 그림판 (px, py)): 불투명하면 정수로 섞고 아니면 <see cref="Img.Blend"/>.</summary>
    private static void VeilDot(Img img, Span<uint> d, int i, int px, int py, uint w, uint crb, uint cg)
    {
        uint q = d[i], inv = 256 - w;
        if (q >> 24 != 255) { img.Blend(px, py, FogTint, w / 256f); return; }
        d[i] = ((q & 0x00FF00FFu) * inv + crb * w) >> 8 & 0x00FF00FFu | (((q >> 8) & 0xFFu) * inv + cg * w) >> 8 << 8 | 0xFF000000u;
    }

    /// <summary>이 장의 물결 (흐른 만큼 민 표 — 덩이 줄 64 × (칸 x mod 8 · 덩이 8) 64): 칸 x의 덩이 bx는 [줄 · 64 + (x & 7) · 8 + bx] (흐름이 바뀔 때만 다시 셈한다).</summary>
    private readonly int[] _fogWave = new int[64 * 64];
    private int _fogDrift = -1;

    /// <summary>습한 방의 안개 (습도 0.9 넘는 바닥 칸): 흰 막 짙기 0.25 × (습도 − 0.9) / 0.1 · 4도트 덩이마다 배 자리의 부드러운 물결 (<see cref="FogWave"/>)로 짙기가 달라 천천히 옆으로 흐른다
    /// (칸 경계에서 이어진다 · 사람 위에 덮는다). 한 칸 줄 y의 안개 칸 (_fog[start .. start + n) — x 차례)을 그림판 줄째로 칠한다: 덩이 짙기만 셈하고 (점마다 펴지 않는다 — 칠하기가 덩이를 읽어 편다)
    /// 이웃한 안개 칸은 한 토막으로 이어 칠한다 (칸 줄마다 여러 코어에서 부른다). (x0, y0)은 보는 창 · k는 줄임.</summary>
    private void FogRow(Img img, int start, int n, int y, int x0, int y0, int k)
    {
        int D = T / k, sh = k == 1 ? 2 : k == 2 ? 1 : 0, py0 = (y - y0) * D;   // 덩이 하나 = 4 / k 도트 (2^sh)
        Span<uint> wb = n * 8 <= 2048 ? stackalloc uint[n * 8] : new uint[n * 8];   // 칸마다 이 덩이 줄의 덩이 짙기 (/256)
        var d = MemoryMarshal.Cast<byte, uint>(img.Px.AsSpan());
        var fog = CollectionsMarshal.AsSpan(_fog).Slice(start, n);
        bool vec = Vector256.IsHardwareAccelerated && !NoVector;
        var half = Vector256.Create(128u);   // 반올림
        ref uint wbr = ref MemoryMarshal.GetReference(wb);
        ref int wave = ref _fogWave[0];
        for (int by = 0; by < 8; by++)
        {
            int row = ((y * 8 + by) & 63) * 64;
            for (int j = 0; j < n; j++)
            {
                var (x, aq) = fog[j];
                int wv = row + (x & 7) * 8;
                if (vec) ((Vector256.LoadUnsafe(ref wave, (nuint)wv).AsUInt32() * aq + half) >> 8).StoreUnsafe(ref wbr, (nuint)(j * 8));   // 덩이 여덟을 한 번에 (같은 정수 셈)
                else for (int bx = 0; bx < 8; bx++) wb[j * 8 + bx] = (aq * (uint)_fogWave[wv + bx] + 128) >> 8;
            }
            for (int r = 0; r < 1 << sh; r++)
            {
                int py = py0 + (by << sh) + r;
                if ((uint)py >= (uint)img.H) continue;
                for (int j = 0; j < n;)
                {
                    int j1 = j + 1, cx = fog[j].x;
                    while (j1 < n && fog[j1].x == cx + (j1 - j)) j1++;   // 이웃한 안개 칸 토막
                    int px0 = (cx - x0) * D, xa = Math.Max(0, -px0), xb = Math.Min((j1 - j) * D, img.W - px0);
                    if (xa < xb) VeilRow(img, d, px0, py, wb.Slice(j * 8, (j1 - j) * 8), sh, xa, xb);
                    j = j1;
                }
            }
        }
    }

    /// <summary>거울 김 서림 (세면대 칸 습도 0.85 넘게): 거울 사각(몸 그림 7..24, 1..9)에 흰 덮개 짙기 0.6 × (습도 − 0.85) / 0.15 · 맺힌 물이 흘러내린 줄 셋은 옅다 (32도트만 — 넓은 보기는 고른 덮개).
    /// (gx, gy)는 몸 그림 자리 (32도트 그림판) · k는 줄임.</summary>
    private static void MirrorFog(Img img, double rh, int fu, int gx, int gy, int k = 1)
    {
        float a = 0.6f * (float)Math.Min(1, (rh - MirrorRh) / 0.15);
        if (a <= 0.004f) return;
        if (k > 1)
        {
            int fx0 = FloorDiv(gx + 7, k), fy0 = FloorDiv(gy + 1, k);
            img.Fill(fx0, fy0, FloorDiv(gx + 25, k) - fx0, FloorDiv(gy + 10, k) - fy0, FogTint, a);
            return;
        }
        Span<int> dx = stackalloc int[3], dy = stackalloc int[3];
        for (int d = 0; d < 3; d++) { dx[d] = 8 + (int)(DotHash.F(fu, d, 5) * 16); dy[d] = 2 + (int)(DotHash.F(fu, d, 6) * 4); }
        for (int y = 1; y < 10; y++)
            for (int x = 7; x < 25; x++)
            {
                bool drip = x == dx[0] && y >= dy[0] || x == dx[1] && y >= dy[1] || x == dx[2] && y >= dy[2];
                img.Blend(gx + x, gy + y, FogTint, drip ? a * 0.35f : a);
            }
    }

    /// <summary>김 · 안개 · 거울 김 서림 (장마다 — 32도트 Draw와 넓은 보기가 '떠 있는 물방울' 층 뒤에 함께 쓴다 · 습도는 이어지는 값이라 굽거나 캐시 열쇠에 넣지 않는다):
    /// 거울 김 서림 → 바닥 칸 안개 (습도는 한 스레드로 훑고 · 칠하기는 칸 줄마다 여러 코어 — 제 줄만 칠한다) → 샤워기 김 (그 방의 공기에만). 칸 (x, y)의 그림판 자리는 ((x − x0) · 32, (y − y0) · 32) / k.</summary>
    private void WashAir(Img img, Field f, Network? net, int x0, int y0, int w, int h, int frame, int k = 1)
    {
        foreach (int i in _washFix)
        {
            if (_washKind[i] != 2) continue;
            var (bx, by, bw, bh) = _fixBox[i];
            int kc = L.Furniture[i].Cells[0];
            if (bx + bw < x0 || by + bh < y0 || bx > x0 + w || by > y0 + h + 1 || !Mine(kc)) continue;
            double rh = f.RelHumidity(kc);
            if (rh > MirrorRh) MirrorFog(img, rh, i, (bx - x0) * T, (by - y0) * T - WashRise(2), k);
        }
        _fog.Clear(); _fogRows.Clear();
        var kind = L.Kind;
        int xa = Math.Max(0, x0), xe = Math.Min(L.W, x0 + w);   // 도면 안 칸만 (넓은 보기 창은 도면 밖 한 칸까지)
        for (int y = Math.Max(0, y0); y < Math.Min(L.H, y0 + h); y++)   // 칸 줄마다 안개 칸을 모은다
        {
            int start = _fog.Count;
            for (int x = xa; x < xe; x++)
            {
                int c = y * L.W + x;
                if (kind[c] is not (CellKind.Floor or CellKind.Door) || !Mine(c)) continue;
                if (L.Kind[c] == CellKind.Door && L.DoorAt[c] >= 0 && f.DoorOpen[L.DoorAt[c]] < 0.05f) continue;   // 닫힌 문 칸은 기체 칸이 아니다
                double rh = FogRhAt(f, c);
                if (rh <= FogRh) continue;
                uint aq = (uint)MathF.Round(64 * (float)Math.Min(1, (rh - FogRh) / 0.1));   // 짙기 0.25 × (습도 − 0.9) / 0.1 (/256)
                if (aq > 1) _fog.Add((x, aq));
            }
            if (_fog.Count > start) _fogRows.Add((start, _fog.Count - start, y));
        }
        if (_fog.Count > 0)
        {
            int drift = (int)(frame * 0.35f);   // 안개가 흐른 도트 (8장에 약 3도트)
            if (drift != _fogDrift)
            {
                _fogDrift = drift;
                for (int r = 0; r < 64; r++) for (int i = 0; i < 64; i++) _fogWave[r * 64 + i] = FogWave[r * 256 + ((drift + (i >> 3) * T + (i & 7) * 4) & 255)];
            }
            void Rows(int i) { var (start, n, y) = _fogRows[i]; FogRow(img, start, n, y, x0, y0, k); }
            if (_fog.Count >= FogPar) Parallel.For(0, _fogRows.Count, Rows);   // 칸 줄마다 제 줄만 칠한다
            else for (int i = 0; i < _fogRows.Count; i++) Rows(i);
        }
        if (net == null) return;
        foreach (int i in _washFix)
        {
            if (_washKind[i] != 3) continue;
            var (bx, by, bw, bh) = _fixBox[i];
            int sc = L.Furniture[i].Cells[0];
            if (bx + bw < x0 || by + bh < y0 || bx > x0 + w || by > y0 + h + 2 || !Mine(sc)) continue;   // 김은 두 칸 위까지 오른다 (화면 아래 두 줄의 샤워 칸 김도)
            int d = DeviceOf(net, i);
            if (d < 0 || !net.ShowerRunning(d) || !net.Working(d)) continue;   // 찬물 (온수기가 꺼졌다 · 전기가 없다)은 김이 없다
            Steam(img, f.RelHumidity(sc), L.Room[sc], SteamHead(sc), (bx - x0) * T, (by - y0) * T - WashRise(3), x0 * T, y0 * T, frame, i, k);
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
