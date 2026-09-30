using System;
using Godot;

namespace ShipSim.View;

/// <summary>그리기 함수를 꽂아 쓰는 빈 캔버스. 정적 레이어(한 번만 그림)와 동적 레이어(매 프레임)를 나누는 데 쓴다.</summary>
public partial class DrawLayer : Node2D
{
    public Action<CanvasItem>? Painter { get; set; }

    public override void _Draw() => Painter?.Invoke(this);
}
