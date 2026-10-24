namespace ShipEngine;

/// <summary>
/// ④ 날아가는 물건 (docs/DEPTH.md · ENGINE.md 파편 사슬): 바닥에 놓인 물건은 감압 바람의 끌림힘 ½ρv²CdA가 마찰 μmg를 넘으면 바람 쪽으로 미끄러진다.
/// 큰 물건(10kg 이상)은 열린 문틀에 걸려 문이 안 닫히고(끼인 문 — 주컴퓨터 · 로봇이 바로잡으며 치운다) · 사람 칸에 들어가면 부딪혀 다치고 · 구멍 밖으로 나가면 우주로 (장부 '우주로').
/// 놓인 물건은 ④ 구조의 물체 장부에 있다 (PH3a) — 미끄러지는 셈은 아직 여기서 (다리 · PH4가 걷는다) 장(바람)을 읽고 칸 옮김 · 막음 · 문을 구조에 요청한다 — 아래 층은 위를 모른다.
/// </summary>
public sealed partial class Mind
{
    public const double ItemCd = 1.2, ItemMu = 0.3, WetMu = 0.1, Gravity = 9.8, ItemDensity = 500;   // 젖은 바닥 마찰 0.1 // 상자 끌림 계수 · 금속 바닥 마찰 · 물건은 속이 찬 상자(500kg/m³)로 본다
    public const double JamKg = 10;                                       // 이보다 무거운 것은 문틀에 걸린다
    public long Flung, Spaced, ItemHits;

