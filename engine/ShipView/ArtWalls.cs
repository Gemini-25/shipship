using ShipSim.Art;

namespace ShipView;

/// <summary>
/// 새 벽 · 문 · 선체 그림 (docs/GRAPHICS.md 텍스처 T2 · T3 · T7 — 함장 요청 10-23 '텍스처가 너무 구려'): 세계는 읽지 않고 모양 값만 받는다.
/// 벽은 서 있다: 벽 칸 위 18도트는 어두운 뚜껑 · 아래가 바닥이면 앞면 24도트 (벽 칸 아래 14 + 바닥 칸 위 10 — 사람 선 키 약 36의 2/3).
/// 앞면 (위에서 아래로): 밝은 모서리 1 · 처마 그늘 2 · 판 12 (48도트 판 · 이음 · 볼트) · 꾸밈 띠 3 (띠등 · 관 · 전선 · 환기구) · 판 1 · 방 종류 띠 1 · 굽도리 4.
/// 선체: 볼록 모서리는 사선으로 깎고 · 바깥으로 트인 쪽엔 밝은 테 · 아래가 우주면 바깥 면 8도트 (어둡게 — 빛을 받지 않는다).
/// 값 위계: 우주 5~15 · 뚜껑 15~35 · 바닥 45~85 · 앞면 55~100 · 사람 120~230 (그림자 쪽은 푸른 보라 · 밝은 쪽은 조금 따뜻하게).
/// </summary>
public static class ArtWalls
{
    public const int T = 32, CapH = 18, FaceH = 24, FaceLow = FaceH - (T - CapH), SkinH = 8, Bevel = 11;

    private static Rgba H(string s) => Rgba.Hex(s);

    // 뚜껑 (0 가장 어둠 · 4 트인 쪽 테)
    private static readonly Rgba[] CapPanel = { H("#101218"), H("#171a21"), H("#1e222a"), H("#272c36"), H("#3a4150") };
    private static readonly Rgba[] CapPart = { H("#121419"), H("#1a1d24"), H("#22262e"), H("#2c313b"), H("#434a58") };
    private static readonly Rgba[] CapHull = { H("#0d0f14"), H("#13161c"), H("#1a1e25"), H("#232831"), H("#4a5363") };
    // 앞면 (0 가장 어둠 · 6 모서리 빛)
    private static readonly Rgba[] FacePanel = { H("#1f222a"), H("#2b2f38"), H("#383d48"), H("#454b57"), H("#535a67"), H("#666e7c"), H("#8a93a2") };
    private static readonly Rgba[] FacePart = { H("#22242b"), H("#2f323a"), H("#3d4049"), H("#4b4f59"), H("#5a5e69"), H("#6e737e"), H("#9298a4") };
    private static readonly Rgba[] FaceHull = { H("#1a1d23"), H("#24282f"), H("#2f343c"), H("#3b414a"), H("#484f59"), H("#5a626d"), H("#7a8390") };
    private static readonly Rgba[] Skin = { H("#07080b"), H("#0e1015"), H("#151820"), H("#1d212a"), H("#2a303b"), H("#3e4654") };
    private static readonly Rgba Lens = H("#9aa6b6"), Brass = H("#8c7a55"), CableA = H("#14161b"), CableB = H("#3a2f22");

    public static Rgba[] CapOf(DotWall k) => k switch { DotWall.Partition => CapPart, DotWall.Hull => CapHull, _ => CapPanel };
    public static Rgba[] FaceOf(DotWall k) => k switch { DotWall.Partition => FacePart, DotWall.Hull => FaceHull, _ => FacePanel };

