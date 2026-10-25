using ShipEngine;

namespace ShipView;

/// <summary>살펴볼 것의 갈래 (docs/UI2.md §3.2 누르기 차례 — 앞의 것이 먼저).</summary>
public enum PickKind : byte { None, Agent, Prop, Furniture, Door, Wall, Floor }

/// <summary>살펴볼 것: 갈래 · 번호 (사람 번호 · 물체 Id · 가구 번호 · 문 번호 · 칸 — 모두 세계에서 흔들리지 않는다).</summary>
public readonly record struct PickKey(PickKind Kind, int Id)
{
    public bool Any => Kind != PickKind.None;
}

/// <summary>왼쪽 카드의 갈래: 없음 · 사람 · 로봇 · 물건 (기체 · 선 · 관은 겹쳐 보기로 본다).</summary>
public enum CardKind : byte { None, Agent, Thing }

/// <summary>세계 위 테두리 하나 (칸 · 그린 자리 네 모서리 — 돌린 네모): 고른 것 (노랑) · 올린 것 (흰).</summary>
public readonly record struct Outline(bool Picked, double X0, double Y0, double X1, double Y1, double X2, double Y2, double X3, double Y3);

/// <summary>
/// 물건 살펴보기 (docs/UI2.md §3 — U4 · 화면만 · 세계를 읽기만): 누르기 차례 (사람 먼저 · 발자국이 작은 물체 · 가구 · 문 · 벽 · 바닥) · 화면 10픽셀 여유 ·
/// 떨어져 나간 조각 위 칸 · 올린 것 (한 점 — 다음 장에 실린다) · 고른 물건 카드 (갈래마다) · 테두리 · 카드의 [사고 ›] (그 물건에 겨눈다).
/// 새 읽기 (<see cref="Network.BrokenCause"/> · <see cref="Structure.HurtShare"/>)는 값을 셈해 돌려줄 뿐 쓰지 않는다 — 쌍둥이 세계와 찍기 바이트가 같다 (§8 V4).
/// </summary>
public sealed partial class LiveShow
{
    /// <summary>고른 물건 (사람 · 로봇은 <see cref="Selected"/>) — 카드는 물건을 따라가지 않는다 (카메라 그대로).</summary>
    public PickKey Look;
    private double _hoverPad = 0.21;

    /// <summary>누르기 여유 (칸): 화면 10픽셀 · 적어도 0.12칸 (cellPx = 화면 너비 픽셀 ÷ 보는 칸 수).</summary>
    public static double PadFor(double cellPx) => Math.Max(0.12, 10 / Math.Max(1e-6, cellPx));

    /// <summary>메인 스레드가 마우스 자리를 알린다 (칸 · 소수 — 끌지 않을 때 · 칸이 0.02 넘게 바뀔 때): 다음 장에 그 한 점만 셈한다 (겨눔 미리 보기 · 올린 것).</summary>
    public void SetHoverAt(double x, double y) { _hoverX = x; _hoverY = y; }
    public void SetHoverAt(double x, double y, double pad) { _hoverX = x; _hoverY = y; _hoverPad = pad; }

    /// <summary>
    /// 화면 자리 (칸 · 소수) 아래의 칸 (도면 밖 · 빈 곳이면 -1): 떨어져 나간 조각 (몸 b ≥ 1)은 그린 자세를 거꾸로 돌려 그 몸의 칸인지 본다 ·
    /// 아니면 배 (몸 0)의 칸 (떨어져 나간 조각의 옛 자리는 비었다).
    /// </summary>
    public int CellUnder(double x, double y)
    {
        var L = World.Layout; var st = World.Find<Structure>();
        if (st != null && st.Bodies.Count > 1)
            for (int b = 1; b < st.Bodies.Count; b++)
            {
                var (bx, by) = IntoBody(st, b, x, y);
                int ix = (int)Math.Floor(bx), iy = (int)Math.Floor(by);
                if (L.In(ix, iy) && st.BodyOf(L.Index(ix, iy)) == b) return L.Index(ix, iy);
            }
        int jx = (int)Math.Floor(x), jy = (int)Math.Floor(y);
        if (!L.In(jx, jy)) return -1;
        int c = L.Index(jx, jy);
        return st != null && st.Bodies.Count > 1 && st.BodyOf(c) > 0 ? -1 : c;
    }

    /// <summary>화면 자리를 몸 b의 도면 자리로 (그린 자세의 거꾸로 — 몸 0은 그대로).</summary>
    private (double x, double y) IntoBody(Structure st, int b, double x, double y)
    {
        if (b <= 0) return (x, y);
        var (cs, sn, tx, ty) = PoseDrawn(st, b);
        double dx = x - tx, dy = y - ty;
        return (cs * dx + sn * dy, -sn * dx + cs * dy);
    }

    /// <summary>몸 b의 도면 자리를 화면 자리로 (그린 자세).</summary>
    private (double x, double y) FromBody(Structure? st, int b, double x, double y)
    {
        if (st == null || b <= 0 || b >= st.Bodies.Count) return (x, y);
        var (cs, sn, tx, ty) = PoseDrawn(st, b);
        return (cs * x - sn * y + tx, sn * x + cs * y + ty);
    }

    private int BodyAt(Structure? st, int cell) => st != null && st.Bodies.Count > 1 && cell >= 0 ? Math.Max(0, st.BodyOf(cell)) : 0;

