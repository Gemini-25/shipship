namespace ShipEngine;

/// <summary>
/// ⑩ 주컴퓨터 — 조타 (⑤ 바퀴 2 · docs/ENGINE.md ⑤ 기동 · 피하기 · 다른 배).
/// <list type="bullet">
/// <item>정해진 점화(출발 · 고침 · 도착): 3분 전 방송 → 불 · 빠짐 · 쓰러진 사람 · 업고 가는 사람이 있으면 10분씩 미룬다 (늦어도 될 때까지) → 엔진이 돌면 20초에 걸쳐 올린다.
/// 점화 중 엔진이 멈추면 끊고 남은 Δv를 다시 정한다.</item>
/// <item>운석 비키기: 레이더(SensorArray)가 잡는 거리는 레이더 방정식대로 (돌의 단면^¼) · 방향 오차만큼 틀리게 안다 (항법 컴퓨터가 멈추면 네 배).
/// '맞을까'를 외곽 칸으로 셈하고 — 그냥 맞기 · 주엔진(미는 쪽으로만) · 자세 분사기를 견준다 (맞을 칸 곁 사람 · 외판 ↔ 서 있는 사람이 넘어질 몫).</item>
/// <item>다른 배: 신호기 · 레이더로 알아보고 · 지나가는 배와 교신(소식) · 조난선이면 갈지 정한다 (함장 방침 · 도착 몫 추진제) →
/// 맞추는 점화 → 다가감 → 멈추는 점화 → 붙는다 → 생존자가 에어락으로 · 난파선이면 그 배의 공기가 에어락으로 들고 쓸 만한 부품을 건진다.</item>
/// </list>
/// </summary>
public sealed partial class ShipMind
{
    private Outside? _out;
    private int[]? _engineDevs, _radarDevs;
    private int _navDev = -2;
    public const double AnnounceMin = 3, DelayMin = 10, RampSec = 20;
    public long BurnsDone, BurnsDelayed, RocksSeen, DodgesTried, DodgesAvoided, DodgesFailed, BraceCalls, Hails, Rescues, Declined, Salvaged, Rescued;
    public int GoingTo = -1;
    public byte GoPhase;          // 1 맞추는 점화 방송 · 2 맞추는 중 · 3 다가감 · 4 멈추는 점화 방송 · 5 멈춤 · 6 붙음(옮김) · 7 자세 분사기로 다가감
    public long GoAt, GoSince;
    public double GoClose;
    public int GoCause;
    public PlannedBurn Active;
    public byte HasActive;
    private readonly List<int> _rocksKnown = new(), _contactsKnown = new(), _hailed = new(), _braced = new();
    private int _dodgeRock = -1;
    private long _dodgeUntil, _noEngineSaid = long.MinValue / 2;
    public string HelmLine = "";

    private void BindHelm()
    {
        if (_engineDevs != null || _net == null) return;
        _engineDevs = _net.Devices.Where(d => d.Type == "EngineCore").Select(d => d.Id).ToArray();
        _radarDevs = _net.Devices.Where(d => d.Type == "SensorArray").Select(d => d.Id).ToArray();
        _navDev = _net.Devices.FirstOrDefault(d => d.Type == "NavComputer")?.Id ?? -1;
    }

    public int EnginesWorking() { int n = 0; if (_engineDevs != null && _net != null) foreach (int d in _engineDevs) if (_net.Working(d)) n++; return n; }
    public bool RadarOn() { if (_radarDevs != null && _net != null) foreach (int d in _radarDevs) if (_net.Working(d)) return true; return false; }
    private bool NavOk => _navDev >= 0 && _net != null && _net.Working(_navDev);

    public static string BurnName(BurnKind k) => k switch
    {
        BurnKind.Depart => "출발", BurnKind.Correct => "항로 고침", BurnKind.Arrive => "도착", BurnKind.Capture => "붙잡기", BurnKind.Dodge => "비키기",
        BurnKind.Match => "다가가는", BurnKind.Brake => "멈추는", _ => "",
    };

