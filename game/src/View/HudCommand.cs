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
}