    /// <summary>
    /// 물체가 그려진 자리 (그 몸의 도면 칸 · 소수): 틱 안 기록대로 메운 자리 · 넘어지는 중이면 넘어지는 쪽으로 옮긴 자리 (Painter.PropPose와 같은 셈) ·
    /// 높이 z/2칸 위 (1층 1/16m = 1도트 · 3/4 시점) · 발자국 (m) · 꼴 높이 (칸 — H층 = H도트). 없으면 거짓.
    /// </summary>
    private bool PropSpot(Structure st, in Prop p, out double x, out double y, out double th, out double rise, out DotForm fm)
    {
        x = y = th = rise = 0; fm = null!;
        if (p.Kind is not (0 or 1 or 2 or 3) || p.State == 5) return false;
        var (px, py, pth) = DrawnProp(p.Id);
        if (double.IsNaN(px)) return false;
        fm = p.Piece > 0 ? st.PieceLook(p) : st.PropForm(p);
        x = px; y = py; th = pth;
        if (p.State == 2 && p.Tilt > 0)
        {
            int side = p.TiltDir & 3;
            double c = Math.Cos(p.Th), sn = Math.Sin(p.Th);
            var (dx, dy) = side switch { 0 => (c, sn), 1 => (-sn, c), 2 => (-c, -sn), _ => (sn, -c) };
            double k = Math.Clamp(p.Tilt / (Math.PI / 2), 0, 1) * (fm.TipB[side] + fm.Hc);
            x += dx * k; y += dy * k;
        }
        rise = TrackPlay.PropZ(World, p.Id, Math.Clamp(_carry, 0, 1)) * 0.5;
        return true;
    }

    /// <summary>그 점이 물체 상자 안인가 (도면 자리 · 여유 pad): 발자국을 자세대로 돌린 네모 — 밑면 높이와 꼴 꼭대기 높이 두 자리 중 하나.</summary>
    private static bool InPropBox(DotForm fm, double x, double y, double th, double rise, double qx, double qy, double pad)
    {
        double c = Math.Cos(th), s = Math.Sin(th), w = fm.W * DotForm.Dot, d = fm.D * DotForm.Dot, top = fm.H / 32.0;
        for (int k = 0; k < 2; k++)
        {
            double dx = qx - x, dy = qy + rise + k * top - y;
            double lx = c * dx + s * dy + fm.Cx, ly = -s * dx + c * dy + fm.Cy;   // 꼴 틀 (발자국 모서리에서 m)
            if (lx >= -pad && lx <= w + pad && ly >= -pad && ly <= d + pad) return true;
        }
        return false;
    }

    public PickKey PickAt(double x, double y, double pad, IReadOnlyList<AgentSpot>? spots = null) => PickAt(x, y, pad, spots, out _);

    /// <summary>
    /// 누르기 차례 (docs/UI2.md §3.2 — 한 점 · 겹치면 앞의 것): 사람 · 로봇 (0.45칸 안이면 늘 · 0.9칸까지는 물체 · 가구 상자 안이 아닐 때만 · 겹치면 사람) →
    /// 물체 (그린 상자 + 여유 · 여럿이면 발자국이 작은 것) → 제자리 가구 · 설비 → 문 → 벽 · 외판 → 배 안 바닥 → 없음 (바깥 · 우주).
    /// inside: 그 점이 물체 · 가구 상자 안인가 (여유 없이 — 메인 스레드가 사람을 고를 때 같은 규칙을 쓴다).
    /// </summary>
    public PickKey PickAt(double x, double y, double pad, IReadOnlyList<AgentSpot>? spots, out bool inside)
    {
        inside = false;
        var w = World; var L = w.Layout; var st = w.Find<Structure>();
        if (double.IsNaN(x)) return default;
        int cell = CellUnder(x, y);
        // 물체: 몸마다 그 점을 그 몸의 도면 자리로
        int best = 0; double bestArea = double.MaxValue;
        if (st != null)
        {
            int nb = st.Bodies.Count;
            Span<double> bx = stackalloc double[Math.Min(Math.Max(1, nb), 64)], by = stackalloc double[bx.Length];
            for (int b = 0; b < bx.Length; b++) (bx[b], by[b]) = b == 0 || nb <= 1 ? (x, y) : IntoBody(st, b, x, y);
            foreach (var p in st.Props)
            {
                if (p.Kind is not (0 or 1 or 2 or 3) || p.State == 5) continue;
                int b = nb > 1 ? Math.Max(0, st.BodyOf(st.CellOf(p))) : 0;
                if (b >= bx.Length) continue;
                double qx = bx[b], qy = by[b];
                if (Math.Abs(p.X - qx) > 4 || Math.Abs(p.Y - qy) > 4) continue;   // 먼 것은 셈하지 않는다 (한 점 ≤ 0.03ms)
                if (!PropSpot(st, p, out double px, out double py, out double th, out double rise, out var fm)) continue;
                if (!InPropBox(fm, px, py, th, rise, qx, qy, pad)) continue;
                if (InPropBox(fm, px, py, th, rise, qx, qy, 0)) inside = true;
                double area = fm.W * fm.D;
                if (area < bestArea) { bestArea = area; best = p.Id; }
            }
        }
        int furn = cell >= 0 && L.FurnitureAt[cell] >= 0 && (st == null || st.FurnPlaced[L.FurnitureAt[cell]] == 1) ? L.FurnitureAt[cell] : -1;
        if (furn >= 0) inside = true;
        int agent = AgentAt(spots ?? SpotsNow(), x, y, inside);
        if (agent >= 0) return new PickKey(PickKind.Agent, agent);
        if (best > 0) return new PickKey(PickKind.Prop, best);
        if (furn >= 0) return new PickKey(PickKind.Furniture, furn);
        if (cell < 0) return default;
        if (L.DoorAt[cell] >= 0) return new PickKey(PickKind.Door, L.DoorAt[cell]);
        if (L.Kind[cell] == CellKind.Wall) return new PickKey(PickKind.Wall, cell);
        var f = w.Find<Field>();
        if (L.Kind[cell] == CellKind.Floor && (f == null || f.Severed[cell] == 0)) return new PickKey(PickKind.Floor, cell);
        return default;
    }

