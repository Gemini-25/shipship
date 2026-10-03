using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v16.4 물건 물리 한 벌: 질량 · 마찰 · 고정 · 충격 내성 — 폭발(Blast) · 기동(v17.0) · 무중력(v18.4)이 같은 함수로 물건을 움직인다.
// 깨짐 단계(금 → 깨짐 → 조각 — 날카로운 조각은 칸 상태 "유리"가 되어 위험물 · 청소 대상) · 담긴 액체가 흐르고 · 가루가 흩날린다(→ 분진 폭발).
// 물건은 재질을 갖고 Matter 표대로 원소에 반응한다: 러그 · 수건은 물을 머금고(넘치면 아래가 젖는다) · 마른 뒤 달궈지면 그을다 연기 ·
// 플라스틱은 녹아 유독 연기 · 종이는 젖으면 망가진다 · 얼음은 녹아 물 · 유리는 급한 온도 변화에 금이 간다.

public enum ArticleKind : byte
{
    Rug, Towel, RubberMat, PaperStack, CardboardBox, PlasticCrate, GlassJar, Mug, WaterJug, OilCan, PowderSack, IceBlock, Toolbox, FoodCrate, Radio, Shards,
}

public enum BreakStage : byte { Intact, Cracked, Broken, Shards }

/// <summary>물건 종류 한 줄: 이름 · 재질 · 질량(kg) · 마찰 · 충격 내성(J) · 머금는 물(L) · 통로 점유(길찾기 비용) · 바람에 날림 · 전자기기 · 담긴 것 · 바닥에 깔림.</summary>
public sealed record ArticleSpec(ArticleKind Kind, string Name, Material Mat, float Mass, float Mu, float Tough, float Capacity, int Bulk, bool Light, bool Electronic,
    Material Holds = Material.None, float Contents = 0f, bool Flat = false);

public static class ArticleSpecs
{
    public static readonly ArticleSpec[] All =
    {
        new(ArticleKind.Rug, "러그", Material.Fabric, 4f, 0.6f, 500f, 6f, 0, false, false, Flat: true),
        new(ArticleKind.Towel, "수건", Material.Fabric, 0.4f, 0.5f, 300f, 0.8f, 0, true, false),
        new(ArticleKind.RubberMat, "고무 매트", Material.Rubber, 3f, 0.9f, 800f, 0f, 0, false, false, Flat: true),
        new(ArticleKind.PaperStack, "서류 뭉치", Material.Paper, 0.5f, 0.3f, 50f, 0.4f, 0, true, false),
        new(ArticleKind.CardboardBox, "종이 상자", Material.Paper, 3f, 0.5f, 60f, 1.5f, 8, false, false),
        new(ArticleKind.PlasticCrate, "플라스틱 상자", Material.Plastic, 4f, 0.4f, 150f, 0f, 10, false, false),
        new(ArticleKind.GlassJar, "유리병", Material.Glass, 0.6f, 0.3f, 4f, 0f, 0, false, false, Material.Liquid, 0.5f),
        new(ArticleKind.Mug, "사기 컵", Material.Ceramic, 0.3f, 0.35f, 6f, 0f, 0, false, false),
        new(ArticleKind.WaterJug, "물통", Material.Plastic, 10f, 0.4f, 120f, 0f, 4, false, false, Material.Liquid, 10f),
        new(ArticleKind.OilCan, "기름통", Material.Metal, 6f, 0.35f, 300f, 0f, 4, false, false, Material.Oil, 5f),
        new(ArticleKind.PowderSack, "가루 포대", Material.Powder, 20f, 0.7f, 40f, 2f, 6, false, false, Material.Powder, 20f),
        new(ArticleKind.IceBlock, "얼음 덩어리", Material.Ice, 8f, 0.05f, 80f, 0f, 3, false, false),
        new(ArticleKind.Toolbox, "공용 공구함", Material.Metal, 8f, 0.5f, 400f, 0f, 3, false, false),
        new(ArticleKind.FoodCrate, "식량 상자", Material.Food, 6f, 0.5f, 30f, 1f, 6, false, false),
        new(ArticleKind.Radio, "휴대 무전기", Material.Plastic, 0.4f, 0.4f, 15f, 0f, 0, false, true),
        new(ArticleKind.Shards, "조각", Material.Glass, 0.2f, 0.6f, 99999f, 0f, 0, false, false, Flat: true),
    };
    public static ArticleSpec Of(ArticleKind k) => All[(int)k];
}

/// <summary>배 안에 놓인 물건 하나 (재질 · 물리 · 원소 상태).</summary>
public sealed class Article
{
    public int Id { get; init; }
    public ArticleKind Kind { get; internal set; }
    public ArticleSpec Spec => ArticleSpecs.Of(Kind);
    public string Name => Kind == ArticleKind.Shards ? $"{Materials.Name(Mat)} 조각" : Spec.Name;
    /// <summary>재질 (얼음이 녹으면 바뀐다 · 조각은 깨진 것의 재질).</summary>
    public Material Mat { get; internal set; }
    public Cell At { get; internal set; }
    /// <summary>제자리 (러그를 말린 뒤 되돌려 놓는 곳).</summary>
    public Cell Home { get; internal set; }
    /// <summary>칸 안의 자리 (-0.35~0.35 · 화면) · 돌아간 각도.</summary>
    public Vector2 Off { get; internal set; }
    public float Angle { get; internal set; }
    public float Mass { get; internal set; }
    public bool Fixed { get; internal set; }
    /// <summary>말아 둔 매트 · 접어 둔 것 (깔면 풀린다).</summary>
    public bool Stowed { get; internal set; }
    /// <summary>걸어 말리는 중 (러그 · 수건).</summary>
    public bool Hung { get; internal set; }
    public int CarriedBy { get; internal set; } = -1;
    public int ClaimedBy { get; internal set; } = -1;
    /// <summary>머금은 물 (L).</summary>
    public float Water { get; internal set; }
    public float WetFrac => Spec.Capacity > 0f ? Math.Clamp(Water / Spec.Capacity, 0f, 1f) : Math.Clamp(Water, 0f, 1f);
    /// <summary>물건의 온도 (℃).</summary>
    public float Temp { get; internal set; } = 20f;
    internal float TempWas = 20f;
    /// <summary>그을음 · 탄 정도 0~1 (1 = 다 탔다).</summary>
    public float Char { get; internal set; }
    public bool Smolder { get; internal set; }
    /// <summary>녹은 정도 0~1 (플라스틱 · 고무).</summary>
    public float Melt { get; internal set; }
    public BreakStage Stage { get; internal set; }
    /// <summary>망가졌다 (젖은 종이 · 뭉친 가루 · 상한 음식 · 합선된 기기).</summary>
    public bool Ruined { get; internal set; }
    /// <summary>담긴 것 (L · kg) — 깨지면 쏟아진다.</summary>
    public float Contents { get; internal set; }
    /// <summary>손때 (Soil 종류별 — 공구함 · 손잡이처럼 다음 사람에게 옮는다).</summary>
    public readonly float[] Soil = new float[ShipSim.Core.Soil.Kinds];
    /// <summary>무중력에서 떠도는 속도 (칸/분).</summary>
    public Vector2 Vel { get; internal set; }
    /// <summary>누가 그 상태를 봤다 (보이는 대로 — 숨은 건 모른다).</summary>
    public bool Known { get; internal set; }
    /// <summary>주 컴퓨터가 위험하다고 짚었다.</summary>
    public bool Flagged { get; internal set; }
    public long Since { get; internal set; }
    public long LastSpark { get; internal set; } = -100000;
    public long LastMoved { get; internal set; } = -100000;
    public string Why { get; internal set; } = "";
    public bool Loose => CarriedBy < 0 && !Fixed;
    public bool Flammable => Matter.Ignitability(Mat, WetFrac) > 0.12f && Stage != BreakStage.Shards && Char < 1f;
}

