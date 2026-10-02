using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.24 심한 상처는 그냥 두면 나빠진다 (점검 항해: 보통 재해로 아무도 안 다치고 안 죽었다).
//   출혈 — 베이고 · 찢기고 · 부러진 큰 상처는 누가 눌러 주거나 치료할 때까지 피가 난다 (작은 상처는 저절로 멎는다)
//   화상 쇼크 — 넓게 덴 사람은 진물이 빠져 천천히 기운을 잃는다 (식히고 감싸 주면 멎는다)
//   심정지 — 센 감전은 가끔 심장을 세운다: 곧바로 쓰러지고, 몇 분 안에 누가 가슴을 누르지 않으면 죽는다
//   불붙는 순간 — 불이 일어난 자리 곁에 있던 사람은 덴다 (잠든 사람은 피하지 못한다)
// 사람: 다친 사람은 스스로 누르고 · 곁의 사람이 눌러 주고 · 가슴을 누른다 (혼자 · 잠든 시간 · 모두 바쁜 위기에는 늦는다)
// 주 컴퓨터: 데이터선이 닿는 방이면 생체 신호로 알아채 가장 가까운 사람을 부른다 (멎었거나 선이 끊긴 방은 못 본다)
public enum TraumaKind : byte { Bleed, BurnShock, Arrest }

public sealed class Trauma
{
    public int Id { get; init; }
    public int CrewId { get; init; }
    public TraumaKind Kind { get; init; }
    public float Rate { get; set; }           // 시간당 체력이 빠지는 양
    public long Since { get; init; }
    public string Cause { get; init; } = "";
    public int RoomId { get; init; } = -1;
    public bool Asleep { get; init; }         // 다칠 때 자고 있었다
    public bool SelfPressed { get; set; }
    public float PressMul { get; set; } = 1f; // 스스로 누른 만큼 (정신을 잃으면 1로)
    public long HelperSince { get; set; } = -1;
    public int Helper { get; set; } = -1;
    public bool Paged { get; set; }           // 컴퓨터가 불렀다
    public int Tries { get; set; }            // 가슴 압박 시도
    public bool Closed { get; set; }
    public string Outcome { get; set; } = "";
}

public sealed class CasualtySystem
{
    private readonly World _w;
    private Rng? _rng;
    private Rng R => _rng ??= new Rng(unchecked(_w.Seed * 7457 + 1693));
    private readonly Dictionary<int, float> _inj = new();
    private readonly HashSet<Cell> _fires = new();
    private int _next = 1;

    public List<Trauma> Open { get; } = new();
    public List<Trauma> Done { get; } = new();
    public int Bleeds, Shocks, Arrests, Flashes, Stopped, Revived, Died, Paged, SelfAid, Pressed;

    public CasualtySystem(World w) => _w = w;

    public Trauma? Of(CrewMember c) => Open.FirstOrDefault(t => t.CrewId == c.Id);

    /// <summary>지금 피가 나거나 심장이 멎었다 (치료 · 구조가 급하다).</summary>
    public bool Urgent(CrewMember c) => !c.Dead && Open.Any(t => t.CrewId == c.Id && (t.Kind == TraumaKind.Arrest || t.Rate >= 0.06f));

    /// <summary>죽은 까닭 (World.Die 훅): 상처가 아니라 그 뒤 — "파편 · 출혈".</summary>
    public string? DeathCause(CrewMember c) => Of(c) is Trauma t ? $"{t.Cause} · {KindWord(t.Kind)}" : null;

    public static string KindWord(TraumaKind k) => k switch { TraumaKind.Bleed => "출혈", TraumaKind.BurnShock => "화상 쇼크", _ => "심정지" };

    public void Update(float dt)
    {
        var w = _w;
        Ignitions();
        foreach (var c in w.Crew)
        {
            if (c.Dead) { _inj.Remove(c.Id); continue; }
            float inj = c.Vitals.Injury;
            float last = _inj.TryGetValue(c.Id, out var l) ? l : inj;
            _inj[c.Id] = inj;
            if (inj - last >= 0.12f && !c.Away && c.CareBed == null) Wounded(c, inj - last);
        }
        if (Open.Count == 0) return;
        for (int i = Open.Count - 1; i >= 0; i--)
        {
            var t = Open[i];
            var c = w.Crew.FirstOrDefault(x => x.Id == t.CrewId);
            if (c == null) { Close(t, "사라짐"); continue; }
            if (c.Dead) { Died++; Close(t, "숨졌다"); Mourn(c, t); continue; }
            Tend(c, t, dt);
        }
    }

