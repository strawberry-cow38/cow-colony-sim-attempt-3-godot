using Godot;

namespace CowColonySim.Render;

public sealed partial class OrbitCamera : Camera3D
{
    [Export] public Vector3 Target { get; set; } = new(0, 4, 0);
    [Export] public float Radius { get; set; } = 24.0f;
    [Export] public float MinRadius { get; set; } = 3.0f;
    [Export] public float MaxRadius { get; set; } = 200.0f;
    [Export] public float YawDegrees { get; set; } = 45.0f;
    [Export] public float PitchDegrees { get; set; } = 35.0f;
    [Export] public float DragSensitivity { get; set; } = 0.35f;
    [Export] public float ZoomStep { get; set; } = 0.9f;

    private bool _dragging;

    public override void _Ready() => UpdateTransform();

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right) _dragging = mb.Pressed;
            if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp)   { Radius = Mathf.Clamp(Radius * ZoomStep, MinRadius, MaxRadius); UpdateTransform(); }
            if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown) { Radius = Mathf.Clamp(Radius / ZoomStep, MinRadius, MaxRadius); UpdateTransform(); }
        }
        else if (ev is InputEventMouseMotion mm && _dragging)
        {
            YawDegrees   -= mm.Relative.X * DragSensitivity;
            PitchDegrees = Mathf.Clamp(PitchDegrees - mm.Relative.Y * DragSensitivity, 5.0f, 85.0f);
            UpdateTransform();
        }
    }

    private void UpdateTransform()
    {
        var yaw = Mathf.DegToRad(YawDegrees);
        var pitch = Mathf.DegToRad(PitchDegrees);
        var cosP = Mathf.Cos(pitch);
        var offset = new Vector3(
            Radius * cosP * Mathf.Sin(yaw),
            Radius * Mathf.Sin(pitch),
            Radius * cosP * Mathf.Cos(yaw)
        );
        Position = Target + offset;
        LookAt(Target, Vector3.Up);
    }
}
