using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v18.15 딜레마가 올라와서 정해지기까지.
//  올라옴: 배의 사정(공기 · 열 · 신호 · 해적 · 화물칸 · 기항지 · 실수 · 컴퓨터 불신 …)이 그 딜레마를 만든다 — 한 번에 하나.
//  급한 일: 선장이 몇 사람에게 묻고(듣는 선장은 셋, 밀어붙이는 선장은 하나) · 주 컴퓨터의 셈을 듣고 · 자기 가치관대로 정한다.
//          선장이 없거나 움직일 수 없으면 권한이 있는 주 컴퓨터가 자기 성격대로, 그도 아니면 가장 믿음직한 사람이 정한다.
//  급하지 않은 일: 긴급 회의(v18.18 안건)로 — 사람마다 가치관대로 의견이 갈리고 · 컴퓨터는 기록과 셈만 말하고 · 표결한다.
//  정한 뒤: 실제로 배가 바뀌고(신호를 끊는다 · 사람을 내준다 · 배급 방침 · 물을 나눈다 …) · 사람마다 반응하고 · 결정 장부에 적힌다.

public sealed class Dilemma
{
    public int Id { get; init; }
    public DilemmaSpec Spec { get; init; } = null!;
    public long Raised { get; init; }
    public int Subject { get; init; } = -1;
    public int Room { get; init; } = -1;
    public string Detail { get; init; } = "";
    public int Extra { get; set; }
    /// <summary>0 정하는 중 · 1 회의에 올라감 · 2 정함 · 3 흐지부지.</summary>
    public int Stage { get; set; }
    public int Motion { get; set; } = -1;
    public bool? ChoseA { get; set; }
    public int By { get; set; } = -2;
    public long DecidedAt { get; set; } = -1;
    public int Verdict { get; set; } = -1;
    public List<(int who, float s, string why)> Voices { get; } = new();
    public string Computer { get; set; } = "";
    public int ComputerSign { get; set; }
    public string Choice => ChoseA is bool a ? (a ? Spec.A : Spec.B) : "";
}

public sealed partial class ValueSystem
{
    public List<Dilemma> Dilemmas { get; } = new();
    private readonly Dictionary<int, int> _motionOf = new();
    private readonly SortedDictionary<string, long> _lastKey = new();
    private readonly SortedDictionary<int, int> _mistakes = new();
    private readonly List<int> _dropAtPort = new();
    private int _nextDilemma = 1;
    private long _lastRaised = -1_000_000, _signalSeen = -1;
    private int _portsSeen = -1;
    private LegKind _legSeen = LegKind.Cruise;

    public Dilemma? Get(int id) { foreach (var d in Dilemmas) if (d.Id == id) return d; return null; }
    public Dilemma? Open => Dilemmas.LastOrDefault(d => d.Stage < 2);
    public Dilemma? OfMotion(Motion m) => _motionOf.TryGetValue(m.Id, out var id) ? Get(id) : null;
    public bool DropAtPort(CrewMember c) => _dropAtPort.Contains(c.Id);

    // ───────────────────────────── 올라오기 ─────────────────────────────

    /// <summary>딜레마를 올린다 (사정이 만들었거나 · 시험이 꾸민 장면).</summary>
    public Dilemma? Raise(string key, int subject = -1, int room = -1, string detail = "", int extra = 0)
    {
        var w = _w;
        if (DilemmaTable.Get(key) is not DilemmaSpec spec) return null;
        var d = new Dilemma { Id = _nextDilemma++, Spec = spec, Raised = w.Tick, Subject = subject, Room = room, Detail = detail, Extra = extra };
        Dilemmas.Add(d);
        if (Dilemmas.Count > 40) Dilemmas.RemoveAt(Dilemmas.FindIndex(x => x.Stage >= 2) is int i && i >= 0 ? i : 0);
        _lastKey[key] = w.Tick;
        _lastRaised = w.Tick;
        Stats.Dilemmas++;
        var (ct, cs) = Advise(d);
        d.Computer = ct; d.ComputerSign = cs;
        string head = detail != "" ? $"{spec.Name} — {detail}" : spec.Name;
        w.Log.Add(w.Tick, LogKind.Ship, head, subject);
        w.History.Add(w, HistoryKind.Decision, $"정해야 할 일 — {head}", room >= 0 ? w.Ship.Rooms.FirstOrDefault(r => r.Id == room) : null,
            P(subject) is CrewMember sj ? new[] { sj } : null);
        // 사람들이 그 이야기를 한다 (가장 강하게 생각하는 두 사람 — 서로 반대쪽)
        var ad = w.Crew.Where(c => Adult(c) && c.CanAct && c.IsAwake).Select(c => (c, s: Stance(c, d))).ToList();
        if (ad.Count > 0)
        {
            var pro = ad.OrderByDescending(x => x.s.s).ThenBy(x => x.c.Id).First();
            var con = ad.OrderBy(x => x.s.s).ThenBy(x => x.c.Id).First();
            if (pro.s.s > 0.2f) w.Log.Add(w.Tick, LogKind.Life, Persona.Say(pro.c, $"{spec.A} — {pro.s.why}"), pro.c.Id);
            if (con.c != pro.c && con.s.s < -0.2f) w.Log.Add(w.Tick, LogKind.Life, Persona.Say(con.c, $"{spec.B} — {con.s.why}"), con.c.Id);
        }
        if (!spec.Urgent && !MotionSystem.Off) ToCouncil(d);
        return d;
    }

    private bool Cool(string key, float days) => !_lastKey.TryGetValue(key, out var t) || _w.Tick - t > SimTime.TicksPerDay * days;

