namespace ShipEngine;

/// <summary>별계 지도의 한 곳: 천체 · 정거장 · 광산 (표에서 — 찍지 않는다).</summary>
public sealed class Place
{
    public string Name = "";
    public double Au;
    public byte Kind;     // 0 천체 · 1 정거장 · 2 광산
    public int Ref;       // 천체 번호 · 정거장 번호
    public string Planet = "";
}

/// <summary>
/// ⑤ 별계 지도 (깊이 바퀴 3 · docs/ENGINE.md ⑤ '별계 지도'): 천체 · 정거장 · 광산이 제 원 궤도를 돈다 (케플러 — 공전 주기 a^1.5 년 · 처음 각은 시드 · 고향이 0°).
/// 배도 제 자리를 돈다 — 머물면 그 거리의 빠르기로 · 전이 궤도 위에서는 케플러 방정식대로 (떠난 자리 + 지난 참근점 이각) · 닿으면 그 자리에서 다시 돈다.
/// <para>호만 전이로 닿으려면 떠날 때 목표가 앞서 있어야 한다 (앞선 각 π(1 − ((r₁+r₂)/2r₂)^1.5)) — 그 각이 돌아오는 때가 발사 창 (회합 주기마다).
/// 닿았을 때 목표가 <see cref="CaptureDeg"/>° 안에 있으면 그 자리에 든다 · 정거장 · 광산이면 곁에 붙는 배가 된다 (⑪ 보급 흐름). 어디로 갈지는 주컴퓨터(⑩)가 정한다.</para>
/// </summary>
public sealed partial class Outside
{
    public const double CaptureDeg = 3;
    public const double ApproachKm = 30;     // 닿은 정거장 · 광산이 떠 있는 거리

    public readonly List<Place> Places = new();
    public int Home;                          // 고향 (지도의 0°)

    // ── 상태 (찍는다) ──
    public double[] PlaceTheta0 = Array.Empty<double>();   // 틱 0의 각 (라디안)
    public double ShipTheta0;                 // 머문 배의 각 (ShipThetaTick 때)
    public long ShipThetaTick;
    public double ShipParkAu = 1;             // 머문 궤도 반지름
    public int AtPlace = -1;                  // 지금 머문 곳 (-1: 전이 궤도 위 · 빈 궤도)
    public int VoyagePlace = -1, PendingPlace = -1;   // 가는 곳 · 출발 점화 뒤 갈 곳
    public double ArrivalMissDeg;             // 닿았을 때 목표와 어긋난 각
    public long PlacesReached, PlacesMissed;

    private void InitMap(ulong seed)
    {
        var sys = T.Space.System;
        for (int i = 0; i < sys.Bodies.Count; i++) Places.Add(new Place { Name = sys.Bodies[i].Name, Au = sys.Bodies[i].Au, Kind = 0, Ref = i, Planet = sys.Bodies[i].Planet });
        for (int i = 0; i < T.Space.Stations.Count; i++) Places.Add(new Place { Name = T.Space.Stations[i].Name, Au = T.Space.Stations[i].Au, Kind = (byte)(T.Space.Stations[i].Mine ? 2 : 1), Ref = i });
        Home = 0;
        for (int i = 0; i < Places.Count; i++) if (Places[i].Kind == 0 && Math.Abs(Places[i].Au - 1) < Math.Abs(Places[Home].Au - 1)) Home = i;
        PlaceTheta0 = new double[Places.Count];
        for (int i = 0; i < Places.Count; i++) PlaceTheta0[i] = i == Home ? 0 : 2 * Math.PI * Rng.Unit(seed, Rng.Key("별계 지도"), 0, i);
        ShipTheta0 = 0; ShipThetaTick = 0; ShipParkAu = Places.Count > 0 ? Places[Home].Au : 1; AtPlace = Places.Count > 0 ? Home : -1;
    }

    // ───────────────────────── 법칙 ─────────────────────────

    /// <summary>원 궤도의 빠르기 (라디안/틱 — 공전 주기 a^1.5 년).</summary>
    public static double Mean(double au) => 2 * Math.PI / (Math.Pow(Math.Max(1e-3, au), 1.5) * DaysPerYear * Time.TicksPerDay);
    /// <summary>공전 주기 (일).</summary>
    public static double PeriodDays(double au) => Math.Pow(au, 1.5) * DaysPerYear;
    /// <summary>호만 전이로 닿으려면 떠날 때 목표가 앞서 있어야 하는 각 (라디안 — 안으로 가면 음수: 뒤에 있어야 한다).</summary>
    public static double LeadAngle(double r1, double r2) => Math.PI * (1 - Math.Pow((r1 + r2) / (2 * r2), 1.5));
    /// <summary>회합 주기 (일): 두 궤도의 앞뒤가 다시 같아지는 때 — 발사 창은 이만큼마다.</summary>
    public static double SynodicDays(double r1, double r2) => 1 / Math.Abs(1 / PeriodDays(r1) - 1 / PeriodDays(r2));

