using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v23 외계 기생체 (사용자가 고른 26): 낯선 땅 · 포자 구름에서 묻어 온다.
//  잠복(하루 남짓 — 춥다 · 자꾸 먹는다 · 남몰래 숨기기도) → 발견(의사 로봇 · 의무관 · 주컴퓨터 생체 검사) → 격리할지
//  → 같은 방 사람에게 번짐 → 의무관이 치료법을 연구 (실험실 · 의무실이 있으면 빨리) → 치료.
//  너무 오래 두면 기생체가 몸 밖으로 빠져나와 환풍구에 숨는다 — 로봇이 방마다 수색하고, 그동안 배선 · 식량을 갉는다.

public sealed class ParasiteHost
{
    public int Crew { get; init; }
    public long Since { get; init; }
    public bool Found { get; set; }
    public bool Hiding { get; set; }
    public long SymptomAt { get; set; } = -1;
}

public sealed class ParasiteSystem
{
    public static bool Off;
    private readonly World _w;
    private readonly Rng _rng;
    private long _nextHour;
    public List<ParasiteHost> Hosts { get; } = new();
    public float Cure { get; private set; }
    public bool CureReady { get; private set; }
    public bool Loose { get; private set; }
    public int LooseRoom { get; private set; } = -1;
    public int Infected, Found, Cured, Escapes, Caught, Gnawed;

    public ParasiteSystem(World w) { _w = w; _rng = new Rng(unchecked(w.Seed * 61 + 2626)); }

    public bool Active => Hosts.Count > 0 || Loose;

    /// <summary>한 사람에게 옮겨 붙는다 (원정 · 포자 구름 · 시험).</summary>
    public bool Infect(CrewMember c, string why)
    {
        var w = _w;
        if (Off || c.Dead || Hosts.Any(h => h.Crew == c.Id)) return false;
        Hosts.Add(new ParasiteHost { Crew = c.Id, Since = w.Tick, Hiding = c.Value is CrewValue.Freedom || c.Traits.Bravery > 0.7f && _rng.Chance(0.5f) });
        Infected++;
        w.Causes.Root(CauseKind.Hazard, "외계 기생체", c.Room, c.Position);
        if (Hosts.Count == 1) CureReady = false;
        return true;
    }

    public void Update(float dt)
    {
        var w = _w;
        if (Off || w.Tick < _nextHour) return;
        _nextHour = w.Tick + SimTime.Hours(1);
        // 저절로: 포자 · 생물 환경에 머물면 아주 가끔
        if (!Active && w.Env.Now[EnvMech.Bio] > 0.3f && _rng.Chance(0.004f * w.Env.Now[EnvMech.Bio]))
        {
            var pool = w.Crew.Where(c => !c.Dead && !c.Away && !c.LeftShip && (c.Outside || c.Room?.Kind is RoomType.Airlock or RoomType.EvaPrep or RoomType.Hydroponics)).ToList();
            if (pool.Count > 0) Infect(pool[_rng.Range(0, pool.Count)], "포자");
        }
        foreach (var h in Hosts.ToList()) StepHost(h);
        if (Hosts.Any(h => h.Found)) Research();
        if (Loose) Hunt();
    }

    private CrewMember? CrewOf(ParasiteHost h) => _w.Crew.FirstOrDefault(c => c.Id == h.Crew);

