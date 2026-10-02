using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.14 ④ 연구가 행동이다.
// 쌓인 연구(작업대 · 솜씨 좋은 사람)는 그대로 흐르고, 그 위에 실험이 있다: 회의가 고른 연구에 몇 시간마다 실험 차례가 열리면
// 그 분야에 솜씨 · 관심이 있는 사람이 실험실 · 작업대에서 실험한다 (성격 — 신중 · 무난 · 대담, 둘이면 협업).
// 결과: 성공(연구가 밀린다) · 실패(조금 배운다) · 작은 사고(폭발은 Blast · 불은 Fire — 분야마다 다르다) · 돌파구.
// 끊기면(경보 · 끼니 · 잠) 작업대에 연구 노트(물건)가 남고 — 누구든 노트를 펴고 이어 한다. 노트는 불에 탄다.
// 원정 유물 역설계는 실험으로만 된다(먼저 유물을 살펴본다).
// 주 컴퓨터: 다음 연구를 근거와 함께 추천하고, 위험한 실험에 경고한다 (믿는 사람은 천천히 한다) · 사고를 읽고 다음 실험을 조언한다.

public enum ResearchStyle : byte { Steady, Cautious, Bold }

public sealed class ExperimentState
{
    public string Tech { get; init; } = "";
    public int Lead { get; set; } = -1;
    public int Partner { get; set; } = -1;
    public int Bench { get; set; } = -1;
    public float Progress { get; set; }
    public float Hours { get; init; } = 1.2f;
    public int Sessions { get; set; }
    public long Opened { get; init; }
    public long LastWork { get; set; } = -1;
    public bool Paused { get; set; }
    public bool Relic { get; init; }
    public bool RelicSeen { get; set; }
    public string LeadWhy { get; set; } = "";
    public ResearchStyle Style { get; set; }
    public bool Warned { get; set; }
    public bool Heeded { get; set; }
    public bool ReadNotes { get; set; }
    /// <summary>시험용: "accident" · "breakthrough" · "fail" · "success".</summary>
    public string? Force { get; set; }
    public List<int> Hands { get; } = new();
    /// <summary>지금 손대는 사람 (이번 틱에 진척을 보탠 사람 — 둘이면 협업).</summary>
    public long LeadTick { get; set; } = -1;
    public long PartnerTick { get; set; } = -1;
}

/// <summary>연구 노트 한 권 (작업대 위에 놓인다 — 이어 할 사람이 펴 본다 · 불에 탄다).</summary>
public sealed class ResearchNote
{
    public int Id { get; init; }
    public string Tech { get; init; } = "";
    public TechField Field { get; init; }
    public int Author { get; init; } = -1;
    public Cell At { get; init; }
    public int Room { get; init; } = -1;
    public float Progress { get; set; }
    public long Tick { get; init; }
    public string Text { get; init; } = "";
    /// <summary>0 끊김(이어 할 것) · 1 성공 · 2 실패 · 3 사고 · 4 돌파구.</summary>
    public int Kind { get; init; }
    public int Reads { get; set; }
}

/// <summary>실험실에 남은 자국 (화면: 그을음 · 깨진 유리 · 반짝임).</summary>
public sealed record ResearchMark(long Tick, Cell At, int Kind, TechField Field, int Room, string Text);

public sealed partial class TechWebSystem
{
    public ExperimentState? Trial { get; private set; }
    public List<ResearchNote> Notes { get; } = new();
    public List<ResearchMark> Marks { get; } = new();
    /// <summary>주 컴퓨터가 권한 다음 연구 · 근거.</summary>
    public string? RecTech { get; private set; }
    public string RecWhy { get; private set; } = "";
    public long RecTick { get; private set; } = -1;
    /// <summary>사람마다 한 실험 수 (화면 · 칭찬).</summary>
    public Dictionary<int, int> Experiments { get; } = new();
    private readonly Dictionary<string, List<int>> _hands = new();
    private readonly Dictionary<int, long> _shaken = new();
    private long _nextTrial = SimTime.Hours(3);
    private long _nextBurn;
    private int _noteId = 1;

    // ─────────────────────────────── 사람 ───────────────────────────────

    public static Skill SkillOf(TechField f) => f switch
    {
        TechField.Power or TechField.Computing or TechField.Sensors or TechField.Defense => Skill.Electrical,
        TechField.Cooling or TechField.Propulsion or TechField.Hull => Skill.Engineering,
        TechField.Robotics or TechField.Fabrication or TechField.Habitat => Skill.Mechanics,
        TechField.Life or TechField.Food => Skill.Botany,
        _ => Skill.Medicine,
    };

