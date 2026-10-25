using System.Runtime.InteropServices;

namespace ShipEngine;

/// <summary>함장의 손 갈래 (docs/UI2.md §2.1 — 월드 길만 · 사람을 겨누지 않는다).</summary>
public enum HandKind : byte { None, Fire, Hole, PowerCut, DataCut, Fault, Gravity, WaterLeak, CoolantLeak, DoorJam, O2Drift, Blast, Shove, Rock, Cloud, Flare, Route }

/// <summary>손 하나: 일어난 틱 · 받은 차례 · 갈래 · 크기 (0 작게 · 1 보통 · 2 크게) · 부품 (고장) · 칸 · 대상 (물체 Id · 항로 번호) · 쪽 (밀기 m/s · 구름이 오는 쪽) · 붙인 원인 (0: 헛손).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]   // 찍기는 바이트를 그대로 쓴다 — 정렬용 빈 바이트가 없게
public struct HandOrder
{
    public long Tick;        // 일어난 틱 (받을 때는 0 — Step이 찍는다)
    public int Seq;          // 받은 차례 (1부터 · 세계마다)
    public HandKind Kind;
    public byte Size, Part;  // 크기 0~2 · 부품 (고장)
    public int Cell, Obj;    // 칸 · 대상 (물체 Id · 항로 번호)
    public float X, Y;       // 쪽 · 빠르기 (밀기 m/s · 구름이 오는 쪽)
    public int Cause;        // 붙인 원인 마디 (0: 헛손 — 그사이 대상이 사라졌다)
}

/// <summary>
/// 함장의 손 (docs/UI2.md §2.4 — 함장 요청 10-25): 바깥에서 오는 입력. 화면이 틱 사이에 받아 두면 다음 틱의 맨 처음 (단계 '바깥' · 엔진 가운데 가장 먼저)에
/// 받은 차례대로 이미 있는 월드 길 (불씨 · 구멍 · 선 끊기 · 낡음 · 샘 · 끼임 · 폭풍 · 밀기 · 운석 · 구름 · 별 폭발 · 항로)을 부른다 — 새 법칙은 없다.
/// 이야기꾼이 아니다: 사람 · 로봇 · 주컴퓨터 (⑦⑧⑨⑩)는 모른다 — 그들은 센서 · 본 것 · 들은 것으로만 안다. 원인 마디는 '함장이 일으킨 …' (갈래 함장).
/// 결정론: 한 줄로 적용 (병렬 없음) · 기다리는 손 · 한 손 장부 · 차례를 찍는다 (되살린 세계 · 다시보기가 같은 틱에 같은 손을 재현한다).
/// </summary>
public sealed class Hand : IEngine
{
    public string Name => "함장의 손";
    public Phase Phase => Phase.Outside;
    public int Every => 1;
    public int Offset => 0;

    /// <summary>받아 둔 손 (다음 틱 맨 처음에 비운다) · 한 손 (256까지 — 다시보기 고리 1시간치보다 넉넉).</summary>
    public List<HandOrder> Waiting = new(), Done = new();
    public int Seq;
    public const int MaxWaiting = 8, Keep = 256;

    // 크기 (작게 · 보통 · 크게 — 기능 확인용 첫 값 · 자연 사건처럼 맨 끝에 다듬는다 · 불씨: 침대 · 의자는 1~1.5MJ 사이에서 붙는다 — 보통이 붙게 2MJ)
    public static readonly double[] FireJ = { 0.5e6, 2e6, 5e6 }, HoleM2 = { 0.002, 0.02, 0.2 }, BlastJ = { 0.5e6, 2e6, 8e6 }, RockKg = { 0.005, 0.06, 0.5 },
        CloudThick = { 0.5, 1, 3 }, FlareScale = { 1, 2, 4 }, DriftHours = { 6, 2, 0.5 };
    public static readonly float[] Seam = { 0.1f, 0.4f, 1f };   // 관 샘: 이음매 조금 · 이음매 · 터짐 (끊는다)
    public const double RockSpeed = 5000, CloudMinutes = 14, CloudRadius = 400_000, MaxShove = 8;
    /// <summary>
    /// 운석이 처음 놓이는 거리 (약 456km — 91초 뒤): 틱마다 12km씩 오니 맞기 전 마지막으로 레이더가 보는 거리가 한 틱 길이 바로 아래 (11.9km)가 되게 —
    /// 레이더가 크기대로 잡는다 (5g 11.4km 못 잡고 맞는다 · 60g 17km 2.4초 앞 · 0.5kg 25km 4.8초 앞 — ⑥ 레이더 방정식 그대로).
    /// </summary>
    public static readonly double RockDistance = RockSpeed * Time.SecondsPerTick * 38 - 100;