    private void Helm(Sim sim)
    {
        if (_out == null || _net == null || _mind == null) return;
        BindHelm();
        PlannedBurns(sim);
        Dodge(sim);
        ContactsWatch(sim);
        Going(sim);
        var o = _out;
        HelmLine = $"추진제 {o.PropellantKg / Math.Max(1, o.PropellantStart) * 100:0}% (남은 Δv {o.DvLeft / 1000:0.00}km/s) · 엔진 {EnginesWorking()}/{o.Engines}"
            + (o.Burning != BurnKind.None ? $" · {BurnName(o.Burning)} 점화 중 {Math.Sqrt(o.Ax * o.Ax + o.Ay * o.Ay) / Outside.G0:0.00}g" : "")
            + (o.Plan.Count > 0 ? $" · 다음 점화: {BurnName(o.Plan.MinBy(p => p.Due).Kind)} {Time.Clock(o.Plan.Min(p => p.Due))}" : "")
            + (o.Missed != 0 ? " · 도착을 놓쳐 한 바퀴 더" : "")
            + (GoingTo >= 0 && o.ContactIndex(GoingTo) is int gk and >= 0 ? $" · {Ko.Euro(o.ContactName(o.Contacts[gk]))} 가는 중 {o.Contacts[gk].Range / 1000:0.#}km" : "");
    }

    /// <summary>사람이 있는 방마다 방송 (말은 점화 믿음으로 — 들은 이는 붙잡는다).</summary>
    private void AnnounceAll(Sim sim, int subjectTick, int cause, float conf = 1)
    {
        var rooms = new SortedSet<int>();
        foreach (var a in _mind!.Agents) if (a.IsPerson && a.C.Cell >= 0 && L.Room[a.C.Cell] >= 0) rooms.Add(L.Room[a.C.Cell]);
        var b = new Belief { Kind = BeliefKind.Burn, Cell = -1, Subject = subjectTick, Conf = conf, Seen = sim.Tick, Cause = cause };
        foreach (int r in rooms)
            if (r < RoomSeen.Length && _roomVent[r] >= 0 && F.Pressure(_roomVent[r]) > 5)
            {
                b.Cell = _roomVent[r];
                _mind.Announce(sim, _roomVent[r], AlarmDb, b);
            }
    }

    /// <summary>점화를 미룰 까닭: 불 · 빠짐 · 독한 공기 · 쓰러진 사람 · 사람을 업고 가는 이.</summary>
    private string? Busy()
    {
        foreach (var x in Incidents)
            if (x.Active != 0 && x.Kind is IncidentKind.Fire or IncidentKind.Vacuum or IncidentKind.PersonDown or IncidentKind.Injured or IncidentKind.Toxic or IncidentKind.CO)
                return $"{RoomName(x.Room)} {KindName(x.Kind)}";
        foreach (var a in _mind!.Agents) if (a.C.Carrying >= 0) return "사람을 업고 가는 이가 있다";
        return null;
    }

    // ───────────────────────── 정해진 점화 ─────────────────────────