    /// <summary>방 종류 띠 (굽도리 위 1도트 — 글 없이 방을 가늠한다): 생활 · 의료 · 함교 · 기관 · 재배 · 생명 유지 · 창고 · 연구.</summary>
    public static int BandOf(string type) => type switch
    {
        "Quarters" or "PrivateCabins" or "QuietQuarters" or "Lounge" or "Chapel" or "Meditation" or "Theater" or "School" or "WaterWallCabin" or "Observatory" => 1,
        "Mess" or "Galley" or "Freezer" => 2,
        "Medbay" or "Triage" or "Quarantine" or "QuarantineLock" or "Morgue" or "Hyperbaric" or "Decon" => 3,
        "Bridge" or "BackupBridge" or "Comms" or "Navigation" or "ComputerRoom" or "ServerRoom" or "MeetingRoom" or "Archive" => 4,
        "Engine" or "Reactor" or "Cooling" or "Power" or "Substation" or "BatteryRoom" or "FuelCell" or "PropellantTank" or "HeatStorage" or "Foundry" or "Workshop" or "Crusher" => 5,
        "Hydroponics" or "AlgaeLab" or "ProteinFarm" or "SeedVault" or "MushroomFarm" => 6,
        "LifeSupport" or "WaterPlant" or "HvacRoom" or "PumpRoom" or "Recycling" or "GasStorage" or "Washroom" or "Laundry" => 7,
        "Storage" or "Cargo" or "Hangar" or "Airlock" or "Dock" or "SuitRoom" => 8,
        "Lab" or "ElectronicsLab" or "Calibration" => 9,
        _ => 0,
    };
    private static readonly Rgba[] Bands =
    {
        H("#59616e"), H("#7c4157"), H("#a8682c"), H("#2f8f88"), H("#3d6aa8"), H("#a47b26"), H("#4f8a3c"), H("#2f7f96"), H("#857452"), H("#6a54a0"),
    };

    /// <summary>
    /// 벽 칸 (32×32): 어두운 뚜껑 · 트인 쪽 테 · 벽 줄 따라 가운데 홈.
    /// mask: 1 북 · 2 동 · 4 남 · 8 서 (그쪽도 벽 · 문) · faceBelow: 아래가 바닥 (뚜껑은 위 18도트 — 그 아래는 앞면이 덮는다)
    /// out8 (선체만): 바깥(우주)인 이웃 — 1 북 · 2 북동 · 4 동 · 8 남동 · 16 남 · 32 남서 · 64 서 · 128 북서 → 볼록 모서리는 사선으로 깎는다 (투명).
    /// </summary>
    public static Img Cap(DotWall kind, int mask, bool faceBelow, int out8, int variant)
    {
        var o = new Img(T, T);
        var c = CapOf(kind);
        bool n = (mask & 1) != 0, e = (mask & 2) != 0, s = (mask & 4) != 0, w = (mask & 8) != 0;
        int bottom = faceBelow ? CapH : T;
        o.Fill(0, 0, T, T, c[2]);
        if (faceBelow) o.Fill(0, CapH, T, T - CapH, c[1]);   // 앞면이 덮을 자리 (구멍 난 벽이면 그대로 보인다)
        bool horiz = (e || w) && !(n || s);
        if (kind == DotWall.Hull)
        {
            // 16도트 판 이음 · 모서리 리벳
            for (int i = 0; i < T; i++)
            {
                if (i < bottom) { o.Set(0, i, c[1]); o.Set(16, i, c[1]); o.Set(17, i, c[3]); }
                o.Set(i, 0, c[1]); o.Set(i, 16, c[1]); if (17 < bottom) o.Set(i, 17, c[3]);
            }
            foreach (int py in new[] { 3, 13, 19, 29 })
                foreach (int px in new[] { 3, 13, 19, 29 })
                    if (py < bottom - 1) { o.Set(px, py, c[3]); o.Set(px + 1, py + 1, c[0]); }
        }
        else
        {
            // 벽 줄 따라 가운데 홈 (가로 벽 · 세로 벽) — 칸막이는 두 줄
            int mid = faceBelow ? 8 : 15;
            if (horiz || !(n || s)) { for (int x = 0; x < T; x++) { o.Set(x, mid, c[1]); o.Set(x, mid + 1, c[3]); if (kind == DotWall.Partition) { o.Set(x, mid - 3, c[1]); o.Set(x, mid - 2, c[3]); } } }
            if (n || s) for (int y = 0; y < bottom; y++) { o.Set(15, y, c[1]); o.Set(16, y, c[3]); }
            // 작은 결 (칸마다 다르게 — 되풀이가 덜 보이게)
            for (int k = 0; k < 3; k++)
            {
                uint u = DotHash.U(variant, k, 91);
                int px = 2 + (int)(u % (T - 4)), py = 2 + (int)((u >> 8) % (uint)Math.Max(1, bottom - 4));
                o.Set(px, py, (u >> 16) % 2 == 0 ? c[3] : c[1]);
            }
        }
        // 트인 쪽 테: 위 · 왼쪽은 빛을 받고 · 오른쪽 · 아래는 그늘 (두께 1~2)
        for (int i = 0; i < T; i++)
        {
            if (!n) { o.Set(i, 0, c[4]); o.Set(i, 1, c[3]); }
            if (!w && i < bottom) { o.Set(0, i, c[4]); o.Set(1, i, c[3]); }
            if (!e && i < bottom) { o.Set(T - 1, i, c[0]); o.Set(T - 2, i, c[1]); }
            if (!s && !faceBelow) { o.Set(i, T - 1, c[0]); o.Set(i, T - 2, c[1]); }
        }
        if (faceBelow) for (int x = 0; x < T; x++) o.Set(x, CapH - 1, c[0]);   // 뚜껑 앞 끝 (앞면 모서리 빛과 갈린다)
        // 선체 볼록 모서리: 사선으로 깎고 사선 테를 밝게
        if (kind == DotWall.Hull && out8 != 0)
        {
            bool oN = (out8 & 1) != 0, oE = (out8 & 4) != 0, oS = (out8 & 16) != 0, oW = (out8 & 64) != 0;
            if (oN && oW) Cut(o, 0, c[4]);
            if (oN && oE) Cut(o, 1, c[4]);
            if (oS && oW && !faceBelow) Cut(o, 2, c[3]);
            if (oS && oE && !faceBelow) Cut(o, 3, c[3]);
        }
        return o;
    }