    /// <summary>받아 둔다 (틱 사이에만 — 작업 스레드): 차례를 매긴다 · 가득이면 거짓.</summary>
    public bool Enqueue(HandOrder o)
    {
        if (Waiting.Count >= MaxWaiting || o.Kind == HandKind.None) return false;
        o.Seq = ++Seq; o.Tick = 0; o.Cause = 0;
        o.Size = (byte)Math.Min((int)o.Size, 2);
        Waiting.Add(o);
        return true;
    }

    public void Step(Sim sim, double dt)
    {
        if (Waiting.Count == 0) return;
        for (int i = 0; i < Waiting.Count; i++)   // 받은 차례대로 (Seq가 오르는 차례)
        {
            var o = Waiting[i];
            o.Tick = sim.Tick;
            if (Why(sim, o).Length == 0)
            {
                o.Cause = sim.Causes.Add(CauseKind.Captain, Text(sim, o), sim.Tick, CellOf(sim, o));
                Apply(sim, o, o.Cause);
            }
            Done.Add(o);   // 헛손 (그사이 대상이 사라졌다 · 판정이 바뀌었다)도 장부에는 남는다 — 원인 마디 없이
        }
        Waiting.Clear();
        if (Done.Count > Keep) Done.RemoveRange(0, Done.Count - Keep);
    }

    /// <summary>그 손의 자리 칸 (원인 마디 · 화면이 그 자리로) — 배 전체 · 바깥의 일은 -1.</summary>
    private static int CellOf(Sim sim, in HandOrder o) => o.Kind switch
    {
        HandKind.Shove => sim.Find<Structure>() is { } st && st.IndexOf(o.Obj) is int i and >= 0 ? sim.Layout.Index((int)Math.Floor(st.Props[i].X), (int)Math.Floor(st.Props[i].Y)) is int c && c >= 0 && c < sim.Layout.N ? c : -1 : -1,
        HandKind.Gravity => sim.Find<Network>() is { } net && GravityDevice(net) is int g and >= 0 ? net.Devices[g].Cell : -1,
        HandKind.O2Drift or HandKind.Cloud or HandKind.Flare or HandKind.Route => -1,
        _ => o.Cell >= 0 && o.Cell < sim.Layout.N ? o.Cell : -1,
    };

    /// <summary>칸 위의 설비 장치 (없으면 -1).</summary>
    public static int DeviceAt(Network net, Layout L, int cell)
    {
        if (cell < 0 || cell >= L.N || L.FurnitureAt[cell] < 0) return -1;
        int fu = L.FurnitureAt[cell];
        foreach (var d in net.Devices) if (d.Furniture == fu) return d.Id;
        return -1;
    }

    public static int GravityDevice(Network net) { foreach (var d in net.Devices) if (d.Def.Role == "gravity") return d.Id; return -1; }

    /// <summary>문 칸의 문 번호 (없으면 -1).</summary>
    private static int DoorOf(Layout L, int cell) => cell >= 0 && cell < L.N ? L.DoorAt[cell] : -1;

    private static bool In(Layout L, int c) => c >= 0 && c < L.N;

