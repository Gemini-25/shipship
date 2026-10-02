using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ShipSim.Core;

// v12.6 절차 생성 배: tools/shipgen/gen_ships.py의 뼈대(왼쪽 엔진실, 층마다 방 줄, 층 사이 통로, 오른쪽 뱃머리 함교)를 옮기고,
// 시드에 따라 방 순서·크기를 흔들고 크기에 맞는 새 방(표준·대형·세대선)을 섞는다. 설계도 글자 지도(범례 포함)를 내놓는다.
// 키 "gen:인원:시드" 하나로 같은 배가 다시 나온다 (저장·재생).

public static partial class ShipGenerator
{
    private sealed class GRoom
    {
        public char Label;
        public int W, H, W0 = -1;
        public List<List<char>> G = new();
        public HashSet<(int, int)> Carve = new();
        public RoomType? Special;

        public GRoom(char label, int w, int h)
        {
            Label = label; W = w; H = h;
            for (int y = 0; y < h; y++) G.Add(Enumerable.Repeat('.', w).ToList());
            G[0][0] = label;
        }

        public void Put(char ch, int x, int y, int w = 1, int h = 1)
        {
            for (int yy = y; yy < y + h; yy++)
                for (int xx = x; xx < x + w; xx++)
                {
                    if (xx < 0 || xx >= W || yy < 0 || yy >= H) throw new InvalidOperationException($"{Label}: {ch} ({xx},{yy}) 방 밖");
                    if (G[yy][xx] != '.') throw new InvalidOperationException($"{Label}: {ch} ({xx},{yy}) 겹침 {G[yy][xx]}");
                    G[yy][xx] = ch;
                }
        }

        public void Widen(int extra)
        {
            if (W0 < 0) W0 = W;
            foreach (var row in G) row.AddRange(Enumerable.Repeat('.', extra));
            W += extra;
        }
    }

    private static int Ceil(int a, int b) => (a + b - 1) / b; // 양수만

    // ── 방 모듈 (생성기와 같은 규칙) ──

    private static GRoom Reactor(int n, int H)
    {
        int s = n <= 6 ? 3 : n <= 12 ? 4 : n <= 20 ? 5 : n <= 40 ? 6 : 7; // v16.9 40명 넘으면 7×7
        var r = new GRoom('r', s + 5, H);
        r.Put('R', 2, 1, s, s);
        r.Put('C', s + 3, 0, 2, 1);
        return r;
    }

    private static GRoom Cooling(int pumps, int H)
    {
        var r = new GRoom('k', 3 * pumps - 1 + 2, H);
        r.G[0][0] = '.';
        for (int i = 0; i < pumps; i++) r.Put('P', 1 + 3 * i, 0, 2, 2);
        r.G[H - 1][0] = 'k';
        return r;
    }

    private static GRoom PowerRoom(int batteries, int H)
    {
        int rows = H >= 7 ? 2 : 1;
        int perRow = Ceil(batteries, rows);
        int w = Math.Max(6, 3 * perRow + 1);
        var r = new GRoom('p', w, H);
        r.Put('X', 1, 0, 3, 1);
        r.Put('Z', w - 2, 1, 2, 1);
        for (int i = 0; i < batteries; i++)
        {
            int row = i / perRow, col = i % perRow;
            r.Put('Y', 1 + 3 * col, H - 2 - 3 * row, 2, 2);
        }
        return r;
    }

    private static GRoom Workshop(int n, int H)
    {
        int benches = Math.Max(1, Ceil(n, 10));
        int w = Math.Max(6, 4 * benches + 3);
        var r = new GRoom('w', w, H);
        for (int i = 0; i < benches; i++) r.Put('W', 1 + 4 * i, 0, 3, 1);
        r.Put('H', w - 2, 0, 2, 1);
        int refineries = n <= 12 ? 1 : 2;
        for (int i = 0; i < refineries; i++) r.Put('N', 1 + 3 * i, H - 2, 2, 2);
        r.Put('K', w - 1, H - 3, 1, 3);
        return r;
    }