    /// <summary>
    /// 누를 사람 · 로봇 (§3.2): 0.45칸 안이면 늘 · 0.9칸까지는 그 점이 물체 · 가구 상자 안이 아닐 때만 — 없으면 -1. 올림 · 누름 · 메인 스레드가 같은 셈.
    /// 상자 안이면 0.45칸 안에서만 고른다 (검토 고침 — 0.45칸 안의 로봇이 0.45 ~ 0.9칸의 사람에게 가려져 물체 · 가구가 골라졌다).
    /// </summary>
    public static int AgentAt(IReadOnlyList<AgentSpot> spots, double x, double y, bool inside) => Nearest(spots, x, y, out _, inside ? 0.45 : 0.9);

    /// <summary>살펴본다 (올린 열쇠로 — 올린 것과 누른 것이 늘 같다): 사람 · 로봇은 고르기 · 없음은 놓기 · 물건은 카드 (고른 이는 놓는다).</summary>
    public void Inspect(PickKey k)
    {
        if (k.Kind == PickKind.Agent) { SelectAgent(k.Id); return; }
        if (!k.Any) { Release(); return; }
        Look = k; Selected = -1; FollowSelected = false;
    }

    /// <summary>그 자리를 다시 셈해 살펴본다 (빠른 손 — 누른 자리가 올린 자리에서 여유 넘게 떨어졌다).</summary>
    public PickKey InspectAt(double x, double y, double pad)
    {
        var k = PickAt(x, y, pad);
        Inspect(k);
        return k;
    }

    /// <summary>물건 카드를 닫는다.</summary>
    public void LookClose() => Look = default;

    /// <summary>카드 이름 줄을 눌렀다: 고른 이 · 고른 물건이 있는 자리로 (카메라만).</summary>
    public void CardGo()
    {
        if (Selected >= 0) { CenterOn(Selected); return; }
        int c = LookCell(Look);
        if (c >= 0) LookAt(c);
    }

    /// <summary>살펴보는 것의 칸 (물체는 그 자리 칸 · 가구는 첫 칸) — 없으면 -1.</summary>
    private int LookCell(PickKey k)
    {
        var w = World; var L = w.Layout; var st = w.Find<Structure>();
        switch (k.Kind)
        {
            case PickKind.Prop: { int i = st?.IndexOf(k.Id) ?? -1; return i >= 0 ? st!.CellOf(st.Props[i]) : -1; }
            case PickKind.Furniture: return k.Id >= 0 && k.Id < L.Furniture.Count && L.Furniture[k.Id].Cells.Length > 0 ? L.Furniture[k.Id].Cells[0] : -1;
            case PickKind.Door: return k.Id >= 0 && k.Id < L.Doors.Count ? L.Doors[k.Id].Cell : -1;
            case PickKind.Wall or PickKind.Floor: return k.Id >= 0 && k.Id < L.N ? k.Id : -1;
            default: return -1;
        }
    }

    /// <summary>테두리 (그린 자리): 물체는 발자국을 돌린 네모 · 가구는 칸들을 두른 네모 · 문 · 벽 · 바닥은 그 칸. 없으면 null.</summary>
    public Outline? OutlineOf(PickKey k, bool picked)
    {
        var w = World; var L = w.Layout; var st = w.Find<Structure>();
        Outline Box(int b, double x0, double y0, double x1, double y1)
        {
            var a = FromBody(st, b, x0, y0); var c = FromBody(st, b, x1, y0); var d = FromBody(st, b, x1, y1); var e = FromBody(st, b, x0, y1);
            return new Outline(picked, a.x, a.y, c.x, c.y, d.x, d.y, e.x, e.y);
        }
        switch (k.Kind)
        {
            case PickKind.Prop:
            {
                int i = st?.IndexOf(k.Id) ?? -1;
                if (i < 0) return null;
                var p = st!.Props[i];
                if (!PropSpot(st, p, out double x, out double y, out double th, out double rise, out var fm)) return null;
                int b = BodyAt(st, st.CellOf(p));
                double cs = Math.Cos(th), sn = Math.Sin(th), wm = fm.W * DotForm.Dot, dm = fm.D * DotForm.Dot, lift = rise + fm.H / 64.0;
                (double, double) P(double lx, double ly) { lx -= fm.Cx; ly -= fm.Cy; return FromBody(st, b, x + cs * lx - sn * ly, y + sn * lx + cs * ly - lift); }
                var (ax, ay) = P(0, 0); var (bx2, by2) = P(wm, 0); var (cx2, cy2) = P(wm, dm); var (dx2, dy2) = P(0, dm);
                return new Outline(picked, ax, ay, bx2, by2, cx2, cy2, dx2, dy2);
            }
            case PickKind.Furniture:
            {
                if (k.Id < 0 || k.Id >= L.Furniture.Count || L.Furniture[k.Id].Cells.Length == 0) return null;
                var cells = L.Furniture[k.Id].Cells;
                int x0 = cells.Min(L.X), y0 = cells.Min(L.Y), x1 = cells.Max(L.X) + 1, y1 = cells.Max(L.Y) + 1;
                return Box(BodyAt(st, cells[0]), x0, y0, x1, y1);
            }
            case PickKind.Door or PickKind.Wall or PickKind.Floor:
            {
                int c = LookCell(k);
                return c < 0 ? null : Box(BodyAt(st, c), L.X(c), L.Y(c), L.X(c) + 1, L.Y(c) + 1);
            }
            default: return null;
        }
    }