    /// <summary>두 시간마다: 배의 사정이 딜레마를 만든다 (한 번에 하나 · 하루에 하나 남짓).</summary>
    private void Triggers()
    {
        var w = _w;
        if (Open != null || Crisis.Acting(w)) return;
        bool calm = w.Tick >= SimTime.TicksPerDay * 3;
        // 실수 장부 (정비 실수를 덮을지)
        foreach (var c in w.Crew)
        {
            if (c.Dead) continue;
            int was = _mistakes.TryGetValue(c.Id, out var mm) ? mm : c.Stats.Mistakes;
            _mistakes[c.Id] = c.Stats.Mistakes;
            if (calm && c.Stats.Mistakes > was && Adult(c) && Cool("cover_up", 8f) && w.Tick - _lastRaised > SimTime.TicksPerDay && R.Chance(0.3f))
            { Raise("cover_up", c.Id, c.Room?.Id ?? -1, $"{Ko.IGa(c.Name)} 정비하다 실수했다"); return; }
        }
        if (!calm || w.Tick - _lastRaised < SimTime.TicksPerDay) return;
        var specs = DilemmaTable.All;
        int start = (int)(w.Tick / SimTime.Hours(2) % specs.Length);
        for (int k = 0; k < specs.Length; k++)
        {
            var s = specs[(start + k) % specs.Length];
            if (!Cool(s.Key, 8f)) continue;
            if (Ready(s) is not { } ctx) continue;
            Raise(s.Key, ctx.subj, ctx.room, ctx.detail, ctx.extra);
            return;
        }
    }

    private List<CrewMember> Adults() => _w.Crew.Where(c => Adult(c) && c.CanAct).ToList();

    /// <summary>이 딜레마가 지금 생길 사정인가 (걸린 사람 · 방 · 사정 한 줄).</summary>
    private (int subj, int room, string detail, int extra)? Ready(DilemmaSpec s)
    {
        var w = _w;
        var leg = w.Voyage.Current.Kind;
        var ad = Adults();
        if (ad.Count < 3) return null;
        CrewMember? Pick(IEnumerable<CrewMember> src) { var l = src.OrderBy(c => c.Id).ToList(); return l.Count == 0 ? null : l[R.Range(0, l.Count)]; }
        switch (s.Key)
        {
            case "air_short":
            {
                if (w.Air.Reserve > w.Air.ReserveCapacity * 0.35f) return null;
                var hurt = w.Crew.FirstOrDefault(c => !c.Dead && !c.Away && (c.Vitals.Injury > 0.2f || c.IsChild));
                return hurt == null ? null : (hurt.Id, -1, $"공기 탱크가 {w.Air.Reserve / w.Air.ReserveCapacity * 100:0}%다", 0);
            }
            case "quarantine":
            {
                var sick = w.Crew.FirstOrDefault(c => !c.Dead && !c.Away && c.InfectedAt >= 0 && !c.Immune);
                return sick == null ? null : (sick.Id, sick.Room?.Id ?? -1, $"{Ko.IGa(sick.Name)} 열이 난다", 0);
            }
            case "pirate":
            {
                if (leg is not (LegKind.PatrolLane or LegKind.Narrows or LegKind.TradeLane) || !R.Chance(0.05f)) return null;
                var cap = w.Command.Captain;
                var who = ad.FirstOrDefault(c => c.Joined?.Contains("화물칸") == true) ?? Pick(ad.Where(c => c != cap && c.Background is Background.Police or Background.Lawyer or Background.Reporter or Background.Soldier))
                          ?? Pick(ad.Where(c => c != cap));
                return who == null ? null : (who.Id, -1, $"{Ko.EulReul(who.Name)} 이름으로 찾는다 — 넘기지 않으면 쏘겠다고 한다", 0);
            }
            case "left_behind":
            {
                var down = w.Crew.FirstOrDefault(c => !c.Dead && !c.Away && c.Outside && c.Down);
                return down == null ? null : (down.Id, -1, $"{Ko.IGa(down.Name)} 선체 밖에 쓰러져 있다", 0);
            }
            case "experiment":
            {
                if (!R.Chance(0.03f)) return null;
                var sci = Pick(ad.Where(c => c.Background is Background.Chemist or Background.Physicist or Background.Roboticist or Background.FarmResearcher));
                return sci == null ? null : (sci.Id, -1, $"{Ko.IGa(sci.Name)} 해 보고 싶은 실험이 있다 — 잘되면 배가 나아지고 잘못되면 불꽃이 튄다", 0);
            }
            case "trust_machine":
                if (!w.Automation.Present || w.Automation.Trusts.Spread() < 0.3f || !R.Chance(0.06f)) return null;
                return (-1, -1, "컴퓨터는 이쪽, 현장 사람들은 저쪽이라 한다", 0);
            case "ration_who":
            {
                if (FoodPolicy.FoodDays(w) > 3f || w.Policies["rations"] is 1 or 2) return null;
                var weak = w.Crew.FirstOrDefault(c => !c.Dead && !c.Away && (c.Vitals.Injury > 0.25f || c.InfectedAt >= 0));
                return weak == null ? null : (weak.Id, -1, $"먹을 것이 {FoodPolicy.FoodDays(w):0.#}일치 남았다", 0);
            }
            case "thief_mercy":
            {
                var th = w.Motions.Thefts.LastOrDefault(t => t.Noticed && !t.Accused && w.Tick - t.Tick < SimTime.TicksPerDay * 2);
                if (th == null || P(th.Thief) is not CrewMember tf || tf.Dead || !R.Chance(0.3f)) return null;
                return (tf.Id, th.RoomId, $"{Ko.IGa(tf.Name)} {th.Item}{(Ko.EulReul(th.Item).EndsWith("을") ? "을" : "를")} 몰래 꺼냈다", 0);
            }
            case "seal_door":
                foreach (var r in w.Ship.Rooms)
                {
                    if (r.Detached || w.Fire.CountIn(r) == 0) continue;
                    var inside = w.Crew.FirstOrDefault(c => !c.Dead && !c.Away && c.Room == r);
                    if (inside != null && w.Crew.Any(c => Adult(c) && c.Room != r)) return (inside.Id, r.Id, $"{r.Name}에 불이 번지는데 {Ko.IGa(inside.Name)} 아직 안에 있다", 0);
                }
                return null;
            case "jettison_keepsake":
            {
                if (w.Propulsion.Capacity <= 0f || w.Propulsion.Propellant > w.Propulsion.Capacity * 0.15f || !R.Chance(0.1f)) return null;
                var own = Pick(ad);
                return own == null ? null : (own.Id, -1, $"추진제가 바닥이다 — {Ko.IGa(own.Name)} 싣고 온 짐이 제일 무겁다", 0);
            }
            case "wreck_relics":
                if (leg is not (LegKind.Derelict or LegKind.Graveyard) || !R.Chance(0.3f)) return null;
                return (-1, -1, "난파선 선실에 이름표가 붙은 짐이 그대로 있다", 0);
            case "bad_news":
                if (!(FoodPolicy.FoodDays(w) < 2f || w.Air.Reserve < w.Air.ReserveCapacity * 0.2f || w.Water.Level < w.Water.Capacity * 0.15f) || !R.Chance(0.25f)) return null;
                return (-1, -1, "버틸 날이 생각보다 짧다", 0);
            case "scarce_medicine":
            {
                if (w.Ship.CountStored(ItemKind.MedKit) > 2) return null;
                var hurt = w.Crew.FirstOrDefault(c => !c.Dead && !c.Away && c.Vitals.Injury > 0.45f);
                return hurt == null ? null : (hurt.Id, -1, $"구급함이 {w.Ship.CountStored(ItemKind.MedKit)}개 남았다 — {Ko.IGa(hurt.Name)} 많이 아프다", 0);
            }
            case "night_repair":
            {
                float h = SimTime.HourOfDay(w.Tick);
                if (h is > 4f and < 22f || ad.Average(c => c.Needs.Rest) > 0.45f || !R.Chance(0.04f)) return null;
                return (-1, -1, "고장 난 설비가 아침까지 버틸지 모른다", 0);
            }
            case "refugees":
                if (leg != LegKind.Port || w.Crew.Count(c => !c.Dead) >= World.MaxCrew - 1 || !R.Chance(0.25f)) return null;
                return (-1, -1, $"{w.Voyage.Current.Name} 부두에서 만난 가족이다", 0);
            case "smuggler_report":
                if (leg is not (LegKind.Port or LegKind.TradeLane) || !R.Chance(0.06f)) return null;
                return (-1, -1, "옆에 댄 배의 짐칸에서 금지된 물건을 봤다", 0);
            case "share_water":
                if (leg is not (LegKind.TradeLane or LegKind.Cruise) || w.Water.Level < w.Water.Capacity * 0.5f || !R.Chance(0.03f)) return null;
                return (-1, -1, "옆을 지나는 작은 배의 물탱크가 깨졌다", 0);
            case "mutineer":
            {
                var cap = w.Command.Captain;
                var plot = cap == null ? null : w.Schemes.All.FirstOrDefault(x => x.Spec.Key == "mutiny_plot" && x.Knows.ContainsKey(cap.Id) && x.Lead != cap.Id && w.Tick - x.Born < SimTime.TicksPerDay * 6);
                return plot == null || P(plot.Lead) is not CrewMember ld || ld.Dead ? null : (ld.Id, -1, $"{Ko.IGa(ld.Name)} 함교 열쇠를 노렸다는 말이 돈다", 0);
            }
            case "turn_back":
            {
                if (leg == LegKind.Port) return null;
                var bad = w.Crew.FirstOrDefault(c => !c.Dead && !c.Away && c.Vitals.Health < 0.35f && c.Vitals.Injury > 0.55f);
                return bad == null || !R.Chance(0.4f) ? null : (bad.Id, -1, $"{Ko.IGa(bad.Name)} 의무실에서 버티기 어렵다", 0);
            }
            case "leash_computer":
                if (!w.Automation.Present || w.Automation.Trusts.Average() > 0.35f || w.Policies["computerask"] == 2 || !R.Chance(0.2f)) return null;
                return (-1, -1, "요즘 컴퓨터의 자동 조치가 자주 틀렸다", 0);
        }
        return null;
    }

