using ShipEngine;

namespace Lab;

/// <summary>인공 중력 · 무중력 (바퀴 3) — 발생기 · 몸마다 바닥 중력. docs/ENGINE.md '인공 중력 · 무중력'</summary>
public static partial class Program
{
    private static void GravityRules()
    {
        if (!Do("중력")) return;
        Console.WriteLine("\n[인공 중력 · 무중력]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var legend = Worlds.MindLegend; legend['G'] = "GravityGenerator";
        var lay = Worlds.Ascii(Worlds.MindShip.Replace("#.........#....a....a.r.#", "#....G....#....a....a.r.#"), legend);
        int At(int x, int y) => Worlds.At(lay, x, y);

        // ── (가) 발생기: 켜진 배는 1g · 끄면 10초 남짓에 무중력 (사실 · 원인 '꺼졌다') · 다시 켜면 30초에 1g / 발생기가 없는 시험 배는 바탕 1g ──
        {
            var s = Worlds.Full(lay, 1); var f = s.Get<Field>(); var net = s.Get<Network>();
            int gen = net.Devices.First(d => d.Type == "GravityGenerator").Id;
            s.Run(Time.Minutes(1));
            float start = f.Grav[At(15, 2)];
            long f0 = s.Facts.Total;
            net.SetOn(gen, false);
            s.Run(5);   // 12초
            float off = f.Grav[At(15, 2)], offFar = f.Grav[At(3, 8)];
            var lost = s.Facts.Since(f0).FirstOrDefault(x => x.Kind == FactKind.Gravity && x.Size == 0);
            net.SetOn(gen, true);
            s.Run(6);   // 14초 — 아직 반쯤
            float half = f.Grav[At(15, 2)];
            s.Run(8);   // 33초
            float on = f.Grav[At(15, 2)];
            var back = s.Facts.Since(f0).FirstOrDefault(x => x.Kind == FactKind.Gravity && x.Size == 1);
            var plain = Worlds.Full(Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend), 1); plain.Run(Time.Minutes(1));
            float bare = plain.Get<Field>().Grav.Min();
            string why = lost.Kind == FactKind.Gravity ? s.Causes.Text(lost.Cause) : "";
            Check("인공 중력 발생기: 켜진 배는 1g · 끄면 12초에 무중력 (배 어디나 · 사실 '중력이 사라진다' — 꺼졌다) · 다시 켜면 반쯤에서 30초 남짓에 1g (사실 '돌아온다') / 발생기가 없는 시험 배는 바탕 1g",
                start == 1 && off == 0 && offFar == 0 && why.Contains("꺼졌다") && half > 0.3f && half < 0.7f && on == 1 && back.Kind == FactKind.Gravity && bare == 1,
                $"처음 {start:0.00}g · 끄고 12초 {off:0.00} (먼 칸 {offFar:0.00}) · \"{why}\" · 켜고 14초 {half:0.00} · 33초 {on:0.00} · 돌아옴 {back.Kind == FactKind.Gravity} / 발생기 없는 배 {bare:0.00}g");
        }

        // ── 전기가 끊기면 · 망가지면 무중력 — 원인 사슬이 그 까닭에 닿는다 ──
        {
            (float g, string why) Run(bool breakIt)
            {
                var s = Worlds.Full(lay, 2); var f = s.Get<Field>(); var net = s.Get<Network>();
                var gen = net.Devices.First(d => d.Type == "GravityGenerator");
                s.Run(Time.Minutes(1));
                long f0 = s.Facts.Total;
                if (breakIt) net.Age(gen.Id, Part.Mechanical, 1.2, s.Causes.Add(CauseKind.Story, "발생기 회전자 축받이가 닳았다", s.Tick));
                else net.Breaker[gen.Feeder] = 0;
                s.Run(Time.Minutes(1));
                var lost = s.Facts.Since(f0).FirstOrDefault(x => x.Kind == FactKind.Gravity && x.Size == 0);
                return (f.Grav[At(15, 2)], lost.Kind == FactKind.Gravity ? s.Causes.Story(lost.Cause) : "");
            }
            var cut = Run(false); var broke = Run(true);
            Check("전기가 끊기면 (차단기) 무중력 · '전기가 끊겼다' / 발생기가 망가지면 무중력 · 원인 사슬이 그 고장에 닿는다",
                cut.g == 0 && cut.why.Contains("전기가 끊겼다") && broke.g == 0 && broke.why.Contains("망가졌다") && broke.why.Contains("→"),
                $"차단기: {cut.g:0.00}g · \"{cut.why}\" / 고장: {broke.g:0.00}g · \"{broke.why}\"");
        }

        // ── 배가 갈라지면: 발생기가 있는 쪽은 1g · 떨어져 나간 조각은 무중력 (사실 · '발생기가 없는 조각') ──
        {
            var dl = Worlds.Ascii(Dumbbell.Replace("#.......#####.......#\n#....e..#", "#..G....#####.......#\n#....e..#"), legend);
            var s = Worlds.Full(dl, 3); var f = s.Get<Field>(); var st = s.Get<Structure>();
            s.Run(Time.Minutes(1));
            long f0 = s.Facts.Total;
            int rock = s.Causes.Add(CauseKind.Story, "커다란 운석이 통로를 꿰뚫었다", s.Tick);
            for (int y = 2; y <= 4; y++) st.Destroy(s, Worlds.At(dl, 10, y), rock);
            s.Run(Time.Minutes(1));
            float left = f.Grav[Worlds.At(dl, 3, 2)], right = f.Grav[Worlds.At(dl, 16, 2)];
            var lost = s.Facts.Since(f0).FirstOrDefault(x => x.Kind == FactKind.Gravity && x.Size == 0);
            string why = lost.Kind == FactKind.Gravity ? s.Causes.Story(lost.Cause) : "";
            if (Debug) Console.WriteLine($"    몸 {st.Bodies.Count} · 왼쪽 몸 {st.BodyOf(Worlds.At(dl, 3, 2))} · 오른쪽 몸 {st.BodyOf(Worlds.At(dl, 16, 2))}");
            Check("배가 갈라지면: 발생기가 있는 쪽은 1g · 떨어져 나간 조각은 무중력 (사실 '중력이 사라진다' — 발생기가 없는 조각)",
                st.Bodies.Count >= 2 && left == 1 && right == 0 && why.Contains("조각에는") && why.Contains("운석"),
                $"몸 {st.Bodies.Count} · 발생기 쪽 {left:0.00}g · 조각 {right:0.00}g · \"{why}\"");
        }
    }
}
