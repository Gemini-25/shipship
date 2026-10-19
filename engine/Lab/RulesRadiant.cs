using ShipEngine;

namespace Lab;

/// <summary>③ 장 물리 — 불의 복사열: 보이는 불은 몇 칸 떨어져서도 데운다 (점원 χQ/4πr²) · 벽은 가린다 · 불 곁의 벽이 달아오른다. docs/DEPTH.md</summary>
public static partial class Program
{
    /// <summary>의자 넷(a~d)이 벽(x=8)에 붙은 방 · 벽 너머 빈 방 (문 없음).</summary>
    private const string RadiantRooms = @"
#################
#.......#.......#
#.......#.......#
#...abcd#.......#
#.......#.......#
#.......#.......#
#################";

    private static void RadiantRules()
    {
        if (!Do("복사열")) return;
        Console.WriteLine("\n[③ 복사열]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";

        // ── 불 곁 두 칸: 트인 곳에 선 사람은 공기가 데워지기 전에 복사열로 데고 · 같은 두 칸이라도 벽 너머는 안 덴다 · 불 곁 벽이 달아오른다 ──
        {
            var lay = Worlds.Ascii(RadiantRooms, Worlds.Seats);
            int At(int x, int y) => Worlds.At(lay, x, y);
            var s = Worlds.Full(lay, 1);
            var life = s.Get<Life>(); var ch = s.Get<Chemistry>(); var f = s.Get<Field>();
            int open = life.AddBody(At(7, 5)), behind = life.AddBody(At(9, 3)); // 둘 다 가장 가까운 의자(d)에서 두 칸
            int wall = At(8, 3), farWall = At(0, 3); // 의자 줄을 마주한 벽 · 같은 방 반대쪽 벽 (뜨거운 공기만 닿는다)
            s.Run(3);
            int cause = s.Causes.Add(CauseKind.Setup, "의자 줄에 불이 붙었다", s.Tick);
            for (int x = 4; x <= 7; x++) ch.Ignite(At(x, 3), 3e6, cause);
            double wall0 = f.Ts[wall], far0 = f.Ts[farWall], burnCool = 0, peakQ = 0;
            for (int k = 0; k < Time.Minutes(3); k++)
            {
                float before = life[open].Burn;
                s.Step();
                if (f.Temp(At(7, 5)) < 330) burnCool += life[open].Burn - before; // 공기가 데워지기 전 (뜨거운 공기 화상이 아닌 몫)
                peakQ = Math.Max(peakQ, life.RadiantKwM2(At(7, 5), ch, out _));
                if (Debug && k % 25 == 0) Console.WriteLine($"    {k / 25}분 불 {ch.BurningCount} · 받는 열 {life.RadiantKwM2(At(7, 5), ch, out _):0.0}kW/m² · 공기 {f.Temp(At(7, 5)):0}K · 화상 {life[open].Burn:0.00} / 벽 너머 {life[behind].Burn:0.00} · 벽 {f.Ts[wall]:0}K");
            }
            double nearRise = f.Ts[wall] - wall0, farRise = f.Ts[farWall] - far0;
            Check("불의 복사열: 의자 줄 불에서 두 칸 — 트인 곳에 선 사람은 공기가 데워지기 전에 데고 · 같은 두 칸이라도 벽 너머는 안 덴다 · 불을 마주한 벽이 먼 벽보다 훨씬 데워진다",
                burnCool > 0.05 && life[behind].Burn < 1e-6 && nearRise > 3 && nearRise > farRise * 3 && s.Causes.Story(life[open].HurtCause).Contains("의자 줄"),
                $"받은 열 많게는 {peakQ:0.0}kW/m² · 트인 곳 화상 {life[open].Burn:0.00} (공기가 데워지기 전 {burnCool:0.00}) · 벽 너머 {life[behind].Burn:0.00} · 마주한 벽 +{nearRise:0.0}K · 먼 벽 +{farRise:0.0}K · {s.Causes.Story(life[open].HurtCause)}");
        }
    }
}
