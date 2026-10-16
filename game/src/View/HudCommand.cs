using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

// v23 지휘 카드: 사고 지휘관의 열린 지휘 건 (누가 지휘하나 · 출동 · 현장 · 첫 도착 · 크기 · 마지막 판단).
//   아무것도 고르지 않았으면 오른쪽 위에 크게 (긴급 작업 목록 대신) · 고른 것이 있으면 그 카드 아래 남는 자리에 짧게.
public partial class Hud
{
    private const float CmdHead = 34f, CmdRow = 50f, CmdRowShort = 22f;

    private bool CommandOpen => _world.Commander.OpenCases.Any() || _world.Commander.Tier != CommandTier.Main && _world.Commander.Tier != CommandTier.Human || _world.Relays.Zones.Any(z => !z.Up);

    /// <summary>지휘 카드 (그렸으면 높이).</summary>
    private float DrawCommandCard(float y, float maxH, bool full, Vector2 mouse)
    {
        var cm = _world.Commander;
        var open = cm.OpenCases.OrderByDescending(c => c.Life).ThenBy(c => c.Id).Take(full ? 4 : 3).ToList();
        var cut = _world.Relays.Zones.Where(z => !z.Up || z.DroneId >= 0).ToList();
        if (open.Count == 0 && cm.Tier == CommandTier.Main && cut.Count == 0) return 0f;
        float rowH = full ? CmdRow : CmdRowShort;
        float cutH = cut.Count > 0 ? 18f : 0f;
        float h = CmdHead + cutH + open.Count * rowH + 10f;
        if (h > maxH) return 0f;
        float x0 = ColX, w = RightColumnWidth;
        var card = new Rect2(x0, y, w, h);
        Card(card);
        float x = x0 + 14, right = card.End.X - 14;
        var tierCol = cm.Tier switch { CommandTier.Main => Palette.Accent, CommandTier.Backup => Palette.Warning, CommandTier.RobotLeader => new Color("#ff9a3c"), _ => Palette.Danger };
        float pulse = open.Count > 0 ? 0.65f + 0.35f * Mathf.Sin(_time * 4f) : 1f;
        this.Box(new Rect2(card.Position + new Vector2(0, 8), new Vector2(3, h - 16)), tierCol.WithAlpha(pulse));
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y + 22), "지휘", Ui.TextLabel, Palette.Text);
        Gfx.TextRight(this, Fonts.Bold, new Vector2(right, y + 22), Fit($"{cm.Voice} · 지휘 건 {open.Count}", w - 80, Ui.TextSmall, Fonts.Bold), Ui.TextSmall, tierCol);
        float ly = y + CmdHead;
        if (cut.Count > 0)
        {
            // v23 구역 중계기: 끊긴 구역 (드론이 임시로 잇는 구역은 따로)
            string line = "무전 " + string.Join(" · ", cut.Select(z => z.Up ? $"{z.Name} 드론 중계" : $"{z.Name} 끊김({z.Why})"));
            Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 8), Fit(line, w - 28, Ui.TextTiny, Fonts.Body), Ui.TextTiny, cut.Any(z => !z.Up) ? Palette.Danger : Palette.Warning);
            ly += cutH;
        }
        foreach (var k in open)
        {
            var room = k.RoomId >= 0 && k.RoomId < _world.Ship.Rooms.Count ? _world.Ship.Rooms[k.RoomId] : null;
            var who = k.CrewId >= 0 ? _world.Crew.FirstOrDefault(c => c.Id == k.CrewId) : null;
            var col = k.Life ? Palette.Danger : k.Phase == CasePhase.Verify ? Palette.Good : Palette.Warning;
            string what = $"#{k.Id} {IncidentCommander.KindName(k.Kind)} — {(who != null ? who.Name + " · " : "")}{room?.Name ?? "?"}";
            float mins = (_world.Tick - k.Opened) / (float)SimTime.Minutes(1);
            string state = k.Phase == CasePhase.Verify ? "확인 중" : k.OnScene >= 0 ? $"현장 {(k.OnScene - k.Opened) / (float)SimTime.Minutes(1):0}분" : $"가는 중 {mins:0}분";
            Gfx.Text(this, Fonts.Bold, new Vector2(x, ly + 14), Fit(what, w - 120, Ui.TextSmall, Fonts.Bold), Ui.TextSmall, col);
            Gfx.TextRight(this, Fonts.Body, new Vector2(right, ly + 14), state, Ui.TextSmall, k.OnScene >= 0 ? Palette.Good : Palette.TextDim);
            if (full)
            {
                string size = k.Kind switch
                {
                    CaseKind.Fire => $"불 {k.Size:0}칸 (가장 컸을 때 {k.Peak:0}){(room != null && _world.Nozzles.Has(room) ? $" · 천장 {_world.Nozzles.State(room)}" : "")}",
                    CaseKind.Breach => "공기가 샌다",
                    _ => k.Life ? "위중" : "다침",
                };
                Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 30), Fit($"출동 {k.Bots.Count}대 (모두 {k.Sent}) · {size}{(k.Reinforced > 0 ? $" · 증원 {k.Reinforced}" : "")} · {k.Source}", w - 28, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim);
                if (k.Notes.Count > 0)
                    Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 44), Fit($"{SimTime.Clock(k.Notes[^1].Tick)} {k.Notes[^1].Text}", w - 28, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextMuted);
            }
            ly += rowH;
        }
        return h + UiLayout.Gap;
    }

    // ── v23 사고 탭 · 지휘: 지휘 순서 · 지휘 건 타임라인 (판단 기록 · 틀린 판단까지) · 구역 무전 · 천장 소화 · 눈의 그물 · 복기 ──
    private void DrawCommandPage(Vector2 mouse)
    {
        var w = _world;
        var cm = w.Commander;
        var card = PageCard(_dockBody.Size.Y);
        float x = card.Position.X + 18f, y = card.Position.Y + 30f, right = card.End.X - 18f, bottom = card.End.Y - 10f, wd = right - x;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), "지휘", Ui.TextTitle, Palette.Text);
        Gfx.TextRight(this, Fonts.Body, new Vector2(right, y), "주 컴퓨터 전권 자동 · 바꿀 수 없다", Ui.TextTiny, Palette.TextMuted);
        y += 24f;
        // 지휘 순서
        float cx = x;
        foreach (var t in new[] { CommandTier.Main, CommandTier.Backup, CommandTier.RobotLeader, CommandTier.Human })
        {
            bool now = cm.Tier == t;
            string label = IncidentCommander.TierName(t) + (now && t == CommandTier.RobotLeader ? $" ({cm.Voice})" : "");
            float tw = Gfx.Width(Fonts.Bold, label, Ui.TextSmall) + 16f;
            var r = new Rect2(cx, y - 14f, tw, 20f);
            Gfx.RoundRect(this, r, now ? Palette.Accent.WithAlpha(0.25f) : new Color(1, 1, 1, 0.04f), 6f, now ? Palette.Accent : new Color(1, 1, 1, 0.12f));
            Gfx.Text(this, Fonts.Bold, new Vector2(cx + 8f, y), label, Ui.TextSmall, now ? Palette.Text : Palette.TextDim);
            cx += tw + 4f;
            if (t != CommandTier.Human) { Gfx.Text(this, Fonts.Body, new Vector2(cx, y), "→", Ui.TextSmall, Palette.TextMuted); cx += 16f; }
        }
        y += 20f;
        Gfx.Text(this, Fonts.Body, new Vector2(x, y), UiKit.Fit(cm.Stats.Line(), wd, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim);
        y += 16f;
        Gfx.Text(this, Fonts.Body, new Vector2(x, y), UiKit.Fit(cm.Lessons.Line(), wd, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim);
        y += 12f;

        // 지휘 건: 열린 것 먼저 · 최근 닫힌 것
        var list = cm.Cases.Where(k => k.Open).OrderByDescending(k => k.Life).ThenBy(k => k.Id)
            .Concat(cm.Cases.Where(k => !k.Open).OrderByDescending(k => k.ClosedAt).Take(4)).ToList();
        SectionTitle(x, y + 12f, list.Count == 0 ? "지휘 건 — 아직 없다" : "지휘 건");
        y += 26f;
        foreach (var k in list)
        {
            if (y > bottom - 120f) break;
            var room = k.RoomId >= 0 && k.RoomId < w.Ship.Rooms.Count ? w.Ship.Rooms[k.RoomId] : null;
            var who = k.CrewId >= 0 ? w.Crew.FirstOrDefault(c => c.Id == k.CrewId) : null;
            var col = !k.Open ? Palette.TextDim : k.Life ? Palette.Danger : k.Phase == CasePhase.Verify ? Palette.Good : Palette.Warning;
            float mins = ((k.Open ? w.Tick : k.ClosedAt) - k.Opened) / (float)SimTime.Minutes(1);
            string head = $"#{k.Id} {IncidentCommander.KindName(k.Kind)} — {(who != null ? who.Name + " · " : "")}{room?.Name ?? "?"}";
            string state = k.Open ? (k.Phase == CasePhase.Verify ? "확인 중" : k.OnScene >= 0 ? "현장" : "가는 중") + $" · {mins:0}분" : $"{k.Outcome} · {mins:0}분";
            Gfx.Text(this, Fonts.Bold, new Vector2(x, y), UiKit.Fit(head, wd * 0.55f, Ui.TextSmall, Fonts.Bold), Ui.TextSmall, col);
            Gfx.TextRight(this, Fonts.Body, new Vector2(right, y), UiKit.Fit(state, wd * 0.43f, Ui.TextSmall, Fonts.Body), Ui.TextSmall, k.Open ? Palette.Text : Palette.TextMuted);
            y += 16f;
            string bots = string.Join(" · ", k.Served.Select(id => w.Robots.Robots.FirstOrDefault(r => r.Id == id)?.Name).Where(n => n != null).Take(5));
            Gfx.Text(this, Fonts.Body, new Vector2(x + 10f, y), UiKit.Fit($"{Ko.EuRo(k.Source)} 알았다 · 출동 {k.Sent}대{(k.Reinforced > 0 ? $" · 증원 {k.Reinforced}" : "")}{(bots != "" ? $" ({bots})" : "")}", wd - 10f, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim);
            y += 15f;
            int take = k.Open ? 5 : 2;
            foreach (var n in k.Notes.Skip(Math.Max(0, k.Notes.Count - take)))
            {
                if (y > bottom - 110f) break;
                bool wrong = n.Text.Contains("다시 붙었다") || n.Text.Contains("다시 샌다") || n.Text.Contains("빠짐") || n.Text.Contains("고장") || n.Text.Contains("바닥");
                Gfx.Text(this, Fonts.Body, new Vector2(x + 10f, y), UiKit.Fit($"{SimTime.Clock(n.Tick)}  {n.Text}", wd - 10f, Ui.TextTiny, Fonts.Body), Ui.TextTiny, wrong ? Palette.Warning : Palette.TextMuted);
                y += 14f;
            }
            y += 6f;
        }

        // 구역 무전 · 천장 소화 · 눈의 그물 · 복기
        y = Math.Max(y, bottom - 104f);
        Divider(x, right, y);
        y += 16f;
        var zones = w.Relays.Zones;
        string relay = zones.Count == 0 ? "구역 없음" : string.Join(" · ", zones.Select(z => z.Up ? (z.DroneId >= 0 ? $"{z.Name} 드론" : z.Name) : $"{z.Name} 끊김"));
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), "무전", Ui.TextSmall, Palette.Text);
        Gfx.Text(this, Fonts.Body, new Vector2(x + 54f, y), UiKit.Fit(relay, wd - 54f, Ui.TextTiny, Fonts.Body), Ui.TextTiny, zones.Any(z => !z.Up) ? Palette.Danger : Palette.TextDim);
        y += 18f;
        var live = w.Ship.LiveRooms.Where(w.Nozzles.Has).ToList();
        string noz = $"뿌리는 중 {live.Count(w.Nozzles.Spraying)} · 고장 {live.Count(r => r.NozzleBroken)} · 반 넘게 빔 {live.Count(r => !r.NozzleBroken && r.NozzleAgent < 0.5f)} · 방 {live.Count}";
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), "소화", Ui.TextSmall, Palette.Text);
        Gfx.Text(this, Fonts.Body, new Vector2(x + 54f, y), UiKit.Fit(noz, wd - 54f, Ui.TextTiny, Fonts.Body), Ui.TextTiny, live.Any(r => r.NozzleBroken) ? Palette.Warning : Palette.TextDim);
        y += 18f;
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), "눈", Ui.TextSmall, Palette.Text);
        Gfx.Text(this, Fonts.Body, new Vector2(x + 54f, y), UiKit.Fit(w.Eyes.Stats.Line(), wd - 54f, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim);
        y += 18f;
        string last = cm.Lessons.Recent.Count > 0 ? cm.Lessons.Recent[^1] : "아직 복기한 건이 없다";
        Gfx.Text(this, Fonts.Bold, new Vector2(x, y), "복기", Ui.TextSmall, Palette.Text);
        Gfx.Text(this, Fonts.Body, new Vector2(x + 54f, y), UiKit.Fit(last, wd - 54f, Ui.TextTiny, Fonts.Body), Ui.TextTiny, Palette.TextDim);
    }
}
