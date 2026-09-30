using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>
/// v12.0 전조의 진짜 원인. 같은 기척(진동·과열·압력·계기)에도 원인이 여럿이라, 무엇을 갈아야 하는지는 열어 봐야 안다.
/// </summary>
public enum OmenCause
{
    LooseMount,       // 고정 볼트 풀림 (진동) — 조이면 된다
    BearingWear,      // 베어링 마모 (진동) — 윤활유
    ImpellerDamage,   // 회전부 손상 (진동, 도는 설비) — 베어링
    LooseTerminal,    // 단자 풀림 (과열) — 조이면 된다
    InsulationDamage, // 피복 손상 (과열) — 케이블
    Overload,         // 과부하 접점 (과열) — 퓨즈
    FilterClog,       // 필터 막힘 (압력, 거르는 설비) — 필터
    MicroLeak,        // 미세 누수 (압력, 물·냉각수가 도는 설비) — 실링폼
    StickyValve,      // 구동부 걸림 (압력) — 윤활유
    SensorOffset,     // 센서 영점 틀어짐 (계기) — 다시 맞춘다
    BoardFault,       // 제어 기판 불량 (계기) — 전자재
    Connector,        // 커넥터 접촉 불량 (계기) — 닦고 끼운다
    Phantom,          // 계기 오류: 설비는 멀쩡하다, 감지기 교정이 틀어졌다 (감지기만 낸다)
}

/// <summary>원인 하나의 성질: 어떤 기척을 내고, 무엇으로 고치고, 열어 보면 무엇이 보이는지.</summary>
public sealed record CauseSpec(OmenCause Cause, OmenKind Symptom, string Name, (ItemKind kind, int count)[] Fix, float Hours, string Finding, float Weight);

public static class Causes
{
    private static (ItemKind, int)[] F(params (ItemKind, int)[] x) => x;

    private static readonly Dictionary<OmenCause, CauseSpec> Table = new[]
    {
        new CauseSpec(OmenCause.LooseMount, OmenKind.Vibration, "고정 볼트 풀림", F(), 0.3f, "받침 볼트가 풀려 떨고 있었다 — 조였다", 1f),
        new CauseSpec(OmenCause.BearingWear, OmenKind.Vibration, "베어링 마모", F((ItemKind.Lubricant, 1)), 0.5f, "베어링이 닳아 갈리는 소리가 났다", 1.3f),
        new CauseSpec(OmenCause.ImpellerDamage, OmenKind.Vibration, "회전부 손상", F((ItemKind.Bearing, 1)), 0.8f, "회전날개 끝이 깎여 균형이 틀어져 있었다", 0.6f),
        new CauseSpec(OmenCause.LooseTerminal, OmenKind.Heat, "단자 풀림", F(), 0.3f, "단자 나사가 풀려 불꽃 자국이 나 있었다 — 조였다", 1f),
        new CauseSpec(OmenCause.InsulationDamage, OmenKind.Heat, "피복 손상", F((ItemKind.Cable, 1)), 0.5f, "전선 피복이 녹아 구리가 드러나 있었다", 1.1f),
        new CauseSpec(OmenCause.Overload, OmenKind.Heat, "과부하 접점", F((ItemKind.Fuse, 1)), 0.4f, "접점이 과부하로 그을려 있었다", 0.7f),
        new CauseSpec(OmenCause.FilterClog, OmenKind.Pressure, "필터 막힘", F((ItemKind.Filter, 1)), 0.4f, "필터가 먼지와 찌꺼기로 막혀 있었다", 1.3f),
        new CauseSpec(OmenCause.MicroLeak, OmenKind.Pressure, "미세 누수", F((ItemKind.Sealant, 1)), 0.5f, "이음새에 물방울이 맺혀 있었다", 0.8f),
        new CauseSpec(OmenCause.StickyValve, OmenKind.Pressure, "구동부 걸림", F((ItemKind.Lubricant, 1)), 0.4f, "밸브 축이 뻑뻑하게 걸려 있었다", 0.8f),
        new CauseSpec(OmenCause.SensorOffset, OmenKind.Drift, "센서 영점 틀어짐", F(), 0.3f, "센서 영점이 틀어져 있었다 — 다시 맞췄다", 1.2f),
        new CauseSpec(OmenCause.BoardFault, OmenKind.Drift, "제어 기판 불량", F((ItemKind.Electronics, 1)), 0.6f, "제어 기판의 부품 하나가 부풀어 있었다", 0.6f),
        new CauseSpec(OmenCause.Connector, OmenKind.Drift, "커넥터 접촉 불량", F(), 0.25f, "커넥터가 헐거워 신호가 끊기고 있었다 — 닦아 끼웠다", 1f),
        new CauseSpec(OmenCause.Phantom, OmenKind.Drift, "계기 오류", F(), 0.2f, "설비는 멀쩡했다 — 감지기 교정이 틀어져 있었다", 0f),
    }.ToDictionary(c => c.Cause);

    public static CauseSpec Spec(OmenCause c) => Table[c];
    public static string Name(OmenCause c) => Table[c].Name;
    public static bool NeedsParts(OmenCause c) => Table[c].Fix.Length > 0;

    /// <summary>도는 설비 (베어링·회전날개가 있다).</summary>
    public static bool Rotating(FurnitureType t) => t is FurnitureType.CoolantPump or FurnitureType.WaterRecycler or FurnitureType.HeatExchanger or FurnitureType.Fridge
        or FurnitureType.AuxGenerator or FurnitureType.EngineCore or FurnitureType.Collector or FurnitureType.Fabricator or FurnitureType.Workbench;

