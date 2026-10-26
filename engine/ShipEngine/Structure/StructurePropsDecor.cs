namespace ShipEngine;

/// <summary>
/// ④ 생활 소품 (docs/PHYSICS.md PH9 — 꾸밈 · 제자리): 설계한 배를 만들 때 한 번 (<see cref="ShipWorld.Create(string, ulong)"/>) 설비 표 `decor`의 가구 윗면에
/// 시드에서 컵 · 접시 · 물병 · 유리병 · 책 · 공구 상자를 놓는다 — 식당 · 조리실 탁자 · 침실 선반 (침대 곁) · 책장 · 작업대 · 조리대.
/// 놓은 것은 보통 물건 (Kind 0)과 같은 물체다 (점화 · 감압 · 폭풍 · 걷는 이에 똑같이 움직이고 깨진다) · 다른 것은 꾸밈 표시 (Flags 1 — 제자리가 있다)뿐: 마음은 줍지 않는다.
/// 자리: 그 칸 안 (난수 '소품 자리') — 발자국이 모두 그 가구의 같은 윗면 위 (밖으로 걸치지 않는다) · 이웃 꾸밈과 겹치지 않는다 ·
/// 가구 바깥 이웃 칸 (걸어 지나는 칸)에서 걷는 이 원판 + 2도트 + 닿는 반지름 밖 (조용한 하루에 걷는 이가 깨우지 않는다 — 조용한 틱 물체 셈이 그대로다).
/// 놓인 채 잠들어 있다 (놓임 · Asleep) — 깨어 있는 것이 없으면 물체 걸음은 곧장 돌아간다.
/// </summary>
public sealed partial class Structure
{
    /// <summary>꾸밈 표시 (Prop.Flags 1 — 제자리가 있다 · 마음은 줍지 않는다).</summary>
    public const byte DecorFlag = 1;

    /// <summary>
    /// 요청 (⑩ 상상 복제): 가벼운 물체 셈 (자세함 0 — 물체끼리 · 원판 닿음 끔 · 섬마다 잔걸음 4)으로 바꾸고 잠든 꾸밈을 뺀다 —
    /// 주컴퓨터가 상상하는 사람 · 구멍 · 불의 일에 탁자 위 컵은 끼지 않는다 (감압 복제 125틱이 꾸밈 수만큼 느려지지 않게 — 깨어 움직이는 것은 그대로 둔다).
    /// </summary>
    public void Lighten()
    {
        PropDetail = 0;
        Props.RemoveAll(p => (p.Flags & DecorFlag) != 0 && p.State == 0);
    }

