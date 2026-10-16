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

    public static int At(Layout l, int x, int y) => l.Index(x + 1, y + 1); // 글자 그림 좌표(둘레 한 줄 덧댐을 뺀)
}