    private static GRoom Storage(int n, int H)
    {
        int shelves = Math.Max(4, Ceil(n * 2, 3));
        int[] rows = H >= 7 ? new[] { 0, 3, H - 2 } : new[] { 0, H - 2 };
        int cols = Ceil(shelves, rows.Length);
        int w = 3 * cols - 1;
        var r = new GRoom('s', Math.Max(5, w + 1), H);
        r.G[0][0] = '.';
        for (int i = 0; i < shelves; i++)
        {
            int col = i / rows.Length, row = i % rows.Length;
            r.Put('K', 1 + 3 * col, rows[row], 2, 2);
        }
        r.G[2][0] = 's';
        return r;
    }

    private static GRoom Galley(int n, int H)
    {
        int stoves = Math.Max(1, Ceil(n, 6)), fridges = Math.Max(1, Ceil(n, 6));
        if (stoves + fridges <= 4)
        {
            int w0 = Math.Max(5, 3 * (stoves + fridges) + 1);
            var g = new GRoom('j', w0, H);
            int x = 1;
            for (int i = 0; i < stoves; i++) { g.Put('V', x, 0, 2, 1); x += 3; }
            for (int i = 0; i < fridges; i++) { g.Put('F', x, 0, 2, 1); x += 3; }
            g.Put('T', 1, H - 3, 2, 1);
            return g;
        }
        int w = Math.Max(7, 3 * Math.Max(stoves, fridges) + 2);
        var r = new GRoom('j', w, H);
        for (int i = 0; i < stoves; i++) r.Put('V', 1 + 3 * i, 0, 2, 1);
        for (int i = 0; i < fridges; i++) r.Put('F', 1 + 3 * i, H - 1, 2, 1);
        r.Put('T', 2, H / 2 - 1, 2, 1);
        return r;
    }

    private static GRoom Mess(int n, int H)
    {
        int disp = Math.Max(2, Ceil(n, 4)), tables = Math.Max(2, Ceil(n, 4));
        int w = Math.Max(9, Math.Max(3 * tables + 3, disp + 3));
        var r = new GRoom('m', w, H);
        for (int i = 0; i < disp; i++) r.Put('D', 1 + i, 0);
        for (int i = 0; i < tables; i++)
        {
            int x = 3 + 3 * i;
            if (x + 1 >= w) break;
            r.Put('S', x, 2, 2, 1); r.Put('T', x, 3, 2, 1); r.Put('S', x, 4, 2, 1);
        }
        return r;
    }

    private static GRoom CommsRoom(int H)
    {
        var r = new GRoom('o', 6, H);
        r.Put('C', 3, 0);
        r.Put('A', 3, 1, 2, 1);
        return r;
    }

    private static GRoom Life(int n, int H)
    {
        int gens = Math.Max(2, Ceil(n, 3)), recyclers = Math.Max(1, Ceil(n, 6));
        int w = Math.Max(8, Math.Max(3 * gens + 1, 3 * recyclers + 3));
        var r = new GRoom('l', w, H);
        for (int i = 0; i < recyclers; i++) r.Put('U', w - 2 - 3 * i, 0, 2, 2);
        if (gens > 4 && H >= 8)
        {
            int per = Ceil(gens, 2);
            int w2 = Math.Max(w, Math.Max(3 * per + 1, 3 * recyclers + 3));
            if (w2 != w)
            {
                foreach (var row in r.G) row.AddRange(Enumerable.Repeat('.', w2 - w));
                r.W = w2;
                foreach (var row in r.G)
                    for (int x = 0; x < w2; x++) row[x] = row[x] != 'l' ? '.' : 'l';
                for (int i = 0; i < recyclers; i++) r.Put('U', w2 - 2 - 3 * i, 0, 2, 2);
                w = w2;
            }
            for (int i = 0; i < gens; i++)
            {
                int row = i / per, col = i % per;
                r.Put('O', 3 * col, H - 6 + 3 * row, 2, 3);
            }
        }
        else
            for (int i = 0; i < gens; i++) r.Put('O', 3 * i, H - 4, 2, 4);
        r.Put('C', w - 1, H - 1);
        return r;
    }

    private static GRoom Hydro(int beds, int H)
    {
        int[] ys = H >= 7 ? new[] { 1, H == 7 ? 3 : 4, H - 2 } : new[] { 1, H - 2 };
        int perRow = Ceil(beds, ys.Length);
        var r = new GRoom('f', 5 * perRow + 1, H);
        for (int i = 0; i < beds; i++)
        {
            int row = i / perRow, col = i % perRow;
            r.Put('G', 1 + 5 * col, ys[row], 4, 1);
        }
        return r;
    }

