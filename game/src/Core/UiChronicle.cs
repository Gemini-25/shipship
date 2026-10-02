using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ShipSim.Core;

// v16.24 연대기 = 타임라인 + 장(章) 요약 + 일기 발췌 (읽기만 — 화면이 있든 없든 결과가 같다).
//  · 타임라인: 사건(규모 사건 · 죽음 · 익힌 기술 · 이정표 · 결정)을 시각 순으로. 사건은 규모(① ~ ⑤) 색으로 칠한다.
//  · 장: 하루 또는 한 주. 신문 머리기사 같은 한 줄 + 부제(숫자) + 주 컴퓨터 항해 일지 + 승무원 일기 발췌(같은 사건을 서로 다르게)
//    + 그날의 사진 자리(시각 · 자리) + 숫자.
//  · 거르기: 사람 · 방 · 물건 · 기술 · 규모. 누르면 그 장면(시각 · 자리)으로.
// 난수 없음 · 사전 순회 없음 (목록 순서 그대로) → 같은 세계면 같은 장.

public enum ChronFilterKind : byte { All, Person, Room, Thing, Tech, Scale }

/// <summary>연대기 거르기 한 가지.</summary>
public readonly record struct ChronFilter(ChronFilterKind Kind, int Id = -1, string Key = "", IncidentScale MinScale = IncidentScale.Personal)
{
    public static readonly ChronFilter All = new(ChronFilterKind.All);
    public bool IsAll => Kind == ChronFilterKind.All;
}

/// <summary>타임라인의 점 하나.</summary>
public sealed record ChronMark(long Tick, long End, IncidentScale? Scale, string Kind, string Title, int RoomId, int[] Crew, Vector2? At,
    int CaseId = -1, int CauseRoot = -1, int[]? Things = null, string Tech = "")
{
    public bool IsCase => Kind == "case";
}

/// <summary>일기 한 토막 (누가 · 언제 · 무엇을). About: 머리기사 사건을 겪은 사람의 글.</summary>
public sealed record DiaryBit(int CrewId, string Name, long Tick, string Text, bool About, string Role);

/// <summary>연대기 한 장 (하루 또는 한 주).</summary>
public sealed class ChronChapter
{
    public int Index { get; init; }
    public bool Week { get; init; }
    public int FirstDay { get; init; }
    public int LastDay { get; init; }
    public long Start { get; init; }
    public long End { get; init; }
    public string Title => Week ? $"{(FirstDay - 1) / 7 + 1}주 ({FirstDay}~{LastDay}일)" : $"{FirstDay}일";
    /// <summary>신문 머리기사 한 줄.</summary>
    public string Headline { get; set; } = "";
    /// <summary>머리기사 아래 한 줄 (어떻게 끝났나 · 숫자).</summary>
    public string Deck { get; set; } = "";
    public ChronMark? Lead { get; set; }
    public List<ChronMark> Marks { get; } = new();
    /// <summary>주 컴퓨터 항해 일지 (시각 · 한 줄).</summary>
    public List<(long Tick, string Text)> ShipLog { get; } = new();
    public List<DiaryBit> Diaries { get; } = new();
    public int[] ByScale { get; } = new int[5];
    public int Hurt { get; set; }
    public int Deaths { get; set; }
    public int Acts { get; set; }
    public int Decisions { get; set; }
    public int Learned { get; set; }
    public int DiaryCount { get; set; }
    /// <summary>그날의 사진 자리 (시각 · 자리 · 방). 사진을 찍는 사람이 생기면 여기에 건다.</summary>
    public (long Tick, Vector2? At, int RoomId)? Photo { get; set; }
    public IncidentScale? Peak { get { IncidentScale? p = null; foreach (var m in Marks) if (m.Scale is IncidentScale s && (p == null || s > p)) p = s; return p; } }
}

public static class ChronicleBook
{
    // ─────────────────────────── 타임라인 ───────────────────────────

