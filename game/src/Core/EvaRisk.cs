using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v16.11 선외 작업의 위험: 선체 밖의 사람은 체력 하나가 아니라 우주복 · 생명줄 · 산소 · 추진팩 · 무전으로 버틴다.
//   우주복: 긁힘 → 찢김 → 미세 누출 → 큰 파공 (응급 패치로 버티거나 에어락까지 가야 한다 — 못 하면 진공이 죽인다) · 바이저 금 → 깨짐.
//   생명줄: 파편 · 추진팩 오작동이 줄을 끊으면 몸이 빙글빙글 돌며 멀어진다 → 추진팩으로 돌아오기 / 동료 구조 EVA / 견인 드론 / 실종(무전이 약해진다).
//   두 줄 관행: 한 번 떠내려간 일을 겪은 배는 생명줄을 두 줄 건다 (한 줄이 끊겨도 산다).
//   무전: 들은 사람만 안다 (선체 밖 우주복 · 함교 · 통신실 · 에어락 인터컴 · 지휘자의 손 무전기). 멀어지면 지직거리다 끊긴다.
//   운석 경보: 에어락까지 갈 시간이 되나 → 아니면 선체 그늘에 붙는다 → 거의 다 했으면 마저 끝내기도 한다. 주 컴퓨터(궤적) · 지휘자(무전)가 일러 준다.
//   주 컴퓨터: 우주복 원격 측정(산소 · 생명줄 장력 · 운석 도착 시간 · 에어락까지 거리)을 읽고 "들어오라" 경고 · 드론 회수/구조 EVA 제안.
//   놓친 공구 · 깨진 바이저 조각 · 드론 잔해는 같은 규칙으로 떠다닌다 (건지면 부품 · 고철 · 주인에게).
//   돌아오면 에어락에서 우주복을 점검하고(모르던 긁힘을 찾는다), 상한 우주복은 보관함에서 수리를 기다린다 (재료가 없으면 다음 사람이 상한 걸 입는다).
//   겪은 사람은 선외 작업이 무서워진다 → 거부 → 정비가 밀린다. 구해 준 사람은 잊지 않는다.

public enum SuitBreach { None, Scratch, Tear, MicroLeak, Puncture }
public enum EvaPart { Helmet, Torso, LeftArm, RightArm, LeftLeg, RightLeg, Pack, Tether }
public enum EvaPlan { Work, ToAirlock, Shelter, Finish }
public enum DriftMode { Tumble, Stabilize, Brake, Return, Wait }
public enum FloatKind { Wrench, BoltBag, Torch, Cutter, Lamp, Mirror, Plate, Motor, Board, Cell, Rotor, Shard, Scrap, Tape }
public enum RadioTone { Chat, Hurt, Mayday, Command, Computer, Last }

/// <summary>우주복 한 벌의 상처 — 입은 사람이 바뀌어도 그 우주복을 따라간다 (보관함에 걸면 수리 목록에).</summary>
public sealed class SuitWear
{
    public SuitBreach Breach { get; set; }
    public EvaPart BreachPart { get; set; } = EvaPart.Torso;
    /// <summary>긁힘 0~1 (다음 상처의 바탕 — 점검 전엔 입은 사람도 모른다).</summary>
    public float Scuff { get; set; }
    public int Scratches { get; set; }
    /// <summary>바이저: 0 멀쩡 · 0.35 금 · 1 깨짐.</summary>
    public float Visor { get; set; }
    /// <summary>깨진 바이저 위로 해가리개를 내리고 테이프로 감았다.</summary>
    public bool VisorShield { get; set; }
    public int PatchKit { get; set; } = 2;
    public int Patches { get; set; }
    /// <summary>지금 상처를 패치가 막고 있다.</summary>
    public bool Patched { get; set; }
    /// <summary>추진팩 연료 0~1.</summary>
    public float Fuel { get; set; } = 1f;
    /// <summary>입은 사람이 상처를 안다 (긁힘은 점검 전엔 모른다).</summary>
    public bool Known { get; set; }
    /// <summary>상처 전에 새던 정도 (보관함 밸브 · v11.0).</summary>
    public float BaseLeak { get; set; }
    public List<string> Notes { get; } = new();

    public bool Damaged => Breach != SuitBreach.None || Visor > 0.05f || Scuff > 0.12f || Patches > 0 || PatchKit < 2 || Fuel < 0.4f;
    public bool Leaking => Breach >= SuitBreach.MicroLeak && !Patched || Visor >= 1f && !VisorShield;

    public SuitWear Copy()
    {
        var x = new SuitWear
        {
            Breach = Breach, BreachPart = BreachPart, Scuff = Scuff, Scratches = Scratches, Visor = Visor, VisorShield = VisorShield,
            PatchKit = PatchKit, Patches = Patches, Patched = Patched, Fuel = Fuel, Known = true,
        };
        x.Notes.AddRange(Notes);
        return x;
    }

    public string Describe()
    {
        var parts = new List<string>();
        if (Breach >= SuitBreach.Tear) parts.Add($"{EvaRiskSystem.PartName(BreachPart)} {EvaRiskSystem.BreachName(Breach)}" + (Patched ? " (패치)" : ""));
        if (Scratches > 0) parts.Add($"긁힘 {Scratches}곳");
        if (Visor >= 1f) parts.Add(VisorShield ? "바이저 깨짐 (해가리개로 막음)" : "바이저 깨짐");
        else if (Visor >= 0.35f) parts.Add("바이저 금");
        else if (Visor > 0.05f) parts.Add("바이저 잔금");
        if (PatchKit < 2) parts.Add($"패치 {2 - PatchKit}장 씀");
        if (Fuel < 0.4f) parts.Add($"추진팩 연료 {Fuel * 100:0}%");
        return parts.Count == 0 ? "이상 없음" : string.Join(" · ", parts);
    }
}

/// <summary>보관함에 걸린, 수리를 기다리는 우주복.</summary>
public sealed class StoredSuit
{
    public int Id { get; init; }
    public int LockerId { get; init; }
    public SuitWear Wear { get; init; } = new();
    public long Since { get; init; }
    public string LastWearer { get; init; } = "";
    public int MenderId { get; set; } = -1;
}

/// <summary>선체 밖에 나간 사람 한 명의 선외 상태 (밖에 있는 동안 · 떠내려간 몸은 찾을 때까지).</summary>
public sealed class EvaPerson
{
    public int Id { get; init; }
    public long Since { get; init; }
    /// <summary>건 생명줄 (두 줄 관행이면 2 · 튕겨 나갔으면 0).</summary>
    public int Tethers { get; set; } = 1;
    public int Cut { get; set; }
    public long CutAt { get; set; } = -1;
    public Vector2 CutDir { get; set; }
    public bool Lines => Tethers - Cut > 0;
    public bool Adrift { get; set; }
    /// <summary>표류 속도 (칸/시간).</summary>
    public Vector2 Vel { get; set; }
    public float Spin { get; set; }
    /// <summary>몸이 도는 빠르기 (rad/시간).</summary>
    public float SpinRate { get; set; }
    public long AdriftSince { get; set; } = -1;
    public DriftMode Mode { get; set; }
    public float Signal { get; set; } = 1f;
    public bool Missing { get; set; }
    public bool Lost { get; set; }
    public Vector2 LastHeard { get; set; }
    public EvaPlan Plan { get; set; }
    public int PlanFor { get; set; } = -1;
    public Cell? ShelterAt { get; set; }
    public float Thrust { get; set; }
    public Vector2 ThrustDir { get; set; }
    public int RescuerId { get; set; } = -1;
    public int AssignedId { get; set; } = -1;
    public bool DroneOrdered { get; set; }
    public int TowedBy { get; set; } = -1;
    public int TowDrone { get; set; } = -1;
    public long PatchStart { get; set; } = -1;
    public int PatchBy { get; set; } = -1;
    public float Panic { get; set; }
    public long HurryUntil { get; set; } = -1;
    public long HitAt { get; set; } = -1;
    public EvaPart HitPart { get; set; }
    public long JetStuckUntil { get; set; } = -1;
    public bool RescueLine { get; set; }
    public bool Rescuer { get; set; }
    public HashSet<int> KnownBy { get; } = new();
    public long LastCall { get; set; } = -1;
    public long LastAdvice { get; set; } = -1;
    public string? Advice { get; set; }
    public EvaPlan AdvicePlan { get; set; }
    public int AdviceFor { get; set; } = -1;
    public bool AdviceComputer { get; set; }
    public bool Ignored { get; set; }
    public List<(ItemKind kind, int count)> Pocket { get; } = new();
    public bool Incident { get; set; }
    public bool DeathNoted { get; set; }
    public int SavedBy { get; set; } = -1;
    public bool LastWords { get; set; }
    public bool Announced { get; set; }
    public long OffHullSince { get; set; } = -1;
    public float StartFuel { get; set; } = 1f;
}

/// <summary>떠다니는 것 하나: 놓친 공구 · 드론 잔해 · 바이저 조각 (같은 규칙으로 떠다니고 · 건지고 · 잃는다).</summary>
public sealed class Floater
{
    public int Id { get; init; }
    public FloatKind Kind { get; init; }
    public string Name { get; init; } = "";
    public Vector2 Pos { get; set; }
    public Vector2 Prev { get; set; }
    public Vector2 Vel { get; set; }
    public float Angle { get; set; }
    public float SpinRate { get; set; }
    public long Since { get; init; }
    public (ItemKind kind, int count)[] Yield { get; init; } = Array.Empty<(ItemKind, int)>();
    public int Owner { get; init; } = -1;
    public int FromDrone { get; init; } = -1;
    public int Belonging { get; init; } = -1;
    public float Heat { get; set; }
    public int ClaimedBy { get; set; } = -1;
    public int CarriedBy { get; set; } = -1;
    public bool Lost { get; set; }
    public bool Tool => Kind <= FloatKind.Mirror;
}

/// <summary>무전 한 마디 — 들은 사람만 안다.</summary>
public sealed class RadioCall
{
    public int Id { get; init; }
    public long Tick { get; init; }
    public int From { get; init; } = -1;
    public string Who { get; init; } = "";
    public string Text { get; init; } = "";
    public float Signal { get; init; } = 1f;
    public RadioTone Tone { get; init; }
    public Vector2 At { get; init; }
    public List<int> Heard { get; } = new();
    public bool ComputerHeard { get; set; }
}

/// <summary>두 줄 관행 (생명줄을 두 줄 건다) — 생긴 까닭과 따르는 사람.</summary>
public sealed class TwoLineCustom
{
    public long Born { get; init; }
    public string Origin { get; init; } = "";
    public string Founder { get; init; } = "";
    public HashSet<int> Followers { get; } = new();
    public int Saves { get; set; }
}

public sealed class EvaStats
{
    public int Hits, Breaches, Patches, PatchFails, BuddyPatches, VisorCracks, VisorBreaks, TetherCuts, TwoLineSaves, Adrifts, SelfReturns, CrewRescues, DroneRescues,
        Missing, Deaths, BodiesRecovered, Shelters, FinishUnder, ToAirlock, Refusals, ToolsLost, ToolsCaught, ToolsGone, SuitChecks, SuitsMended, DamagedIssued,
        Calls, Heard, Unheard, Warnings, Proposals, Lookouts, NearMisses, Waits, Received;

    public string Summary() =>
        $"맞음 {Hits} · 우주복 누출 {Breaches}(패치 {Patches} · 실패 {PatchFails} · 동료 패치 {BuddyPatches}) · 바이저 금 {VisorCracks}/깨짐 {VisorBreaks} · 생명줄 끊김 {TetherCuts}(두 줄로 산 {TwoLineSaves}) · " +
        $"표류 {Adrifts}(스스로 {SelfReturns} · 동료 구조 {CrewRescues} · 드론 {DroneRescues} · 실종 {Missing}) · 선외 사망 {Deaths}(시신 회수 {BodiesRecovered}) · 그늘 {Shelters} · 마저 끝냄 {FinishUnder} · " +
        $"EVA 거부 {Refusals} · 공구 놓침 {ToolsLost}(붙잡음 {ToolsCaught} · 잃음 {ToolsGone}) · 점검 {SuitChecks} · 수리 {SuitsMended} · 무전 {Calls}(들음 {Heard} · 아무도 못 들음 {Unheard}) · 컴퓨터 경고 {Warnings} · 제안 {Proposals}";
}

