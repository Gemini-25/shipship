using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.19 측정: 기본 배 5척 × 시드 × 며칠 — 정전 · 연쇄 · 감압 번짐 · 고장 간격 · 운석우 붕괴 비율 · 사망 원인
public static partial class Program
{
    private sealed class FsProbe
    {
        public int Outages, Deaths, Faults, RobotFaults, DroneFaults, Escalated, BigCases, DecoEpisodes, DecoMaxRooms, DecoDragged;
        public float DarkEssH, DarkRoomH, Days, CollapseMax, DecoRoomsSum;
        public readonly Dictionary<string, int> Causes = new();
        public readonly int[] Peaks = new int[5];
        public readonly float[] DarkWhy = new float[5]; // 회로 · 간선 · 분전함 · 끊음 · 모자람
        public void Add(FsProbe o)
        {
            for (int i = 0; i < 5; i++) DarkWhy[i] += o.DarkWhy[i];
            Outages += o.Outages; Deaths += o.Deaths; Faults += o.Faults; RobotFaults += o.RobotFaults; DroneFaults += o.DroneFaults;
            Escalated += o.Escalated; BigCases += o.BigCases; DecoEpisodes += o.DecoEpisodes; DecoMaxRooms = Math.Max(DecoMaxRooms, o.DecoMaxRooms);
            DecoDragged += o.DecoDragged; DarkEssH += o.DarkEssH; DarkRoomH += o.DarkRoomH; Days += o.Days; CollapseMax = MathF.Max(CollapseMax, o.CollapseMax);
            DecoRoomsSum += o.DecoRoomsSum;
            foreach (var (k, v) in o.Causes) Causes[k] = Causes.GetValueOrDefault(k) + v;
            for (int i = 0; i < 5; i++) Peaks[i] += o.Peaks[i];
        }
    }

    private static bool FsEssential(World w, Room r) =>
        r.Type is RoomType.LifeSupport or RoomType.Bridge or RoomType.Medbay || r.Furniture.Any(f => f.Type == FurnitureType.MainComputer);

    /// <summary>세계를 ticks만큼 돌리며 1분마다 잰다.</summary>
    private static FsProbe FsWatch(World w, long ticks)
    {
        var p = new FsProbe { Days = ticks / (float)SimTime.TicksPerDay };
        var rooms = w.Ship.Rooms.Where(r => !r.Detached && r.Type != RoomType.Corridor).ToList();
        var ess = rooms.Where(r => FsEssential(w, r)).ToList();
        int faults0 = w.Ship.Machines.Sum(m => m.FaultCount);
        var robotFault = w.Robots.Robots.ToDictionary(r => r.Id, r => r.Fault != null);
        var droneBad = w.Drones.Drones.ToDictionary(d => d.Id, d => d.Faulty || d.Wrecked);
        int cases0 = w.Scale.Cases.Count > 0 ? w.Scale.Cases.Max(c => c.Id) : -1;
        var deadBefore = w.Crew.Where(c => c.Dead).Select(c => c.Id).ToHashSet();
        bool essDark = false, inDeco = false;
        var decoSeen = new HashSet<int>();
        var decoLeak = new HashSet<int>();
        int step = SimTime.Minutes(1);
        for (long t = 0; t < ticks; t++)
        {
            w.Step();
            if (t % step != 0) continue;
            float hrs = step / (float)SimTime.TicksPerHour;
            int dEss = 0, dAll = 0, low = 0, lost = 0;
            foreach (var r in rooms)
            {
                bool dark = !r.Powered && !r.Detached;
                bool lowP = r.Detached || r.Air.Pressure < 70f;
                if (dark)
                {
                    dAll++;
                    int why = !w.Power.CircuitFed[r.Circuit] ? 0 : !r.PowerLinked ? 1 : r.BreakerOff ? 2 : r.PowerCut ? 3 : 4;
                    p.DarkWhy[why] += hrs;
                }
                if (lowP) low++;
                if (dark || lowP) lost++;
                if (lowP)
                {
                    decoSeen.Add(r.Id);
                    if (r.Leaking || r.Detached) decoLeak.Add(r.Id);
                }
            }
            foreach (var r in ess) if (!r.Powered && !r.Detached) dEss++;
            p.DarkEssH += dEss * hrs;
            p.DarkRoomH += dAll * hrs;
            if (dEss > 0 && !essDark) p.Outages++;
            essDark = dEss > 0;
            p.CollapseMax = MathF.Max(p.CollapseMax, lost / (float)Math.Max(1, rooms.Count));
            if (low > 0 && !inDeco) { inDeco = true; decoSeen.Clear(); decoLeak.Clear(); foreach (var r in rooms) if (r.Air.Pressure < 70f || r.Detached) { decoSeen.Add(r.Id); if (r.Leaking || r.Detached) decoLeak.Add(r.Id); } }
            if (low == 0 && inDeco)
            {
                inDeco = false;
                p.DecoEpisodes++;
                p.DecoRoomsSum += decoSeen.Count;
                p.DecoMaxRooms = Math.Max(p.DecoMaxRooms, decoSeen.Count);
                p.DecoDragged += decoSeen.Count(id => !decoLeak.Contains(id));
            }
            foreach (var r in w.Robots.Robots)
            {
                bool f = r.Fault != null;
                if (f && !robotFault.GetValueOrDefault(r.Id)) p.RobotFaults++;
                robotFault[r.Id] = f;
            }
            foreach (var d in w.Drones.Drones)
            {
                bool f = d.Faulty || d.Wrecked;
                if (f && !droneBad.GetValueOrDefault(d.Id)) p.DroneFaults++;
                droneBad[d.Id] = f;
            }
        }
        if (inDeco)
        {
            p.DecoEpisodes++;
            p.DecoRoomsSum += decoSeen.Count;
            p.DecoMaxRooms = Math.Max(p.DecoMaxRooms, decoSeen.Count);
            p.DecoDragged += decoSeen.Count(id => !decoLeak.Contains(id));
        }
        p.Faults = w.Ship.Machines.Sum(m => m.FaultCount) - faults0;
        foreach (var k in w.Scale.Cases.Where(c => c.Id > cases0))
        {
            p.Peaks[(int)k.Peak]++;
            if (k.Peak >= IncidentScale.System && k.Base <= IncidentScale.Room) p.Escalated++;
            if (k.Peak >= IncidentScale.System) p.BigCases++;
        }
        foreach (var c in w.Crew.Where(c => c.Dead && !deadBefore.Contains(c.Id)))
        {
            p.Deaths++;
            string why = c.Vitals.InjuryCause ?? "사고";
            p.Causes[why] = p.Causes.GetValueOrDefault(why) + 1;
        }
        return p;
    }