    /// <summary>[from, to) 사이의 점들 (시각 순). 거르기를 주면 맞는 것만.</summary>
    public static List<ChronMark> Timeline(World w, long from, long to, ChronFilter f)
    {
        var list = new List<ChronMark>();
        // 1) 규모 사건
        foreach (var k in w.Scale.Cases)
        {
            if (k.Start < from || k.Start >= to) continue;
            var crew = new List<int>();
            if (k.CrewId >= 0) crew.Add(k.CrewId);
            foreach (var id in k.Workers) if (!crew.Contains(id)) crew.Add(id);
            foreach (var id in k.Mustered) if (!crew.Contains(id)) crew.Add(id);
            foreach (var id in k.Feared) if (!crew.Contains(id)) crew.Add(id);
            var things = new List<int>();
            Vector2? at = null;
            if (k.Root >= 0 && k.Root < w.Causes.Nodes.Count)
            {
                var root = w.Causes.Node(k.Root);
                at = root.At;
                if (w.Causes.IncidentOf(k.Root) is CauseIncident inc)
                    foreach (var nid in inc.Nodes) if (ThingOf(w.Causes.Node(nid).Key) is int fid && !things.Contains(fid)) things.Add(fid);
            }
            foreach (var mc in w.Major.Cases)
            {
                if (mc.Node < 0 || k.Root < 0 || w.Causes.IncidentOf(mc.Node)?.Root != k.Root) continue;
                if (mc.Machine >= 0 && !things.Contains(mc.Machine)) things.Add(mc.Machine);
                foreach (var fid in mc.Furn) if (!things.Contains(fid)) things.Add(fid);
            }
            at ??= RoomCenter(w, k.RoomId);
            list.Add(new ChronMark(k.Start, k.End, k.Peak, "case", CaseTitle(w, k), k.RoomId, crew.ToArray(), at, k.Id, k.Root, things.ToArray()));
        }
        // 2) 역사의 큰 줄: 죽음 · 이정표 · 결정 · 전우 · 개조
        foreach (var e in w.History.Events)
        {
            if (e.Tick < from || e.Tick >= to) continue;
            string? kind = e.Kind switch
            {
                HistoryKind.Death => "death", HistoryKind.Milestone => "milestone", HistoryKind.Decision => "decision",
                HistoryKind.Bond => "bond", HistoryKind.Upgrade => "upgrade", HistoryKind.Lesson => "lesson", _ => null,
            };
            if (kind == null) continue;
            Vector2? at = e.At is Cell c ? new Vector2(c.X + 0.5f, c.Y + 0.5f) : RoomCenter(w, e.RoomId);
            list.Add(new ChronMark(e.Tick, e.Tick, null, kind, e.Text, e.RoomId, e.CrewIds, at));
        }
        // 3) 익힌 기술
        foreach (var (id, tick) in w.TechWeb.Learned)
        {
            if (tick < from || tick >= to) continue;
            string name = EraSystem.Find(id)?.Name ?? id;
            list.Add(new ChronMark(tick, tick, null, "tech", $"{Ko.EulReul(name)} 익혔다", -1, Array.Empty<int>(), null, Tech: id));
        }
        list.Sort((a, b) => a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick) : string.CompareOrdinal(a.Kind, b.Kind));
        if (f.IsAll) return list;
        return list.Where(m => Matches(m, f)).ToList();
    }

    /// <summary>점 하나가 거르기에 맞나.</summary>
    public static bool Matches(ChronMark m, ChronFilter f) => f.Kind switch
    {
        ChronFilterKind.All => true,
        ChronFilterKind.Person => m.Crew.Contains(f.Id),
        ChronFilterKind.Room => m.RoomId == f.Id,
        ChronFilterKind.Thing => m.Things != null && m.Things.Contains(f.Id),
        ChronFilterKind.Tech => m.Tech != "" && (f.Key == "" || m.Tech == f.Key),
        ChronFilterKind.Scale => m.Scale is IncidentScale s && s >= f.MinScale,
        _ => true,
    };

    /// <summary>"fault:12:…" · "ext:12:…" 고리 열쇠 → 설비 번호.</summary>
    private static int? ThingOf(string key)
    {
        if (!(key.StartsWith("fault:", StringComparison.Ordinal) || key.StartsWith("ext:", StringComparison.Ordinal))) return null;
        int a = key.IndexOf(':') + 1, b = key.IndexOf(':', a);
        return int.TryParse(b > a ? key[a..b] : key[a..], out var id) ? id : null;
    }

    private static Room? RoomOf(World w, int id) => id >= 0 && id < w.Ship.Rooms.Count ? w.Ship.Rooms[id] : null;

    private static Vector2? RoomCenter(World w, int id)
    {
        if (RoomOf(w, id) is not Room r || r.Cells.Count == 0) return null;
        float x = 0, y = 0;
        foreach (var c in r.Cells) { x += c.X; y += c.Y; }
        return new Vector2(x / r.Cells.Count + 0.5f, y / r.Cells.Count + 0.5f);
    }

    /// <summary>"침실 화재" + 방 이름 "그날의 침실" → "그날의 침실 화재" (방 이름이 이미 들어 있으면 그대로).</summary>
    private static string CaseTitle(World w, ScaleCase k)
    {
        if (RoomOf(w, k.RoomId) is not Room r) return k.Name;
        string where = r.Name, plain = RoomTypes.Name(r.Kind);
        if (k.Name.Contains(where)) return k.Name;
        if (k.Name.StartsWith(plain, StringComparison.Ordinal)) return where + k.Name[plain.Length..];
        return $"{where} {k.Name}";
    }

    /// <summary>머리기사용으로 줄인다: 괄호 속 덧붙임을 떼고 n자.</summary>
    private static string Head(string s, int n)
    {
        int p = s.IndexOf(" (", StringComparison.Ordinal);
        if (p >= 8) s = s[..p];
        return Short(s, n);
    }

    // ─────────────────────────── 장 ───────────────────────────

    /// <summary>장 목록 (오래된 것부터): 하루씩 또는 한 주씩. 거르기를 주면 맞는 것이 있는 장만.</summary>
    public static List<ChronChapter> Chapters(World w, bool weekly, ChronFilter f)
    {
        var list = new List<ChronChapter>();
        int first = SimTime.Day(Math.Max(0, w.History.FoundedTick)), last = w.Day;
        int step = weekly ? 7 : 1;
        int startDay = weekly ? (first - 1) / 7 * 7 + 1 : first;
        for (int d = startDay, i = 0; d <= last; d += step, i++)
        {
            long start = (long)(d - 1) * SimTime.TicksPerDay, end = Math.Min((long)(d - 1 + step) * SimTime.TicksPerDay, w.Tick + 1);
            var ch = Build(w, i, weekly, Math.Max(d, first), Math.Min(d + step - 1, last), start, end, f);
            if (!f.IsAll && ch.Marks.Count == 0 && ch.Diaries.Count == 0) continue;
            list.Add(ch);
        }
        return list;
    }

    /// <summary>한 장 만들기.</summary>
    public static ChronChapter Build(World w, int index, bool weekly, int firstDay, int lastDay, long start, long end, ChronFilter f)
    {
        var ch = new ChronChapter { Index = index, Week = weekly, FirstDay = firstDay, LastDay = lastDay, Start = start, End = end };
        ch.Marks.AddRange(Timeline(w, start, end, f));
        foreach (var m in ch.Marks)
        {
            if (m.Scale is IncidentScale s) ch.ByScale[(int)s]++;
            if (m.Kind == "death") ch.Deaths++;
            else if (m.Kind == "decision") ch.Decisions++;
            else if (m.Kind == "tech") ch.Learned++;
        }
        foreach (var inc in w.Causes.Incidents)
            if (inc.Start >= start && inc.Start < end) ch.Hurt += inc.Casualties;
        foreach (var a in w.Automation.Book.Acts)
            if (a.Tick >= start && a.Tick < end) ch.Acts++;
        ch.Lead = LeadOf(ch.Marks);
        ShipLog(w, ch);
        Diaries(w, ch, f);
        ch.Headline = Headline(w, ch);
        ch.Deck = Deck(w, ch);
        if (ch.Lead != null) ch.Photo = (ch.Lead.Tick, ch.Lead.At, ch.Lead.RoomId);
        return ch;
    }

    /// <summary>머리기사 감: 가장 큰 규모 사건 → 죽음 → 기술 · 이정표 · 결정 · 전우.</summary>
    public static ChronMark? LeadOf(List<ChronMark> marks)
    {
        ChronMark? best = null;
        int bestRank = int.MinValue;
        foreach (var m in marks)
        {
            int rank = m.Scale is IncidentScale s ? 100 + (int)s * 10 : m.Kind switch
            {
                "death" => 135, "milestone" => 60, "tech" => 55, "upgrade" => 50, "decision" => 45, "bond" => 40, "lesson" => 35, _ => 0,
            };
            if (m.Kind == "case" && m.Scale == IncidentScale.Personal) rank = 30; // 혼자 다친 일은 조용한 날의 머리기사 정도
            if (rank > bestRank) { best = m; bestRank = rank; }
        }
        return best;
    }

    /// <summary>신문 머리기사 같은 한 줄.</summary>
    public static string Headline(World w, ChronChapter ch)
    {
        var m = ch.Lead;
        if (m == null) return Quiet(w, ch);
        if (m.IsCase && w.Scale.CaseById(m.CaseId) is ScaleCase k)
        {
            string spread = k.Peak switch
            {
                IncidentScale.Cosmic => ", 배가 길을 틀었다",
                IncidentScale.Ship => ", 배 전체가 비상",
                IncidentScale.System => ", 이웃 칸까지 번져",
                _ => "",
            };
            var inc = k.Root >= 0 ? w.Causes.IncidentOf(k.Root) : null;
            string outcome = inc is { Deaths: > 0 } ? $"{inc.Deaths}명 숨져"
                : k.Open ? "아직 잡히지 않았다"
                : inc is { Casualties: > 0 } ? $"{inc.Casualties}명 다쳐"
                : $"{Dur(k.End - k.Start)} 만에 잡혀";
            return $"{m.Title}{spread} — {outcome}";
        }
        string t = Head(m.Title, 36);
        return m.Kind switch
        {
            "death" => t,
            "tech" => $"새 솜씨 — {t}",
            "milestone" => t,
            "decision" => $"회의 — {t}",
            "bond" => t,
            _ => t,
        };
    }

    /// <summary>사건이 없는 날: 일기 한 줄을 머리기사로 (누가 무엇을 했나).</summary>
    private static string Quiet(World w, ChronChapter ch)
    {
        var d = ch.Diaries.FirstOrDefault();
        string span = ch.Week ? "조용한 한 주" : "조용한 하루";
        return d != null ? $"{span} — {d.Name}: \"{Short(d.Text, 30)}\"" : span;
    }

    private static string Deck(World w, ChronChapter ch)
    {
        var parts = new List<string>();
        int cases = ch.ByScale.Sum();
        if (cases > 0) parts.Add($"사고 {cases}건");
        if (ch.Hurt > 0) parts.Add($"다친 사람 {ch.Hurt}");
        if (ch.Deaths > 0) parts.Add($"숨진 사람 {ch.Deaths}");
        if (ch.Lead is { IsCase: true } m && w.Scale.CaseById(m.CaseId) is ScaleCase k && k.Responders > 0) parts.Add($"나선 사람 {k.Responders}");
        if (ch.Learned > 0) parts.Add($"익힌 기술 {ch.Learned}");
        if (ch.Decisions > 0) parts.Add($"회의 결정 {ch.Decisions}");
        if (ch.Acts > 0) parts.Add($"컴퓨터 조치 {ch.Acts}");
        return string.Join(" · ", parts);
    }

    private static readonly ActKind[] LogKinds = { ActKind.Alarm, ActKind.Bulkhead, ActKind.Suppress, ActKind.Breaker, ActKind.Shed, ActKind.Broadcast, ActKind.Proposal, ActKind.Valve, ActKind.Reboot, ActKind.Forecast };

    /// <summary>주 컴퓨터 항해 일지: 여섯 시간마다 적은 줄 → 그날의 굵은 조치 → 하루 보고.</summary>
    private static void ShipLog(World w, ChronChapter ch)
    {
        int cap = ch.Week ? 6 : 4;
        if (w.Automation.MateOrNull is ShipMate mate) foreach (var (tick, text) in mate.Evenings) if (tick >= ch.Start && tick < ch.End && ch.ShipLog.Count < cap / 2) ch.ShipLog.Add((tick, Short(StripDay(text), 90))); // v16.27 저녁 항해 일지
        if (w.Automation.Apps is ComputerApps apps)
            foreach (var (tick, text) in apps.Logbook)
                if (tick >= ch.Start && tick < ch.End && ch.ShipLog.Count < cap / 2) ch.ShipLog.Add((tick, Short(StripDay(text), 70)));
        foreach (var a in w.Automation.Book.Acts)
        {
            if (ch.ShipLog.Count >= cap) break;
            if (a.Tick < ch.Start || a.Tick >= ch.End || Array.IndexOf(LogKinds, a.Kind) < 0 || a.Act == "") continue;
            string room = RoomOf(w, a.RoomId)?.Name is string rn ? $"{rn} — " : "";
            ch.ShipLog.Add((a.Tick, Short(room + a.Act, 70)));
        }
        foreach (var r in w.Automation.Book.Reports)
        {
            if (ch.ShipLog.Count >= cap + 1) break;
            long t = (long)r.Day * SimTime.TicksPerDay - 1; // 하루 보고는 그날 끝에 적힌다
            if (r.Day >= ch.FirstDay && r.Day <= ch.LastDay) ch.ShipLog.Add((Math.Min(t, ch.End - 1), Short(r.Text.Replace($"{r.Day}일 컴퓨터 보고: ", "하루 정리: "), 80)));
        }
        ch.ShipLog.Sort((a, b) => a.Tick.CompareTo(b.Tick));
    }

    private static string StripDay(string text)
    {
        int i = text.IndexOf(" — ", StringComparison.Ordinal);
        return i > 0 && i < 14 ? text[(i + 3)..] : text;
    }

    /// <summary>
    /// 일기 발췌: 머리기사 사건을 겪은 사람들의 글을 먼저 (같은 사건을 서로 다르게 — 나선 사람 · 겁먹은 사람 · 소식만 들은 사람),
    /// 그다음 그 장의 다른 일기. 한 사람에 한 토막 · 일기가 없으면 마음에 남은 것(기억)으로.
    /// </summary>
    private static void Diaries(World w, ChronChapter ch, ChronFilter f)
    {
        int cap = ch.Week ? 4 : 3;
        var k = ch.Lead is { IsCase: true } lm ? w.Scale.CaseById(lm.CaseId) : null;
        string RoleOf(CrewMember c)
        {
            if (k == null) return "";
            if (k.CrewId == c.Id) return "겪은 사람";
            if (k.Workers.Contains(c.Id)) return "나선 사람";
            if (k.Feared.Contains(c.Id)) return "겁먹은 사람";
            if (k.Mustered.Contains(c.Id)) return "모인 사람";
            if (k.Told.Contains(c.Id)) return "들은 사람";
            return "";
        }
        var used = new HashSet<int>();
        int count = 0;
        // 1) 머리기사 사건 → 2) 그 밖의 일기 (사람 순서는 승무원 목록 순서)
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var c in w.Crew)
            {
                if (f.Kind == ChronFilterKind.Person && c.Id != f.Id) continue;
                foreach (var (tick, text) in c.Diary)
                {
                    if (tick < ch.Start || tick >= ch.End) continue;
                    if (pass == 0) count++;
                    if (used.Contains(c.Id) || ch.Diaries.Count >= cap) continue;
                    string role = RoleOf(c);
                    bool about = k != null && role != "" && tick >= k.Start && tick <= (k.End >= 0 ? k.End : w.Tick) + SimTime.Hours(30);
                    if (pass == 0 && !about) continue;
                    if (pass == 1 && f.Kind is not (ChronFilterKind.All or ChronFilterKind.Person) && !about) continue;
                    ch.Diaries.Add(new DiaryBit(c.Id, c.Name, tick, text, about, role));
                    used.Add(c.Id);
                }
            }
        }
        ch.DiaryCount = count;
        // 3) 머리기사 사건을 겪은 사람이 일기를 안 썼으면: 마음에 남은 것
        if (k != null && ch.Diaries.Count(d => d.About) < 2)
            foreach (var c in w.Crew)
            {
                if (ch.Diaries.Count >= cap + 1 || used.Contains(c.Id) || RoleOf(c) == "") continue;
                if (f.Kind == ChronFilterKind.Person && c.Id != f.Id) continue;
                foreach (var mk in c.Memory.Marks)
                {
                    if (mk.Tick < k.Start || mk.Tick >= ch.End + SimTime.Hours(30)) continue;
                    ch.Diaries.Add(new DiaryBit(c.Id, c.Name, mk.Tick, mk.Text, true, RoleOf(c)));
                    used.Add(c.Id);
                    break;
                }
            }
        ch.Diaries.Sort((a, b) => a.About != b.About ? (a.About ? -1 : 1) : a.Tick.CompareTo(b.Tick));
    }

    // ─────────────────────────── 거르기 고르개 ───────────────────────────

    /// <summary>거를 수 있는 것들: 사건에 나온 사람 · 방 · 물건 · 기술 (많이 나온 순 · 같으면 번호 순).</summary>
    public static List<(ChronFilter Filter, string Label, int Count)> Choices(World w, ChronFilterKind kind, int max = 8)
    {
        var all = Timeline(w, 0, long.MaxValue, ChronFilter.All);
        var counts = new SortedDictionary<int, int>();
        var keys = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var m in all)
        {
            switch (kind)
            {
                case ChronFilterKind.Person: foreach (var id in m.Crew) counts[id] = counts.GetValueOrDefault(id) + 1; break;
                case ChronFilterKind.Room: if (m.RoomId >= 0) counts[m.RoomId] = counts.GetValueOrDefault(m.RoomId) + 1; break;
                case ChronFilterKind.Thing: if (m.Things != null) foreach (var id in m.Things) counts[id] = counts.GetValueOrDefault(id) + 1; break;
                case ChronFilterKind.Tech: if (m.Tech != "") keys[m.Tech] = keys.GetValueOrDefault(m.Tech) + 1; break;
            }
        }
        var list = new List<(ChronFilter, string, int)>();
        if (kind == ChronFilterKind.Scale)
        {
            foreach (var s in ScaleTable.Scales)
            {
                int n = all.Count(m => m.Scale is IncidentScale ms && ms >= s);
                if (n > 0) list.Add((new ChronFilter(ChronFilterKind.Scale, MinScale: s), $"{ScaleTable.Mark(s)} 이상", n));
            }
            return list;
        }
        if (kind == ChronFilterKind.Tech)
        {
            foreach (var (k, n) in keys.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Take(max))
                list.Add((new ChronFilter(ChronFilterKind.Tech, Key: k), EraSystem.Find(k)?.Name ?? k, n));
            return list;
        }
        foreach (var (id, n) in counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key).Take(max))
        {
            string? label = kind switch
            {
                ChronFilterKind.Person => w.Crew.FirstOrDefault(c => c.Id == id)?.Name,
                ChronFilterKind.Room => RoomOf(w, id)?.Name,
                ChronFilterKind.Thing => w.Ship.Furniture.FirstOrDefault(x => x.Id == id) is Furniture fu ? $"{fu.Room.Name} {fu.Name}" : null,
                _ => null,
            };
            if (label != null) list.Add((new ChronFilter(kind, id), label, n));
        }
        return list;
    }

    // ─────────────────────────── 글 ───────────────────────────

    public static string Dur(long ticks)
    {
        float h = ticks / (float)SimTime.TicksPerHour;
        return h < 1f ? $"{MathF.Max(1f, h * 60f):0}분" : h < 48f ? $"{h:0.#}시간" : $"{h / 24f:0.#}일";
    }

    public static string Short(string s, int n) => s.Length <= n ? s : s[..(n - 1)].TrimEnd() + "…";
}
