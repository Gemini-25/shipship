using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>물체 법칙 (docs/PHYSICS.md §10): 보는 쪽 한 벌 · (PH1b부터) 도트 꼴.</summary>
public static partial class Program
{
    private static void PropLaws()
    {
        if (!Do("물체")) return;
        Console.WriteLine("\n[물체 — 보는 쪽 · 도트 꼴 · 평면 움직임]");
        FacingLaw();
        FormLaw();
        PlaneLaws();
    }

    /// <summary>
    /// 보는 쪽 한 벌 (PH1a): 가구가 보는 쪽은 도면에서 한 번 (`Layout.Facing`) — 그림이 셈하던 때 (Painter `FacingOfBox`)와 일곱 척 가구마다 같다.
    /// 값은 옮기기 전 코드로 잰 S/N/E/W 문자열의 FNV-1a 해시.
    /// </summary>
    private static void FacingLaw()
    {
        var was = new Dictionary<string, ulong>
        {
            ["didim"] = 0xeedd48741bc2bb93UL, ["hannuri"] = 0x7eaa18f4db702dffUL, ["hwasal"] = 0x7957fe64ce798e38UL, ["nareum"] = 0x29289543d328937bUL,
            ["saetteul"] = 0x13d64cffb51b55c6UL, ["salpim"] = 0x7f4072b4d2ba2241UL, ["soedol"] = 0x7647af24efa023eeUL,
        };
        var bad = new List<string>(); var got = new List<string>();
        foreach (var key in Layout.ShipKeys().OrderBy(k => k, StringComparer.Ordinal))
        {
            var L = Layout.Ship(key, Tables.Default);
            ulong h = 1469598103934665603UL;
            foreach (var f in L.Facing) { h ^= "SNEW"[(int)f]; h *= 1099511628211UL; }
            got.Add(key);
            if (L.Facing.Length != L.Furniture.Count || !was.TryGetValue(key, out var w) || w != h) bad.Add($"{key} {h:x16}");
        }
        Check("보는 쪽 한 벌 — 도면에서 한 번 셈한 가구의 보는 쪽이 그림이 셈하던 것과 일곱 척 모두 같다", bad.Count == 0 && got.Count == 7,
            bad.Count == 0 ? $"{got.Count}척 같음" : "다른 배: " + string.Join(" · ", bad));
    }

