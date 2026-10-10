using System;
using System.Linq;
using ShipSim.Core;

// v13.0 중앙 컴퓨터 대응 수순: V 기본 · 모듈 · 질식 소화 · 진공 소화 · 방침(금지·빈 방만·카운트다운) · 다시 가압 · 공기 구역
public static partial class Program
{
    /// <summary>방 하나에 불을 크게 낸다 (소화조로는 금방 못 잡을 만큼).</summary>
    private static void BigFire(World w, Room room, int cells = 7)
    {
        foreach (var c in room.Cells.Where(w.Ship.IsOpenFloor).Take(cells)) w.Fire.Ignite(c, 0.9f);
    }

    private static Room StoreRoom(World w) => w.Ship.RoomsOf(RoomType.Storage).First();

    private static void ClearRoom(World w, Room room)
    {
        // 그 방 사람을 식당으로 옮긴다
        var mess = w.Ship.RoomsOf(RoomType.Mess).First();
        var spots = mess.Cells.Where(w.Ship.IsOpenFloor).ToList();
        int i = 0;
        foreach (var c in w.Crew.Where(c => c.Room == room))
        {
            c.EndJob(w, ToilStatus.Interrupted);
            c.Position = spots[i++ % spots.Count].Center; c.PreviousPosition = c.Position;
        }
    }

