using System;
using System.Linq;
using ShipSim.Core;

// v14.9 배의 문화: 관행이 생기고 · 신입에게 전해지고 · 이유가 잊히고 · 기념일 · 같은 설계의 배가 달라진다
public static partial class Program
{
    private static int RunCultureTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"배의 문화 점검 (v14.9) · 시드 {seed}\n");
        try
        {
            static void Fire(World w)
            {
                var room = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Storage or RoomType.Lounge or RoomType.Mess).OrderBy(r => r.Id).First();
                var cell = room.Cells.First(c => w.Ship.IsWalkable(c));
                w.Fire.Ignite(cell, 0.9f);
            }

            // 1) 큰불을 겪은 배: 식사 전에 소화기 자리를 본다
            {
                var w = DayOne(seed, "Mirinae");
                Fire(w);
                Run(w, SimTime.Hours(30));
                var cu = w.Culture.Of(CustomKind.FireCheck);
                Check("관행이 생긴다 — 큰불을 겪은 배는 밥 먹기 전에 소화기 자리를 본다", cu != null && w.Culture.Stats.ExtChecks > 0,
                    cu == null ? "관행 없음" : $"{w.Culture.Describe(cu)} · 소화기 확인 {w.Culture.Stats.ExtChecks}번");
            }

            // 2) 신입: 처음엔 모르다가 설명을 듣고 받아들인다 (보고 따라 하기도 한다)
            {
                var w = DayOne(seed, "Mirinae");
                Fire(w);
                Run(w, SimTime.Hours(4));
                var cu = w.Culture.Of(CustomKind.FireCheck)!;
                var spot = w.Ship.LiveRooms.First(r => r.Type is RoomType.Mess or RoomType.Lounge).Cells.First(c => w.Ship.IsWalkable(c));
                var n1 = w.AddSurvivor(spot);
                bool before = cu.Followers.Contains(n1.Id);
                for (int h = 0; h < 72 && !cu.Followers.Contains(n1.Id); h++) Run(w, SimTime.Hours(1));
                Check("신입 — 처음엔 모르다가 같이 지내며 설명을 듣거나 보고 따라 한다", !before && cu.Followers.Contains(n1.Id),
                    $"{n1.Name}: 따름 {(cu.Followers.Contains(n1.Id) ? "예" : "아니오")} · 이유를 앎 {(cu.Knowers.Contains(n1.Id) ? "예" : "아니오")} · {w.Culture.Stats.Summary()} · 일기: {n1.Diary.Select(d => d.text).LastOrDefault()}");
            }

            // 3) 이유를 아는 사람이 다 떠나고 기록도 없으면, 이유는 잊히고 행동만 남는다
            {
                var w = DayOne(seed, "Mirinae");
                Fire(w);
                // 불이 알려져 관행이 생길 때까지 (감지기·본 사람) — 그 뒤에 기록이 사라진다
                for (int h = 0; h < 8 && w.Culture.Of(CustomKind.FireCheck) == null; h++) Run(w, SimTime.Hours(1));
                var cu = w.Culture.Of(CustomKind.FireCheck)!;
                w.Automation.Remove(ComputerModule.Archive); w.Automation.V15NoAuto = true; // v15.9 기록 보관 모듈도 없는 배
                foreach (var f in w.Ship.FurnitureOf(FurnitureType.MainComputer)) w.Machines.Break(f.Machine!, FaultKind.Wrecked); // 기록이 없다
                Run(w, SimTime.Minutes(30));
                var spot = w.Ship.LiveRooms.First(r => r.Type is RoomType.Mess or RoomType.Lounge).Cells.First(c => w.Ship.IsWalkable(c));
                var olds = w.Crew.Where(c => !c.Dead).ToList();
                var n = w.AddSurvivor(spot);
                // 신입이 이유를 모른 채 따라 할 때까지 (다들 바빠 설명할 틈이 없을 때) — 아니면 이유까지 들을 때까지
                for (int h = 0; h < 96 && !cu.Followers.Contains(n.Id); h++) Run(w, SimTime.Hours(1));
                bool knew = cu.Knowers.Contains(n.Id);
                // 원래 사람들이 기항지에서 내렸다 (시험: 배에서 빠진 셈)
                foreach (var o in olds) w.Culture.OnGone(o);
                w.Automation.Remove(ComputerModule.Archive);
                Run(w, SimTime.Minutes(30));
                bool lostOk = knew ? !cu.ReasonLost : cu.ReasonLost && w.Culture.Stats.Forgotten > 0;
                Check("이유가 잊힌다 — 아는 사람이 떠나고 기록도 없으면 행동만 남는다 (이유를 들은 신입이 있으면 남는다)",
                    cu.Followers.Contains(n.Id) && lostOk && !cu.Written,
                    $"{n.Name}: 이유를 앎 {(knew ? "예" : "아니오")} · 이유 잊힘 {(cu.ReasonLost ? "예" : "아니오")} · {w.Culture.Describe(cu)}");
            }

            // 4) 추모일: 그 사람의 자리에 물건을 놓는다
            {
                var w = DayOne(seed, "Mirinae");
                var dead = w.Crew.Where(c => !c.IsChild).OrderBy(c => c.Id).Skip(2).First();
                w.CrewCanDie = true;
                dead.Vitals.Health = 0f;
                Run(w, SimTime.Hours(2));
                w.CrewCanDie = false;
                var cu = w.Culture.Of(CustomKind.Memorial);
                if (cu != null) cu.NextDay = w.Tick + SimTime.Hours(1); // 이레 뒤를 당겨서
                Run(w, SimTime.Hours(20));
                Check("추모일 — 떠난 사람의 자리에 물건을 놓는다", dead.Dead && cu != null && w.Culture.Stats.MemorialItems > 0,
                    cu == null ? $"죽음 {dead.Dead} · 관행 없음" : $"{w.Culture.Describe(cu)} · 놓은 물건 {w.Culture.Stats.MemorialItems} · {w.Log.Entries.Where(e => e.Text.Contains("자리에")).Select(e => e.Text).LastOrDefault()}");
            }

            // 5) 같은 설계의 배가 서로 다른 공동체가 된다
            {
                var a = DayOne(seed, "Mirinae");
                var b = DayOne(seed, "Mirinae");
                Fire(a);
                for (int h = 0; h < 10; h++) { b.Water.Level = 0f; Run(b, SimTime.Hours(1)); Run(a, SimTime.Hours(1)); }
                b.Water.Level = b.Water.Capacity * 0.8f; // 기항지에서 물을 다시 채웠다 — 그래도 아낀다
                foreach (var c in b.Crew) c.Soil.Hands[(int)SoilKind.Oil] = 0.6f;
                Run(a, SimTime.Hours(14)); Run(b, SimTime.Hours(14));
                string A = string.Join(", ", a.Culture.Customs.Select(x => CultureSystem.Name(x.Kind)));
                string B = string.Join(", ", b.Culture.Customs.Select(x => CultureSystem.Name(x.Kind)));
                Check("갈라짐 — 같은 배가 겪은 일에 따라 다른 관행을 갖는다",
                    a.Culture.Of(CustomKind.FireCheck) != null && a.Culture.Of(CustomKind.WaterThrift) == null && b.Culture.Of(CustomKind.WaterThrift) != null && b.Culture.Of(CustomKind.FireCheck) == null
                    && b.Culture.Stats.ThriftWashes > 0,
                    $"불을 겪은 배: {A} ↔ 물이 바닥난 배: {B} (아껴 씻기 {b.Culture.Stats.ThriftWashes})");
            }

            // 6) 정전 속에서 배를 살린 정비사의 방식: 그 사람에게서 시작해 배우는 사람에게 건너간다
            {
                var w = DayOne(seed, "Mirinae");
                w.Net.Stats.Blackouts = 1; w.Net.Stats.Repairs = 2;
                Run(w, SimTime.Minutes(30));
                var cu = w.Culture.Of(CustomKind.MaintainerWay);
                int first = cu?.Followers.Count ?? 0;
                Run(w, SimTime.TicksPerDay * 3);
                Check("점검 방식 — 배를 살린 사람에게서 시작한다 (배우는 사람에게 건너간다)", cu != null && first == 1,
                    cu == null ? "관행 없음" : $"{cu.Founder} · 처음 {first}명 → 사흘 뒤 {cu.Followers.Count}명 (제자에게 {w.Culture.Stats.Inherited})");
            }

            // 7) 결정론
            {
                uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Fire(w); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
                uint x = H(), y = H();
                Check("결정론 — 관행이 든 배도 같은 시드 같은 지문", x == y, $"{x:x8} / {y:x8}");
            }
        }
        catch (Exception e) { Console.WriteLine(e); _fails++; }
        Console.WriteLine(_fails == 0 ? "\n✔ 배의 문화 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