    /// <summary>열 분마다: 신호 · 기항지 · 정하는 중인 딜레마.</summary>
    private void RunDilemmas()
    {
        var w = _w;
        // 구조 신호: 배 사정이 빠듯하면 딜레마가 된다 (방침이 '무조건'이면 묻지 않고 간다)
        if (w.Comms.SignalOpen && w.Comms.SignalAt != _signalSeen)
        {
            _signalSeen = w.Comms.SignalAt;
            if (w.Policies["rescue"] != 0 && Pressure() is string why)
                Raise("distress", -1, w.Sensors.CommsRoom?.Id ?? -1, $"탈출 캡슐 · 생존자 {w.Comms.SignalSurvivors}명 · {why}", w.Comms.SignalSurvivors);
        }
        // 기항지: 내리기로 한 사람이 내린다 · 떠날 때 화물칸에 숨어 탄 사람
        if (_portsSeen < 0) _portsSeen = w.Voyage.PortsVisited;
        if (w.Voyage.PortsVisited != _portsSeen) { _portsSeen = w.Voyage.PortsVisited; OnPort(); }
        var leg = w.Voyage.Current.Kind;
        if (_legSeen == LegKind.Port && leg != LegKind.Port && Open == null && w.Crew.Count(c => !c.Dead) < World.MaxCrew && w.Tick >= SimTime.TicksPerDay * 3 && Cool("stowaway", 10f) && R.Chance(0.3f))
            Stowaway();
        _legSeen = leg;
        foreach (var d in Dilemmas)
        {
            if (d.Stage >= 2) continue;
            if (d.Stage == 0 && w.Tick - d.Raised >= SimTime.Minutes(20)) DecideNow(d);
            else if (d.Stage == 1)
            {
                var m = d.Motion >= 0 ? w.Motions.Get(d.Motion) : null;
                if (m == null || m.Stage == MotionStage.Dropped || w.Tick - d.Raised > SimTime.TicksPerDay * 2 && m.Stage != MotionStage.Sitting) DecideNow(d);
            }
        }
    }

