using System;
using Godot;

namespace ShipSim.View;

/// <summary>그리기 함수를 꽂아 쓰는 빈 캔버스. 정적 레이어(한 번만 그림)와 동적 레이어(매 프레임)를 나누는 데 쓴다.</summary>
public partial class DrawLayer : Node2D
{
    public Action<CanvasItem>? Painter { get; set; }
    /// <summary>v17.7 다시 그린 횟수 (구워 둔 층이 다시 구울 때를 안다).</summary>
    public int Draws { get; private set; }

    public override void _Draw()
    {
        long t = FrameProbe.Now; // v17.7 층별 그리기 시간
        Draws++;
        Painter?.Invoke(this);
        if (t != 0) FrameProbe.Add(Name, t);
    }
}
