using System;
using System.Collections.Generic;
using ShipSim.Core;

namespace ShipSim.Art;

// 도트 설비 · 가구 — 위에서 비스듬히 본 시점: 윗면은 밝게, 남쪽 앞면이 보이고, 키 큰 것은 칸 위로 솟는다.
//   137종을 원형 틀 13가지 + 종류마다 몸 색 · 강조 색 · 부품 표로 그린다 (부품: 화면 · 통풍구 · 계기 · 불빛 · 문 · 서랍 · 창 · 팬 · 관 ·
//   십자 · 위험 띠 · 손잡이 · 코일 · 바늘 계기 · 자판 · 공구 · 물건 · 병 · 책 · 식물 · 물 · 렌즈 · 빛 · 김 · 화구 · 전선 · 차단기 · 옷 · 물고기 · 벨트 · 카드).
//   몸체(Body)는 구워 두는 그림, 움직이는 부분(Life)은 따로 겹친다 — 화면 글자 · 깜빡이는 불빛 · 도는 팬 · 단 화구 · 출렁이는 물 · 노심 빛 · 바늘.
//   상태: 켜짐 · 꺼짐(불빛 · 화면이 꺼짐) · 고장(빨간 불빛 · 깨진 화면 · 멈춘 팬). 낡음 단계 · 등급(Mk1 땜질 · Mk3 매끈한 테)은 몸체에.

public enum FixArch : byte { Box, Bed, Seat, Table, Rack, Planter, Tank, Pad, Reactor, Engine, Computer, Dish, Wall }

[Flags]
public enum FixKit : ulong
{
    None = 0,
    Screen = 1UL << 0, Vents = 1UL << 1, Dials = 1UL << 2, Leds = 1UL << 3, Doors = 1UL << 4, Drawer = 1UL << 5, Window = 1UL << 6, Fan = 1UL << 7,
    Pipes = 1UL << 8, Cross = 1UL << 9, Hazard = 1UL << 10, Handle = 1UL << 11, Coil = 1UL << 12, Gauge = 1UL << 13, Keys = 1UL << 14, Tools = 1UL << 15,
    Items = 1UL << 16, Bottles = 1UL << 17, Books = 1UL << 18, Plants = 1UL << 19, Water = 1UL << 20, Lens = 1UL << 21, Glow = 1UL << 22, Steam = 1UL << 23,
    Burners = 1UL << 24, Cables = 1UL << 25, Breakers = 1UL << 26, Clothes = 1UL << 27, Fish = 1UL << 28, Belt = 1UL << 29, Cards = 1UL << 30, Med = 1UL << 31,
    Wheels = 1UL << 32, Bars = 1UL << 33, Spray = 1UL << 34, Lamp = 1UL << 35, Arm = 1UL << 36, Tube = 1UL << 37, Cat = 1UL << 38, Note = 1UL << 39,
    Cells = 1UL << 40, Suit = 1UL << 41,
}

/// <summary>설비 한 종류의 그림 사양: 틀 · 몸 색 · 강조 색 · 부품 · 칸 위로 솟는 높이 · 앞면 높이.</summary>
public readonly record struct FixSpec(FixArch Arch, int Body, int Accent, FixKit Kit, int Rise, int Front);

public enum FixState : byte { On, Off, Broken }

public static class DotMachines
{
    public const int T = 32;

    private static readonly Dictionary<FurnitureType, FixSpec> Table = Build();

    public static FixSpec Spec(FurnitureType t) => Table.TryGetValue(t, out var s) ? s : new FixSpec(FixArch.Box, Ramps.Steel, Ramps.Cyan, FixKit.Leds | FixKit.Vents, 6, 12);

    /// <summary>칸 위로 솟는 높이 (그리는 자리 = 맨 위 칸 - 이만큼).</summary>
    public static int RiseOf(FurnitureType t) => Spec(t).Rise;

    private static FixSpec S(FixArch a, int body, int accent, FixKit kit, int rise = 6, int front = 12) => new(a, body, accent, kit, rise, front);

