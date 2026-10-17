using ShipEngine;
using static Lab.ShipPlan;

namespace Lab;

/// <summary>
/// 배 일곱 척 (Lab --shipgen [키] [--show]) — 맡은 일 · 사람 수에 맞는 모양과 짜임.
/// 식량: 보관함 하나 250kg (포장 식량 — 상하지 않는다) · 1kg = 포도당 3.3몰 · 한 사람 하루 약 3.6몰(1.1kg).
/// 재배 선반: 4칸 × 6단 = 24m² · 1m²에 하루 0.1몰.
/// </summary>
public static partial class Program
{
    private static readonly (string Key, Func<ShipPlan> Make)[] Designs =
    {
        ("Hwasal", Hwasal), ("Didim", Didim), ("Salpim", Salpim), ("Nareum", Nareum), ("Soedol", Soedol), ("Saetteul", Saetteul), ("Hannuri", Hannuri),
    };

    private static (int, string, string, (string, int)[]) R(int w, string type, string name, params (string, int)[] furn) => (w, type, name, furn);

    /// <summary>연락선 화살호 — 3명 · 60일. 뾰족한 뱃머리 · 뒤로 젖힌 날개(내릴 때 미끄럼판) · 엔진 둘. 착륙한다.</summary>
    private static ShipPlan Hwasal()
    {
        var s = new ShipPlan("Hwasal", "화살호", 60, 26, 3, 400, 100, "연락선 · 3명 · 60일 · 착륙한다") { Robots = 3 };
        int cor = s.Corridor(12, 12, 41, 13, "중앙 통로");
        s.Row(12, 12, 13, 'N', 4, cor,
            R(7, "Reactor", "원자로실", F("ReactorCore"), F("Console")),
            R(5, "Power", "배전실", F("AuxGenerator"), F("PowerPanel"), F("Battery", 2), F("SurgeProtector")),
            R(7, "LifeSupport", "생명유지실", F("OxygenGenerator", 2), F("WaterRecycler"), F("Scrubber"), F("HeatExchanger"), F("CoolantPump", 2)),
            R(8, "Quarters", "침실", F("Bed", 3), F("Shelf")));
        var south = s.Row(12, 12, 13, 'S', 4, cor,
            R(6, "Workshop", "정비실", F("Workbench"), F("Shelf"), F("RobotDock", 3)),
            R(4, "Storage", "식량 창고", F("FoodLocker"), F("OxygenMaskBox")),
            R(5, "Airlock", "에어락", F("SuitLocker", 3)),
            R(4, "Medbay", "의무실", F("MedBed")),
            R(7, "Galley", "주방 겸 식당", F("Table"), F("Stove"), F("Fridge"), F("MealDispenser"), F("Seat", 3)));
        s.SetLinkKind(south[2], "pressure");
        s.Exit(south[2], 'S');
        int eng = s.Rect(3, 8, 11, 17, "Engine", "엔진실", null, F("EngineCore", 2), F("Console"));
        s.Link(eng, cor);
        int br = s.Rect(42, 7, 57, 18, "Bridge", "함교", (x, y) => Math.Abs(y + 0.5 - 13) <= (57.5 - x) * 0.42,
            F("MainComputer"), F("NavComputer"), F("Console", 2), F("SensorArray"), F("CrashSeat", 3));
        s.Link(br, cor);
        s.StrutPoly((3, 7.5), (18, 7.5), (6, 1.5), (2, 1.5));     // 뒤로 젖힌 날개 (위 · 아래)
        s.StrutPoly((3, 18.5), (18, 18.5), (6, 24.5), (2, 24.5));
        return s;
    }

