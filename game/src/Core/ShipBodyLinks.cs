using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.3 배 본체 ↔ 주 컴퓨터 · 승무원 · 다른 시스템.
// 주 컴퓨터는 배 본체의 감지기만 읽는다 — 점검 뚜껑 스위치 · 바닥 물 감지기 · 문 너머 압력 · 표시판. 감지기가 없는 것(유리 조각 · 기름)은 모른다.
//   판단 · 경고 · 제안은 다섯 칸 기록(관측 · 판단 · 조치 · 요청 · 결과)으로 남고, 방송은 들은 사람만 안다 (세계 ≠ 사람이 아는 것).
// 승무원은 직접 본 것(옆 사람이 미끄러지는 것)과 들은 것(방송)으로 조심한다 — 젖은 바닥에서 뛰지 않고, 고장 난 표시판을 믿지 않는다.
// 다른 시스템과 양방향: 조리(기름 튐 · 탄 냄비 그을음) · 엎지른 국(젖은 칸) · 넘어지며 쏟은 음식 · 이동식 장비(히터 · 선풍기가 바닥을 말린다 /
//   카트 바퀴가 격자에 걸리고 젖은 바닥에서 미끄러지고 바닥을 닳게 한다) · 오래 젖은 카펫 곰팡이 → 균(오염) → 악취(냄새) · 그을음 닦기 → 방 오염이 준다.

public sealed partial class BodySystem
{
    /// <summary>조심하는 사람: 사람 → (방, 그때까지, 왜).</summary>
    private readonly Dictionary<int, (int room, long until, string why)> _caution = new();
    /// <summary>표시판이 고장 났다는 방송을 들은 사람: 사람 → 문 번호들.</summary>
    private readonly Dictionary<int, HashSet<int>> _knowsBadIndicator = new();
    private readonly Dictionary<int, long> _wetAnnounced = new();
    /// <summary>컴퓨터가 걸레질을 요청한 방 → 그때까지.</summary>
    private readonly Dictionary<int, long> _mopRequest = new();
    private readonly Dictionary<int, List<long>> _fallsIn = new();
    private int _scorchSeen;

    /// <summary>이 사람이 이 방에서 조심해서 걷는다 (방송을 들었거나 누가 미끄러지는 걸 봤다).</summary>
    public bool Cautious(CrewMember c, Room? r) => r != null && _caution.TryGetValue(c.Id, out var x) && x.room == r.Id && x.until > _w.Tick;
    public string? CautionWhy(CrewMember c) => _caution.TryGetValue(c.Id, out var x) && x.until > _w.Tick ? x.why : null;
    public bool MopRequested(Room r) => _mopRequest.TryGetValue(r.Id, out var t) && t > _w.Tick;

    private void BeCareful(CrewMember c, Room r, float hours, string why, BeliefSource src = BeliefSource.Seen, float conf = 1f)
    {
        if (c.Dead || c.IsChild && c.Age < 6f) return;
        _caution[c.Id] = (r.Id, _w.Tick + SimTime.Hours(hours), why);
        _w.Brain2.Beliefs.Learn(c, Topic.Slip, r.Id, 1, src, conf); // v16 통합: 본 것 · 들은 것은 믿음 장부로 (가서 마른 바닥을 보면 고친다 · 알리러 간다)
    }

    /// <summary>문 너머 표시판 (사람마다): 고장 방송을 들은 사람은 표시판을 믿지 않고 실제 위험을 안다.</summary>
    public string? Reading(Door d, string? real, CrewMember c)
    {
        if (_knowsBadIndicator.TryGetValue(c.Id, out var set) && set.Contains(d.Id)) return real;
        return Reading(d, real);
    }

    /// <summary>들고 미는 카트 (없으면 null).</summary>
    private PortableDevice? CartOf(CrewMember c)
    {
        var devs = _w.Portable.Devices;
        for (int i = 0; i < devs.Count; i++)
            if (devs[i].Kind == PortableKind.Cart && devs[i].HeldBy == c) return devs[i];
        return null;
    }