    private static GRoom Airlock(int n, int H)
    {
        int docks = n <= 12 ? 2 : 3;
        int lockers = n <= 6 ? 2 : n <= 12 ? 3 : 4;
        int w = lockers <= 2 && docks <= 2 ? 3 : 5;
        var r = new GRoom('a', w, H);
        var xs = Enumerable.Range(0, w).Where(x => x % 2 == 0).ToList();
        for (int i = 0; i < lockers; i++)
        {
            int y = 1 + 2 * (i / xs.Count);
            if (y + 1 < H - 2) r.Put('L', xs[i % xs.Count], y, 1, 2);
        }
        for (int i = 0; i < docks; i++) r.Put('Q', xs[i % xs.Count], H - 2, 1, 2);
        return r;
    }

    private static List<GRoom> Quarters(int n, int H)
    {
        var rooms = new List<GRoom>();
        int left = n;
        while (left > 0)
        {
            int k = Math.Min(left, 12);
            int bottom = Ceil(k, 2), topc = k - bottom;
            int topY = H <= 5 ? 0 : 1;
            int w = Math.Max(8, 2 * Math.Max(bottom, topc) + 3);
            var r = new GRoom('q', w, H);
            for (int i = 0; i < bottom; i++) r.Put('B', 1 + 2 * i, H - 2, 1, 2);
            for (int i = 0; i < topc; i++)
            {
                int x = 1 + 2 * i + (topY == 0 ? 1 : 0);
                if (x < w - 1) r.Put('B', x, topY, 1, 2);
            }
            r.Put('K', w - 1, topY + 2);
            rooms.Add(r);
            left -= k;
        }
        return rooms;
    }

    private static GRoom Medbay(int n, int H)
    {
        int beds = Math.Max(2, Ceil(n, 5));
        int w = Math.Max(5, 2 * beds + 1);
        var r = new GRoom('h', w, H);
        r.Put('C', w - 1, 0);
        for (int i = 0; i < beds; i++) r.Put('M', 1 + 2 * i, H - 3, 1, 2);
        r.Put('K', 0, H - 1);
        return r;
    }

    private static GRoom Lounge(int n, int H)
    {
        int groups = Math.Max(1, Ceil(n, 6));
        var r = new GRoom('g', Math.Max(5, 4 * groups + 1), H);
        for (int i = 0; i < groups; i++)
        {
            int x = 1 + 4 * i;
            r.Put('S', x, 1, 2, 1); r.Put('T', x, 2, 2, 2);
            if (H > 5) r.Put('S', x, 4, 2, 1);
        }
        return r;
    }

    private static GRoom Nose(int hn)
    {
        int c = Math.Min(7, hn / 2 - 1);
        int w = c + 6;
        var r = new GRoom('b', w, hn);
        for (int y = 0; y < hn; y++)
        {
            int yy = Math.Min(y, hn - 1 - y);
            if (yy < c) for (int x = w - (c - yy); x < w; x++) r.Carve.Add((x, y));
        }
        int mid = hn / 2;
        r.Put('I', 1, mid - 1, 2, 2);
        r.Put('C', w - 2 - (r.Carve.Contains((w - 2, mid)) ? 1 : 0), mid);
        r.Put('S', w - 3, mid);
        r.Put('C', 5, 1); r.Put('S', 4, 1);
        r.Put('C', 5, hn - 2); r.Put('S', 4, hn - 2);
        return r;
    }

    private static GRoom Engine(int n, int h2)
    {
        int engines = n <= 12 ? 2 : 3;
        const int w = 7, c = 3;
        var r = new GRoom('e', w, h2);
        for (int y = 0; y < h2; y++)
        {
            int yy = Math.Min(y, h2 - 1 - y);
            if (yy < c) for (int x = 0; x < c - yy; x++) r.Carve.Add((x, y));
        }
        r.G[0][0] = '.'; r.G[0][c] = 'e';
        int span = h2 - 2 * c;
        for (int i = 0; i < engines; i++)
        {
            int y = c + (i + 1) * span / (engines + 1) - 1;
            r.Put('E', 0, y, 3, 3);
        }
        r.Put('C', w - 1, c + 1);
        return r;
    }