    private static readonly string[] FsShips = { "Kestrel", "Mirinae", "Hanbit", "Eunha", "Cheonma" };
    private static readonly string[] FsBundle = { "MeteorShower", "fire", "PowerSurge", "pipe" };

    /// <summary>보통 재해 하나를 첫날 뒤에 걸고 hours 동안 본다.</summary>
    private static FsProbe FsDisaster(int seed, string ship, string key, float hours)
    {
        var w = DayOne(seed, ship);
        w.Hazards.FireStory(key, null);
        return FsWatch(w, SimTime.Hours(hours));
    }

    /// <summary>항해: 이야기꾼(꾸준형 · 보통)을 켜고 days 동안.</summary>
    private static FsProbe FsVoyage(int seed, string ship, float days)
    {
        float p0 = Storyteller.PersonaValue, l0 = Storyteller.LevelValue;
        try
        {
            Storyteller.PersonaValue = 1; Storyteller.LevelValue = 3;
            var w = DayOne(seed, ship);
            return FsWatch(w, (long)(SimTime.TicksPerDay * days));
        }
        finally { Storyteller.PersonaValue = p0; Storyteller.LevelValue = l0; }
    }

    private static string FsLine(string name, FsProbe p, int runs) =>
        $"{name,-10} 런 {runs,2} · 사망 {p.Deaths,2} ({p.Deaths / (float)Math.Max(1, runs):0.00}/런) · 필수방 정전 {p.Outages,3}회 {p.DarkEssH:0.0}방시간 · 정전 방시간 {p.DarkRoomH:0.0}"
        + $" · 감압 {p.DecoEpisodes}회 평균 {p.DecoRoomsSum / Math.Max(1, p.DecoEpisodes):0.0}방 최대 {p.DecoMaxRooms} 끌려간 방 {p.DecoDragged}"
        + $" · 고장 설비 {p.Faults} 로봇 {p.RobotFaults} 드론 {p.DroneFaults} · 계통↑ {p.BigCases} 번짐 {p.Escalated} · 붕괴 최대 {p.CollapseMax * 100:0}%"
        + $" · 규모 [{string.Join(" ", p.Peaks)}] · 정전 까닭(회로/간선/분전함/끊음/모자람) [{string.Join(" ", p.DarkWhy.Select(x => x.ToString("0")))}]"
        + (p.Causes.Count > 0 ? " · 사인 " + string.Join(", ", p.Causes.OrderByDescending(x => x.Value).Select(x => $"{x.Key}×{x.Value}")) : "");

    /// <summary>측정 표 (--failsafetest --measure[=일수] [--seeds=n]).</summary>
    private static int RunFailsafeMeasure(int seed, float days, int seeds, string[]? only = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Console.WriteLine($"v16.19 측정 · 시드 {seed}부터 {seeds}개 · 항해 {days}일 · 재해 묶음 {string.Join("/", FsBundle)} (각 10시간){(Durability.Legacy ? " · 예전 값" : "")}\n");
        var allV = new FsProbe();
        var allD = new Dictionary<string, FsProbe>();
        int nv = 0;
        foreach (var ship in only ?? FsShips)
        {
            var sv = new FsProbe();
            var sd = new FsProbe();
            for (int s = 0; s < seeds; s++)
            {
                if (days > 0f) { var v = FsVoyage(seed + s * 4, ship, days); sv.Add(v); nv++; }
                foreach (var key in FsBundle)
                {
                    var d = FsDisaster(seed + s * 4, ship, key, 10f);
                    sd.Add(d);
                    if (!allD.TryGetValue(key, out var a)) allD[key] = a = new FsProbe();
                    a.Add(d);
                }
            }
            if (days > 0f) Console.WriteLine(FsLine($"{ship} 항해", sv, seeds));
            Console.WriteLine(FsLine($"{ship} 재해", sd, seeds * FsBundle.Length));
            allV.Add(sv);
            Console.Out.Flush();
        }
        Console.WriteLine();
        if (nv > 0) Console.WriteLine(FsLine("항해 전체", allV, nv));
        foreach (var (k, p) in allD) Console.WriteLine(FsLine(k, p, (only ?? FsShips).Length * seeds));
        Console.WriteLine($"\n{sw.Elapsed.TotalSeconds:0}초");
        return 0;
    }
}