    /// <summary>이 사람이 이 분야에 끌리는 정도 (역할 · 내력 · 버릇) 0~0.5.</summary>
    public static float Interest(CrewMember c, TechField f)
    {
        float s = 0f;
        s += c.Role switch
        {
            CrewRole.Engineer when f is TechField.Power or TechField.Propulsion or TechField.Cooling or TechField.Hull => 0.25f,
            CrewRole.Electrician when f is TechField.Power or TechField.Computing or TechField.Sensors or TechField.Defense => 0.25f,
            CrewRole.Technician when f is TechField.Fabrication or TechField.Robotics or TechField.Hull => 0.25f,
            CrewRole.Botanist when f is TechField.Food or TechField.Life => 0.25f,
            CrewRole.Medic when f is TechField.Medical or TechField.Life => 0.25f,
            CrewRole.Pilot when f is TechField.Propulsion or TechField.Sensors or TechField.Defense => 0.2f,
            CrewRole.Cook when f is TechField.Food or TechField.Habitat => 0.2f,
            _ => 0f,
        };
        s += c.Background switch
        {
            Background.Chemist when f is TechField.Life or TechField.Medical or TechField.Food => 0.2f,
            Background.Physicist when f is TechField.Power or TechField.Propulsion or TechField.Sensors => 0.2f,
            Background.Programmer or Background.SysAdmin when f == TechField.Computing => 0.2f,
            Background.Roboticist or Background.DroneRacer when f == TechField.Robotics => 0.2f,
            Background.Welder or Background.Miner when f is TechField.Hull or TechField.Fabrication => 0.15f,
            Background.Gardener or Background.FarmResearcher or Background.Chef or Background.Baker when f == TechField.Food => 0.2f,
            Background.Astronomer when f == TechField.Sensors => 0.2f,
            Background.Firefighter or Background.SafetyInspector when f is TechField.Defense or TechField.Habitat => 0.15f,
            Background.Nurse or Background.MedStudent or Background.Paramedic or Background.Veterinarian when f == TechField.Medical => 0.2f,
            Background.Carpenter when f is TechField.Habitat or TechField.Fabrication => 0.15f,
            _ => 0f,
        };
        if (c.Habits.Contains(Habit.Bookworm)) s += 0.08f;
        if (c.Habits.Contains(Habit.Tinkerer) && f is TechField.Fabrication or TechField.Robotics) s += 0.12f;
        return MathF.Min(0.5f, s);
    }

    /// <summary>실험하는 버릇: 대담(빠르고 사고 · 돌파구가 잦다) · 신중(느리고 안전) — 사고에 놀란 사람은 사흘 신중.</summary>
    public ResearchStyle StyleOf(CrewMember c)
    {
        if (_shaken.TryGetValue(c.Id, out var until) && _w.Tick < until) return ResearchStyle.Cautious;
        int bold = 0, careful = 0;
        if (c.Traits.Bravery >= 0.65f) bold++;
        if (c.Traits.Bravery <= 0.35f) careful++;
        if (c.Habits.Contains(Habit.Daredevil)) bold += 2;
        if (c.Habits.Contains(Habit.Hasty)) bold++;
        if (c.Habits.Contains(Habit.Methodical)) careful++;
        if (c.Habits.Contains(Habit.Perfectionist)) careful++;
        if (c.Habits.Contains(Habit.Patient)) careful++;
        if (c.Habits.Contains(Habit.Worrier)) careful++;
        return bold > careful ? ResearchStyle.Bold : careful > bold ? ResearchStyle.Cautious : ResearchStyle.Steady;
    }

    public static string StyleName(ResearchStyle s) => s switch { ResearchStyle.Bold => "대담", ResearchStyle.Cautious => "신중", _ => "무난" };

    private CrewMember? Crew(int id) => id >= 0 && id < _w.Crew.Count ? _w.Crew[id] : null;

    /// <summary>이 실험에 맞는 사람 (솜씨 · 관심 · 성실 − 실험실이 무서움 − 지침).</summary>
    private float Fit(CrewMember c, EraTech t, Room? lab)
    {
        if (c.Dead || c.Away || c.IsChild || !c.CanAct) return -9f;
        float fear = c.Memory.FearOf(lab);
        if (fear > 0.4f) return -5f; // 그 방이 무섭다 — 피한다
        return 0.6f * c.RawSkill(SkillOf(t.Field)) + Interest(c, t.Field) + 0.15f * c.Traits.Diligence - 0.5f * fear - 0.2f * c.Needs.Stress;
    }

    // ─────────────────────────────── 자리 ───────────────────────────────

    private static readonly FurnitureType[] BenchTypes =
        { FurnitureType.Workbench, FurnitureType.PartTestBench, FurnitureType.SolderStation, FurnitureType.Lathe, FurnitureType.CalibrationRig, FurnitureType.DiagnosticScanner };

    private static bool LabRoom(Room r) => r.Type is RoomType.Lab or RoomType.ElectronicsLab or RoomType.AlgaeLab or RoomType.Workshop;

    private static bool Usable(Furniture f) => !f.Stowed && !f.Room.Abandoned && !f.Room.OffLimits && f.Machine is not { Stopped: true } && f.UseSpots.Count > 0;

    /// <summary>실험할 자리: 실험실 → 정비실 순, 작업대가 먼저.</summary>
    public Furniture? PickBench()
    {
        Furniture? best = null;
        int bestRank = int.MaxValue;
        foreach (var f in _w.Ship.Furniture)
        {
            int ti = Array.IndexOf(BenchTypes, f.Type);
            if (ti < 0 || !Usable(f)) continue;
            int rank = (f.Room.Type is RoomType.Lab or RoomType.ElectronicsLab or RoomType.AlgaeLab ? 0 : LabRoom(f.Room) ? 100 : 200) + ti * 10 + (f.Room.Dark ? 5 : 0);
            if (rank < bestRank) { bestRank = rank; best = f; }
        }
        return best;
    }

    public Furniture? BenchOf(ExperimentState x)
    {
        var f = x.Bench >= 0 && x.Bench < _w.Ship.Furniture.Count ? _w.Ship.Furniture[x.Bench] : null;
        if (f != null && Usable(f)) return f;
        f = PickBench();
        x.Bench = f?.Id ?? -1;
        return f;
    }

    // ─────────────────────────────── 매 틱 · 매 시간 ───────────────────────────────