    /// <summary>시험 · 장면: 이 상처 뒤를 바로 연다.</summary>
    public void Inflict(CrewMember c, TraumaKind kind, float rate, string cause) => Start(c, kind, rate, cause);

    // ───────────── 다쳤다: 어떤 상처인가 ─────────────

    private void Wounded(CrewMember c, float hit)
    {
        var w = _w;
        string cause = c.Vitals.InjuryCause ?? "사고";
        if (Wounds.Classify(cause, hit, 0) is not (BodyPart part, WoundKind kind)) return;
        bool shock = cause.Contains("감전") || cause.Contains("누전");
        if (Of(c) is Trauma old && old.Kind == TraumaKind.Arrest) return;
        if (shock)
        {
            Shocks++;
            // 센 감전은 가끔 심장을 세운다 — 젖은 몸 · 다친 몸 · 나이 든 몸은 더
            float p = Math.Clamp((hit - 0.1f) * 2f, 0f, 0.4f) * (1f + 0.6f * c.Vitals.Frailty + (c.Room?.Humidity > 0.8f ? 0.3f : 0f));
            if (R.Chance(p)) { Start(c, TraumaKind.Arrest, 0.6f, cause); return; }
            if (hit < 0.2f) return;
            Start(c, TraumaKind.BurnShock, 0.22f * (hit - 0.1f), cause);
            return;
        }
        switch (kind)
        {
            case WoundKind.Cut or WoundKind.Crush or WoundKind.Fracture:
                if (hit < 0.14f) return;
                Start(c, TraumaKind.Bleed, 0.9f * (hit - 0.12f) * (part == BodyPart.Head ? 1.3f : 1f), cause);
                break;
            case WoundKind.Burn:
                if (hit < 0.18f) return;
                Start(c, TraumaKind.BurnShock, 0.3f * (hit - 0.1f), cause);
                break;
            case WoundKind.Barotrauma:
                Start(c, TraumaKind.Bleed, 0.25f * hit, cause); // 폐가 상했다 — 숨이 차고 피를 토한다
                break;
        }
    }

