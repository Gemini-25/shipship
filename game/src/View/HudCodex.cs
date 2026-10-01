using System.Collections.Generic;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v12.2 설명서 (I): 설비·방을 누른 뒤 — 하는 일 · 원리 · 필요한 것 · 멈추면 · 관리 · 위험,
/// 그리고 지금 이 물건이 어느 망에서 받고 무엇을 먹이는지, 이 배에서 있었던 일.
/// </summary>
public partial class Hud
{
    public bool CodexMode { get; set; }
    public void ToggleCodex() => CodexMode = !CodexMode;

    private void CodexButton(float right, float y, Vector2 mouse) =>
        Button(new Rect2(right - 74, y, 74, 20), CodexMode ? "← 상태" : "? 설명서", CodexMode, mouse, ToggleCodex, 11);

    private void DrawCodex(string title, string sub, Color accent, CodexEntry entry, List<(string, string, Color)> links, List<string> past, float y, float maxHeight, Vector2 mouse)
    {
        float x0 = Screen.X - Margin - RightColumnWidth;
        float width = RightColumnWidth - 36;
        var sections = new (string label, string text, Color col)[]
        {
            ("하는 일", entry.What, Palette.Text), ("원리", entry.How, Palette.TextDim), ("필요한 것", entry.Needs, Palette.Accent),
            ("멈추면", entry.IfStopped, Palette.Warning), ("관리", entry.Care, Palette.Good), ("위험", entry.Danger, Palette.Danger),
        };
        var wrapped = sections.Select(s => (s.label, lines: WrapText(s.text, width, 12), s.col)).ToList();
        float height = 64 + wrapped.Sum(s => 20 + s.lines.Count * 17 + 6) + (links.Count > 0 ? 30 + links.Count * 19 : 0) + (past.Count > 0 ? 30 + past.Count * 17 : 0) + 10;
        height = Mathf.Min(height, maxHeight);
        var card = new Rect2(x0, y, RightColumnWidth, height);
        Card(card);
        float x = x0 + 18, right = card.End.X - 18;
        Gfx.RoundRect(this, new Rect2(x, y + 18, 16, 16), accent.WithAlpha(0.25f), 4, accent.WithAlpha(0.8f));
        Gfx.Text(this, Fonts.Bold, new Vector2(x + 26, y + 32), Fit(title, width - 110, 17, Fonts.Bold), 17, Palette.Text);
        Gfx.Text(this, Fonts.Body, new Vector2(x + 26, y + 50), $"설명서 · {sub}", 12, Palette.TextMuted);
        CodexButton(right, y + 16, mouse);
        float ly = y + 64;
        foreach (var (label, lines, col) in wrapped)
        {
            if (ly > card.End.Y - 30) break;
            SectionTitle(x, ly + 12, label);
            DrawLine(new Vector2(x, ly + 16), new Vector2(x + 3, ly + 16), col, 2f);
            ly += 18;
            foreach (var l in lines) { Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 13), l, 12, col == Palette.Text ? Palette.Text : col.Lerp(Palette.Text, 0.35f)); ly += 17; }
            ly += 6;
        }
        if (links.Count > 0 && ly < card.End.Y - 40)
        {
            Divider(x, right, ly + 4);
            SectionTitle(x, ly + 22, "지금 연결");
            ly += 28;
            foreach (var (label, value, col) in links)
            {
                Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 13), label, 12, Palette.TextDim);
                Gfx.TextRight(this, Fonts.Body, new Vector2(right, ly + 13), Fit(value, width - 80, 12, Fonts.Body), 12, col);
                ly += 19;
            }
        }
        if (past.Count > 0 && ly < card.End.Y - 30)
        {
            Divider(x, right, ly + 4);
            SectionTitle(x, ly + 22, "이 배에서 있었던 일");
            ly += 28;
            foreach (var p in past)
            {
                if (ly > card.End.Y - 14) break;
                Gfx.Text(this, Fonts.Body, new Vector2(x, ly + 12), Fit(p, width, 11, Fonts.Body), 11, Palette.TextDim);
                ly += 17;
            }
        }
    }

    private (string, string, Color) NetLine(string label, bool fed, bool needs = true) =>
        !needs ? (label, "쓰지 않음", Palette.TextMuted) : fed ? (label, "이어짐", Palette.Good) : (label, "끊김 — 받지 못한다", Palette.Danger);

    private List<(string, string, Color)> CodexLinks(Furniture f)
    {
        var room = f.Room;
        var links = new List<(string, string, Color)>
        {
            NetLine("전력 간선", room.PowerLinked),
            NetLine("급수관", room.WaterLinked, UtilityNet.NeedsWater(room) || Procedures.Plumbed(f.Type)),
            NetLine("환기 덕트", room.DuctLinked),
            NetLine("데이터선", room.DataLinked),
        };
        if (f.Machine is Machine m)
        {
            if (m.Spec.PowerDraw > 0f) links.Add(("설비 인입선", m.Feed >= 0.95f ? "정상" : $"{m.Feed * 100:0}%" + (m.Spliced ? " (임시 접속)" : ""), m.Feed < 0.5f ? Palette.Danger : m.Feed < 0.95f ? Palette.Warning : Palette.TextDim));
            if (Procedures.Plumbed(f.Type)) links.Add(("설비 배관", m.Line >= 0.95f ? "정상" : $"{m.Line * 100:0}%", m.Line < 0.5f ? Palette.Danger : m.Line < 0.95f ? Palette.Warning : Palette.TextDim));
            if (m.Spec.PowerDraw > 0f) links.Add(("쓰는 전기", $"{m.Spec.PowerDraw:0.#} kW · {PowerGrid.CircuitName(room.Circuit)}회로", Palette.TextDim));
        }
        var live = _world.Ship.LiveRooms.Where(r => !r.Detached).ToList();
        string Fed(System.Func<Room, bool> ok) { var list = live.Where(ok).ToList(); return $"{list.Count}곳 ({string.Join("·", list.Take(3).Select(r => r.Name))}{(list.Count > 3 ? " …" : "")})"; }
        switch (f.Type)
        {
            case FurnitureType.PowerPanel: links.Add(("먹이는 방", Fed(r => r.PowerLinked), Palette.Accent)); break;
            case FurnitureType.WaterRecycler: links.Add(("먹이는 방", Fed(r => r.WaterLinked && UtilityNet.NeedsWater(r)), Palette.Accent)); break;
            case FurnitureType.OxygenGenerator: links.Add(("공기를 보내는 방", Fed(r => r.DuctLinked), Palette.Accent)); break;
            case FurnitureType.CoolantPump: links.Add(("먹이는 것", $"원자로 냉각 (냉각 {_world.Power.CoolingCapacity:0} kW)", Palette.Accent)); break;
            case FurnitureType.ReactorCore: links.Add(("출력", _world.Power.ReactorOnline ? $"{_world.Power.ReactorOutput:0} kW" : "멈춤", _world.Power.ReactorOnline ? Palette.Good : Palette.Danger)); break;
        }
        return links;
    }

    private List<string> CodexPast(System.Func<CauseNode, bool> match) =>
        _world.Causes.Nodes.Where(match).Reverse().Take(6)
            .Select(n => $"{SimTime.Day(n.Tick)}일 {SimTime.Clock(n.Tick)} {n.Text}").ToList();

    /// <summary>설비 상세 대신 설명서를 그렸으면 true.</summary>
    private bool DrawFurnitureCodex(Furniture f, float y, float maxHeight, Vector2 mouse)
    {
        if (!CodexMode || Codex.Of(f.Type) is not CodexEntry e) return false;
        string name = f.Machine?.Name ?? f.Label;
        DrawCodex(f.Label, FurnitureTypes.Name(f.Type), Palette.Room(f.Room.Kind), e, CodexLinks(f),
            CodexPast(n => n.Kind != CauseKind.Recovery && n.Text.Contains(name)), y, maxHeight, mouse);
        return true;
    }

    private bool DrawRoomCodex(Room room, float y, float maxHeight, Vector2 mouse)
    {
        if (!CodexMode || Codex.Of(room.Kind) is not CodexEntry e) return false;
        var links = new List<(string, string, Color)>
        {
            NetLine("전력 간선", room.PowerLinked), NetLine("급수관", room.WaterLinked, UtilityNet.NeedsWater(room)), NetLine("환기 덕트", room.DuctLinked),
            ("설비", string.Join("·", room.Furniture.Where(x => x.Machine != null).Select(x => FurnitureTypes.Name(x.Type)).Distinct().Take(4)).DefaultIfEmptyText("없음"), Palette.TextDim),
        };
        DrawCodex(room.Name, "방", Palette.Room(room.Kind), e, links, CodexPast(n => n.RoomId == room.Id && n.Kind != CauseKind.Recovery), y, maxHeight, mouse);
        return true;
    }

    /// <summary>v12.3 분해도: 설비 몸통에서 부품마다 지시선 — 상태 막대와 한 줄 (고장·이상 징후·전선·관·열·교정).</summary>
    private float DrawParts(Machine m, PartSpec[] parts, float x, float right, float ly, Color accent)
    {
        Divider(x, right, ly + 8);
        SectionTitle(x, ly + 26, "부품 (분해도)");
        ly += 32;
        float top = ly + 4, bottom = ly + parts.Length * 20 - 4;
        // 몸통
        var body = new Rect2(x, top, 18, bottom - top);
        Gfx.RoundRect(this, body, accent.WithAlpha(0.18f), 4, accent.WithAlpha(0.6f));
        for (int i = 0; i < parts.Length; i++)
        {
            var st = MachineParts.State(m, parts[i], _world.Tick);
            float cy = ly + i * 20 + 10;
            var col = st.Broken ? Palette.Danger : st.Hidden ? Palette.Warning.WithAlpha(0.6f) : st.Health < 0.45f ? Palette.Warning : st.Health < 0.75f ? Palette.Text.WithAlpha(0.8f) : Palette.Good;
            // 지시선 (부품이 몸통에서 떨어져 나온 듯)
            DrawLine(new Vector2(body.End.X, cy), new Vector2(x + 30, cy), Palette.PanelBorder.Lightened(0.3f), 1f);
            DrawCircle(new Vector2(x + 32, cy), 3f, col, true, -1f, true);
            if (st.Broken || st.Health < 0.45f) DrawCircle(new Vector2(x + 32, cy), 5.5f + Mathf.Sin(_time * 5f + i), col.WithAlpha(0.18f), true, -1f, true);
            Gfx.Text(this, Fonts.Body, new Vector2(x + 40, cy + 4), parts[i].Name, 12, Palette.TextDim);
            Gfx.Bar(this, new Rect2(x + 124, cy - 3, 50, 5), st.Health, col);
            Gfx.TextRight(this, Fonts.Body, new Vector2(right, cy + 4), Fit(st.Text, right - x - 184, 11, Fonts.Body), 11, col);
        }
        return ly + parts.Length * 20;
    }
}