    /// <summary>새 방 하나: 표의 설비를 세로로 쌓아 가며 칸을 채운다 (문 줄인 맨 위·아래는 비운다).</summary>
    private static GRoom SpecialRoom(RoomSpec spec, char label, int H)
    {
        var items = new List<(char ch, int w, int h)>();
        foreach (var tok in spec.Furnish.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = tok[1..].Split('x');
            int iw = int.Parse(p[0]), ih = Math.Min(int.Parse(p[1]), H - 2), cnt = int.Parse(p[2]);
            for (int i = 0; i < cnt; i++) items.Add((tok[0], iw, ih));
        }
        // 세로로 쌓기: 한 기둥에 들어가는 만큼 → 다음 기둥
        var place = new List<(char ch, int x, int y, int w, int h)>();
        int cx = 1, cy = 1, colW = 0;
        foreach (var (ch, iw, ih) in items)
        {
            if (cy + ih > H - 1) { cx += colW + 1; cy = 1; colW = 0; }
            place.Add((ch, cx, cy, iw, ih));
            cy += ih + 1;
            colW = Math.Max(colW, iw);
        }
        int w = Math.Max(5, cx + colW + 1);
        var r = new GRoom(label, w, H) { Special = spec.Kind };
        foreach (var (ch, x, y, iw, ih) in place) r.Put(ch, x, y, iw, ih);
        return r;
    }

    // 기반 시설을 새로 나누는 방(정수실·배터리실·변전실…)은 무작위로 뽑지 않는다 — 크기에 따라 정해 넣는다 (InfraFor).
    private static readonly HashSet<RoomType> Infra = new()
    {
        RoomType.WaterPlant, RoomType.BatteryRoom, RoomType.FuelCell, RoomType.HvacRoom, RoomType.PumpRoom, RoomType.Substation,
        RoomType.HeatStorage, RoomType.BackupBridge, RoomType.Navigation, RoomType.CraneControl, RoomType.Security, RoomType.ServerRoom, RoomType.ComputerRoom,
    };

    private const string LegendChars = "dintuvxyz0123456789";

    private static readonly string[] Names = { "새벽호", "누리호", "가람호", "별빛호", "한울호", "다온호", "아라호", "해솔호", "나래호", "미르호", "온새미호", "여울호" };

    /// <summary>크기에 맞는 새 방 고르기: 소형은 표준 몇, 중형은 표준 여럿+대형 조금, 대형·초대형은 세대선 방까지.</summary>
    private static List<RoomSpec> PickExtras(int n, Rng rng, ShipPurpose purpose = ShipPurpose.General)
    {
        if (purpose != ShipPurpose.General) return PickExtrasFor(n, rng, purpose); // v16.9 용도가 방 고르기 가중치를 바꾼다
        int maxTier = n <= 6 ? 1 : n <= 12 ? 2 : 3;
        int count = n <= 4 ? 2 : n <= 6 ? 3 : n <= 12 ? 5 : n <= 20 ? 8 : 11;
        var pool = RoomCatalog.All.Where(s => s.Tier <= maxTier && !Infra.Contains(s.Kind)).ToList();
        var must = new List<RoomType> { RoomType.Shelter, RoomType.Gym };
        if (n > 6) must.Add(RoomType.Quarantine);
        var pick = pool.Where(s => must.Contains(s.Kind)).ToList();
        var rest = pool.Where(s => !must.Contains(s.Kind)).ToList();
        while (pick.Count < count && rest.Count > 0)
        {
            var s = rest[rng.Range(0, rest.Count)];
            rest.Remove(s);
            pick.Add(s);
        }
        pick = pick.Take(Math.Min(count, LegendChars.Length)).ToList();
        // 기반 시설 방: 큰 배일수록 생명유지·전력을 한 방에 몰지 않는다 (한 번의 사고가 배 전체를 멈추지 않게)
        foreach (var kind in InfraFor(n))
            if (pick.Count < LegendChars.Length && RoomCatalog.Of(kind) is RoomSpec spec) pick.Add(spec);
        return pick;
    }