    /// <summary>모서리 사선 깎기 (0 왼쪽 위 · 1 오른쪽 위 · 2 왼쪽 아래 · 3 오른쪽 아래): 깎인 자리는 투명 · 사선 테 1도트 + 안쪽 그늘.</summary>
    private static void Cut(Img o, int corner, Rgba rim)
    {
        for (int y = 0; y < T; y++)
            for (int x = 0; x < T; x++)
            {
                int ax = corner is 0 or 2 ? x : T - 1 - x, ay = corner is 0 or 1 ? y : T - 1 - y;
                int d = ax + ay;
                if (d < Bevel) o.Set(x, y, new Rgba(0, 0, 0, 0));
                else if (d == Bevel) o.Set(x, y, rim);
                else if (d == Bevel + 1) o.Blend(x, y, H("#000000"), 0.25f);
            }
    }

    /// <summary>
    /// 앞면 (32×24 — 벽 칸 18도트 아래부터 바닥 칸 위 10도트까지): 모서리 빛 · 처마 그늘 · 판 · 꾸밈 띠 · 방 띠 · 굽도리.
    /// deco 0 없음 · 1 띠등 (빛은 곱한 뒤 따로) · 2 관 · 3 전선 · 4 환기구 · band 방 종류 띠 (<see cref="BandOf"/>) · seam 판 이음 자리 (−1 없음).
    /// </summary>
    public static Img Face(DotWall kind, bool openW, bool openE, int deco, int band, int seam, int variant)
    {
        var o = new Img(T, FaceH);
        var c = FaceOf(kind);
        o.Fill(0, 0, T, FaceH, c[3]);
        o.Fill(0, 3, T, 3, c[4]);                       // 판 위쪽은 빛을 더 받는다
        o.Fill(0, 0, T, 1, c[6]);                       // 모서리 빛
        o.Fill(0, 1, T, 1, c[1]); o.Fill(0, 2, T, 1, c[2]);   // 처마 그늘
        // 판 이음 (48도트 판 — 칸 자리로 정해 이어진다) · 볼트
        if (seam >= 0)
        {
            for (int y = 3; y < 15; y++) { o.Set(seam, y, c[1]); if (seam + 1 < T) o.Set(seam + 1, y, c[5]); }
            foreach (int bx in new[] { seam + 3, seam - 3 })
                if (bx > 0 && bx < T - 1) { o.Set(bx, 5, c[6]); o.Set(bx, 13, c[6]); o.Set(bx, 6, c[1]); o.Set(bx, 14, c[1]); }
        }
        if (kind == DotWall.Hull)   // 외판 안쪽: 늑골 (16도트마다 세로 갈비)
            for (int y = 3; y < 15; y++) { o.Set(8, y, c[5]); o.Set(9, y, c[2]); o.Set(24, y, c[5]); o.Set(25, y, c[2]); }
        if (kind == DotWall.Partition)   // 칸막이: 몰딩 한 줄
            for (int x = 0; x < T; x++) { o.Set(x, 9, c[2]); o.Set(x, 10, c[5]); }
        // 꾸밈 띠 (15~17)
        switch (deco)
        {
            case 1:   // 띠등 틀 (빛은 FaceLamp가 곱한 뒤 그린다)
                o.Fill(7, 15, 18, 3, c[1]);
                o.Fill(8, 16, 16, 1, Lens);
                break;
            case 2:   // 관 (가로) · 받침쇠
                for (int x = 0; x < T; x++) { o.Set(x, 15, c[6]); o.Set(x, 16, c[4]); o.Set(x, 17, c[1]); }
                foreach (int bx in new[] { 6, 22 }) { o.Fill(bx, 14, 2, 5, c[2]); o.Set(bx, 14, c[5]); }
                break;
            case 3:   // 전선 다발 (어두운 줄 둘 · 묶음 띠)
                for (int x = 0; x < T; x++) { o.Set(x, 16, CableA); o.Set(x, 17, (x & 7) < 6 ? CableB : CableA); }
                foreach (int bx in new[] { 10, 26 }) o.Fill(bx, 15, 2, 3, Brass);
                break;
            case 4:   // 환기구 (판 가운데 · 살 네 줄)
                o.Fill(10, 5, 12, 9, c[1]);
                for (int y = 6; y < 13; y += 2) o.Fill(11, y, 10, 1, c[0]);
                o.Fill(10, 5, 12, 1, c[5]);
                break;
        }
        o.Fill(0, 18, T, 1, c[2]);                       // 판 아래 홈
        o.Fill(0, 19, T, 1, Bands[Math.Clamp(band, 0, Bands.Length - 1)]);   // 방 종류 띠
        o.Fill(0, 20, T, 1, c[2]);                       // 굽도리 윗변
        o.Fill(0, 21, T, 2, c[1]);
        o.Fill(0, 23, T, 1, c[0]);
        // 벽 줄 끝: 왼쪽 끝은 빛 · 오른쪽 끝은 그늘
        if (openW) for (int y = 0; y < FaceH; y++) o.Set(0, y, y < 3 ? c[6] : c[5]);
        if (openE) for (int y = 0; y < FaceH; y++) { o.Set(T - 1, y, c[0]); o.Set(T - 2, y, c[1]); }
        // 작은 결
        for (int k = 0; k < 2; k++)
        {
            uint u = DotHash.U(variant, k, 93);
            int px = 2 + (int)(u % (T - 4)), py = 4 + (int)((u >> 8) % 10);
            if (o.Get(px, py).Equals(c[3]) || o.Get(px, py).Equals(c[4])) o.Set(px, py, c[2]);
        }
        return o;
    }

