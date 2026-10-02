using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v16.13 폭발과 연쇄 — 폭발 공통 물리.
//   무엇이 터지든(설비 · 폭발성 물건 · 폭약) 같은 규칙을 탄다: 압력파가 방 연결망을 따라 퍼지고(닫힌 문 · 격벽은 막다 휘거나 날아가고,
//   열린 문은 지나가고, 칸막이 벽은 무너지며 조금 넘기고, 진공 쪽은 약하다) · 파편이 곧게 날아(사람 · 설비 · 벽 · 물건 — 운석 파편과 같은 판정)
//   · 사람과 물건을 밀쳐내고(넘어짐 · 날아감 · 문 쾅) · 불을 붙이고 · 근처 폭발성 물건을 달군다(연쇄 — 끝이 있다).
//   감각: 섬광(눈부심) · 이명(경보 · 방송을 늦게 듣는다) · 벽 너머 쿵(들은 사람만 안다).
//   흔적: 방사형 그을음 · 구멍 · 잔해 · 휜 문틀 · 깨진 조명 · 유리 조각 — 며칠 남는다 (배 손보기가 닦고 편다).
//   사람: 놀람 · 공포(그 방을 꺼린다) · 구조 · 조사(원인 · 책임) · 일기 · 추모.
//   규모: 개인 · 방 · 계통 · 배 — 세기와 번진 정도로 표시한다.

/// <summary>무엇이 터졌나 — 압력 · 열 · 파편 · 연기 · 독이 다르고, 화면도 종류마다 다르게 그린다.</summary>
public enum BlastKind : byte
{
    Generic, Battery, Hydrogen, Arc, Fuel, Combustion, Steam, Grease, Furnace, Refrigerant,
    Oxygen, Gas, Dust, Charge, ColdGas, Ferment, Aerosol, Propellant, Powder, Acetylene, Nitrate, Flare,
}

/// <summary>폭발의 규모 (다음 단계에서 사고를 이 규모로 묶는다).</summary>
public enum BlastScale : byte { Personal, Room, System, Ship }

/// <summary>종류별 성질: 압력 몫 · 열(불붙임, 음수면 불을 끈다) · 파편 수 · 연기 · 독 · 증기 · 냉기 · 산소 · 섬광 · 소리.</summary>
public readonly record struct BlastSpec(string Name, float Shock, float Heat, int Frags, float Smoke, float Toxin, float Steam, float Cold, float O2, float Flash, float Loud);

/// <summary>파편 하나의 궤적 (화면: 선 · 맞은 자리). Hit: 0 없음 · 1 칸막이 벽 · 2 외벽 · 3 사람 · 4 설비 · 5 물건 · 6 문.</summary>
public readonly record struct Shard(Vector2 From, Vector2 To, float Strength, byte Hit);

/// <summary>압력파가 닿은 칸 (화면: 벽에 막히는 충격파 고리).</summary>
public readonly record struct WaveCell(short X, short Y, float P, float D);

public sealed class BlastRecord
{
    public int Id { get; init; }
    public long Tick { get; init; }
    public Cell At { get; init; }
    public float Power { get; init; }
    public BlastKind Kind { get; init; }
    public string Cause { get; init; } = "";
    public int Room { get; init; } = -1;
    public int Depth { get; init; }
    /// <summary>책임: 그 물건을 그 자리에 둔 사람 · 폭약을 설치한 사람 (-1 모름).</summary>
    public int Blame { get; set; } = -1;
    public string BlameWhy { get; set; } = "";
    public BlastScale Scale { get; set; }
    public List<WaveCell> Wave { get; } = new();
    /// <summary>압력파가 부딪친 벽 칸과 힘 (화면: 고리가 벽에 막혀 번쩍).</summary>
    public List<WaveCell> WallHits { get; } = new();
    public List<Shard> Shards { get; } = new();
    public List<int> Heard { get; } = new();
    public List<int> Deafened { get; } = new();
    public List<int> Flashed { get; } = new();
    public List<int> Hurt { get; } = new();
    public List<int> Fell { get; } = new();
    public List<int> BentDoors { get; } = new();
    public List<int> BlownDoors { get; } = new();
    public List<int> SlammedDoors { get; } = new();
    public List<int> Rooms { get; } = new();
    public List<Cell> Ignited { get; } = new();
    public int Breaches { get; set; }
    public int Chained { get; set; }
    public int Machines { get; set; }
    public bool Investigated { get; set; }
    public int Investigator { get; set; } = -1;
    public bool Mourned { get; set; }
    public string Finding { get; set; } = "";
    public BlastSpec Spec => BlastSystem.Spec(Kind);
}

/// <summary>폭발 자리 (며칠 남는 흔적): 방사형 그을음 줄기 · 깨진 조명 · 추모.</summary>
public sealed class BlastScar
{
    public int Record { get; init; }
    public Cell At { get; init; }
    public BlastKind Kind { get; init; }
    public float Power { get; init; }
    public long Tick { get; init; }
    public int Room { get; init; } = -1;
    public float Reach { get; init; }
    /// <summary>그을음 줄기 (각도 · 길이 0~1) — 막힌 쪽은 짧다.</summary>
    public List<(float angle, float len)> Rays { get; } = new();
    public List<Cell> Lights { get; } = new();
    public bool Memorial { get; set; }
    public List<int> Visited { get; } = new();
    public float Fade { get; set; } = 1f;
}

public sealed class BlastStats
{
    public int Detonations, Chains, MaxDepth, DoorsBent, DoorsBlown, DoorsSlammed, ThinWalls, Breaches, Shards, ShardHits, Knocked, Flung, Burned, Scalded,
        Deafened, Flashed, Thumps, HeardLate, BroadcastMissed, ComputerRelays, Rescues, Investigations, Blamed, Memorials, Lights, Woke, Suppressed, Avoided;
    public override string ToString() =>
        $"폭발 {Detonations}(연쇄 {Chains} · 깊이 {MaxDepth}) · 문 휨 {DoorsBent} · 날아감 {DoorsBlown} · 쾅 {DoorsSlammed} · 칸막이 {ThinWalls} · 외벽 파공 {Breaches} · 파편 {Shards}(맞음 {ShardHits}) · "
        + $"넘어짐 {Knocked} · 날아간 물건 {Flung} · 화상 {Burned} · 증기 {Scalded} · 이명 {Deafened} · 섬광 {Flashed} · 벽 너머 쿵 {Thumps} · 경보 늦게 {HeardLate} · 방송 놓침 {BroadcastMissed}(컴퓨터 다시 알림 {ComputerRelays}) · "
        + $"구조 {Rescues} · 조사 {Investigations} · 책임 {Blamed} · 추모 {Memorials} · 깨진 조명 {Lights} · 깸 {Woke} · 분말 소화 {Suppressed} · 피함 {Avoided}";
}