    private void Fling(Sim sim, double dt)
    {
        if (_st == null || _st.Props.Count == 0) return;
        double ix = _out != null ? -_out.Ax : 0, iy = _out != null ? -_out.Ay : 0;   // ⑤ 점화: 배 안의 것은 −a 쪽으로 (관성력 m·a)
        bool burning = ix != 0 || iy != 0;
        bool bodies = _st != null && _st.Bodies.Count > 1;   // ④ 칸 강체: 떨어져 나간 조각 · 덜컹인 배 안의 것이 느끼는 힘
        var props = _st.Props;
        for (int k = 0; k < props.Count; k++)
        {
            var it = props[k];
            if (it.Kind != 0) continue;
            int c = _st.CellOf(it), id = it.Id;
            byte item = (byte)it.What;
            if (c < 0 || c >= L.N || L.Kind[c] == CellKind.Door && _st.DoorJam[L.DoorAt[c]] != 0) continue; // 문틀에 걸린 것은 거기 있다
            if (it.PlugAt != 0)
            {
                // ③ 구멍에 빨려 붙은 물건: 구멍을 덧대거나 기압이 빠지면 (누르는 힘이 무게보다 작아지면) 떨어진다
                int h = it.PlugAt - 1;
                double face0 = Math.Pow(ItemKg(item) / ItemDensity, 2.0 / 3);
                if (F.Hole[h] <= 0.01f || F.Severed[h] != 0 || HoleSuction(c, h, face0) < ItemKg(item) * Gravity) _st.SetItemPlug(id, 0);
                continue;
            }
            double wx = F.WindX[c], wy = F.WindY[c], v = Math.Sqrt(wx * wx + wy * wy), pa = v >= 0.5 ? F.WindPa[c] : 0;
            // ③ 구멍 곁: 구멍 입으로 모이는 흐름 (방 바람보다 세면 그쪽으로 — 구멍이 나는 그 순간에도)
            double qMouth = MouthPull(c, out int mouth);
            if (qMouth > pa) { pa = qMouth; wx = L.X(mouth) - L.X(c); wy = L.Y(mouth) - L.Y(c); v = 1; }
            bool windy = v >= 0.5 && pa >= 1;
            double sx = 0, sy = 0, jx = 0, jy = 0;
            if (bodies) { (sx, sy) = _st!.Felt(c); (jx, jy) = _st.JoltDv(c); }
            double mu = (F.Stored(T.sWater, c) > 50 ? WetMu : ItemMu) * Math.Max(0.05, F.Grav[c]);   // 바닥 중력만큼 누른다 (무중력이면 거의 안 붙는다 — 찍찍이 몫 0.05)
            bool spun = sx * sx + sy * sy > (mu * Gravity) * (mu * Gravity);    // 원심이 마찰을 넘는다
            double jv = Math.Sqrt(jx * jx + jy * jy);
            int jolted = jv > 0.3 ? (int)Math.Min(3, Math.Floor(jv * jv / (2 * mu * Gravity) + 0.5)) : 0;   // 덜컹임: 바닥이 움직인 만큼 미끄러져 마찰로 선다 (v²/2μg)
            bool shaken = spun || jolted > 0;
            if (!windy && !burning && !shaken) continue;
            double kg = ItemKg(item), face = Math.Pow(kg / ItemDensity, 2.0 / 3); // 한 면 넓이 (m²)
            double fx = 0, fy = 0;
            if (windy) { double dr = pa * ItemCd * face; fx = dr * wx / v; fy = dr * wy / v; }
            if (burning) { fx += kg * ix; fy += kg * iy; }
            if (spun) { fx += kg * sx; fy += kg * sy; }
            double kick = jolted > 0 ? kg * jv / dt : 0;   // 덜컹임의 힘 (한 틱에 받은 운동량 — 까닭 · 방향을 가른다)
            if (jolted > 0) { fx += kick * jx / jv; fy += kick * jy / jv; }
            double drag = Math.Sqrt(fx * fx + fy * fy), grip = mu * kg * Gravity;   // 젖은 바닥은 미끄럽다
            if (drag <= grip && jolted == 0) continue;
            wx = fx; wy = fy;
            // 바람 쪽 이웃 칸으로 (센 쪽 축) — 한 틱에 많아야 세 칸 (끌림이 이긴 만큼 빨리 · 1m 미끄러지며 얻는 빠르기)
            double speed = Math.Max(jv, Math.Sqrt(2 * Math.Max(0, drag - grip) / kg * 1.0));
            int steps = Math.Clamp(Math.Max(jolted, (int)(speed * dt / 4)), 1, 3);
            int dx = Math.Abs(wx) >= Math.Abs(wy) ? Math.Sign(wx) : 0, dy = dx == 0 ? Math.Sign(wy) : 0;
            // 가장 센 힘이 까닭이다: 바람 · 점화 · 조각의 흔들림 (돌거나 덜컹임)
            double fw = windy ? pa * ItemCd * face : 0, fb = burning ? kg * Math.Sqrt(ix * ix + iy * iy) : 0, fs = (spun ? kg * Math.Sqrt(sx * sx + sy * sy) : 0) + kick;
            int src = fw >= fb && fw >= fs ? 0 : fb >= fs ? 1 : 2;
            int cause = src == 0 ? WindCause(c) : src == 1 ? _out!.BurnCause : _st!.BodyCause(c);
            string by = src == 0 ? "바람에 날린" : src == 1 ? "점화에 미끄러진" : spun && kick < kg * Math.Sqrt(sx * sx + sy * sy) ? "도는 조각에서 미끄러진" : "덜컹임에 미끄러진";
            var ck = src == 0 ? CauseKind.Pressure : src == 1 ? CauseKind.Route : CauseKind.Breach;
            int cell = c;
            for (int s = 0; s < steps; s++)
            {
                int x = L.X(cell) + dx, y = L.Y(cell) + dy;
                if (!L.In(x, y)) break;
                int j = L.Index(x, y);
                var kind = L.Kind[j];
                if (kind == CellKind.Wall && F.Hole[j] > 0.01f && face > F.Hole[j] * 1.2)
                {
                    // ③ 구멍보다 큰 물건은 빠져나가지 못하고 구멍에 빨려 붙어 막는다 (둘레 틈으로만 샌다)
                    int pc = sim.Causes.Add(ck, $"{Ko.I(ItemNames[item])} {(src == 0 ? "바람에 날려" : "미끄러져")} 구멍에 빨려 붙었다 — 새는 것을 거의 막았다", sim.Tick, j, cause);
                    sim.Facts.Emit(sim.Tick, FactKind.ObjectFlung, cell, (float)kg, pc, other: item);
                    _st.SetItemPlug(id, j + 1); HolesPlugged++;
                    break;
                }
                if (kind == CellKind.Outside || kind == CellKind.Wall && F.Hole[j] > 0.01f)
                {
                    // 구멍 밖 우주로
                    sim.Book.AddItem(item, Flow.Spaced, -1); Spaced++;
                    int sc = sim.Causes.Add(ck, $"{Ko.I(ItemNames[item])} {(src == 0 ? "바람에 날려" : src == 1 ? "점화에 미끄러져" : "덜컹임에 미끄러져")} 구멍 밖으로 사라졌다", sim.Tick, j, cause);
                    sim.Facts.Emit(sim.Tick, FactKind.ObjectFlung, j, (float)kg, sc, other: item);
                    _st.MoveItemCell(id, -1); k--;   // 목록에서 빠졌다 — 다음 물건이 이 자리로 온다
                    break;
                }
                if (kind == CellKind.Wall || L.FurnitureAt[j] >= 0) break; // 벽 · 설비에 부딪혀 멈춘다
                if (kind == CellKind.Door && F.DoorOpen[L.DoorAt[j]] < 0.5f) break; // 닫힌 문에 막힌다
                cell = j; _st.MoveItemCell(id, j); Flung++;
                // 사람에 부딪힌다: 운동 에너지 ½mv² — 200J에 가벼운 타박 0.25
                if (_life != null)
                    for (int b = 0; b < _life.Bodies.Count; b++)
                        if (_life[b].Cell == j && _life[b].Alive != 0)
                        {
                            double e = 0.5 * kg * speed * speed;
                            if (e < 20) continue;
                            int hc = sim.Causes.Add(ck, $"{by} {ItemNames[item]}에 맞았다", sim.Tick, j, cause);
                            _life.Strike(sim, b, (float)Math.Min(1.5, e / 800), 0, hc); ItemHits++;
                        }
                // 큰 것은 열린 문틀에 걸린다 → 문이 안 닫힌다 (구조에 '끼였다'를 요청)
                if (kind == CellKind.Door && kg >= JamKg)
                {
                    int d = L.DoorAt[j];
                    int jc = sim.Causes.Add(ck, $"{by} {Ko.I(ItemNames[item])} 문틀에 끼었다", sim.Tick, j, cause);
                    _st.JamDoor(sim, d, jc);
                    break;
                }
            }
        }
    }

