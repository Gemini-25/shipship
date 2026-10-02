using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v15.9 칭호·업적 50: 배에 붙는 이름 25 · 사람에게 붙는 이름 25.
// 조건은 이미 쌓이는 기록(연대기 · 사고 · 일상 · 관행 · 오염 · 배관 · 부품 · 사람마다의 기록)으로만 본다.
// 얻으면 연대기와 일지에 남고, 사람은 일기에 적으며 조금 풀린다 (배 칭호는 모두가 조금씩). 같은 칭호는 두 번 붙지 않는다.

public enum TitleScope { Ship, Crew }

public static class ChronicleV15
{
    public sealed record Row(string Id, TitleScope Scope, string Name, string Note, Func<World, bool>? Ship = null, Func<World, CrewMember, bool>? Crew = null);

    private static Row S(string id, string name, string note, Func<World, bool> f) => new(id, TitleScope.Ship, name, note, Ship: f);
    private static Row C(string id, string name, string note, Func<World, CrewMember, bool> f) => new(id, TitleScope.Crew, name, note, Crew: f);
    private static float H(long ticks) => ticks / (float)SimTime.TicksPerHour;

    public static readonly Row[] Rows =
    {
        // ── 배 ──
        S("voyage7", "이레를 넘긴 배", "출항하고 이레 — 첫 고비를 넘겼다", w => w.Day >= 8),
        S("voyage30", "한 달을 난 배", "서른 날을 우주에서 났다", w => w.Day >= 31),
        S("voyage100", "백 날의 배", "백 날 — 이제 이 배가 집이다", w => w.Day >= 101),
        S("meteor1", "운석을 맞고도 뜬 배", "처음 운석을 맞고도 떠 있다", w => w.History.Meteors >= 1),
        S("meteor10", "별똥별 과녁", "운석을 열 번 맞았다", w => w.History.Meteors >= 10),
        S("fire5", "불을 아는 배", "불을 다섯 번 겪었다", w => w.History.Fires >= 5),
        S("breach5", "누더기 선체", "선체에 구멍이 다섯 번 났다 — 덧댄 자리투성이", w => w.History.Breaches >= 5),
        S("nodeath30", "아무도 잃지 않은 한 달", "서른 날 동안 한 사람도 잃지 않았다", w => w.Day >= 31 && w.History.Deaths == 0),
        S("memorial", "기억하는 배", "떠난 사람의 추모일을 지킨다", w => w.Culture.Customs.Any(c => c.Kind == CustomKind.Memorial)),
        S("customs3", "관행이 있는 배", "겪은 일이 관행 셋이 되었다", w => w.Culture.Customs.Count >= 3),
        S("explained10", "까닭을 전하는 배", "관행의 까닭을 열 번 말로 전했다", w => w.Culture.Stats.Explained >= 10),
        S("daily30", "사는 이야기가 있는 배", "사고 아닌 일상 사건이 서른 번", w => w.Daily.Stats.Fired >= 30),
        S("dailyseen25", "별일 다 겪은 배", "일상 사건을 스물다섯 가지 겪었다", w => w.Daily.Stats.Seen.Count >= 25),
        S("wash100", "손 씻는 배", "손을 백 번 씻었다", w => w.Soil.Stats.HandWashes >= 100),
        S("decon20", "털고 들어오는 배", "에어락에서 분진을 스무 번 털었다", w => w.Soil.Stats.Decons >= 20),
        S("equalize20", "압을 맞추는 배", "문 앞에서 균압 밸브를 스무 번 돌렸다", w => w.Flow.Stats.Equalized >= 20),
        S("theseus", "테세우스의 배", "부품을 마흔 번 갈았다 — 처음 그 배가 맞나", w => w.Parts.Stats.Replaced >= 40),
        S("handmade5", "손때 묻은 배", "손으로 깎은 부품이 다섯 개 돌고 있다", w => w.Parts.Stats.ByOrigin[(int)PartOrigin.Handmade] >= 5),
        S("prevent10", "미리 막는 배", "전조를 잡아 고장을 열 번 막았다", w => w.Precursors.Prevented >= 10),
        S("warned5", "먼저 보는 배", "운석을 다섯 번 미리 알렸다", w => w.Sensors.Warned >= 5),
        S("rescue", "건져 올린 배", "탈출 캡슐에서 사람을 건졌다", w => w.Comms.Rescued >= 1),
        S("hazards15", "산전수전", "사고를 열다섯 번 넘겼다", w => w.Hazards.Count.Sum() >= 15),
        S("smother", "불을 굶긴 배", "산소를 빼서 불을 껐다", w => w.Automation.Smothered + w.Automation.Vacuumed >= 1),
        S("thinking", "생각하는 배", "주 컴퓨터가 겪은 일로 판단 여섯 가지를 다듬었다", w => w.Automation.Core.GradeSum >= 6), // v16.20 처음부터 다 있다 — 자란 만큼
        S("dark", "어둠을 견딘 배", "정전 속에서 열두 시간을 버텼다", w => w.History.DarkHours >= 12f),

        // ── 사람 ──
        C("fixer", "고치는 손", "고장을 열 번 고쳤다", (w, c) => c.Stats.Repairs >= 10),
        C("fixer40", "기계의 친구", "고장을 마흔 번 고쳤다", (w, c) => c.Stats.Repairs >= 40),
        C("service", "기름칠하는 사람", "정비를 열다섯 번 했다", (w, c) => c.Stats.Services >= 15),
        C("cook", "배의 부엌", "밥을 서른 그릇 지었다", (w, c) => c.Stats.MealsCooked >= 30),
        C("farmer", "초록 손", "열 번 거뒀다", (w, c) => c.Stats.Harvests >= 10),
        C("chat", "말동무", "마흔 번 이야기를 나눴다", (w, c) => c.Stats.Chats >= 40),
        C("rescuer", "끌어낸 사람", "쓰러진 사람을 끌어냈다", (w, c) => c.Stats.Rescues >= 1),
        C("rescuer3", "구조대", "쓰러진 사람을 세 번 끌어냈다", (w, c) => c.Stats.Rescues >= 3),
        C("firstin", "먼저 달려가는 사람", "비상에 다섯 번 달려갔다", (w, c) => c.Stats.Emergencies >= 5),
        C("steady", "흔들리지 않는 사람", "비상 세 번에 한 번도 얼어붙지 않았다", (w, c) => c.Stats.Emergencies >= 3 && c.Stats.Panics == 0),
        C("teacher", "가르치는 사람", "다섯 번 가르쳤다", (w, c) => c.Stats.Taught >= 5),
        C("student", "배우는 사람", "열 번 배웠다", (w, c) => c.Stats.Lessons >= 10),
        C("survivor", "다시 일어난 사람", "쓰러졌다가 일어났다", (w, c) => c.Stats.TimesDown >= 1 && !c.Down),
        C("hardluck", "굴곡진 사람", "세 번 쓰러졌다", (w, c) => c.Stats.TimesDown >= 3),
        C("eva", "바깥을 걷는 사람", "선체 밖에서 네 시간", (w, c) => c.EvaHours >= 4f),
        C("flawless", "실수 없는 손", "마흔 시간을 일하고 한 번도 틀리지 않았다", (w, c) => H(c.Stats.TicksWorking) >= 40f && c.Stats.Mistakes == 0),
        C("worker", "일벌레", "백스무 시간을 일했다", (w, c) => H(c.Stats.TicksWorking) >= 120f),
        C("quals", "자격 부자", "배에서 자격을 둘 더 땄다", (w, c) => w.Titles.QualsGained(c) >= 2),
        C("veteran", "고참", "이 배에서 이백 시간을 잤다", (w, c) => H(c.Stats.TicksAsleep) >= 200f),
        C("relax", "쉴 줄 아는 사람", "마흔 시간을 제대로 쉬었다", (w, c) => H(c.Stats.TicksRelaxing) >= 40f),
        C("fit", "단단한 몸", "체력 단련 85%", (w, c) => c.Fitness >= 0.85f),
        C("dose", "빛을 맞은 사람", "방사선 0.5Sv를 맞고도 일한다", (w, c) => c.Dose >= 0.5f),
        C("brave", "겁을 이긴 사람", "겁이 남았는데도 비상에 세 번 달려갔다", (w, c) => c.Memory.Trauma >= 0.3f && c.Stats.Emergencies >= 3),
        C("diarist", "일기 쓰는 사람", "일기가 서른 줄", (w, c) => c.Diary.Count >= 30),
        C("hungry", "배고픔을 아는 사람", "열두 시간을 굶었다", (w, c) => H(c.Stats.TicksStarving) >= 12f),
        // v16.12 원정
        S("halted1", "멈췄다 다시 간 배", "재료가 바닥나 멈췄다가 다시 엔진을 켰다", w => w.Expedition.Stats.Resumed >= 1),
        S("expedition5", "원정의 배", "원정대가 다섯 번 돌아왔다", w => w.Expedition.Stats.Returned >= 5),
        C("expleader", "원정대장", "원정대를 이끌고 돌아왔다", (w, c) => w.Expedition.Led(c) >= 1),
        C("explorer3", "떠돌이 채집꾼", "원정을 세 번 다녀왔다", (w, c) => w.Expedition.Went(c) >= 3),
        C("tether", "끈을 잡은 사람", "원정에서 떠내려가던 동료를 붙잡았다", (w, c) => w.Expedition.Saved(c) >= 1),
    };

