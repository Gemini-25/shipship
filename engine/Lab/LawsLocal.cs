using ShipEngine;

namespace Lab;

/// <summary>③ 국소 현상 (깊이 바퀴 3) — 칸 안 위아래 두 층 · 막은 구멍. docs/ENGINE.md ③ '국소 현상'</summary>
public static partial class Program
{
    private static void LocalLaws()
    {
        if (!Do("국소 현상")) return;
        Console.WriteLine("\n[③ 국소 현상 — 칸 안 위아래 두 층 · 막은 구멍]");
        var t = Tables.Default;
        var lay = Worlds.Ascii(Worlds.Sealed);
        var room = Enumerable.Range(0, lay.N).Where(c => lay.Kind[c] == CellKind.Floor && lay.X(c) < 10).ToList();
        int mid = Worlds.At(lay, 5, 3);

        // 방 전체를 데우고(바닥 · 벽보다 dT 뜨겁게) 연기 · 일산화탄소를 넣은 세계 — 틱마다 데워 층을 유지한다
        (Sim s, Field f) Hot(double dT, Action<Field>? more = null, bool heatEveryTick = true)
        {
            var s = Worlds.FieldOnly(lay, 1);
            var f = s.Get<Field>();
            foreach (int c in room) { f.AddGas(c, t.gSoot, 0.05, f.Temp(c)); f.AddGas(c, t.gCO, 0.2, f.Temp(c)); }
            more?.Invoke(f);
            for (int k = 0; k < 60; k++)
            {
                if (heatEveryTick || k == 0) foreach (int c in room) { double want = f.Ts[c] + dT, now = f.Temp(c); if (now < want) f.AddHeat(c, (want - now) * f.GasC(c)); }
                s.Step();
            }
            return (s, f);
        }

        // ── 뜨거운 층: 고요한 방의 뜨거운 기체(바닥보다 300K) — 층이 서고(s≈1) 연기 · 일산화탄소는 위에 · 몰은 칸 그대로 (위 + 아래 = 둘 × 평균) · 아래 공기는 바닥보다 차갑지 않다 ──
        {
            var (s, f) = Hot(300);
            double up = f.HeadPpm(t.gCO, mid, false), low = f.HeadPpm(t.gCO, mid, true), mean = f.Ppm(t.gCO, mid);
            double worst = 0;
            for (int g = 0; g < t.GasCount; g++) worst = Math.Max(worst, Math.Abs(f.HeadPartial(g, mid, false) + f.HeadPartial(g, mid, true) - 2 * f.Partial(g, mid)));
            double tu = f.HeadTemp(mid, false), tl = f.HeadTemp(mid, true), tm = f.Temp(mid);
            Check("뜨거운 층: 바닥 · 벽보다 300K 뜨거운 고요한 기체 — 층이 서고(s > 0.95) 일산화탄소 · 연기가 위에 (위 > 평균의 1.9배 · 아래 < 평균의 0.1) · 성분마다 위 + 아래 = 둘 × 평균 · 위 온도 + 아래 온도 = 둘 × 평균 · 아래는 바닥보다 차갑지 않다",
                f.Strat[mid] > 0.95f && up > 1.9 * mean && low < 0.1 * mean && worst < 1e-9 && Math.Abs(tu + tl - 2 * tm) < 1e-6 && tl >= f.Ts[mid] - 1e-6,
                $"s {f.Strat[mid]:0.000} · 일산화탄소 위 {up:0}ppm · 아래 {low:0}ppm · 평균 {mean:0}ppm · 분압 어긋남 {worst:0e0} · 온도 위 {tu - 273.15:0}℃ · 아래 {tl - 273.15:0}℃ · 바닥 {f.Ts[mid] - 273.15:0}℃");
        }

        // ── 섞임: 무중력이면 부력이 없어 층이 서지 않고(s = 0) · 센 공조 바람(5m/s)은 층을 흩는다 · 보통 방(바닥과 1K · 이산화탄소 1%)은 문턱 아래라 0 ──
        {
            var (_, still) = Hot(60);
            var (_, zeroG) = Hot(60, f => Array.Fill(f.Grav, 0f));
            var (_, fan) = Hot(60, f => Array.Fill(f.Airflow, 5f));
            var plain = Worlds.FieldOnly(lay, 1);
            var pf = plain.Get<Field>();
            foreach (int c in room) pf.AddGas(c, t.gCO2, pf.Moles(c) * 0.01, pf.Temp(c));
            for (int k = 0; k < 60; k++) { foreach (int c in room) { double want = pf.Ts[c] + 1, now = pf.Temp(c); if (now < want) pf.AddHeat(c, (want - now) * pf.GasC(c)); } plain.Step(); }
            Check("섞임: 무중력이면 층이 서지 않는다 (0) · 센 공조 바람(5m/s)은 고요할 때의 반 아래로 흩는다 · 보통 방(바닥과 1K · 이산화탄소 1%)은 0 — 숨 쉬는 데 층이 끼어들지 않는다",
                still.Strat[mid] > 0.9f && zeroG.Strat[mid] == 0 && fan.Strat[mid] < 0.5f * still.Strat[mid] && pf.Strat[mid] == 0,
                $"고요 {still.Strat[mid]:0.000} · 무중력 {zeroG.Strat[mid]:0.000} · 바람 5m/s {fan.Strat[mid]:0.000} · 보통 방 {pf.Strat[mid]:0.000}");
        }

        // ── 무거운 층: 소화기의 차가운 이산화탄소(−78℃ · 칸 공기의 0.4배)가 바닥에 깔린다 — 바닥 높이 이산화탄소가 평균의 1.6배 넘게 · 산소는 평균보다 적다 ──
        {
            var s = Worlds.FieldOnly(lay, 1);
            var f = s.Get<Field>();
            foreach (int c in room) f.AddGas(c, t.gCO2, f.Moles(c) * 0.4, 195);
            s.Run(15);
            double low = f.HeadPartial(t.gCO2, mid, true), up = f.HeadPartial(t.gCO2, mid, false), mean = f.Partial(t.gCO2, mid);
            double o2Low = f.HeadPartial(t.gO2, mid, true), o2 = f.Partial(t.gO2, mid);
            Check("무거운 층: 차가운 이산화탄소(−78℃)를 뿜은 방 — 반 분 만에 층이 바닥에 서고(s < −0.9) 바닥 높이 이산화탄소가 평균의 1.6배 넘게 · 위는 옅고 · 바닥의 산소는 평균보다 적다 (누운 이가 위험하다)",
                f.Strat[mid] < -0.9f && low > 1.6 * mean && up < 0.5 * mean && o2Low < o2,
                $"s {f.Strat[mid]:0.000} · 이산화탄소 바닥 {low:0.0}kPa · 위 {up:0.0}kPa · 평균 {mean:0.0}kPa · 산소 바닥 {o2Low:0.0} / 평균 {o2:0.0}kPa · 기체 {f.Temp(mid) - 273.15:0}℃");
        }

        // ── 막은 구멍: 외판의 0.05m² 구멍을 0.045m² 막으면 (상자가 빨려 붙음) 처음 10틱 동안 빠지는 몰이 막지 않았을 때의 12% 아래 ──
        {
            double Lost(float plug)
            {
                var s = Worlds.FieldOnly(lay, 1);
                var f = s.Get<Field>();
                int hole = Worlds.At(lay, 0, 3);
                f.SetHole(hole, 0.05f);
                if (plug > 0) f.SetPlug(hole, plug);
                double n0 = room.Sum(f.Moles);
                s.Run(2);   // 첫 틱에 구역을 다시 짓고 흐른다 — 처음의 빠르기 (방이 다 비기 전)
                return n0 - room.Sum(f.Moles);
            }
            double open = Lost(0), plugged = Lost(0.045f);
            Check("막은 구멍: 외판 구멍 0.05m²를 0.045m² 막으면 빠지는 공기가 막지 않았을 때의 12% 아래 (구멍 틈 = 구멍 − 막은 넓이)",
                open > 0 && plugged < 0.12 * open && plugged > 0,
                $"두 틱에 빠진 공기 — 열림 {open:0}몰 · 막음 {plugged:0.0}몰 ({plugged / Math.Max(1e-9, open) * 100:0.0}%)");
        }
    }
}
