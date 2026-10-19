using ShipEngine;

namespace Lab;

/// <summary>④ 열 · 진동 (깊이 바퀴 3) — 막힌 열 응력 · 피로(마이너) · 진동이 금을 키운다. docs/ENGINE.md ④ '열 · 진동'</summary>
public static partial class Program
{
    private static void HeatVibLaws()
    {
        if (!Do("열 · 진동")) return;
        Console.WriteLine("\n[④ 열 · 진동 — 막힌 열 응력 · 피로 · 진동]");
        var t = Tables.Default;
        var lay = Worlds.Ascii(Worlds.TwoRooms);
        int hull = Worlds.At(lay, 4, 0), corner = Worlds.At(lay, 0, 0);
        var m = t.Materials[lay.Material[hull]].Def;

        // ── 막힌 열 응력: σ = −E·α·ΔT × 0.5 (구속) — 골조가 만나는 이음매는 × 3 ──
        {
            var s = FieldStructure(lay, 1);
            var st = s.Get<Structure>(); var f = s.Get<Field>();
            f.Ts[hull] = 393; f.Ts[corner] = 393;
            double want = -m.E * 1000 * m.Alpha * 100 * Structure.ThermalRestraint;
            Check("막힌 열 응력: 100K 데운 강철 판은 −E·α·ΔT × 0.5 로 눌린다 · 골조가 만나는 이음매는 세 배",
                Rel(st.ThermalSigma(hull), want) < 1e-9 && Rel(st.ThermalSigma(corner), 3 * want) < 1e-9,
                $"판 {st.ThermalSigma(hull):0.0}MPa (셈 {want:0.0}) · 이음매 {st.ThermalSigma(corner):0.0}MPa");
        }

        // ── 피로(마이너): 피로 한계(피로 몫 × 강도) 아래의 되풀이는 아무것도 남기지 않는다 · 넘으면 넘친 몫의 세제곱만큼 쌓여 0.85에서 금이 된다 ──
        {
            var s = FieldStructure(lay, 1);
            var st = s.Get<Structure>();
            double end = m.Fatigue * m.Strength;
            st.LoadCycle(s, hull, end * 0.95, 100_000);
            float below = st.Fatigue[hull];
            const double range = 200;
            double x = (range - end) / end, per = Structure.SnK * x * x * x;
            int need = (int)Math.Ceiling(0.85 / per);          // 반 주기
            st.LoadCycle(s, hull, range, need / 2 - 2);
            bool before = st.Crack[hull] <= 0;
            st.LoadCycle(s, hull, range, 3);
            string story = s.Causes.Story(st.CrackCause[hull]);
            Check($"피로: 한계({end:0}MPa) 아래 10만 주기는 흔적이 없다 · {range:0}MPa 되풀이는 반 주기 {need}번에 금이 된다 (마이너 · 원인: 응력이 되풀이돼 피로)",
                below == 0 && before && st.Crack[hull] > 0 && st.ThermalCracks == 1 && story.Contains("되풀이돼") && story.Contains("피로해졌다"),
                $"한계 아래 피로 {below} · 금 전 {before} · 금 {st.Crack[hull] * 1000:0.0}mm · \"{story}\"");
        }

        // ── 열 되돌이: 외판 겉면을 데웠다 식히기를 되풀이하면 (불 · 그늘과 볕) 피로가 쌓여 결국 금이 간다 ──
        {
            var s = FieldStructure(lay, 1);
            var st = s.Get<Structure>(); var f = s.Get<Field>();
            s.Run(3);
            int k = 0;
            for (; k < 400 && st.Crack[hull] <= 0; k++) { f.Ts[hull] = k % 2 == 0 ? 600 : 293; s.Run(1); }
            string story = s.Causes.Story(st.CrackCause[hull]);
            Check("열 되돌이: 외판을 600K ↔ 293K로 데웠다 식히기를 되풀이하면 피로가 쌓여 (정격이 깎여) 금이 간다 — 압력 순환이 없어도 · 원인: 열 팽창 · 수축",
                st.Crack[hull] > 0 && st.Fatigue[hull] > 0.3f && story.Contains("열 팽창 · 수축"), $"{k}틱 · 금 {st.Crack[hull] * 1000:0.0}mm · 피로 {st.Fatigue[hull]:0.00} · \"{story}\"");
        }

        // ── 진동: 문턱 ΔK 아래는 그대로 · 넘으면 횟수 × 파리스 법칙만큼 자란다 (곁 칸은 거리로 나눈 폭) ──
        {
            int part = Worlds.At(lay, 10, 2), floor = Worlds.At(lay, 11, 2);
            var s = FieldStructure(lay, 1, sim => sim.Get<Structure>().SeedCrack(part, 0.05, 0));
            var st = s.Get<Structure>();
            var pm = t.Materials[lay.Material[part]].Def;
            double weak = 2, strong = 40; const int n = 90_000;
            st.Vibrate(s, floor, weak, n, 0);
            bool still = st.Crack[part] == 0.05f;
            double dk = st.StressIntensity(part, strong / 2);
            double want = 0.05 + n * pm.ParisC * Math.Pow(dk, pm.ParisM);
            st.Vibrate(s, floor, strong, n, 0);
            Check($"진동: 약한 떨림({weak:0}MPa)은 문턱 아래라 금이 그대로 · {strong:0}MPa로 한 시간(9만 번) 흔들면 곁 벽의 금이 파리스 법칙만큼 자란다",
                still && st.Crack[part] > 0.05f && Rel(st.Crack[part], want) < 0.01,
                $"약한 떨림 뒤 {still} · ΔK {dk:0.0} · {st.Crack[part] * 1000:0.000}mm (셈 {want * 1000:0.000}mm)");
        }
    }
}