    /// <summary>
    /// 요청 (⑧ — 5단 C10b 컵을 제자리에): 그 자리 (x, y)에 꾸밈 하나를 다시 놓는다 — 발자국 (가운데 · 껍질 표본)이 모두 가구 furniture의 같은 윗면 위 ·
    /// 그 높이에서 막히지 않고 (벽 · 다른 가구 기둥) · 놓인 다른 물체와 겹치지 않으면 놓임 · 잠듦 · 꾸밈 표시로. 새 물체 Id (못 놓으면 −1 — 부른 이가 곁에 내려놓는다).
    /// </summary>
    public int ReturnDecor(int item, float liquid, double x, double y, int furniture, int cause, int tag)
    {
        if (item < 0 || x < 0 || y < 0 || x >= L.W || y >= L.H) return -1;
        int c = L.Index((int)x, (int)y);
        var p = new Prop { Kind = 0, What = (short)item };
        FillBody(ref p);
        var fm = PropForm(p);
        var (sx, sy, reach) = fm.Shape.Hit;
        double top = TopAt(x, y, out int on);
        if (on != furniture + 1 || top <= 0) return -1;
        for (int k = 0; k < sx.Length; k++)
        {
            double t = TopAt(x + sx[k], y + sy[k], out int o);
            if (o != on || Math.Abs(t - top) > 1e-9) return -1;
        }
        // 걸어 지나는 이웃 칸에서 떨어진다 (처음 놓을 때와 같게 — 조용한 하루에 걷는 이가 깨우지 않는다)
        double keep = ActorR + 2 * DotForm.Dot + reach + 1e-3;
        int cx = L.X(c), cy = L.Y(c);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0 || !L.In(cx + dx, cy + dy)) continue;
                int nb = L.Index(cx + dx, cy + dy);
                if (L.Kind[nb] is not (CellKind.Floor or CellKind.Door) || L.FurnitureAt[nb] == furniture) continue;
                double ex = Math.Max(Math.Max(cx + dx - x, 0), x - (cx + dx + 1)), ey = Math.Max(Math.Max(cy + dy - y, 0), y - (cy + dy + 1));
                if (ex * ex + ey * ey < keep * keep) return -1;
            }
        _zLo = top; _zHi = top + HeightOf(fm);
        bool fits = FitsAt(fm, x, y);
        FullHeight();
        if (!fits) return -1;
        foreach (ref readonly var q in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Props))
        {
            if (q.Kind is not (0 or 2) || q.State == 5) continue;
            double r = reach + SamplesOf(PropForm(q)).reach + DotForm.Dot;
            if ((q.X - x) * (q.X - x) + (q.Y - y) * (q.Y - y) < r * r && Math.Abs(q.Z - top) < 0.3) return -1;   // 그 자리에 다른 것이 놓였다
        }
        p.Id = NextPropId++; p.X = x; p.Y = y; p.Z = (float)top; p.On = on; p.Liquid = Math.Max(0, liquid);
        p.G = F.Grav[c]; p.Flags |= DecorFlag; p.Asleep = 1; p.Since = _now; p.Cause = cause; p.Dropper = tag >= 0 ? tag + 1 : tag;
        Props.Add(p);
        return p.Id;
    }

    /// <summary>
    /// 요청 (⑧ — 5단 C10b): 가구 furniture 윗면의 빈 자리에 꾸밈 하나를 놓는다 — 칸마다 3 × 3 자리를 (nearX, nearY)에서 가까운 차례로 <see cref="ReturnDecor"/>. 새 Id (자리가 없으면 −1).
    /// </summary>
    public int ReturnDecorOn(int item, float liquid, int furniture, double nearX, double nearY, int cause, int tag)
    {
        if (furniture < 0 || furniture >= L.Furniture.Count || item < 0) return -1;
        var p = new Prop { Kind = 0, What = (short)item };
        FillBody(ref p);
        double reach = PropForm(p).Shape.Hit.reach, span = Math.Max(0, 1 - 2 * reach);
        var spots = new List<(double d, double x, double y)>();
        foreach (int c in L.Furniture[furniture].Cells)
            for (int j = 0; j < 3; j++)
                for (int i = 0; i < 3; i++)
                {
                    double x = L.X(c) + reach + span * i / 2, y = L.Y(c) + reach + span * j / 2;
                    spots.Add(((x - nearX) * (x - nearX) + (y - nearY) * (y - nearY), x, y));
                }
        spots.Sort((a, b) => a.d != b.d ? a.d.CompareTo(b.d) : a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        foreach (var (_, x, y) in spots) { int id = ReturnDecor(item, liquid, x, y, furniture, cause, tag); if (id >= 0) return id; }
        return -1;
    }

    /// <summary>그 점 (x, y)의 맨 윗면 높이 (m — 제자리 가구 기둥의 윗면 · 없으면 바닥 0) · on: 0 바닥 · 가구 + 1.</summary>
    public double TopAt(double x, double y, out int on) => SupportAt(x, y, Ceiling, out on, out _);

    /// <summary>
    /// 생활 소품을 놓는다 (만들 때 한 번 — 시드에서): 가구 차례 · 칸 차례 · 목록 차례로 저마다 몫 DecorP를 굴려 · 붙을 자리를 여섯 번까지 찾는다 ·
    /// 한 벌 (DecorSet — 식탁)이면 몫에서 빠진 물건을 칸 차례로 다시 찾는다. 놓은 수를 돌려준다.
    /// 컵은 반쯤 물이 들고 (꼴 표의 든 물) · 물병은 가득 · 담긴 물은 질량에 넣지 않는다 (PH8 그대로).
    /// </summary>
    public int Decorate(Sim sim)
    {
        ulong key = Rng.Key("생활 소품"), spot = Rng.Key("소품 자리");
        var names = T.People.Items.Keys.ToList();
        var placed = new List<(double x, double y, double reach)>();
        int n = 0;
        for (int f = 0; f < L.Furniture.Count; f++)
        {
            var fu = L.Furniture[f];
            var e = T.Equip(fu.Type);
            if (e.Decor == null || fu.Cells.Length == 0) continue;
            string room = fu.Room >= 0 && fu.Room < L.Rooms.Count ? L.Rooms[fu.Room].Type : "";
            if (!e.Decor.TryGetValue(room, out var list) && !e.Decor.TryGetValue("_", out list)) continue;
            placed.Clear();
            Span<bool> got = stackalloc bool[list.Length];
            for (int k = 0; k < fu.Cells.Length; k++)
                for (int j = 0; j < list.Length; j++)
                {
                    long slot = ((long)f << 12) | ((long)k << 4) | (uint)j;
                    if (Rng.Unit(sim.Seed, key, slot, 0) >= e.DecorP) continue;
                    if (TryDecor(sim, names.IndexOf(list[j]), list[j], f, fu.Cells[k], slot, 0, key, spot, placed)) { n++; got[j] = true; }
                }
            // 한 벌 (식탁): 몫에서 빠진 것은 칸 차례로 다시 찾는다
            if (e.DecorSet)
                for (int j = 0; j < list.Length; j++)
                    for (int k = 0; k < fu.Cells.Length && !got[j]; k++)
                        if (TryDecor(sim, names.IndexOf(list[j]), list[j], f, fu.Cells[k], ((long)f << 12) | ((long)k << 4) | (uint)j, 6, key, spot, placed)) { n++; got[j] = true; }
        }
        return n;
    }

    /// <summary>그 칸에 꾸밈 하나를 여섯 번까지 (난수 차례 from부터) 놓아 본다.</summary>
    private bool TryDecor(Sim sim, int item, string name, int f, int c, long slot, int from, ulong key, ulong spot, List<(double x, double y, double reach)> placed)
    {
        if (item < 0) return false;
        float liquid = DecorLiquid(name, Rng.Unit(sim.Seed, key, slot, 1));
        for (int tries = from; tries < from + 6; tries++)
        {
            double u = Rng.Unit(sim.Seed, spot, slot, 3 * tries), v = Rng.Unit(sim.Seed, spot, slot, 3 * tries + 1), w = Rng.Unit(sim.Seed, spot, slot, 3 * tries + 2);
            if (PlaceDecor(sim, item, f, c, u, v, w, liquid, placed)) return true;
        }
        return false;
    }

    /// <summary>꾸밈에 담긴 물 (몰): 물병은 가득 · 컵은 반쯤 (난수 u &lt; 0.5) · 그 밖은 없다 — 꼴 표의 든 물 (forms.json liquid).</summary>
    private float DecorLiquid(string name, double u)
    {
        var it = T.People.Items[name];
        double full = T.Forms.TryGetValue(it.Form ?? name, out var fd) && fd.Liquid > 0 ? fd.Liquid : it.Liquid;
        return name switch { "물병" => (float)full, "컵" => u < 0.5 ? (float)full : 0, _ => 0 };
    }

    /// <summary>
    /// 칸 c 안의 자리 (u, v — 0~1 · 자세 w)에 꾸밈 하나: 발자국 (가운데 · 껍질 표본)이 모두 가구 f의 같은 윗면 위 · 가구 바깥 걸어 지나는 이웃 칸에서
    /// 걷는 이 원판 + 2도트 + 닿는 반지름 밖 · 이 가구의 앞 꾸밈과 1도트 넘게 떨어진다. 놓으면 참.
    /// </summary>
    private bool PlaceDecor(Sim sim, int item, int f, int c, double u, double v, double w, float liquid, List<(double x, double y, double reach)> placed)
    {
        var p = new Prop { Kind = 0, What = (short)item };
        FillBody(ref p);
        var fm = PropForm(p);
        var (sx, sy, reach) = fm.Shape.Hit;
        double x = L.X(c) + reach + u * Math.Max(0, 1 - 2 * reach), y = L.Y(c) + reach + v * Math.Max(0, 1 - 2 * reach);
        double th = fm.Round ? 0 : fm.W > 2 * fm.D ? Math.Round(w * 4 - 0.5) * Math.PI / 2 + (w * 40 % 1 - 0.5) * 0.3 : w * 2 * Math.PI;   // 긴 것 (공구 상자)은 가구 결을 따라 · 책은 아무 쪽
        // 걸어 지나는 이웃 칸 (이 가구 밖의 바닥 · 문 — 다른 가구 칸도 사람이 넘는다)에서 떨어진다
        double keep = ActorR + 2 * DotForm.Dot + reach + 1e-3;
        int cx = L.X(c), cy = L.Y(c);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0 || !L.In(cx + dx, cy + dy)) continue;
                int nb = L.Index(cx + dx, cy + dy);
                if (L.Kind[nb] is not (CellKind.Floor or CellKind.Door) || L.FurnitureAt[nb] == f) continue;
                double ex = Math.Max(Math.Max(cx + dx - x, 0), x - (cx + dx + 1)), ey = Math.Max(Math.Max(cy + dy - y, 0), y - (cy + dy + 1));
                if (ex * ex + ey * ey < keep * keep) return false;
            }
        // 발자국이 모두 같은 윗면 위 (걸치지 않는다)
        double top = TopAt(x, y, out int on);
        if (on != f + 1 || top <= 0) return false;
        double cs = Math.Cos(th), sn = Math.Sin(th);
        for (int k = 0; k < sx.Length; k++)
        {
            double t = TopAt(x + cs * sx[k] - sn * sy[k], y + sn * sx[k] + cs * sy[k], out int o);
            if (o != on || Math.Abs(t - top) > 1e-9) return false;
        }
        foreach (var q in placed)
            if ((q.x - x) * (q.x - x) + (q.y - y) * (q.y - y) < (q.reach + reach + DotForm.Dot) * (q.reach + reach + DotForm.Dot)) return false;
        placed.Add((x, y, reach));
        p.Id = NextPropId++; p.X = x; p.Y = y; p.Th = th; p.Z = (float)top; p.On = on; p.Liquid = liquid;
        p.G = F.Grav[c]; p.Flags |= DecorFlag; p.Asleep = 1; p.Since = sim.Tick;
        Props.Add(p);
        return true;
    }
}