    // ───────────────────────────── 주 컴퓨터가 읽는다 (1분마다) ─────────────────────────────

    private void ComputerWatch()
    {
        var w = _w;
        var au = w.Automation;
        if (!au.Present || !au.MainOnline) return;
        var ship = w.Ship;
        var grid = ship.Grid;
        long now = w.Tick;

        // 1) 점검 뚜껑 스위치: 일이 끝났는데 열려 있다 → 잊은 것 · 닫기 요청 (손보기가 먼저 한다)
        foreach (var h in Hatches)
        {
            if (h.Flagged || !h.Forgotten) continue;
            if (ship.RoomAt(h.Cell) is not Room room || !room.DataLinked) continue;
            h.Flagged = true;
            Stats.ComputerFlags++;
            var cell = h.Cell;
            au.Book.Add(ActKind.Advice, room, $"{room.Name} 점검 뚜껑 스위치 '열림' {(now - h.Since) / SimTime.Minutes(1)}분 · 그 칸에 작업 없음",
                "작업이 끝났는데 열려 있다 — 닫는 걸 잊은 것", "발밑 조심 방송", "가까운 사람이 닫아 달라", "hatch:" + grid.Index(cell), SimTime.Hours(1), 40f,
                (world, act) => world.Body.HatchOpenAt(cell) ? (-1, "아직 열려 있다") : (1, "닫혔다"));
            au.Speak.Announce(au.Voice.Style($"{room.Name} 점검 뚜껑이 열려 있다 — 발밑 조심"), room, 1);
        }

        // 2) 바닥 물 감지기: 미끄러운 바닥이 젖은 방 → "뛰지 마라" 방송 · 걸레질 요청 — 들은 사람만 조심한다
        Dictionary<int, int>? wet = null;
        foreach (var (i, s) in Marks)
        {
            if (s.V[(int)CellMark.Wet] < 0.35f || Materials.Of(Floor[i]).WetSlip < 0.4f) continue;
            if (ship.RoomAt(grid.CellAt(i)) is not Room r || r.Detached || !r.DataLinked) continue;
            wet ??= new();
            wet[r.Id] = wet.TryGetValue(r.Id, out var n) ? n + 1 : 1;
        }
        if (wet != null)
            foreach (var (rid, n) in wet.OrderBy(kv => kv.Key))
            {
                if (n < 2 || _wetAnnounced.TryGetValue(rid, out var t) && now - t < SimTime.Hours(3)) continue;
                var room = ship.Rooms[rid];
                if (MoistureSystem.Depth(room) > 0.004f) continue; // 침수는 침수 대응이 맡는다
                _wetAnnounced[rid] = now;
                _mopRequest[rid] = now + SimTime.Hours(4);
                Stats.ComputerWarnings++;
                var mat = Materials.Name(Floor[grid.Index(room.Cells[room.Cells.Count / 2])]);
                au.Book.Add(ActKind.Advice, room, $"{room.Name} 바닥 물 감지 {n}칸 ({mat})", $"젖은 {mat} — 뛰면 미끄러진다", "뛰지 말라는 방송", "걸레질",
                    "wetfloor:" + rid, SimTime.Hours(3), 90f, (world, act) => (world.Body.WetCells(room) < 2 ? 1 : 2, world.Body.WetCells(room) < 2 ? "말랐다" : "아직 젖어 있다"));
                var b = au.Speak.Announce(au.Voice.Style($"{room.Name} 바닥이 젖었다 — 뛰지 말고 걸어라"), room, 1);
                if (b != null)
                    foreach (var id in b.HeardBy)
                        if (CrewById(id) is CrewMember c) BeCareful(c, room, 3f, "방송을 들었다", BeliefSource.Broadcast, 0.3f + 0.7f * au.Trusts.Of(c));
            }

        // 3) 문: 닫힌 문 너머로 압력이 조금씩 샌다 → 패킹 노화 추정 · 정비 요청 / 표시판이 압력 감지기와 다르다 → 표시판을 믿지 말라
        foreach (var db in Doors)
        {
            if (db.Door >= ship.Doors.Count) continue;
            var d = ship.Doors[db.Door];
            if (d.Removed || d.RoomA is not Room ra || d.RoomB is not Room rb || ra.Detached || rb.Detached) continue;
            if (!db.Flagged && (db.Whistling || db.Gasket < 0.25f && MathF.Abs(ra.Air.Pressure - rb.Air.Pressure) > 2f) && (ra.DataLinked || rb.DataLinked))
            {
                db.Flagged = true;
                Stats.ComputerFlags++;
                au.Book.Add(ActKind.Advice, ra, $"{ra.Name}·{rb.Name} 사이 닫힌 문 너머로 압력이 샌다 ({MathF.Abs(ra.Air.Pressure - rb.Air.Pressure):0}kPa 차)",
                    "문 패킹이 삭았다고 추정", "정비 요청을 올렸다", "패킹 교체", "gasket:" + db.Door, SimTime.Hours(6), 240f,
                    (world, act) => db.Gasket >= 0.25f ? (1, "패킹을 갈았다") : (2, "아직 새는 중"));
            }
            if (db.IndicatorBroken && !db.IndicatorWarned)
            {
                // 표시판은 '정상'인데 너머 압력 감지기가 진공 (또는 그 반대) — 컴퓨터는 압력 감지기를 믿는다
                Room? vac = ra.Air.Pressure < 30f && rb.Air.Pressure > 60f ? ra : rb.Air.Pressure < 30f && ra.Air.Pressure > 60f ? rb : null;
                if (vac == null || !db.IndicatorSaysSafe || !vac.DataLinked) continue;
                db.IndicatorWarned = true;
                db.Flagged = true;
                Stats.ComputerWarnings++;
                au.Book.Add(ActKind.Advice, vac, $"{ra.Name}·{rb.Name} 문 표시판 '정상' ↔ {vac.Name} 압력 감지기 {vac.Air.Pressure:0}kPa",
                    "표시판이 고장 — 압력 감지기를 믿는다", "열지 말라는 방송", "표시판 수리", "indicator:" + db.Door, SimTime.Hours(6), 60f,
                    (world, act) => (1, "표시판 대신 감지기를 믿었다"));
                var b = au.Speak.Announce(au.Voice.Style($"{ra.Name}·{rb.Name} 문 표시판이 고장 — 너머 {vac.Name}은(는) 진공이다. 열지 마라"), vac, 2);
                if (b != null)
                    foreach (var id in b.HeardBy)
                    {
                        if (!_knowsBadIndicator.TryGetValue(id, out var set)) _knowsBadIndicator[id] = set = new HashSet<int>();
                        set.Add(d.Id);
                    }
            }
        }

        // 4) 같은 방에서 하루 두 번 넘게 넘어졌다 → 바닥을 봐 달라 (제안)
        foreach (var (rid, list) in _fallsIn.OrderBy(kv => kv.Key))
        {
            list.RemoveAll(t => now - t > SimTime.TicksPerDay);
            if (list.Count < 3 || rid >= ship.Rooms.Count) continue;
            var room = ship.Rooms[rid];
            if (au.Book.Add(ActKind.Advice, room, $"{room.Name}에서 하루 {list.Count}번 넘어짐", $"{Materials.Name(Floor[grid.Index(room.Cells[room.Cells.Count / 2])])} 바닥 · 물기 · 닳음",
                "걸레질 · 테이프 요청", "바닥을 손봐 달라", "falls:" + rid, SimTime.TicksPerDay, 120f) != null)
            {
                Stats.ComputerWarnings++;
                _mopRequest[rid] = now + SimTime.Hours(6);
            }
        }
    }

