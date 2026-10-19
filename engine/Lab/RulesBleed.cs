using ShipEngine;

namespace Lab;

/// <summary>④ 비워지지 않는 에어락 — 바깥문 밸브는 방이 비워질 때만 (새터호 시드 18: 배와 공기가 이어진 격납고를 로봇이 비우려다 밸브가 열린 채 13시간 · 40명이 숨졌다).</summary>
public static partial class Program
{
    private static void BleedRules()
    {
        if (!Do("에어락 비우기")) return;
        Console.WriteLine("\n[④ 에어락 비우기]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";

        // 화살호 에어락: 바깥문을 열어 달라고 한다 — 막힌 에어락 / 안쪽 벽에 100cm² 구멍이 난 에어락 (배 공기가 계속 들어온다)
        (bool opened, long aborts, byte valve, float target, bool locked, double lost, string why) Run(bool holed)
        {
            var s = ShipWorld.Create("Hwasal", 11);
            var f = s.Get<Field>(); var st = s.Get<Structure>(); var L2 = s.Layout;
            var ext = L2.Doors.First(d => d.External);
            int hi = st.OutsideBleedRoomCell(ext.Id), room = L2.Room[hi];
            if (holed)
            {
                // 에어락과 이웃 방 사이 벽 한 칸에 구멍
                Span<int> nb = stackalloc int[4], nb2 = stackalloc int[4];
                int wall = -1;
                foreach (int c in L2.Rooms[room].Cells)
                {
                    L2.Neighbors(c, nb);
                    foreach (int w in nb)
                    {
                        if (w < 0 || L2.Kind[w] != CellKind.Wall || L2.Hull[w]) continue;
                        L2.Neighbors(w, nb2);
                        foreach (int o in nb2) if (o >= 0 && L2.Room[o] >= 0 && L2.Room[o] != room) { wall = w; break; }
                        if (wall >= 0) break;
                    }
                    if (wall >= 0) break;
                }
                f.SetHole(wall, 0.01f);
            }
            s.Run(5);
            double mol0 = L2.Rooms.Where(r => r.Id != room).Sum(r => r.Cells.Sum(c => f.Moles(c)));
            bool opened = false;
            for (int k = 0; k < Time.Minutes(10); k++)
            {
                st.RequestDoor(ext.Id, 1);                               // 누군가 계속 열어 달라고 한다
                s.Step();
                if (f.DoorOpen[ext.Id] > 0) { opened = true; break; }
                if (Debug && k % 125 == 0) Console.WriteLine($"    {(holed ? "구멍" : "막힘")} {k / 25}분 · 에어락 {f.Pressure(hi):0.0}kPa · 밸브 {f.DoorBleed[ext.Id]} · 멈춤 {st.BleedAborts}");
            }
            double mol1 = L2.Rooms.Where(r => r.Id != room).Sum(r => r.Cells.Sum(c => f.Moles(c)));
            var dec = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Decision && x.Other == ext.Id);
            return (opened, st.BleedAborts, f.DoorBleed[ext.Id], st.DoorTarget[ext.Id], st.BleedLock[ext.Id] > s.Tick, mol0 - mol1,
                dec.Kind == FactKind.Decision ? s.Causes.Text(dec.Cause) : "");
        }
        var sealedT = Task.Run(() => Run(false)); var holed = Run(true); var sealedA = sealedT.Result;
        Check("에어락 비우기: 막힌 에어락은 밸브로 비운 뒤 바깥문이 열린다 · 안쪽 벽이 뚫려 배 공기가 계속 들어오는 에어락은 몇 분 만에 밸브를 잠그고 한동안 비우지 않는다 (배 공기를 우주로 버리지 않는다)",
            sealedA.opened && sealedA.aborts == 0 && !holed.opened && holed.aborts >= 1 && holed.valve == 0 && holed.locked && holed.why.Contains("비워지지 않아"),
            $"막힌 에어락: 열림 {sealedA.opened} · 멈춤 {sealedA.aborts} / 뚫린 에어락: 열림 {holed.opened} · 멈춤 {holed.aborts} · 밸브 {holed.valve} · 잠금 {holed.locked} · 배에서 빠진 공기 10분 {holed.lost:0}몰 · \"{holed.why}\"");

        // ── 예비 공기: 못 보는 방(센서 선이 끊긴 방)의 외판 금으로 푸는 만큼 샌다 — 배 기체가 늘지 않으면 예비를 잠근다 (모르는 샘에 예비를 버리지 않는다) ──
        (double used, long held, string said) Refill(bool leak)
        {
            var s = ShipWorld.Create("Hwasal", 11);
            var f = s.Get<Field>(); var net = s.Get<Network>(); var sm = s.Get<ShipMind>(); var L2 = s.Layout;
            foreach (var rm in L2.Rooms) foreach (int c in rm.Cells) for (int g = 0; g < f.G; g++) f.RemoveGas(c, g, f.N[c * f.G + g] * 0.16);   // 지난 사고로 배가 85kPa 남짓 (주컴퓨터가 처음 읽기 전에)
            // 외판에 닿은 방 하나 (생명유지 설비가 없는 방): 센서 선이 끊겼다 — 주컴퓨터가 그 방을 못 본다
            Span<int> nb = stackalloc int[4];
            int hull = -1, room = -1;
            foreach (var rm in L2.Rooms.OrderByDescending(r => r.Cells.Length))
            {
                if (rm.Cells.Length == 0 || rm.Name == "에어락" || net.DataPath[rm.Id].Length == 0 || net.Devices.Any(d => d.Def.Role == "o2gen" && L2.Room[d.Cell] == rm.Id)) continue;
                if (!net.DataPath[rm.Id].Any(c => Enumerable.Range(0, L2.Rooms.Count).All(r2 => r2 == rm.Id || !net.DataPath[r2].Contains(c)))) continue;   // 그 방만 끊을 수 있어야
                foreach (int c in rm.Cells) { L2.Neighbors(c, nb); foreach (int w in nb) if (w >= 0 && L2.Hull[w] && L2.Kind[w] == CellKind.Wall) { hull = w; break; } if (hull >= 0) break; }
                if (hull >= 0) { room = rm.Id; break; }
            }
            var path = net.DataPath[room];
            int cut = path.Reverse().FirstOrDefault(c => Enumerable.Range(0, L2.Rooms.Count).All(r2 => r2 == room || !net.DataPath[r2].Contains(c)), -1);   // 그 방에만 쓰이는 칸
            net.CutData(s, cut, s.Causes.Add(CauseKind.Setup, "센서 선이 끊겼다", s.Tick));
            for (int k = 0; k < Time.Minutes(15) && net.AirRelease <= 0; k++) s.Step();
            if (Debug) Console.WriteLine($"    {(leak ? "새는" : "멀쩡한")} 배: {s.Tick / 25.0:0}분에 예비를 푼다 {net.AirRelease:0.000}몰/초 · 사건 {string.Join(",", sm.Incidents.Where(x => x.Active != 0).Select(x => ShipMind.KindName(x.Kind)))}");
            // 푸는 만큼 새는 금 (50cm² 밸브가 63kPa에서 15.6몰/초 — 1cm² · 1kPa에 0.005몰/초)
            if (leak) f.SetHole(hull, (float)(net.AirRelease / (0.005 * 85) * 1e-4));
            double r0 = net.AirReserve;
            s.Run(Time.Minutes(40));
            return (r0 - net.AirReserve, sm.RefillHeld, sm.Log.LastOrDefault(x => x.text.Contains("풀어도")).text ?? "");
        }
        // ── 끼여 열린 바깥문: 파편이 에어락 바깥문을 반쯤 열린 채 끼웠다 — '순환 중'이 아니다 · 그 사이 다른 방의 작은 금을 배 전체 느린 샘으로 찾는다 (화살호 시드 18: 스무 시간 가려져 셋이 숨졌다) ──
        {
            var s = ShipWorld.Create("Hwasal", 11);
            var f = s.Get<Field>(); var st = s.Get<Structure>(); var sm = s.Get<ShipMind>(); var L2 = s.Layout;
            var ext = L2.Doors.First(d => d.External);
            int alRoom = L2.Room[st.OutsideBleedRoomCell(ext.Id)];
            st.DoorJam[ext.Id] = 1; f.SetDoor(ext.Id, 0.3f);
            s.Run(Time.Minutes(20));                                            // 에어락은 빠졌다 · 안쪽 문은 닫혀 있다
            Span<int> nb = stackalloc int[4];
            int hull = -1, room = -1;
            foreach (var rm in L2.Rooms.Where(r => r.Id != alRoom && r.Cells.Length > 0).OrderByDescending(r => r.Cells.Length))
            {
                foreach (int c in rm.Cells) { L2.Neighbors(c, nb); foreach (int w in nb) if (w >= 0 && L2.Hull[w] && L2.Kind[w] == CellKind.Wall) { hull = w; break; } if (hull >= 0) break; }
                if (hull >= 0) { room = rm.Id; break; }
            }
            f.SetHole(hull, 0.00015f);                                          // 1.5cm² — 방 하나의 샘 판단으로는 잡히지 않을 만큼
            long found = -1;
            for (int k = 0; k < Time.Minutes(40) && found < 0; k++) { s.Step(); if (sm.Incidents.Any(x => x.Active != 0 && x.Kind == IncidentKind.Leak)) found = k; }
            Check("끼여 열린 바깥문: 반쯤 열린 채 끼인 에어락 바깥문을 '순환 중'으로 보지 않는다 → 다른 방 외판의 작은 금을 배 전체 느린 샘으로 찾는다",
                found >= 0, found >= 0 ? $"금 {found / 25.0:0}분 뒤 '공기가 샌다' — {ShipMind.KindName(IncidentKind.Leak)} · {sm.RoomName(sm.Incidents.First(x => x.Active != 0 && x.Kind == IncidentKind.Leak).Room)} (금은 {L2.Rooms[room].Name})" : "40분 동안 샘을 모른다");
        }

        var tightT = Task.Run(() => Refill(false)); var leaky = Refill(true); var tight = tightT.Result;
        if (Debug) Console.WriteLine($"    새지 않는 배: 예비 {tight.used:0}몰 · 참음 {tight.held} / 새는 배: 예비 {leaky.used:0}몰 · 참음 {leaky.held}");
        Check("예비 공기: 못 보는 방의 금으로 푸는 만큼 새면 — 배 기체가 늘지 않는 것을 보고 예비를 잠근다 (새지 않는 배는 계속 푼다)",
            tight.held == 0 && tight.used > 0 && leaky.held >= 1 && leaky.used < tight.used * 0.6 && leaky.said.Length > 0,
            $"새지 않는 배: 예비 {tight.used:0}몰 풀었다 / 새는 배: {leaky.used:0}몰 · \"{leaky.said}\"");
    }
}
