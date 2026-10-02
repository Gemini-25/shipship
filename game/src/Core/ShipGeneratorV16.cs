using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ShipSim.Core;

// v16.9 생성기 확장: "gen:용도:뼈대:인원:시드" (2~60인).
//  · 용도(일반 · 채굴 · 이민 · 병원 · 연구 · 보급)가 새 방 고르기 가중치와 기본 방 크기를 바꾼다.
//  · 뼈대: 직선(기존) · 고리(위아래 통로를 양 끝에서 이어 한 바퀴 — Build 안에서) · 척추(긴 중앙 통로에 모듈이 가지처럼, 모듈마다 기밀문 하나).
//  · 설계사와 시작 상태는 시드에서 정한다 (같은 키 → 같은 배 · 같은 내력).
// 옛 키 "gen:인원:시드"는 예전 그대로다 (내력 없음).

public static partial class ShipGenerator
{
    /// <summary>용도마다 즐겨 고르는 새 방 (앞의 것일수록 꼭 넣는다).</summary>
    private static readonly Dictionary<ShipPurpose, RoomType[]> PurposeRooms = new()
    {
        [ShipPurpose.Mining] = new[] { RoomType.Crusher, RoomType.Cargo, RoomType.DroneBay, RoomType.Recycling, RoomType.WeldingShop, RoomType.PropellantTank, RoomType.PartsPrep },
        [ShipPurpose.Colony] = new[] { RoomType.PrivateCabins, RoomType.Theater, RoomType.School, RoomType.Garden, RoomType.Chapel, RoomType.SeedVault, RoomType.WaterWallCabin, RoomType.Meditation, RoomType.MushroomFarm, RoomType.ProteinFarm }, // v16.22 이민선은 먹을 길이 여럿 (버섯 · 단백질)
        [ShipPurpose.Hospital] = new[] { RoomType.Triage, RoomType.Quarantine, RoomType.Hyperbaric, RoomType.QuarantineLock, RoomType.Morgue, RoomType.Decon, RoomType.Lab },
        [ShipPurpose.Research] = new[] { RoomType.Lab, RoomType.Calibration, RoomType.AlgaeLab, RoomType.ElectronicsLab, RoomType.Observatory, RoomType.Archive }, // v16.22 교정실 · 조류 배양실을 꼭
        [ShipPurpose.Supply] = new[] { RoomType.Cargo, RoomType.Freezer, RoomType.DockingBay, RoomType.GasStorage, RoomType.Laundry, RoomType.ShuttleBay, RoomType.PropellantTank },
    };

    private static readonly Dictionary<ShipPurpose, string[]> PurposeNames = new()
    {
        [ShipPurpose.Mining] = new[] { "곡괭이호", "광맥호", "쇠바위호", "돌가루호", "정호", "부리호" },
        [ShipPurpose.Colony] = new[] { "새터호", "보금자리호", "씨앗호", "너른들호", "한마당호", "꿈터호" },
        [ShipPurpose.Hospital] = new[] { "보듬호", "약손호", "다솜호", "인술호", "숨결호" },
        [ShipPurpose.Research] = new[] { "궁리호", "살핌호", "별자리호", "헤아림호", "갈피호" },
        [ShipPurpose.Supply] = new[] { "나르미호", "짐꾼호", "곳간호", "두레호", "마중호" },
    };

    private static string NameFor(ShipPurpose p, int seed) =>
        PurposeNames.TryGetValue(p, out var list) ? list[(int)((uint)seed % list.Length)] : Names[(int)((uint)seed % Names.Length)];

    /// <summary>용도가 기본 방의 크기를 바꾼다 (병원선은 의무실이 크고, 보급선은 창고가, 채굴선은 정비실이, 이민선은 휴게실이).</summary>
    private static int Sized(ShipPurpose p, char room, int n) => (p, room) switch
    {
        (ShipPurpose.Hospital, 'h') => n * 3,
        (ShipPurpose.Supply, 's') => n * 3,
        (ShipPurpose.Mining, 's') => n * 2,
        (ShipPurpose.Mining, 'w') => n * 2,
        (ShipPurpose.Research, 'w') => n * 2,
        (ShipPurpose.Colony, 'g') => n * 2,
        _ => n,
    };

