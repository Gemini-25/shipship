using ShipEngine;

namespace Lab;

/// <summary>⑤ 통신 지연 규칙 시험 — 원격 진료(빛의 왕복) · 태양 합 (docs/ENGINE.md ⑤ 통신 지연).</summary>
public static partial class Program
{
    private static void CommsRules()
    {
        if (!Do("통신 지연")) return;
        Console.WriteLine("\n[⑤ 통신 지연]");
        var lay = Worlds.Ascii(HelmShip, HelmLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);

        // 항해 중인 배: 1 → 1.52AU 전이 궤도 위 (dist AU · 떠난 지 days일)
        (Sim s, Mind m, ShipMind sm, Outside o, Life life) Voyage(double dist, double days, ulong seed, bool arrived = false)
        {
            var s = Worlds.Full(lay, seed);
            var m = s.Get<Mind>(); var o = s.Get<Outside>(); var life = s.Get<Life>();
            s.Run(5);
            // 닿은 배도 전이 궤도를 지나 닿게 한다 (⑤ 별계 지도 — 닿은 자리에서 다시 돈다: 첫 틱에 닿는다)
            o.Route = 4; o.VoyageFrom = 1; o.VoyageTo = 1.52; o.Distance = dist; o.VoyageTicks = (long)(Outside.VoyageDays(1, 1.52) * Time.TicksPerDay); o.VoyageStart = s.Tick - (long)(days * Time.TicksPerDay);
            return (s, m, s.Get<ShipMind>(), o, life);
        }
        // 가슴이 아픈 사람 (상한 심장) — 배에서는 체한 것으로 보았다 (닮은 병)
        int Patient(Sim s, Mind m, Life life)
        {
            int p = m.AddPerson(s, At(14, 3));
            ref var b = ref life.Bodies.ToArray()[0];
            var body = life[m[p].C.Body]; body.Heart = 0.4f; body.HeartPeak = 0.4f; life[m[p].C.Body] = body;
            ref var c = ref m[p].C;
            c.DxAil = Ail.Indigestion; c.DxAt = s.Tick; c.DxBy = p; c.DxSym = Life.Symptoms(life[c.Body]); c.DxRoom = lay.Room[At(14, 3)];
            c.DxCause = s.Causes.Add(CauseKind.Person, "가슴이 쓰리다 — 체한 것으로 보았다", s.Tick, c.Cell);
            return p;
        }

        // ── 원격 진료: 무거운 증상의 진료 기록을 고향 관제 의사에게 보내고 · 답은 빛의 왕복 + 읽는 20분 뒤 · 다르면 진료 기록을 고친다 ──
        {
            var (s, m, sm, o, life) = Voyage(1.30, 120, 31);
            life.HomeDxChance = 1;
            int p = Patient(s, m, life);
            double oneWay = o.HomeLightMinutes(s.Tick);
            long sent = -1, answered = -1;
            for (int k = 0; k < Time.Hours(3) && answered < 0; k++)
            {
                s.Step();
                if (sent < 0 && sm.TeleSent > 0) sent = s.Tick;
                if (answered < 0 && sm.TeleAnswered > 0) answered = s.Tick;
            }
            double rt = (answered - sent) * 60.0 / Time.TicksPerHour;
            string said = sm.Log.Select(l => l.text).LastOrDefault(t => t.Contains("고향 관제 의사")) ?? "";
            Check("원격 진료: 무거운 증상의 진료 기록을 고향 관제 의사에게 보내고 · 답은 빛의 왕복(편도 × 2) + 읽는 20분 뒤 · 다르면 진료 기록을 고친다 (체한 것 → 상한 심장)",
                sent > 0 && answered > 0 && Math.Abs(rt - (2 * oneWay + ShipMind.HomeReadMinutes)) < 2.5 && m[p].C.DxAil == Ail.Heart && m[p].C.DxBy == -1 && said.Contains("고쳤다"),
                $"편도 {oneWay:0.0}분 · 보낸 뒤 답까지 {rt:0.0}분 · 기록 {Life.AilName(m[p].C.DxAil)} · \"{said}\"");
        }

        // ── 태양 합: 고향에서 본 배가 별 곁 2° 안이면 보내지도 받지도 못한다 (방송) · 편지도 쌓인다 — 합이 아니면 보낸다 ──
        {
            (bool conj, long sentTele, string said, long letters) Run(double dist, double days, bool arrived)
            {
                var (s, m, sm, o, life) = Voyage(dist, days, 33, arrived);
                s.Get<Society>().LetterChance = 1;
                Patient(s, m, life);
                s.Run(Time.Hours(1));
                return (o.Conjunction(s.Tick), sm.TeleSent, sm.Log.Select(l => l.text).FirstOrDefault(t => t.StartsWith("태양 합")) ?? "", s.Get<Society>().Letters);
            }
            // 붉은 사막에 닿아 머문 배가 별 건너편에 드는 날: 닿을 때 반 바퀴(π) 건너 · 그 뒤 고향이 앞서 돌아 둘 사이가 다시 π가 되는 때 (실제 화성 합처럼 닿은 지 일곱 달 남짓)
            double n1 = 2 * Math.PI / Outside.PeriodDays(1), n2 = 2 * Math.PI / Outside.PeriodDays(1.52), T = Outside.VoyageDays(1, 1.52);
            double conjDay = (n2 * T - 2 * Math.PI) / (n2 - n1);
            var behind = Run(1.52, conjDay, true);
            var open = Run(1.30, 120, false);
            Check("태양 합: 고향에서 본 배가 별 곁 2° 안이면 통신이 끊긴다 — 진료 기록을 보내지 못하고 방송 '태양 합' · 합이 아니면 보낸다",
                behind.conj && behind.sentTele == 0 && behind.said.Contains("통신이 끊긴다") && !open.conj && open.sentTele > 0,
                $"떠난 지 {conjDay:0}일 (닿은 지 {conjDay - T:0}일) 별 건너편: 합 {behind.conj} · 보냄 {behind.sentTele} · \"{behind.said}\" / 120일째 1.3AU: 합 {open.conj} · 보냄 {open.sentTele}");
        }
    }
}