    /// <summary>
    /// 도트 꼴 (PH1b — docs/PHYSICS.md §10 법칙 1): 질량 = 표 kg (설비 105 · 물건 전부 · 누운 꼴 넷) · 대칭 꼴의 무게중심이 가운데 · 상자 회전관성 m(w² + d²)/12 ·
    /// 누우면 높이가 옆으로 간다 · 일곱 척 가구의 발자국이 칸 안 · 앞면 눈금 (표 h × 16 = 그림 앞면) · 위에서 본 발자국 = 그림의 불투명 윗면 ±2도트.
    /// </summary>
    private static void FormLaw()
    {
        var t = Tables.Default;
        const double d = DotForm.Dot;
        static bool Near(double a, double b) => Math.Abs(a - b) <= 1e-9 * Math.Max(1, Math.Abs(b));
        var ships = Layout.ShipKeys().OrderBy(k => k, StringComparer.Ordinal).Select(k => Layout.Ship(k, t)).ToList();
        // 배에 놓인 가구마다 (종류 · 칸 상자 · 보는 쪽 — 같은 것은 한 번)
        var placed = new SortedSet<(string type, int bw, int bh, Facing f)>();
        foreach (var L in ships)
            for (int i = 0; i < L.Furniture.Count; i++)
            {
                var c = L.Furniture[i].Cells; if (c.Length == 0) continue;
                placed.Add((L.Furniture[i].Type, c.Max(L.X) - c.Min(L.X) + 1, c.Max(L.Y) - c.Min(L.Y) + 1, L.Facing[i]));
            }
        // ① 질량
        var bad = new List<string>(); int nEquip = 0, nItem = 0;
        foreach (var (type, e) in t.Equipment)
        {
            if (type == "_default") continue;
            nEquip++;
            var p = placed.FirstOrDefault(x => x.type == type);
            var fm = p.type == null ? DotForge.Make(t, type) : DotForge.Make(t, type, p.f, p.bw, p.bh);
            double kg = e.Parts.Values.Sum();
            if (!Near(fm.Mass, kg) || !Near(fm.Kg.Sum(), kg)) bad.Add($"{type} {fm.Mass:0.###}/{kg:0.###}");
        }
        var items = t.People.Items.Keys.Concat(t.Forms.Where(kv => kv.Value.Kg > 0 && !t.Equipment.ContainsKey(kv.Key) && !t.People.Items.ContainsKey(kv.Key)).Select(kv => kv.Key)).ToList();
        foreach (var name in items)
        {
            nItem++;
            double kg = t.People.Items.TryGetValue(name, out var it) ? it.Kg : t.Forms[name].Kg;
            for (int lie = 0; lie <= 4; lie++)
            {
                var fm = DotForge.Make(t, name, lie: lie);
                if (!Near(fm.Mass, kg) || !Near(fm.Kg.Sum(), kg) || fm.Columns == 0) { bad.Add($"{name}/{lie} {fm.Mass:0.###}/{kg:0.###}"); break; }
            }
        }
        Check("도트 꼴 — 질량 = 표 kg (설비 · 물건 · 누운 꼴 넷 — 기둥 합도 · 1e-9)", bad.Count == 0 && nEquip == 105 && nItem > 30,
            bad.Count == 0 ? $"설비 {nEquip} · 물건 {nItem} × 다섯 자세" : string.Join(" · ", bad.Take(6)) + (bad.Count > 6 ? $" … ({bad.Count})" : ""));
        // ② 대칭 꼴 무게중심 · 상자 회전관성
        var sym = new[] { "소화기", "식량 상자", "컵", "접시" }.Select(n => DotForge.Make(t, n)).ToArray();
        var off = sym.Where(f => Math.Abs(f.Cx - f.W * d / 2) > 1e-12 || Math.Abs(f.Cy - f.D * d / 2) > 1e-12).Select(f => $"{f.Name} ({f.Cx / d:0.##}, {f.Cy / d:0.##})/({f.W / 2.0}, {f.D / 2.0})").ToList();
        Check("도트 꼴 — 대칭 꼴의 무게중심이 발자국 가운데 (소화기 · 식량 상자 · 컵 · 접시)", off.Count == 0 && sym.All(f => f.Columns > 0), off.Count == 0 ? "넷 모두 가운데" : string.Join(" · ", off));
        var box = DotForge.Make(t, "식량 상자");
        double w = box.W * d, dd = box.D * d, ib = box.Mass * (w * w + dd * dd) / 12;
        Check("도트 꼴 — 상자 회전관성 = m(w² + d²)/12 ± 2% (식량 상자)", box.Mass > 0 && Math.Abs(box.I - ib) <= 0.02 * ib,
            $"{box.W}×{box.D}도트 · {box.Mass:0.#}kg · I {box.I:0.0000} / {ib:0.0000}kg·m²");
        // 넘어짐 여유 · 마찰 반지름 · 바람 넓이 (상자 옆 = 깊이 × 높이 · 앞 = 가로 × 높이 · 대각은 그 사이 · 대칭이면 압력 중심이 가운데) · 원판 ρ = 2R/3
        double hgt = box.H * DotForm.Layer, ext = DotForge.Make(t, "소화기").Rho, r3 = 2 * (5 * d / 2) / 3;
        bool tip = Math.Abs(box.TipB[0] - w / 2) < 1e-12 && Math.Abs(box.TipB[1] - dd / 2) < 1e-12 && Math.Abs(box.TipB[2] - w / 2) < 1e-12 && Math.Abs(box.TipB[3] - dd / 2) < 1e-12;
        bool wind = Math.Abs(box.Area16[0] - dd * hgt) < 1e-12 && Math.Abs(box.Area16[4] - w * hgt) < 1e-12 && Math.Abs(box.Area16[8] - dd * hgt) < 1e-12
            && box.Area16[2] > box.Area16[4] && box.Area16[2] <= (w + dd) / Math.Sqrt(2) * hgt + 1e-12 && box.Cop16.All(c => Math.Abs(c) < 1e-9);
        Check("도트 꼴 — 넘어짐 여유 · 바람 넓이 · 마찰 반지름 (상자 b = 반 너비 · 옆 · 앞 · 대각 넓이 · 압력 중심 0 · 소화기 ρ = 2R/3 ± 5%)", tip && wind && Math.Abs(ext - r3) <= 0.05 * r3,
            $"b {box.TipB[0] / d:0.#}·{box.TipB[1] / d:0.#}도트 · 넓이 {box.Area16[0]:0.0000}·{box.Area16[2]:0.0000}·{box.Area16[4]:0.0000}m² · ρ {ext / d:0.000}/{r3 / d:0.000}도트");
        // ③ 누운 꼴: 높이가 누운 쪽으로 간다 (+x로 누우면 무게중심 x = 선 꼴의 높이 · 높이 = 반대쪽 끝까지 — 한 층 안)
        var lies = new List<string>();
        foreach (var n in new[] { "소화기", "식량 상자", "우주복", "유리병", "공구 상자" })
        {
            var up = DotForge.Make(t, n);
            for (int lie = 1; lie <= 4; lie++)
            {
                var lf = DotForge.Make(t, n, lie: lie);
                // 누운 쪽 축 · 그 쪽 무게중심 (꼴 끝에서)
                double along = lie switch { 1 => lf.Cx, 2 => lf.Cy, 3 => lf.W * d - lf.Cx, _ => lf.D * d - lf.Cy };
                double rest = lie switch { 1 => up.W * d - up.Cx, 2 => up.D * d - up.Cy, 3 => up.Cx, _ => up.Cy };
                if (lf.Columns == 0 || up.Hc <= 0 || Math.Abs(along - up.Hc) > d || Math.Abs(lf.Hc - rest) > DotForm.Layer || lf.Lie != lie) lies.Add($"{n}/{lie} {along:0.000}~{up.Hc:0.000} · {lf.Hc:0.000}~{rest:0.000}");
            }
        }
        Check("도트 꼴 — 누운 꼴: 선 꼴의 높이가 누운 쪽으로 · 높이는 닿은 면에서 (한 도트 · 한 층 안 · 다섯 물건 × 네 쪽)", lies.Count == 0, lies.Count == 0 ? "20꼴 맞음" : string.Join(" · ", lies.Take(4)));
        // ④ 일곱 척 가구: 발자국이 칸 안 · 앞면 눈금 · 위에서 본 발자국 = 그림 윗면 ±2도트
        var outBox = new List<string>(); var scale = new List<string>(); var foot = new List<string>(); int nArt = 0;
        foreach (var (type, bw, bh, f) in placed)
        {
            var fm = DotForge.Make(t, type, f, bw, bh);
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int y = 0; y < fm.D; y++) for (int x = 0; x < fm.W; x++) if (fm.Solid(x, y)) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
            if (x1 < 0 || fm.Ox + x0 < 0 || fm.Oy + y0 < 0 || fm.Ox + x1 >= bw * 32 || fm.Oy + y1 >= bh * 32) { outBox.Add($"{type} {bw}×{bh} {f}"); continue; }
            if (!ArtFurniture.Has(type)) continue;
            nArt++;
            var (front, _) = ArtFurniture.Size(type, bw, bh, f);
            if (Math.Abs(t.Equip(type).H * 16 - front) > 1e-9 || fm.H != front) scale.Add($"{type} {t.Equip(type).H * 16:0.##}·{fm.H}/{front}");
            // 그림의 윗면 띠: E · W 그림은 옆모습이라 칸 상자를 돌린 남쪽 그림과 견준다
            bool side = f is Facing.E or Facing.W;
            int aw = side ? bh : bw, ah = side ? bw : bh;
            var img = ArtFurniture.Body(type, aw, ah, side ? Facing.S : f, default, 0);
            var (fr, tp) = ArtFurniture.Size(type, aw, ah, side ? Facing.S : f);
            int ty = img.H - fr - tp, ax0 = int.MaxValue, ax1 = -1, rows = 0;
            for (int y = ty; y < img.H - fr; y++)
            {
                bool any = false;
                for (int x = 0; x < img.W; x++) if (img.Get(x, y).A > 0) { any = true; ax0 = Math.Min(ax0, x); ax1 = Math.Max(ax1, x); }
                if (any) rows++;
            }
            int fw = side ? y1 - y0 + 1 : x1 - x0 + 1, fd = side ? x1 - x0 + 1 : y1 - y0 + 1;
            if (Math.Abs(fw - (ax1 - ax0 + 1)) > 2 || Math.Abs(fd - rows) > 2) foot.Add($"{type} {bw}×{bh} {f} {fw}×{fd}/{ax1 - ax0 + 1}×{rows}");
        }
        Check("도트 꼴 — 일곱 척 가구의 발자국이 칸 상자 안", outBox.Count == 0 && placed.Count > 100, outBox.Count == 0 ? $"{placed.Count}가지 (종류 · 상자 · 보는 쪽)" : string.Join(" · ", outBox.Take(6)) + $" ({outBox.Count})");
        Check("도트 꼴 — 앞면 눈금: 표 높이 h × 16 = 그림 앞면 도트 = 꼴 층 수 (새 그림 가구)", scale.Count == 0 && nArt > 20, scale.Count == 0 ? $"{nArt}가지" : string.Join(" · ", scale.Distinct().Take(6)));
        Check("도트 꼴 — 위에서 본 발자국 = 그림의 불투명 윗면 ±2도트 (일곱 척 새 그림 가구)", foot.Count == 0 && nArt > 20, foot.Count == 0 ? $"{nArt}가지" : string.Join(" · ", foot.Take(12)) + $" ({foot.Count})");
    }

    // 공기 없는 10×6 방 (글자 그림 둘레 한 줄을 덧대어 안쪽은 도면 x 2~12 · y 2~8 m)
    private const string PropRoom = "\n############\n#..........#\n#..........#\n#..........#\n#..........#\n#..........#\n#..........#\n############";

    /// <summary>힘이 없는 작은 세계 (장 + 구조 · 공기 없음 · 마음 없음): 바닥 중력 g를 고르게.</summary>
    private static Sim PropWorld(float g, ulong seed = 1, string art = PropRoom, Dictionary<char, string>? legend = null)
    {
        var lay = Worlds.Ascii(art, legend);
        var s = new Sim(new SimSetup(lay, seed, sim => { var f = new Field(sim.Layout, sim.Tables); sim.Add(f); sim.Add(new Structure(f)); }));
        Array.Fill(s.Get<Field>().Grav, g);
        return s;
    }

    /// <summary>시험 차림: 물건을 놓고 (x, y)에 둔다 · 밀어 빠르기 (vx, vy) · 각속도 w를 준다 (충격량 = 질량 × 빠르기).</summary>
    private static int Slide(Sim s, string name, double x, double y, double vx, double vy, double w = 0)
    {
        var st = s.Get<Structure>();
        int item = s.Tables.People.Items.Keys.ToList().IndexOf(name);
        int id = st.PutItem(item, 0, s.Layout.Index((int)x, (int)y));
        int i = st.IndexOf(id); var p = st.Props[i]; p.X = x; p.Y = y; st.Props[i] = p;
        var fm = st.PropForm(p);
        if (vx != 0 || vy != 0 || w != 0) st.Shove(id, fm.Mass * vx, fm.Mass * vy, fm.I * w);
        return id;
    }

    /// <summary>방 안쪽 (도면 x 2~12 · y 2~8 m) 밖으로 나간 껍질 꼭짓점의 가장 깊은 겹침 (m).</summary>
    private static double WallOverlap(Structure st, in Prop p)
    {
        var fm = st.PropForm(p);
        double c = Math.Cos(p.Th), sn = Math.Sin(p.Th), worst = 0;
        foreach (var (hx, hy) in fm.Hull)
        {
            double rx = hx - fm.Cx, ry = hy - fm.Cy, x = p.X + c * rx - sn * ry, y = p.Y + sn * rx + c * ry;
            worst = Math.Max(worst, Math.Max(Math.Max(2 - x, x - 12), Math.Max(2 - y, y - 8)));
        }
        return worst;
    }

    /// <summary>
    /// 평면 움직임 (PH4a — docs/PHYSICS.md §10 법칙 2 · 3 · 14 · 12 · 벽): 힘이 없으면 그대로 · 마찰로 선다 (정확한 걸음) · 잠 · 미끄러지는 중 왕복 · 1코어 = 4코어 ·
    /// 빠른 상자도 벽을 건너뛰지 않는다 (매 틱 끝 겹침 ≤ 0.5도트 · 반발 e).
    /// </summary>
    private static void PlaneLaws()
    {
        var t = Tables.Default;
        const double g = 9.8, dot = DotForm.Dot;
        var box = DotForge.Make(t, "식량 상자");
        // 법칙 2: 힘이 없으면 그대로 — 무중력 · 공기 없음 · 50틱 뒤 x₀ + vt · θ = ωt · 운동 에너지 비트까지
        {
            var s = PropWorld(0);
            var st = s.Get<Structure>();
            int id = Slide(s, "식량 상자", 3.0, 2.8, 0.05, 0.02, 0.5);
            var p0 = st.Props[st.IndexOf(id)];
            double ke0 = 0.5 * p0.Kg * (p0.Vx * p0.Vx + p0.Vy * p0.Vy) + 0.5 * p0.Inertia * p0.W * p0.W;
            s.Run(50);
            var p = st.Props[st.IndexOf(id)];
            double time = 50 * Time.SecondsPerTick, ke = 0.5 * p.Kg * (p.Vx * p.Vx + p.Vy * p.Vy) + 0.5 * p.Inertia * p.W * p.W;
            double ex = Math.Abs(p.X - (p0.X + p0.Vx * time)), ey = Math.Abs(p.Y - (p0.Y + p0.Vy * time)), eth = Math.Abs(p.Th - (p0.Th + p0.W * time));
            Check("평면 움직임 — 힘이 없으면 그대로: 무중력 · 공기 없는 방의 식량 상자 (0.05, 0.02)m/s · 0.5rad/s → 50틱 뒤 x₀ + vt · θ = ωt (1e-9) · 운동 에너지 비트까지",
                ex <= 1e-9 && ey <= 1e-9 && eth <= 1e-9 && BitConverter.DoubleToInt64Bits(ke) == BitConverter.DoubleToInt64Bits(ke0) && p.State == 1,
                $"x 어긋남 {ex:0.0e0} · y {ey:0.0e0} · θ {eth:0.0e0} ({p.Th:0.000}rad) · 운동 에너지 {ke0:R} → {ke:R} · 상태 {p.State}");
        }
        // 법칙 3: 마찰로 선다 — 1g 마른 강철 바닥 · 식량 상자 (고분자 μk 0.3): 3m/s → v²/2μg = 1.5306m · 돌기만 3rad/s → Δθ = Iω²/(2 μk m g ρ)
        {
            var s = PropWorld(1);
            var st = s.Get<Structure>();
            int a = Slide(s, "식량 상자", 3.0, 3.0, 3, 0), b = Slide(s, "식량 상자", 6.0, 6.0, 0, 0, 3);
            var a0 = st.Props[st.IndexOf(a)]; var b0 = st.Props[st.IndexOf(b)];
            s.Run(1);
            var pa = st.Props[st.IndexOf(a)]; var pb = st.Props[st.IndexOf(b)];
            double want = 9 / (2 * 0.3 * g), turn = b0.Inertia * 9 / (2 * 0.3 * b0.Kg * g * box.Rho);
            double da = pa.X - a0.X, db = pb.Th - b0.Th;
            Check("평면 움직임 — 마찰로 선다: 1g 마른 바닥 · 식량 상자 3m/s → 1.5306m (1e-6) 뒤 멈춤 · 돌기만 3rad/s → Δθ = Iω²/(2μk m g ρ) (1e-6) · 자리는 그대로",
                Math.Abs(da - want) <= 1e-6 && pa.Vx == 0 && pa.Vy == 0 && Math.Abs(pa.Y - a0.Y) <= 1e-12 && Math.Abs(db - turn) <= 1e-6 && pb.W == 0 && Math.Abs(pb.X - b0.X) + Math.Abs(pb.Y - b0.Y) <= 1e-12,
                $"미끄러짐 {da:0.0000000}/{want:0.0000000}m · v ({pa.Vx:0.###}, {pa.Vy:0.###}) · 돎 {db:0.0000000}/{turn:0.0000000}rad · ω {pb.W:0.###} (ρ {box.Rho:0.0000}m)");
        }
        // 법칙 14: 잠 — 깨어 있는 것 하나면 첫 틱 잔걸음 > 0 · 선 뒤 잠들면 100틱 동안 잔걸음이 늘지 않는다 · 섬 놓침 0
        {
            var s = PropWorld(1);
            var st = s.Get<Structure>();
            int id = Slide(s, "식량 상자", 4.0, 4.0, 1, 0.5, 1);
            s.Run(1);
            long steps1 = st.PropSteps;
            var p = st.Props[st.IndexOf(id)];
            s.Run(100);
            Check("평면 움직임 — 잠: 깨어 있는 상자 하나면 첫 틱 잔걸음 > 0 · 선 뒤 0.5초면 잠든다 (빠르기 0 · 찍힌 잠) · 100틱 동안 잔걸음이 늘지 않는다 · 섬 놓침 0",
                steps1 > 0 && p.Asleep == 1 && p.State == 0 && p.Vx == 0 && p.Vy == 0 && p.W == 0 && st.PropSteps == steps1 && st.IslandMiss == 0,
                $"첫 틱 잔걸음 {steps1} · 잠 {p.Asleep} · 상태 {p.State} · 100틱 뒤 잔걸음 {st.PropSteps} · 섬 놓침 {st.IslandMiss}");
        }
        // 법칙 12: 결정론 — 미끄러지는 상자 20개 (0.1g · 벽에 튀며)를 찍고 되살려 60틱 바이트 같음 · 1코어 = 4코어
        {
            (ulong hash, bool same, int awake) Run(int threads)
            {
                int keep = Par.Threads; Par.Threads = threads;
                var s = PropWorld(0.1f, 9);
                var st = s.Get<Structure>();
                var rng = new Random(5);
                string[] names = { "식량 상자", "소화기", "공구 상자", "수리 꾸러미", "책" };
                for (int k = 0; k < 20; k++)
                {
                    double a = rng.NextDouble() * 2 * Math.PI, v = 1 + 5 * rng.NextDouble();
                    Slide(s, names[k % names.Length], 2.5 + k % 5 * 2, 2.5 + k / 5 * 1.5, v * Math.Cos(a), v * Math.Sin(a), 6 * rng.NextDouble() - 3);
                }
                s.Run(1);
                var r = new Sim(s.Setup); r.Load(s.Save());
                s.Run(60); r.Run(60);
                bool same = s.Save().AsSpan().SequenceEqual(r.Save());
                int awake = 0; foreach (var p in st.Props) if (Structure.Awake(p)) awake++;
                Par.Threads = keep;
                return (s.Hash(), same, awake);
            }
            var one = Run(1); var four = Run(4);
            Check("평면 움직임 — 결정론: 미끄러지며 벽에 튀는 상자 20개를 1틱 뒤 찍고 되살려 60틱 — 바이트 같음 · 1코어 = 4코어",
                one.same && four.same && one.hash == four.hash && one.hash != 0, $"되살림 같음 {one.same}/{four.same} · {one.hash:x16} / {four.hash:x16} · 61틱 뒤 깨어 있음 {one.awake}");
        }
        // 닫힌 문은 벽 · 제자리 가구는 기둥: 1g 상자 3 · 4m/s가 닫힌 문 · 탁자 앞에서 튀어 선다 (문 칸 · 탁자 발자국 안으로 들어가지 않는다)
        {
            const string art = "\n############\n#.......tt.#\n#.......tt.#\n#..........#\n#####D######\n#..........#\n############";
            var s = PropWorld(1, 1, art, new Dictionary<char, string> { ['t'] = "Table" });
            var st = s.Get<Structure>(); var L = s.Layout;
            var table = L.Furniture.Single(fu => fu.Type == "Table");
            var tf = DotForge.Make(t, "Table", L.Facing[table.Id], 2, 2);
            int lx = int.MaxValue; for (int y = 0; y < tf.D; y++) for (int x = 0; x < tf.W; x++) if (tf.Solid(x, y)) lx = Math.Min(lx, x);
            double tableLeft = (table.Cells.Min(L.X) * 32 + tf.Ox + lx) * dot;   // 탁자 발자국 왼쪽 끝 (m)
            int a = Slide(s, "식량 상자", 6.5, 3.5, 0, 3), b = Slide(s, "식량 상자", 6.5, 2.9, 4, 0);
            s.Run(2);
            var pa = st.Props[st.IndexOf(a)]; var pb = st.Props[st.IndexOf(b)];
            double south = pa.Y + box.Hull.Max(v => v.y) - box.Cy, east = pb.X + box.Hull.Max(v => v.x) - box.Cx;
            Check("평면 움직임 — 닫힌 문은 벽 · 제자리 가구는 기둥: 3 · 4m/s 상자가 닫힌 문 앞 · 탁자 발자국 앞에서 튀어 선다 (안으로 0.5도트 넘게 들어가지 않는다 · 닿을 만큼 갔다)",
                south <= 5 + 0.5 * dot && south > 4.85 && east <= tableLeft + 0.5 * dot && east > tableLeft - 0.15 && pa.Asleep == 1 && pb.Asleep == 1 && st.Bounces >= 2,
                $"문 앞 상자 남쪽 끝 {south:0.000}/5 · 탁자 앞 상자 동쪽 끝 {east:0.000}/{tableLeft:0.000}m · 튐 {st.Bounces} · 잠 {pa.Asleep}{pb.Asleep}");
        }
        // 벽을 건너뛰지 않는다: 20m/s 상자 — 무중력 하나 (벽에 튀어 e = min(고분자 0.5, 강철 0.4)씩 줄어든다) · 1g 하나 (튀고 미끄러져 벽에 선다) — 매 틱 끝 겹침 ≤ 0.5도트
        {
            var s = PropWorld(0);
            var st = s.Get<Structure>();
            var f = s.Get<Field>();
            for (int c = 0; c < s.Layout.N; c++) if (s.Layout.Y(c) >= 5) f.Grav[c] = 1;   // 아래 반은 1g
            int a = Slide(s, "식량 상자", 10.5, 3.5, 20, 0), b = Slide(s, "식량 상자", 9.5, 6.5, 20, 0), cc = Slide(s, "식량 상자", 11.69, 7.2, 0.9, 0);   // 셋째: 0.46m/s로 닿아 (튀지 않고) 벽에 붙어 선다
            double worst = 0; long bounces1 = 0; Prop pa1 = default;
            for (int k = 0; k < 4; k++)
            {
                s.Step();
                foreach (int id in new[] { a, b, cc }) worst = Math.Max(worst, WallOverlap(st, st.Props[st.IndexOf(id)]));
                if (k == 0) { pa1 = st.Props[st.IndexOf(a)]; bounces1 = st.Bounces; }
            }
            var pb = st.Props[st.IndexOf(b)]; var pc = st.Props[st.IndexOf(cc)];
            double gap = 12 - (pc.X + box.Hull.Max(v => v.x) - box.Cx);
            double e = 0.4, want = 20 * Math.Pow(e, 2);
            Check("평면 움직임 — 벽을 건너뛰지 않는다: 20m/s 상자 둘 (무중력 · 1g) — 매 틱 끝 벽 겹침 ≤ 0.5도트 · 무중력 상자는 두 번 튀어 20 × 0.4² = 3.2m/s (±2%) · 1g 상자는 방 안에 선다 · 느리게 닿은 상자는 벽에 붙어 잔다 (틈 ≤ 1도트)",
                worst <= 0.5 * dot && Math.Abs(pa1.Vx - want) <= 0.02 * want && Math.Abs(pa1.Vy) < 1e-3 && Math.Abs(pa1.W) < 1e-3 && bounces1 >= 2 && pb.Vx == 0 && pb.X > 2 && pb.X < 12
                && pc.Asleep == 1 && Math.Abs(gap) <= dot,
                $"겹침 {worst / dot:0.00}도트 · 무중력 상자 1틱 뒤 vx {pa1.Vx:0.000}/{want:0.000} · vy {pa1.Vy:0.0e0} · ω {pa1.W:0.000} · 튐 {bounces1} · 1g 상자 x {pb.X:0.000} vx {pb.Vx:0.###} · 붙어 선 상자 틈 {gap / dot:0.00}도트 · 잠 {pc.Asleep}");
        }
    }
}
