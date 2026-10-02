using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v14.3~v14.4 관계의 이유: "무엇 때문에 그런 사이인지"가 남는다.
// 친밀도 숫자만이 아니라 — 나를 구해 줬다 · 내 실수를 감쌌다 · 다쳤을 때 일을 대신했다 · 내 경고를 무시했다 · 내 물건을 가져갔다.
// 같은 일을 서로 다르게 기억할 수 있다 (v14.4: 엇갈린 기억과 진실).

public enum RelationReason
{
    SavedMe, CoveredMyMistake, DidMyShift, IgnoredMyWarning, TookMyThing, SavedMyThing, GaveMeGift, FixedMyThing, AbandonedMe,
    NursedMe, TaughtMe, BlamedMe, Comforted, BrokeMyThing, KeptPromise, Apologized,
    SharedHardship, // v16.12 원정에서 함께 고생했다
    CutMyHair, BotchedMyHair, // v17.1 머리를 잘라 줬다 · 망쳐 놨다
}

public sealed class RelationMemory
{
    public int Who { get; init; }      // 기억하는 사람
    public int About { get; init; }    // 그 사람에 대해
    public RelationReason Reason { get; set; }
    public string Text { get; set; } = "";
    public long Tick { get; set; }     // 마지막으로 겪은 때 (같은 이유가 다시 생기면 새로)
    /// <summary>+ 고마움 · − 서운함 (관계를 끌어당기는 힘).</summary>
    public float Weight { get; set; }
    /// <summary>v14.4 실제로는 무엇이었나 (엇갈린 기억 — 진실을 들으면 바뀐다).</summary>
    public string? Truth { get; set; }
    public bool Revealed { get; set; }
}

public sealed partial class RelationSystem
{
    private readonly World _w;
    public List<RelationMemory> All { get; } = new();

    public RelationSystem(World w) => _w = w;

    public static float WeightOf(RelationReason r) => r switch
    {
        RelationReason.SavedMe => 0.5f, RelationReason.CoveredMyMistake => 0.3f, RelationReason.DidMyShift => 0.2f, RelationReason.NursedMe => 0.25f,
        RelationReason.TaughtMe => 0.15f, RelationReason.Comforted => 0.12f, RelationReason.GaveMeGift => 0.2f, RelationReason.FixedMyThing => 0.18f,
        RelationReason.SavedMyThing => 0.15f, RelationReason.KeptPromise => 0.1f, RelationReason.Apologized => 0.12f, RelationReason.SharedHardship => 0.22f, RelationReason.CutMyHair => 0.12f, RelationReason.BotchedMyHair => -0.12f,
        RelationReason.IgnoredMyWarning => -0.25f, RelationReason.TookMyThing => -0.15f, RelationReason.AbandonedMe => -0.45f,
        RelationReason.BlamedMe => -0.25f, RelationReason.BrokeMyThing => -0.15f,
        _ => 0f,
    };

    public static string Name(RelationReason r) => r switch
    {
        RelationReason.SavedMe => "나를 구해 줬다", RelationReason.CoveredMyMistake => "내 실수를 감쌌다", RelationReason.DidMyShift => "아플 때 일을 대신했다",
        RelationReason.IgnoredMyWarning => "내 경고를 무시했다", RelationReason.TookMyThing => "내 물건을 가져갔다", RelationReason.SavedMyThing => "내 물건을 건져 줬다",
        RelationReason.GaveMeGift => "선물을 줬다", RelationReason.FixedMyThing => "내 물건을 고쳐 줬다", RelationReason.AbandonedMe => "나를 두고 갔다",
        RelationReason.NursedMe => "나를 돌봐 줬다", RelationReason.TaughtMe => "나를 가르쳤다", RelationReason.BlamedMe => "나를 탓했다",
        RelationReason.Comforted => "위로해 줬다", RelationReason.BrokeMyThing => "내 물건을 망가뜨렸다", RelationReason.Apologized => "먼저 사과했다", RelationReason.SharedHardship => "원정에서 함께 고생했다", RelationReason.CutMyHair => "머리를 잘라 줬다", RelationReason.BotchedMyHair => "머리를 망쳐 놨다", _ => "약속을 지켰다",
    };

    /// <summary>{who}가 {about}에 대해 기억한다 (같은 이유는 하나로 묶고 최근 것으로).</summary>
    public RelationMemory Remember(CrewMember who, CrewMember about, RelationReason reason, string text)
    {
        var w = _w;
        var m = All.FirstOrDefault(x => x.Who == who.Id && x.About == about.Id && x.Reason == reason);
        if (m != null) { m.Text = text; m.Tick = w.Tick; m.Weight = Math.Clamp(m.Weight + WeightOf(reason) * 0.5f, -1f, 1f); return m; }
        m = new RelationMemory { Who = who.Id, About = about.Id, Reason = reason, Text = text, Tick = w.Tick, Weight = WeightOf(reason) };
        All.Add(m);
        // 한 사람이 한 사람에 대해 품는 기억은 여섯 개까지 (가장 가벼운 것부터 잊는다)
        var mine = All.Where(x => x.Who == who.Id && x.About == about.Id).ToList();
        if (mine.Count > 6) All.Remove(mine.OrderBy(x => MathF.Abs(x.Weight)).ThenBy(x => x.Tick).First());
        return m;
    }

    public IEnumerable<RelationMemory> Of(CrewMember who, CrewMember about) => All.Where(x => x.Who == who.Id && x.About == about.Id);

    /// <summary>그 사람을 그렇게 보는 가장 큰 이유 (없으면 null).</summary>
    public RelationMemory? Why(CrewMember who, CrewMember about) =>
        Of(who, about).OrderByDescending(x => MathF.Abs(x.Weight)).ThenByDescending(x => x.Tick).FirstOrDefault();

    /// <summary>기억이 그 사람을 믿는 정도에 더하는 몫 (−1~1).</summary>
    public float Trust(CrewMember who, CrewMember about) => Math.Clamp(Of(who, about).Sum(x => x.Weight), -1f, 1f);
}
