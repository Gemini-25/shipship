namespace ShipEngine;

/// <summary>
/// ⑫ 자연 사건 — 고향 편지 (docs/ENGINE.md ⑫ '소식 · 만남'): 배 밖 가족 · 친구도 사회다. 하루 한 번 통신 창(저녁 6시)에 안테나(센서 배열)가 돌면
/// 사람마다 편지가 올 수 있다 — 오는지 · 무슨 소식인지는 시드에서 (사람 · 날마다) · 빛이 고향에서 오는 만큼 늦게 (⑤ <see cref="Outside.HomeLightMinutes"/>).
/// 안테나가 멈춘 날의 편지는 쌓였다가 다음 창에 한꺼번에 온다. 항해에 나선 배만 (항로를 고른 배 — 시험의 가만히 떠 있는 배는 고향과 잇지 않았다).
/// 받은 이가 어떻게 받아들일지는 그 사람의 마음 (⑧ <c>Mind.Letters</c>). 몫은 기능 확인용 첫 값 — 맨 끝에 다듬는다.
/// </summary>
public sealed partial class Society
{
    /// <summary>통신 창이 열리는 시각 (시 — 세계마다 · 시험이 바꾼다 · 찍지 않는다) · 하루에 한 사람에게 편지가 올 몫.</summary>
    public double MailHour = 18, LetterChance = 0.3;
    private long _mailDay = -1;
    public long Letters;

    private static readonly (int kind, double p, string[] what)[] Mail =
    {
        (2, 0.06, new[] { "아이가 태어났다", "동생이 결혼했다" }),
        (1, 0.30, new[] { "아픈 어머니가 나았다", "동생이 시험에 붙었다", "고향 마을에 새 학교가 섰다", "오랜 친구가 사진을 보냈다" }),
        (0, 0.45, new[] { "별일 없다는 안부", "고향 날씨 이야기", "식구들의 사진" }),
        (-1, 0.15, new[] { "아버지가 크게 아프다", "오래 키운 개가 죽었다", "집에 불이 났다 — 다친 이는 없다" }),
        (-2, 0.04, new[] { "할머니가 돌아가셨다" }),
    };

    private void MailStep(Sim sim)
    {
        var o = sim.Find<Outside>();
        if (o == null || o.Route <= 0 || sim.Tick % Time.TicksPerDay != Time.Hours(MailHour) % Time.TicksPerDay) return;
        long today = sim.Tick / Time.TicksPerDay;
        if (_mailDay < 0) _mailDay = today - 1;
        var sense = sim.Find<Sensors>();
        if (sense == null || !sense.Up) return;   // 안테나가 멈췄다 — 편지는 고향에 쌓인다
        if (o.Conjunction(sim.Tick)) return;      // ⑤ 태양 합 — 별이 신호를 삼킨다 (편지는 쌓인다)
        double delay = o.HomeLightMinutes(sim.Tick);
        for (long d = _mailDay + 1; d <= today; d++)
            for (int p = 0; p < N; p++)
            {
                if (!Person(p) || Rng.Unit(sim.Seed, Rng.Key("편지"), d, p) >= LetterChance) continue;
                double u = Rng.Unit(sim.Seed, Rng.Key("편지 소식"), d, p), acc = 0;
                foreach (var (kind, prob, what) in Mail)
                {
                    acc += prob;
                    if (u >= acc && kind != -2) continue;
                    string news = what[(int)(Rng.Unit(sim.Seed, Rng.Key("편지 글"), d, p) * what.Length) % what.Length];
                    string late = d < today ? $" · {today - d}일 늦게" : "";
                    int cause = sim.Causes.Add(CauseKind.Society, $"고향에서 {_mind!.NameOf(p)}에게 편지가 왔다 — {news} (빛으로 {delay:0}분{late})", sim.Tick, C(p).Cell, 0, 0, p);
                    sim.Facts.Emit(sim.Tick, FactKind.Letter, C(p).Cell, kind, cause, p, (int)Math.Round(delay));
                    Letters++;
                    break;
                }
            }
        _mailDay = today;
    }

    private void SaveMail(StateWriter w) { w.I64(_mailDay); w.I64(Letters); }
    private void LoadMail(StateReader r) { _mailDay = r.I64(); Letters = r.I64(); }
}