/// <summary>v16.13 폭발 공통 물리 · 감각 · 흔적 · 사람. 폭발성 물건과 의도적 폭파는 Items (Explosives.cs).</summary>
public sealed partial class BlastSystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7867 + 251));
    public BlastStats Stats { get; } = new();
    public List<BlastRecord> Recent { get; } = new();
    public List<BlastScar> Scars { get; } = new();
    public ExplosiveSet Items { get; }
    internal HashSet<(int rec, int victim)> Helped { get; } = new();
    /// <summary>지금 살피러 가는 사람 (다친 사람 → 구조하러 가는 사람). 끝나면 Helped 로 옮긴다 — 도중에 끊기면 다른 사람이 간다.</summary>
    internal Dictionary<(int rec, int victim), int> RescueBy { get; } = new();
    /// <summary>문짝이 날아간 문 (문틀을 펴면 다시 단다).</summary>
    private readonly SortedSet<int> _blown = new();
    /// <summary>불기둥 · 셀 분출: 몇 분 동안 그 자리를 달군다 (곁의 폭발성 물건이 익는다).</summary>
    public List<(Cell at, long until, float heat, BlastKind kind)> Jets { get; } = new();
    public bool Blown(Door d) => _blown.Contains(d.Id);
    internal void MarkBlown(Door d) { if (_blown.Add(d.Id)) Stats.DoorsBlown++; }
    public IReadOnlyCollection<int> BlownDoors => _blown;
    private int _next = 1;

    public BlastSystem(World w)
    {
        _w = w;
        Items = new ExplosiveSet(w, this);
    }

    // ───────────────────────────── 종류 표 ─────────────────────────────

    private static readonly BlastSpec[] Specs =
    {
        new("폭발", 0.8f, 0.4f, 6, 0.4f, 0.05f, 0f, 0f, 0f, 0.6f, 0.8f),
        new("배터리 열폭주", 0.45f, 0.7f, 6, 0.6f, 0.5f, 0f, 0f, 0f, 0.8f, 0.6f),
        new("수소 폭발", 0.95f, 0.4f, 5, 0.1f, 0f, 0.2f, 0f, 0f, 1f, 1f),
        new("아크 섬광", 0.2f, 0.6f, 2, 0.3f, 0.1f, 0f, 0f, 0f, 1f, 0.7f),
        new("연료 폭발", 0.55f, 0.95f, 3, 0.9f, 0.15f, 0f, 0f, 0f, 0.6f, 0.8f),
        new("연소실 파열", 0.8f, 0.6f, 10, 0.6f, 0.1f, 0f, 0f, 0f, 0.7f, 1f),
        new("증기 파열", 0.7f, 0f, 6, 0f, 0f, 1f, 0f, 0f, 0.1f, 0.8f),
        new("기름 불기둥", 0.2f, 0.9f, 1, 0.8f, 0.05f, 0f, 0f, 0f, 0.5f, 0.4f),
        new("가열로 파열", 0.6f, 0.8f, 9, 0.5f, 0.1f, 0f, 0f, 0f, 0.7f, 0.8f),
        new("냉매 분출", 0.35f, 0f, 3, 0f, 0.6f, 0f, 0.8f, 0f, 0.1f, 0.5f),
        new("산소통 파열", 0.75f, 0.6f, 7, 0.2f, 0f, 0f, 0.2f, 1f, 0.9f, 0.9f),
        new("가스 실린더 폭발", 0.8f, 0.85f, 8, 0.4f, 0.05f, 0f, 0f, 0f, 0.8f, 1f),
        new("분진 폭발", 0.65f, 0.9f, 1, 0.6f, 0.05f, 0f, 0f, 0f, 0.7f, 0.9f),
        new("폭약", 1f, 0.25f, 12, 0.3f, 0.05f, 0f, 0f, 0f, 1f, 1f),
        new("가스통 파열", 0.6f, 0f, 6, 0f, 0f, 0f, 0.6f, 0f, 0.1f, 0.7f),
        new("발효 항아리 파열", 0.3f, 0f, 5, 0f, 0f, 0f, 0f, 0f, 0f, 0.35f),
        new("스프레이 캔 폭발", 0.35f, 0.7f, 2, 0.3f, 0.1f, 0f, 0f, 0f, 0.6f, 0.45f),
        new("추진제 폭발", 0.85f, 0.9f, 8, 0.7f, 0.3f, 0f, 0f, 0f, 0.9f, 1f),
        new("소화기 파열", 0.4f, -1f, 4, 0f, 0.05f, 0f, 0f, 0f, 0.1f, 0.5f),
        new("아세틸렌 폭발", 0.9f, 0.85f, 6, 0.8f, 0.05f, 0f, 0f, 0f, 1f, 1f),
        new("질산염 폭발", 0.95f, 0.5f, 6, 0.5f, 0.45f, 0f, 0f, 0f, 0.8f, 1f),
        new("신호탄 불꽃", 0.15f, 0.8f, 10, 0.5f, 0.1f, 0f, 0f, 0f, 0.9f, 0.4f),
    };

    public static BlastSpec Spec(BlastKind k) => Specs[(int)k];

    public static BlastKind KindOf(BlowKind k) => k switch
    {
        BlowKind.ThermalRunaway => BlastKind.Battery,
        BlowKind.Hydrogen => BlastKind.Hydrogen,
        BlowKind.ArcFlash => BlastKind.Arc,
        BlowKind.FuelFire => BlastKind.Fuel,
        BlowKind.Combustion => BlastKind.Combustion,
        BlowKind.Rupture => BlastKind.Steam,
        BlowKind.Grease => BlastKind.Grease,
        BlowKind.Furnace => BlastKind.Furnace,
        BlowKind.Refrigerant => BlastKind.Refrigerant,
        _ => BlastKind.Generic,
    };

    /// <summary>원인 글로 종류를 짐작한다 (탱크 파열 같은 옛 호출).</summary>
    public static BlastKind Guess(string cause) =>
        cause.Contains("산소") ? BlastKind.Oxygen : cause.Contains("질소") || cause.Contains("가스통") ? BlastKind.ColdGas : cause.Contains("수소") ? BlastKind.Hydrogen
        : cause.Contains("연료") ? BlastKind.Fuel : cause.Contains("배터리") ? BlastKind.Battery : BlastKind.Generic;

    public static string ScaleName(BlastScale s) => s switch { BlastScale.Personal => "개인", BlastScale.Room => "방", BlastScale.System => "계통", _ => "배" };

    // ───────────────────────────── 터뜨린다 ─────────────────────────────

    private int _depthNow;
    internal int DepthNow => _depthNow;

    /// <summary>
    /// 폭발. 칸 · 세기(0~1.5) · 종류 · 원인. 설비 폭발(Volatile) · 폭발성 물건 · 폭약 · 탱크 파열이 모두 이걸 탄다.
    /// 인과 사슬: 지금 사슬이 있으면 그 자식, 없으면 새 뿌리.
    /// </summary>
    public BlastRecord? Detonate(Cell at, float power, BlastKind kind, string cause, Machine? source = null, int depth = 0, int blame = -1, Explosive? item = null)
    {
        var w = _w;
        power *= TechWeb.Mul(w, "blast.power"); // v16.14 폭압 배출구 · 폭발 억제 거품
        if (power <= 0.005f || !w.Ship.Grid.InBounds(at)) return null;
        var cl = w.Causes;
        var room = w.Ship.RoomAt(at);
        if (cl.Context >= 0)
        {
            cl.Hit(room, cl.Context);
            return Core(at, power, kind, cause, source, depth, blame, item);
        }
        int node = cl.Root(CauseKind.Explosion, $"{Spec(kind).Name} — {room?.Name ?? "선체"}" + (cause.Length > 0 && cause != "시험" ? $" ({cause})" : ""), room, at.Center, observer: cl.ConsumeObserver());
        w.Scale.Kind(node, "blast:" + kind); // v16.18 도감: 폭발 종류 (규모는 피해로)
        cl.Hit(room, node);
        using (cl.Because(node)) return Core(at, power, kind, cause, source, depth, blame, item);
    }

    private BlastRecord Core(Cell at, float power, BlastKind kind, string cause, Machine? source, int depth, int blame, Explosive? item)
    {
        var w = _w;
        var ship = w.Ship;
        var spec = Spec(kind);
        var room = ship.RoomAt(at);
        _depthNow = depth;
        w.Movement.Bang(room, at.Center, MathF.Min(1f, 0.45f + power * spec.Loud), cause); // v14.5 펑 — 가까운 사람이 움찔한다
        Stats.Detonations++;
        Stats.MaxDepth = Math.Max(Stats.MaxDepth, depth);
        if (power >= 0.12f)
        {
            // 옛 기록 (위기 판단 · 소리 공포가 읽는다)
            w.Volatile.Stats.Explosions++;
            w.Volatile.Blasts.Add(new BlastEvent(w.Tick, at, power, cause));
            if (w.Volatile.Blasts.Count > 12) w.Volatile.Blasts.RemoveAt(0);
        }
        var rec = new BlastRecord { Id = _next++, Tick = w.Tick, At = at, Power = power, Kind = kind, Cause = cause, Room = room?.Id ?? -1, Depth = depth, Blame = blame };
        if (blame >= 0) rec.BlameWhy = item != null ? $"{Ko.EulReul(item.Spec.Name)} 그 자리에 두었다" : "";
        Recent.Add(rec);
        if (Recent.Count > 16) Recent.RemoveAt(0);

        // ── 1) 압력파: 한 번 돌려 문 · 칸막이가 받는 힘을 재고 → 문이 휘거나 날아가고 → 다시 돌려 실제로 퍼진 모양 ──
        float shock = power * spec.Shock * Medium(room);
        float reach = MathF.Min(10f, 1.5f + 3.6f * power);
        _doorLoad.Clear();
        Flood(at, shock, reach, sound: false, seeds: null);
        var seeds = ResolveObstacles(rec, shock, reach);
        Flood(at, shock, reach, sound: false, seeds: seeds);
        foreach (int i in _reached)
        {
            var c = ship.Grid.CellAt(i);
            rec.Wave.Add(new WaveCell((short)c.X, (short)c.Y, _v[i], _d[i]));
            if (ship.RoomAt(c) is Room rr && !rec.Rooms.Contains(rr.Id)) rec.Rooms.Add(rr.Id);
        }
        // 압력파가 받은 칸 값을 남겨 둔다 (아래 단계가 같은 값을 읽는다)
        _field.Clear();
        foreach (int i in _reached) _field[i] = _v[i];
        _wallField.Clear();
        foreach (int i in _wallsTouched)
        {
            _wallField[i] = _wl[i];
            if (_wl[i] < 0.04f) continue;
            var wc = ship.Grid.CellAt(i);
            rec.WallHits.Add(new WaveCell((short)wc.X, (short)wc.Y, _wl[i], (wc.Center - at.Center).Length()));
        }

        // ── 2) 벽: 받은 힘만큼 (외벽은 구멍 · 칸막이는 무너진다) · 창은 깨진다 ──
        foreach (int i in _wallsTouched)
        {
            var cell = ship.Grid.CellAt(i);
            float load = _wl[i];
            if (load < 0.03f) continue;
            var body = w.Body.WallAt(cell);
            float mul = body?.Thin == true ? 1.5f : 1f;
            var ws = ship.WallAt(cell);
            bool wasLeaking = ws != null && ws.IsHull && Hull.EffectiveBreach(ws) > 0f;
            Hull.Damage(ship, cell, 0.7f * load * mul * R.Range(0.8f, 1.1f));
            if (ws != null && ws.IsHull && !wasLeaking && Hull.EffectiveBreach(ws) > 0f) { rec.Breaches++; Stats.Breaches++; }
            if (body?.Window == true && load > 0.25f)
                foreach (var d in Cell.Dirs4)
                    if (ship.Grid.Kind(cell + d) == TileKind.Floor) w.Body.RaiseMark(cell + d, CellMark.Glass, MathF.Min(1f, 0.4f + load), "폭발에 창이 깨졌다");
        }

        // ── 3) 문: 구동기 (가까우면 잔해가 낀다) ──
        foreach (var door in ship.Doors)
        {
            if (door.Removed || !_doorLoad.TryGetValue(door.Id, out float dl)) continue;
            w.Fixtures.OnDebris(door.Cell, dl, room);
        }

        // ── 4) 설비: 수명 · 고장 · 달아오름 (터질 수 있는 설비는 연쇄) ──
        int hurtMachines = 0;
        foreach (var f in ship.Furniture)
        {
            if (f.Machine is not Machine m || f.Room.Detached || f.Stowed || m == source) continue;
            float k = 0f;
            foreach (var fc in f.Cells) k = MathF.Max(k, PAt(fc));
            if (k < 0.02f) continue;
            m.Condition = MathF.Max(0.02f, m.Condition - 0.45f * k);
            m.Heat += 0.8f * k * MathF.Max(0.3f, spec.Heat + 0.3f);
            Procedures.DamageLinks(w, m, 2.6f * k, 2.1f * k, cause); // v12.1 설비 전선 · 관
            if (R.Chance(0.8f * k)) { w.Machines.Break(m); hurtMachines++; }
            if (VolatileSystem.Mode(f.Type) != BlowKind.None && m.Heat > 0.9f && R.Chance(0.5f * k) && depth < ExplosiveSet.MaxDepth)
            {
                w.Volatile.Stats.Chain++;
                Stats.Chains++;
                rec.Chained++;
                m.Heat = MathF.Max(m.Heat, 1.2f); // 다음 틱에 터진다 (한 번에 다 터지지 않게)
            }
        }
        rec.Machines = hurtMachines;
        // 관 · 배 전체 망 · 그 방 회로
        foreach (var seg in w.Piping.Segments.ToList())
        {
            if (seg.Path.Count == 0) continue;
            Cell near = default; float best = 0f;
            foreach (var pc in seg.Path) { float v = PAt(pc); if (v > best) { best = v; near = pc; } }
            if (best < 0.05f) continue;
            if (R.Chance(0.9f * best)) w.Piping.Damage(seg, 0.5f * best + 0.1f, near, cause);
        }
        if (shock > 0.05f) w.Net.DamageNear(at, reach * 0.8f, 1.3f * shock, cause);
        if (room != null && R.Chance(0.6f * shock) && ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault()?.Machine is Machine panel
            && !panel.Faults.Any(x => x.Circuit == room.Circuit))
        {
            panel.Faults.Add(new Fault { Kind = FaultKind.ShortCircuit, Since = w.Tick, Circuit = room.Circuit });
            w.Causes.OnFault(panel, panel.Faults[^1]);
            panel.FaultCount++;
            w.History.CircuitFaults++;
        }

        // ── 5) 파편: 곧게 날아 사람 · 설비 · 벽 · 물건을 맞힌다 (운석 파편과 같은 판정) ──
        Fragments(rec, at, power, spec, cause, item);

        // ── 6) 사람: 압력 · 화상 · 증기 · 넘어짐 · 놀람 · 공포 ──
        People(rec, at, power, spec, cause, room);

        // ── 7) 감각: 소리(벽 너머 쿵 · 이명 · 깨움) · 섬광 ──
        Senses(rec, at, power, spec, room);

        // ── 8) 물건: 보관함 속 물건이 깨지고, 놓인 물건 · 이동식 장비가 날아간다 · 폭발성 물건이 달아오른다(연쇄) ──
        Things(rec, at, cause);
        w.Matter.OnBlast(at, PAt, cause); // v16.4 물건 물리 한 벌 (같은 Push · 깨짐 단계 · 가루)
        Items.OnBlast(rec, item);

        // ── 9) 불 · 연기 · 열 · 독 · 증기 · 냉기 · 산소 ──
        Heat(rec, at, power, spec, room, cause);

        // ── 10) 잔해 (폭약은 겨눈 곳을 치우려 쓴 것이라 그 곁에는 덜 쌓인다) ──
        int rubble = Rubble(rec, at, power, kind, item);

        // ── 11) 흔적: 방사형 그을음 · 깨진 조명 · 자리 ──
        Traces(rec, at, power, spec, cause);

        // ── 12) 방 기압이 출렁인다 ──
        foreach (int rid in rec.Rooms)
        {
            float peak = 0f;
            foreach (var wc in rec.Wave) if (ship.RoomAt(new Cell(wc.X, wc.Y))?.Id == rid) peak = MathF.Max(peak, wc.P);
            EnsureRooms();
            _slosh[rid] = MathF.Max(_slosh[rid], peak);
        }

        // ── 규모 · 기록 ──
        rec.Scale = power < 0.12f && rec.Hurt.Count <= 1 ? BlastScale.Personal
            : rec.Breaches > 0 || rec.Rooms.Count >= 4 || power >= 0.85f ? BlastScale.Ship
            : rec.Rooms.Count >= 2 || hurtMachines >= 2 || rec.Chained > 0 || power >= 0.4f ? BlastScale.System
            : BlastScale.Room;
        w.Structure.Touch();
        if (rubble > 0) w.Paths.Invalidate();
        string where = room?.Name ?? "선체";
        if (source == null)
        {
            w.History.Add(w, HistoryKind.Incident, $"{where}에서 {spec.Name} — {cause} · 규모 {ScaleName(rec.Scale)}" + (depth > 0 ? $" · 연쇄 {depth}단" : ""), room, rec.Hurt.Select(id => w.Crew.FirstOrDefault(c => c.Id == id)).Where(c => c != null).Cast<CrewMember>(), at);
            if (power >= 0.12f)
                w.RaiseAlert($"{spec.Name} — {where} ({ScaleName(rec.Scale)} 규모)", room, rec.Scale >= BlastScale.System || rec.Hurt.Count > 0 ? AlertLevel.Critical : AlertLevel.Warning, shipWide: power >= 0.3f);
        }
        if (rec.Hurt.Count > 0 || rubble > 0 || hurtMachines > 0 || rec.Breaches > 0)
            w.Log.Add(w.Tick, LogKind.Warning, $"폭발({cause}) — {ScaleName(rec.Scale)} 규모 · 부상 {rec.Hurt.Count} · 넘어짐 {rec.Fell.Count} · 설비 {hurtMachines}대 · 잔해 {rubble}칸"
                + (rec.Breaches > 0 ? $" · 외벽 파공 {rec.Breaches}" : "") + (rec.BlownDoors.Count > 0 ? $" · 날아간 문 {rec.BlownDoors.Count}" : ""));
        w.Board.RequestScan();
        return rec;
    }

    // ───────────────────────────── 압력파 ─────────────────────────────

    private float[] _v = Array.Empty<float>(), _d = Array.Empty<float>(), _t = Array.Empty<float>(), _wl = Array.Empty<float>();
    private int[] _stamp = Array.Empty<int>(), _wstamp = Array.Empty<int>(), _room = Array.Empty<int>(), _wdir = Array.Empty<int>();
    private int _gen;
    private readonly List<int> _reached = new(), _wallsTouched = new();
    private readonly PriorityQueue<int, float> _pq = new();
    private readonly SortedDictionary<int, float> _doorLoad = new();
    private readonly Dictionary<int, float> _field = new(), _wallField = new();
    private float[] _slosh = Array.Empty<float>();

    private void Ensure()
    {
        int n = _w.Ship.Grid.CellCount;
        if (_v.Length == n) return;
        _v = new float[n]; _d = new float[n]; _t = new float[n]; _wl = new float[n];
        _stamp = new int[n]; _wstamp = new int[n]; _room = new int[n]; _wdir = new int[n];
    }

    private void EnsureRooms()
    {
        if (_slosh.Length < _w.Ship.Rooms.Count) Array.Resize(ref _slosh, _w.Ship.Rooms.Count + 4);
    }

    /// <summary>그 방 공기가 압력파를 얼마나 실어 나르나 (진공 쪽은 약하다).</summary>
    public static float Medium(Room? r) => r == null ? 0.15f : Math.Clamp(r.Air.Pressure / 90f, 0.12f, 1f);

    private static float Fall(float d, float reach, bool sound) => sound ? (d > reach ? 0f : 1f / (1f + d * d / 16f)) : MathF.Max(0f, 1f - d / (reach + 0.5f));

    /// <summary>닫힌 문이 압력파를 얼마나 넘기나 (열린 문 · 날아간 문은 거의 다, 격벽은 거의 안).</summary>
    public float DoorPass(Door d)
    {
        if (d.Removed || d.JammedOpen) return 0.9f;
        float closed = d.Bulkhead ? 0.06f : 0.12f;
        if (d.Welded) closed *= 0.8f;
        closed += 0.2f * Math.Clamp(d.Bent, 0f, 1f); // 휜 문틀은 덜 막는다
        float open = Math.Clamp(d.Openness, 0f, 1f);
        return MathF.Min(0.92f, closed + (0.92f - closed) * open);
    }

    private static float DoorSound(Door d) => d.Removed || d.JammedOpen ? 0.95f : 0.35f + 0.6f * Math.Clamp(d.Openness, 0f, 1f);

    private float WallSound(Cell c)
    {
        var ws = _w.Ship.WallAt(c);
        if (ws == null) return 0.15f;
        if (ws.IsHull) return 0f;
        var b = _w.Body.WallAt(c);
        return b == null ? 0.2f : b.Thin ? 0.4f : MathF.Max(0.08f, 0.25f * (1f - 0.5f * MathF.Min(1f, b.SoundBlock)));
    }

    private bool Passable(Cell c, bool sound)
    {
        var k = _w.Ship.Grid.Kind(c);
        return k == TileKind.Floor || k == TileKind.Door || sound && k == TileKind.Wall && !(_w.Ship.WallAt(c)?.IsHull ?? true);
    }

    /// <summary>
    /// 칸 연결망을 따라 퍼진다 (가장 센 길이 먼저): 값 = 세기 × 지나온 문 · 공기 몫 × 거리 감쇠.
    /// 압력은 벽에서 멈추고(벽이 받는 힘을 적는다), 소리는 벽도 조금 넘는다.
    /// </summary>
    private void Flood(Cell at, float power, float reach, bool sound, List<(Cell c, float v, float d)>? seeds)
    {
        Ensure();
        var ship = _w.Ship;
        var g = ship.Grid;
        _gen++;
        _reached.Clear();
        _wallsTouched.Clear();
        _pq.Clear();
        if (power <= 0.001f) return;
        void Push(int i, float t, float d, int room)
        {
            float v = power * t * Fall(d, reach, sound);
            if (v < 0.015f) return;
            if (_stamp[i] == _gen && _v[i] >= v) return;
            if (_stamp[i] != _gen) { _stamp[i] = _gen; _reached.Add(i); }
            _v[i] = v; _t[i] = t; _d[i] = d; _room[i] = room;
            _pq.Enqueue(i, -v);
        }
        Push(g.Index(at), 1f, 0f, ship.RoomAt(at)?.Id ?? -1);
        if (seeds != null)
            foreach (var (c, v, d) in seeds)
            {
                float f = Fall(d, reach, sound);
                if (f <= 0f || !g.InBounds(c)) continue;
                Push(g.Index(c), MathF.Min(1f, v / (power * f)), d, ship.RoomAt(c)?.Id ?? -1);
            }
        while (_pq.TryDequeue(out int i, out float pr))
        {
            if (-pr < _v[i] - 1e-5f) continue; // 낡은 항목
            var c = g.CellAt(i);
            var ck = g.Kind(c);
            for (int k = 0; k < 8; k++)
            {
                var dir = Cell.Dirs8[k];
                var n = c + dir;
                if (!g.InBounds(n)) continue;
                bool diag = dir.X != 0 && dir.Y != 0;
                var nk = g.Kind(n);
                if (diag && (ck == TileKind.Door || nk == TileKind.Door || !Passable(new Cell(c.X + dir.X, c.Y), sound) || !Passable(new Cell(c.X, c.Y + dir.Y), sound))) continue;
                if (nk == TileKind.Void) continue;
                int ni = g.Index(n);
                if (nk == TileKind.Wall)
                {
                    if (!diag)
                    {
                        if (_wstamp[ni] != _gen) { _wstamp[ni] = _gen; _wl[ni] = 0f; _wallsTouched.Add(ni); }
                        if (_v[i] > _wl[ni]) { _wl[ni] = _v[i]; _wdir[ni] = k; }
                    }
                    if (!sound || diag) continue;
                }
                float nt = _t[i];
                int nroom = _room[i];
                if (nk == TileKind.Door && ship.DoorAt(n) is Door door)
                {
                    if (!sound)
                    {
                        _doorLoad.TryGetValue(door.Id, out float dl0);
                        if (_v[i] > dl0) _doorLoad[door.Id] = _v[i];
                    }
                    nt *= sound ? DoorSound(door) : DoorPass(door);
                }
                else if (nk == TileKind.Wall) nt *= WallSound(n);
                else if (ship.RoomAt(n) is Room rn && rn.Id != nroom)
                {
                    if (!sound) nt *= Medium(rn);
                    else if (rn.Air.Pressure < 30f) nt *= Medium(rn);
                    nroom = rn.Id;
                }
                Push(ni, nt, _d[i] + (diag ? 1.4142f : 1f), nroom);
            }
        }
    }

    /// <summary>첫 바퀴에서 받은 힘으로 문과 칸막이의 운명을 정한다 → 둘째 바퀴의 씨앗(무너진 칸막이 너머).</summary>
    private List<(Cell, float, float)> ResolveObstacles(BlastRecord rec, float shock, float reach)
    {
        var w = _w;
        var ship = w.Ship;
        var seeds = new List<(Cell, float, float)>();
        foreach (var (id, load) in _doorLoad)
        {
            if (id >= ship.Doors.Count) continue;
            var d = ship.Doors[id];
            if (d.Removed || d.IsExternal) continue;
            bool closed = d.Openness < 0.5f && !d.JammedOpen;
            if (!closed)
            {
                // 열린 문: 압력파가 지나가며 쾅 — 문이 닫혀 버린다 (자동문은 다시 열리지만, 사람들은 그 소리를 기억한다)
                if (load > 0.12f && !d.HoldOpen && R.Chance(0.6f))
                {
                    d.Openness = 0f;
                    rec.SlammedDoors.Add(id);
                    Stats.DoorsSlammed++;
                }
                continue;
            }
            float bendAt = d.Bulkhead ? 0.45f : 0.22f;
            float blowAt = d.Bulkhead ? 0.95f : d.Welded ? 0.65f : 0.5f;
            if (load >= blowAt)
            {
                d.JammedOpen = true; d.Welded = false; d.Locked = false; d.MotorBroken = true; d.Openness = 1f;
                d.Bent = 1f;
                _blown.Add(id);
                if (w.Body.DoorOf(d) is DoorBody db) { db.Gasket = 0f; }
                rec.BlownDoors.Add(id);
                Stats.DoorsBlown++;
                w.Log.Add(w.Tick, LogKind.Warning, $"폭발에 {d.RoomA?.Name ?? "?"}·{d.RoomB?.Name ?? "?"} 사이 문이 날아갔다");
                MarkLog.Add(d.RoomA?.Marks ?? d.RoomB!.Marks, w.Tick, "폭발에 문이 날아갔다");
            }
            else if (load >= bendAt)
            {
                float before = d.Bent;
                d.Bent = MathF.Min(0.95f, MathF.Max(d.Bent, 0.3f + (load - bendAt) * 2.2f));
                if (w.Body.DoorOf(d) is DoorBody db) db.Gasket = MathF.Min(db.Gasket, MathF.Max(0f, 1f - d.Bent * 1.1f));
                if (d.Bent > before + 0.05f) { rec.BentDoors.Add(id); Stats.DoorsBent++; }
            }
        }
        // 칸막이 벽: 세게 받으면 무너지며 일부를 넘긴다 (외벽 · 두꺼운 벽은 아니다)
        foreach (int i in _wallsTouched)
        {
            float load = _wl[i];
            if (load < 0.3f) continue;
            var cell = ship.Grid.CellAt(i);
            if (w.Body.WallAt(cell) is not { Thin: true, Hull: false }) continue;
            var dir = Cell.Dirs8[_wdir[i]];
            var beyond = cell + dir;
            if (ship.Grid.Kind(beyond) != TileKind.Floor) continue;
            Stats.ThinWalls++;
            seeds.Add((beyond, load * 0.3f, 2f + (cell.Center - rec.At.Center).Length()));
        }
        return seeds;
    }

    /// <summary>마지막 폭발의 압력파가 그 칸에 준 힘 (벽 칸은 벽이 받은 힘).</summary>
    public float PAt(Cell c)
    {
        var g = _w.Ship.Grid;
        if (!g.InBounds(c)) return 0f;
        int i = g.Index(c);
        return _field.TryGetValue(i, out var v) ? v : _wallField.TryGetValue(i, out var wv) ? wv : 0f;
    }

    /// <summary>미리 보기: 여기서 이 세기로 터지면 그 칸에 얼마나 오나 (대피 · 폭파 준비용 — 상태는 바꾸지 않는다).</summary>
    public Dictionary<int, float> Preview(Cell at, float power, BlastKind kind)
    {
        var spec = Spec(kind);
        float shock = power * spec.Shock * Medium(_w.Ship.RoomAt(at));
        Flood(at, MathF.Max(shock, power * 0.35f), MathF.Min(10f, 1.5f + 3.6f * power), sound: false, seeds: null);
        var map = new Dictionary<int, float>(_reached.Count);
        foreach (int i in _reached) map[i] = _v[i];
        return map;
    }

    /// <summary>그 방이 출렁이는 정도 (폭발 직후 몇 분).</summary>
    public float Slosh(Room r) => r.Id < _slosh.Length ? _slosh[r.Id] : 0f;

    // ───────────────────────────── 파편 ─────────────────────────────

    private void Fragments(BlastRecord rec, Cell at, float power, BlastSpec spec, string cause, Explosive? item)
    {
        var w = _w;
        int n = Math.Min(18, (int)MathF.Round(spec.Frags * (0.4f + power)));
        if (n <= 0) return;
        var hitMachines = new HashSet<Machine>();
        var hurt = new HashSet<CrewMember>();
        for (int k = 0; k < n; k++)
        {
            float ang = R.Float() * MathF.Tau;
            var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
            float s0 = power * (0.5f + 0.5f * R.Float()) * (rec.Kind == BlastKind.Charge ? 1.25f : 1f);
            float reach = 1.5f + 6.5f * s0;
            var shard = Shrapnel.Fly(w, R, at.Center + dir * 0.3f, dir, reach, s0, cause, hitMachines, hurt, this, item);
            rec.Shards.Add(shard);
            Stats.Shards++;
            if (shard.Hit != 0) Stats.ShardHits++;
            if (shard.Hit == 2) { rec.Breaches++; Stats.Breaches++; }
        }
        foreach (var c in hurt) if (!rec.Hurt.Contains(c.Id)) rec.Hurt.Add(c.Id);
        // 배터리: 셀이 튀어 떨어진 곳에 작은 불 · 신호탄: 불똥 · 가스 실린더 · 추진제: 통째로 로켓처럼 (긴 궤적 하나)
        if (rec.Kind is BlastKind.Battery or BlastKind.Flare)
            foreach (var s in rec.Shards.Take(rec.Kind == BlastKind.Battery ? 3 : 5))
            {
                var land = Cell.FromPosition(s.To);
                if (w.Ship.Grid.Kind(land) == TileKind.Floor && R.Chance(0.6f)) { w.Fire.Ignite(land, 0.3f); rec.Ignited.Add(land); }
            }
        if (rec.Kind is BlastKind.Gas or BlastKind.Propellant or BlastKind.Oxygen && power >= 0.3f)
        {
            float ang = R.Float() * MathF.Tau;
            var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
            var rocket = Shrapnel.Fly(w, R, at.Center, dir, 9f, power * 1.2f, $"{cause} — 통째로 날아갔다", hitMachines, hurt, this, item);
            rec.Shards.Add(rocket);
            if (rocket.Hit == 2) { rec.Breaches++; Stats.Breaches++; }
            foreach (var c in hurt) if (!rec.Hurt.Contains(c.Id)) rec.Hurt.Add(c.Id);
        }
    }

    // ───────────────────────────── 사람 ─────────────────────────────

    private void People(BlastRecord rec, Cell at, float power, BlastSpec spec, string cause, Room? room)
    {
        var w = _w;
        var ship = w.Ship;
        float fireR = 0.8f + 2.6f * power * MathF.Max(0f, spec.Heat);
        float steamR = 1f + 3f * power * spec.Steam;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Outside) continue;
            float p = PAt(c.Cell);
            float dist = (c.Position - at.Center).Length();
            bool reached = p > 0.02f || dist < 1.2f;
            if (!reached) continue;
            float suit = c.Suit != null ? 0.55f : 1f;
            bool hit = false;
            // 압력 (가까울수록 · 우주복이면 덜)
            if (p > 0.06f)
            {
                float dmg = (0.08f + 0.55f * p) * R.Range(0.7f, 1.1f) * suit;
                c.Vitals.Health = MathF.Max(0.02f, c.Vitals.Health - dmg);
                NeedsSystem.AddInjury(c.Vitals, dmg * 0.85f, $"폭발 — {spec.Name}");
                hit = true;
            }
            // 화구: 화상
            if (spec.Heat > 0f && dist < fireR && p > 0.03f)
            {
                float burn = 0.12f * spec.Heat * power * (1f - dist / (fireR + 0.5f)) * suit * 2f;
                if (burn > 0.01f)
                {
                    c.Vitals.Health = MathF.Max(0.02f, c.Vitals.Health - burn);
                    NeedsSystem.AddInjury(c.Vitals, burn, $"{spec.Name} 화상");
                    Stats.Burned++;
                    hit = true;
                }
            }
            // 증기: 데인다
            if (spec.Steam > 0f && dist < steamR && p > 0.03f)
            {
                float sc = 0.15f * spec.Steam * (1f - dist / (steamR + 0.5f)) * suit;
                if (sc > 0.01f) { NeedsSystem.AddInjury(c.Vitals, sc, "증기에 데었다"); Stats.Scalded++; hit = true; }
            }
            if (hit && !rec.Hurt.Contains(c.Id)) rec.Hurt.Add(c.Id);
            // 밀쳐냄: 바깥쪽으로 한 칸 · 세면 넘어진다
            float fallAt = 0.18f * (c.Suit != null ? 1.25f : 1f) * (c.IsChild ? 0.7f : 1f);
            if (p > fallAt * 0.75f)
            {
                var away = c.Position - at.Center;
                away = away.LengthSquared() < 0.01f ? new Vector2(R.Range(-1f, 1f), R.Range(-1f, 1f)) : away;
                var step = c.Position + Vector2.Normalize(away == Vector2.Zero ? Vector2.UnitX : away) * MathF.Min(1.6f, 0.6f + 2f * p);
                var sc2 = Cell.FromPosition(step);
                if (sc2 != c.Cell && ship.IsWalkable(sc2) && !ship.Rubble.ContainsKey(sc2) && ship.Grid.Kind(sc2) == TileKind.Floor)
                { c.Position = sc2.Center; c.PreviousPosition = c.Position; }
            }
            if (p > fallAt)
            {
                w.Body.KnockDown(c, $"{spec.Name} 압력에 넘어졌다", 0.02f + 0.05f * p);
                rec.Fell.Add(c.Id);
                Stats.Knocked++;
            }
            c.Interrupt(w);
            // 놀람 · 공포 (그 방을 꺼리게 된다) · 긴장
            float k = MathF.Min(1f, p * 1.4f + (hit ? 0.2f : 0f));
            if (c.Room != null) Memory.Frighten(w, c, room ?? c.Room, 0.25f + 0.55f * k, $"{Ko.IGa(spec.Name)} 났다");
            Memory.Shake(w, c, 0.05f + 0.15f * k, spec.Name);
            MarkLog.Add(c.Memory.Marks, w.Tick, hit ? $"폭발에 휘말렸다 ({cause})" : $"폭발을 곁에서 겪었다 ({cause})");
            c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.1f + 0.3f * k);
            Items.Experienced(c);
            if (hit) Life.Diary(w, c, $"{room?.Name ?? "?"}에서 {spec.Name}. 아직 손이 떨린다.");
        }
    }

    // ───────────────────────────── 감각 ─────────────────────────────

    private long[] _ringUntil = Array.Empty<long>(), _ringFrom = Array.Empty<long>(), _flashUntil = Array.Empty<long>(), _lateAt = Array.Empty<long>();
    private float[] _ringSev = Array.Empty<float>();
    private string[] _lateWhat = Array.Empty<string>();

    private void EnsureCrew(int id)
    {
        if (id < _ringUntil.Length) return;
        int n = Math.Max(id + 1, _ringUntil.Length * 2 + 8);
        int old = _ringUntil.Length;
        Array.Resize(ref _ringUntil, n); Array.Resize(ref _ringFrom, n); Array.Resize(ref _flashUntil, n); Array.Resize(ref _lateAt, n);
        Array.Resize(ref _ringSev, n); Array.Resize(ref _lateWhat, n);
        for (int i = old; i < n; i++) { _ringUntil[i] = -1; _ringFrom[i] = -1; _flashUntil[i] = -1; _lateAt[i] = -1; _lateWhat[i] = ""; }
    }

    private void Senses(BlastRecord rec, Cell at, float power, BlastSpec spec, Room? room)
    {
        var w = _w;
        var ship = w.Ship;
        float loud = MathF.Min(1.4f, power * spec.Loud * 1.3f + 0.1f);
        Flood(at, loud, 6f + 14f * MathF.Min(1f, loud), sound: true, seeds: null);
        var g = ship.Grid;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Outside) continue;
            int i = g.Index(c.Cell);
            float L = g.InBounds(c.Cell) && _stamp[i] == _gen ? _v[i] : 0f;
            if (L < 0.06f) continue; // 못 들었다 — 경보나 컴퓨터가 알려 주기 전에는 모른다
            EnsureCrew(c.Id);
            rec.Heard.Add(c.Id);
            c.Mind.Knows[$"blast:{rec.Id}"] = (KnowSource.Seen, w.Tick, $"{spec.Name} 소리 ({ship.Rooms.ElementAtOrDefault(rec.Room)?.Name ?? "?"})");
            bool here = c.Room != null && c.Room.Id == rec.Room;
            // 자던 사람: 들은 만큼 깬다
            if (c.Pose == Pose.Sleeping && L > 0.12f) { c.Jolt(w); Stats.Woke++; }
            // 벽 너머 쿵: 다른 방에서 들은 사람만 — 무슨 일인지 궁금해한다
            if (!here)
            {
                Stats.Thumps++;
                MarkLog.Add(c.Memory.Marks, w.Tick, L > 0.3f ? "가까이서 쾅 — 배가 흔들렸다" : "벽 너머에서 쿵 소리를 들었다");
                c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.12f * MathF.Min(1f, L * 2f));
                if (c.IsAwake && c.Job?.Urgent != true) c.NextThinkTick = Math.Min(c.NextThinkTick, w.Tick + 2);
                Items.Experienced(c, light: true);
            }
            // 이명: 아주 가까이 · 같은 방 (우주복 헬멧이면 덜)
            float ear = L * (c.Suit != null ? 0.5f : 1f);
            if (ear > 0.42f)
            {
                float sev = MathF.Min(1f, 0.25f + (ear - 0.42f) * 1.6f);
                long until = w.Tick + SimTime.Hours(1f + 7f * sev);
                if (until > _ringUntil[c.Id]) { _ringUntil[c.Id] = until; _ringFrom[c.Id] = w.Tick; _ringSev[c.Id] = MathF.Max(sev, Ringing(c)); }
                rec.Deafened.Add(c.Id);
                Stats.Deafened++;
                MarkLog.Add(c.Memory.Marks, w.Tick, "귀가 멍하다 — 삐 소리만 들린다");
            }
        }
        // 섬광: 같은 방이나 열린 문으로 본 사람 (압력파가 닿은 곳 = 보이는 곳)
        if (spec.Flash > 0.05f)
            foreach (var c in w.Crew)
            {
                if (c.Dead || c.Outside || !c.IsAwake) continue;
                float p = PAt(c.Cell);
                if (p <= 0.015f) continue;
                float d = (c.Position - at.Center).Length();
                float f = spec.Flash * power * MathF.Max(0f, 1f - d / 9f) * (c.Suit != null ? 0.4f : 1f);
                if (f < 0.12f) continue;
                EnsureCrew(c.Id);
                long until = w.Tick + SimTime.Minutes(4f + 30f * f);
                if (until > _flashUntil[c.Id]) _flashUntil[c.Id] = until;
                rec.Flashed.Add(c.Id);
                Stats.Flashed++;
            }
    }

    /// <summary>귀를 먹먹하게 한다 (시험 · 다른 소음 사고).</summary>
    public void Deafen(CrewMember c, float severity, float hours)
    {
        EnsureCrew(c.Id);
        _ringUntil[c.Id] = _w.Tick + SimTime.Hours(hours);
        _ringFrom[c.Id] = _w.Tick;
        _ringSev[c.Id] = Math.Clamp(severity, 0f, 1f);
    }

    /// <summary>이명 세기 0~1 (시간이 가며 잦아든다).</summary>
    public float Ringing(CrewMember c)
    {
        if (c.Id >= _ringUntil.Length || _ringUntil[c.Id] <= _w.Tick) return 0f;
        float total = MathF.Max(1f, _ringUntil[c.Id] - _ringFrom[c.Id]);
        return _ringSev[c.Id] * (_ringUntil[c.Id] - _w.Tick) / total;
    }

    /// <summary>귀가 울려 말 · 방송을 못 알아듣는다.</summary>
    public bool Deaf(CrewMember c) => Ringing(c) >= 0.3f;

    public bool Dazzled(CrewMember c) => c.Id < _flashUntil.Length && _flashUntil[c.Id] > _w.Tick;

    /// <summary>일 빠르기: 눈이 부시고 귀가 울리면 손이 더디다.</summary>
    public float WorkMul(CrewMember c)
    {
        if (c.Id >= _ringUntil.Length) return 1f;
        float m = 1f;
        if (Dazzled(c)) m *= 0.7f;
        float r = Ringing(c);
        if (r > 0f) m *= 1f - 0.2f * r;
        return m;
    }

    /// <summary>
    /// 경보 훅 (World.RaiseAlert): 귀가 울리는 사람은 경보를 늦게 듣는다 — 곁(같은 방 · 옆방)이면 경광등을 보고 조금 덜 늦게.
    /// 컴퓨터가 살아 있으면 손목 단말 · 방 불빛으로 다시 알려 덜 늦는다 (컴퓨터가 이명을 읽는다).
    /// </summary>
    public bool HearLate(CrewMember c, bool nearby, string text)
    {
        float r = Ringing(c);
        if (r < 0.25f || c.Dead) return false;
        var w = _w;
        bool computer = w.Automation.Present && w.Automation.MainOnline;
        float minutes = (3f + 18f * r) * (nearby ? 0.5f : 1f) * (computer ? 0.6f : 1f);
        long at = w.Tick + SimTime.Minutes(minutes);
        if (_lateAt[c.Id] < 0 || _lateAt[c.Id] > at) { _lateAt[c.Id] = at; _lateWhat[c.Id] = text; }
        return true;
    }

    /// <summary>방송 훅: 귀가 울리는 사람이 방송을 놓쳤다 → 컴퓨터가 알아채고 손목 단말로 다시 알린다.</summary>
    public void MissedBroadcast(CrewMember c, string text)
    {
        Stats.BroadcastMissed++;
        EnsureCrew(c.Id);
        long at = _w.Tick + SimTime.Minutes(2f + 6f * Ringing(c));
        if (_lateAt[c.Id] < 0 || _lateAt[c.Id] > at) { _lateAt[c.Id] = at; _lateWhat[c.Id] = "[방송] " + text; }
    }

    private void Late()
    {
        var w = _w;
        foreach (var c in w.Crew)
        {
            if (c.Id >= _lateAt.Length || _lateAt[c.Id] < 0 || _lateAt[c.Id] > w.Tick) continue;
            _lateAt[c.Id] = -1;
            if (c.Dead) continue;
            bool computer = w.Automation.Present && w.Automation.MainOnline;
            c.Interrupt(w);
            Stats.HeardLate++;
            if (computer && _lateWhat[c.Id].StartsWith("[방송]")) Stats.ComputerRelays++;
            w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.IGa(c.Name)} 뒤늦게 알아챘다 — 귀가 울려서 ({_lateWhat[c.Id]})" + (computer ? " · 컴퓨터가 손목 단말로 다시 알렸다" : ""), c.Id);
            c.Say(w, Persona.Say(c, "뭐? 경보였어? 귀가 멍해서…"));
        }
    }

    // ───────────────────────────── 물건 ─────────────────────────────

    private void Things(BlastRecord rec, Cell at, string cause)
    {
        var w = _w;
        var ship = w.Ship;
        // 보관함: 물건이 깨지고 흩어진다
        foreach (var f in ship.Containers)
        {
            float p = 0f;
            foreach (var fc in f.Cells) p = MathF.Max(p, PAt(fc));
            if (p < 0.2f) continue;
            var inv = f.Storage!;
            if (inv.Total == 0) continue;
            var kinds = inv.Contents.Select(x => x.kind).ToList();
            var kind = R.Pick(kinds);
            int lost = inv.Take(kind, R.Range(1, 2 + (int)(4 * p)));
            if (lost > 0) w.Log.Add(w.Tick, LogKind.Warning, $"폭발로 {f.Label}의 {ItemKinds.Name(kind)} {lost}개가 부서졌다");
        }
        // 놓인 개인 물건: 날아가고 상한다
        foreach (var b in w.Belongings.All)
        {
            if (b.At is not Cell bc || b.Holder >= 0) continue;
            float p = PAt(bc);
            if (p < 0.22f) continue;
            var away = bc.Center - at.Center;
            var to = Cell.FromPosition(bc.Center + (away.LengthSquared() < 0.01f ? Vector2.UnitX : Vector2.Normalize(away)) * (1f + 2f * p));
            if (ship.IsOpenFloor(to)) b.At = to;
            b.Condition = MathF.Max(0f, b.Condition - 0.5f * p);
            MarkLog.Add(b.Marks, w.Tick, $"폭발에 날아갔다 ({cause})");
            Stats.Flung++;
        }
        // 이동식 장비: 밀려 나가고(설치가 풀린다) 세게 맞으면 망가진다
        foreach (var d in w.Portable.Devices)
        {
            if (!d.Placed) continue;
            float p = PAt(d.At);
            if (p < 0.28f) continue;
            var away = d.At.Center - at.Center;
            var to = Cell.FromPosition(d.At.Center + (away.LengthSquared() < 0.01f ? Vector2.UnitY : Vector2.Normalize(away)) * (1f + 2.5f * p));
            if (ship.IsOpenFloor(to) && to != d.At) { d.At = to; d.CableTo = null; Stats.Flung++; }
            if (p > 0.45f && R.Chance(p)) d.Broken = true;
        }
    }

    // ───────────────────────────── 불 · 공기 ─────────────────────────────

    private void Heat(BlastRecord rec, Cell at, float power, BlastSpec spec, Room? room, string cause)
    {
        var w = _w;
        var ship = w.Ship;
        if (spec.Heat < 0f)
        {
            // 소화기 파열: 분말이 곁의 불을 덮는다
            int before = w.Fire.Count;
            w.Fire.Suppress(at, 2.5f + 2f * power, 1f);
            if (w.Fire.Count < before) Stats.Suppressed++;
        }
        else if (spec.Heat > 0f)
        {
            float fireR = 0.8f + 2.6f * power * spec.Heat;
            float o2 = room != null && room.Air.O2 > 22.5f ? 1.4f : 1f; // 짙은 산소: 불이 세다
            for (int dy = -(int)fireR - 1; dy <= (int)fireR + 1; dy++)
                for (int dx = -(int)fireR - 1; dx <= (int)fireR + 1; dx++)
                {
                    var c = new Cell(at.X + dx, at.Y + dy);
                    float d = (c.Center - at.Center).Length();
                    if (d > fireR || ship.Grid.Kind(c) != TileKind.Floor || PAt(c) <= 0.015f && d > 0.6f) continue;
                    float wet = w.Body.Mark(c, CellMark.Wet), oil = w.Body.Mark(c, CellMark.Oil);
                    float ch = spec.Heat * power * (1f - d / (fireR + 0.5f)) * 1.3f * o2 * (1f - 0.7f * wet) * (1f + oil);
                    if (R.Chance(MathF.Min(0.95f, ch)) && w.Fire.Ignite(c, MathF.Min(0.8f, 0.25f + 0.5f * ch))) rec.Ignited.Add(c);
                }
            // 불기둥 · 셀 분출: 몇 분 동안 그 자리를 달군다
            if (spec.Heat >= 0.5f && rec.Kind != BlastKind.Dust)
            {
                Jets.Add((at, w.Tick + SimTime.Minutes(2f + 10f * power * spec.Heat), power * spec.Heat * 2.5f, rec.Kind));
                if (Jets.Count > 12) Jets.RemoveAt(0);
            }
            // 분진: 노란 불길이 방 안 가루를 타고 번진다
            if (rec.Kind == BlastKind.Dust && room != null)
                foreach (var c in room.Cells)
                    if (ship.Grid.Kind(c) == TileKind.Floor && R.Chance(0.25f * power) && w.Fire.Ignite(c, 0.35f)) rec.Ignited.Add(c);
        }
        if (room == null) return;
        float vol = MathF.Max(8f, room.Volume);
        room.Air.Smoke = MathF.Min(1f, room.Air.Smoke + 0.5f * power * spec.Smoke + 0.05f * power);
        room.Air.Temperature = MathF.Min(95f, room.Air.Temperature + 25f * power * MathF.Max(0f, spec.Heat) + 12f * power * spec.Steam - 15f * power * spec.Cold);
        room.Air.CO = MathF.Min(1f, room.Air.CO + 0.15f * power * MathF.Max(0f, spec.Heat));
        if (spec.Toxin > 0f) room.Air.Toxin = MathF.Min(1f, room.Air.Toxin + 20f * spec.Toxin * power / vol);
        if (spec.O2 > 0f) room.Air.O2 = MathF.Min(40f, room.Air.O2 + 60f * spec.O2 * power / vol * 3f); // 산소통이 쏟아낸 산소 — 방이 짙어진다
        if (spec.Cold > 0f)
            foreach (var wc in rec.Wave)
                if (wc.P > 0.1f) w.Body.RaiseMark(new Cell(wc.X, wc.Y), CellMark.Frost, MathF.Min(1f, wc.P * 2f * spec.Cold), $"{spec.Name} — 서리");
        if (rec.Kind == BlastKind.Ferment)
            foreach (var wc in rec.Wave)
                if (wc.P > 0.05f) w.Body.RaiseMark(new Cell(wc.X, wc.Y), CellMark.Wet, MathF.Min(1f, 0.3f + wc.P * 3f), "발효 국물이 튀었다");
        if (rec.Kind == BlastKind.Ferment) w.Smells.Emit(room, SmellKind.Foul, 0.6f);
        else if (spec.Heat > 0.3f) w.Smells.Emit(room, SmellKind.Burnt, 0.4f * power);
    }

    private int Rubble(BlastRecord rec, Cell at, float power, BlastKind kind, Explosive? item)
    {
        var w = _w;
        var ship = w.Ship;
        int rubble = 0;
        foreach (var wc in rec.Wave)
        {
            var c = new Cell(wc.X, wc.Y);
            if (wc.D < 0.5f || wc.P < 0.12f) continue;
            var tk = ship.Grid.Kind(c);
            if (tk != TileKind.Floor && tk != TileKind.Door) continue;
            if (tk == TileKind.Floor && ship.FurnitureAt(c) != null) continue;
            if (kind == BlastKind.Charge && item?.Aim is Cell aim && (c.Center - aim.Center).Length() < 2.2f) continue; // 겨눈 쪽은 뚫으려던 것
            if (!R.Chance(0.75f * wc.P)) continue;
            float amount = MathF.Min(1f, 0.35f + wc.P);
            ship.Rubble[c] = MathF.Max(ship.Rubble.GetValueOrDefault(c), amount);
            if (ship.DoorAt(c) is Door door && amount >= 0.5f) door.Blocked = true;
            rubble++;
            foreach (var cm in w.Crew)
            {
                if (cm.Cell != c || cm.Dead) continue;
                var free = Cell.Dirs8.Select(dd => c + dd).FirstOrDefault(x => ship.IsWalkable(x) && !ship.Rubble.ContainsKey(x));
                if (free != default) { cm.Position = free.Center; cm.PreviousPosition = cm.Position; }
            }
        }
        w.Volatile.Stats.RubbleCells += rubble;
        return rubble;
    }

    // ───────────────────────────── 흔적 ─────────────────────────────

    private void Traces(BlastRecord rec, Cell at, float power, BlastSpec spec, string cause)
    {
        var w = _w;
        var ship = w.Ship;
        var scar = new BlastScar { Record = rec.Id, At = at, Kind = rec.Kind, Power = power, Tick = w.Tick, Room = rec.Room, Reach = MathF.Min(6f, 1f + 3.5f * power) };
        // 방사형 그을음: 줄기마다 압력파를 따라가다 벽 · 닫힌 문에 막히면 짧다
        int rays = 10 + (int)(14 * power);
        float sootK = spec.Heat > 0f ? 1f : rec.Kind is BlastKind.Steam or BlastKind.Ferment or BlastKind.Powder or BlastKind.ColdGas or BlastKind.Refrigerant ? 0.25f : 0.6f;
        for (int k = 0; k < rays; k++)
        {
            float ang = (k + R.Range(-0.35f, 0.35f)) / rays * MathF.Tau;
            var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
            float len = 0f;
            for (float t = 0.5f; t <= scar.Reach; t += 0.5f)
            {
                var c = Cell.FromPosition(at.Center + dir * t);
                float p = PAt(c);
                if (ship.Grid.Kind(c) != TileKind.Floor || p < 0.04f) break;
                len = t;
                if (sootK > 0.2f) w.Body.RaiseMark(c, CellMark.Soot, MathF.Min(1f, (0.25f + p * 1.6f) * sootK * (1f - t / (scar.Reach + 1f))), $"{spec.Name} 그을음");
            }
            scar.Rays.Add((ang, len / scar.Reach * R.Range(0.75f, 1f)));
        }
        // 조명: 천장 등이 깨지고 유리가 떨어진다 → 반 넘게 깨지면 그 방이 어둡다
        var lostIn = new Dictionary<int, (int lost, int all)>();
        foreach (var wc in rec.Wave)
        {
            var c = new Cell(wc.X, wc.Y);
            if (!ship.Grid.InBounds(c) || (w.Body.Ceiling[ship.Grid.Index(c)] & CeilingFlags.Light) == 0) continue;
            var rr = ship.RoomAt(c);
            if (rr == null) continue;
            lostIn.TryGetValue(rr.Id, out var lc);
            if (wc.P > 0.25f && R.Chance(MathF.Min(0.9f, wc.P * 1.5f)))
            {
                scar.Lights.Add(c);
                Stats.Lights++;
                w.Body.RaiseMark(c, CellMark.Glass, MathF.Min(1f, 0.45f + wc.P), "깨진 조명");
                lc.lost++;
            }
            lc.all++;
            lostIn[rr.Id] = lc;
        }
        foreach (var rid in lostIn.Keys.OrderBy(x => x))
        {
            var (lost, all) = lostIn[rid];
            var rr = ship.Rooms[rid];
            int total = rr.Cells.Count(c => (w.Body.Ceiling[ship.Grid.Index(c)] & CeilingFlags.Light) != 0);
            if (lost > 0 && lost * 2 >= Math.Max(1, total)) w.Fixtures.LightsFail(rr, "폭발에 조명이 깨졌다");
        }
        if (rec.Room >= 0) MarkLog.Add(ship.Rooms[rec.Room].Marks, w.Tick, $"{spec.Name} 자국 ({cause})");
        Scars.Add(scar);
        if (Scars.Count > 30) Scars.RemoveAt(0);
    }

    // ───────────────────────────── 시스템 틱 ─────────────────────────────

    private long _nextSlow;

    public void Update(float dt)
    {
        var w = _w;
        // 방 출렁임이 잦아든다
        for (int i = 0; i < _slosh.Length; i++) _slosh[i] *= 0.6f;
        // 불기둥: 그 칸이 계속 탄다
        Jets.RemoveAll(j => j.until <= w.Tick);
        foreach (var j in Jets) if (w.Fire.At(j.at) < 0.3f && w.Ship.Grid.Kind(j.at) == TileKind.Floor) w.Fire.Ignite(j.at, 0.4f);
        // 날아간 문: 문짝이 없으니 열린 채 — 문틀을 펴면(배 손보기) 다시 단다
        foreach (int id in _blown.ToList())
        {
            if (id >= w.Ship.Doors.Count) { _blown.Remove(id); continue; }
            var d = w.Ship.Doors[id];
            if (d.Removed) { _blown.Remove(id); continue; }
            if (d.Bent < 0.3f)
            {
                _blown.Remove(id);
                d.JammedOpen = false;
                w.Log.Add(w.Tick, LogKind.Work, $"{d.RoomA?.Name ?? "?"}·{d.RoomB?.Name ?? "?"} 사이 날아간 문을 다시 달았다");
                continue;
            }
            d.JammedOpen = true;
        }
        Late();
        Items.Update(dt);
        if (w.Tick < _nextSlow) return;
        _nextSlow = w.Tick + SimTime.Minutes(10);
        Aftermath();
    }

    /// <summary>폭발 뒤 (10분마다): 흔적이 옅어지고, 죽은 사람이 있으면 추모 자리가 된다.</summary>
    private void Aftermath()
    {
        var w = _w;
        var ship = w.Ship;
        foreach (var s in Scars)
        {
            float age = (w.Tick - s.Tick) / (float)SimTime.TicksPerDay;
            float soot = w.Body.Mark(s.At, CellMark.Soot);
            // 며칠 남는다 — 닦으면(배 손보기) 빨리 옅어진다
            s.Fade = Math.Clamp(MathF.Max(soot * 1.2f, 1f - age / 6f) * (age > 9f ? 0f : 1f), 0f, 1f);
            if (soot < 0.05f && age > 2f) s.Fade = MathF.Min(s.Fade, MathF.Max(0f, 1f - (age - 2f) / 2f));
        }
        Scars.RemoveAll(s => s.Fade <= 0.01f && !s.Memorial);
        foreach (var rec in Recent)
        {
            if (rec.Mourned) continue;
            var dead = rec.Hurt.Select(id => w.Crew.FirstOrDefault(c => c.Id == id)).Where(c => c != null && c.Dead).ToList();
            if (dead.Count == 0) continue;
            rec.Mourned = true;
            Stats.Memorials++;
            if (Scars.FirstOrDefault(s => s.Record == rec.Id) is BlastScar sc) sc.Memorial = true;
            var room = rec.Room >= 0 ? ship.Rooms[rec.Room] : null;
            w.History.Add(w, HistoryKind.Memory, $"{room?.Name ?? "?"}의 폭발 자리 — {Ko.EulReul(string.Join("·", dead.Select(c => c!.Name)))} 기억하는 자리가 되었다", room, dead.Cast<CrewMember>(), rec.At);
        }
    }

    // ───────────────────────────── 조사 · 책임 ─────────────────────────────

    /// <summary>현장을 살핀 사람이 원인을 짚는다: 무엇이 무엇을 터뜨렸나 · 누가 그 물건을 거기 두었나.</summary>
    internal void Investigate(BlastRecord rec, CrewMember c)
    {
        var w = _w;
        if (rec.Investigated) return;
        rec.Investigated = true;
        rec.Investigator = c.Id;
        Stats.Investigations++;
        var room = rec.Room >= 0 ? w.Ship.Rooms[rec.Room] : null;
        var chain = Recent.Where(r => r.Room == rec.Room && Math.Abs(r.Tick - rec.Tick) < SimTime.Minutes(20)).OrderBy(r => r.Tick).Select(r => r.Spec.Name).Distinct().ToList();
        string finding = chain.Count > 1 ? $"{string.Join(" → ", chain)} 순으로 번졌다" : $"{rec.Spec.Name} ({rec.Cause})";
        var blamed = rec.Blame >= 0 ? w.Crew.FirstOrDefault(x => x.Id == rec.Blame) : null;
        if (blamed != null)
        {
            finding += $" — {Ko.IGa(blamed.Name)} {rec.BlameWhy}";
            Stats.Blamed++;
            blamed.Needs.Stress = MathF.Min(1f, blamed.Needs.Stress + 0.2f);
            Life.Diary(w, blamed, Persona.Say(blamed, $"내가 {rec.BlameWhy.Replace("두었다", "둔")} 탓이다"));
            foreach (int id in rec.Hurt)
                if (w.Crew.FirstOrDefault(x => x.Id == id) is CrewMember v && v != blamed) v.ChangeAffinity(blamed, -0.06f);
        }
        rec.Finding = finding;
        w.History.Add(w, HistoryKind.Lesson, $"폭발 조사({c.Name}): {finding}", room, new[] { c }, rec.At);
        Life.Diary(w, c, $"{room?.Name ?? "?"} 폭발 자리를 살폈다. {finding}.");
        foreach (var o in w.Crew)
            if (!o.Dead && (rec.Heard.Contains(o.Id) || rec.Hurt.Contains(o.Id)))
                o.Mind.Knows[$"blastcause:{rec.Id}"] = (KnowSource.Rumor, w.Tick, finding);
        // 알게 된 것은 행동으로: 그 방의 위험한 배치를 손본다
        if (room != null) Items.SafetySweep(room, $"{c.Name}의 폭발 조사");
    }

    // ───────────────────────────── 지문 ─────────────────────────────

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Stats.Detonations); I(Stats.Chains); I(Stats.Knocked); I(Stats.Deafened); I(Stats.HeardLate); I(Stats.Investigations);
        I(Recent.Count); I(Scars.Count); I(_blown.Count); I(Jets.Count);
        foreach (var r in Recent) { I(r.Tick); I(r.At.X); I(r.At.Y); F(r.Power); I((int)r.Kind); I(r.Hurt.Count); I(r.Shards.Count); }
        Items.Hash(I, F);
    }
}

