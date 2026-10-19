namespace ShipEngine;

public enum ContactKind : byte { Trader, Patrol, Distress, Derelict }
public enum ContactState : byte { Far, Coming, Docked, Gone }   // Coming: 주컴퓨터가 가겠다고 했다 (가까이 느리면 붙는다)

/// <summary>다른 배 하나: 배에 대한 상대 위치 · 속도 (m · m/s) · 탄 사람 · 남은 공기 · 그 배의 형편.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
public struct Contact
{
    public int Id, NameNo, Cause;
    public ContactKind Kind;
    public ContactState State;
    public double X, Y, Vx, Vy;
    public int Crew;          // 살아 있는 사람 (조난선)
    public float AirHours;    // 조난선: 공기가 남은 시간
    public byte Fire;         // 불이 났던 배 (일산화탄소 · 연기가 남았다)
    public byte Sick;         // 옮는 병이 도는 배
    public byte Beacon;       // 신호기가 켜져 있다 (멀리서도 안다)
    public long Since, DockedAt;
    public double Range => Math.Sqrt(X * X + Y * Y);
    public double Speed => Math.Sqrt(Vx * Vx + Vy * Vy);
}

/// <summary>
/// ⑤ 다른 배 (docs/ENGINE.md ⑤ 바퀴 2): 상대 운동으로 지나가고 · 조난선은 공기가 줄고 · 가까이 멈추면 붙는다(도킹).
/// 알아보고 · 교신하고 · 갈지 정하는 것은 주컴퓨터(⑩) · 붙은 뒤 넘어오는 사람 · 공기는 위층이 한다. 지나가는 배는 <see cref="TrafficOn"/>일 때만 저절로 (시험은 끈다).
/// </summary>
public sealed partial class Outside
{
    public static readonly string[] ShipNames = { "푸른솔호", "은하수호", "갈매기호", "새벽별호", "너울호", "미리내호", "한울호", "바람꽃호", "늘봄호", "다솜호", "별누리호", "물결호" };
    public static string KindName(ContactKind k) => k switch { ContactKind.Trader => "상선", ContactKind.Patrol => "순찰선", ContactKind.Distress => "조난선", _ => "난파선" };
    public const double DockRange = 300, DockSpeed = 3;   // 이만큼 가까이 · 이만큼 느리면 붙는다 (m · m/s)

    public bool TrafficOn;
    /// <summary>⑫ 함장이 출항 때 고른 항로 (1~8 · 0이면 고르지 않았다 — 시험의 가만히 떠 있는 배).</summary>
    public int Route;
    public RouteDef? Rt => Route >= 1 && Route <= T.Space.Routes.Count ? T.Space.Routes[Route - 1] : null;
    public List<Contact> Contacts = new();
    public int ContactSeq, RockSeq, CloudSeq;
    public long Docked, LateArrivals;
    private RngStream _traffic, _route;

    public string ContactName(in Contact c) => $"{KindName(c.Kind)} {ShipNames[c.NameNo % ShipNames.Length]}";

    /// <summary>다른 배 하나를 놓는다 (교통량 · 시험): 종류 · 지금 거리(m) · 오는 쪽 · 상대 속도(m/s) · 비낀 거리(m).</summary>
    public int AddContact(Sim sim, ContactKind kind, double distance, double dirX, double dirY, double speed, double offset, int crew, float airHours, int cause, byte fire = 0, byte sick = 0)
    {
        double len = Math.Sqrt(dirX * dirX + dirY * dirY);
        dirX /= len; dirY /= len;
        double px = -dirY, py = dirX;
        var c = new Contact
        {
            Id = ++ContactSeq, NameNo = (int)(_traffic.Unit() * ShipNames.Length), Kind = kind, State = ContactState.Far,
            X = -dirX * distance + px * offset, Y = -dirY * distance + py * offset, Vx = dirX * speed, Vy = dirY * speed,
            Crew = crew, AirHours = airHours, Fire = fire, Sick = sick, Beacon = (byte)(kind == ContactKind.Derelict ? 0 : 1), Since = sim.Tick, Cause = cause,
        };
        Contacts.Add(c);
        return c.Id;
    }

    public int ContactIndex(int id) => Contacts.FindIndex(c => c.Id == id);

    /// <summary>주컴퓨터의 요청: 그 배로 간다 (가까이 느리게 다가가면 붙는다).</summary>
    public void Approach(int id) { int k = ContactIndex(id); if (k < 0) return; var c = Contacts[k]; if (c.State == ContactState.Far) c.State = ContactState.Coming; Contacts[k] = c; }
    /// <summary>가기를 그만둔다.</summary>
    public void GiveUp(int id) { int k = ContactIndex(id); if (k < 0) return; var c = Contacts[k]; if (c.State == ContactState.Coming) c.State = ContactState.Far; Contacts[k] = c; }