    /// <summary>구조 신호를 받았을 때 배가 빠듯한 까닭.</summary>
    private string? Pressure()
    {
        var w = _w;
        float days = FoodPolicy.FoodDays(w);
        if (days < 3f) return $"먹을 것이 {days:0.#}일치";
        if (w.Propulsion.Propellant < w.Propulsion.EvadeCost * 4f) return $"추진제가 {w.Propulsion.Propellant:0}kg뿐이다";
        if (w.Crew.Any(c => !c.Dead && c.Vitals.Injury > 0.6f)) return "의무실에 위중한 사람이 있다";
        if (w.Policies["rescue"] == 2) return "건질 가망이 낮다";
        return null;
    }

    /// <summary>기항지를 떠날 때 화물칸에서 사람이 나온다.</summary>
    public Dilemma? Stowaway()
    {
        var w = _w;
        var hold = w.Ship.Rooms.Where(r => !r.Detached && r.Type is RoomType.Storage or RoomType.Cargo).OrderBy(r => r.Id).FirstOrDefault()
                   ?? w.Ship.Rooms.Where(r => !r.Detached && r.Type == RoomType.Airlock).OrderBy(r => r.Id).FirstOrDefault();
        var cell = hold?.Cells.FirstOrDefault(w.Ship.IsOpenFloor);
        if (hold == null || cell == null) return null;
        var c = w.AddSurvivor(cell.Value);
        c.Vitals.Injury = 0f; c.Vitals.Wounds.Clear(); c.Vitals.Health = 0.85f; c.Vitals.InjuryCause = null;
        c.Needs.Food = 0.25f; c.Needs.Rest = 0.5f; c.Needs.Stress = 0.6f;
        c.Joined = "화물칸에 숨어 탔다";
        _nests.Add((hold.Id, cell.Value, w.Tick));
        if (_nests.Count > 6) _nests.RemoveAt(0);
        w.History.Add(w, HistoryKind.Milestone, $"화물칸 짐 사이에서 {Ko.IGa(c.Name)} 나왔다 — 기항지에서 몰래 탔다", hold, new[] { c }, log: true);
        return Raise("stowaway", c.Id, hold.Id, $"{c.Name} · 짐 사이에 담요와 빈 깡통", 0);
    }

    /// <summary>밀항자가 숨어 지낸 자리 (그림).</summary>
    private readonly List<(int room, Cell cell, long tick)> _nests = new();
    public IReadOnlyList<(int room, Cell cell, long tick)> Nests => _nests;

    // ───────────────────────────── 의견 ─────────────────────────────

    /// <summary>이 사람이 A 쪽을 얼마나 바라나 (+ A · − B) 와 그 사람의 말.</summary>
    public (float s, string why) Stance(CrewMember c, Dilemma d)
    {
        var w = _w;
        var spec = d.Spec;
        float s = Lean(c, spec.Vec);
        string? own = null;
        if (P(d.Subject) is CrewMember sj && spec.Side != 0)
        {
            if (c == sj) { s += spec.Side * 1.2f; own = spec.Side > 0 ? "나를 살려 달라" : "나를 내놓지 마라"; }
            else if (MathF.Abs(c.AffinityTo(sj)) > 0.2f)
            {
                s += spec.Side * 0.6f * c.AffinityTo(sj);
                if (c.AffinityTo(sj) > 0.35f) own = spec.Side > 0 ? $"{Ko.EunNeun(sj.Name)} 내 사람이다" : $"{Ko.EulReul(sj.Name)} 내줄 수는 없다";
            }
        }
        switch (spec.Key)
        {
            case "trust_machine": case "leash_computer":
            {
                float f = w.Automation.Present ? w.Automation.Trusts.Of(c) : 0.5f;
                float k = spec.Key == "trust_machine" ? 1f : -1f;
                s += k * 1.1f * (f - 0.5f);
                if (MathF.Abs(f - 0.5f) > 0.2f) own = f > 0.5f ? "컴퓨터 덕에 산 적이 있다" : "컴퓨터가 틀리는 걸 봤다";
                break;
            }
            case "air_short" when c.Vitals.Injury > 0.2f: s += 0.4f; own = "숨이 가쁜 게 어떤 건지 안다"; break;
            case "air_short" when c.Role is CrewRole.Engineer or CrewRole.Technician or CrewRole.Electrician: s -= 0.15f; break;
            case "quarantine" when c.Fears.Contains(Fear.Disease): s += 0.35f; own = "병이 옮는 게 무섭다"; break;
            case "quarantine" when c.Role == CrewRole.Medic: s -= 0.2f; own = "돌보는 건 내 일이다"; break;
            case "ration_who" when c.Needs.Food < 0.3f: s -= 0.3f; own = "나도 배가 고프다"; break;
            case "distress" when w.Comms.Rescued > 0 || c.Rescued: s += 0.3f; own = "나도 캡슐에서 건져졌다"; break;
            case "stowaway" when c.Rescued: s += 0.3f; own = "나도 남의 배에 얹혀 왔다"; break;
            case "scarce_medicine" when c.Role == CrewRole.Medic: s += 0.15f; break;
            case "night_repair" when c.Needs.Rest < 0.3f: s -= 0.35f; own = "눈이 감긴다"; break;
            case "share_water" when w.Water.Level < w.Water.Capacity * 0.6f: s -= 0.2f; break;
            case "pirate" when c.Traits.Bravery < 0.35f: s += 0.25f; own = "총 앞에서 버틸 수는 없다"; break;
        }
        // 선장 · 컴퓨터를 어떻게 보는지 (컴퓨터가 권한 쪽)
        if (d.ComputerSign != 0 && w.Automation.Present) s += 0.25f * d.ComputerSign * (w.Automation.Trusts.Of(c) - 0.45f);
        if (own == null)
        {
            var (ax, plus, wgt) = Strongest(c, spec.Vec);
            bool agreesA = s > 0f;
            own = wgt > 0.25f && (c.Id + d.Id) % 2 == 0 ? (agreesA ? spec.WhyA : spec.WhyB) : Voice(ax, plus);
        }
        return (Math.Clamp(s, -1.5f, 1.5f), own);
    }