/// <summary>
/// 파편 판정 한 벌 — 운석 파편 원뿔(Incidents)과 폭발 파편이 같은 함수를 쓴다.
/// </summary>
public static class Shrapnel
{
    private static readonly Cell[] Ring = Cell.Dirs8.Append(new Cell(0, 0)).ToArray();
    private static readonly Cell[] Center = { new(0, 0) };

    /// <summary>파편이 한 칸(ring이면 둘레까지)을 훑는다: 설비 · 칸막이 벽 · 문 구동기 · 조명 · 배 전체 망 · 인과 사슬.</summary>
    public static void Sweep(World w, Rng rng, Cell cell, float strength, bool ring, HashSet<Machine> hitMachines, string label)
    {
        var ship = w.Ship;
        foreach (var d in ring ? Ring : Center)
        {
            if (ship.FurnitureAt(cell + d)?.Machine is Machine m && hitMachines.Add(m)) HitMachine(w, rng, m, strength, label);
            if (ship.WallAt(cell + d) is WallState ws && !ws.IsHull)
                Hull.Damage(ship, cell + d, 0.15f * strength);
            if (d.X == 0 && d.Y == 0) w.Fixtures.OnDebris(cell, strength, ship.RoomAt(cell)); // v9.4 문 구동기 · 조명
            if (d.X == 0 && d.Y == 0) w.Net.DamageNear(cell, 1.1f, 0.55f * strength, label); // 배 전체 망
            if (d.X == 0 && d.Y == 0) w.Causes.Hit(ship.RoomAt(cell), w.Causes.Context); // v12.2 파편이 지나간 방
            if (d.X == 0 && d.Y == 0 && w.Blast.Items.At(cell) is Explosive ex) w.Blast.Items.OnShard(ex, strength, label); // v16.13 폭발성 물건 (연쇄)
        }
    }