    // ───────────────────────── 카드 (갈래마다 — 첫 줄 이름 · 둘째 줄 옅은 풀이 · 그 아래 상태 줄) ─────────────────────────

    /// <summary>방 이름 (방이 아니면 곁의 방 · 없으면 '통로').</summary>
    private string RoomNear(int cell)
    {
        var L = World.Layout;
        if (cell < 0) return "배 바깥";
        if (L.Room[cell] >= 0) return L.Rooms[L.Room[cell]].Name;
        foreach (int j in new[] { cell - L.W, cell + L.W, cell - 1, cell + 1 }) if (j >= 0 && j < L.N && L.Room[j] >= 0) return L.Rooms[L.Room[j]].Name;
        return "통로";
    }

    /// <summary>원인 글 한 줄: 그 마디 · 뿌리가 다르면 '처음: 뿌리'.</summary>
    private string WhyText(int cause)
    {
        var w = World;
        if (cause <= 0 || cause >= w.Causes.Count) return "";
        string t = TextLint.Clean(w.Causes.Text(cause));
        int root = w.Causes.Root(cause);
        return root > 0 && root != cause ? $"{t} (처음: {TextLint.Clean(w.Causes.Text(root))})" : t;
    }

    /// <summary>올린 것의 이름 (풀이 글 '식탁 — 누르면 살펴본다').</summary>
    public string LookName(PickKey k)
    {
        var w = World; var L = w.Layout; var st = w.Find<Structure>();
        switch (k.Kind)
        {
            case PickKind.Agent: return w.Find<Mind>() is { } m && k.Id >= 0 && k.Id < m.Agents.Count ? m.NameOf(k.Id) : "";
            case PickKind.Prop: { int i = st?.IndexOf(k.Id) ?? -1; return i >= 0 ? st!.PropName(st.Props[i]) : ""; }
            case PickKind.Furniture: return k.Id >= 0 && k.Id < L.Furniture.Count ? w.Tables.Equip(L.Furniture[k.Id].Type).Name : "";
            case PickKind.Door: return k.Id >= 0 && k.Id < L.Doors.Count ? DoorTitle(k.Id) : "";
            case PickKind.Wall: return k.Id >= 0 && k.Id < L.N ? (L.Hull[k.Id] ? "외판" : "벽") : "";
            case PickKind.Floor: return k.Id >= 0 && k.Id < L.N ? FloorTitle(k.Id) : "";
            default: return "";
        }
    }

    /// <summary>고른 물건의 카드 (없어졌으면 빈 것 — 카드가 닫힌다).</summary>
    public string[] LookCard()
    {
        var lines = new List<string>();
        switch (Look.Kind)
        {
            case PickKind.Prop: PropCard(Look.Id, lines); break;
            case PickKind.Furniture: FurnCard(Look.Id, lines); break;
            case PickKind.Door: DoorCard(Look.Id, lines); break;
            case PickKind.Wall: WallCard(Look.Id, lines); break;
            case PickKind.Floor: FloorCard(Look.Id, lines); break;
        }
        return lines.ToArray();
    }

    private static string Kg(double kg) => kg >= 10 ? $"{kg:0}kg" : kg >= 1 ? $"{kg:0.#}kg" : $"{kg:0.##}kg";
    private static string Cm(double m) => $"{Math.Max(1, Math.Round(m * 100)):0}cm";

    /// <summary>놓은 이 (꼬리표 + 1 · 로봇은 음수) → 이름.</summary>
    private string DropperName(int dropper)
    {
        var mind = World.Find<Mind>();
        if (dropper == 0 || mind == null) return "";
        int tag = dropper > 0 ? dropper - 1 : dropper;
        foreach (var a in mind.Agents)
            if (tag >= 0 ? a.IsPerson && a.C.Body == tag : !a.IsPerson && a.C.Mech == -1 - tag) return mind.NameOf(a.Id);
        return "";
    }