    /// <summary>용도에 맞춰 새 방 고르기: 용도 방 몇 개는 꼭, 나머지는 용도 방을 다섯 배로 더 자주 (단계 제한은 용도 방엔 느슨하게).</summary>
    private static List<RoomSpec> PickExtrasFor(int n, Rng rng, ShipPurpose purpose)
    {
        int maxTier = n <= 6 ? 1 : n <= 12 ? 2 : 3;
        int count = (n <= 4 ? 2 : n <= 6 ? 3 : n <= 12 ? 5 : n <= 20 ? 8 : 11) + (n >= 8 ? 1 : 0);
        var fav = PurposeRooms.TryGetValue(purpose, out var f) ? f : Array.Empty<RoomType>();
        var pool = RoomCatalog.All.Where(s => !Infra.Contains(s.Kind) && (s.Tier <= maxTier || fav.Contains(s.Kind) && s.Tier <= maxTier + 1)).ToList();
        var must = new List<RoomType> { RoomType.Shelter, RoomType.Gym };
        if (n > 6) must.Add(RoomType.Quarantine);
        must.AddRange(fav.Take(n <= 4 ? 2 : 3));
        var pick = pool.Where(s => must.Contains(s.Kind)).ToList();
        var rest = pool.Where(s => !must.Contains(s.Kind)).ToList();
        while (pick.Count < count && rest.Count > 0)
        {
            float total = rest.Sum(s => fav.Contains(s.Kind) ? 5f : 1f);
            float x = rng.Float() * total;
            int i = 0;
            for (; i < rest.Count - 1; i++) { x -= fav.Contains(rest[i].Kind) ? 5f : 1f; if (x <= 0f) break; }
            pick.Add(rest[i]);
            rest.RemoveAt(i);
        }
        pick = pick.Take(Math.Min(count, LegendChars.Length)).ToList();
        foreach (var kind in InfraFor(n))
            if (pick.Count < LegendChars.Length && RoomCatalog.Of(kind) is RoomSpec spec) pick.Add(spec);
        return pick;
    }

    public static string KeyFor(ShipPurpose p, ShipFrame f, int crew, int seed) => $"gen:{ShipInfos.Key(p)}:{ShipInfos.Key(f)}:{crew}:{seed}";

    private static ShipTemplate? FromKeyV16(string[] p)
    {
        if (p[0] != "gen" || ShipInfos.ParsePurpose(p[1]) is not ShipPurpose purpose || ShipInfos.ParseFrame(p[2]) is not ShipFrame frame
            || !int.TryParse(p[3], out int n) || !int.TryParse(p[4], out int seed)) return null;
        if (!ShipInfos.GenFrames.Contains(frame) || !ShipInfos.GenPurposes.Contains(purpose)) return null;
        return Template(purpose, frame, Math.Clamp(n, 2, 60), seed);
    }