    /// <summary>물·냉각수가 도는 설비.</summary>
    public static bool Fluid(FurnitureType t) => t is FurnitureType.CoolantPump or FurnitureType.WaterRecycler or FurnitureType.HeatExchanger or FurnitureType.OxygenGenerator
        or FurnitureType.GrowBed or FurnitureType.EngineCore or FurnitureType.ReactorCore;

    /// <summary>거르는 설비 (필터가 들어 있다).</summary>
    public static bool Filtered(FurnitureType t) => t is FurnitureType.OxygenGenerator or FurnitureType.WaterRecycler or FurnitureType.Scrubber or FurnitureType.Refinery
        or FurnitureType.MainComputer or FurnitureType.GrowBed;

    /// <summary>이 설비에서 이 기척을 낼 수 있는 원인들 (계기 오류는 빼고).</summary>
    public static List<OmenCause> For(FurnitureType t, OmenKind symptom)
    {
        var list = new List<OmenCause>();
        foreach (var s in Table.Values)
        {
            if (s.Symptom != symptom || s.Cause == OmenCause.Phantom) continue;
            bool ok = s.Cause switch
            {
                OmenCause.ImpellerDamage or OmenCause.BearingWear => Rotating(t),
                OmenCause.FilterClog => Filtered(t),
                OmenCause.MicroLeak => Fluid(t),
                _ => true,
            };
            if (ok) list.Add(s.Cause);
        }
        if (list.Count == 0) list.Add(symptom switch
        {
            OmenKind.Vibration => OmenCause.LooseMount,
            OmenKind.Heat => OmenCause.LooseTerminal,
            OmenKind.Pressure => OmenCause.StickyValve,
            _ => OmenCause.Connector,
        });
        return list;
    }

    /// <summary>원인 무게대로 하나 (결정적 주사위 0~1).</summary>
    public static OmenCause Weighted(IReadOnlyList<OmenCause> list, float roll)
    {
        float total = list.Sum(c => MathF.Max(0.05f, Table[c].Weight));
        float x = roll * total;
        foreach (var c in list)
        {
            x -= MathF.Max(0.05f, Table[c].Weight);
            if (x <= 0f) return c;
        }
        return list[^1];
    }
}

/// <summary>당직 일지 한 줄의 단계.</summary>
public enum NoteStage { Observed, Suspected, Confirmed, Resolved, Missed, Cleared }

/// <summary>
/// v12.0 당직 일지 한 줄: 관측한 사실 · 승무원의 판단 · 확인한 결과를 나눠 적는다.
/// 누가 알고 있는지(사람의 머릿속)와 컴퓨터 일지에 적혔는지가 따로 있다 — 인수인계가 끊기면 아는 사람이 잠든 동안 아무도 모른다.
/// </summary>
public sealed class ShiftNote
{
    public int Id { get; init; }
    public Machine Machine { get; init; } = null!;
    public Omen Omen { get; init; } = null!;
    public long Tick { get; init; }
    public string Author { get; init; } = "";
    public int AuthorId { get; init; } = -1;
    /// <summary>어떻게 알았나: 당직 · 순찰 · 감지기 · 로봇.</summary>
    public string How { get; init; } = "";

    // ── 관측한 사실 ──
    public string Observation { get; set; } = "";

    // ── 판단 ──
    public OmenCause? Suspect { get; set; }
    public float Confidence { get; set; }
    public string? SuspectBy { get; set; }

    // ── 확인한 결과 ──
    public OmenCause? Confirmed { get; set; }
    public string? ConfirmedBy { get; set; }
    public long ConfirmedAt { get; set; }
    public string? Finding { get; set; }

    /// <summary>갈아 봤는데 아니었던 원인 (잘못된 부품 교체).</summary>
    public List<OmenCause> RuledOut { get; } = new();

    /// <summary>컴퓨터 일지에 적혔다 (주 컴퓨터가 돌면 누구나 콘솔에서 읽는다). 판단까지 적었는지.</summary>
    public bool Logged { get; set; }
    public bool LoggedFull { get; set; }

    /// <summary>알고 있는 사람 → 판단까지 아는지 (관측만 전해 들었으면 false).</summary>
    public Dictionary<int, bool> Holders { get; } = new();

    public int Handovers { get; set; }
    public int Attempts { get; set; } // 분해 검사를 한 횟수
    public int Duplicates { get; set; }
    public int WrongFixes { get; set; }
    public bool Unhanded { get; set; }

    /// <summary>이 기록이 거쳐 간 길 (화면·연대기): "03:12 윤채원 당직 발견", "08:00 박도윤에게 인계" …</summary>
    public List<string> Trail { get; } = new();

    public NoteStage Stage { get; set; }
    public long ClosedAt { get; set; } = -1;
    public string? Outcome { get; set; }

    public bool Open => Stage is NoteStage.Observed or NoteStage.Suspected or NoteStage.Confirmed;
}

public sealed class WatchStats
{
    public int Notes;
    public int Written;      // 교대 때 컴퓨터 일지에 적음
    public int Verbal;       // 짧은 대화로 인계
    public int Lost;         // 인계 못 함 (아는 사람이 잠들었다)
    public int Duplicates;   // 이미 누가 본 것을 모르고 다시 봄
    public int Diagnoses;    // 분해 검사로 확인
    public int Inconclusive; // 열어 봤지만 못 찾음
    public int WrongFixes;   // 잘못된 부품 교체
    public int RightFirst;   // 판단대로 갈았는데 맞았다
    public int Calibrations;
    public int Phantoms;     // 계기 오류로 생긴 가짜 경보
    public int PhantomsCaught;
    public int Resolved;
    public int MissedUnhanded; // 전해지지 않아 끝내 고장 난 것