    private void ResearchTick(float dt)
    {
        var w = _w;
        if (Trial is ExperimentState x && !x.Paused && x.LastWork >= 0 && w.Tick - x.LastWork > SimTime.Minutes(20) && x.Progress > 0.02f)
        {
            // 끊겼다: 경보 · 끼니 · 잠 — 노트를 남긴다
            x.Paused = true;
            Stats.Interrupts++;
            var t = TechWeb.Find(x.Tech);
            var who = Crew(x.Lead);
            if (t != null && BenchOf(x) is Furniture b)
            {
                AddNote(x, t, who, b, 0, $"{t.Name} 실험 {x.Progress * 100f:0}%에서 멈춤 — 이어서 할 것");
                w.Log.Add(w.Tick, LogKind.Work, $"{t.Name} 실험이 {x.Progress * 100f:0}%에서 끊겼다 — 작업대에 연구 노트를 두고 갔다", who?.Id ?? -1);
                Note($"실험이 끊겼다 — {t.Name} {x.Progress * 100f:0}% (노트를 남겼다)");
            }
        }
        if (w.Tick >= _nextBurn && Notes.Count > 0)
        {
            _nextBurn = w.Tick + SimTime.Minutes(10);
            for (int i = Notes.Count - 1; i >= 0; i--)
            {
                var n = Notes[i];
                if (w.Fire.At(n.At) <= 0.05f) continue;
                Notes.RemoveAt(i);
                Stats.NotesBurned++;
                Note($"연구 노트가 탔다 — {TechWeb.Find(n.Tech)?.Name ?? n.Tech}", 2);
                w.Log.Add(w.Tick, LogKind.Warning, $"불이 작업대의 연구 노트를 태웠다 ({TechWeb.Find(n.Tech)?.Name ?? n.Tech})");
                if (n.Kind == 0 && Trial is ExperimentState y && y.Paused && y.Tech == n.Tech) y.Progress *= 0.5f; // 적어 둔 게 타서 절반을 다시
            }
        }
    }

    private void ResearchHour()
    {
        var w = _w;
        var e = w.Eras;
        if (Trial is ExperimentState x)
        {
            if (e.Project != x.Tech || e.Known.Contains(x.Tech)) { Trial = null; return; } // 회의가 연구를 바꿨거나 이미 익혔다
            ReassignIfNeeded(x);
            return;
        }
        if (e.Project is not string pid || w.Tick < _nextTrial || TechWeb.Find(pid) is not EraTech t) return;
        var bench = PickBench();
        if (bench == null) return;
        var nx = new ExperimentState
        {
            Tech = t.Id, Opened = w.Tick, Bench = bench.Id, Hours = Math.Clamp(0.8f + 0.004f * t.Cost, 0.9f, 2f), Relic = TechWeb.Node(t.Id).Trial,
        };
        Trial = nx;
        if (!ReassignIfNeeded(nx)) { Trial = null; _nextTrial = w.Tick + SimTime.Hours(2); return; }
        Warn(nx, t, bench);
    }

    /// <summary>실험할 사람을 (다시) 정한다. 없으면 false.</summary>
    private bool ReassignIfNeeded(ExperimentState x)
    {
        var w = _w;
        var t = TechWeb.Find(x.Tech)!;
        var lab = BenchOf(x)?.Room;
        var lead = Crew(x.Lead);
        if (lead != null && Fit(lead, t, lab) > -1f && (x.Partner < 0 || Crew(x.Partner) is CrewMember p0 && Fit(p0, t, lab) > -1f)) return true;
        var ranked = w.Crew.Where(c => Fit(c, t, lab) > -1f).OrderByDescending(c => Fit(c, t, lab)).ThenBy(c => c.Id).ToList();
        // 실험실이 무서워 피한 사람 (가장 잘 맞을 사람이 빠졌다)
        var afraid = w.Crew.Where(c => !c.Dead && !c.IsChild && Fit(c, t, lab) <= -4f).ToList();
        if (afraid.Count > 0 && (ranked.Count == 0 || afraid.Any(a => 0.6f * a.RawSkill(SkillOf(t.Field)) + Interest(a, t.Field) > Fit(ranked[0], t, lab))))
        {
            Stats.Avoided++;
            foreach (var a in afraid) Life.Diary(w, a, Persona.Say(a, $"{lab?.Name ?? "실험실"}에는 아직 못 들어가겠다 — {t.Name} 실험은 남에게 미뤘다."));
        }
        if (ranked.Count == 0) return false;
        var nl = ranked[0];
        bool changed = x.Lead >= 0 && x.Lead != nl.Id;
        x.Lead = nl.Id;
        x.Style = StyleOf(nl);
        var why = new List<string>();
        float sk = nl.RawSkill(SkillOf(t.Field));
        if (sk >= 0.5f) why.Add($"{Skills.Name(SkillOf(t.Field))} {sk:0.00}");
        if (Interest(nl, t.Field) >= 0.15f) why.Add($"{TechFieldName(t.Field)}에 관심");
        why.Add($"{StyleName(x.Style)}한 연구자");
        x.LeadWhy = string.Join(" · ", why);
        x.Partner = -1;
        if (ranked.Count >= 2 && w.Crew.Count(c => !c.Dead && !c.IsChild) >= 4)
        {
            var p = ranked.Skip(1).FirstOrDefault(c => c.AffinityTo(nl) >= -0.1f && Fit(c, t, lab) >= 0.3f);
            if (p != null) x.Partner = p.Id;
        }
        if (changed) w.Log.Add(w.Tick, LogKind.Work, $"{t.Name} 실험을 {Ko.IGa(nl.Name)} 이어 맡는다 ({x.LeadWhy})", nl.Id);
        return true;
    }

    private static string TechFieldName(TechField f) => EraSystem.Fields(f);

    /// <summary>사고가 날 만한 정도 (컴퓨터 경고 · 결과 판정이 같은 식을 쓴다).</summary>
    public float AccidentRisk(ExperimentState x, CrewMember? c, Room? room)
    {
        var t = TechWeb.Find(x.Tech);
        if (t == null) return 0f;
        float p = 0.07f;
        p *= x.Style switch { ResearchStyle.Bold => 2f, ResearchStyle.Cautious => 0.4f, _ => 1f };
        p *= t.Field switch
        {
            TechField.Power or TechField.Propulsion or TechField.Defense or TechField.Fabrication => 1.3f,
            TechField.Computing or TechField.Medical => 0.6f,
            _ => 1f,
        };
        if (room != null && room.Air.O2 > 23.5f) p *= 2f; // 산소가 짙으면 불꽃이 불이 된다
        if (c != null && c.Needs.Rest < 0.3f) p *= 1.5f;
        if (x.Partner >= 0 && Crew(x.Partner) is CrewMember pa && StyleOf(pa) == ResearchStyle.Cautious) p *= 0.7f; // 신중한 짝이 말린다
        p *= Mul("lab.accident");
        return MathF.Min(0.6f, p);
    }