    private void Start(CrewMember c, TraumaKind kind, float rate, string cause)
    {
        var w = _w;
        if (Of(c) is Trauma old) { old.Rate = MathF.Max(old.Rate, rate) + 0.3f * MathF.Min(old.Rate, rate); return; } // 또 다쳤다 — 더 빨리
        var t = new Trauma { Id = _next++, CrewId = c.Id, Kind = kind, Rate = rate, Since = w.Tick, Cause = cause, RoomId = c.Room?.Id ?? -1, Asleep = c.Pose == Pose.Sleeping };
        Open.Add(t);
        string where = c.Room?.Name ?? (c.Outside ? "선체 밖" : "?");
        switch (kind)
        {
            case TraumaKind.Bleed:
                Bleeds++;
                c.Say(w, Persona.Say(c, rate > 0.15f ? "피가… 안 멈춰" : "베였다 — 피가 나"));
                w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.IGa(c.Name)} 피를 흘린다 — {cause} ({where})", c.Id);
                break;
            case TraumaKind.BurnShock:
                c.Say(w, Persona.Say(c, "살이… 타는 것 같아"));
                w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.IGa(c.Name)} 넓게 데었다 — {cause} ({where})", c.Id);
                break;
            case TraumaKind.Arrest:
                Arrests++;
                c.Vitals.Health = MathF.Min(c.Vitals.Health, 0.1f); // 그 자리에서 쓰러진다
                w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.IGa(c.Name)} {cause} 뒤 쓰러졌다 — 숨을 안 쉰다 ({where})", c.Id);
                MarkLog.Add(c.Memory.Marks, w.Tick, $"{cause} — 심장이 멎었다");
                break;
        }
        w.Board.RequestScan();
        foreach (var o in w.Crew)
            if (o != c && o.CanAct && o.Room == c.Room && o.Room != null && o.NextThinkTick > w.Tick + 1) o.NextThinkTick = w.Tick + 1; // 곁의 사람이 본다
    }

    // ───────────── 돌본다: 누르기 · 가슴 압박 · 치료 · 컴퓨터 ─────────────

    private void Tend(CrewMember c, Trauma t, float dt)
    {
        var w = _w;
        long now = w.Tick;
        float min = (now - t.Since) / (float)SimTime.TicksPerHour * 60f;
        // 치료받았다 · 치료 침대 · 업혀 가는 사람이 누르고 있다
        if (c.Vitals.TreatedTick >= t.Since && t.Kind != TraumaKind.Arrest) { Stop(c, t, "치료로 멎었다", null); return; }
        if (c.CareBed?.Machine is Machine bed && bed.Efficiency > 0f) { if (t.Kind == TraumaKind.Arrest) Revived++; Stop(c, t, t.Kind == TraumaKind.Arrest ? "치료 침대가 심장을 다시 뛰게 했다" : "치료 침대에서 멎었다", null); return; }
        // 걸을 수 있으면 의무실까지 가서 스스로 감싼다 (걷지 못하는 사람 · 갇힌 사람 · 멀리 있는 사람은 못 한다)
        if (t.Kind != TraumaKind.Arrest && c.CanAct && c.Room is { Type: RoomType.Medbay } && c.Job?.Activity is RecoverActivity && c.Pose == Pose.Sleeping) { SelfAid++; Stop(c, t, "의무실까지 걸어가 스스로 감쌌다", null); return; }
        var helper = Helper(c);
        if (helper != null)
        {
            if (t.Helper != helper.Id) { t.Helper = helper.Id; t.HelperSince = now; }
        }
        else { t.Helper = -1; t.HelperSince = -1; }
        float helped = t.HelperSince >= 0 ? (now - t.HelperSince) / (float)SimTime.TicksPerHour * 60f : 0f;

        if (t.Kind == TraumaKind.Arrest)
        {
            if (!w.CrewCanDie && min > 20f) { Stop(c, t, "심장이 저절로 다시 뛰었다", null); return; }
            if (helper != null && helped >= 1f && (now - t.Since) % SimTime.Minutes(1) < World.SystemInterval)
            {
                t.Tries++;
                float p = MathF.Max(0.04f, 0.55f - 0.05f * min) * (helper.Role == CrewRole.Medic ? 1.4f : 1f) * (0.7f + 0.3f * helper.SkillLevel(Skill.Medicine) + 0.3f);
                if (R.Chance(p)) { Revived++; Stop(c, t, $"{Ko.IGa(helper.Name)} 가슴을 눌러 심장을 다시 뛰게 했다", helper); return; }
                if (t.Tries == 1) helper.Say(w, Persona.Say(helper, "돌아와 — 하나, 둘, 셋…"));
            }
            c.Vitals.Health = MathF.Max(0f, c.Vitals.Health - t.Rate * dt);
            if (!w.CrewCanDie) c.Vitals.Health = MathF.Max(0.02f, c.Vitals.Health);
        }
        else
        {
            // 스스로 누른다 (정신이 있으면 · 몇 분 뒤) — 의료를 아는 사람은 제대로 묶는다
            if (!t.SelfPressed && c.CanAct && c.Pose != Pose.Sleeping && min >= 3f && t.Kind == TraumaKind.Bleed)
            {
                t.SelfPressed = true;
                SelfAid++;
                t.PressMul = c.SkillLevel(Skill.Medicine) >= 0.5f || c.Role == CrewRole.Medic ? 0.3f : 0.6f;
                t.Rate *= t.PressMul;
                c.Say(w, Persona.Say(c, "꽉 누르고 있자…"));
            }
            // 정신을 잃으면 누르던 손이 풀린다 — 다시 쏟아진다
            else if (t.SelfPressed && t.PressMul < 1f && c.Down) { t.Rate /= t.PressMul; t.PressMul = 1f; }
            // 곁의 사람이 눌러 준다 · 식혀서 감싼다 (2분 남짓)
            if (helper != null && helped >= 2f)
            {
                Pressed++;
                Stop(c, t, t.Kind == TraumaKind.Bleed ? $"{Ko.IGa(helper.Name)} 상처를 눌러 피를 멎게 했다" : $"{Ko.IGa(helper.Name)} 덴 곳을 식혀 감쌌다", helper);
                return;
            }
            // 작은 출혈은 저절로 멎는다 · 화상 쇼크는 천천히 준다
            float clot = t.Kind == TraumaKind.Bleed ? (t.Rate < 0.08f ? 1.0f : 0.1f) : 0.12f;
            t.Rate *= MathF.Exp(-clot * dt);
            if (t.Rate < 0.015f) { Stop(c, t, t.Kind == TraumaKind.Bleed ? "피가 저절로 멎었다" : "고비를 넘겼다", null); return; }
            c.Vitals.Health -= t.Rate * dt;
            if (!w.CrewCanDie) c.Vitals.Health = MathF.Max(0.02f, c.Vitals.Health);
        }
        Page(c, t, min);
    }

    /// <summary>곁에서 돌볼 수 있는 사람 (깨어 있고 · 손이 비었거나 이 사람을 돌보러 왔고 · 우주복이 아니어도 된다).</summary>
    private CrewMember? Helper(CrewMember c)
    {
        if (c.CarriedBy is CrewMember carrier && carrier.CanAct) return carrier;
        CrewMember? best = null;
        float bd = 2.3f * 2.3f;
        foreach (var o in _w.Crew)
        {
            if (o == c || !o.CanAct || o.Outside != c.Outside || o.Pose == Pose.Sleeping) continue;
            if (o.Room != c.Room && !(c.Room == null && o.Room == null)) continue;
            if (o.Job?.Urgent == true && o.Job.Target?.Room != c.Room && o.Job.Order?.Target.Crew != c) continue; // 제 급한 일로 지나가는 사람은 멈추지 않는다
            float d = (o.Position - c.Position).LengthSquared();
            if (d < bd) { bd = d; best = o; }
        }
        return best;
    }

    /// <summary>주 컴퓨터: 생체 신호로 알아채고 가장 가까운 사람을 부른다.</summary>
    private void Page(CrewMember c, Trauma t, float min)
    {
        var w = _w;
        if (t.Paged || t.Helper >= 0 || min < 1f) return;
        var a = w.Automation;
        if (!a.Present || !a.MainOnline || c.Room is not Room room || !room.DataLinked || !room.Powered) return;
        var near = w.Crew.Where(o => o != c && o.CanAct && !o.Outside && o.Room != null && o.Pose != Pose.Sleeping)
            .OrderBy(o => (o.Position - c.Position).LengthSquared()).ThenBy(o => o.Id).FirstOrDefault();
        t.Paged = true;
        Paged++;
        string what = t.Kind switch { TraumaKind.Arrest => "심장이 멎었습니다", TraumaKind.Bleed => "피를 많이 흘립니다", _ => "넓게 데었습니다" };
        a.Speak.Announce(a.Voice.Style($"{room.Name} — {Ko.IGa(c.Name)} {what}" + (near != null ? $". {near.Name}, 가장 가깝습니다 — 가 주세요" : "")), room, 1);
        a.Book.Add(ActKind.Shed, null, $"{c.Name} 생체 신호 — {KindWord(t.Kind)} ({t.Cause})", near != null ? $"가장 가까운 {Ko.EulReul(near.Name)} 불렀다" : "부를 사람이 없다",
            "방송으로 불렀다", "", "page", SimTime.Minutes(30), 20f,
            (world, act) => world.Crew.FirstOrDefault(x => x.Id == t.CrewId) is { Dead: false } ? (1, "맞았다 — 살았다") : (-1, "늦었다 — 숨졌다"));
        if (near != null) near.NextThinkTick = Math.Min(near.NextThinkTick, w.Tick + 1);
        w.Board.RequestScan();
    }

    private void Stop(CrewMember c, Trauma t, string how, CrewMember? by)
    {
        var w = _w;
        Stopped++;
        Close(t, how);
        w.Log.Add(w.Tick, LogKind.Life, $"{c.Name}: {how}", c.Id);
        if (by != null)
        {
            c.ChangeAffinity(by, 0.08f);
            by.ChangeAffinity(c, 0.04f);
            MarkLog.Add(c.Memory.Marks, w.Tick, $"{Ko.IGa(by.Name)} 살렸다 ({t.Cause} · {KindWord(t.Kind)})");
            MarkLog.Add(by.Memory.Marks, w.Tick, $"{Ko.EulReul(c.Name)} 붙잡고 있었다 ({KindWord(t.Kind)})");
            if (t.Kind == TraumaKind.Arrest) w.History.Add(w, HistoryKind.Response, $"{Ko.IGa(by.Name)} {c.Name}의 멎은 심장을 다시 뛰게 했다 ({t.Cause})", c.Room, new[] { by, c });
        }
    }

    private void Close(Trauma t, string outcome)
    {
        t.Closed = true;
        t.Outcome = outcome;
        Open.Remove(t);
        Done.Add(t);
        if (Done.Count > 40) Done.RemoveAt(0);
    }

    /// <summary>상처 뒤에 숨졌다 — 왜 늦었는지 남긴다 (혼자 · 잠든 시간 · 컴퓨터가 못 봤다 · 모두 바빴다).</summary>
    private void Mourn(CrewMember c, Trauma t)
    {
        var w = _w;
        var room = t.RoomId >= 0 && t.RoomId < w.Ship.Rooms.Count ? w.Ship.Rooms[t.RoomId] : c.Room;
        int awake = w.Crew.Count(o => !o.Dead && o != c && o.Pose != Pose.Sleeping && o.CanAct);
        string why = t.Asleep ? "자다가 다쳐 아무도 몰랐다"
            : !t.Paged && t.Helper < 0 && (room == null || !room.DataLinked || !w.Automation.MainOnline) ? "혼자였고, 주 컴퓨터도 보지 못했다"
            : awake <= 1 ? "깨어 있는 사람이 없었다"
            : Crisis.Level(w) >= CrisisLevel.Alert ? "모두 다른 불을 끄고 있었다"
            : "아무도 제때 닿지 못했다";
        w.History.Add(w, HistoryKind.Death, $"{Ko.IGa(c.Name)} {t.Cause} 뒤 {KindWord(t.Kind)}로 숨졌다 — {why}", room, new[] { c });
        if (room != null) MarkLog.Add(room.Marks, w.Tick, $"{c.Name}: {KindWord(t.Kind)} — {why}");
        foreach (var o in w.Crew)
            if (!o.Dead && o != c && o.AffinityTo(c) > 0.3f) MarkLog.Add(o.Memory.Marks, w.Tick, $"{c.Name} — {why}");
    }

    // ───────────── 불붙는 순간 곁에 있던 사람 ─────────────

    private void Ignitions()
    {
        var w = _w;
        var fires = w.Fire.Fires;
        if (fires.Count == 0) { _fires.Clear(); return; }
        foreach (var (cell, inten) in fires)
        {
            if (_fires.Contains(cell)) continue;
            bool spread = Cell.Dirs4.Any(d => _fires.Contains(cell + d));
            foreach (var c in w.Crew)
            {
                if (c.Dead || c.Outside || c.Room == null) continue;
                float d2 = (c.Position - cell.Center).LengthSquared();
                if (d2 > 1.3f * 1.3f) continue;
                bool asleep = c.Pose == Pose.Sleeping;
                // 옷 · 머리카락에 옮겨붙는다 — 깨어 있으면 물러서고, 우주복은 막는다
                float burn = R.Range(0.08f, 0.24f) * (spread ? 0.6f : 1f) * (asleep ? 1.6f : 1f) * (c.Suit != null ? 0.25f : 1f) * (1.2f - 0.4f * d2 / (1.3f * 1.3f));
                if (burn < 0.04f) continue;
                Flashes++;
                c.Vitals.Health = MathF.Max(0.02f, c.Vitals.Health - burn);
                NeedsSystem.AddInjury(c.Vitals, burn, asleep ? "자다가 불에 덴 화상" : "불이 옮겨붙은 화상");
                c.Interrupt(w);
                MarkLog.Add(c.Memory.Marks, w.Tick, asleep ? $"자다가 불에 데었다 ({c.Room.Name})" : $"불이 옮겨붙었다 ({c.Room.Name})");
                Memory.Frighten(w, c, c.Room, 0.3f, "불이 옮겨붙었다");
                w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.IGa(c.Name)} {(asleep ? "자다가 " : "")}불에 데었다 ({c.Room.Name} · 체력 {c.Vitals.Health * 100:0}%)", c.Id);
            }
        }
        _fires.Clear();
        foreach (var k in fires.Keys) _fires.Add(k);
    }

    public void Hash(Action<long> I, Action<float> F)
    {
        I(Open.Count);
        foreach (var t in Open) { I(t.CrewId); I((int)t.Kind); F(t.Rate); I(t.Helper); }
        I(Bleeds); I(Arrests); I(Flashes); I(Stopped); I(Revived);
    }
}