    /// <summary>파편이 설비를 맞혔다: 수명 · 전선 · 관 · 고장.</summary>
    public static void HitMachine(World w, Rng rng, Machine m, float strength, string label)
    {
        strength *= Durability.ShardOnMachine(m); // v16.19 강화 외함 · 설비 내구
        m.Condition = MathF.Max(0.02f, m.Condition - 0.5f * strength);
        Procedures.DamageLinks(w, m, 0.7f * strength, 0.6f * strength, label); // v12.1
        if (rng.Chance(0.7f * strength)) w.Machines.Break(m);
    }

    /// <summary>파편이 사람을 맞혔다 (우주복이면 덜 · 경보를 듣고 숙였으면 덜).</summary>
    public static float HitCrew(World w, Rng rng, CrewMember c, float baseDmg, float mul, string cause, string mark)
    {
        float dmg = baseDmg * rng.Range(0.6f, 1f) * (c.Suit != null ? 0.5f : 1f) * mul;
        c.Vitals.Health = MathF.Max(0.02f, c.Vitals.Health - dmg);
        NeedsSystem.AddInjury(c.Vitals, dmg * 0.9f, cause);
        c.Interrupt(w);
        w.Log.Add(w.Tick, LogKind.Warning, $"파편에 맞아 다쳤다 (체력 {c.Vitals.Health * 100:0}%)", c.Id);
        MarkLog.Add(c.Memory.Marks, w.Tick, mark);
        return dmg;
    }

