using ShipEngine;

namespace Lab;

/// <summary>⑬ 이야기 편집 규칙 시험 — 하루 · 한 주의 장 · 다시보기 컷 목록 (docs/ENGINE.md ⑬ 이야기 편집).</summary>
public static partial class Program
{
    private static void ChapterRules()
    {
        if (!Do("이야기 편집")) return;
        Console.WriteLine("\n[⑬ 이야기 편집]");
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);

        // ── 불이 난 날: 제목은 그 불 · 큰 사슬 줄 · 다시 볼 장면 · 주인공 / 컷: 조짐(뿌리 자리) → 순간 · 정점(가장 느리게) → 사람의 시점 → 뒤 ──
        {
            var s = Worlds.Full(lay, 2);
            var m = s.Get<Mind>();
            int a = m.AddPerson(s, At(14, 2)), b = m.AddPerson(s, At(17, 3));
            var d = new Director(s.Layout) { KeepRing = false };
            for (int i = 0; i < 30; i++) { s.Step(); d.Observe(s); }
            int fire = At(15, 1);
            int c0 = s.Causes.Add(CauseKind.Setup, "의자 밑 불씨", s.Tick, fire);
            s.Get<Chemistry>().Ignite(fire, 3e6, c0);
            for (int i = 0; i < Time.Minutes(20); i++) { s.Step(); d.Observe(s); }
            var ch = d.ChapterNow(s);
            var h = d.Highlights.OrderByDescending(x => x.Score).FirstOrDefault();
            Check("불이 난 날의 장: 제목이 그 일 · '큰 사슬' 줄 · 다시 볼 장면 · 주인공(나온 사람) · 하루의 결",
                ch.Title.StartsWith("1일째 — ") && !ch.Title.Contains("조용한") && ch.Lines.Any(l => l.StartsWith("큰 사슬")) && ch.Scenes.Count >= 1 && ch.Cast.Count >= 1 && ch.Arc.Contains("→"),
                $"\"{ch.Title}\" · 장면 {ch.Scenes.Count} · 주인공 {string.Join(", ", ch.Cast.Select(m.NameOf))} · {string.Join(" / ", ch.Lines)}");
            var shots = h != null ? d.Shots(s, h) : new List<Shot>();
            bool ordered = shots.Zip(shots.Skip(1)).All(p => p.First.From < p.Second.From && p.First.To <= p.Second.From)
                && shots.All(x => x.To > x.From && x.To - x.From <= (x.Kind == "정점" ? 2 : 1) * Director.ShotCap && x.At >= x.From && x.At <= x.To);
            var peak = shots.FirstOrDefault(x => x.Kind == "정점");
            Check("컷 목록: 첫 조짐(불씨 자리) → 일이 난 순간들(정점은 가장 느리게) → 사람의 시점 → 뒤 · 때 순 · 겹치지 않게 · 컷 하나는 1분(정점 2분)까지 · 여덟 개까지",
                shots.Count is >= 4 and <= Director.MaxShots && shots[0].Kind == "조짐" && shots[0].Cell == fire && peak != null && peak.Speed == Director.CriticalSpeed
                && shots.Any(x => x.Kind == "시점" && x.Actor >= 0 || x.Kind is "정점" or "순간" && x.Actor >= 0) && shots[^1].Kind == "뒤" && ordered,
                h == null ? "하이라이트 없음" : string.Join(" | ", shots.Select(x => $"{x.Kind} {Time.Clock(x.From)}~{Time.Clock(x.To)} ×{x.Speed} {(x.Actor >= 0 ? m.NameOf(x.Actor) + " " : "")}{x.Caption}")));
        }

        // ── 조용한 날: 큰일이 없으면 '조용한 하루' ──
        {
            var s = Worlds.Full(lay, 3);
            var m = s.Get<Mind>();
            m.AddPerson(s, At(14, 2));
            var d = new Director(s.Layout) { KeepRing = false };
            for (int i = 0; i < Time.Hours(1); i++) { s.Step(); d.Observe(s); }
            var ch = d.ChapterNow(s);
            Check("조용한 날의 장: 하이라이트가 없으면 '조용한 하루' · '큰일 없이 지나갔다'", ch.Title == "1일째 — 조용한 하루" && ch.Scenes.Count == 0 && ch.Lines.FirstOrDefault() == "큰일 없이 지나갔다",
                $"\"{ch.Title}\" · {string.Join(" / ", ch.Lines)}");
        }

        // ── 사람을 잃은 날: 제목이 그 사람 ──
        {
            var s = Worlds.Full(lay, 5);
            var m = s.Get<Mind>();
            int dead = m.AddPerson(s, At(3, 2));
            var d = new Director(s.Layout) { KeepRing = false };
            for (int i = 0; i < 5; i++) { s.Step(); d.Observe(s); }
            s.Facts.Emit(s.Tick, FactKind.Died, At(3, 2), 0, s.Causes.Add(CauseKind.Setup, "숨이 멎었다", s.Tick, At(3, 2)), m[dead].C.Body);
            for (int i = 0; i < 5; i++) { s.Step(); d.Observe(s); }
            var ch = d.ChapterNow(s);
            Check("사람을 잃은 날의 장: 제목 'n일째 — ○○을 잃은 날' · '잃음' 줄", ch.Title == $"1일째 — {Ko.Eul(m.NameOf(dead))} 잃은 날" && ch.Lines.Any(l => l.StartsWith("잃음 — " + m.NameOf(dead))),
                $"\"{ch.Title}\" · {string.Join(" / ", ch.Lines)}");
        }

