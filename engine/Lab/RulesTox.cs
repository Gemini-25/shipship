using ShipEngine;

namespace Lab;

/// <summary>② 사고 화학 (docs/ENGINE.md ② '바퀴 3 설계 — 사고 화학'): 재질마다 다른 독가스 · 몸 (ISO 13571 · Purser).</summary>
public static partial class Program
{
    private static void ToxRules()
    {
        if (!Do("사고 화학")) return;
        Console.WriteLine("\n[② 사고 화학 — 독가스]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";

        // ── (가) 달궈진 피복: 불꽃 없이도 염소가 빠져 염화수소가 방에 찬다 (탈염화수소 250℃~ — 타기 전에 매캐하다) ──
        {
            var lay = Worlds.Ascii(Worlds.Lounge, Worlds.Seats);
            int hot = Worlds.At(lay, 8, 2), far = Worlds.At(lay, 2, 4);
            var s = Worlds.WithChem(lay, 1, (f, c) => f.AddStore(hot, f.T.sPvc, 1.0));   // 전선 열네 가닥 몫
            var f = s.Get<Field>(); var ch = s.Get<Chemistry>();
            for (int i = 0; i < Time.Minutes(10); i++) { f.Tf[hot] = 600; s.Step(); }   // 과부하로 달아오른 전선 (327℃)
            Check("달궈진 피복: 327℃에서 10분이면 불꽃 없이도 피복의 염소 대부분이 염화수소로 빠져 방 반대편까지 매캐하다 (냄새 문턱 1ppm · 해로운 5ppm을 훨씬 넘게)",
                ch.BurningCount == 0 && f.Stored(f.T.sPvc, hot) < 0.4 && f.Ppm(f.T.gHCl, far) > 20,
                $"불 {ch.BurningCount} · 남은 피복 {f.Stored(f.T.sPvc, hot):0.00}몰 · 먼 구석 염화수소 {f.Ppm(f.T.gHCl, far):0}ppm");
        }

        // ── (가) 폼이 산소 모자라게 타면 시안화수소 ──
        {
            var lay = Worlds.Ascii(Worlds.Lounge, Worlds.Seats);
            int bed = Worlds.At(lay, 8, 3), far = Worlds.At(lay, 2, 4);
            var s = Worlds.WithChem(lay, 1, (f, c) => { f.AddStore(bed, f.T.sPu, 45); c.Ignite(bed, 3e6, 0); });   // 침대 매트리스 폼 10kg
            var f = s.Get<Field>();
            double hcn = 0, o2min = 100;
            for (int i = 0; i < Time.Minutes(40); i++) { s.Step(); hcn = Math.Max(hcn, f.Ppm(f.T.gHCN, far)); o2min = Math.Min(o2min, f.Partial(f.T.gO2, far)); }
            Check("폼(폴리우레탄)이 닫힌 방에서 산소가 모자라게 타면 시안화수소가 방에 찬다 (먼 구석 100ppm 넘게 — 20분이면 쓰러지는 짙기)",
                hcn > 100, $"먼 구석 시안화수소 최고 {hcn:0}ppm · 산소 최저 {o2min:0.0}kPa");
        }

        // ── (가) 시안화수소 속 몸 (Purser): 200ppm이면 쉬는 사람도 몇 분 만에 쓰러지고 · 400ppm이면 숨진다 · 20ppm은 한 시간 버틴다 ──
        {
            var lay = Worlds.Ascii(Worlds.Sealed);
            int a = Worlds.At(lay, 4, 3);
            Sim Hcn(double ppm) => Worlds.Full(lay, 1, w => { var f = w.Get<Field>(); foreach (int c in lay.Rooms.First(r => r.Cells.Contains(a)).Cells) f.AddGas(c, f.T.gHCN, f.Moles(c) * ppm * 1e-6, f.Temp(c)); });
            long Down(Sim w, Life life, int b, double hours) { for (int i = 0; i < Time.Hours(hours); i++) { w.Step(); if (life[b].Conscious == 0) return w.Tick; } return -1; }
            var s1 = Hcn(200); var l1 = s1.Get<Life>(); int b1 = l1.AddBody(a); long t0 = s1.Tick; long d1 = Down(s1, l1, b1, 0.5);
            var s2 = Hcn(20); var l2 = s2.Get<Life>(); int b2 = l2.AddBody(a); long d2 = Down(s2, l2, b2, 1);
            var s3 = Hcn(400); var l3 = s3.Get<Life>(); int b3 = l3.AddBody(a);
            for (int i = 0; i < Time.Minutes(15) && l3[b3].Alive != 0; i++) s3.Step();
            string why = l3[b3].Alive == 0 ? s3.Causes.Text(l3[b3].DeathCause) : "";
            double min1 = d1 > 0 ? Time.ToSeconds(d1 - t0) / 60 : -1;
            Check("시안화수소 속 몸 (Purser): 200ppm이면 쉬는 사람도 3~10분에 쓰러지고 · 400ppm이면 숨지며 (원인 '시안화수소') · 20ppm은 한 시간 버틴다",
                min1 > 3 && min1 < 10 && d2 < 0 && l3[b3].Alive == 0 && why.Contains("시안화수소"),
                $"200ppm 쓰러짐 {min1:0.0}분 · 20ppm {(d2 < 0 ? "버팀" : "쓰러짐")} (FED {l2[b2].CN:0.00}) · 400ppm \"{why}\"");
        }

        // ── (가) 염화수소: 짙으면 눈 · 목 · 폐를 찌른다 (폐 손상이 쌓인다) ──
        {
            var lay = Worlds.Ascii(Worlds.Sealed);
            int a = Worlds.At(lay, 4, 3);
            var s = Worlds.Full(lay, 1, w => { var f = w.Get<Field>(); foreach (int c in lay.Rooms.First(r => r.Cells.Contains(a)).Cells) f.AddGas(c, f.T.gHCl, f.Moles(c) * 1500e-6, f.Temp(c)); });
            var life = s.Get<Life>(); int b = life.AddBody(a);
            s.Run(Time.Minutes(30));
            Check("염화수소 1500ppm 속 30분: 폐가 상하고 아프다 (암모니아의 두 배쯤 독하다)", life[b].LungDamage > 0.2 && life[b].Pain > 0.3,
                $"폐 손상 {life[b].LungDamage:0.00} · 아픔 {life[b].Pain:0.00}");
        }
    }
}