    /// <summary>탐사선 디딤호 — 6명 · 180일. 팔각 몸통 · 대각선 착륙 다리 넷 · 가운데 하강 엔진 · 둘레 통로 · 남쪽 탐사 장비고와 에어락(내리는 길).</summary>
    private static ShipPlan Didim()
    {
        const double c = 31.5;
        var s = new ShipPlan("Didim", "디딤호", 64, 64, 6, 900, 200, "탐사선 · 6명 · 180일 · 착륙한다 · 작은 재배실") { Robots = 3, Drones = 2 };
        bool Body(int x, int y) { double dx = Math.Abs(x + 0.5 - c), dy = Math.Abs(y + 0.5 - c); return dx <= 19 && dy <= 19 && dx + dy <= 30; }
        double M(int x, int y) => Math.Max(Math.Abs(x + 0.5 - c), Math.Abs(y + 0.5 - c));
        int cor = s.Corridor(0, 0, 63, 63, "둘레 통로", (x, y) => M(x, y) >= 8 && M(x, y) < 10);
        // 가운데: 하강 엔진 · 원자로 · 배전 · 정비 (네 조각)
        var core = new (string T, string N, (string, int)[] F)[]
        {
            ("Engine", "엔진실", new[] { F("EngineCore"), F("Console") }),
            ("Reactor", "원자로실", new[] { F("ReactorCore"), F("Console") }),
            ("Power", "배전실", new[] { F("AuxGenerator"), F("PowerPanel"), F("Battery", 2), F("SurgeProtector") }),
            ("Workshop", "정비실", new[] { F("Workbench"), F("Shelf"), F("RobotDock", 3) }),
        };
        for (int q = 0; q < 4; q++)
        {
            int qq = q;
            int r = s.Rect(0, 0, 63, 63, core[q].T, core[q].N, (x, y) => M(x, y) < 8 && (x + 0.5 >= c) == (qq % 2 == 1) && (y + 0.5 >= c) == (qq >= 2), core[q].F);
            s.Link(r, cor);
        }
        // 둘레: 열두 조각 (0도 = 동쪽 · 시계 방향)
        var ring = new (string T, string N, (string, int)[] F)[]
        {
            ("Quarters", "침실 둘", new[] { F("Bed", 3), F("Shelf") }),
            ("Lounge", "휴게실", new[] { F("Treadmill"), F("GameTable"), F("Bookshelf"), F("Seat", 3) }),
            ("Medbay", "의무실", new[] { F("MedBed", 2), F("DiagnosticScanner"), F("Shelf") }),
            ("Gear", "탐사 장비고", new[] { F("Rover"), F("Refinery"), F("Collector"), F("SuitLocker", 6), F("DroneDock", 2), F("Shelf") }),
            ("Galley", "주방", new[] { F("Stove"), F("Fridge", 2), F("Shelf") }),
            ("Mess", "식당", new[] { F("Table", 2), F("MealDispenser"), F("Seat", 6) }),
            ("Hydroponics", "재배실", new[] { F("GrowBed", 2), F("NutrientDoser") }),
            ("Storage", "식량 창고", new[] { F("FoodLocker", 5), F("Shelf") }),
            ("LifeSupport", "생명유지실", new[] { F("OxygenGenerator", 2), F("WaterRecycler"), F("Scrubber", 2), F("Dehumidifier"), F("HeatExchanger"), F("CoolantPump", 2) }),
            ("Bridge", "함교", new[] { F("MainComputer"), F("NavComputer"), F("Console", 2), F("SensorArray"), F("CrashSeat", 6) }),
            ("Comms", "통신실", new[] { F("Console"), F("SensorArray") }),
            ("Quarters", "침실 하나", new[] { F("Bed", 3), F("Shelf") }),
        };
        int gear = -1;
        for (int k = 0; k < 12; k++)
        {
            double a0 = k * 30 - 15;
            int r = s.Rect(0, 0, 63, 63, ring[k].T, ring[k].N, (x, y) => Body(x, y) && M(x, y) >= 10 && InAng(Ang(x + 0.5 - c, y + 0.5 - c), a0, a0 + 30), ring[k].F);
            s.Link(r, cor);
            if (ring[k].T == "Gear") gear = r;
        }
        int air = s.Rect(27, 50, 36, 56, "Airlock", "에어락", null, F("SuitLocker", 2), F("OxygenMaskBox"));
        s.Link(gear, air, kind: "pressure");
        s.Exit(air, 'S');
        foreach (var (sx, sy) in new[] { (1, 1), (-1, 1), (-1, -1), (1, -1) })
        {
            s.StrutLine((c + sx * 15.5, c + sy * 15.5), (c + sx * 22.5, c + sy * 22.5), 2);   // 착륙 다리
            s.StrutDisc(c + sx * 24, c + sy * 24, 2.6);                                        // 발판
        }
        return s;
    }

