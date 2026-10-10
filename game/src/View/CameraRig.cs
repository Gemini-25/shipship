using System;
using Godot;

namespace ShipSim.View;

/// <summary>관찰자 카메라. 휠 확대(커서 기준), 우클릭/휠클릭 드래그 이동, WASD 이동, 승무원 따라가기.</summary>
public partial class CameraRig : Camera2D
{
    public const float MinZoom = 0.15f; // v10.7: 30인용 배(191칸)도 한 화면에
    public const float MaxZoom = 3f;
    private const float KeyPanSpeed = 900f;

    private bool _dragging;
    private float _shake;
    private float _shakeTime;

    /// <summary>화면을 흔든다 (운석 충돌 같은 큰 사고). amount는 픽셀.</summary>
    public void Shake(float amount) => _shake = Mathf.Max(_shake, amount);

    /// <summary>값이 있으면 그 위치를 부드럽게 따라간다.</summary>
    public Func<Vector2?>? FollowTarget { get; set; }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton mb when mb.ButtonIndex is MouseButton.Right or MouseButton.Middle:
                _dragging = mb.Pressed;
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } up:
                ZoomAt(1.15f, up.Position);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } down:
                ZoomAt(1f / 1.15f, down.Position);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseMotion motion when _dragging:
                Position -= motion.Relative / Zoom;
                FollowTarget = null;
                break;
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_dragging && !Input.IsMouseButtonPressed(MouseButton.Right) && !Input.IsMouseButtonPressed(MouseButton.Middle))
            _dragging = false;

        var move = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) move.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) move.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) move.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) move.X += 1;
        if (move != Vector2.Zero)
        {
            Position += move.Normalized() * KeyPanSpeed * dt / Zoom.X;
            FollowTarget = null;
        }

        if (FollowTarget?.Invoke() is Vector2 target)
            Position = Position.Lerp(target, 1f - Mathf.Exp(-dt * 6f));

        _shakeTime += dt;
        if (_shake > 0.2f)
        {
            Offset = new Vector2(Mathf.Sin(_shakeTime * 71f), Mathf.Cos(_shakeTime * 53f)) * _shake / Zoom.X;
            _shake *= Mathf.Exp(-dt * 5f);
        }
        else if (Offset != Vector2.Zero) Offset = Vector2.Zero;
    }

    public void ZoomAt(float factor, Vector2 screenPos)
    {
        var half = GetViewportRect().Size * 0.5f;
        float oldZoom = Zoom.X;
        float newZoom = Mathf.Clamp(oldZoom * factor, MinZoom, MaxZoom);
        var before = Position + (screenPos - half) / oldZoom;
        Zoom = new Vector2(newZoom, newZoom);
        var after = Position + (screenPos - half) / newZoom;
        if (FollowTarget == null) Position += before - after;
    }

    /// <summary>world 영역이 화면의 screenArea 안에 꽉 차게 맞춘다.</summary>
    public void FitTo(Rect2 world, Rect2 screenArea)
    {
        float z = Mathf.Min(screenArea.Size.X / world.Size.X, screenArea.Size.Y / world.Size.Y);
        z = Mathf.Clamp(z, MinZoom, MaxZoom);
        Zoom = new Vector2(z, z);
        var half = GetViewportRect().Size * 0.5f;
        Position = world.GetCenter() - (screenArea.GetCenter() - half) / z;
    }
}
