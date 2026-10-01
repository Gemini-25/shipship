using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v15 설비 70: 새 모듈 34 — 겪은 일 뒤에 개조 후보로 오르고, 달면 효과가 난다
public static partial class Program
{
    private static int RunModuleTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"설비 70 점검 (v15) · 시드 {seed}\n");
        try
        {
            int types = Enum.GetValues<FurnitureType>().Length;
            bool specs = ModulesV15.Rows.All(r => MachineSpecs.For(r.Type) != null && Modules.Of(r.Type) != null && FurnitureTypes.Name(r.Type) != r.Type.ToString());
            Check("목록 — 설비 70 · 새 모듈 34는 사양·개조표·이름이 있다", types == 70 && ModulesV15.Rows.Length == 34 && specs, $"설비 {types} · 새 모듈 {ModulesV15.Rows.Length}");

            // 1) 달아 보면 효과가 난다
            {
                var w = DayOne(seed, "Mirinae");
                var who = w.Crew.First(c => !c.IsChild);
                int put = 0;
                var missingRoom = new List<string>();
                // 좁은 방은 모듈끼리 자리를 다툰다 — 확인할 것부터 단다
                var first = new[] { FurnitureType.SurgeProtector, FurnitureType.Oven, FurnitureType.VibrationMonitor, FurnitureType.EmergencyLight, FurnitureType.FireBlanket };
                foreach (var r in ModulesV15.Rows.OrderBy(r => Array.IndexOf(first, r.Type) is int i && i >= 0 ? i : 99))
                {
                    var room = w.Ship.LiveRooms.Where(x => x.Type == r.Room).OrderBy(x => x.Id).FirstOrDefault();
                    if (room == null) { missingRoom.Add(r.Name); continue; }
                    if (Modules.Spot(w, room) is Cell at && Modules.Install(w, room, r.Type, who, at)) put++;
                    else missingRoom.Add(r.Name + "(자리 없음)");
                }
                Run(w, SimTime.Minutes(2));
                var galley = w.Ship.LiveRooms.FirstOrDefault(r => r.Type == RoomType.Galley);
                var engine = w.Ship.LiveRooms.FirstOrDefault(r => r.Type == RoomType.Engine);
                var hall = w.Ship.LiveRooms.FirstOrDefault(r => r.Type == RoomType.Corridor && ModulesV15.Has(r, FurnitureType.EmergencyLight));
                var quarters = w.Ship.LiveRooms.FirstOrDefault(r => r.Type == RoomType.Quarters);
                float speed = ModulesV15.SpeedMul(galley, Skill.Cooking), omen = ModulesV15.OmenMul(engine, OmenKind.Vibration);
                bool lit = false;
                if (hall != null) { hall.LightsOut = true; lit = !hall.Dark; }
                string surge = w.Hazards.FireStory(nameof(HazardKind.PowerSurge), null) ?? "";
                bool guarded = w.History.Events.Any(e => e.Text.Contains("서지 보호기"));
                float fireAt = 1f;
                if (galley != null && ModulesV15.Has(galley, FurnitureType.FireBlanket))
                {
                    var cell = galley.Cells.First(c => w.Ship.IsOpenFloor(c));
                    w.Fire.Ignite(cell, 1f);
                    fireAt = w.Fire.At(cell);
                    w.Fire.Suppress(cell, 3f, 5f);
                }
                Check("효과 — 오븐은 조리를, 진동 감시기는 진동 전조를, 비상등은 어둠을, 서지 보호기는 서지를, 방화포는 불을",
                    put >= 24 && speed > 1.1f && omen > 1.3f && lit && guarded && fireAt <= 0.55f,
                    $"단 모듈 {put}/34 (방이 없음: {string.Join(",", missingRoom)}) · 조리 ×{speed:0.00} · 진동 전조 ×{omen:0.00} · 비상등 {(lit ? "밝음" : "어둠")} · 서지 {(guarded ? "보호기가 받음" : "그대로")} · 방화포 불 세기 {fireAt:0.00} · 침실 잠 +{ModulesV15.SleepAdd(quarters):0.00}");
            }

            // 2) 겪은 일 뒤에 개조 후보로 오른다
            {
                var w = DayOne(seed, "Mirinae");
                var before = Modules.Candidates(w).Select(p => (FurnitureType)p.Circuit).Where(ModulesV15.Is).ToHashSet();
                w.History.Fires = 2;
                w.Soil.Stats.TaintedMeals = 2;
                w.Hazards.Count[(int)HazardKind.PowerSurge] = 1;
                var after = Modules.Candidates(w).Select(p => (FurnitureType)p.Circuit).Where(ModulesV15.Is).ToHashSet();
                bool ok = after.Contains(FurnitureType.FireBlanket) && after.Contains(FurnitureType.DishWasher) && after.Contains(FurnitureType.SurgeProtector)
                          && !before.Contains(FurnitureType.FireBlanket);
                Check("개조 후보 — 불 · 균 든 식사 · 서지를 겪으면 방화포 · 식기 세척기 · 서지 보호기가 오른다", ok,
                    $"전 {before.Count}가지 → 뒤 {after.Count}가지 ({string.Join(", ", after.Select(t => FurnitureTypes.Name(t)))})");
            }

            // 3) 결정론
            {
                uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
                uint x = H(), y = H();
                Check("결정론 — 새 설비가 든 배도 같은 시드 같은 지문", x == y, $"{x:x8} / {y:x8}");
            }
        }
        catch (Exception e) { Console.WriteLine(e); _fails++; }
        Console.WriteLine(_fails == 0 ? "\n✔ 설비 70 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