    private static Dictionary<FurnitureType, FixSpec> Build()
    {
        int St = Ramps.Steel, Pa = Ramps.Panel, Wh = Ramps.SuitWhite, Wd = Ramps.Wood, Hz = Ramps.Hazard, Cy = Ramps.Cyan, Ld = Ramps.Led, Al = Ramps.Alarm;
        int Bl = Ramps.ClothBlue, Gr = Ramps.ClothGreen, Rd = Ramps.ClothRed, Or = Ramps.ClothOrange, Pu = Ramps.ClothPurple, Te = Ramps.ClothTeal, Br = Ramps.ClothBrown, Nv = Ramps.ClothNavy, Sw = Ramps.SteelWarm;
        const FixKit sc = FixKit.Screen, ve = FixKit.Vents, di = FixKit.Dials, le = FixKit.Leds, dr = FixKit.Doors, dw = FixKit.Drawer, wi = FixKit.Window, fa = FixKit.Fan, pi = FixKit.Pipes,
            cr = FixKit.Cross, hz = FixKit.Hazard, ha = FixKit.Handle, co = FixKit.Coil, ga = FixKit.Gauge, ke = FixKit.Keys, to = FixKit.Tools, it = FixKit.Items, bo = FixKit.Bottles,
            bk = FixKit.Books, pl = FixKit.Plants, wa = FixKit.Water, ln = FixKit.Lens, gl = FixKit.Glow, stm = FixKit.Steam, bu = FixKit.Burners, ca = FixKit.Cables, brk = FixKit.Breakers,
            cl = FixKit.Clothes, fi = FixKit.Fish, be = FixKit.Belt, cd = FixKit.Cards, md = FixKit.Med, wh = FixKit.Wheels, ba = FixKit.Bars, sp = FixKit.Spray, la = FixKit.Lamp,
            ar = FixKit.Arm, tu = FixKit.Tube, ct = FixKit.Cat, no = FixKit.Note, ce = FixKit.Cells, su = FixKit.Suit;
        var t = new Dictionary<FurnitureType, FixSpec>
        {
            // 잠 · 식사 · 생활
            [FurnitureType.Bed] = S(FixArch.Bed, St, Nv, FixKit.None, 2, 4),
            [FurnitureType.Cot] = S(FixArch.Bed, Br, Sw, FixKit.None, 1, 3),
            [FurnitureType.MedBed] = S(FixArch.Bed, Wh, Cy, md | sc | le, 4, 4),
            [FurnitureType.OperatingTable] = S(FixArch.Bed, St, Cy, md | la, 4, 4),
            [FurnitureType.Seat] = S(FixArch.Seat, Nv, Pa, FixKit.None, 10, 6),
            [FurnitureType.CrashSeat] = S(FixArch.Seat, Or, Pa, hz, 12, 6),
            [FurnitureType.Table] = S(FixArch.Table, Wd, Wh, it, 2, 5),
            [FurnitureType.GameTable] = S(FixArch.Table, Gr, Wd, cd, 2, 5),
            [FurnitureType.Workbench] = S(FixArch.Table, St, Hz, to | it, 4, 8),
            [FurnitureType.PartTestBench] = S(FixArch.Table, St, Cy, to | sc | ca, 6, 8),
            [FurnitureType.SolderStation] = S(FixArch.Table, Pa, Hz, to | stm | ca, 4, 8),
            [FurnitureType.MealDispenser] = S(FixArch.Box, St, Or, sc | ha | le, 10, 16),
            [FurnitureType.Stove] = S(FixArch.Box, St, Al, bu | di | dr | stm, 4, 14),
            [FurnitureType.Oven] = S(FixArch.Box, St, Or, wi | di | ha, 6, 16),
            [FurnitureType.BreadOven] = S(FixArch.Box, Br, Or, wi | ha | stm, 8, 16),
            [FurnitureType.Fridge] = S(FixArch.Box, Wh, Cy, dr | ha | le, 14, 22),
            [FurnitureType.IceMaker] = S(FixArch.Box, Wh, Cy, dw | le, 10, 16),
            [FurnitureType.CoffeeMachine] = S(FixArch.Box, Pa, Br, sc | stm | le, 10, 16),
            [FurnitureType.DishWasher] = S(FixArch.Box, Wh, Cy, wi | ha | le, 6, 14),
            [FurnitureType.WashingMachine] = S(FixArch.Box, Wh, Bl, wi | di, 8, 16),
            [FurnitureType.SpiceRack] = S(FixArch.Rack, Wd, Rd, bo, 12, 18),
            [FurnitureType.Fermenter] = S(FixArch.Tank, Wd, Br, ga | bo, 12, 18),
            [FurnitureType.GreaseTrap] = S(FixArch.Box, Sw, Br, ve | ha, 2, 8),
            [FurnitureType.Compactor] = S(FixArch.Box, Hz, Pa, hz | dr | le, 10, 16),
            [FurnitureType.Composter] = S(FixArch.Planter, Br, Gr, ve, 4, 10),
            [FurnitureType.Bookshelf] = S(FixArch.Rack, Wd, Rd, bk, 14, 20),
            [FurnitureType.Aquarium] = S(FixArch.Tank, Wd, Cy, wa | fi, 10, 18),
            [FurnitureType.Treadmill] = S(FixArch.Table, Pa, Gr, be | sc, 8, 6),
            [FurnitureType.Projector] = S(FixArch.Box, Pa, Cy, ln | le, 4, 10),
            [FurnitureType.MusicCorner] = S(FixArch.Table, Wd, Or, it | no, 6, 6),
            [FurnitureType.CatTower] = S(FixArch.Rack, Br, Sw, ct, 16, 6),
            [FurnitureType.PlantWall] = S(FixArch.Planter, Wd, Gr, pl, 14, 20),
            [FurnitureType.PlantRack] = S(FixArch.Rack, St, Gr, pl, 12, 18),
            [FurnitureType.ClothesRack] = S(FixArch.Rack, St, Pu, cl, 14, 6),
            [FurnitureType.SewingMachine] = S(FixArch.Table, Wh, Rd, it | la, 6, 6),
            [FurnitureType.MemorialWall] = S(FixArch.Wall, Wd, Hz, no | la, 12, 16),
            [FurnitureType.MeetingBoard] = S(FixArch.Wall, Wh, Bl, no, 12, 18),
            [FurnitureType.BlackoutCurtain] = S(FixArch.Wall, Nv, Pa, cl, 14, 20),
            [FurnitureType.NoiseDamper] = S(FixArch.Wall, Pa, Gr, ve, 10, 16),
            [FurnitureType.WhiteNoise] = S(FixArch.Box, Wh, Cy, ve | le, 2, 8),
            // 보관 · 정비
            [FurnitureType.Shelf] = S(FixArch.Rack, St, Hz, it, 14, 20),
            [FurnitureType.SuitLocker] = S(FixArch.Box, Pa, Or, su | ha | le, 16, 24),
            [FurnitureType.SupplyCache] = S(FixArch.Box, Al, Wh, cr | ha | dr, 6, 14),
            [FurnitureType.ToolWall] = S(FixArch.Wall, Pa, Hz, to, 12, 18),
            [FurnitureType.MaintCart] = S(FixArch.Table, Hz, St, to | wh, 6, 8),
            [FurnitureType.Hoist] = S(FixArch.Box, Hz, St, ar | hz, 18, 8),
            [FurnitureType.Lathe] = S(FixArch.Table, St, Gr, to | co | sc, 6, 10),
            [FurnitureType.Fabricator] = S(FixArch.Box, Pa, Cy, sc | wi | le | ve, 12, 18),
            [FurnitureType.CargoNet] = S(FixArch.Rack, Br, Hz, it | ba, 10, 6),
            [FurnitureType.MagBootRack] = S(FixArch.Rack, Pa, Hz, it, 10, 14),
            [FurnitureType.HeatSuitRack] = S(FixArch.Box, St, Or, su | ha, 16, 24),
            [FurnitureType.SuitDryer] = S(FixArch.Box, Pa, Or, fa | ve | le, 12, 18),
            [FurnitureType.OxygenMaskBox] = S(FixArch.Wall, Hz, Cy, dr | ha, 8, 12),
            [FurnitureType.FireBlanket] = S(FixArch.Wall, Al, Wh, ha, 8, 12),
            [FurnitureType.EyeWash] = S(FixArch.Wall, Gr, Cy, sp | wa, 8, 12),
            [FurnitureType.GrabRail] = S(FixArch.Wall, Hz, St, ba, 6, 6),
            [FurnitureType.EmergencyLight] = S(FixArch.Wall, Pa, Al, la | le, 6, 8),
            // 동력 · 냉각 · 감시
            [FurnitureType.ReactorCore] = S(FixArch.Reactor, St, Cy, gl | pi | hz, 10, 16),
            [FurnitureType.EngineCore] = S(FixArch.Engine, St, Or, gl | pi, 8, 16),
            [FurnitureType.AuxGenerator] = S(FixArch.Box, Hz, Al, fa | ve | ga | hz | pi, 10, 16),
            [FurnitureType.Battery] = S(FixArch.Box, Pa, Ld, ce | hz | ca, 10, 22),
            [FurnitureType.CapacitorBank] = S(FixArch.Box, Pa, Hz, co | le | hz, 10, 16),
            [FurnitureType.PowerPanel] = S(FixArch.Wall, St, Hz, brk | le | hz | ca, 10, 18),
            [FurnitureType.LedPanel] = S(FixArch.Wall, Pa, Ld, le | sc, 6, 12),
            [FurnitureType.SurgeProtector] = S(FixArch.Wall, Pa, Hz, le | ca, 6, 10),
            [FurnitureType.CoolantPump] = S(FixArch.Tank, Bl, Cy, fa | pi | ga, 10, 18),
            [FurnitureType.HeatExchanger] = S(FixArch.Box, St, Bl, co | pi | ve, 8, 14),
            [FurnitureType.VibrationMonitor] = S(FixArch.Box, Pa, Ld, sc | ca | le, 4, 10),
            [FurnitureType.ThermalCamera] = S(FixArch.Dish, Pa, Al, ln | le, 10, 8),
            [FurnitureType.LeakDetector] = S(FixArch.Box, Wh, Cy, ga | le | tu, 4, 10),
            [FurnitureType.CalibrationRig] = S(FixArch.Table, St, Cy, di | sc | ca, 6, 8),
            [FurnitureType.DiagnosticScanner] = S(FixArch.Box, Wh, Cy, sc | ln | le, 8, 14),
            [FurnitureType.ReactorSimulator] = S(FixArch.Box, Pa, Cy, sc | ke | le | gl, 8, 14),
            // 생명 유지 · 재배 · 위생
            [FurnitureType.OxygenGenerator] = S(FixArch.Tank, Wh, Cy, wi | ga | pi | le, 12, 18),
            [FurnitureType.WaterRecycler] = S(FixArch.Tank, Te, Cy, wi | pi | ga | le, 12, 18),
            [FurnitureType.Scrubber] = S(FixArch.Box, St, Gr, fa | ve | le, 10, 16),
            [FurnitureType.AirPurifier] = S(FixArch.Box, Wh, Cy, fa | ve | le, 10, 16),
            [FurnitureType.Dehumidifier] = S(FixArch.Tank, Wh, Bl, ve | wa | le, 10, 16),
            [FurnitureType.GreywaterFilter] = S(FixArch.Tank, Te, Br, pi | ga, 10, 16),
            [FurnitureType.GrowBed] = S(FixArch.Planter, Wd, Gr, pl | tu, 4, 8),
            [FurnitureType.NutrientDoser] = S(FixArch.Tank, Wh, Gr, bo | tu | le, 10, 14),
            [FurnitureType.InsectFarm] = S(FixArch.Box, Wd, Gr, wi | ve, 8, 14),
            [FurnitureType.PestTrap] = S(FixArch.Box, Hz, Pa, la, 2, 6),
            [FurnitureType.DeconShower] = S(FixArch.Pad, Wh, Cy, sp | wa, 18, 6),
            [FurnitureType.Autoclave] = S(FixArch.Box, Wh, Cy, wi | ga | stm | le, 8, 16),
            [FurnitureType.LabStill] = S(FixArch.Table, Wh, Gr, bo | tu | stm, 8, 8),
            // 의료
            [FurnitureType.SurgicalLamp] = S(FixArch.Dish, Wh, Hz, la, 18, 6),
            [FurnitureType.AnesthesiaMachine] = S(FixArch.Box, Wh, Cy, sc | tu | wh | ga, 12, 18),
            [FurnitureType.BloodFridge] = S(FixArch.Box, Wh, Al, dr | wi | cr | le, 14, 22),
            [FurnitureType.MedCabinet] = S(FixArch.Box, Wh, Al, cr | dr | ha, 12, 18),
            [FurnitureType.Dialyzer] = S(FixArch.Box, Wh, Rd, sc | tu | wh | le, 12, 18),
            [FurnitureType.Ecmo] = S(FixArch.Box, Wh, Rd, sc | tu | wh | ga, 10, 16),
            [FurnitureType.HeartPump] = S(FixArch.Box, Wh, Rd, sc | tu | le, 8, 14),
            [FurnitureType.OrganCooler] = S(FixArch.Box, Wh, Cy, wi | cr | le, 6, 12),
            [FurnitureType.BioPrinter] = S(FixArch.Box, Wh, Pu, wi | ar | sc | le, 12, 18),
            [FurnitureType.NegPressure] = S(FixArch.Box, Wh, Al, fa | ve | hz | le, 10, 16),
            [FurnitureType.SurgicalArm] = S(FixArch.Box, Wh, Cy, ar | le, 18, 8),
            [FurnitureType.ArcadeCabinet] = S(FixArch.Box, Pu, Ld, sc | ke | le | gl, 14, 16), // v23 오락기
            [FurnitureType.VrChair] = S(FixArch.Seat, Nv, Cy, sc | ca | gl, 12, 6), // v23 가상 고향 의자
            [FurnitureType.CarbonReclaimer] = S(FixArch.Tank, St, Ld, pi | co | ga | le, 14, 18), // 탄소 회수기 (반응 기둥 · 전열 코일)
            // 지휘 · 통신 · 안전
            [FurnitureType.Console] = S(FixArch.Box, Pa, Cy, sc | ke | le, 8, 10),
            [FurnitureType.NavComputer] = S(FixArch.Box, Pa, Bl, sc | ke | le, 8, 12),
            [FurnitureType.MainComputer] = S(FixArch.Computer, Pa, Cy, le | sc | ca, 16, 26),
            [FurnitureType.ServerRack] = S(FixArch.Computer, Pa, Ld, le | ca, 16, 24),
            [FurnitureType.RecorderVault] = S(FixArch.Box, Pa, Hz, hz | dr | le | di, 10, 16),
            [FurnitureType.SensorArray] = S(FixArch.Dish, St, Cy, ln | le | ca, 14, 8),
            [FurnitureType.SignalBooster] = S(FixArch.Dish, St, Ld, le, 14, 8),
            [FurnitureType.ListeningPost] = S(FixArch.Dish, Pa, Cy, sc | le, 12, 10),
            [FurnitureType.Telescope] = S(FixArch.Dish, Wh, Bl, ln, 16, 8),
            [FurnitureType.DockClampPanel] = S(FixArch.Wall, Hz, Pa, brk | le | hz, 8, 14),
            [FurnitureType.AirlockPump] = S(FixArch.Tank, St, Cy, ga | pi | hz, 10, 16),
            // 채집 · 제작 · 함대
            [FurnitureType.Collector] = S(FixArch.Box, Hz, St, hz | pi | ve | ar, 10, 14),
            [FurnitureType.Refinery] = S(FixArch.Box, Sw, Or, wi | pi | ga | hz | ve, 14, 22),
            [FurnitureType.DroneDock] = S(FixArch.Pad, St, Hz, hz | le, 4, 4),
            [FurnitureType.RobotDock] = S(FixArch.Pad, Pa, Cy, le | ca, 6, 4),
        };
        return t;
    }