    /// <summary>주 컴퓨터: 위험한 실험이면 미리 경고한다 — 믿는 사람은 천천히(신중) 한다.</summary>
    private void Warn(ExperimentState x, EraTech t, Furniture bench)
    {
        var w = _w;
        if (!w.Automation.Present || !w.Automation.MainOnline) return;
        var lead = Crew(x.Lead);
        float p = AccidentRisk(x, lead, bench.Room);
        if (p < 0.1f) return;
        var why = new List<string>();
        if (x.Style == ResearchStyle.Bold) why.Add($"{lead?.Name}은(는) 대담하게 한다");
        if (bench.Room.Air.O2 > 23.5f) why.Add($"{bench.Room.Name} 산소 {bench.Room.Air.O2:0.0}kPa");
        if (lead != null && lead.Needs.Rest < 0.3f) why.Add("지쳐 있다");
        if (t.Field is TechField.Power or TechField.Propulsion or TechField.Defense or TechField.Fabrication) why.Add($"{TechFieldName(t.Field)} 실험은 터지기 쉽다");
        var act = w.Automation.Book.Add(ActKind.Advice, bench.Room, $"{t.Name} 실험 — 사고 위험 {p * 100f:0}%", "원인 추정: " + string.Join(" · ", why),
            "천천히 · 둘이서 · 산소를 낮추고", "실험 전에 소화기를 곁에 두세요", "techwarn:" + t.Id, SimTime.Hours(6));
        if (act == null) return;
        x.Warned = true;
        Stats.Warnings++;
        if (lead == null) return;
        float trust = w.Automation.Trusts.Of(lead);
        if (trust >= 0.5f || lead.Habits.Contains(Habit.Worrier))
        {
            x.Heeded = true;
            x.Style = ResearchStyle.Cautious;
            Stats.Heeded++;
            w.Log.Add(w.Tick, LogKind.Work, $"{lead.Name}: 컴퓨터 경고를 듣고 {t.Name} 실험을 천천히 하기로 했다", lead.Id);
        }
        else Life.Diary(w, lead, Persona.Say(lead, $"컴퓨터가 {t.Name} 실험이 위험하다고 한다. 해 보면 안다."));
    }

    // ─────────────────────────────── 진행 ───────────────────────────────

    /// <summary>한 틱의 실험 (WaitToil이 부른다).</summary>
    internal void Work(CrewMember c, ExperimentState x)
    {
        var w = _w;
        var t = TechWeb.Find(x.Tech);
        if (t == null) return;
        bool lead = c.Id == x.Lead;
        if (lead) x.LeadTick = w.Tick; else x.PartnerTick = w.Tick;
        bool together = x.Partner >= 0 && w.Tick - x.LeadTick <= 1 && w.Tick - x.PartnerTick <= 1;
        float skill = c.SkillLevel(SkillOf(t.Field));
        float speed = (0.55f + 0.9f * skill) * (lead ? 1f : 0.6f);
        speed *= x.Style switch { ResearchStyle.Bold => 1.3f, ResearchStyle.Cautious => 0.8f, _ => 1f };
        if (together) speed *= 1.15f;
        if (x.ReadNotes) speed *= 1.1f;
        if (c.Room is Room r && r.Dark && c.Suit == null) speed *= 0.75f;
        speed *= w.Portable.LampWorkMul(c) * (1f - 0.3f * c.Vitals.Injury) * Mul("lab.speed");
        x.Progress = MathF.Min(1f, x.Progress + speed / (x.Hours * SimTime.TicksPerHour));
        x.LastWork = w.Tick;
        if (!x.Hands.Contains(c.Id)) x.Hands.Add(c.Id);
    }

    /// <summary>시작(또는 재개): 끊긴 실험이면 노트를 펴고 이어 한다.</summary>
    internal void Begin(CrewMember c, ExperimentState x)
    {
        var w = _w;
        var t = TechWeb.Find(x.Tech);
        if (t == null) return;
        if (!x.ReadNotes)
        {
            var mine = Notes.Where(n => n.Field == t.Field && n.Author != c.Id).ToList();
            if (mine.Count > 0)
            {
                x.ReadNotes = true;
                Stats.NotesRead++;
                foreach (var n in mine) n.Reads++;
            }
        }
        if (!x.Paused) { x.LastWork = w.Tick; return; }
        x.Paused = false;
        x.Sessions++;
        x.LastWork = w.Tick;
        Stats.Resumed++;
        var note = Notes.LastOrDefault(n => n.Kind == 0 && n.Tech == x.Tech);
        if (note != null)
        {
            note.Reads++;
            Notes.Remove(note);
            if (note.Author != c.Id) Stats.NotesRead++;
        }
        string by = note != null && note.Author != c.Id && Crew(note.Author) is CrewMember au ? $"{au.Name}의 노트를 펴고" : "노트를 펴고";
        w.Log.Add(w.Tick, LogKind.Work, $"{t.Name} 실험 — {by} {x.Progress * 100f:0}%부터 이어 한다", c.Id);
        Note($"실험 재개 — {t.Name} ({c.Name} · {x.Progress * 100f:0}%부터)");
    }