    /// <summary>
    /// 되나: "" 되면 · 아니면 안 되는 까닭 (게임 글 — 화면의 겨눔 이름표와 Step이 같은 판정 · 읽기만).
    /// </summary>
    public static string Why(Sim sim, in HandOrder o)
    {
        var L = sim.Layout; var f = sim.Find<Field>(); var net = sim.Find<Network>(); var st = sim.Find<Structure>(); var outside = sim.Find<Outside>();
        int c = o.Cell, size = Math.Min((int)o.Size, 2);
        switch (o.Kind)
        {
            case HandKind.Fire or HandKind.Blast:
                if (!In(L, c) || !L.IsSpace(c) || f == null || f.Severed[c] != 0) return "배 안을 누른다";
                if (o.Kind == HandKind.Fire && sim.Find<Chemistry>() == null) return "불이 붙을 세상이 아니다";
                if (o.Kind == HandKind.Blast && st == null) return "터질 세상이 아니다";
                return "";
            case HandKind.Hole:
                if (!In(L, c) || L.Kind[c] != CellKind.Wall || !L.Hull[c] || st == null || f == null) return "외판 칸을 누른다";
                if (f.Hole[c] >= HoleM2[size]) return "이미 뚫렸다";
                return "";
            case HandKind.PowerCut:
                if (net == null || !In(L, c) || (net.LineAt[c] & Network.Bit(LineKind.Power)) == 0) return "전선이 지나는 칸을 누른다";
                if ((net.LineCut[c] & Network.Bit(LineKind.Power)) != 0) return "이미 끊겼다";
                return "";
            case HandKind.DataCut:
                if (net == null || !net.OnData(c)) return "센서 선이 지나는 칸을 누른다";
                if (net.DataCutAt(c)) return "이미 끊겼다";
                return "";
            case HandKind.WaterLeak or HandKind.CoolantLeak:
            {
                var k = o.Kind == HandKind.WaterLeak ? LineKind.Water : LineKind.Coolant;
                string pipe = k == LineKind.Water ? "물관" : "냉각관";
                if (net == null || !In(L, c) || (net.LineAt[c] & Network.Bit(k)) == 0) return $"{pipe} 칸을 누른다";
                if ((net.LineCut[c] & Network.Bit(k)) != 0) return "이미 끊겼다";
                if (size < 2 && net.PipeLeak[c * 2 + (k == LineKind.Water ? 1 : 0)] >= Seam[size]) return "이미 샌다";
                return "";
            }
            case HandKind.Fault:
            {
                int d = net != null ? DeviceAt(net, L, c) : -1;
                if (d < 0) return "설비를 누른다";
                var p = (Part)Math.Min((int)o.Part, 3);
                if (!net!.Devices[d].Has(p)) return $"그 설비에는 {Device.PartName(p)} 부품이 없다";
                if (net.IsBroken(d, p)) return "이미 망가졌다";
                return "";
            }
            case HandKind.Gravity:
            {
                int g = net != null ? GravityDevice(net) : -1;
                if (g < 0) return "이 배에는 인공 중력 발생기가 없다";
                if (net!.IsBroken(g, Part.Mechanical)) return "이미 망가졌다";
                return "";
            }
            case HandKind.DoorJam:
            {
                int d = DoorOf(L, c);
                if (d < 0 || st == null) return "문 칸을 누른다";
                if (st.LeafGone(d)) return "문짝이 날아간 문이다";
                if (st.DoorJam[d] != 0) return "이미 끼었다";
                return "";
            }
            case HandKind.O2Drift:
                if (net == null || !net.Devices.Any(x => x.Def.Role == "o2gen")) return "이 배에는 산소 발생기가 없다";
                if (net.O2DriftRate > 0 && net.O2Gain > net.O2DriftFloor) return "이미 어긋나는 중이다";
                return "";
            case HandKind.Shove:
            {
                int i = st?.IndexOf(o.Obj) ?? -1;
                if (i < 0) return "그 물건이 없다";
                if (st!.Props[i].State is 5 or 6 or 7) return "그 물건은 움직일 수 없다";
                return "";
            }
            case HandKind.Rock:
                if (outside == null) return "바깥이 없는 세상이다";
                if (outside.Planet != null) return "행성 위에서는 오지 않는다";
                if (!In(L, c) || L.Kind[c] != CellKind.Wall || !L.Hull[c]) return "외판 칸을 누른다";
                return "";
            case HandKind.Cloud or HandKind.Flare:
                if (outside == null) return "바깥이 없는 세상이다";
                if (outside.Planet != null) return "행성 위에서는 오지 않는다";
                return "";
            case HandKind.Route:
            {
                if (outside == null || o.Obj < 1 || o.Obj > sim.Tables.Space.Routes.Count) return "그런 항로는 없다";
                if (DepartDue(outside) < 0) return "이미 떠났다 — 항로는 출항 때만 고른다";
                return "";
            }
            default: return "할 일을 고르지 않았다";
        }
    }

    /// <summary>출발 점화가 계획에 남은 틱 (떠났으면 -1).</summary>
    private static long DepartDue(Outside o)
    {
        if (double.IsNaN(o.PendingTo) || o.VoyageTicks > 0) return -1;
        foreach (var p in o.Plan) if (p.Kind == BurnKind.Depart) return p.Due;
        return -1;
    }