    // ───────────────────────────── 몸체 (구워 둔다) ─────────────────────────────

    /// <summary>
    /// 몸체 그림 (w × h 칸, 칸 위로 Rise만큼 솟는다). blanket: 침대 담요 색표(-1 기본) · grade 0 Mk1 · 1 표준 · 2 Mk3 · wear 0~3 낡음 단계.
    /// </summary>
    public static Img Body(FurnitureType type, int w, int h, int blanket = -1, int grade = 1, int wear = 0, int seed = 0)
    {
        var s = Spec(type);
        int W = w * T, H = h * T + s.Rise;
        var d = new Dot(W, H);
        var g = new Geo(s, W, H);
        switch (s.Arch)
        {
            case FixArch.Bed: Bed(d, s, g, type, blanket); break;
            case FixArch.Seat: Seat(d, s, g, type); break;
            case FixArch.Table: TableArt(d, s, g, type); break;
            case FixArch.Rack: Rack(d, s, g, type, seed); break;
            case FixArch.Planter: Planter(d, s, g, type); break;
            case FixArch.Tank: Tank(d, s, g, type); break;
            case FixArch.Pad: Pad(d, s, g, type); break;
            case FixArch.Reactor: Reactor(d, s, g); break;
            case FixArch.Engine: Engine(d, s, g); break;
            case FixArch.Computer: Computer(d, s, g); break;
            case FixArch.Dish: Dish(d, s, g, type); break;
            case FixArch.Wall: WallBox(d, s, g, type); break;
            default: Box(d, s, g, type); break;
        }
        d.Bevel();
        Marks(d, s, g, type, grade, wear, seed);
        return d.ToImg();
    }

    /// <summary>모양 자리 (몸체와 움직이는 부분이 같은 자리를 쓴다).</summary>
    private readonly struct Geo
    {
        public readonly int W, H, X0, X1, Top0, TopH, Front0, Bottom;
        public Geo(FixSpec s, int w, int h)
        {
            W = w; H = h;
            X0 = 2; X1 = w - 3;
            Bottom = h - 2;
            int front = Math.Min(s.Front, h - 8);
            Front0 = Bottom - front + 1;
            Top0 = s.Arch == FixArch.Wall ? Math.Max(1, Front0 - 6) : 1;
            TopH = Front0 - Top0;
        }
        public int FW => X1 - X0 + 1;
        public int FH => Bottom - Front0 + 1;
    }

    /// <summary>상자: 윗면(밝게) + 앞면.</summary>
    private static void Block(Dot d, Geo g, int body, int topShade = 3, int frontShade = 2)
    {
        d.Part = 1; d.Rect(g.X0, g.Top0, g.FW, g.TopH, body, topShade);
        d.Part = 2; d.Rect(g.X0, g.Front0, g.FW, g.FH, body, frontShade);
    }

    private static void Box(Dot d, FixSpec s, Geo g, FurnitureType type)
    {
        Block(d, g, s.Body);
        var k = s.Kit;
        // 윗면 부품
        if ((k & FixKit.Fan) != 0) { d.Part = 3; int r = Math.Min(g.TopH, g.FW) / 2 - 2; if (r >= 3) d.Oval(g.X0 + g.FW / 2f, g.Top0 + g.TopH / 2f, r, Math.Max(2, r * 0.8f), Ramps.Panel, 1); }
        if ((k & FixKit.Burners) != 0) { d.Part = 3; int n = Math.Max(1, g.FW / 26); for (int i = 0; i < n * 2; i++) d.Oval(g.X0 + (i + 0.5f) * g.FW / (n * 2f), g.Top0 + g.TopH / 2f, 5.5f, Math.Min(4f, g.TopH / 2f - 1), Ramps.Ink, 2); }
        if ((k & FixKit.Coil) != 0) { d.Part = 3; for (int x = g.X0 + 3; x < g.X1 - 2; x += 3) d.Rect(x, g.Top0 + 2, 2, Math.Max(2, g.TopH - 4), Ramps.ClothOrange, 1); }
        if ((k & FixKit.Lens) != 0) { d.Part = 3; d.Oval(g.X0 + g.FW / 2f, g.Top0 + g.TopH / 2f, 4.5f, 3.5f, Ramps.Ink, 2); }
        if ((k & FixKit.Arm) != 0) { d.Part = 3; d.Line(g.X0 + 4, g.Top0 + 2, g.X0 + g.FW / 2, 0, Ramps.Steel, 4); d.Line(g.X0 + g.FW / 2, 0, g.X1 - 3, g.Top0 + 4, Ramps.Steel, 3); d.Rect(g.X1 - 5, g.Top0 + 4, 4, 3, Ramps.Steel, 4); }
        if ((k & FixKit.Pipes) != 0) { d.Part = 3; d.Rect(g.X1 - 6, 0, 3, g.Top0 + 3, Ramps.Steel, 3); d.Rect(g.X0 + 4, 0, 3, g.Top0 + 2, Ramps.Steel, 3); }
        if ((k & FixKit.Cross) != 0 && (k & FixKit.Doors) == 0) { d.Part = 3; int cx = g.X0 + g.FW / 2, cy = g.Top0 + g.TopH / 2; d.Rect(cx - 1, cy - 3, 3, 7, Ramps.Alarm, 3); d.Rect(cx - 3, cy - 1, 7, 3, Ramps.Alarm, 3); }
        if ((k & FixKit.Wheels) != 0) { d.Part = 4; d.Oval(g.X0 + 3, g.Bottom, 2, 1.6f, Ramps.Rubber, 2); d.Oval(g.X1 - 3, g.Bottom, 2, 1.6f, Ramps.Rubber, 2); }
        // 앞면 부품 (문 · 서랍은 몸체 부위로)
        if ((k & FixKit.Doors) != 0)
        {
            int n = g.FW > 40 ? 2 : 1;
            for (int i = 0; i < n; i++) { d.Part = (byte)(5 + i); d.Rect(g.X0 + 2 + i * (g.FW - 4) / n, g.Front0 + 2, (g.FW - 4) / n - 1, g.FH - 4, s.Body, 2); }
        }
        if ((k & FixKit.Window) != 0) { d.Part = 7; int ww = Math.Min(16, g.FW - 8); d.Rect(g.X0 + (g.FW - ww) / 2, g.Front0 + 3, ww, Math.Max(4, g.FH - 7), Ramps.Glass, 1); }
        if ((k & FixKit.Cells) != 0)
        {
            // 전지 셀 (칸마다 둘): 녹색 몸 · 위에 단자 — 충전 막대는 움직이는 부분
            int n = Math.Max(2, g.FW / 15);
            for (int i = 0; i < n; i++) { d.Part = (byte)(5 + i); int cw = (g.FW - 4) / n - 2; d.Rect(g.X0 + 2 + i * (cw + 2), g.Front0 + 2, cw, g.FH - 5, Ramps.ClothGreen, 1); }
            d.Part = 20; for (int i = 0; i < n; i++) { int cw = (g.FW - 4) / n - 2, x = g.X0 + 2 + i * (cw + 2); d.Rect(x + 2, g.Top0 + 2, 3, 2, Ramps.Alarm, 3); d.Rect(x + cw - 5, g.Top0 + 2, 3, 2, Ramps.Ink, 2); }
        }
        if ((k & FixKit.Suit) != 0)
        {
            // 유리문 속 우주복 (헬멧 · 몸)
            d.Part = 7; d.Rect(g.X0 + 3, g.Front0 + 2, g.FW - 6, g.FH - 4, Ramps.Glass, 1);
            d.Part = 8; d.Oval(g.X0 + g.FW / 2f, g.Front0 + 7, 4.5f, 4, Ramps.SuitWhite, 3);
            d.Part = 9; d.Rect(g.X0 + g.FW / 2 - 4, g.Front0 + 10, 8, g.FH - 14, Ramps.SuitWhite, 2);
        }
        if ((k & FixKit.Arm) != 0 && type is FurnitureType.SurgicalArm or FurnitureType.Hoist) { d.Part = 8; d.Rect(g.X0 + g.FW / 2 - 1, 0, 3, g.Top0 + 2, Ramps.Steel, 3); }
    }