    /// <summary>연구선 살핌호 — 12명 · 1년 (재배 30%). 긴 몸통 · 줄기 끝 실험실 꼬투리 셋 · 뱃머리 관측 돛대와 관측실 · 뒤쪽 방열판.</summary>
    private static ShipPlan Salpim()
    {
        var s = new ShipPlan("Salpim", "살핌호", 140, 50, 12, 1800, 300, "연구선 · 12명 · 1년 · 재배 30% · 실험실 셋 · 관측 돛대") { Robots = 6, Drones = 2 };
        int cor = s.Corridor(17, 24, 101, 25, "중앙 통로");
        s.Row(17, 24, 25, 'N', 6, cor,
            R(8, "Reactor", "원자로실", F("ReactorCore"), F("Console")),
            R(6, "Power", "배전실", F("AuxGenerator"), F("PowerPanel"), F("Battery", 2), F("SurgeProtector")),
            R(5, "Cooling", "냉각실", F("CoolantPump", 2), F("HeatExchanger")),
            R(10, "LifeSupport", "생명유지실", F("OxygenGenerator", 3), F("WaterRecycler"), F("Scrubber", 2), F("Dehumidifier"), F("HeatExchanger")),
            R(8, "Workshop", "정비실", F("Workbench"), F("Lathe"), F("SolderStation"), F("Shelf"), F("RobotDock", 4)),
            R(2, "Corridor", "", Array.Empty<(string, int)>()),
            R(9, "Quarters", "침실 가", F("Bed", 4), F("Shelf")),
            R(9, "Quarters", "침실 나", F("Bed", 4), F("Shelf")),
            R(2, "Corridor", "", Array.Empty<(string, int)>()),
            R(9, "Quarters", "침실 다", F("Bed", 4), F("Shelf")),
            R(7, "Comms", "통신실", F("Console"), F("SensorArray", 2)));
        var nx = s.RowX.ToList();
        var south = s.Row(17, 24, 25, 'S', 6, cor,
            R(9, "Storage", "식량 창고 하나", F("FoodLocker", 7), F("RobotDock", 2)),
            R(9, "Storage", "식량 창고 둘", F("FoodLocker", 7), F("Shelf")),
            R(11, "Hydroponics", "재배실", F("GrowRack", 6), F("NutrientDoser")),
            R(7, "Galley", "주방", F("Stove"), F("Oven"), F("Fridge", 2), F("CoffeeMachine")),
            R(9, "Mess", "식당", F("Table", 3), F("MealDispenser"), F("Seat", 12)),
            R(2, "Corridor", "", Array.Empty<(string, int)>()),
            R(9, "Lounge", "휴게실", F("GameTable"), F("Bookshelf"), F("Projector"), F("Seat", 4)),
            R(6, "Gym", "운동실", F("Treadmill", 2)),
            R(8, "Medbay", "의무실", F("MedBed", 2), F("DiagnosticScanner"), F("Autoclave"), F("Shelf")),
            R(6, "Airlock", "에어락", F("SuitLocker", 4), F("DroneDock", 2)));
        var sx = s.RowX.ToList();
        s.SetLinkKind(south[9], "pressure");
        s.Exit(south[9], 'S');
        // 실험실 꼬투리: 줄기(통로 가지)를 밖으로 늘이고 그 끝에 실험실
        var pods = new (int X0, int X1, bool North, string Name, (string, int)[] F)[]
        {
            (nx[5].X0, nx[5].X1, true, "물리 실험실", new[] { F("Console", 2), F("Workbench"), F("SensorArray"), F("DiagnosticScanner") }),
            (nx[8].X0, nx[8].X1, true, "생물 실험실", new[] { F("Console"), F("Workbench"), F("Autoclave"), F("DiagnosticScanner"), F("Shelf") }),
            (sx[5].X0, sx[5].X1, false, "표본 실험실", new[] { F("Console"), F("Workbench"), F("SolderStation"), F("Collector"), F("Shelf") }),
        };
        foreach (var p in pods)
        {
            int px = (p.X0 + p.X1) / 2;
            if (p.North) { s.Extend(cor, Box(p.X0, 15, p.X1, 16)); s.Link(s.Rect(px - 6, 4, px + 7, 13, "Lab", p.Name, null, p.F), cor); }
            else { s.Extend(cor, Box(p.X0, 33, p.X1, 35)); s.Link(s.Rect(px - 6, 37, px + 7, 46, "Lab", p.Name, null, p.F), cor); }
        }
        int eng = s.Rect(3, 16, 16, 33, "Engine", "엔진실", null, F("EngineCore", 2), F("Console"));
        s.Link(eng, cor);
        int br = s.Rect(102, 17, 116, 33, "Bridge", "함교", (x, y) => Math.Abs(y + 0.5 - 25) <= (116.5 - x) * 0.62 + 0.6,
            F("MainComputer"), F("NavComputer"), F("Console", 3), F("SensorArray"), F("CrashSeat", 4));
        s.Link(br, cor);
        int mast = s.Corridor(117, 24, 125, 25, "관측 돛대", (x, y) => y == 24 || x == 117);
        s.Link(br, mast);
        int obs = s.Rect(126, 18, 138, 31, "Observatory", "관측실", (x, y) => (x + 0.5 - 132.5) * (x + 0.5 - 132.5) + (y + 0.5 - 24.5) * (y + 0.5 - 24.5) <= 5.6 * 5.6,
            F("SensorArray", 2), F("Console"), F("Seat", 2));
        s.Link(obs, mast);
        foreach (int rx in new[] { 20, 26, 32 })
        {
            s.StrutLine((rx, 14.5), (rx, 3), 1);        // 방열판
            s.StrutLine((rx, 35.5), (rx, 47), 1);
        }
        return s;
    }