    /// <summary>물건 무게 (kg — 데이터 표).</summary>
    public double ItemKg(int item) => P.Items.TryGetValue(ItemNames[item], out var d) ? d.Kg : 2;

    /// <summary>문 칸에 걸린 물건이 있으면 문이 닫히지 않는다 (몸처럼).</summary>
    private void HoldDoorsByItems()
    {
        if (_st == null) return;
        foreach (var x in _st.Props)
        {
            if (x.Kind != 0) continue;
            int xc = _st.CellOf(x);
            if (xc < 0 || L.Kind[xc] != CellKind.Door) continue;
            int d = L.DoorAt[xc];
            if (_st.DoorTarget[d] < 0.5f && _st.DoorJam[d] == 0) _st.RequestDoor(d, 1);
        }
    }

    /// <summary>문틀을 바로잡을 때 거기 걸린 물건을 옆 바닥으로 치운다.</summary>
    internal void ClearDoorItems(int doorCell)
    {
        if (_st == null) return;
        Span<int> nb = stackalloc int[4];
        L.Neighbors(doorCell, nb);
        int floor = -1;
        foreach (int j in nb) if (j >= 0 && L.IsSpace(j) && L.Kind[j] == CellKind.Floor && F.Pressure(j) >= F.Pressure(doorCell) - 1) { floor = j; break; }
        if (floor < 0) foreach (int j in nb) if (j >= 0 && L.IsSpace(j) && L.Kind[j] == CellKind.Floor) { floor = j; break; }
        if (floor < 0) return;
        var props = _st.Props;
        for (int k = 0; k < props.Count; k++) if (props[k].Kind == 0 && _st.CellOf(props[k]) == doorCell) _st.MoveItemCell(props[k].Id, floor);
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
