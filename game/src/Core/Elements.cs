using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v16.4 칸 단위 원소 장 (주변 몇 칸만 · 1분 간격): 온도(뜨거운 설비 · 히터 · 불 곁 · 차가운 외벽) · 전기 전도(젖은 · 금속 바닥 · 젖은 러그) ·
// 국소 공기 흐름(문틈 · 환기구 — 연기 · 가루 · 가벼운 물건). 방 전체는 Atmosphere · Ambience가 맡고, 여기는 위험 곁 몇 칸만 자세히.
// 원소가 만나면 Matter 표대로: 물 + 전기 = 누전 · 감전 · 산소 + 불꽃 = 폭발적 · 흩날린 가루 + 불꽃 = 분진 폭발.
// 그을리는 물건의 연기 → 감지기 → 주 컴퓨터가 문을 닫아 연기를 가둔다 → 탄 냄새 → 사람이 찾아와 치운다.
// 주 컴퓨터는 위험한 조합(히터 옆 젖은 천 · 산소 + 불꽃 · 누설 전류 · 분진)을 읽고 경고하고, 승무원은 그 경고와 눈에 보이는 것을 보고 움직인다.

public sealed partial class MatterSystem
{
    private readonly Dictionary<int, float> _heat = new();
    private readonly Dictionary<int, float> _live = new();
    private readonly SortedDictionary<int, float> _liveSeen = new();
    private readonly Dictionary<int, Vector2> _draft = new();
    private readonly SortedDictionary<int, float> _dust = new();
    private readonly List<(Cell at, float lph, string why, long until)> _drips = new();
    private readonly HashSet<int> _alarmed = new();
    private readonly List<(int room, int door)> _sealed = new();
    private int _watchTurn;

    /// <summary>화면 · 시험: 칸 온도 차 · 전기 · 바람 · 가루.</summary>
    public IReadOnlyDictionary<int, float> HeatField => _heat;
    public IReadOnlyDictionary<int, float> LiveField => _live;
    public IReadOnlyDictionary<int, Vector2> DraftField => _draft;
    public IReadOnlyDictionary<int, float> DustField => _dust;
    public IReadOnlyList<(Cell at, float lph, string why, long until)> Drips => _drips;
    public bool SmokeSealed(Room r) { foreach (var s in _sealed) if (s.room == r.Id) return true; return false; }

    /// <summary>그 칸의 온도 (방 공기 + 곁의 열원 · 찬 벽).</summary>
    public float CellTemp(Cell c, Room? room = null)
    {
        room ??= _w.Ship.RoomAt(c);
        float t = room?.Air.Temperature ?? -100f;
        return _heat.TryGetValue(Idx(c), out var d) ? t + d : t;
    }

    public float HeatAt(Cell c) => _heat.TryGetValue(Idx(c), out var d) ? d : 0f;
    public float LiveAt(Cell c) => _live.TryGetValue(Idx(c), out var v) ? v : 0f;
    public float DustAt(Cell c) => _dust.TryGetValue(Idx(c), out var v) ? v : 0f;
    public Vector2? DraftAt(Cell c) => _draft.TryGetValue(Idx(c), out var v) ? v : null;

    /// <summary>시험 · 사건: 그 칸 천장에서 물이 샌다 (L/시간).</summary>
    public void Drip(Cell at, float litersPerHour, string why, long hours = 24) => _drips.Add((at, litersPerHour, why, _w.Tick + SimTime.Hours(hours)));