        // ── 한 주 장: 날마다의 제목 · 그 주에 가장 큰 날이 제목 (사람을 잃은 날이 먼저) · 조용한 주 ──
        {
            var d = new Director(lay);
            Chapter Day(int n, string title, float peak, params int[] cast)
            {
                var c = new Chapter { Day = n, Title = $"{n + 1}일째 — {title}", Peak = peak };
                c.Cast.AddRange(cast);
                return c;
            }
            var week = d.WeekOf(Enumerable.Range(0, 7).Select(n => n == 2 ? Day(n, "바위 띠를 빠져나왔다", 300, 1) : n == 4 ? Day(n, "하늘을 잃은 날", 150, 2, 1) : Day(n, "조용한 하루", 0, 1)).ToList());
            var calm = d.WeekOf(Enumerable.Range(7, 7).Select(n => Day(n, "조용한 하루", 0)).ToList());
            Check("한 주 장: 날마다의 제목 일곱 줄 · 사람을 잃은 날이 그 주의 제목 · 가장 자주 나온 사람 · 조용한 주는 '조용한 한 주'",
                week.Week && week.Title == "1주째 — 하늘을 잃은 날" && week.Lines.Count == 7 && week.Cast.SequenceEqual(new[] { 1 }) && calm.Title == "2주째 — 조용한 한 주",
                $"\"{week.Title}\" · 주인공 {string.Join(",", week.Cast)} · \"{calm.Title}\"");
        }

        // ── G6 (docs/OBSERVER.md §5.5 — V3 · 바뀜): 큰 일 없는 날 둘이 짝이 되면 그날의 제목 · 같은 두 사람 사이 일이 이레에 셋을 넘으면 한 주 장에 '관계 장면' · 하이라이트 · 문턱은 그대로 ──
        //    (조용한 날의 장이 사람 일 장부를 읽는다 — 위 '조용한 날' 시험의 세계는 한 시간 혼자라 사람 일이 없어 그대로 '조용한 하루'다)
        {
            var s = Worlds.Full(lay, 7);
            var m = s.Get<Mind>();
            int a = m.AddPerson(s, At(14, 2)), b = m.AddPerson(s, At(17, 3));
            var d = new Director(s.Layout) { KeepRing = false };
            for (int i = 0; i < 30; i++) { s.Step(); d.Observe(s); }
            string na = m.NameOf(a), nb = m.NameOf(b);
            int c = s.Causes.Add(CauseKind.Society, $"{Ko.Gwa(na)} {Ko.I(nb)} 짝이 되었다", s.Tick, m[a].C.Cell, 0, 0, a);
            s.Facts.Emit(s.Tick, FactKind.Relationship, m[a].C.Cell, 1, c, a, b);
            for (int k = 0; k < 3; k++)   // 사과 셋 (따로따로 — 한 자리에 몰면 관심이 문턱을 넘는다)
            {
                for (int i = 0; i < 30; i++) { s.Step(); d.Observe(s); }
                s.Facts.Emit(s.Tick, FactKind.Talk, m[b].C.Cell, (int)TalkEnd.Apology, 0, b, a);
            }
            for (int i = 0; i < 30; i++) { s.Step(); d.Observe(s); }
            var ch = d.ChapterNow(s);
            var week = d.WeekOf(Enumerable.Range(0, 7).Select(n => new Chapter { Day = n, Title = $"{n + 1}일째 — 조용한 하루", From = n == 0 ? 0 : n * Time.TicksPerDay, To = n == 6 ? s.Tick + 1 : (n + 1) * Time.TicksPerDay }).ToList());
            Check("G6 사람 일 장 제목: 큰 일 없는 날 둘이 짝이 되면 '1일째 — 김하늘과 정지우가 짝이 되었다' · '사람 일' 줄 · 같은 두 사람 사이 일이 이레에 셋을 넘으면 한 주 장에 '관계 장면' 줄 · 하이라이트 0 · 문턱 80 그대로",
                ch.Title == $"1일째 — {Ko.Gwa(na)} {Ko.I(nb)} 짝이 되었다" && ch.Lines.Any(l => l.StartsWith("사람 일 — ")) && week.Lines.Any(l => l.StartsWith("관계 장면 — ") && l.Contains(na) && l.Contains(nb))
                && d.Highlights.Count == 0 && Director.HighlightAt == 80,
                $"\"{ch.Title}\" · {string.Join(" / ", ch.Lines)} · 주 {string.Join(" / ", week.Lines.Skip(7))} · 하이라이트 {d.Highlights.Count}");
        }
    }
}
