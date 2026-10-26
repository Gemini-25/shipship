using ShipEngine;

namespace Lab;

/// <summary>깊이 고침 — 사고의 물리 (docs/DEPTH_FIX.md DF8 · DF9 · 근거 docs/ENGINE_DEPTH_REVIEW.md §9 · §4-14).</summary>
public static partial class Program
{
    private static void Depth2Rules()
    {
        // ── DF8 사고의 물리 1: 파편이 선 · 관을 끊고 · 두 겹 벽도 샌다 · 맞은 몸은 그 자리에서 · 원뿔 ──
        if (Do("사고의 물리"))
        {
            Console.WriteLine("\n[사고의 물리 1 — 파편 길의 선 · 두 겹 벽 · 맞은 몸 · 원뿔]");
            const double Rock60 = 0.5 * 0.06 * 7000 * 7000;   // 60g · 7km/s

            // 파편 길의 전선: 60g이 외판을 뚫고 전선이 깔린 칸을 지나면 그 칸의 전선이 끊긴다 (곁 외판 구멍 340cm² — 예전 문턱 500cm²보다 작다)
            {
                var legend = new Dictionary<char, string>
                {
                    ['R'] = "ReactorCore", ['u'] = "CoolantPump", ['v'] = "CoolantPump", ['b'] = "Battery", ['p'] = "PowerPanel", ['c'] = "MainComputer", ['r'] = "RobotDock",
                    ['O'] = "OxygenGenerator", ['W'] = "WaterRecycler", ['g'] = "GrowBed", ['k'] = "CoffeeMachine",
                };
                var lay = Worlds.Ascii(LineShip, legend);
                var s = Worlds.Full(lay, 3);
                s.Run(5);
                var net = s.Get<Network>();
                byte pw = Network.Bit(LineKind.Power);
                // 외판 바로 아래가 아닌 (둘째 줄) 전선 칸 — 그 위 첫 줄은 공기 칸
                int line = -1, hull = -1;
                for (int x = 1; x < 32 && line < 0; x++)
                {
                    int c2 = Worlds.At(lay, x, 2), c1 = Worlds.At(lay, x, 1);
                    if ((net.LineAt[c2] & pw) != 0 && (net.LineAt[c1] & pw) == 0 && lay.IsSpace(c1) && lay.IsSpace(c2)) { line = c2; hull = Worlds.At(lay, x, 0); }
                }
                int rock = s.Causes.Add(CauseKind.Impact, "60g 운석이 외판에 부딪혔다", s.Tick, hull);
                s.Get<Structure>().Impact(s, hull, Rock60, 0, 1, rock);
                s.Run(2);
                var cut = s.Facts.Since(0).Where(f => f.Kind == FactKind.CableCut && f.Cell == line).ToList();
                string why = cut.Count > 0 ? s.Causes.Story(cut[0].Cause) : "";
                Check("파편 길의 전선: 60g · 7km/s가 외판을 뚫고 전선 칸 (외판 아래 둘째 줄)을 지나면 그 칸의 전선이 끊긴다 — 원인 '파편에 전선이 끊겼다' → 그 운석",
                    line >= 0 && net.LineCutAt(line, LineKind.Power) && why.Contains("파편에 전선이 끊겼다") && why.Contains("60g 운석"),
                    $"전선 칸 {(line >= 0 ? $"({lay.X(line)},{lay.Y(line)})" : "없음")} · 끊김 {(line >= 0 && net.LineCutAt(line, LineKind.Power))} · 외판 구멍 {s.Get<Field>().Hole[hull] * 1e4:0}cm² · '{why}'");
            }

            // 두 겹 벽: 외판 뒤에 칸막이가 붙은 자리를 60g이 뚫으면 두 구멍이 한 줄기 틈 — 3분 안에 방 기압이 떨어진다 (예전: 두 벽을 뚫어도 101kPa)
            {
                var lay = Worlds.Ascii(@"
###########
#####.....#
#.........#
#.........#
#.........#
#.........#
#.........#
#.........#
#.........#
#.........#
###########");
                var s = Worlds.Full(lay, 1);
                var f = s.Get<Field>();
                s.Run(3);
                int hull = Worlds.At(lay, 2, 0), inner = Worlds.At(lay, 2, 1), room = Worlds.At(lay, 6, 2);
                double p0 = f.Pressure(room);
                s.Get<Structure>().Impact(s, hull, Rock60, 0, 1, s.Causes.Add(CauseKind.Impact, "60g 운석이 외판에 부딪혔다", s.Tick, hull));
                s.Run(Time.Minutes(3));
                Check("두 겹 벽: 외판 (2,0) 뒤에 칸막이 (2,1)가 붙은 자리를 60g · 7km/s가 뚫으면 두 구멍이 한 줄기 틈 — 3분 안에 방 기압이 떨어진다 (101kPa 그대로가 아니다)",
                    f.Hole[hull] > 0 && f.Hole[inner] > 0 && f.Pressure(room) < p0 * 0.8,
                    $"구멍 외판 {f.Hole[hull] * 1e4:0}cm² · 칸막이 {f.Hole[inner] * 1e4:0}cm² · 방 {p0:0.0} → {f.Pressure(room):0.0}kPa · 틈 {string.Join(",", f.Links.Select(k => $"{k.Kind}({lay.X(k.Via)},{lay.Y(k.Via)}){k.Area*1e4:0}"))} · 구멍 {string.Join(",", Enumerable.Range(0, lay.N).Where(c => f.Hole[c] > 0).Select(c => $"({lay.X(c)},{lay.Y(c)}){lay.Kind[c]}{f.Hole[c]*1e4:0}"))}");
            }

            // 맞은 몸은 그 자리에서: 같은 파편 · 같은 자리 · 다음 틱에 걸어 나간다 — 생명 박자의 위상 다섯 가지 모두 같은 타박 (예전: 바로 다음 틱이 생명 틱일 때만 맞았다)
            {
                var lay = Worlds.Ascii(Worlds.Sealed);
                var blunt = new List<float>();
                for (int k = 0; k < 5; k++)
                {
                    var s = Worlds.Full(lay, 1);
                    var life = s.Get<Life>();
                    int at = Worlds.At(lay, 1, 3), away = Worlds.At(lay, 5, 1), hull = Worlds.At(lay, 0, 3);
                    int b = life.AddBody(at);
                    s.Run(2);
                    while (s.Tick % 5 != k) s.Step();
                    float b0 = life[b].Blunt;
                    s.Get<Structure>().Impact(s, hull, 0.5 * 0.005 * 3000 * 3000, 1, 0, s.Causes.Add(CauseKind.Impact, "5g 운석이 외판에 부딪혔다", s.Tick, hull));
                    s.Step();
                    life.Move(b, away);
                    s.Run(10);
                    blunt.Add(life[b].Blunt - b0);
                }
                Check("맞은 몸은 그 자리에서: 5g · 3km/s 파편이 지나는 칸에 있던 이가 다음 틱에 걸어 나가도 — 생명 박자의 위상 다섯 가지 모두 같은 타박 (0보다 크다)",
                    blunt.Min() > 0.05f && blunt.Max() - blunt.Min() < 0.01f,
                    $"위상 0~4 타박 {string.Join(" · ", blunt.Select(x => x.ToString("0.000")))}");
            }

            // 원뿔: 외판을 뚫은 파편 구름이 반각 30°쯤 퍼진다 — 넷째 칸 줄에서 가운데 줄 옆 두 칸까지 맞는다 (예전: 한 칸 폭 줄)
            {
                var lay = Worlds.Ascii(@"
#################
#...............#
#...............#
#...............#
#...............#
#...............#
#...............#
#...............#
#################");
                var s = Worlds.Full(lay, 1);
                s.Run(2);
                var st = s.Get<Structure>();
                int hull = Worlds.At(lay, 8, 0);
                st.Impact(s, hull, Rock60, 0, 1, 0);
                var hit = Enumerable.Range(0, lay.N).Where(c => st.ImpactJ[c] > 0).ToList();
                int row4 = hit.Count(c => lay.Y(c) == lay.Y(hull) + 4);
                int width = hit.Count == 0 ? 0 : hit.Max(c => Math.Abs(lay.X(c) - lay.X(hull)));
                Check("원뿔: 60g · 7km/s가 외판을 뚫은 뒤 파편 구름이 퍼진다 — 넷째 칸 줄에서 셋 넘는 칸이 맞고 가운데 줄에서 두 칸 넘게 벗어난 칸까지",
                    row4 >= 3 && width >= 2, $"맞은 칸 {hit.Count} · 넷째 줄 {row4}칸 · 가장 먼 옆 {width}칸");
            }
        }

        // ── DF9 사고의 물리 2: 폭발은 충격량으로 판을 찢는다 ──
        if (Do("사고의 물리 2"))
        {
            Console.WriteLine("\n[사고의 물리 2 — 폭발의 충격량 · 판]");
            // 방 가운데 바닥에서 터진 폭발: 0.5MJ (TNT 120g — 함장의 손 '작게')는 첨두압이 바닥 정격을 넘어도 양압이 0.1ms 남짓이라 판을 못 뚫는다 · 8MJ (TNT 1.9kg)는 뚫는다 (예전: 0.5MJ도 바닥 2m²)
            (int holes, double kpa) Deck(double j)
            {
                var lay = Worlds.Ascii(@"
###########
#.........#
#.........#
#.........#
#.........#
#.........#
###########");
                var s = Worlds.Full(lay, 1);
                s.Run(2);
                var st = s.Get<Structure>(); var f = s.Get<Field>();
                int mid = Worlds.At(lay, 5, 3);
                st.Blast(s, mid, j, s.Causes.Add(CauseKind.Setup, "폭발", s.Tick, mid));
                return (Enumerable.Range(0, lay.N).Count(c => lay.Kind[c] == CellKind.Floor && f.Hole[c] > 0), st.BlastPeakKPa[mid]);
            }
            // 끊긴 관의 흐름: 관 단면 × √(2ΔP/ρ) — 펌프가 도는 물관이 끊기면 분에 수십 L (예전 0.5L) · 스미는 틈 (0.05)의 열 배 넘게
            {
                var legend = new Dictionary<char, string>
                {
                    ['R'] = "ReactorCore", ['u'] = "CoolantPump", ['v'] = "CoolantPump", ['b'] = "Battery", ['p'] = "PowerPanel", ['c'] = "MainComputer", ['r'] = "RobotDock",
                    ['O'] = "OxygenGenerator", ['W'] = "WaterRecycler", ['g'] = "GrowBed", ['k'] = "CoffeeMachine",
                };
                var lay = Worlds.Ascii(LineShip, legend);
                (double cutLmin, double seepLmin) Flow()
                {
                    double Run(float leak)
                    {
                        var s = Worlds.Full(lay, 3);
                        s.Run(5);
                        var net = s.Get<Network>();
                        byte wt = Network.Bit(LineKind.Water);
                        int cell = Enumerable.Range(0, lay.N).First(c => (net.LineAt[c] & wt) != 0 && lay.IsSpace(c));
                        double t0 = net.WaterTank;
                        if (leak >= 1) net.CutLines(s, cell, wt, s.Causes.Add(CauseKind.Setup, "물관을 끊었다", s.Tick, cell));
                        else net.LeakPipe(s, cell, LineKind.Water, leak, s.Causes.Add(CauseKind.Setup, "물관 이음매가 스민다", s.Tick, cell));
                        s.Run(5);   // 처음 12초 (주컴퓨터가 밸브를 잠그기 전)
                        return (t0 - net.WaterTank) * 0.018 * Time.Minutes(1) / 5;   // kg = L · 분에
                    }
                    return (Run(1), Run(0.05f));
                }
                var (cutL, seepL) = Flow();
                Check("끊긴 관의 흐름: 물 재생기가 도는 물관 (½인치 · 100kPa)이 끊기면 분에 30L 넘게 (관 단면 × √(2ΔP/ρ)) · 스미는 틈 (0.05)의 열 배 넘게",
                    cutL > 30 && cutL > 10 * seepL && seepL > 0, $"끊김 분에 {cutL:0.0}L · 스밈 분에 {seepL:0.000}L");
            }

            var small = Deck(0.5e6); var big = Deck(8e6);
            Check("폭발은 충격량으로 판을 찢는다: 방 가운데 0.5MJ (TNT 120g)는 첨두압이 바닥 정격을 넘어도 바닥을 못 뚫고 · 8MJ (TNT 1.9kg)는 뚫는다",
                small.kpa > 300 && small.holes == 0 && big.holes > 0, $"0.5MJ 첨두 {small.kpa:0}kPa · 뚫린 바닥 {small.holes}칸 / 8MJ 첨두 {big.kpa:0}kPa · {big.holes}칸");
        }
    }
}
