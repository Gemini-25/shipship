using System;
using System.Collections.Generic;

namespace ShipSim.Core;

// v17.6 화면 배치 규칙 — 패널끼리 겹치지 않게: 세로로 쌓고, 자리가 모자라면 접는다.
// v20 UI 개편: 늘 보이는 것은 위 줄(시간 · 배속 · 자원) · 사고/경보 카드 · 고른 것 카드뿐. 나머지 창은 아래 탭(F1~F7)으로 열고 오른쪽 한 칸에 하나씩.
// 화면(Hud)과 헤드리스 시험(--hudtest)이 같은 규칙을 쓴다 (글꼴 없이 계산되는 것만). 배 그림은 위 줄 · 아래 탭 · 오른쪽 창을 피한 가장 넓은 자리에 맞춘다.

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
    /// <summary>우주 대재난 카드 (0 = 없음).</summary>
    public float CosmicH;
    /// <summary>보기 범례 (0 = 일반 보기).</summary>
    public float LegendW, LegendH;
    /// <summary>고른 것 카드 (사람 · 설비 · 방 · 로봇 — 0 = 고른 것 없음).</summary>
    public float SideH;
    /// <summary>오른쪽 창이 열려 있나 (아래 탭).</summary>
    public bool Dock;
    public float ShipW = 1200f, ShipH = 520f;
}

/// <summary>배치 결과: 패널마다 사각형 · 접혔나 · 배를 그릴 자리.</summary>
public sealed class HudPlan
{
    public UiRect TopBar, Status, Cosmic, Legend, Side, Dock, TabBar, ShipArea;
    public bool CosmicFolded, CosmicHidden, LegendHidden;
    public float StatusMaxW;
    /// <summary>왼쪽 위 더미의 자리 (그 패널이 지난 그림에 없었어도 이번에 그릴 자리).</summary>
    public float CosmicY, LegendY;
    /// <summary>사고 카드 · 시간 띠가 쌓이는 아래쪽 띠 (아래 탭 바로 위 · 고른 것 카드와 오른쪽 창 앞까지).</summary>
    public float AlertBottom, AlertRight;

    /// <summary>이름 붙은 패널 (빈 것은 뺀다).</summary>
    public IEnumerable<(string name, UiRect r)> Panels()
    {
        var all = new (string, UiRect)[]
        {
            ("시계", TopBar), ("상태 줄", Status), ("대재난", Cosmic), ("범례", Legend), ("고른 것", Side), ("오른쪽 창", Dock), ("아래 탭", TabBar),
        };
        foreach (var p in all) if (!p.Item2.Empty) yield return p;
    }

    /// <summary>배가 피하는 패널 (위 줄 · 아래 탭 · 오른쪽 창 — 고른 것 카드와 경보는 배 위에 뜬다).</summary>
    public IEnumerable<UiRect> Fixed()
    {
        foreach (var r in new[] { TopBar, Status, TabBar, Dock }) if (!r.Empty) yield return r;
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
    public const float Margin = 16f, Gap = 8f, RightW = 324f, TopBarH = 52f;
    public const float CosmicFoldH = 54f, CosmicW = 344f;
    /// <summary>아래 탭 막대: 탭 7개.</summary>
    public const int Tabs = 7;
    public const float TabH = 46f, TabW = 104f;
    public const float DockMinW = 520f, DockMaxW = 760f;

    // ── 주 컴퓨터 카드 속 줄 높이 (화면과 시험이 같은 값) ──
    public const float ComputerW = 318f, ComputerFoldH = 44f;
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

    /// <summary>오른쪽 창 너비 (화면의 4할 남짓 · 520~760).</summary>
    public static float DockWidth(float screenW) => Math.Clamp(screenW * 0.42f, DockMinW, DockMaxW);

    /// <summary>규칙: ① 위 줄은 시계 · 상태 ② 아래 가운데는 탭 막대 ③ 오른쪽 창은 위 줄과 탭 사이 오른쪽 끝
    /// ④ 고른 것 카드는 오른쪽 (창이 열려 있으면 창 왼쪽) ⑤ 왼쪽 위는 대재난 → 범례 (고른 것 카드와 겹치면 접거나 숨긴다)
    /// ⑥ 배는 위 줄 · 탭 · 창을 피한 사각형 중 배 모양이 가장 크게 들어가는 곳에 (고른 것 카드 · 경보는 배 위에 뜬다).</summary>
    public static HudPlan Plan(HudNeeds n)
    {
        var p = new HudPlan();
        float W = n.W, H = n.H;
        p.TopBar = new UiRect(Margin, Margin, Math.Min(n.TopBarW, W - Margin * 2f), TopBarH);
        float sx = p.TopBar.Right + 10f;
        p.StatusMaxW = Math.Max(0f, W - Margin - sx);
        p.Status = p.StatusMaxW >= 60f ? new UiRect(sx, Margin, p.StatusMaxW, TopBarH) : default;
        float top = Margin + TopBarH + Gap;
        float tabW = Math.Min(Tabs * TabW + 16f, W - Margin * 2f);
        p.TabBar = new UiRect((W - tabW) * 0.5f, H - Margin - TabH, tabW, TabH);
        float bottom = p.TabBar.Y - Gap;
        if (n.Dock)
        {
            float dw = DockWidth(W);
            p.Dock = new UiRect(W - Margin - dw, top, dw, Math.Max(80f, bottom - top));
        }
        float sideX = (n.Dock ? p.Dock.X - Gap : W - Margin) - RightW;
        if (n.SideH > 0f) p.Side = new UiRect(sideX, top, RightW, Math.Min(n.SideH, bottom - top));
        p.AlertBottom = bottom;
        p.AlertRight = (n.SideH > 0f ? p.Side.X : n.Dock ? p.Dock.X : W - Margin) - Gap;
        // ⑤ 왼쪽 위 더미
        float y = top;
        float stackLimit = bottom;
        bool Free(UiRect r) => r.Bottom <= stackLimit && !r.Intersects(p.Side) && !r.Intersects(p.Dock) && r.Right <= W - Margin;
        p.CosmicY = y;
        if (n.CosmicH > 0f)
        {
            var full = new UiRect(Margin, y, CosmicW, n.CosmicH);
            var fold = new UiRect(Margin, y, CosmicW, CosmicFoldH);
            if (Free(full)) { p.Cosmic = full; y = full.Bottom + Gap; }
            else if (Free(fold)) { p.Cosmic = fold; p.CosmicFolded = true; y = fold.Bottom + Gap; }
            else p.CosmicHidden = true;
        }
        p.LegendY = y;
        if (n.LegendH > 0f)
        {
            var below = new UiRect(Margin, y, n.LegendW, n.LegendH);
            if (Free(below)) p.Legend = below;
            else p.LegendHidden = true;
        }
        // ⑥ 배 자리
        p.ShipArea = FreeArea(W, H, new List<UiRect>(p.Fixed()), n.ShipW / Math.Max(1f, n.ShipH), Margin);
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