    // ───────────────────────────── 틱 ─────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        if (!_seeded) Seed();
        if (w.Tick < _nextMinute) return;
        _nextMinute = w.Tick + SimTime.Minutes(1);
        long pf = Prof.Now;
        float h = SimTime.Minutes(1) / (float)SimTime.TicksPerHour;
        if (_has.Length != w.Ship.Grid.CellCount) Reindex();
        Release();
        Fields();
        Drips_(h);
        UpdateSpills(h);
        UpdateUnder(h);
        UpdateThings(h);
        UpdateJunctions(h);
        LiveField_();
        Shocks(h);
        UpdateDust(h);
        Oxygen(h);
        Notice();
        SmokeWatch();
        if (++_watchTurn % 2 == 0) ComputerWatch();
        foreach (var c in w.Crew) if (!c.Dead && c.Pose == Pose.Working && MatAt(c.Cell)) Stats.MatWorks++;
        Prof.Lap("sys.Matter", pf);
    }

    // ───────────────────────────── 장: 온도 · 바람 ─────────────────────────────

    private void Fields()
    {
        var w = _w;
        var ship = w.Ship;
        _heat.Clear();
        void AddHeat(Cell c0, float center, float ring1, float ring2)
        {
            var room = ship.RoomAt(c0);
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    var c = new Cell(c0.X + dx, c0.Y + dy);
                    if (!ship.Grid.InBounds(c) || ship.Grid.Kind(c) != TileKind.Floor) continue;
                    if (room != null && ship.RoomAt(c) != room) continue; // 벽 너머로는 안 간다
                    int r = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    float v = r == 0 ? center : r == 1 ? ring1 : ring2;
                    int i = ship.Grid.Index(c);
                    _heat[i] = (_heat.TryGetValue(i, out var o) ? o : 0f) + v;
                }
        }
        // 히터: 겉판은 150℃ 가까이, 곁 한 칸은 수건이 그을 만큼
        foreach (var d in w.Portable.Devices)
            if (d.Kind == PortableKind.Heater && d.Running && d.Placed) AddHeat(d.At, 150f * d.Supply, 85f * d.Supply, 22f * d.Supply);
        // 불
        if (w.Fire.Count > 0)
            foreach (var (c, v) in w.Fire.Fires) AddHeat(c, 250f * v, 90f * v, 25f * v);
        // 뜨거운 설비 (달아오른 기계)
        foreach (var f in ship.Furniture)
            if (f.Machine is Machine m && m.Heat > 0.35f && !f.Room.Detached && f.Cells.Count > 0) AddHeat(f.UseSpots.Count > 0 ? f.UseSpots[0] : f.Cells[0], 70f * m.Heat, 25f * m.Heat, 6f * m.Heat);
        // 차가운 외벽 (단열재가 상한 곳) · 관측창
        foreach (var wb in w.Body.WallList)
        {
            if (!wb.Hull || wb.Insulation >= 0.5f && !wb.Window) continue;
            float cold = wb.Insulation < 0.5f ? -(0.5f - wb.Insulation) * 40f : -4f;
            foreach (var d in Cell.Dirs4)
            {
                var c = wb.Cell + d;
                if (!ship.Grid.InBounds(c) || ship.Grid.Kind(c) != TileKind.Floor) continue;
                int i = ship.Grid.Index(c);
                _heat[i] = (_heat.TryGetValue(i, out var o) ? o : 0f) + cold;
            }
        }
        // 냉각수 웅덩이는 차갑다
        foreach (var (i, s) in Spills) if (s.Coolant) _heat[i] = (_heat.TryGetValue(i, out var o) ? o : 0f) - 12f;

        // 바람: 열린 문틈 (기압 차 · 연기 차) · 환기구 (덕트 쪽으로) — 연기 · 가루 · 가벼운 물건이 있는 방만
        _draft.Clear();
        bool any = _dust.Count > 0 || Gravity < 0.2f;
        foreach (var t in Things) if (t.Spec.Light && t.CarriedBy < 0) { any = true; break; }
        if (!any) foreach (var r in ship.Rooms) if (r.Air.Smoke > 0.02f) { any = true; break; }
        if (!any) return;
        foreach (var d in ship.Doors)
        {
            if (d.Removed || d.IsExternal || d.Openness < 0.3f || d.RoomA is not Room a || d.RoomB is not Room b || a.Detached || b.Detached) continue;
            var dir = d.ConnectsVertically ? new Vector2(0, 1) : new Vector2(1, 0);
            var step = d.ConnectsVertically ? new Cell(0, 1) : new Cell(1, 0);
            if (ship.RoomAt(d.Cell + step) != b) { dir = -dir; step = new Cell(-step.X, -step.Y); }
            float m = Math.Clamp((a.Air.Pressure - b.Air.Pressure) / 15f, -2f, 2f) + 0.4f * MathF.Sign(a.Air.Smoke - b.Air.Smoke) * MathF.Min(1f, MathF.Abs(a.Air.Smoke - b.Air.Smoke) * 10f);
            if (MathF.Abs(m) < 0.05f) continue;
            for (int k = -2; k <= 2; k++)
            {
                var c = new Cell(d.Cell.X + step.X * k, d.Cell.Y + step.Y * k);
                if (!ship.Grid.InBounds(c)) continue;
                int i = ship.Grid.Index(c);
                var v = dir * m * d.Openness * (k == 0 ? 1f : MathF.Abs(k) == 1 ? 0.6f : 0.3f);
                _draft[i] = (_draft.TryGetValue(i, out var o) ? o : Vector2.Zero) + v;
            }
        }
        foreach (var room in ship.Rooms)
        {
            if (room.Detached || !Atmosphere.Vented(room) || room.Air.Smoke < 0.02f && !RoomHasLight(room)) continue;
            foreach (var c in room.Cells)
            {
                int i = ship.Grid.Index(c);
                if ((w.Body.Ceiling.Length > i ? w.Body.Ceiling[i] : CeilingFlags.None).HasFlag(CeilingFlags.Duct)) continue;
                // 가장 가까운 덕트 칸 쪽으로 (방 가운데 줄)
                var toward = room.MaxX - room.MinX >= room.MaxY - room.MinY ? new Vector2(0, MathF.Sign((room.MinY + room.MaxY) / 2 - c.Y)) : new Vector2(MathF.Sign((room.MinX + room.MaxX) / 2 - c.X), 0);
                if (toward == Vector2.Zero) continue;
                _draft[i] = (_draft.TryGetValue(i, out var o) ? o : Vector2.Zero) + toward * 0.12f;
            }
        }
    }

    private bool RoomHasLight(Room room)
    {
        foreach (var t in Things) if (t.Spec.Light && t.CarriedBy < 0 && _w.Ship.RoomAt(t.At) == room) return true;
        return false;
    }

    // ───────────────────────────── 새는 관 → 물 ─────────────────────────────

    private void Drips_(float h)
    {
        var w = _w;
        var ship = w.Ship;
        // 새는 관: 샌 물은 방 바닥(Moisture)에 고이고, 떨어지는 그 자리 몇 칸은 여기서 자세히
        foreach (var s in w.Piping.Segments)
        {
            if (s.Sound || s.Closed || s.Path.Count == 0) continue;
            var at = s.LeakAt;
            if (!ship.Grid.InBounds(at)) continue;
            if (ship.Grid.Kind(at) != TileKind.Floor)
            {
                bool found = false;
                foreach (var d in Cell.Dirs4) if (ship.Grid.InBounds(at + d) && ship.Grid.Kind(at + d) == TileKind.Floor) { at += d; found = true; break; }
                if (!found) continue;
            }
            float lph = MathF.Min(8f, (1f - s.Integrity) * (s.IsCoolant ? 5f : 8f));
            Pour(at, Material.Liquid, lph * h, s.IsCoolant ? "냉각수 샘" : "새는 관", coolant: s.IsCoolant);
        }
        for (int k = _drips.Count - 1; k >= 0; k--)
        {
            var (at, lph, why, until) = _drips[k];
            if (w.Tick > until) { _drips.RemoveAt(k); continue; }
            Pour(at, Material.Liquid, lph * h, why);
        }
    }

    // ───────────────────────────── 바닥 아래 접속부 · 전기 ─────────────────────────────

    private void Wet(Junction j, float amount, string why)
    {
        float before = j.Wet;
        j.Wet = MathF.Min(1f, j.Wet + amount * (j.Taped ? 0.3f : 1f)); // 감아 둔 접속부는 덜 젖는다
        if (before < 0.25f && j.Wet >= 0.25f) { Stats.JunctionsWet++; if (!j.Taped) j.Fixed = false; }
    }

    /// <summary>러그를 걷거나 밀어내자 그 아래가 드러났다 — 젖은 배선 접속부.</summary>
    internal void Expose(Article rug, Junction j, CrewMember? by)
    {
        var w = _w;
        if (j.Known || j.Wet < 0.1f) { if (j.Wet < 0.1f && by != null) j.Suspected = false; return; }
        j.Known = true;
        j.Suspected = false;
        Stats.JunctionsFound++;
        var room = w.Ship.RoomAt(j.At);
        string who = by != null ? Ko.IGa(by.Name) : "누군가";
        w.Log.Add(w.Tick, LogKind.Warning, $"{who} {rug.Name}를 걷자 그 아래 젖은 배선 접속부가 드러났다 — 물이 스며 {(j.Live ? "불꽃이 튄다" : "축축하다")}", by?.Id ?? -1);
        if (room != null) MarkLog.Add(room.Marks, w.Tick, $"{rug.Name} 아래 젖은 배선 접속부 발견" + (by != null ? $" ({by.Name})" : ""));
        if (by != null) MarkLog.Add(by.Memory.Marks, w.Tick, $"{room?.Name} 러그 아래 젖은 접속부를 찾았다");
        w.History.Add(w, HistoryKind.Incident, $"{room?.Name ?? "?"} — 젖은 러그 아래 숨은 배선 접속부 (젖음 {j.Wet * 100:0}%)", room, by != null ? new[] { by } : null, j.At);
        w.Board.RequestScan();
    }

    private void UpdateJunctions(float h)
    {
        var w = _w;
        var ship = w.Ship;
        foreach (var j in Junctions)
        {
            if (j.Room < 0 || j.Room >= ship.Rooms.Count) continue;
            var room = ship.Rooms[j.Room];
            if (room.Detached) { j.Live = false; continue; }
            float depth = MoistureSystem.Depth(room);
            if (depth > 0.06f) Wet(j, depth * 3f * h, "바닥 물");
            var rug = RugAt(j.At);
            bool fed = depth > 0.06f || UnderWet(j.At) > 0.05f;
            if (!fed && j.Wet > 0f) j.Wet = MathF.Max(0f, j.Wet - (rug == null ? 0.35f : 0.03f) * h * (1f + MathF.Max(0f, HeatAt(j.At)) / 40f));
            j.Live = j.Wet > 0.25f && !j.Taped && room.Powered && !room.BreakerOff;
            if (!j.Live) continue;
            // 불꽃: 젖은 접속부가 튄다
            if (R.Chance(MathF.Min(0.9f, 0.35f * j.Wet)))
            {
                j.LastSpark = w.Tick;
                Stats.Sparks++;
                Spark(j.At, room, rug != null ? $"{rug.Name} 아래 젖은 접속부" : "젖은 배선 접속부");
            }
            // 이따금 회로가 단락된다 (원인은 보이지 않는다 — 차단기만 떨어진다)
            if (R.Chance(0.02f * j.Wet) && ship.FurnitureOf(FurnitureType.PowerPanel).FirstOrDefault()?.Machine is Machine panel && !panel.Faults.Any(f => f.Circuit == room.Circuit))
            {
                panel.Faults.Add(new Fault { Kind = FaultKind.ShortCircuit, Since = w.Tick, Circuit = room.Circuit });
                panel.FaultCount++;
                w.Causes.OnFault(panel, panel.Faults[^1]);
                Stats.Shorts++;
                w.Log.Add(w.Tick, LogKind.Warning, $"{room.Name} {PowerGrid.CircuitName(room.Circuit)} 회로 단락 — 원인이 보이지 않는다");
                foreach (var jj in Junctions) if (jj.Room == j.Room && !jj.Known) jj.Suspected = true;
            }
        }
    }

    /// <summary>전기 불꽃 한 번: 곁의 마른 천 · 흩날린 가루 · 짙은 산소가 반응한다 (표: 전기 × 그것).</summary>
    public void Spark(Cell at, Room room, string why)
    {
        var w = _w;
        // 가루: 분진 폭발
        if (DustAt(at) >= 0.3f || Cell.Dirs4.Any(d => DustAt(at + d) >= 0.4f)) { DustBlast(at, $"전기 불꽃 ({why})"); return; }
        // 마른 천 · 종이: 그을기 시작
        if (Any(at))
            foreach (var t in At(at))
                if (Matter.Ignitability(t.Mat, t.WetFrac, room.Air.O2) > 0.3f && t.Char < 1f) { t.Char = MathF.Min(1f, t.Char + 0.12f); t.LastSpark = w.Tick; if (!t.Smolder) { t.Smolder = true; Stats.Smolders++; } }
        // 짙은 산소: 불꽃이 곧 불 (표: 전기 × 산소 = 폭발적)
        if (room.Air.O2 > 25f)
        {
            float o = Matter.Pair(Element.Electric, Element.Oxygen).Rate;
            if (w.Fire.Ignite(at, MathF.Min(0.9f, 0.25f + (room.Air.O2 - 25f) * 0.04f * o)))
            {
                Stats.OxygenFlashes++;
                w.Log.Add(w.Tick, LogKind.Warning, $"{room.Name} 산소 {room.Air.O2:0}kPa — {why}의 불꽃이 순식간에 불이 됐다");
            }
            if (room.Air.O2 > 35f) w.Blast.Detonate(at, MathF.Min(0.5f, 0.1f + (room.Air.O2 - 35f) * 0.012f), BlastKind.Oxygen, "짙은 산소에 전기 불꽃");
        }
    }

    /// <summary>전기 장: 살아 있는 젖은 접속부 · 물에 젖은 이동식 장비 케이블에서 젖은 · 금속 바닥을 타고 몇 칸 번진다 (고무 매트에서 끊긴다).</summary>
    private void LiveField_()
    {
        var w = _w;
        var ship = w.Ship;
        _live.Clear();
        _liveSeen.Clear();
        var q = new Queue<(Cell c, float v)>();
        void Seed_(Cell c, float v)
        {
            float cond = Conduct(c);
            float s = v * MathF.Max(0.35f, cond);
            if (s < 0.15f) return;
            int i = ship.Grid.Index(c);
            if (_live.TryGetValue(i, out var o) && o >= s) return;
            _live[i] = s;
            q.Enqueue((c, s));
        }
        foreach (var j in Junctions) if (j.Live) Seed_(j.At, j.Wet);
        foreach (var d in w.Portable.Devices) if (d.Running && d.Placed && d.Soak > 0.6f) Seed_(d.At, d.Soak - 0.3f);
        while (q.Count > 0)
        {
            var (c, v) = q.Dequeue();
            var room = ship.RoomAt(c);
            foreach (var d in Cell.Dirs4)
            {
                var n = c + d;
                if (!ship.Grid.InBounds(n) || ship.Grid.Kind(n) != TileKind.Floor || ship.RoomAt(n) != room) continue;
                float s = v * Conduct(n) * 0.85f;
                if (s < 0.15f) continue;
                int i = ship.Grid.Index(n);
                if (_live.TryGetValue(i, out var o) && o >= s) continue;
                _live[i] = s;
                q.Enqueue((n, s));
            }
        }
        foreach (var (i, v) in _live)
            if (v >= 0.4f && !UnderRug(ship.Grid.CellAt(i))) _liveSeen[i] = v; // 젖은 바닥 위로 파란 불꽃이 보인다
    }

    /// <summary>그 칸이 전기를 통하는 정도: 바닥재 × 젖음 · 고인 물 · 젖은 러그 — 고무 매트는 0.</summary>
    public float Conduct(Cell c)
    {
        var b = _w.Body;
        if (MatAt(c)) return 0f;
        float wet = MathF.Max(b.Mark(c, CellMark.Wet), MathF.Min(1f, LitersAt(c)));
        float v = Matter.Conductivity(b.FloorAt(c), wet);
        if (RugAt(c) is Article rug) v = MathF.Max(Matter.Conductivity(rug.Mat, rug.WetFrac), UnderWet(c) > 0.2f ? 0.6f : 0f);
        return v;
    }

    /// <summary>감전 배율 (Moisture가 부른다): 고무 매트 위는 거의 없다 · 바닥재 표 (고무 바닥은 덜).</summary>
    public float ShockMul(CrewMember c) => MatAt(c.Cell) ? 0.03f : Matter.ShockMul(_w.Body.FloorAt(c.Cell));

    private void Shocks(float h)
    {
        var w = _w;
        if (_live.Count == 0) return;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.Outside || c.Suit != null || c.Room == null) continue;
            float v = LiveAt(c.Cell);
            if (v < 0.25f) continue;
            if (!R.Chance(MathF.Min(0.8f, 0.3f * v))) continue;
            float dmg = 0.04f + 0.1f * v;
            c.Vitals.Health = MathF.Max(0.02f, c.Vitals.Health - dmg);
            NeedsSystem.AddInjury(c.Vitals, dmg * 0.7f, "감전");
            c.Interrupt(w);
            Stats.LiveShocks++;
            var room = c.Room;
            bool hidden = Junctions.Any(j => j.Live && !j.Known && j.Room == room.Id);
            w.Causes.Effect(CauseKind.Shock, "", $"{c.Name} 감전 ({room.Name} 젖은 바닥)", room, c.Position, lasting: false);
            w.Log.Add(w.Tick, LogKind.Warning, hidden ? $"{room.Name} 바닥을 밟자 찌릿했다 — 어딘가 전기가 샌다" : $"{room.Name} 젖은 바닥에서 감전됐다", c.Id);
            MarkLog.Add(c.Memory.Marks, w.Tick, $"{room.Name} 젖은 바닥에서 찌릿");
            Memory.Shake(w, c, 0.05f, "젖은 바닥에서 감전됐다");
            foreach (var j in Junctions) if (j.Room == room.Id && j.Live && !j.Known) j.Suspected = true; // 어딘가 숨은 게 있다 — 걷어 봐야 안다
        }
    }

    // ───────────────────────────── 가루 · 분진 폭발 ─────────────────────────────

    /// <summary>가루가 피어오른다 (포대가 터짐 · 흔들림).</summary>
    public void Raise(Cell c, float v, string why)
    {
        int i = Idx(c);
        if (i < 0) return;
        if (!_dust.ContainsKey(i)) Stats.DustClouds++;
        _dust[i] = MathF.Min(1f, MathF.Max(_dust.TryGetValue(i, out var o) ? o : 0f, v));
        foreach (var d in Cell.Dirs4)
        {
            var n = c + d;
            if (!_w.Ship.Grid.InBounds(n) || _w.Ship.Grid.Kind(n) != TileKind.Floor) continue;
            int j = _w.Ship.Grid.Index(n);
            _dust[j] = MathF.Min(1f, MathF.Max(_dust.TryGetValue(j, out var p) ? p : 0f, v * 0.6f));
        }
        var room = _w.Ship.RoomAt(c);
        if (room != null) MarkLog.Add(room.Marks, _w.Tick, $"가루가 피어올랐다 ({why})");
    }

    private void UpdateDust(float h)
    {
        if (_dust.Count == 0) return;
        var w = _w;
        var ship = w.Ship;
        var grid = ship.Grid;
        var next = new SortedDictionary<int, float>();
        foreach (var (i, v) in _dust)
        {
            var c = grid.CellAt(i);
            var room = ship.RoomAt(c);
            if (room == null || room.Detached) continue;
            float keep = v;
            // 바람을 타고 흐른다
            if (DraftAt(c) is Vector2 dv && dv.Length() > 0.1f)
            {
                var n = Cell.FromPosition(c.Center + Vector2.Normalize(dv));
                if (grid.InBounds(n) && grid.Kind(n) == TileKind.Floor) { float m = v * MathF.Min(0.5f, dv.Length() * 0.4f); keep -= m; next[grid.Index(n)] = (next.TryGetValue(grid.Index(n), out var o) ? o : 0f) + m; }
            }
            // 퍼진다 (같은 방)
            if (v > 0.15f)
                foreach (var d in Cell.Dirs4)
                {
                    var n = c + d;
                    if (!grid.InBounds(n) || grid.Kind(n) != TileKind.Floor || ship.RoomAt(n) != room) continue;
                    float m = v * 0.06f;
                    keep -= m;
                    next[grid.Index(n)] = (next.TryGetValue(grid.Index(n), out var o) ? o : 0f) + m;
                }
            // 가라앉아 바닥 · 방 오염으로 (Soil)
            float settle = MathF.Min(keep, (0.5f + (Atmosphere.Vented(room) ? 0.4f : 0f)) * h);
            keep -= settle;
            w.Soil.RoomSoil(room)[(int)SoilKind.Dust] = MathF.Min(1f, w.Soil.RoomSoil(room)[(int)SoilKind.Dust] + settle * 0.6f * 10f / MathF.Max(4, room.Cells.Count));
            next[i] = (next.TryGetValue(i, out var s) ? s : 0f) + keep;
        }
        _dust.Clear();
        foreach (var (i, v) in next) if (v > 0.02f) _dust[i] = MathF.Min(1f, v);
        // 불씨: 불 · 그을리는 것 · 켜진 히터 곁 · 접속부 불꽃
        foreach (var (i, v) in _dust.ToList())
        {
            if (v < 0.3f || !_dust.ContainsKey(i)) continue;
            var c = grid.CellAt(i);
            bool spark = w.Fire.Count > 0 && (w.Fire.At(c) > 0f || w.Fire.AnyWithin(c, 1.2f)) || HeatAt(c) > 60f;
            if (!spark && Any(c)) foreach (var t in At(c)) if (t.Smolder) { spark = true; break; }
            if (spark) DustBlast(c, HeatAt(c) > 60f ? "뜨거운 히터 곁" : "불씨");
        }
    }

    private void DustBlast(Cell at, string why)
    {
        var w = _w;
        float total = 0f;
        var cells = new List<int>();
        foreach (var (i, v) in _dust)
        {
            var c = w.Ship.Grid.CellAt(i);
            if ((c.Center - at.Center).LengthSquared() > 6.5f) continue;
            total += v;
            cells.Add(i);
        }
        foreach (var i in cells) _dust.Remove(i);
        if (total < 0.2f) return;
        Stats.DustBlasts++;
        float o2 = w.Ship.RoomAt(at)?.Air.O2 ?? 21f;
        float power = MathF.Min(0.8f, (0.12f + 0.12f * total) * (o2 > 21f ? MathF.Min(1.6f, o2 / 21f) : 1f) * Matter.Rule(Material.Powder, Element.Fire).Rate);
        w.Log.Add(w.Tick, LogKind.Warning, $"흩날린 가루에 불이 닿았다 — 분진 폭발 ({why})");
        w.Blast.Detonate(at, power, BlastKind.Dust, $"분진 폭발 ({why})");
    }

    // ───────────────────────────── 산소 + 불꽃 ─────────────────────────────

    private void Oxygen(float h)
    {
        var w = _w;
        foreach (var d in w.Portable.Devices)
        {
            if (d.Kind != PortableKind.Heater || !d.Running || !d.Placed) continue;
            var room = w.Ship.RoomAt(d.At);
            if (room == null || room.Air.O2 < 28f) continue;
            // 짙은 산소에서는 히터 열선이 곧 불씨다 (표: 열 × 산소 = 불을 키운다)
            if (R.Chance(MathF.Min(0.8f, (room.Air.O2 - 28f) * 0.04f * Matter.Pair(Element.Heat, Element.Oxygen).Rate * 2f)) && w.Fire.Ignite(d.At, 0.35f))
            {
                Stats.OxygenFlashes++;
                w.Log.Add(w.Tick, LogKind.Warning, $"{room.Name} 산소 {room.Air.O2:0}kPa — 히터 열선에서 불이 붙었다");
            }
        }
    }

    // ───────────────────────────── 보이는 것 · 연기 감지 · 문 ─────────────────────────────

    private bool[] _awake = Array.Empty<bool>();

    private void Notice()
    {
        var w = _w;
        int n = w.Ship.Rooms.Count;
        if (_awake.Length < n) _awake = new bool[n + 4]; else Array.Clear(_awake);
        foreach (var c in w.Crew) if (!c.Dead && c.IsAwake && c.Room is Room r && r.Id < _awake.Length) _awake[r.Id] = true;
        foreach (var t in Things)
        {
            if (t.CarriedBy >= 0) continue;
            var r = w.Ship.RoomAt(t.At);
            if (r != null && r.Id < _awake.Length && _awake[r.Id]) t.Known = true;
        }
    }

    private readonly List<int> _smoky = new();

    private void SmokeWatch()
    {
        var w = _w;
        var ship = w.Ship;
        _smoky.Clear();
        foreach (var t in Things)
            if (t.CarriedBy < 0 && (t.Smolder || t.Melt > 0f && t.Temp > Matter.MeltPoint(t.Mat)) && ship.RoomAt(t.At) is Room r && !_smoky.Contains(r.Id)) _smoky.Add(r.Id);
        _smoky.Sort();
        foreach (int rid in _smoky)
        {
            var room = ship.Rooms[rid];
            if (room.Detached || w.Fire.CountIn(room) > 0 || room.Air.Smoke < 0.04f) continue; // 불이면 화재 계통이 맡는다
            if (!room.Powered || !w.Automation.AlarmsIn(room)) continue; // 감지기가 없으면 냄새로 누가 알아챌 때까지
            if (_alarmed.Add(rid))
            {
                Stats.SmokeAlarms++;
                w.RaiseAlert($"{room.Name} 연기 감지 — 불꽃은 없다 · 무언가 그을린다", room, AlertLevel.Warning, shipWide: false);
                w.Automation.Book.Add(ActKind.Alarm, room, $"{room.Name} 연기 감지기 · 연기 {room.Air.Smoke * 100:0}% · 불꽃 없음", "그을음 (불이 붙기 전)", "연기 경보", "그을리는 것을 찾아 치워라", $"smoke:{rid}", SimTime.Minutes(30), 10f);
                MarkLog.Add(room.Marks, w.Tick, "연기 감지기 — 그을음");
                w.Board.RequestScan();
            }
            if (!SmokeSealed(room) && w.Automation.AutoDoorsIn(room))
            {
                int n = 0;
                foreach (var d in room.Doors)
                {
                    if (d.IsExternal || d.Removed || !d.Powered || d.Locked || d.JammedOpen || d.Welded) continue;
                    d.Locked = true;
                    _sealed.Add((rid, d.Id));
                    n++;
                }
                if (n > 0)
                {
                    Stats.DoorSeals++;
                    w.Automation.Book.Add(ActKind.Door, room, $"{room.Name} 연기 {room.Air.Smoke * 100:0}%", "연기가 옆방으로 번지지 않게", $"문 {n}개를 닫아 연기를 가둔다", "그을리는 것을 찾아 치워라", $"smokedoor:{rid}", SimTime.Minutes(30), 10f);
                    w.Automation.Reason($"smokedoor:{rid}", $"{room.Name} 연기 감지 (불꽃 없음) → 문 {n}개를 닫아 연기를 가뒀다 · 요청: 그을리는 것을 찾아 치워라", SimTime.Minutes(30));
                    w.Log.Add(w.Tick, LogKind.Ship, $"주 컴퓨터가 {room.Name} 문 {n}개를 닫았다 — 연기를 가둔다");
                }
            }
        }
        // 연기가 걷히면 다시 연다
        for (int k = _sealed.Count - 1; k >= 0; k--)
        {
            var (rid, did) = _sealed[k];
            var room = ship.Rooms[rid];
            if (_smoky.Contains(rid) || room.Air.Smoke >= 0.03f || w.Fire.CountIn(room) > 0) continue; // 연기가 걷힐 때까지 닫아 둔다
            if (did < ship.Doors.Count && ship.Doors[did] is Door d && d.Locked && !room.Lockdown && !(d.RoomA?.Lockdown ?? false) && !(d.RoomB?.Lockdown ?? false)) d.Locked = false;
            _sealed.RemoveAt(k);
            if (!SmokeSealed(room)) { Stats.Unseals++; _alarmed.Remove(rid); w.Log.Add(w.Tick, LogKind.Ship, $"{room.Name} 연기가 걷혔다 — 주 컴퓨터가 문을 다시 열었다"); }
        }
        foreach (var rid in _alarmed.ToList()) if (!_smoky.Contains(rid) && ship.Rooms[rid].Air.Smoke < 0.03f) _alarmed.Remove(rid);
    }

    // ───────────────────────────── 주 컴퓨터가 읽는 위험한 조합 ─────────────────────────────

    private void ComputerWatch()
    {
        var w = _w;
        var au = w.Automation;
        if (!au.Present || !au.MainOnline) return;
        var ship = w.Ship;
        // ① 히터 옆 젖은 천 · 종이 (마르면 그을린다)
        foreach (var d in w.Portable.Devices)
        {
            if (d.Kind != PortableKind.Heater || !d.Running || !d.Placed) continue;
            var room = ship.RoomAt(d.At);
            if (room == null || !room.DataLinked) continue;
            foreach (var t in Things)
            {
                if (t.CarriedBy >= 0 || t.Flagged || Math.Max(Math.Abs(t.At.X - d.At.X), Math.Abs(t.At.Y - d.At.Y)) > 1) continue;
                if (Matter.React(t.Mat, Element.Heat) is not (Reaction.Char or Reaction.Melt) || t.Char >= 1f) continue;
                t.Flagged = true;
                Stats.ComputerWarns++;
                string wet = t.WetFrac > 0.2f ? "젖은 " : "";
                string rule = Matter.Rule(t.Mat, Element.Heat).Text;
                au.Book.Add(ActKind.Advice, room, $"{room.Name} 히터 곁 {wet}{t.Name} ({Materials.Name(t.Mat)})", $"예측: {rule}", "경고", $"{t.Name}를 히터에서 치워라", $"heatcloth:{t.Id}", SimTime.Hours(2), 20f);
                au.Reason($"heatcloth:{t.Id}", $"예측: {room.Name} 히터 곁 {wet}{t.Name} — {(t.WetFrac > 0.2f ? "마르고 나면 " : "")}그을다 연기가 난다 ({Materials.Name(t.Mat)} + 열 = {Matter.Name(Matter.React(t.Mat, Element.Heat))}) · 요청: 치워라", SimTime.Hours(2));
            }
        }
        // ② 짙은 산소 + 불씨 (켜진 히터 · 살아 있는 접속부 · 젖은 케이블)
        foreach (var room in ship.LiveRooms)
        {
            if (room.Air.O2 < 25f || !room.DataLinked) continue;
            string? src = null;
            foreach (var d in w.Portable.Devices) if (d.Running && d.Placed && ship.RoomAt(d.At) == room && (d.Kind == PortableKind.Heater || d.Soak > 0.5f)) { src = d.Name; break; }
            if (src == null) foreach (var j in Junctions) if (j.Live && j.Room == room.Id) { src = "누설 전류"; break; }
            if (src == null && w.Fire.CountIn(room) > 0) src = "불";
            if (src == null) continue;
            if (au.Book.Add(ActKind.Alarm, room, $"{room.Name} 산소 {room.Air.O2:0}kPa · 불씨: {src}", $"예측: {Matter.Pair(Element.Fire, Element.Oxygen).Text}", "경보", $"{src}를 꺼라 · 환기", $"o2spark:{room.Id}", SimTime.Hours(1), 10f) != null)
            {
                Stats.O2Warns++;
                Stats.ComputerWarns++;
                w.RaiseAlert($"{room.Name} 산소 {room.Air.O2:0}kPa에 불씨({src}) — 불이 나면 폭발적이다", room, AlertLevel.Warning, shipWide: true);
                au.Reason($"o2spark:{room.Id}", $"예측: {room.Name} 산소 {room.Air.O2:0}kPa · {src} — 산소 + 불꽃 = 폭발적 · 요청: {src} 끄기 · 환기", SimTime.Hours(1));
            }
        }
        // ③ 누설 전류 (보이지 않는 젖은 접속부)
        foreach (var j in Junctions)
        {
            if (!j.Live || j.Known || j.Room < 0) continue;
            var room = ship.Rooms[j.Room];
            if (!room.DataLinked) continue;
            if (au.Book.Add(ActKind.Advice, room, $"{room.Name} {PowerGrid.CircuitName(room.Circuit)} 회로 누설 전류", "원인 추정: 바닥 아래 젖은 접속부", "경고", "젖은 깔개를 걷고 바닥 아래를 점검하라", $"leak:{room.Id}", SimTime.Hours(2), 30f) != null)
            {
                Stats.LeakWarns++;
                Stats.ComputerWarns++;
                j.Suspected = true;
                au.Reason($"leak:{room.Id}", $"{room.Name} 회로에 누설 전류 — 원인 추정: 바닥 아래 젖은 접속부 · 요청: 젖은 깔개를 걷고 점검", SimTime.Hours(2));
            }
        }
        // ④ 흩날린 가루 + 불씨
        foreach (var (i, v) in _dust)
        {
            if (v < 0.3f) continue;
            var room = ship.RoomAt(ship.Grid.CellAt(i));
            if (room == null || !room.DataLinked) continue;
            if (au.Book.Add(ActKind.Alarm, room, $"{room.Name} 공기 중 가루 {v * 100:0}%", $"예측: {Matter.Rule(Material.Powder, Element.Fire).Text}", "경보", "불씨를 치우고 가라앉을 때까지 기다려라", $"dust:{room.Id}", SimTime.Hours(1), 10f) != null)
            {
                Stats.DustWarns++;
                Stats.ComputerWarns++;
                w.RaiseAlert($"{room.Name} 가루가 흩날린다 — 불씨가 닿으면 분진 폭발", room, AlertLevel.Warning, shipWide: false);
            }
        }
    }
}