    /// <summary>선체 바깥 면 (32×8 — 아래가 우주인 외판 칸 아래에): 어두운 외판 · 늑골 이음 · 아래 끝 그늘 (빛을 받지 않는다). cutW · cutE: 볼록 모서리 쪽은 깎인 만큼 짧게.</summary>
    public static Img HullSkin(bool cutW, bool cutE)
    {
        var o = new Img(T, SkinH);
        for (int y = 0; y < SkinH; y++)
        {
            int x0 = cutW ? Bevel + 1 : 0, x1 = cutE ? T - Bevel - 1 : T;
            for (int x = x0; x < x1; x++)
            {
                var col = y == 0 ? Skin[5] : y == 1 ? Skin[4] : y >= SkinH - 1 ? Skin[0] : y >= SkinH - 3 ? Skin[1] : Skin[3];
                if ((x & 15) == 0 && y > 0 && y < SkinH - 1) col = Skin[1];
                if ((x & 15) == 1 && y > 1 && y < SkinH - 2) col = Skin[4];
                o.Set(x, y, col);
            }
        }
        return o;
    }

    /// <summary>
    /// 문 한 칸. inHorizWall (가로 벽의 문 — 위아래 방을 잇는다): 32×42 — 위 18도트 문 위 뚜껑 · 앞면 24도트에 문틀 · 문짝 둘 (좌우로 열린다 · 열린 자리는 투명해 바닥이 보인다).
    /// 세로 벽의 문: 32×32 — 위에서 본 문짝 둘 (위아래로 열린다).
    /// open 0~6 · kind 0 보통 · 1 바깥 (노랑 · 검정 사선) · 2 격벽 (주황 띠) · light 0 대기 · 1 열림 · 2 정전 · 3 끼임.
    /// </summary>
    public static Img Door(bool inHorizWall, int open, int kind, int light, int band)
    {
        var cap = kind == 1 ? CapHull : CapPanel;
        var face = kind == 1 ? FaceHull : FacePanel;
        var leaf = kind == 1 ? new[] { H("#2c2a24"), H("#4a463b"), H("#6a6453"), H("#8a836c"), H("#aaa38a") }
                             : new[] { H("#2c313b"), H("#454c5a"), H("#5f6778"), H("#7a8394"), H("#a0a9b9") };
        var led = light switch { 1 => H("#57e07a"), 2 => H("#ff4a3a"), 3 => H("#ffc83a"), _ => H("#4fc6e8") };
        int lw = Math.Max(0, 12 - open * 2);   // 문짝 한 쪽 너비 (12 → 0)
        if (inHorizWall)
        {
            var o = new Img(T, T + FaceLow);
            // 문 위 뚜껑 (상인방) · 문짝이 들어가는 홈
            o.Fill(0, 0, T, CapH, cap[2]);
            for (int x = 0; x < T; x++) { o.Set(x, 8, cap[1]); o.Set(x, 9, cap[3]); o.Set(x, CapH - 1, cap[0]); }
            // 문틀: 머리 3 · 기둥 4씩
            int top = CapH;
            o.Fill(0, top, T, 3, face[5]); o.Fill(0, top, T, 1, face[6]); o.Fill(0, top + 2, T, 1, face[2]);
            o.Fill(0, top, 4, FaceH, face[4]); o.Fill(T - 4, top, 4, FaceH, face[3]);
            for (int y = top; y < top + FaceH; y++) { o.Set(0, y, face[6]); o.Set(3, y, face[1]); o.Set(T - 4, y, face[5]); o.Set(T - 1, y, face[0]); }
            o.Fill(0, top + FaceH - 4, 4, 4, face[1]); o.Fill(T - 4, top + FaceH - 4, 4, 4, face[1]);   // 기둥 굽도리
            // 상태 불 (머리 양쪽 · 3×2)
            o.Fill(5, top + 1, 3, 2, led); o.Fill(T - 8, top + 1, 3, 2, led);
            // 문 자리 (4..27 · 머리 아래) — 열린 곳은 투명 (문 칸 바닥 · 아래 칸 바닥이 보인다) · 문턱 홈
            int y0 = top + 3, y1 = top + FaceH;
            for (int y = y0; y < y1; y++) for (int x = 4; x < T - 4; x++) o.Set(x, y, new Rgba(0, 0, 0, 0));
            o.Fill(4, T - 1, T - 8, 1, H("#14161b"));
            if (lw > 0)
            {
                Leaf(o, 4, y0, lw, y1 - y0, leaf, kind, true);
                Leaf(o, T - 4 - lw, y0, lw, y1 - y0, leaf, kind, false);
            }
            if (open == 0) for (int y = y0; y < y1; y++) { o.Set(15, y, leaf[0]); o.Set(16, y, leaf[0]); }   // 닫힌 이음
            return o;
        }
        else
        {
            var o = new Img(T, T);
            o.Fill(0, 0, T, T, cap[2]);
            for (int y = 0; y < T; y++) { o.Set(0, y, cap[4]); o.Set(T - 1, y, cap[0]); }
            // 문짝 홈 (가운데 세로 · 9..22) — 열린 곳은 투명
            for (int y = 4; y < T - 4; y++) for (int x = 9; x < 23; x++) o.Set(x, y, new Rgba(0, 0, 0, 0));
            o.Fill(9, 0, 14, 4, face[4]); o.Fill(9, T - 4, 14, 4, face[3]);   // 기둥 (위 · 아래)
            o.Fill(9, 0, 14, 1, face[6]); o.Fill(9, T - 1, 14, 1, face[0]);
            o.Fill(12, 1, 2, 2, led); o.Fill(18, T - 3, 2, 2, led);
            if (lw > 0)
            {
                int lh = lw;   // 위아래 문짝 (12 → 0)
                LeafV(o, 10, 4, 12, lh, leaf, kind, true);
                LeafV(o, 10, T - 4 - lh, 12, lh, leaf, kind, false);
            }
            if (open == 0) for (int x = 10; x < 22; x++) { o.Set(x, 15, leaf[0]); o.Set(x, 16, leaf[0]); }
            return o;
        }
    }