    /// <summary>전이 궤도에서 지난 참근점 이각 (라디안 — 떠난 자리부터): 케플러 방정식 → 이심 이각 → 참근점 이각 (이어진 식).</summary>
    public static double Travelled(double from, double to, double fraction)
    {
        double e = Math.Abs(to - from) / Math.Max(1e-12, to + from);
        double m0 = to < from ? Math.PI : 0, m = m0 + Math.PI * fraction;
        double ecc = m;
        for (int k = 0; k < 30; k++) ecc -= (ecc - e * Math.Sin(ecc) - m) / (1 - e * Math.Cos(ecc));
        double beta = e / (1 + Math.Sqrt(1 - e * e));
        double Nu(double E) => E + 2 * Math.Atan(beta * Math.Sin(E) / (1 - beta * Math.Cos(E)));
        return Nu(ecc) - Nu(m0);
    }

    private static double Wrap(double a) { a %= 2 * Math.PI; if (a > Math.PI) a -= 2 * Math.PI; if (a < -Math.PI) a += 2 * Math.PI; return a; }
    private static double Wrap2(double a) { a %= 2 * Math.PI; return a < 0 ? a + 2 * Math.PI : a; }

    public double PlaceTheta(int i, long tick) => PlaceTheta0[i] + Mean(Places[i].Au) * tick;

    /// <summary>배의 각: 머물면 머문 궤도의 빠르기로 · 전이 궤도 위면 떠난 자리 + 지난 참근점 이각.</summary>
    public double ShipTheta(long tick)
    {
        if (VoyageTicks > 0 && VoyageTo > 0 && VoyageFrom > 0)
        {
            double dep = ShipTheta0 + Mean(ShipParkAu) * (VoyageStart - ShipThetaTick);
            double frac = (tick - VoyageStart) / (double)VoyageTicks;
            return dep + Travelled(VoyageFrom, VoyageTo, Missed != 0 ? Math.Max(0, frac) : Math.Clamp(frac, 0, 1));
        }
        return ShipTheta0 + Mean(ShipParkAu) * (tick - ShipThetaTick);
    }

    /// <summary>그곳까지 별에서 본 각 (도 · −180~180 — 양수면 그곳이 앞서 있다).</summary>
    public double AheadDeg(int place, long tick) => Wrap(PlaceTheta(place, tick) - ShipTheta(tick)) * 180 / Math.PI;

    /// <summary>그곳으로 가는 발사 창까지 (틱 — 머문 배에서 · 같은 궤도면 없다: long.MaxValue).</summary>
    public long WindowWait(int place, long tick)
    {
        double r1 = Distance, r2 = Places[place].Au;
        double rel = Mean(r2) - Mean(r1);
        if (Math.Abs(rel) < 1e-15) return long.MaxValue;
        double phi = PlaceTheta(place, tick) - ShipTheta(tick), need = LeadAngle(r1, r2);
        double w = rel > 0 ? Wrap2(need - phi) / rel : Wrap2(phi - need) / -rel;
        return (long)Math.Round(w);
    }

    /// <summary>그곳까지 호만 전이 Δv 둘의 합 (m/s — 항로 기울기 포함).</summary>
    public double LegDv(double fromAu, int place) { var (a, b) = HohmannDv(fromAu, Places[place].Au, Rt?.Incline ?? 0); return a + b; }

    /// <summary>시작 준비 (함장이 정한 임무 — 처음 구간은 발사 창에 맞춰 나선다): 그곳이 tick에 발사 창에 오도록 처음 각을 맞춘다.</summary>
    public void AlignWindow(int place, long tick)
    {
        if (place < 0 || place >= Places.Count || place == Home) return;
        double want = ShipTheta(tick) + LeadAngle(Distance, Places[place].Au);
        PlaceTheta0[place] = want - Mean(Places[place].Au) * tick;
    }

    public int PlaceOf(string name) => Places.FindIndex(p => p.Name == name);

    /// <summary>정해진 곳으로 가는 항해를 계획한다 (주컴퓨터 — 발사 창에 맞춰 minutes 뒤 출발 점화).</summary>
    public void PlanVoyageTo(Sim sim, int place, double minutes, int cause)
    {
        PlanVoyage(sim, Places[place].Au, Places[place].Name, minutes, cause);
        PendingPlace = place;
    }