    /// <summary>화물선 나름호 — 8명 · 240일. 긴 등뼈 통로 양쪽에 화물칸 여덟 (사이가 떠 있다) · 앞쪽 작은 거주 구역 · 뒤쪽 기관.</summary>
    private static ShipPlan Nareum()
    {
        var s = new ShipPlan("Nareum", "나름호", 152, 46, 8, 1000, 250, "화물선 · 8명 · 240일 · 화물칸 여덟") { Robots = 8, Drones = 1 };
        int cor = s.Corridor(10, 22, 133, 23, "등뼈 통로");
        s.Row(10, 22, 23, 'N', 8, cor,
            R(8, "Reactor", "원자로실", F("ReactorCore"), F("Console")),
            R(6, "Power", "배전실", F("AuxGenerator"), F("PowerPanel"), F("Battery", 2), F("SurgeProtector")));
        s.Row(10, 22, 23, 'S', 8, cor,
            R(8, "LifeSupport", "생명유지실", F("OxygenGenerator", 2), F("WaterRecycler"), F("Scrubber", 2), F("Dehumidifier"), F("HeatExchanger"), F("CoolantPump", 2)),
            R(6, "Workshop", "정비실", F("Workbench"), F("Shelf"), F("RobotDock", 6)));
        int k = 1;
        foreach (int hx in new[] { 29, 47, 65, 83 })
        {
            int n = s.Rect(hx, 11, hx + 13, 21, "Cargo", $"화물칸 {k++}", null, F("CargoCrate", 10), F("CargoNet", 2));
            s.Link(n, cor, kind: "pressure");
            int so = s.Rect(hx, 24, hx + 13, 34, "Cargo", $"화물칸 {k++}", null, F("CargoCrate", 10), F("CargoNet", 2));
            s.Link(so, cor, kind: "pressure");
        }
        s.Row(101, 22, 23, 'N', 6, cor,
            R(8, "Quarters", "침실 가", F("Bed", 4), F("Shelf")),
            R(8, "Quarters", "침실 나", F("Bed", 4), F("Shelf")),
            R(8, "Lounge", "휴게실", F("GameTable"), F("Treadmill"), F("Bookshelf"), F("Seat", 3)),
            R(6, "ComputerRoom", "주컴퓨터실", F("MainComputer"), F("Console")));
        var south = s.Row(101, 22, 23, 'S', 6, cor,
            R(6, "Galley", "주방", F("Stove"), F("Fridge"), F("Shelf")),
            R(6, "Mess", "식당", F("Table", 2), F("MealDispenser"), F("Seat", 8)),
            R(6, "Storage", "식량 창고", F("FoodLocker", 9), F("RobotDock", 2)),
            R(5, "Medbay", "의무실", F("MedBed"), F("DiagnosticScanner"), F("Shelf")),
            R(5, "Airlock", "에어락", F("SuitLocker", 3), F("DroneDock")));
        s.SetLinkKind(south[4], "pressure");
        s.Exit(south[4], 'S');
        int eng = s.Rect(2, 12, 9, 33, "Engine", "엔진실", null, F("EngineCore", 3), F("Console"));
        s.Link(eng, cor);
        int br = s.Rect(134, 14, 149, 31, "Bridge", "함교", (x, y) => Math.Abs(y + 0.5 - 23) <= (149.5 - x) * 0.6 + 0.6,
            F("NavComputer"), F("Console", 2), F("SensorArray"), F("CrashSeat", 4));
        s.Link(br, cor);
        return s;
    }

    /// <summary>채굴선 쇠돌호 — 10명 · 240일 (재배 20%). 넓고 무거운 몸통 · 고리 통로 안에 광석 창고와 정제 공장 · 앞쪽 굴착 팔 · 착륙 다리 여섯.</summary>
    private static ShipPlan Soedol()
    {
        const int o = 7; // 다리 자리
        var s = new ShipPlan("Soedol", "쇠돌호", 94, 72, 10, 1500, 300, "채굴선 · 10명 · 240일 · 재배 20% · 광석 창고 · 정제 공장 · 착륙한다") { Robots = 8, Drones = 4 };
        int cor = s.Corridor(20, 16 + o, 59, 17 + o, "고리 통로");
        s.Extend(cor, Box(20, 38 + o, 59, 39 + o));
        s.Extend(cor, Box(20, 16 + o, 21, 39 + o));
        s.Extend(cor, Box(58, 16 + o, 59, 39 + o));
        int ore = s.Rect(22, 18 + o, 43, 37 + o, "OreHold", "광석 창고", null, F("OreBin", 16));
        int fnd = s.Rect(44, 18 + o, 57, 37 + o, "Foundry", "정제 공장", null, F("Refinery", 2), F("Compactor"), F("Collector"), F("Lathe"), F("Workbench"), F("Console"));
        s.Link(ore, cor); s.Link(fnd, cor); s.Link(ore, fnd);
        s.Row(20, 16 + o, 17 + o, 'N', 10, cor,
            R(8, "Quarters", "침실 가", F("Bed", 5), F("Shelf")),
            R(7, "Quarters", "침실 나", F("Bed", 5), F("Shelf")),
            R(7, "Lounge", "휴게실", F("GameTable"), F("Bookshelf"), F("Projector"), F("Seat", 4)),
            R(6, "Gym", "운동실", F("Treadmill", 2)),
            R(8, "Medbay", "의무실", F("MedBed", 2), F("DiagnosticScanner"), F("Shelf")));
        s.Row(20, 38 + o, 39 + o, 'S', 10, cor,
            R(8, "Galley", "주방", F("Stove"), F("Oven"), F("Fridge", 2), F("Shelf")),
            R(8, "Mess", "식당", F("Table", 3), F("MealDispenser"), F("Seat", 10)),
            R(8, "Storage", "식량 창고", F("FoodLocker", 9), F("RobotDock", 2)),
            R(13, "Hydroponics", "재배실", F("GrowRack", 3), F("NutrientDoser")));
        int pw = s.Rect(8, 16 + o, 19, 23 + o, "Power", "배전실", null, F("AuxGenerator"), F("PowerPanel"), F("Battery", 3), F("SurgeProtector"));
        int ls = s.Rect(8, 24 + o, 19, 31 + o, "LifeSupport", "생명유지실", null, F("OxygenGenerator", 3), F("WaterRecycler"), F("Scrubber", 2), F("Dehumidifier"), F("HeatExchanger", 2));
        int ws = s.Rect(8, 32 + o, 19, 39 + o, "Workshop", "정비실", null, F("Workbench"), F("Lathe"), F("SolderStation"), F("Shelf"), F("RobotDock", 6));
        s.Link(pw, cor); s.Link(ls, cor); s.Link(ws, cor);
        int re = s.Rect(8, 5 + o, 19, 15 + o, "Reactor", "원자로실", null, F("ReactorCore"), F("Console"));
        s.Link(re, pw);
        int co = s.Rect(8, 40 + o, 19, 50 + o, "Cooling", "냉각실", null, F("CoolantPump", 3), F("HeatExchanger"));
        s.Link(co, ws);
        int eng = s.Rect(2, 10 + o, 7, 45 + o, "Engine", "엔진실", null, F("EngineCore", 2), F("Console"));
        s.Link(eng, pw);
        int br = s.Rect(60, 16 + o, 71, 27 + o, "Bridge", "함교", null, F("MainComputer"), F("NavComputer"), F("Console", 2), F("SensorArray"), F("CrashSeat", 4));
        int cm = s.Rect(60, 28 + o, 71, 33 + o, "Comms", "통신실", null, F("Console"), F("SensorArray"));
        int db = s.Rect(60, 34 + o, 71, 39 + o, "DroneBay", "드론 격납고", null, F("DroneDock", 4), F("Shelf"));
        s.Link(br, cor); s.Link(cm, cor); s.Link(db, cor);
        int lab = s.Rect(60, 5 + o, 71, 15 + o, "Lab", "시료 분석실", null, F("Console"), F("Workbench"), F("DiagnosticScanner"), F("SensorArray"));
        s.Link(lab, br);
        int air = s.Rect(60, 40 + o, 71, 50 + o, "Airlock", "광석 에어락", null, F("SuitLocker", 4), F("OxygenMaskBox"));
        s.Link(air, db, kind: "pressure");
        s.Exit(air, 'S');
        s.StrutLine((72.5, 28 + o), (83, 28 + o), 4);   // 굴착 팔
        s.StrutDisc(86.5, 28 + o, 4.6);                 // 굴착 머리
        foreach (int lx in new[] { 13, 37, 61 })
        {
            s.StrutLine((lx + 0.5, 3.5 + o), (lx - 2.5, 2.5), 2);  s.StrutDisc(lx - 3, 2.5, 1.8);
            s.StrutLine((lx + 0.5, 52.5 + o), (lx - 2.5, 66), 2);  s.StrutDisc(lx - 3, 67, 1.8);
        }
        return s;
    }