    public static Row? Of(string id) => Rows.FirstOrDefault(r => r.Id == id);
}

/// <summary>v15.9 칭호: 한 시간마다 조건을 보고, 처음 맞으면 붙인다 (출항 이튿날부터).</summary>
public sealed class TitleSystem
{
    public sealed record Award(long Tick, string Id, int CrewId);

    private readonly World _w;
    private readonly HashSet<string> _earned = new();
    private readonly Dictionary<int, int> _quals0 = new(); // 처음 본 때의 자격 수
    private long _next;

    public List<Award> Awards { get; } = new();

    public TitleSystem(World w) => _w = w;

    private static string Key(string id, int crewId) => crewId < 0 ? id : $"{id}#{crewId}";
    public bool Has(string id, CrewMember? c = null) => _earned.Contains(Key(id, c?.Id ?? -1));

    /// <summary>배에 올라 처음 본 뒤로 더 딴 자격 수.</summary>
    public int QualsGained(CrewMember c) => _quals0.TryGetValue(c.Id, out var n) ? c.Quals.Count - n : 0;

    /// <summary>그 사람이 얻은 칭호 (얻은 차례대로).</summary>
    public IEnumerable<string> Of(CrewMember c) => Awards.Where(a => a.CrewId == c.Id).Select(a => ChronicleV15.Of(a.Id)!.Name);
    public IEnumerable<string> ShipTitles => Awards.Where(a => a.CrewId < 0).Select(a => ChronicleV15.Of(a.Id)!.Name);