    /// <summary>주 컴퓨터의 셈과 권하는 쪽 (+1 A · −1 B · 0 말을 아낀다).</summary>
    private (string text, int sign) Advise(Dilemma d)
    {
        var w = _w;
        var a = w.Automation;
        if (!a.Present || !a.CoreOnline) return ("", 0);
        var ch = a.Character;
        var mine = new[] { 0.15f + 0.7f * ch.Caution, 0.3f, 0.5f, 0.1f + 0.8f * ch.PeopleTilt };
        float s = 0f, n = 0f;
        for (int i = 0; i < 4; i++) { s += mine[i] * d.Spec.Vec[i]; n += MathF.Abs(d.Spec.Vec[i]); }
        s = n > 0f ? s / n : 0f;
        string fact;
        switch (d.Spec.Key)
        {
            case "distress":
            {
                float cost = w.Propulsion.EvadeCost * 2f, after = w.Propulsion.Propellant - cost;
                fact = $"배를 돌리면 추진제 {cost:0}kg · 남는 추진제 {after:0}kg · 식량 {FoodPolicy.FoodDays(w):0.#}일치";
                if (after < cost) s -= 0.6f;
                break;
            }
            case "air_short":
                fact = $"공기 탱크 {w.Air.Reserve / MathF.Max(1f, w.Air.ReserveCapacity) * 100:0}% · 다친 사람 {w.Crew.Count(c => !c.Dead && c.Vitals.Injury > 0.2f)}명";
                break;
            case "quarantine":
                fact = "같은 방을 쓰면 옮을 가능성이 큽니다"; s += 0.3f;
                break;
            case "stowaway":
                fact = $"한 사람이 늘면 식량이 {FoodPolicy.FoodDays(w) / MathF.Max(1, w.Crew.Count(c => !c.Dead)):0.#}일치 줄어듭니다";
                break;
            case "pirate":
                fact = "상대 배의 무장이 우리 선체보다 셉니다"; s += 0.2f;
                break;
            case "trust_machine":
                fact = "제 셈이 맞을 가능성은 열에 여섯입니다"; s += 0.3f;
                break;
            case "leash_computer":
                fact = "최근 제 판단이 여러 번 틀렸습니다 — 정하시는 대로 따르겠습니다"; s = 0f;
                break;
            case "experiment":
                fact = "성공할 가능성은 반을 조금 넘습니다";
                break;
            case "cover_up":
                fact = "정비 기록은 지워지지 않습니다 — 기항지 검사에서 다시 읽힙니다"; s -= 0.3f;
                break;
            default:
                fact = d.Detail != "" ? d.Detail : d.Spec.Name;
                break;
        }
        int sign = MathF.Abs(s) < 0.08f ? 0 : MathF.Sign(s);
        string rec = sign > 0 ? $" — {d.Spec.A} 쪽을 권합니다" : sign < 0 ? $" — {d.Spec.B} 쪽을 권합니다" : " — 정하시는 대로 따르겠습니다";
        return (fact + rec, sign);
    }

    // ───────────────────────────── 정하기 ─────────────────────────────

    /// <summary>긴급 회의에 올린다 (가장 A 쪽으로 기운 사람이 낸다 · 배 전체 일이라 서명은 바로 찬다).</summary>
    private void ToCouncil(Dilemma d)
    {
        var w = _w;
        var ad = w.Crew.Where(c => Adult(c) && c.CanAct).ToList();
        if (ad.Count < 3) return;
        var prop = ad.Select(c => (c, s: Stance(c, d).s)).OrderByDescending(x => x.s).ThenBy(x => x.c.Id).First().c;
        var m = w.Motions.Propose(prop, MotionKind.Crisis, SittingKind.Emergency, $"{d.Spec.Name} — {d.Spec.A}", d.Detail != "" ? $"{d.Detail} — {Stance(prop, d).why}" : Stance(prop, d).why);
        m.Need = 1;
        w.Motions.Cosign(m, prop.Id);
        _motionOf[m.Id] = d.Id;
        d.Motion = m.Id;
        d.Stage = 1;
    }

    /// <summary>바로 정한다: 선장 → (권한이 있으면) 주 컴퓨터 → 가장 믿음직한 사람.</summary>
    private void DecideNow(Dilemma d)
    {
        var w = _w;
        var spec = d.Spec;
        var cap = w.Command.Captain;
        var a = w.Automation;
        bool compOk = a.Present && a.CoreOnline;
        if (cap != null && cap.CanAct && cap.Id != d.Subject)
        {
            // 선장: 몇 사람에게 묻는다 (듣는 선장은 셋 · 컴퓨터가 귀띔했으면 더 무겁게)
            int ask = w.Command.Style switch { CaptainStyle.Authoritarian => 1, CaptainStyle.Laissez => 4, _ => 3 };
            var near = w.Crew.Where(c => Adult(c) && c.CanAct && c != cap).OrderByDescending(c => c.Room == cap.Room ? 1 : 0).ThenByDescending(c => cap.AffinityTo(c)).ThenBy(c => c.Id).Take(ask).ToList();
            float heard = 0f;
            foreach (var v in near)
            {
                var (vs, vw) = Stance(v, d);
                d.Voices.Add((v.Id, vs, vw));
                heard += vs;
                w.Log.Add(w.Tick, LogKind.Life, Persona.Say(v, $"{(vs >= 0f ? spec.A : spec.B)} — {vw}"), v.Id);
            }
            float own = Stance(cap, d).s;
            float wHeard = (w.Command.Style == CaptainStyle.Authoritarian ? 0.15f : w.Command.Style == CaptainStyle.Laissez ? 0.6f : 0.35f) + (Listening ? 0.2f : 0f);
            float score = own + wHeard * (near.Count > 0 ? heard / near.Count : 0f) + (compOk ? 0.3f * d.ComputerSign * (a.Trusts.Of(cap) - 0.3f) : 0f);
            if (compOk && d.Computer != "") w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: {a.Manner.Speak(d.Computer)}");
            Decide(d, score >= 0f, cap.Id, near.Select(x => x.Id).ToList());
            return;
        }
        if (compOk && w.Policies["computerask"] == 0)
        {
            Decide(d, d.ComputerSign > 0 || d.ComputerSign == 0 && spec.Vec[3] > 0f, -1, null);
            return;
        }
        var acting = w.Crew.Where(c => Adult(c) && c.CanAct && c.Id != d.Subject).OrderByDescending(CommandSystem.Leadership).ThenBy(c => c.Id).FirstOrDefault();
        if (acting == null) { d.Stage = 3; return; }
        Decide(d, Stance(acting, d).s >= 0f, acting.Id, null);
    }