    /// <summary>이민선 새뜰호 — 40명 · 1년 (재배 60%). 두 줄 통로 · 북쪽 침실 여덟 · 가운데 공동 구역 · 남쪽 재배 갑판 셋과 셔틀 격납고.</summary>
    private static ShipPlan Saetteul()
    {
        var s = new ShipPlan("Saetteul", "새뜰호", 134, 62, 40, 6000, 800, "이민선 · 40명 · 1년 · 재배 60% · 셔틀") { Robots = 16, Drones = 4 };
        int cor = s.Corridor(16, 18, 108, 19, "북쪽 통로");
        s.Extend(cor, Box(16, 42, 108, 43));
        s.Extend(cor, Box(16, 10, 17, 51));
        var north = new List<(int, string, string, (string, int)[])>();
        string[] q = { "가", "나", "다", "라", "마", "바", "사", "아" };
        foreach (var n in q) north.Add(R(7, "Quarters", $"침실 {n}", F("Bed", 5), F("Shelf")));
        north.Add(R(9, "Lounge", "휴게실", F("GameTable"), F("Bookshelf"), F("Projector"), F("Aquarium"), F("Seat", 4)));
        north.Add(R(7, "Chapel", "추모실", F("MemorialWall"), F("Seat", 4)));
        north.Add(R(9, "School", "배움방", F("Table", 2), F("Bookshelf"), F("Seat", 6)));
        s.Row(18, 18, 19, 'N', 7, cor, north.ToArray());
        void Middle(int y0, int y1, params (int W, string T, string N, (string, int)[] F)[] rs)
        {
            int x = 18;
            for (int i = 0; i < rs.Length; i++)
            {
                int x1 = x + rs[i].W - 1 + (i > 0 ? 1 : 0);
                s.Link(s.Rect(x, y0, x1, y1, rs[i].T, rs[i].N, null, rs[i].F), cor);
                x = x1 + 1;
            }
            if (x != 109) throw new InvalidOperationException($"새뜰호 가운데 줄 끝이 {x - 1} (108이어야)");
        }
        Middle(20, 30,
            R(8, "Galley", "주방 하나", F("Stove", 2), F("Oven"), F("Fridge", 3), F("Shelf")),
            R(18, "Mess", "식당 하나", F("Table", 4), F("MealDispenser", 2), F("Seat", 16)),
            R(10, "Medbay", "의무실", F("MedBed", 3), F("DiagnosticScanner"), F("Autoclave"), F("Shelf")),
            R(8, "Gym", "운동실", F("Treadmill", 3)),
            R(16, "Workshop", "정비실", F("Workbench", 2), F("Lathe"), F("SolderStation"), F("Shelf"), F("RobotDock", 10)),
            R(6, "Comms", "통신실", F("Console"), F("SensorArray", 2)),
            R(10, "Lounge", "작은 휴게실", F("GameTable"), F("Bookshelf"), F("Seat", 4)),
            R(8, "ComputerRoom", "주컴퓨터실", F("MainComputer"), F("Console", 2)));
        Middle(31, 41,
            R(10, "Storage", "식량 창고 하나", F("FoodLocker", 13), F("Shelf")),
            R(10, "Storage", "식량 창고 둘", F("FoodLocker", 13), F("RobotDock", 6)),
            R(12, "LifeSupport", "생명유지실 하나", F("OxygenGenerator", 5), F("WaterRecycler", 2), F("Scrubber", 3), F("Dehumidifier", 2), F("HeatExchanger", 2)),
            R(12, "LifeSupport", "생명유지실 둘", F("OxygenGenerator", 4), F("WaterRecycler"), F("Scrubber", 2), F("Dehumidifier", 2), F("HeatExchanger", 2)),
            R(8, "Galley", "주방 둘", F("Stove", 2), F("Fridge", 3), F("Shelf")),
            R(18, "Mess", "식당 둘", F("Table", 4), F("MealDispenser", 2), F("Seat", 16)),
            R(15, "Cooling", "냉각실", F("LargeCoolantPump"), F("CoolantPump", 2), F("HeatExchanger", 2)));
        var south = s.Row(18, 42, 43, 'S', 11, cor,
            R(16, "Hydroponics", "재배 갑판 하나", F("GrowRack", 13), F("NutrientDoser")),
            R(16, "Hydroponics", "재배 갑판 둘", F("GrowRack", 12), F("NutrientDoser")),
            R(16, "Hydroponics", "재배 갑판 셋", F("GrowRack", 12), F("NutrientDoser")),
            R(16, "Hangar", "셔틀 격납고", F("Shuttle"), F("SuitLocker", 4), F("DroneDock", 4)),
            R(6, "Airlock", "에어락", F("SuitLocker", 4), F("OxygenMaskBox")));
        s.SetLinkKind(south[3], "pressure"); s.SetLinkKind(south[4], "pressure");
        s.Exit(south[3], 'S'); s.Exit(south[4], 'S');
        s.Link(s.Rect(3, 10, 15, 21, "Reactor", "원자로실", null, F("ReactorCore", 2), F("Console")), cor);
        s.Link(s.Rect(3, 22, 15, 37, "Engine", "엔진실", null, F("EngineCore", 3), F("Console")), cor);
        s.Link(s.Rect(3, 38, 15, 51, "Power", "배전실", null, F("AuxGenerator", 2), F("PowerPanel", 2), F("Battery", 4), F("SurgeProtector"), F("CapacitorBank")), cor);
        int br = s.Rect(109, 10, 128, 55, "Bridge", "함교", (x, y) => Math.Abs(y + 0.5 - 31) <= (128.5 - x) * 1.25 + 1,
            F("NavComputer"), F("Console", 4), F("SensorArray", 2), F("CrashSeat", 6));
        s.Link(br, cor, (108, 18.5)); s.Link(br, cor, (108, 42.5));
        return s;
    }

