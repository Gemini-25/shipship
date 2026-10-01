using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v15.5 시대 기술 18 → 70: 시대마다 새 기술 8~9 · 익히면 연결된 수치가 실제로 바뀌고 · 새 위험이 붙는다
public static partial class Program
{
    private static int RunEraV15Test(int seed)
    {
        _fails = 0;
        Console.WriteLine($"시대 기술 70 점검 (v15.5) · 시드 {seed}\n");
        try
        {
            var all = EraSystem.All;
            var fresh = ErasV15.Rows.Select(r => r.Tech).ToList();
            var keys = new HashSet<string>(new[] { "meteor", "bigmeteor", "fire", "break", "pipe" }
                .Concat(Hazards.All.Where(h => h.Kind != HazardKind.RescueSignal).Select(h => h.Kind.ToString()))); // 사고 고르는 무게 표에 실제로 있는 키
            var stats = new HashSet<string>(ErasV15.Stats);
            (string stat, float mul)[] Fx(string id) => ErasV15.Rows.First(r => r.Tech.Id == id).Fx;
            static void Learn(World w, string id) { w.Eras.Known.Add(id); w.Eras.Order.Add(id); }
            static bool Near(float a, float b, float tol = 1e-4f) => MathF.Abs(a - b) <= tol;

            // 0) 목록
            {
                var perEra = EraSystem.Eras.Select(e => fresh.Count(t => t.Era == e.era)).ToList();
                var perField = Enum.GetValues<TechField>().Select(f => fresh.Count(t => t.Field == f)).ToList();
                bool unique = all.Select(t => t.Id).Distinct().Count() == all.Length && all.Select(t => t.Name).Distinct().Count() == all.Length;
                Check("목록 — 기술 70 · 새 기술은 시대마다 8~9개 · 열세 분야에 넷씩 · id·이름이 겹치지 않는다",
                    all.Length == 70 && fresh.Count == 52 && perEra.All(n => n is >= 8 and <= 9) && perField.All(n => n == 4) && unique,
                    $"기술 {all.Length} (새 {fresh.Count}) · 시대별 새 기술 {string.Join("/", perEra)} · 분야별 {string.Join("/", perField)}");
                var badFx = ErasV15.Rows.Where(r => r.Fx.Length == 0 || r.Fx.Any(f => !(stats.Contains(f.stat) || keys.Contains(f.stat)) || f.mul == 1f)).Select(r => r.Tech.Id).ToList();
                var badRisk = fresh.Where(t => t.Risk != "없음" ? t.RiskKey == null || !keys.Contains(t.RiskKey) || t.RiskMul <= 1f : t.RiskKey != null).Select(t => t.Id).ToList();
                var unused = ErasV15.Stats.Where(s => !ErasV15.Rows.Any(r => r.Fx.Any(f => f.stat == s))).ToList();
                Check("효과·위험 — 새 기술마다 연결된 수치가 있고, 위험 키는 실제 사고 무게 표에 있다",
                    badFx.Count == 0 && badRisk.Count == 0 && unused.Count == 0,
                    $"수치 {ErasV15.Stats.Length}개 모두 쓰임 · 사고 키 효과 {ErasV15.Rows.Sum(r => r.Fx.Count(f => keys.Contains(f.stat)))} · 위험 {fresh.Count(t => t.RiskKey != null)}"
                    + (badFx.Count + badRisk.Count + unused.Count > 0 ? $" · 잘못: {string.Join(",", badFx.Concat(badRisk).Concat(unused))}" : "")
                    + $" · 예: {fresh[0].Name} — {fresh[0].Effect}");
            }

            // 1) 새 기술 10개를 얻으면 Mul 값이 그만큼 바뀐다 (얻기 전에는 1)
            var ten = new[] { "coolantdope", "predictive", "nftloop", "vcd", "amine", "labnotes", "telemed", "additive", "fibernet", "shapememory" };
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                var pairs = ten.SelectMany(id => Fx(id).Select(f => (id, f.stat, f.mul))).ToList();
                bool allOne = pairs.All(p => ErasV15.Mul(w, p.stat) == 1f);
                foreach (var id in ten) Learn(w, id);
                var wrong = pairs.Where(p => !Near(ErasV15.Mul(w, p.stat), p.mul)).Select(p => $"{p.id}:{p.stat} ×{ErasV15.Mul(w, p.stat):0.###}").ToList();
                Check("새 기술 10개 — 얻기 전엔 ×1, 얻은 뒤엔 적힌 배율 그대로 (마모·고장·작물·물·산소·연구·회복·제작·전조·선체)", allOne && wrong.Count == 0,
                    string.Join(" · ", pairs.Select(p => $"{p.stat} ×{ErasV15.Mul(w, p.stat):0.##}")) + (wrong.Count > 0 ? $" · 틀림: {string.Join(", ", wrong)}" : ""));
                Check("일 속도 — 수리·제작·조리·치료 일이 제 수치를 읽는다", ErasV15.Work(WorkKind.Repair) == "repair" && ErasV15.Work(WorkKind.Fabricate) == "craft"
                    && ErasV15.Work(WorkKind.Cook) == "cook" && ErasV15.Work(WorkKind.Treat) == "treat" && ErasV15.Work(null) == "" && ErasV15.Mul(w, ErasV15.Work(WorkKind.Fabricate)) > 1.1f,
                    $"제작 일 ×{ErasV15.Mul(w, ErasV15.Work(WorkKind.Fabricate)):0.##} · 아무 일 아닌 것 ×{ErasV15.Mul(w, ErasV15.Work(null)):0.##}");
            }

            // 2) 실제 시뮬 수치: 같은 시드 두 배 — 한쪽만 10개를 익히고 한 시간
            {
                var a = World.CreateDefault(seed, 0, "Hanbit");
                var b = World.CreateDefault(seed, 0, "Hanbit");
                foreach (var id in ten) Learn(b, id);
                float ra0 = a.Research, rb0 = b.Research;
                Run(a, SimTime.Hours(1));
                Run(b, SimTime.Hours(1));
                float water = b.Water.Produced / MathF.Max(1e-4f, a.Water.Produced);
                float o2 = b.Air.O2Capacity / MathF.Max(1e-4f, a.Air.O2Capacity);
                float research = (b.Research - rb0) / MathF.Max(1e-4f, a.Research - ra0);
                Check("시뮬 — 정수기 물 회수 ×1.12 · 산소 발생 ×1.08 · 연구 ×1.1", Near(water, 1.12f, 0.01f) && Near(o2, 1.08f, 0.01f) && Near(research, 1.1f, 0.01f),
                    $"물 {a.Water.Produced:0.00} → {b.Water.Produced:0.00} L/h (×{water:0.000}) · 산소 {a.Air.O2Capacity:0.0} → {b.Air.O2Capacity:0.0} (×{o2:0.000}) · 연구 {a.Research - ra0:0.00} → {b.Research - rb0:0.00} (×{research:0.000})");

                // 설비 한 번 돌리기: 마모 증가 · 작물 성장 (설비마다 비율, 가운데 값)
                var la = a.Ship.Machines.ToList();
                var lb = b.Ship.Machines.ToList();
                var wa = la.Select(m => m.Wear).ToList();
                var wb = lb.Select(m => m.Wear).ToList();
                var ga = la.Select(m => m.Crop?.Growth ?? -1f).ToList();
                var gb = lb.Select(m => m.Crop?.Growth ?? -1f).ToList();
                a.Machines.Update(0.25f);
                b.Machines.Update(0.25f);
                float Median(List<float> xs) { xs.Sort(); return xs.Count == 0 ? 0f : xs[xs.Count / 2]; }
                var wear = new List<float>();
                var grow = new List<float>();
                int n = Math.Min(la.Count, lb.Count);
                for (int i = 0; i < n; i++)
                {
                    var ma = la[i];
                    var mb = lb[i];
                    float da = ma.Wear - wa[i], db = mb.Wear - wb[i];
                    if (da > 1e-7f && ma.Wear < 1f && mb.Wear < 1f) wear.Add(db / da);
                    if (ma.Crop is { Ripe: false } ca && mb.Crop is { Ripe: false } cb && ga[i] >= 0f && ca.Growth - ga[i] > 1e-7f) grow.Add((cb.Growth - gb[i]) / (ca.Growth - ga[i]));
                }
                float mw = Median(wear), mg = Median(grow);
                Check("시뮬 — 설비 마모 ×0.92 · 작물 성장 ×1.1 (설비마다 견준 가운데 값)", wear.Count >= 5 && grow.Count >= 1 && Near(mw, 0.92f, 0.01f) && Near(mg, 1.1f, 0.01f),
                    $"마모 ×{mw:0.000} (설비 {wear.Count}) · 작물 ×{mg:0.000} (재배대 {grow.Count})");

                // 사고 무게: 줄이는 쪽(형상 기억 합금)과 늘리는 쪽(양액 순환 재배의 위험)
                float crack = b.Eras.RiskMul(nameof(HazardKind.HullCrack)) / a.Eras.RiskMul(nameof(HazardKind.HullCrack));
                float window = b.Eras.RiskMul(nameof(HazardKind.WindowCrack)) / a.Eras.RiskMul(nameof(HazardKind.WindowCrack));
                float nutrient = b.Eras.RiskMul(nameof(HazardKind.NutrientCrash)) / a.Eras.RiskMul(nameof(HazardKind.NutrientCrash));
                Check("사고 무게 — 선체 균열 ×0.7 · 창 균열 ×0.6 줄고, 양액 오염 ×1.3 는다", Near(crack, 0.7f) && Near(window, 0.6f) && Near(nutrient, 1.3f),
                    $"선체 균열 ×{crack:0.00} · 창 균열 ×{window:0.00} · 양액 오염 ×{nutrient:0.00}");
            }

            // 3) 위험 배율: 새 기술을 하나씩 더 익혀 가면 제 위험 키 무게가 적힌 배율만큼, 제 효과 키는 효과 배율만큼 바뀐다
            {
                var w = World.CreateDefault(seed, 0, "Mirinae");
                var wrong = new List<string>();
                int risks = 0, guards = 0;
                foreach (var r in ErasV15.Rows)
                {
                    var t = r.Tech;
                    var probe = r.Fx.Where(f => keys.Contains(f.stat)).Select(f => (f.stat, f.mul)).ToList();
                    if (t.RiskKey != null) probe.Add((t.RiskKey, t.RiskMul));
                    var before = probe.Select(p => w.Eras.RiskMul(p.Item1)).ToList();
                    Learn(w, t.Id);
                    for (int i = 0; i < probe.Count; i++)
                    {
                        float got = w.Eras.RiskMul(probe[i].Item1) / before[i];
                        if (!Near(got, probe[i].Item2, 1e-3f)) wrong.Add($"{t.Id}:{probe[i].Item1} ×{got:0.###}");
                    }
                    risks += t.RiskKey != null ? 1 : 0;
                    guards += probe.Count - (t.RiskKey != null ? 1 : 0);
                }
                Check("위험 배율 — 새 기술 하나하나가 제 위험 키는 늘리고 제 효과 키는 줄인다 (RiskMul)", wrong.Count == 0 && risks >= 35 && guards >= 30,
                    $"위험 {risks}개 · 줄이는 사고 {guards}개" + (wrong.Count > 0 ? $" · 틀림: {string.Join(", ", wrong.Take(6))}" : ""));
            }

            // 4) 연구로 얻는 흐름: 연구가 쌓이면 회의가 새 기술도 고르고, 익힌 것만 수치에 붙는다
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                Run(w, SimTime.Hours(1));
                for (int i = 0; i < 36; i++) { w.Research += 300f; Run(w, SimTime.Minutes(10)); }
                var got = w.Eras.Order.Where(id => ErasV15.Rows.Any(r => r.Tech.Id == id)).ToList();
                var chosen = w.History.Events.Count(e => e.Text.Contains("다음 연구는") && fresh.Any(t => e.Text.Contains(t.Name)));
                var mismatch = ErasV15.Stats.Where(s =>
                {
                    float expect = 1f;
                    foreach (var r in ErasV15.Rows) if (w.Eras.Known.Contains(r.Tech.Id)) foreach (var f in r.Fx) if (f.stat == s) expect *= f.mul;
                    return !Near(ErasV15.Mul(w, s), expect, 1e-4f);
                }).ToList();
                Check("연구 흐름 — 회의가 새 기술을 골라 익히고, 수치는 익힌 것만큼", got.Count >= 10 && chosen >= 10 && mismatch.Count == 0,
                    $"익힌 기술 {w.Eras.Known.Count} (새 {got.Count}) · 회의가 고른 새 기술 {chosen} · 처음 다섯: {string.Join(", ", got.Take(5).Select(id => EraSystem.All.First(t => t.Id == id).Name))}"
                    + $" · 연구 ×{ErasV15.Mul(w, "research"):0.00} · 수리 ×{ErasV15.Mul(w, "repair"):0.00}" + (mismatch.Count > 0 ? $" · 어긋남: {string.Join(",", mismatch)}" : ""));
            }

            // 5) 결정론
            {
                uint H()
                {
                    var w = World.CreateDefault(seed, 0, "Hanbit");
                    Run(w, SimTime.TicksPerDay + SimTime.Hours(6));
                    return SaveGame.StateHash(w);
                }
                uint a = H(), b = H();
                Check("결정론 — 같은 시드 같은 지문", a == b, $"{a:x8} / {b:x8}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"예외: {ex}");
            _fails++;
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 시대 기술 70 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
