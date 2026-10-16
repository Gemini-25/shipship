using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v14.8 배관 · 배선 전달량 · 우회의 한계 · 접촉 저항 · 역류 · 같은 길 보조 간선 · 문 균압 · 에어락 절차
public static partial class Program
{
    private static int RunFlowTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"배관·배선 전달량 점검 (v14.8) · 시드 {seed}\n");
        try
        {
            static void Settle(World w) { w.Net.Update(0f); w.Flow.Update(0.01f); }
            static Room Source(World w, NetKind k) => w.Net.SourceRoom(k)!;

            // 1) 평소에는 모자라지 않게 깔았다
            {
                foreach (var ship in new[] { "Mirinae", "Hanbit" })
                {
                    var w = DayOne(seed, ship);
                    Run(w, SimTime.Hours(2));
                    var rooms = w.Ship.LiveRooms.ToList();
                    float minP = rooms.Min(r => r.PowerFlow), minW = rooms.Where(UtilityNet.NeedsWater).Select(r => r.WaterFlow).DefaultIfEmpty(1f).Min();
                    var far = rooms.Where(UtilityNet.NeedsWater).OrderBy(r => r.WaterFlow).First();
                    Check($"평소 ({ship}) — 설계한 만큼은 흐른다 (전압 강하 없음 · 먼 방은 수압이 조금 준다)", w.Flow.Stats.Brownouts == 0 && minP >= 0.75f && minW >= 0.6f,
                        $"가장 낮은 전압 {minP * 100:0}% · 가장 낮은 수압 {minW * 100:0}% ({far.Name}, 문 {w.Flow.Share(NetKind.Water, far).Doors}개 너머) · {w.Flow.Stats.Summary()}");
                }
            }

            // 2) 우회의 한계: 주 간선이 끊겨 보조 간선으로 돌면, 가늘어서 모자란다
            {
                var w = DayOne(seed, "Mirinae");
                var src = Source(w, NetKind.Power);
                var ring = w.Net.Links.FirstOrDefault(l => l.Kind == NetKind.Power && UtilityNet.IsRing(l));
                if (ring == null)
                {
                    var t0 = w.Net.RingTargets(NetKind.Power).First();
                    w.Net.AddRing(NetKind.Power, src, t0);
                    Settle(w);
                    ring = w.Net.Links.First(l => l.Kind == NetKind.Power && UtilityNet.IsRing(l));
                }
                int to = int.Parse(ring.Key.Split(':')[2].Split('-')[1]);
                var target = w.Ship.Rooms.First(r => r.Id == to);
                var m = target.Furniture.Select(f => f.Machine).FirstOrDefault(x => x != null && x.Spec.PowerDraw > 0f);
                float effBefore = m?.Efficiency ?? 0f;
                foreach (var l in w.Net.Links.Where(l => l.Kind == NetKind.Power && !UtilityNet.IsRing(l) && l.Door != null && (l.Door.RoomA == target || l.Door.RoomB == target)).ToList())
                    w.Net.Hurt(l, 1f, "시험");
                Settle(w);
                var share = w.Flow.Share(NetKind.Power, target);
                float effAfter = m?.Efficiency ?? 0f;
                Check("우회의 한계 — 주 간선이 끊기면 보조 간선으로 돌지만 가늘어 전압이 떨어지고 설비가 덜 돈다",
                    target.PowerLinked && share.ViaRing && target.PowerFlow < 0.75f && w.Flow.Stats.Bypassed > 0 && (m == null || effAfter < effBefore),
                    $"{target.Name} 전압 {target.PowerFlow * 100:0}% ({share.Why}) · {m?.Name ?? "설비 없음"} 효율 {effBefore * 100:0}% → {effAfter * 100:0}% · {w.Flow.Stats.Summary()}");
            }

            // 3) 접촉 저항: 많은 방을 나르는 간선을 임시로 이으면 이음매가 달아오른다 → 제대로 다시 잇는다
            {
                var w = DayOne(seed, "Mirinae");
                Settle(w);
                var src = Source(w, NetKind.Power);
                // 가장 많은 방이 지나는 토막 (공급원 바로 앞)
                var t = w.Net.Throughput(NetKind.Power);
                var trunk = w.Net.Links.Where(l => l.Kind == NetKind.Power && !UtilityNet.IsRing(l) && l.Door != null && (l.Door.RoomA == src || l.Door.RoomB == src) && l.Key.EndsWith(":X")).First();
                var splicer = w.Crew.First(c => c.CanAct && !c.IsChild);
                trunk.Temp = true; trunk.Integrity = 0.75f; trunk.SplicedBy = splicer.Id; // 통합8 0.6으로 이으면 보조 간선(굵기 0.55)보다 가늘어 전기가 고리로 돌아 이음매를 안 지난다 — 그럭저럭 이은 이음매라야 많은 방을 나른다
                Settle(w);
                int dimmed = w.Ship.LiveRooms.Count(r => r.PowerFlow < 0.75f);
                float peak = 0f;
                bool hotSeen = false;
                for (int m = 0; m < 12 * 60 && trunk.Temp; m++)
                {
                    Run(w, SimTime.Minutes(1));
                    peak = MathF.Max(peak, w.Flow.SpliceHeat(trunk));
                    hotSeen |= w.Flow.Hot(trunk);
                }
                Check("접촉 저항 — 많은 방을 나르는 임시 이음은 달아오르고(이은 사람이 남는다), 급해져서 제대로 다시 잇는다",
                    w.Flow.Stats.SpliceHot > 0 && !trunk.Temp && dimmed > 0,
                    $"임시로 이은 {trunk.Room.Name} 토막 ({splicer.Name}) · 흐려진 방 {dimmed} · 이음매 열 최고 {peak * 100:0}% · 지금 {(trunk.Temp ? "임시 그대로" : "제대로 이음")} · {w.Flow.Stats.Summary()}");
            }

            // 4) 역류: 수압이 떨어진 의무실의 더러운 물이 급수관으로 → 씻어 낸다 (마신 사람은 탈이 나기도)
            {
                var w = DayOne(seed, "Mirinae");
                var med = w.Ship.LiveRooms.First(r => r.Type == RoomType.Medbay);
                for (int h = 0; h < 12 && w.Flow.Stats.Flushes == 0; h++)
                {
                    foreach (var l in w.Net.Links.Where(l => l.Kind == NetKind.Water && (l.Room == med || l.Door != null && (l.Door.RoomA == med || l.Door.RoomB == med))))
                    { l.Temp = true; l.Integrity = 0.31f; }
                    w.Soil.RoomSoil(med)[(int)SoilKind.Bio] = 0.6f;
                    Settle(w);
                    Run(w, SimTime.Hours(1));
                }
                var st = w.Flow.Stats;
                var ev = w.History.Events.LastOrDefault(e => e.Text.Contains("거꾸로"));
                Check("역류 — 수압이 떨어진 방의 더러운 물이 급수관으로 들고, 누군가 씻어 낸다",
                    st.Backflows > 0 && st.Flushes > 0,
                    $"{med.Name} 수압 {med.WaterFlow * 100:0}% · 물 깨끗함 {w.Flow.WaterQuality * 100:0}% · {st.Summary()} · 기록: {ev?.Text}");
            }

            // 5) 같은 길: 보조 간선이 주 간선과 같은 방을 지나면 한 번에 둘 다 끊긴다
            {
                // 한빛호의 보조 간선은 선체 속을 곧게 지나며 주 간선이 지나는 방을 가로지르기도 한다
                var w = DayOne(seed, "Hanbit");
                var src = Source(w, NetKind.Power);
                Settle(w);
                var ring = w.Net.Links.Where(l => l.Kind == NetKind.Power && UtilityNet.IsRing(l)).OrderByDescending(l => w.Net.SharedWithMain(l).Count).First();
                var far = w.Ship.Rooms.First(r => r.Id == int.Parse(ring.Key.Split(':')[2].Split('-')[1]));
                var shared = w.Net.SharedWithMain(ring);
                bool bothCut = false;
                if (shared.Count > 0)
                {
                    var room = shared[0];
                    var at = ring.Cells.First(c => w.Ship.RoomAt(c) == room);
                    w.Net.DamageNear(at, 2.5f, 1.5f, "시험 폭발");
                    Settle(w);
                    bothCut = ring.Cut && w.Net.Links.Any(l => l.Kind == NetKind.Power && !UtilityNet.IsRing(l) && l.Cut);
                }
                Check("같은 길 — 보조 간선이 주 간선과 같은 방을 지나면 알아채고(기록), 그 방에서 터지면 둘 다 끊긴다",
                    shared.Count > 0 && w.Flow.Stats.CommonMode > 0 && bothCut,
                    $"보조 간선 {src.Name} → {far.Name} · 겹치는 방 {string.Join("·", shared.Select(r => r.Name))} · 둘 다 끊김 {(bothCut ? "예" : "아니오")} · {far.Name} 전력 {(far.PowerLinked ? "들어옴" : "끊김")}");
            }

            // 6) 문 균압: 문 양쪽 기압이 다르면 균압 밸브로 맞추고 연다
            {
                var w = DayOne(seed, "Mirinae");
                var hall = w.Ship.LiveRooms.Where(r => r.Type == RoomType.Corridor).OrderByDescending(r => r.Cells.Count).First();
                for (int i = 0; i < SimTime.Minutes(40) && w.Flow.Stats.Equalized == 0; i++)
                {
                    if (i % 15 == 0 && hall.Air.Pressure > 85f) { hall.VentOpen = false; float k = 84f / hall.Air.Pressure; hall.Air.O2 *= k; hall.Air.N2 *= k; }
                    // 통합8 문틈 · 덕트로 옆방 공기가 통로로 새어 몇 분이면 차가 12kPa 아래로 준다 — 옆방은 생명 유지 장치가 채우고 있다고 둔다 (차가 남아 있어야 장면이 선다)
                    if (i % 15 == 0) foreach (var n in hall.Doors.Select(d => d.RoomA == hall ? d.RoomB : d.RoomA).Where(r => r != null && r != hall && !r.Detached && r.Air.Pressure < 99f && r.Air.Pressure > 50f).Distinct().ToList()) { float k2 = 100f / n!.Air.Pressure; n.Air.O2 *= k2; n.Air.N2 *= k2; }
                    w.Step();
                }
                var who = w.Crew.FirstOrDefault(c => c.Gait.DoorReading?.StartsWith("균압") == true);
                Check("문 균압 — 문 양쪽 기압이 다르면 손으로 못 열어 균압 밸브로 맞추고 연다", w.Flow.Stats.Equalized > 0,
                    $"{hall.Name} {hall.Air.Pressure:0}kPa · 균압 {w.Flow.Stats.Equalized} · {who?.Name} \"{who?.Gait.DoorReading}\"");
            }

            // 7) 에어락 절차: 점검 → 감압 (전기가 모자라면 손 펌프) · 들어오면 가압 → 우주복을 턴다
            {
                var w = DayOne(seed, "Mirinae");
                var hatch = DroneSystem.Hatch(w)!;
                var inner = Cell.Dirs4.Select(d => hatch.Cell + d).First(x => w.Ship.RoomAt(x) != null && w.Ship.IsWalkable(x));
                var lockRoom = w.Ship.RoomAt(inner)!;
                var c = w.Crew.First(x => x.CanAct && !x.IsChild);
                var toils = new List<Toil> { new GotoToil(inner) };
                toils.AddRange(w.Flow.AirlockOut(c, hatch, inner, false));
                toils.Add(new DoToil((cm, world) => { cm.Soil.SuitDust = 0.7f; return true; })); // 밖에서 분진이 앉았다 (나간 셈)
                toils.AddRange(w.Flow.AirlockIn(hatch, inner));
                c.StartJob(new Job(null, "시험 선외", toils) { InterruptMargin = 5f }, w, null);
                for (int m = 0; m < 30; m++)
                {
                    Run(w, SimTime.Minutes(1));
                    if (Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "2" && m % 3 == 0) Console.WriteLine($"      {m}분 {c.Name} {c.Job?.Label} 칸 {c.Cell} → {inner} · {c.Room?.Name}");
                }
                int checks = w.Flow.Stats.AirlockChecks, decons = w.Flow.Stats.AirlockDecons;
                // 에어락 전기가 끊기면 손 펌프로 느리게
                lockRoom.BreakerOff = true;
                var c2 = w.Crew.First(x => x.CanAct && !x.IsChild && x != c);
                var toils2 = new List<Toil> { new GotoToil(inner) };
                Run(w, SimTime.Minutes(1)); // 다음 시스템 틱에 정전이 반영된다
                toils2.AddRange(w.Flow.AirlockOut(c2, hatch, inner, false));
                c2.StartJob(new Job(null, "시험 선외", toils2) { InterruptMargin = 5f }, w, null);
                Run(w, SimTime.Minutes(30));
                Check("에어락 절차 — 점검 → 감압 · 들어오면 가압 뒤 우주복을 턴다 · 전기가 없으면 손 펌프로",
                    checks > 0 && decons > 0 && w.Flow.Stats.AirlockSlow > 0,
                    $"{c.Name}: 점검 {checks} · 털기 {decons} · {c2.Name}: 손 펌프 {w.Flow.Stats.AirlockSlow} · {w.Flow.Stats.Summary()}");
            }

            // 8) 결정론
            {
                uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
                uint x = H(), y = H();
                Check("결정론 — 전달량이 든 배도 같은 시드 같은 지문", x == y, $"{x:x8} / {y:x8}");
            }
        }
        catch (Exception e) { Console.WriteLine(e); _fails++; }
        Console.WriteLine(_fails == 0 ? "\n✔ 배관·배선 전달량 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