    /// <summary>세대선 한누리호 — 100명 · 먹을 것을 다 기른다 (+ 90일 비상 식량). 도는 고리(안쪽 침실 · 바깥 재배 갑판) · 바퀴살 넷 · 가운데 축(엔진 · 원자로 · 함교 · 셔틀).</summary>
    private static ShipPlan Hannuri()
    {
        const double c = 71;
        var s = new ShipPlan("Hannuri", "한누리호", 142, 142, 100, 15000, 2000, "세대선 · 100명 · 다 기른다 · 90일 비상 식량 · 도는 고리 · 셔틀") { Robots = 36, Drones = 4 };
        double Rr(int x, int y) => Math.Sqrt((x + 0.5 - c) * (x + 0.5 - c) + (y + 0.5 - c) * (y + 0.5 - c));
        double A(int x, int y) => Ang(x + 0.5 - c, y + 0.5 - c);
        bool Spoke(int x, int y) => Math.Abs(x + 0.5 - c) < 1 || Math.Abs(y + 0.5 - c) < 1;
        int cor = s.Corridor(0, 0, 141, 141, "고리 통로", (x, y) => Rr(x, y) >= 50 && Rr(x, y) < 52.5);
        s.Extend(cor, Box(0, 0, 141, 141, (x, y) => Spoke(x, y) && Rr(x, y) >= 7.5 && Rr(x, y) < 50));
        // 가운데 축
        s.Link(s.Rect(0, 0, 141, 141, "Engine", "엔진실", (x, y) => Rr(x, y) < 6, F("EngineCore", 2), F("Console")), cor);
        var hub = new (double A0, double R0, double R1, string T, string N, (string, int)[] F)[]
        {
            (270, 13, 20, "Bridge", "함교", new[] { F("MainComputer"), F("NavComputer"), F("Console", 4), F("SensorArray", 2), F("CrashSeat", 4) }),
            (270, 7.5, 13, "Comms", "통신실", new[] { F("Console", 2), F("SensorArray") }),
            (180, 13, 20, "Reactor", "원자로실", new[] { F("LargeReactorCore"), F("ReactorCore"), F("Console") }),
            (180, 7.5, 13, "Power", "배전실", new[] { F("AuxGenerator", 2), F("PowerPanel", 2), F("Battery", 4), F("SurgeProtector"), F("CapacitorBank") }),
            (90, 13, 20, "Cooling", "냉각실", new[] { F("LargeCoolantPump", 4), F("CoolantPump", 2), F("HeatExchanger", 2) }),
            (90, 7.5, 13, "Workshop", "축 정비실", new[] { F("Workbench"), F("Lathe"), F("SolderStation"), F("RobotDock", 10) }),
            (0, 13, 20, "Hangar", "셔틀 격납고", new[] { F("Shuttle"), F("SuitLocker", 6), F("DroneDock", 4) }),
            (0, 7.5, 13, "Storage", "부품 창고", new[] { F("Shelf", 3), F("RobotDock", 4) }),
        };
        foreach (var h in hub)
        {
            int r = s.Rect(0, 0, 141, 141, h.T, h.N, (x, y) => !Spoke(x, y) && Rr(x, y) >= h.R0 && Rr(x, y) < h.R1 && InAng(A(x, y), h.A0, h.A0 + 90), h.F);
            s.Link(r, cor);
            if (h.T == "Hangar") s.Exit(r, 'S');
        }
        // 고리: 사분면마다 여섯 조각 (안쪽 · 바깥쪽 띠) — 0도 = 동쪽 · 시계 방향
        var quarters = Enumerable.Range(0, 17).Select(i => ("Quarters", $"침실 {i + 1}", new[] { F("Bed", i == 16 ? 4 : 6), F("Shelf") })).ToList();
        int qi = 0;
        (string, string, (string, int)[]) Q() => quarters[qi++];
        var inner = new[]
        {
            new[] { Q(), Q(), Q(), Q(), Q(), ("Medbay", "의무실 하나", new[] { F("MedBed", 3), F("DiagnosticScanner"), F("Shelf") }) },
            new[] { Q(), Q(), ("School", "배움방", new[] { F("Table", 2), F("Bookshelf"), F("Seat", 8) }), Q(), Q(), ("Gym", "운동실", new[] { F("Treadmill", 4) }) },
            new[] { Q(), Q(), ("Chapel", "추모실", new[] { F("MemorialWall"), F("Seat", 6) }), Q(), Q(), ("Lounge", "휴게실 하나", new[] { F("GameTable"), F("Bookshelf"), F("Projector"), F("Seat", 4) }) },
            new[] { Q(), Q(), ("Medbay", "의무실 둘", new[] { F("MedBed", 3), F("DiagnosticScanner"), F("Autoclave") }), Q(), Q(), ("Lounge", "휴게실 둘", new[] { F("GameTable"), F("Aquarium"), F("Seat", 4) }) },
        };
        (string, string, (string, int)[]) Farm(int n) => ("Hydroponics", $"재배 갑판 {n}", new[] { Fill("GrowRack", 16), F("NutrientDoser") });
        var outer = new[]
        {
            new[] { Farm(1), Farm(2), ("Galley", "주방 하나", new[] { F("Stove", 2), F("Oven"), F("Fridge", 3), F("Shelf") }), ("Mess", "식당 하나", new[] { F("Table", 5), F("MealDispenser", 2), F("Seat", 20) }), Farm(3), ("Airlock", "에어락", new[] { F("SuitLocker", 6), F("OxygenMaskBox") }) },
            new[] { Farm(4), Farm(5), ("Storage", "식량 창고 하나", new[] { F("FoodLocker", 14), F("RobotDock", 4) }), ("LifeSupport", "생명유지실 하나", new[] { F("OxygenGenerator", 7), F("WaterRecycler", 2), F("Scrubber", 4), F("Dehumidifier", 3), F("HeatExchanger", 2) }), Farm(6), ("Workshop", "정비실", new[] { F("Workbench", 2), F("Lathe"), F("SolderStation"), F("Shelf"), F("RobotDock", 12) }) },
            new[] { Farm(7), Farm(8), ("Galley", "주방 둘", new[] { F("Stove", 2), F("Oven"), F("Fridge", 3), F("Shelf") }), ("Mess", "식당 둘", new[] { F("Table", 5), F("MealDispenser", 2), F("Seat", 20) }), Farm(9), ("LifeSupport", "생명유지실 둘", new[] { F("OxygenGenerator", 7), F("WaterRecycler", 2), F("Scrubber", 4), F("Dehumidifier", 3), F("HeatExchanger", 2) }) },
            new[] { Farm(10), Farm(11), ("Storage", "식량 창고 둘", new[] { F("FoodLocker", 13), F("RobotDock", 3) }), ("Storage", "식량 창고 셋", new[] { F("FoodLocker", 13), F("RobotDock", 3) }), Farm(12), ("LifeSupport", "생명유지실 셋", new[] { F("OxygenGenerator", 7), F("WaterRecycler", 2), F("Scrubber", 5), F("Dehumidifier", 3), F("HeatExchanger", 2) }) },
        };
        for (int qd = 0; qd < 4; qd++)
            for (int k = 0; k < 6; k++)
            {
                double a0 = qd * 90 + k * 15;
                var (ti, ni, fi) = inner[qd][k];
                s.Link(s.Rect(0, 0, 141, 141, ti, ni, (x, y) => !Spoke(x, y) && Rr(x, y) >= 42 && Rr(x, y) < 50 && InAng(A(x, y), a0, a0 + 15), fi), cor);
                var (to, no, fo) = outer[qd][k];
                int r = s.Rect(0, 0, 141, 141, to, no, (x, y) => Rr(x, y) >= 52.5 && Rr(x, y) < 64 && InAng(A(x, y), a0, a0 + 15), fo);
                s.Link(r, cor, kind: to == "Airlock" ? "pressure" : "normal");
                if (to == "Airlock") s.Exit(r, 'S');
            }
        return s;
    }

