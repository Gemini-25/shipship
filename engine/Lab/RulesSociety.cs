using ShipEngine;

namespace Lab;

/// <summary>⑨ 사회 규칙 시험 — 정 · 빚 · 슬픔 · 주컴퓨터에 대한 믿음.</summary>
public static partial class Program
{
    private static void SocietyRules()
    {
        if (!Do("사회")) return;
        Console.WriteLine("\n[사회]");
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);

        // ── 함께 어울리면 가까워진다 (문턱을 넘으면 사실로) ──
        {
            var s = Worlds.Full(lay, 5);
            var m = s.Get<Mind>(); var so = s.Get<Society>();
            int a = m.AddPerson(s, At(15, 2)), b = m.AddPerson(s, At(18, 2));
            s.Run(2);
            so.Bond[a * so.N + b] = 0.4f; so.Bond[b * so.N + a] = 0.4f;
            m[a].C.Lonely = 1; m[b].C.Lonely = 1; m[a].C.Sociable = 0.8f; m[b].C.Sociable = 0.8f;
            for (int k = 0; k < Time.Hours(4); k++)
            {
                s.Step();
                if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1" && k % 250 == 0)
                    Console.WriteLine($"    {k} {m[a].C.Goal}/{m[b].C.Goal} {m.Thought(a)} 칸 {m[a].C.Cell} 방 {m.RoomOf(m[a].C.Cell)} · {m.Thought(b)} 칸 {m[b].C.Cell} 방 {m.RoomOf(m[b].C.Cell)} · 외로움 {m[a].C.Lonely:0.00}/{m[b].C.Lonely:0.00} · 정 {so.BondOf(a, b):0.00}");
            }
            var rel = s.Facts.Since(0).Where(f => f.Kind == FactKind.Relationship).ToList();
            Check("함께 어울리면 가까워진다 — 정이 문턱을 넘으면 '가까워졌다'가 남는다", so.BondOf(a, b) >= Society.FriendAt && so.BondOf(b, a) >= Society.FriendAt && rel.Count > 0,
                $"정 {so.BondOf(a, b):0.00}/{so.BondOf(b, a):0.00} · {(rel.Count > 0 ? s.Causes.Story(rel[0].Cause) : "사실 없음")}");
        }

        // ── 구해 준 은혜: 업혀 나온 사람은 구한 이에게 정이 크게 는다 · 본 사람도 ──
        {
            var s = Worlds.Full(lay, 5);
            var m = s.Get<Mind>(); var so = s.Get<Society>();
            int v = m.AddPerson(s, At(3, 2)), r = m.AddPerson(s, At(4, 2)), w = m.AddPerson(s, At(5, 3));
            s.Run(2);
            int c0 = s.Causes.Add(CauseKind.Story, "연기에 쓰러졌다", s.Tick, At(3, 2));
            s.Facts.Emit(s.Tick, FactKind.Rescue, At(4, 2), 0, c0, r, v);
            s.Run(2);
            Check("구해 준 은혜: 업혀 나온 사람은 구한 이에게 정이 크게 늘고, 곁에서 본 사람도 그를 다시 본다", so.BondOf(v, r) >= 0.4f && so.BondOf(w, r) > 0.05f && so.BondOf(v, r) > so.BondOf(r, v),
                $"구해진 이→구한 이 {so.BondOf(v, r):0.00} · 구한 이→구해진 이 {so.BondOf(r, v):0.00} · 본 사람 {so.BondOf(w, r):0.00}");
        }

        // ── 죽음: 친구는 깊이 슬퍼하고(일손을 놓는다) · 모르는 사이는 조금 ──
        {
            var s = Worlds.Full(lay, 5);
            var m = s.Get<Mind>(); var so = s.Get<Society>(); var life = s.Get<Life>();
            int dead = m.AddPerson(s, At(3, 2)), friend = m.AddPerson(s, At(15, 2)), other = m.AddPerson(s, At(18, 2));
            s.Run(2);
            so.Bond[friend * so.N + dead] = 0.8f;
            s.Facts.Emit(s.Tick, FactKind.Died, At(3, 2), 0, s.Causes.Add(CauseKind.Story, "숨이 멎었다", s.Tick), m[dead].C.Body);
            s.Run(2);
            var g = s.Facts.Since(0).Where(f => f.Kind == FactKind.Grieved).ToList();
            Check("죽음: 친구는 깊이 슬퍼한다 ('잃었다'가 남는다) · 모르는 사이는 조금", so.GriefOf(friend) >= 0.8f && so.GriefOf(other) is > 0.1f and < 0.3f && g.Count == 1 && g[0].Actor == friend,
                $"친구 슬픔 {so.GriefOf(friend):0.00} · 남 {so.GriefOf(other):0.00} · {(g.Count > 0 ? s.Causes.Text(g[0].Cause) : "")}");
        }

        // ── 주컴퓨터가 사람이 있는 채 닫은 방에서 누가 죽으면 → 사람들이 주컴퓨터를 덜 믿고, 그 경보를 덜 믿는다 ──
        {
            var s = Worlds.Full(lay, 5);
            var m = s.Get<Mind>(); var so = s.Get<Society>();
            int dead = m.AddPerson(s, At(3, 2)), a = m.AddPerson(s, At(15, 2)), b = m.AddPerson(s, At(18, 3));
            s.Run(2);
            so.Bond[a * so.N + dead] = 0.7f;
            int room = m.RoomOf(At(3, 2));
            s.Facts.Emit(s.Tick, FactKind.ComputerSeal, At(3, 2), 1, s.Causes.Add(CauseKind.Story, "주컴퓨터가 왼쪽 방을 봉쇄했다", s.Tick), -1, room);
            s.Run(Time.Minutes(10));
            s.Facts.Emit(s.Tick, FactKind.Died, At(3, 2), 0, s.Causes.Add(CauseKind.Story, "숨이 멎었다", s.Tick), m[dead].C.Body);
            s.Run(2);
            float ta = so.Trust(a), tb = so.Trust(b);
            // 같은 경보를 둘이 듣는다: 덜 믿는 사람의 믿음이 더 약하다
            m.Announce(s, At(17, 3), 75, new Belief { Kind = BeliefKind.BadAir, Cell = At(20, 4), Subject = -1, Conf = 0.9f });
            s.Run(3);
            float ca = m[a].Beliefs.Where(x => x.Kind == BeliefKind.BadAir).Select(x => x.Conf).DefaultIfEmpty(0).Max();
            float cb = m[b].Beliefs.Where(x => x.Kind == BeliefKind.BadAir).Select(x => x.Conf).DefaultIfEmpty(0).Max();
            Check("봉쇄된 방에서 사람이 죽으면 주컴퓨터를 덜 믿는다 (친구였으면 더) — 그 뒤 경보를 덜 믿는다", ta < tb && tb < 0.8f && ca < cb && s.Facts.CountOf(FactKind.TrustLost) == 1,
                $"믿음 친구 {ta:0.00} · 남 {tb:0.00} · 같은 경보 믿음 {ca:0.00} / {cb:0.00}");
        }
    }
}