    private void StepHost(ParasiteHost h)
    {
        var w = _w;
        var c = CrewOf(h);
        if (c == null || c.Dead || c.LeftShip) { Hosts.Remove(h); return; }
        long age = w.Tick - h.Since;
        // 잠복 → 증상
        if (h.SymptomAt < 0 && age > SimTime.Hours(26))
        {
            h.SymptomAt = w.Tick;
            w.Ailments.Catch(c, "parasite", null, "외계 기생체");
            Life.Diary(w, c, h.Hiding ? "몸이 이상하다. 말하면 격리될 거다. 조금만 더 버티자." : "자꾸 춥고 배가 고프다. 밤에 뱃속에서 뭔가 움직였다.");
            w.Log.Add(w.Tick, LogKind.Life, h.Hiding ? $"{Ko.IGa(c.Name)} 담요를 두르고 혼자 밥을 두 그릇 먹는다" : $"{Ko.IGa(c.Name)} 자꾸 춥다고 한다 — 밥을 두 그릇 먹었다", c.Id);
        }
        // 발견: 의무실 · 의사 로봇 · 의무관 · 주컴퓨터 생체 검사 (숨기면 더디다)
        if (!h.Found && h.SymptomAt >= 0)
        {
            bool medic = w.Crew.Any(o => o != c && !o.Dead && o.Room == c.Room && o.Role == CrewRole.Medic) || c.Room?.Kind is RoomType.Medbay or RoomType.Triage;
            bool bot = w.Robots.Robots.Any(r => r.Kind is RobotKind.Medic or RobotKind.Nurse && r.Room == c.Room && r.Operational);
            float p = (medic ? 0.35f : 0f) + (bot ? 0.3f : 0f) + (w.Automation.MainOnline ? 0.05f : 0f);
            if (h.Hiding) p *= 0.4f;
            if (_rng.Chance(p) || w.Tick - h.SymptomAt > SimTime.Hours(48))
            {
                h.Found = true; Found++;
                string by = bot ? "의사 로봇" : medic ? "의무관" : "주 컴퓨터 생체 검사";
                w.RaiseAlert($"외계 기생체 — {c.Name} ({by}이 찾았다)", c.Room, AlertLevel.Warning, shipWide: true);
                w.History.Add(w, HistoryKind.Incident, $"{c.Name}에게서 외계 기생체가 나왔다 — {by}" + (h.Hiding ? " (숨기고 있었다)" : ""), c.Room);
                var q = w.Ship.KindOf(RoomType.Quarantine).FirstOrDefault() ?? w.Ship.KindOf(RoomType.Medbay).FirstOrDefault();
                w.Automation.Reason($"para:{c.Id}", q != null ? $"{c.Name} 씨를 {q.Name}에 격리합니다 — 같은 방에 오래 있지 마세요" : "격리실이 없다 — 침실 하나를 비워 따로 지낸다", SimTime.Hours(6));
                if (h.Hiding) foreach (var o in w.Crew.Where(o => !o.Dead && o != c && o.AffinityTo(c) > 0.2f)) o.ChangeAffinity(c, -0.05f); // 숨긴 걸 알고 서운하다
            }
        }
        // 번짐: 같은 방에 오래 (격리실이면 거의 없다)
        if (c.Room != null && h.SymptomAt >= 0)
        {
            bool isolated = c.Room.Kind is RoomType.Quarantine or RoomType.QuarantineLock;
            foreach (var o in w.Crew.Where(o => o != c && !o.Dead && o.Room == c.Room && o.Suit == null).ToList())
                if (_rng.Chance(isolated ? 0.003f : h.Found ? 0.015f : 0.04f)) { Infect(o, $"{c.Name}에게서"); w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.IGa(o.Name)} {c.Name} 곁에 있다가 옮았다"); }
        }
        // 치료
        if (CureReady)
        {
            c.Ailments.RemoveAll(a => a.Id == "parasite");
            Hosts.Remove(h); Cured++;
            w.Log.Add(w.Tick, LogKind.Life, $"{Ko.IGa(c.Name)} 기생체 치료를 받았다 — 몸 밖으로 녹아 나왔다", c.Id);
            Life.Diary(w, c, "치료가 끝났다. 처음으로 따뜻하다.");
            return;
        }
        // 너무 오래 — 빠져나와 환풍구로
        if (!Loose && h.SymptomAt >= 0 && w.Tick - h.SymptomAt > SimTime.Hours(96))
        {
            Loose = true; Escapes++;
            LooseRoom = c.Room?.Id ?? -1;
            c.Ailments.RemoveAll(a => a.Id == "parasite");
            c.Vitals.Injury = MathF.Max(c.Vitals.Injury, 0.4f); c.Vitals.InjuryCause = "기생체가 빠져나간 상처";
            Hosts.Remove(h);
            w.RaiseAlert("기생체가 몸 밖으로 빠져나와 환풍구에 숨었다 — 로봇이 수색한다", c.Room, AlertLevel.Critical, shipWide: true);
            w.History.Add(w, HistoryKind.Incident, $"{c.Name}의 몸에서 기생체가 빠져나와 환풍구로 숨었다", c.Room);
        }
    }