    /// <summary>HUD 한 줄: 최근 것부터.</summary>
    public string Line(CrewMember c)
    {
        var t = Of(c).Reverse().ToList();
        return t.Count == 0 ? "—" : string.Join(" · ", t);
    }

    public void Update(float dt)
    {
        var w = _w;
        if (w.Tick < _next) return;
        _next = w.Tick + SimTime.TicksPerHour;
        foreach (var c in w.Crew) _quals0.TryAdd(c.Id, c.Quals.Count);
        if (w.Tick <= SimTime.TicksPerDay) return; // 첫날은 그대로
        Check();
    }

    /// <summary>지금 맞는 칭호를 모두 붙인다 (이미 있는 것은 건너뛴다). 붙인 수.</summary>
    public int Check()
    {
        var w = _w;
        int n = 0;
        foreach (var r in ChronicleV15.Rows)
        {
            if (r.Scope == TitleScope.Ship)
            {
                if (_earned.Contains(r.Id) || !r.Ship!(w)) continue;
                Give(r, null);
                n++;
                continue;
            }
            foreach (var c in w.Crew)
            {
                if (c.Dead || _earned.Contains(Key(r.Id, c.Id)) || !r.Crew!(w, c)) continue;
                Give(r, c);
                n++;
            }
        }
        return n;
    }

    private void Give(ChronicleV15.Row r, CrewMember? c)
    {
        var w = _w;
        if (!_earned.Add(Key(r.Id, c?.Id ?? -1))) return;
        Awards.Add(new Award(w.Tick, r.Id, c?.Id ?? -1));
        if (c == null)
        {
            w.History.Add(w, HistoryKind.Milestone, $"{w.Ship.Name}에 붙은 이름 — '{r.Name}' ({r.Note})", log: true);
            foreach (var p in w.Crew) if (!p.Dead) p.Needs.Stress = MathF.Max(0f, p.Needs.Stress - 0.02f); // 배가 자랑스럽다
            return;
        }
        w.History.Add(w, HistoryKind.Milestone, $"{c.Name}에게 붙은 이름 — '{r.Name}' ({r.Note})", crew: new[] { c }, log: true, crewLog: c.Id);
        c.Needs.Stress = MathF.Max(0f, c.Needs.Stress - 0.05f);
        Life.Diary(w, c, Persona.Say(c, $"다들 나더러 '{r.Name}' 소리를 한다 — {r.Note}"));
    }
}
