namespace ShipEngine;

/// <summary>
/// ④ 날아가는 물건 (docs/DEPTH.md · ENGINE.md 파편 사슬): 바닥에 놓인 물건은 감압 바람의 끌림힘 ½ρv²CdA가 마찰 μmg를 넘으면 바람 쪽으로 미끄러진다.
/// 큰 물건(10kg 이상)은 열린 문틀에 걸려 문이 안 닫히고(끼인 문 — 주컴퓨터 · 로봇이 바로잡으며 치운다) · 사람 칸에 들어가면 부딪혀 다치고 · 구멍 밖으로 나가면 우주로 (장부 '우주로').
/// 놓인 물건은 마음 층에 있으니 여기서 장(바람)을 읽고 구조(문)에 요청한다 — 아래 층은 위를 모른다.
/// </summary>
public sealed partial class Mind
{
    public const double ItemCd = 1.2, ItemMu = 0.3, Gravity = 9.8, ItemDensity = 500; // 상자 끌림 계수 · 금속 바닥 마찰 · 물건은 속이 찬 상자(500kg/m³)로 본다
    public const double JamKg = 10;                                       // 이보다 무거운 것은 문틀에 걸린다
    public long Flung, Spaced, ItemHits;

    private void Fling(Sim sim, double dt)
    {
        if (Loose.Count == 0) return;
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Loose);
        Span<int> nb = stackalloc int[4];
        for (int k = 0; k < span.Length; k++)
        {
            ref var it = ref span[k];
            int c = it.Cell;
            if (c < 0 || c >= L.N || L.Kind[c] == CellKind.Door && _st != null && _st.DoorJam[L.DoorAt[c]] != 0) continue; // 문틀에 걸린 것은 거기 있다
            double wx = F.WindX[c], wy = F.WindY[c], v = Math.Sqrt(wx * wx + wy * wy), pa = F.WindPa[c];
            if (v < 0.5 || pa < 1) continue;
            double kg = ItemKg(it.Item), face = Math.Pow(kg / ItemDensity, 2.0 / 3); // 한 면 넓이 (m²)
            double drag = pa * ItemCd * face, grip = ItemMu * kg * Gravity;
            if (drag <= grip) continue;
            // 바람 쪽 이웃 칸으로 (센 쪽 축) — 한 틱에 많아야 세 칸 (끌림이 이긴 만큼 빨리 · 1m 미끄러지며 얻는 빠르기)
            double speed = Math.Sqrt(2 * (drag - grip) / kg * 1.0);
            int steps = Math.Clamp((int)(speed * dt / 4), 1, 3);
            int dx = Math.Abs(wx) >= Math.Abs(wy) ? Math.Sign(wx) : 0, dy = dx == 0 ? Math.Sign(wy) : 0;
            int cause = WindCause(c);
            for (int s = 0; s < steps; s++)
            {
                int x = L.X(it.Cell) + dx, y = L.Y(it.Cell) + dy;
                if (!L.In(x, y)) break;
                int j = L.Index(x, y);
                var kind = L.Kind[j];
                if (kind == CellKind.Outside || kind == CellKind.Wall && F.Hole[j] > 0.01f)
                {
                    // 구멍 밖 우주로
                    sim.Book.AddItem(it.Item, Flow.Spaced, -1); Spaced++;
                    int sc = sim.Causes.Add(CauseKind.Pressure, $"{Ko.I(ItemNames[it.Item])} 바람에 날려 구멍 밖으로 사라졌다", sim.Tick, j, cause);
                    sim.Facts.Emit(sim.Tick, FactKind.ObjectFlung, j, (float)kg, sc, other: it.Item);
                    it.Cell = -1;
                    break;
                }
                if (kind == CellKind.Wall || L.FurnitureAt[j] >= 0) break; // 벽 · 설비에 부딪혀 멈춘다
                if (kind == CellKind.Door && F.DoorOpen[L.DoorAt[j]] < 0.5f) break; // 닫힌 문에 막힌다
                it.Cell = j; Flung++;
                // 사람에 부딪힌다: 운동 에너지 ½mv² — 200J에 가벼운 타박 0.25
                if (_life != null)
                    for (int b = 0; b < _life.Bodies.Count; b++)
                        if (_life[b].Cell == j && _life[b].Alive != 0)
                        {
                            double e = 0.5 * kg * speed * speed;
                            if (e < 20) continue;
                            int hc = sim.Causes.Add(CauseKind.Pressure, $"바람에 날린 {ItemNames[it.Item]}에 맞았다", sim.Tick, j, cause);
                            _life.Strike(sim, b, (float)Math.Min(1.5, e / 800), 0, hc); ItemHits++;
                        }
                // 큰 것은 열린 문틀에 걸린다 → 문이 안 닫힌다 (구조에 '끼였다'를 요청)
                if (kind == CellKind.Door && kg >= JamKg && _st != null)
                {
                    int d = L.DoorAt[j];
                    int jc = sim.Causes.Add(CauseKind.Pressure, $"바람에 날린 {Ko.I(ItemNames[it.Item])} 문틀에 끼었다", sim.Tick, j, cause);
                    _st.JamDoor(sim, d, jc);
                    break;
                }
            }
        }
        Loose.RemoveAll(x => x.Cell < 0);
    }

    /// <summary>물건 무게 (kg — 데이터 표).</summary>
    public double ItemKg(int item) => P.Items.TryGetValue(ItemNames[item], out var d) ? d.Kg : 2;

    /// <summary>문 칸에 걸린 물건이 있으면 문이 닫히지 않는다 (몸처럼).</summary>
    private void HoldDoorsByItems()
    {
        if (_st == null) return;
        foreach (var x in Loose)
        {
            if (x.Cell < 0 || L.Kind[x.Cell] != CellKind.Door) continue;
            int d = L.DoorAt[x.Cell];
            if (_st.DoorTarget[d] < 0.5f && _st.DoorJam[d] == 0) _st.RequestDoor(d, 1);
        }
    }

    /// <summary>문틀을 바로잡을 때 거기 걸린 물건을 옆 바닥으로 치운다.</summary>
    internal void ClearDoorItems(int doorCell)
    {
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Loose);
        Span<int> nb = stackalloc int[4];
        L.Neighbors(doorCell, nb);
        int floor = -1;
        foreach (int j in nb) if (j >= 0 && L.IsSpace(j) && L.Kind[j] == CellKind.Floor && F.Pressure(j) >= F.Pressure(doorCell) - 1) { floor = j; break; }
        if (floor < 0) foreach (int j in nb) if (j >= 0 && L.IsSpace(j) && L.Kind[j] == CellKind.Floor) { floor = j; break; }
        if (floor < 0) return;
        for (int k = 0; k < span.Length; k++) if (span[k].Cell == doorCell) span[k].Cell = floor;
    }

    /// <summary>그 칸 바람의 원인 (구역이 새는 틈을 낸 원인).</summary>
    private int WindCause(int c)
    {
        int r = F.RegionOf.Length > c ? F.RegionOf[c] : -1;
        if (_st == null || r < 0) return 0;
        foreach (var k in F.Links) if (k.RA == r && k.RB < 0 && _st.DamageCause[k.Via] != 0) return _st.DamageCause[k.Via];
        return 0;
    }
}