    private void PropCard(int id, List<string> lines)
    {
        var w = World; var L = w.Layout; var st = w.Find<Structure>();
        int i = st?.IndexOf(id) ?? -1;
        if (i < 0) return;
        var p = st!.Props[i];
        var fm = p.Piece > 0 ? st.PieceLook(p) : st.PropForm(p);
        string mat = st.PropMaterial(p).Name, name = st.PropName(p);
        double kg = p.Kg > 0 ? p.Kg : fm.Mass + p.Liquid * 0.018;
        string size = (p.Flags & 2) != 0 ? $"지름 {Cm(Math.Max(fm.W, fm.D) * DotForm.Dot)}" : $"{Cm(fm.W * DotForm.Dot)} × {Cm(fm.D * DotForm.Dot)}";
        switch (p.Kind)
        {
            case 2:
                lines.Add(name);
                lines.Add($"깨진 것의 한 조각 · {mat}{(mat is "유리" or "세라믹" ? " (날카롭다)" : "")}");
                break;
            case 1:
                lines.Add(name.Replace(" 조각", "") + " (뜯겨 나왔다)");
                lines.Add($"바닥에 박혀 있던 가구 · {mat} · {size}");
                break;
            case 3:
            {
                int door = -1; double bd = double.MaxValue;
                for (int d = 0; d < L.Doors.Count; d++) { int c = L.Doors[d].Cell; double dd = Math.Abs(L.X(c) + 0.5 - p.X) + Math.Abs(L.Y(c) + 0.5 - p.Y); if (st.LeafGone(d) && dd < bd) { bd = dd; door = d; } }
                lines.Add(door >= 0 ? $"날아간 문짝 — {DoorTitle(door)}" : "날아간 문짝");
                lines.Add($"문틀에서 떨어져 나온 문짝 · {mat}");
                break;
            }
            default:
            {
                lines.Add(name);
                var item = w.Tables.People.Items.TryGetValue(name, out var it) ? it : null;
                bool holds = p.Liquid > 0.05f || (item?.Liquid ?? 0) > 0 || name.Contains("병") || name == "컵";
                lines.Add($"{mat} · {size} · 높이 {Cm(fm.H * DotForm.Layer)}{(holds ? " · 물을 담는다" : "")}");
                break;
            }
        }
        var stuff = new List<string> { "무게 " + Kg(kg) };
        if (p.Kind == 0) stuff.Add("재질 " + mat);
        if (p.Liquid > 0.05f) stuff.Add($"물 {Math.Round(p.Liquid * 18 / 10) * 10:0}mL");
        var def = w.Tables.People.Items.TryGetValue(p.Kind == 0 ? st.PropName(p) : "", out var idef) ? idef : null;
        double full = def == null ? 0 : Math.Max(def.Water, Math.Max(def.O2, def.Co2));
        if (full > 0) stuff.Add($"남은 양 {Math.Clamp(p.Charge / full, 0, 1) * 100:0}%");
        lines.Add(string.Join(" · ", stuff));
        string on = p.On == 0 ? "바닥" : p.On > 0 && p.On <= L.Furniture.Count ? w.Tables.Equip(L.Furniture[p.On - 1].Type).Name + " 위"
            : p.On < 0 && st.IndexOf(-p.On - 1) is int u and >= 0 ? st.PropName(st.Props[u]) + " 위" : "바닥";
        lines.Add($"놓인 곳: {on} · {RoomNear(st.CellOf(p))} · 높이 {Cm(p.Z)}{(p.Kind == 0 ? p.Pose == 0 ? " · 섰다" : " · 누웠다" : "")}");
        double v = Math.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy);
        string moving = p.State switch
        {
            1 => $"미끄러진다 {v:0.0}m/s", 2 => "넘어지는 중", 3 => "떨어지는 중", 4 => "떠 있다", 5 => "우주로 나갔다", 6 => "구멍을 막고 있다", 7 => "문틀에 끼었다", _ => "놓였다",
        };
        if (Math.Abs(p.W) > 0.5 && p.State is 1 or 3 or 4) moving += " · 돈다";
        lines.Add("움직임: " + moving);
        double share = st.HurtShare(p);
        bool dent = p.Dent0 != 0 || p.Dent1 != 0;
        if (p.Kind is 0 or 1 or 2) lines.Add($"깨짐 피로 {Math.Clamp(share, 0, 9.99) * 100:0}%{(dent ? " · 찌그러졌다" : "")}");
        if (p.Kind == 2 && p.Parent > 0 && st.IndexOf(p.Parent) is int pi and >= 0) lines.Add("원래 물건: " + st.PropName(st.Props[pi]));
        var who = new List<string>();
        string dropper = DropperName(p.Dropper);
        if (dropper.Length > 0) who.Add("놓은 이: " + dropper);
        if (p.State == 0 && p.Still > 60) who.Add($"고요하게 {Quiet(p.Still)}");
        if (who.Count > 0) lines.Add(string.Join(" · ", who));
        if (p.Cause > 0) lines.Add((p.Kind == 2 ? "깨진 까닭: " : p.Kind is 1 or 3 ? "뜯은 것: " : "마지막으로 민 것: ") + WhyText(p.Cause));
    }

    private static string Quiet(double sec) => sec >= 3600 ? $"{sec / 3600:0}시간" : $"{Math.Max(1, sec / 60):0}분";

    private void FurnCard(int f, List<string> lines)
    {
        var w = World; var L = w.Layout; var st = w.Find<Structure>(); var net = w.Find<Network>();
        if (f < 0 || f >= L.Furniture.Count) return;
        var fu = L.Furniture[f];
        string name = w.Tables.Equip(fu.Type).Name;
        int room = fu.Cells.Length > 0 ? L.Room[fu.Cells[0]] : -1;
        lines.Add(room >= 0 ? $"{name} · {L.Rooms[room].Name}" : name);
        int dev = net != null && fu.Cells.Length > 0 ? Hand.DeviceAt(net, L, fu.Cells[0]) : -1;
        int wx = fu.Cells.Max(L.X) - fu.Cells.Min(L.X) + 1, hy = fu.Cells.Max(L.Y) - fu.Cells.Min(L.Y) + 1;
        lines.Add($"{(dev >= 0 ? "설비" : "가구")} · {wx} × {hy}칸 · 볼트로 바닥에 박혔다");
        if (st != null && f < st.FurnPlaced.Length)
        {
            var bits = new List<string> { st.FurnPlaced[f] == 1 ? "제자리" : "뜯겨 나갔다", $"볼트 {st.FurnAnchor[f]}/10" };
            if (f < st.FurnHit.Length && st.FurnHit[f] > 0.01f) bits.Add($"맞음 {Math.Min(1, st.FurnHit[f]) * 100:0}%");
            lines.Add(string.Join(" · ", bits));
            if (f < st.FurnBroken.Length && st.FurnBroken[f] != 0) lines.Add("부서졌다 — " + (st.FurnCause[f] > 0 ? WhyText(st.FurnCause[f]) : "까닭을 모른다"));
            var on = st.Props.Where(p => p.On == f + 1 && p.Kind is 0 or 2).Select(p => st.PropName(p)).ToList();
            if (on.Count > 0) lines.Add($"위에 놓인 것 {on.Count}: " + string.Join(" · ", on.Take(3)) + (on.Count > 3 ? " …" : ""));
        }
        if (dev >= 0) DeviceLines(net!, dev, lines);
    }

    /// <summary>설비 줄 (가구 줄 아래): 전기 · 주컴퓨터가 끔 · 부품마다 낡음 (숨은 한계에 대한 몫) · 정비 뒤 돈 시간 · 고장 까닭 · 봉함 샘 · 볼트 풀림 · 산소 조절 센서.</summary>
    private void DeviceLines(Network net, int d, List<string> lines)
    {
        var w = World; var dv = net.Devices[d];
        var power = new List<string> { net.On[d] != 0 ? "켜짐" : "꺼짐", $"받은 전기 {Math.Clamp(net.Supply[d], 0, 1) * 100:0}%" };
        if (dv.Feeder >= 0 && dv.Feeder < net.Breaker.Length) power.Add(net.Breaker[dv.Feeder] != 0 ? "차단기 붙어 있다" : "차단기가 떨어졌다");
        else power.Add("급전선 없음");
        lines.Add("전기: " + string.Join(" · ", power));
        if (w.Find<ShipMind>() is { } sm && d < sm.Shed.Length && sm.Shed[d] != 0) lines.Add("주컴퓨터가 껐다 — 전력을 아끼려");
        var wear = new List<string>(); var run = new List<string>(); var broke = new List<string>();
        foreach (var p in new[] { Part.Electric, Part.Mechanical, Part.Seal, Part.Filter })
        {
            if (!dv.Has(p)) continue;
            string pn = Device.PartName(p);
            wear.Add($"{pn} {Math.Clamp(net.WearFraction(d, p), 0, 9.99) * 100:0}%");
            run.Add($"{pn} {net.RunHours(d, p):0}시간");
            if (net.IsBroken(d, p)) { int bc = net.BrokenCause(d, p); broke.Add($"{pn} — {(bc > 0 ? WhyText(bc) : "까닭을 모른다")}"); }
        }
        if (wear.Count > 0) lines.Add("낡음 (숨은 한계에 대한 몫 — 사람 · 주컴퓨터는 모른다): " + string.Join(" · ", wear));
        if (run.Count > 0) lines.Add("정비 뒤 돈 시간: " + string.Join(" · ", run));
        foreach (var b in broke) lines.Add("고장: " + b);
        if (d < net.SealLeakCause.Length && net.SealLeakCause[d] > 0) lines.Add("봉함 샘: " + WhyText(net.SealLeakCause[d]));
        if (d < net.Loose.Length && net.Loose[d] >= 1) lines.Add("볼트가 풀려 덜컹거린다" + (net.LooseCause[d] > 0 ? " — " + WhyText(net.LooseCause[d]) : ""));
        if (dv.Def.Role == "o2gen") lines.Add($"조절 센서 읽는 몫 {net.O2Gain:0.00} · 읽음 {net.O2Reading:0.0}kPa · 실제 {net.PpO2:0.0}kPa");
    }

    /// <summary>문 이름: '식당 ↔ 복도' (양쪽 방).</summary>
    private string DoorTitle(int door)
    {
        var L = World.Layout; var dr = L.Doors[door];
        int x = L.X(dr.Cell), y = L.Y(dr.Cell);
        int Side(int xx, int yy) => L.In(xx, yy) ? L.Index(xx, yy) : -1;
        int a = dr.Vertical ? Side(x, y - 1) : Side(x - 1, y), b = dr.Vertical ? Side(x, y + 1) : Side(x + 1, y);
        string Name(int c) => c >= 0 && L.Room[c] >= 0 ? L.Rooms[L.Room[c]].Name : dr.External ? "바깥" : "통로";
        return $"{Name(a)} ↔ {Name(b)}";
    }

    private void DoorCard(int door, List<string> lines)
    {
        var w = World; var L = w.Layout; var st = w.Find<Structure>(); var f = w.Find<Field>();
        if (door < 0 || door >= L.Doors.Count || st == null || f == null) return;
        var dr = L.Doors[door]; int c = dr.Cell;
        lines.Add(DoorTitle(door));
        lines.Add(dr.Kind switch { "pressure" => "차압 문 (기압 차를 버틴다)", "airlock" => dr.External ? "에어락 바깥문" : "에어락 문", _ => "보통 문 (닫혀도 조금 샌다)" });
        if (st.LeafGone(door)) { lines.Add("문짝이 날아갔다 — 빈 문틀"); }
        else
        {
            float open = Math.Clamp(f.DoorOpen[door], 0, 1), target = st.DoorTarget[door];
            string going = target > open + 0.02f ? " · 열리는 중" : target < open - 0.02f ? " · 닫히는 중" : "";
            lines.Add($"열림 {open * 100:0}%{going} · {(st.DoorPowered[door] != 0 ? "전동" : "손으로만 연다")}");
            if (st.DoorLock[door] > w.Tick) lines.Add($"주컴퓨터가 잠갔다 · {Math.Max(1, (st.DoorLock[door] - w.Tick) * Time.SecondsPerTick / 60):0}분 더");
            if (st.DoorJam[door] != 0) lines.Add("끼었다" + (st.DamageCause[c] > 0 ? " — " + WhyText(st.DamageCause[c]) : ""));
            if (door < st.ThermalJam.Length && st.ThermalJam[door] != 0) lines.Add("열에 부풀어 끼었다 (식으면 풀린다)");
            var stuck = st.Props.Where(p => p.State == 7 && st.CellOf(p) == c).Select(p => st.PropName(p)).ToList();
            if (stuck.Count > 0) lines.Add("문틀에 낀 것: " + string.Join(" · ", stuck.Take(3)));
            var gap = new List<string>();
            if (f.DoorTaped[door] != 0) gap.Add("틈을 봉합 꾸러미로 막았다");
            if (f.DoorBleed[door] != 0) gap.Add("압력 맞춤 밸브가 열렸다");
            if (f.DoorGapOpen(door)) gap.Add("닫힌 틈으로 조금 샌다");
            if (gap.Count > 0) lines.Add(string.Join(" · ", gap));
        }
        lines.Add($"양쪽 기압 차 {st.DoorDeltaP(door):0.0}kPa · 버팀 {st.Stress[c] * 100:0}%{(st.Braced[c] != 0 ? " · 받쳤다" : "")}");
    }

    private void WallCard(int c, List<string> lines)
    {
        var w = World; var L = w.Layout; var st = w.Find<Structure>(); var f = w.Find<Field>();
        if (c < 0 || c >= L.N || st == null || f == null) return;
        string side = "";
        int x = L.X(c), y = L.Y(c);
        foreach (var (dx, dy, dir) in new[] { (0, 1, "북쪽"), (0, -1, "남쪽"), (1, 0, "서쪽"), (-1, 0, "동쪽") })
            if (L.In(x + dx, y + dy) && L.Room[L.Index(x + dx, y + dy)] is int r and >= 0) { side = $" ({L.Rooms[r].Name} {dir})"; break; }
        string mat = L.Material[c] < w.Tables.Materials.Count ? w.Tables.Materials[L.Material[c]].Name : "강철";
        lines.Add((L.Hull[c] ? "외판" : "안쪽 벽") + side);
        lines.Add($"{mat} {(L.Hull[c] ? 10 : 4)}mm{(L.Hull[c] ? " · 그 너머는 우주" : "")}");
        if (f.Hole[c] > 0) lines.Add((f.Hole[c] >= 2 ? "뚫렸다" : $"구멍 {f.Hole[c] * 10000:0}㎠") + (st.DamageCause[c] > 0 ? " — " + WhyText(st.DamageCause[c]) : ""));
        if (st.Crack[c] > 0) lines.Add($"금 {Cm(st.Crack[c] * 2)}" + (st.CrackCause[c] > 0 ? " — " + WhyText(st.CrackCause[c]) : ""));
        lines.Add($"피로 {Math.Clamp(st.Fatigue[c], 0, 1) * 100:0}% · 부식 {Math.Clamp(st.Corrode[c], 0, 1) * 100:0}% · 버팀 {st.Stress[c] * 100:0}%{(st.Braced[c] != 0 ? " · 받쳤다" : "")}");
        lines.Add($"겉 온도 {f.Temp(c) - 273.15:0}℃");
    }

    private string FloorTitle(int c)
    {
        var w = World; var f = w.Find<Field>(); var st = w.Find<Structure>();
        string room = RoomNear(c);
        if (f != null && f.Stored(w.Tables.sWater, c) / 55.5 >= 0.5) return "고인 물 — " + room;
        if (st != null && c < st.Grit.Length && st.Grit[c] > 0.001f) return $"깨진 {(st.GritMat[c] < w.Tables.Materials.Count ? w.Tables.Materials[st.GritMat[c]].Name : "유리")} 가루 — {room}";
        return room + " 바닥";
    }

    private void FloorCard(int c, List<string> lines)
    {
        var w = World; var L = w.Layout; var f = w.Find<Field>(); var st = w.Find<Structure>();
        if (c < 0 || c >= L.N || f == null) return;
        lines.Add(FloorTitle(c));
        double mm = f.Stored(w.Tables.sWater, c) / 55.5;
        lines.Add(mm >= 0.5 ? "바닥에 물이 고였다" : st != null && c < st.Grit.Length && st.Grit[c] > 0.001f ? "깨진 것의 가루가 흩어졌다" : "바닥 한 칸");
        if (mm >= 0.05) lines.Add($"고인 물 {mm:0.#}mm — " + (c < f.WetCause.Length && f.WetCause[c] > 0 ? WhyText(f.WetCause[c]) : "어디서 왔는지 모른다"));
        if (st != null && c < st.Grit.Length && st.Grit[c] > 0.001f) lines.Add($"가루 {st.Grit[c] * 1000:0}g · {(st.GritMat[c] < w.Tables.Materials.Count ? w.Tables.Materials[st.GritMat[c]].Name : "유리")}");
        var marks = new List<string>();
        double soot = f.SootGPerM2(c);
        if (soot > 0.5) marks.Add($"그을음 {soot:0}g/㎡");
        if (w.Find<Life>() is { } life && c < life.Mold.Length && life.Mold[c] > 0.05f) marks.Add("곰팡이가 슬었다");
        if (marks.Count > 0) lines.Add(string.Join(" · ", marks));
        int r = L.Room[c];
        double kpa = f.Pressure(c), o2 = f.Partial(w.Tables.gO2, c);
        var who = new List<string>();
        if (r >= 0 && w.Find<Mind>() is { } mind && w.Find<Life>() is { } lf)
            foreach (var a in mind.Agents) if (a.IsPerson && a.C.Cell >= 0 && L.Room[a.C.Cell] == r && lf.Bodies[a.C.Body].Alive != 0) who.Add(mind.NameOf(a.Id));
        lines.Add($"기압 {kpa:0}kPa · {f.Temp(c) - 273.15:0}℃ · 산소 {o2:0.0}kPa · 사람 {who.Count}{(who.Count > 0 ? " (" + string.Join(" · ", who.Take(3)) + (who.Count > 3 ? " …" : "") + ")" : "")}");
        if (r >= 0 && w.Find<ShipMind>() is { } sm && r < sm.RoomSeen.Length)
        {
            if (sm.RoomSeen[r] == 0) lines.Add($"주컴퓨터는 이 방을 못 본다 ({(w.Find<Network>() is { } nt && !nt.RoomPowered(r) ? "전원이 없다" : "센서 선이 닿지 않는다")})");
            else lines.Add($"주컴퓨터가 아는 이 방: 기압 {sm.RoomKPa[r]:0}kPa — {(Math.Abs(sm.RoomKPa[r] - kpa) < 2 ? "실제와 같다" : "실제와 다르다")}{(sm.RoomSeen[r] == 2 ? " (무선으로 늦게 읽는다)" : "")}");
        }
        else if (r < 0) lines.Add("방이 아닌 곳 — 주컴퓨터 센서가 닿지 않는다");
        if (c < f.Grav.Length && Math.Abs(f.Grav[c] - 1) > 0.05f) lines.Add($"바닥 중력 {f.Grav[c]:0.00}g");
        if (st != null && st.Bodies.Count > 1 && st.BodyOf(c) > 0)
        {
            var (bx, by) = DrawnBody(st.BodyOf(c));
            double dist = Math.Sqrt(Math.Pow(bx - L.W / 2.0, 2) + Math.Pow(by - L.H / 2.0, 2));
            lines.Add($"떨어져 나간 조각 위 — 배에서 {dist:0}m · 돈다");
        }
    }

    // ───────────────────────── 카드의 [사고 ›] (docs/UI2.md §5.4 — U3에서 미룬 HandFrom) ─────────────────────────

    /// <summary>살펴보는 것에 맞는 사고 갈래 (앞의 것이 먼저 — 그 자리에 되는 첫 갈래).</summary>
    private static HandKind[] HandFor(PickKind k, bool hull) => k switch
    {
        PickKind.Prop => new[] { HandKind.Shove },
        PickKind.Furniture => new[] { HandKind.Fault, HandKind.Fire, HandKind.Blast },
        PickKind.Door => new[] { HandKind.DoorJam, HandKind.Blast },
        PickKind.Wall => hull ? new[] { HandKind.Hole, HandKind.Rock, HandKind.PowerCut, HandKind.DataCut, HandKind.WaterLeak, HandKind.CoolantLeak } : new[] { HandKind.PowerCut, HandKind.DataCut, HandKind.WaterLeak, HandKind.CoolantLeak, HandKind.Blast },
        PickKind.Floor => new[] { HandKind.Fire, HandKind.WaterLeak, HandKind.PowerCut, HandKind.DataCut, HandKind.CoolantLeak, HandKind.Blast },
        _ => Array.Empty<HandKind>(),
    };

    /// <summary>[사고 ›]가 겨눌 갈래 · 자리 (안 되면 None과 까닭) — 산 세계의 판정 (<see cref="Hand.Why"/>)과 같다.</summary>
    private (HandKind kind, double x, double y, string why) LookHandPick()
    {
        string blocked = HandBlocked();
        if (blocked.Length > 0) return (HandKind.None, 0, 0, blocked);
        int c = LookCell(Look);
        if (c < 0) return (HandKind.None, 0, 0, "이것에 일으킬 사고가 없다");
        var L = Sim.Layout;
        double x = L.X(c) + 0.5, y = L.Y(c) + 0.5;
        if (Look.Kind == PickKind.Prop) { var (px, py, _) = DrawnProp(Look.Id); if (!double.IsNaN(px)) (x, y) = (px, py); }
        else (x, y) = CellDrawn(c);
        foreach (var k in HandFor(Look.Kind, L.Hull[c]))
        {
            var o = OrderFor(k, _handSize[(int)k], x, y);
            if (k == HandKind.Shove) o.Obj = Look.Id;
            if (Hand.Why(Sim, o).Length == 0) return (k, x, y, "");
        }
        return (HandKind.None, 0, 0, "이것에 일으킬 사고가 없다");
    }

    /// <summary>카드의 [사고 ›]: 사고 판을 열고 그 물건에 맞는 첫 갈래를 그 자리에 겨눈다 (누르면 일어난다 · 밀어 던지기는 끌어 놓는다).</summary>
    public bool HandFrom()
    {
        var (k, x, y, why) = LookHandPick();
        if (k == HandKind.None) { Notify(why, 3, PanelKind.Hand); return false; }
        if (Panel != PanelKind.Hand) TogglePanel(PanelKind.Hand);
        ArmHand(k, _handSize[(int)k]);
        _hoverX = x; _hoverY = y;
        return Armed == k;
    }
}