    private static int RunResponseTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"대응 수순 점검 (v13.0) · 시드 {seed}\n");
        try
        {
            // 1) V 기본 · 기본 모듈 둘
            {
                var w = DayOne(seed, "Mirinae");
                var a = w.Automation;
                // v16.20 모듈은 잠금 해제가 아니다 — 첫날부터 전부 (예전: 화재 대응 · 공기 구역 둘만)
                Check("중앙 컴퓨터 — V 지휘가 기본, 화재 대응·공기 구역 모듈 (v16.20: 첫날부터 모듈 전부)", a.Level == 5 && a.Has(ComputerModule.FireResponse) && a.Has(ComputerModule.AirZones) && a.Modules.Count == Enum.GetValues<ComputerModule>().Length,
                    $"{AutomationSystem.LevelName(a.Level)} · 모듈 {string.Join(", ", a.Modules.Select(AutomationSystem.ModuleName))} · 방침 {string.Join(" · ", PolicySystem.All.Select(p => $"{p.Name} {w.Policies.Option(p.Id)}"))}");
            }
            // 2) 질식 소화: 빈 창고에 큰 불 → 가스로 끄고 → 환기로 산소가 돌아온다
            {
                var w = DayOne(seed, "Mirinae");
                var room = StoreRoom(w);
                ClearRoom(w, room);
                float gas0 = -1f;
                BigFire(w, room, 6); // 여덟 칸이 넘으면 컴퓨터는 처음부터 진공을 고른다 (번지는 운을 빼고)
                bool smothered = false; float minO2 = 21f; int minutes = 0;
                for (; minutes < 240; minutes++)
                {
                    Run(w, SimTime.Minutes(1));
                    if (gas0 < 0f) gas0 = w.Automation.InertGas;
                    smothered |= room.Inerting;
                    minO2 = MathF.Min(minO2, room.Air.O2);
                    if (smothered && w.Fire.CountIn(room) == 0 && !room.ResponseHold) break;
                    if (Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "1" && minutes < 12)
                        Console.WriteLine($"   {minutes}분 불 {w.Fire.CountIn(room)} 등급 {w.Automation.Level} ({w.Automation.LevelWhy}) 수순 {string.Join(",", w.Automation.FireCases.Select(f => $"{f.Stage}:{f.Method}:{f.Status}"))} · 자동실행 {w.Policies["autoscope"]} 질식 {w.Policies["inertfire"]}");
                }
                Check("질식 소화 — 소화조로 안 되는 불을 불활성 가스로 끄고, 산소가 돌아오면 격벽을 푼다",
                    smothered && w.Fire.CountIn(room) == 0 && !room.ResponseHold && room.Air.O2 > 17f && w.Automation.InertGas < gas0,
                    $"{minutes}분 · 산소 최저 {minO2:0.0}kPa → 지금 {room.Air.O2:0.0} · 가스 {gas0:0} → {w.Automation.InertGas:0} · 질식 {w.Automation.Smothered} · 진공 {w.Automation.Vacuumed}");
                var first = w.Automation.Reasoning.FirstOrDefault(r => r.text.Contains("질식 소화"));
                Console.WriteLine($"    근거: {first.text}");
            }
            // 3) 진공 소화: 질식 소화를 금지하면 배기 밸브로 공기를 빼서 끈다 → 공기 탱크로 다시 가압
            {
                var w = DayOne(seed, "Mirinae");
                w.Policies.Set("inertfire", 0, "시험");
                var room = StoreRoom(w);
                ClearRoom(w, room);
                BigFire(w, room);
                float tank0 = w.Air.Reserve;
                bool vacuumed = false; float minP = 101f; int minutes = 0;
                for (; minutes < 300; minutes++)
                {
                    Run(w, SimTime.Minutes(1));
                    vacuumed |= room.Purging;
                    minP = MathF.Min(minP, room.Air.Pressure);
                    if (vacuumed && w.Fire.CountIn(room) == 0 && !room.ResponseHold) break;
                }
                bool neighborsOk = w.Ship.LiveRooms.Where(r => r != room && r.Type != RoomType.Corridor).All(r => r.Air.Pressure > 80f);
                Check("진공 소화 — 공기를 바깥으로 빼서 끄고, 공기 탱크로 다시 가압한다 (옆방은 지킨다)",
                    vacuumed && w.Fire.CountIn(room) == 0 && !room.ResponseHold && room.Air.Pressure > 85f && w.Air.Reserve < tank0 && neighborsOk,
                    $"{minutes}분 · 최저 {minP:0}kPa → 지금 {room.Air.Pressure:0} · 공기 탱크 {tank0:0} → {w.Air.Reserve:0} · 옆방 {(neighborsOk ? "멀쩡" : "빠졌다")}");
            }
            // 4) 빈 방만: 안에 쓰러진 사람이 있으면 하지 않는다 (기다리다 소화조에 맡긴다) ↔ 카운트다운이면 시간이 되면 한다
            {
                bool[] executed = new bool[2];
                for (int k = 0; k < 2; k++)
                {
                    var w = DayOne(seed, "Mirinae");
                    w.Policies.Set("inertfire", 0, "시험");
                    w.Policies.Set("vacuumfire", k == 0 ? 1 : 2, "시험");
                    var room = StoreRoom(w);
                    ClearRoom(w, room);
                    var victim = w.Crew.First(c => c.CanAct);
                    victim.EndJob(w, ToilStatus.Interrupted);
                    victim.Position = room.Cells.Where(w.Ship.IsOpenFloor).Last().Center; victim.PreviousPosition = victim.Position;
                    victim.Vitals.Oxygen = 0.1f; victim.Down = true; victim.Pose = Pose.Down;
                    BigFire(w, room, 6);
                    for (int m = 0; m < 12; m++)
                    {
                        Run(w, SimTime.Minutes(1));
                        victim.Down = true; victim.Pose = Pose.Down; // 시험: 구하러 오기 전까지 그대로
                        if (Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "2") Console.WriteLine($"      [{k}] {SimTime.Clock(w.Tick)} 피해자 방 {victim.Room?.Name} 업힘 {victim.CarriedBy?.Name} 빼는 중 {room.Purging} 안 {w.Crew.Count(c => c.Room == room)} · {w.Automation.FireCases.FirstOrDefault()?.Status}");
                        executed[k] |= room.Purging && (k == 1 || victim.Room == room && victim.CarriedBy == null); // 빈 방만: 쓰러진 사람이 안에 있는 채로 빼면 안 된다 (업어 내온 뒤는 괜찮다)
                    }
                }
                Check("방침 — 빈 방만이면 쓰러진 사람이 있는 방은 빼지 않고, 카운트다운이면 시간이 되면 뺀다", !executed[0] && executed[1],
                    $"빈 방만: {(executed[0] ? "뺐다" : "기다렸다")} · 카운트다운 뒤: {(executed[1] ? "뺐다" : "기다렸다")}");
            }
            // 5) 안에 깨어 있는 사람은 경보를 듣고 나가고, 그다음에 뺀다
            {
                var w = DayOne(seed, "Mirinae");
                w.Policies.Set("inertfire", 0, "시험");
                var room = StoreRoom(w);
                ClearRoom(w, room);
                var c = w.Crew.First(x => x.CanAct && x.IsAwake);
                c.EndJob(w, ToilStatus.Interrupted);
                // 통합8 불붙을 칸(앞 여섯) 위에 세우면 첫 분에 옷에 옮겨붙어 쓰러진다 — 나갈 사람이 아니게 된다. 문 가까이 · 불에서 두 칸 넘게
                var rcs = room.Cells.Where(w.Ship.IsOpenFloor).ToList();
                var rdoor = room.Doors.Where(d => !d.IsExternal).Select(d => d.Cell).FirstOrDefault();
                var rspot = rcs.Skip(6).Where(x => rcs.Take(6).Min(f => Math.Abs(f.X - x.X) + Math.Abs(f.Y - x.Y)) >= 2).OrderBy(x => Math.Abs(x.X - rdoor.X) + Math.Abs(x.Y - rdoor.Y)).ThenBy(x => x.X).ThenBy(x => x.Y).FirstOrDefault();
                if (rspot == default) rspot = rcs.Last();
                c.Position = rspot.Center; c.PreviousPosition = c.Position;
                BigFire(w, room, 6);
                bool left = false, purged = false;
                for (int m = 0; m < 30 && !purged; m++)
                {
                    Run(w, SimTime.Minutes(1));
                    if (c.Room != room) left = true;
                    if (room.Purging) purged = true;
                }
                Check("대피 — 소화 경보를 들은 사람이 나간 뒤에 뺀다", left && purged && !c.Dead && c.Room != room, $"{c.Name}: {(left ? "나갔다" : "남았다")} · 진공 {(purged ? "했다" : "안 했다")}");
            }
            // 6) 금지: 둘 다 금지면 컴퓨터는 하지 않고 사람이 끈다
            {
                var w = DayOne(seed, "Mirinae");
                w.Policies.Set("inertfire", 0, "시험");
                w.Policies.Set("vacuumfire", 0, "시험");
                var room = StoreRoom(w);
                ClearRoom(w, room);
                BigFire(w, room, 3);
                int m = 0;
                for (; m < 180 && w.Fire.CountIn(room) > 0; m++) Run(w, SimTime.Minutes(1));
                Check("방침 — 금지면 질식·진공 소화를 하지 않는다", w.Automation.Smothered == 0 && w.Automation.Vacuumed == 0,
                    $"질식 {w.Automation.Smothered} · 진공 {w.Automation.Vacuumed} · 불 {(w.Fire.CountIn(room) == 0 ? $"{m}분에 사람이 껐다" : "남았다")}");
            }
            // 7) 공기 구역: 여러 방이 새면 생명유지실이 있는 구역을 지킨다
            {
                var w = DayOne(seed, "Mirinae");
                foreach (var t in new[] { RoomType.Storage, RoomType.Lounge })
                {
                    var room = w.Ship.RoomsOf(t).First();
                    var wall = w.Ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) == room).Select(kv => kv.Key).First();
                    Hull.Damage(w.Ship, wall, 1.2f);
                }
                Run(w, SimTime.Minutes(5));
                var a = w.Automation;
                bool life = w.Ship.RoomsOf(RoomType.LifeSupport).All(a.InZone);
                Check("공기 구역 — 여러 방이 새면 생명유지실이 있는 구역을 정해 사람을 모은다", a.ZoneActive && life && a.ZonesDeclared >= 1,
                    a.ZoneNote + $" · 구역 방 {a.Zone.Count}");
            }
            // 8) 구역 포기 시점 방침
            {
                var w = DayOne(seed, "Mirinae");
                float[] h = new float[3];
                for (int k = 0; k < 3; k++) { w.Policies.Set("zoneabandon", k, "시험"); h[k] = w.Policies.AbandonHours; }
                Check("방침 — 구역 포기 시점 (일찍·보통·끝까지)", h[0] < h[1] && float.IsInfinity(h[2]) && w.Policies.Changes.Count >= 2,
                    $"{h[0]}시간 · {h[1]}시간 · {(float.IsInfinity(h[2]) ? "끝까지" : h[2].ToString())} · 바뀐 이력 {w.Policies.Changes.Count}");
            }
            // 9) 결정론
            {
                uint H()
                {
                    var w = DayOne(seed, "Mirinae");
                    BigFire(w, StoreRoom(w));
                    Run(w, SimTime.Hours(2));
                    return SaveGame.StateHash(w);
                }
                uint x = H(), y = H();
                Check("결정론 — 소화 대응이 든 배도 같은 시드 같은 지문", x == y, $"{x:x8} / {y:x8}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"예외: {e}");
            _fails++;
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 대응 수순 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