    private void AddNote(ExperimentState x, EraTech t, CrewMember? by, Furniture bench, int kind, string text)
    {
        var at = bench.Cells.Count > 0 ? bench.Cells[(Notes.Count + bench.Id) % bench.Cells.Count] : Cell.FromPosition(bench.Center);
        Notes.Add(new ResearchNote { Id = _noteId++, Tech = t.Id, Field = t.Field, Author = by?.Id ?? -1, At = at, Room = bench.Room.Id, Progress = x.Progress, Tick = _w.Tick, Text = text, Kind = kind });
        Stats.Notes++;
        if (Notes.Count > 14) Notes.RemoveAt(0);
    }

    /// <summary>실험이 끝났다: 결과를 굴린다 (끝낸 사람이 부른다).</summary>
    internal void Finish(CrewMember c, ExperimentState x)
    {
        var w = _w;
        if (Trial != x) return;
        var t = TechWeb.Find(x.Tech);
        var bench = BenchOf(x);
        if (t == null || bench == null) { Trial = null; return; }
        var lead = Crew(x.Lead) ?? c;
        var partner = x.Partner >= 0 && x.Hands.Contains(x.Partner) ? Crew(x.Partner) : null;
        float skill = lead.SkillLevel(SkillOf(t.Field));
        if (partner != null) skill = MathF.Max(skill, 0.5f * (skill + partner.SkillLevel(SkillOf(t.Field))));
        float pAcc = AccidentRisk(x, lead, bench.Room);
        float pBreak = 0.07f * (x.Style switch { ResearchStyle.Bold => 1.7f, ResearchStyle.Cautious => 0.7f, _ => 1f }) * (0.6f + skill) * (partner != null ? 1.3f : 1f) * (x.ReadNotes ? 1.2f : 1f);
        float pFail = Math.Clamp(0.32f - 0.25f * skill, 0.08f, 0.35f) * (x.Style == ResearchStyle.Cautious ? 0.8f : 1f);
        float r = R.Float();
        string outcome = x.Force ?? (r < pAcc ? "accident" : r < pAcc + pBreak ? "breakthrough" : r < pAcc + pBreak + pFail ? "fail" : "success");
        Stats.Experiments++;
        foreach (int id in x.Hands) Experiments[id] = Experiments.GetValueOrDefault(id) + 1;
        if (!_hands.TryGetValue(t.Id, out var hl)) _hands[t.Id] = hl = new List<int>();
        foreach (int id in x.Hands) if (!hl.Contains(id)) hl.Add(id);
        float cost = CostOf(t);
        bool mineProject = w.Eras.Project == t.Id;
        string who = partner != null ? $"{lead.Name} · {partner.Name}" : lead.Name;
        switch (outcome)
        {
            case "breakthrough":
            {
                Stats.Breakthroughs++;
                if (mineProject) w.Eras.Boost(cost * 0.45f);
                foreach (int id in x.Hands) if (Crew(id) is CrewMember h) { h.Practice(SkillOf(t.Field), 0.03f); h.Needs.Stress = MathF.Max(0f, h.Needs.Stress - 0.08f); }
                AddNote(x, t, lead, bench, 4, $"{t.Name} — 돌파구!");
                Marks.Add(new ResearchMark(w.Tick, Cell.FromPosition(bench.Center), 1, t.Field, bench.Room.Id, "돌파구"));
                w.History.Add(w, HistoryKind.Milestone, $"{t.Name} 실험에서 돌파구 — {who} ({StyleName(x.Style)})", bench.Room, x.Hands.Select(Crew).Where(h => h != null)!, log: true);
                Life.Diary(w, lead, Persona.Say(lead, $"{t.Name} — 됐다! 몇 번을 다시 해도 같은 값이 나온다."));
                Note($"돌파구 — {t.Name} ({who})", 1);
                break;
            }
            case "success":
                Stats.Successes++;
                if (mineProject) w.Eras.Boost(cost * (0.14f + 0.1f * skill));
                foreach (int id in x.Hands) if (Crew(id) is CrewMember h) h.Practice(SkillOf(t.Field), 0.015f);
                AddNote(x, t, lead, bench, 1, $"{t.Name} — 값이 맞았다");
                w.Log.Add(w.Tick, LogKind.Work, $"{t.Name} 실험 성공 — 연구가 한 걸음 나갔다 ({who})", lead.Id);
                Note($"실험 성공 — {t.Name} ({who})", 1);
                break;
            case "fail":
                Stats.Failures++;
                if (mineProject) w.Eras.Boost(cost * 0.03f);
                foreach (int id in x.Hands) if (Crew(id) is CrewMember h) { h.Practice(SkillOf(t.Field), 0.008f); h.Needs.Stress = MathF.Min(1f, h.Needs.Stress + 0.03f); }
                AddNote(x, t, lead, bench, 2, $"{t.Name} — 실패. 원인 추정을 적어 둔다");
                Marks.Add(new ResearchMark(w.Tick, Cell.FromPosition(bench.Center), 2, t.Field, bench.Room.Id, "실패"));
                w.Log.Add(w.Tick, LogKind.Work, $"{t.Name} 실험 실패 — 노트에 원인을 적었다 ({who})", lead.Id);
                Note($"실험 실패 — {t.Name} ({who})", 2);
                break;
            default:
                Accident(x, t, lead, partner, bench);
                break;
        }
        if (partner != null && outcome != "accident")
        {
            Stats.Collabs++;
            lead.ChangeAffinity(partner, 0.04f);
            partner.ChangeAffinity(lead, 0.04f);
        }
        if (x.Relic) Stats.RelicStudies++;
        if (Marks.Count > 12) Marks.RemoveAt(0);
        _nextTrial = w.Tick + SimTime.Hours(outcome switch { "accident" => 10f, "fail" => 3f, _ => 4f });
        Trial = null;
    }

