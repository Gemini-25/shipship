using ShipEngine;

namespace Lab;

/// <summary>⑧ 고립의 피로 (바퀴 3) — 몇 주에 걸쳐 쌓인다 (예민하고 새것을 바라는 이는 빨리) · 지친 둘은 더 부딪친다 · 반가운 일이 풀어 준다. docs/ENGINE.md ⑧</summary>
public static partial class Program
{
    private static void WearyRules()
    {
        if (!Do("고립의 피로")) return;
        Console.WriteLine("\n[⑧ 고립의 피로]");
        var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);

        // ── 쌓인다: 깨어 있는 시간마다 · 예민하고 새것을 바라는 이(신경성 · 열림 0.9)가 무던한 이(0.1)보다 세 배쯤 빨리 ──
        {
            var s = Worlds.Full(lay, 3);
            var m = s.Get<Mind>();
            int edgy = m.AddPerson(s, At(13, 2)), calm = m.AddPerson(s, At(16, 4));
            m.SetPersonality(edgy, 0.9f, 0.5f, 0.5f, 0.5f, 0.9f);
            m.SetPersonality(calm, 0.1f, 0.5f, 0.5f, 0.5f, 0.1f);
            for (int k = 0; k < Time.Hours(8); k++) { s.Step(); foreach (int p in new[] { edgy, calm }) { m[p].C.Hunger = 0; m[p].C.Sleepy = 0; m[p].C.Bored = 0; m[p].C.Lonely = 0; } }
            float we = m[edgy].C.Weary, wc = m[calm].C.Weary;
            Check("고립의 피로가 쌓인다: 깨어 있는 여덟 시간 — 예민하고 새것을 바라는 이는 무던한 이보다 빨리 지친다 (25일에 다 차는 꼴)",
                we > 0.02f && we < 0.08f && we > 2 * wc && wc > 0,
                $"예민한 이 {we:0.000} · 무던한 이 {wc:0.000}");
        }

        // ── 지친 둘은 더 부딪친다: 같은 성격(친화 0.35 둘)의 잡담 — 피로 0.9면 말끝마다 부딪치는 일이 잦다 ──
        {
            (long friction, long talks) Chat1(float weary, ulong seed)
            {
                var s = Worlds.Full(lay, seed);
                var m = s.Get<Mind>(); var so = s.Get<Society>();
                int x = m.AddPerson(s, At(14, 2)), y = m.AddPerson(s, At(15, 3));
                m.SetPersonality(x, 0.5f, 0.5f, 0.5f, 0.35f, 0.5f);
                m.SetPersonality(y, 0.5f, 0.5f, 0.5f, 0.35f, 0.5f);
                s.Run(2);
                for (int k = 0; k < Time.Hours(4); k++) { s.Step(); foreach (int p in new[] { x, y }) { m[p].C.Hunger = 0; m[p].C.Sleepy = 0; m[p].C.Lonely = 0.5f; m[p].C.Weary = weary; } }
                return (so.Sayings.Count(q => q.End == TalkEnd.Friction), so.Talks);
            }
            (long friction, long talks) Chat(float weary)
            {
                var r = new ulong[] { 4, 5, 6, 7, 8, 9 }.Select(sd => Task.Run(() => Chat1(weary, sd))).ToArray().Select(t => t.Result).ToList();
                return (r.Sum(q => q.friction), r.Sum(q => q.talks));
            }
            var fresh = Chat(0); var worn = Chat(0.9f);
            Check("지친 둘은 더 부딪친다: 같은 둘의 잡담 — 피로 0.9면 말끝마다 부딪치는 일이 잦고 · 피로가 없으면 드물다",
                worn.friction >= 3 && worn.friction >= 3 * Math.Max(1, fresh.friction) && fresh.talks > 0,
                $"피로 없음: 부딪침 {fresh.friction}/{fresh.talks} · 피로 0.9: 부딪침 {worn.friction}/{worn.talks}");
        }

        // ── 풀어 준다: 다른 배와 닿으면 0.1 · 도착 점화면 0.4 (끝이 보인다) ──
        {
            var s = Worlds.Full(lay, 3);
            var m = s.Get<Mind>();
            int p = m.AddPerson(s, At(13, 2));
            s.Run(2);
            m[p].C.Weary = 0.7f;
            s.Facts.Emit(s.Tick, FactKind.Contact, -1, 0, s.Causes.Add(CauseKind.Contact, "상선이 교신해 왔다", s.Tick, -1), other: 1);
            s.Run(30);
            float afterContact = m[p].C.Weary;
            s.Facts.Emit(s.Tick, FactKind.Burn, -1, 0.1f, s.Causes.Add(CauseKind.Route, "도착 점화", s.Tick, -1), other: (int)BurnKind.Arrive);
            s.Run(30);
            float afterArrive = m[p].C.Weary;
            Check("반가운 일이 풀어 준다: 다른 배와 닿으면 조금 · 도착 점화면 크게 (끝이 보인다)",
                afterContact < 0.62f && afterContact > 0.55f && afterArrive < 0.25f,
                $"피로 0.70 → 교신 뒤 {afterContact:0.00} → 도착 점화 뒤 {afterArrive:0.00}");
        }
    }
}