    /// <summary>젖은 칸 수 (시험 · 채점).</summary>
    public int WetCells(Room r)
    {
        int n = 0;
        var grid = _w.Ship.Grid;
        foreach (var c in r.Cells) if (Marks.TryGetValue(grid.Index(c), out var s) && s.V[(int)CellMark.Wet] >= 0.35f) n++;
        return n;
    }

    // ───────────────────────────── 승무원이 본다 ─────────────────────────────

    /// <summary>넘어진 걸 본 사람들은 그 방에서 조심한다 · 컴퓨터는 넘어진 방을 센다.</summary>
    private void Witness(CrewMember fallen, Cell cell)
    {
        var w = _w;
        if (w.Ship.RoomAt(cell) is not Room room) return;
        if (!_fallsIn.TryGetValue(room.Id, out var list)) _fallsIn[room.Id] = list = new List<long>();
        list.Add(w.Tick);
        foreach (var o in w.Crew)
        {
            // 같은 방에서 눈에 닿는 거리(10칸)면 본다 — 6칸이면 넓은 방(수경재배실) 맞은편에서 지켜보던 사람도 못 본 셈이 되던 것
            if (o == fallen || o.Dead || !o.IsAwake || o.Room != room || (o.Position - cell.Center).LengthSquared() > 100f) continue;
            if (!Cautious(o, room)) Stats.Witnessed++;
            BeCareful(o, room, 2f, $"{Ko.IGa(fallen.Name)} 넘어지는 걸 봤다");
        }
        BeCareful(fallen, room, 4f, "넘어져 봤다");
    }