    private static void WallBox(Dot d, FixSpec s, Geo g, FurnitureType type)
    {
        // 벽에 붙은 것: 칸 위쪽에 얇은 함 (윗면이 좁고 앞면이 크다)
        Block(d, g, s.Body);
        var k = s.Kit;
        if ((k & FixKit.Doors) != 0) { d.Part = 5; d.Rect(g.X0 + 2, g.Front0 + 2, g.FW - 4, g.FH - 4, s.Body, 3); }
        if ((k & FixKit.Bars) != 0) { d.Part = 5; d.Rect(g.X0 + 1, g.Front0 + g.FH / 2 - 1, g.FW - 2, 2, Ramps.Steel, 4); }
        if ((k & FixKit.Clothes) != 0) { d.Part = 5; for (int x = g.X0 + 1; x < g.X1; x += 4) d.Rect(x, g.Front0 - 2, 3, g.FH + 2, s.Body, (x / 4) % 2 == 0 ? 2 : 3); }
        if ((k & FixKit.Spray) != 0) { d.Part = 5; d.Rect(g.X0 + g.FW / 2 - 3, g.Front0 + g.FH - 6, 6, 3, Ramps.Bandage, 3); }
    }

    private static void Bed(Dot d, FixSpec s, Geo g, FurnitureType type, int blanket)
    {
        int W = g.W, H = g.H;
        bool med = type is FurnitureType.MedBed or FurnitureType.OperatingTable;
        bool op = type == FurnitureType.OperatingTable;
        int bl = BlanketOf(type, blanket);
        d.Part = 1; d.Rect(1, 2, W - 2, H - 6, s.Body, 3);                         // 틀
        d.Part = 2; d.Rect(3, 4, W - 6, H - 12, op ? Ramps.Steel : Ramps.Bandage, op ? 4 : 2); // 매트리스
        if (!op)
        {
            d.Part = 3; d.Rect(5, 6, W - 10, Math.Min(8, H / 6), Ramps.Bandage, 3); // 베개
            var (bx0, by, bw, bh) = BlanketRect(type, W / T, (H - s.Rise) / T);
            d.Part = 5; d.Rect(bx0, by, bw, bh, bl, 2);                          // 담요
            d.Part = 6; d.Rect(3, by, W - 6, 3, bl, 3);                          // 접힌 끝
        }
        d.Part = 7; d.Rect(1, H - 4, W - 2, 4, s.Body, 2);                       // 발치 앞면
        if (med)
        {
            // 침대 난간 · 머리맡 화면 · 수액대
            d.Part = 8; d.Rect(0, 8, 2, H - 16, Ramps.Steel, 4); d.Rect(W - 2, 8, 2, H - 16, Ramps.Steel, 4);
            if (!op) { d.Part = 9; d.Rect(W - 9, 0, 8, 6, Ramps.Panel, 3); }
            d.Part = 10; d.Line(2, 0, 2, 10, Ramps.Steel, 4); d.Rect(0, 0, 5, 2, Ramps.Glass, 3);
        }
        if (type == FurnitureType.Cot) { d.Part = 8; d.Line(2, H - 3, W - 3, 3, Ramps.Wood, 1); }
    }

    /// <summary>침대 담요 색표 (주인 색 · 없으면 의료용 파랑 · 그 밖은 종류 강조 색).</summary>
    public static int BlanketOf(FurnitureType type, int blanket) =>
        blanket >= 0 ? blanket : type is FurnitureType.MedBed or FurnitureType.OperatingTable ? Ramps.ClothBlue : Spec(type).Accent;

    /// <summary>침대 그림 속 담요 자리 (몸체 그림 좌표).</summary>
    public static (int X, int Y, int W, int H) BlanketRect(FurnitureType type, int w, int h)
    {
        int W = w * T, H = h * T + Spec(type).Rise;
        int by = Math.Min(22, H / 2 - 4);
        return (3, by, W - 6, H - by - 8);
    }

    /// <summary>
    /// 담요 밑에 누운 몸: 어깨가 넓고 발로 갈수록 좁은 굴곡 + 가슴께 접힌 끝 + 다리 사이 주름. 담요 자리에 겹친다.
    /// curl: 0 바로 누움 · 1 옆으로 웅크림(무릎이 한쪽으로).
    /// </summary>
    public static Img Sleeper(int blanket, int w, int h, int curl = 0)
    {
        var d = new Dot(w, h);
        int cx = w / 2;
        int n = Math.Max(4, h - 3);
        var half = new int[n];
        for (int y = 0; y < n; y++)
        {
            float t = y / (float)(n - 1);
            float hw = t < 0.2f ? 7.5f + t * 7f : t < 0.55f ? 8.9f - (t - 0.2f) * 4f : 7.5f - (t - 0.55f) * 7f;
            if (t > 0.9f) hw += 1f; // 발끝
            half[y] = Math.Max(2, (int)MathF.Round(Math.Min(hw, cx - 1)));
        }
        d.Part = 1;
        int sx = curl == 1 ? 2 : 0;
        for (int y = 0; y < n; y++)
        {
            int off = curl == 1 && y > n / 2 ? (int)MathF.Round((y - n / 2) * 4f / n) : 0;
            for (int x = cx - half[y] + off - sx / 2; x < cx + half[y] + off; x++) d.Set(x, 3 + y, blanket, 2);
        }
        d.Part = 2; d.Rect(1, 0, w - 2, 4, blanket, 3);   // 가슴께 접힌 끝 (침대 접힌 끝을 끌어올린 자리)
        d.Bevel();
        d.Part = 3; d.MarkRect(1, 3, w - 2, 1, blanket, 1); // 접힌 끝 그늘
        for (int y = n * 6 / 10; y < n - 1; y++) d.Tone(cx + (curl == 1 ? (y - n / 2) * 4 / n : 0), 3 + y, -1); // 다리 사이 주름
        return d.ToImg(outline: false);
    }

    private static void Seat(Dot d, FixSpec s, Geo g, FurnitureType type)
    {
        int W = g.W, H = g.H;
        int cx = W / 2;
        d.Part = 1; d.Rect(cx - 10, 2, 20, 12, s.Body, 2);                      // 등받이
        d.Part = 2; d.Rect(cx - 11, 13, 22, 10, s.Body, 3);                     // 방석
        d.Part = 3; d.Rect(cx - 11, 23, 22, 4, s.Body, 1);                      // 앞면
        d.Part = 4; d.Rect(cx - 9, 27, 2, H - 29, Ramps.Steel, 2); d.Rect(cx + 7, 27, 2, H - 29, Ramps.Steel, 2);
        if (type == FurnitureType.CrashSeat)
        {
            // 안전띠 둘 · 머리 받침
            d.Part = 5; d.Rect(cx - 6, 0, 12, 4, s.Body, 3);
            d.Part = 6; d.Line(cx - 7, 4, cx - 3, 22, Ramps.Rubber, 3); d.Line(cx + 7, 4, cx + 3, 22, Ramps.Rubber, 3);
        }
    }

    private static void TableArt(Dot d, FixSpec s, Geo g, FurnitureType type)
    {
        int top = s.Body == Ramps.Steel || s.Body == Ramps.Panel ? 4 : 3;
        Block(d, g, s.Body, top, 1);
        // 다리
        d.Part = 3; d.Rect(g.X0 + 2, g.Bottom - 2, 3, 3, s.Body, 0); d.Rect(g.X1 - 4, g.Bottom - 2, 3, 3, s.Body, 0);
        var k = s.Kit;
        if ((k & FixKit.Belt) != 0) { d.Part = 4; d.Rect(g.X0 + 2, g.Top0 + 2, g.FW - 4, g.TopH - 4, Ramps.Rubber, 2); d.Part = 5; d.Rect(g.X0 + 2, 0, g.FW - 4, 5, Ramps.Panel, 3); }
        if ((k & FixKit.Coil) != 0) { d.Part = 4; d.Oval(g.X0 + g.FW / 2f, g.Top0 + g.TopH / 2f, 3, 3, Ramps.Steel, 4); d.Rect(g.X0 + 3, g.Top0 + g.TopH / 2 - 1, g.FW - 6, 2, Ramps.Steel, 3); }
        if ((k & FixKit.Lamp) != 0) { d.Part = 6; d.Line(g.X1 - 4, g.Top0 + 2, g.X1 - 8, 0, Ramps.Steel, 4); d.Rect(g.X1 - 11, 0, 5, 3, Ramps.Hazard, 3); }
        if ((k & FixKit.Wheels) != 0) { d.Part = 7; d.Oval(g.X0 + 3, g.Bottom, 2, 1.6f, Ramps.Rubber, 2); d.Oval(g.X1 - 3, g.Bottom, 2, 1.6f, Ramps.Rubber, 2); }
    }