    /// <summary>크기별 기반 시설 방: 12인 이상 정수실(예비 정수기 둘), 20인 이상 배터리실(축전지 넷), 30인 이상 공조실(산소 발생기 둘).</summary>
    public static IEnumerable<RoomType> InfraFor(int n)
    {
        // v16.22 크기에 따라 기반 시설 방을 나눠 둔다 (전에는 정수실 · 배터리실 · 공조실뿐이라 항법실 · 펌프실 · 서버실 같은 방이 어디에도 없었다)
        if (n >= 8) yield return RoomType.Navigation;
        if (n >= 12) { yield return RoomType.WaterPlant; yield return RoomType.PumpRoom; yield return RoomType.FuelCell; }
        if (n >= 16) { yield return RoomType.ServerRoom; yield return RoomType.Security; }
        if (n >= 20) { yield return RoomType.BatteryRoom; yield return RoomType.HeatStorage; yield return RoomType.Substation; }
        if (n >= 30) { yield return RoomType.HvacRoom; yield return RoomType.BackupBridge; yield return RoomType.CraneControl; }
    }

    public static string KeyFor(int crew, int seed) => $"gen:{crew}:{seed}";

    public static ShipTemplate? FromKey(string key)
    {
        var p = key.Split(':');
        if (p.Length == 5) return FromKeyV16(p); // v16.9 "gen:용도:뼈대:인원:시드"
        if (p.Length != 3 || p[0] != "gen" || !int.TryParse(p[1], out int n) || !int.TryParse(p[2], out int seed)) return null;
        return Template(Math.Clamp(n, 4, 40), seed);
    }

    private static readonly Dictionary<string, ShipTemplate> Cache = new();