    /// <summary>작은 사고 — 분야마다 다르다 (방전 · 시약 · 냉매 · 분진 · 불똥 · 로봇 팔). 폭발은 Blast, 불은 Fire.</summary>
    private void Accident(ExperimentState x, EraTech t, CrewMember lead, CrewMember? partner, Furniture bench)
    {
        var w = _w;
        Stats.Accidents++;
        var room = bench.Room;
        var cell = bench.Cells.Count > 0 ? bench.Cells[0] : Cell.FromPosition(bench.Center);
        string what;
        bool fire = false;
        BlastKind? kind = null;
        float power = 0.06f;
        switch (t.Field)
        {
            case TechField.Power or TechField.Defense or TechField.Sensors: kind = BlastKind.Arc; power = 0.07f; what = "축전기가 방전하며 펑"; break;
            case TechField.Propulsion: kind = BlastKind.Propellant; power = 0.08f; what = "시험 노즐의 추진제가 터졌다"; break;
            case TechField.Cooling: kind = BlastKind.ColdGas; power = 0.07f; what = "냉매 시료관이 터져 서리가 뿜어졌다"; break;
            case TechField.Food: kind = BlastKind.Ferment; power = 0.05f; what = "발효 시료 병이 터졌다"; break;
            case TechField.Life or TechField.Medical: kind = BlastKind.Gas; power = 0.05f; what = "시약 병이 터져 매운 김이 올랐다"; break;
            case TechField.Hull or TechField.Fabrication:
                if (R.Chance(0.5f)) { kind = BlastKind.Dust; power = 0.06f; what = "금속 분진에 불꽃이 튀어 펑"; }
                else { fire = true; what = "용접 불똥이 걸레에 붙었다"; }
                break;
            case TechField.Robotics: what = "시험 로봇 팔이 제멋대로 휘둘렀다"; lead.Vitals.Injury = MathF.Min(1f, lead.Vitals.Injury + 0.07f); break;
            default: fire = true; what = "시험 기판에서 불꽃이 일었다"; break;
        }
        if (kind is BlastKind k) w.Blast.Detonate(cell, power, k, $"실험 사고 — {t.Name}");
        if (fire || room.Air.O2 > 23.5f && kind is BlastKind.Arc or BlastKind.Dust or BlastKind.Propellant)
        {
            w.Fire.Ignite(cell, 0.3f);
            if (!fire) what += " — 산소가 짙어 불이 붙었다";
        }
        lead.Vitals.Injury = MathF.Min(1f, lead.Vitals.Injury + 0.03f);
        lead.Needs.Stress = MathF.Min(1f, lead.Needs.Stress + 0.12f);
        Memory.Frighten(w, lead, room, 0.25f, $"실험 사고 — {t.Name}");
        foreach (var c in w.Crew)
            if (c != lead && !c.Dead && c.Room == room && c.IsAwake) Memory.Frighten(w, c, room, 0.12f, $"실험 사고를 봤다 — {t.Name}");
        if (x.Style == ResearchStyle.Bold)
        {
            _shaken[lead.Id] = w.Tick + SimTime.TicksPerDay * 3;
            Stats.Shaken++;
            Life.Diary(w, lead, Persona.Say(lead, $"{t.Name} 실험이 터졌다. 내가 너무 서둘렀다 — 한동안은 천천히 한다."));
        }
        else Life.Diary(w, lead, Persona.Say(lead, $"{t.Name} 실험 중에 {what}. 손이 아직 떨린다."));
        if (partner != null)
        {
            partner.Needs.Stress = MathF.Min(1f, partner.Needs.Stress + 0.08f);
            if (x.Style == ResearchStyle.Bold)
            {
                partner.ChangeAffinity(lead, -0.05f);
                Life.Diary(w, partner, Persona.Say(partner, $"{Ko.IGa(lead.Name)} 서둘렀다. 천천히 하자고 했는데."));
            }
        }
        AddNote(x, t, lead, bench, 3, $"{t.Name} — 사고: {what}");
        Marks.Add(new ResearchMark(w.Tick, cell, 0, t.Field, room.Id, what));
        string text = $"실험 사고 — {room.Name}: {what} ({lead.Name} · {StyleName(x.Style)}한 연구자" + (x.Warned && !x.Heeded ? " · 컴퓨터 경고를 듣지 않았다" : "") + ")";
        w.History.Add(w, HistoryKind.Incident, text, room, new[] { lead }, cell, log: true);
        w.RaiseAlert(text, room, AlertLevel.Notice, shipWide: false);
        Note(text, 2);
        // 주 컴퓨터가 읽는다: 무엇이 사고를 키웠나 · 다음 실험은 어떻게
        if (w.Automation.Present)
        {
            var why = new List<string>();
            if (x.Style == ResearchStyle.Bold) why.Add("대담한 연구자");
            if (room.Air.O2 > 23.5f) why.Add($"산소 {room.Air.O2:0.0}kPa");
            if (lead.Needs.Rest < 0.3f) why.Add("지친 손");
            if (x.Warned && !x.Heeded) why.Add("경고를 듣지 않았다");
            if (why.Count == 0) why.Add($"{TechFieldName(t.Field)} 실험의 본래 위험");
            w.Automation.Book.Add(ActKind.Advice, room, $"실험 사고 — {what}", "원인 추정: " + string.Join(" · ", why), "다음 실험은 신중한 연구자에게 · 열 시간 쉰다",
                "실험실을 환기하고 다친 사람을 봐 주세요", "techacc:" + t.Id, SimTime.Hours(2));
        }
    }

