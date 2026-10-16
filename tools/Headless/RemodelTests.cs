using System;
using System.Linq;
using ShipSim.Core;

// v10.12 공간 개조: 칸막이 철거(방 합치기), 설비 옮기기
public static partial class Program
{
    private static int RunRemodelTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"공간 개조 점검 (v10.12) · 시드 {seed}\n");

        // ── 1) 칸막이를 세웠다 걷으면 방이 원래대로 — 칸·가구·문·연결부·공기·길 ──
        {
            var w = DayOne(seed, "Mirinae");
            var mess = w.Ship.RoomsOf(RoomType.Mess).First();
            int cells0 = mess.Cells.Count, furn0 = mess.Furniture.Count, doors0 = mess.Doors.Count(d => !d.Removed), joints0 = mess.Joints.Count;
            var plan = Remodel.FindSplit(w, mess)!;
            var inner = Remodel.Apply(w, plan)!;
            Run(w, SimTime.Hours(2));
            // 안쪽 칸만 공기를 조금 빼 둔다 (섞이는지 본다)
            inner.Air.O2 = 18f;
            bool can = Remodel2.CanMerge(w, inner);
            var merged = Remodel2.Merge(w, inner);
            float o2 = mess.Air.O2; // 합친 직후: 두 칸의 공기가 부피대로 섞였다
            bool gone = inner.Merged && inner.Detached && inner.Cells.Count == 0 && inner.Furniture.Count == 0;
            bool walls = inner.SplitWall.All(c => w.Ship.WallAt(c) == null && w.Ship.Grid.Kind(c) == TileKind.Floor && w.Ship.RoomAt(c) == mess);
            bool door = inner.SplitDoor!.Removed && w.Ship.DoorAt(inner.SplitDoor.Cell) == null;
            // 사람이 예전 안쪽 칸 자리까지 걸어갈 수 있나
            var far = mess.Cells.Where(w.Ship.IsOpenFloor).OrderByDescending(c => (c.Center - mess.Doors.First(d => !d.Removed).Cell.Center).LengthSquared()).First();
            var c0 = w.Crew.First(c => !c.Dead && c.Room != null);
            bool reach = w.Paths.Find(c0.Cell, far, c0.PathProfile) != null;
            Run(w, SimTime.Hours(10));
            bool nobodyInWall = w.Crew.All(c => w.Ship.Grid.Kind(c.Cell) != TileKind.Wall);
            Check("칸막이 철거 — 방이 원래대로 (칸·가구·문·연결부·공기·길)",
                can && merged == mess && gone && walls && door && mess.Cells.Count == cells0 && mess.Furniture.Count == furn0
                && mess.Doors.Count(d => !d.Removed) == doors0 && mess.Joints.Count == joints0 && o2 < 20.9f && o2 > 18f && reach && nobodyInWall && !mess.Partitioned,
                $"칸 {cells0} → {mess.Cells.Count} · 가구 {furn0} → {mess.Furniture.Count} · 문 {doors0} → {mess.Doors.Count(d => !d.Removed)} · 연결부 {joints0} → {mess.Joints.Count} · 합친 직후 산소 {o2:0.0} · 길 {(reach ? "있음" : "없음")}");
        }

        // ── 2) 개조로: 가른 뒤로 뚫린 적 없이 오래 평화로운 식당 — 사교적인 사람들이 칸막이를 걷자고 한다 ──
        {
            var w = DayOne(seed, "Mirinae");
            var mess = w.Ship.RoomsOf(RoomType.Mess).First();
            var inner = Remodel.Apply(w, Remodel.FindSplit(w, mess)!)!;
            bool proposed = false;
            for (int t = 0; t < SimTime.TicksPerDay * 30 && w.History.Unpartitions == 0; t++)
            {
                w.Step();
                if (t % 1500 == 0) proposed |= w.Board.All.Any(o => o.Upgrade == UpgradeKind.RemovePartition);
            }
            var vote = w.History.Events.LastOrDefault(e => e.Kind == HistoryKind.Decision && e.Text.Contains("칸막이 걷기"));
            Check("칸막이 철거 개조 — 오래 평화로우면 회의로", proposed && (w.History.Unpartitions == 1 || vote != null),
                $"안건 {(proposed ? "올라옴" : "없음")} · 걷음 {w.History.Unpartitions}번 · {SimTime.Day(w.Tick)}일" + (vote != null ? $" · {vote.Text}" : ""));
        }

        // ── 3) 설비 옮기기: 여러 번 뚫린 외벽 방의 설비를 안쪽 방으로 — 옮긴 뒤에도 돈다 ──
        {
            var w = DayOne(seed, "Hanbit");
            // 옮길 설비가 있는 외벽 방을 고른다 (뚫린 기록을 쌓아 둔다)
            var room = w.Ship.Rooms.FirstOrDefault(r => !r.Detached && !Remodel2.Interior(w, r)
                && r.Furniture.Any(f => Remodel2.Movable(f.Type) && f.Machine != null && Remodel2.FindSpot(w, f) != null));
            if (room == null) Check("설비 옮기기", false, "옮길 설비가 있는 외벽 방이 없다");
            else
            {
                w.History.BreachesByRoom[room.Id] = 3;
                var f = room.Furniture.Where(x => Remodel2.Movable(x.Type) && x.Machine != null).OrderByDescending(x => x.Machine!.Spec.Critical).ThenBy(x => x.Id).First();
                float wear0 = f.Machine!.Wear;
                int marks0 = f.Machine.Marks.Count;
                for (int t = 0; t < SimTime.TicksPerDay * 12 && w.History.Relocations == 0; t++) w.Step();
                Run(w, SimTime.Hours(3));
                bool moved = w.History.Relocations >= 1;
                var moved1 = w.Ship.Furniture.FirstOrDefault(x => x.Machine?.Marks.Any(m => m.Text.Contains("옮겼다")) == true);
                bool inside = moved1 != null && Remodel2.Interior(w, moved1.Room) && moved1.UseSpots.Count > 0 && moved1.Cells.All(c => w.Ship.FurnitureAt(c) == moved1);
                bool works = moved1?.Machine is Machine mm && mm.Powered && mm.Efficiency > 0.3f;
                var vote = w.History.Events.LastOrDefault(e => e.Kind == HistoryKind.Decision && e.Text.Contains("옮기기"));
                Check("설비 옮기기 — 뚫렸던 외벽 방에서 안쪽 방으로, 옮긴 뒤에도 돈다", moved && inside && works,
                    $"{room.Name}의 {(moved1 ?? f).Label} → {(moved1?.Room.Name ?? "못 옮김")} · 사용 자리 {moved1?.UseSpots.Count ?? 0} · 효율 {(moved1?.Machine?.Efficiency ?? 0f) * 100:0}%"
                    + (vote != null ? $" · {vote.Text}" : ""));
            }
        }

        // ── 4) 같은 시드면 같은 역사 (칸막이 철거·설비 옮기기가 들어간 배) ──
        {
            uint Hash()
            {
                var w = DayOne(seed, "Mirinae");
                var mess = w.Ship.RoomsOf(RoomType.Mess).First();
                var inner = Remodel.Apply(w, Remodel.FindSplit(w, mess)!)!;
                Run(w, SimTime.Hours(6));
                Remodel2.Merge(w, inner);
                Run(w, SimTime.Hours(20));
                return SaveGame.StateHash(w);
            }
            uint a = Hash(), b = Hash();
            Check("칸막이 철거가 들어간 배의 결정론", a == b, $"지문 {(a == b ? "같음" : "다름")}");
        }

        Console.WriteLine(_fails == 0 ? "\n✔ 공간 개조 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
