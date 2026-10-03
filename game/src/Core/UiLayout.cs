using System;
using System.Collections.Generic;

namespace ShipSim.Core;

// v17.6 화면 배치 규칙 — 패널끼리 겹치지 않게: 세로로 쌓고, 자리가 모자라면 접는다.
// 화면(Hud)과 헤드리스 시험(--hudtest)이 같은 규칙을 쓴다 (글꼴 없이 계산되는 것만). 배 그림은 패널이 비운 가장 넓은 자리에 맞춘다.

/// <summary>화면 사각형 (왼쪽 위 · 너비 · 높이).</summary>
public readonly record struct UiRect(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;
    public bool Empty => W <= 0f || H <= 0f;
    /// <summary>맞닿기만 한 것은 겹침이 아니다.</summary>
    public bool Intersects(UiRect o) => !Empty && !o.Empty && X < o.Right - 0.5f && o.X < Right - 0.5f && Y < o.Bottom - 0.5f && o.Y < Bottom - 0.5f;
    public bool Inside(float sw, float sh) => X >= -0.5f && Y >= -0.5f && Right <= sw + 0.5f && Bottom <= sh + 0.5f;
    public override string ToString() => $"({X:0},{Y:0} {W:0}×{H:0})";
}

/// <summary>화면이 그리려는 패널들의 크기 (글꼴로 잰 값 · 시험은 어림값).</summary>
public sealed class HudNeeds
{
    public float W = 1600f, H = 900f;
    public float TopBarW = 560f;
    /// <summary>함선 지표 카드 (0 = 안 보임).</summary>
    public float ProfileW, ProfileH;
    /// <summary>우주 대재난 카드 (0 = 없음).</summary>
    public float CosmicH;
    /// <summary>보기 범례 (0 = 일반 보기).</summary>
    public float LegendW, LegendH;
    public bool Computer = true;
    /// <summary>주 컴퓨터 카드를 다 펼쳤을 때 높이.</summary>
    public float ComputerH = 260f;
    public bool ComputerUserFolded;
    public float LogH = 190f;
    public float MinimapW, MinimapH;
    public bool Voyage;
    /// <summary>위쪽 줄(보기 · 사고 도구)이 보이나.</summary>
    public bool Tools = true;
    public float ShipW = 1200f, ShipH = 520f;
}

/// <summary>배치 결과: 패널마다 사각형 · 접혔나 · 배를 그릴 자리.</summary>
public sealed class HudPlan
{
    public UiRect TopBar, Status, Tools, Profile, Cosmic, Legend, Computer, Log, Minimap, Voyage, Right, ShipArea;
    public bool ComputerFolded, ComputerHidden;
    public float StatusMaxW, ToolsMaxW;
    /// <summary>왼쪽 위 더미의 자리 (그 패널이 지난 그림에 없었어도 이번에 그릴 자리).</summary>
    public float ProfileY, CosmicY, LegendY;

    /// <summary>이름 붙은 패널 (빈 것은 뺀다).</summary>
    public IEnumerable<(string name, UiRect r)> Panels()
    {
        var all = new (string, UiRect)[]
        {
            ("시계", TopBar), ("상태 줄", Status), ("보기 · 사고", Tools), ("함선 지표", Profile), ("대재난", Cosmic), ("범례", Legend),
            ("주 컴퓨터", Computer), ("항해 기록", Log), ("지도", Minimap), ("항로", Voyage), ("오른쪽 칸", Right),
        };
        foreach (var p in all) if (!p.Item2.Empty) yield return p;
    }

    /// <summary>겹치는 패널 쌍 (없으면 빈 목록).</summary>
    public List<string> Overlaps()
    {
        var list = new List<(string n, UiRect r)>(Panels());
        var bad = new List<string>();
        for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
                if (list[i].r.Intersects(list[j].r)) bad.Add($"{list[i].n}{list[i].r}×{list[j].n}{list[j].r}");
        return bad;
    }
}

