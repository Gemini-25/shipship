using ShipEngine;

namespace Lab;

/// <summary>⑩ 주컴퓨터 — 샘 판단 · ⑧ 빠져나온 방: 디딤호 하루 일지에서 찾은 결함 셋을 작은 배로 (원자로실 · 둘레 통로 · 재배실이 함께 '샌다').</summary>
public static partial class Program
{
    private static void LeakJudgeRules()
    {
        if (!Do("샘 판단")) return;
        Console.WriteLine("\n[⑩ 샘 판단 · 빠져나온 방]");
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        int left = lay.Room[At(4, 2)], right = lay.Room[At(15, 2)];

        // ── 왼쪽 방이 새어 봉쇄된 뒤 그 문의 압력 맞춤 밸브를 열면 오른쪽 방 공기가 그리로 몰린다 — 옮겨 간 공기다 (다른 방이 받았다): 오른쪽 방은 '샌다'가 아니다 ──
        {
            var s = Worlds.Full(lay, 1);
            var f = s.Get<Field>(); var st = s.Get<Structure>(); var sm = s.Get<ShipMind>();
            s.Run(5);
            st.DamageCause[At(0, 2)] = s.Causes.Add(CauseKind.Story, "외판에 작은 금이 갔다", s.Tick, At(0, 2));
            f.SetHole(At(0, 2), 0.0015f);
            for (int k = 0; k < Time.Minutes(6) && sm.Find(IncidentKind.Leak, left) < 0; k++) s.Step();
            bool found = sm.Find(IncidentKind.Leak, left) >= 0;
            int door = lay.DoorAt[At(10, 3)];
            for (int k = 0; k < Time.Minutes(3) && f.Pressure(At(15, 2)) - f.Pressure(At(4, 2)) < 10; k++) s.Step();
            double before = f.Pressure(At(15, 2)) - f.Pressure(At(4, 2));
            for (int k = 0; k < Time.Minutes(2); k++)
            {
                if (k < 10) st.RequestDoor(door, 1); // 로봇이 살피러 들어가려고 문을 연다 (디딤호: 새는 에어락 문 — 옆 방 공기가 한꺼번에 몰린다)
                s.Step();
                if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1" && k % 5 == 0) Console.WriteLine($"    {k} 왼 {f.Pressure(At(4, 2)):0.0} 오른 {f.Pressure(At(15, 2)):0.0} 문 {f.DoorOpen[door]:0.0} · 사건 {string.Join(",", sm.Incidents.Where(x => x.Active != 0).Select(x => sm.RoomName(x.Room) + ShipMind.KindName(x.Kind)))}");
            }
            int rightLeaks = sm.Incidents.Count(x => x.Kind == IncidentKind.Leak && x.Room == right);
            Check("새는 방의 문을 열어 옆 방 공기가 그리로 몰리면 옮겨 간 공기다 (다른 방이 받았다) — 옆 방은 '샌다'가 아니다",
                found && before > 3 && rightLeaks == 0, $"왼쪽 방 샘 찾음 {found} · 문 열기 전 기압 차 {before:0.0}kPa · 오른쪽 방 '샌다' {rightLeaks}건");
        }

        // ── 일하던 방에 금 소식 → 빠져나와 문을 닫는다 — 아직 위험하다고 믿는 동안 그 방의 침대로 되돌아가지 않는다 (나왔다 들어갔다 문을 여닫지 않게) ──
        {
            var s = Worlds.Full(lay, 2);
            var m = s.Get<Mind>(); var st = s.Get<Structure>();
            int p = m.AddPerson(s, At(4, 4));
            m.Agents[p].C.Sleepy = 0.97f;
            s.Run(5);
            int cause = s.Causes.Add(CauseKind.Story, "왼쪽 방 외판에 금이 갔다", s.Tick, At(0, 3));
            m.Announce(s, At(4, 2), 85, new Belief { Kind = BeliefKind.Leak, Cell = At(1, 3), Subject = -1, Conf = 0.9f, Cause = cause });
            int entries = 0; bool inside = true;
            for (int k = 0; k < Time.Minutes(8); k++)
            {
                s.Step();
                bool now = lay.Room[m.Agents[p].C.Cell] == left;
                if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1" && k < 60) Console.WriteLine($"    {k} 칸 {lay.X(m.Agents[p].C.Cell)},{lay.Y(m.Agents[p].C.Cell)} 목표 {m.Agents[p].C.Goal} 침대 {m.Agents[p].C.Bed} · {m.Thought(p)} · 피함 {string.Join(",", m.Agents[p].Avoid.Select(v => v.Goal + ":" + v.Target))} · 믿음 {string.Join(",", m.Agents[p].Beliefs.Where(b => b.Kind == BeliefKind.Leak).Select(b => $"{b.Cell}:{b.Conf:0.00}"))}");
                if (now && !inside) entries++;
                inside = now;
            }
            long seals = s.Facts.Since(0).Count(x => x.Kind == FactKind.DoorSealed && x.Actor == p);
            Check("금 소식에 방을 빠져나온 사람은 아직 위험하다고 믿는 동안 그 방(침대)으로 되돌아가지 않는다 — 문을 한 번만 닫는다",
                seals == 1 && entries == 0 && !inside, $"문 닫음 {seals}번 · 다시 들어감 {entries}번 · 지금 그 방 {inside} · 생각 '{m.Thought(p)}'");
        }
    }
}