    /// <summary>넘어지면 들고 있던 음식을 쏟는다 — 젖은 칸 · 균. 작은 물건은 곁의 정비 통로로 굴러 들어가기도 한다.</summary>
    private void Drop(CrewMember c, Cell cell)
    {
        if (c.Carrying is ItemStack small && small.Kind is not (ItemKind.Meal or ItemKind.Ration) && small.Count <= 2)
        {
            foreach (var d in Cell.Dirs4)
                if (WallAt(cell + d) is WallBody cw && cw.Crawl && cw.Lost == null && R.Chance(0.5f))
                {
                    cw.Lost = small;
                    c.Carrying = null;
                    Stats.RolledIn++;
                    _w.Log.Add(_w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} 넘어지며 놓친 {ItemKinds.Name(small.Kind)}이(가) 정비 통로 덮개 틈으로 굴러 들어갔다", c.Id);
                    if (c.SaidUntil < _w.Tick + 20) c.Say(_w, Persona.Say(c, "아, 저 안으로 굴러 들어갔네…"));
                    return;
                }
            return;
        }
        if (c.Carrying is not ItemStack held || held.Kind is not (ItemKind.Meal or ItemKind.Ration)) return;
        var w = _w;
        c.Carrying = null;
        Stats.FoodSpills++;
        RaiseMark(cell, CellMark.Wet, held.Kind == ItemKind.Ration ? 0.3f : 0.8f, "쏟은 음식");
        if (w.Ship.RoomAt(cell) is Room r)
        {
            var soil = w.Soil.RoomSoil(r);
            soil[(int)SoilKind.Bio] = MathF.Min(1f, soil[(int)SoilKind.Bio] + 0.1f);
        }
        w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} 넘어지며 들고 있던 {ItemKinds.Name(held.Kind)} {held.Count}개를 쏟았다", c.Id);
    }

    // ───────────────────────────── 다른 시스템과 (1분마다) ─────────────────────────────

    private float[] _dryMul = Array.Empty<float>();

    private void LinkSystems(float dt)
    {
        var w = _w;
        var ship = w.Ship;
        var grid = ship.Grid;
        // 조리: 끓는 화구 앞에 기름이 튄다 (타일 주방은 미끄러워진다) · 냄비가 타면 화구 앞이 그을린다
        foreach (var st in ship.FurnitureOf(FurnitureType.Stove))
        {
            if (st.UseSpots.Count == 0 || w.Cooking.CookingAt(st) == null) continue;
            var spot = st.UseSpots[0];
            float oil = Mark(spot, CellMark.Oil);
            if (oil < 0.6f && R.Chance(0.05f)) { RaiseMark(spot, CellMark.Oil, MathF.Min(0.6f, oil + 0.2f), "조리 기름 튐"); Stats.Grease++; }
        }
        int scorched = w.Cooking.Stats.Scorched;
        if (scorched > _scorchSeen)
        {
            var st = ship.FurnitureOf(FurnitureType.Stove).FirstOrDefault(f => f.UseSpots.Count > 0);
            if (st != null) RaiseMark(st.UseSpots[0], CellMark.Soot, 0.5f, "탄 냄비");
        }
        _scorchSeen = scorched;

        // 엎지른 국 (일상 장면): 그 칸이 젖는다 — 닦은 만큼 마른다 (타일은 미끄럽고, 카펫은 머금어 냄새가 밴다)
        foreach (var s in w.Scenes.Scenes)
        {
            if (!s.Open || s.Kind != SceneKind.Spill) continue;
            foreach (var t in s.Things)
            {
                if (t.Kind != ThingKind.Stain) continue;
                float v = 0.75f * Math.Clamp(t.Amount, 0f, 1f);
                var ms = MarksAt(t.At);
                if (ms == null || ms.V[(int)CellMark.Wet] < 0.05f || ms.Cause[(int)CellMark.Wet] == "엎지른 국") SetMark(t.At, CellMark.Wet, v, "엎지른 국");
            }
        }

        // 이동식 히터 · 선풍기: 그 방 바닥이 빨리 마르고 서리가 녹는다
        if (_dryMul.Length != ship.Rooms.Count) _dryMul = new float[ship.Rooms.Count];
        Array.Clear(_dryMul);
        foreach (var d in w.Portable.Devices)
        {
            if (!d.Running || d.Stored || d.Kind is not (PortableKind.Heater or PortableKind.Fan)) continue;
            if (ship.RoomAt(d.At) is Room r && r.Id < _dryMul.Length) _dryMul[r.Id] += d.Kind == PortableKind.Heater ? 3f : 2f;
        }

        // 오래 젖은 카펫: 곰팡이 → 방에 균이 쌓인다 (냄새가 악취로 맡는다 · 위생)
        foreach (var (i, s) in Marks)
        {
            if (s.V[(int)CellMark.Wet] < 0.3f || Materials.Of(Floor[i]).Absorb < 0.5f || w.Tick - s.Since[(int)CellMark.Wet] < SimTime.Hours(3)) continue;
            if (ship.RoomAt(grid.CellAt(i)) is not Room r) continue;
            var soil = w.Soil.RoomSoil(r);
            if (soil[(int)SoilKind.Bio] < 0.7f)
            {
                if (soil[(int)SoilKind.Bio] < 0.3f && soil[(int)SoilKind.Bio] + 0.02f * dt * 60f >= 0.3f)
                    w.Log.Add(w.Tick, LogKind.Life, $"{r.Name} 젖은 카펫에서 곰팡내가 난다");
                soil[(int)SoilKind.Bio] = MathF.Min(0.7f, soil[(int)SoilKind.Bio] + 0.02f * dt * 60f);
                Stats.Mildew++;
            }
        }
    }

    // ───────────────────────────── 정비 통로 ─────────────────────────────

    private static bool Technical(RoomType k) => k is RoomType.Engine or RoomType.Reactor or RoomType.Power or RoomType.LifeSupport or RoomType.Cooling
        or RoomType.PumpRoom or RoomType.HvacRoom or RoomType.Workshop or RoomType.Storage or RoomType.Substation or RoomType.BatteryRoom
        or RoomType.WaterPlant or RoomType.Recycling or RoomType.FuelCell or RoomType.ServerRoom;

    /// <summary>
    /// 정비 통로 자리: 문으로 바로 이어지지 않은 두 방(한쪽은 기관 · 설비 방) 사이의 한 칸짜리 안쪽 벽, 양쪽이 빈 바닥 — 짝마다 하나, 배마다 몇 개.
    /// </summary>
    private void PickCrawls()
    {
        var ship = _w.Ship;
        int max = Math.Clamp(ship.Rooms.Count / 6, 2, 6), n = 0;
        var pairs = new HashSet<(int, int)>();
        foreach (var wb in WallList) { if (!wb.Crawl) continue; wb.Crawl = false; }
        foreach (var wb in WallList)
        {
            if (n >= max) break;
            if (wb.Hull || wb.Thin || wb.Window) continue;
            for (int axis = 0; axis < 2 && !wb.Crawl; axis++)
            {
                var d = axis == 0 ? new Cell(1, 0) : new Cell(0, 1);
                Cell a = wb.Cell - d, b = wb.Cell + d;
                if (ship.RoomAt(a) is not Room ra || ship.RoomAt(b) is not Room rb || ra == rb || ra.Detached || rb.Detached) continue;
                if (!ship.IsOpenFloor(a) || !ship.IsOpenFloor(b) || ship.DoorAt(a) != null || ship.DoorAt(b) != null) continue;
                if (!(Technical(ra.Kind) || Technical(rb.Kind)) || Cabin(ra.Kind) || Cabin(rb.Kind)) continue;
                if (ra.Doors.Any(x => x.RoomA == rb || x.RoomB == rb)) continue;
                var key = ra.Id < rb.Id ? (ra.Id, rb.Id) : (rb.Id, ra.Id);
                if (pairs.Contains(key)) continue;
                pairs.Add(key);
                wb.Crawl = true; wb.CrawlA = ra.Id; wb.CrawlB = rb.Id;
                n++;
            }
        }
        Stats.Crawlways = n;
    }

    /// <summary>정비 통로 덮개: 양쪽 압력이 맞고 진공이 아닐 때만 열린다 (안에 사람이 있으면 그대로) — 길찾기에 알린다.</summary>
    private void UpdateCrawls()
    {
        var ship = _w.Ship;
        var grid = ship.Grid;
        var crawl = _w.Paths.Crawl;
        bool changed = false;
        foreach (var wb in WallList)
        {
            int i = grid.Index(wb.Cell);
            bool open = false;
            if (wb.Crawl && wb.CrawlA < ship.Rooms.Count && wb.CrawlB < ship.Rooms.Count)
            {
                Room ra = ship.Rooms[wb.CrawlA], rb = ship.Rooms[wb.CrawlB];
                open = !ra.Detached && !rb.Detached && ra.Air.Pressure > 60f && rb.Air.Pressure > 60f && MathF.Abs(ra.Air.Pressure - rb.Air.Pressure) < 15f;
                if (!open && crawl[i]) foreach (var c in _w.Crew) if (!c.Dead && c.Cell == wb.Cell) { open = true; break; } // 안에 사람이 있다
            }
            wb.CrawlOpen = open;
            if (crawl[i] != open) { crawl[i] = open; changed = true; }
        }
        if (changed) _w.Paths.CrawlChanged();
    }

    /// <summary>정비 통로 속 한 걸음: 엎드려 기어간다 (느리다) · 처음 들어서면 센다.</summary>
    private float CrawlStep(CrewMember c)
    {
        var w = _w;
        int ci = w.Ship.Grid.Index(c.Cell);
        if (_lastCell[c.Id] != ci)
        {
            _lastCell[c.Id] = ci;
            Stats.Crawls++;
            if (c.SaidUntil < w.Tick) c.Say(w, Persona.Say(c, "정비 통로로 질러간다 — 좁네"));
        }
        c.Pose = Pose.Walking;
        return 0.3f;
    }

    /// <summary>정비 통로에 지금 들어가 있는 사람 (화면).</summary>
    public bool Crawling(CrewMember c) => c.Room == null && !c.Outside && _w.Ship.Grid.Kind(c.Cell) == TileKind.Wall;

    /// <summary>이 칸의 마르는 빠르기 배율 (히터 · 선풍기).</summary>
    private float DryMul(Room? r) => r != null && r.Id < _dryMul.Length ? 1f + _dryMul[r.Id] : 1f;
}