    public override string ToString() =>
        $"기록 {Notes} · 적음 {Written} · 말로 인계 {Verbal} · 인계 못 함 {Lost} · 중복 점검 {Duplicates} · 분해 검사 {Diagnoses}(못 찾음 {Inconclusive}) · " +
        $"판단대로 맞음 {RightFirst} · 잘못된 부품 교체 {WrongFixes} · 교정 {Calibrations} · 계기 오류 {Phantoms}(잡음 {PhantomsCaught}) · 손봄 {Resolved} · 전해지지 않아 고장 {MissedUnhanded}";
}

/// <summary>
/// v12.0 당직 일지와 진단.
/// - 관측 · 판단 · 확인을 나눈다: 당직자는 "진동이 커지고 압력이 조금 내려갔다"를 보고 "베어링 마모 같다"고 판단한다. 무엇이 닳았는지는 열어 봐야 안다.
/// - 꼼꼼한 사람은 분해 검사부터(부품을 챙기러 한 번 더 다녀온다), 급한 사람이나 시간이 없을 때는 판단대로 갈아 본다 — 틀리면 부품만 버린다.
/// - 교대: 근무가 끝날 때 컴퓨터 일지에 적거나(성실함·피로), 곁에 있는 다음 근무자에게 말로 넘긴다. 못 넘기면 그 사람이 깰 때까지 아무도 모른다.
///   모르는 사람이 같은 기척을 다시 찾으면 중복 점검이다.
/// - 감지기: 설비마다 마지막 측정 시각과 교정 상태. 주 컴퓨터가 멎거나 방에 전기가 없으면 값이 멈춰 오래된 값을 보여 준다.
///   교정이 틀어지면 기척을 덜 잡고, 설비는 멀쩡한데 경보를 낸다 (계기 오류). 전기 기사가 방을 돌며 다시 맞춘다.
/// - 친숙함: 같은 종류 설비를 여러 번 만진 사람은 평소와 다른 소리를 먼저 알아채고 판단도 잘 맞는다.
/// </summary>
public sealed class WatchLog
{
    private readonly World _w;
    private int _nextId = 1;
    private bool[] _onShift = Array.Empty<bool>();

    public List<ShiftNote> Notes { get; } = new();
    public WatchStats Stats { get; } = new();

    /// <summary>교대가 끝났는데 곁에 다음 근무자가 없어 찾아가 넘겨야 하는 기록 (사람 번호 → 교대 시각).</summary>
    public Dictionary<int, long> Pending { get; } = new();

    /// <summary>시험용: 교대 때 적지도 말하지도 않는 배.</summary>
    public bool NoHandover { get; set; }

    /// <summary>시험용: 아무도 분해 검사를 하지 않는 배 (모두 판단대로 갈아 본다).</summary>
    public bool NoDiagnosis { get; set; }

    /// <summary>시험용: 감지기가 전조를 잡지 않고 컴퓨터 일지도 없는 배 (사람의 눈과 말로만 전해진다).</summary>
    public bool NoSensors { get; set; }

    public WatchLog(World w) => _w = w;

    public IEnumerable<ShiftNote> OpenNotes => Notes.Where(n => n.Open);

