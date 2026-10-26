using ShipEngine;

namespace Lab;

/// <summary>법칙 시험 — 기록 소음 · 몸 숫자 (docs/DEPTH_FIX.md DF10 · 근거 docs/ENGINE_DEPTH_REVIEW.md §4-15 · §4-19): 볼트 풀림 빠르기 · 산소 포화 곡선 · 피가 다시 참.</summary>
public static partial class Program
{
    /// <summary>기관실 (배전반 p · 엔진 E · 배터리 b) 하나.</summary>
    private const string EngineBay = @"
#########
#p...E..#
#b......#
#########";

    private static void Depth3Laws()
    {
        if (!Do("기록 소음 · 몸 숫자 법칙")) return;
        Console.WriteLine("\n[기록 소음 · 몸 숫자 — 볼트 풀림 빠르기 · 산소 포화 곡선 · 피가 다시 참]");

        // 볼트 풀림 빠르기: 새 엔진 (8MPa)의 볼트가 다 풀리기까지 몇 주 ~ 몇 달 (실제 재조임 주기) · 베어링이 75% 닳아 크게 떨면 사흘 안
        // (예전: 새 엔진도 50시간 — 새뜰호 열흘 '고쳤다' 73건의 대부분이 볼트)
        {
            var lay = Worlds.Ascii(EngineBay, new Dictionary<char, string> { ['p'] = "PowerPanel", ['E'] = "EngineCore", ['b'] = "Battery" });
            (double hours, double mpa) Loosen(double worn)
            {
                var s = Worlds.Full(lay, 1);
                var net = s.Get<Network>();
                int e = net.Devices.First(d => d.Type == "EngineCore").Id;
                s.Run(25);   // 망이 한 번 돌아 풀림 장부를 연다
                if (worn > 0) net.Age(e, Part.Mechanical, worn);
                double l0 = net.Loose[e];
                s.Run(Time.Hours(1));
                return (1 / Math.Max(1e-12, net.Loose[e] - l0), net.VibMPa(e));
            }
            var fresh = Loosen(0); var worn = Loosen(0.75);
            Check("볼트 풀림: 새 엔진 (8MPa)의 볼트는 2주 ~ 4달에 다 풀린다 (실제 재조임 주기 · 예전 50시간) · 베어링이 75% 닳아 크게 떠는 엔진은 사흘 안 (닳은 베어링만 빨리)",
                fresh.hours >= 14 * 24 && fresh.hours <= 120 * 24 && worn.hours <= 72,
                $"새 엔진 {fresh.mpa:0.0}MPa → {fresh.hours / 24:0.0}일 · 닳은 엔진 {worn.mpa:0.0}MPa → {worn.hours:0}시간");
        }

        // 산소 포화 곡선: 건강한 이가 쉴 때 — 해수면 0.97~0.99 (예전 0.95) · 4,500m 0.80~0.88 · 8,000m 0.55~0.65 (고산 실측 · 처음 오른 이)
        // (숨 들이쉰 산소가 기관에서 젖고 폐꽈리에서 이산화탄소와 나눠진 분압 → 피의 산소 해리 곡선 · 낮을수록 숨이 깊어진다)
        {
            double Sat(double kPa)
            {
                var s = new Sim(new SimSetup(Worlds.Ascii(Worlds.Symmetric), 1, sim => { var ff = new Field(sim.Layout, sim.Tables); sim.Add(ff); sim.Add(new Life(ff)); }));
                s.Get<Field>().FillAir(kPa);
                var life = s.Get<Life>(); int b = life.AddBody(Worlds.At(s.Layout, 5, 3));
                life[b].ActivityW = 105;
                s.Run(5);
                return life[b].SpO2;
            }
            double sea = Sat(101.325), mid = Sat(57.7), high = Sat(35.6);
            Check("산소 포화: 건강한 이가 쉴 때 해수면 0.97~0.99 (예전 0.95) · 4,500m (57.7kPa) 0.80~0.88 · 8,000m (35.6kPa) 0.55~0.65 — 고산 실측대로",
                sea >= 0.97 && sea <= 0.99 && mid >= 0.80 && mid <= 0.88 && high >= 0.55 && high <= 0.65,
                $"해수면 {sea:0.000} · 4,500m {mid:0.000} · 8,000m {high:0.000}");
        }

        // 피가 다시 찬다: 피가 멎은 산 몸은 잃은 피의 부피를 하루에 셋 중 둘쯤 되찾는다 (몸 안의 물이 핏줄로 — 헌혈 0.5L는 하루 이틀) · 예전엔 흘린 피가 나흘 그대로
        {
            var s = new Sim(new SimSetup(Worlds.Ascii(Worlds.Symmetric), 1, sim => { var ff = new Field(sim.Layout, sim.Tables); sim.Add(ff); sim.Add(new Life(ff)); }));
            s.Get<Field>().FillAir();
            var life = s.Get<Life>(); int b = life.AddBody(Worlds.At(s.Layout, 5, 3));
            life[b].BloodL = 4f;
            double w0 = life[b].Water;
            float day1 = 0;
            for (long k = 1; k <= Time.Hours(72); k++) { life[b].Water = w0; s.Step(); if (k == Time.Hours(24)) day1 = life[b].BloodL; }   // 마실 물은 늘 넉넉히 (마음이 없는 몸)
            float day3 = life[b].BloodL;
            Check("피가 다시 찬다: 1L를 잃고 피가 멎은 몸 — 하루 뒤 4.5~4.75L (부피의 셋 중 둘) · 사흘 뒤 4.9L 넘게 (예전: 나흘 그대로 4.0L)",
                life[b].Alive != 0 && day1 >= 4.5f && day1 <= 4.75f && day3 > 4.9f,
                $"하루 뒤 {day1:0.00}L · 사흘 뒤 {day3:0.00}L");
        }
    }
}
