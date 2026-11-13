using ShipEngine;
using ShipSim.Art;

namespace ShipView;

// 새 그림 (매끈한 반입체 — docs/GRAPHICS.md '화풍 다시 보기'): 도트 그림을 찍는 대신 "무엇을 어디에 그릴지"만 세계에서 읽어 싣는다.
// 그리기(선과 면 · 빛을 곱하기)는 게임 쪽(그래픽 카드)이 한다. 여기에는 고도 타입이 없다 — 실험실 시험이 이 값을 그대로 본다.
// 원칙은 그대로: 그림은 모두 세계의 값에서 (사람 자리 · 문 열림 · 방 조명 · 장치 상태 · 불).

/// <summary>설비 묶음 — 묶음마다 한 틀로 그린다 (106가지를 그 변형으로).</summary>
public enum VCat : byte { Bed, MedBed, Seat, Table, Game, Bench, Books, Store, Small, Stove, Steel, Grow, Aqua, Reactor, Engine, Screen, Battery, Dock, Tech, Toilet, Sink, Shower }

/// <summary>설비 하나: 차지한 칸 상자 (X1 · Y1은 끝 다음) · 높이 (도트 — 칸은 32) · 스스로 빛나나.</summary>
public readonly record struct VFurn(int Id, string Type, VCat Cat, int X0, int Y0, int X1, int Y1, int Height, bool Glows);

public sealed class VRoomInfo
{
    public string Type = "", Name = "";
    public float LabelX, LabelY;   // 이름표 자리 (칸 · 소수)
    public uint Floor;             // 바닥 빛깔 0xRRGGBB
}

/// <summary>배마다 한 번 짓는 것 — 바뀌지 않는 도면.</summary>
public sealed class VStatic
{
    public const byte Space = 0, Floor = 1, Wall = 2, Hull = 3, Door = 4;
    public int W, H;
    public byte[] Kind = Array.Empty<byte>();
    public int[] Room = Array.Empty<int>();
    public VRoomInfo[] Rooms = Array.Empty<VRoomInfo>();
    public VFurn[] Furn = Array.Empty<VFurn>();
    public (int X, int Y, bool Vertical, bool External)[] Doors = Array.Empty<(int, int, bool, bool)>();

    public byte At(int x, int y) => (uint)x < (uint)W && (uint)y < (uint)H ? Kind[y * W + x] : Space;
    public bool Solid(int x, int y) { byte k = At(x, y); return k == Wall || k == Hull; }