    private void PlannedBurns(Sim sim)
    {
        var o = _out!; long t = sim.Tick;
        if (HasActive != 0)
        {
            if (o.Burning == Active.Kind && o.BurnRcs == 0 && EnginesWorking() == 0) { o.EndBurn(sim); Say(sim, $"엔진이 멈춰 {BurnName(Active.Kind)} 점화가 끊겼다"); }
            if (o.Burning != Active.Kind)
            {
                HasActive = 0;
                double got = o.LastBurnDv, want = Active.Dv;
                if (got < 0.95 * want && o.PropellantKg > 0 && t < Active.Deadline)
                {
                    var rest = Active; rest.Dv = want - got; rest.Due = t + Time.Minutes(DelayMin); rest.Announced = 0;
                    o.AddPlan(rest);
                    Say(sim, $"{BurnName(Active.Kind)} 점화 {got:0.#}/{want:0.#}m/s — 남은 것은 10분 뒤");
                }
                else Say(sim, $"{BurnName(Active.Kind)} 점화를 마쳤다 — {got:0.#}m/s");
            }
        }
        if (o.Burning != BurnKind.None) return;
        for (int k = 0; k < o.Plan.Count; k++)
        {
            var p = o.Plan[k];
            if (p.Announced == 0 && t >= p.Due - Time.Minutes(AnnounceMin))
            {
                p.Announced = 1; o.SetPlan(k, p);
                int eng = Math.Max(1, EnginesWorking());
                double g = o.MaxAccel(eng) / Outside.G0, sec = p.Dv / Math.Max(1e-3, o.MaxAccel(eng));
                Say(sim, $"{Math.Max(0, (p.Due - t) * Time.SecondsPerTick / 60):0}분 뒤 {BurnName(p.Kind)} 점화 ({p.Dv:0.#}m/s · {g:0.00}g · {Math.Max(1, sec / 60):0}분) — 앉거나 붙잡으세요");
                AnnounceAll(sim, (int)p.Due, p.Cause);
            }
            if (t < p.Due) continue;
            string? why = Busy();
            if (why != null && t + Time.Minutes(DelayMin) <= p.Deadline)
            {
                p.Due = t + Time.Minutes(DelayMin); p.Announced = 0; p.Delays++; o.SetPlan(k, p); BurnsDelayed++;
                int dc = sim.Causes.Add(CauseKind.Computer, $"주컴퓨터가 {BurnName(p.Kind)} 점화를 10분 미뤘다 — {why}", t, -1, p.Cause);
                sim.Facts.Emit(t, FactKind.Decision, -1, 0, dc, -2);
                Say(sim, $"{why} — {BurnName(p.Kind)} 점화를 10분 미룬다");
                continue;
            }
            int en = EnginesWorking();
            if (en == 0)
            {
                if (t - _noEngineSaid > Time.Minutes(10)) { _noEngineSaid = t; Say(sim, $"엔진이 멈춰 {BurnName(p.Kind)} 점화를 못 한다 — 고쳐야 한다"); }
                continue;
            }
            int c = sim.Causes.Add(CauseKind.Computer, $"주컴퓨터가 {BurnName(p.Kind)} 점화를 했다 ({p.Dv:0.#}m/s)", t, -1, p.Cause);
            if (o.Burn(sim, p.Kind, o.AxisX, o.AxisY, o.MaxAccel(en), p.Dv, 0, RampSec, false, en, c))
            {
                Active = p; HasActive = 1; o.TakePlan(k); BurnsDone++;
                Say(sim, $"{BurnName(p.Kind)} 점화 — {o.BurnAccel / Outside.G0:0.00}g");
            }
            break;
        }
    }

    // ───────────────────────── 운석 비키기 ─────────────────────────

    /// <summary>서 있는 사람(깨어 있고 · 앉거나 눕지 않은)이 a(m/s²)를 갑자기 받으면 넘어질 몫의 합 — 주컴퓨터의 어림.</summary>
    private double FallCost(double a)
    {
        double sum = 0;
        foreach (var x in _mind!.Agents)
        {
            if (!x.IsPerson || x.C.Up == 0 || x.C.Body < 0 || _life == null) continue;
            var b = _life.Bodies[x.C.Body];
            if (b.Alive == 0 || b.Asleep != 0 || b.Conscious == 0) continue;
            sum += Mind.FallChance(a / Outside.G0);
        }
        return sum;
    }

    private int PeopleNear(int cell, int r)
    {
        if (_life == null || cell < 0) return 0;
        int n = 0, x0 = L.X(cell), y0 = L.Y(cell);
        foreach (var b in _life.Bodies) if (b.Alive != 0 && b.Cell >= 0 && Math.Abs(L.X(b.Cell) - x0) <= r && Math.Abs(L.Y(b.Cell) - y0) <= r) n++;
        return n;
    }