    /// <summary>익힌 기술에 실험으로 보탠 사람들을 연대기에 남긴다.</summary>
    private void CreditResearchers(EraTech t)
    {
        var w = _w;
        if (!_hands.TryGetValue(t.Id, out var hl) || hl.Count == 0) return;
        var people = hl.Select(Crew).Where(c => c != null && !c.Dead).Cast<CrewMember>().ToList();
        if (people.Count == 0) return;
        w.History.Add(w, HistoryKind.Upgrade, $"{t.Name} — {string.Join(" · ", people.Select(c => c.Name))}의 실험이 보탰다", null, people, log: true);
        foreach (var c in people) Life.Diary(w, c, Persona.Say(c, $"{t.Name}을(를) 익혔다. 그 실험대의 시간이 헛되지 않았다."));
        _hands.Remove(t.Id);
        if (Trial?.Tech == t.Id) Trial = null;
    }

    // ─────────────────────────────── 회의 · 주 컴퓨터 ───────────────────────────────

    /// <summary>주 컴퓨터가 다음 연구를 고른다 — 이 배의 기록(겪은 사고 · 줄일 위험 · 새로 생길 위험 · 열어 줄 기술)을 근거로.</summary>
    public string? Advise(List<EraTech> options, Func<EraTech, float> need)
    {
        var w = _w;
        RecTech = null;
        RecWhy = "";
        if (options.Count == 0 || !w.Automation.Present || !w.Automation.MainOnline) return null;
        EraTech? best = null;
        float bestS = float.MinValue;
        List<(float v, string why)>? bestWhy = null;
        bool twin = Known("digitaltwin");
        foreach (var t in options)
        {
            var why = new List<(float v, string why)>();
            float n = need(t);
            if (n > 0.3f) why.Add((n, $"겪은 일({TechFieldName(t.Field)}) {n:0.0}"));
            var fx = TechWeb.RowOf(t.Id)?.Fx ?? ErasV15.Rows.FirstOrDefault(r => r.Tech.Id == t.Id)?.Fx ?? Array.Empty<(string, float)>();
            foreach (var (k, m) in fx)
            {
                int inc = TechWeb.Incidents(w, k);
                if (m >= 1f || inc == 0) continue;
                float v = (1f - m) * (0.3f + 0.4f * MathF.Min(4, inc)) * (twin ? 1.2f : 1f);
                why.Add((v, $"{TechWeb.KeyName(k)} {inc}번 겪음 → −{(1f - m) * 100f:0}%"));
            }
            if (t.RiskKey != null)
            {
                int inc = TechWeb.Incidents(w, t.RiskKey);
                float v = -(t.RiskMul - 1f) * (0.3f + 0.5f * MathF.Min(4, inc));
                if (inc > 0) why.Add((v, $"위험: {TechWeb.KeyName(t.RiskKey)} 이미 {inc}번"));
                else why.Add((v * 0.5f, $"위험: {t.Risk}"));
            }
            int opens = 0;
            foreach (var nd in TechWeb.Nodes.Values) if (Array.IndexOf(nd.Pre, t.Id) >= 0 && !Known(nd.Id)) opens++;
            if (opens >= 2) why.Add((0.04f * opens, $"선행으로 {opens}개를 연다"));
            float s = why.Sum(x => x.v) - 0.004f * CostOf(t);
            if (s > bestS || s == bestS && best != null && string.CompareOrdinal(t.Id, best.Id) < 0) { bestS = s; best = t; bestWhy = why; }
        }
        if (best == null) return null;
        RecTech = best.Id;
        RecTick = w.Tick;
        RecWhy = bestWhy!.Count == 0 ? "싸고 빨리 끝난다" : string.Join(" · ", bestWhy.OrderByDescending(x => MathF.Abs(x.v)).Take(3).Select(x => x.why));
        Stats.Advices++;
        var lab = PickBench()?.Room;
        string rec = best.Id;
        w.Automation.Book.Add(ActKind.Advice, lab, $"연구 후보 {options.Count}개", $"추천: {best.Name} — {RecWhy}", $"다음 연구로 {Ko.EulReul(best.Name)} 권한다",
            "회의에서 정해 주세요", "techrec:" + best.Id, SimTime.Hours(4), 60f,
            (world, a) => world.Eras.Known.Contains(rec) ? (1, $"권한 {best.Name}을(를) 익혔다") : world.Eras.Project == rec ? null : (2, "참고 — 회의가 다른 연구를 골랐다"));
        return RecTech;
    }

    /// <summary>회의가 기우는 정도: 컴퓨터 추천 × 사람들의 믿음 · 사람들의 관심 · 배에 있는 유물 · 갈림길에서 고른 길.</summary>
    public float Bias(EraTech t, string? rec)
    {
        var w = _w;
        float b = 0f;
        float trust = 0f, interest = 0f;
        int n = 0;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.IsChild || !c.CanAct) continue;
            n++;
            trust += w.Automation.Trusts.Of(c);
            interest += Interest(c, t.Field);
        }
        if (n > 0)
        {
            if (rec == t.Id) b += 0.45f * trust / n;
            b += MathF.Min(0.3f, 0.06f * interest);
        }
        if (TechWeb.Node(t.Id).Trial) b += 0.5f; // 배에 둔 유물 — 다들 궁금하다
        // 고른 길을 잇는 기술 (배의 정체성)
        foreach (var f in TechWeb.Forks)
        {
            var st = ForkStates[f.Id];
            if (st.Side < 0) continue;
            string chosen = st.Side == 0 ? f.A : f.B;
            if (Array.IndexOf(TechWeb.Node(t.Id).Pre, chosen) >= 0) b += 0.12f;
        }
        return b;
    }

    /// <summary>회의가 고른 것이 추천과 같았나 (연대기 한 마디).</summary>
    public string Followed(EraTech pick, string? rec)
    {
        if (rec == null) return "";
        if (pick.Id == rec) { Stats.Followed++; return " · 컴퓨터 추천대로"; }
        Stats.Ignored++;
        return $" · 컴퓨터는 {Ko.EulReul(TechWeb.Find(rec)?.Name ?? rec)} 권했다";
    }

    /// <summary>이 사람이 지금 실험 중인가 (화면 · 다른 시스템).</summary>
    public bool Researching(CrewMember c) => Trial is ExperimentState x && (x.Lead == c.Id || x.Partner == c.Id) && c.Job?.Activity is ResearchActivity;
}