    private void ContactsStep(Sim sim, double dt)
    {
        // 저절로 지나가는 배 (행성 · 천체 궤도 곁이면 더 자주)
        if (TrafficOn && Planet == null)
        {
            var tr = T.Space.Traffic;
            bool near = T.Space.System.Bodies.Exists(b => Math.Abs(b.Au - Distance) < tr.NearAu);
            if (_traffic.Unit() < tr.PerDay * (near ? tr.Boost : 1) * (Rt?.Traffic ?? 1) / Time.TicksPerDay)
            {
                double u = _traffic.Unit();
                var kind = u < 0.5 ? ContactKind.Trader : u < 0.72 ? ContactKind.Patrol : u < 0.9 ? ContactKind.Distress : ContactKind.Derelict;
                double ang = _traffic.Range(0, 2 * Math.PI), speed = kind == ContactKind.Distress ? _traffic.Range(200, 1500) : _traffic.Range(1500, 6000);
                double dist = speed * _traffic.Range(6, 30) * 3600;   // 6 ~ 30시간 거리
                int crew = kind == ContactKind.Distress ? 1 + (int)(_traffic.Unit() * 4) : 0;
                float air = kind == ContactKind.Distress ? (float)_traffic.Range(10, 48) : 0;
                byte fire = (byte)(kind == ContactKind.Derelict && _traffic.Unit() < 0.5 ? 1 : 0), sick = (byte)(kind == ContactKind.Distress && _traffic.Unit() < 0.3 ? 1 : 0);
                string what = kind switch { ContactKind.Distress => $"{crew}명이 탄 배가 고장 나 떠돈다", ContactKind.Derelict => "버려진 배가 떠돈다", _ => "다른 배가 항로를 지난다" };
                int cause = sim.Causes.Add(CauseKind.Contact, what, sim.Tick);
                AddContact(sim, kind, dist, Math.Cos(ang), Math.Sin(ang), speed, _traffic.Range(-1, 1) * dist * 0.02, crew, air, cause, fire, sick);
            }
        }
        for (int k = 0; k < Contacts.Count; k++)
        {
            var c = Contacts[k];
            if (c.State != ContactState.Docked) { c.X += c.Vx * dt; c.Y += c.Vy * dt; }
            else { c.X = c.Y = c.Vx = c.Vy = 0; }
            if (c.Kind == ContactKind.Distress && c.Crew > 0 && c.State != ContactState.Docked)
            {
                c.AirHours -= (float)(dt / 3600);
                if (c.AirHours <= 0)
                {
                    c.AirHours = 0; c.Crew = 0; c.Beacon = 0;
                    int cause = sim.Causes.Add(CauseKind.Contact, $"{ContactName(c)}의 공기가 다했다 — 신호가 끊겼다", sim.Tick, -1, c.Cause);
                    if (c.State == ContactState.Coming) LateArrivals++;
                    sim.Facts.Emit(sim.Tick, FactKind.Contact, -1, c.Id, cause, other: 5);
                    c.Cause = cause;
                }
            }
            if (c.State == ContactState.Coming && c.Range < DockRange && c.Speed < DockSpeed)
            {
                c.State = ContactState.Docked; c.DockedAt = sim.Tick; c.X = c.Y = c.Vx = c.Vy = 0; Docked++;
                int cause = sim.Causes.Add(CauseKind.Contact, $"{ContactName(c)}에 붙었다", sim.Tick, -1, c.Cause);
                c.Cause = cause;
                sim.Facts.Emit(sim.Tick, FactKind.Contact, -1, c.Id, cause, other: 3);
            }
            Contacts[k] = c;
        }
        // 멀어진 배 · 떠난 배는 지운다 (다가오는 것은 남긴다)
        Contacts.RemoveAll(c => c.State != ContactState.Docked && c.State != ContactState.Coming && c.X * c.Vx + c.Y * c.Vy > 0 && c.Range > 5e8);
    }

    /// <summary>그 배의 형편을 고친다 (넘어온 사람만큼 줄었다).</summary>
    public void SetContact(in Contact c) { int k = ContactIndex(c.Id); if (k >= 0) Contacts[k] = c; }

    /// <summary>붙어 있던 배를 놓는다 (다 옮겼다).</summary>
    public void Undock(Sim sim, int id)
    {
        int k = ContactIndex(id);
        if (k < 0) return;
        var c = Contacts[k];
        c.State = ContactState.Gone; c.Vx = 0.5; c.Vy = 0; c.X = DockRange + 1;
        Contacts[k] = c;
        sim.Facts.Emit(sim.Tick, FactKind.Contact, -1, c.Id, c.Cause, other: 4);
    }

    private void SaveContacts(StateWriter w)
    {
        w.Bool(TrafficOn); w.I32(Route); w.List(Contacts); w.I32(ContactSeq); w.I32(RockSeq); w.I32(CloudSeq); w.I64(Docked); w.I64(LateArrivals);
        _traffic.Save(w); _route.Save(w);
    }

    private void LoadContacts(StateReader r)
    {
        TrafficOn = r.Bool(); Route = r.I32(); Contacts = r.List<Contact>(); ContactSeq = r.I32(); RockSeq = r.I32(); CloudSeq = r.I32(); Docked = r.I64(); LateArrivals = r.I64();
        _traffic.Load(r); _route.Load(r);
    }
}