/// <summary>바닥에 쏟아진 액체 한 칸 (물 · 기름 · 냉각수).</summary>
public sealed class Spill
{
    public Material Kind { get; set; } = Material.Liquid;
    public float Liters { get; set; }
    public bool Coolant { get; set; }
    public string Why { get; set; } = "";
}

/// <summary>바닥 아래 배선 접속부 (러그 · 바닥 아래라 보이지 않는다).</summary>
public sealed class Junction
{
    public Cell At { get; init; }
    public int Room { get; init; } = -1;
    public float Wet { get; internal set; }
    public bool Known { get; internal set; }
    public bool Suspected { get; internal set; }
    public bool Taped { get; internal set; }
    /// <summary>찾아서 손봤다 (다시 젖으면 풀린다).</summary>
    public bool Fixed { get; internal set; }
    public int ClaimedBy { get; internal set; } = -1;
    public long LastSpark { get; internal set; } = -100000;
    public bool Live { get; internal set; }
}

/// <summary>공개 API: 폭발 · 기동 · 무중력이 같은 함수로 물건을 움직인다.</summary>
public static class ObjectPhysics
{
    public const float G = 9.8f;

    /// <summary>그 칸의 풀린 물건을 민다 (힘 = 충격량 kg·m/s, 방향). 움직인 물건 수.</summary>
    public static int Push(World w, Cell c, float impulse, Vector2 dir, string why) => w.Matter.PushCell(c, impulse, dir, why);

    /// <summary>물건 하나를 민다.</summary>
    public static bool Push(World w, Article t, float impulse, Vector2 dir, string why) => w.Matter.PushThing(t, impulse, dir, why);

    /// <summary>기동 (v17.0): 배가 accel(G 단위)로 seconds 동안 가속하면 방 안 풀린 물건이 미끄러진다 — 마찰을 넘는 만큼만.</summary>
    public static int Shove(World w, Room room, Vector2 accelG, float seconds, string why)
    {
        int n = 0;
        float a = accelG.Length();
        if (a < 0.001f) return 0;
        var dir = -accelG / a; // 배가 앞으로 가면 물건은 뒤로
        foreach (var t in w.Matter.Things.ToList())
        {
            if (!t.Loose || w.Ship.RoomAt(t.At) != room) continue;
            float mu = Friction(w, t) * w.Matter.Gravity;
            if (a <= mu) continue; // 정지 마찰을 못 넘는다
            if (w.Matter.PushThing(t, t.Mass * (a - mu) * G * seconds, dir, why)) n++;
        }
        return n;
    }

    /// <summary>무중력 (v18.4): 1 = 보통 · 0 = 마찰이 사라져 물건이 떠돈다.</summary>
    public static void SetGravity(World w, float g) => w.Matter.Gravity = Math.Clamp(g, 0f, 2f);

    /// <summary>부딪힘: 충격 에너지(J)가 내성을 넘으면 깨짐 단계가 오른다.</summary>
    public static BreakStage Impact(World w, Article t, float energy, string why) => w.Matter.Impact(t, energy, why);

    /// <summary>마찰 계수: 물건 재질 × 바닥 (젖음 · 기름 · 서리가 미끄럽게).</summary>
    public static float Friction(World w, Article t)
    {
        var b = w.Body;
        var floor = b.FloorAt(t.At);
        float slip = floor == Material.None ? 0f : Materials.SlipNow(floor, b.Mark(t.At, CellMark.Wet), b.Mark(t.At, CellMark.Oil), b.Mark(t.At, CellMark.Frost), 0f);
        return MathF.Max(0.02f, t.Spec.Mu * (1f - 0.7f * slip));
    }
}

public sealed class MatterStats
{
    public int Seeded, Pushed, Slid, Cracked, Broken, Shattered, Melted, Charred, Smolders, Ignited, Thawed, Ruined, ThermalCracks, Overflows, Seeps,
        JunctionsWet, JunctionsFound, JunctionsFixed, Sparks, LiveShocks, Shorts, MatsLaid, MatWorks, Lifted, Hung, Returned, MovedFromHeat, Doused,
        SmokeAlarms, DoorSeals, Unseals, ComputerWarns, HeededWarns, LeakWarns, O2Warns, DustWarns, DustClouds, DustBlasts, OxygenFlashes,
        HandleTouches, ToolTouches, HandleWipes, Aisles, Spills, Drifted, Rattled;
    public float Absorbed, Spilled, Drained, Evaporated;
    public override string ToString() =>
        $"물건 {Seeded} · 밀림 {Pushed}(미끄럼 {Slid} · 떠돎 {Drifted} · 덜컹 {Rattled}) · 금 {Cracked} · 깨짐 {Broken} · 조각 {Shattered}(열충격 {ThermalCracks}) · 녹음 {Melted} · 그을음 {Charred}(연기 {Smolders} · 불 {Ignited}) · 녹아 물 {Thawed} · 망가짐 {Ruined} · " +
        $"머금음 {Absorbed:0.0}L(넘침 {Overflows} · 스밈 {Seeps}) · 쏟음 {Spills}({Spilled:0.0}L · 빠짐 {Drained:0.0}L · 마름 {Evaporated:0.0}L) · 접속부 젖음 {JunctionsWet}(발견 {JunctionsFound} · 고침 {JunctionsFixed}) · 불꽃 {Sparks} · 감전 {LiveShocks} · 합선 {Shorts} · " +
        $"고무 매트 {MatsLaid}(그 위 작업 {MatWorks}) · 걷음 {Lifted}(널어 말림 {Hung} · 되돌림 {Returned}) · 불 곁에서 치움 {MovedFromHeat} · 적심 {Doused} · 연기 감지 {SmokeAlarms}(문 닫음 {DoorSeals} · 다시 엶 {Unseals}) · " +
        $"컴퓨터 경고 {ComputerWarns}(따름 {HeededWarns} · 누설 전류 {LeakWarns} · 산소 {O2Warns} · 분진 {DustWarns}) · 분진 {DustClouds}(폭발 {DustBlasts}) · 산소 불꽃 {OxygenFlashes} · 손잡이 {HandleTouches}(닦음 {HandleWipes}) · 공구 {ToolTouches} · 통로 치움 {Aisles}";
}