    private void Dodge(Sim sim)
    {
        var o = _out!; long t = sim.Tick; double dt = Time.SecondsPerTick;
        if (_dodgeRock >= 0 && t > _dodgeUntil)
        {
            int rk = o.Rocks.FindIndex(r => r.Id == _dodgeRock);
            if (rk >= 0 && o.Rocks[rk].Mass > 0) { DodgesAvoided++; Say(sim, "운석이 비껴갔다"); }
            else { DodgesFailed++; Say(sim, "비키지 못했다"); }
            _dodgeRock = -1;
        }
        if (Policy.Dodge == 0 || o.Rocks.Count == 0 || !RadarOn() || o.Planet != null) return;
        var M = T.Space.Ship;
        for (int k = 0; k < o.Rocks.Count; k++)
        {
            var r = o.Rocks[k];
            if (r.Mass <= 0) continue;
            if (_sense == null || !_sense.Tracked(TrackKind.Rock, r.Id)) continue;                      // ⑥ 레이더가 잡은 것만 (레이더 방정식 R ∝ σ^¼ — 감지 법칙)
            double R = Math.Sqrt(r.X * r.X + r.Y * r.Y);
            double v2 = r.Vx * r.Vx + r.Vy * r.Vy;
            if (v2 < 1) continue;
            double tc = -(r.X * r.Vx + r.Y * r.Vy) / v2;
            if (tc <= 0) continue;
            double sp = Math.Sqrt(v2), ux = r.Vx / sp, uy = r.Vy / sp, nx = -uy, ny = ux;
            double offTrue = (r.X + r.Vx * tc) * nx + (r.Y + r.Vy * tc) * ny;
            // 레이더의 방향 오차 (거리 × 0.5mrad · 항법 컴퓨터가 멈추면 네 배) — 정규 분포 (박스-뮐러)
            double sig = R * M.RadarMrad * 1e-3 * (NavOk ? 1 : 4);
            double u1 = Math.Max(1e-12, Rng.Unit(sim.Seed, Rng.Key("레이더"), t, r.Id)), u2 = Rng.Unit(sim.Seed, Rng.Key("레이더2"), t, r.Id);
            double offEst = offTrue + sig * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
            bool first = !_rocksKnown.Contains(r.Id);
            int seen = 0;
            if (first)
            {
                _rocksKnown.Add(r.Id); if (_rocksKnown.Count > 64) _rocksKnown.RemoveAt(0); RocksSeen++;
                seen = sim.Causes.Add(CauseKind.Computer, $"레이더가 {r.Mass:0.##}kg 운석을 잡았다 — {tc:0}초 뒤 · {R / 1000:0.#}km", t, -1, _sense.CauseOf(TrackKind.Rock, r.Id));
                sim.Facts.Emit(t, FactKind.Noticed, -1, (float)tc, seen, -2);
            }
            if (_dodgeRock == r.Id) continue;
            int hit = o.WouldHit(ux, uy, -offEst);
            if (hit < 0) continue;                                                                      // 비껴간다고 본다
            double te = Math.Max(0, tc - dt);                                                           // 한 틱 늦게 시작
            int eng = EnginesWorking();
            double aMain = eng > 0 ? o.MaxAccel(eng) : 0, aRcs = o.PropellantKg > 0 ? o.RcsAccel : 0;
            double nAx = o.AxisX * nx + o.AxisY * ny;
            double offMain = offEst - 0.5 * aMain * te * te * nAx;
            double sgn = offEst >= 0 ? 1 : -1;
            double offRcs = offEst + sgn * 0.5 * aRcs * te * te;
            bool Clear(double off, double away) => o.WouldHit(ux, uy, -off) < 0 && o.WouldHit(ux, uy, -(off + 3 * away)) < 0;   // 3m 여유
            bool missMain = aMain > 0 && Math.Abs(nAx) > 0.2 && o.PropellantKg > 0 && Clear(offMain, Math.Sign(offMain - offEst));
            bool missRcs = aRcs > 0 && Clear(offRcs, sgn);
            double hitCost = 3 + 10 * PeopleNear(hit, 5);
            double costMain = aMain > 0 && Math.Abs(nAx) > 0.2 ? FallCost(aMain) + (missMain ? 0 : 0.7 * hitCost) : double.MaxValue;
            double costRcs = aRcs > 0 ? FallCost(aRcs) + (missRcs ? 0 : 0.9 * hitCost) : double.MaxValue;
            if (seen == 0) seen = sim.Causes.Add(CauseKind.Computer, $"레이더: {r.Mass:0.##}kg 운석이 {tc:0}초 뒤 부딪힌다", t, -1, r.Cause);
            if (Math.Min(costMain, costRcs) >= hitCost)
            {
                if (!_braced.Contains(r.Id))
                {
                    _braced.Add(r.Id); if (_braced.Count > 32) _braced.RemoveAt(0); BraceCalls++;
                    Say(sim, $"운석 {tc:0}초 뒤 — 비킬 수 없다 · 충격 대비!");
                    AnnounceAll(sim, (int)t, seen, 0.9f);
                }
                continue;
            }
            bool useMain = costMain <= costRcs;
            int dc = sim.Causes.Add(CauseKind.Computer, $"주컴퓨터가 운석을 비키려 {(useMain ? "주엔진" : "자세 분사기")}을 켰다 ({(useMain ? aMain : aRcs) / Outside.G0:0.00}g)", t, -1, seen);
            bool ok = useMain
                ? o.Burn(sim, BurnKind.Dodge, o.AxisX, o.AxisY, aMain, 0, te + dt, 0, false, eng, dc)
                : o.Burn(sim, BurnKind.Dodge, -sgn * nx, -sgn * ny, aRcs, 0, te + dt, 0, true, 0, dc);
            if (!ok) continue;
            if (HasActive != 0) { var rest = Active; rest.Due = t + Time.Minutes(DelayMin); rest.Announced = 0; HasActive = 0; o.AddPlan(rest); } // 하던 점화는 다시
            _dodgeRock = r.Id; _dodgeUntil = t + (long)Math.Ceiling(tc / dt) + 1; DodgesTried++;
            sim.Facts.Emit(t, FactKind.Decision, -1, 1, dc, -2);
            Say(sim, $"운석 {tc:0}초 뒤 — 비킨다! ({(useMain ? "주엔진" : "자세 분사기")} {(useMain ? aMain : aRcs) / Outside.G0:0.00}g)");
            AnnounceAll(sim, (int)t, dc, 0.9f);
            break;
        }
    }