    /// <summary>
    /// 폭발 파편 하나: 곧게 날다 맞은 것에서 힘을 잃는다 — 사람(다침) · 설비 · 폭발성 물건(연쇄) · 닫힌 문 · 칸막이 벽(멈춤) · 외벽(뚫리면 감압).
    /// </summary>
    public static Shard Fly(World w, Rng rng, Vector2 from, Vector2 dir, float reach, float strength, string cause, HashSet<Machine> hitMachines, HashSet<CrewMember> hurt, BlastSystem blast, Explosive? self)
    {
        var ship = w.Ship;
        float s = strength;
        var p = from;
        Cell last = Cell.FromPosition(from);
        for (float t = 0.35f; t <= reach && s > 0.04f; t += 0.35f)
        {
            p = from + dir * t;
            var cell = Cell.FromPosition(p);
            if (cell == last) { s *= 0.995f; continue; }
            last = cell;
            var kind = ship.Grid.Kind(cell);
            if (kind == TileKind.Void) return new Shard(from, p, strength, 0);
            if (kind == TileKind.Wall)
            {
                var ws = ship.WallAt(cell);
                bool hull = ws?.IsHull == true;
                bool was = ws != null && hull && Hull.EffectiveBreach(ws) > 0f;
                Hull.Damage(ship, cell, s * (hull ? 0.95f : 0.5f));
                bool now = ws != null && hull && Hull.EffectiveBreach(ws) > 0f;
                if (now && !was) MarkLog.Add(ws!.Marks, w.Tick, $"폭발 파편이 뚫었다 ({cause})");
                return new Shard(from, p, strength, (byte)(now && !was ? 2 : 1));
            }
            if (kind == TileKind.Door && ship.DoorAt(cell) is Door door && door.Openness < 0.5f && !door.JammedOpen && !door.Removed)
            {
                w.Fixtures.OnDebris(cell, s, ship.RoomAt(cell));
                return new Shard(from, p, strength, 6);
            }
            if (blast.Items.At(cell, self) is Explosive ex)
            {
                blast.Items.OnShard(ex, s, cause);
                s *= 0.5f;
                if (s < 0.08f) return new Shard(from, p, strength, 5);
            }
            if (ship.FurnitureAt(cell) is Furniture f)
            {
                if (f.Machine is Machine m && hitMachines.Add(m)) HitMachine(w, rng, m, s, cause);
                s *= 0.45f;
                if (s < 0.08f) return new Shard(from, p, strength, 4);
            }
            foreach (var c in w.Crew)
            {
                if (c.Dead || c.Outside || (c.Position - p).LengthSquared() > 0.45f * 0.45f || hurt.Contains(c)) continue;
                hurt.Add(c);
                HitCrew(w, rng, c, 0.1f + 0.4f * s, 1f, $"폭발 파편 ({cause})", $"폭발 파편에 맞았다 ({c.Room?.Name ?? "?"})");
                if (rng.Chance(0.6f)) return new Shard(from, p, strength, 3);
                s *= 0.5f;
            }
        }
        return new Shard(from, p, strength, 0);
    }
}