    public static VStatic Build(Layout L)
    {
        var s = new VStatic { W = L.W, H = L.H, Kind = new byte[L.N], Room = (int[])L.Room.Clone() };
        for (int c = 0; c < L.N; c++)
        {
            var k = L.Kind[c];
            if (k == CellKind.Floor) s.Kind[c] = Floor;
            else if (k == CellKind.Door) s.Kind[c] = Door;
            else if (k == CellKind.Wall)
            {
                // 선체: 바깥(빈 칸 · 도면 밖)과 맞닿은 벽 (대각선까지)
                bool hull = false;
                int x = L.X(c), y = L.Y(c);
                for (int dy = -1; dy <= 1 && !hull; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (!L.In(x + dx, y + dy) || L.Kind[L.Index(x + dx, y + dy)] == CellKind.Outside) { hull = true; break; }
                s.Kind[c] = hull ? Hull : Wall;
            }
        }
        s.Rooms = L.Rooms.Select(r =>
        {
            var info = new VRoomInfo { Type = r.Type, Name = r.Name, Floor = FloorOf(r.Type) };
            if (r.Cells.Length > 0)
            {
                double mx = r.Cells.Average(c => (double)L.X(c));
                int top = int.MaxValue;
                foreach (int c in r.Cells) if (Math.Abs(L.X(c) - mx) <= 1.2 && L.Y(c) < top) top = L.Y(c);
                if (top == int.MaxValue) top = r.Cells.Min(c => L.Y(c));
                info.LabelX = (float)mx + 0.5f; info.LabelY = top + 0.5f;
            }
            return info;
        }).ToArray();
        s.Furn = L.Furniture.Where(f => f.Cells.Length > 0).Select(f =>
        {
            int x0 = f.Cells.Min(c => L.X(c)), y0 = f.Cells.Min(c => L.Y(c)), x1 = f.Cells.Max(c => L.X(c)) + 1, y1 = f.Cells.Max(c => L.Y(c)) + 1;
            var cat = CatOf(f.Type);
            return new VFurn(f.Id, f.Type, cat, x0, y0, x1, y1, HeightOf(cat), cat is VCat.Reactor or VCat.Engine or VCat.Screen or VCat.Aqua or VCat.Battery or VCat.Stove);
        }).ToArray();
        s.Doors = L.Doors.Select(d => (L.X(d.Cell), L.Y(d.Cell), d.Vertical, d.External)).ToArray();
        return s;
    }

    public static VCat CatOf(string t) => t switch
    {
        "MedBed" => VCat.MedBed,
        "GrowBed" or "PlantRack" or "Composter" or "NutrientDoser" => VCat.Grow,
        "Bed" => VCat.Bed,
        "GameTable" => VCat.Game,
        "Workbench" => VCat.Bench,
        "Bookshelf" => VCat.Books,
        "OxygenMaskBox" or "EmergencyLight" or "PestTrap" or "ThermalCamera" or "SmokeDetector" => VCat.Small,
        "Stove" or "Oven" => VCat.Stove,
        "Fridge" or "IceMaker" or "CoffeeMachine" or "MealDispenser" or "Fermenter" or "EyeWash" or "WaterTank" => VCat.Steel,
        "Aquarium" => VCat.Aqua,
        "ReactorCore" or "LargeReactorCore" => VCat.Reactor,
        "EngineCore" => VCat.Engine,
        "Battery" => VCat.Battery,
        "Toilet" => VCat.Toilet,
        "Sink" => VCat.Sink,
        "Shower" => VCat.Shower,
        _ when t.EndsWith("Dock") => VCat.Dock,
        _ when t.Contains("Seat") || t.Contains("Chair") => VCat.Seat,
        _ when t.StartsWith("Table") || t == "MeetingBoard" => VCat.Table,
        _ when t.Contains("Console") || t.Contains("Scanner") || t.Contains("Arcade") || t == "MusicCorner" || t == "MainComputer" || t.Contains("Monitor") => VCat.Screen,
        _ when t.Contains("Shelf") || t.Contains("Locker") || t.Contains("Cabinet") || t.Contains("Cache") || t == "CargoNet" || t.Contains("Rack") || t == "ToolWall" || t.Contains("Crate") => VCat.Store,
        _ => VCat.Tech,
    };

    public static int HeightOf(VCat c) => c switch
    {
        VCat.Bed => 7, VCat.MedBed => 9, VCat.Seat => 9, VCat.Table => 10, VCat.Game => 10, VCat.Bench => 11, VCat.Books => 20, VCat.Store => 18, VCat.Small => 0,
        VCat.Stove => 13, VCat.Steel => 20, VCat.Grow => 9, VCat.Aqua => 14, VCat.Reactor => 16, VCat.Engine => 14, VCat.Screen => 14, VCat.Battery => 16, VCat.Dock => 5,
        VCat.Toilet => 9, VCat.Sink => 12, VCat.Shower => 4, _ => 15,
    };

    /// <summary>바닥 빛깔: 채도를 낮춘 묶음 (생활 따뜻하게 · 일 차게 · 재배 녹 · 의무 · 화장실 밝은 청).</summary>
    public static uint FloorOf(string type) => type switch
    {
        "Corridor" => 0x566176, "Quarters" => 0x8a6e62, "Lounge" or "Chapel" => 0x8b7763, "Mess" => 0xa3937d, "Galley" => 0xa89c8a, "Medbay" => 0x93a8b3,
        "Hydroponics" => 0x5f7a66, "Storage" or "Cargo" or "OreHold" => 0x6c7480, "LifeSupport" or "Cooling" => 0x5f7382, "Bridge" => 0x4c5f7c,
        "Engine" => 0x4a4f5a, "Reactor" => 0x474c58, "Power" => 0x4d525e, "Workshop" or "Foundry" or "DroneBay" => 0x5b5a58, "Washroom" => 0x9bb0ba,
        "Gear" or "Hangar" => 0x636d7b, "Airlock" => 0x5a6472, "Comms" or "ComputerRoom" or "Observatory" => 0x55637a, "Lab" => 0x7d8ea0, "Gym" or "School" => 0x7f7a70,
        _ => 0x5a6270,
    };
}

/// <summary>사람 · 로봇 하나 (그릴 자리는 틱 사이를 메운 값).</summary>
public struct VPerson
{
    public int Id;
    public float X, Y;
    public bool Person, Down, Dead, Asleep, Suit, Moving;
    public uint Shirt, Skin, Hair;
    public string Name;
    public byte Face;   // DotFace: 0 차분 · 웃음 · 겁 · 아픔 · 슬픔
}

/// <summary>한 장에 실리는 것 — 세계의 지금.</summary>
public sealed class VFrame
{
    public VStatic S = null!;
    public VPerson[] People = Array.Empty<VPerson>();
    /// <summary>문마다 열린 몫 0~1.</summary>
    public float[] Door = Array.Empty<float>();
    /// <summary>칸마다 곱할 빛 (R · G · B — 255가 그대로). 도트 그림과 같은 빛 지도: 방 조명 · 밤 · 비상등 · 연기 · 설비 · 불.</summary>
    public byte[] Light = Array.Empty<byte>();
    /// <summary>설비마다: 0 장치 아님 · 1 돌아간다 · 2 꺼짐 · 전기 없음 · 3 망가짐 (VStatic.Furn과 같은 차례).</summary>
    public byte[] Furn = Array.Empty<byte>();
    /// <summary>불난 칸: 칸 · 세기 (kW).</summary>
    public (int Cell, float Hrr)[] Fires = Array.Empty<(int, float)>();
    public long Tick;
    public double Anim;
}

public sealed partial class Painter
{
    private VStatic? _vstatic;
    public VStatic VectorStatic => _vstatic ??= VStatic.Build(L);