    /// <summary>모두 쓰는 방 (휴게실 — 없으면 식당 · 주방) — 배를 가리지 않는 시험용.</summary>
    internal static RoomDef CommonRoom(Layout L) => L.Rooms.FirstOrDefault(r => r.Type == "Lounge") ?? L.Rooms.First(r => r.Type is "Mess" or "Galley");

    /// <summary>외판에 닿은 방 (종류 순서대로 찾는다) — 외판 구멍 시험용.</summary>
    internal static RoomDef HullRoom(Layout L, params string[] types)
    {
        bool OnHull(RoomDef r) => r.Cells.Any(c => { int x = L.X(c), y = L.Y(c); return new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Any(d => L.In(x + d.Item1, y + d.Item2) && L.Hull[L.Index(x + d.Item1, y + d.Item2)]); });
        foreach (var t in types) { var r = L.Rooms.FirstOrDefault(r => r.Type == t && OnHull(r)); if (r != null) return r; }
        return L.Rooms.First(OnHull);
    }

    // ───────────── 생성 명령 ─────────────

    private const double FoodKgLocker = 250, MolPerKg = 3.33, MolPerPersonDay = 3.6, RackM2 = 24, MolPerM2Day = 0.1;

    private static void ShipGen(string[] args)
    {
        string? only = args.SkipWhile(a => a != "--shipgen").Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
        bool show = args.Contains("--show");
        string dir = FindShipDir();
        foreach (var (key, make) in Designs)
        {
            if (only != null && !key.Equals(only, StringComparison.OrdinalIgnoreCase)) continue;
            var s = make();
            try { s.Build(); }
            catch (InvalidOperationException) { if (show) Console.WriteLine(s.Show()); throw; }
            string json = s.Json();
            var L = Layout.FromJson(json, Tables.Default);
            var sim = ShipWorld.Create(L, 1); // 엔진이 띄울 수 있나 (장 · 망 · 마음 · 사람)
            sim.Get<Mind>().Crew(sim, s.Crew, s.Robots, s.Drones);
            sim.Run(50);
            if (show) Console.WriteLine(s.Show() + "\n");
            Console.WriteLine(Summary(s));
            if (!show) File.WriteAllText(Path.Combine(dir, s.Key.ToLowerInvariant() + ".json"), json);
        }
    }

