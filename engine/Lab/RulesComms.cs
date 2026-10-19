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
            o.Route = 4; o.VoyageFrom = 1; o.VoyageTo = 1.52; o.Distance = dist; o.VoyageTicks = arrived ? 0 : Time.Days(259); o.VoyageStart = s.Tick - (long)(days * Time.TicksPerDay);
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
            var behind = Run(1.52, 365.25, true);   // 붉은 사막에 닿아 머문 지 — 떠난 지 한 해: 고향이 한 바퀴 돌아 배는 별 건너편
            var open = Run(1.30, 120, false);
            Check("태양 합: 고향에서 본 배가 별 곁 2° 안이면 통신이 끊긴다 — 진료 기록을 보내지 못하고 방송 '태양 합' · 합이 아니면 보낸다",
                behind.conj && behind.sentTele == 0 && behind.said.Contains("통신이 끊긴다") && !open.conj && open.sentTele > 0,
                $"한 해째 별 건너편: 합 {behind.conj} · 보냄 {behind.sentTele} · \"{behind.said}\" / 120일째 1.3AU: 합 {open.conj} · 보냄 {open.sentTele}");
        }
    }
}
