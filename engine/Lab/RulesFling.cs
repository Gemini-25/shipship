using ShipEngine;

namespace Lab;

/// <summary>④ 구조 — 날아가는 물건: 감압 돌풍에 밀리고 · 닫히는 문짝에 물려 문이 안 닫히고 · 구멍 밖 우주로 (설계서 '파편' 사슬). docs/DEPTH.md · PHYSICS.md</summary>
public static partial class Program
{
    /// <summary>왼쪽 화물칸(외판 x=0) · 문 D · 오른쪽 통로.</summary>
    private const string FlingRooms = @"
###############
#.....#.......#
#.....D.......#
#.....#.......#
###############";

    private static void FlingRules()
    {
        if (!Do("날아가는 물건")) return;
        Console.WriteLine("\n[④ 날아가는 물건]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var lay = Worlds.Ascii(FlingRooms, Worlds.Seats);
        int At(int x, int y) => Worlds.At(lay, x, y);

        // ── 화물칸 외판에 1m² 구멍: 통로 문 곁 식량 상자(13kg)가 돌풍에 밀려 열린 문틀에 끼어 닫으라는 명령에도 문이 안 닫히고 (상자가 없으면 닫힌다) ·
        //    구멍 곁 수리 꾸러미는 구멍 밖 우주로 날아가 장부에 '우주로' (장부가 맞는다) ──
        (bool jam, float open, string story, int crateCell, bool kitGone, double kitBook, double kitOther, long spaced, float target) Run(bool crate)
        {
            var s = Worlds.Full(lay, 1);
            var m = s.Get<Mind>(); var st = s.Get<Structure>(); var f = s.Get<Field>(); var w = s.Get<Work>();
            int door = lay.DoorAt[At(6, 2)];
            byte box = (byte)m.ItemIndex("식량 상자"), kit = (byte)m.ItemIndex("수리 꾸러미");
            if (crate) Worlds.PutItem(s, box, At(7, 2));
            Worlds.PutItem(s, kit, At(1, 2));
            st.RequestDoor(door, 1); // 평소처럼 열어 둔 문
            s.Run(10);
            int cause = s.Causes.Add(CauseKind.Setup, "화물칸 외판에 파편이 박혔다", s.Tick);
            st.DamageCause[At(0, 2)] = cause; f.SetHole(At(0, 2), 1.0f);
            for (int k = 0; k < Time.Minutes(1); k++)
            {
                if (k == 1) st.RequestDoor(door, 0); // 한 틱 뒤 문을 닫으려 한다 (주컴퓨터의 봉쇄처럼)
                s.Step();
                if (Debug && k < 4) Console.WriteLine($"    {k}틱 물건 {string.Join(",", Worlds.Items(s).Select(x => $"{m.ItemNames[x.Item]}@{x.Cell}"))} · 문 곁 돌풍 {f.WindPa[At(7, 2)]:0}Pa · 문 {f.DoorOpen[door]:0.0} 끼임 {st.DoorJam[door]}");
            }
            int crateCell = Worlds.Items(s).FirstOrDefault(x => x.Item == box, new Worlds.FloorItem(0, 0, -1, 0, 0)).Cell;
            return (st.DoorJam[door] != 0, f.DoorOpen[door], s.Causes.Story(st.DamageCause[At(6, 2)]), crateCell, !Worlds.Items(s).Any(x => x.Item == kit),
                s.Book.OfItem(kit, Flow.Spaced), w.Other(s, Book.Bulk + kit), m.Spaced, st.DoorTarget[door]);
        }
        var with = Run(true); var without = Run(false);
        Check("파편 사슬: 화물칸 외판에 1m² 구멍 — 통로 문 곁 식량 상자(13kg)가 돌풍에 밀려 문 칸에 들고 · 닫히는 문짝에 물려 문을 끼운다 (닫으라는 목표는 그대로 · 상자가 없으면 닫힌다) · 원인 사슬은 파편까지",
            with.jam && with.open >= 0.5f && with.target == 0 && with.crateCell == At(6, 2) && with.story.Contains("파편") && with.story.Contains("식량 상자") && with.story.Contains("문짝에 물려") && !without.jam && without.open < 0.1f,
            $"상자 있음: 끼임 {with.jam} · 문 {with.open:0.0} · 목표 {with.target:0.0} · 상자 {(with.crateCell == At(6, 2) ? "문틀" : with.crateCell.ToString())} · \"{with.story}\" / 상자 없음: 문 {without.open:0.0}");
        Check("구멍 곁 수리 꾸러미(4kg)는 구멍 밖 우주로 날아가 장부에 '우주로' — 장부가 맞는다",
            with.kitGone && with.spaced >= 1 && Math.Abs(with.kitBook + 1) < 1e-9 && Math.Abs(with.kitOther) < 1e-9,
            $"꾸러미 사라짐 {with.kitGone} · 장부 '우주로' {with.kitBook} · 장부 어긋남 {with.kitOther:0e0}");
    }
}