    /// <summary>결정적 주사위 (난수를 뽑지 않는다 — 같은 사람은 같은 기록을 두고 같은 판단을 한다).</summary>
    public static float Roll(int a, int b, int c)
    {
        ulong z = (ulong)(uint)a * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)b * 0xC2B2AE3D27D4EB4FUL ^ (ulong)(uint)c * 0x165667B19E3779F9UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 40) / (float)(1UL << 24);
    }

    /// <summary>꼼꼼함 0~1: 성실함과 침착함에서 (사람마다 조금씩 다르게).</summary>
    public static float Thoroughness(CrewMember c) =>
        Math.Clamp(0.55f * c.Traits.Diligence + 0.35f * c.Traits.Calm + 0.3f * (Roll(c.Id, 77, 3) - 0.5f) + 0.05f, 0f, 1f);

    public bool Knows(ShiftNote n, CrewMember c) => n.Holders.ContainsKey(c.Id) || (n.Logged && _w.Automation.MainOnline);

    /// <summary>이 사람이 알고 있는 판단 (확인됐으면 확인 결과).</summary>
    public (OmenCause cause, float confidence, bool confirmed)? Belief(ShiftNote n, CrewMember c)
    {
        if (n.Confirmed is OmenCause done) return (done, 1f, true);
        if (n.Suspect is not OmenCause s) return null;
        bool heard = n.Holders.TryGetValue(c.Id, out var full) && full;
        bool read = n.Logged && n.LoggedFull && _w.Automation.MainOnline;
        return heard || read ? (s, n.Confidence, false) : null;
    }

    /// <summary>누군가 손을 댈 수 있는 기록인지: 컴퓨터 일지로 읽히거나, 아는 사람이 깨어 있다.</summary>
    public bool Actionable(ShiftNote n) =>
        n.Open && ((n.Logged && _w.Automation.MainOnline) || n.Holders.Keys.Any(id => _w.Crew[id] is { CanAct: true, IsAwake: true }));

    // ─────────────────────────────── 발견 ───────────────────────────────

    /// <summary>관측: 처음이면 기록을 만들고, 이미 누가 본 것을 모르고 다시 찾았으면 중복 점검이다.</summary>
    public void Observe(Machine m, Omen o, string how, CrewMember? by, string? byName)
    {
        var w = _w;
        bool sensor = how is "감지기" or "로봇" && !NoSensors;
        if (o.Note is ShiftNote n)
        {
            if (by != null && !Knows(n, by))
            {
                // 아는 사람이 같은 방에 깨어 있으면 그 자리에서 들은 것 (중복이 아니다)
                if (n.Holders.Keys.Any(id => id != by.Id && w.Crew[id] is { CanAct: true, IsAwake: true } h && h.Room == by.Room))
                {
                    n.Holders[by.Id] = false;
                    return;
                }
                n.Duplicates++;
                Stats.Duplicates++;
                n.Holders[by.Id] = true;
                n.Trail.Add($"{SimTime.Clock(w.Tick)} {by.Name} {how} — 다시 발견 (앞선 기록을 몰랐다)");
                if (n.Suspect == null) Judge(n, by, m, o);
                w.Log.Add(w.Tick, LogKind.Work, $"{m.Name}의 {Prevention.Name(o.Kind)} — 이미 {n.Author}이(가) 봤던 것을 모르고 다시 살폈다", by.Id);
            }
            if (sensor && !n.Logged) { n.Logged = true; n.Trail.Add($"{SimTime.Clock(w.Tick)} {byName ?? how} — 컴퓨터 일지에 올림"); }
            return;
        }
        n = new ShiftNote
        {
            Id = _nextId++, Machine = m, Omen = o, Tick = w.Tick, Author = by?.Name ?? byName ?? how, AuthorId = by?.Id ?? -1, How = how,
            Observation = Observation(m, o, how, sensor),
            Logged = sensor, LoggedFull = sensor,
            Stage = NoteStage.Observed,
        };
        o.Note = n;
        Notes.Add(n);
        Stats.Notes++;
        if (o.Cause == OmenCause.Phantom) Stats.Phantoms++;
        if (by != null) n.Holders[by.Id] = true;
        n.Trail.Add($"{SimTime.Clock(w.Tick)} {n.Author} {how} 발견: {n.Observation}");
        Judge(n, by, m, o);
        if (Notes.Count > 240) Notes.RemoveAll(x => !x.Open && x.ClosedAt >= 0 && w.Tick - x.ClosedAt > SimTime.TicksPerDay * 3);
    }

    /// <summary>관측한 사실: 기척의 종류와 설비 모양에 따라.</summary>
    private string Observation(Machine m, Omen o, string how, bool sensor)
    {
        var t = m.Body.Type;
        string fact = o.Kind switch
        {
            OmenKind.Vibration => Causes.Fluid(t) ? "진동이 커지고 압력이 조금 내려갔다" : t == FurnitureType.EngineCore ? "연소음이 고르지 않고 몸체가 떤다" : "평소와 다른 떨림과 소리가 난다",
            OmenKind.Heat => "단자함이 뜨겁고 탄내가 난다",
            OmenKind.Pressure => Causes.Fluid(t) ? "압력이 내려가고 흐름이 약해졌다" : "배출이 느려지고 부하가 오른다",
            _ => "계기 수치가 흔들리고 제어 신호가 가끔 끊긴다",
        };
        if (!sensor) return fact;
        float age = (_w.Tick - m.LastReading) / (float)SimTime.TicksPerHour;
        string what = o.Kind switch { OmenKind.Vibration => "진동", OmenKind.Heat => "온도", OmenKind.Pressure => "압력", _ => "신호" };
        return $"감지기 {what} 경보 (교정 {m.SensorCal * 100:0}%" + (age > 0.25f ? $" · 측정 {age:0.0}시간 전" : "") + ")";
    }

    /// <summary>판단: 본 사람의 솜씨와 그 설비에 대한 친숙함만큼 맞는다. 감지기는 가장 흔한 원인을 짚는다.</summary>
    private void Judge(ShiftNote n, CrewMember? by, Machine m, Omen o)
    {
        var (cause, conf) = Guess(n, by, m, o, 0);
        n.Suspect = cause;
        n.Confidence = conf;
        n.SuspectBy = by?.Name ?? "컴퓨터 추정";
        n.Stage = NoteStage.Suspected;
    }

    /// <summary>한 사람의 추측 (salt가 같으면 같은 답).</summary>
    public (OmenCause cause, float confidence) Guess(ShiftNote n, CrewMember? c, Machine m, Omen o, int salt)
    {
        var list = Causes.For(m.Body.Type, o.Kind);
        if (n.How is "감지기") list.Add(OmenCause.Phantom);
        list.RemoveAll(n.RuledOut.Contains);
        if (list.Count == 0) list.Add(o.Cause);
        if (c == null)
        {
            // 컴퓨터: 가장 흔한 원인
            var common = list.Where(x => x != OmenCause.Phantom).OrderByDescending(x => Causes.Spec(x).Weight).ThenBy(x => (int)x).FirstOrDefault(list[0]);
            return (common, 0.4f);
        }
        float skill = c.SkillLevel(m.Spec.Skill);
        float fam = c.FamiliarityWith(m.Body.Type);
        float p = Math.Clamp(0.28f + 0.42f * skill + 0.3f * fam, 0.2f, 0.93f);
        float conf = Math.Clamp(0.35f + 0.4f * skill + 0.2f * fam, 0.3f, 0.95f);
        if (list.Contains(o.Cause) && Roll(n.Id, c.Id, 11 + salt) < p) return (o.Cause, conf);
        var others = list.Where(x => x != o.Cause).ToList();
        if (others.Count == 0) return (o.Cause, conf);
        return (Causes.Weighted(others, Roll(n.Id, c.Id, 23 + salt)), conf);
    }

    // ─────────────────────────────── 확인·조치 ───────────────────────────────

    /// <summary>분해 검사: 솜씨·친숙함만큼 원인을 찾는다. 못 찾으면 판단만 흐려진다.</summary>
    public bool Diagnose(ShiftNote n, CrewMember c)
    {
        var w = _w;
        var m = n.Machine;
        float p = Math.Clamp(0.72f + 0.25f * c.SkillLevel(m.Spec.Skill) + 0.15f * c.FamiliarityWith(m.Body.Type), 0f, 0.98f);
        c.Familiarize(m.Body.Type, 0.05f);
        n.Attempts++;
        if (Roll(n.Id, c.Id, 31 + 7 * n.Attempts + 13 * n.WrongFixes) >= p)
        {
            Stats.Inconclusive++;
            n.Trail.Add($"{SimTime.Clock(w.Tick)} {c.Name} 분해 검사 — 원인을 못 찾았다");
            w.Log.Add(w.Tick, LogKind.Work, $"{Ko.EulReul(m.Name)} 열어 봤지만 원인을 못 찾았다", c.Id);
            n.Confidence *= 0.7f;
            return false;
        }
        var cause = n.Omen.Cause;
        n.Confirmed = cause;
        n.ConfirmedBy = c.Name;
        n.ConfirmedAt = w.Tick;
        n.Finding = Causes.Spec(cause).Finding;
        n.Stage = NoteStage.Confirmed;
        n.Logged |= w.Automation.MainOnline && !NoSensors;
        n.LoggedFull |= n.Logged;
        n.Holders[c.Id] = true;
        Stats.Diagnoses++;
        n.Trail.Add($"{SimTime.Clock(w.Tick)} {c.Name} 분해 검사 — {n.Finding}");
        string was = n.Suspect is OmenCause s && s != cause ? $" (판단은 {Causes.Name(s)}였다)" : "";
        w.Log.Add(w.Tick, LogKind.Work, $"{Ko.EulReul(m.Name)} 열어 보니 {Causes.Name(cause)} — {n.Finding}{was}", c.Id);
        MarkLog.Add(m.Marks, w.Tick, $"{c.Name}: 분해 검사 — {Causes.Name(cause)}");
        return true;
    }

    /// <summary>판단대로 손봤다: 맞으면 전조가 사라지고, 틀리면 부품만 버리고 그 원인을 지운다.</summary>
    public bool ApplyFix(ShiftNote n, CrewMember c, OmenCause plan)
    {
        var w = _w;
        var m = n.Machine;
        c.Familiarize(m.Body.Type, 0.04f);
        if (plan == n.Omen.Cause)
        {
            if (n.Confirmed == null) Stats.RightFirst++;
            if (plan == OmenCause.Phantom) { ClearPhantom(n, c); return false; }
            Resolve(n, c, $"{Causes.Name(plan)} 손봄");
            return true;
        }
        n.RuledOut.Add(plan);
        n.WrongFixes++;
        Stats.WrongFixes++;
        n.Suspect = null;
        n.Stage = NoteStage.Observed;
        string parts = Causes.NeedsParts(plan) ? string.Join(" + ", Causes.Spec(plan).Fix.Select(x => ItemKinds.Name(x.kind))) + "을(를) 갈았지만" : "손봤지만";
        n.Trail.Add($"{SimTime.Clock(w.Tick)} {c.Name} {Causes.Name(plan)}(으)로 보고 {parts} 그대로 — 아니었다");
        w.Log.Add(w.Tick, LogKind.Warning, $"{m.Name}: {Causes.Name(plan)}(으)로 보고 {parts} {Prevention.Name(n.Omen.Kind)}은(는) 그대로다", c.Id);
        MarkLog.Add(m.Marks, w.Tick, $"{c.Name}: 잘못 짚음 — {Causes.Name(plan)}");
        if (Causes.NeedsParts(plan))
            w.History.Add(w, HistoryKind.Maintenance, $"{Ko.IGa(c.Name)} {m.Name}의 {Prevention.Name(n.Omen.Kind)}을(를) {Causes.Name(plan)}(으)로 보고 부품을 갈았지만 그대로였다 — 열어 봐야 한다",
                m.Body.Room, new[] { c });
        return false;
    }

    /// <summary>계기 오류였다: 그 설비의 감지기를 다시 맞추고 경보를 걷는다.</summary>
    public void ClearPhantom(ShiftNote n, CrewMember c)
    {
        var m = n.Machine;
        m.SensorCal = MathF.Max(m.SensorCal, 0.9f + 0.08f * c.SkillLevel(Skill.Electrical));
        m.LastCalibrated = _w.Tick;
        if (m.Omen == n.Omen) m.Omen = null;
        n.Confirmed ??= OmenCause.Phantom;
        n.Finding ??= Causes.Spec(OmenCause.Phantom).Finding;
        Resolve(n, c, "계기 오류 — 감지기를 다시 맞췄다");
    }

    /// <summary>전조가 사라졌다 (손봤다).</summary>
    public void Resolve(ShiftNote n, CrewMember? c, string outcome)
    {
        var w = _w;
        if (!n.Open) return;
        n.Stage = NoteStage.Resolved;
        n.ClosedAt = w.Tick;
        n.Outcome = outcome;
        Stats.Resolved++;
        if (n.Omen.Cause == OmenCause.Phantom) Stats.PhantomsCaught++;
        n.Trail.Add($"{SimTime.Clock(w.Tick)} {c?.Name ?? "?"} — {outcome}");
        // 이야기: 교대를 건너 이어진 기록, 여럿의 손을 거친 기록은 연대기에
        if (c != null && (n.Handovers > 0 || n.WrongFixes > 0 || n.AuthorId != c.Id && n.AuthorId >= 0 && n.Confirmed != null))
        {
            var crew = new List<CrewMember> { c };
            if (n.AuthorId >= 0 && n.AuthorId != c.Id) crew.Add(w.Crew[n.AuthorId]);
            string passed = n.Handovers > 0 ? "교대를 넘겨 받은 기록을 보고 " : "";
            string found = n.Finding != null ? $" — {n.Finding}" : "";
            string wrong = n.WrongFixes > 0 ? $" (한 번 잘못 짚은 뒤)" : "";
            w.History.Add(w, HistoryKind.Maintenance,
                $"{n.Author}이(가) {SimTime.Clock(n.Tick)}에 적은 \"{n.Machine.Name}: {n.Observation}\" — {c.Name}이(가) {passed}{Causes.Name(n.Omen.Cause)}을(를) 손봤다{wrong}{found}",
                n.Machine.Body.Room, crew);
        }
    }

    /// <summary>전조가 때가 되어 고장이 됐거나(놓침), 계기 오류가 저절로 걷혔다.</summary>
    public void Close(Omen o, bool broke)
    {
        var w = _w;
        if (o.Note is not ShiftNote n || !n.Open) return;
        n.Stage = broke ? NoteStage.Missed : NoteStage.Cleared;
        n.ClosedAt = w.Tick;
        n.Outcome = broke ? $"손보기 전에 {Faults.Spec(o.Fault).Name}" : "경보가 저절로 걷혔다 (계기 오류)";
        n.Trail.Add($"{SimTime.Clock(w.Tick)} — {n.Outcome}");
        if (broke && n.Unhanded && !n.Logged)
        {
            Stats.MissedUnhanded++;
            var crew = n.AuthorId >= 0 ? new[] { w.Crew[n.AuthorId] } : null;
            w.History.Add(w, HistoryKind.Maintenance,
                $"{n.Author}만 알던 {n.Machine.Name}의 {Prevention.Name(o.Kind)} — 교대 때 전해지지 않아 끝내 {Faults.Spec(o.Fault).Name}(으)로", n.Machine.Body.Room, crew);
        }
    }

    // ─────────────────────────────── 교대·감지기 ───────────────────────────────

    public void Update(float dt)
    {
        var w = _w;
        // 교대: 근무가 끝나는(또는 쓰러진) 사람이 들고 있는 기록을 넘긴다
        if (_onShift.Length != w.Crew.Count) _onShift = w.Crew.Select(c => OnShift(c)).ToArray();
        for (int i = 0; i < w.Crew.Count; i++)
        {
            var c = w.Crew[i];
            bool now = OnShift(c);
            if (_onShift[i] && !now) Handover(c);
            _onShift[i] = now;
        }
        // 찾아가 넘기지 못하고 두 시간이 지났거나 잠들었으면 혼자만 안다
        foreach (var (id, since) in Pending.ToList())
        {
            var c = w.Crew[id];
            var left = ToHandOver(c);
            if (left.Count == 0) { Pending.Remove(id); continue; }
            if (w.Tick - since > SimTime.Hours(2) || !c.CanAct || !c.IsAwake)
            {
                foreach (var n in left) LoseNote(n, c);
                Pending.Remove(id);
            }
        }

        // 감지기: 측정 시각과 교정
        bool online = w.Automation.MainOnline;
        float stormDrift = w.Hazards.StormActive ? 4f : 1f;
        foreach (var m in w.Ship.Machines)
        {
            var room = m.Body.Room;
            if (online && room.Powered && !room.Detached && !m.Body.Stowed) m.LastReading = w.Tick;
            // 하루 1%쯤 (태양 폭풍 동안 네 배), 뜨거운 방에서 조금 더
            float heat = room.Air.Temperature > 32f ? 1.5f : 1f;
            m.SensorCal = MathF.Max(0.3f, m.SensorCal - dt / 24f * 0.011f * stormDrift * heat);
            // 작은 이상: 마모와 상관없이 가끔 (풀린 볼트·단자·커넥터·막힘) — 놓치면 고장이 된다
            if (m.Omen == null && m.Faults.Count == 0 && !m.Body.Stowed && !room.Abandoned && !room.OffLimits && !room.Detached && m.Spec.PowerDraw > 0f
                && w.Rng.Chance(Tuning.AnomalyPerDay / 24f * dt))
            {
                var faults = m.Spec.FaultKinds.Where(k => k != FaultKind.BreakerTrip && Prevention.KindOf(k) != null).ToList();
                if (faults.Count > 0)
                {
                    var fault = faults[w.Rng.Range(0, faults.Count)];
                    var kind = Prevention.KindOf(fault)!.Value;
                    m.Omen = new Omen
                    {
                        Kind = kind, Fault = fault, Cause = Causes.Weighted(Causes.For(m.Body.Type, kind), w.Rng.Float()),
                        Since = w.Tick, Due = w.Tick + SimTime.Hours(w.Rng.Range(10f, 34f)),
                    };
                    w.Precursors.Omens++;
                }
            }
            // 교정이 많이 틀어진 감지기는 멀쩡한 설비에서 경보를 낸다 (계기 오류)
            if (m.Omen == null && m.Faults.Count == 0 && online && room.Powered && m.SensorCal < 0.8f && !room.Abandoned && !room.OffLimits)
            {
                float miss = (0.8f - m.SensorCal) / 0.2f;
                if (w.Rng.Chance(0.0035f * miss * miss * dt))
                {
                    var kinds = new[] { OmenKind.Heat, OmenKind.Pressure, OmenKind.Drift };
                    var kind = kinds[w.Rng.Range(0, kinds.Length)];
                    m.Omen = new Omen { Kind = kind, Fault = FaultKind.SensorDrift, Cause = OmenCause.Phantom, Since = w.Tick, Due = w.Tick + SimTime.Hours(w.Rng.Range(18f, 36f)) };
                    w.Precursors.Omens++;
                }
            }
        }
    }

    public static bool OnShift(CrewMember c, World w) => c.CanAct && ChoresActivity.OnShiftStatic(c, w);
    private bool OnShift(CrewMember c) => OnShift(c, _w);

    /// <summary>근무가 끝난다: 컴퓨터 일지에 적고(성실함·피로), 곁의 다음 근무자에게 말로 넘긴다.</summary>
    private void Handover(CrewMember c)
    {
        var w = _w;
        var mine = Notes.Where(n => n.Open && n.Holders.ContainsKey(c.Id)).ToList();
        if (mine.Count == 0) return;
        foreach (var n in mine)
        {
            bool someoneElse = n.Holders.Keys.Any(id => id != c.Id && w.Crew[id] is { CanAct: true, IsAwake: true });
            if (NoHandover)
            {
                if (!someoneElse && !(n.Logged && w.Automation.MainOnline)) { n.Unhanded = true; Stats.Lost++; }
                continue;
            }
            bool full = n.Holders[c.Id];
            // 1) 컴퓨터 일지에 적는다: 성실할수록, 덜 지쳤을수록
            if (!n.Logged && w.Automation.MainOnline && c.CanAct && !NoSensors)
            {
                float p = 0.3f + 0.6f * c.Traits.Diligence - 0.3f * (1f - c.Needs.Rest) - 0.2f * c.Needs.Stress;
                if (Roll(n.Id, c.Id, 41 + (int)(w.Tick / SimTime.TicksPerHour)) < p)
                {
                    n.Logged = true;
                    n.LoggedFull = full && Roll(n.Id, c.Id, 43) < 0.45f + 0.5f * c.Traits.Diligence;
                    Stats.Written++;
                    n.Trail.Add($"{SimTime.Clock(w.Tick)} {c.Name} 교대 일지에 적음" + (n.LoggedFull ? "" : " (관측만)"));
                }
            }
            // 2) 곁의 다음 근무자에게 말로: 지금 근무 중이거나 한 시간 안에 시작하는, 깨어 있는 사람 (같은 방·옆방 또는 가까이)
            if (c.CanAct && c.IsAwake)
            {
                var relief = w.Crew
                    .Where(x => x != c && x.CanAct && x.IsAwake && !x.Outside && !n.Holders.ContainsKey(x.Id)
                                && (OnShift(x) || SimTime.InWindow(SimTime.HourOfDay(w.Tick) + 1f, x.Schedule.WorkStart, 1f))
                                && ((x.Position - c.Position).LengthSquared() < 14f * 14f || x.Room == c.Room))
                    .OrderByDescending(x => x.RawSkill(n.Machine.Spec.Skill) >= 0.4f ? 1 : 0)
                    .ThenBy(x => (x.Position - c.Position).LengthSquared())
                    .FirstOrDefault();
                if (relief != null)
                {
                    bool keepJudgment = full && Roll(n.Id, relief.Id, 47) < 0.55f + 0.4f * c.Traits.Calm - 0.2f * (1f - c.Needs.Rest);
                    n.Holders[relief.Id] = keepJudgment;
                    n.Handovers++;
                    Stats.Verbal++;
                    n.Trail.Add($"{SimTime.Clock(w.Tick)} {c.Name} → {relief.Name} 말로 인계" + (keepJudgment ? "" : " (관측만)"));
                    string what = keepJudgment && n.Suspect is OmenCause s ? $"{Causes.Name(s)} 같아" : "한번 봐 줘";
                    c.Say(w, $"인수인계: {n.Machine.Name} {Prevention.Name(n.Omen.Kind)} — {what}");
                    relief.Say(w, "알았어, 확인할게");
                    w.Log.Add(w.Tick, LogKind.Life, $"{relief.Name}에게 인수인계: {n.Machine.Name} — {n.Observation}", c.Id);
                    continue;
                }
            }
            if (!someoneElse && !(n.Logged && w.Automation.MainOnline))
            {
                // 곁에 없으면 찾아가서 넘긴다 (두 시간 안에 — 그 전에 잠들면 혼자만 안다)
                if (c.CanAct && c.IsAwake) { Pending[c.Id] = w.Tick; continue; }
                LoseNote(n, c);
            }
        }
    }

    private void LoseNote(ShiftNote n, CrewMember c)
    {
        if (n.Unhanded) return;
        n.Unhanded = true;
        Stats.Lost++;
        n.Trail.Add($"{SimTime.Clock(_w.Tick)} {c.Name} 교대 — 넘길 사람을 못 만났다 (혼자만 안다)");
    }

    /// <summary>찾아가서 넘길 기록 (아직 아무도 모르는 것).</summary>
    public List<ShiftNote> ToHandOver(CrewMember c) =>
        Notes.Where(n => n.Open && n.Holders.ContainsKey(c.Id) && !(n.Logged && _w.Automation.MainOnline)
                         && !n.Holders.Keys.Any(id => id != c.Id && _w.Crew[id] is { CanAct: true, IsAwake: true })).ToList();

    /// <summary>넘겨받을 사람: 지금 근무 중이거나 곧 시작하는, 깨어 있는 사람 (그 설비를 만질 줄 아는 사람 먼저).</summary>
    public CrewMember? Relief(CrewMember c, ShiftNote n) =>
        _w.Crew.Where(x => x != c && x.CanAct && x.IsAwake && !x.Outside && !n.Holders.ContainsKey(x.Id)
                           && (OnShift(x) || SimTime.InWindow(SimTime.HourOfDay(_w.Tick) + 1f, x.Schedule.WorkStart, 1f)))
            .OrderByDescending(x => x.RawSkill(n.Machine.Spec.Skill))
            .ThenBy(x => (x.Position - c.Position).LengthSquared())
            .FirstOrDefault();

    /// <summary>찾아가서 말로 넘겼다.</summary>
    public void HandOver(CrewMember c, CrewMember relief)
    {
        var w = _w;
        foreach (var n in ToHandOver(c))
        {
            bool full = n.Holders[c.Id];
            bool keepJudgment = full && Roll(n.Id, relief.Id, 49) < 0.6f + 0.35f * c.Traits.Calm - 0.2f * (1f - c.Needs.Rest);
            n.Holders[relief.Id] = keepJudgment;
            n.Handovers++;
            Stats.Verbal++;
            n.Trail.Add($"{SimTime.Clock(w.Tick)} {c.Name} → {relief.Name} 찾아가 말로 인계" + (keepJudgment ? "" : " (관측만)"));
            w.Log.Add(w.Tick, LogKind.Life, $"{relief.Name}을(를) 찾아가 인수인계: {n.Machine.Name} — {n.Observation}", c.Id);
        }
        c.Say(w, "인수인계할 게 있어");
        relief.Say(w, "응, 내가 볼게");
        Pending.Remove(c.Id);
    }

    /// <summary>방을 돌며 감지기를 다시 맞췄다: 계기 오류가 걷힌다.</summary>
    public void Calibrate(Room room, CrewMember c)
    {
        var w = _w;
        float skill = c.SkillLevel(Skill.Electrical);
        foreach (var f in room.Furniture)
        {
            if (f.Machine is not Machine m) continue;
            m.SensorCal = MathF.Max(m.SensorCal, 0.9f + 0.08f * skill);
            m.LastCalibrated = w.Tick;
            if (m.Omen is { Cause: OmenCause.Phantom } o)
            {
                if (o.Note is ShiftNote n)
                {
                    n.ConfirmedBy ??= c.Name;
                    n.ConfirmedAt = w.Tick;
                    ClearPhantom(n, c);
                }
                m.Omen = null;
            }
        }
        Stats.Calibrations++;
    }
}