    /// <summary>v16.9 용도 · 뼈대를 고른 생성 배 (2~60인). 같은 키는 같은 배.</summary>
    public static ShipTemplate Template(ShipPurpose purpose, ShipFrame frame, int n, int seed)
    {
        string key = KeyFor(purpose, frame, n, seed);
        lock (Cache)
            if (Cache.TryGetValue(key, out var t)) return t;
        var info = InfoFor(purpose, frame, n, seed);
        int hDelta = info.Designer == ShipDesigner.Military ? -1 : info.Designer == ShipDesigner.Civilian && n > 4 ? 1 : 0;
        Exception? last = null;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                var (ascii, note, name) = frame == ShipFrame.Spine ? BuildSpine(n, seed, attempt, purpose, hDelta) : Build(n, seed, attempt, purpose, frame, hDelta);
                var tpl = new ShipTemplate(key, name, n, ascii, $"{ShipInfos.Name(frame)} {ShipInfos.Name(purpose)} · {note}", info);
                lock (Cache) Cache[key] = tpl;
                return tpl;
            }
            catch (InvalidOperationException e) { last = e; }
        }
        throw new InvalidOperationException($"배를 만들지 못했습니다 ({key}): {last?.Message}");
    }

    // ───────────────────────────── 내력 (시드에서) ─────────────────────────────

    private static readonly string[] FormerNames = { "청람 2호", "흰여울호", "제3 화물 예인선", "바람개비호", "OX-17", "은가람호", "새벽별 7호", "희망 운송 12호" };
    private static readonly string[] Ports = { "케레스", "가니메데", "타이탄 북항", "베스타", "팔라스 정거장", "에우로파 남항" };

    private static ShipInfo InfoFor(ShipPurpose purpose, ShipFrame frame, int n, int seed)
    {
        var r = new Rng(unchecked(seed * 6151 + n * 17 + (int)purpose * 101 + (int)frame * 7 + 3));
        (float mil, float set) = purpose switch
        {
            ShipPurpose.Mining => (0.2f, 0.5f), ShipPurpose.Colony => (0.1f, 0.3f), ShipPurpose.Hospital => (0.4f, 0.1f),
            ShipPurpose.Research => (0.3f, 0.1f), ShipPurpose.Supply => (0.3f, 0.2f), _ => (0.25f, 0.25f),
        };
        float d = r.Float();
        var designer = d < mil ? ShipDesigner.Military : d < mil + set ? ShipDesigner.Settler : ShipDesigner.Civilian;
        float s = r.Float();
        var start = s < 0.2f ? ShipStart.New : s < 0.6f ? ShipStart.Used : s < 0.75f ? ShipStart.Junk : s < 0.87f ? ShipStart.WarScarred : ShipStart.Derelict;
        int age = start switch
        {
            ShipStart.New => r.Range(1, 3), ShipStart.Used => r.Range(8, 25), ShipStart.Junk => r.Range(30, 60),
            ShipStart.WarScarred => r.Range(15, 40), _ => r.Range(20, 50),
        };
        string port = Ports[r.Range(0, Ports.Length)];
        string? former = start == ShipStart.New ? null : FormerNames[r.Range(0, FormerNames.Length)];
        var events = new List<string>();
        if (start != ShipStart.New) events.Add($"{ShipInfos.Year - age}년 {port}에서 건조");
        if (start is ShipStart.Used or ShipStart.Junk) events.Add($"{port} 정비창에서 주인이 두 번 바뀌었다");
        if (start == ShipStart.WarScarred) events.Add("궤도 분쟁 때 좌현이 뚫렸다 — 땜질하고 구획 하나를 막았다");
        if (start == ShipStart.Derelict) events.Add($"{r.Range(3, 12)}년 동안 버려져 떠돌았다 — 예인해 와 다시 띄웠다");
        if (start == ShipStart.Junk) events.Add("고장 난 설비를 떼어 다른 배 것으로 갈았다");
        var cargo = purpose switch
        {
            ShipPurpose.Mining => new[] { (ItemKind.Plate, 12), (ItemKind.Structure, 8), (ItemKind.MetalOre, 30) },
            ShipPurpose.Colony => new[] { (ItemKind.Seed, 30), (ItemKind.Ration, 40), (ItemKind.Thread, 10) },
            ShipPurpose.Hospital => new[] { (ItemKind.MedKit, 16), (ItemKind.Bandage, 20), (ItemKind.Disinfectant, 10) },
            ShipPurpose.Research => new[] { (ItemKind.Electronics, 10), (ItemKind.Sensor, 4), (ItemKind.Fiber, 6) },
            ShipPurpose.Supply => new[] { (ItemKind.Ration, 80), (ItemKind.Plate, 20), (ItemKind.Sealant, 12), (ItemKind.Filter, 10) },
            _ => Array.Empty<(ItemKind, int)>(),
        };
        int diff = start switch { ShipStart.New => 1, ShipStart.Used => 2, ShipStart.WarScarred => 3, _ => 4 }
                   + (frame == ShipFrame.Spine ? 1 : 0) + (n <= 3 ? 1 : 0) - (designer == ShipDesigner.Military ? 1 : 0);
        string pros = frame switch
        {
            ShipFrame.Ring => "어디든 두 갈래 길 — 한쪽이 막혀도 돌아간다", ShipFrame.Spine => "구획째 봉쇄 · 분리 — 사고가 번지지 않는다", _ => "익숙한 배치",
        } + (designer == ShipDesigner.Military ? " · 격벽과 이중 배선" : designer == ShipDesigner.Civilian ? " · 넓은 방" : " · 손에 익은 임시 개조");
        string cons = frame switch
        {
            ShipFrame.Ring => "통로가 길다", ShipFrame.Spine => "중앙 통로가 막히면 끝까지 못 간다", _ => "가운데 통로에 사람이 몰린다",
        } + (designer == ShipDesigner.Military ? " · 비좁다" : designer == ShipDesigner.Civilian ? " · 단일 고장점 · 싼 부품" : " · 비표준 부품");
        return new ShipInfo(purpose, frame, designer, start, ShipInfos.Year - age, cargo, former, events.ToArray(), pros, cons, Math.Clamp(diff, 1, 5),
            $"{ShipInfos.Name(designer)} 설계 {ShipInfos.Name(purpose)} · {ShipInfos.Name(start)}");
    }

    // ───────────────────────────── 척추형 ─────────────────────────────

    /// <summary>방을 위아래로 뒤집는다 (문을 낼 줄이 막혔을 때).</summary>
    private static void FlipV(GRoom r)
    {
        r.G.Reverse();
        r.Carve = r.Carve.Select(p => (p.Item1, r.H - 1 - p.Item2)).ToHashSet();
    }

    private static int? FreeAt(GRoom room, int gx, int row)
    {
        var free = Enumerable.Range(0, room.W).Where(xx => (room.G[row][xx] == '.' || room.G[row][xx] == room.Label) && !room.Carve.Contains((xx, row))).ToList();
        if (free.Count == 0) return null;
        return gx + free.OrderBy(xx => Math.Abs(xx - room.W / 2)).First();
    }

    /// <summary>
    /// 척추형: 왼쪽 엔진실 · 긴 중앙 통로(2칸) · 위아래로 가지처럼 붙은 모듈(방 줄 + 모듈 복도) · 오른쪽 뱃머리 함교.
    /// 모듈 복도와 중앙 통로 사이엔 기밀문이 하나뿐이다 — 통로끼리의 문이라 격벽(구획 경계)이 되어 모듈째 봉쇄 · 분리된다.
    /// </summary>
    private static (string ascii, string note, string name) BuildSpine(int n, int seed, int attempt, ShipPurpose purpose, int hDelta)
    {
        var rng = new Rng(unchecked(seed * 7927 + attempt * 104723 + n * 31 + 7));
        int H = Math.Clamp((n <= 4 ? 5 : n <= 12 ? 6 : 7) + hDelta, n > 6 ? 6 : 5, 8);
        int s = n <= 6 ? 3 : n <= 12 ? 4 : n <= 20 ? 5 : n <= 40 ? 6 : 7;
        if (H < s + 1) H = s + 1;
        float reactorKw = 48f * s * s / 9f;
        int pumps = Math.Max(2, Ceil((int)(reactorKw * 1.15f), 30));
        int beds = Math.Max(2, Ceil(4 * n, 6));

        var extras = PickExtras(n, rng, purpose);
        var legend = new StringBuilder();
        var specials = new List<GRoom>();
        for (int i = 0; i < extras.Count; i++)
        {
            char ch = LegendChars[i];
            legend.Append('@').Append(ch).Append('=').Append(extras[i].Kind).Append('\n');
            specials.Add(SpecialRoom(extras[i], ch, H));
        }

        // 수경재배실이 너무 길면 둘로
        var hydro = new List<GRoom>();
        for (int left = beds; left > 0;)
        {
            int k = Math.Min(left, H >= 7 ? 12 : 8);
            hydro.Add(Hydro(k, H));
            left -= k;
        }
        // 모듈: (방들, 위쪽에 둘지 · null이면 짧은 쪽)
        var mods = new List<(List<GRoom> rooms, bool? top)>
        {
            (new() { Reactor(n, H), Cooling(pumps, H), PowerRoom(Math.Max(2, 2 * Ceil(n, 6)), H) }, true),
            (new() { Life(n, H), hydro[0] }, false),
            (new() { Workshop(Sized(purpose, 'w', n), H), Storage(Sized(purpose, 's', n), H) }, null),
            (new() { Galley(n, H), Mess(n, H) }, null),
            (new() { Medbay(Sized(purpose, 'h', n), H), Lounge(Sized(purpose, 'g', n), H) }, null),
        };
        foreach (var hy in hydro.Skip(1)) mods.Add((new() { hy }, null));
        var quarters = Quarters(n, H);
        for (int i = 0; i < quarters.Count; i += 2) mods.Add((quarters.Skip(i).Take(2).ToList(), null));
        // 새 방은 둘셋씩 묶어 모듈로 (시드마다 순서가 다르다)
        var sp = specials.OrderBy(_ => rng.Float()).ToList();
        for (int i = 0; i < sp.Count;)
        {
            int k = Math.Min(sp.Count - i, sp.Count - i == 4 ? 2 : 3);
            mods.Add((sp.Skip(i).Take(k).ToList(), null));
            i += k;
        }
        mods.Add((new() { Airlock(n, H), CommsRoom(H) }, false));
        foreach (var (rooms, _) in mods) foreach (var r in rooms) Furnish(r);

        // ── 세로 배치 ──
        int yTopRoom = 1, yTopWall = H + 1, yTopHall = H + 2, yTopDoor = H + 4, ySpine = H + 5, yBotDoor = H + 7, yBotHall = H + 8, yBotWall = H + 10, yBotRoom = H + 11;
        int Ht = 2 * H + 12;
        int h2 = 2 * H + 10;
        var eng = Engine(n, h2);
        int x0 = 1 + eng.W + 1;
        int xTop = x0, xBot = x0;
        // 공용 모듈(기관 · 생명유지 · 에어락)은 먼저, 나머지는 시드에 따라 섞어 짧은 쪽에
        var order = mods.Where(m => m.top != null).ToList();
        var restMods = mods.Where(m => m.top == null).OrderBy(_ => rng.Float()).ToList();
        var airlock = order[^1];
        order.RemoveAt(order.Count - 1);
        order.AddRange(restMods);
        order.Add(airlock);
        var placed = new List<(List<GRoom> rooms, bool top, int x)>();
        foreach (var (rooms, top) in order)
        {
            int wm = rooms.Sum(r => r.W) + rooms.Count - 1;
            bool t = top ?? xTop <= xBot;
            int x = t ? xTop : xBot;
            placed.Add((rooms, t, x));
            if (t) xTop = x + wm + 3; else xBot = x + wm + 3;
        }
        int xSpineEnd = Math.Max(xTop, xBot) - 1;
        int xNose = xSpineEnd + 2;
        int hn = 8;
        var nose = Nose(hn);
        int W = xNose + nose.W + 2;
        var grid = new char[Ht, W];
        for (int y = 0; y < Ht; y++) for (int x = 0; x < W; x++) grid[y, x] = ' ';
        void Blit(GRoom room, int gx, int gy)
        {
            for (int yy = 0; yy < room.H; yy++)
                for (int xx = 0; xx < room.W; xx++)
                    if (!room.Carve.Contains((xx, yy))) grid[gy + yy, gx + xx] = room.G[yy][xx];
        }
        Blit(eng, 1, 1);
        // 중앙 통로
        for (int x = x0; x <= xSpineEnd; x++) { grid[ySpine, x] = '.'; grid[ySpine + 1, x] = '.'; }
        grid[ySpine, x0 + 1] = 'c';
        var doors = new List<(int x, int y)>();
        var moduleDoors = new List<int>();
        foreach (var (rooms, top, mx) in placed)
        {
            int wm = rooms.Sum(r => r.W) + rooms.Count - 1;
            int hallY = top ? yTopHall : yBotHall;
            for (int yy = hallY; yy < hallY + 2; yy++) for (int xx = mx; xx < mx + wm; xx++) grid[yy, xx] = '.';
            int x = mx;
            foreach (var r in rooms)
            {
                // 문은 모듈 복도 쪽 줄에 — 막혔으면 방을 뒤집는다
                int hallRow = top ? H - 1 : 0, outRow = top ? 0 : H - 1;
                int? px = FreeAt(r, x, hallRow);
                if (px == null) { FlipV(r); px = FreeAt(r, x, hallRow); }
                if (px == null) throw new InvalidOperationException($"{r.Label}: 구획 복도 쪽 문 자리가 없다");
                Blit(r, x, top ? yTopRoom : yBotRoom);
                doors.Add((px.Value, top ? yTopWall : yBotWall));
                if (r.Label == 'a')
                {
                    int? ex = FreeAt(r, x, outRow) ?? throw new InvalidOperationException("에어락 바깥 문 자리가 없다");
                    doors.Add((ex.Value, top ? 0 : Ht - 1));
                }
                x += r.W + 1;
            }
            int dx = mx + wm / 2;
            doors.Add((dx, top ? yTopDoor : yBotDoor)); // 모듈 기밀문
            moduleDoors.Add(dx);
        }
        int ny0 = ySpine - 3;
        Blit(nose, xNose, ny0);
        FinishWalls(grid);
        foreach (var (x, y) in doors) PutDoor(grid, x, y);
        PutDoor(grid, x0 - 1, ySpine); PutDoor(grid, x0 - 1, ySpine + 1);
        PutDoor(grid, xNose - 1, ySpine); PutDoor(grid, xNose - 1, ySpine + 1);
        // 중앙 통로 격벽 (모듈 사이 빈틈에서) — 한 구간이 뚫려도 나머지는 지킨다
        int bulk = n >= 20 ? 2 : n >= 6 ? 1 : 0;
        for (int k = 1; k <= bulk; k++)
        {
            int target = x0 + (xSpineEnd - x0) * k / (bulk + 1);
            for (int off = 0; off < 40; off++)
            {
                int bx = target + (off % 2 == 0 ? off / 2 : -(off / 2 + 1));
                if (bx <= x0 + 2 || bx >= xSpineEnd - 2) continue;
                bool ok = grid[ySpine - 1, bx] == '#' && grid[ySpine + 2, bx] == '#'
                          && moduleDoors.All(md => Math.Abs(md - bx) > 1)
                          && grid[ySpine - 1, bx - 1] != '+' && grid[ySpine - 1, bx + 1] != '+' && grid[ySpine + 2, bx - 1] != '+' && grid[ySpine + 2, bx + 1] != '+';
                if (!ok) continue;
                grid[ySpine, bx] = '+'; grid[ySpine + 1, bx] = '+';
                break;
            }
        }
        LabelCorridors(grid);
        CheckDoorFronts(grid);
        string name = purpose == ShipPurpose.General ? Names[(int)((uint)seed % Names.Length)] : NameFor(purpose, seed);
        string note = $"생성 · {n}인 · 구획 {placed.Count} · 원자로 {s}×{s} · 펌프 {pumps} · 새 방 {extras.Count}: {string.Join("·", extras.Select(e => e.Name))}";
        return (legend + Ascii(grid), note, name);
    }

    // ───────────────────────────── 공용 마무리 ─────────────────────────────

    private static bool Floorish(char ch) => ch != ' ' && ch != '#';

    /// <summary>바닥에 8방향으로 닿은 빈칸은 벽.</summary>
    private static void FinishWalls(char[,] grid)
    {
        int Ht = grid.GetLength(0), W = grid.GetLength(1);
        for (int y = 0; y < Ht; y++)
            for (int x = 0; x < W; x++)
            {
                if (grid[y, x] != ' ') continue;
                bool near = false;
                for (int dy = -1; dy <= 1 && !near; dy++)
                    for (int dx = -1; dx <= 1 && !near; dx++)
                    {
                        int yy = y + dy, xx = x + dx;
                        if (yy >= 0 && yy < Ht && xx >= 0 && xx < W && Floorish(grid[yy, xx]) && grid[yy, xx] != '+') near = true;
                    }
                if (near) grid[y, x] = '#';
            }
    }

    private static void PutDoor(char[,] grid, int x, int y)
    {
        if (grid[y, x] != '#') throw new InvalidOperationException($"벽이 아닌 곳에 문 ({x},{y}) '{grid[y, x]}'");
        grid[y, x] = '+';
    }

    /// <summary>라벨 없는 바닥 덩어리(모듈 복도 · 격벽에 잘린 통로)에 통로 라벨.</summary>
    private static void LabelCorridors(char[,] grid)
    {
        int Ht = grid.GetLength(0), W = grid.GetLength(1);
        var seen = new bool[Ht, W];
        for (int y = 0; y < Ht; y++)
            for (int x = 0; x < W; x++)
            {
                if (seen[y, x] || grid[y, x] == ' ' || grid[y, x] == '#' || grid[y, x] == '+') continue;
                var comp = new List<(int x, int y)>();
                var st = new Stack<(int x, int y)>();
                st.Push((x, y)); seen[y, x] = true;
                bool labeled = false;
                while (st.Count > 0)
                {
                    var (cx, cy) = st.Pop();
                    comp.Add((cx, cy));
                    char ch = grid[cy, cx];
                    if (char.IsLower(ch) || char.IsDigit(ch)) labeled = true;
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= W || ny >= Ht || seen[ny, nx]) continue;
                        char nc = grid[ny, nx];
                        if (nc == ' ' || nc == '#' || nc == '+') continue;
                        seen[ny, nx] = true;
                        st.Push((nx, ny));
                    }
                }
                if (!labeled)
                {
                    var spot = comp.Where(p => grid[p.y, p.x] == '.').OrderBy(p => p.y).ThenBy(p => p.x).First();
                    grid[spot.y, spot.x] = 'c';
                }
            }
    }

    private static void CheckDoorFronts(char[,] grid)
    {
        int Ht = grid.GetLength(0), W = grid.GetLength(1);
        for (int y = 0; y < Ht; y++)
            for (int x = 0; x < W; x++)
            {
                if (grid[y, x] != '+') continue;
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int yy = y + dy, xx = x + dx;
                    if (yy < 0 || yy >= Ht || xx < 0 || xx >= W || !Floorish(grid[yy, xx])) continue;
                    char ch = grid[yy, xx];
                    if (!(ch == '.' || ch == '+' || char.IsLower(ch) || char.IsDigit(ch))) throw new InvalidOperationException($"문 앞이 막힘 ({x},{y}) '{ch}'");
                }
            }
    }

    private static string Ascii(char[,] grid)
    {
        int Ht = grid.GetLength(0), W = grid.GetLength(1);
        var sb = new StringBuilder();
        for (int y = 0; y < Ht; y++)
        {
            var line = new StringBuilder();
            for (int x = 0; x < W; x++) line.Append(grid[y, x]);
            sb.Append(line.ToString().TrimEnd()).Append('\n');
        }
        return sb.ToString();
    }
}
