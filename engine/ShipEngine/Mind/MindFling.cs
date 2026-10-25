namespace ShipEngine;

/// <summary>
/// ④ 미끄러지는 물건 — 다리 (docs/PHYSICS.md PH3a~PH4d): 바닥에 놓인 물건은 점화의 관성 · 도는 조각의 원심 · 덜컹임이 마찰 μmg를 넘으면 그쪽 칸으로 미끄러진다.
/// 감압 바람 · 구멍 입 · 막음은 ④가 실수 자리로 셈한다 (PH4c) — 여기는 놓인 것 (움직이는 것 · 막은 것 · 문틀에 낀 것은 ④ 몫)만 칸으로 옮긴다.
/// 문 칸에 든 물건은 닫히는 문짝이 붙든다 (④ 문) · 사람 칸에 들어가면 맞음 장부에 적혀 ⑦이 다친다 · 구멍 밖으로 나가면 우주로 (장부 '우주로').
/// 놓인 물건은 ④ 구조의 물체 장부에 있다 (PH3a) — 칸 옮김 · 막음 · 맞음을 구조에 요청한다 (PH4d가 다리를 걷는다) — 아래 층은 위를 모른다.
/// </summary>
public sealed partial class Mind
{
    public const double ItemMu = 0.3, WetMu = 0.1, Gravity = 9.8, ItemDensity = 500;   // 금속 바닥 마찰 · 젖은 바닥 마찰 0.1 · 물건은 속이 찬 상자(500kg/m³)로 본다 (다리 — 바람의 끌림은 ④가 꼴로)
    public const double JamKg = 10;                                       // 이보다 무거운 짐을 든 이는 잘 넘어진다
    public long Flung, Spaced, ItemHits;

