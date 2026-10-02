using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ShipSim.Core;
using ShipSim.TexGen;
using ShipSim.View;

// v16.5a 재질 그림 점검 (Godot 없이): 텍스처 파일 · 생성기 결정론(지문) · 바닥재 · 방 · 상태 · 흔적 · 입자 매핑에 빠짐이 없는지,
// 바닥재마다 무늬가 서로 다르고 이음매 없이 이어지는지, 화면 코드가 Core 를 읽기만 하는지, 시뮬레이션 지문이 그대로인지.
public static partial class Program
{
    private static string LookHere([CallerFilePath] string p = "") => p;

    private static string? LookRoot()
    {
        var cands = new List<string>();
        var here = LookHere();
        if (here.Length > 0) cands.Add(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here) ?? ".", "..", "..")));
        var d = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (int i = 0; i < 6 && d != null; i++, d = d.Parent) cands.Add(d.FullName);
        return cands.FirstOrDefault(c => Directory.Exists(Path.Combine(c, "game", "assets", "textures")) && Directory.Exists(Path.Combine(c, "game", "src", "View")));
    }

    private static float LookLum(Pix p, int i) => 0.299f * p.R[i] + 0.587f * p.G[i] + 0.114f * p.B[i];

    /// <summary>
    /// 감싼 경계(마지막 줄 ↔ 첫 줄)의 밝기 차이 ÷ 안쪽 주기 경계(64 · 128 · 192줄) 차이 중 큰 것.
    /// 칸마다 되풀이되는 판 이음매는 안쪽에도 똑같이 있으니, 감싼 경계가 안쪽 경계보다 튀지 않으면 이음매가 없다.
    /// </summary>
    private static float LookSeam(Pix p)
    {
        double Row(int a, int b) { double d = 0; for (int k = 0; k < p.W; k++) d += MathF.Abs(LookLum(p, p.I(k, a)) - LookLum(p, p.I(k, b))); return d / p.W; }
        double Col(int a, int b) { double d = 0; for (int k = 0; k < p.H; k++) d += MathF.Abs(LookLum(p, p.I(a, k)) - LookLum(p, p.I(b, k))); return d / p.H; }
        double edge = Math.Max(Row(p.H - 1, 0), Col(p.W - 1, 0));
        if (edge < 0.04) return (float)(edge / 0.04);
        double inner = 0;
        foreach (var q in new[] { p.H / 4, p.H / 2, 3 * p.H / 4 }) inner = Math.Max(inner, Math.Max(Row(q - 1, q), Col(q - 1, q)));
        return (float)(edge / Math.Max(0.02, inner));
    }

    private static int RunLookCheck(int seed)
    {
        _fails = 0;
        Console.WriteLine($"재질 그림 점검 (v16.5a) · 시드 {seed}\n");
        var root = LookRoot();
        Check("저장소 — game/assets/textures · game/src/View 를 찾았다", root != null, root ?? "없음");
        if (root == null) { Console.WriteLine($"\n✘ {_fails}개 실패"); return 1; }
        string texDir = Path.Combine(root, "game", "assets", "textures"), viewDir = Path.Combine(root, "game", "src", "View");

        // 1) 생성기 = 표 · 파일 존재 · 파일 = 생성기 (결정론 지문)
        var t0 = DateTime.UtcNow;
        var genA = Gen.All();
        var genB = Gen.All();
        var spec = LookSpec.AllFiles();
        var names = genA.Select(g => g.name).ToList();
        Check($"표 — 생성기 그림 {names.Count}장 = LookSpec 이름 {spec.Length}개 (순서 무관 · 빠짐 · 남음 없음)",
            names.OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(spec.OrderBy(n => n, StringComparer.Ordinal)),
            $"생성기에만: {string.Join(",", names.Except(spec))} · 표에만: {string.Join(",", spec.Except(names))}");
        var hashA = genA.ToDictionary(g => g.name, g => Png.Hash(g.pix.Rgba8()));
        var hashB = genB.ToDictionary(g => g.name, g => Png.Hash(g.pix.Rgba8()));
        var nondet = names.Where(n => hashA[n] != hashB[n]).ToList();
        Check("결정론 — 같은 입력으로 두 번 만든 그림의 지문이 모두 같다", nondet.Count == 0,
            nondet.Count > 0 ? "다름: " + string.Join(",", nondet) : $"{names.Count}장 · {(DateTime.UtcNow - t0).TotalSeconds:0.0}초");
        var missing = new List<string>();
        var stale = new List<string>();
        var noImport = new List<string>();
        foreach (var (name, pix) in genA)
        {
            var path = Path.Combine(texDir, name + ".png");
            if (!File.Exists(path)) { missing.Add(name); continue; }
            if (!File.Exists(path + ".import") || !File.ReadAllText(path + ".import").Contains($"res://assets/textures/{name}.png")) noImport.Add(name);
            var got = Png.Decode(File.ReadAllBytes(path));
            if (got == null || got.Value.w != pix.W || got.Value.h != pix.H || Png.Hash(got.Value.rgba) != hashA[name]) stale.Add(name);
        }
        Check("파일 — 그림 파일이 모두 있고 Godot 가져오기 파일(.import)도 있다", missing.Count == 0 && noImport.Count == 0,
            (missing.Count > 0 ? "없음: " + string.Join(",", missing) : "") + (noImport.Count > 0 ? " · .import 없음: " + string.Join(",", noImport) : "") + $" · {texDir}");
        Check("파일 = 생성기 — 저장된 PNG 픽셀 지문이 생성기 결과와 같다 (팔레트 · 코드가 바뀌면 다시 만들 것)", stale.Count == 0,
            stale.Count > 0 ? "다름: " + string.Join(",", stale) + " → dotnet run --project tools/texgen -c Release" : string.Join(" ", genA.Take(4).Select(g => $"{g.name[5..]}:{hashA[g.name]:x8}")) + " …");

        var tex = genA.ToDictionary(g => g.name, g => g.pix);

        // 2) 바닥재 · 벽: 불투명 · 무늬가 있다 · 서로 다르다 · 이음매 없음
        var surfaces = LookSpec.FloorFiles.Concat(LookSpec.WallFiles).ToList();
        var flat = new List<string>();
        var seams = new List<string>();
        var holes = new List<string>();
        foreach (var n in surfaces)
        {
            var p = tex[n];
            if (p.A.Any(a => a < 0.999f)) holes.Add(n);
            double mean = 0, sq = 0;
            for (int i = 0; i < p.W * p.H; i++) { float l = LookLum(p, i); mean += l; sq += l * l; }
            mean /= p.W * p.H;
            double sd = Math.Sqrt(Math.Max(0, sq / (p.W * p.H) - mean * mean));
            if (sd < 0.025) flat.Add($"{n}({sd:0.000})");
            float seam = LookSeam(p);
            if (seam > 1.6f) seams.Add($"{n}({seam:0.0})");
        }
        Check("바닥 · 벽 — 모두 불투명하고 무늬가 있다 (밝기 표준편차 ≥ 0.025 — 단색 사각형 금지)", holes.Count == 0 && flat.Count == 0,
            (holes.Count > 0 ? "구멍: " + string.Join(",", holes) : "") + (flat.Count > 0 ? " 밋밋: " + string.Join(",", flat) : "") + $" · {surfaces.Count}장");
        Check("바닥 · 벽 — 가장자리가 감싸 이어진다 (감싼 경계 차이 ÷ 안쪽 주기 경계 차이 ≤ 1.6)", seams.Count == 0,
            seams.Count > 0 ? string.Join(",", seams) : string.Join(" ", surfaces.Select(n => $"{n[5..]}:{LookSeam(tex[n]):0.0}")));
        var alike = new List<string>();
        for (int a = 0; a < surfaces.Count; a++)
        for (int b = a + 1; b < surfaces.Count; b++)
        {
            Pix pa = tex[surfaces[a]], pb = tex[surfaces[b]];
            double d = 0;
            for (int i = 0; i < pa.W * pa.H; i += 7) d += MathF.Abs(pa.R[i] - pb.R[i]) + MathF.Abs(pa.G[i] - pb.G[i]) + MathF.Abs(pa.B[i] - pb.B[i]);
            d /= (pa.W * pa.H / 7) * 3;
            // 무늬 차이: 평균색을 빼고 비교 (색만 바꾼 같은 무늬를 잡는다)
            double ma = 0, mb = 0;
            for (int i = 0; i < pa.W * pa.H; i += 7) { ma += LookLum(pa, i); mb += LookLum(pb, i); }
            ma /= pa.W * pa.H / 7; mb /= pa.W * pa.H / 7;
            double dp = 0;
            for (int i = 0; i < pa.W * pa.H; i += 7) dp += Math.Abs((LookLum(pa, i) - ma) - (LookLum(pb, i) - mb));
            dp /= pa.W * pa.H / 7;
            if (d < 0.05 || dp < 0.03) alike.Add($"{surfaces[a][5..]}~{surfaces[b][5..]}({d:0.00}/{dp:0.00})");
        }
        Check("바닥 · 벽 — 그림끼리 서로 다르다 (색 차이 ≥ 0.05 · 평균색을 뺀 무늬 차이 ≥ 0.03)", alike.Count == 0, alike.Count > 0 ? string.Join(",", alike) : $"{surfaces.Count * (surfaces.Count - 1) / 2}쌍");

        // 3) 매핑: 모든 방 × 바닥재 × 가장자리 → 그림 · 모든 그림이 쓰인다
        var floorMats = new[] { Material.Grate, Material.Rubber, Material.Tile, Material.Carpet, Material.MetalPlate, Material.None };
        var used = new HashSet<LookSpec.FloorLook>();
        var badMap = new List<string>();
        foreach (var t in Enum.GetValues<RoomType>())
        {
            var core = Materials.FloorFor(t);
            if (Array.IndexOf(floorMats, core) < 0) badMap.Add($"{t}:Core바닥재 {core}");
            foreach (var m in floorMats)
            foreach (var rim in new[] { false, true })
            {
                var f = LookSpec.Floor(m, t, rim);
                if ((int)f < 0 || (int)f >= LookSpec.FloorFiles.Length || !tex.ContainsKey(LookSpec.FloorFiles[(int)f])) badMap.Add($"{t}/{m}/{rim}");
                else if (m == Material.None || m == core) used.Add(f);
            }
        }
        var unusedLooks = Enum.GetValues<LookSpec.FloorLook>().Where(f => !used.Contains(f)).ToList();
        int rooms = Enum.GetValues<RoomType>().Length;
        Check($"매핑 — 방 {rooms}종 × 바닥재 6 × 가장자리 2 가 모두 바닥 그림을 얻는다", badMap.Count == 0, badMap.Count > 0 ? string.Join(",", badMap.Take(8)) : $"{rooms * 12}가지");
        Check("매핑 — 바닥 그림 6종이 모두 실제 배(Core 기본 바닥재)에서 쓰인다", unusedLooks.Count == 0 && Enum.GetValues<LookSpec.FloorLook>().Length == LookSpec.FloorFiles.Length,
            unusedLooks.Count > 0 ? "안 쓰임: " + string.Join(",", unusedLooks) : string.Join(" ", Enum.GetValues<RoomType>().Take(14).Select(t => $"{t}:{LookSpec.Floor(Materials.FloorFor(t), t, false)}")) + " …");
        var badOv = Enum.GetValues<LookSpec.Ov>().Where(o => (int)o >= LookSpec.OvFiles.Length || !tex.ContainsKey(LookSpec.OvFiles[(int)o]) ||
            floorMats.Any(m => LookSpec.OvStyle(o, m).maxA <= 0.05f || LookSpec.OvStyle(o, m).soft <= 0f)).ToList();
        Check("매핑 — 상태 겹치기 7종(닳음 · 물기 · 기름 · 그을음 · 녹 · 먼지 · 서리)이 그림 · 모양을 가진다", badOv.Count == 0 && LookSpec.OvFiles.Length == 7,
            badOv.Count > 0 ? string.Join(",", badOv) : string.Join(" ", LookSpec.OvFiles.Select(f => f[8..])));

        // 4) 겹치기: 양 = 덮인 넓이 (드러나는 순서가 고르게 펴져 있다) · 서로 다른 모양
        var cover = new List<string>();
        foreach (var n in LookSpec.OvFiles)
        {
            var p = tex[n];
            int half = p.A.Count(a => a > 0.5f);
            float frac = half / (float)(p.W * p.H);
            if (MathF.Abs(frac - 0.5f) > 0.03f) cover.Add($"{n}({frac:0.00})");
        }
        Check("겹치기 — 양 0.5 에서 절반쯤 덮인다 (드러나는 순서를 순위로 고르게 폈다)", cover.Count == 0, cover.Count > 0 ? string.Join(",", cover) : "7장 모두 0.47~0.53");
        var ovAlike = new List<string>();
        for (int a = 0; a < LookSpec.OvFiles.Length; a++)
        for (int b = a + 1; b < LookSpec.OvFiles.Length; b++)
        {
            Pix pa = tex[LookSpec.OvFiles[a]], pb = tex[LookSpec.OvFiles[b]];
            int agree = 0, n = 0;
            for (int i = 0; i < pa.W * pa.H; i += 5, n++) if ((pa.A[i] > 0.7f) == (pb.A[i] > 0.7f)) agree++;
            if (agree / (float)n > 0.9f) ovAlike.Add($"{LookSpec.OvFiles[a]}~{LookSpec.OvFiles[b]}");
        }
        Check("겹치기 — 상태마다 번지는 모양이 다르다 (양 0.3 에서 덮인 자리가 90% 넘게 겹치지 않는다)", ovAlike.Count == 0, ovAlike.Count > 0 ? string.Join(",", ovAlike) : "21쌍");

        // 5) 흔적 · 입자: 칸마다 그림이 있고 서로 다르다
        var dec = tex[LookSpec.DecalFile];
        int slots = Enum.GetValues<LookSpec.Decal>().Length;
        var emptySlots = new List<string>();
        var sigs = new List<double>();
        for (int k = 0; k < slots; k++)
        {
            int sx = (k % LookSpec.DecalCols) * LookSpec.DecalPx, sy = (k / LookSpec.DecalCols) * LookSpec.DecalPx, on = 0;
            double sig = 0;
            for (int y = 0; y < LookSpec.DecalPx; y++)
            for (int x = 0; x < LookSpec.DecalPx; x++)
            {
                int i = dec.I(sx + x, sy + y);
                if (dec.A[i] > 0.25f) { on++; sig += (x * 31 + y * 17) * dec.A[i] * (dec.R[i] + 2 * dec.G[i] + 3 * dec.B[i]); }
            }
            if (on < 40) emptySlots.Add($"{(LookSpec.Decal)k}({on})");
            sigs.Add(Math.Round(sig, 1));
        }
        Check($"흔적 — 아틀라스 {slots}칸(얼룩 · 발자국 · 긁힘 · 테이프 · 쪽지 · 용접 · 탄 자국 · 금)이 모두 그려져 있고 서로 다르다",
            emptySlots.Count == 0 && sigs.Distinct().Count() == slots && slots == LookSpec.DecalCols * LookSpec.DecalRows && dec.W == LookSpec.DecalCols * LookSpec.DecalPx,
            emptySlots.Count > 0 ? "빈칸: " + string.Join(",", emptySlots) : $"{slots}칸 · {dec.W}×{dec.H}");
        var par = tex[LookSpec.ParticleFile];
        int parts = Enum.GetValues<LookSpec.Part>().Length;
        var emptyParts = new List<string>();
        for (int k = 0; k < parts; k++)
        {
            int on = 0;
            for (int y = 0; y < LookSpec.PartPx; y++)
            for (int x = 0; x < LookSpec.PartPx; x++) if (par.A[par.I(k * LookSpec.PartPx + x, y)] > 0.2f) on++;
            if (on < 6) emptyParts.Add($"{(LookSpec.Part)k}({on})");
        }
        Check($"입자 — {parts}칸(김 · 연기 · 물방울 · 튀는 물 · 불꽃 · 불씨 · 먼지 · 결로)이 모두 그려져 있다", emptyParts.Count == 0 && par.W == parts * LookSpec.PartPx,
            emptyParts.Count > 0 ? string.Join(",", emptyParts) : $"{parts}칸 · {par.W}×{par.H}");

        // 6) 빛: 방 색온도 (기관 청록 · 생활 호박) · 정전 어둠 · 비상등 붉음 · 설비 불빛
        var badTemp = new List<string>();
        foreach (var t in Enum.GetValues<RoomType>())
        {
            var (r, g, b) = LookSpec.Temperature(t);
            if (LookSpec.EngineZone(t) && !(b > r + 0.15f)) badTemp.Add($"{t}:기관인데 청록 아님");
            if (LookSpec.Living(t) && !(r > b + 0.2f)) badTemp.Add($"{t}:생활인데 호박 아님");
            if (r <= 0f || g <= 0f || b <= 0f) badTemp.Add($"{t}:0");
        }
        Check("빛 — 방마다 색온도 (기관 구역 청록 · 생활 구역 호박) · 정전은 어둡고 비상등은 붉다",
            badTemp.Count == 0 && LookSpec.AmbientDark < 0.12f && LookSpec.AmbientLit > 3f * LookSpec.AmbientDark && LookSpec.Emergency.r > 3f * LookSpec.Emergency.g,
            badTemp.Count > 0 ? string.Join(",", badTemp.Take(6)) : $"엔진실 {LookSpec.Temperature(RoomType.Engine)} · 침실 {LookSpec.Temperature(RoomType.Quarters)} · 어둠 {LookSpec.AmbientDark}");
        int glows = Enum.GetValues<FurnitureType>().Count(t => LookSpec.Glow(t) != null);
        Check("빛 — 화면 · 콘솔 · 노심 · 재배등 · 화구 등 불빛을 내는 설비가 10종 이상", glows >= 10, $"{glows}종");

        // 7) 화면 코드: 표를 쓰고 Core 상태를 읽기만 한다
        var lookFiles = Directory.GetFiles(viewDir, "ShipViewLook*.cs").Concat(Directory.GetFiles(viewDir, "LookTextures.cs")).OrderBy(f => f, StringComparer.Ordinal).ToList();
        var src = string.Join("\n", lookFiles.Select(File.ReadAllText));
        var needs = new[] { "LookSpec.Floor(", "Body.Marks", "Body.Wear", "DustField", "Portable.Lights", "Fire.Fires", "LookSpec.Temperature(", "LookSpec.Glow(", "BlendModeEnum.Mul", "LookSpec.MaxParticles", "LookSpec.LodOf(" };
        var lacks = needs.Where(n => !src.Contains(n)).ToList();
        Check("화면 — 바닥 · 겹치기 · 빛 · 입자 코드가 표(LookSpec)와 Core 상태를 읽는다", lookFiles.Count >= 3 && lacks.Count == 0,
            $"파일 {lookFiles.Count}개" + (lacks.Count > 0 ? " · 없음: " + string.Join(",", lacks) : ""));
        var writes = Regex.Matches(src, @"\.(SetMark|RaiseMark|Drip|PlaceNow|CloseHatch|KnockDown|Add\(ArticleKind)|_world\.\w+\s*=[^=]|\bRng\b|System\.Random").Select(m => m.Value).ToList();
        Check("화면 — Core 상태를 바꾸지 않고 시뮬레이션 난수를 쓰지 않는다", writes.Count == 0, writes.Count > 0 ? string.Join(",", writes.Distinct()) : "쓰기 없음");

        // 8) 결정론: 화면은 시뮬레이션에 끼지 않는다 — 같은 시드 두 번 같은 지문
        uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
        uint h1 = H(), h2 = H();
        Check("결정론 — 같은 시드 두 번 돌린 세계 지문이 같다", h1 == h2, $"{h1:x8} / {h2:x8}");

        Console.WriteLine(_fails == 0 ? "\n✔ 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