public sealed partial class EvaRiskSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7393 + 811));

    public const float RadioRange = 40f;
    public const float RescueLineLength = 22f;
    public const float FuelPerCell = 0.007f;
    public const float FuelPerDv = 1f / 230f;

    public List<EvaPerson> People { get; } = new();
    public List<Floater> Floaters { get; } = new();
    public List<RadioCall> Radio { get; } = new();
    public List<StoredSuit> DamagedSuits { get; } = new();
    public EvaStats Stats { get; } = new();
    public TwoLineCustom? TwoLines { get; private set; }
    /// <summary>그림: 최근에 맞은 자리 (번쩍).</summary>
    public List<(long tick, Vector2 at, float power, bool suit)> Flashes { get; } = new();

    private readonly Dictionary<int, float> _dread = new();
    private readonly Dictionary<int, long> _refusedAt = new();
    private readonly Dictionary<int, long> _heardAt = new();
    private int _nextFloat = 1, _nextCall = 1, _nextSuit = 1;
    private long _nextWatch;
    internal bool PlanningRescue;

    public EvaRiskSystem(World w) => _w = w;

    // ─────────────────────────────── 이름 ───────────────────────────────

    public static string PartName(EvaPart p) => p switch
    {
        EvaPart.Helmet => "헬멧", EvaPart.Torso => "몸통", EvaPart.LeftArm => "왼팔", EvaPart.RightArm => "오른팔",
        EvaPart.LeftLeg => "왼다리", EvaPart.RightLeg => "오른다리", EvaPart.Pack => "등짐(산소통·추진팩)", _ => "생명줄",
    };

    public static string BreachName(SuitBreach b) => b switch
    {
        SuitBreach.Scratch => "긁힘", SuitBreach.Tear => "찢김", SuitBreach.MicroLeak => "미세 누출", SuitBreach.Puncture => "큰 파공", _ => "멀쩡",
    };

    public static string FloatName(FloatKind k) => k switch
    {
        FloatKind.Wrench => "토크 렌치", FloatKind.BoltBag => "볼트 주머니", FloatKind.Torch => "용접 토치", FloatKind.Cutter => "케이블 커터",
        FloatKind.Lamp => "손전등", FloatKind.Mirror => "점검 거울", FloatKind.Plate => "외피 판", FloatKind.Motor => "추진기 모터",
        FloatKind.Board => "회로판", FloatKind.Cell => "배터리 셀", FloatKind.Rotor => "날개", FloatKind.Shard => "바이저 조각",
        FloatKind.Scrap => "고철", _ => "패치 조각",
    };

    public static string PlanName(EvaPlan p) => p switch
    {
        EvaPlan.ToAirlock => "에어락으로", EvaPlan.Shelter => "선체 그늘에 숨기", EvaPlan.Finish => "마저 끝내기", _ => "작업",
    };

    // ─────────────────────────────── 찾기 ───────────────────────────────

    public EvaPerson? Of(CrewMember c)
    {
        foreach (var p in People) if (p.Id == c.Id) return p;
        return null;
    }

    public EvaPerson? Of(int id)
    {
        foreach (var p in People) if (p.Id == id) return p;
        return null;
    }

    private CrewMember? Crew(int id)
    {
        foreach (var c in _w.Crew) if (c.Id == id) return c;
        return null;
    }

    public float Dread(CrewMember c) => _dread.TryGetValue(c.Id, out var d) ? d : 0f;
    public bool RefusedRecently(CrewMember c) => _refusedAt.TryGetValue(c.Id, out var t) && _w.Tick - t < SimTime.Hours(1);
    public bool HeardRecently(CrewMember c) => _heardAt.TryGetValue(c.Id, out var t) && _w.Tick - t < SimTime.Minutes(3);

    public void AddDread(CrewMember c, float amount)
    {
        if (c.Dead || c.IsChild) return;
        amount *= amount > 0f ? 1.2f - 0.6f * c.Traits.Bravery : 1f;
        _dread[c.Id] = Math.Clamp(Dread(c) + amount, 0f, 1f);
    }

    /// <summary>외부 해치 바깥 칸 (선체 밖에서 해치에 닿는 우주 칸).</summary>
    public static Cell? HatchOuter(World w)
    {
        var h = DroneSystem.Hatch(w);
        if (h == null) return null;
        foreach (var d in Cell.Dirs4)
            if (w.Ship.Grid.Kind(h.Cell + d) == TileKind.Void) return h.Cell + d;
        return null;
    }

    public static Cell? HatchInner(World w)
    {
        var h = DroneSystem.Hatch(w);
        if (h == null) return null;
        foreach (var d in Cell.Dirs4)
            if (w.Ship.RoomAt(h.Cell + d) != null && w.Ship.IsWalkable(h.Cell + d)) return h.Cell + d;
        return null;
    }

    private Vector2 Anchor => HatchOuter(_w)?.Center ?? _w.Structure.ShipCenter;

    private (float x0, float y0, float x1, float y1) _box;
    private long _boxAt = -1;

    /// <summary>선체에서 떨어진 거리 (선체를 감싼 상자 밖으로 — 선체 곁이면 0).</summary>
    public float FromHull(Vector2 at)
    {
        var w = _w;
        if (_boxAt < 0 || w.Tick - _boxAt > SimTime.Hours(1))
        {
            _boxAt = w.Tick;
            var g = w.Ship.Grid;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
            for (int i = 0; i < g.CellCount; i++)
            {
                var c = g.CellAt(i);
                if (g.Kind(c) == TileKind.Void) continue;
                x0 = Math.Min(x0, c.X); y0 = Math.Min(y0, c.Y); x1 = Math.Max(x1, c.X + 1); y1 = Math.Max(y1, c.Y + 1);
            }
            _box = x0 == int.MaxValue ? (0f, 0f, g.Width, g.Height) : (x0, y0, x1, y1);
        }
        float dx = MathF.Max(0f, MathF.Max(_box.x0 - at.X, at.X - _box.x1));
        float dy = MathF.Max(0f, MathF.Max(_box.y0 - at.Y, at.Y - _box.y1));
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>무전 세기: 선체(안테나 중계)에서 멀수록 · 안테나가 상할수록 · 태양 폭풍이면 약하다.</summary>
    public float SignalAt(Vector2 at)
    {
        float range = RadioRange * _w.Exterior.AntennaFactor;
        float s = Math.Clamp(1f - FromHull(at) / MathF.Max(1f, range), 0f, 1f);
        if (_w.Hazards.StormActive) s *= 0.55f;
        return s;
    }

    /// <summary>a에서 b까지 사이에 선체(우주가 아닌 칸)가 있으면 가려진다 — 사람 · 드론 · 떠다니는 것이 다 같은 규칙.</summary>
    public bool LineBlocked(Vector2 a, Vector2 b)
    {
        var grid = _w.Ship.Grid;
        var d = b - a;
        float len = d.Length();
        if (len < 1.2f) return false;
        var dir = d / len;
        for (float t = 0.9f; t < len - 0.45f; t += 0.3f)
        {
            var c = Cell.FromPosition(a + dir * t);
            if (grid.InBounds(c) && grid.Kind(c) != TileKind.Void) return true;
        }
        return false;
    }

    private bool HugsHull(Vector2 pos)
    {
        var grid = _w.Ship.Grid;
        var c = Cell.FromPosition(pos);
        foreach (var d in Cell.Dirs4)
            if (grid.InBounds(c + d) && grid.Kind(c + d) != TileKind.Void) return true;
        return false;
    }

    /// <summary>파편이 이 자리에 얼마나 닿나: 선체 그늘이면 거의 안 · 선체에 몸을 붙였으면 덜.</summary>
    public float Exposure(Vector2 pos, Vector2 from, bool braced)
    {
        if (LineBlocked(from, pos)) return 0.08f;
        if (HugsHull(pos)) return braced ? 0.5f : 0.85f;
        return 1f;
    }

    public bool OnHull(CrewMember c) => _w.Paths.IsSpace(c.Cell) || _w.Ship.Grid.InBounds(c.Cell) && _w.Ship.Grid.Kind(c.Cell) != TileKind.Void;

    // ─────────────────────────────── 매 틱: 몸 · 떠다니는 것 ───────────────────────────────

    public void Step()
    {
        var w = _w;
        const float hr = 1f / SimTime.TicksPerHour;
        foreach (var c in w.Crew)
            if (c.Outside && !c.Dead && c.Aboard == null && Of(c) == null) Begin(c);
        for (int i = 0; i < People.Count; i++)
        {
            var p = People[i];
            var c = Crew(p.Id);
            if (c == null) { People.RemoveAt(i--); continue; }
            p.Thrust *= 0.85f;
            if (!c.Outside && !p.Adrift && c.Aboard == null)
            {
                if (c.CarriedBy != null && c.CarriedBy.Outside) continue;
                End(c, p);
                People.RemoveAt(i--);
                continue;
            }
            if (!p.Adrift || p.Lost) continue;
            if (p.TowedBy >= 0)
            {
                if (c.CarriedBy == null || c.CarriedBy.Id != p.TowedBy) p.TowedBy = -1;
                p.Spin += p.SpinRate * 0.1f * hr;
                continue;
            }
            if (p.TowDrone >= 0) { p.Spin += p.SpinRate * 0.2f * hr; continue; } // 드론이 붙잡아 옮긴다 (DroneDamage)
            if (c.CarriedBy != null) continue;
            c.PreviousPosition = c.Position;
            c.Position += p.Vel * hr;
            p.Spin += p.SpinRate * hr;
            if (p.JetStuckUntil > w.Tick) p.SpinRate += MathF.Sign(p.SpinRate == 0f ? 1f : p.SpinRate) * 1.5f; // 열린 채 걸린 밸브가 몸을 계속 돌린다
            if ((c.Position - w.Structure.ShipCenter).Length() > StructureSystem.LostRange + 30f)
            {
                p.Lost = true;
                if (!p.Missing) { p.Missing = true; Stats.Missing++; } // 통합7 안테나가 좋은 배(한빛호)는 무전이 닿는 채로 시야 밖에 이른다 — 그때도 실종이다 (실종 수에 안 들어가던 것)
                w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.IGa(c.Name)} 시야 밖으로 사라졌다 — 다시 찾을 길이 없다", c.Id);
                w.History.Add(w, HistoryKind.Casualty, $"{Ko.IGa(c.Name)} 별 사이로 사라졌다 — {(c.Dead ? "시신을 찾지 못했다" : "아직 숨이 있었다")}", null, new[] { c });
            }
        }
        // 떠다니는 것
        for (int i = 0; i < Floaters.Count; i++)
        {
            var f = Floaters[i];
            f.Prev = f.Pos;
            if (f.CarriedBy >= 0)
            {
                var d = w.Drones.Drones.FirstOrDefault(x => x.Id == f.CarriedBy);
                if (d == null || d.State == DroneState.Lost) { f.CarriedBy = -1; continue; }
                f.Pos = d.Position + new Vector2(0.25f, 0.3f);
                f.Angle += 0.4f * hr * 60f;
                if (d.State == DroneState.Docked) { Deliver(f, d); Floaters.RemoveAt(i--); }
                continue;
            }
            f.Pos += f.Vel * hr;
            f.Angle += f.SpinRate * hr;
            if (f.Heat > 0f) f.Heat = MathF.Max(0f, f.Heat - 2.5f * hr);
            if ((f.Pos - w.Structure.ShipCenter).Length() > StructureSystem.LostRange + 20f)
            {
                Gone(f);
                Floaters.RemoveAt(i--);
            }
        }
        if (Floaters.Count > 0) GrabNearby();
        // 경보가 막 울렸다: 선체 밖 사람은 그 자리에서 판단한다 (다음 시스템 틱을 기다리지 않는다)
        if (People.Count > 0 && w.Sensors.Alarm is IncomingMeteor al && al.Id != _alarmSeen)
        {
            _alarmSeen = al.Id;
            ComputerWatch(); // 주 컴퓨터 · 지휘자가 먼저 일러 준다 (궤적 · 에어락 거리)
            CommanderWatch();
            _nextWatch = w.Tick + SimTime.Minutes(1);
            foreach (var p in People.ToList())
                if (Crew(p.Id) is CrewMember c && c.CanAct && !p.Adrift && p.PlanFor != al.Id && al.MinutesLeft(w.Tick) > 0f) Decide(c, p, al);
        }
    }

    private int _alarmSeen = -1;

    /// <summary>선체 밖에 나왔다: 생명줄을 건다 (두 줄 관행이면 둘).</summary>
    private EvaPerson Begin(CrewMember c)
    {
        var w = _w;
        bool viaHatch = c.EvaMode || c.Job?.Activity is EvaRescueActivity;
        var p = new EvaPerson { Id = c.Id, Since = w.Tick, StartFuel = c.Suit?.Wear.Fuel ?? 0f };
        if (!viaHatch) p.Tethers = 0;
        else
        {
            bool two = c.Traits.Diligence > 0.82f;
            if (TwoLines is TwoLineCustom tl)
            {
                if (!tl.Followers.Contains(c.Id))
                {
                    var teller = w.Crew.FirstOrDefault(o => o != c && !o.Dead && tl.Followers.Contains(o.Id) && (o.Position - c.Position).LengthSquared() < 25f);
                    if (teller != null || R.Chance(0.35f))
                    {
                        tl.Followers.Add(c.Id);
                        w.Log.Add(w.Tick, LogKind.Life, teller != null ? $"{Ko.IGa(teller.Name)} 생명줄은 두 줄이라고 일러 줬다 — {tl.Origin}" : "다들 두 줄을 거는 걸 보고 따라 건다", c.Id);
                    }
                }
                two |= tl.Followers.Contains(c.Id);
            }
            p.Tethers = two ? 2 : 1;
        }
        People.Add(p);
        return p;
    }

    /// <summary>에어락으로 돌아왔다: 붙잡아 온 것을 내려놓고 · 우주복을 점검하고 · 겪은 일을 남긴다.</summary>
    private void End(CrewMember c, EvaPerson p)
    {
        var w = _w;
        if (p.Pocket.Count > 0)
        {
            var box = w.Ship.Containers.Where(f => f.Storage!.Free > 0).OrderBy(f => (f.Center - c.Position).LengthSquared()).FirstOrDefault();
            if (box != null)
            {
                foreach (var (k, n) in p.Pocket) box.Storage!.Add(k, n);
                w.Log.Add(w.Tick, LogKind.Work, $"선체 밖에서 붙잡아 온 것을 {box.Room.Name}에 내려놓았다 — {string.Join(" · ", p.Pocket.Select(x => $"{ItemKinds.Name(x.kind)} {x.count}"))}", c.Id);
            }
        }
        if (c.Suit is SuitState s && !c.Dead)
        {
            var wv = s.Wear;
            bool hidden = !wv.Known && wv.Scuff > 0.12f;
            if (wv.Damaged || p.Incident)
            {
                Stats.SuitChecks++;
                wv.Known = true;
                w.Log.Add(w.Tick, LogKind.Work, $"에어락에서 우주복 점검 — {wv.Describe()}" + (hidden ? " (밖에서는 몰랐던 긁힘이다)" : ""), c.Id);
                MarkLog.Add(c.Memory.Marks, w.Tick, $"우주복 점검: {wv.Describe()}");
            }
        }
        if (p.Incident && !c.Dead)
        {
            if (p.AdriftSince >= 0) Life.Diary(w, c, Persona.Say(c, "에어락 문이 닫히는 소리가 그렇게 좋은 줄 몰랐다"));
            else if (p.HitAt >= 0) Life.Diary(w, c, Persona.Say(c, "선체 밖에서 파편을 맞았다. 헬멧 안에서 내 숨소리만 들렸다"));
        }
        else if (!c.Dead && w.Tick - p.Since > SimTime.Minutes(20)) AddDread(c, -0.03f); // 아무 일 없이 다녀오면 조금 덜 무섭다
        // 에어락 앞에서 기다리던 사람 (의무관 · 가까운 사람)
        if (p.Incident && !c.Dead)
            foreach (var o in w.Crew)
            {
                if (o == c || o.Dead || !o.CanAct || o.Job?.Activity is not EvaRescueActivity || o.Job.Label != "에어락 마중" || (o.Position - c.Position).LengthSquared() > 25f) continue;
                Stats.Received++;
                c.Vitals.Health = MathF.Min(c.Vitals.MaxHealth, c.Vitals.Health + (o.Role == CrewRole.Medic ? 0.06f : 0.02f));
                c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.08f);
                c.ChangeAffinity(o, 0.05f);
                Life.Diary(w, c, Persona.Say(c, $"{Ko.IGa(o.Name)} 에어락 앞에서 기다리고 있었다"));
                w.Log.Add(w.Tick, LogKind.Life, $"에어락 앞에서 {Ko.EulReul(c.Name)} 맞았다" + (o.Role == CrewRole.Medic ? " — 바로 응급 처치" : ""), o.Id);
                break;
            }
        // 컴퓨터가 일러 준 것을 따랐는데 무사히 들어왔다
        if (p.AdviceComputer && !p.Ignored && p.Advice != null && !c.Dead) w.Automation.Trusts.Change(c, 0.02f, "선외에서 컴퓨터가 들어오라고 해서 들어왔다", quiet: true);
    }

    // ─────────────────────────────── 시스템 틱 ───────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        UpdateSuits(dt);
        if (People.Count > 0) UpdatePeople(dt);
        if (w.Tick >= _nextWatch)
        {
            _nextWatch = w.Tick + SimTime.Minutes(1);
            if (People.Count > 0) { ComputerWatch(); CommanderWatch(); }
            Lookouts();
        }
        // 무서움은 천천히 가라앉는다
        if (_dread.Count > 0)
            foreach (var id in _dread.Keys.OrderBy(k => k).ToList())
                _dread[id] = MathF.Max(0f, _dread[id] - 0.004f * dt);
        if (Flashes.Count > 0) Flashes.RemoveAll(f => w.Tick - f.tick > SimTime.Minutes(2));
        w.Drones.HurtUpdate(dt);
    }

    /// <summary>우주복이 새는 정도 = 밸브 × 상처 × 바이저 × 숨 (겁에 질리면 숨이 가쁘다).</summary>
    public static float LeakMul(SuitWear wv) =>
        (wv.Breach switch
        {
            SuitBreach.Tear => 1.12f,
            SuitBreach.MicroLeak => wv.Patched ? 1.3f : 7f,
            SuitBreach.Puncture => wv.Patched ? 2.6f : 70f,
            _ => 1f,
        }) * (wv.Visor >= 1f ? (wv.VisorShield ? 14f : 120f) : wv.Visor >= 0.35f ? 1.25f : 1f);

    private void UpdateSuits(float dt)
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Suit is not SuitState s) continue;
            var wv = s.Wear;
            if (wv.BaseLeak <= 0f) wv.BaseLeak = s.Leak;
            var p = c.Outside ? Of(c) : null;
            float breath = 1f + 0.6f * (p?.Panic ?? 0f);
            if (p is { Adrift: true, Mode: DriftMode.Wait } && c.Traits.Calm > 0.5f) breath *= 0.85f; // 숨을 아낀다
            s.Leak = wv.BaseLeak * LeakMul(wv) * breath;
            if (!c.Outside || c.Dead || p == null) continue;
            // 패치가 큰 구멍을 오래 막지는 못한다
            if (wv.Patched && wv.Breach == SuitBreach.Puncture && R.Chance(0.12f * dt))
            {
                wv.Patched = false;
                w.Log.Add(w.Tick, LogKind.Warning, $"{PartName(wv.BreachPart)} 패치가 들떴다 — 다시 샌다", c.Id);
                if (c.CanAct) Say(c, "패치가 떨어졌어 — 다시 샌다!", RadioTone.Hurt, c.Id);
                c.Interrupt(w);
            }
            // 새면 겁이 난다 (침착한 사람은 덜)
            if (wv.Leaking && c.CanAct) p.Panic = MathF.Min(1f, p.Panic + (0.9f - 0.7f * c.Traits.Calm) * dt * 3f);
            else p.Panic = MathF.Max(0f, p.Panic - 0.8f * dt);
            // 산소가 끝났는데 구멍이 나 있다: 진공이 몸을 상하게 한다
            if (s.Oxygen <= 0f && (wv.Breach >= SuitBreach.MicroLeak || wv.Visor >= 1f))
            {
                c.Vitals.Health = MathF.Max(0f, c.Vitals.Health - 2.4f * dt);
                NeedsSystem.AddInjury(c.Vitals, 0.25f * dt, "감압 (우주복 파공)");
                c.Vitals.InjuryCause = "감압 (우주복 파공)";
            }
            // 마지막 무전
            if (!p.LastWords && c.CanAct && s.Oxygen > 0f && s.Oxygen / MathF.Max(0.01f, s.Leak) < 4f / 60f && (wv.Leaking || p.Adrift))
            {
                p.LastWords = true;
                string last = p.Adrift ? "…산소가 다 됐어. 다들… 고마웠어" : "…에어락이 안 보여… 미안해";
                Say(c, last, RadioTone.Last, c.Id);
            }
        }
    }

    private void UpdatePeople(float dt)
    {
        var w = _w;
        var alarm = w.Sensors.Alarm;
        foreach (var p in People.ToList())
        {
            var c = Crew(p.Id);
            if (c == null) continue;
            p.Signal = SignalAt(c.Position);
            if (p.Signal > 0.04f) p.LastHeard = c.Position;
            // 죽었다
            if (c.Dead)
            {
                if (!p.DeathNoted)
                {
                    p.DeathNoted = true;
                    Stats.Deaths++;
                    foreach (var o in w.Crew)
                        if (!o.Dead && p.KnownBy.Contains(o.Id))
                        {
                            AddDread(o, 0.2f + 0.2f * MathF.Max(0f, o.AffinityTo(c)));
                            Life.Diary(w, o, Persona.Say(o, $"무전 너머에서 {c.Name}의 숨소리가 멎었다"));
                        }
                    w.History.Add(w, HistoryKind.Death, $"{Ko.IGa(c.Name)} 선체 밖에서 숨졌다 ({c.Vitals.InjuryCause ?? "사고"})" + (p.Adrift ? $" — 해치에서 {(c.Position - Anchor).Length():0}칸 떨어진 곳" : ""), null, new[] { c });
                }
                if (p.Lost && !p.Announced)
                {
                    p.Announced = true;
                    w.History.Add(w, HistoryKind.Death, $"{c.Name}의 시신은 찾지 못했다 — 추모 때 헬멧 대신 이름표를 놓았다", null, new[] { c });
                }
                continue;
            }
            // 실종: 무전이 끊겼다
            if (p.Adrift && !p.Missing && p.Signal < 0.04f && p.TowedBy < 0 && p.TowDrone < 0)
            {
                p.Missing = true;
                Stats.Missing++;
                w.Log.Add(w.Tick, LogKind.Warning, $"{c.Name}의 무전이 끊겼다 — 실종 (마지막 위치 해치에서 {(p.LastHeard - Anchor).Length():0}칸)", c.Id);
                w.History.Add(w, HistoryKind.Casualty, $"{c.Name}의 무전이 끊겼다 — 선체 밖에서 실종", null, new[] { c });
                foreach (var o in w.Crew) if (!o.Dead && p.KnownBy.Contains(o.Id)) { o.Needs.Stress = MathF.Min(1f, o.Needs.Stress + 0.1f); AddDread(o, 0.1f); }
            }
            // 선체에서 떨어져 있다 (표류는 아닌데 손잡이에서 멀다 — 추진팩으로 돌아가야 한다)
            if (!p.Adrift && c.CanAct && !OnHull(c) && c.Job?.Current is not JetToil) { if (p.OffHullSince < 0) p.OffHullSince = w.Tick; }
            else p.OffHullSince = -1;
            // 표류 중: 몇 분마다 무전으로 부른다
            if (p.Adrift && c.CanAct && p.TowedBy < 0 && p.TowDrone < 0 && w.Tick - p.LastCall > SimTime.Minutes(5) && c.Suit != null)
            {
                p.LastCall = w.Tick;
                float o2 = c.Suit.Oxygen / MathF.Max(0.01f, c.Suit.Leak) * 60f;
                float away = (c.Position - Anchor).Length();
                string text = p.RescuerId >= 0 ? $"보인다 — 여기야! 산소 {o2:0}분"
                    : p.Mode == DriftMode.Return ? $"추진팩으로 돌아가는 중 — {away:0}칸 남았다"
                    : $"메이데이 — {Ko.IGa(c.Name)} 표류 중. 해치에서 {away:0}칸 · 산소 {o2:0}분";
                Say(c, text, p.RescuerId >= 0 ? RadioTone.Chat : RadioTone.Mayday, c.Id);
            }
            // 운석 경보: 판단
            if (alarm != null && alarm.MinutesLeft(w.Tick) > 0f && c.CanAct && !p.Adrift && p.PlanFor != alarm.Id && (alarm.Warned >= WarnLevel.Manual || HeardRecently(c) || alarm.Warned == WarnLevel.Lookout))
                Decide(c, p, alarm);
            if (p.PlanFor >= 0 && (alarm == null || alarm.Id != p.PlanFor))
            {
                if (p.Plan == EvaPlan.Shelter && c.CanAct && !c.Dead) w.Log.Add(w.Tick, LogKind.Work, "파편이 지나갔다 — 선체 그늘에서 나와 하던 일로", c.Id);
                p.Plan = EvaPlan.Work;
                p.PlanFor = -1;
                p.ShelterAt = null;
            }
        }
    }

    // ─────────────────────────────── 운석 경보: 에어락 · 그늘 · 마저 끝내기 ───────────────────────────────

    /// <summary>에어락(해치)까지 걸리는 분 (선체 밖 길 · 표류 중이면 곧장).</summary>
    public float EtaMinutes(CrewMember c)
    {
        var w = _w;
        if (HatchOuter(w) is not Cell outer) return 999f;
        float cells;
        if (!OnHull(c)) cells = (c.Position - outer.Center).Length() * 1.6f;
        else
        {
            var path = w.Paths.Find(c.Cell, outer, new PathProfile(1f, true, true, null, true));
            cells = path == null ? (c.Position - outer.Center).Length() * 2f : path.Count;
        }
        float perMin = Locomotion.BaseSpeed * SimTime.TicksPerHour / 60f * 0.7f * Wounds.LegFactor(c.Vitals); // 손잡이를 잡고 옮겨 간다
        return cells / MathF.Max(0.3f, perMin) + 0.3f;
    }

    /// <summary>운석 들어오는 곳에서 선체가 가려 주는, 선체에 붙은 칸 (가까운 것).</summary>
    public Cell? FindShelter(CrewMember c, Vector2 from)
    {
        var w = _w;
        var grid = w.Ship.Grid;
        var dist = w.Paths.Flood(c.Cell, new PathProfile(1f, true, true, null, true));
        Cell? best = null;
        int bestD = int.MaxValue;
        var at = c.Cell;
        for (int dy = -9; dy <= 9; dy++)
        for (int dx = -9; dx <= 9; dx++)
        {
            var cell = new Cell(at.X + dx, at.Y + dy);
            if (!w.Paths.IsSpace(cell) || !HugsHull(cell.Center)) continue;
            if (!LineBlocked(from, cell.Center)) continue;
            int d = dist.Get(cell);
            if (d < 0 || d >= bestD) continue;
            if (w.Crew.Any(o => o != c && !o.Dead && o.Cell == cell)) continue;
            best = cell;
            bestD = d;
        }
        return best;
    }

    /// <summary>가까운 선체에 붙을 칸 (궤적을 모를 때: 몸을 붙이기만 한다).</summary>
    private Cell? NearestHug(CrewMember c)
    {
        var w = _w;
        var at = c.Cell;
        Cell? best = null;
        float bd = float.MaxValue;
        for (int dy = -3; dy <= 3; dy++)
        for (int dx = -3; dx <= 3; dx++)
        {
            var cell = new Cell(at.X + dx, at.Y + dy);
            if (!w.Paths.IsSpace(cell) || !HugsHull(cell.Center)) continue;
            float d = (cell.Center - c.Position).LengthSquared();
            if (d < bd) { bd = d; best = cell; }
        }
        return best;
    }

    private void Decide(CrewMember c, EvaPerson p, IncomingMeteor inc)
    {
        var w = _w;
        float left = inc.MinutesLeft(w.Tick);
        float eta = EtaMinutes(c);
        bool traj = inc.Warned >= WarnLevel.Manual;
        var shade = traj ? FindShelter(c, inc.Entry.Center) : NearestHug(c);
        float perMin = Locomotion.BaseSpeed * SimTime.TicksPerHour / 60f * 0.7f;
        if (shade is Cell sh0 && (sh0.Center - c.Position).Length() * 1.3f / perMin > left) shade = NearestHug(c); // 그늘까지 못 간다 — 선체에 몸만 붙인다
        EvaPlan own = eta + 0.4f < left ? EvaPlan.ToAirlock : shade != null ? EvaPlan.Shelter : EvaPlan.ToAirlock;
        // 거의 다 했고 들어오는 곳이 멀다: 성실하고 대담한 사람은 마저 끝낸다
        float prog = c.Job?.Current?.Progress ?? 0f;
        if (traj && c.Job?.Order is WorkOrder && prog > 0.55f && (c.Position - inc.Entry.Center).Length() > 9f + 4f * inc.Size && c.Traits.Diligence + c.Traits.Bravery > 1.1f)
            own = EvaPlan.Finish;
        var plan = own;
        if (p.AdviceFor == inc.Id && p.Advice != null)
        {
            bool obey = p.AdviceComputer ? w.Automation.Trusts.Obeys(c) : w.Minds.Obedience(c) > 0.35f;
            if (obey) plan = p.AdvicePlan;
            else if (p.AdvicePlan != own)
            {
                p.Ignored = true;
                w.Log.Add(w.Tick, LogKind.Warning, $"무전 지시({PlanName(p.AdvicePlan)})를 듣지 않고 {PlanName(own)}", c.Id);
                if (p.AdviceComputer) w.Automation.Trusts.Hesitated(c);
            }
            if (plan == EvaPlan.Shelter && shade == null) shade = NearestHug(c);
        }
        p.Plan = plan;
        p.PlanFor = inc.Id;
        p.ShelterAt = plan == EvaPlan.Shelter ? shade : null;
        p.Incident = true;
        switch (plan)
        {
            case EvaPlan.ToAirlock:
                Stats.ToAirlock++;
                p.HurryUntil = w.Tick + SimTime.Minutes(left + 1f);
                Say(c, eta < left ? $"알았다 — 들어간다 (에어락까지 {eta:0.#}분)" : "들어간다 — 늦을지도 몰라!", RadioTone.Chat, c.Id);
                break;
            case EvaPlan.Shelter:
                Stats.Shelters++;
                Say(c, traj ? "에어락은 늦다 — 선체 그늘에 붙는다" : "몸을 선체에 붙인다!", RadioTone.Chat, c.Id);
                break;
            case EvaPlan.Finish:
                Stats.FinishUnder++;
                Say(c, $"거의 다 했다 — 마저 끝낸다 ({prog * 100:0}%)", RadioTone.Chat, c.Id);
                break;
        }
        w.Log.Add(w.Tick, LogKind.Warning, $"운석 {left:0.#}분 — 에어락까지 {eta:0.#}분 → {PlanName(plan)}", c.Id);
        c.Interrupt(w);
    }

    /// <summary>운석 경보에 마저 끝내거나 그늘에 숨는 사람은 에어락으로 뛰지 않는다 (대피 판단이 이 판단을 따른다).</summary>
    public bool Staying(CrewMember c) => Of(c) is EvaPerson p && p.PlanFor >= 0 && p.Plan is EvaPlan.Finish or EvaPlan.Shelter;

    /// <summary>선체 밖의 사람이 다가오는 운석을 먼저 본다 (경보가 없을 때) — 무전으로 외친다.</summary>
    private void Lookouts()
    {
        var w = _w;
        foreach (var inc in w.Sensors.Incoming)
        {
            if (inc.Warned != WarnLevel.None) continue;
            float left = inc.MinutesLeft(w.Tick);
            if (left > 1.4f || left <= 0f) continue;
            var eye = w.Crew.Where(c => c.Outside && c.CanAct && !c.Dead && c.Suit != null && (c.Position - inc.Entry.Center).Length() < 22f)
                .OrderBy(c => (c.Position - inc.Entry.Center).LengthSquared()).FirstOrDefault();
            if (eye == null || !R.Chance(0.55f + 0.3f * eye.Traits.Calm)) continue;
            inc.Warned = WarnLevel.Lookout;
            inc.WarnedAt = w.Tick;
            inc.WarnedBy = eye.Name;
            Stats.Lookouts++;
            var call = Say(eye, $"운석! {Dir(inc.Entry.Center - w.Structure.ShipCenter)} 쪽에서 불빛 — 충격 대비!", RadioTone.Mayday, eye.Id);
            MarkLog.Add(eye.Memory.Marks, w.Tick, "선체 밖에서 운석을 먼저 봤다");
            if (call.Heard.Any(id => Crew(id) is { Outside: false })) w.RaiseAlert($"{eye.Name}(선외 무전): 운석 — 충격 대비!", null, AlertLevel.Critical, shipWide: true);
        }
    }

    private static string Dir(Vector2 v) =>
        MathF.Abs(v.X) > MathF.Abs(v.Y) ? (v.X > 0 ? "우현" : "좌현") : (v.Y > 0 ? "선미" : "선수");

    // ─────────────────────────────── 파편 ───────────────────────────────

    /// <summary>운석이 박혔다: 선체 밖의 사람과 드론이 파편을 맞는다 (그늘이면 덜 · 가까우면 직격). 다친 사람을 돌려준다.</summary>
    public List<CrewMember> OnImpact(Cell entry, float size, Vector2 dir, WarnLevel warned)
    {
        var w = _w;
        var hurt = new List<CrewMember>();
        var from = entry.Center;
        float r = 3f + 3f * size;
        foreach (var c in w.Crew)
        {
            if (c.Dead || !c.Outside) continue;
            float d = (c.Position - from).Length();
            if (d > r) continue;
            var p = Of(c);
            bool braced = p != null && p.Plan == EvaPlan.Shelter && p.ShelterAt is Cell sc && c.Cell == sc || c.Down;
            float power = size * (1f - d / (r + 0.5f)) * R.Range(0.6f, 1.15f) * Exposure(c.Position, from, braced);
            if (d < 1.1f) power *= 2.2f; // 직격
            if (power < 0.035f)
            {
                Stats.NearMisses++;
                if (p != null && p.Plan == EvaPlan.Shelter) w.Log.Add(w.Tick, LogKind.Work, "선체 그늘에 붙어 있었다 — 파편이 머리 위로 지나갔다", c.Id);
                AddDread(c, 0.04f);
                continue;
            }
            HitPerson(c, power, from, "운석 파편 (선체 밖)");
            hurt.Add(c);
            if (p != null && p.Plan == EvaPlan.Finish) Life.Diary(w, c, Persona.Say(c, "마저 끝내려다 파편을 맞았다. 들어갈걸"));
            if (p != null && p.Ignored && p.AdviceComputer) w.Automation.Trusts.Change(c, 0.06f, "컴퓨터 말을 듣지 않았다가 선체 밖에서 맞았다");
        }
        w.Drones.OnMeteorParts(entry, size, dir);
        Push(from, r, 14f * size);
        return hurt;
    }

    /// <summary>폭발 · 충돌이 떠다니는 것을 밀어낸다.</summary>
    public void Push(Vector2 from, float r, float strength)
    {
        foreach (var f in Floaters)
        {
            if (f.CarriedBy >= 0) continue;
            var d = f.Pos - from;
            float len = d.Length();
            if (len > r || len < 0.01f) continue;
            f.Vel += d / len * strength * (1f - len / r);
            f.SpinRate += strength * 20f * (R.Float() - 0.5f);
        }
    }

    private EvaPart PickPart(EvaPerson? p, bool braced)
    {
        // 그늘에 몸을 붙였으면 등과 다리만 드러난다
        float[] wts = braced ? new[] { 3f, 8f, 6f, 6f, 14f, 14f, 30f, 3f } : new[] { 12f, 30f, 11f, 11f, 9f, 9f, 12f, 6f };
        if (p == null || !p.Lines) wts[(int)EvaPart.Tether] = 0f;
        float sum = 0f;
        foreach (var x in wts) sum += x;
        float roll = R.Float() * sum;
        for (int i = 0; i < wts.Length; i++)
        {
            roll -= wts[i];
            if (roll <= 0f) return (EvaPart)i;
        }
        return EvaPart.Torso;
    }

    private static BodyPart BodyOf(EvaPart p) => p switch
    {
        EvaPart.Helmet => BodyPart.Head, EvaPart.LeftArm => BodyPart.LeftArm, EvaPart.RightArm => BodyPart.RightArm,
        EvaPart.LeftLeg => BodyPart.LeftLeg, EvaPart.RightLeg => BodyPart.RightLeg, _ => BodyPart.Chest,
    };

    /// <summary>부상을 맞은 부위에 남긴다 (원인 글이 고르는 부위 대신).</summary>
    private static void Injure(CrewMember c, float amount, EvaPart part, string cause)
    {
        var v = c.Vitals;
        int n0 = v.Wounds.Count;
        var before = new float[n0];
        for (int i = 0; i < n0; i++) before[i] = v.Wounds[i].Weight;
        NeedsSystem.AddInjury(v, amount, cause);
        Wound? got = null;
        float added = 0f;
        if (v.Wounds.Count > n0) { got = v.Wounds[^1]; added = got.Weight; }
        else for (int i = 0; i < n0; i++) if (v.Wounds[i].Weight > before[i] + 1e-6f) { got = v.Wounds[i]; added = got.Weight - before[i]; break; }
        if (got == null || added <= 0f) return;
        var want = BodyOf(part);
        var kind = part == EvaPart.Helmet ? WoundKind.Cut : amount > 0.3f ? WoundKind.Fracture : part == EvaPart.Pack ? WoundKind.Crush : WoundKind.Cut;
        if (got.Part == want && got.Kind == kind) return;
        got.Weight -= added;
        if (got.Weight <= 1e-5f && v.Wounds.Count > n0) v.Wounds.Remove(got);
        var tgt = v.Wounds.FirstOrDefault(x => x.Part == want && x.Kind == kind && !x.Lost);
        if (tgt == null) v.Wounds.Add(tgt = new Wound { Part = want, Kind = kind, Cause = cause });
        tgt.Weight += added;
    }

    /// <summary>선체 밖에서 맞았다 — 부위 · 우주복 · 생명줄 · 공구. 운석 파편과 드론 폭발 파편이 같은 규칙.</summary>
    public void HitPerson(CrewMember c, float power, Vector2 from, string cause)
    {
        var w = _w;
        if (c.Dead) return;
        var p = Of(c) ?? Begin(c);
        bool braced = p.Plan == EvaPlan.Shelter && p.ShelterAt is Cell sc && c.Cell == sc;
        var part = PickPart(p, braced);
        p.HitAt = w.Tick;
        p.HitPart = part;
        p.Incident = true;
        Stats.Hits++;
        var wv = c.Suit?.Wear;
        float armor = c.Suit == null ? 0f : 0.42f - 0.15f * MathF.Min(1f, wv!.Scuff);
        float mul = part switch
        {
            EvaPart.Helmet => 1.5f, EvaPart.Torso => 1.15f, EvaPart.LeftArm or EvaPart.RightArm => 0.7f,
            EvaPart.LeftLeg or EvaPart.RightLeg => 0.8f, EvaPart.Pack => 0.45f, _ => 0.1f,
        };
        float body = power * (1f - armor) * mul;
        c.Vitals.Health = MathF.Max(0f, c.Vitals.Health - body); // 하한 없음: 직격은 죽을 수 있다
        string why = $"{cause} · {PartName(part)}";
        if (body > 0.01f) Injure(c, body * 0.9f, part, why);
        string suitNote = "";
        if (wv != null) suitNote = SuitHit(c, p, wv, part, power);
        Flashes.Add((w.Tick, c.Position, power, wv != null));
        Push(c, p, from, power, part);
        if (!c.Down && R.Chance(0.15f + 0.55f * power)) DropTool(c, from, power);
        c.Interrupt(w);
        Memory.Shake(w, c, 0.08f + 0.15f * MathF.Min(1f, power), "선체 밖에서 파편을 맞았다");
        AddDread(c, 0.08f + 0.25f * MathF.Min(1f, power));
        MarkLog.Add(c.Memory.Marks, w.Tick, $"선체 밖에서 {cause} — {PartName(part)}" + (suitNote != "" ? $" ({suitNote})" : ""));
        w.Log.Add(w.Tick, LogKind.Warning, $"선체 밖에서 {cause} — {PartName(part)} (체력 {c.Vitals.Health * 100:0}%)" + (suitNote != "" ? $" · {suitNote}" : ""), c.Id);
        if (c.Vitals.Health > 0.12f && !c.Down)
        {
            string say = suitNote.Contains("파공") || suitNote.Contains("깨짐") ? "우주복이 뚫렸다 — 숨이 샌다!"
                : suitNote.Contains("누출") ? $"{PartName(wv!.BreachPart)}에서 쉭 소리가 — 샌다!"
                : suitNote.Contains("금") ? "바이저에 금이 갔어…"
                : p.Adrift ? "" : "윽 — 맞았어. 괜찮아, 아직은";
            if (say != "") Say(c, say, RadioTone.Hurt, c.Id);
        }
        else
        {
            // 쓰러졌다: 무전 너머가 조용해진다 — 동료 · 컴퓨터가 알아챈다
            var call = Say(null, "…(숨소리만 들린다)", RadioTone.Hurt, c.Id, c.Position, c.Name);
            foreach (var id in call.Heard) p.KnownBy.Add(id);
        }
    }

    /// <summary>우주복에 난 상처 (같은 자리를 또 맞으면 깊어진다).</summary>
    private string SuitHit(CrewMember c, EvaPerson p, SuitWear wv, EvaPart part, float power)
    {
        var w = _w;
        float d = power * R.Range(0.7f, 1.35f);
        switch (part)
        {
            case EvaPart.Helmet:
            {
                float before = wv.Visor;
                wv.Visor = MathF.Min(1f, wv.Visor + d * 1.5f);
                if (before < 1f && wv.Visor >= 1f)
                {
                    Stats.VisorBreaks++;
                    wv.Known = true;
                    wv.VisorShield = false;
                    wv.Notes.Add("바이저 깨짐");
                    for (int i = 0; i < 4; i++)
                        Spawn(FloatKind.Shard, "바이저 조각", c.Position + new Vector2(0f, -0.15f), new Vector2(R.Range(-6f, 6f), R.Range(-6f, 6f)), Array.Empty<(ItemKind, int)>(), c.Id, -1);
                    return "바이저 깨짐";
                }
                if (before < 0.35f && wv.Visor >= 0.35f) { Stats.VisorCracks++; wv.Known = true; wv.Notes.Add("바이저 금"); return "바이저 금"; }
                return wv.Visor > 0.05f ? "바이저 잔금" : "";
            }
            case EvaPart.Pack:
            {
                if (c.Suit != null) c.Suit.Oxygen = MathF.Max(0f, c.Suit.Oxygen - d * 1.2f);
                wv.Fuel = MathF.Max(0f, wv.Fuel - d * 0.8f);
                string note = $"산소통이 맞아 산소가 빠져나갔다 · 추진팩 연료 {wv.Fuel * 100:0}%";
                if (R.Chance(0.1f + 0.35f * power))
                {
                    p.JetStuckUntil = w.Tick + SimTime.Minutes(R.Range(0.3f, 1.2f));
                    p.SpinRate += (R.Chance(0.5f) ? -1f : 1f) * 60f;
                    note += " · 추진팩 밸브가 열린 채 걸렸다";
                }
                wv.Notes.Add("등짐 파손");
                return note;
            }
            case EvaPart.Tether:
                return "";
        }
        float sev = (d + wv.Scuff * 0.25f) * TechWeb.Mul(w, "eva.suit"); // v16.14 아라미드 겹 · 자가 봉합 우주복
        if (wv.Patched && wv.Breach >= SuitBreach.MicroLeak && R.Chance(0.5f))
        {
            wv.Patched = false;
            w.Log.Add(w.Tick, LogKind.Warning, "붙여 둔 패치가 떨어져 나갔다", c.Id);
        }
        var nb = sev < 0.12f ? SuitBreach.Scratch : sev < 0.3f ? SuitBreach.Tear : sev < 0.55f ? SuitBreach.MicroLeak : SuitBreach.Puncture;
        if (wv.Breach == SuitBreach.Tear && sev >= 0.08f && nb < SuitBreach.MicroLeak) nb = SuitBreach.MicroLeak; // 찢긴 데를 또 맞으면 샌다
        if (wv.Breach == SuitBreach.MicroLeak && sev >= 0.25f) nb = SuitBreach.Puncture;
        if (nb == SuitBreach.Scratch)
        {
            wv.Scuff = MathF.Min(1f, wv.Scuff + d);
            wv.Scratches++;
            return ""; // 긁힘은 입은 사람도 모른다 (돌아와 점검할 때 안다)
        }
        if (nb > wv.Breach)
        {
            wv.Breach = nb;
            wv.BreachPart = part;
            wv.Patched = false;
            wv.Known = true;
            wv.Notes.Add($"{PartName(part)} {BreachName(nb)}");
            if (nb >= SuitBreach.MicroLeak) Stats.Breaches++;
            return $"우주복 {PartName(part)} {BreachName(nb)}";
        }
        wv.Scuff = MathF.Min(1f, wv.Scuff + d * 0.5f);
        wv.Scratches++;
        return "";
    }

    /// <summary>맞은 힘이 몸을 민다: 생명줄이 붙잡거나 끊긴다 (두 줄이면 한 줄이 남기도).</summary>
    private void Push(CrewMember c, EvaPerson p, Vector2 from, float power, EvaPart part)
    {
        var w = _w;
        var away = c.Position - from;
        away = away.LengthSquared() < 0.0001f ? new Vector2(0f, 1f) : Vector2.Normalize(away);
        bool stuck = p.JetStuckUntil > w.Tick;
        int lines = p.Tethers - p.Cut;
        int cutNow = 0;
        if (lines > 0)
        {
            float snap = part == EvaPart.Tether ? 0.92f : 0.06f + 0.5f * power + (stuck ? 0.25f : 0f);
            for (int i = 0; i < lines; i++) if (R.Chance(snap * (i == 0 ? 1f : 0.5f))) cutNow++; // 두 번째 줄은 다른 고리라 덜 당겨진다
            if (part == EvaPart.Tether) cutNow = Math.Max(1, cutNow);
            if (cutNow > 0)
            {
                p.Cut += cutNow;
                p.CutAt = w.Tick;
                p.CutDir = away;
                Stats.TetherCuts += cutNow;
                w.Log.Add(w.Tick, LogKind.Warning, $"생명줄이 끊겼다 ({p.Cut}/{p.Tethers}줄)", c.Id);
            }
            lines -= cutNow;
        }
        if (lines <= 0 && (power > 0.12f || part == EvaPart.Tether || stuck))
            StartDrift(c, p, away * (8f + 26f * power), (R.Chance(0.5f) ? -1f : 1f) * (90f + 380f * power), cutNow > 0 ? "파편에 생명줄이 끊겼다" : "생명줄 없이 파편에 밀려났다");
        else if (cutNow > 0 && lines > 0)
        {
            Stats.TwoLineSaves++;
            if (TwoLines != null) TwoLines.Saves++;
            Say(c, "한 줄 끊겼어 — 남은 줄로 버틴다", RadioTone.Hurt, c.Id);
            Life.Diary(w, c, Persona.Say(c, "생명줄 한 줄이 끊겼다. 두 줄을 걸어 둔 덕에 살았다"));
            w.History.Add(w, HistoryKind.Response, $"{Ko.IGa(c.Name)} 생명줄 한 줄이 끊겼지만 남은 줄로 버텼다 — 두 줄 관행", null, new[] { c });
        }
    }

    /// <summary>선체에서 떨어져 떠내려가기 시작한다.</summary>
    public void StartDrift(CrewMember c, EvaPerson p, Vector2 vel, float spin, string why)
    {
        var w = _w;
        if (p.Adrift) { p.Vel += vel * 0.5f; p.SpinRate += spin * 0.3f; return; }
        p.Adrift = true;
        p.AdriftSince = w.Tick;
        p.Vel = vel;
        p.SpinRate = spin;
        p.Mode = DriftMode.Tumble;
        p.Missing = false;
        p.Incident = true;
        p.Plan = EvaPlan.Work;
        p.PlanFor = -1;
        Stats.Adrifts++;
        if (c.Job != null) c.EndJob(w, ToilStatus.Interrupted);
        c.Path = null;
        c.Destination = null;
        c.NextThinkTick = w.Tick + 1;
        AddDread(c, 0.35f);
        MarkLog.Add(c.Memory.Marks, w.Tick, $"선체에서 떨어져 떠내려갔다 ({why})");
        w.History.Add(w, HistoryKind.Casualty, $"{Ko.IGa(c.Name)} 선체에서 떨어져 떠내려간다 — {why}", null, new[] { c });
        if (c.CanAct && c.Suit != null)
        {
            var call = Say(c, "메이데이 — 줄이 끊겼다! 떠내려간다!", RadioTone.Mayday, c.Id);
            foreach (var id in call.Heard) p.KnownBy.Add(id);
        }
        TelemetryAlarm(c, p, why);
        BornTwoLines(c, why);
    }

    private void BornTwoLines(CrewMember c, string why)
    {
        var w = _w;
        if (TwoLines != null) return;
        TwoLines = new TwoLineCustom { Born = w.Tick, Origin = $"{SimTime.Day(w.Tick)}일째 {Ko.IGa(c.Name)} 생명줄이 끊겨 떠내려간 뒤로", Founder = c.Name };
        TwoLines.Followers.Add(c.Id);
        foreach (var o in w.Crew) if (!o.Dead && !o.IsChild && (HeardRecently(o) || o.Outside)) TwoLines.Followers.Add(o.Id);
        w.History.Add(w, HistoryKind.Lesson, $"이제 선외 작업엔 생명줄을 두 줄 건다 — {TwoLines.Origin}", null, new[] { c }, log: true);
    }

    // ─────────────────────────────── 떠다니는 것 ───────────────────────────────

    public Floater Spawn(FloatKind k, string name, Vector2 at, Vector2 vel, (ItemKind kind, int count)[] yield, int owner, int fromDrone, int belonging = -1, float heat = 0f)
    {
        var f = new Floater
        {
            Id = _nextFloat++, Kind = k, Name = name, Pos = at, Prev = at, Vel = vel, Angle = R.Float() * 6.28f,
            SpinRate = R.Range(-160f, 160f), Since = _w.Tick, Yield = yield, Owner = owner, FromDrone = fromDrone, Belonging = belonging, Heat = heat,
        };
        Floaters.Add(f);
        if (Floaters.Count > 80) { Gone(Floaters[0]); Floaters.RemoveAt(0); }
        return f;
    }

    /// <summary>맞은 충격에 쥐고 있던 공구를 놓친다 (개인 공구 세트면 그 사람 것이 빠진다).</summary>
    private void DropTool(CrewMember c, Vector2 from, float power)
    {
        var w = _w;
        var kinds = new[] { FloatKind.Wrench, FloatKind.BoltBag, FloatKind.Torch, FloatKind.Cutter, FloatKind.Lamp, FloatKind.Mirror };
        var k = kinds[R.Range(0, kinds.Length)];
        var own = w.Belongings.All.FirstOrDefault(b => b.Kind == BelongingKind.Toolset && (b.Owner == c.Id && b.BorrowedBy < 0 || b.BorrowedBy == c.Id) && b.Usable);
        var away = c.Position - from;
        away = away.LengthSquared() < 0.0001f ? new Vector2(1f, 0f) : Vector2.Normalize(away);
        var side = new Vector2(-away.Y, away.X) * R.Range(-2f, 2f);
        string name = own != null ? $"{own.Name}의 {FloatName(k)}" : FloatName(k);
        var yield = k switch { FloatKind.BoltBag => new[] { (ItemKind.Clamp, 1) }, FloatKind.Lamp => new[] { (ItemKind.Lamp, 1) }, _ => Array.Empty<(ItemKind, int)>() };
        Spawn(k, name, c.Position, away * (2.5f + 7f * power) + side, yield, c.Id, -1, own?.Id ?? -1);
        Stats.ToolsLost++;
        if (own != null)
        {
            own.Condition = MathF.Max(0.25f, own.Condition - 0.25f);
            MarkLog.Add(own.Marks, w.Tick, $"선체 밖에서 {Ko.IGa(FloatName(k))} 빠져 떠내려갔다");
        }
        w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.EulReul(name)} 놓쳤다 — 빙글빙글 떠내려간다", c.Id);
    }

    /// <summary>선체 밖의 사람이 손 닿는 곳의 떠다니는 것을 붙잡는다.</summary>
    private void GrabNearby()
    {
        var w = _w;
        foreach (var p in People)
        {
            if (p.Adrift) continue;
            var c = Crew(p.Id);
            if (c == null || !c.CanAct || !c.Outside || c.CarryingPerson != null) continue;
            for (int i = 0; i < Floaters.Count; i++)
            {
                var f = Floaters[i];
                if (f.CarriedBy >= 0 || f.Kind == FloatKind.Shard || (f.Pos - c.Position).LengthSquared() > 1.1f) continue;
                if (f.Tool) Stats.ToolsCaught++;
                foreach (var y in f.Yield) p.Pocket.Add(y);
                if (f.Belonging >= 0 && w.Belongings.All.FirstOrDefault(b => b.Id == f.Belonging) is Belonging b)
                {
                    b.Condition = MathF.Min(1f, b.Condition + 0.25f);
                    MarkLog.Add(b.Marks, w.Tick, $"{Ko.IGa(c.Name)} 선체 밖에서 {Ko.EulReul(FloatName(f.Kind))} 붙잡아 왔다");
                }
                if (f.Heat > 0.3f) { NeedsSystem.AddInjury(c.Vitals, 0.03f, "화상 (달아오른 잔해)"); }
                w.Log.Add(w.Tick, LogKind.Work, $"떠다니던 {Ko.EulReul(f.Name)} 붙잡았다", c.Id);
                Floaters.RemoveAt(i--);
            }
        }
    }

    /// <summary>드론이 건져 와 거치대에 내려놓았다 (부품 · 고철 · 주인에게).</summary>
    private void Deliver(Floater f, Drone d)
    {
        var w = _w;
        var got = new List<string>();
        foreach (var (k, n) in f.Yield)
        {
            // 거치대 자재칸이 받으면 거기, 아니면 거치대 곁 보관함으로 (재고가 된다)
            int put = d.Dock.Storage?.Add(k, n) ?? 0;
            if (put < n)
                foreach (var box in w.Ship.Containers.Where(x => x.Storage!.Free > 0 && x.Storage.Accepts(k)).OrderBy(x => (x.Center - d.Dock.Center).LengthSquared()).ThenBy(x => x.Id))
                {
                    put += box.Storage!.Add(k, n - put);
                    if (put >= n) break;
                }
            if (put > 0) { got.Add($"{ItemKinds.Name(k)} {put}"); w.Drones.SalvagedItems += put; }
        }
        if (f.Tool) Stats.ToolsCaught++;
        if (f.Belonging >= 0 && w.Belongings.All.FirstOrDefault(b => b.Id == f.Belonging) is Belonging own)
        {
            own.Condition = MathF.Min(1f, own.Condition + 0.25f);
            MarkLog.Add(own.Marks, w.Tick, $"{Ko.IGa(d.Name)} 선체 밖에서 건져 왔다");
        }
        w.Drones.Salvaged++;
        w.Log.Add(w.Tick, LogKind.Work, $"{Ko.IGa(d.Name)} 떠다니던 {Ko.EulReul(f.Name)} 건져 왔다" + (got.Count > 0 ? $" — {string.Join(" · ", got)}" : ""));
    }

    private void Gone(Floater f)
    {
        var w = _w;
        f.Lost = true;
        if (f.Tool)
        {
            Stats.ToolsGone++;
            if (Crew(f.Owner) is CrewMember o && !o.Dead)
            {
                o.Needs.Stress = MathF.Min(1f, o.Needs.Stress + (f.Belonging >= 0 ? 0.05f : 0.02f));
                if (f.Belonging >= 0) Life.Diary(w, o, Persona.Say(o, $"{Ko.IGa(FloatName(f.Kind))} 별 사이로 사라졌다. 손에 익은 거였는데"));
            }
            w.Log.Add(w.Tick, LogKind.Life, $"놓친 {Ko.IGa(f.Name)} 시야 밖으로 사라졌다");
        }
    }

    // ─────────────────────────────── 무전: 들은 사람만 안다 ───────────────────────────────

    private RadioCall Say(CrewMember c, string text, RadioTone tone, int about) => Say(c, text, tone, about, c.Position, c.Name);

    public RadioCall Say(CrewMember? from, string text, RadioTone tone, int about, Vector2 at, string who)
    {
        var w = _w;
        float sig = from == null && tone == RadioTone.Computer ? 1f : SignalAt(at);
        if (from?.Suit?.Wear is SuitWear wv && wv.Visor >= 1f && !wv.VisorShield) sig *= 0.6f;
        var call = new RadioCall { Id = _nextCall++, Tick = w.Tick, From = from?.Id ?? (tone == RadioTone.Computer ? -1 : about), Who = who, Text = text, Signal = sig, Tone = tone, At = at };
        var cmd = w.Command;
        foreach (var o in w.Crew)
        {
            if (o == from || o.Dead || o.Down || o.IsChild) continue;
            bool hears = false;
            float to = tone == RadioTone.Computer ? (o.Outside ? SignalAt(o.Position) : 1f) : sig;
            if (o.Outside && o.Suit != null) hears = to > 0.12f || tone != RadioTone.Computer && (o.Position - at).Length() < 20f; // 우주복 무전 (가까우면 바로)
            else if (!o.Outside && o.IsAwake && o.Room is Room r && r.Powered && r.Type is RoomType.Bridge or RoomType.Comms or RoomType.Airlock or RoomType.EvaPrep) hears = to > 0.08f; // 무전 콘솔 · 에어락 인터컴
            else if (cmd.Active && cmd.Commander == o && o.IsAwake) hears = to > 0.1f; // 지휘자의 손 무전기
            if (!hears || tone == RadioTone.Computer && !o.Outside && about >= 0 && o.Id != about && !(o.Room?.Type is RoomType.Bridge or RoomType.Comms)) continue;
            call.Heard.Add(o.Id);
            _heardAt[o.Id] = w.Tick;
            if (about >= 0 && Of(about) is EvaPerson ap) ap.KnownBy.Add(o.Id);
            if (tone is RadioTone.Mayday or RadioTone.Hurt or RadioTone.Last)
            {
                o.Interrupt(w);
                o.Needs.Stress = MathF.Min(1f, o.Needs.Stress + (tone == RadioTone.Last ? 0.12f : 0.05f) * (1.2f - o.Traits.Calm));
                AddDread(o, tone == RadioTone.Last ? 0.12f : 0.04f);
                if (tone == RadioTone.Last && Crew(about) is CrewMember dying) Life.Diary(w, o, Persona.Say(o, $"{dying.Name}의 마지막 무전을 들었다"));
            }
        }
        call.ComputerHeard = w.Automation.Present && w.Automation.MainOnline && sig > 0.06f;
        Radio.Add(call);
        if (Radio.Count > 60) Radio.RemoveAt(0);
        Stats.Calls++;
        Stats.Heard += call.Heard.Count;
        if (call.Heard.Count == 0 && !call.ComputerHeard) Stats.Unheard++;
        string shown = Garble(text, sig, call.Id);
        w.Log.Add(w.Tick, tone is RadioTone.Mayday or RadioTone.Last or RadioTone.Hurt ? LogKind.Warning : LogKind.Life,
            $"[무전] {who}: {shown}" + (call.Heard.Count == 0 ? call.ComputerHeard ? " (주 컴퓨터만 들었다)" : " (아무도 듣지 못했다)" : $" (들은 사람 {call.Heard.Count})"), from?.Id ?? -1);
        return call;
    }

    /// <summary>약한 무전은 지직거린다 (같은 무전이면 같은 자리가 끊긴다 — 결정론).</summary>
    public static string Garble(string text, float signal, int seed)
    {
        if (signal >= 0.4f) return text;
        var chars = text.ToCharArray();
        uint h = (uint)(seed * 2654435761u);
        float drop = (0.4f - signal) * 1.6f;
        for (int i = 0; i < chars.Length; i++)
        {
            h ^= h << 13; h ^= h >> 17; h ^= h << 5;
            if (chars[i] != ' ' && (h & 1023) / 1023f < drop) chars[i] = '·';
        }
        return new string(chars) + (signal < 0.2f ? " …지직…" : "");
    }

    // ─────────────────────────────── 주 컴퓨터: 원격 측정 → 판단 · 경고 · 제안 ───────────────────────────────

    private bool Telemetry(EvaPerson p) => _w.Automation.Present && _w.Automation.MainOnline && p.Signal > 0.06f;

    /// <summary>생명줄 장력이 끊겼다: 컴퓨터가 바로 안다 (텔레메트리) → 방송 · 드론/구조 제안.</summary>
    private void TelemetryAlarm(CrewMember c, EvaPerson p, string why)
    {
        var w = _w;
        if (!Telemetry(p)) return;
        var airlock = w.Ship.RoomsOf(RoomType.Airlock).FirstOrDefault();
        float d = (c.Position - Anchor).Length();
        var b = w.Automation.Speak.Announce(w.Automation.Voice.Style($"선외 인원 표류 — {c.Name}, 해치에서 {d:0}칸 · 생명줄 장력 없음"), airlock, 2);
        if (b != null) foreach (var id in b.HeardBy) p.KnownBy.Add(id);
        Stats.Warnings++;
        w.Automation.Book.Add(ActKind.Alarm, airlock, $"{c.Name} 생명줄 장력 0 · 해치에서 {d:0}칸 · {p.Vel.Length():0}칸/시 멀어짐", "표류 — 산소가 다하기 전에 건져야 한다",
            "선내 방송 · 무전", "구조", $"eva:drift:{c.Id}", SimTime.Minutes(10), 60f,
            (world, a) => world.Crew.FirstOrDefault(x => x.Id == c.Id) is CrewMember v ? v.Dead ? (2, "구하지 못했다") : !v.Outside ? (1, "건져 왔다") : null : null);
        PlanRetrieval(c, p);
    }

    /// <summary>표류자를 어떻게 건질지: 견인 드론이 있으면 드론 (사람이 나가지 않는다) · 없으면 구조 EVA 사람을 고른다. 방침에 따라 먼저 묻는다.</summary>
    private void PlanRetrieval(CrewMember c, EvaPerson p)
    {
        var w = _w;
        var au = w.Automation;
        var airlock = w.Ship.RoomsOf(RoomType.Airlock).FirstOrDefault();
        float o2 = c.Suit is SuitState s ? s.Oxygen / MathF.Max(0.01f, s.Leak) * 60f : 0f;
        var tug = w.Drones.Drones.FirstOrDefault(d => RobotsV15.Base(d.Kind) == DroneKind.Tow && d.Operational && d.Hurt.Arm >= 0.5f);
        string key = $"eva:fetch:{c.Id}";
        if (tug != null)
        {
            if (au.Asks.Needed("rescue"))
            {
                if (au.Asks.Latest(key) == null)
                {
                    Stats.Proposals++;
                    au.Asks.Propose(key, "rescue", airlock, $"견인 드론 {tug.Name} 발진 — {Ko.EulReul(c.Name)} 건진다", $"생명줄 끊김 · 해치에서 {(c.Position - Anchor).Length():0}칸 · 산소 {o2:0}분",
                        "사람이 나가지 않는다 · 드론 배터리 왕복", 1.5f, null);
                }
            }
            else
            {
                p.DroneOrdered = true;
                au.Book.Add(ActKind.Advice, airlock, $"{c.Name} 표류 · 견인 드론 {tug.Name} 대기 중", "사람이 나가는 것보다 드론이 안전하다", $"{tug.Name} 발진 명령", "", $"eva:tug:{c.Id}", SimTime.Minutes(20), 40f);
            }
            return;
        }
        // 드론이 없다: 가장 알맞은 사람에게 구조 EVA를 맡긴다
        var best = w.Crew.Where(x => x != c && x.CanAct && !x.IsChild && x.Vitals.Health > 0.5f && !(Of(x)?.Adrift ?? false) && !RefusesQuiet(x, true))
            .OrderBy(x => x.Outside ? 0 : 1).ThenBy(x => (x.Position - c.Position).LengthSquared()).ThenBy(x => x.Id).FirstOrDefault();
        if (best == null) return;
        if (au.Asks.Needed("rescue"))
        {
            if (au.Asks.Latest(key) == null)
            {
                Stats.Proposals++;
                au.Asks.Propose(key, "rescue", airlock, $"구조 EVA — {Ko.EulReul(best.Name)} 보낸다", $"{c.Name} 표류 · 견인 드론 없음 · 산소 {o2:0}분",
                    $"{best.Name} 추진팩 · 구조줄 {RescueLineLength:0}칸", 1.5f, null);
            }
            p.AssignedId = -best.Id - 2; // 승인 전 (음수로 기다림)
        }
        else
        {
            p.AssignedId = best.Id;
            p.KnownBy.Add(best.Id);
            Say(null, $"{best.Name}, {c.Name} 구조 EVA 부탁한다 — 해치에서 {(c.Position - Anchor).Length():0}칸", RadioTone.Computer, best.Id, Anchor, w.Automation.Voice.Call);
            au.Book.Add(ActKind.Advice, airlock, $"{c.Name} 표류 · 드론 없음", $"{best.Name}{(best.Outside ? " (이미 밖에 있다)" : "")}이 가장 빠르다", $"{best.Name}에게 구조 EVA 지시", "구조줄 · 추진팩", $"eva:assign:{c.Id}", SimTime.Minutes(20), 40f);
        }
    }

    /// <summary>1분마다: 선외 인원의 산소 · 생명줄 · 운석 도착 시간을 읽고 들어오라 · 그늘로 · 패치하라고 일러 준다.</summary>
    private void ComputerWatch()
    {
        var w = _w;
        var au = w.Automation;
        var airlock = w.Ship.RoomsOf(RoomType.Airlock).FirstOrDefault();
        var alarm = w.Sensors.Alarm;
        foreach (var p in People)
        {
            var c = Crew(p.Id);
            if (c == null || c.Dead || !Telemetry(p)) continue;
            // 승인된 제안 실행
            string key = $"eva:fetch:{c.Id}";
            if (au.Asks.Latest(key) is Proposal prop && prop.Accepted && p.Adrift)
            {
                if (prop.Title.StartsWith("견인")) p.DroneOrdered = true;
                else if (p.AssignedId < -1) { p.AssignedId = -p.AssignedId - 2; p.KnownBy.Add(p.AssignedId); }
            }
            if (c.Suit is not SuitState s) continue;
            float o2 = s.Oxygen / MathF.Max(0.01f, s.Leak) * 60f;
            float eta = EtaMinutes(c);
            string? text = null;
            EvaPlan plan = EvaPlan.ToAirlock;
            string observe = "", judge = "";
            if (alarm != null && w.Sensors.Tracking && alarm.MinutesLeft(w.Tick) > 0f && !p.Adrift && p.AdviceFor != alarm.Id && (c.Position - alarm.Entry.Center).Length() < 24f)
            {
                float left = alarm.MinutesLeft(w.Tick);
                if (eta + 0.4f < left) { text = $"{c.Name}, 운석 {left:0.#}분 · 에어락까지 {eta:0.#}분 — 들어오십시오"; plan = EvaPlan.ToAirlock; }
                else if (FindShelter(c, alarm.Entry.Center) is Cell sh)
                {
                    text = $"{c.Name}, 운석 {left:0.#}분 — 에어락은 늦다. {Dir(sh.Center - c.Position)} 쪽 선체 그늘로 ({(sh.Center - c.Position).Length():0}칸)";
                    plan = EvaPlan.Shelter;
                }
                else { text = $"{c.Name}, 운석 {left:0.#}분 — 선체에 몸을 붙이십시오"; plan = EvaPlan.Shelter; }
                p.AdviceFor = alarm.Id;
                observe = $"운석 {left:0.#}분 뒤 · {c.Name} 에어락까지 {eta:0.#}분";
                judge = plan == EvaPlan.ToAirlock ? "들어올 시간이 된다" : "들어오기엔 늦다 — 가려진 칸";
            }
            else if (!p.Adrift && o2 < eta + 12f && w.Tick - p.LastAdvice > SimTime.Minutes(6))
            {
                text = $"{c.Name}, 산소 {o2:0}분 · 에어락까지 {eta:0.#}분 — 들어오십시오";
                observe = $"{c.Name} 우주복 산소 {o2:0}분 (누출 ×{s.Leak:0.#})";
                judge = $"에어락까지 {eta:0.#}분 + 여유 12분이 안 된다";
            }
            else if (s.Wear.Leaking && s.Wear.PatchKit > 0 && w.Tick - p.LastAdvice > SimTime.Minutes(4) && p.PatchStart < 0)
            {
                text = $"{c.Name}, 우주복 압력이 떨어진다 — {PartName(s.Wear.BreachPart)} 패치하십시오 · 산소 {o2:0}분";
                observe = $"{c.Name} 우주복 압력 하강 · 산소 {o2:0}분";
                judge = "패치하면 버틴다";
            }
            else if (p.Lines && p.Cut > 0 && p.Tethers - p.Cut == 1 && w.Tick - p.LastAdvice > SimTime.Minutes(15) && !p.Adrift)
            {
                text = $"{c.Name}, 생명줄 한 줄 끊김 — 남은 한 줄뿐이다. 작업 멈추고 줄부터 다시 거십시오";
                observe = $"{c.Name} 생명줄 장력 한 줄";
                judge = "한 줄이 더 끊기면 표류";
            }
            else if (p.Adrift && !p.DroneOrdered && p.AssignedId == -1 && p.RescuerId < 0 && p.TowDrone < 0) PlanRetrieval(c, p);
            if (text == null) continue;
            p.LastAdvice = w.Tick;
            p.Advice = text;
            p.AdvicePlan = plan;
            p.AdviceComputer = true;
            Stats.Warnings++;
            Say(null, w.Automation.Voice.Style(text), RadioTone.Computer, c.Id, Anchor, w.Automation.Voice.Call);
            int cid = c.Id;
            au.Book.Add(ActKind.Advice, airlock, observe, judge, "무전: " + text, plan == EvaPlan.Shelter ? "그늘로" : "귀환", $"eva:{c.Id}:{plan}", SimTime.Minutes(4), 30f,
                (world, a) => world.Crew.FirstOrDefault(x => x.Id == cid) is CrewMember v ? v.Dead ? (2, "선외에서 잃었다") : !v.Outside ? (1, "무사히 들어왔다") : null : null);
            // 들은 사람은 다시 판단한다 (따를지는 신뢰가 정한다)
            c.Interrupt(w);
            if (alarm != null && p.AdviceFor == alarm.Id && p.PlanFor == alarm.Id) Decide(c, p, alarm);
        }
    }

    /// <summary>컴퓨터가 멎었으면: 함교 · 통신실의 사람(또는 지휘자)이 무전으로 지휘한다 — 숫자 없이 눈과 경험으로.</summary>
    private void CommanderWatch()
    {
        var w = _w;
        if (w.Automation.Present && w.Automation.MainOnline) return;
        var cmd = w.Command;
        var boss = w.Crew.FirstOrDefault(o => !o.Dead && o.CanAct && o.IsAwake && !o.Outside && o.Room?.Type is RoomType.Bridge or RoomType.Comms)
            ?? (cmd.Active && cmd.Commander is CrewMember k && k.CanAct && k.IsAwake && !k.Outside ? k : null);
        if (boss == null) return;
        var alarm = w.Sensors.Alarm;
        foreach (var p in People)
        {
            var c = Crew(p.Id);
            if (c == null || c.Dead || p.Adrift) continue;
            if (alarm == null || alarm.MinutesLeft(w.Tick) <= 0f || p.AdviceFor == alarm.Id || p.Signal < 0.1f) continue;
            float left = alarm.MinutesLeft(w.Tick);
            float guess = (c.Position - Anchor).Length() / 2f; // 눈대중
            var plan = guess < left ? EvaPlan.ToAirlock : EvaPlan.Shelter;
            string text = plan == EvaPlan.ToAirlock ? $"{c.Name}, 운석이다 — 들어와!" : $"{c.Name}, 늦었어 — 선체에 붙어!";
            p.AdviceFor = alarm.Id;
            p.Advice = text;
            p.AdvicePlan = plan;
            p.AdviceComputer = false;
            Say(boss, text, RadioTone.Command, c.Id);
            c.Interrupt(w);
        }
    }

    // ─────────────────────────────── 선외 공포 ───────────────────────────────

    /// <summary>선외 작업을 거부한다: 겪은 일이 무섭다 (구조라면 가까운 사람일수록 이겨 낸다).</summary>
    public bool Refuses(CrewMember c, out string? why)
    {
        why = null;
        bool rescue = PlanningRescue;
        if (!RefusesQuiet(c, rescue)) return false;
        var w = _w;
        why = rescue ? "선외 공포 — 구하러 나가지 못한다" : "선외 공포 — 에어락 앞에서 발이 떨어지지 않는다";
        if (!_refusedAt.TryGetValue(c.Id, out var t) || w.Tick - t > SimTime.Hours(8))
        {
            Stats.Refusals++;
            w.Log.Add(w.Tick, LogKind.Warning, $"선외 작업을 거부했다 — {why}", c.Id);
            Life.Diary(w, c, Persona.Say(c, "에어락 문 앞에서 다리가 떨렸다. 나는 못 나가겠다"));
            MarkLog.Add(c.Memory.Marks, w.Tick, "선외 작업을 거부했다 (무서워서)");
        }
        _refusedAt[c.Id] = w.Tick;
        return true;
    }

    private bool RefusesQuiet(CrewMember c, bool rescue)
    {
        float d = Dread(c);
        if (d < 0.3f) return false;
        float th = 0.55f + 0.3f * c.Traits.Bravery;
        if (TwoLines != null && TwoLines.Followers.Contains(c.Id)) th += 0.05f;
        if (_w.Crew.Any(o => o != c && !o.Dead && o.Outside && c.AffinityTo(o) > 0.4f)) th += 0.1f; // 믿는 사람이 밖에 있다
        if (rescue) th += 0.25f + 0.4f * MathF.Max(0f, People.Where(x => x.Adrift).Select(x => Crew(x.Id)).Where(x => x != null).Select(x => c.AffinityTo(x!)).DefaultIfEmpty(0f).Max());
        return d > th;
    }

    // ─────────────────────────────── 보관함: 상한 우주복 ───────────────────────────────

    /// <summary>보관함에서 우주복을 꺼냈다: 멀쩡한 게 남아 있으면 그걸, 아니면 수리 대기 중인 걸 입는다.</summary>
    public void OnIssue(CrewMember c, Furniture locker)
    {
        var w = _w;
        if (c.Suit is not SuitState s) return;
        int left = locker.Storage!.Count(ItemKind.Suit);
        var mine = DamagedSuits.Where(x => x.LockerId == locker.Id).ToList();
        while (mine.Count > left + 1) { DamagedSuits.Remove(mine[^1]); mine.RemoveAt(mine.Count - 1); }
        bool goodLeft = left + 1 - mine.Count > 0;
        if (!goodLeft && mine.Count > 0)
        {
            var st = mine[0];
            DamagedSuits.Remove(st);
            s.Wear = st.Wear.Copy();
            Stats.DamagedIssued++;
            w.Log.Add(w.Tick, LogKind.Warning, $"남은 우주복은 수리를 기다리던 것뿐 — {st.Wear.Describe()}인 걸 입었다", c.Id);
        }
        // 전기가 있는 보관함은 추진팩을 채워 둔다 (없으면 반쯤)
        if (!locker.Room.Powered && s.Wear.Fuel > 0.5f && R.Chance(0.5f)) s.Wear.Fuel = 0.5f;
    }

    /// <summary>우주복을 보관함에 걸었다: 상했으면 수리 목록에.</summary>
    public void OnStow(CrewMember c, Furniture locker)
    {
        var w = _w;
        if (c.Suit is not SuitState s) return;
        var wv = s.Wear;
        if (locker.Room.Powered && w.Air.Reserve > 4f && wv.Fuel < 1f) { w.Air.Reserve -= 3f * (1f - wv.Fuel); wv.Fuel = 1f; }
        if (!(wv.Breach >= SuitBreach.Tear || wv.Visor >= 0.2f || wv.Scuff > 0.3f || wv.PatchKit < 2 || wv.Patches > 0)) return;
        DamagedSuits.Add(new StoredSuit { Id = _nextSuit++, LockerId = locker.Id, Wear = wv.Copy(), Since = w.Tick, LastWearer = c.Name });
        w.Log.Add(w.Tick, LogKind.Work, $"상한 우주복을 \"수리 대기\" 꼬리표를 달아 걸었다 — {wv.Describe()}", c.Id);
    }

    public static (ItemKind kind, int count)[] MendCost(SuitWear wv)
    {
        var need = new List<(ItemKind, int)> { (ItemKind.Tape, 1) };
        if (wv.Breach >= SuitBreach.Tear || wv.Scuff > 0.3f) need.Add((ItemKind.Glue, 1));
        if (wv.Breach == SuitBreach.Puncture) need.Add((ItemKind.Sealant, 1));
        if (wv.Visor >= 0.35f) need.Add((ItemKind.Silicate, 1));
        return need.ToArray();
    }

    public static float MendHours(SuitWear wv) =>
        0.35f + (wv.Breach >= SuitBreach.Tear ? 0.3f : 0f) + (wv.Breach == SuitBreach.Puncture ? 0.5f : 0f) + (wv.Visor >= 0.35f ? 0.6f : 0f);

    internal void Mended(StoredSuit st, CrewMember by)
    {
        var w = _w;
        DamagedSuits.Remove(st);
        Stats.SuitsMended++;
        by.Practice(Skill.Mechanics, 0.02f);
        w.Log.Add(w.Tick, LogKind.Work, $"우주복을 고쳤다 — {st.Wear.Describe()} → 이상 없음 (마지막에 입은 사람 {st.LastWearer})", by.Id);
        MarkLog.Add(by.Memory.Marks, w.Tick, $"우주복 수리 ({st.Wear.Describe()})");
    }

    // ─────────────────────────────── 구조 · 회수의 결과 ───────────────────────────────

    /// <summary>사람이 표류자를 건져 왔다: 목숨을 빚졌다.</summary>
    internal void OnCrewRescue(CrewMember rescuer, CrewMember victim)
    {
        var w = _w;
        if (Of(victim) is EvaPerson vp) { vp.Adrift = false; vp.Vel = Vector2.Zero; vp.SpinRate = 0f; vp.TowedBy = -1; vp.RescuerId = -1; vp.AssignedId = -1; }
        if (victim.Dead)
        {
            Stats.BodiesRecovered++;
            w.History.Add(w, HistoryKind.Response, $"{Ko.IGa(rescuer.Name)} 선체 밖에서 {victim.Name}의 시신을 거둬 왔다", null, new[] { rescuer, victim });
            Life.Diary(w, rescuer, Persona.Say(rescuer, $"{Ko.EulReul(victim.Name)} 데려왔다. 가벼웠다"));
            rescuer.Needs.Stress = MathF.Min(1f, rescuer.Needs.Stress + 0.08f);
            return;
        }
        Stats.CrewRescues++;
        if (Of(victim) is EvaPerson sp) sp.SavedBy = rescuer.Id;
        rescuer.Stats.Rescues++;
        victim.ChangeAffinity(rescuer, 0.3f);
        rescuer.ChangeAffinity(victim, 0.1f);
        w.Relations.Remember(victim, rescuer, RelationReason.SavedMe, "줄이 끊겨 떠내려가던 나를 붙잡아 왔다");
        w.Relations.Rescued(victim, rescuer);
        w.Automation.Trusts.Saved(victim, rescuer, w.Ship.RoomsOf(RoomType.Airlock).FirstOrDefault() ?? w.Ship.Rooms[0]);
        MarkLog.Add(victim.Memory.Marks, w.Tick, $"{Ko.IGa(rescuer.Name)} 떠내려가던 나를 붙잡아 왔다");
        MarkLog.Add(rescuer.Memory.Marks, w.Tick, $"떠내려가던 {Ko.EulReul(victim.Name)} 붙잡아 왔다");
        Life.Diary(w, victim, Persona.Say(victim, $"별이 빙글빙글 돌았다. {rescuer.Name}의 장갑이 내 팔을 잡았을 때 울었다"));
        Life.Diary(w, rescuer, Persona.Say(rescuer, $"{Ko.EulReul(victim.Name)} 잡았다. 추진팩 연료가 바닥나기 전에"));
        if (TwoLines != null) { TwoLines.Followers.Add(victim.Id); TwoLines.Followers.Add(rescuer.Id); }
        w.History.Add(w, HistoryKind.Response, $"{Ko.IGa(rescuer.Name)} 추진팩을 켜고 나가 떠내려가던 {Ko.EulReul(victim.Name)} 붙잡아 왔다", null, new[] { rescuer, victim }, log: true);
    }

    /// <summary>드론이 표류자를 해치까지 끌어왔다.</summary>
    internal void OnDroneRescue(CrewMember victim, Drone d)
    {
        var w = _w;
        if (Of(victim) is EvaPerson vp) { vp.Adrift = false; vp.Vel = Vector2.Zero; vp.SpinRate = 0f; vp.TowDrone = -1; vp.DroneOrdered = false; }
        if (victim.Dead) { Stats.BodiesRecovered++; w.History.Add(w, HistoryKind.Response, $"{Ko.IGa(d.Name)} {victim.Name}의 시신을 해치까지 끌어왔다", null, new[] { victim }); return; }
        Stats.DroneRescues++;
        MarkLog.Add(victim.Memory.Marks, w.Tick, $"{Ko.IGa(d.Name)} 떠내려가던 나를 끌어왔다");
        MarkLog.Add(d.Marks, w.Tick, $"떠내려가던 {Ko.EulReul(victim.Name)} 해치까지 끌어왔다");
        Life.Diary(w, victim, Persona.Say(victim, $"{d.Name}의 집게가 등짐을 물었다. 그 드론에 이름을 붙여 준 게 다행이다"));
        if (d.Hurt.Keeper < 0) d.Hurt.Keeper = victim.Id;
        w.Automation.Trusts.Change(victim, 0.05f, $"컴퓨터가 보낸 드론이 선체 밖에서 나를 건졌다");
        w.History.Add(w, HistoryKind.Response, $"{Ko.IGa(d.Name)} 떠내려가던 {Ko.EulReul(victim.Name)} 해치까지 끌어왔다", null, new[] { victim }, log: true);
    }

    internal void OnSelfReturn(CrewMember c, EvaPerson p)
    {
        var w = _w;
        Stats.SelfReturns++;
        w.Log.Add(w.Tick, LogKind.Work, $"추진팩으로 돌아와 선체 손잡이를 잡았다 (연료 {(c.Suit?.Wear.Fuel ?? 0f) * 100:0}%)", c.Id);
        Life.Diary(w, c, Persona.Say(c, "추진팩 연료가 바닥나기 전에 손잡이를 잡았다. 손이 한참 떨렸다"));
        w.History.Add(w, HistoryKind.Response, $"{Ko.IGa(c.Name)} 추진팩으로 회전을 멈추고 선체로 돌아왔다", null, new[] { c });
        c.Practice(Skill.Mechanics, 0.03f);
    }

    /// <summary>표류 · 쓰러짐 · 시신 — 건지러 갈 대상 (이 사람이 아는 것만).</summary>
    internal CrewMember? RescueTarget(CrewMember c, out EvaPerson? tp)
    {
        tp = null;
        CrewMember? best = null;
        float bestKey = float.MaxValue;
        foreach (var p in People)
        {
            if (p.Id == c.Id || p.Lost || p.TowDrone >= 0 || p.TowedBy >= 0) continue;
            var v = Crew(p.Id);
            if (v == null || v.CarriedBy != null) continue;
            bool need = p.Adrift || v.Dead && v.Outside;
            if (!need) continue;
            if (p.RescuerId >= 0 && p.RescuerId != c.Id && Crew(p.RescuerId) is CrewMember other && !other.Dead && other.Job?.Activity is EvaRescueActivity) continue;
            bool knows = p.KnownBy.Contains(c.Id) || p.AssignedId == c.Id || c.Outside && (c.Position - v.Position).LengthSquared() < 100f;
            if (!knows) continue;
            if (p.DroneOrdered && DroneComing(p)) continue;
            float key = (v.Dead ? 1000f : 0f) + (v.Position - c.Position).Length() - (p.AssignedId == c.Id ? 500f : 0f);
            if (key < bestKey) { bestKey = key; best = v; tp = p; }
        }
        return best;
    }

    private bool DroneComing(EvaPerson p) => _w.Drones.Drones.Any(d => d.Hurt.FetchPerson == p.Id && d.State != DroneState.Docked);

    /// <summary>표류자 중 드론이 건지러 갈 사람 (컴퓨터가 명령했거나 · 바로 실행 방침이면).</summary>
    internal (CrewMember c, EvaPerson p)? FetchCandidate(Drone d)
    {
        foreach (var p in People.OrderBy(x => Crew(x.Id)?.Dead == true ? 1 : 0).ThenBy(x => x.Id))
        {
            if (!p.Adrift || p.Lost || p.TowDrone >= 0 || p.TowedBy >= 0) continue;
            var c = Crew(p.Id);
            if (c == null || c.CarriedBy != null) continue;
            if (!p.DroneOrdered && !(c.Dead && p.KnownBy.Count > 0)) continue;
            if ((c.Position - _w.Structure.ShipCenter).Length() > StructureSystem.LostRange + 15f) continue;
            return (c, p);
        }
        return null;
    }

    /// <summary>v16.11 지문 (결정론 시험).</summary>
    public void Hash(Action<long> I, Action<float> F)
    {
        I(People.Count); I(Floaters.Count); I(Radio.Count); I(DamagedSuits.Count);
        I(Stats.Hits); I(Stats.Breaches); I(Stats.Patches); I(Stats.TetherCuts); I(Stats.Adrifts); I(Stats.Calls); I(Stats.Heard); I(Stats.Warnings); I(Stats.Refusals);
        foreach (var p in People) { I(p.Id); F(p.Vel.X); F(p.Vel.Y); I(p.Adrift ? 1 : 0); I(p.Cut); I((int)p.Plan); }
        foreach (var f in Floaters) { I(f.Id); F(f.Pos.X); F(f.Pos.Y); }
        foreach (var c in _w.Crew) if (c.Suit is SuitState s) { I((int)s.Wear.Breach); F(s.Wear.Visor); F(s.Wear.Fuel); }
        foreach (var kv in _dread.OrderBy(k => k.Key)) { I(kv.Key); F(kv.Value); }
    }
}