    /// <summary>정했다: 배를 바꾸고 · 사람마다 반응하고 · 장부에 적는다.</summary>
    private void Decide(Dilemma d, bool chooseA, int by, List<int>? voters, List<int>? forIds = null, List<int>? againstIds = null)
    {
        var w = _w;
        var spec = d.Spec;
        d.ChoseA = chooseA;
        d.By = by;
        d.Stage = 2;
        d.DecidedAt = w.Tick;
        if (by == -1) Stats.ByComputer++; else if (by == -2) Stats.ByCouncil++; else Stats.ByCaptain++;
        string who = ByName(by);
        string choice = d.Choice;
        var a = w.Automation;
        if (by == -1)
            w.Log.Add(w.Tick, LogKind.Ship, $"{a.Voice.Call}: {a.Manner.Speak($"{spec.Name} — {choice}. {d.Computer}")}");
        string split = d.Voices.Count > 0 ? $" (물어본 사람 {d.Voices.Count}명 중 {d.Voices.Count(v => v.s >= 0f == chooseA)}명이 같은 쪽)" : "";
        w.History.Add(w, HistoryKind.Decision, $"{who} — {spec.Name}: {choice}{split}", d.Room >= 0 ? w.Ship.Rooms.FirstOrDefault(r => r.Id == d.Room) : null,
            P(by) is CrewMember dc ? new[] { dc } : null, log: true);
        Apply(d, chooseA, by, forIds);
        var vec = chooseA ? spec.Vec : spec.Vec.Select(x => -x).ToArray();
        var v = Book(d, forIds ?? w.Crew.Where(c => Adult(c) && Stance(c, d).s >= 0f == chooseA).Select(c => c.Id).ToList(),
            againstIds ?? w.Crew.Where(c => Adult(c) && Stance(c, d).s >= 0f != chooseA).Select(c => c.Id).ToList());
        React(v.Id, by, $"{spec.Name.Split(" — ")[0]} 때 '{choice}'", vec, 1f, d.Subject, spec.Side * (chooseA ? 1 : -1), voters ?? d.Voices.Select(x => x.who).ToList());
        // 정한 사람의 가치관도 아주 조금 그쪽으로 굳는다
        if (P(by) is CrewMember dec) for (int i = 0; i < 4; i++) if (MathF.Abs(vec[i]) > 0.5f) Shift(dec, (Axis)i, 0.02f * MathF.Sign(vec[i]), $"{spec.Name}에서 {choice}");
        if (by == -1 && a.Present) a.Character.Nudge(0f, spec.Vec[3] * (chooseA ? 0.02f : -0.02f), $"{spec.Name} — {choice}");
    }