    // ───────────────────────── 다른 배 ─────────────────────────

    private void ContactsWatch(Sim sim)
    {
        var o = _out!; long t = sim.Tick;
        for (int k = 0; k < o.Contacts.Count; k++)
        {
            var c = o.Contacts[k];
            if (c.State == ContactState.Gone) continue;
            if (_sense == null || !_sense.Tracked(TrackKind.Contact, c.Id)) continue;   // ⑥ 신호 · 레이더로 잡은 배만
            string name = o.ContactName(c);
            if (!_contactsKnown.Contains(c.Id))
            {
                _contactsKnown.Add(c.Id); if (_contactsKnown.Count > 64) _contactsKnown.RemoveAt(0);
                string what = c.Kind switch
                {
                    ContactKind.Distress => c.Crew > 0 ? $"조난 신호 — {name}: {c.Crew}명 · 공기 {c.AirHours:0}시간 · {c.Range / 1000:0}km" : $"{name} — 응답이 없다",
                    ContactKind.Derelict => $"레이더에 {name} — 응답이 없다 · {c.Range / 1000:0}km",
                    _ => $"{name} — {c.Range / 1000:0}km",
                };
                int cause = sim.Causes.Add(CauseKind.Computer, what, t, -1, c.Cause);
                sim.Facts.Emit(t, FactKind.Contact, -1, c.Id, cause, other: c.Kind == ContactKind.Distress && c.Crew > 0 ? 1 : 0);
                Say(sim, what);
                if (c.Kind == ContactKind.Distress && c.Crew > 0 || c.Kind == ContactKind.Derelict) Decide(sim, c, cause);
            }
            if (c.Kind is ContactKind.Trader or ContactKind.Patrol && !_hailed.Contains(c.Id) && c.Range < 2e8)
            {
                _hailed.Add(c.Id); if (_hailed.Count > 64) _hailed.RemoveAt(0); Hails++;
                int cause = sim.Causes.Add(CauseKind.Computer, $"{Ko.Gwa(name)} 교신했다 — 소식을 나눴다", t, -1, c.Cause);
                sim.Facts.Emit(t, FactKind.Contact, -1, c.Id, cause, other: 2);
                Say(sim, $"{Ko.Gwa(name)} 교신 — 소식을 나눴다");
                _mind!.Cheer(sim, 0.15f, cause);
            }
        }
    }

    /// <summary>그 배로 가는 값: 다가가는 빠르기(조난선이면 공기가 다하기 전에 · 많아야 30시간) · 맞추는 Δv + 멈추는 Δv · 걸리는 시간.</summary>
    private (double dv, double hours, double vClose) Rendezvous(in Contact c)
    {
        double R = Math.Max(1, c.Range);
        double hrs = c.Kind == ContactKind.Distress && c.Crew > 0 ? Math.Min(c.AirHours * 0.6, 30) : 30;
        double vClose = Math.Clamp(R / (Math.Max(0.5, hrs) * 3600), 5, 3000);
        double ux = c.X / R, uy = c.Y / R;
        double mx = c.Vx + ux * vClose, my = c.Vy + uy * vClose;
        double dv = Math.Sqrt(mx * mx + my * my) + vClose;
        return (dv, R / vClose / 3600 + 2 * AnnounceMin / 60, vClose);
    }