    /// <summary>주컴퓨터의 요청: 계획한 출발을 거둔다 (떠나지 않는다 — 출발 점화 · 갈 곳을 지운다).</summary>
    public void CancelVoyage()
    {
        if (double.IsNaN(PendingTo)) return;
        Plan.RemoveAll(p => p.Kind == BurnKind.Depart);
        PendingTo = double.NaN; PendingName = ""; PendingPlace = -1; VoyagePlanned = 0;
    }

    /// <summary>항해를 시작할 때: 머문 궤도의 각을 지금으로 다시 잡는다 (떠난 자리).</summary>
    private void LeavePark(long tick)
    {
        ShipTheta0 = ShipTheta(tick); ShipThetaTick = tick; ShipParkAu = Distance; AtPlace = -1;
    }

    /// <summary>닿았다 (전이 궤도가 끝났다): 그 거리에서 다시 돈다 · 목표가 3° 안이면 그곳에 든다 · 정거장 · 광산이면 곁에 붙는 배가 된다.</summary>
    private void Arrive(Sim sim, long tick)
    {
        double dep = ShipTheta0 + Mean(ShipParkAu) * (VoyageStart - ShipThetaTick);
        long tArr = Missed != 0 ? tick : Math.Min(tick, VoyageStart + VoyageTicks);
        double frac = (tArr - VoyageStart) / (double)VoyageTicks;
        double at = dep + Travelled(VoyageFrom, VoyageTo, Missed != 0 ? Math.Max(0, frac) : Math.Clamp(frac, 0, 1)) + Mean(VoyageTo) * (tick - tArr);
        ShipTheta0 = at; ShipThetaTick = tick; ShipParkAu = VoyageTo;
        int place = VoyagePlace;
        VoyagePlace = -1;
        if (place < 0 || place >= Places.Count) { AtPlace = -1; return; }
        double miss = Math.Abs(Wrap(PlaceTheta(place, tick) - at)) * 180 / Math.PI;
        ArrivalMissDeg = miss;
        var p = Places[place];
        if (miss > CaptureDeg)
        {
            AtPlace = -1; PlacesMissed++;
            int c = sim.Causes.Add(CauseKind.Route, $"{Ko.Gwa(p.Name)} {miss:0}° 어긋났다 — 발사 창을 놓쳐 그 궤도를 빈손으로 돈다", tick);
            sim.Facts.Emit(tick, FactKind.Burn, -1, (float)miss, c, other: -100);
            return;
        }
        AtPlace = place; PlacesReached++;
        if (p.Kind == 0) return;
        // 정거장 · 광산: 곁에 떠 있다 (지나는 길의 만남은 이미 했다 — 다시 지나는 곳에서 두 번 만나지 않게)
        if (p.Ref < 31) StationsMet |= 1 << p.Ref;
        var st = T.Space.Stations[p.Ref];
        int cause = sim.Causes.Add(CauseKind.Contact, $"{st.Name}에 닿았다 — {st.Line}", tick);
        int id = AddContact(sim, st.Mine ? ContactKind.Mine : ContactKind.Station, ApproachKm * 1000, 1, 0, 0, 0, 0, 0, cause);
        int k = ContactIndex(id); var cc = Contacts[k]; cc.NameNo = p.Ref; cc.Beacon = 1; Contacts[k] = cc;
    }

    /// <summary>⑤ 별계 지도 (나): 붙은 정거장에서 추진제를 받는다 (주컴퓨터의 요청 — 정거장이 채워 주기로 한 만큼 · 탱크를 넘지 않게). 받은 kg.</summary>
    public double TakeFuel(Sim sim, int id, int cause)
    {
        int k = ContactIndex(id);
        if (k < 0) return 0;
        var c = Contacts[k];
        if (c.State != ContactState.Docked || c.Fuel <= 0) return 0;
        double kg = Math.Min(c.Fuel, Math.Max(0, PropellantStart - PropellantKg));
        PropellantKg += kg; FuelTaken += kg;
        c.Fuel = 0; Contacts[k] = c;
        return kg;
    }
    public double FuelTaken;

    private void SaveMap(StateWriter w)
    {
        w.Arr(PlaceTheta0); w.F64(ShipTheta0); w.I64(ShipThetaTick); w.F64(ShipParkAu); w.I32(AtPlace); w.I32(VoyagePlace); w.I32(PendingPlace); w.F64(ArrivalMissDeg);
        w.I64(PlacesReached); w.I64(PlacesMissed); w.F64(FuelTaken);
    }
    private void LoadMap(StateReader r)
    {
        PlaceTheta0 = r.Arr<double>(); ShipTheta0 = r.F64(); ShipThetaTick = r.I64(); ShipParkAu = r.F64(); AtPlace = r.I32(); VoyagePlace = r.I32(); PendingPlace = r.I32(); ArrivalMissDeg = r.F64();
        PlacesReached = r.I64(); PlacesMissed = r.I64(); FuelTaken = r.F64();
    }
}
