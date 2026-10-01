using System;
using System.Linq;
using ShipSim.Core;

// v14.7 오염 · 씻기 · 빨래 · 우주복 털기 · 손으로 옮는 것
public static partial class Program
{
    private static int RunSoilTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"오염·위생 점검 (v14.7) · 시드 {seed}\n");
        try
        {
            // 1) 정비하면 손에 기름이 묻고, 씻으면 준다
            {
                var w = DayOne(seed, "Mirinae");
                var tech = w.Crew.OrderByDescending(c => c.RawSkill(Skill.Mechanics)).First();
                var pump = w.Ship.Machines.First(m => m.Body.Type == FurnitureType.CoolantPump);
                w.Machines.Break(pump, FaultKind.BearingWear);
                float before = tech.Soil.Hands[(int)SoilKind.Oil], peak = 0f;
                for (int m = 0; m < 240; m++) { Run(w, SimTime.Minutes(1)); peak = MathF.Max(peak, w.Crew.Max(c => c.Soil.Hands[(int)SoilKind.Oil])); }
                var st = w.Soil.Stats;
                Check("기름 — 정비하면 손과 옷에 묻고, 일이 끝나면 씻고 갈아입는다", peak > 0.3f && st.HandWashes > 0,
                    $"손의 기름 최고 {peak * 100:0}% · {st.Summary()}");
            }

            // 2) 씻지 않은 손으로 조리하면 식사에 균이 든다 (씻으면 덜) — 누가 만들었는지 남는다
            {
                int[] tainted = new int[2];
                for (int k = 0; k < 2; k++)
                {
                    var w = DayOne(seed, "Mirinae");
                    for (int h = 0; h < 48; h++)
                    {
                        foreach (var c in w.Crew) { c.Soil.Hands[(int)SoilKind.Bio] = 0.8f; if (k == 1) c.Soil.WashedAt = w.Tick; }
                        if (k == 1) foreach (var c in w.Crew) w.Soil.WashHands(c, "시험");
                        Run(w, SimTime.Hours(1));
                    }
                    tainted[k] = w.Soil.Stats.TaintedMeals;
                }
                Check("손 위생 — 균이 묻은 손으로 조리하면 식사에 균이 들고, 씻으면 덜하다", tainted[0] > tainted[1],
                    $"안 씻음 {tainted[0]}끼 ↔ 씻음 {tainted[1]}끼");
            }

            // 3) 선외 작업 뒤 우주복을 털지 않으면 분진이 옷과 방으로 번진다
            {
                var w = DayOne(seed, "Mirinae");
                var c = w.Crew.First(x => !x.IsChild && x.CanAct);
                c.Soil.SuitDust = 0.8f;
                var room = c.Room!;
                float dust0 = w.Soil.RoomSoil(room)[(int)SoilKind.Dust];
                Run(w, SimTime.Minutes(30));
                float clothes = c.Soil.Clothes[(int)SoilKind.Dust];
                float spread = w.Ship.LiveRooms.Max(r => w.Soil.RoomSoil(r)[(int)SoilKind.Dust]);
                Check("우주복 분진 — 털지 않고 들어오면 옷과 방으로 번진다 (털면 멈춘다)", clothes > 0.01f && spread > dust0 && w.Soil.Stats.SkippedDecons > 0,
                    $"옷의 분진 {clothes * 100:0}% · 가장 더러운 방의 분진 {spread * 100:0}% · {w.Soil.Stats.Summary()}");
            }

            // 4) 물이 모자라면 손만 씻고 빨래를 미룬다
            {
                var w = DayOne(seed, "Mirinae");
                w.Policies.Set("water", 2, "시험");
                foreach (var c in w.Crew) { c.Soil.Clothes[(int)SoilKind.Oil] = 0.8f; c.Soil.Hands[(int)SoilKind.Oil] = 0.8f; }
                Run(w, SimTime.Hours(24));
                var st = w.Soil.Stats;
                Check("물 부족 — 손만 씻고 빨래를 미룬다", st.PartialWashes > 0 && st.LaundryRuns == 0 || st.LaundryDeferred > 0,
                    st.Summary());
            }

            // 4b) 물이 넉넉하면 갈아입은 옷이 모여 빨래를 돌린다
            {
                var w = DayOne(seed, "Mirinae");
                foreach (var c in w.Crew) c.Soil.Clothes[(int)SoilKind.Oil] = 0.8f;
                Run(w, SimTime.Hours(36));
                var st = w.Soil.Stats;
                Check("빨래 — 갈아입은 옷이 바구니에 모이고 세탁실에서 돌린다", st.Changes >= 4 && st.LaundryRuns > 0, st.Summary());
            }

            // 5) 결정론
            {
                uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
                uint x = H(), y = H();
                Check("결정론 — 오염이 든 배도 같은 시드 같은 지문", x == y, $"{x:x8} / {y:x8}");
            }
        }
        catch (Exception e) { Console.WriteLine(e); _fails++; }
        Console.WriteLine(_fails == 0 ? "\n✔ 오염·위생 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