public static class UiLayout
{
    public const float Margin = 16f, Gap = 8f, RightW = 324f, TopBarH = 52f, ToolsH = 40f;
    public const float ComputerW = 318f, ComputerFoldH = 44f, LogW = 470f, LogFullH = 190f, VoyageH = 40f, CosmicW = 344f;

    // ── 주 컴퓨터 카드 속 줄 높이 (화면과 시험이 같은 값) ──
    public const float CompHead = 44f, CompNow = 34f, CompLoad = 20f, CompIconRow = 19f, CompIconPad = 12f, CompBookLine = 18f, CompBrain = 74f, CompButtons = 34f, CompProposal = 118f;
    public const float CompIconStep = 18f;

    /// <summary>한 줄에 들어가는 설비 아이콘 수.</summary>
    public static int IconsPerRow(float width) => Math.Max(1, (int)((width - 28f - 16f) / CompIconStep) + 1);

    /// <summary>아이콘 줄 수 (최대 3줄 — 넘치면 우선순위가 낮은 것을 뺀다).</summary>
    public static int IconRows(int modules, float width) => Math.Clamp((modules + IconsPerRow(width) - 1) / IconsPerRow(width), 1, 3);

    /// <summary>주 컴퓨터 카드 높이: 머리 · 지금 · 부하 · 아이콘 줄 · 오늘 조치 · 두뇌 · 단추 · 제안.</summary>
    public static float ComputerHeight(int modules, int proposals, bool folded, float width = ComputerW) =>
        folded ? ComputerFoldH
            : CompHead + CompNow + CompLoad + IconRows(modules, width) * CompIconRow + CompIconPad + CompBookLine + CompBrain + CompButtons + proposals * CompProposal + 6f;

    /// <summary>규칙: ① 위 두 줄은 오른쪽 칸 앞에서 멈춘다(넘치면 접힘 칩) ② 왼쪽 위는 지표 → 대재난 → 범례 순으로 쌓는다
    /// ③ 기록은 왼쪽 아래 · 지도와 항로는 그 옆에 쌓는다 ④ 주 컴퓨터는 기록 바로 위 — 왼쪽 위 더미와 겹치면 접고, 접어도 겹치면 숨긴다
    /// ⑤ 배는 남은 자리 중 배 모양이 가장 크게 들어가는 사각형에.</summary>
    public static HudPlan Plan(HudNeeds n)
    {
        var p = new HudPlan();
        float W = n.W, H = n.H;
        p.Right = new UiRect(W - Margin - RightW, Margin, RightW, H - Margin * 2f);
        float limit = p.Right.X - 12f;
        p.TopBar = new UiRect(Margin, Margin, Math.Min(n.TopBarW, limit - Margin), TopBarH);
        float sx = p.TopBar.Right + 10f;
        p.StatusMaxW = Math.Max(0f, limit - sx);
        p.Status = p.StatusMaxW >= 60f ? new UiRect(sx, Margin, p.StatusMaxW, TopBarH) : default;
        float ty = Margin + TopBarH + Gap;
        p.ToolsMaxW = Math.Max(0f, limit - Margin);
        p.Tools = n.Tools ? new UiRect(Margin, ty, p.ToolsMaxW, ToolsH) : default;
        // ② 왼쪽 위 더미
        float y = n.Tools ? ty + ToolsH + Gap : ty;
        p.ProfileY = y;
        if (n.ProfileH > 0f) { p.Profile = new UiRect(Margin, y, Math.Min(n.ProfileW, limit - Margin), n.ProfileH); y = p.Profile.Bottom + Gap; }
        p.CosmicY = y;
        if (n.CosmicH > 0f) { p.Cosmic = new UiRect(Margin, y, CosmicW, n.CosmicH); y = p.Cosmic.Bottom + Gap; }
        p.LegendY = y;
        if (n.LegendH > 0f) { p.Legend = new UiRect(Margin, y, n.LegendW, n.LegendH); y = p.Legend.Bottom + Gap; }
        float stackBottom = y - Gap;
        // ③ 아래
        p.Log = new UiRect(Margin, H - Margin - n.LogH, LogW, n.LogH);
        if (n.MinimapW > 0f)
        {
            p.Minimap = new UiRect(Margin + LogW + 10f, H - Margin - LogFullH, n.MinimapW, n.MinimapH);
            if (p.Minimap.Right > limit) p.Minimap = default; // 좁은 화면: 지도는 접는다 (G로 다시)
            else if (n.Voyage) p.Voyage = new UiRect(p.Minimap.X, p.Minimap.Y - VoyageH - 6f, p.Minimap.W, VoyageH);
        }
        // ④ 주 컴퓨터: 기록 바로 위
        if (n.Computer)
        {
            float bottom = p.Log.Y - 10f;
            float room = bottom - Math.Max(stackBottom + Gap, ty + (n.Tools ? ToolsH + Gap : 0f));
            float full = n.ComputerUserFolded ? ComputerFoldH : n.ComputerH;
            if (full <= room) p.Computer = new UiRect(Margin, bottom - full, ComputerW, full);
            else if (ComputerFoldH <= room) { p.Computer = new UiRect(Margin, bottom - ComputerFoldH, ComputerW, ComputerFoldH); p.ComputerFolded = true; }
            else p.ComputerHidden = true;
            p.ComputerFolded |= n.ComputerUserFolded;
        }
        // ⑤ 배 자리
        var taken = new List<UiRect>();
        foreach (var (_, r) in p.Panels()) taken.Add(r);
        p.ShipArea = FreeArea(W, H, taken, n.ShipW / Math.Max(1f, n.ShipH), Margin);
        return p;
    }