    /// <summary>원인 글 (게임 글): '함장이 일으킨 불씨 — 식당 침대' · '함장이 보낸 운석 — 60g' …</summary>
    public static string Text(Sim sim, in HandOrder o)
    {
        var L = sim.Layout; var net = sim.Find<Network>();
        int c = o.Cell, size = Math.Min((int)o.Size, 2);
        string Room(int cell) => In(L, cell) && L.Room[cell] >= 0 ? L.Rooms[L.Room[cell]].Name : "통로";
        string Near(int cell)
        {
            // 벽 · 문 칸은 곁의 방 이름 (첫 공간 이웃)
            if (!In(L, cell)) return "배";
            if (L.Room[cell] >= 0) return L.Rooms[L.Room[cell]].Name;
            foreach (int j in new[] { cell - 1, cell + 1, cell - L.W, cell + L.W }) if (In(L, j) && L.Room[j] >= 0) return L.Rooms[L.Room[j]].Name;
            return "통로";
        }
        string Furn(int cell) => In(L, cell) && L.FurnitureAt[cell] >= 0 ? " " + sim.Tables.Equip(L.Furniture[L.FurnitureAt[cell]].Type).Name : "";
        switch (o.Kind)
        {
            case HandKind.Fire: return $"함장이 일으킨 불씨 — {Room(c)}{Furn(c)}";
            case HandKind.Hole: return $"함장이 일으킨 외판 구멍 — {Near(c)}";
            case HandKind.PowerCut: return $"함장이 일으킨 전선 끊김 — {Near(c)}";
            case HandKind.DataCut: return $"함장이 일으킨 센서 선 끊김 — {Near(c)}";
            case HandKind.Fault:
            {
                int d = net != null ? DeviceAt(net, L, c) : -1;
                return d >= 0 ? $"함장이 일으킨 고장 — {net!.Devices[d].Name}의 {Device.PartName((Part)Math.Min((int)o.Part, 3))} 부품" : "함장이 일으킨 고장";
            }
            case HandKind.Gravity: return "함장이 일으킨 고장 — 인공 중력 발생기";
            case HandKind.WaterLeak: return $"함장이 일으킨 물관 {(size == 2 ? "터짐" : "샘")} — {Near(c)}";
            case HandKind.CoolantLeak: return $"함장이 일으킨 냉각관 {(size == 2 ? "터짐" : "샘")} — {Near(c)}";
            case HandKind.DoorJam:
            {
                int d = DoorOf(L, c);
                if (d < 0) return "함장이 일으킨 문 끼임";
                int a = -1, b = -1;
                foreach (int j in new[] { c - 1, c + 1, c - L.W, c + L.W }) if (In(L, j) && L.Room[j] >= 0) { if (a < 0) a = L.Room[j]; else if (L.Room[j] != a) { b = L.Room[j]; break; } }
                return $"함장이 일으킨 문 끼임 — {(a >= 0 ? L.Rooms[a].Name : "통로")} ↔ {(b >= 0 ? L.Rooms[b].Name : "통로")}";
            }
            case HandKind.O2Drift: return "함장이 일으킨 센서 어긋남 — 산소 발생기 조절 센서";
            case HandKind.Blast: return $"함장이 일으킨 폭발 — 화약 {BlastJ[size] / Structure.TntJ:0.#}kg만큼 · {Room(c)}";
            case HandKind.Shove:
            {
                var st = sim.Find<Structure>();
                int i = st?.IndexOf(o.Obj) ?? -1;
                return i >= 0 ? $"함장이 던진 {st!.PropName(st.Props[i])} ({Math.Min(MaxShove, Math.Sqrt(o.X * o.X + o.Y * o.Y)):0.#}m/s)" : "함장이 던진 물건";
            }
            case HandKind.Rock: return $"함장이 보낸 운석 — {RockKg[size] * 1000:0}g";
            case HandKind.Cloud: return "함장이 보낸 작은 바위 구름";
            case HandKind.Flare: return "함장이 일으킨 별 폭발";
            case HandKind.Route:
            {
                var routes = sim.Tables.Space.Routes;
                return o.Obj >= 1 && o.Obj <= routes.Count ? $"함장이 항로를 골랐다 — {o.Obj} '{routes[o.Obj - 1].Name}'" : "함장이 항로를 골랐다";
            }
            default: return "함장의 손";
        }
    }