    private void Research()
    {
        var w = _w;
        if (CureReady) return;
        var doc = w.Crew.Where(o => !o.Dead && o.CanAct && !Hosts.Any(h => h.Crew == o.Id)).OrderByDescending(o => o.RawSkill(Skill.Medicine)).ThenBy(o => o.Id).FirstOrDefault();
        float lab = w.Ship.KindOf(RoomType.Lab).Any() || w.Ship.KindOf(RoomType.ElectronicsLab).Any() ? 1.5f : 1f;
        float bot = w.Robots.Robots.Any(r => r.Kind == RobotKind.Medic && r.Operational) ? 0.02f : 0f;
        Cure = MathF.Min(1f, Cure + ((doc?.RawSkill(Skill.Medicine) ?? 0f) * 0.035f + bot + 0.005f) * lab);
        if (Cure >= 1f)
        {
            CureReady = true;
            w.Log.Add(w.Tick, LogKind.Ship, $"치료법을 찾았다 — {doc?.Name ?? "의사 로봇"} (실험 {(lab > 1f ? "실험실" : "의무실")})");
            w.History.Add(w, HistoryKind.Incident, $"외계 기생체 치료법 — {doc?.Name ?? "의사 로봇"}");
        }
    }

    private void Hunt()
    {
        var w = _w;
        // 숨어 다니며 갉는다
        if (_rng.Chance(0.25f))
        {
            var rooms = w.Ship.Rooms.Where(r => !r.Detached && r.Type != RoomType.Corridor).ToList();
            var r = rooms[_rng.Range(0, rooms.Count)];
            LooseRoom = r.Id;
            string? what = _rng.Chance(0.5f) ? w.Hazards.FireStory("InsulationCrack", r) : w.Hazards.FireStory("WeevilSwarm", r);
            Gnawed++;
            w.Log.Add(w.Tick, LogKind.Warning, $"{r.Name}에서 무언가 갉았다" + (what != null ? $" — {what}" : ""));
        }
        // 로봇 수색: 방재 · 청소 로봇이 환풍구를 훑는다
        int hunters = w.Robots.Robots.Count(r => r.Operational && r.Kind is RobotKind.Safety or RobotKind.Sentry or RobotKind.Cleaner or RobotKind.Ventman);
        if (_rng.Chance(MathF.Min(0.5f, 0.06f + 0.06f * hunters)))
        {
            Loose = false; Caught++;
            var room = w.Ship.Rooms.FirstOrDefault(r => r.Id == LooseRoom);
            w.Log.Add(w.Tick, LogKind.Work, $"로봇이 {room?.Name ?? "환풍구"}에서 기생체를 붙잡았다 — 격리 용기에 넣었다");
            w.History.Add(w, HistoryKind.Incident, "환풍구의 기생체를 붙잡았다", room);
        }
    }

    public string? Line() => !Active ? null : Loose ? "기생체가 환풍구에 숨어 있다 — 수색 중" : $"기생체 숙주 {Hosts.Count} (찾음 {Hosts.Count(h => h.Found)}) · 치료법 {Cure * 100:0}%";

    public void Hash(Action<long> I, Action<float> F) { I(Hosts.Count); F(Cure); I(Loose ? 1 : 0); }
}
