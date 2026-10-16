using System.Collections.Generic;

namespace ShipSim.Core;

// v18.8 나머지 배 종류: 누더기형 — 시대가 다른 선체 토막을 이어 붙여 바깥 줄이 들쭉날쭉하다.
//  (바퀴형 · 화물선형은 Build 안에서 — 고리 + 가운데 바퀴살 통로 · 크게 넓힌 화물칸)

public static partial class ShipGenerator
{
    /// <summary>바깥 줄(row) 방 몇 개의 바깥쪽 한 줄을 깎는다 (문 · 설비가 없는 빈 바닥만) — 선체 윤곽이 토막마다 다르다.</summary>
    private static void Patch(List<GRoom> band, int row, Rng rng)
    {
        const string core = "rcpe"; // 원자로 · 냉각 · 배전 · 엔진은 손대지 않는다
        for (int i = 0; i < band.Count; i++)
        {
            var r = band[i];
            if (core.Contains(r.Label) || r.W < 6 || !rng.Chance(0.55f)) continue;
            bool left = rng.Chance(0.5f);
            int from = left ? 1 : r.W / 2, to = left ? r.W / 2 : r.W - 1;
            for (int x = from; x < to; x++)
                if (r.G[row][x] == '.' && (row == 0 ? r.G[1][x] : r.G[row - 1][x]) == '.') r.Carve.Add((x, row));
        }
    }
}
