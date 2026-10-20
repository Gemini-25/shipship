using ShipEngine;

namespace Lab;

/// <summary>
/// 꾸밈의 물건 쓰기 (함장 승인 — docs/FURNISH.md): 오락기 · VR 의자 · 음악 구석 · 망원경을 쉬는 이가 · 방화 담요 · 비상 물자함을 위기의 이가 쓴다.
/// </summary>
public static partial class Program
{
    // 쉼터: 오락기 A · VR 의자 V · 음악 구석 M · 망원경 T 만 (다른 쉼 설비 없이)
    private const string LeisureShip = @"
##############
#A..........T#
#............#
#V..........M#
##############";

    // 방 하나: 벽의 방화 담요 F · 의자 s (불씨) — 소화기는 배에 없다
    private const string BlanketShip = @"
##############
#F...........#
#............#
#.........s..#
##############";

    // 숨 쉴 공기 시험의 배 — 마스크함 대신 비상 물자함 S
    private const string CacheShip = @"
##############################
#..S.......#.......#.........#
#..........P..c.pb.P.........#
#..........#.......#.........#
##############################";

    private static void DressUseRules()
    {
        if (!Do("꾸밈 쓰기")) return;
        Console.WriteLine("\n[꾸밈의 물건 쓰기 — 쉼 · 방화 담요 · 비상 물자함]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";

        // ── 쉼: 쉼터에 그 물건 하나만 있으면 빈 시간의 사람이 그것으로 쉰다 — 오락기 · VR 의자 · 음악 구석 · 망원경 (생각 칸도 그 물건대로) ──
        {
            var got = new List<string>(); bool all = true;
            foreach (var (type, name) in new[] { ("ArcadeCabinet", "오락기를 한다"), ("VrChair", "가상 현실에 들어가 있다"), ("MusicCorner", "음악 구석에서 악기를 만진다"), ("Telescope", "망원경으로 별을 본다") })
            {
                var lay = Worlds.Ascii(LeisureShip.Replace('A', '.').Replace('V', '.').Replace('M', '.').Replace('T', 'X'), new() { ['X'] = type });
                var s = Worlds.Full(lay, 2);
                var m = s.Get<Mind>();
                int p = m.AddPerson(s, Worlds.At(lay, 4, 2));
                int off = ((int)Time.HourOfDay(s.Tick) + 24 - 10) % 24;
                bool used = false;
                for (long t = 0; t < Time.Minutes(30) && !used; t++)
                {
                    ref var c = ref m[p].C; c.ShiftHour = off; c.Sleepy = 0; c.Hunger = 0; c.Thirst = 0; c.Lonely = 0; c.Unfit = 0; c.Bored = Math.Max(c.Bored, 0.5f);
                    s.Step();
                    used = m[p].C.Goal == GoalKind.Relax && m.Thought(p) == name;
                }
                all &= used; got.Add($"{name} {(used ? "✔" : "✘ — " + m.Thought(p))}");
            }
            Check("쉼: 쉼터에 그 물건 하나만 있으면 빈 시간의 사람이 그것으로 쉰다 — 오락기 · VR 의자 · 음악 구석 · 망원경 (생각 칸도 그 물건대로)", all, string.Join(" · ", got));
        }

        // ── 방화 담요: 바닥의 작은 탈 것(고분자 조각 0.5kg)에 붙은 불 · 배에 소화기가 없다 — 벽의 방화 담요를 가져와 덮어 끈다 / 담요가 없으면 탈 것이 다 탈 때까지 ──
        {
            var lay = Worlds.Ascii(BlanketShip, new() { ['F'] = "FireBlanket", ['s'] = "Seat" });
            (bool outAt, long sec, string story, float peakKw, double leftKg) Run(bool blanket)
            {
                var s = Worlds.Full(lay, 4);
                var m = s.Get<Mind>(); var ch = s.Get<Chemistry>(); var f = s.Get<Field>();
                int p = m.AddPerson(s, Worlds.At(lay, 6, 2));
                m.SetPersonality(p, 0.5f, 0.7f, 0.5f, 0.6f, 0.3f);
                if (!blanket) for (int fu = 0; fu < s.Layout.Furniture.Count; fu++) m.Stock[fu * m.ItemCount + m.iBlanket] = 0;
                s.Run(3);
                int spot = Worlds.At(lay, 8, 2);
                double molar = f.T.Substances[f.T.Stores[f.T.sPoly].Index].MolarMass;
                f.AddStore(spot, f.T.sPoly, 0.5 / molar);
                ch.Ignite(spot, 1.5e6, s.Causes.Add(CauseKind.Setup, "바닥의 천 조각에 불씨가 튀었다", s.Tick));
                long t0 = s.Tick, outAt = -1; float peak = 0;
                for (int k = 0; k < Time.Minutes(10) && outAt < 0; k++)
                {
                    s.Step(); peak = Math.Max(peak, ch.Hrr[spot]);
                    if (k > 3 && Enumerable.Range(0, lay.N).All(c => ch.Burning[c] == 0)) outAt = s.Tick;
                    if (Debug && k % 5 == 0) Console.WriteLine($"    {(blanket ? "담요" : "없음")} {Time.Clock(s.Tick)} {m.Thought(p)} · 불 {ch.Hrr[spot]:0}kW · 들고 {m[p].C.Holding} · 목표 {m[p].C.Goal} · 막힘 {m[p].C.Snag} · 자리 {lay.X(m[p].C.Cell)},{lay.Y(m[p].C.Cell)}");
                }
                var fact = s.Facts.Since(0).LastOrDefault(x => x.Kind == FactKind.FireOut);
                return (outAt > 0, outAt > 0 ? (long)Time.ToSeconds(outAt - t0) : -1, fact.Kind == FactKind.FireOut ? s.Causes.Story(fact.Cause) : "", peak, f.Stored(f.T.sPoly, spot) * molar);
            }
            var noneT = Task.Run(() => Run(false)); var yes = Run(true); var none = noneT.Result;
            Check("방화 담요: 바닥의 작은 탈 것(0.5kg)에 붙은 불 · 소화기가 없는 배 — 벽의 방화 담요를 가져와 덮어 끈다 (원인: 담요로 덮었다 · 탈 것이 남는다) / 담요가 없으면 다 타서야 꺼진다",
                yes.outAt && yes.story.Contains("방화 담요로 불을 덮었다") && yes.leftKg > none.leftKg + 0.05,
                $"담요: 꺼짐 {yes.sec}초 · 가장 큰 불 {yes.peakKw:0}kW · 남은 탈 것 {yes.leftKg:0.00}kg · \"{yes.story}\" / 없음: 꺼짐 {(none.outAt ? $"{none.sec}초" : "아니")} · 남은 탈 것 {none.leftKg:0.00}kg");
        }

        // ── 방화 담요의 한계: 큰 불(150kW 넘게)은 덮어도 꺼지지 않는다 (세상의 법칙) ──
        {
            var lay = Worlds.Ascii(BlanketShip, new() { ['F'] = "FireBlanket", ['s'] = "Seat" });
            var s = Worlds.Full(lay, 4);
            var ch = s.Get<Chemistry>();
            s.Run(3);
            int seat = Worlds.At(lay, 10, 3);
            ch.Ignite(seat, 3e6, s.Causes.Add(CauseKind.Setup, "불씨", s.Tick));
            s.Run(2);
            ch.Hrr[seat] = 250;  // 다 자란 불
            bool big = ch.Blanket(s, seat, 0);
            ch.Hrr[seat] = 100;  // 사그라든 불
            bool small = ch.Blanket(s, seat, 0);
            s.Run(30);
            Check("방화 담요의 한계: 열 방출 250kW의 불은 덮어도 그대로 · 100kW의 불은 꺼지고 덮인 동안 다시 붙지 않는다",
                !big && small && ch.Burning[seat] == 0, $"큰 불 덮음 {big} · 작은 불 덮음 {small} · 1분 뒤 타는가 {ch.Burning[seat] != 0} · 덮인 틱 {ch.Smother[seat]}");
        }

        // ── 비상 물자함: 배 전체가 45kPa (좋은 공기가 어디에도 없다) — 마스크함 대신 비상 물자함의 산소 마스크를 쓰고 산다 / 물자함이 비었으면 쓰러진다 ──
        {
            var lay = Worlds.Ascii(CacheShip, new() { ['S'] = "SupplyCache", ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer" });
            (bool masked, bool up, string why) Run(bool stocked)
            {
                var s = Worlds.Full(lay, 3);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var f = s.Get<Field>(); var net = s.Get<Network>();
                net.AirReserve = 0;
                int p = m.AddPerson(s, Worlds.At(lay, 6, 2));
                m.SetPersonality(p, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f);
                if (!stocked) for (int fu = 0; fu < s.Layout.Furniture.Count; fu++) m.Stock[fu * m.ItemCount + m.iMask] = 0;
                s.Run(2);
                var cells = Enumerable.Range(0, s.Layout.N).Where(c => s.Layout.IsSpace(c)).ToList();
                double k1 = Math.Pow(0.45, 1.0 / 50);
                for (int t = 0; t < 50; t++) { foreach (int c in cells) for (int g = 0; g < f.G; g++) f.RemoveGas(c, g, f.N[c * f.G + g] * (1 - k1)); s.Step(); }
                bool masked = false;
                for (int k = 0; k < Time.Minutes(30); k++) { s.Step(); masked |= life[m[p].C.Body].Mask != 0; }
                ref var b = ref life[m[p].C.Body];
                return (masked, b.Conscious != 0 && b.Alive != 0, $"포화도 {b.SpO2:0.00} · 마스크 {b.Mask}");
            }
            var noneT = Task.Run(() => Run(false)); var yes = Run(true); var none = noneT.Result;
            Check("비상 물자함: 배 전체가 45kPa — 비상 물자함의 산소 마스크를 쓰고 산다 / 물자함이 비었으면 30분 안에 쓰러진다",
                yes.masked && yes.up && !none.up, $"물자함 있음: 씀 {yes.masked} · {yes.why} / 빔: {none.why} · 깨어 있음 {none.up}");
        }
    }
}
