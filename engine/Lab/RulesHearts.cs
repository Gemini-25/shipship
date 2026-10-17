using ShipEngine;

namespace Lab;

/// <summary>⑨ 사회 (바퀴 2) — 끌림 → 고백 → 짝 · 헤어짐: 정해 둔 이야기 틀 없이 관계 값에서. docs/DEPTH.md</summary>
public static partial class Program
{
    private static void HeartRules()
    {
        if (!Do("짝")) return;
        Console.WriteLine("\n[⑨ 끌림 · 짝]");
        var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";

        // 둘이 오른쪽 방에서 함께 쉰다 (배고픔 · 졸림 없이) — 정은 이미 있고 끌림은 고백 문턱 바로 아래
        (int couples, int rejections, int partner, float grew, string fact, string line, float after) Run(float back, ulong seed)
        {
            var s = Worlds.Full(lay, seed);
            var m = s.Get<Mind>(); var so = s.Get<Society>();
            int a = m.AddPerson(s, At(13, 2)), b = m.AddPerson(s, At(15, 3));
            s.Run(1);
            int N = so.N;
            so.Bond[a * N + b] = so.Bond[b * N + a] = 0.55f;
            so.Known[a * N + b] = so.Known[b * N + a] = 0.8f;
            so.Attract[a * N + b] = 0.585f; so.Attract[b * N + a] = back;
            float start = so.AttractOf(a, b), grew = 0;
            for (int k = 0; k < Time.Hours(4) && so.Couples + so.Rejections == 0; k++)
            {
                foreach (int p in new[] { a, b }) { m[p].C.Hunger = 0; m[p].C.Thirst = 0; m[p].C.Sleepy = 0; }
                s.Step();
                grew = Math.Max(grew, so.AttractOf(a, b) - start);
            }
            var heart = so.Sayings.FirstOrDefault(x => x.Topic == Topic.Heart);
            var f = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Relationship && x.Size == 1);
            if (Debug) Console.WriteLine($"    맞음 {so.Spark(s, a, b):0.00} · 끌림 {so.AttractOf(a, b):0.000} / {so.AttractOf(b, a):0.000} · 대화 {so.Talks}");
            return ((int)so.Couples, (int)so.Rejections, so.PartnerOf(a), grew, f.Kind == FactKind.Relationship ? s.Causes.Text(f.Cause) : "",
                heart.Line != 0 ? s.Causes.Text(heart.Line) : "", so.AttractOf(a, b));
        }
        // 맞음이 큰 둘 (처음 둘의 번호 0 · 1 — 시드마다 다르다)
        ulong good = 1;
        while (Rng.Unit(good, Rng.Key("끌림"), 0, 1) < 0.85) good++;
        var yes = Run(0.55f, good); var no = Run(0.1f, good);
        Check("끌림 → 짝: 함께 쉬는 동안 끌림이 자라 고백 문턱을 넘으면 마음을 고백한다 — 상대도 끌리면 짝이 된다 · 아니면 거절당하고 끌림이 반으로 준다",
            yes.grew > 0.01f && yes.couples == 1 && yes.partner >= 0 && yes.fact.Contains("짝이 되었다") && yes.line.Length > 2 && !yes.line.Contains('{')
            && no.couples == 0 && no.rejections == 1 && no.partner < 0 && no.after < 0.35f,
            $"받아 줌: 끌림 +{yes.grew:0.000} · 짝 {yes.couples} \"{yes.fact}\" \"{yes.line}\" / 거절: 짝 {no.couples} · 거절 {no.rejections} · 끌림 {no.after:0.00}");

        // ── 짝이 크게 다퉈 원한이 쌓이면 헤어진다 — 둘 다 슬프다 ──
        {
            var s = Worlds.Full(lay, 4);
            var m = s.Get<Mind>(); var so = s.Get<Society>();
            int a = m.AddPerson(s, At(13, 2)), b = m.AddPerson(s, At(15, 3));
            s.Run(1);
            int N = so.N;
            so.Partner[a] = b; so.Partner[b] = a;
            so.Bond[a * N + b] = so.Bond[b * N + a] = 0.6f;
            so.Grudge[a * N + b] = 0.5f;                       // 그날 일을 두고 크게 다퉜다
            s.Run(Time.Minutes(2));
            var f = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Relationship && x.Size == -1);
            Check("헤어짐: 짝 사이에 원한이 쌓이면 (0.45 넘게) 헤어진다 — 원인 '쌓인 원망' · 둘 다 슬프다",
                so.PartnerOf(a) < 0 && so.PartnerOf(b) < 0 && so.Breakups == 1 && f.Kind == FactKind.Relationship && s.Causes.Text(f.Cause).Contains("원망") && m[a].C.Sad > 0.3f && m[b].C.Sad > 0.3f,
                $"짝 {so.PartnerOf(a)} · 헤어짐 {so.Breakups} · \"{(f.Kind == FactKind.Relationship ? s.Causes.Text(f.Cause) : "")}\" · 슬픔 {m[a].C.Sad:0.00} / {m[b].C.Sad:0.00}");
        }
    }
}
