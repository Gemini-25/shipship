using ShipEngine;

namespace Lab;

/// <summary>시험 · 장면에 쓰는 작은 세계들.</summary>
public static class Worlds
{
    public static Tables T => Tables.Default;

    /// <summary>두 방 + 통로: 왼쪽 방 | 보통 문 | 오른쪽 방, 아래는 차압 문 · 보통 문으로 통로와 이어진다.</summary>
    public const string TwoRooms = @"
#########################
#.........#.............#
#.........#.............#
#.........D.............#
#.........#.............#
#.........#.............#
#####P#######D###########
#.......................#
#.......................#
#########################";

    /// <summary>두 방 사이 차압 문 하나 (닫히면 막힌다).</summary>
    public const string Sealed = @"
#########################
#.........#.............#
#.........#.............#
#.........P.............#
#.........#.............#
#.........#.............#
#########################";

    /// <summary>마음 시험 배: 왼쪽 방(우주복 보관함 · 배전반 · 배터리 · 침대) | 오른쪽 방(의자 넷 · 충전독 · 보관함) · 아래 통로(작업대 · 배식기 · 충전독).
    /// 통로로 가는 문이 둘 (왼쪽 방 · 오른쪽 방).</summary>
    public const string MindShip = @"
#########################
#..s......#....a....a.s.#
#.........#.............#
#..p.b....D.............#
#.........#....a....a.r.#
#..e....q.#.............#
#####D########D##########
#m.r..........w.....m...#
#.......................#
#########################";

    /// <summary>마음 시험 배 + 주컴퓨터(왼쪽 방 위).</summary>
    public static string MindShipComputer => MindShip.Replace("#..s......#....a....a.s.#", "#..s...c..#....a....a.s.#");

    public static Dictionary<char, string> MindLegend => new()
    {
        ['c'] = "MainComputer",
        ['s'] = "SuitLocker", ['p'] = "PowerPanel", ['b'] = "Battery", ['a'] = "Seat", ['e'] = "Bed",
        ['r'] = "RobotDock", ['w'] = "Workbench", ['m'] = "MealDispenser", ['q'] = "RobotDock",
    };

    /// <summary>문 위의 벽: 위는 통로 · 아래는 닫힌 문 (문 양옆은 다른 방).</summary>
    public const string WallOverDoor = @"
#########
#.......#
#########
#...D...#
#########";

    /// <summary>좌우 대칭 방 (가운데 칸이 정확히 가운데).</summary>
    public const string Symmetric = @"
###########
#.........#
#.........#
#.........#
#.........#
#.........#
###########";

    public static Layout Ascii(string art, Dictionary<char, string>? legend = null) => LayoutBuilder.FromAscii("시험", art, T, legend);

    /// <summary>장(기체 · 열)만 붙인 세계. 공기를 채운다.</summary>
    public static Sim FieldOnly(Layout layout, ulong seed = 1, Action<Field>? setup = null)
    {
        var s = new Sim(new SimSetup(layout, seed, sim =>
        {
            var f = new Field(sim.Layout, sim.Tables);
            sim.Add(f);
        }));
        var field = s.Get<Field>();
        field.FillAir();
        setup?.Invoke(field);
        return s;
    }

    /// <summary>모든 엔진을 붙인 세계 (ShipWorld) — 공기 · 설비 + 준비.</summary>
    public static Sim Full(Layout layout, ulong seed = 1, Action<Sim>? setup = null)
    {
        var s = ShipWorld.Create(layout, seed);
        setup?.Invoke(s);
        return s;
    }

    /// <summary>장 + 화학(불) + 설비를 앉힌 세계.</summary>
    public static Sim WithChem(Layout layout, ulong seed = 1, Action<Field, Chemistry>? setup = null)
    {
        var s = new Sim(new SimSetup(layout, seed, sim =>
        {
            var f = new Field(sim.Layout, sim.Tables);
            sim.Add(f);
            sim.Add(new Chemistry(f));
        }));
        var field = s.Get<Field>();
        field.FillAir();
        Furnish.Apply(field);
        setup?.Invoke(field, s.Get<Chemistry>());
        return s;
    }

    /// <summary>의자 줄이 있는 방 (c: 의자 · 글자마다 다른 의자).</summary>
    public const string Lounge = @"
#################
#...............#
#...............#
#..abcd.........#
#...............#
#...............#
#################";

    public static Dictionary<char, string> Seats => new() { ['a'] = "Seat", ['b'] = "Seat", ['c'] = "Seat", ['d'] = "Seat", ['e'] = "Bed", ['f'] = "Fridge" };

    public static int At(Layout l, int x, int y) => l.Index(x + 1, y + 1); // 글자 그림 좌표(둘레 한 줄 덧댐을 뺀)
}