    /// <summary>남은 항로 점화에 드는 Δv (도착 · 붙잡기 · 고침).</summary>
    private double RouteNeed() => _out!.Plan.Sum(p => p.Dv) + (double.IsNaN(_out.PendingTo) ? 0 : 0);

    private void Decide(Sim sim, in Contact c, int cause)
    {
        var o = _out!; long t = sim.Tick;
        string name = o.ContactName(c);
        if (GoingTo >= 0) { Say(sim, $"{name} — 이미 다른 배로 가는 중"); return; }
        var (dv, hours, vClose) = Rendezvous(c);
        double reserve = 1.1 * RouteNeed(), left = o.DvLeft;
        bool afford = left - dv > reserve;
        bool inTime = c.Kind != ContactKind.Distress || hours < c.AirHours;
        bool engines = EnginesWorking() > 0;
        bool go = engines && inTime && (Policy.AnswerDistress == 2 || Policy.AnswerDistress == 1 && afford);
        if (c.Kind == ContactKind.Derelict) go = engines && afford && Policy.AnswerDistress != 0 && dv < 0.25 * left;   // 난파선은 부품 — 넉넉할 때만
        if (go)
        {
            GoingTo = c.Id; GoPhase = 1; GoAt = t + Time.Minutes(AnnounceMin); GoSince = t; GoClose = vClose;
            GoCause = sim.Causes.Add(CauseKind.Computer, $"주컴퓨터가 {Ko.Euro(name)} 가기로 했다 — Δv {dv:0}m/s · {hours:0.#}시간", t, -1, cause);
            sim.Facts.Emit(t, FactKind.Decision, -1, 1, GoCause, -2);
            Say(sim, $"{Ko.Euro(name)} 간다 — {hours:0.#}시간 · 3분 뒤 점화 · 앉거나 붙잡으세요");
            AnnounceAll(sim, (int)GoAt, GoCause);
            return;
        }
        Declined++;
        string why = !engines ? "엔진이 멈춰 있다" : !inTime ? "공기가 다하기 전에 닿을 수 없다" : Policy.AnswerDistress == 0 ? "함장 방침 — 가지 않는다" : "추진제가 모자란다 (도착 몫을 남겨야 한다)";
        int dc = sim.Causes.Add(CauseKind.Computer, $"주컴퓨터가 {name}에 가지 않기로 했다 — {why}", t, -1, cause);
        sim.Facts.Emit(t, FactKind.Decision, -1, -1, dc, -2);
        Say(sim, $"{name}에 가지 않는다 — {why}");
        if (c.Kind == ContactKind.Distress) _mind!.Distressed(sim, c.Crew, dc);
    }