    private static void Rack(Dot d, FixSpec s, Geo g, FurnitureType type, int seed)
    {
        var k = s.Kit;
        if ((k & (FixKit.Clothes | FixKit.Cat | FixKit.Bars)) != 0)
        {
            // 옷걸이 · 고양이 탑 · 화물망: 기둥과 가로대
            d.Part = 1; d.Rect(g.X0 + 1, 0, 2, g.Bottom + 1, s.Body, 3); d.Rect(g.X1 - 2, 0, 2, g.Bottom + 1, s.Body, 3);
            d.Part = 2; d.Rect(g.X0 + 1, 1, g.FW - 2, 2, s.Body, 4);
            d.Part = 3; d.Rect(g.X0, g.Bottom - 1, g.FW, 2, s.Body, 1);
            if ((k & FixKit.Cat) != 0) { d.Part = 4; d.Rect(g.X0 + 3, g.H / 3, g.FW - 6, 4, Ramps.ClothBeige, 3); d.Rect(g.X0 + 5, g.H * 2 / 3, g.FW - 10, 4, Ramps.ClothBeige, 3); d.Oval(g.X0 + g.FW / 2f, 3, 6, 3, Ramps.ClothBeige, 3); }
            if ((k & FixKit.Bars) != 0) { d.Part = 4; for (int y = 4; y < g.Bottom; y += 4) d.Rect(g.X0 + 1, y, g.FW - 2, 1, Ramps.Rubber, 3); }
            return;
        }
        // 선반: 옆판 · 칸 셋~넷 (물건은 Marks에서)
        Block(d, g, s.Body, 3, 2);
        int shelves = Math.Max(2, g.FH / 7);
        for (int i = 0; i < shelves; i++) { d.Part = (byte)(4 + i); d.Rect(g.X0 + 2, g.Front0 + 1 + i * g.FH / shelves, g.FW - 4, g.FH / shelves - 2, Ramps.Ink, 2); }
    }

    private static void Planter(Dot d, FixSpec s, Geo g, FurnitureType type)
    {
        Block(d, g, s.Body, 3, 2);
        // 흙 (식물은 움직이는 부분 — 자람에 따라)
        d.Part = 3; d.Rect(g.X0 + 2, g.Top0 + 2, g.FW - 4, Math.Max(3, g.TopH - 4), Ramps.Soil, 2);
        if ((s.Kit & FixKit.Tube) != 0) { d.Part = 4; d.Rect(g.X0 + 1, g.Front0 - 1, g.FW - 2, 1, Ramps.Rubber, 3); }
    }

    private static void Tank(Dot d, FixSpec s, Geo g, FurnitureType type)
    {
        var k = s.Kit;
        // 받침 상자 + 원통 탱크 (칸 너비마다 하나 · 2칸 이상이면 둘)
        int n = Math.Max(1, Math.Min(3, g.W / 26));
        d.Part = 1; d.Rect(g.X0, g.Bottom - 6, g.FW, 7, Ramps.Steel, 2);
        if (type == FurnitureType.Aquarium)
        {
            d.Part = 2; d.Rect(g.X0, g.Top0 + 2, g.FW, g.Bottom - g.Top0 - 7, Ramps.Glass, 2);
            d.Part = 3; d.Rect(g.X0, g.Top0, g.FW, 3, s.Body, 3);
            return;
        }
        for (int i = 0; i < n; i++)
        {
            int cw = (g.FW - 2) / n - 2, x = g.X0 + 1 + i * (cw + 2);
            d.Part = (byte)(2 + i * 2); d.Rect(x, g.Top0 + 4, cw, g.Bottom - g.Top0 - 10, s.Body, 2);
            d.Part = (byte)(3 + i * 2); d.Oval(x + cw / 2f, g.Top0 + 4, cw / 2f, 3.2f, s.Body, 3);
        }
        if ((k & FixKit.Window) != 0) { d.Part = 10; int ww = Math.Min(14, g.FW / 3); d.Rect(g.X1 - ww - 3, g.Front0 + 1, ww, Math.Max(5, g.FH - 9), Ramps.Glass, 1); }
        if ((k & FixKit.Fan) != 0) { d.Part = 10; int r = Math.Min(g.FW, g.FH) / 3; d.Oval(g.X1 - r - 3, g.Front0 + g.FH / 2f - 2, r, r, Ramps.Panel, 1); }
        if ((k & FixKit.Pipes) != 0) { d.Part = 11; d.Rect(g.X0, g.Front0 + 2, g.FW, 3, Ramps.Steel, 4); }
    }

    private static void Pad(Dot d, FixSpec s, Geo g, FurnitureType type)
    {
        int y0 = g.H - T + 1;
        d.Part = 1; d.Rect(1, y0, g.W - 2, T - 3, s.Body, 2);
        d.Part = 2; d.Rect(3, y0 + 2, g.W - 6, T - 7, s.Body, 3);
        if (type == FurnitureType.DeconShower)
        {
            d.Part = 3; d.Rect(g.W / 2 - 1, 0, 3, y0 + 4, Ramps.Steel, 4);
            d.Part = 4; d.Rect(g.W / 2 - 6, 0, 12, 3, Ramps.Steel, 3);
        }
        else
        {
            // 거치 기둥 (뒤쪽) · 착륙 표시 · 충전 단자
            d.Part = 3; d.Rect(g.W / 2 - 3, Math.Max(0, y0 - s.Rise), 6, s.Rise + 3, Ramps.Panel, 3);
            d.Part = 4; d.Oval(g.W / 2f, y0 + (T - 3) / 2f, 9, 8, s.Body, 2);
            d.Part = 5; d.Oval(g.W / 2f, y0 + (T - 3) / 2f, 6, 5, s.Body, 3);
            d.Part = 6; d.Rect(g.W / 2 - 1, y0 + 9, 3, 9, s.Accent, 3);
        }
    }

