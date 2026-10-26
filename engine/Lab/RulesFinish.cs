using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>
/// 연출 · 마무리 (docs/UI2.md U9 — 묶음 '연출 손질'): 장면 판 줄 머리 '함장의 손 · ' (손이 뿌리인 하이라이트) · 도움말 글자 ·
/// 일으킨 칸 고리 (손이 실제로 일어난 칸 — 3초) · 두 곳 나눠 보기 (따라가는 동안 다른 곳의 위험을 구석 작은 창에 · 누르면 그리로).
/// </summary>
public static partial class Program
{
    private static void FinishRules()
    {
        if (!Do("연출 손질")) return;
        Console.WriteLine("\n[연출 · 마무리 — 장면 판 '함장의 손' · 도움말 · 일으킨 칸 고리 · 두 곳 나눠 보기]");
        var show = LiveShow.Crewed("Didim", 51);
        show.FrameBudget = 1000; show.SpeedOverride = 1;
        for (int i = 0; i < 3; i++) show.Advance(1);
        var w = show.Sim; var L = w.Layout; var mind = w.Get<Mind>();

        // ── 장면 판 줄 머리: 손이 뿌리인 하이라이트는 '함장의 손 · ' (다른 뿌리는 그대로) ──
        {
            int c = L.Rooms.First(r => r.Type == "Mess").Cells[0];
            int root = w.Causes.Add(CauseKind.Captain, "함장이 일으킨 폭발 — 식당", w.Tick, c);
            int child = w.Causes.Add(CauseKind.Impact, "폭발에 식탁이 넘어졌다", w.Tick, c, root);
            int other = w.Causes.Add(CauseKind.Setup, "침대 밑에서 불씨가 튀었다", w.Tick, c);
            show.Dir.Highlights.Add(new Highlight { Tick = w.Tick, StartTick = w.Tick, Last = w.Tick, Cell = c, Score = 60, Kind = FactKind.WallBreach, Cause = child, Root = root, Title = "폭발 — 식당" });
            show.Dir.Highlights.Add(new Highlight { Tick = w.Tick, StartTick = w.Tick, Last = w.Tick, Cell = c, Score = 40, Kind = FactKind.Ignition, Cause = other, Root = other, Title = "불 — 침실" });
            show.TogglePanel(PanelKind.Highlights);
            var rows = show.PanelRows();
            show.ClosePanel();
            show.Dir.Highlights.RemoveRange(show.Dir.Highlights.Count - 2, 2);
            bool hand = rows.Any(r => r.Main.Contains("함장의 손 · 폭발 — 식당")), plain = rows.Any(r => r.Main.Contains("불 — 침실") && !r.Main.Contains("함장의 손"));
            Check("마무리 장면 판: 원인 사슬의 뿌리가 함장의 손인 하이라이트는 줄 머리에 '함장의 손 · ' (사람 이야기 · 와 같은 자리) · 다른 뿌리는 그대로",
                hand && plain, string.Join(" | ", rows.Select(r => r.Main)));
        }

        // ── 도움말 글자: 새 탭 · 사고 · 살펴보기 · 자세히 · 구석 작은 창 줄 · 없는 글자 0 ──
        {
            var lines = Hud.HelpLines("1 '가장 짧은 길'");
            string[] need = { "로봇 (G)", "사고 (X)", "사고 탭", "물건 누르기", "자세히", "(P · G · J)", "구석 작은 창" };
            var lack = need.Where(n => !lines.Any(l => l.Contains(n))).ToList();
            var glyph = lines.Where(l => l.Any(ch => Hud.Missing.Contains(ch))).ToList();
            Check("마무리 도움말: 판 탭 일곱 (로봇 G · 사고 X) · '사고 탭 — 줄을 누르고 배 위를 누르면 일어난다' · '물건 누르기 — 살펴보기' · '사람 · 로봇 · 주컴퓨터 판 — 자세히 (P · G · J)' · '구석 작은 창' · 글꼴에 없는 글자 0",
                lack.Count == 0 && glyph.Count == 0, lack.Count > 0 ? "없는 줄: " + string.Join(" · ", lack) : glyph.Count > 0 ? "없는 글자: " + glyph[0] : $"줄 {lines.Length}");
        }

        // ── 일으킨 칸 고리: 받아 둔 손이 다음 틱에 실제로 일어나면 그 칸에 3초 (누른 칸 고리 0.5초와 따로) ──
        {
            int bed = L.Furniture.First(f => f.Type == "Bed").Cells[0];
            show.ArmHand(HandKind.Fire, 0);
            bool took = show.HandAt(L.X(bed) + 0.5, L.Y(bed) + 0.5);
            show.Advance(1);   // 다음 틱 맨 처음에 일어난다
            var f1 = show.Render(); show.Recycle(f1.Img);
            bool ring = f1.HandMarks.Any(m => m.Done && Math.Abs(m.X - (L.X(bed) + 0.5)) < 1.5 && Math.Abs(m.Y - (L.Y(bed) + 0.5)) < 1.5);
            show.Paused = true;
            for (int k = 0; k < 8; k++) show.Advance(0.5);
            var f2 = show.Render(); show.Recycle(f2.Img);
            bool gone = !f2.HandMarks.Any(m => m.Done);
            show.Paused = false;
            Check("마무리 일으킨 칸 고리: 받아 둔 손 (침대 불씨)이 실제로 일어난 틱에 그 칸에 고리 — 화면 3초 뒤 사라진다",
                took && ring && gone, $"받음 {took} · 고리 {ring} · 4초 뒤 사라짐 {gone} · 표 {string.Join(" ", f1.HandMarks.Select(m => $"({m.X:0.0},{m.Y:0.0}{(m.Done ? " 일어남" : m.Waiting ? " 기다림" : "")})"))}");
        }

        // ── 두 곳 나눠 보기: 사람을 따라가는 동안 다른 곳에서 위험 · 결정적 순간 → 구석 작은 창 · 누르면 연출을 따라 그리로 · 연출을 따라가면 창이 없다 ──
        {
            var s2 = LiveShow.Crewed("Didim", 52);
            s2.FrameBudget = 1000; s2.SpeedOverride = 2;
            for (int i = 0; i < 3; i++) s2.Advance(1);
            var w2 = s2.Sim; var L2 = w2.Layout; var m2 = w2.Get<Mind>();
            var f0 = s2.Render(); s2.Recycle(f0.Img);
            bool none0 = f0.InsetTitle.Length == 0;
            // 가장 먼 두 사람: 한 사람을 따라가고 · 다른 사람 곁에 큰 폭발
            var people = m2.Agents.Where(a => a.IsPerson).ToList();
            var (pa, pb) = people.SelectMany(a => people.Select(b => (a, b))).OrderByDescending(t => Math.Abs(L2.X(t.a.C.Cell) - L2.X(t.b.C.Cell)) + Math.Abs(L2.Y(t.a.C.Cell) - L2.Y(t.b.C.Cell))).First();
            int at = pb.C.Cell;
            s2.SelectAgent(pa.Id);
            w2.Get<Hand>().Enqueue(new HandOrder { Kind = HandKind.Blast, Cell = at, Size = 2 });
            LiveFrame? fin = null; int tries = 0;
            for (; tries < 60; tries++)
            {
                s2.Advance(1);
                var f = s2.Render();
                if (f.InsetTitle.Length > 0 && f.Inset != null) { fin = f; break; }
                s2.Recycle(f.Img);
            }
            bool far = s2.InsetCell >= 0 && Math.Abs(L2.X(s2.InsetCell) - L2.X(at)) + Math.Abs(L2.Y(s2.InsetCell) - L2.Y(at)) <= 12;
            string title = fin?.InsetTitle ?? "";
            // 비용: 창이 떠 있는 동안 그리기 (0.2초마다 한 장 — 작은 32도트 10 × 6칸)
            double insetMs = 0; int drawn = 0, ver = fin?.InsetVersion ?? 0;
            for (int k = 0; k < 10; k++) { s2.Advance(0.2); var f = s2.Render(); if (f.InsetVersion != ver) { ver = f.InsetVersion; insetMs += s2.InsetMs; drawn++; } s2.Recycle(f.Img); }
            double frameMs = drawn > 0 ? insetMs / drawn : 0;
            // 자리: 가운데 칸 아래 (판 · 카드 · 사람 줄 · 보기 묶음과 겹치지 않는다)
            var hud = new Hud();
            string place = "";
            if (fin != null)
            {
                HudLayout.Build(fin, new HudState(), 1920, 1080, Hud.Approx, Hud.ApproxLines, hud);
                var box = hud.Area("구석 창");
                if (box.Empty || !hud.Hits.Any(x => x.Act == UiAct.Inset)) place = "자리 없음";
                foreach (var o in hud.Overlaps()) place += " 겹침 " + o;
                s2.Recycle(fin.Img);
            }
            s2.InsetGo();
            bool went = !s2.FollowSelected && s2.Follow;
            for (int k = 0; k < 6; k++) s2.Advance(1);
            var fe = s2.Render(); s2.Recycle(fe.Img);
            Check("마무리 두 곳 나눠 보기: 연출을 따라갈 때는 창이 없고 · 먼 사람을 따라가는 동안 다른 사람 곁의 큰 폭발 (위험)이 구석 작은 창에 (그 자리 · 제목) · 판 · 카드 · 사람 줄과 겹치지 않는다 · 누르면 연출을 따라 그리로 · 창이 닫힌다",
                none0 && fin != null && far && title.Contains(" — ") && place.Length == 0 && went && fe.InsetTitle.Length == 0,
                $"처음 없음 {none0} · {tries}번째 장 '{title}' · 그 자리 {far} · {place} · 그리로 {went} · 닫힘 {fe.InsetTitle.Length == 0} · 구석 창 한 장 {frameMs:0.00}ms ({drawn}장 · 0.2초마다)");
        }
    }
}
