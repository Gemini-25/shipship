using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>화면 — 4단 로봇 칩 · 로봇 카드 · 주컴퓨터 말 창 (판단 일지 · 주컴퓨터 일지를 읽기만 · 세계 그대로).</summary>
public static partial class Program
{
    private static void RobotViewRules(double dt)
    {
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        // 로봇 카드 · 칩: 주컴퓨터가 불을 맡긴 로봇 — 칩 'R1 불' · 카드 '하는 일: 불 끄기 ▸ …' · '맡은 까닭: 주컴퓨터가 맡겼다 — (까닭)' · '최근 판단: … 맡음 …' · 글 속 이름은 '로봇 1' (칩만 R1) · 금지어 없음 (전지 줄 빼고) · 세계는 쌍둥이와 같다
        {
            Sim Make()
            {
                var s = Worlds.Full(lay, 41);
                var m = s.Get<Mind>();
                m.AddRobot(s, At(3, 7), AgentKind.Robot, At(3, 7));
                m.AddPerson(s, At(20, 8));
                s.Run(12);
                return s;
            }
            var w = Make(); var twin = Make();
            var show = new LiveShow(w, "시험호") { FrameBudget = 1000 };   // 판단 일지 · 주컴퓨터 일지를 켠다 (쓰기만)
            int r = w.Get<Mind>().Agents.First(a => !a.IsPerson).Id;
            foreach (var s in new[] { w, twin }) s.Get<Chemistry>().Ignite(At(15, 1), 3e6, s.Causes.Add(CauseKind.Setup, "빈 방 의자 밑에서 불씨가 튀었다", s.Tick));
            string[] card = Array.Empty<string>(); long seen = -1;
            show.SelectAgent(r);
            for (int i = 0; i < Time.Minutes(30) && seen < 0; i++)   // 틱마다 — 작은 불은 1분 안에 꺼진다
            {
                w.Run(1);
                card = show.Card();
                if (card.Any(l => l.StartsWith("맡은 까닭: 주컴퓨터가 맡겼다")) && card.Any(l => l.StartsWith("최근 판단:"))) seen = w.Tick;
            }
            var f = show.Render(); show.Recycle(f.Img);
            var chip = f.Robots.FirstOrDefault(c => c.Id == r);
            var names = new[] { show.Sim.Get<Mind>().NameOf(r) };
            var bad = card.Skip(1).Where(l => !l.StartsWith("전지") && TextLint.Bad(l, names) != null).Select(l => $"'{l}' {TextLint.Bad(l, names)}").ToList();
            twin.Run(w.Tick - twin.Tick);
            bool same = w.Save().AsSpan().SequenceEqual(twin.Save());
            var doing = card.FirstOrDefault(l => l.StartsWith("하는 일:")) ?? "";
            Check("로봇 카드: 주컴퓨터가 불을 맡긴 로봇 — 칩 'R1' + 하는 일 낱말 · 카드 '하는 일: (목표) ▸ (방법)' · '맡은 까닭: 주컴퓨터가 맡겼다 — (까닭)' · '최근 판단: … 맡음 …' · 글 속 이름은 '로봇 1' (칩만 R1) · 금지어 없음 (전지 줄 빼고) · 세계는 쌍둥이와 같다",
                seen >= 0 && chip.Tag == "R1" && chip.Word.Length is >= 1 and <= 3 && doing.Length > 6 && card.Any(l => l.StartsWith("맡은 까닭: 주컴퓨터가 맡겼다 — ")) && card.Any(l => l.StartsWith("최근 판단:") && l.Contains("맡음")) && bad.Count == 0 && same,
                $"{(seen >= 0 ? Time.Clock(seen) : "못 봄")} · 칩 '{chip.Tag} {chip.Word}' · {string.Join(" | ", card)} · 금지어 {bad.Count} {string.Join(" ", bad.Take(2))} · 같음 {same}");
        }
        // 말 창: 주컴퓨터 말 열둘이 한꺼번에 — 세 줄은 곧바로 · 밀린 줄은 여섯까지 (가벼운 것부터 버린다) · 한 줄은 화면 2초 넘게 · 12초 동안 새 말이 없으면 접는다 · 세 줄 모두 금지어 없음
        {
            var s = Worlds.Full(lay, 43);
            var m = s.Get<Mind>(); m.AddPerson(s, At(20, 8));
            s.Run(12);
            var show = new LiveShow(s, "시험호") { FrameBudget = 1000 };
            show.SetFixed(0);   // ×1 — 시험 동안 주컴퓨터가 새로 말할 틈이 거의 없다
            show.Advance(dt);
            var cm = s.Get<ShipMind>();
            var notes = cm.Notes ??= new Ring<ComputerNote>(512);
            string[] texts = { "식당 히터 전원을 내렸다 — 불이 났다", "로봇 1 → 식당 불", "식당 정화 배기 — 덕트를 정화기로만 돌린다", "식당 봉쇄 (사람이 안에 있다)", "예비 공기를 푼다", "보조 발전기를 켰다", "로봇 2: 식당 불 일이 막혔다 — 소화기가 비었다", "식당 바깥문이 열린 채 아무도 없어 닫았다", "전력이 모자라 덜 중요한 장치를 껐다", "로봇 2 → 복도 연기", "막힌 곳: 식당 불 — 로봇 · 사람 모두 못 했다", "주간 비상 훈련 — 절차를 되짚습니다" };
            NoteKind[] kinds = { NoteKind.Power, NoteKind.Assign, NoteKind.Vent, NoteKind.Seal, NoteKind.Other, NoteKind.Other, NoteKind.Block, NoteKind.Door, NoteKind.Power, NoteKind.Assign, NoteKind.Stuck, NoteKind.Other };
            for (int i = 0; i < texts.Length; i++) notes.Add(new ComputerNote { Tick = s.Tick, Kind = kinds[i], Robot = -1, Incident = -1, Text = texts[i] });
            show.Advance(1 / 60.0);
            var f0 = show.Render(); show.Recycle(f0.Img);
            for (int i = 0; i < 114; i++) show.Advance(1 / 60.0);   // 1.9초
            var f19 = show.Render(); show.Recycle(f19.Img);
            for (int i = 0; i < 18; i++) show.Advance(1 / 60.0);    // 2.2초
            var f22 = show.Render(); show.Recycle(f22.Img);
            int shown = 3; var all = new List<string>(f0.Talk.Select(t => t.Text));
            for (int i = 0; i < 60 * 30; i++) { show.Advance(1 / 60.0); var fx = show.Render(); show.Recycle(fx.Img); foreach (var t in fx.Talk) if (!all.Contains(t.Text)) { all.Add(t.Text); shown++; } }
            var fEnd = show.Render(); show.Recycle(fEnd.Img);
            string T(TalkLine[] t) => string.Join(" / ", t.Select(x => x.Text));
            bool dropped = !all.Any(t => t.Contains("예비 공기를 푼다")) && !all.Any(t => t.Contains("보조 발전기")) && !all.Any(t => t.Contains("바깥문"));   // 셋은 곧바로 · 밀린 아홉 가운데 가벼운 · 오래된 셋이 버려졌다
            var bad = all.Where(t => TextLint.Bad(t, new[] { "로봇 1", "로봇 2" }) != null).ToList();
            Check("말 창: 주컴퓨터 말 열둘이 한꺼번에 — 세 줄은 곧바로 · 밀린 줄은 여섯까지 (가벼운 것 · 오래된 것부터 버린다) · 한 줄은 화면 2초 넘게 (1.9초엔 그대로 · 2.2초엔 한 줄 밀림) · 12초 동안 새 말이 없으면 접는다 · 금지어 없음",
                f0.Talk.Length == 3 && T(f19.Talk) == T(f0.Talk) && f22.Talk.Length == 3 && f22.Talk[0].Text == f0.Talk[1].Text && shown == 9 && dropped && fEnd.Talk.Length == 0 && bad.Count == 0,
                $"첫 장 [{T(f0.Talk)}] · 1.9초 같음 {T(f19.Talk) == T(f0.Talk)} · 2.2초 [{T(f22.Talk)}] · 보인 줄 {shown} · 가벼운 둘 버림 {dropped} · 끝 {fEnd.Talk.Length}줄 · 금지어 {bad.Count} {string.Join(" ", bad.Take(2))}");
        }
    }
}
