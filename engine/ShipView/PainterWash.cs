using ShipSim.Art;

namespace ShipView;

/// <summary>
/// 화장실 · 샤워실 설비의 제 그림 (함장 요청 — 옛 도트에 없는 설비): 변기 · 세면대 · 샤워 칸.
/// 칸 하나 32도트 · 비스듬한 시점 (위로 솟은 만큼 윗벽에 걸친다) · 흰 도기 · 강철 · 유리.
/// </summary>
public sealed partial class Painter
{
    /// <summary>설비 종류 → 제 그림 번호 (0 = 옛 도트로).</summary>
    private static int WashKind(string type) => type switch { "Toilet" => 1, "Sink" => 2, "Shower" => 3, _ => 0 };

    /// <summary>위로 솟은 도트 (변기 물통 · 세면대 거울 · 샤워 칸 유리벽).</summary>
    private static int WashRise(int kind) => kind switch { 1 => 6, 2 => 10, _ => 14 };

    private static readonly Rgba WOut = Rgba.Hex("#5d6873"), WPorc = Rgba.Hex("#eef3f6"), WPorcSide = Rgba.Hex("#c4ced7"), WPorcDeep = Rgba.Hex("#9eabb7"),
        WWater = Rgba.Hex("#8fc0dc"), WSteel = Rgba.Hex("#7f8fa0"), WSteelHi = Rgba.Hex("#b3c1cd"), WGlass = Rgba.Hex("#a8dcea"), WTile = Rgba.Hex("#dbe4ea"), WGrout = Rgba.Hex("#aebbc5");

    private static Img WashBody(int kind)
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
                g.Oval(cx, cy + 0.5f, 3.5f, 4.5f, WWater);                                                     // 안
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
            default:  // 샤워 칸 (푸른 타일 뒷벽 · 샤워기 · 받침 · 반쯤 친 커튼)
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
                for (int x = 2; x < 16; x++)                                                                     // 반쯤 친 커튼 (주름)
                {
                    var c = (x % 4) switch { 0 => Rgba.Hex("#1f7d86"), 1 => Rgba.Hex("#36aab1"), 2 => Rgba.Hex("#5cc4c6"), _ => Rgba.Hex("#2c9199") };
                    g.Fill(x, 19, 1, 25, c);
                    if (x % 4 == 2) g.Set(x, 19, Rgba.Hex("#d6f2f1"));
                }
                g.Fill(2, 43, 14, 2, Rgba.Hex("#145156"));                                                       // 커튼 밑단
                break;
            }
        }
        return g;
    }
}