    private void Going(Sim sim)
    {
        if (GoingTo < 0) return;
        var o = _out!; long t = sim.Tick;
        int k = o.ContactIndex(GoingTo);
        if (k < 0 || t - GoSince > Time.Days(4)) { GoingTo = -1; GoPhase = 0; return; }
        var c = o.Contacts[k];
        string name = o.ContactName(c);
        double R = Math.Max(1e-6, c.Range), v = c.Speed;
        int eng = EnginesWorking();
        double a = o.MaxAccel(Math.Max(1, eng));
        switch (GoPhase)
        {
            case 1 or 4 when t >= GoAt:
            {
                if (eng == 0) { GoAt = t + Time.Minutes(5); if (t - _noEngineSaid > Time.Minutes(10)) { _noEngineSaid = t; Say(sim, $"엔진이 멈춰 {Ko.Euro(name)} 가는 점화를 못 한다"); } break; }
                bool match = GoPhase == 1;
                double dx = match ? c.Vx + c.X / R * GoClose : c.Vx, dy = match ? c.Vy + c.Y / R * GoClose : c.Vy;
                double dv = Math.Sqrt(dx * dx + dy * dy);
                int bc = sim.Causes.Add(CauseKind.Computer, $"주컴퓨터가 {Ko.Euro(name)} {(match ? "다가가는" : "멈추는")} 점화를 했다 ({dv:0}m/s)", t, -1, GoCause);
                if (match) o.Approach(c.Id);
                if (dv < 0.2 || o.Burn(sim, match ? BurnKind.Match : BurnKind.Brake, dx, dy, a, dv, 0, RampSec, false, eng, bc)) GoPhase = (byte)(match ? 2 : 5);
                break;
            }
            case 2: if (o.Burning == BurnKind.None) GoPhase = 3; break;
            case 3:
            {
                // 다가감: 멈출 거리 (v²/2a + 방송 3분 + 올리는 20초에 가는 거리 + 200m)에 닿으면 멈추는 점화를 방송
                double stop = v * v / (2 * a) + v * (AnnounceMin * 60 + RampSec) + 200;
                if (R <= stop || c.X * c.Vx + c.Y * c.Vy > 0)
                {
                    GoPhase = 4; GoAt = t + Time.Minutes(AnnounceMin);
                    Say(sim, $"3분 뒤 멈추는 점화 — {name}까지 {R / 1000:0.#}km · 앉거나 붙잡으세요");
                    AnnounceAll(sim, (int)GoAt, GoCause);
                }
                break;
            }
            case 5 or 7:
            {
                if (o.Burning != BurnKind.None) break;
                if (c.State == ContactState.Docked) { GoPhase = 6; GoAt = t; Say(sim, $"{name}에 붙었다"); break; }
                if (GoPhase == 5 && R > Outside.DockRange)
                {
                    // 자세 분사기로 천천히 (2m/s) 다가간다
                    double dx = c.Vx + c.X / R * 2, dy = c.Vy + c.Y / R * 2;
                    if (o.Burn(sim, BurnKind.Match, dx, dy, o.RcsAccel, Math.Sqrt(dx * dx + dy * dy), 0, 0, true, 0, GoCause)) GoPhase = 7;
                }
                else if (GoPhase == 7 && (R < Outside.DockRange * 0.7 || c.X * c.Vx + c.Y * c.Vy > 0))
                {
                    if (v > 0.1) o.Burn(sim, BurnKind.Brake, c.Vx, c.Vy, o.RcsAccel, v, 0, 0, true, 0, GoCause);
                    GoPhase = 5;
                }
                break;
            }
            case 6 when t >= GoAt: Transfer(sim, c); break;
        }
    }

    /// <summary>붙은 배에서 옮긴다: 생존자는 1분에 한 사람씩 에어락으로 (숨이 모자라고 · 다치고 · 몇은 옮는 병) · 난파선은 그 배의 공기가 에어락으로 들고 수리 꾸러미를 건진다.</summary>
    private void Transfer(Sim sim, Contact c)
    {
        var o = _out!; long t = sim.Tick;
        string name = o.ContactName(c);
        int lockRoom = -1;
        for (int r = 0; r < L.Rooms.Count && lockRoom < 0; r++) foreach (int d in _roomDoors[r]) if (L.Doors[d].External) { lockRoom = r; break; }
        int cell = -1;
        if (lockRoom >= 0) foreach (int x in L.Rooms[lockRoom].Cells) if (L.Kind[x] == CellKind.Floor && L.FurnitureAt[x] < 0) { cell = x; break; }
        if (cell < 0) { o.Undock(sim, c.Id); GoingTo = -1; GoPhase = 0; return; }
        // 에어락에 공기가 없으면 (바깥문이 끼었다 · 새는 중) 넘어오지 않고 기다린다 — 진공으로 사람을 들이지 않는다 (찾아 고친 결함: 문이 낀 에어락으로 생존자 셋이 넘어와 그대로 숨졌다)
        double lockKPa = 0; foreach (int x in L.Rooms[lockRoom].Cells) lockKPa += F.Pressure(x); lockKPa /= Math.Max(1, L.Rooms[lockRoom].Cells.Length);
        if (lockKPa < 70)
        {
            if (t % Time.Minutes(10) < Time.Minutes(1)) Say(sim, $"{name}에 붙었지만 에어락에 공기가 없다 ({lockKPa:0}kPa) — 생존자는 그 배에서 기다린다");
            GoAt = t + Time.Minutes(1);
            return;
        }
        if (c.Kind == ContactKind.Distress && c.Crew > 0 && _life != null)
        {
            int id = _mind!.AddPerson(sim, cell);
            var who = _mind.Agents[id];
            ref var b = ref _life[who.C.Body];
            uint h = (uint)(c.Id * 31 + c.Crew);
            b.Hypoxia = 0.15f + 0.3f * (float)Rng.Unit(sim.Seed, Rng.Key("생존자"), t, h);
            b.Blunt = 0.2f * (float)Rng.Unit(sim.Seed, Rng.Key("생존자 다침"), t, h);
            int pc = sim.Causes.Add(CauseKind.Contact, $"{name}의 {Ko.I(_mind.NameOf(id))} 에어락으로 넘어왔다", t, cell, GoCause, 0, id);
            if (c.Sick != 0 && b.Ill == 0) { b.Ill = 1; b.IllH = 0; b.IllCause = pc; }
            sim.Facts.Emit(t, FactKind.Contact, cell, c.Id, pc, id, 6);
            Rescued++;
            c.Crew--; o.SetContact(c);
            GoAt = t + Time.Minutes(1);
            if (c.Crew > 0) return;
            Rescues++;
            Say(sim, $"{name}의 생존자를 모두 태웠다");
        }
        else if (c.Kind == ContactKind.Derelict || c.Kind == ContactKind.Distress)
        {
            // 그 배의 공기 (불이 났던 배면 일산화탄소 · 이산화탄소) 가 에어락으로 · 쓸 만한 부품
            int pc = sim.Causes.Add(CauseKind.Contact, $"{name}의 해치를 열었다 — {(c.Fire != 0 ? "탄 냄새와 매연이 밀려든다" : "차갑고 묵은 공기")}", t, cell, GoCause);
            if (c.Fire != 0)
                foreach (int x in L.Rooms[lockRoom].Cells)
                {
                    if (!L.IsSpace(x)) continue;
                    F.AddGas(x, T.gCO, 0.5, 300); F.AddGas(x, T.gCO2, 4, 300);
                }
            int kits = 2 + (int)(3 * Rng.Unit(sim.Seed, Rng.Key("건짐"), t, c.Id));
            _mind!.Salvage(sim, cell, kits, pc);
            Salvaged += kits;
            sim.Facts.Emit(t, FactKind.Contact, cell, c.Id, pc, -1, 7);
            Say(sim, $"{name}에서 수리 꾸러미 {kits}개를 건졌다");
        }
        o.Undock(sim, c.Id);
        GoingTo = -1; GoPhase = 0;
    }