    public static ShipTemplate Template(int n, int seed)
    {
        string key = KeyFor(n, seed);
        lock (Cache)
            if (Cache.TryGetValue(key, out var t)) return t;
        // 드물게 문이 막히는 배치가 나오면 조금 다르게 다시 뽑는다
        Exception? last = null;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                var (ascii, note, name) = Build(n, seed, attempt);
                var tpl = new ShipTemplate(key, name, n, ascii, note);
                lock (Cache) Cache[key] = tpl;
                return tpl;
            }
            catch (InvalidOperationException e) { last = e; }
        }
        throw new InvalidOperationException($"배를 만들지 못했습니다 ({key}): {last?.Message}");
    }

    private static (string ascii, string note, string name) Build(int n, int seed, int attempt, ShipPurpose purpose = ShipPurpose.General, ShipFrame frame = ShipFrame.Linear, int hDelta = 0)
    {
        var rng = new Rng(unchecked(seed * 7919 + attempt * 104729 + n));
        bool ring = frame == ShipFrame.Ring; // v16.9 고리형: 위아래 통로를 양 끝에서 이어 한 바퀴
        int H = n <= 4 ? 5 : n <= 6 ? 6 : n <= 12 ? 7 : 8;
        if (hDelta != 0) H = Math.Clamp(H + hDelta, n > 6 ? 6 : 5, 9); // v16.9 군용은 좁고 민간은 넓다
        int K = ring || n <= 12 ? 3 : 4;
        int s = n <= 6 ? 3 : n <= 12 ? 4 : n <= 20 ? 5 : n <= 40 ? 6 : 7;
        if (H < s + 1) H = s + 1; // 원자로가 들어가게 (예전 배는 늘 들어간다)
        float reactorKw = 48f * s * s / 9f;
        int pumps = Math.Max(2, Ceil((int)(reactorKw * 1.15f), 30));
        int beds = Math.Max(2, Ceil(4 * n, 6));

        var extras = PickExtras(n, rng, purpose);
        var legend = new StringBuilder();
        var specialRooms = new List<GRoom>();
        for (int i = 0; i < extras.Count; i++)
        {
            char ch = LegendChars[i];
            legend.Append('@').Append(ch).Append('=').Append(extras[i].Kind).Append('\n');
            specialRooms.Add(SpecialRoom(extras[i], ch, H));
        }

        var bands = Enumerable.Range(0, K).Select(_ => new List<GRoom>()).ToList();
        bands[0].AddRange(new[] { Reactor(n, H), Cooling(pumps, H), PowerRoom(Math.Max(2, 2 * Ceil(n, 6)), H), Workshop(Sized(purpose, 'w', n), H) });
        bands[1].AddRange(new[] { Life(n, H), Hydro(beds, H) });
        bands[K - 1].Add(Airlock(n, H));
        var tail0 = new List<GRoom> { CommsRoom(H) };
        var pool = new List<GRoom> { Storage(Sized(purpose, 's', n), H), Medbay(Sized(purpose, 'h', n), H), Lounge(Sized(purpose, 'g', n), H) };
        pool.AddRange(Quarters(n, H));
        pool.AddRange(specialRooms);
        // 시드마다 방 순서가 조금씩 다르다 (같은 너비끼리 섞이고, 가끔 한 칸씩 자리를 바꾼다)
        var jitter = pool.ToDictionary(r => r, _ => rng.Range(0f, 2.5f));
        pool = pool.OrderBy(r => -(r.W + jitter[r])).ToList();
        var galley = Galley(n, H);
        var mess = Mess(n, H);
        var ratio = new float[K];
        for (int b = 0; b < K; b++) ratio[b] = b == 0 || b == K - 1 ? 0.85f : 1f;
        var inner = Enumerable.Range(1, K - 2).ToList();
        int Width(int b)
        {
            var rooms = bands[b].Concat(b == 0 ? tail0 : Enumerable.Empty<GRoom>()).ToList();
            return rooms.Sum(r => r.W) + rooms.Count - 1 + (inner.Contains(b) ? 3 : 0);
        }
        int gmBand = inner[^1];
        bands[gmBand].Add(galley);
        bands[gmBand].Add(mess);
        foreach (var r in pool)
        {
            int bestB = 0;
            float best = float.MaxValue;
            for (int b = 0; b < K; b++)
            {
                float v = (Width(b) + r.W + 1) / ratio[b];
                if (v < best) { best = v; bestB = b; }
            }
            bands[bestB].Add(r);
        }
        bands[0].AddRange(tail0);

        const string widenOk = "swjmqhgfpl";
        bool CanWiden(GRoom r) => widenOk.Contains(r.Label) || r.Special != null;
        int wIn = Enumerable.Range(0, K).Max(Width);
        void Pad(int b, int target)
        {
            int extra = target - Width(b);
            var ok = bands[b].Where(r => CanWiden(r) && r.Label is not ('l' or 'f')).ToList();
            if (ok.Count == 0) ok = bands[b].Where(CanWiden).ToList();
            while (extra > 0 && ok.Count > 0)
            {
                ok.OrderBy(r => r.W).First().Widen(1);
                extra--;
            }
        }
        foreach (int b in inner) Pad(b, wIn);
        Pad(0, (int)(wIn * 0.62f));
        Pad(K - 1, (int)(wIn * 0.62f));
        foreach (var band in bands) foreach (var r in band) Furnish(r);
        var spineAt = new Dictionary<int, int>();
        foreach (int b in inner) spineAt[b] = ring ? 0 : Math.Max(b == 1 ? 2 : 1, bands[b].Count / 2); // 고리형: 왼쪽 끝에서 위아래 통로를 잇는다

        // ── 좌표 ──
        var bandY = Enumerable.Range(0, K).Select(b => 1 + b * (H + 4)).ToArray();
        int h2 = K * H + 4 * (K - 1);
        var eng = Engine(n, h2);
        int x0 = 1 + eng.W + 1;
        int ny0, ny1;
        if (K == 3) { ny0 = bandY[1] - 3; ny1 = bandY[1] + H + 2; }
        else { ny0 = bandY[1]; ny1 = bandY[2] + H - 1; }
        var nose = Nose(ny1 - ny0 + 1);
        var bodyEnd = new int[K];
        var xs = new List<(GRoom? room, int x)>[K];
        for (int b = 0; b < K; b++)
        {
            int x = x0;
            var pos = new List<(GRoom?, int)>();
            for (int i = 0; i < bands[b].Count; i++)
            {
                if (spineAt.TryGetValue(b, out int si) && i == si) { pos.Add((null, x)); x += 3; }
                pos.Add((bands[b][i], x));
                x += bands[b][i].W + 1;
            }
            xs[b] = pos;
            bodyEnd[b] = x - 2;
        }
        int xNose = Enumerable.Range(0, K).Where(b => inner.Contains(b) || K == 3 && b == 1).Max(b => bodyEnd[b]) + 2 + (ring ? 3 : 0);
        foreach (int b in inner)
        {
            int gap = xNose - 2 - bodyEnd[b] - (ring ? 3 : 0);
            if (gap > 0)
            {
                xs[b].Where(p => p.room != null).Last().room!.Widen(gap);
                bodyEnd[b] += gap;
            }
            if (ring) bodyEnd[b] = xNose - 2; // 오른쪽 끝 연결 통로 (xNose-3 · xNose-2)
        }
        int W = xNose + nose.W + 2;
        int Ht = 1 + h2 + 1;
        var grid = new char[Ht, W];
        for (int y = 0; y < Ht; y++) for (int x = 0; x < W; x++) grid[y, x] = ' ';

        void Blit(GRoom room, int gx, int gy)
        {
            for (int yy = 0; yy < room.H; yy++)
                for (int xx = 0; xx < room.W; xx++)
                {
                    if (room.Carve.Contains((xx, yy))) continue;
                    grid[gy + yy, gx + xx] = room.G[yy][xx];
                }
        }

        Blit(eng, 1, 1);
        for (int b = 0; b < K; b++)
            foreach (var (r, x) in xs[b])
            {
                if (r == null)
                {
                    for (int yy = bandY[b] - 1; yy < bandY[b] + H + 1; yy++) { grid[yy, x] = '.'; grid[yy, x + 1] = '.'; }
                }
                else Blit(r, x, bandY[b]);
            }
        if (ring)
            foreach (int b in inner)
                for (int yy = bandY[b] - 1; yy < bandY[b] + H + 1; yy++) { grid[yy, xNose - 3] = '.'; grid[yy, xNose - 2] = '.'; }
        Blit(nose, xNose, ny0);
        var corrRows = new List<(int cy, int end)>();
        for (int b = 0; b < K - 1; b++)
        {
            int cy = bandY[b] + H + 1;
            int end = Math.Min(Math.Max(bodyEnd[b], bodyEnd[b + 1]), xNose - 2);
            foreach (int yy in new[] { cy, cy + 1 })
                for (int xx = x0; xx <= end; xx++) grid[yy, xx] = '.';
            corrRows.Add((cy, end));
        }
        grid[corrRows[0].cy, x0 + 1] = 'c';

        // ── 벽: 바닥에 8방향으로 닿은 빈칸은 벽 ──
        bool Floorish(char ch) => ch != ' ' && ch != '#';
        for (int y = 0; y < Ht; y++)
            for (int x = 0; x < W; x++)
            {
                if (grid[y, x] != ' ') continue;
                bool near = false;
                for (int dy = -1; dy <= 1 && !near; dy++)
                    for (int dx = -1; dx <= 1 && !near; dx++)
                    {
                        int yy = y + dy, xx = x + dx;
                        if (yy >= 0 && yy < Ht && xx >= 0 && xx < W && Floorish(grid[yy, xx])) near = true;
                    }
                if (near) grid[y, x] = '#';
            }

        void Door(int x, int y)
        {
            if (grid[y, x] != '#') throw new InvalidOperationException($"벽이 아닌 곳에 문 ({x},{y}) '{grid[y, x]}'");
            grid[y, x] = '+';
        }

        int? Pick(GRoom room, int gx, int insideRow, Func<int, bool>? prefer = null)
        {
            var free = Enumerable.Range(0, room.W).Where(xx => (room.G[insideRow][xx] == '.' || room.G[insideRow][xx] == room.Label) && !room.Carve.Contains((xx, insideRow))).ToList();
            if (prefer != null) { var pf = free.Where(prefer).ToList(); if (pf.Count > 0) free = pf; }
            if (free.Count == 0) return null;
            return gx + free.OrderBy(xx => Math.Abs(xx - room.W / 2)).First();
        }

        for (int b = 0; b < K; b++)
            foreach (var (r, x) in xs[b])
            {
                if (r == null) continue;
                if (b > 0 && Pick(r, x, 0, r.Label == 'l' ? xx => xx <= 2 : null) is int px) Door(px, bandY[b] - 1);
                if (b < K - 1 && Pick(r, x, H - 1, r.Label == 'k' ? xx => xx >= r.W - 3 : null) is int qx) Door(qx, bandY[b] + H);
                if (r.Label == 'a' && Pick(r, x, H - 1) is int ax) Door(ax, bandY[b] + H);
            }
        foreach (var (cy, _) in corrRows) { Door(x0 - 1, cy); Door(x0 - 1, cy + 1); }
        foreach (var (cy, end) in corrRows)
            if (ny0 <= cy && cy <= ny1 && end == xNose - 2) { Door(xNose - 1, cy); Door(xNose - 1, cy + 1); }
        // 구획 격벽 (20인 이상): 통로를 가로질러 격벽 문 — 한쪽이 뚫려도 다른 구획은 지킨다
        int bulkheads = ring ? (n >= 30 ? 3 : 2) : n >= 30 ? 2 : n >= 20 ? 1 : 0; // 고리형은 늘 격벽으로 나눈다 (한쪽이 막혀도 반대로)
        var spineXs = new List<int>();
        foreach (int b in inner) foreach (var (r, x) in xs[b]) if (r == null) spineXs.Add(x);
        if (ring) spineXs.Add(xNose - 3);
        for (int k = 1; k <= bulkheads; k++)
        {
            int target = x0 + (xNose - 2 - x0) * k / (bulkheads + 1);
            for (int off = 0; off < 30; off++)
            {
                int bx = target + (off % 2 == 0 ? off / 2 : -(off / 2 + 1));
                bool ok = spineXs.All(sx => bx < sx - 1 || bx > sx + 2) && corrRows.All(cr => cr.end > bx + 2);
                foreach (var (cy, _) in corrRows)
                    ok &= grid[cy - 1, bx] == '#' && grid[cy + 2, bx] == '#' && grid[cy, bx] == '.' && grid[cy + 1, bx] == '.'
                          && grid[cy - 1, bx - 1] != '+' && grid[cy - 1, bx + 1] != '+' && grid[cy + 2, bx - 1] != '+' && grid[cy + 2, bx + 1] != '+';
                if (!ok) continue;
                foreach (var (cy, _) in corrRows) { grid[cy, bx] = '+'; grid[cy + 1, bx] = '+'; }
                break;
            }
        }
        // 라벨 없는 바닥 덩어리(격벽에 잘린 통로)에는 통로 라벨
        {
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
        var sb = new StringBuilder();
        sb.Append(legend);
        for (int y = 0; y < Ht; y++)
        {
            var line = new StringBuilder();
            for (int x = 0; x < W; x++) line.Append(grid[y, x]);
            sb.Append(line.ToString().TrimEnd()).Append('\n');
        }
        string name = purpose == ShipPurpose.General ? Names[(int)((uint)seed % Names.Length)] : NameFor(purpose, seed);
        string note = $"생성 · {n}인 · 원자로 {s}×{s} · 펌프 {pumps} · 새 방 {extras.Count}: {string.Join("·", extras.Select(e => e.Name))}";
        return (sb.ToString(), note, name);
    }

    /// <summary>넓힌 방의 빈 자리를 그 방답게 채운다 (생성기와 같다).</summary>
    private static void Furnish(GRoom room)
    {
        int w0 = room.W0 >= 0 ? room.W0 : room.W;
        int H = room.H;
        bool Free(int x, int y, int w, int h)
        {
            for (int yy = y; yy < y + h; yy++)
                for (int xx = x; xx < x + w; xx++)
                    if (!(xx >= 0 && xx < room.W - 1 && yy >= 0 && yy < H && room.G[yy][xx] == '.')) return false;
            return true;
        }
        char L = room.Special != null ? '?' : room.Label;
        int x = L == 'j' ? 1 : w0 + 1;
        if (L == 'j' && H < 7) return;
        while (x < room.W - 2)
        {
            if (L == 'm' && H >= 5 && Free(x, 2, 2, 3)) { room.Put('S', x, 2, 2, 1); room.Put('T', x, 3, 2, 1); room.Put('S', x, 4, 2, 1); x += 3; }
            else if (L == 'g' && H >= 6 && Free(x, 1, 2, 4)) { room.Put('S', x, 1, 2, 1); room.Put('T', x, 2, 2, 2); room.Put('S', x, 4, 2, 1); x += 4; }
            else if (L == 'j' && Free(x, H / 2, 2, 1) && Free(x, H / 2 - 1, 2, 1) && Free(x, H / 2 + 1, 2, 1)) { room.Put('T', x, H / 2, 2, 1); x += 3; }
            else if (L == 's' && H >= 7 && Free(x, 3, 2, 2)) { room.Put('K', x, 3, 2, 2); x += 3; }
            else x++;
        }
    }
}