    private void Fling(Sim sim, double dt)
    {
        if (_st == null || _st.Props.Count == 0) return;
        double ix = _out != null ? -_out.Ax : 0, iy = _out != null ? -_out.Ay : 0;   // ⑤ 점화: 배 안의 것은 −a 쪽으로 (관성력 m·a)
        bool burning = ix != 0 || iy != 0;
        bool bodies = _st != null && _st.Bodies.Count > 1;   // ④ 칸 강체: 떨어져 나간 조각 · 덜컹인 배 안의 것이 느끼는 힘
        if (!burning && !bodies) return;   // 바람은 ④가 (PH4c)
        var props = _st!.Props;
        for (int k = 0; k < props.Count; k++)
        {
            var it = props[k];
            if (it.Kind != 0 || it.State != 0) continue;   // 움직이는 것 · 구멍을 막은 것 · 문틀에 낀 것은 ④가 셈한다
            int c = _st.CellOf(it), id = it.Id;
            byte item = (byte)it.What;
            if (c < 0 || c >= L.N) continue;
            double sx = 0, sy = 0, jx = 0, jy = 0;
            if (bodies) { (sx, sy) = _st!.Felt(c); (jx, jy) = _st.JoltDv(c); }
            double mu = (F.Stored(T.sWater, c) > 50 ? WetMu : ItemMu) * Math.Max(0.05, F.Grav[c]);   // 바닥 중력만큼 누른다 (무중력이면 거의 안 붙는다 — 찍찍이 몫 0.05)
            bool spun = sx * sx + sy * sy > (mu * Gravity) * (mu * Gravity);    // 원심이 마찰을 넘는다
            double jv = Math.Sqrt(jx * jx + jy * jy);
            int jolted = jv > 0.3 ? (int)Math.Min(3, Math.Floor(jv * jv / (2 * mu * Gravity) + 0.5)) : 0;   // 덜컹임: 바닥이 움직인 만큼 미끄러져 마찰로 선다 (v²/2μg)
            bool shaken = spun || jolted > 0;
            if (!burning && !shaken) continue;
            double kg = ItemKg(item), face = Math.Pow(kg / ItemDensity, 2.0 / 3); // 한 면 넓이 (m²)
            double fx = 0, fy = 0;
            if (burning) { fx += kg * ix; fy += kg * iy; }
            if (spun) { fx += kg * sx; fy += kg * sy; }
            double kick = jolted > 0 ? kg * jv / dt : 0;   // 덜컹임의 힘 (한 틱에 받은 운동량 — 까닭 · 방향을 가른다)
            if (jolted > 0) { fx += kick * jx / jv; fy += kick * jy / jv; }
            double drag = Math.Sqrt(fx * fx + fy * fy), grip = mu * kg * Gravity;   // 젖은 바닥은 미끄럽다
            if (drag <= grip && jolted == 0) continue;
            double wx = fx, wy = fy;
            // 미는 쪽 이웃 칸으로 (센 쪽 축) — 한 틱에 많아야 세 칸 (힘이 이긴 만큼 빨리 · 1m 미끄러지며 얻는 빠르기)
            double speed = Math.Max(jv, Math.Sqrt(2 * Math.Max(0, drag - grip) / kg * 1.0));
            int steps = Math.Clamp(Math.Max(jolted, (int)(speed * dt / 4)), 1, 3);
            int dx = Math.Abs(wx) >= Math.Abs(wy) ? Math.Sign(wx) : 0, dy = dx == 0 ? Math.Sign(wy) : 0;
            // 가장 센 힘이 까닭이다: 점화 · 조각의 흔들림 (돌거나 덜컹임)
            double fb = burning ? kg * Math.Sqrt(ix * ix + iy * iy) : 0, fs = (spun ? kg * Math.Sqrt(sx * sx + sy * sy) : 0) + kick;
            bool burnt = fb >= fs;
            int cause = burnt ? _out!.BurnCause : _st!.BodyCause(c);
            string by = burnt ? "점화에 미끄러진" : spun && kick < kg * Math.Sqrt(sx * sx + sy * sy) ? "도는 조각에서 미끄러진" : "덜컹임에 미끄러진";
            var ck = burnt ? CauseKind.Route : CauseKind.Breach;
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
                    int pc = sim.Causes.Add(ck, $"{Ko.I(ItemNames[item])} 미끄러져 구멍에 빨려 붙었다 — 새는 것을 거의 막았다", sim.Tick, j, cause);
                    sim.Facts.Emit(sim.Tick, FactKind.ObjectFlung, cell, (float)kg, pc, other: item);
                    _st.SetItemPlug(id, j + 1); HolesPlugged++;
                    break;
                }
                if (kind == CellKind.Outside || kind == CellKind.Wall && F.Hole[j] > 0.01f)
                {
                    // 구멍 밖 우주로
                    sim.Book.AddItem(item, Flow.Spaced, -1); Spaced++;
                    int sc = sim.Causes.Add(ck, $"{Ko.I(ItemNames[item])} {(burnt ? "점화에 미끄러져" : "덜컹임에 미끄러져")} 구멍 밖으로 사라졌다", sim.Tick, j, cause);
                    sim.Facts.Emit(sim.Tick, FactKind.ObjectFlung, j, (float)kg, sc, other: item);
                    _st.MoveItemCell(id, -1); k--;   // 목록에서 빠졌다 — 다음 물건이 이 자리로 온다
                    break;
                }
                if (kind == CellKind.Wall || L.FurnitureAt[j] >= 0) break; // 벽 · 설비에 부딪혀 멈춘다
                if (kind == CellKind.Door && F.DoorOpen[L.DoorAt[j]] < 0.5f) break; // 닫힌 문에 막힌다
                cell = j; _st.MoveItemCell(id, j, cause); Flung++;
                // 사람에 부딪힌다: 운동 에너지 ½mv² — ④ 맞음 장부에 적고 ⑦이 5틱마다 읽는다 (200J에 가벼운 타박 0.25)
                if (_life != null)
                    for (int b = 0; b < _life.Bodies.Count; b++)
                        if (_life[b].Cell == j && _life[b].Alive != 0)
                        {
                            double e = 0.5 * kg * speed * speed;
                            if (e < Life.HitHurtJ) continue;
                            int hc = sim.Causes.Add(ck, $"{by} {ItemNames[item]}에 맞았다", sim.Tick, j, cause);
                            _st.AddPropHit(sim.Tick, b, j, e, kg, speed, hc); ItemHits++;
                        }
            }
        }
    }

    /// <summary>물건 무게 (kg — 데이터 표).</summary>
    public double ItemKg(int item) => P.Items.TryGetValue(ItemNames[item], out var d) ? d.Kg : 2;

    /// <summary>그 칸 바람의 원인 (구역이 새는 틈을 낸 원인).</summary>
    private int WindCause(int c)
    {
        int r = F.RegionOf.Length > c ? F.RegionOf[c] : -1;
        if (_st == null || r < 0) return 0;
        foreach (var k in F.Links) if (k.RA == r && k.RB < 0 && _st.DamageCause[k.Via] != 0) return _st.DamageCause[k.Via];
        return 0;
    }
}