    /// <summary>칸마다 곱할 빛 — 도트 그림이 쓰는 빛 지도를 그대로 (배 전체). 벽 칸은 맞닿은 빈 칸의 빛.</summary>
    public void LightCells(Sim s, int frame, byte[] rgb)
    {
        var f = s.Get<Field>();
        BuildLight(s, f, s.Find<Network>(), s.Find<Light>(), s.Find<Chemistry>(), s.Find<Structure>(), 0, 0, L.W, L.H, frame);
        WallLight();
        float ar = Ambient * AmbientTint.r, ag = Ambient * AmbientTint.g, ab = Ambient * AmbientTint.b, keep = 1 - Ambient;
        for (int y = 0; y < L.H; y++)
            for (int x = 0; x < L.W; x++)
            {
                int i = (y + 1) * _mw + x + 1, o = (y * L.W + x) * 3;
                if (_lk[i] == 0) { rgb[o] = rgb[o + 1] = rgb[o + 2] = 255; continue; }   // 배 밖은 그대로 (우주)
                rgb[o] = (byte)Math.Clamp((ar + keep * _lr[i]) * 255, 0, 255);
                rgb[o + 1] = (byte)Math.Clamp((ag + keep * _lg[i]) * 255, 0, 255);
                rgb[o + 2] = (byte)Math.Clamp((ab + keep * _lb[i]) * 255, 0, 255);
            }
    }

    private static uint Rgb(Rgba c) => (uint)(c.R << 16 | c.G << 8 | c.B);

    /// <summary>새 그림 한 장의 값 (세계를 읽기만 한다). at: 사람마다 그릴 자리 (틱 사이를 메운 것).</summary>
    public VFrame VectorFrame(Sim s, int frame, double anim, Func<int, (double x, double y)> at)
    {
        var st = VectorStatic;
        var v = new VFrame { S = st, Tick = s.Tick, Anim = anim, Light = new byte[L.N * 3], Door = new float[L.Doors.Count], Furn = new byte[st.Furn.Length] };
        LightCells(s, frame, v.Light);
        var f = s.Get<Field>();
        for (int d = 0; d < v.Door.Length; d++) v.Door[d] = f.DoorOpen[d];
        var net = s.Find<Network>();
        if (net != null)
            for (int i = 0; i < st.Furn.Length; i++)
            {
                int dev = DeviceOf(net, st.Furn[i].Id);
                if (dev < 0) continue;
                v.Furn[i] = net.IsBroken(dev, Part.Electric) || net.IsBroken(dev, Part.Mechanical) ? (byte)3 : net.Working(dev) ? (byte)1 : (byte)2;
            }
        var chem = s.Find<Chemistry>();
        if (chem != null)
        {
            var fires = new List<(int, float)>();
            for (int c = 0; c < L.N; c++) if (chem.Hrr[c] >= 5) fires.Add((c, chem.Hrr[c]));
            v.Fires = fires.ToArray();
        }
        var mind = s.Find<Mind>(); var life = s.Find<Life>();
        if (mind != null)
        {
            v.People = new VPerson[mind.Agents.Count];
            for (int i = 0; i < v.People.Length; i++)
            {
                var a = mind.Agents[i];
                var (x, y) = at(a.Id);
                var p = new VPerson { Id = a.Id, X = (float)x, Y = (float)y, Person = a.IsPerson, Name = mind.NameOf(a.Id), Moving = a.PathIdx < a.Path.Count && a.C.Up != 0 };
                if (a.IsPerson && life != null)
                {
                    var b = life.Bodies[a.C.Body];
                    var l = LookOf(mind, a);
                    p.Dead = b.Alive == 0; p.Asleep = b.Asleep != 0; p.Suit = b.Suit != 0;
                    p.Down = p.Dead || b.Conscious == 0 || p.Asleep || a.C.Up == 0;
                    p.Shirt = Rgb(Ramps.Get(l.Cloth, 3)); p.Hair = Rgb(Ramps.Get(l.HairRamp, 1));
                    p.Skin = Rgb(Ramps.Get(p.Dead ? Ramps.DeadSkin : Ramps.Skin[l.Skin], 3));
                    p.Face = (byte)(p.Down ? DotFace.Calm : FaceOf(a.C, b));
                }
                else { p.Shirt = 0xd9a441; p.Skin = 0xc9ced6; p.Hair = 0x3a4252; p.Down = a.C.Up == 0; }
                v.People[i] = p;
            }
        }
        return v;
    }
}