public sealed partial class WorkBoard
{
    /// <summary>v12.0 교대가 끝난 사람이 다음 근무자를 찾아가 기록을 넘긴다.</summary>
    private void ScanHandover(Poster post)
    {
        var w = _world;
        foreach (var id in w.Watch.Pending.Keys.ToList())
        {
            var c = w.Crew[id];
            var notes = w.Watch.ToHandOver(c);
            if (notes.Count == 0 || !c.CanAct) continue;
            if (w.Watch.Relief(c, notes[0]) is not CrewMember relief) continue;
            post(WorkKind.Handover, WorkTarget.OfCrew(relief), 0.55f, notes[0].Machine.Spec.Skill,
                $"{string.Join(", ", notes.Select(n => $"{n.Machine.Name} {Prevention.Name(n.Omen.Kind)}"))} — 교대 전에 말해 두기", circuit: c.Id);
        }
    }

    /// <summary>v12.0 감지기 교정: 교정이 틀어진 방(평균 72% 아래, 핵심 설비는 70% 아래)을 전기 기사가 돈다.</summary>
    private void ScanCalibration(Poster post)
    {
        var w = _world;
        foreach (var room in w.Ship.LiveRooms)
        {
            if (room.Abandoned || room.OffLimits || room.Leaking) continue;
            var ms = room.Furniture.Where(f => f.Machine != null).Select(f => f.Machine!).ToList();
            if (ms.Count == 0) continue;
            float avg = ms.Average(m => m.SensorCal);
            float worstVital = ms.Where(m => m.Spec.Critical).Select(m => m.SensorCal).DefaultIfEmpty(1f).Min();
            bool phantom = ms.Any(m => m.Omen is { Cause: OmenCause.Phantom, Note: { Stage: NoteStage.Confirmed } });
            if (avg >= 0.72f && worstVital >= 0.7f && !phantom) continue;
            post(WorkKind.Calibrate, WorkTarget.OfRoom(room), MathF.Min(0.5f, 0.2f + (0.8f - avg) * 0.8f + (phantom ? 0.2f : 0f)), Skill.Electrical,
                $"감지기 교정 평균 {avg * 100:0}%" + (worstVital < 0.7f ? $" · 핵심 설비 {worstVital * 100:0}%" : "") + (phantom ? " · 계기 오류 확인됨" : ""));
        }
    }
}

