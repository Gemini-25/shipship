using System;
using System.Linq;
using Godot;
using ShipSim.Core;

namespace ShipSim.View;

/// <summary>
/// v16.24 사고 카드 "이야기" 탭 — 원인 · 전조 · 대응 · 쓴 방법 · 흔적 · 누가 기억하나 여섯 칸 (아이콘 · 색 · 줄 셋).
/// 기록에서 찾은 것만 쓰고, 없으면 흐린 칸에 "없었다"를 그대로. "사슬" 탭은 번진 길을 나무로 (HudCauses).
/// </summary>
public partial class Hud
{
    /// <summary>0 이야기 · 1 사슬.</summary>
    private int _chainTab;
    private IncidentStory? _story;
    private (int, int) _storyKey = (-1, -1);
    private float _storyAt = -99f;

    private static readonly Color[] StoryColors =
    {
        new("#ff9a5c"), new("#f5d547"), new("#7cc4ff"), new("#6ee7b7"), new("#c9a27a"), new("#b07cff"),
    };

    /// <summary>이야기 · 사슬 탭 (오른쪽 끝에서 왼쪽으로). 왼쪽 끝을 돌려준다.</summary>
    private float ChainTabs(float rightX, float y, Vector2 mouse)
    {
        string[] tabs = { "이야기", "사슬" };
        float bx = rightX;
        for (int i = tabs.Length - 1; i >= 0; i--)
        {
            float bw = Gfx.Width(Fonts.Bold, tabs[i], Ui.TextSmall) + 18f;
            bx -= bw;
            int t = i;
            Button(new Rect2(bx, y, bw, 26), tabs[i], _chainTab == i, mouse, () => { _chainTab = t; _chainScroll = 0; }, Ui.TextSmall);
            bx -= 4f;
        }
        return bx;
    }

    /// <summary>여섯 칸 (두 줄 × 세 칸 · 좁으면 세 줄 × 두 칸).</summary>
    private void DrawIncidentStory(CauseIncident inc, float x, float right, float y, float bottom)
    {
        var key = (inc.Root, _world.Causes.Version);
        if (_story == null || key != _storyKey || _time - _storyAt > 2f)
        {
            _story = IncidentStory.Of(_world, inc);
            _storyKey = key;
            _storyAt = _time;
        }
        var s = _story;
        int cols = right - x >= 540f ? 3 : 2;
        int rows = (6 + cols - 1) / cols;
        float gap = Ui.S2;
        float tw = (right - x - gap * (cols - 1)) / cols;
        float th = Mathf.Max(70f, (bottom - y - gap * (rows - 1)) / rows);
        int maxLines = th >= 140f ? 5 : th >= 110f ? 4 : 3;
        for (int i = 0; i < 6; i++)
        {
            int c = i % cols, r = i / cols;
            var rect = new Rect2(x + c * (tw + gap), y + r * (th + gap), tw, th);
            if (rect.End.Y > bottom + 1f) break;
            UiKit.Tile(this, rect, IncidentStory.FieldIcons[i], IncidentStory.Fields[i], s.Lines[i], StoryColors[i], s.Found[i], maxLines);
        }
    }
}
