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
        Console.WriteLine("\n[물체 — 보는 쪽 · 도트 꼴]");
        FacingLaw();
        FormLaw();
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
}