public static partial class WorkPlanners
{
    /// <summary>인수인계: 다음 근무자 곁으로 가서 짧게 말한다.</summary>
    private static Job? Handover(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var relief = o.Target.Crew!;
        if (!relief.CanAct || !relief.IsAwake) { blocked = $"{relief.Name}이(가) 자고 있다"; return null; }
        var near = Cell.Dirs8.Select(d => relief.Cell + d).Where(x => w.Ship.IsWalkable(x) && dist.Reachable(x)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
        if (near is not Cell spot) { blocked = "곁에 갈 수 없다"; return null; }
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(spot));
        toils.Add(new WaitToil(SimTime.Minutes(4), Pose.Standing, relief.Position));
        toils.Add(new DoToil((cm, world) =>
        {
            world.Board.Close(o);
            if ((relief.Position - cm.Position).LengthSquared() > 16f || !relief.IsAwake) return false;
            world.Watch.HandOver(cm, relief);
            cm.ChangeAffinity(relief, 0.01f);
            return true;
        }));
        return Wrap(a, o, c, w, "인수인계", toils, $"{relief.Name}을(를) 찾아가 인수인계");
    }

    /// <summary>감지기 교정: 방의 설비마다 영점을 다시 맞춘다.</summary>
    private static Job? Calibrate(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var room = o.Target.Room!;
        int n = room.Furniture.Count(f => f.Machine != null);
        var toils = Plans.DropOff(c, w, dist);
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.15f + 0.06f * n, Skill.Electrical, room.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) =>
        {
            world.Watch.Calibrate(room, cm);
            cm.Practice(Skill.Electrical, 0.01f);
            world.Board.Close(o);
            world.Log.Add(world.Tick, LogKind.Work, $"{room.Name} 감지기 {n}개를 다시 맞췄다", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "감지기 교정", toils, $"{room.Name} 감지기 교정");
    }
}