/// <summary>실험: 맡은 사람이 실험실 · 작업대로 가서 (유물이면 먼저 살펴보고) 실험한다. 끊기면 노트가 남고 이어 한다.</summary>
public sealed class ResearchActivity : Activity
{
    public override string Id => "research";
    public override string Label => "실험";

    public override (float, string) Score(CrewMember c, World w, DistanceField dist)
    {
        var tw = w.TechWeb;
        if (tw.Trial is not ExperimentState x) return (0f, "—");
        bool lead = x.Lead == c.Id, partner = x.Partner == c.Id;
        if (!lead && !partner) return (0f, "—");
        if (c.IsChild || !c.CanAct || w.Movement.Hurry(w)) return (0f, "지금은 실험할 때가 아니다");
        if (tw.BenchOf(x) is not Furniture bench || !bench.UseSpots.Any(dist.Reachable)) return (0f, "실험할 자리가 없다");
        var t = TechWeb.Find(x.Tech);
        if (t == null) return (0f, "—");
        if (partner && !(w.Crew[x.Lead].Job?.Activity is ResearchActivity) && x.Progress < 0.05f) return (0f, $"{w.Crew[x.Lead].Name}을(를) 기다린다");
        float s = 0.34f + 0.2f * c.Traits.Diligence + 0.3f * TechWebSystem.Interest(c, t.Field) + (OnShift(c, w) ? 0.12f : -0.08f) + (x.Paused ? 0.06f : 0f);
        if (partner) s -= 0.06f;
        if (Bedtime(c, w)) s -= 0.35f;
        s -= 0.3f * c.Needs.Stress + 0.4f * c.Memory.FearOf(bench.Room);
        string why = lead ? $"{t.Name} 실험 — {x.LeadWhy}" + (x.Paused ? " · 끊긴 실험을 이어 한다" : "") : $"{w.Crew[x.Lead].Name}의 {t.Name} 실험을 거든다";
        return (MathF.Max(0f, s), why);
    }

    public override Job? Plan(CrewMember c, World w, DistanceField dist)
    {
        var tw = w.TechWeb;
        if (tw.Trial is not ExperimentState x || tw.BenchOf(x) is not Furniture bench) return null;
        var t = TechWeb.Find(x.Tech);
        if (t == null) return null;
        var spots = bench.UseSpots.Where(dist.Reachable).OrderBy(dist.Get).ToList();
        if (spots.Count == 0) return null;
        bool lead = x.Lead == c.Id;
        var spot = lead || spots.Count == 1 ? spots[0] : spots[1];
        if (!lead && spots.Count == 1)
            spot = Cell.Dirs8.Select(d => spots[0] + d).Where(s => w.Ship.IsWalkable(s) && dist.Reachable(s) && !w.IsSpotTaken(s, c)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault() ?? spots[0];
        var toils = Plans.DropOff(c, w, dist);
        // 유물 역설계: 먼저 유물을 살펴본다
        if (x.Relic && !x.RelicSeen && lead && TechWeb.Node(t.Id).Gate is TechGate g && tw.RelicFor(g.Key) is PlacedProp relic)
        {
            var near = new[] { relic.At }.Concat(Cell.Dirs8.Select(d => relic.At + d)).Where(s => w.Ship.IsWalkable(s) && dist.Reachable(s)).OrderBy(dist.Get).Cast<Cell?>().FirstOrDefault();
            if (near is Cell nc)
            {
                toils.Add(new GotoToil(nc));
                toils.Add(new WaitToil(SimTime.Minutes(20), Pose.Standing, relic.At.Center) { EveryTick = (cm, _) => cm.Needs.Stress = MathF.Max(0f, cm.Needs.Stress - 0.0001f) });
            }
            toils.Add(new DoToil((cm, world) =>
            {
                x.RelicSeen = true;
                world.Log.Add(world.Tick, LogKind.Work, $"{relic.Name}을(를) 들여다보며 재고 적었다 — {t.Name}", cm.Id);
                return true;
            }));
        }
        toils.Add(new GotoToil(spot));
        toils.Add(new DoToil((cm, world) => { if (world.TechWeb.Trial != x) return false; world.TechWeb.Begin(cm, x); return true; }));
        int max = (int)(SimTime.Hours(x.Hours) * 2.2f);
        toils.Add(new WaitToil(max, Pose.Working, bench.Center)
        {
            EveryTick = (cm, world) => { if (world.TechWeb.Trial == x) world.TechWeb.Work(cm, x); },
            DoneWhen = (cm, world) => world.TechWeb.Trial != x || x.Progress >= 1f || bench.Room.Abandoned || bench.Machine is { Stopped: true },
        });
        toils.Add(new DoToil((cm, world) =>
        {
            if (world.TechWeb.Trial == x && x.Progress >= 1f) world.TechWeb.Finish(cm, x);
            return true;
        }));
        string label = x.Relic ? $"{t.Name} (유물 역설계)" : $"{t.Name} 실험";
        return new Job(this, label, toils)
        {
            LogText = lead ? (x.Paused ? $"{t.Name} 실험을 이어 하러 간다 ({TechWebSystem.StyleName(x.Style)})" : $"{bench.Room.Name}에서 {t.Name} 실험 ({TechWebSystem.StyleName(x.Style)})") : $"{w.Crew[x.Lead].Name}의 {t.Name} 실험을 거든다",
            LogKind = LogKind.Work,
        };
    }
}