    /// <summary>패널을 피한 사각형 중 aspect(너비/높이) 모양이 가장 크게 들어가는 것. 후보 모서리는 패널 가장자리들.</summary>
    public static UiRect FreeArea(float W, float H, IReadOnlyList<UiRect> taken, float aspect, float pad)
    {
        var xs = new List<float> { pad };
        var xe = new List<float> { W - pad };
        var ys = new List<float> { pad };
        var ye = new List<float> { H - pad };
        foreach (var r in taken)
        {
            xs.Add(r.Right + pad); xe.Add(r.X - pad);
            ys.Add(r.Bottom + pad); ye.Add(r.Y - pad);
        }
        xs.Sort(); xe.Sort(); ys.Sort(); ye.Sort();
        UiRect best = new(pad, pad, Math.Max(1f, W * 0.3f), Math.Max(1f, H * 0.3f));
        float bestScore = -1f;
        foreach (float x0 in xs)
            foreach (float x1 in xe)
            {
                if (x1 - x0 < 40f) continue;
                foreach (float y0 in ys)
                    foreach (float y1 in ye)
                    {
                        if (y1 - y0 < 40f) continue;
                        var c = new UiRect(x0, y0, x1 - x0, y1 - y0);
                        float score = Math.Min(c.W / aspect, c.H); // 배가 들어갈 높이 (모양을 지킨 채)
                        if (score <= bestScore) continue;
                        bool hit = false;
                        foreach (var r in taken) if (c.Intersects(r)) { hit = true; break; }
                        if (hit) continue;
                        bestScore = score;
                        best = c;
                    }
            }
        return best;
    }

    /// <summary>상태 칩 접기: 너비 목록과 자리 → 보일 칩 수 (나머지는 "외 n" 칩 하나로).</summary>
    public static int FitChips(IReadOnlyList<float> widths, float avail, float pad, float gap, float moreW)
    {
        float w = pad * 2f;
        for (int i = 0; i < widths.Count; i++)
        {
            float next = w + widths[i] + gap;
            bool last = i == widths.Count - 1;
            if (next - gap + (last ? 0f : moreW + gap) > avail) return Math.Max(1, i);
            w = next;
        }
        return widths.Count;
    }
}