    private static void Reactor(Dot d, FixSpec s, Geo g)
    {
        float cx = g.W / 2f, cy = (g.H + 4) / 2f;
        float R = Math.Min(g.W, g.H) / 2f - 3;
        d.Part = 1; d.Rect(2, (int)(cy - 2), g.W - 4, (int)(g.H - cy), Ramps.Steel, 2); // 받침 (아래 반)
        d.Part = 2; d.Oval(cx, cy, R, R * 0.92f, s.Body, 2);                           // 격납 용기
        d.Part = 3; d.Oval(cx, cy - 2, R * 0.72f, R * 0.64f, s.Body, 3);               // 윗판
        d.Part = 4; d.Oval(cx, cy - 2, R * 0.44f, R * 0.4f, Ramps.Panel, 1);           // 노심 창 (빛은 움직이는 부분)
        // 냉각관 넷
        d.Part = 5;
        foreach (var (dx, dy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            d.Rect((int)(cx + dx * R * 0.78f) - 2, (int)(cy + dy * R * 0.7f) - 2, 5, 5, Ramps.ClothBlue, 3);
    }

    private static void Engine(Dot d, FixSpec s, Geo g)
    {
        // 가로로 누운 큰 원통 (가운데 60%) · 왼쪽 노즐 종(배 밖으로 분사) · 보강 고리 · 받침 · 연료관
        float cy = (g.Top0 + g.Bottom) / 2f, R = (g.Bottom - g.Top0) * 0.3f;
        d.Part = 1; d.Rect(10, g.Bottom - 6, g.W - 16, 6, Ramps.Steel, 2);                 // 받침
        d.Part = 2; d.Rect(14, (int)(cy - R), g.W - 26, (int)(2 * R), s.Body, 2);          // 몸통
        d.Part = 3; d.Oval(g.W - 12, cy, 5, R, s.Body, 3);                                 // 뒤 둥근 끝
        d.Part = 4; d.Rect(14, (int)(cy - R), g.W - 26, (int)(R * 0.5f), s.Body, 3);       // 위쪽 빛 받는 면
        for (int x = 22; x < g.W - 16; x += 12) { d.Part = 5; d.Rect(x, (int)(cy - R) - 1, 3, (int)(2 * R) + 2, Ramps.Steel, 4); }
        // 노즐 종: 왼쪽으로 넓어진다
        d.Part = 6;
        for (int x = 0; x < 15; x++) { float hr = R * (1.25f - x / 15f * 0.55f); d.Rect(x, (int)(cy - hr), 1, (int)(2 * hr), Ramps.Steel, x < 3 ? 1 : 2); }
        d.Part = 7; d.Rect(g.W - 10, (int)(cy + R) - 2, 8, 4, Ramps.ClothOrange, 3);       // 연료관
    }

    private static void Computer(Dot d, FixSpec s, Geo g)
    {
        // 서버 탑 여럿 (칸마다 둘) · 위에 전선 다발
        int n = Math.Max(1, g.W / 16);
        for (int i = 0; i < n; i++)
        {
            int x = 2 + i * (g.W - 4) / n, w = (g.W - 4) / n - 2;
            d.Part = (byte)(1 + i * 2); d.Rect(x, g.Top0, w, g.TopH, s.Body, 3);
            d.Part = (byte)(2 + i * 2); d.Rect(x, g.Front0, w, g.FH, s.Body, 2);
        }
        d.Part = 40; d.Rect(2, 0, g.W - 4, 2, Ramps.Rubber, 3);
    }

    private static void Dish(Dot d, FixSpec s, Geo g, FurnitureType type)
    {
        float cx = g.W / 2f;
        d.Part = 1; d.Rect(g.X0 + 2, g.Bottom - 7, g.FW - 4, 8, Ramps.Steel, 2);        // 받침
        d.Part = 2; d.Rect((int)cx - 2, g.Top0 + 6, 4, g.Bottom - g.Top0 - 12, Ramps.Steel, 3); // 기둥
        switch (type)
        {
            case FurnitureType.SurgicalLamp:
                d.Part = 3; d.Line((int)cx, g.Top0 + 6, (int)cx + 8, g.Top0, Ramps.Steel, 4);
                d.Part = 4; d.Oval(cx + 8, g.Top0 + 4, 7, 4, Ramps.SuitWhite, 3);
                break;
            case FurnitureType.Telescope:
                d.Part = 3; d.Line((int)cx - 8, g.Top0 + 10, (int)cx + 8, g.Top0, Ramps.SuitWhite, 3); d.Line((int)cx - 8, g.Top0 + 11, (int)cx + 8, g.Top0 + 1, Ramps.SuitWhite, 3); d.Line((int)cx - 7, g.Top0 + 11, (int)cx + 9, g.Top0 + 1, Ramps.SuitWhite, 2);
                break;
            case FurnitureType.ThermalCamera:
                d.Part = 3; d.Rect((int)cx - 5, g.Top0 + 2, 10, 7, Ramps.Panel, 3);
                break;
            default:
                d.Part = 3; d.Oval(cx, g.Top0 + 6, Math.Min(12, g.FW / 2f - 1), 6, s.Body, 3);   // 접시
                d.Part = 4; d.Line((int)cx, g.Top0 + 6, (int)cx, 0, Ramps.Steel, 4);
                break;
        }
    }

    // ───────────────────────────── 세부 · 낡음 · 등급 ─────────────────────────────

    private static void Marks(Dot d, FixSpec s, Geo g, FurnitureType type, int grade, int wear, int seed)
    {
        var k = s.Kit;
        int fx = g.X0 + 2, fy = g.Front0 + 2;
        bool box = s.Arch is FixArch.Box or FixArch.Wall or FixArch.Computer;
        if (box || s.Arch == FixArch.Tank)
        {
            if ((k & FixKit.Screen) != 0 && s.Arch != FixArch.Computer) { int sw = Math.Min(14, g.FW / 2), sh = Math.Max(3, Math.Min(7, g.FH - 6)); d.MarkRect(fx, fy, sw, sh, Ramps.Ink, 0); d.MarkRect(fx, fy, sw, 1, Ramps.Ink, 2); fx += sw + 2; }
            if ((k & FixKit.Breakers) != 0)
                for (int r = 0; r < 2; r++) for (int c = 0; c < g.FW / 6; c++) { d.MarkRect(g.X0 + 3 + c * 6, g.Front0 + 3 + r * 7, 3, 5, Ramps.Ink, 1); d.Mark(g.X0 + 4 + c * 6, g.Front0 + 4 + r * 7, Ramps.Bandage, 3); }
            if ((k & FixKit.Dials) != 0) { d.Mark(g.X1 - 4, fy + 1, Ramps.Ink, 1); d.Mark(g.X1 - 3, fy + 1, Ramps.Steel, 5); d.Mark(g.X1 - 7, fy + 1, Ramps.Ink, 1); d.Mark(g.X1 - 6, fy + 1, Ramps.Steel, 5); }
            if ((k & FixKit.Gauge) != 0) { d.MarkOval(g.X1 - 6, g.Front0 + g.FH / 2f, 3, 3, Ramps.Bandage, 3); }
            if ((k & FixKit.Vents) != 0) for (int y = g.Bottom - 4; y < g.Bottom - 1; y += 2) for (int x = g.X0 + 3; x < g.X1 - 2; x++) d.Mark(x, y, s.Body, 0);
            if ((k & FixKit.Handle) != 0) { int hx = g.X0 + g.FW / 2; for (int y = g.Front0 + 3; y < g.Front0 + 3 + Math.Min(8, g.FH - 6); y++) d.Mark(hx + ((k & FixKit.Doors) != 0 && g.FW > 40 ? -2 : 3), y, Ramps.Steel, 5); }
            if ((k & FixKit.Drawer) != 0) for (int y = g.Front0 + 4; y < g.Bottom - 2; y += 5) { for (int x = g.X0 + 2; x < g.X1 - 1; x++) d.Mark(x, y, s.Body, 1); d.Mark(g.X0 + g.FW / 2, y + 2, Ramps.Steel, 5); }
            if ((k & FixKit.Hazard) != 0) for (int x = g.X0; x <= g.X1; x++) d.Mark(x, g.Bottom - 1, ((x / 2) & 1) == 0 ? Ramps.Hazard : Ramps.Ink, ((x / 2) & 1) == 0 ? 3 : 1);
            if ((k & FixKit.Cross) != 0 && (k & FixKit.Doors) != 0) { int cx = g.X0 + g.FW / 2, cy = g.Front0 + g.FH / 2; d.MarkRect(cx - 1, cy - 3, 3, 7, Ramps.Alarm, 3); d.MarkRect(cx - 3, cy - 1, 7, 3, Ramps.Alarm, 3); }
            if ((k & FixKit.Keys) != 0) for (int y = g.Top0 + 2; y < g.Front0 - 1; y += 2) for (int x = g.X0 + 3; x < g.X1 - 2; x += 2) d.Mark(x, y, Ramps.Ink, 2);
            if ((k & FixKit.Cables) != 0) { d.Mark(g.X0 + 1, g.Bottom + 1, Ramps.Rubber, 3, onEmpty: true); d.Mark(g.X0, g.Bottom + 1, Ramps.Rubber, 3, onEmpty: true); }
            if ((k & FixKit.Note) != 0) for (int i = 0; i < 4; i++) { int x = g.X0 + 3 + (int)(DotHash.F(seed, i) * (g.FW - 8)), y = g.Front0 + 2 + (int)(DotHash.F(seed, i, 1) * (g.FH - 6)); d.MarkRect(x, y, 4, 3, i % 2 == 0 ? Ramps.ClothYellow : Ramps.Bandage, 4); }
            if ((k & FixKit.Tools) != 0 && s.Arch == FixArch.Wall)
                for (int i = 0; i < g.FW / 6; i++) { int x = g.X0 + 3 + i * 6; d.MarkRect(x, g.Front0 + 3, 1, g.FH - 7, Ramps.Steel, 4); d.MarkRect(x - 1, g.Front0 + 2, 3, 2, i % 2 == 0 ? Ramps.Alarm : Ramps.Hazard, 3); }
        }
        if (s.Arch == FixArch.Computer)
        {
            int n = Math.Max(1, g.W / 16);
            for (int i = 0; i < n; i++)
            {
                int x = 2 + i * (g.W - 4) / n, w = (g.W - 4) / n - 2;
                for (int y = g.Front0 + 2; y < g.Bottom - 2; y += 3) d.MarkRect(x + 2, y, w - 4, 1, Ramps.Ink, 1);
            }
        }
        if (s.Arch is FixArch.Table && (k & FixKit.Belt) == 0)
        {
            int ty = g.Top0 + 2, th = Math.Max(3, g.TopH - 4);
            if ((k & FixKit.Items) != 0 && type != FurnitureType.Workbench)
                for (int i = 0; i < Math.Max(1, g.FW / 14); i++) { int x = g.X0 + 4 + i * 14 + (int)(DotHash.F(seed, i) * 4); d.MarkOval(x + 3, ty + th / 2f, 3, 2, Ramps.Bandage, 3); d.Mark(x + 3, ty + th / 2, Ramps.Bandage, 1); }
            if ((k & FixKit.Tools) != 0)
                for (int i = 0; i < g.FW / 9; i++)
                {
                    int x = g.X0 + 4 + i * 9, y = ty + 1 + (i % 2) * 3;
                    int col = (i % 3) switch { 0 => Ramps.Alarm, 1 => Ramps.Hazard, _ => Ramps.ClothBlue };
                    d.MarkRect(x, y, 5, 1, Ramps.Steel, 5); d.MarkRect(x + 5, y - 1, 2, 3, col, 3);
                }
            if ((k & FixKit.Cards) != 0) { d.MarkRect(g.X0 + 5, ty + 2, 4, 5, Ramps.Bandage, 4); d.MarkRect(g.X0 + 11, ty + 1, 4, 5, Ramps.Bandage, 4); d.Mark(g.X0 + 12, ty + 3, Ramps.Alarm, 3); d.MarkRect(g.X1 - 9, ty + 2, 5, 5, Ramps.Wood, 1); }
            if ((k & FixKit.Bottles) != 0) for (int i = 0; i < 4; i++) d.MarkRect(g.X0 + 4 + i * 5, ty, 2, 4, i % 2 == 0 ? Ramps.Glass : Ramps.Leaf, 3);
            if ((k & FixKit.Note) != 0) { d.MarkRect(g.X0 + 4, ty, 6, 3, Ramps.Wood, 3); d.Mark(g.X0 + 12, ty + 1, Ramps.ClothRed, 3); d.MarkRect(g.X1 - 8, ty, 4, 4, Ramps.ClothYellow, 3); }
        }
        if (s.Arch == FixArch.Rack && (k & (FixKit.Clothes | FixKit.Cat | FixKit.Bars)) == 0)
        {
            int shelves = Math.Max(2, g.FH / 7);
            for (int i = 0; i < shelves; i++)
            {
                int y = g.Front0 + 1 + i * g.FH / shelves, hh = g.FH / shelves - 2;
                for (int x = g.X0 + 3; x < g.X1 - 2;)
                {
                    float r = DotHash.F(seed + i, x);
                    int w = (k & FixKit.Books) != 0 ? 2 : (k & FixKit.Bottles) != 0 ? 2 : 3 + (int)(r * 3);
                    int hgt = Math.Max(1, hh - (int)(r * 3));
                    int col = (k & FixKit.Plants) != 0 ? Ramps.Leaf : (k & FixKit.Books) != 0 ? Ramps.Accent[(int)(r * 8) % Ramps.Accent.Length] : (k & FixKit.Bottles) != 0 ? (r < 0.5f ? Ramps.ClothRed : Ramps.ClothYellow) : r < 0.33f ? Ramps.Wood : r < 0.66f ? Ramps.ClothBlue : Ramps.Hazard;
                    if (r < 0.15f && (k & FixKit.Books) == 0) { x += 3; continue; }
                    for (int yy = y + hh - hgt; yy < y + hh; yy++) for (int xx = x; xx < Math.Min(g.X1 - 2, x + w); xx++) d.Mark(xx, yy, col, yy == y + hh - hgt ? 3 : 2);
                    x += w + ((k & FixKit.Books) != 0 ? 0 : 1);
                }
            }
        }
        // 등급: Mk1은 덧댄 판 · 테이프, Mk3는 매끈한 강조 테
        if (grade == 0 && box) { d.MarkRect(g.X0 + 1, g.Front0 + 1, 5, 4, Ramps.SteelWarm, 2); d.Mark(g.X0 + 1, g.Front0 + 1, Ramps.Steel, 5); d.Mark(g.X0 + 5, g.Front0 + 4, Ramps.Steel, 5); d.MarkRect(g.X1 - 8, g.Top0 + 1, 6, 1, Ramps.Hazard, 2); }
        if (grade >= 2 && (box || s.Arch == FixArch.Tank)) for (int x = g.X0; x <= g.X1; x++) d.Mark(x, g.Front0, s.Accent, 3);
        // 낡음: 긁힘 · 녹 · 그을음 점
        for (int i = 0; i < wear * 4; i++)
        {
            int x = g.X0 + 1 + (int)(DotHash.F(seed, i, 9) * (g.FW - 2)), y = g.Top0 + (int)(DotHash.F(seed, i, 10) * (g.Bottom - g.Top0));
            d.Mark(x, y, i % 3 == 0 ? Ramps.Char : Ramps.Wood, i % 3 == 0 ? 1 : 1);
        }
    }

    // ───────────────────────────── 움직이는 부분 (상태 · 장면) ─────────────────────────────

    /// <summary>
    /// 움직이는 부분 (몸체와 같은 크기 · 나머지는 투명): 화면 글자 · 불빛 · 팬 날 · 화구 · 물 · 노심 빛 · 바늘 · 식물.
    /// level: 노심 출력 · 작물 자람 · 물 높이 0~1 (종류마다 뜻이 다르다).
    /// </summary>
    public static Img Life(FurnitureType type, int w, int h, FixState st, int frame, float level = 1f)
    {
        var s = Spec(type);
        int W = w * T, H = h * T + s.Rise;
        var img = new Img(W, H);
        var g = new Geo(s, W, H);
        var k = s.Kit;
        bool on = st == FixState.On, broken = st == FixState.Broken;
        Rgba C(int ramp, int shade) => Ramps.Get(ramp, shade);
        // 화면
        if ((k & FixKit.Screen) != 0 && s.Arch is FixArch.Box or FixArch.Wall or FixArch.Tank or FixArch.Table)
        {
            int fx = g.X0 + 2, fy = g.Front0 + 2, sw = Math.Min(14, g.FW / 2), sh = Math.Max(3, Math.Min(7, g.FH - 6));
            if (s.Arch == FixArch.Table) { fx = g.X1 - 12; fy = g.Top0 + 1; sw = 9; sh = 5; }
            if (on)
            {
                for (int y = 1; y < sh - 1; y += 2)
                {
                    int len = 2 + (int)(DotHash.F(y, frame / 2) * (sw - 4));
                    for (int x = 1; x < 1 + len; x++) img.Set(fx + x, fy + y, C(s.Accent, 3));
                }
                img.Set(fx + 1 + frame % Math.Max(1, sw - 2), fy + sh - 1, C(s.Accent, 4));
            }
            else if (broken)
            {
                for (int x = 0; x < sw; x++) img.Set(fx + x, fy + (x * sh / Math.Max(1, sw)), C(Ramps.Alarm, 3));
                if ((frame & 1) == 0) img.Set(fx + sw / 2, fy + sh / 2, C(Ramps.Alarm, 4));
            }
        }
        if (type == FurnitureType.MedBed && on)
        {
            // 머리맡 심전도
            int x0 = g.W - 8, y0 = 2;
            for (int x = 0; x < 6; x++) img.Set(x0 + x, y0 + ((x + frame) % 6 == 2 ? 0 : 2), C(Ramps.Led, 3));
        }
        // 불빛 (위쪽 오른편에 셋)
        if ((k & FixKit.Leds) != 0)
        {
            int n = 3;
            for (int i = 0; i < n; i++)
            {
                int x = g.X1 - 3 - i * 3, y = s.Arch == FixArch.Pad ? g.H - T + 3 : s.Arch == FixArch.Computer ? g.Front0 + 2 : g.Front0 + 2;
                if (s.Arch == FixArch.Computer)
                {
                    // 서버 불빛: 탑마다 여러 줄 깜빡임
                    int nt = Math.Max(1, g.W / 16);
                    for (int t2 = 0; t2 < nt; t2++)
                        for (int yy = g.Front0 + 2; yy < g.Bottom - 3; yy += 3)
                            for (int xx = 0; xx < 3; xx++)
                            {
                                int px = 4 + t2 * (g.W - 4) / nt + xx * 2;
                                bool lit = on ? DotHash.F(t2 * 31 + yy, xx, frame) < 0.55f : broken && DotHash.F(t2, yy, xx) < 0.2f;
                                if (lit) img.Set(px, yy, C(broken ? Ramps.Alarm : (xx == 0 ? s.Accent : Ramps.Led), 3));
                            }
                    break;
                }
                bool litLed = on ? ((frame + i) % 4 != 0) : broken ? ((frame & 1) == 0 && i == 0) : false;
                if (litLed) { img.Set(x, y, C(broken ? Ramps.Alarm : i == 2 ? Ramps.Hazard : Ramps.Led, 4)); }
            }
        }
        // 팬 (날 셋이 돈다 · 꺼지면 멈춤 · 고장이면 비틀)
        if ((k & FixKit.Fan) != 0)
        {
            float cx, cy, r;
            if (s.Arch == FixArch.Tank) { r = Math.Min(g.FW, g.FH) / 3; cx = g.X1 - r - 3; cy = g.Front0 + g.FH / 2f - 2; }
            else { r = Math.Min(g.TopH, g.FW) / 2 - 2; cx = g.X0 + g.FW / 2f; cy = g.Top0 + g.TopH / 2f; }
            if (r >= 3)
            {
                float a0 = on ? frame * 0.9f : broken ? 0.3f + (frame & 1) * 0.15f : 0.2f;
                for (int b = 0; b < 3; b++)
                {
                    float a = a0 + b * 2.094f;
                    for (int i = 1; i < r; i++) img.Set((int)(cx + MathF.Cos(a) * i), (int)(cy + MathF.Sin(a) * i * 0.8f), C(Ramps.Steel, 4));
                }
                img.Set((int)cx, (int)cy, C(Ramps.Steel, 5));
            }
        }
        // 화구 (켜지면 단다)
        if ((k & FixKit.Burners) != 0 && on)
        {
            int n = Math.Max(1, g.FW / 26) * 2;
            for (int i = 0; i < n; i++)
            {
                if (i % 2 == 1 && (frame & 2) == 0) continue;
                float cx = g.X0 + (i + 0.5f) * g.FW / n, cy = g.Top0 + g.TopH / 2f;
                img.Oval(cx, cy, 4f, Math.Min(3f, g.TopH / 2f - 1), C(Ramps.Fire, 3), 1f);
                img.Oval(cx, cy, 2.5f, 1.6f, C(Ramps.Fire, 5), 1f);
                img.Glow(cx, cy, 7f, C(Ramps.Fire, 5), 0.35f);
            }
        }
        if ((k & FixKit.Coil) != 0 && on && s.Arch == FixArch.Box)
            for (int x = g.X0 + 3; x < g.X1 - 2; x += 3) img.Fill(x, g.Top0 + 2, 2, Math.Max(2, g.TopH - 4), C(Ramps.Fire, 4), 0.5f + 0.2f * (frame & 1));
        // 창 속 (빨래 · 그릇 · 반죽 · 거품 · 곤충)
        if ((k & FixKit.Window) != 0 && on)
        {
            int ww = s.Arch == FixArch.Tank ? Math.Min(14, g.FW / 3) : Math.Min(16, g.FW - 8);
            int wx = s.Arch == FixArch.Tank ? g.X1 - ww - 3 : g.X0 + (g.FW - ww) / 2, wy = g.Front0 + 3, wh = Math.Max(4, g.FH - 7);
            if (s.Arch == FixArch.Tank) wy = g.Front0 + 1;
            var col = type switch { FurnitureType.Oven or FurnitureType.BreadOven => C(Ramps.Fire, 4), FurnitureType.InsectFarm => C(Ramps.Leaf, 3), FurnitureType.OxygenGenerator or FurnitureType.WaterRecycler => C(Ramps.Cyan, 4), _ => C(Ramps.Cyan, 3) };
            for (int i = 0; i < 5; i++)
            {
                int bx = wx + 1 + (int)(DotHash.F(type.GetHashCode(), i) * (ww - 2)), by = wy + wh - 1 - (int)((DotHash.F(i, 3) * wh + frame * 1.5f) % Math.Max(1, wh - 1));
                img.Set(bx, by, col);
            }
            if (type is FurnitureType.Oven or FurnitureType.BreadOven) img.Fill(wx + 1, wy + wh / 2, ww - 2, wh / 2, C(Ramps.Fire, 3), 0.55f);
        }
        // 전지 충전 막대 (level = 충전량 · 고장이면 빨간 셀 하나)
        if ((k & FixKit.Cells) != 0)
        {
            int n = Math.Max(2, g.FW / 15);
            int bars = Math.Max(1, (g.FH - 9) / 3);
            for (int i = 0; i < n; i++)
            {
                int cw = (g.FW - 4) / n - 2, x = g.X0 + 2 + i * (cw + 2) + cw / 2 - 1;
                int lit = st == FixState.Off ? 0 : (int)MathF.Round(Math.Clamp(level, 0f, 1f) * bars);
                for (int b = 0; b < bars; b++)
                {
                    bool redCell = broken && i == 1;
                    bool on2 = b < lit && !(redCell && (frame & 1) == 1);
                    var cc = redCell ? C(Ramps.Alarm, on2 ? 4 : 1) : C(Ramps.Led, on2 ? 3 : 1);
                    img.Set(x, g.Bottom - 4 - b * 3, cc); img.Set(x + 1, g.Bottom - 4 - b * 3, cc);
                }
            }
        }
        // 바늘 계기
        if ((k & FixKit.Gauge) != 0)
        {
            float gx = s.Arch == FixArch.Tank ? g.X0 + 6 : g.X1 - 6, gy = g.Front0 + g.FH / 2f;
            float a = on ? -2.4f + 1.5f + 0.2f * MathF.Sin(frame) : broken ? -0.5f + (frame & 1) * 0.6f : -2.4f;
            img.Set((int)(gx + MathF.Cos(a) * 2), (int)(gy + MathF.Sin(a) * 2), C(Ramps.Alarm, 3));
            img.Set((int)gx, (int)gy, C(Ramps.Ink, 1));
        }
        // 노심 · 엔진 · 정제기 빛
        if ((k & FixKit.Glow) != 0)
        {
            float pulse = 0.75f + 0.25f * MathF.Sin(frame * 0.8f);
            float lv = on ? Math.Clamp(level, 0.15f, 1f) : broken ? 0.1f : 0f;
            if (s.Arch == FixArch.Reactor && lv > 0f)
            {
                float cx = g.W / 2f, cy = (g.H + 4) / 2f - 2, R = (Math.Min(g.W, g.H) / 2f - 3) * 0.44f;
                img.Oval(cx, cy, R - 1, R * 0.9f - 1, C(Ramps.Cyan, 2), 1f);
                img.Oval(cx, cy, (R - 2) * (0.5f + 0.5f * lv * pulse), (R * 0.9f - 2) * (0.5f + 0.5f * lv * pulse), C(Ramps.Cyan, 4), 1f);
                img.Oval(cx, cy, 2.5f, 2.2f, C(Ramps.Cyan, 4), 1f);
                img.Glow(cx, cy, R * 2.4f, C(Ramps.Cyan, 3), 0.35f * lv * pulse);
            }
            else if (s.Arch == FixArch.Engine && lv > 0f)
            {
                float cy = (g.Top0 + g.Bottom) / 2f, R = (g.Bottom - g.Top0) * 0.3f * 1.2f;
                img.Fill(0, (int)(cy - R) + 1, 2, (int)(2 * R) - 2, C(Ramps.Fire, 5), 0.6f + 0.3f * pulse * lv);
                img.Glow(1, cy, R * 1.6f, C(Ramps.Fire, 4), 0.35f * lv);
            }
            else if (lv > 0f)
            {
                float cx = g.X0 + g.FW / 2f, cy = g.Front0 + g.FH / 2f;
                img.Oval(cx, cy, 4, 3, C(Ramps.Fire, 4), 0.8f * pulse);
                img.Glow(cx, cy, 10, C(Ramps.Fire, 4), 0.3f * pulse);
            }
        }
        // 물 (수족관 · 탱크 위) · 물고기
        if (type == FurnitureType.Aquarium)
        {
            int x0 = g.X0 + 1, y0 = g.Top0 + 4, x1 = g.X1 - 1, y1 = g.Bottom - 8;
            for (int x = x0; x <= x1; x++) img.Set(x, y0 + ((x + frame) % 5 == 0 ? 1 : 0), C(Ramps.Glass, 4));
            for (int i = 0; i < 3; i++)
            {
                int fx = x0 + 2 + (int)((DotHash.F(i, 7) * (x1 - x0 - 6) + frame * (i % 2 == 0 ? 1 : -1) + 100) % Math.Max(1, x1 - x0 - 5));
                int fy = y0 + 3 + (int)(DotHash.F(i, 8) * Math.Max(1, y1 - y0 - 5));
                var fc = C(i == 0 ? Ramps.ClothOrange : i == 1 ? Ramps.ClothYellow : Ramps.ClothRed, 3);
                img.Set(fx, fy, fc); img.Set(fx + 1, fy, fc); img.Set(fx + 2, fy, fc); img.Set(fx + (i % 2 == 0 ? -1 : 3), fy, C(Ramps.Ink, 1));
            }
            if (on) img.Set(x0 + 2, y1 - ((frame * 2) % Math.Max(1, y1 - y0)), C(Ramps.Glass, 5));
        }
        // 김 (켜지면 위로)
        if ((k & FixKit.Steam) != 0 && on)
            for (int i = 0; i < 3; i++)
            {
                float y = g.Top0 + 2 - ((frame * 2 + i * 4) % 10), x = g.X0 + g.FW * 0.6f + i * 2 + MathF.Sin((frame + i) * 1.3f) * 1.5f;
                img.Oval(x, y, 2.2f, 1.6f, C(Ramps.Smoke, 6), 0.35f);
            }
        // 식물 (자람에 따라 · 작물판 · 식물 벽)
        if ((k & FixKit.Plants) != 0 && s.Arch == FixArch.Planter)
        {
            float grow = Math.Clamp(level, 0f, 1f);
            int y0 = g.Top0 + 2, hh = Math.Max(3, g.TopH - 4);
            for (int x = g.X0 + 3; x < g.X1 - 2; x += 4)
            {
                int ph = 1 + (int)(grow * (6 + DotHash.F(x, 3) * 4));
                int baseY = y0 + hh - 1;
                for (int i = 0; i < ph; i++) img.Set(x + (i > 2 ? ((x / 4) % 2 == 0 ? 1 : -1) : 0), baseY - i, C(Ramps.Leaf, i == ph - 1 ? 4 : 2 + (i & 1)));
                if (grow > 0.5f) { img.Set(x - 1, baseY - ph / 2, C(Ramps.Leaf, 3)); img.Set(x + 1, baseY - ph / 2 - 1, C(Ramps.Leaf, 3)); }
                if (grow > 0.92f) img.Set(x, baseY - ph, C(type == FurnitureType.GrowBed ? Ramps.ClothRed : Ramps.ClothYellow, 4)); // 열매
            }
            if (type == FurnitureType.PlantWall)
                for (int y = g.Front0 + 1; y < g.Bottom - 1; y += 2)
                    for (int x = g.X0 + 1; x < g.X1; x += 2)
                        if (DotHash.F(x, y) < 0.35f + 0.5f * grow) img.Set(x + (y & 1), y, C(Ramps.Leaf, 2 + (int)(DotHash.F(y, x) * 3)));
        }
        // 분사 (제독 샤워 · 세안대)
        if ((k & FixKit.Spray) != 0 && on)
            for (int i = 0; i < 8; i++) img.Blend(g.W / 2 - 4 + (int)(DotHash.F(frame, i) * 9), 3 + (int)(DotHash.F(frame, i, 1) * (g.H - 10)), C(Ramps.Cyan, 4), 0.6f);
        // 조명 (수술 등 · 비상등 · 납땜 등)
        if ((k & FixKit.Lamp) != 0 && on)
        {
            if (type == FurnitureType.SurgicalLamp) img.Glow(g.W / 2f + 8, g.Top0 + 8, 14, C(Ramps.Bandage, 3), 0.45f);
            else if (type == FurnitureType.EmergencyLight) { if ((frame & 2) == 0) img.Glow(g.X0 + g.FW / 2f, g.Front0 + 3, 10, C(Ramps.Alarm, 4), 0.6f); }
            else img.Glow(g.X1 - 9, 2, 6, C(Ramps.Hazard, 4), 0.4f);
        }
        // 접시 · 안테나 끝 불빛 · 렌즈 빛
        if (s.Arch == FixArch.Dish && on)
        {
            if ((frame & 2) == 0) img.Set(g.W / 2, 0, C(Ramps.Alarm, 4));
            if ((k & FixKit.Lens) != 0) img.Glow(g.W / 2f, g.Top0 + 5, 4, C(s.Accent, 3), 0.5f);
        }
        if (s.Arch == FixArch.Pad && on && type != FurnitureType.DeconShower)
        {
            // 거치대 착륙 표시 (도는 빛)
            int y0 = g.H - T + 1;
            float a = frame * 0.8f;
            img.Set((int)(g.W / 2f + MathF.Cos(a) * 9), (int)(y0 + (T - 3) / 2f + MathF.Sin(a) * 9), C(s.Accent, 4));
        }
        return img;
    }
}