    private static string Summary(ShipPlan s)
    {
        var c = s.Furniture.GroupBy(f => f.Type).ToDictionary(g => g.Key, g => g.Count());
        int n(string t) => c.GetValueOrDefault(t);
        double foodKg = n("FoodLocker") * FoodKgLocker + n("Fridge") * 40 + n("MealDispenser") * 20;
        double growM2 = s.Furniture.Where(f => f.Type == "GrowRack").Sum(f => f.Cells.Count * 6) + s.Furniture.Where(f => f.Type is "GrowBed" or "PlantWall").Sum(f => f.Cells.Count);
        double need = s.Crew * MolPerPersonDay, grown = growM2 * MolPerM2Day;
        string days = need > grown ? $"{foodKg * MolPerKg / (need - grown):0}일" : "끝없이";
        var devs = Tables.Default;
        double kw = s.Furniture.Sum(f => devs.Dev(f.Type) is { } d && d.On && d.Role is not ("reactor" or "aux" or "battery") ? d.Kw : 0);
        double gen = s.Furniture.Sum(f => devs.Dev(f.Type) is { Role: "reactor" } d ? d.Gen : 0);
        return $"{s.Key,-10} {s.Name} {s.W}×{s.H} · 방 {s.Rooms.Count} · 문 {s.Doors.Count} · 설비 {s.Furniture.Count} · 사람 {s.Crew} · 로봇 {s.Robots}"
             + $" · 식량 {foodKg:0}kg → {days} · 재배 {growM2:0}m² ({grown / need * 100:0}%) · 전력 {kw:0}/{gen:0}kW · 물 {s.WaterL:0}L";
    }

    private static string FindShipDir()
    {
        for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d != null; d = d.Parent)
        {
            string data = Path.Combine(d.FullName, "engine", "data");
            if (Directory.Exists(data)) return Directory.CreateDirectory(Path.Combine(data, "ships")).FullName;
        }
        throw new DirectoryNotFoundException("engine/data 를 찾지 못했다 (저장소 안에서 돌린다)");
    }
}