public sealed partial class MatterSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 6151 + 97));
    public MatterStats Stats { get; } = new();
    public List<Article> Things { get; } = new();
    public SortedDictionary<int, Spill> Spills { get; } = new();
    public List<Junction> Junctions { get; } = new();
    /// <summary>중력 배율 (무중력 v18.4).</summary>
    public float Gravity { get; internal set; } = 1f;
    /// <summary>시험: 이때까지 승무원이 미리 치우지 않는다 (사고 사슬을 끝까지 보려고).</summary>
    public long QuietUntil { get; set; } = -1;
    private int _next;
    private bool _seeded;
    private long _nextMinute;
    private bool[] _has = Array.Empty<bool>();

    public MatterSystem(World w) => _w = w;

    public Article? Get(int id) { foreach (var t in Things) if (t.Id == id) return t; return null; }
    public IEnumerable<Article> At(Cell c) { foreach (var t in Things) if (t.CarriedBy < 0 && t.At == c) yield return t; }
    public bool Any(Cell c) { int i = Idx(c); return i >= 0 && i < _has.Length && _has[i]; }
    public Spill? SpillAt(Cell c) => Spills.TryGetValue(Idx(c), out var s) ? s : null;
    public float LitersAt(Cell c) => SpillAt(c)?.Liters ?? 0f;
    private int Idx(Cell c) => _w.Ship.Grid.InBounds(c) ? _w.Ship.Grid.Index(c) : -1;
    public bool MatAt(Cell c) { if (!Any(c)) return false; foreach (var t in At(c)) if (t.Kind == ArticleKind.RubberMat && !t.Stowed) return true; return false; }

    /// <summary>새 물건을 놓는다.</summary>
    public Article Add(ArticleKind kind, Cell at, string why = "", bool stowed = false)
    {
        var sp = ArticleSpecs.Of(kind);
        var t = new Article
        {
            Id = _next++, Kind = kind, Mat = sp.Mat, At = at, Home = at, Mass = sp.Mass, Contents = sp.Contents, Stowed = stowed, Since = _w.Tick, Why = why,
            Off = new Vector2(((_next * 37) % 7 - 3) * 0.06f, ((_next * 53) % 7 - 3) * 0.06f), Angle = ((_next * 29) % 11 - 5) * 0.06f,
        };
        var room = _w.Ship.RoomAt(at);
        if (room != null) { t.Temp = room.Air.Temperature; t.TempWas = t.Temp; }
        Things.Add(t);
        Mark(at);
        return t;
    }

    public void Remove(Article t) { Things.Remove(t); Reindex(); }

    private void Mark(Cell c) { int i = Idx(c); if (i < 0) return; if (_has.Length != _w.Ship.Grid.CellCount) Reindex(); else _has[i] = true; }

    private void Reindex()
    {
        int n = _w.Ship.Grid.CellCount;
        if (_has.Length != n) _has = new bool[n]; else Array.Clear(_has);
        foreach (var t in Things) if (t.CarriedBy < 0 && Idx(t.At) is int i && i >= 0) _has[i] = true;
    }

    internal void Place(Article t, Cell at)
    {
        t.At = at;
        t.CarriedBy = -1;
        t.LastMoved = _w.Tick;
        Reindex();
    }

    internal void PickUp(Article t, CrewMember c)
    {
        t.CarriedBy = c.Id;
        t.Stowed = false;
        t.Hung = false;
        Reindex();
    }

    // ───────────────────────────── 밀기 · 부딪힘 ─────────────────────────────

    private bool Passable(Cell c, out bool door)
    {
        var ship = _w.Ship;
        door = false;
        if (!ship.Grid.InBounds(c)) return false;
        if (ship.DoorAt(c) is Door d) { door = true; return !d.Removed && d.Openness > 0.5f; }
        return ship.IsOpenFloor(c);
    }

    public int PushCell(Cell c, float impulse, Vector2 dir, string why)
    {
        int n = 0;
        if (!Any(c)) return 0;
        foreach (var t in At(c).ToList()) if (PushThing(t, impulse, dir, why)) n++;
        return n;
    }

    /// <summary>민다: 속도 = 충격량/질량, 미끄러지는 거리 = v²/(2μg) — 벽 · 설비 · 닫힌 문에 부딪히면 남은 속도만큼 충격.</summary>
    public bool PushThing(Article t, float impulse, Vector2 dir, string why)
    {
        if (t.CarriedBy >= 0 || impulse <= 0f) return false;
        if (dir.LengthSquared() < 1e-6f) dir = Vector2.UnitX;
        dir = Vector2.Normalize(dir);
        if (t.Fixed)
        {
            if (impulse < t.Spec.Tough * 0.5f) return false;
            t.Fixed = false; // 고정이 뜯겨 나간다
        }
        float v0 = impulse / MathF.Max(0.05f, t.Mass);
        float mu = ObjectPhysics.Friction(_w, t) * Gravity;
        float dist = mu * ObjectPhysics.G > 0.05f ? v0 * v0 / (2f * mu * ObjectPhysics.G) : MathF.Min(8f, v0);
        if (Gravity < 0.2f && why != "무중력") t.Vel = dir * MathF.Min(3f, v0 * 0.3f); // 떠돈다 (통합6 떠도는 중엔 속도를 그대로 이어 간다 — 공기 저항만)
        dist = MathF.Min(8f, dist);
        var start = t.At;
        var pos = t.At.Center + t.Off;
        var last = t.At;
        float traveled = 0f;
        float hit = -1f;
        while (traveled < dist)
        {
            float step = MathF.Min(0.5f, dist - traveled);
            var np = pos + dir * step;
            var nc = Cell.FromPosition(np);
            if (nc != last && !Passable(nc, out _))
            {
                hit = 0.5f * t.Mass * MathF.Max(0f, v0 * v0 - 2f * mu * ObjectPhysics.G * traveled); // 벽 · 설비에 부딪힌다
                break;
            }
            pos = np;
            traveled += step;
            if (nc != last && !(_w.Ship.DoorAt(nc) != null)) last = nc;
        }
        // 문틀 칸엔 멈추지 않는다
        var end = last;
        if (end != start || (pos - (t.At.Center + t.Off)).LengthSquared() > 0.01f)
        {
            t.Off = Vector2.Clamp(pos - end.Center, new Vector2(-0.49f), new Vector2(0.49f));
            t.Angle += dir.X * 0.4f - dir.Y * 0.2f;
        }
        bool moved = end != start;
        if (moved)
        {
            Place(t, end);
            Stats.Pushed++;
            if (mu < 0.15f) Stats.Slid++;
            if (t.Kind == ArticleKind.Rug && JunctionAt(start) is { } j) Expose(t, j, null); // 밀려난 러그 아래가 드러났다
        }
        if (hit > 0f) Impact(t, hit, why); // 멈춘 자리에서 깨진다 (조각은 거기 흩어진다)
        if (hit > 0f && Gravity < 0.2f) t.Vel = -dir * MathF.Min(3f, v0 * 0.5f); // 통합6 무중력: 벽 · 설비에 부딪히면 튕겨 나와 반대로 떠돈다
        return moved;
    }

    /// <summary>깨짐 단계: 내성 대비 충격 (1~3배 한 단계 · 3배 넘으면 두 단계 · 8배면 바로 조각).</summary>
    public BreakStage Impact(Article t, float energy, string why)
    {
        if (t.Stage == BreakStage.Shards || energy <= 0f) return t.Stage;
        float tough = t.Spec.Tough;
        var room = _w.Ship.RoomAt(t.At);
        if (room != null && room.Air.Temperature < -5f && Matter.React(t.Mat, Element.Cold) == Reaction.Brittle) tough *= 0.4f; // 언 플라스틱 · 고무는 잘 깨진다
        if (t.Stage == BreakStage.Cracked) tough *= 0.5f;
        float ratio = energy / MathF.Max(0.1f, tough);
        if (ratio < 1f) return t.Stage;
        bool breakable = Matter.Breakable(t.Mat) || t.Spec.Holds != Material.None || t.Spec.Electronic;
        if (!breakable) { if (Matter.React(t.Mat, Element.Pressure) == Reaction.Scatter && t.Contents > 0f) Burst(t, why); return t.Stage; }
        int steps = ratio >= 8f ? 3 : ratio >= 3f ? 2 : 1;
        for (int k = 0; k < steps && t.Stage < BreakStage.Shards; k++) Advance(t, why);
        return t.Stage;
    }

    private void Advance(Article t, string why)
    {
        var w = _w;
        switch (t.Stage)
        {
            case BreakStage.Intact:
                t.Stage = BreakStage.Cracked;
                Stats.Cracked++;
                break;
            case BreakStage.Cracked:
                t.Stage = BreakStage.Broken;
                Stats.Broken++;
                if (t.Spec.Electronic) t.Ruined = true;
                Burst(t, why);
                break;
            case BreakStage.Broken:
                if (!Matter.Breakable(t.Mat)) { t.Ruined = true; return; } // 물통 · 기름통은 깨진 채 남는다
                Shatter(t, why);
                break;
        }
    }

    /// <summary>깨진 것이 조각이 된다 — 날카로운 조각은 칸 상태 "유리"(길찾기가 돌아가고 · 손보기가 쓸어 낸다).</summary>
    private void Shatter(Article t, string why)
    {
        var w = _w;
        Burst(t, why);
        t.Stage = BreakStage.Shards;
        t.Kind = ArticleKind.Shards;
        t.Mass = MathF.Min(t.Mass, 0.3f);
        t.Fixed = false;
        Stats.Shattered++;
        if (Matter.Sharp(t.Mat))
        {
            w.Body.RaiseMark(t.At, CellMark.Glass, 0.75f, $"깨진 {ArticleSpecs.Of(KindBefore(t)).Name} 조각 ({why})");
            // 튄 조각: 이웃 한 칸
            var d = Cell.Dirs4[(t.Id + (int)w.Tick) & 3];
            if (w.Ship.IsOpenFloor(t.At + d)) w.Body.RaiseMark(t.At + d, CellMark.Glass, 0.35f, "튄 조각");
        }
        var room = w.Ship.RoomAt(t.At);
        if (room != null) MarkLog.Add(room.Marks, w.Tick, $"{Materials.Name(t.Mat)} 물건이 깨져 조각이 흩어졌다 ({why})");
    }

    private static ArticleKind KindBefore(Article t) => t.Mat switch { Material.Glass => ArticleKind.GlassJar, Material.Ceramic => ArticleKind.Mug, Material.Ice => ArticleKind.IceBlock, _ => ArticleKind.PlasticCrate };

    /// <summary>담긴 것이 쏟아진다 (물 · 기름 → 바닥으로 흐름 · 가루 → 흩날림 · 음식 → 쏟은 음식).</summary>
    private void Burst(Article t, string why)
    {
        if (t.Contents <= 0f) return;
        var hold = t.Spec.Holds;
        if (hold is Material.Liquid or Material.Oil) Pour(t.At, hold, t.Contents, $"{Ko.IGa(t.Name)} 깨져 쏟아졌다");
        else if (hold == Material.Powder) Raise(t.At, MathF.Min(1f, 0.25f + t.Contents / 25f), $"{t.Name} 터짐 ({why})");
        t.Contents = 0f;
        if (t.Kind == ArticleKind.PowderSack) t.Ruined = true;
    }

    /// <summary>바닥에 액체를 붓는다 (흐르고 · 머금고 · 빠지고 · 마른다).</summary>
    public void Pour(Cell c, Material kind, float liters, string why, bool coolant = false)
    {
        int i = Idx(c);
        if (i < 0 || liters <= 0f || _w.Ship.Grid.Kind(c) != TileKind.Floor) return;
        if (!Spills.TryGetValue(i, out var s)) { s = new Spill { Kind = kind, Why = why }; Spills[i] = s; Stats.Spills++; }
        if (kind == Material.Oil) s.Kind = Material.Oil;
        s.Coolant |= coolant;
        s.Liters += liters;
        Stats.Spilled += liters;
    }

    // ───────────────────────────── 물건 한 벌의 틱 (1분마다) ─────────────────────────────

    private void UpdateThings(float h)
    {
        var w = _w;
        var ship = w.Ship;
        var b = w.Body;
        List<Article>? gone = null;
        foreach (var t in Things)
        {
            if (t.CarriedBy >= 0) continue;
            var room = ship.RoomAt(t.At);
            if (room == null || room.Detached) continue;
            if (t.Kind == ArticleKind.Shards && Matter.Sharp(t.Mat) && b.Mark(t.At, CellMark.Glass) < 0.1f) { (gone ??= new()).Add(t); continue; } // 쓸어 냈다
            var air = room.Air;
            float cellT = CellTemp(t.At, room);
            float fire = w.Fire.Count > 0 ? w.Fire.At(t.At) : 0f;
            float depth = MoistureSystem.Depth(room);
            var sp = t.Spec;

            // ── 물: 바닥 물 · 쏟은 물을 머금는다 (천 · 종이 · 나무 · 음식 · 가루) ──
            if (Matter.Absorbent(t.Mat) && sp.Capacity > 0f && !t.Hung)
            {
                float room_ = sp.Capacity - t.Water;
                float take = 0f;
                if (depth > 0.03f && room_ > 0f) take = MathF.Min(room_, (0.5f + 6f * depth) * sp.Capacity * h * 6f);
                if (take > 0f) { t.Water += take; Stats.Absorbed += take; }
            }
            // 담긴 물 · 땀 · 비: 종이 · 가루는 젖으면 망가진다 (표: 물 → Ruin)
            if (!t.Ruined && t.WetFrac > 0.3f && Matter.React(t.Mat, Element.Water) == Reaction.Ruin)
            {
                t.Ruined = true;
                Stats.Ruined++;
                MarkLog.Add(room.Marks, w.Tick, $"{Ko.IGa(t.Name)} 젖어 망가졌다");
            }
            if (!t.Ruined && t.Spec.Electronic && (t.Water > 0.05f || depth > 0.05f) && R.Chance(0.3f * h * 60f * Matter.Electronic(Element.Water).Rate / 6f)) { t.Ruined = true; Stats.Shorts++; } // 전자기기는 젖으면 불안정 → 합선

            // ── 열 · 온도 ──
            t.TempWas = t.Temp;
            float target = fire > 0f ? MathF.Max(cellT, air.Temperature + 300f * fire) : cellT;
            float k = Matter.Base(t.Mat) == Material.Metal ? 0.9f : t.Mass > 5f ? 0.25f : 0.5f;
            if (t.Temp > 50f && (LitersAt(t.At) > 0.1f || depth > 0.03f)) t.Temp = MathF.Min(t.Temp, air.Temperature + 8f); // 물이 닿으면 급히 식는다 (표: 열 × 냉기 = 금)
            else t.Temp += (target - t.Temp) * MathF.Min(1f, k * h * 60f);
            // 젖은 것은 김을 내며 마른다 — 물이 남은 동안 끓는 점 아래에 머문다 (젖은 천은 안 탄다)
            if (t.Water > 0f)
            {
                float evap = sp.Capacity > 0f ? sp.Capacity : 0.5f;
                float rate = (0.12f + 0.04f * MathF.Max(0f, t.Temp - 20f)) * (1f - 0.6f * room.Humidity) * (t.Hung ? 1.8f : 1f) * (air.Pressure < 30f ? 20f : 1f);
                float dry = MathF.Min(t.Water, evap * rate * h);
                t.Water -= dry;
                Stats.Evaporated += dry;
                if (dry > 0f && room.Humidity < 0.98f) room.Humidity = MathF.Min(1f, room.Humidity + dry / (room.Cells.Count * 40f));
                if (t.Water > 0.02f * evap) t.Temp = MathF.Min(t.Temp, 72f); // 증발이 열을 먹는다
            }
            // 급한 온도 변화: 유리 · 사기 · 얼음 (표: 열 × 냉기 = 금)
            if (Matter.Breakable(t.Mat) && t.Stage < BreakStage.Shards && MathF.Abs(t.Temp - t.TempWas) > Matter.ShockTolerance(t.Mat))
            {
                Stats.ThermalCracks++;
                Advance(t, MathF.Abs(t.Temp - t.TempWas) > 0 && t.Temp < t.TempWas ? "급히 식어" : "급히 달궈져");
            }

            // ── 그을음 · 연기 · 불 (천 · 종이 · 나무 · 음식 · 기름 · 가루) ──
            float ign = Matter.Ignitability(t.Mat, t.WetFrac, air.O2);
            if (t.Char < 1f && ign > 0.1f && t.Stage != BreakStage.Shards)
            {
                float cp = Matter.CharPoint(t.Mat);
                if (fire > 0.05f)
                {
                    t.Char = MathF.Min(1f, t.Char + 2.5f * fire * ign * h);
                    if (!t.Smolder) { t.Smolder = true; Stats.Smolders++; }
                }
                else if (t.Temp > cp)
                {
                    float before = t.Char;
                    t.Char = MathF.Min(1f, t.Char + (0.6f + (t.Temp - cp) / 18f) * ign * h);
                    if (before < 0.04f && t.Char >= 0.04f) { Stats.Charred++; MarkLog.Add(room.Marks, w.Tick, $"{Ko.IGa(t.Name)} 그을기 시작했다 ({t.Temp:0}℃)"); }
                    if (t.Char >= 0.04f && !t.Smolder) { t.Smolder = true; Stats.Smolders++; }
                }
                else if (t.Smolder && t.Temp < cp - 25f) t.Smolder = false; // 식으면 잦아든다
                if (t.Smolder && fire <= 0.05f)
                {
                    // 그을음 연기: 작은 방일수록 빨리 찬다 · 탄 냄새
                    float smoke = (2.5f + 60f / MathF.Max(4f, room.Volume)) * (0.5f + t.Char) * h;
                    air.Smoke = MathF.Min(1f, air.Smoke + smoke);
                    air.Toxin = MathF.Min(1f, air.Toxin + 0.2f * Matter.ToxicSmoke(t.Mat, sp.Electronic) * smoke);
                    // 다 그을리면 불꽃이 인다 (산소가 있으면)
                    if (t.Char > 0.6f && air.O2 > 14f && R.Chance(MathF.Min(0.9f, 1.2f * ign * h * 60f / 10f)) && w.Fire.Ignite(t.At, 0.2f))
                    {
                        Stats.Ignited++;
                        w.Log.Add(w.Tick, LogKind.Warning, $"{room.Name} — 그을던 {t.Name}에서 불꽃이 일었다");
                    }
                }
                if (t.Char >= 1f) { t.Ruined = true; t.Smolder = false; b.RaiseMark(t.At, CellMark.Soot, 0.6f, $"{t.Name} 탄 자국"); }
            }
            else if (t.Smolder && (ign <= 0.1f || t.Char >= 1f)) t.Smolder = false; // 적시면 꺼진다

            // ── 녹음: 플라스틱 · 고무 (유독 연기) ──
            if (t.Temp > Matter.MeltPoint(t.Mat) && Matter.MeltPoint(t.Mat) > 1f && t.Melt < 1f)
            {
                float before = t.Melt;
                t.Melt = MathF.Min(1f, t.Melt + (0.4f + (t.Temp - Matter.MeltPoint(t.Mat)) / 60f) * Matter.Rule(t.Mat, Element.Heat).Rate * h * 3f);
                float tox = Matter.ToxicSmoke(t.Mat, sp.Electronic);
                air.Toxin = MathF.Min(1f, air.Toxin + tox * 2.5f * (t.Melt - before) * 10f / MathF.Max(4f, room.Volume));
                air.Smoke = MathF.Min(1f, air.Smoke + 0.4f * (t.Melt - before) * 10f / MathF.Max(4f, room.Volume));
                if (before < 0.1f && t.Melt >= 0.1f) { Stats.Melted++; MarkLog.Add(room.Marks, w.Tick, $"{Ko.IGa(t.Name)} 녹아내린다 — 유독 연기"); }
                if (t.Melt >= 0.6f && !t.Ruined) { t.Ruined = true; if (t.Spec.Holds == Material.Liquid && t.Contents > 0f) Burst(t, "녹아 샘"); }
            }

            // ── 얼음 + 열 = 물 ──
            if (t.Mat == Material.Ice)
            {
                if (cellT > 0f || fire > 0f)
                {
                    float melt = MathF.Min(t.Mass, (0.8f + 0.12f * MathF.Max(0f, cellT) + 20f * fire) * Matter.Rule(Material.Ice, Element.Heat).Rate * h);
                    t.Mass -= melt;
                    Pour(t.At, Matter.Rule(Material.Ice, Element.Heat).Into, melt, $"녹은 {t.Name}");
                    t.Temp = 0f;
                    if (t.Mass <= 0.15f) { (gone ??= new()).Add(t); Stats.Thawed++; MarkLog.Add(room.Marks, w.Tick, $"{Ko.IGa(t.Name)} 다 녹아 물이 됐다"); }
                }
            }

            // ── 음식: 열 · 물 · 방사선에 상한다 ──
            if (t.Mat == Material.Food && !t.Ruined && (t.WetFrac > 0.4f || room.Radiation > 0.5f || t.Temp > 45f) && R.Chance(0.5f * h * 6f)) { t.Ruined = true; Stats.Ruined++; }

            // ── 진동: 덜컹거리며 기어간다 (사기 · 유리는 떨어져 깨진다) ──
            if (room.Vibration > 0.5f && t.Loose && t.Mass < 3f && R.Chance((room.Vibration - 0.5f) * h * 6f))
            {
                var d = Cell.Dirs4[R.Range(0, 4)];
                Stats.Rattled++;
                if (PushThing(t, t.Mass * 2.5f, new Vector2(d.X, d.Y), "진동") && Matter.React(t.Mat, Element.Vibration) is Reaction.Rattle or Reaction.Crack) Impact(t, t.Spec.Tough * 1.2f, "떨어졌다");
            }
            // ── 가벼운 물건은 바람(문틈 · 환기구)을 탄다 ──
            if (sp.Light && t.Loose && t.WetFrac < 0.3f && DraftAt(t.At) is Vector2 dv && dv.Length() > 0.25f && R.Chance(MathF.Min(1f, dv.Length()))) // 통합7 물먹은 천은 바람에 안 날린다 (히터 곁 젖은 수건이 바람에 세 칸 굴러가 버렸다)
                PushThing(t, t.Mass * dv.Length() * 3.2f, dv, "바람");
            // ── 무중력: 떠돈다 ──
            if (Gravity < 0.2f && t.Loose)
            {
                var v = t.Vel + (DraftAt(t.At) ?? Vector2.Zero) * 0.3f;
                if (v.LengthSquared() > 0.01f) { t.Vel = v * 0.95f; var p0 = t.At.Center + t.Off; if (PushThing(t, t.Mass * v.Length(), v, "무중력") || (t.At.Center + t.Off - p0).LengthSquared() > 0.01f) Stats.Drifted++; } // 통합6 좁은 칸 안에서 튕기며 떠도는 것도 떠돎
            }
        }
        if (gone != null) foreach (var t in gone) Things.Remove(t);
        if (gone != null) Reindex();
    }

    // ───────────────────────────── 액체: 흐름 · 머금음 · 빠짐 · 마름 ─────────────────────────────

    private readonly List<(int i, float l, Material k, bool cool, string why)> _flow = new();

    private void UpdateSpills(float h)
    {
        if (Spills.Count == 0) return;
        var w = _w;
        var ship = w.Ship;
        var grid = ship.Grid;
        var b = w.Body;
        _flow.Clear();
        List<int>? dead = null;
        foreach (var (i, s) in Spills)
        {
            var c = grid.CellAt(i);
            var room = ship.RoomAt(c);
            if (room == null || room.Detached || room.Air.Pressure < 30f) { (dead ??= new()).Add(i); continue; }
            // 1) 머금는다: 칸의 천 · 종이 (러그가 먼저) — 넘치면 그 아래 바닥이 젖는다
            if (s.Kind == Material.Liquid && Any(c))
                foreach (var t in At(c))
                {
                    if (!Matter.Absorbent(t.Mat) || t.Spec.Capacity <= 0f || t.Hung) continue;
                    float room_ = t.Spec.Capacity - t.Water;
                    if (room_ <= 0.001f)
                    {
                        if (t.Spec.Flat) { float pass = s.Liters * 0.4f; Seep(t, c, pass, room); s.Liters -= pass; } // 다 머금은 러그: 아래로 스민다
                        continue;
                    }
                    float take = MathF.Min(room_, s.Liters);
                    // 거의 다 머금은 깔개는 받은 물 일부를 그대로 아래로 흘려보낸다 (위에서는 아직 안 보인다)
                    if (t.Spec.Flat && t.WetFrac > 0.6f) { float pass = take * (t.WetFrac - 0.6f) / 0.4f * 0.6f; Seep(t, c, pass, room); take -= pass; s.Liters -= pass; }
                    t.Water += take;
                    s.Liters -= take;
                    Stats.Absorbed += take;
                    if (t.Water >= t.Spec.Capacity * 0.999f) { Stats.Overflows++; MarkLog.Add(room.Marks, w.Tick, $"{Ko.IGa(t.Name)} 물을 다 머금었다 — 넘친다"); }
                }
            // 2) 격자는 아래로 빠진다
            var floor = b.FloorAt(c);
            float drain = Materials.Of(floor).Drain;
            if (drain > 0f)
            {
                float d = MathF.Min(s.Liters, s.Liters * drain * 6f * h + 0.3f * drain * h * 60f);
                s.Liters -= d;
                Stats.Drained += d;
                if (d > 0.05f && JunctionAt(c) is { } jd && s.Kind == Material.Liquid) Wet(jd, d * 0.2f, "격자 아래로 빠진 물");
            }
            // 3) 흐른다: 얕은 쪽으로 (문이 열렸으면 문 너머로)
            float keepL = UnderRug(c) ? 0.05f : 0.3f; // 다 머금은 러그 밑에선 가장자리로 번져 나온다
            if (s.Liters > keepL)
            {
                float share = (s.Liters - keepL) * MathF.Min(0.8f, 6f * h * 4f + (keepL < 0.1f ? 0.5f : 0f));
                int n = 0;
                foreach (var d in Cell.Dirs4) if (FlowOk(c, c + d)) n++;
                if (n > 0)
                    foreach (var d in Cell.Dirs4)
                    {
                        var nc = c + d;
                        if (!FlowOk(c, nc)) continue;
                        if (ship.DoorAt(nc) != null) nc += d;
                        if (!ship.Grid.InBounds(nc) || grid.Kind(nc) != TileKind.Floor) continue;
                        int j = grid.Index(nc);
                        float there = Spills.TryGetValue(j, out var o) ? o.Liters : 0f;
                        if (there >= s.Liters) continue;
                        float give = share / n * (1f - there / MathF.Max(0.01f, s.Liters));
                        _flow.Add((j, give, s.Kind, s.Coolant, s.Why));
                        s.Liters -= give;
                    }
            }
            // 4) 마른다 (기름은 거의 안 마른다)
            float t0 = CellTemp(c, room);
            float ev = (s.Kind == Material.Oil ? 0.01f : 0.25f + 0.03f * MathF.Max(0f, t0 - 15f)) * (1f - 0.5f * room.Humidity) * h;
            s.Liters = MathF.Max(0f, s.Liters - ev);
            Stats.Evaporated += ev;
            // 5) 보이는 칸 상태: 물기 · 기름 · 서리 (냉각수 · 언 방)
            if (s.Liters > 0.05f)
            {
                if (s.Kind == Material.Oil) b.RaiseMark(c, CellMark.Oil, MathF.Min(1f, 0.3f + s.Liters / 3f), s.Why);
                else if (!UnderRug(c)) b.RaiseMark(c, CellMark.Wet, MathF.Min(1f, 0.3f + s.Liters / 2f), s.Why);
                if (s.Coolant || room.Air.Temperature < 1f) b.RaiseMark(c, CellMark.Frost, MathF.Min(1f, 0.3f + s.Liters / 3f), s.Coolant ? "냉각수 서리" : "언 물");
            }
            if (s.Liters <= 0.02f) (dead ??= new()).Add(i);
        }
        foreach (var (j, l, k, cool, why) in _flow)
        {
            if (!Spills.TryGetValue(j, out var o)) { o = new Spill { Kind = k, Why = why }; Spills[j] = o; }
            if (k == Material.Oil) o.Kind = Material.Oil;
            o.Coolant |= cool;
            o.Liters += l;
        }
        if (dead != null) foreach (var i in dead) Spills.Remove(i);
    }

    private bool FlowOk(Cell from, Cell to)
    {
        var ship = _w.Ship;
        if (!ship.Grid.InBounds(to)) return false;
        if (ship.DoorAt(to) is Door d) return !d.Removed && d.Openness > 0.5f;
        return ship.Grid.Kind(to) == TileKind.Floor;
    }

    /// <summary>러그가 덮은 칸 (위에서 바닥이 안 보인다).</summary>
    public bool UnderRug(Cell c)
    {
        if (!Any(c)) return false;
        foreach (var t in At(c)) if (t.Kind == ArticleKind.Rug && !t.Hung && !t.Stowed) return true;
        return false;
    }

    public Article? RugAt(Cell c)
    {
        if (!Any(c)) return null;
        foreach (var t in At(c)) if (t.Kind == ArticleKind.Rug && !t.Hung && !t.Stowed) return t;
        return null;
    }

    /// <summary>다 머금은 러그에서 아래로 스민다: 바닥이 젖고 그 아래 접속부까지 (위에서는 안 보인다).</summary>
    private readonly Dictionary<int, float> _under = new();
    public float UnderWet(Cell c) => _under.TryGetValue(Idx(c), out var v) ? v : 0f;

    private void Seep(Article rug, Cell c, float liters, Room room)
    {
        if (liters <= 0f) return;
        int i = Idx(c);
        float was = _under.TryGetValue(i, out var v) ? v : 0f;
        _under[i] = MathF.Min(1f, was + liters * 0.8f);
        if (was < 0.05f && _under[i] >= 0.05f) Stats.Seeps++;
        if (JunctionAt(c) is { } j) Wet(j, liters * 0.9f, "러그에서 스민 물");
    }

    private void UpdateUnder(float h)
    {
        if (_under.Count == 0) return;
        List<int>? dead = null;
        foreach (var i in _under.Keys.ToList())
        {
            var c = _w.Ship.Grid.CellAt(i);
            var rug = RugAt(c);
            float v = _under[i];
            if (rug == null)
            {
                // 걷었다: 젖은 바닥이 드러난다 (보이는 칸 상태로)
                if (v > 0.05f) _w.Body.RaiseMark(c, CellMark.Wet, MathF.Min(1f, 0.3f + v), "러그 아래 젖은 바닥");
                (dead ??= new()).Add(i);
                continue;
            }
            // 덮인 바닥은 거의 안 마른다 (러그가 덜 젖었으면 러그로 다시 빨려 올라간다)
            v -= (rug.WetFrac < 0.6f ? 0.6f : 0.05f) * h;
            if (v <= 0.01f) (dead ??= new()).Add(i); else _under[i] = v;
        }
        if (dead != null) foreach (var i in dead) _under.Remove(i);
    }

    // ───────────────────────────── 시작: 배마다 같은 규칙으로 물건을 놓는다 ─────────────────────────────

    private void Seed()
    {
        _seeded = true;
        var ship = _w.Ship;
        int rugs = 0, mats = 0;
        foreach (var room in ship.LiveRooms.OrderBy(r => r.Id))
        {
            var cells = room.Cells.Where(c => ship.IsOpenFloor(c) && ship.DoorAt(c) == null && !_w.Portable.Occupied(c) && !room.Doors.Any(d => (d.Cell.Center - c.Center).LengthSquared() < 2.1f))
                .OrderBy(c => (c.Center - room.Center).LengthSquared()).ThenBy(c => c.X * 1000 + c.Y).ToList();
            if (cells.Count < 4) continue;
            var edge = cells.AsEnumerable().Reverse().ToList(); // 벽 쪽
            int e = 0;
            Cell Edge() => edge[(e++ * 3) % edge.Count];
            switch (room.Kind)
            {
                case RoomType.Lounge or RoomType.Quarters or RoomType.Mess or RoomType.Chapel or RoomType.Meditation or RoomType.Theater or RoomType.PrivateCabins when rugs < 4:
                {
                    var at = cells[Math.Min(1, cells.Count - 1)];
                    Add(ArticleKind.Rug, at, "처음부터");
                    rugs++;
                    if (_w.Body.Under.Length > 0 && (_w.Body.Under[_w.Ship.Grid.Index(at)] & UnderFlags.Wiring) != 0) AddJunction(at);
                    if (room.Kind is RoomType.Quarters or RoomType.PrivateCabins) Add(ArticleKind.Towel, Edge(), "처음부터");
                    if (room.Kind is RoomType.Mess or RoomType.Lounge) Add(ArticleKind.Mug, Edge(), "처음부터");
                    break;
                }
                case RoomType.Laundry:
                    Add(ArticleKind.Towel, Edge(), "처음부터"); Add(ArticleKind.Towel, Edge(), "처음부터"); Add(ArticleKind.WaterJug, Edge(), "처음부터");
                    Add(ArticleKind.RubberMat, Edge(), "처음부터", stowed: true); // 물 쓰는 방엔 고무 매트
                    break;
                case RoomType.Galley:
                    Add(ArticleKind.Towel, Edge(), "처음부터"); Add(ArticleKind.GlassJar, Edge(), "처음부터"); Add(ArticleKind.Mug, Edge(), "처음부터");
                    Add(ArticleKind.PowderSack, Edge(), "처음부터"); Add(ArticleKind.FoodCrate, Edge(), "처음부터");
                    Add(ArticleKind.RubberMat, Edge(), "처음부터", stowed: true);
                    break;
                case RoomType.Medbay or RoomType.Lab or RoomType.AlgaeLab:
                    Add(ArticleKind.GlassJar, Edge(), "처음부터"); Add(ArticleKind.PaperStack, Edge(), "처음부터");
                    break;
                case RoomType.Bridge or RoomType.Archive or RoomType.Comms:
                    Add(ArticleKind.PaperStack, Edge(), "처음부터"); Add(ArticleKind.Radio, Edge(), "처음부터");
                    break;
                case RoomType.Workshop:
                    Add(ArticleKind.Toolbox, Edge(), "처음부터"); Add(ArticleKind.OilCan, Edge(), "처음부터"); Add(ArticleKind.PlasticCrate, Edge(), "처음부터");
                    Add(ArticleKind.RubberMat, Edge(), "처음부터", stowed: true);
                    break;
                case RoomType.Storage or RoomType.Cargo:
                    Add(ArticleKind.CardboardBox, Edge(), "처음부터"); Add(ArticleKind.PlasticCrate, Edge(), "처음부터");
                    if (mats < 4) { Add(ArticleKind.RubberMat, Edge(), "처음부터", stowed: true); mats++; }
                    break;
                case RoomType.Freezer:
                    Add(ArticleKind.IceBlock, Edge(), "처음부터"); Add(ArticleKind.FoodCrate, Edge(), "처음부터");
                    break;
                case RoomType.Power or RoomType.Substation or RoomType.BatteryRoom or RoomType.Engine or RoomType.ServerRoom when mats < 4:
                    Add(ArticleKind.RubberMat, Edge(), "처음부터", stowed: true); mats++;
                    break;
            }
            // 물이 지나는 방 바닥 아래 접속부 하나 (점검 뚜껑 자리)
            if ((_w.Body.Under.Length > 0) && room.Kind != RoomType.Corridor)
                foreach (var c in room.Cells)
                {
                    var u = _w.Body.Under[_w.Ship.Grid.Index(c)];
                    if ((u & UnderFlags.Pipe) != 0 && (u & UnderFlags.Wiring) != 0 && (u & UnderFlags.HatchSpot) != 0 && JunctionAt(c) == null) { AddJunction(c); break; }
                }
        }
        Stats.Seeded = Things.Count;
        Reindex();
    }

    public Junction AddJunction(Cell c)
    {
        var j = JunctionAt(c);
        if (j != null) return j;
        j = new Junction { At = c, Room = _w.Ship.RoomAt(c)?.Id ?? -1 };
        Junctions.Add(j);
        return j;
    }

    public Junction? JunctionAt(Cell c) { foreach (var j in Junctions) if (j.At == c) return j; return null; }

    // ───────────────────────────── 다른 시스템이 부르는 짧은 훅 ─────────────────────────────

    /// <summary>Fire: 그 칸에 놓인 물건이 불을 키우거나(마른 천 · 종이) 막는다(젖은 것).</summary>
    public float FuelMul(Cell c)
    {
        if (!Any(c)) return 1f;
        float m = 1f;
        foreach (var t in At(c))
        {
            float ign = Matter.Ignitability(t.Mat, t.WetFrac);
            if (ign > 0.1f) m *= 1f + 0.35f * MathF.Min(1f, ign);
            else if (t.WetFrac > 0.5f) m *= 0.8f;
        }
        return m;
    }

    /// <summary>Body.SlipAt: 바닥에 흩어진 종이 · 얼음 · 가루는 미끄럽다.</summary>
    public float SlipAdd(int i)
    {
        if (i < 0 || i >= _has.Length || !_has[i]) return 0f;
        float s = 0f;
        foreach (var t in Things)
        {
            if (t.CarriedBy >= 0 || _w.Ship.Grid.Index(t.At) != i || t.LastMoved < 0 && t.Mat != Material.Ice) continue; // 제자리의 서류 뭉치는 밟지 않는다 — 흩어진 뒤에야 미끄럽다
            s += t.Mat switch { Material.Ice => 0.4f, Material.Paper when t.Kind == ArticleKind.PaperStack => 0.2f, Material.Powder when t.Contents <= 0f => 0.15f, _ => 0f };
        }
        return s;
    }

    /// <summary>Body.FillPathCost: 통로 점유 (짐 · 상자 — 카트는 Portable이 비켜 가게 한다) · 보이는 전기 불꽃 칸.</summary>
    public void PathCost(int[] cost)
    {
        foreach (var t in Things)
        {
            if (t.CarriedBy >= 0 || t.Spec.Bulk <= 0 || !InAisle(t.At)) continue; // 벽 쪽에 둔 짐은 길을 막지 않는다
            int i = Idx(t.At);
            if (i >= 0) cost[i] += t.Spec.Bulk;
        }
        foreach (var (i, v) in _liveSeen) cost[i] += (int)(40 * v);
    }

    /// <summary>길목: 통로 · 문 앞 칸 (여기 내려놓은 짐은 동선을 바꾼다).</summary>
    public bool InAisle(Cell c)
    {
        var ship = _w.Ship;
        if (ship.RoomAt(c) is { Kind: RoomType.Corridor }) return true;
        foreach (var d in Cell.Dirs4) if (ship.DoorAt(c + d) != null) return true;
        return false;
    }

    /// <summary>Soil: 손이 닿는 곳 — 문 손잡이 · 공용 공구 (더러운 장갑 → 손잡이 → 다음 사람).
    /// 손때는 묻어 남고(재질 표의 머금음 — 금속 손잡이는 덜, 고무 손잡이는 더), 다음 손으로 옮는 건 균(Bio)이다 — 기름 · 그을음은 얼룩으로 남는다.</summary>
    public void Touch(CrewMember c, Soil s, float dt)
    {
        var w = _w;
        if (w.Ship.DoorAt(c.Cell) is Door d && !d.IsExternal)
        {
            EnsureHandles();
            float hold = Matter.Hold(Material.Metal) + 0.3f; // 쥐는 힘으로 눌러 묻는다
            int b = d.Id * Core.Soil.Kinds;
            for (int k = 0; k < Core.Soil.Kinds; k++) _handle[b + k] = MathF.Min(1f, _handle[b + k] + s.Hands[k] * 0.6f * hold * (1f - _handle[b + k]));
            float germ = _handle[b + (int)SoilKind.Bio];
            if (germ > 0.02f && s.Hands[(int)SoilKind.Bio] < germ) s.Hands[(int)SoilKind.Bio] = MathF.Min(1f, s.Hands[(int)SoilKind.Bio] + (germ - s.Hands[(int)SoilKind.Bio]) * 0.35f);
            Stats.HandleTouches++;
        }
        if (c.Pose == Pose.Working && c.Job?.Order is WorkOrder o && o.Kind is WorkKind.Repair or WorkKind.Maintain or WorkKind.PreventiveCheck or WorkKind.Fabricate && c.Room is Room room)
            foreach (var t in Things)
            {
                if (t.Kind != ArticleKind.Toolbox || t.CarriedBy >= 0 || w.Ship.RoomAt(t.At) != room) continue;
                float hold = Matter.Hold(t.Mat) + 0.15f; // 손잡이는 고무
                for (int k = 0; k < Core.Soil.Kinds; k++) t.Soil[k] = MathF.Min(1f, t.Soil[k] + s.Hands[k] * 0.2f * hold * dt * 6f * (1f - t.Soil[k]));
                float germ = t.Soil[(int)SoilKind.Bio];
                if (germ > 0.02f && s.Hands[(int)SoilKind.Bio] < germ) s.Hands[(int)SoilKind.Bio] = MathF.Min(1f, s.Hands[(int)SoilKind.Bio] + (germ - s.Hands[(int)SoilKind.Bio]) * 0.3f * dt * 6f);
                Stats.ToolTouches++;
                break;
            }
    }

    /// <summary>손잡이 · 공구의 손때는 천천히 옅어진다 (닦으면 바로).</summary>
    private void FadeSoil(float h)
    {
        for (int i = 0; i < _handle.Length; i++) _handle[i] = MathF.Max(0f, _handle[i] - 0.03f * h);
        foreach (var t in Things) if (t.Kind == ArticleKind.Toolbox) for (int k = 0; k < Core.Soil.Kinds; k++) t.Soil[k] = MathF.Max(0f, t.Soil[k] - 0.02f * h);
    }

    private float[] _handle = Array.Empty<float>();
    private void EnsureHandles() { int n = _w.Ship.Doors.Count * Core.Soil.Kinds; if (_handle.Length < n) Array.Resize(ref _handle, n); }
    public float HandleSoil(Door d, SoilKind k) => d.Id * Core.Soil.Kinds + (int)k < _handle.Length ? _handle[d.Id * Core.Soil.Kinds + (int)k] : 0f;
    public float HandleDirt(Door d) { float m = 0f; for (int k = 0; k < Core.Soil.Kinds; k++) m = MathF.Max(m, HandleSoil(d, (SoilKind)k)); return m; }
    internal void WipeHandle(Door d) { EnsureHandles(); for (int k = 0; k < Core.Soil.Kinds; k++) _handle[d.Id * Core.Soil.Kinds + k] *= 0.1f; Stats.HandleWipes++; }

    /// <summary>Blast: 압력파가 물건을 민다 · 표대로 깨지고 흩날린다 (같은 Push 함수).</summary>
    public void OnBlast(Cell at, Func<Cell, float> pAt, string cause)
    {
        foreach (var t in Things.ToList())
        {
            if (t.CarriedBy >= 0) continue;
            float p = pAt(t.At);
            if (p < 0.04f) continue;
            var away = t.At.Center + t.Off - at.Center;
            var dir = away.LengthSquared() < 0.01f ? new Vector2(MathF.Cos(t.Id * 2.1f), MathF.Sin(t.Id * 2.1f)) : Vector2.Normalize(away);
            var r = Matter.React(t.Mat, Element.Pressure);
            // 충격파 자체: 유리 · 사기는 산산조각, 금 가는 것은 금
            if (r == Reaction.Shatter) Impact(t, t.Spec.Tough * 30f * p, cause);
            else if (r == Reaction.Crack) Impact(t, t.Spec.Tough * 6f * p, cause);
            else if (r == Reaction.Scatter && t.Contents > 0f && p > 0.15f) Burst(t, cause); // 가루 포대가 터진다
            if (t.Stage == BreakStage.Shards) continue; // 산산조각 난 자리에 조각이 흩어진다
            PushThing(t, 70f * p * (r == Reaction.Scatter ? 1.5f : 1f), dir, cause);
        }
    }

    // ───────────────────────────── 지문 ─────────────────────────────

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Things.Count);
        foreach (var t in Things) { I(t.At.X * 1000 + t.At.Y); I((int)t.Stage + (t.Smolder ? 8 : 0) + (t.Ruined ? 16 : 0) + (t.CarriedBy >= 0 ? 32 : 0)); F(t.Water); F(t.Char); F(t.Melt); }
        I(Spills.Count); foreach (var (i, s) in Spills) { I(i); F(s.Liters); }
        I(Junctions.Count); foreach (var j in Junctions) { F(j.Wet); I((j.Known ? 1 : 0) + (j.Taped ? 2 : 0)); }
        I(_dust.Count);
        var s0 = Stats; I(s0.Pushed); I(s0.Shattered); I(s0.Smolders); I(s0.SmokeAlarms); I(s0.DoorSeals); I(s0.MatsLaid); I(s0.LiveShocks); I(s0.Sparks); I(s0.ComputerWarns); I(s0.HandleTouches); I(s0.DustBlasts);
    }
}