    /// <summary>§2.1 표의 월드 길 (있는 법칙을 그대로 부른다).</summary>
    private static void Apply(Sim sim, in HandOrder o, int cause)
    {
        var L = sim.Layout; var net = sim.Find<Network>(); var st = sim.Find<Structure>(); var outside = sim.Find<Outside>();
        int c = o.Cell, size = Math.Min((int)o.Size, 2);
        switch (o.Kind)
        {
            case HandKind.Fire: sim.Get<Chemistry>().Ignite(c, FireJ[size], cause); break;
            case HandKind.Hole: st!.Tear(sim, c, (float)HoleM2[size], cause); break;
            case HandKind.PowerCut: net!.CutLines(sim, c, Network.Bit(LineKind.Power), cause); break;
            case HandKind.DataCut: net!.CutData(sim, c, cause); break;
            case HandKind.Fault: net!.Age(DeviceAt(net, L, c), (Part)Math.Min((int)o.Part, 3), 1.2, cause); break;   // 숨은 한계를 넘었다 — 1분 안의 낡음 걸음이 고장을 낸다
            case HandKind.Gravity: net!.Age(GravityDevice(net), Part.Mechanical, 1.2, cause); break;
            case HandKind.WaterLeak or HandKind.CoolantLeak:
            {
                var k = o.Kind == HandKind.WaterLeak ? LineKind.Water : LineKind.Coolant;
                if (size < 2) net!.LeakPipe(sim, c, k, Seam[size], cause, known: false);   // 모르는 샘 — 찾아야 안다
                else net!.CutLines(sim, c, Network.Bit(k), cause);
                break;
            }
            case HandKind.DoorJam: st!.JamDoor(sim, DoorOf(L, c), cause); break;
            case HandKind.O2Drift: net!.StartO2Drift(DriftHours[size], cause); break;
            case HandKind.Blast: st!.Blast(sim, c, BlastJ[size], cause); break;
            case HandKind.Shove:
            {
                double v = Math.Sqrt(o.X * o.X + o.Y * o.Y), k = v > MaxShove ? MaxShove / v : 1;
                st!.ShoveTo(o.Obj, o.X * k, o.Y * k, cause);
                break;
            }
            case HandKind.Rock:
            {
                var (ux, uy, offset) = RockAim(L, c);
                outside!.AddRock(RockKg[size], RockSpeed, ux, uy, offset, RockDistance, cause);
                break;
            }
            case HandKind.Cloud:
            {
                double dx = o.X, dy = o.Y;
                if (dx * dx + dy * dy < 1e-6 && In(L, c)) { dx = L.W / 2.0 - (L.X(c) + 0.5); dy = L.H / 2.0 - (L.Y(c) + 0.5); }   // 누른 쪽에서 배로
                if (dx * dx + dy * dy < 1e-6) { dx = 1; dy = 0; }
                var d = sim.Tables.Space.Debris["rubble"];
                outside!.AddCloud("rubble", d.Speed * CloudMinutes * 60 + CloudRadius, CloudRadius, 2e-7 * CloudThick[size], dx, dy, cause);   // 14분 앞 — 센서 안 · 대피 문턱 안
                break;
            }
            case HandKind.Flare: outside!.ScheduleFlare(sim.Tick, 1, cause, FlareScale[size]); break;
            case HandKind.Route:
            {
                long due = DepartDue(outside!);
                double minutes = Math.Max(1, (due - sim.Tick) / (double)Time.TicksPerHour * 60);
                outside!.Route = o.Obj;
                outside.PlanVoyage(sim, outside.PendingTo, outside.PendingName, minutes, cause);   // 출발 점화를 새 항로의 Δv로 다시 계획한다 (주컴퓨터가 하는 것은 그대로)
                break;
            }
        }
    }

    /// <summary>
    /// 운석이 그 외판 칸에 맞게: 오는 쪽 = 그 칸의 바깥 이웃 쪽의 반대 (바깥에서 안으로) · 비낀 거리 = 칸 가운데가 배 가운데에서 비낀 만큼
    /// (⑤ 바깥이 운석을 옮기는 틀 그대로 — 도면 가운데 틀 · 맞음 셈은 비낀 거리의 반대쪽 부호를 쓴다).
    /// </summary>
    public static (double ux, double uy, double offset) RockAim(Layout L, int c)
    {
        int x = L.X(c), y = L.Y(c);
        double nx = 0, ny = 0;
        foreach (var (dx, dy) in new[] { (0, -1), (0, 1), (-1, 0), (1, 0) })
            if (!L.In(x + dx, y + dy) || L.Kind[L.Index(x + dx, y + dy)] == CellKind.Outside) { nx = dx; ny = dy; break; }
        if (nx == 0 && ny == 0) { nx = x + 0.5 - L.W / 2.0; ny = y + 0.5 - L.H / 2.0; double n = Math.Sqrt(nx * nx + ny * ny); if (n < 1e-9) { nx = 1; } else { nx /= n; ny /= n; } }
        double ux = -nx, uy = -ny;                            // 바깥에서 안으로
        double rx = x + 0.5 - L.W / 2.0, ry = y + 0.5 - L.H / 2.0;
        double hit = rx * -uy + ry * ux;                      // 칸 가운데가 진행 쪽의 왼쪽으로 비낀 거리
        return (ux, uy, -hit);
    }

    public void Save(StateWriter w) { w.List(Waiting); w.List(Done); w.I32(Seq); }
    public void Load(StateReader r) { Waiting = r.List<HandOrder>(); Done = r.List<HandOrder>(); Seq = r.I32(); }
}