    /// <summary>정한 일이 배를 실제로 바꾼다.</summary>
    private void Apply(Dilemma d, bool A, int by, List<int>? forIds)
    {
        var w = _w;
        var sj = P(d.Subject);
        var dec = P(by);
        var emo = w.Brain2.Emotions;
        switch (d.Spec.Key)
        {
            case "air_short":
                foreach (var c in w.Crew.Where(Adult))
                {
                    bool weak = c.Vitals.Injury > 0.2f;
                    if (A && !weak && c.Role is CrewRole.Engineer or CrewRole.Technician or CrewRole.Electrician) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.05f);
                    if (!A && weak) { c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.08f); c.Vitals.Health = MathF.Max(0.1f, c.Vitals.Health - 0.02f); }
                }
                if (A && sj != null) sj.ExcusedUntil = Math.Max(sj.ExcusedUntil, w.Tick + SimTime.TicksPerDay);
                break;
            case "quarantine" when sj != null:
                if (A)
                {
                    sj.ExcusedUntil = Math.Max(sj.ExcusedUntil, w.Tick + SimTime.TicksPerDay);
                    emo.Feel(sj, Feeling.Sadness, 0.15f, "혼자 의무실에 있다");
                    Life.Diary(w, sj, Persona.Say(sj, "의무실에 혼자 있다. 문틈으로 밥이 들어온다."));
                }
                else if (w.Crew.Where(c => Adult(c) && c != sj).OrderByDescending(c => c.AffinityTo(sj)).ThenBy(c => c.Id).FirstOrDefault() is CrewMember nurse)
                {
                    w.Relations.Remember(sj, nurse, RelationReason.NursedMe, "열이 날 때 곁에 있어 줬다");
                    nurse.Needs.Stress = MathF.Min(1f, nurse.Needs.Stress + 0.03f);
                }
                break;
            case "distress":
                if (!A && w.Comms.SignalOpen) { w.Comms.SignalUntil = w.Tick; d.Extra = Math.Max(d.Extra, w.Comms.SignalSurvivors); }
                break;
            case "pirate" when sj != null:
                if (A) Depart(sj, "무장한 배에 넘겨졌다");
                else
                {
                    Life.Take(w, ItemKind.Ration, 4); Life.Take(w, ItemKind.Electronics, 2);
                    w.Voyage.Credits = MathF.Max(0f, w.Voyage.Credits - MathF.Min(15f, w.Voyage.Credits * 0.4f));
                    w.Log.Add(w.Tick, LogKind.Warning, "무장한 배가 짐칸을 털어 갔다 — 비상식량 · 전자 부품 · 돈");
                    Relations(sj, forIds, RelationReason.SavedMe, "총 앞에서 나를 내주지 않았다");
                }
                break;
            case "stowaway" when sj != null:
                if (A) { sj.Joined = "화물칸에 숨어 탔다 — 함께 가기로 했다"; Relations(sj, forIds, RelationReason.BackedMe, "나를 태우자고 했다"); }
                else { _dropAtPort.Add(sj.Id); sj.Joined = "화물칸에 숨어 탔다 — 다음 기항지까지"; }
                // 반대한 사람은 밀항자에게 서먹하다 (회의 쪽이 '반대편에 섰다'를 남긴다)
                foreach (var c in w.Crew.Where(c => Adult(c) && c != sj))
                    if (Stance(c, d).s < -0.2f) sj.ChangeAffinity(c, -0.06f); else if (Stance(c, d).s > 0.2f) sj.ChangeAffinity(c, 0.08f);
                break;
            case "left_behind" when sj != null:
                if (A && w.Crew.Where(c => Adult(c) && c.CanAct && c != sj && !c.Outside).OrderByDescending(c => c.Traits.Bravery).ThenBy(c => c.Id).FirstOrDefault() is CrewMember hero)
                {
                    hero.Needs.Stress = MathF.Min(1f, hero.Needs.Stress + 0.08f);
                    w.Relations.Remember(sj, hero, RelationReason.SavedMe, "밖에 쓰러진 나를 데리러 나왔다");
                    if (R.Chance(0.25f)) { hero.Vitals.Injury = MathF.Min(1f, hero.Vitals.Injury + 0.1f); hero.Vitals.InjuryCause ??= "쓰러진 동료를 데리러 나갔다 다쳤다"; }
                }
                else if (!A && dec != null) w.Relations.Remember(sj, dec, RelationReason.AbandonedMe, "밖에 쓰러진 나를 두고 문을 닫았다");
                break;
            case "cover_up" when sj != null:
                if (A && dec != null) w.Relations.Remember(sj, dec, RelationReason.CoveredMyMistake, "실수를 기록에서 덮어 줬다");
                else { sj.Needs.Stress = MathF.Min(1f, sj.Needs.Stress + 0.08f); emo.Feel(sj, Feeling.Shame, 0.15f, "실수가 기록에 남았다"); }
                break;
            case "ration_who":
                _skipPolicy = $"rations:{(A ? 2 : 1)}";
                w.Policies.Set("rations", A ? 2 : 1, $"{d.Spec.Name} — {d.Choice}");
                break;
            case "thief_mercy" when sj != null:
                if (A) { sj.Needs.Food = MathF.Max(0f, sj.Needs.Food - 0.1f); MindSystem.Anger(sj, 0.1f); }
                else Relations(sj, forIds, RelationReason.ForgaveMe, "나를 용서하자고 했다");
                break;
            case "jettison_keepsake" when sj != null:
                if (A) { emo.Feel(sj, Feeling.Sadness, 0.2f, "싣고 온 짐을 버렸다"); Life.Diary(w, sj, Persona.Say(sj, "집에서 가져온 상자를 에어락 밖으로 내보냈다.")); }
                else Life.Take(w, ItemKind.Ration, 2);
                break;
            case "wreck_relics":
                if (A) { VoyageV15.Put(w, ItemKind.Electronics, 2); VoyageV15.Put(w, ItemKind.Plate, 2); }
                break;
            case "bad_news":
                if (A) foreach (var c in w.Crew.Where(Adult)) c.Needs.Stress = MathF.Min(1f, c.Needs.Stress + 0.04f);
                break;
            case "scarce_medicine" when sj != null:
                if (A && Life.Take(w, ItemKind.MedKit, 1)) sj.Vitals.Health = MathF.Min(sj.Vitals.MaxHealth, sj.Vitals.Health + 0.1f);
                else sj.Needs.Stress = MathF.Min(1f, sj.Needs.Stress + 0.05f);
                break;
            case "night_repair":
                if (A) foreach (var c in w.Crew.Where(c => Adult(c) && c.Role is CrewRole.Engineer or CrewRole.Technician or CrewRole.Electrician)) c.Needs.Rest = MathF.Max(0f, c.Needs.Rest - 0.15f);
                break;
            case "refugees":
                if (A)
                {
                    var dock = w.Comms.Airlock?.Cells.FirstOrDefault(w.Ship.IsOpenFloor);
                    for (int i = 0; i < 2 && dock is Cell dc && w.Crew.Count(c => !c.Dead) < World.MaxCrew; i++)
                    {
                        var nc = w.AddSurvivor(dc);
                        nc.Vitals.Injury = 0f; nc.Vitals.Wounds.Clear(); nc.Vitals.Health = 1f; nc.Vitals.InjuryCause = null;
                        nc.Joined = $"{w.Voyage.Current.Name}에서 가족과 함께 탔다";
                    }
                }
                break;
            case "smuggler_report":
                if (A) w.Voyage.Credits += 6f;
                break;
            case "share_water":
                if (A) w.Water.Level = MathF.Max(0f, w.Water.Level - w.Water.Capacity * 0.12f);
                break;
            case "mutineer" when sj != null:
                if (A)
                {
                    sj.ExcusedUntil = Math.Max(sj.ExcusedUntil, w.Tick + SimTime.TicksPerDay * 2);
                    MindSystem.Anger(sj, 0.15f);
                    if (w.Command.Captain is CrewMember cp) w.Relations.Remember(sj, cp, RelationReason.BlamedMe, "나를 근무에서 뺐다");
                }
                else { var o = Of(sj); o.Captain = Math.Clamp(o.Captain + 0.35f, -1f, 1f); o.Loyalty = MathF.Min(1f, o.Loyalty + 0.12f); }
                break;
            case "turn_back" when sj != null:
                if (A) w.Voyage.Shift(-0.4f);
                else sj.Needs.Stress = MathF.Min(1f, sj.Needs.Stress + 0.1f);
                break;
            case "leash_computer":
                _skipPolicy = $"computerask:{(A ? 2 : 0)}";
                w.Policies.Set("computerask", A ? 2 : 0, $"{d.Spec.Name} — {d.Choice}");
                if (A && w.Automation.Present) w.Automation.Character.Nudge(0.1f, 0f, "맡은 일이 줄었다 — 더 조심하겠다");
                break;
            case "leave_wish" when sj != null:
            {
                var o = Of(sj);
                if (A) { o.LeaveAsked = -1; o.LeaveWhy = null; o.Captain = Math.Clamp(o.Captain - 0.15f, -1f, 1f); o.Loyalty = MathF.Min(1f, o.Loyalty + 0.15f); Life.Diary(w, sj, Persona.Say(sj, "다들 붙잡았다. 한 번만 더 가 보기로 했다.")); }
                else Life.Diary(w, sj, Persona.Say(sj, "다들 보내 주기로 했다. 고맙고, 조금 서운하다."));
                break;
            }
        }
    }

    private void Relations(CrewMember about, List<int>? ids, RelationReason why, string text)
    {
        if (ids == null) return;
        foreach (var id in ids) if (P(id) is CrewMember c && c != about && !c.Dead) _w.Relations.Remember(about, c, why, text);
    }

    /// <summary>하선 요청을 선장 앞에 올린다 (붙잡을지 보내 줄지).</summary>
    private void RaiseLeave(CrewMember c)
    {
        if (Open != null) return;
        Raise("leave_wish", c.Id, c.Room?.Id ?? -1, $"{Ko.IGa(c.Name)} 다음 기항지에서 내리겠다고 한다");
    }

    /// <summary>배에서 내린다 (기항지 · 넘겨짐) — 배에 없는 사람이 된다.</summary>
    internal void Depart(CrewMember c, string why)
    {
        var w = _w;
        if (c.Dead || c.Away) return;
        c.Interrupt(w);
        c.Away = true;
        c.Position = new System.Numerics.Vector2(-90f - c.Id * 1.5f, -90f);
        c.PreviousPosition = c.Position;
        c.Room = null;
        c.Path = null;
        c.Destination = null;
        c.TalkingTo = null;
        var o = Of(c);
        o.Left = true;
        Stats.Departed++;
        _dropAtPort.Remove(c.Id);
        w.History.Add(w, HistoryKind.Milestone, $"{Ko.IGa(c.Name)} 배를 떠났다 — {why}", null, new[] { c }, log: true);
        foreach (var f in w.Crew.Where(x => Adult(x) && x.AffinityTo(c) > 0.35f))
        {
            w.Brain2.Emotions.Feel(f, Feeling.Sadness, 0.15f, $"{c.Name}이 떠났다", c);
            Life.Diary(w, f, Persona.Say(f, $"{Ko.IGa(c.Name)} 떠났다. 자리가 비었다."));
        }
    }

    /// <summary>기항지에 닿았다: 내려놓기로 한 사람 · 내리겠다던 사람.</summary>
    private void OnPort()
    {
        var w = _w;
        foreach (var id in _dropAtPort.ToList()) if (P(id) is CrewMember c) Depart(c, $"{w.Voyage.Current.Name}에 내려놓았다");
        _dropAtPort.Clear();
        foreach (var c in w.Crew.Where(Adult).ToList())
        {
            if (Peek(c) is not Outlook o || o.LeaveAsked < 0 || o.Left) continue;
            if (o.Loyalty > 0.35f || o.Captain > 0f)
            {
                o.LeaveAsked = -1;
                Life.Diary(w, c, Persona.Say(c, "기항지에 닿았지만 내리지 않았다. 아직 할 일이 있다."));
                w.Log.Add(w.Tick, LogKind.Life, "내리겠다던 말을 거뒀다", c.Id);
            }
            else Depart(c, $"{w.Voyage.Current.Name}에서 내렸다 — {o.LeaveWhy}");
        }
    }

    // ───────────────────────────── 회의 쪽 훅 ─────────────────────────────

    /// <summary>회의 의견 (v18.18 Opinion에서 부른다): 딜레마 안건 · 선장 불신임.</summary>
    public (float v, string why)? Opinion(CrewMember c, Motion m)
    {
        if (Off) return null;
        var w = _w;
        if (m.Kind == MotionKind.Confidence && Peek(c) is Outlook o && MathF.Abs(o.Captain) > 0.1f && c.Id != m.Target)
        {
            var last = o.Recent.LastOrDefault(r => r.By == m.Target);
            string why = o.Captain < 0f ? (last is { Liked: false } ? $"{last.Title} — 그렇게 정한 사람이다" : "요즘 정하는 일마다 마음에 안 든다")
                                        : (last is { Liked: true } ? $"{last.Title} — 잘 정했다" : "요즘 잘 이끌고 있다");
            return (-0.7f * o.Captain, why);
        }
        if (OfMotion(m) is not Dilemma d) return null;
        var (s, ow) = Stance(c, d);
        return (0.9f * s, ow);
    }

    public string? Effect(Motion m, bool pass) => !Off && OfMotion(m) is Dilemma d ? (pass ? d.Spec.A : d.Spec.B) : null;

    public (string text, int sign)? Advice(Motion m) => !Off && OfMotion(m) is Dilemma d && d.Computer != "" ? (d.Computer, d.ComputerSign) : null;

    public void Decided(Motion m, bool pass, List<CrewMember> yes, List<CrewMember> no)
    {
        if (Off || OfMotion(m) is not Dilemma d || d.Stage >= 2) return;
        foreach (var kv in m.Final) d.Voices.Add((kv.Key, kv.Value, ""));
        var forIds = (pass ? yes : no).Select(c => c.Id).ToList();
        var against = (pass ? no : yes).Select(c => c.Id).ToList();
        Decide(d, pass, -2, yes.Concat(no).Select(c => c.Id).ToList(), forIds, against);
    }
}
