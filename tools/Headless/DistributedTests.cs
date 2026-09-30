using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v11.1 분산 운영: 핵심 방 하나를 잃어도 느리고 비싸게나마 버티나 — 미리 나눠 둔 배 ↔ 한 방에 몰아 둔 배
public static partial class Program
{
    /// <summary>분산 개조를 미리 해 둔다 (보조 작업대 · 예비 조타석 · 산소 발생기 하나를 안쪽 방으로).</summary>
    private static void Distribute(World w)
    {
        var ship = w.Ship;
        var shop = ship.FurnitureOf(FurnitureType.Workbench).First().Room;
        var target = ship.Rooms.First(r => r != shop && !r.Detached && r.Type is RoomType.Storage && Adaptation.BenchCell(w, r) != null);
        ship.AddFurniture(FurnitureType.Workbench, Adaptation.BenchCell(w, target)!.Value).Improved = true;
        var eng = ship.RoomsOf(RoomType.Engine).First();
        var helm = ship.AddFurniture(FurnitureType.Console, Adaptation.BenchCell(w, eng)!.Value);
        helm.AuxHelm = true;
        helm.Label = "예비 조타석";
        var gen = ship.FurnitureOf(FurnitureType.OxygenGenerator).OrderByDescending(f => f.Id).First();
        if (Remodel2.FindSpot(w, gen) is { } spot) Remodel2.Move(w, gen, spot.room, spot.cells);
        w.Paths.Invalidate();
        w.Structure.Touch();
    }

    /// <summary>방을 잃는다: 사람을 빼내고 떼어 낸다 (되찾지 않는다 — 잔해로).</summary>
    private static void LoseRoom(World w, Room room)
    {
        var ship = w.Ship;
        var corridor = ship.RoomsOf(RoomType.Corridor).First();
        var floor = corridor.Cells.Where(ship.IsOpenFloor).ToList();
        int k = 0;
        foreach (var c in w.Crew.Where(c => c.Room == room && !c.Dead))
        {
            c.Position = floor[(k++ * 7) % floor.Count].Center;
            c.PreviousPosition = c.Position;
            c.Interrupt(w);
        }
        foreach (var r in w.Robots.Robots.Where(r => r.Room == room)) r.Position = floor[(k++ * 7) % floor.Count].Center;
        w.Structure.Detach(room, "분산 운영 게이트", controlled: true);
        room.Wreck = true;
    }

    private static int RunDistributedGate(int runs, int seed)
    {
        Console.WriteLine($"분산 운영 게이트 (v11.1) · 한빛호 · {runs}쌍 — 핵심 방 하나를 잃고 나흘\n");
        int wins = 0, total = 0;
        foreach (var what in new[] { RoomType.Workshop, RoomType.Bridge, RoomType.LifeSupport })
        {
            var sums = new Dictionary<bool, List<float>> { [false] = new(), [true] = new() };
            string unit = what switch { RoomType.Workshop => "작업대 있던 시간 %", RoomType.Bridge => "비킴·스침", _ => "최저 산소 kPa" };
            var extra = new Dictionary<bool, List<string>> { [false] = new(), [true] = new() };
            for (int i = 0; i < runs; i++)
            {
                int s = seed + i * 577;
                foreach (bool dist in new[] { false, true })
                {
                    var w = DayOne(s, "Hanbit");
                    if (dist) Distribute(w);
                    Run(w, SimTime.Hours(2));
                    var room = w.Ship.RoomsOf(what).First(r => !r.Detached);
                    float research0 = w.Research;
                    int parts0 = w.Adapt.PartsMade;
                    LoseRoom(w, room);
                    float metric = 0f, minO2 = 99f;
                    int collapses0 = w.History.Collapses, dodged = 0, glanced = 0;
                    var rooms = w.Ship.Rooms.Where(r => !r.Detached && r.Type is RoomType.Quarters or RoomType.Storage or RoomType.Hydroponics or RoomType.Mess or RoomType.Lounge
                                                        && !Remodel2.Interior(w, r)).ToList();
                    float benchHours = 0f;
                    for (int h = 0; h < 96; h++)
                    {
                        // 함교를 잃은 배: 반나절마다 작은 운석 (피할 수 있나)
                        if (what == RoomType.Bridge && h >= 12 && h % 12 == 0)
                            Player.Meteor(w, Scenarios.OuterTarget(w, rooms[(h / 12) % rooms.Count]), 0.4f);
                        Run(w, SimTime.Hours(1));
                        if (w.Ship.FurnitureOf(FurnitureType.Workbench).Any(b => !b.Stowed && !b.Room.Detached && !b.Room.Abandoned && b.Machine!.Efficiency > 0f)) benchHours += 1f;
                        foreach (var r in w.Ship.LiveRooms.Where(r => !r.Leaking && r.Air.Pressure > 85f && w.Crew.Any(c => !c.Dead && c.Room == r)))
                            minO2 = MathF.Min(minO2, r.Air.O2);
                    }
                    dodged = w.Propulsion.Dodged; glanced = w.Propulsion.Glanced;
                    metric = what switch
                    {
                        RoomType.Workshop => benchHours / 96f * 100f,
                        RoomType.Bridge => dodged + glanced,
                        _ => minO2,
                    };
                    sums[dist].Add(metric);
                    extra[dist].Add(what switch
                    {
                        RoomType.Workshop => $"작업대 없이 {96 - benchHours:0}시간 · 연구 {w.Research - research0:0} · 부품 {w.Adapt.PartsMade - parts0} · 임시 정비실 {w.Adapt.Workshops}(금속판 2 + 케이블 1)",
                        RoomType.Bridge => $"회피 {w.Propulsion.Evasions}(비킴 {dodged}·스침 {glanced}) · 조종 {w.Propulsion.Control().by} · 임시 컴퓨터 {(w.Automation.MainOnline ? "켜짐" : "없음")}",
                        _ => $"최저 산소 {minO2:0.0}kPa · 쓰러짐 {w.History.Collapses - collapses0} · 산소 몫 {w.Air.O2Capacity:0}",
                    });
                }
            }
            float plain = sums[false].Average(), spread = sums[true].Average();
            bool ok = spread > plain + MathF.Abs(plain) * 0.01f;
            total++;
            if (ok) wins++;
            Console.WriteLine($"── {RoomTypes.Name(what)}을(를) 잃었다 ({unit}) ──");
            for (int i = 0; i < runs; i++)
                Console.WriteLine($"  시드 {seed + i * 577,-9} 몰아 둔 배 {sums[false][i],6:0.0} ({extra[false][i]})\n                    나눠 둔 배 {sums[true][i],6:0.0} ({extra[true][i]})");
            Console.WriteLine($"  평균: 몰아 둔 배 {plain:0.0} ↔ 나눠 둔 배 {spread:0.0} {(ok ? "✔" : "✘")}\n");
        }
        Console.WriteLine(wins == total ? "✔ 게이트 통과 — 나눠 둔 배가 핵심 방을 잃고도 더 버틴다" : $"✘ {total - wins}개 실패");
        return wins == total ? 0 : 1;
    }
}
