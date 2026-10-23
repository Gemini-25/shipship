using ShipEngine;

namespace Lab;

/// <summary>판단 결함 넷 (docs/REVIEW.md ③ — 함장 결정 '추천대로'): 사람 사건 열쇠 · 흔들림 · 손 비우기 · 일감 실패 기억.</summary>
public static partial class Program
{
    private static void MindFixRules()
    {
        if (!Do("판단 결함")) return;
        Console.WriteLine("\n[판단 결함 넷]");
        // ── 사람 사건은 사람에 묶는다: 덮지 않은 화상을 입은 이가 방을 오가도 사건은 하나 · 맡은 로봇이 바뀌지 않는다 (예전 20분에 사건 14개) ──
        {
            var s = ShipWorld.Crewed("Hwasal", 11, 2, 2, 0);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var sm = s.Get<ShipMind>();
            s.Run(Time.Minutes(10));
            var p = m.Agents.First(a => a.IsPerson);
            int body = p.C.Body;
            life[body].Burn = 0.4f;
            var rooms = s.Layout.Rooms.Where(r => r.Cells.Length > 6).ToArray();
            int hops = 0;
            for (long t = 1; t <= Time.Minutes(20); t++)
            {
                if (t % (long)Time.Minutes(1.5) == 0 && life[body].Dressed <= 0)   // 방을 오간다 (몸을 옮겨 흉내)
                {
                    var r = rooms[(hops * 3 + 1) % rooms.Length];
                    life[body].Cell = r.Cells[r.Cells.Length / 2];
                    hops++;
                }
                s.Step();
            }
            var ids = sm.Incidents.Where(x => x.Kind == IncidentKind.Injured && x.Subject == body).Select(x => x.Id).Distinct().ToList();
            Check("사람 사건은 사람에 묶인다: 화상 입은 이가 방을 오가도 '다친 사람' 사건은 하나 (예전엔 방마다 새로 서 20분에 14개)",
                ids.Count == 1 && hops >= 3, $"방 옮김 {hops}번 · 사건 {ids.Count}개 · 덮음 {life[body].Dressed:0.00}");
        }
        // ── 흔들림 (벽 너머 변기 둘): 디딤호 통신실의 마려운 이 — 벽 너머 화장실의 두 변기 사이를 오가지 않고 한 곳에서 볼일 (예전 2시간 반 오감) ──
        {
            var s = ShipWorld.Crewed("Didim", 11, 1, 0, 0);
            var m = s.Get<Mind>(); var life = s.Get<Life>();
            s.Run(5);
            var p = m.Agents.First(a => a.IsPerson);
            int spot = Worlds.At(s.Layout, 39, 15);
            p.C.Cell = spot; life[p.C.Body].Cell = spot; p.Plan.Clear(); p.Path.Clear(); p.PathIdx = 0;
            life[p.C.Body].Bladder = (float)(0.92 * Life.BladderMax); p.C.Sleepy = 0; p.C.Hunger = 0; p.C.Thirst = 0;
            var g = new MindGauge(s);
            long toilets0 = m.Toilets;
            for (int k = 0; k < Time.Minutes(30); k++) { s.Step(); g.Observe(s.Tick); p.C.Sleepy = 0; }
            Check("흔들림: 벽 너머 화장실의 변기 둘 사이를 오가지 않고 (길 거리로 고르고 · 고른 곳을 지킨다) 한 곳에서 볼일",
                g.Flips == 0 && m.Toilets > toilets0, $"흔들림 {g.Flips}번 · 볼일 {m.Toilets - toilets0} · {m.Thought(p.Id)}");
        }
        // ── 문턱 흔들림 (대피 중 목마른 이): 대피실에 든 이는 목이 말라도 문 앞에서 들락거리지 않고 대피실에 머문다 ──
        {
            var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
            Sim? s = null; Mind? m = null; Agent? p = null;
            for (ulong seed = 1; seed < 40; seed++)
            {
                s = Worlds.Full(lay, seed); m = s.Get<Mind>();
                p = m[m.AddPerson(s, Worlds.At(lay, 15, 8))];
                s.Run(2);
                p.C.Diligent = 1;
                long h0 = m.SheltersHeeded;
                m.Shelter(s, lay.Room[Worlds.At(lay, 15, 2)], Worlds.At(lay, 15, 2), s.Tick + Time.Hours(2), 0, 1);
                if (m.SheltersHeeded > h0) break;
            }
            int shelter = lay.Room[Worlds.At(lay, 15, 2)];
            for (int k = 0; k < Time.Minutes(10); k++) s!.Step();
            bool arrived = m!.RoomOf(p!.C.Cell) == shelter;
            p.C.Thirst = 0.85f; p.C.Hunger = 0.3f;
            var g = new MindGauge(s!);
            int outside = 0;
            for (int k = 0; k < Time.Minutes(20); k++) { s!.Step(); g.Observe(s.Tick); if (m.RoomOf(p.C.Cell) != shelter) outside++; }
            Check("문턱 흔들림: 대피실에 든 이는 목이 말라도 문 앞에서 들락거리지 않고 대피실에 머문다 ('거기 있어라' 선택지가 방 안에서도 남는다)",
                arrived && g.Flips == 0 && outside < Time.Minutes(1), $"닿음 {arrived} · 흔들림 {g.Flips}번 · 대피실 밖 {outside * Time.SecondsPerTick / 60:0.0}분 · {m.Thought(p.Id)}");
        }
    }
}
