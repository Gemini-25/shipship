using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>새 그림 (매끈한 반입체 — docs/GRAPHICS.md 시안 3): 그리기는 게임 쪽 그래픽 카드가 하지만, 무엇을 어디에 그릴지는 모두 세계에서 읽은 값이다.
/// 그 값(VFrame)을 본다 — 사람 자리 · 문 열림 · 방 조명 · 장치 상태 · 불. 화면만의 가짜가 없는지.</summary>
public static partial class Program
{
    private static void VectorRules()
    {
        if (!Do("새 그림")) return;
        Console.WriteLine("\n[새 그림 — 장면 값은 세계에서]");

        // ── 관전 화면이 새 그림을 켜면 도트 그림 대신 장면 값이 실린다: 사람 · 로봇 모두 · 제 칸 곁에 · 도면(벽 · 선체 · 설비 · 문)은 배 그대로 ──
        {
            var show = LiveShow.Crewed("Didim", 18);
            show.Vector = true;
            show.Advance(0.5);
            var fr = show.Render();
            var v = fr.V;
            var s = show.Sim; var L = s.Layout; var mind = s.Get<Mind>();
            bool people = v != null && v.People.Length == mind.Agents.Count
                && v.People.All(p => { var a = mind.Agents[p.Id]; return Math.Abs(p.X - (L.X(a.C.Cell) + 0.5)) <= 1.01 && Math.Abs(p.Y - (L.Y(a.C.Cell) + 0.5)) <= 1.01 && p.Person == a.IsPerson && (!a.IsPerson || p.Name == mind.NameOf(a.Id)); });
            bool plan = v != null && v.S.W == L.W && v.S.Furn.Length == L.Furniture.Count(f => f.Cells.Length > 0) && v.S.Doors.Length == L.Doors.Count
                && Enumerable.Range(0, L.N).All(c => (v.S.Kind[c] == VStatic.Space) == (L.Kind[c] == CellKind.Outside) && (v.S.Kind[c] == VStatic.Door) == (L.Kind[c] == CellKind.Door))
                && v.S.Kind.Count(k => k == VStatic.Hull) > 20 && v.S.Kind.Count(k => k == VStatic.Wall) > 20;
            show.Vector = false;
            var old = show.Render();
            Check("새 그림: 켜면 도트 그림 대신 장면 값이 실린다 — 사람 · 로봇 모두 제 칸 곁에 제 이름으로 · 도면(바깥 · 문 · 설비)은 배 그대로 · 끄면 다시 도트 그림",
                people && plan && fr.Img.W <= Painter.T * 2 && old.V == null && old.Img.W > Painter.T * 4,
                $"사람 {v?.People.Length}/{mind.Agents.Count} 맞음 {people} · 도면 {plan} · 새 그림일 때 그림판 {fr.Img.W}×{fr.Img.H} · 옛 그림 {old.Img.W}×{old.Img.H}");
        }

        // ── 빛 · 문 · 장치 · 불은 세계의 값: 전기가 끊긴 방은 어둡고 · 문은 열린 만큼 · 망가진 장치는 망가졌다고 · 불난 칸에 불 ──
        {
            var s = ShipWorld.Crewed("Didim", 5, 2, 1);
            s.Run(30);
            var L = s.Layout; var net = s.Get<Network>(); var f = s.Get<Field>(); var chem = s.Get<Chemistry>();
            var pa = new Painter(L);
            VFrame Snap() => pa.VectorFrame(s, 0, 0, id => { int c = s.Get<Mind>().Agents[id].C.Cell; return (L.X(c) + 0.5, L.Y(c) + 0.5); });
            int Lum(VFrame v, int cell) => v.Light[cell * 3] + v.Light[cell * 3 + 1] + v.Light[cell * 3 + 2];
            // 일하는 방 둘 (밤에도 조명을 낮추지 않는 방 — 배는 한밤에 떠난다)
            var mess = L.Rooms.First(r => r.Type == "Workshop"); var quarters = L.Rooms.First(r => r.Type == "LifeSupport");
            var v0 = Snap();
            int messLit = Lum(v0, mess.Cells[mess.Cells.Length / 2]), space = v0.Light[0];
            // 정비실의 급전선 차단기를 내린다 (방 조명은 그 방 급전선에서 — 꺼지면 비상등만 붉게)
            int feeder = net.RoomFeeder[mess.Id];
            net.Breaker[feeder] = 0; net.FaultCell[feeder] = mess.Cells[0];   // 합선이 남아 있어 주컴퓨터가 다시 올리지 못한다
            s.Run(2);
            var v1 = Snap();
            if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") Console.WriteLine($"    정비실 방 {mess.Id} · 급전선 {net.RoomFeed(mess.Id)} · 차단기 {net.Breaker[net.RoomFeeder[mess.Id]]} · 등 상태 {net.LampState(mess.Id)} · 럭스 {s.Get<Light>().Lamp[mess.Id]:0} · 가운데 칸 방 {L.Room[mess.Cells[mess.Cells.Length / 2]]}");
            int messDark = Lum(v1, mess.Cells[mess.Cells.Length / 2]), otherLit = Lum(v1, quarters.Cells[quarters.Cells.Length / 2]);
            // 문 하나를 열고 · 장치 하나를 망가뜨리고 · 불을 붙인다
            int door = L.Doors.First(d => !d.External).Id; float door0 = v0.Door[door];
            s.Get<Structure>().RequestDoor(door, door0 > 0.5f ? 0f : 1f);
            int fi = Enumerable.Range(0, v1.S.Furn.Length).First(i => v1.Furn[i] == 1);
            int dev = net.Devices.First(d => d.Furniture == v1.S.Furn[fi].Id).Id;
            net.Broken[dev * 4 + (int)Part.Electric] = 1;
            int fireCell = quarters.Cells[0];
            chem.Ignite(fireCell, 3e6, s.Causes.Add(CauseKind.Setup, "시험 불씨", s.Tick));
            s.Run(12);
            var v2 = Snap();
            bool fire = v2.Fires.Any(x => x.Cell == fireCell || L.Room[x.Cell] == quarters.Id);
            Check("새 그림의 빛 · 문 · 장치 · 불은 세계의 값: 차단기가 내려간 정비실은 어둡고 (다른 방은 그대로) · 여닫은 문은 그만큼 · 망가뜨린 장치는 망가졌다고 · 불난 방에 불 · 배 밖은 곱하지 않는다",
                messLit > 300 && messDark < messLit * 0.6 && otherLit > 300 && Math.Abs(v2.Door[door] - door0) > 0.5f && v2.Door[door] == f.DoorOpen[door] && v2.Furn[fi] == 3 && fire && space == 255,
                $"정비실 빛 {messLit} → 차단기 내리고 {messDark} · 생명유지실 {otherLit} · 문 {door0:0.0} → {v2.Door[door]:0.0} · 장치 상태 1 → {v2.Furn[fi]} · 불 {v2.Fires.Length}칸 · 배 밖 {space}");
        }
    }
}