    /// <summary>앞에서 본 문짝 (좌 · 우): 판 · 테 · 작은 창 · 종류 표시 (바깥 = 노랑 · 검정 사선 · 격벽 = 주황 띠).</summary>
    private static void Leaf(Img o, int x, int y, int w, int h, Rgba[] c, int kind, bool left)
    {
        o.Fill(x, y, w, h, c[2]);
        o.Fill(x, y, w, 1, c[4]); o.Fill(x, y + h - 1, w, 1, c[0]);
        for (int yy = y; yy < y + h; yy++) { o.Set(x, yy, c[3]); o.Set(x + w - 1, yy, c[1]); }
        if (w >= 6) o.Fill(left ? x + w - 5 : x + 2, y + 3, 3, 4, H("#22394a"));   // 작은 창
        if (w >= 6) o.Set(left ? x + w - 5 : x + 2, y + 3, H("#7fb4cc"));
        if (kind == 1)
            for (int yy = y + h - 7; yy < y + h - 1; yy++)
                for (int xx = x + 1; xx < x + w - 1; xx++)
                    o.Set(xx, yy, ((xx + yy) / 3 & 1) == 0 ? H("#d9a520") : H("#1c1a14"));
        if (kind == 2) o.Fill(x + 1, y + h - 7, w - 2, 3, H("#c8611c"));
        if (w >= 4) o.Fill(left ? x + w - 3 : x + 1, y + h / 2, 2, 3, c[0]);   // 손잡이 홈
    }

    /// <summary>위에서 본 문짝 (세로 벽 — 위 · 아래로 열린다).</summary>
    private static void LeafV(Img o, int x, int y, int w, int h, Rgba[] c, int kind, bool top)
    {
        o.Fill(x, y, w, h, c[2]);
        o.Fill(x, y, 1, h, c[4]); o.Fill(x + w - 1, y, 1, h, c[0]);
        o.Fill(x, top ? y + h - 1 : y, w, 1, c[1]);
        if (kind == 1) for (int yy = y; yy < y + h; yy++) for (int xx = x + 1; xx < x + 4; xx++) o.Set(xx, yy, ((xx + yy) / 2 & 1) == 0 ? H("#d9a520") : H("#1c1a14"));
        if (kind == 2) o.Fill(x + w - 4, y, 3, h, H("#c8611c"));
    }
}