    private void SaveHelm(StateWriter w)
    {
        w.I64(BurnsDone); w.I64(BurnsDelayed); w.I64(RocksSeen); w.I64(DodgesTried); w.I64(DodgesAvoided); w.I64(DodgesFailed); w.I64(BraceCalls); w.I64(Hails); w.I64(Rescues); w.I64(Declined); w.I64(Salvaged); w.I64(Rescued);
        w.I32(GoingTo); w.Byte(GoPhase); w.I64(GoAt); w.I64(GoSince); w.F64(GoClose); w.I32(GoCause);
        w.List(new List<PlannedBurn> { Active }); w.Byte(HasActive);
        w.Arr(_rocksKnown.ToArray()); w.Arr(_contactsKnown.ToArray()); w.Arr(_hailed.ToArray()); w.Arr(_braced.ToArray());
        w.I32(_dodgeRock); w.I64(_dodgeUntil); w.I64(_noEngineSaid); w.Str(HelmLine);
        w.Arr(RoomDarkSince); w.I64(JumpersCut);
    }

    private void LoadHelm(StateReader r)
    {
        BurnsDone = r.I64(); BurnsDelayed = r.I64(); RocksSeen = r.I64(); DodgesTried = r.I64(); DodgesAvoided = r.I64(); DodgesFailed = r.I64(); BraceCalls = r.I64(); Hails = r.I64(); Rescues = r.I64(); Declined = r.I64(); Salvaged = r.I64(); Rescued = r.I64();
        GoingTo = r.I32(); GoPhase = r.Byte(); GoAt = r.I64(); GoSince = r.I64(); GoClose = r.F64(); GoCause = r.I32();
        Active = r.List<PlannedBurn>()[0]; HasActive = r.Byte();
        _rocksKnown.Clear(); _rocksKnown.AddRange(r.Arr<int>()); _contactsKnown.Clear(); _contactsKnown.AddRange(r.Arr<int>()); _hailed.Clear(); _hailed.AddRange(r.Arr<int>()); _braced.Clear(); _braced.AddRange(r.Arr<int>());
        _dodgeRock = r.I32(); _dodgeUntil = r.I64(); _noEngineSaid = r.I64(); HelmLine = r.Str() ?? "";
        RoomDarkSince = r.Arr<long>(); JumpersCut = r.I64();
    }
}
